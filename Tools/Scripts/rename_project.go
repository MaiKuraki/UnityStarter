package main

import (
	"bufio"
	"fmt"
	"io/ioutil"
	"os"
	"os/exec"
	"path/filepath"
	"regexp"
	"runtime"
	"strings"
)

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

// getCurrentProjectInfo reads the current project settings to find the project name, company name, and app name.
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

	// Read EditorBuildSettings.asset to get project name from scene path
	editorBuildSettingsPath := filepath.Join(projectRoot, "ProjectSettings", "EditorBuildSettings.asset")
	editorBuildSettingsBytes, err := ioutil.ReadFile(editorBuildSettingsPath)
	if err != nil {
		return "", "", "", fmt.Errorf("failed to read %s: %v", editorBuildSettingsPath, err)
	}
	editorBuildSettingsContent := string(editorBuildSettingsBytes)

	// In Unity, scene paths in EditorBuildSettings use forward slashes regardless of OS.
	projectNameRegex := regexp.MustCompile(`path: Assets/(.*?)/Scenes/`)
	projectNameMatches := projectNameRegex.FindStringSubmatch(editorBuildSettingsContent)
	if len(projectNameMatches) < 2 {
		// Fallback: check directories in Assets
		assetsPath := filepath.Join(projectRoot, "Assets")
		files, err := ioutil.ReadDir(assetsPath)
		if err != nil {
			return "", "", "", fmt.Errorf("could not find project name in %s and failed to scan %s: %v", editorBuildSettingsPath, assetsPath, err)
		}
		for _, f := range files {
			if f.IsDir() {
				// A simple heuristic: if a directory has a "Scenes" subdirectory, it's likely the project folder.
				scenesPath := filepath.Join(assetsPath, f.Name(), "Scenes")
				if _, err := os.Stat(scenesPath); err == nil {
					projectName := f.Name()
					return projectName, companyName, appName, nil
				}
			}
		}
		return "", "", "", fmt.Errorf("could not find project name in %s or by scanning %s", editorBuildSettingsPath, assetsPath)
	}
	projectName := strings.TrimSpace(projectNameMatches[1])

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
		fmt.Println("The folder name (Assets\\RPROJECT_NAME) should only contain letters, numbers, underscores (_), and dashes (-).")
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

	// 1. Rename the folder and its meta file
	err = renameFolderAndMeta(filepath.Join(projectRoot, "Assets", oldName), filepath.Join(projectRoot, "Assets", newProjectName))
	if err != nil {
		fmt.Println("Error renaming folder:", err)
		waitForKeyPress()
		return
	}

	// 2. Update BuildScript.cs with the new names
	buildScriptPath := filepath.Join(projectRoot, "Assets", "Build", "Editor", "BuildPipeline", "BuildScript.cs")
	err = updateBuildScript(buildScriptPath, oldName, newProjectName, oldCompanyName, newCompanyName, oldAppName, newAppName)
	if err != nil {
		fmt.Println("Error updating BuildScript.cs:", err)
		waitForKeyPress()
		return
	}

	// 3. Update ProjectSettings.asset with the new names
	projectSettingsPath := filepath.Join(projectRoot, "ProjectSettings", "ProjectSettings.asset")
	err = updateProjectSettings(projectSettingsPath, oldCompanyName, newCompanyName, oldAppName, newAppName)
	if err != nil {
		fmt.Println("Error updating ProjectSettings.asset:", err)
		waitForKeyPress()
		return
	}

	// 4. Update EditorBuildSettings.asset with the new project name
	editorBuildSettingsPath := filepath.Join(projectRoot, "ProjectSettings", "EditorBuildSettings.asset")
	err = updateEditorBuildSettings(editorBuildSettingsPath, oldName, newProjectName)
	if err != nil {
		fmt.Println("Error updating EditorBuildSettings.asset:", err)
		waitForKeyPress()
		return
	}

	fmt.Println("\nProject successfully renamed!")
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
	// Unity paths use forward slashes
	oldPath := "Assets/" + oldProjectName + "/Scenes/"
	newPath := "Assets/" + newProjectName + "/Scenes/"
	content = strings.Replace(content, oldPath, newPath, -1)

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
