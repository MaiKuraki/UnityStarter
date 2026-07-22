# Providers and integrations

[English | 简体中文](Providers.SCH.md)

AssetManagement ships three providers (Resources, Addressables, YooAsset) and two integrations (Navigathena, VContainer). Provider-neutral consumers depend on `IAssetPackage`; only composition and release-operation assemblies reference a provider assembly.

## Table of Contents

- [Overview](#overview)
- [Core Concepts](#core-concepts)
- [Usage Guide](#usage-guide)
- [Advanced Topics](#advanced-topics)
- [Troubleshooting](#troubleshooting)

## Overview

Each provider is exposed as an `IAssetModule` with a distinct capability surface. Addressables and YooAsset cannot be active through these adapters simultaneously — the framework AssetBundle runtime guard establishes one provider authority until coexistence, shutdown ordering, and memory behavior are qualified as a complete product configuration.

### Key Features

- **Resources**: always available, bootstrap-friendly, loading-only.
- **Addressables**: catalog maintenance, Tags/Locations downloaders, scene loading, bulk loading.
- **YooAsset**: manifest activation, All/Tags/Locations downloaders, raw-file access, scene loading, bulk loading.
- **Navigathena**: explicit scene navigation built on `IAssetSceneLoader`.
- **VContainer**: DI installer for module and retention scheduler.

### Choose a provider

| Requirement | Resources | Addressables | YooAsset |
| --- | ---: | ---: | ---: |
| Async asset load and prefab instance | yes | yes | yes |
| Synchronous asset load | yes | no | yes |
| Bulk/sub-asset load | no | yes | yes |
| Raw-file access | no | no | yes |
| Async scene load | no | yes | yes |
| Remote downloader | no | Tags, Locations | All, Tags, Locations |
| Provider maintenance | no | latest catalog, unused-bundle cleanup, global Unity cache | product-version manifest activation, package cache |
| Reliable built-in storage probe | no | desktop/Editor cache volume only | specific desktop/Editor Host configuration |
| Provider runtime ownership | one owner per module | one process owner, one logical package | one process owner, multiple packages |

For example, UIFramework's dynamic-atlas sample requires `IAssetSyncOperations`, DataTable raw loading requires `IAssetRawFileLoader`, and Navigathena requires `IAssetSceneLoader`.

### Optional assembly activation

An optional assembly becomes eligible for compilation when its dependency is installed in `UnityStarter/Packages/manifest.json` and resolved in `packages-lock.json`, its version satisfies the asmdef `versionDefines` range, and every `defineConstraints` entry is satisfied. These assemblies use `autoReferenced: false`, so a consumer asmdef must reference the selected provider or integration assembly; that reference does not control whether the optional assembly itself is compiled.

| Assembly | Supported version condition |
| --- | --- |
| `CycloneGames.AssetManagement.Runtime.Providers.Addressables` | `com.unity.addressables` `[2.11.1,2.11.2)` |
| `CycloneGames.AssetManagement.Runtime.Providers.YooAsset` | `com.tuyoogame.yooasset` `[3.0.5,4.0.0)` |
| `CycloneGames.AssetManagement.Runtime.Integrations.Navigathena` | `com.mackysoft.navigathena` `[1.1.0,1.1.1)` |
| `CycloneGames.AssetManagement.Runtime.Integrations.VContainer` | installed `jp.hadashikick.vcontainer` |

Never add generated `CYCLONEGAMES_HAS_*` symbols manually in Player Settings.

## Core Concepts

### Runtime-location contract

`AssetRef`, `SceneRef`, and load methods use an exact provider runtime key. An Editor asset path is not a provider-neutral address.

| Provider | Example asset in project | Runtime location example |
| --- | --- | --- |
| Resources | `Assets/Game/Resources/UI/Inventory.png` | `UI/Inventory` |
| Addressables | any addressable entry | the configured Addressables address |
| YooAsset | collected asset | the configured YooAsset address |

The property drawer stores an Editor GUID to display the authoring object and exposes `Runtime Location` as a separate field. Selecting another object clears the previous runtime location. The validator reports missing GUID targets and empty locations but never rewrites a custom provider key.

### Scene provider contract

Addressables and YooAsset expose only the asynchronous `IAssetSceneLoader` capability. The advanced overload carries Unity `LoadSceneParameters` and supports `LocalPhysicsMode.None`, `Physics2D`, `Physics3D`, and the valid `Physics2D | Physics3D` combination. The loaded scene owns those local physics worlds and destroys them during unload.

Each package owns an active-scene registry and a private origin token. Active unload validates the exact handle and registry entry. After a successful unload, `LoadSceneMode.Single` replacement, or external `SceneManager` unload, a repeated unload through the same package generation is a terminal no-op; another package or a recreated generation is rejected.

Activation and unload are main-thread-affine single-flight transitions. Cancellation is accepted only before the first provider mutation. Once unload starts, later calls join it even with a cancelled token. A failed unload leaves the handle registered, removes the diagnostic `Unloading` state, records the lifecycle error, and permits a new attempt.

Scenes are not asset-cache entries: SLRU admission, cache trim, bucket clear, and low-memory cache maintenance do not unload them. `ISceneHandle.Dispose` is idempotent caller-wrapper release only. The originating package remains the unload authority until terminal unload or package shutdown.

## Usage Guide

### Resources ownership composition

Resources is always available. Keep the module in an application owner; returning only the package loses shutdown authority.

```csharp
public sealed class ResourcesAssetOwner
{
    private IAssetModule _module;

    public IAssetPackage Package { get; private set; }

    public async UniTask InitializeAsync(CancellationToken cancellationToken)
    {
        _module = new ResourcesModule();
        try
        {
            Package = await AssetManager.InitializeDefaultPackageAsync(
                _module, "BuiltIn",
                new AssetManagementOptions(), new AssetPackageInitOptions(),
                cancellationToken);
        }
        catch
        {
            await _module.DestroyAsync();
            throw;
        }
    }

    public async UniTask ShutdownAsync()
    {
        if (_module == null) return;
        await _module.DestroyAsync();
        Package = null;
        _module = null;
    }
}
```

Resources exposes async and sync single-asset loading plus runtime diagnostics. It does not expose bulk, raw-file, scene, catalog, downloader, storage-preflight, or provider-maintenance capabilities. Releasing a handle releases AssetManagement ownership, but Unity may retain native Resources data. `UnloadUnusedAssetsAsync` clears idle wrappers and calls Unity's global unused-resource pass; schedule it at a measured phase boundary.

### Addressables composition

The Addressables provider targets `com.unity.addressables` `[2.11.1,2.11.2)`. Install a compatible package and reference the provider assembly from the composition assembly.

```csharp
IAssetModule module = new AddressablesModule();

IAssetPackage package = await AssetManager.InitializeDefaultPackageAsync(
    module, "Default",
    new AssetManagementOptions(),
    new AssetPackageInitOptions(providerOptions: null),
    cancellationToken);
```

The adapter enforces one AssetManagement Addressables owner and one logical package. Module initialization and package initialization are separate gates; `ProviderOptions` must be null. Addressables Groups, Profiles, remote paths, and Player content must already be built by the product pipeline.

```csharp
if (package is not IAddressablesCatalogMaintenance maintenance)
{
    throw new System.NotSupportedException("Addressables maintenance is unavailable.");
}

bool updated = await maintenance.UpdateLatestCatalogsAsync(cancellationToken);
bool cleaned = await maintenance.CleanUnusedBundleCacheAsync(cancellationToken);
```

Catalog and cache mutations fail fast when another mutation is active on the same package. Once catalog check starts, caller cancellation is observed only after the provider reaches a safe terminal boundary and before activation begins. A catalog attempt advances wrapper cache generation even after provider failure. Idle entries are disposed; active handles remain valid and counted Active but become generation-detached from keyed SLRU lookup until final release or shutdown.

`CleanUnusedBundleCacheAsync` is the normal maintenance operation. `ClearAllCacheFilesAsync` calls Unity's process-wide `Caching.ClearCache`; it is destructive, not scoped to the logical package. `ReadReleaseMetadataVersionAsync` reads bounded product metadata for correlation; it is not authenticated authorization or anti-rollback evidence.

Addressables Tags/Locations downloaders use provider-global scheduling; per-downloader concurrency and retry are not exposed. A key array accepts 1-65,536 values, up to 4,096 characters each and 8 Mi characters total. Each package retains at most 128 registered downloader wrappers and 262,144 explicit scope values. Addressables cannot abort an in-flight `DownloadDependenciesAsync`; `Cancel`/`Dispose` cancels every joined caller-visible wait while the adapter retains the pending handle, drains it to terminal, records its final snapshot, and releases the provider handle exactly once. `CurrentDownloadBytes` caches provider status and refreshes at most four times per second. All downloader status properties are main-thread-affine.

Pending Addressables operations reject `WaitForAsyncComplete` on every platform. Await `Task`.

### YooAsset composition

The YooAsset provider targets stable `com.tuyoogame.yooasset` releases in `[3.0.5,4.0.0)`. The asmdef range is the compilation envelope; SemVer prereleases sort before their final release, so a prerelease inside that envelope can enter compilation, but the activation test rejects it as unsupported. `YooAssetModule` exclusively owns the process-global Yoo runtime. Each client instance that requires an isolated writable cache must pass a distinct explicit `PackageRoot` through its file-system parameters.

```csharp
using YooAsset;

var module = new YooAssetModule(asyncOperationMaxTimeSliceMs: 16L);
await module.InitializeAsync(new AssetManagementOptions());

IAssetPackage package = module.CreatePackage("DefaultPackage");
var options = new OfflinePlayModeOptions
{
    BuiltinFileSystemParameters =
        FileSystemParameters.CreateDefaultBuiltinFileSystemParameters(),
    BundleLoadingMaxConcurrency = 4,
};

bool initialized = await package.InitializeAsync(
    new AssetPackageInitOptions(providerOptions: options),
    cancellationToken);
```

Create a fresh `InitializePackageOptions` subtype for each package. The adapter accepts `BundleLoadingMaxConcurrency` 1-64; `int.MaxValue` selects the platform fallback.

| Mode | Required product-owned options |
| --- | --- |
| EditorSimulate | `EditorSimulateModeOptions` and Editor file-system parameters for the simulated package root |
| Offline | `OfflinePlayModeOptions` and built-in file-system parameters |
| Host | `HostPlayModeOptions`, built-in/cache file-system parameters, and an `IRemoteService` owned by the product |
| Web | `WebPlayModeOptions` and web-server and/or web-network file-system parameters |
| Custom | `CustomPlayModeOptions` with an ordered product-owned file-system parameter list |

`asyncOperationMaxTimeSliceMs` accepts 10-100 ms. It controls Yoo's main-thread asynchronous-operation budget, not network concurrency. The default 16 ms is not a 60/120 fps guarantee.

`IYooAssetPackageMaintenance` accepts a product-supplied version for typed manifest loading and exposes typed cache cleanup plus All/Tags/Locations downloaders. Package names and manifest versions are ASCII tokens, 1-128 characters with path-safe rules. Downloader concurrency is 1-32, retry count 0-16, and maintenance timeouts 1-3,600 seconds. Tag/location arrays use the same bounds as Addressables. Downloader `Cancel`/`Dispose` cancels every joined caller-visible wait, requests provider-native `CancelDownload`, and keeps the wrapper registered until it observes provider terminal state.

Desktop/Editor storage preflight is reliable only for Host mode using the default sandbox cache file system with a non-empty explicit `PackageRoot`. Other modes, custom file systems, implicit roots, mobile, WebGL, and consoles report `Unknown`.

Raw content is loaded as `RawFileObject`. The adapter snapshots its text and bytes on the Unity main thread before the operation publishes success. Completed `ReadText` and `ReadBytes` calls are then worker-safe; every `ReadBytes` call returns a new defensive copy. `FilePath` remains empty because a provider bundle/archive path is not a portable raw-file path.

Use `WebServerFileSystem` and/or `WebNetworkFileSystem` through `WebPlayModeOptions` on WebGL; `SandboxFileSystem` is not supported there. The provider `link.xml` preserves YooAsset's built-in reflected file-system implementations.

### Capability negotiation in consumers

```csharp
if (package is IAssetBulkLoader bulkLoader)
{
    using IAllAssetsHandle<UnityEngine.Sprite> handle =
        bulkLoader.LoadAllAssetsAsync<UnityEngine.Sprite>("UI/Icons");
    await handle.Task;
    UseSprites(handle.Assets);
}

if (package is IAssetRawFileLoader rawLoader)
{
    using IRawFileHandle raw = rawLoader.LoadRawFileAsync(
        "Config/Balance.bytes", cancellationToken: cancellationToken);
    await raw.Task;
    byte[] bytes = raw.ReadBytes();
    if (bytes == null) throw new System.IO.IOException(raw.Error);
}
```

Resources and Addressables do not implement `IAssetRawFileLoader`; a product needing portable data loading can author a `TextAsset` path through the normal asset contract.

## Advanced Topics

### VContainer

`AssetManagementVContainerInstaller` registers `IAssetModule`, starts module initialization through `IAsyncStartable`, and can register the retention scheduler. It does not create a package and cannot await module shutdown from synchronous scope disposal.

```csharp
var installer = new AssetManagementVContainerInstaller(
    moduleFactory: _ => new ResourcesModule(),
    options: new AssetManagementOptions(),
    cacheRetentionOptions: default,
    packageResolver: null);

installer.Install(builder);
```

VContainer cancellation is checked before provider-global initialization begins; it cannot interrupt initialization already in progress. Create and initialize packages separately. Before disposing the owning `LifetimeScope`, explicitly await `module.DestroyAsync()` and handle failure.

### Navigathena

Navigathena requires an explicit scene capability:

```csharp
IAssetSceneLoader sceneLoader = package as IAssetSceneLoader ??
    throw new System.NotSupportedException("The selected provider has no scene capability.");

var loadParameters = new UnityEngine.SceneManagement.LoadSceneParameters(
    UnityEngine.SceneManagement.LoadSceneMode.Additive)
{
    localPhysicsMode = UnityEngine.SceneManagement.LocalPhysicsMode.Physics2D
};

var identifier = sceneRef.ToSceneIdentifier(
    sceneLoader, loadParameters, SceneActivationMode.ActivateOnLoad,
    bucket: "Gameplay.Scene");
```

`CreateHandle` is lazy and does not acquire a provider scene. The first `Load` performs a cancellation precheck, acquires the provider handle, and starts authoritative cleanup if load or activation fails before ownership is handed back. Repeated `Load` calls fail fast; repeated or concurrent `Unload` calls join one retryable operation. StandardSceneNavigator is qualified only for additive loading, which is the integration default.

Navigathena 1.1.0 does not clean up the target handle when cancellation, blank-scene unloading, or entry-point discovery fails after `ISceneHandle.Load` returns but before `SceneState` takes ownership. Production use requires an upstream, fork, or custom navigator that closes both the post-load ownership handoff and asynchronous teardown boundaries.

### Custom provider boundary

A custom provider belongs in a separate assembly that references the core runtime. Keep SDK types out of provider-neutral public contracts. A production adapter must define and test: one owner for module, package, provider handles, instances, scenes, and downloaders; main-thread affinity and any narrow worker-safe operation; memoized multi-await completion and provider-fault propagation; caller cancellation versus shared caller-visible cancellation, physical-abort capability, pending-operation ownership, and terminal release; cache key generation and invalidation after content mutation; deterministic shutdown, leak containment, and retryable versus terminal failure; and platform, IL2CPP/AOT, stripping, WebGL, filesystem, and suspend/resume behavior.

Do not add a capability to `IAssetPackage` unless all providers can implement the same semantics. Provider-specific maintenance remains in the provider assembly.

## Troubleshooting

| Symptom | Likely cause | Resolution |
| --- | --- | --- |
| Provider assembly not compiling | Dependency missing or version out of range | Install and lock a supported stable SDK version; confirm `versionDefines` and reference the assembly from the composition asmdef |
| Addressables `ProviderOptions` rejected | Non-null options passed | `ProviderOptions` must be null for Addressables |
| YooAsset package initialization fails | Wrong `InitializePackageOptions` subtype | Construct the exact subtype for the selected play mode and pass it through `AssetPackageInitOptions.ProviderOptions` |
| Addressables catalog update split authority | Direct `Addressables.UpdateCatalogs` call | Route every catalog mutation through the owning package adapter |
| Downloader concurrency not effective | Provider-global scheduling | Addressables: concurrency is provider-managed; YooAsset: explicit 1-32 per downloader, but multiple downloaders and on-demand I/O add together |
| `WaitForAsyncComplete` throws | Pending Addressables operation | Await `Task` instead |
| YooAsset raw `FilePath` empty | By design | `FilePath` is intentionally empty; await `Task` and use `ReadText`/`ReadBytes` |
| Scene unload rejected | Different package or recreated generation | Use the originating package generation; a recreated generation is rejected |
| VContainer shutdown does not await | Scope disposal is synchronous | Explicitly await `module.DestroyAsync()` before disposing the `LifetimeScope` |

## Provider validation checklist

For every claimed provider/platform combination:

1. Lock the exact SDK version and confirm the conditional assembly is active.
2. Compile the provider and its consumer composition asmdef.
3. Validate success, fault, cancellation, repeated await, disposal, main-thread rejection for every status read, cross-package rejection, and shutdown. For Scene, cover `None`/`Physics2D`/`Physics3D` worlds, invalid enums, repeated/concurrent activation and unload, cancellation before mutation and join after commit, failed-load/unload recovery, Single/external unload, and exactly-once provider release.
4. Exercise a clean Player cache and content build, not only Editor simulation.
5. Test Mono and IL2CPP, stripping, domain reload disabled, network loss, storage full, low memory, suspend/resume, and process termination.
6. Record workload, device, content build, and raw performance evidence; do not generalize an Editor result to other platforms.
