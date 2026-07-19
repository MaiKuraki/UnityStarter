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

`CycloneGames.RPGFoundation.Projectile.Networking.Core` uses the `CycloneGames.RPGFoundation.Projectile.Networking` root namespace and references `CycloneGames.RPGFoundation.Projectile.Core` and `CycloneGames.Networking.Core`. It does not reference Unity packages, backend SDKs, Unity scene objects, PlayerSettings scripting define symbols, concrete transports, or a DI container.

The Core and EditMode test assemblies use `autoReferenced: false`. Consumer asmdefs must reference Core explicitly. No PlayerSettings scripting define is required.

## CycloneGames.Networking Collaboration

`CycloneGames.Networking` owns shared networking infrastructure. This package owns projectile protocol metadata, DTOs, validation, reconciliation, snapshot history, and authority bridge contracts.

| Capability | Owner |
| --- | --- |
| Message range, manifest, channels, and catalog registration | `ProjectileNetworkProtocol` plus `CycloneGames.Networking` protocol APIs |
| Stable network vector payloads | `NetworkVector3` from `CycloneGames.Networking` |
| Validation results | `NetworkActionResult`, `NetworkActionResultCode`, and `NetworkTickId` from `CycloneGames.Networking.Simulation` |
| Snapshot history | `NetworkActionHistory<T>` from `CycloneGames.Networking.Simulation` |
| Transport, routing, sessions, security pipeline, and backend SDK adapters | Product or `CycloneGames.Networking` runtime packages |
| Projectile simulation and snapshots | `CycloneGames.RPGFoundation.Projectile.Core` |

This package does not send packets. A project-specific networking adapter registers the protocol, serializes messages through the selected transport, validates inbound messages, and forwards accepted messages to gameplay systems.

## Protocol

`ProjectileNetworkProtocol` owns message IDs `17000-17999` inside the shared `NetworkMessageRanges.Module` range (`1000-29999`). `CreateProtocolManifest` builds the complete manifest, and `RegisterMessageCatalog` / `TryRegisterMessageCatalog` submit it through `TryRegisterProtocolManifest`. Registration either commits the range and every descriptor together or rejects the manifest without a partial catalog update.

Every descriptor declares an explicit printable-ASCII `ContractId` such as `ProjectileSpawnMessage:v1`. Its nonzero `SchemaHash` is the FNV-1a 64-bit hash of that exact identifier; manifest validation rejects a mismatch. The protocol fingerprint includes the range and each descriptor's message ID, contract identity, schema hash, channel, and payload limit. CLR type names and reflection are not protocol identity. A payload layout, codec, or semantic compatibility change must receive a new contract identity and coordinate `CurrentVersion` / `MinimumSupportedVersion` across all communicating peers. Incompatible peers must be rejected before gameplay traffic. Project-specific messages belong in a separate project-owned manifest using an unclaimed `NetworkMessageRanges.User` subrange; this module exposes no dynamic descriptor-registration facade.

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

Authentication is fail-closed: a missing `Sender` is unauthenticated and produces `Unauthorized`. Trusted in-process paths must still provide an explicit authenticated connection context or use a separately validated project boundary; `null` is never an authentication bypass.

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

Project code owns snapshot application, including the ownership policy for visual interpolation, prediction rollback, presentation-only projectiles, and server-owned projectile worlds.

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
