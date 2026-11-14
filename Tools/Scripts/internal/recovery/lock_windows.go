//go:build windows

package recovery

import (
	"errors"
	"fmt"
	"os"
	"syscall"
	"unsafe"
)

const (
	genericRead             = 0x80000000
	fileShareReadWrite      = 0x00000003
	openExisting            = 3
	fileAttributeNormal     = 0x00000080
	fileAttributeReparse    = 0x00000400
	lockfileExclusive       = 0x00000002
	lockfileFailImmediately = 0x00000001
	errorLockViolation      = syscall.Errno(33)
	invalidHandle           = ^uintptr(0)
)

var (
	kernel32      = syscall.NewLazyDLL("kernel32.dll")
	createFileW   = kernel32.NewProc("CreateFileW")
	closeHandle   = kernel32.NewProc("CloseHandle")
	lockFileEx    = kernel32.NewProc("LockFileEx")
	unlockFileEx  = kernel32.NewProc("UnlockFileEx")
	getAttributes = kernel32.NewProc("GetFileAttributesW")
)

// HeldLock reports whether a live process owns the byte-range lease. A lock
// file left behind by a finished process is inert and does not block cleanup.
func HeldLock(path string) (bool, error) {
	pointer, err := syscall.UTF16PtrFromString(path)
	if err != nil {
		return false, err
	}
	handle, _, callErr := createFileW.Call(
		uintptr(unsafe.Pointer(pointer)),
		genericRead,
		fileShareReadWrite,
		0,
		openExisting,
		fileAttributeNormal,
		0)
	if handle == invalidHandle {
		if errors.Is(callErr, os.ErrNotExist) {
			return false, nil
		}
		return false, fmt.Errorf("cannot open lock file %s: %w", path, callErr)
	}
	defer func() { _, _, _ = closeHandle.Call(handle) }()

	var overlapped syscall.Overlapped
	result, _, lockErr := lockFileEx.Call(
		handle,
		lockfileExclusive|lockfileFailImmediately,
		0, 1, 0,
		uintptr(unsafe.Pointer(&overlapped)))
	if result == 0 {
		var errno syscall.Errno
		if errors.As(lockErr, &errno) && errno == errorLockViolation {
			return true, nil
		}
		return false, fmt.Errorf("cannot probe lock %s: %w", path, lockErr)
	}
	_, _, _ = unlockFileEx.Call(handle, 0, 1, 0, uintptr(unsafe.Pointer(&overlapped)))
	return false, nil
}

func isRedirected(path string) (bool, error) {
	pointer, err := syscall.UTF16PtrFromString(path)
	if err != nil {
		return false, err
	}
	attributes, _, callErr := getAttributes.Call(uintptr(unsafe.Pointer(pointer)))
	if attributes == 0xffffffff {
		return false, callErr
	}
	return attributes&fileAttributeReparse != 0, nil
}
