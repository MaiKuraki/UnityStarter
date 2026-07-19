# CycloneGames.AIPerception

English | [简体中文](./README.SCH.md)

AI perception components for sight, hearing, proximity, stimulus memory, distance-based update frequency, and Burst/Jobs query processing.

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

AIPerception answers one question: given a set of sensors and registered perceptible objects in the world, what does this AI agent currently detect? Three sensor types — sight (cone + LOS), hearing (sphere + occlusion), and proximity (radius trigger) — query a 3D spatial grid using Burst-compiled jobs. Results persist in stimulus memory with configurable decay, and distance-based LOD reduces update frequency for distant agents.

Use this module when AI agents need awareness of their surroundings through configurable senses, and when hundreds or thousands of agents require batched job scheduling.

### Key Features

- **Three sensor types**: Sight (cone FOV + LOS raycast), Hearing (sphere + wall occlusion), Proximity (simple radius trigger).
- **Burst/Jobs** `IJobParallelFor` pre-filtering with SIMD acceleration.
- **Stimulus memory** with configurable duration and linear visibility decay.
- **LOD system** with distance-based update frequency scaling.
- **3D spatial grid** for O(k) range queries instead of O(n) full scans.
- **Extensible type system** with integer-based classification.
- **Deferred mode** for batched job completion in `LateUpdate`.

### Dependencies

- `com.unity.collections` (2.1+)
- `com.unity.burst` (1.8+)
- `com.unity.mathematics` (1.3+)

## Architecture

```
PerceptibleComponent ──Register──> PerceptibleRegistry (generational handles)
                                         │
                                    RebuildData (once/frame)
                                         │
                                   SpatialGrid (3D cell sort)
                                         │
AIPerceptionComponent ──creates──> SightSensor ──> SightConeQueryJob [Burst]
                            │     HearingSensor ──> SphereQueryJob   [Burst]
                            │     ProximitySensor ──> ProximityQueryJob [Burst]
                            │           │
                            │     ProcessJobResults
                            │           │
                            │     MergeMemory (stimulus persistence)
                            │           │
                            └── Query API <── DetectionResult[] (live + memory)
```

### Key Types

| Type | Role |
| --- | --- |
| `PerceptibleHandle` | Value-type handle (Id + Generation) — no GC, no dangling refs |
| `PerceptibleData` | Blittable struct for Burst job input |
| `DetectionResult` | Sensor output: target, distance, position, visibility, `IsFromMemory` |
| `StimulusMemoryEntry` | Persists after target leaves range; visibility decays linearly |
| `SensorLODLevel` | Distance threshold + frequency multiplier |

## Quick Start

**Step 1:** Mark detectable objects by adding `PerceptibleComponent`:

```csharp
var perceptible = gameObject.AddComponent<PerceptibleComponent>();
perceptible.SetTypeId(PerceptibleTypes.Enemy);
```

**Step 2:** Add perception to AI agents with `AIPerceptionComponent`. Configure in Inspector: sight sensor (cone FOV, max distance, LOS), hearing sensor (radius, occlusion), proximity sensor (trigger radius). Each sensor has independent memory duration.

**Step 3:** Query results:

```csharp
using CycloneGames.AIPerception.Runtime;

public class AIBrain : MonoBehaviour
{
    private AIPerceptionComponent _perception;

    void Start() => _perception = GetComponent<AIPerceptionComponent>();

    void Update()
    {
        if (_perception.HasSightDetection)
        {
            var target = _perception.GetClosestSightTarget();
            Debug.Log($"See: {((PerceptibleComponent)target).name}");
        }

        if (_perception.HasProximityDetection)
        {
            var nearest = _perception.GetClosestProximityTarget();
            Debug.Log($"Nearby at {nearest.Position}");
        }
    }
}
```

## Core Concepts

### PerceptibleComponent

Makes a GameObject detectable. `[DisallowMultipleComponent]`, implements `IPerceptible`.

