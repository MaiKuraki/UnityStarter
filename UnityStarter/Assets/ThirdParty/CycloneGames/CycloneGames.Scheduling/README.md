# CycloneGames.Scheduling

[English | 简体中文](README.SCH.md)

CycloneGames.Scheduling splits deferred work across frames. You give it items that make progress in small steps; it runs a bounded number of steps each update and leaves the rest queued.

It is not a general job system. It runs on one thread, keeps a fixed-size queue, and cannot interrupt a Step that has already started.

## Table of contents

- [What it does](#what-it-does)
- [Quick start](#quick-start)
- [The work item contract](#the-work-item-contract)
- [Budgets and fairness](#budgets-and-fairness)
- [A full queue](#a-full-queue)
- [Cancellation and termination](#cancellation-and-termination)
- [Shutdown](#shutdown)
- [Unity host](#unity-host)
- [DI and ownership](#di-and-ownership)
- [Factory integration](#factory-integration)
- [Storage and allocation](#storage-and-allocation)
- [Thread and I/O boundaries](#thread-and-io-boundaries)
- [Common mistakes](#common-mistakes)
- [Tests](#tests)

## What it does

Use it for batch processing, pool maintenance, content checks, cache maintenance, or any other work that can finish a frame late.

Do not use it for input, physics, deterministic simulation, network lockstep, audio DSP, or code that already needs a Job dependency graph.

The scheduler moves cost around. It does not reduce total work, and it cannot preempt a long synchronous call inside one Step.

It is not a fix for every frame spike. A spike from one long Step stays unless that Step is split; spikes from rendering, GPU submission, or GC are outside this module.

## Quick start

```csharp
using System;
using CycloneGames.Scheduling.Core;

public sealed class BatchWorkItem : IWorkItem
{
    private readonly int[] data;
    private readonly int batchSize;
    private int index;

    public int ProcessedCount => index;
    public WorkTerminationReason? TerminationReason { get; private set; }

    public BatchWorkItem(int[] data, int batchSize = 4)
    {
        this.data = data ?? throw new ArgumentNullException(nameof(data));
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }

        this.batchSize = batchSize;
    }

    public WorkStepResult Step()
    {
        int end = Math.Min(data.Length, index + batchSize);
        while (index < end)
        {
            data[index]++;
            index++;
        }

        return index == data.Length ? WorkStepResult.Completed : WorkStepResult.Running;
    }

    public void OnTerminated(WorkTerminationReason reason)
    {
        TerminationReason = reason;
    }
}
```

Drive it on the thread that created the scheduler:

```csharp
var scheduler = new WorkScheduler(capacity: 128);
var work = new BatchWorkItem(new int[1000], batchSize: 4);

if (scheduler.TrySchedule(work, out WorkItemHandle handle))
{
    // The scheduler owns the registration from here.
}

scheduler.Tick(maxSteps: 8, softBudgetMs: 2.0);
```

`Tick` runs at most `maxSteps` items. Call it once per update. Call `TrySchedule`, `Cancel`, `ShutdownStep`, `Shutdown`, and `GetSnapshot` from the same thread.

## The work item contract

```csharp
public interface IWorkItem
{
    WorkStepResult Step();
    void OnTerminated(WorkTerminationReason reason);
}
```

`Step` returns `Running` while more work remains and `Completed` when the item is done. The item stores its own progress and partial results.

Every accepted registration ends in exactly one `OnTerminated` call:

| Reason | Meaning |
| --- | --- |
| `Completed` | `Step` returned `Completed`. |
| `Cancelled` | `Cancel` stopped the item first. |
| `Faulted` | `Step` threw or returned an invalid value. |
| `Shutdown` | Shutdown stopped the remaining work. |

If `TrySchedule` returns `false`, the registration was never accepted and `OnTerminated` is not called. The caller still owns the item.

One instance cannot be queued twice at once.

## Budgets and fairness

```csharp
scheduler.Tick(maxSteps: 8, softBudgetMs: double.PositiveInfinity);
```

`maxSteps` caps the number of `Step` calls per Tick. It says nothing about how long one Step takes.

`softBudgetMs` caps admission by time. The scheduler checks the clock before starting later Steps, but it cannot stop a running Step, so a Tick can exceed the budget.

```csharp
scheduler.Tick(maxSteps: 64, softBudgetMs: 2.0);
```

Read the counters instead of assuming the budget held:

```csharp
SchedulerSnapshot snapshot = scheduler.GetSnapshot();
long overBudgetTicks = snapshot.OverBudgetTickCount;
double lastTickMs = snapshot.LastTickMilliseconds;
```

Running items go to the back of the queue. With items `A`, `B`, `C` and a Step budget of two:

| Tick | Runs | Queue after |
| --- | --- | --- |
| 1 | `A`, `B` | `C`, `A`, `B` |
| 2 | `C`, `A` | `B`, `C`, `A` |
| 3 | `B`, `C` | `A`, `B`, `C` |

No item runs twice in one Tick.

## A full queue

Capacity is fixed at construction. When the queue is full, `TrySchedule` returns `false` and a default handle, and increments `RejectedCount`. Accepted items are never dropped silently.

Keep the same instance and retry later:

```csharp
if (!scheduler.TrySchedule(work, out handle))
{
    // Retry on a later update with the same instance.
}
```

If the queue is full often, reduce production or merge items. A bigger capacity just adds memory and queue latency.

## Cancellation and termination

```csharp
if (scheduler.Cancel(handle))
{
    // The item gets no further Step calls.
}
```

`Cancel` is O(1). The scheduler drops its reference before calling `OnTerminated(Cancelled)`. A default, stale, or released handle returns `false`.

`GetSnapshot` reports the counters. They follow:

```text
AcceptedCount == CompletedCount + FaultedCount + CancelledCount + OutstandingCount
```

`RejectedCount` is separate because rejected work was never accepted.

When `Step` throws, the item becomes `Faulted`, the exception propagates, and the other items stay queued. A `Step` or `OnTerminated` callback that throws does not leave a half-released registration.

Do not call `TrySchedule`, `Cancel`, `Tick`, `ShutdownStep`, `Shutdown`, or `GetSnapshot` from inside `Step` or `OnTerminated`.

## Shutdown

Final cleanup:

```csharp
scheduler.Shutdown();
```

This stops admission and terminates every remaining item synchronously. It attempts all callbacks before throwing an aggregate failure, and it is safe to call twice.

For a large queue, drain it in batches instead:

```csharp
scheduler.ShutdownStep(0);  // Stop admission, cancel nothing yet.
scheduler.ShutdownStep(32); // Terminate up to 32 items, no Steps.
```

`IsShutdown` turns true when admission stops. `IsShutdownCompleted` turns true only when the queue is empty. Callback failures are collected per batch, and later `ShutdownStep` calls continue the drain. The count budget does not bound one callback's cost.

## Unity host

Add `SchedulingDriver` to a GameObject yourself. The package never creates runtime objects.

| Setting | Meaning |
| --- | --- |
| `Capacity` | Fixed registration capacity. Recreate the host to change it. |
| `Max Steps Per Frame` | Steps or shutdown terminations per Update. Zero pauses both. |
| `Soft Budget Milliseconds` | Soft time budget. Zero pauses normal work. |

Register work:

```csharp
using CycloneGames.Scheduling.Core;
using CycloneGames.Scheduling.Runtime;

public static class SchedulingExample
{
    public static void Configure(SchedulingDriver driver, IWorkItem work)
    {
        driver.Scheduler.TrySchedule(work, out _);
    }
}
```

`Update` drives normal work or a bounded shutdown. Disabling the component pauses the queue without cancelling it. `OnApplicationQuit` and `OnDestroy` finish whatever is left immediately.

For a scheduler the host created, `driver.BeginShutdown()` switches Update from Steps to bounded termination. If the host is destroyed mid-drain, the rest is still cleaned up.

A destroyed driver is fake-null in Unity. Accessing `driver.Scheduler` after destruction throws rather than creating a new scheduler. Unity may destroy objects after the Step that requested the destroy, so fake-null checks and main-thread ownership stay in the Unity integration code.

## DI and ownership

Create the scheduler directly for non-DI use:

```csharp
var scheduler = new WorkScheduler(capacity: 256);
// Drive Tick from an owner you control.
```

For DI, let the container own the scheduler and bind it to the host:

```csharp
var scheduler = new WorkScheduler(capacity: 256);
driver.Bind(scheduler);
```

Binding is allowed after `Awake` only while the driver's own scheduler has accepted no work. An external scheduler may already hold work from the composition root.

| Operation | Host-owned scheduler | Bound scheduler |
| --- | --- | --- |
| Driver Update | Runs work | Runs work |
| `driver.BeginShutdown()` | Starts bounded shutdown | Rejected |
| `driver.Shutdown()` / `OnDestroy` | Shuts down | Detaches, cancels nothing |
| Final owner | Driver | Composition root |

`OwnsScheduler` tells you which case you are in. The external owner must drain before destroying resources its items reference.

## Factory integration

The Factory integration adapts existing bounded pool-maintenance calls.

```csharp
using CycloneGames.Scheduling.Integrations.Factory;

var warmup = new PoolWarmupWorkItem(pool, count: 100, batchSize: 4);
scheduler.TrySchedule(warmup, out _);

var trim = new PoolTrimInactiveWorkItem(pool, targetInactiveCount: 16, batchSize: 4);
scheduler.TrySchedule(trim, out _);

var despawn = new PoolDespawnWorkItem(pool, batchSize: 4);
scheduler.TrySchedule(despawn, out _);
```

`batchSize` limits pool operations per adapter Step. `maxSteps` limits adapter calls per Tick. They are separate budgets.

`TargetReached` tells you whether the requested target was met or the pool just made no progress. Cancelling stops later pool calls without undoing earlier ones and without disposing the pool. The pool owner keeps the pool alive and avoids conflicting structural changes.

## Storage and allocation

The scheduler allocates `Slot[]` and a free-slot `int[]` once at construction.

- `TrySchedule`, `Cancel`, and requeue are O(1).
- Shutdown is O(terminated items). Use `ShutdownStep` to spread it.
- `WorkItemHandle` holds a slot index and a generation. It only works with the scheduler that issued it.
- Generation zero is the default handle. Handles that wrap the full generation range can alias, so do not keep them forever.

Normal scheduling, cancellation, requeue, and bounded shutdown do not allocate managed memory after construction. Exception paths may allocate. The hot path avoids LINQ, captured lambdas, resizing, temporary collections, and per-item strings.

Work item allocation and payload memory are the caller's. Pool work items only when a measured high-churn load needs it and reset ownership is clear.

## Thread and I/O boundaries

The scheduler is owner-thread confined. It is not thread-safe, it does not marshal work from worker threads, and it does not make Unity APIs callable off the main thread. Cross-thread producers need a separate bounded adapter with its own synchronization design.

A Step must not hide unbounded synchronous I/O. File reads, hashes, decompression, async loads, and buffer ownership need bounded domain adapters. A large file must be processed incrementally inside the operation; one file per Step is not enough.

Performance numbers depend on device, Unity version, backend, build type, workload, and stripping settings. Editor tests do not establish target Player performance.

## Common mistakes

| Mistake | Fix |
| --- | --- |
| One long synchronous operation in a Step | Split it into bounded steps. |
| A new work item after every rejection | Keep one instance and retry later. |
| The same instance queued twice | Use separate registrations or wait for termination. |
| Scheduler calls from `Step` or `OnTerminated` | Return and act from the next owner update. |
| Assuming `maxSteps` bounds pool objects or callback cost | Budget each domain separately. |
| Host and container both shutting down a bound scheduler | Let the composition root own it. |
| Destroying a pool before its maintenance work stops | Stop and drain first. |
| Treating Editor tests as Player performance data | Measure the target Player. |

## Tests

EditMode:

```bash
<unity-exe> -batchmode -nographics -projectPath <repo-root>/UnityStarter -runTests -testPlatform EditMode -testFilter CycloneGames.Scheduling -testResults <output.xml> -logFile <output.log>
```

PlayMode:

```bash
<unity-exe> -batchmode -nographics -projectPath <repo-root>/UnityStarter -runTests -testPlatform PlayMode -testFilter CycloneGames.Scheduling.Tests.PlayMode -testResults <output.xml> -logFile <output.log>
```

The suites cover FIFO order, budgets, rejection, cancellation, terminal callbacks, exceptions, immediate and bounded shutdown, handle reuse, Factory maintenance, Unity Update and destruction, fake-null handling, and external ownership.

Measure allocation and performance outside the Editor or in the target Player. Target Player, IL2CPP, AOT, stripping, and device-specific behavior need their own runs.
