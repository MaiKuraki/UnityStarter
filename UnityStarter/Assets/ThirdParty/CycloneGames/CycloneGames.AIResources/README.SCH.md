# CycloneGames.AIResources

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

同一共享资源在同一时刻至多允许一个持有者。本模块记录资源的持有者与该占用的到期时间。

模块不保存资源的任何描述信息：位置、场景对象与资产引用均由调用方持有，模块只比较整数标识。

三类职责刻意排除在外：空间查询、等待队列与持久化。它们属于调用方或独立模块。

## <a id="assemblies"></a>程序集与依赖

| Assembly | 作用 | 使用方引用方式 |
| --- | --- | --- |
| `CycloneGames.AIResources.Core` | 占用表本体。 | 显式引用 |
| `CycloneGames.AIResources.Tests.Editor` | 契约测试与随机操作模型。 | 仅 Test Runner |

Core 程序集没有任何依赖，也不引用引擎，因此客户端与无头进程运行同一份表。

## <a id="model"></a>设计模型

一条占用记录由三个值构成。

| 字段 | 含义 |
| --- | --- |
| 持有者 | 调用方提供的 `int` 标识。 |
| 优先级 | `int`，仅当两个持有者冲突时参与比较。 |
| 到期时间 | 调用方时钟上的绝对时间；零表示不过期。 |

每次申请的返回值是下表中的一种。

| 情形 | 结果 |
| --- | --- |
| 资源空闲 | `Claimed` |
| 当前持有者再次申请 | `Refreshed`，到期时间被替换 |
| 其他持有者占用且不满足抢占条件 | `Rejected` |
| 其他持有者占用、优先级更低且传入 `preempt` | `Preempted` |
| 槽位已满 | `Exhausted` |

抢占需要同时满足 `preempt` 标志与严格更高的优先级。优先级相同时不发生替换，因此同级的两个持有者不会交替占用同一资源。

设置租约后，到期时间成为硬性截止：超过之后，下一次清扫会在持有者不配合的情况下收回资源；不设租约时，占用保留到显式释放。

## <a id="usage"></a>使用步骤

### 1. 构造占用表

```csharp
var resources = new AIResourceTable(capacity: 256);
```

`capacity` 约束同时存在的占用数量，与历史出现过的标识数量无关。内存在构造时分配，占用、释放与清扫不再分配。表不扩容，占满后申请返回 `Exhausted`。

### 2. 占用与释放

```csharp
if (resources.TryClaim(resourceId, ownerId, priority: 0, nowSeconds: clock.NowSeconds)
    == AIResourceClaimResult.Claimed)
{
    // 调用方持有 resourceId。
}

resources.Release(resourceId, ownerId);
```

调用方不是持有者时 `Release` 返回 false，因此重复释放不会改变表的状态。

### 3. 查询状态

```csharp
bool heldByCaller = resources.IsClaimedBy(resourceId, ownerId);
bool heldByAnyone = resources.TryGetOwner(resourceId, out int holder);
```

两者都是无副作用的查表。它们用于判断是否值得发起申请，本身不占用资源。

### 4. 附加租约

不设租约的占用会一直保留到被释放。若持有者在释放前被销毁或禁用，资源将持续被占用。租约给出该时长的上界。

```csharp
resources.TryClaim(resourceId, ownerId, priority: 0,
    nowSeconds: clock.NowSeconds, leaseSeconds: 5d);

// 每次更新调用一次，且位于持有线程。
resources.SweepExpired(clock.NowSeconds);
```

`leaseSeconds` 小于等于零表示不设截止时间。当前持有者再次申请会刷新截止时间并返回 `Refreshed`。

### 5. 允许抢占

```csharp
AIResourceClaimResult result = resources.TryClaim(
    resourceId, otherOwnerId, priority: 2,
    nowSeconds: clock.NowSeconds, leaseSeconds: 5d, preempt: true);
```

被替换的持有者不会收到通知。需要响应替换的持有者在下一次决策时重新申请或查询 `IsClaimedBy`，表因此不引入回调。

### 6. 按持有者批量释放

```csharp
int released = resources.ReleaseOwner(ownerId);
```

该调用遍历一次已占用的槽位，返回被移除的占用条数。适用于一个持有者同时占用多个资源、且整体被移除的场景。

## <a id="semantics"></a>语义约定

- 占用、释放、查询与清扫必须在同一线程执行；表内部不加锁。
- 标识按值比较，因此哈希碰撞不会让两个标识指向同一资源。
- 被拒绝的申请不改变任何状态：持有者、优先级与到期时间保持原值。
- `SweepExpired(now)` 释放所有到期时间非零且不晚于 `now` 的占用。
- `Clear` 丢弃全部占用，仅用于拆卸阶段，不能替代释放。

## <a id="cost"></a>复杂度与内存

| 操作 | 复杂度 |
| --- | --- |
| `TryClaim`、`Release`、`IsClaimedBy`、`TryGetOwner` | 期望常数时间。 |
| `ReleaseOwner`、`SweepExpired` | 与已占用槽位数线性相关。 |
| 频繁增删后的桶重建 | 线性；被移除条目超过桶数组一半时触发一次。 |

内存在构造时固定：一个容量两倍的桶数组、一个 `capacity` 长度的槽位数组，以及一个用于批量删除的临时数组。

## <a id="api"></a>API

```csharp
AIResourceTable(int capacity = 64);

AIResourceClaimResult TryClaim(int resourceId, int ownerId, int priority, double nowSeconds,
                               double leaseSeconds = 0d, bool preempt = false);
bool Release(int resourceId, int ownerId);
int ReleaseOwner(int ownerId);

bool IsClaimedBy(int resourceId, int ownerId);
bool TryGetOwner(int resourceId, out int ownerId);

int SweepExpired(double nowSeconds);
void Clear();

int ClaimedCount { get; }
int Capacity { get; }
```

## <a id="limits"></a>已知边界

- 不做空间查询。候选资源的定位由 `CycloneGames.Spatial` 或调用方自己的索引完成。
- 不做等待队列。被拒绝的调用方按自身节奏重试，表不保存队列，也不为每个等待者维护定时器。
- 不做持久化。占用属于运行时状态，不写入磁盘。
- 不提供持有权回调。替换只能通过轮询或重新申请观察。
