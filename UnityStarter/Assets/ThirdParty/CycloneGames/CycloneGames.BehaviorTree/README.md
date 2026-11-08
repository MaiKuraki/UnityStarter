# CycloneGames.BehaviorTree

[English | 简体中文](README.SCH.md)

A Unity behavior-tree runtime for agents. Managed node graph by default, flat Burst path when needed.

## <a id="what-is-a-bt"></a>What a behavior tree is

A behavior tree is a decision structure evaluated from the root on every tick. Leaves do work or test a condition. Internal nodes decide which leaves run and in what order. Every node returns `Success`, `Failure` or `Running`. `Running` is what makes the model fit agents: a node can span many frames without losing its place, and the next tick resumes at the same branch.

The same shape covers a patrol, a boss phase and an interaction flow. What changes is the leaf set, not the evaluation rule.

| Model | Strength | Cost |
| --- | --- | --- |
| Finite state machine | Explicit transitions; a handful of states is easy to read | Transition count grows with state pairs; reuse across agents is manual |
| Behavior tree | Composable subtrees, one evaluation rule, `Running` keeps multi-frame work simple | A careless tree re-checks conditions every tick |
| Goal-oriented planner | Handles combinations of actions nobody authored | Planning cost per decision; harder to bound and debug |
| Utility scoring | Smooth priority changes across many small options | Score tuning dominates; sequencing is awkward |

A behavior tree keeps authored control flow and still lets a subtree run under different parents. That is the trade it makes against a state machine: less explicit transition bookkeeping, more reuse.

## <a id="why-this-module"></a>Why this module exists

Most behavior-tree libraries optimize for authoring. This one optimizes for running many trees for a long time inside a commercial project, which changes the requirements:

- Per-tick work must be bounded, so one deep tree cannot take the frame.
- Steady-state execution must not allocate, because GC spikes show up in frame time.
- A tree must behave the same after a reload, a replay or a network round trip.
- Failures must be local and early. An invalid graph should fail at compile time, not halfway through a fight.

The module is split so each decision has one home.

| Layer | Problem it solves | Out of scope |
| --- | --- | --- |
| Authoring (optional) | Serializes a `BehaviorTree` asset and its layout in Edit Mode | Runtime state |
| Composition root | Builds or compiles a `RuntimeBehaviorTree`, owns lifetime, injects owner and services, mounts schedulers | Node semantics, transport policy |
| Managed runtime | Owns one acyclic `RuntimeNode` graph and one `RuntimeBlackboard`; defines activation, completion, abort, fault, reset and disposal | Threads and jobs |
| Reevaluation | Carries a monotone epoch from producers to the owner thread | Deciding what counts as an event |
| Scheduling | Bounds how many trees are scanned per pass | Frame pacing; the `MonoBehaviour` wrappers drive it |
| Serialization contracts | Produces and consumes bounded snapshot, delta and hash payloads | Transport, authority, ordering, rate limits, resynchronization |
| DOD runtime | Executes homogeneous flat trees in a Burst job with fixed slots | Managed nodes, observers, schemas, `object` values |

Two rules follow. The managed tree, its nodes, the runner components and the managed schedulers are owner-thread only; `WakeUp()` and `RequestReevaluation()` are the cross-thread entry points. The networking types encode and decode bytes; the product layer decides when to send them and what to do with the result.

## <a id="design-approaches"></a>How behavior trees are usually built

Four designs show up in production code. They differ in where the tree lives and when it re-evaluates.

| Design | Tree lives in | Re-evaluates | Good for |
| --- | --- | --- | --- |
| Node graph | Objects with parent and child links | Every tick, from the root | Hand-authored trees that change often |
| Table-driven | Rows in a config or asset | Every tick, from the root | Large content teams, bulk edits, localization |
| Event-driven | Node graph plus key observers | When a watched value changes | Expensive guards, many idle agents |
| Flat or data-oriented | Structs in contiguous arrays | Every tick, inside a job | Thousands of similar agents |

The first two are authoring choices, the last two are runtime choices. They combine: a table can generate a node graph, and an event-driven tree can still tick on a budget.

This module uses a node graph by default, adds event-driven reevaluation per decorator, and offers a flat Burst path for homogeneous crowds. All three share the blackboard and the scheduling contract, so a subtree can move to the flat path without rewriting the rest of the project.

## <a id="features"></a>What this module provides

- A managed runtime with explicit lifecycle, abort and fault handling. One tree is one object graph, with no hidden global state.
- A typed blackboard with an optional strict schema. Keys are ints, values are typed, and the hot path does not box.
- Bounded scheduling with a persistent cursor, per-bucket budgets and optional distance LOD.
- Event-driven reevaluation: a blackboard observer can request a rescan instead of polling every guard.
- Snapshot, delta and hash contracts for network synchronization, plus a separate package for authority and replication policy.
- An editor that opens `BehaviorTree` assets as a graph and shows live state, the active child index and elapsed time.
- Six CI gates that each finish in under a second: store fuzzing, scheduler fairness, short soak with pulses, depth limits, and two allocation checks.

What it does not provide: transport, authority resolution, prediction or rollback, perception, abilities or tags. Those live in separate packages. Chapter 13 shows how to connect them.

## <a id="authoring"></a>Two ways to author a tree

A tree can be built as a `BehaviorTree` asset or with code. Both end up as the same `RuntimeBehaviorTree`.

| Path | How | Good for |
| --- | --- | --- |
| Asset | Create a `BehaviorTree` asset, add nodes in the graph editor, wire parents and children, assign a blackboard schema, then point a `BTRunnerComponent` at it | Designers iterating on behaviour; trees that change between builds |
| Code | `RuntimeBehaviorTreeBuilder`, or direct node construction in tests | Generated trees, unit tests, tools, prototypes |
| Mixed | Asset for structure, code for custom leaf nodes | Project-specific actions that need typed references |

The editor stores node objects in the asset; `BehaviorTree.Compile` turns them into runtime nodes when the tree starts. The builder produces the same runtime node types directly. Tick cost depends on the nodes you use, not on how the tree was authored.

The examples below use the builder because it reads well in a document. Production trees are usually authored as assets.

The graph editor colours nodes by type, prints the runtime state on each node, and marks the active child index. The context menu adds action, condition, composite and decorator nodes without leaving the canvas.

## <a id="contents"></a>Contents

