package main

import (
	"bufio"
	"bytes"
	"encoding/json"
	"fmt"
	"io"
	"os"
	"os/exec"
	"path/filepath"
	"regexp"
	"runtime"
	"sort"
	"strconv"
	"strings"
	"time"
)

// ============================================================
// Constants & Configuration
// ============================================================

const (
	stateFileName                   = ".rename_project.json"
	backupDirName                   = ".rename_backup"
	maxBackupCount                  = 5
	buildDataScriptRelativePath     = "Assets/Build/Editor/BuildPipeline/BuildData.cs"
	buildDataScriptMetaRelativePath = buildDataScriptRelativePath + ".meta"
)

var namePattern = regexp.MustCompile(`^[a-zA-Z_][a-zA-Z0-9_-]*$`)
var metaGUIDPattern = regexp.MustCompile(`(?m)^guid:\s*([0-9a-fA-F]{32})\s*$`)
var unityYAMLDocumentPattern = regexp.MustCompile(`(?m)^--- !u!`)

// Directories to exclude when searching for the main project folder
var excludedDirs = map[string]bool{
	// Unity standard folders
	"Build":                    true,
	"ThirdParty":               true,
	"Resources":                true,
	"Settings":                 true,
	"Plugins":                  true,
	"StreamingAssets":          true,
	"Editor Default Resources": true,
	"Gizmos":                   true,
	"Standard Assets":          true,

	// HybridCLR related
	"HybridCLRGenerate": true,
	"HybridCLRData":     true,
	"CompiledDLLs":      true,

	// Obfuscation related
	"Obfuz":      true,
	"Obfuscator": true,

	// Asset management related
	"AddressableAssetsData": true,
	"YooAsset":              true,
	"yoo":                   true,
	"Bundles":               true,

	// Common third-party or generated folders
	"TextMesh Pro": true,
	"Demigiant":    true,
	"DOTween":      true,
}

// Global stdin reader to avoid multiple buffered readers competing for stdin
var stdinReader *bufio.Reader

func init() {
	stdinReader = bufio.NewReader(os.Stdin)
}

// ============================================================
// Types
// ============================================================

// RenameState persists the current project identity for reliable re-detection.
// Saved as .rename_project.json in the Unity project root after each successful rename.
type RenameState struct {
	ProjectFolder string `json:"projectFolder"`
	CompanyName   string `json:"companyName"`
	AppName       string `json:"appName"`
	RenamedAt     string `json:"renamedAt"`
}

// FileChange describes a planned modification for dry-run preview
type FileChange struct {
	Path    string
	Action  string   // "rename", "modify"
	Details []string // specific changes within the file
}

// buildDataAssetUpdate is a validated, in-memory rewrite of one BuildData asset.
// Planning every update before writing prevents a malformed asset from causing a partial batch update.
type buildDataAssetUpdate struct {
	Path           string
	UpdatedContent []byte
	Details        []string
}

// Logger writes to both stdout and a log file simultaneously
type Logger struct {
	file   *os.File
	writer io.Writer
}

// ============================================================
// Logger
// ============================================================

func NewLogger(logPath string) *Logger {
	f, err := os.OpenFile(logPath, os.O_CREATE|os.O_WRONLY|os.O_TRUNC, 0644)
	if err != nil {
		fmt.Printf("Warning: could not create log file %s: %v\n", logPath, err)
		return &Logger{writer: os.Stdout}
	}
	return &Logger{file: f, writer: io.MultiWriter(os.Stdout, f)}
}

func (l *Logger) Printf(format string, args ...interface{}) {
	fmt.Fprintf(l.writer, format, args...)
}

func (l *Logger) Println(args ...interface{}) {
	fmt.Fprintln(l.writer, args...)
}

func (l *Logger) Close() {
	if l.file != nil {
		l.file.Close()
	}
}

// ============================================================
// State Management
// ============================================================

func loadState(projectRoot string) (*RenameState, error) {
	statePath := filepath.Join(projectRoot, stateFileName)
	data, err := os.ReadFile(statePath)
	if err != nil {
		return nil, err
	}
	var state RenameState
	if err := json.Unmarshal(data, &state); err != nil {
		return nil, fmt.Errorf("invalid state file %s: %v", statePath, err)
	}
	return &state, nil
}

func saveState(projectRoot string, state *RenameState) error {
	statePath := filepath.Join(projectRoot, stateFileName)
	data, err := json.MarshalIndent(state, "", "    ")
	if err != nil {
		return err
	}
	return os.WriteFile(statePath, data, 0644)
}

// ============================================================
// Project Detection
// ============================================================

// findProjectRoot scans for a Unity project root directory in the current or immediate subdirectories.
func findProjectRoot() (string, error) {
	if _, err := os.Stat("./Assets"); err == nil {
		if _, err := os.Stat("./ProjectSettings"); err == nil {
			return ".", nil
		}
	}

	entries, err := os.ReadDir(".")
	if err != nil {
		return "", err
	}
	for _, entry := range entries {
		if entry.IsDir() {
			if _, err := os.Stat(filepath.Join(entry.Name(), "Assets")); err == nil {
				if _, err := os.Stat(filepath.Join(entry.Name(), "ProjectSettings")); err == nil {
					return entry.Name(), nil
				}
			}
		}
	}
	return "", fmt.Errorf("Unity project root not found in current directory or immediate subdirectories")
}

