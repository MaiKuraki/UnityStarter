package audio_volume_normalizer

import (
	"bufio"
	"context"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"io/fs"
	"math"
	"os"
	"os/signal"
	"path/filepath"
	"regexp"
	"runtime"
	"strconv"
	"strings"
	"sync"
	"sync/atomic"
	"syscall"
	"time"

	"cyclonegames.tools/scripts/internal/logging"
	"cyclonegames.tools/scripts/internal/safefs"
	"cyclonegames.tools/scripts/internal/term"
	"cyclonegames.tools/scripts/internal/toolenv"
	"cyclonegames.tools/scripts/internal/toolkit"
)

const (
	MAX_SAMPLERATE            = 48000
	FILENAME_SUFFIX           = "_normalized"
	LOUDNESS_TOLERANCE        = 0.5
	PEAK_TOLERANCE            = 0.5
	SHORT_AUDIO_THRESHOLD_SEC = 3.0
	defaultFFmpegTimeout      = 10 * time.Minute
)

// Options holds the resolved executable and every per-run setting. It is built
// once in Run and passed down by value, so consecutive in-process runs never
// share mutable configuration and every worker observes the same snapshot.
type Options struct {
	FFmpegPath    string
	FFmpegTimeout time.Duration
	Format        outputFormat
	Mono          bool
	Workers       int
}

type audioCategory struct {
	name       string
	targetLUFS float64
	targetTP   float64
	targetPeak float64
}

var (
	categoryMusic   = audioCategory{name: "Music", targetLUFS: -14.0, targetTP: -1.0, targetPeak: -1.0}
	categoryVoice   = audioCategory{name: "Voice", targetLUFS: -16.0, targetTP: -1.5, targetPeak: -1.0}
	categorySFX     = audioCategory{name: "SFX", targetLUFS: -14.0, targetTP: -1.0, targetPeak: -1.0}
	categoryAmbient = audioCategory{name: "Ambient", targetLUFS: -20.0, targetTP: -1.5, targetPeak: -3.0}
	categoryDefault = audioCategory{name: "Default", targetLUFS: -16.0, targetTP: -1.5, targetPeak: -1.0}
)

// categoryKeywords maps a parent folder name to its loudness target.
var categoryKeywords = map[string]audioCategory{
	"music":   categoryMusic,
	"bgm":     categoryMusic,
	"voice":   categoryVoice,
	"dialog":  categoryVoice,
	"vo":      categoryVoice,
	"sfx":     categorySFX,
	"se":      categorySFX,
	"sound":   categorySFX,
	"ambient": categoryAmbient,
	"env":     categoryAmbient,
}

var audioExtensions = map[string]bool{
	".mp3": true, ".wav": true, ".flac": true, ".m4a": true, ".aac": true,
	".ogg": true, ".wma": true, ".opus": true,
}

var (
	sampleRateRegex = regexp.MustCompile(`Stream\s+#\d+:\d+.*?(\d+)\s+Hz`)
	durationRegex   = regexp.MustCompile(`Duration:\s+(\d+):(\d+):(\d+)\.(\d+)`)
	maxVolRegex     = regexp.MustCompile(`max_volume:\s+([\-\d.]+)\s+dB`)
)

var ErrAlreadyNormalized = errors.New("file is already within the target loudness range")

type outputFormat struct {
	name       string
	ext        string
	ffmpegArgs []string
}

var (
	formatWAV = outputFormat{
		name:       "WAV (Lossless PCM 16-bit)",
		ext:        ".wav",
		ffmpegArgs: []string{"-c:a", "pcm_s16le"},
	}
	formatOGG = outputFormat{
		name:       "OGG (Vorbis VBR Quality 6)",
		ext:        ".ogg",
		ffmpegArgs: []string{"-c:a", "libvorbis", "-q:a", "6"},
	}
)

type LoudnormInfo struct {
	InputI       string `json:"input_i"`
	InputTP      string `json:"input_tp"`
	InputLRA     string `json:"input_lra"`
	InputThresh  string `json:"input_thresh"`
	TargetOffset string `json:"target_offset"`
}

