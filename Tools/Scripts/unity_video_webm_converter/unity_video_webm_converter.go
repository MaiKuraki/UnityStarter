package unity_video_webm_converter

import (
	"bufio"
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"io"
	"io/fs"
	"math"
	"os"
	"os/exec"
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
	"cyclonegames.tools/scripts/internal/term"
	"cyclonegames.tools/scripts/internal/toolkit"
)

const (
	defaultAudioBitrate  = "128k"
	defaultAudioRate     = "44100"
	defaultFFmpegTimeout = 2 * time.Hour
	loudnessTolerance    = 0.5
	peakTolerance        = 0.5
	shortAudioThreshold  = 3.0
)

// ffmpegTimeout bounds every per-invocation FFmpeg call. It is configurable via
// the -ffmpeg-timeout flag so very long encodes can extend the bounded default.
var ffmpegTimeout = defaultFFmpegTimeout

// ffmpegBinary is the ffmpeg executable used by every invocation. It defaults
// to PATH lookup and can be overridden with the -ffmpeg flag (CI-friendly for
// pinned toolchain installations).
var ffmpegBinary = "ffmpeg"

// progressReportInterval throttles per-file encode progress lines so long
// cutscene encodes stay observable without flooding the console.
const progressReportInterval = 2 * time.Second

type preset struct {
	Key          string
	Name         string
	Description  string
	Suffix       string
	CRF          int
	CPUUsed      int
	VideoBitrate string
	VideoMaxRate string
	VideoBufSize string
	AudioBitrate string
}

type encodeSettings struct {
	Preset             preset
	SelectedResolution resolutionOption
	VideoBitrate       string
	VideoMaxRate       string
	VideoBufSize       string
	// AlphaOutput keeps transparency by encoding VP8 with the yuva420p pixel
	// format plus the alpha_mode metadata key (UI/VFX videos with alpha).
	AlphaOutput bool
}

type resolutionOption struct {
	Key         string
	Name        string
	Description string
	MaxWidth    int
	MaxHeight   int
}

type job struct {
	InputPath  string
	OutputPath string
}

type audioCategory struct {
	Name       string
	TargetLUFS float64
	TargetTP   float64
	TargetPeak float64
}

type loudnormInfo struct {
	InputI       string `json:"input_i"`
	InputTP      string `json:"input_tp"`
	InputLRA     string `json:"input_lra"`
	InputThresh  string `json:"input_thresh"`
	TargetOffset string `json:"target_offset"`
}

type mediaInfo struct {
	DurationSec      float64
	HasAudio         bool
	DetectedCategory audioCategory
}

var presets = []preset{
	{
		Key:          "1",
		Name:         "Small / Mobile",
		Description:  "Entry preset with a 3M video bitrate floor for cleaner mobile and lightweight runtime playback.",
		Suffix:       "_unity_vp8_mobile",
		CRF:          16,
		CPUUsed:      2,
		VideoBitrate: "3M",
		VideoMaxRate: "4.5M",
		VideoBufSize: "6M",
		AudioBitrate: defaultAudioBitrate,
	},
	{
		Key:          "2",
		Name:         "Balanced / Universal",
		Description:  "Default preset with a 9M target for noticeably cleaner playback across Android, iOS, WebGL, Windows, and macOS.",
		Suffix:       "_unity_vp8_balanced",
		CRF:          12,
		CPUUsed:      2,
		VideoBitrate: "9M",
		VideoMaxRate: "13.5M",
		VideoBufSize: "18M",
		AudioBitrate: defaultAudioBitrate,
	},
	{
		Key:          "3",
		Name:         "High / Showcase",
		Description:  "Highest quality preset with an 18M target for showcase clips and maximum preservation within VP8/WebM.",
		Suffix:       "_unity_vp8_high",
		CRF:          8,
		CPUUsed:      2,
		VideoBitrate: "18M",
		VideoMaxRate: "27M",
		VideoBufSize: "36M",
		AudioBitrate: defaultAudioBitrate,
	},
}

var resolutionOptions = []resolutionOption{
	{
		Key:         "1",
		Name:        "Original",
		Description: "Default. Keep the source resolution unchanged.",
		MaxWidth:    0,
		MaxHeight:   0,
	},
	{
		Key:         "2",
		Name:        "1080p Cap",
		Description: "Downscale only when the source is larger than 1920x1080.",
		MaxWidth:    1920,
		MaxHeight:   1080,
	},
	{
		Key:         "3",
		Name:        "720p Cap",
		Description: "Downscale only when the source is larger than 1280x720.",
		MaxWidth:    1280,
		MaxHeight:   720,
	},
	{
		Key:         "4",
		Name:        "540p Cap",
		Description: "Downscale only when the source is larger than 960x540.",
		MaxWidth:    960,
		MaxHeight:   540,
	},
}

var videoExtensions = map[string]bool{
	".mp4":  true,
	".mov":  true,
	".m4v":  true,
	".avi":  true,
	".mkv":  true,
	".webm": true,
	".wmv":  true,
	".mpg":  true,
	".mpeg": true,
	".ts":   true,
	".m2ts": true,
	".flv":  true,
}

var (
	categoryMusic   = audioCategory{Name: "Music", TargetLUFS: -14.0, TargetTP: -1.0, TargetPeak: -1.0}
	categoryVoice   = audioCategory{Name: "Voice", TargetLUFS: -16.0, TargetTP: -1.5, TargetPeak: -1.0}
	categorySFX     = audioCategory{Name: "SFX", TargetLUFS: -14.0, TargetTP: -1.0, TargetPeak: -1.0}
	categoryAmbient = audioCategory{Name: "Ambient", TargetLUFS: -20.0, TargetTP: -1.5, TargetPeak: -3.0}
	categoryDefault = audioCategory{Name: "Default", TargetLUFS: -16.0, TargetTP: -1.5, TargetPeak: -1.0}

	categoryKeywords = map[string]audioCategory{
		"music":    categoryMusic,
		"bgm":      categoryMusic,
		"voice":    categoryVoice,
		"dialog":   categoryVoice,
		"vo":       categoryVoice,
		"sfx":      categorySFX,
		"se":       categorySFX,
		"sound":    categorySFX,
		"ambient":  categoryAmbient,
		"env":      categoryAmbient,
		"video":    categoryDefault,
		"cutscene": categoryDefault,
		"movie":    categoryDefault,
		"movies":   categoryDefault,
	}

	durationRegex        = regexp.MustCompile(`Duration:\s+(\d+):(\d+):(\d+)\.(\d+)`)
	maxVolRegex          = regexp.MustCompile(`max_volume:\s+([\-\d.]+)\s+dB`)
	audioStreamRegex     = regexp.MustCompile(`Stream\s+#\d+:\d+(?:\[[^\]]+\])?(?:\([^)]+\))?:\s+Audio:`)
	videoStreamLineRegex = regexp.MustCompile(`Stream\s+#\d+:\d+(?:\[[^\]]+\])?(?:\([^)]+\))?:\s+Video:`)
	progressTimeRegex    = regexp.MustCompile(`time=(\d+:\d+:\d+\.\d+)`)
)