// findMainProjectFolder intelligently detects the main project folder in Assets directory.
// It uses multiple heuristics to identify the correct folder.
func findMainProjectFolder(projectRoot, productName string) (string, error) {
	assetsPath := filepath.Join(projectRoot, "Assets")
	entries, err := os.ReadDir(assetsPath)
	if err != nil {
		return "", fmt.Errorf("failed to read Assets directory: %v", err)
	}

	type candidate struct {
		name   string
		score  int
		reason string
	}
	var candidates []candidate

	for _, entry := range entries {
		if !entry.IsDir() {
			continue
		}

		dirName := entry.Name()

		if excludedDirs[dirName] {
			continue
		}
		if strings.HasPrefix(dirName, ".") || strings.HasPrefix(dirName, "_") {
			continue
		}

		dirPath := filepath.Join(assetsPath, dirName)
		score := 0
		var reasons []string

		// Check if directory name matches productName (highest priority)
		if strings.EqualFold(dirName, productName) {
			score += 100
			reasons = append(reasons, "matches productName")
		}

		subEntries, err := os.ReadDir(dirPath)
		if err != nil {
			continue
		}

		hasAsmdef := false
		hasEditor := false
		hasScripts := false
		hasBuiltIn := false
		hasLiveContent := false
		hasScenes := false

		for _, sub := range subEntries {
			subName := sub.Name()
			if !sub.IsDir() && strings.HasSuffix(subName, ".asmdef") {
				hasAsmdef = true
			}
			if sub.IsDir() {
				switch subName {
				case "Editor":
					hasEditor = true
				case "Scripts":
					hasScripts = true
				case "BuiltIn":
					hasBuiltIn = true
				case "LiveContent":
					hasLiveContent = true
				case "Scenes":
					hasScenes = true
				}
			}
		}

		if hasAsmdef {
			score += 30
			reasons = append(reasons, "contains asmdef")
		}
		if hasEditor {
			score += 20
			reasons = append(reasons, "has Editor folder")
		}
		if hasScripts {
			score += 15
			reasons = append(reasons, "has Scripts folder")
		}
		if hasBuiltIn {
			score += 25
			reasons = append(reasons, "has BuiltIn folder")
		}
		if hasLiveContent {
			score += 25
			reasons = append(reasons, "has LiveContent folder")
		}
		if hasScenes {
			score += 20
			reasons = append(reasons, "has Scenes folder")
		}

		asmdefCount := countAsmdefFiles(dirPath)
		if asmdefCount > 0 {
			score += asmdefCount * 10
			reasons = append(reasons, fmt.Sprintf("contains %d asmdef files", asmdefCount))
		}

		if score > 0 {
			candidates = append(candidates, candidate{
				name:   dirName,
				score:  score,
				reason: strings.Join(reasons, ", "),
			})
		}
	}

	if len(candidates) == 0 {
		return "", fmt.Errorf("could not find main project folder in Assets directory")
	}

	best := candidates[0]
	for _, c := range candidates[1:] {
		if c.score > best.score {
			best = c
		}
	}

	fmt.Printf("Detected main project folder: %s (score: %d, reason: %s)\n", best.name, best.score, best.reason)
	return best.name, nil
}

// countAsmdefFiles counts .asmdef files recursively in a directory
func countAsmdefFiles(dir string) int {
	count := 0
	filepath.Walk(dir, func(path string, info os.FileInfo, err error) error {
		if err != nil {
			return nil
		}
		if !info.IsDir() && strings.HasSuffix(info.Name(), ".asmdef") {
			count++
		}
		return nil
	})
	return count
}

// getCurrentProjectInfo reads the current project settings.
// Priority: state file > auto-detection > EditorBuildSettings fallback.
func getCurrentProjectInfo(projectRoot string) (string, string, string, error) {
	// Priority 1: Read from state file (reliable for re-runs)
	state, err := loadState(projectRoot)
	if err == nil && state.ProjectFolder != "" {
		folderPath := filepath.Join(projectRoot, "Assets", state.ProjectFolder)
		if _, statErr := os.Stat(folderPath); statErr == nil {
			fmt.Printf("Loaded project info from state file (%s)\n", stateFileName)
			return state.ProjectFolder, state.CompanyName, state.AppName, nil
		}
		fmt.Printf("Warning: state file references non-existent folder '%s', falling back to auto-detection\n", state.ProjectFolder)
	}

	// Priority 2: Auto-detect from ProjectSettings
	projectSettingsPath := filepath.Join(projectRoot, "ProjectSettings", "ProjectSettings.asset")
	projectSettingsBytes, err := os.ReadFile(projectSettingsPath)
	if err != nil {
		return "", "", "", fmt.Errorf("failed to read %s: %v", projectSettingsPath, err)
	}
	projectSettingsContent := string(projectSettingsBytes)

	companyNameRegex := regexp.MustCompile(`companyName: (.*)`)
	productNameRegex := regexp.MustCompile(`productName: (.*)`)

	companyNameMatches := companyNameRegex.FindStringSubmatch(projectSettingsContent)
	if len(companyNameMatches) < 2 {
		return "", "", "", fmt.Errorf("could not find companyName in %s", projectSettingsPath)
	}
	companyName := strings.TrimSpace(companyNameMatches[1])

	productNameMatches := productNameRegex.FindStringSubmatch(projectSettingsContent)
	if len(productNameMatches) < 2 {
		return "", "", "", fmt.Errorf("could not find productName in %s", projectSettingsPath)
	}
	appName := strings.TrimSpace(productNameMatches[1])

	// Use intelligent detection to find the main project folder
	projectName, err := findMainProjectFolder(projectRoot, appName)
	if err != nil {
		// Fallback: try to extract from EditorBuildSettings
		editorBuildSettingsPath := filepath.Join(projectRoot, "ProjectSettings", "EditorBuildSettings.asset")
		editorBuildSettingsBytes, readErr := os.ReadFile(editorBuildSettingsPath)
		if readErr != nil {
			return "", "", "", fmt.Errorf("auto-detection failed and fallback read error: %v", readErr)
		}
		editorBuildSettingsContent := string(editorBuildSettingsBytes)

		projectNameRegex := regexp.MustCompile(`path: Assets/([^/]+)/`)
		projectNameMatches := projectNameRegex.FindStringSubmatch(editorBuildSettingsContent)
		if len(projectNameMatches) < 2 {
			return "", "", "", fmt.Errorf("could not detect project folder: %v", err)
		}
		projectName = strings.TrimSpace(projectNameMatches[1])
		fmt.Printf("Using fallback detection: %s\n", projectName)
	}

	return projectName, companyName, appName, nil
}

// ============================================================
// User Input & Validation
// ============================================================

// promptValidatedInput prompts the user for a value with immediate validation.
// Pressing Enter keeps the current value.
func promptValidatedInput(stepNum int, label, description, currentValue string) string {
	for {
		clearScreen()
		fmt.Printf("Step %d: Enter the New %s\n", stepNum, label)
		fmt.Println(description)
		fmt.Printf("\nCurrent value: %s\n", currentValue)
		fmt.Print("Enter new value (press Enter to keep current): ")

		input, _ := stdinReader.ReadString('\n')
		input = strings.TrimSpace(input)

		if input == "" {
			return currentValue
		}

		if !namePattern.MatchString(input) {
			fmt.Printf("\nInvalid input: '%s'\n", input)
			fmt.Println("Must only contain letters, numbers, underscores (_), and dashes (-).")
			fmt.Println("Cannot start with a number or dash.")
			waitForKeyPress()
			continue
		}

		return input
	}
}

