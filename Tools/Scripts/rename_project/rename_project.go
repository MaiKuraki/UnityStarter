package rename_project

import (
	"bufio"
	"bytes"
	"crypto/rand"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"flag"
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
	stateFileName                 = ".rename_project.json"
	renameStateSchemaVersion      = 1
	backupDirName                 = ".rename_backup"
	projectLockDirName            = ".rename_project.lock"
	projectLockOwnerFileName      = "owner.json"
	maxBackupCount                = 5
	templateIdentityRegistryPath  = "ProjectSettings/TemplateIdentityRegistry.json"
	templateIdentitySchemaVersion = 1
	transactionJournalVersion     = 3
)

var projectTokenPattern = regexp.MustCompile(`^[a-zA-Z_][a-zA-Z0-9_]*$`)
var applicationIdentifierSegmentPattern = regexp.MustCompile(`^[a-z][a-z0-9]*$`)
var metaGUIDPattern = regexp.MustCompile(`(?m)^guid:\s*([0-9a-fA-F]{32})\s*$`)
var unityYAMLDocumentPattern = regexp.MustCompile(`(?m)^--- !u!`)
var sha256HexPattern = regexp.MustCompile(`^[0-9a-f]{64}$`)

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
	SchemaVersion         int    `json:"schemaVersion"`
	ProjectFolder         string `json:"projectFolder"`
	CompanyName           string `json:"companyName"`
	AppName               string `json:"appName"`
	ApplicationIdentifier string `json:"applicationIdentifier,omitempty"`
	RenamedAt             string `json:"renamedAt"`
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

type templateIdentityField struct {
	Name          string `json:"name"`
	ValueTemplate string `json:"valueTemplate"`
}

type templateIdentityTarget struct {
	ID                  string                  `json:"id"`
	Kind                string                  `json:"kind"`
	Scope               string                  `json:"scope"`
	Availability        string                  `json:"availability"`
	PresencePath        string                  `json:"presencePath"`
	Path                string                  `json:"path"`
	MinimumMatches      int                     `json:"minimumMatches,omitempty"`
	Fields              []templateIdentityField `json:"fields,omitempty"`
	Property            string                  `json:"property,omitempty"`
	ValueTemplate       string                  `json:"valueTemplate,omitempty"`
	ExpectedOccurrences int                     `json:"expectedOccurrences,omitempty"`
	Section             string                  `json:"section,omitempty"`
	Key                 string                  `json:"key,omitempty"`
}

type templateIdentityRegistry struct {
	SchemaVersion int                      `json:"schemaVersion"`
	WorkspaceRoot string                   `json:"workspaceRoot"`
	Targets       []templateIdentityTarget `json:"targets"`
}

type templateIdentityRegistrySnapshot struct {
	Registry                *templateIdentityRegistry
	WorkspaceRoot           string
	ContentSHA256           string
	ExternalWriteTargets    map[string]string
	DeclaredExternalTargets map[string]string
}

type templateIdentityValues struct {
	ProjectToken          string
	CompanyName           string
	ProductName           string
	ApplicationIdentifier string
	UnityProjectDirectory string
}

type RenameRequest struct {
	OldProjectName           string
	NewProjectName           string
	OldCompanyName           string
	NewCompanyName           string
	OldAppName               string
	NewAppName               string
	OldApplicationIdentifier string
	NewApplicationIdentifier string
}

type renameOperationKind string

const (
	renameOperationWrite renameOperationKind = "write"
	renameOperationMove  renameOperationKind = "move"
)

type renameOperation struct {
	Kind          renameOperationKind
	Source        string
	Target        string
	Content       []byte
	Mode          os.FileMode
	BeforeExists  bool
	BeforeHash    string
	AfterHash     string
	Details       []string
	OriginalIsDir bool
}

type RenamePlan struct {
	ProjectRoot                  string
	WorkspaceRoot                string
	TemplateIdentityRegistryHash string
	Request                      RenameRequest
	Changes                      []FileChange
	Operations                   []renameOperation
	FinalState                   RenameState
	ExternalWriteTargets         map[string]string
	SkippedTargets               []string
	Warnings                     []string
}

type renameFileSystem interface {
	Lstat(path string) (os.FileInfo, error)
	ReadFile(path string) ([]byte, error)
	OpenFile(path string, flag int, mode os.FileMode) (*os.File, error)
	CreateFileExclusiveSync(path string, data []byte, mode os.FileMode) error
	Mkdir(path string, mode os.FileMode) error
	ReadDir(path string) ([]os.DirEntry, error)
	Rename(oldPath, newPath string) error
	Remove(path string) error
	RemoveAll(path string) error
}

type osRenameFileSystem struct {
	root     *os.Root
	rootPath string
}

func newRootedRenameFileSystem(projectRoot string) (*osRenameFileSystem, error) {
	absoluteRoot, err := filepath.Abs(projectRoot)
	if err != nil {
		return nil, err
	}
	absoluteRoot = filepath.Clean(absoluteRoot)
	canonicalRoot, err := filepath.EvalSymlinks(absoluteRoot)
	if err != nil {
		return nil, err
	}
	canonicalRoot, err = filepath.Abs(canonicalRoot)
	if err != nil {
		return nil, err
	}
	canonicalRoot = filepath.Clean(canonicalRoot)
	root, err := os.OpenRoot(canonicalRoot)
	if err != nil {
		return nil, err
	}
	return &osRenameFileSystem{root: root, rootPath: canonicalRoot}, nil
}

func (fileSystem osRenameFileSystem) Close() error {
	if fileSystem.root == nil {
		return nil
	}
	return fileSystem.root.Close()
}

func (fileSystem osRenameFileSystem) rootedPath(path string) (string, error) {
	if fileSystem.root == nil {
		return path, nil
	}
	if !filepath.IsAbs(path) {
		return "", fmt.Errorf("rooted filesystem requires an absolute project path: %s", path)
	}
	cleanPath := filepath.Clean(path)
	relative, err := filepath.Rel(fileSystem.rootPath, cleanPath)
	if err != nil {
		return "", fmt.Errorf("failed to resolve rooted project path %s: %v", path, err)
	}
	if relative == ".." || strings.HasPrefix(relative, ".."+string(os.PathSeparator)) ||
		filepath.IsAbs(relative) {
		return "", fmt.Errorf("path is outside the rooted Unity project: %s", path)
	}
	return relative, nil
}

func (fileSystem osRenameFileSystem) Lstat(path string) (os.FileInfo, error) {
	rootedPath, err := fileSystem.rootedPath(path)
	if err != nil {
		return nil, err
	}
	if fileSystem.root != nil {
		return fileSystem.root.Lstat(rootedPath)
	}
	return os.Lstat(rootedPath)
}

func (fileSystem osRenameFileSystem) ReadFile(path string) ([]byte, error) {
	rootedPath, err := fileSystem.rootedPath(path)
	if err != nil {
		return nil, err
	}
	if fileSystem.root != nil {
		return fileSystem.root.ReadFile(rootedPath)
	}
	return os.ReadFile(rootedPath)
}

func (fileSystem osRenameFileSystem) OpenFile(
	path string,
	flag int,
	mode os.FileMode,
) (*os.File, error) {
	rootedPath, err := fileSystem.rootedPath(path)
	if err != nil {
		return nil, err
	}
	if fileSystem.root != nil {
		return fileSystem.root.OpenFile(rootedPath, flag, mode)
	}
	return os.OpenFile(rootedPath, flag, mode)
}

func (fileSystem osRenameFileSystem) CreateFileExclusiveSync(
	path string,
	data []byte,
	mode os.FileMode,
) error {
	file, err := fileSystem.OpenFile(path, os.O_CREATE|os.O_EXCL|os.O_WRONLY, mode)
	if err != nil {
		return err
	}
	if _, err = file.Write(data); err == nil {
		err = file.Sync()
	}
	closeErr := file.Close()
	if err != nil {
		return err
	}
	return closeErr
}

func (fileSystem osRenameFileSystem) Mkdir(path string, mode os.FileMode) error {
	rootedPath, err := fileSystem.rootedPath(path)
	if err != nil {
		return err
	}
	if fileSystem.root != nil {
		return fileSystem.root.Mkdir(rootedPath, mode)
	}
	return os.Mkdir(rootedPath, mode)
}

func (fileSystem osRenameFileSystem) ReadDir(path string) ([]os.DirEntry, error) {
	rootedPath, err := fileSystem.rootedPath(path)
	if err != nil {
		return nil, err
	}
	if fileSystem.root == nil {
		return os.ReadDir(rootedPath)
	}
	directory, err := fileSystem.root.Open(rootedPath)
	if err != nil {
		return nil, err
	}
	entries, readErr := directory.ReadDir(-1)
	closeErr := directory.Close()
	if readErr != nil {
		return nil, readErr
	}
	if closeErr != nil {
		return nil, closeErr
	}
	return entries, nil
}

func (fileSystem osRenameFileSystem) Rename(oldPath, newPath string) error {
	rootedOldPath, err := fileSystem.rootedPath(oldPath)
	if err != nil {
		return err
	}
	rootedNewPath, err := fileSystem.rootedPath(newPath)
	if err != nil {
		return err
	}
	if fileSystem.root != nil {
		return fileSystem.root.Rename(rootedOldPath, rootedNewPath)
	}
	return os.Rename(rootedOldPath, rootedNewPath)
}

func (fileSystem osRenameFileSystem) Remove(path string) error {
	rootedPath, err := fileSystem.rootedPath(path)
	if err != nil {
		return err
	}
	if fileSystem.root != nil {
		return fileSystem.root.Remove(rootedPath)
	}
	return os.Remove(rootedPath)
}

func (fileSystem osRenameFileSystem) RemoveAll(path string) error {
	rootedPath, err := fileSystem.rootedPath(path)
	if err != nil {
		return err
	}
	if fileSystem.root != nil {
		return fileSystem.root.RemoveAll(rootedPath)
	}
	return os.RemoveAll(rootedPath)
}

func acquireProjectLock(
	fileSystem renameFileSystem,
	projectRoot string,
) (*projectLock, error) {
	lockDirectory := filepath.Join(projectRoot, projectLockDirName)
	if err := fileSystem.Mkdir(lockDirectory, 0700); err != nil {
		if os.IsExist(err) {
			ownerPath := filepath.Join(lockDirectory, projectLockOwnerFileName)
			ownerInfo, lstatErr := fileSystem.Lstat(ownerPath)
			if lstatErr == nil && ownerInfo.Mode().IsRegular() &&
				ownerInfo.Mode()&os.ModeSymlink == 0 {
				ownerContent, readErr := fileSystem.ReadFile(ownerPath)
				var owner projectLockOwner
				if readErr == nil && json.Unmarshal(ownerContent, &owner) == nil {
					return nil, fmt.Errorf(
						"rename project lock is already held by PID %d since %s; stale locks require manual confirmation and deletion: %s",
						owner.PID,
						owner.CreatedAt,
						lockDirectory,
					)
				}
			}
			return nil, fmt.Errorf(
				"rename project lock already exists and cannot be claimed automatically; inspect and manually delete it only after confirming no rename process is active: %s",
				lockDirectory,
			)
		}
		return nil, fmt.Errorf("failed to acquire rename project lock: %v", err)
	}

	tokenBytes := make([]byte, 16)
	if _, err := rand.Read(tokenBytes); err != nil {
		_ = fileSystem.Remove(lockDirectory)
		return nil, fmt.Errorf("failed to create rename project lock owner token: %v", err)
	}
	owner := projectLockOwner{
		Token:     hex.EncodeToString(tokenBytes),
		PID:       os.Getpid(),
		CreatedAt: time.Now().UTC().Format(time.RFC3339Nano),
	}
	ownerContent, err := json.MarshalIndent(owner, "", "    ")
	if err != nil {
		_ = fileSystem.Remove(lockDirectory)
		return nil, err
	}
	ownerPath := filepath.Join(lockDirectory, projectLockOwnerFileName)
	if err := createExclusiveVerifiedFile(fileSystem, ownerPath, ownerContent, 0600); err != nil {
		_ = fileSystem.Remove(lockDirectory)
		return nil, fmt.Errorf("failed to persist rename project lock owner: %v", err)
	}
	return &projectLock{
		Directory: lockDirectory,
		OwnerPath: ownerPath,
		Token:     owner.Token,
	}, nil
}

func releaseProjectLock(fileSystem renameFileSystem, lock *projectLock) error {
	if lock == nil {
		return nil
	}
	if err := validateInternalRegularFile(fileSystem, lock.OwnerPath, ""); err != nil {
		return fmt.Errorf("cannot validate rename project lock owner: %v", err)
	}
	ownerContent, err := fileSystem.ReadFile(lock.OwnerPath)
	if err != nil {
		return fmt.Errorf("cannot verify rename project lock ownership: %v", err)
	}
	var owner projectLockOwner
	if err := json.Unmarshal(ownerContent, &owner); err != nil {
		return fmt.Errorf("cannot decode rename project lock owner: %v", err)
	}
	if owner.Token == "" || owner.Token != lock.Token {
		return fmt.Errorf("refusing to release a rename project lock owned by another process")
	}
	if err := validateInternalRegularFile(
		fileSystem,
		lock.OwnerPath,
		contentHash(ownerContent),
	); err != nil {
		return fmt.Errorf("rename project lock owner changed before release: %v", err)
	}
	if err := fileSystem.Remove(lock.OwnerPath); err != nil {
		return fmt.Errorf("failed to remove rename project lock owner: %v", err)
	}
	if err := fileSystem.Remove(lock.Directory); err != nil {
		_ = createExclusiveVerifiedFile(fileSystem, lock.OwnerPath, ownerContent, 0600)
		return fmt.Errorf("failed to remove rename project lock directory: %v", err)
	}
	return nil
}

type transactionJournalOperation struct {
	Kind           renameOperationKind `json:"kind"`
	Source         string              `json:"source,omitempty"`
	Target         string              `json:"target"`
	BackupPath     string              `json:"backupPath,omitempty"`
	StagePath      string              `json:"stagePath,omitempty"`
	OldTempPath    string              `json:"oldTempPath,omitempty"`
	OriginalExists bool                `json:"originalExists"`
	OriginalIsDir  bool                `json:"originalIsDir"`
	Mode           uint32              `json:"mode"`
	BeforeHash     string              `json:"beforeHash,omitempty"`
	AfterHash      string              `json:"afterHash,omitempty"`
	BackupHash     string              `json:"backupHash,omitempty"`
}

type projectLockOwner struct {
	Token     string `json:"token"`
	PID       int    `json:"pid"`
	CreatedAt string `json:"createdAt"`
}

type projectLock struct {
	Directory string
	OwnerPath string
	Token     string
}

type transactionJournal struct {
	Version                        int                           `json:"version"`
	Status                         string                        `json:"status"`
	ProjectRoot                    string                        `json:"projectRoot"`
	TemplateIdentityRegistrySHA256 string                        `json:"templateIdentityRegistrySha256"`
	RegistryWorkspaceRoot          string                        `json:"registryWorkspaceRoot"`
	ApprovedExternalTargets        []string                      `json:"approvedExternalTargets"`
	AppliedCount                   int                           `json:"appliedCount"`
	InProgress                     int                           `json:"inProgress"`
	Operations                     []transactionJournalOperation `json:"operations"`
}

// Logger writes to both stdout and a log file simultaneously
type Logger struct {
	file   *os.File
	writer io.Writer
}

// ============================================================
// Logger
// ============================================================

func NewLogger(fileSystem renameFileSystem, logPath string) (*Logger, error) {
	pathInfo, err := fileSystem.Lstat(logPath)
	var rotatedPath string
	var rotatedExpectedHash string
	if os.IsNotExist(err) {
		pathInfo = nil
	} else if err != nil {
		return nil, fmt.Errorf("failed to inspect log path %s: %v", logPath, err)
	} else {
		if !pathInfo.Mode().IsRegular() || pathInfo.Mode()&os.ModeSymlink != 0 {
			return nil, fmt.Errorf("refusing unsafe log path: %s", logPath)
		}
		existingContent, readErr := fileSystem.ReadFile(logPath)
		if readErr != nil {
			return nil, fmt.Errorf("failed to read existing log before safe rotation: %v", readErr)
		}
		rotatedExpectedHash = contentHash(existingContent)
		pathInfoAfterRead, lstatErr := fileSystem.Lstat(logPath)
		if lstatErr != nil || !pathInfoAfterRead.Mode().IsRegular() ||
			pathInfoAfterRead.Mode()&os.ModeSymlink != 0 {
			return nil, fmt.Errorf("existing log changed before safe rotation: %s", logPath)
		}
		tokenBytes := make([]byte, 16)
		if _, err := rand.Read(tokenBytes); err != nil {
			return nil, fmt.Errorf("failed to create safe log rotation token: %v", err)
		}
		rotatedPath = logPath + ".rotation-" + hex.EncodeToString(tokenBytes)
		if _, err := fileSystem.Lstat(rotatedPath); !os.IsNotExist(err) {
			return nil, fmt.Errorf("safe log rotation path is unavailable: %s", rotatedPath)
		}
		if err := fileSystem.Rename(logPath, rotatedPath); err != nil {
			return nil, fmt.Errorf("failed to rotate existing log entry safely: %v", err)
		}
		rotatedInfo, lstatErr := fileSystem.Lstat(rotatedPath)
		if lstatErr != nil || !rotatedInfo.Mode().IsRegular() ||
			rotatedInfo.Mode()&os.ModeSymlink != 0 {
			if _, currentErr := fileSystem.Lstat(logPath); os.IsNotExist(currentErr) {
				_ = fileSystem.Rename(rotatedPath, logPath)
			}
			return nil, fmt.Errorf("existing log entry changed during safe rotation: %s", logPath)
		}
		rotatedContent, readErr := fileSystem.ReadFile(rotatedPath)
		if readErr != nil || contentHash(rotatedContent) != rotatedExpectedHash {
			if _, currentErr := fileSystem.Lstat(logPath); os.IsNotExist(currentErr) {
				_ = fileSystem.Rename(rotatedPath, logPath)
			}
			return nil, fmt.Errorf("existing log content changed during safe rotation: %s", logPath)
		}
	}
	file, err := fileSystem.OpenFile(logPath, os.O_CREATE|os.O_EXCL|os.O_WRONLY, 0644)
	if err != nil {
		if rotatedPath != "" {
			if _, currentErr := fileSystem.Lstat(logPath); os.IsNotExist(currentErr) {
				_ = fileSystem.Rename(rotatedPath, logPath)
			}
		}
		return nil, fmt.Errorf("failed to open log file %s safely: %v", logPath, err)
	}
	handleInfo, statErr := file.Stat()
	currentPathInfo, lstatErr := fileSystem.Lstat(logPath)
	if statErr != nil || lstatErr != nil || !currentPathInfo.Mode().IsRegular() ||
		currentPathInfo.Mode()&os.ModeSymlink != 0 || !os.SameFile(handleInfo, currentPathInfo) {
		_ = file.Close()
		return nil, fmt.Errorf("log path changed while it was being opened safely: %s", logPath)
	}
	if rotatedPath != "" {
		rotatedInfo, rotationErr := fileSystem.Lstat(rotatedPath)
		if rotationErr != nil || !rotatedInfo.Mode().IsRegular() ||
			rotatedInfo.Mode()&os.ModeSymlink != 0 {
			_ = file.Close()
			return nil, fmt.Errorf("rotated log entry changed before cleanup: %s", rotatedPath)
		}
		rotatedContent, readErr := fileSystem.ReadFile(rotatedPath)
		if readErr != nil || contentHash(rotatedContent) != rotatedExpectedHash {
			_ = file.Close()
			return nil, fmt.Errorf("rotated log content changed before cleanup: %s", rotatedPath)
		}
		if err := fileSystem.Remove(rotatedPath); err != nil {
			_ = file.Close()
			return nil, fmt.Errorf("failed to remove safely rotated log entry: %v", err)
		}
	}
	return &Logger{file: file, writer: io.MultiWriter(os.Stdout, file)}, nil
}

