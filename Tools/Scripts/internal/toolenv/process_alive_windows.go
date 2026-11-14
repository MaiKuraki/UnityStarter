//go:build windows

package toolenv

import (
	"errors"
	"fmt"

	"golang.org/x/sys/windows"
)

const (
	processQueryLimitedInformation = 0x1000
	statusStillActive              = 259
)

// ProcessAlive asks the kernel whether a PID is still running. It uses no
// external command, so a minimal PATH or a localized tasklist output cannot
// change the answer, and a PID substring can never be mistaken for a process.
func ProcessAlive(pid int) (bool, error) {
	if pid <= 0 {
		return false, fmt.Errorf("invalid process id %d", pid)
	}
	handle, err := windows.OpenProcess(processQueryLimitedInformation, false, uint32(pid))
	if err != nil {
		if errors.Is(err, windows.ERROR_INVALID_PARAMETER) {
			return false, nil
		}
		// A protected process still exists even when its status cannot be read.
		if errors.Is(err, windows.ERROR_ACCESS_DENIED) {
			return true, nil
		}
		return false, err
	}
	defer func() { _ = windows.CloseHandle(handle) }()
	var code uint32
	if err := windows.GetExitCodeProcess(handle, &code); err != nil {
		return false, err
	}
	return code == statusStillActive, nil
}
