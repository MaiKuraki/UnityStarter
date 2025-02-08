package main

import (
	"bufio"
	"fmt"
	"os"
	"path/filepath"
)

// Folders and file extensions to delete
var directoriesToDelete = []string{
	".vs",
	".idea",
	".vscode",
	".utmp",
	"obj",
	"Logs",
	"Temp",
	"Library",
	"SceneBackups",
	"MemoryCaptures",
	"Build",
	"HybridCLRData",
	"Bundles",
	"yoo",
	"HotUpdateAssetsPreUpload",
}

var fileExtensionsToDelete = []string{
	".csproj",
	".sln",
	".txt",
	".user",
	".vsconfig",
}

// Deletes the specified directories in the base path
func deleteDirectories(basePath string) error {
	for _, dir := range directoriesToDelete {
		path := filepath.Join(basePath, dir)
		info, err := os.Stat(path)
		if err != nil {
			if os.IsNotExist(err) {
				continue
			}
			fmt.Printf("Unable to access directory: %s, Error: %s\n", path, err)
			continue
		}
		if info.IsDir() {
			err = os.RemoveAll(path)
			if err != nil {
				fmt.Printf("Unable to delete directory: %s, Error: %s\n", path, err)
			} else {
				fmt.Printf("Deleted directory: %s\n", path)
			}
		}
	}
	return nil
}

// Deletes the specified files in the base path
func deleteFiles(basePath string) error {
	files, err := os.ReadDir(basePath)
	if err != nil {
		return err
	}

	for _, file := range files {
		if !file.IsDir() {
			for _, ext := range fileExtensionsToDelete {
				if filepath.Ext(file.Name()) == ext {
					path := filepath.Join(basePath, file.Name())
					err := os.Remove(path)
					if err != nil {
						fmt.Printf("Unable to delete file: %s, Error: %s\n", path, err)
					} else {
						fmt.Printf("Deleted file: %s\n", path)
					}
					break
				}
			}
		}
	}

	return nil
}

func main() {
	basePath, err := os.Getwd()
	if err != nil {
		fmt.Printf("Unable to get current directory: %s\n", err)
		waitForKeyPress()
		return
	}

	err = deleteDirectories(basePath)
	if err != nil {
		fmt.Printf("Error deleting directories: %s\n", err)
	}

	err = deleteFiles(basePath)
	if err != nil {
		fmt.Printf("Error deleting files: %s\n", err)
	}

	fmt.Println("Operation completed. Press any key to exit...")
	waitForKeyPress()
}

// waitForKeyPress waits for the user to press any key before closing
func waitForKeyPress() {
	fmt.Println("Press any key to continue...")
	bufio.NewReader(os.Stdin).ReadBytes('\n')
}
