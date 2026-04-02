// Generate File Tree — Recursively generates a Markdown directory structure.
// Supports multiple profiles for different detail levels, depth limits, file extension
// filters, and .treeignore files for project-specific exclusions.
//
// Build: go build generate_file_tree.go
//
// Interactive: Run with -i for profile selection menu.
// CLI:         generate_file_tree -profile standard -depth 5 -o tree.md

package main

import (
	"bufio"
	"bytes"
	"flag"
	"fmt"
	"os"
	"path/filepath"
	"sort"
	"strconv"
	"strings"
	"time"
)

// ============================================================
// Configuration & Types
// ============================================================

type config struct {
	targetDir  string
	outputFile string
	maxDepth   int  // 0 = unlimited
	dirsOnly   bool
	showSize   bool
	showCount  bool
	ciMode     bool
	extensions map[string]bool // nil = accept all files
	exactNames map[string]bool // exact filename matches (README, LICENSE, etc.)
	ignoreDirs  map[string]bool
	ignoreExts  map[string]bool
	ignoreNames map[string]bool
}

type stats struct {
	dirs      int
	files     int
	totalSize int64
}

type profile struct {
	name        string
	description string
	extensions  []string
	exactNames  []string
	dirsOnly    bool
	maxDepth    int
}

// ============================================================
// Profiles
// ============================================================

var profiles = []profile{
	{
		name:        "minimal",
		description: "Folders only, max depth 3 — quick overview",
		dirsOnly:    true,
		maxDepth:    3,
	},
	{
		name:        "standard",
		description: "Code & doc files, unlimited depth",
		extensions: []string{
			".go", ".cs", ".md", ".json", ".yaml", ".yml", ".xml",
			".asmdef", ".asmref", ".shader", ".hlsl", ".cginc", ".compute",
		},
		exactNames: []string{"README", "LICENSE", "Makefile", "Dockerfile", ".gitignore", ".editorconfig"},
	},
	{
		name:        "detailed",
		description: "Code, config, and Unity asset types",
		extensions: []string{
			".go", ".cs", ".md", ".json", ".yaml", ".yml", ".xml",
			".txt", ".cfg", ".ini", ".toml",
			".asmdef", ".asmref", ".shader", ".hlsl", ".cginc", ".compute",
			".asset", ".prefab", ".unity", ".mat", ".controller",
			".overrideController", ".physicMaterial", ".fontsettings",
			".guiskin", ".preset", ".signal", ".playable",
			".renderTexture", ".lighting",
		},
		exactNames: []string{"README", "LICENSE", "Makefile", "Dockerfile", ".gitignore", ".editorconfig"},
	},
	{
		name:        "full",
		description: "All files, no filtering",
	},
}

// ============================================================
// Default Ignore Lists
// ============================================================

var defaultIgnoreDirs = []string{
	".git", ".vs", ".idea", ".vscode", ".utmp",
	"node_modules", "obj", "Logs", "Temp",
	"Library", "SceneBackups", "MemoryCaptures",
	"UserSettings", "Packages",
}

var defaultIgnoreExts = []string{
	".tmp", ".log", ".bak", ".swp", ".swo", ".meta",
}

var defaultIgnoreNames = []string{
	".DS_Store", "Thumbs.db", "desktop.ini",
}

// Global stdin reader
var stdinReader *bufio.Reader

func init() {
	stdinReader = bufio.NewReader(os.Stdin)
}

// ============================================================
// .treeignore File
// ============================================================

// loadTreeIgnore reads a .treeignore file from the target directory.
// Format: one entry per line.
//
//	name/   → ignore directory by name
//	*.ext   → ignore file extension
//	name    → ignore exact file or directory name
//	# ...   → comment
func loadTreeIgnore(dir string) (dirs, exts, names []string) {
	f, err := os.Open(filepath.Join(dir, ".treeignore"))
	if err != nil {
		return
	}
	defer f.Close()

	scanner := bufio.NewScanner(f)
	for scanner.Scan() {
		line := strings.TrimSpace(scanner.Text())
		if line == "" || strings.HasPrefix(line, "#") {
			continue
		}
		if strings.HasPrefix(line, "*.") {
			exts = append(exts, strings.ToLower("."+strings.TrimPrefix(line, "*.")))
		} else if strings.HasSuffix(line, "/") {
			dirs = append(dirs, strings.TrimSuffix(line, "/"))
		} else {
			names = append(names, line)
		}
	}
	return
}

