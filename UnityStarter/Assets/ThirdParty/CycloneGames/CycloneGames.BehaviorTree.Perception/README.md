# CycloneGames.BehaviorTree.Perception

[简体中文](README.SCH.md)

- [Design purpose](#purpose)
- [Assemblies and dependencies](#assemblies)
- [Model](#model)
- [Usage](#usage)
- [Semantics](#semantics)
- [Cost](#cost)
- [API](#api)
- [Limits](#limits)

## <a id="purpose"></a>Design purpose

Publish one sensor's result into blackboard slots, so a tree reads perception through ordinary keys instead of holding a sensor reference.

The bridge copies what the sensor already computed. It does not evaluate sight, memory or team attitude, and it does not decide which target matters beyond "the nearest one the sensor reports".

## <a id="assemblies"></a>Assemblies and dependencies

| Assembly | Purpose | Consumer reference |
| --- | --- | --- |
| `CycloneGames.BehaviorTree.Runtime.Integrations.Perception` | Reads a sensor and publishes to the blackboard. | Explicit |
| `CycloneGames.BehaviorTree.Integrations.Perception.Tests.Editor` | Contract, fuzz, allocation and soak tests. | Test Runner only |

The assembly references `CycloneGames.BehaviorTree.Core` and `CycloneGames.AIPerception`. It is `autoReferenced: false`, so a project that never reads perception does not compile against it.

## <a id="model"></a>Model

A sensor keeps its results sorted by distance, nearest first. The bridge copies the first `capacity` of them into a contiguous array of `PerceptionTarget`, so index 0 is always the nearest target of the last update.

| Field | Meaning |
| --- | --- |
| `Handle` | `PerceptibleHandle` of the target. |
| `Position` | Last known world position. |
| `Distance` | Distance at the last detection, in the unit the sensor reports. |
| `Visibility` | 0..1 at the last detection; decays while the target is only remembered. |
| `IsFromMemory` | True when the sensor reports it from memory rather than from a live detection. |

Publishing writes four kinds of slot: a visibility flag, a distance, a position as `BTFloat3`, and a target count. A target that is only remembered still counts as visible, because the sensor still reports it; read `IsFromMemory` when the distinction matters.

Distance is published in the unit the sensor uses, which is linear distance. The Spatial adapter publishes squared distance because that is what a spatial index reports. Mixing the two means mixing units, and the key names should say which one is stored.

## <a id="usage"></a>Usage

### 1. Create one bridge per sensor

```csharp
var bridge = new PerceptionBlackboardBridge(perception.SightSensor, capacity: 4);
```

`capacity` bounds how many targets are copied per update. It does not change what the sensor computes.

### 2. Update once per frame, after perception

```csharp
bridge.Update();
bridge.PublishNearest(blackboard, visibleKey, distanceKey);
bridge.PublishNearestPosition(blackboard, positionKey);
bridge.PublishCount(blackboard, countKey);

// Optional: who it is, and whether it is currently seen.
bridge.PublishNearestHandle(blackboard, handleKey);
bridge.PublishNearestFromMemory(blackboard, fromMemoryKey);
```

The handle is written as three longs (registry, id, generation) because one 64-bit word cannot hold the registry id. Read it back with `PerceptionBlackboardBridge.TryUnpackHandle`; a zero triple means "no target".

Call it on the thread that commits perception results, after the perception update. The bridge records the creating thread and rejects calls from any other.

### 3. Read the slots from the tree

```csharp
builder.Condition(bb => bb.GetBool(visibleKey));
builder.Action(bb => bb.GetFloat(distanceKey) < 12f
    ? RuntimeState.Success
    : RuntimeState.Failure);
```

The slots are ordinary blackboard keys, so any condition, decorator or comparison node works without knowing about perception.

### 4. Merge several senses

One bridge serves one sensor. When a tree should react to the nearest of several senses, an aggregator merges the bridges into one list and one set of keys.

```csharp
var aggregator = new PerceptionAggregator(capacity: 8, bridgeCapacity: 4);
aggregator.Add(sightBridge);
aggregator.Add(hearingBridge);

aggregator.Update();
aggregator.PublishNearest(blackboard, visibleKey, distanceKey);
aggregator.PublishNearestHandle(blackboard, handleKey);
```

Merge rules, in order:

| Rule | Behaviour |
| --- | --- |
| Identity | Targets are keyed by `PerceptibleHandle`; one handle yields one entry. |
| Preference | A live detection beats a remembered one. |
| Tie | Between two entries of the same kind, the nearer one wins. |
| Order | Output is sorted by distance, ascending. |
| Capacity | Beyond capacity the nearest entries survive and `IsTruncated` is set. |

The merge collects every target before collapsing duplicates, so the result does not depend on the order bridges report. `Add` sizes the internal buffer and is the only allocating call; `Update` and the publish methods allocate nothing.

### 5. Check truncation when the capacity is tight

```csharp
if (bridge.IsTruncated)
{
    // More targets were reported than the buffer holds; raise the capacity or accept the nearest N.
}
```

## <a id="semantics"></a>Semantics

- An empty sensor writes `false` and a zero distance. Read the flag before using the distance.
- Publishing goes through `SetBool`, `SetFloat`, `SetFloat3` and `SetInt`. The blackboard drops unchanged writes, so a stable target neither notifies observers nor enters the network delta.
- `Update` reads the sensor only; it does not advance the sensor or touch the registry.
- One bridge serves one sensor and one thread. Two bridges on the same sensor are allowed and read the same results.
- `Dispose` clears the buffer and releases nothing else; the sensor lifetime stays with the caller.

## <a id="cost"></a>Cost

| Operation | Cost |
| --- | --- |
| `Update` | Linear in `min(sensor results, capacity)`. |
| `Publish*` | Constant time each, dominated by the blackboard write. |
| Memory | One `PerceptionTarget` array of `capacity` entries, allocated once. |

Nothing allocates after the constructor. A `PerceptionTarget` is a readonly struct of a handle, a position, two floats and a flag, so the array is contiguous and iteration stays in cache for the capacities this adapter is used with.

## <a id="api"></a>API

```csharp
PerceptionBlackboardBridge(ISensor sensor, int capacity = 8);

bool Update();

void PublishNearest(RuntimeBlackboard blackboard, int visibleKey, int distanceKey);
void PublishNearestPosition(RuntimeBlackboard blackboard, int positionKey);
void PublishNearestHandle(RuntimeBlackboard blackboard, int handleKey);
void PublishNearestFromMemory(RuntimeBlackboard blackboard, int fromMemoryKey);
void PublishCount(RuntimeBlackboard blackboard, int countKey);

static RuntimeBlackboardLong3 PackHandle(in PerceptibleHandle handle);
static bool TryUnpackHandle(in RuntimeBlackboardLong3 packed, out PerceptibleHandle handle);

int Count { get; }
int Capacity { get; }
bool IsTruncated { get; }
PerceptionTarget Nearest { get; }
ReadOnlySpan<PerceptionTarget> Targets { get; }
ISensor Sensor { get; }
```

```csharp
PerceptionAggregator(int capacity = 8, int bridgeCapacity = 4);
bool Add(PerceptionBlackboardBridge bridge);
bool Update();

int Count { get; }
int Capacity { get; }
bool IsTruncated { get; }
PerceptionTarget Nearest { get; }
ReadOnlySpan<PerceptionTarget> Targets { get; }
```

## <a id="limits"></a>Limits

- One sensor per bridge. A project that wants sight and hearing in the same tree uses two bridges and two sets of keys.
- Nearest only. Selecting by team, threat or visibility is left to the caller, because those rules differ per project.
- Pull only. The adapter reads results when `Update` runs; the perception event layer exists for consumers that need to react to a change rather than to a state.
- No distance conversion. The published value is the sensor's number, not a squared or normalised variant.
