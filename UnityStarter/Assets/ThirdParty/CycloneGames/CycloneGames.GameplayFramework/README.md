# CycloneGames GameplayFramework

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