// getBuildDataScriptGUID returns the Unity GUID for BuildData.cs. A project without the
// Build module is valid and returns an empty GUID; a present but malformed module is an error.
func getBuildDataScriptGUID(projectRoot string) (string, error) {
	metaPath := filepath.Join(projectRoot, filepath.FromSlash(buildDataScriptMetaRelativePath))
	content, err := os.ReadFile(metaPath)
	if err != nil {
		if os.IsNotExist(err) {
			scriptPath := filepath.Join(projectRoot, filepath.FromSlash(buildDataScriptRelativePath))
			_, scriptErr := os.Stat(scriptPath)
			if scriptErr == nil {
				return "", fmt.Errorf("BuildData script exists but its meta file is missing: %s", metaPath)
			}
			if !os.IsNotExist(scriptErr) {
				return "", fmt.Errorf("failed to inspect BuildData script %s: %v", scriptPath, scriptErr)
			}
			return "", nil
		}
		return "", fmt.Errorf("failed to read BuildData script meta file %s: %v", metaPath, err)
	}

	matches := metaGUIDPattern.FindSubmatch(content)
	if len(matches) != 2 {
		return "", fmt.Errorf("could not read a valid Unity GUID from %s", metaPath)
	}

	return string(matches[1]), nil
}

func buildDataScriptReferencePattern(guid string) *regexp.Regexp {
	return regexp.MustCompile(
		`(?m)^[ \t]*m_Script:[ \t]*\{[^\r\n}]*guid:[ \t]*` +
			regexp.QuoteMeta(guid) +
			`(?:[ \t]*,|[ \t]*\})`,
	)
}

func fileContainsBytes(path string, needle []byte) (bool, error) {
	if len(needle) == 0 {
		return false, nil
	}

	file, err := os.Open(path)
	if err != nil {
		return false, err
	}
	defer file.Close()

	const chunkSize = 64 * 1024
	buffer := make([]byte, chunkSize+len(needle)-1)
	overlap := 0
	for {
		count, readErr := file.Read(buffer[overlap:])
		available := overlap + count
		if bytes.Contains(buffer[:available], needle) {
			return true, nil
		}
		if readErr == io.EOF {
			return false, nil
		}
		if readErr != nil {
			return false, readErr
		}
		if count == 0 {
			return false, io.ErrNoProgress
		}

		overlap = len(needle) - 1
		if overlap > available {
			overlap = available
		}
		copy(buffer[:overlap], buffer[available-overlap:available])
	}
}

// findBuildDataAssets identifies BuildData assets by their serialized MonoScript GUID instead
// of relying on a project-specific asset path or file name.
func findBuildDataAssets(projectRoot, scriptGUID string) ([]string, error) {
	if scriptGUID == "" {
		return nil, nil
	}

	assetsRoot := filepath.Join(projectRoot, "Assets")
	referencePattern := buildDataScriptReferencePattern(scriptGUID)
	var paths []string
	err := filepath.Walk(assetsRoot, func(path string, info os.FileInfo, walkErr error) error {
		if walkErr != nil {
			return walkErr
		}
		if info.IsDir() || !strings.EqualFold(filepath.Ext(info.Name()), ".asset") {
			return nil
		}
		if info.Mode()&os.ModeSymlink != 0 {
			return fmt.Errorf("refusing to inspect symlinked Unity asset: %s", path)
		}

		relativePath, relErr := filepath.Rel(assetsRoot, path)
		if relErr != nil || relativePath == ".." || strings.HasPrefix(relativePath, ".."+string(os.PathSeparator)) {
			return fmt.Errorf("asset path escaped the Assets directory: %s", path)
		}

		containsGUID, readErr := fileContainsBytes(path, []byte(scriptGUID))
		if readErr != nil {
			return fmt.Errorf("failed to read asset %s: %v", path, readErr)
		}
		if !containsGUID {
			return nil
		}

		content, readErr := os.ReadFile(path)
		if readErr != nil {
			return fmt.Errorf("failed to read candidate BuildData asset %s: %v", path, readErr)
		}
		if !referencePattern.Match(content) {
			return nil
		}
		paths = append(paths, path)
		return nil
	})
	if err != nil {
		return nil, fmt.Errorf("failed to discover BuildData assets: %v", err)
	}

	sort.Strings(paths)
	return paths, nil
}

func decodeUnityYAMLScalar(raw string) (string, error) {
	value := strings.TrimSpace(raw)
	if len(value) >= 2 && value[0] == '"' && value[len(value)-1] == '"' {
		decoded, err := strconv.Unquote(value)
		if err != nil {
			return "", fmt.Errorf("invalid double-quoted YAML scalar %q: %v", value, err)
		}
		return decoded, nil
	}
	if len(value) >= 2 && value[0] == '\'' && value[len(value)-1] == '\'' {
		return strings.ReplaceAll(value[1:len(value)-1], "''", "'"), nil
	}
	return value, nil
}

// replaceBuildDataField updates one top-level serialized BuildData field. User-provided values
// are inserted as quoted scalars, and never interpreted as regular-expression replacement text.
func replaceBuildDataField(document, fieldName, newValue string) (string, string, bool, error) {
	fieldPattern := regexp.MustCompile(
		`(?m)^([ \t]{2}` + regexp.QuoteMeta(fieldName) + `:[ \t]*)([^\r\n]*)(\r?)$`,
	)
	matches := fieldPattern.FindAllStringSubmatchIndex(document, -1)
	if len(matches) != 1 {
		return "", "", false, fmt.Errorf(
			"expected exactly one top-level %s field in a BuildData document, found %d",
			fieldName,
			len(matches),
		)
	}

	match := matches[0]
	currentValue, err := decodeUnityYAMLScalar(document[match[4]:match[5]])
	if err != nil {
		return "", "", false, fmt.Errorf("could not decode BuildData.%s: %v", fieldName, err)
	}
	if currentValue == newValue {
		return document, "", false, nil
	}

	encodedValue := strconv.Quote(newValue)
	updated := document[:match[0]] +
		document[match[2]:match[3]] +
		encodedValue +
		document[match[6]:match[7]] +
		document[match[1]:]
	detail := fmt.Sprintf("%s: %q -> %q", fieldName, currentValue, newValue)
	return updated, detail, true, nil
}

