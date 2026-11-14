package remove_unity_packages

import (
	"bytes"
	"io"
	"os"
	"path/filepath"
	"runtime"
	"strings"
	"testing"

	"cyclonegames.tools/scripts/internal/projectlayout"
	"cyclonegames.tools/scripts/internal/toolkit"
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

// writeDurableBuildEvidence reproduces the audit records a finished build
// pipeline keeps on disk: a published journal, an opaque envelope, a scratch
// folder, an owner marker and an idle lock. None of these is active work.
func writeDurableBuildEvidence(t *testing.T, transactionsDir string) {
	t.Helper()
	directories := []string{"gameplay-tags", "performance-testing", "scratch", "transaction-abc"}
	for _, name := range directories {
		if err := os.MkdirAll(filepath.Join(transactionsDir, name), 0o755); err != nil {
			t.Fatalf("cannot create %s: %v", name, err)
		}
	}
	files := map[string]string{
		filepath.Join("gameplay-tags", "active.json"):         `{"phase":"Published","transactionId":"gameplay-tags"}`,
		filepath.Join("performance-testing", "active.json"):   `{"payloadBase64":"eyJwaGFzZSI6IlB1Ymxpc2hlZCJ9"}`,
		filepath.Join("transaction-abc", "transaction.owner"): `{"owner":"build-worker"}`,
		"build.lock": "",
	}
	for relative, content := range files {
		if err := os.WriteFile(filepath.Join(transactionsDir, relative), []byte(content), 0o644); err != nil {
			t.Fatalf("cannot write %s: %v", relative, err)
		}
	}
}

func TestDurableBuildEvidenceDoesNotBlockMutation(t *testing.T) {
	root := t.TempDir()
	layout := projectlayout.Default()
	transactionsDir, err := layout.Join(root, layout.TransactionsDir)
	if err != nil {
		t.Fatalf("cannot resolve transactions dir: %v", err)
	}
	writeDurableBuildEvidence(t, transactionsDir)
	if err := ensurePackageMutationReady(root, layout, false); err != nil {
		t.Fatalf("durable audit evidence must not block an apply: %v", err)
	}
}

func TestStrictRecoveryBlocksUnclassifiedEvidence(t *testing.T) {
	root := t.TempDir()
	layout := projectlayout.Default()
	transactionsDir, err := layout.Join(root, layout.TransactionsDir)
	if err != nil {
		t.Fatalf("cannot resolve transactions dir: %v", err)
	}
	writeDurableBuildEvidence(t, transactionsDir)
	err = ensurePackageMutationReady(root, layout, true)
	if err == nil {
		t.Fatalf("--strict-recovery must block unclassified evidence")
	}
	if !strings.Contains(err.Error(), "transaction.owner") ||
		!strings.Contains(err.Error(), "performance-testing/active.json") {
		t.Fatalf("the refusal must name the concrete evidence: %v", err)
	}
	if !strings.Contains(err.Error(), "Wait for any live build process") {
		t.Fatalf("the refusal must explain how to proceed: %v", err)
	}
}

func TestLoadInitialLayoutReadsExplicitOverride(t *testing.T) {
	path := filepath.Join(t.TempDir(), "layout.json")
	if err := os.WriteFile(path, []byte(`{"transactionsDir":".buildpipeline/alternate"}`), 0o644); err != nil {
		t.Fatalf("cannot write layout override: %v", err)
	}
	layout, err := loadInitialLayout(path)
	if err != nil {
		t.Fatalf("explicit layout override rejected: %v", err)
	}
	if layout.TransactionsDir != ".buildpipeline/alternate" {
		t.Fatalf("transactionsDir = %q, want the overridden value", layout.TransactionsDir)
	}
	if layout.LeaseFile != projectlayout.Default().LeaseFile {
		t.Fatalf("unrelated layout entries must keep their defaults")
	}
}

func TestEnsurePackageMutationReadyUsesLayoutTransactionsDir(t *testing.T) {
	root := t.TempDir()
	layout := projectlayout.Default()
	transactionsDir, err := layout.Join(root, layout.TransactionsDir)
	if err != nil {
		t.Fatalf("cannot resolve transactions dir: %v", err)
	}
	if err := os.MkdirAll(transactionsDir, 0o755); err != nil {
		t.Fatalf("cannot create transactions dir: %v", err)
	}
	if err := os.WriteFile(filepath.Join(transactionsDir, "active.json"), []byte(`{"phase":"applying"}`), 0o644); err != nil {
		t.Fatalf("cannot write active journal: %v", err)
	}

	diverted := projectlayout.Default()
	diverted.TransactionsDir = ".buildpipeline/alternate-transactions"
	if err := ensurePackageMutationReady(root, diverted, false); err != nil {
		t.Fatalf("an explicit layout must move the recovery scan: %v", err)
	}
	err = ensurePackageMutationReady(root, layout, false)
	if err == nil {
		t.Fatalf("the default layout must detect the active record")
	}
	if !strings.Contains(err.Error(), "active.json") {
		t.Fatalf("the refusal must name the concrete evidence: %v", err)
	}
}

func TestApplyRemovalRunsEndToEnd(t *testing.T) {
	root := writeUnityProjectFixture(t)
	manifestPath := filepath.Join(root, "Packages", "manifest.json")
	if err := os.WriteFile(manifestPath,
		[]byte(`{"dependencies":{"com.example.placeholder":"1.0.0"}}`), 0o644); err != nil {
		t.Fatalf("cannot write manifest fixture: %v", err)
	}
	code := run([]string{
		"--project", root,
		"--apply",
		"--allow-package", "com.example.placeholder",
	}, io.Discard, io.Discard)
	if code != toolkit.ExitSuccess {
		t.Fatalf("a fully authorized --apply must succeed, got exit code %d", code)
	}
	updated, err := os.ReadFile(manifestPath)
	if err != nil {
		t.Fatalf("cannot read committed manifest: %v", err)
	}
	document, err := parseManifest(updated)
	if err != nil {
		t.Fatalf("committed manifest is not valid JSON: %v", err)
	}
	for _, item := range document.Dependencies {
		if item.Name == "com.example.placeholder" {
			t.Fatalf("authorized package still present after --apply")
		}
	}
	if !bytes.Contains(updated, []byte("dependencies")) {
		t.Fatalf("committed manifest lost its dependencies object")
	}
}

func TestStrictRecoveryFlagBlocksApply(t *testing.T) {
	root := writeUnityProjectFixture(t)
	manifestPath := filepath.Join(root, "Packages", "manifest.json")
	if err := os.WriteFile(manifestPath,
		[]byte(`{"dependencies":{"com.example.placeholder":"1.0.0"}}`), 0o644); err != nil {
		t.Fatalf("cannot write manifest fixture: %v", err)
	}
	transactionsDir, err := projectlayout.Default().Join(root, projectlayout.Default().TransactionsDir)
	if err != nil {
		t.Fatalf("cannot resolve transactions dir: %v", err)
	}
	writeDurableBuildEvidence(t, transactionsDir)

	base := []string{"--project", root, "--apply", "--allow-package", "com.example.placeholder"}
	if code := run(append([]string{}, base...), io.Discard, io.Discard); code != toolkit.ExitSuccess {
		t.Fatalf("durable evidence must not block a normal --apply, got %d", code)
	}
	// Restore the manifest so the strict run has a package to remove again.
	if err := os.WriteFile(manifestPath,
		[]byte(`{"dependencies":{"com.example.placeholder":"1.0.0"}}`), 0o644); err != nil {
		t.Fatalf("cannot restore manifest fixture: %v", err)
	}
	strictArgs := append(append([]string{}, base...), "--strict-recovery")
	if code := run(strictArgs, io.Discard, io.Discard); code != toolkit.ExitFailure {
		t.Fatalf("--strict-recovery must refuse the mutation, got %d", code)
	}
}

func TestBuildWorkspaceLeaseLeavesNoMetadata(t *testing.T) {
	root := t.TempDir()
	layout := projectlayout.Default()
	lease, err := acquireBuildWorkspaceLease(root, layout)
	if err != nil {
		t.Fatalf("cannot acquire lease: %v", err)
	}
	metadataPath, resolveErr := layout.Join(root, layout.LeaseMetadataFile)
	if resolveErr != nil {
		_ = lease.release()
		t.Fatalf("cannot resolve metadata path: %v", resolveErr)
	}
	if err := lease.release(); err != nil {
		t.Fatalf("cannot release lease: %v", err)
	}
	if _, err := os.Lstat(metadataPath); !os.IsNotExist(err) {
		t.Fatalf("releasing the lease must not leave metadata another tool could misread")
	}
}
