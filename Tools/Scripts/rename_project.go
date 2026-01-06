package main

import (
	"bufio"
	"encoding/json"
	"fmt"
	"io/ioutil"
	"os"
	"os/exec"
	"path/filepath"
	"regexp"
	"runtime"
	"strings"
)

// Directories to exclude when searching for the main project folder
var excludedDirs = map[string]bool{
	"Build":                    true,
	"ThirdParty":               true,
	"Resources":                true,
	"Settings":                 true,
	"Plugins":                  true,
	"StreamingAssets":          true,
	"Editor Default Resources": true,
	"Gizmos":                   true,
	"Standard Assets":          true,
}

// findProjectRoot scans for a Unity project root directory in the current or immediate subdirectories.
func findProjectRoot() (string, error) {
	// Check current directory
	if _, err := os.Stat("./Assets"); err == nil {
		if _, err := os.Stat("./ProjectSettings"); err == nil {
			return ".", nil
		}
	}

	// Check immediate subdirectories
	files, err := ioutil.ReadDir(".")
	if err != nil {
		return "", err
	}
	for _, f := range files {
		if f.IsDir() {
			if _, err := os.Stat(filepath.Join(f.Name(), "Assets")); err == nil {
				if _, err := os.Stat(filepath.Join(f.Name(), "ProjectSettings")); err == nil {
					return f.Name(), nil
				}
			}
		}
	}
	return "", fmt.Errorf("Unity project root not found in current directory or immediate subdirectories")
}

// findMainProjectFolder intelligently detects the main project folder in Assets directory.
// It uses multiple heuristics to identify the correct folder.
func findMainProjectFolder(projectRoot, productName string) (string, error) {
	assetsPath := filepath.Join(projectRoot, "Assets")
	entries, err := ioutil.ReadDir(assetsPath)
	if err != nil {
		return "", fmt.Errorf("failed to read Assets directory: %v", err)
	}

	type candidate struct {
		name   string
		score  int
		reason string
	}
	var candidates []candidate

	for _, entry := range entries {
		if !entry.IsDir() {
			continue
		}

		dirName := entry.Name()

		// Skip excluded directories
		if excludedDirs[dirName] {
			continue
		}

		// Skip hidden directories
		if strings.HasPrefix(dirName, ".") || strings.HasPrefix(dirName, "_") {
			continue
		}

		dirPath := filepath.Join(assetsPath, dirName)
		score := 0
		var reasons []string

		// Check if directory name matches productName (highest priority)
		if strings.EqualFold(dirName, productName) {
			score += 100
			reasons = append(reasons, "matches productName")
		}

		// Check for project structure indicators
		subEntries, err := ioutil.ReadDir(dirPath)
		if err != nil {
			continue
		}

		hasAsmdef := false
		hasEditor := false
		hasScripts := false
		hasBuiltIn := false
		hasLiveContent := false

		for _, sub := range subEntries {
			subName := sub.Name()

			// Check for asmdef files (indicates a code package)
			if !sub.IsDir() && strings.HasSuffix(subName, ".asmdef") {
				hasAsmdef = true
			}

			if sub.IsDir() {
				switch subName {
				case "Editor":
					hasEditor = true
				case "Scripts":
					hasScripts = true
				case "BuiltIn":
					hasBuiltIn = true
				case "LiveContent":
					hasLiveContent = true
				}
			}
		}

		// Score based on structure
		if hasAsmdef {
			score += 30
			reasons = append(reasons, "contains asmdef")
		}
		if hasEditor {
			score += 20
			reasons = append(reasons, "has Editor folder")
		}
		if hasScripts {
			score += 15
			reasons = append(reasons, "has Scripts folder")
		}
		if hasBuiltIn {
			score += 25
			reasons = append(reasons, "has BuiltIn folder")
		}
		if hasLiveContent {
			score += 25
			reasons = append(reasons, "has LiveContent folder")
		}

		// Check for nested asmdef files (recursive check)
		asmdefCount := countAsmdefFiles(dirPath)
		if asmdefCount > 0 {
			score += asmdefCount * 10
			reasons = append(reasons, fmt.Sprintf("contains %d asmdef files", asmdefCount))
		}

		if score > 0 {
			candidates = append(candidates, candidate{
				name:   dirName,
				score:  score,
				reason: strings.Join(reasons, ", "),
			})
		}
	}

	if len(candidates) == 0 {
		return "", fmt.Errorf("could not find main project folder in Assets directory")
	}

	// Find the candidate with the highest score
	best := candidates[0]
	for _, c := range candidates[1:] {
		if c.score > best.score {
			best = c
		}
	}

	fmt.Printf("Detected main project folder: %s (score: %d, reason: %s)\n", best.name, best.score, best.reason)
	return best.name, nil
}

