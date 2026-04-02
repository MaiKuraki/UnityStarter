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

| Category             | Tools                                               | Purpose                               |
| -------------------- | --------------------------------------------------- | ------------------------------------- |
| **Project Setup**    | `rename_project`, `remove_unity_packages`           | Initialize and configure new projects |
| **Maintenance**      | `unity_project_full_clean`                          | Clean up temporary files and caches   |
| **Asset Processing** | `audio_volume_normalizer`, `texture_channel_packer` | Process and convert assets            |
| **Documentation**    | `generate_file_tree`                                | Generate project documentation        |

## Quick Reference

| Tool                         | Purpose                                                  | When to Use                                        | Location        |
| ---------------------------- | -------------------------------------------------------- | -------------------------------------------------- | --------------- |
| **rename_project**           | Renames Unity project (folder, company, app name)        | Starting a new project from template               | Project root    |
| **remove_unity_packages**    | Removes unnecessary packages from manifest.json          | Creating minimal project template                  | Project root    |
| **unity_project_full_clean** | Deletes temporary files, caches, build artifacts         | Before version control, archiving, troubleshooting | Project root    |
| **audio_volume_normalizer**  | Batch normalizes audio files with category-aware targets | Processing audio assets for consistent loudness    | Audio directory |
| **texture_channel_packer**   | Packs multiple images into RGBA channels of one texture  | Creating HDRP/URP Mask Maps, packed textures       | Anywhere        |
| **generate_file_tree**       | Generates Markdown directory tree                        | Documenting project structure                      | Project root    |

## Tool Details

### 1. Rename Project `rename_project.exe`

**Purpose**: Automatically renames a Unity project, updating all related configuration files. Designed to be **safely re-runnable** on any project derived from UnityStarter.

**What It Does**:

- Renames project folder (`Assets/OldName` → `Assets/NewName`)
- Updates `.asmdef` files (name, references) with word-boundary matching
- Updates `.asmdef` references across **all** of `Assets/` (not just the project folder)
- Updates `BuildScript.cs` constants (precise const-declaration matching)
- Updates `ProjectSettings.asset` (companyName, productName, applicationIdentifier)
- Updates `EditorBuildSettings.asset` (scene paths)
- Updates `.meta` file references

**Key Features**:

- **State file** (`.rename_project.json`): Records current project identity after each rename, ensuring reliable re-detection on subsequent runs
- **Automatic backup**: Creates timestamped backups of all affected files before modification (keeps last 5)
- **Change preview**: Shows a detailed dry-run of all planned changes before execution
- **Immediate input validation**: Validates each name as you enter it, not after confirmation
- **Keep current values**: Press Enter on any prompt to keep the current value unchanged
- **Dual-output logging**: All operations logged to both console and `rename_project.log`
- **Precise replacements**: Uses word-boundary regex (`\b`) for asmdef names, exact const matching for BuildScript.cs, and exact bundle ID matching for ProjectSettings
- **Partial failure recovery**: Saves state checkpoints during execution so re-runs can resume correctly even after partial failures

**Use Case**: When using UnityStarter as a template, rename it to your project name. Can be re-run safely to change names again later.

**Requirements**:

- Unity project root directory
- Write permissions

**Usage**:

```bash
# 1. Place executable in Unity project root (same level as Assets folder)
# 2. Run the executable
rename_project.exe

# 3. Follow interactive prompts:
#    Step 1: Enter new project folder name (or Enter to keep current)
#    Step 2: Enter new company name (or Enter to keep current)
#    Step 3: Enter new application name (or Enter to keep current)
#    Review change preview → Confirm (y/N)
```

**What Gets Updated**:

- Project folder name + `.meta`
- `.asmdef` files: name field, references, file names (word-boundary safe)
- `Assets/Build/Editor/BuildPipeline/BuildScript.cs` (CompanyName, ApplicationName constants)
- `ProjectSettings/ProjectSettings.asset` (companyName, productName, applicationIdentifier for all platforms, metroPackageName, metroApplicationDescription)
- `ProjectSettings/EditorBuildSettings.asset` (scene path prefixes)

**Generated Files**:

- `.rename_project.json` — State file for reliable re-runs (commit to version control)
- `.rename_backup/` — Timestamped backup directory (add to `.gitignore`)
- `rename_project.log` — Operation log

**Safety**:

- Automatic backup before any changes
- Full change preview before execution
- Refuses to overwrite existing target folders (no `os.RemoveAll`)
- State checkpoint after folder rename ensures safe re-runs even after partial failures
- Word-boundary matching prevents accidental substring replacements
- JSON validation after asmdef modifications

---

### 2. Remove Unity Packages `remove_unity_packages.exe`

**Purpose**: Removes unnecessary Unity packages from `Packages/manifest.json` to create a minimal project template.

**What It Does**:

- Reads `Packages/manifest.json` and shows which packages can be removed
- Groups packages into 8 categories (Physics, AI, XR, Visual Scripting, etc.)
- Preserves JSON key order using text-based replacement (clean git diffs)
- Creates `.bak` backup before any modification
- Shows categorized preview before execution

