//go:build linux

package safefs

import (
	"errors"
	"fmt"
	"os"
	"path/filepath"

	"golang.org/x/sys/unix"
)

func moveNoReplace(source, target string) error {
	err := unix.Renameat2(unix.AT_FDCWD, source, unix.AT_FDCWD, target, unix.RENAME_NOREPLACE)
	if err == nil {
		return nil
	}
	// Older kernels and some network or overlay filesystems do not implement
	// RENAME_NOREPLACE. Fall back to an existence check plus a plain rename;
	// the check is best effort because Linux offers no portable atomic
	// no-replace primitive on those filesystems.
	if !errors.Is(err, unix.ENOSYS) && !errors.Is(err, unix.EINVAL) && !errors.Is(err, unix.EOPNOTSUPP) {
		return err
	}
	if _, statErr := os.Lstat(target); statErr == nil {
		return os.ErrExist
	} else if !errors.Is(statErr, os.ErrNotExist) {
		return statErr
	}
	return fallbackNoReplace(source, target)
}

func fallbackNoReplace(source, target string) error {
	return os.Rename(source, target)
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

// ValidateMountBoundary compares mount identity when the kernel exposes
// STATX_MNT_ID (Linux 5.8+) and falls back to device identity on older kernels
// and filesystems that do not report a mount ID. Bind mounts inside one device
// cannot be distinguished by the fallback; per-segment symlink validation in
// the caller remains part of the containment contract.
func ValidateMountBoundary(root, path string) error {
	var rootStatus unix.Statx_t
	var pathStatus unix.Statx_t
	rootErr := unix.Statx(unix.AT_FDCWD, root, unix.AT_SYMLINK_NOFOLLOW, unix.STATX_MNT_ID, &rootStatus)
	pathErr := unix.Statx(unix.AT_FDCWD, path, unix.AT_SYMLINK_NOFOLLOW, unix.STATX_MNT_ID, &pathStatus)
	if rootErr == nil && pathErr == nil &&
		rootStatus.Mask&unix.STATX_MNT_ID != 0 && pathStatus.Mask&unix.STATX_MNT_ID != 0 {
		if rootStatus.Mnt_id != pathStatus.Mnt_id {
			return fmt.Errorf("path crosses a mount or bind-mount boundary: %s", path)
		}
		return nil
	}
	var rootStat unix.Stat_t
	if err := unix.Lstat(root, &rootStat); err != nil {
		return fmt.Errorf("cannot inspect trusted root device identity: %w", err)
	}
	var pathStat unix.Stat_t
	if err := unix.Lstat(path, &pathStat); err != nil {
		return fmt.Errorf("cannot inspect path device identity: %w", err)
	}
	if rootStat.Dev != pathStat.Dev {
		return fmt.Errorf("path crosses a filesystem device boundary: %s", path)
	}
	return nil
}
