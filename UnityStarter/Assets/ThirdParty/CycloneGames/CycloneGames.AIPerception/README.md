# CycloneGames.AIPerception

Perception for Unity agents: register what can be noticed, evaluate it for many observers at once in Burst jobs, and read the result without allocating.

- [What it is for](#what-it-is-for)
- [How it works](#how-it-works)
- [Getting started](#getting-started)
- [Tutorials](#tutorials)
- [Parameters and their cost](#parameters-and-their-cost)
- [Performance and memory](#performance-and-memory)
- [Debugging](#debugging)
- [API reference](#api-reference)

## What it is for

Three questions, answered without touching your gameplay code:

| Question | Mechanism |
| --- | --- |
| Can this agent notice that target right now? | sight / hearing / proximity, evaluated per sensor |
| What did it just notice, and when? | results with distance, position, timestamp and memory flag |
| What did it get hit by? | one-shot stimuli delivered to every interested listener once |

It does not model stealth modifiers, threat scoring, cover, or behaviour selection. Those consume perception; keeping them outside is what lets the data model stay small.

## How it works

```mermaid
flowchart LR
    A[register targets] --> B[snapshot<br/>once per frame]
    B --> C[broadphase<br/>per sensor]
    C --> D[Burst job<br/>cone / sphere test]
    D --> E[commit<br/>results + memory]
    E --> F[gameplay reads]
```

Two invariants make the Burst path possible:

1. The snapshot is written once per frame on the owner thread and never mutated while jobs read it.
2. Results and memory are only updated on the owner thread, after job completion.

## Getting started

### 1. Add a manager

Create an empty object and add `PerceptionManagerComponent`. Nothing runs without it; `AIPerceptionComponent` logs an error and stays uninitialized instead of creating a hidden driver.

| Field | Meaning |
| --- | --- |
| Maximum Perceptibles | capacity reserved up front; an oversized value costs memory immediately |
| Spatial Cell Size | broadphase cell edge; tune from profiling, not from intuition |
| Use Deferred Job Completion | finish jobs in `LateUpdate` instead of `Update` |
| LOD Reference / LOD Levels | optional distance-based update throttling |

### 2. Mark what can be detected

Add `PerceptibleComponent` to every object that can be noticed.

```csharp
PerceptibleComponent target = GetComponent<PerceptibleComponent>();
target.SetTypeId(PerceptibleTypes.Enemy);   // stable, project-owned number
target.SetDetectionRadius(1.25f);           // extends effective range; 0 means a point
```

`Is Sound Source` only matters for hearing, and hears continuous emitters rather than events. `LOS Point` overrides the transform origin used for occlusion rays.

### 3. Configure a sense

Add `AIPerceptionComponent` to the observer and enable the senses you need. Every sense owns its own range, interval, memory and capacity, so a cheap proximity sense can run at 5 Hz while sight runs at 20 Hz.

If `Use Line of Sight` is on, each candidate inside the cone costs one `Physics.Raycast`. `Max LOS Checks` bounds that per update and the sensor rotates its cursor across frames, so a budget never starves the same targets forever.

### 4. Read results

```csharp
using CycloneGames.AIPerception.Runtime;
using UnityEngine;

public sealed class ChaseNearestSightTarget : MonoBehaviour
{
    private AIPerceptionComponent _perception;

    private void Awake() => _perception = GetComponent<AIPerceptionComponent>();

    private void Update()
    {
        if (!_perception.HasSightDetection)
        {
            return;
        }

        IPerceptible closest = _perception.GetClosestSightTarget();
        if (closest != null)
        {
            transform.position = Vector3.MoveTowards(transform.position, closest.Position, 2f * Time.deltaTime);
        }
    }
}
```

Results are ordered by distance, so index `0` is always the nearest. A target that left stays in the set until its memory expires, with `IsFromMemory == true`; check it when a remembered position must not be treated as a live sighting.

### 5. Confirm it works

- the Scene view draws the sensor cone and detection links for the selected component;
- the Inspector's Scene Diagnostics block shows a count once a target is in range;
- `SensorManager.GetMemoryStats()` reports non-zero perceptibles and sensors.

If the count stays at zero, check in this order: manager present, target `Is Detectable`, type filter, affiliation mask.

## Tutorials

### Filter by type

Give each kind of target a stable number and let a sense ignore everything else.

```csharp
config.FilterByType = true;
config.TargetTypeId = PerceptibleTypes.Player;
```

The filter runs inside the job, so rejected targets cost nothing beyond the broadphase.

### Split the world into teams

Team identity is optional: implement `IPerceptionTeamProvider` (or use `PerceptibleComponent`, which already does).

```csharp
// One-time policy.
manager.SetSameTeamFriendly();
manager.SetTeamAttitude(Team.Blue, Team.Red, PerceptionTeamAttitude.Hostile);

// Per sense.
sightConfig.ObserverTeamId = Team.Blue;
sightConfig.Affiliation = PerceptionAffiliation.Enemies;
```

`AIPerceptionComponent` stamps its own `Observer Team` into every sense it creates, so a component only needs the value once.

The table is directional: `A -> B` and `B -> A` are separate cells, which is what one-sided aggression needs. `Tools > CycloneGames > AI Perception > Team Matrix` edits it visually; the same data can be stored in a `PerceptionTeamAttitudeAsset` (relation list plus a default policy, so two factions serialize as one row).

A sensor with `Affiliation = All` skips the lookup entirely.

### Report damage

Damage is an event, not a snapshot query. Report it once; every damage sensor consumes it once.

```csharp
manager.ReportStimulus(attackerHandle, hitPoint, damageAmount);
```

The manager stamps the clock and a monotonic sequence, so two sensors observing the same frame agree on order.

Sequence semantics:

- starts at 1 (`0` means "no stimulus") and increases by one per report, never reused;
- **per manager**, not global - two managers each count from 1, so sequences from different managers are not comparable;
- the ring holds 256 events, so only `[latest - 255, latest]` is resident; `TryGetStimulus` returns false outside that window;
- a sensor that falls behind misses the oldest event rather than growing memory, and stimuli reported before a sensor exists are never replayed.

A `long` counter at 1 000 reports per second would take about 292 million years to wrap, so overflow is not a practical concern.

### Drive perception from your own clock

```csharp
var clock = new DelegatePerceptionClock(() => matchTime.Seconds);
var manager = new SensorManager(registry, 4096, clock);
```

`ManualPerceptionClock` suits fixed-step loops and tests. The clock is a pure reader: perception never advances it. Swapping clocks at runtime invalidates the aging baseline, so recreate sensors after a swap.

### Build without MonoBehaviours

```csharp
var registry = new PerceptibleRegistry(initialCapacity: 1024, maximumCapacity: 8192, cellSize: 20f);
var manager = new SensorManager(registry, 256);
var sensor = new ProximitySensor(transform, ProximitySensorConfig.Default, manager);

PerceptibleHandle handle = registry.Register(perceptible);
registry.RebuildData();
sensor.UpdateSensor(0f);
```

Dispose in reverse order: sensors, then manager, then registry.

## Parameters and their cost

| Setting | Trades |
| --- | --- |
| Half Angle, Max Distance | broader cone means more candidates from the broadphase |
| Use Line of Sight | one raycast per candidate, bounded by Max LOS Checks |
| Update Interval | lower frequency saves job time; memory still ages by the clock |
| Memory Duration | longer memory keeps results in the set after the target leaves |
| Maximum Results | preallocated per sensor; overflow drops the farthest results |
| Spatial Cell Size | too small multiplies cells, too large degrades to a linear scan |

## Performance and memory

- One snapshot per frame regardless of sensor count; cost scales with registered perceptibles.
- Each sensor runs one job over its candidates. Below `InlineExecutionThreshold` candidates the query runs inline, which is cheaper than scheduling.
- Results, memory and candidate storage are native and sized once from `Capacity`.
- No managed allocation after the first update. Anything that would allocate is rejected at configuration time rather than per frame.

Measure with `SensorManager.GetMemoryStats()`: peak candidate count, result capacity, eviction count and per-update workload per sensor.

## Debugging

| Tool | Shows |
| --- | --- |
| Scene gizmos | cone, radius, detection links; pin a component to keep it visible while unselected |
| Inspector Scene Diagnostics | live counts while not playing |
| Inspector Runtime Diagnostics | per-sense counts, status and memory in play mode |
| Team Matrix window | the whole attitude table, with hover readout and undo |

Common symptoms:

| Symptom | Likely cause |
| --- | --- |
| No detections at all | no manager, target not detectable, type filter, or affiliation mask |
| Detections but stale positions | results are memory entries (`IsFromMemory`) |
| Every target detected | `Affiliation = All` and no type filter |
| Editor hitches while playing | LOS checks over many candidates; lower Max LOS Checks |

## API reference

```csharp
// Registry
PerceptibleHandle Registry.Register(IPerceptible perceptible);
bool Registry.Unregister(PerceptibleHandle handle);
void Registry.RebuildData();

// Manager
long Manager.ReportStimulus(PerceptibleHandle instigator, float3 position, float strength,
                            PerceptionStimulusKind kind = PerceptionStimulusKind.Damage);
void Manager.SetTeamAttitude(int observerTeam, int targetTeam, PerceptionTeamAttitude attitude);
void Manager.SetSameTeamFriendly();
void Manager.CopyTeamAttitudes(NativeArray<byte> source);
AIPerceptionMemoryStats Manager.GetMemoryStats();

// Observer
bool Perception.HasSightDetection;
int Perception.SightDetectedCount;
IPerceptible Perception.GetClosestSightTarget();
void Perception.GetAllSightDetections(ref NativeList<DetectionResult> results);
```
