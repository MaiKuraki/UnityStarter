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
// the error identity used by callers. The list must name the holders that most
// often keep a Unity cache tree open: an IDE language server or file watcher
// holds directory and assembly handles for the whole session, not just while
// the editor is running.
func LockHint(err error) string {
	if errors.Is(err, ErrLocked) || IsSharingViolation(err) {
		return ". Another process still holds a handle inside this tree: close the Unity Editor and Hub helper processes, your IDE and its language server or file watcher (for example VS Code with C# Dev Kit / Roslyn), File Explorer, antivirus, or a media player, then retry"
	}
	return ""
}

func isRetryableLock(err error) bool {
	return errors.Is(err, ErrLocked) || IsSharingViolation(err)
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
// still held. It stops immediately on any other error or on cancellation. The
// returned error deliberately carries no hint: callers append LockHint once, so
// wrapping a lock error cannot produce a duplicated explanation.
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
		if !isRetryableLock(last) {
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
	return fmt.Errorf("operation still blocked after %d attempt(s): %w", attempts, last)
}

// RetryOnLockFor retries a lock conflict until a wall-clock budget elapses.
// A fixed attempt count is a poor fit for a multi-gigabyte cache: an antivirus
// scan, a closing editor subprocess or an IDE language server may need seconds
// rather than a few hundred milliseconds to release its handle. Non-lock errors
// and cancellation still stop immediately, and the caller supplies the budget.
func RetryOnLockFor(ctx context.Context, timeout, delay time.Duration, action func() error) error {
	if timeout <= 0 {
		return RetryOnLock(ctx, 1, delay, action)
	}
	if delay <= 0 {
		delay = time.Millisecond
	}
	deadline := time.Now().Add(timeout)
	var last error
	for {
		if err := ctx.Err(); err != nil {
			return err
		}
		last = action()
		if last == nil {
			return nil
		}
		if !isRetryableLock(last) {
			return last
		}
		remaining := time.Until(deadline)
		if remaining <= 0 {
			break
		}
		wait := delay
		if wait > remaining {
			wait = remaining
		}
		timer := time.NewTimer(wait)
		select {
		case <-ctx.Done():
			timer.Stop()
			return ctx.Err()
		case <-timer.C:
		}
	}
	return fmt.Errorf("operation still blocked after %s: %w", timeout, last)
}
