//go:build !unix && !windows

package recovery

import "testing"

func holdLock(t *testing.T, _ string) func() {
	t.Helper()
	t.Skip("byte-range locks are not available on this platform")
	return func() {}
}
