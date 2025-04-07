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
func isBlacklisted(path, name string, blacklist []string) bool {
	for _, item := range blacklist {
		if strings.HasSuffix(item, "/") {
			folder := strings.TrimSuffix(item, "/")
			if strings.HasPrefix(path, folder) || name == folder {
				return true
			}
		} else if strings.HasPrefix(item, "*.") {
			ext := strings.TrimPrefix(item, "*.")
			if strings.HasSuffix(name, "."+ext) {
				return true
			}
		} else if item == name {
			return true
		}
	}
	return false
}

// isCollapsed checks if the path or file name is in the collapsible list.
func isCollapsed(path, name string, collapselist []string) bool {
	for _, item := range collapselist {
		if strings.HasSuffix(item, "/") {
			folder := strings.TrimSuffix(item, "/")
			if strings.HasPrefix(path, folder) || name == folder {
				return true
			}
		} else if item == name {
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

// traverseDir recursively traverses the directory and generates a Markdown-formatted tree structure.
func traverseDir(path string, prefix string, isLastParent bool, blacklist, collapselist, whitelist []string) string {
	var markdown string
	entries, err := os.ReadDir(path)
	if err != nil {
		return fmt.Sprintf("Error reading directory %s: %v\n", path, err)
	}

	// Filter entries and track non-whitelisted files.
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

	// If all files in the folder are ignored, display it in a collapsed manner.
	if len(filteredEntries) == 0 && len(entries) > 0 {
		connector := "└── "
		if !isLastParent {
			connector = "├── "
		}
		markdown += fmt.Sprintf("%s%s...\n", prefix, connector)
		return markdown
	}

	// Process the filtered entries.
	for i, entry := range filteredEntries {
		fullPath := filepath.Join(path, entry.Name())
		isLast := i == len(filteredEntries)-1

		// Determine the connector symbol.
		var connector string
		if isLast {
			connector = "└── "
		} else {
			connector = "├── "
		}

		// Handle empty folders, keep the trailing /.
		entryName := entry.Name()
		if entry.IsDir() && len(entryName) > 0 && !strings.HasSuffix(entryName, "/") {
			entriesInDir, err := os.ReadDir(fullPath)
			if err == nil && len(entriesInDir) == 0 {
				entryName += "/"
			}
		}

		markdown += fmt.Sprintf("%s%s%s\n", prefix, connector, entryName)

		if entry.IsDir() {
			var nextPrefix string
			if isLast {
				nextPrefix = prefix + "    "
			} else {
				nextPrefix = prefix + "│   "
			}

			if isCollapsed(fullPath, entry.Name(), collapselist) {
				markdown += fmt.Sprintf("%s└── ...\n", nextPrefix)
			} else {
				markdown += traverseDir(fullPath, nextPrefix, isLast, blacklist, collapselist, whitelist)
			}
		}
	}

	// Add ... for non-whitelisted files at the root level.
	if prefix == "" && hasNonWhitelisted {
		markdown += fmt.Sprintf("%s└── ...\n", prefix)
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
}
