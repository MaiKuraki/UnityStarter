# CycloneGames.Scheduling

[English](README.md) | 简体中文

CycloneGames.Scheduling 把可延迟的工作分摊到多帧。你交给它一批「能分小步推进」的任务，它每帧执行有限步数，其余留在队列里。

它不是通用 Job 系统：单线程运行、容量固定、已经在跑的 Step 无法被打断。

## 目录

- [适用场景](#适用场景)
- [快速开始](#快速开始)
- [通道与饥饿上界](#通道与饥饿上界)
- [工作项契约](#工作项契约)
- [预算](#预算)
- [诊断](#诊断)
- [队列已满](#队列已满)
- [取消与句柄](#取消与句柄)
- [故障处理](#故障处理)
- [关机](#关机)
- [Unity 宿主](#unity-宿主)
- [依赖注入与所有权](#依赖注入与所有权)
- [Factory 集成](#factory-集成)
- [存储、分配与线程](#存储分配与线程)
- [常见错误](#常见错误)
- [测试](#测试)

## 适用场景

批量处理、对象池维护、内容校验、缓存整理，以及其他「晚一帧完成也没关系」的工作。

不要用于输入、物理、确定性模拟、帧同步、音频 DSP，或已经有 Job 依赖图的代码。

调度器只搬运开销，不减少总工作量，也无法打断某个 Step 内的长同步调用。

## 快速开始

```csharp
using CycloneGames.Scheduling.Core;

var scheduler = new WorkScheduler(capacity: 128);
var work = new BatchWorkItem(new int[1000], batchSize: 4);

scheduler.TrySchedule(work, WorkLane.Normal, out WorkItemHandle handle);
scheduler.Tick(maxSteps: 8, softBudgetMs: 2.0);
```

`Tick` 每次最多执行 `maxSteps` 项，每帧调用一次。`TrySchedule`、`Cancel`、`ShutdownStep`、`Shutdown`、`GetSnapshot` 必须在创建调度器的同一线程调用。

上面用到的工作项：

```csharp
sealed class BatchWorkItem : IWorkItem
{
    private readonly int[] data;
    private readonly int batchSize;
    private int index;

    public BatchWorkItem(int[] data, int batchSize)
    {
        this.data = data;
        this.batchSize = batchSize;
    }

    public WorkStepResult Step()
    {
        int end = Math.Min(data.Length, index + batchSize);
        while (index < end)
        {
            data[index]++;
            index++;
        }

        return index == data.Length ? WorkStepResult.Completed : WorkStepResult.Running;
    }

    public void OnTerminated(WorkTerminationReason reason)
    {
    }
}
```

## 通道与饥饿上界

四个通道，从 `Immediate` 到 `Background` 依次排空：

| 通道 | 用途 |
| --- | --- |
| `Immediate` | 必须本帧推进，可越过低优先通道。 |
| `High` | 时效敏感，可容忍一到两帧。 |
| `Normal` | 默认通道。 |
| `Background` | 空闲期维护：缓存清扫、池裁剪、索引重建。 |

最高优先的非空通道先被排空。永不排空的通道也无法饿死其他通道：任何非空通道至少每 `StarvationLimitTicks` 个 Tick（默认 4）获得一次执行机会。

```csharp
scheduler.TrySchedule(criticalWork, WorkLane.Immediate, out _);
scheduler.TrySchedule(cacheSweep, WorkLane.Background, out _);
```

同一通道内保持 FIFO，返回 `Running` 的项回到本通道队尾，因此同一项在单个 Tick 内不会执行两次。

## 工作项契约

```csharp
public interface IWorkItem
{
    WorkStepResult Step();
    void OnTerminated(WorkTerminationReason reason);
}
```

`Step` 返回 `Running` 表示还有后续工作，返回 `Completed` 表示完成。进度与中间结果由工作项自己保存。

每个被接受的注册恰好触发一次 `OnTerminated`：

| 原因 | 含义 |
| --- | --- |
| `Completed` | `Step` 返回了 `Completed`。 |
| `Cancelled` | 被 `Cancel` 取消。 |
| `Faulted` | `Step` 抛出异常或返回了未定义值。 |
| `Shutdown` | 关机终止了剩余工作。 |

`TrySchedule` 返回 `false` 表示注册未被接受，不会触发 `OnTerminated`，工作项仍归调用方所有。

同一实例不能同时入队两次。Editor 与 Development 构建下重复注册会立即抛异常；Release Player 构建不包含该检查，契约由调用方自行保证。

## 预算

```csharp
scheduler.Tick(maxSteps: 64, softBudgetMs: 2.0);
```

`maxSteps` 限制单个 Tick 的 `Step` 调用次数；`softBudgetMs` 按时间限制准入。调度器在启动后续 Step 前读时钟，但无法打断正在执行的 Step，因此预算只约束准入，不约束实际耗时。

预算设为正无穷表示关闭按时间准入，只保留步数上限。

不要假设预算一定守住，读计数器：

```csharp
SchedulerSnapshot snapshot = scheduler.GetSnapshot();
long overBudgetTicks = snapshot.OverBudgetTickCount;
double lastTickMs = snapshot.LastTickMilliseconds;
```

## 诊断

`SchedulerSnapshot` 把四种终止原因分开计数，运维侧可以区分「调用方主动取消」与「关机清空」：

```csharp
long cancelled = snapshot.CancelledCount;   // 显式 Cancel
long drained   = snapshot.ShutdownCount;    // Shutdown / ShutdownStep
long faulted   = snapshot.FaultedCount;     // Step 抛出异常
```

单步归因需要显式开启，因为它会在每一步多付一次时钟换算：

```csharp
scheduler.TrackStepTiming = true;   // 诊断时开启，之后关掉

double worst = snapshot.SlowestStepMilliseconds;
WorkItemHandle culprit = snapshot.SlowestStep;   // 仅诊断用途；槽位被复用后不再解析
```

`TrackStepTiming` 关闭时这两个字段恒为 0。

`GetSnapshot` 允许在 Step 内部调用，工作项可以上报自身进度而不破坏调度器状态。

## 队列已满

容量在构造时固定。队列满时 `TrySchedule` 返回 `false` 与默认句柄，并递增 `RejectedCount`；已接受的项不会被静默丢弃。

如果经常满队列，应降低生产速度、按通道分流或合并工作项。加大容量只会增加内存与排队延迟。

## 取消与句柄

```csharp
if (scheduler.Cancel(handle))
{
    // 该项不会再收到 Step 调用。
}
```

`Cancel` 为 O(1)，并且在调用 `OnTerminated(Cancelled)` 之前就已释放内部引用。

句柄携带槽位、世代与签发调度器的身份。默认、过期、已释放或来自其他调度器的句柄一律返回 `false`，跨调度器误用不会再取消掉无关的工作项。

## 故障处理

`Step` 抛异常只会让该注册以 `Faulted` 终止。同一 Tick 内其余项仍会各执行一步，随后统一抛出：只有一个失败时抛出原始异常，多个失败时抛出 `AggregateException`。

这样单个坏项不会抢走其他项本帧的预算。

不要在 `Step` 或 `OnTerminated` 内调用 `TrySchedule`、`Cancel`、`Tick`、`ShutdownStep`、`Shutdown`、`Dispose`（`GetSnapshot` 除外）。

## 关机

```csharp
scheduler.Shutdown();
```

停止准入并同步终止全部剩余项，先尝试所有回调再抛出聚合异常，可安全重复调用。

队列较大时应分批排空：

```csharp
scheduler.ShutdownStep(0);  // 先停止准入，暂不取消。
scheduler.ShutdownStep(32); // 最多终止 32 项，不执行 Step。
```

`Dispose` 同样会排空，但不会因回调失败而抛出；线程错误或从 Step 内调用这类误用仍会抛出。

## Unity 宿主

`SchedulingDriver` 需要你手动挂到 GameObject 上，包本身不创建运行时对象。

| 设置 | 含义 |
| --- | --- |
| `Capacity` | 固定注册容量，改变需重建宿主。 |
| `Max Steps Per Frame` | 每帧的 Step 或关机终止次数，0 表示暂停正常工作。 |
| `Soft Budget Milliseconds` | 软预算，0 表示暂停正常工作。 |
| `Max Budget Milliseconds` | 软预算的硬上限，即软预算设为无穷也不会卡住一帧。 |

`Update` 驱动正常工作或有界关机。禁用组件只暂停队列，不取消。步数预算为 0 不会卡住关机：排空每帧至少推进一项，`IsShutdownDrainForced` 可用于判断该覆盖是否生效。

## 依赖注入与所有权

非 DI 场景直接创建；DI 场景由容器持有并绑定到宿主：

```csharp
var scheduler = new WorkScheduler(capacity: 256);
driver.Bind(scheduler);
```

宿主仍有未完成工作时拒绝 `Bind`；已排空的调度器可以被替换。

| 操作 | 宿主自建调度器 | 绑定的外部调度器 |
| --- | --- | --- |
| 驱动 Update | 执行工作 | 执行工作 |
| `driver.BeginShutdown()` | 开始有界关机 | 拒绝 |
| `driver.Shutdown()` / `OnDestroy` | 执行关机 | 仅解绑，不取消 |

## Factory 集成

Factory 集成把既有的有界池维护调用适配到调度器上，仅在安装 `com.cyclone-games.factory` 时编译。预热适配器把池报告的数量钳制到请求范围内，因此池无法造成负数请求或超出请求数量。

## 存储、分配与线程

调度器在构造时一次性分配槽位数组与四个通道数组。每个注册项占用：

- 每槽位 24 字节，且不再需要独立的空闲槽位数组。

`TrySchedule`、`Cancel`、重新入队均为 O(1)；关机为 O(终止项数)，大队列请用 `ShutdownStep` 分批。

正常调度、取消、重新入队与有界关机在构造之后不产生托管分配；异常路径会分配。

调度器限定在属主线程。它不是线程安全的，也不会把工作线程的任务编组进来，更不会让 Unity API 变成可跨线程调用。跨线程生产方需要自带同步设计的有界适配器。

## 常见错误

| 错误 | 修正 |
| --- | --- |
| 在单个 Step 内做长同步操作 | 拆成有界的多个步骤。 |
| 同一实例入队两次 | 等它终止，或改用不同实例。 |
| 所有工作都放 `Immediate` | 只留给必须本帧完成的工作，其余靠饥饿上界保障。 |
| 在 `Step` 或 `OnTerminated` 内调用调度器 | 返回后由属主下一帧处理，`GetSnapshot` 除外。 |
| 以为 `maxSteps` 能限制回调开销 | 每个域单独做预算。 |
| 池的维护工作未停止就销毁池 | 先停止并排空。 |

## 测试

EditMode 与 PlayMode 用例覆盖通道优先级、饥饿上界、故障隔离、句柄身份、预算边界、重入、终止回调、关机计数、Factory 适配器、Unity Update 与销毁，以及外部所有权。

EditMode 还包含确定性性能门槛：10 万次操作的稳态分配、每注册项占用、30 万次操作的压力运行，以及基于反射的公共 API 形状检查。它们只断言分配与布局，不断言墙钟时间。

```bash
<unity-exe> -batchmode -nographics -projectPath <repo-root>/UnityStarter -runTests -testPlatform EditMode -testFilter CycloneGames.Scheduling -testResults <output.xml> -logFile <output.log>
```

性能数字取决于设备、Unity 版本、后端、构建类型、负载与裁剪设置，Editor 测试不能代表目标 Player 的性能。
