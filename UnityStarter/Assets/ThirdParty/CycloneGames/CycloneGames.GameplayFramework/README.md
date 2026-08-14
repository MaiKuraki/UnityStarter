# CycloneGames GameplayFramework

[简体中文](README.SCH.md)

This module provides a Unity gameplay-flow foundation organized around the familiar `GameInstance -> World -> GameMode -> Controller -> Pawn -> PlayerState -> GameState` runtime chain. Its common query APIs, authority concepts, player admission, possession, and camera orchestration follow Unreal Engine Gameplay Framework usage conventions while retaining explicit Unity lifecycle and composition boundaries.

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

The package contains two runtime layers. `CycloneGames.GameplayFramework.Core` owns engine-independent admission, roster, match-state, snapshot, and capacity rules. `CycloneGames.GameplayFramework.Runtime` owns the Unity object graph and presents the familiar Unreal-style gameplay interface. Runtime depends on Core; Core never references Runtime or UnityEngine.

The module handles what UE calls the "game flow" layer—not input, physics, or networking transport. `WorldNetMode` (Standalone, ListenServer, DedicatedServer) controls framework authority behavior; actual network transport and replication live in separate modules composed into the World.

### Owner-thread Contract

`GameInstance` and each `World` are single-owner runtime scopes. The thread that creates the `GameInstance` becomes the owner; Unity compositions should create and use it on the Unity main thread. World mutation and inline callbacks must remain on that owner thread. The framework provides neither an implicit lock nor a cross-thread queue.

For a World-bound `Actor`, `SetOwner` and `SetInstigator` assert the World owner thread before mutation. `OwnerChanged` is invoked synchronously on the same thread. Unbound Actors retain their existing Unity-facing contract and should be accessed on the Unity main thread. Product network adapters must explicitly marshal remote input to the World owner before changing these references.

## Architecture

### Lifecycle and Relationship Diagram

~~~mermaid
flowchart TD
    H["GameplayWorldHost<br/>Unity composition root"] --> GI
    H --> ED["GameplayWorldTickDriver<br/>early Update / FixedUpdate"]
    H --> LD["GameplayWorldLateTickDriver<br/>late LateUpdate"]
    ED --> GI
    LD --> GI
    GI["GameInstance<br/>application scope"] --> LP["LocalPlayer slots<br/>0..8"]
    GI --> W["World<br/>one active scope"]
    GI --> CLA["ICameraOutputLeaseArbiter<br/>composition resource domain"]
    W --> WD["WorldDefinition<br/>resolved prefab references and leases"]
    W --> A["Registered Actors"]
    W --> GM["GameMode<br/>authority only"]
    W --> GS["GameState<br/>committed World state"]
    GM --> S["IGameSession / GameSession<br/>Unity participant facade"]
    S --> R["ParticipantRoster<br/>pure Core rules"]
    GS --> M["MatchStateMachine<br/>pure Core rules"]
    GM --> PC["PlayerController"]
    PC --> PS["PlayerState"]
    PC --> P["Possessed Pawn"]
    LP -. local association .-> PC
    PC --> CM["CameraManager<br/>local Controller only"]
    CM --> CC["CameraContext<br/>view target and mode stack"]
    CM --> OUT["ICameraOutput<br/>exclusive output resource"]
    W --> CLA
~~~

These relationships have distinct meanings:

- **Lifecycle ownership:** GameInstance owns the active World. World owns Actors created through `SpawnActor` and `SpawnActorDeferred`.
- **Registration:** Scene and external Actors can join a World without transferring GameObject destruction ownership to the World.
- **Possession:** One Controller has exclusive control of one Pawn. Possession does not transfer lifecycle ownership.
- **Participant state:** PlayerState identifies a participant and can survive Pawn replacement within the same World.
- **Local association:** LocalPlayer associates a device/user slot with the current world-scoped PlayerController.
- **View target:** A PlayerController's camera target is independent of possession.
- **Camera resource domain:** Worlds sharing one camera-output lease arbiter cannot concurrently own overlapping backend resources.
- **Authority:** A World accepts authoritative gameplay orchestration in Standalone, ListenServer, and DedicatedServer modes.

### Directory Layout

| Area | Responsibility |
| --- | --- |
| `Core` | Engine-independent participant roster, login values, match timestamps/state/snapshots, player snapshots, World runtime limits, Actor admission snapshots, and Actor-tag limits |
| `Runtime/Scripts/World` | GameplayWorldHost, GameplayWorldComposition, early/late Tick drivers, GameInstance, LocalPlayer, World, WorldSettings, WorldDefinition, KillZVolume |
| `Runtime/Scripts/Foundation` | Actor lifecycle, primary Tick, tags, and damage contracts |
| `Runtime/Scripts/Game` | GameMode, GameSession, GameState, PlayerState |
| `Runtime/Scripts/Controllers` | Controller, PlayerController, AIController |
| `Runtime/Scripts/Pawns` | Pawn, SpectatorPawn, PlayerStart |
| `Runtime/Scripts/Camera` | Camera context, modes, blends, output, actions, and post-processors |
| `Runtime/Scripts/Config` | ScriptableObject authoring assets |
| `Runtime/Scripts/Integrations` | Optional cross-package adapters |
| `Editor` | Inspectors, property drawers, gizmos, World Debugger, project validation, and camera diagnostics |
| `Core/Tests/Editor` | Engine-independent EditMode rule and boundary tests |
| `Tests/Editor` | Unity EditMode contract and performance tests |
| `Tests/PlayMode` | Unity lifecycle tests for GameplayWorldHost |
| `Samples` | Runtime-capable composition and camera examples |

### Assembly Boundaries

| Assembly | Auto referenced | Platform | Consumer action |
| --- | --- | --- | --- |
| `CycloneGames.GameplayFramework.Core` | No | Managed Runtime and Editor; no engine references | Add an explicit reference when source uses Core types directly |
| `CycloneGames.GameplayFramework.Runtime` | No | Runtime and Editor | Add an explicit asmdef reference |
| `CycloneGames.GameplayFramework.Editor` | Yes | Editor only | Loaded for supported Inspectors and tools |
| `CycloneGames.GameplayFramework.Core.Tests.Editor` | No | Editor only; no engine references | Run Core rules with Unity Test Framework |
| `CycloneGames.GameplayFramework.Tests.Editor` | No | Editor only | Run with Unity Test Framework |
| `CycloneGames.GameplayFramework.Tests.PlayMode` | No | Runtime test Player | Run with Unity Test Framework |
| `CycloneGames.GameplayFramework.Sample.PureUnity` | No | Runtime and Editor | Use its sample scene or reference its code explicitly |
| `CycloneGames.GameplayFramework.Sample.CameraModes` | No | Runtime and Editor | Use the camera samples or reference their code explicitly |
| `CycloneGames.GameplayFramework.Runtime.Integrations.Cinemachine` | No | Runtime and Editor when Cinemachine is installed | Reference only from a Cinemachine integration assembly |

`CycloneGames.GameplayFramework.Runtime` references Core directly. This does not grant a consumer compile-time access to Core types: consumer asmdefs add every assembly whose types appear in their own source. Integration assemblies are not auto-referenced either.

## Assembly Integration

The module currently resides at:

~~~text
<repo-root>/UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayFramework/
~~~

Project Unity Runtime code that uses only `Actor`, `World`, `GameMode`, or other Unity-facing types needs this reference:

~~~json
{
  "references": [
    "CycloneGames.GameplayFramework.Runtime"
  ]
}
~~~

Code that directly constructs or queries `ParticipantRoster`, `PlayerLoginRequest`, `MatchTimestamp`, `MatchStateMachine`, `MatchState`, `MatchStateSnapshot`, `PlayerStateSnapshot`, `WorldRuntimeLimits`, `ActorAdmissionSnapshot`, or `ActorTagLimits` also adds:

~~~json
{
  "references": [
    "CycloneGames.GameplayFramework.Core",
    "CycloneGames.GameplayFramework.Runtime"
  ]
}
~~~

A pure rules or protocol Unity asmdef can reference `CycloneGames.GameplayFramework.Core` alone and set `noEngineReferences: true`. This engine independence applies inside Unity's assembly graph; distributing the source as a standalone .NET artifact requires a separate .NET project/package build and target-framework validation. Project code should also add every other assembly it uses directly, including UniTask or an optional companion integration assembly when its APIs appear in project code. Do not edit Unity-generated csproj or solution files.

