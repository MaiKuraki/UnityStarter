# CycloneGames.BehaviorTree Test and Benchmark Guide

[简体中文](README.SCH.md)

This guide explains how to verify `CycloneGames.BehaviorTree`, run bounded performance experiments, and interpret the resulting evidence. Start with the functional suites, use a small benchmark smoke run, and expand to matrices or soak runs only after the smaller workload is stable.

The benchmark tools produce measurements for a specific Unity process, backend, build configuration, machine, and workload. A passing test or budget is not certification for a Player, IL2CPP, AOT, managed stripping, a target platform, or long-running production use.

## Five-minute verification

### 1. Run the Editor suites

1. Open the Unity project at `<repo-root>/UnityStarter`.
2. Open `Window > General > Test Runner`.
3. Select `EditMode`.
4. Run `CycloneGames.BehaviorTree.Tests.Editor`.
5. If the DOD assembly is active, also run `CycloneGames.BehaviorTree.Runtime.DOD.Tests.Editor`.
6. In this checkout, also run `CycloneGames.BehaviorTree.Integrations.DeterministicMath.Tests.Editor` because both local modules and the explicit integration folder are present.

These suites provide the fastest feedback for compiler validation, code-first construction, runtime semantics, blackboard contracts, scheduling, DOD safety, Editor authoring safety, and benchmark utilities.

### 2. Run the PlayMode suites

1. Switch the Test Runner to `PlayMode`.
2. Run `CycloneGames.BehaviorTree.Tests.PlayMode`.

The PlayMode assembly verifies the scene-bound benchmark runner and `BTRunnerComponent` registration, pause, disable, stop, and replay behavior.

### 3. Run a bounded benchmark smoke test

1. Open `Tools > CycloneGames > Behavior Tree > Behavior Tree Benchmark`.
2. Select `Custom` and enter this smoke workload: `64` agents, `2` leaves, `1` read, `1` write, `0` decorator layers, `0` work iterations, `4` tracked keys, `8` warmup frames, `60` measurement frames, `0` soak frames, and `1` tick per frame.
3. Leave delta flush enabled and deterministic hash checks disabled for the first run.
4. Select `Run Editor Benchmark`.
5. Confirm that the result is valid, the workload fields match the request, and the CSV/JSON export works.
6. Only then increase scale, run a matrix, or start a soak test.

## Test assemblies and coverage

The test assemblies are `autoReferenced: false`. Unity Test Runner discovers them through their test-assembly configuration; ordinary runtime assemblies do not acquire a dependency on them.

### Editor assembly

`CycloneGames.BehaviorTree.Tests.Editor` contains the following test classes:

| File | Primary coverage |
| --- | --- |
| `Consistency/BehaviorTreeAuthoringCompilerTests.cs` | Authoring graph structure, exact custom-emitter registration, protected/revalidated analysis artifacts, semantic setup validation, node/runtime-GUID uniqueness across subtree occurrences, hard traversal limits, direct built-in configuration emission, and stable UTF-16 hashing |
| `Consistency/BehaviorTreeCodeFirstTests.cs` | Fluent builder contracts, deterministic random behavior, setup freezing and validation, malformed snapshot/delta rejection, node lifecycle reasons, Parallel/Switch/directional SubTree semantics, time and cooldown boundaries, owner-thread checks, disposal, transactional runtime graph validation, and repair after rejected graphs |
| `Consistency/BehaviorTreeConsistencyTests.cs` | Stop/wake behavior, selector aborts, typed blackboard storage, observers, schema enforcement, deterministic serialization, local-object preservation during reads, monotonic stamp sequences, strict snapshot framing, and snapshots/deltas |
| `Consistency/RuntimeBlackboardSafetyTests.cs` | Concurrent hash/serialization scratch exclusion, bitwise float change contracts, producer-thread delta signaling, atomic SubTree output batches, and copied Editor diagnostics |
| `Consistency/BehaviorTreeEditorSafetyTests.cs` | Non-mutating graph population, explicit root repair, canonical diagnostics, cycle prevention, safe paste behavior, and benchmark request limits |
| `Consistency/BehaviorTreeTickManagerTests.cs` | Capacity validation, terminal removal, deferred registration, priority movement/removal, budget validation, and LOD configuration |
| `Performance/BehaviorTreeBenchmarkTests.cs` | Managed tick and blackboard measurements, delta flush/apply allocation guards after warmup, result assessment, batch summaries, preset matrices, and memory-budget estimates |

