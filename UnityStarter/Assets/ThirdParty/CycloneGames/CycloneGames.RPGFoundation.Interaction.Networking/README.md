# CycloneGames.RPGFoundation.Interaction.Networking

English | [Simplified Chinese](./README.SCH.md)

`CycloneGames.RPGFoundation.Interaction.Networking` is the optional Cyclone Networking bridge for RPGFoundation Interaction. It keeps the base `CycloneGames.RPGFoundation` package usable without `CycloneGames.Networking`, while giving networked projects transport-friendly interaction DTOs, `NetworkVector3` conversion, authority validation helpers, stable message IDs, and message catalog registration.

## Package Layout

```text
CycloneGames.RPGFoundation.Interaction.Networking/
  Core/
    CycloneGames.RPGFoundation.Interaction.Networking.Core.asmdef
    InteractionNetworkAuthorityBridge.cs
    InteractionNetworkCancelRequest.cs
    InteractionNetworkProtocol.cs
    InteractionNetworkRequest.cs
    InteractionNetworkResult.cs
    InteractionNetworkVectorExtensions.cs
  Tests/Editor/
    CycloneGames.RPGFoundation.Interaction.Networking.Tests.Editor.asmdef
    InteractionNetworkingIntegrationTests.cs
```

## Assembly Boundary

| Assembly | Responsibility | Unity dependency |
| --- | --- | --- |
| `CycloneGames.RPGFoundation.Interaction.Networking.Core` | Interaction DTOs, vector conversion, authority bridge, Interaction module message range, message catalog registration | No |
| `CycloneGames.RPGFoundation.Interaction.Networking.Tests.Editor` | Integration regression coverage | No |

The runtime assembly directly references `CycloneGames.RPGFoundation.Interaction.Core` and `CycloneGames.Networking.Core`. It does not use PlayerSettings scripting define symbols, service locators, Unity lifecycle hooks, package-driven `CYCLONE_*` compile gates, or a specific DI container. Projects that do not include `CycloneGames.Networking` should omit this package and use `CycloneGames.RPGFoundation` directly.

## Protocol

`InteractionNetworkProtocol` declares a package-owned Interaction sub-range inside the generic `NetworkMessageRanges.Module` space (`13000-13999`) and registers that ownership with `INetworkMessageCatalog`. Interaction currently owns the first four IDs.

| Message | ID | Channel | Purpose |
| --- | --- | --- | --- |
| `REQUEST_MESSAGE_ID` | `13000` | Reliable | Client or peer requests an interaction. |
| `RESULT_MESSAGE_ID` | `13001` | Reliable | Authority returns an interaction result. |
| `CANCEL_REQUEST_MESSAGE_ID` | `13002` | Reliable | Client or authority cancels a pending interaction. |
| `DETERMINISTIC_REQUEST_MESSAGE_ID` | `13003` | Reliable | Reserved deterministic interaction request entry point. |

Use `RegisterMessageCatalog(INetworkMessageCatalog)` from DI composition roots, or `TryRegisterMessageCatalog(INetworkManager)` from non-DI bootstrap code when the network manager exposes `INetworkRuntimeContextProvider`.

```csharp
using CycloneGames.Networking;
using CycloneGames.RPGFoundation.Interaction.Networking;

public sealed class InteractionNetworkComposition
{
    public void Configure(INetworkMessageCatalog catalog)
    {
        InteractionNetworkProtocol.RegisterMessageCatalog(catalog);
    }
}
```

## Usage

```csharp
using CycloneGames.Networking;
using CycloneGames.RPGFoundation.Interaction.Core;
using CycloneGames.RPGFoundation.Interaction.Networking;

public sealed class InteractionRequestMapper
{
    public InteractionNetworkRequest CreateNetworkRequest(InteractionRequest request, InteractionVector3 instigatorPosition)
    {
        return InteractionNetworkRequest.From(request, instigatorPosition.ToNetworkVector3());
    }

    public InteractionValidationResult Validate(InteractionAuthorityService authority, InteractionNetworkRequest request, int serverTick)
    {
        return authority.ValidateNetworkRequest(request, serverTick);
    }
}
```

## Adapter Model

This package depends only on the Cyclone networking abstraction. Mirror, Mirage, Nakama, Photon, sharding, backend identity, anti-cheat, or game-specific ownership rules should live in higher-level adapter packages that map their connection/session data into these DTOs.

## Persistence

This package does not write files, assets, preferences, cache data, or runtime save data. It only defines runtime protocol metadata and value-type DTO helpers.

## Validation

- Build `CycloneGames.RPGFoundation.Interaction.Tests.Editor`.
- Build `CycloneGames.RPGFoundation.Interaction.Networking.Tests.Editor` after Unity refreshes generated project files.
- Build `CycloneGames.Networking.Tests.Editor`.
- In Unity Editor, run the `CycloneGames.RPGFoundation.Interaction.Networking.Tests.Editor` EditMode tests.
