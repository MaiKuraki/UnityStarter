package rename_project

import (
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"
)

func TestValidateProjectToken(t *testing.T) {
	for _, valid := range []string{"MyGame", "_internal", "game2"} {
		if err := validateProjectToken(valid); err != nil {
			t.Fatalf("validateProjectToken(%q) = %v, want nil", valid, err)
		}
	}
	for _, invalid := range []string{"2game", "my game", "my-game", "", "CON", "com1"} {
		if err := validateProjectToken(invalid); err == nil {
			t.Fatalf("validateProjectToken(%q) = nil, want an error", invalid)
		}
	}
}

func TestValidateDisplayName(t *testing.T) {
	if err := validateDisplayName("company name", "Cyclone Games"); err != nil {
		t.Fatalf("valid display name rejected: %v", err)
	}
	if err := validateDisplayName("company name", ""); err == nil {
		t.Fatalf("empty display name must be rejected")
	}
	if err := validateDisplayName("company name", "bad\x00name"); err == nil {
		t.Fatalf("control characters must be rejected")
	}
}

func TestValidateApplicationIdentifier(t *testing.T) {
	if err := validateApplicationIdentifier("com.cyclonegames.mygame2"); err != nil {
		t.Fatalf("valid identifier rejected: %v", err)
	}
	for _, invalid := range []string{"onlyone", "com.Example.game", "com.2bad.game", ""} {
		if err := validateApplicationIdentifier(invalid); err == nil {
			t.Fatalf("invalid identifier %q was accepted", invalid)
		}
	}
}

func TestIsProtectedProjectFolder(t *testing.T) {
	if !isProtectedProjectFolder("Plugins") || !isProtectedProjectFolder("plugins") {
		t.Fatalf("protected folders must match case-insensitively")
	}
	if isProtectedProjectFolder("MyGame") {
		t.Fatalf("ordinary names must not be protected")
	}
}

// TestProcessIsLikelyRunningContract verifies the liveness-hint contract:
// non-positive PIDs are never "running", and the current process is reported
// as running. The probe must never hang (bounded tasklist call on Windows).
func TestProcessIsLikelyRunningContract(t *testing.T) {
	if processIsLikelyRunning(0) || processIsLikelyRunning(-5) {
		t.Fatalf("non-positive PIDs must be reported as not running")
	}
	if !processIsLikelyRunning(os.Getpid()) {
		t.Fatalf("current process must be reported as running")
	}
}

func TestTransactionStatusRequiresRecovery(t *testing.T) {
	for _, status := range []string{"prepared", "applying", "rolling_back", "committing", "rollback_failed"} {
		if !transactionStatusRequiresRecovery(status) {
			t.Fatalf("status %q must require recovery", status)
		}
	}
	for _, status := range []string{"committed", "rolled_back", "unknown"} {
		if transactionStatusRequiresRecovery(status) {
			t.Fatalf("status %q must not require recovery", status)
		}
	}
}

func TestLockOwnerProbeTimeoutIsBounded(t *testing.T) {
	if lockOwnerProbeTimeout <= 0 || lockOwnerProbeTimeout > time.Minute {
		t.Fatalf("lockOwnerProbeTimeout = %v, want a small positive bound", lockOwnerProbeTimeout)
	}
}

// writeEditorInstance plants Unity's per-project "editor is open" marker with
// the given process id.
func writeEditorInstance(t *testing.T, projectRoot string, pid int) string {
	t.Helper()
	if err := os.MkdirAll(filepath.Join(projectRoot, "Library"), 0o755); err != nil {
		t.Fatalf("cannot create Library fixture: %v", err)
	}
	instance := fmt.Sprintf(`{"process_id": %d, "unity_version": "6000.0.0f1-test"}`, pid)
	path := filepath.Join(projectRoot, "Library", "EditorInstance.json")
	if err := os.WriteFile(path, []byte(instance), 0o644); err != nil {
		t.Fatalf("cannot write EditorInstance fixture: %v", err)
	}
	return path
}

