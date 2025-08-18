> [!NOTE]
> README和部分代码由AI辅助完成

# CycloneGames.Logger

[English](README.md) | 简体中文

高性能、低/零 GC 的 Unity/.NET 日志模块，兼顾稳定与跨平台（Android、iOS、Windows、macOS、Linux、Web/WASM）。

## 功能特性

- 可插拔处理策略：线程化后台或单线程 Pump
- 零/低 GC Builder API
- LogMessage 与 StringBuilder 池化
- 等级与分类过滤
- Unity Console 可点击跳转
- 可选 FileLogger + 维护/轮转

## 快速开始（Unity）

默认引导在任意场景加载前自动运行：按平台选择处理策略，并默认注册 UnityLogger（可配置关闭）。

```csharp
using CycloneGames.Logger;
void Start() { CLogger.LogInfo("Hello from CycloneGames.Logger"); }
```

## 全局集中配置

可通过项目资源或代码集中配置。

### 推荐：LoggerSettings 资源

1) 创建：Assets -> Create -> CycloneGames -> Logger -> LoggerSettings
2) 放置到：Assets/Resources/CycloneGames.Logger/LoggerSettings.asset  
   重要：不要重命名该资源或父目录；默认从 Resources/CycloneGames.Logger/LoggerSettings 加载。
3) 配置：
   - 处理策略：AutoDetect / ForceThreaded / ForceSingleThread
   - 注册：UnityLogger、FileLogger 开关
   - 文件：persistentDataPath 或自定义路径/文件名
   - 默认：LogLevel、LogFilter

### 代码方式（高级）

在首次使用前调用：

```csharp
// 策略
CLogger.ConfigureThreadedProcessing(); // 支持线程的平台
// 或
CLogger.ConfigureSingleThreadedProcessing(); // Web/WASM（需要 Pump()）

// 后端
CLogger.Instance.AddLoggerUnique(new UnityLogger());
var path = System.IO.Path.Combine(Application.persistentDataPath, "App.log");
CLogger.Instance.AddLoggerUnique(new FileLogger(path));

// 默认
CLogger.Instance.SetLogLevel(LogLevel.Info);
CLogger.Instance.SetLogFilter(LogFilter.LogAll);
```

## 日志 API

- 字符串重载：

```csharp
CLogger.LogInfo("Connected", "Net");
CLogger.LogWarning("Low HP", "Gameplay");
```

- Builder 重载（低/零 GC）：

```csharp
CLogger.LogDebug(sb => { sb.Append("PlayerId="); sb.Append(playerId); }, "Net");
CLogger.LogError(sb => { sb.Append("Err="); sb.Append(code); }, "Net");
```

## WebGL 与 Pump()

Web/WASM 不支持后台线程，需定期 Pump（例如每帧）：

```csharp
void Update() { CLogger.Instance.Pump(4096); }
```

线程化模式下 Pump() 为 no-op，可无条件调用。

## FileLogger 配置与维护

- 基础：

```csharp
var path = System.IO.Path.Combine(Application.persistentDataPath, "App.log");
CLogger.Instance.AddLoggerUnique(new FileLogger(path));
```

- 预警/轮转：

```csharp
var options = new FileLoggerOptions {
  MaintenanceMode = FileMaintenanceMode.Rotate, // 或 WarnOnly
  MaxFileBytes = 10 * 1024 * 1024,
  MaxArchiveFiles = 5,
  ArchiveTimestampFormat = "yyyyMMdd_HHmmss"
};
CLogger.Instance.AddLoggerUnique(new FileLogger(path, options));
```

注意：WebGL 无文件系统；移动/主机平台优先 persistentDataPath。

## 过滤

```csharp
CLogger.Instance.SetLogLevel(LogLevel.Warning);
CLogger.Instance.SetLogFilter(LogFilter.LogAll);
CLogger.Instance.AddToWhiteList("Gameplay");
CLogger.Instance.SetLogFilter(LogFilter.LogWhiteList);
```

## 使用建议

- 仅保留一个集中引导（资源或代码）避免重复注册
- 热路径优先使用 Builder 重载
- 单线程处理时调优 Pump(maxItems)
- 全局后端用 AddLoggerUnique；专项后端（如基准文件）用 AddLogger
- Editor 避免同时添加 ConsoleLogger 与 UnityLogger

## 故障排查

- Console 重复：Editor 下同时启用 ConsoleLogger 与 UnityLogger 可能重复展示，建议 Editor 跳过 ConsoleLogger 或仅保留 UnityLogger。
- 文件无输出：确认已添加 FileLogger（默认不注册）且路径可写。
