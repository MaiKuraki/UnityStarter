# CycloneGames.BehaviorTree Test & Benchmark Guide

[简体中文](README.SCH.md)

This folder contains EditMode and PlayMode tests, benchmark runners, scheduling profiles, soak sampling, and result export for `CycloneGames.BehaviorTree`.

## Included parts

- `Editor` tests
  - semantic consistency checks
  - code-first builder structure and fail-fast checks
  - authoring graph compiler validation
  - composite/decorator edge-case checks
  - blackboard type-slot, observer, and serialization limit checks
  - blackboard schema fail-fast and network filtering checks
  - deterministic blackboard and snapshot checks
  - transactional blackboard delta validation
  - malformed network snapshot and delta rejection checks
  - DOD compiler fail-fast checks
  - observer-backed zero-allocation delta flush guard
  - editor-side benchmark baselines
- `PlayMode` tests
  - runtime benchmark runner smoke coverage
  - export format coverage
- benchmark runtime tools
  - reusable benchmark session
  - in-scene benchmark runner
  - CSV / JSON export helpers
- scale and complexity matrix support
- realistic scheduling profiles
- soak benchmark sampling
- editor tooling
  - benchmark control window
  - benchmark scene generation

## Scale presets

- `500 AI Battle`
- `1000 AI Crowd`
- `5000 AI Stress`
- `10000 AI Extreme`
- `100 Players + 500 AI`
- `Long Soak 1000 AI`

## Complexity tiers

- `Light`
- `Medium`
- `Heavy`

Each benchmark configuration selects two dimensions:

- scale preset
- complexity tier

## Scheduling profiles

- `FullRate`
  - every agent ticks every frame
  - best for small arena or boss-fight style validation
- `LodCrowd`
  - near agents tick every frame
  - mid agents tick at half rate
  - far agents tick at a reduced crowd rate
- `PriorityLod`
  - critical agents keep full rate
  - medium priority agents run at reduced cadence
  - ambient far agents run at sparse cadence
- `NetworkMixed`
  - a player-heavy front band ticks every frame
  - AI layers tick at mixed rates
  - deterministic hash checks run on an interval
- `FarCrowd`
  - more aggressive far-distance crowd reduction
  - useful for 5k+ background agents
- `UltraLod`
  - only a very small near set runs at full rate
  - meant for extreme crowd-presence validation
- `PriorityManaged`
  - benchmark-side approximation of priority-budgeted ticking
  - useful as a comparison baseline against full-rate and simpler LOD

Scheduling profiles add a third dimension:

- scale preset
- complexity tier
- scheduling profile

## How to run Editor tests

1. Open Unity.
2. Open `Window > General > Test Runner`.
3. Switch to `EditMode`.
4. Run assembly `CycloneGames.BehaviorTree.Tests.Editor`.

Main files:

- `Consistency/BehaviorTreeConsistencyTests.cs`
- `Consistency/BehaviorTreeCodeFirstTests.cs`
- `Consistency/BehaviorTreeAuthoringCompilerTests.cs`
- `Performance/BehaviorTreeBenchmarkTests.cs`

Editor tests cover success and failure paths. Remote payload tests assert that malformed input is rejected before local runtime state changes.

## How to run PlayMode tests

1. Open Unity Test Runner.
2. Switch to `PlayMode`.
3. Run assembly `CycloneGames.BehaviorTree.Tests.PlayMode`.

Main file:

- `PlayMode/BehaviorTreePlayModeBenchmarkTests.cs`

## How to use the benchmark panel

1. Open `Tools > CycloneGames > Behavior Tree > Behavior Tree Benchmark`.
2. Choose a scale preset, complexity tier, and scheduling profile, or adjust the benchmark configuration manually.
3. Use `Run Editor Benchmark` for a single editor-side benchmark pass.
4. Use `Run Scale Matrix For Selected Complexity` to compare all scale presets under the current complexity tier.
5. Use `Run Full Matrix (Scale x Complexity)` to run the complete preset-by-complexity benchmark matrix with each preset's recommended scheduling profile.
6. Use `Run PriorityManaged Comparison` to compare the current setup across `FullRate / PriorityLod / PriorityManaged / UltraLod`.
7. Use `Run Production Certification Matrix` to run the matrix named by the tool with its configured budgets.
8. Use `Create PlayMode Benchmark Scene` to generate a scene from the current config.
9. Use `Create Scene From Preset` to generate a scene from the selected preset and complexity tier.
10. Use `Create Scale Matrix Scene` to generate a PlayMode scene that runs all scale presets for the selected complexity tier.
11. Use `Create Full Matrix Scene` to generate a PlayMode scene that runs the full scale-by-complexity matrix automatically.
12. Use `Create PriorityManaged Comparison Scene` to generate a scene that auto-runs the scheduling comparison batch.
13. Use `Create Production Certification Scene` to generate a PlayMode scene that auto-runs the certification matrix.
14. Enter Play Mode to let the generated runner execute automatically.

