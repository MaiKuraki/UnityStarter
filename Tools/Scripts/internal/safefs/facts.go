package safefs

import (
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"strings"
)

// Facts describes filesystem behavior that cannot be inferred reliably from
// GOOS: a macOS volume may be case-insensitive, a Windows directory may be
// marked case-sensitive, and Linux bind mounts may differ from their parent.
type Facts struct {
	CaseSensitive     bool
	NormalizesUnicode bool
	Probed            bool
}

// Fallback is used only when a probe cannot be performed. It is deliberately
// conservative: treating a volume as case-sensitive avoids silently merging two
// distinct paths, which is the safer failure mode for destructive operations.
func Fallback() Facts {
	return Facts{CaseSensitive: true, NormalizesUnicode: false, Probed: false}
}

// Probe measures case sensitivity on the volume that holds root by creating a
// disposable probe directory and comparing two spellings of the same name.
func Probe(root string) (Facts, error) {
	probeDir, err := os.MkdirTemp(root, ".fsprobe-*")
	if err != nil {
		return Facts{}, fmt.Errorf("cannot probe filesystem case sensitivity in %s: %w", root, err)
	}
	defer func() { _ = os.RemoveAll(probeDir) }()

	lower := filepath.Join(probeDir, "caseprobe")
	if err := os.WriteFile(lower, []byte("probe"), 0o600); err != nil {
		return Facts{}, fmt.Errorf("cannot write filesystem probe: %w", err)
	}
	lowerInfo, err := os.Lstat(lower)
	if err != nil {
		return Facts{}, err
	}
	upperInfo, err := os.Lstat(filepath.Join(probeDir, "CASEPROBE"))
	caseSensitive := true
	if err == nil {
		caseSensitive = !os.SameFile(lowerInfo, upperInfo)
	} else if !errors.Is(err, os.ErrNotExist) {
		return Facts{}, err
	}

	// APFS and HFS+ store names in a normalized form. A name written in NFC can
	// therefore be reached by its NFD spelling, which would otherwise look like
	// two distinct entries to a path-uniqueness check.
	composed := filepath.Join(probeDir, "probe-\u00e9")
	decomposed := filepath.Join(probeDir, "probe-e\u0301")
	if err := os.WriteFile(composed, []byte("probe"), 0o600); err != nil {
		return Facts{}, err
	}
	composedInfo, err := os.Lstat(composed)
	if err != nil {
		return Facts{}, err
	}
	normalizes := false
	if decomposedInfo, err := os.Lstat(decomposed); err == nil {
		normalizes = os.SameFile(composedInfo, decomposedInfo)
	} else if !errors.Is(err, os.ErrNotExist) {
		return Facts{}, err
	}
	return Facts{CaseSensitive: caseSensitive, NormalizesUnicode: normalizes, Probed: true}, nil
}

// FactsFor probes root and falls back to the conservative default when the
// volume cannot be written to. Callers that require a measured answer should
// use Probe directly.
func FactsFor(root string) Facts {
	facts, err := Probe(root)
	if err != nil {
		return Fallback()
	}
	return facts
}

// EqualPath compares two paths using the measured semantics of the volume.
func EqualPath(left, right string, facts Facts) bool {
	left = filepath.Clean(left)
	right = filepath.Clean(right)
	if facts.CaseSensitive {
		return left == right
	}
	return strings.EqualFold(left, right)
}

// IsWithin reports whether candidate is strictly below root. Traversal is
// checked lexically first and then again against the volume's case behavior.
func IsWithin(root, candidate string, facts Facts) bool {
	root = filepath.Clean(root)
	candidate = filepath.Clean(candidate)
	rel, err := filepath.Rel(root, candidate)
	if err != nil || rel == "." || rel == "" || filepath.IsAbs(rel) {
		return false
	}
	if rel == ".." || strings.HasPrefix(rel, ".."+string(os.PathSeparator)) {
		return false
	}
	prefix := root + string(os.PathSeparator)
	if facts.CaseSensitive {
		return strings.HasPrefix(candidate, prefix)
	}
	return strings.HasPrefix(strings.ToLower(candidate), strings.ToLower(prefix))
}

// ValidateComponentLength rejects names that exceed portable filesystem limits.
func ValidateComponentLength(name string) error {
	if name == "" || name == "." || name == ".." {
		return fmt.Errorf("invalid path component %q", name)
	}
	if len(name) > 255 {
		return fmt.Errorf("path component exceeds 255 bytes: %s", name)
	}
	return nil
}
