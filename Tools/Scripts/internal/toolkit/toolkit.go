// Package toolkit provides the shared command registry and dispatch contract for the
// unitystarter_tools single-binary entry point.
package toolkit

import (
	"bufio"
	"fmt"
	"io"
	"os"
	"sort"
)

// Version is the unitystarter_tools release version.
const Version = "2.0.0"

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
func Dispatch(args []string, commands []Command, stdout, stderr io.Writer) int {
	if len(args) == 0 {
		writeUsage(stderr, commands)
		return 2
	}

	switch args[0] {
	case "-h", "--help", "help":
		writeUsage(stdout, commands)
		return 0
	case "--version", "version":
		fmt.Fprintf(stdout, "unitystarter_tools %s\n", Version)
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

func writeUsage(w io.Writer, commands []Command) {
	fmt.Fprintf(w, "unitystarter_tools %s\n\n", Version)
	fmt.Fprintln(w, "Usage: unitystarter_tools <command> [arguments]")
	fmt.Fprintln(w)
	writeCommandList(w, commands)
	fmt.Fprintln(w)
	fmt.Fprintln(w, "Run 'unitystarter_tools <command> --help' for command-specific help.")
}

// WaitForExit pauses an interactive session until the user presses Enter, so the
// console window stays readable after the tool finishes. CI modes never call this.
func WaitForExit() {
	fmt.Println()
	fmt.Print("Press Enter to exit...")
	_, _ = bufio.NewReader(os.Stdin).ReadString('\n')
}

func writeCommandList(w io.Writer, commands []Command) {
	sorted := make([]Command, len(commands))
	copy(sorted, commands)
	sort.Slice(sorted, func(i, j int) bool { return sorted[i].Name < sorted[j].Name })
	for _, command := range sorted {
		fmt.Fprintf(w, "  %-26s %s\n", command.Name, command.Summary)
	}
}
