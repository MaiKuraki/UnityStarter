## CycloneGames.Factory

<div align="left"><a href="./README.md">English</a> | 简体中文</div>

---

面向 Unity 与纯 C# 的高性能、低 GC 工厂与对象池工具集。模块化、可插拔，易于与 DI 框架集成。

### 模块包含

- **工厂接口**：`IFactory<TValue>`、`IFactory<TArg, TValue>` 用于对象创建；`IUnityObjectSpawner` 用于 Unity `Object` 实例化。
- **默认 Spawner**：`DefaultUnityObjectSpawner` 基于 `Object.Instantiate`，可在非 DI 或作为 DI 默认实现直接使用。
- **Prefab 工厂**：`MonoPrefabFactory<T>` 通过注入的 `IUnityObjectSpawner` 从 Prefab 创建（可选设置父节点），创建后的实例默认 `SetActive(false)`。
- **PoolBase**：`PoolBase<TValue>` — 所有托管池的抽象基类。提供活跃对象跟踪（O(1) swap-remove）、`SoftCapacity`/`HardCapacity`/`OverflowPolicy`/`TrimPolicy`、诊断信息、批量操作（`DespawnStep`、`WarmupStep`）和协程辅助方法（`DespawnAllCoroutine`、`WarmupCoroutine`）。
- **ObjectPool**：`ObjectPool<TParam1, TValue>` — sealed 密封类，带参数化 Spawn 的自动扩缩容池。需要 `IFactory<TValue>` 和 `TValue : IPoolable<TParam1, TValue>`。本身非线程安全。
- **ConcurrentMemoryPool**：`ConcurrentMemoryPool<TValue>` — 基于 `lock` 的线程安全包装器，可包装任意 `IMemoryPool<TValue>`。
- **FastObjectPool**：`FastObjectPool<T>` — 抽象轻量主线程池。无参数 `Spawn()`/`TrySpawn()`，继承 `PoolBase` 全部基础设施。
- **MonoFastPool**：`MonoFastPool<T>` — 面向 Unity `Component` 的 `FastObjectPool` 具体实现；自动 `SetActive`、父节点管理、`Object.Destroy` 清理。
- **NativePool**：`NativePool<T>`（DOD）— 简单索引式 `struct` 池，仅支持 `unmanaged` 类型。紧凑活跃数组，O(1) swap-and-pop 回收，批量 Spawn/Despawn。无句柄安全性。需要 `com.unity.collections`。
- **NativeDensePool**：`NativeDensePool<T>`（DOD）— 基于句柄的 `struct` 池，使用 `NativePoolHandle`（slot + generation）。稳定引用，O(1) 操作，完整诊断信息。需要 `com.unity.collections`。
- **NativeDenseColumnPool**：`NativeDenseColumnPool2<T0,T1>`、`ColumnPool3`、`ColumnPool4` — `NativeDensePool` 的 SoA（结构体数组）变体，提供并行的类型化数据流。
- **EntityPool**：`EntityPool<TData>`（ECS）— ECS `Entity` 池，支持同步和 `EntityCommandBuffer` 路径的 Spawn/Despawn。需要 `com.unity.entities`。
- **极低 GC 热路径**：swap-and-pop O(1) 回收；所有 DOD 池设计上零 GC（`NativeArray` 支撑）。

### 池类型对比

