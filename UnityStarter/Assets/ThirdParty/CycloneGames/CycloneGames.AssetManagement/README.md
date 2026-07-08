# CycloneGames.AssetManagement

English | [简体中文](./README.SCH.md)

A DI-first, interface-driven, unified asset management abstraction layer for Unity. Gameplay code talks to `IAssetModule` and `IAssetPackage`; concrete providers such as Resources, YooAsset, Addressables, or future provider adapters live behind assembly boundaries.

Built on a **W-TinyLFU** inspired cache, it provides deterministic idle-handle eviction, Tag/Owner tracking metadata, content trust verification for downloaded files, and editor diagnostics for inspecting cache pressure, handles, scenes, and runtime governance.

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
| R3           | Yes      | `com.cysharp.r3` - For `IPatchService` events   |
| CycloneGames.Hash | Yes | `com.cyclone-games.hash` - Deterministic fingerprints and content hashes |
| CycloneGames.IO | Yes | `com.cyclone-games.io` - File hashing and path-safety helpers |
| CycloneGames.Logger | Yes | `com.cyclone-games.logger` - Runtime diagnostics |
| YooAsset     | Optional | `com.tuyoogame.yooasset` - Optional provider |
| Addressables | Optional | `com.unity.addressables` - Optional provider |
| Navigathena  | Optional | `com.mackysoft.navigathena` - Optional scene navigation bridge |
| VContainer   | Optional | `jp.hadashikick.vcontainer` - Optional DI integration |

## Installation

1. Import the package into your Unity project
2. Install the required core dependencies listed above when the package is not imported through UPM dependency resolution
3. Provider assemblies are enabled through asmdef `versionDefines` and `defineConstraints`
4. Do not add PlayerSettings scripting define symbols manually

### Assembly Layout

The core runtime assembly depends on UniTask, R3, CycloneGames.Hash, CycloneGames.IO, and CycloneGames.Logger. Provider and integration code lives in separate assemblies. Optional assemblies use `versionDefines` to generate `CYCLONEGAMES_HAS_*` capability symbols, then `defineConstraints` to compile only when the package exists. Optional provider/integration assemblies are not auto-referenced; host assemblies should explicitly reference only the provider or bridge they use.

| Assembly | Purpose | Optional dependency |
| --- | --- | --- |
| `CycloneGames.AssetManagement.Runtime` | Core interfaces, cache, Resources provider, references, diagnostics | None beyond core dependencies |
| `CycloneGames.AssetManagement.Runtime.Providers.YooAsset` | YooAsset provider and YooAsset patch workflow | `com.tuyoogame.yooasset` |
| `CycloneGames.AssetManagement.Runtime.Providers.Addressables` | Addressables provider and version-file helpers | `com.unity.addressables` |
| `CycloneGames.AssetManagement.Runtime.Integrations.VContainer` | VContainer composition helper | `jp.hadashikick.vcontainer` |
| `CycloneGames.AssetManagement.Runtime.Integrations.Navigathena` | Navigathena scene bridge backed by `IAssetPackage` | `com.mackysoft.navigathena` |
| `CycloneGames.AssetManagement.Runtime.CacheRetention` | Optional, opt-in scheduler that periodically applies cache retention policies | None beyond core dependencies |

---

## Quick Start

This section walks you through loading your first asset in just a few steps.

### Step 1: Initialize (Once at Game Startup)

```csharp
using CycloneGames.AssetManagement.Runtime;
using Cysharp.Threading.Tasks;

public class GameBootstrap
{
    // Store the module reference for later access
    public static IAssetModule AssetModule { get; private set; }

    public async UniTask Initialize()
    {
        // Create and initialize the module (do this once)
        AssetModule = new ResourcesModule();
        await AssetModule.InitializeAsync();

        // Create and initialize a package (do this once per package)
        var package = AssetModule.CreatePackage("DefaultPackage");

        var initOptions = new AssetPackageInitOptions(
            AssetPlayMode.Offline,
            providerOptions: null
        );

        await package.InitializeAsync(initOptions);
    }
}
```

### Step 2: Load Assets (Anywhere in Your Game)

```csharp
using Cysharp.Threading.Tasks;
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
    +-- YooAssetModule
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
| Common Fit       | Patch-heavy products | Existing Addressables projects | Built-in prototypes and small tools |

---

## Usage Examples

### YooAsset Provider

YooAsset is an optional provider for projects that explicitly adopt its package and patch workflow.

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
                float progress = progressArgs.TotalDownloadSizeBytes <= 0
                    ? 0f
                    : (float)progressArgs.CurrentDownloadSizeBytes / progressArgs.TotalDownloadSizeBytes;
                Debug.Log($"Progress: {progress:P0}");
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

#### Tuning Download Parameters

`RunAsync` accepts an optional `PatchDownloadOptions` to tune the download phase. Any non-positive field falls back to its default, so passing `default` is always safe:

```csharp
await patchService.RunAsync(
    autoDownloadOnFoundNewVersion: true,
    downloadOptions: new PatchDownloadOptions
    {
        MaxConcurrentDownloads = 16,   // default 10
        FailedRetryCount       = 5,    // default 3
        RequestTimeoutSeconds  = 90,   // default 60
    });