| Field | Type | Default | Description |
| --- | --- | --- | --- |
| Type ID | `int` | 0 | Perceptible category |
| Tag | `string` | "" | Optional filter tag |
| Detection Radius | `float` | 1 | Proximity trigger size |
| Is Detectable | `bool` | true | Toggle detection |
| LOS Point | `Transform` | null | Line-of-sight origin (falls back to transform) |
| Is Sound Source | `bool` | false | Mark as audio emitter |
| Loudness | `float` | 1 | Sound volume (0–10) |

```csharp
var handle = perceptible.Handle;    // generational handle
var pos = perceptible.Position;     // float3 world position
perceptible.SetLoudness(0.5f);      // dynamic loudness
```

### AIPerceptionComponent

Sensor host for AI agents. `[DisallowMultipleComponent]`.

| Field | Type | Default |
| --- | --- | --- |
| Enable Sight | `bool` | true |
| Sight Config | `SightSensorConfig` | default |
| Enable Hearing | `bool` | false |
| Hearing Config | `HearingSensorConfig` | default |
| Enable Proximity | `bool` | false |
| Proximity Config | `ProximitySensorConfig` | default |

```csharp
bool hasSight = perception.HasSightDetection;
int sightCount = perception.SightDetectedCount;
IPerceptible target = perception.GetClosestSightTarget();
SightSensor sight = perception.SightSensor;
var result = sight.GetResult(0);
```

### PerceptionManagerComponent

Global system driver. Auto-created as `[PerceptionManager]` in `DontDestroyOnLoad`.

| Field | Type | Default | Description |
| --- | --- | --- | --- |
| Deferred Job Completion | `bool` | false | Batch jobs in LateUpdate |
| LOD Reference | `Transform` | null | Distance reference (camera/player) |
| LOD Levels | `SensorLODLevel[]` | 3 levels | Distance → frequency multiplier |

```csharp
PerceptionManagerComponent.Instance.UseDeferredJobCompletion = true;
```

## Usage Guide

### Sight Sensor

Cone-shaped visual detection with Burst pre-filter and main-thread LOS raycast.

| Property | Range | Default | Description |
| --- | --- | --- | --- |
| Half Angle | 0–180 deg | 60 | Half of total FOV |
| Max Distance | 0–200 m | 30 | Detection range |
| Update Interval | 0–5 s | 0.1 | Seconds between updates |
| Obstacle Layer | LayerMask | Default | Layers blocking LOS |
| Use Line of Sight | bool | true | Raycast visibility check |
| Filter by Type | bool | false | Only specific Type ID |
| Memory Duration | 0–60 s | 3 | Persist after leaving FOV |

### Hearing Sensor

Sphere-based audio detection with wall occlusion attenuation.

| Property | Range | Default | Description |
| --- | --- | --- | --- |
| Radius | 0–100 m | 15 | Detection sphere |
| Update Interval | 0–5 s | 0.2 | Seconds between updates |
| Use Occlusion | bool | true | Wall attenuation |
| Occlusion Layer | LayerMask | Default | Layers blocking sound |
| Occlusion Attenuation | 0–1 | 0.5 | Volume through walls |
| Memory Duration | 0–60 s | 5 | Persist after sound fades |

### Proximity Sensor

Simple spherical trigger — no LOS, no occlusion.

| Property | Range | Default | Description |
| --- | --- | --- | --- |
| Radius | 0–50 m | 5 | Trigger sphere |
| Update Interval | 0–5 s | 0.15 | Seconds between updates |
| Memory Duration | 0–60 s | 2 | Persist after leaving range |

### Stimulus Memory

When a target leaves sensor range, it is not immediately forgotten. Entries persist with linearly decaying visibility.

```
Detection → MemoryEntry (PeakVisibility, LastDetectedTime)
              │
              ├── Re-detected → refresh time, update peak
              │
              └── Not detected → age increases
                      │
                      ├── age < MemoryDuration → emit as IsFromMemory result
                      └── age >= MemoryDuration → RemoveAtSwapBack
```

