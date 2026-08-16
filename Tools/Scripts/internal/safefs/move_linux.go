//go:build linux

package safefs

import (
	"fmt"
	"os"
	"path/filepath"

	"golang.org/x/sys/unix"
)

func moveNoReplace(source, target string) error {
	return unix.Renameat2(unix.AT_FDCWD, source, unix.AT_FDCWD, target, unix.RENAME_NOREPLACE)
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
	var rootStatus unix.Statx_t
	if err := unix.Statx(unix.AT_FDCWD, root, unix.AT_SYMLINK_NOFOLLOW, unix.STATX_MNT_ID, &rootStatus); err != nil {
		return fmt.Errorf("cannot inspect trusted root mount identity: %w", err)
	}
	var pathStatus unix.Statx_t
	if err := unix.Statx(unix.AT_FDCWD, path, unix.AT_SYMLINK_NOFOLLOW, unix.STATX_MNT_ID, &pathStatus); err != nil {
		return fmt.Errorf("cannot inspect path mount identity: %w", err)
	}
	if rootStatus.Mask&unix.STATX_MNT_ID == 0 || pathStatus.Mask&unix.STATX_MNT_ID == 0 {
		return fmt.Errorf("kernel did not provide a mount ID for '%s'", path)
	}
	if rootStatus.Mnt_id != pathStatus.Mnt_id {
		return fmt.Errorf("path crosses a mount or bind-mount boundary: %s", path)
	}
	return nil
}
