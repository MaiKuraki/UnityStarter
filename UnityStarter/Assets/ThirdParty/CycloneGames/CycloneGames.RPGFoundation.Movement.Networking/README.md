# CycloneGames.RPGFoundation.Movement.Networking

English | [Simplified Chinese](./README.SCH.md)

`CycloneGames.RPGFoundation.Movement.Networking` connects RPGFoundation Movement to `CycloneGames.Networking`. It defines transport-neutral movement input, authoritative snapshot, correction, teleport, full-state request, authority transfer, and manifest handshake DTOs.

The base Movement module remains usable without `CycloneGames.Networking`. This bridge is only required when movement state crosses a Cyclone network boundary.

## Package Layout

```text
CycloneGames.RPGFoundation.Movement.Networking/
  Core/
    CycloneGames.RPGFoundation.Movement.Networking.Core.asmdef
    MovementAuthorityTransferMessage.cs
    MovementCorrectionMessage.cs
    MovementFullStateRequestMessage.cs
    MovementInputCommandMessage.cs
    MovementManifestHandshakeMessage.cs
    MovementNetworkAuthorityBridge.cs
    MovementNetworkProtocol.cs
    MovementNetworkSnapshotFlags.cs
    MovementNetworkSnapshotMessage.cs
    MovementNetworkVectorExtensions.cs
    MovementTeleportMessage.cs
  Tests/Editor/
    CycloneGames.RPGFoundation.Movement.Networking.Tests.Editor.asmdef
    MovementNetworkingIntegrationTests.cs
```

## Assembly Boundary

| Assembly | Role | Unity dependency |
| --- | --- | --- |
| `CycloneGames.RPGFoundation.Movement.Networking.Core` | Movement DTOs, snapshot conversion, authority bridge, message range, and protocol manifest registration. | No UnityEngine; references `Unity.Mathematics` through Movement core. |
| `CycloneGames.RPGFoundation.Movement.Networking.Tests.Editor` | EditMode coverage for protocol and bridge behavior. | No UnityEngine |

The core assembly references `CycloneGames.RPGFoundation.Movement.Core`, `CycloneGames.Networking.Core`, and `Unity.Mathematics`. It does not reference backend SDK types, PlayerSettings scripting define symbols, or a DI container.

## Core Concepts

| Type | Purpose |
| --- | --- |
| `MovementInputCommandMessage` | Carries input intent, tick data, sequence, button mask, custom flags, move axes, and aim direction. |
| `MovementNetworkSnapshotMessage` | Carries authoritative movement state converted from `MovementSnapshot`. |
| `MovementCorrectionMessage` | Carries correction data for client reconciliation. |
| `MovementTeleportMessage` | Carries authoritative teleport or hard reset data. |
| `MovementAuthorityTransferMessage` | Carries movement authority transfer data. |
| `MovementNetworkAuthorityBridge` | Captures, applies, resets, and validates movement snapshots through Movement core interfaces. |
| `MovementNetworkProtocol` | Owns the Movement message range and protocol manifest. |

## Movement Sync Flow

```mermaid
graph TD
    Input["MovementInputCommandMessage"]
    Provider["IMovementSnapshotProvider"]
    Validator["IMovementValidator"]
    Bridge["MovementNetworkAuthorityBridge"]
    Snapshot["MovementNetworkSnapshotMessage"]
    Correction["MovementCorrectionMessage"]
    Teleport["MovementTeleportMessage"]
    Manager["INetworkManager"]

    Input --> Manager
    Provider --> Bridge
    Validator --> Bridge
    Bridge --> Snapshot
    Snapshot --> Manager
    Correction --> Manager
    Teleport --> Manager
```

## Protocol

`MovementNetworkProtocol` owns message ids `16000-16999` in the Cyclone module range.

| Message | ID | Channel | Payload |
| --- | ---: | --- | --- |
| `MSG_MANIFEST_HANDSHAKE` | `16000` | Reliable | `MovementManifestHandshakeMessage` |
| `MSG_INPUT_COMMAND` | `16001` | UnreliableSequenced | `MovementInputCommandMessage` |
| `MSG_AUTHORITATIVE_SNAPSHOT` | `16002` | UnreliableSequenced | `MovementNetworkSnapshotMessage` |
| `MSG_CORRECTION` | `16003` | Reliable | `MovementCorrectionMessage` |
| `MSG_FULL_STATE_REQUEST` | `16004` | Reliable | `MovementFullStateRequestMessage` |
| `MSG_AUTHORITY_TRANSFER` | `16005` | Reliable | `MovementAuthorityTransferMessage` |
| `MSG_TELEPORT` | `16006` | Reliable | `MovementTeleportMessage` |

Register the protocol in a composition root:

```csharp
using CycloneGames.Networking;
using CycloneGames.RPGFoundation.Movement.Networking;

public static class MovementNetworkInstaller
{
    public static void Configure(INetworkMessageCatalog catalog)
    {
        MovementNetworkProtocol.RegisterMessageCatalog(catalog);
    }
}
```

## Snapshot Workflow

`MovementNetworkAuthorityBridge` works with `IMovementSnapshotProvider` and optional `IMovementValidator`:

```csharp
using CycloneGames.RPGFoundation.Movement.Core;
using CycloneGames.RPGFoundation.Movement.Networking;

public sealed class MovementSnapshotEndpoint
{
    private readonly MovementNetworkAuthorityBridge _bridge;

    public MovementSnapshotEndpoint(IMovementSnapshotProvider provider, IMovementValidator validator)
    {
        _bridge = new MovementNetworkAuthorityBridge(provider, validator);
    }

    public MovementNetworkSnapshotMessage Capture(ulong entityId, int serverTick, ushort sequence)
    {
        return _bridge.CaptureSnapshot(entityId, serverTick, sequence);
    }

    public bool Apply(MovementNetworkSnapshotMessage snapshot)
    {
        return _bridge.ApplySnapshot(snapshot);
    }
}
```

`ValidateTransition` compares two network snapshots through the optional `IMovementValidator`.

## Input Command Workflow

`MovementInputCommandMessage` keeps input extensible with `ButtonMask` and `CustomFlags`. A project assembly defines the bit meanings and converts local input into the DTO:

```csharp
using CycloneGames.Networking;
using CycloneGames.RPGFoundation.Movement.Networking;

public static class MovementInputFactory
{
    public const uint JumpButton = 1u << 0;

    public static MovementInputCommandMessage CreateJump(
        ulong entityId,
        int clientTick,
        int lastServerTick,
        ushort sequence,
        float deltaTime)
    {
        return new MovementInputCommandMessage(
            entityId,
            clientTick,
            lastServerTick,
            sequence,
            JumpButton,
            0u,
            deltaTime,
            new NetworkVector3(0f, 0f, 1f),
            new NetworkVector3(0f, 0f, 1f));
    }
}
```

## Extension Points

- Define project-specific movement verbs in a project-owned `NetworkMessageKind.User` manifest.
- Keep backend connection, ownership, and host/session logic in the network adapter.
- Use `CustomFlags` and project-owned button masks for input concepts not represented by the generic DTO fields.

## Persistence

This package does not write files, assets, preferences, caches, or runtime save data. It only defines protocol metadata, value-type DTOs, and bridge helpers.

## Validation

Run these checks after changing the package:

```text
Unity Test Runner > EditMode > CycloneGames.RPGFoundation.Movement.Networking.Tests.Editor
Unity Test Runner > EditMode > CycloneGames.RPGFoundation.Movement.Tests.Editor
Unity Test Runner > EditMode > CycloneGames.Networking.Tests.Editor
```
