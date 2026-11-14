// Package projectroot locates the Unity project a command should act on. It
// centralizes discovery for every Unity-project tool so CLI runs, CI jobs and
// double-click launches resolve the same root.
//
// A destructive operation is only permitted against a root the caller named
// explicitly or the working directory itself. Inferred roots are reported with
// their origin, and a directory containing more than one project is never
// resolved implicitly: the caller must pass --project.
package projectroot

import (
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"sort"
	"strings"

	"cyclonegames.tools/scripts/internal/projectlayout"
	"cyclonegames.tools/scripts/internal/safefs"
)

const (
	maxWalkLevels       = 32
	maxDirectoryEntries = 4096
	maxTriedListed      = 24
)

// Validator confirms that a directory is a usable Unity project root and
// returns its canonical absolute path. Each command supplies its own strictness.
type Validator func(directory string) (string, error)

// Origin records which discovery step produced a resolved root.
type Origin int

const (
	OriginExplicit Origin = iota
	OriginWorkingDirectory
	OriginWorkingSubdirectory
	OriginAncestor
	OriginExecutableFallback
)

func (o Origin) String() string {
	switch o {
	case OriginExplicit:
		return "explicit --project path"
	case OriginWorkingDirectory:
		return "current working directory"
	case OriginWorkingSubdirectory:
		return "subdirectory of the current working directory"
	case OriginAncestor:
		return "ancestor of the current working directory"
	case OriginExecutableFallback:
		return "executable directory fallback"
	default:
		return "unknown"
	}
}

// RejectedCandidate records a directory that looked like a project but failed
// strict validation. It is diagnostic only and never blocks a healthy sibling.
type RejectedCandidate struct {
	Path string
	Err  error
}

// Result is a resolved root together with how it was found.
type Result struct {
	Root       string
	Origin     Origin
	Candidates []string
	Rejected   []RejectedCandidate
}

func (r Result) Ambiguous() bool {
	return r.Origin == OriginExecutableFallback || len(r.Candidates) > 1
}

func (r Result) Describe() string {
	return fmt.Sprintf("%s [source: %s]", r.Root, r.Origin)
}

// GuardDestructive refuses a destructive mutation unless the root was named by
// the caller or is the working directory itself. A run with several candidates
// is refused even in an interactive session: a menu cannot disambiguate safely.
func GuardDestructive(result Result, interactive bool) error {
	if len(result.Candidates) > 1 {
		return &AmbiguousRootsError{Roots: result.Candidates}
	}
	if result.Origin == OriginExplicit || result.Origin == OriginWorkingDirectory {
		return nil
	}
	if interactive {
		return nil
	}
	return fmt.Errorf(
		"refusing to modify %s: the project was inferred (%s). Run from the project root or pass --project <path>.",
		result.Root, result.Origin)
}

// FallbackWarning explains why a preview should be treated as advisory.
func (r Result) FallbackWarning() string {
	if len(r.Candidates) > 1 {
		return "several Unity project roots were found; pass --project <path> to select one explicitly."
	}
	if r.Origin != OriginExecutableFallback {
		return ""
	}
	return fmt.Sprintf(
		"the project root %s was inferred from the executable's own directory; pass --project <path> to be explicit.",
		r.Root)
}

// AmbiguousRootsError reports that discovery found more than one project.
type AmbiguousRootsError struct {
	Roots []string
}

func (e *AmbiguousRootsError) Error() string {
	roots := append([]string(nil), e.Roots...)
	sort.Strings(roots)
	var b strings.Builder
	b.WriteString("several Unity project roots were found; pass --project <path> to choose one:\n")
	for _, root := range roots {
		b.WriteString("  - ")
		b.WriteString(root)
		b.WriteByte('\n')
	}
	return b.String()
}

// Resolver applies the shared discovery order. The zero value is ready to use;
// the hooks let callers drive discovery without touching process state.
type Resolver struct {
	Validate   Validator
	Getwd      func() (string, error)
	Executable func() (string, error)
	Layout     projectlayout.Layout

	// stop bounds the upward walk in tests. Production resolvers leave it empty.
	stop string
}