// countAsmdefFiles counts .asmdef files recursively in a directory
func countAsmdefFiles(dir string) int {
	count := 0
	filepath.Walk(dir, func(path string, info os.FileInfo, err error) error {
		if err != nil {
			return nil
		}
		if !info.IsDir() && strings.HasSuffix(info.Name(), ".asmdef") {
			count++
		}
		return nil
	})
	return count
}

// getCurrentProjectInfo reads the current project settings to find the company name and app name.
// It now uses intelligent detection for the project folder name.
func getCurrentProjectInfo(projectRoot string) (string, string, string, error) {
	// Read ProjectSettings.asset to get company and product name
	projectSettingsPath := filepath.Join(projectRoot, "ProjectSettings", "ProjectSettings.asset")
	projectSettingsBytes, err := ioutil.ReadFile(projectSettingsPath)
	if err != nil {
		return "", "", "", fmt.Errorf("failed to read %s: %v", projectSettingsPath, err)
	}
	projectSettingsContent := string(projectSettingsBytes)

	companyNameRegex := regexp.MustCompile(`companyName: (.*)`)
	productNameRegex := regexp.MustCompile(`productName: (.*)`)

	companyNameMatches := companyNameRegex.FindStringSubmatch(projectSettingsContent)
	if len(companyNameMatches) < 2 {
		return "", "", "", fmt.Errorf("could not find companyName in %s", projectSettingsPath)
	}
	companyName := strings.TrimSpace(companyNameMatches[1])

	productNameMatches := productNameRegex.FindStringSubmatch(projectSettingsContent)
	if len(productNameMatches) < 2 {
		return "", "", "", fmt.Errorf("could not find productName in %s", projectSettingsPath)
	}
	appName := strings.TrimSpace(productNameMatches[1])

	// Use intelligent detection to find the main project folder
	projectName, err := findMainProjectFolder(projectRoot, appName)
	if err != nil {
		// Fallback: try to extract from EditorBuildSettings
		editorBuildSettingsPath := filepath.Join(projectRoot, "ProjectSettings", "EditorBuildSettings.asset")
		editorBuildSettingsBytes, readErr := ioutil.ReadFile(editorBuildSettingsPath)
		if readErr != nil {
			return "", "", "", fmt.Errorf("intelligent detection failed and fallback read error: %v", readErr)
		}
		editorBuildSettingsContent := string(editorBuildSettingsBytes)

		// Extract the FIRST directory after Assets/
		projectNameRegex := regexp.MustCompile(`path: Assets/([^/]+)/`)
		projectNameMatches := projectNameRegex.FindStringSubmatch(editorBuildSettingsContent)
		if len(projectNameMatches) < 2 {
			return "", "", "", fmt.Errorf("could not detect project folder: %v", err)
		}
		projectName = strings.TrimSpace(projectNameMatches[1])
		fmt.Printf("Using fallback detection: %s\n", projectName)
	}

	return projectName, companyName, appName, nil
}

