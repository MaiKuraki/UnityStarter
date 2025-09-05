# CycloneGames.AssetManagement

English | [简体中文](./README.SCH.md)

A DI-first, interface-driven, unified asset management abstraction layer for Unity. It decouples your game logic from the underlying asset system (like YooAsset, Addressables, or Resources), allowing you to write cleaner, more portable code. A default provider for YooAsset is included.

## Requirements

- Unity 2022.3+
- Optional: `com.tuyoogame.yooasset`
- Optional: `com.unity.addressables`
- Optional: `com.cysharp.unitask`, `jp.hadashikick.vcontainer`, `com.mackysoft.navigathena`, `com.cyclonegames.factory`, `com.cyclone-games.logger`, `com.harumak.addler`

## Quick Start

To get started, you need an implementation of the `IAssetModule` interface that works with your chosen asset system. The following example demonstrates how to use a custom module that loads assets from Unity's `Resources` folder. This showcases how your game code interacts with the unified API, completely decoupled from the underlying `Resources.Load` calls.

```csharp
using CycloneGames.AssetManagement;
using UnityEngine;
using System.Threading.Tasks;

// Assume you have written a 'ResourcesModule' that implements IAssetModule for the Resources system.
// A minimal implementation might look like this:
async Task LoadMyPlayer()
{
    IAssetModule module = new ResourcesModule();
    module.Initialize(new AssetManagementOptions());

    var pkg = module.CreatePackage("MyResources");

    // The API call is the same, regardless of the backend!
    using (var handle = pkg.LoadAssetAsync<GameObject>("Prefabs/MyPlayer"))
    {
        // In a real project, you would await this asynchronously.
        while (!handle.IsDone)
        {
            await Task.Yield(); // Or yield return null in a coroutine
        }

        if (handle.Asset)
        {
            var go = Object.Instantiate(handle.Asset);
        }
    }
}
```

## Features

- **Interface-First Design**: Decouples your game logic from the underlying asset system. Write your code against a stable interface and swap the backend anytime without major refactoring.
- **DI-Friendly**: Designed from the ground up for dependency injection, making it easy to manage asset loading services in a clean and testable way.
- **Unified API**: Provides a single, consistent API for all asset operations. Whether you're using `YooAsset`, `Addressables`, or a custom `Resources.Load` wrapper, the calling code remains the same.
- **Extensible**: Easily create your own providers by implementing the `IAssetModule` and `IAssetPackage` interfaces to support any asset management system.
- **Robust Tooling**: Includes built-in support for common needs like batch downloading, retries, caching, and progress aggregation.

## Concepts (2-minute overview)

- IAssetModule: global initializer/registry for logical packages
- IAssetPackage: one content package (catalog + bundles) with loading, downloading, scenes
- Handle types: `IAssetHandle<T>`, `IAllAssetsHandle<T>`, `IInstantiateHandle`, `ISceneHandle` (all disposable when you own them)
- Downloader: batching and progress APIs for prefetch/update flows
- Diagnostics: optional handle leak tracker, can be disabled in production

## Update & Download

- Request latest version:

```csharp
string version = await pkg.RequestPackageVersionAsync();
```

- Update active manifest:

```csharp
bool ok = await pkg.UpdatePackageManifestAsync(version);
```

- Pre-download a specific version (without switching active manifest yet):

```csharp
var downloader = await pkg.CreatePreDownloaderForAllAsync(version, downloadingMaxNumber: 8, failedTryAgain: 2);
await downloader.StartAsync();
```

- Download by tags or by locations:

```csharp
IDownloader d1 = pkg.CreateDownloaderForTags(new[]{"Base","UI"}, 8, 2);
IDownloader d2 = pkg.CreateDownloaderForLocations(new[]{"Assets/Prefabs/Hero.prefab"}, true, 8, 2);
d1.Combine(d2);
d1.Begin();
await d1.StartAsync();
```

- Clear cache:

```csharp
await pkg.ClearCacheFilesAsync(clearMode: "All");
```

## Scenes (Basics)