```

#### Transactional Patch With Content Trust

Patch services that implement `IAssetPatchTransactionService` expose a stricter transaction API. It keeps the legacy event stream, but returns a `PatchRunResult` and can verify a provider-neutral `ContentTrustManifest` after downloads finish:

```csharp
using CycloneGames.AssetManagement.Runtime.Trust;

var manifest = new ContentTrustManifest(
    version: "2026.07.09",
    entries: bundleEntries,
    rollbackVersion: "2026.07.08");

var trustOptions = new PatchContentTrustOptions(
    rootDirectory: downloadRoot,
    manifest: manifest,
    signatureVerifier: signatureVerifier,
    failurePolicy: PatchTrustFailurePolicy.RollbackManifestThenFail,
    clearUnusedCacheAfterRollback: true,
    failureBuffer: reusableFailureList);

var runOptions = new PatchRunOptions(
    autoDownloadOnFoundNewVersion: true,
    downloadOptions: PatchDownloadOptions.Default,
    trustOptions: trustOptions,
    appendTimeTicks: false);

if (patchService is IAssetPatchTransactionService transaction)
{
    PatchRunResult result = await transaction.RunAsync(runOptions, cancellationToken);
    if (result.Succeeded)
    {
        Debug.Log($"Patch applied: {result.PackageVersion}");
    }
}
```

The transaction state machine reports public `PatchWorkflowState` values through `PatchEvent.PatchStatesChanged`. Content trust failures throw `PatchTrustVerificationException`; depending on `PatchTrustFailurePolicy`, the service can fail fast, clear unused cache, clear all cache, repair corrupted locations, or update the active manifest back to the rollback version and then fail. The rollback step is intentionally explicit and provider-neutral: it calls `UpdatePackageManifestAsync(rollbackVersion)` and optionally `ClearCacheFilesAsync(ClearCacheMode.Unused)`.

#### Manifest Documents and Signing Payloads

`ContentTrustManifestBuilder` creates provider-neutral manifests from build outputs or downloaded files. It normalizes relative locations to forward slashes, validates duplicate locations after deterministic sorting, and can compute SHA-256 or XxHash64 entries through `CycloneGames.IO`.

`ContentTrustManifestCodec` writes and reads a compact JSON document for transport and inspection. The JSON document is not the signing boundary. Signatures should be calculated over `ContentTrustManifestCanonicalPayload` bytes, which use a deterministic schema version, length-prefixed UTF-8 strings, little-endian numeric fields, sorted entries, and no `Signature` field. This prevents JSON whitespace, property order, escaping, or parser behavior from changing signature semantics.

```csharp
ContentTrustManifest unsignedManifest = new ContentTrustManifestBuilder()
    .WithVersion("2026.07.09")
    .WithRollbackVersion("2026.07.08")
    .AddFile(contentRoot, "bundles/ui.bundle")
    .Build();

ContentTrustManifest signedManifest =
    ContentTrustManifestSignatureUtility.Sign(unsignedManifest, manifestSigner);

string json = ContentTrustManifestCodec.ToJson(signedManifest);
byte[] canonicalPayload = ContentTrustManifestCodec.ToCanonicalPayloadBytes(signedManifest);
```

#### Content Repair and Self-Healing

Content repair is provider-neutral. `AssetRepairPlanner` converts content trust failures into a deterministic `AssetRepairPlan`; `AssetRepairService` executes the plan by clearing unused cache, downloading the failed locations through `CreateDownloaderForLocations`, and optionally running content trust verification again. The service does not know Addressables, YooAsset, or any future provider SDK type.

```csharp
var repairService = new AssetRepairService(package);
var repairOptions = new AssetRepairOptions(
    downloadOptions: PatchDownloadOptions.Default,
    trustOptions: trustOptions,
    clearUnusedCacheBeforeDownload: true,
    recursiveDownloadLocations: true,
    verifyAfterRepair: true);

AssetRepairRunResult repair = await repairService.RepairAsync(
    manifest,
    reusableFailureList,
    repairOptions,
    cancellationToken);
```

Only location-based content failures are repairable: missing file, size mismatch, hash mismatch, hash computation failure, and I/O error. Manifest-level failures such as invalid manifest data, rejected signatures, unsupported hash algorithms, or paths escaping the trust root remain non-repairable and should fail fast or roll back. For patch transactions, `PatchTrustFailurePolicy.RepairLocationsThenReverify` repairs location failures and allows the patch to succeed only when the post-repair verification passes. `RepairLocationsThenFail` performs the same repair attempt but still fails the transaction so the caller can decide when to retry or restart.

#### Patch Profiles and Product Policy

Long-lived product policy should be configured through a profile instead of being hard-coded into gameplay code. `AssetPatchProfileAsset` is the Unity authoring bridge; it builds an `AssetPatchRuntimeProfile` for the current platform or a specified platform. The runtime profile then creates `PatchRunOptions` once the product has supplied the current trust manifest, signature verifier, and reusable failure buffer.

```csharp
AssetPatchRuntimeProfile profile = patchProfileAsset.BuildRuntimeProfile();

