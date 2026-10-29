# CycloneGames.Spatial

[English | 简体中文](README.SCH.md)

Uniform spatial hash grid for Unity and plain C# hosts. A radius query visits only the cells the radius touches, so the
cost follows the number of nearby elements instead of the total count.

## Contents

- [What this is](#what-this-is)
- [Quick start](#quick-start)
- [Cells, handles, layers](#cells-handles-layers)
- [Queries](#queries)
- [Tutorial](#tutorial)
  - [1. Track entities and query around a point](#1-track-entities-and-query-around-a-point)
  - [2. Find the nearest enemy](#2-find-the-nearest-enemy)
  - [3. Move entities](#3-move-entities)
  - [4. Rebuild once, query many times](#4-rebuild-once-query-many-times)
  - [5. Pick a cell size](#5-pick-a-cell-size)
  - [6. Large worlds](#6-large-worlds)
- [Reference](#reference)
  - [API](#api)
  - [Capacity](#capacity)
  - [Memory](#memory)
  - [Complexity](#complexity)
  - [Threading](#threading)
  - [Allocation](#allocation)
  - [Diagnostics](#diagnostics)
  - [Limits](#limits)
  - [Interfaces](#interfaces)
- [Common mistakes](#common-mistakes)

## What this is

The world is divided into uniform cells, and each cell keeps a chain of the elements in it. A radius query walks the
cells the radius covers and tests the elements it finds there.

Use it when you need proximity queries over hundreds to hundreds of thousands of entities, when the entities are plain
data rather than physics bodies, and when the same code has to run in a headless server or a unit test without Unity.

Do not use it for collision resolution, ray casts, or swept tests. Those belong to a narrow phase, yours or
`CycloneGames.DeterministicMath`. It is also a poor fit below a few hundred elements, where a linear scan is faster and
much easier to read.

## Quick start

```csharp
using CycloneGames.Spatial.Core;

// One cell every 4 world units.
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

The query writes into a buffer you own. Removal takes the handle that insert returned.

## Cells, handles, layers

**Cells.** `GridSettings` holds the cell size and the world origin. A position maps to a cell with
`floor((position - origin) / cellSize)`. Floor, not truncation, so -0.5 belongs to cell -1.

**Handles.** `SpatialHandle` is a 16-byte value returned by `TryInsert` and used by `TryUpdate`, `TryRemove` and
`TryGetPosition`. It stops working when the element is removed, and it does not start working again when the slot is
reused, because each reuse advances a 64-bit generation. A stale handle returns `false`. Keep the handle next to
whatever owns the entity.

**Layers.** Each element carries 32 layer bits. Queries match them with a bitwise AND.

```csharp
const uint Enemies = 1u << 0;
const uint Allies  = 1u << 1;

grid.TryInsert(entity, x, y, z, Enemies, out SpatialHandle handle);

int count = grid.QuerySphere(x, y, z, 20f, buffer, buffer.Length, layerMask: Enemies);
```

Filtering inside the traversal matters because the result is truncated. Filter afterwards and the budget may go to
elements you discard. Element layers must be non-zero: `TryInsert` converts zero to `SpatialLayers.Default`, and
`TrySetLayer` rejects it.

## Queries

| Query | Returns |
| --- | --- |
| `QuerySphere` | Elements whose stored position is inside the sphere. |
| `QueryBox` | Elements inside an axis-aligned box, all faces inclusive. |
| `QueryNearest` | The closest k elements inside a sphere, ordered by distance. |

Each writes into a caller-owned buffer and never grows it, so a small buffer means a truncated result. None of them
allocate.

`QueryNearest` fills a second buffer with squared distances. Squared distance avoids a square root and sorts the same
way as distance.

## Tutorial

### 1. Track entities and query around a point

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

`CountNearby` returns how many entries of `_buffer` are valid. Reusing the buffer keeps the query free of garbage.

### 2. Find the nearest enemy

Query a radius and sort the result yourself, and you can lose the nearest element: the query truncates before it knows
any distance. `QueryNearest` keeps the best candidates regardless of traversal order.

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

Keep the buffer small. The result buffer is insertion sorted, so the cost per accepted candidate grows with its length.
A few dozen entries is the intended range; beyond that, query a radius and sort it yourself.

### 3. Move entities

```csharp
grid.TryUpdate(handle, newX, newY, newZ);
```

Within one cell this rewrites three floats. Crossing a cell boundary unlinks the element from one chain and links it
into another. A `false` return means the handle is stale: the element was removed, or the grid was cleared or disposed.

`TryGetPosition` reads a stored position back, which helps when the handle is all you kept.

### 4. Rebuild once, query many times

The live grid pays for incremental insert and remove. A system that already holds a flat array of positions and
rebuilds its whole view every tick gets nothing from that bookkeeping. `SpatialSnapshot<TItem>` takes the array once and
answers from a sorted layout.

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

Call `Build` on a tick boundary, not mid-frame. A same-size rebuild reuses the arrays, so a per-tick refresh settles at
zero allocation. If `Build` fails, on a non-finite position or a cell outside the addressable span, the previous
contents stay intact and queryable.

### 5. Pick a cell size

Cell size trades memory against cells visited.

Too small and a radius covers many empty cells. Too large and every element lands in a few long chains, which is a
scan with extra steps. Start near half your typical query radius, then adjust until a cell holds a handful of elements
at the density you expect. Two systems with very different scales, characters at 2 m and buildings at 40 m, want two
grids rather than one compromise.

`EnsureCapacity` reserves slots and buckets together. Call it during a load, then watch `GrowthCount` and
`BucketRebuildCount` in `GetStatistics()`. Both stay flat in steady state; a rising counter means the reservation was
too small.

### 6. Large worlds

Positions are `float`, the same type as `Transform.position`. That works for a few kilometres and gets coarse beyond
it: around 0.5 mm of resolution at 8 km, 6 cm at 1 000 km. The index adds nothing to that; it stores exactly the floats
you give it. What degrades is your ability to tell two nearby points apart.

```csharp
// Rebase the origin. Every handle stays valid.
grid.SetOrigin(playerX, playerY, playerZ);
```

`SetOrigin` re-buckets every element in O(n) and leaves positions alone. Run it on a teleport or a loading boundary,
and shift the rest of your world by the same offset. For a world spanning thousands of kilometres, use one grid per
sector with a local origin.

`SpatialSnapshot` has a fixed span of ±1 048 576 cells per axis, about ±2 097 km at a 2 m cell. `Build` throws past it
rather than mis-indexing. The mutable grid uses 32-bit cell indices and does not reach a limit in practice.

## Reference

### API

`SpatialHashGrid<TItem>`

| Member | Notes |
| --- | --- |
| `TryInsert(item, x, y, z, out handle)` | Layer defaults to `SpatialLayers.Default`. |
| `TryInsert(item, x, y, z, layerMask, out handle)` | |
| `TryUpdate(handle, x, y, z)` | Keeps the layer. |
| `TryRemove(handle)` | Recycles the slot. |
| `TryGetPosition(handle, out x, out y, out z)` | |
| `TryGetLayer` / `TrySetLayer` | `TrySetLayer` rejects zero. |
| `QuerySphere` / `QueryBox` | Array sink and `List<T>` sink. |
| `QueryNearest` | Array sink plus squared distances. |
| `Clear()` | Keeps capacity, invalidates every handle. |
| `EnsureCapacity(n)` / `TrimExcess()` | Reserve and shrink. |
| `SetOrigin(x, y, z)` | O(n); handles survive. |
| `Count` / `Capacity` / `BucketCapacity` / `Settings` | |
| `GetStatistics()` / `ValidateStructure()` | The validator is Debug-only. |

`SpatialSnapshot<TItem>`

| Member | Notes |
| --- | --- |
| `Build(ReadOnlySpan<SpatialEntry<TItem>>)` | O(n log n); atomic on failure. |
| `QuerySphere` / `QueryBox` / `QueryNearest` | Same semantics as the grid. |
| `Clear()` / `Count` / `Capacity` / `BuildCount` | |
| `AxisLimitCells` | Addressable span per axis. |

### Capacity

Two capacities, reported separately: `Capacity` for elements and `BucketCapacity` for addressable cells.

`SpatialCapacityPolicy.Grow`, the default, doubles the slot arrays when an insert arrives full. `Fixed` returns `false`
instead, for paths where a reallocation must not happen mid-frame.

`TrimExcess` is the only path that shrinks. It never moves a live element, so handles survive, and for the same reason
it cannot shrink below the highest slot ever used. Call `Clear` first if you need that.

### Memory

One flat array per per-element field, indexed by slot:

| Array | Bytes per slot |
| --- | --- |
| payload | `sizeof(TItem)` (8 for a reference type) |
| chain links and cell key | 20 |
| position (xyz) | 12 |
| generation, layer, state, free stack | 17 |
| total | 49 + `sizeof(TItem)` |

```
slotBytes   = Capacity * (49 + sizeof(TItem))
bucketBytes = BucketCapacity * 17
```

Capacities round up to powers of two, so budget for up to twice the slot count and about three and a half times the
bucket count relative to the element count. 100 000 reference-typed elements in distinct cells land near 11.9 MB.

### Complexity

| Operation | Amortized | Worst |
| --- | --- | --- |
| `TryInsert` | O(1) | O(n) on growth or bucket rebuild |
| `TryRemove` | O(1) | O(1) |
| `TryUpdate` same cell | O(1) | O(1) |
| `TryUpdate` across cells | O(1) | O(k), k is the target chain length |
| `QuerySphere` / `QueryBox` | O(m + k) | O(n) when the cell range exceeds the budget |
| `QueryNearest` | O(m + k + a·r) | same, plus the linear fallback |
| `Clear` | O(occupied slots) | same |
| `SetOrigin` | O(n) | O(n) |

`m` is covered cells, `k` candidates inside them, `a` accepted candidates, `r` the result limit.

### Threading

Single writer, no internal lock. All writes come from one thread, and reads may interleave with writes on that thread.
A Debug build throws when a second thread writes. Cross-thread readers need their own lock around every call, including
`Count`.

An index can be built on one thread and used on another as long as nothing was written before the handover. If
something was, call `RebindWriteThread()` first.

### Allocation

Steady state is zero: no boxing, no closures, no iterators, no `params`, no string formatting, no LINQ. The query walk
uses a stack cursor rather than an enumerator.

Two allocations are allowed: capacity growth, which is counted, and the first `ValidateStructure` call, which allocates
a Debug-only scratch array.

### Diagnostics

`GetStatistics()` reports element count, occupied cells, both capacities, peak elements, growth events, bucket rebuilds,
cumulative inserts and removes, and generation wraps. It allocates nothing.

`ValidateStructure()` walks every bucket and chain, checking chain termination, back-links, single ownership, cell key
agreement, free-slot accounting and non-zero generations. Debug only, and it returns `true` unchecked in Release.

### Limits

| Limit | Value | Past it |
| --- | --- | --- |
| Cells per query | 65 536 | Falls back to a linear scan, still correct |
| Snapshot span | ±1 048 576 cells per axis | `Build` throws |
| Grid cell index | 32-bit per axis | Not reachable in practice |
| Slot capacity | 16 777 216 | `EnsureCapacity` throws |

### Interfaces

`ISpatialReadOnlyIndex<TItem>` declares the three queries. `ISpatialMutableIndex<TItem>` adds insert, update, remove and
clear. The grid implements both; the snapshot implements the read-only one.

Where a method takes the interface, the call costs one dispatch. Where throughput matters, take the concrete type.

## Common mistakes

| Symptom | Cause |
| --- | --- |
| Entities vanish from queries | `Fixed` policy with an under-sized index and an ignored `TryInsert` result |
| Results contain elements outside the radius | Boundary tolerance, by design; filter in your narrow phase |
| `TryUpdate` always fails | Stale handle: removed element, cleared grid, or a different instance |
| Periodic frame spikes | `BucketRebuildCount` rising; reserve with `EnsureCapacity` |
| Silent corruption across threads | Release compiles the thread guard out; one grid per thread, or lock |
| Nearest result misses the closest entity | Built from a truncated radius query instead of `QueryNearest` |
