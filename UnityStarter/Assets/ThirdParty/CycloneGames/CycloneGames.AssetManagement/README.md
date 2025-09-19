# CycloneGames.AssetManagement

English | [简体中文](./README.SCH.md)

A DI-first, interface-driven, unified asset management abstraction layer for Unity. It decouples your game logic from the underlying asset system (like YooAsset, Addressables, or Resources), allowing you to write cleaner, more portable, and high-performance code. A default, zero-GC provider for YooAsset is included.

## Requirements

- Unity 2022.3+
- Optional: `com.tuyoogame.yooasset`
- Optional: `com.unity.addressables`
- Optional: `com.cysharp.unitask`
- Optional: `jp.hadashikick.vcontainer`
- Optional: `com.cysharp.r3` (for `IPatchService` events)

## Quick Start

To get started, you need an implementation of the `IAssetModule` interface. The following example demonstrates how to use the `YooAssetModule` and load an asset, showcasing the unified, provider-agnostic API.

```csharp
using CycloneGames.AssetManagement.Runtime;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YooAsset; // YooAsset is only needed here for provider-specific options

public class MyGameManager
{
    private IAssetModule assetModule;

    public async UniTaskVoid Start()
    {
        // 1. Initialize the Module (ideally in a DI container)
        assetModule = new YooAssetModule();
        assetModule.Initialize(new AssetManagementOptions());

        // 2. Create and initialize a package
        var package = assetModule.CreatePackage("DefaultPackage");
        
        // Provider-specific options are passed directly.
        // This is one of the few places you need to reference the underlying provider's types.
        var yooAssetOptions = new HostPlayModeParameters(); 
        // ... configure yooAssetOptions if needed ...

        var initOptions = new AssetPackageInitOptions(AssetPlayMode.Host, yooAssetOptions);
        bool success = await package.InitializeAsync(initOptions);
        if (!success)
        {
            Debug.LogError("Package initialization failed.");
            return;
        }

        // 3. Load an asset using the unified API
        await LoadMyPlayer(package);
    }

    private async UniTask LoadMyPlayer(IAssetPackage package)
    {
        // The API call is the same, regardless of the backend!
        using (var handle = package.LoadAssetAsync<GameObject>("Prefabs/MyPlayer"))
        {
            await handle.Task; // Asynchronously wait for the asset to load

            if (handle.Asset)
            {
                // Instantiate the loaded asset. Both sync and async methods are available.
                // Use the sync version if the handle is already complete for a zero-GC instantiation.
                var go = package.InstantiateSync(handle);
            }
        }
    }
}
```

## More Usage Examples

### Offline Play Mode (YooAsset)

Here is how to initialize a package for `OfflinePlayMode`, which is common for single-player games where all assets are included in the initial build.

```csharp
// 1. Initialize the Module as usual
assetModule = new YooAssetModule();
assetModule.Initialize(new AssetManagementOptions());

// 2. Create a package
var package = assetModule.CreatePackage("DefaultPackage");

// 3. Create YooAsset-specific parameters for Offline Mode
var yooAssetOfflineParams = new OfflinePlayModeParameters(); 
// In this mode, YooAsset locates assets via a build-in file query service,
// which is configured in the YooAsset Editor settings.

// 4. Wrap provider-specific options into our generic InitOptions
var initOptions = new AssetPackageInitOptions(
    AssetPlayMode.Offline,      // Set the play mode
    yooAssetOfflineParams       // Pass the provider-specific options
);

// 5. Initialize the package
bool success = await package.InitializeAsync(initOptions);
if (success)
{
    // The package is now ready to load assets from the local build.
}
```

### Addressables Provider

The Addressables provider has a simpler initialization flow, as it initializes globally. Note that the Addressables provider does not support synchronous operations.

```csharp
using Cysharp.Threading.Tasks;
using UnityEngine;

public class MyAddressablesManager
{
    private IAssetModule assetModule;

    public async UniTaskVoid Start()
    {
        // 1. Create the module
        assetModule = new AddressablesModule();
        
        // 2. Initialize and wait for it to be ready
        // Addressables initializes asynchronously in the background.
        // We must wait for the 'Initialized' flag to become true.
        assetModule.Initialize();
        await UniTask.WaitUntil(() => assetModule.Initialized);

        Debug.Log("Addressables Module Initialized.");

        // 3. Create a "package" (this is a logical grouping for Addressables)
        var package = assetModule.CreatePackage("DefaultPackage");
        
        // 4. Load an asset
        // Note: Addressables provider only supports async operations.
        using (var handle = package.LoadAssetAsync<GameObject>("MyAddressablePrefab"))
        {
            await handle.Task;
            if (handle.Asset)
            {
                var go = await package.InstantiateAsync(handle).Task;
            }
        }
    }
}
```
> [!NOTE]
> The Addressables provider has some limitations compared to the YooAsset provider:
> - It does not support synchronous loading or instantiation.
> - It does not support the `IPatchService` workflow.
> - Advanced features like version querying and pre-downloading specific versions are not available.

