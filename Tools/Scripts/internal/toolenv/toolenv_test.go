package toolenv

import (
	"context"
	"errors"
	"fmt"
	"os"
	"os/exec"
	"strings"
	"testing"
	"time"
)

func TestEnvironmentKey(t *testing.T) {
	for input, want := range map[string]string{
		"ffmpeg": "FFMPEG", "ffprobe": "FFPROBE", "my-tool": "MY_TOOL", "go": "GO",
	} {
		if got := EnvironmentKey(input); got != want {
			t.Fatalf("EnvironmentKey(%q) = %q, want %q", input, got, want)
		}
	}
}

func TestResolvePrefersEnvironment(t *testing.T) {
	resolver := Resolver{
		Environ:    func() []string { return []string{"FFMPEG=/opt/bin/ffmpeg"} },
		LookupPath: func(string) (string, error) { return "/usr/bin/ffmpeg", nil },
		Stat: func(path string) (os.FileInfo, error) {
			if path != "/opt/bin/ffmpeg" {
				return nil, os.ErrNotExist
			}
			return fakeInfo{}, nil
		},
	}
	res, err := resolver.Resolve("ffmpeg")
	if err != nil || res.Path != "/opt/bin/ffmpeg" || res.Source != SourceEnvironment {
		t.Fatalf("Resolve = %+v, %v; want the environment value", res, err)
	}
}

func TestResolveFallsBackToPath(t *testing.T) {
	resolver := Resolver{
		Environ:    func() []string { return nil },
		LookupPath: func(name string) (string, error) { return "/usr/bin/" + name, nil },
		Stat:       func(string) (os.FileInfo, error) { return fakeInfo{}, nil },
	}
	res, err := resolver.Resolve("ffmpeg")
	if err != nil || res.Path != "/usr/bin/ffmpeg" || res.Source != SourcePath {
		t.Fatalf("Resolve = %+v, %v; want PATH value", res, err)
	}
}

func TestResolveErrorNamesTheEnvironmentVariable(t *testing.T) {
	resolver := Resolver{
		Environ:    func() []string { return nil },
		LookupPath: func(string) (string, error) { return "", os.ErrNotExist },
	}
	_, err := resolver.Resolve("ffmpeg")
	if err == nil || !strings.Contains(err.Error(), "FFMPEG") || !strings.Contains(err.Error(), "PATH") {
		t.Fatalf("error must explain both configuration points, got %v", err)
	}
}

func TestResolveRejectsMissingEnvironmentValue(t *testing.T) {
	resolver := Resolver{
		Environ:    func() []string { return []string{"FFMPEG=/missing/ffmpeg"} },
		LookupPath: func(string) (string, error) { return "", os.ErrNotExist },
		Stat:       func(string) (os.FileInfo, error) { return nil, os.ErrNotExist },
	}
	if _, err := resolver.Resolve("ffmpeg"); err == nil {
		t.Fatal("an environment override pointing to a missing file must fail")
	}
}

type fakeInfo struct{}

func (fakeInfo) Name() string       { return "ffmpeg" }
func (fakeInfo) Size() int64        { return 1 }
func (fakeInfo) Mode() os.FileMode  { return 0o755 }
func (fakeInfo) ModTime() time.Time { return time.Time{} }
func (fakeInfo) IsDir() bool        { return false }
func (fakeInfo) Sys() interface{}   { return nil }

func TestBoundedWriterKeepsHeadAndTail(t *testing.T) {
	writer := NewBoundedWriter(1024)
	for i := 0; i < 200; i++ {
		_, _ = fmt.Fprintf(writer, "line-%03d\n", i)
	}
	if !writer.Truncated() {
		t.Fatal("a long stream must be marked truncated")
	}
	value := string(writer.Bytes())
	if !strings.Contains(value, "line-000") || !strings.Contains(value, "line-199") {
		t.Fatalf("bounded output must keep both ends: %q", value)
	}
	if len(value) > 1200 {
		t.Fatalf("bounded output grew to %d bytes", len(value))
	}
}

func helperCommand(t *testing.T, behaviour string) []string {
	t.Helper()
	return []string{"-test.run=TestHelperProcess", "--", behaviour}
}

// TestHelperProcess is the child used by the Runner tests.
func TestHelperProcess(t *testing.T) {
	if os.Getenv("TOOLENV_HELPER") != "1" {
		return
	}
	args := os.Args
	for index, value := range args {
		if value == "--" && index+1 < len(args) {
			args = args[index+1:]
			break
		}
	}
	behaviour := args[0]
	switch behaviour {
	case "ok":
		fmt.Fprint(os.Stdout, "done")
		os.Exit(0)
	case "fail":
		fmt.Fprint(os.Stderr, "encoder refused the input")
		os.Exit(7)
	case "sleep":
		time.Sleep(5 * time.Second)
		os.Exit(0)
	case "flood":
		for i := 0; i < 20000; i++ {
			fmt.Fprintf(os.Stderr, "frame %d progress data\n", i)
		}
		os.Exit(1)
	default:
		os.Exit(2)
	}
}

