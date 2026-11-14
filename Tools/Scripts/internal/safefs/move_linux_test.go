//go:build linux

package safefs

import (
	"errors"
	"os"
	"path/filepath"
	"testing"
)

// The no-replace contract must hold both when RENAME_NOREPLACE is available and
// when the filesystem falls back to an existence check.
func TestLinuxFallbackRefusesExistingTarget(t *testing.T) {
	root := t.TempDir()
	stage := filepath.Join(root, "stage")
	target := filepath.Join(root, "target")
	if err := os.WriteFile(stage, []byte("new"), 0o644); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(target, []byte("existing"), 0o644); err != nil {
		t.Fatal(err)
	}
	if err := moveNoReplace(stage, target); err == nil {
		t.Fatal("an existing target must never be replaced")
	}
	data, err := os.ReadFile(target)
	if err != nil || string(data) != "existing" {
		t.Fatalf("target was modified: %q %v", data, err)
	}
}

func TestLinuxFallbackCreatesTarget(t *testing.T) {
	root := t.TempDir()
	stage := filepath.Join(root, "stage")
	target := filepath.Join(root, "target")
	if err := os.WriteFile(stage, []byte("new"), 0o644); err != nil {
		t.Fatal(err)
	}
	if err := moveNoReplace(stage, target); err != nil {
		t.Fatalf("move to an absent target failed: %v", err)
	}
	data, err := os.ReadFile(target)
	if err != nil || string(data) != "new" {
		t.Fatalf("target = %q, %v; want new", data, err)
	}
	if _, err := os.Lstat(stage); !errors.Is(err, os.ErrNotExist) {
		t.Fatal("stage must be consumed")
	}
}
