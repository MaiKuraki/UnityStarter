// dev-tools is the cross-platform executable for the general-purpose repository tools
// (audio normalization, video conversion, texture packing, directory trees).
// Unity-project tools live in the separate cmd/unity-project-tools binary.
package main

import (
	"os"

	"cyclonegames.tools/scripts/audio_volume_normalizer"
	"cyclonegames.tools/scripts/generate_file_tree"
	"cyclonegames.tools/scripts/internal/term"
	"cyclonegames.tools/scripts/internal/toolkit"
	"cyclonegames.tools/scripts/texture_channel_packer"
	"cyclonegames.tools/scripts/unity_video_webm_converter"
)

const programName = "dev-tools"

func main() {
	commands := []toolkit.Command{
		{Name: "audio_volume_normalizer", Summary: "Normalize audio files with category-aware loudness targets.", Run: audio_volume_normalizer.Run},
		{Name: "generate_file_tree", Summary: "Generate a Markdown directory tree.", Run: generate_file_tree.Run},
		{Name: "texture_channel_packer", Summary: "Pack source images into RGBA texture channels.", Run: texture_channel_packer.Run},
		{Name: "unity_video_webm_converter", Summary: "Convert videos to Unity-friendly WebM.", Run: unity_video_webm_converter.Run},
	}
	args := os.Args[1:]
	if len(args) == 0 && term.IsTerminal(os.Stdin.Fd()) && term.IsTerminal(os.Stdout.Fd()) {
		// Launched by double-click (or a plain no-argument run) on an interactive
		// terminal: show the command menu instead of the usage error.
		os.Exit(toolkit.InteractiveMenu(programName, commands, os.Stdin, os.Stdout))
	}
	os.Exit(toolkit.Dispatch(programName, args, commands, os.Stdout, os.Stderr))
}
