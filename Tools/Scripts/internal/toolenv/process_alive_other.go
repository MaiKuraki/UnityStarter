//go:build !unix && !windows

package toolenv

import "fmt"

// ProcessAlive fails closed on hosts without a portable process probe.
func ProcessAlive(_ int) (bool, error) {
	return false, fmt.Errorf("process liveness cannot be proven on this host")
}
