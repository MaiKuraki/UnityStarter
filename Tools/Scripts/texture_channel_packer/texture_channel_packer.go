// Package texture_channel_packer packs multiple images into the RGBA channels
// of a single texture. It supports PNG and JPEG inputs, emits lossless PNG
// output, and loads one source at a time to keep peak memory bounded.
//
// The command is dispatched in-process by cmd/dev-tools.
package texture_channel_packer

import (
	"bufio"
	"errors"
	"flag"
	"fmt"
	"image"
	"image/draw"
	_ "image/jpeg"
	"image/png"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"time"

	"cyclonegames.tools/scripts/internal/logging"
	"cyclonegames.tools/scripts/internal/safefs"
	"cyclonegames.tools/scripts/internal/toolkit"
)

// maxOutputDimension caps each axis so a crafted header cannot request an
// unbounded allocation. The RGBA buffer size is checked in int64 before use.
const maxOutputDimension = 16384

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

type channelSource struct {
	FilePath string
	Channel  string
	Fill     uint8
}

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
	default:
		return "Gray"
	}
}

// parseSourceSpec parses a CLI channel source specification. The channel
// defaults to the one the flag names, so "-r img.png" means "use the red
// channel"; an explicit ":Gray" suffix opts out of that default.
func parseSourceSpec(spec string, defaultFill uint8, defaultChannel string) channelSource {
	spec = strings.TrimSpace(spec)
	if spec == "" {
		return channelSource{Fill: defaultFill}
	}

	if val, err := strconv.Atoi(spec); err == nil && val >= 0 && val <= 255 {
		return channelSource{Fill: uint8(val)}
	}

	if strings.HasPrefix(strings.ToLower(spec), "fill:") {
		if val, err := strconv.Atoi(spec[5:]); err == nil && val >= 0 && val <= 255 {
			return channelSource{Fill: uint8(val)}
		}
	}

	channel := defaultChannel
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

// loadImageAsNRGBA decodes into NRGBA for correct non-premultiplied channel
// extraction. Advisory warnings (for example a 16-bit source reduced to 8 bits)
// are returned separately from hard errors.
func loadImageAsNRGBA(path string) (*image.NRGBA, []string, error) {
	f, err := os.Open(path)
	if err != nil {
		return nil, nil, err
	}
	defer f.Close()

	img, _, err := image.Decode(f)
	if err != nil {
		return nil, nil, fmt.Errorf("decode failed: %w", err)
	}

	var warnings []string
	switch img.(type) {
	case *image.NRGBA64, *image.RGBA64, *image.Gray16:
		warnings = append(warnings, fmt.Sprintf("  [WARNING] 16-bit source %s is reduced to 8-bit output", filepath.Base(path)))
	}

	if nrgba, ok := img.(*image.NRGBA); ok {
		return nrgba, warnings, nil
	}

	bounds := img.Bounds()
	nrgba := image.NewNRGBA(bounds)
	draw.Draw(nrgba, bounds, img, bounds.Min, draw.Src)
	return nrgba, warnings, nil
}

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

// resizeNearest performs nearest-neighbor resize of a single-channel buffer,
// preserving hard edges for mask and data textures.
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

// packChannels assembles an output NRGBA image from 4 channel sources,
// processing one source at a time so peak memory stays bounded to the output
// buffer plus one decoded image and one channel buffer.
func packChannels(outW, outH int, sources [4]channelSource) (*image.NRGBA, []string, error) {
	out := image.NewNRGBA(image.Rect(0, 0, outW, outH))
	var log []string

	for i := 0; i < len(out.Pix); i += 4 {
		out.Pix[i] = sources[0].Fill
		out.Pix[i+1] = sources[1].Fill
		out.Pix[i+2] = sources[2].Fill
		out.Pix[i+3] = sources[3].Fill
	}

	for ci := 0; ci < 4; ci++ {
		src := sources[ci]
		if src.FilePath == "" {
			log = append(log, fmt.Sprintf("  [OK] %s <- fill(%d)", channelLetters[ci], src.Fill))
			continue
		}

		img, warnings, err := loadImageAsNRGBA(src.FilePath)
		if err != nil {
			return nil, nil, fmt.Errorf("%s channel: %w", channelLetters[ci], err)
		}
		log = append(log, warnings...)
		if ext := filepath.Ext(src.FilePath); strings.EqualFold(ext, ".jpg") || strings.EqualFold(ext, ".jpeg") {
			log = append(log, fmt.Sprintf("  [NOTE] JPEG input %s is lossy (chroma subsampling); prefer PNG for mask data", filepath.Base(src.FilePath)))
		}

		srcW := img.Bounds().Dx()
		srcH := img.Bounds().Dy()
		chBuf := extractChannel(img, src.Channel)

		resized := false
		if srcW != outW || srcH != outH {
			chBuf = resizeNearest(chBuf, srcW, srcH, outW, outH)
			resized = true
		}

		for y := 0; y < outH; y++ {
			pixRow := out.PixOffset(0, y)
			bufRow := y * outW
			for x := 0; x < outW; x++ {
				out.Pix[pixRow+x*4+ci] = chBuf[bufRow+x]
			}
		}

		sizeInfo := fmt.Sprintf("%dx%d", srcW, srcH)
		if resized {
			sizeInfo += fmt.Sprintf(" -> %dx%d", outW, outH)
		}
		log = append(log, fmt.Sprintf("  [OK] %s <- %s (%s, %s)",
			channelLetters[ci], filepath.Base(src.FilePath), src.Channel, sizeInfo))
	}

	return out, log, nil
}

// validateOutputDimensions enforces the hard axis cap and proves the RGBA
// allocation fits the platform's addressable int range using int64 arithmetic.
func validateOutputDimensions(w, h int) error {
	if w <= 0 || h <= 0 {
		return fmt.Errorf("output dimensions must be positive, got %dx%d", w, h)
	}
	width, height := int64(w), int64(h)
	if width > maxOutputDimension || height > maxOutputDimension {
		return fmt.Errorf("output dimensions %dx%d exceed the %dx%d limit", w, h, maxOutputDimension, maxOutputDimension)
	}
	pixels := width * height
	bufferBytes := pixels * 4
	maxInt := int64(^uint(0) >> 1)
	if pixels <= 0 || bufferBytes/pixels != 4 || bufferBytes > maxInt {
		return fmt.Errorf("output allocation overflows the addressable buffer size")
	}
	return nil
}

// detectOutputSize reads only the first source header, never the full image.
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
	return 0, 0, fmt.Errorf("no source images provided - cannot determine output size")
}

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
			fmt.Printf("  %s%s <- fill(%d)\n", channelLetters[ci], label, src.Fill)
		} else {
			fmt.Printf("  %s%s <- %s : %s\n", channelLetters[ci], label, filepath.Base(src.FilePath), src.Channel)
		}
	}
	fmt.Printf("\n  Output: %s (%dx%d, PNG)\n", outPath, outW, outH)
}