func main() {
	projectRoot, err := findProjectRoot()
	if err != nil {
		fmt.Println("Error:", err)
		waitForKeyPress()
		return
	}
	fmt.Printf("Found Unity project root at: %s\n", projectRoot)

	var newProjectName, newCompanyName, newAppName string
	reader := bufio.NewReader(os.Stdin)

	namePattern := `^[a-zA-Z_][a-zA-Z0-9_-]*$`
	nameRegex := regexp.MustCompile(namePattern)

	for {
		clearScreen()

		// Prompt user for the new project name
		fmt.Println("Step 1: Enter the New Project Name")
		fmt.Println("The folder name (Assets\\PROJECT_NAME) should only contain letters, numbers, underscores (_), and dashes (-).")
		fmt.Println("It cannot start with a number or dash.")
		fmt.Print("\nEnter the new project name: ")
		newProjectName, _ = reader.ReadString('\n')
		newProjectName = strings.TrimSpace(newProjectName)

		clearScreen()

		// Prompt user for the new company name
		fmt.Println("Step 2: Enter the New Company Name")
		fmt.Println("The name should only contain letters, numbers, underscores (_), and dashes (-).")
		fmt.Println("It cannot start with a number or dash.")
		fmt.Print("\nEnter the new company name: ")
		newCompanyName, _ = reader.ReadString('\n')
		newCompanyName = strings.TrimSpace(newCompanyName)

		clearScreen()

		// Prompt user for the new application name
		fmt.Println("Step 3: Enter the New Application Name")
		fmt.Println("The name should only contain letters, numbers, underscores (_), and dashes (-).")
		fmt.Println("It cannot start with a number or dash.")
		fmt.Print("\nEnter the new application name: ")
		newAppName, _ = reader.ReadString('\n')
		newAppName = strings.TrimSpace(newAppName)

		clearScreen()

		// Display entered information for confirmation
		fmt.Println("Step 4: Confirm the Entered Information\n")
		fmt.Printf("New Project Name: \t%s\n", newProjectName)
		fmt.Printf("New Company Name: \t%s\n", newCompanyName)
		fmt.Printf("New Application Name: \t%s\n", newAppName)
		fmt.Print("\nIs the information correct? (Y/n/r): ")

		confirm, _ := reader.ReadString('\n')
		confirm = strings.TrimSpace(strings.ToLower(confirm))

		if confirm == "" || confirm == "y" {
			// Validate names
			if !nameRegex.MatchString(newProjectName) {
				fmt.Println("Invalid project name. It must only contain letters, numbers, underscores, dashes, and cannot start with a number or dash.")
				waitForKeyPress()
				continue
			}
			if !nameRegex.MatchString(newCompanyName) {
				fmt.Println("Invalid company name. It must only contain letters, numbers, underscores, dashes, and cannot start with a number or dash.")
				waitForKeyPress()
				continue
			}
			if !nameRegex.MatchString(newAppName) {
				fmt.Println("Invalid application name. It must only contain letters, numbers, underscores, dashes, and cannot start with a number or dash.")
				waitForKeyPress()
				continue
			}
			break
		} else if confirm == "n" {
			fmt.Println("\nOperation cancelled.")
			waitForKeyPress()
			return
		} else if confirm == "r" {
			fmt.Println("\nRestarting setup...")
			waitForKeyPress()
			continue
		} else {
			fmt.Println("\nInvalid input. Please enter 'y', 'n', or 'r'.")
			waitForKeyPress()
			continue
		}
	}

	// Dynamically get old project identifiers
	oldName, oldCompanyName, oldAppName, err := getCurrentProjectInfo(projectRoot)
	if err != nil {
		fmt.Println("Error getting current project info:", err)
		waitForKeyPress()
		return
	}

	fmt.Printf("\nDetected current settings:\n")
	fmt.Printf("  Project Folder: %s\n", oldName)
	fmt.Printf("  Company Name:   %s\n", oldCompanyName)
	fmt.Printf("  App Name:       %s\n", oldAppName)
	fmt.Println("\nStarting rename operation...")

	// 1. Rename the main project folder and its meta file
	oldFolderPath := filepath.Join(projectRoot, "Assets", oldName)
	newFolderPath := filepath.Join(projectRoot, "Assets", newProjectName)
	err = renameFolderAndMeta(oldFolderPath, newFolderPath)
	if err != nil {
		fmt.Println("Error renaming folder:", err)
		waitForKeyPress()
		return
	}
	fmt.Printf("[OK] Renamed folder: %s -> %s\n", oldName, newProjectName)

	// 2. Update asmdef files in the renamed folder
	err = updateAsmdefFiles(newFolderPath, oldName, newProjectName)
	if err != nil {
		fmt.Println("Warning: Error updating asmdef files:", err)
		// Continue with other updates
	}

	// 3. Update BuildScript.cs with the new names (if exists)
	buildScriptPath := filepath.Join(projectRoot, "Assets", "Build", "Editor", "BuildPipeline", "BuildScript.cs")
	if _, statErr := os.Stat(buildScriptPath); statErr == nil {
		err = updateBuildScript(buildScriptPath, oldName, newProjectName, oldCompanyName, newCompanyName, oldAppName, newAppName)
		if err != nil {
			fmt.Println("Warning: Error updating BuildScript.cs:", err)
		} else {
			fmt.Println("[OK] Updated BuildScript.cs")
		}
	}

	// 4. Update ProjectSettings.asset with the new names
	projectSettingsPath := filepath.Join(projectRoot, "ProjectSettings", "ProjectSettings.asset")
	err = updateProjectSettings(projectSettingsPath, oldCompanyName, newCompanyName, oldAppName, newAppName)
	if err != nil {
		fmt.Println("Error updating ProjectSettings.asset:", err)
		waitForKeyPress()
		return
	}
	fmt.Println("[OK] Updated ProjectSettings.asset")

	// 5. Update EditorBuildSettings.asset with the new project name
	editorBuildSettingsPath := filepath.Join(projectRoot, "ProjectSettings", "EditorBuildSettings.asset")
	err = updateEditorBuildSettings(editorBuildSettingsPath, oldName, newProjectName)
	if err != nil {
		fmt.Println("Error updating EditorBuildSettings.asset:", err)
		waitForKeyPress()
		return
	}
	fmt.Println("[OK] Updated EditorBuildSettings.asset")

	fmt.Println("\n===========================================")
	fmt.Println("Project successfully renamed!")
	fmt.Println("===========================================")
	fmt.Printf("\nSummary:\n")
	fmt.Printf("  Folder:  Assets/%s -> Assets/%s\n", oldName, newProjectName)
	fmt.Printf("  Company: %s -> %s\n", oldCompanyName, newCompanyName)
	fmt.Printf("  App:     %s -> %s\n", oldAppName, newAppName)
	fmt.Println("\nPlease verify the changes in Unity Editor.")
	waitForKeyPress()
}

