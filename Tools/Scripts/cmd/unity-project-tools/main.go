// unity-project-tools is the cross-platform executable for the Unity project tools
// (project rename, package removal, cache cleanup). General-purpose tools live in
// the separate cmd/dev-tools binary.
//
// Build:
//
//	go build -mod=readonly -trimpath -buildvcs=false -o unity-project-tools[.exe] ./cmd/unity-project-tools
package main

import (
	"os"

	"cyclonegames.tools/scripts/internal/term"
	"cyclonegames.tools/scripts/internal/toolkit"
	"cyclonegames.tools/scripts/remove_unity_packages"
	"cyclonegames.tools/scripts/rename_project"
	"cyclonegames.tools/scripts/unity_project_full_clean"
)

const programName = "unity-project-tools"

func main() {
	commands := []toolkit.Command{
		{Name: "rename_project", Summary: "Rename a UnityStarter-derived project transactionally.", Run: rename_project.Run},
		{Name: "remove_unity_packages", Summary: "Remove explicitly authorized Unity packages.", Run: remove_unity_packages.Run},
		{Name: "unity_project_full_clean", Summary: "Clean verified Unity caches and owned build outputs.", Run: unity_project_full_clean.Run},
	}
	args, noPause := toolkit.ExtractNoPauseFlag(os.Args[1:])
	if len(args) == 0 && term.IsTerminal(os.Stdin.Fd()) && term.IsTerminal(os.Stdout.Fd()) {
		// Launched by double-click (or a plain no-argument run) on an interactive
		// terminal: show the command menu instead of the usage error.
		os.Exit(toolkit.InteractiveMenu(programName, commands, os.Stdin, os.Stdout))
	}
	code := toolkit.Dispatch(programName, args, commands, os.Stdout, os.Stderr)
	// Keep a double-clicked console window readable after the run; scripts and
	// CI callers (pipes, shells, --no-pause, TOOLS_NO_PAUSE=1) are never paused.
	toolkit.PauseAfterRun(noPause)
	os.Exit(code)
}
