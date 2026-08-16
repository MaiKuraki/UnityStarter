package unity_project_full_clean

import (
	"bytes"
	"crypto/rand"
	"crypto/sha256"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"hash"
	"io"
	"io/fs"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"sort"
	"strconv"
	"strings"
	"syscall"
	"time"
	"unicode/utf16"

	"cyclonegames.tools/scripts/internal/safefs"
)

const (
	leaseRelativePath       = "Temp/BuildPipeline/Workspace/lease.lock"
	leaseMetadataRelative   = "Temp/BuildPipeline/Workspace/lease.json"
	transactionRelativePath = ".buildpipeline/transactions"
	quarantineRelativePath  = "Temp/BuildPipeline/Workspace/cleanup-quarantine"
	playerOwnerSuffix       = ".buildpipeline-player-owner.json"
	maximumOwnerBytes       = 64 * 1024
	maximumManifestBytes    = 64 * 1024 * 1024
	maximumOwnerJSONDepth   = 64
	maximumCleanEntries     = 500000
	maximumCleanDepth       = 64
)

var cacheDirectories = []string{
	".vs", ".utmp", "obj", "Logs", "Library",
}

var publicationRoots = []string{
	"Build", "Bundles", "HybridCLRData", "yoo", "HotUpdateAssetsPreUpload",
}

type editorInstance struct {
	ProcessID int `json:"process_id"`
}

type leaseMetadata struct {
	DocumentType string `json:"documentType"`
	RunID        string `json:"runId"`
	Operation    string `json:"operation"`
	PID          int    `json:"pid"`
	StartedUTC   string `json:"startedUtc"`
}

type buildOwnerMarker struct {
	DocumentType  string `json:"documentType"`
	Owner         string `json:"owner"`
	Kind          string `json:"kind"`
	TransactionID string `json:"transactionId"`
	Checksum      string `json:"checksum"`
}

type addressablesOwnerMarker struct {
	DocumentType   string `json:"documentType"`
	Owner          string `json:"owner"`
	TransactionID  string `json:"transactionId"`
	ManifestSHA256 string `json:"manifestSha256"`
}

type hybridCLROwnerFile struct {
	Kind   string `json:"kind"`
	Path   string `json:"path"`
	Size   int64  `json:"size"`
	SHA256 string `json:"sha256"`
}

type hybridCLROwnerMarker struct {
	DocumentType  string               `json:"documentType"`
	Owner         string               `json:"owner"`
	Role          string               `json:"role"`
	TransactionID string               `json:"transactionId"`
	Files         []hybridCLROwnerFile `json:"files"`
}

type yooAssetOwnerMarker struct {
	DocumentType             string `json:"documentType"`
	Owner                    string `json:"owner"`
	Kind                     string `json:"kind"`
	PackageName              string `json:"packageName"`
	PackageVersion           string `json:"packageVersion"`
	CryptographyAdapterID    string `json:"cryptographyAdapterId"`
	RuntimeDecryptContractID string `json:"runtimeDecryptContractId"`
	TransactionID            string `json:"transactionId"`
	ContentIdentity          string `json:"contentIdentity"`
	EntryCount               int    `json:"entryCount"`
	Checksum                 string `json:"checksum"`
}

type playerTreeIdentity struct {
	Digest     string `json:"digest"`
	EntryCount int    `json:"entryCount"`
	FileCount  int    `json:"fileCount"`
	TotalBytes int64  `json:"totalBytes"`
}

type playerCompatibilityIdentity struct {
	PipelineImplementationFingerprint string `json:"pipelineImplementationFingerprint"`
	UnityVersion                      string `json:"unityVersion"`
	BuildTarget                       string `json:"buildTarget"`
	NamedBuildTarget                  string `json:"namedBuildTarget"`
	ScriptingBackend                  string `json:"scriptingBackend"`
	OutputArtifactPath                string `json:"outputArtifactPath"`
	OutputIsFolder                    bool   `json:"outputIsFolder"`
	CompanyName                       string `json:"companyName"`
	ProductName                       string `json:"productName"`
	ApplicationIdentifier             string `json:"applicationIdentifier"`
	ExportAndroidProject              bool   `json:"exportAndroidProject"`
	DebugBuild                        bool   `json:"debugBuild"`
	DeleteDebugFiles                  bool   `json:"deleteDebugFiles"`
	CheatEnabled                      bool   `json:"cheatEnabled"`
	BuildPurpose                      string `json:"buildPurpose"`
	PlayerExtensionFingerprint        string `json:"playerExtensionFingerprint"`
	Digest                            string `json:"digest"`
}

type playerOwnerMarker struct {
	DocumentType          string                       `json:"documentType"`
	Kind                  string                       `json:"kind"`
	TransactionID         string                       `json:"transactionId"`
	HasIdentity           bool                         `json:"hasIdentity"`
	Identity              *playerTreeIdentity          `json:"identity"`
	CompatibilityIdentity *playerCompatibilityIdentity `json:"compatibilityIdentity"`
	Checksum              string                       `json:"checksum"`
}

type playerTreeEntry struct {
	relativePath string
	isDirectory  bool
	length       int64
	hash         string
}

type buildWorkspaceLease struct {
	file     *os.File
	path     string
	identity os.FileInfo
}

type cleanItem struct {
	path           string
	kind           string
	size           int64
	identity       os.FileInfo
	playerIdentity *playerTreeIdentity
	ownerEvidence  *ownerMarkerEvidence
}

type ownerMarkerEvidence struct {
	Path          string
	SHA256        string
	TransactionID string
}

type quarantineEntry struct {
	OriginalPath             string `json:"originalPath"`
	ClaimedPath              string `json:"claimedPath"`
	Kind                     string `json:"kind"`
	State                    string `json:"state"`
	OwnerMarkerPath          string `json:"ownerMarkerPath,omitempty"`
	OwnerMarkerSHA256        string `json:"ownerMarkerSha256,omitempty"`
	OwnerMarkerTransactionID string `json:"ownerMarkerTransactionId,omitempty"`
	PlayerTreeDigest         string `json:"playerTreeDigest,omitempty"`
}

type quarantineJournal struct {
	DocumentType  string            `json:"documentType"`
	TransactionID string            `json:"transactionId"`
	State         string            `json:"state"`
	StartedUTC    string            `json:"startedUtc"`
	Entries       []quarantineEntry `json:"entries"`
}

type claimedCleanItem struct {
	item            cleanItem
	claimedPath     string
	claimedIdentity os.FileInfo
	entryIndex      int
}

type boundQuarantineRoot struct {
	root     *os.Root
	path     string
	identity os.FileInfo
	closed   bool
}

// Run executes the full-clean tool and returns its process exit code.
func Run(arguments []string) int {
	return run(arguments, os.Stdout, os.Stderr)
}

func run(arguments []string, stdout, stderr io.Writer) int {
	flags := flag.NewFlagSet("unity_project_full_clean", flag.ContinueOnError)
	flags.SetOutput(stderr)
	var ciMode bool
	var dryRun bool
	var includeBuildOutputs bool
	flags.BoolVar(&ciMode, "ci", false, "Non-interactive mode")
	flags.BoolVar(&dryRun, "dry-run", false, "Validate and preview without deleting")
	flags.BoolVar(&includeBuildOutputs, "include-build-outputs", false, "Delete only output trees proven to be Build-owned")
	if err := flags.Parse(arguments); err != nil {
		if errors.Is(err, flag.ErrHelp) {
			return 0
		}
		return 2
	}
	if flags.NArg() != 0 {
		fmt.Fprintf(stderr, "[ERROR] Unexpected positional arguments: %s\n", strings.Join(flags.Args(), " "))
		return 2
	}

	projectRoot, err := os.Getwd()
	if err != nil {
		fmt.Fprintf(stderr, "[ERROR] Cannot resolve current directory: %v\n", err)
		return 1
	}
	projectRoot, err = validateProjectRoot(projectRoot)
	if err != nil {
		fmt.Fprintf(stderr, "[ERROR] %v\n", err)
		return 1
	}

	lease, err := acquireBuildWorkspaceLease(projectRoot)
	if err != nil {
		fmt.Fprintf(stderr, "[ERROR] Build workspace is busy or unsafe; cleanup refused: %v\n", err)
		return 1
	}
	defer func() {
		if err := lease.release(); err != nil {
			fmt.Fprintf(stderr, "[WARNING] Failed to release Build workspace lease cleanly: %v\n", err)
		}
	}()

	if running, pid, err := checkUnityRunning(projectRoot); err != nil {
		fmt.Fprintf(stderr, "[ERROR] Unity activity cannot be proven idle: %v\n", err)
		return 1
	} else if running {
		fmt.Fprintf(stderr, "[ERROR] Unity Editor is active for this project (PID %d). Cleanup refused; there is no force mode.\n", pid)
		return 1
	}
	if err := ensureNoPendingRecovery(projectRoot); err != nil {
		fmt.Fprintf(stderr, "[ERROR] Pending or ambiguous Build recovery evidence blocks cleanup: %v\n", err)
		return 1
	}
	if err := ensureNoStaleCleanupQuarantine(projectRoot); err != nil {
		fmt.Fprintf(stderr, "[ERROR] Stale or ambiguous cleanup quarantine requires explicit recovery: %v\n", err)
		return 1
	}

	ownedOutputs, err := inspectPublicationOwnership(projectRoot, includeBuildOutputs)
	if err != nil {
		fmt.Fprintf(stderr, "[ERROR] Foreign or invalid build output blocks cleanup: %v\n", err)
		return 1
	}
	items, err := collectCleanItems(projectRoot, ownedOutputs, includeBuildOutputs)
	if err != nil {
		fmt.Fprintf(stderr, "[ERROR] Cleanup inventory rejected: %v\n", err)
		return 1
	}
	printPreview(stdout, projectRoot, items, ownedOutputs, includeBuildOutputs)
	if len(items) == 0 || dryRun {
		if dryRun {
			fmt.Fprintln(stdout, "[Dry Run] Lease and safety validation passed; no files were deleted.")
		}
		return 0
	}

	if !ciMode {
		fmt.Fprint(stdout, "Type CLEAN to delete the listed cache state")
		if includeBuildOutputs {
			fmt.Fprint(stdout, " and Build-owned publications")
		}
		fmt.Fprint(stdout, ": ")
		var confirmation string
		if _, err := fmt.Fscan(os.Stdin, &confirmation); err != nil || confirmation != "CLEAN" {
			fmt.Fprintln(stdout, "Cleanup cancelled.")
			return 0
		}
	}

	// Close the TOCTOU window as far as the EditorInstance contract permits.
	if running, pid, err := checkUnityRunning(projectRoot); err != nil || running {
		if err != nil {
			fmt.Fprintf(stderr, "[ERROR] Unity activity changed before deletion: %v\n", err)
		} else {
			fmt.Fprintf(stderr, "[ERROR] Unity Editor started before deletion (PID %d). Nothing was deleted.\n", pid)
		}
		return 1
	}
	if err := ensureNoPendingRecovery(projectRoot); err != nil {
		fmt.Fprintf(stderr, "[ERROR] Build recovery state changed before deletion: %v\n", err)
		return 1
	}
	recheckedOwnedOutputs, err := inspectPublicationOwnership(projectRoot, includeBuildOutputs)
	if err != nil {
		fmt.Fprintf(stderr, "[ERROR] Build output ownership changed before deletion: %v\n", err)
		return 1
	}
	if !cleanItemInventoriesEqual(ownedOutputs, recheckedOwnedOutputs) {
		fmt.Fprintln(stderr, "[ERROR] Build output ownership targets changed after preview; nothing was deleted.")
		return 1
	}
	recheckedItems, err := collectCleanItems(projectRoot, recheckedOwnedOutputs, includeBuildOutputs)
	if err != nil {
		fmt.Fprintf(stderr, "[ERROR] Cleanup inventory changed before deletion: %v\n", err)
		return 1
	}
	if !cleanItemInventoriesEqual(items, recheckedItems) {
		fmt.Fprintln(stderr, "[ERROR] Cleanup targets changed after preview; nothing was deleted.")
		return 1
	}
	if running, pid, err := checkUnityRunning(projectRoot); err != nil || running {
		if err != nil {
			fmt.Fprintf(stderr, "[ERROR] Unity activity changed during final ownership validation: %v\n", err)
		} else {
			fmt.Fprintf(stderr, "[ERROR] Unity Editor started during final ownership validation (PID %d). Nothing was deleted.\n", pid)
		}
		return 1
	}
	if err := ensureNoPendingRecovery(projectRoot); err != nil {
		fmt.Fprintf(stderr, "[ERROR] Build recovery state changed during final ownership validation: %v\n", err)
		return 1
	}
	if err := lease.validate(); err != nil {
		fmt.Fprintf(stderr, "[ERROR] Build workspace lease identity changed before quarantine: %v\n", err)
		return 1
	}
	if err := ensureNoStaleCleanupQuarantine(projectRoot); err != nil {
		fmt.Fprintf(stderr, "[ERROR] Cleanup quarantine state changed before claim: %v\n", err)
		return 1
	}
	items = recheckedItems

	start := time.Now()
	deleted, failed, freed := executeQuarantinedCleanup(projectRoot, lease, items, stdout)
	fmt.Fprintf(stdout, "Deleted %d items; failed %d; reclaimed %s; elapsed %s.\n", deleted, failed, formatSize(freed), time.Since(start).Round(time.Millisecond))
	if failed != 0 {
		return 1
	}
	return 0
}

