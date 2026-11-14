//go:build windows

package safefs

import (
	"errors"
	"path/filepath"
	"strings"
	"testing"
)

func TestWindowsReservedAndForbiddenNames(t *testing.T) {
	policy := HostPathPolicy()
	for _, name := range []string{"CON", "con", "NUL", "COM1", "LPT9", "CON.txt", "AUX.log"} {
		if err := policy.ValidateComponent(name); !errors.Is(err, ErrInvalidName) {
			t.Fatalf("reserved name %q must be rejected: %v", name, err)
		}
	}
	for _, name := range []string{`bad:name`, `bad?name`, `bad*name`, `bad|name`, `trailing `, `trailing.`} {
		if err := policy.ValidateComponent(name); !errors.Is(err, ErrInvalidName) {
			t.Fatalf("forbidden name %q must be rejected: %v", name, err)
		}
	}
	if err := policy.ValidateComponent("normal.asset"); err != nil {
		t.Fatalf("a normal name must be accepted: %v", err)
	}
	if !policy.SupportsLongPrefix {
		t.Fatal("Windows must support extended-length paths")
	}
}

func TestWindowsExtendedPathPrefix(t *testing.T) {
	short := `C:\work\file.bin`
	prepared, err := prepareWindowsPath(short)
	if err != nil || prepared != short {
		t.Fatalf("short paths must be unchanged: %q %v", prepared, err)
	}
	long := `C:\work\` + strings.Repeat("segment\\", 40) + "file.bin"
	if len(long) < 260 {
		t.Fatal("fixture is not longer than MAX_PATH")
	}
	prepared, err = prepareWindowsPath(long)
	if err != nil {
		t.Fatalf("extended path preparation failed: %v", err)
	}
	if !strings.HasPrefix(prepared, `\\?\`) {
		t.Fatalf("long path must use the extended prefix: %q", prepared)
	}
	unc := `\\server\share\` + strings.Repeat("a", 300)
	prepared, err = prepareWindowsPath(unc)
	if err != nil || !strings.HasPrefix(prepared, `\\?\UNC\`) {
		t.Fatalf("long UNC path must use the UNC extended prefix: %q %v", prepared, err)
	}
	if _, err := prepareWindowsPath("relative\\" + strings.Repeat("x", 300)); err == nil {
		t.Fatal("a relative extended path must be rejected")
	}
}

func TestWindowsLongAbsolutePathIsAcceptedWithinExtendedLimit(t *testing.T) {
	policy := HostPathPolicy()
	root, err := filepath.Abs(".")
	if err != nil {
		t.Fatal(err)
	}
	long := filepath.Join(root, strings.Repeat("a", 200), strings.Repeat("b", 200))
	if err := policy.ValidateAbsolute(long); err != nil {
		t.Fatalf("a path between MAX_PATH and the extended limit must be accepted: %v", err)
	}
}
