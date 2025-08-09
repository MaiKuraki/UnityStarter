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

This repository can be used in two primary ways: as a complete template for a new project, or as a source of individual modules to be imported into an existing project.

### As a Full Project Template
If you intend to use this repository as a starter for a new Unity project, follow these steps to rename it:

1.  **Locate the Renaming Tool**: Find the `rename_project` Go script or `.exe` executable inside the `Tools/` directory.
2.  **Move the Renaming Tool**: Copy the `rename_project`'s script or executable into the `UnityStarter/` directory.
3.  **Run the Tool**: Execute the script. It will automatically update project-specific components like namespaces and assembly definitions.
4.  **Rename Manually**: Finally, manually rename the `UnityStarter/` root folder to your desired project name.

### Using Specific Modules
If you only need specific modules for your project, you have two options:

- **Simple Method**: Navigate to `Assets/ThirdParty/CycloneGames/` and simply delete the package folders you do not need.
- **Recommended Method (Production)**: For a production environment, it is best practice to move the required packages from `Assets/ThirdParty/CycloneGames/` to a location *outside* your project's `Assets` folder. Then, add them to your project via the Unity Package Manager using the **"Add package from disk..."** option. This approach keeps your project clean and highly modular.
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
