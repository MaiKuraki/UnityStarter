// To build directly without modules or external libs.
// This tool copies Base64 to clipboard using OS commands (Windows: clip; macOS: pbcopy; Linux: xclip/wl-copy if available).

package main

import (
	"bufio"
	"encoding/base64"
	"fmt"
	"log"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"strings"
)

func normalizePath(p string) string {
	p = strings.TrimSpace(p)
	p = strings.Trim(p, "\"\u00a0")
	if strings.HasPrefix(p, "~") {
		if home, err := os.UserHomeDir(); err == nil {
			p = filepath.Join(home, strings.TrimPrefix(p, "~"))
		}
	}
	return p
}

func copyToClipboard(s string) error {
	switch runtime.GOOS {
	case "windows":
		cmd := exec.Command("cmd", "/c", "clip")
		stdin, err := cmd.StdinPipe()
		if err != nil {
			return err
		}
		if err := cmd.Start(); err != nil {
			return err
		}
		if _, err := stdin.Write([]byte(s)); err != nil {
			return err
		}
		stdin.Close()
		return cmd.Wait()
	case "darwin":
		cmd := exec.Command("pbcopy")
		stdin, err := cmd.StdinPipe()
		if err != nil {
			return err
		}
		if err := cmd.Start(); err != nil {
			return err
		}
		if _, err := stdin.Write([]byte(s)); err != nil {
			return err
		}
		stdin.Close()
		return cmd.Wait()
	default: // linux/bsd
		// Try wl-copy then xclip
		if _, err := exec.LookPath("wl-copy"); err == nil {
			cmd := exec.Command("wl-copy")
			stdin, err := cmd.StdinPipe()
			if err != nil {
				return err
			}
			if err := cmd.Start(); err != nil {
				return err
			}
			if _, err := stdin.Write([]byte(s)); err != nil {
				return err
			}
			stdin.Close()
			return cmd.Wait()
		}
		if _, err := exec.LookPath("xclip"); err == nil {
			cmd := exec.Command("xclip", "-selection", "clipboard")
			stdin, err := cmd.StdinPipe()
			if err != nil {
				return err
			}
			if err := cmd.Start(); err != nil {
				return err
			}
			if _, err := stdin.Write([]byte(s)); err != nil {
				return err
			}
			stdin.Close()
			return cmd.Wait()
		}
		return fmt.Errorf("no clipboard tool found (tried wl-copy/xclip)")
	}
}

func main() {
	reader := bufio.NewReader(os.Stdin)
	fmt.Println("Please enter the path of the image file (you can drag the image here and press enter):")

	imagePath, err := reader.ReadString('\n')
	if err != nil {
		log.Fatalf("Failed to read input: %v", err)
	}
	imagePath = normalizePath(imagePath)

	data, err := os.ReadFile(imagePath)
	if err != nil {
		log.Fatalf("Unable to read file: %v", err)
	}

	base64String := base64.StdEncoding.EncodeToString(data)

	fmt.Println("\nBase64 encoded string:")
	fmt.Println(base64String)

	if err := copyToClipboard(base64String); err != nil {
		fmt.Printf("\nWarning: failed to copy to clipboard: %v\n", err)
	} else {
		fmt.Println("\nBase64 string has been automatically copied to the clipboard!")
	}

	outName := filepath.Base(imagePath) + ".base64.txt"
	if err := os.WriteFile(outName, []byte(base64String), 0644); err != nil {
		log.Fatalf("Unable to save Base64 string to file: %v", err)
	}
	fmt.Printf("\nBase64 string has been saved to file: %s\n", outName)

	fmt.Println("\nPress the enter key to close the program...")
	reader.ReadString('\n')
}
