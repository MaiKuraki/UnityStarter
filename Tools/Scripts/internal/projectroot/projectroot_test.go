package projectroot

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
)

// makeUnityProject creates <parent>/UnityStarter with the three Unity markers and
// returns its absolute path.
func makeUnityProject(t *testing.T, parent string) string {
	t.Helper()
	project := filepath.Join(parent, "UnityStarter")
	for _, marker := range []string{"Assets", "ProjectSettings", "Packages"} {
		if err := os.MkdirAll(filepath.Join(project, marker), 0o755); err != nil {
			t.Fatalf("cannot create marker %s: %v", marker, err)
		}
	}
	return project
}

func fixedResolver(cwd, executable string) Resolver {
	return Resolver{
		Validate:   MarkerValidate,
		Getwd:      func() (string, error) { return cwd, nil },
		Executable: func() (string, error) { return executable, nil },
	}
}

// bounded keeps the upward walk inside the fixture so an unrelated Unity project
// elsewhere on the machine cannot make a test non-deterministic.
func bounded(resolver Resolver, directory string) Resolver {
	resolver.stop = directory
	return resolver
}

func mustResolve(t *testing.T, resolver Resolver, explicit string) Result {
	t.Helper()
	result, err := resolver.Resolve(explicit)
	if err != nil {
		t.Fatalf("Resolve(%q) failed: %v", explicit, err)
	}
	return result
}

func TestResolveExplicitPathWinsAndValidates(t *testing.T) {
	repo := t.TempDir()
	project := makeUnityProject(t, repo)

	resolver := bounded(fixedResolver(filepath.Join(repo, "elsewhere"), filepath.Join(repo, "bin", "tool")), repo)
	result := mustResolve(t, resolver, project)
	if result.Root != project {
		t.Fatalf("resolved %q, want %q", result.Root, project)
	}
	if result.Origin != OriginExplicit {
		t.Fatalf("origin = %v, want %v", result.Origin, OriginExplicit)
	}
}

func TestResolveExplicitPathFailsClosedWithoutFallback(t *testing.T) {
	repo := t.TempDir()
	makeUnityProject(t, repo)
	missing := filepath.Join(repo, "does-not-exist")

	// Even though the CWD holds a valid project, an explicit bad path must not
	// silently fall back to it.
	resolver := bounded(fixedResolver(repo, filepath.Join(repo, "bin", "tool")), repo)
	_, err := resolver.Resolve(missing)
	if err == nil {
		t.Fatalf("a missing explicit project path must fail")
	}
	if !strings.Contains(err.Error(), "--project") {
		t.Fatalf("error must mention --project, got: %v", err)
	}
}

func TestResolveCWDIsProjectRoot(t *testing.T) {
	project := makeUnityProject(t, t.TempDir())
	resolver := bounded(fixedResolver(project, filepath.Join(project, "..", "bin", "tool")), project)
	result := mustResolve(t, resolver, "")
	if result.Root != project {
		t.Fatalf("resolved %q, want %q", result.Root, project)
	}
	if result.Origin != OriginWorkingDirectory {
		t.Fatalf("origin = %v, want %v", result.Origin, OriginWorkingDirectory)
	}
}

// Running from the repository root must find the Unity project in an immediate
// subdirectory (the "UnityStarter is a subfolder of the repo" layout).
func TestResolveCWDSubdirectoryProject(t *testing.T) {
	repo := t.TempDir()
	project := makeUnityProject(t, repo)
	resolver := bounded(fixedResolver(repo, filepath.Join(repo, "bin", "tool")), repo)
	result := mustResolve(t, resolver, "")
	if result.Root != project {
		t.Fatalf("resolved %q, want %q", result.Root, project)
	}
	if result.Origin != OriginWorkingSubdirectory {
		t.Fatalf("origin = %v, want %v", result.Origin, OriginWorkingSubdirectory)
	}
}

func TestResolveWalksUpFromCWD(t *testing.T) {
	repo := t.TempDir()
	project := makeUnityProject(t, repo)
	deep := filepath.Join(project, "Assets", "UnityStarter", "Scenes")
	if err := os.MkdirAll(deep, 0o755); err != nil {
		t.Fatalf("cannot create deep directory: %v", err)
	}
	resolver := bounded(fixedResolver(deep, filepath.Join(repo, "bin", "tool")), repo)
	result := mustResolve(t, resolver, "")
	if result.Root != project {
		t.Fatalf("resolved %q, want %q", result.Root, project)
	}
	if result.Origin != OriginAncestor {
		t.Fatalf("origin = %v, want %v", result.Origin, OriginAncestor)
	}
}

// Simulates a Windows double-click: the working directory is the executable's own
// folder (Tools/Executable/<OS>/<ARCH>), and only the executable's ancestor
// (the repository root) holds the project as a subdirectory. This origin is the
// ambiguous fallback that destructive tools must guard against.
func TestResolveFromExecutableDirectoryDoubleClick(t *testing.T) {
	repo := t.TempDir()
	project := makeUnityProject(t, repo)
	executable := filepath.Join(repo, "Tools", "Executable", "Windows", "amd64", "unity-project-tools.exe")

	resolver := bounded(fixedResolver(filepath.Dir(executable), executable), repo)
	result := mustResolve(t, resolver, "")
	if result.Root != project {
		t.Fatalf("resolved %q, want %q", result.Root, project)
	}
	if result.Origin != OriginExecutableFallback {
		t.Fatalf("origin = %v, want %v", result.Origin, OriginExecutableFallback)
	}
	if !result.Ambiguous() {
		t.Fatalf("executable fallback must be reported as ambiguous")
	}
}