```csharp
sightConfig.MemoryDuration = 3f;    // Remember what you saw for 3s
hearingConfig.MemoryDuration = 5f;  // Remember sounds for 5s

for (int i = 0; i < perception.SightSensor.DetectedCount; i++)
{
    var r = perception.SightSensor.GetResult(i);
    if (r.IsFromMemory)
        Debug.Log($"Remembered: {r.LastKnownPosition}, vis={r.Visibility:F2}");
}
```

Long durations (5–10s) create hunting AI; short durations (1–2s) create reactive AI.

### Type System

Integer-based extensible classification:

| ID | Constant | Description |
| --- | --- | --- |
| 0 | `PerceptibleTypes.Default` | Unspecified |
| 1 | `PerceptibleTypes.Player` | Player characters |
| 2 | `PerceptibleTypes.Enemy` | Hostile NPCs |
| 3 | `PerceptibleTypes.Ally` | Friendly NPCs |
| 4 | `PerceptibleTypes.Neutral` | Neutral entities |
| 5 | `PerceptibleTypes.Interactable` | Interactive objects |
| 6 | `PerceptibleTypes.SoundSource` | Audio emitters |

```csharp
public static class MyTypes
{
    public static int Treasure;
    public static int Trap;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        Treasure = PerceptibleTypes.RegisterType("Treasure");
        Trap = PerceptibleTypes.RegisterType("Trap");
    }
}

sightConfig.FilterByType = true;
sightConfig.TargetTypeId = PerceptibleTypes.Enemy;
```

## Advanced Topics

### Spatial Partitioning

All sensors use a 3D uniform grid (20m cell size default). `RebuildData` sorts `PerceptibleData[]` by cell key; queries iterate overlapping cells and copy contiguous slices. The grid is fully 3D (X/Y/Z), handling multi-story buildings and flying units.

### Job Scheduling

**Immediate mode** (default): Jobs complete within `Update()`, results available immediately.

**Deferred mode**: Jobs are batched and completed in `LateUpdate()`.

```
Update():     schedule jobs → previous frame results visible
LateUpdate(): complete all → atomic swap → new results visible
```

```csharp
PerceptionManagerComponent.Instance.UseDeferredJobCompletion = true;
```

### LOD System

```
Sensor position → distance to LOD Reference → LOD multiplier → effective interval

Default levels:
  0–30m:   1.00x (full frequency)
  30–80m:  0.50x (half frequency)
  80–200m: 0.10x (minimal frequency)
```

```csharp
SensorManager.Instance.ConfigureLOD(
    Camera.main.transform,
    new[] {
        new SensorLODLevel { Distance = 30f, FrequencyMultiplier = 1.0f },
        new SensorLODLevel { Distance = 100f, FrequencyMultiplier = 0.25f },
    }
);
```

### Extending the System

```csharp
// Custom perceptible
public class WeaponPerceptible : PerceptibleComponent
{
    [SerializeField] private int _dangerLevel;
    public int DangerLevel => _dangerLevel;
}

// Custom AI perception
public class AdvancedPerception : AIPerceptionComponent
{
    [SerializeField] private float _alertLevel;

    protected override void Update()
    {
        base.Update();
        _alertLevel = HasSightDetection
            ? Mathf.Min(_alertLevel + Time.deltaTime, 1f)
            : Mathf.Max(_alertLevel - Time.deltaTime * 0.5f, 0f);
    }
}
```

Implement `ISensor` and `IDisposable`, register via `SensorManager.Instance.Register()` for custom sensors.

### Editor Tools

- `Tools > CycloneGames > AI Perception` — global debug overlay toggle and gizmo commands.
- Custom Inspectors for `AIPerceptionComponent`, `PerceptibleComponent`, and `PerceptionManagerComponent`.
- Runtime debug overlay: in-game GUI showing live detections and memory entries with live markers (`>`, `~`, `*`) and memory markers (`<`, `~M`, `.M` with `[mem]` suffix).
- "Always Show Gizmos" — displays sensor wireframes for all AIs without individual selection.

