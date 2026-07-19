# CycloneGames.Logger 示例

Sample scene 通过小而隔离的步骤讲解 Logger 工作流：写入普通记录、使用关注分配的 builder API、观察 queue/cache 状态、挂载临时 file sink，以及运行本地对比 harness。

示例脚本编译到 `CycloneGames.Logger.Samples`。该程序集引用 `CycloneGames.Logger` 与 `CycloneGames.Logger.Unity`，设置 `autoReferenced: false`，不属于生产 public API surface。

Sample 是教学与诊断工具。Timing 和 allocation 会受 Editor/Player、backend、硬件、Console 状态、存储、active sink 与当前 settings 影响。它们不是 shipping 性能目标、通用容量建议或平台认证证据。

## 示例内容

| 文件 | 演示内容 | 重要副作用 |
| --- | --- | --- |
| `LoggerSample.cs` | 最小 `CLogger.LogInfo`、`LogWarning` 与 `LogError` 用法 | 使用项目拥有的 Unity bootstrap；不创建或停止 logger |
| `LoggerPerformanceTest.cs` | 使用 state 加 cached/static builder 的有限混合 level 负载 | WebGL 之外注册临时 file sink，并把全局 level 改为 `Trace` |
| `LoggerPoolMonitor.cs` | Queue 消息/字符占用与进程级 cache 观察 | 通过 `Debug.Log` 展示，并可提交有界 burst |
| `LoggerBenchmark.cs` | 对 filtered、no-sink、core、file 与 Unity Console path 进行本地比较 | 重新配置/停止全局 logger、强制 GC、执行 I/O 并写入 report |
| `SampleScene.unity` | 承载示例 component | 默认启用 `Benchmark`；禁用 `LoggerSample` 与 `PerformanceTest` |

`LoggerPoolMonitor` 未放入 scene。需要检查 queue 与 cache 时，将它添加到临时 GameObject。

## 运行示例前

1. 打开 `UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Logger/Samples/SampleScene.unity`。
2. 等待 `CycloneGames.Logger`、`CycloneGames.Logger.Unity` 与 `CycloneGames.Logger.Samples` 无错误编译。
3. 如果不存在 `Assets/Resources/CycloneGames.Logger/LoggerSettings.asset`，创建并校验它。
4. `Benchmark`、`LoggerSample` 与 `PerformanceTest` 只保留一个 active。
5. 进入 Play Mode，观察对应输出；退出后检查 shutdown 或 disposal error。

`LoggerBenchmark` 在隔离运行期间拥有全局 Logger reconfiguration。包含其他全局 logger owner 或 consumer 的 Scene 中不能启用它。

## 教程 1：最小 Unity 日志

启用 `LoggerSample` GameObject，并禁用其他 scenario。该 component 依赖 `LoggerBootstrap`，只包含普通应用调用：

```csharp
private void Start()
{
    CLogger.LogInfo("Logger sample started.", "Sample");
    CLogger.LogWarning("This is a warning example.", "Sample");
    CLogger.LogError("This is an error example.", "Sample");
}
```

预期结果：

- active settings asset 决定 sink set；
- 默认 `Info` threshold 接受三条记录；
- `Sample` 显示为 category；
- `UnityLogger` active 时，Unity Console 输出包含 source link。

没有记录时，检查 `registerUnityLogger`、`defaultLevel`、`defaultFilter` 与 Console filter。

## 教程 2：关注分配的消息构造

Interpolated string 会在 logger 过滤前创建：

```csharp
CLogger.LogDebug($"Entity {entityId} updated.", "Simulation");
```

对已经测量的热路径，单独传递 state，并使用 static 或 cached delegate：

```csharp
CLogger.LogDebug(
    entityId,
    static (value, builder) => builder.Append("Entity ").Append(value).Append(" updated."),
    "Simulation");
```

Builder 只在 level、category、sink、lifecycle 与 queue-reservation 检查成功后运行。示例调用避免 capturing closure，但不保证完整路径零分配。Pool miss、builder 扩容、sink 格式化、Unity Console copy、exception 和 I/O 仍可能分配。