func TestResolveNotFoundListsTriedPathsAndHintsProject(t *testing.T) {
	repo := t.TempDir()
	deep := filepath.Join(repo, "a", "b", "c")
	if err := os.MkdirAll(deep, 0o755); err != nil {
		t.Fatalf("cannot create fixture: %v", err)
	}
	resolver := bounded(fixedResolver(deep, filepath.Join(repo, "bin", "tool")), repo)
	_, err := resolver.Resolve("")
	if err == nil {
		t.Fatalf("empty tree must not resolve")
	}
	message := err.Error()
	if !strings.Contains(message, "--project") {
		t.Fatalf("not-found error must suggest --project, got:\n%s", message)
	}
	if !strings.Contains(message, deep) {
		t.Fatalf("not-found error must list the inspected directories, got:\n%s", message)
	}
}

func TestMarkerValidateRequiresBothMarkers(t *testing.T) {
	directory := t.TempDir()
	if _, err := MarkerValidate(directory); err == nil {
		t.Fatalf("a directory without markers must be rejected")
	}
	if err := os.MkdirAll(filepath.Join(directory, "Assets"), 0o755); err != nil {
		t.Fatalf("cannot create Assets: %v", err)
	}
	if _, err := MarkerValidate(directory); err == nil {
		t.Fatalf("Assets alone must not be accepted")
	}
	if err := os.MkdirAll(filepath.Join(directory, "ProjectSettings"), 0o755); err != nil {
		t.Fatalf("cannot create ProjectSettings: %v", err)
	}
	if _, err := MarkerValidate(directory); err != nil {
		t.Fatalf("complete markers must be accepted: %v", err)
	}
}

// A directory that matches the markers but fails strict validation must surface
// the validator's error rather than being silently skipped.
func TestResolveSurfacesStrictValidationFailure(t *testing.T) {
	project := makeUnityProject(t, t.TempDir())
	resolver := bounded(Resolver{
		Validate: func(string) (string, error) { return "", os.ErrInvalid },
		Getwd:    func() (string, error) { return project, nil },
		Executable: func() (string, error) {
			return filepath.Join(project, "..", "bin", "tool"), nil
		},
	}, project)
	if _, err := resolver.Resolve(""); err == nil {
		t.Fatalf("a marker-matching but invalid project must fail closed")
	}
}

// The P0 guard: a destructive, non-interactive run must refuse an
// executable-directory fallback root, while interactive sessions and the
// non-ambiguous origins are allowed through.
func TestGuardDestructiveRejectsAmbiguousFallback(t *testing.T) {
	ambiguous := Result{Root: `/repo/UnityStarter`, Origin: OriginExecutableFallback}

	err := GuardDestructive(ambiguous, false)
	if err == nil {
		t.Fatalf("non-interactive executable fallback must be refused")
	}
	if !strings.Contains(err.Error(), ambiguous.Root) || !strings.Contains(err.Error(), "--project") {
		t.Fatalf("refusal must name the path and suggest --project, got: %v", err)
	}
	if GuardDestructive(ambiguous, true) != nil {
		t.Fatalf("an interactive session may proceed after confirmation")
	}

	for _, origin := range []Origin{OriginExplicit, OriginWorkingDirectory, OriginWorkingSubdirectory, OriginAncestor} {
		if err := GuardDestructive(Result{Root: `/repo/UnityStarter`, Origin: origin}, false); err != nil {
			t.Fatalf("origin %v must not be refused, got: %v", origin, err)
		}
	}
}

func TestResultDescribeAndWarning(t *testing.T) {
	fallback := Result{Root: `/repo/UnityStarter`, Origin: OriginExecutableFallback}
	if !strings.Contains(fallback.Describe(), "/repo/UnityStarter") || !strings.Contains(fallback.Describe(), "executable") {
		t.Fatalf("Describe must include the root and origin, got: %s", fallback.Describe())
	}
	if fallback.FallbackWarning() == "" {
		t.Fatalf("fallback must produce a warning")
	}
	explicit := Result{Root: `/repo/UnityStarter`, Origin: OriginExplicit}
	if explicit.FallbackWarning() != "" {
		t.Fatalf("explicit origin must not warn, got: %s", explicit.FallbackWarning())
	}
	for origin, want := range map[Origin]string{
		OriginExplicit:            "explicit",
		OriginWorkingDirectory:    "current working directory",
		OriginWorkingSubdirectory: "subdirectory",
		OriginAncestor:            "ancestor",
		OriginExecutableFallback:  "executable",
	} {
		if !strings.Contains(origin.String(), want) {
			t.Fatalf("Origin.String() for %d = %q, want to contain %q", origin, origin.String(), want)
		}
	}
}