func TestRunSuccessAndExitFailure(t *testing.T) {
	binary, err := os.Executable()
	if err != nil {
		t.Fatal(err)
	}
	env := append(os.Environ(), "TOOLENV_HELPER=1")

	outcome, err := Run(context.Background(), Invocation{
		Binary: binary, Args: helperCommand(t, "ok")[0:], Env: env,
	})
	if err != nil || outcome.ExitCode != 0 {
		t.Fatalf("successful child: outcome=%+v err=%v", outcome, err)
	}

	outcome, err = Run(context.Background(), Invocation{
		Binary: binary, Args: helperCommand(t, "fail")[0:], Env: env,
	})
	var exit *ExitError
	if !errors.As(err, &exit) || exit.Code != 7 {
		t.Fatalf("non-zero child must produce ExitError(7), got %v", err)
	}
	if !strings.Contains(outcome.Stderr, "encoder refused") {
		t.Fatalf("stderr must be preserved, got %q", outcome.Stderr)
	}
	rendered := FormatFailure(err, outcome)
	if !strings.Contains(rendered, "exited with code 7") || !strings.Contains(rendered, "encoder refused") {
		t.Fatalf("FormatFailure must keep the cause and diagnostics: %q", rendered)
	}
}

func TestRunTimeoutAndCancellation(t *testing.T) {
	binary, err := os.Executable()
	if err != nil {
		t.Fatal(err)
	}
	env := append(os.Environ(), "TOOLENV_HELPER=1")
	start := time.Now()
	_, err = Run(context.Background(), Invocation{
		Binary: binary, Args: helperCommand(t, "sleep")[0:], Env: env,
		Timeout: 80 * time.Millisecond,
	})
	if !errors.Is(err, ErrTimeout) {
		t.Fatalf("a sleeping child must time out, got %v", err)
	}
	if elapsed := time.Since(start); elapsed > 3*time.Second {
		t.Fatalf("timeout took %s; WaitDelay is not bounding the wait", elapsed)
	}

	ctx, cancel := context.WithCancel(context.Background())
	cancel()
	_, err = Run(ctx, Invocation{Binary: binary, Args: helperCommand(t, "ok")[0:], Env: env})
	if !errors.Is(err, ErrCancelled) {
		t.Fatalf("a cancelled context must be reported, got %v", err)
	}
}

func TestRunStartFailureIsNotAMediaConclusion(t *testing.T) {
	_, err := Run(context.Background(), Invocation{Binary: ""})
	if !IsStartFailure(err) {
		t.Fatalf("an unresolved binary must be a start failure, got %v", err)
	}
	outcome, err := Run(context.Background(), Invocation{
		Binary: "definitely-not-an-executable-binary-xyz", Args: []string{"--version"},
	})
	if !IsStartFailure(err) {
		t.Fatalf("an unstartable binary must be a start failure, got %v", err)
	}
	rendered := FormatFailure(err, outcome)
	if strings.Contains(rendered, "no video stream") || !strings.Contains(rendered, "cannot start") {
		t.Fatalf("start failures must never be rewritten as media results: %q", rendered)
	}
}

func TestRunBoundsStderr(t *testing.T) {
	binary, err := os.Executable()
	if err != nil {
		t.Fatal(err)
	}
	env := append(os.Environ(), "TOOLENV_HELPER=1")
	outcome, err := Run(context.Background(), Invocation{
		Binary: binary, Args: helperCommand(t, "flood")[0:], Env: env,
		StderrLimit: 4096, Timeout: 10 * time.Second,
	})
	if err == nil {
		t.Fatal("flood helper must exit non-zero")
	}
	if !outcome.StderrTruncated || len(outcome.Stderr) > 4200 {
		t.Fatalf("stderr must be bounded: truncated=%v len=%d", outcome.StderrTruncated, len(outcome.Stderr))
	}
}

func TestRunUsesResolvedEnvironmentBinary(t *testing.T) {
	goPath, err := exec.LookPath("go")
	if err != nil {
		t.Skip("go toolchain is not on PATH")
	}
	resolver := Resolver{
		Environ:    func() []string { return []string{"GO=" + goPath} },
		LookupPath: exec.LookPath,
		Stat:       os.Stat,
	}
	res, err := resolver.Resolve("go")
	if err != nil || res.Source != SourceEnvironment {
		t.Fatalf("environment override failed: %+v %v", res, err)
	}
}
