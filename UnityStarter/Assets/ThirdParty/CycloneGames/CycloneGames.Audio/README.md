# CycloneGames.Audio

<div align="left">English | <a href="./README.SCH.md">简体中文</a></div>

An enhanced audio management system for Unity. The core logic is sourced from Microsoft's `Audio-Manager-for-Unity`, extended by CycloneGames with a strong focus on performance, memory efficiency, and production-grade robustness.

If you do not plan to use mature middleware such as **Wwise**, **CriWare**, or **FMOD**, this plugin is highly recommended. Its logic for managing and editing audio is similar to **Wwise**, including common Wwise-like features such as **Bank, RTPC, Parameter, and Multi-Bus**, making it more suitable for developers and designers familiar with **Wwise**.

**Upstream Source**: https://github.com/microsoft/Audio-Manager-for-Unity

This version introduces critical optimizations for production environments, including performance monitoring, asynchronous resource loading, reduced GC overhead, DI-compatible architecture, safe asset lifecycle management, and cross-platform support including consoles.

## Features

- **AudioGraph Redrawing**: A more intuitive and visually appealing AudioGraph editing interface, featuring Unreal Engine-like shortcuts (e.g., Alt + mouse click on connections).
- **Centralized Audio Control**: Manage sound effects and music from a unified API.
- **DI & Non-DI Compatible**: Full `IAudioService` interface for DI containers (VContainer, Zenject, etc.), plus static `AudioManager` methods for direct access.
- **Smart Audio Pool**: Intelligent AudioSource pool with device-adaptive sizing, auto-expansion, voice stealing, and intelligent shrinking.
- **Safe Asset Lifecycle**: `UnloadBank` guarantees zero dangling `AudioSource.clip` references, with an `OnBankUnloaded` event hook for external asset management integration.
- **Cross-Platform**: Windows, macOS, Linux, Android, iOS, WebGL, and consoles (Switch, PS4/PS5, Xbox) with platform-adaptive pool sizing.
- **Performance Monitoring**: In-built hooks and utilities to monitor audio system performance in real-time.
- **Asynchronous Loading**: Integrates `UniTask` for non-blocking, asynchronous loading of audio assets, ensuring smooth gameplay without hitches.
- **GC Optimization**: Zero-allocation hot paths using fixed-size arrays, struct-based data, and object pooling. No `List<>` or LINQ in update loops.
- **Thread Safety**: Lock-free `ConcurrentQueue` command dispatch; off-thread calls are automatically deferred to the main thread.
- **Mobile Optimized**: Automatic audio pause/resume on application focus loss, battery-friendly update throttling on mobile platforms.

## Installation & Dependencies

- Unity: `2022.3`+
- Dependencies:
  - `com.cysharp.unitask` ≥ `2.0.0`

Install via UPM or place the package under `Packages`/`Assets`. See `package.json` in this folder for details.

## Editor Preview

- <img src="./Documents~/Preview_01.png" alt="Preview 1" style="width: 100%; height: auto; max-width: 800px;" />
- <img src="./Documents~/Preview_02.png" alt="Preview 2" style="width: 100%; height: auto; max-width: 800px;" />
- <img src="./Documents~/Preview_03.png" alt="Preview 3" style="width: 100%; height: auto; max-width: 800px;" />

## Quick Start

### 0) Creating AudioEvent Assets

Before you can play audio, you need to create AudioEvent assets in Unity:

1. Right-click in your Project window
2. Select **Create > CycloneGames > Audio > Audio Bank**
3. Configure the internal logic of your AudioEvent using AudioFile nodes and other audio components

### 1) Playing a Sound Effect (SFX)