Sample asmdefs target both Runtime and Editor, so their Prefab components remain available in Player builds. They are not auto-referenced; project code that calls sample APIs must add the assembly reference explicitly. `GameplayWorldHost` is the sealed Unity composition root. Manual bootstrap code and DI containers provide the same `GameplayWorldComposition` value before startup; applications that own another loop can construct `GameInstance` directly.

### Logging Integration

GameplayFramework only produces logs. Its package dependency is `com.cyclone-games.logging`; every assembly that writes records directly references `CycloneGames.Logging.Core` and uses the `CycloneGames.Logging` API namespace. Runtime and sample records use the stable `CycloneGames.GameplayFramework` category, while Editor records use `CycloneGames.GameplayFramework.Editor`.

The module does not initialize, own, or shut down a concrete backend. When the application has not installed an `ILogWriter`, the process writer is `NullLogWriter` and all records are safe no-ops. Only the application composition root should install or replace a writer through `LogRuntime`. For standard Unity Console and file outputs, compose `com.cyclone-games.logging.pipeline` with `com.cyclone-games.logging.unity`; `LoggingBootstrap` then owns the pipeline lifecycle. Neither backend package is a GameplayFramework dependency.

Runtime, Editor, and the PureUnity sample own assembly-local facades at `Runtime/Scripts/Diagnostics/GameplayFrameworkLog.cs`, `Editor/Diagnostics/GameplayFrameworkEditorLog.cs`, and `Samples/Sample.PureUnity/Diagnostics/GameplayFrameworkSampleLog.cs`. Every facade exposes `Category`, `Channel`, and `Create(ILogWriter logWriter)`. Package-local ambient fields are named `Log`; explicitly injected instance fields are named `_log`. All records use these cached channels with the shared `Trace`, `Debug`, `Info`, `Warning`, `Error`, and `Fatal` methods rather than platform-native logging or concrete pipeline APIs. Exceptions use the matching severity overload with the complete `Exception` and a message describing the failed operation. Ordinary logs containing dynamic values use deferred generic-state builders. Project extensions should define the same kind of facade in the assembly that produces the logs instead of initializing a backend from the extension. Ambient channels resolve the current process writer on every write, so a successful `LogRuntime.TryReplaceWriter(expected, replacement)` handoff does not require GameplayFramework reinitialization.

Logging adds no serialized state and writes no files by itself. Persistence, rotation, flushing, shutdown, and corruption recovery belong to the selected backend and its application-level owner.

## Quick Start

### Prepare Prefabs

Create GameObject prefabs containing one framework class component on the prefab root:

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

Dedicated Server mode always uses zero local players. The Host starts before ordinary Actor `Start` callbacks, owns the GameInstance, creates an early Update/FixedUpdate driver and a late LateUpdate driver, exposes runtime status and failure diagnostics, and disposes the World when its GameObject is destroyed. Disabling the Host component pauses both drivers without changing the World lifecycle; keep the Host enabled until stop or disposal.

Direct Reference requires no resolver. Asset Reference and Path require an explicit `IWorldSettingsReferenceResolver`; the WorldSettings section describes the resolver contract and the optional AssetManagement companion package. If the project's DI container already owns the application lifetime, construct and dispose `GameInstance` directly without adding a Host.

### Expected Standalone Result

After `StartWorldAsync` completes:

- `GameInstance.CurrentWorld` is non-null;
- `World.LifecycleState` is `Playing`;
- `World.GameMode` is running;
- every configured LocalPlayer is associated with a PlayerController;
- every non-spectator Controller has a PlayerState and a possessed Pawn;
- an authoritative World exposes GameState when the GameMode prefab assigns `gameStateClass`; a Client World exposes it after a replication adapter commits a registered instance;
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
| `IActorLifetime` | Required Actor creation-and-release boundary |
| `localPlayerCount` | Number of persistent local-user slots, from 0 through `MaxLocalPlayers` |
| `IWorldSettingsReferenceResolver` | Optional external WorldSettings asset loader |
| `ISceneTransitionHandler` | Optional scene-navigation adapter |
| `WorldRuntimeLimits` | Optional immutable Actor admission and initial collection-capacity profile |
| `IWorldActorSource` | Optional, explicit source of externally owned Actors to register during World initialization |
| `IMatchClock` | Optional match-clock domain; defaults to `UnityMatchClock.Scaled` |
| `ICameraOutputLeaseArbiter` | Optional camera-resource ownership domain; defaults to a new `CameraOutputLeaseArbiter` for this GameInstance's composition domain |

`LocalPlayer` contains a stable `Index` and the current world-scoped `PlayerController`. Controller logout, World stop, and GameInstance disposal clear this association.

One GameInstance accepts only one active World. Call and await `StopWorldAsync` before starting the next World. Calling public `World.ShutdownAsync` or `World.Dispose` directly performs the same ownership cleanup and notifies the owning GameInstance to clear `CurrentWorld`. A reentrant stop while the World is already `Stopping` does not release `CurrentWorld`; replacement start remains rejected until disposal completes.

### Host Composition and DI

`GameplayWorldComposition` is the single Host dependency boundary for manual bootstrap and DI. It contains the required `IActorLifetime` plus optional reference resolution, scene transition, session, World runtime limits, Actor source, match clock, and camera-output lease arbiter. Configure the sealed Host before it starts:

~~~csharp
var sharedCameraOutputLeaseArbiter = new CameraOutputLeaseArbiter();
var composition = new GameplayWorldComposition(
    actorLifetime,
    referenceResolver: referenceResolver,
    sceneTransitionHandler: sceneTransitionHandler,
    gameSession: gameSession,
    runtimeLimits: runtimeLimits,
    actorSource: new SceneWorldActorSource(gameObject.scene),
    matchClock: UnityMatchClock.Unscaled,
    cameraOutputLeaseArbiter: sharedCameraOutputLeaseArbiter);

host.Configure(composition);
await host.StartWorldAsync(cancellationToken);
~~~

The caller retains ownership of supplied services and keeps them valid until the Host has stopped. An unconfigured `GameplayWorldHost` uses `UnityActorLifetime`, a `SceneWorldActorSource` fixed to the Host GameObject's scene, `UnityMatchClock.Scaled`, and a new `CameraOutputLeaseArbiter`. An explicitly configured Host uses the composition exactly as supplied: a null `ActorSource` disables startup discovery. A directly constructed `GameInstance` also performs no scene scan when `actorSource` is null. A DI container supplies the same constructor arguments; it does not require a GameplayWorldHost subclass or a container-specific Runtime assembly.

Host startup is a single transaction. A second start while the first is pending is rejected. `StopWorldAsync` during startup cancels the pending transaction and waits for its rollback. Pre-cancelled starts, resolver faults, and destruction during an await release the temporary GameInstance; a non-disposed Host returns to `Stopped` and can start again after the failed transaction has completed.

`IActorLifetime.Create` and `Release` are paired for every World-owned Actor, including failed spawn transactions, self-destruction, and shutdown. Implementations must accept the Actor even when Unity destruction has already occurred. `Release` must permanently end that Actor instance; returning an Actor that has reached its terminal `Destroyed` lifecycle state to a pool is unsupported. Projects that use CycloneGames.Factory install the Factory companion package and compose `FactoryActorLifetime`; Factory types do not appear in GameplayFramework Runtime interfaces.

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
5. Ask the configured `IWorldActorSource`, when present, to collect externally owned Actors and register its non-null, non-duplicate results.
6. Spawn and initialize GameMode in authoritative Worlds.
7. On authority, create GameState from the GameMode prefab's `gameStateClass` when configured.
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
6. Active camera outputs are deactivated and their exclusive resource leases are released.
7. External WorldDefinition leases are released in reverse acquisition order.
8. World enters `Disposed`, and GameInstance clears `CurrentWorld`.

`GameMode.TravelToLevel` first stops the World with `EndPlayReason.Travel`, then calls `ISceneTransitionHandler.ChangeScene`. The destination scene creates its own World. Capture any data that must cross scenes before requesting travel.

`GameInstance.Dispose` cancels its lifetime, immediately shuts down the World with `ApplicationShutdown`, clears LocalPlayer associations, and releases its cancellation source.

## WorldSettings and WorldDefinition

### Authoring and Runtime Responsibilities

`WorldSettings` is a ScriptableObject authoring asset. Each class entry selects a prefab whose root contains exactly one component of the required framework type. Runtime startup resolves these prefab classes into an immutable `WorldDefinition`; Runtime code reads the definition through `World.Definition`.

