## CycloneGames.Factory

<div align="left"><a href="./README.md">English</a> | 简体中文</div>

---

面向 Unity 与纯 C# 的高性能、低 GC 工厂与对象池工具集。模块化、可插拔，易于与 DI 框架集成。

### 模块包含

- **工厂接口**：`IFactory<TValue>`、`IFactory<TArg, TValue>` 用于对象创建；`IUnityObjectSpawner` 用于 Unity `Object` 实例化。
- **默认 Spawner**：`DefaultUnityObjectSpawner` 基于 `Object.Instantiate`，可在非 DI 或作为 DI 默认实现直接使用。
- **Prefab 工厂**：`MonoPrefabFactory<T>` 通过注入的 `IUnityObjectSpawner` 从 Prefab 创建（可选设置父节点），创建后的实例默认 `SetActive(false)`。
- **ObjectPool**：`ObjectPool<TParam1, TValue>` — 线程安全、自动扩缩容，使用 `ReaderWriterLockSlim`、活跃对象跟踪、延迟回收队列。要求 `TValue : IPoolable<TParam1, IMemoryPool>`。
- **FastObjectPool**：`FastObjectPool<T>` — 抽象轻量主线程池。Stack 驱动，无锁，无活跃对象跟踪开销。支持峰值衰减智能收缩。
- **MonoFastPool**：`MonoFastPool<T>` — 面向 Unity `Component` 的 `FastObjectPool` 具体实现；自动 `SetActive`、父节点管理、`Object.Destroy` 清理。
- **NativePool**：`NativePool<T>`（DOD）— Burst/Jobs 兼容的 `struct` 池，仅支持 `unmanaged` 类型。`NativeArray` 支撑，零 GC，O(1) swap-and-pop 回收，批量 Spawn/Despawn。需要 `com.unity.collections`。
- **EntityPool**：`EntityPool<TData>`（ECS）— ECS `Entity` 池，支持同步和 `EntityCommandBuffer` 路径的 Spawn/Despawn。需要 `com.unity.entities`。

### 设计目标

- **极低 GC**：优先使用对象池，热路径无隐藏分配。
- **性能与稳定**：O(1) 回收（swap-and-pop）、读写锁；`Maintenance()` 期间的回收使用延迟队列，避免锁冲突。
- **强拓展性**：接口简洁，方便接入 VContainer、Zenject 等 DI 框架。

### 池类型对比

|                | `ObjectPool`              | `MonoFastPool` | `NativePool`       | `EntityPool`       |
| -------------- | ------------------------- | -------------- | ------------------ | ------------------ |
| 线程安全       | 是（读写锁）              | 否（主线程）   | 否（单线程 / Job） | 否（主线程 / ECB） |
| 活跃对象跟踪   | 是（`UpdateActiveItems`） | 否             | 是（基于索引）     | 是（HashSet）      |
| 自动扩缩       | 扩容 + 收缩               | 扩容 + 收缩    | 手动 `Expand`      | 手动               |
| 热路径 GC 分配 | 接近零                    | 零             | 零                 | 零（ECB 路径）     |
| 适用场景       | 复杂系统、多线程访问      | 简单 GO 生成   | DOD / Burst / Jobs | ECS 实体           |

### 安装

本仓库以内嵌包形式位于 `Assets/ThirdParty`。包名：`com.cyclone-games.factory`（Unity 2022.3+）。可直接使用或迁移到你自己的 UPM。

### 兼容性

- Unity 2022.3+
- .NET 4.x（Unity）/ 现代 .NET（纯 C# 示例）
- **可选依赖**：`com.unity.collections` + `com.unity.burst`（`NativePool`）；`com.unity.entities` + `com.unity.mathematics` + `com.unity.transforms`（`EntityPool`）

### 快速上手

1）纯 C# 工厂

```csharp
using CycloneGames.Factory.Runtime;

public class DefaultFactory<T> : IFactory<T> where T : new()
{
    public T Create() => new T();
}

var intFactory = new DefaultFactory<int>();
int number = intFactory.Create();
```

2）Unity Prefab 生成（无 DI）