## 教程 3：有限混合 Level 负载

启用 `PerformanceTest` 并禁用其他 scenario。`LoggerPerformanceTest` 会：

1. 在 WebGL 之外，于 `Application.temporaryCachePath` 下创建 `FileLogger`；
2. 通过 `AddLoggerUnique` 注册；
3. 将全局 level 设为 `Trace`；
4. 提交最多 10000 条、覆盖六个 active severity 的记录；
5. 只有 `RemoveLogger` 返回 `true` 时才移除并 Dispose file sink。

输出文件为：

`Application.temporaryCachePath/CycloneGames.Logger/LoadExample.log`

显示的 elapsed time 包含跨帧提交。Frame rate、active sink、queue drop、Unity Console、存储、Editor overhead 与 scheduling 都会影响它。不得作为 Logger throughput 报告。得出本地结论前应检查：

- `CLogger.Instance.GetProcessingStatistics()`；
- `FileLogger.Statistics`；
- Unity Profiler 数据；
- 文件内容与最终 byte count。

WebGL 不支持 `FileLogger`，因此会跳过 file sink。

## 教程 4：观察 Queue 与 Cache

将 `LoggerPoolMonitor` 添加到临时 GameObject。它报告：

- 当前与峰值 core queue message occupancy；
- 当前与峰值 retained-character occupancy；
- core total drop；
- 当前与峰值 cached `LogMessage`/`StringBuilder` 数；
- cache miss。

使用 `Run Bounded Burst Example` context menu，通过 static state-builder callback 提交 `BurstLogCount` 条记录。Burst 始终受 active `LoggerProcessingOptions` 管理：记录可能被拒绝或驱逐，reserved critical capacity 可以减少普通竞争，但不保证必达。

该 component 有意只展示较小 subset。高级诊断还应查询：

- `ReservedCount`、`InFlightCount` 及对应 character 字段；
- `MessageBuilderFailureCount` 与 `TimestampProviderFailureCount`；
- filter occupancy 与 rejected mutation；
- sink failure、quarantine 与 disposal counter；
- 第二层 Unity handoff 的 `UnityLogger.GetStatistics()`。

Logger cache statistics 不是 heap profile。它不包括 caller string、大多数 object、sink buffer、Unity Console storage、native/OS buffer 与 filesystem cache。完整内存调查应使用 Unity Memory Profiler 和目标平台工具。

## 教程 5：本地 Benchmark Harness

启用 `Benchmark` 并禁用所有其他 scenario。Harness 运行：

- 直接 `UnityEngine.Debug.Log` 输出；
- filtered generic logging；
- 已初始化但没有 sink 的 logger；
- core string、capturing builder 与 generic state-builder case；
- 不进行中间 pump 的 burst；
- WebGL 之外的 file output；
- Unity Console handoff。

它将 UTF-8 without BOM 写到：

- `Application.temporaryCachePath/CycloneGames.Logger/LoggerBenchmarkReport.txt`
- `Application.temporaryCachePath/CycloneGames.Logger/LoggerBenchmarkFile.log`

Report 包含 elapsed time、派生 microseconds/log、派生 logs/second、runtime 支持时的 current-thread allocation 观察、Gen0 collection count、pool miss/discard 和 core drop。

解释 report 时需要注意：

- case 使用不同 iteration count，并包含不同工作；
- harness 为受控 caller-pumped case 选择 single-thread processing；
- `NullLogger` 测量 core dispatch，不是生产 sink；
- Unity Console 与 file case 包含各自格式化和 I/O 成本；
- 强制 GC、coroutine yield、Console visibility/collapse、filesystem cache、杀毒软件与 thermal state 会影响结果；
- `GC.GetAllocatedBytesForCurrentThread` 可能不可用，也不包括其他线程上的 allocation；
- harness 没有 standalone Player automation、confidence interval、device thermal protocol 或 multi-platform baseline。