| Reference | Required | Runtime purpose |
| --- | --- | --- |
| GameMode | Yes | Authoritative rules and player orchestration |
| PlayerController | Yes | Participant Controller spawning |
| Pawn | Yes | Default non-spectator Pawn spawning |
| PlayerState | Yes | Participant identity/state spawning |
| CameraManager | No | Local camera runtime |
| SpectatorPawn | No | Spectator possession |

An authoritative GameState is configured through the GameMode prefab's `gameStateClass`; when that field is empty, authority startup leaves `World.GameState` null. A Client World receives its instance through the explicit replication boundary described under Familiar Runtime Queries.

The `GameMode`, `PlayerController`, `Pawn`, and `PlayerState` prefab classes are required. `CameraManager` and `SpectatorPawn` are optional. The WorldSettings Inspector validates required entries, prefab roots, reference source, and external location. Runtime configuration remains prefab-based: the fields select Actor classes to instantiate, not live scene objects.

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

### AssetManagement Companion

The sibling package `com.cyclone-games.gameplay-framework-asset-management` provides `AssetManagementWorldSettingsReferenceResolver`. It receives an explicit `IAssetPackage`, supports `AssetReference`, and transfers successful asset handles to `WorldDefinition` as leases. It does not support `PathLocation`.

~~~csharp
var resolver =
    new AssetManagementWorldSettingsReferenceResolver(assetPackage);

