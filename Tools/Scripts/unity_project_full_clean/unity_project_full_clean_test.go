package unity_project_full_clean

import (
	"os"
	"runtime"
	"testing"
)

func TestIsHexVariants(t *testing.T) {
	if !isHex("0123456789abcdefABCDEF") {
		t.Fatalf("isHex must accept mixed-case hex digits")
	}
	if !isHex("") {
		// The loop-based implementation vacuously accepts the empty string;
		// callers enforce length separately.
		t.Fatalf("isHex(\"\") must be true by implementation contract")
	}
	if isHex("0x12") || isHex("12g") {
		t.Fatalf("isHex must reject non-hex input")
	}
	if !isUpperHex("0123456789ABCDEF") {
		t.Fatalf("isUpperHex must accept uppercase hex")
	}
	if isUpperHex("abcdef") {
		t.Fatalf("isUpperHex must reject lowercase hex")
	}
	if !isLowerHex("0123456789abcdef") {
		t.Fatalf("isLowerHex must accept lowercase hex")
	}
	if isLowerHex("ABCDEF") {
		t.Fatalf("isLowerHex must reject uppercase hex")
	}
}

func TestSamePathAndDescendantSemantics(t *testing.T) {
	if !samePath("E:\\Repo\\Project", "e:\\repo\\project") && os.PathSeparator == '\\' {
		t.Fatalf("samePath must fold case on Windows")
	}
	root := `E:\Repo\Project`
	child := `E:\Repo\Project\Library\Cache`
	if !isDescendant(root, child) {
		t.Fatalf("nested path must be a descendant")
	}
	if isDescendant(child, root) {
		t.Fatalf("parent must not be a descendant of its child")
	}
	if isDescendant(root, root) {
		t.Fatalf("a path is not its own descendant")
	}
}

func TestProcessIsRunningProbeSanity(t *testing.T) {
	if runtime.GOOS != "windows" {
		t.Skip("the tasklist probe is Windows-only")
	}
	running, err := processIsRunning(os.Getpid())
	if err != nil {
		t.Fatalf("processIsRunning(current pid) failed: %v", err)
	}
	if !running {
		t.Fatalf("current process must be reported as running")
	}
}

func TestMinimizeTargetsRemovesCoveredEntries(t *testing.T) {
	targets := []string{
		`E:\Repo\Project\Build\Windows`,
		`E:\Repo\Project\Build`,
		`E:\Repo\Project\Bundles\pc`,
	}
	minimized := minimizeTargets(targets)
	if len(minimized) != 2 {
		t.Fatalf("minimized = %v, want 2 entries", minimized)
	}
}
