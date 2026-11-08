# CycloneGames.AIResources

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

A shared resource admits one holder at a time. This module records which owner holds a resource and when that claim expires.

The module stores no description of the resource. Positions, scene objects and asset references remain with the caller; the module compares integer identifiers only.

Three concerns are deliberately excluded: spatial lookup, waiting queues and persistence. Each belongs to the caller or to a separate module.

## <a id="assemblies"></a>Assemblies and dependencies

| Assembly | Purpose | Consumer reference |
| --- | --- | --- |
| `CycloneGames.AIResources.Core` | Claim table. | Explicit |
| `CycloneGames.AIResources.Tests.Editor` | Contract tests and a randomised operation model. | Test Runner only |

The core assembly has no dependencies and no engine references, so the same table runs in a player and in a headless process.

## <a id="model"></a>Model

A claim is a record of three values.

| Field | Meaning |
| --- | --- |
| Owner | `int` identity supplied by the caller. |
| Priority | `int`, compared only when two owners conflict. |
| Expiry | Absolute time on the caller's clock. Zero means the claim does not expire. |

Every claim resolves to exactly one of these results.

| Situation | Result |
| --- | --- |
| Resource is free | `Claimed` |
| Same owner claims again | `Refreshed`, expiry replaced |
| Another owner holds it, no valid preemption | `Rejected` |
| Another owner holds it with lower priority and `preempt` is set | `Preempted` |
| All slots are occupied | `Exhausted` |

Preemption requires the `preempt` flag and a strictly higher priority. Equal priorities never displace each other, so two owners at the same level cannot alternate possession.

With a lease, the expiry acts as a hard deadline: once it passes, the next sweep releases the resource without the owner's cooperation. Without a lease the claim remains until released explicitly.

## <a id="usage"></a>Usage

### 1. Construct the table

```csharp
var resources = new AIResourceTable(capacity: 256);
```

`capacity` bounds simultaneous claims, not the number of identifiers seen over time. Storage is allocated at construction; claim, release and sweep do not allocate. The table does not grow, so a claim on a full table returns `Exhausted`.

### 2. Claim and release

```csharp
if (resources.TryClaim(resourceId, ownerId, priority: 0, nowSeconds: clock.NowSeconds)
    == AIResourceClaimResult.Claimed)
{
    // The caller holds resourceId.
}

resources.Release(resourceId, ownerId);
```

`Release` returns false when the caller is not the holder. A repeated release therefore leaves the table unchanged.

### 3. Query the state

```csharp
bool heldByCaller = resources.IsClaimedBy(resourceId, ownerId);
bool heldByAnyone = resources.TryGetOwner(resourceId, out int holder);
```

Both are lookups with no side effect. They answer whether a claim attempt is worth issuing; they do not reserve anything.

### 4. Attach a lease

A claim without a lease survives until released. If the holder is destroyed or disabled first, the resource stays occupied. A lease bounds that duration.

```csharp
resources.TryClaim(resourceId, ownerId, priority: 0,
    nowSeconds: clock.NowSeconds, leaseSeconds: 5d);

// Once per update, on the owning thread.
resources.SweepExpired(clock.NowSeconds);
```

`leaseSeconds` of zero or less disables the deadline. Re-claiming by the current owner refreshes the deadline and returns `Refreshed`.

### 5. Permit preemption

```csharp
AIResourceClaimResult result = resources.TryClaim(
    resourceId, otherOwnerId, priority: 2,
    nowSeconds: clock.NowSeconds, leaseSeconds: 5d, preempt: true);
```

The displaced owner is not notified. A holder that must react to displacement polls `IsClaimedBy` or re-claims on its next decision, which keeps the table free of callbacks.

### 6. Release by owner

```csharp
int released = resources.ReleaseOwner(ownerId);
```

The call walks the occupied slots once and returns how many claims were removed. It applies to holders that own several resources and are removed as a unit.

## <a id="semantics"></a>Semantics

- Claim, release, query and sweep must run on one thread. The table takes no lock.
- Identifiers are compared by value, so a hash collision cannot make two identifiers refer to the same resource.
- A rejected claim changes nothing: owner, priority and expiry stay as they were.
- `SweepExpired(now)` releases every claim whose expiry is non-zero and not greater than `now`.
- `Clear` drops all claims. It exists for teardown and is not a substitute for releasing.

## <a id="cost"></a>Cost

| Operation | Cost |
| --- | --- |
| `TryClaim`, `Release`, `IsClaimedBy`, `TryGetOwner` | Expected constant time. |
| `ReleaseOwner`, `SweepExpired` | Linear in the number of occupied slots. |
| Bucket rebuild after heavy churn | Linear, triggered once removed entries exceed half the bucket array. |

Memory is fixed at construction: one bucket array sized to twice the capacity, one slot array of `capacity` entries, and one scratch array for batch removal.

## <a id="api"></a>API

```csharp
AIResourceTable(int capacity = 64);

AIResourceClaimResult TryClaim(int resourceId, int ownerId, int priority, double nowSeconds,
                               double leaseSeconds = 0d, bool preempt = false);
bool Release(int resourceId, int ownerId);
int ReleaseOwner(int ownerId);

bool IsClaimedBy(int resourceId, int ownerId);
bool TryGetOwner(int resourceId, out int ownerId);

int SweepExpired(double nowSeconds);
void Clear();

int ClaimedCount { get; }
int Capacity { get; }
```

## <a id="limits"></a>Limits

- No spatial lookup. Locating candidate resources belongs to `CycloneGames.Spatial` or to the caller's own index.
- No waiting queue. A rejected caller retries on its own schedule; the table keeps no queue and no per-waiter timer.
- No persistence. Claims are runtime state and are not written to disk.
- No ownership callbacks. Displacement is observed by polling, not by an event.
