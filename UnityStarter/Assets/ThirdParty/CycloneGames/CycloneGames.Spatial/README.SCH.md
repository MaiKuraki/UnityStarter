# CycloneGames.Spatial

[English](README.md) | 简体中文

Unity 与纯 C# 宿主通用的均匀空间哈希网格。半径查询只走半径覆盖到的格子，成本跟附近元素的数量有关，跟总数无关。

## 目录

- [这是什么](#这是什么)
- [快速开始](#快速开始)
- [格子、句柄、层级](#格子句柄层级)
- [查询](#查询)
- [教程](#教程)
  - [1. 维护实体并查询周围](#1-维护实体并查询周围)
  - [2. 找到最近的敌人](#2-找到最近的敌人)
  - [3. 移动实体](#3-移动实体)
  - [4. 构建一次，查询多次](#4-构建一次查询多次)
  - [5. 选格尺寸](#5-选格尺寸)
  - [6. 大世界](#6-大世界)
- [参考](#参考)
  - [API](#api)
  - [容量](#容量)
  - [内存](#内存)
  - [复杂度](#复杂度)
  - [线程](#线程)
  - [分配](#分配)
  - [诊断](#诊断)
  - [上限](#上限)
  - [接口](#接口)
- [常见错误](#常见错误)

## 这是什么

世界被划分成均匀的格子，每格挂着落在其中的元素。半径查询遍历半径覆盖的格子，再逐个判断格里的元素。

需要每帧对数百到数十万实体做邻近查询、实体是纯数据而不是物理刚体、同一份代码还要能在无渲染的服务端或单元测试里跑，
就用它。

碰撞求解、射线、扫掠检测不要用它。那些属于窄阶段，你自己的或者 `CycloneGames.DeterministicMath` 的。元素只有几百个
时也不划算，线性扫描更快，而且好读得多。

## 快速开始

```csharp
using CycloneGames.Spatial.Core;

// 每 4 个世界单位一格。
var grid = new SpatialHashGrid<EntityId>(new GridSettings(cellSize: 4f), initialCapacity: 1024);

grid.TryInsert(entity, 12.5f, 0f, -3.25f, out SpatialHandle handle);

var buffer = new EntityId[64];
int count = grid.QuerySphere(12.5f, 0f, -3.25f, radius: 10f, buffer, buffer.Length);
for (int i = 0; i < count; i++)
{
    EntityId near = buffer[i];
}

grid.TryRemove(handle);
grid.Dispose();
```

查询写进你自己的缓冲。移除用的是插入时返回的句柄。

## 格子、句柄、层级

**格子。** `GridSettings` 保存格尺寸和世界原点。位置按 `floor((position - origin) / cellSize)` 映射到格子。是 floor
不是截断，所以 -0.5 属于 -1 号格。

**句柄。** `SpatialHandle` 是 16 字节值类型，由 `TryInsert` 返回，`TryUpdate`、`TryRemove`、`TryGetPosition` 都用它
定位元素。元素被移除后句柄失效；槽位被复用时它也不会复活，因为每次复用都会推进 64 位世代号。过期句柄返回 `false`。
把句柄存在保存实体的地方就行。

**层级。** 每个元素带 32 位层级，查询按位与匹配。

```csharp
const uint Enemies = 1u << 0;
const uint Allies  = 1u << 1;

grid.TryInsert(entity, x, y, z, Enemies, out SpatialHandle handle);

int count = grid.QuerySphere(x, y, z, 20f, buffer, buffer.Length, layerMask: Enemies);
```

在遍历内部过滤是必要的，因为结果是截断的。查完再过滤，预算可能已经花在你准备丢掉的元素上。元素层级不能为 0：
`TryInsert` 会把 0 转成 `SpatialLayers.Default`，`TrySetLayer` 直接拒绝。

## 查询

| 查询 | 返回 |
| --- | --- |
| `QuerySphere` | 存储位置落在球内的元素。 |
| `QueryBox` | 落在轴对齐盒内的元素，六个面都是闭区间。 |
| `QueryNearest` | 球内最近的 k 个，按距离升序。 |

三者都写进调用方缓冲，不会扩容，所以缓冲给小了结果就被截断。都不产生分配。

`QueryNearest` 还会填一个平方距离缓冲。平方距离省掉开方，排序结果和距离一致。

## 教程

### 1. 维护实体并查询周围

```csharp
public sealed class ProximitySystem : IDisposable
{
    private readonly SpatialHashGrid<int> _grid = new SpatialHashGrid<int>(new GridSettings(4f), 512);
    private readonly int[] _buffer = new int[64];

    public void Add(int entityId, float x, float y, float z, out SpatialHandle handle)
    {
        _grid.TryInsert(entityId, x, y, z, out handle);
    }

    public int CountNearby(float x, float y, float z, float radius)
    {
        return _grid.QuerySphere(x, y, z, radius, _buffer, _buffer.Length);
    }

    public void Dispose() => _grid.Dispose();
}
```

`CountNearby` 返回 `_buffer` 里有效的条目数。缓冲复用，查询本身不产生垃圾。

### 2. 找到最近的敌人

先查半径再自己排序，可能会丢掉最近的那个：查询在知道任何距离之前就截断了。`QueryNearest` 不受遍历顺序影响，
始终保留最优的候选。

```csharp
private readonly int[] _nearest = new int[4];
private readonly float[] _nearestSquared = new float[4];

public int FindClosestEnemy(float x, float y, float z, float searchRadius, out float distance)
{
    int count = _grid.QueryNearest(x, y, z, searchRadius, 4, Enemies, _nearest, _nearestSquared);
    if (count == 0)
    {
        distance = 0f;
        return -1;
    }

    distance = MathF.Sqrt(_nearestSquared[0]);
    return _nearest[0];
}
```

缓冲别开大。结果缓冲是插入排序，每个被接受的候选成本随其长度增长。几十个是设计区间，再大就改成查半径自己排序。

### 3. 移动实体

```csharp
grid.TryUpdate(handle, newX, newY, newZ);
```

同格移动只重写三个浮点数。跨格移动会把元素从一条链摘下来挂到另一条。返回 `false` 说明句柄过期了：元素被移除，
或者网格被清空、被释放。

`TryGetPosition` 能把存储位置读回来，适用于你只留了句柄的情况。

### 4. 构建一次，查询多次

可变网格为增量增删付出代价。如果某个系统本来就持有整批位置、每 tick 重建整套视图，那这份记账是白做的。
`SpatialSnapshot<TItem>` 一次接收整批数组，用有序布局回答查询。

```csharp
private readonly SpatialEntry<int>[] _entries = new SpatialEntry<int>[4096];
private readonly SpatialSnapshot<int> _snapshot = new SpatialSnapshot<int>(new GridSettings(4f), 4096);
private readonly int[] _buffer = new int[64];

public void Refresh(Vector3[] positions, int count)
{
    for (int i = 0; i < count; i++)
    {
        Vector3 p = positions[i];
        _entries[i] = new SpatialEntry<int>(p.x, p.y, p.z, SpatialLayers.Default, i);
    }

    _snapshot.Build(new ReadOnlySpan<SpatialEntry<int>>(_entries, 0, count));
}

public int QueryNearby(float x, float y, float z, float radius)
{
    return _snapshot.QuerySphere(x, y, z, radius, _buffer, _buffer.Length, SpatialLayers.All);
}
```

`Build` 放在 tick 边界上，不要放在帧中间。同规模重建会复用数组，所以每 tick 刷新最终稳定在零分配。`Build` 失败时
（位置非有限，或格子超出可寻址跨度），上一份内容保持完整，仍然可以查询。

### 5. 选格尺寸

格尺寸在内存和访问格数之间做交换。

太小，一个半径覆盖一堆空格子。太大，所有元素挤进几条长链，那就是扫描加一点额外开销。可以从典型查询半径的一半起步，
再按预期密度调到每格几个元素。两种尺度差很多的系统（角色 2 米、建筑 40 米）应该用两个网格，而不是找一个折中值。

`EnsureCapacity` 一次预留槽位和桶。加载时调一次，然后盯 `GetStatistics()` 里的 `GrowthCount` 和
`BucketRebuildCount`。稳态下两个数都不动；在涨说明预留给小了。

### 6. 大世界

位置是 `float`，和 `Transform.position` 同一个类型。几公里内够用，再远就变粗：8 km 处约 0.5 mm，1 000 km 处约
6 cm。索引不往里面加误差，它存的就是你给的那几个浮点数。变差的是你分辨两个邻近点的能力。

```csharp
// 重定基原点，所有句柄保持有效。
grid.SetOrigin(playerX, playerY, playerZ);
```

`SetOrigin` 以 O(n) 重排全部元素的格子，位置本身不动。放在传送或加载边界上做，同时把世界其余部分按同一偏移平移。
世界跨度几千公里时，用每个区域一个网格、各自局部原点。

`SpatialSnapshot` 的跨度固定为每轴 ±1 048 576 格，2 米格时约 ±2 097 km。越界时 `Build` 抛异常，不会错索引。
可变网格用 32 位格索引，实践中碰不到上限。

## 参考

### API

`SpatialHashGrid<TItem>`

| 成员 | 说明 |
| --- | --- |
| `TryInsert(item, x, y, z, out handle)` | 层级默认 `SpatialLayers.Default`。 |
| `TryInsert(item, x, y, z, layerMask, out handle)` | |
| `TryUpdate(handle, x, y, z)` | 保持层级不变。 |
| `TryRemove(handle)` | 回收槽位。 |
| `TryGetPosition(handle, out x, out y, out z)` | |
| `TryGetLayer` / `TrySetLayer` | `TrySetLayer` 拒绝 0。 |
| `QuerySphere` / `QueryBox` | 数组出口和 `List<T>` 出口。 |
| `QueryNearest` | 数组出口加平方距离。 |
| `Clear()` | 保留容量，所有句柄失效。 |
| `EnsureCapacity(n)` / `TrimExcess()` | 预留与缩容。 |
| `SetOrigin(x, y, z)` | O(n)，句柄存活。 |
| `Count` / `Capacity` / `BucketCapacity` / `Settings` | |
| `GetStatistics()` / `ValidateStructure()` | 校验器仅 Debug 有效。 |

`SpatialSnapshot<TItem>`

| 成员 | 说明 |
| --- | --- |
| `Build(ReadOnlySpan<SpatialEntry<TItem>>)` | O(n log n)；失败时保持原状。 |
| `QuerySphere` / `QueryBox` / `QueryNearest` | 语义与网格一致。 |
| `Clear()` / `Count` / `Capacity` / `BuildCount` | |
| `AxisLimitCells` | 每轴可寻址跨度。 |

### 容量

两套容量分开报告：`Capacity` 管元素，`BucketCapacity` 管可寻址格子。

`SpatialCapacityPolicy.Grow` 是默认值，插入时槽位满了就翻倍。`Fixed` 返回 `false`，用在帧内不能发生重分配的路径上。

`TrimExcess` 是唯一缩容路径。它不搬动存活元素，所以句柄不受影响；同样因为这个原因，它缩不到历史最高槽位以下。
需要更彻底的收缩先调 `Clear`。

### 内存

每个逐元素字段一条连续数组，按槽位索引：

| 数组 | 每槽字节 |
| --- | --- |
| 载荷 | `sizeof(TItem)`（引用类型为 8） |
| 链指针与格键 | 20 |
| 位置（xyz） | 12 |
| 世代、层级、状态、空闲栈 | 17 |
| 合计 | 49 + `sizeof(TItem)` |

```
槽位字节 = Capacity * (49 + sizeof(TItem))
桶字节   = BucketCapacity * 17
```

两套容量都向上取 2 的幂，预算时按"槽位最多 2 倍、桶最多约 3.5 倍元素数"估。10 万个引用类型元素分散在独立格子时
约 11.9 MB。

`SpatialSnapshot` 除了同样的逐元素数组，还多两条有序键相关的数组。它的格查找表是一张开放寻址的 `int` 表，长度取
不小于"占用格数 × 2"的 2 的幂，因此每个占用格约 8 字节，另加对齐到 2 的幂的余量。

### 复杂度

| 操作 | 均摊 | 最坏 |
| --- | --- | --- |
| `TryInsert` | O(1) | O(n)，扩容或桶重建时 |
| `TryRemove` | O(1) | O(1) |
| `TryUpdate` 同格 | O(1) | O(1) |
| `TryUpdate` 跨格 | O(1) | O(k)，k 是目标链长 |
| `QuerySphere` / `QueryBox` | O(m + k) | 格范围超预算时 O(n) |
| `QueryNearest` | O(m + k + a·r) | 同上，另加线性退化 |
| `Clear` | O(占用槽位) | 同左 |
| `SetOrigin` | O(n) | O(n) |

`m` 是覆盖格数，`k` 是其中的候选数，`a` 是被接受的候选数，`r` 是结果上限。

### 线程

单写线程，内部不加锁。所有写来自同一个线程，读可以在该线程内与写交替。Debug 构建下第二个线程写入会抛异常。
跨线程读需要自己加锁，每次调用都要，包括 `Count`。

可以在一个线程构建、交给另一个线程用，前提是交接前没有发生过写入。发生过就先调 `RebindWriteThread()`。

### 分配

稳态零分配：不装箱、不闭包、不迭代器、不用 `params`、不做字符串格式化、不用 LINQ。查询遍历用栈上游标，不是枚举器。

允许两处分配：容量增长（有计数），以及首次 `ValidateStructure`（Debug 专用的临时数组）。

### 诊断

`GetStatistics()` 给出元素数、占用格数、两套容量、峰值元素数、增长次数、桶重建次数、累计增删次数与世代回绕次数。
零分配。

`ValidateStructure()` 遍历每个桶和每条链，检查链终止、双向链接、单一归属、格键一致、空闲记账、世代非零。仅 Debug；
Release 下不检查，直接返回 `true`。

### 上限

| 上限 | 值 | 越过之后 |
| --- | --- | --- |
| 单次查询格数 | 65 536 | 退化为线性扫描，结果仍正确 |
| 快照跨度 | 每轴 ±1 048 576 格 | `Build` 抛异常 |
| 网格格索引 | 每轴 32 位 | 实践中到不了 |
| 槽位容量 | 16 777 216 | `EnsureCapacity` 抛异常 |

### 接口

`ISpatialReadOnlyIndex<TItem>` 声明三种查询。`ISpatialMutableIndex<TItem>` 追加增删改清。网格两个都实现，快照只实现
只读的那个。

方法收接口时，每次调用多一次分派。吞吐敏感的地方直接用具体类型。

## 常见错误

| 现象 | 原因 |
| --- | --- |
| 实体从查询里消失 | `Fixed` 策略下容量不足，且忽略了 `TryInsert` 的返回值 |
| 结果里有半径外的元素 | 边界容差，属设计行为；在你的窄阶段过滤 |
| `TryUpdate` 一直失败 | 句柄过期：元素已移除、网格已清空，或来自另一个实例 |
| 周期性帧尖峰 | `BucketRebuildCount` 在涨；用 `EnsureCapacity` 预留 |
| 跨线程写下悄悄损坏 | Release 把线程守卫编译掉了；每线程一个网格，或在外部加锁 |
| 最近邻漏掉最近的实体 | 用截断的半径查询自己排序，而不是 `QueryNearest` |
