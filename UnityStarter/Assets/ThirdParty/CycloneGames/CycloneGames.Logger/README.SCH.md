# CycloneGames.Logger

[English | 简体中文](README.md)

CycloneGames.Logger 是面向 Unity 应用、Headless Player、命令行工具、测试和纯 C# 服务的有界、可观测日志底座。它提供无 Unity 依赖的核心、可选 Unity adapter、显式队列与内存预算、失败隔离 sink、具备恢复能力的文件输出，以及可以被监控而不是被假设成功的生命周期结果。

## 目录

- [概述](#概述)
- [架构](#架构)
- [快速上手](#快速上手)
- [核心概念](#核心概念)
- [使用指南](#使用指南)
- [进阶主题](#进阶主题)
- [常见场景](#常见场景)
- [性能与内存](#性能与内存)
- [故障排查](#故障排查)

## 概述

当应用需要比直接调用 `Debug.Log` 更强的控制能力时，CycloneGames.Logger 为生产者提供单一的有界管线：在构造延迟消息前执行 severity 与 category 过滤，同时受消息数与保留字符数限制的队列，按 sink 隔离失败的同步 sink，以及会主动报告丢弃、sink 失败与未完成 shutdown 的生命周期结果。

核心程序集设置了 `noEngineReferences: true`，且不暴露任何 `UnityEngine` 类型。Unity 专属行为（`LoggerBootstrap`、`LoggerSettings`、`UnityLogger`）位于独立的 adapter 程序集，使同一套日志契约能在 Editor、Runtime、Headless Player、Dedicated Server、CLI 工具、测试和纯 C# 服务中运行。

有限队列是过载保护，不是消息必达。本模块不提供自动脱敏、加密、远程上传、服务端确认、事务审计存储或主机平台 SDK 集成。支付、账号、反作弊、合规和安全审计记录需要单独评审的持久化管线。没有产品负责的数据政策时，禁止记录 credential、token、个人数据或未经脱敏的用户内容。

### 主要特性

- **有界队列**：消息数与保留字符数双重限制，多种 overflow policy，以及 critical record 预留。
- **Threaded 与 caller-pumped 两种处理模式**：通过 `CLoggerFactory.CreateThreaded` 与 `CreateSingleThreaded` 选择。
- **失败隔离 sink**：`UnityLogger`、`ConsoleLogger`、`FileLogger` 以及自定义 `ILogger`；按 sink 隔离避免一个失败 sink 阻塞其他 sink。
- **具备恢复能力的文件输出**：有界轮转、恢复尝试、flush 模式与健康统计。
- **可观测生命周期**：`LogProcessingStatistics`、`UnityLoggerStatistics`、`FileLogger.Statistics` 与 `LoggerShutdownResult` 暴露丢弃、失败与未完成工作。
- **静态与可注入断言**：通过 `CLogAssert` 与 `CLogAssertService`。
- **Unity 设置资产**：自定义 Inspector，支持环境变量与命令行参数的构建期覆盖。

## 架构

```mermaid
flowchart LR
    Product["游戏、工具、测试或服务"] --> Facade["CLogger 静态 Facade"]
    Product --> Contract["ICLogger 实例契约"]
    Facade --> Core["CycloneGames.Logger<br/>无 Unity 依赖核心"]
    Contract --> Core
    UnityHost["Unity 生命周期"] --> UnityAdapter["CycloneGames.Logger.Unity"]
    UnityAdapter --> Core
    Editor["CycloneGames.Logger.Editor"] --> UnityAdapter
    Core --> Console["ConsoleLogger"]
    Core --> File["FileLogger"]
    Core --> Custom["自定义同步 sink"]
    UnityAdapter --> UnityConsole["UnityLogger 主线程 handoff"]
```

| 程序集 | 用途 | Unity 依赖 |
| --- | --- | --- |
| `CycloneGames.Logger` | 核心契约、处理、过滤、断言、`ConsoleLogger`、`FileLogger` | 无（`noEngineReferences: true`） |
| `CycloneGames.Logger.Unity` | `LoggerBootstrap`、`LoggerSettings`、`UnityLogger`、Unity 生命周期宿主 | `UnityEngine` |
| `CycloneGames.Logger.Editor` | 设置 Inspector、源码超链接、构建覆盖处理 | `UnityEditor` |
| `CycloneGames.Logger.Samples` | 隔离的 sample scene 与诊断 component | Unity adapter（`autoReferenced: false`） |
| `CycloneGames.Logger.Tests.Editor` | 功能与可靠性测试 | Unity Test Framework |
| `CycloneGames.Logger.Tests.Performance` | 性能 case 与稳态分配断言 | Performance Test Framework |

核心 public 契约不暴露 `GameObject`、`MonoBehaviour`、`ScriptableObject` 或其他 `UnityEngine` 类型。Unity 专属行为留在 adapter 程序集中。

每条被接受的记录都经过相同的有界管线：

```mermaid
flowchart LR
    Call["日志调用"] --> Gate["Level、category、sink、生命周期检查"]
    Gate --> Reserve["预留消息与字符容量"]
    Reserve --> Build["捕获 UTC 时间并构造有界 payload"]
    Build --> Queue["有界 core ring"]
    Queue --> Processor["Worker 或调用方 Pump"]
    Processor --> Sinks["同步借用式 dispatch"]
    Sinks --> UnityQueue["可选有界 Unity handoff"]
    UnityQueue --> MainThread["Unity 主线程 drain"]
```

延迟 builder 执行前会先检查过滤与 sink 可用性。消息数与保留字符预算同时计入 queued、reserved 与 in-flight 工作。sink 调用是同步操作，timeout 无法抢占。sink 只能借用 `LogMessage` 到 `ILogger.Log` 返回。Unity Console 使用第二个有界队列，因为 Unity API 要求主线程。

## 快速上手

### Unity 接入

1. 在 Unity 中选择 `Tools > CycloneGames > Logger > Create Default LoggerSettings`，命令会在 `Assets/Resources/CycloneGames.Logger/LoggerSettings.asset` 创建资产。
2. 选中资产，在自定义 Inspector 中点击 `Validate Settings`。无效容量、不受支持的 Unity Console policy 和不安全文件路径会在进入构建前被拒绝。
3. 在任何引用 `CycloneGames.Logger` 与 `CycloneGames.Logger.Unity` 的代码中写日志：

```csharp
using CycloneGames.Logger;
using UnityEngine;

public sealed class InventoryController : MonoBehaviour
{
    private void Start()
    {
        CLogger.LogInfo("Inventory initialized.", "Inventory");
    }

    public void ReportLoadFailure(string itemId)
    {
        CLogger.LogError(
            itemId,
            static (value, builder) => builder.Append("Failed to load item: ").Append(value),
            "Inventory");
    }
}
```

`LoggerBootstrap` 在第一个 Scene 之前运行，加载设置资产、创建 runtime host、注册所选 sink，并应用默认 level 与 filter。如果没有 sink 能够注册，静态日志会被抑制，也不会创建未配置的全局实例。

### 纯 C# 或服务器接入

核心程序集设置了 `noEngineReferences: true`，可以在没有 `UnityEngine` 的环境中使用：

```csharp
using CycloneGames.Logger;

var options = new LoggerProcessingOptions
{
    MaxQueuedMessages = 2048,
    MaxQueuedCharacters = 1024 * 1024,
    OverflowPolicy = LogQueueOverflowPolicy.DropNewest,
    CriticalLevel = LogLevel.Error
};

CLogger logger = CLoggerFactory.CreateThreaded(options);
logger.AddLoggerUnique(new ConsoleLogger());

logger.Log(LogLevel.Info, "Service started.", "Bootstrap");

LoggerShutdownResult result = logger.ShutdownInstance(LogFlushMode.Buffered, 2000);
if (result.IsComplete)
{
    logger.Dispose();
}
else
{
    // Keep the instance, release the blocked external dependency, and retry shutdown.
}
```

宿主必须控制 dispatch affinity 时使用 `CLoggerFactory.CreateSingleThreaded`，并从宿主 update loop 调用 `Pump`：

```csharp
ICLogger logger = CLoggerFactory.CreateSingleThreaded(options);
logger.Pump(maxItems: 256);
```

向领域服务注入 `ICLogger`。Composition root 拥有具体 `CLogger`、sink 与最终 shutdown。领域代码不应通过 Service Locator 解析 `CLogger.Instance`。

## 核心概念

### Level 与过滤

Level 按严重程度从低到高排列：`Trace`、`Debug`、`Info`、`Warning`、`Error`、`Fatal`、`None`。`SetLogLevel(LogLevel.Warning)` 过滤 `Trace`、`Debug` 与 `Info`。`None` 禁用所有可接受日志级别。

```csharp
CLogger.Instance.SetLogLevel(LogLevel.Warning);

CLogger.LogInfo("Filtered.", "Loading");   // 不会入队
CLogger.LogError("Accepted.", "Loading");  // 入队
```

Category 匹配不区分大小写。`LogAll` 接受所有 category，`LogWhiteList` 只接受已列出的 category，`LogNoBlackList` 接受除列出项之外的所有 category。

```csharp
ICLogger logger = CLogger.Instance;

logger.SetLogFilter(LogFilter.LogWhiteList);
logger.AddToWhiteList("Networking");
logger.AddToWhiteList("Save");

logger.SetLogFilter(LogFilter.LogNoBlackList);
logger.AddToBlackList("AnimationTrace");
```

Whitelist 与 blacklist 更新会复制对应集合，并共享 `MaxFilterCategories` 与 `MaxFilterCharacters`。Key 过长会抛 `ArgumentOutOfRangeException`；共享预算耗尽会抛 `InvalidOperationException`。

### 消息构造

三种 overload 覆盖冷路径到已测量的热路径。

**简单字符串** —— 值已经存在或调用属于冷路径。字符串插值发生在 logger 过滤之前，因此当 level 可能被过滤时优先使用延迟形式：

```csharp
CLogger.LogInfo("Matchmaking connected.", "Networking");

// The string is created before LogDebug checks the active level.
CLogger.LogDebug($"Entity {entityId} moved to {position}.", "Simulation");
```

**延迟 builder** —— callback 只在 admission 成功后运行：

```csharp
CLogger.LogDebug(
    builder => builder.Append("Entity ").Append(entityId).Append(" updated."),
    "Simulation");
```

**State 与缓存 builder** —— 对已测量的热路径，单独传递 state 并缓存 delegate 以避免 capturing closure：

```csharp
using System;
using System.Text;
using CycloneGames.Logger;

public static class CombatLog
{
    private static readonly Action<HitState, StringBuilder> AppendHit = AppendHitMessage;

    public static void Hit(int attackerId, int targetId, int damage)
    {
        CLogger.LogDebug(
            new HitState(attackerId, targetId, damage),
            AppendHit,
            "Combat");
    }

    private static void AppendHitMessage(HitState state, StringBuilder builder)
    {
        builder.Append("Attacker ").Append(state.AttackerId)
            .Append(" hit target ").Append(state.TargetId)
            .Append(" for ").Append(state.Damage).Append('.');
    }

    private readonly struct HitState
    {
        public readonly int AttackerId;
        public readonly int TargetId;
        public readonly int Damage;

        public HitState(int attackerId, int targetId, int damage)
        {
            AttackerId = attackerId;
            TargetId = targetId;
            Damage = damage;
        }
    }
}
```

该形式避免示例调用点产生 capturing closure，但不是全局零分配承诺。Pool miss、builder 扩容、调用方 state、sink、异常和平台 I/O 仍可能分配。

API 默认捕获 `CallerFilePath`、`CallerLineNumber` 与 `CallerMemberName`。File 与 Console sink 默认只输出文件名。`FullPath` 可能暴露构建机目录，只有在明确隐私 policy 下才能启用。

### Builder 失败行为

已获准 builder 抛出非 `OutOfMemoryException` 时，异常不会逃逸到日志调用方。Logger 会增加 `MessageBuilderFailureCount`、清空不完整消息、通过正常队列提交有界 `[log message builder failed: ExceptionType]` 记录，并在该实例第一次 builder failure 时输出 emergency diagnostic。`OutOfMemoryException` 会传播；reservation 与临时 pooled builder 仍由 `finally` 路径释放。

### 处理模式

**Threaded** —— `CLoggerFactory.CreateThreaded` 以及受支持非 WebGL 目标上的 Unity `AutoDetect` 使用一个名为 `CLogger.Worker` 的后台线程。Producer 向同步有界 ring 预留并提交，worker 串行 dispatch 记录并执行周期性 sink maintenance。该模式下 `Pump` 不执行工作。

**Single-threaded** —— `CreateSingleThreaded` 只在调用 `Pump` 时 dispatch。调用 `Pump` 的线程会执行该 batch 中所有 sink。适用情景包括 WebGL、宿主拥有确定的 dispatch affinity、测试需要显式推进，或主线程集成没有使用 handoff adapter 而是直接执行。

Unity runtime host 每帧最多 pump 256 条 core record，并使用约 1 ms 的 between-item budget；Unity Console 独立最多 drain 256 条并使用约 2 ms 的 between-item budget。预算只在每个同步 item 返回后检查，因此一个阻塞 sink 可以突破预算。

### 队列容量与背压

核心队列同时施加两个限制：

- `MaxQueuedMessages`：queued + reserved + in-flight record 数量。
- `MaxQueuedCharacters`：queued + reserved + in-flight logger-owned 保留字符数（逻辑保留预算，不是精确 managed heap bytes）。

`MaxMessageCharacters` 会截断正文并在格式化时追加 ` [truncated]`。Category、source path 与 member name 只复制到各自配置上限。

| Overflow policy | 容量满时行为 | 权衡 |
| --- | --- | --- |
| `DropNewest` | 拒绝传入记录 | Producer latency 稳定；可能丢失最新上下文 |
| `DropOldest` | 驱逐一个符合条件的 queued record | 保留较新上下文；过载时可能扫描并移动 entry |
| `Block` | 等待到 `EnqueueBlockTimeoutMs`，随后拒绝 | 可能阻塞调用方；避免用于 Unity 主线程和延迟敏感线程 |

`ReservedCriticalMessages` 与 `ReservedCriticalCharacters` 会保留部分容量不允许低于 `CriticalLevel` 的记录使用。Critical record 可以使用完整队列，并在 policy 允许时优先驱逐非 critical record。这是过载保护，不是消息必达——队列被 critical 工作占满、sink 阻塞、存储失败、shutdown 超时或进程终止时，critical record 仍可能丢失。

## 使用指南

### Sink 与所有权

| Sink | 目标宿主 | 执行与存储行为 |
| --- | --- | --- |
| `UnityLogger` | Unity client/Editor | 在借用 dispatch 中格式化，复制到有界 handoff，再从 Unity 主线程输出 |
| `ConsoleLogger` | CLI、headless 进程、Dedicated Server | 同步写入；低级别到 `Console.Out`，`Error`/`Fatal` 到 `Console.Error` |
| `FileLogger` | 支持且可写文件系统的目标 | 同步格式化 UTF-8 文本，在配置限制内轮转并报告健康状态 |

注册规则：

- `AddLogger` 返回 `true` 时，该精确 sink 实例的所有权转移给 `CLogger`。返回 `false` 时没有建立新的转移；它也可能表示同一 identity 已由 logger 拥有，因此不能只因为返回 `false` 就 Dispose。
- `AddLoggerUnique` 对同一精确 runtime type 最多接受一个。被拒绝的另一实例会在返回前 Dispose；重复引用不会 Dispose。
- `RemoveLogger` 不会 Dispose。只有 `true` 表示 dispatch 已静止且所有权转回该调用方。`false` 时绝不能 Dispose；解除 timeout 原因后重试。
- `ClearLoggers` retire 所有 active sink，并在静止后调度 logger-owned disposal。
- 每个 `CLogger` 对 active、retired、queued-for-disposal 或 disposing sink 的总拥有数量上限为 256。

Disposal 由每个 logger 一个惰性创建的 owner 串行执行。非 WebGL 目标使用 `CLogger.SinkDisposal` 后台 worker，WebGL 使用同步路径。普通自定义 sink 只执行一次 `Dispose` attempt。只有在较早 `Dispose` 中途抛异常后重试仍安全时，才能实现 `IIdempotentLoggerSinkDisposal`；marked sink 最多尝试三次。

### 编写自定义 Sink

`ILogger.Log(LogMessage)` 是同步 borrowed-payload 契约。只能在调用期间读取 payload 并通过 `AppendMessageTo` 使用正文；不得保留 `LogMessage` 或任何内部 pooled storage。

以下固定容量的近期消息 sink 使用了有明确用途的同步，因为 worker dispatch 与 UI 读取可能发生在不同线程。容量满时覆盖最早复制的 string，因此 retained entry 数量有界。

```csharp
using System;
using System.Text;
using CycloneGames.Logger;

public sealed class RecentLogSink : ILogger
{
    private readonly object _syncRoot = new object();
    private readonly string[] _entries;
    private readonly StringBuilder _scratch = new StringBuilder(256);
    private int _next;
    private bool _disposed;

    public RecentLogSink(int capacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _entries = new string[capacity];
    }

    public void Log(LogMessage message)
    {
        if (message == null) throw new ArgumentNullException(nameof(message));

        lock (_syncRoot)
        {
            if (_disposed) return;

            _scratch.Clear();
            message.AppendMessageTo(_scratch, escapeControlCharacters: true);
            _entries[_next] = _scratch.ToString();
            _next = (_next + 1) % _entries.Length;
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed) return;
            _disposed = true;
            Array.Clear(_entries, 0, _entries.Length);
            _scratch.Clear();
        }
    }
}
```

该示例限制了 entry 数，但每条被接受记录仍会分配一个复制后的 string。异步、远程或主线程 adapter 还需要 retained character/byte 预算、overflow policy、drop counter、线程亲和规则、flush 语义和显式 shutdown 所有权。

### 生命周期、Flush 与 Shutdown

**全局 Logger** —— 在 Unity bootstrap 之外，应在 `CLogger.Instance` 或第一条被接受的静态日志之前配置 processing：

```csharp
CLogger.ConfigureThreadedProcessing(options);
CLogger.ConfigureTimestampProvider(static () => DateTime.UtcNow);

ICLogger logger = CLogger.Instance;
```

全局实例存在后，processing 配置会返回 `false`。全局实例只能通过 `CLogger.Shutdown(LogFlushMode.Buffered)` 停止。对 `CLogger.Instance` 调用 `ShutdownInstance` 会抛异常，因为静态 shutdown 负责全局 detach 与重试协调。

**显式 Logger** —— Factory 创建的 logger 使用 `logger.ShutdownInstance(LogFlushMode.Durable, 5000)`。Shutdown 超时时应保留实例，释放或修复阻塞的外部依赖，然后重试。Timeout 不表示所有权已经完成。

| Flush 模式 | 请求 |
| --- | --- |
| `Buffered` | 排空 core 工作并 flush managed sink buffer |
| `Durable` | 另外请求有能力的 sink 执行操作系统 durable flush |

`Durable` 不保证断电、控制器 cache、浏览器 storage 或远端 acknowledgement。`TryFlush` 等待 core processing、active dispatch 和 logger-owned sink disposal，再调用 `IFlushableLogger` sink。Timeout 在同步操作之间检查，无法 cancel 已经阻塞的 `ILogger.Log`、`TryFlush`、`Dispose`、Console 调用或文件系统调用。

| Shutdown 状态 | 含义 |
| --- | --- |
| `Completed` | Processing 与所请求 flush 完成，未观察到 drop 或终态失败 |
| `CompletedWithDrops` | Shutdown 完成，但 logger 观察到记录丢弃 |
| `CompletedWithFailures` | Shutdown 完成，但存在 sink flush 或 disposal 失败 |
| `TimedOut` | 工作或所有权仍未完成；保留实例并重试 |
| `AlreadyStopped` | 实例已经停止 |

对于 `CompletedWithDrops` 和 `CompletedWithFailures`，`IsComplete` 也为 `true`。必须同时检查 `Status`、`DroppedMessageCount` 和 `SinksFlushed`。

### 文件日志

在 Unity 设置中启用 `registerFileLogger`。安全默认路径为 `Application.persistentDataPath/App.log`。`fileName` 只能使用可移植 leaf name。自定义路径必须满足 `usePersistentDataPath = false`、`allowCustomFilePath = true`、`customFilePath` 是 fully qualified absolute path，并在目标平台验证 sandbox、permission、quota、backup、可移动存储和 shutdown。

```csharp
var fileOptions = new FileLoggerOptions
{
    MaintenanceMode = FileMaintenanceMode.Rotate,
    MaxFileBytes = 10L * 1024L * 1024L,
    MaxArchiveFiles = 5,
    FlushBatchSize = 64,
    FlushIntervalMs = 1000,
    DurableFlushOnFatal = false,
    SourcePathMode = LogSourcePathMode.FileName
};

var fileSink = new FileLogger(logPath, fileOptions);
logger.AddLoggerUnique(fileSink);
```

`FileLogger` 写入 UTF-8 without BOM。它会转义 message、category 与 source field 中的控制字符，避免一个 event 注入任意物理行。`Error` 与 `Fatal` 会触发 flush；启用 `DurableFlushOnFatal` 后，`Fatal` 请求 durable flush。

| 字段 | 默认值 | 含义 |
| --- | ---: | --- |
| `MaintenanceMode` | `Rotate` | `None`、只在阈值处告警的 `WarnOnly`，或有界 `Rotate` |
| `MaxFileBytes` | 10 MiB | `Rotate` 模式下 active-file UTF-8 字节上限 |
| `MaxArchiveFiles` | 5 | Logger-owned archive 最大数量；为零时轮转后移除 archive |
| `FlushBatchSize` | 64 | Buffered flush 之间接受的记录数 |
| `FlushIntervalMs` | 1000 | 最大 buffered 间隔；为零时每条接受记录都 flush |
| `RecoveryRetryIntervalMs` | 5000 | Writer 不可用期间的最小重试间隔 |
| `DiagnosticIntervalMs` | 30000 | Emergency diagnostic 最小间隔；为零时禁用节流 |
| `DurableFlushOnFatal` | `false` | 为 `Fatal` 请求 OS durable flush |
| `SourcePathMode` | `FileName` | `None`、`FileName` 或隐私敏感的 `FullPath` |

打开、轮转或写入都可能失败。触发操作的记录会被丢弃，而不是突破字节上限。Sink 会尝试有界恢复并报告 `Healthy`、`Degraded`、`Faulted` 或 `Disposed`。显式构造无法建立 writer 时会抛异常；Unity bootstrap 会捕获该失败，通过 emergency 与 Unity 路径报告且不包含配置路径，并继续使用已成功初始化的 sink。

### 断言

`CLogAssert` 是静态 Facade，`CLogAssert.CreateService(ICLogger, options)` 创建可注入的 `CLogAssertService`。

```csharp
CLogAssert.Configure(new CLogAssertOptions
{
    Enabled = true,
    FailureLevel = LogLevel.Error,
    FailureBehavior = CLogAssertFailureBehavior.LogAndThrow,
    Category = "GameplayInvariant",
    FlushBeforeThrow = true,
    FlushTimeoutMs = 100
});

CLogAssert.IsNotNull(playerState, "Player state must exist before simulation.");
```

支持 `That`、`IsTrue`、`IsFalse`、`IsNull`、`IsNotNull`、`AreEqual`、`AreNotEqual` 与 `Fail`。Builder overload 在条件成立时跳过消息构造。`LogOnly` 只记录，`Throw` 只抛异常，`LogAndThrow` 同时执行两者。同时记录并抛出时，默认先请求一次 best-effort buffered flush。阻塞 sink 可能使实际 throw 晚于 `FlushTimeoutMs`，因为同步工作无法被抢占。Flush 失败不会抑制 `CLogAssertionException`。

断言不能替代输入校验、可恢复错误处理、authority check 或安全强制。

### 可观测性

`logger.GetProcessingStatistics()` 返回某一时刻的 `LogProcessingStatistics` 快照，最常用字段如下：

| 字段 | 含义 |
| --- | --- |
| `QueuedCount`, `QueuedCharacters` | Core queue 中等待处理的已提交工作 |
| `ReservedCount` | 尚未 commit 或 cancel 的 producer reservation |
| `InFlightCount`, `InFlightCharacters` | 正在执行 processor/sink dispatch 的记录 |
| `PeakQueuedCount`, `PeakQueuedCharacters` | Committed 加 in-flight 的累计 high-watermark |
| `EnqueuedMessageCount`, `ProcessedMessageCount` | 成功 commit 与完成的记录数 |
| `DroppedMessageCount` | Newest drop + oldest eviction + stop 后 rejection |
| `DroppedNewestCount`, `DroppedOldestCount` | Rejection 与 eviction 总数 |
| `DroppedCriticalCount` | 达到或高于 `CriticalLevel` 的 drop |
| `SinkFailureCount`, `QuarantinedSinkCount` | Sink 异常与累计 quarantine event |
| `PendingSinkDisposalCount` | 等待静止或 disposal 完成的 owned sink |
| `MessageBuilderFailureCount` | 非 OOM 异常后被替代的延迟 builder |

`CLogger.GetMemoryStatistics()` 报告进程级 cache 观察：当前与峰值 retained `LogMessage` 与 `StringBuilder` object、pool miss、discard 与 invalid return。`UnityLogger.GetStatistics()` 报告第二层队列：queued/reserved/in-flight 占用、当前 generation high-watermark、当前 generation drop 与成功 subsystem reset 时 abandon 的累计 entry 数。

生产诊断视图至少应显示 critical/total drop、builder failure、pending disposal、quarantined sink、终态 disposal failure、Unity reset abandonment 以及 file `Degraded`/`Faulted` health。告警阈值必须来自可重复 load、device 与 soak 证据。

```csharp
LogProcessingStatistics core = logger.GetProcessingStatistics();
UnityLoggerStatistics unity = UnityLogger.GetStatistics();

if (core.DroppedCriticalCount > 0 || unity.DroppedCriticalCount > 0)
{
    // Escalate through a diagnostics path that cannot recurse into the same failed sink.
}
```

## 进阶主题

### LoggerSettings 字段参考

Inspector 按用途对 serialized field 分组。新建资产使用以下默认值。

| 分组 | 字段 | 默认值 | 含义 |
| --- | --- | ---: | --- |
| Processing | `processing` | `AutoDetect` | 除 WebGL 外使用 threaded；受支持位置可强制 threaded 或 caller-pumped |
| Processing | `maxQueuedMessages` | 8192 | Core 消息容量 |
| Processing | `maxQueuedCharacters` | 4 Mi characters | Core 保留字符容量 |
| Processing | `maxMessageCharacters` | 16 Ki characters | 单条 message body 上限 |
| Processing | `maxCategoryCharacters` | 256 | 保留的 category prefix 上限 |
| Processing | `reservedCriticalMessages` | 64 | 非 critical record 不可使用的 message slot |
| Processing | `reservedCriticalCharacters` | 64 Ki characters | 非 critical record 不可使用的字符预算 |
| Processing | `unityConsoleMaxQueuedMessages` | 4096 | Unity 主线程 handoff 消息容量 |
| Processing | `unityConsoleMaxQueuedCharacters` | 2 Mi characters | Unity handoff 保留字符容量 |
| Processing | `unityConsoleOverflowPolicy` | `DropNewest` | 独立 Unity handoff policy；只支持 `DropNewest` 或 `DropOldest` |
| Processing | `shutdownDrainTimeoutMs` | 2000 | 默认 drain 与静止等待 timeout |
| Processing | `enqueueBlockTimeoutMs` | 1 | Core `Block` producer 等待上限 |
| Processing | `maintenanceIntervalMs` | 250 | Threaded maintenance 间隔；最小 10 ms |
| Processing | `sinkFailureThreshold` | 3 | Sink 连续异常达到该数后 quarantine |
| Processing | `overflowPolicy` | `DropNewest` | Core queue overflow policy |
| Processing | `guaranteedLevel` | `Error` | 允许使用 reserved capacity 的 severity；不保证必达 |
| Registration | `registerUnityLogger` | `true` | 注册 Unity Console adapter，`UNITY_SERVER` 除外 |
| Registration | `registerConsoleLogger` | `false` | 注册 `System.Console` sink |
| Registration | `registerFileLogger` | `false` | 在受支持位置注册 file sink |
| File | `usePersistentDataPath` | `true` | 将 active file 直接放在 `Application.persistentDataPath` 下 |
| File | `fileName` | `App.log` | Persistent-data placement 使用的可移植 leaf name |
| File | `allowCustomFilePath` | `false` | 显式启用 custom path trust boundary |
| File | `customFilePath` | empty | 禁用 persistent-data placement 时的 fully qualified path |
| File | `fileMaintenanceMode` | `Rotate` | 文件大小处理模式 |
| File | `maxFileBytes` | 10 MiB | 按 mode 作为 active-file byte 阈值或上限 |
| File | `maxArchiveFiles` | 5 | Logger-owned archive 保留数 |
| File | `fileFlushBatchSize` | 64 | 每次 buffered flush 的记录数 |
| File | `fileFlushIntervalMs` | 1000 | 最大 buffered flush 间隔 |
| File | `durableFlushOnFatal` | `false` | 为 `Fatal` 请求 durable flush |
| File | `fileSourcePathMode` | `FileName` | Source path 披露 policy |
| Defaults | `defaultLevel` | `Info` | Sink 注册后的 runtime severity threshold |
| Defaults | `defaultFilter` | `LogAll` | Sink 注册后的 runtime category policy |

`LoggerSettings` 通过 serialized field `guaranteedLevel` 提供配置，`LoggerProcessingOptions` 则通过 `CriticalLevel` 提供 programmatic 配置。两者都表示 reserved capacity 的使用门槛，不代表消息必达。

### 构建期覆盖

构建覆盖创建隔离设置资产，绝不会编辑 canonical 项目资产。解析顺序：clone canonical asset（不存在时创建内存默认对象），可选地复制项目内 `LoggerSettings` profile，应用所选 sink mode，应用单项 environment option，再应用单项 command-line option。同一字段由 command-line 值覆盖 environment 值。

| 环境变量 | 命令行参数 | 值 |
| --- | --- | --- |
| `CG_LOGGER_SETTINGS` | `-loggerSettings` | 项目内 `Assets/...` profile path |
| `CG_LOGGER_MODE` | `-loggerMode` | `Settings`、`Off`、`Unity`、`File` 或 `UnityAndFile` |
| `CG_LOGGER_UNITY` | `-loggerUnity` | Boolean |
| `CG_LOGGER_CONSOLE` | `-loggerConsole` | Boolean |
| `CG_LOGGER_FILE` | `-loggerFile` | Boolean |
| `CG_LOGGER_USE_PERSISTENT_DATA_PATH` | `-loggerUsePersistentDataPath` | Boolean |
| `CG_LOGGER_FILE_NAME` | `-loggerFileName` | 可移植 leaf name |
| `CG_LOGGER_CUSTOM_FILE_PATH` | `-loggerCustomFilePath` | 可选 fully qualified absolute path |
| `CG_LOGGER_LEVEL` | `-loggerLevel` | `LogLevel` name |
| `CG_LOGGER_FILTER` | `-loggerFilter` | `LogFilter` name |
| `CG_LOGGER_PROCESSING` | `-loggerProcessing` | `LoggerSettings.ProcessingMode` name |
| `CG_LOGGER_MAX_QUEUED_MESSAGES` | `-loggerMaxQueuedMessages` | 正整数 |
| `CG_LOGGER_UNITY_CONSOLE_MAX_QUEUED_MESSAGES` | `-loggerUnityConsoleMaxQueuedMessages` | 正整数 |
| `CG_LOGGER_SHUTDOWN_DRAIN_TIMEOUT_MS` | `-loggerShutdownDrainTimeoutMs` | 非负整数 |
| `CG_LOGGER_OVERFLOW_POLICY` | `-loggerOverflowPolicy` | Core `LogQueueOverflowPolicy` name |
| `CG_LOGGER_GUARANTEED_LEVEL` | `-loggerGuaranteedLevel` | 允许使用 reserved capacity 的 severity |

Boolean 接受 `true/false`、`1/0`、`yes/no`、`on/off` 和 `enabled/disabled`。显式存在但无效的值会使构建失败。

存在 override 时，preprocessing 创建 `Assets/Generated/CycloneGames.Logger/Resources/CycloneGames.Logger/LoggerSettingsBuildOverride.asset`。Player 先加载该 Resources key，再回退 canonical key；Editor 始终使用 canonical asset。`Library/CycloneGames.Logger/LoggerSettingsBuildOverride.marker.json` 的 transaction marker 记录 project identity、path、asset GUID、transaction 和 phase。Cleanup 只在 identity 验证后删除生成资产。无效 marker 或被未经验证内容占用的 path 会被保留并阻断构建，等待检查，而不是删除未知数据。

### Unity Editor 行为

- `LoggerSettingsEditor` 使用 `SerializedObject` 与 `SerializedProperty`，支持多对象编辑，并保持 Undo、asset serialization 与 Inspector workflow。
- Source link 将 caller path 与 line 嵌入 Unity Console 输出。点击链接会打开原始写日志的调用位置。Editor registry 有界为 2048 entry。
- Unity Console record 禁用 Unity 附加 stack trace，因为 logger 已包含 caller source 信息。
- Build override 使用生成资产，绝不修改 canonical source settings asset。

不要把 Unity Console 当作 shipping throughput sink。其格式化、Editor rendering、stack 处理与可见 Console 状态可能主导 timing 和 allocation 测量。

### 自定义 Timestamp Provider

`CLogger.ConfigureTimestampProvider` 安装自定义 UTC 时间来源。如果 provider 抛出非 `OutOfMemoryException`，logger 会增加 `TimestampProviderFailureCount`，在该实例剩余生命周期内绕过 provider，并回退到 `DateTime.UtcNow`。Circuit-breaker 每实例最多触发一次。

## 常见场景

### 热路径战斗日志

战斗系统需要按命中记录日志，且不希望每次调用产生 closure 或字符串插值：

```csharp
public static class CombatLog
{
    private static readonly Action<HitState, StringBuilder> AppendHit = AppendHitMessage;

    public static void Hit(int attackerId, int targetId, int damage)
    {
        if ((CLogger.Instance.GetLogLevel() & LogLevel.Debug) == 0) return;

        CLogger.LogDebug(new HitState(attackerId, targetId, damage), AppendHit, "Combat");
    }

    private static void AppendHitMessage(HitState s, StringBuilder b) =>
        b.Append("Attacker ").Append(s.AttackerId)
         .Append(" hit target ").Append(s.TargetId)
         .Append(" for ").Append(s.Damage).Append('.');
}
```

缓存的 `static` delegate 避免 closure；提前的 level 检查避免在 `Debug` 被过滤时执行调用。在 shipped build 中依赖此模式前，应在代表性硬件上测量真实 sink set。

### Dedicated Server 同时输出 stdout 与轮转文件

Headless server 需要 stdout 供容器采集，同时需要轮转文件用于事后分析：

```csharp
var options = new LoggerProcessingOptions
{
    MaxQueuedMessages = 8192,
    MaxQueuedCharacters = 4 * 1024 * 1024,
    OverflowPolicy = LogQueueOverflowPolicy.DropNewest,
    CriticalLevel = LogLevel.Error
};

CLogger logger = CLoggerFactory.CreateThreaded(options);
logger.AddLoggerUnique(new ConsoleLogger());
logger.AddLoggerUnique(new FileLogger("/var/log/mygame/server.log", new FileLoggerOptions
{
    MaintenanceMode = FileMaintenanceMode.Rotate,
    MaxFileBytes = 50L * 1024L * 1024L,
    MaxArchiveFiles = 10,
    FlushBatchSize = 128,
    FlushIntervalMs = 2000
}));
```

`UNITY_SERVER` 下 `registerUnityLogger` 默认为 `false`。容器编排应在 SIGTERM 时调用 `CLogger.Shutdown(LogFlushMode.Durable, timeoutMs)`，让 file sink 在进程退出前 drain。

### WebGL 单线程日志

WebGL 无法使用 threaded processing。Bootstrap 编译到 single-thread 路径，并把任何序列化的 `Block` policy 转为 `DropNewest`。宿主从 Unity `Update` loop pump 队列：

```csharp
public sealed class WebLogPump : MonoBehaviour
{
    private void Update()
    {
        CLogger.Instance.Pump(maxItems: 64);
    }
}
```

`FileLogger` 在 WebGL 上不支持。要把日志送出页面，需要实现一个有界 `ILogger`，缓存记录并通过单独拥有的 queue 发送到远端 endpoint。

### CI 构建管线覆盖

CI 构建希望启用文件日志、禁用 Unity Console，且不修改 canonical 资产：

```text
-playerSettings -loggerMode File -loggerUnity false -loggerFile true \
  -loggerCustomFilePath /build/logs/game.log -loggerLevel Info -loggerFilter LogAll
```

Preprocessing 创建 `LoggerSettingsBuildOverride.asset`。Canonical 资产在源码管理中保持不变。构建完成后，验证后的 transaction cleanup 删除生成资产；identity 不匹配会 fail-closed 并保留生成资产供检查。

## 性能与内存

核心队列按 `MaxQueuedMessages` 预分配 entry array。Unity handoff 预分配第二个 entry array。`LogMessage` 与 `StringBuilder` 使用有界进程级 cache。Oversized builder 和超出 cache limit 的 return 会被 discard，不会无限期保留。Unity subsystem registration 会清理 cache state。

以下情况仍可能分配：

- 调用方创建 string 或 interpolated string；
- delegate 捕获 state；
- cache miss 或 builder 扩容；
- 超限 string 被复制为有界 substring；
- sink 格式化或复制文本；
- Unity Console、file rotation、archive enumeration、exception 或平台 I/O 分配。

Performance test assembly 对四条具体 warm 路径提供 current-thread 稳态零分配断言：filtered cached builder、accepted cached builder 加同步 pump、accepted constant short string 加同步 pump，以及过载 `DropOldest` head replacement。这些测试只描述相应 Editor test 条件，不能证明 Player、IL2CPP、所有 sink、所有消息形态或所有平台都零分配。

热路径应按以下顺序处理：

1. 构造前过滤；
2. 使用带 cached static delegate 的 `Log<T>`；
3. 保持 category 简短稳定；
4. 通过真实 sink set 预热；
5. 测量 queue peak、drop 和 cache miss；
6. 聚合或采样高频诊断；
7. 在代表性硬件上 profile Development 与 Release Player。

大型 entity 规模下，未经测量的逐 entity、逐 tick 日志不可接受。应优先使用 counter、histogram、sampled trace 或 state-transition record。

### 线程

- 核心队列、registration snapshot、统计和内置 sink 保护真实并发路径。
- 自定义 sink 必须线程安全，因为 threaded processing 可以从 worker 调用它，而生命周期操作可能发生在其他线程。
- 线程安全不意味着可以在 `ILogger.Log` 中执行阻塞网络请求、压缩、上传或无界文件工作。这类工作必须放在单独拥有的有界 adapter queue 后面。

### 平台行为

| 目标 | 已实现路径 | 产品验证 |
| --- | --- | --- |
| Windows、Linux、macOS Player | `AutoDetect` 选择 threaded；Unity、Console、file sink 可配置 | Mono/IL2CPP、path permission、stdout、rotation、graceful quit、forced termination |
| iOS、Android | Threaded path；pause 请求 buffered flush | Suspend/kill、sandbox、quota、低存储、thermal effect |
| WebGL | 编译期 single-thread；不支持 `FileLogger` | Browser pump、memory、tab close、unload |
| Dedicated Server | `UNITY_SERVER` 禁用 Unity Console sink；Console 与 file sink 仍可配置 | Container/service shutdown hook、stdout、file quota、外部 rotation |
| 主机平台 | 不包含 proprietary SDK integration | 获得 SDK 后添加有界 adapter；验证 thread affinity、storage、认证规则 |

`FileLogger.IsSupported` 只编码 WebGL exclusion，不是 runtime permission、free-space、quota 或 storage-health probe。平台兼容必须由 build 与目标证据证明；Editor test 不能单独证明 IL2CPP/AOT、设备文件系统、浏览器、server soak 或主机认证行为。

### 持久化清单

| 数据 | 路径 | Owner |
| --- | --- | --- |
| Canonical settings | `Assets/Resources/CycloneGames.Logger/LoggerSettings.asset` | 项目；共享时提交 |
| Build override | `Assets/Generated/CycloneGames.Logger/Resources/CycloneGames.Logger/LoggerSettingsBuildOverride.asset` | Build transaction；不提交 |
| Build marker | `Library/CycloneGames.Logger/LoggerSettingsBuildOverride.marker.json` | Build processor；手动清理前检查 |
| Active runtime log | 默认 `Application.persistentDataPath/App.log`；UTF-8 without BOM | `FileLogger`；产品负责 quota、privacy、retention |
| Logger-owned archive | 与 active file 同目录；内部 name grammar | `FileLogger`；受 `MaxArchiveFiles` 限制 |

模块不使用 `EditorPrefs`、`PlayerPrefs` 或 `SessionState`。Runtime log file 是明文，可能包含应用传入的敏感数据。脱敏必须在记录到达 sink 前完成。

## 故障排查

| 现象 | 可能原因 | 解决方法 |
| --- | --- | --- |
| 没有输出 | 未注册 sink；level/filter 拒绝；settings 无效；bootstrap 抑制了无 sink 全局实例 | 确认已注册 sink，level/filter 接受记录，settings 资产校验通过 |
| 延迟 builder 不运行 | 被过滤、容量满或 lifecycle 已停止 | 检查 level/category、active sink、`DroppedNewestCount` |
| 出现 builder failure 记录 | Builder callback 抛异常 | 检查 `MessageBuilderFailureCount` 并修复 callback；`OutOfMemoryException` 单独传播 |
| Filter mutation 抛异常 | Key 过长或共享预算耗尽 | 检查 `RejectedFilterMutationCount`；减少 key 或提高经过测量的预算 |
| Custom timestamp 切换到 UTC | Provider 抛异常；circuit-breaker 触发 | 检查 `TimestampProviderFailureCount`；首次非 OOM failure 后 provider 被绕过 |
| Drop 增加 | 队列容量、sink latency 或日志速率超限 | 增加容量前比较消息/字符峰值、critical drop、sink latency |
| 主线程卡顿 | Core `Block`、慢 sink、无界 `Pump`、string-heavy 调用 | 避免主线程 `Block`；把慢 sink 移到单独拥有的 queue |
| Sink 消失 | Sink 连续异常达到阈值 | 检查 `SinkFailureCount`/`QuarantinedSinkCount`；恢复依赖后创建新 sink |
| Disposal 长期 pending | 阻塞 `Dispose` 串行阻塞后续工作 | 检查 `PendingSinkDisposalCount`；释放阻塞依赖 |
| Shutdown 超时 | 阻塞的同步 sink/disposal/reservation | 保留实例，释放依赖，重试正确的全局或实例 shutdown API |
| Unity flush 一直为 false | Unity handoff queue 未静止 | 检查 Unity handoff 的 queued/reserved/in-flight 占用，从主线程 drain |
| File health 为 degraded/faulted | Permission、quota、sharing、path validity | 检查 `LastFailure` 与 recovery counter；验证目标 sandbox |
| 文件增长超过预期 | `MaintenanceMode` 不是 `Rotate` | 确认 `Rotate`；`None` 与 `WarnOnly` 不限制 active-file size |
| WebGL 没有创建文件 | 符合预期 | 改用有界 browser 或 remote adapter |
| Build override 阻断构建 | Identity 不匹配或未验证 path 被占用 | 检查生成资产与 marker；fail-closed 保留数据供审查 |
| Custom file path 被拒绝 | Opt-in 未启用或 path 非 fully qualified | 启用 `allowCustomFilePath`，禁用 `usePersistentDataPath`，使用绝对路径 |

## 验证

运行功能与可靠性测试：

```text
<UnityEditor> -batchmode -nographics -projectPath <repo-root>/UnityStarter -runTests -testPlatform EditMode -assemblyNames CycloneGames.Logger.Tests.Editor -testResults <result-path> -quit
```

运行性能测试：

```text
<UnityEditor> -batchmode -nographics -projectPath <repo-root>/UnityStarter -runTests -testPlatform EditMode -assemblyNames CycloneGames.Logger.Tests.Performance -testResults <result-path> -quit
```

对每个支持的 target/backend，验证 startup selection、Console/stdout/file output、path permission、rotation、pause/resume、graceful quit、forced termination、burst drop、低存储 recovery 与 `LoggerShutdownResult`。使用 IL2CPP 时单独测试。WebGL 需要 browser main-thread 与 unload 检查；Dedicated Server 需要 service/container shutdown 与 stdout 检查；主机平台需要 SDK、devkit 和认证证据。

一个 Editor 环境中的测试通过只证明对应已测试契约，不能单独证明 Player、IL2CPP、真机、长时间运行、存储失败或跨平台行为。

## 示例

`Samples/README.md` 和 `Samples/README.SCH.md` 介绍隔离的 sample scene、最小日志 component、有限负载生成器、queue/cache monitor 和本地 benchmark harness。Sample 是教学与诊断辅助，不是生产 bootstrap code 或 shipping 性能目标。
