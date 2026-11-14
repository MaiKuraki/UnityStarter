package rename_project

import (
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"cyclonegames.tools/scripts/internal/projectlayout"
	"cyclonegames.tools/scripts/internal/projectroot"
	"cyclonegames.tools/scripts/internal/term"
	"cyclonegames.tools/scripts/internal/toolenv"
	"cyclonegames.tools/scripts/internal/toolkit"
)

func TestParseRenameOptionsAcceptsProjectFlag(t *testing.T) {
	options, err := parseRenameOptions([]string{"--dry-run", "--project", "project"})
	if err != nil {
		t.Fatalf("parse failed: %v", err)
	}
	if !options.dryRun || options.project != "project" {
		t.Fatalf("options = %+v, want dryRun=true project=project", options)
	}
}

func TestParseRenameOptionsAcceptsIdentityFlags(t *testing.T) {
	options, err := parseRenameOptions([]string{
		"--company", "NewCompany",
		"--product", "NewProduct",
		"--project-name", "NewGame",
		"--app-id", "com.new.game",
		"--yes",
		"--layout", "layout.json",
	})
	if err != nil {
		t.Fatalf("parse failed: %v", err)
	}
	if options.company != "NewCompany" || options.product != "NewProduct" ||
		options.projectName != "NewGame" || options.appID != "com.new.game" ||
		!options.yes || options.layout != "layout.json" {
		t.Fatalf("options = %+v, want all identity flags", options)
	}
}

func TestNonInteractiveRequiresAllIdentityFlags(t *testing.T) {
	if term.IsTerminal(os.Stdin.Fd()) && term.IsTerminal(os.Stdout.Fd()) {
		t.Skip("the non-interactive guard does not apply to a terminal session")
	}
	if err := nonInteractiveInputError(renameOptions{}); err == nil {
		t.Fatalf("missing identity flags must be rejected")
	}
	complete := renameOptions{
		company: "NewCompany", product: "NewProduct",
		projectName: "NewGame", appID: "com.new.game", yes: true,
	}
	if err := nonInteractiveInputError(complete); err != nil {
		t.Fatalf("a fully specified non-interactive run must be accepted: %v", err)
	}
	preview := complete
	preview.yes = false
	preview.dryRun = true
	if err := nonInteractiveInputError(preview); err != nil {
		t.Fatalf("a non-interactive dry run does not need --yes: %v", err)
	}
	commit := complete
	commit.yes = false
	if err := nonInteractiveInputError(commit); err == nil {
		t.Fatalf("a non-interactive commit needs --yes")
	}
}

func TestLoadRenameLayoutReadsExplicitOverride(t *testing.T) {
	path := filepath.Join(t.TempDir(), "layout.json")
	if err := os.WriteFile(path, []byte(`{"identityRegistry":"ProjectSettings/custom-identity.json"}`), 0o644); err != nil {
		t.Fatalf("cannot write layout override: %v", err)
	}
	layout, err := loadRenameLayout(path)
	if err != nil {
		t.Fatalf("explicit layout override rejected: %v", err)
	}
	if layout.IdentityRegistry != "ProjectSettings/custom-identity.json" {
		t.Fatalf("identityRegistry = %q, want the overridden value", layout.IdentityRegistry)
	}
	if layout.LeaseFile != projectlayout.Default().LeaseFile {
		t.Fatalf("unrelated layout entries must keep their defaults")
	}
}

func TestIdentityValuesEqual(t *testing.T) {
	same := RenameRequest{
		OldProjectName: "Game", NewProjectName: "Game",
		OldCompanyName: "C", NewCompanyName: "C",
		OldAppName: "P", NewAppName: "P",
		OldApplicationIdentifier: "com.c.p", NewApplicationIdentifier: "com.c.p",
	}
	if !identityValuesEqual(same) {
		t.Fatalf("identical values must be reported as unchanged")
	}
	changed := same
	changed.NewAppName = "Q"
	if identityValuesEqual(changed) {
		t.Fatalf("a changed value must not be reported as unchanged")
	}
}

func TestParseRenameOptionsRejectsPositionalArguments(t *testing.T) {
	if _, err := parseRenameOptions([]string{"unexpected"}); err == nil {
		t.Fatalf("positional arguments must be rejected")
	}
}

