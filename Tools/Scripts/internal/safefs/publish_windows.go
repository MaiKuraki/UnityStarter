//go:build windows

package safefs

import (
	"errors"
	"fmt"
	"os"
	"syscall"
	"unsafe"
)

const (
	genericReadValue        = 0x80000000
	genericWriteValue       = 0x40000000
	openExistingValue       = 3
	fileAttributeNormal     = 0x00000080
	fileAttributeReadOnly   = 0x00000001
	invalidHandleValue      = ^uintptr(0)
	moveFileReplaceExisting = 0x00000001
	fileFlagBackupSemantics = 0x02000000
	errorFileNotFound       = syscall.Errno(2)
	errorPathNotFound       = syscall.Errno(3)
	errorAccessDenied       = syscall.Errno(5)
	errorSharingViolation   = syscall.Errno(32)
)

var (
	createFileW        = kernel32.NewProc("CreateFileW")
	setFileAttributesW = kernel32.NewProc("SetFileAttributesW")
	getFileAttributesW = kernel32.NewProc("GetFileAttributesW")
	closeHandleProc    = kernel32.NewProc("CloseHandle")
)

// IsSharingViolation reports the two Windows errors produced by an open handle
// in a tree that is being moved, replaced or removed.
func IsSharingViolation(err error) bool {
	var errno syscall.Errno
	if !errors.As(err, &errno) {
		return false
	}
	return errno == errorSharingViolation || errno == errorAccessDenied
}

// tryExclusiveOpen asks for a handle with no sharing modes. Success proves no
// other process holds the file; a sharing violation is reported as ErrLocked.
func tryExclusiveOpen(path string) error {
	prepared, err := prepareWindowsPath(path)
	if err != nil {
		return err
	}
	pointer, err := syscall.UTF16PtrFromString(prepared)
	if err != nil {
		return err
	}
	// Read access is enough to probe for sharing conflicts and also works for
	// read-only targets, which must stay distinguishable from held files.
	handle, _, callErr := createFileW.Call(
		uintptr(unsafe.Pointer(pointer)),
		genericReadValue,
		0,
		0,
		openExistingValue,
		fileAttributeNormal,
		0)
	if handle == invalidHandleValue {
		var errno syscall.Errno
		if errors.As(callErr, &errno) {
			if errno == errorSharingViolation || errno == errorAccessDenied {
				return fmt.Errorf("%w: %s: %v", ErrLocked, path, errno)
			}
			if errno == errorFileNotFound || errno == errorPathNotFound {
				return nil
			}
			return errno
		}
		return callErr
	}
	_, _, _ = closeHandleProc.Call(handle)
	return nil
}

// ProbeExclusive proves that path can be opened with no sharing modes at all.
// Directories need FILE_FLAG_BACKUP_SEMANTICS; without it CreateFileW reports a
// spurious access denied. Windows refuses to rename a directory while any entry
// in its tree is held open, so this read-only probe is a best-effort preflight
// before a quarantine move. Nothing is created, written or locked.
func ProbeExclusive(path string, isDir bool) error {
	prepared, err := prepareWindowsPath(path)
	if err != nil {
		return err
	}
	pointer, err := syscall.UTF16PtrFromString(prepared)
	if err != nil {
		return err
	}
	flags := uintptr(fileAttributeNormal)
	if isDir {
		flags |= fileFlagBackupSemantics
	}
	handle, _, callErr := createFileW.Call(
		uintptr(unsafe.Pointer(pointer)),
		genericReadValue,
		0,
		0,
		openExistingValue,
		flags,
		0)
	if handle == invalidHandleValue {
		var errno syscall.Errno
		if errors.As(callErr, &errno) {
			if errno == errorSharingViolation || errno == errorAccessDenied {
				return fmt.Errorf("%w: %s: %v", ErrLocked, path, errno)
			}
			if errno == errorFileNotFound || errno == errorPathNotFound {
				return nil
			}
		}
		return callErr
	}
	_, _, _ = closeHandleProc.Call(handle)
	return nil
}

func replaceExisting(stage, target string) error {
	if err := clearReadOnlyAttribute(target); err != nil {
		return err
	}
	preparedStage, err := prepareWindowsPath(stage)
	if err != nil {
		return err
	}
	preparedTarget, err := prepareWindowsPath(target)
	if err != nil {
		return err
	}
	stagePointer, err := syscall.UTF16PtrFromString(preparedStage)
	if err != nil {
		return err
	}
	targetPointer, err := syscall.UTF16PtrFromString(preparedTarget)
	if err != nil {
		return err
	}
	result, _, callErr := moveFileExW.Call(
		uintptr(unsafe.Pointer(stagePointer)),
		uintptr(unsafe.Pointer(targetPointer)),
		moveFileReplaceExisting|moveFileWriteThrough)
	if result == 0 {
		return fmt.Errorf("MoveFileEx replacement failed: %w", callErr)
	}
	return nil
}

func clearReadOnlyAttribute(path string) error {
	if _, err := os.Lstat(path); errors.Is(err, os.ErrNotExist) {
		return nil
	} else if err != nil {
		return err
	}
	prepared, err := prepareWindowsPath(path)
	if err != nil {
		return err
	}
	pointer, err := syscall.UTF16PtrFromString(prepared)
	if err != nil {
		return err
	}
	attributes, _, callErr := getFileAttributesW.Call(uintptr(unsafe.Pointer(pointer)))
	if attributes == 0xffffffff {
		return callErr
	}
	if attributes&fileAttributeReadOnly == 0 {
		return nil
	}
	result, _, callErr := setFileAttributesW.Call(
		uintptr(unsafe.Pointer(pointer)),
		attributes&^fileAttributeReadOnly)
	if result == 0 {
		return fmt.Errorf("cannot clear the read-only attribute: %w", callErr)
	}
	return nil
}
