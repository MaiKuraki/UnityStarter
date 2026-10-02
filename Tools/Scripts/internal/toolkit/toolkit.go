// Package toolkit provides the shared command registry and dispatch contract for the
// tool binaries (unity-project-tools and dev-tools).
package toolkit

import (
	"bufio"
	"fmt"
	"io"
	"os"
	"sort"
	"strconv"
	"strings"

	"cyclonegames.tools/scripts/internal/term"
)

// pausedThisProcess records whether WaitForExit already paused once, so the
// post-dispatch pause hook does not ask the user twice in a single run.
var pausedThisProcess = false

// Version is the shared release version of both tool binaries.
// The tools have not shipped yet, so the baseline starts at 0.1.0.
const Version = "0.1.0"

// Exit codes shared by every tool command.
const (
	ExitSuccess   = 0   // completed successfully
	ExitFailure   = 1   // completed with failures
	ExitUsage     = 2   // invalid command line
	ExitCancelled = 130 // run cancelled by a user signal (128 + SIGINT)
)

// Command describes one dispatchable tool command.
type Command struct {
	Name    string
	Summary string
	Run     func(args []string) int
}

// Dispatch routes the command line to a registered command and returns its exit code.
// Every tool executes in-process: there are no child processes, downloads, or temporary files.
func Dispatch(programName string, args []string, commands []Command, stdout, stderr io.Writer) int {
	if len(args) == 0 {
		writeUsage(programName, stderr, commands)
		return 2
	}

	switch args[0] {
	case "-h", "--help", "help":
		writeUsage(programName, stdout, commands)
		return 0
	case "--version", "version":
		fmt.Fprintf(stdout, "%s %s\n", programName, Version)
		return 0
	case "--list", "list":
		writeCommandList(stdout, commands)
		return 0
	}

	for _, command := range commands {
		if command.Name == args[0] {
			return command.Run(args[1:])
		}
	}

	fmt.Fprintf(stderr, "[ERROR] Unknown command %q.\n\n", args[0])
	writeCommandList(stderr, commands)
	return 2
}

func writeUsage(programName string, w io.Writer, commands []Command) {
	fmt.Fprintf(w, "%s %s\n\n", programName, Version)
	fmt.Fprintf(w, "Usage: %s <command> [arguments]\n", programName)
	fmt.Fprintln(w)
	writeCommandList(w, commands)
	fmt.Fprintln(w)
	fmt.Fprintf(w, "Run '%s <command> --help' for command-specific help.\n", programName)
}

// WaitForExit pauses an interactive session until the user presses Enter, so the
// console window stays readable after the tool finishes. The pause only happens
// when both stdin and stdout are interactive terminals; pipes, redirects, and CI
// runners never block here. CI modes never call this unless they run on a TTY.
func WaitForExit() {
	if !term.IsTerminal(os.Stdin.Fd()) || !term.IsTerminal(os.Stdout.Fd()) {
		return
	}
	pausedThisProcess = true
	fmt.Println()
	fmt.Print("Press Enter to exit...")
	_, _ = bufio.NewReader(os.Stdin).ReadString('\n')
}

// ProjectContext describes the Unity project an interactive command will act on.
type ProjectContext struct {
	Root   string
	Origin string
	// ExecutableFallback marks a root inferred from the executable's own directory
	// (the ambiguous double-click fallback), so the menu can say so explicitly.
	ExecutableFallback bool
}

// ProjectResolver reports the Unity project context for the menu's commands.
type ProjectResolver func() (ProjectContext, error)

// MenuOption customizes InteractiveMenu.
type MenuOption func(*menuConfig)

type menuConfig struct {
	resolveProject ProjectResolver
}

// WithProjectResolver makes the interactive menu report the resolved Unity
// project root, and an actionable hint (including the --project usage) when none
// can be found. Callers whose commands do not operate on a Unity project omit it.
func WithProjectResolver(resolver ProjectResolver) MenuOption {
	return func(config *menuConfig) {
		config.resolveProject = resolver
	}
}

