# CycloneGames.GameplayFramework.Factory

[Simplified Chinese](README.SCH.md)

## Overview

This package connects the GameplayFramework `IActorLifetime` seam to the symmetric Unity object lifetime contract in CycloneGames.Factory. `FactoryActorLifetime` delegates Actor creation and permanent release to one `IUnityObjectLifetime` supplied by the composition root.

GameplayFramework remains independent of Factory. Install and reference this package only when a product already composes Unity object lifetime through CycloneGames.Factory.

## Assemblies and Dependencies

| Assembly | Purpose | Consumer reference |
| --- | --- | --- |
| `CycloneGames.GameplayFramework.Runtime.Integrations.Factory` | Adapts `IUnityObjectLifetime` to `IActorLifetime`. | Explicit |
| `CycloneGames.GameplayFramework.Integrations.Factory.Tests.Editor` | Verifies create/release identity and already-destroyed Actor handling. | Test Runner only |

The package declares direct UPM dependencies on GameplayFramework and Factory. The Runtime assembly is `autoReferenced: false`; a project assembly that constructs `FactoryActorLifetime` must reference it explicitly.

## Installation

### UPM

Install `com.cyclone-games.gameplay-framework-factory`. Package Manager resolves `com.cyclone-games.gameplay-framework` and `com.cyclone-games.factory`. No scripting define symbol is required.

### Embedded under Assets

Place this package, `CycloneGames.GameplayFramework`, and `CycloneGames.Factory` under the project's Assets tree. Direct asmdef references compile the integration when all three package roots are present. Remove this integration package root when Factory is not part of an Assets-based project.

## Composition

Create one Factory lifetime and pass the adapter through the same explicit composition path used without a DI container:

```csharp
using CycloneGames.Factory.Runtime;
using CycloneGames.GameplayFramework.Runtime;
using CycloneGames.GameplayFramework.Runtime.Integrations.Factory;

IUnityObjectLifetime unityLifetime = new DefaultUnityObjectSpawner();
IActorLifetime actorLifetime = new FactoryActorLifetime(unityLifetime);

var composition = new GameplayWorldComposition(actorLifetime);
gameplayWorldHost.Configure(composition);
```

A DI container registers the same concrete objects and supplies the same constructors. Neither assembly resolves a container or uses a service locator.

## Ownership and Lifecycle

- `GameInstance` passes the configured `IActorLifetime` to each `World`.
- `World` becomes the sole owner of every Actor returned from `Create`.
- Spawn rollback, explicit Actor destruction, and World shutdown terminate owned instances through `Release`.
- `Release` remains terminal even when an Actor destroys itself during `EndPlay`; the adapter still forwards that Actor reference so the Factory lifetime can complete its accounting.
- Scene and externally registered Actors do not transfer ownership to the injected lifetime. An explicit `DestroyActor` request terminates them through GameplayFramework's core Unity destruction path.

## Pooling Boundary

`FactoryActorLifetime` does not use `IMemoryPool`, `FastObjectPool`, `MonoFastPool`, `Despawn`, or `Return`. GameplayFramework Actors enter terminal lifecycle state before release and are never offered for reuse. Actor pooling requires a separate reset, lease invalidation, stale-reference, double-return, and component-state contract and is not provided by this package.

## Performance and Threading

Creation and release are lifecycle cold paths on the Unity main thread. The adapter adds one direct interface call and owns no collection, cache, static state, thread, task, or subscription. It performs no reflection or runtime type discovery.

Unity object allocation and destruction costs remain those of the supplied `IUnityObjectLifetime`. Measure spawn bursts and destruction queues in the target Player when they are part of a product performance budget.

## Persistence

This integration writes no files, saves no preferences, and introduces no serialized fields or assets. It has no schema or migration state.

## Validation

After both required packages compile, run:

```text
CycloneGames.GameplayFramework.Integrations.Factory.Tests.Editor
```

The EditMode tests verify that the same Actor reference crosses the create/release boundary, release is terminal, and an Actor already destroyed by Unity is still reported to the supplied lifetime. Run the GameplayFramework core EditMode and PlayMode suites as the ownership regression coverage.

Player, IL2CPP, stripping, and device validation remain product responsibilities for each target configuration.
