package main

import (
	"bufio"
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"log"
	"math"
	"os"
	"os/exec"
	"path/filepath"
	"regexp"
	"runtime"
	"strconv"
	"strings"
	"sync"
	"sync/atomic"
	"time"
)

// --- CONFIGURATION PARAMETERS ---
const MAX_SAMPLERATE = 48000
const FILENAME_SUFFIX = "_normalized"
const LOUDNESS_TOLERANCE = 0.5  // Skip files within +/- 0.5 LUFS of target
const PEAK_TOLERANCE = 0.5     // Skip files within +/- 0.5 dB of target peak (for SFX)
const FFMPEG_TIMEOUT = 10 * time.Minute

// Short audio threshold: files shorter than this use peak normalization instead of LUFS.
// LUFS (ITU-R BS.1770) requires >= 400ms for reliable measurement.
// We use a higher threshold to be safe with game SFX.
const SHORT_AUDIO_THRESHOLD_SEC = 3.0

// Audio category loudness targets.
// These can be matched via parent folder name (case-insensitive), e.g.:
//   Audio/Music/battle_theme.wav  -> categoryMusic
//   Audio/SFX/gunshot.wav         -> categorySFX
//   Audio/Voice/dialog_01.wav     -> categoryVoice
//   Audio/Ambient/wind.wav        -> categoryAmbient
//   Audio/other.wav               -> categoryDefault
type audioCategory struct {
	name       string
	targetLUFS float64
	targetTP   float64
	targetPeak float64 // For peak normalization on short files
}

var (
	categoryMusic   = audioCategory{name: "Music", targetLUFS: -14.0, targetTP: -1.0, targetPeak: -1.0}
	categoryVoice   = audioCategory{name: "Voice", targetLUFS: -16.0, targetTP: -1.5, targetPeak: -1.0}
	categorySFX     = audioCategory{name: "SFX", targetLUFS: -14.0, targetTP: -1.0, targetPeak: -1.0}
	categoryAmbient = audioCategory{name: "Ambient", targetLUFS: -20.0, targetTP: -1.5, targetPeak: -3.0}
	categoryDefault = audioCategory{name: "Default", targetLUFS: -16.0, targetTP: -1.5, targetPeak: -1.0}
)

// Folder name to category mapping (case-insensitive).
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

var WORKER_COUNT = runtime.NumCPU()

// --- CORE SCRIPT LOGIC ---
var audioExtensions = map[string]bool{
	".mp3": true, ".wav": true, ".flac": true, ".m4a": true, ".aac": true,
	".ogg": true, ".wma": true, ".opus": true,
}

var (
	// Regex to parse stream info from ffmpeg's stream description line.
	sampleRateRegex = regexp.MustCompile(`Stream\s+#\d+:\d+.*?(\d+)\s+Hz`)
	// Regex to parse duration from ffmpeg output.
	durationRegex = regexp.MustCompile(`Duration:\s+(\d+):(\d+):(\d+)\.(\d+)`)
	// Regex to parse max_volume from ffmpeg volumedetect output.
	maxVolRegex = regexp.MustCompile(`max_volume:\s+([\-\d.]+)\s+dB`)
)

var ErrAlreadyNormalized = errors.New("file is already within the target loudness range")

// outputFormat holds the user's chosen output encoding settings.
type outputFormat struct {
	name      string   // Display name
	ext       string   // File extension (with dot)
	ffmpegArgs []string // ffmpeg codec/quality arguments
}

var (
	formatWAV = outputFormat{
		name:      "WAV (Lossless PCM 16-bit)",
		ext:       ".wav",
		ffmpegArgs: []string{"-c:a", "pcm_s16le"},
	}
	formatOGG = outputFormat{
		name:      "OGG (Vorbis VBR Quality 6)",
		ext:       ".ogg",
		ffmpegArgs: []string{"-c:a", "libvorbis", "-q:a", "6"},
	}
)

