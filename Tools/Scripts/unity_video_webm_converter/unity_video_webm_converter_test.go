package unity_video_webm_converter

import (
	"bufio"
	"errors"
	"io"
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
	if _, err := chooseVideoBitrate(reader, presets[1], resolutionOptions[0]); !errors.Is(err, io.EOF) {
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
