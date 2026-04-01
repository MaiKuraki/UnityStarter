# Unity Starter Tools

A collection of standalone utility scripts designed to streamline Unity development workflows and project management tasks. All tools are written in Go and provided as pre-compiled executables for Windows.

<p align="left"><br> English | <a href="README.SCH.md">简体中文</a></p>

## Table of Contents

1. [Overview](#overview)
2. [Quick Reference](#quick-reference)
3. [Tool Details](#tool-details)
4. [Installation & Setup](#installation--setup)
5. [Usage Examples](#usage-examples)

## Overview

These tools automate common but tedious tasks in Unity development:

- **Project Setup**: Rename projects, clean up packages
- **Project Maintenance**: Deep clean temporary files and caches
- **Asset Processing**: Normalize audio, convert images
- **Documentation**: Generate project structure trees

All tools are **standalone executables** - no installation required. Simply download and run.

### Tool Categories

| Category             | Tools                                        | Purpose                               |
| -------------------- | -------------------------------------------- | ------------------------------------- |
| **Project Setup**    | `rename_project`, `remove_unity_packages`    | Initialize and configure new projects |
| **Maintenance**      | `unity_project_full_clean`                   | Clean up temporary files and caches   |
| **Asset Processing** | `audio_volume_normalizer`, `image_to_base64` | Process and convert assets            |
| **Documentation**    | `generate_file_tree`                         | Generate project documentation        |

## Quick Reference

| Tool                         | Purpose                                                  | When to Use                                        | Location        |
| ---------------------------- | -------------------------------------------------------- | -------------------------------------------------- | --------------- |
| **rename_project**           | Renames Unity project (folder, company, app name)        | Starting a new project from template               | Project root    |
| **remove_unity_packages**    | Removes unnecessary packages from manifest.json          | Creating minimal project template                  | Project root    |
| **unity_project_full_clean** | Deletes temporary files, caches, build artifacts         | Before version control, archiving, troubleshooting | Project root    |
| **audio_volume_normalizer**  | Batch normalizes audio files with category-aware targets | Processing audio assets for consistent loudness    | Audio directory |
| **image_to_base64**          | Converts image to Base64 string                          | Embedding images in code/config files              | Anywhere        |
| **generate_file_tree**       | Generates Markdown directory tree                        | Documenting project structure                      | Project root    |

## Tool Details

### 1. Rename Project `rename_project.exe`

**Purpose**: Automatically renames a Unity project, updating all related configuration files.

**What It Does**:

- Renames project folder
- Updates company name in `ProjectSettings.asset`
- Updates application name in `ProjectSettings.asset` and `EditorBuildSettings.asset`
- Updates `.meta` file references
- Updates Build script references

**Use Case**: When using UnityStarter as a template, rename it to your project name.

**Requirements**:

- Unity project root directory
- Write permissions

**Usage**:

```bash
# 1. Place executable in Unity project root (same level as Assets folder)
# 2. Run the executable
rename_project.exe

# 3. Follow interactive prompts:
#    - Enter new project folder name
#    - Enter new company name
#    - Enter new application name
#    - Confirm changes
```

**What Gets Updated**:

- Project folder name
- `ProjectSettings/ProjectSettings.asset` (companyName, productName)
- `ProjectSettings/EditorBuildSettings.asset` (productName)
- `.meta` files (folder references)
- Build script references (if using UnityStarter Build module)

**Safety**: Prompts for confirmation before making changes. Safe to run multiple times.

---

### 2. Remove Unity Packages `remove_unity_packages.exe`

**Purpose**: Removes unnecessary Unity packages from `Packages/manifest.json` to create a minimal project template.

**What It Does**:

- Reads `Packages/manifest.json`
- Removes predefined list of non-essential packages (Timeline, Visual Scripting, etc.)
- Writes updated manifest back to file

**Use Case**: Creating a minimal project template by removing unused Unity packages.

**Packages Removed** (default list):

- `com.unity.timeline`
- `com.unity.visualscripting`
- `com.unity.modules.physics`
- `com.unity.modules.physics2d`
- `com.unity.modules.terrain`
- And 20+ more non-essential packages

**Requirements**:

- Unity project root directory
- `Packages/manifest.json` file exists
- Write permissions

**Usage**:

```bash
# Standard mode (applies changes)
remove_unity_packages.exe

# Dry run mode (preview changes without applying)
set DRY_RUN=1
remove_unity_packages.exe
```

**Dry Run Mode**:

- Set environment variable `DRY_RUN=1` before running
- Shows what would be removed without modifying files
- Useful for previewing changes

**Safety**: Includes dry run mode. Always backup `manifest.json` before running.

---

### 3. Unity Project Full Clean `unity_project_full_clean.exe`

**Purpose**: Performs a deep clean of Unity project, removing all temporary files, caches, and build artifacts.

**What It Deletes**:

- **Folders**: `Library/`, `Temp/`, `obj/`, `Build/`, `.vs/`, `.vscode/`, `Logs/`, etc.
- **Files**: `.sln`, `.csproj`, `.user`, `.vsconfig`, etc.
- **Build Artifacts**: All generated build outputs

**Use Case**:

- Before committing to version control (reduce repo size)
- Before archiving projects
- Troubleshooting Unity issues (clean slate)
- Preparing project for distribution

**Requirements**:

- Unity project root directory
- **Unity Editor must be closed** (checks for running instance)
- Write permissions

**Usage**:

```bash
# 1. Close Unity Editor (tool will check and warn if running)
# 2. Place executable in Unity project root
# 3. Run the executable
unity_project_full_clean.exe

# Tool will:
# - Check if Unity is running (warns if detected)
# - List files/folders to be deleted
# - Prompt for confirmation
# - Delete using concurrent workers (fast)
```

**What Gets Deleted**:

```
Folders:
- Library/          (Unity cache)
- Temp/             (Temporary files)
- obj/              (Build objects)
- Build/            (Build outputs)
- .vs/              (Visual Studio cache)
- .vscode/          (VS Code cache)
- Logs/             (Unity logs)
- HybridCLRData/    (HybridCLR cache)
- Bundles/          (Asset bundles)
- And more...

Files:
- *.sln             (Solution files)
- *.csproj          (C# project files)
- *.user            (User settings)
- *.vsconfig        (VS config)
```

**Safety**:

- Checks for running Unity Editor
- Lists all items before deletion
- Requires confirmation
- **Warning**: This is destructive. Ensure Unity Editor is closed and you have backups.

---

### 4. Audio Volume Normalizer `audio_volume_normalizer.exe`

**Purpose**: Batch normalizes audio files to a consistent loudness level using FFmpeg, with game audio-optimized strategies.

**What It Does**:

- Recursively scans directory for audio files
- **Auto-detects audio category** from parent folder name (Music, SFX, Voice, Ambient)
- **Dual normalization strategy**: LUFS for long audio (≥ 3s), Peak for short SFX (< 3s)
- **Selectable output format**: WAV (lossless) or OGG (Vorbis VBR)
- Uses two-pass linear mode loudnorm for highest quality
- Skips files already within target range
- Per-file timeout protection against corrupted files

**Category Auto-Detection**:

The tool automatically detects the audio category based on parent folder names (case-insensitive):

| Category | Matched Folders         | LUFS Target | True Peak | Peak Target (short SFX) |
| -------- | ----------------------- | ----------- | --------- | ----------------------- |
| Music    | `music`, `bgm`          | -14.0 LUFS  | -1.0 dBTP | -1.0 dB                 |
| Voice    | `voice`, `dialog`, `vo` | -16.0 LUFS  | -1.5 dBTP | -1.0 dB                 |
| SFX      | `sfx`, `se`, `sound`    | -14.0 LUFS  | -1.0 dBTP | -1.0 dB                 |
| Ambient  | `ambient`, `env`        | -20.0 LUFS  | -1.5 dBTP | -3.0 dB                 |
| Default  | _(any other)_           | -16.0 LUFS  | -1.5 dBTP | -1.0 dB                 |

**Normalization Strategies**:

- **Long audio (≥ 3s)**: Two-pass LUFS normalization with `linear=true` — preserves dynamic range while adjusting perceived loudness. Ideal for music, dialog, and ambient.
- **Short audio (< 3s)**: Peak normalization — LUFS (ITU-R BS.1770) requires ≥ 400ms for reliable measurement, making it unsuitable for short SFX like button clicks, footsteps, and gunshots.

**Output Format Selection**:

At startup, you can choose the output format:

| Format | Codec         | Best For                                                                             |
| ------ | ------------- | ------------------------------------------------------------------------------------ |
| WAV    | PCM 16-bit    | **Recommended**: Lossless, avoids double compression when Unity re-encodes on import |
| OGG    | Vorbis VBR q6 | When disk space / Git repo size matters                                              |

> **Note**: WAV vs OGG source files have **zero impact** on Unity runtime memory or CPU — Unity re-encodes all audio on import according to your AudioClip Import Settings. See the [Audio Best Practices Guide](../Docs/AudioBestPractices/AudioBestPractices.md) for details.

**Use Case**: Processing audio assets to ensure consistent volume levels across all files, with category-aware loudness targets optimized for game audio.

**Requirements**:

- **FFmpeg** installed and accessible in system PATH
- Audio files in directory (supports: `.wav`, `.mp3`, `.ogg`, `.flac`, `.m4a`, `.aac`, `.wma`, `.opus`)
- Write permissions

**Usage**:

```bash
# 1. Install FFmpeg and add to PATH
#    Download from: https://ffmpeg.org/download.html

# 2. Place executable in directory containing audio files
#    Organize files by category folders:
#    Audio/
#    ├── Music/        (auto-detected as Music)
#    ├── SFX/          (auto-detected as SFX)
#    ├── Voice/        (auto-detected as Voice)
#    └── Ambient/      (auto-detected as Ambient)
cd /path/to/audio/files

# 3. Run the executable
audio_volume_normalizer.exe

# 4. Select output format (1=WAV, 2=OGG)
# 5. Confirm to proceed

# Tool will:
# - Auto-detect category from folder names
# - Choose strategy based on audio duration
# - Show progress bar during processing
# - Show summary (processed, skipped, failed with error details)
```

**Output**:

- Original file: `SFX/gunshot.wav`
- Normalized file: `SFX/gunshot_normalized.wav` (or `.ogg`)

**Features**:

- Category-aware loudness targets (auto-detected from folder structure)
- Dual strategy: LUFS for long audio, Peak for short SFX
- Two-pass FFmpeg with `linear=true` for highest quality
- Selectable output format (WAV lossless / OGG Vorbis VBR)
- Skips already-normalized files and silent files
- Per-file timeout (10 min) to handle corrupted files
- Concurrent processing with progress bar
- Strips source metadata to prevent encoding issues (WAV does not support UTF-8 metadata)
- Detailed summary with error messages for failed files

**Safety**: Creates new files, never modifies originals. Safe to run multiple times.

---

### 5. Image to Base64 `image_to_base64.exe`

**Purpose**: Converts an image file to Base64 encoded string for embedding in code or config files.

**What It Does**:

- Reads image file (supports common formats)
- Encodes to Base64 string
- Copies string to clipboard automatically
- Saves string to `.txt` file for backup

**Use Case**:

- Embedding images in code (data URIs)
- Storing images in JSON/YAML config files
- Creating inline image resources

**Requirements**:

- Image file path (supports drag-and-drop)
- Clipboard access

**Usage**:

```bash
# 1. Run the executable
image_to_base64.exe

# 2. When prompted, drag and drop image file onto terminal
#    Or type/paste the file path

# 3. Base64 string is automatically:
#    - Copied to clipboard
#    - Saved to {filename}_base64.txt
```

**Output**:

- Clipboard: Base64 string (ready to paste)
- File: `{original_filename}_base64.txt` (backup)

**Supported Formats**: `.png`, `.jpg`, `.jpeg`, `.gif`, `.bmp`, `.webp`, etc.

**Safety**: Read-only operation. Never modifies original image.

---

### 6. Generate File Tree `generate_file_tree.go`

**Purpose**: Generates a Markdown file representing the project directory structure.

**What It Does**:

- Scans current directory recursively
- Filters files using whitelist (extensions) and blacklist (folders)
- Generates `directory_structure.md` with tree view
- Collapses excluded directories for cleaner output

**Use Case**:

- Documenting project structure
- Creating README file trees
- Visualizing codebase organization

**Features**:

- **Whitelist**: Only includes specified file extensions (`.cs`, `.md`, `.go`, etc.)
- **Blacklist**: Excludes common folders (`.git/`, `node_modules/`, `Library/`, etc.)
- **Collapsing**: Hides excluded directories for cleaner view

**Requirements**:

- Read permissions for directory
- Write permissions for output file

**Usage**:

```bash
# 1. Place executable in directory you want to map
cd /path/to/project

# 2. Run the executable
generate_file_tree.exe

# 3. Output file created: directory_structure.md
```

**Output Example**:

```markdown
.
├── Assets/
│ ├── Scripts/
│ │ └── Player.cs
│ └── Scenes/
│ └── Main.unity
├── Packages/
└── README.md
```

**Customization**: Edit the Go source to modify whitelist/blacklist filters.

**Safety**: Read-only operation. Only creates new file, never modifies existing files.

## Installation & Setup

### Getting the Tools

**Option 1: Use Pre-compiled Executables (Recommended)**

1. Navigate to `Tools/Executable/Windows/`
2. Copy the `.exe` files you need
3. Place them in your desired location
4. Run directly (no installation required)

**Option 2: Build from Source**

1. Install [Go](https://golang.org/dl/) (1.16+)
2. Navigate to `Tools/Scripts/`
3. Build:
   ```bash
   go build -o rename_project.exe rename_project.go
   go build -o remove_unity_packages.exe remove_unity_packages.go
   # ... etc for each tool
   ```

### Prerequisites

**For All Tools**:

- Windows OS (executables provided)
- Appropriate file permissions

**For Specific Tools**:

- **audio_volume_normalizer**: Requires [FFmpeg](https://ffmpeg.org/download.html) in system PATH
- **rename_project**: Requires Unity project structure
- **remove_unity_packages**: Requires `Packages/manifest.json`
- **unity_project_full_clean**: Requires Unity project structure

### Setting Up FFmpeg (for Audio Normalizer)

1. Download FFmpeg from [official website](https://ffmpeg.org/download.html)
2. Extract to a location (e.g., `C:\ffmpeg\`)
3. Add to system PATH:
   - Open "Environment Variables" in Windows
   - Edit "Path" variable
   - Add FFmpeg `bin` directory (e.g., `C:\ffmpeg\bin`)
4. Verify: Open command prompt and run `ffmpeg -version`

## Usage Examples

### Example 1: Setting Up a New Project

**Scenario**: You cloned UnityStarter and want to rename it to "MyGame".

```bash
# Step 1: Rename the project
cd UnityStarter
rename_project.exe
# Enter: MyGame (project name)
# Enter: MyCompany (company name)
# Enter: MyGame (application name)
# Confirm: Yes

# Step 2: Remove unnecessary packages (optional)
remove_unity_packages.exe
# Review the list, confirm removal

# Step 3: Clean the project
unity_project_full_clean.exe
# Close Unity Editor first, then run
```

### Example 2: Processing Audio Assets

**Scenario**: You have audio files organized by category that need consistent volume.

```bash
# Step 1: Organize audio files by category in folders
# Assets/Audio/
# ├── BGM/           -> Music targets (-14 LUFS)
# ├── SFX/           -> SFX targets (-14 LUFS / Peak -1.0 dB for short clips)
# ├── Voice/         -> Voice targets (-16 LUFS)
# └── Ambient/       -> Ambient targets (-20 LUFS)

# Step 2: Navigate to audio root directory
cd Assets/Audio

# Step 3: Run normalizer
audio_volume_normalizer.exe
# Select output format: 1 (WAV) or 2 (OGG)
# Confirm: Yes

# Step 4: Review summary
# - 15 files processed (category auto-detected from folder names)
# - 3 files skipped (already normalized)
# - 2 files skipped (short SFX, peak-normalized instead of LUFS)
# - 0 files failed

# Step 5: Use normalized files in your project
# Files: gunshot_normalized.wav (or .ogg)
```

For detailed Unity audio import settings and optimization recommendations, see the [Audio Best Practices Guide](../Docs/AudioBestPractices/AudioBestPractices.md).

### Example 3: Generating Project Documentation

**Scenario**: You want to document your project structure in README.

```bash
# Step 1: Navigate to project root
cd /path/to/project

# Step 2: Generate file tree
generate_file_tree.exe

# Step 3: Review output
# File created: directory_structure.md

# Step 4: Copy relevant sections to README.md
```

### Example 4: Preparing Project for Version Control

**Scenario**: You want to clean the project before committing to Git.

```bash
# Step 1: Close Unity Editor (important!)

# Step 2: Run full clean
unity_project_full_clean.exe

# Step 3: Verify .gitignore excludes:
# - Library/
# - Temp/
# - Build/
# - *.csproj
# - *.sln

# Step 4: Commit cleaned project
git add .
git commit -m "Initial project setup"
```

## Best Practices

### 1. Backup Before Running

- **rename_project**: Backup `ProjectSettings/` folder
- **remove_unity_packages**: Backup `Packages/manifest.json`
- **unity_project_full_clean**: Ensure Unity Editor is closed

### 2. Use Dry Run Mode

For `remove_unity_packages`, always test with dry run first:

```bash
set DRY_RUN=1
remove_unity_packages.exe
```

### 3. Close Unity Editor

Always close Unity Editor before running:

- `unity_project_full_clean` (required)
- `rename_project` (recommended)
- `remove_unity_packages` (recommended)

### 4. Verify Prerequisites

- Check FFmpeg installation for audio normalizer
- Verify file permissions
- Ensure you're in the correct directory

### 5. Read Output Carefully

All tools provide:

- Preview of changes
- Confirmation prompts
- Summary reports

Always review before confirming.

## Troubleshooting

### Tool Not Found / Not Executable

**Problem**: Executable won't run or not found.

**Solutions**:

- Ensure you're using Windows executables from `Tools/Executable/Windows/`
- Check file permissions (right-click > Properties > Unblock if needed)
- Try running from command prompt with full path

### FFmpeg Not Found (Audio Normalizer)

**Problem**: `audio_volume_normalizer` fails with "FFmpeg not found".

**Solutions**:

- Install FFmpeg from [official website](https://ffmpeg.org/download.html)
- Add FFmpeg `bin` directory to system PATH
- Verify with `ffmpeg -version` in command prompt
- Restart command prompt after adding to PATH

### Unity Editor Running (Full Clean)

**Problem**: `unity_project_full_clean` warns about Unity running.

**Solutions**:

- Close Unity Editor completely
- Check Task Manager for Unity processes
- Wait a few seconds after closing, then retry

### Permission Denied

**Problem**: Tool can't write files or delete folders.

**Solutions**:

- Run as Administrator (right-click > Run as administrator)
- Check file/folder permissions
- Ensure files aren't locked by other processes
- Close Unity Editor and other applications using the files

### Project Root Not Found (Rename Project)

**Problem**: `rename_project` can't find Unity project.

**Solutions**:

- Place executable in Unity project root (same level as `Assets/` folder)
- Ensure `ProjectSettings/` folder exists
- Check current directory with `cd` command
