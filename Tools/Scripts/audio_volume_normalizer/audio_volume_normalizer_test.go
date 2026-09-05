package audio_volume_normalizer

import (
	"context"
	"os"
	"path/filepath"
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
	if err := processFile(context.Background(), garbage); err == nil {
		t.Fatalf("processFile on unreadable audio must return an error")
	}
}
