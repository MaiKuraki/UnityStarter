//go:build !unix && !windows

package safefs

import (
	"errors"
	"os"
)

func IsSharingViolation(_ error) bool { return false }

func tryExclusiveOpen(path string) error {
	file, err := os.OpenFile(path, os.O_RDWR, 0)
	if err != nil {
		if errors.Is(err, os.ErrNotExist) {
			return nil
		}
		return err
	}
	return file.Close()
}

func replaceExisting(stage, target string) error { return os.Rename(stage, target) }
