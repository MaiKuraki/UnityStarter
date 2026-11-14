//go:build unix

package recovery

import (
	"errors"
	"os"

	"golang.org/x/sys/unix"
)

// HeldLock reports whether a live process owns the advisory lease. Advisory
// locks are cooperative on Unix; the absolute path is also checked for a
// symlink by the caller's containment pass.
func HeldLock(path string) (bool, error) {
	fd, err := unix.Open(path, unix.O_RDWR|unix.O_CLOEXEC, 0)
	if err != nil {
		if errors.Is(err, os.ErrNotExist) {
			return false, nil
		}
		return false, err
	}
	defer func() { _ = unix.Close(fd) }()
	lock := unix.Flock_t{Type: unix.F_WRLCK, Whence: 0, Start: 0, Len: 1}
	if err := unix.FcntlFlock(uintptr(fd), unix.F_SETLK, &lock); err != nil {
		if errors.Is(err, unix.EAGAIN) || errors.Is(err, unix.EWOULDBLOCK) {
			return true, nil
		}
		return false, err
	}
	lock.Type = unix.F_UNLCK
	_ = unix.FcntlFlock(uintptr(fd), unix.F_SETLK, &lock)
	return false, nil
}

func isRedirected(path string) (bool, error) {
	info, err := os.Lstat(path)
	if err != nil {
		return false, err
	}
	return info.Mode()&os.ModeSymlink != 0, nil
}
