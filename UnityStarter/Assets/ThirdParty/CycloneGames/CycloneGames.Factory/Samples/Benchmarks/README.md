# CycloneGames.Factory Benchmarks

This folder contains benchmark and profiling entry points for the current `CycloneGames.Factory` architecture.

The benchmark suite is not only for micro-benchmarking method calls. Its main purpose is to answer architecture questions:

- Should this system stay in OOP pooling?
- Should it move to dense DOD storage?
- Is ECS reuse better than direct instantiate/destroy under this workload?
- Is the current capacity policy too strict or too loose?

## Benchmark Families

### Pure C#

Entry point:

- `Samples/Benchmarks/PureCSharp/Program.cs`

Covers:

- direct allocation
- factory creation
- `ObjectPool<TParam, TValue>`
- dense DOD handle-pool churn

### Unity / Runtime OOP

Main sample:

- `Samples/Benchmarks/Unity/GameObjectPoolBenchmark.cs`

Covers:

- `GameObject.Instantiate/Destroy`
- `ObjectPool<TParam, TValue>`
- prewarmed vs cold start behavior
- sustained active-count scenarios

### Unity / DOD

Main sample:

- `Samples/Benchmarks/Unity/HighDensityDODBenchmark.cs`

Covers:

- `NativePool<T>`
- `NativeDensePool<T>`
- `NativeDenseColumnPool2<T0, T1>`
- batched column-pool spawn/despawn

The DOD benchmark now logs unified profile vocabulary such as `CountActive`, `CountInactive`, and `PeakCountActive`.

### Unity / ECS

Main samples:

- `ECS/Samples/BulletSpawnerAuthoring.cs`
- `ECS/Samples/ECSHighLoadBenchmark.cs`

Covers:

- `PoolReuse`
- `DirectInstantiateDestroy`
- shared metrics vocabulary for current counts, peaks, and rejected spawns

## Recommended Workflow

1. Fix one target active count and one hard capacity.
2. Run the OOP, DOD, or ECS scenario with the same spawn pressure.
3. Compare frame time together with diagnostics, not separately.
4. Tune capacity policy only after reading peak and reject metrics.
5. If OOP pooling shows stable pressure at very high counts, test a DOD or ECS version next.

## Reading The Numbers

Use the metrics as a system:

- high `TotalCreated` with relatively stable `PeakCountActive` usually means reuse is underperforming
- non-zero `RejectedSpawns` usually means `HardCapacity` is lower than the true burst requirement
- a large gap between `PeakCountActive` and current `CountActive` usually means bursty traffic
- high `CountInactive` means memory is being retained for fast reuse, not freed

## Suggested Comparisons

### OOP

Compare:

- cold pool vs prewarmed pool
- trim-on-despawn vs manual trim
- `Throw` vs `ReturnNull` overflow behavior

### DOD

Compare:

- `NativePool<T>` vs `NativeDensePool<T>`
- one-struct dense layout vs columnar layout
- single spawn/despawn vs batch spawn/despawn

### ECS

Compare:

- `PoolReuse`
- `DirectInstantiateDestroy`

Keep these aligned between runs:

- spawn rate
- target active count
- hard capacity
- reporting interval

## Notes

- Benchmark results should be validated on target hardware, not only in editor.
- WebGL, mobile, and desktop should be profiled separately.
- For dense DOD and ECS scenarios, prioritize frame-time stability over average-only throughput.
