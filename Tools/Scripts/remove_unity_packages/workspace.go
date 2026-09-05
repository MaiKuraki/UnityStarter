package remove_unity_packages

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io/fs"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"strconv"
	"strings"
	"syscall"
	"time"

	"cyclonegames.tools/scripts/internal/safefs"
)

const (
	buildLeaseRelativePath       = "Temp/BuildPipeline/Workspace/lease.lock"
	buildLeaseMetadataRelative   = "Temp/BuildPipeline/Workspace/lease.json"
	buildTransactionRelativePath = ".buildpipeline/transactions"
)

type buildWorkspaceLease struct {
	file     *os.File
	path     string
	identity os.FileInfo
}

type buildLeaseMetadata struct {
	DocumentType string `json:"documentType"`
	RunID        string `json:"runId"`
	Operation    string `json:"operation"`
	PID          int    `json:"pid"`
	StartedUTC   string `json:"startedUtc"`
}

type unityEditorInstance struct {
	ProcessID int `json:"process_id"`
}

func acquireBuildWorkspaceLease(projectRoot string) (*buildWorkspaceLease, error) {
	leasePath := filepath.Join(projectRoot, filepath.FromSlash(buildLeaseRelativePath))
	metadataPath := filepath.Join(projectRoot, filepath.FromSlash(buildLeaseMetadataRelative))
	if !pathIsDescendant(projectRoot, leasePath) || !pathIsDescendant(projectRoot, metadataPath) {
		return nil, errors.New("Build workspace lease path escapes the project root")
	}
	if err := ensureWorkspaceSegmentsNotRedirected(projectRoot, filepath.Dir(leasePath)); err != nil {
		return nil, err
	}
	if err := os.MkdirAll(filepath.Dir(leasePath), 0700); err != nil {
		return nil, err
	}
	if err := ensureWorkspaceSegmentsNotRedirected(projectRoot, leasePath); err != nil {
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
		return nil, errors.New("Build workspace lease path is not bound to the locked regular file")
	}
	if redirected, redirectErr := pathIsReparsePoint(leasePath); redirectErr != nil || redirected {
		_ = unlockFile(file)
		_ = file.Close()
		return nil, errors.New("Build workspace lease path is redirected or unreadable")
	}
	lease := &buildWorkspaceLease{file: file, path: leasePath, identity: fileInfo}
	metadata := buildLeaseMetadata{
		DocumentType: "build-workspace-lease",
		RunID:        fmt.Sprintf("package-removal-%d-%d", os.Getpid(), time.Now().UTC().UnixNano()),
		Operation:    "package-removal",
		PID:          os.Getpid(),
		StartedUTC:   time.Now().UTC().Format(time.RFC3339Nano),
	}
	data, err := json.Marshal(metadata)
	if err != nil {
		_ = lease.release()
		return nil, err
	}
	if err := writeLeaseMetadata(metadataPath, data); err != nil {
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

func ensurePackageMutationReady(projectRoot string) error {
	if running, pid, err := checkUnityEditorRunning(projectRoot); err != nil {
		return fmt.Errorf("Unity activity cannot be proven idle: %w", err)
	} else if running {
		return fmt.Errorf("Unity Editor is active for this project (PID %d)", pid)
	}
	return ensureNoBuildRecoveryPending(projectRoot)
}

func checkUnityEditorRunning(projectRoot string) (bool, int, error) {
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
	var instance unityEditorInstance
	if err := json.Unmarshal(data, &instance); err != nil || instance.ProcessID <= 0 {
		return false, 0, errors.New("EditorInstance.json is malformed")
	}
	running, err := packageToolProcessIsRunning(instance.ProcessID)
	return running, instance.ProcessID, err
}

// processProbeTimeout bounds the external process-liveness probe so a wedged
// tasklist can never hang the whole tool.
const processProbeTimeout = 10 * time.Second

func packageToolProcessIsRunning(pid int) (bool, error) {
	if runtime.GOOS == "windows" {
		ctx, cancel := context.WithTimeout(context.Background(), processProbeTimeout)
		defer cancel()
		command := exec.CommandContext(ctx, "tasklist", "/FI", fmt.Sprintf("PID eq %d", pid), "/NH", "/FO", "CSV")
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

func ensureNoBuildRecoveryPending(projectRoot string) error {
	root := filepath.Join(projectRoot, filepath.FromSlash(buildTransactionRelativePath))
	info, err := os.Lstat(root)
	if errors.Is(err, os.ErrNotExist) {
		return nil
	}
	if err != nil || !info.IsDir() {
		return fmt.Errorf("Build transaction root is unreadable or not a directory: %s", root)
	}
	if err := ensureWorkspaceSegmentsNotRedirected(projectRoot, root); err != nil {
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
			return fmt.Errorf("Build transaction evidence is redirected or unreadable: %s", path)
		}
		relative, _ := filepath.Rel(root, path)
		depth := len(strings.Split(filepath.ToSlash(relative), "/"))
		if entry.IsDir() {
			if depth == 1 {
				return nil
			}
			return fmt.Errorf("durable Build transaction directory requires explicit recovery: %s", path)
		}
		if depth == 2 && (entry.Name() == "build.lock" || entry.Name() == "active.lock") {
			info, err := entry.Info()
			if err != nil || info.Size() > 4096 {
				return fmt.Errorf("reusable Build lock metadata is invalid: %s", path)
			}
			return nil
		}
		return fmt.Errorf("durable Build transaction evidence requires explicit recovery: %s", path)
	})
}

func writeLeaseMetadata(target string, data []byte) error {
	file, err := os.CreateTemp(filepath.Dir(target), ".lease-metadata-*")
	if err != nil {
		return err
	}
	stage := file.Name()
	if err := file.Chmod(0600); err != nil {
		_ = file.Close()
		return fmt.Errorf("lease metadata stage retained at %s: %w", stage, err)
	}
	if _, err := file.Write(data); err != nil {
		_ = file.Close()
		return fmt.Errorf("lease metadata stage retained at %s: %w", stage, err)
	}
	if err := file.Sync(); err != nil {
		_ = file.Close()
		return fmt.Errorf("lease metadata stage retained at %s: %w", stage, err)
	}
	if err := file.Close(); err != nil {
		return fmt.Errorf("lease metadata stage retained at %s: %w", stage, err)
	}
	readBack, err := os.ReadFile(stage)
	if err != nil || !bytes.Equal(readBack, data) {
		return fmt.Errorf("lease metadata read-back mismatch; stage retained at %s", stage)
	}
	if err := replacePathAtomically(stage, target); err != nil {
		return fmt.Errorf("lease metadata publish failed; stage may be retained at %s: %w", stage, err)
	}
	return nil
}

func ensureWorkspaceSegmentsNotRedirected(projectRoot, target string) error {
	if !pathIsDescendant(projectRoot, target) && !samePath(projectRoot, target) {
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
