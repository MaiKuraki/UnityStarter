// Package projectroot locates the Unity project root a tool command should act
// on. It centralizes the search order shared by every Unity-project tool so that
// double-click launches, repository-root launches, and CI invocations all resolve
// the same project instead of trusting the process working directory.
//
// Resolution is not silent: every result records the Origin it was found from,
// because the executable-directory fallback (step 5) can infer an unrelated
// project and must never be used to drive a destructive, non-interactive run
// without the caller noticing.
package projectroot

import (
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"runtime"
	"strings"
)

// Validator confirms that a directory is a usable Unity project root and returns
// its canonical absolute path. Each command supplies its own strictness: the
// cleanup and package tools additionally validate the manifest and ProjectVersion
// file, while the rename tool and the interactive menu only need the markers.
type Validator func(directory string) (string, error)

// Origin records which search step produced a resolved root. Callers use it to
// distinguish a root the user clearly meant (the working directory, a project
// subdirectory, or an explicit --project) from one merely inferred from where
// the executable happens to live.
type Origin int

const (
	// OriginExplicit is an explicit --project path. Unambiguous.
	OriginExplicit Origin = iota
	// OriginWorkingDirectory is the current working directory itself.
	OriginWorkingDirectory
	// OriginWorkingSubdirectory is an immediate subdirectory of the working directory.
	OriginWorkingSubdirectory
	// OriginAncestor is a project root found by walking up from the working directory.
	OriginAncestor
	// OriginExecutableFallback is a project root inferred from the executable's own
	// directory (and its ancestors/subdirectories). It makes a double-clicked
	// binary work, but it is ambiguous: any invocation from an unrelated directory
	// would silently resolve to the project that shipped the binary.
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

// Result is a resolved Unity project root together with how it was found.
type Result struct {
	Root   string
	Origin Origin
}

// Ambiguous reports whether the root was only inferred from the executable's
// location, which is the case that must be treated with care.
func (r Result) Ambiguous() bool {
	return r.Origin == OriginExecutableFallback
}

// Describe renders the root and its source for audit logs.
func (r Result) Describe() string {
	return fmt.Sprintf("%s [source: %s]", r.Root, r.Origin)
}

// GuardDestructive returns an actionable error when a destructive, non-interactive
// operation would run against a root inferred only from the executable's own
// directory. It returns nil for explicit/working-directory origins, for
// non-destructive operations (the caller passes interactive), and for interactive
// sessions (the double-click menu), which proceed after an explicit confirmation.
func GuardDestructive(result Result, interactive bool) error {
	if result.Origin != OriginExecutableFallback || interactive {
		return nil
	}
	return fmt.Errorf(
		"refusing to modify %s: the project was located by falling back to the executable's directory, which is ambiguous. Run from the project root or pass --project <path>.",
		result.Root)
}

// FallbackWarning returns a prominent warning for a non-destructive preview that
// runs against an executable-directory fallback root, or "" for any other origin.
func (r Result) FallbackWarning() string {
	if r.Origin != OriginExecutableFallback {
		return ""
	}
	return fmt.Sprintf(
		"the project root %s was inferred from the executable's own directory, not from the working directory or --project; pass --project <path> to be explicit.",
		r.Root)
}

// Resolver applies the shared search order. The zero value is ready to use; the
// Getwd and Executable hooks exist so tests can drive the search without touching
// the real process state.
type Resolver struct {
	Validate   Validator
	Getwd      func() (string, error)
	Executable func() (string, error)

	// stop bounds the upward walk once it has processed this directory. It is an
	// internal test seam: production resolvers leave it empty and walk to the
	// filesystem root, while tests keep the walk inside their own fixture so an
	// unrelated Unity project elsewhere on the machine cannot influence them.
	stop string
}

// errNoCandidate marks a directory that is not a Unity project root at all, as
// opposed to one that matched the markers but failed strict validation. It is an
// internal sentinel and never escapes Resolve.
var errNoCandidate = errors.New("not a Unity project root candidate")

// Resolve locates the Unity project root using this order:
//
//  1. an explicit --project path, which is validated strictly and never falls back
//     (OriginExplicit);
//  2. the current working directory, when it is a project root (OriginWorkingDirectory);
//  3. a Unity project root among the working directory's immediate subdirectories
//     (OriginWorkingSubdirectory);
//  4. walking up from the working directory, checking each ancestor itself
//     (OriginAncestor);
//  5. walking up from the executable's directory, checking each ancestor and its
//     immediate subdirectories (OriginExecutableFallback). This is what makes a
//     double-clicked binary work, because Windows sets the working directory to
//     the binary's own folder, whose ancestor is the repository root that holds
//     the project as a subfolder.
//
// When every step fails it returns a *NotFoundError listing the inspected
// directories so the caller can act on it (for example with --project).
func (r Resolver) Resolve(explicit string) (Result, error) {
	validate := r.validator()
	if trimmed := strings.TrimSpace(explicit); trimmed != "" {
		root, err := validate(trimmed)
		if err != nil {
			return Result{}, fmt.Errorf("the --project path %q is not a usable Unity project root: %w", trimmed, err)
		}
		return Result{Root: root, Origin: OriginExplicit}, nil
	}

	tried := make([]string, 0, 16)
	if cwd, err := r.getwd(); err == nil && cwd != "" {
		result, err := r.searchFrom(cwd, false, OriginWorkingDirectory, OriginWorkingSubdirectory, OriginAncestor, &tried)
		if err == nil {
			return result, nil
		} else if !errors.Is(err, errNoCandidate) {
			return Result{}, err
		}
	}
	if executable, err := r.executable(); err == nil && executable != "" {
		result, err := r.searchFrom(filepath.Dir(executable), true, OriginExecutableFallback, OriginExecutableFallback, OriginExecutableFallback, &tried)
		if err == nil {
			return result, nil
		} else if !errors.Is(err, errNoCandidate) {
			return Result{}, err
		}
	}
	return Result{}, &NotFoundError{Tried: dedupe(tried)}
}

// searchFrom walks from base toward the filesystem root. Immediate
// subdirectories are always scanned at base itself; scanSubdirectoriesEverywhere
// additionally scans them at every ancestor (used for the executable directory,
// whose ancestor is the repository root that holds the project as a subfolder).
func (r Resolver) searchFrom(base string, scanSubdirectoriesEverywhere bool, selfOrigin, subdirectoryOrigin, ancestorOrigin Origin, tried *[]string) (Result, error) {
	current := filepath.Clean(base)
	first := true
	for {
		origin := ancestorOrigin
		if first {
			origin = selfOrigin
		}
		if result, err := r.matchCandidate(current, origin, tried); err == nil {
			return result, nil
		} else if !errors.Is(err, errNoCandidate) {
			return Result{}, err
		}
		if first || scanSubdirectoriesEverywhere {
			if result, err := r.matchSubdirectories(current, subdirectoryOrigin, tried); err == nil {
				return result, nil
			} else if !errors.Is(err, errNoCandidate) {
				return Result{}, err
			}
		}
		if r.stop != "" && sameDir(current, r.stop) {
			return Result{}, errNoCandidate
		}
		first = false
		parent := filepath.Dir(current)
		if parent == current {
			return Result{}, errNoCandidate
		}
		current = parent
	}
}

// sameDir compares two directory paths the way the host filesystem does: Windows
// is case-insensitive, every other platform is not.
func sameDir(left, right string) bool {
	left = filepath.Clean(left)
	right = filepath.Clean(right)
	if runtime.GOOS == "windows" {
		return strings.EqualFold(left, right)
	}
	return left == right
}

// matchCandidate returns the validated result for directory with the given
// origin, errNoCandidate when it merely lacks the markers, or the validator's own
// error when it looks like a project but fails strict validation (which the caller
// surfaces immediately, so a half-initialized project fails closed instead of
// being silently skipped).
func (r Resolver) matchCandidate(directory string, origin Origin, tried *[]string) (Result, error) {
	*tried = append(*tried, directory)
	if !LooksLikeProjectRoot(directory) {
		return Result{}, errNoCandidate
	}
	root, err := r.validator()(directory)
	if err != nil {
		return Result{}, err
	}
	return Result{Root: root, Origin: origin}, nil
}

func (r Resolver) matchSubdirectories(directory string, origin Origin, tried *[]string) (Result, error) {
	entries, err := os.ReadDir(directory)
	if err != nil {
		return Result{}, errNoCandidate
	}
	for _, entry := range entries {
		if !entry.IsDir() {
			continue
		}
		if result, err := r.matchCandidate(filepath.Join(directory, entry.Name()), origin, tried); err == nil {
			return result, nil
		} else if !errors.Is(err, errNoCandidate) {
			return Result{}, err
		}
	}
	return Result{}, errNoCandidate
}

func (r Resolver) validator() Validator {
	if r.Validate != nil {
		return r.Validate
	}
	return MarkerValidate
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

// MarkerValidate is the loosest validator: it only requires the Assets and
// ProjectSettings directory markers and returns the canonical absolute path.
func MarkerValidate(directory string) (string, error) {
	if !LooksLikeProjectRoot(directory) {
		return "", fmt.Errorf("directory does not contain the Unity markers 'Assets' and 'ProjectSettings': %s", directory)
	}
	return canonical(directory)
}

// LooksLikeProjectRoot is the cheap pre-filter used while searching: both Unity
// directory markers must exist as directories. It deliberately reads no files so
// the search can probe many ancestors cheaply; strict validation happens once a
// candidate is chosen.
func LooksLikeProjectRoot(directory string) bool {
	for _, marker := range []string{"Assets", "ProjectSettings"} {
		info, err := os.Stat(filepath.Join(directory, marker))
		if err != nil || !info.IsDir() {
			return false
		}
	}
	return true
}

func canonical(directory string) (string, error) {
	absolute, err := filepath.Abs(directory)
	if err != nil {
		return "", err
	}
	return filepath.Clean(absolute), nil
}

// Locate is the convenience entry point for callers that only need the marker
// check (the interactive menu and the rename tool). It resolves the root from the
// optional explicit path plus the shared search order and reports its origin.
func Locate(explicit string) (Result, error) {
	return Resolver{Validate: MarkerValidate}.Resolve(explicit)
}

// NotFoundError reports that no Unity project root could be located. It lists the
// inspected directories and the actionable next step.
type NotFoundError struct {
	Tried []string
}

func (e *NotFoundError) Error() string {
	var builder strings.Builder
	builder.WriteString("could not locate a Unity project root. Inspected:\n")
	for _, directory := range e.Tried {
		builder.WriteString("  - ")
		builder.WriteString(directory)
		builder.WriteByte('\n')
	}
	builder.WriteString("Run the tool from a Unity project root (a directory containing Assets, ProjectSettings and Packages), or pass --project <path> to name it explicitly.")
	return builder.String()
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
