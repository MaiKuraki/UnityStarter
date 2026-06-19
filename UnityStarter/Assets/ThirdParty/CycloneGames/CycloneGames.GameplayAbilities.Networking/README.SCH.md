# CycloneGames.GameplayAbilities.Networking

[English](./README.md) | 简体中文

`CycloneGames.GameplayAbilities.Networking` 用于把 `CycloneGames.GameplayAbilities` 接入 `CycloneGames.Networking`。它提供与传输层无关的 Ability 激活、预测确认/拒绝、Active Gameplay Effect、Attribute、Tag、状态元数据和 Full-State Recovery 复制能力。

这个包不会把 GameplayAbilities 直接绑定到 Mirror、Mirage、Nakama 或其他 SDK。它依赖 Cyclone 网络接口，因此同一套 GAS 网络层可以运行在任何提供 `INetworkManager` 和必要后端服务的网络实现之上。

## 模块能力

- `NetworkedAbilityBridge` 负责 Ability RPC 和 GAS 复制消息。
- `INetworkedASC` 作为 Ability System Component 的网络侧契约。
- `GameplayAbilitiesNetworkedASCAdapter` 连接 Unity Runtime 的 `AbilitySystemComponent`。
- 提供 Ability 激活、Effect 复制、Attribute 更新、Tag 更新、Multicast、Full State、Sync Metadata 等紧凑消息结构。
- 提供 `GASNetworkSerializer` 和 `GASNetworkSerializerOptions`，用于有边界的确定性序列化。
- 提供 `GASNetFixed`，用于网络中的 fixed-point raw value。
- 提供 Full-State 授权策略和连接限流接口。
- 提供状态 checksum 工具，用于 drift detection 和 reconnect validation。
- 提供 Editor 诊断 preset 和 diagnostics window。

## 目录结构

```text
CycloneGames.GameplayAbilities.Networking/
  Core/             纯 C# bridge、messages、serializer、安全策略
  Unity.Runtime/    Unity AbilitySystemComponent adapter
  Editor/           Editor-only diagnostics 和 inspectors
  Tests/Editor/     Bridge、serializer、policy、adapter 测试
```

关键程序集：

| 程序集 | 说明 |
| --- | --- |
| `CycloneGames.GameplayAbilities.Networking.Core` | 纯 C# 网络 bridge、消息结构、序列化器、checksum、安全策略和接口。 |
| `CycloneGames.GameplayAbilities.Networking.Unity.Runtime` | Unity Runtime `AbilitySystemComponent` adapter。 |
| `CycloneGames.GameplayAbilities.Networking.Unity.Editor` | Editor diagnostics window、preset 和 inspector。 |
| `CycloneGames.GameplayAbilities.Networking.Tests.Editor` | Serializer、bridge、安全策略和 runtime adapter 测试。 |

## 环境要求

- `com.cyclone-games.networking`。
- `com.cyclone-games.gameplay-abilities`。
- `com.cyclone-games.gameplay-tags`。
- 一个通过 `CycloneGames.Networking` 注册的具体网络后端。

Mirror 或 Nakama 等可选 SDK 会被 Editor 诊断识别，但本包仍应通过 Cyclone 接口接入。

## 快速开始

### 1. 创建 bridge

在网络 bootstrap 中创建 `NetworkedAbilityBridge`。传入来自 Mirror、Mirage、Nakama 或自定义后端 adapter 的 `INetworkManager`。

```csharp
using CycloneGames.GameplayAbilities.Networking;
using CycloneGames.Networking;

public sealed class GASNetworkBootstrap : System.IDisposable
{
    private readonly NetworkedAbilityBridge _bridge;

    public GASNetworkBootstrap(INetworkManager networkManager)
    {
        _bridge = new NetworkedAbilityBridge(networkManager);
        _bridge.RegisterHandlers();
    }

    public void Dispose()
    {
        _bridge.Dispose();
    }
}
```

### 2. 注册网络化 Ability System Component

每个拥有 Ability System Component 的网络 Actor 应提供一个稳定的 `networkId` 和一个 owner connection id。

```csharp
using CycloneGames.GameplayAbilities.Networking;
using CycloneGames.GameplayAbilities.Runtime;

public sealed class NetworkedAbilityOwner : System.IDisposable
{
    private readonly NetworkedAbilityBridge _bridge;
    private readonly GameplayAbilitiesNetworkedASCAdapter _adapter;

    public NetworkedAbilityOwner(
        NetworkedAbilityBridge bridge,
        AbilitySystemComponent asc,
        uint networkId,
        int ownerConnectionId)
    {
        _bridge = bridge;
        _adapter = new GameplayAbilitiesNetworkedASCAdapter(asc, networkId, ownerConnectionId);
        _bridge.RegisterASC(networkId, ownerConnectionId, _adapter);
    }

    public void Dispose()
    {
        _bridge.UnregisterASC(_adapter.NetworkId, _adapter.OwnerConnectionId);
        _adapter.Dispose();
    }
}
```

