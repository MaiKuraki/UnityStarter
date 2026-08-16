//go:build windows

package term

import "golang.org/x/sys/windows"

// IsTerminal reports whether the file descriptor is attached to an interactive console.
func IsTerminal(fd uintptr) bool {
	var mode uint32
	return windows.GetConsoleMode(windows.Handle(fd), &mode) == nil
}
