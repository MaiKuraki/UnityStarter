package main

import (
	"bufio"
	"bytes"
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
)

// --- CONFIGURATION PARAMETERS ---
const TARGET_LUFS = -16.0
const TARGET_TP = -1.5
const MAX_BITRATE = 320000
const MAX_SAMPLERATE = 48000
const FILENAME_SUFFIX = "_normalized"
const LOUDNESS_TOLERANCE = 0.5 // Skip files within +/- 0.5 LUFS of target

var WORKER_COUNT = runtime.NumCPU()

// --- CORE SCRIPT LOGIC ---
var audioExtensions = map[string]bool{
	".mp3": true, ".wav": true, ".flac": true, ".m4a": true, ".aac": true,
	".ogg": true, ".wma": true, ".opus": true,
}

// NEW: Regex to parse stream info from ffmpeg's output.
var (
	sampleRateRegex = regexp.MustCompile(`(\d+)\s+Hz`)
	bitRateRegex    = regexp.MustCompile(`(\d+)\s+kb/s`)
)

// NEW: Custom error to indicate a file is already normalized.
var ErrAlreadyNormalized = errors.New("file is already within the target loudness range")

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
	fmt.Println("--- LoudNorm: Batch Audio Normalizer ---")
	fmt.Println("\n[ About This Tool ]")
	fmt.Println("This script normalizes audio files to a standard perceived loudness.")
	fmt.Printf("It uses a two-pass method to adjust loudness to %.1f LUFS for high quality results.\n", TARGET_LUFS)

	fmt.Println("\n[ How It Works ]")
	fmt.Println("1. It will recursively scan the current directory for audio files.")
	fmt.Println("2. For each audio file, it will create a new '.ogg' file with the '_normalized' suffix.")
	fmt.Println("3. Existing '_normalized.ogg' files will be overwritten.")
	fmt.Println("4. IMPORTANT: This tool requires FFmpeg to be installed and accessible in your system's PATH.")

	fmt.Printf("\nDo you want to proceed? (Y/N): ")

	scanner := bufio.NewScanner(os.Stdin)
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

	// Verify that ffmpeg and ffprobe are available in the system's PATH.
	if !commandExists("ffmpeg") || !commandExists("ffprobe") {
		log.Println("Error: Could not find ffmpeg or ffprobe. Please ensure FFmpeg is installed and added to your system's PATH.")
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
	var failedFiles []string
	var skippedFiles []string
	for res := range results {
		atomic.AddInt32(&processedFiles, 1)
		if res.err == nil {
			successfulFiles = append(successfulFiles, res.path)
		} else if errors.Is(res.err, ErrAlreadyNormalized) {
			skippedFiles = append(skippedFiles, res.path)
		} else {
			failedFiles = append(failedFiles, res.path)
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
		for _, file := range failedFiles {
			fmt.Printf("  - %s\n", file)
		}
	} else {
		fmt.Println("  (None)")
	}
	// --- End of Summary ---

	waitForExit()
}

// worker is a concurrent processor for handling normalization jobs.
func worker(id int, wg *sync.WaitGroup, jobs <-chan job, results chan<- result) {
	defer wg.Done()
	for j := range jobs {
		// Processing messages are now disabled to keep the progress bar clean.
		// fmt.Printf("[Worker %d] Processing: %s\n", id, filepath.Base(j.path))
		err := processFileTwoPass(j.path)
		// No error logging here for skipped files, as it's not a "failure".
		if err != nil && !errors.Is(err, ErrAlreadyNormalized) {
			log.Printf("ERROR: Failed to process %s: %v\n", filepath.Base(j.path), err)
		}
		results <- result{path: j.path, err: err}
	}
}

// processFileTwoPass performs the two-pass loudness normalization on a single file.
func processFileTwoPass(filePath string) error {
	// --- Step 1: FFmpeg First Pass (Analysis) ---
	// This single pass now replaces the separate ffprobe call.
	loudnormFilterPass1 := fmt.Sprintf("loudnorm=I=%.1f:TP=%.1f:LRA=11:print_format=json", TARGET_LUFS, TARGET_TP)
	cmdPass1 := exec.Command("ffmpeg", "-i", filePath, "-af", loudnormFilterPass1, "-f", "null", "-")
	var pass1Stderr bytes.Buffer
	cmdPass1.Stderr = &pass1Stderr
	_ = cmdPass1.Run() // We ignore the error here because ffmpeg with -f null might return a non-zero exit code.
	pass1Output := pass1Stderr.String()

	// --- Step 2: Extract Loudness Info AND Stream Info from the same output ---
	lnInfo, err := extractLoudnormInfo(pass1Output)
	if err != nil {
		return fmt.Errorf("failed to extract loudness info: %w", err)
	}

	// --- NEW: Check if the file is already within the target loudness range ---
	measuredLufs, err := strconv.ParseFloat(lnInfo.InputI, 64)
	if err == nil {
		if math.Abs(measuredLufs-TARGET_LUFS) <= LOUDNESS_TOLERANCE {
			return ErrAlreadyNormalized
		}
	}
	// --- End of check ---

	// OPTIMIZATION: Parse stream info from ffmpeg output instead of using ffprobe.
	sampleRateMatch := sampleRateRegex.FindStringSubmatch(pass1Output)
	bitRateMatch := bitRateRegex.FindStringSubmatch(pass1Output)
	if len(sampleRateMatch) < 2 {
		return fmt.Errorf("could not parse sample rate from ffmpeg output")
	}
	originalSampleRate, _ := strconv.Atoi(sampleRateMatch[1])
	originalBitRate := 0 // Default bitrate
	if len(bitRateMatch) >= 2 {
		originalBitRate, _ = strconv.Atoi(bitRateMatch[1])
		originalBitRate *= 1000 // convert kb/s to b/s
	}

	// --- Step 3: FFmpeg Second Pass (Application) ---

	targetSampleRate := originalSampleRate
	if originalSampleRate > MAX_SAMPLERATE || originalSampleRate == 0 {
		targetSampleRate = MAX_SAMPLERATE
	}

	targetBitRateStr := fmt.Sprintf("%d", originalBitRate)
	if originalBitRate > MAX_BITRATE || originalBitRate == 0 {
		targetBitRateStr = fmt.Sprintf("%d", MAX_BITRATE)
	}

	outputFilePath := strings.TrimSuffix(filePath, filepath.Ext(filePath)) + FILENAME_SUFFIX + ".ogg"

	loudnormFilterPass2 := fmt.Sprintf(
		"loudnorm=I=%.1f:TP=%.1f:LRA=11:measured_I=%s:measured_TP=%s:measured_LRA=%s:measured_thresh=%s:offset=%s",
		TARGET_LUFS, TARGET_TP, lnInfo.InputI, lnInfo.InputTP, lnInfo.InputLRA, lnInfo.InputThresh, lnInfo.TargetOffset,
	)

	cmdPass2 := exec.Command("ffmpeg",
		"-y",
		"-i", filePath,
		"-af", loudnormFilterPass2,
		"-c:a", "libvorbis",
		"-b:a", targetBitRateStr,
		"-ar", strconv.Itoa(targetSampleRate),
		outputFilePath,
	)
	var pass2Stderr bytes.Buffer
	cmdPass2.Stderr = &pass2Stderr
	if err := cmdPass2.Run(); err != nil {
		return fmt.Errorf("ffmpeg second pass failed: %w\nOutput:\n%s", err, pass2Stderr.String())
	}

	return nil
}

func extractLoudnormInfo(stderr string) (*LoudnormInfo, error) {
	lines := strings.Split(stderr, "\n")
	jsonText := ""
	inJsonBlock := false
	for _, line := range lines {
		if strings.HasPrefix(strings.TrimSpace(line), "{") {
			inJsonBlock = true
		}
		if inJsonBlock {
			jsonText += line + "\n"
		}
		if strings.HasPrefix(strings.TrimSpace(line), "}") {
			break
		}
	}

	if jsonText == "" {
		return nil, fmt.Errorf("could not find JSON block in ffmpeg stderr")
	}

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
