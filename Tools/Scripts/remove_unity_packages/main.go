// Package remove_unity_packages removes explicitly authorized dependencies from a Unity
// Packages/manifest.json. It fails closed on ambiguous policy, source references,
// lock-file drift, backup failures, malformed JSON, or non-durable writes.
//
// The command is dispatched in-process by cmd/unitystarter_tools.
package remove_unity_packages

import (
	"bytes"
	"crypto/rand"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"io"
	"io/fs"
	"os"
	"path/filepath"
	"sort"
	"strings"
	"time"

	"cyclonegames.tools/scripts/internal/safefs"
)

const (
	removalProfileDocumentType = "unity-package-removal-policy"
	maximumJSONBytes           = 64 * 1024 * 1024
	maximumEvidenceFiles       = 250000
)

type stringListFlag []string

func (values *stringListFlag) String() string { return strings.Join(*values, ",") }
func (values *stringListFlag) Set(value string) error {
	value = strings.TrimSpace(value)
	if value == "" {
		return errors.New("package ID must not be empty")
	}
	*values = append(*values, value)
	return nil
}

type removalProfile struct {
	DocumentType              string   `json:"documentType"`
	Name                      string   `json:"name"`
	Packages                  []string `json:"packages"`
	AllowReferencedPackages   []string `json:"allowReferencedPackages,omitempty"`
	AllowLockfileRegeneration bool     `json:"allowLockfileRegeneration"`
}

type jsonMember struct {
	Name  string
	Value json.RawMessage
}

type dependency struct {
	Name    string
	Version string
}

type manifestDocument struct {
	Members      []jsonMember
	Dependencies []dependency
}

type lockNode struct {
	Depth        int
	Dependencies map[string]string
}

type sourceSignature struct {
	PackageID string
	Needles   []string
}

type packageRemovalEntry struct {
	CanonicalPath string `json:"canonicalPath"`
	ClaimedPath   string `json:"claimedPath"`
	State         string `json:"state"`
}

type packageRemovalJournal struct {
	DocumentType  string                `json:"documentType"`
	TransactionID string                `json:"transactionId"`
	State         string                `json:"state"`
	StartedUTC    string                `json:"startedUtc"`
	Entries       []packageRemovalEntry `json:"entries"`
}

type claimedPackageFile struct {
	canonicalPath    string
	claimedPath      string
	expected         []byte
	expectedIdentity os.FileInfo
	claimedIdentity  os.FileInfo
	permission       fs.FileMode
}

var sourceSignatures = []sourceSignature{
	{PackageID: "com.unity.ai.navigation", Needles: []string{"Unity.AI.Navigation", "NavMeshSurface", "NavMeshLink", "NavMeshModifier"}},
	{PackageID: "com.unity.test-framework", Needles: []string{"TestAssemblies", "UnityEngine.TestTools", "Unity.PerformanceTesting", "NUnit.Framework"}},
	{PackageID: "com.unity.timeline", Needles: []string{"Unity.Timeline", "UnityEngine.Timeline", "PlayableDirector"}},
	{PackageID: "com.unity.visualscripting", Needles: []string{"Unity.VisualScripting"}},
	{PackageID: "com.unity.modules.physics", Needles: []string{"UnityEngine.Physics", "Rigidbody", "Collider", "CharacterController"}},
	{PackageID: "com.unity.modules.physics2d", Needles: []string{"UnityEngine.Physics2D", "Rigidbody2D", "Collider2D"}},
	{PackageID: "com.unity.modules.tilemap", Needles: []string{"UnityEngine.Tilemaps", "Tilemap"}},
	{PackageID: "com.unity.2d.tilemap", Needles: []string{"Unity.2D.Tilemap", "UnityEngine.Tilemaps", "Tilemap"}},
	{PackageID: "com.unity.modules.video", Needles: []string{"UnityEngine.Video", "VideoPlayer"}},
	{PackageID: "com.unity.modules.terrain", Needles: []string{"UnityEngine.Terrain", "TerrainData"}},
	{PackageID: "com.unity.modules.terrainphysics", Needles: []string{"TerrainCollider"}},
	{PackageID: "com.unity.modules.vehicles", Needles: []string{"WheelCollider"}},
	{PackageID: "com.unity.modules.cloth", Needles: []string{"UnityEngine.Cloth"}},
	{PackageID: "com.unity.modules.xr", Needles: []string{"UnityEngine.XR", "XRGeneralSettings"}},
	{PackageID: "com.unity.modules.vr", Needles: []string{"UnityEngine.VR"}},
}

// Run executes the removal tool and returns its process exit code.
func Run(arguments []string) int {
	return run(arguments, os.Stdout, os.Stderr)
}

