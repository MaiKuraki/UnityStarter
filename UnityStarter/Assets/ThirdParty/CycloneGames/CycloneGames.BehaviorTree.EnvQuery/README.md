# CycloneGames.BehaviorTree.EnvQuery

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

Run an environment query and publish the best candidate position into blackboard slots, so a tree reads the result through ordinary keys instead of holding a query object.

The bridge does not decide what makes a position good. Candidate generation and scoring belong to the query, which the caller configures once and keeps.

## <a id="assemblies"></a>Assemblies and dependencies

| Assembly | Purpose | Consumer reference |
| --- | --- | --- |
| `CycloneGames.BehaviorTree.Runtime.Integrations.EnvQuery` | Runs a query and publishes the best candidate. | Explicit |
| `CycloneGames.BehaviorTree.Integrations.EnvQuery.Tests.Editor` | Contract, fuzz, allocation and soak tests. | Test Runner only |

Both assemblies are `noEngineReferences: true`. The query core and the blackboard are engine-free, so the same adapter runs in a player and in a headless process.

## <a id="model"></a>Model

The bridge owns three things: the query runner, the ranked result array, and the generator and tests it was constructed with.

| Call | Effect |
| --- | --- |
| `Run(context)` | Generates candidates, applies every test, sorts in place, keeps the best `Capacity` entries. |
| `PublishBest` | Writes a `bool` for "a candidate was found" and the best score as a `float`. |
| `PublishBestPosition` | Writes the best position as `BTFloat3`. |
| `PublishCount` | Writes how many ranked candidates were kept. |

Ordering comes from the query: score descending, ties broken by candidate index. A fixed generator, tests and context therefore produce the same output every run, which is what makes a replay or a headless re-simulation agree with the client.

## <a id="usage"></a>Usage

### 1. Construct once

```csharp
var generator = new GridAroundQuerierGenerator(spacing: 1.5f, radius: 8f);
var tests = new IEnvQueryTest[]
{
    new DistanceTest(range: 15f, useTarget: true),
    new FacingTest(weight: 0.5f)
};

var bridge = new EnvQueryBlackboardBridge(generator, tests, maxCandidates: 64, resultCapacity: 4);
```

`maxCandidates` bounds the runner's buffers and the cost of one run. `resultCapacity` is how many ranked candidates the bridge keeps and can publish.

A fifth argument accepts `IEnvQueryFilter[]`. Filters run before scoring, so a rejected candidate never occupies a result slot:

```csharp
var filters = new IEnvQueryFilter[] { new ReachableFilter(navigation) };
var bridge = new EnvQueryBlackboardBridge(generator, tests, 64, 4, filters);
```

### 2. Build the context each frame

```csharp
var context = new EnvQueryContext
{
    QuerierX = transform.position.x,
    QuerierY = transform.position.y,
    QuerierZ = transform.position.z,
    TargetX = target.x,
    TargetY = target.y,
    TargetZ = target.z,
    HasTarget = hasTarget
};
context.SetForward(transform.forward.x, transform.forward.y, transform.forward.z);
```

`SetForward` normalises the vector, so a non-unit forward does not distort `FacingTest`.

### 3. Run and publish

```csharp
bridge.Run(in context);
bridge.PublishBest(blackboard, foundKey, scoreKey);
bridge.PublishBestPosition(blackboard, positionKey);
bridge.PublishCount(blackboard, countKey);
```

Call it on the thread that created the bridge. The runner keeps buffers between calls and is not safe to share.

### 4. Read the slots from the tree

```csharp
builder.Condition(bb => bb.GetBool(foundKey));
builder.Action(bb => bb.GetFloat(scoreKey) > 0.5f
    ? RuntimeState.Success
    : RuntimeState.Failure);
```

The position slot is a `BTFloat3`, which the Unity assembly converts to and from `Vector3` through extension methods.

## <a id="semantics"></a>Semantics

- When the generator returns nothing, `Run` reports zero, `PublishBest` writes `false` and a zero score, and the position slot receives `BTFloat3.Zero`.
- A test that depends on the target contributes nothing when `HasTarget` is false; it does not score against the origin.
- Publishing goes through `SetBool`, `SetFloat`, `SetFloat3` and `SetInt`. The blackboard drops unchanged writes, so a stable result neither notifies observers nor enters the network delta.
- `Run` overwrites the previous result. A caller that needs history copies the points out before the next call.
- `Dispose` releases the runner's buffers and clears the result array; the generator and tests stay with the caller.

## <a id="cost"></a>Cost

| Part | Cost |
| --- | --- |
| Generation | Proportional to the candidates the generator scans. |
| Scoring | Candidates × tests. |
| Ordering | `n log n` in the candidate count, in place. |
| `Publish*` | Constant time each, dominated by the blackboard write. |

Memory is fixed at construction: the runner's three arrays of `maxCandidates`, plus one result array of `resultCapacity`. Nothing allocates per run.

## <a id="api"></a>API

```csharp
EnvQueryBlackboardBridge(IEnvQueryGenerator generator, IEnvQueryTest[] tests,
                         int maxCandidates = 64, int resultCapacity = 4,
                         IEnvQueryFilter[] filters = null);

int Run(in EnvQueryContext context);

void PublishBest(RuntimeBlackboard blackboard, int foundKey, int scoreKey);
void PublishBestPosition(RuntimeBlackboard blackboard, int positionKey);
void PublishBestPayload(RuntimeBlackboard blackboard, int payloadKey);
void PublishCount(RuntimeBlackboard blackboard, int countKey);

int Count { get; }
int Capacity { get; }
bool HasResult { get; }
EnvQueryPoint Best { get; }
ReadOnlySpan<EnvQueryPoint> Results { get; }
```

## <a id="limits"></a>Limits

- No pathfinding and no projection. A candidate can be unreachable; only a test supplied by the caller can reject it.
- One bridge, one thread. Sharing a bridge between agents is not supported; give each agent its own, or run the query once and publish to a shared board.
- No result history. The array is overwritten every run.
- Cost grows with `maxCandidates`. A large candidate budget in a per-frame path should be justified by the query it buys, not by convenience.