func NewConsoleLogger() *Logger {
	return &Logger{writer: os.Stdout}
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
	// Schema version 0 is the legacy file written before versioning; it is compatible
	// and is upgraded to the current version on the next successful rename.
	if state.SchemaVersion != 0 && state.SchemaVersion != renameStateSchemaVersion {
		return nil, fmt.Errorf("unsupported state schema version %d in %s", state.SchemaVersion, statePath)
	}
	return &state, nil
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

func readUniqueTopLevelUnityYAMLScalar(content, fieldName string) (string, error) {
	pattern := regexp.MustCompile(
		`(?m)^[ \t]{2}` + regexp.QuoteMeta(fieldName) + `:[ \t]*([^\r\n]*)(?:\r?)$`,
	)
	matches := pattern.FindAllStringSubmatch(content, -1)
	if len(matches) != 1 {
		return "", fmt.Errorf(
			"expected exactly one top-level %s field, found %d",
			fieldName,
			len(matches),
		)
	}

	value, err := decodeUnityYAMLScalar(matches[0][1])
	if err != nil {
		return "", fmt.Errorf("could not decode %s: %v", fieldName, err)
	}
	return value, nil
}

// getCurrentProjectInfo reads authoritative display and identifier values from
// ProjectSettings. The state file is only a validated project-folder hint.
func getCurrentProjectInfo(projectRoot string) (string, string, string, string, error) {
	projectSettingsPath := filepath.Join(projectRoot, "ProjectSettings", "ProjectSettings.asset")
	projectSettingsBytes, err := os.ReadFile(projectSettingsPath)
	if err != nil {
		return "", "", "", "", fmt.Errorf("failed to read %s: %v", projectSettingsPath, err)
	}
	projectSettingsContent := string(projectSettingsBytes)

	companyName, err := readUniqueTopLevelUnityYAMLScalar(projectSettingsContent, "companyName")
	if err != nil {
		return "", "", "", "", fmt.Errorf("could not read companyName from %s: %v", projectSettingsPath, err)
	}
	appName, err := readUniqueTopLevelUnityYAMLScalar(projectSettingsContent, "productName")
	if err != nil {
		return "", "", "", "", fmt.Errorf("could not read productName from %s: %v", projectSettingsPath, err)
	}
	identifiers, err := readProjectApplicationIdentifiers(projectSettingsContent)
	if err != nil {
		return "", "", "", "", fmt.Errorf("could not read applicationIdentifier from %s: %v", projectSettingsPath, err)
	}
	applicationIdentifier := identifiers[0]
	for _, identifier := range identifiers[1:] {
		if identifier != applicationIdentifier {
			return "", "", "", "", fmt.Errorf(
				"ProjectSettings contains different per-platform application identifiers; normalize them explicitly before using this tool",
			)
		}
	}

	state, stateErr := loadState(projectRoot)
	if stateErr == nil && state.ProjectFolder != "" {
		if err := validateExistingProjectFolder(projectRoot, state.ProjectFolder); err != nil {
			return "", "", "", "", fmt.Errorf("invalid %s projectFolder: %v", stateFileName, err)
		}
		if state.CompanyName != companyName || state.AppName != appName {
			return "", "", "", "", fmt.Errorf(
				"%s display identity does not match ProjectSettings.asset",
				stateFileName,
			)
		}
		if state.ApplicationIdentifier != "" && state.ApplicationIdentifier != applicationIdentifier {
			return "", "", "", "", fmt.Errorf(
				"%s applicationIdentifier does not match ProjectSettings.asset",
				stateFileName,
			)
		}
		fmt.Printf("Loaded validated project folder from state file (%s)\n", stateFileName)
		return state.ProjectFolder, companyName, appName, applicationIdentifier, nil
	}
	if stateErr != nil && !os.IsNotExist(stateErr) {
		return "", "", "", "", stateErr
	}

	// Auto-detect the project folder when no state exists.
	projectName, err := findMainProjectFolder(projectRoot, appName)
	if err != nil {
		// Fallback: try to extract from EditorBuildSettings
		editorBuildSettingsPath := filepath.Join(projectRoot, "ProjectSettings", "EditorBuildSettings.asset")
		editorBuildSettingsBytes, readErr := os.ReadFile(editorBuildSettingsPath)
		if readErr != nil {
			return "", "", "", "", fmt.Errorf("auto-detection failed and fallback read error: %v", readErr)
		}
		editorBuildSettingsContent := string(editorBuildSettingsBytes)

		projectNameRegex := regexp.MustCompile(`path: Assets/([^/]+)/`)
		projectNameMatches := projectNameRegex.FindStringSubmatch(editorBuildSettingsContent)
		if len(projectNameMatches) < 2 {
			return "", "", "", "", fmt.Errorf("could not detect project folder: %v", err)
		}
		projectName = strings.TrimSpace(projectNameMatches[1])
		fmt.Printf("Using fallback detection: %s\n", projectName)
	}
	if err := validateExistingProjectFolder(projectRoot, projectName); err != nil {
		return "", "", "", "", err
	}

	return projectName, companyName, appName, applicationIdentifier, nil
}

// ============================================================
// User Input & Validation
// ============================================================

// promptValidatedInput prompts the user for a value with immediate validation.
// Pressing Enter keeps the current value.
func promptValidatedInput(
	stepNum int,
	label,
	description,
	currentValue string,
	validate func(string) error,
) string {
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

		if err := validate(input); err != nil {
			fmt.Printf("\nInvalid input: '%s'\n", input)
			fmt.Println(err)
			waitForKeyPress()
			continue
		}

		return input
	}
}

func validateProjectToken(value string) error {
	if !projectTokenPattern.MatchString(value) {
		return fmt.Errorf(
			"project name must start with a letter or underscore and contain only letters, numbers, and underscores",
		)
	}

	upper := strings.ToUpper(value)
	if upper == "CON" || upper == "PRN" || upper == "AUX" || upper == "NUL" ||
		regexp.MustCompile(`^(COM|LPT)[1-9]$`).MatchString(upper) {
		return fmt.Errorf("project name is reserved by Windows")
	}
	return nil
}

func isProtectedProjectFolder(value string) bool {
	for excluded := range excludedDirs {
		if strings.EqualFold(value, excluded) {
			return true
		}
	}
	return false
}

func validateExistingProjectFolder(projectRoot, value string) error {
	if err := validateProjectToken(value); err != nil {
		return err
	}
	if isProtectedProjectFolder(value) {
		return fmt.Errorf("project folder %q is reserved or protected", value)
	}
	folderPath := filepath.Join(projectRoot, "Assets", value)
	info, err := os.Lstat(folderPath)
	if err != nil {
		return fmt.Errorf("project folder is unavailable: %v", err)
	}
	if !info.IsDir() || info.Mode()&os.ModeSymlink != 0 {
		return fmt.Errorf("project folder must be a real directory: %s", folderPath)
	}
	metaInfo, err := os.Lstat(folderPath + ".meta")
	if err != nil || !metaInfo.Mode().IsRegular() || metaInfo.Mode()&os.ModeSymlink != 0 {
		return fmt.Errorf("project folder meta file is unavailable or unsafe: %s.meta", folderPath)
	}
	return nil
}

func validateDisplayName(label, value string) error {
	if value == "" {
		return fmt.Errorf("%s must not be empty", label)
	}
	if len([]rune(value)) > 128 {
		return fmt.Errorf("%s must not exceed 128 characters", label)
	}
	for _, character := range value {
		if character < 0x20 || character == 0x7f {
			return fmt.Errorf("%s must not contain control characters", label)
		}
	}
	return nil
}

func validateApplicationIdentifierSegment(label, value string) error {
	if !applicationIdentifierSegmentPattern.MatchString(value) {
		return fmt.Errorf(
			"%s must start with a lowercase ASCII letter and contain only lowercase ASCII letters and numbers for Android/iOS compatibility",
			label,
		)
	}
	return nil
}

func validateApplicationIdentifier(value string) error {
	segments := strings.Split(value, ".")
	if len(segments) < 2 {
		return fmt.Errorf("application identifier must contain at least two dot-separated segments")
	}
	for _, segment := range segments {
		if err := validateApplicationIdentifierSegment("application identifier segment", segment); err != nil {
			return fmt.Errorf("invalid Android/iOS application identifier %q: %v", value, err)
		}
	}
	return nil
}

func validateRenameRequest(request RenameRequest) error {
	if request.NewProjectName != request.OldProjectName {
		if err := validateProjectToken(request.NewProjectName); err != nil {
			return err
		}
		if isProtectedProjectFolder(request.NewProjectName) {
			return fmt.Errorf("project folder %q is reserved or protected", request.NewProjectName)
		}
	}

	if err := validateDisplayName("company name", request.NewCompanyName); err != nil {
		return err
	}
	if err := validateDisplayName("application name", request.NewAppName); err != nil {
		return err
	}
	if request.NewApplicationIdentifier != request.OldApplicationIdentifier {
		if err := validateApplicationIdentifier(request.NewApplicationIdentifier); err != nil {
			return err
		}
	}
	return nil
}

func decodeStrictJSON(content []byte, destination interface{}) error {
	decoder := json.NewDecoder(bytes.NewReader(content))
	decoder.DisallowUnknownFields()
	if err := decoder.Decode(destination); err != nil {
		return err
	}
	var trailing interface{}
	if err := decoder.Decode(&trailing); err != io.EOF {
		if err == nil {
			return fmt.Errorf("JSON contains more than one top-level value")
		}
		return fmt.Errorf("invalid trailing JSON content: %v", err)
	}
	return nil
}

