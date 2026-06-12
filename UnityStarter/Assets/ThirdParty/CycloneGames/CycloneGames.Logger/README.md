# CycloneGames.Logger

English | [简体中文](README.SCH.md)

High-performance, low/zero-GC logging for Unity and .NET, designed for stability and portability across platforms (Android, iOS, Windows, macOS, Linux, Web/WASM such as Unity WebGL).

## Features

- **Three-Tier Capacity Management**: Adaptive object pools with automatic expansion & contraction (Target/Peak/Max)
- **Zero-GC Logging**: Builder APIs and pooled objects eliminate allocations in hot paths
- **Cross-Platform**: Threaded worker or Single-threaded Pump processing strategies
- **Object Pool Monitoring**: Statistics API for development/debugging (Editor & Development builds only)
- **Flexible Filtering**: Category filtering (whitelist/blacklist) and severity levels
- **Unity Integration**: Console click-to-source formatting, auto-bootstrap
- **Optional FileLogger**: With maintenance/rotation capabilities

## Quick Start (Unity)

Out of the box, the default bootstrap runs before any scene loads:

- Auto-detects platform and selects processing strategy (WebGL -> Single-threaded; others -> Threaded)
- Registers UnityLogger by default (can be disabled via settings)

Start logging immediately:

```csharp
using CycloneGames.Logger;

void Start()
{
    CLogger.LogInfo("Hello from CycloneGames.Logger");
}
```

## Unity Console Integration

CLogger includes a clickable hyperlink feature for quick source navigation in the Unity Editor Console:

- **Single-click** on the hyperlink `(at Assets/.../File.cs:27)` to open the file at the exact line in your configured code editor
- The hyperlink is formatted to remain hidden in the Console's single-line preview, keeping the log list clean

<img src="./Documents~/Doc_01.png" alt="Hyperlink support in Unity Console" style="width: 100%; height: auto; max-width: 800px;" />

### Console Pro Users