// findProjectRoot delegates to the shared resolver; running inside a project root
// must still detect it.
func TestFindProjectRootDetectsCurrentDirectory(t *testing.T) {
	project := t.TempDir()
	for _, marker := range []string{"Assets", "ProjectSettings"} {
		if err := os.MkdirAll(filepath.Join(project, marker), 0o755); err != nil {
			t.Fatalf("cannot create %s: %v", marker, err)
		}
	}
	t.Chdir(project)
	got, err := findProjectRoot()
	if err != nil {
		t.Fatalf("current directory project not detected: %v", err)
	}
	if got.Origin != projectroot.OriginWorkingDirectory {
		t.Fatalf("origin = %v, want %v", got.Origin, projectroot.OriginWorkingDirectory)
	}
	gotResolved, err := filepath.EvalSymlinks(got.Root)
	if err != nil {
		t.Fatalf("cannot canonicalize resolved root: %v", err)
	}
	wantResolved, err := filepath.EvalSymlinks(project)
	if err != nil {
		t.Fatalf("cannot canonicalize fixture root: %v", err)
	}
	if gotResolved != wantResolved {
		t.Fatalf("resolved %q, want %q", gotResolved, wantResolved)
	}
}

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

func TestProcessLivenessProbeUsesKernel(t *testing.T) {
	alive, err := toolenv.ProcessAlive(os.Getpid())
	if err != nil || !alive {
		t.Fatalf("the current process must be reported alive: %v %v", alive, err)
	}
	if alive, err := toolenv.ProcessAlive(0); err == nil || alive {
		t.Fatalf("an invalid pid must fail closed: %v %v", alive, err)
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

// writeBuildLease plants an idle lease lock and leftover metadata of the kind a
// finished run may leave behind. Neither may block a rename.
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

// Only a lease currently held by a live process may refuse a rename.
func TestAssessWorkspaceSafetyHeldLeaseBlocks(t *testing.T) {
	original := buildLeaseHeld
	t.Cleanup(func() { buildLeaseHeld = original })
	buildLeaseHeld = func(string) (bool, error) { return true, nil }
	report := assessWorkspaceSafety(t.TempDir())
	if len(report.Blockers) == 0 {
		t.Fatalf("a held Build workspace lease must block the rename: %+v", report)
	}
	if !strings.Contains(strings.Join(report.Blockers, " "), "held by a live process") {
		t.Fatalf("blocker must name the live lease holder: %+v", report)
	}
}

// An idle lock plus metadata left by another tool must never be misread as a
// live holder; the tools stay independent.
func TestAssessWorkspaceSafetyIdleLeaseDoesNotBlock(t *testing.T) {
	projectRoot := t.TempDir()
	writeBuildLease(t, projectRoot, stalePID)
	report := assessWorkspaceSafety(projectRoot)
	if len(report.Blockers) != 0 {
		t.Fatalf("an idle Build workspace lease must not block: %+v", report)
	}
}

func TestAssessWorkspaceSafetyLeftoverMetadataDoesNotBlock(t *testing.T) {
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
	if len(report.Blockers) != 0 {
		t.Fatalf("leftover or malformed metadata must not block: %+v", report)
	}
}

func TestAssessWorkspaceSafetyUsesLayoutOverride(t *testing.T) {
	projectRoot := t.TempDir()
	original := buildLeaseHeld
	t.Cleanup(func() { buildLeaseHeld = original })
	buildLeaseHeld = func(path string) (bool, error) {
		return strings.Contains(filepath.ToSlash(path), "custom/workspace/lease.lock"), nil
	}
	custom := projectlayout.Default()
	custom.WorkspaceDir = "custom/workspace"
	custom.LeaseFile = "custom/workspace/lease.lock"
	custom.LeaseMetadataFile = "custom/workspace/lease.json"
	custom.QuarantineDir = "custom/workspace/cleanup-quarantine"
	if report := assessWorkspaceSafetyWithLayout(projectRoot, custom); len(report.Blockers) == 0 {
		t.Fatalf("the layout override must drive the probed lease path: %+v", report)
	}
	if report := assessWorkspaceSafety(projectRoot); len(report.Blockers) != 0 {
		t.Fatalf("the default layout resolves a different lease path: %+v", report)
	}
}

func TestAssessWorkspaceSafetyIdleProjectPasses(t *testing.T) {
	report := assessWorkspaceSafety(t.TempDir())
	if len(report.Blockers) != 0 || len(report.Warnings) != 0 {
		t.Fatalf("an idle project must pass clean: %+v", report)
	}
}

func TestLoadTemplateIdentityRegistryIsOptional(t *testing.T) {
	configureLayout(projectlayout.Default())
	snapshot, err := loadTemplateIdentityRegistry(t.TempDir())
	if err != nil {
		t.Fatalf("a missing identity registry must not be a startup prerequisite: %v", err)
	}
	if snapshot.Registry == nil || len(snapshot.Registry.Targets) != 0 {
		t.Fatalf("a missing registry must yield no identity targets: %+v", snapshot.Registry)
	}
	if snapshot.ContentSHA256 != "" || len(snapshot.ExternalWriteTargets) != 0 ||
		len(snapshot.DeclaredExternalTargets) != 0 {
		t.Fatalf("a missing registry must be an empty snapshot: %+v", snapshot)
	}
}

func TestReadBoundedFileRejectsSymlink(t *testing.T) {
	directory := t.TempDir()
	target := filepath.Join(directory, "content.txt")
	if err := os.WriteFile(target, []byte("content"), 0o644); err != nil {
		t.Fatalf("cannot write fixture: %v", err)
	}
	data, err := readBoundedFile(target)
	if err != nil || string(data) != "content" {
		t.Fatalf("readBoundedFile failed on a regular file: %q, %v", data, err)
	}
	link := filepath.Join(directory, "link.txt")
	if err := os.Symlink(target, link); err != nil {
		t.Skipf("symbolic links are unavailable: %v", err)
	}
	if _, err := readBoundedFile(link); err == nil {
		t.Fatalf("symbolic links must be rejected")
	}
	if _, err := readBoundedFile(directory); err == nil {
		t.Fatalf("directories must be rejected")
	}
}

// writeRenameFixture builds a minimal Unity project without an identity
// registry so the core rename targets can be planned on their own.
func writeRenameFixture(t *testing.T) string {
	t.Helper()
	root := t.TempDir()
	files := map[string]string{
		filepath.Join("Assets", "OldGame.meta"): "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\nfolderAsset: yes\n",
		filepath.Join("ProjectSettings", "ProjectSettings.asset"): strings.Join([]string{
			"%YAML 1.1",
			"--- !u!129 &1",
			"PlayerSettings:",
			"  companyName: OldCompany",
			"  productName: OldProduct",
			"  applicationIdentifier:",
			"    Android: com.old.game",
			"    iPhone: com.old.game",
			"  metroApplicationDescription: OldProduct",
			"",
		}, "\n"),
		filepath.Join("ProjectSettings", "EditorBuildSettings.asset"): strings.Join([]string{
			"%YAML 1.1",
			"--- !u!1045 &1",
			"EditorBuildSettings:",
			"  m_Scenes:",
			"  - enabled: 1",
			"    path: Assets/OldGame/Scenes/Main.unity",
			"",
		}, "\n"),
	}
	for relative, content := range files {
		path := filepath.Join(root, relative)
		if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
			t.Fatalf("cannot create fixture directory: %v", err)
		}
		if err := os.WriteFile(path, []byte(content), 0o644); err != nil {
			t.Fatalf("cannot write fixture file: %v", err)
		}
	}
	if err := os.MkdirAll(filepath.Join(root, "Assets", "OldGame"), 0o755); err != nil {
		t.Fatalf("cannot create project folder: %v", err)
	}
	return root
}

func TestBuildRenamePlanWithoutIdentityRegistry(t *testing.T) {
	configureLayout(projectlayout.Default())
	root := writeRenameFixture(t)
	request := RenameRequest{
		OldProjectName: "OldGame", NewProjectName: "NewGame",
		OldCompanyName: "OldCompany", NewCompanyName: "NewCompany",
		OldAppName: "OldProduct", NewAppName: "NewProduct",
		OldApplicationIdentifier: "com.old.game", NewApplicationIdentifier: "com.new.game",
	}
	plan, err := buildRenamePlan(root, request)
	if err != nil {
		t.Fatalf("a rename must be plannable without an identity registry: %v", err)
	}
	if len(plan.Operations) == 0 {
		t.Fatalf("the plan must still contain the core rename operations")
	}
	if len(plan.SkippedTargets) != 0 {
		t.Fatalf("no registry targets can be skipped when the registry is absent: %+v", plan.SkippedTargets)
	}
	hasSettingsWrite := false
	for _, operation := range plan.Operations {
		if operation.Kind == renameOperationWrite && filepath.Base(operation.Target) == "ProjectSettings.asset" {
			hasSettingsWrite = true
		}
	}
	if !hasSettingsWrite {
		t.Fatalf("ProjectSettings must still be updated without the registry")
	}
}

func TestRunRenameToolNonInteractiveRequiresFlags(t *testing.T) {
	if term.IsTerminal(os.Stdin.Fd()) && term.IsTerminal(os.Stdout.Fd()) {
		t.Skip("the non-interactive guard does not apply to a terminal session")
	}
	if code := runRenameTool(nil); code != toolkit.ExitUsage {
		t.Fatalf("a non-interactive run without identity flags must fail with a usage error, got %d", code)
	}
}

func TestRunRenameToolNonInteractiveDryRun(t *testing.T) {
	configureLayout(projectlayout.Default())
	root := writeRenameFixture(t)
	code := runRenameTool([]string{
		"--project", root,
		"--company", "NewCompany",
		"--product", "NewProduct",
		"--project-name", "NewGame",
		"--app-id", "com.new.game",
		"--dry-run",
	})
	if code != toolkit.ExitSuccess {
		t.Fatalf("a fully specified non-interactive dry run must succeed, got %d", code)
	}
}