**Package Categories** (24 packages across 8 categories):

| Category | Packages |
|----------|----------|
| 2D | `com.unity.2d.tilemap` |
| AI / Navigation | `com.unity.ai.navigation`, `com.unity.modules.ai` |
| Physics | `com.unity.modules.physics`, `physics2d`, `cloth`, `vehicles`, `wind`, `terrain`, `terrainphysics` |
| Visual Scripting / Timeline | `com.unity.timeline`, `com.unity.visualscripting` |
| XR / VR | `com.unity.modules.vr`, `com.unity.modules.xr` |
| Analytics / Services | `collab-proxy`, `multiplayer.center`, `unityanalytics` |
| Testing | `com.unity.test-framework` |
| Misc Modules | `accessibility`, `jsonserialize`, `tilemap`, `uielements`, `umbra`, `video` |

**Interactive Mode** (select which categories to remove):

```bash
remove_unity_packages -i
```

**CLI Mode**:

```bash
# Remove all listed packages (with preview + confirmation)
remove_unity_packages

# Dry-run: preview without modifying
remove_unity_packages --dry-run

# CI mode: no prompts, remove all
remove_unity_packages --ci

# List all removable packages
remove_unity_packages --list
```

**Flags**:

| Flag | Description |
|------|-------------|
| `-i` | Interactive category selection |
| `--dry-run` | Preview only, no changes |
| `--ci` | Non-interactive mode |
| `--list` | List all removable packages and exit |

**Safety Features**:

- **Unity project validation** before any operation
- **Automatic `.bak` backup** of manifest.json
- **Preview with category grouping** before execution
- **Order-preserving JSON edits** (no key reordering)
- **Confirmation prompt** (default: No)
- **packages-lock.json hint** — reminds to open Unity for regeneration
- Legacy `DRY_RUN=1` env var still supported

---

### 3. Unity Project Full Clean `unity_project_full_clean.exe`

**Purpose**: Performs a deep clean of Unity project, removing all temporary files, caches, and build artifacts.

**What It Deletes**:

- **Folders**: `Library/`, `Temp/`, `obj/`, `Build/`, `.vs/`, `.vscode/`, `Logs/`, etc.
- **Files**: `.sln`, `.csproj`, `.user`, `.vsconfig`
- **Build Artifacts**: All generated build outputs

**Use Case**:

- Before committing to version control (reduce repo size)
- Before archiving projects
- Troubleshooting Unity issues (clean slate)
- Preparing project for distribution

**Key Features**:

- **Unity project validation**: Verifies current directory is a Unity project before any deletion
- **Reliable process detection**: Checks if Unity Editor is running using `EditorInstance.json` + actual PID verification (cross-platform)
- **Change preview with sizes**: Shows every item to be deleted and its size before execution
- **Dry-run mode**: `--dry-run` flag to preview without deleting
- **CI mode**: `--ci` flag for non-interactive automation
- **Concurrent deletion**: Uses multiple workers for fast I/O-bound cleanup
- **Robust retry**: Handles read-only files and transient locks with recursive chmod + retry
- **Deletion summary**: Shows total items deleted, failures, freed space, and elapsed time

**Requirements**:

- Unity project root directory (validates `Assets/` and `ProjectSettings/` exist)
- **Unity Editor must be closed** (actively verifies process is running, not just file presence)
- Write permissions

**Usage**:

```bash
# Standard mode (interactive)
unity_project_full_clean.exe

# Preview mode (see what would be deleted, no changes)
unity_project_full_clean.exe --dry-run

# CI mode (non-interactive, no confirmation)
unity_project_full_clean.exe --ci
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

Files (root level only):
- *.sln             (Solution files)
- *.csproj          (C# project files)
- *.user            (User settings)
- *.vsconfig        (VS config)
```

**Safety**:

- Validates Unity project structure before any deletion
- Verifies Unity Editor process is actually alive (not just stale lock files)
- Shows detailed preview with file sizes before execution
- Requires explicit `y` confirmation (default is No)
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

### 5. Texture Channel Packer `texture_channel_packer.exe`

**Purpose**: Packs multiple source images into the R/G/B/A channels of a single output texture. Essential for creating HDRP/URP Mask Maps and other packed textures in Unity.

**What It Does**:

- Combine up to 4 source images (or constant fill values) into one RGBA texture
- Extract specific channels (R/G/B/A/Gray) from each source
- Auto-detect output resolution from source images
- Nearest-neighbor resize when sources have different dimensions
- Built-in presets for HDRP Mask Map and URP Mask Map
- Memory-efficient: loads one source at a time, processes sequentially

**Use Case**:

- **HDRP/URP Mask Maps**: Metallic(R) + AO(G) + DetailMask(B) + Smoothness(A)
- Packing multiple grayscale maps into a single RGBA texture
- Reducing texture count (4 textures → 1 = 75% fewer draw calls)
- CI/CD texture pipeline automation

**Interactive Mode** (double-click or run without flags):

```bash
# Launches guided wizard with preset selection
texture_channel_packer.exe
```

