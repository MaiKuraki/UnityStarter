# CycloneGames.Logging.Unity Samples

These samples demonstrate the three-layer logging design inside Unity: producers use `LogChannel`, the application bootstrap owns the ambient `LogPipeline`, and isolated diagnostics may create their own explicit pipeline and sinks.

The scripts compile into `CycloneGames.Logging.Unity.Samples`. This optional assembly references `CycloneGames.Logging`, `CycloneGames.Logging.Pipeline`, and `CycloneGames.Logging.Unity`, and has `autoReferenced: false`. It is not a production API assembly.

Sample timings, allocations, queue peaks, and file behavior depend on the Editor or Player, backend, hardware, Console state, storage, active sinks, and settings. They are local diagnostic observations. Shipping targets and platform certification need separate validation.

## Contents

| File | Demonstrates | Ownership or side effect |
| --- | --- | --- |
| `Diagnostics/LoggingSamplesLog.cs` | Assembly-local categories and explicit/ambient `LogChannel` creation | Centralizes all sample channel construction |
| `LoggingSample.cs` | Minimal producer use of the project-owned bootstrap | Produces three records; owns no backend |
| `LoggingPerformanceTest.cs` | Finite mixed-severity load with cached state builders | Temporarily changes minimum severity and may register a file sink; restores/removes both on destroy |
| `LoggingPoolMonitor.cs` | Core queue and process-wide idle-pool observations | Runs a bounded burst after two seconds when attached |
| `LoggingBenchmark.cs` | Local comparison of disabled, no-sink, core, burst, file, and Unity Console paths | Owns one explicit single-threaded pipeline per case, forces GC, performs I/O, and writes a report |
| `SampleScene.unity` | Hosts the example components | `Benchmark` is active; `LoggingSample` and `PerformanceTest` are inactive |

`LoggingPoolMonitor` is not present in the scene. Add it to a temporary GameObject when that exercise is needed.

## Before running

1. Open:

   `UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Logging.Unity/Samples/SampleScene.unity`

2. Wait for `CycloneGames.Logging`, `CycloneGames.Logging.Pipeline`, `CycloneGames.Logging.Unity`, and `CycloneGames.Logging.Unity.Samples` to compile.
3. Create or verify `Assets/Resources/CycloneGames.Logging.Unity/LoggingSettings.asset`.
4. Keep only one of `Benchmark`, `LoggingSample`, or `PerformanceTest` active.
5. Enter Play Mode, observe the selected output and statistics, then leave Play Mode and check for shutdown or disposal diagnostics.

The default active `Benchmark` case forces full garbage collections and performs file and Console I/O. Disable it before collecting unrelated application performance data.

## Tutorial 1: minimal producer

Disable `Benchmark`, enable the `LoggingSample` GameObject, and enter Play Mode. The component uses the same producer contract as a business package:

```csharp
private static readonly LogChannel Log = LoggingSamplesLog.Channel;

private void Start()
{
    Log.Info("Logging sample started.");
    Log.Warning("This is a warning example.");
    Log.Error("This is an error example.");
}
```

Expected behavior depends on `LoggingSettings`:

- `minimumSeverity` must admit the record;
- `categoryFilter` must admit `CycloneGames.Logging.Sample`;
- at least one sink must be active;
- Unity Console output includes a source location when `UnityConsoleLogSink` is active.

The component does not initialize, replace, flush, or shut down the process writer.

## Tutorial 2: deferred hot-path message

An interpolated string is created before the backend can reject it:

```csharp
Log.Debug($"Entity {entityId} updated.");
```

For a measured hot path, pass the state and use a static or cached delegate:

```csharp
Log.Debug(
    entityId,
    static (value, builder) => builder.Append("Entity ").Append(value).Append(" updated."));
```

The pipeline invokes the builder only after severity, category, active-sink, lifecycle, and queue-reservation checks pass. This avoids the shown capturing closure and preformatted string; it does not guarantee that the complete sink path is allocation-free.

## Tutorial 3: finite mixed-severity load

Disable the other scenarios and enable `PerformanceTest`. `LoggingPerformanceTest`:

1. requires the current `LogRuntime.Writer` to be a `LogPipeline`;
2. creates `Application.temporaryCachePath/CycloneGames.Logging/LoadExample.log` outside WebGL Player builds;
3. registers that sink with `TryAddSink` when possible;
4. stores the prior `MinimumSeverity`, then selects `Trace`;
5. submits exactly 10,000 records, up to six records per frame across all active severities;
6. restores the prior minimum severity in `OnDestroy`;
7. disposes the temporary file sink only after `RemoveSink(...)=true` transfers ownership back.

The displayed duration measures frame-distributed submission, not completed sink delivery or durable persistence. Before interpreting it, inspect:

- `LogPipeline.GetStatistics()` for core admission, processing, and drops;
- `FileLogSink.Statistics` and file contents;
- `UnityConsoleLogSink.GetStatistics()` when Unity Console output is enabled;
- Unity Profiler data and target storage behavior.

WebGL skips the file sink. Leaving Play Mode or destroying the component runs cleanup; disabling only the component after it finishes does not invoke `OnDestroy`, so keep the GameObject until normal scene/Play Mode teardown.

## Tutorial 4: queue and pool monitor

Add `LoggingPoolMonitor` to a temporary GameObject. After two seconds it automatically submits `BurstLogCount` `Info` records, then reports once per `MonitorIntervalSeconds`. Its context menu also exposes `Run Bounded Burst Example` and `Show Logging Statistics`.

The report includes:

- current and peak core queue message occupancy;
- current and peak core retained-character occupancy;
- total core drops;
- retained and peak cached `LogEvent` and `StringBuilder` counts;
- pool misses.

