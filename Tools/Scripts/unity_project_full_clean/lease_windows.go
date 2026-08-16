//go:build windows

package unity_project_full_clean

import (
	"fmt"
	"os"
	"syscall"
	"unsafe"
)

const (
	lockfileExclusiveLock   = 0x00000002
	lockfileFailImmediately = 0x00000001
	fileAttributeReparse    = 0x00000400
	movefileReplaceExisting = 0x00000001
	movefileWriteThrough    = 0x00000008
)

var (
	kernel32           = syscall.NewLazyDLL("kernel32.dll")
	lockFileEx         = kernel32.NewProc("LockFileEx")
	unlockFileEx       = kernel32.NewProc("UnlockFileEx")
	getFileAttributesW = kernel32.NewProc("GetFileAttributesW")
	moveFileExW        = kernel32.NewProc("MoveFileExW")
)

func lockFileNonBlocking(file *os.File) error {
	var overlapped syscall.Overlapped
	result, _, callErr := lockFileEx.Call(
		file.Fd(),
		lockfileExclusiveLock|lockfileFailImmediately,
		0,
		1,
		0,
		uintptr(unsafe.Pointer(&overlapped)))
	if result == 0 {
		return fmt.Errorf("LockFileEx byte-range lease failed: %w", callErr)
	}
	return nil
}

func unlockFile(file *os.File) error {
	var overlapped syscall.Overlapped
	result, _, callErr := unlockFileEx.Call(
		file.Fd(),
		0,
		1,
		0,
		uintptr(unsafe.Pointer(&overlapped)))
	if result == 0 {
		return fmt.Errorf("UnlockFileEx byte-range lease failed: %w", callErr)
	}
	return nil
}

func pathIsReparsePoint(path string) (bool, error) {
	pointer, err := syscall.UTF16PtrFromString(path)
	if err != nil {
		return false, err
	}
	attributes, _, callErr := getFileAttributesW.Call(uintptr(unsafe.Pointer(pointer)))
	if attributes == 0xffffffff {
		return false, callErr
	}
	return attributes&fileAttributeReparse != 0, nil
}

func replacePathAtomically(stage, target string) error {
	stagePointer, err := syscall.UTF16PtrFromString(stage)
	if err != nil {
		return err
	}
	targetPointer, err := syscall.UTF16PtrFromString(target)
	if err != nil {
		return err
	}
	result, _, callErr := moveFileExW.Call(
		uintptr(unsafe.Pointer(stagePointer)),
		uintptr(unsafe.Pointer(targetPointer)),
		movefileReplaceExisting|movefileWriteThrough)
	if result == 0 {
		return fmt.Errorf("MoveFileEx atomic replacement failed: %w", callErr)
	}
	return nil
}