PatchRunOptions runOptions = profile.CreateRunOptions(
    manifest: signedManifest,
    signatureVerifier: signatureVerifier,
    failureBuffer: reusableFailureList);

IPatchService patchService = assetModule.CreatePatchService(profile.PackageName);
PatchRunResult result = await ((IAssetPatchTransactionService)patchService)
    .RunAsync(runOptions, cancellationToken);
```

Server, headless, or DI composition code can bypass Unity assets and use the builder directly:

```csharp
AssetPatchRuntimeProfile profile = new AssetPatchRuntimeProfileBuilder()
    .WithPackageName("Main")
    .WithPlatform(AssetPatchPlatform.Android)
    .WithDownloadPolicy(new AssetPatchDownloadPolicy(8, 3, 60))
    .WithTrustPolicy(new AssetPatchTrustPolicy(
        enabled: true,
        rootDirectory: contentRoot,
        PatchTrustFailurePolicy.RollbackManifestThenFail,
        rollbackVersionOverride: null,
        clearUnusedCacheAfterRollback: true))
    .Build();
```

The profile owns policy only: package name, platform override, download concurrency/retry/timeout, append-time behavior, content trust root, trust failure policy, rollback override, and post-rollback cache cleanup. It does not own CDN routing, login state, regional rollout rules, UI decisions, or account-specific entitlement logic; inject those from the product layer before creating `PatchRunOptions`.

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

### Content Trust Verification

`CycloneGames.AssetManagement.Runtime.Trust` provides provider-neutral verification for downloaded bundles, raw files, and external catalog payloads before they enter a runtime cache. It is intended for update and patch boundaries, not gameplay hot paths.

```csharp
using CycloneGames.AssetManagement.Runtime.Trust;

var entry = new ContentTrustFileEntry(
    "bundles/ui.bundle",
    sizeBytes: 1048576,
    ContentTrustHashAlgorithm.Sha256,
    expectedHashHex: "...");

ContentTrustVerificationResult result =
    ContentTrustVerifier.Shared.VerifyFile(downloadRoot, entry);

if (!result.Succeeded)
{
    // Reject the update, quarantine the file, or trigger a redownload.
}
```

Supported checks include manifest root containment, per-file path traversal defense, file size, SHA-256, XxHash64, and optional signature policy via `IContentTrustSignatureVerifier`. The verifier uses `CycloneGames.IO` for file hashing and path containment, and `CycloneGames.Hash` for deterministic manifest fingerprints. It does not write files or keep persistent state.

### Runtime Cache Diagnostics

Packages that implement `IAssetRuntimeDiagnostics` expose an allocation-free aggregate cache snapshot for telemetry, stress HUDs, and automatic memory governance:

```csharp
if (package is IAssetRuntimeDiagnostics diagnostics)
{
    AssetRuntimeCacheSnapshot snapshot = diagnostics.GetRuntimeCacheSnapshot();
    if (snapshot.IdleBudgetUsage > 0.8f)
    {
        package.TrimIdleCache(AssetCacheRetentionPolicy.IdleForAtLeast(TimeSpan.FromSeconds(30)));
    }
}
```

The snapshot reports package name, provider name, active handle count, idle handle count, approximate idle bytes, idle byte budget, and budget usage. It intentionally does not enumerate individual cache entries; use the Editor cache debugger for per-entry analysis.

### Runtime Telemetry Recorder

`AssetRuntimeTelemetryRecorder` records a bounded in-memory window of `AssetRuntimeCacheSnapshot` samples. It is caller-owned, has no background thread, and does not write files by itself. Use it in players, stress builds, QA builds, or in-game debug panels when the game needs a small local record of asset pressure:

```csharp
var recorder = new AssetRuntimeTelemetryRecorder(
    new AssetRuntimeTelemetryOptions(
        capacity: 512,
        minimumSampleInterval: TimeSpan.FromSeconds(1),
        includeZeroActivitySamples: false));

if (package is IAssetRuntimeDiagnostics diagnostics)
{
    recorder.TryRecord(diagnostics);
}
```

To persist the current bounded window in a packaged build, use `AssetRuntimeTelemetryFileSink` with caller-owned scratch buffers:

```csharp
string path = AssetRuntimeTelemetryPaths.GetDefaultPersistentJsonLinesPath();
var sink = new AssetRuntimeTelemetryFileSink();
var samples = new AssetRuntimeTelemetrySample[recorder.Capacity];
var text = new StringBuilder(64 * 1024);