type job struct {
	inputPath  string
	outputPath string
}

type result struct {
	path string
	err  error
}

// displayIntroAndConfirm renders the guided menu and returns the chosen output
// format together with the user's consent. A closed input stream cancels the run
// instead of silently applying defaults.
func displayIntroAndConfirm() (outputFormat, bool) {
	scanner := bufio.NewScanner(os.Stdin)
	format := formatWAV

	fmt.Println("--- LoudNorm: Game Audio Normalizer ---")
	fmt.Println("\n[ About This Tool ]")
	fmt.Println("This tool normalizes audio files for optimal game audio integration.")
	fmt.Println("It automatically detects audio category from folder names and applies")
	fmt.Println("appropriate normalization strategies.")

	fmt.Println("\n[ Normalization Strategies ]")
	fmt.Println("  Long audio (>= 3s): Two-pass LUFS loudness normalization (linear mode)")
	fmt.Println("  Short audio (< 3s): Peak normalization (LUFS is unreliable for short SFX)")

	fmt.Println("\n[ Category Targets (auto-detected from folder name) ]")
	fmt.Printf("  Music/BGM:     %5.1f LUFS | Peak: %.1f dBTP\n", categoryMusic.targetLUFS, categoryMusic.targetPeak)
	fmt.Printf("  Voice/Dialog:  %5.1f LUFS | Peak: %.1f dBTP\n", categoryVoice.targetLUFS, categoryVoice.targetPeak)
	fmt.Printf("  SFX/SE:        %5.1f LUFS | Peak: %.1f dBTP\n", categorySFX.targetLUFS, categorySFX.targetPeak)
	fmt.Printf("  Ambient/Env:   %5.1f LUFS | Peak: %.1f dBTP\n", categoryAmbient.targetLUFS, categoryAmbient.targetPeak)
	fmt.Printf("  Default:       %5.1f LUFS | Peak: %.1f dBTP\n", categoryDefault.targetLUFS, categoryDefault.targetPeak)

	fmt.Println("\n[ Output Format ]")
	fmt.Println("  1. WAV  - Lossless (recommended: let the engine handle final compression)")
	fmt.Println("  2. OGG  - Vorbis VBR (smaller files, use when disk/memory matters)")
	fmt.Printf("\nSelect output format (1 or 2) [default: 1]: ")
	if !scanner.Scan() {
		return format, false
	}
	if strings.TrimSpace(scanner.Text()) == "2" {
		format = formatOGG
	}
	fmt.Printf("  -> Selected: %s\n", format.name)

	fmt.Println("\n[ How It Works ]")
	fmt.Println("1. It will recursively scan the current directory for audio files.")
	fmt.Printf("2. For each audio file, it will create a new '%s' file with the '%s' suffix.\n", format.ext, FILENAME_SUFFIX)
	if format.ext == ".wav" {
		fmt.Println("3. Output is lossless WAV to avoid double compression when the engine re-encodes on import.")
	} else {
		fmt.Println("3. Output is OGG Vorbis - smaller files, but may double-compress if the engine re-encodes.")
	}
	fmt.Printf("4. Existing '%s%s' files will be overwritten.\n", FILENAME_SUFFIX, format.ext)
	fmt.Println("5. FFmpeg is resolved from the FFMPEG environment variable or the process PATH.")

	fmt.Printf("\nDo you want to proceed? (Y/N): ")
	if !scanner.Scan() {
		return format, false
	}
	response := strings.TrimSpace(scanner.Text())
	return format, strings.ToLower(response) == "y"
}

