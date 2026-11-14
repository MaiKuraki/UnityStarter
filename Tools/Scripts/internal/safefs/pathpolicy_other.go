//go:build !unix && !windows

package safefs

func extendedPathLimit() int { return 4096 }

func hostPathPolicy() PathPolicy {
	return PathPolicy{MaxPathBytes: 4096, MaxComponentBytes: 255, SupportsLongPrefix: false}
}

func preparePlatformPath(path string) (string, error) { return path, nil }
