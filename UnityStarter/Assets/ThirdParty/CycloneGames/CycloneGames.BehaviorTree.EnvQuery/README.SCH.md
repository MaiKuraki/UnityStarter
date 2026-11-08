# CycloneGames.BehaviorTree.EnvQuery

[English](README.md)

- [设计目的](#purpose)
- [程序集与依赖](#assemblies)
- [设计模型](#model)
- [使用步骤](#usage)
- [语义约定](#semantics)
- [复杂度与内存](#cost)
- [API](#api)
- [已知边界](#limits)

## <a id="purpose"></a>设计目的

执行一次环境查询，把最优候选位置发布到黑板槽位，使行为树通过普通键读取结果，而不必持有查询对象。

适配器不决定"什么位置算好"。候选生成与评分属于查询本身，由调用方配置一次并长期持有。

## <a id="assemblies"></a>程序集与依赖

| Assembly | 作用 | 使用方引用方式 |
| --- | --- | --- |
| `CycloneGames.BehaviorTree.Runtime.Integrations.EnvQuery` | 执行查询并发布最优候选。 | 显式引用 |
| `CycloneGames.BehaviorTree.Integrations.EnvQuery.Tests.Editor` | 契约、随机、分配与长稳测试。 | 仅 Test Runner |

两个程序集均为 `noEngineReferences: true`。查询核心与黑板都不依赖引擎，因此同一份适配器既可跑在客户端，也可跑在无头进程。

## <a id="model"></a>设计模型

适配器持三样东西：查询运行器、结果数组，以及构造时传入的生成器与测试。

| 调用 | 作用 |
| --- | --- |
| `Run(context)` | 生成候选、依次施加测试、原地排序、保留前 `Capacity` 条。 |
| `PublishBest` | 写入"是否找到"的 `bool` 与最优分数 `float`。 |
| `PublishBestPosition` | 以 `BTFloat3` 写入最优位置。 |
| `PublishCount` | 写入保留的候选条数。 |

顺序由查询决定：按分数降序，同分按候选下标。固定的生成器、测试与上下文每次得到相同输出，这也是回放与服务端重算能与客户端一致的前提。

## <a id="usage"></a>使用步骤

### 1. 构造一次

```csharp
var generator = new GridAroundQuerierGenerator(spacing: 1.5f, radius: 8f);
var tests = new IEnvQueryTest[]
{
    new DistanceTest(range: 15f, useTarget: true),
    new FacingTest(weight: 0.5f)
};

var bridge = new EnvQueryBlackboardBridge(generator, tests, maxCandidates: 64, resultCapacity: 4);
```

`maxCandidates` 约束运行器的缓冲区与单次查询成本。`resultCapacity` 是适配器保留并可发布的候选条数。

第五个参数接收 `IEnvQueryFilter[]`。过滤器在评分之前执行，被拒绝的候选不会占用结果槽位：

```csharp
var filters = new IEnvQueryFilter[] { new ReachableFilter(navigation) };
var bridge = new EnvQueryBlackboardBridge(generator, tests, 64, 4, filters);
```

### 2. 每帧组装上下文

```csharp
var context = new EnvQueryContext
{
    QuerierX = transform.position.x,
    QuerierY = transform.position.y,
    QuerierZ = transform.position.z,
    TargetX = target.x,
    TargetY = target.y,
    TargetZ = target.z,
    HasTarget = hasTarget
};
context.SetForward(transform.forward.x, transform.forward.y, transform.forward.z);
```

`SetForward` 会归一化输入，因此非单位向量不会让 `FacingTest` 失真。

### 3. 执行并发布

```csharp
bridge.Run(in context);
bridge.PublishBest(blackboard, foundKey, scoreKey);
bridge.PublishBestPosition(blackboard, positionKey);
bridge.PublishCount(blackboard, countKey);
```

调用点必须在创建适配器的线程上。运行器在两次调用之间保留缓冲区，不支持共享。

### 4. 在树里读这些槽位

```csharp
builder.Condition(bb => bb.GetBool(foundKey));
builder.Action(bb => bb.GetFloat(scoreKey) > 0.5f
    ? RuntimeState.Success
    : RuntimeState.Failure);
```

位置槽是 `BTFloat3`，Unity 侧通过扩展方法与 `Vector3` 互转。

## <a id="semantics"></a>语义约定

- 生成器没有产出时，`Run` 返回零，`PublishBest` 写入 `false` 与零分数，位置槽写入 `BTFloat3.Zero`。
- 依赖目标的测试在 `HasTarget` 为 false 时不贡献分数，而不是以原点作为目标参与计算。
- 发布走 `SetBool`、`SetFloat`、`SetFloat3` 与 `SetInt`。黑板会丢弃未变化的写入，因此结果稳定时既不通知观察者，也不进入网络增量。
- `Run` 覆盖上一次结果。需要历史时，调用方应在下一次调用前把候选复制出去。
- `Dispose` 释放运行器缓冲区并清空结果数组；生成器与测试仍由调用方持有。

## <a id="cost"></a>复杂度与内存

| 环节 | 复杂度 |
| --- | --- |
| 生成 | 与生成器扫描的候选数量成正比。 |
| 评分 | 候选数乘以测试数。 |
| 排序 | 候选数的 `n log n`，原地完成。 |
| `Publish*` | 各自常数时间，主要开销是黑板写入。 |

内存在构造时固定：运行器的三个 `maxCandidates` 长度数组，加一个 `resultCapacity` 长度的结果数组。单次运行不再分配。

## <a id="api"></a>API

```csharp
EnvQueryBlackboardBridge(IEnvQueryGenerator generator, IEnvQueryTest[] tests,
                         int maxCandidates = 64, int resultCapacity = 4,
                         IEnvQueryFilter[] filters = null);

int Run(in EnvQueryContext context);

void PublishBest(RuntimeBlackboard blackboard, int foundKey, int scoreKey);
void PublishBestPosition(RuntimeBlackboard blackboard, int positionKey);
void PublishBestPayload(RuntimeBlackboard blackboard, int payloadKey);
void PublishCount(RuntimeBlackboard blackboard, int countKey);

int Count { get; }
int Capacity { get; }
bool HasResult { get; }
EnvQueryPoint Best { get; }
ReadOnlySpan<EnvQueryPoint> Results { get; }
```

## <a id="limits"></a>已知边界

- 不做寻路与投影。候选点可能不可达，只有调用方提供的测试能够排除它。
- 一个适配器对应一个线程。不支持多个 agent 共享；要么每个 agent 各自持有，要么统一执行一次再写入共享黑板。
- 不保留历史结果。数组每次运行都会被覆盖。
- 成本随 `maxCandidates` 增长。逐帧路径上的大候选预算，应当由它换来的查询质量来证明，而不是为了方便。
