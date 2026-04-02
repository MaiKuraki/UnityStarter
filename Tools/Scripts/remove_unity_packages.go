// Remove Unity Packages — Remove unnecessary packages from Packages/manifest.json.
// Preserves JSON key order to keep git diffs clean.
// Supports interactive selection, category-based removal, backup, preview, and dry-run.
//
// Build: go build remove_unity_packages.go

package main

import (
	"bufio"
	"flag"
	"fmt"
	"os"
	"path/filepath"
	"regexp"
	"sort"
	"strconv"
	"strings"
	"time"
)

// ============================================================
// Configuration
// ============================================================

type packageCategory struct {
	name     string
	packages []string
}

var categories = []packageCategory{
	{
		name: "2D",
		packages: []string{
			"com.unity.2d.tilemap",
		},
	},
	{
		name: "AI / Navigation",
		packages: []string{
			"com.unity.ai.navigation",
			"com.unity.modules.ai",
		},
	},
	{
		name: "Physics",
		packages: []string{
			"com.unity.modules.physics",
			"com.unity.modules.physics2d",
			"com.unity.modules.cloth",
			"com.unity.modules.vehicles",
			"com.unity.modules.wind",
			"com.unity.modules.terrain",
			"com.unity.modules.terrainphysics",
		},
	},
	{
		name: "Visual Scripting / Timeline",
		packages: []string{
			"com.unity.timeline",
			"com.unity.visualscripting",
		},
	},
	{
		name: "XR / VR",
		packages: []string{
			"com.unity.modules.vr",
			"com.unity.modules.xr",
		},
	},
	{
		name: "Analytics / Services",
		packages: []string{
			"com.unity.collab-proxy",
			"com.unity.multiplayer.center",
			"com.unity.modules.unityanalytics",
		},
	},
	{
		name: "Testing",
		packages: []string{
			"com.unity.test-framework",
		},
	},
	{
		name: "Misc Modules",
		packages: []string{
			"com.unity.modules.accessibility",
			"com.unity.modules.jsonserialize",
			"com.unity.modules.tilemap",
			"com.unity.modules.uielements",
			"com.unity.modules.umbra",
			"com.unity.modules.video",
		},
	},
}

// Global stdin reader
var stdinReader *bufio.Reader

func init() {
	stdinReader = bufio.NewReader(os.Stdin)
}

// ============================================================
// Unity Project Validation
// ============================================================

func isUnityProject(dir string) bool {
	markers := []string{"Assets", "ProjectSettings"}
	for _, m := range markers {
		info, err := os.Stat(filepath.Join(dir, m))
		if err != nil || !info.IsDir() {
			return false
		}
	}
	return true
}

// ============================================================
// Order-Preserving JSON Operations
// ============================================================
// Go's map[string]interface{} randomizes key order on marshal.
// For manifest.json we need to preserve the original key order
// to keep git diffs clean. We operate on raw text with regex.

// readDependencies extracts package names from the "dependencies" block.
func readDependencies(content string) []string {
	// Find the "dependencies" block
	re := regexp.MustCompile(`"dependencies"\s*:\s*\{([^}]*)\}`)
	match := re.FindStringSubmatch(content)
	if len(match) < 2 {
		return nil
	}

	// Extract package names
	pkgRe := regexp.MustCompile(`"(com\.[^"]+)"`)
	matches := pkgRe.FindAllStringSubmatch(match[1], -1)
	var packages []string
	for _, m := range matches {
		packages = append(packages, m[1])
	}
	return packages
}

// removeDependencyLine removes a single package line from the JSON content.
// Handles trailing comma cleanup to keep JSON valid.
func removeDependencyLine(content, pkg string) string {
	// Pattern: "pkg": "version",\n  or  "pkg": "version"\n
	// Need to handle trailing comma edge cases
	escaped := regexp.QuoteMeta(pkg)

	// Try removing line with trailing comma first
	reWithComma := regexp.MustCompile(`\s*"` + escaped + `"\s*:\s*"[^"]*"\s*,\s*\n`)
	if reWithComma.MatchString(content) {
		return reWithComma.ReplaceAllString(content, "\n")
	}

	// Line without trailing comma (last entry) — need to also clean up
	// the comma on the previous line
	reWithoutComma := regexp.MustCompile(`,?\s*\n\s*"` + escaped + `"\s*:\s*"[^"]*"\s*\n`)
	if reWithoutComma.MatchString(content) {
		return reWithoutComma.ReplaceAllString(content, "\n")
	}

	return content
}

// ============================================================
// Backup
// ============================================================