func validateProjectRoot(path string) (string, error) {
	root, err := filepath.Abs(path)
	if err != nil {
		return "", err
	}
	root = filepath.Clean(root)
	volumeRoot := filepath.Clean(filepath.VolumeName(root) + string(os.PathSeparator))
	if samePath(root, volumeRoot) {
		return "", errors.New("filesystem roots cannot be cleaned")
	}
	rootInfo, err := os.Lstat(root)
	if err != nil || !rootInfo.IsDir() || rootInfo.Mode()&os.ModeSymlink != 0 {
		return "", errors.New("project root is unavailable, redirected, or not a directory")
	}
	if redirected, err := pathIsReparsePoint(root); err != nil || redirected {
		return "", errors.New("project root is a symlink/reparse point or cannot be inspected")
	}
	if err := safefs.ValidateMountBoundary(root, root); err != nil {
		return "", fmt.Errorf("project root mount identity cannot be trusted: %w", err)
	}
	resolvedRoot, err := filepath.EvalSymlinks(root)
	if err != nil {
		return "", fmt.Errorf("project root cannot be canonicalized: %w", err)
	}
	resolvedRoot, err = filepath.Abs(resolvedRoot)
	if err != nil || !samePath(root, resolvedRoot) {
		return "", errors.New("project root is reached through a symlink/reparse path")
	}
	for _, relative := range []string{"Assets", "ProjectSettings", "Packages"} {
		markerPath := filepath.Join(root, relative)
		if err := ensurePathSegmentsNotRedirected(root, markerPath); err != nil {
			return "", fmt.Errorf("Unity project marker '%s' is redirected: %w", relative, err)
		}
		info, err := os.Lstat(markerPath)
		if err != nil || !info.IsDir() || info.Mode()&os.ModeSymlink != 0 {
			return "", fmt.Errorf("Unity project marker '%s' is unavailable, redirected, or not a directory", relative)
		}
	}
	projectVersionPath := filepath.Join(root, "ProjectSettings", "ProjectVersion.txt")
	projectVersion, _, err := readBoundedStableFile(root, projectVersionPath, 4*1024)
	if err != nil || !bytes.Contains(projectVersion, []byte("m_EditorVersion:")) {
		return "", fmt.Errorf("ProjectSettings/ProjectVersion.txt is invalid: %v", err)
	}
	manifestPath := filepath.Join(root, "Packages", "manifest.json")
	manifest, _, err := readBoundedStableFile(root, manifestPath, maximumManifestBytes)
	if err != nil {
		return "", fmt.Errorf("Packages/manifest.json is invalid: %w", err)
	}
	if err := validateUnityManifest(manifest); err != nil {
		return "", fmt.Errorf("Packages/manifest.json is not a structured Unity manifest: %w", err)
	}
	return root, nil
}

func readBoundedStableFile(projectRoot, path string, maximumBytes int64) ([]byte, os.FileInfo, error) {
	if err := ensurePathSegmentsNotRedirected(projectRoot, path); err != nil {
		return nil, nil, err
	}
	before, err := os.Lstat(path)
	if err != nil || !before.Mode().IsRegular() || before.Mode()&os.ModeSymlink != 0 || before.Size() < 2 || before.Size() > maximumBytes {
		return nil, nil, fmt.Errorf("file is unavailable or outside its regular-file byte budget: %s", path)
	}
	if redirected, err := pathIsReparsePoint(path); err != nil || redirected {
		return nil, nil, fmt.Errorf("file is redirected or unreadable: %s", path)
	}
	data, err := os.ReadFile(path)
	if err != nil {
		return nil, nil, err
	}
	after, err := os.Lstat(path)
	if err != nil || !after.Mode().IsRegular() || after.Mode()&os.ModeSymlink != 0 ||
		!os.SameFile(before, after) || before.Size() != after.Size() ||
		!before.ModTime().Equal(after.ModTime()) || before.Mode().Perm() != after.Mode().Perm() {
		return nil, nil, fmt.Errorf("file identity changed while reading: %s", path)
	}
	if err := ensurePathSegmentsNotRedirected(projectRoot, path); err != nil {
		return nil, nil, fmt.Errorf("file path became redirected while reading: %s: %w", path, err)
	}
	return data, after, nil
}

func validateUnityManifest(data []byte) error {
	decoder := json.NewDecoder(bytes.NewReader(data))
	token, err := decoder.Token()
	if err != nil {
		return err
	}
	if delimiter, ok := token.(json.Delim); !ok || delimiter != '{' {
		return errors.New("manifest root must be an object")
	}
	seen := make(map[string]bool)
	foundDependencies := false
	for decoder.More() {
		nameToken, err := decoder.Token()
		if err != nil {
			return err
		}
		name, ok := nameToken.(string)
		if !ok || seen[name] {
			return fmt.Errorf("invalid or duplicate manifest member '%v'", nameToken)
		}
		seen[name] = true
		var value json.RawMessage
		if err := decoder.Decode(&value); err != nil {
			return err
		}
		if name == "dependencies" {
			foundDependencies = true
			var dependencies map[string]json.RawMessage
			if err := json.Unmarshal(value, &dependencies); err != nil || dependencies == nil {
				return errors.New("dependencies must be an object")
			}
		}
	}
	if _, err := decoder.Token(); err != nil {
		return err
	}
	var extra interface{}
	if err := decoder.Decode(&extra); err != io.EOF {
		if err != nil {
			return err
		}
		return errors.New("unexpected data after manifest")
	}
	if !foundDependencies {
		return errors.New("dependencies object is missing")
	}
	return nil
}

func validateJSONNoDuplicateObjectMembers(data []byte) error {
	decoder := json.NewDecoder(bytes.NewReader(data))
	decoder.UseNumber()
	if err := validateJSONValue(decoder, 0); err != nil {
		return err
	}
	if token, err := decoder.Token(); err != io.EOF {
		if err != nil {
			return err
		}
		return fmt.Errorf("unexpected data after JSON document: %v", token)
	}
	return nil
}

func validateJSONValue(decoder *json.Decoder, depth int) error {
	if depth > maximumOwnerJSONDepth {
		return fmt.Errorf("JSON nesting exceeds %d levels", maximumOwnerJSONDepth)
	}
	token, err := decoder.Token()
	if err != nil {
		return err
	}
	delimiter, isDelimiter := token.(json.Delim)
	if !isDelimiter {
		return nil
	}
	switch delimiter {
	case '{':
		seen := make(map[string]struct{})
		for decoder.More() {
			nameToken, err := decoder.Token()
			if err != nil {
				return err
			}
			name, ok := nameToken.(string)
			if !ok {
				return fmt.Errorf("object member name is not a string: %v", nameToken)
			}
			if _, exists := seen[name]; exists {
				return fmt.Errorf("duplicate object member %q", name)
			}
			seen[name] = struct{}{}
			if err := validateJSONValue(decoder, depth+1); err != nil {
				return err
			}
		}
		closing, err := decoder.Token()
		if err != nil {
			return err
		}
		if closing != json.Delim('}') {
			return fmt.Errorf("unexpected object terminator: %v", closing)
		}
		return nil
	case '[':
		for decoder.More() {
			if err := validateJSONValue(decoder, depth+1); err != nil {
				return err
			}
		}
		closing, err := decoder.Token()
		if err != nil {
			return err
		}
		if closing != json.Delim(']') {
			return fmt.Errorf("unexpected array terminator: %v", closing)
		}
		return nil
	default:
		return fmt.Errorf("unexpected JSON delimiter %q", delimiter)
	}
}