```csharp
using CycloneGames.Audio.Runtime;
using UnityEngine;

public class AudioExample : MonoBehaviour
{
    [SerializeField] private AudioEvent jumpEvent;
    [SerializeField] private AudioEvent machineGunEvent;

    void Start()
    {
        // Play a one-shot sound effect
        AudioManager.PlayEvent(jumpEvent, gameObject);

        // Play a sound and get a handle to control it later
        var audioHandle = AudioManager.PlayEvent(machineGunEvent, gameObject);

        // Stop the looping sound after 5 seconds
        StartCoroutine(StopAfterDelay(audioHandle, 5f));
    }

    private System.Collections.IEnumerator StopAfterDelay(ActiveEvent audioHandle, float delay)
    {
        yield return new WaitForSeconds(delay);
        audioHandle?.Stop();
    }
}
```

### 2) Playing Music

```csharp
using CycloneGames.Audio.Runtime;
using UnityEngine;

public class MusicController : MonoBehaviour
{
    [SerializeField] private AudioEvent backgroundMusic;

    void Start()
    {
        AudioManager.PlayEvent(backgroundMusic, gameObject);
    }

    public void StopMusic()
    {
        AudioManager.StopAll(backgroundMusic);
    }
}
```

### 3) Using with Dependency Injection (DI)

```csharp
using CycloneGames.Audio.Runtime;
using UnityEngine;

public class AudioConsumer
{
    private readonly IAudioService _audio;

    // Inject via constructor (VContainer, Zenject, etc.)
    public AudioConsumer(IAudioService audio)
    {
        _audio = audio;
    }

    public void PlayJump(GameObject emitter, AudioEvent jumpEvent)
    {
        _audio.PlayEvent(jumpEvent, emitter);
    }

    public void SetMusicVolume(float volumeDb)
    {
        _audio.SetMixerVolume("MusicVolume", volumeDb);
    }
}
```

For DI containers, register `AudioManager` as `IAudioService`:

```csharp
// VContainer example
builder.RegisterComponentInHierarchy<AudioManager>().As<IAudioService>();

// Or register an externally created instance
AudioManager.SetInstance(myAudioManagerInstance);
```

### 4) Bank Loading & Unloading

```csharp
using CycloneGames.Audio.Runtime;
using UnityEngine;

public class BankExample : MonoBehaviour
{
    [SerializeField] private AudioBank sfxBank;

    void Start()
    {
        // Load bank — registers events for string-based lookup
        AudioManager.LoadBank(sfxBank);

        // Play by name
        AudioManager.PlayEvent("Jump_SFX", gameObject);
    }

    void OnDestroy()
    {
        // Unload bank — stops all playing events, clears all clip references,
        // then fires OnBankUnloaded for external asset managers
        AudioManager.UnloadBank(sfxBank);
    }
}
```

## API Reference

### IAudioService Interface

The `IAudioService` interface provides full audio system access for DI environments:

| Method                                                     | Description                                                            |
| ---------------------------------------------------------- | ---------------------------------------------------------------------- |
| `PlayEvent(AudioEvent, GameObject)`                        | Play an event attached to a GameObject for 3D spatial tracking         |
| `PlayEvent(AudioEvent, Vector3)`                           | Play an event at a fixed world position                                |
| `PlayEvent(string, GameObject)`                            | Play a named event (registered via `LoadBank`) on a GameObject         |
| `PlayEvent(string, Vector3)`                               | Play a named event at a fixed world position                           |
| `PlayEventScheduled(AudioEvent, GameObject, double)`       | Schedule playback at a precise DSP time (sample-accurate sync)         |
| `StopAll(AudioEvent)`                                      | Stop all instances of an event with configured fade-out                |
| `StopAll(string)`                                          | Stop all instances matching a registered event name                    |
| `StopAll(int)`                                             | Stop all events in a group (mutually exclusive playback)               |
| `PauseAll()` / `ResumeAll()`                               | Pause / resume all playing events                                      |
| `PauseEvent(ActiveEvent)` / `ResumeEvent(ActiveEvent)`     | Pause / resume a single event                                          |
| `IsEventPlaying(string)`                                   | Check if any instance of a named event is playing                      |
| `SetGlobalVolume(float)` / `GetGlobalVolume()`             | Master volume via `AudioListener.volume` (0–1, post-mixer)             |
| `SetMixerVolume(string, float)` / `GetMixerVolume(string)` | Per-bus volume via AudioMixer exposed parameters (dB)                  |
| `LoadBank(AudioBank, bool)`                                | Load a bank and register its events for name-based lookup              |
| `UnloadBank(AudioBank)`                                    | Unload bank, stop events, clear clip references, fire `OnBankUnloaded` |