func validateRegistryRelativePath(label, value string) error {
	if value == "" {
		return fmt.Errorf("%s must not be empty", label)
	}
	if filepath.IsAbs(value) || strings.Contains(value, `\`) {
		return fmt.Errorf("%s must be a portable relative path: %q", label, value)
	}
	clean := filepath.ToSlash(filepath.Clean(filepath.FromSlash(value)))
	if clean != value || clean == "." || clean == ".." || strings.HasPrefix(clean, "../") {
		return fmt.Errorf("%s is not a normalized contained path: %q", label, value)
	}
	return nil
}

func validateTemplate(value string) error {
	allowed := map[string]bool{
		"projectToken":          true,
		"companyName":           true,
		"productName":           true,
		"applicationIdentifier": true,
		"unityProjectDirectory": true,
	}
	placeholderPattern := regexp.MustCompile(`\$\{([a-zA-Z][a-zA-Z0-9]*)\}`)
	for _, match := range placeholderPattern.FindAllStringSubmatch(value, -1) {
		if !allowed[match[1]] {
			return fmt.Errorf("unsupported identity placeholder %q", match[0])
		}
	}
	remainder := placeholderPattern.ReplaceAllString(value, "")
	if strings.Contains(remainder, "${") {
		return fmt.Errorf("malformed identity placeholder in %q", value)
	}
	return nil
}

func renderIdentityTemplate(template string, values templateIdentityValues) (string, error) {
	if err := validateTemplate(template); err != nil {
		return "", err
	}
	replacer := strings.NewReplacer(
		"${projectToken}", values.ProjectToken,
		"${companyName}", values.CompanyName,
		"${productName}", values.ProductName,
		"${applicationIdentifier}", values.ApplicationIdentifier,
		"${unityProjectDirectory}", values.UnityProjectDirectory,
	)
	return replacer.Replace(template), nil
}

func validateTemplateIdentityTarget(target templateIdentityTarget) error {
	if !regexp.MustCompile(`^[a-z][a-z0-9-]*$`).MatchString(target.ID) {
		return fmt.Errorf("target id must use lowercase kebab-case")
	}
	if target.Scope != "project" && target.Scope != "workspace" {
		return fmt.Errorf("scope must be project or workspace")
	}
	if target.Availability != "required" && target.Availability != "optional" {
		return fmt.Errorf("availability must be explicitly required or optional")
	}
	if target.Availability == "optional" {
		if err := validateRegistryRelativePath("presencePath", target.PresencePath); err != nil {
			return err
		}
	} else if target.PresencePath != "" {
		return fmt.Errorf("required targets must not declare presencePath")
	}
	if err := validateRegistryRelativePath("path", target.Path); err != nil {
		return err
	}

	switch target.Kind {
	case "unityScriptAssets":
		if target.Scope != "project" {
			return fmt.Errorf("unityScriptAssets must use project scope")
		}
		if target.MinimumMatches < 0 || len(target.Fields) == 0 {
			return fmt.Errorf("unityScriptAssets requires fields and a non-negative minimumMatches")
		}
		fieldNames := make(map[string]bool)
		for _, field := range target.Fields {
			if !regexp.MustCompile(`^[a-zA-Z_][a-zA-Z0-9_]*$`).MatchString(field.Name) {
				return fmt.Errorf("invalid serialized field name %q", field.Name)
			}
			if fieldNames[field.Name] {
				return fmt.Errorf("duplicate serialized field %q", field.Name)
			}
			fieldNames[field.Name] = true
			if err := validateTemplate(field.ValueTemplate); err != nil {
				return fmt.Errorf("field %s: %v", field.Name, err)
			}
		}
		if target.Property != "" || target.ValueTemplate != "" || target.ExpectedOccurrences != 0 ||
			target.Section != "" || target.Key != "" {
			return fmt.Errorf("unityScriptAssets contains fields for another target kind")
		}
	case "jsonStringOccurrences":
		if target.Property == "" || target.ValueTemplate == "" || target.ExpectedOccurrences < 1 {
			return fmt.Errorf("jsonStringOccurrences requires property, valueTemplate, and positive expectedOccurrences")
		}
		if !regexp.MustCompile(`^[a-zA-Z_][a-zA-Z0-9_]*$`).MatchString(target.Property) {
			return fmt.Errorf("invalid JSON property %q", target.Property)
		}
		if err := validateTemplate(target.ValueTemplate); err != nil {
			return err
		}
		if target.MinimumMatches != 0 || len(target.Fields) != 0 || target.Section != "" || target.Key != "" {
			return fmt.Errorf("jsonStringOccurrences contains fields for another target kind")
		}
	case "iniValue":
		if target.Section == "" || target.Key == "" || target.ValueTemplate == "" {
			return fmt.Errorf("iniValue requires section, key, and valueTemplate")
		}
		if strings.ContainsAny(target.Section, "[]\r\n") ||
			!regexp.MustCompile(`^[a-zA-Z_][a-zA-Z0-9_.-]*$`).MatchString(target.Key) {
			return fmt.Errorf("invalid INI section or key")
		}
		if err := validateTemplate(target.ValueTemplate); err != nil {
			return err
		}
		if target.MinimumMatches != 0 || len(target.Fields) != 0 || target.Property != "" ||
			target.ExpectedOccurrences != 0 {
			return fmt.Errorf("iniValue contains fields for another target kind")
		}
	default:
		return fmt.Errorf("unsupported target kind %q", target.Kind)
	}
	return nil
}

func resolveRegistryTargetPath(
	projectRoot,
	workspaceRoot,
	scope,
	relativePath string,
) (string, error) {
	root := projectRoot
	if scope == "workspace" {
		root = workspaceRoot
	}
	path := filepath.Join(root, filepath.FromSlash(relativePath))
	if err := ensurePathInsideProject(root, path); err != nil {
		return "", err
	}
	return filepath.Clean(path), nil
}

func loadTemplateIdentityRegistry(
	projectRoot string,
) (*templateIdentityRegistrySnapshot, error) {
	registryPath := filepath.Join(projectRoot, filepath.FromSlash(templateIdentityRegistryPath))
	info, err := os.Lstat(registryPath)
	if err != nil {
		return nil, fmt.Errorf("template identity registry is required: %s: %v", registryPath, err)
	}
	if !info.Mode().IsRegular() || info.Mode()&os.ModeSymlink != 0 {
		return nil, fmt.Errorf("template identity registry must be a regular non-symlink file: %s", registryPath)
	}
	content, err := os.ReadFile(registryPath)
	if err != nil {
		return nil, fmt.Errorf("failed to read template identity registry: %v", err)
	}
	var registry templateIdentityRegistry
	if err := decodeStrictJSON(content, &registry); err != nil {
		return nil, fmt.Errorf("invalid template identity registry %s: %v", registryPath, err)
	}
	if registry.SchemaVersion != templateIdentitySchemaVersion {
		return nil, fmt.Errorf(
			"unsupported template identity registry schemaVersion %d",
			registry.SchemaVersion,
		)
	}
	if registry.WorkspaceRoot != "." && registry.WorkspaceRoot != ".." {
		return nil, fmt.Errorf("workspaceRoot must be exactly . or ..")
	}
	workspaceRoot, err := filepath.Abs(filepath.Join(projectRoot, filepath.FromSlash(registry.WorkspaceRoot)))
	if err != nil {
		return nil, fmt.Errorf("failed to resolve registry workspaceRoot: %v", err)
	}
	workspaceRoot = filepath.Clean(workspaceRoot)
	canonicalWorkspace, err := filepath.EvalSymlinks(workspaceRoot)
	if err != nil {
		return nil, fmt.Errorf("failed to canonicalize registry workspaceRoot: %v", err)
	}
	canonicalProject, err := filepath.EvalSymlinks(projectRoot)
	if err != nil {
		return nil, fmt.Errorf("failed to canonicalize Unity project root: %v", err)
	}
	if err := ensurePathInsideProject(canonicalWorkspace, canonicalProject); err != nil {
		return nil, fmt.Errorf("registry workspaceRoot does not contain the Unity project: %v", err)
	}
	if len(registry.Targets) == 0 {
		return nil, fmt.Errorf("template identity registry contains no targets")
	}
	ids := make(map[string]bool)
	externalTargets := make(map[string]string)
	declaredExternalTargets := make(map[string]string)
	for index, target := range registry.Targets {
		if err := validateTemplateIdentityTarget(target); err != nil {
			return nil, fmt.Errorf("invalid template identity target %d (%q): %v", index, target.ID, err)
		}
		if ids[target.ID] {
			return nil, fmt.Errorf("duplicate template identity target id %q", target.ID)
		}
		ids[target.ID] = true
		targetPath, err := resolveRegistryTargetPath(canonicalProject, canonicalWorkspace, target.Scope, target.Path)
		if err != nil {
			return nil, fmt.Errorf("target %q path is unsafe: %v", target.ID, err)
		}
		targetAvailable := true
		if target.Availability == "optional" {
			presencePath, err := resolveRegistryTargetPath(
				canonicalProject,
				canonicalWorkspace,
				target.Scope,
				target.PresencePath,
			)
			if err != nil {
				return nil, fmt.Errorf("target %q presencePath is unsafe: %v", target.ID, err)
			}
			presenceInfo, presenceErr := os.Lstat(presencePath)
			if os.IsNotExist(presenceErr) {
				targetAvailable = false
			} else if presenceErr != nil {
				return nil, fmt.Errorf("failed to inspect target %q presencePath: %v", target.ID, presenceErr)
			} else if !presenceInfo.IsDir() || presenceInfo.Mode()&os.ModeSymlink != 0 {
				return nil, fmt.Errorf(
					"target %q presencePath must be a real directory: %s",
					target.ID,
					presencePath,
				)
			}
		}
		if !isPathInside(targetPath, canonicalProject) {
			key := normalizedPathKey(targetPath)
			if existing, duplicated := declaredExternalTargets[key]; duplicated {
				return nil, fmt.Errorf(
					"template identity targets claim the same external path more than once: %s / %s",
					existing,
					targetPath,
				)
			}
			declaredExternalTargets[key] = targetPath
			if targetAvailable {
				externalTargets[key] = targetPath
			}
		}
	}
	return &templateIdentityRegistrySnapshot{
		Registry:                &registry,
		WorkspaceRoot:           canonicalWorkspace,
		ContentSHA256:           contentHash(content),
		ExternalWriteTargets:    externalTargets,
		DeclaredExternalTargets: declaredExternalTargets,
	}, nil
}

func getRegisteredUnityScriptGUID(scriptPath string) (string, error) {
	info, err := os.Lstat(scriptPath)
	if err != nil {
		return "", fmt.Errorf("registered Unity script path is unavailable: %s: %v", scriptPath, err)
	}
	if !info.Mode().IsRegular() || info.Mode()&os.ModeSymlink != 0 {
		return "", fmt.Errorf("registered Unity script must be a regular non-symlink file: %s", scriptPath)
	}
	metaPath := scriptPath + ".meta"
	metaInfo, err := os.Lstat(metaPath)
	if err != nil {
		return "", fmt.Errorf("registered Unity script meta file is unavailable: %s: %v", metaPath, err)
	}
	if !metaInfo.Mode().IsRegular() || metaInfo.Mode()&os.ModeSymlink != 0 {
		return "", fmt.Errorf("registered Unity script meta must be a regular non-symlink file: %s", metaPath)
	}
	content, err := os.ReadFile(metaPath)
	if err != nil {
		return "", fmt.Errorf("failed to read registered Unity script meta file %s: %v", metaPath, err)
	}
	matches := metaGUIDPattern.FindAllSubmatch(content, -1)
	if len(matches) != 1 || len(matches[0]) != 2 {
		return "", fmt.Errorf("expected exactly one valid Unity GUID in %s", metaPath)
	}
	return string(matches[0][1]), nil
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
func replaceBuildDataField(
	document,
	fieldName,
	expectedValue,
	newValue string,
) (string, string, bool, error) {
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
	if currentValue != expectedValue {
		return "", "", false, fmt.Errorf(
			"BuildData.%s changed after identity detection: expected %q, found %q",
			fieldName,
			expectedValue,
			currentValue,
		)
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

func updateBuildDataAssetContent(
	content []byte,
	scriptGUID,
	targetID string,
	fields []templateIdentityField,
	oldValues,
	newValues templateIdentityValues,
) ([]byte, []string, error) {
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

	var builder strings.Builder
	builder.Grow(len(text) + 64)
	var details []string
	matchedDocuments := 0
	for index := 0; index < len(boundaries)-1; index++ {
		document := text[boundaries[index]:boundaries[index+1]]
		if referencePattern.MatchString(document) {
			matchedDocuments++
			for _, field := range fields {
				expectedValue, err := renderIdentityTemplate(field.ValueTemplate, oldValues)
				if err != nil {
					return nil, nil, fmt.Errorf("target %q field %s old value: %v", targetID, field.Name, err)
				}
				newValue, err := renderIdentityTemplate(field.ValueTemplate, newValues)
				if err != nil {
					return nil, nil, fmt.Errorf("target %q field %s new value: %v", targetID, field.Name, err)
				}
				updatedDocument, detail, changed, err := replaceBuildDataField(
					document,
					field.Name,
					expectedValue,
					newValue,
				)
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
func planBuildDataAssetUpdates(
	projectRoot string,
	target templateIdentityTarget,
	scriptPath string,
	oldValues,
	newValues templateIdentityValues,
) ([]buildDataAssetUpdate, error) {
	scriptGUID, err := getRegisteredUnityScriptGUID(scriptPath)
	if err != nil {
		return nil, err
	}

	assetPaths, err := findBuildDataAssets(projectRoot, scriptGUID)
	if err != nil {
		return nil, err
	}
	if len(assetPaths) < target.MinimumMatches {
		return nil, fmt.Errorf(
			"template identity target %q expected at least %d matching assets for script GUID %s, found %d",
			target.ID,
			target.MinimumMatches,
			scriptGUID,
			len(assetPaths),
		)
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
			target.ID,
			target.Fields,
			oldValues,
			newValues,
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

func stripJSONLineComments(content []byte) ([]byte, error) {
	result := make([]byte, 0, len(content))
	inString := false
	escaped := false
	for index := 0; index < len(content); index++ {
		character := content[index]
		if inString {
			result = append(result, character)
			if escaped {
				escaped = false
			} else if character == '\\' {
				escaped = true
			} else if character == '"' {
				inString = false
			}
			continue
		}
		if character == '"' {
			inString = true
			result = append(result, character)
			continue
		}
		if character == '/' && index+1 < len(content) && content[index+1] == '/' {
			index += 2
			for index < len(content) && content[index] != '\n' && content[index] != '\r' {
				index++
			}
			if index < len(content) {
				result = append(result, content[index])
			}
			continue
		}
		result = append(result, character)
	}
	if inString {
		return nil, fmt.Errorf("unterminated JSON string")
	}
	return result, nil
}

func validateJSONWithLineComments(content []byte) error {
	stripped, err := stripJSONLineComments(content)
	if err != nil {
		return err
	}
	var document interface{}
	decoder := json.NewDecoder(bytes.NewReader(stripped))
	if err := decoder.Decode(&document); err != nil {
		return err
	}
	var trailing interface{}
	if err := decoder.Decode(&trailing); err != io.EOF {
		if err == nil {
			return fmt.Errorf("JSON contains more than one top-level value")
		}
		return err
	}
	return nil
}

type jsonCTokenKind uint8

const (
	jsonCTokenEnd jsonCTokenKind = iota
	jsonCTokenString
	jsonCTokenColon
	jsonCTokenOther
)

type jsonCToken struct {
	kind        jsonCTokenKind
	start       int
	end         int
	stringValue string
}

type jsonCStringPropertyOccurrence struct {
	valueStart int
	valueEnd   int
	value      string
}

func nextJSONCToken(content []byte, offset int) (jsonCToken, error) {
	for offset < len(content) {
		switch content[offset] {
		case ' ', '\t', '\r', '\n':
			offset++
			continue
		case '/':
			if offset+1 < len(content) && content[offset+1] == '/' {
				offset += 2
				for offset < len(content) && content[offset] != '\r' && content[offset] != '\n' {
					offset++
				}
				continue
			}
		}
		break
	}

	if offset >= len(content) {
		return jsonCToken{kind: jsonCTokenEnd, start: len(content), end: len(content)}, nil
	}

	start := offset
	if content[offset] == ':' {
		return jsonCToken{kind: jsonCTokenColon, start: start, end: start + 1}, nil
	}
	if content[offset] != '"' {
		return jsonCToken{kind: jsonCTokenOther, start: start, end: start + 1}, nil
	}

	offset++
	for offset < len(content) {
		switch content[offset] {
		case '\\':
			if offset+1 >= len(content) {
				return jsonCToken{}, fmt.Errorf("unterminated JSON escape at byte %d", offset)
			}
			offset += 2
		case '"':
			end := offset + 1
			var decoded string
			if err := json.Unmarshal(content[start:end], &decoded); err != nil {
				return jsonCToken{}, fmt.Errorf("invalid JSON string at byte %d: %v", start, err)
			}
			return jsonCToken{
				kind:        jsonCTokenString,
				start:       start,
				end:         end,
				stringValue: decoded,
			}, nil
		default:
			offset++
		}
	}

	return jsonCToken{}, fmt.Errorf("unterminated JSON string at byte %d", start)
}

func findRegisteredJSONStringProperties(
	content []byte,
	property string,
) ([]jsonCStringPropertyOccurrence, error) {
	if err := validateJSONWithLineComments(content); err != nil {
		return nil, fmt.Errorf("registered JSON is malformed: %v", err)
	}

	occurrences := make([]jsonCStringPropertyOccurrence, 0)
	offset := 0
	for {
		token, err := nextJSONCToken(content, offset)
		if err != nil {
			return nil, err
		}
		if token.kind == jsonCTokenEnd {
			return occurrences, nil
		}
		offset = token.end
		if token.kind != jsonCTokenString || token.stringValue != property {
			continue
		}

		colon, err := nextJSONCToken(content, token.end)
		if err != nil {
			return nil, err
		}
		if colon.kind != jsonCTokenColon {
			continue
		}

		value, err := nextJSONCToken(content, colon.end)
		if err != nil {
			return nil, err
		}
		if value.kind != jsonCTokenString {
			return nil, fmt.Errorf("property %q at byte %d is not a JSON string", property, token.start)
		}
		occurrences = append(occurrences, jsonCStringPropertyOccurrence{
			valueStart: value.start,
			valueEnd:   value.end,
			value:      value.stringValue,
		})
	}
}

func replaceRegisteredJSONStringOccurrences(
	content []byte,
	target templateIdentityTarget,
	expectedValue,
	newValue string,
) ([]byte, []string, error) {
	occurrences, err := findRegisteredJSONStringProperties(content, target.Property)
	if err != nil {
		return nil, nil, err
	}
	if len(occurrences) != target.ExpectedOccurrences {
		return nil, nil, fmt.Errorf(
			"target %q expected exactly %d %s string properties, found %d",
			target.ID,
			target.ExpectedOccurrences,
			target.Property,
			len(occurrences),
		)
	}
	for _, occurrence := range occurrences {
		if occurrence.value != expectedValue {
			return nil, nil, fmt.Errorf(
				"target %q %s changed after identity detection: expected %q, found %q",
				target.ID,
				target.Property,
				expectedValue,
				occurrence.value,
			)
		}
	}
	if expectedValue == newValue {
		return append([]byte(nil), content...), nil, nil
	}
	encodedValue, err := json.Marshal(newValue)
	if err != nil {
		return nil, nil, fmt.Errorf("target %q could not encode the replacement JSON string: %v", target.ID, err)
	}
	updated := append([]byte(nil), content...)
	for index := len(occurrences) - 1; index >= 0; index-- {
		occurrence := occurrences[index]
		replacement := make([]byte, 0, len(updated)-(occurrence.valueEnd-occurrence.valueStart)+len(encodedValue))
		replacement = append(replacement, updated[:occurrence.valueStart]...)
		replacement = append(replacement, encodedValue...)
		replacement = append(replacement, updated[occurrence.valueEnd:]...)
		updated = replacement
	}
	updatedOccurrences, err := findRegisteredJSONStringProperties(updated, target.Property)
	if err != nil {
		return nil, nil, fmt.Errorf("target %q postcondition failed: %v", target.ID, err)
	}
	if len(updatedOccurrences) != target.ExpectedOccurrences {
		return nil, nil, fmt.Errorf(
			"target %q postcondition expected exactly %d %s string properties, found %d",
			target.ID,
			target.ExpectedOccurrences,
			target.Property,
			len(updatedOccurrences),
		)
	}
	for _, occurrence := range updatedOccurrences {
		if occurrence.value != newValue {
			return nil, nil, fmt.Errorf(
				"target %q postcondition expected %s %q, found %q",
				target.ID,
				target.Property,
				newValue,
				occurrence.value,
			)
		}
	}
	detail := fmt.Sprintf(
		"%s (%d occurrences): %q -> %q",
		target.Property,
		len(occurrences),
		expectedValue,
		newValue,
	)
	return updated, []string{detail}, nil
}

func replaceRegisteredINIValue(
	content []byte,
	target templateIdentityTarget,
	expectedValue,
	newValue string,
) ([]byte, []string, error) {
	text := string(content)
	sectionPattern := regexp.MustCompile(
		`(?m)^[ \t]*\[` + regexp.QuoteMeta(target.Section) + `\][ \t]*(?:\r?)$`,
	)
	sections := sectionPattern.FindAllStringIndex(text, -1)
	if len(sections) != 1 {
		return nil, nil, fmt.Errorf(
			"target %q expected exactly one [%s] section, found %d",
			target.ID,
			target.Section,
			len(sections),
		)
	}
	sectionEnd := len(text)
	nextSectionPattern := regexp.MustCompile(`(?m)^[ \t]*\[[^\]\r\n]+\][ \t]*(?:\r?)$`)
	if next := nextSectionPattern.FindStringIndex(text[sections[0][1]:]); next != nil {
		sectionEnd = sections[0][1] + next[0]
	}
	sectionBody := text[sections[0][1]:sectionEnd]
	keyPattern := regexp.MustCompile(
		`(?m)^([ \t]*` + regexp.QuoteMeta(target.Key) + `[ \t]*=[ \t]*)([^\r\n]*)(\r?)$`,
	)
	matches := keyPattern.FindAllStringSubmatchIndex(sectionBody, -1)
	if len(matches) != 1 {
		return nil, nil, fmt.Errorf(
			"target %q expected exactly one %s key in [%s], found %d",
			target.ID,
			target.Key,
			target.Section,
			len(matches),
		)
	}
	match := matches[0]
	currentValue := strings.TrimSpace(sectionBody[match[4]:match[5]])
	if currentValue != expectedValue {
		return nil, nil, fmt.Errorf(
			"target %q %s changed after identity detection: expected %q, found %q",
			target.ID,
			target.Key,
			expectedValue,
			currentValue,
		)
	}
	if currentValue == newValue {
		return append([]byte(nil), content...), nil, nil
	}
	updatedBody := sectionBody[:match[0]] +
		sectionBody[match[2]:match[3]] +
		newValue +
		sectionBody[match[6]:match[7]] +
		sectionBody[match[1]:]
	updated := text[:sections[0][1]] + updatedBody + text[sectionEnd:]
	verified, _, err := replaceRegisteredINIValue(
		[]byte(updated),
		target,
		newValue,
		newValue,
	)
	if err != nil || !bytes.Equal(verified, []byte(updated)) {
		return nil, nil, fmt.Errorf("target %q INI postcondition failed: %v", target.ID, err)
	}
	detail := fmt.Sprintf("[%s] %s: %q -> %q", target.Section, target.Key, expectedValue, newValue)
	return []byte(updated), []string{detail}, nil
}

func identityTargetIsAvailable(
	projectRoot,
	workspaceRoot string,
	target templateIdentityTarget,
) (bool, string, error) {
	if target.Availability == "required" {
		return true, "", nil
	}
	presencePath, err := resolveRegistryTargetPath(
		projectRoot,
		workspaceRoot,
		target.Scope,
		target.PresencePath,
	)
	if err != nil {
		return false, "", err
	}
	info, err := os.Lstat(presencePath)
	if os.IsNotExist(err) {
		return false, fmt.Sprintf(
			"%s: optional component is absent (%s)",
			target.ID,
			projectRelativePath(workspaceRoot, presencePath),
		), nil
	}
	if err != nil {
		return false, "", fmt.Errorf("failed to inspect target %q presencePath: %v", target.ID, err)
	}
	if !info.IsDir() || info.Mode()&os.ModeSymlink != 0 {
		return false, "", fmt.Errorf(
			"target %q presencePath must be a real directory: %s",
			target.ID,
			presencePath,
		)
	}
	return true, "", nil
}

func planTemplateIdentityTargets(
	plan *RenamePlan,
	registry *templateIdentityRegistry,
	oldValues,
	newValues templateIdentityValues,
	targetClaims map[string]string,
) error {
	for _, target := range registry.Targets {
		available, skipReason, err := identityTargetIsAvailable(
			plan.ProjectRoot,
			plan.WorkspaceRoot,
			target,
		)
		if err != nil {
			return err
		}
		if !available {
			plan.SkippedTargets = append(plan.SkippedTargets, skipReason)
			continue
		}
		targetPath, err := resolveRegistryTargetPath(
			plan.ProjectRoot,
			plan.WorkspaceRoot,
			target.Scope,
			target.Path,
		)
		if err != nil {
			return fmt.Errorf("target %q path is unsafe: %v", target.ID, err)
		}
		info, err := os.Lstat(targetPath)
		if err != nil {
			return fmt.Errorf(
				"template identity target %q path drifted or is unavailable: %s: %v",
				target.ID,
				targetPath,
				err,
			)
		}
		if !info.Mode().IsRegular() || info.Mode()&os.ModeSymlink != 0 {
			return fmt.Errorf(
				"template identity target %q must be a regular non-symlink file: %s",
				target.ID,
				targetPath,
			)
		}

		switch target.Kind {
		case "unityScriptAssets":
			updates, err := planBuildDataAssetUpdates(
				plan.ProjectRoot,
				target,
				targetPath,
				oldValues,
				newValues,
			)
			if err != nil {
				return fmt.Errorf("template identity target %q: %v", target.ID, err)
			}
			for _, update := range updates {
				if err := appendPlannedWrite(
					plan,
					update.Path,
					update.UpdatedContent,
					0644,
					false,
					append([]string{"Identity registry target: " + target.ID}, update.Details...),
					targetClaims,
				); err != nil {
					return err
				}
			}
		case "jsonStringOccurrences", "iniValue":
			content, err := os.ReadFile(targetPath)
			if err != nil {
				return fmt.Errorf("failed to read template identity target %q: %v", target.ID, err)
			}
			expectedValue, err := renderIdentityTemplate(target.ValueTemplate, oldValues)
			if err != nil {
				return fmt.Errorf("target %q old value: %v", target.ID, err)
			}
			newValue, err := renderIdentityTemplate(target.ValueTemplate, newValues)
			if err != nil {
				return fmt.Errorf("target %q new value: %v", target.ID, err)
			}
			var updated []byte
			var details []string
			if target.Kind == "jsonStringOccurrences" {
				updated, details, err = replaceRegisteredJSONStringOccurrences(
					content,
					target,
					expectedValue,
					newValue,
				)
			} else {
				updated, details, err = replaceRegisteredINIValue(
					content,
					target,
					expectedValue,
					newValue,
				)
			}
			if err != nil {
				return err
			}
			if err := appendPlannedWrite(
				plan,
				targetPath,
				updated,
				0644,
				false,
				append([]string{"Identity registry target: " + target.ID}, details...),
				targetClaims,
			); err != nil {
				return err
			}
		default:
			return fmt.Errorf("unsupported template identity target kind %q", target.Kind)
		}
	}
	return nil
}

// ============================================================
// Backup
// ============================================================

// cleanupOldBackups keeps only the most recent backups
func cleanupOldBackups(fileSystem renameFileSystem, backupBaseDir string) {
	entries, err := fileSystem.ReadDir(backupBaseDir)
	if err != nil {
		return
	}
	var dirs []string
	for _, entry := range entries {
		path := filepath.Join(backupBaseDir, entry.Name())
		info, lstatErr := fileSystem.Lstat(path)
		if lstatErr == nil && info.IsDir() && info.Mode()&os.ModeSymlink == 0 {
			dirs = append(dirs, entry.Name())
		}
	}
	if len(dirs) <= maxBackupCount {
		return
	}
	sort.Strings(dirs)
	for _, dir := range dirs[:len(dirs)-maxBackupCount] {
		_ = fileSystem.RemoveAll(filepath.Join(backupBaseDir, dir))
	}
}

// ============================================================
// Transactional Rename Planning
// ============================================================

func contentHash(data []byte) string {
	sum := sha256.Sum256(data)
	return fmt.Sprintf("%x", sum[:])
}

func projectRelativePath(projectRoot, path string) string {
	relative, err := filepath.Rel(projectRoot, path)
	if err != nil {
		return path
	}
	return relative
}

func normalizedPathKey(path string) string {
	cleanPath := filepath.Clean(path)
	if runtime.GOOS == "windows" {
		return strings.ToLower(cleanPath)
	}
	return cleanPath
}

func pathsEqual(left, right string) bool {
	return normalizedPathKey(left) == normalizedPathKey(right)
}

func ensurePathInsideProject(projectRoot, path string) error {
	relative, err := filepath.Rel(projectRoot, path)
	if err != nil {
		return fmt.Errorf("failed to resolve path %s: %v", path, err)
	}
	if relative == ".." || strings.HasPrefix(relative, ".."+string(os.PathSeparator)) {
		return fmt.Errorf("path escaped the Unity project root: %s", path)
	}
	return nil
}

func ensurePlannedWriteTarget(plan *RenamePlan, target string) error {
	if isPathInside(target, plan.ProjectRoot) {
		return ensurePathInsideProject(plan.ProjectRoot, target)
	}
	approvedTarget, approved := plan.ExternalWriteTargets[normalizedPathKey(target)]
	if !approved || !pathsEqual(approvedTarget, target) {
		return fmt.Errorf("path is not a registry-approved external identity target: %s", target)
	}
	if err := ensurePathInsideProject(plan.WorkspaceRoot, target); err != nil {
		return fmt.Errorf("external identity target escaped the workspace root: %v", err)
	}
	return nil
}

func appendPlannedWrite(
	plan *RenamePlan,
	target string,
	content []byte,
	mode os.FileMode,
	allowCreate bool,
	details []string,
	targetClaims map[string]string,
) error {
	if err := ensurePlannedWriteTarget(plan, target); err != nil {
		return err
	}

	info, statErr := os.Lstat(target)
	beforeExists := statErr == nil
	var beforeContent []byte
	if beforeExists {
		if info.Mode()&os.ModeSymlink != 0 {
			return fmt.Errorf("refusing to rewrite symlink: %s", target)
		}
		if !info.Mode().IsRegular() {
			return fmt.Errorf("rewrite target is not a regular file: %s", target)
		}
		var readErr error
		beforeContent, readErr = os.ReadFile(target)
		if readErr != nil {
			return fmt.Errorf("failed to read rewrite target %s: %v", target, readErr)
		}
		if bytes.Equal(beforeContent, content) {
			return nil
		}
		mode = info.Mode().Perm()
	} else {
		if !os.IsNotExist(statErr) {
			return fmt.Errorf("failed to inspect rewrite target %s: %v", target, statErr)
		}
		if !allowCreate {
			return fmt.Errorf("required rewrite target does not exist: %s", target)
		}
	}

	key := normalizedPathKey(target)
	if existing, claimed := targetClaims[key]; claimed {
		return fmt.Errorf("multiple planned operations target %s and %s", existing, target)
	}
	targetClaims[key] = target

	operation := renameOperation{
		Kind:         renameOperationWrite,
		Target:       target,
		Content:      append([]byte(nil), content...),
		Mode:         mode,
		BeforeExists: beforeExists,
		Details:      append([]string(nil), details...),
	}
	operation.AfterHash = contentHash(content)
	if beforeExists {
		operation.BeforeHash = contentHash(beforeContent)
	}
	plan.Operations = append(plan.Operations, operation)

	action := "modify"
	if !beforeExists {
		action = "create"
	}
	plan.Changes = append(plan.Changes, FileChange{
		Path:    projectRelativePath(plan.ProjectRoot, target),
		Action:  action,
		Details: append([]string(nil), details...),
	})
	return nil
}

func appendPlannedMove(
	plan *RenamePlan,
	source,
	target string,
	details []string,
	targetClaims map[string]string,
) error {
	if err := ensurePathInsideProject(plan.ProjectRoot, source); err != nil {
		return err
	}
	if err := ensurePathInsideProject(plan.ProjectRoot, target); err != nil {
		return err
	}
	if filepath.Clean(source) == filepath.Clean(target) {
		return nil
	}
	if normalizedPathKey(source) == normalizedPathKey(target) {
		return fmt.Errorf("case-only rename is not supported safely: %s -> %s", source, target)
	}

	sourceInfo, err := os.Lstat(source)
	if err != nil {
		return fmt.Errorf("required rename source is unavailable %s: %v", source, err)
	}
	if sourceInfo.Mode()&os.ModeSymlink != 0 {
		return fmt.Errorf("refusing to rename symlink: %s", source)
	}
	if _, err := os.Lstat(target); err == nil {
		return fmt.Errorf("rename target already exists: %s", target)
	} else if !os.IsNotExist(err) {
		return fmt.Errorf("failed to inspect rename target %s: %v", target, err)
	}

	key := normalizedPathKey(target)
	if existing, claimed := targetClaims[key]; claimed {
		return fmt.Errorf("multiple planned operations target %s and %s", existing, target)
	}
	targetClaims[key] = target

	operation := renameOperation{
		Kind:          renameOperationMove,
		Source:        source,
		Target:        target,
		Mode:          sourceInfo.Mode().Perm(),
		BeforeExists:  true,
		OriginalIsDir: sourceInfo.IsDir(),
		Details:       append([]string(nil), details...),
	}
	if !sourceInfo.IsDir() {
		sourceContent, readErr := os.ReadFile(source)
		if readErr != nil {
			return fmt.Errorf("failed to read rename source %s: %v", source, readErr)
		}
		operation.BeforeHash = contentHash(sourceContent)
		for _, previousOperation := range plan.Operations {
			if previousOperation.Kind == renameOperationWrite &&
				normalizedPathKey(previousOperation.Target) == normalizedPathKey(source) {
				operation.BeforeHash = previousOperation.AfterHash
			}
		}
	}
	plan.Operations = append(plan.Operations, operation)
	plan.Changes = append(plan.Changes, FileChange{
		Path:    projectRelativePath(plan.ProjectRoot, source),
		Action:  "rename",
		Details: append([]string(nil), details...),
	})
	return nil
}

func collectAsmdefPaths(root string) ([]string, error) {
	var paths []string
	err := filepath.Walk(root, func(path string, info os.FileInfo, walkErr error) error {
		if walkErr != nil {
			return walkErr
		}
		if info.Mode()&os.ModeSymlink != 0 {
			return fmt.Errorf("refusing to inspect symlink while planning: %s", path)
		}
		if !info.IsDir() && strings.EqualFold(filepath.Ext(info.Name()), ".asmdef") {
			paths = append(paths, path)
		}
		return nil
	})
	if err != nil {
		return nil, err
	}
	sort.Strings(paths)
	return paths, nil
}

func isPathInside(path, parent string) bool {
	relative, err := filepath.Rel(parent, path)
	if err != nil {
		return false
	}
	return relative != ".." && !strings.HasPrefix(relative, ".."+string(os.PathSeparator))
}

func replaceUniqueProjectSettingsScalar(
	text,
	fieldName,
	expectedValue,
	newValue string,
) (string, string, error) {
	pattern := regexp.MustCompile(
		`(?m)^([ \t]{2}` + regexp.QuoteMeta(fieldName) + `:[ \t]*)([^\r\n]*)(\r?)$`,
	)
	matches := pattern.FindAllStringSubmatchIndex(text, -1)
	if len(matches) != 1 {
		return "", "", fmt.Errorf(
			"expected exactly one top-level %s field in ProjectSettings.asset, found %d",
			fieldName,
			len(matches),
		)
	}

	match := matches[0]
	currentValue, err := decodeUnityYAMLScalar(text[match[4]:match[5]])
	if err != nil {
		return "", "", fmt.Errorf("could not decode ProjectSettings.%s: %v", fieldName, err)
	}
	if currentValue != expectedValue {
		return "", "", fmt.Errorf(
			"ProjectSettings.%s changed after identity detection: expected %q, found %q",
			fieldName,
			expectedValue,
			currentValue,
		)
	}
	if currentValue == newValue {
		return text, "", nil
	}
	encodedValue := strconv.Quote(newValue)

	updated := text[:match[0]] +
		text[match[2]:match[3]] +
		encodedValue +
		text[match[6]:match[7]] +
		text[match[1]:]
	return updated, fmt.Sprintf("%s: %q -> %q", fieldName, currentValue, newValue), nil
}

func replaceOptionalProjectSettingsScalar(text, fieldName, newValue string) (string, string, error) {
	pattern := regexp.MustCompile(
		`(?m)^([ \t]{2}` + regexp.QuoteMeta(fieldName) + `:[ \t]*)([^\r\n]*)(\r?)$`,
	)
	matches := pattern.FindAllStringSubmatchIndex(text, -1)
	if len(matches) == 0 {
		return text, "", nil
	}
	if len(matches) != 1 {
		return "", "", fmt.Errorf(
			"expected at most one top-level %s field in ProjectSettings.asset, found %d",
			fieldName,
			len(matches),
		)
	}

	match := matches[0]
	currentValue, err := decodeUnityYAMLScalar(text[match[4]:match[5]])
	if err != nil {
		return "", "", fmt.Errorf("could not decode ProjectSettings.%s: %v", fieldName, err)
	}
	if currentValue == newValue {
		return text, "", nil
	}
	encodedValue := strconv.Quote(newValue)
	updated := text[:match[0]] +
		text[match[2]:match[3]] +
		encodedValue +
		text[match[6]:match[7]] +
		text[match[1]:]
	return updated, fmt.Sprintf("%s: %q -> %q", fieldName, currentValue, newValue), nil
}

func replaceProjectApplicationIdentifiers(text, newValue string) (string, []string, error) {
	headerPattern := regexp.MustCompile(`(?m)^[ \t]{2}applicationIdentifier:[ \t]*(?:\r?)$`)
	headers := headerPattern.FindAllStringIndex(text, -1)
	if len(headers) != 1 {
		return "", nil, fmt.Errorf(
			"expected exactly one applicationIdentifier block in ProjectSettings.asset, found %d",
			len(headers),
		)
	}

	start := headers[0][1]
	if start < len(text) && text[start] == '\n' {
		start++
	}
	position := start
	var builder strings.Builder
	var details []string
	entryCount := 0
	for position < len(text) {
		lineEnd := strings.IndexByte(text[position:], '\n')
		nextPosition := len(text)
		lineWithEnding := text[position:]
		if lineEnd >= 0 {
			nextPosition = position + lineEnd + 1
			lineWithEnding = text[position:nextPosition]
		}

		line := strings.TrimSuffix(lineWithEnding, "\n")
		carriageReturn := ""
		if strings.HasSuffix(line, "\r") {
			line = strings.TrimSuffix(line, "\r")
			carriageReturn = "\r"
		}
		if !strings.HasPrefix(line, "    ") {
			break
		}

		colon := strings.IndexByte(line[4:], ':')
		if colon < 1 {
			return "", nil, fmt.Errorf("malformed applicationIdentifier entry: %q", line)
		}
		colon += 4
		platform := strings.TrimSpace(line[4:colon])
		rawValue := strings.TrimSpace(line[colon+1:])
		currentValue, err := decodeUnityYAMLScalar(rawValue)
		if err != nil {
			return "", nil, fmt.Errorf(
				"could not decode applicationIdentifier.%s: %v",
				platform,
				err,
			)
		}

		builder.WriteString(line[:colon+1])
		builder.WriteString(" ")
		builder.WriteString(newValue)
		builder.WriteString(carriageReturn)
		if strings.HasSuffix(lineWithEnding, "\n") {
			builder.WriteByte('\n')
		}
		if currentValue != newValue {
			details = append(
				details,
				fmt.Sprintf("applicationIdentifier.%s: %q -> %q", platform, currentValue, newValue),
			)
		}
		entryCount++
		position = nextPosition
	}
	if entryCount == 0 {
		return "", nil, fmt.Errorf("applicationIdentifier block contains no platform entries")
	}

	return text[:start] + builder.String() + text[position:], details, nil
}

func readProjectApplicationIdentifiers(text string) ([]string, error) {
	headerPattern := regexp.MustCompile(`(?m)^[ \t]{2}applicationIdentifier:[ \t]*(?:\r?)$`)
	headers := headerPattern.FindAllStringIndex(text, -1)
	if len(headers) != 1 {
		return nil, fmt.Errorf(
			"expected exactly one applicationIdentifier block in ProjectSettings.asset, found %d",
			len(headers),
		)
	}

	start := headers[0][1]
	if start < len(text) && text[start] == '\n' {
		start++
	}
	position := start
	var values []string
	for position < len(text) {
		lineEnd := strings.IndexByte(text[position:], '\n')
		nextPosition := len(text)
		line := text[position:]
		if lineEnd >= 0 {
			nextPosition = position + lineEnd + 1
			line = text[position : position+lineEnd]
		}
		line = strings.TrimSuffix(line, "\r")
		if !strings.HasPrefix(line, "    ") {
			break
		}
		colon := strings.IndexByte(line[4:], ':')
		if colon < 1 {
			return nil, fmt.Errorf("malformed applicationIdentifier entry: %q", line)
		}
		colon += 4
		value, err := decodeUnityYAMLScalar(strings.TrimSpace(line[colon+1:]))
		if err != nil {
			return nil, err
		}
		values = append(values, value)
		position = nextPosition
	}
	if len(values) == 0 {
		return nil, fmt.Errorf("applicationIdentifier block contains no platform entries")
	}
	return values, nil
}

func planProjectSettingsContent(content []byte, request RenameRequest) ([]byte, []string, error) {
	text := string(content)
	currentCompany, err := readUniqueTopLevelUnityYAMLScalar(text, "companyName")
	if err != nil {
		return nil, nil, err
	}
	currentProduct, err := readUniqueTopLevelUnityYAMLScalar(text, "productName")
	if err != nil {
		return nil, nil, err
	}
	if currentCompany != request.OldCompanyName || currentProduct != request.OldAppName {
		return nil, nil, fmt.Errorf(
			"ProjectSettings identity changed after detection: expected %q/%q, found %q/%q",
			request.OldCompanyName,
			request.OldAppName,
			currentCompany,
			currentProduct,
		)
	}
	currentIdentifiers, err := readProjectApplicationIdentifiers(text)
	if err != nil {
		return nil, nil, err
	}
	for _, identifier := range currentIdentifiers {
		if identifier != request.OldApplicationIdentifier {
			return nil, nil, fmt.Errorf(
				"ProjectSettings applicationIdentifier changed after detection: expected %q, found %q",
				request.OldApplicationIdentifier,
				identifier,
			)
		}
	}

	var details []string
	if request.NewCompanyName != request.OldCompanyName {
		updated, detail, replaceErr := replaceUniqueProjectSettingsScalar(
			text,
			"companyName",
			request.OldCompanyName,
			request.NewCompanyName,
		)
		if replaceErr != nil {
			return nil, nil, replaceErr
		}
		text = updated
		if detail != "" {
			details = append(details, detail)
		}
	}
	if request.NewAppName != request.OldAppName {
		updated, detail, replaceErr := replaceUniqueProjectSettingsScalar(
			text,
			"productName",
			request.OldAppName,
			request.NewAppName,
		)
		if replaceErr != nil {
			return nil, nil, replaceErr
		}
		text = updated
		if detail != "" {
			details = append(details, detail)
		}
	}

	if request.NewApplicationIdentifier != request.OldApplicationIdentifier {
		if err := validateApplicationIdentifier(request.NewApplicationIdentifier); err != nil {
			return nil, nil, err
		}
		updated, identifierDetails, replaceErr := replaceProjectApplicationIdentifiers(
			text,
			request.NewApplicationIdentifier,
		)
		if replaceErr != nil {
			return nil, nil, replaceErr
		}
		text = updated
		details = append(details, identifierDetails...)
	}

	if request.NewAppName != request.OldAppName {
		for _, fieldName := range []string{"metroApplicationDescription"} {
			updatedText, detail, replaceErr := replaceOptionalProjectSettingsScalar(
				text,
				fieldName,
				request.NewAppName,
			)
			if replaceErr != nil {
				return nil, nil, replaceErr
			}
			text = updatedText
			if detail != "" {
				details = append(details, detail)
			}
		}
	}

	verifiedCompany, err := readUniqueTopLevelUnityYAMLScalar(text, "companyName")
	if err != nil || verifiedCompany != request.NewCompanyName {
		return nil, nil, fmt.Errorf("ProjectSettings companyName postcondition failed")
	}
	verifiedProduct, err := readUniqueTopLevelUnityYAMLScalar(text, "productName")
	if err != nil || verifiedProduct != request.NewAppName {
		return nil, nil, fmt.Errorf("ProjectSettings productName postcondition failed")
	}
	identifiers, readErr := readProjectApplicationIdentifiers(text)
	if readErr != nil {
		return nil, nil, readErr
	}
	for _, identifier := range identifiers {
		if identifier != request.NewApplicationIdentifier {
			return nil, nil, fmt.Errorf(
				"ProjectSettings applicationIdentifier postcondition failed: expected %q, found %q",
				request.NewApplicationIdentifier,
				identifier,
			)
		}
	}
	return []byte(text), details, nil
}

func buildRenamePlan(projectRoot string, request RenameRequest) (*RenamePlan, error) {
	if err := validateRenameRequest(request); err != nil {
		return nil, err
	}

	absoluteRoot, err := filepath.Abs(projectRoot)
	if err != nil {
		return nil, fmt.Errorf("failed to resolve Unity project root: %v", err)
	}
	absoluteRoot = filepath.Clean(absoluteRoot)
	canonicalRoot, err := filepath.EvalSymlinks(absoluteRoot)
	if err != nil {
		return nil, fmt.Errorf("failed to canonicalize Unity project root: %v", err)
	}
	absoluteRoot, err = filepath.Abs(canonicalRoot)
	if err != nil {
		return nil, fmt.Errorf("failed to resolve canonical Unity project root: %v", err)
	}
	absoluteRoot = filepath.Clean(absoluteRoot)
	if _, err := os.Stat(filepath.Join(absoluteRoot, "Assets")); err != nil {
		return nil, fmt.Errorf("Unity project Assets directory is unavailable: %v", err)
	}
	if _, err := os.Stat(filepath.Join(absoluteRoot, "ProjectSettings")); err != nil {
		return nil, fmt.Errorf("Unity project ProjectSettings directory is unavailable: %v", err)
	}
	identitySnapshot, err := loadTemplateIdentityRegistry(absoluteRoot)
	if err != nil {
		return nil, err
	}

	plan := &RenamePlan{
		ProjectRoot:                  absoluteRoot,
		WorkspaceRoot:                identitySnapshot.WorkspaceRoot,
		TemplateIdentityRegistryHash: identitySnapshot.ContentSHA256,
		Request:                      request,
		ExternalWriteTargets:         identitySnapshot.ExternalWriteTargets,
		FinalState: RenameState{
			SchemaVersion:         renameStateSchemaVersion,
			ProjectFolder:         request.NewProjectName,
			CompanyName:           request.NewCompanyName,
			AppName:               request.NewAppName,
			ApplicationIdentifier: request.NewApplicationIdentifier,
			RenamedAt:             time.Now().Format("2006-01-02 15:04:05"),
		},
	}
	if request.NewApplicationIdentifier == request.OldApplicationIdentifier {
		if identifierErr := validateApplicationIdentifier(request.NewApplicationIdentifier); identifierErr != nil {
			plan.Warnings = append(
				plan.Warnings,
				fmt.Sprintf(
					"Preserving legacy application identifier %q unchanged; choose a new lowercase identifier to satisfy current Android/iOS portability policy (%v).",
					request.NewApplicationIdentifier,
					identifierErr,
				),
			)
		}
	}
	targetClaims := make(map[string]string)

	if err := validateExistingProjectFolder(absoluteRoot, request.OldProjectName); err != nil {
		return nil, err
	}
	oldProjectFolder := filepath.Join(absoluteRoot, "Assets", request.OldProjectName)
	newProjectFolder := filepath.Join(absoluteRoot, "Assets", request.NewProjectName)
	if info, err := os.Lstat(oldProjectFolder); err != nil {
		return nil, fmt.Errorf("project folder is unavailable: %v", err)
	} else if !info.IsDir() || info.Mode()&os.ModeSymlink != 0 {
		return nil, fmt.Errorf("project folder must be a real directory: %s", oldProjectFolder)
	}

	if request.NewProjectName != request.OldProjectName {
		wordRegex := regexp.MustCompile(`\b` + regexp.QuoteMeta(request.OldProjectName) + `\b`)
		internalAsmdefs, err := collectAsmdefPaths(oldProjectFolder)
		if err != nil {
			return nil, fmt.Errorf("failed to inspect project asmdefs: %v", err)
		}
		for _, path := range internalAsmdefs {
			content, readErr := os.ReadFile(path)
			if readErr != nil {
				return nil, fmt.Errorf("failed to read asmdef %s: %v", path, readErr)
			}
			updatedContent := wordRegex.ReplaceAll(content, []byte(request.NewProjectName))
			newFileName := wordRegex.ReplaceAllString(filepath.Base(path), request.NewProjectName)
			if !bytes.Equal(content, updatedContent) {
				var jsonCheck interface{}
				if jsonErr := json.Unmarshal(updatedContent, &jsonCheck); jsonErr != nil {
					return nil, fmt.Errorf("planned asmdef is invalid JSON %s: %v", path, jsonErr)
				}
				if err := appendPlannedWrite(
					plan,
					path,
					updatedContent,
					0644,
					false,
					[]string{"Update assembly name and references"},
					targetClaims,
				); err != nil {
					return nil, err
				}
			}

			if newFileName != filepath.Base(path) {
				newPath := filepath.Join(filepath.Dir(path), newFileName)
				if err := appendPlannedMove(
					plan,
					path,
					newPath,
					[]string{fmt.Sprintf("Rename file: %s -> %s", filepath.Base(path), newFileName)},
					targetClaims,
				); err != nil {
					return nil, err
				}
				if err := appendPlannedMove(
					plan,
					path+".meta",
					newPath+".meta",
					[]string{"Preserve asmdef meta GUID while renaming"},
					targetClaims,
				); err != nil {
					return nil, err
				}
			}
		}

		assetsRoot := filepath.Join(absoluteRoot, "Assets")
		allAsmdefs, err := collectAsmdefPaths(assetsRoot)
		if err != nil {
			return nil, fmt.Errorf("failed to inspect global asmdef references: %v", err)
		}
		for _, path := range allAsmdefs {
			if isPathInside(path, oldProjectFolder) {
				continue
			}
			content, readErr := os.ReadFile(path)
			if readErr != nil {
				return nil, fmt.Errorf("failed to read external asmdef %s: %v", path, readErr)
			}
			updatedContent := wordRegex.ReplaceAll(content, []byte(request.NewProjectName))
			if bytes.Equal(content, updatedContent) {
				continue
			}
			var jsonCheck interface{}
			if jsonErr := json.Unmarshal(updatedContent, &jsonCheck); jsonErr != nil {
				return nil, fmt.Errorf("planned external asmdef is invalid JSON %s: %v", path, jsonErr)
			}
			if err := appendPlannedWrite(
				plan,
				path,
				updatedContent,
				0644,
				false,
				[]string{"Update external assembly references"},
				targetClaims,
			); err != nil {
				return nil, err
			}
		}
	}

	oldIdentityValues := templateIdentityValues{
		ProjectToken:          request.OldProjectName,
		CompanyName:           request.OldCompanyName,
		ProductName:           request.OldAppName,
		ApplicationIdentifier: request.OldApplicationIdentifier,
		UnityProjectDirectory: filepath.Base(absoluteRoot),
	}
	newIdentityValues := templateIdentityValues{
		ProjectToken:          request.NewProjectName,
		CompanyName:           request.NewCompanyName,
		ProductName:           request.NewAppName,
		ApplicationIdentifier: request.NewApplicationIdentifier,
		UnityProjectDirectory: filepath.Base(absoluteRoot),
	}
	if err := planTemplateIdentityTargets(
		plan,
		identitySnapshot.Registry,
		oldIdentityValues,
		newIdentityValues,
		targetClaims,
	); err != nil {
		return nil, err
	}

	projectSettingsPath := filepath.Join(
		absoluteRoot,
		"ProjectSettings",
		"ProjectSettings.asset",
	)
	projectSettingsContent, err := os.ReadFile(projectSettingsPath)
	if err != nil {
		return nil, fmt.Errorf("failed to read ProjectSettings.asset: %v", err)
	}
	updatedSettings, details, err := planProjectSettingsContent(projectSettingsContent, request)
	if err != nil {
		return nil, err
	}
	if err := appendPlannedWrite(
		plan,
		projectSettingsPath,
		updatedSettings,
		0644,
		false,
		details,
		targetClaims,
	); err != nil {
		return nil, err
	}

	if request.NewProjectName != request.OldProjectName {
		editorBuildSettingsPath := filepath.Join(
			absoluteRoot,
			"ProjectSettings",
			"EditorBuildSettings.asset",
		)
		content, err := os.ReadFile(editorBuildSettingsPath)
		if err != nil {
			return nil, fmt.Errorf("failed to read EditorBuildSettings.asset: %v", err)
		}
		oldPrefix := "Assets/" + request.OldProjectName + "/"
		newPrefix := "Assets/" + request.NewProjectName + "/"
		updatedContent := []byte(strings.ReplaceAll(string(content), oldPrefix, newPrefix))
		if err := appendPlannedWrite(
			plan,
			editorBuildSettingsPath,
			updatedContent,
			0644,
			false,
			[]string{fmt.Sprintf("Scene paths: %s... -> %s...", oldPrefix, newPrefix)},
			targetClaims,
		); err != nil {
			return nil, err
		}

		if err := appendPlannedMove(
			plan,
			oldProjectFolder,
			newProjectFolder,
			[]string{fmt.Sprintf(
				"Rename folder: Assets/%s -> Assets/%s",
				request.OldProjectName,
				request.NewProjectName,
			)},
			targetClaims,
		); err != nil {
			return nil, err
		}
		if err := appendPlannedMove(
			plan,
			oldProjectFolder+".meta",
			newProjectFolder+".meta",
			[]string{"Rename project folder meta while preserving its GUID"},
			targetClaims,
		); err != nil {
			return nil, err
		}
	}

	stateContent, err := json.MarshalIndent(&plan.FinalState, "", "    ")
	if err != nil {
		return nil, fmt.Errorf("failed to serialize final rename state: %v", err)
	}
	if err := appendPlannedWrite(
		plan,
		filepath.Join(absoluteRoot, stateFileName),
		stateContent,
		0644,
		true,
		[]string{"Commit final rename identity"},
		targetClaims,
	); err != nil {
		return nil, err
	}

	if len(plan.Operations) == 0 {
		return nil, fmt.Errorf("rename request produced no file operations")
	}
	return plan, nil
}

func printRenamePlan(log *Logger, plan *RenamePlan) {
	log.Println("\n=============================================")
	log.Println("  VALIDATED RENAME PLAN")
	log.Println("=============================================")
	for index, change := range plan.Changes {
		log.Printf("\n[%d] %s (%s)\n", index+1, change.Path, change.Action)
		for _, detail := range change.Details {
			log.Printf("    -> %s\n", detail)
		}
	}
	if len(plan.SkippedTargets) > 0 {
		log.Println("\nExplicit optional identity targets not installed:")
		for _, skipped := range plan.SkippedTargets {
			log.Printf("    -> %s\n", skipped)
		}
	}
	if len(plan.Warnings) > 0 {
		log.Println("\nWarnings:")
		for _, warning := range plan.Warnings {
			log.Printf("    -> %s\n", warning)
		}
	}
	log.Printf(
		"\nTotal: %d operation(s) across %d displayed change(s).\n",
		len(plan.Operations),
		len(plan.Changes),
	)
}

// ============================================================
// Transaction Execution And Recovery
// ============================================================

func fileExistsWithFS(fileSystem renameFileSystem, path string) (bool, error) {
	_, err := fileSystem.Lstat(path)
	if err == nil {
		return true, nil
	}
	if os.IsNotExist(err) {
		return false, nil
	}
	return false, err
}

func validateInternalRegularFile(
	fileSystem renameFileSystem,
	path,
	expectedHash string,
) error {
	info, err := fileSystem.Lstat(path)
	if err != nil {
		return err
	}
	if !info.Mode().IsRegular() || info.Mode()&os.ModeSymlink != 0 {
		return fmt.Errorf("transaction internal path is not a regular non-symlink file: %s", path)
	}
	content, err := fileSystem.ReadFile(path)
	if err != nil {
		return err
	}
	if expectedHash != "" && contentHash(content) != expectedHash {
		return fmt.Errorf("transaction internal file hash mismatch: %s", path)
	}
	infoAfterRead, err := fileSystem.Lstat(path)
	if err != nil || !infoAfterRead.Mode().IsRegular() || infoAfterRead.Mode()&os.ModeSymlink != 0 {
		return fmt.Errorf("transaction internal path changed during validation: %s", path)
	}
	return nil
}

func validateInternalDirectory(fileSystem renameFileSystem, path string) error {
	info, err := fileSystem.Lstat(path)
	if err != nil {
		return err
	}
	if !info.IsDir() || info.Mode()&os.ModeSymlink != 0 {
		return fmt.Errorf("transaction internal path is not a real directory: %s", path)
	}
	return nil
}

func createExclusiveVerifiedFile(
	fileSystem renameFileSystem,
	path string,
	content []byte,
	mode os.FileMode,
) error {
	if err := fileSystem.CreateFileExclusiveSync(path, content, mode); err != nil {
		return err
	}
	if err := validateInternalRegularFile(fileSystem, path, contentHash(content)); err != nil {
		return fmt.Errorf("failed to verify exclusively created internal file: %v", err)
	}
	return nil
}

func validateJournalGenerationFile(
	fileSystem renameFileSystem,
	path string,
) (string, error) {
	if err := validateInternalRegularFile(fileSystem, path, ""); err != nil {
		return "", err
	}
	content, err := fileSystem.ReadFile(path)
	if err != nil {
		return "", err
	}
	var journal transactionJournal
	if err := json.Unmarshal(content, &journal); err != nil ||
		(journal.Version != 2 && journal.Version != transactionJournalVersion) {
		return "", fmt.Errorf("invalid transaction journal generation: %s", path)
	}
	hash := contentHash(content)
	if err := validateInternalRegularFile(fileSystem, path, hash); err != nil {
		return "", err
	}
	return hash, nil
}

func writeTransactionJournal(
	fileSystem renameFileSystem,
	journalPath string,
	journal *transactionJournal,
) error {
	content, err := json.MarshalIndent(journal, "", "    ")
	if err != nil {
		return err
	}
	temporaryPath := journalPath + ".next"
	previousPath := journalPath + ".previous"
	expectedHash := contentHash(content)
	temporaryExists, err := fileExistsWithFS(fileSystem, temporaryPath)
	if err != nil {
		return err
	}
	if temporaryExists {
		if err := validateInternalRegularFile(fileSystem, temporaryPath, expectedHash); err != nil {
			return fmt.Errorf("unsafe or stale journal staging file: %v", err)
		}
	} else if err := createExclusiveVerifiedFile(fileSystem, temporaryPath, content, 0644); err != nil {
		return fmt.Errorf("failed to persist rename transaction journal exclusively: %v", err)
	}

	journalExists, err := fileExistsWithFS(fileSystem, journalPath)
	if err != nil {
		return err
	}
	if journalExists {
		journalHash, err := validateJournalGenerationFile(fileSystem, journalPath)
		if err != nil {
			return fmt.Errorf("unsafe current transaction journal: %v", err)
		}
		previousExists, err := fileExistsWithFS(fileSystem, previousPath)
		if err != nil {
			return err
		}
		if previousExists {
			previousHash, validateErr := validateJournalGenerationFile(fileSystem, previousPath)
			if validateErr != nil {
				return fmt.Errorf("unsafe previous transaction journal: %v", validateErr)
			}
			if err := validateInternalRegularFile(fileSystem, previousPath, previousHash); err != nil {
				return fmt.Errorf("previous transaction journal changed before rotation: %v", err)
			}
			if err := fileSystem.Remove(previousPath); err != nil {
				return fmt.Errorf("failed to rotate previous transaction journal: %v", err)
			}
		}
		if err := validateInternalRegularFile(fileSystem, journalPath, journalHash); err != nil {
			return fmt.Errorf("current transaction journal changed before rotation: %v", err)
		}
		if err := fileSystem.Rename(journalPath, previousPath); err != nil {
			return fmt.Errorf("failed to preserve previous transaction journal: %v", err)
		}
		if err := validateInternalRegularFile(fileSystem, previousPath, journalHash); err != nil {
			return fmt.Errorf("failed to verify rotated previous transaction journal: %v", err)
		}
	}
	if err := validateInternalRegularFile(fileSystem, temporaryPath, expectedHash); err != nil {
		return fmt.Errorf("journal staging file changed before commit: %v", err)
	}
	if err := fileSystem.Rename(temporaryPath, journalPath); err != nil {
		if journalExists {
			if _, validateErr := validateJournalGenerationFile(fileSystem, previousPath); validateErr == nil {
				_ = fileSystem.Rename(previousPath, journalPath)
			}
		}
		return fmt.Errorf("failed to commit rename transaction journal: %v", err)
	}
	if err := validateInternalRegularFile(fileSystem, journalPath, expectedHash); err != nil {
		return fmt.Errorf("committed rename transaction journal verification failed")
	}
	return nil
}

func sortedApprovedExternalTargets(targets map[string]string) []string {
	paths := make([]string, 0, len(targets))
	for _, path := range targets {
		paths = append(paths, path)
	}
	sort.Strings(paths)
	return paths
}

func createTransactionJournal(
	fileSystem renameFileSystem,
	plan *RenamePlan,
) (*transactionJournal, string, string, error) {
	backupBase := filepath.Join(plan.ProjectRoot, backupDirName)
	if info, err := fileSystem.Lstat(backupBase); err == nil {
		if info.Mode()&os.ModeSymlink != 0 || !info.IsDir() {
			return nil, "", "", fmt.Errorf("rename backup root must be a real directory: %s", backupBase)
		}
	} else if os.IsNotExist(err) {
		if err := fileSystem.Mkdir(backupBase, 0755); err != nil {
			return nil, "", "", fmt.Errorf("failed to create rename backup root atomically: %v", err)
		}
		if err := validateInternalDirectory(fileSystem, backupBase); err != nil {
			return nil, "", "", err
		}
	} else {
		return nil, "", "", fmt.Errorf("failed to inspect rename backup root: %v", err)
	}

	tokenBytes := make([]byte, 16)
	if _, err := rand.Read(tokenBytes); err != nil {
		return nil, "", "", fmt.Errorf("failed to create transaction token: %v", err)
	}
	token := fmt.Sprintf(
		"%s-%d-%s",
		time.Now().UTC().Format("20060102T150405.000000000Z"),
		os.Getpid(),
		hex.EncodeToString(tokenBytes),
	)
	transactionDirectory := filepath.Join(backupBase, token)
	filesDirectory := filepath.Join(transactionDirectory, "files")
	if err := fileSystem.Mkdir(transactionDirectory, 0755); err != nil {
		return nil, "", "", fmt.Errorf("failed to create exclusive transaction directory: %v", err)
	}
	if err := validateInternalDirectory(fileSystem, transactionDirectory); err != nil {
		return nil, "", "", err
	}
	if err := fileSystem.Mkdir(filesDirectory, 0755); err != nil {
		return nil, "", "", fmt.Errorf("failed to create exclusive transaction files directory: %v", err)
	}
	if err := validateInternalDirectory(fileSystem, filesDirectory); err != nil {
		return nil, "", "", err
	}

	journal := &transactionJournal{
		Version:                        transactionJournalVersion,
		Status:                         "prepared",
		ProjectRoot:                    plan.ProjectRoot,
		TemplateIdentityRegistrySHA256: plan.TemplateIdentityRegistryHash,
		RegistryWorkspaceRoot:          plan.WorkspaceRoot,
		ApprovedExternalTargets:        sortedApprovedExternalTargets(plan.ExternalWriteTargets),
		AppliedCount:                   0,
		InProgress:                     -1,
		Operations:                     make([]transactionJournalOperation, 0, len(plan.Operations)),
	}

	for index, operation := range plan.Operations {
		journalOperation := transactionJournalOperation{
			Kind:           operation.Kind,
			Source:         operation.Source,
			Target:         operation.Target,
			OriginalExists: operation.BeforeExists,
			OriginalIsDir:  operation.OriginalIsDir,
			Mode:           uint32(operation.Mode.Perm()),
			BeforeHash:     operation.BeforeHash,
			AfterHash:      operation.AfterHash,
		}
		if operation.Kind == renameOperationWrite {
			journalOperation.StagePath = fmt.Sprintf(
				"%s.rename-stage-%s-%03d",
				operation.Target,
				token,
				index,
			)
			journalOperation.OldTempPath = fmt.Sprintf(
				"%s.rename-old-%s-%03d",
				operation.Target,
				token,
				index,
			)
		}

		var backupSource string
		if operation.Kind == renameOperationWrite && operation.BeforeExists {
			backupSource = operation.Target
		} else if operation.Kind == renameOperationMove && !operation.OriginalIsDir {
			backupSource = operation.Source
		}
		if backupSource != "" {
			content, err := fileSystem.ReadFile(backupSource)
			if err != nil {
				return nil, "", "", fmt.Errorf(
					"failed to read transaction preimage %s: %v",
					backupSource,
					err,
				)
			}
			backupPath := filepath.Join(filesDirectory, fmt.Sprintf("%03d.before", index))
			if err := createExclusiveVerifiedFile(fileSystem, backupPath, content, operation.Mode.Perm()); err != nil {
				return nil, "", "", fmt.Errorf(
					"failed to persist transaction preimage %s: %v",
					backupSource,
					err,
				)
			}
			journalOperation.BackupPath = backupPath
			journalOperation.BackupHash = contentHash(content)
		}
		journal.Operations = append(journal.Operations, journalOperation)
	}

	journalPath := filepath.Join(transactionDirectory, "transaction.json")
	if err := writeTransactionJournal(fileSystem, journalPath, journal); err != nil {
		return nil, "", "", err
	}
	if err := validateTransactionJournal(fileSystem, plan.ProjectRoot, journalPath, journal); err != nil {
		return nil, "", "", fmt.Errorf("generated transaction journal failed validation: %v", err)
	}
	return journal, journalPath, transactionDirectory, nil
}

func verifyWritePrecondition(fileSystem renameFileSystem, operation renameOperation) error {
	exists, err := fileExistsWithFS(fileSystem, operation.Target)
	if err != nil {
		return err
	}
	if operation.BeforeExists {
		if !exists {
			return fmt.Errorf("planned source disappeared before commit: %s", operation.Target)
		}
		targetHash, err := readRegularFileHash(fileSystem, operation.Target)
		if err != nil {
			return err
		}
		if targetHash != operation.BeforeHash {
			return fmt.Errorf("planned source changed after preview: %s", operation.Target)
		}
	} else if exists {
		return fmt.Errorf("planned create target appeared after preview: %s", operation.Target)
	}
	return nil
}

func applyWriteOperation(
	fileSystem renameFileSystem,
	operation renameOperation,
	journalOperation transactionJournalOperation,
) error {
	if err := verifyWritePrecondition(fileSystem, operation); err != nil {
		return err
	}
	for _, temporaryPath := range []string{
		journalOperation.StagePath,
		journalOperation.OldTempPath,
	} {
		exists, err := fileExistsWithFS(fileSystem, temporaryPath)
		if err != nil {
			return err
		}
		if exists {
			return fmt.Errorf("transaction temporary path already exists: %s", temporaryPath)
		}
	}

	if err := createExclusiveVerifiedFile(
		fileSystem,
		journalOperation.StagePath,
		operation.Content,
		operation.Mode.Perm(),
	); err != nil {
		return fmt.Errorf("failed to stage %s: %v", operation.Target, err)
	}
	if operation.BeforeExists {
		oldTempExists, err := fileExistsWithFS(fileSystem, journalOperation.OldTempPath)
		if err != nil {
			return err
		}
		if oldTempExists {
			return fmt.Errorf("transaction oldTemp path appeared before use: %s", journalOperation.OldTempPath)
		}
		if err := validateInternalRegularFile(
			fileSystem,
			journalOperation.StagePath,
			operation.AfterHash,
		); err != nil {
			return err
		}
		targetHash, err := readRegularFileHash(fileSystem, operation.Target)
		if err != nil || targetHash != operation.BeforeHash {
			return fmt.Errorf("write target changed before oldTemp preservation: %s", operation.Target)
		}
		if err := fileSystem.Rename(operation.Target, journalOperation.OldTempPath); err != nil {
			return fmt.Errorf("failed to preserve current file %s: %v", operation.Target, err)
		}
		if err := validateInternalRegularFile(
			fileSystem,
			journalOperation.OldTempPath,
			operation.BeforeHash,
		); err != nil {
			return fmt.Errorf("failed to verify preserved write target: %v", err)
		}
	}
	if targetExists, err := fileExistsWithFS(fileSystem, operation.Target); err != nil {
		return err
	} else if targetExists {
		return fmt.Errorf("write target appeared before staged commit: %s", operation.Target)
	}
	if err := validateInternalRegularFile(
		fileSystem,
		journalOperation.StagePath,
		operation.AfterHash,
	); err != nil {
		return fmt.Errorf("write stage changed before commit: %v", err)
	}
	if err := fileSystem.Rename(journalOperation.StagePath, operation.Target); err != nil {
		return fmt.Errorf("failed to commit staged file %s: %v", operation.Target, err)
	}
	committedHash, err := readRegularFileHash(fileSystem, operation.Target)
	if err != nil || committedHash != operation.AfterHash {
		return fmt.Errorf("failed to verify committed staged file: %s", operation.Target)
	}
	return nil
}

func applyMoveOperation(fileSystem renameFileSystem, operation renameOperation) error {
	sourceInfo, err := fileSystem.Lstat(operation.Source)
	if err != nil {
		return fmt.Errorf("rename source changed after preview %s: %v", operation.Source, err)
	}
	if sourceInfo.IsDir() != operation.OriginalIsDir {
		return fmt.Errorf("rename source type changed after preview: %s", operation.Source)
	}
	if sourceInfo.Mode()&os.ModeSymlink != 0 {
		return fmt.Errorf("refusing to rename symlink: %s", operation.Source)
	}
	if !operation.OriginalIsDir {
		content, readErr := fileSystem.ReadFile(operation.Source)
		if readErr != nil {
			return fmt.Errorf("failed to verify rename source %s: %v", operation.Source, readErr)
		}
		if contentHash(content) != operation.BeforeHash {
			return fmt.Errorf("rename source changed after preview: %s", operation.Source)
		}
	}
	targetExists, err := fileExistsWithFS(fileSystem, operation.Target)
	if err != nil {
		return err
	}
	if targetExists {
		return fmt.Errorf("rename target appeared after preview: %s", operation.Target)
	}
	if err := fileSystem.Rename(operation.Source, operation.Target); err != nil {
		return fmt.Errorf(
			"failed to rename %s to %s: %v",
			operation.Source,
			operation.Target,
			err,
		)
	}
	return nil
}

func applyRenameOperation(
	fileSystem renameFileSystem,
	operation renameOperation,
	journalOperation transactionJournalOperation,
) error {
	switch operation.Kind {
	case renameOperationWrite:
		return applyWriteOperation(fileSystem, operation, journalOperation)
	case renameOperationMove:
		return applyMoveOperation(fileSystem, operation)
	default:
		return fmt.Errorf("unsupported rename operation kind: %s", operation.Kind)
	}
}

func removeRegularFileIfPresent(fileSystem renameFileSystem, path string) error {
	info, err := fileSystem.Lstat(path)
	if os.IsNotExist(err) {
		return nil
	}
	if err != nil {
		return err
	}
	if !info.Mode().IsRegular() || info.Mode()&os.ModeSymlink != 0 {
		return fmt.Errorf("refusing to remove non-regular transaction path: %s", path)
	}
	return fileSystem.Remove(path)
}

func restoreFileFromBackup(
	fileSystem renameFileSystem,
	journalOperation transactionJournalOperation,
) error {
	content, err := readVerifiedTransactionPreimage(fileSystem, journalOperation)
	if err != nil {
		return err
	}

	restoreStagePath := journalOperation.OldTempPath + ".restore-stage"
	preservedTargetPath := journalOperation.OldTempPath + ".restore-current"
	stageExists, err := fileExistsWithFS(fileSystem, restoreStagePath)
	if err != nil {
		return err
	}
	if stageExists {
		stageHash, readErr := readRegularFileHash(fileSystem, restoreStagePath)
		if readErr != nil || stageHash != journalOperation.BackupHash {
			return fmt.Errorf("unexpected rollback staging file: %s", restoreStagePath)
		}
	} else {
		if err := createExclusiveVerifiedFile(
			fileSystem,
			restoreStagePath,
			content,
			os.FileMode(journalOperation.Mode),
		); err != nil {
			return err
		}
	}

	targetExists, err := fileExistsWithFS(fileSystem, journalOperation.Target)
	if err != nil {
		return err
	}
	preservedExists, err := fileExistsWithFS(fileSystem, preservedTargetPath)
	if err != nil {
		return err
	}
	var targetHash string
	if targetExists {
		targetHash, err = readRegularFileHash(fileSystem, journalOperation.Target)
		if err != nil {
			return err
		}
		if targetHash == journalOperation.BackupHash {
			if preservedExists {
				preservedHash, readErr := readRegularFileHash(fileSystem, preservedTargetPath)
				if readErr != nil || preservedHash != journalOperation.AfterHash {
					return fmt.Errorf("unexpected rollback preservation file: %s", preservedTargetPath)
				}
				if err := removeRegularFileIfPresent(fileSystem, preservedTargetPath); err != nil {
					return err
				}
			}
			return removeRegularFileIfPresent(fileSystem, restoreStagePath)
		}
		if targetHash != journalOperation.AfterHash {
			return fmt.Errorf(
				"cannot restore backup because target is owned by another writer: %s",
				journalOperation.Target,
			)
		}
	}

	if preservedExists {
		if targetExists {
			return fmt.Errorf("rollback preservation path and target both exist: %s", journalOperation.Target)
		}
		preservedHash, readErr := readRegularFileHash(fileSystem, preservedTargetPath)
		if readErr != nil || preservedHash != journalOperation.AfterHash {
			return fmt.Errorf("unexpected rollback preservation file: %s", preservedTargetPath)
		}
	} else if targetExists {
		if err := validateInternalRegularFile(fileSystem, restoreStagePath, journalOperation.BackupHash); err != nil {
			return err
		}
		currentTargetHash, readErr := readRegularFileHash(fileSystem, journalOperation.Target)
		if readErr != nil || currentTargetHash != targetHash {
			return fmt.Errorf("rollback target changed before preservation: %s", journalOperation.Target)
		}
		if err := fileSystem.Rename(journalOperation.Target, preservedTargetPath); err != nil {
			return err
		}
		preservedHash, readErr := readRegularFileHash(fileSystem, preservedTargetPath)
		if readErr != nil || preservedHash != targetHash {
			_ = fileSystem.Rename(preservedTargetPath, journalOperation.Target)
			return fmt.Errorf("failed to verify preserved rollback target: %s", journalOperation.Target)
		}
	}

	if err := validateInternalRegularFile(fileSystem, restoreStagePath, journalOperation.BackupHash); err != nil {
		return err
	}
	if err := fileSystem.Rename(restoreStagePath, journalOperation.Target); err != nil {
		if !preservedExists && targetExists {
			_ = fileSystem.Rename(preservedTargetPath, journalOperation.Target)
		}
		return err
	}
	restoredHash, err := readRegularFileHash(fileSystem, journalOperation.Target)
	if err != nil || restoredHash != journalOperation.BackupHash {
		return fmt.Errorf("failed to verify restored transaction preimage: %s", journalOperation.Target)
	}
	if preservedExists, existsErr := fileExistsWithFS(fileSystem, preservedTargetPath); existsErr != nil {
		return existsErr
	} else if preservedExists {
		expectedPreservedHash := targetHash
		if expectedPreservedHash == "" {
			expectedPreservedHash = journalOperation.AfterHash
		}
		if err := validateInternalRegularFile(fileSystem, preservedTargetPath, expectedPreservedHash); err != nil {
			return err
		}
	}
	if err := removeRegularFileIfPresent(fileSystem, preservedTargetPath); err != nil {
		return err
	}
	return nil
}

func validateRollbackPreimages(
	fileSystem renameFileSystem,
	journal *transactionJournal,
) error {
	for index, operation := range journal.Operations {
		if operation.BackupPath == "" {
			continue
		}
		if _, err := readVerifiedTransactionPreimage(fileSystem, operation); err != nil {
			return fmt.Errorf("operation %d has an invalid transaction preimage: %v", index, err)
		}
	}
	return nil
}

func readVerifiedTransactionPreimage(
	fileSystem renameFileSystem,
	operation transactionJournalOperation,
) ([]byte, error) {
	recoveryPath := operation.Source
	if recoveryPath == "" {
		recoveryPath = operation.Target
	}
	if operation.BackupPath == "" || operation.BackupHash == "" {
		return nil, fmt.Errorf("transaction preimage is unavailable for %s", recoveryPath)
	}
	info, err := fileSystem.Lstat(operation.BackupPath)
	if err != nil {
		return nil, fmt.Errorf("failed to inspect transaction preimage: %v", err)
	}
	if !info.Mode().IsRegular() || info.Mode()&os.ModeSymlink != 0 {
		return nil, fmt.Errorf("transaction preimage is not a regular file: %s", operation.BackupPath)
	}
	content, err := fileSystem.ReadFile(operation.BackupPath)
	if err != nil {
		return nil, fmt.Errorf("failed to read transaction preimage: %v", err)
	}
	if contentHash(content) != operation.BackupHash {
		return nil, fmt.Errorf("transaction preimage hash mismatch for %s", recoveryPath)
	}
	if err := validateInternalRegularFile(
		fileSystem,
		operation.BackupPath,
		operation.BackupHash,
	); err != nil {
		return nil, fmt.Errorf("transaction preimage changed during validation: %v", err)
	}
	return content, nil
}

func readRegularFileHash(
	fileSystem renameFileSystem,
	path string,
) (string, error) {
	info, err := fileSystem.Lstat(path)
	if err != nil {
		return "", err
	}
	if !info.Mode().IsRegular() || info.Mode()&os.ModeSymlink != 0 {
		return "", fmt.Errorf("expected a regular non-symlink file: %s", path)
	}
	content, err := fileSystem.ReadFile(path)
	if err != nil {
		return "", err
	}
	return contentHash(content), nil
}

func restoreRegularMoveSourceFromBackup(
	fileSystem renameFileSystem,
	operation transactionJournalOperation,
) error {
	content, err := readVerifiedTransactionPreimage(fileSystem, operation)
	if err != nil {
		return err
	}
	restoreStagePath := operation.BackupPath + ".move-restore-stage"
	stageInfo, stageErr := fileSystem.Lstat(restoreStagePath)
	switch {
	case stageErr == nil:
		if !stageInfo.Mode().IsRegular() || stageInfo.Mode()&os.ModeSymlink != 0 {
			return fmt.Errorf("unsafe regular-move restore staging path: %s", restoreStagePath)
		}
		stageHash, readErr := readRegularFileHash(fileSystem, restoreStagePath)
		if readErr != nil {
			return readErr
		}
		if stageHash != operation.BackupHash {
			if currentHash, hashErr := readRegularFileHash(fileSystem, restoreStagePath); hashErr != nil || currentHash != stageHash {
				return fmt.Errorf("regular-move restore stage changed before replacement: %s", restoreStagePath)
			}
			if err := fileSystem.Remove(restoreStagePath); err != nil {
				return fmt.Errorf("failed to replace partial regular-move restore stage: %v", err)
			}
			stageInfo = nil
		}
	case os.IsNotExist(stageErr):
		stageInfo = nil
	default:
		return stageErr
	}

	if stageInfo == nil {
		content, err = readVerifiedTransactionPreimage(fileSystem, operation)
		if err != nil {
			return err
		}
		if err := createExclusiveVerifiedFile(
			fileSystem,
			restoreStagePath,
			content,
			os.FileMode(operation.Mode),
		); err != nil {
			return fmt.Errorf("failed to stage regular-move source recovery: %v", err)
		}
	}
	stageHash, err := readRegularFileHash(fileSystem, restoreStagePath)
	if err != nil || stageHash != operation.BackupHash {
		return fmt.Errorf("failed to verify regular-move source recovery stage: %s", restoreStagePath)
	}

	sourceExists, err := fileExistsWithFS(fileSystem, operation.Source)
	if err != nil {
		return err
	}
	targetExists, err := fileExistsWithFS(fileSystem, operation.Target)
	if err != nil {
		return err
	}
	if sourceExists || targetExists {
		return fmt.Errorf(
			"regular-move paths changed while staging recovery: %s / %s",
			operation.Source,
			operation.Target,
		)
	}
	if err := validateInternalRegularFile(fileSystem, restoreStagePath, operation.BackupHash); err != nil {
		return err
	}
	if err := fileSystem.Rename(restoreStagePath, operation.Source); err != nil {
		return fmt.Errorf("failed to commit staged regular-move source recovery: %v", err)
	}
	restoredHash, err := readRegularFileHash(fileSystem, operation.Source)
	if err != nil || restoredHash != operation.BackupHash {
		return fmt.Errorf("failed to verify restored regular-move source: %s", operation.Source)
	}
	return nil
}

func rollbackRegularFileMove(
	fileSystem renameFileSystem,
	operation transactionJournalOperation,
) error {
	sourceExists, err := fileExistsWithFS(fileSystem, operation.Source)
	if err != nil {
		return err
	}
	targetExists, err := fileExistsWithFS(fileSystem, operation.Target)
	if err != nil {
		return err
	}

	if sourceExists && !targetExists {
		sourceHash, readErr := readRegularFileHash(fileSystem, operation.Source)
		if readErr != nil {
			return readErr
		}
		if sourceHash == operation.BeforeHash || sourceHash == operation.BackupHash {
			stagePath := operation.BackupPath + ".move-restore-stage"
			stageExists, err := fileExistsWithFS(fileSystem, stagePath)
			if err != nil {
				return err
			}
			if stageExists {
				if err := validateInternalRegularFile(fileSystem, stagePath, operation.BackupHash); err != nil {
					return fmt.Errorf("unsafe regular-move recovery stage: %v", err)
				}
			}
			return removeRegularFileIfPresent(
				fileSystem,
				stagePath,
			)
		}
		return fmt.Errorf(
			"cannot rollback regular-file move because source has unknown content: %s",
			operation.Source,
		)
	}
	if targetExists && !sourceExists {
		targetHash, readErr := readRegularFileHash(fileSystem, operation.Target)
		if readErr != nil {
			return readErr
		}
		if targetHash != operation.BeforeHash {
			return fmt.Errorf(
				"cannot rollback move because target changed externally: %s",
				operation.Target,
			)
		}
		if _, err := readVerifiedTransactionPreimage(fileSystem, operation); err != nil {
			return err
		}
		targetHash, readErr = readRegularFileHash(fileSystem, operation.Target)
		if readErr != nil || targetHash != operation.BeforeHash {
			return fmt.Errorf("regular-file move target changed before rollback: %s", operation.Target)
		}
		if sourceExists, err = fileExistsWithFS(fileSystem, operation.Source); err != nil {
			return err
		} else if sourceExists {
			return fmt.Errorf("regular-file move source appeared before rollback: %s", operation.Source)
		}
		if err := fileSystem.Rename(operation.Target, operation.Source); err != nil {
			return err
		}
		restoredHash, err := readRegularFileHash(fileSystem, operation.Source)
		if err != nil || restoredHash != operation.BeforeHash {
			targetExistsAfterMove, targetErr := fileExistsWithFS(fileSystem, operation.Target)
			sourceExistsAfterMove, sourceErr := fileExistsWithFS(fileSystem, operation.Source)
			if targetErr == nil && sourceErr == nil && !targetExistsAfterMove && sourceExistsAfterMove {
				_ = fileSystem.Rename(operation.Source, operation.Target)
			}
			return fmt.Errorf("failed to verify rolled-back regular-file move: %s", operation.Source)
		}
		return nil
	}
	if !sourceExists && !targetExists {
		return restoreRegularMoveSourceFromBackup(fileSystem, operation)
	}
	return fmt.Errorf(
		"cannot safely rollback move because both source and target exist: %s / %s",
		operation.Source,
		operation.Target,
	)
}

func rollbackWriteWithOldTemp(
	fileSystem renameFileSystem,
	operation transactionJournalOperation,
	targetExists bool,
) error {
	oldTempHash, err := readRegularFileHash(fileSystem, operation.OldTempPath)
	if err != nil {
		return fmt.Errorf("cannot validate write rollback oldTemp: %v", err)
	}
	oldTempMatchesPreimage := oldTempHash == operation.BeforeHash ||
		oldTempHash == operation.BackupHash

	if targetExists {
		targetHash, readErr := readRegularFileHash(fileSystem, operation.Target)
		if readErr != nil {
			return readErr
		}
		if targetHash != operation.AfterHash && targetHash != operation.BeforeHash &&
			targetHash != operation.BackupHash {
			return fmt.Errorf(
				"cannot rollback write because target is owned by another writer: %s",
				operation.Target,
			)
		}
	}

	if !oldTempMatchesPreimage {
		// The exact transaction path is recoverable only through the independently
		// verified backup. Its current content is never used as a restoration source.
		if _, err := readVerifiedTransactionPreimage(fileSystem, operation); err != nil {
			return fmt.Errorf("oldTemp is invalid and backup recovery is unavailable: %v", err)
		}
	}
	if err := restoreFileFromBackup(fileSystem, operation); err != nil {
		return err
	}
	restoredHash, err := readRegularFileHash(fileSystem, operation.Target)
	if err != nil || (restoredHash != operation.BeforeHash && restoredHash != operation.BackupHash) {
		return fmt.Errorf("failed to verify write rollback target: %s", operation.Target)
	}
	currentOldTempHash, err := readRegularFileHash(fileSystem, operation.OldTempPath)
	if err != nil {
		return fmt.Errorf("cannot revalidate write rollback oldTemp before cleanup: %v", err)
	}
	if currentOldTempHash != oldTempHash {
		return fmt.Errorf("write rollback oldTemp changed during recovery: %s", operation.OldTempPath)
	}
	if err := removeRegularFileIfPresent(fileSystem, operation.OldTempPath); err != nil {
		return err
	}
	return nil
}

func rollbackJournalOperation(
	fileSystem renameFileSystem,
	operation transactionJournalOperation,
) error {
	switch operation.Kind {
	case renameOperationWrite:
		stageExists, err := fileExistsWithFS(fileSystem, operation.StagePath)
		if err != nil {
			return err
		}
		if stageExists {
			if err := validateInternalRegularFile(fileSystem, operation.StagePath, operation.AfterHash); err != nil {
				return fmt.Errorf("unsafe write rollback stage: %v", err)
			}
		}
		targetExists, err := fileExistsWithFS(fileSystem, operation.Target)
		if err != nil {
			return err
		}
		if operation.OriginalExists {
			oldTempExists, err := fileExistsWithFS(fileSystem, operation.OldTempPath)
			if err != nil {
				return err
			}
			if oldTempExists {
				if err := rollbackWriteWithOldTemp(fileSystem, operation, targetExists); err != nil {
					return err
				}
			} else if !stageExists {
				if targetExists {
					targetHash, readErr := readRegularFileHash(fileSystem, operation.Target)
					if readErr != nil {
						return readErr
					}
					switch targetHash {
					case operation.BeforeHash:
						// The operation did not replace the original target.
					case operation.AfterHash:
						if err := restoreFileFromBackup(fileSystem, operation); err != nil {
							return err
						}
					default:
						return fmt.Errorf(
							"cannot rollback write because target changed externally: %s",
							operation.Target,
						)
					}
				} else if err := restoreFileFromBackup(fileSystem, operation); err != nil {
					return err
				}
			}
		} else if targetExists {
			if stageExists {
				return fmt.Errorf(
					"cannot rollback create because the target appeared before commit: %s",
					operation.Target,
				)
			}
			targetHash, readErr := readRegularFileHash(fileSystem, operation.Target)
			if readErr != nil {
				return readErr
			}
			if targetHash != operation.AfterHash {
				return fmt.Errorf(
					"cannot rollback create because target is owned by another writer: %s",
					operation.Target,
				)
			}
			if err := validateInternalRegularFile(fileSystem, operation.Target, operation.AfterHash); err != nil {
				return err
			}
			if err := removeRegularFileIfPresent(fileSystem, operation.Target); err != nil {
				return err
			}
		}
		if stageExists {
			if err := validateInternalRegularFile(fileSystem, operation.StagePath, operation.AfterHash); err != nil {
				return err
			}
		}
		if err := removeRegularFileIfPresent(fileSystem, operation.StagePath); err != nil {
			return err
		}
		return nil

	case renameOperationMove:
		if !operation.OriginalIsDir {
			return rollbackRegularFileMove(fileSystem, operation)
		}
		sourceExists, err := fileExistsWithFS(fileSystem, operation.Source)
		if err != nil {
			return err
		}
		targetExists, err := fileExistsWithFS(fileSystem, operation.Target)
		if err != nil {
			return err
		}
		if targetExists && !sourceExists {
			return fileSystem.Rename(operation.Target, operation.Source)
		}
		if sourceExists && !targetExists {
			return nil
		}
		if sourceExists && targetExists {
			return fmt.Errorf(
				"cannot safely rollback move because both source and target exist: %s / %s",
				operation.Source,
				operation.Target,
			)
		}
		return fmt.Errorf(
			"cannot rollback move because both source and target are missing: %s / %s",
			operation.Source,
			operation.Target,
		)
	default:
		return fmt.Errorf("unsupported journal operation kind: %s", operation.Kind)
	}
}

func rollbackTransaction(
	fileSystem renameFileSystem,
	journal *transactionJournal,
	journalPath string,
	log *Logger,
) error {
	if err := validateRollbackPreimages(fileSystem, journal); err != nil {
		journal.Status = "rollback_failed"
		if writeErr := writeTransactionJournal(fileSystem, journalPath, journal); writeErr != nil {
			log.Printf("Warning: could not persist rollback preimage failure: %v\n", writeErr)
		}
		return fmt.Errorf("rollback preimage validation failed before target mutation: %v", err)
	}
	journal.Status = "rolling_back"
	if err := writeTransactionJournal(fileSystem, journalPath, journal); err != nil {
		log.Printf("Warning: could not mark transaction as rolling back: %v\n", err)
	}

	lastOperation := journal.AppliedCount - 1
	if journal.InProgress > lastOperation {
		lastOperation = journal.InProgress
	}
	var rollbackErrors []string
	for index := lastOperation; index >= 0; index-- {
		if err := rollbackJournalOperation(fileSystem, journal.Operations[index]); err != nil {
			rollbackErrors = append(
				rollbackErrors,
				fmt.Sprintf("operation %d: %v", index, err),
			)
		}
	}
	if len(rollbackErrors) > 0 {
		journal.Status = "rollback_failed"
		_ = writeTransactionJournal(fileSystem, journalPath, journal)
		return fmt.Errorf("rollback failed: %s", strings.Join(rollbackErrors, "; "))
	}

	journal.Status = "rolled_back"
	journal.AppliedCount = 0
	journal.InProgress = -1
	if err := writeTransactionJournal(fileSystem, journalPath, journal); err != nil {
		return err
	}
	return nil
}

func translatePathAfterPlanMoves(plan *RenamePlan, path string) string {
	translated := filepath.Clean(path)
	for _, operation := range plan.Operations {
		if operation.Kind != renameOperationMove {
			continue
		}
		if translated == filepath.Clean(operation.Source) {
			translated = filepath.Clean(operation.Target)
			continue
		}
		if !operation.OriginalIsDir || !isPathInside(translated, operation.Source) {
			continue
		}
		relative, err := filepath.Rel(operation.Source, translated)
		if err == nil {
			translated = filepath.Join(operation.Target, relative)
		}
	}
	return translated
}

func verifyCommittedPlan(fileSystem renameFileSystem, plan *RenamePlan) error {
	for _, operation := range plan.Operations {
		switch operation.Kind {
		case renameOperationWrite:
			finalTarget := translatePathAfterPlanMoves(plan, operation.Target)
			finalHash, err := readRegularFileHash(fileSystem, finalTarget)
			if err != nil {
				return fmt.Errorf("failed to verify committed file %s: %v", finalTarget, err)
			}
			if finalHash != operation.AfterHash {
				return fmt.Errorf("committed file content mismatch: %s", finalTarget)
			}
		case renameOperationMove:
			finalTarget := translatePathAfterPlanMoves(plan, operation.Target)
			info, err := fileSystem.Lstat(finalTarget)
			if err != nil {
				return fmt.Errorf("failed to verify renamed target %s: %v", finalTarget, err)
			}
			if info.Mode()&os.ModeSymlink != 0 {
				return fmt.Errorf("renamed target became a symlink or reparse point: %s", finalTarget)
			}
		}
	}
	return nil
}

func cleanupCommittedTemporaryFiles(
	fileSystem renameFileSystem,
	plan *RenamePlan,
	journal *transactionJournal,
) error {
	for _, operation := range journal.Operations {
		for index, temporaryPath := range []string{operation.StagePath, operation.OldTempPath} {
			if temporaryPath == "" {
				continue
			}
			finalPath := translatePathAfterPlanMoves(plan, temporaryPath)
			exists, err := fileExistsWithFS(fileSystem, finalPath)
			if err != nil {
				return err
			}
			if exists {
				expectedHash := operation.AfterHash
				if index == 1 {
					expectedHash = operation.BackupHash
					if expectedHash == "" {
						expectedHash = operation.BeforeHash
					}
				}
				if err := validateInternalRegularFile(fileSystem, finalPath, expectedHash); err != nil {
					return fmt.Errorf("unsafe committed transaction temporary file: %v", err)
				}
			}
			if err := removeRegularFileIfPresent(fileSystem, finalPath); err != nil {
				return err
			}
		}
	}
	return nil
}

func executeRenamePlan(
	fileSystem renameFileSystem,
	plan *RenamePlan,
	log *Logger,
) (string, error) {
	journal, journalPath, transactionDirectory, err := createTransactionJournal(
		fileSystem,
		plan,
	)
	if err != nil {
		return "", err
	}
	journal.Status = "applying"
	if err := writeTransactionJournal(fileSystem, journalPath, journal); err != nil {
		return transactionDirectory, err
	}

	for index, operation := range plan.Operations {
		journal.InProgress = index
		if err := writeTransactionJournal(fileSystem, journalPath, journal); err != nil {
			rollbackErr := rollbackTransaction(fileSystem, journal, journalPath, log)
			if rollbackErr != nil {
				return transactionDirectory, fmt.Errorf("%v; %v", err, rollbackErr)
			}
			return transactionDirectory, err
		}

		if err := applyRenameOperation(
			fileSystem,
			operation,
			journal.Operations[index],
		); err != nil {
			rollbackErr := rollbackTransaction(fileSystem, journal, journalPath, log)
			if rollbackErr != nil {
				return transactionDirectory, fmt.Errorf("%v; %v", err, rollbackErr)
			}
			return transactionDirectory, err
		}
		journal.AppliedCount = index + 1
		journal.InProgress = -1
		if err := writeTransactionJournal(fileSystem, journalPath, journal); err != nil {
			rollbackErr := rollbackTransaction(fileSystem, journal, journalPath, log)
			if rollbackErr != nil {
				return transactionDirectory, fmt.Errorf("%v; %v", err, rollbackErr)
			}
			return transactionDirectory, err
		}
	}

	if err := verifyCommittedPlan(fileSystem, plan); err != nil {
		rollbackErr := rollbackTransaction(fileSystem, journal, journalPath, log)
		if rollbackErr != nil {
			return transactionDirectory, fmt.Errorf("%v; %v", err, rollbackErr)
		}
		return transactionDirectory, err
	}

	journal.Status = "committing"
	if err := writeTransactionJournal(fileSystem, journalPath, journal); err != nil {
		rollbackErr := rollbackTransaction(fileSystem, journal, journalPath, log)
		if rollbackErr != nil {
			return transactionDirectory, fmt.Errorf("%v; %v", err, rollbackErr)
		}
		return transactionDirectory, err
	}
	if err := cleanupCommittedTemporaryFiles(fileSystem, plan, journal); err != nil {
		rollbackErr := rollbackTransaction(fileSystem, journal, journalPath, log)
		if rollbackErr != nil {
			return transactionDirectory, fmt.Errorf("%v; %v", err, rollbackErr)
		}
		return transactionDirectory, err
	}

	journal.Status = "committed"
	if err := writeTransactionJournal(fileSystem, journalPath, journal); err != nil {
		rollbackErr := rollbackTransaction(fileSystem, journal, journalPath, log)
		if rollbackErr != nil {
			return transactionDirectory, fmt.Errorf("%v; %v", err, rollbackErr)
		}
		return transactionDirectory, err
	}
	cleanupOldBackups(fileSystem, filepath.Join(plan.ProjectRoot, backupDirName))
	return transactionDirectory, nil
}

func ensurePathInsideRootWithoutSymlinks(
	fileSystem renameFileSystem,
	root,
	path string,
) error {
	root = filepath.Clean(root)
	path = filepath.Clean(path)
	if err := ensurePathInsideProject(root, path); err != nil {
		return err
	}
	for current := path; ; current = filepath.Dir(current) {
		info, err := fileSystem.Lstat(current)
		if err == nil && info.Mode()&os.ModeSymlink != 0 {
			return fmt.Errorf("path traverses a symlink or reparse point: %s", current)
		}
		if err != nil && !os.IsNotExist(err) {
			return fmt.Errorf("failed to inspect transaction path %s: %v", current, err)
		}
		if normalizedPathKey(current) == normalizedPathKey(root) {
			break
		}
		parent := filepath.Dir(current)
		if parent == current {
			return fmt.Errorf("transaction path escaped its allowed root: %s", path)
		}
	}
	return nil
}

func transactionStatusRequiresRecovery(status string) bool {
	switch status {
	case "prepared", "applying", "rolling_back", "committing", "rollback_failed":
		return true
	default:
		return false
	}
}

func validatePinnedExternalTargets(
	projectRoot string,
	journal *transactionJournal,
) (map[string]string, error) {
	if !sha256HexPattern.MatchString(journal.TemplateIdentityRegistrySHA256) {
		return nil, fmt.Errorf("transaction has no valid pinned template identity registry SHA-256")
	}
	if journal.RegistryWorkspaceRoot == "" ||
		!filepath.IsAbs(journal.RegistryWorkspaceRoot) ||
		filepath.Clean(journal.RegistryWorkspaceRoot) != journal.RegistryWorkspaceRoot {
		return nil, fmt.Errorf("transaction has no valid pinned registry workspace root")
	}
	if !isPathInside(projectRoot, journal.RegistryWorkspaceRoot) {
		return nil, fmt.Errorf(
			"transaction pinned registry workspace does not contain the Unity project: %s",
			journal.RegistryWorkspaceRoot,
		)
	}
	if journal.ApprovedExternalTargets == nil {
		return nil, fmt.Errorf("transaction has no pinned external-target authorization set")
	}
	if !sort.StringsAreSorted(journal.ApprovedExternalTargets) {
		return nil, fmt.Errorf("transaction pinned external-target authorization set is not sorted")
	}

	pinnedTargets := make(map[string]string, len(journal.ApprovedExternalTargets))
	for _, path := range journal.ApprovedExternalTargets {
		if path == "" || !filepath.IsAbs(path) || filepath.Clean(path) != path {
			return nil, fmt.Errorf("transaction has an invalid pinned external target: %q", path)
		}
		if isPathInside(path, projectRoot) {
			return nil, fmt.Errorf("transaction external authorization points inside the Unity project: %s", path)
		}
		if !isPathInside(path, journal.RegistryWorkspaceRoot) {
			return nil, fmt.Errorf("transaction pinned external target escaped the registry workspace: %s", path)
		}
		key := normalizedPathKey(path)
		if existing, duplicated := pinnedTargets[key]; duplicated {
			return nil, fmt.Errorf(
				"transaction pins the same external target more than once: %s / %s",
				existing,
				path,
			)
		}
		pinnedTargets[key] = path
	}
	return pinnedTargets, nil
}

func validateTransactionJournal(
	fileSystem renameFileSystem,
	projectRoot,
	journalPath string,
	journal *transactionJournal,
) error {
	validStatus := map[string]bool{
		"prepared": true, "applying": true, "rolling_back": true,
		"committing": true, "rollback_failed": true,
		"rolled_back": true, "committed": true,
	}
	if !validStatus[journal.Status] {
		return fmt.Errorf("unsupported rename transaction status: %q", journal.Status)
	}
	requiresRecovery := transactionStatusRequiresRecovery(journal.Status)
	if !pathsEqual(journal.ProjectRoot, projectRoot) {
		return fmt.Errorf("transaction belongs to another project root: %s", journal.ProjectRoot)
	}
	if journal.AppliedCount < 0 || journal.AppliedCount > len(journal.Operations) {
		return fmt.Errorf("transaction appliedCount is out of range: %d", journal.AppliedCount)
	}
	if journal.InProgress < -1 || journal.InProgress >= len(journal.Operations) {
		return fmt.Errorf("transaction inProgress is out of range: %d", journal.InProgress)
	}

	var workspaceRoot string
	var externalTargets map[string]string
	switch journal.Version {
	case 2:
		if requiresRecovery {
			return fmt.Errorf(
				"legacy incomplete rename transaction has no pinned registry digest; automatic recovery is refused",
			)
		}
		identitySnapshot, err := loadTemplateIdentityRegistry(projectRoot)
		if err != nil {
			return fmt.Errorf("cannot validate legacy terminal transaction without the identity registry: %v", err)
		}
		workspaceRoot = identitySnapshot.WorkspaceRoot
		externalTargets = identitySnapshot.ExternalWriteTargets
	case transactionJournalVersion:
		pinnedTargets, err := validatePinnedExternalTargets(projectRoot, journal)
		if err != nil {
			return err
		}
		workspaceRoot = journal.RegistryWorkspaceRoot
		externalTargets = pinnedTargets
		if requiresRecovery {
			identitySnapshot, loadErr := loadTemplateIdentityRegistry(projectRoot)
			if loadErr != nil {
				return fmt.Errorf("cannot verify the pinned identity registry before recovery: %v", loadErr)
			}
			if identitySnapshot.ContentSHA256 != journal.TemplateIdentityRegistrySHA256 {
				return fmt.Errorf(
					"template identity registry drifted after transaction planning; recovery is refused (pinned %s, current %s)",
					journal.TemplateIdentityRegistrySHA256,
					identitySnapshot.ContentSHA256,
				)
			}
			if !pathsEqual(identitySnapshot.WorkspaceRoot, journal.RegistryWorkspaceRoot) {
				return fmt.Errorf("template identity registry workspace drifted after transaction planning; recovery is refused")
			}
			for key, pinnedPath := range pinnedTargets {
				declaredPath, declared := identitySnapshot.DeclaredExternalTargets[key]
				if !declared || !pathsEqual(declaredPath, pinnedPath) {
					return fmt.Errorf(
						"pinned external target is no longer declared exactly by the identity registry: %s",
						pinnedPath,
					)
				}
			}
		}
	default:
		return fmt.Errorf("unsupported rename transaction journal version: %d", journal.Version)
	}

	transactionDirectory := filepath.Dir(journalPath)
	transactionToken := filepath.Base(transactionDirectory)
	backupRoot := filepath.Join(projectRoot, backupDirName)
	if err := ensurePathInsideRootWithoutSymlinks(fileSystem, backupRoot, transactionDirectory); err != nil {
		return fmt.Errorf("unsafe transaction directory: %v", err)
	}
	for index, operation := range journal.Operations {
		validateScopedPath := func(label, path string, required, allowExternalTarget bool) error {
			if path == "" {
				if required {
					return fmt.Errorf("operation %d has no %s", index, label)
				}
				return nil
			}
			if isPathInside(path, projectRoot) {
				if err := ensurePathInsideRootWithoutSymlinks(fileSystem, projectRoot, path); err != nil {
					return fmt.Errorf("operation %d has unsafe %s: %v", index, label, err)
				}
				return nil
			}
			approvedPath, approved := externalTargets[normalizedPathKey(path)]
			if !allowExternalTarget || !approved || !pathsEqual(approvedPath, path) {
				return fmt.Errorf("operation %d has unregistered external %s: %s", index, label, path)
			}
			if err := ensurePathInsideRootWithoutSymlinks(fileSystem, workspaceRoot, path); err != nil {
				return fmt.Errorf("operation %d has unsafe external %s: %v", index, label, err)
			}
			return nil
		}
		validateWorkspaceTemporaryPath := func(label, path string) error {
			if path == "" {
				return fmt.Errorf("operation %d has no %s", index, label)
			}
			if err := ensurePathInsideRootWithoutSymlinks(fileSystem, workspaceRoot, path); err != nil {
				return fmt.Errorf("operation %d has unsafe %s: %v", index, label, err)
			}
			return nil
		}
		if err := validateScopedPath("target", operation.Target, true, true); err != nil {
			return err
		}
		if operation.BackupPath != "" {
			if err := ensurePathInsideRootWithoutSymlinks(
				fileSystem,
				transactionDirectory,
				operation.BackupPath,
			); err != nil {
				return fmt.Errorf("operation %d has unsafe backupPath: %v", index, err)
			}
		}
		switch operation.Kind {
		case renameOperationWrite:
			if err := validateWorkspaceTemporaryPath("stagePath", operation.StagePath); err != nil {
				return err
			}
			if err := validateWorkspaceTemporaryPath("oldTempPath", operation.OldTempPath); err != nil {
				return err
			}
			expectedStagePath := fmt.Sprintf(
				"%s.rename-stage-%s-%03d",
				operation.Target,
				transactionToken,
				index,
			)
			expectedOldTempPath := fmt.Sprintf(
				"%s.rename-old-%s-%03d",
				operation.Target,
				transactionToken,
				index,
			)
			if normalizedPathKey(operation.StagePath) != normalizedPathKey(expectedStagePath) ||
				normalizedPathKey(operation.OldTempPath) != normalizedPathKey(expectedOldTempPath) {
				return fmt.Errorf("operation %d has forged transaction temporary paths", index)
			}
			if operation.AfterHash == "" {
				return fmt.Errorf("operation %d has no committed-content hash", index)
			}
			if operation.OriginalExists && (operation.BeforeHash == "" || operation.BackupPath == "") {
				return fmt.Errorf("operation %d has no original-file preimage", index)
			}
		case renameOperationMove:
			if err := validateScopedPath("source", operation.Source, true, false); err != nil {
				return err
			}
			if !operation.OriginalIsDir && (operation.BeforeHash == "" || operation.BackupPath == "") {
				return fmt.Errorf("operation %d has no move-source preimage", index)
			}
			if operation.OriginalIsDir {
				assetsRoot := filepath.Join(projectRoot, "Assets")
				if normalizedPathKey(filepath.Dir(operation.Source)) != normalizedPathKey(assetsRoot) ||
					normalizedPathKey(filepath.Dir(operation.Target)) != normalizedPathKey(assetsRoot) ||
					validateProjectToken(filepath.Base(operation.Source)) != nil ||
					validateProjectToken(filepath.Base(operation.Target)) != nil ||
					isProtectedProjectFolder(filepath.Base(operation.Source)) ||
					isProtectedProjectFolder(filepath.Base(operation.Target)) {
					return fmt.Errorf("operation %d has an unsafe project-folder move", index)
				}
			}
		default:
			return fmt.Errorf("operation %d has unsupported kind %q", index, operation.Kind)
		}
		expectedBackupPath := filepath.Join(
			transactionDirectory,
			"files",
			fmt.Sprintf("%03d.before", index),
		)
		requiresBackup := (operation.Kind == renameOperationWrite && operation.OriginalExists) ||
			(operation.Kind == renameOperationMove && !operation.OriginalIsDir)
		if requiresBackup {
			if normalizedPathKey(operation.BackupPath) != normalizedPathKey(expectedBackupPath) {
				return fmt.Errorf("operation %d has a forged backupPath", index)
			}
			if operation.BackupHash == "" {
				return fmt.Errorf("operation %d has no backupHash", index)
			}
		} else if operation.BackupPath != "" {
			return fmt.Errorf("operation %d has an unexpected backupPath", index)
		} else if operation.BackupHash != "" {
			return fmt.Errorf("operation %d has an unexpected backupHash", index)
		}
	}
	return nil
}

func loadTransactionJournal(
	fileSystem renameFileSystem,
	projectRoot,
	path string,
) (*transactionJournal, error) {
	var loadErrors []string
	for _, candidate := range []string{path, path + ".previous"} {
		if err := validateInternalRegularFile(fileSystem, candidate, ""); err != nil {
			if !os.IsNotExist(err) {
				loadErrors = append(loadErrors, fmt.Sprintf("%s: %v", candidate, err))
			}
			continue
		}
		content, err := fileSystem.ReadFile(candidate)
		if err != nil {
			if !os.IsNotExist(err) {
				loadErrors = append(loadErrors, fmt.Sprintf("%s: %v", candidate, err))
			}
			continue
		}
		var journal transactionJournal
		if err := json.Unmarshal(content, &journal); err != nil {
			loadErrors = append(loadErrors, fmt.Sprintf("%s: %v", candidate, err))
			continue
		}
		if err := validateInternalRegularFile(fileSystem, candidate, contentHash(content)); err != nil {
			loadErrors = append(loadErrors, fmt.Sprintf("%s: %v", candidate, err))
			continue
		}
		if err := validateTransactionJournal(fileSystem, projectRoot, path, &journal); err != nil {
			loadErrors = append(loadErrors, fmt.Sprintf("%s: %v", candidate, err))
			continue
		}
		return &journal, nil
	}
	if len(loadErrors) == 0 {
		return nil, os.ErrNotExist
	}
	return nil, fmt.Errorf("no valid transaction journal generation: %s", strings.Join(loadErrors, "; "))
}

func findIncompleteTransactionWithFS(
	fileSystem renameFileSystem,
	projectRoot string,
) (string, *transactionJournal, error) {
	backupRoot := filepath.Join(projectRoot, backupDirName)
	backupInfo, lstatErr := fileSystem.Lstat(backupRoot)
	if os.IsNotExist(lstatErr) {
		return "", nil, nil
	}
	if lstatErr != nil {
		return "", nil, lstatErr
	}
	if !backupInfo.IsDir() || backupInfo.Mode()&os.ModeSymlink != 0 {
		return "", nil, fmt.Errorf("rename backup root is not a real directory: %s", backupRoot)
	}
	entries, err := fileSystem.ReadDir(backupRoot)
	if err != nil {
		return "", nil, err
	}
	for _, entry := range entries {
		transactionDirectory := filepath.Join(backupRoot, entry.Name())
		transactionInfo, err := fileSystem.Lstat(transactionDirectory)
		if err != nil {
			return "", nil, err
		}
		if !transactionInfo.IsDir() || transactionInfo.Mode()&os.ModeSymlink != 0 {
			continue
		}
		journalPath := filepath.Join(transactionDirectory, "transaction.json")
		journal, err := loadTransactionJournal(fileSystem, projectRoot, journalPath)
		if os.IsNotExist(err) {
			continue
		}
		if err != nil {
			return "", nil, fmt.Errorf("invalid rename transaction journal %s: %v", journalPath, err)
		}
		switch journal.Status {
		case "prepared", "applying", "rolling_back", "committing", "rollback_failed":
			return journalPath, journal, nil
		}
	}
	return "", nil, nil
}

func findIncompleteTransaction(projectRoot string) (string, *transactionJournal, error) {
	return findIncompleteTransactionWithFS(osRenameFileSystem{}, projectRoot)
}

func recoverIncompleteTransaction(
	fileSystem renameFileSystem,
	projectRoot string,
	log *Logger,
) error {
	journalPath, journal, err := findIncompleteTransactionWithFS(fileSystem, projectRoot)
	if err != nil {
		return err
	}
	if journal == nil {
		return nil
	}
	if !pathsEqual(journal.ProjectRoot, projectRoot) {
		return fmt.Errorf(
			"incomplete rename transaction belongs to another project root: %s",
			journal.ProjectRoot,
		)
	}
	log.Printf("Recovering incomplete rename transaction: %s\n", journalPath)
	return rollbackTransaction(fileSystem, journal, journalPath, log)
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

func parseRenameOptions(args []string) (bool, error) {
	flags := flag.NewFlagSet("rename_project", flag.ContinueOnError)
	flags.SetOutput(io.Discard)
	dryRun := flags.Bool("dry-run", false, "validate and print the complete plan without writing files")
	if err := flags.Parse(args); err != nil {
		return false, err
	}
	if flags.NArg() != 0 {
		return false, fmt.Errorf("unexpected positional arguments: %s", strings.Join(flags.Args(), " "))
	}
	return *dryRun, nil
}

func printRenameUsage() {
	fmt.Println("Usage: rename_project [--dry-run]")
	fmt.Println("  --dry-run  Validate and print the complete plan without writing files.")
}

func runRenameTool(args []string) (exitCode int) {
	dryRun, err := parseRenameOptions(args)
	if err != nil {
		if errors.Is(err, flag.ErrHelp) {
			printRenameUsage()
			return 0
		}
		fmt.Printf("Error: %v\n\n", err)
		printRenameUsage()
		return 2
	}

	projectRoot, err := findProjectRoot()
	if err != nil {
		fmt.Println("Error:", err)
		return 1
	}
	projectRoot, err = filepath.Abs(projectRoot)
	if err != nil {
		fmt.Println("Error resolving Unity project root:", err)
		return 1
	}
	projectRoot = filepath.Clean(projectRoot)
	identitySnapshot, err := loadTemplateIdentityRegistry(projectRoot)
	if err != nil {
		fmt.Println("Error:", err)
		return 1
	}
	fileSystemRoot := identitySnapshot.WorkspaceRoot
	if len(identitySnapshot.DeclaredExternalTargets) == 0 {
		fileSystemRoot = projectRoot
	}

	fileSystem, err := newRootedRenameFileSystem(fileSystemRoot)
	if err != nil {
		fmt.Println("Error opening rooted template workspace filesystem:", err)
		return 1
	}
	projectRoot, err = filepath.EvalSymlinks(projectRoot)
	if err != nil {
		_ = fileSystem.Close()
		fmt.Println("Error canonicalizing Unity project root:", err)
		return 1
	}
	projectRoot, err = filepath.Abs(projectRoot)
	if err != nil {
		_ = fileSystem.Close()
		fmt.Println("Error resolving canonical Unity project root:", err)
		return 1
	}
	projectRoot = filepath.Clean(projectRoot)
	if err := ensurePathInsideProject(fileSystem.rootPath, projectRoot); err != nil {
		_ = fileSystem.Close()
		fmt.Println("Error validating Unity project inside template workspace:", err)
		return 1
	}
	fmt.Printf("Found Unity project root at: %s\n", projectRoot)
	defer func() {
		if closeErr := fileSystem.Close(); closeErr != nil {
			fmt.Printf("Error closing rooted Unity project filesystem: %v\n", closeErr)
			if exitCode == 0 {
				exitCode = 1
			}
		}
	}()
	projectLock, err := acquireProjectLock(fileSystem, projectRoot)
	if err != nil {
		fmt.Println("Error:", err)
		return 1
	}
	defer func() {
		if releaseErr := releaseProjectLock(fileSystem, projectLock); releaseErr != nil {
			fmt.Printf("Error releasing rename project lock: %v\n", releaseErr)
			if exitCode == 0 {
				exitCode = 1
			}
		}
	}()

	var log *Logger
	logPath := filepath.Join(projectRoot, "rename_project.log")
	if dryRun {
		log = NewConsoleLogger()
		journalPath, journal, inspectErr := findIncompleteTransactionWithFS(fileSystem, projectRoot)
		if inspectErr != nil {
			log.Printf("Error inspecting previous rename transactions: %v\n", inspectErr)
			return 1
		}
		if journal != nil {
			log.Printf("Error: incomplete rename transaction requires recovery before dry-run: %s\n", journalPath)
			return 1
		}
	} else {
		log, err = NewLogger(fileSystem, logPath)
		if err != nil {
			fmt.Println("Error:", err)
			return 1
		}
		defer log.Close()
		log.Printf("=== Rename Project Tool started at %s ===\n", time.Now().Format("2006-01-02 15:04:05"))
		if recoverErr := recoverIncompleteTransaction(fileSystem, projectRoot, log); recoverErr != nil {
			log.Printf("Error recovering an incomplete rename transaction: %v\n", recoverErr)
			return 1
		}
	}

	oldName, oldCompanyName, oldAppName, oldApplicationIdentifier, err := getCurrentProjectInfo(projectRoot)
	if err != nil {
		log.Printf("Error getting current project info: %v\n", err)
		return 1
	}
	log.Println("\nCurrent project settings:")
	log.Printf("  Project Folder: %s\n", oldName)
	log.Printf("  Company Name:   %s\n", oldCompanyName)
	log.Printf("  App Name:       %s\n", oldAppName)
	log.Printf("  App Identifier: %s\n", oldApplicationIdentifier)

	newProjectName := promptValidatedInput(
		1,
		"Project Name",
		"The Assets folder token must start with a letter or underscore and contain only ASCII letters, numbers, and underscores.",
		oldName,
		validateProjectToken,
	)
	newCompanyName := promptValidatedInput(
		2,
		"Company Name",
		"Display name written to PlayerSettings.companyName. Spaces and Unicode are allowed.",
		oldCompanyName,
		func(value string) error {
			return validateDisplayName("company name", value)
		},
	)
	newAppName := promptValidatedInput(
		3,
		"Application Name",
		"Display name written to PlayerSettings.productName. Spaces and Unicode are allowed.",
		oldAppName,
		func(value string) error {
			return validateDisplayName("application name", value)
		},
	)
	newApplicationIdentifier := promptValidatedInput(
		4,
		"Application Identifier",
		"Android/iOS-compatible identifier written to every PlayerSettings applicationIdentifier entry. Use dot-separated segments that start with a lowercase ASCII letter and otherwise contain only lowercase ASCII letters or digits (for example, com.example.game2).",
		oldApplicationIdentifier,
		validateApplicationIdentifier,
	)

	if newProjectName == oldName && newCompanyName == oldCompanyName &&
		newAppName == oldAppName && newApplicationIdentifier == oldApplicationIdentifier {
		log.Println("\nNo changes needed - all values are unchanged.")
		return 0
	}

	request := RenameRequest{
		OldProjectName:           oldName,
		NewProjectName:           newProjectName,
		OldCompanyName:           oldCompanyName,
		NewCompanyName:           newCompanyName,
		OldAppName:               oldAppName,
		NewAppName:               newAppName,
		OldApplicationIdentifier: oldApplicationIdentifier,
		NewApplicationIdentifier: newApplicationIdentifier,
	}
	plan, err := buildRenamePlan(projectRoot, request)
	if err != nil {
		log.Printf("Error preparing rename plan: %v\n", err)
		return 1
	}
	printRenamePlan(log, plan)

	if dryRun {
		log.Println("\nDry run complete. No project files, logs, backups, state, or transaction temporary files were written. The project lock will now be released.")
		return 0
	}

	fmt.Print("\nProceed with this validated transaction? (y/N): ")
	confirm, _ := stdinReader.ReadString('\n')
	if strings.TrimSpace(strings.ToLower(confirm)) != "y" {
		log.Println("\nOperation cancelled by user.")
		return 0
	}

	transactionDir, err := executeRenamePlan(fileSystem, plan, log)
	if err != nil {
		log.Printf("Rename transaction failed: %v\n", err)
		log.Printf("Transaction evidence: %s\n", transactionDir)
		return 1
	}

	log.Println("\n===========================================")
	log.Println("  Project successfully renamed!")
	log.Println("===========================================")
	log.Printf("  Project folder: %s -> %s\n", oldName, newProjectName)
	log.Printf("  Company name:   %s -> %s\n", oldCompanyName, newCompanyName)
	log.Printf("  App name:       %s -> %s\n", oldAppName, newAppName)
	log.Printf("  App identifier: %s -> %s\n", oldApplicationIdentifier, newApplicationIdentifier)
	log.Printf("  Transaction:    %s\n", transactionDir)
	log.Printf("  Log:            %s\n", logPath)
	log.Println("\nPlease open the project in Unity Editor and perform the documented validation steps.")
	return 0
}

// Run executes the project rename tool and returns its process exit code.
func Run(args []string) int {
	return runRenameTool(args)
}
