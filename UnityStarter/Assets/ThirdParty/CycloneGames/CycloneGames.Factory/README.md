## CycloneGames.Factory
<div align="left">English | <a href="./README.SCH.md">简体中文</a></div>

---

High-performance, low-GC factory and object-pooling utilities for Unity and pure C#. Designed to be DI-friendly and easy to adopt incrementally.

### What is inside
- **Factory interfaces**: `IFactory<TValue>`, `IFactory<TArg, TValue>` for object creation; `IUnityObjectSpawner` for Unity `Object` instantiation.
- **Default spawner**: `DefaultUnityObjectSpawner` uses `Object.Instantiate` and is safe for non-DI usage or as a DI default.
- **Prefab factory**: `MonoPrefabFactory<T>` creates disabled instances from a prefab via an injected `IUnityObjectSpawner` (optionally sets parent).
- **Object pool**: `ObjectPool<TParam1, TValue>` is a thread-safe, auto-scaling pool. Requires `TValue : IPoolable<TParam1, IMemoryPool>, ITickable`.

### Goals
- **Minimal GC**: Pooling-first design, no hidden allocations in hot paths.
- **Performance & safety**: O(1) despawn via swap-and-pop, reader/writer locks, deferred despawns during Tick to avoid lock contention.
- **Extensibility**: Small interfaces, easy to integrate with DI containers (VContainer, Zenject, etc.).

### Install
This repo embeds the package under `Assets/ThirdParty`. The package name is `com.cyclone-games.factory` (Unity 2022.3+). You can keep it embedded or reference it via UPM in your own projects.

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

// Pooled item must implement both IPoolable<TParam1, IMemoryPool> and ITickable
public sealed class Bullet : MonoBehaviour, IPoolable<BulletData, IMemoryPool>, ITickable
{
    private IMemoryPool owningPool;
    public void OnSpawned(BulletData data, IMemoryPool pool) { owningPool = pool; /* init state */ }
    public void OnDespawned() { owningPool = null; /* reset state */ }
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
pool.Tick(); // Ticks active bullets and handles auto-shrink
```

### With a DI container
- Bind `IUnityObjectSpawner` to `DefaultUnityObjectSpawner` (or your own implementation that integrates Addressables or ECS).
- Bind your `IFactory<T>` or use `MonoPrefabFactory<T>` where appropriate.
- Pools can be registered as singletons or scoped services depending on lifecycle needs.

Example (pseudo):
```csharp
builder.Register<IUnityObjectSpawner, DefaultUnityObjectSpawner>(Lifetime.Singleton);
builder.Register<IFactory<Bullet>>(c => new MonoPrefabFactory<Bullet>(
    c.Resolve<IUnityObjectSpawner>(), bulletPrefab, parent)).AsSelf();
```

### Auto-scaling object pool notes
- Expands when empty by `expansionFactor` (default 50% of current total).
- Shrinks after `shrinkCooldownTicks`, keeping a buffer above the recent high-water mark.
- `Tick()` performs: read-locked ticking of active items, write-locked shrink check, and processes deferred despawns safely.

### Samples
Under `Samples/`:
- `PureCSharp/` shows a data-only particle system using `ObjectPool`.
- `PureUnity/` shows spawning `MonoBehaviour` prefabs via `IUnityObjectSpawner` and a minimal manager.

### Tips
- For zero-GC and high-performance logging, pair with `CycloneGames.Logger` (also in this repo) which uses pooled messages.
- Gameplay modules in this repo already depend on `CycloneGames.Factory.Runtime`, demonstrating integration patterns.