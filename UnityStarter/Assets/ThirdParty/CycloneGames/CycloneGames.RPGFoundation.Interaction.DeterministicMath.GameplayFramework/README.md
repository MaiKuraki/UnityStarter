# CycloneGames.RPGFoundation.Interaction.DeterministicMath.GameplayFramework

[Simplified Chinese](README.SCH.md)

## Overview

This companion package is the narrow Unity boundary between GameplayFramework Actors and deterministic RPGFoundation Interaction authority data. An Actor supplies Unity lifetime and identity context; an explicit `IInteractionDeterministicPositionProvider` supplies the authoritative fixed-point position.

The bridge never derives authority position from `Transform`. This keeps rendering interpolation and floating-point scene state outside deterministic validation.

## Package Layout

```text
CycloneGames.RPGFoundation.Interaction.DeterministicMath.GameplayFramework/
  Runtime/
    DeterministicGameplayFramework.asmdef
    GameplayFrameworkDeterministicInteractionExtensions.cs
  Tests/Editor/
    DeterministicGameplayFramework.Tests.Editor.asmdef
    InteractionDeterministicGameplayFrameworkIntegrationTests.cs
```

## Assemblies and Dependencies

| Assembly | Role | Unity dependency | Consumer reference |
| --- | --- | --- | --- |
| `CycloneGames.RPGFoundation.Interaction.Integrations.DeterministicMath.GameplayFramework` | Actor and deterministic-position adapter methods. | Yes | Explicit |
| `CycloneGames.RPGFoundation.Interaction.DeterministicMathGameplayFramework.Tests.Editor` | EditMode behavior coverage. | Yes | Test Runner only |

The package declares every direct package dependency used by its Runtime assembly: RPGFoundation, the Interaction DeterministicMath companion, DeterministicMath, and GameplayFramework. Its assemblies are `autoReferenced: false`. No scripting define symbol or DI container is required.

This package does not reference the non-deterministic Interaction GameplayFramework companion. Consumers may install either bridge independently or install both when they expose both authority paths.

## Installation

### UPM

Install `com.cyclone-games.rpg-foundation-interaction-deterministic-math-gameplay-framework`. Unity Package Manager resolves the declared dependencies.

### Embedded under Assets

Place these package roots under the project Assets tree:

```text
CycloneGames.RPGFoundation/
CycloneGames.DeterministicMath/
CycloneGames.GameplayFramework/
CycloneGames.RPGFoundation.Interaction.DeterministicMath/
CycloneGames.RPGFoundation.Interaction.DeterministicMath.GameplayFramework/
```

The integration assembly uses direct asmdef references. The same source compiles in Assets-based projects without PlayerSettings symbols or generated capability files.

## Deterministic Actor Composition

The deterministic simulation owner implements `IInteractionDeterministicPositionProvider`:

```csharp
public sealed class PlayerSimulationState : IInteractionDeterministicPositionProvider
{
    public FPVector3 Position { get; set; }

    public bool TryGetDeterministicInteractionPosition(out FPVector3 position)
    {
        position = Position;
        return true;
    }
}
```

Use that provider with an Actor to create authority data:

```csharp
bool created = actor.TryCreateDeterministicInteractionTargetSnapshot(
    simulationState,
    worldId,
    targetStableId,
    interactionRange,
    out InteractionDeterministicTargetSnapshot snapshot,
    enabledActionIds: enabledActions);
```

Request payload creation follows the same rule:

```csharp
bool created = actor.TryCreateDeterministicInteractionRequestPayload(
    simulationState,
    requestId,
    instigatorStableId,
    targetStableId,
    actionId,
    tick,
    worldId,
    out InteractionDeterministicRequestPayload payload);
```

A missing or destroyed Actor, missing provider, failed provider read, or zero target stable identifier returns `false` and a default output value.

## Ownership, Threading, and Performance

- GameplayFramework owns Actor lifetime. The deterministic simulation owner owns the position provider and its update schedule.
- The provider must read the same authoritative state used by rollback, lockstep, replay, or server simulation.
- Do not read Transform position inside the provider when fixed-point simulation state is authoritative.
- Actor validity checks and MonoBehaviour providers are Unity main-thread operations. A pure C# provider can follow its simulation owner's thread contract, but this bridge call still touches Actor state and therefore belongs on the Unity main thread.
- Successful payload and snapshot construction uses value types. The supplied action array follows the snapshot ownership contract and should be treated as immutable after publication.

## Persistence

This package writes no files, assets, preferences, caches, or save data. It creates deterministic value objects only. Protocol, replay, and save owners define serialization, bounds, integrity, and storage lifetime.

## Validation

Run these EditMode assemblies after changing the integration:

```text
CycloneGames.RPGFoundation.Interaction.DeterministicMathGameplayFramework.Tests.Editor
CycloneGames.RPGFoundation.Interaction.DeterministicMath.Tests.Editor
CycloneGames.GameplayFramework.Tests.Editor
```

Validate the product's rollback or lockstep owner separately with its real simulation clock and target build backend.

## Troubleshooting

- If extension methods are unavailable, add `CycloneGames.RPGFoundation.Interaction.Integrations.DeterministicMath.GameplayFramework` to the consumer asmdef references.
- If an asmdef reference cannot be resolved, confirm that every package root listed in the Assets installation section is present.
- If creation returns `false`, verify Actor lifetime, provider availability, provider read success, and stable identifiers.
- If authority results differ from the deterministic simulation, confirm that the provider reads simulation state rather than rendered Transform state.