If you use [Console Pro](https://assetstore.unity.com/packages/tools/utilities/console-pro-11889), we recommend enabling **single-line display mode** for a cleaner log list:

**Multi-line Mode:**

<img src="./Documents~/Doc_02.png" alt="Multi-line display" style="width: 100%; height: auto; max-width: 800px;" />

**Single-line Mode (Recommended):**

<img src="./Documents~/Doc_03.png" alt="Single-line display" style="width: 100%; height: auto; max-width: 800px;" />

> [!TIP]
> Single-line mode hides the source location hyperlink in the log list, reducing visual clutter while still allowing click-to-source navigation when you select a log entry.

## Object Pool Architecture

The logger employs **three-tier adaptive capacity management** for optimal zero-GC performance:

```
Target Capacity    <- Normal steady-state (128 for StringBuilder, 256 for LogMessage)
     | Auto-expand under load
Peak Capacity      <- Maximum during bursts (1024/4096) - Zero GC!
     | Async trim when exceeded
Max Capacity       <- Hard limit (2048/8192) - Prevents memory leaks
```

**Result**: 99.9% zero-GC operation while maintaining memory safety through automatic pool trimming.

## Configuration and Build Tutorial

CLogger has three configuration layers. New projects should start with `LoggerSettings`; CI can override that asset for a single build; advanced projects can still configure everything from code.

### Default runtime behavior

The built-in `LoggerBootstrap` runs before the first scene loads.

- If `Assets/Resources/CycloneGames.Logger/LoggerSettings.asset` exists, it is loaded automatically.
- If no `LoggerSettings` asset exists, `UnityLogger` is registered by default so `CLogger.LogInfo(...)` works in both Editor and Player builds.
- Player logging is not disabled by `DEVELOPMENT_BUILD`. Release output is controlled by `LoggerSettings`, command-line build overrides, or environment variables.
- WebGL uses single-threaded processing and skips `FileLogger`; other platforms use threaded processing by default.

### Create the project settings asset

Recommended setup for most projects:

1. Use `Tools -> CycloneGames -> Logger -> Create Default LoggerSettings`.
2. Confirm the generated asset is at `Assets/Resources/CycloneGames.Logger/LoggerSettings.asset`.
3. Do not rename `LoggerSettings.asset` or the `CycloneGames.Logger` folder. Runtime loading expects `Resources/CycloneGames.Logger/LoggerSettings`.

Important fields:

| Field | Purpose | Typical value |
|-------|---------|---------------|
| `processing` | Threading strategy | `AutoDetect` |
| `registerUnityLogger` | Send logs to `UnityEngine.Debug.*` / Unity Console | `true` in Editor/debug builds, `false` in low-end release builds |
| `registerFileLogger` | Write logs through `FileLogger` | `true` for Player diagnostics, `false` for WebGL |
| `defaultLevel` | Minimum severity accepted by CLogger | `Info` in development, `Warning` or `Error` in release |
| `overflowPolicy` | Queue behavior when logging bursts exceed capacity | `DropNewest` for frame stability |
| `guaranteedLevel` | Severity that should be preserved under pressure | `Error` |

### Recommended build profiles

Use these as starting points:

| Build type | UnityLogger | FileLogger | Level | Notes |
|------------|-------------|------------|-------|-------|
| Editor / local debug | On | Optional | `Info` | Best source navigation and iteration speed |
| QA / development Player | On | On | `Info` or `Warning` | Useful for testers; avoid very high-frequency logs |
| Low-end release Player | Off | On | `Warning` | Recommended for performance-sensitive platforms |
| Silent release Player | Off | Off | `Error` or any | Static CLogger calls become a cheap no-op when no default sinks exist |
| WebGL | On or Off | Off | `Warning` | File logging is skipped; call `Pump()` every frame if using single-threaded processing |

For high-frequency runtime diagnostics, prefer `FileLogger` or disabled/filtered logs. Do not stream thousands of messages per frame into Unity Console.

### Build and CI overrides

`CycloneGames.Logger.Editor` includes a build processor that reads the same Unity command line used by your build pipeline. It can temporarily override `Assets/Resources/CycloneGames.Logger/LoggerSettings.asset` for a build, then restore the project asset afterward.

This design keeps `Build.Pipeline.Editor` independent from `CycloneGames.Logger`: the Build module does not reference Logger assemblies and does not parse Logger-specific arguments. Logger owns its own build integration.

Common command-line overrides:

```text
-loggerMode File -loggerLevel Warning -loggerFileName Player.log
-loggerMode UnityAndFile -loggerLevel Info
-loggerMode Off
-loggerMode Settings
-loggerSettings Assets/Config/LoggerSettings.Release.asset
```

`-loggerMode` values:

| Value | Result |
|-------|--------|
| `Settings` | Use the project asset without applying mode overrides |
| `Off` | Disable both UnityLogger and FileLogger |
| `Unity` | Enable UnityLogger only |
| `File` | Enable FileLogger only |
| `UnityAndFile` | Enable both UnityLogger and FileLogger |

Example Unity batchmode command:

```text
Unity.exe -batchmode -quit ^
  -projectPath "E:/Work/GitRepo/unity_starter/UnityStarter" ^
  -executeMethod Build.Pipeline.Editor.BuildScript.PerformBuild_CI ^
  -buildTarget Android ^
  -output "Builds/Android/Game.apk" ^
  -loggerMode File ^
  -loggerLevel Warning ^
  -loggerFileName Player.log
```

The same pattern works for Windows, macOS, Linux, iOS, and other targets supported by the project build script.

### CI environment variables

Environment variables are useful when the CI system manages build options outside the Unity command line:

```text
CG_LOGGER_SETTINGS=Assets/Config/LoggerSettings.Release.asset
CG_LOGGER_MODE=File
CG_LOGGER_UNITY=false
CG_LOGGER_FILE=true
CG_LOGGER_USE_PERSISTENT_DATA_PATH=true
CG_LOGGER_FILE_NAME=Player.log
CG_LOGGER_CUSTOM_FILE_PATH=
CG_LOGGER_LEVEL=Warning
CG_LOGGER_FILTER=LogAll
CG_LOGGER_PROCESSING=AutoDetect
CG_LOGGER_MAX_QUEUED_MESSAGES=8192
CG_LOGGER_UNITY_CONSOLE_MAX_QUEUED_MESSAGES=2048
CG_LOGGER_SHUTDOWN_DRAIN_TIMEOUT_MS=1000
CG_LOGGER_OVERFLOW_POLICY=DropNewest
CG_LOGGER_GUARANTEED_LEVEL=Error
```

Priority order:

1. Command-line arguments override the same environment-variable options.
2. Environment variables override the project asset for that build.
3. `-loggerSettings` / `CG_LOGGER_SETTINGS` first loads a profile asset, then individual options such as `-loggerLevel` or `CG_LOGGER_FILE_NAME` override fields on top of that profile.
4. If no Logger build options are present, the build processor does nothing.

Avoid setting global machine-wide `CG_LOGGER_*` variables on shared build agents. Prefer job-scoped variables so one pipeline cannot accidentally affect another.

### What happens to temporary build assets

If CI overrides require a `LoggerSettings` asset and the project does not already have one, the build processor may create a temporary asset at the runtime loading path.

- If the asset existed before the build, its original JSON state is restored after the build.
- If the asset was created only for the build, it is deleted afterward.
- Auto-created `.meta` files, empty parent folders, and their `.meta` files are cleaned after AssetDatabase refresh.
- If Unity exits during a build, stale backup data is restored the next time the Editor domain loads.

### Programmatic configuration (advanced)

Call before the first use of `CLogger.Instance`:

```csharp
// Strategy
CLogger.ConfigureThreadedProcessing();            // Platforms with threads
// or
CLogger.ConfigureSingleThreadedProcessing();      // Web/WASM (requires Pump())

// Register sinks
CLogger.Instance.AddLoggerUnique(new UnityLogger());
var path = System.IO.Path.Combine(Application.persistentDataPath, "App.log");
CLogger.Instance.AddLoggerUnique(new FileLogger(path));

// Defaults
CLogger.Instance.SetLogLevel(LogLevel.Info);
CLogger.Instance.SetLogFilter(LogFilter.LogAll);
```

## Logging APIs

### String overloads (simple)

```csharp
CLogger.LogInfo("Connected", "Net");
CLogger.LogWarning("Low HP", "Gameplay");
```

### Builder overloads (low-GC)

```csharp
CLogger.LogDebug(sb => { sb.Append("PlayerId="); sb.Append(playerId); }, "Net");
CLogger.LogError(sb => { sb.Append("Err="); sb.Append(code); }, "Net");
```

> **Note**: If the lambda captures external variables (e.g., `playerId`), a closure object is allocated per call. For true zero-GC, use the stateful builder below.

### Stateful builder (zero-GC, recommended for hot paths)

```csharp
CLogger.LogInfo(player, static (p, sb) =>
    sb.Append("Player ").Append(p.name).Append(" HP: ").Append(p.hp), "Combat");
```

The `static` keyword prevents the compiler from capturing any outer variables, guaranteeing zero closure allocation.

## Log Assertions

Use `CLogAssert` for runtime invariant checks that should be reported through the same logging pipeline as normal logs.

`CLogAssert` is different from unit-test assertions:

- It is available in Runtime code.
- It can log only, throw only, or log and throw.
- It uses caller info, so failures still point to the call site.
- Successful assertions return before message builders run.
- It is controlled by runtime options, not by `DEVELOPMENT_BUILD`.

Basic usage:

```csharp
CLogAssert.IsTrue(player.IsAlive, "Player should be alive.", "Gameplay");
CLogAssert.IsNotNull(config, "Config must be loaded.", "Config");
CLogAssert.AreEqual(expectedState, currentState, "State mismatch.", "Net");
```

Hot-path friendly builder usage:

```csharp
CLogAssert.That(isValid, (entityId, systemName), static (state, sb) =>
{
    sb.Append("Invalid entity. EntityId=");
    sb.Append(state.entityId);
    sb.Append(", System=");
    sb.Append(state.systemName);
}, "Gameplay");
```

Configure behavior:

```csharp
CLogAssert.Configure(new CLogAssertOptions
{
    Enabled = true,
    FailureLevel = LogLevel.Error,
    FailureBehavior = CLogAssertFailureBehavior.LogOnly,
    Category = "Assert"
});
```

Available failure behaviors:

| Behavior | Result |
|----------|--------|
| `LogOnly` | Logs the failure and continues |
| `Throw` | Throws `CLogAssertionException` without logging |
| `LogAndThrow` | Logs first, then throws `CLogAssertionException` |

DI-friendly usage:

```csharp
ICLogger logger = CLoggerFactory.CreateSingleThreaded();
ICLogAssert logAssert = new CLogAssertService(logger, new CLogAssertOptions
{
    FailureLevel = LogLevel.Warning,
    FailureBehavior = CLogAssertFailureBehavior.LogOnly
});

logAssert.AreEqual(10, currentCount, "Unexpected count.", "Inventory");
```

Guidelines:

- Use `CLogAssert` for impossible states, invalid lifecycle order, and data integrity checks.
- Do not use it as a replacement for unit tests.
- Avoid throwing in public release builds unless the failure is truly unrecoverable.
- Do not log secrets or authentication data in assertion messages.
- For high-frequency checks, pass state into a `static` builder instead of using string interpolation.

## Object Pool Monitoring

Monitor pool health in Editor or Development builds:

```csharp
#if UNITY_EDITOR || DEVELOPMENT_BUILD
var sbStats = CycloneGames.Logger.Util.StringBuilderPool.GetStatistics();
var msgStats = LogMessagePool.GetStatistics();

Debug.Log($@"
StringBuilder Pool - Current: {sbStats.CurrentSize}, Peak: {sbStats.PeakSize}
  Hit Rate: {sbStats.HitRate:P}, Misses: {sbStats.TotalMisses}, Discard Rate: {sbStats.DiscardRate:P}

LogMessage Pool - Current: {msgStats.CurrentSize}, Peak: {msgStats.PeakSize}
  Hit Rate: {msgStats.HitRate:P}, Misses: {msgStats.TotalMisses}, Discard Rate: {msgStats.DiscardRate:P}
");
#endif
```

**Key Metrics**:

- **HitRate**: Should be ~100% (objects retrieved from pool vs newly allocated)
- **TotalMisses**: Number of times a `new` allocation was required (pool empty); should be ~0 when warm
- **PeakSize**: Maximum pool size reached (should stay well below Max capacity)
- **DiscardRate**: Should be ~0% for optimal performance
- **TrimCount**: Number of times pool auto-contracted (validates trim mechanism)

## WebGL and Pump()

- Web/WASM does not support background threads. The bootstrap selects Single-threaded mode and you should call Pump() regularly (e.g., once per frame):

```csharp
void Update()
{
    CLogger.Instance.Pump(4096); // bound per-frame work
}
```

- Pump() is a no-op in Threaded mode, so it is safe to call unconditionally in shared code.

## FileLogger setup and maintenance

Basic usage:

```csharp
var path = System.IO.Path.Combine(Application.persistentDataPath, "App.log");
CLogger.Instance.AddLoggerUnique(new FileLogger(path));
```

Rotation and warnings (optional):

```csharp
var options = new FileLoggerOptions
{
    MaintenanceMode = FileMaintenanceMode.Rotate, // or WarnOnly
    MaxFileBytes = 10 * 1024 * 1024,              // 10 MB
    MaxArchiveFiles = 5,                           // keep latest 5
    ArchiveTimestampFormat = "yyyyMMdd_HHmmss",
    FlushBatchSize = 64,                           // flush every N writes
    FlushIntervalMs = 1000                         // or every 1 second
};

var path = System.IO.Path.Combine(Application.persistentDataPath, "App.log");
CLogger.Instance.AddLoggerUnique(new FileLogger(path, options));
```

Flush strategy: writes are batched for I/O throughput. Error/Fatal messages are always flushed immediately regardless of batch settings.

Notes:

- Avoid FileLogger on WebGL (no filesystem). The bootstrap does not register it by default.
- On mobile/console, prefer persistentDataPath for write permission.

## Filtering

```csharp
CLogger.Instance.SetLogLevel(LogLevel.Warning);        // Show Warning and above
CLogger.Instance.SetLogFilter(LogFilter.LogAll);

// Whitelist / Blacklist
CLogger.Instance.AddToWhiteList("Gameplay");
CLogger.Instance.SetLogFilter(LogFilter.LogWhiteList);
```

## Packaging Checklist

Before shipping a Player build, verify these items:

- Decide whether the build should log at all. Use `-loggerMode Off` for silent builds.
- For low-end platforms, prefer `-loggerMode File -loggerLevel Warning` instead of Unity Console output.
- Keep `registerUnityLogger=false` for high-frequency release diagnostics unless the build is specifically intended for debugging.
- Keep `usePersistentDataPath=true` unless the platform owner has approved a custom writable path.
- Use `defaultLevel=Warning` or `Error` for release builds. Avoid `Trace` / `Debug` in public builds.
- Keep `overflowPolicy=DropNewest` and `guaranteedLevel=Error` when frame stability matters more than preserving every low-severity message.
- On WebGL, do not expect file logs. Use Unity console/browser diagnostics and call `Pump()` regularly.
- In CI, prefer job-scoped `CG_LOGGER_*` variables or explicit `-logger...` arguments. Avoid machine-global environment variables.

## Best practices

**Runtime performance:**

- Use the stateful generic builder API in hot paths:

```csharp
CLogger.LogInfo(playerId, static (id, sb) =>
{
    sb.Append("PlayerId=");
    sb.Append(id);
}, "Gameplay");
```

- Avoid string interpolation in hot paths, because the string is created before CLogger can filter the message.
- Avoid captured lambdas in hot paths. `static` lambdas prevent closure allocations.
- Do not send high-frequency logs to Unity Console; use filtering or `FileLogger`.
- Monitor `DiscardRate` during development; it should stay close to `0%` under normal load.
- Set an appropriate `LogLevel` before stress testing. Filtering is the cheapest optimization.

**Platform:**

- Tune Pump(maxItems) for single-threaded processing to fit frame budget
- Use centralized bootstrap (settings asset or code) to avoid duplicate registration
- Use `persistentDataPath` for Player file logs on mobile and console platforms
- Treat WebGL separately: no background worker, no normal file output

**Quality:**

- Use AddLoggerUnique for global sinks
- Use AddLogger for per-feature dedicated sinks (e.g., a benchmark file)
- In the Unity Editor, avoid adding ConsoleLogger alongside UnityLogger to prevent duplicate console entries
- Keep build-time Logger control in CI arguments/environment variables instead of modifying assets manually per build
- Keep Logger build integration inside the Logger module; Build pipeline code does not need a direct Logger dependency

## Samples

See `/Samples` folder for:

- **LoggerPoolMonitor**: Interactive pool statistics and burst testing
- **LoggerBenchmark**: Performance comparison with GC tracking
- **LoggerPerformanceTest**: High-volume stress testing
- **LoggerSample**: Basic usage example

## Troubleshooting

**Duplicate lines in Unity Console**:  
If both ConsoleLogger and UnityLogger are active in the Editor, the Editor may surface both stdout and Debug.Log. Skip ConsoleLogger in the Editor or keep only UnityLogger.

**No file output**:  
Ensure you added a FileLogger (it is not registered by default) and that the path is writeable.

**CI build produced unexpected logging behavior**:
Check for `-logger...` command-line arguments first, then job-scoped `CG_LOGGER_*` variables, then machine-global environment variables. Command-line arguments have the highest priority.

**LoggerSettings.asset appeared after a build**:
If it was created only for a build override, the build processor should restore/delete it after the build. If Unity was killed during build, reopen the Editor once so stale backup restoration can run.

**Release build is slower than expected**:
Make sure `UnityLogger` is disabled for high-frequency logs. `CLogger + UnityLogger` still calls `UnityEngine.Debug.*`, so Unity Console / player log output cost dominates.

**High DiscardRate in pool statistics**:  
Consider increasing PeakPoolSize in pool source code, or reduce log frequency.

**Memory growth over time**:  
Verify TrimCount > 0 in statistics. Pools should auto-trim after bursts.
