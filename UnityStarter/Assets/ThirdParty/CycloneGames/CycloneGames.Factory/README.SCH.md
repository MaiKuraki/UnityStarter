## CycloneGames.Factory
<div align="left"><a href="./README.md">English</a> | 简体中文</div>

---

面向 Unity 与纯 C# 的高性能、低 GC 工厂与对象池工具集。模块化、可插拔，易于与 DI 框架集成。

### 模块包含
- **工厂接口**：`IFactory<TValue>`、`IFactory<TArg, TValue>` 用于对象创建；`IUnityObjectSpawner` 用于 Unity `Object` 实例化。
- **默认 Spawner**：`DefaultUnityObjectSpawner` 基于 `Object.Instantiate`，可在非 DI 或作为 DI 默认实现直接使用。
- **Prefab 工厂**：`MonoPrefabFactory<T>` 通过注入的 `IUnityObjectSpawner` 从 Prefab 创建（可选设置父节点），创建后的实例默认 `SetActive(false)`。
- **对象池**：`ObjectPool<TParam1, TValue>` 线程安全、自动扩缩容。要求 `TValue : IPoolable<TParam1, IMemoryPool>, ITickable`。

### 设计目标
- **极低 GC**：优先使用对象池，热路径无隐藏分配。
- **性能与稳定**：O(1) 回收（swap-and-pop）、读写锁；`Tick()` 期间的回收使用延迟队列，避免锁冲突。
- **强拓展性**：接口简洁，方便接入 VContainer、Zenject 等 DI 框架。

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

3）Prefab 工厂 + 对象池
```csharp
using UnityEngine;
using CycloneGames.Factory.Runtime;

// 被池化的类型需实现 IPoolable<TParam1, IMemoryPool> 与 ITickable
public sealed class Bullet : MonoBehaviour, IPoolable<BulletData, IMemoryPool>, ITickable
{
    private IMemoryPool owningPool;
    public void OnSpawned(BulletData data, IMemoryPool pool) { owningPool = pool; /* 初始化 */ }
    public void OnDespawned() { owningPool = null; /* 重置 */ }
    public void Tick() { /* 每帧更新，完成后可调用 owningPool.Despawn(this) */ }
}

public struct BulletData { public Vector3 Position; public Vector3 Velocity; }

// 组装
var spawner = new DefaultUnityObjectSpawner();
var factory = new MonoPrefabFactory<Bullet>(spawner, bulletPrefab, parentTransform);
var pool = new ObjectPool<BulletData, Bullet>(factory, initialCapacity: 16);

// 使用
var bullet = pool.Spawn(new BulletData { Position = start, Velocity = dir });
// 在你的游戏循环中
pool.Tick(); // Tick 活跃对象并处理自动收缩
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
- `Tick()` 做三件事：读锁遍历 `Tick()`、写锁处理延迟回收、写锁进行收缩判断。

### 示例说明
位于 `Samples/`：
- `PureCSharp/` 演示基于 `ObjectPool` 的纯数据粒子系统模拟。
- `PureUnity/` 演示通过 `IUnityObjectSpawner` 生成 `MonoBehaviour` Prefab 的最小示例。
- `Benchmarks/PureCSharp/` 提供纯 C# 工厂模式和对象池的综合性能基准测试。
- `Benchmarks/Unity/` 提供 Unity 特定的 GameObject 池化、Prefab 实例化和内存分析基准测试。

### 性能与基准
- Unity 与纯 C# 的基准样例位于 `Samples/Benchmarks/`，会将报告保存到 `BenchmarkReports/`（`.txt`、`.md`、`.SCH.md`）。
- 提示：Benchmark 示例由 AI 编写，其他代码由作者本人编写。