//	Put this file located in the Project path, parallel with ProjectFolder/Assets not in ProjectFolder/Assets path.

package main

import (
	"bufio"
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"time"
)

func main() {
	startTime := time.Now()
	fmt.Println("Starting manifest.json cleanup process...")
	fmt.Printf("Working directory: %s\n", getWorkingDir())

	dryRun := strings.EqualFold(os.Getenv("DRY_RUN"), "1")
	if dryRun {
		fmt.Println("[DRY RUN] No changes will be written. Set DRY_RUN=0 to apply.")
	}

	packagesToRemove := []string{
		"com.unity.2d.tilemap",
		"com.unity.ai.navigation",
		"com.unity.collab-proxy",
		"com.unity.multiplayer.center",
		"com.unity.test-framework",
		"com.unity.modules.accessibility",
		"com.unity.modules.ai",
		"com.unity.modules.cloth",
		"com.unity.modules.jsonserialize",
		"com.unity.modules.physics",
		"com.unity.modules.physics2d",
		"com.unity.modules.terrain",
		"com.unity.modules.terrainphysics",
		"com.unity.modules.tilemap",
		"com.unity.modules.uielements",
		"com.unity.modules.umbra",
		"com.unity.modules.unityanalytics",
		"com.unity.modules.video",
		"com.unity.modules.vehicles",
		"com.unity.modules.vr",
		"com.unity.modules.wind",
		"com.unity.modules.xr",
		"com.unity.timeline",
		"com.unity.visualscripting",
	}

	manifestPath := filepath.Join(".", "Packages", "manifest.json")
	fmt.Printf("\n[1/3] Reading %s...\n", manifestPath)
	data, err := os.ReadFile(manifestPath)
	if handleError(err, "Error reading file") {
		return
	}

	fmt.Println("[2/3] Parsing JSON structure...")
	var manifest map[string]interface{}
	if err := json.Unmarshal(data, &manifest); handleError(err, "Invalid JSON format") {
		return
	}

	fmt.Println("[3/3] Processing dependencies...")
	removedCount := 0
	if deps, ok := manifest["dependencies"].(map[string]interface{}); ok {
		for _, pkg := range packagesToRemove {
			if _, exists := deps[pkg]; exists {
				if dryRun {
					fmt.Printf("  [DRY] Would remove: %s\n", pkg)
				} else {
					delete(deps, pkg)
					fmt.Printf("  Removed package: %s\n", pkg)
					removedCount++
				}
			}
		}
		manifest["dependencies"] = deps
		if !dryRun {
			updatedData, err := json.MarshalIndent(manifest, "", "  ")
			if handleError(err, "Error marshaling JSON") {
				return
			}
			if err := os.WriteFile(manifestPath, updatedData, 0644); handleError(err, "Error writing file") {
				return
			}
		}
	} else {
		fmt.Println("Warning: No dependencies section found")
	}

	fmt.Printf("\nOperation completed%v in %v\n",
		map[bool]string{true: " (dry-run)"}[dryRun], time.Since(startTime).Round(time.Millisecond))
	fmt.Printf("Total packages removed: %d\n\n", removedCount)

	waitForExit()
}

func getWorkingDir() string {
	dir, err := os.Getwd()
	if err != nil {
		return fmt.Sprintf("[Error getting working directory: %v]", err)
	}
	return dir
}

func handleError(err error, message string) bool {
	if err != nil {
		fmt.Printf("\nERROR: %s: %v\n", message, err)
		waitForExit()
		return true
	}
	return false
}

func waitForExit() {
	fmt.Print("\nPress Enter to exit...")
	bufio.NewReader(os.Stdin).ReadBytes('\n')
	os.Exit(0)
}
