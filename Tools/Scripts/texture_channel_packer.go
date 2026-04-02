// Texture Channel Packer — Pack multiple images into RGBA channels of a single texture.
// Common in Unity game development for creating HDRP/URP Mask Maps, packed textures, etc.
//
// Build: go build texture_channel_packer.go
//
// Supports PNG and JPEG input. Output is always PNG (lossless, alpha-preserving).
// Memory-efficient: loads one source at a time, processes channels sequentially.

package main

import (
	"bufio"
	"flag"
	"fmt"
	"image"
	"image/draw"
	_ "image/jpeg"
	"image/png"
	"os"
	"path/filepath"
	"runtime"
	"strconv"
	"strings"
	"time"
)

// ============================================================
// Configuration
// ============================================================

var channelLabels = [4]string{"Red", "Green", "Blue", "Alpha"}
var channelLetters = [4]string{"R", "G", "B", "A"}
var defaultFills = [4]uint8{0, 0, 0, 255}

type preset struct {
	name        string
	description string
	labels      [4]string
}

var presets = []preset{
	{
		name:        "hdrp-mask",
		description: "HDRP Mask Map",
		labels:      [4]string{"Metallic", "Ambient Occlusion", "Detail Mask", "Smoothness"},
	},
	{
		name:        "urp-mask",
		description: "URP Mask Map",
		labels:      [4]string{"Metallic", "Occlusion", "Detail", "Smoothness"},
	},
}

// channelSource describes where to get one output channel's data
type channelSource struct {
	FilePath string // "" means use Fill value
	Channel  string // "R", "G", "B", "A", "Gray"
	Fill     uint8
}

// Global stdin reader to avoid multiple buffered readers competing for stdin
var stdinReader *bufio.Reader

func init() {
	stdinReader = bufio.NewReader(os.Stdin)
}

// ============================================================
// Path & Name Utilities
// ============================================================

func normalizePath(p string) string {
	p = strings.TrimSpace(p)
	p = strings.Trim(p, "\"\u00a0'")
	if strings.HasPrefix(p, "~") {
		if home, err := os.UserHomeDir(); err == nil {
			p = filepath.Join(home, strings.TrimPrefix(p, "~"))
		}
	}
	return filepath.Clean(p)
}

func normalizeChannelName(s string) string {
	switch strings.ToUpper(strings.TrimSpace(s)) {
	case "R", "RED":
		return "R"
	case "G", "GREEN":
		return "G"
	case "B", "BLUE":
		return "B"
	case "A", "ALPHA":
		return "A"
	case "GRAY", "GREY", "LUMINANCE", "L":
		return "Gray"
	default:
		return "Gray"
	}
}

// parseSourceSpec parses a CLI channel source specification.
// Formats: "file.png", "file.png:R", "fill:128", "128", ""
func parseSourceSpec(spec string, defaultFill uint8) channelSource {
	spec = strings.TrimSpace(spec)
	if spec == "" {
		return channelSource{Fill: defaultFill}
	}

	// Pure number → fill value
	if val, err := strconv.Atoi(spec); err == nil && val >= 0 && val <= 255 {
		return channelSource{Fill: uint8(val)}
	}

	// "fill:N" format
	if strings.HasPrefix(strings.ToLower(spec), "fill:") {
		if val, err := strconv.Atoi(spec[5:]); err == nil && val >= 0 && val <= 255 {
			return channelSource{Fill: uint8(val)}
		}
	}

	// File path with optional :CHANNEL suffix
	channel := "Gray"
	if idx := strings.LastIndex(spec, ":"); idx > 0 {
		suffix := strings.ToUpper(spec[idx+1:])
		switch suffix {
		case "R", "RED", "G", "GREEN", "B", "BLUE", "A", "ALPHA", "GRAY", "GREY":
			channel = normalizeChannelName(suffix)
			spec = spec[:idx]
		}
	}

	return channelSource{FilePath: normalizePath(spec), Channel: channel, Fill: defaultFill}
}

// ============================================================
// Image I/O
// ============================================================

// loadImageAsNRGBA loads an image file and ensures it's in NRGBA format
// for correct non-premultiplied channel extraction.
func loadImageAsNRGBA(path string) (*image.NRGBA, error) {
	f, err := os.Open(path)
	if err != nil {
		return nil, err
	}
	defer f.Close()

	img, _, err := image.Decode(f)
	if err != nil {
		return nil, fmt.Errorf("decode failed: %w", err)
	}

	// Fast path: already NRGBA
	if nrgba, ok := img.(*image.NRGBA); ok {
		return nrgba, nil
	}

	// Convert to NRGBA (handles un-premultiplication, Gray→RGBA, YCbCr→RGBA, etc.)
	bounds := img.Bounds()
	nrgba := image.NewNRGBA(bounds)
	draw.Draw(nrgba, bounds, img, bounds.Min, draw.Src)
	return nrgba, nil
}

