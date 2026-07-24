# CycloneGames.AIPerception

[English | 简体中文](README.SCH.md)

CycloneGames.AIPerception is a Unity runtime module for continuous world perception. It registers perceptible targets, captures an immutable world snapshot, runs sight/hearing/proximity prefilters through Burst/Jobs, and exposes live and remembered detections to gameplay code.

## Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Quick Start](#quick-start)
- [Core Concepts](#core-concepts)
- [Usage Guide](#usage-guide)
- [Advanced Topics](#advanced-topics)
- [Common Scenarios](#common-scenarios)
- [Performance and Memory](#performance-and-memory)
- [Troubleshooting](#troubleshooting)

## Overview

Use this module when Unity agents need continuous senses:

- sight within a 3D cone, with optional main-thread Physics line-of-sight checks;
- hearing from perceptibles marked as continuous sound sources (`IsSoundSource == true`);
- proximity within a 3D radius;
- short-lived stimulus memory after direct detection ends;
- distance-based sensor update throttling (LOD).

The data model is genre-neutral. A target has a stable integer type, position, detection radius, loudness, sound-source state, and an optional tag. Teams, stealth, threat, faction, damage, and behavior selection remain in product code.

### Assemblies and dependencies

| Assembly | Platform | Responsibility |
| --- | --- | --- |
| `CycloneGames.AIPerception` | Runtime | Registry, snapshots, spatial broadphase, sensors, jobs, memory, and Unity components |
| `CycloneGames.AIPerception.Editor` | Editor | Custom Inspectors, validation, runtime diagnostics, and Scene gizmos |
| `CycloneGames.AIPerception.Tests.Editor` | Editor tests | Core contract and boundary tests |

Runtime dependencies: `Unity.Burst`, `Unity.Collections`, `Unity.Mathematics`.

## Architecture

```mermaid
flowchart LR
    P["IPerceptible / PerceptibleComponent"] -->|register / unregister| R["PerceptibleRegistry"]
    R -->|capture dynamic values| S["Immutable frame snapshot"]
    S --> G["SpatialGrid broadphase"]
    G --> J["Burst sensor prefilter jobs"]
    J -->|complete| F["Main-thread refinement"]
    F --> B["Bounded results and stimulus memory"]
    B --> Q["Gameplay query API"]
    M["PerceptionManagerComponent"] --> SM["SensorManager"]
    SM --> R
    SM --> J
    SM --> F

    classDef authoring fill:#2f6f9f,color:#fff,stroke:#183d58;
    classDef runtime fill:#2d7d57,color:#fff,stroke:#17442f;
    classDef worker fill:#9a6b1f,color:#fff,stroke:#594012;
    classDef output fill:#7b4d9c,color:#fff,stroke:#432957;
    class P,M authoring;
    class R,S,G,SM,F runtime;
    class J worker;
    class B,Q output;
```

One manager update:

1. Complete and commit work still pending from the previous deferred update.
2. Sample every registered `IPerceptible` on the owner thread.
3. Rebuild the sorted spatial snapshot and native copy only when captured data changed.
4. Select candidates for sensors whose effective interval elapsed.
5. Run Burst-compatible prefilters against the immutable native snapshot.
6. Complete immediately or in `LateUpdate`, depending on scheduling configuration.
7. Perform Unity Physics refinement on the main thread, then commit live results and memory.

Jobs borrow the registry snapshot and sensor-owned buffers. The manager completes pending jobs before publishing another snapshot or removing a sensor.

## Quick Start

### 1. Add a manager

Create a scene object and add `PerceptionManagerComponent`. An instance is created automatically when an `AIPerceptionComponent` initializes, but an explicit manager makes world capacity, spatial cell size, scheduling, and LOD visible.

Keep `Deferred Job Completion` enabled unless gameplay needs results in the same `Update`. Start with a finite `Maximum Perceptibles`. Tune `Spatial Cell Size` from profiling data.

### 2. Mark targets

Add `PerceptibleComponent` to each detectable object:

```csharp
using CycloneGames.AIPerception.Runtime;

PerceptibleComponent target = GetComponent<PerceptibleComponent>();
target.SetTypeId(PerceptibleTypes.Enemy);
target.SetDetectionRadius(1.25f);
target.SetSoundSource(true);
target.SetLoudness(0.8f);
```

- assign a stable project-owned `Type ID`;
- set `Detection Radius` to its world-space detection extent;
- assign `LOS Point` when the transform origin is unsuitable;
- enable `Is Sound Source` only for continuous hearing emitters;
- set `Loudness` to a non-negative hearing range multiplier.

`PerceptibleComponent` registers automatically in `OnEnable`. If finite world capacity rejected that attempt, release capacity and call `TryRegister()` once from a cold-path recovery workflow. Do not retry every frame.

### 3. Add senses to an agent

Add `AIPerceptionComponent` to the AI object. Enable the required senses and configure them in the Inspector. When the same GameObject has `PerceptibleComponent`, its handle is excluded from all built-in sensor queries.

### 4. Consume results

```csharp
using CycloneGames.AIPerception.Runtime;
using UnityEngine;

[RequireComponent(typeof(AIPerceptionComponent))]
public sealed class GuardAwareness : MonoBehaviour
{
    private AIPerceptionComponent _perception;

    private void Awake()
    {
        _perception = GetComponent<AIPerceptionComponent>();
    }

    private void Update()
    {
        SightSensor sight = _perception.SightSensor;
        if (sight == null || !sight.TryGetResult(0, out DetectionResult result))
        {
            return;
        }

        if (result.IsFromMemory)
        {
            Investigate(result.LastKnownPosition);
            return;
        }

        IPerceptible target = PerceptibleRegistry.Instance.Get(result.Target);
        if (target is PerceptibleComponent component)
        {
            Engage(component.gameObject);
        }
    }

    private void Investigate(Unity.Mathematics.float3 position)
    {
        // Feed the last known position to product-specific navigation or behavior code.
    }

    private void Engage(GameObject target)
    {
        // Product-specific decision and authority remain outside perception.
    }
}
```

Results are ordered by distance, then by runtime handle fields. `TryGetResult` avoids a temporary collection.

## Core Concepts

### Runtime handles

`PerceptibleHandle` contains `RegistryId`, `Id`, and `Generation`:

- valid only in the registry that issued it;
- slot reuse changes the generation and invalidates old handles;
- it is a process-local identity — do not persist, save, or send over the network;
- the two-argument constructor creates an unscoped comparison handle that a registry never resolves.

### Perceptible data

| Member | Meaning |
| --- | --- |
| `PerceptibleTypeId` | Stable category used by the exact-type filter |
| `IsDetectable` | Whether the target enters the current snapshot |
| `Position` | Center used by broadphase and distance tests |
| `DetectionRadius` | Non-negative target extent added to sensor range |
| `Loudness` | Non-negative hearing range multiplier |
| `IsSoundSource` | Required for built-in hearing |
| `GetLOSPoint()` | Point used by sight raycasts |
| `Tag` | Consumer metadata; built-in sensors do not filter by tag |

The registry samples these members once per manager update. Non-finite position, LOS point, radius, or loudness excludes the target from that snapshot.

### Stable type IDs

Persistent or networked contracts use explicitly assigned IDs:

```csharp
public static class GamePerceptibleTypes
{
    public const int AlarmEmitter = 1001;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        PerceptibleTypes.RegisterType(AlarmEmitter, "AlarmEmitter");
    }
}
```

`RegisterType(string)` allocates in process registration order — suitable only for non-persistent, non-networked extensions.

### Detection results

`DetectionResult` contains the target handle, sensor-relative distance, last known position, detection time, `Visibility`, sensor type, and `IsFromMemory`.

`Visibility` is sensor-specific strength: sight uses angular strength, hearing uses audibility after optional attenuation, proximity uses distance intensity. Memory linearly decays from the most recent live detection's visibility.

Read results without a temporary collection:

```csharp
SightSensor sensor = perception.SightSensor;
for (int i = 0; sensor != null && i < sensor.DetectedCount; i++)
{
    if (sensor.TryGetResult(i, out DetectionResult result))
    {
        Consume(result);
    }
}
```

Copy methods append to caller-owned storage:

```csharp
var results = new Unity.Collections.NativeList<DetectionResult>(
    sensor.DetectedCount,
    Unity.Collections.Allocator.Temp);
try
{
    sensor.GetDetectionResults(ref results);
    // Consume results here.
}
finally
{
    results.Dispose();
}
```

## Usage Guide

### Sight

Sight uses a Burst cone prefilter followed by optional main-thread `Physics.Raycast`.

| Setting | Default | Description |
| --- | ---: | --- |
| `HalfAngle` | 60 degrees | Half of total 3D FOV; finite range 0–180 |
| `MaxDistance` | 30 | Base range; target radius is added |
| `UpdateInterval` | 0.1 s | Base interval before LOD |
| `UseLineOfSight` | enabled | Enables 3D Physics refinement |
| `ObstacleLayer` | default raycast layers | Layers treated as occluders |
| `MaximumLineOfSightChecksPerUpdate` | 64 | Positive values bound checks; 0 is unlimited |
| `FilterByType` | disabled | Exact `TargetTypeId` match when enabled |
| `MemoryDuration` | 3 s | Zero disables memory |

Exclude target colliders from `ObstacleLayer` unless they intentionally block sight. When the LOS budget is reached, sight commits already refined results, reports `LineOfSightBudgetExceeded`, and advances its cursor.

### Hearing

Hearing accepts only `IsSoundSource == true` targets. Effective range:

```text
(sensor Radius x target Loudness) + target DetectionRadius
```

| Setting | Default | Description |
| --- | ---: | --- |
| `Radius` | 15 | Base continuous-emission range |
| `UpdateInterval` | 0.2 s | Base interval before LOD |
| `UseOcclusion` | enabled | Enables main-thread `Physics.Linecast` |
| `OcclusionLayer` | default raycast layers | Layers treated as sound blockers |
| `OcclusionAttenuation` | 0.5 | Finite multiplier in 0–1 |
| `MaximumOcclusionChecksPerUpdate` | 64 | Positive values bound linecasts; 0 is unlimited |
| `FilterByType` | disabled | Optional exact type filter |
| `MemoryDuration` | 5 s | Zero disables memory |

Hearing does not inspect `AudioSource`, mixer state, clips, volume curves, or one-shot events. `Loudness == 0` and `DetectionRadius == 0` produce no result, including at the sensor origin.

### Proximity

Proximity performs a Burst sphere test without Physics occlusion:

```text
effective range = sensor Radius + target DetectionRadius
```

| Setting | Default | Description |
| --- | ---: | --- |
| `Radius` | 5 | Base proximity range |
| `UpdateInterval` | 0.15 s | Base interval before LOD |
| `FilterByType` | disabled | Optional exact type filter |
| `MemoryDuration` | 2 s | Zero disables memory |

Zero effective radius returns no detection, including at the same position.

### Stimulus memory

Each built-in sensor owns independent bounded memory:

1. A live detection creates or refreshes one entry per handle.
2. A refreshed entry is emitted only as live output, never as a duplicate memory result.
3. A missed but unexpired entry is emitted with `IsFromMemory == true`.
4. Visibility decays linearly with age.
5. Entries are removed at the configured duration or when decayed visibility reaches the internal 0.01 threshold.
6. At capacity, the oldest entry is evicted.

Memory uses `Time.timeAsDouble` and follows Unity game time. A refresh replaces the stored position, timestamp, distance, and visibility with the latest live detection.

### Result iteration patterns

**Index-based (no GC):**

```csharp
SightSensor sensor = perception.SightSensor;
for (int i = 0; sensor != null && i < sensor.DetectedCount; i++)
{
    if (sensor.TryGetResult(i, out DetectionResult result))
    {
        // Consume result.
    }
}
```

**Bulk copy:**

```csharp
var results = new NativeList<DetectionResult>(128, Allocator.Temp);
try
{
    sensor.GetDetectionResults(ref results);
    foreach (DetectionResult result in results) { /* ... */ }
}
finally { results.Dispose(); }
```

**Handle-only (lightweight):**

```csharp
var handles = new NativeList<PerceptibleHandle>(64, Allocator.Temp);
try
{
    sensor.GetDetectedHandles(ref handles);
    foreach (PerceptibleHandle handle in handles)
    {
        IPerceptible target = PerceptibleRegistry.Instance.Get(handle);
        // ...
    }
}
finally { handles.Dispose(); }
```

## Advanced Topics

### Runtime reconfiguration

Sensor behavior can be changed at runtime:

```csharp
SightSensor sight = perception.SightSensor;
if (sight != null)
{
    SightSensorConfig config = sight.Config;
    config.MaxDistance = 45f;
    config.MemoryDuration = 1.5f;
    sight.ApplyConfig(in config);
}
```

`AIPerceptionComponent.ApplyAuthoringConfiguration` is a cold-path rebuild. It drains, unregisters, disposes, and recreates component-owned sensors.

### World capacity

`PerceptionManagerComponent.Maximum Perceptibles` configures the registry hard limit:

- a positive value rejects registrations after exhaustion;
- `0` permits safe-point array growth without a module-level hard limit;
- a limit cannot be lowered below the active count.

### Per-sensor capacity

Each sensor config embeds `PerceptionSensorCapacity`:

| Field | Default | Behavior |
| --- | ---: | --- |
| `InitialCandidateCapacity` | 64 | Initial persistent broadphase-index storage |
| `MaximumCandidates` | 16384 | Hard candidates per query |
| `InitialResultCapacity` | 32 | Initial persistent result storage |
| `MaximumResults` | 1024 | Hard live-plus-memory result count |
| `InitialMemoryCapacity` | 32 | Initial persistent memory storage |
| `MaximumMemoryEntries` | 1024 | Hard remembered-target count |

Capacity failure is explicit:

- `CandidateCapacityExceeded`: rejects and clears the current live candidate query;
- `ResultCapacityExceeded`: prevents additional live or remembered output;
- `LineOfSightBudgetExceeded`: partial sight refinement;
- `OcclusionBudgetExceeded`: partial hearing refinement.

Record these states in development telemetry. Do not treat them as silent normal truncation.

### LOD frequency

`SensorLODLevel.FrequencyMultiplier` scales update frequency:

```text
effective interval = base UpdateInterval / FrequencyMultiplier
```

| Distance from reference | Default multiplier | Interval scale |
| --- | ---: | ---: |
| up to 30 | 1.00 | 1x |
| up to 80 | 0.50 | 2x |
| up to 200 and beyond | 0.25 | 4x |

Distances must be finite, positive, and strictly increasing. Multipliers must be in `(0, 1]`. A null reference or empty levels disables LOD.

### Lifecycle and ownership

| Object | Owner | Shutdown rule |
| --- | --- | --- |
| `PerceptibleComponent` registration | Enabled component | Unregisters in `OnDisable` |
| Built-in component sensors | `AIPerceptionComponent` | Unregisters and disposes on disable/rebuild |
| Directly constructed sensor | Constructing integration | Unregister, then `Dispose` |
| Sensor buffers and jobs | Sensor | Complete pending work before releasing buffers |
| Registry snapshot/native storage | `PerceptibleRegistry` | Dispose during world shutdown/reset |
| Sensor scheduling | The `SensorManager` supplied at construction | Dispose before its registry |

Direct ownership example:

```csharp
public sealed class OwnedSightSense : MonoBehaviour
{
    private SensorManager _manager;
    private SightSensor _sensor;

    private void OnEnable()
    {
        _ = PerceptionManagerComponent.Instance;
        _manager = SensorManager.Instance;
        PerceptibleComponent self = GetComponent<PerceptibleComponent>();
        PerceptibleHandle ignored = self != null
            ? self.Handle
            : PerceptibleHandle.Invalid;

        SightSensorConfig config = SightSensorConfig.Default;
        config.UseLineOfSight = false;
        _sensor = new SightSensor(transform, config, _manager, ignored);
        _manager.Register(_sensor);
    }

    private void OnDisable()
    {
        if (_sensor == null)
        {
            return;
        }

        if (_manager != null && !_manager.IsDisposed)
        {
            _manager.Unregister(_sensor);
        }

        _sensor.Dispose();
        _sensor = null;
        _manager = null;
    }
}
```

All three built-in sensor families expose the same explicit-owner overload: `(Transform, Config, SensorManager owner, PerceptibleHandle ignoredTarget = default)`.

### Thread affinity

`PerceptibleRegistry` and `SensorManager` capture an owner managed-thread ID and reject mutating calls from another thread. Registration, unregistration, capture, configuration, result commit, Physics checks, and disposal belong to that owner thread.

Worker jobs read immutable native snapshots and write sensor-owned arrays. They do not access `Transform`, `GameObject`, managed perceptibles, or Unity Physics.

### Immediate and deferred completion

- Immediate mode completes a sensor inside `UpdateSensor`; results are available when it returns.
- Deferred mode schedules eligible sensors in manager `Update` and completes/commits them in `LateUpdate`.
- Switching deferred mode off first completes pending work.
- Removing or disposing a sensor drains its pending job.

### Extension patterns

`PerceptibleComponent`, `AIPerceptionComponent`, and `PerceptionManagerComponent` are sealed adapters. Extend through composition:

- place product behavior beside `AIPerceptionComponent`;
- implement `IPerceptible` for another data source;
- implement `ISensor` for a product-specific sense;
- register direct sensors with `SensorManager`;
- keep Unity object and Physics access in adapter/refinement boundaries.

An `ISensor` implementation must define ownership, bounded capacity, update status, scheduling, result visibility, disposal, and thread affinity. The manager does not supply those policies automatically.

Custom sensors must not reentrantly register or unregister sensors from `UpdateSensor` or `ProcessJobResults`. Queue collection changes and apply them at an owner-thread safe point outside manager iteration.

### Networking bridge

Runtime handles are never network identities. The optional sibling module `CycloneGames.AIPerception.Networking` integrates with `CycloneGames.Networking` through stable network target identities, protocol versions, authority policy, and observer filtering. See its [documentation](../CycloneGames.AIPerception.Networking/README.md).

## Common Scenarios

### Detecting the closest target

```csharp
SightSensor sight = perception.SightSensor;
if (sight != null && sight.HasDetection)
{
    // Results are pre-sorted by distance.
    if (sight.TryGetResult(0, out DetectionResult closest))
    {
        IPerceptible target = PerceptibleRegistry.Instance.Get(closest.Target);
        if (target is PerceptibleComponent component)
        {
            // Use component.gameObject for navigation, combat, etc.
        }
    }
}
```

### Detecting all targets with a type filter

```csharp
SightSensor sight = perception.SightSensor;
for (int i = 0; sight != null && i < sight.DetectedCount; i++)
{
    if (!sight.TryGetResult(i, out DetectionResult result))
    {
        continue;
    }

    IPerceptible target = PerceptibleRegistry.Instance.Get(result.Target);
    if (target != null && target.PerceptibleTypeId == PerceptibleTypes.Enemy)
    {
        // Process enemy target.
    }
}
```

### Handling memory-only targets

```csharp
for (int i = 0; i < sight.DetectedCount; i++)
{
    if (!sight.TryGetResult(i, out DetectionResult result))
    {
        continue;
    }

    if (result.IsFromMemory)
    {
        // Target is no longer directly detected.
        // Use result.LastKnownPosition and result.Visibility.
        AIInvestigate(result.LastKnownPosition, result.Visibility);
    }
    else
    {
        // Target is currently detected. Full data available.
        IPerceptible target = PerceptibleRegistry.Instance.Get(result.Target);
        AIEngage(target);
    }
}
```

### Dynamically changing sensor configuration

```csharp
void OnEnterAlertMode(AIPerceptionComponent perception)
{
    SightSensor sight = perception.SightSensor;
    if (sight == null) return;

    var config = sight.Config;
    config.MaxDistance = 60f;
    config.UpdateInterval = 0.05f;
    config.HalfAngle = 90f;
    sight.ApplyConfig(in config);
}

void OnExitAlertMode(AIPerceptionComponent perception)
{
    SightSensor sight = perception.SightSensor;
    if (sight == null) return;

    var config = sight.Config;
    config.MaxDistance = 30f;
    config.UpdateInterval = 0.1f;
    config.HalfAngle = 60f;
    sight.ApplyConfig(in config);
}
```

## Performance and Memory

### Cost factors

| Variable | Dominant cost |
| --- | --- |
| N | All registered perceptibles sampled per manager update |
| C | Broadphase candidates processed by one sensor job |
| R | Main-thread sight LOS or hearing occlusion checks |
| M | Memory entries processed by one sensor |
| S | Sensors whose effective interval expires in one frame |

Runtime characteristics:

- managed/native arrays, candidate lists, result lists, memory lists, and lookup storage are reused after reaching required capacity;
- registration growth, first use, capacity growth, configuration rebuild, and spatial dictionary growth are allocation points;
- snapshot capture is O(N) even when captured values do not change;
- query bounds include maximum relevant target radius or loudness before per-target final tests;
- a query spanning more than the grid cell-visit safety threshold falls back to a linear snapshot scan;
- sight and hearing Physics refinement remains on the main thread;
- committed output is sorted for deterministic distance-first consumption.

### Numerical limits

Finite inputs first use a float fast path. If a distance square overflows while the final distance is still representable, the guarded path recomputes in double, then returns a finite float result. When the true offset exceeds `float.MaxValue`, jobs write an internal `-1` sentinel; built-in sensors map it to `CoordinateRangeExceeded` (9), omit that target, and do not issue a Physics query.

Practical Unity Physics precision degrades far below `float.MaxValue`. Large-world products should keep active simulation near a floating origin or partition the world.

### Tuning order

1. Define gameplay latency per sense.
2. Set finite world and sensor capacities from peak distributions plus an explicit margin.
3. Disable unused senses.
4. Increase intervals where stale data is acceptable.
5. Configure and verify an authoritative LOD reference.
6. Bound sight LOS and hearing occlusion checks with positive budgets.
7. Restrict obstacle and occlusion masks.
8. Set memory duration to zero when persistence is unnecessary.
9. Tune cell size from measured density and ranges.
10. Compare immediate and deferred scheduling in the target Player.

### Editor workflow

Custom Inspectors provided for `PerceptibleComponent`, `AIPerceptionComponent`, and `PerceptionManagerComponent` use `SerializedObject`/`SerializedProperty`, support multi-object editing, preserve Undo and Prefab overrides, and lock authoring in Play Mode.

During Play Mode, use `Tools > CycloneGames > AI Perception > Show All Runtime Gizmos (Session)`. The toggle is session-scoped and resets on exit.

### Platform notes

| Target family | Required validation |
| --- | --- |
| Windows, Linux, macOS | Player build, Burst compilation, worker scheduling, Physics masks, shutdown, and long-run memory |
| iOS, Android | IL2CPP/AOT build, device thermal profile, worker count, native memory, pause/resume, and reload |
| WebGL | Backend/package compatibility, actual Job execution model, memory ceiling, latency, and deferred behavior |
| Dedicated Server | Headless lifecycle, 3D Physics, authority-owned LOD, and no camera assumptions |
| Consoles | Licensed SDK build, Burst/Jobs support, memory limits, suspend/resume, and certification constraints |

## Troubleshooting

| Status | Meaning | Action |
| --- | --- | --- |
| `Uninitialized` | No usable sensor state | Check construction and lifecycle |
| `Ready` | Latest query and commit completed | No action |
| `NoTargets` | No live candidates | Check registration, detectability, range, and filter |
| `CandidateCapacityExceeded` | Candidate hard limit rejected the live query | Increase measured budget or reduce density/range |
| `ResultCapacityExceeded` | Result hard limit truncated live or remembered output, including memory-only updates | Increase measured budget or narrow query/memory |
| `LineOfSightBudgetExceeded` | Sight returned a partial refined set | Raise positive budget or reduce raycast work |
| `OcclusionBudgetExceeded` | Hearing returned a partial occlusion-refined set | Raise a measured positive budget or reduce linecasts |
| `CoordinateRangeExceeded` (9) | A target distance cannot be represented by the float result/Physics contract | Omit the target and move the active world closer to a floating origin |
| `InvalidConfiguration` | Transform or finite/range rule is invalid | Correct config and lifetime |
| `Disposed` | Sensor buffers are no longer usable | Stop querying or construct a new sensor |

| Symptom | Check |
| --- | --- |
| Target is never detected | Enabled state, `IsDetectable`, finite data, exact type filter, and range |
| Hearing ignores a target | `IsSoundSource`, loudness, target radius, type filter, and occlusion mask |
| Sight is blocked by its target | Remove target layers from `ObstacleLayer` or choose another LOS point |
| Results appear one phase later | Deferred mode commits in `LateUpdate`; adjust order or use immediate mode |
| Closest target is null while memory exists | Handle no longer resolves; consume `LastKnownPosition` |
| Capacity status appears | Inspect measured peak distribution; do not ignore it |
| LOD has no effect | Assign reference, increasing distances, and multipliers in `(0, 1]` |
| Runtime Inspector is locked | Use sensor APIs or a cold-path rebuild |
| Global gizmos disappear | Session state resets on Play Mode exit |