// Run executes the video converter and returns its process exit code.
func Run(args []string) int {
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()
	logging.Command("video_webm_converter")

	flags := flag.NewFlagSet("unity_video_webm_converter", flag.ContinueOnError)
	flags.SetOutput(os.Stderr)
	var ciMode bool
	var inputPath string
	var outputRoot string
	var presetKey string
	var overwrite bool
	var jobs int
	var ffmpegPath string
	var alphaOutput bool
	flags.BoolVar(&ciMode, "ci", false, "Non-interactive mode (requires --input and --output)")
	flags.StringVar(&inputPath, "input", "", "Source video file or folder (CI mode)")
	flags.StringVar(&outputRoot, "output", "", "Output root directory (CI mode)")
	flags.StringVar(&presetKey, "preset", "2", "Quality preset 1/2/3 (CI mode, default 2)")
	flags.BoolVar(&overwrite, "overwrite", false, "Overwrite existing outputs (CI mode)")
	flags.IntVar(&jobs, "jobs", 0, "Parallel conversion workers (default: CPU count, clamped 1..64)")
	flags.DurationVar(&ffmpegTimeout, "ffmpeg-timeout", defaultFFmpegTimeout, "Per-invocation FFmpeg timeout (for example 45m or 2h)")
	flags.StringVar(&ffmpegPath, "ffmpeg", "", "Custom ffmpeg executable path (default: ffmpeg on PATH)")
	flags.BoolVar(&alphaOutput, "alpha", false, "Preserve transparency: encode VP8 with the yuva420p alpha channel (UI/VFX videos)")
	if err := flags.Parse(args); err != nil {
		if errors.Is(err, flag.ErrHelp) {
			return 0
		}
		return 2
	}
	if ffmpegTimeout <= 0 {
		logging.Error("--ffmpeg-timeout must be a positive duration")
		return 2
	}
	if ffmpegPath != "" {
		resolved, resolveErr := resolveCustomFFmpeg(ffmpegPath)
		if resolveErr != nil {
			logging.Error("ffmpeg path not usable", "path", ffmpegPath, "error", resolveErr)
			return 2
		}
		ffmpegBinary = resolved
	}
	if ciMode {
		return runCi(ctx, inputPath, outputRoot, presetKey, overwrite, alphaOutput, jobs)
	}
	return runInteractiveFlow(ctx, jobs, alphaOutput)
}

// resolveCustomFFmpeg validates a user-provided ffmpeg path and returns its
// cleaned absolute form.
func resolveCustomFFmpeg(path string) (string, error) {
	cleaned := normalizePath(path)
	if cleaned == "" {
		return "", fmt.Errorf("empty path")
	}
	info, err := os.Stat(cleaned)
	if err != nil {
		return "", err
	}
	if info.IsDir() {
		return "", fmt.Errorf("path is a directory")
	}
	return cleaned, nil
}

// runInteractiveFlow is the guided interactive conversion.
func runInteractiveFlow(ctx context.Context, jobs int, alphaOutput bool) int {
	printIntro()

	if !commandExists("ffmpeg") {
		return failWithMessage("Error: ffmpeg was not found in PATH. Please install ffmpeg and make sure the command is available in your system environment.")
	}

	reader := bufio.NewReader(os.Stdin)

	sourcePath, err := chooseSourcePath(reader)
	if err != nil {
		return cancelledWithMessage(err)
	}

	info, err := os.Stat(sourcePath)
	if err != nil {
		return failWithMessage(fmt.Sprintf("Failed to read input path: %v", err))
	}

	selectedPreset, err := choosePreset(reader)
	if err != nil {
		return cancelledWithMessage(err)
	}
	selectedResolution, err := chooseResolution(reader)
	if err != nil {
		return cancelledWithMessage(err)
	}
	settings, err := chooseVideoBitrate(reader, selectedPreset, selectedResolution, alphaOutput)
	if err != nil {
		return cancelledWithMessage(err)
	}
	outputRoot, overwrite, err := chooseOutputOptions(reader, sourcePath, info, settings.Preset)
	if err != nil {
		return cancelledWithMessage(err)
	}

	conversionJobs, err := buildJobs(sourcePath, info, outputRoot, settings.Preset)
	if err != nil {
		return failWithMessage(fmt.Sprintf("Failed to build conversion list: %v", err))
	}
	if len(conversionJobs) == 0 {
		return failWithMessage("No supported video files were found to process.")
	}

	fmt.Println()
	fmt.Println("Summary")
	fmt.Printf("  Source:   %s\n", sourcePath)
	fmt.Printf("  Preset:   %s\n", settings.Preset.Name)
	fmt.Printf("  Resolution: %s\n", settings.SelectedResolution.Name)
	fmt.Printf("  Video bitrate: %s\n", settings.VideoBitrate)
	fmt.Printf("  Output:   %s\n", outputRoot)
	fmt.Printf("  Files:    %d\n", len(conversionJobs))
	fmt.Printf("  Overwrite:%t\n", overwrite)
	fmt.Println()
	fmt.Println("Audio note: WebM does not support AAC in the standard container, so this tool uses Vorbis audio plus loudness normalization for broad Unity/WebM compatibility.")

	confirmed, err := confirm(reader, "Start conversion now? (Y/N) [default: Y]: ", true)
	if err != nil {
		return cancelledWithMessage(err)
	}
	if !confirmed {
		return failWithMessage("Operation cancelled by user.")
	}

	successCount, skipCount, failCount := runConversion(ctx, conversionJobs, overwrite, settings, jobs)

	fmt.Println()
	fmt.Println("Finished")
	fmt.Printf("  Succeeded: %d\n", successCount)
	fmt.Printf("  Skipped:   %d\n", skipCount)
	fmt.Printf("  Failed:    %d\n", failCount)
	if ctx.Err() != nil {
		fmt.Println("  Cancelled by signal.")
	}
	toolkit.WaitForExit()
	if ctx.Err() != nil {
		return toolkit.ExitCancelled
	}
	if failCount > 0 {
		return toolkit.ExitFailure
	}
	return toolkit.ExitSuccess
}

