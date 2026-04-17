# CycloneGames.AssetManagement

English | [简体中文](./README.SCH.md)

A DI-first, interface-driven, unified asset management abstraction layer for Unity. It decouples your game logic from the underlying asset system (YooAsset, Addressables, or Resources), enabling cleaner, more portable, and high-performance code.

Built on a **W-TinyLFU** inspired caching architecture, it provides deterministic memory management, fine-grained resource tracking (via Tag/Owner metadata), and comes with powerful editor debugging tools (Cache Debugger and Handle Tracker) to guarantee leak-free memory operations.

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
- [Asset References (AssetRef / SceneRef)](#asset-references-assetref--sceneref)
- [API Reference](#api-reference)

---

## Requirements

| Dependency   | Required | Description                                     |
| ------------ | -------- | ----------------------------------------------- |
| Unity        | 2022.3+  | Minimum Unity version                           |
| UniTask      | Yes      | `com.cysharp.unitask` - Async/await support     |
| YooAsset     | Optional | `com.tuyoogame.yooasset` - Recommended provider |
| Addressables | Optional | `com.unity.addressables` - Alternative provider |
| VContainer   | Optional | `jp.hadashikick.vcontainer` - DI integration    |
| R3           | Optional | `com.cysharp.r3` - For `IPatchService` events   |

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
>
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

| Interface         | Purpose                                                         |
| ----------------- | --------------------------------------------------------------- |
| `IAssetModule`    | Entry point for the asset system. Creates and manages packages. |
| `IAssetPackage`   | Handles all asset operations: loading, instantiation, scenes.   |
| `IAssetHandle<T>` | Represents a loaded asset. Disposable for memory management.    |
| `IPatchService`   | High-level hot update workflow (YooAsset only).                 |

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

| Feature          | YooAsset   | Addressables      | Resources   |
| ---------------- | ---------- | ----------------- | ----------- |
| Sync Loading     | Yes        | No                | Yes         |
| Async Loading    | Yes        | Yes               | Yes         |
| Hot Update       | Yes        | Limited           | No          |
| Scene Loading    | Yes        | Yes               | No          |
| Raw File Loading | Yes        | No                | No          |
| Recommended For  | Production | Existing Projects | Prototyping |

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
>
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
>
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

### High-Performance Asset Cache (W-TinyLFU inspired)

The asset management system features a zero-GC, three-tier caching architecture (Active, Trial, and Main pools) to maximize cache hit rates and deterministic memory management without adding runtime overhead:

- **Active Pool**: Assets currently explicitly referenced by game logic (Refs > 0).
- **Trial Pool (LRU)**: A probation area for recently released assets.
- **Main Pool (LFU/LRU)**: A hot cache for frequently accessed assets that have survived the Trial pool.

Cache capacity and deterministic eviction can be controlled via **Buckets**:

```csharp
// Load an asset and assign it to the "UI" bucket
using (var handle = package.LoadAssetAsync<GameObject>("Prefabs/MainMenu", bucket: "UI"))
{
    // ...
}

// Later, clear only idle assets in the "UI" bucket to forcefully free memory
package.ClearBucket("UI");
```

> [!NOTE]
> `ClearBucket` / `ClearBucketsByPrefix` only evict handles whose `RefCount` has already reached 0 (idle in Trial or Main pools). Active handles (still in use) are **never** evicted — this prevents dangling references by design.

#### Hierarchical Bucket Paths

Bucket names support a **hierarchical dot-separated** convention (e.g. `"UI.Scene.MainCity"`). Use `AssetBucketPath` to compose and match paths safely:

```csharp
using CycloneGames.AssetManagement.Runtime;

// Compose hierarchical bucket names (zero-alloc when segments are non-empty)
string bucket = AssetBucketPath.Combine("UI", "Scene");           // → "UI.Scene"
string sub    = AssetBucketPath.Combine("UI", "Scene", "MainCity"); // → "UI.Scene.MainCity"

// Clear a single exact bucket
package.ClearBucket("UI.Scene.MainCity");

// Clear a bucket and ALL its descendants in one call
package.ClearBucketsByPrefix("UI");
// → clears "UI", "UI.Scene", "UI.Scene.MainCity", etc.
```

#### AssetBucketScope (Scoped Loading)

`AssetBucketScope` is a lightweight wrapper that **pre-fills** `bucket`, `tag`, and `owner` for every loading call, eliminating repetitive parameter passing:

```csharp
// Create a scope — all loads inherit its bucket/tag/owner
var uiScope = package.CreateBucketScope("UI", tag: "UIAsset", owner: "UIManager");

// Load through the scope — no need to repeat bucket/tag/owner
using (var handle = uiScope.LoadAssetAsync<GameObject>("Prefabs/MainMenu"))
{
    await handle.Task;
    var menu = uiScope.Package.InstantiateSync(handle);
}

// Create a child scope for a sub-system (bucket becomes "UI.Shop")
var shopScope = uiScope.CreateChild("Shop", owner: "ShopUI");
using (var handle = shopScope.LoadAssetAsync<Sprite>("Icons/Coin"))
{
    await handle.Task;
}

// When leaving the UI, clear only this scope's bucket hierarchy
uiScope.ClearHierarchy();  // clears "UI" and all descendants
// Or clear only the exact bucket:
shopScope.Clear();          // clears "UI.Shop" only
```

### Resource Tracking & Metadata

To make runtime resource tracking effortless, loading APIs support zero-GC `tag` and `owner` metadata parameters. This enables fine-grained tracking of exactly _who_ loaded an asset and _what_ it is used for.

```csharp
// Load an asset with tracing metadata
var handle = package.LoadAssetAsync<GameObject>("Prefabs/Hero",
    tag: "Character",
    owner: "PlayerSpawner"
);
```

**Case Study: UIFramework Integration**
`CycloneGames.UIFramework` seamlessly integrates this feature. When opening a UI window, it automatically tags loaded assets:

- `owner`: The specific UI window's name (e.g., `HomeUI`)
- `tag`: The asset category (e.g., `UIConfig` or `UIPrefab`)

This makes it instantly clear in the debugger which UI is holding onto memory.

### Advanced Editor Debugging Tools

`CycloneGames.AssetManagement` provide a powerful, best-in-class editor windows to visualize cache health and identify memory leaks without guessing.

#### 1. Asset Cache Debugger Window (`Tools/CycloneGames/AssetManagement/Asset Cache Debugger`)

A comprehensive view of the entire W-TinyLFU cache.

- **Tier Visualization**: Instantly see if assets are Active, in Trial, or in the Main hot cache.
- **Metadata Columns**: Sort and filter by `Tag`, `Owner`, and `Bucket`.
- **Ref-count Anomalies**: Automatically highlights active assets with unusually high reference counts (> 8), warning you of potential missing `Dispose()` calls.
- **Summary Breakdowns**: Statistical distribution of assets by Provider, Tag, and Owner.

#### 2. Handle Tracker Window (`Tools/CycloneGames/AssetManagement/Asset Handle Tracker`)

A microscopic view of every active handle allocation, cross-referenced against the cache.

- **Smart Status Identification**: Detects whether an active handle (Refs=0) is safely sitting in the idle cache (`Cached`), or if it is a genuine memory leak (`Leaked`).
- **Stack Trace Expansion**: Click any leaked handle to instantly reveal the exact C# stack trace where it was allocated.

<img src="./Documents~/Doc_01.png" style="width: 100%; height: auto; max-width: 900px;" />
<img src="./Documents~/Doc_02.png" style="width: 100%; height: auto; max-width: 900px;" />
<img src="./Documents~/Doc_03.png" style="width: 100%; height: auto; max-width: 900px;" />
<img src="./Documents~/Doc_04.png" style="width: 100%; height: auto; max-width: 900px;" />
<img src="./Documents~/Doc_05.png" style="width: 100%; height: auto; max-width: 900px;" />

---

## Asset References (AssetRef / SceneRef)

### Why AssetRef?

In a real project, referencing assets by raw strings like `"Prefabs/Enemy"` is fragile:

- **No type safety** — nothing prevents you from passing a Texture path to a method expecting a Prefab.
- **No rename tracking** — if an artist moves or renames the asset, you get a silent runtime failure.
- **No Inspector support** — designers must type paths manually and can't drag-and-drop.
- **Direct `UnityEngine.Object` references** in prefab/SO fields pull assets into the same bundle, bloating memory and preventing per-language/per-patch splitting.

`AssetRef<T>` and `SceneRef` solve all of these while remaining **zero-cost at runtime** (they are `struct` types that store only two strings).

### Design Principles

| Principle                     | How                                                                                                                                                 |
| ----------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Pure data key**             | `AssetRef` stores `location` + `guid`. It never loads, never caches, never holds a handle.                                                          |
| **Zero GC**                   | `struct`, not `class`. 100k references = zero heap objects.                                                                                         |
| **Loading via IAssetPackage** | `package.LoadAsync(assetRef)` returns `IAssetHandle<T>` — reusing the existing ARC + W-TinyLFU cache.                                               |
| **GUID auto-heal**            | Editor PropertyDrawer resolves GUID → path on every frame. If the asset was moved/renamed, the stored location is updated automatically.            |
| **SceneRef separation**       | `SceneAsset` is editor-only, and scenes use `LoadSceneAsync` instead of `LoadAssetAsync`. A separate `SceneRef` type carries the correct semantics. |
| **Build validation**          | `AssetRefValidator` scans all Prefabs and ScriptableObjects for broken GUIDs before shipping.                                                       |

### Available Types

| Type          | Usage                                                                                                       |
| ------------- | ----------------------------------------------------------------------------------------------------------- |
| `AssetRef<T>` | Typed reference. Inspector ObjectField is filtered by `T` (e.g., `AssetRef<AudioClip>` only accepts audio). |
| `AssetRef`    | Non-generic reference. For data-driven configs or runtime-resolved types.                                   |
| `SceneRef`    | Scene reference. Inspector ObjectField is filtered to `SceneAsset`.                                         |

### Quick Start

#### 1. Declare References in Inspector

```csharp
using CycloneGames.AssetManagement.Runtime;
using UnityEngine;

public class EnemyConfig : ScriptableObject
{
    [Header("Visual")]
    [SerializeField] private AssetRef<GameObject> prefab;
    [SerializeField] private AssetRef<Material>   material;

    [Header("Audio")]
    [SerializeField] private AssetRef<AudioClip> spawnSound;
    [SerializeField] private AssetRef<AudioClip> deathSound;

    [Header("Scene")]
    [SerializeField] private SceneRef bossArena;

    // Read-only accessors for other systems
    public AssetRef<GameObject> Prefab     => prefab;
    public AssetRef<AudioClip>  SpawnSound => spawnSound;
    public SceneRef             BossArena  => bossArena;
}
```

In the Inspector, each field renders as a standard ObjectField — drag-and-drop from the Project window, type-filtered. No manual path entry required.

#### 2. Load Assets at Runtime

```csharp
public class EnemySpawner
{
    private readonly IAssetPackage package;
    private readonly EnemyConfig config;

    public async UniTask SpawnEnemy(Vector3 position)
    {
        // AssetRef<T> → IAssetHandle<T>, fully integrated with ARC + cache
        using (var handle = package.LoadAsync(config.Prefab, bucket: "Gameplay"))
        {
            await handle.Task;
            var enemy = package.InstantiateSync(handle);
            enemy.transform.position = position;
        }
    }
}
```

#### 3. Load Scenes (with Navigathena Integration)

```csharp
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.AssetManagement.Runtime.Integrations.Navigathena;

public class LevelLoader
{
    private readonly IAssetPackage package;
    private readonly ISceneNavigator navigator;

    public async UniTask LoadBossArena(EnemyConfig config)
    {
        // Option A: Direct scene loading via IAssetPackage
        var sceneHandle = package.LoadSceneAsync(config.BossArena);
        await sceneHandle.Task;

        // Option B: Navigathena scene navigation (push/pop/change)
        await navigator.Push(config.BossArena.ToSceneIdentifier(package));
    }
}
```

#### 4. Backward Compatibility

`AssetRef<T>`, `AssetRef`, and `SceneRef` all support `implicit operator string`, so they work directly with existing `string`-based APIs:

```csharp
// These are equivalent:
package.LoadAssetAsync<GameObject>(config.Prefab.Location);
package.LoadAssetAsync<GameObject>(config.Prefab);  // implicit string conversion
package.LoadAsync(config.Prefab);                    // extension method (recommended)
```

#### 5. Works with Any Asset Type

Any type inheriting from `UnityEngine.Object` is supported:

```csharp
[SerializeField] AssetRef<GameObject>       prefab;          // Prefab
[SerializeField] AssetRef<ScriptableObject> config;          // Any ScriptableObject
[SerializeField] AssetRef<YarnProject>      dialogue;        // Yarn Spinner project
[SerializeField] AssetRef<Sprite>           icon;            // Sprite
[SerializeField] AssetRef<AudioClip>        clip;            // Audio
[SerializeField] AssetRef<Material>         mat;             // Material
[SerializeField] AssetRef<AnimationClip>    anim;            // Animation
[SerializeField] AssetRef<TextAsset>        textFile;        // TextAsset
[SerializeField] SceneRef                   scene;           // Scene
```

### Build Validation

Before shipping, validate all references via the menu:

**`Tools > CycloneGames > AssetManagement > Validate All AssetRefs`**

This scans every Prefab and ScriptableObject in the project:

- **Broken refs**: GUID no longer resolves → logged as error.
- **Stale locations**: Asset was moved/renamed but GUID is still valid → location auto-healed.

Integrate this into your CI/CD pipeline by calling `AssetRefValidator.ValidateAll()` from a build script.

---

## API Reference

### IAssetModule

| Method                     | Description                            |
| -------------------------- | -------------------------------------- |
| `InitializeAsync(options)` | Initialize the asset system            |
| `Destroy()`                | Cleanup and release resources          |
| `CreatePackage(name)`      | Create a new asset package             |
| `GetPackage(name)`         | Get an existing package                |
| `RemovePackageAsync(name)` | Remove and destroy a package           |
| `CreatePatchService(name)` | Create a patch service (YooAsset only) |

### IAssetPackage

| Method                         | Description                                                         |
| ------------------------------ | ------------------------------------------------------------------- |
| `InitializeAsync(options)`     | Initialize the package                                              |
| `DestroyAsync()`               | Destroy the package                                                 |
| `LoadAssetAsync<T>(...)`       | Load an asset asynchronously (supports `bucket`/`tag`/`owner`)      |
| `LoadAssetSync<T>(...)`        | Load an asset synchronously (supports `bucket`/`tag`/`owner`)       |
| `LoadAllAssetsAsync<T>(...)`   | Load all assets at location (supports `bucket`/`tag`/`owner`)       |
| `InstantiateAsync(handle)`     | Instantiate a loaded prefab                                         |
| `InstantiateSync(handle)`      | Sync instantiate (zero-GC)                                          |
| `LoadSceneAsync(location)`     | Load a scene                                                        |
| `UnloadSceneAsync(handle)`     | Unload a scene                                                      |
| `LoadRawFileAsync(location)`   | Load a raw file                                                     |
| `UnloadUnusedAssetsAsync()`    | Global sweep of all unused assets                                   |
| `ClearBucket(bucket)`          | Evict idle handles matching an exact bucket name                    |
| `ClearBucketsByPrefix(prefix)` | Evict idle handles matching a bucket prefix and all its descendants |

### Scripting Define Symbols

These symbols are automatically defined based on installed packages:

| Symbol                 | When Defined                      |
| ---------------------- | --------------------------------- |
| `YOOASSET_PRESENT`     | YooAsset package is installed     |
| `ADDRESSABLES_PRESENT` | Addressables package is installed |
| `VCONTAINER_PRESENT`   | VContainer package is installed   |

---

## Best Practices

1. **Always dispose handles** - Use `using` statements or call `Dispose()` manually
2. **Use async loading** - Sync loading blocks the main thread
3. **Choose the right provider** - YooAsset for production, Resources for prototyping
4. **Enable handle tracking in development** - Helps find memory leaks early
5. **Use DI containers** - Register `IAssetModule` as a singleton for clean architecture