`CycloneGames.BehaviorTree.Runtime.DOD.Tests.Editor` is a separate conditional test assembly with the same Burst, Collections, and Mathematics gates as the DOD runtime. `DataOriented/BehaviorTreeDataOrientedSafetyTests.cs` covers timing accumulation, Repeater/Retry/WaitTicks parameter domains, state-hash v2, immutable flat-tree ownership, scheduler leases, internal Job visibility, generation-safe handles, stale action requests, Job completion before public reads, owner-thread access, reactive invalidation, and empty Parallel normalization. It naturally disappears when the optional DOD dependencies are absent.

`CycloneGames.BehaviorTree.Integrations.DeterministicMath.Tests.Editor` is a separate explicit test assembly. `Integrations/DeterministicMath/DeterministicMathBlackboardIntegrationTests.cs` covers fixed-point blackboard storage, schema defaults, delta round trips, and deterministic random state restoration. The assembly is `autoReferenced: false`, directly references both local modules, and is present whenever the integration folder is distributed.

### PlayMode assembly

`CycloneGames.BehaviorTree.Tests.PlayMode` contains:

| File | Primary coverage |
| --- | --- |
| `PlayMode/BehaviorTreePlayModeBenchmarkTests.cs` | Runner completion, recommended matrices, priority comparisons, CSV/JSON serialization, and result-file writes |
| `PlayMode/BehaviorTreeRunnerLifecycleTests.cs` | Managed and priority-managed runner registration across pause, disable, stop, and play transitions |

### Networking tests are separate

Networking verification belongs to the sibling `CycloneGames.BehaviorTree.Networking` package. Run its `CycloneGames.BehaviorTree.Networking.Tests.Editor` assembly and `BehaviorTreeNetworkingIntegrationTests.cs` when changing protocol messages, receive-state ordering, snapshots, deltas, authority, or transactional apply behavior. That suite covers composite auxiliary state in tree hashes, bridge owner-thread/disposal rules, protocol registration, payload bounds and framing, identity/order/replay checks, sequence wrap, and rejection without partial mutation. Do not treat the main package's blackboard serialization tests as a substitute for the adapter package's integration tests.

## Batchmode templates

Replace every angle-bracket placeholder. The Unity executable must match the version recorded by the current checkout; do not commit a machine-specific executable path.

Editor tests:

```text
"<UnityEditorExecutable>" -batchmode -nographics -quit -projectPath "<repo-root>/UnityStarter" -runTests -testPlatform EditMode -assemblyNames "CycloneGames.BehaviorTree.Tests.Editor" -testResults "<results-path>/behavior-tree-editmode.xml"
```

Conditional DOD Editor tests:

```text
"<UnityEditorExecutable>" -batchmode -nographics -quit -projectPath "<repo-root>/UnityStarter" -runTests -testPlatform EditMode -assemblyNames "CycloneGames.BehaviorTree.Runtime.DOD.Tests.Editor" -testResults "<results-path>/behavior-tree-dod-editmode.xml"
```

PlayMode tests:

```text
"<UnityEditorExecutable>" -batchmode -nographics -quit -projectPath "<repo-root>/UnityStarter" -runTests -testPlatform PlayMode -assemblyNames "CycloneGames.BehaviorTree.Tests.PlayMode" -testResults "<results-path>/behavior-tree-playmode.xml"
```

Run the conditional DOD assembly, explicit DeterministicMath integration assembly, and separate Networking assembly with their own `-assemblyNames` invocation when those assemblies are in the change closure. Preserve the Unity process exit code, test XML, Editor log, checkout revision, backend, and command line as the reproducible test record.

## Benchmark architecture

Benchmark code is isolated in `CycloneGames.BehaviorTree.Benchmarks`, which is also `autoReferenced: false`. A test or tool assembly must reference it explicitly.

- `BehaviorTreeBenchmarkSession` owns the synthetic runtime trees and delta trackers for one configuration. `RunImmediate` performs setup, warmup, measurement, soak, assessment, and disposal synchronously.
- `BehaviorTreeBenchmarkWindow` provides guarded Editor controls for a single run, scale matrix, full matrix, priority comparison, and configured budget matrix.
- `BehaviorTreeBenchmarkRunner` executes a configuration or matrix across PlayMode frames and can export results automatically.
- `BehaviorTreeBenchmarkExportUtility` serializes single and batch results as CSV or JSON.

