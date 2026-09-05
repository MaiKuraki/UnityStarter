package remove_unity_packages

import (
	"os"
	"runtime"
	"testing"
)

func TestIsStalePackageEvidenceName(t *testing.T) {
	for _, name := range []string{
		".package-removal-transaction-0123456789abcdef0123456789abcdef",
		"packages-lock.json.removal-transaction-20260101T000000.000000000Z",
		".manifest.json.stage-20260101",
		".packages-lock.json.stage-20260101",
		".manifest.json.backup-20260101.stage-x",
		".packages-lock.json.backup-20260101.stage-x",
	} {
		if !isStalePackageEvidenceName(name) {
			t.Fatalf("isStalePackageEvidenceName(%q) = false, want true", name)
		}
	}
	for _, name := range []string{
		"manifest.json",
		"packages-lock.json",
		".manifest.json.backup-20260101", // published backup: retained, not blocked
		"packages-lock.json.backup-20260101",
		"other.file",
	} {
		if isStalePackageEvidenceName(name) {
			t.Fatalf("isStalePackageEvidenceName(%q) = true, want false", name)
		}
	}
}

func TestEnsureNoPackageTransactionEvidenceCleanDirectory(t *testing.T) {
	tempDir := t.TempDir()
	if err := ensureNoPackageTransactionEvidence(tempDir, tempDir+"/packages-lock.json"); err != nil {
		t.Fatalf("clean directory must pass: %v", err)
	}
	if err := os.MkdirAll(tempDir+"/.package-removal-transaction-abc", 0o700); err != nil {
		t.Fatalf("cannot create stale evidence: %v", err)
	}
	if err := ensureNoPackageTransactionEvidence(tempDir, tempDir+"/packages-lock.json"); err == nil {
		t.Fatalf("stale transaction directory must be detected")
	}
}

func TestEnsureNoStalePackageStagesCleanDirectory(t *testing.T) {
	tempDir := t.TempDir()
	if err := ensureNoStalePackageStages(tempDir); err != nil {
		t.Fatalf("clean directory must pass: %v", err)
	}
	if err := os.WriteFile(tempDir+"/.manifest.json.stage-1", []byte("x"), 0o600); err != nil {
		t.Fatalf("cannot create stale stage: %v", err)
	}
	if err := ensureNoStalePackageStages(tempDir); err == nil {
		t.Fatalf("stale stage must be detected")
	}
}

// TestPackageToolProcessIsRunningProbeSanity exercises the real liveness probe
// with its bounded timeout on Windows; other platforms use os.FindProcess and
// are covered indirectly by the shared signal(0) logic.
func TestPackageToolProcessIsRunningProbeSanity(t *testing.T) {
	if runtime.GOOS != "windows" {
		t.Skip("the tasklist probe is Windows-only")
	}
	running, err := packageToolProcessIsRunning(os.Getpid())
	if err != nil {
		t.Fatalf("packageToolProcessIsRunning(current pid) failed: %v", err)
	}
	if !running {
		t.Fatalf("current process must be reported as running")
	}
}
