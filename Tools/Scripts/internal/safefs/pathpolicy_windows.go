//go:build windows

package safefs

import (
	"fmt"
	"path/filepath"
	"strings"
)

const windowsExtendedPathLimit = 32767

func extendedPathLimit() int { return windowsExtendedPathLimit }

func hostPathPolicy() PathPolicy {
	return PathPolicy{
		MaxPathBytes:       260,
		MaxComponentBytes:  255,
		SupportsLongPrefix: true,
		Reserved:           isReservedDeviceName,
		Forbidden:          hasForbiddenWindowsCharacters,
	}
}

func isReservedDeviceName(name string) bool {
	upper := strings.ToUpper(name)
	base := upper
	if dot := strings.IndexByte(upper, '.'); dot >= 0 {
		base = upper[:dot]
	}
	switch base {
	case "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$":
		return true
	}
	if len(base) == 4 && (strings.HasPrefix(base, "COM") || strings.HasPrefix(base, "LPT")) &&
		base[3] >= '1' && base[3] <= '9' {
		return true
	}
	return false
}

func hasForbiddenWindowsCharacters(name string) bool {
	if strings.ContainsAny(name, `<>:"/\|?*`) {
		return true
	}
	// Trailing spaces and dots are silently trimmed by the Win32 file APIs and
	// would make a generated path differ from the one the caller validated.
	return strings.HasSuffix(name, " ") || strings.HasSuffix(name, ".")
}

// prepareWindowsPath converts an absolute path into the extended-length form
// when it exceeds MAX_PATH. The \\?\ form bypasses Win32 path normalization and
// works without enabling the LongPathsEnabled policy. UNC paths keep their
// server and share components through the UNC extended prefix.
func prepareWindowsPath(path string) (string, error) {
	if len(path) < 260 {
		return path, nil
	}
	if !filepath.IsAbs(path) {
		return "", fmt.Errorf("%w: extended paths require an absolute path", ErrPathTooLong)
	}
	cleaned := filepath.Clean(path)
	if strings.HasPrefix(cleaned, `\\?\`) {
		return cleaned, nil
	}
	if strings.HasPrefix(cleaned, `\\`) {
		return `\\?\UNC\` + strings.TrimPrefix(cleaned, `\\`), nil
	}
	return `\\?\` + cleaned, nil
}
