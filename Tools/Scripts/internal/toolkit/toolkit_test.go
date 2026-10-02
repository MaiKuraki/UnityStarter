package toolkit

import (
	"errors"
	"strings"
	"testing"
)

func TestInteractiveMenuRunsSelectionByNumberThenQuits(t *testing.T) {
	ran := ""
	commands := []Command{
		{Name: "zeta", Summary: "z", Run: func([]string) int { ran = "zeta"; return 1 }},
		{Name: "alpha", Summary: "a", Run: func([]string) int { ran = "alpha"; return 0 }},
	}
	var output strings.Builder
	code := InteractiveMenu("test-tool", commands, strings.NewReader("1\nq\n"), &output)
	if code != ExitSuccess {
		t.Fatalf("exit code = %d, want %d", code, ExitSuccess)
	}
	if ran != "alpha" {
		t.Fatalf("selected command = %q, want %q (sorted menu position 1)", ran, "alpha")
	}
}

func TestInteractiveMenuRunsSelectionByName(t *testing.T) {
	ran := ""
	commands := []Command{
		{Name: "zeta", Summary: "z", Run: func([]string) int { ran = "zeta"; return 0 }},
	}
	var output strings.Builder
	code := InteractiveMenu("test-tool", commands, strings.NewReader("zeta\nq\n"), &output)
	if code != ExitSuccess || ran != "zeta" {
		t.Fatalf("code=%d ran=%q, want 0 and zeta", code, ran)
	}
}

func TestInteractiveMenuUnknownChoiceKeepsLooping(t *testing.T) {
	ran := ""
	commands := []Command{
		{Name: "zeta", Summary: "z", Run: func([]string) int { ran = "zeta"; return 0 }},
	}
	var output strings.Builder
	code := InteractiveMenu("test-tool", commands, strings.NewReader("nope\n99\nzeta\nq\n"), &output)
	if code != ExitSuccess || ran != "zeta" {
		t.Fatalf("code=%d ran=%q, want 0 and zeta", code, ran)
	}
	if !strings.Contains(output.String(), "Unknown command") {
		t.Fatalf("expected unknown-command feedback, got:\n%s", output.String())
	}
}

func TestInteractiveMenuEOFReturnsSuccess(t *testing.T) {
	commands := []Command{{Name: "zeta", Summary: "z", Run: func([]string) int { return 0 }}}
	var output strings.Builder
	if code := InteractiveMenu("test-tool", commands, strings.NewReader(""), &output); code != ExitSuccess {
		t.Fatalf("exit code = %d, want %d", code, ExitSuccess)
	}
}

// A bare Enter used to redraw the entire menu without feedback, which read as
// the menu being printed twice. It must re-prompt with a hint instead and still
// run the following selection exactly once.
func TestInteractiveMenuEmptyInputRePromptsWithoutRedraw(t *testing.T) {
	runs := 0
	commands := []Command{
		{Name: "zeta", Summary: "z", Run: func([]string) int { runs++; return 0 }},
	}
	var output strings.Builder
	code := InteractiveMenu("test-tool", commands, strings.NewReader("\n\nzeta\nq\n"), &output)
	if code != ExitSuccess {
		t.Fatalf("exit code = %d, want %d", code, ExitSuccess)
	}
	if runs != 1 {
		t.Fatalf("command ran %d times, want 1", runs)
	}
	if !strings.Contains(output.String(), "Please enter a number") {
		t.Fatalf("expected empty-input feedback, got:\n%s", output.String())
	}
	// One banner for the first menu plus one when the menu restarts after the
	// command; empty input must not add another redraw.
	if got := strings.Count(output.String(), "test-tool "+Version); got != 2 {
		t.Fatalf("banner drawn %d times, want 2, got:\n%s", got, output.String())
	}
}

func TestInteractiveMenuShowsResolvedProjectRoot(t *testing.T) {
	commands := []Command{{Name: "zeta", Summary: "z", Run: func([]string) int { return 0 }}}
	var output strings.Builder
	code := InteractiveMenu("test-tool", commands, strings.NewReader("q\n"), &output,
		WithProjectResolver(func() (ProjectContext, error) {
			return ProjectContext{Root: "/repo/UnityStarter", Origin: "current working directory"}, nil
		}))
	if code != ExitSuccess {
		t.Fatalf("exit code = %d, want %d", code, ExitSuccess)
	}
	if !strings.Contains(output.String(), "Current project: /repo/UnityStarter") {
		t.Fatalf("menu must show the resolved project, got:\n%s", output.String())
	}
}

// An executable-directory fallback root must be flagged in the menu so the user
// knows the path was inferred, not chosen.
func TestInteractiveMenuFlagsExecutableFallback(t *testing.T) {
	commands := []Command{{Name: "zeta", Summary: "z", Run: func([]string) int { return 0 }}}
	var output strings.Builder
	if code := InteractiveMenu("test-tool", commands, strings.NewReader("q\n"), &output,
		WithProjectResolver(func() (ProjectContext, error) {
			return ProjectContext{Root: "/repo/UnityStarter", Origin: "executable directory fallback", ExecutableFallback: true}, nil
		})); code != ExitSuccess {
		t.Fatalf("exit code = %d, want %d", code, ExitSuccess)
	}
	text := output.String()
	if !strings.Contains(text, "Current project: /repo/UnityStarter") {
		t.Fatalf("menu must show the fallback project, got:\n%s", text)
	}
	if !strings.Contains(text, "inferred from the executable's own directory") {
		t.Fatalf("menu must flag the executable fallback, got:\n%s", text)
	}
}

func TestInteractiveMenuHintsWhenProjectUnresolved(t *testing.T) {
	commands := []Command{{Name: "zeta", Summary: "z", Run: func([]string) int { return 0 }}}
	var output strings.Builder
	code := InteractiveMenu("test-tool", commands, strings.NewReader("q\n"), &output,
		WithProjectResolver(func() (ProjectContext, error) { return ProjectContext{}, errors.New("no project") }))
	if code != ExitSuccess {
		t.Fatalf("exit code = %d, want %d", code, ExitSuccess)
	}
	text := output.String()
	if !strings.Contains(text, "Current project: not detected") {
		t.Fatalf("unresolved project must be reported as not detected, got:\n%s", text)
	}
	if !strings.Contains(text, "--project") {
		t.Fatalf("unresolved project must hint at --project, got:\n%s", text)
	}
}

// Tools without a project resolver (dev-tools) must keep the original menu header.
func TestInteractiveMenuWithoutResolverOmitsProjectLine(t *testing.T) {
	commands := []Command{{Name: "zeta", Summary: "z", Run: func([]string) int { return 0 }}}
	var output strings.Builder
	if code := InteractiveMenu("test-tool", commands, strings.NewReader("q\n"), &output); code != ExitSuccess {
		t.Fatalf("exit code = %d, want %d", code, ExitSuccess)
	}
	if strings.Contains(output.String(), "Current project") {
		t.Fatalf("menu without a resolver must not print a project line, got:\n%s", output.String())
	}
}
