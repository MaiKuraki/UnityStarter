# CycloneGames.GameplayFramework.AssetManagement

[Simplified Chinese](README.SCH.md)

## Overview

This package connects GameplayFramework `WorldSettings` asset locations to an explicitly owned CycloneGames AssetManagement package. It provides one resolver and does not initialize, select, or shut down an asset backend.

Use the package when a `WorldSettings` entry uses `AssetReference`. Direct prefab references do not require this integration. `PathLocation` requires a resolver supplied by the application for its own addressing rules.

## Assemblies and Dependencies

| Assembly | Purpose | Consumer reference |
| --- | --- | --- |
| `CycloneGames.GameplayFramework.Runtime.Integrations.AssetManagement` | Implements `IWorldSettingsReferenceResolver` with `IAssetPackage`. | Explicit |
| `CycloneGames.GameplayFramework.Integrations.AssetManagement.Tests.Editor` | Verifies prefab component resolution, failure handling, and immediate lease registration. | Test Runner only |

The package declares direct UPM dependencies on GameplayFramework, AssetManagement, and UniTask. The Runtime assembly is `autoReferenced: false`; a project assembly that names the resolver must reference it explicitly.

## Installation

### UPM

Install `com.cyclone-games.gameplay-framework-asset-management`. Package Manager resolves its declared host and AssetManagement dependencies. No scripting define symbol is required.

### Embedded under Assets

Place this package, `CycloneGames.GameplayFramework`, and `CycloneGames.AssetManagement` under the project's Assets tree. The integration asmdef uses direct assembly references, so it participates in compilation whenever those package roots are present. No PlayerSettings symbol or generated capability file is used.

## Composition

Initialize and own the AssetManagement package at the application composition root, then construct the resolver from that package:

~~~csharp
IAssetPackage assetPackage = applicationAssets;
IWorldSettingsReferenceResolver resolver =
    new AssetManagementWorldSettingsReferenceResolver(assetPackage);

// Pass resolver to the application-owned GameInstance composition.
~~~

The resolver supports `WorldSettingsReferenceSource.AssetReference`. For a component type, it loads a prefab `GameObject` and requires exactly one matching component on the prefab root. For other Unity object types, it loads the requested asset directly.

## Ownership and Failure Behavior

- The application owns `IAssetPackage`; the resolver never disposes it.
- Every non-null `IAssetHandle` is registered with the core-owned `IWorldSettingsLeaseRegistrar` immediately after creation and before its task, cancellation, or asset state is observed.
- One resolver call registers at most one non-null ownership handle. Once the backend returns a non-null handle, the resolver registers its owner before the first subsequent failure point. A backend that creates multiple child handles must pre-create and register one composite `IDisposable`, then create every child under that owner.
- Registration transfers exclusive disposal responsibility to GameplayFramework. The resolver never disposes a registered handle and the load result contains no lease field.
- Resolution rollback and World shutdown dispose registered leases through the retryable GameplayFramework lifetime owner. A failed disposal remains owned and can be retried; it is never silently discarded.
- Cancellation is propagated as `OperationCanceledException`.
- `OutOfMemoryException`, including one nested in an `AggregateException`, is propagated after the handle ownership transfer rather than converted into a normal load failure.
- Invalid locations and incompatible prefab contents return a failed result with an error message.

## Performance and Threading

Resolution is a World startup and travel operation, not a per-frame API. Prefab component resolution performs a root component query and may allocate. Cache the resolved runtime definition rather than resolving references from gameplay hot paths.

Asset completion may occur away from the Unity main thread. The resolver switches to the main thread before reading Unity objects. Handle registration, World creation, runtime access, rollback, and shutdown remain bound to the GameplayFramework owner thread.

## Persistence

This integration writes no files and stores no preferences. Asset cache, catalog, storage, and recovery behavior belong to the selected AssetManagement backend and its application-level owner.

## Validation

Run the following EditMode assembly after both required packages compile:

~~~text
CycloneGames.GameplayFramework.Integrations.AssetManagement.Tests.Editor
~~~

For a Player target, verify one direct-reference World and one asset-reference World, cancellation and out-of-memory propagation after handle creation, retryable World shutdown, and backend handle counts after shutdown.