// ============================================================
// Filtering
// ============================================================

func (c *config) isIgnored(name string, isDir bool) bool {
	if isDir {
		return c.ignoreDirs[name]
	}
	if c.ignoreNames[name] {
		return true
	}
	ext := strings.ToLower(filepath.Ext(name))
	return ext != "" && c.ignoreExts[ext]
}

func (c *config) matchesFilter(name string) bool {
	if c.dirsOnly {
		return false
	}
	// No filter = accept all (full profile)
	if c.extensions == nil && c.exactNames == nil {
		return true
	}
	if c.exactNames != nil && c.exactNames[name] {
		return true
	}
	ext := strings.ToLower(filepath.Ext(name))
	return ext != "" && c.extensions != nil && c.extensions[ext]
}

// ============================================================
// Tree Traversal
// ============================================================

func traverseDir(cfg *config, buf *bytes.Buffer, st *stats, dirPath, prefix string, depth int) {
	entries, err := os.ReadDir(dirPath)
	if err != nil {
		return
	}

	var visibleDirs, visibleFiles []os.DirEntry
	filteredCount := 0

	for _, entry := range entries {
		name := entry.Name()

		// Completely ignored — invisible, no "..." indicator
		if cfg.isIgnored(name, entry.IsDir()) {
			continue
		}

		if entry.IsDir() {
			if cfg.maxDepth > 0 && depth >= cfg.maxDepth {
				filteredCount++
				continue
			}
			visibleDirs = append(visibleDirs, entry)
		} else if cfg.dirsOnly {
			// Files hidden in dirs-only mode (expected, no "...")
			continue
		} else if cfg.matchesFilter(name) {
			visibleFiles = append(visibleFiles, entry)
		} else {
			filteredCount++
		}
	}

	// Sort: case-insensitive alphabetical
	sort.Slice(visibleDirs, func(i, j int) bool {
		return strings.ToLower(visibleDirs[i].Name()) < strings.ToLower(visibleDirs[j].Name())
	})
	sort.Slice(visibleFiles, func(i, j int) bool {
		return strings.ToLower(visibleFiles[i].Name()) < strings.ToLower(visibleFiles[j].Name())
	})

	// Combine: directories first, then files
	visible := make([]os.DirEntry, 0, len(visibleDirs)+len(visibleFiles))
	visible = append(visible, visibleDirs...)
	visible = append(visible, visibleFiles...)

	hasEllipsis := filteredCount > 0

	// Nothing visible at all
	if len(visible) == 0 {
		if hasEllipsis {
			if cfg.showCount {
				buf.WriteString(fmt.Sprintf("%s└── ... (%d items)\n", prefix, filteredCount))
			} else {
				buf.WriteString(fmt.Sprintf("%s└── ...\n", prefix))
			}
		}
		return
	}

	// Render entries
	for i, entry := range visible {
		isLast := i == len(visible)-1 && !hasEllipsis
		connector := "├── "
		if isLast {
			connector = "└── "
		}
		childPrefix := prefix + "│   "
		if isLast {
			childPrefix = prefix + "    "
		}

		name := entry.Name()

		if entry.IsDir() {
			st.dirs++
			buf.WriteString(fmt.Sprintf("%s%s%s\n", prefix, connector, name))
			traverseDir(cfg, buf, st, filepath.Join(dirPath, name), childPrefix, depth+1)
		} else {
			st.files++
			display := name
			if cfg.showSize {
				if info, err := entry.Info(); err == nil {
					st.totalSize += info.Size()
					display += fmt.Sprintf("  (%s)", formatSize(info.Size()))
				}
			}
			buf.WriteString(fmt.Sprintf("%s%s%s\n", prefix, connector, display))
		}
	}

	// Trailing ellipsis for filtered items
	if hasEllipsis {
		if cfg.showCount {
			buf.WriteString(fmt.Sprintf("%s└── ... (%d items)\n", prefix, filteredCount))
		} else {
			buf.WriteString(fmt.Sprintf("%s└── ...\n", prefix))
		}
	}
}

