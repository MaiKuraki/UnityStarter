# CycloneGames.AIEnvQuery

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

Select one position from a set of candidates by additive, weighted rules.

Each rule is a separate object, so a query can be read, reordered, reweighted and tested one rule at a time. The module does not generate meaning for positions: it receives coordinates, scores them, and returns them in order.

Four concerns are excluded deliberately: pathfinding, projection onto a navigation surface, reachability validation and authoring assets. A candidate can be unreachable; rejecting it is the job of a test supplied by the caller.

## <a id="assemblies"></a>Assemblies and dependencies

| Assembly | Purpose | Consumer reference |
| --- | --- | --- |
| `CycloneGames.AIEnvQuery.Core` | Generators, tests and the scoring runner. | Explicit |
| `CycloneGames.AIEnvQuery.Tests.Editor` | Generator coverage, scoring order, determinism and allocation tests. | Test Runner only |

The core assembly has no dependencies and no engine references. Line of sight is a delegate, so the package does not reference physics and works wherever the caller can supply a probe.

## <a id="model"></a>Model

A run has three stages and a fixed amount of storage.

| Stage | Type | Behaviour |
| --- | --- | --- |
| Generation | `IEnvQueryGenerator` | Writes candidates into a buffer and returns how many were written. |
| Scoring | `IEnvQueryTest` | Adds a value in 0..1 for one candidate and carries a weight. |
| Ordering | `EnvQueryRunner` | Sums `value × weight`, sorts in place, returns the first N. |

The runner owns the buffers. `maxCandidates` fixes their length at construction, which also bounds one run's cost.

Scoring is additive. Each test contributes `value × weight` to the candidate's score, so removing a test removes only its contribution. The runner does not know which tests exist; it iterates the array it receives.

The context carries the inputs every test reads: the querier position, its normalised facing, the target position and whether a target exists.

## Extension points

Four seams exist for project logic, all of them ordinary interfaces or fields rather than callbacks the runner must know about.

| Seam | What it replaces | Typical use |
| --- | --- | --- |
| `IEnvQueryGenerator` | Where candidates come from | A level's marker table, a navigation volume, a server-side spawn list |
| `IEnvQueryFilter` | Nothing; filters run between generation and scoring | Reject unreachable points, disabled slots, points outside a region |
| `IEnvQueryTest` | How a candidate is scored | Distance to a cover edge, threat level at a point, custom cover quality |
| `EnvQueryPoint.Payload` | Nothing; the runner carries it untouched | Row index or identifier of the source point, so the winner maps back without a position lookup |
| `EnvQueryContext.UserData` | Nothing; the runner never reads it | Project data the tests need, so test instances can stay shared instead of per agent |

A filter is not a zero-weight test. A rejected candidate leaves the run entirely and cannot occupy a result slot; a test with weight zero merely stops contributing score.

Custom implementations must not allocate: they run inside the per-frame path, once per candidate for tests and filters.

### Heavy queries stay outside the runner

Two cost controls are deliberately absent: score shaping and time slicing. Both belong to policy, and both compose from the public seams.

Score shaping is a test. A curve that prefers the middle of a range, or squares a distance, is an `IEnvQueryTest` that writes the shaped value. The runner only multiplies by the weight, so no change is needed.

Time slicing is a work item. `CycloneGames.Scheduling` steps an `IWorkItem` under a budget; a work item can call `Generate` once and then score a fixed number of candidates per `Step()`, keeping the best so far. This skips the runner's sorting, so it is worth doing only when one query no longer fits in a frame.

Cheaper options come first: lower `maxCandidates`, or run the query every N frames per agent and reuse the last result. A sliced query trades ordering guarantees and extra code for a bounded per-frame cost.

## <a id="usage"></a>Usage

### 1. Minimal query

```csharp
var runner = new EnvQueryRunner(maxCandidates: 128);
var generator = new GridAroundQuerierGenerator(spacing: 1f, radius: 6f);
var tests = new IEnvQueryTest[] { new DistanceTest(range: 10f) };
var results = new EnvQueryPoint[1];

var context = new EnvQueryContext { QuerierX = 0f, QuerierY = 0f, QuerierZ = 0f };
int count = runner.Run(generator, tests, in context, results);
```

