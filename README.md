![Unity Project Starter](<https://capsule-render.vercel.app/api?type=waving&height=220&color=gradient&text=Unity%20Project%20Starter&section=header&reversal=false&textBg=false&desc=GameplayFramework%20│%20GameplayAbility(GAS)%20│%20UIFramework%20│%20HotUpdate%20Ready%20(HybridCLR)%20│%20CI/CD%20Ready&descAlign=50&descAlignY=58&descSize=16&fontAlignY=30&fontSize=72>)

A production-ready, modular Unity project template inspired by **Unreal Engine** architecture. Built for large-scale commercial game development with performance-first systems, comprehensive tooling, and modern CI/CD workflows.

<p align="left"><br> English | <a href="README.SCH.md">简体中文</a></p>

> [!NOTE]
> If you find this project helpful, please consider giving it a star ⭐. Thank you!

![Unity](https://img.shields.io/badge/Unity-2022.3%20LTS-black?logo=unity)
![Unity6](https://img.shields.io/badge/Unity-6000.3%20LTS-black?logo=unity)
![License](https://img.shields.io/badge/License-MIT-green.svg)
[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/MaiKuraki/UnityStarter)

## Table of Contents

1. [Overview](#overview)
2. [Architecture](#architecture)
3. [Module Catalog](#module-catalog)
4. [Getting Started](#getting-started)
5. [Technology Stack](#technology-stack)
6. [Related Projects](#related-projects)

## Overview

UnityStarter is a battle-tested foundation for professional game projects — not a collection of isolated utilities, but a coherent full-stack game framework. It provides:

- **Complete game architecture** — Actor/Pawn/Controller/GameMode pattern from Unreal Engine
- **Full combat system** — GAS-inspired abilities, attributes, effects, cooldowns, and costs
- **Production AI** — visual behavior tree editor with Burst/DOD scaling to 10,000+ agents
- **Dual-mode networking** — state synchronization + lockstep rollback with 5 AOI strategies
- **Performance-first** — zero-GC hot paths across all core systems
- **Modular design** — 24+ self-contained packages, import only what you need
- **Hot update ready** — HybridCLR (C#) + YooAsset/Addressables (assets) + Obfuz (code protection)
- **Developer tooling** — custom Roslyn analyzers, build pipeline, cheat console, UI profiler
- **Cross-engine core** — key modules target `netstandard2.0` with zero Unity API, Godot-ready

## Architecture

```text
UnityStarter/
├── Assets/
│   ├── Build/                  # Build pipeline & CI/CD (HybridCLR, Obfuz, YooAsset/Addressables)
│   ├── Plugins/                # Third-party native plugins
│   ├── Settings/               # URP PipelineAssets (Performant / Balanced / HighFidelity)
│   ├── ThirdParty/CycloneGames/ # Core framework (22+ modules, 80+ asmdef)
│   └── UnityStarter/           # Project-specific code & scenes
├── Analyzers/                  # Roslyn analyzers (22 implemented rules) [standalone .sln]
├── Packages/                   # UPM manifest
├── ProjectSettings/            # Unity project configuration
└── Tools/                      # Go utility scripts
```

Every module follows the same internal structure: `Runtime/` (core) → `Editor/` (tools) → optional `Core/` (engine-agnostic) → optional `Integrations/` (cross-module bridges) → `Samples/`. Each has bilingual `README.md` + `README.SCH.md`.

## Module Catalog

> **📚** Each module has detailed documentation at `{ModulePath}/README.md`. Click the links below to jump directly.

### 🎮 Gameplay Systems

| Module | Description | Docs |
| --- | --- | --- |
| **GameplayFramework** | UE-style Actor/Pawn/Controller/GameMode. DI-friendly, extensible game architecture. | [README](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayFramework/README.md) |
| **GameplayAbilities** | Data-driven ability system (GAS). ScriptableObject abilities, attributes, effects, cost/cooldown, cues. | [README](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayAbilities/README.md) |
| **GameplayAbilities.Networking** | Networked GAS: client prediction, rollback, state replication, 14 message types, drift detection. | Module directory |
| **GameplayTags** | Hierarchical tag system. Source-generated, zero-allocation queries, Unity Editor integration. | [README](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayTags/README.md) |
| **BehaviorTree** | Visual AI behavior tree. GraphView editor, DOD/Burst mass simulation, 30+ nodes, network sync. | [README](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.BehaviorTree/README.md) |
| **AIPerception** | Burst-accelerated AI sensors. Visual/deep/proximity queries, spatial grid, 0 GC per tick. | [README](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.AIPerception/README.md) |
| **RPGFoundation** | RPG extensions: inventory, attributes, quests, 2D/3D movement controllers. | Module directory |
| **UIFramework** | Hierarchical UI with window state machine, MVP support, tween backends, dynamic atlas. | [README](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.UIFramework/README.md) |

### 🏗️ Core Infrastructure

| Module | Description | Docs |
| --- | --- | --- |
| **Factory** | High-performance object pooling. Thread-safe, auto-scaling, O(1) operations, DOD variant. | [README](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Factory/README.md) |
| **Logger** | Structured logging. Multi-threaded, file rotation, pluggable processors, WebGL support. | [README](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Logger/README.md) |
| **AssetManagement** | Asset loading abstraction. W-TinyLFU cache, YooAsset/Addressables/Resources backends. | [README](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.AssetManagement/README.md) |
| **DataTable** | Config data pipeline. Luban/MessagePack backends, zero-GC O(1) queries, engine-agnostic Core. | [README](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.DataTable/README.md) |
| **Audio** | Audio management with Wwise-like API. Bank system, platform profiles, voice policies. | [README](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Audio/README.md) |
| **Localization** | BCP 47 locale framework. CLDR plurals (25+ languages), fallback chains, pseudo-localization QA. | [README](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Localization/README.md) |

### 🌐 Networking

| Module | Description | Docs |
| --- | --- | --- |
| **Networking** | Network abstraction layer. Pluggable transports (Mirror/Mirage), QoS channels, 4 serializers, Burst AOI. | [README](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Networking/README.md) |

### 🕹️ Input & Device

| Module | Description | Docs |
| --- | --- | --- |
| **InputSystem** | Reactive input with context stacks. Local multiplayer, device auto-detection, runtime rebinding. | [README](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.InputSystem/README.md) |
| **DeviceFeedback** | Cross-platform haptics. Mobile vibration (Android/iOS/WebGL), gamepad rumble, device light control. | [README](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.DeviceFeedback/README.md) |

### 🧰 Developer Tools

| Module | Description | Docs |
| --- | --- | --- |
| **Analyzers** | Custom Roslyn analyzers. 22 implemented rules across 5 categories, 2 CodeFix providers. | [README](UnityStarter/Analyzers/CycloneGames.Analyzers/README.md) |
| **Cheat** | Type-safe debug command console. VitalRouter integration, async commands, thread-safe execution. | [README](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Cheat/README.md) |
| **Utility** | Common utilities: FPS counter, safe area, splash screen, file operations. | Module directory |
| **Services** | Service abstractions: camera management, graphics settings, device configuration. | Module directory |
| **FontAssets** | Multilingual font collection. Latin, CJK (Simplified/Traditional/Japanese/Korean). | Module directory |

### 🎯 2D & Platform

| Module | Description | Docs |
| --- | --- | --- |
| **Foundation2D** | 2D game foundation. Performance benchmarks, platformer components, sprite management. | Module directory |

### 🔧 Build & Deployment

| Module | Description | Docs |
| --- | --- | --- |
| **Build** | Complete build pipeline. HybridCLR + Obfuz + asset management. CLI-driven, CI/CD ready. | [README](UnityStarter/Assets/Build/README.md) |

## Getting Started

### Prerequisites

- **Unity 2022.3 LTS** or later (6000.3 LTS also supported)
- **Git** (for automatic versioning in Build module)

### Quick Start

```bash
git clone https://github.com/MaiKuraki/UnityStarter.git
```

1. **Open in Unity** — point Unity Hub to the `UnityStarter/` directory
2. **Explore** — start with `GameplayFramework` to understand the architecture
3. **Read the docs** — every module has `README.md` + `README.SCH.md`
4. **Configure builds** — see [Build Module](UnityStarter/Assets/Build/README.md) for CI/CD setup

### Using Individual Modules

Copy the module folder from `UnityStarter/Assets/ThirdParty/CycloneGames/` into your own project. Check the module's README for any dependencies — most modules are self-contained.

## Technology Stack

### Runtime

| Package | Purpose |
| --- | --- |
| `UniTask` (Cysharp) | Zero-allocation async/await |
| `R3` (Cysharp) | Reactive data streams |
| `VContainer` (hadashiA) | DI/IoC container |
| `VitalRouter` (hadashiA) | Message routing |
| `VYaml` (hadashiA) | YAML serialization |
| `LitMotion` (annulusgames) | Tween animation |
| `PrimeTween` | Alternative tween backend |
| `MessagePack-CSharp` | Binary serialization |
| `Unity Debug Sheet` (harumak) | In-game debug panel |

### Build & Pipeline

| Tool | Purpose |
| --- | --- |
| `HybridCLR` | C# hot update |
| `YooAsset` / `Addressables` | Asset hot update & management |
| `Obfuz` / `Obfuz4HybridCLR` | Code obfuscation |
| `Mirror` / `Mirage` | Network transports |
| `Navigathena` | Scene management |
| `Unity MCP` | AI-assisted development |

## Related Projects

- **[Rhythm Pulse](https://github.com/MaiKuraki/RhythmPulse)** — Rhythm game mechanics collection
- **[Unity GAS Sample](https://github.com/MaiKuraki/UnityGameplayAbilitySystemSample)** — GAS demonstration project

---

**License**: [MIT](LICENSE) · **Support**: [GitHub Issues](https://github.com/MaiKuraki/UnityStarter/issues)