// ============================================================
// Config Building
// ============================================================

func findProfile(name string) (profile, bool) {
	for _, p := range profiles {
		if p.name == name {
			return p, true
		}
	}
	return profile{}, false
}

func buildConfig(p profile, targetDir, outputFile string, maxDepth int,
	dirsOnly, showSize, showCount, ciMode bool, extOverride, ignoreExtra string) *config {

	cfg := &config{
		targetDir:   targetDir,
		outputFile:  outputFile,
		maxDepth:    p.maxDepth,
		dirsOnly:    p.dirsOnly || dirsOnly,
		showSize:    showSize,
		showCount:   showCount,
		ciMode:      ciMode,
		ignoreDirs:  make(map[string]bool),
		ignoreExts:  make(map[string]bool),
		ignoreNames: make(map[string]bool),
	}

	// CLI depth override (-1 = use profile default)
	if maxDepth >= 0 {
		cfg.maxDepth = maxDepth
	}

	// Build extension filter
	if extOverride != "" {
		cfg.extensions = make(map[string]bool)
		for _, ext := range strings.Split(extOverride, ",") {
			ext = strings.TrimSpace(ext)
			if ext == "" {
				continue
			}
			if !strings.HasPrefix(ext, ".") {
				ext = "." + ext
			}
			cfg.extensions[strings.ToLower(ext)] = true
		}
	} else if len(p.extensions) > 0 {
		cfg.extensions = make(map[string]bool)
		for _, ext := range p.extensions {
			cfg.extensions[strings.ToLower(ext)] = true
		}
	}
	// else: nil = accept all files (full profile)

	if len(p.exactNames) > 0 {
		cfg.exactNames = make(map[string]bool)
		for _, name := range p.exactNames {
			cfg.exactNames[name] = true
		}
	}

	// Default ignore lists
	for _, d := range defaultIgnoreDirs {
		cfg.ignoreDirs[d] = true
	}
	for _, e := range defaultIgnoreExts {
		cfg.ignoreExts[e] = true
	}
	for _, n := range defaultIgnoreNames {
		cfg.ignoreNames[n] = true
	}

	// .treeignore
	igDirs, igExts, igNames := loadTreeIgnore(targetDir)
	for _, d := range igDirs {
		cfg.ignoreDirs[d] = true
	}
	for _, e := range igExts {
		cfg.ignoreExts[strings.ToLower(e)] = true
	}
	for _, n := range igNames {
		cfg.ignoreNames[n] = true
	}

	// CLI -ignore additions
	if ignoreExtra != "" {
		for _, item := range strings.Split(ignoreExtra, ",") {
			item = strings.TrimSpace(item)
			if item == "" {
				continue
			}
			if strings.HasPrefix(item, "*.") {
				cfg.ignoreExts[strings.ToLower("."+strings.TrimPrefix(item, "*."))] = true
			} else {
				cfg.ignoreDirs[item] = true
				cfg.ignoreNames[item] = true
			}
		}
	}

	return cfg
}

// ============================================================
// Generation
// ============================================================