// ============================================================
// Channel Operations
// ============================================================

// extractChannel extracts a single channel from an NRGBA image into a flat byte buffer.
// Uses direct pixel access with precomputed offsets for maximum performance.
func extractChannel(nrgba *image.NRGBA, ch string) []uint8 {
	bounds := nrgba.Bounds()
	w, h := bounds.Dx(), bounds.Dy()
	buf := make([]uint8, w*h)

	stride := nrgba.Stride
	pixBase := nrgba.PixOffset(bounds.Min.X, bounds.Min.Y)

	if ch == "Gray" {
		// ITU-R BT.601 luminance: 0.299R + 0.587G + 0.114B (integer approximation)
		for y := 0; y < h; y++ {
			srcRow := pixBase + y*stride
			dstRow := y * w
			for x := 0; x < w; x++ {
				si := srcRow + x*4
				buf[dstRow+x] = uint8((uint16(nrgba.Pix[si])*77 +
					uint16(nrgba.Pix[si+1])*150 +
					uint16(nrgba.Pix[si+2])*29) >> 8)
			}
		}
	} else {
		var offset int
		switch ch {
		case "R":
			offset = 0
		case "G":
			offset = 1
		case "B":
			offset = 2
		case "A":
			offset = 3
		}
		for y := 0; y < h; y++ {
			srcRow := pixBase + y*stride + offset
			dstRow := y * w
			for x := 0; x < w; x++ {
				buf[dstRow+x] = nrgba.Pix[srcRow+x*4]
			}
		}
	}

	return buf
}

// resizeNearest performs nearest-neighbor resize of a single-channel buffer.
// Ideal for mask/data textures where preserving hard edges is important.
func resizeNearest(src []uint8, srcW, srcH, dstW, dstH int) []uint8 {
	if srcW == dstW && srcH == dstH {
		return src
	}
	dst := make([]uint8, dstW*dstH)
	for y := 0; y < dstH; y++ {
		sy := y * srcH / dstH
		srcRowOff := sy * srcW
		dstRowOff := y * dstW
		for x := 0; x < dstW; x++ {
			dst[dstRowOff+x] = src[srcRowOff+x*srcW/dstW]
		}
	}
	return dst
}

// ============================================================
// Pack Logic
// ============================================================

// packChannels assembles an output NRGBA image from 4 channel sources.
// Processes channels sequentially to minimize peak memory:
//   output (W*H*4) + one source image at a time + one channel buffer.
func packChannels(outW, outH int, sources [4]channelSource) (*image.NRGBA, []string, error) {
	out := image.NewNRGBA(image.Rect(0, 0, outW, outH))
	var log []string

	// Fill output with defaults
	for i := 0; i < len(out.Pix); i += 4 {
		out.Pix[i] = sources[0].Fill
		out.Pix[i+1] = sources[1].Fill
		out.Pix[i+2] = sources[2].Fill
		out.Pix[i+3] = sources[3].Fill
	}

	// Process each channel source sequentially
	for ci := 0; ci < 4; ci++ {
		src := sources[ci]
		if src.FilePath == "" {
			log = append(log, fmt.Sprintf("  [OK] %s ← fill(%d)", channelLetters[ci], src.Fill))
			continue
		}

		// Load source image
		img, err := loadImageAsNRGBA(src.FilePath)
		if err != nil {
			return nil, nil, fmt.Errorf("%s channel: %w", channelLetters[ci], err)
		}

		srcW := img.Bounds().Dx()
		srcH := img.Bounds().Dy()

		// Extract the specified channel
		chBuf := extractChannel(img, src.Channel)
		img = nil // release source image for GC
		runtime.GC()

		// Resize if dimensions differ
		resized := false
		if srcW != outW || srcH != outH {
			chBuf = resizeNearest(chBuf, srcW, srcH, outW, outH)
			resized = true
		}

		// Write channel data to output
		for y := 0; y < outH; y++ {
			pixRow := out.PixOffset(0, y)
			bufRow := y * outW
			for x := 0; x < outW; x++ {
				out.Pix[pixRow+x*4+ci] = chBuf[bufRow+x]
			}
		}
		chBuf = nil

		sizeInfo := fmt.Sprintf("%dx%d", srcW, srcH)
		if resized {
			sizeInfo += fmt.Sprintf(" → %dx%d", outW, outH)
		}
		log = append(log, fmt.Sprintf("  [OK] %s ← %s (%s, %s)",
			channelLetters[ci], filepath.Base(src.FilePath), src.Channel, sizeInfo))
	}

	return out, log, nil
}

