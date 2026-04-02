package main

import (
	"bufio"
	"encoding/json"
	"flag"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"strconv"
	"strings"
	"sync"
	"syscall"
	"time"
)

// ============================================================
// Configuration
// ============================================================

// Directories to delete (relative to project root, top-level only)
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

// File extensions to delete (top-level files only)
var fileExtensionsToDelete = []string{
	".csproj",
	".sln",
	".slnx",
	".user",
	".vsconfig",
}

// Global stdin reader to avoid multiple buffered readers competing for stdin
var stdinReader *bufio.Reader

func init() {
	stdinReader = bufio.NewReader(os.Stdin)
}

// ============================================================
// Types
// ============================================================

// EditorInstance represents the structure of Library/EditorInstance.json
type EditorInstance struct {
	ProcessID int `json:"process_id"`
}

// deleteResult tracks the outcome of a single delete operation
type deleteResult struct {
	path    string
	kind    string // "directory" or "file"
	size    int64  // size in bytes (0 if unknown)
	err     error
}

// ============================================================
// Unity Project Validation
// ============================================================

// isUnityProject checks if the given path contains a Unity project structure
func isUnityProject(basePath string) bool {
	markers := []string{"Assets", "ProjectSettings"}
	for _, marker := range markers {
		info, err := os.Stat(filepath.Join(basePath, marker))
		if err != nil || !info.IsDir() {
			return false
		}
	}
	return true
}

// ============================================================
// Unity Running Detection
// ============================================================

// checkUnityRunning checks if Unity Editor is running for this project.
// Uses EditorInstance.json (created by Unity when opening a project)
// and verifies the process is actually alive.
func checkUnityRunning(basePath string) (bool, int) {
	editorInstancePath := filepath.Join(basePath, "Library", "EditorInstance.json")
	data, err := os.ReadFile(editorInstancePath)
	if err != nil {
		return false, 0
	}

	var instance EditorInstance
	if err := json.Unmarshal(data, &instance); err != nil {
		return false, 0
	}

	if instance.ProcessID <= 0 {
		return false, 0
	}

	if isProcessRunning(instance.ProcessID) {
		return true, instance.ProcessID
	}

	return false, 0
}

// isProcessRunning checks if a process with the given PID is alive.
func isProcessRunning(pid int) bool {
	if runtime.GOOS == "windows" {
		return isProcessRunningWindows(pid)
	}
	return isProcessRunningUnix(pid)
}

// isProcessRunningWindows uses tasklist to check if a PID exists.
func isProcessRunningWindows(pid int) bool {
	cmd := exec.Command("tasklist", "/FI", fmt.Sprintf("PID eq %d", pid), "/NH", "/FO", "CSV")
	output, err := cmd.Output()
	if err != nil {
		return false
	}
	// tasklist output contains the PID as a quoted string if the process exists
	return strings.Contains(string(output), strconv.Itoa(pid))
}

// isProcessRunningUnix uses kill -0 to check if a PID exists.
func isProcessRunningUnix(pid int) bool {
	process, err := os.FindProcess(pid)
	if err != nil {
		return false
	}
	// On Unix, sending signal 0 checks if the process exists without affecting it.
	// On Windows this branch is never reached (guarded by isProcessRunning).
	err = process.Signal(syscall.Signal(0))
	return err == nil
}

// ============================================================
// Size Calculation
// ============================================================

// getDirSize calculates the total size of a directory
func getDirSize(path string) int64 {
	var size int64
	filepath.Walk(path, func(_ string, info os.FileInfo, err error) error {
		if err != nil {
			return nil
		}
		if !info.IsDir() {
			size += info.Size()
		}
		return nil
	})
	return size
}

// formatSize formats bytes into human-readable string
func formatSize(bytes int64) string {
	const (
		KB = 1024
		MB = KB * 1024
		GB = MB * 1024
	)
	switch {
	case bytes >= GB:
		return fmt.Sprintf("%.2f GB", float64(bytes)/float64(GB))
	case bytes >= MB:
		return fmt.Sprintf("%.2f MB", float64(bytes)/float64(MB))
	case bytes >= KB:
		return fmt.Sprintf("%.2f KB", float64(bytes)/float64(KB))
	default:
		return fmt.Sprintf("%d B", bytes)
	}
}