- [What a behavior tree is](#what-is-a-bt)
- [Why this module exists](#why-this-module)
- [How behavior trees are usually built](#design-approaches)
- [What this module provides](#features)
- [Two ways to author a tree](#authoring)
- [1. Environment and assembly boundaries](#environment)
- [2. Step 1: the smallest runnable tree](#step-1)
- [3. Step 2: blackboard and keys](#step-2)
- [4. Step 3: strict schema](#step-3)
- [5. Step 4: composite nodes](#step-4)
- [6. Step 5: decorators and conditions](#step-5)
- [7. Step 6: event-driven reevaluation](#step-6)
- [8. Step 7: tick ownership and scheduling](#step-7)
- [9. Step 8: LOD and the explicit composition root](#step-8)
- [10. Step 9: snapshot, delta, and the network contract](#step-9)
- [11. Step 10: the DOD Burst path](#step-10)
- [12. Composite example: a multi-layer tree](#composite-example)
- [13. Combining with other CycloneGames packages](#combining)
- [14. Performance, capacity and measurement](#performance)
- [15. API quick reference](#api)

## <a id="environment"></a>1. Environment and assembly boundaries

The project opens at `<repo-root>/UnityStarter`. The runtime is split into an engine-independent core and a Unity layer, so the same tree, the same blackboard and the same scheduling contract run in the editor, in a player, and in a headless .NET service.

| Assembly | Engine reference | Contents |
| --- | --- | --- |
| `CycloneGames.BehaviorTree.Core` | none (`noEngineReferences: true`) | `RuntimeBehaviorTree`, blackboard and schema, managed nodes, scheduling, `BTFloat3`, serialization contracts |
| `CycloneGames.BehaviorTree.Runtime` | Unity | `BehaviorTree` and `BTNode` ScriptableObjects, compiler, runner and manager components, LOD, `UnityBTContext` |
| `CycloneGames.BehaviorTree.Editor` | Unity Editor | graph window, inspectors, blackboard schema editing |

Dependencies point one way: `Runtime -> Core`. The core references `CycloneGames.Hash.Core` and `CycloneGames.Logging.Core` only, so a server that references the core needs no engine assembly.

Three optional assemblies are `autoReferenced: false` and must be referenced explicitly by a consumer asmdef:

- `CycloneGames.BehaviorTree.Runtime.DOD` is gated by `versionDefines` on `com.unity.burst`, `com.unity.collections`, and `com.unity.mathematics` and by matching `defineConstraints`. It references Unity.Burst, Unity.Collections, Unity.Mathematics, and Unity.Jobs.
- `CycloneGames.BehaviorTree.Integrations.DeterministicMath` is gated by a `versionDefine` on `com.cyclone-games.deterministic-math` in `[1.0.0,2.0.0)` producing `CYCLONEGAMES_HAS_DETERMINISTIC_MATH`, and by the matching `defineConstraint`.
- `CycloneGames.BehaviorTree.Benchmarks` holds the Editor and PlayMode measurement tooling.

A missing or unsupported optional package excludes the corresponding assembly; the core and Unity assemblies continue to compile. Capability symbols come from `versionDefines`, not from PlayerSettings.

### What the Unity layer adds

Two things cannot live in the core: a clock and component lookup. `UnityBTContext` supplies both, and a caller-registered service always wins over the built-in fallback.

```csharp
using CycloneGames.BehaviorTree.Core;
using CycloneGames.BehaviorTree.Runtime;
using UnityEngine;

public sealed class UnityContextUsage
{
    public RuntimeBehaviorTree Build(Component owner)
    {
        // GetOwner<T>() resolves through the GameObject; timed nodes read the injected clock.
        var context = new UnityBTContext(owner);
        return new RuntimeBehaviorTreeBuilder(context)
            .Action(_ => RuntimeState.Success)
            .Build();
    }
}
```

`BTFloat3` replaces `Vector3` in the core so the blackboard, snapshots and deltas stay engine-free. The Unity assembly adds `SetVector3`, `GetVector3` and `TryGetVector3` extension methods that convert without allocating, so Unity code keeps writing `Vector3`.

### Running without Unity

A headless host references `CycloneGames.BehaviorTree.Core` plus `CycloneGames.Hash.Core` and `CycloneGames.Logging.Core`. It creates the tree with the same builder and calls `Tick` itself: no `BTRunnerComponent`, no manager, no scene.

```csharp
using CycloneGames.BehaviorTree.Core;

public static void TickUntilStopped(RuntimeBehaviorTree tree)
{
    while (!tree.IsStopped)
    {
        tree.Tick();
    }
}
```

The clock comes from `IRuntimeBTTimeProvider` when one is registered and from a monotonic `Stopwatch` otherwise, so `RuntimeWaitNode` and the timeout decorators keep correct timing outside Unity. Logging goes through `CycloneGames.Logging`, which has no engine dependency. Chapter 2 builds the tree that this loop drives.

The shared ceilings are exposed as constants:

```csharp
using CycloneGames.BehaviorTree.Core;

public static class Step0
{
    public static void ReadLimits()
    {
        int maxDepth = BTCapacityLimits.MaxTreeDepth;             // 256
        int maxNodes = BTCapacityLimits.MaxNodeCount;             // 65536
        int defaultNodes = BTCapacityLimits.DefaultNodeCount;     // 4096
        int priorityLevels = BTCapacityLimits.PriorityLevelCount; // 8
        int defaultAgents = BTCapacityLimits.DefaultAgentCount;   // 65536
    }
}
```

`BTCapacityLimits` is consumed by managed graph validation, managed scheduling, and flat-tree validation, so the bounds cannot drift between execution paths.

## <a id="step-1"></a>2. Step 1: the smallest runnable tree

A tree is a `RuntimeBehaviorTree` built from a `RuntimeBehaviorTreeBuilder`. The builder has a fluent scope: a composite or decorator opens a scope, `.End()` closes it, and `Build()` closes any remaining scopes. A tree has exactly one root child, so a single `.Action(...)` is the smallest complete tree.

```csharp
using CycloneGames.BehaviorTree.Core;

public static class Step1
{
    public static void Run()
    {
        RuntimeBehaviorTree tree = new RuntimeBehaviorTreeBuilder()
            .Action(_ => RuntimeState.Success, "Complete")
            .Build();

        tree.Terminated += state => UnityEngine.Debug.Log($"Terminated: {state}");

        RuntimeState result = tree.Tick();  // Success
        bool stopped = tree.IsStopped;     // true

        tree.Dispose();
    }
}
```

`Tick()` starts the root activation as needed and returns `RuntimeState.Running`, `Success`, or `Failure`. `Success` and `Failure` are terminal results: the tree sets `IsStopped`, clears scheduling wake-up state, and publishes `Terminated` exactly once. `Play()` begins a new activation on a stopped tree; `Stop()` aborts the active node stack and publishes `Terminated(NotEntered)`. Lifecycle operations reject reentrant calls. A builder is single-use: `Build()` freezes it and rejects a tree without a root child. `Dispose()` aborts active work, disposes node-owned resources, disposes the owned blackboard, and removes termination subscribers; after disposal, public operations throw `ObjectDisposedException`.

## <a id="step-2"></a>3. Step 2: blackboard and keys

`RuntimeBlackboard` holds eight independent typed stores: `int`, `float`, `bool`, `Float3`, `long`, `Long2`, `Long3`, and `object`. `Float3` is the engine-free triple; the Unity assembly adds `Vector3` overloads. Typed access avoids boxing primitive values. A key is an `int` hash; `DefaultStringHashFunc` is stable `BTHash.FNV1A`, so string-key overloads are a convenience that hashes on each call. Pre-hash frequently used keys once.

```csharp
using UnityEngine;
using CycloneGames.BehaviorTree.Core;

public static class Step2
{
    private static readonly int StageKey =
        RuntimeBlackboard.DefaultStringHashFunc("Stage");
    private static readonly int FocusKey =
        RuntimeBlackboard.DefaultStringHashFunc("Focus");
    private static readonly int ActiveKey =
        RuntimeBlackboard.DefaultStringHashFunc("Active");
    private static readonly int PositionKey =
        RuntimeBlackboard.DefaultStringHashFunc("Position");
    private static readonly int SequenceKey =
        RuntimeBlackboard.DefaultStringHashFunc("Sequence");
    private static readonly int PairKey =
        RuntimeBlackboard.DefaultStringHashFunc("Pair");
    private static readonly int TripleKey =
        RuntimeBlackboard.DefaultStringHashFunc("Triple");
    private static readonly int ReferenceKey =
        RuntimeBlackboard.DefaultStringHashFunc("Reference");

    public static void Run()
    {
        var shared = new RuntimeBlackboard();
        shared.SetInt(StageKey, 3);

        // Parent chain: reads fall back through Parent; writes affect only the local board.
        var local = new RuntimeBlackboard(parent: shared);
        int fromParent = local.GetInt(StageKey);          // 3
        local.SetInt(StageKey, 4);                        // local only
        int parentValue = shared.GetInt(StageKey);        // still 3

        // Eight typed stores.
        local.SetFloat(FocusKey, 1.5f);
        local.SetBool(ActiveKey, true);
        local.SetFloat3(PositionKey, new BTFloat3(1f, 2f, 3f));
        local.SetLong(SequenceKey, 42L);
        local.SetLong2(PairKey, new RuntimeBlackboardLong2(1L, 2L));
        local.SetLong3(TripleKey, new RuntimeBlackboardLong3(1L, 2L, 3L));
        local.SetObject(ReferenceKey, shared);

        // String-key convenience hashes with DefaultStringHashFunc.
        local.SetInt("Stage", 7);
        int viaString = local.GetInt("Stage");

        // Stamps change only when the local value actually changes.
        ulong stamp0 = local.GetStamp(StageKey);
        local.SetInt(StageKey, 8);
        ulong stamp1 = local.GetStamp(StageKey);
        local.SetInt(StageKey, 8);
        ulong stamp2 = local.GetStamp(StageKey);         // equals stamp1

        // Observers run synchronously after the write commits.
        BlackboardObserverCallback onStage = (key, bb) =>
            Debug.Log($"Stage changed: {bb.GetInt(key)}");
        local.AddObserver(StageKey, onStage);
        local.AddGlobalObserver((key, bb) => { });
        local.SetInt(StageKey, 9);
        local.RemoveObserver(StageKey, onStage);
        local.ClearAllObservers();

        // Restart the per-blackboard deterministic random stream.
        local.ResetDefaultRandom(seed: 1234u);

        // Precise type probing without a default fallback.
        bool hasInt = local.TryGetInt(StageKey, out int probed);

        local.Dispose();
        shared.Dispose();
    }
}
```

Reads walk `Parent`; writes never do. `GetStamp` returns a `ulong` that advances only when a local value changes, which gives low-cost change detection. `AddObserver`, `AddGlobalObserver`, `RemoveObserver`, `RemoveGlobalObserver`, and `ClearAllObservers` manage a callback set; subscription changes allocate new callback arrays, so register during setup and remove at owner shutdown. `RuntimeBlackboard.ResetDefaultRandom(uint seed = 0u)` restarts the deterministic stream that randomized nodes use when no `IRuntimeBTRandomProvider` is supplied; unseeded random nodes do not depend on process-global state. `EnableConcurrentStorageAccess()` installs a `ReaderWriterLockSlim` over the value table; it protects the table only, not `Parent`, `Context`, tree execution, or Unity objects.

## <a id="step-3"></a>4. Step 3: strict schema

A `RuntimeBlackboardSchema` turns key existence, type, defaults, and synchronization flags into an explicit contract. `RuntimeBlackboardSchemaBuilder` builds one; binding it to a blackboard applies defaults and makes writes validate key existence and type.

```csharp
using System.IO;
using UnityEngine;
using CycloneGames.BehaviorTree.Core;

public static class Step3
{
    private static readonly int StageKey =
        RuntimeBlackboard.DefaultStringHashFunc("Stage");

    public static void Run()
    {
        RuntimeBlackboardSchema schema = new RuntimeBlackboardSchemaBuilder()
            .AddInt("Stage", 0, RuntimeBlackboardSyncFlags.SnapshotAndDelta)
            .AddBool("EventActive", false, RuntimeBlackboardSyncFlags.LocalOnly)
            .AddFloat("Focus", 0f, RuntimeBlackboardSyncFlags.Snapshot)
            .AddFloat3("Position", BTFloat3.Zero, RuntimeBlackboardSyncFlags.Delta)
            .AddLong("Sequence", 0L, RuntimeBlackboardSyncFlags.LocalOnly)
            .AddObject("Reference")
            .Build();

        var blackboard = new RuntimeBlackboard(schema: schema);

        // Defaults are applied at construction.
        int stage = blackboard.GetInt(StageKey);   // 0

        // Schema-bound writes validate the key and its type.
        blackboard.SetInt(StageKey, 2);
        blackboard.ResetToSchemaDefaults();       // Stage back to 0

        // Inspect the contract.
        bool known = schema.IsKnownKey(StageKey);
        bool delta = schema.UsesDelta(StageKey);
        bool snapshot = schema.UsesSnapshot(StageKey);
        int contractVersion = schema.ContractVersion;

        // Bounded snapshot payload; returns the captured revision.
        ulong revision;
        using (var stream = new MemoryStream())
        {
            using (var writer = new BinaryWriter(stream))
            {
                revision = blackboard.WriteTo(
                    writer, RuntimeBlackboardNetworkScope.Snapshot, maxBytes: 8192);
                writer.Flush();
            }
        }

        ulong hash = blackboard.ComputeHash(RuntimeBlackboardNetworkScope.Networked);

        blackboard.Dispose();
    }
}
```

`RuntimeBlackboardSyncFlags` is `LocalOnly`, `Snapshot`, `Delta`, and `SnapshotAndDelta`. `SnapshotAndDelta` marks a key for both the full-snapshot scope and the delta path. `Object` keys are always `LocalOnly` and cannot carry an authoring default; `AddObject` injects references through code. A write to an undeclared key raises `KeyNotFoundException`; a type mismatch raises `InvalidOperationException`. `RuntimeBlackboardNetworkScope` is `Snapshot` (keys marked for full snapshots) or `Networked` (every non-local primitive, including delta-only keys). `WriteTo` also has a `maxBytes` overload that writes nothing when the computed size exceeds the bound and returns the blackboard revision captured under the same lock. `ResetToSchemaDefaults()` replaces local values atomically; an unbound schema resets to an empty local board.

Use `WithBlackboardSchema(schema, applyDefaults)` on the builder when the tree should own the schema, or pass a pre-built blackboard with `WithBlackboard(...)`. Both are composition-root decisions.

## <a id="step-4"></a>5. Step 4: composite nodes

Composites own an ordered child array. `Seal()` converts the setup-time list into that array once; execution reads the sealed array. The builder opens a composite scope for each of these methods: `Sequence`, `Selector`, `SequenceWithMemory`, `SelectorRandom`, `ReactiveSequence`, `ReactiveFallback`, `Parallel`, `ParallelAll`, `IfThenElse`, `WhileDoElse`, `Switch`, `ProbabilityBranch`, and `UtilitySelector`.

| Composite | Semantics |
| --- | --- |
| `RuntimeSequencer` (`Sequence`) | Children left to right; first `Failure` fails; all `Success` succeeds. |
| `RuntimeSelector` (`Selector`) | Children left to right; first `Success` succeeds; all `Failure` fails. Also owns abort scans driven by `AbortType`. |
| `RuntimeSequenceWithMemory` (`SequenceWithMemory`) | Skips already-succeeded children and resumes from the last running one. |
| `RuntimeSelectorRandom` (`SelectorRandom`) | Shuffles a setup-time index array and then applies selector semantics. |
| `RuntimeReactiveSequence` / `RuntimeReactiveFallback` (`ReactiveSequence` / `ReactiveFallback`) | Rescan guards from the head each step and abort a displaced running branch. |
| `RuntimeIfThenElseNode` (`IfThenElse`) | Children are `[condition, then, else]`. |
| `RuntimeWhileDoElseNode` (`WhileDoElse`) | Children are `[condition, body, else]`. |
| `RuntimeSwitchNode` (`Switch`) | Uses a blackboard `int` as a zero-based case; the last child is the default. |
| `RuntimeProbabilityBranch` (`ProbabilityBranch`) | Selects one child per activation from validated non-negative weights. |
| `RuntimeUtilitySelector` (`UtilitySelector`) | Reads one blackboard `float` score per child and runs the highest-scoring one. |
| `RuntimeParallelNode` / `RuntimeParallelAllNode` (`Parallel` / `ParallelAll`) | Interleave branches on the owner thread; see the finish policy below. |

```csharp
using CycloneGames.BehaviorTree.Core;
using CycloneGames.BehaviorTree.Core.Nodes.Compositors;

public static class Step4
{
    private static readonly int PhaseKey =
        RuntimeBlackboard.DefaultStringHashFunc("Phase");
    private static readonly int GateKey =
        RuntimeBlackboard.DefaultStringHashFunc("Gate");
    private static readonly int ScoreAKey =
        RuntimeBlackboard.DefaultStringHashFunc("ScoreA");
    private static readonly int ScoreBKey =
        RuntimeBlackboard.DefaultStringHashFunc("ScoreB");

    public static void Run()
    {
        RuntimeBehaviorTree tree = new RuntimeBehaviorTreeBuilder()
            .Selector(selector => selector.AbortType = RuntimeAbortType.LowerPriority)
                .Sequence()
                    .Condition(bb => bb.GetInt(PhaseKey) >= 0, "PhaseReady")
                    .Action(bb => RuntimeState.Success, "Advance")
                .End()
                .SequenceWithMemory()
                    .Action(bb => RuntimeState.Success, "Prepare")
                    .Action(bb => RuntimeState.Success, "Commit")
                .End()
                .ReactiveFallback(fallback =>
                {
                    fallback.AbortType = RuntimeAbortType.LowerPriority;
                    fallback.ReevaluationMode = RuntimeReevaluationMode.Hybrid;
                    fallback.ReevaluationPollInterval = 4;
                })
                    .Condition(bb => bb.GetBool(GateKey), "Gate")
                    .Action(bb => RuntimeState.Running, "Observe")
                .End()
                .IfThenElse()
                    .Condition(bb => bb.GetBool(GateKey), "BranchGate")
                    .Action(bb => RuntimeState.Success, "Then")
                    .Action(bb => RuntimeState.Success, "Else")
                .End()
                .Switch(PhaseKey)
                    .Action(bb => RuntimeState.Success, "Case0")
                    .Action(bb => RuntimeState.Success, "Case1")
                    .Action(bb => RuntimeState.Success, "Default")
                .End()
                .UtilitySelector(new[] { ScoreAKey, ScoreBKey })
                    .Action(bb => RuntimeState.Success, "OptionA")
                    .Action(bb => RuntimeState.Success, "OptionB")
                .End()
                .ProbabilityBranch(new[] { 1f, 3f })
                    .Action(bb => RuntimeState.Success, "LowWeight")
                    .Action(bb => RuntimeState.Success, "HighWeight")
                .End()
                .WhileDoElse()
                    .Condition(bb => bb.GetBool(GateKey), "LoopGuard")
                    .Action(bb => RuntimeState.Success, "Body")
                    .Action(bb => RuntimeState.Success, "Exit")
                .End()
            .End()
            .Build();

        tree.Dispose();
    }

    public static void FinishPolicy()
    {
        // Parallel finish policy is expressed by RuntimeParallelMode,
        // plus thresholds on RuntimeParallelAllNode.
        RuntimeBehaviorTree parallel = new RuntimeBehaviorTreeBuilder()
            .Parallel(RuntimeParallelMode.UntilAnySuccess)
                .Action(bb => RuntimeState.Success, "CompleteA")
                .Action(bb => RuntimeState.Running, "HoldB")
            .End()
            .Build();

        RuntimeBehaviorTree thresholds = new RuntimeBehaviorTreeBuilder()
            .ParallelAll(all => all.SuccessThreshold = 2)
                .Action(bb => RuntimeState.Success, "A")
                .Action(bb => RuntimeState.Success, "B")
                .Action(bb => RuntimeState.Success, "C")
            .End()
            .Build();

        parallel.Dispose();
        thresholds.Dispose();
    }
}
```

`RuntimeParallelMode` is `Default`, `UntilAnyComplete`, `UntilAnyFailure`, and `UntilAnySuccess`. `RuntimeParallelNode` retains each child's terminal state and does not rerun that child within one activation. `RuntimeParallelAllNode` ticks every unfinished child once per step; `SuccessThreshold` and `FailureThreshold` accept `-1` for all children or a value from `1` through the child count, and failure takes precedence when both thresholds are reached in one step. Managed parallel nodes interleave branches inside one owner-thread tick; they do not create threads, tasks, or jobs. `RuntimeCompositeNode` also exposes `ReevaluationMode`, `ReevaluationPollInterval`, and `AbortType`, which are the subject of Step 6.

## <a id="step-5"></a>6. Step 5: decorators and conditions

Decorators wrap exactly one child. The builder has direct helpers for `Inverter`, `Succeeder`, `ForceFailure`, `Repeat`, `RepeatForever`, `Retry`, `Timeout`, `Delay`, `CoolDown`, `RunOnce`, `KeepRunningUntilFailure`, `WaitSuccess`, `Wait`, and `WaitRandom`. For a node that needs its own configuration, use the generic `Decorator<TNode>(node, configure)` overload, which invokes `configure` before the node is added and before the tree owns it.

`BBComparisonNode` is the authored ScriptableObject decorator whose `BehaviorTreeComparisonValueType` selects `Int`, `Float`, `Bool`, or `Object`. Its runtime counterpart is `RuntimeBBComparisonNode`, with `BBComparisonOp` (`Equal`, `NotEqual`, `GreaterThan`, `GreaterOrEqual`, `LessThan`, `LessOrEqual`, `IsSet`, `IsNotSet`) and typed reference values. `SubTreeNode` / `RuntimeSubTreeNode` runs a child against an owned scoped blackboard with directional port remapping. `RuntimeServiceNode` runs a periodic side-effect callback while its child executes.

```csharp
using CycloneGames.BehaviorTree.Core;
using CycloneGames.BehaviorTree.Core.Nodes.Decorators;

public sealed class AdvanceCommand : IRuntimeBTCommand
{
    public RuntimeState Execute(RuntimeBlackboard blackboard)
    {
        return RuntimeState.Success;
    }
}

public sealed class StageGate : IRuntimeBTConditionStrategy
{
    public bool Evaluate(RuntimeBlackboard blackboard)
    {
        return true;
    }
}

public static class Step5
{
    private static readonly int PhaseKey =
        RuntimeBlackboard.DefaultStringHashFunc("Phase");
    private static readonly int LocalPhaseKey =
        RuntimeBlackboard.DefaultStringHashFunc("LocalPhase");
    private static readonly int ParentPhaseKey =
        RuntimeBlackboard.DefaultStringHashFunc("ParentPhase");

    public static void Run()
    {
        RuntimeBehaviorTree tree = new RuntimeBehaviorTreeBuilder()
            .Sequence()
                .Inverter()
                    .Condition(new StageGate(), "Negated")
                .End()
                .Repeat(3)
                    .Action(bb => RuntimeState.Success, "RepeatBody")
                .End()
                .CoolDown(0.5f)
                    .Action(bb => RuntimeState.Success, "RateLimited")
                .End()
                .Timeout(2f)
                    .Action(bb => RuntimeState.Running, "BoundedWork")
                .End()
                .Decorator(new RuntimeBBComparisonNode(), comparison =>
                {
                    comparison.KeyHash = PhaseKey;
                    comparison.ValueType = BehaviorTreeComparisonValueType.Int;
                    comparison.Operator = BBComparisonOp.GreaterOrEqual;
                    comparison.RefInt = 1;
                    comparison.AbortMode = RuntimeAbortType.Self;
                })
                    .Command(new AdvanceCommand(), "GuardedAdvance")
                .End()
                .Decorator(new RuntimeSubTreeNode(), subtree =>
                {
                    subtree.SetPortRemapping(
                        new[] { LocalPhaseKey },
                        new[] { ParentPhaseKey },
                        new byte[] { 1 },
                        new[] { RuntimeSubTreePortDirection.InOut });
                })
                    .Action(bb => RuntimeState.Success, "ScopedWork")
                .End()
            .End()
            .Build();

        tree.Dispose();
    }
}
```

Named `IRuntimeBTCommand` and `IRuntimeBTConditionStrategy` objects avoid captured lambdas and are reusable across trees. Pre- and post-conditions are also available on any `RuntimeNode` through `AddPreCondition` and `AddPostCondition`, with `ConditionPolicy.FailWhenFalse`, `SucceedWhenFalse`, and `AbortWhenFalse`. Time-sensitive managed nodes resolve `IRuntimeBTTimeProvider` from the runtime context; randomized nodes resolve `IRuntimeBTRandomProvider` when supplied, otherwise they use the per-blackboard deterministic stream.

## <a id="step-6"></a>7. Step 6: event-driven reevaluation

`RuntimeBTEvaluationController` owns one monotone epoch per tree. `RequestReevaluation()` is an `Interlocked.Increment`; concurrent requests from any thread coalesce into one counter, so a bursty external event stream creates no queue, no allocation, and no backlog. The owner thread consumes the epoch during tick through `ShouldReevaluate(ref lastSeenEpoch, ref pollCounter, mode, pollInterval)`.

- `RuntimeReevaluationMode.PollEveryTick` scans guards unconditionally. Use it when guards are cheap and must never miss a frame.
- `RuntimeReevaluationMode.OnChange` scans only after a request. Use it when a producer can signal a relevant change.
- `RuntimeReevaluationMode.Hybrid` scans on a request and also on `ReevaluationPollInterval`. Use it when events are the primary signal but a bounded periodic rescan is still wanted.

`RuntimeCompositeNode.ReevaluationMode` and `ReevaluationPollInterval` select the mode per composite; `RuntimeReactiveSequence` and `RuntimeReactiveFallback` advance only the active branch on idle ticks under `OnChange` or `Hybrid`. `RuntimeBBComparisonNode.AbortMode` is a property of type `RuntimeAbortType` (`None`, `Self`, `LowerPriority`, `Both`): a non-`None` value binds blackboard observers at graph commit, and a tracked key change calls `RuntimeBehaviorTree.RequestReevaluation()`, which increments the epoch and schedules an immediate wake-up. `RuntimeNode.EmitWakeUpSignal()` is the protected helper a node calls to request reevaluation from inside execution.

```csharp
using CycloneGames.BehaviorTree.Core;
using CycloneGames.BehaviorTree.Core.Nodes.Decorators;

public sealed class WakeUpOnSignalNode : RuntimeNode
{
    private readonly int _signalKey;

    public WakeUpOnSignalNode(int signalKey) => _signalKey = signalKey;

    protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
    {
        if (blackboard.GetBool(_signalKey))
        {
            EmitWakeUpSignal();
            return RuntimeState.Success;
        }

        return RuntimeState.Running;
    }
}

public static class Step6
{
    private static readonly int GateKey =
        RuntimeBlackboard.DefaultStringHashFunc("Gate");

    public static void Run()
    {
        RuntimeBehaviorTree tree = new RuntimeBehaviorTreeBuilder()
            .Selector(selector =>
            {
                selector.AbortType = RuntimeAbortType.Both;
                selector.ReevaluationMode = RuntimeReevaluationMode.OnChange;
            })
                .Decorator(new RuntimeBBComparisonNode(), comparison =>
                {
                    comparison.KeyHash = GateKey;
                    comparison.ValueType = BehaviorTreeComparisonValueType.Bool;
                    comparison.Operator = BBComparisonOp.Equal;
                    comparison.RefBool = true;
                    comparison.AbortMode = RuntimeAbortType.Both;
                })
                    .Action(bb => RuntimeState.Running, "GuardedWork")
                .End()
                .Node(new WakeUpOnSignalNode(GateKey))
            .End()
            .Build();

        // Producer side: requests coalesce into one epoch counter.
        int epochBefore = tree.Evaluation.Epoch;
        tree.Evaluation.RequestReevaluation();
        int epochAfter = tree.Evaluation.Epoch;

        // Schedule bounded immediate owner-thread ticks.
        tree.WakeUp(boostedTicks: 2);

        // A tracked key change also calls tree.RequestReevaluation() through the observer.
        tree.Blackboard.SetBool(GateKey, true);

        RuntimeState state = tree.Tick();
        tree.Dispose();
    }
}
```

`RuntimeBehaviorTree.RequestReevaluation(int boostedTicks = 1)` is the combined entry point: it increments the epoch and calls `RequestWakeUp`. `WakeUp(int boostedTicks = 1)` only carries the bounded immediate-tick budget. `ShouldTick()` consumes one boosted tick and resets the tick-interval counter; `WakeUpTickBudget` and `HasWakeUpRequest` expose the pending state. Choose polling when no reliable producer exists or when guard cost is negligible; choose events when changes are observable and tick work must be bounded. `Hybrid` keeps a periodic rescan alongside events.

## <a id="step-7"></a>8. Step 7: tick ownership and scheduling

`TickMode` on `BTRunnerComponent` has four values:

| Mode | Owner | Driver |
| --- | --- | --- |
| `Self` | `BTRunnerComponent.Update` | One component tick opportunity per frame. |
| `Manual` | Caller | `ManualTick()`. |
| `Managed` | `BTTickManagerComponent` | Bounded round-robin scan. |
| `PriorityManaged` | `BTPriorityTickManagerComponent` | Eight priority buckets plus optional distance LOD. |

Underneath the components, `BTTickManager` and `BTPriorityTickManager` are pure C# classes with no Unity logging dependency. `BTTickManager.TickBudget` counts trees scanned in one pass; a scanned tree may still skip execution because of `TickInterval`. `BTPriorityTickManager` holds eight buckets and an `int[]` budget per bucket; `SetBudgets(int[])` updates them and zero disables a bucket. Register, unregister, and priority changes requested during `Tick()` are queued per tree and applied after the pass, so a node callback cannot mutate the collection mid-scan. Terminal trees are removed automatically.

```csharp
using UnityEngine;
using CycloneGames.BehaviorTree.Core;
using CycloneGames.BehaviorTree.Runtime.Components;

public static class Step7
{
    public static RuntimeBehaviorTree BuildHoldTree()
    {
        return new RuntimeBehaviorTreeBuilder()
            .WithTickInterval(2)
            .Selector()
                .Action(bb => RuntimeState.Running, "Hold")
            .End()
            .Build();
    }

    public static void ConfigureRunner(BTRunnerComponent runner)
    {
        runner.SetTickMode(TickMode.Managed);
        runner.SetTickInterval(2);
        runner.WakeUp(boostedTicks: 2);
        runner.Pause();
        runner.Resume();
        runner.Stop();
        runner.Play();
    }

    public static void Schedule()
    {
        RuntimeBehaviorTree a = BuildHoldTree();
        var roundRobin = new BTTickManager(initialCapacity: 64);
        roundRobin.TickBudget = 32;
        if (roundRobin.TryRegister(a))
        {
            roundRobin.Tick();       // deferred mutations applied at pass end
        }
        roundRobin.Unregister(a);
        a.Dispose();

        RuntimeBehaviorTree b = BuildHoldTree();
        var priority = new BTPriorityTickManager(
            new[] { 64, 32, 16, 8, 4, 2, 1, 1 });
        priority.TryRegister(b, priority: 2);
        priority.Tick();
        priority.UpdatePriority(b, 0);
        priority.Unregister(b);
        b.Dispose();

        RuntimeBehaviorTree c = BuildHoldTree();
        BTTickManagerComponent managed = BTTickManagerComponent.CreateInstance();
        managed.TickBudget = 128;
        managed.Register(c);
        managed.Unregister(c);
        c.Dispose();

        RuntimeBehaviorTree d = BuildHoldTree();
        BTPriorityTickManagerComponent priorityManaged =
            BTPriorityTickManagerComponent.CreateInstance();
        priorityManaged.Register(d, priorityManaged.transform);
        priorityManaged.Unregister(d);
        d.Dispose();
    }
}
```

`RuntimeBehaviorTree.TickInterval` is settable from the owner thread; `ShouldTick()` gates execution on it, and a `WakeUp` boost bypasses the interval for a bounded number of owner-thread ticks. `BTCapacityLimits.DefaultAgentCount` is `65536` and `MaxAgentCount` is `1048576`; managers reject configuration above the hard ceiling. `TryRegister` returns `false` when tree or deferred-mutation capacity is unavailable, while the `void` wrappers delegate to the `Try*` path. `BTTickManagerComponent.Instance` and `BTPriorityTickManagerComponent.Instance` resolve a scene-authored manager without creating one; `CreateInstance()` is the explicit composition-root entry point for a persistent manager.

## <a id="step-8"></a>9. Step 8: LOD and the explicit composition root

A managed-mode runner requires a manager component to be present: either mounted explicitly on a GameObject in the scene, or created from a composition root with `BTTickManagerComponent.CreateInstance()` / `BTPriorityTickManagerComponent.CreateInstance()`. `Instance` only resolves an existing scene component; it does not allocate. When no manager is present, a runner in a managed tick mode ticks locally in `Update()` and emits one warning, so managed scheduling is inactive until a manager is mounted.

`BTLODConfig` maps strictly increasing distances to a `TickInterval` and a priority bucket from `0` through `7`, with `PriorityBudgets`, `BoostedPriority`, and `BoostedTickInterval`. `BTDistanceLODProvider` is an optional component mounted on the same GameObject as the priority manager: `Initialize()` uses `TryGetComponent` to find it. Without that provider, the manager keeps priority scheduling and uses each tree's own tick interval.

```csharp
using UnityEngine;
using CycloneGames.BehaviorTree.Core;
using CycloneGames.BehaviorTree.Runtime.Components;

public static class Step8
{
    public static void Run(Transform reference)
    {
        RuntimeBehaviorTree tree = new RuntimeBehaviorTreeBuilder()
            .Action(bb => RuntimeState.Running, "Hold")
            .Build();

        // Option A: explicit composition-root creation for Managed mode.
        BTTickManagerComponent managed = BTTickManagerComponent.CreateInstance();
        bool hasManaged = BTTickManagerComponent.HasInstance;
        managed.TickBudget = 64;

        // Option B: explicitly mounted priority manager with the optional LOD provider.
        var host = new GameObject("[BTPriorityTickManager]");
        host.AddComponent<BTDistanceLODProvider>();
        BTPriorityTickManagerComponent priority =
            host.AddComponent<BTPriorityTickManagerComponent>();

        BTLODConfig config = ScriptableObject.CreateInstance<BTLODConfig>();
        config.PriorityBudgets = new[] { 64, 32, 16, 8 };
        config.BoostedPriority = 0;
        config.BoostedTickInterval = 1;
        priority.Config = config;
        priority.SetReferencePoint(reference);
        priority.Register(tree, reference);
        priority.BoostPriority(tree, 0.5f);

        priority.Unregister(tree);
        Object.Destroy(config);
        Object.Destroy(host);
        tree.Dispose();
    }
}
```

Mounting the optional `BTDistanceLODProvider` before adding the manager component matters: the manager resolves it once during its own initialization and then keeps the reference. `BTLODConfig.TryValidate(out string error)` checks finite strictly increasing distances, tick intervals of at least one, valid priorities, non-negative budgets, and a budget entry for every used priority. Distance checks use pre-squared thresholds. `BTPriorityTickManagerComponent.CreateInstance()` is the equivalent composition-root call when no scene object is authored; it returns the existing instance rather than creating a duplicate.

## <a id="step-9"></a>10. Step 9: snapshot, delta, and the network contract

`RuntimeBlackboard.WriteTo`, `ReadFrom`, and `ComputeHash` are the primitive contracts. `RuntimeBlackboardNetworkScope` selects the visibility boundary: `Snapshot` includes keys marked for full snapshots, `Networked` includes every non-local primitive including delta-only keys. `RuntimeBlackboardSyncFlags.SnapshotAndDelta` from Step 3 determines which keys participate in which scope. `Object` keys never enter a payload.

The `CycloneGames.BehaviorTree.Core.Networking` types are contract-only: `BTNetworkSync` provides snapshot capture, serialization, deserialization, tree-state hashing, desync comparison, and blackboard snapshot application; `BTBlackboardDelta` tracks changed keys and produces or applies bounded delta frames. They do not create a transport, choose authority, define ordering or rate limits, or perform resynchronization and failure recovery. The product layer decides when to call them, which bytes to send, and how to react to a mismatch.

The sibling `CycloneGames.BehaviorTree.Networking` package extends those primitives. `BehaviorTreeNetworkProtocol` freezes message identifiers and payload schemas; `BehaviorTreeNetworkProfile` declares sync models, feature flags and tick cadences; `BehaviorTreeNetworkAuthority` binds a tree to a network identity and resolves tick and apply permission; `BehaviorTreeNetworkReplication` filters observers by owner, team or area; `BehaviorTreeNetworkSyncBridge` captures snapshots, deltas and hash-only frames and applies validated payloads. Its assemblies are `autoReferenced: false` and contain no transport or driver loop: a composition root calls `CaptureSnapshot`, `TryCreateBlackboardDelta` or `TryApplyPayload` on its own schedule, and drives the profile cadences explicitly. `CycloneGames.AIPerception.Networking` follows the same contract-only shape for perception data.

```csharp
using System;
using System.IO;
using CycloneGames.BehaviorTree.Core;
using CycloneGames.BehaviorTree.Core.Networking;

public static class Step9
{
    public static void Run(RuntimeBehaviorTree tree)
    {
        RuntimeBlackboard bb = tree.Blackboard;

        // Bounded snapshot payload.
        byte[] payload;
        using (var stream = new MemoryStream())
        {
            using (var writer = new BinaryWriter(stream))
            {
                bb.WriteTo(writer, RuntimeBlackboardNetworkScope.Snapshot, maxBytes: 8192);
                writer.Flush();
            }
            payload = stream.ToArray();
        }

        using (var stream = new MemoryStream(payload))
        using (var reader = new BinaryReader(stream))
        {
            bb.ReadFrom(reader, RuntimeBlackboardSerializationLimits.Default,
                RuntimeBlackboardNetworkScope.Snapshot);
        }

        ulong bbHash = bb.ComputeHash(RuntimeBlackboardNetworkScope.Networked);

        // Snapshot projection and tree-state hash (contracts only).
        BTStateSnapshot snapshot = BTNetworkSync.CaptureSnapshot(tree);
        byte[] wire = BTNetworkSync.SerializeSnapshot(snapshot);
        BTStateSnapshot decoded = BTNetworkSync.DeserializeSnapshot(wire);
        BTNetworkSync.ApplyBlackboardSnapshot(tree, decoded);

        using (var buffer = new BTStateSnapshotBuffer())
        {
            ulong treeHash = BTNetworkSync.ComputeTreeStateHash(
                tree, RuntimeBlackboardNetworkScope.Networked, buffer,
                BTNetworkSync.Limits.Default);
            bool desynced = BTNetworkSync.CheckDesync(tree, treeHash);
        }

        // Delta frames from the schema's delta-enabled keys.
        using (var delta = BTBlackboardDelta.CreateForSchema(bb.Schema))
        {
            delta.Attach(bb);
            if (delta.TryFlush(bb, out ArraySegment<byte> patch))
            {
                byte[] frame = new byte[patch.Count];
                Array.Copy(patch.Array, patch.Offset, frame, 0, patch.Count);
                BTBlackboardDelta.Apply(bb, frame);
            }
            delta.Detach();
        }
    }
}
```

`WriteTo(BinaryWriter, RuntimeBlackboardNetworkScope, int maxBytes)` returns the blackboard revision captured under the same storage lock and writes no payload bytes when the computed size exceeds the bound. `ReadFrom` validates framing, counts, stamps, and schema declarations before committing; a schema mismatch is rejected. `ComputeHash` folds keys in ascending order so peers produce the same value. `BTBlackboardDelta.CreateForSchema(schema)` tracks exactly the keys whose `RuntimeBlackboardSyncFlags` include `Delta`; `TryFlush` returns an `ArraySegment<byte>` over a reused internal buffer, valid until the next flush. `BTNetworkSync.ApplyBlackboardSnapshot` applies only the blackboard portion of a snapshot; managed node execution state is not reconstructed generically.

## <a id="step-10"></a>11. Step 10: the DOD Burst path

Use the DOD path when many agents share one homogeneous flat tree and the workload fits a restricted node set with fixed `int`/`float`/`bool` blackboard slots and fixed action slots. It is a separate execution model: `FlatBehaviorTree` owns persistent native storage for the shared immutable definition, and `BTTickScheduler` owns per-agent mutable state in NativeArrays scheduled by a Burst `BTTickJob`. It does not use `RuntimeNode` objects, `RuntimeBlackboard` observers, schemas, `object` values, or the managed tick managers.

```csharp
using CycloneGames.BehaviorTree.Core;
using CycloneGames.BehaviorTree.Runtime.DOD;

public static class Step10
{
    public static void Run()
    {
        var nodes = new[]
        {
            new FlatNodeDef
            {
                Type = FlatNodeType.Root,
                ChildCount = 1,
                ChildStartIndex = 0
            },
            new FlatNodeDef
            {
                Type = FlatNodeType.BlackboardCondition,
                BBKey = 0,
                Compare = CompareOp.Greater,
                CompareValue = 0
            }
        };

        using (var flatTree = new FlatBehaviorTree(nodes, new[] { 1 }))
        using (var scheduler = new BTTickScheduler(
                   flatTree,
                   bbSlotCount: 1,
                   actionSlotCount: 0,
                   initialCapacity: 256))
        {
            BTAgentHandle agent = scheduler.AddAgent(tickInterval: 1);
            scheduler.SetBBInt(agent, 0, 10);
            scheduler.ScheduleTick(deltaTime: 0.016f, batchSize: 64);
            scheduler.CompleteTick();
            RuntimeState result = scheduler.GetRootState(agent);
            scheduler.RemoveAgent(agent);
        }
    }
}
```

Node `0` must be the unique `FlatNodeType.Root`, and composite children are referenced by a flattened `ChildIndices` array. `BTAgentHandle` carries a slot index plus a generation, so a recycled slot rejects a stale handle. `TryAddAgent` is the admission-safe form; `AddAgent` throws `InvalidOperationException` at effective capacity. Terminal agents remain terminal until `ResetAgent(handle, clearBlackboard)`. External actions use `BTActionRequestHandle` through `TryGetActionRequest` and `TrySetActionStatus`. Every public state access completes the outstanding scheduled job before touching NativeArrays, and the scheduler has creator/owner-thread affinity. Dispose every scheduler before disposing the shared `FlatBehaviorTree`; the nested `using` order above does that. `BTCapacityLimits.MaxTreeDepth` also bounds flat-tree depth validation.

### Blackboard in this path

The DOD runtime does not read `RuntimeBlackboard` schemas. Each agent owns a fixed slot space, allocated with the scheduler.

| Aspect | Behaviour |
| --- | --- |
| Storage | Three arrays per agent: `int`, `float`, and a byte-backed `bool`. |
| Addressing | Slot index in `0 .. bbSlotCount - 1`. There are no key names and no schema. |
| Value types | `int`, `float`, `bool` only. `Float3`, `Long`, `Long2`, `Long3` and `object` have no slot. |
| Conditions | `FlatNodeType.BlackboardCondition` reads the int array and compares it with `CompareValue` using `CompareOp`. Float and bool slots can be read by actions, but that node cannot compare them. |
| Out-of-range access | `SetBBInt`, `GetBBInt` and the float and bool pairs throw `ArgumentOutOfRangeException` for a slot outside the range and `InvalidOperationException` for a stale or inactive agent. A node whose `BBKey` is outside the slot count fails its condition instead of reading adjacent memory. |
| Persistence | Values belong to the agent and survive ticks until `ResetAgent(handle, clearBlackboard: true)`. |
| Observers | None. Observers, abort modes and network scopes of the managed blackboard do not exist here. |
| Capacity | `bbSlotCount` is fixed at scheduler construction and applies to every agent. |
| Thread | Creator and owner-thread affinity; every access completes the outstanding job before touching the arrays. |

There is no bridge between the two blackboards. A tree that needs `Float3` or `object` values runs on the managed runtime; one that needs thousands of homogeneous agents runs here with the reduced value set.

## <a id="composite-example"></a>12. Composite example: a multi-layer tree

This example assembles one tree that uses a strict schema, observer abort, `Hybrid` reevaluation, a Service, a Parallel finish policy, explicit scheduling, optional LOD, and event wake-up together. It is a single compilable `MonoBehaviour` composition root.

```csharp
using UnityEngine;
using CycloneGames.BehaviorTree.Core;
using CycloneGames.BehaviorTree.Core.Nodes.Compositors;
using CycloneGames.BehaviorTree.Core.Nodes.Decorators;
using CycloneGames.BehaviorTree.Runtime.Components;

public sealed class MultiLayerTreeRoot : MonoBehaviour
{
    private static readonly int PerceptionReadyKey =
        RuntimeBlackboard.DefaultStringHashFunc("PerceptionReady");
    private static readonly int TaskStageKey =
        RuntimeBlackboard.DefaultStringHashFunc("TaskStage");
    private static readonly int SignalLevelKey =
        RuntimeBlackboard.DefaultStringHashFunc("SignalLevel");
    private static readonly int WorkCounterKey =
        RuntimeBlackboard.DefaultStringHashFunc("WorkCounter");
    private static readonly int ScoreAKey =
        RuntimeBlackboard.DefaultStringHashFunc("ScoreA");
    private static readonly int ScoreBKey =
        RuntimeBlackboard.DefaultStringHashFunc("ScoreB");

    private RuntimeBehaviorTree _tree;
    private BTPriorityTickManagerComponent _priorityManager;
    private BTDistanceLODProvider _lodProvider;
    private BTLODConfig _lodConfig;
    private GameObject _managerObject;

    private void Awake()
    {
        // (1) Strict schema: defaults, validation, and sync flags.
        RuntimeBlackboardSchema schema = new RuntimeBlackboardSchemaBuilder()
            .AddBool("PerceptionReady", false, RuntimeBlackboardSyncFlags.LocalOnly)
            .AddInt("TaskStage", 0, RuntimeBlackboardSyncFlags.SnapshotAndDelta)
            .AddInt("SignalLevel", 0, RuntimeBlackboardSyncFlags.LocalOnly)
            .AddInt("WorkCounter", 0, RuntimeBlackboardSyncFlags.Delta)
            .AddFloat("ScoreA", 0f, RuntimeBlackboardSyncFlags.LocalOnly)
            .AddFloat("ScoreB", 0f, RuntimeBlackboardSyncFlags.LocalOnly)
            .Build();

        var blackboard = new RuntimeBlackboard(schema: schema);

        // (2)-(6) Multi-layer tree.
        _tree = new RuntimeBehaviorTreeBuilder()
            .WithBlackboard(blackboard)
            .WithTickInterval(2)
            .Selector(selector =>
            {
                selector.AbortType = RuntimeAbortType.Both;
                selector.ReevaluationMode = RuntimeReevaluationMode.Hybrid;
                selector.ReevaluationPollInterval = 4;
            })
                // Branch A: observer-abort guard + Parallel finish policy.
                .Decorator(new RuntimeBBComparisonNode(), guard =>
                {
                    guard.KeyHash = PerceptionReadyKey;
                    guard.ValueType = BehaviorTreeComparisonValueType.Bool;
                    guard.Operator = BBComparisonOp.Equal;
                    guard.RefBool = true;
                    guard.AbortMode = RuntimeAbortType.Both;
                })
                    .Parallel(RuntimeParallelMode.UntilAnySuccess)
                        .Action(bb =>
                        {
                            bb.SetInt(WorkCounterKey, bb.GetInt(WorkCounterKey) + 1);
                            return RuntimeState.Success;
                        }, "CountWork")
                        .Wait(0.25f)
                    .End()          // Parallel
                .End()              // RuntimeBBComparisonNode
                // Branch B: Service + Utility selection under Hybrid reevaluation.
                .Decorator(new RuntimeServiceNode(), service =>
                {
                    service.Interval = 0.25f;
                    service.OnServiceTick = bb =>
                    {
                        int stage = bb.GetInt(TaskStageKey);
                        bb.SetFloat(ScoreAKey, stage * 0.5f);
                        bb.SetInt(SignalLevelKey, bb.GetInt(SignalLevelKey) + 1);
                    };
                })
                    .UtilitySelector(new[] { ScoreAKey, ScoreBKey })
                        .Action(bb =>
                        {
                            bb.SetInt(TaskStageKey, bb.GetInt(TaskStageKey) + 1);
                            return RuntimeState.Success;
                        }, "AdvanceStage")
                        .Action(bb => RuntimeState.Success, "HoldStage")
                    .End()          // UtilitySelector
                .End()              // RuntimeServiceNode
            .End()                  // Selector
            .Build();

        _tree.Terminated += state => Debug.Log($"Tree terminated: {state}");
    }

    private void Start()
    {
        // (7) Scheduling + explicit composition root + optional LOD provider.
        _managerObject = new GameObject("[BTPriorityTickManager]");
        _lodProvider = _managerObject.AddComponent<BTDistanceLODProvider>();
        _priorityManager =
            _managerObject.AddComponent<BTPriorityTickManagerComponent>();

        _lodConfig = ScriptableObject.CreateInstance<BTLODConfig>();
        _lodConfig.PriorityBudgets = new[] { 64, 32, 16, 8 };
        _priorityManager.Config = _lodConfig;
        _priorityManager.SetReferencePoint(transform);
        _priorityManager.Register(_tree, transform);
    }

    // External event stream: coalesced epoch + bounded immediate ticks.
    public void PushExternalEvent()
    {
        if (_tree == null) return;
        _tree.Evaluation.RequestReevaluation();
        _tree.WakeUp(boostedTicks: 2);
    }

    // Perception data change: an observer-driven abort request.
    public void PushPerceptionChange(bool ready)
    {
        if (_tree == null) return;
        _tree.Blackboard.SetBool(PerceptionReadyKey, ready);
    }

    private void OnDestroy()
    {
        if (_priorityManager != null && _tree != null)
        {
            _priorityManager.Unregister(_tree);
        }
        _tree?.Dispose();
        if (_lodConfig != null) Destroy(_lodConfig);
        if (_managerObject != null) Destroy(_managerObject);
    }
}
```

Step-by-step reading:

1. The schema declares six keys. `TaskStage` uses `RuntimeBlackboardSyncFlags.SnapshotAndDelta`, `WorkCounter` uses `Delta`, and the rest are `LocalOnly`. Defaults are applied when the blackboard is constructed, and every later write is validated against this contract.
2. `WithBlackboard(blackboard)` injects that schema-bound board; `WithTickInterval(2)` sets the baseline cadence. The builder is used once.
3. The root `RuntimeSelector` uses `AbortType.Both` and `ReevaluationMode.Hybrid` with `ReevaluationPollInterval = 4`, so guard rescans happen on events and on a bounded poll cadence.
4. Branch A wraps a `Parallel(RuntimeParallelMode.UntilAnySuccess)` in a `RuntimeBBComparisonNode` whose `AbortMode` is `Both`. A change to `PerceptionReady` fires the observer, which calls `RuntimeBehaviorTree.RequestReevaluation()` and lets the selector rescan its guards.
5. The Parallel finish policy completes the branch on the first `Success` and aborts the sibling that is still `Running`.
6. Branch B wraps a `UtilitySelector` in a `RuntimeServiceNode`. The service refreshes a score key on an interval while the child executes; the utility selector reads two `float` score keys and runs the highest-scoring child.
7. `Start()` mounts `BTDistanceLODProvider` before `BTPriorityTickManagerComponent` so the manager resolves it during initialization, then assigns a `BTLODConfig`, sets the reference point, and registers the tree.
8. `PushExternalEvent()` and `PushPerceptionChange(bool)` are the two producer entry points: one signals the epoch and wake-up budget directly, the other writes a tracked key so the observer path signals it. `OnDestroy()` unregisters and disposes in order.

## <a id="combining"></a>13. Combining with other CycloneGames packages

The module has no hard dependency on the other packages. Integration follows one shape: an external system writes a blackboard key, and the tree reacts to it.

### Perception: AIPerception

Publish the current sighting as a fact and let a decorator observe the key. The write wakes the tree; no per-frame polling node is needed.

```csharp
using CycloneGames.AIPerception.Runtime;
using CycloneGames.BehaviorTree.Core;
using UnityEngine;

public sealed class PerceptionBridge : MonoBehaviour
{
    private AIPerceptionComponent _perception;
    private RuntimeBlackboard _blackboard;

    public void Bind(AIPerceptionComponent perception, RuntimeBlackboard blackboard)
    {
        _perception = perception;
        _blackboard = blackboard;
    }

    private void Update()
    {
        if (_perception == null || _blackboard == null) return;

        IPerceptible target = _perception.GetClosestSightTarget();
        bool hasTarget = _perception.HasSightDetection && target != null;
        _blackboard.SetBool("HasTarget", hasTarget);
        _blackboard.SetFloat("TargetDistance",
            hasTarget ? Vector3.Distance(transform.position, target.Position) : float.MaxValue);
    }
}
```

A `RuntimeBBComparisonNode` over `HasTarget` with `AbortMode` set to `Self`, `LowerPriority` or `Both` turns the write into a reevaluation request. `IPerceptible.IsFromMemory` distinguishes a live sighting from a remembered position.

### Tags: GameplayTags

`GameplayTag` is owned by the tag manager. Request it once, then hash its name into a blackboard key. A read-only container can also be stored as an `Object` value when the tree needs to test several tags at once.

```csharp
using CycloneGames.GameplayTags;
using CycloneGames.BehaviorTree.Core;

GameplayTag alerted = GameplayTagManager.Request("State.Alerted");
int alertedKey = RuntimeBlackboard.DefaultStringHashFunc("State.Alerted");

// Per frame, on the owner thread. `view` is the read-only container
// exposed by your tag component.
_blackboard.SetBool(alertedKey, view.HasTag(alerted));
_blackboard.SetObject("TagContainer", view);
```

`Object` keys are local-only: they never enter a snapshot or a delta. Keep every replicated decision in primitive keys.

### Abilities: GameplayAbilities

A leaf node is a natural place to start an ability. Keep the spec at the composition layer, call `TryActivateAbility`, and publish the outcome as a key so guards can react.

```csharp
using CycloneGames.GameplayAbilities;
using CycloneGames.BehaviorTree.Core;

public sealed class ActivateAbilityNode : RuntimeNode
{
    private readonly AbilitySystemComponent _abilities;
    private readonly GameplayAbilitySpec _spec;

    public ActivateAbilityNode(AbilitySystemComponent abilities, GameplayAbilitySpec spec)
    {
        _abilities = abilities;
        _spec = spec;
    }

    protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
    {
        bool started = _abilities.TryActivateAbility(_spec);
        blackboard.SetBool("AbilityStarted", started);
        return started ? RuntimeState.Success : RuntimeState.Failure;
    }
}
```

Write `AbilityStarted` again from the ability end callback, or from an `AbilityTask_WaitAbilityEnd`, to drive the next branch.

### Framework: GameplayFramework

A framework actor owns the runner and decides when the tree updates. The tree does not assume an update source: `Self`, `Managed`, `PriorityManaged` and `ManualTick` are all valid.

```csharp
using CycloneGames.BehaviorTree.Runtime;
using CycloneGames.BehaviorTree.Runtime.Components;

// On your framework actor, after the tree asset is assigned.
var runner = gameObject.AddComponent<BTRunnerComponent>();
runner.SetTree(treeAsset);
// Tick mode is a serialized field; choose it in the Inspector.
```

A controller that owns its own clock can keep `ManualTick()` and call it from the framework update instead of mounting a manager.

### Space: Spatial

Proximity-driven behavior lives in the sibling package `CycloneGames.BehaviorTree.Spatial`, not here. It publishes a nearest-neighbour query into blackboard slots, so the tree reads ordinary keys and this package never gains a spatial dependency. Its README documents the wiring; the shape is one query per agent per update, then a `RuntimeBBComparisonNode` on the resulting `bool` and `float` slots.

## <a id="performance"></a>14. Performance, capacity and measurement

The steady-state low-allocation structure comes from how the managed runtime is built. A composite converts its setup-time child list into a sealed array once, and every execution step reads that array. Typed blackboard stores avoid boxing primitive values, and `RuntimeBlackboard.DefaultStringHashFunc` values can be pre-hashed. Manager scans reuse their storage, and the reevaluation epoch is a single `Interlocked.Increment` with no queue. Strict schema parsing does not happen in `Tick`; runtime access uses typed stores and precomputed integer hashes, with one dictionary contract lookup before a schema-bound write. Dictionary growth, first-time arrays, observer subscription changes, compile operations, schema construction, and capacity growth do allocate. Serialization may allocate unless the reusable-buffer APIs are used.

Graph validation is bounded. `RuntimeBehaviorTreeLimits` bounds code-first node count and depth, and `BTCapacityLimits.MaxTreeDepth` is the shared ceiling consumed by managed and flat-tree validation. `BTCapacityLimits.MaxNodeCount`, `DefaultNodeCount`, `DefaultAgentCount`, `MaxAgentCount`, `PriorityLevelCount`, `DefaultTickManagerCapacity`, and `DefaultPriorityBucketCapacity` expose the remaining ceilings. These are safety bounds, not sizing targets.

### Choosing a scheduling configuration

The right configuration depends on how many trees must run per frame, not on a benchmark table. Start from these rules, then measure in the build you ship:

| Workload | Starting point |
| --- | --- |
| Under 100 agents | `Self` for simplicity, or `Managed` with a budget at least as large as the agent count. |
| Hundreds of agents | `Managed`; raise `TickBudget` until the frame cost fits your budget, then raise `TickInterval`. |
| Thousands of agents | `PriorityManaged` with distance LOD so distant trees tick less often. |
| Tens of thousands | LOD with `TickInterval` of at least 4, or move a homogeneous case to the DOD path. |
| Reactive composites wider than about 128 children | Switch to `ReevaluationMode.OnChange` or `Hybrid`; a full scan visits every child on every tick. |

A `Managed` budget smaller than the agent count is not a fairness problem: the cursor is persistent and every tree is visited in turn. It lowers per-tree tick frequency, which is what a budget is for.

### Measuring

Use the Benchmark window (`Tools > CycloneGames > Behavior Tree > Behavior Tree Benchmark`) for throughput, allocation and soak runs; it exports CSV and JSON. Inside the Editor, use `ProfilerRecorder` with `GC Allocated In Frame` for per-frame allocation; `GC.GetAllocatedBytesForCurrentThread()` returns zero in the Editor and cannot be used there.

### Debugging the graph

The editor shows the live state of a running tree: node colour and status label, the active child index, and elapsed time for time-based nodes. When a tree does not advance, check the runner's tick mode and whether a manager was resolved; a managed runner without a manager logs one warning and ticks locally. Compile and validation failures are reported by `BehaviorTreeCompiler` before any runtime node is created, so an invalid graph never reaches `Tick`.

The structural guarantees come from the source: sealed child arrays, typed stores, the reevaluation epoch, deferred mutation, and the depth and capacity ceilings. Per-tick cost, allocation and retained memory depend on the build target, backend, hardware and workload, so measure them in your own environment.

## <a id="api"></a>15. API quick reference

| Type / member | Purpose |
| --- | --- |
| `RuntimeBehaviorTreeBuilder` | Code-first fluent builder; `Build()` returns one `RuntimeBehaviorTree`. |
| `RuntimeBehaviorTree` | `Tick()`, `Play()`, `Stop()`, `Dispose()`, `Terminated`, `State`, `IsStopped`, `Blackboard`, `Root`, `Evaluation`, `TickInterval`, `WakeUp`, `RequestReevaluation`, `ShouldTick`, `GetOwner<T>()`, `GetService<T>()`, `SetContext`. |
| `RuntimeState` | `NotEntered`, `Running`, `Success`, `Failure`. |
| `RuntimeBlackboard` | Eight typed `Set*`/`Get*`/`TryGet*` stores (`SetFloat3` for vectors; `SetVector3` in the Unity assembly), `Parent`, `GetStamp`, `HasKey`, `Remove`, `Clear`, `ResetToSchemaDefaults`, `AddObserver`, `AddGlobalObserver`, `RemoveObserver`, `ClearAllObservers`, `EnableConcurrentStorageAccess`, `ResetDefaultRandom`, `WriteTo`, `ReadFrom`, `ComputeHash`, `Schema`, `BindSchema`. |
| `RuntimeBlackboard.DefaultStringHashFunc` | Stable `BTHash.FNV1A` string-to-`int` key hash. |
| `RuntimeBlackboardSchemaBuilder` / `RuntimeBlackboardSchema` | `AddInt`, `AddFloat`, `AddBool`, `AddFloat3` (`AddVector3` in the Unity assembly), `AddLong`, `AddLong2`, `AddLong3`, `AddObject`, `Build`, `Count`, `ContractVersion`, `IsKnownKey`, `UsesSnapshot`, `UsesDelta`, `IsNetworkedKey`, `TryGetDefinition`, `ValidateWrite`. |
| `RuntimeBlackboardSyncFlags` | `LocalOnly`, `Snapshot`, `Delta`, `SnapshotAndDelta`. |
| `RuntimeBlackboardNetworkScope` | `Snapshot`, `Networked`. |
| `BTCapacityLimits` | `MaxTreeDepth`, `MaxNodeCount`, `DefaultNodeCount`, `MaxAgentCount`, `DefaultAgentCount`, `PriorityLevelCount`, `DefaultTickManagerCapacity`, `DefaultPriorityBucketCapacity`. |
| `RuntimeBTEvaluationController` | `Epoch`, `RequestReevaluation()`, `ShouldReevaluate(...)`. |
| `RuntimeReevaluationMode` | `PollEveryTick`, `OnChange`, `Hybrid`. |
| `RuntimeCompositeNode` | `AbortType`, `ReevaluationMode`, `ReevaluationPollInterval`, `Children`, `ChildCount`, `Seal()`, `AddChild`, `CurrentIndex`. |
| `RuntimeAbortType` | `None`, `Self`, `LowerPriority`, `Both`. |
| `RuntimeBBComparisonNode` | `KeyHash`, `Operator`, `ValueType`, `RefInt`, `RefFloat`, `RefBool`, `RefKeyHash`, `UseRefKey`, `FloatEpsilon`, `AbortMode`. |
| `BehaviorTreeComparisonValueType` / `BBComparisonOp` | `Int`, `Float`, `Bool`, `Object`; `Equal`, `NotEqual`, `GreaterThan`, `GreaterOrEqual`, `LessThan`, `LessOrEqual`, `IsSet`, `IsNotSet`. |
| `RuntimeParallelMode` / `RuntimeParallelNode.Mode` | `Default`, `UntilAnyComplete`, `UntilAnyFailure`, `UntilAnySuccess`. |
| `RuntimeParallelAllNode` | `SuccessThreshold`, `FailureThreshold`. |
| `RuntimeServiceNode` | `Interval`, `RandomDeviation`, `UseUnscaledTime`, `OnServiceTick`. |
| `RuntimeSubTreeNode` | `SetPortRemapping(...)`, with `RuntimeSubTreePortDirection.Input`, `Output`, `InOut`. |
| `RuntimeNode.EmitWakeUpSignal()` | Protected node-side reevaluation signal. |
| `IRuntimeBTCommand` / `IRuntimeBTConditionStrategy` | Reusable command and condition objects. |
| `RuntimeBTContext` | `Owner`, `ServiceResolver`, `GetOwner<T>()`, `GetService<T>()`; `WithOwner`, `WithContext`, `WithServiceResolver` on the builder. |
| `UnityBTContext` / `UnityBTTimeProvider` | Unity-side context: component lookup from a `GameObject` owner, and the `Time`-backed clock. |
| `BTFloat3` | Engine-free float triple stored by the core blackboard; `ToVector3()` and `ToBTFloat3()` convert in the Unity assembly. |
| `SpatialBlackboardQuery<TItem>` | `QueryNearest`, `QueryNearestFrom`, `PublishNearest`, `PublishCount`, `NearestItem`, `NearestSquaredDistance`, `Results`, `SquaredDistances`. |
| `BTTickManager` / `BTPriorityTickManager` | `TickBudget`, `SetBudgets`, `Register`, `TryRegister`, `Unregister`, `TryUnregister`, `UpdatePriority`, `TryUpdatePriority`, `Tick`, `Clear`, `GetMemoryStats`, `GetTreeCount`, `GetTotalCount`. |
| `BTTickManagerComponent.CreateInstance()` / `Instance` | Managed-mode component creation and resolution. |
| `BTPriorityTickManagerComponent.CreateInstance()` / `Instance` | Priority-managed component creation and resolution; `Config`, `Register(tree, transform)`, `BoostPriority`, `SetReferencePoint`. |
| `TickMode` | `Self`, `Manual`, `Managed`, `PriorityManaged`. |
| `BTRunnerComponent` | `ManualTick`, `SetTickMode`, `SetTickInterval`, `WakeUp`, `BoostPriority`, `Pause`, `Resume`, `Stop`, `Play`, `BTSetData`, `SetContext`, `SetServiceResolver`, `OnTreeStopped`, `RuntimeTree`. |
| `BTDistanceLODProvider` | Optional LOD source: `Config`, `ReferencePoint`, `MaximumTreeCount`, `TryRegisterTree`, `UnregisterTree`, `GetPriority`, `GetTickInterval`, `UpdateAllLOD`, `BoostPriority`. |
| `BTLODConfig` | `Levels`, `PriorityBudgets`, `BoostedPriority`, `BoostedTickInterval`, `TryValidate`, `GetLODLevel`, `GetLODLevelSqr`. |
| `BTNetworkSync` / `BTStateSnapshot` / `BTStateSnapshotBuffer` | `CaptureSnapshot`, `SerializeSnapshot`, `DeserializeSnapshot`, `ApplyBlackboardSnapshot`, `CheckDesync`, `ComputeTreeStateHash`, `DoesExecutionStateMatch`. |
| `BTBlackboardDelta` | `CreateForSchema`, `TrackKey`, `Attach`, `Detach`, `TryFlush`, `Apply`. |
| `FlatBehaviorTree` / `BTTickScheduler` | DOD definition owner and agent scheduler: `AddAgent`, `TryAddAgent`, `RemoveAgent`, `ResetAgent`, `ScheduleTick`, `CompleteTick`, `SetBBInt`, `GetRootState`, `TryGetActionRequest`, `TrySetActionStatus`, `ComputeAgentStateHash`. |
| `FlatNodeDef` / `FlatNodeType` / `CompareOp` / `BTAgentHandle` | Flat-tree node definition, node-type enum, comparison operator, and generation-safe agent handle. |
