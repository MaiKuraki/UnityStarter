package unity_project_full_clean

import (
	"bytes"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"cyclonegames.tools/scripts/internal/projectlayout"
	"cyclonegames.tools/scripts/internal/recovery"
	"cyclonegames.tools/scripts/internal/safefs"
)

func writeCompletedTransactions(t *testing.T, root string) {
	t.Helper()
	txn := filepath.Join(root, filepath.FromSlash(projectlayout.Default().TransactionsDir))
	files := map[string]string{
		"gameplay-tags/active.json":                             `{"schemaVersion":1,"phase":"Published","effects":[]}`,
		"gameplay-tags/build.lock":                              "process=1\n",
		"gameplay-tags/scratch/note.txt":                        "audit\n",
		"performance-testing/active.json":                       `{"documentType":"build-asset-envelope","payloadBase64":"abc"}`,
		"performance-testing/transaction-abc/transaction.owner": "abc",
		"player/active.lock":                                    "",
	}
	for relative, content := range files {
		path := filepath.Join(txn, filepath.FromSlash(relative))
		if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
			t.Fatal(err)
		}
		if err := os.WriteFile(path, []byte(content), 0o644); err != nil {
			t.Fatal(err)
		}
	}
}

func TestCompletedTransactionsDoNotBlockCleanup(t *testing.T) {
	root := writeUnityProjectFixture(t)
	writeCompletedTransactions(t, root)
	cache := filepath.Join(root, "Library")
	if err := os.MkdirAll(cache, 0o755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(filepath.Join(cache, "cache.bin"), []byte("cache"), 0o644); err != nil {
		t.Fatal(err)
	}
	var stdout, stderr bytes.Buffer
	code := run([]string{"--project", root, "--ci"}, &stdout, &stderr)
	if code != 0 {
		t.Fatalf("exit = %d, stdout:\n%s\nstderr:\n%s", code, stdout.String(), stderr.String())
	}
	if _, err := os.Lstat(cache); !os.IsNotExist(err) {
		t.Fatalf("cache was not removed: %v", err)
	}
	for _, marker := range projectlayout.Default().Markers {
		if _, err := os.Lstat(filepath.Join(root, filepath.FromSlash(marker))); err != nil {
			t.Fatalf("marker %s was removed: %v", marker, err)
		}
	}
}

func TestDryRunCreatesNoFiles(t *testing.T) {
	root := writeUnityProjectFixture(t)
	writeCompletedTransactions(t, root)
	if err := os.MkdirAll(filepath.Join(root, "Library"), 0o755); err != nil {
		t.Fatal(err)
	}
	before := snapshotPaths(t, root)
	var stdout, stderr bytes.Buffer
	code := run([]string{"--project", root, "--dry-run", "--ci"}, &stdout, &stderr)
	if code != 0 {
		t.Fatalf("exit = %d, stderr:\n%s", code, stderr.String())
	}
	after := snapshotPaths(t, root)
	if before != after {
		t.Fatalf("dry-run changed the tree:\nbefore: %v\nafter:  %v", before, after)
	}
	if !strings.Contains(stdout.String(), "no files were written") {
		t.Fatalf("dry-run must state that nothing was written: %s", stdout.String())
	}
}

func snapshotPaths(t *testing.T, root string) string {
	t.Helper()
	var b strings.Builder
	err := filepath.WalkDir(root, func(path string, entry os.DirEntry, walkErr error) error {
		if walkErr != nil {
			return walkErr
		}
		relative, err := filepath.Rel(root, path)
		if err != nil {
			return err
		}
		b.WriteString(filepath.ToSlash(relative))
		b.WriteByte('\n')
		return nil
	})
	if err != nil {
		t.Fatal(err)
	}
	return b.String()
}

func TestStrictRecoveryRefusesUnclassifiedEvidence(t *testing.T) {
	root := writeUnityProjectFixture(t)
	writeCompletedTransactions(t, root)
	var stdout, stderr bytes.Buffer
	code := run([]string{"--project", root, "--dry-run", "--ci", "--strict-recovery"}, &stdout, &stderr)
	if code == 0 {
		t.Fatal("strict recovery must refuse an unclassified transaction envelope")
	}
	transactions := filepath.Join(root, filepath.FromSlash(projectlayout.Default().TransactionsDir))
	_, err := recovery.EnsureReady(root, transactions, true)
	if err == nil || !strings.Contains(err.Error(), "unclassified") {
		t.Fatalf("the refusal must explain what was unclassified: %v", err)
	}
}

func TestRecoveryClassifierHandlesRealShape(t *testing.T) {
	root := writeUnityProjectFixture(t)
	writeCompletedTransactions(t, root)
	transactions := filepath.Join(root, filepath.FromSlash(projectlayout.Default().TransactionsDir))
	report, err := recovery.EnsureReady(root, transactions, false)
	if err != nil {
		t.Fatalf("completed state must be ready: %v", err)
	}
	if report.Blocking(false) || len(report.Unknown) == 0 {
		t.Fatalf("unexpected report: %+v", report)
	}
}

func TestLayoutOverrideChangesCacheRoots(t *testing.T) {
	root := writeUnityProjectFixture(t)
	layoutPath := filepath.Join(root, "custom-layout.json")
	document := `{"cacheRoots":["Intermediates"]}`
	if err := os.WriteFile(layoutPath, []byte(document), 0o644); err != nil {
		t.Fatal(err)
	}
	if err := os.MkdirAll(filepath.Join(root, "Intermediates", "nested"), 0o755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(filepath.Join(root, "Intermediates", "nested", "cache.bin"), []byte("x"), 0o644); err != nil {
		t.Fatal(err)
	}
	if err := os.MkdirAll(filepath.Join(root, "Library"), 0o755); err != nil {
		t.Fatal(err)
	}
	var stdout, stderr bytes.Buffer
	code := run([]string{"--project", root, "--layout", layoutPath, "--ci"}, &stdout, &stderr)
	if code != 0 {
		t.Fatalf("exit = %d, stderr:\n%s", code, stderr.String())
	}
	if _, err := os.Lstat(filepath.Join(root, "Intermediates")); !os.IsNotExist(err) {
		t.Fatal("configured cache root was not removed")
	}
	if _, err := os.Lstat(filepath.Join(root, "Library")); err != nil {
		t.Fatal("a cache root outside the configured layout must survive")
	}
}

func TestOwnerMarkerKindUsesLayout(t *testing.T) {
	configure(runConfig{layout: projectlayout.Default(), maxEntries: 1000, maxDepth: 16,
		policy: safefs.HostPathPolicy(), facts: safefs.Fallback()})
	names := projectlayout.Default().OwnerMarkerNames
	if len(names) < 2 {
		t.Fatal("layout must declare two owner marker names")
	}
	if ownerMarkerKind(names[0]) != "general" || ownerMarkerKind(names[1]) != "yoo" {
		t.Fatal("owner marker kinds must follow the layout order")
	}
	if isOwnerMarkerName("unrelated.json") {
		t.Fatal("an unrelated file must not be an owner marker")
	}
	custom := projectlayout.Default()
	custom.OwnerMarkerNames = []string{"owner-a.json", "owner-b.json"}
	configure(runConfig{layout: custom, maxEntries: 1000, maxDepth: 16,
		policy: safefs.HostPathPolicy(), facts: safefs.Fallback()})
	if !isOwnerMarkerName("owner-a.json") || isOwnerMarkerName(names[0]) {
		t.Fatal("a custom layout must fully replace marker names")
	}
}
