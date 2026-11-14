// Package generate_file_tree recursively generates a Markdown directory tree.
// It supports several detail profiles, depth limits, extension filters and
// .treeignore files for project-specific exclusions.
//
// The command is dispatched in-process by cmd/dev-tools.
package generate_file_tree

import (
	"bufio"
	"bytes"
	"errors"
	"flag"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"sort"
	"strconv"
	"strings"
	"time"

	"cyclonegames.tools/scripts/internal/logging"
	"cyclonegames.tools/scripts/internal/safefs"
	"cyclonegames.tools/scripts/internal/toolkit"
)

type config struct {
	targetDir   string
	outputFile  string
	maxDepth    int
	dirsOnly    bool
	showSize    bool
	showCount   bool
	ciMode      bool
	extensions  map[string]bool
	exactNames  map[string]bool
	ignoreDirs  map[string]bool
	ignoreExts  map[string]bool
	ignoreNames map[string]bool
}

type stats struct {
	dirs       int
	files      int
	totalSize  int64
	readErrors []string
}

type profile struct {
	name        string
	description string
	extensions  []string
	exactNames  []string
	dirsOnly    bool
	maxDepth    int
}

var profiles = []profile{
	{
		name:        "minimal",
		description: "Folders only, max depth 3 - quick overview",
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
		description: "Code, config, and asset types",
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

// loadTreeIgnore reads a .treeignore file from the target directory.
// Format: one entry per line.
//
//	name/   -> ignore directory by name
//	*.ext   -> ignore file extension
//	name    -> ignore exact file or directory name
//	# ...   -> comment
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
	if c.extensions == nil && c.exactNames == nil {
		return true
	}
	if c.exactNames != nil && c.exactNames[name] {
		return true
	}
	ext := strings.ToLower(filepath.Ext(name))
	return ext != "" && c.extensions != nil && c.extensions[ext]
}

// traverseDir collects read failures into stats instead of dropping them, so a
// partially unreadable tree cannot be reported as a complete success.
func traverseDir(cfg *config, buf *bytes.Buffer, st *stats, dirPath, prefix string, depth int) {
	entries, err := os.ReadDir(dirPath)
	if err != nil {
		st.readErrors = append(st.readErrors, fmt.Sprintf("%s: %v", dirPath, err))
		return
	}

	var visibleDirs, visibleFiles []os.DirEntry
	filteredCount := 0

	for _, entry := range entries {
		name := entry.Name()

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
			continue
		} else if cfg.matchesFilter(name) {
			visibleFiles = append(visibleFiles, entry)
		} else {
			filteredCount++
		}
	}

	sort.Slice(visibleDirs, func(i, j int) bool {
		return strings.ToLower(visibleDirs[i].Name()) < strings.ToLower(visibleDirs[j].Name())
	})
	sort.Slice(visibleFiles, func(i, j int) bool {
		return strings.ToLower(visibleFiles[i].Name()) < strings.ToLower(visibleFiles[j].Name())
	})

	visible := make([]os.DirEntry, 0, len(visibleDirs)+len(visibleFiles))
	visible = append(visible, visibleDirs...)
	visible = append(visible, visibleFiles...)

	hasEllipsis := filteredCount > 0

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

	if hasEllipsis {
		if cfg.showCount {
			buf.WriteString(fmt.Sprintf("%s└── ... (%d items)\n", prefix, filteredCount))
		} else {
			buf.WriteString(fmt.Sprintf("%s└── ...\n", prefix))
		}
	}
}

func findProfile(name string) (profile, bool) {
	for _, p := range profiles {
		if p.name == name {
			return p, true
		}
	}
	return profile{}, false
}

