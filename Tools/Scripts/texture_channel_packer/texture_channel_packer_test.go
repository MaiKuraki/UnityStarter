package texture_channel_packer

import (
	"bufio"
	"bytes"
	"image"
	"image/color"
	"image/png"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"cyclonegames.tools/scripts/internal/toolkit"
)

// writeTestPNG encodes a small solid-color PNG so tests never depend on
// committed binary fixtures.
func writeTestPNG(t *testing.T, directory string) string {
	t.Helper()
	image := image.NewRGBA(image.Rect(0, 0, 2, 2))
	for y := 0; y < 2; y++ {
		for x := 0; x < 2; x++ {
			image.Set(x, y, color.RGBA{R: 0x80, G: 0x40, B: 0x20, A: 0xFF})
		}
	}
	var buffer bytes.Buffer
	if err := png.Encode(&buffer, image); err != nil {
		t.Fatalf("png.Encode failed: %v", err)
	}
	path := filepath.Join(directory, "input.png")
	if err := os.WriteFile(path, buffer.Bytes(), 0o644); err != nil {
		t.Fatalf("cannot write test PNG: %v", err)
	}
	return path
}

// TestRunReportsFailureWhenOutputCannotBeWritten guards the exit-code
// regression: a failing pack (here an unwritable output directory) must return
// ExitFailure instead of a silent success.
func TestRunReportsFailureWhenOutputCannotBeWritten(t *testing.T) {
	inputPNG := writeTestPNG(t, t.TempDir())
	// A missing parent directory makes os.Create fail inside executePack.
	unwritableOutput := filepath.Join(t.TempDir(), "missing-directory", "packed.png")
	code := Run([]string{
		"-r", inputPNG,
		"-o", unwritableOutput,
		"-ci",
	})
	if code != toolkit.ExitFailure {
		t.Fatalf("exit code = %d, want %d when the output file cannot be created", code, toolkit.ExitFailure)
	}
}

func TestRunRejectsMissingInput(t *testing.T) {
	code := Run([]string{"-r", filepath.Join(t.TempDir(), "absent.png"), "-ci"})
	if code != toolkit.ExitFailure {
		t.Fatalf("exit code = %d, want %d for a missing input file", code, toolkit.ExitFailure)
	}
}

func TestRunDryRunSucceeds(t *testing.T) {
	inputPNG := writeTestPNG(t, t.TempDir())
	output := filepath.Join(t.TempDir(), "packed.png")
	code := Run([]string{"-r", inputPNG, "-o", output, "-ci", "-dry-run"})
	if code != toolkit.ExitSuccess {
		t.Fatalf("exit code = %d, want %d for a successful dry run", code, toolkit.ExitSuccess)
	}
	if _, err := os.Stat(output); !os.IsNotExist(err) {
		t.Fatalf("dry run must not write the output file, stat error = %v", err)
	}
}

func TestParseSourceSpecFillValues(t *testing.T) {
	if source := parseSourceSpec("128", 0, "R"); source.FilePath != "" || source.Fill != 128 {
		t.Fatalf("parseSourceSpec(128) = %+v, want fill 128", source)
	}
	if source := parseSourceSpec("fill:255", 0, "R"); source.FilePath != "" || source.Fill != 255 {
		t.Fatalf("parseSourceSpec(fill:255) = %+v, want fill 255", source)
	}
	if source := parseSourceSpec("", 64, "R"); source.FilePath != "" || source.Fill != 64 {
		t.Fatalf("parseSourceSpec(\"\") = %+v, want default fill 64", source)
	}
}

// The flag names a channel, so "-r img.png" must mean "use the red channel";
// an explicit :Gray suffix still opts out of that default.
func TestParseSourceSpecDefaultsToFlagChannel(t *testing.T) {
	if source := parseSourceSpec("img.png", 0, "R"); source.Channel != "R" || source.FilePath == "" {
		t.Fatalf("parseSourceSpec(img.png, R) = %+v, want the R channel", source)
	}
	if source := parseSourceSpec("img.png:Gray", 0, "R"); source.Channel != "Gray" {
		t.Fatalf("parseSourceSpec(img.png:Gray, R) = %+v, want an explicit Gray override", source)
	}
	if source := parseSourceSpec("img.png:alpha", 0, "R"); source.Channel != "A" {
		t.Fatalf("parseSourceSpec(img.png:alpha, R) = %+v, want the A channel", source)
	}
	if source := parseSourceSpec("img.png", 0, "A"); source.Channel != "A" {
		t.Fatalf("parseSourceSpec(img.png, A) = %+v, want the A channel", source)
	}
}

// writeChannelPNG encodes a 2x2 NRGBA PNG whose pixel values come from the
// given generator, so channel round-trips can assert exact bytes.
func writeChannelPNG(t *testing.T, directory, name string, pixel func(x, y int) color.NRGBA) string {
	t.Helper()
	img := image.NewNRGBA(image.Rect(0, 0, 2, 2))
	for y := 0; y < 2; y++ {
		for x := 0; x < 2; x++ {
			img.SetNRGBA(x, y, pixel(x, y))
		}
	}
	var buffer bytes.Buffer
	if err := png.Encode(&buffer, img); err != nil {
		t.Fatalf("png.Encode failed: %v", err)
	}
	path := filepath.Join(directory, name)
	if err := os.WriteFile(path, buffer.Bytes(), 0o644); err != nil {
		t.Fatalf("cannot write test PNG: %v", err)
	}
	return path
}