#### Events

| Event            | Description                                                                                                                                                                   |
| ---------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `OnBankUnloaded` | Invoked after a bank is fully unloaded and all `AudioSource.clip` references are cleared. External asset management systems should subscribe to safely release asset handles. |

### AudioManager Static Methods

All `IAudioService` methods are also available as static methods on `AudioManager`:

- `PlayEvent(AudioEvent, GameObject)` / `PlayEvent(AudioEvent, Vector3)` — Play events
- `PlayEvent(string, GameObject)` / `PlayEvent(string, Vector3)` — Play by name
- `StopAll(AudioEvent)` / `StopAll(string)` / `StopAll(int)` — Stop events
- `PauseAll()` / `ResumeAll()` — Global pause/resume
- `SetGlobalVolume(float)` / `GetGlobalVolume()` — Master volume
- `LoadBank(AudioBank, bool)` / `UnloadBank(AudioBank)` — Bank management
- `IsEventPlaying(string)` — Query playback state
- `SetInstance(AudioManager)` — Register an externally-created instance for DI
- `ValidateManager()` — Ensure the AudioManager singleton exists

### ActiveEvent Methods

- `Stop()` — Stop the event with fade-out
- `StopImmediate()` — Stop the event immediately
- `Pause()` / `Resume()` — Pause/resume individual event
- `SetMute(bool)` — Mute/unmute the event
- `SetSolo(bool)` — Solo/unsolo the event
- `IsPaused` — Check if the event is currently paused
- `EstimatedRemainingTime` — Approximate time until playback completes

### AudioHandle (Struct)

A lightweight, safe handle for referencing an `ActiveEvent` without holding a strong reference:

- `IsValid` — Check if the referenced event is still alive and playing
- `Stop()` / `StopImmediate()` — Control playback through the handle
- `EstimatedRemainingTime` / `IsPlaying` — Query state

## External Asset Management Integration

`UnloadBank` is designed for safe integration with external asset management systems such as **CycloneGames.AssetManagement**, **Addressables**, or **Resources**.

When `UnloadBank` is called:

1. All active events from the bank are **stopped immediately**
2. All `AudioSource.clip` references are **set to null** (releasing the clip reference)
3. Bank events are **removed from the name registry**
4. `OnBankUnloaded` event is **fired** — external systems can subscribe to release asset handles

This guarantees that after `UnloadBank` returns, the audio system holds **zero references** to the bank's `AudioClip` assets.

### Integration with CycloneGames.AssetManagement

```csharp
using CycloneGames.Audio.Runtime;
using CycloneGames.AssetManagement.Runtime;

public class AudioAssetBridge
{
    private readonly IAssetModule _assets;
    private readonly IAudioService _audio;
    private readonly Dictionary<AudioBank, IAssetHandle<AudioBank>> _bankHandles = new();

    public AudioAssetBridge(IAssetModule assets, IAudioService audio)
    {
        _assets = assets;
        _audio = audio;

        // Subscribe once — when the audio system finishes unloading a bank,
        // release the underlying asset handle
        _audio.OnBankUnloaded += OnBankUnloaded;
    }

    public async UniTask LoadBankAsync(string bankAddress)
    {
        var handle = await _assets.LoadAssetAsync<AudioBank>(bankAddress);
        var bank = handle.Asset;
        _bankHandles[bank] = handle;
        _audio.LoadBank(bank);
    }

    public void UnloadBank(AudioBank bank)
    {
        // This stops all events, clears all clip references, then fires OnBankUnloaded
        _audio.UnloadBank(bank);
    }

    private void OnBankUnloaded(AudioBank bank)
    {
        // Safe to release — audio system holds zero clip references
        if (_bankHandles.TryGetValue(bank, out var handle))
        {
            handle.Release();
            _bankHandles.Remove(bank);
        }
    }
}
```