// InteractiveMenu runs when the binary is launched without arguments on an
// interactive terminal (for example, double-clicking the Windows executable). It
// lists the commands, runs the selected one in-process, and returns to the menu
// until the user quits. Non-terminal invocations keep the usage/exit-2 contract
// and never reach this function.
func InteractiveMenu(programName string, commands []Command, stdin io.Reader, stdout io.Writer, options ...MenuOption) int {
	config := menuConfig{}
	for _, option := range options {
		option(&config)
	}
	// Resolve the project context once: the working directory does not change
	// while the menu is open, and a failed resolution walks the filesystem.
	projectLines := projectContextLines(config.resolveProject)
	reader := bufio.NewReader(stdin)
	firstCycle := true
	for {
		if firstCycle {
			firstCycle = false
		} else {
			// Separate the restarted menu from the previous command's output so
			// it does not look like the menu was printed twice by mistake.
			fmt.Fprintln(stdout, "--------------------------------------------------------------")
		}
		fmt.Fprintf(stdout, "%s %s\n\n", programName, Version)
		for _, line := range projectLines {
			fmt.Fprintln(stdout, line)
		}
		writeCommandList(stdout, commands)
		fmt.Fprintln(stdout)

		// Re-prompt on a bare Enter instead of redrawing the whole menu: the
		// old behaviour silently reprinted everything, which read as a
		// duplicated menu rather than "nothing was entered".
		choice := ""
		for choice == "" {
			fmt.Fprintln(stdout, "Enter a number or command name, or q to quit:")
			line, err := reader.ReadString('\n')
			if err != nil {
				return ExitSuccess // closed input (Ctrl+Z / EOF)
			}
			choice = strings.TrimSpace(line)
			if choice == "" {
				fmt.Fprintln(stdout, "Please enter a number, a command name, or q to quit.")
			}
		}
		if choice == "q" || choice == "quit" || choice == "exit" {
			return ExitSuccess
		}
		selected := matchMenuChoice(commands, choice)
		if selected == nil {
			fmt.Fprintf(stdout, "Unknown command %q.\n\n", choice)
			continue
		}
		code := selected.Run(nil)
		fmt.Fprintln(stdout)
		fmt.Fprintf(stdout, "[%s] finished with exit code %d\n\n", selected.Name, code)
	}
}

// projectContextLines renders the "current project" banner plus an actionable
// hint. It returns nil when the caller supplied no resolver, so non-Unity tools
// keep the original menu unchanged.
func projectContextLines(resolver ProjectResolver) []string {
	if resolver == nil {
		return nil
	}
	context, err := resolver()
	if err != nil {
		return []string{
			"Current project: not detected.",
			"Tip: run this tool from a Unity project root (a directory containing Assets, ProjectSettings and Packages), " +
				"or pass --project <path> to the command.",
		}
	}
	lines := []string{"Current project: " + context.Root}
	if context.ExecutableFallback {
		lines = append(lines,
			"  (inferred from the executable's own directory, not from the working directory or --project; pass --project <path> to be explicit)")
	}
	return lines
}

func matchMenuChoice(commands []Command, choice string) *Command {
	if index, err := strconv.Atoi(choice); err == nil {
		sorted := sortedCommands(commands)
		if index >= 1 && index <= len(sorted) {
			return &sorted[index-1]
		}
		return nil
	}
	for i := range commands {
		if commands[i].Name == choice {
			return &commands[i]
		}
	}
	return nil
}

func sortedCommands(commands []Command) []Command {
	sorted := make([]Command, len(commands))
	copy(sorted, commands)
	sort.Slice(sorted, func(i, j int) bool { return sorted[i].Name < sorted[j].Name })
	return sorted
}

func writeCommandList(w io.Writer, commands []Command) {
	for index, command := range sortedCommands(commands) {
		fmt.Fprintf(w, "  %2d. %-26s %s\n", index+1, command.Name, command.Summary)
	}
}