func generate(cfg *config, profileName string) (string, stats) {
	var buf bytes.Buffer
	var st stats

	rootName := filepath.Base(cfg.targetDir)
	if rootName == "." || rootName == "" {
		if abs, err := filepath.Abs(cfg.targetDir); err == nil {
			rootName = filepath.Base(abs)
		}
	}

	// Header
	buf.WriteString("# Directory Structure\n\n")
	buf.WriteString(fmt.Sprintf("- **Generated**: %s\n", time.Now().Format("2006-01-02 15:04:05")))
	buf.WriteString(fmt.Sprintf("- **Profile**: %s\n", profileName))
	if cfg.maxDepth > 0 {
		buf.WriteString(fmt.Sprintf("- **Depth**: %d\n", cfg.maxDepth))
	}
	buf.WriteString("\n```\n")

	// Root
	buf.WriteString(rootName + "/\n")

	// Traverse
	traverseDir(cfg, &buf, &st, cfg.targetDir, "", 0)

	buf.WriteString("```\n")
	return buf.String(), st
}

func writeOutput(content, outputPath string) error {
	f, err := os.Create(outputPath)
	if err != nil {
		return err
	}
	defer f.Close()

	w := bufio.NewWriterSize(f, 64*1024)
	if _, err := w.WriteString(content); err != nil {
		return err
	}
	return w.Flush()
}

// ============================================================
// Interactive Mode
// ============================================================

func runInteractive() {
	fmt.Println("==============================================")
	fmt.Println("  Generate File Tree")
	fmt.Println("  Create Markdown directory structure")
	fmt.Println("==============================================")

	targetDir, _ := os.Getwd()
	fmt.Printf("\nTarget: %s\n", targetDir)

	// Profile selection
	fmt.Println("\nSelect profile:")
	for i, p := range profiles {
		def := ""
		if i == 1 {
			def = " (default)"
		}
		fmt.Printf("  [%d] %-10s — %s%s\n", i+1, p.name, p.description, def)
	}

	fmt.Print("\n> ")
	input, _ := stdinReader.ReadString('\n')
	input = strings.TrimSpace(input)
	profileIdx := 1 // default: standard
	if n, err := strconv.Atoi(input); err == nil && n >= 1 && n <= len(profiles) {
		profileIdx = n - 1
	}
	p := profiles[profileIdx]
	fmt.Printf("Using profile: %s\n", p.name)

	// Optional depth
	depthDefault := "unlimited"
	if p.maxDepth > 0 {
		depthDefault = strconv.Itoa(p.maxDepth)
	}
	fmt.Printf("\nMax depth (0=unlimited, default: %s): ", depthDefault)
	depthStr, _ := stdinReader.ReadString('\n')
	depthStr = strings.TrimSpace(depthStr)
	maxDepth := -1 // use profile default
	if depthStr != "" {
		if n, err := strconv.Atoi(depthStr); err == nil && n >= 0 {
			maxDepth = n
		}
	}

	// Output file
	fmt.Print("Output file (default: directory_structure.md): ")
	outStr, _ := stdinReader.ReadString('\n')
	outStr = strings.TrimSpace(outStr)
	if outStr == "" {
		outStr = "directory_structure.md"
	}

	cfg := buildConfig(p, targetDir, outStr, maxDepth, false, false, false, false, "", "")

	fmt.Println("\nGenerating...")
	startTime := time.Now()

	content, st := generate(cfg, p.name)
	if err := writeOutput(content, cfg.outputFile); err != nil {
		fmt.Printf("[ERROR] %v\n", err)
		waitForKeyPress()
		return
	}

	duration := time.Since(startTime)
	printSummary(cfg.outputFile, p.name, st, duration, cfg)
	waitForKeyPress()
}

// ============================================================
// Utilities
// ============================================================

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

func printSummary(outputFile, profileName string, st stats, duration time.Duration, cfg *config) {
	fmt.Println("\n===========================================")
	fmt.Println("  GENERATION COMPLETE")
	fmt.Println("===========================================")
	fmt.Printf("  Output:      %s\n", outputFile)
	fmt.Printf("  Directories: %d\n", st.dirs)
	sizeStr := ""
	if cfg.showSize && st.totalSize > 0 {
		sizeStr = fmt.Sprintf(" (%s)", formatSize(st.totalSize))
	}
	fmt.Printf("  Files:       %d%s\n", st.files, sizeStr)
	fmt.Printf("  Profile:     %s\n", profileName)
	if cfg.maxDepth > 0 {
		fmt.Printf("  Depth limit: %d\n", cfg.maxDepth)
	}
	fmt.Printf("  Time:        %s\n", duration.Round(time.Millisecond))
}