// The pack must reproduce each source channel exactly: pack, then assert every
// output pixel against the values the four sources carried.
func TestPackChannelsRoundTrip(t *testing.T) {
	dir := t.TempDir()
	red := writeChannelPNG(t, dir, "red.png", func(x, y int) color.NRGBA { return color.NRGBA{R: uint8(10 + 2*x), G: 0, B: 0, A: 255} })
	green := writeChannelPNG(t, dir, "green.png", func(x, y int) color.NRGBA { return color.NRGBA{R: 0, G: uint8(20 + 2*y), B: 0, A: 255} })
	blue := writeChannelPNG(t, dir, "blue.png", func(x, y int) color.NRGBA { return color.NRGBA{R: 0, G: 0, B: 30, A: 255} })
	alpha := writeChannelPNG(t, dir, "alpha.png", func(x, y int) color.NRGBA { return color.NRGBA{R: 0, G: 0, B: 0, A: uint8(40 + x)} })

	sources := [4]channelSource{
		{FilePath: red, Channel: "R"},
		{FilePath: green, Channel: "G"},
		{FilePath: blue, Channel: "B"},
		{FilePath: alpha, Channel: "A"},
	}
	out, _, err := packChannels(2, 2, sources)
	if err != nil {
		t.Fatalf("packChannels failed: %v", err)
	}
	for y := 0; y < 2; y++ {
		for x := 0; x < 2; x++ {
			i := out.PixOffset(x, y)
			want := color.NRGBA{R: uint8(10 + 2*x), G: uint8(20 + 2*y), B: 30, A: uint8(40 + x)}
			got := color.NRGBA{R: out.Pix[i], G: out.Pix[i+1], B: out.Pix[i+2], A: out.Pix[i+3]}
			if got != want {
				t.Fatalf("pixel (%d,%d) = %+v, want %+v", x, y, got, want)
			}
		}
	}
}

// 16-bit sources are silently reduced to 8 bits by the NRGBA conversion; the
// tool must say so instead of losing precision unnoticed.
func TestLoadImageWarnsOn16BitSource(t *testing.T) {
	dir := t.TempDir()
	img := image.NewNRGBA64(image.Rect(0, 0, 1, 1))
	img.SetNRGBA64(0, 0, color.NRGBA64{R: 0xFFFF, G: 0x8000, B: 0x4000, A: 0xFFFF})
	var buffer bytes.Buffer
	if err := png.Encode(&buffer, img); err != nil {
		t.Fatalf("png.Encode failed: %v", err)
	}
	path := filepath.Join(dir, "high-depth.png")
	if err := os.WriteFile(path, buffer.Bytes(), 0o644); err != nil {
		t.Fatalf("cannot write test PNG: %v", err)
	}
	loaded, warnings, err := loadImageAsNRGBA(path)
	if err != nil {
		t.Fatalf("loadImageAsNRGBA failed: %v", err)
	}
	if len(warnings) == 0 {
		t.Fatalf("16-bit input must produce a precision warning")
	}
	if loaded.Pix[0] != 0xFF {
		t.Fatalf("reduced channel = 0x%02X, want 0xFF", loaded.Pix[0])
	}
}

func TestRunInteractiveEOFCancelsImmediately(t *testing.T) {
	if code := runInteractive(bufio.NewReader(strings.NewReader(""))); code != toolkit.ExitFailure {
		t.Fatalf("exit code = %d, want %d when the input stream is closed", code, toolkit.ExitFailure)
	}
}

// A partially fed stream (piped script, Ctrl+Z) used to run to completion on
// defaults and could write a file without consent. It must cancel instead.
func TestRunInteractivePartialEOFDoesNotWriteOutput(t *testing.T) {
	workDir := t.TempDir()
	oldWd, err := os.Getwd()
	if err != nil {
		t.Fatalf("cannot read working directory: %v", err)
	}
	if err := os.Chdir(workDir); err != nil {
		t.Fatalf("cannot enter temporary working directory: %v", err)
	}
	defer func() { _ = os.Chdir(oldWd) }()

	red := writeTestPNG(t, workDir)
	// Mode, one channel source, its channel answer — then the stream closes
	// before the remaining prompts.
	input := "1\n" + red + "\nR\n"
	if code := runInteractive(bufio.NewReader(strings.NewReader(input))); code != toolkit.ExitFailure {
		t.Fatalf("exit code = %d, want %d when the stream closes mid-flow", code, toolkit.ExitFailure)
	}
	if _, statErr := os.Stat("packed.png"); !os.IsNotExist(statErr) {
		t.Fatalf("no output may be written when the stream closes mid-flow, stat error = %v", statErr)
	}
}

// The output must be published by rename: a pre-existing file is replaced by a
// valid PNG and no temporary file survives the run.
func TestExecutePackReplacesStaleOutputAtomically(t *testing.T) {
	dir := t.TempDir()
	inputPNG := writeTestPNG(t, dir)
	output := filepath.Join(dir, "packed.png")
	if err := os.WriteFile(output, []byte("stale garbage"), 0o644); err != nil {
		t.Fatalf("cannot seed stale output: %v", err)
	}
	sources := [4]channelSource{{FilePath: inputPNG, Channel: "R"}}
	if err := executePack(sources, 2, 2, output); err != nil {
		t.Fatalf("executePack failed: %v", err)
	}
	data, err := os.ReadFile(output)
	if err != nil {
		t.Fatalf("cannot read published output: %v", err)
	}
	if _, err := png.Decode(bytes.NewReader(data)); err != nil {
		t.Fatalf("published output is not a valid PNG: %v", err)
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
