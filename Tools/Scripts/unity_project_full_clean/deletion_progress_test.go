package unity_project_full_clean

import (
	"context"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"sync/atomic"
	"syscall"
	"testing"
)

// writeDeletionFixture builds a cache-like tree and returns its root:
// "Library/ShaderCache/0" holding the requested number of files.
func writeDeletionFixture(t *testing.T, files int) string {
	t.Helper()
	root := t.TempDir()
	nested := filepath.Join(root, "Library", "ShaderCache", "0")
	if err := os.MkdirAll(nested, 0o755); err != nil {
		t.Fatalf("cannot create fixture directory: %v", err)
	}
	for index := 0; index < files; index++ {
		path := filepath.Join(nested, fmt.Sprintf("shader-%04d.bin", index))
		if err := os.WriteFile(path, []byte("payload"), 0o600); err != nil {
			t.Fatalf("cannot create fixture file: %v", err)
		}
	}
	return root
}

// writeWideDeletionFixture builds "Library/<group>/file-N", so deleting Library
// fans its sibling directories out to the bounded worker pool.
func writeWideDeletionFixture(t *testing.T, groups, filesPerGroup int) string {
	t.Helper()
	root := t.TempDir()
	for group := 0; group < groups; group++ {
		directory := filepath.Join(root, "Library", fmt.Sprintf("group-%02d", group))
		if err := os.MkdirAll(directory, 0o755); err != nil {
			t.Fatalf("cannot create fixture directory: %v", err)
		}
		for index := 0; index < filesPerGroup; index++ {
			path := filepath.Join(directory, fmt.Sprintf("asset-%04d.bin", index))
			if err := os.WriteFile(path, []byte("payload"), 0o600); err != nil {
				t.Fatalf("cannot create fixture file: %v", err)
			}
		}
	}
	return root
}

// 600 files exceed the listing batch, so a second pass reports the same child
// count as the first. A count-only progress check would mistake that for a
// stall; the batch signature must keep the deletion going.
func TestRemoveRootedTreeDeletesBatchedTreeAndReportsProgress(t *testing.T) {
	root := writeDeletionFixture(t, 600)
	osRoot, err := os.OpenRoot(root)
	if err != nil {
		t.Fatalf("cannot open root: %v", err)
	}
	defer osRoot.Close()

	reports := make([]int64, 0, 4)
	var entries atomic.Int64
	if err := removeRootedTree(context.Background(), osRoot, filepath.FromSlash("Library"), 0, 0, &entries, func(removed int64) {
		reports = append(reports, removed)
	}); err != nil {
		t.Fatalf("removeRootedTree failed: %v", err)
	}
	if _, err := os.Lstat(filepath.Join(root, "Library")); !errors.Is(err, os.ErrNotExist) {
		t.Fatalf("Library must be deleted, stat error: %v", err)
	}
	if len(reports) == 0 {
		t.Fatalf("deletion must report progress at least once")
	}
	if entries.Load() == 0 {
		t.Fatalf("deletion must count the entries it removed")
	}
}

// The wide tree is what real Library caches look like: many sibling directories
// at the top level. Run with -race to prove the worker pool shares no state.
func TestRemoveRootedTreeDeletesWideTreeInParallel(t *testing.T) {
	root := writeWideDeletionFixture(t, 8, 40)
	osRoot, err := os.OpenRoot(root)
	if err != nil {
		t.Fatalf("cannot open root: %v", err)
	}
	defer osRoot.Close()

	var entries atomic.Int64
	callCount := 0
	if err := removeRootedTree(context.Background(), osRoot, filepath.FromSlash("Library"), 0, 0, &entries, func(removed int64) {
		callCount++
	}); err != nil {
		t.Fatalf("parallel removeRootedTree failed: %v", err)
	}
	if _, err := os.Lstat(filepath.Join(root, "Library")); !errors.Is(err, os.ErrNotExist) {
		t.Fatalf("Library must be deleted, stat error: %v", err)
	}
	// 8 group directories + 320 files + the Library root itself.
	if want := int64(8 + 8*40 + 1); entries.Load() != want {
		t.Fatalf("entry count = %d, want %d", entries.Load(), want)
	}
}

func TestRemoveRootedTreeHonoursCancellation(t *testing.T) {
	root := writeWideDeletionFixture(t, 4, 5)
	osRoot, err := os.OpenRoot(root)
	if err != nil {
		t.Fatalf("cannot open root: %v", err)
	}
	defer osRoot.Close()

	ctx, cancel := context.WithCancel(context.Background())
	cancel()
	var entries atomic.Int64
	if err := removeRootedTree(ctx, osRoot, filepath.FromSlash("Library"), 0, 0, &entries, nil); !errors.Is(err, context.Canceled) {
		t.Fatalf("a cancelled context must abort deletion, got: %v", err)
	}
	if _, err := os.Lstat(filepath.Join(root, "Library")); err != nil {
		t.Fatalf("a cancelled deletion must leave the tree untouched: %v", err)
	}
}

func TestLockContentionHintOnlyTargetsSharingErrors(t *testing.T) {
	if hint := lockContentionHint(errors.New("unrelated failure")); hint != "" {
		t.Fatalf("generic errors must not gain a hint, got %q", hint)
	}
	if hint := lockContentionHint(syscall.Errno(5)); hint == "" {
		t.Fatalf("ERROR_ACCESS_DENIED must produce an actionable hint")
	}
	if hint := lockContentionHint(syscall.Errno(32)); hint == "" {
		t.Fatalf("ERROR_SHARING_VIOLATION must produce an actionable hint")
	}
	if hint := lockContentionHint(syscall.Errno(2)); hint != "" {
		t.Fatalf("ERROR_FILE_NOT_FOUND must not be reported as lock contention, got %q", hint)
	}
}

func TestDeletionWorkerLimitStaysBounded(t *testing.T) {
	if limit := deletionWorkerLimit(); limit < 2 || limit > 8 {
		t.Fatalf("deletionWorkerLimit = %d, want between 2 and 8", limit)
	}
}
