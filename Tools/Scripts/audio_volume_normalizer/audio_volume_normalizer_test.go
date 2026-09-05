package audio_volume_normalizer

import (
	"context"
	"os"
	"path/filepath"
	"slices"
	"strings"
	"testing"
)

// TestProcessFileFailsClosedOnUnreadableAudio guards the pass-1 error-swallowing
// regression: an analysis failure must surface as an error instead of falling
// through to a confusing downstream message. The error path is identical with
// or without ffmpeg installed (LookPath failure vs. decode failure), so the
// test is meaningful on every machine; ffmpeg end-to-end behavior is not
// asserted here.
func TestProcessFileFailsClosedOnUnreadableAudio(t *testing.T) {
	tempDir := t.TempDir()
	garbage := filepath.Join(tempDir, "broken.wav")
	if err := os.WriteFile(garbage, []byte("this is not audio data"), 0o644); err != nil {
		t.Fatalf("cannot write garbage audio file: %v", err)
	}
	if err := processFile(context.Background(), garbage, filepath.Join(tempDir, "broken_normalized.wav")); err == nil {
		t.Fatalf("processFile on unreadable audio must return an error")
	}
}

// Two sources sharing a basename (tone.wav + tone.flac) map to one output;
// the plan must refuse instead of letting the second normalization overwrite
// the first while both jobs report success.
func TestCollectAudioJobsDetectsCollisions(t *testing.T) {
	dir := t.TempDir()
	for _, name := range []string{"tone.wav", "tone.flac", "other.ogg"} {
		if err := os.WriteFile(filepath.Join(dir, name), []byte("stub"), 0o644); err != nil {
			t.Fatalf("cannot write fixture %s: %v", name, err)
		}
	}
	originalFormat := selectedFormat
	selectedFormat = formatWAV
	defer func() { selectedFormat = originalFormat }()

	_, err := collectAudioJobs(dir)
	if err == nil {
		t.Fatalf("collectAudioJobs must reject same-basename sources")
	}
	if !strings.Contains(err.Error(), "output collision detected") {
		t.Fatalf("error must describe the collision, got: %v", err)
	}
	if !strings.Contains(err.Error(), "tone.wav") || !strings.Contains(err.Error(), "tone.flac") {
		t.Fatalf("error must name both colliding sources, got: %v", err)
	}
}

func TestCollectAudioJobsSkipsGeneratedSuffix(t *testing.T) {
	dir := t.TempDir()
	for _, name := range []string{"gunshot.wav", "gunshot_normalized.wav"} {
		if err := os.WriteFile(filepath.Join(dir, name), []byte("stub"), 0o644); err != nil {
			t.Fatalf("cannot write fixture %s: %v", name, err)
		}
	}
	originalFormat := selectedFormat
	selectedFormat = formatWAV
	defer func() { selectedFormat = originalFormat }()

	jobs, err := collectAudioJobs(dir)
	if err != nil {
		t.Fatalf("collectAudioJobs failed: %v", err)
	}
	if len(jobs) != 1 {
		t.Fatalf("job count = %d, want 1 (generated output must be skipped)", len(jobs))
	}
	if !strings.HasSuffix(jobs[0].outputPath, "gunshot_normalized.wav") {
		t.Fatalf("planned output = %s, want the generated-suffix destination", jobs[0].outputPath)
	}
}

func TestBuildAudioEncodeArgsMonoFold(t *testing.T) {
	originalFormat := selectedFormat
	originalMono := monoOutput
	selectedFormat = formatWAV
	defer func() {
		selectedFormat = originalFormat
		monoOutput = originalMono
	}()

	monoOutput = false
	stereo := buildAudioEncodeArgs(48000)
	if slices.Contains(stereo, "1") && slices.Contains(stereo, "-ac") {
		t.Fatalf("default args must not fold channels: %v", stereo)
	}
	if !slices.Contains(stereo, "48000") {
		t.Fatalf("default args must cap the sample rate: %v", stereo)
	}

	monoOutput = true
	mono := buildAudioEncodeArgs(48000)
	if !slices.Contains(mono, "-ac") || !slices.Contains(mono, "1") {
		t.Fatalf("mono fold must add -ac 1: %v", mono)
	}
}

func TestRunRejectsNonPositiveFFmpegTimeout(t *testing.T) {
	code := Run([]string{"--ci", "--ffmpeg-timeout", "0s"})
	if code != 2 {
		t.Fatalf("exit code = %d, want 2 for --ffmpeg-timeout 0s", code)
	}
}