### 3. 拥有者客户端请求 Ability 激活

```csharp
using CycloneGames.Networking;

public void ActivateAbility(
    NetworkedAbilityBridge bridge,
    int abilityIndex,
    int predictionKey,
    NetworkVector3 targetPosition,
    NetworkVector3 direction,
    uint targetNetworkId)
{
    bridge.ClientRequestActivateAbility(
        abilityIndex,
        predictionKey,
        targetPosition,
        direction,
        targetNetworkId);
}
```

服务器在 Gameplay 代码中校验请求，然后调用 `ServerConfirmActivation` 或 `ServerRejectActivation`。客户端根据确认或拒绝保留或回滚本地预测状态。

### 4. 服务器复制 Effect、Attribute 和 Tag

服务器通过 bridge 把复制状态发送给 owner 或 observer connections：

```csharp
bridge.ServerReplicateEffectApplied(observers, targetNetworkId, effectData);
bridge.ServerBroadcastAttributes(observers, targetNetworkId, attributeData);
bridge.ServerSyncTags(observers, targetNetworkId, tagData);
```

`observers` 可以来自 GameplayFramework、兴趣管理系统或项目自己的房间/区域逻辑。

## 主要 Runtime 类型

### `NetworkedAbilityBridge`

`NetworkedAbilityBridge` 是 GAS 网络桥接中心。它负责消息注册、类型化 RPC 收发、按 connection id 或 network id 查找 ASC、处理 full-state 请求，并把事件转发给 Gameplay 代码。

默认 message id：

| Message | Id |
| --- | ---: |
| `MsgAbilityActivateRequest` | 10000 |
| `MsgAbilityActivateConfirm` | 10001 |
| `MsgAbilityActivateReject` | 10002 |
| `MsgAbilityEnd` | 10003 |
| `MsgAbilityCancel` | 10004 |
| `MsgEffectApplied` | 10010 |
| `MsgEffectRemoved` | 10011 |
| `MsgEffectStackChanged` | 10012 |
| `MsgEffectUpdated` | 10013 |
| `MsgAttributeUpdate` | 10020 |
| `MsgTagUpdate` | 10025 |
| `MsgAbilityMulticast` | 10030 |
| `MsgFullState` | 10040 |
| `MsgFullStateRequest` | 10041 |
| `MsgStateSyncMetadata` | 10042 |

这些 id 位于通用 `NetworkMessageRanges.Module` 空间中的 `NetworkedAbilityBridge.MessageRange` package-owned 子区间（`10000-10999`）；当 runtime context 暴露 `INetworkMessageCatalog` 时，区间和 descriptor 会自动注册进去。项目应维护自己的 message id 分配表，避免与已注册的 module range 冲突。

### `INetworkedASC`

`INetworkedASC` 是 Ability System Component 的网络侧契约。它接收服务端确认、拒绝、复制 effect、attribute update、tag update、multicast、full state 和 state metadata。

纯 C# 服务端测试或自定义 Ability Runtime 可以直接实现这个接口。Unity Runtime 的 `AbilitySystemComponent` 推荐使用 `GameplayAbilitiesNetworkedASCAdapter`。

### `GameplayAbilitiesNetworkedASCAdapter`

`GameplayAbilitiesNetworkedASCAdapter` 把 Unity `AbilitySystemComponent` 连接到 `INetworkedASC`。

它支持：

- 注册到 `IGASNetIdRegistry`。
- Prediction key 确认和拒绝回调。
- 复制 Active Effect 的 apply、remove、stack change、update。
- Attribute id 注册和 observer filtering。
- Tag 复制。
- Full-State capture 和 apply。
- State delta 创建。
- 严格 checksum validation。
- 可选 runtime thread policy 检查。

Adapter 实现了 `IDisposable`。拥有它的 Actor 或 Ability Component 销毁时应显式释放。

## Full-State Recovery

Full-State 消息用于客户端 late join、reconnect、检测到 drift 或需要 baseline reset 的情况。

典型流程：

1. 客户端调用 `ClientRequestFullState(targetNetworkId)`。
2. 服务器检查 `FullStateRequestAuthorizer`。
3. 服务器捕获 `GASFullStateData`。
4. 服务器发送 `MsgFullState` 给请求连接。
5. 客户端通过 `INetworkedASC.OnFullState` 应用状态。

如果需要针对不同连接过滤可见状态，实现 `INetworkedASCConnectionScopedFullState`。这样服务器可以为某个 observer 返回过滤后的 state snapshot。

## 授权与限流

Full-State 请求可能暴露敏感 Gameplay 状态。建议配置 bridge 授权：