// renameFolderAndMeta renames the specified folder and its meta file
func renameFolderAndMeta(oldFolderPath, newFolderPath string) error {
	if _, err := os.Stat(newFolderPath); err == nil {
		fmt.Printf("The folder %s already exists. Do you want to overwrite it? (y/n): ", newFolderPath)
		reader := bufio.NewReader(os.Stdin)
		confirm, _ := reader.ReadString('\n')
		confirm = strings.TrimSpace(strings.ToLower(confirm))
		if confirm != "y" {
			return fmt.Errorf("folder renaming cancelled by user")
		}
		err := os.RemoveAll(newFolderPath)
		if err != nil {
			return fmt.Errorf("failed to remove existing folder %s: %v", newFolderPath, err)
		}
	}

	err := os.Rename(oldFolderPath, newFolderPath)
	if err != nil {
		return err
	}

	oldMetaPath := oldFolderPath + ".meta"
	newMetaPath := newFolderPath + ".meta"
	if _, err := os.Stat(newMetaPath); err == nil {
		fmt.Printf("The meta file %s already exists. Do you want to overwrite it? (y/n): ", newMetaPath)
		reader := bufio.NewReader(os.Stdin)
		confirm, _ := reader.ReadString('\n')
		confirm = strings.TrimSpace(strings.ToLower(confirm))
		if confirm != "y" {
			return fmt.Errorf("meta file renaming cancelled by user")
		}
		err := os.Remove(newMetaPath)
		if err != nil {
			return fmt.Errorf("failed to remove existing meta file %s: %v", newMetaPath, err)
		}
	}

	return os.Rename(oldMetaPath, newMetaPath)
}