var errNoCandidate = errors.New("not a Unity project root candidate")

type candidate struct {
	root   string
	origin Origin
}

// Resolve locates the project root using this order:
//  1. an explicit --project path, validated strictly with no fallback;
//  2. the working directory itself;
//  3. the working directory's immediate subdirectories;
//  4. ancestors of the working directory;
//  5. ancestors of the executable's directory and their subdirectories.
//
// Discovery stops at the first level that yields candidates. A level yielding
// more than one candidate returns AmbiguousRootsError instead of picking one.
func (r Resolver) Resolve(explicit string) (Result, error) {
	validate := r.validator()
	if trimmed := strings.TrimSpace(explicit); trimmed != "" {
		root, err := validate(trimmed)
		if err != nil {
			return Result{}, fmt.Errorf("the --project path %q is not a usable Unity project root: %w", trimmed, err)
		}
		return Result{Root: root, Origin: OriginExplicit, Candidates: []string{root}}, nil
	}

	cwd, cwdErr := r.getwd()
	facts := safefs.Fallback()
	if cwdErr == nil && cwd != "" {
		facts = safefs.FactsFor(cwd)
	}
	tried := make([]string, 0, 16)
	rejected := make([]RejectedCandidate, 0, 4)

	if cwdErr == nil && cwd != "" {
		result, err := r.searchFrom(cwd, false, OriginWorkingDirectory,
			OriginWorkingSubdirectory, OriginAncestor, facts, &tried, &rejected)
		if err == nil {
			return result, nil
		}
		if !errors.Is(err, errNoCandidate) {
			return Result{}, err
		}
	}
	if executable, err := r.executable(); err == nil && executable != "" {
		result, err := r.searchFrom(filepath.Dir(executable), true,
			OriginExecutableFallback, OriginExecutableFallback, OriginExecutableFallback,
			facts, &tried, &rejected)
		if err == nil {
			return result, nil
		}
		if !errors.Is(err, errNoCandidate) {
			return Result{}, err
		}
	}
	return Result{}, &NotFoundError{Tried: dedupe(tried), Rejected: rejected}
}

func (r Resolver) searchFrom(base string, scanEverywhere bool, selfOrigin, subdirectoryOrigin,
	ancestorOrigin Origin, facts safefs.Facts, tried *[]string, rejected *[]RejectedCandidate) (Result, error) {
	current := filepath.Clean(base)
	for level := 0; ; level++ {
		origin := ancestorOrigin
		if level == 0 {
			origin = selfOrigin
		}
		if found, err := r.matchCandidate(current, origin, tried); err == nil {
			return r.result(*found, rejected), nil
		} else if !errors.Is(err, errNoCandidate) {
			return Result{}, err
		}
		if level == 0 || scanEverywhere {
			if found, err := r.matchSubdirectories(current, subdirectoryOrigin, tried, rejected); err == nil {
				return r.result(*found, rejected), nil
			} else if !errors.Is(err, errNoCandidate) {
				return Result{}, err
			}
		}
		if r.stop != "" && safefs.EqualPath(current, r.stop, facts) {
			return Result{}, errNoCandidate
		}
		if level >= maxWalkLevels {
			return Result{}, errNoCandidate
		}
		parent := filepath.Dir(current)
		if parent == current {
			return Result{}, errNoCandidate
		}
		current = parent
	}
}

func (r Resolver) result(found candidate, rejected *[]RejectedCandidate) Result {
	return Result{
		Root:       found.root,
		Origin:     found.origin,
		Candidates: []string{found.root},
		Rejected:   append([]RejectedCandidate(nil), (*rejected)...),
	}
}

// matchCandidate fails closed when the directory itself looks like a project
// but strict validation rejects it: the caller clearly pointed at this root.
func (r Resolver) matchCandidate(directory string, origin Origin, tried *[]string) (*candidate, error) {
	*tried = append(*tried, directory)
	if !r.looksLike(directory) {
		return nil, errNoCandidate
	}
	root, err := r.validator()(directory)
	if err != nil {
		return nil, err
	}
	return &candidate{root: root, origin: origin}, nil
}

