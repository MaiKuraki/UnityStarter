package projectroot

import (
	"errors"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"cyclonegames.tools/scripts/internal/projectlayout"
)

func makeProject(t *testing.T, parent, name string) string {
	t.Helper()
	project := filepath.Join(parent, name)
	for _, marker := range projectlayout.Default().Markers {
		if err := os.MkdirAll(filepath.Join(project, filepath.FromSlash(marker)), 0o755); err != nil {
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

// bounded keeps the upward walk inside the fixture so an unrelated project
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

func TestExplicitPathWinsAndValidates(t *testing.T) {
	repo := t.TempDir()
	project := makeProject(t, repo, "ProjectA")
	resolver := bounded(fixedResolver(filepath.Join(repo, "elsewhere"), filepath.Join(repo, "bin", "tool")), repo)
	result := mustResolve(t, resolver, project)
	if result.Root != project || result.Origin != OriginExplicit {
		t.Fatalf("resolved %+v, want explicit %s", result, project)
	}
}

func TestExplicitPathFailsClosedWithoutFallback(t *testing.T) {
	repo := t.TempDir()
	makeProject(t, repo, "ProjectA")
	missing := filepath.Join(repo, "does-not-exist")
	resolver := bounded(fixedResolver(repo, filepath.Join(repo, "bin", "tool")), repo)
	if _, err := resolver.Resolve(missing); err == nil || !strings.Contains(err.Error(), "--project") {
		t.Fatalf("an invalid explicit path must fail closed and explain --project: %v", err)
	}
}

func TestWorkingDirectoryIsProjectRoot(t *testing.T) {
	project := makeProject(t, t.TempDir(), "ProjectA")
	resolver := bounded(fixedResolver(project, filepath.Join(project, "..", "bin", "tool")), project)
	result := mustResolve(t, resolver, "")
	if result.Root != project || result.Origin != OriginWorkingDirectory {
		t.Fatalf("resolved %+v, want working directory %s", result, project)
	}
}

func TestSingleSubdirectoryProject(t *testing.T) {
	repo := t.TempDir()
	project := makeProject(t, repo, "ProjectA")
	resolver := bounded(fixedResolver(repo, filepath.Join(repo, "bin", "tool")), repo)
	result := mustResolve(t, resolver, "")
	if result.Root != project || result.Origin != OriginWorkingSubdirectory {
		t.Fatalf("resolved %+v, want subdirectory %s", result, project)
	}
}

func TestWalksUpFromWorkingDirectory(t *testing.T) {
	repo := t.TempDir()
	project := makeProject(t, repo, "ProjectA")
	deep := filepath.Join(project, "Assets", "Editor", "Scenes")
	if err := os.MkdirAll(deep, 0o755); err != nil {
		t.Fatal(err)
	}
	resolver := bounded(fixedResolver(deep, filepath.Join(repo, "bin", "tool")), repo)
	result := mustResolve(t, resolver, "")
	if result.Root != project || result.Origin != OriginAncestor {
		t.Fatalf("resolved %+v, want ancestor %s", result, project)
	}
}

func TestExecutableDirectoryFallback(t *testing.T) {
	repo := t.TempDir()
	project := makeProject(t, repo, "ProjectA")
	executable := filepath.Join(repo, "Tools", "Executable", "windows", "amd64", "tool")
	if err := os.MkdirAll(filepath.Dir(executable), 0o755); err != nil {
		t.Fatal(err)
	}
	resolver := bounded(fixedResolver(filepath.Dir(executable), executable), repo)
	result := mustResolve(t, resolver, "")
	if result.Root != project || result.Origin != OriginExecutableFallback || !result.Ambiguous() {
		t.Fatalf("resolved %+v, want ambiguous executable fallback to %s", result, project)
	}
}

// A parent holding two projects must never be resolved implicitly. Silent
// selection here would let a destructive command act on the wrong project.
func TestMultipleProjectsAreAmbiguous(t *testing.T) {
	repo := t.TempDir()
	first := makeProject(t, repo, "ProjectA")
	second := makeProject(t, repo, "ProjectB")
	resolver := bounded(fixedResolver(repo, filepath.Join(repo, "bin", "tool")), repo)
	_, err := resolver.Resolve("")
	var ambiguous *AmbiguousRootsError
	if !errors.As(err, &ambiguous) {
		t.Fatalf("two projects must produce AmbiguousRootsError, got %v", err)
	}
	if len(ambiguous.Roots) != 2 {
		t.Fatalf("error must list both candidates, got %v", ambiguous.Roots)
	}
	for _, want := range []string{first, second} {
		if !strings.Contains(err.Error(), want) {
			t.Fatalf("error must name %s, got %v", want, err)
		}
	}
	// An explicit choice still works.
	result := mustResolve(t, resolver, second)
	if result.Root != second || result.Origin != OriginExplicit {
		t.Fatalf("explicit selection failed: %+v", result)
	}
}

// A malformed sibling must not hide a healthy project in the same directory.
func TestInvalidSiblingDoesNotHideHealthyProject(t *testing.T) {
	repo := t.TempDir()
	healthy := makeProject(t, repo, "ProjectA")
	broken := makeProject(t, repo, "ProjectB")
	if err := os.RemoveAll(filepath.Join(broken, "ProjectSettings")); err != nil {
		t.Fatal(err)
	}
	if err := os.MkdirAll(filepath.Join(broken, "ProjectSettings"), 0o755); err != nil {
		t.Fatal(err)
	}
	resolver := bounded(Resolver{
		Validate: func(directory string) (string, error) {
			if directory == broken {
				return "", errors.New("manifest is invalid")
			}
			return MarkerValidate(directory)
		},
		Getwd:      func() (string, error) { return repo, nil },
		Executable: func() (string, error) { return filepath.Join(repo, "bin", "tool"), nil },
	}, repo)
	result := mustResolve(t, resolver, "")
	if result.Root != healthy {
		t.Fatalf("resolved %s, want healthy project %s", result.Root, healthy)
	}
	if len(result.Rejected) != 1 || result.Rejected[0].Path != broken {
		t.Fatalf("invalid sibling must be reported, got %+v", result.Rejected)
	}
}

func TestNotFoundListsTriedPathsAndHintsProject(t *testing.T) {
	repo := t.TempDir()
	deep := filepath.Join(repo, "a", "b", "c")
	if err := os.MkdirAll(deep, 0o755); err != nil {
		t.Fatal(err)
	}
	resolver := bounded(fixedResolver(deep, filepath.Join(repo, "bin", "tool")), repo)
	_, err := resolver.Resolve("")
	if err == nil || !strings.Contains(err.Error(), "--project") || !strings.Contains(err.Error(), deep) {
		t.Fatalf("not-found error must list inspected paths and suggest --project: %v", err)
	}
}

func TestMarkerValidateRequiresStructureMarkers(t *testing.T) {
	directory := t.TempDir()
	if _, err := MarkerValidate(directory); err == nil {
		t.Fatal("a directory without markers must be rejected")
	}
	if err := os.MkdirAll(filepath.Join(directory, "Assets"), 0o755); err != nil {
		t.Fatal(err)
	}
	if _, err := MarkerValidate(directory); err == nil {
		t.Fatal("one marker alone must not be accepted")
	}
	if err := os.MkdirAll(filepath.Join(directory, "ProjectSettings"), 0o755); err != nil {
		t.Fatal(err)
	}
	if _, err := MarkerValidate(directory); err != nil {
		t.Fatalf("complete markers must be accepted: %v", err)
	}
}

func TestWorkingDirectoryValidationFailureSurfacesImmediately(t *testing.T) {
	project := makeProject(t, t.TempDir(), "ProjectA")
	resolver := bounded(Resolver{
		Validate: func(string) (string, error) { return "", os.ErrInvalid },
		Getwd:    func() (string, error) { return project, nil },
		Executable: func() (string, error) {
			return filepath.Join(project, "..", "bin", "tool"), nil
		},
	}, project)
	if _, err := resolver.Resolve(""); err == nil {
		t.Fatal("a marker-matching but invalid working directory must fail closed")
	}
}

func TestCustomLayoutMarkers(t *testing.T) {
	repo := t.TempDir()
	project := filepath.Join(repo, "ProjectA")
	if err := os.MkdirAll(filepath.Join(project, "Source"), 0o755); err != nil {
		t.Fatal(err)
	}
	layout := projectlayout.Default()
	layout.Markers = []string{"Source"}
	resolver := bounded(Resolver{
		Validate: func(directory string) (string, error) {
			if !layout.MatchesRoot(directory) {
				return "", errors.New("missing layout markers")
			}
			return filepath.Abs(directory)
		},
		Layout:     layout,
		Getwd:      func() (string, error) { return repo, nil },
		Executable: func() (string, error) { return filepath.Join(repo, "bin", "tool"), nil },
	}, repo)
	result := mustResolve(t, resolver, "")
	if result.Root != project {
		t.Fatalf("custom layout must resolve %s, got %s", project, result.Root)
	}
}

func TestGuardDestructiveContract(t *testing.T) {
	explicit := Result{Root: "ProjectA", Origin: OriginExplicit, Candidates: []string{"ProjectA"}}
	working := Result{Root: "ProjectA", Origin: OriginWorkingDirectory, Candidates: []string{"ProjectA"}}
	for _, result := range []Result{explicit, working} {
		if err := GuardDestructive(result, false); err != nil {
			t.Fatalf("origin %v must never require confirmation: %v", result.Origin, err)
		}
	}
	for _, origin := range []Origin{OriginWorkingSubdirectory, OriginAncestor, OriginExecutableFallback} {
		result := Result{Root: "ProjectA", Origin: origin, Candidates: []string{"ProjectA"}}
		if err := GuardDestructive(result, false); err == nil {
			t.Fatalf("inferred origin %v must be refused for a non-interactive mutation", origin)
		}
		if err := GuardDestructive(result, true); err != nil {
			t.Fatalf("interactive session may proceed after confirmation: %v", err)
		}
	}
	ambiguous := Result{Root: "ProjectA", Origin: OriginWorkingSubdirectory,
		Candidates: []string{"ProjectA", "ProjectB"}}
	for _, interactive := range []bool{false, true} {
		if err := GuardDestructive(ambiguous, interactive); err == nil {
			t.Fatal("multiple candidates must always be refused")
		}
	}
}

func TestResultDescribeAndWarning(t *testing.T) {
	fallback := Result{Root: "ProjectA", Origin: OriginExecutableFallback, Candidates: []string{"ProjectA"}}
	if !strings.Contains(fallback.Describe(), "ProjectA") || !strings.Contains(fallback.Describe(), "executable") {
		t.Fatalf("Describe must include root and origin: %s", fallback.Describe())
	}
	if fallback.FallbackWarning() == "" {
		t.Fatal("an inferred root must produce a warning")
	}
	explicit := Result{Root: "ProjectA", Origin: OriginExplicit, Candidates: []string{"ProjectA"}}
	if explicit.FallbackWarning() != "" {
		t.Fatalf("an explicit root must not warn: %s", explicit.FallbackWarning())
	}
	for origin, want := range map[Origin]string{
		OriginExplicit:            "explicit",
		OriginWorkingDirectory:    "current working directory",
		OriginWorkingSubdirectory: "subdirectory",
		OriginAncestor:            "ancestor",
		OriginExecutableFallback:  "executable",
	} {
		if !strings.Contains(origin.String(), want) {
			t.Fatalf("Origin.String() for %d = %q, want %q", origin, origin.String(), want)
		}
	}
}