```csharp
var scene = pkg.LoadSceneAsync("Assets/Scenes/Main.unity");
scene.WaitForAsyncComplete();
await pkg.UnloadSceneAsync(scene);
```

Notes:

- `activateOnLoad` is respected. It maps to YooAsset's `suspendLoad` flag (we suspend when `activateOnLoad == false`). You can manually activate via YooAsset API after loading when needed.

## Navigathena Integration (optional)

To use Navigathena with scenes backed by this asset management system, use the provider-agnostic `AssetManagementSceneIdentifier`. This works regardless of whether you are using YooAsset or Addressables underneath.

```csharp
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.AssetManagement.Runtime.Integrations.Navigathena;
using MackySoft.Navigathena.SceneManagement;

// Get the IAssetPackage from your IAssetModule
IAssetPackage pkg = assetModule.GetPackage("DefaultPackage");

// Create the identifier. It will use the provided package to load the scene.
ISceneIdentifier id = new AssetManagementSceneIdentifier(pkg, "Assets/Scenes/Main.unity", LoadSceneMode.Additive, true);
await GlobalSceneNavigator.Instance.Push(id);
```

## Addressables + YooAsset Coexistence (Short Notes)

- Coexistence is supported. Keep Addressables keys equal to YooAsset locations to switch identifiers at runtime.
- Choose Addressables or YooAsset identifiers by config; no extra setup is required here.

## User-confirmed Update Flow (recommended UX)

The module supports a "check → confirm → update" UX.

```csharp
// 1) Check latest
string latest = await pkg.RequestPackageVersionAsync();
bool hasUpdate = !string.IsNullOrEmpty(latest) && latest != currentVersion;
if (!hasUpdate) return;

// 2) Pre-download to estimate size; ask user for confirmation
var pre = await pkg.CreatePreDownloaderForAllAsync(latest, downloadingMaxNumber: 8, failedTryAgain: 2);
long totalBytes = (pre?.TotalDownloadBytes) ?? 0;
int totalFiles = (pre?.TotalDownloadCount) ?? 0;
// Show a dialog: $"Update size {totalBytes} bytes ({totalFiles} files). Proceed?"
await pre.StartAsync(); // user confirmed; supports cancellation

// 3) Switch manifest
bool switched = await pkg.UpdatePackageManifestAsync(latest);
if (switched) { currentVersion = latest; /* persist */ }

// Optional: purge old cache
// await pkg.ClearCacheFilesAsync(clearMode: "All");
```

- For partial updates, use tag or location-based downloaders before switching the manifest.
- Handle cancellations by catching `OperationCanceledException` from `StartAsync` and keeping the old manifest.

## Additional Options

- Synchronous scene loading

```csharp
var handle = pkg.LoadSceneSync("Assets/Scenes/Main.unity", LoadSceneMode.Single);
```

- Handle tracking (diagnostics)

```csharp
module.Initialize(new AssetManagementOptions(
  operationSystemMaxTimeSliceMs: 16,
  bundleLoadingMaxConcurrency: 8,
  logger: null,
  enableHandleTracking: true // Editor/Dev recommended; can be disabled in production
));
```

## Factory Integration (optional)

- Define symbol: `CYCLONEGAMES_FACTORY_PRESENT` (auto-defined when package `com.cyclonegames.factory` is present via versionDefines)
- Prefab factory backed by asset package:

```csharp
using CycloneGames.AssetManagement.Integrations.Factory;

var factory = new YooAssetPrefabFactory<MyMono>(pkg, "Assets/Prefabs/My.prefab");
var instance = factory.Create();
factory.Dispose(); // release cached handle when finished
```

This allows reusing a cached prefab handle for repeated instantiation without re-loading, and fits pooling/Factory patterns.

## Scripting Define Symbols

This package uses Assembly Definition Files (`.asmdef`) to automatically define symbols based on which other packages are present in your project. This allows for optional integrations without causing compile errors if a dependency is missing.

The following symbols are defined and used internally:

