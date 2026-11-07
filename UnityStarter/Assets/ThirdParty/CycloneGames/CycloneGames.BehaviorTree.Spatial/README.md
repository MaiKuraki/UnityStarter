# CycloneGames.BehaviorTree.Spatial

[Simplified Chinese](README.SCH.md)

## Overview

This package connects `CycloneGames.BehaviorTree` to a CycloneGames Spatial index. It provides one adapter that runs a nearest-neighbour query and publishes the result into blackboard slots, so trees react to proximity without holding an index reference and without the behavior tree package depending on Spatial.

Use the package when behavior depends on proximity: nearest target, count inside a radius, distance threshold. Trees that never query space do not need it, and `CycloneGames.BehaviorTree` compiles and runs without it.

## Assemblies and Dependencies

| Assembly | Purpose | Consumer reference |
| --- | --- | --- |
| `CycloneGames.BehaviorTree.Runtime.Integrations.Spatial` | Implements the query-to-blackboard publisher over `ISpatialReadOnlyIndex<TItem>`. | Explicit |
| `CycloneGames.BehaviorTree.Integrations.Spatial.Tests.Editor` | Verifies binding, layer filtering, result limits and allocation. | Test Runner only |

The package declares direct UPM dependencies on BehaviorTree and Spatial. The runtime assembly is engine-free (`noEngineReferences: true`) and `autoReferenced: false`.

## Installation

### UPM

Install `com.cyclone-games.behavior-tree-spatial`. Package Manager resolves its declared BehaviorTree and Spatial dependencies. No scripting define symbol is required, and `com.cyclone-games.behavior-tree` never references this package.

### Embedded under Assets

Place this package, `CycloneGames.BehaviorTree`, and `CycloneGames.Spatial` under the project's Assets tree. The integration asmdef uses direct assembly references, so it participates in compilation whenever those package roots are present.

## Usage

Four steps, run once per agent per update.

**1. Own the index.** The adapter queries whatever index you already maintain; it does not create or move entries.

```csharp
var index = new SpatialHashGrid<int>(cellSize: 4f);
index.TryInsert(agentId, x, y, z, SpatialLayers.Default, out _);
```

**2. Create one adapter per writer.** Buffers are allocated here, so the update loop allocates nothing.

```csharp
var query = new SpatialBlackboardQuery<int>(index, maxResults: 4);
```

**3. Query and publish.** Publishing writes two blackboard slots: a `bool` for "something was found" and a `float` for the squared distance.

```csharp
query.QueryNearest(x, y, z, radius: 12f, layerMask: SpatialLayers.All);
query.PublishNearest(blackboard, visibleKey, squaredDistanceKey);
```

**4. Read the slots in the tree.** They are ordinary keys, so any blackboard node works.

```csharp
builder.Condition(bb => bb.GetBool(visibleKey));
builder.Action(bb => bb.GetFloat(squaredDistanceKey) < 144f
    ? RuntimeState.Success
    : RuntimeState.Failure);
```

Distance stays squared because that is the unit the index reports: neither the query nor the comparison needs a square root.

`SpatialBlackboardQuery` accepts any `ISpatialReadOnlyIndex<TItem>`, so a live `SpatialHashGrid<TItem>` and a per-frame `SpatialSnapshot<TItem>` are interchangeable, and a project-owned index can implement the same interface.

## Behaviour

- Publishing goes through `SetBool` and `SetFloat`. The blackboard drops unchanged writes, so a stable nearest target neither notifies observers nor enters the network delta.
- `NearestSquaredDistance` is zero when nothing matched; read the visibility flag first.
- A non-positive radius clears the result without calling the index.
- One instance per writer thread. The adapter is not thread-safe, and the index lifetime stays with the caller.