`BehaviorTreeBenchmarkSession` is a synthetic workload. It is useful for repeatable comparisons of framework paths, but it does not model every gameplay system, render workload, network transport, asset stream, or platform service in a shipping title.

## Benchmark dimensions

### Scale presets

- `AiBattle500`
- `AiCrowd1000`
- `AiStress5000`
- `AiExtreme10000`
- `Network100Players500Ai`
- `LongSoak1000`
- `Custom`

### Complexity tiers

- `Light`
- `Medium`
- `Heavy`

### Scheduling profiles

| Profile | Workload model |
| --- | --- |
| `FullRate` | Every synthetic agent is eligible on every simulated tick |
| `LodCrowd` | Near, middle, and far groups use progressively reduced cadence |
| `PriorityLod` | Priority and distance groups use different cadences |
| `NetworkMixed` | A player-oriented front group stays frequent while AI groups are reduced; optional hash checks model synchronization work |
| `FarCrowd` | Far agents receive a more aggressive cadence reduction |
| `UltraLod` | Only a small near group remains at full cadence |
| `PriorityManaged` | Synthetic priority-budget behavior for comparison with the other profiles |

These profiles model scheduling decisions inside the benchmark session. They do not prove the behavior or cost of every production AI scheduler.

## Hard workload limits

The Editor window validates both scalar fields and derived work before running or creating a scene. The limits protect the Editor from accidental unbounded requests; they are not supported-agent-count claims.

| Input | Accepted range |
| --- | ---: |
| Agents | `1..100000` |
| Leaf nodes per tree | `1..512` |
| Blackboard reads per leaf/tick | `0..256` |
| Blackboard writes per leaf/tick | `1..256` |
| Decorator layers per leaf | `0..64` |
| Simulated work iterations per leaf | `0..100000` |
| Tracked keys per agent | `0..8192` |
| Warmup frames | `0..1000000` |
| Measurement frames | `1..1000000` |
| Soak frames | `0..1000000` |
| Hash-check and soak-sample intervals | `1..1000000` |
| Ticks per frame | `1..64` |

Derived limits are evaluated with checked arithmetic:

```text
nodesPerAgent = 1 + leafNodes * (1 + decoratorLayers)
totalRuntimeNodes = nodesPerAgent * agents                         <= 2,000,000
totalTrackedKeys = agents * trackedKeysPerAgent                    <= 20,000,000
frameCount = warmupFrames + measurementFrames + soakFrames
workPerLeaf = 1 + reads + writes + decoratorLayers + workIterations
workUnitsPerFrame = agents * leafNodes * ticksPerFrame * workPerLeaf <= 25,000,000
totalWorkUnits = workUnitsPerFrame * frameCount                     <= 1,000,000,000
```

Overflow or any exceeded scalar/derived bound rejects the request before execution. A configuration can therefore be below every scalar maximum and still be rejected by a derived bound.

## Using the benchmark window

Open `Tools > CycloneGames > Behavior Tree > Behavior Tree Benchmark`.

### Single runs

1. Choose a preset, complexity, and scheduling profile, or edit a custom configuration.
2. Check that no validation error is shown.
3. Select `Run Editor Benchmark` for a synchronous Editor measurement.
4. Review the result before exporting it.

Large synchronous Editor runs can make the Editor unresponsive until the loop completes. Establish a bounded smoke result first and prefer a generated PlayMode scene for longer frame-by-frame observation.

### Matrix and comparison runs

- `Run Scale Matrix For Selected Complexity` compares the recommended scale presets at one complexity.
- `Run Full Matrix (Scale x Complexity)` combines the recommended presets and all complexity tiers.
- `Run PriorityManaged Comparison` compares `FullRate`, `PriorityLod`, `PriorityManaged`, and `UltraLod` for the selected base configuration.
- `Run Configured Budget Matrix` runs the source-defined budget matrix. A passing matrix only means that its configured thresholds passed in that process and environment.

### Generated PlayMode scenes

The window can create a single-run, scale-matrix, full-matrix, priority-comparison, or configured-budget-matrix scene. Before replacing the active scene, it calls Unity's modified-scene save prompt. Cancelling the prompt cancels scene creation. The newly created scene is marked dirty and remains unsaved until the user explicitly saves or discards it.

