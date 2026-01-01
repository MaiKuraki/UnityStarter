# Unity Project Starter Template

A production-ready, modular Unity project template that provides a solid foundation for game development. Inspired by **Unreal Engine** architecture patterns, this template integrates proven gameplay systems, high-performance infrastructure, and modern development workflows.

<p align="left"><br> English | <a href="README.SCH.md">简体中文</a></p>

> [!NOTE]
>
> If you find this project helpful, please consider giving it a star ⭐. Thank you!

![Unity](https://img.shields.io/badge/Unity-2022.3%20LTS-black?logo=unity)
![Unity6](https://img.shields.io/badge/Unity-6000.3%20LTS-black?logo=unity)
![License](https://img.shields.io/badge/License-MIT-green.svg)
[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/MaiKuraki/UnityStarter)

## Table of Contents

1. [Overview](#overview)
2. [Key Features](#key-features)
3. [Architecture](#architecture)
4. [Module Catalog](#module-catalog)
5. [Getting Started](#getting-started)
6. [Technology Stack](#technology-stack)
7. [Related Projects](#related-projects)

## Overview

This template is designed for developers who want to start with a professional, battle-tested foundation rather than building everything from scratch. It provides:

- **Modular Architecture**: All systems are decoupled Unity Packages with independent Assembly Definitions
- **Unreal Engine Patterns**: Proven architecture concepts (Gameplay Framework, GAS, Gameplay Tags)
- **Performance-First**: Zero/low-GC systems for critical paths
- **Production-Ready**: Tested in commercial projects, CI/CD ready, cross-platform optimized
- **Developer-Friendly**: Comprehensive documentation, clear examples, flexible DI/IoC support

### What This Template Provides

- ✅ Complete gameplay framework (Actor/Pawn/Controller/GameMode pattern)
- ✅ Data-driven ability system (GAS-inspired)
- ✅ High-performance infrastructure (logging, pooling, audio)
- ✅ Hot update solution (code + assets)
- ✅ Code obfuscation integration (Obfuz) for code protection
- ✅ Build pipeline with CI/CD integration
- ✅ Modern input system with context stacks
- ✅ UI framework with hierarchical management and MVP architecture support

> **📖 Documentation**: Each module has comprehensive documentation. See the [Module Catalog](#module-catalog) section for links to detailed guides.

## Key Features

### Modular Design

Every system is a self-contained Unity Package. Import only what you need, remove what you don't. Each module has:

- Independent Assembly Definition (asmdef)
- Complete package.json configuration
- Comprehensive documentation
- Sample implementations

### Unreal Engine-Inspired Architecture

Implements proven patterns from Unreal Engine:

- **Gameplay Framework**: Actor/Pawn/Controller separation for scalable game architecture
- **Gameplay Ability System**: Data-driven abilities, attributes, and effects
- **Gameplay Tags**: Hierarchical tag system for decoupled game logic

### Performance-Oriented

Critical systems are optimized for GC:

- **Logger**: Zero-GC multi-threaded logging with file rotation
- **Factory**: High-performance object pooling with O(1) operations
- **Audio**: Low-GC audio management with Wwise-like API

### Hot Update Ready

Complete solution for updating games without app store resubmission:

- **HybridCLR**: C# code hot-updates via DLL compilation
- **Asset Management**: YooAsset or Addressables for asset hot-updates
- **Code Protection**: Integrated Obfuz obfuscation for hot update assemblies
- **Unified Pipeline**: Streamlined build workflow for rapid iteration

### DI/IoC Support

Pre-configured adapters for popular dependency injection frameworks:

> [!NOTE]
>
> All **listed frameworks have been** tested in production environments.

- [VContainer](https://github.com/hadashiA/VContainer) (Recommended)
- [StrangeIoC](https://github.com/strangeioc/strangeioc)
- [Extenject (Zenject)](https://github.com/Mathijs-Bakker/Extenject) (No longer actively maintained)

> **Note**: Switch between Git branches to see implementation examples for each DI framework. The **GameplayFramework** and **Factory** modules include DI samples.

<img src="./Docs/ProjectDescription/Main/Des_01.png" alt="Branch Select" style="width: 50%; height: auto; max-width: 360px;" />

### CI/CD Integration

Command-line build interface for automated pipelines:

- Automatic versioning from Git
- Multi-platform builds (Windows, Mac, Android, WebGL)
- Hot update build workflows with optional code obfuscation
- Integration with Jenkins, TeamCity, GitHub Actions

## Architecture

### Project Structure

```
.
├── Docs/                          # Project documentation
├── Tools/                         # Utility scripts (renaming, cleanup, etc.)
└── UnityStarter/                  # Unity project root
    ├── Assets/
    │   ├── Build/                 # Build pipeline & hot update
    │   │   └── [See Build/README.md for details]
    │   └── ThirdParty/
    │       └── CycloneGames/      # Core framework modules
    │           └── [Each module has its own README]
    ├── Packages/                  # Package manifests
    └── ProjectSettings/           # Unity settings
```

### Module Organization

All modules follow the same structure:

- **Runtime/**: Core functionality
- **Editor/**: Editor tools and utilities
- **Samples/**: Example implementations
- **README.md / README.SCH.md**: Comprehensive documentation

### Dependency Management

Modules are designed to be:

- **Loosely Coupled**: Minimal dependencies between modules
- **Optional**: Most modules can work independently
- **Composable**: Mix and match based on your needs

## Module Catalog

> **📚 Important**: Each module has detailed documentation in its directory. Click the module name to view its README, or navigate to `{ModulePath}/README.md` for comprehensive guides.

### 🎮 Gameplay Systems

| Module                | Path                             | Description                                                                                                              | Documentation                                                                                     |
| --------------------- | -------------------------------- | ------------------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------- |
| **GameplayFramework** | `CycloneGames.GameplayFramework` | UE-style gameplay architecture (Actor/Pawn/Controller/GameMode). DI-friendly, scalable foundation for game projects.     | [README.md](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayFramework/README.md) |
| **GameplayAbilities** | `CycloneGames.GameplayAbilities` | Data-driven ability system (GAS-inspired). ScriptableObject-based abilities, attributes, effects, and status management. | [README.md](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayAbilities/README.md) |
| **GameplayTags**      | `CycloneGames.GameplayTags`      | Hierarchical tag system for decoupled game logic. Runtime registration, auto-generation, and tag-based queries.          | [README.md](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayTags/README.md)      |
| **RPGFoundation**     | `CycloneGames.RPGFoundation`     | RPG-specific extensions (movement, combat, etc.). Foundation components for RPG-type games.                              | [See module directory](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.RPGFoundation)    |
| **BehaviorTree**      | `CycloneGames.BehaviorTree`      | AI behavior tree system. Visual editor, ScriptableObject-based, optimized for mobile devices.                            | [README.md](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.BehaviorTree/README.md)      |

### 🏗️ Core Infrastructure

| Module              | Path                           | Description                                                                                                    | Documentation                                                                                   |
| ------------------- | ------------------------------ | -------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------- |
| **Factory**         | `CycloneGames.Factory`         | High-performance object pooling. Thread-safe, auto-scaling pools with O(1) operations. Zero-GC allocation.     | [README.md](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Factory/README.md)         |
| **Logger**          | `CycloneGames.Logger`          | Zero-GC logging system. Multi-threaded, file rotation, cross-platform (including WebGL). Pluggable processors. | [README.md](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Logger/README.md)          |
| **AssetManagement** | `CycloneGames.AssetManagement` | DI-first asset loading abstraction. YooAsset integration, Addressables compatibility, version management.      | [README.md](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.AssetManagement/README.md) |
| **Audio**           | `CycloneGames.Audio`           | High-performance audio management. Wwise-like API, low-GC, advanced features using Unity native audio.         | [README.md](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Audio/README.md)           |

### 🎯 Input & UI

| Module          | Path                       | Description                                                                                                                 | Documentation                                                                               |
| --------------- | -------------------------- | --------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------- |
| **InputSystem** | `CycloneGames.InputSystem` | Reactive input wrapper with context stacks. Local co-op support, device auto-detection, runtime YAML keybind configuration. | [README.md](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.InputSystem/README.md) |
| **UIFramework** | `CycloneGames.UIFramework` | Hierarchical UI management. Layer-based, MVP architecture, transitions, asset integration.                                  | [README.md](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.UIFramework/README.md) |

### 🛠️ Utilities & Services

| Module         | Path                      | Description                                                                                                     | Documentation                                                                         |
| -------------- | ------------------------- | --------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------- |
| **Utility**    | `CycloneGames.Utility`    | Common utilities. FPS counter, safe area fitting, file operations, performance tools, splash screen control.    | See module directory                                                                  |
| **Service**    | `CycloneGames.Services`   | Game service abstractions. Camera management, graphics settings, device configuration with YAML-based settings. | See module directory                                                                  |
| **Cheat**      | `CycloneGames.Cheat`      | Type-safe debug command pipeline. VitalRouter integration, async operations, thread-safe execution.             | [README.md](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Cheat/README.md) |
| **FontAssets** | `CycloneGames.FontAssets` | Multilingual font collections. Latin, Chinese (Simplified/Traditional), Japanese, Korean character sets.        | See module directory                                                                  |

### 🔧 Build & Deployment

| Module    | Path           | Description                                                                                                                                      | Documentation                                    |
| --------- | -------------- | ------------------------------------------------------------------------------------------------------------------------------------------------ | ------------------------------------------------ |
| **Build** | `Assets/Build` | Comprehensive build pipeline. HybridCLR + Obfuz + asset management integration. Full app builds, hot updates with code obfuscation, CI/CD ready. | [README.md](UnityStarter/Assets/Build/README.md) |

### 🌐 Networking

| Module         | Path                      | Description                                                                                                | Documentation        |
| -------------- | ------------------------- | ---------------------------------------------------------------------------------------------------------- | -------------------- |
| **Networking** | `CycloneGames.Networking` | Network abstraction layer. Mirror adapter, transport/serialization interfaces, ability system integration. | See module directory |

### 🧰 Tools

| Module    | Path     | Description                                                                  | Documentation                |
| --------- | -------- | ---------------------------------------------------------------------------- | ---------------------------- |
| **Tools** | `Tools/` | Project utilities. Renaming tool, cleanup scripts, common development tasks. | [README.md](Tools/README.md) |

## Getting Started

### Prerequisites

- **Unity 2022.3 LTS or later**
- **Git** (for automatic versioning in Build module)
- Basic familiarity with Unity and C#

### Step 1: Clone the Repository

```bash
git clone https://github.com/MaiKuraki/UnityStarter.git
```

### Step 2: Rename the Project (Optional)

If using as a full project template:

1. Locate `Tools/Executable/rename_project` executable
2. Copy it to `UnityStarter/` directory
3. Run from command line - it will guide you through:
   - Project folder renaming
   - Company name updates
   - Application name changes
   - Configuration file updates

### Step 3: Open in Unity

1. Open Your Project from UnityHub

### Step 4: Explore Modules

1. **Start with Core Modules**: Begin with `GameplayFramework`
2. **Read Documentation**: Each module has a `README.md` in its directory
3. **Check Samples**: Most modules include sample scenes and scripts
4. **Configure Build**: See [Build Module Documentation](UnityStarter/Assets/Build/README.md) for setup

### Step 5: Import Specific Modules (For Existing Projects)

If you only need specific modules:

**Recommended Method (Package Manager):**

1. Copy module folder outside your `Assets` directory
2. In Unity: **Window > Package Manager**
3. Click **+ > Add package from disk...**
4. Select the module's `package.json` file

**Simple Method (Direct Copy):**

1. Copy module folder from `UnityStarter/Assets/ThirdParty/CycloneGames/`
2. Paste into your project's `Assets` folder

> **💡 Tip**: Check each module's README for specific setup instructions and dependencies.

## Technology Stack

### Core Dependencies

- **Unity**: 2022.3 LTS+
- **UniTask**: Async/await for Unity ([GitHub](https://github.com/Cysharp/UniTask))
- **R3**: Reactive programming ([GitHub](https://github.com/Cysharp/R3))
- **LitMotion**: Animation/tweening ([GitHub](https://github.com/annulusgames/LitMotion))
- **VYaml**: YAML serialization ([GitHub](https://github.com/hadashiA/VYaml))
- **VitalRouter**: Message bus ([GitHub](https://github.com/hadashiA/VitalRouter))

### Optional Dependencies

- **HybridCLR**: C# hot-update ([GitHub](https://github.com/focus-creative-games/hybridclr))
- **Obfuz**: Code obfuscation ([GitHub](https://github.com/Code-Philosophy/Obfuz))
- **Obfuz4HybridCLR**: Obfuz extension for HybridCLR ([GitHub](https://github.com/Code-Philosophy/Obfuz4HybridCLR))
- **YooAsset**: Asset management ([GitHub](https://github.com/tuyoogame/YooAsset))
- **Addressables**: Unity's asset management (via Package Manager)
- **Mirror**: Networking ([GitHub](https://github.com/MirrorNetworking/Mirror))
- **Navigathena**: Scene management ([GitHub](https://github.com/mackysoft/Navigathena))
- **MessagePack**: Binary serialization ([GitHub](https://github.com/MessagePack-CSharp/MessagePack-CSharp))

> See `UnityStarter/Packages/manifest.json` for complete dependency list.

## Related Projects

Projects built using this template:

- **[Rhythm Pulse](https://github.com/MaiKuraki/RhythmPulse)** - Collection of rhythm game mechanics and gameplay types
- **[Unity Gameplay Ability System Sample](https://github.com/MaiKuraki/UnityGameplayAbilitySystemSample)** - Example project demonstrating GAS implementation

---

## Documentation Guide

> **📚 Each module has comprehensive documentation in its directory.**

### How to Find Module Documentation

1. **Navigate to Module Directory**: `UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.{ModuleName}/`
2. **Look for README Files**:
   - `README.md` - English documentation
   - `README.SCH.md` - Simplified Chinese documentation
3. **Check Samples Folder**: Most modules include example implementations

### Key Documentation Links

- **[GameplayFramework](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayFramework/README.md)** - Complete guide to Actor/Pawn/Controller architecture
- **[GameplayAbilities](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayAbilities/README.md)** - GAS system tutorial with step-by-step examples
- **[Build](UnityStarter/Assets/Build/README.md)** - Build pipeline setup and CI/CD integration
- **[InputSystem](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.InputSystem/README.md)** - Input system configuration and usage
- **[UIFramework](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.UIFramework/README.md)** - UI framework architecture and examples
- **[AssetManagement](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.AssetManagement/README.md)** - Asset loading and version management

---

**License**: See [LICENSE](LICENSE) file for details.

**Support**: For questions and discussions, please open an issue on GitHub.
