package toolkit

import (
	"os"
	"runtime"

	"cyclonegames.tools/scripts/internal/term"
)

// PauseOptions carries every input of the "keep the console window open" decision
// so the policy stays a pure, testable function of its inputs.
type PauseOptions struct {
	// NoPauseFlag reports whether the caller passed --no-pause.
	NoPauseFlag bool
	// EnvValue is the raw value of the TOOLS_NO_PAUSE environment variable.
	EnvValue string
	// StdinIsTerminal / StdoutIsTerminal report interactive-terminal attachment.
	StdinIsTerminal  bool
	StdoutIsTerminal bool
	// ConsoleProcessCount is the number of processes attached to the current
	// console. 1 means this process is alone in a fresh console (the classic
	// double-click signature); more than one means a shell is attached.
	ConsoleProcessCount int
}

// ShouldPauseAfterRun implements the default post-run pause policy:
//   - --no-pause disables the pause (explicit CLI opt-out);
//   - TOOLS_NO_PAUSE=1 disables the pause (script/CI opt-out);
//   - a non-terminal stdin or stdout disables the pause (CI/pipe safety);
//   - more than one console process (shell attached) disables the pause;
//   - otherwise (fresh console with only this process) the pause runs.
//
// The platform gate (pause is a Windows double-click feature) is applied by the
// caller, keeping this function portable and unit-testable on every platform.
func ShouldPauseAfterRun(options PauseOptions) bool {
	if options.NoPauseFlag {
		return false
	}
	if options.EnvValue == "1" || options.EnvValue == "true" || options.EnvValue == "TRUE" {
		return false
	}
	if !options.StdinIsTerminal || !options.StdoutIsTerminal {
		return false
	}
	return options.ConsoleProcessCount == 1
}

// PauseAfterRun keeps the console window open after an argument-driven run when
// the process looks like it was launched by double-clicking the executable on
// Windows (fresh console, interactive terminals, no opt-out). CI pipelines,
// scripts, and shells attached to the console are never blocked.
func PauseAfterRun(noPauseFlag bool) {
	if runtime.GOOS != "windows" {
		return
	}
	if pausedThisProcess {
		return
	}
	options := PauseOptions{
		NoPauseFlag:         noPauseFlag,
		EnvValue:            os.Getenv("TOOLS_NO_PAUSE"),
		StdinIsTerminal:     term.IsTerminal(os.Stdin.Fd()),
		StdoutIsTerminal:    term.IsTerminal(os.Stdout.Fd()),
		ConsoleProcessCount: consoleProcessCount(),
	}
	if !ShouldPauseAfterRun(options) {
		return
	}
	WaitForExit()
}

// ExtractNoPauseFlag removes the --no-pause flag from the argument list so the
// subcommand flag parsers never see it, and reports whether it was present.
func ExtractNoPauseFlag(args []string) ([]string, bool) {
	kept := make([]string, 0, len(args))
	found := false
	for _, arg := range args {
		switch arg {
		case "--no-pause", "--no-pause=true", "--no-pause=1":
			found = true
			continue
		}
		kept = append(kept, arg)
	}
	return kept, found
}
