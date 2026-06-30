# CycloneGames.RPGFoundation.Projectile.Networking

English | [Simplified Chinese](./README.SCH.md)

`CycloneGames.RPGFoundation.Projectile.Networking` connects RPGFoundation Projectile to `CycloneGames.Networking`. It provides transport-neutral protocol metadata, message DTOs, validation helpers, prediction reconciliation, snapshot history, and authority bridge abstractions.

The base Projectile module remains usable without `CycloneGames.Networking`. This package is only required when projectile state crosses a Cyclone network boundary.

## Package Layout

```text
CycloneGames.RPGFoundation.Projectile.Networking/
  Core/
    CycloneGames.RPGFoundation.Projectile.Networking.Core.asmdef
    DefaultProjectileNetworkMessageValidator.cs
    IProjectileNetworkMessageValidator.cs
    IProjectileNetworkSnapshotSink.cs
    IProjectileNetworkSnapshotSource.cs
    ProjectileCorrectionMessage.cs
    ProjectileDespawnMessage.cs
    ProjectileFullStateRequestMessage.cs
    ProjectileHitMessage.cs
    ProjectileManifestHandshakeMessage.cs
    ProjectileNetworkAuthorityBridge.cs
    ProjectileNetworkCorrectionFlags.cs
    ProjectileNetworkCorrectionPolicy.cs
    ProjectileNetworkProtocol.cs
    ProjectileNetworkReconciliation.cs
    ProjectileNetworkSnapshotHistory.cs
    ProjectileNetworkValidationContext.cs
    ProjectileNetworkVectorExtensions.cs
    ProjectileSnapshotMessage.cs
    ProjectileSpawnMessage.cs
    ProjectileWorldNetworkSnapshotSource.cs
  Tests/Editor/
    CycloneGames.RPGFoundation.Projectile.Networking.Tests.Editor.asmdef
    ProjectileNetworkingIntegrationTests.cs
```

## Assembly Boundary

| Assembly | Role | Unity dependency |
| --- | --- | --- |
| `CycloneGames.RPGFoundation.Projectile.Networking.Core` | Projectile protocol, DTOs, validation, reconciliation, snapshot history, authority bridge interfaces, and `CycloneGames.Networking` integration contracts. | No UnityEngine |
| `CycloneGames.RPGFoundation.Projectile.Networking.Tests.Editor` | EditMode coverage for protocol, validator, history, reconciliation, and bridge behavior. | No UnityEngine |

The core assembly follows the existing RPGFoundation networking package style: it lives under `Core/`, its assembly name ends with `.Networking.Core`, and its root namespace remains `CycloneGames.RPGFoundation.Projectile.Networking`. It references `CycloneGames.RPGFoundation.Projectile.Core` and `CycloneGames.Networking.Core`. It does not reference Unity packages, backend SDKs, Unity scene objects, PlayerSettings scripting define symbols, concrete transports, or a DI container.

## CycloneGames.Networking Collaboration

This package is designed to work with `CycloneGames.Networking`, not replace it.

| Capability | Owner |
| --- | --- |
| Message range, manifest, channels, and catalog registration | `ProjectileNetworkProtocol` plus `CycloneGames.Networking` protocol APIs |
| Stable network vector payloads | `NetworkVector3` from `CycloneGames.Networking` |
| Validation results | `NetworkActionResult`, `NetworkActionResultCode`, and `NetworkTickId` from `CycloneGames.Networking.Simulation` |
| Snapshot history | `NetworkActionHistory<T>` from `CycloneGames.Networking.Simulation` |
| Transport, routing, sessions, security pipeline, and backend SDK adapters | Product or `CycloneGames.Networking` runtime packages |
| Projectile simulation and snapshots | `CycloneGames.RPGFoundation.Projectile.Core` |

The package intentionally does not send packets by itself. A project-specific networking adapter should register the protocol, serialize messages through the chosen transport, validate inbound messages, and forward accepted messages to gameplay systems.

## Protocol

`ProjectileNetworkProtocol` owns message ids `17000-17999` in the Cyclone module range.

| Message | ID | Channel | Payload |
| --- | ---: | --- | --- |
| `MSG_MANIFEST_HANDSHAKE` | `17000` | Reliable | `ProjectileManifestHandshakeMessage` |
| `MSG_SPAWN` | `17001` | Reliable | `ProjectileSpawnMessage` |
| `MSG_AUTHORITATIVE_SNAPSHOT` | `17002` | UnreliableSequenced | `ProjectileSnapshotMessage` |
| `MSG_CORRECTION` | `17003` | Reliable | `ProjectileCorrectionMessage` |
| `MSG_HIT` | `17004` | Reliable | `ProjectileHitMessage` |
| `MSG_DESPAWN` | `17005` | Reliable | `ProjectileDespawnMessage` |
| `MSG_FULL_STATE_REQUEST` | `17006` | Reliable | `ProjectileFullStateRequestMessage` |

