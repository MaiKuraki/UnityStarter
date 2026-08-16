// unitystarter_tools is the single cross-platform executable that dispatches every UnityStarter
// repository tool in-process. No PowerShell, no child processes, no runtime downloads.
//
// Build:
//
//	go build -mod=readonly -trimpath -buildvcs=false -o unitystarter_tools[.exe] ./cmd/unitystarter_tools
package main

import (
	"os"

	"cyclonegames.tools/scripts/audio_volume_normalizer"
	"cyclonegames.tools/scripts/generate_file_tree"
	"cyclonegames.tools/scripts/internal/toolkit"
	"cyclonegames.tools/scripts/remove_unity_packages"
	"cyclonegames.tools/scripts/rename_project"
	"cyclonegames.tools/scripts/texture_channel_packer"
	"cyclonegames.tools/scripts/unity_project_full_clean"
	"cyclonegames.tools/scripts/unity_video_webm_converter"
)

func main() {
	commands := []toolkit.Command{
		{Name: "audio_volume_normalizer", Summary: "Normalize audio files with category-aware loudness targets.", Run: audio_volume_normalizer.Run},
		{Name: "generate_file_tree", Summary: "Generate a Markdown directory tree.", Run: generate_file_tree.Run},
		{Name: "rename_project", Summary: "Rename a UnityStarter-derived project transactionally.", Run: rename_project.Run},
		{Name: "remove_unity_packages", Summary: "Remove explicitly authorized Unity packages.", Run: remove_unity_packages.Run},
		{Name: "texture_channel_packer", Summary: "Pack source images into RGBA texture channels.", Run: texture_channel_packer.Run},
		{Name: "unity_project_full_clean", Summary: "Clean verified Unity caches and owned build outputs.", Run: unity_project_full_clean.Run},
		{Name: "unity_video_webm_converter", Summary: "Convert videos to Unity-friendly WebM.", Run: unity_video_webm_converter.Run},
	}
	os.Exit(toolkit.Dispatch(os.Args[1:], commands, os.Stdout, os.Stderr))
}
