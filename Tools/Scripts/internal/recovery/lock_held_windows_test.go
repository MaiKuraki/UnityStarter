//go:build windows

package recovery

import (
	"os"
	"syscall"
	"testing"
	"unsafe"
)

func holdLock(t *testing.T, path string) func() {
	t.Helper()
	if err := os.WriteFile(path, []byte("held"), 0o644); err != nil {
		t.Fatal(err)
	}
	pointer, err := syscall.UTF16PtrFromString(path)
	if err != nil {
		t.Fatal(err)
	}
	handle, _, callErr := createFileW.Call(
		uintptr(unsafe.Pointer(pointer)), genericRead, fileShareReadWrite,
		0, openExisting, fileAttributeNormal, 0)
	if handle == invalidHandle {
		t.Fatalf("cannot open held lock: %v", callErr)
	}
	var overlapped syscall.Overlapped
	result, _, lockErr := lockFileEx.Call(handle,
		lockfileExclusive|lockfileFailImmediately, 0, 1, 0,
		uintptr(unsafe.Pointer(&overlapped)))
	if result == 0 {
		_, _, _ = closeHandle.Call(handle)
		t.Fatalf("cannot hold lock: %v", lockErr)
	}
	return func() {
		_, _, _ = unlockFileEx.Call(handle, 0, 1, 0, uintptr(unsafe.Pointer(&overlapped)))
		_, _, _ = closeHandle.Call(handle)
	}
}