### Integration with Addressables (Direct)

```csharp
using CycloneGames.Audio.Runtime;
using UnityEngine.AddressableAssets;

public class AddressablesAudioBridge
{
    private readonly Dictionary<AudioBank, AsyncOperationHandle<AudioBank>> _handles = new();

    public void Initialize()
    {
        AudioManager.OnBankUnloaded += bank =>
        {
            if (_handles.TryGetValue(bank, out var handle))
            {
                Addressables.Release(handle);
                _handles.Remove(bank);
            }
        };
    }

    public async UniTask LoadBankAsync(string address)
    {
        var handle = Addressables.LoadAssetAsync<AudioBank>(address);
        var bank = await handle.Task;
        _handles[bank] = handle;
        AudioManager.LoadBank(bank);
    }

    public void UnloadBank(AudioBank bank)
    {
        AudioManager.UnloadBank(bank); // OnBankUnloaded fires automatically
    }
}
```

## CycloneGames Extensions

This implementation significantly builds upon the original Microsoft audio manager. The key enhancements are:

### AudioGraph Redrawing with Unreal Engine-like Shortcuts

The AudioGraph has been redrawn to enhance the rendering of node connection curves. It now includes shortcuts similar to those in Unreal Engine, such as using `Alt + Left Mouse Button` to delete a single curve or all curves connected to the selected node.

### DI-Compatible Architecture

The system exposes a clean `IAudioService` interface, enabling seamless integration with any DI container. For non-DI projects, all functionality remains accessible via static methods on `AudioManager`. The `SetInstance()` method allows external code to register a pre-existing `AudioManager` as the singleton.

### AudioClipReference and External Loaders

`AudioClipReference` supports multiple source styles:

- `FilePath`
- `StreamingAssetsPath`
- `PersistentDataPath`
- `Url`
- `AssetAddress`

The important distinction is:

- Path / URL modes can be loaded directly by the audio system
- `AssetAddress` is treated as a logical key, not as a local file path
- For `AssetAddress`, you should register a runtime loader

#### Register a Loader for One Reference

```csharp
AudioClipResolver.RegisterManagedReferenceLoader(audioClipReference, async (clipRef, ct) =>
{
    var handle = await myAssetSystem.LoadAudioClipAsync(clipRef.Location, ct);
    if (handle == null || handle.Asset == null)
        return default;

    return new ManagedAudioClipLoadResult(
        handle.Asset,
        () => handle.Release());
});
```

#### Register a Loader for an Entire Location Kind

This is the recommended setup for `AssetAddress`, YooAsset, Addressables, or a custom runtime package system.

```csharp
AudioClipResolver.RegisterManagedLocationKindLoader(AudioLocationKind.AssetAddress, async (clipRef, ct) =>
{
    var handle = await myAssetSystem.LoadAudioClipAsync(clipRef.Location, ct);
    if (handle == null || handle.Asset == null)
        return default;

    return new ManagedAudioClipLoadResult(
        handle.Asset,
        () => handle.Release());
});
```

#### Lifecycle Guarantee

When an external loader returns an `IAudioClipHandle`, the audio system will call `Release()` automatically when the clip is no longer used:

- event stop
- event recycle
- bank unload
- async cancellation cleanup

This means:

- the audio system decides **when** a clip is no longer referenced
- your asset system decides **how** the underlying resource handle is actually released

This split is what keeps external resource lifetimes safe without forcing `CycloneGames.Audio` to own every loading backend directly.

