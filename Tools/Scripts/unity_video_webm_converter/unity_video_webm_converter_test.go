package unity_video_webm_converter

import (
	"bufio"
	"context"
	"errors"
	"io"
	"os"
	"path/filepath"
	"slices"
	"strings"
	"testing"
	"time"

	"cyclonegames.tools/scripts/internal/toolkit"
)

func TestReadLineReturnsDataLine(t *testing.T) {
	reader := bufio.NewReader(strings.NewReader("C:\\videos\\clip.mp4\n"))
	text, err := readLine(reader)
	if err != nil || text != "C:\\videos\\clip.mp4" {
		t.Fatalf("readLine = (%q, %v), want data line without error", text, err)
	}
}

func TestReadLineAcceptsFinalUnterminatedLine(t *testing.T) {
	reader := bufio.NewReader(strings.NewReader("last line without newline"))
	text, err := readLine(reader)
	if err != nil || text != "last line without newline" {
		t.Fatalf("readLine = (%q, %v), want final partial line without error", text, err)
	}
}

// TestReadLineEOFReturnsError guards the busy-loop regression: a closed input
// stream must surface as an error so every interactive prompt cancels instead
// of retrying forever.
func TestReadLineEOFReturnsError(t *testing.T) {
	reader := bufio.NewReader(strings.NewReader(""))
	if _, err := readLine(reader); !errors.Is(err, io.EOF) {
		t.Fatalf("readLine error = %v, want io.EOF on closed input", err)
	}
}

// TestChooseSourcePathTerminatesOnEOF ensures the prompt loop exits instead of
// spinning when stdin is exhausted (redirect, script, or closed console input).
func TestChooseSourcePathTerminatesOnEOF(t *testing.T) {
	reader := bufio.NewReader(strings.NewReader(""))
	done := make(chan struct{})
	var err error
	go func() {
		defer close(done)
		_, err = chooseSourcePath(reader)
	}()
	select {
	case <-done:
		if !errors.Is(err, io.EOF) {
			t.Fatalf("chooseSourcePath error = %v, want io.EOF", err)
		}
	case <-time.After(5 * time.Second):
		t.Fatalf("chooseSourcePath did not terminate on EOF (busy-loop regression)")
	}
}

func TestConfirmPropagatesEOFCancellation(t *testing.T) {
	reader := bufio.NewReader(strings.NewReader(""))
	confirmed, err := confirm(reader, "Proceed? ", true)
	if err == nil {
		t.Fatalf("confirm on closed input must return an error so defaults are not silently applied")
	}
	if confirmed {
		t.Fatalf("confirm on closed input must not report consent")
	}
}

func TestChoosePresetTerminatesOnEOF(t *testing.T) {
	reader := bufio.NewReader(strings.NewReader(""))
	if _, err := choosePreset(reader); !errors.Is(err, io.EOF) {
		t.Fatalf("choosePreset error = %v, want io.EOF", err)
	}
}

func TestChooseResolutionTerminatesOnEOF(t *testing.T) {
	reader := bufio.NewReader(strings.NewReader(""))
	if _, err := chooseResolution(reader); !errors.Is(err, io.EOF) {
		t.Fatalf("chooseResolution error = %v, want io.EOF", err)
	}
}

func TestChooseVideoBitrateTerminatesOnEOF(t *testing.T) {
	reader := bufio.NewReader(strings.NewReader(""))
	if _, err := chooseVideoBitrate(reader, presets[1], resolutionOptions[0], false); !errors.Is(err, io.EOF) {
		t.Fatalf("chooseVideoBitrate error = %v, want io.EOF", err)
	}
}

// A forced resolution failure must reach the user and abort the run.
func TestRunReportsUnresolvedFFmpeg(t *testing.T) {
	t.Setenv("FFMPEG", filepath.Join(t.TempDir(), "missing-ffmpeg"))
	code := Run([]string{"--ci", "--input", t.TempDir(), "--output", t.TempDir()})
	if code == toolkit.ExitSuccess {
		t.Fatalf("exit code = %d, want a failure when ffmpeg cannot be resolved", code)
	}
}

func TestRunRejectsNonPositiveFFmpegTimeout(t *testing.T) {
	code := Run([]string{"--ci", "--ffmpeg-timeout", "0s"})
	if code != toolkit.ExitUsage {
		t.Fatalf("exit code = %d, want %d for --ffmpeg-timeout 0s", code, toolkit.ExitUsage)
	}
}

