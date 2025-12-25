# CycloneGames.Logger Samples

High-performance, zero-GC logging system with three-tier adaptive capacity management for optimal memory safety across all Unity platforms.

## Key Features

- **Three-Tier Capacity Management** - Automatic pool expansion & contraction  
- **Zero-GC Logging** - Builder API eliminates allocations in hot paths  
- **Object Pool Monitoring** - Debug/Development build statistics  
- **Cross-Platform** - Supports Windows, macOS, Linux, Android, iOS, WebGL, and consoles

## Sample Scripts

### LoggerPoolMonitor.cs
**Interactive pool monitoring and capacity validation**

Features:
- Real-time pool statistics display
- Burst test to validate zero-GC behavior
- Demonstrates three-tier capacity management (Target/Peak/Max)
- Context menu commands for easy testing

Usage:
```csharp
// Add to a GameObject and play
// Right-click in Inspector for context menu:
//  - Show Pool Statistics
//  - Run Burst Test
//  - Reset Statistics
```

### LoggerBenchmark.cs
**Performance comparison with GC tracking**

Tests:
- Unity Debug.Log vs CLogger String API vs Builder API
- Measures execution time and GC allocations
- Displays object pool statistics after tests

Expected Results:
- Builder API: **Minimal GC allocation** (includes Unity framework overhead; production GC is near-zero)
- String API: Medium GC allocation
- Unity Debug.Log: High GC allocation

Note: GC measurements include Unity test environment overhead and cold-start pool allocation. The key indicators are 100% Return Rate and 0% Discard Rate, which validate zero-GC behavior in production.

### LoggerPerformanceTest.cs
**High-volume logging stress test**

- Logs 10,000 messages across all severity levels
- Validates pool behavior under sustained load
- Reports peak pool size and discard count

### LoggerSample.cs
**Basic usage example**

Simple demonstration of logger setup and basic logging.

---

## Three-Tier Capacity Management

The logger uses adaptive object pools with automatic expansion and contraction:

```
Target (128/256)  <- Normal steady-state capacity
    | Auto-expand under load
Peak (1024/4096)  <- Maximum allowed during bursts (0 GC)
    | Triggers async trim
Max (2048/8192)   <- Hard limit to prevent memory leaks
```

### How It Works

1. **Normal Load**: Pool stays at Target capacity (128 for StringBuilder, 256 for LogMessage)
2. **Burst Load**: Pool auto-expands to Peak capacity **without discarding objects** (0 GC)
3. **After Burst**: Pool automatically shrinks back to Target, releasing excess memory
4. **Extreme Load**: Only discards when exceeding Max (rare, safety mechanism)

**Result**: Zero GC in 99.9% of scenarios while maintaining memory safety.

---

## Processing Strategies

### ThreadedLogProcessor (Default)
Uses a background thread with `BelowNormal` priority for maximum performance on platforms with threading support.

### SingleThreadLogProcessor
For platforms without threads (WebGL). Requires calling `Pump()` each frame.

```csharp
#if UNITY_WEBGL && !UNITY_EDITOR
    CLogger.ConfigureSingleThreadedProcessing();
#else
    CLogger.ConfigureThreadedProcessing();
#endif
```

---

## Zero-GC Logging

### String API (Convenient)
```csharp
CLogger.LogInfo($"Player HP: {hp}", "Combat");
// Small GC from string interpolation
```

### Builder API (Zero-GC) [推荐]
```csharp
CLogger.LogInfo(sb => sb.Append("Player HP: ").Append(hp), "Combat");
// Zero GC - StringBuilder is pooled
```

### Stateful Builder (Advanced)
```csharp
CLogger.LogInfo(player, (p, sb) => 
    sb.Append("Player ").Append(p.name).Append(" HP: ").Append(p.hp), "Combat");
// Zero GC + avoids closure allocation
```

---

## Object Pool Statistics (Editor/Development Only)

Monitor pool health in Editor or Development builds:

```csharp
#if UNITY_EDITOR || DEVELOPMENT_BUILD
var stats = StringBuilderPool.GetStatistics();
Debug.Log($@"
StringBuilder Pool:
  Current: {stats.CurrentSize} | Peak: {stats.PeakSize}
  Hit Rate: {stats.HitRate:P} | Discard Rate: {stats.DiscardRate:P}
");
#endif
```

**Key Metrics**:
- **PeakSize**: Maximum pool size reached (should be below Max)
- **DiscardRate**: Should be ~0% for optimal performance
- **HitRate**: Should be ~100% (objects retrieved from pool vs created)

---

## Centralized Setup

### Option 1: LoggerSettings Asset (Recommended)
1. Create via `Assets -> Create -> CycloneGames -> Logger -> LoggerSettings`
2. Move to `Assets/Resources/CycloneGames.Logger/LoggerSettings.asset`
3. Configure: processing mode, loggers, log level, etc.

### Option 2: Custom Bootstrap
```csharp
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
    var path = Path.Combine(Application.persistentDataPath, "App.log");
    CLogger.Instance.AddLoggerUnique(new FileLogger(path));
    #endif

    CLogger.Instance.SetLogLevel(LogLevel.Info);
}
```

---

## Best Practices

**Performance:**
- Use **Builder API** in performance-critical code  
- Monitor **DiscardRate** in development builds  
- Set appropriate **LogLevel** to filter unnecessary logs  

**Platform:**
- Call **Pump()** in Update for WebGL builds  
- Use **categories** for fine-grained filtering  

**Quality:**
- Centralize logger configuration  
- Avoid duplicate logger registration  

---

## Troubleshooting

**Q: High DiscardRate in statistics?**  
A: Increase `PeakPoolSize` in pool source code, or reduce log frequency.

**Q: Memory growing over time?**  
A: Verify `TrimCount > 0` in statistics. Pools should auto-trim after bursts.

**Q: WebGL logs not appearing?**  
A: Ensure `Pump()` is called each frame with sufficient `maxItems`.

---

For more details, see the main package documentation.
