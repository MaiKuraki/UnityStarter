## CycloneGames.Factory

<div align="left">English | <a href="./README.SCH.md">简体中文</a></div>

---

High-performance, low-GC factory and object-pooling utilities for Unity and pure C#. Designed to be DI-friendly and easy to adopt incrementally.

### Features

- **Factory interfaces**: `IFactory<TValue>`, `IFactory<TArg, TValue>` for creation; `IUnityObjectSpawner` for Unity `Object` instantiation.
- **Default spawner**: `DefaultUnityObjectSpawner` wraps `Object.Instantiate` (safe default for non-DI or as DI binding).
- **Prefab factory**: `MonoPrefabFactory<T>` creates disabled instances from a prefab via an injected `IUnityObjectSpawner` (optional parent).
- **ObjectPool**: `ObjectPool<TParam1, TValue>` — thread-safe, auto-scaling pool with `ReaderWriterLockSlim`, active-item tracking, deferred despawn queue. Requires `TValue : IPoolable<TParam1, IMemoryPool>`.
- **FastObjectPool**: `FastObjectPool<T>` — abstract, lightweight main-thread pool. Stack-based, no locks, no active-item tracking overhead. Smart auto-shrink via peak-decay.
- **MonoFastPool**: `MonoFastPool<T>` — concrete `FastObjectPool` for Unity `Component`; auto `SetActive`, parent management, `Object.Destroy` cleanup.
- **NativePool**: `NativePool<T>` (DOD) — Burst/Jobs-compatible `struct` pool for `unmanaged` types. `NativeArray`-backed, zero GC, O(1) swap-and-pop despawn, batch spawn/despawn. Requires `com.unity.collections`.
- **EntityPool**: `EntityPool<TData>` (ECS) — pool for ECS `Entity` with sync and `EntityCommandBuffer`-based spawn/despawn. Requires `com.unity.entities`.
- **Low-GC hot paths**: swap-and-pop O(1) despawn; deferred despawns during `Maintenance()` to reduce lock contention.

### Pool comparison

|                      | `ObjectPool`                         | `MonoFastPool`     | `NativePool`              | `EntityPool`    |
| -------------------- | ------------------------------------ | ------------------ | ------------------------- | --------------- |
| Thread-safe          | Yes (RWLock)                         | No (main-thread)   | No (single-thread or job) | No (main / ECB) |
| Active tracking      | Yes (`UpdateActiveItems`)            | No                 | Yes (index-based)         | Yes (HashSet)   |
| Auto-scale           | Expand + shrink                      | Expand + shrink    | Manual `Expand`           | Manual          |
| GC alloc on hot path | Near-zero                            | Zero               | Zero                      | Zero (ECB path) |
| Best for             | Complex systems, multi-thread access | Simple GO spawning | DOD / Burst / Jobs        | ECS entities    |

### Compatibility

