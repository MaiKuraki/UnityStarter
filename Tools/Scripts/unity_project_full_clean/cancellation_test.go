package unity_project_full_clean

import (
	"context"
	"errors"
	"os"
	"path/filepath"
	"sync/atomic"
	"testing"
)

// A saturated or cancelled deletion pool must return promptly instead of
// blocking on a semaphore that ignores the context.
func TestRemoveChildrenHonoursCancellation(t *testing.T) {
	root := t.TempDir()
	for i := 0; i < 12; i++ {
		child := filepath.Join(root, "child-"+string(rune('a'+i)))
		if err := os.MkdirAll(child, 0o755); err != nil {
			t.Fatal(err)
		}
	}
	bound, err := os.OpenRoot(root)
	if err != nil {
		t.Fatal(err)
	}
	defer func() { _ = bound.Close() }()
	children, err := os.ReadDir(root)
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithCancel(context.Background())
	cancel()
	var removed atomic.Int64
	err = removeChildren(ctx, bound, ".", 0, children, &removed, func(int64) {})
	if !errors.Is(err, context.Canceled) {
		t.Fatalf("removeChildren must return the cancellation, got %v", err)
	}
	if len(globalDeletionSlots) != 0 {
		t.Fatalf("cancellation leaked %d semaphore slots", len(globalDeletionSlots))
	}
}

func TestSameStringSet(t *testing.T) {
	if !sameStringSet([]string{"b", "a"}, []string{"a", "b"}) {
		t.Fatal("order must not matter")
	}
	if sameStringSet([]string{"a"}, []string{"a", "b"}) {
		t.Fatal("different lengths must not match")
	}
	if sameStringSet([]string{"a"}, []string{"b"}) {
		t.Fatal("different members must not match")
	}
}