// Run executes the audio normalizer and returns its process exit code.
func Run(args []string) int {
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()
	logging.Command("audio_volume_normalizer")

	flags := flag.NewFlagSet("audio_volume_normalizer", flag.ContinueOnError)
	flags.SetOutput(os.Stderr)
	var ciMode bool
	var inputRoot string
	var formatKey string
	var jobsFlag int
	var mono bool
	var timeout time.Duration
	flags.BoolVar(&ciMode, "ci", false, "Non-interactive mode")
	flags.StringVar(&inputRoot, "input", "", "Root directory to scan (CI mode, required)")
	flags.StringVar(&formatKey, "format", "wav", "Output format: wav or ogg")
	flags.IntVar(&jobsFlag, "jobs", 0, "Parallel worker count (default: CPU count, clamped 1..64)")
	flags.BoolVar(&mono, "mono", false, "Fold every output to a single channel")
	flags.DurationVar(&timeout, "ffmpeg-timeout", defaultFFmpegTimeout, "Per-invocation FFmpeg timeout (for example 5m or 30m)")
	if err := flags.Parse(args); err != nil {
		if errors.Is(err, flag.ErrHelp) {
			return toolkit.ExitSuccess
		}
		return toolkit.ExitUsage
	}
	if timeout <= 0 {
		logging.Error("--ffmpeg-timeout must be a positive duration")
		return toolkit.ExitUsage
	}

	resolution, err := toolenv.Resolve("ffmpeg")
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		if !ciMode {
			toolkit.WaitForExit()
		}
		return toolkit.ExitFailure
	}

	workerCount := jobsFlag
	if workerCount <= 0 {
		workerCount = runtime.NumCPU()
	}
	if workerCount > 64 {
		workerCount = 64
	}
	if workerCount < 1 {
		workerCount = 1
	}

	opts := Options{
		FFmpegPath:    resolution.Path,
		FFmpegTimeout: timeout,
		Mono:          mono,
		Workers:       workerCount,
	}

	if ciMode {
		return runCi(ctx, opts, inputRoot, formatKey)
	}
	return runInteractiveFlow(ctx, opts)
}

// runInteractiveFlow is the guided interactive normalization.
func runInteractiveFlow(ctx context.Context, opts Options) int {
	format, confirmed := displayIntroAndConfirm()
	if !confirmed {
		fmt.Println("Operation cancelled by user.")
		toolkit.WaitForExit()
		return toolkit.ExitFailure
	}
	opts.Format = format

	fmt.Println("\nUser confirmed. Starting process...")

	rootDir, err := os.Getwd()
	if err != nil {
		logging.Error("cannot get working directory", "error", err)
		toolkit.WaitForExit()
		return toolkit.ExitFailure
	}
	fmt.Printf("Scanning for audio files in [%s] and its subdirectories...\n", rootDir)

	return processDirectory(ctx, opts, rootDir, true)
}

// runCi executes the non-interactive normalization path.
func runCi(ctx context.Context, opts Options, inputRoot, formatKey string) int {
	switch strings.ToLower(formatKey) {
	case "wav":
		opts.Format = formatWAV
	case "ogg":
		opts.Format = formatOGG
	default:
		logging.Error("unknown format", "format", formatKey)
		return toolkit.ExitUsage
	}
	rootDir := strings.TrimSpace(inputRoot)
	if rootDir == "" {
		logging.Error("--ci requires --input")
		return toolkit.ExitUsage
	}
	if info, err := os.Stat(rootDir); err != nil || !info.IsDir() {
		logging.Error("input directory not found", "path", rootDir)
		return toolkit.ExitFailure
	}
	return processDirectory(ctx, opts, rootDir, false)
}