func buildConfig(p profile, targetDir, outputFile string, maxDepth int,
	dirsOnly, showSize, showCount, ciMode, noDefaultIgnore bool, extOverride, ignoreExtra string) *config {

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

	if maxDepth >= 0 {
		cfg.maxDepth = maxDepth
	}

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

	if len(p.exactNames) > 0 {
		cfg.exactNames = make(map[string]bool)
		for _, name := range p.exactNames {
			cfg.exactNames[name] = true
		}
	}

	if !noDefaultIgnore {
		for _, d := range defaultIgnoreDirs {
			cfg.ignoreDirs[d] = true
		}
		for _, e := range defaultIgnoreExts {
			cfg.ignoreExts[e] = true
		}
		for _, n := range defaultIgnoreNames {
			cfg.ignoreNames[n] = true
		}
	}

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

func generate(cfg *config, profileName string) (string, stats) {
	var buf bytes.Buffer
	var st stats

	rootName := filepath.Base(cfg.targetDir)
	if rootName == "." || rootName == "" {
		if abs, err := filepath.Abs(cfg.targetDir); err == nil {
			rootName = filepath.Base(abs)
		}
	}

	buf.WriteString("# Directory Structure\n\n")
	buf.WriteString(fmt.Sprintf("- **Generated**: %s\n", time.Now().Format("2006-01-02 15:04:05")))
	buf.WriteString(fmt.Sprintf("- **Profile**: %s\n", profileName))
	if cfg.maxDepth > 0 {
		buf.WriteString(fmt.Sprintf("- **Depth**: %d\n", cfg.maxDepth))
	}
	buf.WriteString("\n```\n")

	buf.WriteString(rootName + "/\n")

	traverseDir(cfg, &buf, &st, cfg.targetDir, "", 0)

	buf.WriteString("```\n")
	return buf.String(), st
}

// writeOutput stages the content in a temporary file and publishes it with an
// atomic replace, so a failed write never truncates the previous tree.
func writeOutput(content, outputPath string) error {
	policy := safefs.HostPathPolicy()
	abs, err := filepath.Abs(outputPath)
	if err != nil {
		return fmt.Errorf("cannot resolve output path %s: %w", outputPath, err)
	}
	if err := policy.ValidateAbsolute(abs); err != nil {
		return err
	}
	base := filepath.Base(abs)
	if err := policy.ValidateComponent(base); err != nil {
		return err
	}
	dir := filepath.Dir(abs)
	if err := policy.EnsureWritable(dir); err != nil {
		return err
	}
	pattern := "." + base + ".partial-*"
	budgetName := "." + base + ".partial-123456789012"
	if err := policy.Budget(dir, budgetName); err != nil {
		return err
	}

	f, err := os.CreateTemp(dir, pattern)
	if err != nil {
		return err
	}
	stage := f.Name()

	w := bufio.NewWriterSize(f, 64*1024)
	if _, err := w.WriteString(content); err != nil {
		_ = f.Close()
		_ = os.Remove(stage)
		return err
	}
	if err := w.Flush(); err != nil {
		_ = f.Close()
		_ = os.Remove(stage)
		return err
	}
	if err := f.Close(); err != nil {
		_ = os.Remove(stage)
		return err
	}
	return safefs.PublishReplace(stage, abs)
}

// readLine returns (text, nil) for a normal line and for the final unterminated
// line before EOF; a closed or exhausted input stream returns ("", io.EOF) so
// callers cancel instead of writing files on defaults.
func readLine(reader *bufio.Reader) (string, error) {
	line, err := reader.ReadString('\n')
	if err != nil && err != io.EOF {
		return "", err
	}
	if err == io.EOF && strings.TrimSpace(line) == "" {
		return "", io.EOF
	}
	return strings.TrimSpace(line), nil
}

func cancelledByEOF() int {
	fmt.Println("\n[FAIL] Cancelled: input stream closed.")
	toolkit.WaitForExit()
	return toolkit.ExitFailure
}

func reportReadErrors(st stats) {
	for _, msg := range st.readErrors {
		logging.Errorf("directory read failed: %s", msg)
	}
}

// runInteractive is the guided interactive mode. Every prompt is fail-closed:
// a closed input stream cancels before any file is written.
func runInteractive(reader *bufio.Reader) int {
	fmt.Println("==============================================")
	fmt.Println("  Generate File Tree")
	fmt.Println("  Create Markdown directory structure")
	fmt.Println("==============================================")

	targetDir, _ := os.Getwd()
	fmt.Printf("\nTarget: %s\n", targetDir)

	fmt.Println("\nSelect profile:")
	for i, p := range profiles {
		def := ""
		if i == 1 {
			def = " (default)"
		}
		fmt.Printf("  [%d] %-10s - %s%s\n", i+1, p.name, p.description, def)
	}

	fmt.Print("\n> ")
	input, err := readLine(reader)
	if err != nil {
		return cancelledByEOF()
	}
	profileIdx := 1
	if n, convErr := strconv.Atoi(input); convErr == nil && n >= 1 && n <= len(profiles) {
		profileIdx = n - 1
	}
	p := profiles[profileIdx]
	fmt.Printf("Using profile: %s\n", p.name)

	depthDefault := "unlimited"
	if p.maxDepth > 0 {
		depthDefault = strconv.Itoa(p.maxDepth)
	}
	fmt.Printf("\nMax depth (0=unlimited, default: %s): ", depthDefault)
	depthStr, err := readLine(reader)
	if err != nil {
		return cancelledByEOF()
	}
	maxDepth := -1
	if depthStr != "" {
		if n, convErr := strconv.Atoi(depthStr); convErr == nil && n >= 0 {
			maxDepth = n
		}
	}

	fmt.Print("Output file (default: directory_structure.md): ")
	outStr, err := readLine(reader)
	if err != nil {
		return cancelledByEOF()
	}
	if outStr == "" {
		outStr = "directory_structure.md"
	}

	cfg := buildConfig(p, targetDir, outStr, maxDepth, false, false, false, false, false, "", "")

	fmt.Println("\nGenerating...")
	startTime := time.Now()

	content, st := generate(cfg, p.name)
	reportReadErrors(st)
	if err := writeOutput(content, cfg.outputFile); err != nil {
		logging.Errorf("%v", err)
		toolkit.WaitForExit()
		return toolkit.ExitFailure
	}

	duration := time.Since(startTime)
	printSummary(cfg.outputFile, p.name, st, duration, cfg)
	toolkit.WaitForExit()
	if len(st.readErrors) > 0 {
		return toolkit.ExitFailure
	}
	return toolkit.ExitSuccess
}

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

// Run executes the file-tree generator and returns its process exit code.
func Run(args []string) int {
	logging.Command("generate_file_tree")
	var (
		profileName     string
		targetDir       string
		outputFile      string
		maxDepth        int
		extStr          string
		ignoreStr       string
		dirsOnly        bool
		showSize        bool
		showCount       bool
		ciMode          bool
		interactive     bool
		noDefaultIgnore bool
	)

	flags := flag.NewFlagSet("generate_file_tree", flag.ContinueOnError)
	flags.SetOutput(os.Stderr)
	flags.StringVar(&profileName, "profile", "", "Profile: minimal, standard, detailed, full (default: standard)")
	flags.StringVar(&targetDir, "target", "", "Target directory (default: current directory)")
	flags.StringVar(&outputFile, "o", "", "Output file (default: directory_structure.md)")
	flags.IntVar(&maxDepth, "depth", -1, "Max depth, 0=unlimited (default: from profile)")
	flags.StringVar(&extStr, "ext", "", "File extensions to include, comma-separated (overrides profile)")
	flags.StringVar(&ignoreStr, "ignore", "", "Additional dirs/names to ignore, comma-separated")
	flags.BoolVar(&dirsOnly, "dirs-only", false, "Show only directories")
	flags.BoolVar(&showSize, "show-size", false, "Show file sizes")
	flags.BoolVar(&showCount, "show-count", false, "Show hidden item counts in ...")
	flags.BoolVar(&ciMode, "ci", false, "CI mode (non-interactive, no prompts)")
	flags.BoolVar(&interactive, "i", false, "Interactive mode with profile selection")
	flags.BoolVar(&noDefaultIgnore, "no-default-ignore", false, "Do not apply the built-in ignore lists (.treeignore and -ignore still apply)")
	if err := flags.Parse(args); err != nil {
		if errors.Is(err, flag.ErrHelp) {
			return toolkit.ExitSuccess
		}
		return toolkit.ExitUsage
	}

	reader := toolkit.Input()

	if interactive {
		return runInteractive(reader)
	}

	if targetDir == "" {
		if rest := flags.Args(); len(rest) > 0 {
			targetDir = rest[0]
		} else {
			var err error
			targetDir, err = os.Getwd()
			if err != nil {
				logging.Errorf("cannot get current directory: %v", err)
				return toolkit.ExitFailure
			}
		}
	}

	info, err := os.Stat(targetDir)
	if err != nil || !info.IsDir() {
		logging.Errorf("target is not a valid directory: %s", targetDir)
		if !ciMode {
			toolkit.WaitForExit()
		}
		return toolkit.ExitFailure
	}

	targetDir, _ = filepath.Abs(targetDir)

	if outputFile == "" {
		if envOut := strings.TrimSpace(os.Getenv("FILE_TREE_OUT")); envOut != "" {
			outputFile = envOut
		} else {
			outputFile = "directory_structure.md"
		}
	}

	if profileName == "" {
		profileName = "standard"
	}
	p, ok := findProfile(profileName)
	if !ok {
		logging.Errorf("unknown profile: %s", profileName)
		fmt.Println("Available profiles: minimal, standard, detailed, full")
		if !ciMode {
			toolkit.WaitForExit()
		}
		return toolkit.ExitFailure
	}

	cfg := buildConfig(p, targetDir, outputFile, maxDepth, dirsOnly, showSize, showCount, ciMode, noDefaultIgnore, extStr, ignoreStr)

	if !ciMode {
		fmt.Printf("Target: %s\n", targetDir)
		fmt.Printf("Profile: %s\n", profileName)
		fmt.Println("Generating...")
	}

	startTime := time.Now()
	content, st := generate(cfg, profileName)
	reportReadErrors(st)

	if err := writeOutput(content, cfg.outputFile); err != nil {
		logging.Errorf("cannot write output file: %v", err)
		if !ciMode {
			toolkit.WaitForExit()
		}
		return toolkit.ExitFailure
	}

	duration := time.Since(startTime)
	printSummary(cfg.outputFile, profileName, st, duration, cfg)

	if !ciMode {
		toolkit.WaitForExit()
	}
	if len(st.readErrors) > 0 {
		return toolkit.ExitFailure
	}
	return toolkit.ExitSuccess
}