func createBackup(manifestPath string) (string, error) {
	data, err := os.ReadFile(manifestPath)
	if err != nil {
		return "", err
	}

	backupPath := manifestPath + ".bak"
	if err := os.WriteFile(backupPath, data, 0644); err != nil {
		return "", err
	}
	return backupPath, nil
}

// ============================================================
// Package Selection
// ============================================================

// buildRemoveSet builds the full set of packages to remove from all categories.
func buildFullRemoveSet() map[string]bool {
	set := make(map[string]bool)
	for _, cat := range categories {
		for _, pkg := range cat.packages {
			set[pkg] = true
		}
	}
	return set
}

// selectInteractive lets the user select categories interactively.
func selectInteractive(existingPkgs map[string]bool) map[string]bool {
	fmt.Println("\n=============================================")
	fmt.Println("  SELECT CATEGORIES TO REMOVE")
	fmt.Println("=============================================")
	fmt.Println("  [0] All categories (default)")

	for i, cat := range categories {
		// Count how many packages in this category exist in manifest
		count := 0
		for _, pkg := range cat.packages {
			if existingPkgs[pkg] {
				count++
			}
		}
		status := fmt.Sprintf("%d/%d in manifest", count, len(cat.packages))
		fmt.Printf("  [%d] %-30s  (%s)\n", i+1, cat.name, status)
	}

	fmt.Print("\nEnter numbers separated by commas (e.g. 1,3,5), or Enter for all: ")
	input, _ := stdinReader.ReadString('\n')
	input = strings.TrimSpace(input)

	if input == "" || input == "0" {
		return buildFullRemoveSet()
	}

	set := make(map[string]bool)
	for _, numStr := range strings.Split(input, ",") {
		numStr = strings.TrimSpace(numStr)
		n, err := strconv.Atoi(numStr)
		if err != nil || n < 1 || n > len(categories) {
			continue
		}
		cat := categories[n-1]
		for _, pkg := range cat.packages {
			set[pkg] = true
		}
	}
	return set
}

// ============================================================
// Preview
// ============================================================

func printPreview(toRemove []string, kept []string) {
	fmt.Println("\n=============================================")
	fmt.Println("  PACKAGES TO REMOVE")
	fmt.Println("=============================================")

	if len(toRemove) == 0 {
		fmt.Println("  (none — all listed packages are already absent)")
		return
	}

	// Group by category for display
	catMap := make(map[string]string)
	for _, cat := range categories {
		for _, pkg := range cat.packages {
			catMap[pkg] = cat.name
		}
	}

	currentCat := ""
	for _, pkg := range toRemove {
		cat := catMap[pkg]
		if cat != currentCat {
			currentCat = cat
			fmt.Printf("\n  [%s]\n", cat)
		}
		fmt.Printf("    - %s\n", pkg)
	}

	fmt.Printf("\n  Total to remove: %d\n", len(toRemove))
	fmt.Printf("  Remaining after: %d packages\n", len(kept))
}

// ============================================================
// Utilities
// ============================================================

func waitForKeyPress() {
	fmt.Println("\nPress Enter to exit...")
	stdinReader.ReadBytes('\n')
}

// ============================================================
// Entry Point
// ============================================================

