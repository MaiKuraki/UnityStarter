/*
该脚本用于递归遍历当前目录并生成目录结构的 Markdown 文件，支持白名单、黑名单与折叠列表。
This script recursively traverses the current directory and generates a Markdown file of the directory tree.
*/

package main

import (
	"bufio"
	"bytes"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"time"
)

// isBlacklisted checks if the path or file name is in the blacklist.
func isBlacklisted(path, name string, blacklist []string) bool {
	absPath, err := filepath.Abs(path)
	if err != nil {
		absPath = path
	}
	absPath = filepath.ToSlash(absPath)
	fullPath := filepath.ToSlash(filepath.Join(absPath, name))

	for _, item := range blacklist {
		item = filepath.ToSlash(item)

		if strings.HasSuffix(item, "/") {
			folder := strings.TrimSuffix(item, "/")
			if strings.HasPrefix(fullPath, folder+"/") ||
				fullPath == folder ||
				strings.HasPrefix(absPath, folder+"/") ||
				absPath == folder {
				return true
			}
			if name == filepath.Base(folder) {
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
		return true
	}
	if !isDir {
		return !isWhitelisted(name, whitelist)
	}
	return shouldCollapseDir(fullPath, blacklist, collapselist, whitelist)
}

// shouldCollapseDir determines if a directory and ALL its contents can be collapsed
func shouldCollapseDir(path string, blacklist, collapselist, whitelist []string) bool {
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
	entries, err := os.ReadDir(path)
	if err != nil {
		return false
	}
	for _, entry := range entries {
		if !canCollapseEntry(path, entry.Name(), entry.IsDir(), blacklist, collapselist, whitelist) {
			return false
		}
	}
	return len(entries) > 0
}

func traverseDir(buf *bytes.Buffer, path string, prefix string, isLastParent bool, blacklist, collapselist, whitelist []string) {
	if shouldCollapseDir(path, blacklist, collapselist, whitelist) {
		connector := "└── "
		if !isLastParent {
			connector = "├── "
		}
		buf.WriteString(fmt.Sprintf("%s%s...\n", prefix, connector))
		return
	}

	entries, err := os.ReadDir(path)
	if err != nil {
		buf.WriteString(fmt.Sprintf("Error reading directory %s: %v\n", path, err))
		return
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
			buf.WriteString(fmt.Sprintf("%s└── ...\n", prefix))
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
		buf.WriteString(fmt.Sprintf("%s%s%s\n", prefix, connector, displayName))
		if entry.IsDir() {
			nextPrefix := prefix + "│   "
			if isLast {
				nextPrefix = prefix + "    "
			}
			traverseDir(buf, fullPath, nextPrefix, isLast, blacklist, collapselist, whitelist)
		}
	}
}

func main() {
	whitelist := []string{".go", ".md", ".cs", "README"}
	blacklist := []string{
		".git/", ".vs/", ".idea/", ".vscode/", ".utmp",
		"node_modules/", "obj/", "Logs/", "Temp/",
		"Library/", "SceneBackups/", "MemoryCaptures/",
		"Build/", "Packages/", "ProjectSettings/",
		"UserSettings/", "*.tmp", "*.log", "temp",
		"./Assets/ThirdParty/InControl/",
	}
	collapselist := []string{"Library/", "Temp/", "Build/"}

	currentDir, err := os.Getwd()
	if err != nil {
		fmt.Printf("Error getting current directory: %v\n", err)
		return
	}

	var buf bytes.Buffer
	buf.WriteString("# Directory Structure\n")
	buf.WriteString("Generation time: " + time.Now().Format("2006-01-02 15:04:05") + "\n\n")
	buf.WriteString("```\n")
	traverseDir(&buf, currentDir, "", false, blacklist, collapselist, whitelist)
	buf.WriteString("```\n")

	outName := os.Getenv("FILE_TREE_OUT")
	if strings.TrimSpace(outName) == "" {
		outName = "directory_structure.md"
	}
	file, err := os.Create(outName)
	if err != nil {
		fmt.Printf("Error creating file: %v\n", err)
		return
	}
	defer file.Close()

	writer := bufio.NewWriter(file)
	if _, err = writer.WriteString(buf.String()); err != nil {
		fmt.Printf("Error writing to file: %v\n", err)
		return
	}
	writer.Flush()

	fmt.Printf("The directory structure has been generated in %s\n", outName)
	waitForKeyPress()
}

// waitForKeyPress waits for the user to press any key before closing
func waitForKeyPress() {
	fmt.Println("Press any key to continue...")
	bufio.NewReader(os.Stdin).ReadBytes('\n')
}
