# CycloneGames.GameplayTags.Networking

English | [Simplified Chinese](./README.SCH.md)

`CycloneGames.GameplayTags.Networking` is the optional Cyclone Networking bridge for `CycloneGames.GameplayTags`. It keeps the base GameplayTags package usable without `CycloneGames.Networking`, while giving Cyclone-based projects stable message IDs, protocol catalog descriptors, manifest handshake messages, and full/delta tag payload wrappers.

## Package Layout

```text
CycloneGames.GameplayTags.Networking/
  Core/
    CycloneGames.GameplayTags.Networking.Core.asmdef
    GameplayTagsNetworkProtocol.cs
  Tests/Editor/
    CycloneGames.GameplayTags.Networking.Tests.Editor.asmdef
    GameplayTagsNetworkingIntegrationTests.cs
```

## Assembly Boundary

| Assembly | Responsibility | Unity dependency |
| --- | --- | --- |
| `CycloneGames.GameplayTags.Networking.Core` | Message IDs, message catalog registration, manifest handshake, full/delta payload wrappers | No |
| `CycloneGames.GameplayTags.Networking.Tests.Editor` | Integration regression coverage | No |

The runtime assembly directly references `CycloneGames.GameplayTags.Core` and `CycloneGames.Networking.Core`. It does not use PlayerSettings scripting define symbols, service locators, Unity lifecycle hooks, or package-driven `CYCLONE_*` compile gates. Projects that do not include `CycloneGames.Networking` should omit this package and use `CycloneGames.GameplayTags` directly.

## Protocol

`GameplayTagsNetworkProtocol` declares a package-owned sub-range inside the generic `NetworkMessageRanges.Module` space (`12000-12999`) and registers that ownership with `INetworkMessageCatalog`.

| Message | ID | Purpose |
| --- | --- | --- |
| `MsgManifestHandshake` | `12000` | Exchange `GameplayTagManager.CurrentManifestHash` and supported serializer versions before applying tag state. |
| `MsgFullState` | `12001` | Send a full `GameplayTagNetSerializer` payload for a target network object. |
| `MsgDelta` | `12002` | Send a delta payload for a target network object. |
| `MsgFullStateRequest` | `12003` | Request a full refresh after join, reconnect, manifest reload, or packet recovery. |

## Usage

```csharp
using CycloneGames.GameplayTags.Core;
using CycloneGames.GameplayTags.Networking;
using CycloneGames.Networking;

public sealed class TagsNetworkComposition
{
    public void Configure(INetworkMessageCatalog catalog)
    {
        GameplayTagsNetworkProtocol.RegisterMessageCatalog(catalog);
    }

    public GameplayTagPayloadMessage CreateDelta(uint targetNetworkId, GameplayTagContainer current, GameplayTagContainer previous)
    {
        byte[] payload = GameplayTagNetSerializer.SerializeDelta(current, previous);
        return GameplayTagsNetworkProtocol.CreateDeltaMessage(targetNetworkId, payload);
    }
}
```

`TryRegisterMessageCatalog(INetworkManager)` can be used by non-DI bootstrap code when the manager exposes `INetworkRuntimeContextProvider`. DI composition roots can register directly against `INetworkMessageCatalog`.

## Persistence

This package does not write files, assets, preferences, cache data, or runtime save data. It only defines runtime protocol metadata and message payload structs.

## Validation

- Build `CycloneGames.GameplayTags.Tests.Editor`.
- Build `CycloneGames.GameplayTags.Networking.Tests.Editor` after Unity refreshes generated project files.
- Build `CycloneGames.Networking.Tests.Editor`.
- In Unity Editor, run the `CycloneGames.GameplayTags.Networking.Tests.Editor` EditMode tests.