// Two sources sharing a basename (clip.mp4 + clip.mov) map to one output;
// the plan must refuse instead of letting the second encode overwrite the first.
func TestBuildJobsDetectsOutputCollisions(t *testing.T) {
	dir := t.TempDir()
	for _, name := range []string{"clip.mp4", "clip.mov", "other.mp4"} {
		if err := os.WriteFile(filepath.Join(dir, name), []byte("stub"), 0o644); err != nil {
			t.Fatalf("cannot write fixture %s: %v", name, err)
		}
	}
	info, err := os.Stat(dir)
	if err != nil {
		t.Fatalf("cannot stat fixture dir: %v", err)
	}
	_, buildErr := buildJobs(dir, info, filepath.Join(dir, "out"), presets[1])
	if buildErr == nil {
		t.Fatalf("buildJobs must reject same-basename sources")
	}
	if !strings.Contains(buildErr.Error(), "output collision detected") {
		t.Fatalf("error must describe the collision, got: %v", buildErr)
	}
	if !strings.Contains(buildErr.Error(), "clip.mp4") || !strings.Contains(buildErr.Error(), "clip.mov") {
		t.Fatalf("error must name both colliding sources, got: %v", buildErr)
	}
}

func TestBuildJobsAcceptsDistinctBasenames(t *testing.T) {
	dir := t.TempDir()
	for _, name := range []string{"clip.mp4", "other.mp4"} {
		if err := os.WriteFile(filepath.Join(dir, name), []byte("stub"), 0o644); err != nil {
			t.Fatalf("cannot write fixture %s: %v", name, err)
		}
	}
	info, err := os.Stat(dir)
	if err != nil {
		t.Fatalf("cannot stat fixture dir: %v", err)
	}
	jobs, buildErr := buildJobs(dir, info, filepath.Join(dir, "out"), presets[1])
	if buildErr != nil {
		t.Fatalf("buildJobs failed on distinct basenames: %v", buildErr)
	}
	if len(jobs) != 2 {
		t.Fatalf("job count = %d, want 2", len(jobs))
	}
}

// Outputs from every preset must be excluded, so switching preset and rerunning
// never feeds a prior conversion back in as a source.
func TestBuildJobsSkipsAllPresetSuffixes(t *testing.T) {
	dir := t.TempDir()
	names := []string{"clip.mp4"}
	for _, p := range presets {
		names = append(names, "clip"+p.Suffix+".webm")
	}
	for _, name := range names {
		if err := os.WriteFile(filepath.Join(dir, name), []byte("stub"), 0o644); err != nil {
			t.Fatalf("cannot write fixture %s: %v", name, err)
		}
	}
	info, err := os.Stat(dir)
	if err != nil {
		t.Fatalf("cannot stat fixture dir: %v", err)
	}
	jobs, buildErr := buildJobs(dir, info, filepath.Join(dir, "out"), presets[2])
	if buildErr != nil {
		t.Fatalf("buildJobs failed: %v", buildErr)
	}
	if len(jobs) != 1 {
		t.Fatalf("job count = %d, want 1 (all preset outputs must be skipped)", len(jobs))
	}
	if !strings.HasSuffix(jobs[0].InputPath, "clip.mp4") {
		t.Fatalf("planned input = %s, want the original source", jobs[0].InputPath)
	}
}

func TestBuildVideoEncodeArgsAlphaChannel(t *testing.T) {
	plain := buildVideoEncodeArgs(encodeSettings{Preset: presets[1]}, 4)
	if !slices.Contains(plain, "yuv420p") || slices.Contains(plain, "yuva420p") {
		t.Fatalf("default encode must use yuv420p without alpha metadata: %v", plain)
	}
	if value, ok := flagValue(plain, "-auto-alt-ref"); !ok || value != "1" {
		t.Fatalf("default encode must keep auto-alt-ref=1, got %q (ok=%v)", value, ok)
	}

	alpha := buildVideoEncodeArgs(encodeSettings{Preset: presets[1], AlphaOutput: true}, 4)
	if !slices.Contains(alpha, "yuva420p") {
		t.Fatalf("alpha encode must select yuva420p: %v", alpha)
	}
	if !slices.Contains(alpha, "alpha_mode=1") {
		t.Fatalf("alpha encode must declare alpha_mode metadata: %v", alpha)
	}
	// libvpx cannot open an alpha encoder while auto-alt-ref is enabled.
	if value, ok := flagValue(alpha, "-auto-alt-ref"); !ok || value != "0" {
		t.Fatalf("alpha encode must disable auto-alt-ref, got %q (ok=%v)", value, ok)
	}
}