// selectedFormat is set during startup based on user choice.
var selectedFormat outputFormat

type LoudnormInfo struct {
	InputI       string `json:"input_i"`
	InputTP      string `json:"input_tp"`
	InputLRA     string `json:"input_lra"`
	InputThresh  string `json:"input_thresh"`
	TargetOffset string `json:"target_offset"`
}

type job struct {
	path string
}

// NEW: result struct to hold processing outcome.
type result struct {
	path string
	err  error
}

// NEW: This function displays the intro and asks for user confirmation.
func displayIntroAndConfirm() bool {
	scanner := bufio.NewScanner(os.Stdin)

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

	// --- Output Format Selection ---
	fmt.Println("\n[ Output Format ]")
	fmt.Println("  1. WAV  - Lossless (recommended: let Unity handle final compression)")
	fmt.Println("  2. OGG  - Vorbis VBR (smaller files, use when disk/memory matters)")
	fmt.Printf("\nSelect output format (1 or 2) [default: 1]: ")
	scanner.Scan()
	formatChoice := strings.TrimSpace(scanner.Text())
	switch formatChoice {
	case "2":
		selectedFormat = formatOGG
	default:
		selectedFormat = formatWAV
	}
	fmt.Printf("  -> Selected: %s\n", selectedFormat.name)

	fmt.Println("\n[ How It Works ]")
	fmt.Println("1. It will recursively scan the current directory for audio files.")
	fmt.Printf("2. For each audio file, it will create a new '%s' file with the '%s' suffix.\n", selectedFormat.ext, FILENAME_SUFFIX)
	if selectedFormat.ext == ".wav" {
		fmt.Println("3. Output is lossless WAV to avoid double compression when Unity re-encodes on import.")
	} else {
		fmt.Println("3. Output is OGG Vorbis — smaller files, but may double-compress if Unity re-encodes.")
	}
	fmt.Printf("4. Existing '%s%s' files will be overwritten.\n", FILENAME_SUFFIX, selectedFormat.ext)
	fmt.Println("5. IMPORTANT: This tool requires FFmpeg to be installed and accessible in your system's PATH.")

	fmt.Printf("\nDo you want to proceed? (Y/N): ")
	scanner.Scan()
	response := strings.TrimSpace(scanner.Text())

	return strings.ToLower(response) == "y"
}

// waitForExit function pauses until the user presses Enter.
func waitForExit() {
	fmt.Println("\nPress Enter to exit...")
	bufio.NewReader(os.Stdin).ReadBytes('\n')
}

