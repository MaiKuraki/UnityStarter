# CycloneGames.AIEnvQuery

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

在一组候选位置中，按可组合的加权规则选出一个位置。

每条规则是一个独立对象，因此一次查询可以逐条阅读、调整顺序、重新分配权重并单独测试。模块不为位置赋予含义：它接收坐标、给出分数、按分数返回。

四类职责刻意排除在外：寻路、投影到导航表面、可达性校验与编辑器资产。候选点可能不可达，排除它属于调用方提供的测试。

## <a id="assemblies"></a>程序集与依赖

| Assembly | 作用 | 使用方引用方式 |
| --- | --- | --- |
| `CycloneGames.AIEnvQuery.Core` | 生成器、测试与评分运行器。 | 显式引用 |
| `CycloneGames.AIEnvQuery.Tests.Editor` | 生成覆盖、评分顺序、确定性与分配测试。 | 仅 Test Runner |

Core 程序集没有任何依赖，也不引用引擎。视线检测是一个委托，因此包本身不引用物理，只要调用方能提供检测方法即可使用。

## <a id="model"></a>设计模型

一次查询分三个阶段，存储量固定。

| 阶段 | 类型 | 行为 |
| --- | --- | --- |
| 生成 | `IEnvQueryGenerator` | 把候选写入缓冲区，返回写入数量。 |
| 评分 | `IEnvQueryTest` | 为单个候选累加一个 0..1 的值，并携带权重。 |
| 排序 | `EnvQueryRunner` | 汇总 `值 × 权重`，原地排序，返回前 N 个。 |

缓冲区由运行器持有。`maxCandidates` 在构造时确定其长度，同时决定单次查询的成本上界。

评分是可累加的：每个测试贡献 `值 × 权重`，移除一个测试只会移除它自己的贡献。运行器不感知测试的具体种类，只遍历收到的数组。

上下文承载所有测试共用的输入：查询者位置、归一化朝向、目标位置，以及目标是否存在。

## 扩展点

工程侧逻辑有四处入口，都是普通接口或字段，运行器不需要知道它们的存在。

| 入口 | 替代的是什么 | 典型用途 |
| --- | --- | --- |
| `IEnvQueryGenerator` | 候选从哪来 | 关卡标记表、导航体积、服务端生成点集 |
| `IEnvQueryFilter` | 无；过滤器在生成与评分之间执行 | 剔除不可达点、禁用槽位、区域外坐标 |
| `IEnvQueryTest` | 候选如何评分 | 到掩体边缘的距离、点的威胁度、自定义掩体质量 |
| `EnvQueryPoint.Payload` | 无；运行器原样携带 | 源点的行号或标识，让胜出点无需坐标查找即可映射回数据 |
| `EnvQueryContext.UserData` | 无；运行器从不读取 | 测试需要的工程数据，使测试实例可共享而不必每个 agent 一份 |

过滤器不是零权重测试。被拒绝的候选会完全离开本次运行，不会占用结果槽位；权重为零的测试只是不再贡献分数。

自定义实现不得分配：它们位于逐帧路径上，测试与过滤器对每个候选各调用一次。

### 重负载查询留在运行器之外

有两项成本控制刻意没有放进核心：分数整形与分帧执行。它们属于策略，且都能由公开接口组合出来。

分数整形就是一个测试。偏好区间中段、或对距离取平方，都是写进 `IEnvQueryTest` 的曲线。运行器只负责乘以权重，无需改动。

分帧执行就是一个工作项。`CycloneGames.Scheduling` 按预算步进 `IWorkItem`；工作项可以一次 `Generate`，然后在每次 `Step()` 中给固定数量的候选评分，保留当前最优。这条路径不走运行器的排序，因此只在单次查询确实塞不进一帧时才值得使用。

更便宜的做法优先：降低 `maxCandidates`，或让每个 agent 每 N 帧查询一次并复用上次结果。分帧查询用"失去排序保证与额外代码"换取"单帧成本有上界"。

## <a id="usage"></a>使用步骤

### 1. 最小查询

```csharp
var runner = new EnvQueryRunner(maxCandidates: 128);
var generator = new GridAroundQuerierGenerator(spacing: 1f, radius: 6f);
var tests = new IEnvQueryTest[] { new DistanceTest(range: 10f) };
var results = new EnvQueryPoint[1];

var context = new EnvQueryContext { QuerierX = 0f, QuerierY = 0f, QuerierZ = 0f };
int count = runner.Run(generator, tests, in context, results);
```