```csharp
bridge.ConfigureFullStateAuthorization(
    new OwnerOrObserverWithRateLimitPolicy(new InMemoryTokenBucketRateLimiter(4f, 1f)),
    getOwnerConnectionId,
    getObservers);
```

推荐规则：

- Owner 可以请求自己的 full state。
- Observer 只能请求自己有权观察的 state。
- 重复请求应限流。
- 项目有 `IGASSecurityAuditSink` 时，应记录未授权请求。

## 序列化选项

`GASNetworkSerializerOptions` 控制数组上限和序列化行为。常见规模可以使用 profile：

```csharp
var options = GASNetworkSerializerOptions.CreateForProfile(GASNetworkCapacityProfile.Conservative);
var bridge = new NetworkedAbilityBridge(networkManager, options);
```

可用 profile 包括 conservative 和 large-server-oriented 容量。建议选择满足游戏模式的最小 profile，并用至少两倍预期峰值做压力测试。

当 `INetworkManager` 实现 `INetworkSerializerConfigurable` 时，bridge 可以自动安装 `GASNetworkSerializer`。

## 预测与 Drift 处理

本包支持标准客户端预测流程：

1. 客户端本地预测 Ability 激活。
2. 客户端携带 prediction key 发送 `AbilityActivateRequest`。
3. 服务器校验 authority、cooldown、cost、tag、range、target 和游戏规则。
4. 服务器发送 `AbilityActivateConfirm` 或 `AbilityActivateReject`。
5. 客户端保留或回滚预测状态。

`GASStateSyncMetadata` 和 checksum 工具用于发现 drift：

- Out-of-order sequence。
- Base version mismatch。
- Checksum mismatch。
- Target network id mismatch。
- Invalid version range。

检测到 drift 后，客户端可以请求 full-state baseline。

## 后端兼容

本包只对接 `CycloneGames.Networking`，不直接对接某个后端 SDK。

| 后端 | 推荐接入方式 |
| --- | --- |
| Mirror | 使用 `CycloneGames.Networking.Adapter.Mirror`，再从暴露的 `INetworkManager` 创建 `NetworkedAbilityBridge`。 |
| Mirage | 使用 `CycloneGames.Networking.Adapter.Mirage`，GAS 代码保持在 Cyclone 接口上。 |
| Nakama | 通过 Cyclone 后端服务使用 session、matchmaking、match state、RPC 或 presence。实时 GAS 复制仍通过 `INetworkManager`。 |
| Best HTTP | 用于后端 HTTP/RPC/download。实时 GAS 复制仍放在 Cyclone networking 后面。 |
| Custom server | 实现 `INetworkManager` 和需要的 Cyclone 服务，GAS bridge 无需修改。 |

## Editor 诊断

创建 preset：

```text
Create > CycloneGames > GameplayAbilities > Networking > Diagnostics Preset
```

打开诊断：

```text
Tools > CycloneGames > GameplayAbilities > Networking > Diagnostics
Tools > CycloneGames > GameplayAbilities > Networking > Run Diagnostics Check
```

诊断会检查：

- 是否具备所需 bridge type 支持。
- 是否具备 Ability runtime 支持。
- 是否具备 Cyclone network runtime 支持。
- 场景中是否缺少 `INetworkManager`。
- 是否检测到 Mirror 和 Nakama 等可选 SDK。

Preset 只用于 Editor，不会增加 Runtime 对可选 SDK 包的依赖。

## 测试

修改本包后推荐执行：

```text
dotnet build UnityStarter/CycloneGames.GameplayAbilities.Networking.Core.csproj -nologo
dotnet build UnityStarter/CycloneGames.GameplayAbilities.Networking.Unity.Runtime.csproj -nologo
dotnet build UnityStarter/CycloneGames.GameplayAbilities.Networking.Tests.Editor.csproj -nologo
```

在 Unity Editor 中打开：

```text
Window > General > Test Runner
```

重点运行 serializer bounds、full-state authorization、checksum drift detection、adapter dispose behavior 和 bridge register/unregister lifecycle 测试。

## 实践建议

- Ability 校验保持服务端权威。
- Prediction key 只用于本地预测状态，不代表权限证明。
- Observer list 应显式并具备 interest-aware 过滤。
- Full-State snapshot 应按目标连接过滤。
- 启动时注册 handlers，teardown 时注销。
- 拥有 Actor 销毁时释放 `GameplayAbilitiesNetworkedASCAdapter`。
- 根据预期峰值预设 serializer profile。
- GAS 代码不要直接调用 Mirror、Mirage、Nakama 或 Best HTTP SDK 类型。
- 项目级维护 message id 分配表。

## 相关包

- `CycloneGames.Networking` 提供本包使用的通用网络层。
- `CycloneGames.GameplayAbilities` 提供 Ability System runtime。
- `CycloneGames.GameplayFramework` 可以为 Ability 状态复制提供 owner、observer、team 和 area-interest 信息。
