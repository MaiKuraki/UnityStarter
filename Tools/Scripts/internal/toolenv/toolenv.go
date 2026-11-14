// Package toolenv resolves and runs external executables provided by the host.
// Binaries are never bundled and no default installation path is embedded: a
// tool is discovered through its override environment variable or the process
// PATH, so the same binary works on any machine whose environment is configured.
package toolenv

import (
	"bytes"
	"context"
	"errors"
	"fmt"
	"io"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"sync"
	"time"
	"unicode"
)

const (
	// WaitDelay bounds how long Cmd.Wait may block on inherited pipe writers
	// after the direct child has been killed. Without it a wrapper script or a
	// grandchild holding the pipe write end can wedge a worker forever.
	WaitDelay = 5 * time.Second
	// DefaultStderrLimit caps diagnostic capture for one invocation.
	DefaultStderrLimit int64 = 256 * 1024
)

var (
	ErrTimeout   = errors.New("external command timed out")
	ErrCancelled = errors.New("external command cancelled")
)

// ExitError reports a non-zero child exit together with its bounded stderr.
type ExitError struct {
	Code   int
	Stderr string
}

func (e *ExitError) Error() string {
	return fmt.Sprintf("external command exited with code %d", e.Code)
}

// StartError reports that the executable could not be started at all, which is
// distinct from a child that ran and failed.
type StartError struct{ Err error }

func (e *StartError) Error() string { return fmt.Sprintf("cannot start external command: %v", e.Err) }
func (e *StartError) Unwrap() error { return e.Err }

func IsStartFailure(err error) bool {
	var target *StartError
	return errors.As(err, &target)
}

// EnvironmentKey derives the override variable for a tool name. The mapping is
// stable and contains no host-specific values: "ffmpeg" -> "FFMPEG".
func EnvironmentKey(name string) string {
	var b strings.Builder
	for _, r := range name {
		switch {
		case unicode.IsLetter(r) || unicode.IsDigit(r):
			b.WriteRune(unicode.ToUpper(r))
		default:
			b.WriteByte('_')
		}
	}
	return b.String()
}

// Resolution records the resolved executable and where it came from.
type Resolution struct {
	Name   string
	Path   string
	Source string
}

const (
	SourceEnvironment = "environment"
	SourcePath        = "PATH"
)

// Resolver finds executables. The zero value uses the process environment; the
// hooks exist so callers can drive resolution without touching global state.
type Resolver struct {
	LookupPath func(name string) (string, error)
	Environ    func() []string
	Stat       func(path string) (os.FileInfo, error)
}

func (r Resolver) lookupPath(name string) (string, error) {
	if r.LookupPath != nil {
		return r.LookupPath(name)
	}
	return exec.LookPath(name)
}

func (r Resolver) environ() []string {
	if r.Environ != nil {
		return r.Environ()
	}
	return os.Environ()
}

func (r Resolver) stat(path string) (os.FileInfo, error) {
	if r.Stat != nil {
		return r.Stat(path)
	}
	return os.Stat(path)
}