|                 | `ObjectPool`      | `FastObjectPool` / `MonoFastPool` | `ConcurrentMemoryPool` | `NativePool`       | `NativeDensePool` / `ColumnPoolN` | `EntityPool`            |
| --------------- | ----------------- | --------------------------------- | ---------------------- | ------------------ | --------------------------------- | ----------------------- |
| 线程安全        | 否                | 否（主线程）                      | 是（`lock`）           | 否（单线程 / Job） | 否（单线程 / Job）                | 否（主线程 / ECB）      |
| 活跃对象跟踪    | 是（Dict + List） | 是（继承自 PoolBase）             | 委托给内部池           | 否（仅索引）       | 是（基于句柄）                    | 是（Dict + List）       |
| 稳定引用        | 是（对象引用）    | 是（对象引用）                    | 委托                   | 否（索引会移动）   | 是（`NativePoolHandle`）          | 是（Entity）            |
| 自动扩缩        | 扩容 + 收缩       | 扩容 + 收缩                       | 委托                   | 手动 `Resize`      | 手动 `Resize`                     | 通过工厂扩容            |
| 诊断信息        | `PoolDiagnostics` | `PoolDiagnostics`                 | 委托                   | 无                 | `NativeDenseDiagnostics`          | `EntityPoolDiagnostics` |
| 热路径 GC 分配  | 接近零            | 接近零                            | 接近零                 | 零                 | 零                                | 零（ECB 路径）          |
| Burst/Jobs 安全 | 否                | 否                                | 否                     | `ActiveItems` 安全 | `ActiveItems` / `StreamN` 安全    | 否                      |
| 适用场景        | 参数化 Spawn      | 简单主线程池化                    | 多线程访问             | DOD / Burst / Jobs | DOD 稳定句柄 / SoA                | ECS 实体                |

### 兼容性

- Unity 2022.3+
- .NET 4.x（Unity）/ 现代 .NET（纯 C# 示例）
- **可选依赖**：`com.unity.collections` + `com.unity.burst`（`NativePool`、`NativeDensePool`）；`com.unity.entities` + `com.unity.mathematics` + `com.unity.transforms`（`EntityPool`）

### 安装

本仓库以内嵌包形式位于 `Assets/ThirdParty`。包名：`com.cyclone-games.factory`（Unity 2022.3+）。可直接使用或迁移到你自己的 UPM。

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

3）Prefab 工厂 + ObjectPool

```csharp
using UnityEngine;
using CycloneGames.Factory.Runtime;

// 被池化的类型需实现 IPoolable<TParam1, TValue>
// 第二个类型参数是自身类型（用于通过池引用自行回收）。
// 注意：IPoolable 继承 IDisposable — 需实现 Dispose()。
public sealed class Bullet : MonoBehaviour, IPoolable<BulletData, Bullet>
{
    private IDespawnableMemoryPool<Bullet> owningPool;
    public void OnSpawned(BulletData data, IDespawnableMemoryPool<Bullet> pool)
    {
        owningPool = pool;
        // 从 data 初始化...
    }
    public void OnDespawned() { owningPool = null; /* 重置状态 */ }
    public void Dispose() { } // IDisposable 要求

    public void ReturnToPool() => owningPool?.Despawn(this);
}

public struct BulletData { public Vector3 Position; public Vector3 Velocity; }

// 组装
var spawner = new DefaultUnityObjectSpawner();
var factory = new MonoPrefabFactory<Bullet>(spawner, bulletPrefab, parentTransform);
var pool = new ObjectPool<BulletData, Bullet>(factory,
    new PoolCapacitySettings(softCapacity: 16, hardCapacity: 256));

// 使用
var bullet = pool.Spawn(new BulletData { Position = start, Velocity = dir });

// 遍历活跃对象
pool.ForEachActive(b => b.GameUpdate());

// 批量回收（例如每帧处理 8 个）
pool.DespawnStep(8);
```

4）MonoFastPool（轻量 Unity 池）

```csharp
using CycloneGames.Factory.Runtime;

// 无需 IPoolable — 只需 Component + Prefab
var pool = new MonoFastPool<MyComponent>(prefab,
    initialCapacity: 32, root: parent, autoSetActive: true);
var item = pool.Spawn();      // 激活并从池中取出
pool.Despawn(item);            // 停用并归还池中

// 用完后清理
pool.Dispose();
```

5）NativePool（DOD — 简单索引式）

```csharp
using CycloneGames.Factory.DOD.Runtime;
using Unity.Collections;

var pool = new NativePool<BulletData>(capacity: 1024, Allocator.Persistent);

int index = pool.Spawn(new BulletData { Position = float3.zero, Speed = 10f });

// 活跃元素占据 [0..ActiveCount)，可安全用于 IJobParallelFor
NativeArray<BulletData> active = pool.ActiveItems;

// 批量回收（掩码方式）
var mask = new NativeArray<bool>(pool.ActiveCount, Allocator.Temp);
mask[index] = true;
pool.DespawnBatch(mask);
mask.Dispose();

pool.Dispose(); // 必须：释放 NativeArray
```