func main() {
	// NEW: Display the introduction and wait for confirmation before doing anything else.
	if !displayIntroAndConfirm() {
		fmt.Println("Operation cancelled by user.")
		waitForExit()
		return
	}

	fmt.Println("\nUser confirmed. Starting process...")

	// Verify that ffmpeg is available in the system's PATH.
	if !commandExists("ffmpeg") {
		log.Println("Error: Could not find ffmpeg. Please ensure FFmpeg is installed and added to your system's PATH.")
		waitForExit()
		return
	}

	// Get the current working directory to start the scan.
	rootDir, err := os.Getwd()
	if err != nil {
		log.Printf("Failed to get current working directory: %v\n", err)
		waitForExit()
		return
	}
	fmt.Printf("Scanning for audio files in [%s] and its subdirectories...\n", rootDir)

	// --- NEW: First pass to count files for the progress bar ---
	var totalFiles int32
	err = filepath.Walk(rootDir, func(path string, info os.FileInfo, err error) error {
		if err != nil {
			return err
		}
		if !info.IsDir() && isAudioFile(path) && !strings.HasSuffix(strings.TrimSuffix(path, filepath.Ext(path)), FILENAME_SUFFIX) {
			atomic.AddInt32(&totalFiles, 1)
		}
		return nil
	})
	if err != nil {
		log.Printf("Error during initial file scan: %v\n", err)
		waitForExit()
		return
	}
	if totalFiles == 0 {
		fmt.Println("No audio files found to process.")
		waitForExit()
		return
	}
	fmt.Printf("Found %d audio files to process.\n\n", totalFiles)
	// --- End of file counting ---

	// Set up a concurrent processing pool.
	var wg sync.WaitGroup
	jobs := make(chan job)
	results := make(chan result)
	var processedFiles int32

	// Start the worker goroutines.
	for i := 0; i < WORKER_COUNT; i++ {
		wg.Add(1)
		go worker(i+1, &wg, jobs, results)
	}

	// Start a goroutine to walk the directory and dispatch jobs.
	go func() {
		defer close(jobs)
		filepath.Walk(rootDir, func(path string, info os.FileInfo, err error) error {
			if err != nil {
				return err
			}
			if !info.IsDir() && isAudioFile(path) && !strings.HasSuffix(strings.TrimSuffix(path, filepath.Ext(path)), FILENAME_SUFFIX) {
				jobs <- job{path: path}
			}
			return nil
		})
	}()

	// Start a goroutine to close the results channel once all workers are done.
	go func() {
		wg.Wait()
		close(results)
	}()

	// Collect results from the workers and display progress.
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
		// --- NEW: Progress Bar ---
		printProgressBar(atomic.LoadInt32(&processedFiles), totalFiles)
	}

	fmt.Println("\nAll tasks completed!")

	// --- NEW: Print Processing Summary ---
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

	waitForExit()
}

// worker is a concurrent processor for handling normalization jobs.
func worker(id int, wg *sync.WaitGroup, jobs <-chan job, results chan<- result) {
	defer wg.Done()
	for j := range jobs {
		err := processFile(j.path)
		results <- result{path: j.path, err: err}
	}
}

// detectCategory determines the audio category based on parent folder names.
func detectCategory(filePath string) audioCategory {
	dir := filepath.Dir(filePath)
	parts := strings.Split(filepath.ToSlash(dir), "/")
	// Walk from deepest folder upward to find the most specific match.
	for i := len(parts) - 1; i >= 0; i-- {
		folderLower := strings.ToLower(parts[i])
		if cat, ok := categoryKeywords[folderLower]; ok {
			return cat
		}
	}
	return categoryDefault
}

// parseDuration extracts audio duration in seconds from ffmpeg stderr output.
func parseDuration(ffmpegOutput string) float64 {
	m := durationRegex.FindStringSubmatch(ffmpegOutput)
	if len(m) < 5 {
		return -1 // Unknown duration
	}
	hours, _ := strconv.ParseFloat(m[1], 64)
	minutes, _ := strconv.ParseFloat(m[2], 64)
	seconds, _ := strconv.ParseFloat(m[3], 64)
	centiseconds, _ := strconv.ParseFloat(m[4], 64)
	return hours*3600 + minutes*60 + seconds + centiseconds/100
}

