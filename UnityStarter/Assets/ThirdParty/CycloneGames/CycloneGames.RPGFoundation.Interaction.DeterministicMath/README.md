# CycloneGames.RPGFoundation.Interaction.DeterministicMath

[Simplified Chinese](README.SCH.md)

## Overview

This companion package provides fixed-point Interaction authority types backed by `CycloneGames.DeterministicMath`. It keeps deterministic simulation values in `FPVector3` and `FPInt64` until an explicit presentation conversion is requested.

The package is Unity-free. It is suitable for authoritative servers, lockstep or rollback simulation, replay validation, and EditMode or standalone pure C# tests. The base Interaction package does not depend on DeterministicMath.

## Package Layout

```text
CycloneGames.RPGFoundation.Interaction.DeterministicMath/
  Runtime/
    CycloneGames.RPGFoundation.Interaction.Integrations.DeterministicMath.asmdef
    IInteractionDeterministicPositionProvider.cs
    InteractionDeterministicAuthorityService.cs
    InteractionDeterministicRequest.cs
    InteractionDeterministicRequestPayload.cs
    InteractionDeterministicTargetSnapshot.cs
    InteractionDeterministicVector3Payload.cs
  Tests/Editor/
    CycloneGames.RPGFoundation.Interaction.DeterministicMath.Tests.Editor.asmdef
    InteractionDeterministicMathIntegrationTests.cs
```

## Assemblies and Dependencies

| Assembly | Role | Unity dependency | Consumer reference |
| --- | --- | --- | --- |
| `CycloneGames.RPGFoundation.Interaction.Integrations.DeterministicMath` | Fixed-point DTOs, providers, conversion, and authority validation. | No | Explicit |
| `CycloneGames.RPGFoundation.Interaction.DeterministicMath.Tests.Editor` | EditMode behavior coverage. | No | Test Runner only |

The package declares direct dependencies on `com.cyclone-games.rpg-foundation` and `com.cyclone-games.deterministic-math`. Both assemblies are `autoReferenced: false`. No scripting define symbol, UnityEngine reference, runtime reflection, or DI container is required.

## Installation

### UPM

Install `com.cyclone-games.rpg-foundation-interaction-deterministic-math`. Unity Package Manager resolves the declared Interaction and DeterministicMath dependencies.

### Embedded under Assets

Place these package roots under the project Assets tree:

```text
CycloneGames.RPGFoundation/
CycloneGames.DeterministicMath/
CycloneGames.RPGFoundation.Interaction.DeterministicMath/
```

Direct asmdef references make the same assembly available in an Assets-based project. No PlayerSettings symbol or generated capability file is used.

## Authority Composition

Construct one service for one explicit authority owner and world:

```csharp
using CycloneGames.RPGFoundation.Interaction.Core;
using CycloneGames.RPGFoundation.Interaction.Integrations.DeterministicMath;

var authority = new InteractionDeterministicAuthorityService(
    new InteractionAuthorityOptions(worldId: worldId));

authority.TryRegisterTarget(new InteractionDeterministicTargetSnapshot(
    worldId,
    targetStableId,
    targetPosition,
    interactionRange,
    isAvailable: true,
    enabledActionIds: enabledActions));
```

Validate requests with an `FPVector3` or an explicit `IInteractionDeterministicPositionProvider`:

```csharp
InteractionValidationResult result = authority.ValidateRequest(
    request,
    instigatorPosition,
    serverTick);
```

`InteractionDeterministicVector3Payload` stores raw Q32.32 components and round-trips without floating-point conversion. Call `ToInteractionVector3` only at a non-authoritative presentation or reporting boundary.

## Ownership, Threading, and Performance

- The composition root owns each `InteractionDeterministicAuthorityService` and its reset or disposal boundary.
- The service is mutable and not internally synchronized. Create, configure, register, validate, queue, and clear it from one owner thread.
- DTOs and vector payloads are value types and do not allocate during conversion.
- Authority dictionaries allocate when their retained identity set grows. Establish and load-test product-specific world sharding and identity budgets before deployment.
- Action arrays supplied to target snapshots are observed by the snapshot. Treat them as immutable after registration.
- The runtime assembly contains no Unity API and is compatible with headless pure C# composition at the source level. Target runtime and AOT behavior still require target-platform builds.

## Persistence and Protocol Boundaries

This package writes no files, preferences, assets, caches, or save data. Payload structs are transport-friendly value shapes but do not define a wire codec or persistence schema. Networking, replay, and save owners must define their own bounded envelope, versioning, validation, and integrity policy.

## Validation

Run these EditMode assemblies after changing the integration:

```text
CycloneGames.RPGFoundation.Interaction.DeterministicMath.Tests.Editor
CycloneGames.RPGFoundation.Interaction.Tests.Editor
CycloneGames.DeterministicMath.Tests.Editor
```

For a server target, also compile the consumer without UnityEngine references and run the target backend and AOT build matrix used by the product.

## Troubleshooting

- If deterministic types are unavailable, add `CycloneGames.RPGFoundation.Interaction.Integrations.DeterministicMath` to the consumer asmdef references.
- If an asmdef reference cannot be resolved, confirm that all three package roots listed in the Assets installation section are present.
- If validation returns `WrongWorld`, use the same explicit world identifier for service options, target snapshots, and requests.
- If validation returns `InvalidRequest`, verify stable identifiers and the deterministic position provider before crossing the authority boundary.
