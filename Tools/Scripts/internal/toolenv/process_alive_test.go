package toolenv

import (
	"os"
	"testing"
)

func TestProcessAliveCurrentProcess(t *testing.T) {
	alive, err := ProcessAlive(os.Getpid())
	if err != nil {
		t.Fatalf("liveness probe errored for the current process: %v", err)
	}
	if !alive {
		t.Fatal("the current process must be reported alive")
	}
}

func TestProcessAliveRejectsInvalidPID(t *testing.T) {
	alive, err := ProcessAlive(0)
	if err == nil || alive {
		t.Fatalf("pid 0 must fail closed: alive=%v err=%v", alive, err)
	}
	alive, err = ProcessAlive(-1)
	if err == nil || alive {
		t.Fatalf("a negative pid must fail closed: alive=%v err=%v", alive, err)
	}
}

// ProcessAlive must not depend on PATH, so a trimmed environment changes nothing.
func TestProcessAliveIndependentOfPath(t *testing.T) {
	original := os.Getenv("PATH")
	t.Cleanup(func() { _ = os.Setenv("PATH", original) })
	if err := os.Setenv("PATH", ""); err != nil {
		t.Fatal(err)
	}
	alive, err := ProcessAlive(os.Getpid())
	if err != nil || !alive {
		t.Fatalf("a kernel liveness probe must not consult PATH: alive=%v err=%v", alive, err)
	}
}
