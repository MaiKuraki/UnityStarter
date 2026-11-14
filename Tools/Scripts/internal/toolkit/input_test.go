package toolkit

import (
	"bytes"
	"os"
	"strings"
	"testing"
)

func restoreInput(t *testing.T) {
	t.Helper()
	t.Cleanup(func() { SetInput(os.Stdin) })
}

func TestSharedInputKeepsPrefetchedLines(t *testing.T) {
	restoreInput(t)
	SetInput(strings.NewReader("3\nCLEAN\nq\n"))

	choice, err := ReadLine("")
	if err != nil || choice != "3" {
		t.Fatalf("first read = %q, %v; want 3", choice, err)
	}
	if !Confirm("", "CLEAN") {
		t.Fatal("the command must still be able to read the prefetched CLEAN line")
	}
	last, err := ReadLine("")
	if err != nil || last != "q" {
		t.Fatalf("third read = %q, %v; want q", last, err)
	}
}

func TestConfirmOnClosedStreamCancels(t *testing.T) {
	restoreInput(t)
	SetInput(strings.NewReader(""))
	if Confirm("", "CLEAN") {
		t.Fatal("a closed stream must cancel a confirmation")
	}
	SetInput(strings.NewReader("cancel\n"))
	if Confirm("", "CLEAN") {
		t.Fatal("a different word must cancel a confirmation")
	}
	SetInput(strings.NewReader("clean\n"))
	if !Confirm("", "CLEAN") {
		t.Fatal("confirmation must be case-insensitive")
	}
}

func TestInteractiveMenuSharesInputWithCommand(t *testing.T) {
	restoreInput(t)
	confirmed := false
	commands := []Command{
		{Name: "clean", Summary: "Clean caches.", Run: func([]string) int {
			confirmed = Confirm("", "CLEAN")
			if confirmed {
				return ExitSuccess
			}
			return ExitFailure
		}},
	}
	var output bytes.Buffer
	code := InteractiveMenu("test-tools", commands,
		strings.NewReader("1\nCLEAN\nq\n"), &output)
	if code != ExitSuccess {
		t.Fatalf("menu exit = %d, output:\n%s", code, output.String())
	}
	if !confirmed {
		t.Fatalf("the command did not receive CLEAN; output:\n%s", output.String())
	}
}
