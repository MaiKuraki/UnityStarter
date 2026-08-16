//go:build !windows

package remove_unity_packages

import (
	"fmt"
	"os"
	"path/filepath"
	"syscall"
)

func lockFileNonBlocking(file *os.File) error {
	lock := syscall.Flock_t{Type: syscall.F_WRLCK, Whence: 0, Start: 0, Len: 1}
	return syscall.FcntlFlock(file.Fd(), syscall.F_SETLK, &lock)
}

func unlockFile(file *os.File) error {
	lock := syscall.Flock_t{Type: syscall.F_UNLCK, Whence: 0, Start: 0, Len: 1}
	return syscall.FcntlFlock(file.Fd(), syscall.F_SETLK, &lock)
}

func pathIsReparsePoint(path string) (bool, error) {
	info, err := os.Lstat(path)
	if err != nil {
		return false, err
	}
	return info.Mode()&os.ModeSymlink != 0, nil
}

func replacePathAtomically(stage, target string) error {
	if err := os.Rename(stage, target); err != nil {
		return err
	}
	if err := syncParentDirectory(target); err != nil {
		return fmt.Errorf("rename completed but parent-directory sync failed for '%s': %w", target, err)
	}
	return nil
}

func syncParentDirectory(path string) error {
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