// ============================================================
// Preview / Dry-Run
// ============================================================

// previewItem represents a file or directory to be deleted
type previewItem struct {
	path string
	kind string // "directory" or "file"
	size int64
}

// collectPreview scans for all items that will be deleted and their sizes
func collectPreview(basePath string) []previewItem {
	var items []previewItem

	// Directories
	for _, dir := range directoriesToDelete {
		path := filepath.Join(basePath, dir)
		info, err := os.Stat(path)
		if err != nil || !info.IsDir() {
			continue
		}
		size := getDirSize(path)
		items = append(items, previewItem{path: dir, kind: "directory", size: size})
	}

	// Files
	extSet := make(map[string]struct{}, len(fileExtensionsToDelete))
	for _, ext := range fileExtensionsToDelete {
		extSet[ext] = struct{}{}
	}

	entries, err := os.ReadDir(basePath)
	if err == nil {
		for _, entry := range entries {
			if entry.IsDir() {
				continue
			}
			ext := filepath.Ext(entry.Name())
			if _, ok := extSet[ext]; !ok {
				continue
			}
			info, err := entry.Info()
			if err != nil {
				continue
			}
			items = append(items, previewItem{path: entry.Name(), kind: "file", size: info.Size()})
		}
	}

	return items
}

// printPreview displays all items that will be deleted
func printPreview(items []previewItem) {
	if len(items) == 0 {
		fmt.Println("\nNothing to clean. Project is already clean.")
		return
	}

	var totalSize int64
	var dirCount, fileCount int

	fmt.Println("\n=============================================")
	fmt.Println("  ITEMS TO DELETE")
	fmt.Println("=============================================")

	fmt.Println("\nDirectories:")
	for _, item := range items {
		if item.kind == "directory" {
			fmt.Printf("  [DIR]  %-30s  %s\n", item.path+"/", formatSize(item.size))
			totalSize += item.size
			dirCount++
		}
	}
	if dirCount == 0 {
		fmt.Println("  (none)")
	}

	fmt.Println("\nFiles:")
	for _, item := range items {
		if item.kind == "file" {
			fmt.Printf("  [FILE] %-30s  %s\n", item.path, formatSize(item.size))
			totalSize += item.size
			fileCount++
		}
	}
	if fileCount == 0 {
		fmt.Println("  (none)")
	}

	fmt.Printf("\nTotal: %d directories, %d files, %s\n", dirCount, fileCount, formatSize(totalSize))
}

// ============================================================
// Delete Operations
// ============================================================

// tryDelete attempts to delete a path with retries.
// For permission errors, walks the tree to remove read-only attributes on all files.
func tryDelete(path string) error {
	var lastErr error
	for attempt := 0; attempt < 3; attempt++ {
		lastErr = os.RemoveAll(path)
		if lastErr == nil {
			return nil
		}

		// On permission error, walk the entire tree and chmod all entries
		if os.IsPermission(lastErr) {
			filepath.Walk(path, func(p string, info os.FileInfo, err error) error {
				if err != nil {
					return nil
				}
				os.Chmod(p, 0777)
				return nil
			})
		}

		// Brief wait for transient file locks (e.g., antivirus, indexer)
		time.Sleep(100 * time.Millisecond)
	}
	return lastErr
}