// runCi executes the non-interactive conversion path.
func runCi(ctx context.Context, inputPath, outputRoot, presetKey string, overwrite bool, alphaOutput bool, workerCount int) int {
	if strings.TrimSpace(inputPath) == "" || strings.TrimSpace(outputRoot) == "" {
		logging.Error("--ci requires --input and --output")
		return 2
	}
	if !commandExists("ffmpeg") {
		logging.Error("ffmpeg not found in PATH")
		return 1
	}
	selectedPreset, ok := findPreset(presetKey)
	if !ok {
		logging.Error("unknown preset", "preset", presetKey)
		return 2
	}
	sourcePath := normalizePath(inputPath)
	if sourcePath == "" {
		logging.Error("invalid input path", "input", inputPath)
		return 2
	}
	info, err := os.Stat(sourcePath)
	if err != nil {
		logging.Error("input not found", "path", sourcePath)
		return 1
	}
	settings := buildEncodeSettings(selectedPreset, resolutionOptions[0], selectedPreset.VideoBitrate, alphaOutput)
	conversionJobs, err := buildJobs(sourcePath, info, outputRoot, selectedPreset)
	if err != nil {
		logging.Error("failed to build conversion list", "error", err)
		return 1
	}
	if len(conversionJobs) == 0 {
		logging.Error("no supported video files found")
		return 1
	}
	logging.Info("conversion started", "files", len(conversionJobs), "workers", workerCount, "preset", selectedPreset.Key)

	successCount, skipCount, failCount := runConversion(ctx, conversionJobs, overwrite, settings, workerCount)

	fmt.Println()
	fmt.Println("Finished")
	fmt.Printf("  Succeeded: %d\n", successCount)
	fmt.Printf("  Skipped:   %d\n", skipCount)
	fmt.Printf("  Failed:    %d\n", failCount)
	if ctx.Err() != nil {
		fmt.Println("  Cancelled by signal.")
		logging.Warn("conversion cancelled by signal")
		return toolkit.ExitCancelled
	}
	logging.Info("conversion finished", "succeeded", successCount, "skipped", skipCount, "failed", failCount)
	if failCount > 0 {
		return toolkit.ExitFailure
	}
	return toolkit.ExitSuccess
}

// findPreset resolves a quality preset by its numeric key.
func findPreset(key string) (preset, bool) {
	for _, candidate := range presets {
		if candidate.Key == key {
			return candidate, true
		}
	}
	return preset{}, false
}

// runConversion converts jobs over a bounded worker pool and returns outcome counts.
func runConversion(ctx context.Context, conversionJobs []job, overwrite bool, settings encodeSettings, requestedWorkers int) (int, int, int) {
	workerCount := requestedWorkers
	if workerCount <= 0 {
		workerCount = runtime.NumCPU()
	}
	if workerCount > 64 {
		workerCount = 64
	}
	if workerCount < 1 {
		workerCount = 1
	}
	// Share the machine's cores across the parallel encodes instead of letting
	// every libvpx process grab all of them at once.
	encodeThreads := computeThreadsPerEncode(runtime.NumCPU(), workerCount)

	type outcome struct {
		skip bool
		fail bool
	}
	jobChannel := make(chan job)
	outcomes := make(chan outcome)
	var workers sync.WaitGroup
	for i := 0; i < workerCount; i++ {
		workers.Add(1)
		go func() {
			defer workers.Done()
			for item := range jobChannel {
				if ctx.Err() != nil {
					outcomes <- outcome{fail: true}
					continue
				}
				if !overwrite {
					if _, statErr := os.Stat(item.OutputPath); statErr == nil {
						fmt.Printf("  Skipped: output already exists -> %s\n", item.OutputPath)
						outcomes <- outcome{skip: true}
						continue
					}
				}
				if err := os.MkdirAll(filepath.Dir(item.OutputPath), 0o755); err != nil {
					fmt.Printf("  Failed: create output directory failed: %v\n", err)
					outcomes <- outcome{fail: true}
					continue
				}
				if err := convertVideo(ctx, item.InputPath, item.OutputPath, settings, encodeThreads); err != nil {
					if ctx.Err() != nil {
						fmt.Printf("  Cancelled: %s\n", filepath.Base(item.InputPath))
					} else {
						fmt.Printf("  Failed: %v\n", err)
					}
					outcomes <- outcome{fail: true}
					continue
				}
				fmt.Printf("  Done: %s\n", item.OutputPath)
				outcomes <- outcome{}
			}
		}()
	}
	go func() {
		defer close(jobChannel)
		for _, item := range conversionJobs {
			select {
			case jobChannel <- item:
			case <-ctx.Done():
				return
			}
		}
	}()
	go func() {
		workers.Wait()
		close(outcomes)
	}()

	successCount := 0
	skipCount := 0
	failCount := 0
	for result := range outcomes {
		if result.skip {
			skipCount++
		} else if result.fail {
			failCount++
		} else {
			successCount++
		}
	}
	return successCount, skipCount, failCount
}

