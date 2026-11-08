# CycloneGames.BehaviorTree

[English](README.md) | 简体中文

面向 agent 的 Unity 行为树运行时。默认使用托管节点图，需要时切到平面 Burst 路径。

## <a id="what-is-a-bt"></a>什么是行为树

行为树是一种从根节点开始、每个 tick 重新求值的决策结构。叶子节点执行动作或判断条件，内部节点决定运行哪些叶子、以什么顺序运行。每个节点返回 `Success`、`Failure` 或 `Running`。`Running` 是它适合 agent 的原因：一个节点可以跨越多帧而不丢失位置，下一个 tick 从同一分支继续。

巡逻、Boss 阶段、交互流程都可以用同一种结构表达。变化的只是叶子集合，求值规则不变。

| 模型 | 长处 | 代价 |
| --- | --- | --- |
| 有限状态机 | 转移显式，状态少时容易读 | 状态对增加后转移数量膨胀；跨 agent 复用要手工做 |
| 行为树 | 子树可组合，求值规则统一，`Running` 让跨帧任务简单 | 写得随意时，条件每个 tick 都会被重新检查 |
| 目标导向规划 | 能处理没人预先写过的动作组合 | 每次决策都要规划；难以界定上界，也难以调试 |
| 效用评分 | 多个小选项之间可以平滑切换优先级 | 调分数占掉大部分工作；表达顺序很别扭 |

行为树保留手写的控制流，同时允许一棵子树挂到不同父节点下。这是它相对状态机的取舍：少写转移，多拿复用。

## <a id="why-this-module"></a>为什么需要这个模块

多数行为树库为编辑体验优化。这个模块为"在商业项目里长期运行大量树"优化，需求因此不同：

- 每 tick 的工作必须有上界，一棵深树不能吃掉整帧。
- 稳态执行不能分配，GC 尖峰会直接反映在帧时间上。
- 同一棵树在重载、回放、网络往返之后必须表现一致。
- 失败要早且局部。非法图应该在编译期失败，而不是打到一半才炸。

模块按层拆分，让每个决定只有一个归属。

| 层 | 解决的问题 | 不在范围内 |
| --- | --- | --- |
| 创作层（可选） | 在 Edit Mode 序列化 `BehaviorTree` 资产与布局 | 运行时状态 |
| composition root | 构建或编译 `RuntimeBehaviorTree`，持有生命周期，注入 owner 与服务，挂载调度器 | 节点语义、传输策略 |
| 托管运行时 | 持有一份无环 `RuntimeNode` 图与一份 `RuntimeBlackboard`；定义激活、完成、中止、故障、重置与释放 | 线程与 job |
| 重评估 | 把单调 epoch 从生产者送到 owner 线程 | 判定什么算事件 |
| 调度 | 限制一次 pass 扫描多少棵树 | 帧节奏，由 `MonoBehaviour` 包装驱动 |
| 序列化契约 | 生成与消费有界 snapshot、delta、hash payload | 传输、authority、排序、限流、重同步 |
| DOD 运行时 | 在 Burst job 中执行同构平面树，使用固定 slot | 托管节点、observer、schema、`object` 值 |

由此得到两条规则。托管树、节点、runner 组件与托管调度器只在 owner 线程执行；`WakeUp()` 与 `RequestReevaluation()` 是跨线程入口。网络类型只负责编解码字节，何时发送、收到后怎么处理由产品层决定。

## <a id="design-approaches"></a>行为树的常见设计

生产代码里常见四种做法。区别在于树存在哪里，以及什么时候重新求值。

| 设计 | 树存在哪里 | 何时重新求值 | 适合 |
| --- | --- | --- | --- |
| 节点图 | 带父子引用的对象 | 每个 tick，从根开始 | 经常改动的手写树 |
| 表驱动 | 配置或资产中的行 | 每个 tick，从根开始 | 内容团队大、批量编辑、本地化 |
| 事件驱动 | 节点图 + key observer | 被观察的值变化时 | guard 开销大、大量空闲 agent |
| 平面 / 数据导向 | 连续数组中的 struct | 每个 tick，在 job 内 | 数千个同构 agent |

前两种是创作方式，后两种是运行方式，可以组合：表可以生成节点图，事件驱动的树仍然可以按 budget tick。

本模块默认使用节点图，按装饰器支持事件驱动重评估，并为同构人群提供平面 Burst 路径。三者共用 Blackboard 与调度契约，因此一棵子树可以迁到平面路径，而不必改写项目其余部分。

## <a id="features"></a>这个模块提供什么

- 托管运行时，生命周期、中止与故障处理都是显式的。一棵树就是一张对象图，没有隐藏的全局状态。
- 类型化 Blackboard，可选严格 schema。key 是 int，值有类型，热路径不装箱。
- 有界调度：持久游标、按 bucket 的 budget、可选的 distance LOD。
- 事件驱动重评估：Blackboard observer 可以请求重新扫描，不必每个 tick 轮询所有 guard。
- 网络同步用的 snapshot、delta 与 hash 契约；authority 与复制策略在独立包中。
- 编辑器把 `BehaviorTree` 资产显示为图，并显示实时状态、当前 child 索引与已耗时。
- 六个 CI 门控，每个都在一秒内完成：存储 FUZZ、调度公平性、带脉冲的短 SOAK、深度上限、两档分配检查。

它不提供：传输、authority 判定、预测或回滚、感知、技能、tag。这些在独立包里，第 13 章说明如何连接。

## <a id="authoring"></a>两种创作方式

一棵树可以做成 `BehaviorTree` 资产，也可以用代码构建。两者最终都是同一个 `RuntimeBehaviorTree`。

| 方式 | 做法 | 适合 |
| --- | --- | --- |
| 资产 | 创建 `BehaviorTree` 资产，在图编辑器里添加节点、连父子、指定 blackboard schema，然后让 `BTRunnerComponent` 引用它 | 策划/设计反复调整行为；需要跨构建修改的树 |
| 代码 | `RuntimeBehaviorTreeBuilder`，或在测试里直接构造节点 | 程序化生成、单元测试、工具、原型 |
| 混合 | 资产负责结构，代码负责自定义叶子节点 | 需要项目内强类型引用的动作 |

编辑器把节点对象存进资产；树启动时 `BehaviorTree.Compile` 把它们转成 runtime node。Builder 直接生成同样的 runtime node 类型。tick 成本取决于你用了哪些节点，与创作方式无关。

下面的示例用 Builder，因为它在文档里更好读。生产环境的树通常以资产形式创作。

编辑器按类型给节点着色，在每个节点上打印运行状态，并标出当前 child 索引。右键菜单可以直接添加动作、条件、组合与装饰节点。

## <a id="contents"></a>目录

