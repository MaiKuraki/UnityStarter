//go:build windows

package safefs

import (
	"fmt"
	"path/filepath"
	"syscall"
	"unsafe"
)

const moveFileWriteThrough = 0x00000008

var (
	kernel32    = syscall.NewLazyDLL("kernel32.dll")
	moveFileExW = kernel32.NewProc("MoveFileExW")
)

func moveNoReplace(source, target string) error {
	sourcePointer, err := syscall.UTF16PtrFromString(source)
	if err != nil {
		return err
	}
	targetPointer, err := syscall.UTF16PtrFromString(target)
	if err != nil {
		return err
	}
	result, _, callErr := moveFileExW.Call(
		uintptr(unsafe.Pointer(sourcePointer)),
		uintptr(unsafe.Pointer(targetPointer)),
		moveFileWriteThrough)
	if result == 0 {
		return fmt.Errorf("MoveFileEx no-replace move failed: %w", callErr)
	}
	return nil
}

func SyncParent(string) error {
	// MOVEFILE_WRITE_THROUGH and synced file handles provide the Windows
	// durability contract. Windows does not expose portable directory fsync.
	return nil
}

func ValidateMountBoundary(root, path string) error {
	if !filepath.IsAbs(root) || !filepath.IsAbs(path) || !equalFoldPath(filepath.VolumeName(root), filepath.VolumeName(path)) {
		return fmt.Errorf("path crosses the trusted Windows volume boundary: %s", path)
	}
	// Windows mount points and junctions carry FILE_ATTRIBUTE_REPARSE_POINT and
	// are rejected by the caller's per-segment reparse validation.
	return nil
}