Important config fields:

- `Deterministic Hash Check`
  - simulates periodic blackboard hash validation that is common in networked or deterministic verification flows
- `Hash Check Interval`
  - controls how often that validation runs
- `Soak Frames`
  - keeps the benchmark running after the measured phase so you can observe long-lived allocation or drift
- `Soak Sample Interval`
  - controls how often the soak phase samples managed memory
- `Production Budgets`
  - stores pass/fail thresholds for average frame time, max frame time, workload-scaled managed memory capacity, GC collections, and effective tick ratio; passing these thresholds is not Player or platform certification
  - `Max Memory Delta` is a session-level retained-memory capacity budget, not a per-frame allocation budget; use GC collection counts, delta flush allocation guards, and soak memory drift for hot-path allocation stability

PlayMode runner behavior:

- `Create PlayMode Benchmark Scene` and `Create Scene From Preset` create a single-run runner.
- `Create Scale Matrix Scene` creates a batch runner that executes all recommended scale presets for the selected complexity.
- `Create Full Matrix Scene` creates a batch runner that executes all recommended scale presets across `Light / Medium / Heavy`.
- `Create Production Certification Scene` creates a batch runner that executes the certification matrix, including heavy stress, network-mixed, and soak cases.
- generated runners auto-export CSV / JSON into `Application.persistentDataPath/BehaviorTreeBenchmarkResults`.
- if `Soak Frames > 0`, generated runners continue into soak mode before export.

Main files:

- `Runtime/PerformanceTest/BehaviorTreeBenchmarkModels.cs`
- `Runtime/PerformanceTest/BehaviorTreeBenchmarkSession.cs`
- `Runtime/PerformanceTest/BehaviorTreeBenchmarkRunner.cs`
- `Editor/BehaviorTreeBenchmarkWindow.cs`

## Exporting CSV / JSON

After a single benchmark completes in the benchmark window:

1. Click `Export Last Result as CSV` or `Export Last Result as JSON`.
2. Choose the target file path.
3. The window writes the file and reveals it in the file explorer.
4. Click `Export Last Result to Default Folder` to write both CSV and JSON into `Application.persistentDataPath/BehaviorTreeBenchmarkResults`.

After a matrix run completes:

1. Click `Export Last Matrix as CSV` or `Export Last Matrix as JSON`.
2. Each row or JSON item represents one scale, complexity, and scheduling-profile case.
3. Click `Export Last Matrix to Default Folder` to write both CSV and JSON into the default benchmark result folder.

## Key Result Fields

- `PotentialTicks`
  - theoretical ticks if every agent ran every frame
- `ExecutedTicks`
  - actual ticks after scheduling / LOD reduction
- `EffectiveTickRatio`
  - executed ticks divided by potential ticks
- `AverageActiveAgentsPerFrame`
  - average number of agents actually ticking per measured frame
- `PeakActiveAgentsPerFrame`
  - highest active-agent count observed in a measured frame
- `TotalHashChecks`
  - number of deterministic blackboard hash passes executed
- `PeakManagedMemoryBytes`
  - highest managed memory sample seen during the run
- `SoakManagedMemoryDeltaBytes`
  - peak managed memory growth since the soak baseline
- `ProductionBudgetPassed`
  - true only when average frame, max frame, managed memory delta, GC, and effective tick ratio budgets all pass
- `BudgetSummary`
  - human-readable pass/fail summary for the run

For PlayMode generated scenes, export is automatic. The runner logs the final file path in the Unity Console.

`BehaviorTreeBenchmarkExportUtility` is the shared serializer used by both tests and editor tooling.

## Suggested workflow

1. Use `EditMode` tests for fast regression checks while iterating on runtime code.
2. Use the benchmark window for quick tuning of both scale and complexity.
3. Use `Run Scale Matrix For Selected Complexity` when you want to answer “how far does this complexity tier scale?”
4. Use `Run Full Matrix (Scale x Complexity)` when you want a product-level comparison surface for engineering and design decisions.
5. Use `Run Production Certification Matrix` to evaluate the configured budgets, then repeat the workload in release Player builds on representative hardware.
6. Use generated PlayMode benchmark scenes to inspect frame behavior, LOD scheduling tradeoffs, and long-running soak scenarios before target-device validation.
7. Export CSV / JSON snapshots to compare benchmark runs over time or across hardware tiers.