- [什么是行为树](#what-is-a-bt)
- [为什么需要这个模块](#why-this-module)
- [行为树的常见设计](#design-approaches)
- [这个模块提供什么](#features)
- [两种创作方式](#authoring)
- [1. 环境与程序集边界](#environment)
- [2. 第 1 步：最小可运行树](#step-1)
- [3. 第 2 步：黑板与键](#step-2)
- [4. 第 3 步：严格 schema](#step-3)
- [5. 第 4 步：组合节点](#step-4)
- [6. 第 5 步：装饰器与条件](#step-5)
- [7. 第 6 步：事件驱动重评估](#step-6)
- [8. 第 7 步：tick 归属与调度](#step-7)
- [9. 第 8 步：LOD 与显式 composition root](#step-8)
- [10. 第 9 步：快照 / 增量 / 网络契约](#step-9)
- [11. 第 10 步：DOD Burst 路径](#step-10)
- [12. 综合示例：一棵多层树](#composite-example)
- [13. 与其他 CycloneGames 包组合](#combining)
- [14. 性能、容量与测量](#performance)
- [15. API 速查表](#api)

## <a id="environment"></a>1. 环境与程序集边界

工程打开路径为 `<repo-root>/UnityStarter`。运行时拆成引擎无关的 Core 与 Unity 层两部分，因此同一棵树、同一套黑板、同一套调度契约可以跑在 Editor、Player 和无 Unity 的 .NET 服务端。

| Assembly | 引擎依赖 | 内容 |
| --- | --- | --- |
| `CycloneGames.BehaviorTree.Core` | 无（`noEngineReferences: true`） | `RuntimeBehaviorTree`、黑板与 schema、托管节点、调度、`BTFloat3`、序列化契约 |
| `CycloneGames.BehaviorTree.Runtime` | Unity | `BehaviorTree` 与 `BTNode` 资源、编译器、runner 与管理器组件、LOD、`UnityBTContext` |
| `CycloneGames.BehaviorTree.Editor` | Unity Editor | 图窗口、Inspector、黑板 schema 编辑 |

依赖只有一个方向：`Runtime -> Core`。Core 仅引用 `CycloneGames.Hash.Core` 与 `CycloneGames.Logging.Core`，服务端只引用 Core 即可，不需要引擎 assembly。

三个可选 assembly 均为 `autoReferenced: false`，使用方 asmdef 必须显式引用：

- `CycloneGames.BehaviorTree.Runtime.DOD` 由 `com.unity.burst`、`com.unity.collections`、`com.unity.mathematics` 的 `versionDefines` 及对应 `defineConstraints` 门控，并引用 Unity.Burst、Unity.Collections、Unity.Mathematics 与 Unity.Jobs。
- `CycloneGames.BehaviorTree.Integrations.DeterministicMath` 由 `com.cyclone-games.deterministic-math` 在 `[1.0.0,2.0.0)` 上的 `versionDefine` 生成 `CYCLONEGAMES_HAS_DETERMINISTIC_MATH`，并由对应的 `defineConstraint` 消费。
- `CycloneGames.BehaviorTree.Benchmarks` 承载 Editor 与 PlayMode 测量工具。

可选包缺失或版本不受支持时，对应 assembly 被排除，Core 与 Unity 层仍可编译。capability symbol 来自 `versionDefines`，不应写入 PlayerSettings。

### Unity 层补了什么

有两件事无法放进 Core：时钟和组件查找。`UnityBTContext` 同时提供两者，并且调用方注册的 service 永远优先于内置兜底。

```csharp
using CycloneGames.BehaviorTree.Core;
using CycloneGames.BehaviorTree.Runtime;
using UnityEngine;

public sealed class UnityContextUsage
{
    public RuntimeBehaviorTree Build(Component owner)
    {
        // GetOwner<T>() 经由 GameObject 解析；计时节点读取注入的时钟。
        var context = new UnityBTContext(owner);
        return new RuntimeBehaviorTreeBuilder(context)
            .Action(_ => RuntimeState.Success)
            .Build();
    }
}
```

Core 用 `BTFloat3` 取代 `Vector3`，黑板、快照与增量因此保持引擎无关。Unity 层提供 `SetVector3`、`GetVector3`、`TryGetVector3` 三个不产生分配的转换扩展方法，Unity 代码仍按 `Vector3` 写。

### 不依赖 Unity 运行

无引擎宿主引用 `CycloneGames.BehaviorTree.Core` 加 `CycloneGames.Hash.Core` 与 `CycloneGames.Logging.Core`。它用同一个 builder 创建树，自己调用 `Tick`：没有 `BTRunnerComponent`，没有 manager，没有场景。

```csharp
using CycloneGames.BehaviorTree.Core;

public static void TickUntilStopped(RuntimeBehaviorTree tree)
{
    while (!tree.IsStopped)
    {
        tree.Tick();
    }
}
```

注册了 `IRuntimeBTTimeProvider` 就用它，否则回落到单调的 `Stopwatch`，因此 `RuntimeWaitNode` 与超时装饰器在 Unity 之外同样保持正确计时。日志走 `CycloneGames.Logging`，本身没有引擎依赖。第 2 章会构建这个循环驱动的树。

共享上限以常量暴露：

```csharp
using CycloneGames.BehaviorTree.Core;

public static class Step0
{
    public static void ReadLimits()
    {
        int maxDepth = BTCapacityLimits.MaxTreeDepth;             // 256
        int maxNodes = BTCapacityLimits.MaxNodeCount;             // 65536
        int defaultNodes = BTCapacityLimits.DefaultNodeCount;     // 4096
        int priorityLevels = BTCapacityLimits.PriorityLevelCount; // 8
        int defaultAgents = BTCapacityLimits.DefaultAgentCount;   // 65536
    }
}
```

托管图校验、托管调度与平面树校验都消费 `BTCapacityLimits`，因此边界不会在执行路径之间漂移。

## <a id="step-1"></a>2. 第 1 步：最小可运行树

一棵树是由 `RuntimeBehaviorTreeBuilder` 构建的 `RuntimeBehaviorTree`。builder 使用流式 scope：组合节点或装饰器打开一个 scope，`.End()` 闭合，`Build()` 闭合所有剩余 scope。树只能有一个 root child，因此单个 `.Action(...)` 就是最小完整树。

```csharp
using CycloneGames.BehaviorTree.Core;

public static class Step1
{
    public static void Run()
    {
        RuntimeBehaviorTree tree = new RuntimeBehaviorTreeBuilder()
            .Action(_ => RuntimeState.Success, "Complete")
            .Build();

        tree.Terminated += state => UnityEngine.Debug.Log($"Terminated: {state}");

        RuntimeState result = tree.Tick();  // Success
        bool stopped = tree.IsStopped;     // true

        tree.Dispose();
    }
}
```

`Tick()` 按需开始 root activation，返回 `RuntimeState.Running`、`Success` 或 `Failure`。`Success` 与 `Failure` 是终态结果：tree 会设置 `IsStopped`、清除调度唤醒状态，并且只发布一次 `Terminated`。`Play()` 在 stopped tree 上开始新的 activation；`Stop()` 中止活动节点栈并发布 `Terminated(NotEntered)`。生命周期操作拒绝重入。builder 一次性使用：`Build()` 会冻结它，并拒绝没有 root child 的树。`Dispose()` 中止活动工作、释放 node-owned resource、释放所拥有的 Blackboard 并移除 termination subscriber；释放后公开操作抛出 `ObjectDisposedException`。

## <a id="step-2"></a>3. 第 2 步：黑板与键

`RuntimeBlackboard` 持有八个相互独立的强类型存储：`int`、`float`、`bool`、`Float3`、`long`、`Long2`、`Long3`、`object`。其中 `Float3` 是引擎无关的三元组，Unity 层另外提供 `Vector3` overload。强类型访问不会装箱 primitive 值。key 是 `int` hash；`DefaultStringHashFunc` 是稳定的 `BTHash.FNV1A`，因此字符串 key overload 只是每次调用时执行 hash 的便利方法。高频 key 应预先 hash 一次。

```csharp
using UnityEngine;
using CycloneGames.BehaviorTree.Core;

public static class Step2
{
    private static readonly int StageKey =
        RuntimeBlackboard.DefaultStringHashFunc("Stage");
    private static readonly int FocusKey =
        RuntimeBlackboard.DefaultStringHashFunc("Focus");
    private static readonly int ActiveKey =
        RuntimeBlackboard.DefaultStringHashFunc("Active");
    private static readonly int PositionKey =
        RuntimeBlackboard.DefaultStringHashFunc("Position");
    private static readonly int SequenceKey =
        RuntimeBlackboard.DefaultStringHashFunc("Sequence");
    private static readonly int PairKey =
        RuntimeBlackboard.DefaultStringHashFunc("Pair");
    private static readonly int TripleKey =
        RuntimeBlackboard.DefaultStringHashFunc("Triple");
    private static readonly int ReferenceKey =
        RuntimeBlackboard.DefaultStringHashFunc("Reference");

    public static void Run()
    {
        var shared = new RuntimeBlackboard();
        shared.SetInt(StageKey, 3);

        // Parent chain: reads fall back through Parent; writes affect only the local board.
        var local = new RuntimeBlackboard(parent: shared);
        int fromParent = local.GetInt(StageKey);          // 3
        local.SetInt(StageKey, 4);                        // local only
        int parentValue = shared.GetInt(StageKey);        // still 3

        // Eight typed stores.
        local.SetFloat(FocusKey, 1.5f);
        local.SetBool(ActiveKey, true);
        local.SetFloat3(PositionKey, new BTFloat3(1f, 2f, 3f));
        local.SetLong(SequenceKey, 42L);
        local.SetLong2(PairKey, new RuntimeBlackboardLong2(1L, 2L));
        local.SetLong3(TripleKey, new RuntimeBlackboardLong3(1L, 2L, 3L));
        local.SetObject(ReferenceKey, shared);

        // String-key convenience hashes with DefaultStringHashFunc.
        local.SetInt("Stage", 7);
        int viaString = local.GetInt("Stage");

        // Stamps change only when the local value actually changes.
        ulong stamp0 = local.GetStamp(StageKey);
        local.SetInt(StageKey, 8);
        ulong stamp1 = local.GetStamp(StageKey);
        local.SetInt(StageKey, 8);
        ulong stamp2 = local.GetStamp(StageKey);         // equals stamp1

        // Observers run synchronously after the write commits.
        BlackboardObserverCallback onStage = (key, bb) =>
            Debug.Log($"Stage changed: {bb.GetInt(key)}");
        local.AddObserver(StageKey, onStage);
        local.AddGlobalObserver((key, bb) => { });
        local.SetInt(StageKey, 9);
        local.RemoveObserver(StageKey, onStage);
        local.ClearAllObservers();

        // Restart the per-blackboard deterministic random stream.
        local.ResetDefaultRandom(seed: 1234u);

        // Precise type probing without a default fallback.
        bool hasInt = local.TryGetInt(StageKey, out int probed);

        local.Dispose();
        shared.Dispose();
    }
}
```

读取沿 `Parent` 回退，写入不会。`GetStamp` 返回 `ulong`，仅在本地值变化时递增，可用于低成本变更检测。`AddObserver`、`AddGlobalObserver`、`RemoveObserver`、`RemoveGlobalObserver` 与 `ClearAllObservers` 管理回调集合；订阅变化会分配新的 callback array，因此应在 setup 阶段注册、在 owner shutdown 时移除。`RuntimeBlackboard.ResetDefaultRandom(uint seed = 0u)` 重启确定性随机流；当未提供 `IRuntimeBTRandomProvider` 时，随机节点使用该流，而不依赖进程全局状态。`EnableConcurrentStorageAccess()` 为值表安装 `ReaderWriterLockSlim`，它只保护值表，不保护 `Parent`、`Context`、树执行或 Unity 对象。

## <a id="step-3"></a>4. 第 3 步：严格 schema

`RuntimeBlackboardSchema` 把 key 是否存在、类型、默认值与同步 flag 固化为显式契约。`RuntimeBlackboardSchemaBuilder` 负责构建；把它绑定到 Blackboard 会应用默认值，并使写入校验 key 存在性与类型。

```csharp
using System.IO;
using UnityEngine;
using CycloneGames.BehaviorTree.Core;

public static class Step3
{
    private static readonly int StageKey =
        RuntimeBlackboard.DefaultStringHashFunc("Stage");

    public static void Run()
    {
        RuntimeBlackboardSchema schema = new RuntimeBlackboardSchemaBuilder()
            .AddInt("Stage", 0, RuntimeBlackboardSyncFlags.SnapshotAndDelta)
            .AddBool("EventActive", false, RuntimeBlackboardSyncFlags.LocalOnly)
            .AddFloat("Focus", 0f, RuntimeBlackboardSyncFlags.Snapshot)
            .AddFloat3("Position", BTFloat3.Zero, RuntimeBlackboardSyncFlags.Delta)
            .AddLong("Sequence", 0L, RuntimeBlackboardSyncFlags.LocalOnly)
            .AddObject("Reference")
            .Build();

        var blackboard = new RuntimeBlackboard(schema: schema);

        // Defaults are applied at construction.
        int stage = blackboard.GetInt(StageKey);   // 0

        // Schema-bound writes validate the key and its type.
        blackboard.SetInt(StageKey, 2);
        blackboard.ResetToSchemaDefaults();       // Stage back to 0

        // Inspect the contract.
        bool known = schema.IsKnownKey(StageKey);
        bool delta = schema.UsesDelta(StageKey);
        bool snapshot = schema.UsesSnapshot(StageKey);
        int contractVersion = schema.ContractVersion;

        // Bounded snapshot payload; returns the captured revision.
        ulong revision;
        using (var stream = new MemoryStream())
        {
            using (var writer = new BinaryWriter(stream))
            {
                revision = blackboard.WriteTo(
                    writer, RuntimeBlackboardNetworkScope.Snapshot, maxBytes: 8192);
                writer.Flush();
            }
        }

        ulong hash = blackboard.ComputeHash(RuntimeBlackboardNetworkScope.Networked);

        blackboard.Dispose();
    }
}
```

`RuntimeBlackboardSyncFlags` 包含 `LocalOnly`、`Snapshot`、`Delta` 与 `SnapshotAndDelta`。`SnapshotAndDelta` 同时把 key 标记为参与全量 snapshot scope 与 delta 路径。`Object` key 始终为 `LocalOnly`，不能带创作默认值；`AddObject` 通过代码注入引用。向未声明 key 写入会抛出 `KeyNotFoundException`，类型不匹配会抛出 `InvalidOperationException`。`RuntimeBlackboardNetworkScope` 为 `Snapshot`（标记为全量 snapshot 的 key）或 `Networked`（全部非本地 primitive，包括仅 delta 的 key）。`WriteTo` 还提供 `maxBytes` overload：计算出的大小超过上界时不写入任何 payload 字节，并返回同一把锁下捕获的 Blackboard revision。`ResetToSchemaDefaults()` 原子替换本地值；未绑定 schema 时重置为空的本地 board。

需要由树拥有 schema 时，可在 builder 上使用 `WithBlackboardSchema(schema, applyDefaults)`；也可先用 `WithBlackboard(...)` 传入预先构建好的 Blackboard。两者都属于 composition root 的决策。

## <a id="step-4"></a>5. 第 4 步：组合节点

组合节点拥有一个有序 child array。`Seal()` 把 setup 阶段的 list 一次性转换为该数组，执行阶段直接读取 sealed array。builder 为以下方法各打开一个组合 scope：`Sequence`、`Selector`、`SequenceWithMemory`、`SelectorRandom`、`ReactiveSequence`、`ReactiveFallback`、`Parallel`、`ParallelAll`、`IfThenElse`、`WhileDoElse`、`Switch`、`ProbabilityBranch`、`UtilitySelector`。

| 组合节点 | 语义 |
| --- | --- |
| `RuntimeSequencer`（`Sequence`） | 从左到右运行 child；首个 `Failure` 使其失败；全部 `Success` 才成功。 |
| `RuntimeSelector`（`Selector`） | 从左到右运行 child；首个 `Success` 使其成功；全部 `Failure` 才失败。同时承担由 `AbortType` 驱动的 abort scan。 |
| `RuntimeSequenceWithMemory`（`SequenceWithMemory`） | 跳过已成功的 child，从上一个 running child 继续。 |
| `RuntimeSelectorRandom`（`SelectorRandom`） | 使用 setup 阶段 index array 洗牌，然后执行 selector 语义。 |
| `RuntimeReactiveSequence` / `RuntimeReactiveFallback`（`ReactiveSequence` / `ReactiveFallback`） | 每步从头部重新扫描 guard，并中止被取代的 running branch。 |
| `RuntimeIfThenElseNode`（`IfThenElse`） | child 依次为 `[condition, then, else]`。 |
| `RuntimeWhileDoElseNode`（`WhileDoElse`） | child 依次为 `[condition, body, else]`。 |
| `RuntimeSwitchNode`（`Switch`） | 使用 Blackboard 中的 `int` 作为从零开始的 case；最后一个 child 是 default。 |
| `RuntimeProbabilityBranch`（`ProbabilityBranch`） | 每次 activation 依据已校验的非负权重选择一个 child。 |
| `RuntimeUtilitySelector`（`UtilitySelector`） | 为每个 child 读取一个 Blackboard `float` score，运行得分最高者。 |
| `RuntimeParallelNode` / `RuntimeParallelAllNode`（`Parallel` / `ParallelAll`） | 在 owner 线程内交错 branch；完成策略见下文。 |

```csharp
using CycloneGames.BehaviorTree.Core;
using CycloneGames.BehaviorTree.Core.Nodes.Compositors;

public static class Step4
{
    private static readonly int PhaseKey =
        RuntimeBlackboard.DefaultStringHashFunc("Phase");
    private static readonly int GateKey =
        RuntimeBlackboard.DefaultStringHashFunc("Gate");
    private static readonly int ScoreAKey =
        RuntimeBlackboard.DefaultStringHashFunc("ScoreA");
    private static readonly int ScoreBKey =
        RuntimeBlackboard.DefaultStringHashFunc("ScoreB");

    public static void Run()
    {
        RuntimeBehaviorTree tree = new RuntimeBehaviorTreeBuilder()
            .Selector(selector => selector.AbortType = RuntimeAbortType.LowerPriority)
                .Sequence()
                    .Condition(bb => bb.GetInt(PhaseKey) >= 0, "PhaseReady")
                    .Action(bb => RuntimeState.Success, "Advance")
                .End()
                .SequenceWithMemory()
                    .Action(bb => RuntimeState.Success, "Prepare")
                    .Action(bb => RuntimeState.Success, "Commit")
                .End()
                .ReactiveFallback(fallback =>
                {
                    fallback.AbortType = RuntimeAbortType.LowerPriority;
                    fallback.ReevaluationMode = RuntimeReevaluationMode.Hybrid;
                    fallback.ReevaluationPollInterval = 4;
                })
                    .Condition(bb => bb.GetBool(GateKey), "Gate")
                    .Action(bb => RuntimeState.Running, "Observe")
                .End()
                .IfThenElse()
                    .Condition(bb => bb.GetBool(GateKey), "BranchGate")
                    .Action(bb => RuntimeState.Success, "Then")
                    .Action(bb => RuntimeState.Success, "Else")
                .End()
                .Switch(PhaseKey)
                    .Action(bb => RuntimeState.Success, "Case0")
                    .Action(bb => RuntimeState.Success, "Case1")
                    .Action(bb => RuntimeState.Success, "Default")
                .End()
                .UtilitySelector(new[] { ScoreAKey, ScoreBKey })
                    .Action(bb => RuntimeState.Success, "OptionA")
                    .Action(bb => RuntimeState.Success, "OptionB")
                .End()
                .ProbabilityBranch(new[] { 1f, 3f })
                    .Action(bb => RuntimeState.Success, "LowWeight")
                    .Action(bb => RuntimeState.Success, "HighWeight")
                .End()
                .WhileDoElse()
                    .Condition(bb => bb.GetBool(GateKey), "LoopGuard")
                    .Action(bb => RuntimeState.Success, "Body")
                    .Action(bb => RuntimeState.Success, "Exit")
                .End()
            .End()
            .Build();

        tree.Dispose();
    }

    public static void FinishPolicy()
    {
        // Parallel finish policy is expressed by RuntimeParallelMode,
        // plus thresholds on RuntimeParallelAllNode.
        RuntimeBehaviorTree parallel = new RuntimeBehaviorTreeBuilder()
            .Parallel(RuntimeParallelMode.UntilAnySuccess)
                .Action(bb => RuntimeState.Success, "CompleteA")
                .Action(bb => RuntimeState.Running, "HoldB")
            .End()
            .Build();

        RuntimeBehaviorTree thresholds = new RuntimeBehaviorTreeBuilder()
            .ParallelAll(all => all.SuccessThreshold = 2)
                .Action(bb => RuntimeState.Success, "A")
                .Action(bb => RuntimeState.Success, "B")
                .Action(bb => RuntimeState.Success, "C")
            .End()
            .Build();

        parallel.Dispose();
        thresholds.Dispose();
    }
}
```

`RuntimeParallelMode` 为 `Default`、`UntilAnyComplete`、`UntilAnyFailure`、`UntilAnySuccess`。`RuntimeParallelNode` 保留每个 child 的终态，同一 activation 内不会再次运行已终止的 child。`RuntimeParallelAllNode` 每个 step Tick 每个尚未完成的 child 一次；`SuccessThreshold` 与 `FailureThreshold` 可取 `-1` 表示全部 child，或取 `1` 到 child count 之间的值；若同一步同时达到两个 threshold，failure 优先。托管 Parallel 只在一个 owner-thread Tick 内交错 branch，不创建线程、task 或 job。`RuntimeCompositeNode` 还暴露 `ReevaluationMode`、`ReevaluationPollInterval` 与 `AbortType`，这是第 6 步的主题。

## <a id="step-5"></a>6. 第 5 步：装饰器与条件

装饰器只包裹一个 child。builder 为 `Inverter`、`Succeeder`、`ForceFailure`、`Repeat`、`RepeatForever`、`Retry`、`Timeout`、`Delay`、`CoolDown`、`RunOnce`、`KeepRunningUntilFailure`、`WaitSuccess`、`Wait`、`WaitRandom` 提供直接 helper。需要单独配置的节点，使用泛型 `Decorator<TNode>(node, configure)` overload：它会在节点被加入、被树拥有之前调用 `configure`。

`BBComparisonNode` 是创作态 ScriptableObject 装饰器，其 `BehaviorTreeComparisonValueType` 可选 `Int`、`Float`、`Bool`、`Object`。对应的运行时节点是 `RuntimeBBComparisonNode`，带 `BBComparisonOp`（`Equal`、`NotEqual`、`GreaterThan`、`GreaterOrEqual`、`LessThan`、`LessOrEqual`、`IsSet`、`IsNotSet`）与强类型 reference 值。`SubTreeNode` / `RuntimeSubTreeNode` 在自有 scoped Blackboard 上运行 child，并支持带方向的 port remapping。`RuntimeServiceNode` 在 child 执行期间按间隔运行副作用回调。

```csharp
using CycloneGames.BehaviorTree.Core;
using CycloneGames.BehaviorTree.Core.Nodes.Decorators;

public sealed class AdvanceCommand : IRuntimeBTCommand
{
    public RuntimeState Execute(RuntimeBlackboard blackboard)
    {
        return RuntimeState.Success;
    }
}

public sealed class StageGate : IRuntimeBTConditionStrategy
{
    public bool Evaluate(RuntimeBlackboard blackboard)
    {
        return true;
    }
}

public static class Step5
{
    private static readonly int PhaseKey =
        RuntimeBlackboard.DefaultStringHashFunc("Phase");
    private static readonly int LocalPhaseKey =
        RuntimeBlackboard.DefaultStringHashFunc("LocalPhase");
    private static readonly int ParentPhaseKey =
        RuntimeBlackboard.DefaultStringHashFunc("ParentPhase");

    public static void Run()
    {
        RuntimeBehaviorTree tree = new RuntimeBehaviorTreeBuilder()
            .Sequence()
                .Inverter()
                    .Condition(new StageGate(), "Negated")
                .End()
                .Repeat(3)
                    .Action(bb => RuntimeState.Success, "RepeatBody")
                .End()
                .CoolDown(0.5f)
                    .Action(bb => RuntimeState.Success, "RateLimited")
                .End()
                .Timeout(2f)
                    .Action(bb => RuntimeState.Running, "BoundedWork")
                .End()
                .Decorator(new RuntimeBBComparisonNode(), comparison =>
                {
                    comparison.KeyHash = PhaseKey;
                    comparison.ValueType = BehaviorTreeComparisonValueType.Int;
                    comparison.Operator = BBComparisonOp.GreaterOrEqual;
                    comparison.RefInt = 1;
                    comparison.AbortMode = RuntimeAbortType.Self;
                })
                    .Command(new AdvanceCommand(), "GuardedAdvance")
                .End()
                .Decorator(new RuntimeSubTreeNode(), subtree =>
                {
                    subtree.SetPortRemapping(
                        new[] { LocalPhaseKey },
                        new[] { ParentPhaseKey },
                        new byte[] { 1 },
                        new[] { RuntimeSubTreePortDirection.InOut });
                })
                    .Action(bb => RuntimeState.Success, "ScopedWork")
                .End()
            .End()
            .Build();

        tree.Dispose();
    }
}
```

具名 `IRuntimeBTCommand` 与 `IRuntimeBTConditionStrategy` 对象避免捕获 lambda，并可在多棵树间复用。任意 `RuntimeNode` 还通过 `AddPreCondition` 与 `AddPostCondition` 提供前置/后置条件，配合 `ConditionPolicy.FailWhenFalse`、`SucceedWhenFalse`、`AbortWhenFalse`。时间相关托管节点从 runtime context 解析 `IRuntimeBTTimeProvider`；随机节点在提供 `IRuntimeBTRandomProvider` 时优先使用，否则使用每黑板确定性随机流。

## <a id="step-6"></a>7. 第 6 步：事件驱动重评估

`RuntimeBTEvaluationController` 为一棵树维护一个单调 epoch。`RequestReevaluation()` 是一次 `Interlocked.Increment`；任意线程的并发请求合并为单个计数，因此突发的外部事件流不会产生队列、分配或积压。owner 线程在 tick 时通过 `ShouldReevaluate(ref lastSeenEpoch, ref pollCounter, mode, pollInterval)` 消费该 epoch。

- `RuntimeReevaluationMode.PollEveryTick` 无条件扫描 guard。适用于 guard 成本低、且不允许漏掉任何一帧的情况。
- `RuntimeReevaluationMode.OnChange` 仅在收到请求后扫描。适用于生产者能够发出相关变更信号的情况。
- `RuntimeReevaluationMode.Hybrid` 在收到请求时扫描，并按 `ReevaluationPollInterval` 额外定期扫描。适用于以事件为主、同时保留有界周期性重扫的情况。

`RuntimeCompositeNode.ReevaluationMode` 与 `ReevaluationPollInterval` 按组合节点选择模式；`RuntimeReactiveSequence` 与 `RuntimeReactiveFallback` 在 `OnChange` 或 `Hybrid` 下，空闲 tick 只推进 active branch。`RuntimeBBComparisonNode.AbortMode` 是类型为 `RuntimeAbortType`（`None`、`Self`、`LowerPriority`、`Both`）的属性：非 `None` 值会在 graph commit 时绑定 Blackboard observer，被跟踪 key 变化时调用 `RuntimeBehaviorTree.RequestReevaluation()`，后者递增 epoch 并调度一次立即唤醒。`RuntimeNode.EmitWakeUpSignal()` 是 protected helper，节点可在执行内部调用它请求重评估。

```csharp
using CycloneGames.BehaviorTree.Core;
using CycloneGames.BehaviorTree.Core.Nodes.Decorators;

public sealed class WakeUpOnSignalNode : RuntimeNode
{
    private readonly int _signalKey;

    public WakeUpOnSignalNode(int signalKey) => _signalKey = signalKey;

    protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
    {
        if (blackboard.GetBool(_signalKey))
        {
            EmitWakeUpSignal();
            return RuntimeState.Success;
        }

        return RuntimeState.Running;
    }
}

public static class Step6
{
    private static readonly int GateKey =
        RuntimeBlackboard.DefaultStringHashFunc("Gate");

    public static void Run()
    {
        RuntimeBehaviorTree tree = new RuntimeBehaviorTreeBuilder()
            .Selector(selector =>
            {
                selector.AbortType = RuntimeAbortType.Both;
                selector.ReevaluationMode = RuntimeReevaluationMode.OnChange;
            })
                .Decorator(new RuntimeBBComparisonNode(), comparison =>
                {
                    comparison.KeyHash = GateKey;
                    comparison.ValueType = BehaviorTreeComparisonValueType.Bool;
                    comparison.Operator = BBComparisonOp.Equal;
                    comparison.RefBool = true;
                    comparison.AbortMode = RuntimeAbortType.Both;
                })
                    .Action(bb => RuntimeState.Running, "GuardedWork")
                .End()
                .Node(new WakeUpOnSignalNode(GateKey))
            .End()
            .Build();

        // Producer side: requests coalesce into one epoch counter.
        int epochBefore = tree.Evaluation.Epoch;
        tree.Evaluation.RequestReevaluation();
        int epochAfter = tree.Evaluation.Epoch;

        // Schedule bounded immediate owner-thread ticks.
        tree.WakeUp(boostedTicks: 2);

        // A tracked key change also calls tree.RequestReevaluation() through the observer.
        tree.Blackboard.SetBool(GateKey, true);

        RuntimeState state = tree.Tick();
        tree.Dispose();
    }
}
```

`RuntimeBehaviorTree.RequestReevaluation(int boostedTicks = 1)` 是合并入口：它递增 epoch 并调用 `RequestWakeUp`。`WakeUp(int boostedTicks = 1)` 只携带 bounded immediate-tick budget。`ShouldTick()` 消费一次 boosted tick 并重置 tick-interval 计数；`WakeUpTickBudget` 与 `HasWakeUpRequest` 暴露待处理状态。没有可靠生产者、或 guard 成本可忽略时使用轮询；变更可观测且需要限制 tick 工作量时使用事件。`Hybrid` 在事件之外保留周期性重扫。

## <a id="step-7"></a>8. 第 7 步：tick 归属与调度

`BTRunnerComponent` 上的 `TickMode` 有四个值：

| 模式 | Owner | 驱动方式 |
| --- | --- | --- |
| `Self` | `BTRunnerComponent.Update` | 每帧由 component 提供一次 Tick 机会。 |
| `Manual` | 调用方 | `ManualTick()`。 |
| `Managed` | `BTTickManagerComponent` | 有界 round-robin scan。 |
| `PriorityManaged` | `BTPriorityTickManagerComponent` | 八个 priority bucket，加可选距离 LOD。 |

组件之下，`BTTickManager` 与 `BTPriorityTickManager` 是不依赖 Unity logging 的纯 C# 类。`BTTickManager.TickBudget` 计算一次 pass 中扫描的树数；被扫描的树仍可能因 `TickInterval` 跳过执行。`BTPriorityTickManager` 持有八个 bucket 与逐 bucket 的 `int[]` budget；`SetBudgets(int[])` 更新它们，零值禁用对应 bucket。`Tick()` 期间请求的注册、注销与 priority 变更按树排队，并在 pass 结束后应用，因此节点回调无法在扫描中途改动集合。终态树会被自动移除。

```csharp
using UnityEngine;
using CycloneGames.BehaviorTree.Core;
using CycloneGames.BehaviorTree.Runtime.Components;

public static class Step7
{
    public static RuntimeBehaviorTree BuildHoldTree()
    {
        return new RuntimeBehaviorTreeBuilder()
            .WithTickInterval(2)
            .Selector()
                .Action(bb => RuntimeState.Running, "Hold")
            .End()
            .Build();
    }

    public static void ConfigureRunner(BTRunnerComponent runner)
    {
        runner.SetTickMode(TickMode.Managed);
        runner.SetTickInterval(2);
        runner.WakeUp(boostedTicks: 2);
        runner.Pause();
        runner.Resume();
        runner.Stop();
        runner.Play();
    }

    public static void Schedule()
    {
        RuntimeBehaviorTree a = BuildHoldTree();
        var roundRobin = new BTTickManager(initialCapacity: 64);
        roundRobin.TickBudget = 32;
        if (roundRobin.TryRegister(a))
        {
            roundRobin.Tick();       // deferred mutations applied at pass end
        }
        roundRobin.Unregister(a);
        a.Dispose();

        RuntimeBehaviorTree b = BuildHoldTree();
        var priority = new BTPriorityTickManager(
            new[] { 64, 32, 16, 8, 4, 2, 1, 1 });
        priority.TryRegister(b, priority: 2);
        priority.Tick();
        priority.UpdatePriority(b, 0);
        priority.Unregister(b);
        b.Dispose();

        RuntimeBehaviorTree c = BuildHoldTree();
        BTTickManagerComponent managed = BTTickManagerComponent.CreateInstance();
        managed.TickBudget = 128;
        managed.Register(c);
        managed.Unregister(c);
        c.Dispose();

        RuntimeBehaviorTree d = BuildHoldTree();
        BTPriorityTickManagerComponent priorityManaged =
            BTPriorityTickManagerComponent.CreateInstance();
        priorityManaged.Register(d, priorityManaged.transform);
        priorityManaged.Unregister(d);
        d.Dispose();
    }
}
```

`RuntimeBehaviorTree.TickInterval` 可在 owner 线程设置；`ShouldTick()` 依此门控执行，而 `WakeUp` boost 会在有界次数的 owner-thread tick 内绕过该间隔。`BTCapacityLimits.DefaultAgentCount` 为 `65536`，`MaxAgentCount` 为 `1048576`；manager 拒绝超过硬上限的配置。tree 或 deferred-mutation 容量不足时 `TryRegister` 返回 `false`，而 `void` 包装方法委托给 `Try*` 路径。`BTTickManagerComponent.Instance` 与 `BTPriorityTickManagerComponent.Instance` 只解析场景中已挂载的 manager，不创建对象；`CreateInstance()` 是 composition root 显式创建常驻 manager 的入口。

## <a id="step-8"></a>9. 第 8 步：LOD 与显式 composition root

managed 模式下的 runner 必须有 manager 组件：要么显式挂载在场景 GameObject 上，要么由 composition root 通过 `BTTickManagerComponent.CreateInstance()` / `BTPriorityTickManagerComponent.CreateInstance()` 创建。`Instance` 只解析已有场景组件，不做分配。没有 manager 时，managed tick 模式的 runner 会在 `Update()` 中本地 tick 并输出一次警告，因此在挂载 manager 之前，managed 调度处于非激活状态。

`BTLODConfig` 把严格递增的距离映射到 `TickInterval` 与 `0` 到 `7` 的 priority bucket，并带 `PriorityBudgets`、`BoostedPriority`、`BoostedTickInterval`。`BTDistanceLODProvider` 是可选组件，挂载在 priority manager 所在 GameObject 上：`Initialize()` 通过 `TryGetComponent` 查找它。没有该 provider 时，manager 保留 priority 调度，并使用每棵树自身的 tick interval。

```csharp
using UnityEngine;
using CycloneGames.BehaviorTree.Core;
using CycloneGames.BehaviorTree.Runtime.Components;

public static class Step8
{
    public static void Run(Transform reference)
    {
        RuntimeBehaviorTree tree = new RuntimeBehaviorTreeBuilder()
            .Action(bb => RuntimeState.Running, "Hold")
            .Build();

        // Option A: explicit composition-root creation for Managed mode.
        BTTickManagerComponent managed = BTTickManagerComponent.CreateInstance();
        bool hasManaged = BTTickManagerComponent.HasInstance;
        managed.TickBudget = 64;

        // Option B: explicitly mounted priority manager with the optional LOD provider.
        var host = new GameObject("[BTPriorityTickManager]");
        host.AddComponent<BTDistanceLODProvider>();
        BTPriorityTickManagerComponent priority =
            host.AddComponent<BTPriorityTickManagerComponent>();

        BTLODConfig config = ScriptableObject.CreateInstance<BTLODConfig>();
        config.PriorityBudgets = new[] { 64, 32, 16, 8 };
        config.BoostedPriority = 0;
        config.BoostedTickInterval = 1;
        priority.Config = config;
        priority.SetReferencePoint(reference);
        priority.Register(tree, reference);
        priority.BoostPriority(tree, 0.5f);

        priority.Unregister(tree);
        Object.Destroy(config);
        Object.Destroy(host);
        tree.Dispose();
    }
}
```

先挂载可选的 `BTDistanceLODProvider`、再添加 manager 组件是必要的：manager 在自身初始化时解析一次并保留引用。`BTLODConfig.TryValidate(out string error)` 校验有限且严格递增的距离、至少为一的 tick interval、合法 priority、非负 budget，以及每个被使用的 priority 都有 budget entry。距离检查使用预先平方的 threshold。没有场景对象时，`BTPriorityTickManagerComponent.CreateInstance()` 是等价的 composition root 调用；它返回已有实例，不会创建重复对象。

## <a id="step-9"></a>10. 第 9 步：快照 / 增量 / 网络契约

`RuntimeBlackboard.WriteTo`、`ReadFrom` 与 `ComputeHash` 是基础契约。`RuntimeBlackboardNetworkScope` 选择可见性边界：`Snapshot` 只包含标记为全量 snapshot 的 key，`Networked` 包含全部非本地 primitive（含仅 delta 的 key）。第 3 步的 `RuntimeBlackboardSyncFlags.SnapshotAndDelta` 决定每个 key 参与哪个 scope。`Object` key 永不进入 payload。

`CycloneGames.BehaviorTree.Core.Networking` 中的类型是 contract-only：`BTNetworkSync` 提供 snapshot 捕获、序列化、反序列化、树状态 hash、反同步比较与 Blackboard snapshot 应用；`BTBlackboardDelta` 跟踪变化的 key 并生成或应用有界 delta frame。它们不创建 transport、不选择 authority、不定义排序或速率限制，也不执行重新同步与故障恢复。产品层决定何时调用、发送哪些字节，以及如何应对不一致。

同级包 `CycloneGames.BehaviorTree.Networking` 在这些原语之上补充消息与 authority 契约：`BehaviorTreeNetworkProtocol` 冻结消息 ID 与 payload schema；`BehaviorTreeNetworkProfile` 声明同步模型、feature flag 与 tick 节奏；`BehaviorTreeNetworkAuthority` 把 tree 绑定到网络身份并判定 tick / apply 权限；`BehaviorTreeNetworkReplication` 按 owner、team 或 area 过滤观察者；`BehaviorTreeNetworkSyncBridge` 捕获 snapshot、delta 与 hash-only frame，并应用经过校验的 payload。该包各 assembly 均为 `autoReferenced: false`，不含 transport 或驱动循环：composition root 需自行按节奏调用 `CaptureSnapshot`、`TryCreateBlackboardDelta` 或 `TryApplyPayload`，并显式驱动 profile 的节奏字段。`CycloneGames.AIPerception.Networking` 对感知数据采用同样的 contract-only 形态。

```csharp
using System;
using System.IO;
using CycloneGames.BehaviorTree.Core;
using CycloneGames.BehaviorTree.Core.Networking;

public static class Step9
{
    public static void Run(RuntimeBehaviorTree tree)
    {
        RuntimeBlackboard bb = tree.Blackboard;

        // Bounded snapshot payload.
        byte[] payload;
        using (var stream = new MemoryStream())
        {
            using (var writer = new BinaryWriter(stream))
            {
                bb.WriteTo(writer, RuntimeBlackboardNetworkScope.Snapshot, maxBytes: 8192);
                writer.Flush();
            }
            payload = stream.ToArray();
        }

        using (var stream = new MemoryStream(payload))
        using (var reader = new BinaryReader(stream))
        {
            bb.ReadFrom(reader, RuntimeBlackboardSerializationLimits.Default,
                RuntimeBlackboardNetworkScope.Snapshot);
        }

        ulong bbHash = bb.ComputeHash(RuntimeBlackboardNetworkScope.Networked);

        // Snapshot projection and tree-state hash (contracts only).
        BTStateSnapshot snapshot = BTNetworkSync.CaptureSnapshot(tree);
        byte[] wire = BTNetworkSync.SerializeSnapshot(snapshot);
        BTStateSnapshot decoded = BTNetworkSync.DeserializeSnapshot(wire);
        BTNetworkSync.ApplyBlackboardSnapshot(tree, decoded);

        using (var buffer = new BTStateSnapshotBuffer())
        {
            ulong treeHash = BTNetworkSync.ComputeTreeStateHash(
                tree, RuntimeBlackboardNetworkScope.Networked, buffer,
                BTNetworkSync.Limits.Default);
            bool desynced = BTNetworkSync.CheckDesync(tree, treeHash);
        }

        // Delta frames from the schema's delta-enabled keys.
        using (var delta = BTBlackboardDelta.CreateForSchema(bb.Schema))
        {
            delta.Attach(bb);
            if (delta.TryFlush(bb, out ArraySegment<byte> patch))
            {
                byte[] frame = new byte[patch.Count];
                Array.Copy(patch.Array, patch.Offset, frame, 0, patch.Count);
                BTBlackboardDelta.Apply(bb, frame);
            }
            delta.Detach();
        }
    }
}
```

`WriteTo(BinaryWriter, RuntimeBlackboardNetworkScope, int maxBytes)` 返回同一把存储锁下捕获的 Blackboard revision，并在计算大小超过上界时不写入任何 payload 字节。`ReadFrom` 在提交前校验 framing、计数、stamp 与 schema 声明；schema 不匹配会被拒绝。`ComputeHash` 按 key 升序折叠，使各端得到相同结果。`BTBlackboardDelta.CreateForSchema(schema)` 精确跟踪 `RuntimeBlackboardSyncFlags` 含 `Delta` 的 key；`TryFlush` 返回指向内部复用 buffer 的 `ArraySegment<byte>`，在下一次 flush 之前有效。`BTNetworkSync.ApplyBlackboardSnapshot` 只应用 snapshot 中的 Blackboard 部分，托管节点执行状态不会被通用地重建。

## <a id="step-10"></a>11. 第 10 步：DOD Burst 路径

当大量 agent 共享同一棵同构平面树，且 workload 适配受限节点集、固定 `int`/`float`/`bool` Blackboard slot 与固定 action slot 时，使用 DOD 路径。它是独立的执行模型：`FlatBehaviorTree` 为共享的不可变 definition 持有 persistent native storage，`BTTickScheduler` 以 NativeArray 持有每 agent 可变状态，并由 Burst `BTTickJob` 调度。它不使用 `RuntimeNode` 对象、`RuntimeBlackboard` observer、schema、`object` 值或托管 tick manager。

```csharp
using CycloneGames.BehaviorTree.Core;
using CycloneGames.BehaviorTree.Runtime.DOD;

public static class Step10
{
    public static void Run()
    {
        var nodes = new[]
        {
            new FlatNodeDef
            {
                Type = FlatNodeType.Root,
                ChildCount = 1,
                ChildStartIndex = 0
            },
            new FlatNodeDef
            {
                Type = FlatNodeType.BlackboardCondition,
                BBKey = 0,
                Compare = CompareOp.Greater,
                CompareValue = 0
            }
        };

        using (var flatTree = new FlatBehaviorTree(nodes, new[] { 1 }))
        using (var scheduler = new BTTickScheduler(
                   flatTree,
                   bbSlotCount: 1,
                   actionSlotCount: 0,
                   initialCapacity: 256))
        {
            BTAgentHandle agent = scheduler.AddAgent(tickInterval: 1);
            scheduler.SetBBInt(agent, 0, 10);
            scheduler.ScheduleTick(deltaTime: 0.016f, batchSize: 64);
            scheduler.CompleteTick();
            RuntimeState result = scheduler.GetRootState(agent);
            scheduler.RemoveAgent(agent);
        }
    }
}
```

节点 `0` 必须是唯一的 `FlatNodeType.Root`，组合节点的 child 通过扁平化的 `ChildIndices` 数组引用。`BTAgentHandle` 携带 slot index 与 generation，被回收的 slot 会拒绝 stale handle。`TryAddAgent` 是带 admission 的形式；`AddAgent` 在 effective capacity 处抛出 `InvalidOperationException`。终态 agent 会保持终态，直到调用 `ResetAgent(handle, clearBlackboard)`。外部 action 通过 `TryGetActionRequest` 与 `TrySetActionStatus` 使用 `BTActionRequestHandle`。每个公开状态访问都会先完成尚未结束的 scheduled job，再访问 NativeArray，且 scheduler 具有 creator/owner-thread affinity。必须先释放所有 scheduler，再释放共享 `FlatBehaviorTree`；上面嵌套的 `using` 顺序正是如此。平面树深度校验同样使用 `BTCapacityLimits.MaxTreeDepth`。

### 这条路径上的黑板

数据导向运行时**不读** `RuntimeBlackboard` 的 schema。每个 agent 拥有一段由调度器分配的固定槽位。

| 方面 | 行为 |
| --- | --- |
| 存储 | 每个 agent 三条数组：`int`、`float`、以字节存储的 `bool`。 |
| 寻址 | 槽位下标 `0 .. bbSlotCount - 1`；没有键名，也没有 schema。 |
| 值类型 | 只有 `int`、`float`、`bool`。`Float3`、`Long`、`Long2`、`Long3` 与 `object` 没有槽位。 |
| 条件节点 | `FlatNodeType.BlackboardCondition` 读取 **int** 数组并与 `CompareValue` 按 `CompareOp` 比较。float 与 bool 槽位可以被动作读取，但该节点无法比较它们。 |
| 越界访问 | `SetBBInt`、`GetBBInt` 及 float、bool 的对应方法在槽位越界时抛 `ArgumentOutOfRangeException`，agent 失效或过期时抛 `InvalidOperationException`。节点的 `BBKey` 超出槽位数时条件直接失败，而不是读到相邻内存。 |
| 生命周期 | 值属于该 agent，跨 tick 保留，直到 `ResetAgent(handle, clearBlackboard: true)`。 |
| 观察者 | 没有。托管黑板的观察者、中止模式与网络 scope 在这条路径上不存在。 |
| 容量 | `bbSlotCount` 在调度器构造时固定，对所有 agent 生效。 |
| 线程 | 创建者与 owner 线程亲和；每次访问都会先完成未完成的 job 再触碰数组。 |

两套黑板之间没有桥接。需要 `Float3` 或 `object` 的树走托管运行时；需要成千上万个同构 agent 的树走这条路径，使用缩减后的值类型集合。

## <a id="composite-example"></a>12. 综合示例：一棵多层树

本示例把严格 schema、observer abort、`Hybrid` 重评估、Service、Parallel 完成策略、显式调度、可选 LOD 与事件唤醒组装进同一棵树。它是一个完整可编译的 `MonoBehaviour` composition root。

```csharp
using UnityEngine;
using CycloneGames.BehaviorTree.Core;
using CycloneGames.BehaviorTree.Core.Nodes.Compositors;
using CycloneGames.BehaviorTree.Core.Nodes.Decorators;
using CycloneGames.BehaviorTree.Runtime.Components;

public sealed class MultiLayerTreeRoot : MonoBehaviour
{
    private static readonly int PerceptionReadyKey =
        RuntimeBlackboard.DefaultStringHashFunc("PerceptionReady");
    private static readonly int TaskStageKey =
        RuntimeBlackboard.DefaultStringHashFunc("TaskStage");
    private static readonly int SignalLevelKey =
        RuntimeBlackboard.DefaultStringHashFunc("SignalLevel");
    private static readonly int WorkCounterKey =
        RuntimeBlackboard.DefaultStringHashFunc("WorkCounter");
    private static readonly int ScoreAKey =
        RuntimeBlackboard.DefaultStringHashFunc("ScoreA");
    private static readonly int ScoreBKey =
        RuntimeBlackboard.DefaultStringHashFunc("ScoreB");

    private RuntimeBehaviorTree _tree;
    private BTPriorityTickManagerComponent _priorityManager;
    private BTDistanceLODProvider _lodProvider;
    private BTLODConfig _lodConfig;
    private GameObject _managerObject;

    private void Awake()
    {
        // (1) Strict schema: defaults, validation, and sync flags.
        RuntimeBlackboardSchema schema = new RuntimeBlackboardSchemaBuilder()
            .AddBool("PerceptionReady", false, RuntimeBlackboardSyncFlags.LocalOnly)
            .AddInt("TaskStage", 0, RuntimeBlackboardSyncFlags.SnapshotAndDelta)
            .AddInt("SignalLevel", 0, RuntimeBlackboardSyncFlags.LocalOnly)
            .AddInt("WorkCounter", 0, RuntimeBlackboardSyncFlags.Delta)
            .AddFloat("ScoreA", 0f, RuntimeBlackboardSyncFlags.LocalOnly)
            .AddFloat("ScoreB", 0f, RuntimeBlackboardSyncFlags.LocalOnly)
            .Build();

        var blackboard = new RuntimeBlackboard(schema: schema);

        // (2)-(6) Multi-layer tree.
        _tree = new RuntimeBehaviorTreeBuilder()
            .WithBlackboard(blackboard)
            .WithTickInterval(2)
            .Selector(selector =>
            {
                selector.AbortType = RuntimeAbortType.Both;
                selector.ReevaluationMode = RuntimeReevaluationMode.Hybrid;
                selector.ReevaluationPollInterval = 4;
            })
                // Branch A: observer-abort guard + Parallel finish policy.
                .Decorator(new RuntimeBBComparisonNode(), guard =>
                {
                    guard.KeyHash = PerceptionReadyKey;
                    guard.ValueType = BehaviorTreeComparisonValueType.Bool;
                    guard.Operator = BBComparisonOp.Equal;
                    guard.RefBool = true;
                    guard.AbortMode = RuntimeAbortType.Both;
                })
                    .Parallel(RuntimeParallelMode.UntilAnySuccess)
                        .Action(bb =>
                        {
                            bb.SetInt(WorkCounterKey, bb.GetInt(WorkCounterKey) + 1);
                            return RuntimeState.Success;
                        }, "CountWork")
                        .Wait(0.25f)
                    .End()          // Parallel
                .End()              // RuntimeBBComparisonNode
                // Branch B: Service + Utility selection under Hybrid reevaluation.
                .Decorator(new RuntimeServiceNode(), service =>
                {
                    service.Interval = 0.25f;
                    service.OnServiceTick = bb =>
                    {
                        int stage = bb.GetInt(TaskStageKey);
                        bb.SetFloat(ScoreAKey, stage * 0.5f);
                        bb.SetInt(SignalLevelKey, bb.GetInt(SignalLevelKey) + 1);
                    };
                })
                    .UtilitySelector(new[] { ScoreAKey, ScoreBKey })
                        .Action(bb =>
                        {
                            bb.SetInt(TaskStageKey, bb.GetInt(TaskStageKey) + 1);
                            return RuntimeState.Success;
                        }, "AdvanceStage")
                        .Action(bb => RuntimeState.Success, "HoldStage")
                    .End()          // UtilitySelector
                .End()              // RuntimeServiceNode
            .End()                  // Selector
            .Build();

        _tree.Terminated += state => Debug.Log($"Tree terminated: {state}");
    }

    private void Start()
    {
        // (7) Scheduling + explicit composition root + optional LOD provider.
        _managerObject = new GameObject("[BTPriorityTickManager]");
        _lodProvider = _managerObject.AddComponent<BTDistanceLODProvider>();
        _priorityManager =
            _managerObject.AddComponent<BTPriorityTickManagerComponent>();

        _lodConfig = ScriptableObject.CreateInstance<BTLODConfig>();
        _lodConfig.PriorityBudgets = new[] { 64, 32, 16, 8 };
        _priorityManager.Config = _lodConfig;
        _priorityManager.SetReferencePoint(transform);
        _priorityManager.Register(_tree, transform);
    }

    // External event stream: coalesced epoch + bounded immediate ticks.
    public void PushExternalEvent()
    {
        if (_tree == null) return;
        _tree.Evaluation.RequestReevaluation();
        _tree.WakeUp(boostedTicks: 2);
    }

    // Perception data change: an observer-driven abort request.
    public void PushPerceptionChange(bool ready)
    {
        if (_tree == null) return;
        _tree.Blackboard.SetBool(PerceptionReadyKey, ready);
    }

    private void OnDestroy()
    {
        if (_priorityManager != null && _tree != null)
        {
            _priorityManager.Unregister(_tree);
        }
        _tree?.Dispose();
        if (_lodConfig != null) Destroy(_lodConfig);
        if (_managerObject != null) Destroy(_managerObject);
    }
}
```

逐步讲解：

1. schema 声明六个 key。`TaskStage` 使用 `RuntimeBlackboardSyncFlags.SnapshotAndDelta`，`WorkCounter` 使用 `Delta`，其余为 `LocalOnly`。Blackboard 构造时应用默认值，此后的每次写入都按该契约校验。
2. `WithBlackboard(blackboard)` 注入这份绑定 schema 的 board；`WithTickInterval(2)` 设置基线节奏。builder 只使用一次。
3. 根 `RuntimeSelector` 使用 `AbortType.Both` 与 `ReevaluationMode.Hybrid`，`ReevaluationPollInterval = 4`，因此 guard 重扫既由事件触发，也按有界的轮询节奏触发。
4. Branch A 用 `AbortMode` 为 `Both` 的 `RuntimeBBComparisonNode` 包裹 `Parallel(RuntimeParallelMode.UntilAnySuccess)`。`PerceptionReady` 变化会触发 observer，进而调用 `RuntimeBehaviorTree.RequestReevaluation()`，让 selector 重扫 guard。
5. Parallel 完成策略在首个 `Success` 时结束该 branch，并中止仍处于 `Running` 的兄弟节点。
6. Branch B 用 `RuntimeServiceNode` 包裹 `UtilitySelector`。Service 在 child 执行期间按间隔刷新 score key；UtilitySelector 读取两个 `float` score key，运行得分最高的 child。
7. `Start()` 先挂载 `BTDistanceLODProvider`，再添加 `BTPriorityTickManagerComponent`，使 manager 在初始化时解析到它；随后赋 `BTLODConfig`、设置 reference point 并注册树。
8. `PushExternalEvent()` 与 `PushPerceptionChange(bool)` 是两个生产者入口：前者直接发出 epoch 与 wake-up budget，后者写入被跟踪 key，由 observer 路径发出信号。`OnDestroy()` 按顺序注销并释放。

## <a id="combining"></a>13. 与其他 CycloneGames 包组合

模块不硬依赖其他包。集成的形态只有一种：外部系统写入一个 Blackboard key，行为树对它作出反应。

### 感知：AIPerception

把当前 sighting 发布为事实，让装饰器观察该 key。写入会唤醒行为树，不需要每帧轮询节点。

```csharp
using CycloneGames.AIPerception.Runtime;
using CycloneGames.BehaviorTree.Core;
using UnityEngine;

public sealed class PerceptionBridge : MonoBehaviour
{
    private AIPerceptionComponent _perception;
    private RuntimeBlackboard _blackboard;

    public void Bind(AIPerceptionComponent perception, RuntimeBlackboard blackboard)
    {
        _perception = perception;
        _blackboard = blackboard;
    }

    private void Update()
    {
        if (_perception == null || _blackboard == null) return;

        IPerceptible target = _perception.GetClosestSightTarget();
        bool hasTarget = _perception.HasSightDetection && target != null;
        _blackboard.SetBool("HasTarget", hasTarget);
        _blackboard.SetFloat("TargetDistance",
            hasTarget ? Vector3.Distance(transform.position, target.Position) : float.MaxValue);
    }
}
```

在 `HasTarget` 上放一个 `RuntimeBBComparisonNode`，把 `AbortMode` 设为 `Self`、`LowerPriority` 或 `Both`，写入就会变成重评估请求。`IPerceptible.IsFromMemory` 用于区分实时 sighting 与记忆中的位置。

### Tag：GameplayTags

`GameplayTag` 由 tag manager 持有。启动时请求一次，然后把名称哈希成 Blackboard key。树需要同时判断多个 tag 时，也可以把只读容器作为 `Object` 值存入。

```csharp
using CycloneGames.GameplayTags;
using CycloneGames.BehaviorTree.Core;

GameplayTag alerted = GameplayTagManager.Request("State.Alerted");
int alertedKey = RuntimeBlackboard.DefaultStringHashFunc("State.Alerted");

// 每帧，在 owner 线程。`view` 是你的 tag 组件暴露的只读容器。
_blackboard.SetBool(alertedKey, view.HasTag(alerted));
_blackboard.SetObject("TagContainer", view);
```

`Object` key 始终是 LocalOnly，不会进入 snapshot 或 delta。需要同步的决策请放在 primitive key 上。

### 技能：GameplayAbilities

叶子节点很适合启动技能。把 spec 留在 composition 层，调用 `TryActivateAbility`，并把结果写成 key，让 guard 可以响应。

```csharp
using CycloneGames.GameplayAbilities;
using CycloneGames.BehaviorTree.Core;

public sealed class ActivateAbilityNode : RuntimeNode
{
    private readonly AbilitySystemComponent _abilities;
    private readonly GameplayAbilitySpec _spec;

    public ActivateAbilityNode(AbilitySystemComponent abilities, GameplayAbilitySpec spec)
    {
        _abilities = abilities;
        _spec = spec;
    }

    protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
    {
        bool started = _abilities.TryActivateAbility(_spec);
        blackboard.SetBool("AbilityStarted", started);
        return started ? RuntimeState.Success : RuntimeState.Failure;
    }
}
```

在技能结束回调或 `AbilityTask_WaitAbilityEnd` 中再次写入 `AbilityStarted`，用来驱动下一个分支。

### 框架：GameplayFramework

框架的 actor 持有 runner，并决定行为树何时更新。行为树不假设更新来源：`Self`、`Managed`、`PriorityManaged` 与 `ManualTick` 都成立。

```csharp
using CycloneGames.BehaviorTree.Runtime;
using CycloneGames.BehaviorTree.Runtime.Components;

// 在框架 actor 上，tree 资产赋值之后。
var runner = gameObject.AddComponent<BTRunnerComponent>();
runner.SetTree(treeAsset);
// tick mode 是序列化字段，在 Inspector 中选择。
```

如果控制器自己有更新时钟，可以保留 `ManualTick()`，由框架更新调用，而不挂 manager。

### 空间：Spatial

邻近决策放在同级包 `CycloneGames.BehaviorTree.Spatial` 里，不在本包。它把最近邻查询的结果写进黑板槽位，树只读普通键，本包因此不需要引入任何空间依赖。接线方式见那个包的 README：每个 agent 每次更新查询一次，随后用 `RuntimeBBComparisonNode` 比较产出的 `bool` 与 `float` 槽位。

## <a id="performance"></a>14. 性能、容量与测量

稳态低分配结构来自托管运行时的构建方式。组合节点把 setup 阶段的 child list 一次性转换为 sealed array，每个执行 step 都读取该数组。强类型 Blackboard 存储避免装箱 primitive 值，`RuntimeBlackboard.DefaultStringHashFunc` 的结果可预先 hash。manager scan 复用其存储，重评估 epoch 是一次不产生队列的 `Interlocked.Increment`。`Tick` 期间不解析严格 schema；运行时访问使用强类型存储与预计算整数 hash，绑定 schema 的写入在 mutation 之前多一次 dictionary 契约查询。dictionary 扩容、首次 array、observer 订阅变更、编译操作、schema 构造与 capacity 扩容都会分配。未使用可复用 buffer API 时，序列化也可能分配。

图校验是有界的。`RuntimeBehaviorTreeLimits` 限制纯代码 node count 与深度，`BTCapacityLimits.MaxTreeDepth` 是托管校验与平面树校验共同消费的上限。`BTCapacityLimits.MaxNodeCount`、`DefaultNodeCount`、`DefaultAgentCount`、`MaxAgentCount`、`PriorityLevelCount`、`DefaultTickManagerCapacity`、`DefaultPriorityBucketCapacity` 暴露其余上限。这些是安全边界，不是容量目标。

### 选择调度配置

配置取决于每帧需要运行多少棵树，而不是某张 benchmark 表。先按下面的规则起步，再在你实际发布的构建里测量：

| 负载 | 建议起点 |
| --- | --- |
| 少于 100 agent | 图省事用 `Self`，或用 `Managed` 且 budget 不小于 agent 数。 |
| 数百 agent | 用 `Managed`；先提高 `TickBudget` 直到帧成本符合预算，再提高 `TickInterval`。 |
| 数千 agent | 用 `PriorityManaged` 加 distance LOD，让远处的树降低 tick 频率。 |
| 数万 agent | LOD 且 `TickInterval` 至少为 4，或把同构场景交给 DOD 路径。 |
| Reactive composite 宽度超过约 128 | 改用 `ReevaluationMode.OnChange` 或 `Hybrid`；全扫会在每个 tick 访问每个 child。 |

`Managed` 的 budget 小于 agent 数不构成公平性问题：游标是持久的，每棵树都会轮流被访问。它降低的是单树 tick 频率，这通常正是 budget 的目的。

### 测量

吞吐、分配与 soak 使用 Benchmark 窗口（`Tools > CycloneGames > Behavior Tree > Behavior Tree Benchmark`），可导出 CSV 与 JSON。Editor 内的逐帧分配请用 `ProfilerRecorder` 的 `GC Allocated In Frame`；`GC.GetAllocatedBytesForCurrentThread()` 在 Editor 中恒为 0，不能用于此处。

### 调试行为树

编辑器显示运行中树的实时状态：节点颜色与状态标签、当前 child 索引，以及时间节点的已耗时。树不再推进时，先检查 runner 的 tick mode 与是否解析到 manager；managed runner 没有 manager 时会输出一次警告并在本地 tick。编译与校验失败由 `BehaviorTreeCompiler` 在任何 runtime node 创建之前报告，非法图不会进入 `Tick`。

结构性保证来自源码：密封的 child 数组、类型化存储、重评估 epoch、延迟变更、深度与容量上限。每 tick 成本、分配与常驻内存取决于构建目标、后端、硬件与 workload，请在你自己的环境中测量。

## <a id="api"></a>15. API 速查表

| 类型 / 成员 | 用途 |
| --- | --- |
| `RuntimeBehaviorTreeBuilder` | 纯代码流式 builder；`Build()` 返回一棵 `RuntimeBehaviorTree`。 |
| `RuntimeBehaviorTree` | `Tick()`、`Play()`、`Stop()`、`Dispose()`、`Terminated`、`State`、`IsStopped`、`Blackboard`、`Root`、`Evaluation`、`TickInterval`、`WakeUp`、`RequestReevaluation`、`ShouldTick`、`GetOwner<T>()`、`GetService<T>()`、`SetContext`。 |
| `RuntimeState` | `NotEntered`、`Running`、`Success`、`Failure`。 |
| `RuntimeBlackboard` | 八种强类型 `Set*`/`Get*`/`TryGet*` 存储，`Parent`、`GetStamp`、`HasKey`、`Remove`、`Clear`、`ResetToSchemaDefaults`、`AddObserver`、`AddGlobalObserver`、`RemoveObserver`、`ClearAllObservers`、`EnableConcurrentStorageAccess`、`ResetDefaultRandom`、`WriteTo`、`ReadFrom`、`ComputeHash`、`Schema`、`BindSchema`。 |
| `RuntimeBlackboard.DefaultStringHashFunc` | 稳定的 `BTHash.FNV1A` 字符串到 `int` key hash。 |
| `RuntimeBlackboardSchemaBuilder` / `RuntimeBlackboardSchema` | `AddInt`、`AddFloat`、`AddBool`、`AddFloat3`（Unity 侧另有 `AddVector3`）、`AddLong`、`AddLong2`、`AddLong3`、`AddObject`、`Build`、`Count`、`ContractVersion`、`IsKnownKey`、`UsesSnapshot`、`UsesDelta`、`IsNetworkedKey`、`TryGetDefinition`、`ValidateWrite`。 |
| `RuntimeBlackboardSyncFlags` | `LocalOnly`、`Snapshot`、`Delta`、`SnapshotAndDelta`。 |
| `RuntimeBlackboardNetworkScope` | `Snapshot`、`Networked`。 |
| `BTCapacityLimits` | `MaxTreeDepth`、`MaxNodeCount`、`DefaultNodeCount`、`MaxAgentCount`、`DefaultAgentCount`、`PriorityLevelCount`、`DefaultTickManagerCapacity`、`DefaultPriorityBucketCapacity`。 |
| `RuntimeBTEvaluationController` | `Epoch`、`RequestReevaluation()`、`ShouldReevaluate(...)`。 |
| `RuntimeReevaluationMode` | `PollEveryTick`、`OnChange`、`Hybrid`。 |
| `RuntimeCompositeNode` | `AbortType`、`ReevaluationMode`、`ReevaluationPollInterval`、`Children`、`ChildCount`、`Seal()`、`AddChild`、`CurrentIndex`。 |
| `RuntimeAbortType` | `None`、`Self`、`LowerPriority`、`Both`。 |
| `RuntimeBBComparisonNode` | `KeyHash`、`Operator`、`ValueType`、`RefInt`、`RefFloat`、`RefBool`、`RefKeyHash`、`UseRefKey`、`FloatEpsilon`、`AbortMode`。 |
| `BehaviorTreeComparisonValueType` / `BBComparisonOp` | `Int`、`Float`、`Bool`、`Object`；`Equal`、`NotEqual`、`GreaterThan`、`GreaterOrEqual`、`LessThan`、`LessOrEqual`、`IsSet`、`IsNotSet`。 |
| `RuntimeParallelMode` / `RuntimeParallelNode.Mode` | `Default`、`UntilAnyComplete`、`UntilAnyFailure`、`UntilAnySuccess`。 |
| `RuntimeParallelAllNode` | `SuccessThreshold`、`FailureThreshold`。 |
| `RuntimeServiceNode` | `Interval`、`RandomDeviation`、`UseUnscaledTime`、`OnServiceTick`。 |
| `RuntimeSubTreeNode` | `SetPortRemapping(...)`，配合 `RuntimeSubTreePortDirection.Input`、`Output`、`InOut`。 |
| `RuntimeNode.EmitWakeUpSignal()` | protected 的节点侧重评估信号。 |
| `IRuntimeBTCommand` / `IRuntimeBTConditionStrategy` | 可复用的 command 与 condition 对象。 |
| `RuntimeBTContext` | `Owner`、`ServiceResolver`、`GetOwner<T>()`、`GetService<T>()`；builder 上的 `WithOwner`、`WithContext`、`WithServiceResolver`。 |
| `BTTickManager` / `BTPriorityTickManager` | `TickBudget`、`SetBudgets`、`Register`、`TryRegister`、`Unregister`、`TryUnregister`、`UpdatePriority`、`TryUpdatePriority`、`Tick`、`Clear`、`GetMemoryStats`、`GetTreeCount`、`GetTotalCount`。 |
| `BTTickManagerComponent.CreateInstance()` / `Instance` | Managed 模式组件的创建与解析。 |
| `BTPriorityTickManagerComponent.CreateInstance()` / `Instance` | PriorityManaged 模式组件的创建与解析；`Config`、`Register(tree, transform)`、`BoostPriority`、`SetReferencePoint`。 |
| `TickMode` | `Self`、`Manual`、`Managed`、`PriorityManaged`。 |
| `BTRunnerComponent` | `ManualTick`、`SetTickMode`、`SetTickInterval`、`WakeUp`、`BoostPriority`、`Pause`、`Resume`、`Stop`、`Play`、`BTSetData`、`SetContext`、`SetServiceResolver`、`OnTreeStopped`、`RuntimeTree`。 |
| `BTDistanceLODProvider` | 可选 LOD 来源：`Config`、`ReferencePoint`、`MaximumTreeCount`、`TryRegisterTree`、`UnregisterTree`、`GetPriority`、`GetTickInterval`、`UpdateAllLOD`、`BoostPriority`。 |
| `BTLODConfig` | `Levels`、`PriorityBudgets`、`BoostedPriority`、`BoostedTickInterval`、`TryValidate`、`GetLODLevel`、`GetLODLevelSqr`。 |
| `BTNetworkSync` / `BTStateSnapshot` / `BTStateSnapshotBuffer` | `CaptureSnapshot`、`SerializeSnapshot`、`DeserializeSnapshot`、`ApplyBlackboardSnapshot`、`CheckDesync`、`ComputeTreeStateHash`、`DoesExecutionStateMatch`。 |
| `BTBlackboardDelta` | `CreateForSchema`、`TrackKey`、`Attach`、`Detach`、`TryFlush`、`Apply`。 |
| `FlatBehaviorTree` / `BTTickScheduler` | DOD definition owner 与 agent scheduler：`AddAgent`、`TryAddAgent`、`RemoveAgent`、`ResetAgent`、`ScheduleTick`、`CompleteTick`、`SetBBInt`、`GetRootState`、`TryGetActionRequest`、`TrySetActionStatus`、`ComputeAgentStateHash`。 |
| `FlatNodeDef` / `FlatNodeType` / `CompareOp` / `BTAgentHandle` | 平面树节点定义、节点类型枚举、比较运算符，以及带 generation 的 agent handle。 |
