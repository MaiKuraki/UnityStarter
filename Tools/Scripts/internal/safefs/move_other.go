//go:build !windows && !linux && !darwin

package safefs

import (
	"fmt"
	"os"
	"path/filepath"
)

func moveNoReplace(_, _ string) error {
	return fmt.Errorf("atomic no-replace move is unsupported on this platform")
}

func SyncParent(path string) error {
	directory, err := os.Open(filepath.Dir(path))
	if err != nil {
		return err
	}
	syncErr := directory.Sync()
	closeErr := directory.Close()
	if syncErr != nil {
		return syncErr
	}
	return closeErr
}

func ValidateMountBoundary(_, path string) error {
	return fmt.Errorf("mount-boundary validation is unsupported on this platform: %s", path)
}
