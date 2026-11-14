//go:build windows

package toolenv

import "os/exec"

// Windows terminates the direct child on cancellation. Cmd.WaitDelay closes the
// inherited pipes after a bounded wait, so a surviving descendant cannot keep
// Wait blocked; occupancy of output files is handled before publishing.
func configureProcessGroup(_ *exec.Cmd) {}