func acquireBuildWorkspaceLease(projectRoot string) (*buildWorkspaceLease, error) {
	leasePath, err := safePath(projectRoot, leaseRelativePath, false)
	if err != nil {
		return nil, err
	}
	metadataPath, err := safePath(projectRoot, leaseMetadataRelative, false)
	if err != nil {
		return nil, err
	}
	if err := ensurePathSegmentsNotRedirected(projectRoot, filepath.Dir(leasePath)); err != nil {
		return nil, err
	}
	if err := os.MkdirAll(filepath.Dir(leasePath), 0700); err != nil {
		return nil, err
	}
	if err := ensurePathSegmentsNotRedirected(projectRoot, leasePath); err != nil {
		return nil, err
	}
	file, err := os.OpenFile(leasePath, os.O_CREATE|os.O_RDWR, 0600)
	if err != nil {
		return nil, err
	}
	if err := lockFileNonBlocking(file); err != nil {
		_ = file.Close()
		return nil, err
	}
	fileInfo, err := file.Stat()
	if err != nil {
		_ = unlockFile(file)
		_ = file.Close()
		return nil, err
	}
	pathInfo, err := os.Lstat(leasePath)
	if err != nil || !pathInfo.Mode().IsRegular() || pathInfo.Mode()&os.ModeSymlink != 0 || !os.SameFile(fileInfo, pathInfo) {
		_ = unlockFile(file)
		_ = file.Close()
		return nil, fmt.Errorf("Build workspace lease path is not bound to the locked regular file")
	}
	if redirected, redirectErr := pathIsReparsePoint(leasePath); redirectErr != nil || redirected {
		_ = unlockFile(file)
		_ = file.Close()
		return nil, fmt.Errorf("Build workspace lease path is redirected or unreadable")
	}
	lease := &buildWorkspaceLease{file: file, path: leasePath, identity: fileInfo}
	metadata := leaseMetadata{
		DocumentType: "build-workspace-lease",
		RunID:        fmt.Sprintf("cleanup-%d-%d", os.Getpid(), time.Now().UTC().UnixNano()),
		Operation:    "cleanup",
		PID:          os.Getpid(),
		StartedUTC:   time.Now().UTC().Format(time.RFC3339Nano),
	}
	data, err := json.Marshal(metadata)
	if err != nil {
		_ = lease.release()
		return nil, err
	}
	if err := writeDiagnosticAtomically(metadataPath, data); err != nil {
		_ = lease.release()
		return nil, err
	}
	return lease, nil
}

func (lease *buildWorkspaceLease) release() error {
	if lease == nil || lease.file == nil {
		return nil
	}
	file := lease.file
	lease.file = nil
	unlockErr := unlockFile(file)
	closeErr := file.Close()
	if unlockErr != nil {
		return unlockErr
	}
	return closeErr
}

func (lease *buildWorkspaceLease) validate() error {
	if lease == nil || lease.file == nil || lease.identity == nil || lease.path == "" {
		return errors.New("Build workspace lease is not active")
	}
	fileInfo, err := lease.file.Stat()
	if err != nil {
		return err
	}
	pathInfo, err := os.Lstat(lease.path)
	if err != nil {
		return err
	}
	if !fileInfo.Mode().IsRegular() || !pathInfo.Mode().IsRegular() || pathInfo.Mode()&os.ModeSymlink != 0 ||
		!os.SameFile(lease.identity, fileInfo) || !os.SameFile(fileInfo, pathInfo) {
		return errors.New("Build workspace lease file identity drifted")
	}
	if redirected, err := pathIsReparsePoint(lease.path); err != nil || redirected {
		return errors.New("Build workspace lease path became redirected or unreadable")
	}
	return nil
}

func writeDiagnosticAtomically(target string, data []byte) error {
	file, err := os.CreateTemp(filepath.Dir(target), ".lease-metadata-*")
	if err != nil {
		return err
	}
	stage := file.Name()
	if err := file.Chmod(0600); err != nil {
		_ = file.Close()
		return fmt.Errorf("diagnostic stage retained at %s: %w", stage, err)
	}
	if _, err := file.Write(data); err != nil {
		_ = file.Close()
		return fmt.Errorf("diagnostic stage retained at %s: %w", stage, err)
	}
	if err := file.Sync(); err != nil {
		_ = file.Close()
		return fmt.Errorf("diagnostic stage retained at %s: %w", stage, err)
	}
	if err := file.Close(); err != nil {
		return fmt.Errorf("diagnostic stage retained at %s: %w", stage, err)
	}
	readBack, err := os.ReadFile(stage)
	if err != nil || !bytes.Equal(readBack, data) {
		return fmt.Errorf("diagnostic read-back mismatch; stage retained at %s", stage)
	}
	if err := replacePathAtomically(stage, target); err != nil {
		return fmt.Errorf("diagnostic publish failed; stage may be retained at %s: %w", stage, err)
	}
	return nil
}

func checkUnityRunning(projectRoot string) (bool, int, error) {
	path := filepath.Join(projectRoot, "Library", "EditorInstance.json")
	data, err := os.ReadFile(path)
	if errors.Is(err, os.ErrNotExist) {
		return false, 0, nil
	}
	if err != nil {
		return false, 0, err
	}
	if len(data) == 0 || len(data) > 64*1024 {
		return false, 0, errors.New("EditorInstance.json has an invalid size")
	}
	var instance editorInstance
	if err := json.Unmarshal(data, &instance); err != nil || instance.ProcessID <= 0 {
		return false, 0, errors.New("EditorInstance.json is malformed")
	}
	running, err := processIsRunning(instance.ProcessID)
	return running, instance.ProcessID, err
}

func processIsRunning(pid int) (bool, error) {
	if runtime.GOOS == "windows" {
		command := exec.Command("tasklist", "/FI", fmt.Sprintf("PID eq %d", pid), "/NH", "/FO", "CSV")
		output, err := command.Output()
		if err != nil {
			return false, err
		}
		return strings.Contains(string(output), strconv.Itoa(pid)), nil
	}
	process, err := os.FindProcess(pid)
	if err != nil {
		return false, err
	}
	err = process.Signal(syscall.Signal(0))
	if err == nil || errors.Is(err, syscall.EPERM) {
		return true, nil
	}
	if errors.Is(err, os.ErrProcessDone) || errors.Is(err, syscall.ESRCH) {
		return false, nil
	}
	return false, err
}

func ensureNoPendingRecovery(projectRoot string) error {
	root := filepath.Join(projectRoot, filepath.FromSlash(transactionRelativePath))
	info, err := os.Lstat(root)
	if errors.Is(err, os.ErrNotExist) {
		return nil
	}
	if err != nil || !info.IsDir() {
		return fmt.Errorf("transaction root is unreadable or not a directory: %s", root)
	}
	if err := ensurePathSegmentsNotRedirected(projectRoot, root); err != nil {
		return err
	}
	return filepath.WalkDir(root, func(path string, entry fs.DirEntry, walkErr error) error {
		if walkErr != nil {
			return walkErr
		}
		if samePath(path, root) {
			return nil
		}
		if redirected, err := pathIsReparsePoint(path); err != nil || redirected {
			return fmt.Errorf("transaction evidence is redirected or unreadable: %s", path)
		}
		if err := safefs.ValidateMountBoundary(projectRoot, path); err != nil {
			return fmt.Errorf("transaction evidence crossed a mount boundary: %w", err)
		}
		relative, _ := filepath.Rel(root, path)
		depth := len(strings.Split(filepath.ToSlash(relative), "/"))
		if entry.IsDir() {
			if depth == 1 {
				return nil // Registered participant state root.
			}
			return fmt.Errorf("durable transaction directory requires explicit Build recovery: %s", path)
		}
		name := entry.Name()
		if depth == 2 && (name == "build.lock" || name == "active.lock") {
			info, err := entry.Info()
			if err != nil || info.Size() > 4096 {
				return fmt.Errorf("reusable transaction lock metadata is invalid: %s", path)
			}
			return nil
		}
		return fmt.Errorf("durable transaction evidence requires explicit Build recovery: %s", path)
	})
}

func inspectPublicationOwnership(projectRoot string, requireDeletableIdentity bool) ([]cleanItem, error) {
	var owned []cleanItem
	for _, relativeRoot := range publicationRoots {
		root, err := safePath(projectRoot, relativeRoot, true)
		if err != nil {
			if errors.Is(err, os.ErrNotExist) {
				continue
			}
			return nil, err
		}
		info, err := os.Lstat(root)
		if errors.Is(err, os.ErrNotExist) {
			continue
		}
		if err != nil || !info.IsDir() {
			return nil, fmt.Errorf("publication root is not a directory: %s", root)
		}
		if err := ensurePathSegmentsNotRedirected(projectRoot, root); err != nil {
			return nil, err
		}
		var targets []string
		var allEntries []string
		playerIdentities := make(map[string]*playerTreeIdentity)
		ownerEvidences := make(map[string]*ownerMarkerEvidence)
		entries := 0
		err = filepath.WalkDir(root, func(path string, entry fs.DirEntry, walkErr error) error {
			if walkErr != nil {
				return walkErr
			}
			entries++
			if entries > maximumCleanEntries {
				return fmt.Errorf("publication inventory exceeds %d entries", maximumCleanEntries)
			}
			if redirected, err := pathIsReparsePoint(path); err != nil || redirected {
				return fmt.Errorf("publication contains a redirected or unreadable entry: %s", path)
			}
			if err := safefs.ValidateMountBoundary(projectRoot, path); err != nil {
				return fmt.Errorf("publication crossed a mount boundary: %w", err)
			}
			if !samePath(path, root) {
				allEntries = append(allEntries, path)
			}
			if entry.IsDir() {
				return nil
			}
			switch entry.Name() {
			case ".buildpipeline-owner.json", ".yoo-pub.json":
				evidence, _, err := validateOwnerMarker(path, entry.Name(), "")
				if err != nil {
					return err
				}
				ownerEvidences[filepath.Dir(path)] = evidence
				if requireDeletableIdentity {
					return fmt.Errorf("ownership marker '%s' is recognized, but this cleaner cannot independently verify its provider-specific artifact identity; use the Build provider's recovery/cleanup path", path)
				}
				targets = append(targets, filepath.Dir(path))
			default:
				if strings.HasSuffix(entry.Name(), playerOwnerSuffix) {
					target := strings.TrimSuffix(path, playerOwnerSuffix)
					evidence, identity, err := validateOwnerMarker(path, entry.Name(), target)
					if err != nil {
						return err
					}
					playerIdentities[target] = identity
					ownerEvidences[target] = evidence
					ownerEvidences[path] = evidence
					if _, err := os.Lstat(target); err != nil {
						return fmt.Errorf("Player ownership sidecar has no output target: %s", path)
					}
					targets = append(targets, target, path)
				}
			}
			return nil
		})
		if err != nil {
			return nil, err
		}
		if len(allEntries) == 0 {
			continue
		}
		targets = minimizeTargets(targets)
		for _, candidate := range allEntries {
			covered := false
			for _, target := range targets {
				if samePath(candidate, target) || isDescendant(target, candidate) || isDescendant(candidate, target) {
					covered = true
					break
				}
			}
			if !covered {
				return nil, fmt.Errorf("entry is not covered by a valid Build ownership marker: %s", candidate)
			}
		}
		for _, target := range targets {
			info, err := os.Lstat(target)
			if err != nil {
				return nil, err
			}
			kind := "Build-owned file"
			if info.IsDir() {
				kind = "Build-owned directory"
			}
			item, err := inventoryItem(projectRoot, target, kind)
			if err != nil {
				return nil, err
			}
			item.playerIdentity = playerIdentities[target]
			item.ownerEvidence = ownerEvidences[target]
			owned = append(owned, item)
		}
	}
	return deduplicateItems(owned), nil
}

