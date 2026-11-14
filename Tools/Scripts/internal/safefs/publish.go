package safefs

import (
	"context"
	"errors"
	"fmt"
	"os"
	"time"
)

// ErrLocked reports that another process holds the target. Windows raises this
// for any open handle; Unix detects it for cooperating flock holders.
var ErrLocked = errors.New("file is held by another process")

// LockHint turns an OS error into an actionable explanation without changing
// the error identity used by callers.
func LockHint(err error) string {
	if errors.Is(err, ErrLocked) || IsSharingViolation(err) {
		return ". Another process is holding this file: close the editor, file explorer, antivirus or media player, then retry"
	}
	return ""
}

// PublishNoReplace moves a fully written stage to an absent target, then syncs
// the parent directory. It never replaces existing content.
func PublishNoReplace(stage, target string) error {
	return MoveNoReplace(stage, target)
}

// PublishReplace atomically replaces target with a fully written stage. Before
// replacing it proves the target is not held by another process, and on Windows
// it clears a read-only attribute so a generated artifact can be refreshed.
func PublishReplace(stage, target string) error {
	if err := validatePublishPaths(stage, target); err != nil {
		return err
	}
	if err := checkOccupancy(target); err != nil {
		return err
	}
	if err := replaceExisting(stage, target); err != nil {
		return fmt.Errorf("atomic replacement failed: %w%s", err, LockHint(err))
	}
	if err := SyncParent(target); err != nil {
		return fmt.Errorf("replacement completed but parent directory sync failed: %w", err)
	}
	return nil
}

func validatePublishPaths(paths ...string) error {
	policy := HostPathPolicy()
	for _, path := range paths {
		if err := policy.ValidateAbsolute(path); err != nil {
			return err
		}
	}
	return nil
}

func checkOccupancy(target string) error {
	if _, err := os.Lstat(target); errors.Is(err, os.ErrNotExist) {
		return nil
	} else if err != nil {
		return err
	}
	return tryExclusiveOpen(target)
}

// RetryOnLock retries an operation that may transiently fail because a file is
// still held. It stops immediately on any other error or on cancellation.
func RetryOnLock(ctx context.Context, attempts int, delay time.Duration, action func() error) error {
	if attempts < 1 {
		attempts = 1
	}
	var last error
	for attempt := 0; attempt < attempts; attempt++ {
		if err := ctx.Err(); err != nil {
			return err
		}
		last = action()
		if last == nil {
			return nil
		}
		if !errors.Is(last, ErrLocked) && !IsSharingViolation(last) {
			return last
		}
		if attempt+1 < attempts {
			timer := time.NewTimer(delay)
			select {
			case <-ctx.Done():
				timer.Stop()
				return ctx.Err()
			case <-timer.C:
			}
		}
	}
	return fmt.Errorf("operation still blocked after %d attempt(s): %w%s", attempts, last, LockHint(last))
}
