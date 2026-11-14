package safefs

import (
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"strings"
)

var (
	ErrPathTooLong = errors.New("path exceeds the host filesystem limit")
	ErrInvalidName = errors.New("path component is not valid on this host")
	ErrNotWritable = errors.New("directory is not writable by this process")
)

// PathPolicy captures the naming and length rules of the host. Tools validate
// generated names against it before creating temporary files, publishing an
// output or deleting a tree, so an unsupported path fails with an actionable
// error instead of an opaque OS failure.
type PathPolicy struct {
	MaxPathBytes       int
	MaxComponentBytes  int
	SupportsLongPrefix bool
	Reserved           func(name string) bool
	Forbidden          func(name string) bool
}

// HostPathPolicy returns the rules for the running platform.
func HostPathPolicy() PathPolicy { return hostPathPolicy() }

// ValidateComponent checks one path segment against host rules.
func (p PathPolicy) ValidateComponent(name string) error {
	if name == "" || name == "." || name == ".." {
		return fmt.Errorf("%w: empty or traversal component", ErrInvalidName)
	}
	if len(name) > p.MaxComponentBytes {
		return fmt.Errorf("%w: component %q is %d bytes, limit %d", ErrInvalidName, name, len(name), p.MaxComponentBytes)
	}
	for _, r := range name {
		if r == 0 || r < 0x20 || r == 0x7f {
			return fmt.Errorf("%w: component contains a control character", ErrInvalidName)
		}
	}
	if p.Forbidden != nil && p.Forbidden(name) {
		return fmt.Errorf("%w: component %q contains a reserved character", ErrInvalidName, name)
	}
	if p.Reserved != nil && p.Reserved(name) {
		return fmt.Errorf("%w: component %q is a reserved device name", ErrInvalidName, name)
	}
	return nil
}

// ValidateRelative checks a portable relative path.
func (p PathPolicy) ValidateRelative(relative string) error {
	if strings.TrimSpace(relative) == "" {
		return fmt.Errorf("%w: empty path", ErrInvalidName)
	}
	if filepath.IsAbs(relative) || filepath.VolumeName(relative) != "" {
		return fmt.Errorf("%w: path must be relative: %s", ErrInvalidName, relative)
	}
	for _, component := range strings.Split(filepath.ToSlash(filepath.Clean(filepath.FromSlash(relative))), "/") {
		if component == "." {
			continue
		}
		if component == ".." {
			return fmt.Errorf("%w: parent traversal is not allowed: %s", ErrInvalidName, relative)
		}
		if err := p.ValidateComponent(component); err != nil {
			return err
		}
	}
	return nil
}

// ValidateAbsolute checks the complete path a tool is about to use.
func (p PathPolicy) ValidateAbsolute(absolute string) error {
	if !filepath.IsAbs(absolute) {
		return fmt.Errorf("%w: path is not absolute: %s", ErrInvalidName, absolute)
	}
	length := len([]byte(absolute))
	limit := p.MaxPathBytes
	if p.SupportsLongPrefix {
		limit = extendedPathLimit()
	}
	if length > limit {
		return fmt.Errorf("%w: %s is %d bytes, limit %d", ErrPathTooLong, absolute, length, limit)
	}
	return nil
}

// Budget verifies that appending generated suffixes to a directory still fits
// the host limit. Call it before CreateTemp so a long source name cannot produce
// an unusable staging path.
func (p PathPolicy) Budget(directory string, suffixes ...string) error {
	candidate := directory
	for _, suffix := range suffixes {
		candidate = filepath.Join(candidate, suffix)
	}
	return p.ValidateAbsolute(candidate)
}

// EnsureWritable proves the process can create a file in directory. It is used
// before any operation that would otherwise fail halfway through a transaction.
func (p PathPolicy) EnsureWritable(directory string) error {
	if err := p.ValidateAbsolute(directory); err != nil {
		return err
	}
	probe, err := os.CreateTemp(directory, ".writeprobe-*")
	if err != nil {
		return fmt.Errorf("%w: %s: %v%s", ErrNotWritable, directory, err, LockHint(err))
	}
	name := probe.Name()
	closeErr := probe.Close()
	removeErr := os.Remove(name)
	if closeErr != nil {
		return closeErr
	}
	return removeErr
}

// EnsureReadable proves a file can be opened without claiming an exclusive lock.
func EnsureReadable(path string) error {
	file, err := os.Open(path)
	if err != nil {
		return err
	}
	return file.Close()
}
