package main

import (
	"bufio"
	"encoding/json"
	"flag"
	"fmt"
	"os"
	"path/filepath"
	"runtime"
	"sync"
	"time"
)

// Command-line flags
var ciMode bool

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
	".slnx",
	".txt",
	".user",
	".vsconfig",
}

// EditorInstance represents the structure of Library/EditorInstance.json
type EditorInstance struct {
	ProcessID int `json:"process_id"`
}

// checkUnityRunning checks if Unity Editor is running for this project
func checkUnityRunning(basePath string) (bool, int) {
	editorInstancePath := filepath.Join(basePath, "Library", "EditorInstance.json")
	data, err := os.ReadFile(editorInstancePath)
	if err != nil {
		// File doesn't exist or can't be read, assume Unity is not running for this project
		return false, 0
	}

	var instance EditorInstance
	if err := json.Unmarshal(data, &instance); err != nil {
		return false, 0
	}

	// Check if process is actually running
	process, err := os.FindProcess(instance.ProcessID)
	if err != nil {
		return false, 0
	}

	// On Windows, FindProcess always succeeds, so we need to send a signal 0 to check existence.
	// However, Go's os.Process.Signal is not fully supported on Windows for existence check in the same way as Unix.
	// But for a simple check, if we can't signal it, it might not be there.
	// A more robust cross-platform way without cgo is tricky, but for this tool's purpose:
	// If the file exists, Unity *might* be running.
	// We can try to send a signal 0 on Unix. On Windows, we'll trust the file existence + simple check.

	// Simple heuristic: if the file exists, warn the user.
	// The file is supposed to be deleted by Unity on close, but if it crashed it might remain.
	// Let's try to be slightly smarter:

	if runtime.GOOS != "windows" {
		if err := process.Signal(os.Signal(nil)); err != nil {
			// Process not found
			return false, 0
		}
	} else {
		// Windows specific check could be added here, but for now we'll assume if the file is there, check PID
		// If we can't verify PID easily without syscalls, we just warn based on file presence.
		// Actually, let's just return true if we found the ID, and let the user decide.
		// Refinement: We can try to see if we can get process info, but standard lib is limited.
	}

	return true, instance.ProcessID
}

// tryDelete attempts to delete a path with retries and permission handling
func tryDelete(path string) error {
	var err error
	for i := 0; i < 3; i++ {
		err = os.RemoveAll(path)
		if err == nil {
			return nil
		}

		// If permission denied, try to remove read-only attribute
		if os.IsPermission(err) {
			_ = os.Chmod(path, 0777)
		}

		// Wait a bit before retrying (handling transient locks)
		time.Sleep(50 * time.Millisecond)
	}
	return err
}

// Deletes the specified directories in the base path using concurrent workers
func deleteDirectories(basePath string) {
	// Use more workers for I/O bound tasks
	workerCount := runtime.NumCPU() * 2
	if workerCount < 4 {
		workerCount = 4
	}

	jobs := make(chan string, len(directoriesToDelete))
	var wg sync.WaitGroup

	// Start workers
	for i := 0; i < workerCount; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			for dir := range jobs {
				path := filepath.Join(basePath, dir)
				// Check if exists first to avoid unnecessary work/logs, though RemoveAll handles it
				if _, err := os.Stat(path); os.IsNotExist(err) {
					continue
				}

				if err := tryDelete(path); err != nil {
					fmt.Printf("[Error] Failed to delete directory: %s, Error: %s\n", path, err)
				} else {
					fmt.Printf("[Deleted] Directory: %s\n", path)
				}
			}
		}()
	}

	// Enqueue jobs
	for _, dir := range directoriesToDelete {
		jobs <- dir
	}
	close(jobs)
	wg.Wait()
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

	// Collect candidate files
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

	workerCount := runtime.NumCPU() * 2
	if workerCount < 4 {
		workerCount = 4
	}
	if workerCount > len(candidates) {
		workerCount = len(candidates)
	}

	jobs := make(chan string, len(candidates))
	var wg sync.WaitGroup

	// Start workers
	for i := 0; i < workerCount; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			for path := range jobs {
				if err := tryDelete(path); err != nil {
					fmt.Printf("[Error] Failed to delete file: %s, Error: %s\n", path, err)
				} else {
					fmt.Printf("[Deleted] File: %s\n", path)
				}
			}
		}()
	}

	// Enqueue jobs
	for _, p := range candidates {
		jobs <- p
	}
	close(jobs)
	wg.Wait()
	return nil
}

func main() {
	// Parse command-line flags
	flag.BoolVar(&ciMode, "ci", false, "Run in CI mode (non-interactive, no confirmation prompts)")
	flag.Parse()

	basePath, err := os.Getwd()
	if err != nil {
		fmt.Printf("Unable to get current directory: %s\n", err)
		if !ciMode {
			waitForKeyPress()
		}
		os.Exit(1)
	}

	fmt.Printf("Target Directory: %s\n", basePath)
	if ciMode {
		fmt.Println("[CI Mode] Running in non-interactive mode")
	}

	// Check if Unity is running
	if isRunning, pid := checkUnityRunning(basePath); isRunning {
		fmt.Printf("\n[WARNING] Unity Editor appears to be running (PID: %d).\n", pid)
		fmt.Println("Cleaning while Unity is open WILL cause errors and file locks.")
		fmt.Println("Please close Unity and try again.")
		if ciMode {
			fmt.Println("\n[CI Mode] Aborting due to Unity running. Exit code: 1")
			os.Exit(1)
		}
		fmt.Println("\nPress 'Enter' to FORCE continue (not recommended), or 'Ctrl+C' to cancel...")
		bufio.NewReader(os.Stdin).ReadBytes('\n')
	} else {
		fmt.Println("This tool will delete the following if they exist:")
		fmt.Println("Directories:", directoriesToDelete)
		fmt.Println("Files with extensions:", fileExtensionsToDelete)
		if !ciMode {
			fmt.Println("\nPress 'Enter' to confirm and start cleaning, or 'Ctrl+C' to cancel...")
			bufio.NewReader(os.Stdin).ReadBytes('\n')
		}
	}

	startTime := time.Now()

	deleteDirectories(basePath)

	if err = deleteFiles(basePath); err != nil {
		fmt.Printf("Error deleting files: %s\n", err)
	}

	duration := time.Since(startTime)
	if ciMode {
		fmt.Printf("\nOperation completed in %s.\n", duration)
	} else {
		fmt.Printf("\nOperation completed in %s. Press any key to exit...\n", duration)
		waitForKeyPress()
	}
}

// waitForKeyPress waits for the user to press any key before closing
func waitForKeyPress() {
	bufio.NewReader(os.Stdin).ReadBytes('\n')
}