// processDirectory scans rootDir, normalizes every audio file in parallel, and prints a summary.
func processDirectory(ctx context.Context, opts Options, rootDir string, waitAfter bool) int {
	jobs, err := collectAudioJobs(rootDir, opts)
	if err != nil {
		logging.Error("audio scan failed", "error", err)
		if waitAfter {
			toolkit.WaitForExit()
		}
		return toolkit.ExitFailure
	}
	if len(jobs) == 0 {
		fmt.Println("No audio files found to process.")
		if waitAfter {
			toolkit.WaitForExit()
		}
		return toolkit.ExitFailure
	}
	totalFiles := int32(len(jobs))
	fmt.Printf("Found %d audio files to process.\n\n", totalFiles)
	logging.Info("normalization started", "root", rootDir, "files", totalFiles, "workers", opts.Workers, "format", opts.Format.ext)

	progressEnabled := term.IsTerminal(os.Stdout.Fd())

	var wg sync.WaitGroup
	jobChannel := make(chan job)
	results := make(chan result)
	var processedFiles int32

	for i := 0; i < opts.Workers; i++ {
		wg.Add(1)
		go worker(ctx, opts, i+1, &wg, jobChannel, results)
	}

	go func() {
		defer close(jobChannel)
		for _, item := range jobs {
			select {
			case jobChannel <- item:
			case <-ctx.Done():
				return
			}
		}
	}()

	go func() {
		wg.Wait()
		close(results)
	}()

	var successfulFiles []string
	var failedFiles []result
	var skippedFiles []string
	for res := range results {
		atomic.AddInt32(&processedFiles, 1)
		if res.err == nil {
			successfulFiles = append(successfulFiles, res.path)
		} else if errors.Is(res.err, ErrAlreadyNormalized) {
			skippedFiles = append(skippedFiles, res.path)
		} else {
			failedFiles = append(failedFiles, res)
		}
		if progressEnabled {
			printProgressBar(atomic.LoadInt32(&processedFiles), totalFiles)
		}
	}

	cancelled := ctx.Err() != nil
	fmt.Println("\nAll tasks completed!")
	if cancelled {
		fmt.Println("Cancelled by signal.")
		logging.Warn("normalization cancelled by signal")
	}
	logging.Info("normalization finished", "succeeded", len(successfulFiles), "skipped", len(skippedFiles), "failed", len(failedFiles))

	fmt.Println("\n--- Processing Summary ---")
	fmt.Printf("\nSuccessfully processed %d files:\n", len(successfulFiles))
	if len(successfulFiles) > 0 {
		for _, file := range successfulFiles {
			fmt.Printf("  - %s\n", file)
		}
	} else {
		fmt.Println("  (None)")
	}

	fmt.Printf("\nSkipped %d files (already normalized):\n", len(skippedFiles))
	if len(skippedFiles) > 0 {
		for _, file := range skippedFiles {
			fmt.Printf("  - %s\n", file)
		}
	} else {
		fmt.Println("  (None)")
	}

	fmt.Printf("\nFailed to process %d files:\n", len(failedFiles))
	if len(failedFiles) > 0 {
		for _, f := range failedFiles {
			fmt.Printf("  - %s\n    Error: %v\n", f.path, f.err)
		}
	} else {
		fmt.Println("  (None)")
	}

	if waitAfter {
		toolkit.WaitForExit()
	}
	if cancelled {
		return toolkit.ExitCancelled
	}
	if len(failedFiles) > 0 {
		return toolkit.ExitFailure
	}
	return toolkit.ExitSuccess
}

// worker is a concurrent processor for handling normalization jobs.
func worker(ctx context.Context, opts Options, id int, wg *sync.WaitGroup, jobs <-chan job, results chan<- result) {
	defer wg.Done()
	for j := range jobs {
		if ctx.Err() != nil {
			results <- result{path: j.inputPath, err: context.Canceled}
			continue
		}
		err := processFile(ctx, opts, j.inputPath, j.outputPath)
		results <- result{path: j.inputPath, err: err}
	}
}

// collectAudioJobs walks rootDir once and maps every non-generated audio file
// to its normalized output destination. Two sources sharing a basename would
// map to the same output and silently overwrite each other, so a colliding plan
// is rejected up front with the full conflict list.
func collectAudioJobs(rootDir string, opts Options) ([]job, error) {
	var jobs []job
	walkErr := filepath.WalkDir(rootDir, func(path string, entry fs.DirEntry, err error) error {
		if err != nil {
			return err
		}
		if entry.IsDir() {
			return nil
		}
		if !isAudioFile(path) {
			return nil
		}
		if strings.HasSuffix(strings.TrimSuffix(path, filepath.Ext(path)), FILENAME_SUFFIX) {
			return nil
		}
		jobs = append(jobs, job{
			inputPath:  path,
			outputPath: strings.TrimSuffix(path, filepath.Ext(path)) + FILENAME_SUFFIX + opts.Format.ext,
		})
		return nil
	})
	if walkErr != nil {
		return nil, walkErr
	}

	owners := make(map[string][]string, len(jobs))
	order := make([]string, 0, len(jobs))
	for _, item := range jobs {
		if _, seen := owners[item.outputPath]; !seen {
			order = append(order, item.outputPath)
		}
		owners[item.outputPath] = append(owners[item.outputPath], item.inputPath)
	}
	var collisions []string
	for _, output := range order {
		if sources := owners[output]; len(sources) > 1 {
			collisions = append(collisions, fmt.Sprintf("  %s <- %s", output, strings.Join(sources, " + ")))
		}
	}
	if len(collisions) > 0 {
		return nil, fmt.Errorf(
			"output collision detected: multiple sources normalize to the same output file; rename the sources or normalize them separately:\n%s",
			strings.Join(collisions, "\n"),
		)
	}
	return jobs, nil
}