func printIntro() {
	fmt.Println("--- Unity Video WebM Converter ---")
	fmt.Println()
	fmt.Println("This tool calls the system ffmpeg to convert video files into Unity-friendly")
	fmt.Println("VP8 WebM outputs suitable for Android, iOS, WebGL, Windows, and macOS builds.")
	fmt.Println()
	fmt.Println("Defaults")
	fmt.Println("  Video: VP8 / yuv420p / default target bitrate 9M")
	fmt.Println("  Audio: Vorbis / stereo / 44.1 kHz / loudness-normalized")
	fmt.Println("  Preset: Balanced / Universal")
	fmt.Println("  Resolution: Original")
	fmt.Println("  Bitrate: adjustable after preset selection")
	fmt.Println()
	fmt.Println("Tip: you can paste or drag a file/folder path directly into this console.")
	fmt.Println()
	fmt.Println("Audio normalization")
	fmt.Println("  Long audio (>= 3s): two-pass LUFS loudness normalization")
	fmt.Println("  Short audio (< 3s): peak normalization for safer short clips")
	fmt.Println()
}

func chooseSourcePath(reader *bufio.Reader) (string, error) {
	for {
		fmt.Print("Enter a video file or folder path (drag-and-drop is also supported): ")
		text, err := readLine(reader)
		if err != nil {
			// A closed input (redirect, script, Ctrl+Z) must exit instead of looping.
			return "", err
		}

		if text == "" {
			fmt.Println("Please enter a valid path.")
			continue
		}

		cleaned := normalizePath(text)
		if cleaned == "" {
			fmt.Println("Please enter a valid path.")
			continue
		}

		if _, err := os.Stat(cleaned); err != nil {
			fmt.Printf("Path not found: %s\n", cleaned)
			continue
		}

		return cleaned, nil
	}
}

func choosePreset(reader *bufio.Reader) (preset, error) {
	fmt.Println("Quality presets")
	for _, p := range presets {
		fmt.Printf("  %s. %s\n", p.Key, p.Name)
		fmt.Printf("     %s\n", p.Description)
		fmt.Printf("     Output suffix: %s.webm\n", p.Suffix)
	}

	fmt.Print("Select preset (1/2/3) [default: 2]: ")
	input, err := readLine(reader)
	if err != nil {
		return presets[1], err
	}
	input = strings.TrimSpace(input)
	if input == "" {
		return presets[1], nil
	}

	for _, p := range presets {
		if p.Key == input {
			return p, nil
		}
	}

	fmt.Println("Unknown preset selection, using Balanced / Universal.")
	return presets[1], nil
}

func chooseResolution(reader *bufio.Reader) (resolutionOption, error) {
	fmt.Println("Resolution options")
	for _, option := range resolutionOptions {
		fmt.Printf("  %s. %s\n", option.Key, option.Name)
		fmt.Printf("     %s\n", option.Description)
	}

	fmt.Print("Select resolution option (1/2/3/4) [default: 1]: ")
	input, err := readLine(reader)
	if err != nil {
		return resolutionOptions[0], err
	}
	input = strings.TrimSpace(input)
	if input == "" {
		return resolutionOptions[0], nil
	}

	for _, option := range resolutionOptions {
		if option.Key == input {
			return option, nil
		}
	}

	fmt.Println("Unknown resolution selection, using Original.")
	return resolutionOptions[0], nil
}

func chooseVideoBitrate(reader *bufio.Reader, selectedPreset preset, selectedResolution resolutionOption, alphaOutput bool) (encodeSettings, error) {
	fmt.Printf("Video bitrate [default: %s, examples: 12M / 8500k]: ", selectedPreset.VideoBitrate)
	input, err := readLine(reader)
	if err != nil {
		return buildEncodeSettings(selectedPreset, selectedResolution, selectedPreset.VideoBitrate, alphaOutput), err
	}

	input = strings.TrimSpace(input)
	if input == "" {
		return buildEncodeSettings(selectedPreset, selectedResolution, selectedPreset.VideoBitrate, alphaOutput), nil
	}

	normalized, ok := normalizeBitrateInput(input)
	if !ok {
		fmt.Printf("Unknown bitrate format '%s', using default %s.\n", input, selectedPreset.VideoBitrate)
		return buildEncodeSettings(selectedPreset, selectedResolution, selectedPreset.VideoBitrate, alphaOutput), nil
	}

	return buildEncodeSettings(selectedPreset, selectedResolution, normalized, alphaOutput), nil
}

func buildEncodeSettings(selectedPreset preset, selectedResolution resolutionOption, videoBitrate string, alphaOutput bool) encodeSettings {
	return encodeSettings{
		Preset:             selectedPreset,
		SelectedResolution: selectedResolution,
		VideoBitrate:       videoBitrate,
		VideoMaxRate:       scaleBitrate(videoBitrate, 1.5),
		VideoBufSize:       scaleBitrate(videoBitrate, 2.0),
		AlphaOutput:        alphaOutput,
	}
}

func chooseOutputOptions(reader *bufio.Reader, sourcePath string, info os.FileInfo, selectedPreset preset) (string, bool, error) {
	defaultOutputRoot := defaultOutputPath(sourcePath, info, selectedPreset)

	fmt.Printf("Output folder [default: %s]: ", defaultOutputRoot)
	outputText, err := readLine(reader)
	if err != nil {
		return "", false, err
	}

	outputRoot := defaultOutputRoot
	if strings.TrimSpace(outputText) != "" {
		outputRoot = normalizePath(outputText)
	}

	overwrite, err := confirm(reader, "Overwrite existing outputs? (y/N) [default: N]: ", false)
	if err != nil {
		return "", false, err
	}
	return outputRoot, overwrite, nil
}

// cancelledWithMessage converts an interactive input error into a graceful
// exit, treating a closed input stream as a cancellation instead of a hang.
func cancelledWithMessage(err error) int {
	if errors.Is(err, io.EOF) {
		return failWithMessage("Cancelled: input stream closed.")
	}
	return failWithMessage(fmt.Sprintf("Cancelled: %v", err))
}

