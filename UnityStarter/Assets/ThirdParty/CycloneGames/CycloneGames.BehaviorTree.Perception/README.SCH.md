# CycloneGames.BehaviorTree.Perception

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

把单个传感器的结果发布到黑板槽位，使行为树通过普通键读取感知，而不必持有传感器引用。

适配器只搬运传感器已经算好的数据。它不判定视野、记忆或阵营态度，也不决定"哪个目标更要紧"，只取传感器给出的最近目标。

## <a id="assemblies"></a>程序集与依赖

| Assembly | 作用 | 使用方引用方式 |
| --- | --- | --- |
| `CycloneGames.BehaviorTree.Runtime.Integrations.Perception` | 读取传感器并写入黑板。 | 显式引用 |
| `CycloneGames.BehaviorTree.Integrations.Perception.Tests.Editor` | 契约、随机、分配与长稳测试。 | 仅 Test Runner |

程序集引用 `CycloneGames.BehaviorTree.Core` 与 `CycloneGames.AIPerception`，且 `autoReferenced: false`：不读感知的项目不会编译到它。

## <a id="model"></a>设计模型

传感器维护的结果按距离升序排列，最近的在首位。适配器把前 `capacity` 条复制进一段连续的 `PerceptionTarget` 数组，因此索引 0 始终是上一次更新时的最近目标。

| 字段 | 含义 |
| --- | --- |
| `Handle` | 目标的 `PerceptibleHandle`。 |
| `Position` | 最后已知的世界坐标。 |
| `Distance` | 最后一次检测到的距离，单位与传感器一致。 |
| `Visibility` | 最后一次检测时的可见度，0..1；目标只存在于记忆时该值会衰减。 |
| `IsFromMemory` | 该目标来自记忆而非本次实际检测时为 true。 |

发布写四种槽位：可见标志、距离、位置（`BTFloat3`）与目标数量。仍在记忆中的目标依然算可见，因为传感器仍在上报它；需要区分时读 `IsFromMemory`。

距离按传感器的单位发布，即线性距离。Spatial 适配器发布的是平方距离，因为那是空间索引的原始单位。两者混用等于混用单位，键名应当写明存的是哪一种。

## <a id="usage"></a>使用步骤

### 1. 每个传感器一个适配器

```csharp
var bridge = new PerceptionBlackboardBridge(perception.SightSensor, capacity: 4);
```

`capacity` 只约束每次更新复制多少条目标，不改变传感器本身的计算量。

### 2. 每帧更新一次，位于感知之后

```csharp
bridge.Update();
bridge.PublishNearest(blackboard, visibleKey, distanceKey);
bridge.PublishNearestPosition(blackboard, positionKey);
bridge.PublishCount(blackboard, countKey);
```

调用点必须在提交感知结果的同一线程上，且位于感知更新之后。适配器在构造时记录线程，其他线程调用会被拒绝。

### 3. 在树里读这些槽位

```csharp
builder.Condition(bb => bb.GetBool(visibleKey));
builder.Action(bb => bb.GetFloat(distanceKey) < 12f
    ? RuntimeState.Success
    : RuntimeState.Failure);
```

槽位就是普通黑板键，任何条件、装饰器或比较节点都能用，无需知道感知的存在。

### 4. 容量吃紧时检查截断

```csharp
if (bridge.IsTruncated)
{
    // 上报数量超过缓冲区；提高容量，或接受"只取最近的 N 个"。
}
```

## <a id="semantics"></a>语义约定

- 传感器为空时写入 `false` 与零距离。使用距离前必须先读标志。
- 发布走 `SetBool`、`SetFloat`、`SetFloat3` 与 `SetInt`。黑板会丢弃未变化的写入，因此目标稳定时既不通知观察者，也不进入网络增量。
- `Update` 只读传感器，不推进传感器，也不访问注册表。
- 一个适配器服务一个传感器、一个线程。同一传感器上的两个适配器是允许的，读到的是同一份结果。
- `Dispose` 清空缓冲区，不释放其他资源；传感器生命周期仍由调用方持有。

## <a id="cost"></a>复杂度与内存

| 操作 | 复杂度 |
| --- | --- |
| `Update` | 与 `min(传感器结果数, capacity)` 线性相关。 |
| `Publish*` | 各自常数时间，主要开销是黑板写入。 |
| 内存 | 一个长度为 `capacity` 的 `PerceptionTarget` 数组，构造时一次性分配。 |

构造之后不再分配。`PerceptionTarget` 由句柄、位置、两个浮点与一个布尔组成的 readonly struct，数组连续，在本适配器的常见容量下遍历基本留在缓存内。

## <a id="api"></a>API

```csharp
PerceptionBlackboardBridge(ISensor sensor, int capacity = 8);

bool Update();

void PublishNearest(RuntimeBlackboard blackboard, int visibleKey, int distanceKey);
void PublishNearestPosition(RuntimeBlackboard blackboard, int positionKey);
void PublishCount(RuntimeBlackboard blackboard, int countKey);

int Count { get; }
int Capacity { get; }
bool IsTruncated { get; }
PerceptionTarget Nearest { get; }
ReadOnlySpan<PerceptionTarget> Targets { get; }
ISensor Sensor { get; }
```

## <a id="limits"></a>已知边界

- 一个适配器对应一个传感器。需要同时读视觉与听觉时，用两个适配器与两组键。
- 只取最近目标。按阵营、威胁度或可见性挑选目标留给调用方，因为这些规则每个项目不同。
- 只有拉取。适配器在 `Update` 时读取结果；需要"变化时响应"的消费方应使用感知的事件层。
- 不做距离换算。发布的是传感器的原值，既不开方也不平方。