await sink.WriteJsonLinesAsync(path, recorder, samples, text, cancellationToken);
```

The default path is:

```text
Application.persistentDataPath/CycloneGames/AssetManagement/Diagnostics/asset-runtime-telemetry.jsonl
```

The file is JSON Lines and is atomically replaced on each flush. This is intentional: the module stores a bounded diagnostic window instead of an unbounded log stream. Delete the file to reset diagnostics; it is not a source of truth, should not be checked into Git, and can be rebuilt by recording again. Repeated flushes allocate for JSON serialization and UTF-8 encoding, so flush on a product-defined cadence rather than every frame.

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

Manual activation is also supported when the provider can defer scene entry:

```csharp
var sceneHandle = package.LoadSceneAsync(
    "Assets/Scenes/Gameplay.unity",
    LoadSceneMode.Single,
    SceneActivationMode.Manual);

// Wait for the provider-controlled preload phase.
await sceneHandle.Task;

// Complete the final activation step when your fade-out is finished.
await sceneHandle.ActivateAsync();
```

Notes:

- `SceneActivationState` is a normalized cross-provider status, not a promise that every provider exposes the same intermediate state.
- Addressables can report `WaitingForActivation` after load completion.
- YooAsset deferred entry is implemented by suspended loading, so the handle may remain in `Loading` until `ActivateAsync()` resumes and completes the load.

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

The asset management system uses a low-allocation, three-tier caching architecture (Active, Trial, and Main pools) to improve cache hit rates and keep idle-memory eviction deterministic. Hot cache paths avoid avoidable allocations where the current API shape permits; project-specific zero-GC claims must be verified with Unity Profiler or allocation tests.

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
> `ClearBucket` / `ClearBucketsByPrefix` only evict handles whose `RefCount` has already reached 0 (idle in Trial or Main pools). Active handles (still in use) are **never** evicted; this prevents dangling references by design.

#### Hierarchical Bucket Paths

Bucket names support a **hierarchical dot-separated** convention (e.g. `"UI.Scene.MainCity"`). Use `AssetBucketPath` to compose and match paths safely:

```csharp
using CycloneGames.AssetManagement.Runtime;

// Compose hierarchical bucket names with a stable dot-separated convention.
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

### Automatic Memory Management

Beyond entry-count limits, the cache enforces an **automatic, platform-aware idle memory budget**. Idle (`RefCount == 0`) handles are evicted once their estimated runtime footprint exceeds the budget — even when the entry count is within limits — so memory stays bounded under sustained load:

| Device profile (system RAM) | Idle budget |
| --------------------------- | ----------- |
| Desktop / console (>= 4 GB) | 512 MB      |
| Mid-range (>= 2 GB)         | 256 MB      |
| Low-end / WebGL             | 96 MB       |

The budget is derived automatically at startup — no configuration required. Footprint is measured with `Profiler.GetRuntimeMemorySizeLong` in the Editor / Development builds, and an allocation-free heuristic (texture / mesh / audio size) in release builds.

The cache also subscribes to **`Application.lowMemory`**: on an OS memory-pressure signal it immediately drops every idle handle (Active, in-use handles are never touched), giving the OS headroom before it terminates the app.

#### Overriding the Budget

The default is automatic, but a host project can override it **without modifying the package** — module-wide, per package, or at runtime. Precedence: per-package override > module default > automatic.

```csharp
// 1) Module-wide default — applies to every package this module creates.
await module.InitializeAsync(new AssetManagementOptions(
    defaultIdleMemoryBudgetBytes: 256L * 1024 * 1024));   // 256 MB for all packages

// 2) Per-package override at init (wins over the module default).
await package.InitializeAsync(new AssetPackageInitOptions(
    playMode, providerOptions,
    idleMemoryBudgetBytesOverride: 128L * 1024 * 1024));  // 128 MB

// 3) At runtime (any time after init) — e.g. tighten before a heavy scene, relax afterwards.
package.SetCacheIdleMemoryBudget(64L * 1024 * 1024);   // 64 MB
package.SetCacheIdleMemoryBudget(0);                   // 0 = restore platform-aware default
```

Setting a new budget immediately evicts idle handles to honor it (Active handles are never touched).

### Querying Cache State

Use the non-allocating `IsAssetCached<T>` to check residency before deciding to load:

```csharp
// True if the asset is currently resident (Active OR sitting in an idle Trial/Main pool).
if (package.IsAssetCached<GameObject>("Prefabs/Boss"))
{
    // LoadAssetAsync will be a cache hit — no bundle IO.
}
```

### Cache Retention Policy & Scheduler

The idle (`RefCount == 0`) cache is intentionally **policy-neutral**: by design it has no built-in timer and no hard-coded lifetime rule. Idle handles are reclaimed when an eviction trigger runs: entry-count limits, the idle memory budget, `Application.lowMemory`, explicit `ClearBucket` / `ClearBucketsByPrefix` / `UnloadUnusedAssetsAsync`, `DestroyAsync`, or a caller-provided retention policy.

This keeps the core cache deterministic and testable while still supporting product-specific retention: UI-heavy games, open-world streaming, headless simulations, tools, low-memory devices, and scene-boundary cleanup can all use different rules without forking the provider.

#### The mechanism hook: `TrimIdleCache`

```csharp
var policy = AssetCacheRetentionPolicy.IdleForAtLeast(TimeSpan.FromSeconds(60));
int evicted = package.TrimIdleCache(policy);
```

