# CycloneGames GameplayFramework

[简体中文](README.SCH.md)

This module provides a Unity gameplay-flow foundation organized around the familiar `GameInstance -> World -> GameMode -> Controller -> Pawn -> PlayerState -> GameState` runtime chain. Its common query APIs, authority concepts, player admission, possession, and camera orchestration follow Unreal Engine Gameplay Framework usage conventions while retaining explicit Unity lifecycle and composition boundaries.

## Table of Contents

- [Overview](#overview)
- [Migration Notes](#migration-notes)
- [Architecture](#architecture)
- [Assembly Integration](#assembly-integration)
- [Quick Start](#quick-start)
- [Runtime Lifecycle](#runtime-lifecycle)
- [WorldSettings and IWorldDefinition](#worldsettings-and-iworlddefinition)
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

The module handles what UE calls the "game flow" layer—not input, physics, or networking transport. `WorldNetMode` (`Standalone`, `Client`, `ListenServer`, and `DedicatedServer`) controls framework authority behavior; actual network transport and replication live in separate modules composed into the World. Undefined enum values are rejected before WorldSettings resolution or World construction begins.

### Owner-thread Contract

`GameInstance` and each `World` are single-owner runtime scopes. The thread that creates the `GameInstance` becomes the owner; Unity compositions should create and use it on the Unity main thread. World mutation, live runtime reads, collection views, and inline callbacks must remain on that owner thread. These APIs fail immediately when called from another thread. Immutable construction metadata remains safe to inspect according to its individual contract. The framework provides neither an implicit lock nor a cross-thread queue.

Each `Actor` captures one immutable lifecycle owner thread in `Awake` or `BindToWorld`. A later lifecycle or World bind on another thread fails immediately. The protected `AssertActorOwnerThread` delegates to the current World while the Actor is registered and otherwise checks the captured lifecycle thread. Actor mutation entry points use this guard, and `PlayerController.GetCameraContext` checks it before creating the context, so a worker thread cannot establish ownership through first access. `OwnerChanged` is invoked synchronously on the same accepted thread. Product network adapters must explicitly marshal remote input to the Actor or World owner before mutation.

CameraManager applies the same guard to every public live-state or output getter and every public mutation or evaluation API. A retained manager reference used from a worker throws `InvalidOperationException` before any component, Transform, or other Unity-object lookup.

## Migration Notes

This refactor preserves assembly names and serialized `.meta` GUIDs, so existing Prefab, Scene, and asmdef references remain valid. The source-level breaking changes are:

| Before | After |
| --- | --- |
| `GameState.EMatchState` | `CycloneGames.GameplayFramework.Core.MatchState` |
| `GameState.ElapsedTime` (`float`) | `GameState.ElapsedTimeSeconds` (`double`) |
| `WorldActorAdmissionSnapshot` | `CycloneGames.GameplayFramework.Core.ActorAdmissionSnapshot` |
| `DamageEvent.EffectContext` (`object`) | Removed. Keep stable IDs or an immutable snapshot in your own adapter. |
| `event Action<float, DamageEvent, Controller, Actor> OnTakePointDamage` / `OnTakeRadialDamage` | `event DamageEventHandler` (the value is passed by `in`) |
| `GameState.SetMatchState` / `AddPlayerState` / `RemovePlayerState` virtual | Non-virtual. Override `OnMatchStateChanged` instead. |
| `Runtime/Scripts/Integrations/AssetManagement` / `GameplayAbilities` / `GameplayTags` | Separate companion packages (`CycloneGames.GameplayFramework.*`) with identical assembly names |

`DamageEvent` is now an immutable `readonly struct` created only through `MakeGenericDamage`, `MakePointDamage`, or `MakeRadialDamage`, and validated on ingress by `Validate()`. `PlayerLoginRequest.Validate()` offers an allocation-free enum result, `MatchStateMachine.PeekElapsedSeconds` offers a non-mutating read, and `UnityMatchClock.WithEpoch(Guid)` enables restoring a persisted clock epoch.

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
    W --> WD["IWorldDefinition<br/>read-only resolved prefab view"]
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
| `Runtime/Scripts/World` | GameplayWorldHost, terminal-cleanup ownership, GameplayWorldComposition, early/late Tick drivers, GameInstance, LocalPlayer, World, WorldSettings, IWorldDefinition, KillZVolume |
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

1. Create a dedicated root GameObject that outlives gameplay scenes and add `GameplayWorldTerminalCleanupOwner`.
2. Set its fixed capacity to the maximum number of GameInstances that may await terminal retry at once.
3. Create a separate scene root named `Gameplay World Host` and add `GameplayWorldHost`.
4. Assign the cleanup owner and WorldSettings asset to the Host. The two components must not share the same root hierarchy.
5. Select the net mode and local-player count.
6. Keep **Auto Start** enabled when the Host is the scene entry point.

Dedicated Server mode always uses zero local players. The Host starts before ordinary Actor `Start` callbacks, registers every new GameInstance with the application-lifetime cleanup owner before World startup, creates an early Update/FixedUpdate driver and a late LateUpdate driver, and exposes runtime status and failure diagnostics. On destruction it attempts terminal disposal and then relinquishes any incomplete GameInstance reference to the independent cleanup owner for later retry. Disabling the Host component pauses both drivers without changing the World lifecycle; keep the Host enabled until stop or disposal.

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

`LocalPlayer` contains a stable `Index` and the current world-scoped `PlayerController`. Every `PlayerController` read and internal assignment enforces the owning GameInstance thread. Controller logout, World stop, and GameInstance disposal clear this association.

One GameInstance accepts only one active World. Call and await `StopWorldAsync` before starting the next World. `StopWorldAsync` and public `World.ShutdownAsync` accept only an `EndPlayReason`; once shutdown begins it is deliberately non-cancellable. Calling public `World.ShutdownAsync` or `World.Dispose` directly uses the same terminal transaction. `CurrentWorld` is cleared only after every cleanup owner reports completion. A failed pass leaves the World in `Stopping`, rejects replacement startup, and can be retried on the same owner thread. Re-entering while a terminal pass is already executing fails fast; retry only after that pass returns or throws.

### Host Composition and DI

`GameplayWorldComposition` is the single Host dependency boundary for manual bootstrap and DI. It contains the required `IActorLifetime` and `IGameplayWorldTerminalCleanupOwner` plus optional reference resolution, scene transition, session, World runtime limits, Actor source, match clock, and camera-output lease arbiter. Configure the sealed Host before it starts:

~~~csharp
var sharedCameraOutputLeaseArbiter = new CameraOutputLeaseArbiter();
var composition = new GameplayWorldComposition(
    actorLifetime,
    terminalCleanupOwner,
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

The caller retains ownership of supplied services and keeps them valid until the Host and its registered GameInstances have completed terminal cleanup. A Host always requires an application-lifetime cleanup owner, supplied by its serialized root component, `ConfigureTerminalCleanupOwner`, or `GameplayWorldComposition`. With that owner and no explicit composition, it uses `UnityActorLifetime`, a `SceneWorldActorSource` fixed to the Host GameObject's scene, `UnityMatchClock.Scaled`, and a new `CameraOutputLeaseArbiter`. An explicit composition controls every seam exactly: a null `ActorSource` disables startup discovery. A directly constructed `GameInstance` also performs no scene scan when `actorSource` is null. A DI container supplies the same dependencies; it does not require a GameplayWorldHost subclass or a container-specific Runtime assembly.

`GameplayWorldTerminalCleanupRegistry` is the pure runtime implementation of `IGameplayWorldTerminalCleanupOwner`; `GameplayWorldTerminalCleanupOwner` is its Unity root component. Both are owner-thread-bound and use fixed, preallocated GameInstance slots. Host startup verifies capacity and registers the new GameInstance before invoking its asynchronous startup. Successful disposal calls `ReleaseCompleted`. `TryCleanupAll` retries every registered instance in one pass, keeps incomplete owners registered, and rethrows the first direct or nested OOM only after the pass. The application shutdown composition calls `TryCleanupAll` and verifies that it returns true before destroying the cleanup owner.

Host startup is a single transaction controlled by the `StartWorldAsync` cancellation token. A second start while the first is pending is rejected. `StopWorldAsync` during startup cancels the Host-owned pending transaction and waits for its rollback; shutdown itself has no cancellation token. Pre-cancelled starts, resolver faults, and destruction during an await release the temporary GameInstance; a non-disposed Host returns to `Stopped` and can start again after the failed transaction has completed.

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
    Stopping --> Stopping: retry incomplete terminal cleanup
    Stopping --> Stopped: Actors and gameplay state end
    Stopped --> Disposed: leases and lifecycle resources released
    Disposed --> [*]
~~~

A World accepts new Actors only while `Initializing` or `Playing`.

### Initialization Order

`StartWorldAsync` performs this transaction:

1. Validate GameInstance state and WorldSettings.
2. Resolve WorldSettings into the internal definition owner exposed through `IWorldDefinition`.
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

Any exception aborts initialization and starts the same staged terminal transaction used by shutdown. `CurrentWorld` and its cleanup owners remain reachable when that transaction is incomplete; only a fully disposed World is removed from the GameInstance.

### Shutdown and Travel

`GameInstance.StopWorldAsync(EndPlayReason)` and `World.ShutdownAsync(EndPlayReason)` expose no cancellation parameter. Once shutdown begins, cleanup no longer accepts cancellation:

1. World stops Actor Tick dispatch, enters `Stopping`, and commits cancellation of `LifetimeToken`.
2. GameMode removes each participant in stages: possession, GameSession registration, GameState PlayerState membership, World PlayerController membership, then associated Actor destruction. A failed stage keeps the exact participant owner reachable and prevents later participant stages from being repeated out of order.
3. Remaining Actor entries advance through CameraContext cleanup when applicable, bookkeeping detachment, one-shot World unbind, one-shot lifetime ownership transfer, and registry removal. Completed stages are recorded so retries neither repeat callbacks nor release the same Actor twice. Scene and external Actor GameObjects are not destroyed by the World.
4. The camera-output arbiter attempts every unique lease once in the pass. Failed leases remain owned; successful leases are removed.
5. Only after Actor and camera-output ownership has completed does the internal definition owner release registered external asset owners in reverse order.
6. Only after definition cleanup succeeds does World dispose its lifetime cancellation source.
7. World enters `Disposed`, GameInstance clears `CurrentWorld`, and the application cleanup owner releases the completed GameInstance registration.

If any owner remains, World stays in `Stopping`. A non-OOM incomplete pass throws the preallocated `WorldShutdownIncompleteException`; its retained World exposes gameplay, Actor, camera-output, WorldSettings-lease, and lifetime-token pending diagnostics. A direct or nested terminal OOM is preserved while required stages continue and is rethrown afterward. GameInstance retains `CurrentWorld`, and GameplayWorldHost retains the registered owner even when it reports `Faulted`; calling stop again on the same owner thread retries only incomplete stages. If the Host is destroyed, the independent `IGameplayWorldTerminalCleanupOwner` remains the retry owner.

`GameMode.TravelToLevel(levelName)` commits a non-cancellable travel operation: it first stops the World with `EndPlayReason.Travel`, then calls `ISceneTransitionHandler.ChangeScene` with `CancellationToken.None`. Decide whether to begin travel and capture any cross-scene data before invoking it; once invoked, shutdown and destination navigation run to completion. The destination scene creates its own World.

`GameInstance.Dispose` is also retryable. It cancels its lifetime once, retries any retained World or definition owner, clears LocalPlayer associations, and releases its cancellation source in stages. `IsDisposalComplete` becomes true only when no terminal owner remains.

## WorldSettings and IWorldDefinition

### Authoring and Runtime Responsibilities

`WorldSettings` is a ScriptableObject authoring asset. Each class entry selects a prefab whose root contains exactly one component of the required framework type. Runtime startup resolves these prefab classes into an internal lifetime owner. Product code reads the immutable public `IWorldDefinition` view through `World.Definition`; the view exposes prefab properties but no disposal operation or external lease handle.

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

Every external entry stores one explicit `AssetLocation` string. WorldSettings uses the selected source only to query `Supports`, then passes the location unchanged through `ResolveAsync<T>`, where `T` is the expected component type. WorldSettings stores no parallel identity metadata. The resolver owns location meaning, normalization, lookup, and lease policy.

Required references must resolve to non-null assets. An optional direct reference may be null. An optional external reference is considered configured whenever its location is non-empty and must then resolve successfully.

### Resolver Contract

~~~csharp
public interface IWorldSettingsReferenceResolver
{
    bool Supports(WorldSettingsReferenceSource source);

    UniTask<WorldSettingsAssetLoadResult<T>> ResolveAsync<T>(
        string location,
        IWorldSettingsLeaseRegistrar leaseRegistrar,
        CancellationToken cancellationToken)
        where T : UnityEngine.Object;
}
~~~

`WorldSettingsAssetLoadResult<T>` contains only `Success`, `Asset`, and `Error`; ownership never travels in the result. Before each external resolve call, WorldSettings reserves one registrar slot. The resolver may register at most one non-null `IDisposable` owner for that call, and a registered owner must never be disposed by the resolver. A resolver that needs several backend handles pre-creates one composite owner, registers that owner before any handle-bearing await, cancellation observation, validation, or callback can fail, and places every handle under it.

WorldSettings preallocates the rollback exception carriers and lease quarantine before invoking external code. A resolver may complete or fault on a worker thread; result validation and rollback first return to the owner/main thread without cancellation. Partial failure releases registered owners in reverse order. A failed cleanup owner remains quarantined. `WorldSettingsLeaseCleanupException` and `WorldSettingsLeaseCleanupOutOfMemoryException` expose diagnostic failures and `PendingLeaseCount`, but no quarantine handle. Before either exception leaves the startup transaction, GameInstance adopts the quarantine exactly once. A Host's application-lifetime terminal registry retains that GameInstance and retries its terminal disposal; direct GameInstance composition keeps the same responsibility with its application owner. On successful resolution, only the internal definition owner can release the registered owners.

Resolver implementations must:

- respond to cancellation without bypassing registered ownership cleanup;
- return bounded error messages;
- register its single owner before the first failure-capable asynchronous boundary and never dispose it after registration;
- avoid storing mutable resolution state in WorldSettings;
- treat a location as untrusted input when it can originate outside project assets.

### AssetManagement Companion

The sibling package `com.cyclone-games.gameplay-framework-asset-management` provides `AssetManagementWorldSettingsReferenceResolver`. It receives an explicit `IAssetPackage`, supports `AssetReference`, and registers the resolve call's load-handle owner through `IWorldSettingsLeaseRegistrar` before awaiting completion. It does not support `PathLocation`.

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

Each World binding publishes `BeginPlay` at most once. World owns the ordinary publication barrier; Unity `Start` only provides a fallback when the Actor is already bound and that World is already `Playing`. After shutdown, a non-owned scene or external Actor is unbound and can reset from `Ended` to `Initialized` when registered with a replacement World. A World-owned Actor cannot re-enter after ending. Each binding publishes `OnWorldUnbound` at most once, including direct destruction; when destruction occurs inside EndPlay, the terminal `Destroyed` state is not overwritten by `Ended`. `OnDestroyed` is the terminal Unity-destruction event and is separate from EndPlay. Its subscriptions use a copy-on-write array, so terminal dispatch consumes the already published snapshot without allocating. Non-OOM observer failures are logged and isolated. Direct or nested OOM is retained while base Actor cleanup and all published destruction observers finish, then the first OOM is rethrown.

An Actor subclass that overrides Unity `OnDestroy` must make base terminal cleanup unconditional: place local cleanup in `try` and invoke `base.OnDestroy()` from `finally`, or use an equivalent accumulator that still reaches the base call before propagating a failure. The built-in Controller, Pawn, PlayerState, GameMode, and CameraManager hierarchy follows this terminal rule. Skipping the base call can leave World registration, lifespan ownership, or observers unreleased.

Actor lifespan cancellation uses staged ownership. Once cancellation commits it is not repeated; the `CancellationTokenSource` remains owned until disposal succeeds, and re-entrant cleanup is rejected while that disposal is in progress. Terminal cleanup detects direct or nested OOM, finishes the required base boundary, and then propagates it without losing the retained source needed by a later retry.

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

`DamageEvent` is a readonly value created with `MakeGenericDamage`, `MakePointDamage`, or `MakeRadialDamage`. Point factories require finite hit geometry; radial factories require a finite origin and `0 <= innerRadius <= outerRadius`. `TakeDamage` accepts the value by `in`, calls its allocation-free `Validate` ingress check, and rejects default, unknown-type, or invalid geometry before dispatch. Point and radial observers use `DamageEventHandler` and also receive the same value by `in`. The value contains the event type, optional `IDamageType`, and event-specific geometry; external gameplay systems retain their own stable identifiers and immutable context snapshots.

Subscriptions to `OnTakePointDamage` and `OnTakeRadialDamage` publish copy-on-write observer snapshots. Adding or removing an observer is an owner-thread boundary after the Actor is bound and can allocate; damage dispatch traverses the published array without managed allocation.

After `InternalTakeDamage` returns a valid committed amount, point and radial dispatch calls the typed `ReceivePointDamage`/`ReceiveRadialDamage` receiver, every published observer, and finally `ReceiveAnyDamage`. Each receiver and each observer independently logs and isolates non-OOM exceptions. A typed-receiver failure does not skip observers or generic dispatch; an observer failure does not skip later observers or generic dispatch; a generic-receiver failure does not change the returned committed damage. A direct or nested OOM is rethrown immediately from the failing receiver or observer, so later damage callbacks and the normal committed-damage return do not run for that call.

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

`GetAuthGameMode` returns null in a non-authoritative World. Generic getters use safe casts and also return null when the active object does not match the requested type. `GameInstance.GetWorld()` returns the current World. Live GameInstance and World queries—including lifecycle state, current World, LocalPlayers, Actor and PlayerController registries, Tick counts, and indexed registration reads—require the owner thread. On a Client World, a replication adapter registers the received GameState Actor and commits it with `World.SetReplicatedGameState`; replacing a non-null committed instance is rejected, and destroying or explicitly clearing that Actor releases the World reference before another instance can be committed. The adapter exposes received Controllers through `GetFirstPlayerController`/`GetPlayerController` by calling `CommitReplicatedPlayerController` after the Controller and its PlayerState are registered and the Controller is initialized; an optional LocalPlayer must be the exact slot owned by that GameInstance. Destroying the Controller clears the committed World and LocalPlayer associations. These APIs are scoped to the object graph and do not create ambient global state.

`World.PlayerControllers`, `World.PlayerStarts`, and `GameState.PlayerArray` return concrete `OwnerThreadReadOnlyList<T>` live views; `GameInstance.LocalPlayers` uses the same contract. `Count`, the indexer, enumerator creation, `Current`, and `MoveNext` enforce the owner thread on every access. Retaining a view or enumerator and using it from a worker thread cannot bypass the check. `LocalPlayer.PlayerController` also performs the owner check on every read and internal assignment. A concrete-view `foreach` on the owner thread uses the struct enumerator and creates no managed allocation.

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

An exception from `PreLogin` or `IGameSession.ApproveLogin` is logged with its full exception inside the authoritative process. The external result is `Rejected` with the bounded message `Player login policy evaluation failed.` and never contains `Exception.Message`. After admission completes, an unexpected participant-staging or extension failure is logged internally and returns `SpawnFailed` with the bounded message `Player login failed while preparing participant state.` Product network adapters still map all result text to their own protocol-safe error catalog.

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

World unbind clears Controller possession, PlayerState, start spot, input-suppression counters, and initialization state. This also applies to non-owned scene Controllers and externally registered Controllers. AIController also stops AI and clears focus. PlayerController clears LocalPlayer, CameraManager, SpectatorPawn, and view-target relationships, then releases its CameraContext only when `CameraContext.Clear()` reports complete cleanup. If that call returns false or throws a non-fatal exception, the failure is logged and the same context reference is retained so explicit `Clear` or terminal `OnDestroy` can retry. Explicitly reinitialize these non-owned objects only after retained camera cleanup has completed.

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

`PawnConfig` is the Pawn's single serialized authoring source for controller-rotation flags, base eye height, and look-angle limits. When assigned, `Pawn.Awake` validates and applies it before gameplay callbacks. `SetPawnConfig` requires a non-null asset, validates it, stores it, and applies it immediately on the owner thread. Base eye height must be finite; both look angles must be finite and within `[0, 180]`. A Pawn with no assigned asset uses its built-in runtime defaults, while an assigned invalid asset fails initialization instead of publishing partially applied values.

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

The state machine owns legal transitions, committed `MatchState`, and elapsed in-progress time in one explicit clock domain. `GameplayWorldComposition` and `GameInstance` accept an `IMatchClock`. During Actor registration, World configures every GameState immediately after binding it and before registry commitment or BeginPlay publication; authoritative spawn and client replication therefore observe the same clock-ordering contract. Clock selection is owned by World composition and is not a public GameState mutation. `UnityMatchClock.Scaled` is the default, while `UnityMatchClock.Unscaled` is available when pause and `Time.timeScale` must not stop match time. A server or deterministic simulation can supply another `IMatchClock` without introducing Unity types into Core.

GameState runtime reads and mutations require registration with a World and run on that World's owner thread. This includes `MatchState`, `PlayerArray`, elapsed time, participant counts, transitions, and snapshot capture or restore. Elapsed-time access therefore cannot establish a clock or thread owner lazily; both are fixed before registry commitment.

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

`OnMatchStateChanged` observes a transition only after the state machine or restored snapshot has committed. A non-OOM callback failure is logged and isolated. A direct OOM propagates after the committed state remains visible; the re-entry guard is cleared in `finally`, so a later transition or restore can proceed.

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

CameraContext captures its construction thread as its owner. `Owner` and `MaxCameraModes` are immutable construction values. Every other live-state getter, view-target or mode mutation, `Clear`, and evaluation-scope transition checks that owner on each access, so retaining a context does not authorize worker-thread reads. The successful fast path is one managed-thread ID integer comparison with no lock or managed allocation; a mismatch fails immediately with `InvalidOperationException`.

`TryPushCameraMode` rejects null, duplicate instances, clearing state, and capacity overflow. `TryPushOrReplaceOldest` provides an explicit full-stack policy. During CameraManager evaluation, base-mode replacement and stack push, replace, or remove are rejected so the iterated stack remains stable. A `Clear` requested during evaluation is deferred until the evaluation scope ends; it then deactivates stacked modes in reverse order before deactivating the base mode.

Mode changes are transactional. An activation failure is compensated by deactivating the attempted mode and restoring the previous activation state when applicable. If a deactivation fails, or activation compensation cannot prove the attempted mode inactive or restore the previous mode safely, `HasModeLifecycleFault` becomes true. CameraContext retains exactly one context-owned cleanup reference to every affected mode instead of publishing an unowned lifecycle state. Callback exceptions are logged, and boolean stack operations return false when the transaction cannot commit.

An `OutOfMemoryException` from `CameraMode.OnActivate` or `OnDeactivate` first commits the same fault state—setting `HasModeLifecycleFault`, retaining one cleanup handle for each affected mode, including an uncommitted replacement during a full-stack replace, and freezing mutation and evaluation—then propagates. `Clear` can retry the retained cleanup and clears the fault only after every survivor deactivates successfully.

While `HasModeLifecycleFault` is true, base/stack mutation and CameraMode evaluation are frozen, and `GetPrimaryCameraMode` returns null. `Clear()` is the only lifecycle mutation that remains available: it retries every retained mode in reverse stack order followed by the base mode, removes only successfully deactivated entries, and keeps failed entries for another attempt. It returns true only when cleanup is complete and the fault has cleared; a deferred, reentrant, or incomplete cleanup returns false.

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

`CameraPose` is a readonly value containing position, normalized rotation, and FOV. Its constructor rejects non-finite positions, non-finite or degenerate rotations, and FOV values outside the open interval `(0, 180)`. `TryCreate` applies the same rules without throwing and returns an invalid default value on failure. `Lerp` requires two valid poses and a finite interpolation value.

CameraManager treats every external evaluation boundary as untrusted: `Actor.CalcCamera`, CameraMode Tick/Evaluate, post-processors, and custom blend curves are contained. An exception or invalid pose is logged, the last-known-good pose is retained, and invalid data is never written to the manager Transform or active output. Before the first valid pose, the manager uses a validated Transform pose and safe FOV, with zero position and identity rotation as the final fallback.

The base mode evaluates first. Stacked modes then evaluate from index 0 through the newest entry. The newest stacked mode is the primary mode used to select transition blend duration.

`CameraBlendState` supports Linear, SmoothStep, EaseOut, EaseIn, and custom evaluation. Built-in curves are selected through `CameraBlendCurveType` and evaluated without allocating curve objects. Extend the blend policy through `ICameraBlendCurve` or `CustomCameraBlendCurve`. Negative blend durations are clamped to zero.

### CameraManager and Output Ownership

During GameMode login, CameraManager is created only for a local PlayerController and only when the World's `IWorldDefinition.CameraManagerClass` is configured.

It:

- evaluates camera state through primary Actor Tick in the LateUpdate phase after initialization;
- resolves an authored `CameraOutputBehaviour` or accepts an explicit `ICameraOutput` through `SetCameraOutput`;
- discovers the backend's complete ownership-resource snapshot through `TryGetResourceSet`;
- asks the World to acquire one atomic lease for that exact snapshot;
- activates the output with the leased snapshot only after every resource has been acquired;
- publishes the final pose and FOV through `ApplyPose`;
- deactivates the output and releases ownership during replacement, Actor teardown, or World shutdown.

`ICameraOutput.TryGetResourceSet(out CameraOutputResourceSet resources, out string error)` is the side-effect-free discovery boundary. It must not change lifecycle state, capture or mutate backend state, or produce another externally visible effect. `CameraOutputResourceSet` is a readonly, allocation-free value snapshot containing between one and `CameraOutputLimits.MaximumResourceCount` (4) distinct live `UnityEngine.Object` resources and their captured instance IDs. Its constructors enforce the resource contract, while `TryCreate` reports invalid input without throwing. `Count`, `GetResource`, `GetResourceId`, and `TryValidate` provide bounded access and detect missing, destroyed, or identity-mismatched resources.

Resource discovery, atomic lease acquisition, activation with the leased snapshot, and commit form one output transaction. `ICameraOutput.TryActivate(CameraManager owner, in CameraOutputResourceSet resources, out string error)` receives the same snapshot already leased by the World; an implementation must not discover, replace, or substitute a resource during activation. The composed `ICameraOutputLeaseArbiter` rejects the complete request when any resource is already leased in its ownership domain and returns one generation-safe `CameraOutputLease` only after the all-or-nothing acquisition succeeds.

`CameraOutputLeaseArbiter` is a composition-owned, owner-thread-affine registry with no static global ownership state. `GameplayWorldComposition` creates and retains one when none is supplied; a directly constructed GameInstance creates its own default and passes the same instance to every World it creates. One GameInstance still accepts only one active World, so this domain remains continuous across replacement Worlds. Parallel Worlds belong to separate GameInstances: if their outputs can reference the same persistent Camera, CinemachineBrain, or other backend resource, inject the same arbiter instance into every participating composition. Independent default arbiters intentionally represent independent resource domains and cannot detect overlap between those Worlds.

Within one arbiter domain, two CameraManagers cannot hold overlapping resource snapshots even when their output components or Worlds differ. Release checks World identity, owner, output identity, lease generation, and complete resource IDs before removing ownership. The arbiter is created and mutated on one owner thread; all sharing GameInstances and Worlds must perform lease operations on that thread. Destroyed Unity resources are treated as unavailable, while managed reference identity remains the ownership token. World shutdown asks the arbiter to release all leases for that World. CameraManager teardown clears its pose, blend, dirty-state, and backend references before reuse. A CameraManager without an output continues evaluating and exposing `CurrentPose`; output is an optional presentation boundary.

If activation cleanup or `Deactivate` throws, CameraManager retains the lease and fails closed because the backend may still own or mutate the leased resources. `CameraOutputBehaviour` retains its owner and faulted lifecycle state and accepts a later `Deactivate` retry; it becomes idle only after that call succeeds. World `TryReleaseAll` processes each unique lease once per cleanup pass. Non-OOM and OOM deactivation failures retain only the affected lease and do not prevent attempts for the remaining unique leases in that pass. Successful deactivation removes the corresponding lease. After all unique leases have been visited, the arbiter rethrows the first direct or nested OOM; `World.CompleteShutdown` preserves it while its remaining required terminal cleanup runs, then propagates it.

A `CameraOutputBehaviour` subclass that overrides `Awake`, `OnEnable`, or `OnDestroy` must call the corresponding base method. The first two establish or validate lifecycle-thread ownership; base `OnDestroy` returns the output to its owning CameraManager or performs the final deactivation path. Its public live-state and mutation APIs enforce that captured lifecycle owner thread.

`CameraManager.HasOutputLeaseFault` becomes true when an arbiter exception or output-deactivation failure leaves ownership untrusted. While that flag is set, the current Runtime binding does not attempt another output bind. World unbind or manager Runtime reset clears the flag. Treat it as an operational fault: stop the affected World, inspect the failing output or custom arbiter, and do not assume the backend resource is free until its ownership domain has been reconciled.

### Core Unity Camera Output

`UnityCameraOutput` is included in the GameplayFramework Runtime assembly. Assign a `UnityEngine.Camera` explicitly or place one on the output hierarchy. The component can apply the final transform, field of view, or both. It requires no camera package and is the output used by the PureUnity sample. That sample's Camera and Light authoring uses built-in Unity and GameplayFramework components; it carries no optional Cinemachine or render-pipeline-specific component.

GameplayFramework Runtime and its public interfaces contain no Cinemachine type. Custom backends implement `ICameraOutput` directly or derive from `CameraOutputBehaviour` for Unity authoring. Discovery, activation, pose application, and deactivation run on the World owner thread. `UnityCameraOutput.TryGetResourceSet` discovers exactly one ownership resource: its explicitly assigned Camera or the Camera resolved from its hierarchy. Discovery does not set `ActiveCamera`; activation assigns that property from the already leased snapshot.

### Optional Cinemachine Output

When `com.unity.cinemachine` in the supported `[3.0.0,4.0.0)` range is installed, the gated assembly `CycloneGames.GameplayFramework.Runtime.Integrations.Cinemachine` provides `CinemachineCameraOutput`. Its asmdef uses `versionDefines`, `defineConstraints`, and `autoReferenced: false`; the GameplayFramework package does not declare Cinemachine as a dependency.

Assign a `CinemachineCamera` and `CinemachineBrain` with `SetVirtualCamera`/`SetBrain` or serialized fields. Scene discovery is disabled by default. The output still checks its own component hierarchy; when **Allow Scene Discovery** is enabled, the cold-path scan inspects only the output GameObject's Scene and succeeds only when that Scene contains exactly one eligible CinemachineCamera and exactly one CinemachineBrain. It never selects a candidate from another loaded Scene. `TryGetResourceSet` returns the Brain and CinemachineCamera as one immutable resource snapshot, so sharing either object conflicts atomically; discovery does not set `ActiveBrain` or `ActiveVirtualCamera`. Activation receives that leased snapshot, captures the brain update mode and camera Follow/LookAt targets, assigns the active properties, selects manual brain updates, and starts applying pose and lens. Deactivation attempts to restore Follow, LookAt, and `CinemachineBrain.UpdateMethod` independently, so one failure does not skip the other restore operations. Completed items are no longer pending; a faulted lifecycle retains the owner, active properties, and only the unfinished restore items for a later `Deactivate` retry. Lifecycle state is released only after all three restorations complete.

### View Target and Post-processors

`DefaultGameplayViewTargetPolicy` resolves manual override, suggested target, possessed Pawn, spectator Pawn, and PlayerController in that order.

CameraManager supports at most 16 registered `ICameraPostProcessor` instances. They run in registration order after every CameraMode. Owners should unregister processors when they end.

`PerlinNoiseShakePostProcessor` is a Runtime object with trauma, amplitude, frequency, decay, and exponent controls.

### Camera Actions

`CameraActionBinding` is a sealed authoring component. Compose it with gameplay components or the bridges below, and map string action keys to `CameraActionPreset`:

1. Check inline entries first.
2. Use `CameraActionMap` as the fallback.
3. For duplicate keys in the map, use the last entry.

Trigger policies are:

- `ReplaceSameKey`;
- `IgnoreIfRunning`;
- `Stack`.

The binding has configurable active-action and pooled-mode limits, both defaulting to 8 and hard-limited to 64. Inline entries, shared `CameraActionMap` entries, and Timeline signal mappings are each hard-limited to 256. `Awake` validates every budget before allocating lookup or pool storage. At the active limit or CameraContext capacity, `PlayAction`/`PlayPreset` returns false. When the pool has no available mode, the binding creates a `PresetCameraMode`; returned modes are retained only up to the configured pool limit.

Every committed `ActiveAction` captures the exact CameraContext that accepted its `PresetCameraMode`. Stop, automatic completion, disable, and destruction cleanup use that stored context instead of resolving the current PlayerController, so a destroyed Unity owner that compares as fake-null does not orphan the mode. A removal requested during CameraContext evaluation can be accepted and deferred; the binding keeps the ActiveAction and checks the captured context again in `LateUpdate`. The mode returns to the pool only after that context no longer contains it. If lifecycle cleanup retains the mode for `Clear`, CameraActionBinding does not reset or pool that instance.

Available bridges are:

- `AnimatorCameraActionBridge` for Animation Events;
- `CameraActionStateBehaviour` for Animator state enter, progress thresholds, and exit;
- `TimelineCameraActionReceiver` for Playables notifications;
- direct calls from gameplay code.

`CameraActionMap.Warmup()` validates and builds a complete immutable Runtime snapshot before publishing it. Runtime reads use `EntryCount`, `GetEntry`, and `TryGetEntry`; callers never receive the serialized backing list. `CameraActionBinding`, `AnimatorCameraActionBridge`, and `TimelineCameraActionReceiver` are sealed owner-thread components. Their live entry points reject use before lifecycle initialization or from a worker thread, and missing required bindings fail during initialization instead of dropping actions silently.

Each CameraActionStateBehaviour instance tracks at most 8 concurrent Animator/layer pairs. At capacity, enter and exit actions continue to run, but progress triggers for additional pairs pause until a slot is released. `OnStateExit` releases the slot.

Exit mode can perform no operation, stop an action key, or play an action key. Progress triggers when normalized time crosses the configured threshold and can run once per entire state lifetime or once per loop. Enter and progress triggers have independent transition gates.

### Camera Authoring Assets

| Asset/Runtime type | Purpose |
| --- | --- |
| `CameraProfile` | Shared default FOV and fallback blend duration; requires an explicit `ApplyTo` call |
| `CameraActionPreset` | Timed framing, offsets, lens, weight curve, and blend data for an action shot |
| `CameraActionMap` | Shared action-key table with an explicitly warmed, atomically published Runtime snapshot |
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

Registration stages both identity indexes before either becomes visible. If a custom composed session cannot roll back a post-commit registration failure, `NetworkGameSessionAdapter` retains exactly one recovery owner, reports `HasRegistrationRollbackFault`, and rejects later registrations. After correcting the session, call `TryRecoverRegistrationRollback()` on the owner thread before admitting another participant.

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
await world.GameMode.TravelToLevel("Stage02");
~~~

The call is the commit boundary for travel. Decide whether to proceed before invoking it; it stops the current World and calls the adapter with `CancellationToken.None`, so caller cancellation cannot interrupt shutdown or navigation after the operation begins. The destination scene's composition root starts its own World. Initialize the supplied `ISceneNavigator` according to the Navigathena lifecycle before gameplay travel occurs.

### Composing Navigathena Through the Host

When `GameplayWorldHost` owns the GameInstance, provide the Navigator through composition before the Host starts:

~~~csharp
var sceneTransitions =
    new NavigathenaSceneTransitionHandler(sceneNavigator);

host.Configure(new GameplayWorldComposition(
    new UnityActorLifetime(),
    terminalCleanupOwner,
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
| Camera Debug Window | Buffered camera telemetry using measured realtime sample intervals, graphs, and configurable alerts |
| World Debugger | Host, World, session, Actor admission/allocated-capacity/peak/rejection diagnostics, per-phase Tick counts, and 32-entry pages of indexed Actor registrations |
| Project Validation | Read-only scan of WorldSettings assets and loaded-scene Hosts, including same-Scene auto-start conflict detection and required external-resolver composition guidance |

Open the camera window through:

~~~text
Tools > CycloneGames > GameplayFramework > Camera Debug Window
~~~

World and authoring tool entries are:

~~~text
Tools > CycloneGames > GameplayFramework > World Debugger
Tools > CycloneGames > GameplayFramework > Project Validation
~~~

The camera window samples only in Play Mode. Sampling modes are Off, Basic, and Full. Sampling frequency is configurable from 5 to 120 Hz. The in-memory ring buffer is configurable from 120 to 2048 samples and defaults to 600. Full mode computes linear and angular speed from the measured `realtimeSinceStartupAsDouble` interval between successful samples, so Editor scheduling delay is not mistaken for the configured nominal interval. Alert thresholds cover FOV delta, remaining blend time, blend stall, and motion speed.

World Debugger reads at most 32 dense registration indices per visible page and does not materialize a World-wide Actor snapshot. Automatic Host binding is refreshed on hierarchy changes or by the explicit **Find Loaded Host** action, avoiding repeated scene-wide searches on the repaint path. Project Validation groups enabled auto-start Hosts by loaded Scene: multiple Hosts conflict only when they would auto-start in the same Scene-scoped Actor domain.

Editor diagnostics are observational only. Validate performance in a target Player and the Profiler before using diagnostic results as release evidence.

## Persistence and Data Ownership

The framework writes no Runtime save file or preference key.

| Data | Owner | Storage provided by the module | Version control | Lifecycle and cleanup |
| --- | --- | --- | --- | --- |
| WorldSettings | Project authoring | ScriptableObject asset with one direct prefab or one `AssetLocation` per class entry | Usually tracked | Edit and validate the serialized asset |
| Actor phase and startup Tick flag | Scene/prefab authoring | Serialized MonoBehaviour fields | Usually tracked | Edit through Actor Inspector; use Runtime APIs for temporary changes |
| GameModeConfig, PawnConfig, CameraProfile | Project authoring | ScriptableObject assets | Usually tracked | Edit and validate through their Inspectors; an assigned PawnConfig is applied by Pawn during Awake |
| CameraActionPreset, CameraActionMap | Project authoring | ScriptableObject assets | Usually tracked | Keep action keys consistent with consumer configuration |
| `IWorldDefinition` view and internal definition owner | World Runtime | Memory only | No | Public view is readonly; staged World shutdown releases the internal owner's registered resolver resources in reverse order |
| GameplayWorldTerminalCleanupRegistry | Application Runtime composition | Memory only | No | Retains incomplete GameInstances for owner-thread retry; release only after `TryCleanupAll()` returns true |
| GameplayWorldHost, GameInstance, LocalPlayer, World | Runtime composition | Memory only | No | Host startup registers before World startup; explicit Stop/Dispose or terminal-owner retry completes staged cleanup |
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
- GameInstance and World live-state reads, collection views, registry queries, and GameState runtime APIs assert that same owner before accessing mutable state. `World.Definition` returns the readonly `IWorldDefinition`, and every property read through a retained definition view repeats the same owner-thread check.
- `LocalPlayers`, `PlayerControllers`, `PlayerStarts`, and `PlayerArray` expose live `OwnerThreadReadOnlyList<T>` views whose count, indexer, enumerator creation, `Current`, and `MoveNext` repeat the owner check even after the view or enumerator has been retained. `LocalPlayer.PlayerController` guards every read and internal assignment.
- CameraContext captures its construction thread. Its live getters, view-target and mode mutations, `Clear`, and evaluation scope repeat a lock-free, allocation-free thread-ID check; only immutable `Owner` and `MaxCameraModes` omit that live-state guard.
- Each Actor captures a non-transferable lifecycle owner thread; while registered, mutation delegates to the World owner-thread check. Tick dispatch, phase changes, Runtime enable changes, and first CameraContext access follow this contract.
- CameraManager guards every public live-state/output getter and public mutation/evaluation API before any Unity-object lookup, including access through a retained reference.
- Network, file, and asset callbacks must marshal to the Unity main thread before mutating framework state.
- Unity object and camera-output operations run on the Unity main thread. Optional camera backends follow the same rule.
- `CameraOutputLeaseArbiter` captures its construction thread; every World sharing it acquires and releases leases on that owner thread.
- WorldSettings resolver I/O can complete on other threads. Each call registers at most one owner through `IWorldSettingsLeaseRegistrar` before its first failure point; multiple backend handles belong to one pre-created composite owner. Result validation, ownership transfer, and rollback marshal without cancellation to the owner thread. `WorldSettingsAssetLoadResult<T>` carries no lease, product code receives only the readonly `IWorldDefinition` view, and internal disposal remains a staged World responsibility.
- `ParticipantRoster` and `GameSession` capture their constructing thread as the single owner and add no locks.
- `MatchStateMachine` captures its constructing thread; a successful `TryRestore` creates a state machine owned by the restoring thread.
- Reads of their mutable state and every mutation assert that owner. Cross-thread access fails with `InvalidOperationException` instead of observing a potentially inconsistent roster, match clock, or Runtime binding.
- Immutable limit values and static validation/transition-policy functions do not read mutable instance state. Worker results still marshal to the owner before accessing a live roster, match state machine, or session.
- Async APIs use UniTask and propagate cancellation during startup and asset resolution. World initialization links caller, GameInstance, and World lifetime tokens; beginning shutdown cancels pending async login so startup cannot continue committing. Stop and shutdown then run to completion without accepting a cancellation token.

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
| `CameraOutputResourceSet` resources | 1 through `CameraOutputLimits.MaximumResourceCount` (4) per snapshot |
| CameraActionStateBehaviour tracking | At most 8 Animator/layer pairs |
| CameraActionBinding active/pool counts | Configurable; default 8 each, hard maximum 64 each |
| CameraActionMap / inline action / Timeline mapping entries | Hard maximum 256 each |
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
- point/radial damage-observer and `OnDestroyed` subscription changes, which publish copy-on-write arrays; warmed damage dispatch and terminal destruction reuse their published arrays without creating a dispatch snapshot;
- Tick registry and reusable-snapshot capacity growth during Actor registration;
- CameraContext construction;
- explicit CameraActionMap warmup and atomic lookup-snapshot construction;
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

- World modes and invalid-value rejection, startup rollback, client GameState/PlayerController commit boundaries, scoped Actor-source discovery, parallel Scene isolation, non-owned Actor unregistration/reuse, trusted local-login validation, bounded admission and participant-staging failure results, staged participant/GameMode terminal cleanup, logout, retained live-view/enumerator owner-thread enforcement, `Stopping` retry, `WorldShutdownIncompleteException`, and final CurrentWorld cleanup;
- terminal Actor-lifetime pairing and staged Actor teardown across successful destruction, spawn failure, self-destruction, shutdown, throwing release, retained cleanup owners, and direct or nested OOM;
- WorldSettings validation, single-location external references, one-owner `IWorldSettingsLeaseRegistrar`, composite-handle ownership, result-without-lease, worker completion marshalled to the owner thread, startup cancellation, quarantine, and retryable lease disposal;
- GameplayWorldHost composition, independent fixed-capacity terminal-cleanup ownership, registration before startup, pre-cancelled and pending-start cancellation, destruction during await, application-lifetime transfer, fault retry, reentrant-start rejection, immutable WorldRuntimeLimits, Actor admission/allocated-capacity/peak/rejection diagnostics, indexed World inspection, custom Inspectors, and project validation;
- Actor tags, readonly damage-event validation and `in` dispatch, copy-on-write damage observers, typed/generic receiver and per-observer non-OOM fault isolation through committed damage return, immediate direct/nested OOM propagation, allocation-free published `OnDestroyed` dispatch, unconditional base cleanup, staged lifespan-source ownership, possession, Pawn input, PawnConfig application/validation, primary Tick phases, Runtime gates, mutation safety, re-entry rejection, exception isolation, immutable lifecycle-owner capture, and bound/unbound owner-thread enforcement;
- PlayerState snapshots, session identity locks, atomic spectator changes, and post-commit Pawn notification;
- GameState transitions, clock configuration before registration-time BeginPlay, custom match-clock composition, committed-state observer isolation and OOM guard reset, match snapshots, and World-scoped PlayerStart;
- CameraContext capacity, retained-context owner-thread enforcement, transactional mode changes, lifecycle-fault retention before OOM propagation, frozen evaluation, boolean `Clear` recovery, PlayerController terminal retry ownership, CameraActionBinding committed-context and deferred-removal cleanup after Unity owner fake-null, pool ownership, evaluation mutation guards, deferred clear, teardown order, view-target policy, and action limits;
- readonly CameraPose validation, last-known-good containment, allocation-free built-in camera curves, side-effect-free output discovery, immutable resource-snapshot validation, activation with the leased snapshot, core UnityCameraOutput, atomic multi-resource ownership, shared-arbiter exclusion across parallel Worlds, all-unique-lease terminal attempts before OOM propagation, retained leases after failed deactivation, independent Cinemachine-state restoration, lease-fault reporting, destroyed-resource handling, reset, replacement, teardown, and failure isolation;
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
2. Open the PureUnity sample scene and confirm its Camera and Light GameObjects contain no optional package components.
3. Confirm GameplayWorldHost references UnitySampleWorldSettings and an independent-root GameplayWorldTerminalCleanupOwner with available capacity.
4. While still in Edit Mode, add `UnitySampleRotatingActor` to a scene GameObject and save the scene.
5. Enter Play Mode.
6. Verify the World is Playing and the local Controller owns a PlayerState and Pawn.
7. Verify the local CameraManager owns its configured `UnityCameraOutput`, applies pose/FOV to the target Camera, and releases the output on stop.
8. Confirm that the sample Actor rotates only while the World is Playing.
9. Open World Debugger and inspect the World, configured Actor admission limit, allocated capacity, peak/rejection counters, per-phase Tick counts, and paged Actor registrations.
10. Click `Disable Runtime Tick` in the sample Actor Inspector. Confirm rotation stops, the Tick Enabled diagnostic changes, and the Actor remains registered; then click `Enable Runtime Tick` to resume.
11. Run Project Validation and confirm the sample reports no configuration errors.
12. Open Camera Debug Window and observe pose/blend data.
13. Exit Play Mode and confirm no participant, Tick, camera-mode, or terminal-cleanup registration state remains.

When Cinemachine is part of the product, repeat the camera check with `CinemachineCameraOutput`: assign the Camera and Brain explicitly, or enable **Allow Scene Discovery** in a Scene containing exactly one of each. Confirm manual update is active only while owned and that brain update mode plus Follow/LookAt targets are restored after stop.

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
| WorldNetMode is rejected | Pass one of `Standalone`, `Client`, `ListenServer`, or `DedicatedServer`; undefined enum values are invalid input |
| Host reports a missing, shared-root, or full terminal-cleanup owner | Assign a `GameplayWorldTerminalCleanupOwner` on an independent persistent root and set enough capacity before startup; do not destroy it until `TryCleanupAll()` returns true |
| Stop/Dispose throws `WorldShutdownIncompleteException` | Keep the World, GameInstance, and terminal-cleanup owner alive; inspect pending-stage properties, repair the failing callback or lifetime adapter, and retry on the same owner thread |
| Owner-thread exception | Marshal to the captured Actor lifecycle thread or the composition owner thread before Actor mutation, first `GetCameraContext` access, retained CameraManager use, `World.Definition` or retained `IWorldDefinition` property access, live GameInstance/World/GameState/CameraContext reads, retained-view use, login, spawn, or possession; ownership cannot move to the calling worker |
| WorldSettings validation fails | Configure GameMode, PlayerController, Pawn, and PlayerState |
| External reference has no resolver | Pass a resolver to GameInstance and confirm `Supports` returns true for the selected source |
| A WorldSettings resolver is rejected for ownership registration | Register exactly one owner before the first await or other failure point. Put multiple backend handles in one pre-created composite `IDisposable`, and never return or dispose the registered owner from the resolver |
| External load fails after cancellation | Propagate cancellation from the resolver and let WorldSettings marshal to the owner thread and roll back its registered owner. GameInstance adopts any failed quarantine once; retry its disposal directly or through the registered terminal-cleanup owner |
| Client World has no GameMode | Client mode is non-authoritative; populate client-visible state through a network adapter |
| Dedicated server has no local Controller | Use remote `LoginAsync`; automatic local login is disabled |
| Login returns InvalidRequest | Check ID and name/address/options bounds, `IsLocal`, and the exact LocalPlayer slot from GameInstance |
| Login returns Rejected | Check PlayerId uniqueness, product admission policy, and the authoritative log for a `PreLogin` or `ApproveLogin` exception; external exception details are intentionally bounded |
| `NetworkGameSessionAdapter.HasRegistrationRollbackFault` is true | Correct the custom session rollback path, then call `TryRecoverRegistrationRollback()` on the owner thread before accepting another registration |
| Login returns AtCapacity | Check GameSession player/spectator capacity and counts |
| Login returns SpawnFailed | Check prefab references, Actor-lifetime results, World state, and custom initialization callbacks |
| Player spawn point is unstable | Override `ChoosePlayerStart` or pass an exact portal name |
| Possession fails | Register and initialize the Controller, use the same World, and avoid reentrant callbacks |
| Movement input has no effect | The movement adapter must consume and apply the pending vector |
| Pawn initialization reports invalid configuration | Assign a PawnConfig with finite base eye height and finite look angles in `[0, 180]`; `SetPawnConfig` does not accept null |
| TakeDamage rejects the event | Construct DamageEvent with its typed factory and pass a value whose event-specific geometry validates |
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
| Camera evaluation reports an invalid pose | Inspect custom CalcCamera, CameraMode, post-processor, or blend-curve code; CameraManager keeps the last-known-good pose instead of publishing invalid data |
| Camera output ownership error | Ensure the discovered snapshot contains live, distinct resources and is leased by only one CameraManager; activation must use that same snapshot, and Cinemachine leases both its Brain and CinemachineCamera |
| `HasOutputLeaseFault` is true | Stop the affected World and inspect the output deactivation path and custom arbiter; the lease remains held and binding stays fail-closed until successful cleanup or World unbind/reset |
| Parallel Worlds both acquire one persistent camera resource | Construct one `CameraOutputLeaseArbiter` on their common owner thread and inject that same instance into every participating GameInstance or Host composition |
| Camera lease arbiter reports an owner-thread error | Create and mutate the shared arbiter on the same owner thread used by every participating World |
| Cinemachine integration does not compile | Confirm `com.unity.cinemachine` in `[3.0.0,4.0.0)` is resolved and reference the gated integration assembly from a matching integration asmdef |
| Cinemachine output cannot resolve resources | Assign Camera and Brain explicitly, or opt into Scene discovery and keep exactly one CinemachineCamera and one CinemachineBrain in the output Scene |
| Host dependency must come from DI | Build `GameplayWorldComposition`, call `Configure` before startup, and do not subclass the sealed Host |
| Actor admission returns false | Inspect configured `WorldRuntimeLimits.MaximumActorCount`, allocated capacity, peak count, and rejected admission count; release or defer work according to product policy |
| Camera-mode push returns false | Check duplicate instance, clearing/evaluation state, CameraContext capacity, and `HasModeLifecycleFault` |
| `HasModeLifecycleFault` is true | Inspect the logged CameraMode callback failure and call `Clear` to retry retained cleanup; mutation and evaluation remain frozen until all retained modes deactivate successfully |
| PlayerController retains its CameraContext after unbind | `Clear()` returned false or threw; inspect the lifecycle log and retry the same context explicitly, while terminal `OnDestroy` also performs another cleanup attempt |
| Camera clear does not execute immediately | A clear requested during evaluation runs after the evaluation scope ends; a faulted mode that fails cleanup remains retained for the next `Clear` attempt |
| Camera action returns false | Check action key, preset, active-action limit, Controller resolution, and mode-stack capacity |
| Animator progress action does not trigger | Check progress key, threshold, transition flag, loop policy, and the 8-pair tracking capacity |
| Snapshot restore fails | Validate non-negative ID, player-name length, and registered identity/spectator locks; validate persistence or protocol envelopes before calling Runtime |
| Core type is not visible to product code | Add `CycloneGames.GameplayFramework.Core` directly to the consumer asmdef |
| Pure rules assembly unexpectedly depends on UnityEngine | Remove GameplayFramework Runtime and Unity adapter references; reference Core directly |
| Networking protocol compiles but Actor adapters are missing | Add `CycloneGames.GameplayFramework.Networking.Runtime` to the Unity replication asmdef |
| Travel reports no handler | Compose `ISceneTransitionHandler` into GameInstance |
| Sample script is missing from a Player build | Confirm the sample asmdef includes the target platform and resolve every compilation error before building |