- `YOOASSET_PRESENT`: Defined when `com.tuyoogame.yooasset` is installed. Enables the YooAsset provider.
- `ADDRESSABLES_PRESENT`: Defined when `com.unity.addressables` is installed. Enables the Addressables provider.
- `VCONTAINER_PRESENT`: Defined when `jp.hadashikick.vcontainer` is installed. Enables the VContainer integration.
- `NAVIGATHENA_PRESENT`: Defined when `com.mackysoft.navigathena` is installed. Enables the Navigathena integration.

You generally do not need to interact with these symbols directly.

## Scene Preload (optional)

Pre-warm content per scene using manifests to reduce spikes during scene switches.

### Setup

1) Create one or more `PreloadManifest` assets (location + weight)
2) Create a `ScenePreloadRegistry` asset mapping `sceneKey` -> list of manifests (sceneKey can be your scene location/name)
3) Set `NavigathenaYooSceneFactory.DefaultPackage = pkg` at boot
4) In `NavigathenaNetworkManager` (provided in NavigathenaMirror), assign `scenePreloadRegistry`
5) Ensure Navigathena and YooAsset are installed; macros are auto-defined by asmdefs

### Flow (Mirror + Navigathena)

- Server:
  - Before notifying clients, runs `_preloadManager.OnBeforeLoadSceneAsync(sceneKey)`
  - Sends scene message to clients
  - Loads scene via Navigathena, then calls `_preloadManager.OnAfterLoadScene(sceneKey)`
- Client:
  - On scene message, runs `OnBeforeLoadSceneAsync(sceneKey)` → Navigathena Replace → `OnAfterLoadScene(sceneKey)`

### Manual use

```csharp
using CycloneGames.AssetManagement.Integrations.Navigathena;
using CycloneGames.AssetManagement.Preload;

var registry = /* load ScenePreloadRegistry */;
var preload = new ScenePreloadManager(pkg, registry);
await preload.OnBeforeLoadSceneAsync("Assets/Scenes/Main.unity");
// ... perform your scene switch via Navigathena
preload.OnAfterLoadScene("Assets/Scenes/Main.unity");
```

### Notes

- For progress calculation and behavior details of PreloadManifest entries, see inline C# XML/tooltips in `PreloadManifest`.

## VContainer Integration (optional)

```csharp
using VContainer;
using VContainer.Unity;
using CycloneGames.AssetManagement;
using YooAsset;

public class GameLifetimeScope : LifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        builder.Register<IAssetModule, YooAssetModule>(Lifetime.Singleton);
        builder.RegisterBuildCallback(async resolver =>
        {
            var module = resolver.Resolve<IAssetModule>();
            module.Initialize(new AssetManagementOptions(16, int.MaxValue));
            var pkg = module.CreatePackage("Default");
            var host = new HostPlayModeParameters
            {
                BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters(),
                CacheFileSystemParameters = FileSystemParameters.CreateDefaultCacheFileSystemParameters(remoteServices: null)
            };
            await pkg.InitializeAsync(new AssetPackageInitOptions(AssetPlayMode.Host, host, 8));
        });
    }
}
```

Notes:

- You can inherit `AssetManagementVContainerInstaller` and override parameter creation per scene.

## Provider Example: Using the YooAsset Adapter

If you have `com.tuyoogame.yooasset` in your project, you can use the provided adapter for a powerful, production-ready asset solution. The setup is similar to the original Quick Start.

```csharp
using CycloneGames.AssetManagement;
using YooAsset;

// 1) Initialize the specific YooAssetModule
IAssetModule module = new YooAssetModule();
module.Initialize(new AssetManagementOptions(operationSystemMaxTimeSliceMs: 16));

// 2) Create and initialize a package
var pkg = module.CreatePackage("Default");
var hostParams = new HostPlayModeParameters
{
    BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters(),
    CacheFileSystemParameters = FileSystemParameters.CreateDefaultCacheFileSystemParameters(remoteServices: null)
};
await pkg.InitializeAsync(new AssetPackageInitOptions(AssetPlayMode.Host, hostParams, bundleLoadingMaxConcurrencyOverride: 8));

// 3) Load and instantiate using the unified API
using (var handle = pkg.LoadAssetAsync<UnityEngine.GameObject>("Assets/Prefabs/My.prefab"))
{
    handle.WaitForAsyncComplete();
    var go = pkg.InstantiateSync(handle); // Note: InstantiateSync is a YooAsset-specific extension
}
```
For details on updating, downloading, and scene management with the YooAsset provider, please refer to the corresponding sections in this document.

