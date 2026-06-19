# CycloneGames.GameplayFramework.Networking

English | [Simplified Chinese](./README.SCH.md)

`CycloneGames.GameplayFramework.Networking` is the optional Cyclone Networking bridge for `CycloneGames.GameplayFramework`. It keeps the base GameplayFramework package usable without `CycloneGames.Networking`, while giving Cyclone-based projects a ready session adapter, stable GameplayFramework message IDs, actor migration serialization, server-authoritative role helpers, and observer resolution for owner/team/area replication.

## Package Layout

```text
CycloneGames.GameplayFramework.Networking/
  Core/
    CycloneGames.GameplayFramework.Networking.Core.asmdef
    ActorMigrationNetworkingExtensions.cs
    GameplayFrameworkNetworkProtocol.cs
    GameplayNetworkObserverRegistry.cs
    GameplayNetworkReplication.cs
    NetworkGameSessionAdapter.cs
  Tests/Editor/
    CycloneGames.GameplayFramework.Networking.Tests.Editor.asmdef
    GameplayNetworkReplicationTests.cs
```

## Assembly Boundary

| Assembly | Responsibility | Unity dependency |
| --- | --- | --- |
| `CycloneGames.GameplayFramework.Networking.Core` | `GameSession` networking bridge, GameplayFramework message catalog, actor migration wire serializer, authority and observer helpers | Yes |
| `CycloneGames.GameplayFramework.Networking.Tests.Editor` | Integration regression coverage | Yes |

The runtime assembly directly references `CycloneGames.GameplayFramework.Runtime` and `CycloneGames.Networking.Core`. It does not use PlayerSettings scripting define symbols, service locators, package-driven `CYCLONE_*` compile gates, or a specific DI container. Projects that do not include `CycloneGames.Networking` should omit this package and use `CycloneGames.GameplayFramework` directly.

## Session Bridge

`NetworkGameSessionAdapter` extends `GameSession` and bridges session rules to `INetworkManager`. It binds `PlayerController` instances to `INetConnection`, validates connected and authenticated connections during login approval, projects stable connection player IDs into `PlayerState`, and disconnects players through `INetworkManager.DisconnectClient`.

```csharp
using CycloneGames.GameplayFramework.Networking;
using CycloneGames.GameplayFramework.Runtime;
using CycloneGames.Networking;

public sealed class MyNetworkSessionInstaller
{
    public void Configure(NetworkGameSessionAdapter session, INetworkManager networkManager)
    {
        session.SetNetworkManager(networkManager);
    }

    public void BindPlayer(NetworkGameSessionAdapter session, PlayerController controller, INetConnection connection)
    {
        session.BindConnection(controller, connection);
    }
}
```

## Authority and Replication

`GameplayNetworkAuthorityRole` describes whether an actor is `ServerAuthority`, `AutonomousProxy`, `SimulatedProxy`, or `None`. `ServerAuthoritativeGameplayAuthorityResolver` covers standard dedicated-server and listen-server projects.

`GameplayReplicationPolicy` describes visibility, channel, distance, tick interval, priority, layer mask, owner inclusion, and authentication requirements. Common presets include `OwnerReliable`, `TeamReliable`, `AlwaysRelevantReliable`, and `AreaUnreliable(distance)`.

`NetworkedGameplayActor` is the boundary data for an `Actor`: network id, owner connection, owner player id, team id, interest layer, relevance flag, and interest position. `GameplayNetworkObserverRegistry` and `GameplayNetworkObserverResolver` build observer sets for area, team, owner-only, and always-relevant replication.

## Protocol Catalog

`GameplayFrameworkNetworkProtocol` declares a package-owned sub-range inside the generic `NetworkMessageRanges.Module` space (`11000-11999`) and registers that ownership with `INetworkMessageCatalog`.

| Message | ID | Purpose |
| --- | --- | --- |
| `MsgActorMigrationState` | `11000` | Register and validate actor migration state payloads. |

`NetworkGameSessionAdapter.SetNetworkManager()` tries to register the GameplayFramework catalog when the manager exposes `INetworkRuntimeContextProvider`. DI composition roots can register directly against `INetworkMessageCatalog`.

```csharp
using CycloneGames.GameplayFramework.Networking;
using CycloneGames.Networking;

public sealed class GameplayNetworkComposition
{
    public void Configure(INetworkMessageCatalog catalog)
    {
        GameplayFrameworkNetworkProtocol.RegisterMessageCatalog(catalog);
    }
}
```

## Adapter Model

This package depends only on the Cyclone networking abstraction. Mirror, Mirage, Nakama, Photon, custom transport, dedicated-server orchestration, sharding, or backend session services should live in a higher adapter package that feeds `INetworkManager`, `INetConnection`, and observer data into this bridge.

## Persistence

This package does not write files, assets, preferences, cache data, or runtime save data. It only defines runtime adapter behavior, protocol metadata, and wire serialization helpers.

## Validation

- Build `CycloneGames.GameplayFramework.Tests.Editor`.
- Build `CycloneGames.GameplayFramework.Networking.Tests.Editor` after Unity refreshes generated project files.
- Build `CycloneGames.Networking.Tests.Editor`.
- In Unity Editor, run the `CycloneGames.GameplayFramework.Networking.Tests.Editor` EditMode tests.
