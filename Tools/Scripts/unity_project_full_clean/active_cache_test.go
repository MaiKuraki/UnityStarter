package unity_project_full_clean

import (
	"bytes"
	"fmt"
	"os"
	"path/filepath"
	"testing"
	"time"
)

// A cache directory is expected to grow while it is inventoried. Its size and
// timestamps must not be mistaken for a replaced target.
func TestTargetIdentityDriftIgnoresLiveDirectoryMetadata(t *testing.T) {
	root := t.TempDir()
	cache := filepath.Join(root, "Logs")
	if err := os.Mkdir(cache, 0o755); err != nil {
		t.Fatal(err)
	}
	before, err := os.Lstat(cache)
	if err != nil {
		t.Fatal(err)
	}
	for i := 0; i < 32; i++ {
		if err := os.WriteFile(filepath.Join(cache, fmt.Sprintf("entry-%d.log", i)), []byte("log"), 0o644); err != nil {
			t.Fatal(err)
		}
	}
	after, err := os.Lstat(cache)
	if err != nil {
		t.Fatal(err)
	}
	if before.Size() == after.Size() && before.ModTime().Equal(after.ModTime()) {
		t.Log("host did not change directory metadata; the identity assertion still applies")
	}
	if reason := targetIdentityDrift(before, after); reason != "" {
		t.Fatalf("live cache metadata must not be a drift: %s", reason)
	}
}

func TestTargetIdentityDriftDetectsDifferentObject(t *testing.T) {
	root := t.TempDir()
	first := filepath.Join(root, "Logs")
	second := filepath.Join(root, "Other")
	for _, path := range []string{first, second} {
		if err := os.Mkdir(path, 0o755); err != nil {
			t.Fatal(err)
		}
	}
	before, err := os.Lstat(first)
	if err != nil {
		t.Fatal(err)
	}
	after, err := os.Lstat(second)
	if err != nil {
		t.Fatal(err)
	}
	if reason := targetIdentityDrift(before, after); reason == "" {
		t.Fatal("a different directory must be reported as a replacement")
	}
}

func TestTargetIdentityDriftDetectsTypeChange(t *testing.T) {
	root := t.TempDir()
	directory := filepath.Join(root, "cache")
	if err := os.Mkdir(directory, 0o755); err != nil {
		t.Fatal(err)
	}
	before, err := os.Lstat(directory)
	if err != nil {
		t.Fatal(err)
	}
	if err := os.Remove(directory); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(directory, []byte("file"), 0o644); err != nil {
		t.Fatal(err)
	}
	after, err := os.Lstat(directory)
	if err != nil {
		t.Fatal(err)
	}
	if reason := targetIdentityDrift(before, after); reason == "" {
		t.Fatal("replacing a directory with a file must be detected")
	}
}

// End-to-end: a cleanup must complete while a cache root is being written to.
func TestCleanupCompletesWithLiveCacheWrites(t *testing.T) {
	root := writeUnityProjectFixture(t)
	logs := filepath.Join(root, "Logs")
	if err := os.MkdirAll(logs, 0o755); err != nil {
		t.Fatal(err)
	}
	for i := 0; i < 40; i++ {
		if err := os.WriteFile(filepath.Join(logs, fmt.Sprintf("seed-%d.log", i)), []byte("log"), 0o644); err != nil {
			t.Fatal(err)
		}
	}
	stop := make(chan struct{})
	done := make(chan struct{})
	go func() {
		defer close(done)
		for i := 0; ; i++ {
			select {
			case <-stop:
				return
			default:
			}
			path := filepath.Join(logs, fmt.Sprintf("live-%d.tmp", i%16))
			_ = os.WriteFile(path, []byte("live"), 0o644)
			_ = os.Remove(path)
			time.Sleep(time.Millisecond)
		}
	}()

	var stdout, stderr bytes.Buffer
	code := run([]string{"--project", root, "--ci"}, &stdout, &stderr)
	close(stop)
	<-done
	if code != 0 {
		t.Fatalf("exit = %d, stdout:\n%s\nstderr:\n%s", code, stdout.String(), stderr.String())
	}
	if _, err := os.Lstat(logs); !os.IsNotExist(err) {
		t.Fatalf("the live cache directory must be removed: %v", err)
	}
}
