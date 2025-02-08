package main

import (
	"bufio"
	"fmt"
	"io/ioutil"
	"os"
	"os/exec"
	"regexp"
	"runtime"
	"strings"
)

func main() {
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

	// Old project identifiers
	oldName := "UnityStarter"
	oldCompanyName := "CycloneGames"
	oldAppName := "UnityStarter"

	// 1. Rename the folder and its meta file
	err := renameFolderAndMeta("./Assets/"+oldName, "./Assets/"+newProjectName)
	if err != nil {
		fmt.Println("Error renaming folder:", err)
		waitForKeyPress()
		return
	}

	// 2. Update BuildScript.cs with the new names
	err = updateBuildScript("./Assets/Editor/BuildScript.cs", oldName, newProjectName, oldCompanyName, newCompanyName, oldAppName, newAppName)
	if err != nil {
		fmt.Println("Error updating BuildScript.cs:", err)
		waitForKeyPress()
		return
	}

	// 3. Update ProjectSettings.asset with the new names
	err = updateProjectSettings("./ProjectSettings/ProjectSettings.asset", oldCompanyName, newCompanyName, oldAppName, newAppName)
	if err != nil {
		fmt.Println("Error updating ProjectSettings.asset:", err)
		waitForKeyPress()
		return
	}

	// 4. Update EditorBuildSettings.asset with the new project name
	err = updateEditorBuildSettings("./ProjectSettings/EditorBuildSettings.asset", oldName, newProjectName)
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
		lines[i] = strings.Replace(line, oldFolderName, newFolderName, -1)
		lines[i] = strings.Replace(line, oldCompanyName, newCompanyName, -1)
		lines[i] = strings.Replace(line, oldAppName, newAppName, -1)
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
	content = strings.Replace(content, "companyName: "+oldCompanyName, "companyName: "+newCompanyName, -1)
	content = strings.Replace(content, "productName: "+oldAppName, "productName: "+newAppName, -1)

	// Update applicationIdentifier
	oldAppIDPrefix := "com." + oldCompanyName + "." + oldAppName
	newAppIDPrefix := "com." + newCompanyName + "." + newAppName
	content = strings.Replace(content, "Android: "+oldAppIDPrefix, "Android: "+newAppIDPrefix, -1)
	content = strings.Replace(content, "Standalone: "+oldAppIDPrefix, "Standalone: "+newAppIDPrefix, -1)
	content = strings.Replace(content, "iPhone: "+oldAppIDPrefix, "iPhone: "+newAppIDPrefix, -1)

	// Update metroPackageName and metroApplicationDescription
	content = strings.Replace(content, "metroPackageName: "+oldAppName, "metroPackageName: "+newAppName, -1)
	content = strings.Replace(content, "metroApplicationDescription: "+oldAppName, "metroApplicationDescription: "+newAppName, -1)

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
	content = strings.Replace(content, "Assets/"+oldProjectName+"/Scenes/", "Assets/"+newProjectName+"/Scenes/", -1)

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