- Unity 2022.3+
- .NET 4.x (Unity) / modern .NET (for Pure C# samples)
- **Optional dependencies**: `com.unity.collections` + `com.unity.burst` (for `NativePool`); `com.unity.entities` + `com.unity.mathematics` + `com.unity.transforms` (for `EntityPool`)

### Install

This repo embeds the package under `Assets/ThirdParty`. Package name: `com.cyclone-games.factory`.

- Keep it embedded, or reference via UPM in your own projects.

### Quick start

1. Pure C# factory

```csharp
using CycloneGames.Factory.Runtime;

public class DefaultFactory<T> : IFactory<T> where T : new()
{
    public T Create() => new T();
}

var intFactory = new DefaultFactory<int>();
int number = intFactory.Create();
```

2. Unity prefab spawning (no DI)

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

3. Prefab factory + pooling

```csharp
using UnityEngine;
using CycloneGames.Factory.Runtime;

// Pooled item must implement IPoolable<TParam1, IMemoryPool>
// Note: IPoolable extends IDisposable — implement Dispose() for cleanup.
public sealed class Bullet : MonoBehaviour, IPoolable<BulletData, IMemoryPool>
{
    private IMemoryPool owningPool;
    public void OnSpawned(BulletData data, IMemoryPool pool) { owningPool = pool; /* init */ }
    public void OnDespawned() { owningPool = null; /* reset */ }
    public void Dispose() { } // Required by IDisposable

    public void GameUpdate() { /* per-frame update */ }
}

public struct BulletData { public Vector3 Position; public Vector3 Velocity; }

// Setup
var spawner = new DefaultUnityObjectSpawner();
var factory = new MonoPrefabFactory<Bullet>(spawner, bulletPrefab, parentTransform);
var pool = new ObjectPool<BulletData, Bullet>(factory, initialCapacity: 16);

// Use
var bullet = pool.Spawn(new BulletData { Position = start, Velocity = dir });

// In your game loop (e.g. Update)
pool.UpdateActiveItems(b => b.GameUpdate());
pool.Maintenance(); // Required: processes deferred despawns and auto-scaling
```

4. MonoFastPool (lightweight Unity pool)

```csharp
using CycloneGames.Factory.Runtime;

// No IPoolable required — just a Component + prefab
var pool = new MonoFastPool<MyComponent>(prefab, parent, initialCapacity: 32, autoSetActive: true);
var item = pool.Spawn();      // activates and returns from pool
pool.Despawn(item);            // deactivates and returns to pool

// Cleanup when done
pool.Dispose();
```

5. NativePool (DOD / Burst / Jobs)

```csharp
using CycloneGames.Factory.Runtime;
using Unity.Collections;

var pool = new NativePool<BulletData>(capacity: 1024, Allocator.Persistent);

int index = pool.Spawn(new BulletData { Position = float3.zero, Speed = 10f });

// Access active items for IJobParallelFor
NativeArray<BulletData> active = pool.ActiveItems;

// Bulk despawn with mask
var mask = new NativeArray<bool>(pool.ActiveCount, Allocator.Temp);
mask[index] = true;
pool.DespawnBatch(mask);
mask.Dispose();

pool.Dispose(); // Required: frees NativeArray
```

### IPoolable interfaces

```
IPoolable                       → OnSpawned(), OnDespawned(), Dispose()
IPoolable<TParam1>              → OnSpawned(TParam1), OnDespawned(), Dispose()
IPoolable<TParam1, TParam2>     → OnSpawned(TParam1, TParam2), OnDespawned(), Dispose()
ITickable                       → Tick()
```

All `IPoolable` variants extend `IDisposable`. `ObjectPool` invokes `OnSpawned`/`OnDespawned` automatically; `MonoFastPool` does **not** invoke `IPoolable` callbacks (it manages `SetActive` only).

### Pool lifecycle

- **`MaxCapacity`** / **`MinCapacity`**: set upper/lower bounds on pool size (default: unlimited / 16).
- **`WarmupCoroutine(count, batchSize)`**: pre-creates objects spread across frames (avoids load-time spikes). Available on both `ObjectPool` and `FastObjectPool`.
- **`DespawnAllActive()`**: returns all active items to the pool in one call.
- **`Dispose()`**: releases all pooled objects. Call in `OnDestroy` or scope exit. All pool types implement `IDisposable`.
- **`Clear()`**: removes all inactive items without disposing the pool.

```csharp
// Pre-warm during loading
pool.MaxCapacity = 500;
StartCoroutine(pool.WarmupCoroutine(200, batchSize: 16));

// Cleanup
pool.Dispose();
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
- `Maintenance()` processes deferred despawns and evaluates shrink logic (call periodically).
- `UpdateActiveItems(action)` allows thread-safe iteration over active items.

### Assemblies & dependencies

| Assembly                           | Optional Dependencies                                                                          | Conditional Defines                                           |
| ---------------------------------- | ---------------------------------------------------------------------------------------------- | ------------------------------------------------------------- |
| `CycloneGames.Factory.Runtime`     | —                                                                                              | —                                                             |
| `CycloneGames.Factory.DOD.Runtime` | `com.unity.collections`, `com.unity.burst`                                                     | `PRESENT_COLLECTIONS`, `PRESENT_BURST`                        |
| `CycloneGames.Factory.ECS.Runtime` | `com.unity.entities`, `com.unity.collections`, `com.unity.mathematics`, `com.unity.transforms` | `PRESENT_BURST`, `PRESENT_ECS`                                |
| `CycloneGames.Factory.Samples`     | UniTask, Burst, Collections, Mathematics                                                       | `PRESENT_MATHEMATICS`, `PRESENT_COLLECTIONS`, `PRESENT_BURST` |
| `CycloneGames.Factory.ECS.Samples` | Entities, Burst, Collections, Mathematics, Transforms                                          | `PRESENT_BURST`, `PRESENT_ECS`                                |

### Samples

Under `Samples/`:

- `PureCSharp/` data-only systems using `ObjectPool`.
- `PureUnity/` minimal `IUnityObjectSpawner` prefab spawning + `AdvancedObjectPoolSample` (WarmupCoroutine, MaxCapacity, Dispose).
- `OOPBullet/` full `MonoFastPool<Bullet>` demo with `BulletSpawner`, `ITickable`, Rigidbody bullets.
- `DODBullet/` three DOD approaches: raw NativeArray, Jobs, and `NativePool<T>` — with GPU instancing.
- `Benchmarks/PureCSharp/` pure C# factory/pooling benchmarks.
- `Benchmarks/Unity/` Unity GameObject pooling vs Instantiate, memory profiling, stress tests.

Under `ECS/Samples/`:

- `BulletSpawnerAuthoring`, `BulletAuthoring`, `BulletPoolManagerSystem` — ECS entity pooling demo.

### Benchmarks

- Benchmark samples live under `Samples/Benchmarks/` and save reports to `BenchmarkReports/` (`.txt`, `.md`, `.SCH.md`).
- Benchmark samples are AI-authored; other package code is authored by the maintainer.
- Reports include: performance tables, memory analysis, bar charts, and auto-generated recommendations with pairwise comparison data.

### Performance expectations (indicative)

- **CPU**: pooling can be 2–10× faster than direct `Instantiate`/`Destroy` for GameObjects.
- **Memory**: 50–90% reduction in GC allocations; pre-warmed pools achieve near-zero runtime alloc.
- **NativePool**: zero GC by design (unmanaged `NativeArray` backing).
- **Unity GameObjects**: 5–20× faster than `Instantiate()`/`Destroy()` in typical pairwise benchmarks.

### License

See repository license.