// AsmdefContent represents the structure of a .asmdef file
type AsmdefContent struct {
	Name                 string   `json:"name"`
	RootNamespace        string   `json:"rootNamespace,omitempty"`
	References           []string `json:"references,omitempty"`
	IncludePlatforms     []string `json:"includePlatforms,omitempty"`
	ExcludePlatforms     []string `json:"excludePlatforms,omitempty"`
	AllowUnsafeCode      bool     `json:"allowUnsafeCode"`
	OverrideReferences   bool     `json:"overrideReferences"`
	PrecompiledReferences []string `json:"precompiledReferences,omitempty"`
	AutoReferenced       bool     `json:"autoReferenced"`
	DefineConstraints    []string `json:"defineConstraints,omitempty"`
	VersionDefines       []interface{} `json:"versionDefines,omitempty"`
	NoEngineReferences   bool     `json:"noEngineReferences"`
}

// updateAsmdefFiles updates all .asmdef files in the project folder
func updateAsmdefFiles(projectFolderPath, oldProjectName, newProjectName string) error {
	var errors []string

	err := filepath.Walk(projectFolderPath, func(path string, info os.FileInfo, err error) error {
		if err != nil {
			return nil
		}

		if info.IsDir() {
			return nil
		}

		if !strings.HasSuffix(info.Name(), ".asmdef") {
			return nil
		}

		// Read the asmdef file
		content, readErr := ioutil.ReadFile(path)
		if readErr != nil {
			errors = append(errors, fmt.Sprintf("failed to read %s: %v", path, readErr))
			return nil
		}

		var asmdef map[string]interface{}
		if jsonErr := json.Unmarshal(content, &asmdef); jsonErr != nil {
			errors = append(errors, fmt.Sprintf("failed to parse %s: %v", path, jsonErr))
			return nil
		}

		modified := false

		// Update the name field
		if name, ok := asmdef["name"].(string); ok {
			newName := strings.Replace(name, oldProjectName, newProjectName, -1)
			if newName != name {
				asmdef["name"] = newName
				modified = true
				fmt.Printf("[OK] Updated asmdef name: %s -> %s\n", name, newName)
			}
		}

		// Update references
		if refs, ok := asmdef["references"].([]interface{}); ok {
			newRefs := make([]interface{}, len(refs))
			for i, ref := range refs {
				if refStr, ok := ref.(string); ok {
					newRef := strings.Replace(refStr, oldProjectName, newProjectName, -1)
					if newRef != refStr {
						modified = true
						fmt.Printf("[OK] Updated asmdef reference: %s -> %s\n", refStr, newRef)
					}
					newRefs[i] = newRef
				} else {
					newRefs[i] = ref
				}
			}
			if modified {
				asmdef["references"] = newRefs
			}
		}

		if !modified {
			return nil
		}

		// Write back with proper formatting
		newContent, marshalErr := json.MarshalIndent(asmdef, "", "    ")
		if marshalErr != nil {
			errors = append(errors, fmt.Sprintf("failed to marshal %s: %v", path, marshalErr))
			return nil
		}

		// Rename the file if needed
		dir := filepath.Dir(path)
		oldFileName := info.Name()
		newFileName := strings.Replace(oldFileName, oldProjectName, newProjectName, -1)
		newPath := filepath.Join(dir, newFileName)

		// Write to new path
		if writeErr := ioutil.WriteFile(newPath, newContent, 0644); writeErr != nil {
			errors = append(errors, fmt.Sprintf("failed to write %s: %v", newPath, writeErr))
			return nil
		}

		// Also update the corresponding .meta file
		oldMetaPath := path + ".meta"
		newMetaPath := newPath + ".meta"
		if oldFileName != newFileName {
			// Remove old file if different path
			if path != newPath {
				os.Remove(path)
			}

			// Rename meta file
			if _, statErr := os.Stat(oldMetaPath); statErr == nil {
				if renameErr := os.Rename(oldMetaPath, newMetaPath); renameErr != nil {
					errors = append(errors, fmt.Sprintf("failed to rename meta file %s: %v", oldMetaPath, renameErr))
				} else {
					fmt.Printf("[OK] Renamed asmdef: %s -> %s\n", oldFileName, newFileName)
				}
			}
		}

		return nil
	})

	if err != nil {
		return err
	}

	if len(errors) > 0 {
		return fmt.Errorf("encountered %d errors: %s", len(errors), strings.Join(errors, "; "))
	}

	return nil
}

