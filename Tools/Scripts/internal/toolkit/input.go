package toolkit

import (
	"bufio"
	"fmt"
	"io"
	"os"
	"strings"
	"sync"
)

var (
	inputMu     sync.Mutex
	sharedInput = bufio.NewReader(os.Stdin)
)

// Input returns the process-wide line reader. The interactive menu and every
// command read from this single buffered reader, so a line prefetched while
// selecting a command remains available to the command that follows.
func Input() *bufio.Reader {
	inputMu.Lock()
	defer inputMu.Unlock()
	return sharedInput
}

// SetInput installs the reader used by the whole process. Call it once during
// startup (or in a test) before any command reads input.
func SetInput(reader io.Reader) {
	inputMu.Lock()
	defer inputMu.Unlock()
	if reader == nil {
		sharedInput = bufio.NewReader(os.Stdin)
		return
	}
	sharedInput = bufio.NewReader(reader)
}

// ReadLine prints a prompt and reads one line. A closed stream returns io.EOF
// so callers can cancel instead of silently continuing with default values.
func ReadLine(prompt string) (string, error) {
	if prompt != "" {
		fmt.Print(prompt)
	}
	line, err := Input().ReadString('\n')
	trimmed := strings.TrimSpace(line)
	if err != nil {
		if trimmed != "" && err == io.EOF {
			return trimmed, nil
		}
		return "", err
	}
	return trimmed, nil
}

// Confirm asks for a typed word and accepts it case-insensitively. A closed
// stream, EOF or any other word cancels the operation.
func Confirm(prompt, expected string) bool {
	value, err := ReadLine(prompt)
	if err != nil {
		return false
	}
	return strings.EqualFold(value, expected)
}
