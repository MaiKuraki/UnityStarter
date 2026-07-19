# CycloneGames.RPGFoundation.Projectile.Networking

[English](./README.md) | 简体中文

`CycloneGames.RPGFoundation.Projectile.Networking` 用于把 RPGFoundation Projectile 接入 `CycloneGames.Networking`。它提供传输无关的协议元数据、消息 DTO、校验辅助、预测修正、快照历史和 authority bridge 抽象。

基础 Projectile 模块不依赖 `CycloneGames.Networking`。只有当 projectile state 需要跨越 Cyclone 网络边界时，才需要使用这个包。

## 包结构

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

## 程序集边界

| Assembly | 职责 | Unity 依赖 |
| --- | --- | --- |
| `CycloneGames.RPGFoundation.Projectile.Networking.Core` | Projectile 协议、DTO、校验、修正、快照历史、authority bridge 接口，以及 `CycloneGames.Networking` 集成契约。 | 无 UnityEngine |
| `CycloneGames.RPGFoundation.Projectile.Networking.Tests.Editor` | 覆盖协议、validator、history、reconciliation 和 bridge 行为的 EditMode 测试。 | 无 UnityEngine |

`CycloneGames.RPGFoundation.Projectile.Networking.Core` 使用 `CycloneGames.RPGFoundation.Projectile.Networking` root namespace，并引用 `CycloneGames.RPGFoundation.Projectile.Core` 和 `CycloneGames.Networking.Core`。它不引用 Unity package、后端 SDK、Unity 场景对象、PlayerSettings scripting define symbol、具体 transport 或 DI 容器。

Core 与 EditMode test assembly 都设置 `autoReferenced: false`。Consumer asmdef 必须显式引用 Core。无需 PlayerSettings scripting define。

## 与 CycloneGames.Networking 协同

`CycloneGames.Networking` 负责共享 networking infrastructure。本包负责 projectile 协议元数据、DTO、校验、reconciliation、snapshot history 和 authority bridge contract。

| 能力 | Owner |
| --- | --- |
| Message range、manifest、channel 和 catalog registration | `ProjectileNetworkProtocol` 与 `CycloneGames.Networking` protocol API |
| 稳定网络向量载荷 | `CycloneGames.Networking` 的 `NetworkVector3` |
| 校验结果 | `CycloneGames.Networking.Simulation` 的 `NetworkActionResult`、`NetworkActionResultCode` 和 `NetworkTickId` |
| 快照历史 | `CycloneGames.Networking.Simulation` 的 `NetworkActionHistory<T>` |
| Transport、routing、session、security pipeline 和后端 SDK adapter | 项目层或 `CycloneGames.Networking` runtime 包 |
| Projectile 模拟与快照 | `CycloneGames.RPGFoundation.Projectile.Core` |

本包不发送网络包。项目侧 networking adapter 负责注册协议、通过选定 transport 序列化消息、校验入站消息，并把已接受的消息转发给 gameplay 系统。

## 协议

`ProjectileNetworkProtocol` 在共享 `NetworkMessageRanges.Module` 范围（`1000-29999`）内拥有 `17000-17999` 消息 ID。`CreateProtocolManifest` 构造完整 manifest，`RegisterMessageCatalog` / `TryRegisterMessageCatalog` 通过 `TryRegisterProtocolManifest` 提交它。Catalog 要么同时提交 range 和全部 descriptor，要么拒绝 manifest 且不留下部分注册。

每个 descriptor 都声明显式的可打印 ASCII `ContractId`，例如 `ProjectileSpawnMessage:v1`。非零 `SchemaHash` 是该标识符原文的 FNV-1a 64-bit hash；manifest validation 会拒绝不匹配的组合。Protocol fingerprint 包含 range，以及每个 descriptor 的 message ID、contract identity、schema hash、channel 和 payload limit。CLR type name 与 reflection 不是协议 identity。Payload layout、codec 或语义兼容性变化必须分配新 contract identity，并在所有通信 peer 之间协调 `CurrentVersion` / `MinimumSupportedVersion`。必须在 gameplay traffic 前拒绝不兼容 peer。项目专用消息应放入独立的项目自有 manifest，并使用未占用的 `NetworkMessageRanges.User` 子范围；本模块不提供动态 descriptor 注册 facade。

