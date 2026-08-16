// Package logging provides the shared leveled console logger for the repository tools.
// Diagnostics go to stderr as structured slog lines; user-facing results keep using stdout.
package logging

import (
	"log/slog"
	"os"
)

var logger = slog.New(slog.NewTextHandler(os.Stderr, &slog.HandlerOptions{Level: slog.LevelInfo}))

// Command scopes subsequent log lines with the active subcommand.
func Command(name string) {
	logger = logger.With("cmd", name)
}

// Info logs at the info level.
func Info(msg string, args ...any) { logger.Info(msg, args...) }

// Warn logs at the warning level.
func Warn(msg string, args ...any) { logger.Warn(msg, args...) }

// Error logs at the error level.
func Error(msg string, args ...any) { logger.Error(msg, args...) }
