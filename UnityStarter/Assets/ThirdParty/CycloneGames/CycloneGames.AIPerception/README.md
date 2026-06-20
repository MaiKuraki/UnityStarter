# CycloneGames.AIPerception

English | [简体中文](./README.SCH.md)

Production-grade AI perception system with Burst/Jobs optimization, 0GC design, 3D spatial partitioning, stimulus memory, LOD, and cross-platform support.

---

## Table of Contents

1. [Features](#features)
2. [Installation](#installation)
3. [Quick Start](#quick-start)
4. [Core Concepts](#core-concepts)
5. [Component Reference](#component-reference)
6. [Sensor Reference](#sensor-reference)
    - [Sight Sensor](#sight-sensor)
    - [Hearing Sensor](#hearing-sensor)
    - [Proximity Sensor](#proximity-sensor)
7. [Stimulus Memory](#stimulus-memory)
8. [LOD System](#lod-system)
9. [Spatial Partitioning](#spatial-partitioning)
10. [Type System](#type-system)
11. [Job Scheduling](#job-scheduling)
12. [Editor Tools](#editor-tools)
13. [Runtime Debug Tools](#runtime-debug-tools)
14. [Extending the System](#extending-the-system)
15. [Performance & Scaling](#performance--scaling)
16. [Platform Support](#platform-support)
17. [API Reference](#api-reference)
18. [Testing](#testing)
19. [Troubleshooting](#troubleshooting)

---

## Features

| Category | Capability |
|----------|------------|
| **Sensors** | Sight (cone + LOS), Hearing (sphere + occlusion), Proximity (radius trigger) |
| **0GC Runtime** | `NativeList`/`NativeArray` + generational handles — zero heap allocation during gameplay |
| **Burst/Jobs** | `IJobParallelFor` pre-filtering with SIMD acceleration |
| **Stimulus Memory** | Targets remembered after leaving sensor range with configurable decay |
| **LOD** | Distance-based update frequency scaling with 3 preset levels |
| **Spatial Index** | 3D grid-based spatial partitioning for O(k) range queries |
| **Deferred Mode** | Batched job completion in `LateUpdate` for 100+ sensor scenarios |
| **Editor Tools** | Custom Inspectors, global gizmo toggle, LOD preview, runtime stats |
| **Debug Overlay** | In-game GUI windows showing live detections and memory entries |
| **Type System** | Extensible integer-based perceptible classification |
| **Cross-Platform** | WebGL fallback, mobile-optimized, console-ready |
| **Capacity Control** | Configurable registry capacity with auto-grow and warning thresholds |

---

## Optional Networking

Network replication lives in the optional sibling package `CycloneGames.AIPerception.Networking`. The base perception package does not depend on `CycloneGames.Networking`; projects that need multiplayer perception can add the networking package to get stable message IDs, detection events, memory snapshots, authority helpers, and interest-filtered observer selection.

---

## Installation

1. Copy `CycloneGames.AIPerception` into your project's `Assets` directory.
2. Required Unity packages:
   - `com.unity.collections` (2.1+)
   - `com.unity.burst` (1.8+)
   - `com.unity.mathematics` (1.3+)

---

## Quick Start

### Step 1: Mark detectable objects

Add `PerceptibleComponent` to any GameObject that should be detectable:

```
Component > CycloneGames > AI > Perceptible
```

```csharp
var perceptible = gameObject.AddComponent<PerceptibleComponent>();
perceptible.SetTypeId(PerceptibleTypes.Enemy);
```

### Step 2: Add perception to AI agents

Add `AIPerceptionComponent` to AI characters:

```
Component > CycloneGames > AI > AI Perception
```

Configure in Inspector:
- **Sight Sensor**: cone FOV, max distance, LOS
- **Hearing Sensor**: radius, occlusion
- **Proximity Sensor**: trigger radius
- Each sensor has independent **Memory Duration**

### Step 3: Query results

```csharp
using CycloneGames.AIPerception;

public class AIBrain : MonoBehaviour
{
    private AIPerceptionComponent _perception;

    void Start() => _perception = GetComponent<AIPerceptionComponent>();

    void Update()
    {
        // Sight — live detection
        if (_perception.HasSightDetection)
        {
            var target = _perception.GetClosestSightTarget();
            Debug.Log($"See: {((PerceptibleComponent)target).name}");
        }

        // Proximity — nearby alert
        if (_perception.HasProximityDetection)
        {
            var nearest = _perception.GetClosestProximityTarget();
            Debug.Log($"Someone nearby at {nearest.Position}");
        }
    }
}
```

---

## Core Concepts

### Architecture

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
|------|------|
| `PerceptibleHandle` | Value-type handle (Id + Generation) — no GC, no dangling refs |
| `PerceptibleData` | Blittable struct for Burst job input |
| `DetectionResult` | Sensor output: target, distance, position, visibility, `IsFromMemory` |
| `StimulusMemoryEntry` | Persists after target leaves range; visibility decays linearly |
| `SensorLODLevel` | Distance threshold + frequency multiplier |

---

## Component Reference

### PerceptibleComponent

Makes a GameObject detectable. `[DisallowMultipleComponent]`, implements `IPerceptible`.

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| Type ID | `int` | 0 | Perceptible category |
| Tag | `string` | "" | Optional filter tag |
| Detection Radius | `float` | 1 | Proximity trigger size |
| Is Detectable | `bool` | true | Toggle detection |
| LOS Point | `Transform` | null | Line-of-sight origin (falls back to transform) |
| Is Sound Source | `bool` | false | Mark as audio emitter |
| Loudness | `float` | 1 | Sound volume (0–10) |

**Runtime API:**

```csharp
bool detected = perceptible.IsDetectable;          // enabled AND active
var handle    = perceptible.Handle;                 // generational handle
var pos       = perceptible.Position;               // float3 world position
var detectors = perceptible.GetDetectors();         // who is detecting us
perceptible.SetLoudness(0.5f);                      // dynamic loudness
perceptible.ShowDebugOverlay = true;                // toggle debug window
```

### AIPerceptionComponent

Sensor host for AI agents. `[DisallowMultipleComponent]`.

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| Enable Sight | `bool` | true | Sight sensor |
| Sight Config | `SightSensorConfig` | default | Cone, range, LOS, memory |
| Enable Hearing | `bool` | false | Hearing sensor |
| Hearing Config | `HearingSensorConfig` | default | Radius, occlusion, memory |
| Enable Proximity | `bool` | false | Proximity sensor |
| Proximity Config | `ProximitySensorConfig` | default | Radius, memory |
| Show Debug Overlay | `bool` | false | Runtime GUI window |

**Runtime API:**

```csharp
// Detection queries
bool hasSight     = perception.HasSightDetection;
bool hasProximity = perception.HasProximityDetection;
int sightCount    = perception.SightDetectedCount;
int memCount      = perception.SightSensor.MemoryCount;

// Get closest
IPerceptible target = perception.GetClosestSightTarget();
IPerceptible nearest = perception.GetClosestProximityTarget();

// Direct sensor access
SightSensor sight = perception.SightSensor;
var result = sight.GetResult(0);  // DetectionResult at index
```

### PerceptionManagerComponent

Global system driver. Auto-created as `[PerceptionManager]` in `DontDestroyOnLoad`.

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| Deferred Job Completion | `bool` | false | Batch jobs in LateUpdate |
| LOD Reference | `Transform` | null | Distance reference (camera/player) |
| LOD Levels | `SensorLODLevel[]` | 3 levels | Distance → frequency multiplier |

```csharp
// Access singleton
var mgr = PerceptionManagerComponent.Instance;
mgr.UseDeferredJobCompletion = true;
```

---

## Sensor Reference

### Sight Sensor

Cone-shaped visual detection with Burst pre-filter and main-thread LOS raycast.

| Property | Range | Default | Description |
|----------|-------|---------|-------------|
| Half Angle | 0–180 deg | 60 | Half of total FOV |
| Max Distance | 0–200 m | 30 | Detection range |
| Update Interval | 0–5 s | 0.1 | Seconds between updates |
| Obstacle Layer | LayerMask | Default | Layers blocking LOS |
| Use Line of Sight | bool | true | Raycast visibility check |
| Filter by Type | bool | false | Only specific Type ID |
| Target Type ID | int | 0 | Type to filter |
| Memory Duration | 0–60 s | 3 | Persist after leaving FOV |

> [!TIP]
> Increase Update Interval to 0.2s for distant enemies. Disable LOS for performance when walls aren't relevant.

### Hearing Sensor

Sphere-based audio detection with wall occlusion attenuation.

| Property | Range | Default | Description |
|----------|-------|---------|-------------|
| Radius | 0–100 m | 15 | Detection sphere |
| Update Interval | 0–5 s | 0.2 | Seconds between updates |
| Use Occlusion | bool | true | Wall attenuation |
| Occlusion Layer | LayerMask | Default | Layers blocking sound |
| Occlusion Attenuation | 0–1 | 0.5 | Volume through walls |
| Filter by Type | bool | false | Only specific Type ID |
| Target Type ID | int | 6 | Type to filter |
| Memory Duration | 0–60 s | 5 | Persist after sound fades |

### Proximity Sensor

Simple spherical trigger detection — no LOS, no occlusion. Ideal for melee range, danger zones, or personal space.

| Property | Range | Default | Description |
|----------|-------|---------|-------------|
| Radius | 0–50 m | 5 | Trigger sphere |
| Update Interval | 0–5 s | 0.15 | Seconds between updates |
| Filter by Type | bool | false | Only specific Type ID |
| Target Type ID | int | 0 | Type to filter |
| Memory Duration | 0–60 s | 2 | Persist after leaving range |

---

## Stimulus Memory

When a target leaves sensor range, it is not immediately forgotten. The memory system persists entries with linearly decaying visibility.

```
Detection → MemoryEntry (PeakVisibility, LastDetectedTime)
              │
              ├── Re-detected → refresh LastDetectedTime, update PeakVisibility
              │
              └── Not detected → age increases
                      │
                      ├── age < MemoryDuration → emit as IsFromMemory result
                      └── age >= MemoryDuration → RemoveAtSwapBack
```

**Configuration per sensor:**

```csharp
sightConfig.MemoryDuration = 3f;    // Remember what you saw for 3s
hearingConfig.MemoryDuration = 5f;  // Remember sounds for 5s
proximityConfig.MemoryDuration = 0f; // No memory for proximity
```

**Query memory:**

```csharp
int remembered = perception.SightSensor.MemoryCount;
var allResults = new List<DetectionResult>();
for (int i = 0; i < perception.SightSensor.DetectedCount; i++)
{
    var r = perception.SightSensor.GetResult(i);
    if (r.IsFromMemory)
        Debug.Log($"Remembered target at {r.LastKnownPosition}, vis={r.Visibility:F2}");
}
```

> [!TIP]
> Long memory durations (5–10s) create "hunting" AI that searches last-known positions. Short durations (1–2s) create reactive AI that only chases visible targets.

---

## LOD System

Distance-based update frequency scaling reduces CPU load for distant AI.

```
Sensor position → distance to LOD Reference → LOD multiplier → effective interval

Default levels:
  0–30m:   1.00x (full frequency)
  30–80m:  0.50x (half frequency)
  80–200m: 0.10x (minimal frequency)
```

**Configuration:**

```csharp
// In Inspector: PerceptionManagerComponent > LOD
// Set Reference to Camera.main or Player transform
// Configure levels as needed
```

Or via code:

```csharp
SensorManager.Instance.ConfigureLOD(
    Camera.main.transform,
    new[] {
        new SensorLODLevel { Distance = 30f, FrequencyMultiplier = 1.0f },
        new SensorLODLevel { Distance = 100f, FrequencyMultiplier = 0.25f },
    }
);
```

**Editor Preview:** The PerceptionManager Inspector shows a color-coded distance band bar visualizing LOD levels at a glance.

**SceneView Gizmo:** Select the PerceptionManager to see concentric LOD rings with frequency labels.

---

## Spatial Partitioning

All sensors use a 3D uniform grid spatial index to avoid O(n) full-table scans.

```
World → Grid cells (20m default)
         │
    RebuildData: sort PerceptibleData[] by cell key
    Query:       iterate overlapping cells → contiguous slice copy
```

| Data scale | Without grid | With grid (20m cells, 50m range) |
|------------|-------------|----------------------------------|
| 1,000 perceptibles | 1,000/job | ~80/job (12x) |
| 10,000 perceptibles | 10,000/job | ~200/job (50x) |
| 100,000 perceptibles | 100,000/job | ~500/job (200x) |

The grid is fully 3D (X/Y/Z), correctly handling multi-story buildings and flying units.

**Cell size:** 20m default. Adjust via `PerceptibleRegistry`:

```csharp
// Not exposed in Inspector by default; use reflection or code
```

---

## Type System

Integer-based extensible classification with runtime registration.

| ID | Constant | Description |
|----|----------|-------------|
| 0 | `PerceptibleTypes.Default` | Unspecified |
| 1 | `PerceptibleTypes.Player` | Player characters |
| 2 | `PerceptibleTypes.Enemy` | Hostile NPCs |
| 3 | `PerceptibleTypes.Ally` | Friendly NPCs |
| 4 | `PerceptibleTypes.Neutral` | Neutral entities |
| 5 | `PerceptibleTypes.Interactable` | Interactive objects |
| 6 | `PerceptibleTypes.SoundSource` | Audio emitters |

**Custom types:**

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
```

**Type filtering:**

```csharp
sightConfig.FilterByType = true;
sightConfig.TargetTypeId = PerceptibleTypes.Enemy; // Only detect enemies
```

---

## Job Scheduling

### Immediate Mode (default)

Jobs complete within `Update()`. Results available immediately. Best for development.

### Deferred Mode

Jobs batched and completed in `LateUpdate()`. Better CPU utilization for 100+ sensors.

```
Update():     schedule jobs → previous frame results visible
LateUpdate(): complete all → atomic swap → new results visible
```

```csharp
PerceptionManagerComponent.Instance.UseDeferredJobCompletion = true;
```

---

## Editor Tools

### Custom Inspectors

| Component | Inspector Features |
|-----------|-------------------|
| AIPerceptionComponent | Color-coded foldouts, toggle per sensor, runtime stats (S/H/P count), debug overlay button, Memory Duration slider |
| PerceptibleComponent | Type/Detection/Sound sections, LOS Point info, runtime detector list |
| PerceptionManagerComponent | Performance toggle, LOD reference picker, LOD preview bar, runtime sensor count |

### Menu Commands

```
Tools > CycloneGames > AI Perception
  ├── Show All Debug Overlays   — Enable all runtime debug windows
  ├── Hide All Debug Overlays   — Disable all
  └── Always Show Gizmos        — Draw sensor ranges in SceneView for ALL AIs
```

When "Always Show Gizmos" is enabled, every AI in the scene displays its sensor wireframes without needing individual selection.

---

## Runtime Debug Tools

### Debug Overlay

Each AI and Perceptible can show an in-game GUI window. Toggle via Inspector button or globally via menu.

```
+---------------------------+
| AI Perception - Enemy     |
| SIGHT                     |
|   Enabled: True           |
|   Detected: 2             |
|   > Player (Player)       |    <- live detection
|     Dist: 5.2m Vis: 87%   |
|   < Enemy_02 (Enemy) [mem]|    <- stimulus memory
|     Dist: 12.1m Vis: 45%  |
| HEARING                   |
|   ~ (No sounds)           |
| PROXIMITY                 |
|   * Player (Player)       |
|     Dist: 2.1m Prox: 95%  |
+---------------------------+
```

Live entries: `>`, `~`, `*`. Memory entries: `<`, `~M`, `.M` with `[mem]` suffix.

### Gizmo Visualization

| Mode | What you see |
|------|-------------|
| **Selected** | Full detail: cone arcs, sphere discs, detection lines, dashed memory lines |
| **Always Show Gizmos** | Simplified wireframes for all AIs at once |

### LOD Gizmo

Select the PerceptionManager to see concentric distance rings with frequency labels (x1.00, x0.50, x0.10).

---

## Extending the System

### Custom Perceptible

```csharp
public class WeaponPerceptible : PerceptibleComponent
{
    [SerializeField] private int _dangerLevel;
    public int DangerLevel => _dangerLevel;
}
```

### Custom AI Perception

```csharp
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

### Custom Sensor

Implement `ISensor` and `IDisposable`, register with `SensorManager.Instance.Register()`.

---

## Performance & Scaling

### Optimization Checklist

1. **Update Interval**: 0.1–0.2s is sufficient for most cases
2. **Type Filtering**: Only scan relevant types
3. **Disable LOS**: When walls aren't relevant, skip raycasts
4. **Enable LOD**: Reduces distant sensor frequency 2–10x
5. **Deferred Mode**: For 100+ concurrent sensors
6. **Memory Duration = 0**: Disable memory for sensors that don't need it

### Scale Limits

| Scenario | Capacity | Recommendation |
|----------|----------|---------------|
| < 100 sensors, < 1K targets | Default | No tuning needed |
| 100–500 sensors, 1K–10K targets | Default | Enable Deferred + LOD |
| 500+ sensors, 10K+ targets | `SetMaxCapacity(32768)` | Enable all optimizations |
| Unlimited | `SetMaxCapacity(0)` | Monitor memory usage |

```csharp
// Increase registry capacity for massive scenes
PerceptibleRegistry.Instance.SetMaxCapacity(32768);
```

---

## Platform Support

| Platform | Strategy | Performance |
|----------|----------|-------------|
| Windows / Mac / Linux | Full Burst SIMD | Optimal |
| Android / iOS | ARM NEON | Excellent |
| WebGL | Main-thread fallback | Good |
| Console | Platform Burst | Excellent |

---

## API Reference

### PerceptibleRegistry

```csharp
var r = PerceptibleRegistry.Instance;

PerceptibleHandle h = r.Register(perceptible);  // O(1)
r.Unregister(h);                                  // O(1)
IPerceptible p = r.Get(h);                       // O(1)
bool valid = r.IsValid(h);                       // O(1)
r.MarkDirty();                                    // force rebuild
r.SetMaxCapacity(16384);                          // configurable limit (0 = unlimited)
int count = r.Count;
int dataCount = r.GetDataCount();
```

### SensorManager

```csharp
var m = SensorManager.Instance;

m.Register(sensor);
m.Unregister(sensor);
m.ConfigureLOD(referenceTransform, lodLevels);
m.UseDeferredJobCompletion = true;
```

### AIPerceptionComponent

```csharp
// Detection
bool hasAny = perception.HasAnyDetection;
int sightCount = perception.SightDetectedCount;
int hearingCount = perception.HearingDetectedCount;
int proximityCount = perception.ProximityDetectedCount;

// Closest target
IPerceptible t = perception.GetClosestSightTarget();
IPerceptible t = perception.GetClosestHearingTarget();
IPerceptible t = perception.GetClosestProximityTarget();

// Sensors
SightSensor s = perception.SightSensor;
int memCount = s.MemoryCount;
DetectionResult r = s.GetResult(index);

// Debug
perception.ShowDebugOverlay = true;
```

---

## Testing

EditMode coverage is provided by `CycloneGames.AIPerception.Tests.Editor`.

Run these tests after changing registry handle lifecycle, perceptible data export, spatial filtering, sensor-facing data contracts, or Burst job inputs:

1. Open Unity and let the project reimport assemblies.
2. Confirm the Console has no compile errors.
3. Open Test Runner in EditMode.
4. Run `CycloneGames.AIPerception.Tests.Editor`.

---

## Troubleshooting

### Detection not working

1. PerceptibleComponent: enabled AND `Is Detectable` checked
2. AIPerceptionComponent: sensor toggle ON
3. Target is within range (check MaxDistance / Radius)
4. Target is in FOV (for Sight)
5. No obstacle blocking LOS (or disable LOS)
6. Check FilterByType — ensure TypeId matches

### "LOS Blocked" when target is visible

Obstacle Layer probably includes the target's layer. Only add environment layers (walls, floor) to Obstacle Layer.

### Memory entries not appearing

Ensure `MemoryDuration > 0` on the sensor config. Check `MemoryCount` on the sensor.

### Inspector shows blank labels

The module uses plain text labels. If you see blank fields, ensure the Unity Editor is using a compatible font (the default Editor font works correctly).

### Registry capacity exceeded

```
[AIPerception] Registry capacity exhausted (16384). Increase via SetMaxCapacity().
```

Call `PerceptibleRegistry.Instance.SetMaxCapacity(32768)` at startup, or set to 0 for unlimited growth.