// prepareOutputTarget validates the destination before any file is created and
// returns an absolute path plus a CreateTemp pattern. The staging-name budget
// uses a representative longest random suffix so a long source name cannot
// produce an unusable temporary path.
func prepareOutputTarget(outPath string) (string, string, error) {
	policy := safefs.HostPathPolicy()
	abs, err := filepath.Abs(outPath)
	if err != nil {
		return "", "", fmt.Errorf("cannot resolve output path %s: %w", outPath, err)
	}
	if err := policy.ValidateAbsolute(abs); err != nil {
		return "", "", err
	}
	base := filepath.Base(abs)
	if err := policy.ValidateComponent(base); err != nil {
		return "", "", err
	}
	dir := filepath.Dir(abs)
	if err := policy.EnsureWritable(dir); err != nil {
		return "", "", err
	}
	pattern := "." + base + ".partial-*"
	budgetName := "." + base + ".partial-123456789012"
	if err := policy.Budget(dir, budgetName); err != nil {
		return "", "", err
	}
	return abs, pattern, nil
}

func executePack(sources [4]channelSource, outW, outH int, outPath string) error {
	if err := validateOutputDimensions(outW, outH); err != nil {
		return err
	}

	fmt.Println("\nPacking channels...")
	startTime := time.Now()

	out, log, err := packChannels(outW, outH, sources)
	if err != nil {
		logging.Errorf("%v", err)
		return err
	}

	for _, msg := range log {
		fmt.Println(msg)
	}

	fmt.Print("\nEncoding PNG...")
	absOutput, pattern, err := prepareOutputTarget(outPath)
	if err != nil {
		logging.Errorf("%v", err)
		return err
	}
	tempFile, err := os.CreateTemp(filepath.Dir(absOutput), pattern)
	if err != nil {
		logging.Errorf("cannot create temporary output file: %v", err)
		return err
	}
	tempPath := tempFile.Name()

	writer := bufio.NewWriterSize(tempFile, 256*1024)
	encoder := &png.Encoder{CompressionLevel: png.BestSpeed}
	if err := encoder.Encode(writer, out); err != nil {
		tempFile.Close()
		os.Remove(tempPath)
		logging.Errorf("png encode failed: %v", err)
		return err
	}
	if err := writer.Flush(); err != nil {
		tempFile.Close()
		os.Remove(tempPath)
		logging.Errorf("write failed: %v", err)
		return err
	}
	if err := tempFile.Close(); err != nil {
		os.Remove(tempPath)
		logging.Errorf("close failed: %v", err)
		return err
	}

	if err := safefs.PublishReplace(tempPath, absOutput); err != nil {
		os.Remove(tempPath)
		logging.Errorf("cannot publish output: %v", err)
		return fmt.Errorf("cannot publish output: %w", err)
	}

	info, _ := os.Stat(absOutput)
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
	return nil
}

