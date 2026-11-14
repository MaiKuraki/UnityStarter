// Package projectlayout defines the file-structure contract a tool uses when it
// operates on a Unity project. Every entry is a portable relative path; no
// project name, drive letter or machine path is part of the contract.
//
// A project may override any entry by placing a layout document at
// Tools/project-layout.json, or a command may pass --layout <path>. Overrides
// are merged onto the defaults, so a project only declares what differs.
package projectlayout

import (
	"bytes"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"sort"
	"strings"

	"cyclonegames.tools/scripts/internal/safefs"
)

// LayoutFile is the optional per-project override, relative to the project root.
const LayoutFile = "Tools/project-layout.json"

const maximumLayoutBytes = 256 * 1024

// Layout is the complete set of structural paths a tool may need.
type Layout struct {
	Markers            []string `json:"markers,omitempty"`
	ProjectVersionFile string   `json:"projectVersionFile,omitempty"`
	ManifestFile       string   `json:"manifestFile,omitempty"`
	PackagesLockFile   string   `json:"packagesLockFile,omitempty"`
	EditorInstanceFile string   `json:"editorInstanceFile,omitempty"`

	CacheRoots       []string `json:"cacheRoots,omitempty"`
	PublicationRoots []string `json:"publicationRoots,omitempty"`
	DurableRoots     []string `json:"durableRoots,omitempty"`
	TempRoot         string   `json:"tempRoot,omitempty"`

	TransactionsDir   string `json:"transactionsDir,omitempty"`
	WorkspaceDir      string `json:"workspaceDir,omitempty"`
	QuarantineDir     string `json:"quarantineDir,omitempty"`
	LeaseFile         string `json:"leaseFile,omitempty"`
	LeaseMetadataFile string `json:"leaseMetadataFile,omitempty"`

	OwnerSuffix        string   `json:"ownerSuffix,omitempty"`
	OwnerMarkerNames   []string `json:"ownerMarkerNames,omitempty"`
	IdentityRegistry   string   `json:"identityRegistry,omitempty"`
	RenameStateFile    string   `json:"renameStateFile,omitempty"`
	RenameBackupDir    string   `json:"renameBackupDir,omitempty"`
	RenameLockDir      string   `json:"renameLockDir,omitempty"`
	PackageEvidenceDir string   `json:"packageEvidenceDir,omitempty"`
}

// Default returns the Unity structure contract: the standard project markers,
// regenerable caches, publication roots and the durable build workspace.
func Default() Layout {
	return Layout{
		Markers:            []string{"Assets", "ProjectSettings"},
		ProjectVersionFile: "ProjectSettings/ProjectVersion.txt",
		ManifestFile:       "Packages/manifest.json",
		PackagesLockFile:   "Packages/packages-lock.json",
		EditorInstanceFile: "Library/EditorInstance.json",

		CacheRoots:       []string{".vs", ".utmp", "obj", "Logs", "Library"},
		PublicationRoots: []string{"Build", "Bundles", "HybridCLRData", "yoo", "HotUpdateAssetsPreUpload"},
		DurableRoots:     []string{".buildpipeline", "Temp/BuildPipeline/Workspace"},
		TempRoot:         "Temp",

		TransactionsDir:   ".buildpipeline/transactions",
		WorkspaceDir:      "Temp/BuildPipeline/Workspace",
		QuarantineDir:     "Temp/BuildPipeline/Workspace/cleanup-quarantine",
		LeaseFile:         "Temp/BuildPipeline/Workspace/lease.lock",
		LeaseMetadataFile: "Temp/BuildPipeline/Workspace/lease.json",

		OwnerSuffix:        ".buildpipeline-player-owner.json",
		OwnerMarkerNames:   []string{".buildpipeline-owner.json", ".yoo-pub.json"},
		IdentityRegistry:   "ProjectSettings/TemplateIdentityRegistry.json",
		RenameStateFile:    ".rename_project.json",
		RenameBackupDir:    ".rename_backup",
		RenameLockDir:      ".rename_project.lock",
		PackageEvidenceDir: "Packages",
	}
}