func run(arguments []string, stdout, stderr io.Writer) int {
	flags := flag.NewFlagSet("remove_unity_packages", flag.ContinueOnError)
	flags.SetOutput(stderr)
	var allowed stringListFlag
	var allowReferenced stringListFlag
	var profilePath string
	var dryRun bool
	var apply bool
	var allowLockRegeneration bool
	var listMode bool
	flags.Var(&allowed, "allow-package", "Exact package ID authorized for removal; repeatable")
	flags.Var(&allowReferenced, "allow-referenced-package", "Explicitly override detected source evidence; repeatable")
	flags.StringVar(&profilePath, "profile", "", "Strict current-contract JSON removal policy")
	flags.BoolVar(&dryRun, "dry-run", false, "Validate and preview without writing")
	flags.BoolVar(&apply, "apply", false, "Commit the reviewed removal transaction")
	flags.BoolVar(&allowLockRegeneration, "allow-lock-regeneration", false, "Back up then remove packages-lock.json so Unity must resolve again")
	flags.BoolVar(&listMode, "list", false, "List packages with built-in source-reference signatures")
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
	if dryRun && apply {
		fmt.Fprintln(stderr, "[ERROR] -dry-run and -apply are mutually exclusive.")
		return 2
	}
	if listMode {
		ids := make([]string, 0, len(sourceSignatures))
		for _, signature := range sourceSignatures {
			ids = append(ids, signature.PackageID)
		}
		sort.Strings(ids)
		fmt.Fprintln(stdout, "Packages with built-in source-reference signatures (this is not an authorization list):")
		for _, packageID := range ids {
			fmt.Fprintf(stdout, "  %s\n", packageID)
		}
		return 0
	}

	projectRoot, err := os.Getwd()
	if err != nil {
		fmt.Fprintf(stderr, "[ERROR] Cannot resolve current directory: %v\n", err)
		return 1
	}
	projectRoot, err = validateUnityProjectRoot(projectRoot)
	if err != nil {
		fmt.Fprintf(stderr, "[ERROR] %v\n", err)
		return 1
	}
	var workspaceLease *buildWorkspaceLease
	if apply {
		workspaceLease, err = acquireBuildWorkspaceLease(projectRoot)
		if err != nil {
			fmt.Fprintf(stderr, "[ERROR] Build workspace is busy or unsafe; package mutation refused: %v\n", err)
			return 1
		}
		defer func() {
			if err := workspaceLease.release(); err != nil {
				fmt.Fprintf(stderr, "[WARNING] Failed to release Build workspace lease cleanly: %v\n", err)
			}
		}()
		if err := ensurePackageMutationReady(projectRoot); err != nil {
			fmt.Fprintf(stderr, "[ERROR] Package mutation safety check failed: %v\n", err)
			return 1
		}
	}

	policyPackages := append([]string(nil), allowed...)
	policyReferenced := append([]string(nil), allowReferenced...)
	if profilePath != "" {
		profile, profileErr := readRemovalProfile(profilePath)
		if profileErr != nil {
			fmt.Fprintf(stderr, "[ERROR] Removal profile rejected: %v\n", profileErr)
			return 1
		}
		policyPackages = append(policyPackages, profile.Packages...)
		policyReferenced = append(policyReferenced, profile.AllowReferencedPackages...)
		allowLockRegeneration = allowLockRegeneration || profile.AllowLockfileRegeneration
		fmt.Fprintf(stdout, "Policy profile: %s\n", profile.Name)
	}

	removeSet, err := validatedPackageSet(policyPackages, "authorized package")
	if err != nil || len(removeSet) == 0 {
		if err == nil {
			err = errors.New("no packages were authorized; use -allow-package or -profile")
		}
		fmt.Fprintf(stderr, "[ERROR] %v\n", err)
		return 1
	}
	overrideSet, err := validatedPackageSet(policyReferenced, "reference override")
	if err != nil {
		fmt.Fprintf(stderr, "[ERROR] %v\n", err)
		return 1
	}
	for packageID := range overrideSet {
		if !removeSet[packageID] {
			fmt.Fprintf(stderr, "[ERROR] Reference override '%s' is not an authorized removal.\n", packageID)
			return 1
		}
	}

	manifestPath := filepath.Join(projectRoot, "Packages", "manifest.json")
	if err := ensureProjectFileNotRedirected(projectRoot, manifestPath); err != nil {
		fmt.Fprintf(stderr, "[ERROR] Manifest path rejected: %v\n", err)
		return 1
	}
	manifestBytes, err := readBoundedFile(manifestPath)
	if err != nil {
		fmt.Fprintf(stderr, "[ERROR] Cannot read manifest: %v\n", err)
		return 1
	}
	document, err := parseManifest(manifestBytes)
	if err != nil {
		fmt.Fprintf(stderr, "[ERROR] Manifest rejected: %v\n", err)
		return 1
	}

	var removals []string
	for _, item := range document.Dependencies {
		if removeSet[item.Name] {
			removals = append(removals, item.Name)
		}
	}
	for packageID := range removeSet {
		if !contains(removals, packageID) {
			fmt.Fprintf(stderr, "[ERROR] Authorized package is absent from manifest: %s\n", packageID)
			return 1
		}
	}
	sort.Strings(removals)

	lockPath := filepath.Join(projectRoot, "Packages", "packages-lock.json")
	if err := ensureNoPackageTransactionEvidence(filepath.Dir(lockPath), lockPath); err != nil {
		fmt.Fprintf(stderr, "[ERROR] Incomplete prior package-removal transaction requires manual recovery before another mutation: %v\n", err)
		return 1
	}
	if err := ensureNoStalePackageStages(filepath.Dir(lockPath)); err != nil {
		fmt.Fprintf(stderr, "[ERROR] Incomplete prior package stage requires manual recovery before another mutation: %v\n", err)
		return 1
	}
	lockBytes, lockExists, err := readOptionalBoundedFile(lockPath)
	if err != nil {
		fmt.Fprintf(stderr, "[ERROR] Cannot inspect packages-lock.json: %v\n", err)
		return 1
	}
	if lockExists {
		if err := ensureProjectFileNotRedirected(projectRoot, lockPath); err != nil {
			fmt.Fprintf(stderr, "[ERROR] Lock path rejected: %v\n", err)
			return 1
		}
	}
	if lockExists {
		if !allowLockRegeneration {
			fmt.Fprintln(stderr, "[ERROR] packages-lock.json exists. Refusing to create manifest/lock drift. Review the change and pass -allow-lock-regeneration or authorize it in the profile.")
			return 1
		}
		if err := validateLockGraph(lockBytes, removeSet); err != nil {
			fmt.Fprintf(stderr, "[ERROR] Lock graph rejected the removal: %v\n", err)
			return 1
		}
	}

	evidence, err := validateSourceEvidencePolicy(projectRoot, removeSet, overrideSet)
	if err != nil {
		fmt.Fprintf(stderr, "[ERROR] %v\n", err)
		return 1
	}

	updatedManifest, err := document.without(removeSet)
	if err != nil {
		fmt.Fprintf(stderr, "[ERROR] Cannot construct updated manifest: %v\n", err)
		return 1
	}
	if _, err := parseManifest(updatedManifest); err != nil {
		fmt.Fprintf(stderr, "[ERROR] Updated manifest failed read-back validation: %v\n", err)
		return 1
	}

	fmt.Fprintf(stdout, "Project: %s\n", projectRoot)
	fmt.Fprintf(stdout, "Authorized removals (%d): %s\n", len(removals), strings.Join(removals, ", "))
	if len(evidence) != 0 {
		fmt.Fprintln(stdout, "Detected source references were explicitly overridden by policy.")
	}
	if lockExists {
		fmt.Fprintln(stdout, "Lock strategy: mandatory backup followed by removal; Unity must perform a fresh package resolution.")
	} else {
		fmt.Fprintln(stdout, "Lock strategy: no packages-lock.json was present.")
	}
	if dryRun || !apply {
		fmt.Fprintln(stdout, "[Preview] Validation passed; no files were modified. Pass -apply to commit this exact policy.")
		return 0
	}
	if err := ensurePackageMutationReady(projectRoot); err != nil {
		fmt.Fprintf(stderr, "[ERROR] Project activity changed before package commit; no package target was modified: %v\n", err)
		return 1
	}
	if err := workspaceLease.validate(); err != nil {
		fmt.Fprintf(stderr, "[ERROR] Build workspace lease identity changed before package commit; no package target was modified: %v\n", err)
		return 1
	}
	if _, err := validateSourceEvidencePolicy(projectRoot, removeSet, overrideSet); err != nil {
		fmt.Fprintf(stderr, "[ERROR] Source dependency evidence changed before package commit; no package target was modified: %v\n", err)
		return 1
	}
	if err := ensureRemovalPreimageUnchanged(projectRoot, manifestPath, manifestBytes, lockPath, lockBytes, lockExists); err != nil {
		fmt.Fprintf(stderr, "[ERROR] Package files changed after validation; no package target was modified: %v\n", err)
		return 1
	}

	validateMutationEvidence := func() error {
		if err := ensurePackageMutationReady(projectRoot); err != nil {
			return fmt.Errorf("project activity changed: %w", err)
		}
		if err := workspaceLease.validate(); err != nil {
			return fmt.Errorf("Build workspace lease identity changed: %w", err)
		}
		if _, err := validateSourceEvidencePolicy(projectRoot, removeSet, overrideSet); err != nil {
			return fmt.Errorf("source dependency evidence changed: %w", err)
		}
		return nil
	}
	backupPaths, err := commitManifestTransaction(projectRoot, manifestPath, manifestBytes, updatedManifest, lockPath, lockBytes, lockExists, validateMutationEvidence)
	if err != nil {
		if len(backupPaths) != 0 {
			fmt.Fprintf(stderr, "[ERROR] Package transaction did not reach a verified terminal state: %v. Recovery backups: %s\n", err, strings.Join(backupPaths, ", "))
		} else {
			fmt.Fprintf(stderr, "[ERROR] Package transaction was rejected before target mutation: %v\n", err)
		}
		return 1
	}
	fmt.Fprintf(stdout, "[OK] Updated manifest atomically. Mandatory backups: %s\n", strings.Join(backupPaths, ", "))
	return 0
}

