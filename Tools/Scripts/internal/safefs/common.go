package safefs

import (
	"fmt"
	"os"
	"path/filepath"
)

// MoveNoReplace atomically moves source to an absent target on the same
// filesystem. Platform implementations must never replace an existing target.
func MoveNoReplace(source, target string) error {
	if err := moveNoReplace(source, target); err != nil {
		return err
	}
	if err := SyncParent(target); err != nil {
		return fmt.Errorf("move completed but target parent sync failed: %w", err)
	}
	if !sameDirectory(source, target) {
		if err := SyncParent(source); err != nil {
			return fmt.Errorf("move completed but source parent sync failed: %w", err)
		}
	}
	return nil
}

// PublishFileNoReplace publishes a fully-written same-filesystem stage through
// the platform's atomic no-replace move. The canonical target becomes visible
// atomically and an existing target is never replaced.
func PublishFileNoReplace(stage, target string) error {
	return MoveNoReplace(stage, target)
}

func CreateExclusiveDirectory(path string, permission os.FileMode) error {
	if err := os.Mkdir(path, permission); err != nil {
		return err
	}
	if err := SyncParent(path); err != nil {
		return fmt.Errorf("directory creation completed but parent sync failed: %w", err)
	}
	return nil
}

func RemoveDurably(path string) error {
	if err := os.Remove(path); err != nil {
		return err
	}
	if err := SyncParent(path); err != nil {
		return fmt.Errorf("remove completed but parent sync failed: %w", err)
	}
	return nil
}

func sameDirectory(left, right string) bool {
	left = filepath.Clean(filepath.Dir(left))
	right = filepath.Clean(filepath.Dir(right))
	if os.PathSeparator == '\\' {
		return filepath.VolumeName(left) == filepath.VolumeName(right) && equalFoldPath(left, right)
	}
	return left == right
}

func equalFoldPath(left, right string) bool {
	if len(left) != len(right) {
		return false
	}
	for index := 0; index < len(left); index++ {
		character := left[index]
		other := right[index]
		if character >= 'A' && character <= 'Z' {
			character += 'a' - 'A'
		}
		if other >= 'A' && other <= 'Z' {
			other += 'a' - 'A'
		}
		if character != other {
			return false
		}
	}
	return true
}
