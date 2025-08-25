## CycloneGames.Factory
<div align="left">English | <a href="./README.SCH.md">简体中文</a></div>

---

High-performance, low-GC factory and object-pooling utilities for Unity and pure C#. Designed to be DI-friendly and easy to adopt incrementally.

### Features
- **Factory interfaces**: `IFactory<TValue>`, `IFactory<TArg, TValue>` for creation; `IUnityObjectSpawner` for Unity `Object` instantiation.
- **Default spawner**: `DefaultUnityObjectSpawner` wraps `Object.Instantiate` (safe default for non-DI or as DI binding).
- **Prefab factory**: `MonoPrefabFactory<T>` creates disabled instances from a prefab via an injected `IUnityObjectSpawner` (optional parent).
- **Object pool**: `ObjectPool<TParam1, TValue>` is thread-safe and auto-scaling. Requires `TValue : IPoolable<TParam1, IMemoryPool>, ITickable`.
- **Low-GC hot paths**: swap-and-pop O(1) despawn; deferred despawns during `Tick()` to reduce lock contention.

### Compatibility
- Unity 2022.3+
- .NET 4.x (Unity) / modern .NET (for Pure C# samples)

### Install
This repo embeds the package under `Assets/ThirdParty`. Package name: `com.cyclone-games.factory`.
- Keep it embedded, or reference via UPM in your own projects.

### Quick start
1) Pure C# factory
```csharp
using CycloneGames.Factory.Runtime;

public class DefaultFactory<T> : IFactory<T> where T : new()
{
    public T Create() => new T();
}

var intFactory = new DefaultFactory<int>();
int number = intFactory.Create();
```

2) Unity prefab spawning (no DI)
```csharp
using UnityEngine;
using CycloneGames.Factory.Runtime;

public class MySpawner
{
    private readonly IUnityObjectSpawner spawner = new DefaultUnityObjectSpawner();

    public T Spawn<T>(T prefab) where T : Object
    {
        return spawner.Create(prefab); // Object.Instantiate under the hood
    }
}
```

3) Prefab factory + pooling
```csharp
using UnityEngine;
using CycloneGames.Factory.Runtime;

// Pooled item must implement IPoolable<TParam1, IMemoryPool> and ITickable
public sealed class Bullet : MonoBehaviour, IPoolable<BulletData, IMemoryPool>, ITickable
{
    private IMemoryPool owningPool;
    public void OnSpawned(BulletData data, IMemoryPool pool) { owningPool = pool; /* init */ }
    public void OnDespawned() { owningPool = null; /* reset */ }
    public void Tick() { /* per-frame update; call owningPool.Despawn(this) when done */ }
}

public struct BulletData { public Vector3 Position; public Vector3 Velocity; }

// Setup
var spawner = new DefaultUnityObjectSpawner();
var factory = new MonoPrefabFactory<Bullet>(spawner, bulletPrefab, parentTransform);
var pool = new ObjectPool<BulletData, Bullet>(factory, initialCapacity: 16);

// Use
var bullet = pool.Spawn(new BulletData { Position = start, Velocity = dir });
// In your game loop
pool.Tick();
```

### DI containers
- Bind `IUnityObjectSpawner` → `DefaultUnityObjectSpawner` (or your own spawner integrating Addressables/ECS).
- Bind your `IFactory<T>` or use `MonoPrefabFactory<T>` where appropriate.
- Pools can be singletons or scoped depending on lifecycle.

```csharp
builder.Register<IUnityObjectSpawner, DefaultUnityObjectSpawner>(Lifetime.Singleton);
builder.Register<IFactory<Bullet>>(c => new MonoPrefabFactory<Bullet>(
    c.Resolve<IUnityObjectSpawner>(), bulletPrefab, parent)).AsSelf();
```

### Object pool notes
- Expands when empty by `expansionFactor` (default 50% of current total).
- Shrinks after `shrinkCooldownTicks`, keeping a buffer above the recent high-water mark.
- `Tick()` reads active items, processes deferred despawns, and evaluates shrink.

### Samples
Under `Samples/`:
- `PureCSharp/` data-only systems using `ObjectPool`.
- `PureUnity/` minimal `IUnityObjectSpawner` prefab spawning.
- `Benchmarks/PureCSharp/` pure C# factory/pooling benchmarks.
- `Benchmarks/Unity/` Unity GameObject pooling vs Instantiate, memory profiling, stress tests.

### Benchmarks
- Unity and Pure C# benchmark samples live under `Samples/Benchmarks/` and save reports to `BenchmarkReports/` (`.txt`, `.md`, `.SCH.md`).
- Benchmark samples are AI-authored; other package code is authored by the maintainer.

### Performance expectations (indicative)
- **CPU**: pooling can be 2–10× faster than `new` for complex objects
- **Memory**: 50–90% reduction in GC allocations
- **Unity GameObjects**: 5–20× faster than `Instantiate()`/`Destroy()` in typical scenarios

### License
See repository license.