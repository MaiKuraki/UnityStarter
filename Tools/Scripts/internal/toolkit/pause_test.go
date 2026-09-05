package toolkit

import (
	"strings"
	"testing"
)

func TestShouldPauseAfterRunOnlyProcessOnConsolePauses(t *testing.T) {
	options := PauseOptions{
		StdinIsTerminal:     true,
		StdoutIsTerminal:    true,
		ConsoleProcessCount: 1, // fresh console: the classic double-click signature
	}
	if !ShouldPauseAfterRun(options) {
		t.Fatalf("sole console process should pause, options = %+v", options)
	}
}

func TestShouldPauseAfterRunShellAttachedDoesNotPause(t *testing.T) {
	options := PauseOptions{
		StdinIsTerminal:     true,
		StdoutIsTerminal:    true,
		ConsoleProcessCount: 2, // a shell is attached to the console
	}
	if ShouldPauseAfterRun(options) {
		t.Fatalf("attached shell should not pause, options = %+v", options)
	}
}

func TestShouldPauseAfterRunUnknownConsoleDoesNotPause(t *testing.T) {
	options := PauseOptions{
		StdinIsTerminal:     true,
		StdoutIsTerminal:    true,
		ConsoleProcessCount: 0, // probe failed or no console: stay conservative
	}
	if ShouldPauseAfterRun(options) {
		t.Fatalf("unknown console state should not pause, options = %+v", options)
	}
}

func TestShouldPauseAfterRunNonTerminalDoesNotPause(t *testing.T) {
	options := PauseOptions{
		StdinIsTerminal:     false,
		StdoutIsTerminal:    true,
		ConsoleProcessCount: 1,
	}
	if ShouldPauseAfterRun(options) {
		t.Fatalf("non-terminal stdin should never pause, options = %+v", options)
	}
	options.StdinIsTerminal = true
	options.StdoutIsTerminal = false
	if ShouldPauseAfterRun(options) {
		t.Fatalf("non-terminal stdout should never pause, options = %+v", options)
	}
}

func TestShouldPauseAfterRunNoPauseFlagDisables(t *testing.T) {
	options := PauseOptions{
		NoPauseFlag:         true,
		StdinIsTerminal:     true,
		StdoutIsTerminal:    true,
		ConsoleProcessCount: 1,
	}
	if ShouldPauseAfterRun(options) {
		t.Fatalf("--no-pause must disable the pause, options = %+v", options)
	}
}

func TestShouldPauseAfterRunEnvOptOutDisables(t *testing.T) {
	for _, value := range []string{"1", "true", "TRUE"} {
		options := PauseOptions{
			EnvValue:            value,
			StdinIsTerminal:     true,
			StdoutIsTerminal:    true,
			ConsoleProcessCount: 1,
		}
		if ShouldPauseAfterRun(options) {
			t.Fatalf("TOOLS_NO_PAUSE=%q must disable the pause, options = %+v", value, options)
		}
	}
	options := PauseOptions{
		EnvValue:            "",
		StdinIsTerminal:     true,
		StdoutIsTerminal:    true,
		ConsoleProcessCount: 1,
	}
	if !ShouldPauseAfterRun(options) {
		t.Fatalf("empty TOOLS_NO_PAUSE must keep the pause, options = %+v", options)
	}
}

func TestExtractNoPauseFlagRemovesFlagAndReports(t *testing.T) {
	kept, found := ExtractNoPauseFlag([]string{"texture_channel_packer", "--no-pause", "-ci", "-r", "a.png"})
	if !found {
		t.Fatalf("--no-pause was not detected")
	}
	want := []string{"texture_channel_packer", "-ci", "-r", "a.png"}
	if len(kept) != len(want) {
		t.Fatalf("kept args = %v, want %v", kept, want)
	}
	for index := range want {
		if kept[index] != want[index] {
			t.Fatalf("kept args = %v, want %v", kept, want)
		}
	}
}

func TestExtractNoPauseFlagAbsentKeepsArgs(t *testing.T) {
	args := []string{"generate_file_tree", "-profile", "standard"}
	kept, found := ExtractNoPauseFlag(args)
	if found {
		t.Fatalf("unexpected --no-pause detection")
	}
	if len(kept) != len(args) {
		t.Fatalf("kept args = %v, want %v", kept, args)
	}
}

func TestDispatchUnknownCommandReturnsUsage(t *testing.T) {
	commands := []Command{{Name: "alpha", Summary: "a", Run: func([]string) int { return 0 }}}
	var stdout, stderr strings.Builder
	if code := Dispatch("tool", []string{"beta"}, commands, &stdout, &stderr); code != ExitUsage {
		t.Fatalf("exit code = %d, want %d", code, ExitUsage)
	}
	if !strings.Contains(stderr.String(), "Unknown command") {
		t.Fatalf("stderr should contain the unknown-command error, got: %s", stderr.String())
	}
}

func TestDispatchNoArgumentsReturnsUsage(t *testing.T) {
	commands := []Command{{Name: "alpha", Summary: "a", Run: func([]string) int { return 0 }}}
	var stdout, stderr strings.Builder
	if code := Dispatch("tool", nil, commands, &stdout, &stderr); code != ExitUsage {
		t.Fatalf("exit code = %d, want %d", code, ExitUsage)
	}
}

func TestDispatchHelpGoesToStdoutAndSucceeds(t *testing.T) {
	commands := []Command{{Name: "alpha", Summary: "a", Run: func([]string) int { return 0 }}}
	var stdout, stderr strings.Builder
	if code := Dispatch("tool", []string{"--help"}, commands, &stdout, &stderr); code != ExitSuccess {
		t.Fatalf("exit code = %d, want %d", code, ExitSuccess)
	}
	if !strings.Contains(stdout.String(), "Usage:") || stderr.Len() != 0 {
		t.Fatalf("help must go to stdout only, stdout = %q stderr = %q", stdout.String(), stderr.String())
	}
}

func TestDispatchVersionAndList(t *testing.T) {
	commands := []Command{{Name: "alpha", Summary: "a", Run: func([]string) int { return 0 }}}
	var stdout, stderr strings.Builder
	if code := Dispatch("tool", []string{"--version"}, commands, &stdout, &stderr); code != ExitSuccess ||
		!strings.Contains(stdout.String(), Version) {
		t.Fatalf("--version failed: code=%d stdout=%q", code, stdout.String())
	}
	stdout.Reset()
	if code := Dispatch("tool", []string{"--list"}, commands, &stdout, &stderr); code != ExitSuccess ||
		!strings.Contains(stdout.String(), "alpha") {
		t.Fatalf("--list failed: code=%d stdout=%q", code, stdout.String())
	}
}

func TestDispatchRoutesArgumentsToCommand(t *testing.T) {
	var received []string
	commands := []Command{{Name: "alpha", Summary: "a", Run: func(args []string) int {
		received = args
		return ExitFailure
	}}}
	var stdout, stderr strings.Builder
	if code := Dispatch("tool", []string{"alpha", "-x", "1"}, commands, &stdout, &stderr); code != ExitFailure {
		t.Fatalf("exit code = %d, want %d", code, ExitFailure)
	}
	if len(received) != 2 || received[0] != "-x" || received[1] != "1" {
		t.Fatalf("command args = %v, want [-x 1]", received)
	}
}
