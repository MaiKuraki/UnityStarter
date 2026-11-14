//go:build unix

package toolenv

import (
	"errors"
	"os/exec"
	"syscall"
)

// configureProcessGroup places the child in its own process group so that a
// cancelled encode terminates the whole process tree, not only the direct child.
// This prevents a wrapper or a spawned encoder from keeping the inherited pipe
// open after cancellation.
func configureProcessGroup(cmd *exec.Cmd) {
	cmd.SysProcAttr = &syscall.SysProcAttr{Setpgid: true}
	cmd.Cancel = func() error {
		if cmd.Process == nil {
			return nil
		}
		err := syscall.Kill(-cmd.Process.Pid, syscall.SIGKILL)
		if err != nil && !errors.Is(err, syscall.ESRCH) {
			_ = cmd.Process.Kill()
		}
		return nil
	}
}
