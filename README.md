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
## DI Framework Selection
Switch between Git branches to explore implementation examples for each DI framework.</br>
<img src="./Docs/ProjectDescription/Main/Des_01.png" alt="Branch Select" style="width: 50%; height: auto; max-width: 360px;" />
## Project Structure
The main source code for the modules is located in the `UnityStarter/Assets/ThirdParty/` directory. The hierarchy follows a modular design, optimized for scalability and clear separation of concerns.

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
