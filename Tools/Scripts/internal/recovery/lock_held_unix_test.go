//go:build unix

package recovery

import (
	"os"
	"testing"

	"golang.org/x/sys/unix"
)

func holdLock(t *testing.T, path string) func() {
	t.Helper()
	if err := os.WriteFile(path, []byte("held"), 0o644); err != nil {
		t.Fatal(err)
	}
	fd, err := unix.Open(path, unix.O_RDWR|unix.O_CLOEXEC, 0)
	if err != nil {
		t.Fatal(err)
	}
	lock := unix.Flock_t{Type: unix.F_WRLCK, Whence: 0, Start: 0, Len: 1}
	if err := unix.FcntlFlock(uintptr(fd), unix.F_SETLK, &lock); err != nil {
		_ = unix.Close(fd)
		t.Fatal(err)
	}
	return func() {
		lock.Type = unix.F_UNLCK
		_ = unix.FcntlFlock(uintptr(fd), unix.F_SETLK, &lock)
		_ = unix.Close(fd)
	}
}
