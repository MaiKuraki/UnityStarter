# CycloneGames.Scheduling

[English | 简体中文](README.md)

CycloneGames.Scheduling 把允许延迟的工作分散到多个帧。你提交能按小步推进的项，调度器每次更新只执行有限步数，其余继续排队。

它不是通用任务系统。它运行在单线程上，使用固定大小的队列，也不能打断已经开始执行的 Step。

## 目录

- [用途](#用途)
- [快速开始](#快速开始)
- [工作项契约](#工作项契约)
- [预算与公平性](#预算与公平性)
- [队列已满](#队列已满)
- [取消与终止](#取消与终止)
- [关闭](#关闭)
- [Unity Host](#unity-host)
- [DI 与所有权](#di-与所有权)
- [Factory 集成](#factory-集成)
- [存储与分配](#存储与分配)
- [线程与 IO 边界](#线程与-io-边界)
- [常见错误](#常见错误)
- [测试](#测试)

## 用途

适合批量处理、pool 维护、内容校验、缓存维护，以及任何允许晚一帧完成的工作。

不要用它处理输入、物理、确定性模拟、网络 lockstep、音频 DSP，也不要替代已有的 Job 依赖图。

调度器只是把成本从一帧挪到多帧。它不减少总工作量，也不能中断一个 Step 里长时间执行的同步调用。

它不是用来消除所有帧尖峰的。单个 Step 过长造成的尖峰只有拆开 Step 才能解决；渲染、GPU 提交或 GC 造成的尖峰不在本模块范围内。

## 快速开始

```csharp
using System;
using CycloneGames.Scheduling.Core;

public sealed class BatchWorkItem : IWorkItem
{
    private readonly int[] data;
    private readonly int batchSize;
    private int index;

    public int ProcessedCount => index;
    public WorkTerminationReason? TerminationReason { get; private set; }

    public BatchWorkItem(int[] data, int batchSize = 4)
    {
        this.data = data ?? throw new ArgumentNullException(nameof(data));
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }

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
        TerminationReason = reason;
    }
}
```

在创建 scheduler 的线程上驱动它：

```csharp
var scheduler = new WorkScheduler(capacity: 128);
var work = new BatchWorkItem(new int[1000], batchSize: 4);

if (scheduler.TrySchedule(work, out WorkItemHandle handle))
{
    // 从这里开始由 scheduler 持有该注册。
}

scheduler.Tick(maxSteps: 8, softBudgetMs: 2.0);
```

`Tick` 一次最多执行 `maxSteps` 项。每次更新调用一次。`TrySchedule`、`Cancel`、`ShutdownStep`、`Shutdown`、`GetSnapshot` 都要在同一个线程上调用。

## 工作项契约

```csharp
public interface IWorkItem
{
    WorkStepResult Step();
    void OnTerminated(WorkTerminationReason reason);
}
```

还有工作要做时 `Step` 返回 `Running`，完成时返回 `Completed`。进度和部分结果由工作项自己保存。

每个被接受的注册，最终只会调用一次 `OnTerminated`：

| 原因 | 含义 |
| --- | --- |
| `Completed` | `Step` 返回了 `Completed`。 |
| `Cancelled` | `Cancel` 先停止了工作项。 |
| `Faulted` | `Step` 抛异常或返回了非法值。 |
| `Shutdown` | Shutdown 停止了剩余工作。 |

`TrySchedule` 返回 `false` 时，注册没有被接受，也不会调用 `OnTerminated`，工作项仍归调用方。

同一个实例不能同时排队两次。

## 预算与公平性

```csharp
scheduler.Tick(maxSteps: 8, softBudgetMs: double.PositiveInfinity);
```

`maxSteps` 限制每次 Tick 的 `Step` 调用次数。它不限制单个 Step 执行多久。

`softBudgetMs` 按时间限制准入。调度器在启动后续 Step 前检查时钟，但无法停止正在运行的 Step，所以一次 Tick 仍可能超预算。

```csharp
scheduler.Tick(maxSteps: 64, softBudgetMs: 2.0);
```

不要假设预算一定被满足，直接读计数器：

```csharp
SchedulerSnapshot snapshot = scheduler.GetSnapshot();
long overBudgetTicks = snapshot.OverBudgetTickCount;
double lastTickMs = snapshot.LastTickMilliseconds;
```

返回 `Running` 的项回到队尾。以 `A`、`B`、`C` 三项、Step 预算为 2 为例：

| Tick | 执行 | 之后队列 |
| --- | --- | --- |
| 1 | `A`、`B` | `C`、`A`、`B` |
| 2 | `C`、`A` | `B`、`C`、`A` |
| 3 | `B`、`C` | `A`、`B`、`C` |

一个工作项不会在一次 Tick 里执行两次。

## 队列已满

容量在构造时固定。队列满时 `TrySchedule` 返回 `false` 和 default handle，并增加 `RejectedCount`。已接受的工作不会被静默丢弃。

保留同一个实例，稍后重试：

```csharp
if (!scheduler.TrySchedule(work, out handle))
{
    // 用同一个实例在之后的 update 中重试。
}
```

如果经常满队列，就减少生产或合并工作项。加大容量只会增加内存和排队延迟。

## 取消与终止

```csharp
if (scheduler.Cancel(handle))
{
    // 该工作项不会再收到 Step 调用。
}
```

`Cancel` 是 O(1)。调度器先释放自己的引用，再调用 `OnTerminated(Cancelled)`。default、失效或已释放的句柄返回 `false`。

`GetSnapshot` 报告计数器，满足：

```text
AcceptedCount == CompletedCount + FaultedCount + CancelledCount + OutstandingCount
```

`RejectedCount` 单独统计，因为被拒绝的工作从未被接受。

`Step` 抛异常时，工作项变为 `Faulted`，异常向上传播，其余工作项继续排队。`Step` 或 `OnTerminated` 抛异常不会留下半释放的注册。

不要在 `Step` 或 `OnTerminated` 里调用 `TrySchedule`、`Cancel`、`Tick`、`ShutdownStep`、`Shutdown` 或 `GetSnapshot`。

## 关闭

最终清理：

```csharp
scheduler.Shutdown();
```

它停止接收并同步终止所有剩余工作项。即使个别回调失败，也会先尝试全部回调，再抛出聚合异常。重复调用是安全的。

队列较大时，分批 drain：

```csharp
scheduler.ShutdownStep(0);  // 停止接收，先不取消任何项。
scheduler.ShutdownStep(32); // 最多终止 32 项，不执行 Step。
```

停止接收后 `IsShutdown` 立即为 true；队列清空后 `IsShutdownCompleted` 才为 true。回调异常按批收集，后续 `ShutdownStep` 继续 drain。数量预算不限制单个回调的耗时。

## Unity Host

自己把 `SchedulingDriver` 加到 GameObject 上。包不会自动创建运行时对象。

| 设置 | 含义 |
| --- | --- |
| `Capacity` | 固定注册容量，修改后需要重建 Host。 |
| `Max Steps Per Frame` | 每次 Update 的 Step 或关闭终止次数，0 同时暂停两者。 |
| `Soft Budget Milliseconds` | 软时间预算，0 暂停正常工作。 |

注册工作：

```csharp
using CycloneGames.Scheduling.Core;
using CycloneGames.Scheduling.Runtime;

public static class SchedulingExample
{
    public static void Configure(SchedulingDriver driver, IWorkItem work)
    {
        driver.Scheduler.TrySchedule(work, out _);
    }
}
```

`Update` 驱动正常工作或分批关闭。禁用组件会暂停队列但不取消。`OnApplicationQuit` 和 `OnDestroy` 会立即处理剩余工作。

对 Host 自己创建的 scheduler，`driver.BeginShutdown()` 会把 Update 从执行 Step 切换到分批终止。drain 期间销毁 Host，剩余部分仍会立即清理。

销毁后的 driver 在 Unity 中是 fake-null。销毁后访问 `driver.Scheduler` 会抛异常，不会重新创建 scheduler。Unity 可能在请求销毁的 Step 之后才真正销毁对象，所以 fake-null 检查和主线程所有权要留在 Unity integration 代码里。

## DI 与所有权

非 DI 环境直接创建：

```csharp
var scheduler = new WorkScheduler(capacity: 256);
// 由你控制的 owner 驱动 Tick。
```

DI 环境让 container 持有 scheduler，再绑定到 Host：

```csharp
var scheduler = new WorkScheduler(capacity: 256);
driver.Bind(scheduler);
```

只有 driver 自己的 scheduler 还没接受过工作时，才允许在 `Awake` 后绑定。外部 scheduler 可以已经持有 composition root 注册的工作。

| 操作 | Host 持有 | 外部绑定 |
| --- | --- | --- |
| Driver Update | 执行工作 | 执行工作 |
| `driver.BeginShutdown()` | 开始分批关闭 | 被拒绝 |
| `driver.Shutdown()` / `OnDestroy` | 立即关闭 | 只解绑，不取消 |
| 最终 owner | Driver | Composition root |

`OwnsScheduler` 能告诉你当前是哪种情况。外部 owner 必须先 drain，再销毁工作项引用的资源。

## Factory 集成

Factory integration 适配已有的有界 pool 维护调用。

```csharp
using CycloneGames.Scheduling.Integrations.Factory;

var warmup = new PoolWarmupWorkItem(pool, count: 100, batchSize: 4);
scheduler.TrySchedule(warmup, out _);

var trim = new PoolTrimInactiveWorkItem(pool, targetInactiveCount: 16, batchSize: 4);
scheduler.TrySchedule(trim, out _);

var despawn = new PoolDespawnWorkItem(pool, batchSize: 4);
scheduler.TrySchedule(despawn, out _);
```

`batchSize` 限制适配器每次 Step 的 pool 操作量，`maxSteps` 限制每次 Tick 的适配器调用次数，两者不同。

`TargetReached` 告诉你到底达到了请求目标，还是 pool 没有进展而结束。取消会停止后续 pool 调用，不撤销已发生的操作，也不会 Dispose pool。pool owner 负责保持 pool 存活，并避免冲突的结构变更。

## 存储与分配

调度器在构造时一次性分配 `Slot[]` 和空闲槽位 `int[]`。

- `TrySchedule`、`Cancel` 和重新排队都是 O(1)。
- Shutdown 是 O(终止项数)，用 `ShutdownStep` 分摊。
- `WorkItemHandle` 保存槽位索引和 generation，只能在发出它的 scheduler 上使用。
- generation 0 是 default handle。generation 完整回绕后句柄可能别名，不要永久保存。

正常注册、取消、重新排队和分批关闭在构造后不分配托管内存。异常路径可能分配。热路径避免 LINQ、捕获 Lambda、扩容、临时集合和逐项字符串。

工作项分配和 payload 内存属于调用方。只有实测证明高频 churn 有收益且 reset 所有权清晰时，才池化工作项。

## 线程与 IO 边界

调度器只属于创建它的线程。它不是线程安全的，不会把工作从 worker 线程搬到主线程，也不会让 Unity API 获得跨线程调用能力。跨线程生产者需要单独的有界 adapter 和同步设计。

Step 不能隐藏无界同步 IO。文件读取、hash、解压、异步加载和 buffer 所有权都需要有界领域 adapter。大文件必须在操作内部增量处理，一个文件一个 Step 不够。

性能数据取决于设备、Unity 版本、backend、构建类型、workload 和 stripping 设置。Editor 测试不能代表目标 Player 性能。

## 常见错误

| 错误 | 修正 |
| --- | --- |
| 一个 Step 里放长时间同步操作 | 拆成有界 Step。 |
| 每次拒绝都新建工作项 | 保留一个实例，稍后重试。 |
| 同一个实例排队两次 | 用不同注册，或等待终止。 |
| 在 `Step` 或 `OnTerminated` 里调调度器 API | 先返回，下一次 owner update 再处理。 |
| 以为 `maxSteps` 能限制 pool 对象数或回调成本 | 各领域分别预算。 |
| Host 和 container 都关闭绑定 scheduler | 让 composition root 负责。 |
| 维护工作没停就销毁 pool | 先停止并 drain。 |
| 把 Editor 测试当 Player 性能数据 | 测目标 Player。 |

## 测试

EditMode：

```bash
<unity-exe> -batchmode -nographics -projectPath <repo-root>/UnityStarter -runTests -testPlatform EditMode -testFilter CycloneGames.Scheduling -testResults <output.xml> -logFile <output.log>
```

PlayMode：

```bash
<unity-exe> -batchmode -nographics -projectPath <repo-root>/UnityStarter -runTests -testPlatform PlayMode -testFilter CycloneGames.Scheduling.Tests.PlayMode -testResults <output.xml> -logFile <output.log>
```

套件覆盖 FIFO 顺序、预算、拒绝、取消、终止回调、异常、立即与分批关闭、句柄复用、Factory 维护、Unity Update 与销毁、fake-null 和外部所有权。

分配和性能测量要在 Editor 之外或目标 Player 中进行。目标 Player、IL2CPP、AOT、stripping 和设备特定行为需要单独验证。
