// Package logging provides the shared leveled console logger for the repository tools.
// Diagnostics go to stderr as structured slog lines; user-facing results keep using stdout.
package logging

import (
	"fmt"
	"io"
	"log/slog"
	"os"
)

// baseLogger is the unscoped logger. Command rebinds the active logger from
// this one so repeated in-process runs (the interactive menu runs commands in
// the same process) never accumulate "cmd" attributes across runs.
var baseLogger = slog.New(slog.NewTextHandler(os.Stderr, &slog.HandlerOptions{Level: slog.LevelInfo}))

var logger = baseLogger

// Command scopes subsequent log lines with the active subcommand.
func Command(name string) {
	logger = baseLogger.With("cmd", name)
}

// SetOutput redirects log output to w and clears any command scope. It lets
// tests assert on the emitted lines; production callers never need it.
func SetOutput(w io.Writer) {
	baseLogger = slog.New(slog.NewTextHandler(w, &slog.HandlerOptions{Level: slog.LevelInfo}))
	logger = baseLogger
}

// Info logs at the info level.
func Info(msg string, args ...any) { logger.Info(msg, args...) }

// Warn logs at the warning level.
func Warn(msg string, args ...any) { logger.Warn(msg, args...) }

// Error logs at the error level.
func Error(msg string, args ...any) { logger.Error(msg, args...) }

// Warnf logs a printf-style formatted message at the warning level.
func Warnf(format string, args ...any) { logger.Warn(fmt.Sprintf(format, args...)) }

// Errorf logs a printf-style formatted message at the error level.
func Errorf(format string, args ...any) { logger.Error(fmt.Sprintf(format, args...)) }
