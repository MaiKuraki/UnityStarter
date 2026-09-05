//go:build !windows

package toolkit

// consoleProcessCount is only meaningful on Windows; other platforms never use
// the post-run pause, so the probe conservatively reports an unknown console.
func consoleProcessCount() int {
	return 0
}