func detectCategory(filePath string) audioCategory {
	dir := filepath.Dir(filePath)
	parts := strings.Split(filepath.ToSlash(dir), "/")
	for i := len(parts) - 1; i >= 0; i-- {
		if cat, ok := categoryKeywords[strings.ToLower(parts[i])]; ok {
			return cat
		}
	}
	return categoryDefault
}

func parseDuration(ffmpegOutput string) float64 {
	m := durationRegex.FindStringSubmatch(ffmpegOutput)
	if len(m) < 5 {
		return -1
	}
	hours, _ := strconv.ParseFloat(m[1], 64)
	minutes, _ := strconv.ParseFloat(m[2], 64)
	seconds, _ := strconv.ParseFloat(m[3], 64)
	centiseconds, _ := strconv.ParseFloat(m[4], 64)
	return hours*3600 + minutes*60 + seconds + centiseconds/100
}

// processFile runs the loudnorm analysis pass, then delegates to the strategy
// matching the measured duration. Each external call owns its own bounded
// lifetime, so a short-file encode is never nested under an analysis deadline.
func processFile(ctx context.Context, opts Options, filePath, outputFilePath string) error {
	cat := detectCategory(filePath)

	loudnormFilterPass1 := fmt.Sprintf("loudnorm=I=%.1f:TP=%.1f:LRA=11:print_format=json", cat.targetLUFS, cat.targetTP)
	outcome, err := toolenv.Run(ctx, toolenv.Invocation{
		Binary:      opts.FFmpegPath,
		Args:        []string{"-i", filePath, "-af", loudnormFilterPass1, "-f", "null", "-"},
		Timeout:     opts.FFmpegTimeout,
		StderrLimit: toolenv.DefaultStderrLimit,
	})
	if err != nil {
		return fmt.Errorf("ffmpeg analysis failed: %s", toolenv.FormatFailure(err, outcome))
	}
	pass1Output := outcome.Stderr

	duration := parseDuration(pass1Output)
	sampleRateMatch := sampleRateRegex.FindStringSubmatch(pass1Output)
	if len(sampleRateMatch) < 2 {
		return fmt.Errorf("could not parse sample rate from ffmpeg output")
	}
	originalSampleRate, _ := strconv.Atoi(sampleRateMatch[1])
	targetSampleRate := originalSampleRate
	if originalSampleRate > MAX_SAMPLERATE || originalSampleRate == 0 {
		targetSampleRate = MAX_SAMPLERATE
	}

	if duration >= 0 && duration < SHORT_AUDIO_THRESHOLD_SEC {
		return processShortAudio(ctx, opts, filePath, outputFilePath, cat, targetSampleRate)
	}
	return processLongAudio(ctx, opts, filePath, outputFilePath, pass1Output, cat, targetSampleRate)
}

// prepareOutputTarget validates the destination before any file is created and
// returns an absolute path plus a CreateTemp pattern. The staging-name budget
// uses a representative longest random suffix so a long source name cannot
// produce an unusable temporary path.
func prepareOutputTarget(outputPath string) (string, string, error) {
	policy := safefs.HostPathPolicy()
	abs, err := filepath.Abs(outputPath)
	if err != nil {
		return "", "", fmt.Errorf("cannot resolve output path %s: %w", outputPath, err)
	}
	if err := policy.ValidateAbsolute(abs); err != nil {
		return "", "", err
	}
	base := filepath.Base(abs)
	if err := policy.ValidateComponent(base); err != nil {
		return "", "", err
	}
	dir := filepath.Dir(abs)
	if err := policy.EnsureWritable(dir); err != nil {
		return "", "", err
	}
	pattern := "." + base + ".partial-*"
	budgetName := "." + base + ".partial-123456789012"
	if err := policy.Budget(dir, budgetName); err != nil {
		return "", "", err
	}
	return abs, pattern, nil
}