Generated runners start automatically in PlayMode. Warmup, measurement, and soak each advance one benchmark frame per Unity frame. Single and matrix runners export according to their CSV/JSON settings.

## Programmatic sessions

Use direct sessions only in an assembly that explicitly references `CycloneGames.BehaviorTree.Benchmarks`:

```csharp
var config = new BehaviorTreeBenchmarkConfig
{
    BenchmarkName = "BehaviorTree Smoke",
    AgentCount = 64,
    LeafNodesPerTree = 2,
    BlackboardReadsPerLeafPerTick = 1,
    WritesPerLeafPerTick = 1,
    DecoratorLayersPerLeaf = 0,
    SimulatedWorkIterationsPerLeaf = 0,
    TrackedKeysPerAgent = 4,
    WarmupFrames = 8,
    MeasurementFrames = 60,
    SoakFrames = 0,
    TicksPerFrame = 1,
    EnableDeltaFlush = true,
    EnableDeterministicHashCheck = false
};

BehaviorTreeBenchmarkResult result =
    BehaviorTreeBenchmarkSession.RunImmediate(config);
```

`RunImmediate` includes explicit garbage collections during setup and measures only the synthetic session loop. For controlled frame progression, construct a session, call `Setup`, invoke `RunWarmupFrame`, `RunMeasuredFrame`, and `RunSoakFrame` as required, then call `Complete` and `Dispose` it.

The hard limits in the previous section are enforced by `BehaviorTreeBenchmarkWindow`. Direct `BehaviorTreeBenchmarkSession` calls and manually configured `BehaviorTreeBenchmarkRunner` instances must apply their own trusted configuration bounds before allocating or running. Keep benchmark-only forced collection and synthetic work out of production gameplay paths.

## Exports and persistence

The default output directory is:

```text
Application.persistentDataPath/BehaviorTreeBenchmarkResults
```

The window can export the last single or matrix result as CSV or JSON to a selected path, or write both formats to the default directory. Generated PlayMode runners use the same default folder unless their serialized folder name is changed, and log the final path to the Unity Console.

Benchmark files are user-local measurement artifacts, not framework configuration or a source of truth. Retain the environment metadata needed for comparison, then archive or delete the files according to the project's evidence policy. A generated scene is persisted only if the user saves it to an explicit project path.

## Interpreting results

| Field | Meaning and limits |
| --- | --- |
| `PotentialTicks` | Theoretical ticks if every agent were eligible for every configured tick |
| `ExecutedTicks` | Ticks actually executed after the selected scheduling profile |
| `EffectiveTickRatio` | `ExecutedTicks / PotentialTicks`; intentional cadence reduction can lower this value, so check the configuration before classifying it as dropped work |
| `AverageActiveAgentsPerFrame` / `PeakActiveAgentsPerFrame` | Average and peak synthetic agents ticked during measured frames |
| `AverageFrameMilliseconds` / `MaxFrameMilliseconds` | Time spent in the benchmark session's measured frame loop, not complete rendered Player frame time |
| `TicksPerSecond` | Executed synthetic ticks divided by measured elapsed time |
| `TotalDeltaFlushes` / `TotalHashChecks` | Enabled synchronization-like work performed by the session |
| `ManagedMemoryDeltaBytes` | Managed heap difference between session samples; process noise and GC timing can affect it |
| `PeakManagedMemoryBytes` | Highest sampled managed heap size, not native, GPU, driver, or total process memory |
| `SoakManagedMemoryDeltaBytes` | Peak sampled managed growth relative to the soak baseline |
| `Gen0Collections` / `Gen1Collections` / `Gen2Collections` | Process collection-count differences observed during the session |
| `ProductionBudgetPassed` / `BudgetSummary` | Evaluation against the configuration's thresholds, not a platform certification result |

`MaxManagedMemoryDeltaBytes` is a retained-memory budget for the session, not a per-frame allocation limit. Use Unity Profiler allocation samples, the focused post-warmup allocation tests, GC counts, and soak drift together when investigating hot-path allocation behavior.

Compare results only when the revision, Unity version, backend, build type, safety checks, hardware, power/thermal state, frame pacing, workload, and background activity are recorded and sufficiently equivalent. Editor and PlayMode measurements are useful for regression discovery, but release Player results on representative devices are required for product budgets.

## Evidence workflow

