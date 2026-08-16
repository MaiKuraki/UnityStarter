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
