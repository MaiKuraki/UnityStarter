package main

import (
	"bufio"
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"io/fs"
	"math"
	"os"
	"os/exec"
	"path/filepath"
	"regexp"
	"runtime"
	"strconv"
	"strings"
	"time"
)

const (
	defaultAudioBitrate = "128k"
	defaultAudioRate    = "44100"
	ffmpegTimeout       = 12 * time.Hour
	loudnessTolerance   = 0.5
	peakTolerance       = 0.5
	shortAudioThreshold = 3.0
)

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
		CPUUsed:      0,
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
		CPUUsed:      0,
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
	".mp4": true,
	".mov": true,
	".m4v": true,
	".avi": true,
	".mkv": true,
	".webm": true,
	".wmv": true,
	".mpg": true,
	".mpeg": true,
	".ts": true,
	".m2ts": true,
	".flv": true,
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

	durationRegex    = regexp.MustCompile(`Duration:\s+(\d+):(\d+):(\d+)\.(\d+)`)
	maxVolRegex      = regexp.MustCompile(`max_volume:\s+([\-\d.]+)\s+dB`)
	audioStreamRegex = regexp.MustCompile(`Stream\s+#\d+:\d+(?:\[[^\]]+\])?(?:\([^)]+\))?:\s+Audio:`)
)