// Resolve applies, in order: the tool's override environment variable, then
// PATH. An empty variable is ignored. A value without a path separator is
// resolved through PATH, so both "ffmpeg" and an absolute path are accepted.
func (r Resolver) Resolve(name string) (Resolution, error) {
	name = strings.TrimSpace(name)
	if name == "" {
		return Resolution{}, errors.New("external tool name is empty")
	}
	key := EnvironmentKey(name)
	for _, entry := range r.environ() {
		parts := strings.SplitN(entry, "=", 2)
		if len(parts) != 2 || parts[0] != key {
			continue
		}
		value := strings.Trim(strings.TrimSpace(parts[1]), `"`)
		if value == "" {
			break
		}
		path := value
		if !strings.ContainsAny(value, `/\`) {
			resolved, err := r.lookupPath(value)
			if err != nil {
				return Resolution{}, fmt.Errorf("%s is set to %q but it cannot be found on PATH: %w", key, value, err)
			}
			path = resolved
		}
		if _, err := r.stat(path); err != nil {
			return Resolution{}, fmt.Errorf("%s points to %q which is not usable: %w", key, path, err)
		}
		return Resolution{Name: name, Path: path, Source: SourceEnvironment}, nil
	}
	path, err := r.lookupPath(name)
	if err != nil {
		return Resolution{}, fmt.Errorf(
			"%s is not available: add it to PATH or set %s to the executable name or path: %w",
			name, key, err)
	}
	return Resolution{Name: name, Path: path, Source: SourcePath}, nil
}

// Resolve resolves through the process environment.
func Resolve(name string) (Resolution, error) { return Resolver{}.Resolve(name) }

// MustResolve resolves a tool or returns a ready-to-print error.
func MustResolve(name string) (string, error) {
	res, err := Resolve(name)
	if err != nil {
		return "", err
	}
	return res.Path, nil
}

// BoundedWriter keeps the beginning and the end of a stream under a fixed byte
// budget. Long-running encoders emit progress lines continuously; keeping only
// the head and tail preserves both the command context and the final error.
type BoundedWriter struct {
	mu        sync.Mutex
	headLimit int64
	tailLimit int64
	head      []byte
	tail      []byte
	total     int64
	truncated bool
}

func NewBoundedWriter(limit int64) *BoundedWriter {
	if limit < 1024 {
		limit = 1024
	}
	return &BoundedWriter{headLimit: limit / 2, tailLimit: limit - limit/2}
}

func (w *BoundedWriter) Write(p []byte) (int, error) {
	w.mu.Lock()
	defer w.mu.Unlock()
	n := len(p)
	w.total += int64(n)
	if int64(len(w.head)) < w.headLimit {
		take := w.headLimit - int64(len(w.head))
		if take > int64(len(p)) {
			take = int64(len(p))
		}
		w.head = append(w.head, p[:take]...)
		p = p[take:]
	}
	if len(p) == 0 {
		return n, nil
	}
	w.truncated = true
	if int64(len(p)) >= w.tailLimit {
		w.tail = append(w.tail[:0], p[int64(len(p))-w.tailLimit:]...)
		return n, nil
	}
	if overflow := int64(len(w.tail)) + int64(len(p)) - w.tailLimit; overflow > 0 {
		copy(w.tail, w.tail[overflow:])
		w.tail = w.tail[:int64(len(w.tail))-overflow]
	}
	w.tail = append(w.tail, p...)
	return n, nil
}

func (w *BoundedWriter) Bytes() []byte {
	w.mu.Lock()
	defer w.mu.Unlock()
	if !w.truncated {
		return append(append([]byte(nil), w.head...), w.tail...)
	}
	out := make([]byte, 0, len(w.head)+len(w.tail)+32)
	out = append(out, w.head...)
	out = append(out, []byte("\n... [truncated] ...\n")...)
	out = append(out, w.tail...)
	return out
}

func (w *BoundedWriter) Truncated() bool {
	w.mu.Lock()
	defer w.mu.Unlock()
	return w.truncated
}

// Invocation describes one external command. Binary is always a resolved path;
// callers obtain it from Resolve so the environment remains the single source
// of truth.
type Invocation struct {
	Binary      string
	Args        []string
	Timeout     time.Duration
	StderrLimit int64
	Stdout      io.Writer
	Dir         string
	Env         []string
}

// Outcome carries the bounded diagnostic stream and the child exit code.
type Outcome struct {
	Stderr          string
	StderrTruncated bool
	ExitCode        int
}

// Run executes one command with a bounded lifetime, bounded diagnostics and a
// deterministic error classification. It never returns a nil error for a child
// that failed to start or exited non-zero.
func Run(ctx context.Context, inv Invocation) (Outcome, error) {
	if strings.TrimSpace(inv.Binary) == "" {
		return Outcome{}, &StartError{Err: errors.New("no executable was resolved")}
	}
	if inv.Timeout > 0 {
		var cancel context.CancelFunc
		ctx, cancel = context.WithTimeout(ctx, inv.Timeout)
		defer cancel()
	}
	limit := inv.StderrLimit
	if limit <= 0 {
		limit = DefaultStderrLimit
	}
	diagnostics := NewBoundedWriter(limit)

	cmd := exec.CommandContext(ctx, inv.Binary, inv.Args...)
	cmd.Stderr = diagnostics
	if inv.Stdout != nil {
		cmd.Stdout = inv.Stdout
	}
	if inv.Dir != "" {
		cmd.Dir = inv.Dir
	}
	if inv.Env != nil {
		cmd.Env = inv.Env
	}
	cmd.WaitDelay = WaitDelay
	configureProcessGroup(cmd)

	runErr := cmd.Run()
	outcome := Outcome{
		Stderr:          string(bytes.TrimSpace(diagnostics.Bytes())),
		StderrTruncated: diagnostics.Truncated(),
	}
	if runErr == nil {
		return outcome, nil
	}
	var exitErr *exec.ExitError
	if errors.As(runErr, &exitErr) {
		outcome.ExitCode = exitErr.ExitCode()
	}
	if errors.Is(ctx.Err(), context.DeadlineExceeded) {
		return outcome, fmt.Errorf("%w after %s: %s", ErrTimeout, inv.Timeout, describe(inv))
	}
	if errors.Is(ctx.Err(), context.Canceled) {
		return outcome, fmt.Errorf("%w: %s", ErrCancelled, describe(inv))
	}
	if exitErr != nil {
		return outcome, &ExitError{Code: outcome.ExitCode, Stderr: outcome.Stderr}
	}
	return outcome, &StartError{Err: runErr}
}

// FormatFailure renders a child failure without discarding the original cause,
// so a missing binary never masquerades as a media-analysis result.
func FormatFailure(err error, outcome Outcome) string {
	var start *StartError
	if errors.As(err, &start) {
		return fmt.Sprintf("%v", err)
	}
	var exit *ExitError
	if errors.As(err, &exit) {
		if strings.TrimSpace(outcome.Stderr) == "" {
			return fmt.Sprintf("%v (no diagnostics emitted)", err)
		}
		return fmt.Sprintf("%v\n%s", err, outcome.Stderr)
	}
	if strings.TrimSpace(outcome.Stderr) != "" {
		return fmt.Sprintf("%v\n%s", err, outcome.Stderr)
	}
	return err.Error()
}

func describe(inv Invocation) string {
	return filepath.Base(inv.Binary)
}