func validateOwnerMarker(path, name, target string) (*ownerMarkerEvidence, *playerTreeIdentity, error) {
	data, err := os.ReadFile(path)
	if err != nil || len(data) < 2 || len(data) > maximumOwnerBytes {
		return nil, nil, fmt.Errorf("ownership marker is unreadable or outside its byte budget: %s", path)
	}
	if err := validateJSONNoDuplicateObjectMembers(data); err != nil {
		return nil, nil, fmt.Errorf("ownership marker contains ambiguous JSON: %s: %w", path, err)
	}
	var marker buildOwnerMarker
	if err := json.Unmarshal(data, &marker); err != nil {
		return nil, nil, fmt.Errorf("ownership marker is invalid or unsupported: %s", path)
	}
	if len(marker.TransactionID) != 32 || !isHex(marker.TransactionID) {
		return nil, nil, fmt.Errorf("ownership marker transaction ID is invalid: %s", path)
	}
	if marker.Checksum != "" && (len(marker.Checksum) != 64 || !isHex(marker.Checksum)) {
		return nil, nil, fmt.Errorf("ownership marker checksum shape is invalid: %s", path)
	}
	evidence := &ownerMarkerEvidence{Path: path, SHA256: fmt.Sprintf("%X", sha256.Sum256(data)), TransactionID: marker.TransactionID}
	if name != ".buildpipeline-owner.json" && name != ".yoo-pub.json" {
		if target == "" || marker.Kind != "published" || marker.Checksum == "" {
			return nil, nil, fmt.Errorf("Player ownership marker is incomplete: %s", path)
		}
		identity, err := validatePlayerOwner(path, data, target)
		if err != nil {
			return nil, nil, err
		}
		return evidence, identity, nil
	}
	expectedDocumentType := ""
	switch name {
	case ".buildpipeline-owner.json":
		if marker.Owner == "Build.Pipeline.AddressablesPublication" {
			expectedDocumentType = "addressables-publication-owner"
		} else if marker.Owner == "Build.Pipeline.Editor.HybridCLR" {
			expectedDocumentType = "hybridclr-output-owner"
		}
	case ".yoo-pub.json":
		if marker.Owner == "Build.Pipeline.Editor.Integrations.YooAsset3" {
			expectedDocumentType = "yooasset-publication-owner"
		}
	}
	if expectedDocumentType == "" || marker.DocumentType != expectedDocumentType {
		return nil, nil, fmt.Errorf("ownership marker is invalid or unsupported: %s", path)
	}
	switch name {
	case ".buildpipeline-owner.json":
		if marker.Owner == "Build.Pipeline.AddressablesPublication" {
			if err := validateAddressablesOwner(path, data); err != nil {
				return nil, nil, err
			}
		} else if marker.Owner == "Build.Pipeline.Editor.HybridCLR" {
			if err := validateHybridCLROwner(path, data); err != nil {
				return nil, nil, err
			}
		} else {
			return nil, nil, fmt.Errorf("ownership marker owner is not recognized: %s", path)
		}
	case ".yoo-pub.json":
		if marker.Owner != "Build.Pipeline.Editor.Integrations.YooAsset3" {
			return nil, nil, fmt.Errorf("YooAsset ownership marker owner is not recognized: %s", path)
		}
		if err := validateYooAssetOwner(path, data); err != nil {
			return nil, nil, err
		}
	}
	return evidence, nil, nil
}

func validateAddressablesOwner(path string, data []byte) error {
	var marker addressablesOwnerMarker
	if err := decodeExactJSON(data, &marker); err != nil {
		return fmt.Errorf("Addressables ownership marker is ambiguous or malformed: %s: %w", path, err)
	}
	if marker.DocumentType != "addressables-publication-owner" || marker.Owner != "Build.Pipeline.AddressablesPublication" || len(marker.TransactionID) != 32 || !isHex(marker.TransactionID) || len(marker.ManifestSHA256) != 64 || !isUpperHex(marker.ManifestSHA256) {
		return fmt.Errorf("Addressables ownership marker is incomplete or unsupported: %s", path)
	}
	return nil
}

func validateHybridCLROwner(path string, data []byte) error {
	var marker hybridCLROwnerMarker
	if err := decodeExactJSON(data, &marker); err != nil {
		return fmt.Errorf("HybridCLR ownership marker is ambiguous or malformed: %s: %w", path, err)
	}
	if marker.DocumentType != "hybridclr-output-owner" || marker.Owner != "Build.Pipeline.Editor.HybridCLR" || marker.Role == "" || len(marker.TransactionID) != 32 || !isHex(marker.TransactionID) || len(marker.Files) == 0 || len(marker.Files) > 8194 {
		return fmt.Errorf("HybridCLR ownership marker is incomplete or unsupported: %s", path)
	}
	for _, entry := range marker.Files {
		if (entry.Kind != "Artifact" && entry.Kind != "Meta") || entry.Path == "" || filepath.IsAbs(entry.Path) || entry.Size < 0 || len(entry.SHA256) != 64 || !isUpperHex(entry.SHA256) {
			return fmt.Errorf("HybridCLR ownership marker contains an invalid file identity: %s", path)
		}
	}
	return nil
}

func validateYooAssetOwner(path string, data []byte) error {
	var marker yooAssetOwnerMarker
	if err := decodeExactJSON(data, &marker); err != nil {
		return fmt.Errorf("YooAsset ownership marker is ambiguous or malformed: %s: %w", path, err)
	}
	if marker.DocumentType != "yooasset-publication-owner" || marker.Owner != "Build.Pipeline.Editor.Integrations.YooAsset3" || marker.Kind == "" || marker.PackageName == "" || marker.PackageVersion == "" || marker.CryptographyAdapterID == "" || marker.RuntimeDecryptContractID == "" || len(marker.TransactionID) != 32 || !isHex(marker.TransactionID) || marker.ContentIdentity == "" || marker.EntryCount < 0 || len(marker.Checksum) != 64 || !isUpperHex(marker.Checksum) {
		return fmt.Errorf("YooAsset ownership marker is incomplete or unsupported: %s", path)
	}
	if marker.Checksum != computeYooAssetOwnerChecksum(&marker) {
		return fmt.Errorf("YooAsset ownership marker checksum verification failed: %s", path)
	}
	return nil
}

func computeYooAssetOwnerChecksum(marker *yooAssetOwnerMarker) string {
	values := []string{
		marker.DocumentType,
		marker.Owner,
		marker.Kind,
		marker.PackageName,
		marker.PackageVersion,
		marker.CryptographyAdapterID,
		marker.RuntimeDecryptContractID,
		marker.TransactionID,
		marker.ContentIdentity,
		strconv.Itoa(marker.EntryCount),
	}
	var builder strings.Builder
	for _, value := range values {
		builder.WriteString(strconv.Itoa(len(utf16.Encode([]rune(value)))))
		builder.WriteByte(':')
		builder.WriteString(value)
		builder.WriteByte(';')
	}
	return fmt.Sprintf("%X", sha256.Sum256([]byte(builder.String())))
}

func validatePlayerOwner(path string, data []byte, target string) (*playerTreeIdentity, error) {
	var marker playerOwnerMarker
	if err := decodeExactJSON(data, &marker); err != nil {
		return nil, fmt.Errorf("Player ownership marker is ambiguous or malformed: %s: %w", path, err)
	}
	if err := validatePlayerOwnerEnvelope(marker.DocumentType, marker.Kind, marker.TransactionID, marker.HasIdentity, marker.Identity, marker.CompatibilityIdentity != nil, marker.Checksum); err != nil {
		return nil, fmt.Errorf("Player ownership marker is incomplete or unsupported: %s: %w", path, err)
	}
	if err := validatePlayerCompatibilityIdentity(marker.CompatibilityIdentity); err != nil {
		return nil, fmt.Errorf("Player compatibility identity is invalid: %s: %w", path, err)
	}
	expectedChecksum := marker.Checksum
	marker.Checksum = ""
	canonical, err := marshalUnityCompatibleJSON(marker)
	if err != nil {
		return nil, err
	}
	if fmt.Sprintf("%X", sha256.Sum256(canonical)) != expectedChecksum {
		return nil, fmt.Errorf("Player ownership marker checksum verification failed: %s", path)
	}
	expectedIdentity := marker.Identity

	actualIdentity, err := computePlayerTreeIdentity(target)
	if err != nil {
		return nil, fmt.Errorf("cannot verify Player output identity '%s': %w", target, err)
	}
	if expectedIdentity.Digest != actualIdentity.Digest || expectedIdentity.EntryCount != actualIdentity.EntryCount || expectedIdentity.FileCount != actualIdentity.FileCount || expectedIdentity.TotalBytes != actualIdentity.TotalBytes {
		return nil, fmt.Errorf("Player output changed after its ownership marker was written: %s", target)
	}
	identity := *expectedIdentity
	return &identity, nil
}

func validatePlayerOwnerEnvelope(documentType, kind, transactionID string, hasIdentity bool, identity *playerTreeIdentity, hasCompatibility bool, checksum string) error {
	if documentType != "player-output-owner" || kind != "published" || !hasIdentity || identity == nil || !hasCompatibility {
		return errors.New("required ownership fields are missing")
	}
	if len(transactionID) != 32 || !isHex(transactionID) {
		return errors.New("transaction ID is invalid")
	}
	if len(checksum) != 64 || !isUpperHex(checksum) {
		return errors.New("checksum is invalid")
	}
	if len(identity.Digest) != 64 || !isUpperHex(identity.Digest) || identity.EntryCount < 0 || identity.FileCount < 0 || identity.FileCount > identity.EntryCount || identity.TotalBytes < 0 {
		return errors.New("tree identity is invalid")
	}
	return nil
}

func validatePlayerCompatibilityIdentity(identity *playerCompatibilityIdentity) error {
	if identity == nil || len(identity.PipelineImplementationFingerprint) != 64 || !isUpperHex(identity.PipelineImplementationFingerprint) {
		return errors.New("pipeline implementation fingerprint is invalid")
	}
	if identity.BuildPurpose != "Release" && identity.BuildPurpose != "Development" && identity.BuildPurpose != "LocalReleasePreview" {
		return errors.New("build purpose is invalid")
	}
	if err := validatePlayerCompatibilityFields(identity.UnityVersion, identity.BuildTarget, identity.NamedBuildTarget, identity.ScriptingBackend, identity.OutputArtifactPath, identity.PlayerExtensionFingerprint, identity.Digest); err != nil {
		return err
	}
	expected := computePlayerCompatibilityDigest(identity)
	if identity.Digest != expected {
		return errors.New("digest verification failed")
	}
	return nil
}

