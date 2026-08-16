//go:build linux

package term

import "golang.org/x/sys/unix"

const ioctlReadTermios = unix.TCGETS