// updateBuildScript updates the BuildScript.cs file with the new project details
func updateBuildScript(filePath, oldFolderName, newFolderName, oldCompanyName, newCompanyName, oldAppName, newAppName string) error {
	input, err := ioutil.ReadFile(filePath)
	if err != nil {
		return err
	}

	lines := strings.Split(string(input), "\n")
	for i, line := range lines {
		// Apply replacements sequentially on the same line
		line = strings.Replace(line, oldFolderName, newFolderName, -1)
		line = strings.Replace(line, oldCompanyName, newCompanyName, -1)
		line = strings.Replace(line, oldAppName, newAppName, -1)
		lines[i] = line
	}

	output := strings.Join(lines, "\n")
	err = ioutil.WriteFile(filePath, []byte(output), 0644)
	if err != nil {
		return err
	}

	return nil
}

// updateProjectSettings updates the ProjectSettings.asset file with the new project details
func updateProjectSettings(filePath, oldCompanyName, newCompanyName, oldAppName, newAppName string) error {
	input, err := ioutil.ReadFile(filePath)
	if err != nil {
		return err
	}

	content := string(input)

	// Helper function for regex replacement
	replacePattern := func(pattern, replacement string) {
		re := regexp.MustCompile(pattern)
		content = re.ReplaceAllString(content, replacement)
	}

	// Update companyName and productName
	replacePattern(`companyName: .*`, "companyName: "+newCompanyName)
	replacePattern(`productName: .*`, "productName: "+newAppName)

	// Update applicationIdentifier
	newAppID := "com." + newCompanyName + "." + newAppName
	// Use capture group ${1} to preserve indentation (e.g. "    Android: ")
	replacePattern(`(Android: ).*`, "${1}"+newAppID)
	replacePattern(`(Standalone: ).*`, "${1}"+newAppID)
	replacePattern(`(iPhone: ).*`, "${1}"+newAppID)
	replacePattern(`(WebGL: ).*`, "${1}"+newAppID)

	// Update metroPackageName and metroApplicationDescription
	replacePattern(`(metroPackageName: ).*`, "${1}"+newAppName)
	replacePattern(`(metroApplicationDescription: ).*`, "${1}"+newAppName)

	err = ioutil.WriteFile(filePath, []byte(content), 0644)
	if err != nil {
		return err
	}

	return nil
}

// updateEditorBuildSettings updates the EditorBuildSettings.asset file with the new project name
func updateEditorBuildSettings(filePath, oldProjectName, newProjectName string) error {
	input, err := ioutil.ReadFile(filePath)
	if err != nil {
		return err
	}

	content := string(input)

	// Replace all occurrences of the old project folder path with the new one
	// This handles any nested paths like:
	//   Assets/OldName/BuiltIn/Scenes/... -> Assets/NewName/BuiltIn/Scenes/...
	//   Assets/OldName/LiveContent/Scenes/... -> Assets/NewName/LiveContent/Scenes/...
	oldPathPrefix := "Assets/" + oldProjectName + "/"
	newPathPrefix := "Assets/" + newProjectName + "/"
	content = strings.Replace(content, oldPathPrefix, newPathPrefix, -1)

	err = ioutil.WriteFile(filePath, []byte(content), 0644)
	if err != nil {
		return err
	}

	return nil
}

// waitForKeyPress waits for the user to press any key before closing
func waitForKeyPress() {
	fmt.Println("\nPress any key to continue...")
	bufio.NewReader(os.Stdin).ReadBytes('\n')
}

// clearScreen clears the terminal screen
func clearScreen() {
	var cmd *exec.Cmd
	switch runtime.GOOS {
	case "windows":
		cmd = exec.Command("cmd", "/c", "cls")
	case "linux", "darwin":
		cmd = exec.Command("clear")
	default:
		return
	}
	cmd.Stdout = os.Stdout
	cmd.Run()
}
