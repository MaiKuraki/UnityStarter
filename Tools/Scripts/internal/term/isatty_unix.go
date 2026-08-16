//go:build unix

package term

import "golang.org/x/sys/unix"

// IsTerminal reports whether the file descriptor is attached to an interactive terminal.
func IsTerminal(fd uintptr) bool {
	_, err := unix.IoctlGetTermios(int(fd), ioctlReadTermios)
	return err == nil
}
