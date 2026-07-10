# CycloneGames.Logger Samples

The sample scene teaches the Logger workflow in small, isolated steps: write ordinary records, use the allocation-aware builder API, observe queue and cache state, attach a temporary file sink, and run a local comparison harness.

The scripts compile in `CycloneGames.Logger.Samples`. That assembly references `CycloneGames.Logger` and `CycloneGames.Logger.Unity`, has `autoReferenced: false`, and is not part of the production API surface.

Samples are teaching and diagnostic tools. Their timings and allocations depend on the Editor or Player, backend, hardware, Console state, storage, active sinks, and current settings. They are not shipping performance targets, universal capacity recommendations, or platform certification evidence.

## Sample Contents

| File | What it demonstrates | Important side effect |
| --- | --- | --- |
| `LoggerSample.cs` | Minimal `CLogger.LogInfo`, `LogWarning`, and `LogError` usage | Uses the project-owned Unity bootstrap; it does not create or stop the logger |
| `LoggerPerformanceTest.cs` | A finite mixed-level load using state plus cached/static builders | Registers a temporary file sink outside WebGL and changes the global level to `Trace` |
| `LoggerPoolMonitor.cs` | Queue count/character occupancy and process-wide cache observations | Prints through `Debug.Log` and can submit a bounded burst |
| `LoggerBenchmark.cs` | Local comparison of filtered, no-sink, core, file, and Unity Console paths | Reconfigures/stops the global logger, forces GC, performs I/O, and writes a report |
| `SampleScene.unity` | Hosts the example components | `Benchmark` is active by default; `LoggerSample` and `PerformanceTest` are inactive |

`LoggerPoolMonitor` is not placed in the scene. Add it to a temporary GameObject when you want to inspect queue and cache statistics.

## Before Running a Sample

1. Open `UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Logger/Samples/SampleScene.unity`.
2. Wait for `CycloneGames.Logger`, `CycloneGames.Logger.Unity`, and `CycloneGames.Logger.Samples` to compile without errors.
3. Create and validate `Assets/Resources/CycloneGames.Logger/LoggerSettings.asset` if it does not exist.
4. Keep only one of `Benchmark`, `LoggerSample`, or `PerformanceTest` active.
5. Enter Play Mode, observe the relevant output, then leave Play Mode and check for shutdown or disposal errors.

`LoggerBenchmark` owns global Logger reconfiguration for its isolated run. Do not enable it in a scene containing application systems that own or use the global logger.

## Tutorial 1: Minimal Unity Logging

Enable the `LoggerSample` GameObject and disable the other scenarios. The component relies on `LoggerBootstrap` and contains only normal application calls:

```csharp
private void Start()
{
    CLogger.LogInfo("Logger sample started.", "Sample");
    CLogger.LogWarning("This is a warning example.", "Sample");
    CLogger.LogError("This is an error example.", "Sample");
}
```

Expected result:

- the active settings asset chooses the sink set;
- the default `Info` threshold accepts all three records;
- `Sample` appears as the category;
- Unity Console output contains a source link when `UnityLogger` is active.

If no record appears, verify `registerUnityLogger`, `defaultLevel`, `defaultFilter`, and the Console filters.

## Tutorial 2: Allocation-Aware Message Construction

An interpolated string is created before the logger can filter it:

```csharp
CLogger.LogDebug($"Entity {entityId} updated.", "Simulation");
```

For a measured hot path, pass the state separately and use a static or cached delegate:

```csharp
CLogger.LogDebug(
    entityId,
    static (value, builder) => builder.Append("Entity ").Append(value).Append(" updated."),
    "Simulation");
```

The builder runs only after level, category, sink, lifecycle, and queue-reservation checks succeed. This avoids a capturing closure in the shown call, but does not guarantee that the complete path is allocation-free. Pool misses, builder growth, sink formatting, Unity Console copies, exceptions, and I/O can allocate.

## Tutorial 3: Finite Mixed-Level Load

Enable `PerformanceTest` and disable the other scenarios. `LoggerPerformanceTest`:

1. creates a `FileLogger` under `Application.temporaryCachePath` outside WebGL;
2. registers it through `AddLoggerUnique`;
3. sets the global level to `Trace`;
4. submits up to 10,000 records across all six active severities;
5. removes and disposes the file sink only when `RemoveLogger` returns `true`.

The output file is:

`Application.temporaryCachePath/CycloneGames.Logger/LoadExample.log`

The displayed elapsed time covers frame-distributed submission. Frame rate, active sinks, queue drops, Unity Console, storage, Editor overhead, and scheduling all affect it. Do not report it as Logger throughput. Inspect all of the following before drawing a local conclusion:

- `CLogger.Instance.GetProcessingStatistics()`;
- `FileLogger.Statistics`;
- Unity Profiler data;
- file contents and final byte count.

WebGL skips the file sink because `FileLogger` is unsupported there.

## Tutorial 4: Queue and Cache Observation

Add `LoggerPoolMonitor` to a temporary GameObject. It reports:

- current and peak core queue message occupancy;
- current and peak retained-character occupancy;
- total core drops;
- retained and peak cached `LogMessage`/`StringBuilder` counts;
- cache misses.

Use the `Run Bounded Burst Example` context menu to submit `BurstLogCount` records through a static state-builder callback. The burst remains governed by the active `LoggerProcessingOptions`: records can be rejected or evicted, and reserved critical capacity reduces ordinary contention without guaranteeing delivery.

The component intentionally displays only a small subset. Advanced diagnosis should also query:

