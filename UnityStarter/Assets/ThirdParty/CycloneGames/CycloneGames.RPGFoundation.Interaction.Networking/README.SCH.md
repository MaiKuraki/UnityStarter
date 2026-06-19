# CycloneGames.RPGFoundation.Interaction.Networking

[English](./README.md) | 简体中文

`CycloneGames.RPGFoundation.Interaction.Networking` 是 RPGFoundation Interaction 的可选 Cyclone Networking 桥接包。它让基础 `CycloneGames.RPGFoundation` 包在没有 `CycloneGames.Networking` 时仍可独立使用，同时为网络项目提供适合传输的 interaction DTO、`NetworkVector3` 转换、authority validation helper、稳定 message ID，以及 message catalog registration。

## 包结构

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

## 程序集边界

| Assembly | 职责 | Unity 依赖 |
| --- | --- | --- |
| `CycloneGames.RPGFoundation.Interaction.Networking.Core` | Interaction DTO、vector conversion、authority bridge、Interaction module message range、message catalog registration | 否 |
| `CycloneGames.RPGFoundation.Interaction.Networking.Tests.Editor` | Integration regression coverage | 否 |

Runtime assembly 直接引用 `CycloneGames.RPGFoundation.Interaction.Core` 和 `CycloneGames.Networking.Core`。它不使用 PlayerSettings scripting define symbols、service locator、Unity lifecycle hook、package-driven `CYCLONE_*` compile gate，也不绑定某个 DI 容器。不包含 `CycloneGames.Networking` 的项目应省略这个包，并直接使用 `CycloneGames.RPGFoundation`。

## 协议

`InteractionNetworkProtocol` 在通用 `NetworkMessageRanges.Module` 空间内声明自己的 Interaction package-owned 子区间（`13000-13999`），并把该 ownership 注册到 `INetworkMessageCatalog`。Interaction 当前占用前四个 ID。

| Message | ID | Channel | 用途 |
| --- | --- | --- | --- |
| `REQUEST_MESSAGE_ID` | `13000` | Reliable | 客户端或 peer 请求一次交互。 |
| `RESULT_MESSAGE_ID` | `13001` | Reliable | 权威端返回交互结果。 |
| `CANCEL_REQUEST_MESSAGE_ID` | `13002` | Reliable | 客户端或权威端取消待处理交互。 |
| `DETERMINISTIC_REQUEST_MESSAGE_ID` | `13003` | Reliable | 预留的确定性交互请求入口。 |

DI composition root 可以调用 `RegisterMessageCatalog(INetworkMessageCatalog)`；非 DI bootstrap code 可以在 network manager 暴露 `INetworkRuntimeContextProvider` 时调用 `TryRegisterMessageCatalog(INetworkManager)`。

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

## 使用方式

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

## Adapter 模型

这个包只依赖 Cyclone networking abstraction。Mirror、Mirage、Nakama、Photon、sharding、backend identity、anti-cheat 或具体游戏的 ownership rule 应放在更高层 adapter 包中，再把 connection/session 数据映射到这些 DTO。

## 持久化行为

这个包不写入文件、资产、偏好、缓存数据或运行时存档。它只定义 runtime protocol metadata 和 value-type DTO helper。

## 验证

- 构建 `CycloneGames.RPGFoundation.Interaction.Tests.Editor`。
- Unity 刷新生成工程文件后，构建 `CycloneGames.RPGFoundation.Interaction.Networking.Tests.Editor`。
- 构建 `CycloneGames.Networking.Tests.Editor`。
- 在 Unity Editor 中运行 `CycloneGames.RPGFoundation.Interaction.Networking.Tests.Editor` EditMode tests。
