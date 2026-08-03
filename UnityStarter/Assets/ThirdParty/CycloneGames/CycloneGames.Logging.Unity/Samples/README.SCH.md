# CycloneGames.Logging.Unity 示例

这些示例演示 Unity 中的三层日志设计：生产者使用 `LogChannel`，应用 bootstrap 拥有 ambient `LogPipeline`，隔离的诊断工具可以创建自己的显式 pipeline 与 sink。

脚本编译进 `CycloneGames.Logging.Unity.Samples`。该可选 assembly 引用 `CycloneGames.Logging.Core`、`CycloneGames.Logging.Pipeline` 与 `CycloneGames.Logging.Unity`，并设置 `autoReferenced: false`，不属于生产 public API assembly。

Sample timing、allocation、queue peak 与文件行为会受到 Editor/Player、backend、硬件、Console 状态、存储、active sink 与 settings 影响。它们只是本地诊断观察。Shipping target 与平台认证需要单独的验证。

## 内容

| 文件 | 演示内容 | 所有权或副作用 |
| --- | --- | --- |
| `Diagnostics/LoggingSamplesLog.cs` | Assembly-local category 与 explicit/ambient `LogChannel` 创建 | 集中所有 sample channel 构造 |
| `LoggingSample.cs` | 最小化使用 project-owned bootstrap 的生产者 | 产生三条记录；不拥有 backend |
| `LoggingPerformanceTest.cs` | 使用缓存 state builder 的有限 mixed-severity load | 临时修改 minimum severity，并可能注册 file sink；destroy 时恢复并移除 |
| `LoggingPoolMonitor.cs` | Pipeline queue 与进程级 idle pool 观察 | 附加后两秒执行一次有界 burst |
| `LoggingBenchmark.cs` | 本地比较 disabled、no-sink、pipeline、burst、file 与 Unity Console path | 每个 case 拥有一个显式 single-threaded pipeline，强制 GC、执行 I/O 并写 report |
| `SampleScene.unity` | 承载示例 component | `Benchmark` active；`LoggingSample` 与 `PerformanceTest` inactive |

Scene 中没有 `LoggingPoolMonitor`，需要该练习时将它添加到临时 GameObject。

## 运行前准备

1. 打开：

   `UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Logging.Unity/Samples/SampleScene.unity`

2. 等待 `CycloneGames.Logging.Core`、`CycloneGames.Logging.Pipeline`、`CycloneGames.Logging.Unity` 与 `CycloneGames.Logging.Unity.Samples` 完成编译。
3. 创建或检查 `Assets/Resources/CycloneGames.Logging.Unity/LoggingSettings.asset`。
4. `Benchmark`、`LoggingSample` 与 `PerformanceTest` 每次只保留一个 active。
5. 进入 Play Mode，观察选定输出与统计；退出后检查 shutdown 或 disposal diagnostics。

默认 active 的 `Benchmark` 会强制执行 full garbage collection，并进行 file 与 Console I/O。采集其他应用性能数据前应禁用它。

## 教程 1：最小生产者

禁用 `Benchmark`，启用 `LoggingSample` GameObject，再进入 Play Mode。该 component 使用与业务包相同的生产者契约：

```csharp
private static readonly LogChannel Log = LoggingSamplesLog.Channel;

private void Start()
{
    Log.Info("Logging sample started.");
    Log.Warning("This is a warning example.");
    Log.Error("This is an error example.");
}
```

预期行为取决于 `LoggingSettings`：

- `minimumSeverity` 必须接纳记录；
- `categoryFilter` 必须接纳 `CycloneGames.Logging.Sample`；
- 至少有一个 active sink；
- `UnityConsoleLogSink` active 时，Unity Console 输出包含 source location。

该 component 不初始化、替换、flush 或 shutdown process writer。

## 教程 2：延迟构造热路径消息

Interpolated string 会在 backend 有机会拒绝前构造：

```csharp
Log.Debug($"Entity {entityId} updated.");
```

对于经过测量的热路径，将 state 单独传入，并使用 static 或缓存的 delegate：

```csharp
Log.Debug(
    entityId,
    static (value, builder) => builder.Append("Entity ").Append(value).Append(" updated."));
```

