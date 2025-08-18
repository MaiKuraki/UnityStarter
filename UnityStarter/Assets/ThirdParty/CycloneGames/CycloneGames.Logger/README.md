> [!NOTE]
> The README and some of the code were co-authored by AI.

# CycloneGames.Logger

English | [简体中文](README.SCH.md)

High-performance, low/zero-GC logging for Unity and .NET, designed for stability and portability across platforms (Android, iOS, Windows, macOS, Linux, Web/WASM such as Unity WebGL).

## Features

- Pluggable processing strategy: Threaded worker or Single-threaded Pump
- Zero/min-GC builder APIs for hot paths
- Log message and StringBuilder pools
- Category filtering (whitelist/blacklist) and severity levels
- Unity Console click-to-source formatting
- Optional FileLogger with maintenance/rotation

## Quick Start (Unity)

Out of the box, the default bootstrap runs before any scene loads:

- Auto-detects platform and selects processing strategy (WebGL → Single-threaded; others → Threaded)
- Registers UnityLogger by default (can be disabled via settings)

Start logging immediately:

```csharp
using CycloneGames.Logger;

void Start()
{
  CLogger.LogInfo("Hello from CycloneGames.Logger");
}
```

## Centralized configuration

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

String overloads (simple):

```csharp
CLogger.LogInfo("Connected", "Net");
CLogger.LogWarning("Low HP", "Gameplay");
```

Builder overloads (low/zero-GC and only build when enabled):

```csharp
CLogger.LogDebug(sb => { sb.Append("PlayerId="); sb.Append(playerId); }, "Net");
CLogger.LogError(sb => { sb.Append("Err="); sb.Append(code); }, "Net");
```

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

- Keep one centralized bootstrap (settings asset or code) to avoid duplicate registration
- Use builder overloads in hot paths
- Tune Pump(maxItems) for single-threaded processing to fit frame budget
- Use AddLoggerUnique for global sinks; use AddLogger for per-feature dedicated sinks (e.g., a benchmark file)
- In the Unity Editor, avoid adding ConsoleLogger alongside UnityLogger to prevent duplicate console entries

## Troubleshooting

- Duplicate lines in Unity Console: If both ConsoleLogger and UnityLogger are active in the Editor, the Editor may surface both stdout and Debug.Log. Skip ConsoleLogger in the Editor or keep only UnityLogger.
- No file output: Ensure you added a FileLogger (it is not registered by default) and that the path is writeable.