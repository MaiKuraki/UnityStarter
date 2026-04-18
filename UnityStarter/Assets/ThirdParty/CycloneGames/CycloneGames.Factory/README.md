## CycloneGames.Factory

<div align="left">English | <a href="./README.SCH.md">简体中文</a></div>

---

High-performance, low-GC factory and object-pooling utilities for Unity and pure C#. Designed to be DI-friendly and easy to adopt incrementally.

### Features

- **Factory interfaces**: `IFactory<TValue>`, `IFactory<TArg, TValue>` for creation; `IUnityObjectSpawner` for Unity `Object` instantiation.
- **Default spawner**: `DefaultUnityObjectSpawner` wraps `Object.Instantiate` (safe default for non-DI or as DI binding).
- **Prefab factory**: `MonoPrefabFactory<T>` creates disabled instances from a prefab via an injected `IUnityObjectSpawner` (optional parent).
- **PoolBase**: `PoolBase<TValue>` — abstract base for all managed pools. Provides active-item tracking (O(1) swap-remove), `SoftCapacity`/`HardCapacity`/`OverflowPolicy`/`TrimPolicy`, diagnostics, batch operations (`DespawnStep`, `WarmupStep`), and coroutine helpers (`DespawnAllCoroutine`, `WarmupCoroutine`).
- **ObjectPool**: `ObjectPool<TParam1, TValue>` — sealed, auto-scaling pool with parametric spawn. Requires `IFactory<TValue>` and `TValue : IPoolable<TParam1, TValue>`. Not thread-safe on its own.
- **ConcurrentMemoryPool**: `ConcurrentMemoryPool<TValue>` — thread-safe `lock`-based wrapper around any `IMemoryPool<TValue>`.
- **FastObjectPool**: `FastObjectPool<T>` — abstract, lightweight main-thread pool. Parameterless `Spawn()`/`TrySpawn()`, inherits all `PoolBase` infrastructure.
- **MonoFastPool**: `MonoFastPool<T>` — concrete `FastObjectPool` for Unity `Component`; auto `SetActive`, parent management, `Object.Destroy` cleanup.
- **NativePool**: `NativePool<T>` (DOD) — simple index-based `struct` pool for `unmanaged` types. Compact active array, O(1) swap-and-pop despawn, batch spawn/despawn. No handle safety. Requires `com.unity.collections`.
- **NativeDensePool**: `NativeDensePool<T>` (DOD) — handle-based `struct` pool with `NativePoolHandle` (slot + generation). Stable references, O(1) operations, full diagnostics. Requires `com.unity.collections`.
- **NativeDenseColumnPool**: `NativeDenseColumnPool2<T0,T1>`, `ColumnPool3`, `ColumnPool4` — SoA (Structure of Arrays) variants of `NativeDensePool` with parallel typed streams.
- **EntityPool**: `EntityPool<TData>` (ECS) — pool for ECS `Entity` with sync and `EntityCommandBuffer`-based spawn/despawn. Requires `com.unity.entities`.
- **Low-GC hot paths**: swap-and-pop O(1) despawn; all DOD pools are zero-GC by design (`NativeArray` backing).

### Pool comparison