6）NativeDensePool（DOD — 基于句柄的稳定引用）

```csharp
using CycloneGames.Factory.DOD.Runtime;
using Unity.Collections;

var pool = new NativeDensePool<EnemyData>(capacity: 512, Allocator.Persistent);

// Spawn 返回稳定句柄（slot + generation）
pool.TrySpawn(new EnemyData { Health = 100 }, out NativePoolHandle handle, out int denseIndex);

// 通过句柄读写 — 即使其他元素被回收也安全
pool.TryRead(handle, out EnemyData data);
pool.TryWrite(handle, new EnemyData { Health = data.Health - 10 });

// 句柄验证
bool alive = pool.Contains(handle);

// 通过句柄回收
pool.Despawn(handle);

pool.Dispose();
```

7）NativeDenseColumnPool（DOD — SoA 多流）

```csharp
using CycloneGames.Factory.DOD.Runtime;
using Unity.Collections;
using Unity.Mathematics;

// 两个并行流：位置 + 速度
var pool = new NativeDenseColumnPool2<float3, float3>(capacity: 1024, Allocator.Persistent);

pool.TrySpawn(float3.zero, new float3(1, 0, 0), out NativePoolHandle handle, out int idx);

// 获取密集数组供 Burst Jobs 使用
NativeArray<float3> positions  = pool.Stream0; // [0..CountActive)
NativeArray<float3> velocities = pool.Stream1;

pool.Dispose();
```

### IPoolable 接口族

```
IPoolable                          → OnSpawned(), OnDespawned(), Dispose()
IPoolable<in TParam1>              → OnSpawned(TParam1), OnDespawned(), Dispose()
IPoolable<in TParam1, TValue>      → OnSpawned(TParam1, IDespawnableMemoryPool<TValue>), OnDespawned(), Dispose()
ITickable                          → Tick()
```

所有 `IPoolable` 变体均继承 `IDisposable`。双参数变体的第二个参数接收池引用，使对象可以自行回收。

`ObjectPool` 会自动调用 `OnSpawned`/`OnDespawned` 回调；`MonoFastPool` **不会**调用 `IPoolable` 回调（仅管理 `SetActive`）。

### 池生命周期

- **`SoftCapacity`** / **`HardCapacity`**：软容量是目标池大小（用于收缩判断）；硬容量是绝对上限（0 = 无限）。通过 `PoolCapacitySettings` 配置。
- **`OverflowPolicy`**：`Throw`（默认）或 `ReturnNull`，当达到硬容量时触发。
- **`TrimPolicy`**：`Manual`（默认）或 `TrimOnDespawn`（回收时自动销毁多余对象，当非活跃数量超过软容量时）。
- **`Prewarm(count)`**：同步预创建对象。
- **`WarmupCoroutine(count, batchSize)`**：跨帧预创建对象（避免加载时卡顿）。
- **`WarmupStep(maxItems)`**：每次调用最多创建 N 个对象（用于手动帧分散预热）。
- **`DespawnAll()`**：一次性归还所有活跃对象。
- **`DespawnStep(maxItems)`**：每次调用最多回收 N 个活跃对象（用于渐进式批量回收）。
- **`DespawnAllCoroutine(batchSize)`**：批量回收的协程版本。
- **`TrimInactive(targetCount)`**：将多余的非活跃对象销毁至目标数量。
- **`ForEachActive(action)`**：遍历所有活跃对象。
- **`Clear()`**：移除所有非活跃对象，不销毁池本身。
- **`Dispose()`**：释放所有池化对象。在 `OnDestroy` 或生命周期结束时调用。所有池类型均实现 `IDisposable`。

```csharp
// 配置容量
var settings = new PoolCapacitySettings(
    softCapacity: 64,
    hardCapacity: 500,
    overflowPolicy: PoolOverflowPolicy.ReturnNull,
    trimPolicy: PoolTrimPolicy.TrimOnDespawn);
var pool = new ObjectPool<BulletData, Bullet>(factory, settings);

// 加载期间预热
StartCoroutine(pool.WarmupCoroutine(200, batchSize: 16));

// 清理
pool.Dispose();
```