func updateBuildDataAssetContent(content []byte, scriptGUID, companyName, productName string) ([]byte, []string, error) {
	text := string(content)
	referencePattern := buildDataScriptReferencePattern(scriptGUID)
	documentHeaders := unityYAMLDocumentPattern.FindAllStringIndex(text, -1)
	boundaries := []int{0}
	for _, header := range documentHeaders {
		if header[0] > 0 {
			boundaries = append(boundaries, header[0])
		}
	}
	boundaries = append(boundaries, len(text))

	applicationIdentifier := "com." + companyName + "." + productName
	fields := []struct {
		name  string
		value string
	}{
		{name: "companyName", value: companyName},
		{name: "productName", value: productName},
		{name: "applicationIdentifier", value: applicationIdentifier},
	}

	var builder strings.Builder
	builder.Grow(len(text) + 64)
	var details []string
	matchedDocuments := 0
	for index := 0; index < len(boundaries)-1; index++ {
		document := text[boundaries[index]:boundaries[index+1]]
		if referencePattern.MatchString(document) {
			matchedDocuments++
			for _, field := range fields {
				updatedDocument, detail, changed, err := replaceBuildDataField(document, field.name, field.value)
				if err != nil {
					return nil, nil, err
				}
				document = updatedDocument
				if changed {
					details = append(details, detail)
				}
			}
		}
		builder.WriteString(document)
	}

	if matchedDocuments == 0 {
		return nil, nil, fmt.Errorf("asset did not contain a BuildData document with script GUID %s", scriptGUID)
	}
	return []byte(builder.String()), details, nil
}

// planBuildDataAssetUpdates validates every matching asset before any of them are written.
func planBuildDataAssetUpdates(projectRoot, companyName, productName string) ([]buildDataAssetUpdate, error) {
	scriptGUID, err := getBuildDataScriptGUID(projectRoot)
	if err != nil || scriptGUID == "" {
		return nil, err
	}

	assetPaths, err := findBuildDataAssets(projectRoot, scriptGUID)
	if err != nil {
		return nil, err
	}

	updates := make([]buildDataAssetUpdate, 0, len(assetPaths))
	for _, assetPath := range assetPaths {
		content, readErr := os.ReadFile(assetPath)
		if readErr != nil {
			return nil, fmt.Errorf("failed to read BuildData asset %s: %v", assetPath, readErr)
		}

		updatedContent, details, updateErr := updateBuildDataAssetContent(
			content,
			scriptGUID,
			companyName,
			productName,
		)
		if updateErr != nil {
			return nil, fmt.Errorf("failed to plan BuildData asset %s: %v", assetPath, updateErr)
		}
		if len(details) > 0 {
			updates = append(updates, buildDataAssetUpdate{
				Path:           assetPath,
				UpdatedContent: updatedContent,
				Details:        details,
			})
		}
	}

	return updates, nil
}

// ============================================================
// Backup
// ============================================================

// collectFilesToBackup returns the list of files that will be modified.
// Does NOT include the project folder itself (folder rename is easily reversible).
func collectFilesToBackup(projectRoot, oldName, newCompanyName, newAppName string) ([]string, error) {
	var files []string
	seen := make(map[string]bool)
	addIfExists := func(path string) {
		if _, err := os.Stat(path); err == nil && !seen[path] {
			files = append(files, path)
			seen[path] = true
		}
	}

	// Project folder meta file
	addIfExists(filepath.Join(projectRoot, "Assets", oldName+".meta"))

	// Asmdef files in project folder
	projectFolderPath := filepath.Join(projectRoot, "Assets", oldName)
	filepath.Walk(projectFolderPath, func(path string, info os.FileInfo, err error) error {
		if err == nil && !info.IsDir() && strings.HasSuffix(info.Name(), ".asmdef") {
			files = append(files, path)
			addIfExists(path + ".meta")
		}
		return nil
	})

	// Asmdef files outside project folder that reference old name
	wordRegex := regexp.MustCompile(`\b` + regexp.QuoteMeta(oldName) + `\b`)
	assetsPath := filepath.Join(projectRoot, "Assets")
	filepath.Walk(assetsPath, func(path string, info os.FileInfo, err error) error {
		if err != nil || info.IsDir() || !strings.HasSuffix(info.Name(), ".asmdef") {
			return nil
		}
		if strings.HasPrefix(path, projectFolderPath+string(os.PathSeparator)) {
			return nil // already handled above
		}
		content, rErr := os.ReadFile(path)
		if rErr == nil && wordRegex.Match(content) {
			files = append(files, path)
		}
		return nil
	})

	// Build profile assets are identified by the BuildData MonoScript GUID, not by file name.
	buildDataUpdates, err := planBuildDataAssetUpdates(projectRoot, newCompanyName, newAppName)
	if err != nil {
		return nil, err
	}
	for _, update := range buildDataUpdates {
		addIfExists(update.Path)
	}

	// Project configuration files
	addIfExists(filepath.Join(projectRoot, "ProjectSettings", "ProjectSettings.asset"))
	addIfExists(filepath.Join(projectRoot, "ProjectSettings", "EditorBuildSettings.asset"))

	return files, nil
}

func createBackup(projectRoot string, files []string) (string, error) {
	if len(files) == 0 {
		return "", nil
	}

	timestamp := time.Now().Format("2006-01-02_150405")
	backupDir := filepath.Join(projectRoot, backupDirName, timestamp)
	if err := os.MkdirAll(backupDir, 0755); err != nil {
		return "", fmt.Errorf("failed to create backup directory: %v", err)
	}

	for _, filePath := range files {
		relPath, err := filepath.Rel(projectRoot, filePath)
		if err != nil {
			relPath = filepath.Base(filePath)
		}
		destPath := filepath.Join(backupDir, relPath)
		if err := os.MkdirAll(filepath.Dir(destPath), 0755); err != nil {
			return "", fmt.Errorf("failed to create backup subdirectory for %s: %v", relPath, err)
		}
		if err := copyFile(filePath, destPath); err != nil {
			return "", fmt.Errorf("failed to backup %s: %v", relPath, err)
		}
	}

	cleanupOldBackups(filepath.Join(projectRoot, backupDirName))
	return backupDir, nil
}

