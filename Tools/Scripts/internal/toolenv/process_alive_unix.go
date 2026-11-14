//go:build unix

package toolenv

import (
	"errors"
	"fmt"
	"syscall"
)

// ProcessAlive uses signal 0, which performs a permission check without
// delivering a signal. EPERM means the process exists but belongs elsewhere.
func ProcessAlive(pid int) (bool, error) {
	if pid <= 0 {
		return false, fmt.Errorf("invalid process id %d", pid)
	}
	err := syscall.Kill(pid, 0)
	if err == nil || errors.Is(err, syscall.EPERM) {
		return true, nil
	}
	if errors.Is(err, syscall.ESRCH) {
		return false, nil
	}
	return false, err
}
