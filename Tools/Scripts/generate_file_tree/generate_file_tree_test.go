package generate_file_tree

import (
	"bufio"
	"bytes"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"cyclonegames.tools/scripts/internal/toolkit"
)

// The tree is staged and published atomically: a stale output is replaced and
// no temporary file survives the run.
func TestWriteOutputPublishesAtomically(t *testing.T) {
	dir := t.TempDir()
	output := filepath.Join(dir, "tree.md")
	if err := os.WriteFile(output, []byte("stale"), 0o644); err != nil {
		t.Fatalf("cannot seed stale output: %v", err)
	}
	if err := writeOutput("fresh content", output); err != nil {
		t.Fatalf("writeOutput failed: %v", err)
	}
	data, err := os.ReadFile(output)
	if err != nil {
		t.Fatalf("cannot read published output: %v", err)
	}
	if string(data) != "fresh content" {
		t.Fatalf("published output = %q, want the fresh content", data)
	}
	entries, err := os.ReadDir(dir)
	if err != nil {
		t.Fatalf("cannot list output directory: %v", err)
	}
	for _, entry := range entries {
		if strings.Contains(entry.Name(), ".partial-") {
			t.Fatalf("temporary file leaked: %s", entry.Name())
		}
	}
}

func TestWriteOutputRejectsMissingParent(t *testing.T) {
	output := filepath.Join(t.TempDir(), "missing-dir", "tree.md")
	if err := writeOutput("content", output); err == nil {
		t.Fatalf("writeOutput must fail when the parent directory is missing")
	}
}

func TestRunRejectsBadOutputPath(t *testing.T) {
	output := filepath.Join(t.TempDir(), "missing-dir", "tree.md")
	code := Run([]string{"-ci", "-target", t.TempDir(), "-o", output})
	if code != toolkit.ExitFailure {
		t.Fatalf("exit code = %d, want %d when the output cannot be created", code, toolkit.ExitFailure)
	}
}

func TestRunInteractiveEOFCancelsWithoutWriting(t *testing.T) {
	workDir := t.TempDir()
	oldWd, err := os.Getwd()
	if err != nil {
		t.Fatalf("cannot read working directory: %v", err)
	}
	if err := os.Chdir(workDir); err != nil {
		t.Fatalf("cannot enter temporary working directory: %v", err)
	}
	defer func() { _ = os.Chdir(oldWd) }()

	code := runInteractive(bufio.NewReader(strings.NewReader("")))
	if code != toolkit.ExitFailure {
		t.Fatalf("exit code = %d, want %d on closed input", code, toolkit.ExitFailure)
	}
	if _, statErr := os.Stat("directory_structure.md"); !os.IsNotExist(statErr) {
		t.Fatalf("no output may be written on EOF, stat error = %v", statErr)
	}
}

// A partially fed stream must cancel before any file is written.
func TestRunInteractivePartialEOFDoesNotWrite(t *testing.T) {
	workDir := t.TempDir()
	oldWd, err := os.Getwd()
	if err != nil {
		t.Fatalf("cannot read working directory: %v", err)
	}
	if err := os.Chdir(workDir); err != nil {
		t.Fatalf("cannot enter temporary working directory: %v", err)
	}
	defer func() { _ = os.Chdir(oldWd) }()

	code := runInteractive(bufio.NewReader(strings.NewReader("1\n")))
	if code != toolkit.ExitFailure {
		t.Fatalf("exit code = %d, want %d when the stream closes mid-flow", code, toolkit.ExitFailure)
	}
	if _, statErr := os.Stat("directory_structure.md"); !os.IsNotExist(statErr) {
		t.Fatalf("no output may be written when the stream closes mid-flow, stat error = %v", statErr)
	}
}

func TestNoDefaultIgnoreClearsBuiltins(t *testing.T) {
	withDefaults := buildConfig(profiles[1], t.TempDir(), "tree.md", -1, false, false, false, true, false, "", "")
	if !withDefaults.ignoreDirs[".git"] {
		t.Fatalf("built-in ignore list must be applied by default")
	}
	cleared := buildConfig(profiles[1], t.TempDir(), "tree.md", -1, false, false, false, true, true, "", "")
	if cleared.ignoreDirs[".git"] || cleared.ignoreExts[".tmp"] || cleared.ignoreNames["Thumbs.db"] {
		t.Fatalf("--no-default-ignore must clear every built-in ignore entry")
	}
}

// The CLI flag must reach config building so a normally ignored directory is
// included when requested.
func TestRunNoDefaultIgnoreIncludesBuiltinIgnoredDirs(t *testing.T) {
	target := t.TempDir()
	if err := os.Mkdir(filepath.Join(target, ".git"), 0o755); err != nil {
		t.Fatalf("cannot create fixture directory: %v", err)
	}
	if err := os.WriteFile(filepath.Join(target, ".git", "config"), []byte("x"), 0o644); err != nil {
		t.Fatalf("cannot create fixture file: %v", err)
	}
	output := filepath.Join(t.TempDir(), "tree.md")
	code := Run([]string{"-ci", "-target", target, "-profile", "full", "-no-default-ignore", "-o", output})
	if code != toolkit.ExitSuccess {
		t.Fatalf("exit code = %d, want success", code)
	}
	data, err := os.ReadFile(output)
	if err != nil {
		t.Fatalf("cannot read generated tree: %v", err)
	}
	if !strings.Contains(string(data), ".git") {
		t.Fatalf("generated tree must include the built-in ignored directory, got:\n%s", data)
	}
}

// A read failure must be collected instead of silently producing a partial tree
// that looks complete.
func TestTraverseDirReportsReadErrors(t *testing.T) {
	var buf bytes.Buffer
	var st stats
	cfg := buildConfig(profiles[3], t.TempDir(), "tree.md", -1, false, false, false, true, true, "", "")
	traverseDir(cfg, &buf, &st, filepath.Join(t.TempDir(), "no-such-dir"), "", 0)
	if len(st.readErrors) == 0 {
		t.Fatalf("traverseDir must report an unreadable directory")
	}
}