func copyFile(src, dst string) error {
	data, err := os.ReadFile(src)
	if err != nil {
		return err
	}
	return os.WriteFile(dst, data, 0644)
}

// cleanupOldBackups keeps only the most recent backups
func cleanupOldBackups(backupBaseDir string) {
	entries, err := os.ReadDir(backupBaseDir)
	if err != nil {
		return
	}
	var dirs []string
	for _, entry := range entries {
		if entry.IsDir() {
			dirs = append(dirs, entry.Name())
		}
	}
	if len(dirs) <= maxBackupCount {
		return
	}
	sort.Strings(dirs)
	for _, dir := range dirs[:len(dirs)-maxBackupCount] {
		os.RemoveAll(filepath.Join(backupBaseDir, dir))
	}
}

// ============================================================
// Change Preview (Dry-Run)
// ============================================================

func previewChanges(projectRoot, oldName, newName, oldCompanyName, newCompanyName, oldAppName, newAppName string) ([]FileChange, error) {
	var changes []FileChange
	wordRegex := regexp.MustCompile(`\b` + regexp.QuoteMeta(oldName) + `\b`)

	// 1. Folder rename
	if oldName != newName {
		changes = append(changes, FileChange{
			Path:   filepath.Join("Assets", oldName),
			Action: "rename",
			Details: []string{
				fmt.Sprintf("Rename folder: Assets/%s -> Assets/%s", oldName, newName),
				fmt.Sprintf("Rename meta:   Assets/%s.meta -> Assets/%s.meta", oldName, newName),
			},
		})
	}

	// 2. Asmdef files in project folder
	projectFolderPath := filepath.Join(projectRoot, "Assets", oldName)
	filepath.Walk(projectFolderPath, func(path string, info os.FileInfo, err error) error {
		if err != nil || info.IsDir() || !strings.HasSuffix(info.Name(), ".asmdef") {
			return nil
		}
		relPath, _ := filepath.Rel(projectRoot, path)
		content, rErr := os.ReadFile(path)
		if rErr != nil {
			return nil
		}
		var details []string
		if wordRegex.MatchString(string(content)) {
			details = append(details, "Update name/references containing '"+oldName+"'")
		}
		newFileName := wordRegex.ReplaceAllString(info.Name(), newName)
		if newFileName != info.Name() {
			details = append(details, fmt.Sprintf("Rename file: %s -> %s", info.Name(), newFileName))
		}
		if len(details) > 0 {
			changes = append(changes, FileChange{Path: relPath, Action: "modify", Details: details})
		}
		return nil
	})

	// 3. Asmdef files outside project folder
	assetsPath := filepath.Join(projectRoot, "Assets")
	filepath.Walk(assetsPath, func(path string, info os.FileInfo, err error) error {
		if err != nil || info.IsDir() || !strings.HasSuffix(info.Name(), ".asmdef") {
			return nil
		}
		if strings.HasPrefix(path, projectFolderPath+string(os.PathSeparator)) {
			return nil
		}
		content, rErr := os.ReadFile(path)
		if rErr != nil {
			return nil
		}
		if wordRegex.MatchString(string(content)) {
			relPath, _ := filepath.Rel(projectRoot, path)
			changes = append(changes, FileChange{
				Path:    relPath,
				Action:  "modify",
				Details: []string{"Update references containing '" + oldName + "'"},
			})
		}
		return nil
	})

	// 4. BuildData assets
	buildDataUpdates, err := planBuildDataAssetUpdates(projectRoot, newCompanyName, newAppName)
	if err != nil {
		return nil, err
	}
	for _, update := range buildDataUpdates {
		relPath, relErr := filepath.Rel(projectRoot, update.Path)
		if relErr != nil {
			return nil, fmt.Errorf("failed to resolve BuildData asset path %s: %v", update.Path, relErr)
		}
		changes = append(changes, FileChange{
			Path:    relPath,
			Action:  "modify",
			Details: update.Details,
		})
	}

	// 5. ProjectSettings.asset
	{
		var details []string
		if oldCompanyName != newCompanyName {
			details = append(details, fmt.Sprintf("companyName: %s -> %s", oldCompanyName, newCompanyName))
		}
		if oldAppName != newAppName {
			details = append(details, fmt.Sprintf("productName: %s -> %s", oldAppName, newAppName))
		}
		if oldCompanyName != newCompanyName || oldAppName != newAppName {
			oldID := "com." + oldCompanyName + "." + oldAppName
			newID := "com." + newCompanyName + "." + newAppName
			details = append(details, fmt.Sprintf("applicationIdentifier: %s -> %s", oldID, newID))
		}
		if len(details) > 0 {
			changes = append(changes, FileChange{
				Path:    filepath.Join("ProjectSettings", "ProjectSettings.asset"),
				Action:  "modify",
				Details: details,
			})
		}
	}

	// 6. EditorBuildSettings.asset
	if oldName != newName {
		editorBuildSettingsPath := filepath.Join(projectRoot, "ProjectSettings", "EditorBuildSettings.asset")
		if content, err := os.ReadFile(editorBuildSettingsPath); err == nil {
			if strings.Contains(string(content), "Assets/"+oldName+"/") {
				changes = append(changes, FileChange{
					Path:   filepath.Join("ProjectSettings", "EditorBuildSettings.asset"),
					Action: "modify",
					Details: []string{
						fmt.Sprintf("Scene paths: Assets/%s/... -> Assets/%s/...", oldName, newName),
					},
				})
			}
		}
	}

	return changes, nil
}

func printPreview(log *Logger, changes []FileChange) {
	log.Println("\n=============================================")
	log.Println("  CHANGE PREVIEW")
	log.Println("=============================================")

	if len(changes) == 0 {
		log.Println("\nNo changes needed.")
		return
	}

	for i, c := range changes {
		log.Printf("\n[%d] %s (%s)\n", i+1, c.Path, c.Action)
		for _, detail := range c.Details {
			log.Printf("    -> %s\n", detail)
		}
	}

	log.Printf("\nTotal: %d file(s) will be affected.\n", len(changes))
}

