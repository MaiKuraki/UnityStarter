package remove_unity_packages

import (
	"os"
	"path/filepath"
	"strings"
	"testing"

	"cyclonegames.tools/scripts/internal/projectroot"
)

// writeUnityProjectFixture builds the minimal shape validateUnityProjectRoot
// accepts and returns its root.
func writeUnityProjectFixture(t *testing.T) string {
	t.Helper()
	root := t.TempDir()
	for _, dir := range []string{"Assets", "ProjectSettings", "Packages"} {
		if err := os.MkdirAll(filepath.Join(root, dir), 0o755); err != nil {
			t.Fatalf("cannot create %s: %v", dir, err)
		}
	}
	if err := os.WriteFile(filepath.Join(root, "ProjectSettings", "ProjectVersion.txt"), []byte("m_EditorVersion: 6000.0.0f1\n"), 0o644); err != nil {
		t.Fatalf("cannot write ProjectVersion.txt: %v", err)
	}
	if err := os.WriteFile(filepath.Join(root, "Packages", "manifest.json"), []byte(`{"dependencies":{}}`), 0o644); err != nil {
		t.Fatalf("cannot write manifest.json: %v", err)
	}
	return root
}

func TestResolveProjectRootExplicitPath(t *testing.T) {
	root := writeUnityProjectFixture(t)
	result, err := resolveProjectRoot(root)
	if err != nil {
		t.Fatalf("explicit project root rejected: %v", err)
	}
	if result.Root != root {
		t.Fatalf("resolved %q, want %q", result.Root, root)
	}
	if result.Origin != projectroot.OriginExplicit {
		t.Fatalf("origin = %v, want %v", result.Origin, projectroot.OriginExplicit)
	}
}

func TestResolveProjectRootInvalidExplicitPathFailsClosed(t *testing.T) {
	root := writeUnityProjectFixture(t)
	_, err := resolveProjectRoot(filepath.Join(root, "missing"))
	if err == nil {
		t.Fatalf("a missing --project path must fail")
	}
	if !strings.Contains(err.Error(), "--project") {
		t.Fatalf("error must mention --project, got: %v", err)
	}
}

// P0 guard: --apply against an executable-directory fallback root must be refused
// when non-interactive, while a preview and the non-ambiguous origins pass.
func TestGuardDestructiveRun(t *testing.T) {
	fallback := projectroot.Result{Root: `/repo/UnityStarter`, Origin: projectroot.OriginExecutableFallback}
	explicit := projectroot.Result{Root: `/repo/UnityStarter`, Origin: projectroot.OriginExplicit}

	if err := guardDestructiveRun(fallback, true, false, false, false); err == nil {
		t.Fatalf("non-interactive --apply against a fallback root must be refused")
	}
	if err := guardDestructiveRun(fallback, false, true, false, false); err == nil {
		t.Fatalf("non-interactive real --resolve-stale-transaction against a fallback root must be refused")
	}
	if err := guardDestructiveRun(fallback, false, true, true, false); err != nil {
		t.Fatalf("a --dry-run stale resolution must be allowed: %v", err)
	}
	if err := guardDestructiveRun(fallback, true, false, false, true); err != nil {
		t.Fatalf("an interactive session may proceed: %v", err)
	}
	if err := guardDestructiveRun(explicit, true, false, false, false); err != nil {
		t.Fatalf("an explicit --project root must never be refused: %v", err)
	}
}
