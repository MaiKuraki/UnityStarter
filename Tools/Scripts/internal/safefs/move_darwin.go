//go:build darwin

package safefs

import (
	"fmt"
	"os"
	"path/filepath"

	"golang.org/x/sys/unix"
)

func moveNoReplace(source, target string) error {
	return unix.RenamexNp(source, target, unix.RENAME_EXCL)
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

func ValidateMountBoundary(root, path string) error {
	var rootStatus unix.Stat_t
	if err := unix.Lstat(root, &rootStatus); err != nil {
		return fmt.Errorf("cannot inspect trusted root device identity: %w", err)
	}
	var pathStatus unix.Stat_t
	if err := unix.Lstat(path, &pathStatus); err != nil {
		return fmt.Errorf("cannot inspect path device identity: %w", err)
	}
	if rootStatus.Dev != pathStatus.Dev {
		return fmt.Errorf("path crosses a filesystem device boundary: %s", path)
	}
	return nil
}