func main() {
	printIntro()

	if !commandExists("ffmpeg") {
		exitWithMessage("Error: ffmpeg was not found in PATH. Please install ffmpeg and make sure the command is available in your system environment.")
	}

	reader := bufio.NewReader(os.Stdin)

	sourcePath, err := chooseSourcePath(reader)
	if err != nil {
		exitWithMessage(fmt.Sprintf("Cancelled: %v", err))
	}

	info, err := os.Stat(sourcePath)
	if err != nil {
		exitWithMessage(fmt.Sprintf("Failed to read input path: %v", err))
	}

	selectedPreset := choosePreset(reader)
	selectedResolution := chooseResolution(reader)
	settings := chooseVideoBitrate(reader, selectedPreset, selectedResolution)
	outputRoot, overwrite, err := chooseOutputOptions(reader, sourcePath, info, settings.Preset)
	if err != nil {
		exitWithMessage(fmt.Sprintf("Cancelled: %v", err))
	}

	jobs, err := buildJobs(sourcePath, info, outputRoot, settings.Preset)
	if err != nil {
		exitWithMessage(fmt.Sprintf("Failed to build conversion list: %v", err))
	}
	if len(jobs) == 0 {
		exitWithMessage("No supported video files were found to process.")
	}

	fmt.Println()
	fmt.Println("Summary")
	fmt.Printf("  Source:   %s\n", sourcePath)
	fmt.Printf("  Preset:   %s\n", settings.Preset.Name)
	fmt.Printf("  Resolution: %s\n", settings.SelectedResolution.Name)
	fmt.Printf("  Video bitrate: %s\n", settings.VideoBitrate)
	fmt.Printf("  Output:   %s\n", outputRoot)
	fmt.Printf("  Files:    %d\n", len(jobs))
	fmt.Printf("  Overwrite:%t\n", overwrite)
	fmt.Println()
	fmt.Println("Audio note: WebM does not support AAC in the standard container, so this tool uses Vorbis audio plus loudness normalization for broad Unity/WebM compatibility.")

	if !confirm(reader, "Start conversion now? (Y/N) [default: Y]: ", true) {
		exitWithMessage("Operation cancelled by user.")
	}

	successCount := 0
	skipCount := 0
	failCount := 0

	for index, item := range jobs {
		fmt.Println()
		fmt.Printf("[%d/%d] %s\n", index+1, len(jobs), filepath.Base(item.InputPath))

		if !overwrite {
			if _, statErr := os.Stat(item.OutputPath); statErr == nil {
				fmt.Printf("  Skipped: output already exists -> %s\n", item.OutputPath)
				skipCount++
				continue
			}
		}

		if err := os.MkdirAll(filepath.Dir(item.OutputPath), 0o755); err != nil {
			fmt.Printf("  Failed: create output directory failed: %v\n", err)
			failCount++
			continue
		}

		if err := convertVideo(item.InputPath, item.OutputPath, settings); err != nil {
			fmt.Printf("  Failed: %v\n", err)
			failCount++
			continue
		}

		fmt.Printf("  Done: %s\n", item.OutputPath)
		successCount++
	}

	fmt.Println()
	fmt.Println("Finished")
	fmt.Printf("  Succeeded: %d\n", successCount)
	fmt.Printf("  Skipped:   %d\n", skipCount)
	fmt.Printf("  Failed:    %d\n", failCount)
	waitForExit()
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
		fmt.Print("Enter a video file or folder path. Press Enter to open a dialog on Windows: ")
		text, err := readLine(reader)
		if err != nil {
			return "", err
		}

		if text == "" && runtime.GOOS == "windows" {
			path, dialogErr := choosePathWithWindowsDialog(reader)
			if dialogErr != nil {
				fmt.Printf("Dialog error: %v\n", dialogErr)
				continue
			}
			if path == "" {
				continue
			}
			text = path
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

func choosePathWithWindowsDialog(reader *bufio.Reader) (string, error) {
	fmt.Print("Open file dialog or folder dialog? (F/D) [default: F]: ")
	choice, err := readLine(reader)
	if err != nil {
		return "", err
	}

	if strings.EqualFold(strings.TrimSpace(choice), "d") {
		return runWindowsDialog(folderDialogScript())
	}
	return runWindowsDialog(fileDialogScript())
}

func choosePreset(reader *bufio.Reader) preset {
	fmt.Println("Quality presets")
	for _, p := range presets {
		fmt.Printf("  %s. %s\n", p.Key, p.Name)
		fmt.Printf("     %s\n", p.Description)
		fmt.Printf("     Output suffix: %s.webm\n", p.Suffix)
	}

	fmt.Print("Select preset (1/2/3) [default: 2]: ")
	input, err := readLine(reader)
	if err != nil {
		return presets[1]
	}
	input = strings.TrimSpace(input)
	if input == "" {
		return presets[1]
	}

	for _, p := range presets {
		if p.Key == input {
			return p
		}
	}

	fmt.Println("Unknown preset selection, using Balanced / Universal.")
	return presets[1]
}

func chooseResolution(reader *bufio.Reader) resolutionOption {
	fmt.Println("Resolution options")
	for _, option := range resolutionOptions {
		fmt.Printf("  %s. %s\n", option.Key, option.Name)
		fmt.Printf("     %s\n", option.Description)
	}

	fmt.Print("Select resolution option (1/2/3/4) [default: 1]: ")
	input, err := readLine(reader)
	if err != nil {
		return resolutionOptions[0]
	}
	input = strings.TrimSpace(input)
	if input == "" {
		return resolutionOptions[0]
	}

	for _, option := range resolutionOptions {
		if option.Key == input {
			return option
		}
	}

	fmt.Println("Unknown resolution selection, using Original.")
	return resolutionOptions[0]
}

func chooseVideoBitrate(reader *bufio.Reader, selectedPreset preset, selectedResolution resolutionOption) encodeSettings {
	fmt.Printf("Video bitrate [default: %s, examples: 12M / 8500k]: ", selectedPreset.VideoBitrate)
	input, err := readLine(reader)
	if err != nil {
		return buildEncodeSettings(selectedPreset, selectedResolution, selectedPreset.VideoBitrate)
	}

	input = strings.TrimSpace(input)
	if input == "" {
		return buildEncodeSettings(selectedPreset, selectedResolution, selectedPreset.VideoBitrate)
	}

	normalized, ok := normalizeBitrateInput(input)
	if !ok {
		fmt.Printf("Unknown bitrate format '%s', using default %s.\n", input, selectedPreset.VideoBitrate)
		return buildEncodeSettings(selectedPreset, selectedResolution, selectedPreset.VideoBitrate)
	}

	return buildEncodeSettings(selectedPreset, selectedResolution, normalized)
}

func buildEncodeSettings(selectedPreset preset, selectedResolution resolutionOption, videoBitrate string) encodeSettings {
	return encodeSettings{
		Preset:             selectedPreset,
		SelectedResolution: selectedResolution,
		VideoBitrate:       videoBitrate,
		VideoMaxRate:       scaleBitrate(videoBitrate, 1.5),
		VideoBufSize:       scaleBitrate(videoBitrate, 2.0),
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

	overwrite := confirm(reader, "Overwrite existing outputs? (y/N) [default: N]: ", false)
	return outputRoot, overwrite, nil
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

	return jobs, err
}

func convertVideo(inputPath string, outputPath string, settings encodeSettings) error {
	tempOutput := outputPath + ".partial.webm"
	_ = os.Remove(tempOutput)

	info, err := inspectMedia(inputPath)
	if err != nil {
		return err
	}

	audioArgs, err := buildAudioArgs(inputPath, info, settings.Preset)
	if err != nil {
		return err
	}

	args := []string{
		"-y",
		"-hide_banner",
		"-i", inputPath,
		"-map_metadata", "-1",
		"-map_chapters", "-1",
		"-map", "0:v:0",
		"-map", "0:a:0?",
		"-sn",
		"-pix_fmt", "yuv420p",
		"-c:v", "libvpx",
		"-crf", fmt.Sprintf("%d", settings.Preset.CRF),
		"-b:v", settings.VideoBitrate,
		"-maxrate", settings.VideoMaxRate,
		"-bufsize", settings.VideoBufSize,
		"-deadline", "good",
		"-cpu-used", fmt.Sprintf("%d", settings.Preset.CPUUsed),
		"-threads", "0",
		"-g", "120",
		"-lag-in-frames", "16",
		"-auto-alt-ref", "1",
	}
	if scaleFilter := buildScaleFilter(settings.SelectedResolution); scaleFilter != "" {
		args = append(args, "-vf", scaleFilter)
	}
	args = append(args, audioArgs...)
	args = append(args, tempOutput)

	ctx, cancel := context.WithTimeout(context.Background(), ffmpegTimeout)
	defer cancel()

	cmd := exec.CommandContext(ctx, "ffmpeg", args...)
	output, err := cmd.CombinedOutput()
	if ctx.Err() == context.DeadlineExceeded {
		_ = os.Remove(tempOutput)
		return fmt.Errorf("ffmpeg timed out after %s", ffmpegTimeout)
	}
	if err != nil {
		_ = os.Remove(tempOutput)
		return fmt.Errorf("ffmpeg error: %w\n%s", err, trimCommandOutput(output))
	}

	if err := os.Rename(tempOutput, outputPath); err != nil {
		_ = os.Remove(tempOutput)
		return fmt.Errorf("failed to finalize output file: %w", err)
	}
	return nil
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

func inspectMedia(inputPath string) (mediaInfo, error) {
	category := detectCategory(inputPath)
	args := []string{
		"-hide_banner",
		"-i", inputPath,
		"-map", "0:v:0",
		"-map", "0:a:0?",
		"-f", "null",
		"-",
	}

	ctx, cancel := context.WithTimeout(context.Background(), ffmpegTimeout)
	defer cancel()

	cmd := exec.CommandContext(ctx, "ffmpeg", args...)
	var stderr bytes.Buffer
	cmd.Stdout = io.Discard
	cmd.Stderr = &stderr
	_ = cmd.Run()

	if ctx.Err() == context.DeadlineExceeded {
		return mediaInfo{}, fmt.Errorf("ffmpeg media inspection timed out after %s", ffmpegTimeout)
	}

	output := stderr.String()
	return mediaInfo{
		DurationSec:      parseDuration(output),
		HasAudio:         audioStreamRegex.MatchString(output),
		DetectedCategory: category,
	}, nil
}

func buildAudioArgs(inputPath string, info mediaInfo, selectedPreset preset) ([]string, error) {
	if !info.HasAudio {
		return []string{"-an"}, nil
	}

	audioFilter, err := buildNormalizedAudioFilter(inputPath, info)
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

func buildNormalizedAudioFilter(inputPath string, info mediaInfo) (string, error) {
	if info.DurationSec >= 0 && info.DurationSec < shortAudioThreshold {
		return buildPeakNormalizationFilter(inputPath, info.DetectedCategory)
	}
	return buildLoudnormFilter(inputPath, info.DetectedCategory)
}

func buildPeakNormalizationFilter(inputPath string, category audioCategory) (string, error) {
	ctx, cancel := context.WithTimeout(context.Background(), ffmpegTimeout)
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

func buildLoudnormFilter(inputPath string, category audioCategory) (string, error) {
	pass1Filter := fmt.Sprintf("loudnorm=I=%.1f:TP=%.1f:LRA=11:print_format=json", category.TargetLUFS, category.TargetTP)

	ctx, cancel := context.WithTimeout(context.Background(), ffmpegTimeout)
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

func confirm(reader *bufio.Reader, prompt string, defaultYes bool) bool {
	fmt.Print(prompt)
	text, err := readLine(reader)
	if err != nil {
		return defaultYes
	}

	text = strings.ToLower(strings.TrimSpace(text))
	if text == "" {
		return defaultYes
	}
	return text == "y" || text == "yes"
}

func readLine(reader *bufio.Reader) (string, error) {
	text, err := reader.ReadString('\n')
	if err != nil && err != io.EOF {
		return "", err
	}
	return strings.TrimSpace(text), nil
}

func waitForExit() {
	fmt.Println()
	fmt.Print("Press Enter to exit...")
	_, _ = bufio.NewReader(os.Stdin).ReadString('\n')
}

func exitWithMessage(message string) {
	fmt.Println(message)
	waitForExit()
	os.Exit(1)
}

func runWindowsDialog(script string) (string, error) {
	cmd := exec.Command("powershell", "-NoProfile", "-STA", "-Command", script)
	output, err := cmd.CombinedOutput()
	if err != nil {
		return "", fmt.Errorf("%w: %s", err, trimCommandOutput(output))
	}
	return strings.TrimSpace(string(output)), nil
}

func fileDialogScript() string {
	return `
Add-Type -AssemblyName System.Windows.Forms
$dialog = New-Object System.Windows.Forms.OpenFileDialog
$dialog.Title = 'Select a source video'
$dialog.Filter = 'Video Files|*.mp4;*.mov;*.m4v;*.avi;*.mkv;*.webm;*.wmv;*.mpg;*.mpeg;*.ts;*.m2ts;*.flv|All Files|*.*'
$dialog.Multiselect = $false
if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
    [Console]::Write($dialog.FileName)
}`
}

func folderDialogScript() string {
	return `
Add-Type -AssemblyName System.Windows.Forms
$dialog = New-Object System.Windows.Forms.FolderBrowserDialog
$dialog.Description = 'Select a folder containing source videos'
$dialog.ShowNewFolderButton = $false
if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
    [Console]::Write($dialog.SelectedPath)
}`
}

func trimCommandOutput(output []byte) string {
	text := strings.TrimSpace(string(output))
	if len(text) <= 4000 {
		return text
	}
	return text[len(text)-4000:]
}