`TrimIdleCache` has no timer and no frame driver. You decide when to call it: a HUD button, a scene boundary, a low-memory event, a telemetry-driven memory governor, or the scheduler below.

#### Composing retention rules

Policies are built from `IAssetCacheRetentionRule` instances. Common rules are available through `AssetCacheRetentionRules`, and custom rules can inspect bucket, tag, owner, idle duration, access count, estimated bytes, and cache tier.

```csharp
// Global rule: evict anything idle for at least 120 seconds.
// Scene rule: evict Scene.Battle and its children after 30 seconds.
var policy = AssetCacheRetentionPolicy.MatchingAny(
    AssetCacheRetentionRules.IdleForAtLeast(TimeSpan.FromSeconds(120)),
    AssetCacheRetentionRules.All(
        AssetCacheRetentionRules.Bucket("Scene.Battle", includeChildren: true),
        AssetCacheRetentionRules.IdleForAtLeast(TimeSpan.FromSeconds(30))));

int evicted = package.TrimIdleCache(policy);
```

Use preserve rules when a global policy should not touch intentionally resident buckets:

```csharp
var policy = AssetCacheRetentionPolicy
    .IdleForAtLeast(TimeSpan.FromMinutes(5))
    .WithPreserveRules(AssetCacheRetentionRules.Bucket("UI.Persistent", includeChildren: true));
```

#### Option A: Drive it from code or DI

`AssetCacheRetentionScheduler` (assembly `CycloneGames.AssetManagement.Runtime.CacheRetention`) is a pure-C# `IDisposable` that applies an `AssetCacheRetentionPolicy` on a fixed real-time interval. It uses `UniTask`, owns an internal `CancellationTokenSource`, and does not throw into the host loop.

```csharp
using CycloneGames.AssetManagement.Runtime.CacheRetention;

var policy = AssetCacheRetentionPolicy.IdleForAtLeast(TimeSpan.FromSeconds(120));
var scheduler = new AssetCacheRetentionScheduler(package, policy, TimeSpan.FromSeconds(30));
scheduler.Start();

// Later, on shutdown:
scheduler.Dispose();

// The package can be resolved lazily, which is useful during DI wiring.
var lazy = new AssetCacheRetentionScheduler(
    () => AssetManagementLocator.DefaultPackage,
    policy,
    TimeSpan.FromSeconds(30));
```

#### Option B: Drive it from a scene object

For Inspector-driven setups, drop `AssetCacheRetentionBehaviour` on a persistent GameObject. It resolves its package from an explicit `Bind(package)` call, falling back to `AssetManagementLocator.DefaultPackage`.

| Field | Meaning | Default |
| --- | --- | --- |
| `MinimumIdleSeconds` | Idle age threshold; `0` evicts all matched idle handles | `120` |
| `CheckIntervalSeconds` | Seconds between passes (min 1) | `30` |
| `Bucket` | Optional exact bucket or bucket root to trim | Empty |
| `IncludeChildBuckets` | Include child buckets when `Bucket` is set | `true` |
| `LogEvictions` | Log the evicted count of each non-empty pass | `false` |
| `AutoStartFromLocator` | Start in `OnEnable` using the locator's default package | `true` |

#### Option C: Apply retention on scene transitions (Navigathena)

When the Navigathena integration is present (`CYCLONEGAMES_HAS_NAVIGATHENA`), pass `ApplyPackageCacheRetentionOperation` as the transition `interruptOperation`. It complements `UnloadPackageAssetsOperation`, which clears on-disk bundle files.

```csharp
var policy = new AssetCacheRetentionPolicy(
    AssetCacheRetentionRules.Bucket("Scene.Battle", includeChildren: true));

var trim = new ApplyPackageCacheRetentionOperation(package, policy);
await navigator.Change(sceneId, interruptOperation: trim);
```

#### Option D: Auto-register the scheduler with VContainer

The VContainer installer can register and lifetime-bind the scheduler for you. It is off by default.

```csharp
var policy = AssetCacheRetentionPolicy.IdleForAtLeast(TimeSpan.FromSeconds(120));
var installer = new AssetManagementVContainerInstaller(
    cacheRetentionOptions: new AssetCacheRetentionOptions(
        enabled: true,
        policy: policy,
        checkIntervalSeconds: 30));
installer.Install(builder);
```

### Bundle Loading Concurrency

Uncapped bundle concurrency causes IO thrash and memory spikes on constrained devices. When the concurrency knob is left unset, the system applies a **platform-aware default** (`AssetPlatformDefaults.BundleLoadingMaxConcurrency`): WebGL = 4, Android / iOS = 8, desktop / console = `clamp(cores x 2, 8, 32)`.

```csharp
// Module-level default (applies to packages that don't override it).
// int.MaxValue or any value <= 0 means "use the platform-aware default".
await module.InitializeAsync(new AssetManagementOptions(
    bundleLoadingMaxConcurrency: int.MaxValue));

// Per-package override (takes precedence over the module value).
await package.InitializeAsync(new AssetPackageInitOptions(
    playMode, providerOptions,
    bundleLoadingMaxConcurrencyOverride: 16));
```