`results[0]` 是距查询者最近的候选，`count` 是实际写入的数量，不会超过 `results.Length`。

### 2. 追加规则与权重

```csharp
context.SetForward(0f, 0f, 1f);

var tests = new IEnvQueryTest[]
{
    new DistanceTest(range: 10f, weight: 1f),
    new FacingTest(weight: 3f)
};
```

`FacingTest` 对正前方的候选返回 1，对正后方返回 0。权重 3 表示其影响是距离测试的三倍。调整影响范围不需要改动运行器。

### 3. 使用自定义候选来源

```csharp
var points = new EnvQueryPoint[] { new EnvQueryPoint(4f, 0f, 2f), new EnvQueryPoint(-3f, 0f, 8f) };
var generator = new PointListGenerator(points, maximumDistance: 20f);
```

距离过滤发生在生成阶段，半径之外的候选不会进入评分循环。

### 4. 引入目标与可见性条件

```csharp
var context = new EnvQueryContext
{
    QuerierX = 0f, QuerierY = 0f, QuerierZ = 0f,
    TargetX = 12f, TargetY = 0f, TargetZ = 4f,
    HasTarget = true
};

var tests = new IEnvQueryTest[]
{
    new DistanceTest(range: 30f, weight: 1f, useTarget: true),
    new LineOfSightTest(trace: HasLineOfSight, weight: 2f)
};

static bool HasLineOfSight(float fromX, float fromY, float fromZ, float toX, float toY, float toZ)
{
    return true; // 替换为调用方的检测：物理射线、导航网格、预烘焙可见性、服务端碰撞。
}
```

`HasTarget` 为 false 时，依赖目标的测试不贡献分数，而不是以原点作为目标位置参与计算。

### 5. 读取多个结果

```csharp
var results = new EnvQueryPoint[4];
int count = runner.Run(generator, tests, in context, results);
for (int i = 0; i < count; i++)
{
    // results[i] 是第 i 优的候选，包含分数。
}
```

返回多个结果使调用方可以追加自己的决胜条件（例如路径长度），无需重新执行查询。

## <a id="semantics"></a>语义约定

- 测试写入 0..1 的值。超出范围仍可运行，但权重在不同查询之间不再可比。
- 权重为零等效于停用该测试，无需从数组中移除。
- 分数相同时按候选下标排序，因此固定的生成器与上下文每次输出完全一致。
- 生成器产出的候选超过 `maxCandidates` 时，只写入缓冲区容纳的部分，其余丢弃。
- 一个运行器属于一个线程；两次运行之间保留缓冲区，不支持并发调用。
- 单次查询不分配，生成器与内置测试同样不分配。

## <a id="cost"></a>复杂度与内存

| 阶段 | 复杂度 |
| --- | --- |
| 生成 | 与生成器扫描的候选数量成正比。 |
| 评分 | 候选数乘以测试数。 |
| 排序 | 候选数的 `n log n`，原地完成。 |

内存在构造时固定：候选、分数与排序索引三个长度均为 `maxCandidates` 的数组。提高候选上限会同时提高耗时与内存。

## <a id="api"></a>API

```csharp
EnvQueryRunner(int maxCandidates = 256);
int Run(IEnvQueryGenerator generator, IEnvQueryTest[] tests,
        in EnvQueryContext context, EnvQueryPoint[] results);
int Run(IEnvQueryGenerator generator, IEnvQueryFilter[] filters, IEnvQueryTest[] tests,
        in EnvQueryContext context, EnvQueryPoint[] results);
int MaxCandidates { get; }

GridAroundQuerierGenerator(float spacing, float radius, float verticalOffset = 0f);
PointListGenerator(EnvQueryPoint[] source, float maximumDistance = 0f);

DistanceTest(float range, float weight = 1f, bool useTarget = false, bool preferFarther = false);
FacingTest(float weight = 1f);
LineOfSightTest(EnvQueryLineOfSight trace, float weight = 1f, float eyeHeight = 0f);
```

## <a id="limits"></a>已知边界

- 不做寻路与投影。候选点可能不可达，只有调用方提供的测试能够排除它。
- 不提供编辑器资产。一次查询由代码构造的实例组成，与执行它的逻辑放在一起。
- 不做异步或分帧执行。一次运行在调用帧内完成，`maxCandidates` 是控制该边界的参数。
- 不做结果缓存。每次调用都重新生成与评分；需要复用结果的调用方应在运行器之外自行保存。