```csharp
using UnityEngine;
using CycloneGames.Factory.Runtime;

public class MySpawner
{
    private readonly IUnityObjectSpawner spawner = new DefaultUnityObjectSpawner();

    public T Spawn<T>(T prefab) where T : Object
    {
        return spawner.Create(prefab); // 内部使用 Object.Instantiate
    }
}
```

3）Prefab 工厂 + 对象池

```csharp
using UnityEngine;
using CycloneGames.Factory.Runtime;

// 被池化的类型需实现 IPoolable<TParam1, IMemoryPool>
// 注意：IPoolable 继承 IDisposable — 需实现 Dispose()。
public sealed class Bullet : MonoBehaviour, IPoolable<BulletData, IMemoryPool>
{
    private IMemoryPool owningPool;
    public void OnSpawned(BulletData data, IMemoryPool pool) { owningPool = pool; /* 初始化 */ }
    public void OnDespawned() { owningPool = null; /* 重置 */ }
    public void Dispose() { } // IDisposable 要求

    public void GameUpdate() { /* 每帧更新 */ }
}

public struct BulletData { public Vector3 Position; public Vector3 Velocity; }

// 组装
var spawner = new DefaultUnityObjectSpawner();
var factory = new MonoPrefabFactory<Bullet>(spawner, bulletPrefab, parentTransform);
var pool = new ObjectPool<BulletData, Bullet>(factory, initialCapacity: 16);

// 使用
var bullet = pool.Spawn(new BulletData { Position = start, Velocity = dir });

// 在你的游戏循环中
pool.UpdateActiveItems(b => b.GameUpdate());
pool.Maintenance(); // 必须：处理延迟回收和自动扩缩容
```

4）MonoFastPool（轻量 Unity 池）

```csharp
using CycloneGames.Factory.Runtime;

// 无需 IPoolable — 只需 Component + Prefab
var pool = new MonoFastPool<MyComponent>(prefab, parent, initialCapacity: 32, autoSetActive: true);
var item = pool.Spawn();      // 激活并从池中取出
pool.Despawn(item);            // 停用并归还池中

// 用完后清理
pool.Dispose();
```

5）NativePool（DOD / Burst / Jobs）

```csharp
using CycloneGames.Factory.Runtime;
using Unity.Collections;

var pool = new NativePool<BulletData>(capacity: 1024, Allocator.Persistent);

int index = pool.Spawn(new BulletData { Position = float3.zero, Speed = 10f });

// 获取活跃元素供 IJobParallelFor 使用
NativeArray<BulletData> active = pool.ActiveItems;

// 批量回收（掩码方式）
var mask = new NativeArray<bool>(pool.ActiveCount, Allocator.Temp);
mask[index] = true;
pool.DespawnBatch(mask);
mask.Dispose();

pool.Dispose(); // 必须：释放 NativeArray
```

### IPoolable 接口族

```
IPoolable                       → OnSpawned(), OnDespawned(), Dispose()
IPoolable<TParam1>              → OnSpawned(TParam1), OnDespawned(), Dispose()
IPoolable<TParam1, TParam2>     → OnSpawned(TParam1, TParam2), OnDespawned(), Dispose()
ITickable                       → Tick()
```

所有 `IPoolable` 变体均继承 `IDisposable`。`ObjectPool` 会自动调用 `OnSpawned`/`OnDespawned` 回调；`MonoFastPool` **不会**调用 `IPoolable` 回调（仅管理 `SetActive`）。

### 池生命周期

- **`MaxCapacity`** / **`MinCapacity`**：设置池大小上下限（默认：无限 / 16）。
- **`WarmupCoroutine(count, batchSize)`**：跨帧预创建对象（避免加载时卡顿）。`ObjectPool` 和 `FastObjectPool` 均支持。
- **`DespawnAllActive()`**：一次性归还所有活跃对象。
- **`Dispose()`**：释放所有池化对象。在 `OnDestroy` 或生命周期结束时调用。所有池类型均实现 `IDisposable`。
- **`Clear()`**：移除所有非活跃对象，不销毁池本身。

```csharp
// 加载期间预热
pool.MaxCapacity = 500;
StartCoroutine(pool.WarmupCoroutine(200, batchSize: 16));

// 清理
pool.Dispose();
```

