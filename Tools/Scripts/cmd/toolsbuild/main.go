// toolsbuild cross-compiles the tool binaries into a platform bundle. The Go
// toolchain is resolved through the host environment; the module root can be
// supplied explicitly so a distributed binary works from any directory.
//
// Usage:
//
//	go run ./cmd/toolsbuild
//	go run ./cmd/toolsbuild --targets windows/amd64,linux/arm64 --output ./bundle
//	go run ./cmd/toolsbuild --list
//	toolsbuild --module-root /path/to/Scripts --verify
package main

import (
	"context"
	"errors"
	"flag"
	"fmt"
	"os"
	"os/signal"
	"path/filepath"
	"runtime"
	"sort"
	"strings"
	"syscall"
	"time"

	"cyclonegames.tools/scripts/internal/logging"
	"cyclonegames.tools/scripts/internal/safefs"
	"cyclonegames.tools/scripts/internal/toolenv"
	"cyclonegames.tools/scripts/internal/toolkit"
)

const (
	defaultTargets   = "windows/amd64,darwin/arm64,darwin/amd64,linux/amd64,linux/arm64"
	moduleRootEnvVar = "TOOLS_MODULE_ROOT"
	defaultTimeout   = 10 * time.Minute
)

type target struct {
	goos   string
	goarch string
}

func (t target) String() string { return t.goos + "/" + t.goarch }

func main() {
	args, noPause := toolkit.ExtractNoPauseFlag(os.Args[1:])
	code := run(args)
	toolkit.PauseAfterRun(noPause)
	os.Exit(code)
}

func run(args []string) int {
	flags := flag.NewFlagSet("toolsbuild", flag.ContinueOnError)
	flags.SetOutput(os.Stderr)
	var targetsCSV, outputDir, moduleRootFlag string
	var verify, listTargets bool
	var timeout time.Duration
	flags.StringVar(&targetsCSV, "targets", defaultTargets, "Comma-separated GOOS/GOARCH targets")
	flags.StringVar(&outputDir, "output", "", "Output root (default: Executable beside the module root)")
	flags.StringVar(&moduleRootFlag, "module-root", "", "Go module root (overrides discovery and "+moduleRootEnvVar+")")
	flags.BoolVar(&verify, "verify", false, "Run each current-platform binary after building")
	flags.BoolVar(&listTargets, "list", false, "Print the supported GOOS/GOARCH targets and exit")
	flags.DurationVar(&timeout, "timeout", defaultTimeout, "Timeout for each go build")
	if err := flags.Parse(args); err != nil {
		if errors.Is(err, flag.ErrHelp) {
			return 0
		}
		return toolkit.ExitUsage
	}
	if flags.NArg() != 0 {
		logging.Error("unexpected positional arguments", "args", strings.Join(flags.Args(), " "))
		return toolkit.ExitUsage
	}

	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM, syscall.SIGHUP)
	defer stop()

	goBinary, err := toolenv.MustResolve("go")
	if err != nil {
		logging.Error("go toolchain is unavailable", "error", err)
		return toolkit.ExitFailure
	}
	supported, err := supportedTargets(ctx, goBinary)
	if err != nil {
		logging.Error("cannot query the Go toolchain", "error", err)
		return toolkit.ExitFailure
	}
	if listTargets {
		for _, item := range supported {
			fmt.Println(item)
		}
		return toolkit.ExitSuccess
	}

	targets, err := parseTargets(targetsCSV, supported)
	if err != nil {
		logging.Error("invalid targets", "error", err)
		return toolkit.ExitUsage
	}
	moduleRoot, err := findModuleRoot(moduleRootFlag)
	if err != nil {
		logging.Error("cannot locate module root", "error", err)
		return toolkit.ExitFailure
	}
	if outputDir == "" {
		outputDir = filepath.Join(filepath.Dir(moduleRoot), "Executable")
	}
	if err := os.MkdirAll(outputDir, 0o755); err != nil {
		logging.Error("cannot create output root", "path", outputDir, "error", err)
		return toolkit.ExitFailure
	}
	policy := safefs.HostPathPolicy()
	if err := policy.EnsureWritable(outputDir); err != nil {
		logging.Error("output root is not usable", "path", outputDir, "error", err)
		return toolkit.ExitFailure
	}

	packages := []string{
		"cmd/unity-project-tools",
		"cmd/dev-tools",
		"cmd/toolsbuild",
	}
	names := []string{"unity-project-tools", "dev-tools", "toolsbuild"}
	var currentBinaries []string

	for _, item := range targets {
		platformDir := filepath.Join(outputDir, item.goos, item.goarch)
		if err := os.MkdirAll(platformDir, 0o755); err != nil {
			logging.Error("cannot create platform directory", "path", platformDir, "error", err)
			return toolkit.ExitFailure
		}
		if err := policy.Budget(platformDir, "unity-project-tools.exe", ".build-stage-temp"); err != nil {
			logging.Error("platform path exceeds host limits", "path", platformDir, "error", err)
			return toolkit.ExitFailure
		}
		for index, pkg := range packages {
			outputPath := filepath.Join(platformDir, names[index]+suffix(item.goos))
			if err := buildPackage(ctx, goBinary, moduleRoot, "./"+pkg, outputPath, item, timeout); err != nil {
				logging.Error("build failed", "target", item.String(), "error", err)
				return toolkit.ExitFailure
			}
			logging.Info("built", "target", item.String(), "output", outputPath)
			if item.goos == runtime.GOOS && item.goarch == runtime.GOARCH {
				currentBinaries = append(currentBinaries, outputPath)
			}
		}
	}

	if verify {
		if len(currentBinaries) == 0 {
			logging.Error("--verify requires the current platform to be part of --targets")
			return toolkit.ExitUsage
		}
		for _, binary := range currentBinaries {
			outcome, runErr := toolenv.Run(ctx, toolenv.Invocation{
				Binary: binary, Args: []string{verifyArgument(binary)},
				Timeout: 30 * time.Second, Stdout: os.Stdout,
			})
			if runErr != nil {
				logging.Error("verification failed", "binary", binary,
					"error", toolenv.FormatFailure(runErr, outcome))
				return toolkit.ExitFailure
			}
		}
	}

	fmt.Printf("\nBuilt %d target(s) under %s\n", len(targets), outputDir)
	return toolkit.ExitSuccess
}