func validateUnityProjectRoot(path string) (string, error) {
	root, err := filepath.Abs(path)
	if err != nil {
		return "", err
	}
	root = filepath.Clean(root)
	volume := filepath.Clean(filepath.VolumeName(root) + string(os.PathSeparator))
	if samePath(root, volume) {
		return "", errors.New("filesystem roots cannot be Unity project roots")
	}
	rootInfo, err := os.Lstat(root)
	if err != nil || !rootInfo.IsDir() || rootInfo.Mode()&os.ModeSymlink != 0 {
		return "", errors.New("project root is unavailable, redirected, or not a directory")
	}
	if redirected, inspectErr := pathIsReparsePoint(root); inspectErr != nil || redirected {
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
		if err := ensureProjectFileNotRedirected(root, markerPath); err != nil {
			return "", fmt.Errorf("Unity project marker '%s' is redirected: %w", relative, err)
		}
		info, err := os.Lstat(markerPath)
		if err != nil || !info.IsDir() || info.Mode()&os.ModeSymlink != 0 {
			return "", fmt.Errorf("Unity project marker '%s' is unavailable, redirected, or not a directory", relative)
		}
	}
	projectVersionPath := filepath.Join(root, "ProjectSettings", "ProjectVersion.txt")
	projectVersion, _, err := readBoundedStableProjectFile(root, projectVersionPath, 4*1024)
	if err != nil {
		return "", fmt.Errorf("ProjectSettings/ProjectVersion.txt is invalid: %w", err)
	}
	if !bytes.Contains(projectVersion, []byte("m_EditorVersion:")) {
		return "", errors.New("ProjectSettings/ProjectVersion.txt does not contain m_EditorVersion")
	}
	manifestPath := filepath.Join(root, "Packages", "manifest.json")
	manifest, _, err := readBoundedStableProjectFile(root, manifestPath, maximumJSONBytes)
	if err != nil {
		return "", fmt.Errorf("Packages/manifest.json is invalid: %w", err)
	}
	if _, err := parseManifest(manifest); err != nil {
		return "", fmt.Errorf("Packages/manifest.json is not a structured Unity manifest: %w", err)
	}
	return root, nil
}

func ensureProjectFileNotRedirected(projectRoot, path string) error {
	projectRoot = filepath.Clean(projectRoot)
	path = filepath.Clean(path)
	if !samePath(projectRoot, path) && !pathIsDescendant(projectRoot, path) {
		return fmt.Errorf("path resolves outside the trusted project root: %s", path)
	}
	current := projectRoot
	relative, err := filepath.Rel(projectRoot, path)
	if err != nil {
		return err
	}
	segments := append([]string{"."}, strings.Split(filepath.Clean(relative), string(os.PathSeparator))...)
	for _, segment := range segments {
		if segment != "." && segment != "" {
			current = filepath.Join(current, segment)
		}
		info, err := os.Lstat(current)
		if err != nil {
			return err
		}
		if info.Mode()&os.ModeSymlink != 0 {
			return fmt.Errorf("path contains a symbolic link or reparse point: %s", current)
		}
		if redirected, inspectErr := pathIsReparsePoint(current); inspectErr != nil {
			return inspectErr
		} else if redirected {
			return fmt.Errorf("path contains a symbolic link or reparse point: %s", current)
		}
		if err := safefs.ValidateMountBoundary(projectRoot, current); err != nil {
			return err
		}
	}
	rootReal, err := filepath.EvalSymlinks(projectRoot)
	if err != nil {
		return err
	}
	pathReal, err := filepath.EvalSymlinks(path)
	if err != nil {
		return err
	}
	rootReal, err = filepath.Abs(rootReal)
	if err != nil {
		return err
	}
	pathReal, err = filepath.Abs(pathReal)
	if err != nil {
		return err
	}
	if !samePath(rootReal, pathReal) && !pathIsDescendant(rootReal, pathReal) {
		return fmt.Errorf("path resolves outside the trusted project root: %s", path)
	}
	return nil
}