// ============================================================
// Rename Operations
// ============================================================

// renameFolderAndMeta renames the specified folder and its meta file.
// Refuses to overwrite existing targets for safety (no os.RemoveAll).
func renameFolderAndMeta(oldFolderPath, newFolderPath string) error {
	// Safety: validate that both paths share the same parent directory
	oldParent := filepath.Dir(oldFolderPath)
	newParent := filepath.Dir(newFolderPath)
	if oldParent != newParent {
		return fmt.Errorf("safety check failed: target folder is not in the same parent directory")
	}

	if _, err := os.Stat(newFolderPath); err == nil {
		return fmt.Errorf(
			"target folder already exists: %s\n"+
				"  If this is from a failed previous run, please remove it manually and retry.\n"+
				"  Path: %s", filepath.Base(newFolderPath), newFolderPath)
	}

	if err := os.Rename(oldFolderPath, newFolderPath); err != nil {
		return fmt.Errorf("failed to rename folder: %v", err)
	}

	oldMetaPath := oldFolderPath + ".meta"
	newMetaPath := newFolderPath + ".meta"
	if _, err := os.Stat(oldMetaPath); err == nil {
		if _, err := os.Stat(newMetaPath); err == nil {
			return fmt.Errorf("target meta file already exists: %s", newMetaPath)
		}
		if err := os.Rename(oldMetaPath, newMetaPath); err != nil {
			return fmt.Errorf("failed to rename meta file (folder was already renamed): %v", err)
		}
	}

	return nil
}

// ============================================================
// Update Operations
// ============================================================

// updateAsmdefFilesInFolder updates all .asmdef files within the project folder.
// Uses word-boundary regex for precise replacement, preserving JSON formatting.
func updateAsmdefFilesInFolder(log *Logger, folderPath, oldProjectName, newProjectName string) error {
	if oldProjectName == newProjectName {
		return nil
	}
	wordRegex := regexp.MustCompile(`\b` + regexp.QuoteMeta(oldProjectName) + `\b`)
	var errors []string

	filepath.Walk(folderPath, func(path string, info os.FileInfo, err error) error {
		if err != nil || info.IsDir() || !strings.HasSuffix(info.Name(), ".asmdef") {
			return nil
		}

		content, readErr := os.ReadFile(path)
		if readErr != nil {
			errors = append(errors, fmt.Sprintf("failed to read %s: %v", path, readErr))
			return nil
		}

		contentStr := string(content)
		newContent := wordRegex.ReplaceAllString(contentStr, newProjectName)
		newFileName := wordRegex.ReplaceAllString(info.Name(), newProjectName)

		if newContent == contentStr && newFileName == info.Name() {
			return nil // nothing to do
		}

		// Validate the result is still valid JSON
		var jsonCheck interface{}
		if jsonErr := json.Unmarshal([]byte(newContent), &jsonCheck); jsonErr != nil {
			errors = append(errors, fmt.Sprintf("JSON validation failed after update for %s: %v", path, jsonErr))
			return nil
		}

		dir := filepath.Dir(path)
		newPath := filepath.Join(dir, newFileName)

		if err := os.WriteFile(newPath, []byte(newContent), 0644); err != nil {
			errors = append(errors, fmt.Sprintf("failed to write %s: %v", newPath, err))
			return nil
		}

		log.Printf("[OK] Updated asmdef: %s", filepath.Base(path))
		if newFileName != info.Name() {
			log.Printf(" -> %s", newFileName)
		}
		log.Println()

		// Clean up old file and rename meta if filename changed
		if newFileName != info.Name() && path != newPath {
			os.Remove(path)
			oldMetaPath := path + ".meta"
			newMetaPath := newPath + ".meta"
			if _, statErr := os.Stat(oldMetaPath); statErr == nil {
				if renameErr := os.Rename(oldMetaPath, newMetaPath); renameErr != nil {
					errors = append(errors, fmt.Sprintf("failed to rename meta: %s: %v", oldMetaPath, renameErr))
				}
			}
		}

		return nil
	})

	if len(errors) > 0 {
		return fmt.Errorf("encountered %d error(s): %s", len(errors), strings.Join(errors, "; "))
	}
	return nil
}

// updateAsmdefReferencesGlobally updates references in ALL .asmdef files across Assets/,
// excluding the project folder (which is handled by updateAsmdefFilesInFolder).
func updateAsmdefReferencesGlobally(log *Logger, assetsPath, projectFolderPath, oldProjectName, newProjectName string) error {
	if oldProjectName == newProjectName {
		return nil
	}
	wordRegex := regexp.MustCompile(`\b` + regexp.QuoteMeta(oldProjectName) + `\b`)
	var errors []string

	filepath.Walk(assetsPath, func(path string, info os.FileInfo, err error) error {
		if err != nil || info.IsDir() || !strings.HasSuffix(info.Name(), ".asmdef") {
			return nil
		}
		// Skip files inside the project folder (already handled)
		if strings.HasPrefix(path, projectFolderPath+string(os.PathSeparator)) {
			return nil
		}

		content, readErr := os.ReadFile(path)
		if readErr != nil {
			return nil
		}

		contentStr := string(content)
		newContent := wordRegex.ReplaceAllString(contentStr, newProjectName)
		if newContent == contentStr {
			return nil
		}

		// Validate JSON
		var jsonCheck interface{}
		if jsonErr := json.Unmarshal([]byte(newContent), &jsonCheck); jsonErr != nil {
			errors = append(errors, fmt.Sprintf("JSON validation failed for %s: %v", path, jsonErr))
			return nil
		}

		if writeErr := os.WriteFile(path, []byte(newContent), 0644); writeErr != nil {
			errors = append(errors, fmt.Sprintf("failed to write %s: %v", path, writeErr))
			return nil
		}

		relPath, _ := filepath.Rel(assetsPath, path)
		log.Printf("[OK] Updated external asmdef references: %s\n", relPath)
		return nil
	})

	if len(errors) > 0 {
		return fmt.Errorf("encountered %d error(s): %s", len(errors), strings.Join(errors, "; "))
	}
	return nil
}

