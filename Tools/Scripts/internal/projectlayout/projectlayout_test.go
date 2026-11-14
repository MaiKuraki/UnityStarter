package projectlayout

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func TestDefaultLayoutIsValid(t *testing.T) {
	layout := Default()
	if err := layout.Validate(); err != nil {
		t.Fatalf("default layout must be valid: %v", err)
	}
	if len(layout.Markers) != 2 {
		t.Fatalf("default markers = %v, want the two Unity structure markers", layout.Markers)
	}
}

func TestLoadMergesOverrides(t *testing.T) {
	root := t.TempDir()
	writeLayout(t, root, `{"cacheRoots":["Cache","Intermediate"],"leaseFile":"State/clean.lock"}`)
	layout, err := Load(root, "")
	if err == nil {
		// leaseFile must stay inside workspaceDir; the override above is invalid,
		// so an error proves validation is applied to merged documents.
		t.Fatal("an override that moves leaseFile out of workspaceDir must be rejected")
	}

	writeLayout(t, root, `{"cacheRoots":["Cache","Intermediate"],"workspaceDir":"State/Work","leaseFile":"State/Work/clean.lock","leaseMetadataFile":"State/Work/clean.json","quarantineDir":"State/Work/quarantine"}`)
	layout, err = Load(root, "")
	if err != nil {
		t.Fatalf("valid override rejected: %v", err)
	}
	if len(layout.CacheRoots) != 2 || layout.CacheRoots[0] != "Cache" {
		t.Fatalf("cacheRoots override = %v", layout.CacheRoots)
	}
	if layout.LeaseFile != "State/Work/clean.lock" {
		t.Fatalf("leaseFile override = %q", layout.LeaseFile)
	}
	// Undeclared fields retain their defaults.
	if layout.ManifestFile != Default().ManifestFile {
		t.Fatalf("undeclared field changed: %q", layout.ManifestFile)
	}
}

func TestLoadRejectsUnknownMembersAndTrailingData(t *testing.T) {
	root := t.TempDir()
	writeLayout(t, root, `{"unknownMember":true}`)
	if _, err := Load(root, ""); err == nil {
		t.Fatal("unknown layout members must be rejected")
	}
	writeLayout(t, root, `{"cacheRoots":["Cache"]} {"cacheRoots":["Other"]}`)
	if _, err := Load(root, ""); err == nil {
		t.Fatal("trailing data must be rejected")
	}
}

func TestLoadRejectsUnsafePaths(t *testing.T) {
	for _, document := range []string{
		`{"cacheRoots":["../outside"]}`,
		`{"cacheRoots":["/absolute"]}`,
		`{"workspaceDir":"C:/tmp"}`,
		`{"cacheRoots":[""]}`,
	} {
		root := t.TempDir()
		writeLayout(t, root, document)
		if _, err := Load(root, ""); err == nil {
			t.Fatalf("unsafe layout %s must be rejected", document)
		}
	}
}

func TestExplicitLayoutPathIsAuthoritative(t *testing.T) {
	root := t.TempDir()
	override := filepath.Join(t.TempDir(), "custom.json")
	if err := os.WriteFile(override, []byte(`{"cacheRoots":["OnlyThis"]}`), 0o644); err != nil {
		t.Fatal(err)
	}
	layout, err := Load(root, override)
	if err != nil {
		t.Fatal(err)
	}
	if len(layout.CacheRoots) != 1 || layout.CacheRoots[0] != "OnlyThis" {
		t.Fatalf("explicit layout not applied: %v", layout.CacheRoots)
	}
}

func TestLoadRejectsOversizedDocument(t *testing.T) {
	root := t.TempDir()
	path := layoutPath(root)
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(path, []byte(`{"cacheRoots":["`+strings.Repeat("a", 300000)+`"]}`), 0o644); err != nil {
		t.Fatal(err)
	}
	if _, err := Load(root, ""); err == nil {
		t.Fatal("an oversized layout document must be rejected")
	}
}

func TestJoinProvesContainment(t *testing.T) {
	root := t.TempDir()
	layout := Default()
	path, err := layout.Join(root, "Packages/manifest.json")
	if err != nil {
		t.Fatal(err)
	}
	want := filepath.Join(root, "Packages", "manifest.json")
	if path != want {
		t.Fatalf("Join = %q, want %q", path, want)
	}
	if _, err := layout.Join(root, "../escape"); err == nil {
		t.Fatal("a traversal path must be rejected")
	}
	paths, err := layout.JoinAll(root, "Assets", "ProjectSettings")
	if err != nil || len(paths) != 2 {
		t.Fatalf("JoinAll = %v, %v", paths, err)
	}
}

func TestMatchesRoot(t *testing.T) {
	root := t.TempDir()
	layout := Default()
	if layout.MatchesRoot(root) {
		t.Fatal("an empty directory must not match")
	}
	for _, marker := range layout.Markers {
		if err := os.MkdirAll(filepath.Join(root, marker), 0o755); err != nil {
			t.Fatal(err)
		}
	}
	if !layout.MatchesRoot(root) {
		t.Fatal("all structure markers are present")
	}
}

func writeLayout(t *testing.T, root, document string) {
	t.Helper()
	path := layoutPath(root)
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(path, []byte(document), 0o644); err != nil {
		t.Fatal(err)
	}
}

func layoutPath(root string) string {
	return filepath.Join(root, filepath.FromSlash(LayoutFile))
}
