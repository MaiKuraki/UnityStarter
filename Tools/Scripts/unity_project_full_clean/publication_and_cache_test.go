package unity_project_full_clean

import (
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"cyclonegames.tools/scripts/internal/projectroot"
)

// writeUnityProjectFixture builds the minimal directory shape validateProjectRoot
// accepts (markers, ProjectVersion, structured manifest) and returns its root.
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
	if err := projectroot.GuardDestructive(result, false); err != nil {
		t.Fatalf("an explicit --project root must never be refused: %v", err)
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

// P0 guard: an executable-directory fallback must never drive a destructive,
// non-interactive cleanup.
func TestGuardDestructiveRunRefusesCIFallback(t *testing.T) {
	fallback := projectroot.Result{Root: `/repo/UnityStarter`, Origin: projectroot.OriginExecutableFallback}

	for _, c := range []struct {
		name                string
		ciMode              bool
		stdinTTY, stdoutTTY bool
	}{
		{name: "ci mode", ciMode: true},
		{name: "piped stdin/stdout", ciMode: false, stdinTTY: false, stdoutTTY: false},
		{name: "tty stdin only", ciMode: false, stdinTTY: true, stdoutTTY: false},
	} {
		err := guardDestructiveRun(fallback, false, c.ciMode, c.stdinTTY, c.stdoutTTY)
		if err == nil {
			t.Fatalf("%s: destructive fallback must be refused", c.name)
		}
		if !strings.Contains(err.Error(), fallback.Root) || !strings.Contains(err.Error(), "--project") {
			t.Fatalf("%s: refusal must name the path and suggest --project, got: %v", c.name, err)
		}
	}
}

func TestGuardDestructiveRunAllowsDryRunAndInteractive(t *testing.T) {
	fallback := projectroot.Result{Root: `/repo/UnityStarter`, Origin: projectroot.OriginExecutableFallback}

	if err := guardDestructiveRun(fallback, true, true, false, false); err != nil {
		t.Fatalf("a --dry-run preview of a fallback root must be allowed: %v", err)
	}
	if err := guardDestructiveRun(fallback, false, false, true, true); err != nil {
		t.Fatalf("an interactive session may proceed to the CLEAN confirmation: %v", err)
	}
	if err := guardDestructiveRun(projectroot.Result{Root: `/repo/UnityStarter`, Origin: projectroot.OriginExplicit}, false, true, false, false); err != nil {
		t.Fatalf("an explicit --project root must never be refused: %v", err)
	}
}

// P0-B: a hand-built publication tree with no ownership marker must be reported
// as skipped, never abort the whole cleanup, and never become a deletion target.
func TestInspectPublicationOwnershipSkipsUnmarkedContent(t *testing.T) {
	root := t.TempDir()
	build := filepath.Join(root, "Build", "Windows")
	if err := os.MkdirAll(build, 0o755); err != nil {
		t.Fatalf("cannot create Build/Windows: %v", err)
	}
	if err := os.WriteFile(filepath.Join(build, "UnityPlayer.dll"), []byte("payload"), 0o644); err != nil {
		t.Fatalf("cannot write player artifact: %v", err)
	}

	for _, includeBuildOutputs := range []bool{false, true} {
		inspection, err := inspectPublicationOwnership(root, includeBuildOutputs)
		if err != nil {
			t.Fatalf("unmarked publication must not block cleanup (include=%v): %v", includeBuildOutputs, err)
		}
		if len(inspection.owned) != 0 {
			t.Fatalf("unmarked content must never be owned (include=%v): %+v", includeBuildOutputs, inspection.owned)
		}
		found := false
		for _, skipped := range inspection.skipped {
			if samePath(skipped, build) {
				found = true
			}
		}
		if !found {
			t.Fatalf("skipped list must report the top-most unmarked directory (include=%v), got %v", includeBuildOutputs, inspection.skipped)
		}
	}
}

// A marker that is present but malformed is not "unmarked": it must fail closed.
func TestInspectPublicationOwnershipFailsClosedOnMalformedMarker(t *testing.T) {
	root := t.TempDir()
	dir := filepath.Join(root, "Build", "Windows")
	if err := os.MkdirAll(dir, 0o755); err != nil {
		t.Fatalf("cannot create Build/Windows: %v", err)
	}
	if err := os.WriteFile(filepath.Join(dir, ".buildpipeline-owner.json"), []byte("not json"), 0o644); err != nil {
		t.Fatalf("cannot write malformed marker: %v", err)
	}
	if _, err := inspectPublicationOwnership(root, false); err == nil {
		t.Fatalf("a malformed ownership marker must fail closed")
	}
}

func writeHybridCLROwnerMarker(t *testing.T, directory string) {
	t.Helper()
	marker := fmt.Sprintf(`{"documentType":"hybridclr-output-owner","owner":"Build.Pipeline.Editor.HybridCLR","role":"HotUpdate","transactionId":"0123456789abcdef0123456789abcdef","files":[{"kind":"Artifact","path":"hot.dll","size":0,"sha256":"%s"}]}`, strings.Repeat("A", 64))
	if err := os.WriteFile(filepath.Join(directory, ".buildpipeline-owner.json"), []byte(marker), 0o644); err != nil {
		t.Fatalf("cannot write ownership marker: %v", err)
	}
}

// A recognized marker is protected (not deleted) by default, and is skipped when
// the cleaner cannot independently verify its provider-specific identity.
func TestInspectPublicationOwnershipRecognizedMarkerHandling(t *testing.T) {
	root := t.TempDir()
	dir := filepath.Join(root, "HybridCLRData", "hot")
	if err := os.MkdirAll(dir, 0o755); err != nil {
		t.Fatalf("cannot create HybridCLRData/hot: %v", err)
	}
	writeHybridCLROwnerMarker(t, dir)

	protected, err := inspectPublicationOwnership(root, false)
	if err != nil {
		t.Fatalf("recognized marker must not error by default: %v", err)
	}
	if len(protected.owned) != 1 || !samePath(protected.owned[0].path, dir) {
		t.Fatalf("recognized marker must yield exactly the owned target by default: %+v", protected.owned)
	}

	including, err := inspectPublicationOwnership(root, true)
	if err != nil {
		t.Fatalf("recognized-but-unverifiable marker must not abort: %v", err)
	}
	if len(including.owned) != 0 {
		t.Fatalf("unverifiable marker must never be a deletion target: %+v", including.owned)
	}
	skippedFound := false
	for _, skipped := range including.skipped {
		if samePath(skipped, dir) {
			skippedFound = true
		}
	}
	if !skippedFound {
		t.Fatalf("unverifiable marker must be reported as skipped, got %v", including.skipped)
	}
}

// P1-D: Unity's cache folder casing varies by version/platform, so the lookup
// must match case-insensitively on every platform.
func TestCollectCleanItemsMatchesCacheDirectoriesCaseInsensitively(t *testing.T) {
	root := t.TempDir()
	for _, dir := range []string{"Library", "logs", "Obj", ".VS"} {
		if err := os.MkdirAll(filepath.Join(root, dir), 0o755); err != nil {
			t.Fatalf("cannot create %s: %v", dir, err)
		}
	}
	items, err := collectCleanItems(root, nil, false)
	if err != nil {
		t.Fatalf("collectCleanItems failed: %v", err)
	}
	got := make(map[string]bool, len(items))
	for _, item := range items {
		got[filepath.Base(item.path)] = true
	}
	for _, want := range []string{"Library", "logs", "Obj", ".VS"} {
		if !got[want] {
			t.Fatalf("cache directory %q must be collected case-insensitively, got %v", want, got)
		}
	}
}
