> [!NOTE]
> The README and some of the code were co-authored by AI.

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

## Centralized Configuration

Configure globally either via a project asset or via code.

### Using LoggerSettings (recommended)

1) Create the asset: `Assets -> Create -> CycloneGames -> Logger -> LoggerSettings`
2) Place it at: `Assets/Resources/CycloneGames.Logger/LoggerSettings.asset`
   Important: Do not rename the asset file or its parent folder. The loader expects `Resources/CycloneGames.Logger/LoggerSettings`.
3) Edit fields:
   - Processing: AutoDetect / ForceThreaded / ForceSingleThread
   - Registration: enable/disable UnityLogger, FileLogger
   - File Logger: choose persistentDataPath or custom file path/name
   - Defaults: LogLevel and LogFilter

The bootstrap loads this asset automatically at startup.

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

### Builder overloads (zero-GC)

```csharp
CLogger.LogDebug(sb => { sb.Append("PlayerId="); sb.Append(playerId); }, "Net");
CLogger.LogError(sb => { sb.Append("Err="); sb.Append(code); }, "Net");
```

### Stateful builder (advanced, avoids closure allocation)

```csharp
CLogger.LogInfo(player, (p, sb) => 
    sb.Append("Player ").Append(p.name).Append(" HP: ").Append(p.hp), "Combat");
```

## Object Pool Monitoring

Monitor pool health in Editor or Development builds:

```csharp
#if UNITY_EDITOR || DEVELOPMENT_BUILD
var sbStats = CycloneGames.Logger.Util.StringBuilderPool.GetStatistics();
var msgStats = LogMessagePool.GetStatistics();

Debug.Log($@"
StringBuilder Pool - Current: {sbStats.CurrentSize}, Peak: {sbStats.PeakSize}
  Hit Rate: {sbStats.HitRate:P}, Discard Rate: {sbStats.DiscardRate:P}
  
LogMessage Pool - Current: {msgStats.CurrentSize}, Peak: {msgStats.PeakSize}
  Hit Rate: {msgStats.HitRate:P}, Discard Rate: {msgStats.DiscardRate:P}
");
#endif
```

**Key Metrics**:
- **PeakSize**: Maximum pool size reached (should stay well below Max capacity)
- **DiscardRate**: Should be ~0% for optimal performance
- **HitRate**: Should be ~100% (objects from pool vs newly allocated)
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
    ArchiveTimestampFormat = "yyyyMMdd_HHmmss"
};

var path = System.IO.Path.Combine(Application.persistentDataPath, "App.log");
CLogger.Instance.AddLoggerUnique(new FileLogger(path, options));
```

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

## Best practices

**Performance:**
- Use builder overloads in hot paths for zero-GC
- Monitor DiscardRate during development (should be ~0%)
- Set appropriate LogLevel to reduce overhead

**Platform:**
- Tune Pump(maxItems) for single-threaded processing to fit frame budget
- Use centralized bootstrap (settings asset or code) to avoid duplicate registration

**Quality:**
- Use AddLoggerUnique for global sinks
- Use AddLogger for per-feature dedicated sinks (e.g., a benchmark file)
- In the Unity Editor, avoid adding ConsoleLogger alongside UnityLogger to prevent duplicate console entries

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

**High DiscardRate in pool statistics**:  
Consider increasing PeakPoolSize in pool source code, or reduce log frequency.

**Memory growth over time**:  
Verify TrimCount > 0 in statistics. Pools should auto-trim after bursts.