// deleteItems concurrently deletes directories and files, returning results
// via a channel. Output is collected and printed in order after completion.
func deleteItems(basePath string, items []previewItem) (deleted int, failed int, freedBytes int64) {
	if len(items) == 0 {
		return 0, 0, 0
	}

	workerCount := runtime.NumCPU() * 2
	if workerCount < 4 {
		workerCount = 4
	}
	if workerCount > len(items) {
		workerCount = len(items)
	}

	type job struct {
		item previewItem
	}

	jobs := make(chan job, len(items))
	results := make(chan deleteResult, len(items))
	var wg sync.WaitGroup

	// Start workers
	for i := 0; i < workerCount; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			for j := range jobs {
				fullPath := filepath.Join(basePath, j.item.path)
				err := tryDelete(fullPath)
				results <- deleteResult{
					path: j.item.path,
					kind: j.item.kind,
					size: j.item.size,
					err:  err,
				}
			}
		}()
	}

	// Enqueue all jobs
	for _, item := range items {
		jobs <- job{item: item}
	}
	close(jobs)

	// Wait for all workers, then close results
	go func() {
		wg.Wait()
		close(results)
	}()

	// Collect and print results in arrival order (safe — only main goroutine prints)
	var deletedCount, failedCount int
	var totalFreed int64
	for r := range results {
		if r.err != nil {
			fmt.Printf("[FAIL] %s: %v\n", r.path, r.err)
			failedCount++
		} else {
			fmt.Printf("[OK]   Deleted %s: %s (%s)\n", r.kind, r.path, formatSize(r.size))
			deletedCount++
			totalFreed += r.size
		}
	}

	return deletedCount, failedCount, totalFreed
}

// ============================================================
// Utilities
// ============================================================

func waitForKeyPress() {
	fmt.Println("\nPress Enter to continue...")
	stdinReader.ReadBytes('\n')
}

// ============================================================
// Entry Point
// ============================================================

func main() {
	var ciMode bool
	var dryRun bool

	flag.BoolVar(&ciMode, "ci", false, "Run in CI mode (non-interactive, no confirmation prompts)")
	flag.BoolVar(&dryRun, "dry-run", false, "Preview what would be deleted without actually deleting")
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
	if dryRun {
		fmt.Println("[Dry Run] Preview mode — no files will be deleted")
	}

	// Validate this is a Unity project
	if !isUnityProject(basePath) {
		fmt.Println("\n[ERROR] Current directory does not appear to be a Unity project.")
		fmt.Println("Expected 'Assets/' and 'ProjectSettings/' directories.")
		fmt.Println("Please run this tool from the Unity project root directory.")
		if !ciMode {
			waitForKeyPress()
		}
		os.Exit(1)
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
		fmt.Println("\nPress Enter to FORCE continue (not recommended), or Ctrl+C to cancel...")
		stdinReader.ReadBytes('\n')
	}

	// Collect and preview items
	fmt.Println("\nScanning project...")
	items := collectPreview(basePath)
	printPreview(items)

	if len(items) == 0 {
		if !ciMode {
			waitForKeyPress()
		}
		return
	}

	// Dry-run stops here
	if dryRun {
		fmt.Println("\n[Dry Run] No files were deleted.")
		if !ciMode {
			waitForKeyPress()
		}
		return
	}

	// Confirm before deletion
	if !ciMode {
		fmt.Print("\nProceed with deletion? (y/N): ")
		confirm, _ := stdinReader.ReadString('\n')
		confirm = strings.TrimSpace(strings.ToLower(confirm))
		if confirm != "y" {
			fmt.Println("Operation cancelled.")
			waitForKeyPress()
			return
		}
	}

	// Execute deletion
	fmt.Println("\nDeleting...")
	startTime := time.Now()

	deletedCount, failedCount, freedBytes := deleteItems(basePath, items)

	duration := time.Since(startTime)

	// Summary
	fmt.Println("\n===========================================")
	fmt.Println("  CLEAN COMPLETE")
	fmt.Println("===========================================")
	fmt.Printf("  Deleted: %d items\n", deletedCount)
	if failedCount > 0 {
		fmt.Printf("  Failed:  %d items\n", failedCount)
	}
	fmt.Printf("  Freed:   %s\n", formatSize(freedBytes))
	fmt.Printf("  Time:    %s\n", duration)

	if !ciMode {
		waitForKeyPress()
	}
}