func main() {
	var (
		dryRun      bool
		ciMode      bool
		interactive bool
		listMode    bool
	)

	flag.BoolVar(&dryRun, "dry-run", false, "Preview changes without modifying files")
	flag.BoolVar(&ciMode, "ci", false, "Non-interactive mode (no prompts, removes all)")
	flag.BoolVar(&interactive, "i", false, "Interactive mode: select categories to remove")
	flag.BoolVar(&listMode, "list", false, "List all removable packages and exit")
	flag.Parse()

	// Also support legacy DRY_RUN env var
	if strings.EqualFold(os.Getenv("DRY_RUN"), "1") {
		dryRun = true
	}

	basePath, err := os.Getwd()
	if err != nil {
		fmt.Printf("[ERROR] Cannot get current directory: %v\n", err)
		if !ciMode {
			waitForKeyPress()
		}
		os.Exit(1)
	}

	fmt.Println("=============================================")
	fmt.Println("  Remove Unity Packages")
	fmt.Println("=============================================")
	fmt.Printf("Target: %s\n", basePath)

	if dryRun {
		fmt.Println("[Dry Run] No files will be modified")
	}

	// List mode
	if listMode {
		fmt.Println("\nRemovable packages by category:\n")
		for _, cat := range categories {
			fmt.Printf("[%s]\n", cat.name)
			for _, pkg := range cat.packages {
				fmt.Printf("  %s\n", pkg)
			}
			fmt.Println()
		}
		if !ciMode {
			waitForKeyPress()
		}
		return
	}

	// Validate Unity project
	if !isUnityProject(basePath) {
		fmt.Println("\n[ERROR] Current directory does not appear to be a Unity project.")
		fmt.Println("Expected 'Assets/' and 'ProjectSettings/' directories.")
		fmt.Println("Please run this tool from the Unity project root.")
		if !ciMode {
			waitForKeyPress()
		}
		os.Exit(1)
	}

	// Read manifest
	manifestPath := filepath.Join(basePath, "Packages", "manifest.json")
	content, err := os.ReadFile(manifestPath)
	if err != nil {
		fmt.Printf("\n[ERROR] Cannot read %s: %v\n", manifestPath, err)
		if !ciMode {
			waitForKeyPress()
		}
		os.Exit(1)
	}

	// Parse existing packages
	existingPackages := readDependencies(string(content))
	existingSet := make(map[string]bool)
	for _, pkg := range existingPackages {
		existingSet[pkg] = true
	}
	fmt.Printf("\nFound %d packages in manifest\n", len(existingPackages))

	// Determine which packages to remove
	var removeSet map[string]bool
	if interactive && !ciMode {
		removeSet = selectInteractive(existingSet)
	} else {
		removeSet = buildFullRemoveSet()
	}

	// Filter: only packages that actually exist in manifest
	var toRemove []string
	for _, pkg := range existingPackages {
		if removeSet[pkg] {
			toRemove = append(toRemove, pkg)
		}
	}

	// Calculate what remains
	var kept []string
	for _, pkg := range existingPackages {
		if !removeSet[pkg] {
			kept = append(kept, pkg)
		}
	}
	sort.Strings(kept)

	// Preview
	printPreview(toRemove, kept)

	if len(toRemove) == 0 {
		fmt.Println("\nNothing to remove.")
		if !ciMode {
			waitForKeyPress()
		}
		return
	}

	// Dry-run stops here
	if dryRun {
		fmt.Println("\n[Dry Run] No files were modified.")
		if !ciMode {
			waitForKeyPress()
		}
		return
	}

	// Confirmation
	if !ciMode {
		fmt.Print("\nProceed with removal? (y/N): ")
		confirm, _ := stdinReader.ReadString('\n')
		confirm = strings.TrimSpace(strings.ToLower(confirm))
		if confirm != "y" {
			fmt.Println("Operation cancelled.")
			waitForKeyPress()
			return
		}
	}

	// Create backup
	backupPath, backupErr := createBackup(manifestPath)
	if backupErr != nil {
		fmt.Printf("[WARNING] Failed to create backup: %v\n", backupErr)
		if !ciMode {
			fmt.Print("Continue without backup? (y/N): ")
			cont, _ := stdinReader.ReadString('\n')
			cont = strings.TrimSpace(strings.ToLower(cont))
			if cont != "y" {
				fmt.Println("Operation cancelled.")
				waitForKeyPress()
				return
			}
		}
	} else {
		fmt.Printf("[OK] Backup: %s\n", backupPath)
	}

	// Remove packages using text-based replacement (preserves key order)
	startTime := time.Now()
	text := string(content)
	removedCount := 0

	for _, pkg := range toRemove {
		before := text
		text = removeDependencyLine(text, pkg)
		if text != before {
			fmt.Printf("  [OK] Removed: %s\n", pkg)
			removedCount++
		} else {
			fmt.Printf("  [--] Not found in dependencies block: %s\n", pkg)
		}
	}

	// Write updated manifest
	if removedCount > 0 {
		if err := os.WriteFile(manifestPath, []byte(text), 0644); err != nil {
			fmt.Printf("\n[ERROR] Failed to write manifest: %v\n", err)
			if !ciMode {
				waitForKeyPress()
			}
			os.Exit(1)
		}
	}

	duration := time.Since(startTime)

	// Check for packages-lock.json
	lockPath := filepath.Join(basePath, "Packages", "packages-lock.json")
	hasLock := false
	if _, err := os.Stat(lockPath); err == nil {
		hasLock = true
	}

	// Summary
	fmt.Println("\n===========================================")
	fmt.Println("  REMOVAL COMPLETE")
	fmt.Println("===========================================")
	fmt.Printf("  Removed:   %d packages\n", removedCount)
	fmt.Printf("  Remaining: %d packages\n", len(kept))
	fmt.Printf("  Backup:    %s\n", backupPath)
	fmt.Printf("  Time:      %s\n", duration.Round(time.Millisecond))

	if hasLock {
		fmt.Println("\n  [TIP] packages-lock.json exists.")
		fmt.Println("  Unity will regenerate it automatically when you open the project.")
	}

	fmt.Println("\n  Please open Unity to let it resolve the updated manifest.")

	if !ciMode {
		waitForKeyPress()
	}
}