// updateBuildDataAssets applies a fully validated update plan. Re-planning after the project
// folder rename ensures BuildData assets remain discoverable even when they live below that folder.
func updateBuildDataAssets(log *Logger, projectRoot, companyName, productName string) error {
	updates, err := planBuildDataAssetUpdates(projectRoot, companyName, productName)
	if err != nil {
		return err
	}
	if len(updates) == 0 {
		log.Println("[--] BuildData assets: no changes needed")
		return nil
	}

	for _, update := range updates {
		if err := os.WriteFile(update.Path, update.UpdatedContent, 0644); err != nil {
			return fmt.Errorf("failed to write BuildData asset %s: %v", update.Path, err)
		}
		persistedContent, readErr := os.ReadFile(update.Path)
		if readErr != nil {
			return fmt.Errorf("failed to verify BuildData asset %s: %v", update.Path, readErr)
		}
		if !bytes.Equal(persistedContent, update.UpdatedContent) {
			return fmt.Errorf("BuildData asset verification failed after writing: %s", update.Path)
		}
		relPath, relErr := filepath.Rel(projectRoot, update.Path)
		if relErr != nil {
			relPath = update.Path
		}
		log.Printf("[OK] Updated BuildData asset: %s\n", relPath)
	}

	return nil
}

// updateProjectSettings updates ProjectSettings.asset using exact value matching.
// Replaces companyName, productName, applicationIdentifier (by exact bundle ID),
// metroPackageName, and metroApplicationDescription.
func updateProjectSettings(log *Logger, filePath, oldCompanyName, newCompanyName, oldAppName, newAppName string) error {
	content, err := os.ReadFile(filePath)
	if err != nil {
		return err
	}

	text := string(content)
	modified := false

	// Replace companyName precisely (exact old value match)
	if oldCompanyName != newCompanyName {
		old := "companyName: " + oldCompanyName
		rep := "companyName: " + newCompanyName
		if strings.Contains(text, old) {
			text = strings.Replace(text, old, rep, 1)
			modified = true
		}
	}

	// Replace productName precisely
	if oldAppName != newAppName {
		old := "productName: " + oldAppName
		rep := "productName: " + newAppName
		if strings.Contains(text, old) {
			text = strings.Replace(text, old, rep, 1)
			modified = true
		}
	}

	// Replace applicationIdentifier by exact bundle ID string (all platforms at once)
	if oldCompanyName != newCompanyName || oldAppName != newAppName {
		oldAppID := "com." + oldCompanyName + "." + oldAppName
		newAppID := "com." + newCompanyName + "." + newAppName
		if strings.Contains(text, oldAppID) {
			text = strings.ReplaceAll(text, oldAppID, newAppID)
			modified = true
		}
	}

	// Replace metroPackageName precisely
	if oldAppName != newAppName {
		old := "metroPackageName: " + oldAppName
		rep := "metroPackageName: " + newAppName
		if strings.Contains(text, old) {
			text = strings.Replace(text, old, rep, 1)
			modified = true
		}
	}

	// Replace metroApplicationDescription precisely
	if oldAppName != newAppName {
		old := "metroApplicationDescription: " + oldAppName
		rep := "metroApplicationDescription: " + newAppName
		if strings.Contains(text, old) {
			text = strings.Replace(text, old, rep, 1)
			modified = true
		}
	}

	if !modified {
		log.Println("[--] ProjectSettings.asset: no changes needed")
		return nil
	}

	return os.WriteFile(filePath, []byte(text), 0644)
}

// updateEditorBuildSettings updates scene paths in EditorBuildSettings.asset
func updateEditorBuildSettings(log *Logger, filePath, oldProjectName, newProjectName string) error {
	if oldProjectName == newProjectName {
		log.Println("[--] EditorBuildSettings.asset: no changes needed")
		return nil
	}

	content, err := os.ReadFile(filePath)
	if err != nil {
		return err
	}

	text := string(content)
	oldPathPrefix := "Assets/" + oldProjectName + "/"
	newPathPrefix := "Assets/" + newProjectName + "/"

	if !strings.Contains(text, oldPathPrefix) {
		log.Println("[--] EditorBuildSettings.asset: no changes needed")
		return nil
	}

	text = strings.ReplaceAll(text, oldPathPrefix, newPathPrefix)
	return os.WriteFile(filePath, []byte(text), 0644)
}

// ============================================================
// Utilities
// ============================================================

func waitForKeyPress() {
	fmt.Println("\nPress Enter to continue...")
	stdinReader.ReadBytes('\n')
}

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

// ============================================================
// Entry Point
// ============================================================