### Resources Provider

The `Resources` provider is the simplest and is useful for quick prototyping or accessing assets bundled directly with the game. It does not require any special initialization.

```csharp
// 1. Create and initialize the module
assetModule = new ResourcesModule();
assetModule.Initialize(); // Initialization is synchronous

// 2. Create a package
var package = assetModule.CreatePackage("DefaultPackage");

// 3. Load an asset (both sync and async are supported)
using (var handle = package.LoadAssetAsync<GameObject>("Path/In/Resources/Folder"))
{
    await handle.Task;
    if (handle.Asset)
    {
        var go = package.InstantiateSync(handle);
    }
}
```
> [!WARNING]
> The `Resources` provider has significant limitations:
> - It cannot load scenes.
> - It does not support any download or patch features.
> - `LoadAllAssetsAsync` is a blocking, synchronous operation.
> - Assets loaded from `Resources` cannot be unloaded individually, which can lead to higher memory usage. It is generally not recommended for production use in large projects.

## Features

- **Interface-First Design**: Decouples your game logic from the underlying asset system. Write your code against a stable interface and swap the backend anytime without major refactoring.
- **DI-Friendly**: Designed from the ground up for dependency injection (`VContainer`, `Zenject`, etc.), making it easy to manage asset loading services in a clean and testable way.
- **Unified API**: Provides a single, consistent API for all asset operations. Whether you're using `YooAsset`, `Addressables`, or a custom `Resources` wrapper, the calling code remains the same.
- **High-Performance & Low GC**: Fully asynchronous API based on `UniTask` for maximum performance. The included YooAsset provider is optimized to reduce garbage collection, especially on hot paths like asset instantiation.
- **Extensible**: Easily create your own providers by implementing the `IAssetModule` and `IAssetPackage` interfaces.
- **Advanced Features**: Built-in support for the full asset lifecycle, including version checking, manifest updates, pre-downloading, and cache management.

## High-Level Update Workflow (YooAsset Provider)

For a streamlined update process, the `YooAsset` provider includes a high-level `IPatchService` that encapsulates the entire update state machine. It uses `R3` (Reactive Extensions) to provide event streams.

```csharp
// 1. Get the patch service from the module
IPatchService patchService = assetModule.CreatePatchService("DefaultPackage");

// 2. Subscribe to patch events
patchService.PatchEvents
    .Subscribe(evt =>
    {
        var (patchEvent, args) = evt;
        if (patchEvent == PatchEvent.FoundNewVersion)
        {
            var eventArgs = (FoundNewVersionEventArgs)args;
            // Show a dialog to the user: "Found new version with size {eventArgs.TotalDownloadSizeBytes}"
            // If user confirms, call patchService.Download();
        }
        else if (patchEvent == PatchEvent.PatchDone)
        {
            // Patch is complete, proceed to game
        }
    });

// 3. Run the patch process
await patchService.RunAsync(autoDownloadOnFoundNewVersion: false);
```

## Low-Level Update & Download API

For more granular control, you can use the low-level `IAssetPackage` API.

- **Request latest version**:
  ```csharp
  string version = await package.RequestPackageVersionAsync();
  ```

- **Pre-download a specific version** (without switching the active manifest):
  ```csharp
  var downloader = await package.CreatePreDownloaderForAllAsync(version, downloadingMaxNumber: 10, failedTryAgain: 3);
  if (downloader != null)
  {
      await downloader.StartAsync(); // Supports cancellation
  }
  ```

- **Update active manifest**:
  ```csharp
  bool manifestUpdated = await package.UpdatePackageManifestAsync(version);
  ```

- **Download by tags**:
  ```csharp
  IDownloader downloader = package.CreateDownloaderForTags(new[]{"Base", "UI"}, 10, 3);
  downloader.Begin();
  await downloader.StartAsync();
  ```

- **Clear cache**:
  ```csharp
  await package.ClearCacheFilesAsync(ClearCacheMode.Unused);
  ```

## Scene Management

```csharp
// Asynchronous load
var sceneHandle = package.LoadSceneAsync("Assets/Scenes/Main.unity");
await sceneHandle.Task;

// Asynchronous unload
await package.UnloadSceneAsync(sceneHandle);
```
> [!WARNING]
> Synchronous scene loading (`LoadSceneSync`) is not recommended as it can cause significant performance issues by blocking the main thread. Always prefer the asynchronous version.

## Scripting Define Symbols

This package uses Assembly Definition Files (`.asmdef`) to automatically define symbols based on which other packages are present in your project.

- `YOOASSET_PRESENT`: Enables the YooAsset provider.
- `ADDRESSABLES_PRESENT`: Enables the Addressables provider.
- `VCONTAINER_PRESENT`: Enables VContainer integration helpers.

You do not need to manage these symbols manually.