func buildJobs(sourcePath string, info os.FileInfo, outputRoot string, selectedPreset preset) ([]job, error) {
	if !info.IsDir() {
		if !isVideoFile(sourcePath) {
			return nil, fmt.Errorf("input file is not a supported video type: %s", sourcePath)
		}
		if hasGeneratedSuffix(sourcePath, selectedPreset.Suffix) {
			return nil, fmt.Errorf("input file already looks like a generated output: %s", sourcePath)
		}

		outputPath := filepath.Join(outputRoot, outputFileName(sourcePath, selectedPreset.Suffix))
		return []job{{InputPath: sourcePath, OutputPath: outputPath}}, nil
	}

	var jobs []job
	skipOutputRoot := ""
	if rel, relErr := filepath.Rel(sourcePath, outputRoot); relErr == nil && rel != "." && !strings.HasPrefix(rel, "..") {
		skipOutputRoot = filepath.Clean(outputRoot)
	}

	err := filepath.WalkDir(sourcePath, func(path string, d fs.DirEntry, walkErr error) error {
		if walkErr != nil {
			return walkErr
		}
		if d.IsDir() {
			if skipOutputRoot != "" && filepath.Clean(path) == skipOutputRoot {
				return filepath.SkipDir
			}
			return nil
		}
		if !isVideoFile(path) {
			return nil
		}
		if hasGeneratedSuffix(path, selectedPreset.Suffix) {
			return nil
		}

		relativePath, relErr := filepath.Rel(sourcePath, path)
		if relErr != nil {
			return relErr
		}

		targetDir := filepath.Join(outputRoot, filepath.Dir(relativePath))
		jobs = append(jobs, job{
			InputPath:  path,
			OutputPath: filepath.Join(targetDir, outputFileName(path, selectedPreset.Suffix)),
		})
		return nil
	})
	if err != nil {
		return nil, err
	}
	if collisions := findOutputCollisions(jobs); len(collisions) != 0 {
		return nil, fmt.Errorf(
			"output collision detected: multiple sources convert to the same output file; rename the sources or convert them separately:\n%s",
			strings.Join(collisions, "\n"),
		)
	}

	return jobs, nil
}

// findOutputCollisions reports output paths that more than one source maps to.
// Two sources sharing a basename (for example clip.mp4 and clip.mov) would
// otherwise silently overwrite each other's output while both jobs report
// success, so collisions must fail the plan instead.
func findOutputCollisions(jobs []job) []string {
	owners := make(map[string][]string, len(jobs))
	order := make([]string, 0, len(jobs))
	for _, item := range jobs {
		if _, seen := owners[item.OutputPath]; !seen {
			order = append(order, item.OutputPath)
		}
		owners[item.OutputPath] = append(owners[item.OutputPath], item.InputPath)
	}
	var collisions []string
	for _, output := range order {
		if sources := owners[output]; len(sources) > 1 {
			collisions = append(collisions, fmt.Sprintf("  %s <- %s", output, strings.Join(sources, " + ")))
		}
	}
	return collisions
}

func convertVideo(ctx context.Context, inputPath string, outputPath string, settings encodeSettings, encodeThreads int) error {
	// A unique per-run temporary name (same directory, same extension) keeps concurrent
	// conversions from clobbering each other and makes the final rename atomic.
	tempFile, err := os.CreateTemp(filepath.Dir(outputPath), "."+filepath.Base(outputPath)+".partial-*"+filepath.Ext(outputPath))
	if err != nil {
		return fmt.Errorf("failed to create temporary output file: %w", err)
	}
	tempOutput := tempFile.Name()
	if err := tempFile.Close(); err != nil {
		_ = os.Remove(tempOutput)
		return fmt.Errorf("failed to close temporary output file: %w", err)
	}

	info, err := probeMediaInfo(ctx, inputPath)
	if err != nil {
		_ = os.Remove(tempOutput)
		return err
	}

	audioArgs, err := buildAudioArgs(ctx, inputPath, info, settings.Preset)
	if err != nil {
		_ = os.Remove(tempOutput)
		return err
	}

	args := []string{"-y", "-hide_banner", "-i", inputPath}
	args = append(args, buildVideoEncodeArgs(settings, encodeThreads)...)
	if scaleFilter := buildScaleFilter(settings.SelectedResolution); scaleFilter != "" {
		args = append(args, "-vf", scaleFilter)
	}
	args = append(args, audioArgs...)
	args = append(args, tempOutput)

	ctx, cancel := context.WithTimeout(ctx, ffmpegTimeout)
	defer cancel()

	cmd := exec.CommandContext(ctx, ffmpegBinary, args...)
	var stderrBuf bytes.Buffer
	progress := newFFmpegProgressWriter()
	cmd.Stderr = io.MultiWriter(&stderrBuf, progress)

	if startErr := cmd.Start(); startErr != nil {
		_ = os.Remove(tempOutput)
		return fmt.Errorf("failed to start ffmpeg: %w", startErr)
	}

	progressDone := make(chan struct{})
	var progressPrinted atomic.Bool
	if term.IsTerminal(os.Stdout.Fd()) && info.DurationSec > 0 {
		go func() {
			ticker := time.NewTicker(progressReportInterval)
			defer ticker.Stop()
			for {
				select {
				case <-progressDone:
					return
				case <-ctx.Done():
					return
				case <-ticker.C:
					elapsed := progress.lastEncodedSeconds()
					percent := int(math.Round(elapsed / info.DurationSec * 100))
					if percent < 0 {
						percent = 0
					}
					if percent > 100 {
						percent = 100
					}
					progressPrinted.Store(true)
					fmt.Printf("\r  Encoding %s: %d%% (%s / %s)",
						filepath.Base(inputPath), percent,
						formatSecondsAsTimecode(elapsed), formatSecondsAsTimecode(info.DurationSec))
				}
			}
		}()
	}

	waitErr := cmd.Wait()
	close(progressDone)
	if progressPrinted.Load() {
		fmt.Println()
	}

	if ctx.Err() == context.DeadlineExceeded {
		_ = os.Remove(tempOutput)
		return fmt.Errorf("ffmpeg timed out after %s", ffmpegTimeout)
	}
	if waitErr != nil {
		_ = os.Remove(tempOutput)
		return fmt.Errorf("ffmpeg error: %w\n%s", waitErr, trimCommandOutput(stderrBuf.Bytes()))
	}

	if err := os.Rename(tempOutput, outputPath); err != nil {
		_ = os.Remove(tempOutput)
		return fmt.Errorf("failed to finalize output file: %w", err)
	}
	return nil
}