## Provider Example: Using the Addressables Adapter

If you have `com.unity.addressables` in your project, you can use the provided adapter for it. The setup is straightforward.

```csharp
using CycloneGames.AssetManagement;

// 1) Initialize the AddressableAssetModule
IAssetModule module = new AddressableAssetModule();
module.Initialize(new AssetManagementOptions());

// 2) Create a package (name is logical and doesn't affect Addressables groups)
var pkg = module.CreatePackage("Default");

// 3) Load an asset using the unified API
using (var handle = pkg.LoadAssetAsync<UnityEngine.GameObject>("Assets/Prefabs/MyCharacter.prefab"))
{
    // In a real project, you would await this asynchronously.
    while (!handle.IsDone)
    {
        await System.Threading.Tasks.Task.Yield(); // Or yield return null in a coroutine
    }
    
    if (handle.Asset)
    {
        var go = UnityEngine.Object.Instantiate(handle.Asset);
    }
}
```
Note that some features like package versioning and pre-downloading are specific to YooAsset and do not have a direct equivalent in the Addressables adapter.

## Other Tips

### Caching

```csharp
var cache = new CycloneGames.AssetManagement.Cache.AssetCacheService(pkg, maxEntries: 128);
var icon = cache.Get<Sprite>("Assets/Art/UI/Icons/Abilities/Fireball.png");
cache.TryRelease("Assets/Art/UI/Icons/Abilities/Fireball.png");
```

### Retry

```csharp
using CycloneGames.AssetManagement.Retry;
var policy = new RetryPolicy(maxAttempts: 3, initialDelaySeconds: 0.5, backoffFactor: 2.0);
var handle = await pkg.LoadAssetWithRetryAsync<Sprite>("Assets/Art/.../Icon.png", policy, ct);
```

### Progress

```csharp
using CycloneGames.AssetManagement.Progressing;
var agg = new ProgressAggregator();
agg.Add(groupOp1, 2f);
agg.Add(groupOp2, 1f);
var p = agg.GetProgress(); // 0..1
```

## Addler Integration (Recommended for Lifetime Management)

While the `AssetManagement` package provides the necessary tools for memory management (via `IDisposable` handles), manually managing the lifetime of every handle in a large project can be error-prone. `Addler` is a higher-level framework that automates handle lifetime management and pooling.

Our package provides a seamless integration with Addler.

### How to Register

During your application's bootstrap phase, after initializing the `AssetManagement` module, you can create an instance of our `AssetManagementAssetLoader` and register it with Addler's `AssetProvider`.

```csharp
using Addler.Runtime.Core;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.AssetManagement.Runtime.Integrations.Addler;

// 1. Initialize your IAssetModule as usual
IAssetModule assetModule = new YooAssetModule(); // or AddressableAssetModule
assetModule.Initialize(new AssetManagementOptions());
IAssetPackage defaultPackage = assetModule.CreatePackage("DefaultPackage");
// ... initialize the package

// 2. Create our custom asset loader, backed by our IAssetPackage
var assetLoader = new AssetManagementAssetLoader(defaultPackage);

// 3. Set this loader as the default for Addler
AssetProvider.Setup(assetLoader);

// 4. Now, you can use Addler to load assets, and it will use our system underneath
var playerHandle = await AssetProvider.LoadAssetAsync<GameObject>("player_prefab_key");
// ... use the asset
// Addler will automatically manage the release of the underlying handle when it's no longer needed.
```

By using this setup, you gain the benefits of Addler's automated memory management while still using our flexible, provider-agnostic asset loading backend.