Pipeline 只在 severity、category、active sink、lifecycle 与 queue reservation 全部通过后调用 builder。该写法避免了示例中的捕获闭包与预格式化字符串，但不保证完整 sink path 零分配。

## 教程 3：有限混合级别负载

禁用其他 scenario，启用 `PerformanceTest`。`LoggingPerformanceTest` 会：

1. 要求当前 `LogRuntime.Writer` 是 `LogPipeline`；
2. 在 WebGL Player 之外创建 `Application.temporaryCachePath/CycloneGames.Logging/LoadExample.log`；
3. 通过 `RegisterSink(UniqueExactType)` 注册该 sink；如果注册后所有权仍属于示例，则立即 dispose；
4. 保存之前的 `MinimumSeverity`，再选择 `Trace`；
5. 每帧最多六条，覆盖全部 active severity，共提交恰好 10,000 条记录；
6. 在运行完成、组件停用或对象销毁时恢复之前的 minimum severity；
7. 只在 `RemoveSink(...)=true` 转回所有权后 dispose 临时 file sink；若静止等待超时，则在后续完成帧重试清理。

显示的 duration 测量跨帧 submission，不表示 sink delivery 或 durable persistence 已完成。解释结果前应检查：

- `LogPipeline.GetStatistics()` 中的 pipeline admission、processing 与 drop；
- `FileLogSink.Statistics` 与文件内容；
- 启用 Unity Console 输出时的 `UnityConsoleLogSink.GetStatistics()`；
- Unity Profiler 数据与目标存储行为。

WebGL 会跳过 file sink。停用或销毁 component 都会尝试执行幂等 cleanup。若完成时等待静止超时，component 会保持启用，并在后续帧继续重试 cleanup，成功后再停用自身。

## 教程 4：Queue 与 Pool Monitor

把 `LoggingPoolMonitor` 添加到临时 GameObject。两秒后它会自动提交 `BurstLogCount` 条 `Info` 记录，之后每隔 `MonitorIntervalSeconds` 报告一次。Context menu 也提供 `Run Bounded Burst Example` 与 `Show Logging Statistics`。

报告包括：

- 当前与峰值 pipeline queue 消息占用；
- 当前与峰值 pipeline 保留字符占用；
- pipeline total drop；
- 当前与峰值 cached `LogEvent` 和 `StringBuilder` 数量；
- pool miss。

Burst 仍受 active pipeline 的 count/character limit、reserved critical capacity 与 overflow policy 约束，可能正常丢弃记录。更深入诊断还应检查 reserved/in-flight field、critical drop、builder/timestamp failure、sink quarantine/disposal、filter budget 与独立 Unity handoff statistics。

Pool statistics 不是 heap profile，不包含 caller string、大多数 managed object、sink buffer、Unity Console storage、native/OS buffer 与 filesystem cache。

## 教程 5：本地比较 Harness

只启用 `Benchmark`。`LoggingBenchmark` 为每个隔离 case 创建显式 single-threaded `LogPipeline`，并直接把 `LogChannel` 绑定到该实例。它不会替换或停止 project-owned ambient writer。

Harness 预热 pool 后测量：

- filtered generic-state record；
- 无 sink 的 pipeline；
- 使用 `NullLogSink` 的 pipeline string、capturing-builder 与 generic-state builder case；
- 不进行中间 pumping 的 generic-state burst；
- WebGL Player 之外的 file output；
- Unity Console handoff。

它在 `Application.temporaryCachePath/CycloneGames.Logging/` 下写入 UTF-8 without BOM 文件：

- `LoggingBenchmarkReport.txt`；
- `LoggingBenchmarkFile.log`。

每个 pipeline 都会在进入下一个 case 前以五秒 buffered budget shutdown。Report 包含 elapsed time、派生 microseconds/record、派生 records/second、可用时的 current-thread allocation observation、Gen0 count、pool miss/discard 与 pipeline drop。

解释 report 时注意：

- case 执行不同工作，iteration count 也不同；
- harness 在 case 间强制 GC；
- `NullLogSink` 测量 pipeline 工作，不是生产 output sink；
- file 与 Unity Console case 包含各自 formatting、handoff 与 I/O cost；
- Console visibility/collapse、filesystem cache、antivirus、thermal state 与 Editor overhead 都会影响结果；
- `GC.GetAllocatedBytesForCurrentThread` 可能不可用，也不包含其他线程 allocation；
- harness 没有 confidence interval、standalone automation、device thermal protocol 或 multi-platform baseline。