// buildVideoEncodeArgs assembles the libvpx video arguments (everything that
// applies to the output stream, before scaling, audio, and the output path) so
// the codec policy is testable in isolation from subprocess plumbing.
func buildVideoEncodeArgs(settings encodeSettings, encodeThreads int) []string {
	pixFmt := "yuv420p"
	if settings.AlphaOutput {
		pixFmt = "yuva420p"
	}
	args := []string{
		"-map_metadata", "-1",
		"-map_chapters", "-1",
		"-map", "0:v:0",
		"-map", "0:a:0?",
		"-sn",
		"-pix_fmt", pixFmt,
		"-c:v", "libvpx",
		"-crf", fmt.Sprintf("%d", settings.Preset.CRF),
		"-b:v", settings.VideoBitrate,
		"-maxrate", settings.VideoMaxRate,
		"-bufsize", settings.VideoBufSize,
		"-deadline", "good",
		"-cpu-used", fmt.Sprintf("%d", settings.Preset.CPUUsed),
		"-threads", strconv.Itoa(encodeThreads),
		"-g", "120",
		"-lag-in-frames", "16",
	}
	// libvpx cannot open an alpha encoder while auto-alt-ref is enabled, so
	// alpha mode forces it off; transparency is signalled to players through
	// the alpha_mode metadata key on the video stream.
	if settings.AlphaOutput {
		args = append(args, "-auto-alt-ref", "0")
		args = append(args, "-metadata:s:v:0", "alpha_mode=1")
	} else {
		args = append(args, "-auto-alt-ref", "1")
	}
	return args
}

// ffmpegProgressWriter consumes ffmpeg's carriage-return progress stream and
// exposes the most recent encoded timestamp (in seconds) for throttled display.
type ffmpegProgressWriter struct {
	mutex          sync.Mutex
	tail           string
	encodedSeconds float64
}

func newFFmpegProgressWriter() *ffmpegProgressWriter {
	return &ffmpegProgressWriter{}
}

func (w *ffmpegProgressWriter) Write(p []byte) (int, error) {
	w.mutex.Lock()
	defer w.mutex.Unlock()
	w.tail += string(p)
	for {
		idx := strings.IndexAny(w.tail, "\r\n")
		if idx < 0 {
			break
		}
		segment := w.tail[:idx]
		w.tail = w.tail[idx+1:]
		if match := progressTimeRegex.FindStringSubmatch(segment); match != nil {
			if seconds, parseErr := parseTimecodeSeconds(match[1]); parseErr == nil {
				w.encodedSeconds = seconds
			}
		}
	}
	if len(w.tail) > 8192 {
		w.tail = w.tail[len(w.tail)-8192:]
	}
	return len(p), nil
}

func (w *ffmpegProgressWriter) lastEncodedSeconds() float64 {
	w.mutex.Lock()
	defer w.mutex.Unlock()
	return w.encodedSeconds
}

func parseTimecodeSeconds(timecode string) (float64, error) {
	parts := strings.Split(timecode, ":")
	if len(parts) != 3 {
		return 0, fmt.Errorf("invalid timecode %q", timecode)
	}
	hours, err := strconv.ParseFloat(parts[0], 64)
	if err != nil {
		return 0, err
	}
	minutes, err := strconv.ParseFloat(parts[1], 64)
	if err != nil {
		return 0, err
	}
	seconds, err := strconv.ParseFloat(parts[2], 64)
	if err != nil {
		return 0, err
	}
	return hours*3600 + minutes*60 + seconds, nil
}

func formatSecondsAsTimecode(seconds float64) string {
	if seconds < 0 {
		seconds = 0
	}
	total := int(math.Round(seconds))
	return fmt.Sprintf("%02d:%02d:%02d", total/3600, (total%3600)/60, total%60)
}

// computeThreadsPerEncode divides the machine's cores across the parallel
// conversions. libvpx with automatic threads grabs every core per encode,
// which thrashes when several conversions run side by side; a bounded share
// keeps total CPU usage at the core count while each encode still gets real
// parallelism.
func computeThreadsPerEncode(cpus, workers int) int {
	if cpus < 1 {
		cpus = 1
	}
	if workers < 1 {
		workers = 1
	}
	threads := cpus / workers
	if threads < 1 {
		threads = 1
	}
	if threads > 8 {
		threads = 8
	}
	return threads
}

func buildScaleFilter(selectedResolution resolutionOption) string {
	if selectedResolution.MaxWidth <= 0 || selectedResolution.MaxHeight <= 0 {
		return ""
	}
	return fmt.Sprintf(
		"scale=w='min(iw,%d)':h='min(ih,%d)':force_original_aspect_ratio=decrease:force_divisible_by=2",
		selectedResolution.MaxWidth,
		selectedResolution.MaxHeight,
	)
}

func defaultOutputPath(sourcePath string, info os.FileInfo, selectedPreset preset) string {
	if info.IsDir() {
		return filepath.Join(sourcePath, "_converted"+selectedPreset.Suffix)
	}
	return filepath.Dir(sourcePath)
}

func outputFileName(inputPath string, suffix string) string {
	base := strings.TrimSuffix(filepath.Base(inputPath), filepath.Ext(inputPath))
	return base + suffix + ".webm"
}

func isVideoFile(path string) bool {
	return videoExtensions[strings.ToLower(filepath.Ext(path))]
}

func hasGeneratedSuffix(path string, suffix string) bool {
	base := strings.TrimSuffix(filepath.Base(path), filepath.Ext(path))
	return strings.HasSuffix(base, suffix)
}

// probeMediaInfo reads duration and stream layout from the container headers.
// It prefers ffprobe (millisecond header reads) and falls back to an ffmpeg -i
// header dump for ffmpeg-only installations. Neither path decodes the media
// payload: the previous implementation mapped the whole file to null just to
// learn the duration, which made every batch job pay a full decode upfront.
func probeMediaInfo(ctx context.Context, inputPath string) (mediaInfo, error) {
	if ffprobeBin, ok := resolveFFprobe(); ok {
		return probeWithFFprobe(ctx, ffprobeBin, inputPath)
	}
	return probeWithFFmpegHeader(ctx, inputPath)
}