// readPromptLine reads one prompt answer. A closed input stream is an error,
// never a silent default: partially fed stdin must not make the tool write
// files unattended. A final line without a trailing newline is still accepted.
func readPromptLine(reader *bufio.Reader, prompt string) (string, error) {
	fmt.Print(prompt)
	line, err := reader.ReadString('\n')
	if err != nil && strings.TrimSpace(line) == "" {
		return "", err
	}
	return strings.TrimSpace(line), nil
}

// cancelledByEOF reports a closed input stream consistently with the other
// interactive tools: cancel loudly and fail instead of proceeding on defaults.
func cancelledByEOF() int {
	fmt.Println("\n[FAIL] Cancelled: input stream closed.")
	return toolkit.ExitFailure
}

// printSourceSizeWarnings reports, before the preview, sources whose dimensions
// differ from the output size: the first file source silently defines that
// size, and a smaller first source would silently downscale larger inputs.
func printSourceSizeWarnings(sources [4]channelSource, outW, outH int) {
	for ci, src := range sources {
		if src.FilePath == "" {
			continue
		}
		f, err := os.Open(src.FilePath)
		if err != nil {
			continue
		}
		cfg, _, err := image.DecodeConfig(f)
		f.Close()
		if err != nil {
			continue
		}
		if cfg.Width != outW || cfg.Height != outH {
			fmt.Printf("  [WARNING] %s channel source %s is %dx%d and will be nearest-resized to %dx%d.\n",
				channelLetters[ci], filepath.Base(src.FilePath), cfg.Width, cfg.Height, outW, outH)
		}
	}
}