## Common Scenarios

### Guard with Sight and Hearing

```csharp
perception.EnableSight = true;
perception.EnableHearing = true;
perception.SightConfig.MaxDistance = 20f;
perception.SightConfig.MemoryDuration = 3f;
perception.HearingConfig.Radius = 10f;
perception.HearingConfig.MemoryDuration = 5f;

void Update()
{
    if (HasSightDetection)
        Chase(GetClosestSightTarget());
    else if (perception.SightSensor.MemoryCount > 0)
        Investigate(perception.SightSensor.GetResult(0).LastKnownPosition);
}
```

### Proximity Warning System

```csharp
perception.EnableProximity = true;
perception.ProximityConfig.Radius = 3f;
perception.ProximityConfig.MemoryDuration = 0f; // no memory

void Update()
{
    if (HasProximityDetection)
        OnNearbyThreat(GetClosestProximityTarget());
}
```

### Type-Filtered Enemy Detection

```csharp
perception.SightConfig.FilterByType = true;
perception.SightConfig.TargetTypeId = PerceptibleTypes.Enemy;
perception.SightConfig.UseLineOfSight = true;
```

## Performance and Memory

### Tuning Checklist

1. **Update Interval**: Choose from gameplay response requirements, then measure.
2. **Type Filtering**: Only scan relevant types.
3. **Disable LOS**: When walls aren't relevant, skip raycasts.
4. **Enable LOD**: Reduce distant sensor frequency when stale results are acceptable.
5. **Deferred Mode**: Compare batched completion with immediate result visibility.
6. **Memory Duration = 0**: Disable memory for sensors that don't need it.

### Capacity

```csharp
PerceptibleRegistry.Instance.SetMaxCapacity(32768); // 0 = unlimited growth
```

Registry capacity is a memory and failure-policy setting. A finite maximum rejects registrations after exhaustion; 0 allows growth and requires product-owned memory monitoring.

### PerceptibleRegistry API

```csharp
var r = PerceptibleRegistry.Instance;
PerceptibleHandle h = r.Register(perceptible);  // O(1)
r.Unregister(h);                                  // O(1)
IPerceptible p = r.Get(h);                       // O(1)
bool valid = r.IsValid(h);                       // O(1)
r.SetMaxCapacity(16384);
```

### SensorManager API

```csharp
var m = SensorManager.Instance;
m.Register(sensor);
m.Unregister(sensor);
m.ConfigureLOD(referenceTransform, lodLevels);
```

### Platform

The Runtime assembly uses Unity Burst, Collections, Mathematics, and Jobs. Verify package support, worker availability, Burst compilation, and memory limits in a Player build for every target. Do not assume WebGL worker behavior or console Burst support from Editor execution.

## Troubleshooting

| Symptom | Likely cause | Resolution |
| --- | --- | --- |
| Detection not working | Perceptible not enabled or sensor off | Check `Is Detectable` and sensor toggle in Inspector; verify range, FOV, LOS |
| "LOS Blocked" when target is visible | Obstacle layer includes target's layer | Restrict mask to environment layers (walls, floors) |
| Memory entries not appearing | `MemoryDuration` is 0 | Set `MemoryDuration > 0` on the sensor config |
| Inspector labels blank | Editor font issue | Use default Editor font |
| Registry capacity exceeded | Max capacity reached | Call `SetMaxCapacity(32768)` at startup or set to 0 for unlimited |
| High CPU on many agents | No LOD configured | Enable LOD with distance thresholds on PerceptionManager |
| Burst jobs not compiling | Missing Burst/Collections/Mathematics | Install required packages and verify Burst is enabled |

## Validation

```text
CycloneGames.AIPerception.Tests.Editor            (EditMode)
CycloneGames.AIPerception.Networking.Tests.Editor (EditMode)
```

Compile without errors, test Play Mode, and run Player builds on each target platform. Verify Burst compilation, memory limits, and IL2CPP/AOT compatibility.
