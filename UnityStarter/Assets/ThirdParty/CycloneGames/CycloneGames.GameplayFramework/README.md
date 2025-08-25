# CycloneGames.GameplayFramework

English | [简体中文](./README.SCH.md)

A minimal, UE-style gameplay framework for Unity. It mirrors Unreal Engine's Gameplay Framework concepts (Actor, Pawn, Controller, GameMode, etc.), DI-friendly.

- Unity: 2022.3+
- Dependencies: `com.unity.cinemachine@3`, `com.cysharp.unitask@2`, `com.cyclone-games.factory@1`, `com.cyclone-games.logger@1`

## Quick Start

1) Create WorldSettings
- Create: Create -> CycloneGames -> GameplayFramework -> WorldSettings
- Fill fields with prefabs that have these components attached:
  - `GameMode`
  - `PlayerController`
  - `Pawn` (your default player pawn)
  - `PlayerState`
  - `CameraManager`
  - `SpectatorPawn`
- Put the created `WorldSettings` asset under a `Resources` folder if you want to load it by name at runtime.

2) Implement an object spawner (or use DI)
- The framework spawns gameplay objects through `IUnityObjectSpawner` (from `com.cyclone-games.factory`). A minimal example:

```csharp
using CycloneGames.Factory.Runtime;
using UnityEngine;

public class SimpleSpawner : IUnityObjectSpawner
{
    public T Create<T>(T origin) where T : Object
    {
        return origin == null ? null : Object.Instantiate(origin);
    }
}
```

3) Bootstrap the game
- Create a small boot `MonoBehaviour` and launch the `GameMode` using your `World` and `WorldSettings`:

```csharp
using UnityEngine;
using CycloneGames.GameplayFramework;
using CycloneGames.Factory.Runtime;

public class GameBoot : MonoBehaviour
{
    private IUnityObjectSpawner spawner;
    private World world;

    void Start()
    {
        world = new World();
        spawner = new SimpleSpawner();

        var ws = Resources.Load<WorldSettings>("YourWorldSettingsName");
        var gm = spawner.Create(ws.GameModeClass) as GameMode;
        gm.Initialize(spawner, ws);
        world.SetGameMode(gm);
        gm.LaunchGameMode();
    }
}
```

4) Place scene actors
- Add at least one `PlayerStart` into your scene (the first found is used by default; name matching is supported via portal parameter in `GameMode`).
- Optionally add `KillZVolume` (with a `BoxCollider` set to Trigger) to auto-destroy actors that fall out of bounds; make sure falling actors have `Collider` and `Rigidbody`.
- Ensure your Main Camera has a `CinemachineBrain` component and there is at least one `CinemachineCamera` in scene. `CameraManager` will find and follow the current view target.

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

## API Highlights

- GameMode
  - `Initialize(IUnityObjectSpawner, IWorldSettings)`: wire dependencies.
  - `LaunchGameMode()`: spawns `PlayerController` then restarts player at a `PlayerStart`.
  - `RestartPlayer(...)`: respawn pipeline; uses `SpawnDefaultPawn*` helpers.
- PlayerController
  - Spawns `PlayerState`, `CameraManager`, and a `SpectatorPawn` during async init.
  - `GetCameraManager()`, `GetSpectatorPawn()` helpers.
- Controller
  - `Possess(Pawn)`, `UnPossess()`, `SetControlRotation(...)`.
  - `GetDefaultPawnPrefab()` comes from `WorldSettings.PawnClass`.
- Pawn
  - `PossessedBy(Controller)`, `UnPossessed()`, `DispatchRestart()`.
- WorldSettings
  - Prefab references for `GameMode`, `PlayerController`, `Pawn`, `PlayerState`, `CameraManager`, `SpectatorPawn`.
- CameraManager
  - Requires `CinemachineBrain` on the active Camera; manages active `CinemachineCamera`.

## Samples

- Check the `Samples/Sample.PureUnity` folder for a ready-to-run scene (`Scene/UnitySampleScene.unity`), prefabs, a `Resources/UnitySampleWorldSettings.asset`, and simple boot code.

## Troubleshooting

- Spawn failed / null references
  - Ensure `WorldSettings` fields reference valid prefabs with the required components.
  - Provide an `IUnityObjectSpawner` (or integrate your DI container to implement it).
- Camera not following
  - Add `CinemachineBrain` to Main Camera, ensure at least one `CinemachineCamera` exists, and that `CameraManager` is spawned.
- Player spawns at origin
  - Add a `PlayerStart` to the scene, or verify its name if you use a portal name.
- KillZ not firing
  - `KillZVolume` needs a trigger collider; falling actors need `Collider` + `Rigidbody`.