func runInteractive(reader *bufio.Reader) int {
	fmt.Println("==============================================")
	fmt.Println("  Texture Channel Packer")
	fmt.Println("  Pack images into RGBA channels of a texture")
	fmt.Println("==============================================")
	fmt.Println("\nSupported input formats: PNG, JPEG")
	fmt.Println("Output format: PNG (lossless)")

	fmt.Println("\nSelect packing mode:")
	fmt.Println("  [1] Custom channel packing")
	for i, p := range presets {
		fmt.Printf("  [%d] %s (%s)\n", i+2, p.description, formatPresetLabels(p.labels))
	}

	modeStr, err := readPromptLine(reader, "\n> ")
	if err != nil {
		return cancelledByEOF()
	}
	mode, convErr := strconv.Atoi(modeStr)
	if convErr != nil || mode < 1 || mode > len(presets)+1 {
		mode = 1
	}

	labels := channelLabels
	if mode > 1 {
		p := presets[mode-2]
		labels = p.labels
		fmt.Printf("\nUsing preset: %s\n", p.description)
	}

	var sources [4]channelSource
	fmt.Println("\nFor each channel, drag an image file or type its path.")
	fmt.Println("Type a number (0-255) for a constant fill value.")
	fmt.Println("Press Enter to use the default fill value.")
	fmt.Println()

	for ci := 0; ci < 4; ci++ {
		fillDefault := defaultFills[ci]
		if labels[ci] != channelLabels[ci] {
			fmt.Printf("--- %s Channel (%s) --- [default fill: %d]\n", channelLabels[ci], labels[ci], fillDefault)
		} else {
			fmt.Printf("--- %s Channel --- [default fill: %d]\n", channelLabels[ci], fillDefault)
		}

		input, err := readPromptLine(reader, "Source: ")
		if err != nil {
			return cancelledByEOF()
		}

		if input == "" {
			sources[ci] = channelSource{Fill: fillDefault}
			fmt.Printf("  -> fill(%d)\n\n", fillDefault)
			continue
		}

		if val, parseErr := strconv.Atoi(input); parseErr == nil && val >= 0 && val <= 255 {
			sources[ci] = channelSource{Fill: uint8(val)}
			fmt.Printf("  -> fill(%d)\n\n", val)
			continue
		}

		filePath := normalizePath(input)
		if _, statErr := os.Stat(filePath); statErr != nil {
			fmt.Printf("  [WARNING] File not found: %s\n", filePath)
			fmt.Printf("  Using fill(%d) instead.\n\n", fillDefault)
			sources[ci] = channelSource{Fill: fillDefault}
			continue
		}

		chStr, err := readPromptLine(reader, "  Extract channel [R/G/B/A/Gray] (default: Gray): ")
		if err != nil {
			return cancelledByEOF()
		}
		ch := "Gray"
		if chStr != "" {
			ch = normalizeChannelName(chStr)
		}

		sources[ci] = channelSource{FilePath: filePath, Channel: ch, Fill: fillDefault}
		fmt.Printf("  -> %s : %s\n\n", filepath.Base(filePath), ch)
	}

	outPathInput, err := readPromptLine(reader, "Output file path (default: packed.png): ")
	if err != nil {
		return cancelledByEOF()
	}
	outPath := outPathInput
	if outPath == "" {
		outPath = "packed.png"
	}
	outPath = normalizePath(outPath)
	if !strings.HasSuffix(strings.ToLower(outPath), ".png") {
		outPath += ".png"
	}

	outW, outH, sizeErr := detectOutputSize(sources)
	if sizeErr != nil {
		logging.Errorf("%v", sizeErr)
		fmt.Println("At least one channel must have a source image.")
		toolkit.WaitForExit()
		return toolkit.ExitFailure
	}
	if err := validateOutputDimensions(outW, outH); err != nil {
		logging.Errorf("%v", err)
		toolkit.WaitForExit()
		return toolkit.ExitUsage
	}
	printSourceSizeWarnings(sources, outW, outH)

	printPreview(sources, outW, outH, outPath, labels)

	confirmed, err := readPromptLine(reader, "\nProceed? (Y/n): ")
	if err != nil {
		return cancelledByEOF()
	}
	if strings.EqualFold(confirmed, "n") || strings.EqualFold(confirmed, "no") {
		fmt.Println("Operation cancelled.")
		toolkit.WaitForExit()
		return toolkit.ExitSuccess
	}

	if packErr := executePack(sources, outW, outH, outPath); packErr != nil {
		toolkit.WaitForExit()
		return toolkit.ExitFailure
	}
	toolkit.WaitForExit()
	return toolkit.ExitSuccess
}

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