// resolveFFprobe locates an ffprobe executable matching the configured ffmpeg
// binary (same directory when a custom path is given), falling back to the
// system PATH. The second result reports whether a probe binary was found.
func resolveFFprobe() (string, bool) {
	if ffmpegBinary != "ffmpeg" {
		probeName := "ffprobe"
		if runtime.GOOS == "windows" {
			probeName = "ffprobe.exe"
		}
		candidate := filepath.Join(filepath.Dir(ffmpegBinary), probeName)
		if info, err := os.Lstat(candidate); err == nil && info.Mode().IsRegular() {
			return candidate, true
		}
	}
	if _, err := exec.LookPath("ffprobe"); err == nil {
		return "ffprobe", true
	}
	return "", false
}

func probeWithFFprobe(ctx context.Context, ffprobeBin, inputPath string) (mediaInfo, error) {
	ctx, cancel := context.WithTimeout(ctx, ffmpegTimeout)
	defer cancel()

	cmd := exec.CommandContext(ctx, ffprobeBin, "-v", "error", "-show_entries",
		"format=duration:stream=codec_type", "-of", "json", inputPath)
	var stdout, stderr bytes.Buffer
	cmd.Stdout = &stdout
	cmd.Stderr = &stderr
	if err := cmd.Run(); err != nil {
		return mediaInfo{}, fmt.Errorf("ffprobe failed for %s: %w\n%s", inputPath, err, trimCommandOutput(stderr.Bytes()))
	}

	var probe struct {
		Streams []struct {
			CodecType string `json:"codec_type"`
		} `json:"streams"`
		Format struct {
			Duration string `json:"duration"`
		} `json:"format"`
	}
	if jsonErr := json.Unmarshal(stdout.Bytes(), &probe); jsonErr != nil {
		return mediaInfo{}, fmt.Errorf("ffprobe output for %s is not valid JSON: %w", inputPath, jsonErr)
	}

	hasVideo, hasAudio := false, false
	for _, stream := range probe.Streams {
		switch stream.CodecType {
		case "video":
			hasVideo = true
		case "audio":
			hasAudio = true
		}
	}
	if !hasVideo {
		return mediaInfo{}, fmt.Errorf("no video stream found in %s", inputPath)
	}

	duration := -1.0
	if probe.Format.Duration != "" {
		if parsed, parseErr := strconv.ParseFloat(probe.Format.Duration, 64); parseErr == nil {
			duration = parsed
		}
	}
	return mediaInfo{DurationSec: duration, HasAudio: hasAudio, DetectedCategory: detectCategory(inputPath)}, nil
}

// probeWithFFmpegHeader parses the input header dump from ffmpeg -i. It is the
// fallback for ffmpeg-only installations and never decodes the payload; the
// non-zero exit (no output was requested) is expected and ignored.
func probeWithFFmpegHeader(ctx context.Context, inputPath string) (mediaInfo, error) {
	ctx, cancel := context.WithTimeout(ctx, ffmpegTimeout)
	defer cancel()

	cmd := exec.CommandContext(ctx, ffmpegBinary, "-hide_banner", "-i", inputPath)
	var stderr bytes.Buffer
	cmd.Stderr = &stderr
	_ = cmd.Run()

	if ctx.Err() == context.DeadlineExceeded {
		return mediaInfo{}, fmt.Errorf("ffmpeg media inspection timed out after %s", ffmpegTimeout)
	}

	output := stderr.String()
	if !videoStreamLineRegex.MatchString(output) {
		return mediaInfo{}, fmt.Errorf("no video stream found in %s", inputPath)
	}
	return mediaInfo{
		DurationSec:      parseDuration(output),
		HasAudio:         audioStreamRegex.MatchString(output),
		DetectedCategory: detectCategory(inputPath),
	}, nil
}

func buildAudioArgs(ctx context.Context, inputPath string, info mediaInfo, selectedPreset preset) ([]string, error) {
	if !info.HasAudio {
		return []string{"-an"}, nil
	}

	audioFilter, err := buildNormalizedAudioFilter(ctx, inputPath, info)
	if err != nil {
		return nil, err
	}

	return []string{
		"-af", audioFilter,
		"-c:a", "libvorbis",
		"-b:a", selectedPreset.AudioBitrate,
		"-ar", defaultAudioRate,
		"-ac", "2",
	}, nil
}

func buildNormalizedAudioFilter(ctx context.Context, inputPath string, info mediaInfo) (string, error) {
	if info.DurationSec >= 0 && info.DurationSec < shortAudioThreshold {
		return buildPeakNormalizationFilter(ctx, inputPath, info.DetectedCategory)
	}
	return buildLoudnormFilter(ctx, inputPath, info.DetectedCategory)
}

func buildPeakNormalizationFilter(ctx context.Context, inputPath string, category audioCategory) (string, error) {
	ctx, cancel := context.WithTimeout(ctx, ffmpegTimeout)
	defer cancel()

	cmd := exec.CommandContext(ctx, "ffmpeg", "-hide_banner", "-i", inputPath, "-vn", "-map", "0:a:0", "-af", "volumedetect", "-f", "null", "-")
	var stderr bytes.Buffer
	cmd.Stdout = io.Discard
	cmd.Stderr = &stderr
	_ = cmd.Run()

	if ctx.Err() == context.DeadlineExceeded {
		return "", fmt.Errorf("ffmpeg volumedetect timed out after %s", ffmpegTimeout)
	}

	match := maxVolRegex.FindStringSubmatch(stderr.String())
	if len(match) < 2 {
		return "", fmt.Errorf("could not detect peak volume for audio track")
	}

	maxVolume, err := strconv.ParseFloat(match[1], 64)
	if err != nil {
		return "", fmt.Errorf("could not parse peak volume: %w", err)
	}

	if math.IsInf(maxVolume, -1) {
		return "volume=0dB", nil
	}

	gainNeeded := category.TargetPeak - maxVolume
	if math.Abs(gainNeeded) <= peakTolerance {
		return "volume=0dB", nil
	}

	return fmt.Sprintf("volume=%.2fdB", gainNeeded), nil
}

