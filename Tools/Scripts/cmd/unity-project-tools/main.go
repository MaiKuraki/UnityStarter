// unity-project-tools hosts the Unity project tools. General-purpose media and
// file tools live in the separate dev-tools binary.
//
// Build:
//
//	go build -mod=readonly -trimpath -buildvcs=false -o unity-project-tools[.exe] ./cmd/unity-project-tools
package main

import (
	"os"

	"cyclonegames.tools/scripts/internal/projectlayout"
	"cyclonegames.tools/scripts/internal/projectroot"
	"cyclonegames.tools/scripts/internal/term"
	"cyclonegames.tools/scripts/internal/toolkit"
	"cyclonegames.tools/scripts/remove_unity_packages"
	"cyclonegames.tools/scripts/rename_project"
	"cyclonegames.tools/scripts/unity_project_full_clean"
)

const programName = "unity-project-tools"

func main() {
	commands := []toolkit.Command{
		{Name: "rename_project", Summary: "Rename a Unity project transactionally.", Run: rename_project.Run},
		{Name: "remove_unity_packages", Summary: "Remove explicitly authorized Unity packages.", Run: remove_unity_packages.Run},
		{Name: "unity_project_full_clean", Summary: "Clean verified Unity caches and owned build outputs.", Run: unity_project_full_clean.Run},
	}
	args, noPause := toolkit.ExtractNoPauseFlag(os.Args[1:])
	if len(args) == 0 && term.IsTerminal(os.Stdin.Fd()) && term.IsTerminal(os.Stdout.Fd()) {
		os.Exit(toolkit.InteractiveMenu(programName, commands, os.Stdin, os.Stdout,
			toolkit.WithProjectResolver(func() (toolkit.ProjectContext, error) {
				result, err := projectroot.Resolver{
					Validate: projectroot.MarkerValidate,
					Layout:   projectlayout.Default(),
				}.Resolve("")
				if err != nil {
					return toolkit.ProjectContext{}, err
				}
				return toolkit.ProjectContext{
					Root:               result.Root,
					Origin:             result.Origin.String(),
					ExecutableFallback: result.Ambiguous(),
				}, nil
			})))
	}
	code := toolkit.Dispatch(programName, args, commands, os.Stdout, os.Stderr)
	toolkit.PauseAfterRun(noPause)
	os.Exit(code)
}
