package unity_project_full_clean

import (
	"bytes"
	"context"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"cyclonegames.tools/scripts/internal/safefs"
	"cyclonegames.tools/scripts/internal/toolkit"
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

// writeBlockedCacheFixture creates exactly two cleanup targets. Their paths
// sort as Library before Logs, so the injected move failure can target a known
// position without depending on the host directory walk order.
func writeBlockedCacheFixture(t *testing.T) (root, library, logs string) {
	t.Helper()
	root = writeUnityProjectFixture(t)
	library = filepath.Join(root, "Library")
	logs = filepath.Join(root, "Logs")
	for _, dir := range []string{library, logs} {
		if err := os.MkdirAll(dir, 0o755); err != nil {
			t.Fatal(err)
		}
	}
	if err := os.WriteFile(filepath.Join(library, "cache.bin"), []byte("cache"), 0o644); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(filepath.Join(logs, "entry.log"), []byte("log"), 0o644); err != nil {
		t.Fatal(err)
	}
	return root, library, logs
}

// A single locked cache must not cancel cleanup of the healthy targets. The
// blocked tree stays untouched; the rest go through the normal quarantine,
// deletion and journal finalization path.
func TestCleanupSkipsBlockedTargetAndCleansTheRest(t *testing.T) {
	root, library, logs := writeBlockedCacheFixture(t)
	original := quarantineMove
	t.Cleanup(func() { quarantineMove = original })
	quarantineMove = func(source, target string) error {
		if filepath.Base(source) == "Library" {
			return fmt.Errorf("%w: %s", safefs.ErrLocked, source)
		}
		return original(source, target)
	}

	var stdout, stderr bytes.Buffer
	code := run([]string{"--project", root, "--ci", "--lock-retry-timeout", "0s"}, &stdout, &stderr)
	if code != toolkit.ExitPartial {
		t.Fatalf("exit = %d, want ExitPartial; stdout:\n%s\nstderr:\n%s", code, stdout.String(), stderr.String())
	}
	if _, err := os.Lstat(library); err != nil {
		t.Fatalf("blocked Library must be left untouched: %v", err)
	}
	if _, err := os.Lstat(logs); !os.IsNotExist(err) {
		t.Fatalf("healthy Logs target must be deleted: %v", err)
	}
	if !strings.Contains(stdout.String(), "[SKIP]") || !strings.Contains(stdout.String(), "[PARTIAL]") {
		t.Fatalf("output must report the skip and the partial outcome:\n%s", stdout.String())
	}
	if !strings.Contains(stdout.String(), "[RESULT] Exit code 3") || !strings.Contains(stdout.String(), "handle.exe") {
		t.Fatalf("partial success must explain its exit code and how to find the holder:\n%s", stdout.String())
	}
	if err := ensureNoStaleCleanupQuarantine(root); err != nil {
		t.Fatalf("quarantine must be finalized after a partial cleanup: %v", err)
	}
}

// --all-or-nothing restores the legacy contract: a blocked target rolls back
// claims already made, so neither cache is removed.
func TestCleanupAllOrNothingRollsBackWhenATargetIsBlocked(t *testing.T) {
	root, library, logs := writeBlockedCacheFixture(t)
	original := quarantineMove
	t.Cleanup(func() { quarantineMove = original })
	calls := 0
	quarantineMove = func(source, target string) error {
		calls++
		if calls == 2 {
			return fmt.Errorf("%w: %s", safefs.ErrLocked, source)
		}
		return original(source, target)
	}

	var stdout, stderr bytes.Buffer
	code := run([]string{"--project", root, "--ci", "--all-or-nothing", "--lock-retry-timeout", "0s"}, &stdout, &stderr)
	if code != toolkit.ExitFailure {
		t.Fatalf("exit = %d, want ExitFailure; stdout:\n%s\nstderr:\n%s", code, stdout.String(), stderr.String())
	}
	if _, err := os.Lstat(library); err != nil {
		t.Fatalf("the first claim must be rolled back to its original path: %v", err)
	}
	if _, err := os.Lstat(logs); err != nil {
		t.Fatalf("a blocked target must stay in place: %v", err)
	}
	if !strings.Contains(stdout.String(), "all claimed paths were restored") {
		t.Fatalf("rollback must be reported to the user:\n%s", stdout.String())
	}
	if !strings.Contains(stdout.String(), "  -> ") || !strings.Contains(stdout.String(), "[RESULT] Exit code 1") {
		t.Fatalf("a failed cleanup must give classified guidance and explain its exit code:\n%s", stdout.String())
	}
	if err := ensureNoStaleCleanupQuarantine(root); err != nil {
		t.Fatalf("quarantine must be finalized after rollback: %v", err)
	}
}

// The read-only occupancy preflight is advisory: it must never fail a cleanup
// and must report nothing for an unoccupied fixture.
func TestOccupancyProbeIsAdvisoryAndBounded(t *testing.T) {
	root := t.TempDir()
	cache := filepath.Join(root, "Logs")
	if err := os.MkdirAll(cache, 0o755); err != nil {
		t.Fatal(err)
	}
	for i := 0; i < 32; i++ {
		if err := os.WriteFile(filepath.Join(cache, fmt.Sprintf("entry-%d.log", i)), []byte("log"), 0o644); err != nil {
			t.Fatal(err)
		}
	}
	info, err := os.Lstat(cache)
	if err != nil {
		t.Fatal(err)
	}
	report := probeCleanItemOccupancy([]cleanItem{{path: cache, kind: "cache directory", identity: info, size: 1}})
	if len(report) != 0 {
		t.Fatalf("an unoccupied fixture must not be reported as held open: %v", report)
	}
}

// A fail-closed outcome must still tell the user what happened and what to do.
// Classification is advisory only and never changes control flow.
func TestExplainFailureGivesActionableGuidance(t *testing.T) {
	cases := []struct {
		name   string
		cause  error
		expect string
	}{
		{"unity started", errors.New("Unity Editor started before physical deletion (PID 42)"), "Close it completely"},
		{"locked", fmt.Errorf("wrapped: %w", safefs.ErrLocked), "Blocked targets are skipped"},
		{"identity drift", errors.New("quarantine identity changed before delete phase"), "Let the editor"},
		{"lease", errors.New("Build workspace lease drifted before claim"), "Another tool run"},
		{"journal", errors.New("cannot persist claim state: disk full"), "free disk space"},
		{"reparse", errors.New("path contains a symbolic link or reparse point"), "Restore the expected layout"},
		{"owner marker", errors.New("owner marker SHA-256 drifted"), "Review the publication marker"},
		{"cancelled", context.Canceled, "No force deletion"},
		{"unknown", errors.New("unclassified condition"), "No cache data was force-deleted"},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			guidance := explainFailure(tc.cause)
			if !strings.Contains(guidance, tc.expect) {
				t.Fatalf("guidance %q must contain %q", guidance, tc.expect)
			}
		})
	}
}

// A mid-deletion failure is the only case where a cache tree can be split. The
// user must be told where the remainder went, why it is not restored and how to
// complete recovery, instead of seeing only a bare error and exit code 1.
func TestPartialDeletionFailureExplainsResidualState(t *testing.T) {
	var out bytes.Buffer
	reportPartialDeletionFailure(&out, fmt.Errorf("wrapped: %w", safefs.ErrLocked),
		"C:/project/Library",
		"C:/project/Temp/BuildPipeline/Workspace/cleanup-quarantine/cleanup-abc")
	text := out.String()
	for _, expected := range []string{
		"[FAIL] Quarantined target deletion failed",
		"original path is now empty",
		"only partially removed",
		"Other targets claimed in this run also remain",
		"NOT moved back",
		"Blocked targets are skipped",
		"--resolve-stale-quarantine",
	} {
		if !strings.Contains(text, expected) {
			t.Fatalf("guidance must mention %q, got:\n%s", expected, text)
		}
	}
}