func verifyArgument(binary string) string {
	base := filepath.Base(binary)
	if base == "toolsbuild" || base == "toolsbuild.exe" {
		return "--help"
	}
	return "--list"
}

func buildPackage(ctx context.Context, goBinary, moduleRoot, packagePath, outputPath string,
	item target, timeout time.Duration) error {
	env := append(os.Environ(),
		"GOOS="+item.goos,
		"GOARCH="+item.goarch,
		"CGO_ENABLED=0",
	)
	outcome, err := toolenv.Run(ctx, toolenv.Invocation{
		Binary: goBinary,
		Args: []string{"build", "-mod=readonly", "-trimpath", "-buildvcs=false",
			"-ldflags", "-s -w", "-o", outputPath, packagePath},
		Timeout: timeout,
		Dir:     moduleRoot,
		Env:     env,
	})
	if err != nil {
		return fmt.Errorf("build %s for %s: %s", packagePath, item.String(),
			toolenv.FormatFailure(err, outcome))
	}
	return nil
}

// supportedTargets asks the installed toolchain rather than embedding a list,
// so a newer Go release automatically exposes its platforms.
func supportedTargets(ctx context.Context, goBinary string) ([]string, error) {
	var buffer strings.Builder
	outcome, err := toolenv.Run(ctx, toolenv.Invocation{
		Binary: goBinary, Args: []string{"tool", "dist", "list"},
		Timeout: 30 * time.Second, Stdout: &buffer,
	})
	if err != nil {
		return nil, errors.New(toolenv.FormatFailure(err, outcome))
	}
	var result []string
	for _, line := range strings.Split(buffer.String(), "\n") {
		line = strings.TrimSpace(line)
		if line != "" && strings.Contains(line, "/") {
			result = append(result, line)
		}
	}
	if len(result) == 0 {
		return nil, errors.New("the Go toolchain returned an empty target list")
	}
	sort.Strings(result)
	return result, nil
}

func parseTargets(csv string, supported []string) ([]target, error) {
	valid := make(map[string]bool, len(supported))
	for _, item := range supported {
		valid[item] = true
	}
	seen := make(map[string]bool)
	var result []target
	for _, part := range strings.Split(csv, ",") {
		value := strings.TrimSpace(part)
		pair := strings.Split(value, "/")
		if len(pair) != 2 || pair[0] == "" || pair[1] == "" {
			return nil, fmt.Errorf("invalid target %q: expected GOOS/GOARCH", value)
		}
		if !valid[value] {
			return nil, fmt.Errorf("target %q is not supported by the installed Go toolchain (use --list)", value)
		}
		if seen[value] {
			continue
		}
		seen[value] = true
		result = append(result, target{goos: pair[0], goarch: pair[1]})
	}
	if len(result) == 0 {
		return nil, errors.New("no targets were selected")
	}
	return result, nil
}

func findModuleRoot(explicit string) (string, error) {
	candidates := make([]string, 0, 4)
	if strings.TrimSpace(explicit) != "" {
		candidates = append(candidates, strings.TrimSpace(explicit))
	}
	if value := strings.TrimSpace(os.Getenv(moduleRootEnvVar)); value != "" {
		candidates = append(candidates, value)
	}
	if _, sourceFile, _, ok := runtime.Caller(0); ok && filepath.IsAbs(sourceFile) {
		candidates = append(candidates, filepath.Dir(filepath.Dir(filepath.Dir(sourceFile))))
	}
	if exePath, err := os.Executable(); err == nil {
		for dir := filepath.Dir(exePath); ; dir = filepath.Dir(dir) {
			candidates = append(candidates, dir, filepath.Join(dir, "Tools", "Scripts"))
			parent := filepath.Dir(dir)
			if parent == dir {
				break
			}
		}
	}
	for _, candidate := range candidates {
		if candidate == "" {
			continue
		}
		absolute, err := filepath.Abs(candidate)
		if err != nil {
			continue
		}
		if info, err := os.Stat(filepath.Join(absolute, "go.mod")); err == nil && info.Mode().IsRegular() {
			return absolute, nil
		}
	}
	return "", fmt.Errorf("no go.mod found; pass --module-root or set %s", moduleRootEnvVar)
}

func suffix(goos string) string {
	if goos == "windows" {
		return ".exe"
	}
	return ""
}
