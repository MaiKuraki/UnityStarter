// Package recovery distinguishes durable build records from work that is still
// in progress. A build pipeline keeps completed transaction journals, scratch
// folders and lock metadata on disk; treating those as pending recovery makes a
// destructive tool permanently unusable. Only an active state marker or a lock
// currently held by a live process blocks a mutation.
package recovery

import (
	"bytes"
	"encoding/json"
	"errors"
	"fmt"
	"io/fs"
	"os"
	"path/filepath"
	"sort"
	"strings"

	"cyclonegames.tools/scripts/internal/safefs"
)

const maximumEvidenceEntries = 500000

// State classifies one transaction record.
type State int

const (
	StateTerminal State = iota
	StateActive
	StateUnknown
)

var activeStates = map[string]bool{
	"pending": true, "claimed": true, "applying": true,
	"inprogress": true, "in_progress": true, "started": true,
	"acquired": true, "staged": true, "prepared": true,
	"rollingback": true, "roll_back": true, "recovering": true,
}

var terminalStates = map[string]bool{
	"published": true, "adopted": true, "completed": true, "complete": true,
	"committed": true, "applied": true, "done": true, "finished": true,
	"rolledback": true, "rolled_back": true, "released": true,
	"cancelled": true, "canceled": true, "failed": true, "aborted": true,
}

// ClassifyRecord reads phase/state/status from a transaction document. Records
// without a recognizable state are classified as unknown: they may be audit
// envelopes owned by another subsystem, so they must not block a tool forever.
func ClassifyRecord(data []byte) State {
	if len(data) == 0 || len(data) > 4*1024*1024 {
		return StateUnknown
	}
	var document map[string]json.RawMessage
	decoder := json.NewDecoder(bytes.NewReader(data))
	if err := decoder.Decode(&document); err != nil {
		return StateUnknown
	}
	for _, key := range []string{"phase", "state", "status"} {
		raw, ok := document[key]
		if !ok {
			continue
		}
		var value string
		if err := json.Unmarshal(raw, &value); err != nil {
			continue
		}
		normalized := strings.ToLower(strings.NewReplacer("-", "", "_", "", " ", "").Replace(strings.TrimSpace(value)))
		if terminalStates[normalized] {
			return StateTerminal
		}
		if activeStates[normalized] {
			return StateActive
		}
		return StateUnknown
	}
	return StateUnknown
}

// Report is the reconciled view of a transaction tree.
type Report struct {
	Active  []string
	Unknown []string
	Scanned int
}

func (r Report) Blocking(strict bool) bool {
	return len(r.Active) != 0 || (strict && len(r.Unknown) != 0)
}

func (r Report) Describe(strict bool) string {
	var b strings.Builder
	if len(r.Active) != 0 {
		b.WriteString("active build transaction evidence: ")
		b.WriteString(strings.Join(r.Active, ", "))
	}
	if strict && len(r.Unknown) != 0 {
		if b.Len() != 0 {
			b.WriteString("; ")
		}
		b.WriteString("unclassified evidence under --strict-recovery: ")
		b.WriteString(strings.Join(r.Unknown, ", "))
	}
	return b.String()
}

// Inspect walks a transaction directory and reports active or unclassified
// evidence. Completed journals, scratch folders and idle lock files are normal
// durable state and never block a mutation.
func Inspect(projectRoot, transactionsDir string) (Report, error) {
	report := Report{}
	info, err := os.Lstat(transactionsDir)
	if errors.Is(err, os.ErrNotExist) {
		return report, nil
	}
	if err != nil {
		return report, err
	}
	if !info.IsDir() || info.Mode()&os.ModeSymlink != 0 {
		return report, fmt.Errorf("transaction root is not a directory: %s", transactionsDir)
	}
	facts := safefs.FactsFor(projectRoot)
	if !safefs.IsWithin(projectRoot, transactionsDir, facts) {
		return report, fmt.Errorf("transaction root escapes the project: %s", transactionsDir)
	}

	err = filepath.WalkDir(transactionsDir, func(path string, entry fs.DirEntry, walkErr error) error {
		if walkErr != nil {
			return walkErr
		}
		report.Scanned++
		if report.Scanned > maximumEvidenceEntries {
			return fmt.Errorf("transaction evidence exceeds %d entries", maximumEvidenceEntries)
		}
		// DirEntry carries type bits from the directory read, so the common
		// file case needs no extra stat. Mount identity and full reparse
		// inspection are performed once per directory instead of per file.
		if entry.Type()&fs.ModeSymlink != 0 {
			return fmt.Errorf("transaction evidence is redirected: %s", path)
		}
		if entry.IsDir() || entry.Type()&fs.ModeIrregular != 0 {
			if redirected, err := isRedirected(path); err != nil {
				return err
			} else if redirected {
				return fmt.Errorf("transaction evidence is redirected: %s", path)
			}
			if err := safefs.ValidateMountBoundary(projectRoot, path); err != nil {
				return err
			}
		}
		if entry.IsDir() {
			return nil
		}
		relative, err := filepath.Rel(transactionsDir, path)
		if err != nil {
			return err
		}
		name := strings.ToLower(entry.Name())
		if strings.HasSuffix(name, ".lock") {
			held, err := HeldLock(path)
			if err != nil {
				return fmt.Errorf("cannot determine lock ownership for %s: %w", path, err)
			}
			if held {
				report.Active = append(report.Active, filepath.ToSlash(relative))
			}
			return nil
		}
		if !strings.HasSuffix(name, ".json") {
			report.Unknown = append(report.Unknown, filepath.ToSlash(relative))
			return nil
		}
		data, err := os.ReadFile(path)
		if err != nil {
			return err
		}
		switch ClassifyRecord(data) {
		case StateActive:
			report.Active = append(report.Active, filepath.ToSlash(relative))
		case StateUnknown:
			report.Unknown = append(report.Unknown, filepath.ToSlash(relative))
		}
		return nil
	})
	if err != nil {
		return Report{}, err
	}
	sort.Strings(report.Active)
	sort.Strings(report.Unknown)
	return report, nil
}

// EnsureReady fails closed only when there is active evidence. Unknown records
// are reported to the caller for an advisory log.
func EnsureReady(projectRoot, transactionsDir string, strict bool) (Report, error) {
	report, err := Inspect(projectRoot, transactionsDir)
	if err != nil {
		return report, err
	}
	if report.Blocking(strict) {
		return report, fmt.Errorf("build recovery is required: %s", report.Describe(strict))
	}
	return report, nil
}