Register the protocol in a composition root:

```csharp
using CycloneGames.Networking;
using CycloneGames.RPGFoundation.Projectile.Networking;

public static class ProjectileNetworkInstaller
{
    public static void Configure(INetworkMessageCatalog catalog)
    {
        ProjectileNetworkProtocol.RegisterMessageCatalog(catalog);
    }
}
```

## Validation And Reconciliation

`DefaultProjectileNetworkMessageValidator` validates projectile messages against `ProjectileNetworkValidationContext` and returns `NetworkActionResult`. It checks payload validity, authentication, tick windows, duplicate sequence state, ordered snapshots, velocity budgets, radius budgets, age budgets, and lifecycle flag masks.

```csharp
var context = new ProjectileNetworkValidationContext(
    sender,
    serverTick,
    lastAcceptedServerTick,
    lastAcceptedSequence);

NetworkActionResult result =
    DefaultProjectileNetworkMessageValidator.Instance.ValidateSnapshot(snapshot, context);

if (!result.IsAccepted)
{
    return;
}
```

`ProjectileNetworkReconciliation` compares predicted and authoritative `ProjectileSnapshotMessage` values and can create `ProjectileCorrectionMessage` values when position, velocity, timeline, lifecycle, target, or definition data diverges beyond policy thresholds.

```csharp
bool needsCorrection = ProjectileNetworkReconciliation.TryCreateCorrection(
    predicted,
    authoritative,
    ProjectileNetworkCorrectionPolicy.Default,
    out ProjectileCorrectionMessage correction);
```

`ProjectileNetworkCorrectionFlags` identifies whether the correction affects transform, velocity, timeline, lifecycle, target, hard snap, or full reset behavior.

## Authority Bridge

`ProjectileNetworkAuthorityBridge` captures authoritative snapshots from an `IProjectileNetworkSnapshotSource` and applies incoming snapshots through an optional `IProjectileNetworkSnapshotSink`.

`ProjectileWorldNetworkSnapshotSource` adapts a `ProjectileWorld` by network projectile entity id:

```csharp
var source = new ProjectileWorldNetworkSnapshotSource(projectileWorld);
var bridge = new ProjectileNetworkAuthorityBridge(source);

if (bridge.TryCaptureSnapshot(projectileEntityId, sequence, out ProjectileSnapshotMessage snapshot))
{
    // Send snapshot through the project networking adapter.
}
```

Snapshot application is intentionally delegated to project code. Visual interpolation, prediction rollback, presentation-only projectiles, and server-owned projectile worlds require different ownership policies.

## Synchronization Policy

Recommended multiplayer flow:

1. Client sends an ability or weapon request through the product-owned gameplay path.
2. Server validates cost, cooldown, tags, line-of-sight, ownership, fire rate, and anti-cheat rules.
3. Server sends `ProjectileSpawnMessage` to relevant observers.
4. Server simulates authoritative projectile state and sends `ProjectileSnapshotMessage` for long-lived projectiles.
5. Clients reconcile predicted visuals with `ProjectileCorrectionMessage`.
6. Server sends `ProjectileHitMessage` and `ProjectileDespawnMessage` reliably when authority resolves the outcome.

High-density bullet patterns should usually replicate `definitionId`, `seed`, `startTick`, and emitter parameters through a project-owned user protocol. Per-projectile snapshots should be reserved for exceptional projectiles that cannot be reconstructed from deterministic pattern state.

## Persistence

This package does not write files, assets, preferences, caches, or runtime save data. It only defines protocol metadata, value-type DTOs, validators, fixed-capacity history helpers, and explicit bridge interfaces.

## Validation

Run these checks after changing the package:

```text
Unity Test Runner > EditMode > CycloneGames.RPGFoundation.Projectile.Networking.Tests.Editor
Unity Test Runner > EditMode > CycloneGames.RPGFoundation.Projectile.Tests.Editor
Unity Test Runner > EditMode > CycloneGames.Networking.Tests.Editor
```

When Unity Editor automation is unavailable, compile the pure C# assemblies with the current branch references and verify no `UnityEngine` reference leaks into `CycloneGames.RPGFoundation.Projectile.Networking.Core`.