var instance = new GameInstance(
    new UnityActorLifetime(),
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
- `ApplicationShutdown`;
- `RemovedFromWorld`.

### Primary Actor Tick

Primary Actor Tick is an optional World-lifecycle service and does not replace all Unity update mechanisms. An Actor with `ActorTickPhase.None` never enters a Tick registry and receives no per-frame framework callback.

~~~mermaid
flowchart TD
    PL["Unity PlayerLoop"] --> ED["GameplayWorldTickDriver<br/>Update / FixedUpdate"]
    PL --> LD["GameplayWorldLateTickDriver<br/>LateUpdate"]
    ED --> GI["GameInstance.Tick"]
    LD --> GI
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

Actor remains a MonoBehaviour, so a dedicated subclass or sibling component may still declare native Unity messages. Unity owns those messages; World registration and lifecycle gates do not control them. They are appropriate for narrow Unity-facing responsibilities whose lifecycle follows the component. `DisallowMultipleComponent` enforces one Actor identity per GameObject; additional gameplay capabilities belong in sibling components or composed pure C# objects.

One Actor has only one primary phase. Components requiring Unity physics callbacks, Animator callbacks, rendering callbacks, worker-scheduled simulation, or multiple independent phases should continue to use a focused MonoBehaviour adapter or a pure C# simulation owner. GameplayAbilities, movement, projectile, and presentation modules do not become Actor-Tick-driven merely because an Actor owns them.

`GameplayWorldHost` creates two sealed drivers at Runtime. `GameplayWorldTickDriver` has execution order `-9999` and forwards Update and FixedUpdate early. `GameplayWorldLateTickDriver` has execution order `9999` and forwards LateUpdate after ordinary default-order LateUpdate callbacks, allowing camera evaluation to observe their completed transforms. Projects that compose `GameInstance` directly must forward each selected Unity phase or custom-loop phase exactly once through `GameInstance.Tick`.

### Registration and Ownership

| API | World registry | Begin/End notification | World destroys GameObject |
| --- | --- | --- | --- |
| Configured `IWorldActorSource` | Yes | Yes | No |
| `RegisterActor` | Yes | Yes | No |
| `UnregisterActor` / `TryUnregisterActor` | Removes registration | EndPlay with the supplied reason | No |
| `SpawnActor` | Yes | Yes | Yes |
| `SpawnActorDeferred` | Yes | After Finish | Yes |

`IWorldActorSource.CollectActors(IWorldActorCollector)` is a cold-path composition boundary. The transaction-scoped collector exposes `Count`, `RemainingCapacity`, and `TryAdd`; a source must stop when `TryAdd` returns false and must not retain the collector. World ignores destroyed references, duplicate candidates, and Actors already registered by an earlier candidate. Exceeding `WorldRuntimeLimits.MaximumActorCount` aborts discovery before any collected candidate is bound. World shutdown or replacement during the callback also aborts the transaction, so a source cannot bind an Actor into a terminal World. `SceneWorldActorSource` traverses active and inactive children beneath the roots of one explicitly selected, loaded Scene without materializing a scene-wide Actor list. Its immutable `MaximumVisitedGameObjectCount` bounds traversal memory and work; exceeding that budget aborts initialization without partial registration. It records its creating thread, requires collection on that thread, and can be reused when a replacement World starts for the same loaded Scene. It never scans another loaded Scene.

`UnregisterActor` and `TryUnregisterActor` are available only for externally owned Actors. They remove the Actor from Tick and gameplay registries, publish EndPlay, clear the World binding and related Controller, PlayerStart, or GameState bookkeeping, and leave the GameObject alive. They reject World-owned Actors because those instances must complete their lifetime through `DestroyActor`.

`DestroyActor` is the explicit destruction command for any registered Actor. It removes the Actor from the World, publishes EndPlay, and destroys the GameObject. Play Mode uses the normal Unity destruction boundary; Edit Mode destroys it immediately. The injected `IActorLifetime` releases only World-owned spawned Actors; explicitly destroying a non-owned registered Actor uses the core Unity destruction helper and does not call the injected lifetime.

Destroying a committed PlayerController or its PlayerState escalates to participant logout so roster, GameState, LocalPlayer, Pawn, camera, and spectator state are cleaned up as one operation. Destroying the active GameMode while the World is `Initializing` or `Playing` escalates to complete World shutdown.

The Actor registry uses swap-back removal. Registry order, and the first result returned by `TryGetActor<T>`, are not stable selection policies.

Diagnostics and low-frequency tools can call `TryGetActorRegistration` over `0..ActorCount`. The call returns a readonly value and does not create a collection snapshot. Any Actor removal invalidates existing indices, so indices must not be persisted. Unity Actor references must be read on the main thread.

### World Runtime Limits and Admission

Core `WorldRuntimeLimits` is immutable and is supplied through `GameInstance` or `GameplayWorldComposition`. Runtime consumes this value directly when it constructs a World; no mirrored Unity configuration object or per-frame conversion is involved. It controls the per-World admission limit and construction-time capacities:

| Property | Default | Purpose |
| --- | ---: | --- |
| `MaximumActorCount` | `65,536` | Product-selected admission limit for this World |
| `InitialActorCapacity` | `128` | Actor registry, lifecycle scratch, and Actor index capacity |
| `InitialUpdateTickCapacity` | `128` | Update registry and reusable Tick scratch planning |
| `InitialFixedUpdateTickCapacity` | `32` | FixedUpdate registry planning |
| `InitialLateUpdateTickCapacity` | `32` | LateUpdate registry planning |

Every initial capacity must be non-negative. A value above the configured maximum is capped to that maximum. The capacities reserve managed collection storage; they do not pre-create Actors and do not prevent later growth up to `MaximumActorCount`. Choose them from measured scene and gameplay workloads rather than setting all values to the ceiling.

`WorldRuntimeLimits.MaximumSupportedActorCount` (`65,536`) is the absolute implementation ceiling, not the active budget of every World. Use `TrySpawnActor`, `TrySpawnActorDeferred`, and `TryRegisterActor` when capacity rejection is expected. `SpawnActor`, `SpawnActorDeferred`, and `RegisterActor` throw `InvalidOperationException` when the configured maximum has been reached. Existing Actor Tick, destruction, unregistration, and shutdown remain available at capacity.

`GetActorAdmissionSnapshot()` returns a Core `ActorAdmissionSnapshot` containing `ActorCount`, configured `MaximumActorCount`, current `AllocatedActorCapacity`, `PeakActorCount`, and the monotonic `RejectedAdmissionCount` without creating a collection snapshot. Use allocated capacity, peak, and rejection values for diagnostics and capacity tuning; they reset with the World and are not persisted.

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
- exact ordinal tags bounded by Core `ActorTagLimits` (up to 64 tags of at most 128 characters each);
- generic, point, and radial damage dispatch;
- cancellable lifespan;
- optional World-scoped primary Tick;
- `HasAuthority`, which returns true before the Actor joins a World and follows World authority afterward;
- `FellOutOfWorld` and `KillZVolume`.

Actor owner, Controller possession, and World ownership are independent relationships.

Tag bulk operations use span boundaries:

~~~csharp
int copied = actor.CopyTagsTo(destinationSpan);
actor.ReplaceTags(replacementSpan);
~~~

`CopyTagsTo(Span<string>)` writes into caller-owned storage. `ReplaceTags(ReadOnlySpan<string>)` validates the complete input before mutating the Actor, removes duplicate ordinal values, and accepts an empty span to clear the set. These overloads do not require an `IEnumerable<string>` adapter, boxing, or creation of a transfer array when the caller already owns compatible span storage. Both operations remain bounded by `ActorTagLimits`; replacement is an owner-thread mutation.

### Familiar Runtime Queries

Common gameplay queries are available without a global service locator:

~~~csharp
World world = actor.GetWorld();
GameInstance instance = actor.GetGameInstance();
MyGameMode mode = actor.GetAuthGameMode<MyGameMode>();
MyGameState state = actor.GetGameState<MyGameState>();

World currentWorld = instance.GetWorld();
GameInstance owner = world.GetGameInstance();
MyGameMode worldMode = world.GetAuthGameMode<MyGameMode>();
MyGameState worldState = world.GetGameState<MyGameState>();
PlayerController firstPlayer = world.GetFirstPlayerController();
PlayerController player = world.GetPlayerController(index);
~~~

`GetAuthGameMode` returns null in a non-authoritative World. Generic getters use safe casts and also return null when the active object does not match the requested type. `GameInstance.GetWorld()` returns the current World. On a Client World, a replication adapter registers the received GameState Actor and commits it with `World.SetReplicatedGameState`; replacing a non-null committed instance is rejected, and destroying or explicitly clearing that Actor releases the World reference before another instance can be committed. The adapter exposes received Controllers through `GetFirstPlayerController`/`GetPlayerController` by calling `CommitReplicatedPlayerController` after the Controller and its PlayerState are registered and the Controller is initialized; an optional LocalPlayer must be the exact slot owned by that GameInstance. Destroying the Controller clears the committed World and LocalPlayer associations. These APIs are scoped to the object graph and do not create ambient global state.

## GameMode Login and Roster

### Authority and Lifecycle

GameMode exists only in authoritative Worlds. Its states are:

~~~text
Uninitialized -> Initialized -> Starting -> Running -> Stopping -> Stopped
~~~

Initialization composes the supplied `IGameSession` or creates a bounded `GameSession`. `GameModeConfig` currently applies default spectator rules. A game-specific configuration asset can inherit it and override `ApplyTo`.

### Login Request Boundary

Core `PlayerLoginRequest` enforces these limits before a Runtime login transaction begins:

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

`PlayerLoginResult.Status` uses Core `PlayerLoginStatus` and reports:

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

`GameSession` is the sealed Runtime facade over one Core `ParticipantRoster`. The roster indexes non-negative PlayerId values, rejects duplicate identities, enforces player and spectator capacities, and updates category counts atomically. The facade keeps the admitted `PlayerController`/`PlayerState` binding in its Runtime dictionary so World cleanup and possession remain direct. Product-specific session behavior is composed through `IGameSession`, including DI-provided implementations; GameSession transactions are not subclass extension points.

Registration gives one GameSession an exclusive identity lock on the PlayerState until `UnregisterPlayer`; the same PlayerState cannot be registered with another session concurrently. Change a registered participant's spectator category through `TrySetSpectatorStatus`. On the owner thread, that method first commits the Core roster change, then updates PlayerState and the Runtime binding as one facade operation. Registration rejected for identity or capacity returns before modifying PlayerState. Direct changes to PlayerId or spectator status that would break consistency with a registered entry are rejected.

Capacities are constructor arguments. Each capacity and their sum are limited by `ParticipantRoster.MaximumSupportedParticipants`. The default implementation has a single-thread owner.

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

GameMode first selects a PlayerStart by exact portal/GameObject name and then calls `ChoosePlayerStart`. The base implementation selects the first cached start. Actor-source and registry order are not stable selection policies, so override `ChoosePlayerStart` when spawn selection must be deterministic.

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

Possession is exclusive. It does not set Actor owner or change the World's destruction ownership. Public `Possess`/`TryPossess` and `UnPossess` are non-virtual transaction boundaries. Subclasses extend committed behavior through `OnPossess` and `OnUnPossess` rather than replacing relationship management.

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

`CaptureSnapshot` creates a Core `PlayerStateSnapshot` containing:

- `PlayerName`;
- `PlayerId`;
- `IsSpectator`.

`TryRestoreSnapshot` validates ID and name bounds before mutating state. The snapshot excludes Pawn, Controller, Transform, Unity object references, and World membership. A save or network adapter owns serialization, envelope metadata, and storage. `PlayerStateSnapshot` is a readonly value type; capturing it does not allocate a snapshot object.

### GameState

GameState contains the participant `PlayerArray` and composes one Core `MatchStateMachine`. It rejects null or duplicate PlayerState entries and validates World membership. The serialized `initialMatchState` field is authoring input for the first state machine; Runtime transitions and restore operations do not write back into that field.

The state machine owns legal transitions, committed `MatchState`, and elapsed in-progress time in one explicit clock domain. `GameplayWorldComposition` and `GameInstance` accept an `IMatchClock`. During Actor registration, World configures every GameState immediately after binding it and before registry commitment or BeginPlay publication; authoritative spawn and client replication therefore observe the same clock-ordering contract. `UnityMatchClock.Scaled` is the default, while `UnityMatchClock.Unscaled` is available when pause and `Time.timeScale` must not stop match time. A server or deterministic simulation can supply another `IMatchClock` without introducing Unity types into Core.

Every clock reading is a `MatchTimestamp` containing a non-empty `Guid` epoch and finite, non-negative `double` seconds. The epoch prevents scaled, unscaled, restarted, or otherwise unrelated time domains from being combined. The state machine also rejects timestamps that move backwards and confines mutable access to its constructing or restoring thread.

Valid match transitions are:

| Current state | Allowed next state |
| --- | --- |
| EnteringMap | WaitingToStart, LeavingMap, Aborted |
| WaitingToStart | InProgress, LeavingMap, Aborted |
| InProgress | WaitingPostMatch, LeavingMap, Aborted |
| WaitingPostMatch | WaitingToStart, LeavingMap, Aborted |
| LeavingMap | None |
| Aborted | None |

Elapsed time advances only during InProgress. A transition from WaitingPostMatch to WaitingToStart resets the accumulated time. Core transition rules can be tested without a GameObject or Unity clock.

GameMode owns transition policy. Use `TrySetMatchState` when a recoverable result is required; use `SetMatchState` when an illegal transition is a programming error.

`GameState.ElapsedTimeSeconds` exposes the accumulated value as `double`. `CaptureMatchStateSnapshot` returns a readonly Core value with `State`, `ElapsedSeconds`, `CapturedTimestamp`, and `ClockEpoch`. `TryRestoreMatchStateSnapshot` restores only when the current clock has the same epoch and has not moved behind the captured timestamp. If the captured state is InProgress, elapsed time includes the same-epoch interval between capture and restore. `RestoreMatchStateSnapshot` throws when restore rejection is a programming error.

`MatchStateSnapshot` is runtime state, not a persistence or wire schema. A save, reconnect, or replication adapter owns its envelope, product format, validation, and clock-epoch continuity. A process-local `UnityMatchClock` epoch changes after a domain reload or process restart, so snapshots intended to cross that boundary require an application-owned clock whose epoch and monotonic timebase remain meaningful there.

`MatchState` is the top-level Core enum used by both `MatchStateMachine` and the Runtime GameState facade:

~~~csharp
if (!gameState.TrySetMatchState(MatchState.InProgress, out string error))
{
    throw new InvalidOperationException(error);
}
~~~

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
    OUT --> IO["ICameraOutput.ApplyPose"]
    IO --> UC["UnityCameraOutput"]
    IO -. optional .-> CM["CinemachineCameraOutput"]
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

### CameraManager and Output Ownership

During GameMode login, CameraManager is created only for a local PlayerController and only when WorldDefinition contains a CameraManager prefab.

It:

- evaluates camera state through primary Actor Tick in the LateUpdate phase after initialization;
- resolves an authored `CameraOutputBehaviour` or accepts an explicit `ICameraOutput` through `SetCameraOutput`;
- prepares the backend's complete ownership-resource set and asks the World for one atomic lease;
- activates the output only after every prepared resource has been acquired;
- publishes the final pose and FOV through `ApplyPose`;
- deactivates the output and releases ownership during replacement, Actor teardown, or World shutdown.

`ICameraOutput.TryPrepare` resolves between one and `CameraOutputLimits.MaximumPreparedResourceCount` (4) stable Unity resources. `PreparedResourceCount` and `GetPreparedResource` expose that bounded set. The composed `ICameraOutputLeaseArbiter` validates non-null, distinct resources, rejects the complete request if any resource is already leased in its ownership domain, and then returns one generation-safe `CameraOutputLease` covering all of them. Acquisition is all-or-nothing; a failed activation rolls back prepared backend state and the entire lease.

`CameraOutputLeaseArbiter` is a composition-owned, owner-thread-affine registry with no static global ownership state. `GameplayWorldComposition` creates and retains one when none is supplied; a directly constructed GameInstance creates its own default and passes the same instance to every World it creates. One GameInstance still accepts only one active World, so this domain remains continuous across replacement Worlds. Parallel Worlds belong to separate GameInstances: if their outputs can reference the same persistent Camera, CinemachineBrain, or other backend resource, inject the same arbiter instance into every participating composition. Independent default arbiters intentionally represent independent resource domains and cannot detect overlap between those Worlds.

Within one arbiter domain, two CameraManagers cannot hold overlapping prepared sets even when their output components or Worlds differ. Release checks World identity, owner, output identity, lease generation, and complete resource IDs before removing ownership. The arbiter is created and mutated on one owner thread; all sharing GameInstances and Worlds must perform lease operations on that thread. Destroyed Unity resources are treated as unavailable, while managed reference identity remains the ownership token. World shutdown asks the arbiter to release all leases for that World. Output destruction, replacement, and activation exceptions also converge on deactivation and lease release. CameraManager teardown clears its pose, blend, dirty-state, and backend references before reuse. A CameraManager without an output continues evaluating and exposing `CurrentPose`; output is an optional presentation boundary.

### Core Unity Camera Output

`UnityCameraOutput` is included in the GameplayFramework Runtime assembly. Assign a `UnityEngine.Camera` explicitly or place one on the output hierarchy. The component can apply the final transform, field of view, or both. It requires no camera package and is the output used by the PureUnity sample.

GameplayFramework Runtime and its public interfaces contain no Cinemachine type. Custom backends implement `ICameraOutput` directly or derive from `CameraOutputBehaviour` for Unity authoring. The prepared count and resource identities must remain stable until `Deactivate` releases prepared state. `TryPrepare`, `TryActivate`, `ApplyPose`, and `Deactivate` run on the World owner thread. `UnityCameraOutput` prepares exactly one ownership resource: its target Camera.

### Optional Cinemachine Output

When `com.unity.cinemachine` in the supported `[3.0.0,4.0.0)` range is installed, the gated assembly `CycloneGames.GameplayFramework.Runtime.Integrations.Cinemachine` provides `CinemachineCameraOutput`. Its asmdef uses `versionDefines`, `defineConstraints`, and `autoReferenced: false`; the GameplayFramework package does not declare Cinemachine as a dependency.

Assign a `CinemachineCamera` and `CinemachineBrain` with `SetVirtualCamera`/`SetBrain` or serialized fields. Built-in discovery inspects only the `CinemachineCameraOutput` GameObject's Scene and succeeds only when it can choose an unambiguous camera and brain there; it never selects a candidate from another loaded Scene. The output prepares both the Brain and Virtual Camera as one resource domain, so sharing either object conflicts atomically. On activation, it stores the brain update mode and camera Follow/LookAt targets, selects manual brain updates, applies pose and lens, and restores the stored state on deactivation or rollback.

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

GameplayFramework Core has no engine references. GameplayFramework Runtime depends on Core plus its declared Unity-facing package and asmdef references. Optional product bridges either live in a sibling companion package or in a gated integration assembly whose third-party dependency can be absent.

| Package | Assembly | Capability | Enablement |
| --- | --- | --- | --- |
| GameplayFramework package | `CycloneGames.GameplayFramework.Runtime.Integrations.Cinemachine` | `CinemachineCameraOutput` | Explicit reference; compiled only for supported Cinemachine versions |
| `com.cyclone-games.gameplay-framework-factory` | `CycloneGames.GameplayFramework.Runtime.Integrations.Factory` | `FactoryActorLifetime` | Install companion and Factory; explicit reference |
| `com.cyclone-games.gameplay-framework-asset-management` | `CycloneGames.GameplayFramework.Runtime.Integrations.AssetManagement` | `AssetManagementWorldSettingsReferenceResolver` | Install companion; explicit reference |
| `com.cyclone-games.gameplay-framework-gameplay-abilities` | `CycloneGames.GameplayFramework.Runtime.Integrations.GameplayAbilities` | AbilitySystem provider and Actor-info helpers | Install companion; explicit reference |
| `com.cyclone-games.gameplay-framework-gameplay-tags` | `CycloneGames.GameplayFramework.Runtime.Integrations.GameplayTags` | Actor tag-container extension methods | Install companion; explicit reference |
| `com.cyclone-games.gameplay-framework-networking` | `CycloneGames.GameplayFramework.Networking.Core` | Pure protocol, security-policy composition, Actor migration codec, and damage validation | Install companion and Networking; explicit reference from protocol code |
| `com.cyclone-games.gameplay-framework-networking` | `CycloneGames.GameplayFramework.Networking.Runtime` | Actor capture/apply, shared replication capture, and Runtime session adapters | Explicit reference from Unity replication code |
| GameplayFramework package | `CycloneGames.GameplayFramework.Runtime.Integrations.Navigathena` | `ISceneTransitionHandler` adapter | Explicit reference; conditionally compiled |

### Factory Actor Lifetime

The Factory companion adapts Factory's `IUnityObjectLifetime` to core `IActorLifetime`. Supply `FactoryActorLifetime` through `GameplayWorldComposition` or directly to `GameInstance`. World remains the sole owner of created Actor instances and pairs every successful creation with one release. This boundary is intended for creation/destruction policy; it does not make terminal Actor instances reusable. UPM installation resolves the companion's declared GameplayFramework and Factory dependencies. In an embedded `Assets` layout, its physically separate asmdef references both modules directly; no PlayerSettings define is required.

### AssetManagement

Install the AssetManagement companion when WorldSettings entries use `AssetReference`. Compose an explicit `IAssetPackage` and pass the resolver through `GameplayWorldComposition` or directly to GameInstance.

### GameplayAbilities

Implement `IAbilitySystemProvider` on an Actor or one of its components, then use:

- `TryGetAbilitySystem`;
- `InitializeAbilityActorInfo`.

Owner and avatar overrides are explicit parameters. Without overrides, the helper uses Actor owner when available and uses the Actor as the avatar.

The GameplayAbilities companion does not schedule `AbilitySystemComponent.Tick`. The ability-system owner selects its clock and forwards it explicitly. When a World-lifecycle gate is required, a GameplayFramework Actor can forward from primary Tick; an independent Unity composition can retain a dedicated MonoBehaviour driver. Movement and physics components continue to own their own phases.

### GameplayTags

Add `GameObjectGameplayTagContainer` to the Actor GameObject. The integration provides:

- `TryGetGameplayTagContainer`;
- `ActorHasGameplayTag`;
- `AddGameplayTag`;
- `RemoveGameplayTag`.

Actor's lightweight string tags and the GameplayTags container are independent APIs.

These extension methods perform component discovery and should be used only during composition, initialization, and other cold paths. Code that checks or modifies tags repeatedly should call `TryGetGameplayTagContainer` once, retain the returned container for the Actor/component lifetime, and use the cached reference directly. The integration provides no hidden per-frame cache and does not take ownership of the container.

The GameplayTags integration is delivered by its sibling companion package, which owns the GameplayTags dependency. GameplayFramework Core and Runtime do not reference GameplayTags. Consumers explicitly reference the companion integration assembly from their own asmdef; `autoReferenced: false` prevents unrelated assemblies from acquiring the API implicitly.

### Networking

The Networking sibling package provides two explicit layers. `CycloneGames.GameplayFramework.Networking.Core` has no engine references and owns protocol messages, bounds, codecs, security-policy composition, and validation. Shared `CycloneGames.Networking.Core` owns replication objects, observers, policies, budgets, and `NetworkReplicationPlanner`. `CycloneGames.GameplayFramework.Networking.Runtime` owns `NetworkGameSessionAdapter`, Actor capture/apply, replication snapshot capture, and every adapter that reads or mutates Unity gameplay objects.

`NetworkGameSessionAdapter` can be supplied as the authoritative `IGameSession`. A client replication adapter registers its received GameState Actor and calls `World.SetReplicatedGameState`; it registers and initializes each received PlayerController and PlayerState before calling `World.CommitReplicatedPlayerController`. Both client-only commit boundaries run on the World owner thread and reject authority Worlds. Transport callbacks must be authenticated and marshalled to that thread before they call gameplay APIs.

Protocol code references Networking Core. Unity replication code references Networking Runtime and every GameplayFramework assembly whose types it uses directly. GameplayFramework Core and Runtime do not reference the Networking companion.

Actor migration keeps its transport value in Networking Core. `ActorMigrationState` stores `NetworkVector3`, `NetworkQuaternion`, prefab definition ID, bounded readonly tags, and other transport-facing Actor state without a Unity object reference. Networking Runtime provides `ActorNetworkingExtensions.CaptureMigrationState` and `ApplyMigrationState` to convert between this value and an Actor. `CaptureReplicationObject` samples Actor state into the shared `CycloneGames.Networking.Replication.NetworkReplicatedObject` consumed by `NetworkReplicationPlanner`. Observer construction and replication planning use the shared Networking contracts directly. These conversions belong at explicit replication or travel boundaries, not in Actor Tick.

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
        new UnityActorLifetime(),
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

When `GameplayWorldHost` owns the GameInstance, provide the Navigator through composition before the Host starts:

~~~csharp
var sceneTransitions =
    new NavigathenaSceneTransitionHandler(sceneNavigator);

host.Configure(new GameplayWorldComposition(
    new UnityActorLifetime(),
    sceneTransitionHandler: sceneTransitions));
~~~

The project composition root calls `Configure` before Unity invokes the Host's `Start`. If it cannot guarantee that order, disable **Auto Start** and call `StartWorldAsync` after configuration completes. `GameplayWorldHost` is sealed; integration is performed through `GameplayWorldComposition`, not inheritance.

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

The integration asmdef references its dependency assemblies directly. Companion packages own their own dependencies; gated core integrations disappear from compilation when their supported optional dependency is absent.

## Editor Tools

| Tool | Capability |
| --- | --- |
| Actor Inspector | Serialized Actor fields, primary Tick authoring, derived fields, multi-object editing, Runtime lifecycle and Tick state, and Play Mode Tick enable/disable controls |
| ActorTag drawer | Searchable selection for fields marked with `ActorTagAttribute` |
| WorldSettings Inspector | Required/optional summary, Direct/Asset/Path authoring, and validation button |
| GameplayWorldHost Inspector | WorldSettings and external-resolver guidance, explicit-composition status, effective local-player count, Runtime state, and Start/Stop controls |
| GameMode Inspector | Runtime mode state and PlayerController roster with Ping |
| PlayerStart Inspector | Configurable 3D, side-scroller, and top-down scene gizmos |
| CameraManager Inspector | Configured/active output, owner, pose, blend, view target, mode, and FOV telemetry |
| CinemachineCameraOutput Inspector | Optional active CinemachineCamera and CinemachineBrain telemetry when the integration is compiled |
| CameraActionStateBehaviour Inspector | Conditional enter/exit/progress authoring and capacity guidance |
| Camera Debug Window | Buffered camera telemetry, graphs, and configurable alerts |
| World Debugger | Host, World, session, Actor admission/allocated-capacity/peak/rejection diagnostics, per-phase Tick counts, and indexed Actor-registration inspection |
| Project Validation | Read-only scan of WorldSettings assets and loaded-scene Hosts, including required external-resolver composition guidance |

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

| Data | Owner | Storage provided by the module | Version control | Lifecycle and cleanup |
| --- | --- | --- | --- | --- |
| WorldSettings | Project authoring | ScriptableObject asset | Usually tracked | Edit and validate the serialized asset |
| Actor phase and startup Tick flag | Scene/prefab authoring | Serialized MonoBehaviour fields | Usually tracked | Edit through Actor Inspector; use Runtime APIs for temporary changes |
| GameModeConfig, PawnConfig, CameraProfile | Project authoring | ScriptableObject assets | Usually tracked | Edit and validate through their Inspectors |
| CameraActionPreset, CameraActionMap | Project authoring | ScriptableObject assets | Usually tracked | Keep action keys consistent with consumer configuration |
| WorldDefinition | World Runtime | Memory only | No | Disposed with World; releases leases in reverse order |
| GameplayWorldHost, GameInstance, LocalPlayer, World | Runtime composition | Memory only | No | Host GameObject lifetime or explicit Stop/Dispose |
| CameraOutputLeaseArbiter ownership registry | Runtime composition | Memory only | No | Releases entries per World shutdown; discard after all sharing Worlds have stopped |
| PlayerStateSnapshot | Save/network adapter | Core in-memory value | Adapter-specific | Validate the persistence or protocol envelope before restore |
| MatchStateSnapshot | Save/network adapter and clock owner | Core in-memory value without a schema envelope | Adapter-specific | Restore only in a compatible clock epoch and validated product format |
| Camera debug samples | CameraDebugWindow | Editor memory only | No | Clear the buffer or close/reload the window |
| World Debugger and Project Validation state | Editor window | Editor memory only | No | Close or reload the window; no EditorPrefs or SessionState writes |

For saved data:

1. Capture `PlayerStateSnapshot` or `MatchStateSnapshot` at a controlled boundary.
2. Pass the value to a dedicated save service.
3. Include slot, format, and integrity metadata owned by the save service.
4. Write atomically to the platform persistent-data location.
5. Validate size and integrity before deserialization.
6. Validate the payload against the product format expected by the current build.
7. Call the corresponding `TryRestore...` API and handle its error, including match-clock epoch rejection.

Select and validate a serializer that supports readonly snapshot properties on the target backend; Unity `JsonUtility` does not serialize these contracts directly. Neither Core snapshot carries a storage schema version; the save or protocol envelope owns format identification and compatibility.

## Performance, Threading, and Platform Notes

### Thread Ownership

- GameInstance and World mutations run on one owner thread.
- GameInstance records the constructor thread ID.
- Actor Tick dispatch, phase changes, and Runtime enable changes use that same owner thread.
- Network, file, and asset callbacks must marshal to the Unity main thread before mutating framework state.
- Unity object and camera-output operations run on the Unity main thread. Optional camera backends follow the same rule.
- `CameraOutputLeaseArbiter` captures its construction thread; every World sharing it acquires and releases leases on that owner thread.
- WorldSettings resolver I/O can complete on other threads. Result validation, rollback, lease transfer, and WorldDefinition disposal run on the main thread performing resolution. Cross-thread WorldDefinition disposal is rejected before consuming ownership.
- `ParticipantRoster` and `GameSession` capture their constructing thread as the single owner and add no locks.
- `MatchStateMachine` captures its constructing thread; a successful `TryRestore` creates a state machine owned by the restoring thread.
- Reads of their mutable state and every mutation assert that owner. Cross-thread access fails with `InvalidOperationException` instead of observing a potentially inconsistent roster, match clock, or Runtime binding.
- Immutable limit values and static validation/transition-policy functions do not read mutable instance state. Worker results still marshal to the owner before accessing a live roster, match state machine, or session.
- Async APIs use UniTask and propagate cancellation during startup and asset resolution. World initialization links caller, GameInstance, and World lifetime tokens; direct World shutdown cancels pending async login so startup cannot continue committing.

### Bounded Structures

| Structure/input | Limit or default |
| --- | --- |
| LocalPlayer slots | At most 8 |
| World Actor registrations | Configured by `WorldRuntimeLimits.MaximumActorCount`; never above `WorldRuntimeLimits.MaximumSupportedActorCount` |
| Initial World collection capacities | Actor 128, Update 128, FixedUpdate 32, LateUpdate 32; each capped to the configured Actor maximum |
| Actor string tags | At most 64; each at most 128 characters |
| Login text inputs | name/address/options: 64/256/1024 characters respectively |
| Total GameSession participants | At most 100,000 |
| CameraContext modes | Fixed per context; default 8 |
| CameraManager post-processors | At most 16 |
| Camera output ownership resources | 1 through 4 per prepared output |
| CameraActionStateBehaviour tracking | At most 8 Animator/layer pairs |
| CameraActionBinding active/pool counts | Configurable; default 8 each |
| Actor primary Tick phase | One phase per Actor; hot-path registry size depends on Runtime-enabled Actors |

`WorldRuntimeLimits.MaximumSupportedActorCount` is an implementation safety ceiling, not a product budget. Use `WorldRuntimeLimits` to define a measured per-World maximum and initial capacities, use the `Try*` admission APIs for recoverable rejection, and monitor peak/rejection diagnostics. Roster growth is bounded separately by GameSession limits.

### Allocation Points

When profiling, inspect these cold-path or boundary operations:

- World construction when configured initial collection capacities are reserved;
- configured World Actor-source collection and growth beyond those initial capacities;
- camera-output arbiter ownership-registry growth beyond its configured initial capacity;
- WorldSettings resolution and lease-array creation;
- persistence/serializer work performed after PlayerState snapshot capture;
- first use of Actor tags and renderer buffers;
- Actor lifespan cancellation-source creation;
- Tick registry and reusable-snapshot capacity growth during Actor registration;
- CameraContext construction;
- CameraActionMap lazy-lookup construction;
- mode creation when the CameraActionBinding pool is empty;
- string parsing from timed Animation Events;
- diagnostic-window buffer resizing.

Actor Tick dispatch traverses a reusable phase snapshot and does not scan Actors whose Tick phase is None. Core rule composition adds no per-Actor mirror object or per-frame synchronization pass: one roster belongs to a GameSession and one state machine belongs to a GameState. Dense registries, fixed camera arrays, and reusable Tick collections are designed to reduce steady-state allocation after capacities stabilize. Startup, Actor creation/release, snapshot serialization, resolver work, logging backends, user callbacks, optional integrations, and collection growth can allocate. The module does not claim global zero-GC behavior; use allocation profiling with representative workloads on target hardware.

### Player, IL2CPP, and Server Builds

- `CycloneGames.GameplayFramework.Core` sets `noEngineReferences: true` and contains only engine-independent rule and value types.
- `CycloneGames.GameplayFramework.Runtime` references Core, UnityEngine, Mathematics, UniTask, and Logging contracts. Cinemachine, Factory, AssetManagement, GameplayAbilities, GameplayTags, and Networking remain isolated behind optional integration or companion assemblies.
- GameplayWorldHost uses separate sealed early and late MonoBehaviour drivers. Direct GameInstance composition must provide an equivalent loop owner.
- PlayerStateSnapshot serialization is external. Reflection-based serializers may require AOT metadata or link preservation.
- DedicatedServer mode suppresses automatic local login, but the Runtime assembly still contains its declared dependencies.
- Client mode does not provide replication by itself.
- Mono, IL2CPP, managed stripping, headless/server, and every target platform require representative Player-build validation.

Owner-thread enforcement and bounded inputs provide consistent failure rules, but the framework does not make Unity physics, floating-point results, Actor-source order, user callbacks, or third-party integrations deterministic across hardware. Products that require lockstep or replay determinism must provide a deterministic simulation model, clock, ordering policy, numeric policy, and target-platform verification above this gameplay-flow layer.

## Examples from Basic to Advanced

### Use Core Rules Without Unity Objects

Reference `CycloneGames.GameplayFramework.Core` and import `CycloneGames.GameplayFramework.Core`. A roster can validate and commit bounded participant membership without a GameObject:

~~~csharp
var roster = new ParticipantRoster(
    maximumPlayers: 2,
    maximumSpectators: 1);

ParticipantRegistrationResult registration = roster.Register(
    participantId: 42,
    category: ParticipantCategory.Player);

if (registration != ParticipantRegistrationResult.Success)
{
    throw new InvalidOperationException(
        $"Participant registration failed: {registration}");
}
~~~

The thread that constructs the roster owns it. `EvaluateRegistration`, `Register`, `Remove`, `ChangeCategory`, `Contains`, `TryGetCategory`, and `AtCapacity` enforce that owner.

A match-state machine accepts application-owned monotonic timestamps from one explicit epoch:

~~~csharp
Guid clockEpoch = Guid.NewGuid();
var enteringAt = new MatchTimestamp(clockEpoch, 10d);
var waitingAt = new MatchTimestamp(clockEpoch, 11d);
var startedAt = new MatchTimestamp(clockEpoch, 12d);
var capturedAt = new MatchTimestamp(clockEpoch, 15.5d);

var match = new MatchStateMachine(
    MatchState.EnteringMap,
    in enteringAt);
match.TryTransition(MatchState.WaitingToStart, in waitingAt);
match.TryTransition(MatchState.InProgress, in startedAt);

double elapsedSeconds = match.GetElapsedSeconds(in capturedAt);
MatchStateSnapshot snapshot = match.CaptureSnapshot(in capturedAt);
~~~

Core does not read Unity time or schedule updates. Runtime GameState reads the `IMatchClock` supplied by composition; a pure simulation or server supplies its own epoch and monotonic clock. Reuse an epoch only while its timebase remains continuous.

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

Do not add a second forwarder when GameplayWorldHost is present. A headless host can call the same API from an explicit loop, but delta must be validated, finite, and non-negative. Explicit ticking alone does not make gameplay deterministic.

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

`PlayerStateSnapshot` is a readonly Core value; `CaptureSnapshot` and `TryRestoreSnapshot` are Runtime facade APIs. A persistence integration owns file paths, serialization envelopes, atomic replacement, integrity, and encryption policy.

### Capture and Restore Match State

~~~csharp
MatchStateSnapshot snapshot = sourceGameState.CaptureMatchStateSnapshot();

if (!targetGameState.TryRestoreMatchStateSnapshot(in snapshot, out string error))
{
    throw new InvalidDataException(error);
}
~~~

The target GameState must use a clock with the snapshot's epoch, and its current timestamp must be at or after `CapturedTimestamp`. The adapter that moves the snapshot owns the storage or protocol schema and the continuity of that clock domain.

### Validate a Lightweight Actor Tag

Core provides the same bounded tag validation used by Runtime Actor operations:

~~~csharp
if (!ActorTagLimits.TryValidate(tag, out ActorTagValidationResult result))
{
    throw new ArgumentException($"Invalid Actor tag: {result}", nameof(tag));
}
~~~

`NullOrWhiteSpace` and `TooLong` are explicit validation results. An Actor additionally enforces `ActorTagLimits.MaximumTagCount` when adding or replacing tags.

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

The Core test assembly covers:

- participant capacity, duplicate identity, registration, removal, and spectator-category accounting;
- bounded login requests and `PlayerLoginStatus` values;
- match-state transitions, epoch validation, elapsed-time accounting, snapshot capture/restore, owner-thread transfer on restore, and reset rules;
- World limits, Actor admission snapshots, player snapshots, and Actor-tag bounds without UnityEngine references.

The Unity EditMode test assembly covers:

- World modes, startup, rollback, client GameState/PlayerController commit boundaries, scoped Actor-source discovery, parallel Scene isolation, non-owned Actor unregistration/reuse, trusted local-login validation, participant/GameMode destruction escalation, logout, and CurrentWorld cleanup;
- terminal Actor-lifetime pairing across successful destruction, spawn failure, self-destruction, shutdown, and throwing-release failure isolation;
- WorldSettings validation, external resolvers, cancellation, and lease disposal;
- GameplayWorldHost composition, pre-cancelled and pending-start cancellation, destruction during await, fault retry, reentrant-start rejection, immutable WorldRuntimeLimits, Actor admission/allocated-capacity/peak/rejection diagnostics, indexed World inspection, custom Inspectors, and project validation;
- Actor tags, damage, lifespan, possession, Pawn input, primary Tick phases, Runtime gates, mutation safety, re-entry rejection, exception isolation, and owner-thread enforcement;
- PlayerState snapshots, session identity locks, atomic spectator changes, and post-commit Pawn notification;
- GameState transitions, clock configuration before registration-time BeginPlay, custom match-clock composition, match snapshots, and World-scoped PlayerStart;
- CameraContext capacity, replacement, evaluation mutation guards, deferred clear, teardown order, view-target policy, and action limits;
- camera blending, camera math, core UnityCameraOutput, atomic multi-resource output ownership, shared-arbiter exclusion across parallel Worlds, activation rollback, destroyed-resource handling, reset, replacement, teardown, and failure isolation;
- CameraContext, GameSession, and 1,000 opt-in Actor Tick performance benchmarks;
- request mapping, customization, validation, and cancellation forwarding when a supported Navigathena package is installed.

Run it from Unity Test Runner:

~~~text
Window > General > Test Runner > EditMode
Assembly: CycloneGames.GameplayFramework.Core.Tests.Editor
Assembly: CycloneGames.GameplayFramework.Tests.Editor
~~~

After installing Navigathena `[1.1.0,2.0.0)`, also run:

~~~text
Assembly: CycloneGames.GameplayFramework.Integrations.Navigathena.Tests.Editor
~~~

Without Navigathena installed, confirm that neither the integration assembly nor its test assembly appears in `Library/ScriptAssemblies`, then run the Core and Unity package test assemblies above.

Run optional integration and companion tests for every package included by the product:

| Capability | EditMode test assembly |
| --- | --- |
| Cinemachine output | `CycloneGames.GameplayFramework.Integrations.Cinemachine.Tests.Editor` |
| Factory Actor lifetime | `CycloneGames.GameplayFramework.Integrations.Factory.Tests.Editor` |
| AssetManagement resolver | `CycloneGames.GameplayFramework.Integrations.AssetManagement.Tests.Editor` |
| GameplayAbilities bridge | `CycloneGames.GameplayFramework.Integrations.GameplayAbilities.Tests.Editor` |
| GameplayTags bridge | `CycloneGames.GameplayFramework.Integrations.GameplayTags.Tests.Editor` |
| Networking protocol and rule layer | `CycloneGames.GameplayFramework.Networking.Core.Tests.Editor` |
| Networking Unity adapters | `CycloneGames.GameplayFramework.Networking.Runtime.Tests.Editor` |

For a UPM consumer, validate the GameplayFramework package once with optional packages absent and once with each selected integration dependency present. Missing optional dependencies must not prevent Core, Runtime, or Editor assemblies from compiling.

Batchmode example:

Before running the command, create `<repo-root>/UnityStarter/TestResults`, or replace both output paths with an existing writable directory.

~~~powershell
<unity-editor> -batchmode -nographics -projectPath "<repo-root>/UnityStarter" -runTests -testPlatform EditMode -assemblyNames "CycloneGames.GameplayFramework.Core.Tests.Editor;CycloneGames.GameplayFramework.Tests.Editor" -testResults "<repo-root>/UnityStarter/TestResults/GameplayFramework.EditMode.xml" -logFile "<repo-root>/UnityStarter/TestResults/GameplayFramework.EditMode.log"
~~~

### PlayMode Tests

The PlayMode assembly verifies that an auto-start Host creates a Playing World; discovers Actors only from the Host's own Scene; creates separate early and late drivers; brackets ordinary MonoBehaviour Update/LateUpdate callbacks with the configured framework phases; forwards Update, FixedUpdate, and LateUpdate Actor Tick phases; stops forwarding while the Host is disabled; releases an externally destroyed World-owned Actor exactly once; and disposes the World with the Host GameObject.

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
7. Verify the local CameraManager owns its configured `UnityCameraOutput`, applies pose/FOV to the target Camera, and releases the output on stop.
8. Confirm that the sample Actor rotates only while the World is Playing.
9. Open World Debugger and inspect the World, configured Actor admission limit, allocated capacity, peak/rejection counters, per-phase Tick counts, and Actor registration.
10. Click `Disable Runtime Tick` in the sample Actor Inspector. Confirm rotation stops, the Tick Enabled diagnostic changes, and the Actor remains registered; then click `Enable Runtime Tick` to resume.
11. Run Project Validation and confirm the sample reports no configuration errors.
12. Open Camera Debug Window and observe pose/blend data.
13. Exit Play Mode and confirm no participant, Tick, or camera-mode state remains.

When Cinemachine is part of the product, repeat the camera check with `CinemachineCameraOutput`: confirm manual update is active only while owned and that brain update mode plus Follow/LookAt targets are restored after stop.

### Player and Platform Validation

For each release target:

1. Add the project Runtime composition root and required scenes to Build Settings.
2. Perform a clean Player build.
3. Cover startup, cancellation, login failure, logout, travel, and application shutdown.
4. Test both direct and external WorldSettings references.
5. Profile camera, Actor, and roster hot paths on target hardware.
6. Validate IL2CPP/AOT serializer behavior and managed stripping.
7. Run Server targets without LocalPlayers and inspect dependencies, logging, and shutdown.
8. Build with the selected Cinemachine, Factory, AssetManagement, GameplayAbilities, GameplayTags, Networking, and Navigathena integrations both present and absent according to the supported product matrix.

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
| Login returns SpawnFailed | Check prefab references, Actor-lifetime results, World state, and custom initialization callbacks |
| Player spawn point is unstable | Override `ChoosePlayerStart` or pass an exact portal name |
| Possession fails | Register and initialize the Controller, use the same World, and avoid reentrant callbacks |
| Movement input has no effect | The movement adapter must consume and apply the pending vector |
| Actor Tick does not run | Confirm the phase is not None, Runtime Tick is enabled, the component is active/enabled, BeginPlay completed, registration is not deferred, and World is Playing |
| Actor Tick reports re-entry | Do not call GameInstance.Tick or World.Tick from an Actor Tick callback; defer work to the next owned loop phase |
| Actors never Tick with direct GameInstance composition | Forward each required phase exactly once from the composition root; GameplayWorldHost supplies this automatically |
| Movement or Ability uses another update model | Retain the module's own MonoBehaviour or explicit simulation clock; Actor Tick is opt-in and does not replace package-owned scheduling |
| Scene Actor begins play outside the World barrier | Start the composition root before ordinary Actor Start callbacks |
| Direct GameInstance does not discover Scene Actors | Supply an explicit `IWorldActorSource`, such as `SceneWorldActorSource`; a null source intentionally performs no scan |
| Host discovers Actors from the wrong Scene | Leave the Host unconfigured to use its own Scene, or pass a `SceneWorldActorSource` for the exact loaded Scene through composition |
| Ended Actor cannot join another World | A non-owned scene/external Actor must be unbound before registration with a replacement World; World-owned Actors cannot re-enter |
| GameState transition is illegal | Follow the valid transition table or handle `TrySetMatchState` failure |
| Match-state snapshot restore fails | Confirm the snapshot is valid, the current `IMatchClock` has the same epoch, and its timestamp has not moved behind `CapturedTimestamp` |
| No CameraManager | Configure the optional prefab and use a local PlayerController |
| CameraManager has no output | Assign a `CameraOutputBehaviour`, call `SetCameraOutput`, or intentionally run pose evaluation without presentation output |
| Camera output ownership error | Ensure all prepared resources are alive, distinct, stable until deactivation, and leased by only one CameraManager; Cinemachine leases both its Brain and Virtual Camera |
| Parallel Worlds both acquire one persistent camera resource | Construct one `CameraOutputLeaseArbiter` on their common owner thread and inject that same instance into every participating GameInstance or Host composition |
| Camera lease arbiter reports an owner-thread error | Create and mutate the shared arbiter on the same owner thread used by every participating World |
| Cinemachine integration does not compile | Confirm `com.unity.cinemachine` in `[3.0.0,4.0.0)` is resolved and reference the gated integration assembly from a matching integration asmdef |
| Host dependency must come from DI | Build `GameplayWorldComposition`, call `Configure` before startup, and do not subclass the sealed Host |
| Actor admission returns false | Inspect configured `WorldRuntimeLimits.MaximumActorCount`, allocated capacity, peak count, and rejected admission count; release or defer work according to product policy |
| Camera-mode push returns false | Check duplicate instance, clearing/evaluation state, and CameraContext capacity |
| Camera clear does not execute immediately | A clear requested during evaluation runs after the evaluation scope ends |
| Camera action returns false | Check action key, preset, active-action limit, Controller resolution, and mode-stack capacity |
| Animator progress action does not trigger | Check progress key, threshold, transition flag, loop policy, and the 8-pair tracking capacity |
| Snapshot restore fails | Validate non-negative ID, player-name length, and registered identity/spectator locks; validate persistence or protocol envelopes before calling Runtime |
| Core type is not visible to product code | Add `CycloneGames.GameplayFramework.Core` directly to the consumer asmdef |
| Pure rules assembly unexpectedly depends on UnityEngine | Remove GameplayFramework Runtime and Unity adapter references; reference Core directly |
| Networking protocol compiles but Actor adapters are missing | Add `CycloneGames.GameplayFramework.Networking.Runtime` to the Unity replication asmdef |
| Travel reports no handler | Compose `ISceneTransitionHandler` into GameInstance |
| Sample script is missing from a Player build | Confirm the sample asmdef includes the target platform and resolve every compilation error before building |
