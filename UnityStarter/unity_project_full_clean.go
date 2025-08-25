package main

import (
	"bufio"
	"fmt"
	"os"
	"path/filepath"
	"runtime"
	"sync"
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

// Deletes the specified directories in the base path using concurrent workers
func deleteDirectories(basePath string) error {
	workerCount := runtime.NumCPU()
	if workerCount < 2 {
		workerCount = 2
	}
	jobs := make(chan string, len(directoriesToDelete))
	var wg sync.WaitGroup

	// workers
	for i := 0; i < workerCount; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			for dir := range jobs {
				path := filepath.Join(basePath, dir)
				// os.RemoveAll returns nil if the path does not exist; no need to Stat first
				if err := os.RemoveAll(path); err != nil {
					fmt.Printf("Unable to delete directory: %s, Error: %s\n", path, err)
				} else {
					fmt.Printf("Deleted directory: %s\n", path)
				}
			}
		}()
	}

	// enqueue
	for _, dir := range directoriesToDelete {
		jobs <- dir
	}
	close(jobs)
	wg.Wait()
	return nil
}

// Deletes the specified files in the base path (top-level only) using concurrent workers
func deleteFiles(basePath string) error {
	entries, err := os.ReadDir(basePath)
	if err != nil {
		return err
	}

	extSet := make(map[string]struct{}, len(fileExtensionsToDelete))
	for _, ext := range fileExtensionsToDelete {
		extSet[ext] = struct{}{}
	}

	// collect candidate files first
	var candidates []string
	for _, entry := range entries {
		if entry.IsDir() {
			continue
		}
		ext := filepath.Ext(entry.Name())
		if _, ok := extSet[ext]; ok {
			candidates = append(candidates, filepath.Join(basePath, entry.Name()))
		}
	}

	if len(candidates) == 0 {
		return nil
	}

	workerCount := runtime.NumCPU()
	if workerCount < 2 {
		workerCount = 2
	}
	if workerCount > len(candidates) {
		workerCount = len(candidates)
	}

	jobs := make(chan string, len(candidates))
	var wg sync.WaitGroup

	for i := 0; i < workerCount; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			for path := range jobs {
				if err := os.Remove(path); err != nil {
					fmt.Printf("Unable to delete file: %s, Error: %s\n", path, err)
				} else {
					fmt.Printf("Deleted file: %s\n", path)
				}
			}
		}()
	}

	for _, p := range candidates {
		jobs <- p
	}
	close(jobs)
	wg.Wait()
	return nil
}

func main() {
	basePath, err := os.Getwd()
	if err != nil {
		fmt.Printf("Unable to get current directory: %s\n", err)
		waitForKeyPress()
		return
	}

	if err = deleteDirectories(basePath); err != nil {
		fmt.Printf("Error deleting directories: %s\n", err)
	}

	if err = deleteFiles(basePath); err != nil {
		fmt.Printf("Error deleting files: %s\n", err)
	}

	fmt.Println("Operation completed. Press any key to exit...")
	waitForKeyPress()
}

// waitForKeyPress waits for the user to press any key before closing
func waitForKeyPress() {
	fmt.Println("Press Enter to continue...")
	bufio.NewReader(os.Stdin).ReadBytes('\n')
}
