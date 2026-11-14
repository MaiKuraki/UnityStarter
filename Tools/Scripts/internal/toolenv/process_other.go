//go:build !unix && !windows

package toolenv

import "os/exec"

func configureProcessGroup(_ *exec.Cmd) {}