### 与 DI 集成

- 绑定 `IUnityObjectSpawner` → `DefaultUnityObjectSpawner`（或你自己的实现，支持 Addressables/ECS）。
- 绑定你的 `IFactory<T>` 或直接使用 `MonoPrefabFactory<T>`。
- 需要线程安全时使用 `ConcurrentMemoryPool<T>` 包装。
- 池根据生命周期选择注册为单例或作用域服务。

```csharp
builder.Register<IUnityObjectSpawner, DefaultUnityObjectSpawner>(Lifetime.Singleton);
builder.Register<IFactory<Bullet>>(c => new MonoPrefabFactory<Bullet>(
    c.Resolve<IUnityObjectSpawner>(), bulletPrefab, parent)).AsSelf();
```

### 程序集与依赖

| 程序集                              | 可选依赖                                                                                       | 条件编译符号                                                  |
| ----------------------------------- | ---------------------------------------------------------------------------------------------- | ------------------------------------------------------------- |
| `CycloneGames.Factory.Runtime`      | —                                                                                              | —                                                             |
| `CycloneGames.Factory.DOD.Runtime`  | `com.unity.collections`, `com.unity.burst`                                                     | `PRESENT_COLLECTIONS`, `PRESENT_BURST`                        |
| `CycloneGames.Factory.ECS.Runtime`  | `com.unity.entities`, `com.unity.collections`, `com.unity.mathematics`, `com.unity.transforms` | `PRESENT_BURST`, `PRESENT_ECS`                                |
| `CycloneGames.Factory.Samples`      | UniTask, Burst, Collections, Mathematics                                                       | `PRESENT_MATHEMATICS`, `PRESENT_COLLECTIONS`, `PRESENT_BURST` |
| `CycloneGames.Factory.ECS.Samples`  | Entities, Burst, Collections, Mathematics, Transforms                                          | `PRESENT_BURST`, `PRESENT_ECS`                                |
| `CycloneGames.Factory.Tests.Editor` | Collections, Burst, Entities（均为可选）                                                       | `PRESENT_COLLECTIONS`, `PRESENT_BURST`, `PRESENT_ECS`         |

所有可选依赖通过 `.asmdef` 文件中的 `versionDefines` 管理。代码使用 `#if` 指令保护，因此无论安装了哪些包，程序集均可正常编译。

### 示例说明

位于 `Samples/`：

- `PureCSharp/` — 使用 `ObjectPool` 的纯数据粒子系统模拟。
- `PureUnity/` — `IUnityObjectSpawner` Prefab 生成 + `AdvancedObjectPoolSample`（`WarmupCoroutine`、`PoolCapacitySettings`、`Profile` 显示、`Dispose`）。
- `OOPBullet/` — `MonoFastPool<Bullet>` 完整演示，含 `BulletSpawner`、`ITickable`、Rigidbody 子弹。
- `DODBullet/` — 三种 DOD 方案：原始 NativeArray、Jobs、`NativePool<T>` — 含 GPU Instancing。
- `Benchmarks/PureCSharp/` — 纯 C# 工厂模式和对象池的综合性能基准测试。
- `Benchmarks/Unity/` — Unity GameObject 池化、Prefab 实例化和内存分析基准测试。

位于 `ECS/Samples/`：

- `BulletSpawnerAuthoring`、`BulletAuthoring`、`ECSHighLoadBenchmark` — ECS 实体池化演示。

### 性能预期（参考值）

- **CPU**：对象池通常比直接 `Instantiate`/`Destroy` 快 2–10×。
- **内存**：GC 分配减少 50–90%；预热池可实现接近零的运行时分配。
- **NativePool / NativeDensePool**：设计上零 GC（unmanaged `NativeArray` 支撑）。
- **Unity GameObjects**：典型成对对比中比 `Instantiate()`/`Destroy()` 快 5–20×。

### License

See repository license.
