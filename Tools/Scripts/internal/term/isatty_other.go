//go:build !unix && !windows

package term

// IsTerminal is unknown on this platform; assume an interactive terminal.
func IsTerminal(fd uintptr) bool {
	return true
}