1. Run the focused EditMode suite and record failures before performance work.
2. Run the runner lifecycle PlayMode tests when scene registration or scheduling changes.
3. Establish a small benchmark smoke result and verify exports.
4. Capture a repeated baseline with fixed configuration and environment metadata.
5. Run only the matrix needed to answer the current capacity or scheduling question.
6. Use Unity Profiler or platform tooling to investigate CPU samples, managed allocations, native memory, and frame-time distribution.
7. Repeat the representative workload in a release Player on every target hardware tier that needs a claim.
8. Run IL2CPP/AOT, managed stripping, headless, WebGL, mobile, desktop, or console-specific checks separately as applicable.
9. Use a bounded soak run for long-lived drift, handle leaks, and collection behavior; retain start/end captures and recovery observations.

No single step can be substituted for all later steps. In particular, a successful Editor benchmark does not establish Player, IL2CPP, AOT, platform, thermal, battery, long-soak, or cross-device compatibility.

## Adding or changing tests

1. Put pure runtime/compiler/blackboard/scheduler contracts in the focused Editor test class closest to the behavior.
2. Put scene, `MonoBehaviour`, registration, disable/enable, and frame-bound behavior in PlayMode tests.
3. Put Editor graph, Undo, copy/paste, asset mutation, and scene-safety behavior in `BehaviorTreeEditorSafetyTests`.
4. Put DOD handle, Job ownership, stale completion, and flat-tree validation behavior in `BehaviorTreeDataOrientedSafetyTests`.
5. Put DeterministicMath behavior in its explicit integration test assembly.
6. Put Networking protocol and adapter behavior in the separate Networking package tests.
7. For each failure path, assert both the rejection and the absence of partial state mutation where transactionality matters.
8. Dispose runtime trees, blackboards, sessions, native owners, and subscriptions in `finally` blocks or fixture teardown.
9. Warm up allocation-sensitive code before measuring, bound every loop and payload, and keep correctness assertions separate from noisy wall-clock thresholds.
10. Update this English guide and `README.SCH.md` together when suites, assemblies, menu paths, limits, fields, or persistence behavior change.

When changing `BTPriorityTickManagerComponent` auto-discovery, include a PlayMode case for an undefined player tag. The intended fail-closed behavior disables automatic lookup instead of repeatedly throwing from the update loop.

## Troubleshooting

### A test assembly is missing from Test Runner

- Wait for script compilation to finish and inspect Console compile errors first.
- Confirm the test asmdef still has `optionalUnityReferences: ["TestAssemblies"]` and references every required runtime/integration assembly.
- The DOD suite is intentionally absent when Burst, Collections, or Mathematics version defines are inactive.
- The DeterministicMath suite uses explicit direct references to both local assemblies. In distributions that omit either dependency, omit the integration folder and its test folder together; both are present in this checkout.
- Networking tests appear under the sibling package's assembly, not the main BehaviorTree test assembly.

### The benchmark request is rejected

- Check every scalar limit, then calculate the derived node, tracked-key, per-frame work, and total-work values.
- Reduce agents, leaves, decorators, tracked keys, ticks, work iterations, or frame counts. Raising the documented hard bounds requires an implementation and safety review; it is not a test workaround.

### Scene creation does nothing

- The modified-scene prompt may have been cancelled.
- Resolve compile errors and retry with a valid bounded configuration.
- After creation, save the dirty scene explicitly if it should become a project asset.

### Results vary between runs

- Stabilize warmup, background Editor activity, power mode, thermal state, and frame pacing.
- Compare the same revision and configuration.
- Use repeated samples and distributions; do not explain a regression from one noisy run.

### Export files are missing

- Check the Unity Console for the resolved path and I/O errors.
- Resolve `Application.persistentDataPath` for the process and platform that performed the run.
- Confirm write permission and available storage; do not redirect results into package source folders.

## Minimum validation record

For a shareable result, record:

- repository revision and local-change status;
- Unity version from the current checkout;
- Editor or Player, scripting backend, architecture, build configuration, and safety settings;
- operating system, device/hardware tier, power and thermal conditions;
- exact benchmark configuration, preset, complexity, scheduling profile, and budgets;
- test command or UI path, exit code, XML/log paths, CSV/JSON paths, and profiler capture paths;
- warmup, sample count, repeated-run distribution, failures, and excluded evidence;
- which platform, IL2CPP/AOT, stripping, memory, leak, and soak checks were not run.

This record makes the measurement reproducible and prevents a local result from being generalized beyond its evidence.
