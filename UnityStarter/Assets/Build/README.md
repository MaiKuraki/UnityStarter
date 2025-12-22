[**English**] | [**简体中文**](README.SCH.md)

# Build Module Documentation

The Build module provides a comprehensive, flexible build pipeline for Unity projects. It supports full app builds, hot updates for both code (via HybridCLR) and assets (via YooAsset or Addressables), and seamless CI/CD integration. The system is designed to be modular, allowing you to use only the features you need.

## Table of Contents

1. [Overview](#overview)
2. [Prerequisites](#prerequisites)
3. [Quick Start](#quick-start)
4. [Core Concepts](#core-concepts)
5. [Configuration](#configuration)
6. [Build Workflows](#build-workflows)
7. [CI/CD Integration](#cicd-integration)
8. [Troubleshooting](#troubleshooting)

## Overview

The Build module consists of several key components:

- **BuildData**: Central configuration ScriptableObject (required for all builds)
- **BuildScript**: Full application build pipeline
- **HotUpdateBuilder**: Unified hot update workflow for code and assets
- **HybridCLR Integration**: C# code hot-update support (optional)
- **Obfuz Integration**: Code obfuscation for protecting your code (optional)
- **YooAsset Integration**: Asset management and hot-update (optional)
- **Addressables Integration**: Unity's official asset management (optional)
- **Buildalon Integration**: Build automation helpers (optional)

### Key Features

- ✅ **Flexible Package Support**: Works with or without optional packages (HybridCLR, Obfuz, YooAsset, Addressables, Buildalon)
- ✅ **Automatic Versioning**: Git-based version generation
- ✅ **Multi-Platform**: Supports Windows, Mac, Linux, Android, iOS, WebGL
- ✅ **Hot Update Ready**: Complete solution for code and asset hot updates
- ✅ **Code Protection**: Integrated Obfuz obfuscation for protecting your code
- ✅ **CI/CD Friendly**: Command-line interface for automated builds
- ✅ **Configuration-Driven**: All settings via ScriptableObject assets

## Prerequisites

### Required

- **Unity 2022.3+**
- **Git** (for automatic versioning)

### Optional Packages

The Build system supports the following optional packages. Install only what you need:

- **[HybridCLR](https://github.com/focus-creative-games/hybridclr)** - For C# code hot-updates
- **[Obfuz](https://github.com/Code-Philosophy/Obfuz)** - Code obfuscation for protecting your code
- **[Obfuz4HybridCLR](https://github.com/Code-Philosophy/Obfuz4HybridCLR)** - Obfuz extension for HybridCLR hot update assemblies
- **[YooAsset](https://github.com/tuyoogame/YooAsset)** - Lightweight asset management system
- **[Addressables](https://docs.unity3d.com/Packages/com.unity.addressables@latest)** - Unity's official asset management (via Package Manager)
- **[Buildalon](https://github.com/virtualmaker/Buildalon)** - Build automation helpers

> **Note**: The Build system uses reflection to detect optional packages. If a package is not installed, the related features will be automatically disabled. No compilation errors will occur.

## Quick Start

### Step 1: Create BuildData Asset

**BuildData is required for all builds.** You must create this asset manually for each project.

1. In the Unity Editor, right-click in your Project window
2. Select **Create > CycloneGames > Build > BuildData**
3. Name it `BuildData` (or any name you prefer)
4. Place it in a location that makes sense for your project (e.g., `Assets/Config/BuildData.asset`)

> **⚠️ Important**: Only **one** BuildData asset should exist in your project. The system will automatically find and use it.

### Step 2: Configure BuildData

Select the BuildData asset and configure it in the Inspector:

**Basic Settings:**

- **Launch Scene**: The scene that will be used as the entry point for builds
- **Application Version**: Version prefix (e.g., `v0.1`). Final version will be `{ApplicationVersion}.{CommitCount}`
- **Output Base Path**: Base directory for build results (relative to project root, e.g., `Build`)

**Build Pipeline Options:**

- **Use Buildalon**: Enable if you have Buildalon package installed and want to use its helpers
- **Use HybridCLR**: Enable if you have HybridCLR package installed and want code hot-updates
- **Use Obfuz**: Enable if you have Obfuz packages installed and want code obfuscation (see [Obfuz Configuration](#obfuz-configuration) below)

**Asset Management System:**

- **None**: No asset management (resources built into player)
- **YooAsset**: Use YooAsset for asset management and hot-updates
- **Addressables**: Use Unity Addressables for asset management and hot-updates

### Step 3: Create Additional Config Assets (If Needed)

Depending on your selected options, you may need additional config assets:

#### If Using HybridCLR

1. Right-click in Project window
2. Select **Create > CycloneGames > Build > HybridCLR Build Config**
3. Configure HybridCLR-specific settings

#### If Using YooAsset

1. Right-click in Project window
2. Select **Create > CycloneGames > Build > YooAsset Build Config**
3. Configure YooAsset-specific settings (package version, build output, etc.)

#### If Using Addressables

1. Right-click in Project window
2. Select **Create > CycloneGames > Build > Addressables Build Config**
3. Configure Addressables-specific settings (content version, remote catalog, etc.)

#### If Using Obfuz

**Obfuz works for both HybridCLR and non-HybridCLR projects.** The primary control is **BuildData.UseObfuz**.

**For All Projects:**

1. Enable **Use Obfuz** in BuildData (this is the main control switch)
2. Configure ObfuzSettings in Unity Editor (Obfuz menu)
3. The build pipeline will automatically apply obfuscation during build

**Additional Step for HybridCLR Projects:**

- If you're using HybridCLR, you can also enable **Enable Obfuz** in HybridCLRBuildConfig for hot update assembly obfuscation
- **Note**: BuildData.UseObfuz takes priority. If BuildData.UseObfuz is enabled, HybridCLRBuildConfig.enableObfuz is automatically considered enabled

> **Note**: These config assets are optional. The system will use default values if they're not found, but it's recommended to create them for proper configuration.

### Step 4: Build Your Project

Once BuildData is configured, you can build using:

**Unity Editor Menu:**

**Release Builds:**

- **Build > Game(Release) > Build Android APK (IL2CPP)**
- **Build > Game(Release) > Build Windows (IL2CPP)**
- **Build > Game(Release) > Build Mac (IL2CPP)**
- **Build > Game(Release) > Build Linux (IL2CPP)**
- **Build > Game(Release) > Build iOS (IL2CPP)**
- **Build > Game(Release) > Build WebGL**
- **Build > Game(Release) > Export Android Project (IL2CPP)**

**Release Fast Builds (No Clean):**

- **Build > Game(Release) > Fast > Build Android APK (Fast)**
- **Build > Game(Release) > Fast > Build Windows (Fast)**
- **Build > Game(Release) > Fast > Build Mac (Fast)**
- **Build > Game(Release) > Fast > Build Linux (Fast)**
- **Build > Game(Release) > Fast > Build iOS (Fast)**
- **Build > Game(Release) > Fast > Build WebGL (Fast)**
- **Build > Game(Release) > Fast > Export Android Project (Fast)**

**Debug Builds:**

- **Build > Game(Debug) > Build Android APK (Debug)**
- **Build > Game(Debug) > Build Windows (Debug)**
- **Build > Game(Debug) > Build Mac (Debug)**
- **Build > Game(Debug) > Build Linux (Debug)**
- **Build > Game(Debug) > Build iOS (Debug)**
- **Build > Game(Debug) > Build WebGL (Debug)**
- **Build > Game(Debug) > Export Android Project (Debug)**

**Debug Fast Builds (No Clean):**

- **Build > Game(Debug) > Fast > Build Android APK (Debug Fast)**
- **Build > Game(Debug) > Fast > Build Windows (Debug Fast)**
- **Build > Game(Debug) > Fast > Build Mac (Debug Fast)**
- **Build > Game(Debug) > Fast > Build Linux (Debug Fast)**
- **Build > Game(Debug) > Fast > Build iOS (Debug Fast)**
- **Build > Game(Debug) > Fast > Build WebGL (Debug Fast)**
- **Build > Game(Debug) > Fast > Export Android Project (Debug Fast)**

**Debug Information:**

- **Build > Print Debug Info** - Print current build configuration details

**Or use the Hot Update pipeline:**

- **Build > HotUpdate Pipeline > Full Build (Generate Code + Bundles)**
- **Build > HotUpdate Pipeline > Fast Build (Compile Code + Bundles)**

## Core Concepts

### BuildData

`BuildData` is the central configuration asset for the entire build system. It contains:

- **Launch Scene**: Entry point scene for builds
- **Application Version**: Version prefix for automatic versioning
- **Output Base Path**: Base directory for build outputs
- **Feature Flags**: Enable/disable optional features (HybridCLR, Obfuz, Buildalon)
- **Asset Management Selection**: Choose between YooAsset, Addressables, or None

**Key Points:**

- ✅ **Required**: Must exist for any build to work
- ✅ **Single Instance**: Only one BuildData should exist in the project
- ✅ **Auto-Discovery**: System automatically finds BuildData using `AssetDatabase.FindAssets`
- ✅ **Manual Creation**: You must create this asset manually (no auto-generation)

### Version System

The build system uses Git for automatic versioning:

- **Format**: `{ApplicationVersion}.{CommitCount}`
- **Example**: If `ApplicationVersion = "v0.1"` and there are 123 commits, the final version is `v0.1.123`
- **Version Info**: Git commit hash, commit count, and build date are saved to `VersionInfoData` ScriptableObject
- **Runtime Access**: Version information is available at runtime via `VersionInfoData` asset

### Build Scripts

#### BuildScript

The main build script for full application builds. Handles:

- Multi-platform builds (Windows, Mac, Linux, Android, iOS, WebGL)
- Automatic versioning
- Optional HybridCLR code generation
- Optional asset bundle building (YooAsset/Addressables)
- Clean build options (Full Build) and Fast Build options (No Clean)
- Debug build options with Development mode and Profiler support
- Debug file management
- Build configuration debug information (Print Debug Info)

#### HotUpdateBuilder

Unified pipeline for hot update builds. Provides two modes:

- **Full Build**: Complete code generation + asset bundling
  - `HybridCLR -> GenerateAllAndCopy` + `Asset Management -> Build Bundles`
  - Use when C# code structure changes or for clean builds
- **Fast Build**: Quick DLL compilation + asset bundling
  - `HybridCLR -> CompileDLLAndCopy` + `Asset Management -> Build Bundles`
  - Use for rapid iteration when only method implementations change

### Optional Package Integration

The Build system uses reflection to detect and integrate with optional packages:

- **HybridCLR**: Detected via `HybridCLR.Editor.Commands.PrebuildCommand` type
- **Obfuz**: Detected via `Obfuz.Settings.ObfuzSettings` type (base package)
- **Obfuz4HybridCLR**: Detected via `Obfuz4HybridCLR.ObfuscateUtil` type (HybridCLR extension)
- **YooAsset**: Detected via `YooAsset.Editor.AssetBundleBuilder` type
- **Addressables**: Detected via `UnityEditor.AddressableAssets.Build` namespace
- **Buildalon**: Detected via `VirtualMaker.Buildalon` namespace

If a package is not installed, related features are automatically disabled without compilation errors.

## Configuration

### BuildData Configuration

**Location**: Inspector when selecting BuildData asset

**Fields:**

| Field                 | Type       | Description                                           | Required |
| --------------------- | ---------- | ----------------------------------------------------- | -------- |
| Launch Scene          | SceneAsset | Entry point scene for builds                          | ✅ Yes   |
| Application Version   | string     | Version prefix (e.g., "v0.1")                         | ✅ Yes   |
| Output Base Path      | string     | Base directory for outputs (relative to project root) | ✅ Yes   |
| Use Buildalon         | bool       | Enable Buildalon helpers                              | ❌ No    |
| Use HybridCLR         | bool       | Enable HybridCLR code hot-updates                     | ❌ No    |
| Use Obfuz             | bool       | Enable Obfuz code obfuscation                         | ❌ No    |
| Asset Management Type | enum       | None / YooAsset / Addressables                        | ❌ No    |

**Validation:**

The BuildData editor provides real-time validation:

- ✅ Checks if Launch Scene is assigned
- ✅ Validates Application Version format
- ✅ Checks Output Base Path exists or can be created
- ✅ Warns if optional configs are missing when features are enabled
- ✅ Shows helpful messages for each asset management option

### HybridCLR Build Config

**When to Create**: If `Use HybridCLR = true` in BuildData

**Location**: **Create > CycloneGames > Build > HybridCLR Build Config**

**Key Settings:**

**Hot Update Configuration:**

- **Hot Update Assemblies**: Drag `.asmdef` files that need hot updates (required)
- **Hot Update DLL Output Directory**: Where hot update DLLs are copied (required)

**Cheat/Debug DLL Configuration (Optional):**

- **Cheat Assemblies**: Drag `.asmdef` files for cheat/debug modules (optional)
- **Cheat DLL Output Directory**: Where cheat DLLs are copied (optional, recommended if cheat assemblies are configured)

**AOT DLL Configuration:**

- **AOT DLL Output Directory**: Where AOT DLLs are copied for metadata generation (required)

**Obfuz Settings:**

- **Enable Obfuz**: Enable obfuscation for hot update assemblies (optional)

**Key Features:**

- ✅ **Multiple DLL Support**: Configure multiple hot update and cheat assemblies
- ✅ **JSON Lists**: Generates `HotUpdate.bytes` and `Cheat.bytes` list files for runtime loading
- ✅ **Separate Outputs**: HotUpdate, Cheat, and AOT DLLs can be output to different directories

**⚠️ Important Configuration Note:**

**HybridCLR requires manual configuration in its Settings window.**

- ✅ **Configuration Source**: All DLL lists (Hot Update, Cheat, AOT) are configured in `HybridCLRBuildConfig`
- ⚠️ **Manual Setup Required**: You **must** manually configure HybridCLR's Settings to match your `HybridCLRBuildConfig`
- 📋 **How to Configure HybridCLR Settings**:
  1. Open Unity menu: `HybridCLR -> Settings`
  2. In the `Hot Update Assembly Definitions` list, add all `.asmdef` files from your `HybridCLRBuildConfig`
  3. Ensure the asmdefs in HybridCLR Settings exactly match those in your `HybridCLRBuildConfig`
- ✅ **Why Two Configs**: `HybridCLRBuildConfig` is used by the build system to determine which DLLs to copy. HybridCLR's Settings is used by HybridCLR for compilation. Both must match.

**📦 Package Assemblies Handling:**

- ✅ **Only Assets/ folder assemblies can be hot update DLLs**: HybridCLR only compiles assemblies in the `Assets/` folder as hot update DLLs. All Package Manager packages (in `Packages/`, `Library/PackageCache/`, or external paths) are AOT assemblies and cannot be hot-updated.
- ✅ **Package Manager packages are AOT**: These should be preserved via `link.xml` to prevent IL2CPP code stripping, not compiled as hot update DLLs.
- ⚠️ **If you need Package code to be hot-updatable**: Copy the package code into your `Assets/` folder and create a new asmdef for it.

> **⚠️ Important**: You must manually configure HybridCLR's Settings (via `HybridCLR -> Settings` menu) to match the asmdefs in your `HybridCLRBuildConfig`. The build system uses `HybridCLRBuildConfig` to determine which DLLs to copy. Runtime loading uses JSON list files (`HotUpdate.bytes`, `Cheat.bytes`) to load multiple DLLs.

**JSON List File Format:**

The build system generates JSON list files (`.bytes` extension) for runtime DLL loading. The JSON structure is:

```json
{
  "assemblies": [
    "Assets/YourProject/CompiledDLLs/HotUpdate/YourProject.HotUpdate.dll.bytes",
    "Assets/YourProject/CompiledDLLs/HotUpdate/AnotherHotUpdate.dll.bytes"
  ]
}
```

**Runtime Loading Sample Code:**

Here's a sample code snippet showing how to load DLLs from the JSON list files at runtime:

```csharp
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Cysharp.Threading.Tasks;
using CycloneGames.AssetManagement.Runtime;
using UnityEngine;
using YooAsset;

// JSON structure for assembly list
[Serializable]
private class AssemblyList
{
    public List<string> assemblies;
}

// Load HotUpdate DLLs from JSON list
private async UniTask<bool> LoadHotUpdateDllsAsync(IAssetModule yooAssetModule, CancellationToken cancellationToken = default)
{
    try
    {
        var rawFilePackage = yooAssetModule.GetPackage("RawFilePackage");
        if (rawFilePackage == null)
        {
            Debug.LogError("RawFilePackage not found.");
            return false;
        }

        // Load JSON list file (adjust path to match your output directory configuration)
        string listPath = "Assets/YourProject/CompiledDLLs/HotUpdate/HotUpdate.bytes";
        var listHandle = rawFilePackage.LoadRawFileAsync(listPath, cancellationToken);
        await listHandle.Task;

        if (!string.IsNullOrEmpty(listHandle.Error))
        {
            Debug.LogError($"Failed to load list file: {listHandle.Error}");
            listHandle.Dispose();
            return false;
        }

        // Parse JSON
        byte[] listBytes = listHandle.ReadBytes();
        listHandle.Dispose();
        string jsonText = Encoding.UTF8.GetString(listBytes);
        AssemblyList list = JsonUtility.FromJson<AssemblyList>(jsonText);

        if (list == null || list.assemblies == null || list.assemblies.Count == 0)
        {
            Debug.LogError("Assembly list is empty.");
            return false;
        }

        // Load each DLL from the list
        foreach (var dllPath in list.assemblies)
        {
            var dllHandle = rawFilePackage.LoadRawFileAsync(dllPath, cancellationToken);
            await dllHandle.Task;

            if (!string.IsNullOrEmpty(dllHandle.Error))
            {
                Debug.LogError($"Failed to load DLL: {dllPath}, Error: {dllHandle.Error}");
                dllHandle.Dispose();
                continue;
            }

            byte[] dllBytes = dllHandle.ReadBytes();
            dllHandle.Dispose();

            if (dllBytes != null && dllBytes.Length > 0)
            {
                Assembly assembly = Assembly.Load(dllBytes);
                Debug.Log($"Loaded DLL: {assembly.GetName().FullName}");
                // Store assembly reference for later use
            }
        }

        return true;
    }
    catch (Exception ex)
    {
        Debug.LogError($"Exception loading DLLs: {ex.Message}");
        return false;
    }
}
```

> **Note**: This sample code demonstrates the basic loading pattern. Adjust the `listPath` to match your HybridCLR Build Config output directory configuration. In practice, you should handle errors more gracefully, validate DLL formats, and manage assembly references properly.

### YooAsset Build Config

**When to Create**: If `Asset Management Type = YooAsset` in BuildData

**Location**: **Create > CycloneGames > Build > YooAsset Build Config**

**Key Settings:**

- **Package Version**: Version for asset bundles (should match BuildData ApplicationVersion)
- **Build Output Directory**: Where to output asset bundles
- **Copy to StreamingAssets**: Whether to copy bundles to StreamingAssets
- **Copy to Output Directory**: Whether to copy bundles to build output directory

**Version Alignment:**

The YooAsset config editor provides version alignment warnings:

- ⚠️ Warns if Package Version doesn't match BuildData ApplicationVersion
- ✅ Suggests matching versions for consistency
- 💡 Provides quick-fix buttons to align versions

### Addressables Build Config

**When to Create**: If `Asset Management Type = Addressables` in BuildData

**Location**: **Create > CycloneGames > Build > Addressables Build Config**

**Key Settings:**

- **Content Version**: Version for Addressables content (should match BuildData ApplicationVersion)
- **Build Remote Catalog**: Whether to build remote catalog for CDN hosting
- **Copy to Output Directory**: Whether to copy content to build output directory
- **Build Output Directory**: Where to output Addressables content

**Version Alignment:**

Similar to YooAsset, the Addressables config editor provides version alignment warnings and suggestions.

### Obfuz Configuration

**What is Obfuz?**

Obfuz is a code obfuscation tool that protects your C# code by making it harder to reverse-engineer. The Build system integrates Obfuz to automatically obfuscate your code during the build process.

**Two Modes of Operation:**

1. **Non-HybridCLR Projects**: Uses Obfuz's native build pipeline integration. Enable **Use Obfuz** in BuildData to activate.
2. **HybridCLR Projects**: Obfuscates hot update assemblies after compilation, then regenerates method bridges and AOT generic references. Enable **Use Obfuz** in BuildData (and optionally **Enable Obfuz** in HybridCLRBuildConfig).

**Required Packages:**

- **Obfuz** (base package) - Required for all obfuscation
- **Obfuz4HybridCLR** (extension) - Required only for HybridCLR projects

**Configuration Steps:**

**Step 1: Install Obfuz Packages**

Install via Package Manager or Git URL:

- `com.code-philosophy.obfuz`
- `com.code-philosophy.obfuz4hybridclr` (for HybridCLR projects)

**Step 2: Enable in BuildData (Primary Control)**

1. Select your BuildData asset
2. Enable **Use Obfuz** checkbox
3. The system will automatically detect Obfuz packages
4. **This is the main control switch** - Obfuz will be enabled based on this setting

> **Important**: BuildData.UseObfuz is the primary control. For HybridCLR projects, if BuildData.UseObfuz is enabled, HybridCLRBuildConfig.enableObfuz is automatically considered enabled.

**Step 3: Configure ObfuzSettings (Required)**

1. In Unity Editor, go to **Obfuz** menu
2. Open **ObfuzSettings** window
3. Configure assemblies to obfuscate:
   - Add assemblies to `assembliesToObfuscate` (for non-HybridCLR: main assemblies; for HybridCLR: hot update assemblies)
   - Add `Assembly-CSharp` to `NonObfuscatedButReferencingObfuscatedAssemblies` (if it references obfuscated assemblies)
4. Save ObfuzSettings

> **Note**: The Build system automatically configures `Assembly-CSharp` in the reference list, but you should verify this in ObfuzSettings.

**Step 4: For HybridCLR Projects (Optional Additional Control)**

1. Create or select **HybridCLR Build Config** (if not already created)
2. Optionally enable **Enable Obfuz** checkbox (if BuildData.UseObfuz is already enabled, this is automatically considered enabled)
3. Ensure hot update assemblies are configured in ObfuzSettings

**What Happens During Build:**

**For Non-HybridCLR Projects (when BuildData.UseObfuz is enabled):**

1. Build preprocessor configures ObfuzSettings
2. Generates encryption VM and secret key files (if needed)
3. Obfuz's native `ObfuscationProcess` runs during build
4. Code is obfuscated before compilation

**For HybridCLR Projects (when BuildData.UseObfuz is enabled):**

1. Build preprocessor configures ObfuzSettings
2. Generates encryption VM and secret key files (if needed)
3. HybridCLR compiles hot update DLLs
4. **Obfuscates** hot update assemblies using obfuscated DLLs
5. **Regenerates** method bridge and reverse P/Invoke wrapper using obfuscated assemblies
6. **Regenerates** AOT generic reference using obfuscated assemblies
7. Copies obfuscated DLLs to output directory

> **Note**: The control priority is: **BuildData.UseObfuz** > HybridCLRBuildConfig.enableObfuz. If BuildData.UseObfuz is enabled, Obfuz will work regardless of HybridCLRBuildConfig settings.

**Important Notes:**

- ⚠️ **Obfuscation is irreversible**: Always keep unobfuscated backups
- ⚠️ **Test thoroughly**: Obfuscation can break reflection-based code
- ✅ **Automatic prerequisites**: Build system generates encryption VM and secret keys automatically
- ✅ **HybridCLR integration**: Method bridges are regenerated after obfuscation to ensure compatibility

## Build Workflows

### Full Application Build

**Purpose**: Build complete application for distribution

**Workflow:**

1. Load BuildData configuration
2. Generate version information from Git
3. (Optional) Configure ObfuzSettings if Obfuz is enabled
4. (Optional) Run HybridCLR code generation if enabled
5. (Optional) Build asset bundles if asset management is enabled
6. Build Unity player (Obfuz obfuscation runs automatically for non-HybridCLR projects)
7. Save version info to `VersionInfoData` asset
8. (Optional) Copy asset bundles to output directory

**Menu Items:**

**Release Builds:**

- `Build > Game(Release) > Build Android APK (IL2CPP)`
- `Build > Game(Release) > Build Windows (IL2CPP)`
- `Build > Game(Release) > Build Mac (IL2CPP)`
- `Build > Game(Release) > Build Linux (IL2CPP)`
- `Build > Game(Release) > Build iOS (IL2CPP)`
- `Build > Game(Release) > Build WebGL`
- `Build > Game(Release) > Export Android Project (IL2CPP)`

**Release Fast Builds:**

- `Build > Game(Release) > Fast > Build [Platform] (Fast)` - Skips clean build for faster iteration

**Debug Builds:**

- `Build > Game(Debug) > Build [Platform] (Debug)` - Includes Development mode, debugging symbols, and Profiler support

**Debug Fast Builds:**

- `Build > Game(Debug) > Fast > Build [Platform] (Debug Fast)` - Debug build without clean

**Output:**

- Built application in `{OutputBasePath}/{Platform}/{ApplicationName}.{ext}`
- Version info in `Assets/Resources/VersionInfoData.asset`

### Hot Update - Full Build

**Purpose**: Complete hot update build (code generation + asset bundling)

**When to Use:**

- C# code structure has changed (new classes, methods, etc.)
- Need a clean build from scratch
- First time setting up hot updates

**Workflow:**

1. Load BuildData
2. **Obfuz**: Generate prerequisites (encryption VM, secret key, configure settings) if BuildData.UseObfuz is enabled
3. **HybridCLR**: Generate all code and metadata (`GenerateAllAndCopy`)
4. **Obfuz**: Obfuscate hot update assemblies (if BuildData.UseObfuz is enabled and HybridCLR is used)
5. **Obfuz**: Regenerate method bridges and AOT generic references (if obfuscation was applied)
6. **HybridCLR**: Copy DLLs to output directories and generate JSON list files (`HotUpdate.bytes`, `Cheat.bytes`)
7. **Asset Management**: Build all asset bundles
8. Output hot update files

**Menu Item:** `Build > HotUpdate Pipeline > Full Build (Generate Code + Bundles)`

**Output:**

- Hot update DLLs in configured output directory with `HotUpdate.bytes` list file
- Cheat DLLs in configured output directory with `Cheat.bytes` list file (if configured)
- AOT DLLs in configured output directory for metadata generation
- Asset bundles in configured output directory

### Hot Update - Fast Build

**Purpose**: Quick hot update build (DLL compilation + asset bundling)

**When to Use:**

- Only method implementations changed (no structure changes)
- Rapid iteration during development
- Quick bug fixes

**Workflow:**

1. Load BuildData
2. **Obfuz**: Generate prerequisites (encryption VM, secret key, configure settings) if BuildData.UseObfuz is enabled
3. **HybridCLR**: Compile DLLs only (`CompileDLLAndCopy`)
4. **Obfuz**: Obfuscate hot update assemblies (if BuildData.UseObfuz is enabled and HybridCLR is used)
5. **Obfuz**: Regenerate method bridges and AOT generic references (if obfuscation was applied)
6. **HybridCLR**: Copy DLLs to output directories and update JSON list files
7. **Asset Management**: Build asset bundles
8. Output hot update files

**Menu Item:** `Build > HotUpdate Pipeline > Fast Build (Compile Code + Bundles)`

**Output:**

- Compiled hot update DLLs with updated list files
- Updated cheat DLLs (if configured)
- Updated asset bundles

### Build Configuration Debug Info

**Purpose**: Print detailed build configuration information for troubleshooting and verification

**Menu Item:** `Build > Print Debug Info`

**Information Displayed:**

- **Basic Build Configuration**: Application version, output base path, current build target
- **Scene Configuration**: List of build scenes
- **Buildalon Configuration**: Whether Buildalon is enabled
- **HybridCLR Configuration**: HybridCLR status, config asset availability, AOT DLL output directory
- **Obfuz Configuration**: Obfuz status, package availability (base and HybridCLR extension), effective obfuscation state
- **Asset Management Configuration**: Selected asset management system (YooAsset/Addressables/None) and config asset availability
- **Version Control Configuration**: Version control type, commit hash, commit count, full build version
- **Build Target Configuration**: Current build target, scripting backend, API compatibility level

**Use Cases:**

- Verify build configuration before building
- Troubleshoot missing config assets
- Check package availability
- Verify feature enablement status
- Debug configuration mismatches

### Standalone Build Operations

You can also run individual build operations:

**HybridCLR:**

- `Build > HybridCLR > Generate All`

**YooAsset:**

- `Build > YooAsset > Build Bundles (From Config)`

**Addressables:**

- `Build > Addressables > Build Content (From Config)`

## CI/CD Integration

The Build system provides command-line interfaces for CI/CD integration.

### Command-Line Build

**Full Application Build:**

```bash
# Basic build
-executeMethod Build.Pipeline.Editor.BuildScript.PerformBuild_CI -buildTarget Android -output Build/Android/MyGame.apk

# With options
-executeMethod Build.Pipeline.Editor.BuildScript.PerformBuild_CI \
  -buildTarget Android \
  -output Build/Android/MyGame.apk \
  -clean \
  -buildHybridCLR \
  -buildYooAsset

# With version override
-executeMethod Build.Pipeline.Editor.BuildScript.PerformBuild_CI \
  -buildTarget StandaloneWindows64 \
  -output Build/Windows/MyGame.exe \
  -clean \
  -version v1.0.0
```

**Parameters:**

| Parameter            | Type        | Description                                                                                  | Required |
| -------------------- | ----------- | -------------------------------------------------------------------------------------------- | -------- |
| `-buildTarget`       | BuildTarget | Target platform (Android, StandaloneWindows64, StandaloneOSX, StandaloneLinux64, iOS, WebGL) | ✅ Yes   |
| `-output`            | string      | Output path (relative to project root)                                                       | ✅ Yes   |
| `-clean`             | flag        | Clean build (delete previous build)                                                          | ❌ No    |
| `-buildHybridCLR`    | flag        | Run HybridCLR generation                                                                     | ❌ No    |
| `-buildYooAsset`     | flag        | Build YooAsset bundles                                                                       | ❌ No    |
| `-buildAddressables` | flag        | Build Addressables content                                                                   | ❌ No    |
| `-version`           | string      | Override version (default: from Git)                                                         | ❌ No    |

**Hot Update Build:**

```bash
# Full hot update build
-executeMethod Build.Pipeline.Editor.HotUpdateBuilder.FullBuild

# Fast hot update build
-executeMethod Build.Pipeline.Editor.HotUpdateBuilder.FastBuild
```

### CI/CD Examples

**GitHub Actions:**

```yaml
name: Build Game

on:
  push:
    branches: [main]

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3

      - name: Setup Unity
        uses: game-ci/unity-builder@v4
        with:
          targetPlatform: Android
          buildMethod: Build.Pipeline.Editor.BuildScript.PerformBuild_CI
          buildArgs: -buildTarget Android -output Build/Android/MyGame.apk -clean -buildHybridCLR -buildYooAsset
```

**Jenkins:**

```groovy
pipeline {
    agent any

    stages {
        stage('Build') {
            steps {
                sh '''
                    Unity -batchmode -quit -projectPath . \
                    -executeMethod Build.Pipeline.Editor.BuildScript.PerformBuild_CI \
                    -buildTarget Android \
                    -output Build/Android/MyGame.apk \
                    -clean \
                    -buildHybridCLR \
                    -buildYooAsset
                '''
            }
        }
    }
}
```

## Troubleshooting

### BuildData Not Found

**Error**: `BuildData not found. Please create a BuildData asset.`

**Solution:**

1. Create BuildData asset: **Create > CycloneGames > Build > BuildData**
2. Ensure only one BuildData exists in the project
3. The system uses `AssetDatabase.FindAssets` to find BuildData - ensure it's in a location that Unity can index

### Config Asset Not Found

**Error**: `YooAssetBuildConfig not found` or similar

**Solution:**

1. Create the required config asset (YooAssetBuildConfig, AddressablesBuildConfig, or HybridCLRBuildConfig)
2. Or disable the related feature in BuildData if you don't need it
3. The system will use default values if configs are missing, but some features may not work correctly

### Version Mismatch Warnings

**Warning**: Version mismatch between BuildData and config assets

**Solution:**

1. Align versions: Set config asset version to match BuildData ApplicationVersion
2. Use the quick-fix buttons in config editors (if available)
3. Or manually update versions for consistency

### HybridCLR Not Found

**Warning**: `HybridCLR package not found. Skipping generation.`

**Solution:**

1. Install HybridCLR package if you need code hot-updates
2. Or disable `Use HybridCLR` in BuildData if you don't need it
3. The build will continue without HybridCLR features

### HybridCLR Configuration Issues

**Warning**: `HybridCLRBuildConfig not found` or missing required settings

**Solution:**

1. Create HybridCLR Build Config: **Create > CycloneGames > Build > HybridCLR Build Config**
2. Configure **Hot Update Assemblies** (required): Drag `.asmdef` files that need hot updates
3. Configure **Hot Update DLL Output Directory** (required): Drag output folder
4. Configure **AOT DLL Output Directory** (required): Drag folder for AOT metadata DLLs
5. Optionally configure **Cheat Assemblies** and **Cheat DLL Output Directory** for debug modules
6. **Manually configure HybridCLR Settings**: Open `HybridCLR -> Settings` and add all asmdefs from your `HybridCLRBuildConfig` to the `Hot Update Assembly Definitions` list

**⚠️ Important**: Configure DLL lists in `HybridCLRBuildConfig` first, then manually ensure HybridCLR Settings (via `HybridCLR -> Settings` menu) matches. The build system uses `HybridCLRBuildConfig` to determine which DLLs to copy, while HybridCLR uses its Settings for compilation.

### Obfuz Not Found

**Warning**: `Obfuz package not found. Skipping obfuscation.`

**Solution:**

1. Install Obfuz packages if you need code obfuscation:
   - `com.code-philosophy.obfuz` (base package, required)
   - `com.code-philosophy.obfuz4hybridclr` (for HybridCLR projects, required)
2. Or disable `Use Obfuz` in BuildData if you don't need it
3. The build will continue without obfuscation

### Obfuz Configuration Issues

**Warning**: Obfuz obfuscation failed or assemblies not configured

**Solution:**

1. Verify **Use Obfuz** is enabled in BuildData (this is the primary control)
2. Open **Obfuz > ObfuzSettings** in Unity Editor
3. Verify assemblies are added to `assembliesToObfuscate`
4. Ensure `Assembly-CSharp` is in `NonObfuscatedButReferencingObfuscatedAssemblies` if needed
5. Check that encryption VM and secret key files are generated (Obfuz menu)
6. For HybridCLR: If BuildData.UseObfuz is enabled, HybridCLRBuildConfig.enableObfuz is automatically considered enabled

### Asset Management Package Not Found

**Warning**: Asset management package (YooAsset/Addressables) not found

**Solution:**

1. Install the required package (YooAsset or Addressables)
2. Or set `Asset Management Type = None` in BuildData
3. Ensure the package is properly imported and accessible

### Build Output Directory Issues

**Error**: Cannot create or access build output directory

**Solution:**

1. Check `Output Base Path` in BuildData
2. Ensure the path is relative to project root (e.g., `Build`, not `C:/Build`)
3. Ensure you have write permissions to the project directory
4. Check for invalid characters in the path

### Git Version Information Missing

**Warning**: Cannot get Git version information

**Solution:**

1. Ensure Git is installed and accessible from command line
2. Ensure the project is in a Git repository
3. Check Git is in your system PATH
4. Version will fall back to a default if Git is unavailable

### Scene Not Found

**Error**: `Invalid scene list, please check BuildData configuration.`

**Solution:**

1. Assign a Launch Scene in BuildData
2. Ensure the scene exists and is not deleted
3. Check scene is added to Build Settings (though BuildData takes precedence)

## Best Practices

### 1. Single BuildData Instance

- ✅ Create only **one** BuildData asset per project
- ✅ Place it in a logical location (e.g., `Assets/Config/BuildData.asset`)
- ✅ Use descriptive naming if you have multiple projects in one Unity instance

### 2. Version Alignment

- ✅ Keep BuildData ApplicationVersion aligned with config asset versions
- ✅ Use semantic versioning (e.g., `v1.0`, `v1.1`, `v2.0`)
- ✅ Let the system append commit count for uniqueness

### 3. Config Asset Organization

- ✅ Create config assets in the same directory as BuildData
- ✅ Use descriptive names (e.g., `YooAssetBuildConfig_Production.asset`)
- ✅ Document any project-specific configurations

### 4. CI/CD Setup

- ✅ Use command-line methods for CI/CD
- ✅ Set up proper build targets and output paths
- ✅ Test builds locally before setting up CI/CD
- ✅ Use version overrides only when necessary

### 5. Hot Update Workflow

- ✅ Use **Full Build** for structure changes or clean builds
- ✅ Use **Fast Build** for rapid iteration
- ✅ Configure all required output directories in HybridCLR Build Config
- ✅ Manually configure HybridCLR Settings (via `HybridCLR -> Settings`) to match your `HybridCLRBuildConfig`
- ✅ JSON list files (`HotUpdate.bytes`, `Cheat.bytes`) are generated automatically
- ✅ Test hot updates in development before production
- ✅ Keep hot update files organized and versioned

### 6. Optional Packages

- ✅ Only install packages you actually need
- ✅ The system gracefully handles missing packages
- ✅ Test builds with and without optional packages
- ✅ Document which packages are required for your project

## Additional Resources

- **HybridCLR Documentation**: [HybridCLR GitHub](https://github.com/focus-creative-games/hybridclr)
- **Obfuz Documentation**: [Obfuz GitHub](https://github.com/Code-Philosophy/Obfuz)
- **Obfuz4HybridCLR Documentation**: [Obfuz4HybridCLR GitHub](https://github.com/Code-Philosophy/Obfuz4HybridCLR)
- **YooAsset Documentation**: [YooAsset GitHub](https://github.com/tuyoogame/YooAsset)
- **Addressables Documentation**: [Unity Addressables Manual](https://docs.unity3d.com/Packages/com.unity.addressables@latest)
- **Buildalon Documentation**: [Buildalon GitHub](https://github.com/virtualmaker/Buildalon)

## Module Structure

```
Assets/Build/
├── Editor/
│   ├── BuildPipeline/
│   │   ├── BuildData.cs              # Central configuration
│   │   ├── BuildDataEditor.cs        # BuildData inspector
│   │   ├── BuildScript.cs            # Full app build
│   │   ├── HotUpdateBuilder.cs       # Hot update pipeline
│   │   ├── HybridCLR/                # HybridCLR integration
│   │   ├── Obfuz/                    # Obfuz obfuscation integration
│   │   ├── YooAsset/                 # YooAsset integration
│   │   ├── Addressables/             # Addressables integration
│   │   ├── Buildalon/                # Buildalon integration
│   │   └── _Common/                  # Shared utilities
│   └── VersionControl/               # Version control providers
└── Runtime/
    └── Data/
        └── VersionInfoData.cs        # Runtime version info
```

---
