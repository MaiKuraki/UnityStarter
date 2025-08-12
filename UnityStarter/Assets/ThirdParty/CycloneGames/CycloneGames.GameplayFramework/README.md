# CycloneGames.GameplayFramework

English | [简体中文](./README.SCH.md)

A minimal, UE-style gameplay framework for Unity. It mirrors Unreal Engine's Gameplay Framework concepts (Actor, Pawn, Controller, GameMode, etc.), DI-friendly.

- Unity: 2022.3+
- Dependencies: `com.unity.cinemachine@3`, `com.cysharp.unitask@2`, `com.cyclone-games.factory@1`, `com.cyclone-games.logger@1`

## Core Concepts

- Actor: Base unit with lifespan and ownership. Examples: `PlayerStart`, `KillZVolume`, `CameraManager`.
- Pawn: Controllable `Actor`, possessed by a `Controller`.
- Controller: Owns and possesses a `Pawn`. `PlayerController` and `AIController` extend it.
- PlayerState: Player-centric data that persists across Pawn changes.
- GameMode: Orchestrates PlayerController/Pawn spawn and respawn rules.
- WorldSettings: ScriptableObject listing classes/prefabs for key gameplay actors.
- World: Lightweight holder for `GameMode` reference and lookups (not UE's UWorld).
- PlayerStart: Spawn point for players.
- CameraManager: Central camera manager (Cinemachine). Follows the current view target.
- SpectatorPawn: Default non-interactive Pawn for players before possessing a real Pawn.
- KillZVolume: Triggers `FellOutOfWorld` on overlap.
- SceneLogic: Similiar with Level Blueprint.

## Samples

- check the `Samples` Folder