// Load resolves the layout for a project. An explicit path is authoritative;
// otherwise the project-level override is used when present. Missing override
// files are not an error: the defaults describe a standard Unity project.
func Load(projectRoot, explicit string) (Layout, error) {
	base := Default()
	path := strings.TrimSpace(explicit)
	if path == "" {
		candidate := filepath.Join(projectRoot, filepath.FromSlash(LayoutFile))
		if info, err := os.Lstat(candidate); err == nil && info.Mode().IsRegular() {
			path = candidate
		}
	}
	if path == "" {
		return base, nil
	}
	info, err := os.Lstat(path)
	if err != nil {
		return Layout{}, fmt.Errorf("cannot read layout %q: %w", path, err)
	}
	if !info.Mode().IsRegular() || info.Mode()&os.ModeSymlink != 0 || info.Size() > maximumLayoutBytes {
		return Layout{}, fmt.Errorf("layout %q is not a bounded regular file", path)
	}
	data, err := os.ReadFile(path)
	if err != nil {
		return Layout{}, fmt.Errorf("cannot read layout %q: %w", path, err)
	}
	merged, err := merge(base, data)
	if err != nil {
		return Layout{}, fmt.Errorf("invalid layout %q: %w", path, err)
	}
	if err := merged.Validate(); err != nil {
		return Layout{}, fmt.Errorf("invalid layout %q: %w", path, err)
	}
	return merged, nil
}

// merge overlays declared fields onto the defaults while rejecting unknown
// members, duplicate members and trailing data.
func merge(base Layout, data []byte) (Layout, error) {
	var shape Layout
	decoder := json.NewDecoder(bytes.NewReader(data))
	decoder.DisallowUnknownFields()
	if err := decoder.Decode(&shape); err != nil {
		return Layout{}, err
	}
	if err := expectEOF(decoder); err != nil {
		return Layout{}, err
	}

	defaults, err := json.Marshal(base)
	if err != nil {
		return Layout{}, err
	}
	var target map[string]json.RawMessage
	if err := json.Unmarshal(defaults, &target); err != nil {
		return Layout{}, err
	}
	var overrides map[string]json.RawMessage
	if err := json.Unmarshal(data, &overrides); err != nil {
		return Layout{}, err
	}
	for key, value := range overrides {
		target[key] = value
	}
	combined, err := json.Marshal(target)
	if err != nil {
		return Layout{}, err
	}
	var result Layout
	if err := json.Unmarshal(combined, &result); err != nil {
		return Layout{}, err
	}
	return result, nil
}

func expectEOF(decoder *json.Decoder) error {
	var extra interface{}
	if err := decoder.Decode(&extra); err != io.EOF {
		if err != nil {
			return err
		}
		return errors.New("unexpected data after layout document")
	}
	return nil
}

// Validate enforces the portability contract: every path is relative, has no
// parent traversal, uses bounded components and keeps mutable state inside a
// durable root.
func (l Layout) Validate() error {
	if len(l.Markers) == 0 {
		return errors.New("layout declares no project markers")
	}
	for _, marker := range l.Markers {
		if err := validateRelative(marker); err != nil {
			return fmt.Errorf("marker %q: %w", marker, err)
		}
	}
	fields := map[string]string{
		"projectVersionFile": l.ProjectVersionFile,
		"manifestFile":       l.ManifestFile,
		"packagesLockFile":   l.PackagesLockFile,
		"editorInstanceFile": l.EditorInstanceFile,
		"transactionsDir":    l.TransactionsDir,
		"tempRoot":           l.TempRoot,
		"workspaceDir":       l.WorkspaceDir,
		"quarantineDir":      l.QuarantineDir,
		"leaseFile":          l.LeaseFile,
		"leaseMetadataFile":  l.LeaseMetadataFile,
		"ownerSuffix":        l.OwnerSuffix,
		"identityRegistry":   l.IdentityRegistry,
		"renameStateFile":    l.RenameStateFile,
		"renameBackupDir":    l.RenameBackupDir,
		"renameLockDir":      l.RenameLockDir,
		"packageEvidenceDir": l.PackageEvidenceDir,
	}
	keys := make([]string, 0, len(fields))
	for key := range fields {
		keys = append(keys, key)
	}
	sort.Strings(keys)
	for _, key := range keys {
		if err := validateRelative(fields[key]); err != nil {
			return fmt.Errorf("%s %q: %w", key, fields[key], err)
		}
	}
	for _, group := range map[string][]string{
		"cacheRoots":       l.CacheRoots,
		"publicationRoots": l.PublicationRoots,
		"durableRoots":     l.DurableRoots,
		"ownerMarkerNames": l.OwnerMarkerNames,
	} {
		for _, entry := range group {
			if err := validateRelative(entry); err != nil {
				return fmt.Errorf("layout entry %q: %w", entry, err)
			}
		}
	}
	if err := requireChild("workspaceDir", l.WorkspaceDir, "quarantineDir", l.QuarantineDir); err != nil {
		return err
	}
	if err := requireChild("workspaceDir", l.WorkspaceDir, "leaseFile", l.LeaseFile); err != nil {
		return err
	}
	if err := requireChild("workspaceDir", l.WorkspaceDir, "leaseMetadataFile", l.LeaseMetadataFile); err != nil {
		return err
	}
	protected := false
	for _, root := range l.DurableRoots {
		if isWithinSlash(root, l.TransactionsDir) {
			protected = true
			break
		}
	}
	if !protected {
		return errors.New("transactionsDir must live under a durableRoots entry")
	}
	for _, cache := range l.CacheRoots {
		for _, durable := range l.DurableRoots {
			if equalSlash(cache, durable) || isWithinSlash(cache, durable) {
				return fmt.Errorf("cacheRoot %q overlaps durableRoot %q", cache, durable)
			}
		}
	}
	return nil
}

