// Package logging provides the shared leveled console logger for the repository tools.
// Diagnostics go to stderr as structured slog lines; user-facing results keep using stdout.
package logging

import (
	"fmt"
	"io"
	"log/slog"
	"os"
	"sync"
)

// Command scopes subsequent log lines with the active subcommand without
// accumulating attributes across repeated in-process runs.
var (
	mu         sync.RWMutex
	baseLogger = slog.New(slog.NewTextHandler(os.Stderr, &slog.HandlerOptions{Level: slog.LevelInfo}))
	logger     = baseLogger
)

func active() *slog.Logger {
	mu.RLock()
	defer mu.RUnlock()
	return logger
}

// Command scopes subsequent log lines with the active subcommand.
func Command(name string) {
	mu.Lock()
	defer mu.Unlock()
	logger = baseLogger.With("cmd", name)
}

// SetOutput redirects log output to w and clears any command scope. It lets
// tests assert on the emitted lines; production callers never need it.
func SetOutput(w io.Writer) {
	mu.Lock()
	defer mu.Unlock()
	baseLogger = slog.New(slog.NewTextHandler(w, &slog.HandlerOptions{Level: slog.LevelInfo}))
	logger = baseLogger
}

// Info logs at the info level.
func Info(msg string, args ...any) { active().Info(msg, args...) }

// Warn logs at the warning level.
func Warn(msg string, args ...any) { active().Warn(msg, args...) }

// Error logs at the error level.
func Error(msg string, args ...any) { active().Error(msg, args...) }

// Warnf logs a printf-style formatted message at the warning level.
func Warnf(format string, args ...any) { active().Warn(fmt.Sprintf(format, args...)) }

// Errorf logs a printf-style formatted message at the error level.
func Errorf(format string, args ...any) { active().Error(fmt.Sprintf(format, args...)) }