func readBoundedStableProjectFile(projectRoot, path string, maximumBytes int64) ([]byte, os.FileInfo, error) {
	if err := ensureProjectFileNotRedirected(projectRoot, path); err != nil {
		return nil, nil, err
	}
	before, err := os.Lstat(path)
	if err != nil || !before.Mode().IsRegular() || before.Mode()&os.ModeSymlink != 0 || before.Size() < 2 || before.Size() > maximumBytes {
		return nil, nil, fmt.Errorf("file is unavailable or outside its regular-file byte budget: %s", path)
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
	if err := ensureProjectFileNotRedirected(projectRoot, path); err != nil {
		return nil, nil, fmt.Errorf("file path became redirected while reading: %s: %w", path, err)
	}
	return data, after, nil
}

func readRemovalProfile(path string) (removalProfile, error) {
	data, err := readBoundedFile(path)
	if err != nil {
		return removalProfile{}, err
	}
	if _, err := decodeObject(data); err != nil {
		return removalProfile{}, fmt.Errorf("removal profile must be an unambiguous JSON object: %w", err)
	}
	decoder := json.NewDecoder(bytes.NewReader(data))
	decoder.DisallowUnknownFields()
	var profile removalProfile
	if err := decoder.Decode(&profile); err != nil {
		return profile, err
	}
	if err := ensureJSONEOF(decoder); err != nil {
		return profile, err
	}
	if profile.DocumentType != removalProfileDocumentType {
		return profile, fmt.Errorf("unsupported or missing documentType %q", profile.DocumentType)
	}
	if strings.TrimSpace(profile.Name) == "" {
		return profile, errors.New("profile name is required")
	}
	if _, err := validatedPackageSet(profile.Packages, "profile package"); err != nil {
		return profile, err
	}
	if _, err := validatedPackageSet(profile.AllowReferencedPackages, "profile reference override"); err != nil {
		return profile, err
	}
	return profile, nil
}

func validatedPackageSet(values []string, label string) (map[string]bool, error) {
	set := make(map[string]bool, len(values))
	for _, value := range values {
		value = strings.TrimSpace(value)
		if !strings.HasPrefix(value, "com.") || strings.ContainsAny(value, " \\/\t\r\n") {
			return nil, fmt.Errorf("invalid %s ID '%s'", label, value)
		}
		if set[value] {
			return nil, fmt.Errorf("duplicate %s ID '%s'", label, value)
		}
		set[value] = true
	}
	return set, nil
}

func parseManifest(data []byte) (manifestDocument, error) {
	members, err := decodeObject(data)
	if err != nil {
		return manifestDocument{}, err
	}
	var dependencies []dependency
	found := false
	for _, member := range members {
		if member.Name != "dependencies" {
			continue
		}
		if found {
			return manifestDocument{}, errors.New("duplicate dependencies object")
		}
		found = true
		dependencyMembers, err := decodeObject(member.Value)
		if err != nil {
			return manifestDocument{}, fmt.Errorf("dependencies must be an object: %w", err)
		}
		seen := make(map[string]bool, len(dependencyMembers))
		for _, item := range dependencyMembers {
			var version string
			if err := json.Unmarshal(item.Value, &version); err != nil || strings.TrimSpace(version) == "" {
				return manifestDocument{}, fmt.Errorf("dependency '%s' must have a non-empty string version", item.Name)
			}
			if seen[item.Name] {
				return manifestDocument{}, fmt.Errorf("duplicate dependency '%s'", item.Name)
			}
			seen[item.Name] = true
			dependencies = append(dependencies, dependency{Name: item.Name, Version: version})
		}
	}
	if !found {
		return manifestDocument{}, errors.New("dependencies object is missing")
	}
	return manifestDocument{Members: members, Dependencies: dependencies}, nil
}

func decodeObject(data []byte) ([]jsonMember, error) {
	decoder := json.NewDecoder(bytes.NewReader(data))
	token, err := decoder.Token()
	if err != nil {
		return nil, err
	}
	if delimiter, ok := token.(json.Delim); !ok || delimiter != '{' {
		return nil, errors.New("expected JSON object")
	}
	var members []jsonMember
	seen := make(map[string]bool)
	for decoder.More() {
		nameToken, err := decoder.Token()
		if err != nil {
			return nil, err
		}
		name, ok := nameToken.(string)
		if !ok || seen[name] {
			return nil, fmt.Errorf("invalid or duplicate object member '%v'", nameToken)
		}
		seen[name] = true
		var raw json.RawMessage
		if err := decoder.Decode(&raw); err != nil {
			return nil, err
		}
		members = append(members, jsonMember{Name: name, Value: raw})
	}
	if _, err := decoder.Token(); err != nil {
		return nil, err
	}
	if err := ensureJSONEOF(decoder); err != nil {
		return nil, err
	}
	return members, nil
}

func ensureJSONEOF(decoder *json.Decoder) error {
	var extra interface{}
	if err := decoder.Decode(&extra); err == io.EOF {
		return nil
	} else if err != nil {
		return err
	}
	return errors.New("unexpected data after JSON document")
}

func (document manifestDocument) without(removeSet map[string]bool) ([]byte, error) {
	var output bytes.Buffer
	output.WriteString("{\n")
	for memberIndex, member := range document.Members {
		name, _ := json.Marshal(member.Name)
		output.WriteString("  ")
		output.Write(name)
		output.WriteString(": ")
		if member.Name == "dependencies" {
			output.WriteString("{\n")
			remaining := make([]dependency, 0, len(document.Dependencies))
			for _, item := range document.Dependencies {
				if !removeSet[item.Name] {
					remaining = append(remaining, item)
				}
			}
			for index, item := range remaining {
				encodedName, _ := json.Marshal(item.Name)
				encodedVersion, _ := json.Marshal(item.Version)
				output.WriteString("    ")
				output.Write(encodedName)
				output.WriteString(": ")
				output.Write(encodedVersion)
				if index+1 != len(remaining) {
					output.WriteByte(',')
				}
				output.WriteByte('\n')
			}
			output.WriteString("  }")
		} else {
			var indented bytes.Buffer
			if err := json.Indent(&indented, member.Value, "  ", "  "); err != nil {
				return nil, fmt.Errorf("cannot format member '%s': %w", member.Name, err)
			}
			output.Write(indented.Bytes())
		}
		if memberIndex+1 != len(document.Members) {
			output.WriteByte(',')
		}
		output.WriteByte('\n')
	}
	output.WriteString("}\n")
	return output.Bytes(), nil
}

func validateLockGraph(data []byte, removals map[string]bool) error {
	lockDependencies, err := parseLockNodes(data)
	if err != nil {
		return fmt.Errorf("malformed packages-lock.json: %w", err)
	}
	for packageID := range removals {
		node, exists := lockDependencies[packageID]
		if !exists {
			return fmt.Errorf("authorized manifest dependency '%s' is absent from packages-lock.json", packageID)
		}
		if node.Depth != 0 {
			return fmt.Errorf("authorized manifest dependency '%s' has non-root lock depth %d", packageID, node.Depth)
		}
	}
	for owner, node := range lockDependencies {
		if removals[owner] {
			continue
		}
		for dependencyID := range node.Dependencies {
			if removals[dependencyID] {
				return fmt.Errorf("remaining lock node '%s' still depends on '%s'", owner, dependencyID)
			}
		}
	}
	return nil
}

func parseLockNodes(data []byte) (map[string]lockNode, error) {
	topLevel, err := decodeObject(data)
	if err != nil {
		return nil, err
	}
	var dependencyObject json.RawMessage
	for _, member := range topLevel {
		if member.Name == "dependencies" {
			dependencyObject = member.Value
			break
		}
	}
	if dependencyObject == nil {
		return nil, errors.New("dependencies object is missing")
	}
	packageMembers, err := decodeObject(dependencyObject)
	if err != nil {
		return nil, fmt.Errorf("dependencies is not an object: %w", err)
	}
	nodes := make(map[string]lockNode, len(packageMembers))
	for _, packageMember := range packageMembers {
		fields, err := decodeObject(packageMember.Value)
		if err != nil {
			return nil, fmt.Errorf("lock node '%s' is invalid: %w", packageMember.Name, err)
		}
		node := lockNode{Depth: -1, Dependencies: make(map[string]string)}
		for _, field := range fields {
			switch field.Name {
			case "depth":
				if err := json.Unmarshal(field.Value, &node.Depth); err != nil || node.Depth < 0 {
					return nil, fmt.Errorf("lock node '%s' has an invalid depth", packageMember.Name)
				}
			case "dependencies":
				children, err := decodeObject(field.Value)
				if err != nil {
					return nil, fmt.Errorf("lock node '%s' dependencies are invalid: %w", packageMember.Name, err)
				}
				for _, child := range children {
					var version string
					if err := json.Unmarshal(child.Value, &version); err != nil || strings.TrimSpace(version) == "" {
						return nil, fmt.Errorf("lock edge '%s' -> '%s' has an invalid version", packageMember.Name, child.Name)
					}
					node.Dependencies[child.Name] = version
				}
			}
		}
		if node.Depth < 0 {
			return nil, fmt.Errorf("lock node '%s' has no depth", packageMember.Name)
		}
		nodes[packageMember.Name] = node
	}
	return nodes, nil
}

func validateSourceEvidencePolicy(projectRoot string, removals, overrides map[string]bool) (map[string][]string, error) {
	evidence, err := findSourceEvidence(projectRoot, removals)
	if err != nil {
		return nil, fmt.Errorf("cannot complete bounded source-dependency inspection: %w", err)
	}
	packageIDs := make([]string, 0, len(evidence))
	for packageID := range evidence {
		packageIDs = append(packageIDs, packageID)
	}
	sort.Strings(packageIDs)
	for _, packageID := range packageIDs {
		if overrides[packageID] {
			continue
		}
		paths := evidence[packageID]
		return nil, fmt.Errorf("source evidence still references %s (for example %s); remove the dependency first or explicitly pass -allow-referenced-package %s after review", packageID, paths[0], packageID)
	}
	return evidence, nil
}

func findSourceEvidence(projectRoot string, removals map[string]bool) (map[string][]string, error) {
	needles := make(map[string][]string)
	for _, signature := range sourceSignatures {
		if removals[signature.PackageID] {
			needles[signature.PackageID] = signature.Needles
		}
	}
	if len(needles) == 0 {
		return nil, nil
	}
	evidence := make(map[string][]string)
	visited := 0
	for _, rootName := range []string{"Assets", "ProjectSettings"} {
		root := filepath.Join(projectRoot, rootName)
		err := filepath.WalkDir(root, func(path string, entry fs.DirEntry, walkErr error) error {
			if walkErr != nil {
				return walkErr
			}
			if entry.Type()&os.ModeSymlink != 0 {
				return fmt.Errorf("source scan encountered a symbolic link or reparse point: %s", path)
			}
			if redirected, inspectErr := pathIsReparsePoint(path); inspectErr != nil || redirected {
				return fmt.Errorf("source scan encountered a redirected or unreadable path: %s", path)
			}
			if err := safefs.ValidateMountBoundary(projectRoot, path); err != nil {
				return fmt.Errorf("source scan crossed a mount boundary: %w", err)
			}
			if entry.IsDir() {
				return nil
			}
			extension := strings.ToLower(filepath.Ext(path))
			if extension != ".cs" && extension != ".asmdef" && extension != ".asmref" && extension != ".json" {
				return nil
			}
			visited++
			if visited > maximumEvidenceFiles {
				return fmt.Errorf("source scan exceeded %d evidence files", maximumEvidenceFiles)
			}
			info, err := entry.Info()
			if err != nil {
				return err
			}
			if !info.Mode().IsRegular() || info.Mode()&os.ModeSymlink != 0 {
				return fmt.Errorf("source evidence path is not a regular file: %s", path)
			}
			if info.Size() > 4*1024*1024 {
				return fmt.Errorf("source evidence file exceeds 4 MiB budget: %s", path)
			}
			data, err := os.ReadFile(path)
			if err != nil {
				return err
			}
			after, err := os.Lstat(path)
			if err != nil || !after.Mode().IsRegular() || after.Mode()&os.ModeSymlink != 0 ||
				!os.SameFile(info, after) || info.Size() != after.Size() ||
				!info.ModTime().Equal(after.ModTime()) || info.Mode().Perm() != after.Mode().Perm() {
				return fmt.Errorf("source evidence file changed while reading: %s", path)
			}
			if redirected, inspectErr := pathIsReparsePoint(path); inspectErr != nil || redirected {
				return fmt.Errorf("source evidence path became redirected while reading: %s", path)
			}
			text := string(data)
			for packageID, packageNeedles := range needles {
				for _, needle := range packageNeedles {
					if strings.Contains(text, needle) {
						relative, _ := filepath.Rel(projectRoot, path)
						evidence[packageID] = append(evidence[packageID], filepath.ToSlash(relative))
						break
					}
				}
			}
			return nil
		})
		if err != nil {
			return nil, err
		}
	}
	return evidence, nil
}

func ensureRemovalPreimageUnchanged(projectRoot, manifestPath string, manifestBytes []byte, lockPath string, lockBytes []byte, lockExists bool) error {
	if err := ensureNoPackageTransactionEvidence(filepath.Dir(manifestPath), lockPath); err != nil {
		return err
	}
	if err := ensureExactRegularPreimage(projectRoot, manifestPath, manifestBytes); err != nil {
		return fmt.Errorf("manifest preimage drift: %w", err)
	}
	if lockExists {
		if err := ensureExactRegularPreimage(projectRoot, lockPath, lockBytes); err != nil {
			return fmt.Errorf("lock preimage drift: %w", err)
		}
		return nil
	}
	if _, err := os.Lstat(lockPath); err == nil {
		return errors.New("packages-lock.json appeared after validation")
	} else if !errors.Is(err, os.ErrNotExist) {
		return fmt.Errorf("cannot prove packages-lock.json remains absent: %w", err)
	}
	return nil
}

func ensureNoPackageTransactionEvidence(packagesDirectory, lockPath string) error {
	prefixes := []string{
		filepath.Base(lockPath) + ".removal-transaction-",
		".package-removal-transaction-",
	}
	return ensureNoDirectoryEntriesMatching(
		packagesDirectory,
		"incomplete prior package-removal transaction exists",
		func(name string) bool {
			for _, prefix := range prefixes {
				if strings.HasPrefix(name, prefix) {
					return true
				}
			}
			return false
		},
	)
}

func ensureNoStalePackageStages(packagesDirectory string) error {
	directStagePrefixes := []string{
		".manifest.json.stage-",
		".packages-lock.json.stage-",
	}
	backupPrefixes := []string{
		".manifest.json.backup-",
		".packages-lock.json.backup-",
	}
	return ensureNoDirectoryEntriesMatching(
		packagesDirectory,
		"stale package stage exists",
		func(name string) bool {
			for _, prefix := range directStagePrefixes {
				if strings.HasPrefix(name, prefix) {
					return true
				}
			}
			for _, prefix := range backupPrefixes {
				if strings.HasPrefix(name, prefix) && strings.Contains(name[len(prefix):], ".stage-") {
					return true
				}
			}
			return false
		},
	)
}

func ensureNoDirectoryEntriesMatching(directory, evidenceDescription string, matches func(string) bool) (returnErr error) {
	directoryHandle, err := os.Open(directory)
	if err != nil {
		return fmt.Errorf("cannot inspect %s: %w", directory, err)
	}
	defer func() {
		if closeErr := directoryHandle.Close(); closeErr != nil && returnErr == nil {
			returnErr = fmt.Errorf("cannot finish inspecting %s: %w", directory, closeErr)
		}
	}()

	for {
		names, readErr := directoryHandle.Readdirnames(256)
		for _, name := range names {
			if matches(name) {
				return fmt.Errorf("%s: %s", evidenceDescription, filepath.Join(directory, name))
			}
		}
		if errors.Is(readErr, io.EOF) {
			return nil
		}
		if readErr != nil {
			return fmt.Errorf("cannot finish inspecting %s: %w", directory, readErr)
		}
	}
}

func ensureExactRegularPreimage(projectRoot, path string, expected []byte) error {
	_, err := exactRegularPreimageSnapshot(projectRoot, path, expected)
	return err
}

func exactRegularPreimageSnapshot(projectRoot, path string, expected []byte) (os.FileInfo, error) {
	infoBefore, err := os.Lstat(path)
	if err != nil {
		return nil, err
	}
	if !infoBefore.Mode().IsRegular() || infoBefore.Mode()&os.ModeSymlink != 0 {
		return nil, fmt.Errorf("path is not a regular non-symlink file: %s", path)
	}
	if err := ensureProjectFileNotRedirected(projectRoot, path); err != nil {
		return nil, err
	}
	actual, err := os.ReadFile(path)
	if err != nil {
		return nil, err
	}
	infoAfter, err := os.Lstat(path)
	if err != nil {
		return nil, err
	}
	if !infoAfter.Mode().IsRegular() || infoAfter.Mode()&os.ModeSymlink != 0 ||
		!os.SameFile(infoBefore, infoAfter) || infoBefore.Size() != infoAfter.Size() ||
		!infoBefore.ModTime().Equal(infoAfter.ModTime()) || infoBefore.Mode().Perm() != infoAfter.Mode().Perm() {
		return nil, errors.New("file identity changed while its preimage was read")
	}
	if !bytes.Equal(actual, expected) {
		return nil, errors.New("file bytes no longer match the validated preimage")
	}
	return infoAfter, nil
}

func commitManifestTransaction(projectRoot, manifestPath string, original, updated []byte, lockPath string, lockBytes []byte, lockExists bool, validateMutationEvidence func() error) ([]string, error) {
	timestamp := time.Now().UTC().Format("20060102T150405.000000000Z")
	manifestIdentity, err := exactRegularPreimageSnapshot(projectRoot, manifestPath, original)
	if err != nil {
		return nil, fmt.Errorf("cannot capture the validated manifest identity: %w", err)
	}
	manifestPermission := manifestIdentity.Mode().Perm()
	var lockIdentity os.FileInfo
	lockPermission := fs.FileMode(0600)
	if lockExists {
		lockIdentity, err = exactRegularPreimageSnapshot(projectRoot, lockPath, lockBytes)
		if err != nil {
			return nil, fmt.Errorf("cannot capture the validated lock identity: %w", err)
		}
		lockPermission = lockIdentity.Mode().Perm()
	}
	stagePath, err := stageDurableFile(manifestPath, updated, manifestPermission)
	if err != nil {
		return nil, err
	}
	stageIdentity, err := exactRegularPreimageSnapshot(projectRoot, stagePath, updated)
	if err != nil {
		return nil, fmt.Errorf("cannot bind staged manifest identity: %w", err)
	}
	if err := ensureRemovalPreimageUnchanged(projectRoot, manifestPath, original, lockPath, lockBytes, lockExists); err != nil {
		return nil, fmt.Errorf("validated package preimage changed while staging; stage retained at %s: %w", stagePath, err)
	}

	manifestBackup := manifestPath + ".backup-" + timestamp
	if err := publishExclusiveFile(manifestBackup, original, 0600); err != nil {
		return []string{manifestBackup}, fmt.Errorf("mandatory manifest backup failed; possible backup evidence retained at %s: %w", manifestBackup, err)
	}
	backups := []string{manifestBackup}
	if lockExists {
		lockBackup := lockPath + ".backup-" + timestamp
		if err := publishExclusiveFile(lockBackup, lockBytes, 0600); err != nil {
			backups = append(backups, lockBackup)
			return backups, fmt.Errorf("mandatory lock backup failed; possible backup evidence retained at %s: %w", lockBackup, err)
		}
		backups = append(backups, lockBackup)
	}
	if err := ensureRemovalPreimageUnchanged(projectRoot, manifestPath, original, lockPath, lockBytes, lockExists); err != nil {
		return backups, fmt.Errorf("validated package preimage changed after backup and before claim; targets were not mutated: %w", err)
	}
	if validateMutationEvidence == nil {
		return backups, errors.New("package mutation evidence validator is unavailable; targets were not mutated")
	}
	if err := validateMutationEvidence(); err != nil {
		return backups, fmt.Errorf("mutation evidence changed before canonical package claims; targets were not mutated: %w", err)
	}
	transactionRoot, journalPath, journal, err := createPackageRemovalTransaction(filepath.Dir(manifestPath), manifestPath, lockPath, lockExists)
	if err != nil {
		return backups, fmt.Errorf("cannot create exclusive package-removal transaction: %w", err)
	}
	var manifestClaim, lockClaim, guardClaim, publishedClaim *claimedPackageFile
	rollback := func(cause error) ([]string, error) {
		journal.State = "recovery-required"
		_ = writePackageRemovalJournal(journalPath, journal, false)
		rollbackErr := rollbackPackageRemovalTransaction(transactionRoot, journalPath, journal, manifestClaim, lockClaim, guardClaim, publishedClaim, stagePath, stageIdentity)
		if rollbackErr != nil {
			return backups, fmt.Errorf("%v; rollback could not be fully proven and recovery evidence was retained at %s: %w", cause, transactionRoot, rollbackErr)
		}
		return backups, fmt.Errorf("%v; original package state was restored without replacing concurrent paths", cause)
	}
	manifestClaim, err = claimPackageFile(projectRoot, manifestPath, filepath.Join(transactionRoot, "original-manifest.json"), original, manifestIdentity, manifestPermission)
	if err != nil {
		return rollback(fmt.Errorf("manifest no-replace claim failed: %w", err))
	}
	journal.Entries[0].State = "claimed-and-verified"
	if lockExists {
		lockClaim, err = claimPackageFile(projectRoot, lockPath, filepath.Join(transactionRoot, "original-packages-lock.json"), lockBytes, lockIdentity, lockPermission)
		if err != nil {
			return rollback(fmt.Errorf("lock no-replace claim failed: %w", err))
		}
		journal.Entries[1].State = "claimed-and-verified"
	}
	lockGuard := []byte(fmt.Sprintf("{\"dependencies\":{},\"cycloneGamesRemovalTransaction\":%q}\n", journal.TransactionID))
	if err := publishExclusiveFile(lockPath, lockGuard, 0600); err != nil {
		if guardIdentity, snapshotErr := exactRegularPreimageSnapshot(projectRoot, lockPath, lockGuard); snapshotErr == nil {
			guardClaim = &claimedPackageFile{
				canonicalPath:    lockPath,
				claimedPath:      filepath.Join(transactionRoot, "lock-absence-guard.json"),
				expected:         lockGuard,
				expectedIdentity: guardIdentity,
				claimedIdentity:  guardIdentity,
				permission:       guardIdentity.Mode().Perm(),
			}
		}
		return rollback(fmt.Errorf("lock absence claim failed without overwriting the canonical path: %w", err))
	}
	guardIdentity, err := exactRegularPreimageSnapshot(projectRoot, lockPath, lockGuard)
	if err != nil {
		return rollback(fmt.Errorf("lock absence claim identity could not be bound: %w", err))
	}
	guardClaim = &claimedPackageFile{
		canonicalPath:    lockPath,
		claimedPath:      filepath.Join(transactionRoot, "lock-absence-guard.json"),
		expected:         lockGuard,
		expectedIdentity: guardIdentity,
		claimedIdentity:  guardIdentity,
		permission:       guardIdentity.Mode().Perm(),
	}
	journal.Entries[2].State = "claimed-at-canonical"
	journal.State = "claimed"
	if err := writePackageRemovalJournal(journalPath, journal, false); err != nil {
		return rollback(fmt.Errorf("cannot persist claimed transaction state: %w", err))
	}
	if err := validateClaimedPackageFile(projectRoot, manifestClaim); err != nil {
		return rollback(err)
	}
	if lockClaim != nil {
		if err := validateClaimedPackageFile(projectRoot, lockClaim); err != nil {
			return rollback(err)
		}
	}
	if err := ensureExactRegularPreimage(projectRoot, lockPath, lockGuard); err != nil {
		return rollback(fmt.Errorf("lock absence claim drifted before publish: %w", err))
	}
	if err := validateMutationEvidence(); err != nil {
		return rollback(fmt.Errorf("mutation evidence changed after canonical claims and before publish: %w", err))
	}
	if err := validateClaimedPackageFile(projectRoot, manifestClaim); err != nil {
		return rollback(fmt.Errorf("manifest claim drifted during final evidence scan: %w", err))
	}
	if lockClaim != nil {
		if err := validateClaimedPackageFile(projectRoot, lockClaim); err != nil {
			return rollback(fmt.Errorf("lock claim drifted during final evidence scan: %w", err))
		}
	}
	if err := ensureExactRegularPreimage(projectRoot, lockPath, lockGuard); err != nil {
		return rollback(fmt.Errorf("lock absence claim drifted during final evidence scan: %w", err))
	}
	if err := safefs.PublishFileNoReplace(stagePath, manifestPath); err != nil {
		if current, statErr := os.Lstat(manifestPath); statErr == nil && os.SameFile(stageIdentity, current) {
			publishedClaim = &claimedPackageFile{
				canonicalPath:    manifestPath,
				claimedPath:      filepath.Join(transactionRoot, "published-manifest.json"),
				expected:         updated,
				expectedIdentity: stageIdentity,
				claimedIdentity:  current,
				permission:       manifestPermission,
			}
		}
		return rollback(fmt.Errorf("manifest no-replace publish failed: %w", err))
	}
	publishedIdentity, err := exactRegularPreimageSnapshot(projectRoot, manifestPath, updated)
	if err != nil || !os.SameFile(stageIdentity, publishedIdentity) {
		return rollback(fmt.Errorf("published manifest identity/read-back verification failed: %v", err))
	}
	publishedClaim = &claimedPackageFile{
		canonicalPath:    manifestPath,
		claimedPath:      filepath.Join(transactionRoot, "published-manifest.json"),
		expected:         updated,
		expectedIdentity: stageIdentity,
		claimedIdentity:  publishedIdentity,
		permission:       manifestPermission,
	}
	journal.Entries[3].State = "published-at-canonical"
	journal.State = "published"
	if err := writePackageRemovalJournal(journalPath, journal, false); err != nil {
		return rollback(fmt.Errorf("cannot persist published transaction state: %w", err))
	}
	if err := quarantineCanonicalPackageFile(projectRoot, guardClaim); err != nil {
		return rollback(fmt.Errorf("lock guard final claim failed: %w", err))
	}
	journal.Entries[2].State = "quarantined"
	journal.State = "finalizing"
	if err := writePackageRemovalJournal(journalPath, journal, false); err != nil {
		return rollback(fmt.Errorf("cannot persist finalizing transaction state: %w", err))
	}
	finalPublishedIdentity, err := exactRegularPreimageSnapshot(projectRoot, manifestPath, updated)
	if err != nil || publishedClaim == nil || publishedClaim.claimedIdentity == nil || !os.SameFile(publishedClaim.claimedIdentity, finalPublishedIdentity) {
		journal.State = "recovery-required"
		journal.Entries[3].State = "canonical-drifted-before-finalization"
		_ = writePackageRemovalJournal(journalPath, journal, false)
		return backups, fmt.Errorf("published manifest changed before transaction finalization; no external canonical state was overwritten and recovery evidence was retained at %s: %v", transactionRoot, err)
	}
	for _, claim := range []*claimedPackageFile{manifestClaim, lockClaim, guardClaim} {
		if claim == nil {
			continue
		}
		if err := removeClaimedPackageFile(projectRoot, claim); err != nil {
			return backups, fmt.Errorf("package transaction committed but quarantine reclamation failed; recovery evidence retained at %s: %w", transactionRoot, err)
		}
	}
	journal.Entries[0].State = "deleted"
	if lockClaim != nil {
		journal.Entries[1].State = "deleted"
	}
	journal.Entries[2].State = "deleted"
	journal.State = "complete"
	if err := writePackageRemovalJournal(journalPath, journal, false); err != nil {
		return backups, fmt.Errorf("package transaction committed but final journal write failed; recovery evidence retained at %s: %w", transactionRoot, err)
	}
	if err := cleanupPackageRemovalTransaction(transactionRoot, journalPath); err != nil {
		return backups, fmt.Errorf("package transaction committed but transaction finalization failed; recovery evidence retained at %s: %w", transactionRoot, err)
	}
	return backups, nil
}

func createPackageRemovalTransaction(packagesDirectory, manifestPath, lockPath string, lockExists bool) (string, string, *packageRemovalJournal, error) {
	if err := ensureNoPackageTransactionEvidence(packagesDirectory, lockPath); err != nil {
		return "", "", nil, err
	}
	randomBytes := make([]byte, 16)
	if _, err := rand.Read(randomBytes); err != nil {
		return "", "", nil, err
	}
	transactionID := fmt.Sprintf("%x", randomBytes)
	transactionRoot := filepath.Join(packagesDirectory, ".package-removal-transaction-"+transactionID)
	if err := safefs.CreateExclusiveDirectory(transactionRoot, 0700); err != nil {
		return transactionRoot, "", nil, fmt.Errorf("exclusive transaction directory creation failed; possible recovery evidence at %s: %w", transactionRoot, err)
	}
	lockState := "planned"
	if !lockExists {
		lockState = "originally-absent"
	}
	journal := &packageRemovalJournal{
		DocumentType:  "unity-package-removal-transaction",
		TransactionID: transactionID,
		State:         "planned",
		StartedUTC:    time.Now().UTC().Format(time.RFC3339Nano),
		Entries: []packageRemovalEntry{
			{CanonicalPath: manifestPath, ClaimedPath: filepath.Join(transactionRoot, "original-manifest.json"), State: "planned"},
			{CanonicalPath: lockPath, ClaimedPath: filepath.Join(transactionRoot, "original-packages-lock.json"), State: lockState},
			{CanonicalPath: lockPath, ClaimedPath: filepath.Join(transactionRoot, "lock-absence-guard.json"), State: "planned"},
			{CanonicalPath: manifestPath, ClaimedPath: filepath.Join(transactionRoot, "published-manifest.json"), State: "planned"},
		},
	}
	journalPath := filepath.Join(transactionRoot, "transaction.json")
	if err := writePackageRemovalJournal(journalPath, journal, true); err != nil {
		return transactionRoot, journalPath, journal, fmt.Errorf("initial package transaction journal failed; recovery evidence retained at %s: %w", transactionRoot, err)
	}
	return transactionRoot, journalPath, journal, nil
}

func writePackageRemovalJournal(path string, journal *packageRemovalJournal, initial bool) error {
	data, err := json.Marshal(journal)
	if err != nil {
		return err
	}
	stage, err := stageDurableFile(path, data, 0600)
	if err != nil {
		return err
	}
	if initial {
		return safefs.PublishFileNoReplace(stage, path)
	}
	return replaceFile(stage, path)
}

func claimPackageFile(projectRoot, canonicalPath, claimedPath string, expected []byte, expectedIdentity os.FileInfo, permission fs.FileMode) (*claimedPackageFile, error) {
	claim := &claimedPackageFile{
		canonicalPath:    canonicalPath,
		claimedPath:      claimedPath,
		expected:         expected,
		expectedIdentity: expectedIdentity,
		permission:       permission,
	}
	moveErr := safefs.MoveNoReplace(canonicalPath, claimedPath)
	claimedInfo, claimedErr := exactRegularPreimageSnapshot(projectRoot, claimedPath, expected)
	if claimedErr == nil && expectedIdentity != nil && os.SameFile(expectedIdentity, claimedInfo) && claimedInfo.Mode().Perm() == permission.Perm() {
		claim.claimedIdentity = claimedInfo
	}
	if moveErr != nil {
		return claim, moveErr
	}
	if claimedErr != nil {
		return claim, claimedErr
	}
	if claim.claimedIdentity == nil {
		return claim, fmt.Errorf("claimed package file identity or mode does not match the validated source: %s", claimedPath)
	}
	if err := validateClaimedPackageFile(projectRoot, claim); err != nil {
		return claim, err
	}
	return claim, nil
}

func validateClaimedPackageFile(projectRoot string, claim *claimedPackageFile) error {
	if claim == nil || claim.expectedIdentity == nil || claim.claimedIdentity == nil {
		return errors.New("package transaction claim is incomplete")
	}
	current, err := exactRegularPreimageSnapshot(projectRoot, claim.claimedPath, claim.expected)
	if err != nil {
		return err
	}
	if !os.SameFile(claim.expectedIdentity, current) || !os.SameFile(claim.claimedIdentity, current) || current.Mode().Perm() != claim.permission.Perm() {
		return fmt.Errorf("claimed package file identity or mode drifted: %s", claim.claimedPath)
	}
	return nil
}

func quarantineCanonicalPackageFile(projectRoot string, claim *claimedPackageFile) error {
	if claim == nil || claim.claimedIdentity == nil {
		return errors.New("canonical package claim is incomplete")
	}
	current, err := exactRegularPreimageSnapshot(projectRoot, claim.canonicalPath, claim.expected)
	if err != nil {
		return err
	}
	if !os.SameFile(claim.claimedIdentity, current) {
		return fmt.Errorf("canonical package path identity drifted: %s", claim.canonicalPath)
	}
	if err := safefs.MoveNoReplace(claim.canonicalPath, claim.claimedPath); err != nil {
		return err
	}
	moved, err := exactRegularPreimageSnapshot(projectRoot, claim.claimedPath, claim.expected)
	if err != nil || !os.SameFile(claim.claimedIdentity, moved) {
		return fmt.Errorf("quarantined canonical package identity could not be proven: %v", err)
	}
	claim.claimedIdentity = moved
	return nil
}

func restoreClaimedPackageFile(projectRoot string, claim *claimedPackageFile) error {
	if claim == nil {
		return nil
	}
	current, err := os.Lstat(claim.claimedPath)
	if errors.Is(err, os.ErrNotExist) {
		return fmt.Errorf("claimed recovery file is missing: %s", claim.claimedPath)
	}
	if err != nil || claim.claimedIdentity == nil || !os.SameFile(claim.claimedIdentity, current) || current.Mode()&os.ModeSymlink != 0 {
		return fmt.Errorf("claimed recovery file identity drifted: %s", claim.claimedPath)
	}
	if err := safefs.MoveNoReplace(claim.claimedPath, claim.canonicalPath); err != nil {
		return err
	}
	restored, err := os.Lstat(claim.canonicalPath)
	if err != nil || !os.SameFile(claim.claimedIdentity, restored) {
		return fmt.Errorf("restored package file identity could not be proven: %s", claim.canonicalPath)
	}
	return nil
}

func removeClaimedPackageFile(projectRoot string, claim *claimedPackageFile) error {
	if claim == nil {
		return nil
	}
	current, err := exactRegularPreimageSnapshot(projectRoot, claim.claimedPath, claim.expected)
	if err != nil || claim.claimedIdentity == nil || !os.SameFile(claim.claimedIdentity, current) {
		return fmt.Errorf("refusing to remove drifted claimed package file '%s': %v", claim.claimedPath, err)
	}
	return safefs.RemoveDurably(claim.claimedPath)
}

func rollbackPackageRemovalTransaction(transactionRoot, journalPath string, journal *packageRemovalJournal, manifestClaim, lockClaim, guardClaim, publishedClaim *claimedPackageFile, stagePath string, stageIdentity os.FileInfo) error {
	var failures []string
	if publishedClaim != nil {
		if _, err := os.Lstat(publishedClaim.canonicalPath); err == nil {
			if err := quarantineCanonicalPackageFile(filepath.Dir(filepath.Dir(publishedClaim.canonicalPath)), publishedClaim); err != nil {
				failures = append(failures, "published manifest claim: "+err.Error())
			}
		} else if !errors.Is(err, os.ErrNotExist) {
			failures = append(failures, "published manifest inspection: "+err.Error())
		}
	}
	if guardClaim != nil {
		if _, err := os.Lstat(guardClaim.canonicalPath); err == nil {
			if err := quarantineCanonicalPackageFile(filepath.Dir(filepath.Dir(guardClaim.canonicalPath)), guardClaim); err != nil {
				failures = append(failures, "lock guard claim: "+err.Error())
			}
		} else if !errors.Is(err, os.ErrNotExist) {
			failures = append(failures, "lock guard inspection: "+err.Error())
		}
	}
	if err := restoreClaimedPackageFile(filepath.Dir(filepath.Dir(manifestClaim.canonicalPath)), manifestClaim); err != nil {
		failures = append(failures, "manifest restore: "+err.Error())
	}
	if lockClaim != nil {
		if err := restoreClaimedPackageFile(filepath.Dir(filepath.Dir(lockClaim.canonicalPath)), lockClaim); err != nil {
			failures = append(failures, "lock restore: "+err.Error())
		}
	}
	if len(failures) != 0 {
		return errors.New(strings.Join(failures, "; "))
	}
	projectRoot := filepath.Dir(filepath.Dir(manifestClaim.canonicalPath))
	for _, claim := range []*claimedPackageFile{publishedClaim, guardClaim} {
		if claim == nil {
			continue
		}
		if _, err := os.Lstat(claim.claimedPath); err == nil {
			if err := removeClaimedPackageFile(projectRoot, claim); err != nil {
				return err
			}
		} else if !errors.Is(err, os.ErrNotExist) {
			return err
		}
	}
	if stageInfo, err := os.Lstat(stagePath); err == nil {
		if stageIdentity == nil || !os.SameFile(stageIdentity, stageInfo) || stageInfo.Mode()&os.ModeSymlink != 0 {
			return fmt.Errorf("manifest stage identity drifted; refusing cleanup: %s", stagePath)
		}
		if err := safefs.RemoveDurably(stagePath); err != nil {
			return err
		}
	} else if !errors.Is(err, os.ErrNotExist) {
		return err
	}
	journal.State = "rolled-back"
	if err := writePackageRemovalJournal(journalPath, journal, false); err != nil {
		return err
	}
	return cleanupPackageRemovalTransaction(transactionRoot, journalPath)
}

func cleanupPackageRemovalTransaction(transactionRoot, journalPath string) error {
	entries, err := os.ReadDir(transactionRoot)
	if err != nil {
		return err
	}
	if len(entries) != 1 || entries[0].Name() != filepath.Base(journalPath) {
		return fmt.Errorf("package transaction is not empty: %s", transactionRoot)
	}
	if err := safefs.RemoveDurably(journalPath); err != nil {
		return err
	}
	return safefs.RemoveDurably(transactionRoot)
}

func publishExclusiveFile(path string, data []byte, permission fs.FileMode) error {
	stage, err := stageDurableFile(path, data, permission)
	if err != nil {
		return err
	}
	stageIdentity, err := os.Lstat(stage)
	if err != nil || !stageIdentity.Mode().IsRegular() || stageIdentity.Mode()&os.ModeSymlink != 0 {
		return fmt.Errorf("exclusive publish stage is unavailable or redirected; evidence retained at %s", stage)
	}
	stageFile, err := os.Open(stage)
	if err != nil {
		return fmt.Errorf("exclusive publish stage cannot be opened; evidence retained at %s: %w", stage, err)
	}
	stageHandleIdentity, statErr := stageFile.Stat()
	closeErr := stageFile.Close()
	if statErr != nil || closeErr != nil || !stageHandleIdentity.Mode().IsRegular() || !os.SameFile(stageIdentity, stageHandleIdentity) {
		return fmt.Errorf("exclusive publish stage handle identity cannot be proven; evidence retained at %s", stage)
	}
	stageIdentity = stageHandleIdentity
	if err := safefs.PublishFileNoReplace(stage, path); err != nil {
		return fmt.Errorf("exclusive no-replace file publish failed; stage or target evidence retained at %s and %s: %w", stage, path, err)
	}
	publishedIdentity, err := os.Lstat(path)
	if err != nil || !publishedIdentity.Mode().IsRegular() || publishedIdentity.Mode()&os.ModeSymlink != 0 ||
		!os.SameFile(stageIdentity, publishedIdentity) || publishedIdentity.Mode().Perm() != stageIdentity.Mode().Perm() {
		return fmt.Errorf("exclusive published file identity or mode drifted; evidence retained at %s", path)
	}
	readBack, err := os.ReadFile(path)
	if err != nil || !bytes.Equal(readBack, data) {
		return fmt.Errorf("published file read-back mismatch; evidence retained at %s", path)
	}
	return nil
}

func stageDurableFile(target string, data []byte, permission fs.FileMode) (string, error) {
	directory := filepath.Dir(target)
	file, err := os.CreateTemp(directory, "."+filepath.Base(target)+".stage-*")
	if err != nil {
		return "", err
	}
	path := file.Name()
	if err := file.Chmod(permission); err != nil {
		_ = file.Close()
		return "", fmt.Errorf("stage chmod failed; evidence retained at %s: %w", path, err)
	}
	if _, err := file.Write(data); err != nil {
		_ = file.Close()
		return "", fmt.Errorf("stage write failed; evidence retained at %s: %w", path, err)
	}
	if err := file.Sync(); err != nil {
		_ = file.Close()
		return "", fmt.Errorf("stage sync failed; evidence retained at %s: %w", path, err)
	}
	if err := file.Close(); err != nil {
		return "", fmt.Errorf("stage close failed; evidence retained at %s: %w", path, err)
	}
	readBack, err := os.ReadFile(path)
	if err != nil || !bytes.Equal(readBack, data) {
		return "", fmt.Errorf("staged file read-back mismatch; evidence retained at %s", path)
	}
	if err := syncParentDirectory(path); err != nil {
		return "", fmt.Errorf("staged file parent-directory sync failed; evidence retained at %s: %w", path, err)
	}
	return path, nil
}

func replaceFile(stagePath, targetPath string) error {
	return replacePathAtomically(stagePath, targetPath)
}

func readBoundedFile(path string) ([]byte, error) {
	info, err := os.Stat(path)
	if err != nil {
		return nil, err
	}
	if !info.Mode().IsRegular() || info.Size() < 2 || info.Size() > maximumJSONBytes {
		return nil, fmt.Errorf("file size/type is outside policy: %s", path)
	}
	return os.ReadFile(path)
}

func readOptionalBoundedFile(path string) ([]byte, bool, error) {
	data, err := readBoundedFile(path)
	if errors.Is(err, os.ErrNotExist) {
		return nil, false, nil
	}
	return data, err == nil, err
}

func samePath(left, right string) bool {
	if os.PathSeparator == '\\' {
		return strings.EqualFold(filepath.Clean(left), filepath.Clean(right))
	}
	return filepath.Clean(left) == filepath.Clean(right)
}

func pathIsDescendant(root, candidate string) bool {
	relative, err := filepath.Rel(filepath.Clean(root), filepath.Clean(candidate))
	if err != nil || relative == "." || relative == "" || filepath.IsAbs(relative) {
		return false
	}
	return relative != ".." && !strings.HasPrefix(relative, ".."+string(os.PathSeparator))
}

func pathExists(path string) bool {
	_, err := os.Lstat(path)
	return err == nil
}

func contains(values []string, value string) bool {
	for _, candidate := range values {
		if candidate == value {
			return true
		}
	}
	return false
}
