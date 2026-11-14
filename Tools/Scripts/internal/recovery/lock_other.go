//go:build !unix && !windows

package recovery

import "os"

func HeldLock(_ string) (bool, error) { return false, nil }

func isRedirected(path string) (bool, error) {
	info, err := os.Lstat(path)
	if err != nil {
		return false, err
	}
	return info.Mode()&os.ModeSymlink != 0, nil
}
