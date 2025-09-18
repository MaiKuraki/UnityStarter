# CycloneGames.AssetManagement

English | [简体中文](./README.SCH.md)

A DI-first, interface-driven, unified asset management abstraction layer for Unity. It decouples your game logic from the underlying asset system (like YooAsset, Addressables, or Resources), allowing you to write cleaner, more portable, and high-performance code. A default, zero-GC provider for YooAsset is included.

## Requirements

- Unity 2022.3+
- Optional: `com.tuyoogame.yooasset`
- Optional: `com.unity.addressables`
- Optional: `com.cysharp.unitask`, `com.cysharp.r3`
- Optional: `jp.hadashikick.vcontainer`

## Quick Start

To get started, you need an implementation of the `IAssetModule` interface. The following example demonstrates how to use the `YooAssetManagementModule` and load an asset, showcasing the unified, provider-agnostic API.

```csharp
using CycloneGames.AssetManagement.Runtime;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YooAsset;

public class MyGameManager
{
    private IAssetModule assetModule;

    public async UniTaskVoid Start()
    {
        // 1. Initialize the Module (ideally in a DI container)
        assetModule = new YooAssetManagementModule();
        assetModule.Initialize(new AssetManagementOptions());

        // 2. Create and initialize a package
        var package = assetModule.CreatePackage("DefaultPackage");
        var initOptions = new AssetPackageInitOptions(
            AssetPlayMode.Host,
            new HostPlayModeParameters() // Configure your YooAsset parameters here
        );
        await package.InitializeAsync(initOptions);

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
                // Use the provider-specific extension for Zero-GC instantiation
                var go = package.InstantiateSync(handle);
            }
        }
    }
}
```

## Features

- **Interface-First Design**: Decouples your game logic from the underlying asset system. Write your code against a stable interface and swap the backend anytime without major refactoring.
- **DI-Friendly**: Designed from the ground up for dependency injection (`VContainer`, `Zenject`, etc.), making it easy to manage asset loading services in a clean and testable way.
- **Unified API**: Provides a single, consistent API for all asset operations. Whether you're using `YooAsset`, `Addressables`, or a custom `Resources` wrapper, the calling code remains the same.
- **Zero-GC on Hot Path**: The default `YooAsset` provider supports zero-garbage collection for critical operations like asset instantiation.
- **UniTask-Powered**: Fully asynchronous API based on `UniTask` for maximum performance and minimal overhead in Unity.
- **Offline Mode Support**: Full support for single-player/offline games by configuring providers (e.g., YooAsset's `OfflinePlayMode`) to load all assets from the local build.
- **Extensible**: Easily create your own providers by implementing the `IAssetModule` and `IAssetPackage` interfaces.

## High-Level Update Workflow (YooAsset Provider)

For a streamlined update process, the `YooAsset` provider includes a high-level `IPatchService` that encapsulates the entire update state machine, inspired by best practices.

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

## Low-Level Update & Download API (YooAsset Provider)

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
> Synchronous scene loading (`LoadSceneSync`) is deprecated as it can cause significant performance issues. Always prefer the asynchronous version.

## Scripting Define Symbols

This package uses Assembly Definition Files (`.asmdef`) to automatically define symbols based on which other packages are present in your project.

- `YOOASSET_PRESENT`: Enables the YooAsset provider.
- `ADDRESSABLES_PRESENT`: Enables the Addressables provider.
- `VCONTAINER_PRESENT`: Enables VContainer integration helpers.

You do not need to manage these symbols manually.
