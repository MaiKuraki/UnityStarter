//go:build unix

package safefs

import (
	"errors"
	"fmt"
	"os"

	"golang.org/x/sys/unix"
)

func IsSharingViolation(_ error) bool { return false }

// tryExclusiveOpen uses a non-blocking advisory write lock. Filesystems or
// processes that do not cooperate with flock cannot be proven occupied, so an
// unsupported lock is reported as unknown rather than as a hard failure.
func tryExclusiveOpen(path string) error {
	fd, err := unix.Open(path, unix.O_RDWR|unix.O_CLOEXEC, 0)
	if err != nil {
		if errors.Is(err, os.ErrNotExist) {
			return nil
		}
		return err
	}
	defer func() { _ = unix.Close(fd) }()
	if err := unix.Flock(fd, unix.LOCK_EX|unix.LOCK_NB); err != nil {
		if errors.Is(err, unix.EWOULDBLOCK) || errors.Is(err, unix.EAGAIN) {
			return fmt.Errorf("%w: %s", ErrLocked, path)
		}
		return nil
	}
	_ = unix.Flock(fd, unix.LOCK_UN)
	return nil
}

func replaceExisting(stage, target string) error { return os.Rename(stage, target) }

// ProbeExclusive is a no-op on Unix: rename(2) does not require a directory
// tree to be free of open handles, so a Windows-style occupancy preflight has
// no meaning here and must not add syscalls per entry.
func ProbeExclusive(string, bool) error { return nil }
