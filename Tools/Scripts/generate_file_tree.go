/*
该脚本的主要功能是递归遍历当前目录，生成一个包含目录结构的Markdown文件。
它支持配置白名单、黑名单和折叠列表，以过滤和处理特定的文件和文件夹。
生成的Markdown文件包含目录结构的树形表示，并记录生成时间。

This script is mainly used to recursively traverse the current directory and generate a Markdown file containing the directory structure.
It supports configuring whitelists, blacklists, and collapsible lists to filter and handle specific files and folders.
The generated Markdown file includes a tree representation of the directory structure and records the generation time.
*/

package main

import (
	"bufio"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"time"
)

// isBlacklisted checks if the path or file name is in the blacklist.
// isBlacklisted checks if the path or file name is in the blacklist.
func isBlacklisted(path, name string, blacklist []string) bool {
	// First convert path to absolute and normalize separators
	absPath, err := filepath.Abs(path)
	if err != nil {
		absPath = path // Fallback to original path if conversion fails
	}
	absPath = filepath.ToSlash(absPath) // Standardize to forward slashes

	fullPath := filepath.ToSlash(filepath.Join(absPath, name))

	for _, item := range blacklist {
		item = filepath.ToSlash(item)

		if strings.HasSuffix(item, "/") {
			// Process directory blacklist items
			folder := strings.TrimSuffix(item, "/")

			// Check for full path or subpath matches
			if strings.HasPrefix(fullPath, folder+"/") ||
				fullPath == folder ||
				strings.HasPrefix(absPath, folder+"/") ||
				absPath == folder {
				return true
			}

			// Check for current directory name match
			if name == filepath.Base(folder) {
				return true
			}
		} else if strings.HasPrefix(item, "*.") {
			// Process extension blacklist
			ext := strings.TrimPrefix(item, "*.")
			if strings.HasSuffix(name, "."+ext) {
				return true
			}
		} else if item == name {
			// Process exact filename match
			return true
		}
	}
	return false
}

// isWhitelisted checks if the file name is in the whitelist.
func isWhitelisted(name string, whitelist []string) bool {
	for _, item := range whitelist {
		if strings.HasPrefix(item, ".") {
			ext := strings.TrimPrefix(item, ".")
			if strings.HasSuffix(name, "."+ext) {
				return true
			}
		} else if item == name {
			return true
		}
	}
	return false
}

// canCollapseEntry checks if a single entry (file/dir) can be collapsed
func canCollapseEntry(path, name string, isDir bool, blacklist, collapselist, whitelist []string) bool {
	fullPath := filepath.Join(path, name)

	if isBlacklisted(fullPath, name, blacklist) {
		return true // Blacklisted items are always treated as collapsible
	}

	if !isDir {
		// Files are collapsible if they're not whitelisted
		return !isWhitelisted(name, whitelist)
	}

	// For directories, check if marked for collapsing or if all contents are collapsible
	return shouldCollapseDir(fullPath, blacklist, collapselist, whitelist)
}

// shouldCollapseDir determines if a directory and ALL its contents can be collapsed
func shouldCollapseDir(path string, blacklist, collapselist, whitelist []string) bool {
	// First check if directory itself is in the collapsible list
	for _, item := range collapselist {
		item = filepath.ToSlash(item)
		pathSlash := filepath.ToSlash(path)

		if strings.HasSuffix(item, "/") {
			folder := strings.TrimSuffix(item, "/")
			if strings.HasPrefix(pathSlash, folder+"/") || pathSlash == folder {
				return true
			}
		} else if filepath.Base(path) == item {
			return true
		}
	}

	// Then check all contents
	entries, err := os.ReadDir(path)
	if err != nil {
		return false
	}

	for _, entry := range entries {
		if !canCollapseEntry(path, entry.Name(), entry.IsDir(), blacklist, collapselist, whitelist) {
			return false
		}
	}

	return len(entries) > 0 // Empty folders won't be collapsed
}

func traverseDir(path string, prefix string, isLastParent bool, blacklist, collapselist, whitelist []string) string {
	if shouldCollapseDir(path, blacklist, collapselist, whitelist) {
		connector := "└── "
		if !isLastParent {
			connector = "├── "
		}
		return fmt.Sprintf("%s%s...\n", prefix, connector)
	}

	var markdown string
	entries, err := os.ReadDir(path)
	if err != nil {
		return fmt.Sprintf("Error reading directory %s: %v\n", path, err)
	}

	var filteredEntries []os.DirEntry
	var hasNonWhitelisted bool
	for _, entry := range entries {
		fullPath := filepath.Join(path, entry.Name())
		if isBlacklisted(fullPath, entry.Name(), blacklist) {
			continue
		}
		if entry.IsDir() || isWhitelisted(entry.Name(), whitelist) {
			filteredEntries = append(filteredEntries, entry)
		} else {
			hasNonWhitelisted = true
		}
	}

	if hasNonWhitelisted && len(filteredEntries) > 0 {
		filteredEntries = append(filteredEntries, nil)
	}

	for i, entry := range filteredEntries {
		isLast := i == len(filteredEntries)-1
		if entry == nil {
			markdown += fmt.Sprintf("%s└── ...\n", prefix)
			continue
		}

		fullPath := filepath.Join(path, entry.Name())

		connector := "├── "
		if isLast {
			connector = "└── "
		}

		displayName := entry.Name()
		if entry.IsDir() {
			if dirEntries, _ := os.ReadDir(fullPath); len(dirEntries) == 0 {
				displayName += "/"
			}
		}

		markdown += fmt.Sprintf("%s%s%s\n", prefix, connector, displayName)

		if entry.IsDir() {
			nextPrefix := prefix + "│   "
			if isLast {
				nextPrefix = prefix + "    "
			}
			markdown += traverseDir(fullPath, nextPrefix, isLast, blacklist, collapselist, whitelist)
		}
	}

	return markdown
}

func main() {
	// Configuration lists
	whitelist := []string{".go", ".md", ".cs", "README"} // Whitelisted extensions and file names
	blacklist := []string{
		".git/", ".vs/", ".idea/", ".vscode/", ".utmp",
		"node_modules/", "obj/", "Logs/", "Temp/",
		"Library/", "SceneBackups/", "MemoryCaptures/",
		"Build/", "Packages/", "ProjectSettings/",
		"UserSettings/", "*.tmp", "*.log", "temp",
		"./Assets/ThirdParty/InControl/",
	}
	collapselist := []string{"Library/", "Temp/", "Build/"}

	// Get the current directory
	currentDir, err := os.Getwd()
	if err != nil {
		fmt.Printf("Error getting current directory: %v\n", err)
		return
	}

	// Generate Markdown
	markdown := "# Directory Structure\n"
	markdown += "Generation time: " + time.Now().Format("2006-01-02 15:04:05") + "\n\n"
	markdown += "```\n"
	markdown += traverseDir(currentDir, "", false, blacklist, collapselist, whitelist)
	markdown += "```\n"

	// Write to file
	file, err := os.Create("directory_structure.md")
	if err != nil {
		fmt.Printf("Error creating file: %v\n", err)
		return
	}
	defer file.Close()

	writer := bufio.NewWriter(file)
	_, err = writer.WriteString(markdown)
	if err != nil {
		fmt.Printf("Error writing to file: %v\n", err)
		return
	}
	writer.Flush()

	fmt.Println("The directory structure has been generated in directory_structure.md")
	waitForKeyPress()
}

// waitForKeyPress waits for the user to press any key before closing
func waitForKeyPress() {
	fmt.Println("Press any key to continue...")
	bufio.NewReader(os.Stdin).ReadBytes('\n')
}