`results[0]` holds the candidate closest to the querier. `count` reports how many results were written and never exceeds `results.Length`.

### 2. Add a second rule

```csharp
context.SetForward(0f, 0f, 1f);

var tests = new IEnvQueryTest[]
{
    new DistanceTest(range: 10f, weight: 1f),
    new FacingTest(weight: 3f)
};
```

`FacingTest` returns 1 for a candidate straight ahead and 0 for one directly behind. With weight 3 its influence is three times that of the distance test. Adjusting influence requires no change in the runner.

### 3. Supply candidates from data

```csharp
var points = new EnvQueryPoint[] { new EnvQueryPoint(4f, 0f, 2f), new EnvQueryPoint(-3f, 0f, 8f) };
var generator = new PointListGenerator(points, maximumDistance: 20f);
```

The distance filter runs during generation, so candidates outside the radius never reach the scoring loop.

### 4. Score against a target and against visibility

```csharp
var context = new EnvQueryContext
{
    QuerierX = 0f, QuerierY = 0f, QuerierZ = 0f,
    TargetX = 12f, TargetY = 0f, TargetZ = 4f,
    HasTarget = true
};

var tests = new IEnvQueryTest[]
{
    new DistanceTest(range: 30f, weight: 1f, useTarget: true),
    new LineOfSightTest(trace: HasLineOfSight, weight: 2f)
};

static bool HasLineOfSight(float fromX, float fromY, float fromZ, float toX, float toY, float toZ)
{
    return true; // Replace with the caller's trace: physics, navigation mesh, baked visibility, server collision.
}
```

When `HasTarget` is false, a target-dependent test contributes nothing instead of measuring against the origin.

### 5. Read more than the best result

```csharp
var results = new EnvQueryPoint[4];
int count = runner.Run(generator, tests, in context, results);
for (int i = 0; i < count; i++)
{
    // results[i] is the i-th best candidate, score included.
}
```

Returning several results lets the caller apply a further tie-break, such as path length, without running the query again.

## <a id="semantics"></a>Semantics

- Tests write values in 0..1. Values outside the range still run, but weights stop being comparable between queries.
- A weight of zero disables a test without removing it from the array.
- Equal scores are ordered by candidate index, so a fixed generator and context produce identical output on every run.
- A generator that produces more candidates than `maxCandidates` writes only what the buffer holds; the remainder is discarded.
- One runner belongs to one thread. It keeps its buffers between runs and is not safe for concurrent use.
- A run allocates nothing. Generators and the built-in tests allocate nothing either.

## <a id="cost"></a>Cost

| Stage | Cost |
| --- | --- |
| Generation | Proportional to the candidates the generator scans. |
| Scoring | Candidates × tests. |
| Ordering | `n log n` in the candidate count, performed in place. |

Memory is fixed at construction: three arrays of `maxCandidates` entries for candidates, scores and ordering. Raising the candidate cap raises cost and memory together.

## <a id="api"></a>API

```csharp
EnvQueryRunner(int maxCandidates = 256);
int Run(IEnvQueryGenerator generator, IEnvQueryTest[] tests,
        in EnvQueryContext context, EnvQueryPoint[] results);
int Run(IEnvQueryGenerator generator, IEnvQueryFilter[] filters, IEnvQueryTest[] tests,
        in EnvQueryContext context, EnvQueryPoint[] results);
int MaxCandidates { get; }

GridAroundQuerierGenerator(float spacing, float radius, float verticalOffset = 0f);
PointListGenerator(EnvQueryPoint[] source, float maximumDistance = 0f);

DistanceTest(float range, float weight = 1f, bool useTarget = false, bool preferFarther = false);
FacingTest(float weight = 1f);
LineOfSightTest(EnvQueryLineOfSight trace, float weight = 1f, float eyeHeight = 0f);
```

## <a id="limits"></a>Limits

- No pathfinding or projection. A candidate can be unreachable; only a caller-supplied test can reject it.
- No authoring asset. A query is a set of instances constructed in code, next to the logic that runs it.
- No asynchronous or time-sliced execution. A run completes inside the calling frame; `maxCandidates` is the knob that keeps it there.
- No caching between runs. Every call regenerates and rescores; a caller that reuses a result set must hold it outside the runner.
