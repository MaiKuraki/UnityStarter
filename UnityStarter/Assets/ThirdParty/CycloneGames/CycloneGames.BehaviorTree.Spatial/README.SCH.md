# CycloneGames.BehaviorTree.Spatial

[English](README.md)

## 概述

这个包把 `CycloneGames.BehaviorTree` 与 CycloneGames Spatial 索引连接起来。它只提供一个适配器：执行一次最近邻查询，把结果写进黑板槽位，因此行为树按邻近关系决策时既不需要持有索引引用，行为树包本身也不依赖 Spatial。

需要邻近信息的场景用它：最近的敌人、半径内的数量、距离阈值。不做空间查询的树不需要它，`CycloneGames.BehaviorTree` 在没有它的情况下照常编译和运行。

## 程序集与依赖

| Assembly | 作用 | 使用方引用方式 |
| --- | --- | --- |
| `CycloneGames.BehaviorTree.Runtime.Integrations.Spatial` | 基于 `ISpatialReadOnlyIndex<TItem>` 实现「查询 → 黑板」发布器。 | 显式引用 |
| `CycloneGames.BehaviorTree.Integrations.Spatial.Tests.Editor` | 验证绑定、层过滤、结果上限与分配。 | 仅 Test Runner |

包在 `package.json` 中直接声明 BehaviorTree 与 Spatial 依赖。运行时 assembly 不依赖引擎（`noEngineReferences: true`），且 `autoReferenced: false`。

## 安装

### UPM

安装 `com.cyclone-games.behavior-tree-spatial` 即可，包管理器会解析它声明的 BehaviorTree 与 Spatial 依赖。不需要任何 scripting define symbol，`com.cyclone-games.behavior-tree` 也从不引用这个包。

### 嵌入 Assets

把本包、`CycloneGames.BehaviorTree` 与 `CycloneGames.Spatial` 一起放在项目的 Assets 树下。集成 asmdef 使用直接程序集引用，因此只要这些包根存在就参与编译。

## 用法

四步，每个 agent 每次更新执行一遍。

**1. 自己持有索引。** 适配器只查询你已有的索引，不负责创建或移动条目。

```csharp
var index = new SpatialHashGrid<int>(cellSize: 4f);
index.TryInsert(agentId, x, y, z, SpatialLayers.Default, out _);
```

**2. 每个写线程创建一个适配器。** buffer 在这里分配，更新循环里不再分配。

```csharp
var query = new SpatialBlackboardQuery<int>(index, maxResults: 4);
```

**3. 查询并发布。** 发布写两个黑板槽位：一个 `bool` 表示是否命中，一个 `float` 表示平方距离。

```csharp
query.QueryNearest(x, y, z, radius: 12f, layerMask: SpatialLayers.All);
query.PublishNearest(blackboard, visibleKey, squaredDistanceKey);
```

**4. 在树里读这两个槽位。** 它们就是普通键，任何黑板节点都能用。

```csharp
builder.Condition(bb => bb.GetBool(visibleKey));
builder.Action(bb => bb.GetFloat(squaredDistanceKey) < 144f
    ? RuntimeState.Success
    : RuntimeState.Failure);
```

距离保持平方，因为这是索引返回的单位：查询和比较都不需要开方。

`SpatialBlackboardQuery` 接受任意 `ISpatialReadOnlyIndex<TItem>`，实时 `SpatialHashGrid<TItem>`、逐帧构建的 `SpatialSnapshot<TItem>`，以及实现同一接口的自研索引都可以互换。

## 行为约定

- 发布走 `SetBool` 与 `SetFloat`。黑板会丢弃未变化的写入，因此最近目标稳定时既不通知 observer，也不进入网络增量。
- 没有命中时 `NearestSquaredDistance` 为 0，应先读可见性标志。
- 半径小于等于 0 时直接清空结果，不调用索引。
- 每个写线程一个实例。适配器不是线程安全的，索引生命周期由调用方持有。
