# CycloneGames.RPGFoundation.Interaction.GameplayFramework

[Simplified Chinese](README.SCH.md)

## Overview

This companion package connects `CycloneGames.GameplayFramework.Runtime.Actor` to RPGFoundation Interaction without adding GameplayFramework types to the Interaction host package. It provides focused extension methods for position conversion, instigator composition, and authority target snapshots.

Install this package only when GameplayFramework Actors participate in the Interaction runtime. Projects that do not use GameplayFramework keep the Interaction assemblies and dependency graph unchanged.

## Package Layout

```text
CycloneGames.RPGFoundation.Interaction.GameplayFramework/
  Runtime/
    CycloneGames.RPGFoundation.Interaction.Integrations.GameplayFramework.asmdef
    GameplayFrameworkInteractionExtensions.cs
  Tests/Editor/
    CycloneGames.RPGFoundation.Interaction.GameplayFramework.Tests.Editor.asmdef
    GameplayFrameworkInteractionExtensionsTests.cs
```

## Assemblies and Dependencies

| Assembly | Role | Unity dependency | Consumer reference |
| --- | --- | --- | --- |
| `CycloneGames.RPGFoundation.Interaction.Integrations.GameplayFramework` | Actor-to-Interaction adapters. | Yes | Explicit |
| `CycloneGames.RPGFoundation.Interaction.GameplayFramework.Tests.Editor` | EditMode behavior coverage. | Yes | Test Runner only |

The package declares direct dependencies on `com.cyclone-games.rpg-foundation` and `com.cyclone-games.gameplay-framework`. The Runtime assembly is `autoReferenced: false`; any assembly that calls these extensions must reference it explicitly. It uses no scripting define symbol and has no dependency on a DI container.

## Installation

### UPM

Install `com.cyclone-games.rpg-foundation-interaction-gameplay-framework`. Unity Package Manager resolves the two declared package dependencies.

### Embedded under Assets

Place these three package roots under the project Assets tree:

```text
CycloneGames.RPGFoundation/
CycloneGames.GameplayFramework/
CycloneGames.RPGFoundation.Interaction.GameplayFramework/
```

The integration assembly uses direct asmdef references, so the same source compiles in an Assets-based project without PlayerSettings symbols or generated capability files.

## Actor Adapters

Convert an Actor location into the Interaction core vector:

```csharp
using CycloneGames.RPGFoundation.Interaction.Core;
using CycloneGames.RPGFoundation.Interaction.Integrations.GameplayFramework;

if (actor.TryGetInteractionPosition(out InteractionVector3 position))
{
    // Submit position to the interaction authority boundary.
}
```

Create an Interaction instigator whose Unity object owner is the Actor GameObject:

```csharp
GameObjectInstigator instigator = actor.CreateInteractionInstigator(stablePlayerId);
```

Create a bounded authority snapshot from the Actor location:

```csharp
bool created = actor.TryCreateInteractionTargetSnapshot(
    worldId,
    targetStableId,
    interactionRange,
    out InteractionTargetSnapshot snapshot,
    enabledActionIds: enabledActions);
```

`targetStableId` must be nonzero. A missing or destroyed Actor returns `false` for `Try*` operations. `GetInteractionPosition` returns `InteractionVector3.Zero` when no Actor is available.

## Ownership, Threading, and Performance

- The Actor and its GameObject remain owned by GameplayFramework and the Unity scene or spawn composition root.
- `GameObjectInstigator` observes the Actor GameObject; this package does not destroy or retain Unity objects independently.
- Snapshot arrays are passed to the Interaction value object according to its ownership contract. Treat the supplied action array as immutable after publication.
- Actor and GameObject APIs are main-thread-only Unity operations.
- Position conversion and successful `TryGetInteractionPosition` calls allocate no managed memory. Instigator creation allocates one managed wrapper by design.
- Cache the instigator when it is reused; do not recreate it in an update loop.

## Persistence

This package writes no files, assets, preferences, caches, or save data. Stable Actor and target identifiers belong to the application authority and persistence layers.

## Validation

Run these EditMode assemblies after changing the integration:

```text
CycloneGames.RPGFoundation.Interaction.GameplayFramework.Tests.Editor
CycloneGames.RPGFoundation.Interaction.Tests.Editor
CycloneGames.GameplayFramework.Tests.Editor
```

Also verify that a project containing only the Interaction host package has no reference to this companion assembly.

## Troubleshooting

- If extension methods are unavailable, add `CycloneGames.RPGFoundation.Interaction.Integrations.GameplayFramework` to the consumer asmdef references.
- If an asmdef reference cannot be resolved, confirm that all three package roots listed in the Assets installation section are present.
- If a `Try*` method returns `false`, confirm that the Actor is alive and the target stable identifier is nonzero.
