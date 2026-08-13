# CycloneGames.GameplayFramework.AssetManagement

[Simplified Chinese](README.SCH.md)

## Overview

This package connects GameplayFramework `WorldSettings` asset locations to an explicitly owned CycloneGames AssetManagement package. It provides one resolver and does not initialize, select, or shut down an asset backend.

Use the package when a `WorldSettings` entry uses `AssetReference`. Direct prefab references do not require this integration. `PathLocation` requires a resolver supplied by the application for its own addressing rules.

## Assemblies and Dependencies

| Assembly | Purpose | Consumer reference |
| --- | --- | --- |
| `CycloneGames.GameplayFramework.Runtime.Integrations.AssetManagement` | Implements `IWorldSettingsReferenceResolver` with `IAssetPackage`. | Explicit |
| `CycloneGames.GameplayFramework.Integrations.AssetManagement.Tests.Editor` | Verifies prefab component resolution, asset resolution, failure handling, and lease transfer. | Test Runner only |

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
- A successful result transfers its `IAssetHandle` as a lease to the resolved `WorldDefinition`.
- World shutdown disposes transferred leases through the GameplayFramework lifetime owner.
- Failed and cancelled resolution releases any acquired handle.
- Cancellation is propagated as `OperationCanceledException`.
- Invalid locations and incompatible prefab contents return a failed result with an error message.

## Performance and Threading

Resolution is a World startup and travel operation, not a per-frame API. Prefab component resolution performs a root component query and may allocate. Cache the resolved runtime definition rather than resolving references from gameplay hot paths.

Asset completion may occur away from the Unity main thread. The resolver switches to the main thread before reading Unity objects or disposing Unity-backed handles. The owning GameplayFramework composition must also create, use, and dispose the resulting World on its owner thread.

## Persistence

This integration writes no files and stores no preferences. Asset cache, catalog, storage, and recovery behavior belong to the selected AssetManagement backend and its application-level owner.

## Validation

Run the following EditMode assembly after both required packages compile:

~~~text
CycloneGames.GameplayFramework.Integrations.AssetManagement.Tests.Editor
~~~

For a Player target, verify one direct-reference World and one asset-reference World, cancellation during loading, World shutdown, and backend handle counts after shutdown.