#### External Clip Lifecycle Validation Checklist

Recommended verification scenarios for projects that integrate `AudioClipReference` with Addressables, YooAsset, or a custom asset system:

- Start playback, then unload the owning bank and confirm the external release callback runs exactly once
- Start async loading, stop the event before completion, and confirm no dangling handle remains after cleanup
- Play the same `AudioClipReference` from multiple instances and confirm the underlying resource is released only after the last instance finishes
- Force the external loader to fail and confirm cache stats record the failure without leaking a partially initialized handle
- Clear or reload the audio system in Play Mode and confirm registered loaders and cached external clips are released safely

### Category Defaults and Voice Policy Overrides

`AudioEvent` now supports a lightweight two-layer voice policy model:

- `Category` selects a high-level runtime intent
- `Use Category Defaults` applies a built-in voice policy template for that category
- Per-event overrides are only needed for special cases

This keeps the common workflow simple. In most projects, designers only need to set the category:

- `CriticalUI`
- `GameplaySFX`
- `Voice`
- `Ambient`
- `Music`

When `Use Category Defaults` is enabled, the event resolves its voice policy automatically. The current built-in defaults are:

| Category | Steal Resistance | Budget Weight | Allow Voice Steal | Allow Distance Steal | Protect Scheduled |
| --- | ---: | ---: | :---: | :---: | :---: |
| `CriticalUI` | `2.2` | `1.5` | `false` | `false` | `true` |
| `GameplaySFX` | `1.0` | `1.0` | `true` | `true` | `true` |
| `Voice` | `1.5` | `1.35` | `true` | `false` | `true` |
| `Ambient` | `0.7` | `0.7` | `true` | `true` | `false` |
| `Music` | `2.6` | `1.8` | `false` | `false` | `true` |

Recommended usage:

- Set BGM and long-lived music layers to `Music`
- Set dialog, narration, and subtitle-driven speech to `Voice`
- Set far-field loops and background environmental beds to `Ambient`
- Keep most one-shot gameplay sounds on `GameplaySFX`
- Reserve `CriticalUI` for menu confirms, warnings, timing cues, or other must-hear feedback

Disable `Use Category Defaults` only when a specific event needs custom behavior beyond its category template.

### Thread-Safe Command Dispatch

All public API methods are thread-safe. Calls from worker threads are dispatched to the main thread through a structured command queue backed by a fixed-capacity ring buffer, reducing hot-path GC pressure and keeping command handoff predictable under load.

### Safe Asset Lifecycle Management

`UnloadBank` performs immediate cleanup: stops all active events, clears every `AudioSource.clip` reference, and fires the `OnBankUnloaded` event. This guarantees that external asset management systems can safely release underlying assets without risking dangling references or use-after-free.

### Asynchronous Operations

All resource-intensive operations, such as loading `AudioClip`s, are performed asynchronously using `UniTask`. This prevents the main thread from blocking, which is essential for eliminating frame rate drops when new sounds are introduced during gameplay.

### GC Optimizations

The audio system uses fixed-size arrays (`EventSource[8]`, `ActiveParameter[8]`) instead of `List<>`, struct-based `EventSource` and `AudioHandle` for stack allocation, object pooling for `ActiveEvent` instances, and O(1) swap-remove for the active events list. Hot-path `Debug.Log` calls are wrapped in `#if UNITY_EDITOR || DEVELOPMENT_BUILD`.

### Cross-Platform Support

Platform-adaptive pool sizing for WebGL, Android/iOS, Desktop, and consoles (Nintendo Switch, PS4/PS5, Xbox One/Series). Mobile platforms feature automatic audio pause/resume on application focus loss and battery-friendly update throttling.

### Performance Monitoring

The system is instrumented to provide memory monitoring data for the **AudioManager**, allowing developers to quickly diagnose audio-related performance issues.

### Domain Reload Safety

Static state is properly reset via `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`, ensuring correct behavior when Unity's "Enter Play Mode Options" (domain reload skip) is enabled.

