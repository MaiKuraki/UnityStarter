# CycloneGames.AssetManagement

English | [简体中文](./README.SCH.md)

A DI-first, interface-driven, unified asset management abstraction layer for Unity. It decouples your game logic from the underlying asset system (YooAsset, Addressables, or Resources), enabling cleaner, more portable, and high-performance code.

## Table of Contents

- [Requirements](#requirements)
- [Installation](#installation)
- [Quick Start](#quick-start)
- [Core Concepts](#core-concepts)
- [Provider Comparison](#provider-comparison)
- [Usage Examples](#usage-examples)
  - [YooAsset Provider](#yooasset-provider)
  - [Addressables Provider](#addressables-provider)
  - [Resources Provider](#resources-provider)
- [Hot Update Workflow](#hot-update-workflow)
- [Advanced Features](#advanced-features)
- [API Reference](#api-reference)

---

## Requirements

| Dependency | Required | Description |
|------------|----------|-------------|
| Unity | 2022.3+ | Minimum Unity version |
| UniTask | Yes | `com.cysharp.unitask` - Async/await support |
| YooAsset | Optional | `com.tuyoogame.yooasset` - Recommended provider |
| Addressables | Optional | `com.unity.addressables` - Alternative provider |
| VContainer | Optional | `jp.hadashikick.vcontainer` - DI integration |
| R3 | Optional | `com.cysharp.r3` - For `IPatchService` events |

## Installation

1. Import the package into your Unity project
2. The module automatically detects available providers via Assembly Definition references
3. No manual scripting define symbols configuration required

---

## Quick Start

This section walks you through loading your first asset in just a few steps.

### Step 1: Initialize (Once at Game Startup)

```csharp
using CycloneGames.AssetManagement.Runtime;
using Cysharp.Threading.Tasks;
using YooAsset;

public class GameBootstrap
{
    // Store the module reference for later access
    public static IAssetModule AssetModule { get; private set; }
    
    public async UniTask Initialize()
    {
        // Create and initialize the module (do this once)
        AssetModule = new YooAssetModule();
        await AssetModule.InitializeAsync();

        // Create and initialize a package (do this once per package)
        var package = AssetModule.CreatePackage("DefaultPackage");
        
        var initOptions = new AssetPackageInitOptions(
            AssetPlayMode.Offline,
            new OfflinePlayModeParameters()
        );
        
        await package.InitializeAsync(initOptions);
    }
}
```

### Step 2: Load Assets (Anywhere in Your Game)

```csharp
using UnityEngine;

public class PlayerSpawner
{
    public async UniTask SpawnPlayer()
    {
        // Get the existing package (don't create it again!)
        var package = GameBootstrap.AssetModule.GetPackage("DefaultPackage");
        
        // Load and use the asset
        using (var handle = package.LoadAssetAsync<GameObject>("Prefabs/Player"))
        {
            await handle.Task;
            
            if (handle.Asset != null)
            {
                GameObject player = package.InstantiateSync(handle);
            }
        }
    }
}
```

> [!TIP]
> **CreatePackage vs GetPackage**
> - `CreatePackage(name)` - Call once during initialization to create a new package
> - `GetPackage(name)` - Call anywhere else to retrieve an existing package

---

## Core Concepts

### Architecture Overview

```
Game Logic
    |
    v
IAssetModule (Interface)
    |
    +-- YooAssetModule (Recommended)
    +-- AddressablesModule
    +-- ResourcesModule
    |
    v
IAssetPackage (Interface)
    |
    v
Asset Loading / Instantiation / Scene Management
```

### Key Interfaces

| Interface | Purpose |
|-----------|---------|
| `IAssetModule` | Entry point for the asset system. Creates and manages packages. |
| `IAssetPackage` | Handles all asset operations: loading, instantiation, scenes. |
| `IAssetHandle<T>` | Represents a loaded asset. Disposable for memory management. |
| `IPatchService` | High-level hot update workflow (YooAsset only). |

### Handle Lifecycle

Handles represent loaded assets and must be properly disposed:

```csharp
// Option 1: Using statement (recommended)
using (var handle = package.LoadAssetAsync<Texture2D>("Textures/Icon"))
{
    await handle.Task;
    // Use handle.Asset here
}
// Automatically disposed

// Option 2: Manual disposal
var handle = package.LoadAssetAsync<Texture2D>("Textures/Icon");
await handle.Task;
// ... use the asset ...
handle.Dispose(); // Don't forget this!
```

---

## Provider Comparison

| Feature | YooAsset | Addressables | Resources |
|---------|----------|--------------|-----------|
| Sync Loading | Yes | No | Yes |
| Async Loading | Yes | Yes | Yes |
| Hot Update | Yes | Limited | No |
| Scene Loading | Yes | Yes | No |
| Raw File Loading | Yes | No | No |
| Recommended For | Production | Existing Projects | Prototyping |

---

## Usage Examples

### YooAsset Provider

YooAsset is the recommended provider with full feature support.

#### Offline Mode (Single-player Games)

```csharp
public async UniTask InitializeOffline()
{
    // 1. Create and initialize module
    var assetModule = new YooAssetModule();
    await assetModule.InitializeAsync();

    // 2. Create package
    var package = assetModule.CreatePackage("DefaultPackage");

    // 3. Initialize for offline mode
    var initOptions = new AssetPackageInitOptions(
        AssetPlayMode.Offline,
        new OfflinePlayModeParameters()
    );
    
    await package.InitializeAsync(initOptions);

    // 4. Load assets
    using (var handle = package.LoadAssetAsync<GameObject>("Prefabs/Enemy"))
    {
        await handle.Task;
        var enemy = package.InstantiateSync(handle);
    }
}
```

#### Host Mode (Online Games with Hot Update)

```csharp
public async UniTask InitializeOnline()
{
    var assetModule = new YooAssetModule();
    await assetModule.InitializeAsync();

    var package = assetModule.CreatePackage("DefaultPackage");

    // Configure host mode with your CDN
    var hostParams = new HostPlayModeParameters
    {
        BuildinQueryServices = new DefaultBuildinQueryServices(),
        RemoteServices = new DefaultRemoteServices("https://cdn.example.com/bundles")
    };

    var initOptions = new AssetPackageInitOptions(AssetPlayMode.Host, hostParams);
    await package.InitializeAsync(initOptions);
}
```

### Addressables Provider

Suitable for projects already using Unity Addressables.

```csharp
public async UniTask UseAddressables()
{
    // 1. Create and initialize
    var assetModule = new AddressablesModule();
    await assetModule.InitializeAsync();

    // 2. Create package
    var package = assetModule.CreatePackage("DefaultPackage");

    // 3. Load assets (async only)
    using (var handle = package.LoadAssetAsync<GameObject>("MyAddressableKey"))
    {
        await handle.Task;
        if (handle.Asset != null)
        {
            var instance = await package.InstantiateAsync(handle).Task;
        }
    }
}
```

> [!NOTE]
> Addressables limitations:
> - No synchronous operations
> - No `IPatchService` support
> - No raw file loading

### Resources Provider

Best for quick prototyping or small projects.

```csharp
public async UniTask UseResources()
{
    // 1. Create and initialize (synchronous)
    var assetModule = new ResourcesModule();
    await assetModule.InitializeAsync();

    // 2. Create package
    var package = assetModule.CreatePackage("DefaultPackage");

    // 3. Load from Resources folder
    using (var handle = package.LoadAssetAsync<Sprite>("Icons/Coin"))
    {
        await handle.Task;
        myImage.sprite = handle.Asset;
    }
}
```

> [!WARNING]
> Resources limitations:
> - Cannot load scenes
> - No hot update support
> - Assets cannot be individually unloaded
> - Not recommended for production

---

## Hot Update Workflow

### High-Level API (Recommended)

The `IPatchService` provides a complete update workflow with event-driven architecture:

```csharp
public async UniTask RunPatchFlow()
{
    // Get the patch service
    var patchService = assetModule.CreatePatchService("DefaultPackage");

    // Subscribe to events
    patchService.PatchEvents.Subscribe(evt =>
    {
        var (eventType, args) = evt;
        
        switch (eventType)
        {
            case PatchEvent.FoundNewVersion:
                var versionArgs = (FoundNewVersionEventArgs)args;
                Debug.Log($"New version found! Size: {versionArgs.TotalDownloadSizeBytes} bytes");
                // Show confirmation dialog, then call:
                // patchService.Download();
                break;
                
            case PatchEvent.DownloadProgress:
                var progressArgs = (DownloadProgressEventArgs)args;
                Debug.Log($"Progress: {progressArgs.Progress:P0}");
                break;
                
            case PatchEvent.PatchDone:
                Debug.Log("Update complete!");
                break;
                
            case PatchEvent.PatchFailed:
                Debug.LogError("Update failed!");
                break;
        }
    });

    // Start the patch process
    await patchService.RunAsync(autoDownloadOnFoundNewVersion: false);
}
```

### Low-Level API (Fine-grained Control)

For custom update flows:

```csharp
// Check for updates
string latestVersion = await package.RequestPackageVersionAsync();

// Update manifest
bool updated = await package.UpdatePackageManifestAsync(latestVersion);

// Create downloader
var downloader = package.CreateDownloaderForAll(downloadingMaxNumber: 10, failedTryAgain: 3);

// Monitor progress
while (!downloader.IsDone)
{
    Debug.Log($"Downloaded: {downloader.CurrentDownloadBytes}/{downloader.TotalDownloadBytes}");
    await UniTask.Yield();
}

// Clear unused cache
await package.ClearCacheFilesAsync(ClearCacheMode.Unused);
```

---

## Advanced Features

### Raw File Loading

Load non-Unity files like JSON, XML, or binary data:

```csharp
// Async loading
using (var handle = package.LoadRawFileAsync("Config/settings.json"))
{
    await handle.Task;
    string jsonText = handle.ReadText();
    var settings = JsonUtility.FromJson<GameSettings>(jsonText);
}

// Sync loading
var handle = package.LoadRawFileSync("Data/level.bin");
byte[] data = handle.ReadBytes();
handle.Dispose();
```

### Scene Management

```csharp
// Load scene
var sceneHandle = package.LoadSceneAsync("Assets/Scenes/Gameplay.unity");
await sceneHandle.Task;

// The scene is now active

// Unload scene
await package.UnloadSceneAsync(sceneHandle);
```

### Batch Loading

Load multiple assets with progress tracking:

```csharp
using CycloneGames.AssetManagement.Runtime.Batch;

var group = new GroupOperation();

// Add operations with optional weights
group.Add(package.LoadAssetAsync<Texture2D>("Tex1"), weight: 1f);
group.Add(package.LoadAssetAsync<Texture2D>("Tex2"), weight: 1f);
group.Add(package.LoadAssetAsync<AudioClip>("Music"), weight: 2f);

// Track progress
_ = TrackProgress(group);

await group.StartAsync();

async UniTask TrackProgress(GroupOperation op)
{
    while (!op.IsDone)
    {
        loadingBar.value = op.Progress;
        await UniTask.Yield();
    }
}
```

### LRU Cache

Automatic caching with LRU eviction:

```csharp
using CycloneGames.AssetManagement.Runtime.Cache;

var cache = new AssetCacheService(package, maxEntries: 100);

// Get from cache (loads if not cached)
var sprite = cache.Get<Sprite>("Icons/Coin");

// Release specific item
cache.TryRelease("Icons/Coin");

// Clear all
cache.Clear();
```

### Handle Tracking (Debug)

Track active handles to detect leaks:

```csharp
// Enable tracking (do this before loading assets)
HandleTracker.Enabled = true;
HandleTracker.EnableStackTrace = true; // For detailed leak analysis

// Later, check for leaks
var report = HandleTracker.GetActiveHandlesReport();
Debug.Log(report);
```

---

## API Reference

### IAssetModule

| Method | Description |
|--------|-------------|
| `InitializeAsync(options)` | Initialize the asset system |
| `Destroy()` | Cleanup and release resources |
| `CreatePackage(name)` | Create a new asset package |
| `GetPackage(name)` | Get an existing package |
| `RemovePackageAsync(name)` | Remove and destroy a package |
| `CreatePatchService(name)` | Create a patch service (YooAsset only) |

### IAssetPackage

| Method | Description |
|--------|-------------|
| `InitializeAsync(options)` | Initialize the package |
| `DestroyAsync()` | Destroy the package |
| `LoadAssetAsync<T>(location)` | Load an asset asynchronously |
| `LoadAssetSync<T>(location)` | Load an asset synchronously |
| `LoadAllAssetsAsync<T>(location)` | Load all assets at location |
| `InstantiateAsync(handle)` | Instantiate a loaded prefab |
| `InstantiateSync(handle)` | Sync instantiate (zero-GC) |
| `LoadSceneAsync(location)` | Load a scene |
| `UnloadSceneAsync(handle)` | Unload a scene |
| `LoadRawFileAsync(location)` | Load a raw file |
| `UnloadUnusedAssets()` | Unload unused assets |

### Scripting Define Symbols

These symbols are automatically defined based on installed packages:

| Symbol | When Defined |
|--------|--------------|
| `YOOASSET_PRESENT` | YooAsset package is installed |
| `ADDRESSABLES_PRESENT` | Addressables package is installed |
| `VCONTAINER_PRESENT` | VContainer package is installed |

---

## Best Practices

1. **Always dispose handles** - Use `using` statements or call `Dispose()` manually
2. **Use async loading** - Sync loading blocks the main thread
3. **Choose the right provider** - YooAsset for production, Resources for prototyping
4. **Enable handle tracking in development** - Helps find memory leaks early
5. **Use DI containers** - Register `IAssetModule` as a singleton for clean architecture