// matchSubdirectories collects every valid project in one directory. A sibling
// that fails strict validation is recorded and skipped so a single malformed
// project cannot hide a healthy one.
func (r Resolver) matchSubdirectories(directory string, origin Origin, tried *[]string,
	rejected *[]RejectedCandidate) (*candidate, error) {
	entries, err := os.ReadDir(directory)
	if err != nil {
		return nil, errNoCandidate
	}
	found := make([]candidate, 0, 2)
	for index, entry := range entries {
		if index >= maxDirectoryEntries {
			break
		}
		if !entry.IsDir() {
			continue
		}
		path := filepath.Join(directory, entry.Name())
		*tried = append(*tried, path)
		if !r.looksLike(path) {
			continue
		}
		root, validateErr := r.validator()(path)
		if validateErr != nil {
			*rejected = append(*rejected, RejectedCandidate{Path: path, Err: validateErr})
			continue
		}
		found = append(found, candidate{root: root, origin: origin})
	}
	if len(found) == 0 {
		return nil, errNoCandidate
	}
	if len(found) > 1 {
		roots := make([]string, 0, len(found))
		for _, item := range found {
			roots = append(roots, item.root)
		}
		return nil, &AmbiguousRootsError{Roots: roots}
	}
	return &found[0], nil
}

func (r Resolver) validator() Validator {
	if r.Validate != nil {
		return r.Validate
	}
	return MarkerValidate
}

func (r Resolver) layout() projectlayout.Layout {
	if len(r.Layout.Markers) != 0 {
		return r.Layout
	}
	return projectlayout.Default()
}

func (r Resolver) looksLike(directory string) bool {
	return r.layout().MatchesRoot(directory)
}

func (r Resolver) getwd() (string, error) {
	if r.Getwd != nil {
		return r.Getwd()
	}
	return os.Getwd()
}

func (r Resolver) executable() (string, error) {
	if r.Executable != nil {
		return r.Executable()
	}
	return os.Executable()
}

// MarkerValidate is the loosest validator: it requires the Unity structure
// markers and returns the canonical absolute path.
func MarkerValidate(directory string) (string, error) {
	if !LooksLikeProjectRoot(directory) {
		return "", fmt.Errorf("directory does not contain the Unity structure markers: %s", directory)
	}
	return canonical(directory)
}

// LooksLikeProjectRoot is the cheap pre-filter used while searching. It reads
// no files so many directories can be probed inexpensively.
func LooksLikeProjectRoot(directory string) bool {
	return projectlayout.Default().MatchesRoot(directory)
}

func canonical(directory string) (string, error) {
	absolute, err := filepath.Abs(directory)
	if err != nil {
		return "", err
	}
	return filepath.Clean(absolute), nil
}

// Locate resolves a root using only the structure markers.
func Locate(explicit string) (Result, error) {
	return Resolver{Validate: MarkerValidate, Layout: projectlayout.Default()}.Resolve(explicit)
}

// NotFoundError reports that no project root could be located.
type NotFoundError struct {
	Tried    []string
	Rejected []RejectedCandidate
}

func (e *NotFoundError) Error() string {
	var b strings.Builder
	b.WriteString("could not locate a Unity project root. Inspected:\n")
	listed := e.Tried
	if len(listed) > maxTriedListed {
		listed = listed[:maxTriedListed]
	}
	for _, directory := range listed {
		b.WriteString("  - ")
		b.WriteString(directory)
		b.WriteByte('\n')
	}
	if len(e.Tried) > len(listed) {
		fmt.Fprintf(&b, "  ... and %d more\n", len(e.Tried)-len(listed))
	}
	for _, rejected := range e.Rejected {
		fmt.Fprintf(&b, "  [invalid] %s: %v\n", rejected.Path, rejected.Err)
	}
	b.WriteString("Run the tool from a Unity project root (a directory containing Assets and ProjectSettings), or pass --project <path> to name it explicitly.")
	return b.String()
}

func dedupe(values []string) []string {
	seen := make(map[string]bool, len(values))
	result := make([]string, 0, len(values))
	for _, value := range values {
		if seen[value] {
			continue
		}
		seen[value] = true
		result = append(result, value)
	}
	return result
}