// processFile normalizes a single audio file using the appropriate strategy.
func processFile(filePath string) error {
	cat := detectCategory(filePath)

	// --- Step 1: FFmpeg First Pass (Analysis) ---
	loudnormFilterPass1 := fmt.Sprintf("loudnorm=I=%.1f:TP=%.1f:LRA=11:print_format=json", cat.targetLUFS, cat.targetTP)
	ctx1, cancel1 := context.WithTimeout(context.Background(), FFMPEG_TIMEOUT)
	defer cancel1()
	cmdPass1 := exec.CommandContext(ctx1, "ffmpeg", "-i", filePath, "-af", loudnormFilterPass1, "-f", "null", "-")
	var pass1Stderr bytes.Buffer
	cmdPass1.Stderr = &pass1Stderr
	if err := cmdPass1.Run(); err != nil {
		if ctx1.Err() == context.DeadlineExceeded {
			return fmt.Errorf("ffmpeg analysis timed out after %v", FFMPEG_TIMEOUT)
		}
	}
	pass1Output := pass1Stderr.String()

	// Determine audio duration to choose normalization strategy.
	duration := parseDuration(pass1Output)

	// Parse sample rate.
	sampleRateMatch := sampleRateRegex.FindStringSubmatch(pass1Output)
	if len(sampleRateMatch) < 2 {
		return fmt.Errorf("could not parse sample rate from ffmpeg output")
	}
	originalSampleRate, _ := strconv.Atoi(sampleRateMatch[1])
	targetSampleRate := originalSampleRate
	if originalSampleRate > MAX_SAMPLERATE || originalSampleRate == 0 {
		targetSampleRate = MAX_SAMPLERATE
	}

	outputFilePath := strings.TrimSuffix(filePath, filepath.Ext(filePath)) + FILENAME_SUFFIX + selectedFormat.ext

	// --- Choose Strategy ---
	if duration >= 0 && duration < SHORT_AUDIO_THRESHOLD_SEC {
		return processShortAudio(filePath, outputFilePath, pass1Output, cat, targetSampleRate)
	}
	return processLongAudio(filePath, outputFilePath, pass1Output, cat, targetSampleRate)
}

// processShortAudio uses peak normalization for short sound effects.
// LUFS measurement is unreliable for audio < 400ms and not ideal for short SFX in general.
func processShortAudio(filePath, outputFilePath, ffmpegOutput string, cat audioCategory, targetSampleRate int) error {
	// Use ffmpeg's volumedetect to get peak level.
	ctx, cancel := context.WithTimeout(context.Background(), FFMPEG_TIMEOUT)
	defer cancel()
	cmdDetect := exec.CommandContext(ctx, "ffmpeg", "-i", filePath, "-af", "volumedetect", "-f", "null", "-")
	var detectStderr bytes.Buffer
	cmdDetect.Stderr = &detectStderr
	if err := cmdDetect.Run(); err != nil {
		if ctx.Err() == context.DeadlineExceeded {
			return fmt.Errorf("ffmpeg volumedetect timed out after %v", FFMPEG_TIMEOUT)
		}
	}
	detectOutput := detectStderr.String()

	// Extract max_volume from volumedetect output.
	match := maxVolRegex.FindStringSubmatch(detectOutput)
	if len(match) < 2 {
		return fmt.Errorf("could not detect peak volume")
	}
	maxVolume, err := strconv.ParseFloat(match[1], 64)
	if err != nil {
		return fmt.Errorf("could not parse peak volume: %w", err)
	}

	// Calculate gain needed to reach target peak.
	gainNeeded := cat.targetPeak - maxVolume

	// Skip silent files.
	if math.IsInf(maxVolume, -1) {
		return fmt.Errorf("file appears to be silent, skipping")
	}

	// Skip if already close enough.
	if math.Abs(gainNeeded) <= PEAK_TOLERANCE {
		return ErrAlreadyNormalized
	}

	// Apply simple volume gain and output.
	// -map_metadata -1: Strip all metadata (ID3 tags, etc.) from output.
	// WAV's RIFF INFO chunk only supports ASCII/Latin-1, so non-ASCII metadata
	// (e.g. Japanese, Chinese) from source MP3/FLAC would become mojibake.
	// Game audio metadata is discarded by Unity on import anyway.
	volumeFilter := fmt.Sprintf("volume=%.2fdB", gainNeeded)
	ctx2, cancel2 := context.WithTimeout(context.Background(), FFMPEG_TIMEOUT)
	defer cancel2()
	args := []string{"-y", "-i", filePath, "-map_metadata", "-1", "-af", volumeFilter}
	args = append(args, selectedFormat.ffmpegArgs...)
	args = append(args, "-ar", strconv.Itoa(targetSampleRate), outputFilePath)
	cmdApply := exec.CommandContext(ctx2, "ffmpeg", args...)
	var applyStderr bytes.Buffer
	cmdApply.Stderr = &applyStderr
	if err := cmdApply.Run(); err != nil {
		if ctx2.Err() == context.DeadlineExceeded {
			return fmt.Errorf("ffmpeg peak normalize timed out after %v", FFMPEG_TIMEOUT)
		}
		return fmt.Errorf("ffmpeg peak normalization failed: %w\nOutput:\n%s", err, applyStderr.String())
	}
	return nil
}

