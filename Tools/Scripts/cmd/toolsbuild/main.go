// toolsbuild cross-compiles both tool binaries (unity-project-tools and dev-tools) plus
// itself for every supported target into Tools/Executable/<OS>/<GOARCH>/. It is the only
// supported way to produce release binaries and works identically in local shells and CI
// runners - no PowerShell, no Makefiles.
//
// Usage:
//
//	go run ./cmd/toolsbuild                       # all default targets
//	go run ./cmd/toolsbuild --targets windows/amd64,darwin/arm64
//	go run ./cmd/toolsbuild --verify              # also smoke-test the current platform binary
package main

import (
	"errors"
	"flag"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"strings"

	"cyclonegames.tools/scripts/internal/logging"
)

type target struct {
	goos   string
	goarch string
}

func main() {
	os.Exit(run(os.Args[1:]))
}

func run(args []string) int {
	flags := flag.NewFlagSet("toolsbuild", flag.ContinueOnError)
	flags.SetOutput(os.Stderr)
	var targetsCSV string
	var outputDir string
	var verify bool
	flags.StringVar(&targetsCSV, "targets", "windows/amd64,darwin/arm64,darwin/amd64,linux/amd64,linux/arm64", "Comma-separated GOOS/GOARCH targets")
	flags.StringVar(&outputDir, "output", "", "Output root (default: Tools/Executable beside the module root)")
	flags.BoolVar(&verify, "verify", false, "Run the current-platform binary with --list after building")
	if err := flags.Parse(args); err != nil {
		if errors.Is(err, flag.ErrHelp) {
			return 0
		}
		return 2
	}

	moduleRoot, err := findModuleRoot()
	if err != nil {
		logging.Error("cannot locate module root", "error", err)
		return 1
	}
	if outputDir == "" {
		outputDir = filepath.Join(filepath.Dir(moduleRoot), "Executable")
	}

	targets, err := parseTargets(targetsCSV)
	if err != nil {
		logging.Error("invalid targets", "error", err)
		return 2
	}

	var currentPlatformBinary string
	var currentPlatformUtilsBinary string
	for _, t := range targets {
		platformDir := filepath.Join(outputDir, t.goos, t.goarch)
		if err := os.MkdirAll(platformDir, 0o755); err != nil {
			logging.Error("cannot create platform directory", "path", platformDir, "error", err)
			return 1
		}
		toolsBinary := filepath.Join(platformDir, "unity-project-tools"+suffix(t.goos))
		utilsBinary := filepath.Join(platformDir, "dev-tools"+suffix(t.goos))
		builderBinary := filepath.Join(platformDir, "toolsbuild"+suffix(t.goos))
		if err := buildPackage(moduleRoot, "cyclonegames.tools/scripts/cmd/unity-project-tools", toolsBinary, t); err != nil {
			logging.Error("build failed", "target", t.goos+"/"+t.goarch, "error", err)
			return 1
		}
		if err := buildPackage(moduleRoot, "cyclonegames.tools/scripts/cmd/dev-tools", utilsBinary, t); err != nil {
			logging.Error("build failed", "target", t.goos+"/"+t.goarch, "error", err)
			return 1
		}
		if err := buildPackage(moduleRoot, "cyclonegames.tools/scripts/cmd/toolsbuild", builderBinary, t); err != nil {
			logging.Error("build failed", "target", t.goos+"/"+t.goarch, "error", err)
			return 1
		}
		logging.Info("built", "target", t.goos+"/"+t.goarch, "output", toolsBinary)
		logging.Info("built", "target", t.goos+"/"+t.goarch, "output", utilsBinary)
		if t.goos == runtime.GOOS && t.goarch == runtime.GOARCH {
			currentPlatformBinary = toolsBinary
			currentPlatformUtilsBinary = utilsBinary
		}
	}

	if verify {
		if currentPlatformBinary == "" {
			logging.Error("--verify requires the current platform to be part of --targets")
			return 2
		}
		for _, binary := range []string{currentPlatformBinary, currentPlatformUtilsBinary} {
			command := exec.Command(binary, "--list")
			command.Stdout = os.Stdout
			command.Stderr = os.Stderr
			if err := command.Run(); err != nil {
				logging.Error("verification failed", "binary", binary, "error", err)
				return 1
			}
		}
	}

	fmt.Printf("\nBuilt %d target(s) under %s\n", len(targets), outputDir)
	return 0
}

func findModuleRoot() (string, error) {
	// When run through `go run`, the compiled source path is absolute and points into the
	// module; use it directly.
	if _, sourceFile, _, ok := runtime.Caller(0); ok && filepath.IsAbs(sourceFile) {
		root := filepath.Dir(filepath.Dir(filepath.Dir(sourceFile)))
		if _, err := os.Stat(filepath.Join(root, "go.mod")); err == nil {
			return root, nil
		}
	}

	// The distributed binary is compiled with -trimpath, so the baked source path is
	// module-relative and cannot locate the module. Fall back to the executable's own
	// location: toolsbuild ships as <repo>/Tools/Executable/<OS>/<GOARCH>/toolsbuild and
	// the module lives at <repo>/Tools/Scripts.
	exePath, err := os.Executable()
	if err != nil {
		return "", fmt.Errorf("cannot locate the toolsbuild executable: %w", err)
	}
	for dir := filepath.Dir(exePath); ; dir = filepath.Dir(dir) {
		if _, err := os.Stat(filepath.Join(dir, "go.mod")); err == nil {
			return dir, nil
		}
		if _, err := os.Stat(filepath.Join(dir, "Tools", "Scripts", "go.mod")); err == nil {
			return filepath.Join(dir, "Tools", "Scripts"), nil
		}
		parent := filepath.Dir(dir)
		if parent == dir {
			break
		}
	}
	return "", errors.New("go.mod not found beside the toolsbuild executable or any parent directory")
}

func parseTargets(csv string) ([]target, error) {
	parts := strings.Split(csv, ",")
	targets := make([]target, 0, len(parts))
	for _, part := range parts {
		pair := strings.Split(strings.TrimSpace(part), "/")
		if len(pair) != 2 || pair[0] == "" || pair[1] == "" {
			return nil, fmt.Errorf("invalid target %q: expected GOOS/GOARCH", part)
		}
		targets = append(targets, target{goos: pair[0], goarch: pair[1]})
	}
	return targets, nil
}

func buildPackage(moduleRoot, packagePath, outputPath string, t target) error {
	command := exec.Command("go", "build", "-mod=readonly", "-trimpath", "-buildvcs=false", "-ldflags", "-s -w", "-o", outputPath, packagePath)
	command.Dir = moduleRoot
	command.Env = append(os.Environ(), "GOOS="+t.goos, "GOARCH="+t.goarch, "CGO_ENABLED=0")
	if combined, err := command.CombinedOutput(); err != nil {
		return fmt.Errorf("build %s for %s/%s failed: %v\n%s", packagePath, t.goos, t.goarch, err, combined)
	}
	return nil
}

func suffix(goos string) string {
	if goos == "windows" {
		return ".exe"
	}
	return ""
}