- `ReservedCount`, `InFlightCount`, and their character equivalents;
- `MessageBuilderFailureCount` and `TimestampProviderFailureCount`;
- filter occupancy and rejected mutations;
- sink failure, quarantine, and disposal counters;
- `UnityLogger.GetStatistics()` for the separate Unity handoff.

Logger cache statistics are not a heap profile. They exclude caller strings, most objects, sink buffers, Unity Console storage, native/OS buffers, and filesystem caches. Use Unity Memory Profiler and target tools for a full memory investigation.

## Tutorial 5: Local Benchmark Harness

Enable `Benchmark` and disable every other scenario. The harness runs:

- direct `UnityEngine.Debug.Log` output;
- filtered generic logging;
- an initialized logger without a sink;
- core string, capturing builder, and generic state-builder cases;
- a burst without intermediate pumping;
- file output outside WebGL;
- Unity Console handoff.

It writes UTF-8 without BOM to:

- `Application.temporaryCachePath/CycloneGames.Logger/LoggerBenchmarkReport.txt`
- `Application.temporaryCachePath/CycloneGames.Logger/LoggerBenchmarkFile.log`

The report includes elapsed time, derived microseconds per log, derived logs per second, current-thread allocation observations when supported, Gen0 collection count, pool misses/discards, and core drops.

Interpret the report carefully:

- cases use different iteration counts and include different work;
- the harness selects single-thread processing for controlled caller-pumped cases;
- `NullLogger` measures core dispatch, not a production sink;
- Unity Console and file cases include their formatting and I/O costs;
- forced GC, coroutine yields, Console visibility/collapse, filesystem cache, antivirus, and thermal state affect results;
- `GC.GetAllocatedBytesForCurrentThread` may be unavailable and does not include allocations on another thread;
- the harness has no standalone Player automation, confidence interval, device thermal protocol, or multi-platform baseline.

Use `CycloneGames.Logger.Tests.Performance` for repeatable package-level regression cases. Shipping performance evidence needs a separate protocol with fixed build, hardware, workload, warmup, sample count, storage state, thermal state, and acceptance thresholds.

## Advanced Exercise: A Safe Custom Sink Boundary

Custom sinks receive a borrowed `LogMessage` and may read it only until `Log` returns. Copy only the data needed by the next owner.

```csharp
using System.Text;
using CycloneGames.Logger;

public sealed class ExampleSink : ILogger
{
    private readonly object _syncRoot = new object();
    private readonly StringBuilder _scratch = new StringBuilder(256);

    public void Log(LogMessage message)
    {
        lock (_syncRoot)
        {
            _scratch.Clear();
            message.AppendMessageTo(_scratch, escapeControlCharacters: true);
            // Consume or copy the bounded text before returning.
        }
    }

    public void Dispose()
    {
    }
}
```

Do not retain `LogMessage`. A copied handoff for UI, network, upload, or platform SDK work needs its own message and byte/character limits, overflow policy, drop statistics, thread affinity, flush behavior, and shutdown owner. Format source line numbers with invariant culture or an equivalent invariant integer routine.

## Ownership Checklist for Samples

- Project bootstrap owns the global `CLogger`.
- `LoggerSample` only produces records.
- `LoggerPerformanceTest` temporarily owns a `FileLogger` until successful registration transfers it to `CLogger`.
- Only `RemoveLogger(...)=true` transfers that sink back for caller disposal.
- `LoggerBenchmark` owns the global configuration during its isolated execution and calls `CLogger.Shutdown`.
- Never run the benchmark beside another global Logger owner.

## Output and Cleanup

| Output | Persistence | Cleanup |
| --- | --- | --- |
| Unity Console records | Editor/Player dependent | Clear normally; they are not durable records |
| `LoadExample.log` | Plaintext UTF-8 under `temporaryCachePath` | Safe to delete after `LoggerPerformanceTest` stops |
| `LoggerBenchmarkReport.txt` | Plaintext UTF-8 under `temporaryCachePath` | Safe to delete after inspection |
| `LoggerBenchmarkFile.log` | Plaintext UTF-8 under `temporaryCachePath` | Safe to delete after the benchmark stops |

Do not commit these files. They can contain source locations and sample/application data. The operating system can clear `temporaryCachePath` at any time.

## Validation and Troubleshooting

Minimum sample validation:

1. Run `CycloneGames.Logger.Tests.Editor` before using sample output for diagnosis.
2. Run one scenario at a time.
3. Record Editor/Player, backend, target, hardware, build type, settings, sink set, and Console state.
4. Confirm core and Unity handoff drop counters are appropriate for the scenario.
5. Confirm temporary files can be opened, flushed, and deleted after Play Mode.
6. Repeat performance investigations in a standalone Player and on representative hardware; test IL2CPP separately where used.

| Symptom | Action |
| --- | --- |
| No sample records | Enable a sink, check level/filter, and confirm the benchmark did not stop the global logger |
| Samples interfere | Keep one scenario active and re-enter Play Mode so subsystem registration resets static state |
| Drop counters increase | Treat it as overload evidence; inspect both count/character peaks and Unity handoff statistics before changing capacity |
| WebGL has no sample file | Expected; file sample code is excluded for the Player path |
| Allocation shows `N/A` or zero | The counter is unavailable or inconclusive; use Profiler and platform tools |
| Timing is large or unstable | Reduce unrelated Editor/Console work, then move the experiment to a controlled standalone Player protocol |
| Temporary file cannot be written | Inspect sandbox, quota, permissions, sharing, and `FileLogger.Statistics` |

For full configuration, lifecycle, platform, persistence, performance, and custom-sink guidance, read the package-level `README.md` or `README.SCH.md`.
