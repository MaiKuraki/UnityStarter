package toolkit

import (
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
