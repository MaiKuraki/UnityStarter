//go:build windows

package toolkit

import (
	"syscall"
	"unsafe"
)

var kernel32ForConsoleProbe = syscall.NewLazyDLL("kernel32.dll")
var getConsoleProcessListProc = kernel32ForConsoleProbe.NewProc("GetConsoleProcessList")

// consoleProcessCount returns how many processes are attached to the console of
// the calling process. A fresh console opened by double-clicking the executable
// reports exactly this process; a console launched from cmd/PowerShell also
// reports the shell and any ancestor console owners.
func consoleProcessCount() int {
	var processIDs [64]uint32
	count, _, _ := getConsoleProcessListProc.Call(
		uintptr(unsafe.Pointer(&processIDs[0])),
		uintptr(len(processIDs)),
	)
	if count == 0 || count > uintptr(len(processIDs)) {
		// No console, or the process list did not fit the fixed buffer; the
		// double-click signature cannot be proven either way.
		return 0
	}
	return int(count)
}
