# CycloneGames.GameplayTags.Networking

[English](./README.md) | 简体中文

`CycloneGames.GameplayTags.Networking` 是 `CycloneGames.GameplayTags` 的可选 Cyclone Networking 桥接包。它让基础 GameplayTags 包在没有 `CycloneGames.Networking` 时仍可独立使用，同时为使用 Cyclone Networking 的项目提供稳定 message ID、protocol catalog descriptor、manifest handshake message，以及 full/delta tag payload wrapper。

## 包结构

```text
CycloneGames.GameplayTags.Networking/
  Core/
    CycloneGames.GameplayTags.Networking.Core.asmdef
    GameplayTagsNetworkProtocol.cs
  Tests/Editor/
    CycloneGames.GameplayTags.Networking.Tests.Editor.asmdef
    GameplayTagsNetworkingIntegrationTests.cs
```

## 程序集边界

| Assembly | 职责 | Unity 依赖 |
| --- | --- | --- |
| `CycloneGames.GameplayTags.Networking.Core` | Message ID、message catalog 注册、manifest handshake、full/delta payload wrapper | 无 |
| `CycloneGames.GameplayTags.Networking.Tests.Editor` | Integration regression coverage | 无 |

Runtime assembly 直接引用 `CycloneGames.GameplayTags.Core` 和 `CycloneGames.Networking.Core`。它不使用 PlayerSettings scripting define symbols、service locator、Unity 生命周期钩子，或 package-driven `CYCLONE_*` 编译门控。不包含 `CycloneGames.Networking` 的项目应省略这个包，并直接使用 `CycloneGames.GameplayTags`。

## 协议

`GameplayTagsNetworkProtocol` 在通用 `NetworkMessageRanges.Module` 空间内声明自己的 package-owned 子区间（`12000-12999`），并把该 ownership 注册到 `INetworkMessageCatalog`。

| Message | ID | 用途 |
| --- | --- | --- |
| `MsgManifestHandshake` | `12000` | 在应用 tag state 前交换 `GameplayTagManager.CurrentManifestHash` 和支持的 serializer version。 |
| `MsgFullState` | `12001` | 为目标 network object 发送完整 `GameplayTagNetSerializer` payload。 |
| `MsgDelta` | `12002` | 为目标 network object 发送 delta payload。 |
| `MsgFullStateRequest` | `12003` | 在 join、reconnect、manifest reload 或 packet recovery 后请求完整刷新。 |

## 使用方式

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

当 manager 暴露 `INetworkRuntimeContextProvider` 时，非 DI bootstrap code 可以使用 `TryRegisterMessageCatalog(INetworkManager)`。DI composition root 可以直接向 `INetworkMessageCatalog` 注册。

## 持久化行为

这个包不会写入文件、资产、偏好、缓存数据或运行时存档。它只定义运行时协议元数据和 message payload struct。

## 验证

- 构建 `CycloneGames.GameplayTags.Tests.Editor`。
- Unity 刷新生成工程文件后，构建 `CycloneGames.GameplayTags.Networking.Tests.Editor`。
- 构建 `CycloneGames.Networking.Tests.Editor`。
- 在 Unity Editor 中运行 `CycloneGames.GameplayTags.Networking.Tests.Editor` EditMode tests。
