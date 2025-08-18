# CycloneGames.AssetManagement

English | [简体中文](./README.SCH.md)

DI-first, interface-driven asset management abstraction for Unity. Default provider is YooAsset, also compatible with Navigathena scene management.

## Requirements

- Unity 2022.3+
- Required: `com.tuyoogame.yooasset`
- Optional: `com.cysharp.unitask`, `jp.hadashikick.vcontainer`, `com.mackysoft.navigathena`, `com.cyclonegames.factory`, `com.cyclone-games.logger`, `com.harumak.addler`

## Quick Start

```csharp
using CycloneGames.AssetManagement;
using YooAsset;

// 1) Initialize module
IAssetModule module = new YooAssetModule();
module.Initialize(new AssetModuleOptions(operationSystemMaxTimeSliceMs: 16));

// 2) Create and initialize a package
var pkg = module.CreatePackage("Default");
var hostParams = new HostPlayModeParameters
{
    BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters(),
    CacheFileSystemParameters = FileSystemParameters.CreateDefaultCacheFileSystemParameters(remoteServices: null)
};
await pkg.InitializeAsync(new AssetPackageInitOptions(AssetPlayMode.Host, hostParams, bundleLoadingMaxConcurrencyOverride: 8));

// 3) Load and instantiate
using (var handle = pkg.LoadAssetAsync<UnityEngine.GameObject>("Assets/Prefabs/My.prefab"))
{
    handle.WaitForAsyncComplete();
    var go = pkg.InstantiateSync(handle);
}
```

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

To use Navigathena with YooAsset-backed scenes, use the provided identifier:

```csharp
using CycloneGames.AssetManagement.Integrations.Navigathena;
using MackySoft.Navigathena.SceneManagement;

IAssetPackage pkg = module.GetPackage("Default");
ISceneIdentifier id = new YooAssetSceneIdentifier(pkg, "Assets/Scenes/Main.unity", LoadSceneMode.Additive, true, 100);
await GlobalSceneNavigator.Instance.Change(new LoadSceneRequest(id));
```

### Dual-stack switching (Addressables <-> YooAsset)

Keep Addressables keys equal to YooAsset locations. At runtime, select the identifier:

```csharp
ISceneIdentifier id;
if (useAddressables)
{
    // Addressables identifier (requires ENABLE_NAVIGATHENA_ADDRESSABLES)
    id = new MackySoft.Navigathena.SceneManagement.AddressableAssets.AddressableSceneIdentifier("Assets/Scenes/Main.unity");
}
else
{
    id = new CycloneGames.AssetManagement.Integrations.Navigathena.YooAssetSceneIdentifier(pkg, "Assets/Scenes/Main.unity");
}
await GlobalSceneNavigator.Instance.Change(new LoadSceneRequest(id));
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
module.Initialize(new AssetModuleOptions(
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

## Macro Notes

- `NAVIGATHENA_PRESENT`: auto-defined when `com.mackysoft.navigathena` is present
- `NAVIGATHENA_YOOASSET`: auto-defined when `com.tuyoogame.yooasset` is present (integration enabled automatically)
- `ENABLE_NAVIGATHENA_ADDRESSABLES`: official Addressables integration from Navigathena
- `VCONTAINER_PRESENT`: auto-defined when `jp.hadashikick.vcontainer` is present
- `ADDLER_PRESENT`: auto-defined when `com.harumak.addler` is present (enables optional Addler adapter)

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
            module.Initialize(new AssetModuleOptions(16, int.MaxValue));
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

## Optional Addler Adapter

See `Runtime/Scripts/Integrations/Addler/AddlerAdapterREADME.md` for mapping Addler keys to YooAsset locations. Enabled with `ADDLER_PRESENT` when `com.harumak.addler` is present.
