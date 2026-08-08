# CycloneGames GameplayFramework

## Bounded Actor Admission

One `World` retains at most `World.MaximumActorCount` (`65,536`) actors. This is an implementation safety ceiling, not a recommended product budget. Existing actor updates, unregistration, and shutdown remain available at capacity; the framework never trims live actors automatically.

New code should use `TrySpawnActor`, `TrySpawnActorDeferred`, or `TryRegisterActor` and handle `false` as a capacity rejection. `SpawnActor`, `SpawnActorDeferred`, and `RegisterActor` preserve their successful behavior but fail fast with `InvalidOperationException` at the ceiling. `GetActorAdmissionSnapshot()` exposes count, capacity, and the monotonic rejection counter in O(1).

Migration is additive: replace exception-driven admission paths with the matching `Try*` API and route rejection through the product's spawn/admission policy. To roll back a call site, return to the legacy API only when fail-fast behavior is intended. Projects that genuinely require more than one implementation ceiling should shard actors across owned Worlds; changing the constant requires a reviewed framework build and corresponding load validation.

This contract adds no serialized field, renames no type or field, changes no prefab, scene, or `ScriptableObject` data, and persists no state. No asset migration or data rollback step is required.

[简体中文](README.SCH.md)

Inspired by Unreal Engine's Gameplay Framework, this module brings the familiar `GameInstance → World → GameMode → Controller → Pawn → PlayerState → GameState` pipeline to Unity. Developers with experience in UE client-server game flow, player admission, possession, and camera systems will recognize the architecture: container ownership, authority modes, and explicit runtime lifecycles are first-class concepts rather than add-on patterns.

## Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Assembly Integration](#assembly-integration)
- [Quick Start](#quick-start)
- [Runtime Lifecycle](#runtime-lifecycle)
- [WorldSettings and WorldDefinition](#worldsettings-and-worlddefinition)
- [Actor and World Ownership](#actor-and-world-ownership)
- [GameMode Login and Roster](#gamemode-login-and-roster)
- [Controller, Pawn, and Possession](#controller-pawn-and-possession)
- [PlayerState and GameState](#playerstate-and-gamestate)
- [Camera System](#camera-system)
- [Integrations](#integrations)
- [Editor Tools](#editor-tools)
- [Persistence and Data Ownership](#persistence-and-data-ownership)
- [Performance, Threading, and Platform Notes](#performance-threading-and-platform-notes)
- [Examples from Basic to Advanced](#examples-from-basic-to-advanced)
- [Validation](#validation)
- [Troubleshooting](#troubleshooting)

## Overview

A `GameInstance` owns one active `World`. That World owns actors and an authoritative `GameMode`. Players log in through the GameMode, receive a `PlayerController`, and possess a `Pawn`. `PlayerState` tracks individual participants across Pawn replacements; `GameState` holds committed match data. For local players, a `CameraManager` owns the camera-mode stack and blending.

The module handles what UE calls the "game flow" layer—not input, physics, or networking transport. `WorldNetMode` (Standalone, ListenServer, DedicatedServer) controls framework authority behavior; actual network transport and replication live in separate modules composed into the World.

### Owner-thread Contract

`GameInstance` and each `World` are single-owner runtime scopes. The thread that creates the `GameInstance` becomes the owner; Unity compositions should create and use it on the Unity main thread. World mutation and inline callbacks must remain on that owner thread. The framework provides neither an implicit lock nor a cross-thread queue.

For a World-bound `Actor`, `SetOwner` and `SetInstigator` assert the World owner thread before mutation. `OwnerChanged` is invoked synchronously on the same thread. Unbound Actors retain their existing Unity-facing contract and should be accessed on the Unity main thread. Product network adapters must explicitly marshal remote input to the World owner before changing these references.

## Architecture

### Lifecycle and Relationship Diagram

~~~mermaid
flowchart TD
    H["GameplayWorldHost<br/>Unity composition root"] --> GI
    H --> TD["GameplayWorldTickDriver<br/>Unity PlayerLoop bridge"]
    TD --> GI
    GI["GameInstance<br/>application scope"] --> LP["LocalPlayer slots<br/>0..8"]
    GI --> W["World<br/>one active scope"]
    W --> WD["WorldDefinition<br/>resolved prefab references and leases"]
    W --> A["Registered Actors"]
    W --> GM["GameMode<br/>authority only"]
    W --> GS["GameState<br/>committed World state"]
    GM --> S["IGameSession<br/>admission and roster"]
    GM --> PC["PlayerController"]
    PC --> PS["PlayerState"]
    PC --> P["Possessed Pawn"]
    LP -. local association .-> PC
    PC --> CM["CameraManager<br/>local Controller only"]
    CM --> CC["CameraContext<br/>view target and mode stack"]
~~~

These relationships have distinct meanings:

- **Lifecycle ownership:** GameInstance owns the active World. World owns Actors created through `SpawnActor` and `SpawnActorDeferred`.
- **Registration:** Scene and external Actors can join a World without transferring GameObject destruction ownership to the World.
- **Possession:** One Controller has exclusive control of one Pawn. Possession does not transfer lifecycle ownership.
- **Participant state:** PlayerState identifies a participant and can survive Pawn replacement within the same World.
- **Local association:** LocalPlayer associates a device/user slot with the current world-scoped PlayerController.
- **View target:** A PlayerController's camera target is independent of possession.
- **Authority:** A World accepts authoritative gameplay orchestration in Standalone, ListenServer, and DedicatedServer modes.

### Directory Layout

| Area | Responsibility |
| --- | --- |
| `Runtime/Scripts/World` | GameplayWorldHost, GameplayWorldTickDriver, GameInstance, LocalPlayer, World, WorldSettings, WorldDefinition, KillZVolume |
| `Runtime/Scripts/Foundation` | Actor lifecycle, primary Tick, tags, and damage contracts |
| `Runtime/Scripts/Game` | GameMode, GameSession, GameState, PlayerState |
| `Runtime/Scripts/Controllers` | Controller, PlayerController, AIController |
| `Runtime/Scripts/Pawns` | Pawn, SpectatorPawn, PlayerStart |
| `Runtime/Scripts/Camera` | Camera context, modes, blends, output, actions, and post-processors |
| `Runtime/Scripts/Config` | ScriptableObject authoring assets |
| `Runtime/Scripts/Integrations` | Optional cross-package adapters |
| `Editor` | Inspectors, property drawers, gizmos, World Debugger, project validation, and camera diagnostics |
| `Tests/Editor` | EditMode contract and performance tests |
| `Tests/PlayMode` | Unity lifecycle tests for GameplayWorldHost |
| `Samples` | Runtime-capable composition and camera examples |

### Assembly Boundaries

| Assembly | Auto referenced | Platform | Consumer action |
| --- | --- | --- | --- |
| `CycloneGames.GameplayFramework.Runtime` | No | Runtime and Editor | Add an explicit asmdef reference |
| `CycloneGames.GameplayFramework.Editor` | Yes | Editor only | Loaded for supported Inspectors and tools |
| `CycloneGames.GameplayFramework.Tests.Editor` | No | Editor only | Run with Unity Test Framework |
| `CycloneGames.GameplayFramework.Tests.PlayMode` | No | Runtime test Player | Run with Unity Test Framework |
| `CycloneGames.GameplayFramework.Sample.PureUnity` | No | Runtime and Editor | Use its sample scene or reference its code explicitly |
| `CycloneGames.GameplayFramework.Sample.CameraModes` | No | Runtime and Editor | Use the camera samples or reference their code explicitly |

Integration assemblies are not auto-referenced either. Consumer asmdefs should add only the integration assemblies they actually use.

## Assembly Integration

The module currently resides at:

~~~text
<repo-root>/UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayFramework/
~~~

Project Runtime code needs this reference:

~~~json
{
  "references": [
    "CycloneGames.GameplayFramework.Runtime"
  ]
}
~~~

Project code should also add the assemblies it uses directly, such as UniTask or Factory.Runtime. Do not edit Unity-generated csproj or solution files.

Sample asmdefs target both Runtime and Editor, so their Prefab components remain available in Player builds. They are not auto-referenced; project code that calls sample APIs must add the assembly reference explicitly. `GameplayWorldHost` is the standard Unity composition root. Projects requiring additional dependencies can override its narrow creation methods, while projects with another composition root can use `GameInstance` directly.

### Logging Integration and Migration

GameplayFramework only produces logs. Its package dependency is `com.cyclone-games.logging`; every assembly that writes records directly references `CycloneGames.Logging.Core` and uses the `CycloneGames.Logging` API namespace. Runtime and sample records use the stable `CycloneGames.GameplayFramework` category, while Editor records use `CycloneGames.GameplayFramework.Editor`.

The module does not initialize, own, or shut down a concrete backend. When the application has not installed an `ILogWriter`, the process writer is `NullLogWriter` and all records are safe no-ops. Only the application composition root should install or replace a writer through `LogRuntime`. For standard Unity Console and file outputs, compose `com.cyclone-games.logging.pipeline` with `com.cyclone-games.logging.unity`; `LoggingBootstrap` then owns the pipeline lifecycle. Neither backend package is a GameplayFramework dependency.

Runtime, Editor, and the PureUnity sample own assembly-local facades at `Runtime/Scripts/Diagnostics/GameplayFrameworkLog.cs`, `Editor/Diagnostics/GameplayFrameworkEditorLog.cs`, and `Samples/Sample.PureUnity/Diagnostics/GameplayFrameworkSampleLog.cs`. Every facade exposes `Category`, `Channel`, and `Create(ILogWriter logWriter)`. Package-local ambient fields are named `Log`; explicitly injected instance fields are named `_log`. All records use these cached channels with the shared `Trace`, `Debug`, `Info`, `Warning`, `Error`, and `Fatal` methods rather than platform-native logging or concrete pipeline APIs. Exceptions use the matching severity overload with the complete `Exception` and a message describing the failed operation. Ordinary logs containing dynamic values use deferred generic-state builders. When migrating project extensions, define the same kind of facade in the assembly that produces the logs instead of initializing a backend from the extension. Ambient channels resolve the current process writer on every write, so a successful `LogRuntime.TryReplaceWriter(expected, replacement)` handoff does not require GameplayFramework reinitialization.

This logging migration adds no serialized state and writes no files by itself. Persistence, rotation, flushing, shutdown, and corruption recovery belong to the selected backend and its application-level owner.

## Quick Start

### Prepare Prefabs

Create GameObject prefabs containing the following components:

1. **GameMode prefab:** a `GameMode` subclass. Assign a GameState prefab to `gameStateClass` when match state and a participant array are required.
2. **PlayerController prefab:** a `PlayerController` subclass.
3. **Pawn prefab:** a `Pawn` subclass plus product movement and input adapters.
4. **PlayerState prefab:** a `PlayerState` subclass.
5. **CameraManager prefab:** optional; configure it when local cameras require output.
6. **SpectatorPawn prefab:** optional; configure it when spectator login needs a possessable presentation object.

Place one or more `PlayerStart` Actors in the scene when spawned players need authored starting points.

### Create WorldSettings

Use:

~~~text
Create > CycloneGames > GameplayFramework > WorldSettings
~~~

Assign the four required references:

- GameMode;
- PlayerController;
- Pawn;
- PlayerState.

CameraManager and SpectatorPawn are optional. Click **Validate Configuration** in the Inspector before entering Play Mode.

### Add GameplayWorldHost

1. Create a scene GameObject named `Gameplay World Host`.
2. Add `GameplayWorldHost`.
3. Assign the WorldSettings asset.
4. Select the net mode and local-player count.
5. Keep **Auto Start** enabled when the Host is the scene entry point.

Dedicated Server mode always uses zero local players. The Host starts before ordinary Actor `Start` callbacks, owns the GameInstance, creates a sealed PlayerLoop Tick bridge, exposes runtime status and failure diagnostics, and disposes the World when its GameObject is destroyed. Disabling the Host component pauses bridge forwarding but does not change the World lifecycle; keep the Host enabled until stop or disposal.

Direct Reference requires no resolver. Asset Reference and Path require an explicit `IWorldSettingsReferenceResolver`; the WorldSettings section describes the resolver contract and the AssetManagement implementation provided by the module. If the project's DI container already owns the application lifetime, construct and dispose `GameInstance` directly without adding a Host.

### Expected Standalone Result

After `StartWorldAsync` completes:

- `GameInstance.CurrentWorld` is non-null;
- `World.LifecycleState` is `Playing`;
- `World.GameMode` is running;
- every configured LocalPlayer is associated with a PlayerController;
- every non-spectator Controller has a PlayerState and a possessed Pawn;
- GameState is available when the GameMode prefab provides one or the scene supplies one;
- a local CameraManager is created when a CameraManager is configured.

The sample scene is located at:

~~~text
Samples/Sample.PureUnity/Scene/UnitySampleScene.unity
~~~

## Runtime Lifecycle

### GameInstance and LocalPlayer

`GameInstance` records the creating thread as its owner thread. State-mutating calls, including `Tick`, assert that thread. Callers using Unity APIs should create and use the instance on the Unity main thread.

Constructor parameters:

| Parameter | Meaning |
| --- | --- |
| `IUnityObjectSpawner` | Required Actor-prefab instantiation boundary |
| `localPlayerCount` | Number of persistent local-user slots, from 0 through `MaxLocalPlayers` |
| `IWorldSettingsReferenceResolver` | Optional external WorldSettings asset loader |
| `ISceneTransitionHandler` | Optional scene-navigation adapter |

`LocalPlayer` contains a stable `Index` and the current world-scoped `PlayerController`. Controller logout, World stop, and GameInstance disposal clear this association.

One GameInstance accepts only one active World. Call and await `StopWorldAsync` before starting the next World. Calling public `World.ShutdownAsync` or `World.Dispose` directly performs the same ownership cleanup and notifies the owning GameInstance to clear `CurrentWorld`. A reentrant stop while the World is already `Stopping` does not release `CurrentWorld`; replacement start remains rejected until disposal completes.

### Net Mode

| WorldNetMode | IsAuthority | Creates GameMode | Automatic local login |
| --- | --- | --- | --- |
| `Standalone` | Yes | Yes | Yes |
| `Client` | No | No | No |
| `ListenServer` | Yes | Yes | Yes |
| `DedicatedServer` | Yes | Yes | No |

Dedicated-server composition should use zero LocalPlayers. A Client World provides a non-authoritative scope; network transport and replication adapters are responsible for adding client-visible state.

### World States

~~~mermaid
stateDiagram-v2
    [*] --> Created
    Created --> Initializing: StartWorldAsync
    Initializing --> Playing: initialization commits
    Initializing --> Stopping: cancellation or failure
    Playing --> Stopping: StopWorldAsync, travel, or dispose
    Stopping --> Stopped: Actors and gameplay state end
    Stopped --> Disposed: leases and lifecycle resources released
    Disposed --> [*]
~~~

A World accepts new Actors only while `Initializing` or `Playing`.

### Initialization Order

`StartWorldAsync` performs this transaction:

1. Validate GameInstance state and WorldSettings.
2. Resolve WorldSettings into a WorldDefinition.
3. Switch to the Unity main thread and assert owner-thread affinity.
4. Create the World and expose it as `CurrentWorld`.
5. Discover Actors, including inactive Actors, across all currently loaded valid scenes with an unordered scan.
6. Spawn and initialize GameMode in authoritative Worlds.
7. Create or discover GameState.
8. Perform LocalPlayer login transactions.
9. Transition the World to `Playing`.
10. Publish BeginPlay to registered, active, non-deferred Actors.
11. Notify GameMode that the World has started.
12. Enable Actor Tick dispatch.

Any exception aborts initialization, ends registered Actors, destroys World-owned Actors, disposes WorldDefinition leases, clears `CurrentWorld`, and rethrows the exception.

### Shutdown and Travel

Once shutdown begins, cleanup no longer accepts cancellation:

1. World stops Actor Tick dispatch, enters `Stopping`, and cancels `LifetimeToken`.
2. GameMode logs out every PlayerController.
3. Remaining Actors receive EndPlay in reverse World-registry order.
4. World-owned GameObjects are destroyed.
5. Scene and external Actors are unbound, but their GameObjects are not destroyed by the World.
6. Camera-brain settings are restored.
7. External WorldDefinition leases are released in reverse acquisition order.
8. World enters `Disposed`, and GameInstance clears `CurrentWorld`.

`GameMode.TravelToLevel` first stops the World with `EndPlayReason.Travel`, then calls `ISceneTransitionHandler.ChangeScene`. The destination scene creates its own World. Capture any data that must cross scenes before requesting travel.

`GameInstance.Dispose` cancels its lifetime, immediately shuts down the World with `ApplicationShutdown`, clears LocalPlayer associations, and releases its cancellation source.

## WorldSettings and WorldDefinition

### Authoring and Runtime Responsibilities

`WorldSettings` is a ScriptableObject authoring asset. Runtime startup resolves it into an immutable `WorldDefinition`. Runtime code reads the definition through `World.Definition`.

| Reference | Required | Runtime purpose |
| --- | --- | --- |
| GameMode | Yes | Authoritative rules and player orchestration |
| PlayerController | Yes | Participant Controller spawning |
| Pawn | Yes | Default non-spectator Pawn spawning |
| PlayerState | Yes | Participant identity/state spawning |
| CameraManager | No | Local camera runtime |
| SpectatorPawn | No | Spectator possession |

GameState is configured on the GameMode prefab or supplied by a scene Actor.

### Reference Sources

| Source | Authoring value | Resolver requirement |
| --- | --- | --- |
| `DirectReference` | Direct prefab reference | None |
| `AssetReference` | Inspector-recorded asset location | Resolver must support `AssetReference` |
| `PathLocation` | Project-defined address/path | Resolver must support `PathLocation` |

Required references must resolve to non-null assets. An optional direct reference may be null. An optional external reference is considered configured whenever its location is non-empty and must then resolve successfully.

### Resolver Contract

~~~csharp
public interface IWorldSettingsReferenceResolver
{
    bool Supports(WorldSettingsReferenceSource source);

    UniTask<WorldSettingsAssetLoadResult<T>> ResolveAsync<T>(
        string location,
        CancellationToken cancellationToken)
        where T : UnityEngine.Object;
}
~~~

The result contains success, asset, error, and an optional `IDisposable` lease. WorldSettings disposes acquired leases after a partial resolution failure. On success, WorldDefinition owns the leases and releases them exactly once in reverse order.

Resolver implementations must:

- respond to cancellation;
- return bounded error messages;
- dispose failed handles before returning;
- avoid storing mutable resolution state in WorldSettings;
- treat a location as untrusted input when it can originate outside project assets.

### AssetManagement Resolver

`AssetManagementWorldSettingsReferenceResolver` receives an explicit `IAssetPackage` and supports `AssetReference`. A successful asset handle becomes a WorldDefinition lease. It does not support `PathLocation`.

~~~csharp
var resolver =
    new AssetManagementWorldSettingsReferenceResolver(assetPackage);

var instance = new GameInstance(
    new DefaultUnityObjectSpawner(),
    localPlayerCount: 1,
    referenceResolver: resolver);
~~~

## Actor and World Ownership

### Actor Lifecycle

~~~mermaid
stateDiagram-v2
    [*] --> Constructed
    Constructed --> Initialized: Awake
    Initialized --> Playing: World BeginPlay or bound-World Start fallback
    Playing --> Ending: EndPlay requested
    Ending --> Ended: EndPlay returns
    Ended --> Initialized: non-owned Actor binds to replacement World
    Constructed --> Destroyed: OnDestroy
    Initialized --> Destroyed: OnDestroy
    Playing --> Destroyed: OnDestroy after EndPlay
    Ended --> Destroyed: OnDestroy
    Destroyed --> [*]
~~~

Override these hooks:

~~~csharp
protected override void BeginPlay()
{
    base.BeginPlay();
    // Subscribe and start world-bound behavior.
}

protected override void EndPlay(EndPlayReason reason)
{
    // Cancel and unsubscribe world-bound behavior.
    base.EndPlay(reason);
}
~~~

Each World binding publishes `BeginPlay` at most once. World owns the ordinary publication barrier; Unity `Start` only provides a fallback when the Actor is already bound and that World is already `Playing`. After shutdown, a non-owned scene or external Actor is unbound and can reset from `Ended` to `Initialized` when registered with a replacement World. A World-owned Actor cannot re-enter after ending. Each binding publishes `OnWorldUnbound` at most once, including direct destruction; when destruction occurs inside EndPlay, the terminal `Destroyed` state is not overwritten by `Ended`. `OnDestroyed` is the terminal Unity-destruction event and is separate from EndPlay.

An inactive Actor registered with a Playing World remains `Initialized` until it becomes active; `OnEnable` then asks the World to publish BeginPlay. The World rejects that fallback while the Actor is still a deferred spawn, so activation cannot bypass `FinishSpawningActor`.

EndPlay reasons include:

- `Destroyed`;
- `SceneUnload`;
- `WorldShutdown`;
- `Travel`;
- `InitializationFailure`;
- `ApplicationShutdown`.

### Primary Actor Tick

Primary Actor Tick is an optional World-lifecycle service and does not replace all Unity update mechanisms. An Actor with `ActorTickPhase.None` never enters a Tick registry and receives no per-frame framework callback.

~~~mermaid
flowchart LR
    PL["Unity PlayerLoop"] --> D["GameplayWorldTickDriver"]
    D --> GI["GameInstance.Tick"]
    GI --> W["World.Tick"]
    W --> R["Phase registry snapshot"]
    R --> A["Actor.Tick"]
~~~

Configure it through the Actor Inspector or in code:

| Member | Purpose |
| --- | --- |
| `ActorTickPhase` | Select None, Update, FixedUpdate, or LateUpdate |
| `CanEverTick` | True when a dispatchable phase is configured |
| `TickPhase` | Current primary phase |
| `IsActorTickEnabled` | Read the Runtime enable flag |
| `SetActorTickEnabled` | Enable or disable Runtime dispatch |
| `SetActorTickPhase` | Change the configured phase and move an enabled Actor between active-phase registries |
| `ConfigureActorTick` | Protected code-side phase and startup configuration |
| `Tick` | Protected virtual gameplay callback |

World calls `Tick` only when all of these conditions hold:

- World startup has completed and `LifecycleState` is `Playing`;
- the Actor remains registered, is not deferred, and has completed BeginPlay;
- the Actor component is active and enabled;
- its configured phase matches the current dispatch, and Runtime Tick is enabled.

Each active-phase registry contains only Actors whose Runtime Tick flag is enabled; disabling Tick removes an Actor from the hot-path list. Each phase uses a reusable snapshot. An Actor spawned, enabled, or moved into that phase during a callback participates beginning with the next dispatch of the target phase. An Actor that the snapshot has not yet visited is skipped if it has since been disabled, destroyed, or moved out of the current phase. World shutdown terminates the remaining entries. `GetTickActorCount` returns the current Runtime-enabled registry count. An exception from one Actor is logged with that Actor as context, and the phase continues with other Actors. Tick dispatch can run only on the owner thread and rejects re-entry.

Tick order is not a gameplay ordering contract. Registration and removal use dense swap-back structures, so dependencies between Actors must be expressed with explicit state, events, or orchestration rather than callback order.

Actor remains a MonoBehaviour, so a dedicated subclass or sibling component may still declare native Unity messages. Unity owns those messages; World registration and lifecycle gates do not control them. They are appropriate for narrow Unity-facing responsibilities whose lifecycle follows the component.

One Actor has only one primary phase. Components requiring Unity physics callbacks, Animator callbacks, rendering callbacks, Jobs/Burst scheduling, or multiple independent phases should continue to use a focused MonoBehaviour adapter or a pure C# simulation owner. GameplayAbilities, movement, projectile, and presentation modules do not become Actor-Tick-driven merely because an Actor owns them.

`GameplayWorldHost` creates a sealed `GameplayWorldTickDriver` at Runtime. Projects that compose `GameInstance` directly must forward each selected Unity phase or custom-loop phase exactly once through `GameInstance.Tick`.

### Registration and Ownership

| API | World registry | Begin/End notification | World destroys GameObject |
| --- | --- | --- | --- |
| Scene discovery | Yes | Yes | No |
| `RegisterActor` | Yes | Yes | No |
| `SpawnActor` | Yes | Yes | Yes |
| `SpawnActorDeferred` | Yes | After Finish | Yes |

For an ordinary registered Actor, `DestroyActor` removes it from the World, publishes EndPlay, and destroys the GameObject. Play Mode uses the normal Unity destruction boundary; Edit Mode destroys it immediately.

Destroying a committed PlayerController or its PlayerState escalates to participant logout so roster, GameState, LocalPlayer, Pawn, camera, and spectator state are cleaned up as one operation. Destroying the active GameMode while the World is `Initializing` or `Playing` escalates to complete World shutdown.

The Actor registry uses swap-back removal. Registry order, and the first result returned by `TryGetActor&lt;T&gt;`, are not stable selection policies.

Diagnostics and low-frequency tools can call `TryGetActorRegistration` over `0..ActorCount`. The call returns a readonly value and does not create a collection snapshot. Any Actor removal invalidates existing indices, so indices must not be persisted. Unity Actor references must be read on the main thread.

### Deferred Spawn

Use deferred spawn when dependencies or state must be configured before BeginPlay:

~~~csharp
Pawn pawn = world.SpawnActorDeferred(pawnPrefab);
bool committed = false;

try
{
    pawn.SetPawnConfig(pawnConfig);
    pawn.SetActorLocationAndRotation(position, rotation);

    world.FinishSpawningActor(pawn);
    committed = true;
}
finally
{
    if (!committed && world.IsActorRegistered(pawn))
    {
        world.DestroyActor(pawn, EndPlayReason.InitializationFailure);
    }
}
~~~

When the spawned instance was active, World temporarily deactivates it until `FinishSpawningActor`. Repeating Finish for a registered Actor is idempotent. Finishing an unregistered Actor throws an exception.

### Actor Services

Actor also provides:

- owner and instigator references;
- transform and view-point helpers;
- renderer-visibility synchronization;
- exact ordinal tags, up to 64 tags of at most 128 characters each;
- generic, point, and radial damage dispatch;
- cancellable lifespan;
- optional World-scoped primary Tick;
- `HasAuthority`, which returns true before the Actor joins a World and follows World authority afterward;
- `FellOutOfWorld` and `KillZVolume`.

Actor owner, Controller possession, and World ownership are independent relationships.

## GameMode Login and Roster

### Authority and Lifecycle

GameMode exists only in authoritative Worlds. Its states are:

~~~text
Uninitialized -> Initialized -> Starting -> Running -> Stopping -> Stopped
~~~

Initialization composes the supplied `IGameSession` or creates a bounded `GameSession`. `GameModeConfig` currently applies default spectator rules. A game-specific configuration asset can inherit it and override `ApplyTo`.

### Login Request Boundary

`PlayerLoginRequest` enforces these limits:

| Field | Limit |
| --- | --- |
| PlayerId | Must not be negative |
| PlayerName | At most 64 characters |
| RemoteAddress | At most 256 characters |
| Options | At most 1024 characters |
| IsLocal | Must match a trusted LocalPlayer slot; a local request cannot contain RemoteAddress |

Remote input must be authenticated, rate-limited, normalized, and validated before constructing a request. Calls that modify World or GameSession must run on the owner thread.

GameMode requires `request.IsLocal` to match whether the `localPlayer` argument was supplied. A supplied LocalPlayer must be the exact slot actually owned by `World.GameInstance`. Network and remote callers pass null and set `IsLocal` to false; an input flag cannot establish trusted local identity.

The base `CreateLocalPlayerLoginRequest` maps LocalPlayer index 0 to player ID 1 and the name `LocalPlayer1`, sets `IsLocal` to true, and increments subsequent slots in sequence. Override this method when local identity comes from a platform-user service.

Base request validation allows a null PlayerName. GameSession enforces PlayerId uniqueness within one session. Account authenticity, cross-session identity, and reconnect/rejoin ID allocation remain product admission responsibilities. `PlayerLoginResult.Error` is diagnostic text; sanitize and map it before including it in a network response.

### Transaction Flow

~~~mermaid
flowchart TD
    R["PlayerLoginRequest"] --> V["Validate mode, authority, cancellation, bounds, and trusted local slot"]
    V --> P["PreLogin / IGameSession.ApproveLogin"]
    P --> D["Deferred-spawn Controller and PlayerState"]
    D --> O["Optional local CameraManager or SpectatorPawn"]
    O --> I["Initialize PlayerController"]
    I --> SR["Register roster entry"]
    SR --> WC["Commit PlayerController to World and LocalPlayer"]
    WC --> SP["Possess spectator or spawn and possess default Pawn"]
    SP --> GS["Add PlayerState to GameState"]
    GS --> F["Finish deferred Actors"]
    F --> PL["PostLogin"]
    V -. failure .-> RB["Return status"]
    P -. rejection .-> RB
    D -. failure .-> RO["Roll back possession, roster, World associations, and spawned Actors"]
    O -. failure .-> RO
    I -. failure .-> RO
    SR -. failure .-> RO
    WC -. failure .-> RO
    SP -. failure .-> RO
    GS -. failure .-> RO
    F -. failure .-> RO
    PL -. failure .-> RO
~~~

`PlayerLoginResult` reports:

- `Success`;
- `InvalidRequest`;
- `NotAuthoritative`;
- `WorldNotAcceptingPlayers`;
- `Rejected`;
- `AtCapacity`;
- `SpawnFailed`;
- `Cancelled`.

`PostLogin` runs after relationships are committed and every deferred Actor has finished spawning. If PostLogin throws, the login transaction rolls back.

### GameSession

`GameSession` indexes every registered participant by both PlayerController reference identity and non-negative PlayerId. It rejects duplicate Controllers and duplicate PlayerIds and tracks player and spectator counts separately.

Registration gives one GameSession an exclusive identity lock on the PlayerState until `UnregisterPlayer`; the same PlayerState cannot be registered with another session concurrently. Change a registered participant's spectator category through `TrySetSpectatorStatus`. On the owner thread, that method checks capacity and atomically updates PlayerState, the roster entry, and both category counts. Registration rejected for identity or capacity returns before modifying PlayerState. Direct changes to PlayerId or spectator status that would break consistency with a registered entry are rejected.

Capacities are constructor arguments. Each capacity and their sum are limited by `MaxSupportedParticipants`. The default implementation has a single-thread owner.

The default GameSession receives serialized `maxPlayers` and `maxSpectators` values from the GameMode prefab. When a product owns admission state elsewhere, pass a custom `IGameSession` through `GameInstance.StartWorldAsync`.

Implement `IGameSession` to provide product admission and roster callbacks:

- `ApproveLogin`;
- `TryRegisterPlayer`;
- `ContainsPlayer`;
- `UnregisterPlayer`;
- `TrySetSpectatorStatus`;
- `HandleMatchHasStarted`;
- `HandleMatchHasEnded`.

Session match notifications are paired. `HandleMatchHasStarted` commits only after the World enters Playing and all initial BeginPlay callbacks complete. A startup rollback before that point does not publish `HandleMatchHasEnded`; after one successful start notification, shutdown publishes one end notification.

### Spawn, Restart, Logout, and Travel

GameMode first selects a PlayerStart by exact portal/GameObject name and then calls `ChoosePlayerStart`. The base implementation selects the first cached start. Scene discovery is unordered, so override `ChoosePlayerStart` when spawn selection must be deterministic.

`RestartPlayer` reuses an existing Pawn or deferred-spawns the default Pawn, then teleports it, publishes initial rotation, performs possession, and finishes the spawn.

The base teleport path handles CharacterController and Rigidbody components. It clears velocity and angular velocity on non-kinematic Rigidbodies. Override `TeleportPawn` when a product movement backend requires a different transaction.

`Logout` is a public, non-virtual atomic entry point. It unpossesses, unregisters the roster entry, removes PlayerState from GameState, clears World and LocalPlayer associations, and destroys World-owned participant Actors. Extend logout behavior through the protected virtual `HandleLogout`. Exceptions from unpossession, roster/GameState removal, the hook, or Actor destruction are isolated and logged so subsequent cleanup continues.

## Controller, Pawn, and Possession

### Possession Contract

A Controller must be registered with the World and initialized for the same World as the Pawn. `TryPossess` returns an error for invalid input; `Possess` throws when the transaction cannot commit.

Transaction steps:

1. Reject reentrant possession.
2. Validate Controller and Pawn World membership.
3. Detach the Controller's current Pawn.
4. Detach the target Pawn's current Controller.
5. Commit Controller, Pawn, and PlayerState associations.
6. Reset control rotation and dispatch Pawn restart.
7. Publish callbacks after all bidirectional relationships are consistent.

Possession is exclusive. It does not set Actor owner or change the World's destruction ownership.

Do not call `Possess` or `UnPossess` from a possession callback; the reentrancy guard rejects that mutation.

Possession callbacks run after state commits. After each callback returns, the transaction verifies the bidirectional Controller, Pawn, and PlayerState relationships again. If a callback destroys or otherwise invalidates the committed Controller or Pawn, the framework performs an emergency detach without callbacks and `TryPossess` returns false. Exceptions still propagate. When committed relationships remain valid, they are preserved, so a throwing callback still requires an explicit compensation policy.

World unbind clears Controller possession, PlayerState, start spot, input-suppression counters, and initialization state. This also applies to non-owned scene Controllers and externally registered Controllers. AIController also stops AI and clears focus. PlayerController clears LocalPlayer, camera context, CameraManager, SpectatorPawn, and view-target relationships. Explicitly reinitialize these non-owned objects before reusing them in a replacement World.

### Controller Input and View

Controller provides:

- control rotation with Pawn pitch limits;
- start-spot storage;
- stackable move/look suppression counters;
- Pawn and PlayerState access;
- view-point forwarding;
- movement stop, game end, and spawn-failure hooks.

Every `SetIgnoreMoveInput(true)` and `SetIgnoreLookInput(true)` call should have a matching false call. `ResetIgnoreInputFlags` clears both counters.

### Pawn

Pawn provides:

- bounded accumulated movement input;
- `ConsumeMovementInputVector`;
- controller-rotation flags and eye height;
- pitch limits configured through `PawnConfig`;
- restart and initial-rotation hooks;
- player, bot, and local-control queries;
- turn-on and turn-off state.

Pawn inherits optional primary Actor Tick but does not participate by default. A movement adapter should consume movement input and call `ApplyControllerRotation` in the phase owned by that movement implementation. Rigidbody-based adapters usually retain Unity `FixedUpdate`; a deterministic simulator can expose an explicit `Step` instead.

`NotifyInitialRotation` finds components on the Pawn that implement `IInitialRotationSettable` and publishes the spawn rotation before possession completes.

### PlayerController and LocalPlayer

`PlayerController.IsLocalController` is true only when a LocalPlayer is assigned. Only a local PlayerController can own a CameraManager. A remote PlayerController can participate, possess a Pawn, and own PlayerState without creating local camera state.

The possessed Pawn, spectator Pawn, manual view target, and LocalPlayer are independent relationships. Automatic view-target order is:

1. possessed Pawn;
2. spectator Pawn;
3. the PlayerController itself.

`SetViewTarget` creates a manual override. `ClearViewTargetOverride` restores policy-driven targeting.

### AIController and PlayerStart

AIController provides focus-Actor/focal-point state and overridable `RunAI`/`StopAI`. It owns a primary Actor Tick in the Update phase: `RunAI` enables Tick and `StopAI` disables it. While running, Tick turns control rotation toward the focus. Product behavior trees, navigation, and perception remain adapter responsibilities.

PlayerStart registration is World-scoped. Its custom Editor supports 3D, side-scroller, and top-down gizmo displays and does not use a Runtime static registry.

## PlayerState and GameState

### PlayerState

PlayerState stores:

- a bounded player name;
- a non-negative player ID;
- spectator status;
- the current Pawn association.

It can survive Pawn replacement within the same World. `CopyProperties` copies identity fields but not the Pawn link.

While registered with GameSession, PlayerId is locked and spectator status is controlled through the session's atomic category-change operation. Setters, property copying, and snapshot restore reject conflicting changes until unregistration completes.

`OnPawnSetEvent` is published after possession commits. Callback observers can read consistent Controller, Pawn, and PlayerState relationships.

### PlayerStateSnapshot

`CaptureSnapshot` creates a `PlayerStateSnapshot` containing:

- `PlayerName`;
- `PlayerId`;
- `IsSpectator`;
- `SchemaVersion`.

The current schema version is 1. `TryRestoreSnapshot` accepts only the current schema and validates ID and name bounds before mutating state. Persistence and network adapters must reject or transform other schemas before calling the Runtime API.

The snapshot excludes Pawn, Controller, Transform, Unity object references, and World membership. A save or network adapter owns serialization and storage. Capture allocates a snapshot object, so use it at explicit persistence or replication boundaries.

### GameState

GameState contains the participant `PlayerArray`, match state, and elapsed in-progress time. It rejects null or duplicate PlayerState entries and validates World membership.

Valid match transitions are:

| Current state | Allowed next state |
| --- | --- |
| EnteringMap | WaitingToStart, LeavingMap, Aborted |
| WaitingToStart | InProgress, LeavingMap, Aborted |
| InProgress | WaitingPostMatch, LeavingMap, Aborted |
| WaitingPostMatch | WaitingToStart, LeavingMap, Aborted |
| LeavingMap | None |
| Aborted | None |

Elapsed time advances only during InProgress. A transition from WaitingPostMatch to WaitingToStart resets the accumulated time.

GameMode owns transition policy. Use `TrySetMatchState` when a recoverable result is required; use `SetMatchState` when an illegal transition is a programming error.

## Camera System

### Evaluation Pipeline

~~~mermaid
flowchart LR
    VT["Resolved view target"] --> BP["Actor.CalcCamera base pose"]
    BP --> BM["Base CameraMode"]
    BM --> SM["Stacked CameraModes<br/>oldest to newest"]
    SM --> PP["Post-processors<br/>registration order"]
    PP --> FO["Explicit FOV override"]
    FO --> BL["CameraBlendState"]
    BL --> OUT["CameraManager output"]
    OUT --> VC["CinemachineCamera pose/lens"]
    VC --> BR["CinemachineBrain.ManualUpdate"]
~~~

### CameraContext

Each PlayerController creates a CameraContext on demand. The context owns:

- view-target policy;
- resolved and manual view targets;
- one base CameraMode;
- a fixed-capacity stacked-mode array.

The default mode capacity is 8 and can be changed by overriding `PlayerController.GetCameraModeStackCapacity`. A requested non-positive capacity becomes 1.

`TryPushCameraMode` rejects null, duplicate instances, clearing state, and capacity overflow. `TryPushOrReplaceOldest` provides an explicit full-stack policy. During CameraManager evaluation, base-mode replacement and stack push, replace, or remove are rejected so the iterated stack remains stable. A `Clear` requested during evaluation is deferred until the evaluation scope ends; it then deactivates stacked modes in reverse order before deactivating the base mode.

### Camera Modes and Blending

Inherit `CameraMode` and implement:

~~~csharp
public override CameraPose Evaluate(
    CameraContext context,
    in CameraPose basePose,
    float deltaTime)
{
    return basePose;
}
~~~

The base mode evaluates first. Stacked modes then evaluate from index 0 through the newest entry. The newest stacked mode is the primary mode used to select transition blend duration.

`CameraBlendState` supports Linear, SmoothStep, EaseOut, EaseIn, and custom `ICameraBlendCurve` evaluation. Negative blend durations are clamped to zero.

### CameraManager and Cinemachine

During GameMode login, CameraManager is created only for a local PlayerController and only when WorldDefinition contains a CameraManager prefab.

It:

- evaluates camera state through primary Actor Tick in the LateUpdate phase after initialization;
- binds an explicitly assigned or discovered `CinemachineBrain`;
- requests exclusive brain ownership from the World;
- saves the brain update mode and switches it to ManualUpdate;
- saves and clears Follow/LookAt targets on the active CinemachineCamera;
- writes the final pose and FOV;
- updates the brain manually;
- restores brain and virtual-camera state on release.

When a scene contains multiple brains, assign `bootstrapBrain` or call `SetBootstrapBrain`. Discovery selects an active brain and logs when the selection is ambiguous.

World rejects two CameraManagers attempting to own the same brain concurrently.

### View Target and Post-processors

`DefaultGameplayViewTargetPolicy` resolves manual override, suggested target, possessed Pawn, spectator Pawn, and PlayerController in that order.

CameraManager supports at most 16 registered `ICameraPostProcessor` instances. They run in registration order after every CameraMode. Owners should unregister processors when they end.

`PerlinNoiseShakePostProcessor` is a Runtime object with trauma, amplitude, frequency, decay, and exponent controls.

### Camera Actions

`CameraActionBinding` maps string action keys to `CameraActionPreset`:

1. Check inline entries first.
2. Use `CameraActionMap` as the fallback.
3. For duplicate keys in the map, use the last entry.

Trigger policies are:

- `ReplaceSameKey`;
- `IgnoreIfRunning`;
- `Stack`.

The binding has configurable active-action and pooled-mode limits, both defaulting to 8. At the active limit or CameraContext capacity, `PlayAction`/`PlayPreset` returns false. When the pool has no available mode, the binding creates a `PresetCameraMode`; returned modes are retained only up to the configured pool limit.

On disable or destruction, the binding stops active actions and removes their modes from the PlayerController that originally accepted them.

Available bridges are:

- `AnimatorCameraActionBridge` for Animation Events;
- `CameraActionStateBehaviour` for Animator state enter, progress thresholds, and exit;
- `TimelineCameraActionReceiver` for Playables notifications;
- direct calls from gameplay code.

Each CameraActionStateBehaviour instance tracks at most 8 concurrent Animator/layer pairs. At capacity, enter and exit actions continue to run, but progress triggers for additional pairs pause until a slot is released. `OnStateExit` releases the slot.

Exit mode can perform no operation, stop an action key, or play an action key. Progress triggers when normalized time crosses the configured threshold and can run once per entire state lifetime or once per loop. Enter and progress triggers have independent transition gates.

### Camera Authoring Assets

| Asset/Runtime type | Purpose |
| --- | --- |
| `CameraProfile` | Shared default FOV and fallback blend duration; requires an explicit `ApplyTo` call |
| `CameraActionPreset` | Timed framing, offsets, lens, weight curve, and blend data for an action shot |
| `CameraActionMap` | Shared action-key table with a lazy Runtime lookup |
| `PresetCameraMode` | Runtime evaluator used by CameraActionBinding |
| `ViewTargetCameraMode` | Pass-through base mode that uses the resolved Actor camera pose |

The Runtime-capable CameraModes sample includes first-person, orbital, third-person follow, and collision post-processor examples.

## Integrations

| Assembly | Required dependency assemblies | Capability | Default consumer reference |
| --- | --- | --- | --- |
| `CycloneGames.GameplayFramework.Runtime.Integrations.AssetManagement` | GameplayFramework Runtime, AssetManagement Runtime, UniTask | `AssetManagementWorldSettingsReferenceResolver` | Explicit |
| `CycloneGames.GameplayFramework.Runtime.Integrations.GameplayAbilities` | GameplayFramework Runtime, GameplayAbilities Runtime | AbilitySystem provider and actor-info helpers | Explicit |
| `CycloneGames.GameplayFramework.Runtime.Integrations.GameplayTags` | GameplayFramework Runtime, GameplayTags Core and Unity Runtime | Actor tag-container extension methods | Explicit |
| `CycloneGames.GameplayFramework.Runtime.Integrations.Navigathena` | GameplayFramework Runtime, Navigathena, Navigathena.SceneManagement, UniTask | `ISceneTransitionHandler` adapter | Explicit and conditional |

### AssetManagement

Use the AssetManagement integration when WorldSettings entries use `AssetReference`. Compose an explicit `IAssetPackage` and pass the resolver to GameInstance.

### GameplayAbilities

Implement `IAbilitySystemProvider` on an Actor or one of its components, then use:

- `TryGetAbilitySystem`;
- `InitializeAbilityActorInfo`.

Owner and avatar overrides are explicit parameters. Without overrides, the helper uses Actor owner when available and uses the Actor as the avatar.

This integration does not schedule `AbilitySystemComponent.Tick`. The ability-system owner selects its clock and forwards it explicitly. When a World-lifecycle gate is required, a GameplayFramework Actor can forward from primary Tick; an independent Unity composition can retain a dedicated MonoBehaviour driver. Movement and physics components continue to own their own phases.

### GameplayTags

Add `GameObjectGameplayTagContainer` to the Actor GameObject. The integration provides:

- `TryGetGameplayTagContainer`;
- `ActorHasGameplayTag`;
- `AddGameplayTag`;
- `RemoveGameplayTag`.

Actor's lightweight string tags and the GameplayTags container are independent APIs.

These extension methods perform component discovery and should be used only during composition, initialization, and other cold paths. Code that checks or modifies tags repeatedly should call `TryGetGameplayTagContainer` once, retain the returned container for the Actor/component lifetime, and use the cached reference directly. The integration provides no hidden per-frame cache and does not take ownership of the container.

The integration assembly ships with the package and directly references GameplayTags Core and Unity Runtime, so GameplayTags is an explicitly declared package dependency. Consumers must still explicitly reference the integration assembly from their own asmdef; `autoReferenced: false` prevents unrelated assemblies from acquiring the API implicitly.

### Navigathena Package Boundary

The Navigathena integration requires the UPM package named `com.mackysoft.navigathena` and supports `[1.1.0,2.0.0)`. GameplayFramework's `package.json` does not declare a Navigathena dependency, so installing GameplayFramework does not install Navigathena. A new major version requires API compatibility validation before extending the range.

The integration asmdef owns these enablement rules:

~~~text
versionDefines: com.mackysoft.navigathena [1.1.0,2.0.0) -> CYCLONEGAMES_HAS_NAVIGATHENA
defineConstraints: CYCLONEGAMES_HAS_NAVIGATHENA
autoReferenced: false
~~~

When Package Manager has not resolved Navigathena, neither the integration assembly nor its tests participate in compilation. GameplayFramework Runtime, Editor tools, samples, and core tests do not depend on Navigathena. No scripting define needs to be maintained in PlayerSettings.

Code that calls the adapter should reside in the project's own integration asmdef. That asmdef must reference the GameplayFramework integration and Navigathena assemblies and configure its own `versionDefines`/`defineConstraints`; a symbol generated by a version define applies only to its owning assembly:

~~~json
{
  "name": "Game.Runtime.Integrations.Navigathena",
  "references": [
    "CycloneGames.GameplayFramework.Runtime",
    "CycloneGames.GameplayFramework.Runtime.Integrations.Navigathena",
    "MackySoft.Navigathena",
    "MackySoft.Navigathena.SceneManagement"
  ],
  "autoReferenced": false,
  "defineConstraints": [
    "GAME_HAS_NAVIGATHENA"
  ],
  "versionDefines": [
    {
      "name": "com.mackysoft.navigathena",
      "expression": "[1.1.0,2.0.0)",
      "define": "GAME_HAS_NAVIGATHENA"
    }
  ]
}
~~~

### Minimal Navigathena Composition

The default adapter treats the string received from `ISceneTransitionHandler` as a built-in scene name. It passes a null transition director to Navigathena, allowing `StandardSceneNavigator` to use its configured default transition.

~~~csharp
public static GameInstance CreateGameInstance(ISceneNavigator sceneNavigator)
{
    var sceneTransitions =
        new NavigathenaSceneTransitionHandler(sceneNavigator);

    return new GameInstance(
        new DefaultUnityObjectSpawner(),
        localPlayerCount: 1,
        referenceResolver: null,
        sceneTransitionHandler: sceneTransitions);
}
~~~

After the World enters Playing, authority-side code can request level travel:

~~~csharp
await world.GameMode.TravelToLevel("Stage02", cancellationToken);
~~~

The call stops the current World before calling `ISceneNavigator.Change`. The destination scene's composition root starts its own World. Initialize the supplied `ISceneNavigator` according to the Navigathena lifecycle before gameplay travel occurs.

### Composing Navigathena Through the Host

When `GameplayWorldHost` owns the GameInstance, provide the Navigator before the Host starts:

~~~csharp
public sealed class NavigathenaGameplayWorldHost : GameplayWorldHost
{
    private ISceneNavigator sceneNavigator;

    public void Configure(ISceneNavigator value)
    {
        if (GameInstance != null)
        {
            throw new InvalidOperationException(
                "Scene navigation must be configured before the World starts.");
        }

        sceneNavigator = value ?? throw new ArgumentNullException(nameof(value));
    }

    protected override ISceneTransitionHandler CreateSceneTransitionHandler()
    {
        if (sceneNavigator == null)
        {
            throw new InvalidOperationException(
                "An initialized ISceneNavigator is required.");
        }

        return new NavigathenaSceneTransitionHandler(sceneNavigator);
    }
}
~~~

The project composition root should call `Configure` before Unity invokes the Host's `Start`. If the composition root cannot guarantee that order, disable **Auto Start** and call `StartWorldAsync` after configuration completes.

### Custom Navigathena Requests

`NavigathenaLoadSceneRequestFactory` receives the operation type and scene key for every Change, Push, and Replace. It returns a complete Navigathena `LoadSceneRequest`, allowing one adapter to select custom scene identifiers, transition directors, scene data, and interrupt operations without adding those types to GameplayFramework's core contracts.

~~~csharp
public static ISceneTransitionHandler CreateSceneTransitions(
    ISceneNavigator navigator,
    Func<string, ISceneIdentifier> resolveScene,
    ITransitionDirector levelTransition,
    ITransitionDirector overlayTransition,
    ISceneData travelData,
    IAsyncOperation interruptOperation)
{
    LoadSceneRequest CreateLoadRequest(
        NavigathenaSceneTransitionOperation operation,
        string sceneKey)
    {
        ITransitionDirector transition =
            operation == NavigathenaSceneTransitionOperation.Push
                ? overlayTransition
                : levelTransition;

        return new LoadSceneRequest(
            resolveScene(sceneKey),
            transition,
            travelData,
            interruptOperation);
    }

    return new NavigathenaSceneTransitionHandler(
        navigator,
        CreateLoadRequest,
        () => new PopSceneRequest(
            overlayTransition,
            interruptOperation));
}
~~~

Scene keys are product-side input. The resolver should reject unknown or malformed keys before constructing an identifier. Navigathena history, `Reload`, direct progress reporting, and navigation operations outside `ISceneTransitionHandler` remain available through the injected `ISceneNavigator`.

The integration asmdef references its dependency assemblies directly. Each dependency and its corresponding integration assembly should be present or removed together.

## Editor Tools

| Tool | Capability |
| --- | --- |
| Actor Inspector | Serialized Actor fields, primary Tick authoring, derived fields, multi-object editing, Runtime lifecycle and Tick state, and Play Mode Tick enable/disable controls |
| ActorTag drawer | Searchable selection for fields marked with `ActorTagAttribute` |
| WorldSettings Inspector | Required/optional summary, Direct/Asset/Path authoring, and validation button |
| GameplayWorldHost Inspector | Composition validation, effective local-player count, runtime state, and Start/Stop controls |
| GameMode Inspector | Runtime mode state and PlayerController roster with Ping |
| PlayerStart Inspector | Configurable 3D, side-scroller, and top-down scene gizmos |
| CameraManager Inspector | Runtime brain, owner, pose, blend, view target, mode, and FOV telemetry |
| CameraActionStateBehaviour Inspector | Conditional enter/exit/progress authoring and capacity guidance |
| Camera Debug Window | Buffered camera telemetry, graphs, and configurable alerts |
| World Debugger | Host, World, session, per-phase Tick registry counts, and indexed Actor-registration inspection |
| Project Validation | Read-only scan of WorldSettings assets and Hosts in loaded scenes |

Open the camera window through:

~~~text
Tools > CycloneGames > GameplayFramework > Camera Debug Window
~~~

World and authoring tool entries are:

~~~text
Tools > CycloneGames > GameplayFramework > World Debugger
Tools > CycloneGames > GameplayFramework > Project Validation
~~~

The camera window samples only in Play Mode. Sampling modes are Off, Basic, and Full. Sampling frequency is configurable from 5 to 120 Hz. The in-memory ring buffer is configurable from 120 to 2048 samples and defaults to 600. Full mode additionally samples linear and angular speed. Alert thresholds cover FOV delta, remaining blend time, blend stall, and motion speed.

Editor diagnostics are observational only. Validate performance in a target Player and the Profiler before using diagnostic results as release evidence.

## Persistence and Data Ownership

The framework writes no Runtime save file or preference key.

| Data | Owner | Storage provided by the module | Version control | Cleanup/migration |
| --- | --- | --- | --- | --- |
| WorldSettings | Project authoring | ScriptableObject asset | Usually tracked | Edit and validate the serialized asset |
| Actor phase and startup Tick flag | Scene/prefab authoring | Serialized MonoBehaviour fields | Usually tracked | Edit through Actor Inspector; use Runtime APIs for temporary changes |
| GameModeConfig, PawnConfig, CameraProfile | Project authoring | ScriptableObject assets | Usually tracked | Provide explicit serialized migration when fields change |
| CameraActionPreset, CameraActionMap | Project authoring | ScriptableObject assets | Usually tracked | Keep action keys stable or migrate consumers |
| WorldDefinition | World Runtime | Memory only | No | Disposed with World; releases leases in reverse order |
| GameplayWorldHost, GameInstance, LocalPlayer, World | Runtime composition | Memory only | No | Host GameObject lifetime or explicit Stop/Dispose |
| PlayerStateSnapshot | Save/network adapter | In-memory DTO | Adapter-specific | Require the current SchemaVersion before restore |
| Camera debug samples | CameraDebugWindow | Editor memory only | No | Clear the buffer or close/reload the window |
| World Debugger and Project Validation state | Editor window | Editor memory only | No | Close or reload the window; no EditorPrefs or SessionState writes |

For saved data:

1. Capture PlayerStateSnapshot at a controlled boundary.
2. Pass it to a dedicated save service.
3. Include slot/schema metadata owned by the save service.
4. Write atomically to the platform persistent-data location.
5. Validate size and integrity before deserialization.
6. Require the Runtime snapshot schema expected by the current build.
7. Call `TryRestoreSnapshot` and handle its error.

Do not serialize PlayerStateSnapshot auto-properties directly with Unity `JsonUtility`. Select and validate a serializer that supports this DTO contract on the target backend.

## Performance, Threading, and Platform Notes

### Thread Ownership

- GameInstance and World mutations run on one owner thread.
- GameInstance records the constructor thread ID.
- Actor Tick dispatch, phase changes, and Runtime enable changes use that same owner thread.
- Network, file, and asset callbacks must marshal to the Unity main thread before mutating framework state.
- Unity object and Cinemachine operations run on the Unity main thread.
- WorldSettings resolver I/O can complete on other threads. Result validation, rollback, lease transfer, and WorldDefinition disposal run on the main thread performing resolution. Cross-thread WorldDefinition disposal is rejected before consuming ownership.
- GameSession is not thread-safe.
- Async APIs use UniTask and propagate cancellation during startup and asset resolution. World initialization links caller, GameInstance, and World lifetime tokens; direct World shutdown cancels pending async login so startup cannot continue committing.

### Bounded Structures

| Structure/input | Limit or default |
| --- | --- |
| LocalPlayer slots | At most 8 |
| World Actor registrations | At most `World.MaximumActorCount` (`65,536`) per World |
| Actor string tags | At most 64; each at most 128 characters |
| Login text inputs | name/address/options: 64/256/1024 characters respectively |
| Total GameSession participants | At most 100,000 |
| CameraContext modes | Fixed per context; default 8 |
| CameraManager post-processors | At most 16 |
| CameraActionStateBehaviour tracking | At most 8 Animator/layer pairs |
| CameraActionBinding active/pool counts | Configurable; default 8 each |
| Actor primary Tick phase | One phase per Actor; hot-path registry size depends on Runtime-enabled Actors |

The World Actor ceiling is an implementation safety limit, not a product budget; use the `Try*` admission APIs and define a lower validated product budget for Actor count, spawn rate, and scene content. Roster growth is bounded by GameSession limits.

### Allocation Points

When profiling, inspect these cold-path or boundary operations:

- World scene discovery and collection growth;
- WorldSettings resolution and lease-array creation;
- PlayerState snapshot capture;
- first use of Actor tags and renderer buffers;
- Actor lifespan cancellation-source creation;
- Tick registry and reusable-snapshot capacity growth during Actor registration;
- CameraContext construction;
- CameraActionMap lazy-lookup construction;
- mode creation when the CameraActionBinding pool is empty;
- string parsing from timed Animation Events;
- diagnostic-window buffer resizing.

Actor Tick dispatch traverses a reusable phase snapshot and does not scan Actors whose Tick phase is None. Fixed camera arrays and reusable Tick collections reduce collection growth after construction but do not provide a module-wide zero-allocation guarantee. Measure hot paths with the Unity Profiler on target hardware.

### Player, IL2CPP, and Server Builds

- The Runtime assembly references UnityEngine, Cinemachine, Burst, Mathematics, UniTask, Factory, and Logging contracts.
- GameplayWorldHost uses one sealed MonoBehaviour bridge to forward Update, FixedUpdate, and LateUpdate. Direct GameInstance composition must provide an equivalent loop owner.
- Only `QuaternionToEulerXYZBurst` is marked with `BurstCompile`; verify whether target call paths actually execute Burst.
- PlayerStateSnapshot serialization is external. Reflection-based serializers may require AOT metadata or link preservation.
- DedicatedServer mode suppresses automatic local login, but the Runtime assembly still contains its declared dependencies.
- Client mode does not provide replication by itself.
- Mono, IL2CPP, managed stripping, headless/server, and every target platform require representative Player-build validation.

## Examples from Basic to Advanced

### Query World Actors

~~~csharp
if (world.TryGetActor<PlayerStart>(out PlayerStart start))
{
    Vector3 startLocation = start.GetActorLocation();
}
~~~

Type lookup is suitable for discovery, not deterministic selection. Product selection should use an explicit identifier or policy.

### Create an Actor That Optionally Participates in Tick

~~~csharp
public sealed class RotatingActor : Actor
{
    [SerializeField] private Vector3 RotationAxis = Vector3.up;
    [SerializeField, Min(0f)] private float DegreesPerSecond = 45f;

    protected override void Awake()
    {
        base.Awake();
        ConfigureActorTick(
            ActorTickPhase.Update,
            startWithTickEnabled: true);
    }

    protected override void Tick(float deltaSeconds)
    {
        if (RotationAxis.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        transform.Rotate(
            RotationAxis.normalized,
            DegreesPerSecond * deltaSeconds,
            Space.Self);
    }
}
~~~

Add this component to a scene object or spawn it through World. World begins dispatch only after BeginPlay. Call `SetActorTickEnabled(false)` to pause this Actor without disabling its GameObject; call `SetActorTickPhase` to change phases on the owner thread. The package provides a compilable counterpart at `Samples/Sample.PureUnity/UnitySampleRotatingActor.cs`.

### Forward a Directly Composed GameInstance

`GameplayWorldHost` already owns these forwards. A custom composition root should call the same instance once in every phase that it owns:

~~~csharp
private void Update()
{
    gameInstance?.Tick(ActorTickPhase.Update, Time.deltaTime);
}

private void FixedUpdate()
{
    gameInstance?.Tick(ActorTickPhase.FixedUpdate, Time.fixedDeltaTime);
}

private void LateUpdate()
{
    gameInstance?.Tick(ActorTickPhase.LateUpdate, Time.deltaTime);
}
~~~

Do not add a second forwarder when GameplayWorldHost is present. A headless or deterministic host can call the same API from an explicit loop, but delta must be validated, finite, and non-negative.

### Deferred Spawn and Possession

~~~csharp
Pawn pawn = world.SpawnActorDeferred(pawnPrefab);
bool committed = false;

try
{
    pawn.SetPawnConfig(pawnConfig);
    pawn.SetActorLocationAndRotation(spawnPosition, spawnRotation);

    if (!controller.TryPossess(pawn, out string error))
    {
        throw new InvalidOperationException(error);
    }

    world.FinishSpawningActor(pawn);
    committed = true;
}
finally
{
    if (!committed && world.IsActorRegistered(pawn))
    {
        if (ReferenceEquals(controller.GetPawn(), pawn))
        {
            controller.UnPossess();
        }

        world.DestroyActor(pawn, EndPlayReason.InitializationFailure);
    }
}
~~~

The Controller must already be registered and initialized for the same World.

### Authoritative Remote Login

~~~csharp
PlayerLoginRequest request = new PlayerLoginRequest(
    playerId: authenticatedPlayerId,
    playerName: validatedDisplayName,
    isSpectator: false,
    remoteAddress: normalizedAddress,
    options: validatedOptions,
    isLocal: false);

PlayerLoginResult result = await world.GameMode.LoginAsync(
    request,
    localPlayer: null,
    cancellationToken);

if (!result.Succeeded)
{
    throw new InvalidOperationException(
        $"Login failed with {result.Status}: {result.Error}");
}

PlayerController remoteController = result.PlayerController;
~~~

Authentication and transport checks must complete before this call. Run the call on the World owner thread.

### Capture and Restore Participant State

~~~csharp
PlayerStateSnapshot snapshot = sourcePlayerState.CaptureSnapshot();

if (!targetPlayerState.TryRestoreSnapshot(snapshot, out string error))
{
    throw new InvalidDataException(error);
}
~~~

`CaptureSnapshot` and `TryRestoreSnapshot` are Runtime APIs. A persistence integration owns file paths, serialization, atomic replacement, integrity, encryption policy, and schema migration.

### Custom Camera Mode

~~~csharp
public sealed class ShoulderOffsetCameraMode : CameraMode
{
    private readonly Vector3 localOffset;

    public ShoulderOffsetCameraMode(Vector3 localOffset)
    {
        this.localOffset = localOffset;
    }

    public override float BlendDuration => 0.12f;

    public override CameraPose Evaluate(
        CameraContext context,
        in CameraPose basePose,
        float deltaTime)
    {
        Vector3 offset = basePose.Rotation * localOffset;
        return new CameraPose(
            basePose.Position + offset,
            basePose.Rotation,
            basePose.Fov);
    }
}

CameraMode mode =
    new ShoulderOffsetCameraMode(new Vector3(0.45f, 0f, 0f));

if (!playerController.TryPushCameraMode(mode))
{
    throw new InvalidOperationException(
        "The camera mode stack rejected the mode.");
}
~~~

Remove the same mode instance when the flow that owns the action ends.

## Validation

### EditMode Tests

The test assembly covers:

- World modes, startup, rollback, non-owned Actor reuse, trusted local-login validation, participant/GameMode destruction escalation, logout, and CurrentWorld cleanup;
- WorldSettings validation, external resolvers, cancellation, and lease disposal;
- AssetManagement prefab-component resolution and handle ownership;
- GameplayWorldHost ownership, indexed World diagnostics, custom Inspectors, and project validation;
- Actor tags, damage, lifespan, possession, Pawn input, primary Tick phases, Runtime gates, mutation safety, re-entry rejection, exception isolation, and owner-thread enforcement;
- PlayerState snapshots, session identity locks, atomic spectator changes, and post-commit Pawn notification;
- GameState transitions and World-scoped PlayerStart;
- CameraContext capacity, replacement, evaluation mutation guards, deferred clear, teardown order, view-target policy, and action limits;
- camera blending and camera math;
- CameraContext, GameSession, and 1,000 opt-in Actor Tick performance benchmarks;
- request mapping, customization, validation, and cancellation forwarding when a supported Navigathena package is installed.

Run it from Unity Test Runner:

~~~text
Window > General > Test Runner > EditMode
Assembly: CycloneGames.GameplayFramework.Tests.Editor
~~~

After installing Navigathena `[1.1.0,2.0.0)`, also run:

~~~text
Assembly: CycloneGames.GameplayFramework.Integrations.Navigathena.Tests.Editor
~~~

Without Navigathena installed, confirm that neither the integration assembly nor its test assembly appears in `Library/ScriptAssemblies`, then run the core test assembly above.

Batchmode example:

Before running the command, create `&lt;repo-root&gt;/UnityStarter/TestResults`, or replace both output paths with an existing writable directory.

~~~powershell
<unity-editor> -batchmode -nographics -quit -projectPath "<repo-root>/UnityStarter" -runTests -testPlatform EditMode -assemblyNames "CycloneGames.GameplayFramework.Tests.Editor" -testResults "<repo-root>/UnityStarter/TestResults/GameplayFramework.EditMode.xml" -logFile "<repo-root>/UnityStarter/TestResults/GameplayFramework.EditMode.log"
~~~

### PlayMode Tests

The PlayMode assembly verifies that an auto-start Host creates a Playing World; forwards the Update, FixedUpdate, and LateUpdate Actor Tick phases; stops forwarding with Host lifetime; and disposes the World with the Host GameObject.

~~~text
Window > General > Test Runner > PlayMode
Assembly: CycloneGames.GameplayFramework.Tests.PlayMode
~~~

### Editor Manual Smoke Test

1. Reimport or reload the project and confirm that Runtime, Editor, sample, and test assemblies compile.
2. Open the PureUnity sample scene.
3. Confirm GameplayWorldHost references UnitySampleWorldSettings.
4. While still in Edit Mode, add `UnitySampleRotatingActor` to a scene GameObject and save the scene.
5. Enter Play Mode.
6. Verify the World is Playing and the local Controller owns a PlayerState and Pawn.
7. If camera output is configured, verify one CameraManager owns the expected CinemachineBrain.
8. Confirm that the sample Actor rotates only while the World is Playing.
9. Open World Debugger and inspect the World, per-phase Tick counts, and Actor registration.
10. Click `Disable Runtime Tick` in the sample Actor Inspector. Confirm rotation stops, the Tick Enabled diagnostic changes, and the Actor remains registered; then click `Enable Runtime Tick` to resume.
11. Run Project Validation and confirm the sample reports no configuration errors.
12. Open Camera Debug Window and observe pose/blend data.
13. Exit Play Mode and confirm no participant, Tick, or camera-mode state remains.

### Player and Platform Validation

For each release target:

1. Add the project Runtime composition root and required scenes to Build Settings.
2. Perform a clean Player build.
3. Cover startup, cancellation, login failure, logout, travel, and application shutdown.
4. Test both direct and external WorldSettings references.
5. Profile camera, Actor, and roster hot paths on target hardware.
6. Validate IL2CPP/AOT serializer behavior and managed stripping.
7. Run Server targets without LocalPlayers and inspect dependencies, logging, and shutdown.

EditMode tests and source inspection do not prove Player, IL2CPP, headless, or target-platform validation.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| “A world is already active” | Call and await `StopWorldAsync` before `StartWorldAsync` |
| Owner-thread exception | Marshal to the Unity main thread before mutating GameInstance or World, or performing login, spawn, or possession |
| WorldSettings validation fails | Configure GameMode, PlayerController, Pawn, and PlayerState |
| External reference has no resolver | Pass a resolver to GameInstance and confirm `Supports` returns true for the selected source |
| External load fails after cancellation | Propagate cancellation and dispose the loader handle |
| Client World has no GameMode | Client mode is non-authoritative; populate client-visible state through a network adapter |
| Dedicated server has no local Controller | Use remote `LoginAsync`; automatic local login is disabled |
| Login returns InvalidRequest | Check ID and name/address/options bounds, `IsLocal`, and the exact LocalPlayer slot from GameInstance |
| Login returns Rejected | Check PlayerId uniqueness within the session and product admission policy |
| Login returns AtCapacity | Check GameSession player/spectator capacity and counts |
| Login returns SpawnFailed | Check prefab references, spawner results, World state, and custom initialization callbacks |
| Player spawn point is unstable | Override `ChoosePlayerStart` or pass an exact portal name |
| Possession fails | Register and initialize the Controller, use the same World, and avoid reentrant callbacks |
| Movement input has no effect | The movement adapter must consume and apply the pending vector |
| Actor Tick does not run | Confirm the phase is not None, Runtime Tick is enabled, the component is active/enabled, BeginPlay completed, registration is not deferred, and World is Playing |
| Actor Tick reports re-entry | Do not call GameInstance.Tick or World.Tick from an Actor Tick callback; defer work to the next owned loop phase |
| Actors never Tick with direct GameInstance composition | Forward each required phase exactly once from the composition root; GameplayWorldHost supplies this automatically |
| Movement or Ability uses another update model | Retain the module's own MonoBehaviour or explicit simulation clock; Actor Tick is opt-in and does not replace package-owned scheduling |
| Scene Actor begins play outside the World barrier | Start the composition root before ordinary Actor Start callbacks |
| Ended Actor cannot join another World | A non-owned scene/external Actor must be unbound before registration with a replacement World; World-owned Actors cannot re-enter |
| GameState transition is illegal | Follow the valid transition table or handle `TrySetMatchState` failure |
| No CameraManager | Configure the optional prefab and use a local PlayerController |
| CameraManager has no output | Assign or resolve a CinemachineBrain and active CinemachineCamera |
| Brain ownership error | Ensure each CinemachineBrain is owned by only one CameraManager |
| Camera-mode push returns false | Check duplicate instance, clearing/evaluation state, and CameraContext capacity |
| Camera clear does not execute immediately | A clear requested during evaluation runs after the evaluation scope ends |
| Camera action returns false | Check action key, preset, active-action limit, Controller resolution, and mode-stack capacity |
| Animator progress action does not trigger | Check progress key, threshold, transition flag, loop policy, and the 8-pair tracking capacity |
| Snapshot restore fails | Require the current schema version and validate non-negative ID, player-name length, and registered identity/spectator locks |
| Travel reports no handler | Compose `ISceneTransitionHandler` into GameInstance |
| Sample script is missing from a Player build | Confirm the sample asmdef includes the target platform and resolve every compilation error before building |