**CLI Mode**:

```bash
# Custom channel packing
texture_channel_packer -r metallic.png -g ao.png -a smoothness.png:A -o mask_map.png --ci

# With preset labels (for preview display)
texture_channel_packer -r metallic.png -g ao.png -b detail.png -a smoothness.png -o mask.png --preset hdrp-mask --ci

# Dry-run (preview only)
texture_channel_packer -r metallic.png -g ao.png --dry-run

# Fill channels with constant values
texture_channel_packer -r metallic.png -g fill:128 -b 0 -a 255 -o packed.png --ci

# Force output size
texture_channel_packer -r metallic.png -g ao.png -size 2048x2048 -o mask.png --ci
```

**Channel Specifiers**: Append `:R`, `:G`, `:B`, `:A`, or `:Gray` to file path (default: Gray).

**Flags**:

| Flag                   | Description                                                         |
| ---------------------- | ------------------------------------------------------------------- |
| `-r`, `-g`, `-b`, `-a` | Source for each channel: `file.png[:channel]`, `fill:N`, or `0-255` |
| `-o`                   | Output file path (default: `packed.png`)                            |
| `-size`                | Force output size `WxH` (default: auto from first source)           |
| `-preset`              | Use preset labels: `hdrp-mask`, `urp-mask`                          |
| `--ci`                 | Non-interactive mode (no prompts)                                   |
| `--dry-run`            | Preview only, no output written                                     |

**Supported Input**: PNG, JPEG. **Output**: PNG (lossless, alpha-preserving).

**Performance**: Direct pixel access, sequential channel processing, buffered I/O, `BestSpeed` PNG encoding.

**Safety**: Read-only on source images. Preview before execution. Dry-run support.

---

### 6. Generate File Tree `generate_file_tree.exe`

**Purpose**: Generates a Markdown file representing the project directory structure with configurable detail levels.

**What It Does**:

- Scans target directory recursively with configurable depth limits
- Filters files using profile-based extension whitelists
- Supports 4 built-in profiles for different detail levels
- Reads `.treeignore` files for project-specific exclusions
- Sorts output: directories first, then files, alphabetically
- Shows `...` indicator for filtered content

**Profiles**:

| Profile    | Description           | Files Shown                                            | Depth     |
| ---------- | --------------------- | ------------------------------------------------------ | --------- |
| `minimal`  | Quick overview        | Folders only                                           | 3         |
| `standard` | Code & docs (default) | `.go`, `.cs`, `.md`, `.json`, `.yaml`, `.shader`, etc. | Unlimited |
| `detailed` | Code + Unity assets   | Standard + `.asset`, `.prefab`, `.unity`, `.mat`, etc. | Unlimited |
| `full`     | Everything            | All files (no filter)                                  | Unlimited |

**Interactive Mode** (run with `-i`):

```bash
generate_file_tree -i
```

**CLI Mode**:

```bash
# Default: standard profile, current directory
generate_file_tree

# Specific profile and depth
generate_file_tree -profile minimal -depth 4

# Target a specific directory
generate_file_tree -target ./Assets -profile detailed -o assets_tree.md

# Custom extensions (overrides profile)
generate_file_tree -ext .cs,.shader,.hlsl -o code_tree.md

# Add extra ignores
generate_file_tree -ignore ThirdParty,Plugins

# Show file sizes and hidden item counts
generate_file_tree -profile full --show-size --show-count

# CI mode (no prompts, no wait)
generate_file_tree -profile standard --ci
```

**`.treeignore` File** (place in target directory):

```
# Directories (trailing slash)
ThirdParty/
InControl/

# File extensions
*.meta
*.asset

# Exact names
temp
```

**Flags**:

| Flag           | Description                                                             |
| -------------- | ----------------------------------------------------------------------- |
| `-profile`     | `minimal`, `standard`, `detailed`, `full` (default: standard)           |
| `-target`      | Target directory (default: current directory)                           |
| `-o`           | Output file (default: `directory_structure.md`, or `FILE_TREE_OUT` env) |
| `-depth`       | Max depth, 0=unlimited (default: from profile)                          |
| `-ext`         | File extensions, comma-separated (overrides profile)                    |
| `-ignore`      | Additional dirs/names to ignore, comma-separated                        |
| `-i`           | Interactive mode with profile selection                                 |
| `--dirs-only`  | Show only directories                                                   |
| `--show-size`  | Show file sizes                                                         |
| `--show-count` | Show hidden item count in `...` lines                                   |
| `--ci`         | Non-interactive mode                                                    |

**Output Example**:

````markdown
# Directory Structure

- **Generated**: 2026-04-02 12:00:00
- **Profile**: standard

​`
MyProject/
├── Assets/
│   ├── Scripts/
│   │   ├── Player.cs
│   │   └── Enemy.cs
│   ├── Scenes/
│   └── ...
├── Tools/
│   └── Scripts/
│       └── generate_file_tree.go
└── README.md
​`
````

**Safety**: Read-only on source directories. Only creates the output file.

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