func validateRelative(value string) error {
	if strings.TrimSpace(value) == "" {
		return errors.New("empty path")
	}
	if filepath.IsAbs(value) || strings.HasPrefix(value, "/") || strings.HasPrefix(value, `\`) || filepath.VolumeName(value) != "" {
		return errors.New("path must be relative")
	}
	cleaned := filepath.ToSlash(filepath.Clean(filepath.FromSlash(value)))
	if cleaned == "." || cleaned == ".." || strings.HasPrefix(cleaned, "../") || strings.Contains(cleaned, "/../") {
		return errors.New("path must stay inside the project root")
	}
	policy := safefs.HostPathPolicy()
	for _, component := range strings.Split(cleaned, "/") {
		// Layout documents are portable: a colon would be a drive letter or an
		// alternate data stream on Windows, and a backslash a separator there.
		if strings.ContainsAny(component, `:\`) {
			return fmt.Errorf("path component %q is not portable", component)
		}
		if err := policy.ValidateComponent(component); err != nil {
			return err
		}
	}
	return nil
}

func requireChild(parentName, parent, childName, child string) error {
	if !isWithinSlash(parent, child) {
		return fmt.Errorf("%s %q must live under %s %q", childName, child, parentName, parent)
	}
	return nil
}

func equalSlash(a, b string) bool {
	return strings.EqualFold(filepath.ToSlash(filepath.Clean(filepath.FromSlash(a))),
		filepath.ToSlash(filepath.Clean(filepath.FromSlash(b))))
}

func isWithinSlash(parent, child string) bool {
	parent = filepath.ToSlash(filepath.Clean(filepath.FromSlash(parent)))
	child = filepath.ToSlash(filepath.Clean(filepath.FromSlash(child)))
	return child != parent && strings.HasPrefix(child, parent+"/")
}

// Join resolves a layout path against the project root and proves containment.
func (l Layout) Join(projectRoot, relative string) (string, error) {
	if err := validateRelative(relative); err != nil {
		return "", err
	}
	target := filepath.Join(projectRoot, filepath.FromSlash(relative))
	rel, err := filepath.Rel(projectRoot, target)
	if err != nil || rel == "." || rel == ".." || strings.HasPrefix(rel, ".."+string(os.PathSeparator)) {
		return "", fmt.Errorf("layout path escapes the project root: %s", relative)
	}
	return target, nil
}

// JoinAll resolves several layout paths in one call.
func (l Layout) JoinAll(projectRoot string, relatives ...string) ([]string, error) {
	result := make([]string, 0, len(relatives))
	for _, relative := range relatives {
		target, err := l.Join(projectRoot, relative)
		if err != nil {
			return nil, err
		}
		result = append(result, target)
	}
	return result, nil
}

// MatchesRoot reports whether a directory exposes every declared marker.
func (l Layout) MatchesRoot(projectRoot string) bool {
	for _, marker := range l.Markers {
		path, err := l.Join(projectRoot, marker)
		if err != nil {
			return false
		}
		info, err := os.Stat(path)
		if err != nil || !info.IsDir() {
			return false
		}
	}
	return true
}