// processLongAudio uses two-pass LUFS normalization with linear mode.
func processLongAudio(filePath, outputFilePath, pass1Output string, cat audioCategory, targetSampleRate int) error {
	lnInfo, err := extractLoudnormInfo(pass1Output)
	if err != nil {
		return fmt.Errorf("failed to extract loudness info: %w", err)
	}

	// Check if the file is already within the target loudness range.
	measuredLufs, err := strconv.ParseFloat(lnInfo.InputI, 64)
	if err == nil {
		if math.IsInf(measuredLufs, -1) {
			return fmt.Errorf("file appears to be silent (measured LUFS: -inf), skipping")
		}
		if math.Abs(measuredLufs-cat.targetLUFS) <= LOUDNESS_TOLERANCE {
			return ErrAlreadyNormalized
		}
	}

	// --- Second Pass: Apply loudnorm with linear=true ---
	// -map_metadata -1: Strip all metadata to avoid encoding issues (see processShortAudio).
	loudnormFilterPass2 := fmt.Sprintf(
		"loudnorm=I=%.1f:TP=%.1f:LRA=11:measured_I=%s:measured_TP=%s:measured_LRA=%s:measured_thresh=%s:offset=%s:linear=true",
		cat.targetLUFS, cat.targetTP, lnInfo.InputI, lnInfo.InputTP, lnInfo.InputLRA, lnInfo.InputThresh, lnInfo.TargetOffset,
	)

	ctx2, cancel2 := context.WithTimeout(context.Background(), FFMPEG_TIMEOUT)
	defer cancel2()
	args := []string{"-y", "-i", filePath, "-map_metadata", "-1", "-af", loudnormFilterPass2}
	args = append(args, selectedFormat.ffmpegArgs...)
	args = append(args, "-ar", strconv.Itoa(targetSampleRate), outputFilePath)
	cmdPass2 := exec.CommandContext(ctx2, "ffmpeg", args...)
	var pass2Stderr bytes.Buffer
	cmdPass2.Stderr = &pass2Stderr
	if err := cmdPass2.Run(); err != nil {
		if ctx2.Err() == context.DeadlineExceeded {
			return fmt.Errorf("ffmpeg second pass timed out after %v", FFMPEG_TIMEOUT)
		}
		return fmt.Errorf("ffmpeg second pass failed: %w\nOutput:\n%s", err, pass2Stderr.String())
	}

	return nil
}

func extractLoudnormInfo(stderr string) (*LoudnormInfo, error) {
	// The loudnorm JSON block is the LAST JSON object in ffmpeg's stderr output.
	// We must scan backwards to avoid accidentally matching metadata or other output.
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
	ext := strings.ToLower(filepath.Ext(path))
	return audioExtensions[ext]
}

func commandExists(cmd string) bool {
	_, err := exec.LookPath(cmd)
	return err == nil
}

// NEW: printProgressBar function to draw the progress bar.
func printProgressBar(current, total int32) {
	barLength := 40
	percent := float64(current) / float64(total)
	filledLength := int(float64(barLength) * percent)

	bar := strings.Repeat("█", filledLength) + strings.Repeat("-", barLength-filledLength)
	fmt.Printf("\r[%s] %.0f%% (%d/%d)", bar, percent*100, current, total)
	if current == total {
		fmt.Println() // Newline at the end
	}
}