// processShortAudio applies peak normalization for short sound effects.
func processShortAudio(ctx context.Context, opts Options, filePath, outputFilePath string, cat audioCategory, targetSampleRate int) error {
	detectOutcome, err := toolenv.Run(ctx, toolenv.Invocation{
		Binary:      opts.FFmpegPath,
		Args:        []string{"-i", filePath, "-af", "volumedetect", "-f", "null", "-"},
		Timeout:     opts.FFmpegTimeout,
		StderrLimit: toolenv.DefaultStderrLimit,
	})
	if err != nil {
		return fmt.Errorf("ffmpeg volumedetect failed: %s", toolenv.FormatFailure(err, detectOutcome))
	}

	match := maxVolRegex.FindStringSubmatch(detectOutcome.Stderr)
	if len(match) < 2 {
		return fmt.Errorf("could not detect peak volume")
	}
	maxVolume, err := strconv.ParseFloat(match[1], 64)
	if err != nil {
		return fmt.Errorf("could not parse peak volume: %w", err)
	}
	if math.IsInf(maxVolume, -1) {
		return fmt.Errorf("file appears to be silent, skipping")
	}
	gainNeeded := cat.targetPeak - maxVolume
	if math.Abs(gainNeeded) <= PEAK_TOLERANCE {
		return ErrAlreadyNormalized
	}

	// WAV RIFF INFO cannot represent non-ASCII metadata, so source tags are
	// dropped; the engine discards game audio metadata at import anyway.
	volumeFilter := fmt.Sprintf("volume=%.2fdB", gainNeeded)
	absOutput, pattern, err := prepareOutputTarget(outputFilePath)
	if err != nil {
		return err
	}
	tempFile, err := os.CreateTemp(filepath.Dir(absOutput), pattern)
	if err != nil {
		return fmt.Errorf("failed to create temporary output file: %w", err)
	}
	tempPath := tempFile.Name()
	if err := tempFile.Close(); err != nil {
		_ = os.Remove(tempPath)
		return fmt.Errorf("failed to close temporary output file: %w", err)
	}

	args := []string{"-y", "-i", filePath, "-map_metadata", "-1", "-af", volumeFilter}
	args = append(args, buildAudioEncodeArgs(opts, targetSampleRate)...)
	args = append(args, tempPath)
	applyOutcome, applyErr := toolenv.Run(ctx, toolenv.Invocation{
		Binary:      opts.FFmpegPath,
		Args:        args,
		Timeout:     opts.FFmpegTimeout,
		StderrLimit: toolenv.DefaultStderrLimit,
	})
	if applyErr != nil {
		_ = os.Remove(tempPath)
		return fmt.Errorf("ffmpeg peak normalization failed: %s", toolenv.FormatFailure(applyErr, applyOutcome))
	}
	if err := safefs.PublishReplace(tempPath, absOutput); err != nil {
		_ = os.Remove(tempPath)
		return fmt.Errorf("failed to finalize output file: %w", err)
	}
	return nil
}

