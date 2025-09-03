# Unity Project Starter Template

This project is a lightweight Unity starter template designed to serve as a foundation for new projects, integrating commonly used packages and providing support for various DI/IoC frameworks.

<p align="left"><br> English | <a href="README.SCH.md">简体中文</a></p>

[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/MaiKuraki/UnityStarter)

## Overview
A lightweight Unity starter template designed to serve as a foundation for new projects. This repository integrates:

- **Commonly used Unity Packages** (updated to recent versions).
- **DI/IoC framework support** with pre-configured adapters for: (All **listed frameworks have been** tested in production environments.)
  - [VContainer](https://github.com/hadashiA/VContainer)
  - [StrangeIoC](https://github.com/strangeioc/strangeioc)
  - [Extenject(Zenject)](https://github.com/Mathijs-Bakker/Extenject) (no longer actively maintained.)
> Since the author of [**Zenject**](https://github.com/Mathijs-Bakker/Extenject) announced the [discontinuation of project updates](https://github.com/Mathijs-Bakker/Extenject/issues/73), I recommend trying [**VContainer**](https://github.com/hadashiA/VContainer). If you prefer a higher degree of customization for your project, [**StrangeIoC**](https://github.com/strangeioc/strangeioc) is a better choice. If you decide to use [**Zenject**](https://github.com/Mathijs-Bakker/Extenject), [**MessagePipe**](https://github.com/Cysharp/MessagePipe) is a compatible messaging framework that works well with it.
## DI Framework Selection
Switch between Git branches to explore implementation examples for each DI framework.</br>
Note: The **GameplayFramework** and **Factory** modules **include** DI samples.</br>
<img src="./Docs/ProjectDescription/Main/Des_01.png" alt="Branch Select" style="width: 50%; height: auto; max-width: 360px;" />

## Usage

This repository can be used in two primary ways: as a complete template for a new project, or as a source of individual modules to be imported into an existing project.

### As a Full Project Template
If you intend to use this repository as a starter for a new Unity project, follow these steps to rename it:

1.  **Locate the Renaming Tool**: Find the `rename_project` Go script or `.exe` executable inside the `Tools/` directory.
2.  **Move the Renaming Tool**: Copy the `rename_project` script or executable into the `UnityStarter/` directory.
3.  **Run the Tool**: Execute the script. It will automatically update project-specific components like namespaces and assembly definitions.
4.  **Rename Manually**: Finally, manually rename the `UnityStarter/` root folder to your desired project name.

### Using Specific Modules
If you only need specific modules for your project, you have two options:

- **Simple Method**: Navigate to `Assets/ThirdParty/CycloneGames/` and simply delete the package folders you do not need.
- **Recommended Method (Production)**: For a production environment, it is best practice to move the required packages from `Assets/ThirdParty/CycloneGames/` to a location *outside* your project's `Assets` folder. Then, add them to your project via the Unity Package Manager using the **"Add package from disk..."** option. This approach keeps your project clean and highly modular.
## Core Framework Modules

### 🎮 Gameplay Systems
- **[GameplayFramework](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayFramework)** - UnrealEngine-style gameplay framework with Actor, Pawn, Controller, GameMode concepts. DI-friendly architecture for scalable game projects.
- **[GameplayAbilities](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayAbilities)** - Powerful data-driven ability system inspired by Unreal Engine's GAS. Supports complex skills, attributes, status effects with ScriptableObject-based design.
- **[GameplayTags](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayTags)** - Tag-based identification system for abilities, effects, and game states, inspired by Unreal Engine's GameplayTags. Supports dynamic runtime tag registration and auto-generation.
- **[RPGFoundation](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.RPGFoundation)** - Contains basic extensions for RPG-type games.

### 🏗️ Core Infrastructure  
- **[Factory](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Factory)** - High-performance, low-GC factory and object pooling utilities. Thread-safe auto-scaling pools with O(1) operations.
- **[Logger](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Logger)** - Zero/low-GC logging system with pluggable processing strategies. Supports threaded workers, file rotation, and cross-platform compatibility (including WebGL).
- **[AssetManagement](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.AssetManagement)** - DI-first asset management abstraction with YooAsset integration. Supports downloading, caching, version management with Addressables/Navigathena compatibility.
- **[Audio](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Audio)** - A high-performance, low-GC, advanced feature extension using Unity's native audio functions, with a Wwise-like operating experience.

### 🎯 Input & UI
- **[InputSystem](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.InputSystem)** - Reactive input wrapper with context stacks, multi-player support, device locking, and YAML-based configuration. Built with R3 Observables.
- **[UIFramework](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.UIFramework)** - Hierarchical UI management system with layer-based organization, transitions, and asset integration support.

### 🛠️ Utilities & Services
- **[Utility](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Utility)** - Common utilities including FPS counter, safe area fitting, file operations, performance tools, and Unity splash screen control.
- **[Services](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Services)** - Game service abstractions for camera management, graphics settings, and device configuration with YAML-based settings.
- **[Cheat](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Cheat)** - Type-safe command pipeline for debugging with VitalRouter integration. Supports async operations and thread-safe execution.
- **[FontAssets](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.FontAssets)** - Multilingual font collections and character sets for Latin, Chinese (Simplified/Traditional), Japanese, and Korean localization.

### 🌐 Networking
- **[Networking](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Networking)** - Networking abstraction layer with Mirror adapter. Provides interfaces for transport, serialization, and ability system integration.

## Project Structure
The main source code for the modules is located in the `UnityStarter/Assets/ThirdParty/` directory. The project is developed using a Unity Package-based approach with separated Assembly Definitions (asmdef), which allows for easy removal of unwanted modules and ensures a clear separation of concerns.

```
.
├── Docs/                               # Documentation
├── Tools/                              # Utility tools (project renaming, cleanup, etc.)
└── UnityStarter/                       # Unity project root
    ├── Assets/
    │   ├── Editor/
    │   │   ├── BuildScript.cs          # Build tools for CI/CD
    │   │   └── ...
    │   ├── ThirdParty/
    │   │   ├── CycloneGames/           # Core development suite
    │   │   │   ├── Cheat/              # Debug command pipeline system
    │   │   │   ├── Factory/            # High-performance object pooling
    │   │   │   ├── GameplayAbilities/  # Data-driven ability system (UnrealEngine GAS-inspired)
    │   │   │   ├── GameplayFramework/  # UE-style gameplay architecture (UnrealEngine GameplayFramework-inspired)
    │   │   │   ├── GameplayTags/       # Tag-based identification system (UnrealEngine GameplayTags-inspired)
    │   │   │   ├── InputSystem/        # Reactive input management with context stacks
    │   │   │   ├── Logger/             # Zero-GC multi-threaded logging
    │   │   │   ├── AssetManagement/    # Asset loading and version management
    │   │   │   ├── Services/           # Common game service abstractions
    │   │   │   ├── UIFramework/        # Hierarchical UI management
    │   │   │   ├── Networking/         # Network abstraction layer
    │   │   │   ├── FontAssets/         # Multilingual font collections
    │   │   │   ├── Audio/              # Enhanced audio management system
    │   │   │   ├── RPGFoundation/      # RPG Foundation components (e.g., Movement)
    │   │   │   └── Utility/            # Performance tools and utilities
    │   │   └── ...
    │   └── ...
    ├── Packages/                       # Package manifests and configurations
    └── ProjectSettings/                # Unity project settings
```

## Technical Features & Dependencies

### Key Technology Stack
- **Unity 2022.3+** - Required Unity version for all modules
- **UniTask** - Async/await support for Unity operations
- **R3** - Reactive Extensions for Unity (used in InputSystem)
- **VYaml** - YAML serialization for configuration files
- **VitalRouter** - Message routing system (used in Cheat system)
- **YooAsset** - Asset management and hot-update support
- **Mirror** - Networking framework adapter

### Architecture Highlights
- **Dependency Injection Ready**: All modules support VContainer, StrangeIoC, and Zenject
- **Assembly Definition Isolation**: Each module has its own asmdef for clean separation
- **ScriptableObject Configuration**: Data-driven design for abilities, effects, and settings
- **Thread-Safe Design**: Logger and Factory modules designed for multi-threaded operations
- **Zero/Low-GC**: Performance-optimized with minimal garbage collection
- **Cross-Platform**: Supports desktop, mobile, and WebGL deployment

## Other Open Source Projects Based on This Project
- [x] [Rhythm Pulse](https://github.com/MaiKuraki/RhythmPulse)
A collection of all common rhythm game mechanics and gameplay types. This project is currently in active development.
- [x] [Unity Gameplay Ability System Sample](https://github.com/MaiKuraki/UnityGameplayAbilitySystemSample)
An example project of a UE-like GAS (Gameplay Ability System) for Unity.