聚焦 package regression case 使用 `CycloneGames.Logging.Pipeline.Tests.Performance`。Shipping evidence 仍需要固定 build、hardware、workload、warmup、sample count、storage state、thermal state 与 acceptance threshold。

## 自定义 Sink 练习

Sink 收到借用的 `LogEvent`，只能使用到 `Emit` 返回：

```csharp
using System.Text;
using CycloneGames.Logging;
using CycloneGames.Logging.Pipeline;

public sealed class ExampleSink : ILogSink
{
    private readonly object _syncRoot = new object();
    private readonly StringBuilder _scratch = new StringBuilder(256);

    public void Emit(LogEvent logEvent)
    {
        lock (_syncRoot)
        {
            _scratch.Clear();
            logEvent.AppendMessageTo(_scratch, escapeControlCharacters: true);
            // Consume or copy bounded data before returning.
        }
    }

    public void Dispose()
    {
    }
}
```

不要保留 `LogEvent`。UI、network、upload 或 platform-SDK handoff 需要自己的 copied payload、count 与 character/byte limit、overflow policy、drop statistics、thread affinity、flush behavior 与 shutdown owner。

## 所有权检查清单

- `LoggingBootstrap` 拥有它创建的 ambient Unity pipeline。
- `LoggingSample` 只产生记录。
- `LoggingPerformanceTest` 在成功注册前拥有新建 file sink；注册后所有权转移给 ambient pipeline。
- 只有成功的 `RemoveSink` 才把 file sink 转回调用方 dispose。
- `LoggingPerformanceTest` 在 teardown 时恢复自己修改的 minimum severity。
- `LoggingBenchmark` 拥有其创建的每个显式 pipeline 与 sink，并在丢弃前调用 `Shutdown`。
- Sample 不访问全局 pipeline 单例；ambient observation 使用 `LogRuntime.Writer`。

## 输出与清理

| 输出 | 持久化 | 清理 |
| --- | --- | --- |
| Unity Console record | 依赖 Editor/Player，不是 durable log | 通过正常 Console workflow 清理 |
| `LoadExample.log` | `temporaryCachePath` 中的 UTF-8 明文 | `PerformanceTest` teardown 后删除 |
| `LoggingBenchmarkReport.txt` | `temporaryCachePath` 中的 UTF-8 明文 | 检查后删除 |
| `LoggingBenchmarkFile.log` | `temporaryCachePath` 中的 UTF-8 明文 | Benchmark shutdown 后删除 |

不要提交这些输出。它们可能包含 source location 与 sample/application data。操作系统可能随时清理 `temporaryCachePath`。

## 验证与故障排查

最小 sample 验证：

1. 使用 sample output 诊断前，运行 `CycloneGames.Logging.Unity.Tests.Editor`。
2. 每次只运行一个 scenario。
3. 记录 Editor/Player、scripting backend、target、hardware、build type、settings、sink set 与 Console state。
4. 检查 pipeline 与 Unity handoff 的 drop/peak counter。
5. 确认 owning scenario 停止后，临时文件可以打开并删除。
6. 在代表性硬件的 standalone Player 中重复性能调查；使用 IL2CPP 时单独测试。

| 现象 | 处理 |
| --- | --- |
| 没有 sample record | 检查 active sink、`minimumSeverity`、`categoryFilter` 与 bootstrap initialization status |
| Load sample 自动禁用 | 确认 `LogRuntime.Writer` 是 Unity bootstrap 拥有的 `LogPipeline` |
| Drop counter 增加 | 视为 overload evidence；调整容量前检查 count 与 character peak |
| WebGL 没有 sample file | 符合预期；WebGL Player 排除 file path |
| Allocation 显示不可用或零 | Counter 不可用或结论不足；使用 Profiler 与目标平台工具 |
| Timing 很大或不稳定 | 减少无关 Editor/Console 工作，再迁移到受控 Player protocol |
| 临时文件无法写入 | 检查 sandbox、quota、permission、sharing 与 `FileLogSink.Statistics` |

完整 lifecycle、build override、platform behavior、persistence 与 shipping validation 见 package-level `README.md` 或 `README.SCH.md`。
