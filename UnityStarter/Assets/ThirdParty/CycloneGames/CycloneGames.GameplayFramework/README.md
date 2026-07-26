# CycloneGames GameplayFramework

## Bounded Actor Admission

One `World` retains at most `World.MaximumActorCount` (`65,536`) actors. This is an implementation safety ceiling, not a recommended product budget. Existing actor updates, unregistration, and shutdown remain available at capacity; the framework never trims live actors automatically.

New code should use `TrySpawnActor`, `TrySpawnActorDeferred`, or `TryRegisterActor` and handle `false` as a capacity rejection. `SpawnActor`, `SpawnActorDeferred`, and `RegisterActor` preserve their successful behavior but now fail fast with `InvalidOperationException` at the ceiling. `GetActorAdmissionSnapshot()` exposes count, capacity, and the monotonic rejection counter in O(1).

Migration is additive: replace exception-driven admission paths with the matching `Try*` API and route rejection through the product's spawn/admission policy. To roll back a call site, return to the legacy API only when fail-fast behavior is intended. Projects that genuinely require more than one implementation ceiling should shard actors across owned Worlds; changing the constant requires a reviewed framework build and corresponding load validation.

This contract adds no serialized field, renames no type or field, changes no prefab/scene/`ScriptableObject` data, and persists no state. No asset migration or rollback data step is required.

[简体中文](README.SCH.md)

Inspired by Unreal Engine's Gameplay Framework, this module brings the familiar `GameInstance → World → GameMode → Controller → Pawn → PlayerState → GameState` pipeline to Unity. Developers who've worked with UE's client-server game flow, player admission, possession, and camera system will recognize the architecture — container ownership, authority modes, and explicit runtime lifecycle are first-class concepts here, not bolted-on patterns.

## Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Quick Start](#quick-start)
- [Core Concepts](#core-concepts)
- [Usage Guide](#usage-guide)
- [Advanced Topics](#advanced-topics)
- [Common Scenarios](#common-scenarios)
- [Performance and Memory](#performance-and-memory)
- [Troubleshooting](#troubleshooting)

## Overview

A `GameInstance` owns one active `World`. That World owns actors and an authoritative `GameMode`. Players log in through the GameMode, receive a `PlayerController`, and possess a `Pawn`. `PlayerState` tracks individual participants across Pawn replacements; `GameState` holds committed match data. For local players, a `CameraManager` stacks camera modes and blends between them.

The module handles what UE calls the "game flow" layer — not input, not physics, not networking transport. `WorldNetMode` (Standalone, ListenServer, DedicatedServer) controls framework authority behavior; actual network transport and replication live in separate modules you compose into the World.