func validatePlayerCompatibilityFields(unityVersion, buildTarget, namedBuildTarget, scriptingBackend, outputArtifactPath, fingerprint, digest string) error {
	if unityVersion == "" || buildTarget == "" || namedBuildTarget == "" || scriptingBackend == "" || outputArtifactPath == "" {
		return errors.New("required compatibility fields are missing")
	}
	if filepath.IsAbs(outputArtifactPath) || strings.HasPrefix(outputArtifactPath, "/") || strings.HasPrefix(outputArtifactPath, `\`) {
		return errors.New("output artifact path is rooted")
	}
	if len(fingerprint) != 64 || !isLowerHex(fingerprint) {
		return errors.New("Player extension fingerprint is invalid")
	}
	if len(digest) != 64 || !isUpperHex(digest) {
		return errors.New("compatibility digest is invalid")
	}
	return nil
}

func computePlayerCompatibilityDigest(identity *playerCompatibilityIdentity) string {
	values := []string{
		"player-output-compatibility",
		identity.PipelineImplementationFingerprint,
		identity.UnityVersion,
		identity.BuildTarget,
		identity.NamedBuildTarget,
		identity.ScriptingBackend,
		identity.OutputArtifactPath,
		boolCompatibilityValue(identity.OutputIsFolder),
		identity.CompanyName,
		identity.ProductName,
		identity.ApplicationIdentifier,
		boolCompatibilityValue(identity.ExportAndroidProject),
		boolCompatibilityValue(identity.DebugBuild),
		boolCompatibilityValue(identity.DeleteDebugFiles),
		boolCompatibilityValue(identity.CheatEnabled),
		identity.BuildPurpose,
		identity.PlayerExtensionFingerprint,
	}
	return computeLengthPrefixedDigest(values)
}

func computeLengthPrefixedDigest(values []string) string {
	var builder strings.Builder
	for _, value := range values {
		builder.WriteString(strconv.Itoa(len(utf16.Encode([]rune(value)))))
		builder.WriteByte(':')
		builder.WriteString(value)
		builder.WriteByte('\n')
	}
	return fmt.Sprintf("%X", sha256.Sum256([]byte(builder.String())))
}

func boolCompatibilityValue(value bool) string {
	if value {
		return "1"
	}
	return "0"
}

func decodeExactJSON(data []byte, value interface{}) error {
	decoder := json.NewDecoder(bytes.NewReader(data))
	decoder.DisallowUnknownFields()
	if err := decoder.Decode(value); err != nil {
		return err
	}
	var extra interface{}
	if err := decoder.Decode(&extra); err != io.EOF {
		if err != nil {
			return err
		}
		return errors.New("unexpected data after JSON document")
	}
	return nil
}

func marshalUnityCompatibleJSON(value interface{}) ([]byte, error) {
	var output bytes.Buffer
	encoder := json.NewEncoder(&output)
	encoder.SetEscapeHTML(false)
	if err := encoder.Encode(value); err != nil {
		return nil, err
	}
	encoded := output.Bytes()
	if len(encoded) != 0 && encoded[len(encoded)-1] == '\n' {
		encoded = encoded[:len(encoded)-1]
	}
	return restoreUnityJSONStringSeparators(encoded), nil
}

func restoreUnityJSONStringSeparators(encoded []byte) []byte {
	requiresRewrite := bytes.Contains(encoded, []byte(`\u2028`)) || bytes.Contains(encoded, []byte(`\u2029`))
	if !requiresRewrite {
		return encoded
	}

	result := make([]byte, 0, len(encoded))
	inString := false
	for index := 0; index < len(encoded); {
		current := encoded[index]
		if !inString {
			result = append(result, current)
			index++
			if current == '"' {
				inString = true
			}
			continue
		}
		if current == '"' {
			result = append(result, current)
			index++
			inString = false
			continue
		}
		if current != '\\' || index+1 >= len(encoded) {
			result = append(result, current)
			index++
			continue
		}
		if index+5 < len(encoded) && encoded[index+1] == 'u' &&
			encoded[index+2] == '2' && encoded[index+3] == '0' && encoded[index+4] == '2' &&
			(encoded[index+5] == '8' || encoded[index+5] == '9') {
			result = append(result, 0xE2, 0x80, 0xA8+(encoded[index+5]-'8'))
			index += 6
			continue
		}
		result = append(result, current, encoded[index+1])
		index += 2
	}
	return result
}

func computePlayerTreeIdentity(root string) (*playerTreeIdentity, error) {
	rootBefore, err := os.Lstat(root)
	if err != nil || !rootBefore.IsDir() || rootBefore.Mode()&os.ModeSymlink != 0 {
		return nil, fmt.Errorf("Player output root is missing or not a directory")
	}
	if redirected, err := pathIsReparsePoint(root); err != nil || redirected {
		return nil, fmt.Errorf("Player output root is redirected or unreadable")
	}
	var entries []playerTreeEntry
	portableNames := make(map[string]bool)
	var totalBytes int64
	fileCount := 0
	err = filepath.WalkDir(root, func(path string, entry fs.DirEntry, walkErr error) error {
		if walkErr != nil {
			return walkErr
		}
		if samePath(path, root) {
			return nil
		}
		if len(entries) >= maximumCleanEntries {
			return fmt.Errorf("Player identity exceeds %d entries", maximumCleanEntries)
		}
		if redirected, err := pathIsReparsePoint(path); err != nil || redirected {
			return fmt.Errorf("Player output contains a redirected or unreadable entry: %s", path)
		}
		if err := safefs.ValidateMountBoundary(root, path); err != nil {
			return fmt.Errorf("Player output crossed a mount boundary: %w", err)
		}
		relative, err := filepath.Rel(root, path)
		if err != nil {
			return err
		}
		relative = filepath.ToSlash(relative)
		portableKey := strings.ToUpper(relative)
		if portableNames[portableKey] {
			return fmt.Errorf("Player output contains a portable casing collision: %s", relative)
		}
		portableNames[portableKey] = true
		if entry.IsDir() {
			entries = append(entries, playerTreeEntry{relativePath: relative, isDirectory: true})
			return nil
		}
		before, err := os.Lstat(path)
		if err != nil || !before.Mode().IsRegular() || before.Mode()&os.ModeSymlink != 0 {
			return fmt.Errorf("Player output entry is not a regular file: %s", path)
		}
		if redirected, err := pathIsReparsePoint(path); err != nil || redirected {
			return fmt.Errorf("Player output entry is redirected or unreadable: %s", path)
		}
		file, err := os.Open(path)
		if err != nil {
			return err
		}
		openedBefore, err := file.Stat()
		if err != nil || !openedBefore.Mode().IsRegular() || !os.SameFile(before, openedBefore) {
			_ = file.Close()
			return fmt.Errorf("Player output entry changed while opening: %s", path)
		}
		digest := sha256.New()
		_, copyErr := io.Copy(digest, file)
		openedAfter, statErr := file.Stat()
		closeErr := file.Close()
		if copyErr != nil {
			return copyErr
		}
		if statErr != nil {
			return statErr
		}
		if !openedAfter.Mode().IsRegular() {
			return fmt.Errorf("Player output entry stopped being a regular file: %s", path)
		}
		if closeErr != nil {
			return closeErr
		}
		after, err := os.Lstat(path)
		if err != nil || !after.Mode().IsRegular() || after.Mode()&os.ModeSymlink != 0 {
			return fmt.Errorf("Player output entry changed while its identity was captured: %s", path)
		}
		if redirected, err := pathIsReparsePoint(path); err != nil || redirected {
			return fmt.Errorf("Player output entry became redirected while its identity was captured: %s", path)
		}
		if !os.SameFile(before, openedBefore) || !os.SameFile(openedBefore, openedAfter) || !os.SameFile(openedAfter, after) ||
			before.Size() != openedBefore.Size() || before.Size() != openedAfter.Size() || before.Size() != after.Size() ||
			!before.ModTime().Equal(openedBefore.ModTime()) || !before.ModTime().Equal(openedAfter.ModTime()) || !before.ModTime().Equal(after.ModTime()) ||
			before.Mode().Perm() != openedBefore.Mode().Perm() || before.Mode().Perm() != openedAfter.Mode().Perm() || before.Mode().Perm() != after.Mode().Perm() {
			return fmt.Errorf("Player output changed while its identity was captured: %s", path)
		}
		totalBytes += before.Size()
		fileCount++
		entries = append(entries, playerTreeEntry{
			relativePath: relative,
			length:       before.Size(),
			hash:         fmt.Sprintf("%X", digest.Sum(nil)),
		})
		return nil
	})
	if err != nil {
		return nil, err
	}
	rootAfter, err := os.Lstat(root)
	if err != nil || !rootAfter.IsDir() || rootAfter.Mode()&os.ModeSymlink != 0 || !os.SameFile(rootBefore, rootAfter) ||
		!rootBefore.ModTime().Equal(rootAfter.ModTime()) || rootBefore.Mode().Perm() != rootAfter.Mode().Perm() {
		return nil, errors.New("Player output root changed while its identity was captured")
	}
	if redirected, err := pathIsReparsePoint(root); err != nil || redirected {
		return nil, errors.New("Player output root became redirected while its identity was captured")
	}
	sort.Slice(entries, func(left, right int) bool {
		return utf16OrdinalLess(entries[left].relativePath, entries[right].relativePath)
	})
	var digest hash.Hash = sha256.New()
	for _, entry := range entries {
		var record string
		if entry.isDirectory {
			record = "D|" + entry.relativePath + "\n"
		} else {
			record = "F|" + entry.relativePath + "|" + strconv.FormatInt(entry.length, 10) + "|" + entry.hash + "\n"
		}
		_, _ = io.WriteString(digest, record)
	}
	return &playerTreeIdentity{
		Digest:     fmt.Sprintf("%X", digest.Sum(nil)),
		EntryCount: len(entries),
		FileCount:  fileCount,
		TotalBytes: totalBytes,
	}, nil
}

func utf16OrdinalLess(left, right string) bool {
	leftUnits := utf16.Encode([]rune(left))
	rightUnits := utf16.Encode([]rune(right))
	length := len(leftUnits)
	if len(rightUnits) < length {
		length = len(rightUnits)
	}
	for index := 0; index < length; index++ {
		if leftUnits[index] != rightUnits[index] {
			return leftUnits[index] < rightUnits[index]
		}
	}
	return len(leftUnits) < len(rightUnits)
}

func collectCleanItems(projectRoot string, ownedOutputs []cleanItem, includeBuildOutputs bool) ([]cleanItem, error) {
	var items []cleanItem
	for _, relative := range cacheDirectories {
		path, err := safePath(projectRoot, relative, true)
		if errors.Is(err, os.ErrNotExist) {
			continue
		}
		if err != nil {
			return nil, err
		}
		info, err := os.Lstat(path)
		if err != nil || !info.IsDir() {
			continue
		}
		item, err := inventoryItem(projectRoot, path, "cache directory")
		if err != nil {
			return nil, err
		}
		items = append(items, item)
	}

	// Temp contains the authoritative lease. Delete only siblings outside the
	// BuildPipeline workspace, then delete BuildPipeline children other than Workspace.
	tempRoot := filepath.Join(projectRoot, "Temp")
	if entries, err := os.ReadDir(tempRoot); err == nil {
		for _, entry := range entries {
			if entry.Name() == "BuildPipeline" {
				buildTemp := filepath.Join(tempRoot, entry.Name())
				children, childErr := os.ReadDir(buildTemp)
				if childErr != nil {
					return nil, childErr
				}
				for _, child := range children {
					if child.Name() == "Workspace" {
						continue
					}
					item, err := inventoryItem(projectRoot, filepath.Join(buildTemp, child.Name()), "temporary cache")
					if err != nil {
						return nil, err
					}
					items = append(items, item)
				}
				continue
			}
			item, err := inventoryItem(projectRoot, filepath.Join(tempRoot, entry.Name()), "temporary cache")
			if err != nil {
				return nil, err
			}
			items = append(items, item)
		}
	} else if !errors.Is(err, os.ErrNotExist) {
		return nil, err
	}

	if includeBuildOutputs {
		items = append(items, ownedOutputs...)
	}
	items = deduplicateItems(items)
	if err := validateCleanItemInventory(projectRoot, items); err != nil {
		return nil, err
	}
	return items, nil
}

func validateCleanItemInventory(projectRoot string, items []cleanItem) error {
	for index := range items {
		if err := validateDeleteTarget(projectRoot, items[index].path); err != nil {
			return err
		}
		for otherIndex := index + 1; otherIndex < len(items); otherIndex++ {
			if isDescendant(items[index].path, items[otherIndex].path) || isDescendant(items[otherIndex].path, items[index].path) {
				return fmt.Errorf(
					"cleanup inventory contains overlapping parent/child targets: '%s' and '%s'",
					items[index].path,
					items[otherIndex].path)
			}
		}
	}
	return nil
}

func cleanItemInventoriesEqual(expected, actual []cleanItem) bool {
	if len(expected) != len(actual) {
		return false
	}
	for index := range expected {
		if !samePath(expected[index].path, actual[index].path) || expected[index].kind != actual[index].kind || expected[index].size != actual[index].size ||
			expected[index].identity == nil || actual[index].identity == nil || !os.SameFile(expected[index].identity, actual[index].identity) ||
			!playerTreeIdentitiesEqual(expected[index].playerIdentity, actual[index].playerIdentity) ||
			!ownerMarkerEvidencesEqual(expected[index].ownerEvidence, actual[index].ownerEvidence) {
			return false
		}
	}
	return true
}

func ownerMarkerEvidencesEqual(left, right *ownerMarkerEvidence) bool {
	if left == nil || right == nil {
		return left == nil && right == nil
	}
	return samePath(left.Path, right.Path) && left.SHA256 == right.SHA256 && left.TransactionID == right.TransactionID
}

func playerTreeIdentitiesEqual(left, right *playerTreeIdentity) bool {
	if left == nil || right == nil {
		return left == nil && right == nil
	}
	return left.Digest == right.Digest && left.EntryCount == right.EntryCount && left.FileCount == right.FileCount && left.TotalBytes == right.TotalBytes
}

func inventoryItem(projectRoot, path, kind string) (cleanItem, error) {
	if err := validateDeleteTarget(projectRoot, path); err != nil {
		return cleanItem{}, err
	}
	before, err := os.Lstat(path)
	if err != nil || before.Mode()&os.ModeSymlink != 0 || (!before.IsDir() && !before.Mode().IsRegular()) {
		return cleanItem{}, fmt.Errorf("cleanup target root is unavailable, redirected, or unsupported: %s", path)
	}
	if redirected, err := pathIsReparsePoint(path); err != nil || redirected {
		return cleanItem{}, fmt.Errorf("cleanup target root is redirected or unreadable: %s", path)
	}
	size, err := pathSize(path)
	if err != nil {
		return cleanItem{}, err
	}
	after, err := os.Lstat(path)
	if err != nil || after.Mode()&os.ModeSymlink != 0 || !os.SameFile(before, after) ||
		before.Size() != after.Size() || !before.ModTime().Equal(after.ModTime()) || before.Mode().Perm() != after.Mode().Perm() {
		return cleanItem{}, fmt.Errorf("cleanup target root changed during inventory: %s", path)
	}
	return cleanItem{path: path, kind: kind, size: size, identity: after}, nil
}

func validateDeleteTarget(projectRoot, target string) error {
	target = filepath.Clean(target)
	if samePath(projectRoot, target) || !isDescendant(projectRoot, target) {
		return fmt.Errorf("delete target escapes or equals the project root: %s", target)
	}
	durableRoot := filepath.Join(projectRoot, ".buildpipeline")
	if samePath(target, durableRoot) || isDescendant(target, durableRoot) || isDescendant(durableRoot, target) {
		return fmt.Errorf("durable Build evidence is never a cleanup target: %s", target)
	}
	workspaceRoot := filepath.Join(projectRoot, filepath.FromSlash("Temp/BuildPipeline/Workspace"))
	if samePath(target, workspaceRoot) || isDescendant(target, workspaceRoot) || isDescendant(workspaceRoot, target) {
		return fmt.Errorf("active Build workspace lease is never a cleanup target: %s", target)
	}
	return ensurePathSegmentsNotRedirected(projectRoot, target)
}

func ensureNoStaleCleanupQuarantine(projectRoot string) error {
	root := filepath.Join(projectRoot, filepath.FromSlash(quarantineRelativePath))
	info, err := os.Lstat(root)
	if errors.Is(err, os.ErrNotExist) {
		return nil
	}
	if err != nil || !info.IsDir() || info.Mode()&os.ModeSymlink != 0 {
		return fmt.Errorf("cleanup quarantine root is unreadable, redirected, or not a directory: %s", root)
	}
	if err := ensurePathSegmentsNotRedirected(projectRoot, root); err != nil {
		return err
	}
	entries, err := os.ReadDir(root)
	if err != nil {
		return err
	}
	if len(entries) != 0 {
		return fmt.Errorf("cleanup quarantine contains recovery evidence: %s", filepath.Join(root, entries[0].Name()))
	}
	return nil
}

func executeQuarantinedCleanup(projectRoot string, lease *buildWorkspaceLease, items []cleanItem, output io.Writer) (int, int, int64) {
	transactionRoot, journalPath, journal, err := createQuarantineTransaction(projectRoot, items)
	if err != nil {
		fmt.Fprintf(output, "[FAIL] Cannot create cleanup quarantine transaction: %v\n", err)
		return 0, 1, 0
	}
	quarantineRoot, err := openBoundQuarantineRoot(projectRoot, transactionRoot)
	if err != nil {
		fmt.Fprintf(output, "[FAIL] Cannot bind cleanup quarantine to its directory handle: %v. Recovery evidence: %s\n", err, transactionRoot)
		return 0, 1, 0
	}
	defer quarantineRoot.close()
	claimed := make([]claimedCleanItem, 0, len(items))
	failClaim := func(cause error) (int, int, int64) {
		if bindErr := quarantineRoot.validate(projectRoot); bindErr != nil {
			fmt.Fprintf(output, "[FAIL] Cleanup claim failed: %v. Quarantine directory identity also drifted, so no path-based rollback or journal overwrite was attempted: %v. Recovery evidence was originally rooted at %s\n", cause, bindErr, transactionRoot)
			return 0, 1, 0
		}
		journal.State = "recovery-required"
		_ = writeQuarantineJournal(journalPath, journal, false)
		rollbackErr := rollbackQuarantineClaims(claimed, journalPath, journal)
		if rollbackErr == nil {
			if bindErr := quarantineRoot.validate(projectRoot); bindErr != nil {
				rollbackErr = fmt.Errorf("quarantine root drifted after rollback: %w", bindErr)
			}
		}
		if rollbackErr == nil {
			_ = quarantineRoot.close()
			rollbackErr = cleanupEmptyQuarantineTransaction(transactionRoot, journalPath)
		}
		if rollbackErr != nil {
			fmt.Fprintf(output, "[FAIL] Cleanup claim failed: %v. Rollback was not fully proven: %v. Recovery evidence: %s\n", cause, rollbackErr, transactionRoot)
		} else {
			fmt.Fprintf(output, "[FAIL] Cleanup claim failed and all claimed paths were restored without replacement: %v\n", cause)
		}
		return 0, 1, 0
	}

	if err := lease.validate(); err != nil {
		return failClaim(fmt.Errorf("Build workspace lease drifted before claim: %w", err))
	}
	for index, item := range items {
		if err := quarantineRoot.validate(projectRoot); err != nil {
			return failClaim(fmt.Errorf("quarantine directory drifted before claim: %w", err))
		}
		if err := validateBoundCleanItem(projectRoot, item, item.path, false); err != nil {
			return failClaim(err)
		}
		claimedPath := journal.Entries[index].ClaimedPath
		moveErr := safefs.MoveNoReplace(item.path, claimedPath)
		claimedInfo, claimedErr := os.Lstat(claimedPath)
		if claimedErr == nil && item.identity != nil && os.SameFile(item.identity, claimedInfo) {
			claimed = append(claimed, claimedCleanItem{item: item, claimedPath: claimedPath, claimedIdentity: claimedInfo, entryIndex: index})
		}
		if moveErr != nil {
			return failClaim(fmt.Errorf("no-replace quarantine move failed for '%s': %w", item.path, moveErr))
		}
		if claimedErr != nil {
			return failClaim(fmt.Errorf("quarantined target cannot be inspected: %s: %w", claimedPath, claimedErr))
		}
		if err := validateBoundCleanItem(projectRoot, item, claimedPath, true); err != nil {
			return failClaim(err)
		}
		journal.Entries[index].State = "claimed-and-verified"
		if err := quarantineRoot.validate(projectRoot); err != nil {
			return failClaim(fmt.Errorf("quarantine directory drifted before claim journaling: %w", err))
		}
		if err := writeQuarantineJournal(journalPath, journal, false); err != nil {
			return failClaim(fmt.Errorf("cannot persist claim state: %w", err))
		}
	}

	for _, claim := range claimed {
		if err := validateBoundCleanItem(projectRoot, claim.item, claim.claimedPath, true); err != nil {
			return failClaim(fmt.Errorf("quarantine identity changed before delete phase: %w", err))
		}
	}
	if err := lease.validate(); err != nil {
		return failClaim(fmt.Errorf("Build workspace lease drifted before physical deletion: %w", err))
	}
	if running, pid, err := checkUnityRunning(projectRoot); err != nil || running {
		if err != nil {
			return failClaim(fmt.Errorf("Unity activity cannot be proven idle before physical deletion: %w", err))
		}
		return failClaim(fmt.Errorf("Unity Editor started before physical deletion (PID %d)", pid))
	}
	if err := ensureNoPendingRecovery(projectRoot); err != nil {
		return failClaim(fmt.Errorf("Build recovery state changed before physical deletion: %w", err))
	}
	if err := quarantineRoot.validate(projectRoot); err != nil {
		return failClaim(fmt.Errorf("quarantine directory drifted before physical deletion: %w", err))
	}

	journal.State = "deleting"
	if err := writeQuarantineJournal(journalPath, journal, false); err != nil {
		return failClaim(fmt.Errorf("cannot persist delete-phase journal: %w", err))
	}
	deleted := 0
	var freed int64
	for _, claim := range claimed {
		if err := lease.validate(); err != nil {
			journal.State = "recovery-required"
			_ = writeQuarantineJournal(journalPath, journal, false)
			fmt.Fprintf(output, "[FAIL] Lease identity changed during physical deletion: %v. Remaining recovery evidence: %s\n", err, transactionRoot)
			return deleted, 1, freed
		}
		if err := validateBoundCleanItem(projectRoot, claim.item, claim.claimedPath, true); err != nil {
			journal.State = "recovery-required"
			_ = writeQuarantineJournal(journalPath, journal, false)
			fmt.Fprintf(output, "[FAIL] Quarantined target identity changed before removal: %v. Recovery evidence: %s\n", err, transactionRoot)
			return deleted, 1, freed
		}
		if err := quarantineRoot.validate(projectRoot); err != nil {
			journal.State = "recovery-required"
			fmt.Fprintf(output, "[FAIL] Quarantine directory identity changed before removal: %v. No path-based deletion was attempted. Recovery evidence was originally rooted at %s\n", err, transactionRoot)
			return deleted, 1, freed
		}
		journal.Entries[claim.entryIndex].State = "deleting"
		if err := writeQuarantineJournal(journalPath, journal, false); err != nil {
			fmt.Fprintf(output, "[FAIL] Cannot persist per-target delete intent: %v. Recovery evidence: %s\n", err, transactionRoot)
			return deleted, 1, freed
		}
		if err := quarantineRoot.removeAll(claim.claimedPath); err != nil {
			journal.State = "recovery-required"
			if bindErr := quarantineRoot.validate(projectRoot); bindErr == nil {
				_ = writeQuarantineJournal(journalPath, journal, false)
			}
			fmt.Fprintf(output, "[FAIL] Quarantined target deletion failed without permission mutation: %v. Recovery evidence: %s\n", err, transactionRoot)
			return deleted, 1, freed
		}
		if err := quarantineRoot.validate(projectRoot); err != nil {
			fmt.Fprintf(output, "[FAIL] Target was removed through the bound quarantine handle, but the canonical quarantine directory drifted before journaling: %v. Recovery evidence was originally rooted at %s\n", err, transactionRoot)
			return deleted + 1, 1, freed + claim.item.size
		}
		journal.Entries[claim.entryIndex].State = "deleted"
		if err := writeQuarantineJournal(journalPath, journal, false); err != nil {
			fmt.Fprintf(output, "[FAIL] Target was deleted but the journal update failed: %v. Recovery evidence: %s\n", err, transactionRoot)
			return deleted + 1, 1, freed + claim.item.size
		}
		fmt.Fprintf(output, "[OK] Deleted %s through quarantine: %s (%s)\n", claim.item.kind, claim.item.path, formatSize(claim.item.size))
		deleted++
		freed += claim.item.size
	}
	journal.State = "complete"
	if err := quarantineRoot.validate(projectRoot); err != nil {
		fmt.Fprintf(output, "[FAIL] Cleanup completed through the bound quarantine handle, but its canonical directory drifted before final journaling: %v. Recovery evidence was originally rooted at %s\n", err, transactionRoot)
		return deleted, 1, freed
	}
	if err := writeQuarantineJournal(journalPath, journal, false); err != nil {
		fmt.Fprintf(output, "[FAIL] Cleanup completed but final journal write failed: %v. Recovery evidence: %s\n", err, transactionRoot)
		return deleted, 1, freed
	}
	if err := quarantineRoot.validate(projectRoot); err != nil {
		fmt.Fprintf(output, "[FAIL] Cleanup completed but quarantine identity changed before finalization: %v. Recovery evidence was originally rooted at %s\n", err, transactionRoot)
		return deleted, 1, freed
	}
	_ = quarantineRoot.close()
	if err := cleanupEmptyQuarantineTransaction(transactionRoot, journalPath); err != nil {
		fmt.Fprintf(output, "[FAIL] Cleanup completed but quarantine finalization failed: %v. Recovery evidence: %s\n", err, transactionRoot)
		return deleted, 1, freed
	}
	return deleted, 0, freed
}

func openBoundQuarantineRoot(projectRoot, transactionRoot string) (*boundQuarantineRoot, error) {
	identity, err := os.Lstat(transactionRoot)
	if err != nil || !identity.IsDir() || identity.Mode()&os.ModeSymlink != 0 {
		return nil, fmt.Errorf("quarantine transaction is unavailable, redirected, or not a directory: %s", transactionRoot)
	}
	if err := ensurePathSegmentsNotRedirected(projectRoot, transactionRoot); err != nil {
		return nil, err
	}
	root, err := os.OpenRoot(transactionRoot)
	if err != nil {
		return nil, err
	}
	binding := &boundQuarantineRoot{root: root, path: transactionRoot, identity: identity}
	if err := binding.validate(projectRoot); err != nil {
		_ = root.Close()
		return nil, err
	}
	return binding, nil
}

func (binding *boundQuarantineRoot) validate(projectRoot string) error {
	if binding == nil || binding.root == nil || binding.identity == nil || binding.closed {
		return errors.New("quarantine directory handle is not active")
	}
	rooted, err := binding.root.Stat(".")
	if err != nil || !rooted.IsDir() || !os.SameFile(binding.identity, rooted) {
		return errors.New("opened quarantine directory identity drifted")
	}
	canonical, err := os.Lstat(binding.path)
	if err != nil || !canonical.IsDir() || canonical.Mode()&os.ModeSymlink != 0 || !os.SameFile(binding.identity, canonical) {
		return errors.New("canonical quarantine directory no longer names the opened directory")
	}
	if err := ensurePathSegmentsNotRedirected(projectRoot, binding.path); err != nil {
		return err
	}
	return safefs.ValidateMountBoundary(projectRoot, binding.path)
}

func (binding *boundQuarantineRoot) removeAll(claimedPath string) error {
	if binding == nil || binding.root == nil || binding.closed {
		return errors.New("quarantine directory handle is not active")
	}
	relative, err := filepath.Rel(binding.path, claimedPath)
	if err != nil || relative == "." || filepath.IsAbs(relative) || filepath.Dir(relative) != "." {
		return fmt.Errorf("claimed path is not an immediate quarantine child: %s", claimedPath)
	}
	entries := 0
	if err := removeRootedTree(binding.root, relative, 0, &entries); err != nil {
		return err
	}
	if _, err := binding.root.Lstat(relative); err == nil {
		return fmt.Errorf("rooted recursive removal returned success but target still exists: %s", claimedPath)
	} else if !errors.Is(err, os.ErrNotExist) {
		return err
	}
	if err := safefs.SyncParent(claimedPath); err != nil {
		return fmt.Errorf("rooted recursive removal completed but parent sync failed: %w", err)
	}
	return nil
}

func removeRootedTree(root *os.Root, relative string, depth int, entries *int) error {
	if depth > maximumCleanDepth {
		return fmt.Errorf("rooted deletion exceeds depth %d: %s", maximumCleanDepth, relative)
	}
	(*entries)++
	if *entries > maximumCleanEntries {
		return fmt.Errorf("rooted deletion exceeds %d entries", maximumCleanEntries)
	}
	info, err := root.Lstat(relative)
	if errors.Is(err, os.ErrNotExist) {
		return nil
	}
	if err != nil {
		return err
	}
	if !info.IsDir() || info.Mode()&os.ModeSymlink != 0 {
		return root.Remove(relative)
	}
	for {
		directory, err := root.Open(relative)
		if err != nil {
			return err
		}
		children, readErr := directory.ReadDir(256)
		closeErr := directory.Close()
		if readErr != nil && !errors.Is(readErr, io.EOF) {
			return readErr
		}
		if closeErr != nil {
			return closeErr
		}
		if len(children) == 0 {
			return root.Remove(relative)
		}
		for _, child := range children {
			if err := removeRootedTree(root, filepath.Join(relative, child.Name()), depth+1, entries); err != nil {
				return err
			}
		}
	}
}

func (binding *boundQuarantineRoot) close() error {
	if binding == nil || binding.root == nil || binding.closed {
		return nil
	}
	binding.closed = true
	return binding.root.Close()
}

func createQuarantineTransaction(projectRoot string, items []cleanItem) (string, string, *quarantineJournal, error) {
	parent := filepath.Join(projectRoot, filepath.FromSlash(quarantineRelativePath))
	if err := ensurePathSegmentsNotRedirected(projectRoot, filepath.Dir(parent)); err != nil {
		return "", "", nil, err
	}
	if err := os.MkdirAll(parent, 0700); err != nil {
		return "", "", nil, err
	}
	if err := safefs.SyncParent(parent); err != nil {
		return "", "", nil, err
	}
	if err := ensureNoStaleCleanupQuarantine(projectRoot); err != nil {
		return "", "", nil, err
	}
	randomBytes := make([]byte, 16)
	if _, err := rand.Read(randomBytes); err != nil {
		return "", "", nil, err
	}
	transactionID := fmt.Sprintf("%x", randomBytes)
	transactionRoot := filepath.Join(parent, "cleanup-"+transactionID)
	if err := safefs.CreateExclusiveDirectory(transactionRoot, 0700); err != nil {
		return transactionRoot, "", nil, fmt.Errorf("exclusive quarantine directory creation failed; possible recovery evidence at %s: %w", transactionRoot, err)
	}
	journal := &quarantineJournal{
		DocumentType:  "build-cleanup-quarantine",
		TransactionID: transactionID,
		State:         "planned",
		StartedUTC:    time.Now().UTC().Format(time.RFC3339Nano),
		Entries:       make([]quarantineEntry, len(items)),
	}
	for index, item := range items {
		entry := quarantineEntry{
			OriginalPath: item.path,
			ClaimedPath:  filepath.Join(transactionRoot, fmt.Sprintf("item-%06d", index)),
			Kind:         item.kind,
			State:        "planned",
		}
		if item.ownerEvidence != nil {
			entry.OwnerMarkerPath = item.ownerEvidence.Path
			entry.OwnerMarkerSHA256 = item.ownerEvidence.SHA256
			entry.OwnerMarkerTransactionID = item.ownerEvidence.TransactionID
		}
		if item.playerIdentity != nil {
			entry.PlayerTreeDigest = item.playerIdentity.Digest
		}
		journal.Entries[index] = entry
	}
	journalPath := filepath.Join(transactionRoot, "transaction.json")
	if err := writeQuarantineJournal(journalPath, journal, true); err != nil {
		return transactionRoot, journalPath, journal, fmt.Errorf("initial quarantine journal failed; recovery evidence retained at %s: %w", transactionRoot, err)
	}
	return transactionRoot, journalPath, journal, nil
}

func writeQuarantineJournal(path string, journal *quarantineJournal, initial bool) error {
	data, err := json.Marshal(journal)
	if err != nil {
		return err
	}
	if initial {
		file, err := os.CreateTemp(filepath.Dir(path), ".transaction-stage-*")
		if err != nil {
			return err
		}
		stage := file.Name()
		if err := file.Chmod(0600); err != nil {
			_ = file.Close()
			return err
		}
		if _, err := file.Write(data); err != nil {
			_ = file.Close()
			return err
		}
		if err := file.Sync(); err != nil {
			_ = file.Close()
			return err
		}
		if err := file.Close(); err != nil {
			return err
		}
		readBack, err := os.ReadFile(stage)
		if err != nil || !bytes.Equal(readBack, data) {
			return errors.New("quarantine journal stage read-back mismatch")
		}
		return safefs.PublishFileNoReplace(stage, path)
	}
	return writeDiagnosticAtomically(path, data)
}

func validateBoundCleanItem(projectRoot string, item cleanItem, path string, inQuarantine bool) error {
	if item.identity == nil {
		return fmt.Errorf("cleanup item has no bound root identity: %s", item.path)
	}
	if !inQuarantine {
		if err := validateDeleteTarget(projectRoot, path); err != nil {
			return err
		}
	} else if !isDescendant(filepath.Join(projectRoot, filepath.FromSlash(quarantineRelativePath)), path) {
		return fmt.Errorf("claimed path escapes cleanup quarantine: %s", path)
	}
	info, err := os.Lstat(path)
	if err != nil || info.Mode()&os.ModeSymlink != 0 || (!info.IsDir() && !info.Mode().IsRegular()) {
		return fmt.Errorf("cleanup item root is unavailable, redirected, or unsupported: %s", path)
	}
	if redirected, err := pathIsReparsePoint(path); err != nil || redirected {
		return fmt.Errorf("cleanup item root is redirected or unreadable: %s", path)
	}
	if !os.SameFile(item.identity, info) {
		return fmt.Errorf("cleanup item root file identity drifted: %s", path)
	}
	if item.ownerEvidence != nil {
		markerPath := item.ownerEvidence.Path
		if inQuarantine {
			relative, err := filepath.Rel(item.path, item.ownerEvidence.Path)
			if err != nil {
				return err
			}
			if relative == ".." || strings.HasPrefix(relative, ".."+string(os.PathSeparator)) {
				markerPath = "" // Player sidecar is a separately claimed clean item.
			} else {
				markerPath = filepath.Join(path, relative)
			}
		}
		if markerPath != "" {
			data, _, err := readBoundedStableFile(projectRoot, markerPath, maximumOwnerBytes)
			if err != nil {
				return fmt.Errorf("owner marker evidence cannot be re-read: %w", err)
			}
			if fmt.Sprintf("%X", sha256.Sum256(data)) != item.ownerEvidence.SHA256 {
				return fmt.Errorf("owner marker SHA-256 drifted: %s", markerPath)
			}
			var marker buildOwnerMarker
			if err := json.Unmarshal(data, &marker); err != nil || marker.TransactionID != item.ownerEvidence.TransactionID {
				return fmt.Errorf("owner marker transaction ID drifted: %s", markerPath)
			}
		}
	}
	if item.playerIdentity != nil {
		actual, err := computePlayerTreeIdentity(path)
		if err != nil {
			return err
		}
		if !playerTreeIdentitiesEqual(item.playerIdentity, actual) {
			return fmt.Errorf("Player output identity drifted after quarantine claim: %s", path)
		}
	}
	return nil
}

func rollbackQuarantineClaims(claimed []claimedCleanItem, journalPath string, journal *quarantineJournal) error {
	var failures []string
	for index := len(claimed) - 1; index >= 0; index-- {
		claim := claimed[index]
		info, err := os.Lstat(claim.claimedPath)
		if errors.Is(err, os.ErrNotExist) {
			failures = append(failures, fmt.Sprintf("claimed path disappeared: %s", claim.claimedPath))
			continue
		}
		if err != nil || claim.claimedIdentity == nil || !os.SameFile(claim.claimedIdentity, info) || info.Mode()&os.ModeSymlink != 0 {
			failures = append(failures, fmt.Sprintf("claimed path identity drifted: %s", claim.claimedPath))
			continue
		}
		if err := safefs.MoveNoReplace(claim.claimedPath, claim.item.path); err != nil {
			failures = append(failures, fmt.Sprintf("%s: %v", claim.item.path, err))
			continue
		}
		restored, err := os.Lstat(claim.item.path)
		if err != nil || !os.SameFile(claim.claimedIdentity, restored) {
			failures = append(failures, fmt.Sprintf("rollback identity could not be proven: %s", claim.item.path))
			continue
		}
		journal.Entries[claim.entryIndex].State = "rolled-back"
		_ = writeQuarantineJournal(journalPath, journal, false)
	}
	if len(failures) != 0 {
		return errors.New(strings.Join(failures, "; "))
	}
	journal.State = "rolled-back"
	return writeQuarantineJournal(journalPath, journal, false)
}

func cleanupEmptyQuarantineTransaction(transactionRoot, journalPath string) error {
	entries, err := os.ReadDir(transactionRoot)
	if err != nil {
		return err
	}
	if len(entries) != 1 || entries[0].Name() != filepath.Base(journalPath) {
		return fmt.Errorf("quarantine transaction is not empty after rollback/finalization: %s", transactionRoot)
	}
	if err := safefs.RemoveDurably(journalPath); err != nil {
		return err
	}
	return safefs.RemoveDurably(transactionRoot)
}

func safePath(projectRoot, relative string, requireExisting bool) (string, error) {
	target := filepath.Clean(filepath.Join(projectRoot, filepath.FromSlash(relative)))
	if !isDescendant(projectRoot, target) {
		return "", fmt.Errorf("path escapes project root: %s", relative)
	}
	if requireExisting {
		if _, err := os.Lstat(target); err != nil {
			return "", err
		}
	}
	return target, nil
}

func ensurePathSegmentsNotRedirected(projectRoot, target string) error {
	if !isDescendant(projectRoot, target) && !samePath(projectRoot, target) {
		return fmt.Errorf("path escapes project root: %s", target)
	}
	relative, err := filepath.Rel(projectRoot, target)
	if err != nil {
		return err
	}
	current := projectRoot
	for _, segment := range strings.Split(filepath.Clean(relative), string(os.PathSeparator)) {
		if segment == "." || segment == "" {
			continue
		}
		current = filepath.Join(current, segment)
		if _, err := os.Lstat(current); errors.Is(err, os.ErrNotExist) {
			continue
		} else if err != nil {
			return err
		}
		redirected, err := pathIsReparsePoint(current)
		if err != nil {
			return err
		}
		if redirected {
			return fmt.Errorf("path contains a symbolic link or reparse point: %s", current)
		}
		if err := safefs.ValidateMountBoundary(projectRoot, current); err != nil {
			return err
		}
	}
	return nil
}

func pathSize(path string) (int64, error) {
	var size int64
	entries := 0
	baseDepth := len(strings.Split(filepath.Clean(path), string(os.PathSeparator)))
	err := filepath.WalkDir(path, func(current string, entry fs.DirEntry, walkErr error) error {
		if walkErr != nil {
			return walkErr
		}
		entries++
		if entries > maximumCleanEntries {
			return fmt.Errorf("inventory exceeds %d entries: %s", maximumCleanEntries, path)
		}
		depth := len(strings.Split(filepath.Clean(current), string(os.PathSeparator))) - baseDepth
		if depth > maximumCleanDepth {
			return fmt.Errorf("inventory exceeds depth %d: %s", maximumCleanDepth, path)
		}
		if redirected, err := pathIsReparsePoint(current); err != nil || redirected {
			return fmt.Errorf("inventory contains redirected or unreadable path: %s", current)
		}
		if err := safefs.ValidateMountBoundary(path, current); err != nil {
			return fmt.Errorf("inventory crossed a mount boundary: %w", err)
		}
		if !entry.IsDir() {
			info, err := entry.Info()
			if err != nil {
				return err
			}
			size += info.Size()
		}
		return nil
	})
	return size, err
}

func minimizeTargets(targets []string) []string {
	sort.Slice(targets, func(left, right int) bool {
		if len(targets[left]) == len(targets[right]) {
			return targets[left] < targets[right]
		}
		return len(targets[left]) < len(targets[right])
	})
	var result []string
	for _, target := range targets {
		covered := false
		for _, parent := range result {
			if samePath(parent, target) || isDescendant(parent, target) {
				covered = true
				break
			}
		}
		if !covered {
			result = append(result, target)
		}
	}
	return result
}

func deduplicateItems(items []cleanItem) []cleanItem {
	sort.Slice(items, func(left, right int) bool { return items[left].path < items[right].path })
	result := make([]cleanItem, 0, len(items))
	for _, item := range items {
		if len(result) != 0 && samePath(result[len(result)-1].path, item.path) {
			continue
		}
		result = append(result, item)
	}
	return result
}

func printPreview(output io.Writer, projectRoot string, items, ownedOutputs []cleanItem, includeOutputs bool) {
	fmt.Fprintf(output, "Project: %s\n", projectRoot)
	if len(ownedOutputs) != 0 && !includeOutputs {
		fmt.Fprintf(output, "Protected Build-owned publications: %d (use -include-build-outputs after review).\n", len(ownedOutputs))
	}
	var total int64
	for _, item := range items {
		relative, _ := filepath.Rel(projectRoot, item.path)
		fmt.Fprintf(output, "  [%s] %s (%s)\n", item.kind, filepath.ToSlash(relative), formatSize(item.size))
		total += item.size
	}
	fmt.Fprintf(output, "Planned deletion: %d items, %s.\n", len(items), formatSize(total))
}

func formatSize(value int64) string {
	const kilo = int64(1024)
	const mega = 1024 * kilo
	const giga = 1024 * mega
	switch {
	case value >= giga:
		return fmt.Sprintf("%.2f GiB", float64(value)/float64(giga))
	case value >= mega:
		return fmt.Sprintf("%.2f MiB", float64(value)/float64(mega))
	case value >= kilo:
		return fmt.Sprintf("%.2f KiB", float64(value)/float64(kilo))
	default:
		return fmt.Sprintf("%d B", value)
	}
}

func isDescendant(root, candidate string) bool {
	relative, err := filepath.Rel(filepath.Clean(root), filepath.Clean(candidate))
	if err != nil || relative == "." || relative == "" || filepath.IsAbs(relative) {
		return false
	}
	return relative != ".." && !strings.HasPrefix(relative, ".."+string(os.PathSeparator))
}

func samePath(left, right string) bool {
	if os.PathSeparator == '\\' {
		return strings.EqualFold(filepath.Clean(left), filepath.Clean(right))
	}
	return filepath.Clean(left) == filepath.Clean(right)
}

func isHex(value string) bool {
	for _, character := range value {
		if !((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f') || (character >= 'A' && character <= 'F')) {
			return false
		}
	}
	return true
}

func isUpperHex(value string) bool {
	for _, character := range value {
		if !((character >= '0' && character <= '9') || (character >= 'A' && character <= 'F')) {
			return false
		}
	}
	return true
}

func isLowerHex(value string) bool {
	for _, character := range value {
		if !((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f')) {
			return false
		}
	}
	return true
}
