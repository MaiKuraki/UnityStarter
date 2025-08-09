# CycloneGames.Logger Samples

This package demonstrates high-throughput, low/zero-GC logging across platforms, including environments without background threads (e.g., Web/WASM such as Unity WebGL).

## Processing Strategies

The logger decouples message processing via a strategy interface. Two strategies are provided:

- ThreadedLogProcessor: Uses a background thread and a BlockingCollection to process log messages asynchronously. Best throughput on platforms that support threads.
- SingleThreadLogProcessor: Uses a lock-free concurrent queue and requires manual Pump() calls to process messages on the main thread. Use on platforms without threads (e.g., WebGL/WASM).

You can select the strategy before first use of CLogger.Instance:

```csharp
// Web/WASM (e.g., WebGL)
CLogger.ConfigureSingleThreadedProcessing();

// Platforms with threads
CLogger.ConfigureThreadedProcessing();
```

If no configuration is provided, the logger will attempt to create a threaded processor and automatically fall back to single-threaded processing if threads are unavailable.

## Pump() and Frame Budget

With SingleThreadLogProcessor, call Pump() periodically (e.g., once per frame) to drain the queue:

```csharp
// Drain up to 4096 messages this frame
CLogger.Instance.Pump(4096);
```

- Pump() is a no-op for ThreadedLogProcessor, so it is safe to call unconditionally in shared code (portable pattern).
- The maxItems parameter lets you bound the cost per frame and avoid long stalls under heavy log bursts.

## Zero/Low-GC Logging

When logging in tight loops or hot paths, prefer the builder overloads to avoid temporary string allocations when logging is disabled or to minimize GC:

```csharp
CLogger.LogInfo(sb => { sb.Append("HP="); sb.Append(hp); }, "Gameplay");
CLogger.LogError(sb => { sb.Append("Err="); sb.Append(code); }, "Net");
```

## Unity Console Click-Through

`UnityLogger` formats messages to include `(at Assets/Path/File.cs:Line)` for click-to-source functionality. Paths are normalized without extra string allocations.

## Recommended Patterns

- Always configure the processing strategy at app initialization. You can centralize setup by adding a bootstrap like `LoggerBootstrap` that runs before any scene loads.
- Call `CLogger.Instance.Pump()` from an Update tick that is always active. Keep `maxItems` tuned to your frame budget.
- Use category filters and log levels to avoid building messages that will be discarded.
- Prefer builder overloads in hot code paths to minimize GC.

## Centralized Setup (Unity)

To avoid repeating configuration in every scene or script, you can either:

- Use the built-in bootstrap in Runtime that reads `Resources/CycloneGames.Logger/LoggerSettings` (recommended for most projects). Create a `LoggerSettings` asset under that path to override defaults.
- Or include a single bootstrap class of your own (example below) if you need full control at code level.

```csharp
// Runs before any scene loads; configure once per app.
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
static void Initialize()
{
    #if UNITY_WEBGL && !UNITY_EDITOR
    CLogger.ConfigureSingleThreadedProcessing();
    #else
    CLogger.ConfigureThreadedProcessing();
    #endif

    CLogger.Instance.AddLoggerUnique(new UnityLogger());

    #if !UNITY_WEBGL || UNITY_EDITOR
    var path = System.IO.Path.Combine(Application.persistentDataPath, "App.log");
    CLogger.Instance.AddLoggerUnique(new FileLogger(path));
    #endif

    CLogger.Instance.SetLogLevel(LogLevel.Info);
    CLogger.Instance.SetLogFilter(LogFilter.LogAll);
}
```

With a centralized bootstrap in place, per-script registration blocks in the sample MonoBehaviours are only for demonstration and can be removed in production. Keep `Pump()` in a global update tick for single-threaded processing (it's a no-op with threaded processing).

## Using LoggerSettings (optional)

You do not need to create any settings for the logger to work. If no `LoggerSettings` asset is found, the default bootstrap uses auto-detection (WebGL -> single-thread processing, others -> threaded), registers `UnityLogger`, and sets `LogLevel=Info`, `LogFilter=LogAll`.

To override defaults via a project asset:

1. Create the asset via Unity menu: `Assets -> Create -> CycloneGames -> Logger -> LoggerSettings`.
2. Move the created asset under `Assets/Resources/CycloneGames.Logger/LoggerSettings.asset`.
   Important: do not rename the asset file or its parent folder when relying on the default bootstrap path `Resources/CycloneGames.Logger/LoggerSettings`.
3. Edit fields:
   - Processing: AutoDetect / ForceThreaded / ForceSingleThread
   - Registration: enable/disable `UnityLogger` and `FileLogger`
   - File Logger: choose `persistentDataPath` or custom file path
   - Defaults: set `LogLevel` and `LogFilter`

This asset will be loaded automatically by the default bootstrap at startup.