func flagValue(args []string, flag string) (string, bool) {
	for index, arg := range args {
		if arg == flag && index+1 < len(args) {
			return args[index+1], true
		}
	}
	return "", false
}

func TestComputeThreadsPerEncodeBounds(t *testing.T) {
	if got := computeThreadsPerEncode(8, 4); got != 2 {
		t.Fatalf("8 cores / 4 workers = %d, want 2", got)
	}
	if got := computeThreadsPerEncode(2, 8); got != 1 {
		t.Fatalf("2 cores / 8 workers = %d, want the floor of 1", got)
	}
	if got := computeThreadsPerEncode(32, 2); got != 8 {
		t.Fatalf("32 cores / 2 workers = %d, want the cap of 8", got)
	}
	if got := computeThreadsPerEncode(0, 0); got != 1 {
		t.Fatalf("degenerate input must fall back to 1, got %d", got)
	}
}

func TestWeightForSizeBounds(t *testing.T) {
	if got := weightForSize(0, 4); got != 1 {
		t.Fatalf("small file weight = %d, want 1", got)
	}
	if got := weightForSize(300<<20, 4); got != 2 {
		t.Fatalf("medium file weight = %d, want 2", got)
	}
	if got := weightForSize(2<<30, 4); got != 3 {
		t.Fatalf("large file weight = %d, want 3", got)
	}
	if got := weightForSize(2<<30, 2); got != 2 {
		t.Fatalf("weight must be capped by pool capacity, got %d", got)
	}
}

func TestFFmpegProgressWriterParsesCarriageReturnStream(t *testing.T) {
	writer := newFFmpegProgressWriter()
	// ffmpeg emits progress separated by \r, not \n; the writer must keep up.
	chunk := "frame= 10 fps=8.0 time=00:00:01.00\rframe= 20 fps=9.0 time=00:00:02.50\rframe= 30"
	if _, err := writer.Write([]byte(chunk)); err != nil {
		t.Fatalf("Write failed: %v", err)
	}
	if got := writer.lastEncodedSeconds(); got != 2.5 {
		t.Fatalf("lastEncodedSeconds = %v, want 2.5 (last completed timecode)", got)
	}
}

func TestParseTimecodeSeconds(t *testing.T) {
	seconds, err := parseTimecodeSeconds("01:02:03.50")
	if err != nil {
		t.Fatalf("parseTimecodeSeconds failed: %v", err)
	}
	if seconds != 3723.5 {
		t.Fatalf("parseTimecodeSeconds = %v, want 3723.5", seconds)
	}
	if _, err := parseTimecodeSeconds("bad"); err == nil {
		t.Fatalf("invalid timecode must be rejected")
	}
}

func missingBinaryOptions() Options {
	return Options{FFmpegPath: filepath.Join(os.TempDir(), "no-such-ffmpeg-binary-for-tests"), FFmpegTimeout: time.Second}
}

// A start failure during header inspection must not become a media conclusion.
func TestProbeHeaderStartFailureNotRewrittenAsMediaConclusion(t *testing.T) {
	_, err := probeWithFFmpegHeader(context.Background(), missingBinaryOptions(), "clip.mp4")
	if err == nil || !strings.Contains(err.Error(), "cannot start external command") {
		t.Fatalf("error must expose the start failure, got: %v", err)
	}
	if strings.Contains(err.Error(), "no video stream found") {
		t.Fatalf("start failure must not become a media conclusion, got: %v", err)
	}
}

// A start failure during loudnorm analysis must not become a missing-JSON verdict.
func TestLoudnormStartFailureNotRewrittenAsMediaConclusion(t *testing.T) {
	_, err := buildLoudnormFilter(context.Background(), missingBinaryOptions(), "clip.mp4", categoryDefault)
	if err == nil || !strings.Contains(err.Error(), "cannot start external command") {
		t.Fatalf("error must expose the start failure, got: %v", err)
	}
	if strings.Contains(err.Error(), "could not find loudnorm JSON block") {
		t.Fatalf("start failure must not become a media conclusion, got: %v", err)
	}
}

// A start failure during peak analysis must not become a peak-detection verdict.
func TestPeakStartFailureNotRewrittenAsMediaConclusion(t *testing.T) {
	_, err := buildPeakNormalizationFilter(context.Background(), missingBinaryOptions(), "clip.mp4", categoryDefault)
	if err == nil || !strings.Contains(err.Error(), "cannot start external command") {
		t.Fatalf("error must expose the start failure, got: %v", err)
	}
	if strings.Contains(err.Error(), "could not detect peak volume") {
		t.Fatalf("start failure must not become a media conclusion, got: %v", err)
	}
}