// ============================================================
// Output Size Detection
// ============================================================

// detectOutputSize determines output dimensions from the first source with a file.
// Uses image.DecodeConfig (reads only the header, not the full image).
func detectOutputSize(sources [4]channelSource) (int, int, error) {
	for _, src := range sources {
		if src.FilePath == "" {
			continue
		}
		f, err := os.Open(src.FilePath)
		if err != nil {
			return 0, 0, err
		}
		cfg, _, err := image.DecodeConfig(f)
		f.Close()
		if err != nil {
			return 0, 0, fmt.Errorf("cannot read dimensions from %s: %w", filepath.Base(src.FilePath), err)
		}
		return cfg.Width, cfg.Height, nil
	}
	return 0, 0, fmt.Errorf("no source images provided — cannot determine output size")
}

// ============================================================
// Preview
// ============================================================

func printPreview(sources [4]channelSource, outW, outH int, outPath string, labels [4]string) {
	fmt.Println("\n=============================================")
	fmt.Println("  CHANNEL PACK PREVIEW")
	fmt.Println("=============================================")
	for ci := 0; ci < 4; ci++ {
		src := sources[ci]
		label := ""
		if labels[ci] != channelLabels[ci] {
			label = " (" + labels[ci] + ")"
		}
		if src.FilePath == "" {
			fmt.Printf("  %s%s ← fill(%d)\n", channelLetters[ci], label, src.Fill)
		} else {
			fmt.Printf("  %s%s ← %s : %s\n", channelLetters[ci], label, filepath.Base(src.FilePath), src.Channel)
		}
	}
	fmt.Printf("\n  Output: %s (%dx%d, PNG)\n", outPath, outW, outH)
}

// ============================================================
// Execution
// ============================================================

func executePack(sources [4]channelSource, outW, outH int, outPath string) {
	fmt.Println("\nPacking channels...")
	startTime := time.Now()

	out, log, err := packChannels(outW, outH, sources)
	if err != nil {
		fmt.Printf("\n[ERROR] %v\n", err)
		return
	}

	for _, msg := range log {
		fmt.Println(msg)
	}

	// Encode output PNG with buffered writer
	fmt.Print("\nEncoding PNG...")
	f, err := os.Create(outPath)
	if err != nil {
		fmt.Printf("\n[ERROR] Cannot create output file: %v\n", err)
		return
	}

	writer := bufio.NewWriterSize(f, 256*1024)
	encoder := &png.Encoder{CompressionLevel: png.BestSpeed}
	if err := encoder.Encode(writer, out); err != nil {
		f.Close()
		os.Remove(outPath) // clean up partial file
		fmt.Printf("\n[ERROR] PNG encode failed: %v\n", err)
		return
	}
	if err := writer.Flush(); err != nil {
		f.Close()
		os.Remove(outPath)
		fmt.Printf("\n[ERROR] Write failed: %v\n", err)
		return
	}
	f.Close()
	out = nil
	runtime.GC()

	// Report
	info, _ := os.Stat(outPath)
	var outSize int64
	if info != nil {
		outSize = info.Size()
	}
	duration := time.Since(startTime)

	fmt.Println(" done")
	fmt.Println("\n===========================================")
	fmt.Println("  PACK COMPLETE")
	fmt.Println("===========================================")
	fmt.Printf("  Output:     %s\n", outPath)
	fmt.Printf("  Resolution: %dx%d\n", outW, outH)
	fmt.Printf("  File size:  %s\n", formatSize(outSize))
	fmt.Printf("  Time:       %s\n", duration.Round(time.Millisecond))
}

// ============================================================
// Interactive Mode
// ============================================================