The burst remains governed by the active pipeline's count/character limits, reserved critical capacity, and overflow policy. It may legitimately drop records. For deeper diagnosis also inspect reserved/in-flight fields, critical drops, builder/timestamp failures, sink quarantine/disposal, filter budget, and the independent Unity handoff statistics.

Pool statistics are not a heap profile. They exclude caller strings, most managed objects, sink buffers, Unity Console storage, native/OS buffers, and filesystem caches.

## Tutorial 5: local comparison harness

Enable only `Benchmark`. `LoggingBenchmark` creates an explicit single-threaded `LogPipeline` for each isolated case and binds a `LogChannel` directly to it. It does not replace or stop the project-owned ambient writer.

The harness warms pools, then measures:

- filtered generic-state records;
- a pipeline with no sink;
- core string, capturing-builder, and generic-state builder cases using `NullLogSink`;
- a generic-state burst without intermediate pumping;
- file output outside WebGL Player builds;
- Unity Console handoff.

It writes UTF-8 without BOM files under `Application.temporaryCachePath/CycloneGames.Logging/`:

- `LoggingBenchmarkReport.txt`;
- `LoggingBenchmarkFile.log`.

Each pipeline is shut down with a five-second buffered budget before the next case. The report includes elapsed time, derived microseconds per record, derived records per second, current-thread allocation observations when available, Gen0 count, pool misses/discards, and core drops.

Interpret the report carefully:

- cases perform different work and use different iteration counts;
- the harness forces GC between cases;
- `NullLogSink` measures pipeline work, not a production output sink;
- file and Unity Console cases include their formatting, handoff, and I/O costs;
- Console visibility/collapse, filesystem cache, antivirus, thermal state, and Editor overhead affect results;
- `GC.GetAllocatedBytesForCurrentThread` may be unavailable and excludes other-thread allocations;
- the harness has no confidence interval, standalone automation, device thermal protocol, or multi-platform baseline.

Use `CycloneGames.Logging.Pipeline.Tests.Performance` for focused package regression cases. Shipping evidence still needs a fixed build, hardware, workload, warmup, sample count, storage state, thermal state, and acceptance threshold.

## Custom sink exercise

A sink receives a borrowed `LogEvent` and may use it only until `Emit` returns:

```csharp
using System.Text;
using CycloneGames.Logging;

public sealed class ExampleSink : ILogSink
{
    private readonly object _syncRoot = new object();
    private readonly StringBuilder _scratch = new StringBuilder(256);

    public void Emit(LogEvent logEvent)
    {
        lock (_syncRoot)
        {
            _scratch.Clear();
            logEvent.AppendMessageTo(_scratch, escapeControlCharacters: true);
            // Consume or copy bounded data before returning.
        }
    }

    public void Dispose()
    {
    }
}
```

Do not retain `LogEvent`. A UI, network, upload, or platform-SDK handoff needs its own copied payload, count and character/byte limits, overflow policy, drop statistics, thread affinity, flush behavior, and shutdown owner.

## Ownership checklist

- `LoggingBootstrap` owns the ambient Unity pipeline it creates.
- `LoggingSample` only produces records.
- `LoggingPerformanceTest` owns a new file sink until successful registration transfers it to the ambient pipeline.
- Only a successful `RemoveSink` transfers that file sink back for caller disposal.
- `LoggingPerformanceTest` restores the minimum severity it changed during teardown.
- `LoggingBenchmark` owns every explicit pipeline and sink it creates and calls `Shutdown` before discarding them.
- No sample accesses a global pipeline singleton; ambient observation uses `LogRuntime.Writer`.

## Output and cleanup

| Output | Persistence | Cleanup |
| --- | --- | --- |
| Unity Console records | Editor/Player dependent; not a durable log | Clear through normal Console workflow |
| `LoadExample.log` | Plaintext UTF-8 in `temporaryCachePath` | Delete after `PerformanceTest` teardown |
| `LoggingBenchmarkReport.txt` | Plaintext UTF-8 in `temporaryCachePath` | Delete after inspection |
| `LoggingBenchmarkFile.log` | Plaintext UTF-8 in `temporaryCachePath` | Delete after benchmark shutdown |

Do not commit these outputs. They may contain source locations and sample/application data. The operating system may clear `temporaryCachePath` at any time.

## Validation and troubleshooting

Minimum sample validation:

1. Run `CycloneGames.Logging.Unity.Tests.Editor` before using sample output for diagnosis.
2. Run one scenario at a time.
3. Record Editor/Player, scripting backend, target, hardware, build type, settings, sink set, and Console state.
4. Check both core and Unity handoff drop/peak counters.
5. Confirm temporary files can be opened and deleted after the owning scenario stops.
6. Repeat performance investigation in a standalone Player on representative hardware; test IL2CPP separately where used.

| Symptom | Action |
| --- | --- |
| No sample records | Check active sink, `minimumSeverity`, `categoryFilter`, and bootstrap initialization status |
| Load sample disables itself | Confirm `LogRuntime.Writer` is a `LogPipeline` owned by the Unity bootstrap |
| Drop counters increase | Treat this as overload evidence; inspect both count and character peaks before changing capacity |
| WebGL creates no sample file | Expected; the file path is excluded for WebGL Player |
| Allocation shows unavailable or zero | The counter is unavailable or inconclusive; use Profiler and target tools |
| Timing is large or unstable | Reduce unrelated Editor/Console work, then move the investigation to a controlled Player protocol |
| Temporary file cannot be written | Inspect sandbox, quota, permission, sharing, and `FileLogSink.Statistics` |

For lifecycle, build overrides, platform behavior, persistence, and shipping validation, read the package-level `README.md` or `README.SCH.md`.