### Smart Audio Pool Management

The audio system features an intelligent AudioSource pool that automatically adapts to different devices and manages resources efficiently.

#### Key Features

| Feature                    | Description                                                                                     |
| -------------------------- | ----------------------------------------------------------------------------------------------- |
| **Device-Adaptive Sizing** | Pool size automatically adjusts based on platform (WebGL/Mobile/Desktop/Console) and device RAM |
| **Auto-Expansion**         | Pool grows dynamically when more sources are needed                                             |
| **Voice Stealing**         | When pool is full, oldest non-looping sound is stopped to free resources                        |
| **Intelligent Shrinking**  | Unused sources are gradually released during idle periods                                       |
| **Zero GC Allocations**    | AudioSources are never created outside the pool, preventing memory leaks                        |

#### Default Pool Sizes

| Platform          | Condition  | Initial | Max |
| ----------------- | ---------- | ------- | --- |
| WebGL             | Always     | 16      | 32  |
| Mobile            | RAM < 3GB  | 32      | 48  |
| Mobile            | RAM 3-6GB  | 32      | 64  |
| Mobile            | RAM > 6GB  | 32      | 96  |
| Desktop           | RAM < 8GB  | 80      | 128 |
| Desktop           | RAM 8-16GB | 80      | 192 |
| Desktop           | RAM > 16GB | 80      | 256 |
| Switch            | Always     | 32      | 64  |
| Console (PS/Xbox) | Always     | 64      | 192 |

#### Custom Configuration (Optional)

By default, the system uses optimal values for your device. To customize:

1. Create a config asset: **Create → CycloneGames → Audio → Audio Pool Config**
2. Place it anywhere in your `Assets` folder
3. Adjust values in the Inspector

<img src="./Documents~/Preview_04.png" alt="Preview 4" style="width: 100%; height: auto; max-width: 400px;" />

> [!NOTE]
>
> - In the **Editor**, the config is auto-discovered from anywhere in the project.
> - For **builds**, place the config in a `Resources` folder for auto-discovery, or it will use default values.
> - Only one `AudioPoolConfig` should exist in the project.

#### Hot-Update Support

For projects using asset management systems like YooAsset or Addressables:

**Option 1: Before AudioManager Initializes** (Recommended)

```csharp
// In bootstrap scene, before AudioManager initializes
var handle = YooAssets.LoadAssetAsync<AudioPoolConfig>("AudioPoolConfig");
await handle.Task;
AudioPoolConfig.SetConfig(handle.AssetObject as AudioPoolConfig);
// AudioManager will automatically use this config when it initializes
```

**Option 2: After AudioManager Initializes**

```csharp
// Load and apply config at runtime
var handle = YooAssets.LoadAssetAsync<AudioPoolConfig>("AudioPoolConfig");
await handle.Task;
AudioPoolConfig.SetConfig(handle.AssetObject as AudioPoolConfig);

// Apply the new config to the running AudioManager
AudioManager.ReloadPoolConfig();
```

> [!NOTE]
> `ReloadPoolConfig()` updates pool size limits but preserves existing AudioSources.

#### Runtime Monitoring

Access pool statistics at runtime:

```csharp
// Check pool status
Debug.Log($"Pool: {AudioManager.PoolStats.InUse}/{AudioManager.PoolStats.CurrentSize}");
Debug.Log($"Max: {AudioManager.PoolStats.MaxSize}");
Debug.Log($"Device Tier: {AudioManager.PoolStats.DeviceTier}");

// Performance metrics
Debug.Log($"Peak Usage: {AudioManager.PoolStats.PeakUsage}");
Debug.Log($"Expansions: {AudioManager.PoolStats.TotalExpansions}");
Debug.Log($"Voice Steals: {AudioManager.PoolStats.TotalSteals}");
```

The AudioManager Inspector also displays real-time pool statistics during Play Mode.