### Resource Tracking & Metadata

To make runtime resource tracking easier, loading APIs accept `tag` and `owner` metadata parameters. Passing existing string constants or cached identifiers keeps this path allocation-free while enabling fine-grained tracking of exactly _who_ loaded an asset and _what_ it is used for.

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

### Provider Catalog Queries

Some providers expose catalog-side tags or labels that can be queried before loading assets. Use the optional `IAssetCatalogQuery` capability when a workflow needs to expand a provider tag into concrete load locations:

```csharp
if (package is IAssetCatalogQuery catalogQuery)
{
    var locations = new List<string>(64);
    if (await catalogQuery.TryGetAssetLocationsByTagAsync("UI", locations))
    {
        for (int i = 0; i < locations.Count; i++)
        {
            // Load or build a plan from locations[i].
        }
    }
}
```

This is a low-frequency planning API, not a gameplay hot-path API. YooAsset and Addressables can map provider catalog tags or labels to locations. The Resources provider has no runtime catalog tag system, so it does not implement this capability by default. Provider catalog tags are separate from the runtime `tag` metadata passed to load APIs for cache tracking and retention rules.

### Advanced Editor Debugging Tools

`CycloneGames.AssetManagement` provides developer-friendly editor windows to visualize cache health and identify memory leaks without guessing.

#### 1. Asset Cache Debugger Window (`Tools/CycloneGames/AssetManagement/Asset Cache Debugger`)

A comprehensive view of the entire W-TinyLFU cache.

- **Tier Visualization**: Instantly see if assets are Active, in Trial, or in the Main hot cache.
- **Resizable Table Columns**: Drag header separators to tune column widths for long locations, provider names, buckets, tags, and owners. Use **Reset Columns** to restore the session defaults.
- **Metadata Columns**: Inspect and filter by `Tag`, `Owner`, and `Bucket`.
- **Ref-count Anomalies**: Automatically highlights active assets with unusually high reference counts (> 8), warning you of potential missing `Dispose()` calls.
- **Memory Footprint**: The table shows a per-row estimated memory column, and the Summary tab reports the live idle-pool footprint versus the platform memory budget.
- **Selection and Copy Menus**: Single-click selects a row, Ctrl/Cmd-click toggles rows, and Shift-click selects a visible range. Right-click a row or header to copy individual fields, selected rows, a full row, TSV/JSON output, or all currently visible rows. Project asset locations can also be pinged from the context menu.
- **Stable View Scroll**: Cache tabs keep independent scroll positions during tab switches and start at the top the first time a tab is opened.
- **Summary Breakdowns**: Statistical distribution of assets by Provider, Tag, and Owner.

#### 2. Handle Tracker Window (`Tools/CycloneGames/AssetManagement/Asset Handle Tracker`)

A microscopic view of every active handle allocation, cross-referenced against the cache.