|                      | `ObjectPool`        | `FastObjectPool` / `MonoFastPool` | `ConcurrentMemoryPool`  | `NativePool`              | `NativeDensePool` / `ColumnPoolN` | `EntityPool`            |
| -------------------- | ------------------- | --------------------------------- | ----------------------- | ------------------------- | --------------------------------- | ----------------------- |
| Thread-safe          | No                  | No (main-thread)                  | Yes (`lock`)            | No (single-thread or job) | No (single-thread or job)         | No (main / ECB)         |
| Active tracking      | Yes (Dict + List)   | Yes (inherited from PoolBase)     | Delegates to inner pool | No (index only)           | Yes (handle-based)                | Yes (Dict+List)         |
| Stable references    | Yes (object ref)    | Yes (object ref)                  | Delegates               | No (indices shift)        | Yes (`NativePoolHandle`)          | Yes (Entity)            |
| Auto-scale           | Expand + trim       | Expand + trim                     | Delegates               | Manual `Resize`           | Manual `Resize`                   | Expand via factory      |
| Diagnostics          | `PoolDiagnostics`   | `PoolDiagnostics`                 | Delegates               | None                      | `NativeDenseDiagnostics`          | `EntityPoolDiagnostics` |
| GC alloc on hot path | Near-zero           | Near-zero                         | Near-zero               | Zero                      | Zero                              | Zero (ECB path)         |
| Burst/Jobs safe      | No                  | No                                | No                      | `ActiveItems` safe        | `ActiveItems` / `StreamN` safe    | No                      |
| Best for             | Parametric spawning | Simple main-thread pooling        | Multi-thread access     | DOD / Burst / Jobs        | DOD with stable handles / SoA     | ECS entities            |

### Compatibility

