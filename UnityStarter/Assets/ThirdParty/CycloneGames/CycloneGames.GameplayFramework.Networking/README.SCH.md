# CycloneGames.GameplayFramework.Networking

[English](./README.md) | 简体中文

`CycloneGames.GameplayFramework.Networking` 是 `CycloneGames.GameplayFramework` 的可选 Cyclone Networking 桥接包。它让基础 GameplayFramework 包在没有 `CycloneGames.Networking` 时仍可独立使用，同时为使用 Cyclone networking abstraction 的项目提供现成 session adapter、稳定的 GameplayFramework message ID、actor migration serialization、server-authoritative role helper，以及 owner/team/area replication 的 observer resolution。

## 包结构

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

## 程序集边界

| Assembly | 职责 | Unity 依赖 |
| --- | --- | --- |
| `CycloneGames.GameplayFramework.Networking.Core` | `GameSession` networking bridge、GameplayFramework message catalog、actor migration wire serializer、authority 和 observer helper | 是 |
| `CycloneGames.GameplayFramework.Networking.Tests.Editor` | Integration regression coverage | 是 |

Runtime assembly 直接引用 `CycloneGames.GameplayFramework.Runtime` 和 `CycloneGames.Networking.Core`。它不使用 PlayerSettings scripting define symbols、service locator、package-driven `CYCLONE_*` compile gate，也不绑定某个 DI 容器。不包含 `CycloneGames.Networking` 的项目应省略这个包，并直接使用 `CycloneGames.GameplayFramework`。

## Session 桥接

`NetworkGameSessionAdapter` 继承 `GameSession`，并把 session 规则桥接到 `INetworkManager`。它会绑定 `PlayerController` 与 `INetConnection`，在登录审批时校验连接和认证状态，把稳定的 connection player ID 投射到 `PlayerState`，并通过 `INetworkManager.DisconnectClient` 断开玩家。

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

## 权威与复制

`GameplayNetworkAuthorityRole` 描述 actor 是 `ServerAuthority`、`AutonomousProxy`、`SimulatedProxy` 还是 `None`。`ServerAuthoritativeGameplayAuthorityResolver` 适合标准 dedicated-server 和 listen-server 项目。

`GameplayReplicationPolicy` 描述 visibility、channel、distance、tick interval、priority、layer mask、是否包含 owner，以及是否要求认证连接。常用 preset 包括 `OwnerReliable`、`TeamReliable`、`AlwaysRelevantReliable` 和 `AreaUnreliable(distance)`。

`NetworkedGameplayActor` 是 `Actor` 的网络边界数据，包含 network id、owner connection、owner player id、team id、interest layer、relevance flag 和 interest position。`GameplayNetworkObserverRegistry` 与 `GameplayNetworkObserverResolver` 用于构建 area、team、owner-only 和 always-relevant replication 的 observer set。

## 协议目录

`GameplayFrameworkNetworkProtocol` 在通用 `NetworkMessageRanges.Module` 空间内声明自己的 package-owned 子区间（`11000-11999`），并把该 ownership 注册到 `INetworkMessageCatalog`。

| Message | ID | 用途 |
| --- | --- | --- |
| `MsgActorMigrationState` | `11000` | 注册并校验 actor migration state payload。 |

当 `NetworkGameSessionAdapter.SetNetworkManager()` 收到的 manager 暴露 `INetworkRuntimeContextProvider` 时，它会尝试注册 GameplayFramework catalog。DI composition root 可以直接向 `INetworkMessageCatalog` 注册。

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

## Adapter 模型

这个包只依赖 Cyclone networking abstraction。Mirror、Mirage、Nakama、Photon、自定义 transport、dedicated-server orchestration、sharding 或 backend session service 应放在更高一层 adapter 包中，再把 `INetworkManager`、`INetConnection` 和 observer 数据喂给这个桥接层。

## 持久化行为

这个包不写入文件、资产、偏好、缓存数据或运行时存档。它只定义 runtime adapter behavior、protocol metadata 和 wire serialization helper。

## 验证

- 构建 `CycloneGames.GameplayFramework.Tests.Editor`。
- Unity 刷新生成工程文件后，构建 `CycloneGames.GameplayFramework.Networking.Tests.Editor`。
- 构建 `CycloneGames.Networking.Tests.Editor`。
- 在 Unity Editor 中运行 `CycloneGames.GameplayFramework.Networking.Tests.Editor` EditMode tests。