func buildLoudnormFilter(ctx context.Context, inputPath string, category audioCategory) (string, error) {
	pass1Filter := fmt.Sprintf("loudnorm=I=%.1f:TP=%.1f:LRA=11:print_format=json", category.TargetLUFS, category.TargetTP)

	ctx, cancel := context.WithTimeout(ctx, ffmpegTimeout)
	defer cancel()

	cmd := exec.CommandContext(ctx, "ffmpeg", "-hide_banner", "-i", inputPath, "-vn", "-map", "0:a:0", "-af", pass1Filter, "-f", "null", "-")
	var stderr bytes.Buffer
	cmd.Stdout = io.Discard
	cmd.Stderr = &stderr
	_ = cmd.Run()

	if ctx.Err() == context.DeadlineExceeded {
		return "", fmt.Errorf("ffmpeg loudnorm analysis timed out after %s", ffmpegTimeout)
	}

	lnInfo, err := extractLoudnormInfo(stderr.String())
	if err != nil {
		return "", fmt.Errorf("failed to extract loudness info: %w", err)
	}

	measuredLUFS, err := strconv.ParseFloat(lnInfo.InputI, 64)
	if err == nil {
		if math.IsInf(measuredLUFS, -1) {
			return "volume=0dB", nil
		}
		if math.Abs(measuredLUFS-category.TargetLUFS) <= loudnessTolerance {
			return "volume=0dB", nil
		}
	}

	return fmt.Sprintf(
		"loudnorm=I=%.1f:TP=%.1f:LRA=11:measured_I=%s:measured_TP=%s:measured_LRA=%s:measured_thresh=%s:offset=%s:linear=true",
		category.TargetLUFS,
		category.TargetTP,
		lnInfo.InputI,
		lnInfo.InputTP,
		lnInfo.InputLRA,
		lnInfo.InputThresh,
		lnInfo.TargetOffset,
	), nil
}

func detectCategory(filePath string) audioCategory {
	dir := filepath.Dir(filePath)
	parts := strings.Split(filepath.ToSlash(dir), "/")
	for i := len(parts) - 1; i >= 0; i-- {
		folderLower := strings.ToLower(parts[i])
		if category, ok := categoryKeywords[folderLower]; ok {
			return category
		}
	}
	return categoryDefault
}

func parseDuration(ffmpegOutput string) float64 {
	matches := durationRegex.FindStringSubmatch(ffmpegOutput)
	if len(matches) < 5 {
		return -1
	}

	hours, _ := strconv.ParseFloat(matches[1], 64)
	minutes, _ := strconv.ParseFloat(matches[2], 64)
	seconds, _ := strconv.ParseFloat(matches[3], 64)
	centiseconds, _ := strconv.ParseFloat(matches[4], 64)
	return hours*3600 + minutes*60 + seconds + centiseconds/100
}

func extractLoudnormInfo(stderr string) (*loudnormInfo, error) {
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
		return nil, fmt.Errorf("could not find loudnorm JSON block in ffmpeg output")
	}

	jsonText := strings.Join(lines[jsonStart:jsonEnd+1], "\n")
	var info loudnormInfo
	if err := json.Unmarshal([]byte(jsonText), &info); err != nil {
		return nil, fmt.Errorf("failed to parse loudnorm JSON: %w", err)
	}

	return &info, nil
}

func normalizeBitrateInput(input string) (string, bool) {
	value := strings.ToLower(strings.TrimSpace(input))
	if value == "" {
		return "", false
	}
	if strings.HasSuffix(value, "m") {
		numberPart := strings.TrimSuffix(value, "m")
		if _, err := strconv.ParseFloat(numberPart, 64); err == nil {
			return strings.ToUpper(value), true
		}
		return "", false
	}
	if strings.HasSuffix(value, "k") {
		numberPart := strings.TrimSuffix(value, "k")
		if _, err := strconv.ParseFloat(numberPart, 64); err == nil {
			return value, true
		}
		return "", false
	}
	if _, err := strconv.ParseFloat(value, 64); err == nil {
		return value + "k", true
	}
	return "", false
}

func scaleBitrate(bitrate string, multiplier float64) string {
	value := strings.ToLower(strings.TrimSpace(bitrate))
	switch {
	case strings.HasSuffix(value, "m"):
		numberPart := strings.TrimSuffix(value, "m")
		if parsed, err := strconv.ParseFloat(numberPart, 64); err == nil {
			return formatBitrateValue(parsed*multiplier, "M")
		}
	case strings.HasSuffix(value, "k"):
		numberPart := strings.TrimSuffix(value, "k")
		if parsed, err := strconv.ParseFloat(numberPart, 64); err == nil {
			return formatBitrateValue(parsed*multiplier, "k")
		}
	}
	return bitrate
}

func formatBitrateValue(value float64, unit string) string {
	if math.Abs(value-math.Round(value)) < 0.001 {
		return fmt.Sprintf("%.0f%s", math.Round(value), unit)
	}
	return fmt.Sprintf("%.1f%s", value, unit)
}

func normalizePath(text string) string {
	text = strings.TrimSpace(text)
	text = strings.Trim(text, "\"'")
	if text == "" {
		return ""
	}
	return filepath.Clean(text)
}

func commandExists(name string) bool {
	_, err := exec.LookPath(name)
	return err == nil
}

func confirm(reader *bufio.Reader, prompt string, defaultYes bool) (bool, error) {
	fmt.Print(prompt)
	text, err := readLine(reader)
	if err != nil {
		return false, err
	}

	text = strings.ToLower(text)
	if text == "" {
		return defaultYes, nil
	}
	return text == "y" || text == "yes", nil
}

// readLine returns (text, nil) for a normal line, and (text, nil) for the final
// unterminated line before EOF. A closed or exhausted input stream returns
// ("", io.EOF) so callers can cancel instead of retrying forever.
func readLine(reader *bufio.Reader) (string, error) {
	text, err := reader.ReadString('\n')
	if err != nil && err != io.EOF {
		return "", err
	}
	if err == io.EOF && strings.TrimSpace(text) == "" {
		return "", io.EOF
	}
	return strings.TrimSpace(text), nil
}

func failWithMessage(message string) int {
	fmt.Println(message)
	toolkit.WaitForExit()
	return 1
}

func trimCommandOutput(output []byte) string {
	text := strings.TrimSpace(string(output))
	if len(text) <= 4000 {
		return text
	}
	return text[len(text)-4000:]
}