- Unity 2022.3+
- .NET 4.x (Unity) / modern .NET (for pure C# samples)
- **Optional dependencies**: `com.unity.collections` + `com.unity.burst` (for `NativePool`, `NativeDensePool`); `com.unity.entities` + `com.unity.mathematics` + `com.unity.transforms` (for `EntityPool`)

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

3. Prefab factory + ObjectPool

```csharp
using UnityEngine;
using CycloneGames.Factory.Runtime;

// Pooled item must implement IPoolable<TParam1, TValue>
// The second type parameter is the item type itself (for self-despawn via pool reference).
// Note: IPoolable extends IDisposable — implement Dispose() for cleanup.
public sealed class Bullet : MonoBehaviour, IPoolable<BulletData, Bullet>
{
    private IDespawnableMemoryPool<Bullet> owningPool;
    public void OnSpawned(BulletData data, IDespawnableMemoryPool<Bullet> pool)
    {
        owningPool = pool;
        // init from data...
    }
    public void OnDespawned() { owningPool = null; /* reset state */ }
    public void Dispose() { } // Required by IDisposable

    public void ReturnToPool() => owningPool?.Despawn(this);
}

public struct BulletData { public Vector3 Position; public Vector3 Velocity; }

// Setup
var spawner = new DefaultUnityObjectSpawner();
var factory = new MonoPrefabFactory<Bullet>(spawner, bulletPrefab, parentTransform);
var pool = new ObjectPool<BulletData, Bullet>(factory,
    new PoolCapacitySettings(softCapacity: 16, hardCapacity: 256));

// Use
var bullet = pool.Spawn(new BulletData { Position = start, Velocity = dir });

// Iterate active items
pool.ForEachActive(b => b.GameUpdate());

// Batch despawn (e.g. process up to 8 per frame)
pool.DespawnStep(8);
```

4. MonoFastPool (lightweight Unity pool)

```csharp
using CycloneGames.Factory.Runtime;

// No IPoolable required — just a Component + prefab
var pool = new MonoFastPool<MyComponent>(prefab,
    initialCapacity: 32, root: parent, autoSetActive: true);
var item = pool.Spawn();      // activates and returns from pool
pool.Despawn(item);            // deactivates and returns to pool

// Cleanup when done
pool.Dispose();
```

5. NativePool (DOD — simple index-based)

```csharp
using CycloneGames.Factory.DOD.Runtime;
using Unity.Collections;

var pool = new NativePool<BulletData>(capacity: 1024, Allocator.Persistent);

int index = pool.Spawn(new BulletData { Position = float3.zero, Speed = 10f });

// Active items occupy [0..ActiveCount), safe for IJobParallelFor
NativeArray<BulletData> active = pool.ActiveItems;

// Bulk despawn with mask
var mask = new NativeArray<bool>(pool.ActiveCount, Allocator.Temp);
mask[index] = true;
pool.DespawnBatch(mask);
mask.Dispose();

pool.Dispose(); // Required: frees NativeArray
```

6. NativeDensePool (DOD — handle-based with stable references)

```csharp
using CycloneGames.Factory.DOD.Runtime;
using Unity.Collections;

var pool = new NativeDensePool<EnemyData>(capacity: 512, Allocator.Persistent);

// Spawn returns a stable handle (slot + generation)
pool.TrySpawn(new EnemyData { Health = 100 }, out NativePoolHandle handle, out int denseIndex);

// Read/write via handle — safe even after other items are despawned
pool.TryRead(handle, out EnemyData data);
pool.TryWrite(handle, new EnemyData { Health = data.Health - 10 });

// Handle validation
bool alive = pool.Contains(handle);

// Despawn via handle
pool.Despawn(handle);

pool.Dispose();
```

7. NativeDenseColumnPool (DOD — SoA multi-stream)

```csharp
using CycloneGames.Factory.DOD.Runtime;
using Unity.Collections;
using Unity.Mathematics;

// Two parallel streams: positions + velocities
var pool = new NativeDenseColumnPool2<float3, float3>(capacity: 1024, Allocator.Persistent);

pool.TrySpawn(float3.zero, new float3(1, 0, 0), out NativePoolHandle handle, out int idx);

// Access dense arrays for Burst jobs
NativeArray<float3> positions  = pool.Stream0; // [0..CountActive)
NativeArray<float3> velocities = pool.Stream1;

pool.Dispose();
```

### IPoolable interfaces

```
IPoolable                          → OnSpawned(), OnDespawned(), Dispose()
IPoolable<in TParam1>              → OnSpawned(TParam1), OnDespawned(), Dispose()
IPoolable<in TParam1, TValue>      → OnSpawned(TParam1, IDespawnableMemoryPool<TValue>), OnDespawned(), Dispose()
ITickable                          → Tick()
```

All `IPoolable` variants extend `IDisposable`. The two-parameter variant receives the pool reference as the second argument, enabling items to despawn themselves.

`ObjectPool` invokes `OnSpawned`/`OnDespawned` automatically; `MonoFastPool` does **not** invoke `IPoolable` callbacks (it manages `SetActive` only).

### Pool lifecycle

- **`SoftCapacity`** / **`HardCapacity`**: soft capacity is the target pool size (used for trim decisions); hard capacity is the absolute upper bound (0 = unlimited). Configured via `PoolCapacitySettings`.
- **`OverflowPolicy`**: `Throw` (default) or `ReturnNull` when hard capacity is reached.
- **`TrimPolicy`**: `Manual` (default) or `TrimOnDespawn` (auto-destroys excess items on despawn when inactive count exceeds soft capacity).
- **`Prewarm(count)`**: pre-creates objects synchronously.
- **`WarmupCoroutine(count, batchSize)`**: pre-creates objects spread across frames (avoids load-time spikes).
- **`WarmupStep(maxItems)`**: creates up to N items per call (for manual frame-spread warmup).
- **`DespawnAll()`**: returns all active items to the pool in one call.
- **`DespawnStep(maxItems)`**: despawns up to N active items per call (for gradual batch despawn).
- **`DespawnAllCoroutine(batchSize)`**: coroutine version of batch despawn.
- **`TrimInactive(targetCount)`**: destroys excess inactive items down to the target count.
- **`ForEachActive(action)`**: iterates over all active items.
- **`Clear()`**: removes all inactive items without disposing the pool.
- **`Dispose()`**: releases all pooled objects. Call in `OnDestroy` or scope exit. All pool types implement `IDisposable`.

```csharp
// Configure capacity
var settings = new PoolCapacitySettings(
    softCapacity: 64,
    hardCapacity: 500,
    overflowPolicy: PoolOverflowPolicy.ReturnNull,
    trimPolicy: PoolTrimPolicy.TrimOnDespawn);
var pool = new ObjectPool<BulletData, Bullet>(factory, settings);

// Pre-warm during loading
StartCoroutine(pool.WarmupCoroutine(200, batchSize: 16));

// Cleanup
pool.Dispose();
```

### DI containers

- Bind `IUnityObjectSpawner` → `DefaultUnityObjectSpawner` (or your own spawner integrating Addressables/ECS).
- Bind your `IFactory<T>` or use `MonoPrefabFactory<T>` where appropriate.
- Wrap with `ConcurrentMemoryPool<T>` if thread safety is needed.
- Pools can be singletons or scoped depending on lifecycle.

```csharp
builder.Register<IUnityObjectSpawner, DefaultUnityObjectSpawner>(Lifetime.Singleton);
builder.Register<IFactory<Bullet>>(c => new MonoPrefabFactory<Bullet>(
    c.Resolve<IUnityObjectSpawner>(), bulletPrefab, parent)).AsSelf();
```

### Assemblies & dependencies

| Assembly                            | Optional Dependencies                                                                          | Conditional Defines                                           |
| ----------------------------------- | ---------------------------------------------------------------------------------------------- | ------------------------------------------------------------- |
| `CycloneGames.Factory.Runtime`      | —                                                                                              | —                                                             |
| `CycloneGames.Factory.DOD.Runtime`  | `com.unity.collections`, `com.unity.burst`                                                     | `PRESENT_COLLECTIONS`, `PRESENT_BURST`                        |
| `CycloneGames.Factory.ECS.Runtime`  | `com.unity.entities`, `com.unity.collections`, `com.unity.mathematics`, `com.unity.transforms` | `PRESENT_BURST`, `PRESENT_ECS`                                |
| `CycloneGames.Factory.Samples`      | UniTask, Burst, Collections, Mathematics                                                       | `PRESENT_MATHEMATICS`, `PRESENT_COLLECTIONS`, `PRESENT_BURST` |
| `CycloneGames.Factory.ECS.Samples`  | Entities, Burst, Collections, Mathematics, Transforms                                          | `PRESENT_BURST`, `PRESENT_ECS`                                |
| `CycloneGames.Factory.Tests.Editor` | Collections, Burst, Entities (all optional)                                                    | `PRESENT_COLLECTIONS`, `PRESENT_BURST`, `PRESENT_ECS`         |

All optional dependencies use `versionDefines` in `.asmdef` files. Code is guarded by `#if` directives, so assemblies compile cleanly regardless of which packages are installed.

### Samples

Under `Samples/`:

- `PureCSharp/` — data-only particle system simulation using `ObjectPool`.
- `PureUnity/` — `IUnityObjectSpawner` prefab spawning + `AdvancedObjectPoolSample` (`WarmupCoroutine`, `PoolCapacitySettings`, `Profile` display, `Dispose`).
- `OOPBullet/` — `MonoFastPool<Bullet>` demo with `BulletSpawner`, `ITickable`, Rigidbody bullets.
- `DODBullet/` — three DOD approaches: raw NativeArray, Jobs, and `NativePool<T>` — with GPU instancing.
- `Benchmarks/PureCSharp/` — pure C# factory/pooling benchmarks.
- `Benchmarks/Unity/` — Unity GameObject pooling vs Instantiate, memory profiling, stress tests.

Under `ECS/Samples/`:

- `BulletSpawnerAuthoring`, `BulletAuthoring`, `ECSHighLoadBenchmark` — ECS entity pooling demo.

### Performance expectations (indicative)

- **CPU**: pooling can be 2–10× faster than direct `Instantiate`/`Destroy` for GameObjects.
- **Memory**: 50–90% reduction in GC allocations; pre-warmed pools achieve near-zero runtime alloc.
- **NativePool / NativeDensePool**: zero GC by design (unmanaged `NativeArray` backing).
- **Unity GameObjects**: 5–20× faster than `Instantiate()`/`Destroy()` in typical pairwise benchmarks.

### License

See repository license.
