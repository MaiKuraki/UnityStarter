package remove_unity_packages

import (
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"strings"

	"cyclonegames.tools/scripts/internal/logging"
	"cyclonegames.tools/scripts/internal/projectlayout"
	"cyclonegames.tools/scripts/internal/recovery"
	"cyclonegames.tools/scripts/internal/safefs"
	"cyclonegames.tools/scripts/internal/toolenv"
)

type buildWorkspaceLease struct {
	file     *os.File
	path     string
	identity os.FileInfo
}

type unityEditorInstance struct {
	ProcessID int `json:"process_id"`
}

// acquireBuildWorkspaceLease claims the durable workspace lock through an OS
// byte-range lease. It writes no metadata: other tools decide whether the
// workspace is busy by probing this lock, so finishing a run leaves no shared
// state that could be misread as a live holder.
func acquireBuildWorkspaceLease(projectRoot string, layout projectlayout.Layout) (*buildWorkspaceLease, error) {
	leasePath, err := layout.Join(projectRoot, layout.LeaseFile)
	if err != nil {
		return nil, fmt.Errorf("cannot resolve the Build workspace lease path: %w", err)
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
	return &buildWorkspaceLease{file: file, path: leasePath, identity: fileInfo}, nil
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

// ensurePackageMutationReady reconciles durable build records before a
// destructive package mutation. Only an in-progress record or a lock held by a
// live process blocks; completed journals, scratch folders and idle locks are
// normal audit evidence. With strict enabled, unclassified evidence also blocks.
func ensurePackageMutationReady(projectRoot string, layout projectlayout.Layout, strict bool) error {
	if running, pid, err := checkUnityEditorRunning(projectRoot, layout); err != nil {
		return fmt.Errorf("Unity activity cannot be proven idle: %w", err)
	} else if running {
		return fmt.Errorf("Unity Editor is active for this project (PID %d)", pid)
	}
	transactionsDir, err := layout.Join(projectRoot, layout.TransactionsDir)
	if err != nil {
		return fmt.Errorf("cannot resolve the build transaction directory: %w", err)
	}
	report, err := recovery.EnsureReady(projectRoot, transactionsDir, strict)
	if err != nil {
		return fmt.Errorf("%w. Wait for any live build process to finish, then review and remove the listed records from the configured transaction directory; without --strict-recovery, unclassified evidence is advisory", err)
	}
	if len(report.Unknown) > 0 {
		logging.Warnf("Unclassified build transaction evidence is present but not blocking: %s", strings.Join(report.Unknown, ", "))
	}
	return nil
}

func checkUnityEditorRunning(projectRoot string, layout projectlayout.Layout) (bool, int, error) {
	path, err := layout.Join(projectRoot, layout.EditorInstanceFile)
	if err != nil {
		return false, 0, err
	}
	data, err := readBoundedFile(path)
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

func packageToolProcessIsRunning(pid int) (bool, error) {
	return toolenv.ProcessAlive(pid)
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