// Run executes the channel packer and returns its process exit code.
func Run(args []string) int {
	logging.Command("texture_channel_packer")
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

	flags := flag.NewFlagSet("texture_channel_packer", flag.ContinueOnError)
	flags.SetOutput(os.Stderr)
	flags.StringVar(&redSpec, "r", "", "Red channel source: file.png[:R|G|B|A|Gray], fill:N, or 0-255 (channel defaults to Red)")
	flags.StringVar(&greenSpec, "g", "", "Green channel source (channel defaults to Green)")
	flags.StringVar(&blueSpec, "b", "", "Blue channel source (channel defaults to Blue)")
	flags.StringVar(&alphaSpec, "a", "", "Alpha channel source (channel defaults to Alpha)")
	flags.StringVar(&outPath, "o", "packed.png", "Output file path (PNG)")
	flags.StringVar(&sizeSpec, "size", "", "Force output size (WxH, e.g. 2048x2048)")
	flags.StringVar(&presetName, "preset", "", "Use preset labels (hdrp-mask, urp-mask)")
	flags.BoolVar(&ciMode, "ci", false, "CI mode (non-interactive, no prompts)")
	flags.BoolVar(&dryRun, "dry-run", false, "Preview only, don't write output")
	if err := flags.Parse(args); err != nil {
		if errors.Is(err, flag.ErrHelp) {
			return toolkit.ExitSuccess
		}
		return toolkit.ExitUsage
	}

	reader := toolkit.Input()

	if redSpec == "" && greenSpec == "" && blueSpec == "" && alphaSpec == "" && !ciMode {
		return runInteractive(reader)
	}

	sources := [4]channelSource{
		parseSourceSpec(redSpec, defaultFills[0], "R"),
		parseSourceSpec(greenSpec, defaultFills[1], "G"),
		parseSourceSpec(blueSpec, defaultFills[2], "B"),
		parseSourceSpec(alphaSpec, defaultFills[3], "A"),
	}

	for ci := 0; ci < 4; ci++ {
		if sources[ci].FilePath != "" {
			if _, err := os.Stat(sources[ci].FilePath); err != nil {
				logging.Errorf("%s channel: file not found: %s", channelLetters[ci], sources[ci].FilePath)
				return toolkit.ExitFailure
			}
		}
	}

	var outW, outH int
	if sizeSpec != "" {
		parts := strings.SplitN(strings.ToLower(sizeSpec), "x", 2)
		if len(parts) != 2 {
			logging.Errorf("invalid size format; use WxH (e.g. 2048x2048)")
			return toolkit.ExitFailure
		}
		w, err1 := strconv.Atoi(parts[0])
		h, err2 := strconv.Atoi(parts[1])
		if err1 != nil || err2 != nil || w <= 0 || h <= 0 {
			logging.Errorf("invalid size values; width and height must be positive integers")
			return toolkit.ExitFailure
		}
		if err := validateOutputDimensions(w, h); err != nil {
			logging.Errorf("%v", err)
			return toolkit.ExitUsage
		}
		outW, outH = w, h
	} else {
		w, h, err := detectOutputSize(sources)
		if err != nil {
			logging.Errorf("%v; use -size WxH to specify output dimensions", err)
			return toolkit.ExitFailure
		}
		if err := validateOutputDimensions(w, h); err != nil {
			logging.Errorf("%v", err)
			return toolkit.ExitUsage
		}
		outW, outH = w, h
	}

	if !strings.HasSuffix(strings.ToLower(outPath), ".png") {
		outPath += ".png"
	}

	labels := channelLabels
	for _, p := range presets {
		if p.name == presetName {
			labels = p.labels
			break
		}
	}

	printSourceSizeWarnings(sources, outW, outH)
	printPreview(sources, outW, outH, outPath, labels)

	if dryRun {
		fmt.Println("\n[Dry Run] No output file written.")
		return toolkit.ExitSuccess
	}

	if !ciMode {
		confirmed, err := readPromptLine(reader, "\nProceed? (Y/n): ")
		if err != nil {
			return cancelledByEOF()
		}
		if strings.EqualFold(confirmed, "n") || strings.EqualFold(confirmed, "no") {
			fmt.Println("Operation cancelled.")
			return toolkit.ExitSuccess
		}
	}

	if err := executePack(sources, outW, outH, outPath); err != nil {
		return toolkit.ExitFailure
	}
	return toolkit.ExitSuccess
}
