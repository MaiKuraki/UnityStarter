# CycloneGames.Scheduling

[English | 简体中文](README.SCH.md)

CycloneGames.Scheduling spreads deferred work across frames. You hand it items that make progress in small steps; it runs a bounded number of steps per update and leaves the rest queued.

It is not a general job system. It runs on one thread, keeps a fixed-size queue, and cannot interrupt a Step that has already started.

## Table of contents

- [What it does](#what-it-does)
- [Quick start](#quick-start)
- [Lanes and starvation](#lanes-and-starvation)
- [The work item contract](#the-work-item-contract)
- [Budgets](#budgets)
- [Diagnostics](#diagnostics)
- [A full queue](#a-full-queue)
- [Cancellation and handles](#cancellation-and-handles)
- [Faults](#faults)
- [Shutdown](#shutdown)
- [Unity host](#unity-host)
- [DI and ownership](#di-and-ownership)
- [Factory integration](#factory-integration)
- [Storage, allocation and threading](#storage-allocation-and-threading)
- [Common mistakes](#common-mistakes)
- [Tests](#tests)

## What it does

Use it for batch processing, pool maintenance, content checks, cache maintenance, or any other work that can finish a frame late.

Do not use it for input, physics, deterministic simulation, network lockstep, audio DSP, or code that already needs a Job dependency graph.

The scheduler moves cost around. It does not reduce total work, and it cannot preempt a long synchronous call inside one Step.

## Quick start

```csharp
using CycloneGames.Scheduling.Core;

var scheduler = new WorkScheduler(capacity: 128);
var work = new BatchWorkItem(new int[1000], batchSize: 4);

scheduler.TrySchedule(work, WorkLane.Normal, out WorkItemHandle handle);
scheduler.Tick(maxSteps: 8, softBudgetMs: 2.0);
```

`Tick` runs at most `maxSteps` items. Call it once per update. Call `TrySchedule`, `Cancel`, `ShutdownStep`, `Shutdown`, and `GetSnapshot` from the same thread.

The item used above:

```csharp
sealed class BatchWorkItem : IWorkItem
{
    private readonly int[] data;
    private readonly int batchSize;
    private int index;

    public BatchWorkItem(int[] data, int batchSize)
    {
        this.data = data;
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
    }
}
```

## Lanes and starvation

Four lanes, drained from `Immediate` to `Background`:

| Lane | Use for |
| --- | --- |
| `Immediate` | Must make progress this frame. Bypasses lower lanes. |
| `High` | Time sensitive, tolerant of a frame or two. |
| `Normal` | Default. |
| `Background` | Idle-time maintenance: cache sweeps, pool trimming, index rebuilds. |

The highest non-empty lane is drained first. A lane that never empties cannot lock out lower lanes: any non-empty lane is guaranteed a step at least every `StarvationLimitTicks` ticks (default 4).

```csharp
scheduler.TrySchedule(criticalWork, WorkLane.Immediate, out _);
scheduler.TrySchedule(cacheSweep, WorkLane.Background, out _);
```

Within one lane the order is FIFO and running items go to that lane's tail, so no item runs twice in one Tick.

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
| `Cancelled` | `Cancel` stopped the item. |
| `Faulted` | `Step` threw or returned an undefined value. |
| `Shutdown` | Shutdown stopped the remaining work. |

If `TrySchedule` returns `false`, the registration was never accepted and `OnTerminated` is not called. The caller still owns the item.

One instance cannot be queued twice at once. In Editor and Development builds a duplicate registration throws immediately; Release Player builds omit the check and leave the contract to the caller.

## Budgets

```csharp
scheduler.Tick(maxSteps: 64, softBudgetMs: 2.0);
```

`maxSteps` caps the number of `Step` calls per Tick. `softBudgetMs` caps admission by time. The scheduler checks the clock before starting later Steps, but it cannot stop a running Step, so the budget bounds admission rather than elapsed time.

A positive infinity budget disables clock-based admission and keeps only the step limit.

Read the counters instead of assuming the budget held:

```csharp
SchedulerSnapshot snapshot = scheduler.GetSnapshot();
long overBudgetTicks = snapshot.OverBudgetTickCount;
double lastTickMs = snapshot.LastTickMilliseconds;
```

## Diagnostics

`SchedulerSnapshot` separates the four termination reasons, so an operator can tell "the caller cancelled this" from "shutdown drained it":

```csharp
long cancelled = snapshot.CancelledCount;   // explicit Cancel
long drained   = snapshot.ShutdownCount;    // Shutdown / ShutdownStep
long faulted   = snapshot.FaultedCount;     // Step threw
```

Per-step attribution is opt-in, because it costs a second clock conversion on every step:

```csharp
scheduler.TrackStepTiming = true;   // enable while diagnosing, then turn it off

double worst = snapshot.SlowestStepMilliseconds;
WorkItemHandle culprit = snapshot.SlowestStep;   // diagnostic; resolves only while the slot is occupied
```

Both stay zero while TrackStepTiming is off.

`GetSnapshot` is safe to call from inside a Step. A work item can report its own progress without tearing scheduler state.

## A full queue

Capacity is fixed at construction. When the queue is full, `TrySchedule` returns `false` and a default handle, and increments `RejectedCount`. Accepted items are never dropped silently.

If the queue is full often, reduce production, split across lanes, or merge items. A bigger capacity just adds memory and queue latency.

## Cancellation and handles

```csharp
if (scheduler.Cancel(handle))
{
    // The item gets no further Step calls.
}
```

`Cancel` is O(1). The scheduler drops its reference before calling `OnTerminated(Cancelled)`.

A handle carries the slot, a generation and the issuing scheduler identity. A default, stale, released, or foreign handle returns `false` — using a handle from another scheduler can no longer cancel an unrelated item.

## Faults

A Step that throws terminates only that registration as `Faulted`. The rest of the queue still gets its step in the same Tick, then the failures are rethrown once the tick completes: the original exception when exactly one item failed, an `AggregateException` otherwise.

That keeps one broken item from stealing every other item's frame budget.

Do not call `TrySchedule`, `Cancel`, `Tick`, `ShutdownStep`, `Shutdown`, or `Dispose` from inside `Step` or `OnTerminated`.

## Shutdown

```csharp
scheduler.Shutdown();
```

This stops admission and terminates every remaining item synchronously, attempting all callbacks before throwing an aggregate failure. It is safe to call twice.

For a large queue, drain it in batches instead:

```csharp
scheduler.ShutdownStep(0);  // Stop admission, cancel nothing yet.
scheduler.ShutdownStep(32); // Terminate up to 32 items, no Steps.
```

`Dispose` drains the same way but does not throw on callback failures; misuse such as a wrong thread or a call from inside a Step still throws.

## Unity host

Add `SchedulingDriver` to a GameObject yourself. The package never creates runtime objects.

| Setting | Meaning |
| --- | --- |
| `Capacity` | Fixed registration capacity. Recreate the host to change it. |
| `Max Steps Per Frame` | Steps or shutdown terminations per Update. Zero pauses normal work. |
| `Soft Budget Milliseconds` | Soft time budget. Zero pauses normal work. |
| `Max Budget Milliseconds` | Hard ceiling on the soft budget, so an infinite soft budget still cannot stall a frame. |

`Update` drives normal work or a bounded shutdown. Disabling the component pauses the queue without cancelling it. A zero step budget does not stall shutdown: the drain advances at least one item per Update, and `IsShutdownDrainForced` reports when that override is active.

## DI and ownership

Create the scheduler directly for non-DI use, or let the container own it and bind it to the host:

```csharp
var scheduler = new WorkScheduler(capacity: 256);
driver.Bind(scheduler);
```

`Bind` is rejected while the driver still owns outstanding work; a drained scheduler can be replaced.

| Operation | Host-owned scheduler | Bound scheduler |
| --- | --- | --- |
| Driver Update | Runs work | Runs work |
| `driver.BeginShutdown()` | Starts bounded shutdown | Rejected |
| `driver.Shutdown()` / `OnDestroy` | Shuts down | Detaches, cancels nothing |

## Factory integration

The Factory integration adapts existing bounded pool-maintenance calls and compiles only when `com.cyclone-games.factory` is installed. The warmup adapter clamps the count reported by a pool to the requested range, so a pool cannot drive a negative request or overshoot the requested count.

## Storage, allocation and threading

The scheduler allocates its slot array and four lane arrays once at construction. Per registration:

- 24 bytes per slot, plus no separate free-slot array.

`TrySchedule`, `Cancel`, and requeue are O(1). Shutdown is O(terminated items); use `ShutdownStep` to spread it.

Normal scheduling, cancellation, requeue, and bounded shutdown do not allocate managed memory after construction. Exception paths allocate.

The scheduler is owner-thread confined. It is not thread-safe, it does not marshal work from worker threads, and it does not make Unity APIs callable off the main thread. Cross-thread producers need a separate bounded adapter with its own synchronization design.

## Common mistakes

| Mistake | Fix |
| --- | --- |
| One long synchronous operation in a Step | Split it into bounded steps. |
| The same instance queued twice | Wait for termination, or use a distinct instance. |
| Everything on `Immediate` | Reserve it for work that must land this frame; the starvation window covers the rest. |
| Scheduler calls from `Step` or `OnTerminated` | Return and act from the next owner update; `GetSnapshot` is the exception. |
| Assuming `maxSteps` bounds callback cost | Budget each domain separately. |
| Destroying a pool before its maintenance work stops | Stop and drain first. |

## Tests

EditMode and PlayMode suites cover lane priority, starvation bound, fault isolation, handle identity, budget edges, reentrancy, termination callbacks, shutdown accounting, Factory adapters, Unity Update and destruction, and external ownership.

EditMode also carries deterministic performance gates: steady-state allocation over 100 000 operations, footprint per registration, a 300 000-operation soak, and a reflection-based public API surface check. They assert allocation and layout, not wall-clock time.

```bash
<unity-exe> -batchmode -nographics -projectPath <repo-root>/UnityStarter -runTests -testPlatform EditMode -testFilter CycloneGames.Scheduling -testResults <output.xml> -logFile <output.log>
```

Performance numbers depend on device, Unity version, backend, build type, workload, and stripping settings. Editor tests do not establish target Player performance.