- **Smart Status Identification**: Classifies each long-lived handle as `Cached` (safely held in an idle pool), `Persistent` (developer-declared long-lived), or `Leaked` (genuinely unexplained).
- **Resizable Table Columns**: Drag header separators to align package, description, location, tag, owner, status, and lifetime columns for the current debugging session.
- **Location Column**: Extracts the asset location from the handle description when possible, making it easier to compare handles with cache rows and project assets.
- **Selection and Copy Menus**: Single-click selects a row, Ctrl/Cmd-click toggles rows, and Shift-click selects a visible range. Right-click a row or header to copy individual fields, selected rows, a full row, TSV/JSON output, stack traces, or all currently visible rows.
- **Persistent Marking**: Right-click any row -> **Mark Persistent** to silence false-positive leaks for intentionally long-lived assets (DontDestroyOnLoad, bootstrap UI, the main scene). See [Marking Persistent Handles](#marking-persistent-handles).
- **Stack Trace Expansion**: Right-click any row with a captured stack trace and choose **Expand Stack Trace** to inspect where it was allocated (enable **Stack Traces** in the toolbar first).

#### 3. Scene Tracker Window (`Tools/CycloneGames/AssetManagement/Scene Tracker`)

Live view of every tracked scene handle: provider, package, bucket, activation state (Loading / Waiting / Activated / Unload Pending / Error), load mode, activation mode, progress, refs, age, and latest error. Use it to catch scenes stuck in `WaitingForActivation`, pending unload, or failed provider operations.

- **Resizable Table Columns**: Drag header separators to tune scene, provider, package, bucket, state, activation, progress, refs, age, and error widths.
- **Selection and Copy Menus**: Supports the same single-click, Ctrl/Cmd-click, and Shift-click multi-select model as the cache and handle windows, with selected/visible TSV and JSON export.
- **Scene Asset Ping**: Right-click rows backed by `Assets/` or `Packages/` scene paths to ping the source scene asset.

#### 4. Runtime Governance Window (`Tools/CycloneGames/AssetManagement/Runtime Governance`)

A single dashboard combining handles, scenes, and cache tiers — overview metric cards, top buckets, longest-lived active handles, and a scene-lifecycle snapshot. The fastest place to assess overall asset health during stress tests.

> All four windows build throttled snapshots before repainting so normal editor repaint paths stay low-allocation and responsive in large projects. Use the Unity Profiler when validating allocation budgets for a specific editor layout.

#### Marking Persistent Handles

The leak heuristic flags any handle alive > 5 min that is not explained by the cache. For intentionally long-lived assets (DontDestroyOnLoad, bootstrap UI, main-scene infrastructure) this is a false positive. Declare them persistent so they show as `Persistent` instead of `Leaked`:

```csharp
using CycloneGames.AssetManagement.Runtime;

// At bootstrap, for permanently-resident assets:
HandleTracker.MarkPersistent("Assets/.../UIFramework.prefab");
HandleTracker.MarkPersistent("Assets/.../EventSystem.prefab");

// Remove the marking if an asset is no longer persistent:
HandleTracker.UnmarkPersistent("Assets/.../UIFramework.prefab");
```

> Right-click -> **Mark Persistent** in the window is convenient but **session-only** (it lives in runtime static state and resets on domain reload / play-stop). For permanent suppression, call `HandleTracker.MarkPersistent` from your bootstrap code.

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

`AssetRef<T>` and `SceneRef` solve all of these while staying lightweight at runtime. They are `struct` values that store only `location` and `guid`; they do not load assets or hold handles by themselves.

### Design Principles

| Principle                     | How                                                                                                                                                 |
| ----------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Pure data key**             | `AssetRef` stores `location` + `guid`. It never loads, never caches, never holds a handle.                                                          |
| **Low allocation**            | `struct`, not `class`; arrays and serialized owners still allocate normally, but each reference value does not allocate a separate object.           |
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
        const string sceneBucket = "Scene.BossArena";

        // Option A: Direct scene loading via IAssetPackage
        var sceneHandle = package.LoadSceneAsync(config.BossArena, bucket: sceneBucket);
        await sceneHandle.Task;

        // Option B: Navigathena scene navigation (push/pop/change)
        await navigator.Push(config.BossArena.ToSceneIdentifier(package, bucket: sceneBucket));
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
- **Stale locations**: Asset was moved/renamed but GUID is still valid. Drawers display a warning icon without mutating assets; `ValidateAll()` heals locations explicitly.

Use `AssetRefValidator.ValidateAllReportOnly()` for CI/reporting flows that must not modify project assets. Use `AssetRefValidator.ValidateAll()` when the build step is allowed to heal stale locations.

---

## API Reference

### IAssetModule

| Method                     | Description                            |
| -------------------------- | -------------------------------------- |
| `InitializeAsync(options)` | Initialize the asset system            |
| `DestroyAsync()`           | Cleanup and release resources deterministically |
| `CreatePackage(name)`      | Create a new asset package             |
| `GetPackage(name)`         | Get an existing package                |
| `RemovePackageAsync(name)` | Remove and destroy a package           |
| `CreatePatchService(name)` | Create a provider-backed patch service |

### IPatchService / IAssetPatchTransactionService

| Member | Description |
| --- | --- |
| `RunAsync(autoDownload, downloadOptions)` | Legacy event-driven patch flow. |
| `Download()` | Starts a pending download through the legacy fire-and-forget path. |
| `Cancel()` | Cancels the active provider downloader. |
| `RunAsync(PatchRunOptions)` | Transactional patch flow with result reporting and optional content trust verification. |
| `DownloadAsync()` | Completes a pending transaction and returns a `PatchRunResult`. |

### AssetPatchProfileAsset / AssetPatchRuntimeProfile

| Member | Description |
| --- | --- |
| `BuildRuntimeProfile()` | Builds a platform-resolved runtime patch profile from a Unity authoring asset. |
| `BuildRuntimeProfile(platform)` | Builds a runtime patch profile for a specific platform. |
| `CreateRunOptions(manifest, verifier, signatureVerifier, failureBuffer)` | Converts a runtime profile plus product-provided trust data into `PatchRunOptions`. |
| `AssetPatchRuntimeProfileBuilder` | Creates the same runtime profile without a Unity asset, suitable for DI/headless/server composition. |

### ContentTrustManifestBuilder / Codec

| Member | Description |
| --- | --- |
| `ContentTrustManifestBuilder.AddFile(root, location, algorithm)` | Adds a relative file entry and computes its content hash. |
| `ContentTrustManifestBuilder.Build()` | Produces a sorted provider-neutral trust manifest. |
| `ContentTrustManifestCodec.ToJson(manifest)` | Writes the manifest document for storage or transport. |
| `ContentTrustManifestCodec.FromJson(json)` | Reads a manifest document from JSON. |
| `ContentTrustManifestCodec.ToCanonicalPayloadBytes(manifest)` | Produces deterministic signing bytes that exclude the signature field. |
| `ContentTrustManifestSignatureUtility.Sign(manifest, signer)` | Signs canonical bytes through an injected product/platform signer. |

### IAssetRepairService

| Member | Description |
| --- | --- |
| `RepairAsync(manifest, failures, options)` | Builds a repair plan from content trust failures and executes location repair. |
| `RepairAsync(plan, options)` | Executes a precomputed provider-neutral repair plan. |
| `RepairEvents` | Reports stage changes, plan creation, download progress, completion, and failure. |

### IAssetPackage

| Method                         | Description                                                         |
| ------------------------------ | ------------------------------------------------------------------- |
| `InitializeAsync(options)`     | Initialize the package                                              |
| `DestroyAsync()`               | Destroy the package                                                 |
| `LoadAssetAsync<T>(...)`       | Load an asset asynchronously (supports `bucket`/`tag`/`owner`)      |
| `LoadAssetSync<T>(...)`        | Load an asset synchronously (supports `bucket`/`tag`/`owner`)       |
| `LoadAllAssetsAsync<T>(...)`   | Load all assets at location (supports `bucket`/`tag`/`owner`)       |
| `IsAssetCached<T>(location)`   | Non-allocating residency check (Active or idle Trial/Main pool)     |
| `InstantiateAsync(handle)`     | Instantiate a loaded prefab                                         |
| `InstantiateSync(handle)`      | Sync instantiate                                                    |
| `LoadSceneAsync(location)`     | Load a scene                                                        |
| `UnloadSceneAsync(handle)`     | Unload a scene                                                      |
| `LoadRawFileAsync(location)`   | Load a raw file                                                     |
| `UnloadUnusedAssetsAsync()`    | Global sweep of all unused assets                                   |
| `SetCacheIdleMemoryBudget(bytes)` | Override the idle memory budget at runtime (0 = restore auto)     |
| `TrimIdleCache(policy)` | Apply an idle cache retention policy; returns the evicted count |
| `ClearBucket(bucket)`          | Evict idle handles matching an exact bucket name                    |
| `ClearBucketsByPrefix(prefix)` | Evict idle handles matching a bucket prefix and all its descendants |

### HandleTracker (Diagnostics)

| Member | Description |
| --- | --- |
| `Enabled` | Master switch for handle tracking. |
| `EnableStackTrace` | Capture allocation stack traces (slower; enable when hunting leaks). |
| `MarkPersistent(location)` | Exclude an intentionally long-lived asset from leak heuristics. |
| `UnmarkPersistent(location)` / `ClearPersistent()` | Remove persistent markings. |
| `IsPersistent(location)` | Query whether a location is marked persistent. |

### IAssetRuntimeDiagnostics

| Member | Description |
| --- | --- |
| `GetRuntimeCacheSnapshot()` | Returns aggregate runtime cache counters without per-entry enumeration. |

### AssetRuntimeTelemetryRecorder

| Member | Description |
| --- | --- |
| `TryRecord(snapshot)` / `TryRecord(diagnostics)` | Records a sample when interval and zero-activity filters allow it. |
| `CopyTo(buffer)` | Copies the newest bounded window from oldest to newest into a caller-owned buffer. |
| `TryGetLatest(out sample)` | Reads the latest recorded sample without allocation. |
| `Clear()` | Clears all in-memory telemetry samples and resets sequence counters. |

### AssetRuntimeTelemetryFileSink

| Member | Description |
| --- | --- |
| `WriteJsonLinesAsync(path, recorder, samples, text, token)` | Atomically writes the recorder's current bounded window as JSON Lines using caller-owned scratch buffers. |

### Scripting Define Symbols

These symbols are automatically defined based on installed packages:

| Symbol                 | When Defined                      |
| ---------------------- | --------------------------------- |
| `CYCLONEGAMES_HAS_YOOASSET`     | YooAsset package is installed through UPM |
| `CYCLONEGAMES_HAS_ADDRESSABLES` | Addressables package is installed through UPM |
| `CYCLONEGAMES_HAS_VCONTAINER`   | VContainer package is installed through UPM |
| `CYCLONEGAMES_HAS_NAVIGATHENA`  | Navigathena package is installed through UPM |
| `CYCLONEGAMES_HAS_VCONTAINER_UNITASK` | UniTask is available in the VContainer integration assembly |

---

## Best Practices

1. **Always dispose handles** - Use `using` statements or call `Dispose()` manually
2. **Use async loading** - Sync loading blocks the main thread
3. **Choose the provider per product** - Resources for built-in prototypes, Addressables for projects already using Unity's catalog pipeline, YooAsset for projects that explicitly adopt its patch workflow
4. **Enable handle tracking in development** - Helps find memory leaks early
5. **Use DI containers** - Register `IAssetModule` as a singleton for clean architecture
6. **Mark persistent assets** - Declare DontDestroyOnLoad / bootstrap / main-scene assets via `HandleTracker.MarkPersistent` so the leak detector stays signal-rich
7. **Record bounded player telemetry** - Use `AssetRuntimeTelemetryRecorder` for packaged-build cache pressure records; choose an explicit flush cadence and storage path per product
