package recovery

import (
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func TestClassifyRecord(t *testing.T) {
	cases := []struct {
		document string
		want     State
	}{
		{`{"phase":"Published"}`, StateTerminal},
		{`{"phase":"Adopted"}`, StateTerminal},
		{`{"state":"Completed"}`, StateTerminal},
		{`{"status":"rolled_back"}`, StateTerminal},
		{`{"phase":"Applying"}`, StateActive},
		{`{"state":"in_progress"}`, StateActive},
		{`{"status":"Claimed"}`, StateActive},
		{`{"documentType":"build-asset-envelope","payloadBase64":"abc"}`, StateUnknown},
		{`{"phase":"SomeFuturePhase"}`, StateUnknown},
		{`not json`, StateUnknown},
		{``, StateUnknown},
	}
	for _, item := range cases {
		if got := ClassifyRecord([]byte(item.document)); got != item.want {
			t.Fatalf("ClassifyRecord(%s) = %v, want %v", item.document, got, item.want)
		}
	}
}

func writeFile(t *testing.T, path, content string) {
	t.Helper()
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(path, []byte(content), 0o644); err != nil {
		t.Fatal(err)
	}
}

// The on-disk shape of a completed build pipeline must never block a tool.
func TestCompletedTransactionShapeIsNormalState(t *testing.T) {
	root := t.TempDir()
	transactions := filepath.Join(root, ".buildpipeline", "transactions")
	writeFile(t, filepath.Join(transactions, "gameplay-tags", "active.json"), `{"phase":"Published","effects":[]}`)
	writeFile(t, filepath.Join(transactions, "gameplay-tags", "build.lock"), "process=1\n")
	writeFile(t, filepath.Join(transactions, "gameplay-tags", "scratch", "note.txt"), "audit\n")
	writeFile(t, filepath.Join(transactions, "performance-testing", "active.json"), `{"documentType":"build-asset-envelope","payloadBase64":"abc"}`)
	writeFile(t, filepath.Join(transactions, "performance-testing", "transaction-abc", "transaction.owner"), "abc\n")
	writeFile(t, filepath.Join(transactions, "player", "active.lock"), "")

	report, err := EnsureReady(root, transactions, false)
	if err != nil {
		t.Fatalf("completed transaction state must not block: %v", err)
	}
	if report.Scanned == 0 || report.Blocking(false) {
		t.Fatalf("unexpected report: %+v", report)
	}
	if len(report.Unknown) == 0 {
		t.Fatal("unclassified envelopes must be reported for diagnostics")
	}
	if _, err := EnsureReady(root, transactions, true); err == nil {
		t.Fatal("--strict-recovery must refuse unclassified evidence")
	}
}

func TestActivePhaseBlocksBothModes(t *testing.T) {
	root := t.TempDir()
	transactions := filepath.Join(root, "transactions")
	writeFile(t, filepath.Join(transactions, "participant", "active.json"), `{"phase":"Applying"}`)
	for _, strict := range []bool{false, true} {
		_, err := EnsureReady(root, transactions, strict)
		if err == nil || !strings.Contains(err.Error(), "active.json") {
			t.Fatalf("an active phase must block (strict=%v): %v", strict, err)
		}
	}
}

func TestHeldLockBlocks(t *testing.T) {
	root := t.TempDir()
	transactions := filepath.Join(root, "transactions")
	lockPath := filepath.Join(transactions, "participant", "build.lock")
	writeFile(t, lockPath, "process=1\n")

	held, err := HeldLock(lockPath)
	if err != nil || held {
		t.Fatalf("an idle lock must not be reported as held: %v %v", held, err)
	}
	release := holdLock(t, lockPath)
	defer release()
	held, err = HeldLock(lockPath)
	if err != nil {
		t.Fatal(err)
	}
	if !held {
		t.Skip("this host does not enforce byte-range lock conflicts")
	}
	_, err = EnsureReady(root, transactions, false)
	if err == nil || !strings.Contains(err.Error(), "build.lock") {
		t.Fatalf("a lock held by a live process must block: %v", err)
	}
}

func TestMissingTransactionRootIsReady(t *testing.T) {
	root := t.TempDir()
	report, err := EnsureReady(root, filepath.Join(root, "missing"), false)
	if err != nil || report.Blocking(false) {
		t.Fatalf("a missing transaction root must be ready: %+v %v", report, err)
	}
}

func TestInspectRejectsRedirectedEvidence(t *testing.T) {
	root := t.TempDir()
	transactions := filepath.Join(root, "transactions")
	if err := os.MkdirAll(transactions, 0o755); err != nil {
		t.Fatal(err)
	}
	outside := filepath.Join(root, "outside")
	if err := os.MkdirAll(outside, 0o755); err != nil {
		t.Fatal(err)
	}
	link := filepath.Join(transactions, "escape")
	if err := os.Symlink(outside, link); err != nil {
		t.Skipf("symlinks are unavailable: %v", err)
	}
	if _, err := Inspect(root, transactions); err == nil {
		t.Fatal("redirected transaction evidence must be rejected")
	}
}
