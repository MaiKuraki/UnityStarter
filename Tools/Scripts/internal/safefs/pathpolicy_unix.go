//go:build unix

package safefs

import "strings"

const unixPathLimit = 4096

func extendedPathLimit() int { return unixPathLimit }

func hostPathPolicy() PathPolicy {
	return PathPolicy{
		MaxPathBytes:       unixPathLimit,
		MaxComponentBytes:  255,
		SupportsLongPrefix: false,
		Forbidden: func(name string) bool {
			return strings.ContainsAny(name, "/\x00")
		},
	}
}

func preparePlatformPath(path string) (string, error) { return path, nil }