### 与 DI 集成

- 绑定 `IUnityObjectSpawner` → `DefaultUnityObjectSpawner`（或你自己的实现，支持 Addressables/ECS）。
- 绑定你的 `IFactory<T>` 或直接使用 `MonoPrefabFactory<T>`。
- 池根据生命周期选择注册为单例或作用域服务。

示例（伪代码）：

```csharp
builder.Register<IUnityObjectSpawner, DefaultUnityObjectSpawner>(Lifetime.Singleton);
builder.Register<IFactory<Bullet>>(c => new MonoPrefabFactory<Bullet>(
    c.Resolve<IUnityObjectSpawner>(), bulletPrefab, parent)).AsSelf();
```

### 自动扩缩容

- 当池为空时按 `expansionFactor` 扩容（默认当前总量的 50%）。
- 经过 `shrinkCooldownTicks` 后，按最近高出发容量 + 缓冲收缩。
- `Maintenance()` 负责处理延迟回收和收缩判断（需周期性调用）。
- `UpdateActiveItems(action)` 提供线程安全的活跃对象遍历能力。

### 程序集与依赖

| 程序集                             | 可选依赖                                                                                       | 条件编译符号                                                  |
| ---------------------------------- | ---------------------------------------------------------------------------------------------- | ------------------------------------------------------------- |
| `CycloneGames.Factory.Runtime`     | —                                                                                              | —                                                             |
| `CycloneGames.Factory.DOD.Runtime` | `com.unity.collections`, `com.unity.burst`                                                     | `PRESENT_COLLECTIONS`, `PRESENT_BURST`                        |
| `CycloneGames.Factory.ECS.Runtime` | `com.unity.entities`, `com.unity.collections`, `com.unity.mathematics`, `com.unity.transforms` | `PRESENT_BURST`, `PRESENT_ECS`                                |
| `CycloneGames.Factory.Samples`     | UniTask, Burst, Collections, Mathematics                                                       | `PRESENT_MATHEMATICS`, `PRESENT_COLLECTIONS`, `PRESENT_BURST` |
| `CycloneGames.Factory.ECS.Samples` | Entities, Burst, Collections, Mathematics, Transforms                                          | `PRESENT_BURST`, `PRESENT_ECS`                                |

### 示例说明

位于 `Samples/`：

- `PureCSharp/` 演示基于 `ObjectPool` 的纯数据粒子系统模拟。
- `PureUnity/` 演示 `IUnityObjectSpawner` Prefab 生成 + `AdvancedObjectPoolSample`（WarmupCoroutine、MaxCapacity、Dispose）。
- `OOPBullet/` 完整的 `MonoFastPool<Bullet>` 演示，含 `BulletSpawner`、`ITickable`、Rigidbody 子弹。
- `DODBullet/` 三种 DOD 方案：原始 NativeArray、Jobs、`NativePool<T>` — 含 GPU Instancing。
- `Benchmarks/PureCSharp/` 提供纯 C# 工厂模式和对象池的综合性能基准测试。
- `Benchmarks/Unity/` 提供 Unity 特定的 GameObject 池化、Prefab 实例化和内存分析基准测试。

位于 `ECS/Samples/`：

- `BulletSpawnerAuthoring`、`BulletAuthoring`、`BulletPoolManagerSystem` — ECS 实体池化演示。

### 性能与基准

- 基准样例位于 `Samples/Benchmarks/`，会将报告保存到 `BenchmarkReports/`（`.txt`、`.md`、`.SCH.md`）。
- 提示：Benchmark 示例由 AI 编写，其他代码由作者本人编写。
- 报告包含：性能表格、内存分析、柱状图、以及基于成对对比数据的自动生成建议。

### 性能预期（参考值）

- **CPU**：对象池通常比直接 `Instantiate`/`Destroy` 快 2–10×。
- **内存**：GC 分配减少 50–90%；预热池可实现接近零的运行时分配。
- **NativePool**：设计上零 GC（unmanaged `NativeArray` 支撑）。
- **Unity GameObjects**：典型成对对比中比 `Instantiate()`/`Destroy()` 快 5–20×。

### License

See repository license.
