package safefs

import (
	"os"
	"path/filepath"
	"testing"
)

// TestMoveNoReplaceRefusesExistingTarget guards the core durability contract:
// the platform move must never replace an existing target.
func TestMoveNoReplaceRefusesExistingTarget(t *testing.T) {
	root := t.TempDir()
	source := filepath.Join(root, "source.txt")
	target := filepath.Join(root, "target.txt")
	if err := os.WriteFile(source, []byte("source"), 0o644); err != nil {
		t.Fatalf("cannot write source: %v", err)
	}
	if err := os.WriteFile(target, []byte("target"), 0o644); err != nil {
		t.Fatalf("cannot write target: %v", err)
	}
	if err := MoveNoReplace(source, target); err == nil {
		t.Fatalf("MoveNoReplace must fail when the target already exists")
	}
	sourceContent, err := os.ReadFile(source)
	if err != nil || string(sourceContent) != "source" {
		t.Fatalf("source must remain intact after a refused move: %v", err)
	}
	targetContent, err := os.ReadFile(target)
	if err != nil || string(targetContent) != "target" {
		t.Fatalf("target must remain intact after a refused move: %v", err)
	}
}

func TestMoveNoReplaceSucceedsToAbsentTarget(t *testing.T) {
	root := t.TempDir()
	source := filepath.Join(root, "source.txt")
	target := filepath.Join(root, "sub", "target.txt")
	if err := os.WriteFile(source, []byte("payload"), 0o644); err != nil {
		t.Fatalf("cannot write source: %v", err)
	}
	if err := os.Mkdir(filepath.Join(root, "sub"), 0o755); err != nil {
		t.Fatalf("cannot create subdirectory: %v", err)
	}
	if err := MoveNoReplace(source, target); err != nil {
		t.Fatalf("MoveNoReplace failed: %v", err)
	}
	content, err := os.ReadFile(target)
	if err != nil || string(content) != "payload" {
		t.Fatalf("target content mismatch: %v", err)
	}
	if _, err := os.Stat(source); !os.IsNotExist(err) {
		t.Fatalf("source must be gone after a successful move, stat error = %v", err)
	}
}

func TestPublishFileNoReplaceIsAtomicAndExclusive(t *testing.T) {
	root := t.TempDir()
	stage := filepath.Join(root, ".stage")
	target := filepath.Join(root, "canonical.txt")
	if err := os.WriteFile(stage, []byte("published"), 0o644); err != nil {
		t.Fatalf("cannot write stage: %v", err)
	}
	if err := PublishFileNoReplace(stage, target); err != nil {
		t.Fatalf("PublishFileNoReplace failed: %v", err)
	}
	content, err := os.ReadFile(target)
	if err != nil || string(content) != "published" {
		t.Fatalf("published content mismatch: %v", err)
	}
	if err := os.WriteFile(stage, []byte("second"), 0o644); err != nil {
		t.Fatalf("cannot rewrite stage: %v", err)
	}
	if err := PublishFileNoReplace(stage, target); err == nil {
		t.Fatalf("PublishFileNoReplace must refuse to replace an existing target")
	}
}

func TestCreateExclusiveDirectoryRefusesExisting(t *testing.T) {
	root := t.TempDir()
	path := filepath.Join(root, "created")
	if err := CreateExclusiveDirectory(path, 0o755); err != nil {
		t.Fatalf("CreateExclusiveDirectory failed: %v", err)
	}
	if err := CreateExclusiveDirectory(path, 0o755); err == nil {
		t.Fatalf("CreateExclusiveDirectory must refuse an existing directory")
	}
}

func TestRemoveDurably(t *testing.T) {
	root := t.TempDir()
	path := filepath.Join(root, "removable.txt")
	if err := os.WriteFile(path, []byte("x"), 0o644); err != nil {
		t.Fatalf("cannot write file: %v", err)
	}
	if err := RemoveDurably(path); err != nil {
		t.Fatalf("RemoveDurably failed: %v", err)
	}
	if _, err := os.Stat(path); !os.IsNotExist(err) {
		t.Fatalf("file must be gone after RemoveDurably, stat error = %v", err)
	}
	if err := RemoveDurably(path); err == nil {
		t.Fatalf("RemoveDurably must fail for an absent path")
	}
}

func TestSameDirectoryAndEqualFoldPath(t *testing.T) {
	root := t.TempDir()
	left := filepath.Join(root, "a", "file.txt")
	right := filepath.Join(root, "b", "file.txt")
	if sameDirectory(left, right) {
		t.Fatalf("different parents must not be the same directory")
	}
	if !sameDirectory(left, filepath.Join(root, "a", "other.txt")) {
		t.Fatalf("same parent must be the same directory")
	}
	if !equalFoldPath("C:\\Repo\\Tools", "c:\\repo\\tools") {
		t.Fatalf("equalFoldPath must fold ASCII case")
	}
	if equalFoldPath("C:\\Repo\\Tools", "C:\\Repo\\Other") {
		t.Fatalf("equalFoldPath must not equate different paths")
	}
}