可重复 package-level 回归 case 使用 `CycloneGames.Logger.Tests.Performance`。Shipping 性能证据需要单独协议，固定 build、硬件、workload、warmup、sample count、storage state、thermal state 和 acceptance threshold。

## 高级练习：安全的自定义 Sink 边界

Custom sink 收到借用的 `LogMessage`，只能读取到 `Log` 返回。只复制下一个 owner 真正需要的数据。

```csharp
using System.Text;
using CycloneGames.Logger;

public sealed class ExampleSink : ILogger
{
    private readonly object _syncRoot = new object();
    private readonly StringBuilder _scratch = new StringBuilder(256);

    public void Log(LogMessage message)
    {
        lock (_syncRoot)
        {
            _scratch.Clear();
            message.AppendMessageTo(_scratch, escapeControlCharacters: true);
            // Consume or copy the bounded text before returning.
        }
    }

    public void Dispose()
    {
    }
}
```

不要保留 `LogMessage`。用于 UI、网络、upload 或 platform SDK 的复制 handoff 必须拥有自身消息数与 byte/character limit、overflow policy、drop statistics、thread affinity、flush behavior 和 shutdown owner。Source line number 应使用 invariant culture 或等效 invariant integer routine 格式化。

## 示例所有权清单

- Project bootstrap 拥有全局 `CLogger`。
- `LoggerSample` 只生产记录。
- `LoggerPerformanceTest` 临时拥有 `FileLogger`，直到成功注册把所有权转移给 `CLogger`。
- 只有 `RemoveLogger(...)=true` 才把该 sink 转回调用方 Dispose。
- `LoggerBenchmark` 在隔离执行期间拥有全局配置，并调用 `CLogger.Shutdown`。
- Benchmark 不能与另一个全局 Logger owner 同时运行。

## 输出与清理

| 输出 | 持久化 | 清理 |
| --- | --- | --- |
| Unity Console record | 依赖 Editor/Player | 正常清空；不是 durable record |
| `LoadExample.log` | `temporaryCachePath` 下的明文 UTF-8 | `LoggerPerformanceTest` 停止后可安全删除 |
| `LoggerBenchmarkReport.txt` | `temporaryCachePath` 下的明文 UTF-8 | 检查后可安全删除 |
| `LoggerBenchmarkFile.log` | `temporaryCachePath` 下的明文 UTF-8 | Benchmark 停止后可安全删除 |

不要提交这些文件。它们可能包含 source location 和 sample/application 数据。操作系统可以随时清理 `temporaryCachePath`。

## 验证与故障排查

最小 sample 验证：

1. 使用 sample output 进行诊断前，运行 `CycloneGames.Logger.Tests.Editor`。
2. 每次只运行一个 scenario。
3. 记录 Editor/Player、backend、target、硬件、build type、settings、sink set 与 Console state。
4. 确认 core 与 Unity handoff drop counter 符合 scenario 预期。
5. 确认临时文件可打开、flush，并在 Play Mode 后删除。
6. 在 standalone Player 与代表性硬件上重复性能调查；使用 IL2CPP 时单独测试。

| 现象 | 处理 |
| --- | --- |
| 没有 sample record | 启用 sink，检查 level/filter，并确认 benchmark 没有停止全局 logger |
| Sample 互相干扰 | 只保留一个 scenario active，并重新进入 Play Mode，让 subsystem registration 重置 static state |
| Drop counter 增加 | 视为过载证据；修改容量前检查 count/character peak 与 Unity handoff statistics |
| WebGL 没有 sample file | 符合预期；Player path 会排除 file sample code |
| Allocation 显示 `N/A` 或零 | Counter 不可用或结论不足；使用 Profiler 与平台工具 |
| Timing 很大或不稳定 | 减少无关 Editor/Console 工作，再把实验转到受控 standalone Player protocol |
| 临时文件无法写入 | 检查 sandbox、quota、permission、sharing 与 `FileLogger.Statistics` |

完整配置、生命周期、平台、持久化、性能与自定义 sink 指南请阅读 package-level `README.md` 或 `README.SCH.md`。
