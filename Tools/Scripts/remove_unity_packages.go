//	Put this file located in the Project path, parallel with ProjectFolder/Assets not in ProjectFolder/Assets path.

package main

import (
	"bufio"
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"time"
)

func main() {
	startTime := time.Now()
	fmt.Println("Starting manifest.json cleanup process...")

	// Print Debug info
	fmt.Printf("Working directory: %s\n", getWorkingDir())

	// List of packages to remove
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

	// Read manifest.json
	manifestPath := filepath.Join(".", "Packages", "manifest.json")
	fmt.Printf("\n[1/4] Reading %s...\n", manifestPath)

	if !fileExists(manifestPath) {
		fmt.Printf("\nERROR: File not found: %s\n", manifestPath)
		waitForExit()
		return
	}

	data, err := os.ReadFile("./Packages/manifest.json")
	if handleError(err, "Error reading file") {
		return
	}

	// Parse JSON
	fmt.Println("[2/4] Parsing JSON structure...")
	var manifest map[string]interface{}
	if err := json.Unmarshal(data, &manifest); handleError(err, "Invalid JSON format") {
		return
	}

	// Process dependencies
	fmt.Println("[3/4] Processing dependencies...")
	removedCount := 0
	if deps, ok := manifest["dependencies"].(map[string]interface{}); ok {
		// Remove unused variable declaration
		for _, pkg := range packagesToRemove {
			if _, exists := deps[pkg]; exists {
				delete(deps, pkg)
				fmt.Printf("  Removed package: %s\n", pkg)
				removedCount++
			}
		}
		manifest["dependencies"] = deps
		fmt.Printf("\nRemoved %d/%d specified packages", removedCount, len(packagesToRemove))
		fmt.Printf("\nRemaining dependencies: %d\n", len(deps))
	} else {
		fmt.Println("Warning: No dependencies section found")
	}

	// Write modified JSON
	fmt.Println("[4/4] Writing changes to file...")
	updatedData, err := json.MarshalIndent(manifest, "", "  ")
	if handleError(err, "Error marshaling JSON") {
		return
	}

	if err := os.WriteFile("./Packages/manifest.json", updatedData, 0644); handleError(err, "Error writing file") {
		return
	}

	// Display summary
	fmt.Printf("\nOperation completed successfully in %v\n", time.Since(startTime).Round(time.Millisecond))
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

func fileExists(path string) bool {
	_, err := os.Stat(path)
	return !os.IsNotExist(err)
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
