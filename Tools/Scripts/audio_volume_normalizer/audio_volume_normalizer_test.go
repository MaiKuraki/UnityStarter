package audio_volume_normalizer

import (
	"context"
	"os"
	"path/filepath"
	"slices"
	"strings"
	"testing"
	"time"

	"cyclonegames.tools/scripts/internal/toolkit"
)

func testOptions() Options {
	return Options{
		FFmpegPath:    "no-binary-configured",
		FFmpegTimeout: 2 * time.Second,
		Format:        formatWAV,
		Workers:       1,
	}
}

// An analysis failure must surface as an error instead of falling through to a
// downstream media conclusion. A missing executable is deterministic on every
// machine and exercises the same fail-closed path as a decode failure.
func TestProcessFileFailsClosedOnUnreadableAudio(t *testing.T) {
	dir := t.TempDir()
	garbage := filepath.Join(dir, "broken.wav")
	if err := os.WriteFile(garbage, []byte("this is not audio data"), 0o644); err != nil {
		t.Fatalf("cannot write garbage audio file: %v", err)
	}
	opts := testOptions()
	opts.FFmpegPath = filepath.Join(dir, "no-such-ffmpeg")
	if err := processFile(context.Background(), opts, garbage, filepath.Join(dir, "broken_normalized.wav")); err == nil {
		t.Fatalf("processFile on unreadable audio must return an error")
	}
}

// A start failure must never be rewritten into a media-analysis verdict.
func TestProcessFileReportsStartFailureWithoutMediaConclusion(t *testing.T) {
	dir := t.TempDir()
	garbage := filepath.Join(dir, "broken.wav")
	if err := os.WriteFile(garbage, []byte("this is not audio data"), 0o644); err != nil {
		t.Fatalf("cannot write garbage audio file: %v", err)
	}
	opts := testOptions()
	opts.FFmpegPath = filepath.Join(dir, "no-such-ffmpeg")
	err := processFile(context.Background(), opts, garbage, filepath.Join(dir, "broken_normalized.wav"))
	if err == nil {
		t.Fatalf("processFile must report the start failure")
	}
	if !strings.Contains(err.Error(), "cannot start external command") {
		t.Fatalf("error must expose the start failure, got: %v", err)
	}
	if strings.Contains(err.Error(), "could not parse sample rate") {
		t.Fatalf("start failure must not become a media conclusion, got: %v", err)
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
	_, err := collectAudioJobs(dir, testOptions())
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
	jobs, err := collectAudioJobs(dir, testOptions())
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
	opts := testOptions()
	stereo := buildAudioEncodeArgs(opts, 48000)
	if slices.Contains(stereo, "1") && slices.Contains(stereo, "-ac") {
		t.Fatalf("default args must not fold channels: %v", stereo)
	}
	if !slices.Contains(stereo, "48000") {
		t.Fatalf("default args must cap the sample rate: %v", stereo)
	}

	opts.Mono = true
	mono := buildAudioEncodeArgs(opts, 48000)
	if !slices.Contains(mono, "-ac") || !slices.Contains(mono, "1") {
		t.Fatalf("mono fold must add -ac 1: %v", mono)
	}
}

func TestRunRejectsNonPositiveFFmpegTimeout(t *testing.T) {
	code := Run([]string{"--ci", "--ffmpeg-timeout", "0s"})
	if code != toolkit.ExitUsage {
		t.Fatalf("exit code = %d, want %d for --ffmpeg-timeout 0s", code, toolkit.ExitUsage)
	}
}

// Resolution failures must reach the user verbatim and abort the run.
func TestRunReportsUnresolvedFFmpeg(t *testing.T) {
	t.Setenv("FFMPEG", filepath.Join(t.TempDir(), "missing-ffmpeg"))
	code := Run([]string{"--ci", "--input", t.TempDir()})
	if code == toolkit.ExitSuccess {
		t.Fatalf("exit code = %d, want a failure when ffmpeg cannot be resolved", code)
	}
}
