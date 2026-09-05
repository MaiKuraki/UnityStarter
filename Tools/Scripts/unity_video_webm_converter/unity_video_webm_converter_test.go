package unity_video_webm_converter

import (
	"bufio"
	"errors"
	"io"
	"os"
	"path/filepath"
	"slices"
	"strings"
	"testing"
	"time"
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

func TestRunCiWithoutFfmpegFailsFast(t *testing.T) {
	if commandExists("ffmpeg") {
		t.Skip("real ffmpeg is installed; the missing-binary branch cannot be exercised")
	}
	code := Run([]string{"--ci", "--input", t.TempDir(), "--output", t.TempDir()})
	if code != 1 {
		t.Fatalf("exit code = %d, want 1 when ffmpeg is absent", code)
	}
}

func TestRunRejectsNonPositiveFFmpegTimeout(t *testing.T) {
	// --ffmpeg-timeout must reject non-positive values with the usage code.
	original := ffmpegTimeout
	defer func() { ffmpegTimeout = original }()
	code := Run([]string{"--ci", "--ffmpeg-timeout", "0s"})
	if code != 2 {
		t.Fatalf("exit code = %d, want 2 for --ffmpeg-timeout 0s", code)
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
