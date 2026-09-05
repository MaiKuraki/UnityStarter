package logging

import (
	"bytes"
	"os"
	"strings"
	"testing"
)

// The interactive menu runs commands repeatedly inside one process, so Command
// must rebind from the unscoped logger instead of stacking another "cmd"
// attribute on every call.
func TestCommandDoesNotAccumulateAttributes(t *testing.T) {
	var buffer bytes.Buffer
	SetOutput(&buffer)
	defer SetOutput(os.Stderr)

	Command("demo_tool")
	Info("first run")
	Command("demo_tool")
	Info("second run")

	lines := strings.Split(strings.TrimSpace(buffer.String()), "\n")
	if len(lines) != 2 {
		t.Fatalf("expected 2 log lines, got %d: %q", len(lines), buffer.String())
	}
	for index, line := range lines {
		if count := strings.Count(line, "cmd="); count != 1 {
			t.Errorf("line %d carries %d cmd attributes, want exactly 1: %s", index, count, line)
		}
	}
}
