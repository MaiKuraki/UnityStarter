package safefs

import (
	"context"
	"errors"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func TestHostPathPolicyLimits(t *testing.T) {
	policy := HostPathPolicy()
	if policy.MaxComponentBytes <= 0 || policy.MaxPathBytes <= 0 {
		t.Fatalf("invalid host policy: %+v", policy)
	}
	if err := policy.ValidateComponent("valid-name.bin"); err != nil {
		t.Fatalf("a plain name must be accepted: %v", err)
	}
	if err := policy.ValidateComponent(""); !errors.Is(err, ErrInvalidName) {
		t.Fatalf("an empty component must be rejected: %v", err)
	}
	if err := policy.ValidateComponent(".."); !errors.Is(err, ErrInvalidName) {
		t.Fatalf("a traversal component must be rejected: %v", err)
	}
	if err := policy.ValidateComponent(strings.Repeat("a", policy.MaxComponentBytes+1)); !errors.Is(err, ErrInvalidName) {
		t.Fatal("an oversized component must be rejected")
	}
	if err := policy.ValidateComponent("bad\x01name"); !errors.Is(err, ErrInvalidName) {
		t.Fatal("a control character must be rejected")
	}
}

func TestValidateRelativeRejectsTraversalAndRootedPaths(t *testing.T) {
	policy := HostPathPolicy()
	for _, value := range []string{"", "../outside", "a/../../outside", "/absolute", "C:/absolute"} {
		if err := policy.ValidateRelative(value); err == nil {
			t.Fatalf("ValidateRelative(%q) must fail", value)
		}
	}
	if err := policy.ValidateRelative("Assets/Scripts/Player.cs"); err != nil {
		t.Fatalf("a portable relative path must be accepted: %v", err)
	}
}

func TestValidateAbsoluteAndBudget(t *testing.T) {
	policy := HostPathPolicy()
	root := t.TempDir()
	if err := policy.ValidateAbsolute(root); err != nil {
		t.Fatalf("a normal absolute path must be accepted: %v", err)
	}
	if err := policy.Budget(root, "output", "frame-0001.partial.webm"); err != nil {
		t.Fatalf("a short generated suffix must fit: %v", err)
	}
	huge := strings.Repeat("a", extendedPathLimit()+1)
	if err := policy.ValidateAbsolute(filepath.Join(root, huge)); !errors.Is(err, ErrPathTooLong) {
		t.Fatalf("an over-limit absolute path must be rejected: %v", err)
	}
	if err := policy.ValidateAbsolute("relative/path"); !errors.Is(err, ErrInvalidName) {
		t.Fatalf("a relative path must be rejected by ValidateAbsolute: %v", err)
	}
}

func TestEnsureWritable(t *testing.T) {
	policy := HostPathPolicy()
	root := t.TempDir()
	if err := policy.EnsureWritable(root); err != nil {
		t.Fatalf("a temp directory must be writable: %v", err)
	}
	if err := policy.EnsureWritable(filepath.Join(root, "missing")); !errors.Is(err, ErrNotWritable) {
		t.Fatalf("a missing directory must be reported as not writable: %v", err)
	}
	entries, err := os.ReadDir(root)
	if err != nil {
		t.Fatal(err)
	}
	if len(entries) != 0 {
		t.Fatalf("the write probe must leave no files behind: %v", entries)
	}
}

func TestFactsProbeAndComparisons(t *testing.T) {
	root := t.TempDir()
	facts, err := Probe(root)
	if err != nil {
		t.Fatalf("probe failed: %v", err)
	}
	if !facts.Probed {
		t.Fatal("a successful probe must be marked Probed")
	}
	if !EqualPath(filepath.Join(root, "A"), filepath.Join(root, "A"), facts) {
		t.Fatal("identical paths must compare equal")
	}
	if !IsWithin(root, filepath.Join(root, "Assets", "file.txt"), facts) {
		t.Fatal("a child path must be within its root")
	}
	if IsWithin(root, root, facts) {
		t.Fatal("a root is not strictly within itself")
	}
	if IsWithin(root, filepath.Join(root, "..", "escape"), facts) {
		t.Fatal("a sibling path must not be within the root")
	}
	upper := filepath.Join(root, "CASEPROBE")
	lower := filepath.Join(root, "caseprobe")
	if !facts.CaseSensitive && !EqualPath(upper, lower, facts) {
		t.Fatal("a case-insensitive volume must fold case")
	}
	if facts.CaseSensitive && EqualPath(upper, lower, facts) {
		t.Fatal("a case-sensitive volume must distinguish case")
	}
	if err := ValidateComponentLength(strings.Repeat("x", 256)); err == nil {
		t.Fatal("a 256-byte component must be rejected")
	}
}

func TestPublishReplaceAndNoReplace(t *testing.T) {
	root := t.TempDir()
	target := filepath.Join(root, "output.bin")
	if err := os.WriteFile(target, []byte("first"), 0o644); err != nil {
		t.Fatal(err)
	}
	stage := filepath.Join(root, ".output.partial-1")
	if err := os.WriteFile(stage, []byte("second"), 0o644); err != nil {
		t.Fatal(err)
	}
	if err := PublishReplace(stage, target); err != nil {
		t.Fatalf("PublishReplace failed: %v", err)
	}
	data, err := os.ReadFile(target)
	if err != nil || string(data) != "second" {
		t.Fatalf("target = %q, %v; want second", data, err)
	}
	if _, err := os.Lstat(stage); !errors.Is(err, os.ErrNotExist) {
		t.Fatal("the stage must be consumed")
	}

	otherStage := filepath.Join(root, ".output.partial-2")
	if err := os.WriteFile(otherStage, []byte("third"), 0o644); err != nil {
		t.Fatal(err)
	}
	if err := PublishNoReplace(otherStage, target); err == nil {
		t.Fatal("PublishNoReplace must refuse an existing target")
	}
	if data, _ := os.ReadFile(target); string(data) != "second" {
		t.Fatalf("PublishNoReplace must not modify the target: %q", data)
	}
}

func TestPublishReplaceClearsReadOnlyTarget(t *testing.T) {
	if !HostPathPolicy().SupportsLongPrefix {
		return
	}
	root := t.TempDir()
	target := filepath.Join(root, "readonly.bin")
	if err := os.WriteFile(target, []byte("old"), 0o444); err != nil {
		t.Fatal(err)
	}
	stage := filepath.Join(root, ".readonly.partial")
	if err := os.WriteFile(stage, []byte("new"), 0o644); err != nil {
		t.Fatal(err)
	}
	if err := PublishReplace(stage, target); err != nil {
		t.Fatalf("a read-only target must be replaceable: %v", err)
	}
	if data, _ := os.ReadFile(target); string(data) != "new" {
		t.Fatalf("target = %q, want new", data)
	}
}

func TestRetryOnLock(t *testing.T) {
	calls := 0
	err := RetryOnLock(context.Background(), 3, 0, func() error {
		calls++
		if calls < 3 {
			return ErrLocked
		}
		return nil
	})
	if err != nil || calls != 3 {
		t.Fatalf("RetryOnLock = %v after %d calls", err, calls)
	}
	calls = 0
	err = RetryOnLock(context.Background(), 3, 0, func() error {
		calls++
		return os.ErrInvalid
	})
	if !errors.Is(err, os.ErrInvalid) || calls != 1 {
		t.Fatalf("a non-lock error must stop immediately: %v after %d calls", err, calls)
	}
}