func runInteractive() {
	fmt.Println("==============================================")
	fmt.Println("  Texture Channel Packer")
	fmt.Println("  Pack images into RGBA channels of a texture")
	fmt.Println("==============================================")
	fmt.Println("\nSupported input formats: PNG, JPEG")
	fmt.Println("Output format: PNG (lossless)")

	// Select mode
	fmt.Println("\nSelect packing mode:")
	fmt.Println("  [1] Custom channel packing")
	for i, p := range presets {
		fmt.Printf("  [%d] %s (%s)\n", i+2, p.description, formatPresetLabels(p.labels))
	}

	fmt.Print("\n> ")
	modeStr, _ := stdinReader.ReadString('\n')
	mode, err := strconv.Atoi(strings.TrimSpace(modeStr))
	if err != nil || mode < 1 || mode > len(presets)+1 {
		mode = 1
	}

	labels := channelLabels
	if mode > 1 {
		p := presets[mode-2]
		labels = p.labels
		fmt.Printf("\nUsing preset: %s\n", p.description)
	}

	// Collect channel sources
	var sources [4]channelSource
	fmt.Println("\nFor each channel, drag an image file or type its path.")
	fmt.Println("Type a number (0-255) for a constant fill value.")
	fmt.Println("Press Enter to use the default fill value.\n")

	for ci := 0; ci < 4; ci++ {
		fillDefault := defaultFills[ci]
		label := labels[ci]
		if label != channelLabels[ci] {
			fmt.Printf("--- %s Channel (%s) --- [default fill: %d]\n", channelLabels[ci], label, fillDefault)
		} else {
			fmt.Printf("--- %s Channel --- [default fill: %d]\n", channelLabels[ci], fillDefault)
		}

		fmt.Print("Source: ")
		input, _ := stdinReader.ReadString('\n')
		input = strings.TrimSpace(input)

		if input == "" {
			sources[ci] = channelSource{Fill: fillDefault}
			fmt.Printf("  → fill(%d)\n\n", fillDefault)
			continue
		}

		// Check if it's a fill value
		if val, err := strconv.Atoi(input); err == nil && val >= 0 && val <= 255 {
			sources[ci] = channelSource{Fill: uint8(val)}
			fmt.Printf("  → fill(%d)\n\n", val)
			continue
		}

		// Treat as file path
		filePath := normalizePath(input)
		if _, err := os.Stat(filePath); err != nil {
			fmt.Printf("  [WARNING] File not found: %s\n", filePath)
			fmt.Printf("  Using fill(%d) instead.\n\n", fillDefault)
			sources[ci] = channelSource{Fill: fillDefault}
			continue
		}

		// Ask which channel to extract
		fmt.Print("  Extract channel [R/G/B/A/Gray] (default: Gray): ")
		chStr, _ := stdinReader.ReadString('\n')
		chStr = strings.TrimSpace(chStr)
		ch := "Gray"
		if chStr != "" {
			ch = normalizeChannelName(chStr)
		}

		sources[ci] = channelSource{FilePath: filePath, Channel: ch, Fill: fillDefault}
		fmt.Printf("  → %s : %s\n\n", filepath.Base(filePath), ch)
	}

	// Output path
	fmt.Print("Output file path (default: packed.png): ")
	outPath, _ := stdinReader.ReadString('\n')
	outPath = strings.TrimSpace(outPath)
	if outPath == "" {
		outPath = "packed.png"
	}
	outPath = normalizePath(outPath)
	if !strings.HasSuffix(strings.ToLower(outPath), ".png") {
		outPath += ".png"
	}

	// Determine output size
	outW, outH, err := detectOutputSize(sources)
	if err != nil {
		fmt.Printf("\n[ERROR] %v\n", err)
		fmt.Println("At least one channel must have a source image.")
		waitForKeyPress()
		return
	}

	// Preview
	printPreview(sources, outW, outH, outPath, labels)

	// Confirm
	fmt.Print("\nProceed? (Y/n): ")
	confirm, _ := stdinReader.ReadString('\n')
	confirm = strings.TrimSpace(strings.ToLower(confirm))
	if confirm == "n" || confirm == "no" {
		fmt.Println("Operation cancelled.")
		waitForKeyPress()
		return
	}

	// Execute
	executePack(sources, outW, outH, outPath)
	waitForKeyPress()
}

// ============================================================
// Utilities
// ============================================================

func formatSize(bytes int64) string {
	const (
		KB = 1024
		MB = KB * 1024
		GB = MB * 1024
	)
	switch {
	case bytes >= GB:
		return fmt.Sprintf("%.2f GB", float64(bytes)/float64(GB))
	case bytes >= MB:
		return fmt.Sprintf("%.2f MB", float64(bytes)/float64(MB))
	case bytes >= KB:
		return fmt.Sprintf("%.2f KB", float64(bytes)/float64(KB))
	default:
		return fmt.Sprintf("%d B", bytes)
	}
}

