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
)

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
// console window stays readable after the tool finishes. CI modes never call this.
func WaitForExit() {
	fmt.Println()
	fmt.Print("Press Enter to exit...")
	_, _ = bufio.NewReader(os.Stdin).ReadString('\n')
}

// InteractiveMenu runs when the binary is launched without arguments on an
// interactive terminal (for example, double-clicking the Windows executable). It
// lists the commands, runs the selected one in-process, and returns to the menu
// until the user quits. Non-terminal invocations keep the usage/exit-2 contract
// and never reach this function.
func InteractiveMenu(programName string, commands []Command, stdin io.Reader, stdout io.Writer) int {
	reader := bufio.NewReader(stdin)
	for {
		fmt.Fprintf(stdout, "%s %s\n\n", programName, Version)
		writeCommandList(stdout, commands)
		fmt.Fprintln(stdout)
		fmt.Fprintln(stdout, "Enter a number or command name, or q to quit:")
		line, err := reader.ReadString('\n')
		if err != nil {
			return ExitSuccess // closed input (Ctrl+Z / EOF)
		}
		choice := strings.TrimSpace(line)
		if choice == "" {
			continue
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
