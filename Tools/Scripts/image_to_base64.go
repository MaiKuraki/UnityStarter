// To run this Go file, you need to install the clipboard package, run the following command:
// go get github.com/atotto/clipboard
/*
	# 1. Create a project directory
	mkdir image_to_base64
	cd image_to_base64

	# 2. Initialize Go module
	go mod init image_to_base64

	# 3. Install clipboard package
	go get github.com/atotto/clipboard

	# 4. Create main.go file and add your code
	# Use a text editor to open and paste the code, then save the file.

	# 5. Run the program
	go run main.go
**/

package main

import (
	"bufio"
	"encoding/base64"
	"fmt"
	"io"
	"log"
	"os"
	"strings"

	"github.com/atotto/clipboard"
)

func main() {
	// Create a command line input prompt
	reader := bufio.NewReader(os.Stdin)
	fmt.Println("Please enter the path of the image file (you can drag the image here and press enter):")

	// Read the user's input path
	imagePath, err := reader.ReadString('\n')
	if err != nil {
		log.Fatalf("Failed to read input: %v", err)
	}

	// Remove the newline character and any extra spaces from the path
	imagePath = strings.TrimSpace(imagePath)

	// Open the image file
	file, err := os.Open(imagePath)
	if err != nil {
		log.Fatalf("Unable to open file: %v", err)
	}
	defer file.Close()

	// Read the content of the image file
	imageData, err := io.ReadAll(file)
	if err != nil {
		log.Fatalf("Unable to read file: %v", err)
	}

	// Convert the image content to Base64
	base64String := base64.StdEncoding.EncodeToString(imageData)

	// Print the result
	fmt.Println("\nBase64 encoded string:")
	fmt.Println(base64String)

	// Copy the Base64 string to the clipboard
	err = clipboard.WriteAll(base64String)
	if err != nil {
		log.Fatalf("Unable to copy Base64 string to clipboard: %v", err)
	}
	fmt.Println("\nBase64 string has been automatically copied to the clipboard!")

	// Optional: Save the Base64 string to a text file
	outputFile := "output_base64.txt"
	err = os.WriteFile(outputFile, []byte(base64String), 0644)
	if err != nil {
		log.Fatalf("Unable to save Base64 string to file: %v", err)
	}
	fmt.Printf("\nBase64 string has been saved to file: %s\n", outputFile)

	// Wait for the user to press enter before exiting
	fmt.Println("\nPress the enter key to close the program...")
	reader.ReadString('\n') // Wait for user to press enter
}