func formatPresetLabels(labels [4]string) string {
	return fmt.Sprintf("R=%s, G=%s, B=%s, A=%s", labels[0], labels[1], labels[2], labels[3])
}

func waitForKeyPress() {
	fmt.Println("\nPress Enter to exit...")
	stdinReader.ReadBytes('\n')
}

// ============================================================
// Entry Point
// ============================================================

func main() {
	var (
		redSpec    string
		greenSpec  string
		blueSpec   string
		alphaSpec  string
		outPath    string
		sizeSpec   string
		presetName string
		ciMode     bool
		dryRun     bool
	)

	flag.StringVar(&redSpec, "r", "", "Red channel source: file.png[:channel], fill:N, or 0-255")
	flag.StringVar(&greenSpec, "g", "", "Green channel source")
	flag.StringVar(&blueSpec, "b", "", "Blue channel source")
	flag.StringVar(&alphaSpec, "a", "", "Alpha channel source")
	flag.StringVar(&outPath, "o", "packed.png", "Output file path (PNG)")
	flag.StringVar(&sizeSpec, "size", "", "Force output size (WxH, e.g. 2048x2048)")
	flag.StringVar(&presetName, "preset", "", "Use preset labels (hdrp-mask, urp-mask)")
	flag.BoolVar(&ciMode, "ci", false, "CI mode (non-interactive, no prompts)")
	flag.BoolVar(&dryRun, "dry-run", false, "Preview only, don't write output")
	flag.Parse()

	// If no channel flags provided, run interactive mode
	if redSpec == "" && greenSpec == "" && blueSpec == "" && alphaSpec == "" && !ciMode {
		runInteractive()
		return
	}

	// CLI mode
	sources := [4]channelSource{
		parseSourceSpec(redSpec, defaultFills[0]),
		parseSourceSpec(greenSpec, defaultFills[1]),
		parseSourceSpec(blueSpec, defaultFills[2]),
		parseSourceSpec(alphaSpec, defaultFills[3]),
	}

	// Validate source files exist
	for ci := 0; ci < 4; ci++ {
		if sources[ci].FilePath != "" {
			if _, err := os.Stat(sources[ci].FilePath); err != nil {
				fmt.Printf("[ERROR] %s channel: file not found: %s\n", channelLetters[ci], sources[ci].FilePath)
				os.Exit(1)
			}
		}
	}

	// Determine output size
	var outW, outH int
	if sizeSpec != "" {
		parts := strings.SplitN(strings.ToLower(sizeSpec), "x", 2)
		if len(parts) != 2 {
			fmt.Println("[ERROR] Invalid size format. Use WxH (e.g. 2048x2048)")
			os.Exit(1)
		}
		w, err1 := strconv.Atoi(parts[0])
		h, err2 := strconv.Atoi(parts[1])
		if err1 != nil || err2 != nil || w <= 0 || h <= 0 {
			fmt.Println("[ERROR] Invalid size values. Width and height must be positive integers.")
			os.Exit(1)
		}
		if w > 16384 || h > 16384 {
			fmt.Println("[WARNING] Texture dimensions exceed 16384. This will require significant memory.")
		}
		outW, outH = w, h
	} else {
		w, h, err := detectOutputSize(sources)
		if err != nil {
			fmt.Printf("[ERROR] %v\nUse -size WxH to specify output dimensions.\n", err)
			os.Exit(1)
		}
		outW, outH = w, h
	}

	// Ensure .png extension
	if !strings.HasSuffix(strings.ToLower(outPath), ".png") {
		outPath += ".png"
	}

	// Resolve preset labels for preview
	labels := channelLabels
	for _, p := range presets {
		if p.name == presetName {
			labels = p.labels
			break
		}
	}

	// Preview
	printPreview(sources, outW, outH, outPath, labels)

	if dryRun {
		fmt.Println("\n[Dry Run] No output file written.")
		return
	}

	// Confirm in non-CI mode
	if !ciMode {
		fmt.Print("\nProceed? (Y/n): ")
		confirm, _ := stdinReader.ReadString('\n')
		confirm = strings.TrimSpace(strings.ToLower(confirm))
		if confirm == "n" || confirm == "no" {
			fmt.Println("Operation cancelled.")
			return
		}
	}

	executePack(sources, outW, outH, outPath)
}