func main() {
	projectRoot, err := findProjectRoot()
	if err != nil {
		fmt.Println("Error:", err)
		waitForKeyPress()
		return
	}
	fmt.Printf("Found Unity project root at: %s\n", projectRoot)

	// Initialize logger
	logPath := filepath.Join(projectRoot, "rename_project.log")
	log := NewLogger(logPath)
	defer log.Close()
	log.Printf("=== Rename Project Tool started at %s ===\n", time.Now().Format("2006-01-02 15:04:05"))

	// Get current project info (prefers state file for reliable re-runs)
	oldName, oldCompanyName, oldAppName, err := getCurrentProjectInfo(projectRoot)
	if err != nil {
		log.Printf("Error getting current project info: %v\n", err)
		waitForKeyPress()
		return
	}

	log.Println("\nCurrent project settings:")
	log.Printf("  Project Folder: %s\n", oldName)
	log.Printf("  Company Name:   %s\n", oldCompanyName)
	log.Printf("  App Name:       %s\n", oldAppName)
	waitForKeyPress()

	// Collect new names with immediate validation (press Enter to keep current)
	newProjectName := promptValidatedInput(1, "Project Name",
		"The folder name (Assets\\PROJECT_NAME) should only contain letters, numbers,\nunderscores (_), and dashes (-). It cannot start with a number or dash.",
		oldName)

	newCompanyName := promptValidatedInput(2, "Company Name",
		"The name should only contain letters, numbers, underscores (_), and dashes (-).\nIt cannot start with a number or dash.",
		oldCompanyName)

	newAppName := promptValidatedInput(3, "Application Name",
		"The name should only contain letters, numbers, underscores (_), and dashes (-).\nIt cannot start with a number or dash.",
		oldAppName)

	// Check if anything actually changed
	if newProjectName == oldName && newCompanyName == oldCompanyName && newAppName == oldAppName {
		clearScreen()
		log.Println("\nNo changes needed — all values are the same as current settings.")
		waitForKeyPress()
		return
	}

	// Preview all changes before execution
	clearScreen()
	changes, previewErr := previewChanges(projectRoot, oldName, newProjectName, oldCompanyName, newCompanyName, oldAppName, newAppName)
	if previewErr != nil {
		log.Printf("Error preparing change preview: %v\n", previewErr)
		waitForKeyPress()
		return
	}
	printPreview(log, changes)

	if len(changes) == 0 {
		waitForKeyPress()
		return
	}

	// Final confirmation
	fmt.Print("\nProceed with these changes? (y/N): ")
	confirm, _ := stdinReader.ReadString('\n')
	confirm = strings.TrimSpace(strings.ToLower(confirm))
	if confirm != "y" {
		log.Println("\nOperation cancelled by user.")
		waitForKeyPress()
		return
	}

	// Create backup of all affected files
	log.Println("\nCreating backup...")
	filesToBackup, collectErr := collectFilesToBackup(projectRoot, oldName, newCompanyName, newAppName)
	if collectErr != nil {
		log.Printf("Error collecting files for backup: %v\n", collectErr)
		waitForKeyPress()
		return
	}
	backupDir, backupErr := createBackup(projectRoot, filesToBackup)
	if backupErr != nil {
		log.Printf("Warning: backup failed: %v\n", backupErr)
		fmt.Print("Continue without backup? (y/N): ")
		cont, _ := stdinReader.ReadString('\n')
		cont = strings.TrimSpace(strings.ToLower(cont))
		if cont != "y" {
			log.Println("Operation cancelled.")
			waitForKeyPress()
			return
		}
	} else if backupDir != "" {
		log.Printf("[OK] Backup created at: %s\n", backupDir)
	}

	log.Println("\nExecuting changes...")

	// 1. Rename project folder + meta
	if oldName != newProjectName {
		oldFolderPath := filepath.Join(projectRoot, "Assets", oldName)
		newFolderPath := filepath.Join(projectRoot, "Assets", newProjectName)
		if err := renameFolderAndMeta(oldFolderPath, newFolderPath); err != nil {
			log.Printf("Error renaming folder: %v\n", err)
			waitForKeyPress()
			return
		}
		log.Printf("[OK] Renamed folder: Assets/%s -> Assets/%s\n", oldName, newProjectName)

		// Save partial state checkpoint: folder renamed, but company/app not yet updated.
		// This ensures re-runs can find the correct folder even if subsequent steps fail.
		partialState := &RenameState{
			ProjectFolder: newProjectName,
			CompanyName:   oldCompanyName,
			AppName:       oldAppName,
			RenamedAt:     time.Now().Format("2006-01-02 15:04:05"),
		}
		if sErr := saveState(projectRoot, partialState); sErr != nil {
			log.Printf("Warning: failed to save state checkpoint: %v\n", sErr)
		}
	}

	// 2. Update asmdef files in the project folder
	if oldName != newProjectName {
		newFolderPath := filepath.Join(projectRoot, "Assets", newProjectName)
		if err := updateAsmdefFilesInFolder(log, newFolderPath, oldName, newProjectName); err != nil {
			log.Printf("Warning: %v\n", err)
		}

		// 3. Update asmdef references globally (outside the project folder)
		assetsPath := filepath.Join(projectRoot, "Assets")
		if err := updateAsmdefReferencesGlobally(log, assetsPath, newFolderPath, oldName, newProjectName); err != nil {
			log.Printf("Warning: %v\n", err)
		}
	}

	// 4. Update all BuildData assets identified by the BuildData MonoScript GUID.
	if err := updateBuildDataAssets(log, projectRoot, newCompanyName, newAppName); err != nil {
		log.Printf("Error updating BuildData assets: %v\n", err)
		waitForKeyPress()
		return
	}

	// 5. Update ProjectSettings.asset
	projectSettingsPath := filepath.Join(projectRoot, "ProjectSettings", "ProjectSettings.asset")
	if err := updateProjectSettings(log, projectSettingsPath, oldCompanyName, newCompanyName, oldAppName, newAppName); err != nil {
		log.Printf("Error updating ProjectSettings.asset: %v\n", err)
		waitForKeyPress()
		return
	}
	log.Println("[OK] Updated ProjectSettings.asset")

	// 6. Update EditorBuildSettings.asset
	editorBuildSettingsPath := filepath.Join(projectRoot, "ProjectSettings", "EditorBuildSettings.asset")
	if err := updateEditorBuildSettings(log, editorBuildSettingsPath, oldName, newProjectName); err != nil {
		log.Printf("Error updating EditorBuildSettings.asset: %v\n", err)
		waitForKeyPress()
		return
	}
	log.Println("[OK] Updated EditorBuildSettings.asset")

	// 7. Save final state file for future re-runs
	finalState := &RenameState{
		ProjectFolder: newProjectName,
		CompanyName:   newCompanyName,
		AppName:       newAppName,
		RenamedAt:     time.Now().Format("2006-01-02 15:04:05"),
	}
	if err := saveState(projectRoot, finalState); err != nil {
		log.Printf("Warning: failed to save state file: %v\n", err)
	} else {
		log.Printf("[OK] Saved state file: %s\n", stateFileName)
	}

	// Summary
	log.Println("\n===========================================")
	log.Println("  Project successfully renamed!")
	log.Println("===========================================")
	log.Println("\nSummary:")
	if oldName != newProjectName {
		log.Printf("  Folder:  Assets/%s -> Assets/%s\n", oldName, newProjectName)
	}
	if oldCompanyName != newCompanyName {
		log.Printf("  Company: %s -> %s\n", oldCompanyName, newCompanyName)
	}
	if oldAppName != newAppName {
		log.Printf("  App:     %s -> %s\n", oldAppName, newAppName)
	}
	if backupDir != "" {
		log.Printf("  Backup:  %s\n", backupDir)
	}
	log.Printf("  Log:     %s\n", logPath)
	log.Println("\nPlease verify the changes in Unity Editor.")
	waitForKeyPress()
}
