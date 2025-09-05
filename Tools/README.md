# Unity Starter Tools

<p align="left"><br> English | <a href="README.SCH.md">简体中文</a></p>

This repository contains a collection of utility scripts designed to streamline common tasks in Unity development and general project management. Each script is a standalone Go program.

## Scripts Overview

| Script                       | Description                                                                                                |
| ---------------------------- | ---------------------------------------------------------------------------------------------------------- |
| `audio_volume_normalizer.go` | Batch normalizes audio files to a standard loudness level (-16 LUFS) using FFmpeg.                         |
| `generate_file_tree.go`      | Generates a Markdown file representing the directory structure, with support for blacklists and whitelists.  |
| `image_to_base64.go`         | Converts an image file to a Base64 string, copies it to the clipboard, and saves it to a text file.        |
| `remove_unity_packages.go`   | Cleans up a Unity project's `Packages/manifest.json` by removing a predefined list of non-essential packages. |
| `rename_project.go`          | Renames a Unity project, updating the project folder, company name, and application name in relevant files. |
| `unity_project_full_clean.go`| Performs a deep clean of a Unity project, removing temporary files, caches, and build artifacts.           |

---

### 1. Audio Volume Normalizer

A tool for batch-normalizing audio files to ensure consistent loudness. It uses a two-pass FFmpeg process to achieve a target of **-16.0 LUFS** and **-1.5 dBTP** (True Peak).

**Dependencies:**
- **FFmpeg:** This tool requires FFmpeg to be installed on the operating system and accessible from the system's PATH.

**Features:**
- Recursively scans the current directory for audio files.
- Creates a new `.ogg` file with the `_normalized` suffix for each processed file.
- Skips files that are already within the target loudness range.
- Provides a summary of processed, skipped, and failed files.

**Usage:**
1.  Ensure **FFmpeg** is installed and accessible in your system's PATH.
2.  Place the executable in the root directory containing the audio files you want to process.
3.  Run the executable. It will prompt for confirmation before starting.

---

### 2. Generate File Tree

This script generates a `directory_structure.md` file that visualizes the directory tree of the current location.

**Features:**
- **Whitelist:** Only includes files with specified extensions (e.g., `.go`, `.md`, `.cs`).
- **Blacklist:** Excludes common temporary or source control folders and files (e.g., `.git/`, `node_modules/`, `Library/`).
- **Collapsing:** Collapses blacklisted or non-whitelisted directories for a cleaner view.

**Usage:**
1.  Place the executable in the root directory you want to map.
2.  Run the executable.
3.  A `directory_structure.md` file will be created in the same directory.

---

### 3. Image to Base64

A simple utility to convert an image into a Base64 encoded string.

**Features:**
- Prompts for an image file path (supports drag-and-drop).
- Encodes the image to a Base64 string.
- Automatically copies the string to the clipboard.
- Saves the string to a `.txt` file for backup.

**Usage:**
1.  Run the executable.
2.  Drag and drop an image file onto the terminal window and press Enter.
3.  The Base64 string is copied to your clipboard and saved to a file.

---

### 4. Remove Unity Packages

This script helps slim down a Unity project by removing a list of potentially unnecessary packages from the `Packages/manifest.json` file. This is useful for creating a minimal project template.

**Features:**
- Targets a predefined list of common packages (e.g., `com.unity.timeline`, `com.unity.visualscripting`).
- Reads `Packages/manifest.json`, removes the specified dependencies, and overwrites the file.
- Includes a **Dry Run** mode to preview changes without modifying any files.

**Usage:**
1.  Place the executable in the root of your Unity project (the same level as the `Assets` and `Packages` folders).
2.  Run the executable to apply the changes.
3.  To perform a dry run, set the environment variable `DRY_RUN=1` before executing.

---

### 5. Rename Project

A powerful script to rename a Unity project. It automates the process of updating the project folder name, company name, and application name across multiple configuration files.

**Features:**
- Automatically detects the Unity project root.
- Reads the current project, company, and app names.
- Prompts the user for new names and asks for confirmation.
- Updates folder names, `.meta` files, `ProjectSettings.asset`, and `EditorBuildSettings.asset`.

**Usage:**
1.  Place the executable in the root of your Unity project.
2.  Run the executable and follow the on-screen prompts to enter the new names.

---

### 6. Unity Project Full Clean

This tool performs a thorough cleaning of a Unity project directory, removing temporary files, caches, and other generated content that can be safely deleted. This is useful for version control, archiving, or troubleshooting.

**Features:**
- Deletes a comprehensive list of folders like `Library`, `Temp`, `obj`, and `Build`.
- Removes solution (`.sln`) and C# project (`.csproj`) files.
- Uses concurrent workers for faster deletion.

**Usage:**
1.  **Close the Unity Editor before running.**
2.  Place the executable in the root of your Unity project.
3.  Run the executable.