func waitForKeyPress() {
	fmt.Println("\nPress Enter to exit...")
	stdinReader.ReadBytes('\n')
}

// ============================================================
// Entry Point
// ============================================================

func main() {
	var (
		profileName string
		targetDir   string
		outputFile  string
		maxDepth    int
		extStr      string
		ignoreStr   string
		dirsOnly    bool
		showSize    bool
		showCount   bool
		ciMode      bool
		interactive bool
	)

	flag.StringVar(&profileName, "profile", "", "Profile: minimal, standard, detailed, full (default: standard)")
	flag.StringVar(&targetDir, "target", "", "Target directory (default: current directory)")
	flag.StringVar(&outputFile, "o", "", "Output file (default: directory_structure.md)")
	flag.IntVar(&maxDepth, "depth", -1, "Max depth, 0=unlimited (default: from profile)")
	flag.StringVar(&extStr, "ext", "", "File extensions to include, comma-separated (overrides profile)")
	flag.StringVar(&ignoreStr, "ignore", "", "Additional dirs/names to ignore, comma-separated")
	flag.BoolVar(&dirsOnly, "dirs-only", false, "Show only directories")
	flag.BoolVar(&showSize, "show-size", false, "Show file sizes")
	flag.BoolVar(&showCount, "show-count", false, "Show hidden item counts in ...")
	flag.BoolVar(&ciMode, "ci", false, "CI mode (non-interactive, no prompts)")
	flag.BoolVar(&interactive, "i", false, "Interactive mode with profile selection")
	flag.Parse()

	// Interactive mode
	if interactive {
		runInteractive()
		return
	}

	// Resolve target directory
	if targetDir == "" {
		if args := flag.Args(); len(args) > 0 {
			targetDir = args[0]
		} else {
			var err error
			targetDir, err = os.Getwd()
			if err != nil {
				fmt.Printf("[ERROR] Cannot get current directory: %v\n", err)
				os.Exit(1)
			}
		}
	}

	// Validate target
	info, err := os.Stat(targetDir)
	if err != nil || !info.IsDir() {
		fmt.Printf("[ERROR] Target is not a valid directory: %s\n", targetDir)
		if !ciMode {
			waitForKeyPress()
		}
		os.Exit(1)
	}

	// Resolve absolute path
	targetDir, _ = filepath.Abs(targetDir)

	// Resolve output file
	if outputFile == "" {
		if envOut := strings.TrimSpace(os.Getenv("FILE_TREE_OUT")); envOut != "" {
			outputFile = envOut
		} else {
			outputFile = "directory_structure.md"
		}
	}

	// Resolve profile
	if profileName == "" {
		profileName = "standard"
	}
	p, ok := findProfile(profileName)
	if !ok {
		fmt.Printf("[ERROR] Unknown profile: %s\n", profileName)
		fmt.Println("Available profiles: minimal, standard, detailed, full")
		if !ciMode {
			waitForKeyPress()
		}
		os.Exit(1)
	}

	// Build config
	cfg := buildConfig(p, targetDir, outputFile, maxDepth, dirsOnly, showSize, showCount, ciMode, extStr, ignoreStr)

	// Generate
	if !ciMode {
		fmt.Printf("Target: %s\n", targetDir)
		fmt.Printf("Profile: %s\n", profileName)
		fmt.Println("Generating...")
	}

	startTime := time.Now()
	content, st := generate(cfg, profileName)

	if err := writeOutput(content, cfg.outputFile); err != nil {
		fmt.Printf("[ERROR] Cannot write output file: %v\n", err)
		if !ciMode {
			waitForKeyPress()
		}
		os.Exit(1)
	}

	duration := time.Since(startTime)
	printSummary(cfg.outputFile, profileName, st, duration, cfg)

	if !ciMode {
		waitForKeyPress()
	}
}
