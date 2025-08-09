# Unity Project Starter Template

This project is a lightweight Unity starter template designed to serve as a foundation for new projects, integrating commonly used packages and providing support for various DI/IoC frameworks.

<p align="center">
    <br> English | <a href="README.SCH.md">简体中文</a>
</p>

## Overview
A lightweight Unity starter template designed to serve as a foundation for new projects. This repository integrates:

- **Commonly used Unity Packages** (updated to recent versions).
- **DI/IoC framework support** with pre-configured adapters for: (All framework listed are tested in production environments.)
  - [VContainer](https://github.com/hadashiA/VContainer)
  - [StrangeIoC](https://github.com/strangeioc/strangeioc)
  - [Extenject(Zenject)](https://github.com/Mathijs-Bakker/Extenject) (no longer actively maintained.)
> Since the author of [**Zenject**](https://github.com/Mathijs-Bakker/Extenject) announced the [discontinuation of project updates](https://github.com/Mathijs-Bakker/Extenject/issues/73), I recommend trying [**VContainer**](https://github.com/hadashiA/VContainer). If you prefer a higher degree of customization for your project, [**StrangeIoC**](https://github.com/strangeioc/strangeioc) is a better choice. If you decide to use [**Zenject**](https://github.com/Mathijs-Bakker/Extenject), [**MessagePipe**](https://github.com/Cysharp/MessagePipe) is a compatible messaging framework that works well with it.
## DI Framework Selection
Switch between Git branches to explore implementation examples for each DI framework.</br>
Note: **GameplayFramework** and **Factory** have DI sample.</br>
<img src="./Docs/ProjectDescription/Main/Des_01.png" alt="Branch Select" style="width: 50%; height: auto; max-width: 360px;" />

## Usage
If you intend to use this template as a starter for your Unity project, it is recommended to use the `rename_project` Go script or executable located in the `Tools/` folder. Copy it to the `UnityStarter/` directory to rename the project components. Note that you will still need to rename the `UnityStarter` folder manually.

If you only want to use specific modules from this project, you can simply delete the unwanted packages from the `Assets/ThirdParty/CycloneGames/` directory. For a real project, it is advisable to move all these packages outside the project's `Assets` folder and reference them as local packages through the Unity Package Manager.
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
    │   │   │   ├── Cheat/              # Simple Cheat System
    │   │   │   ├── Factory/            # Factory and ObjectPool
    │   │   │   ├── GameplayAbilities/  # Gameplay Ability System similar to Unreal Engine's
    │   │   │   ├── GameplayFramework/  # Gameplay framework similar to Unreal Engine's
    │   │   │   ├── GameplayTags/       # GameplayTags module similar to Unreal Engine's
    │   │   │   ├── InputSystem/        # Input binding system
    │   │   │   ├── Logger/             # Multi-Thread and high performance logging utility
    │   │   │   ├── Service/            # Common game services
    │   │   │   ├── UIFramework/        # Simple Hierarchical UI framework
    │   │   │   └── Utility/            # Common utilities (FPS Counter, etc.)
    │   │   └── ...
    │   └── ...
    ├── Packages/                       # Package manifests and configurations
    └── ProjectSettings/                # Unity project settings