func writeBuildLease(t *testing.T, projectRoot string, pid int) {
	t.Helper()
	workspace := filepath.Join(projectRoot, "Temp", "BuildPipeline", "Workspace")
	if err := os.MkdirAll(workspace, 0o755); err != nil {
		t.Fatalf("cannot create Build workspace fixture: %v", err)
	}
	if err := os.WriteFile(filepath.Join(workspace, "lease.lock"), []byte{}, 0o644); err != nil {
		t.Fatalf("cannot write lease fixture: %v", err)
	}
	lease := fmt.Sprintf(`{"documentType": "build-workspace-lease", "pid": %d, "operation": "player"}`, pid)
	if err := os.WriteFile(filepath.Join(workspace, "lease.json"), []byte(lease), 0o644); err != nil {
		t.Fatalf("cannot write lease metadata fixture: %v", err)
	}
}

const stalePID = 999999999 // outside every realistic PID space on all platforms

// Editor detection is project-scoped: a live EditorInstance.json inside THIS
// project blocks the rename, so editors open on other projects never interfere.
func TestAssessWorkspaceSafetyDetectsLiveEditor(t *testing.T) {
	projectRoot := t.TempDir()
	writeEditorInstance(t, projectRoot, os.Getpid())
	report := assessWorkspaceSafety(projectRoot)
	if len(report.Blockers) == 0 {
		t.Fatalf("a live editor with this project open must block the rename: %+v", report)
	}
	if !strings.Contains(strings.Join(report.Blockers, " "), "Unity Editor") {
		t.Fatalf("blocker must name the Unity Editor: %+v", report)
	}
}

func TestAssessWorkspaceSafetyStaleEditorWarnsOnly(t *testing.T) {
	projectRoot := t.TempDir()
	writeEditorInstance(t, projectRoot, stalePID)
	report := assessWorkspaceSafety(projectRoot)
	if len(report.Blockers) != 0 {
		t.Fatalf("a stale editor marker must not block: %+v", report)
	}
	if len(report.Warnings) == 0 {
		t.Fatalf("a stale editor marker must produce a warning: %+v", report)
	}
}

// A held Build workspace lease means BuildData identity writes would race an
// in-flight Build operation; an unprovable lease state fails closed.
func TestAssessWorkspaceSafetyLiveLeaseBlocks(t *testing.T) {
	projectRoot := t.TempDir()
	writeBuildLease(t, projectRoot, os.Getpid())
	report := assessWorkspaceSafety(projectRoot)
	if len(report.Blockers) == 0 {
		t.Fatalf("a held Build workspace lease must block the rename: %+v", report)
	}
	if !strings.Contains(strings.Join(report.Blockers, " "), "Build workspace lease") {
		t.Fatalf("blocker must name the Build workspace lease: %+v", report)
	}
}

func TestAssessWorkspaceSafetyStaleLeaseWarnsOnly(t *testing.T) {
	projectRoot := t.TempDir()
	writeBuildLease(t, projectRoot, stalePID)
	report := assessWorkspaceSafety(projectRoot)
	if len(report.Blockers) != 0 {
		t.Fatalf("a stale Build workspace lease must not block: %+v", report)
	}
	if len(report.Warnings) == 0 {
		t.Fatalf("a stale Build workspace lease must produce a warning: %+v", report)
	}
}

func TestAssessWorkspaceSafetyMalformedLeaseFailsClosed(t *testing.T) {
	projectRoot := t.TempDir()
	workspace := filepath.Join(projectRoot, "Temp", "BuildPipeline", "Workspace")
	if err := os.MkdirAll(workspace, 0o755); err != nil {
		t.Fatalf("cannot create Build workspace fixture: %v", err)
	}
	if err := os.WriteFile(filepath.Join(workspace, "lease.lock"), []byte{}, 0o644); err != nil {
		t.Fatalf("cannot write lease fixture: %v", err)
	}
	if err := os.WriteFile(filepath.Join(workspace, "lease.json"), []byte("not json"), 0o644); err != nil {
		t.Fatalf("cannot write lease metadata fixture: %v", err)
	}
	report := assessWorkspaceSafety(projectRoot)
	if len(report.Blockers) == 0 {
		t.Fatalf("an unprovable lease state must fail closed: %+v", report)
	}
}

func TestAssessWorkspaceSafetyIdleProjectPasses(t *testing.T) {
	report := assessWorkspaceSafety(t.TempDir())
	if len(report.Blockers) != 0 || len(report.Warnings) != 0 {
		t.Fatalf("an idle project must pass clean: %+v", report)
	}
}