// processLongAudio applies two-pass LUFS normalization with linear mode.
func processLongAudio(ctx context.Context, opts Options, filePath, outputFilePath, pass1Output string, cat audioCategory, targetSampleRate int) error {
	lnInfo, err := extractLoudnormInfo(pass1Output)
	if err != nil {
		return fmt.Errorf("failed to extract loudness info: %w", err)
	}

	measuredLufs, err := strconv.ParseFloat(lnInfo.InputI, 64)
	if err == nil {
		if math.IsInf(measuredLufs, -1) {
			return fmt.Errorf("file appears to be silent (measured LUFS: -inf), skipping")
		}
		if math.Abs(measuredLufs-cat.targetLUFS) <= LOUDNESS_TOLERANCE {
			return ErrAlreadyNormalized
		}
	}

	loudnormFilterPass2 := fmt.Sprintf(
		"loudnorm=I=%.1f:TP=%.1f:LRA=11:measured_I=%s:measured_TP=%s:measured_LRA=%s:measured_thresh=%s:offset=%s:linear=true",
		cat.targetLUFS, cat.targetTP, lnInfo.InputI, lnInfo.InputTP, lnInfo.InputLRA, lnInfo.InputThresh, lnInfo.TargetOffset,
	)

	absOutput, pattern, err := prepareOutputTarget(outputFilePath)
	if err != nil {
		return err
	}
	tempFile, err := os.CreateTemp(filepath.Dir(absOutput), pattern)
	if err != nil {
		return fmt.Errorf("failed to create temporary output file: %w", err)
	}
	tempPath := tempFile.Name()
	if err := tempFile.Close(); err != nil {
		_ = os.Remove(tempPath)
		return fmt.Errorf("failed to close temporary output file: %w", err)
	}

	args := []string{"-y", "-i", filePath, "-map_metadata", "-1", "-af", loudnormFilterPass2}
	args = append(args, buildAudioEncodeArgs(opts, targetSampleRate)...)
	args = append(args, tempPath)
	pass2Outcome, pass2Err := toolenv.Run(ctx, toolenv.Invocation{
		Binary:      opts.FFmpegPath,
		Args:        args,
		Timeout:     opts.FFmpegTimeout,
		StderrLimit: toolenv.DefaultStderrLimit,
	})
	if pass2Err != nil {
		_ = os.Remove(tempPath)
		return fmt.Errorf("ffmpeg second pass failed: %s", toolenv.FormatFailure(pass2Err, pass2Outcome))
	}
	if err := safefs.PublishReplace(tempPath, absOutput); err != nil {
		_ = os.Remove(tempPath)
		return fmt.Errorf("failed to finalize output file: %w", err)
	}
	return nil
}

func extractLoudnormInfo(stderr string) (*LoudnormInfo, error) {
	// The loudnorm JSON block is the last JSON object in ffmpeg's stderr.
	lines := strings.Split(stderr, "\n")
	jsonEnd := -1
	jsonStart := -1
	for i := len(lines) - 1; i >= 0; i-- {
		trimmed := strings.TrimSpace(lines[i])
		if jsonEnd == -1 && strings.HasPrefix(trimmed, "}") {
			jsonEnd = i
		}
		if jsonEnd != -1 && strings.HasPrefix(trimmed, "{") {
			jsonStart = i
			break
		}
	}
	if jsonStart == -1 || jsonEnd == -1 {
		return nil, fmt.Errorf("could not find JSON block in ffmpeg stderr")
	}
	jsonText := strings.Join(lines[jsonStart:jsonEnd+1], "\n")
	var lnInfo LoudnormInfo
	if err := json.Unmarshal([]byte(jsonText), &lnInfo); err != nil {
		return nil, fmt.Errorf("failed to parse loudnorm JSON: %w", err)
	}
	return &lnInfo, nil
}

func isAudioFile(path string) bool {
	return audioExtensions[strings.ToLower(filepath.Ext(path))]
}

// buildAudioEncodeArgs assembles the output codec arguments shared by both
// normalization strategies.
func buildAudioEncodeArgs(opts Options, targetSampleRate int) []string {
	args := append([]string{}, opts.Format.ffmpegArgs...)
	if opts.Mono {
		args = append(args, "-ac", "1")
	}
	args = append(args, "-ar", strconv.Itoa(targetSampleRate))
	return args
}

func printProgressBar(current, total int32) {
	barLength := 40
	percent := float64(current) / float64(total)
	filledLength := int(float64(barLength) * percent)

	bar := strings.Repeat("#", filledLength) + strings.Repeat("-", barLength-filledLength)
	fmt.Printf("\r[%s] %.0f%% (%d/%d)", bar, percent*100, current, total)
	if current == total {
		fmt.Println()
	}
}