| Message | ID | Channel | Payload |
| --- | ---: | --- | --- |
| `MSG_MANIFEST_HANDSHAKE` | `17000` | Reliable | `ProjectileManifestHandshakeMessage` |
| `MSG_SPAWN` | `17001` | Reliable | `ProjectileSpawnMessage` |
| `MSG_AUTHORITATIVE_SNAPSHOT` | `17002` | UnreliableSequenced | `ProjectileSnapshotMessage` |
| `MSG_CORRECTION` | `17003` | Reliable | `ProjectileCorrectionMessage` |
| `MSG_HIT` | `17004` | Reliable | `ProjectileHitMessage` |
| `MSG_DESPAWN` | `17005` | Reliable | `ProjectileDespawnMessage` |
| `MSG_FULL_STATE_REQUEST` | `17006` | Reliable | `ProjectileFullStateRequestMessage` |

在 composition root 中注册协议：

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

## 校验与修正

`DefaultProjectileNetworkMessageValidator` 使用 `ProjectileNetworkValidationContext` 校验 projectile 消息，并返回 `NetworkActionResult`。它会检查 payload 有效性、认证状态、tick 窗口、重复 sequence、快照顺序、速度预算、半径预算、age 预算和 lifecycle flag mask。

认证采用 fail-closed：缺失 `Sender` 会被视为未认证并返回 `Unauthorized`。可信任的进程内路径也必须提供显式 authenticated connection context，或在项目独立验证边界完成校验；`null` 不能作为认证绕过方式。

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

`ProjectileNetworkReconciliation` 用于比较 predicted 和 authoritative `ProjectileSnapshotMessage`，并在 position、velocity、timeline、lifecycle、target 或 definition 数据超过策略阈值时创建 `ProjectileCorrectionMessage`。

```csharp
bool needsCorrection = ProjectileNetworkReconciliation.TryCreateCorrection(
    predicted,
    authoritative,
    ProjectileNetworkCorrectionPolicy.Default,
    out ProjectileCorrectionMessage correction);
```

`ProjectileNetworkCorrectionFlags` 用于标识修正影响 transform、velocity、timeline、lifecycle、target、hard snap 或 full reset 行为。

## Authority Bridge

`ProjectileNetworkAuthorityBridge` 可以从 `IProjectileNetworkSnapshotSource` 捕获 authoritative snapshot，并通过可选的 `IProjectileNetworkSnapshotSink` 应用入站 snapshot。

`ProjectileWorldNetworkSnapshotSource` 可以按 projectile network entity id 适配 `ProjectileWorld`：

```csharp
var source = new ProjectileWorldNetworkSnapshotSource(projectileWorld);
var bridge = new ProjectileNetworkAuthorityBridge(source);

if (bridge.TryCaptureSnapshot(projectileEntityId, sequence, out ProjectileSnapshotMessage snapshot))
{
    // Send snapshot through the project networking adapter.
}
```

项目代码负责 snapshot application，并为视觉插值、预测回滚、仅表现 projectile 和服务器持有的 projectile world 定义所有权策略。

## 同步策略

推荐多人流程：

1. Client 通过项目自有 gameplay path 发送 ability 或 weapon request。
2. Server 校验 cost、cooldown、tag、line-of-sight、ownership、fire rate 和 anti-cheat rule。
3. Server 向相关 observer 发送 `ProjectileSpawnMessage`。
4. Server 模拟 authoritative projectile state，并为长生命周期 projectile 发送 `ProjectileSnapshotMessage`。
5. Client 使用 `ProjectileCorrectionMessage` 修正 predicted visual。
6. Server 在权威结果确定后 reliable 发送 `ProjectileHitMessage` 和 `ProjectileDespawnMessage`。

高密度弹幕通常应通过项目自有 user protocol 同步 `definitionId`、`seed`、`startTick` 和 emitter 参数。逐 projectile snapshot 应保留给无法从 deterministic pattern state 重建的特殊 projectile。

## 持久化

本包不写文件、资产、偏好、缓存或 runtime save data。它只定义协议元数据、value-type DTO、validator、固定容量 history helper 和显式 bridge 接口。

## 验证

修改本包后运行：

```text
Unity Test Runner > EditMode > CycloneGames.RPGFoundation.Projectile.Networking.Tests.Editor
Unity Test Runner > EditMode > CycloneGames.RPGFoundation.Projectile.Tests.Editor
Unity Test Runner > EditMode > CycloneGames.Networking.Tests.Editor
```
