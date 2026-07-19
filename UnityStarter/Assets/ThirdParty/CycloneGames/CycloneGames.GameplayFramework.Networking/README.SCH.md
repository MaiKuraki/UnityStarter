# CycloneGames.GameplayFramework.Networking

[English](./README.md) | 简体中文

`CycloneGames.GameplayFramework.Networking` 将 `CycloneGames.GameplayFramework` 接入 `CycloneGames.Networking`。它提供 authoritative session adapter、GameplayFramework 消息 catalog、actor migration wire contract、server-authoritative damage 消息、authority role helper，以及 owner、team、area、always-relevant replication 的 observer 选择能力。

基础 `CycloneGames.GameplayFramework.Runtime` 程序集保持网络无关。只有当 GameplayFramework 对象参与 Cyclone Networking 流程时，项目才需要引用本 integration assembly。

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
    Damage/
      DamageNetworkMessages.cs
      ServerDamageValidation.cs
  Tests/Editor/
    CycloneGames.GameplayFramework.Networking.Tests.Editor.asmdef
    ActorMigrationNetworkingExtensionsTests.cs
    GameplayNetworkReplicationTests.cs
    ServerDamageValidationTests.cs
```

## 程序集边界

| Assembly | 职责 | Unity 依赖 | 引用行为 |
| --- | --- | --- | --- |
| `CycloneGames.GameplayFramework.Networking.Core` | Session bridge、协议注册、actor migration DTO/codec、damage 消息、authority helper、observer registry 和 observer resolution。 | 有 | 显式 opt-in：`autoReferenced` 为 `false`；每个使用它的项目/integration asmdef 都必须直接引用该 assembly。 |
| `CycloneGames.GameplayFramework.Networking.Tests.Editor` | 覆盖 migration、协议注册、authority/observer 选择和 damage validation 的 EditMode 测试。 | 有，仅 Editor | `autoReferenced` 为 `false` 的 test assembly；它显式引用 Runtime、Networking Core 和共享 Networking assembly。 |

Core assembly 直接引用 `CycloneGames.GameplayFramework.Runtime` 和 `CycloneGames.Networking.Core`。它不引用 Editor assembly，不排除任何平台，也不依赖具体 transport SDK 或 DI 容器。由于它不会自动引用，Unity predefined assembly 和无关 asmdef 不会隐式获得该依赖。

`ActorMigrationState` 归属于 `CycloneGames.GameplayFramework.Networking` namespace，并编译在 `CycloneGames.GameplayFramework.Networking.Core` 中。因此，直接使用该 DTO 的 consumer 必须引用 Networking Core assembly。Catalog descriptor 不保存 runtime type name；message `11000` 使用显式 wire contract ID `ActorMigrationState:v1`。

## 核心概念

| 类型 | 作用 |
| --- | --- |
| `NetworkGameSessionAdapter` | 纯 C# `GameSession` 实现，按 player id stage 已认证 connection、绑定成功的 `PlayerController`，并应用 connection-aware admission、kick 和有界内存 address-ban 规则。 |
| `PlayerLoginRequest` | 有界 GameplayFramework admission 输入，由 `GameMode.LoginAsync` 和 `NetworkGameSessionAdapter.ApproveLogin` 消费，并包含仅供可信 composition 使用的 `IsLocal` 标记。 |
| `ActorMigrationState` | 注册为消息 `11000` 的版本 1 actor migration DTO。 |
| `ActorMigrationNetworkingExtensions` | Capture/apply Unity actor 状态，并读写固定的版本 1 payload。 |
| `GameplayFrameworkNetworkProtocol` | 拥有 module manifest、消息范围、显式 contract identity、协议版本/fingerprint 和原子 catalog 注册。 |
| `DamageRequestMessage` / `DamageResultMessage` | Client intent 和 server-authoritative damage 结果消息。 |
| `ServerAuthoritativeGameplayAuthorityResolver` | 解析 server authority、autonomous proxy 和 simulated proxy role。 |
| `NetworkedGameplayActor` | 将 `Actor` 映射为 network id、ownership、team、layer、relevance 和 interest-position 数据。 |
| `GameplayReplicationPolicy` | 携带 visibility、channel、distance、tick interval、priority、layer、owner-inclusion 和 authentication metadata。 |
| `GameplayNetworkObserverRegistry` | 按 connection id 保存 `NetworkInterestObserver` 数据。 |
| `GameplayNetworkObserverResolver` | 按 connection state、authentication、ownership、team、area 和 relevance policy 过滤 caller-owned candidate connections。 |

## 组合与 Session 流程

Authority composition root 拥有 session object。`NetworkGameSessionAdapter` 不是 `MonoBehaviour`，不得作为 scene component 放置。

1. 在 authority composition root 中构造 `NetworkGameSessionAdapter(maxPlayers, maxSpectators)`。
2. 使用当前 `INetworkMessageEndpoint` 调用 `SetMessageEndpoint`。当 endpoint 通过 `INetworkRuntimeContextProvider` 暴露 `INetworkMessageCatalog` 时，该调用会尝试注册 GameplayFramework catalog。
3. 将 session 传入 `GameInstance.StartWorldAsync(settings, authorityNetMode, gameSession: session, cancellationToken: cancellationToken)`。
4. `World.InitializeAsync` 使用该 session 生成并初始化 authoritative `GameMode`。由 `GameMode` 创建的 host/local request 使用 `IsLocal = true`，不要求 staged network connection。`StartWorldAsync` 返回后，从 `world.GameMode` 获取已初始化实例。
5. 由 transport 完成 connection authentication 和 rate limit，分配正数 authoritative player id，再调用 `TryStageConnection(playerId, connection, out error)`。
6. 使用相同 player id 和 connection address 创建 `PlayerLoginRequest`，再调用 `GameMode.LoginAsync`。
7. `TryRegisterPlayer` 自动消费 staged connection，并绑定成功的 `PlayerController`。
8. Login 未成功时，composition root 必须调用 `RemoveStagedConnection(playerId, connection)`。
9. Teardown 时，在 World owner thread 上使用 `GameMode.Logout`、`UnregisterPlayer`、`KickPlayer` 或 `BanPlayer`。

```csharp
using System;
using System.Threading;
using CycloneGames.GameplayFramework.Networking;
using CycloneGames.GameplayFramework.Runtime;
using CycloneGames.Networking;
using Cysharp.Threading.Tasks;

public sealed class GameplayNetworkLoginBridge
{
    private readonly GameMode gameMode;
    private readonly NetworkGameSessionAdapter session;

    private GameplayNetworkLoginBridge(
        GameMode gameMode,
        NetworkGameSessionAdapter session)
    {
        this.gameMode = gameMode;
        this.session = session;
    }

    public static async UniTask<GameplayNetworkLoginBridge> StartAuthorityWorldAsync(
        GameInstance gameInstance,
        WorldSettings settings,
        WorldNetMode authorityNetMode,
        INetworkMessageEndpoint messageEndpoint,
        CancellationToken cancellationToken)
    {
        if (authorityNetMode == WorldNetMode.Client)
        {
            throw new ArgumentOutOfRangeException(nameof(authorityNetMode), "An authority net mode is required.");
        }

        var session = new NetworkGameSessionAdapter(maxPlayers: 64, maxSpectators: 8);
        session.SetMessageEndpoint(messageEndpoint);

        World world = await gameInstance.StartWorldAsync(
            settings,
            authorityNetMode,
            gameSession: session,
            cancellationToken: cancellationToken);

        GameMode gameMode = world.GameMode
            ?? throw new InvalidOperationException("The authority World did not create a GameMode.");
        return new GameplayNetworkLoginBridge(gameMode, session);
    }

    public async UniTask<PlayerLoginResult> LoginAsync(
        INetConnection connection,
        int playerId,
        string playerName,
        string validatedOptions,
        CancellationToken cancellationToken)
    {
        if (connection == null || !connection.IsConnected || !connection.IsAuthenticated)
        {
            return PlayerLoginResult.Failure(PlayerLoginStatus.Rejected, "Connection is not ready.");
        }

        if (!session.TryStageConnection(playerId, connection, out string stageError))
        {
            return PlayerLoginResult.Failure(PlayerLoginStatus.Rejected, stageError);
        }

        try
        {
            var request = new PlayerLoginRequest(
                playerId,
                playerName,
                isSpectator: false,
                remoteAddress: connection.RemoteAddress,
                options: validatedOptions,
                isLocal: false);

            return await gameMode.LoginAsync(request, cancellationToken: cancellationToken);
        }
        finally
        {
            // Success consumes the staged entry. Failure and cancellation require cleanup.
            session.RemoveStagedConnection(playerId, connection);
        }
    }
}
```

`GameInstance.StartWorldAsync` 拥有 World 构造和一次性的 `GameMode.Initialize` 调用。World 返回后不要再次初始化 `world.GameMode`。Start call 与后续 session 操作必须遵守 GameInstance/World owner-thread 规则；backend adapter 不得从任意 transport callback thread 直接调用它们。

本地例外由两部分组成：`GameMode.CreateLocalPlayerLoginRequest` 设置 `IsLocal = true`，生成的 controller 使用 `LocalPlayer` 初始化，因此 `PlayerController.IsLocalController` 为 true。`NetworkGameSessionAdapter` 对可信 local request 跳过 staged admission，并允许可信 local controller 在没有 connection 时注册，使 listen-server local player 能在 `StartWorldAsync` 期间完成初始化。

`IsLocal` 是 server/composition trust marker，不是 network input。Transport adapter 必须忽略 client 提供的任何 local flag，并使用 `isLocal: false` 构造所有 remote `PlayerLoginRequest`。从不可信 payload 设置该值会在 `ApproveLogin` 阶段绕过 staged connection、authentication 和 ban 检查。

### PlayerLoginRequest 契约

| 字段 | 验证与所有权 |
| --- | --- |
| `PlayerId` | 基础 request 要求非负；`NetworkGameSessionAdapter` 的 staged network login 要求正数。Authoritative composition root 拥有 id 分配与冲突处理。 |
| `PlayerName` | 可选；最多 `64` 个 UTF-16 code unit。清理、唯一性与内容审核属于项目职责。 |
| `IsSpectator` | 选择独立受限的 spectator roster。 |
| `RemoteAddress` | 可选；最多 `256` 个 UTF-16 code unit。Adapter 使用它进行内存 address ban 和地址查找。`IsLocal` 为 true 时必须为 null 或空。不要在该字段中放置凭据。 |
| `Options` | 可选；最多 `1024` 个 UTF-16 code unit。解析及逐项验证属于项目职责。 |
| `IsLocal` | 默认为 `false`。只有 authoritative in-process composition 可以将其设为 `true`；remote/network-derived request 必须保持 `false`。 |

`GameSession` 默认允许 `16` 名 player 和 `4` 名 spectator。每个容量必须位于 `0` 到 `100,000`，两者之和不得超过 `100,000`。`ApproveLogin` 先验证 request 和容量，再由 adapter 应用 networking-specific 检查。

`TryStageConnection` 将 pending entry 上限设为 `maxPlayers + maxSpectators`。它拒绝非正数 id、null connection、超长或已禁止地址、已有 active connection 的重复 player id、同一 id 已 stage 的不同 connection、已分配给其他 staged 或 active player id 的同一 connection，以及容量溢出。Adapter 在 staged 与 active binding 中双向执行 one connection ↔ one PlayerId 关系。重复提交相同 player-id/connection pair 是幂等操作。删除 staged entry 或解绑 player 时会释放 reverse connection index。Staging 不能替代 transport authentication 或 flood control。

`RejectUnknownAddresses`、`RejectDisconnectedConnections` 和 `RejectUnauthenticatedConnections` 均默认为 `true`。对 remote request，admission 要求 `PlayerLoginRequest.PlayerId` 存在 staged entry；非空 request address 必须与 staged address 进行大小写不敏感匹配，并且 staged connection 必须已连接且已认证。Adapter 会在 staging 时记录 remote address snapshot，并在 approval 与 commit-time binding 阶段重新验证 connection identity、当前地址、连接状态、认证状态和 ban。地址变化、断开连接、认证失效或 staging 后新增 ban 都会拒绝 admission。Trusted local request 的 PlayerId 若与 staged remote identity 冲突，也会被拒绝。自动 binding 失败或 remote controller 缺少 staged entry 时，`TryRegisterPlayer` 会回滚 roster registration。`TryBindConnection` 用于正常 login flow 之外的显式 binding/rebinding；`BindConnection` 是其抛异常 wrapper。

Staged entry、address ban 和 player/connection index 只存放在内存中。`UnregisterPlayer` 同时解绑 connection。`KickPlayer` 请求断开后，在可用时调用 `player.World.GameMode.Logout`，使 World、`GameState`、session roster 与已生成 player object 通过 authoritative gameplay path 清理；无 `GameMode` 时回退到 `UnregisterPlayer`。`BanPlayer` 要求存在已绑定 connection 和非空地址；添加新 banned address 前执行 `MaxBannedAddresses`（`4096`）上限，然后 kick 玩家。`BanAddress` 还会执行 `PlayerLoginRequest` 地址长度限制。

必须在 stage 或注册任何 connection 之前设置 `INetworkMessageEndpoint`。重复设置同一个 endpoint 是幂等操作；存在 staged 或 active binding 时替换或清除 endpoint 会抛出异常。应先通过所属 transport 与 gameplay teardown path 清理这些 binding。

## Actor Migration 契约

### 稳定 Prefab Definition Id

`CaptureMigrationState` 要求显式传入 `prefabDefinitionId`。在同一协议兼容窗口覆盖的 server/client build、进程、save/restore 边界和内容更新之间，该值必须保持确定且一致。

- 在项目可见、纳入版本控制的 content registry、configuration asset 或 generated table 中管理 id。
- 不要使用 `UnityEngine.Object.name`、runtime instance id、临时 scene hierarchy path 或进程本地 handle 作为身份。
- 分配或生成目标 actor 前，验证该 id 是否解析到预期 spawn definition。
- 为缺失、退役、未授权或不兼容 definition 明确项目行为。

版本 1 DTO 属性名是 `PrefabAssetPath`；它承载传给 `CaptureMigrationState` 的显式 `prefabDefinitionId`。Codec 会检查 byte length，但 definition 是否存在、是否授权以及是否兼容由项目自有 registry 负责。

### Capture 与 Apply

```csharp
ActorMigrationState outbound = sourceActor.CaptureMigrationState(
    prefabDefinitionId: "actors.hero.v1",
    ownerConnectionId: ownerConnectionId,
    instigatorActorId: instigatorActorId);

writer.WriteMigrationState(outbound);

ActorMigrationState inbound = reader.ReadMigrationState();
Actor targetActor = SpawnAndRegisterFromDefinitionId(inbound.PrefabAssetPath); // Project-owned.
targetActor.ApplyMigrationState(inbound);
ResolveOwnershipAndInstigator(targetActor, inbound.OwnerConnectionId, inbound.InstigatorActorId); // Project-owned.
```

| 操作 | 包含的行为 | 明确不属于该操作的行为 |
| --- | --- | --- |
| `CaptureMigrationState` | 复制 position、rotation、scale、remaining lifespan、damageability、hidden state、actor tags、actor name、`HasBegunPlay`、稳定 definition id、owner connection id 和 instigator actor id。 | Prefab lookup、authority validation、目标生成、connection lookup 和 subclass-specific state。 |
| `WriteMigrationState` | 验证 snapshot，并按固定顺序写入版本 1 字段。 | Catalog envelope 创建、总 payload 限制执行、transport 发送、压缩、加密和 retry policy。 |
| `ReadMigrationState` | 限制 tag 分配和 string length，拒绝非有限数值，重建 DTO 并验证 snapshot。 | Definition authorization、语义 world bounds、owner/instigator resolution 和 spawn budget。 |
| `ApplyMigrationState` | 向已生成并注册的 actor 应用 transform、scale、damageability、tags、hidden state、非负 lifespan 和非空 actor name。 | 生成/注册 actor、设置 owner/instigator、调用或重放 `BeginPlay`，或把 `HasBegunPlay` 应用为 lifecycle state。 |

当 actor 含有 tag 时，`CaptureMigrationState` 会分配一个 tag array。应把 capture 当作 migration event 操作，而不是逐帧 replication 路径。

## 协议与版本 1 Wire Layout

`GameplayFrameworkNetworkProtocol` 在共享 `NetworkMessageRanges.Module` 范围（`1000-29999`）内拥有 `11000-11999` 消息 ID。当前协议版本与最低支持版本都为 `1`。`CreateProtocolManifest` 构造完整 manifest，`RegisterMessageCatalog` / `TryRegisterMessageCatalog` 通过 `TryRegisterProtocolManifest` 提交它。Catalog 要么同时提交 range 和全部 descriptor，要么拒绝 manifest 且不留下部分注册。

| 消息 | ID | 默认 channel | Catalog payload 限制 | 用途 |
| --- | ---: | --- | --- | --- |
| `MsgActorMigrationState` | `11000` | Reliable | `NetworkConstants.DefaultMaxPayloadSize * 4` | 版本 1 actor migration state。 |
| `MsgDamageRequest` | `11001` | Reliable | `49` bytes | 交给 server validation 的不可信 client damage intent。 |
| `MsgDamageResult` | `11002` | Reliable | `30` bytes | Server-authoritative damage 结果。 |

在接收流量前，从 composition root 注册 module protocol：

```csharp
using CycloneGames.GameplayFramework.Networking;
using CycloneGames.Networking;

public static class GameplayFrameworkNetworkInstaller
{
    public static void Configure(INetworkMessageCatalog catalog)
    {
        GameplayFrameworkNetworkProtocol.RegisterMessageCatalog(catalog);
    }
}
```

`ActorMigrationState.SchemaVersion` 为 `1`。Payload 不包含内嵌 schema-version byte；兼容性由 protocol manifest/catalog 及其显式 contract identity 确定。Primitive numeric field 采用 little-endian。Float 使用 32-bit IEEE 754 bit representation。String 先写 little-endian `ushort` UTF-8 byte count，再紧随对应数量的 byte。Null string 编码为空 string。字段逐项序列化，不包含 struct padding。

每个 descriptor 都声明显式的可打印 ASCII `ContractId`：`ActorMigrationState:v1`、`DamageRequestMessage:v1` 或 `DamageResultMessage:v1`。非零 `SchemaHash` 是该标识符原文的 FNV-1a 64-bit hash；manifest validation 会拒绝不匹配的组合。Protocol fingerprint 包含 range，以及每个 descriptor 的 message ID、contract identity、schema hash、channel 和 payload limit。Damage 消息还公开 `DamageWireSchemaFingerprint`，它由 canonical field offset、width、endian、payload size 和 `ServerDamageRejectReason` 数值 descriptor 计算得到。`DamageRequestMessage` 固定为 49 bytes，`DamageResultMessage` 固定为 30 bytes；manifest 注册的也是这些精确上限。`ServerDamageRejectReason.Unknown` 为 0，`Accepted` 为 1，所有拒绝码均非零，因此默认初始化的 validation 和 wire message 会 fail closed。CLR type name、reflection、field inspection 和 `SchemaVersion` 都不是自动协议 identity。任何字段顺序、编码、枚举赋值或语义兼容性变化都必须更新 canonical descriptor 与固定 byte fixture；所有通信 peer 必须部署一致的 protocol descriptor 与 fingerprint。必须在 gameplay traffic 前拒绝不兼容 peer。项目消息应放入独立的项目自有 manifest，并使用未占用的 `NetworkMessageRanges.User` 子范围；本模块不提供动态 descriptor 注册 facade。

| 顺序 | 字段 | 编码 | 版本 1 验证 |
| ---: | --- | --- | --- |
| 1 | `Position.x/y/z` | 3 × `float32` | 每个分量必须是有限值。 |
| 2 | `Rotation.x/y/z/w` | 4 × `float32` | 每个分量必须是有限值；codec 不强制 normalization。 |
| 3 | `Scale.x/y/z` | 3 × `float32` | 每个分量必须是有限值；语义 scale bounds 属于项目职责。 |
| 4 | `PrefabAssetPath` | `ushort byteCount` + UTF-8 bytes | 承载 `prefabDefinitionId`；最多 `1024` 个 UTF-8 byte。 |
| 5 | `RemainingLifeSpan` | `float32` | 必须有限且非负。 |
| 6 | `CanBeDamaged`、`Hidden`、`HasBegunPlay` | 按该顺序写入 3 × `byte` | Writer 写入 `0` 或 `1`；reader 将非零值解释为 `true`。 |
| 7 | `Tags` | `ushort tagCount`；每个 tag 为 `ushort byteCount` + UTF-8 bytes | Codec runtime count 上限为 `64`；每个编码后 tag 最多 `384` 个 UTF-8 byte。应用到 `Actor` 时还要求 tag 非空白且不超过 `128` 个 UTF-16 code unit。 |
| 8 | `OwnerConnectionId` | `int32` | Resolution 与 authorization 属于项目职责。 |
| 9 | `InstigatorActorId` | `int32` | Resolution 与 authorization 属于项目职责。 |
| 10 | `ActorName` | `ushort byteCount` + UTF-8 bytes | 最多 `256` 个 UTF-8 byte。为空时，apply 不修改目标名称。 |

公开 codec 常量为 `MaxPrefabDefinitionIdUtf8Bytes = 1024`、`MaxActorNameUtf8Bytes = 256`、`MaxTagUtf8Bytes = Actor.MaxActorTagLength * 3` 和 `DefaultMaxRuntimeTagCount = Actor.MaxActorTags`。

最小 payload 为 `61` byte。对于占用 `P` byte 的 prefab id、占用 `N` byte 的 actor name，以及各个编码后 tag 的 `Ti`，payload 大小为：

```text
61 + P + N + sum(2 + Ti)
```

Snapshot 必须同时满足逐字段限制和 message descriptor 的总 payload 限制。`WriteMigrationState` 执行逐字段规则；catalog/transport validation 负责 descriptor-level 总量限制。

`ReadMigrationState(maxRuntimeTagCount)` 采用正数 caller limit 与 `Actor.MaxActorTags`（`64`）中的较小值。传入零或负数时使用默认 runtime limit。在分配 tag array 或大型 scratch buffer 前会先检查 length prefix。Codec 还会拒绝非有限 transform 数据与无效 lifespan。`ApplyMigrationState` 会调用 `Actor.ReplaceTags`，在修改目标 tag set 前验证每个 inbound tag。项目在生成或应用不可信 state 前，必须补充 world bounds、允许的 definition、允许的 tag、ownership 和 resource budget 等语义验证。

## Authority 与 Observer 边界

### Authority

`ServerAuthoritativeGameplayAuthorityResolver` 提供 role 与 permission 决策；它不发送 packet、不应用 state，也无法阻止 caller 绕过结果。

| Context | Role 与 permission |
| --- | --- |
| 无效 actor（`NetworkId == 0` 或 interest position 非有限） | `None`；不能写 authoritative state，也不能发送 owner input。 |
| Server，包括同时是 client 的 host | `ServerAuthority`；可为有效 actor 写 authoritative state。 |
| Local connection id 等于 `OwnerConnectionId` 的 client | `AutonomousProxy`；可发送 owner input。 |
| 其他有效 client | `SimulatedProxy`；根据该 policy 不得发送 owner input。 |

Authoritative replication loop 必须在生成 state 前调用 `CanWriteAuthoritativeState`。Input bridge 必须调用 `CanSendOwnerInput`，server 仍必须验证每一条收到的 command。Migration owner/instigator id 与 damage request 字段都是不可信标识符，不是 authority proof。

### Observer 解析

`GameplayNetworkObserverResolver.ResolveObservers` 会先清空 caller 提供的 result list，再从 caller 提供的 candidate list 中添加符合条件的项。应复用这两个 list，避免不必要分配。

1. 拒绝 null、已断开，以及在 `RequireAuthenticated` 为 true 时未认证的 candidate。
2. Target 无效或 `Visibility.None` 时返回空结果。
3. `AlwaysRelevant` actor 和 `Visibility.All` 包含其余所有 candidate，包括 owner。
4. 对于其他 visibility mode，只有 `IncludeOwner` 为 true 或 visibility 为 `OwnerOnly` 时才包含 owner。
5. `Team` 要求 actor team 非零，且 observer team 数据匹配。
6. `Area` 要求存在 observer 数据、policy `MaxDistance` 为正、policy/actor/observer layer mask 有交集，并且距离不超过 policy radius。
7. `TeamOrArea` 接受 team 或 area 任一条件。

Registry 会保存 `NetworkInterestObserver.Radius`，但 area resolver 不消费该字段；area 选择使用 `GameplayReplicationPolicy.MaxDistance`。`Channel`、`MinTickInterval` 和 `Priority` 是 caller replication pipeline 的 scheduling metadata，observer resolver 不执行这些约束。Resolver 只负责选择 observer；它不建立 authority、不序列化 payload、不发送 packet，也不拥有 connection lifecycle。

## 线程、性能与平台注意事项

- `GameSession`、`NetworkGameSessionAdapter` 和 `GameplayNetworkObserverRegistry` 是 owner-thread object，并且不是 thread-safe。应在所属 World/replication thread 上调用；transport callback 在调用 GameplayFramework 或 Unity API 前必须完成 marshal。
- `CaptureMigrationState` 与 `ApplyMigrationState` 会访问 `Actor`、`Transform`、`GameObject` 等 Unity-facing state，因此属于 Unity/World owner thread。
- `WriteMigrationState` 与 `ReadMigrationState` 不访问 Unity object。只有当 DTO、reader/writer、buffer 与结果具有独占所有权，并且外围 networking 实现允许时，才可在 worker 上运行。
- 对非空 tag，migration capture 会分配 tag array。Read 会分配解码后的 string 和有界 tag array。短 UTF-8 scratch 数据使用 `stackalloc`；较大 scratch 数据从 `ArrayPool<byte>` 租用。
- 复用预分配 candidate/result collection，并预先设置 registry 容量，避免 observer resolution 期间发生 collection 扩容。
- Core asmdef 不排除任何平台，并直接使用 Unity 类型。它适用于 Unity Player 与 Unity headless/server composition，不是 Unity-free .NET assembly。
- 本包不使用 reflection-based discovery、runtime code generation 或直接 native plugin。IL2CPP、managed stripping、AOT、Burst、architecture 和目标平台行为必须通过目标 Player build 与 runtime test 验证。
- 每个 runtime composition 都必须执行协议注册。不要依赖 Editor-only discovery，或仅凭某类型位于特定 namespace 就假设它会被保留。

## 扩展点

- 实现 `IGameplayNetworkAuthorityResolver` 以接入项目 authority 规则。
- 当 observer 数据不存放在 `GameplayNetworkObserverRegistry` 中时，实现 `IGameplayNetworkObserverSource`。
- 实现 `IGameplayNetworkObserverResolver` 以采用不同 interest policy，同时保留 connection/authentication 和 allocation budget。
- 从 `NetworkGameSessionAdapter` 派生项目 admission 规则，并在适用时调用 base bounded validation。
- 通过独立的项目自有 manifest 添加项目消息，并使用未占用的 `NetworkMessageRanges.User` 子范围；不要占用其他 module 的 ID range。
- 将 Mirror、Mirage、Nakama、Photon、Steam、platform-service 和 dedicated-server transport SDK 代码放入独立 backend adapter。

## 持久化

本包不写入文件、资产、偏好设置、缓存或存档数据。

| 状态 | Owner 与生命周期 | 清理与版本控制 |
| --- | --- | --- |
| Staged connection、player/connection map 与 banned address | `NetworkGameSessionAdapter`；仅在 adapter 生命周期内存在于内存。Staging 受 participant capacity 限制，ban 上限为 `4096`。 | 清理失败/取消的 staged login、解绑/注销玩家并丢弃 adapter。不写入 Git。 |
| Observer record | `GameplayNetworkObserverRegistry`；仅在 registry 生命周期内存在于内存。 | 调用 `Remove`/`Clear` 或丢弃 registry。不写入 Git。 |
| Migration DTO 与编码 payload | Caller/network buffer；临时 transfer state。 | 按 networking buffer owner 约定释放或归还 buffer。不写入 Git。 |
| 稳定 prefab definition registry | 项目拥有，不属于本包。 | 在 owner 项目/模块中记录路径、schema version、migration policy、Git ownership 和安全退役流程。 |

## 验证

修改本包或其契约后，运行以下 EditMode suite：

```text
Unity Test Runner > EditMode > CycloneGames.GameplayFramework.Networking.Tests.Editor
Unity Test Runner > EditMode > CycloneGames.GameplayFramework.Tests.Editor
Unity Test Runner > EditMode > CycloneGames.Networking.Tests.Editor
```

Networking package 测试覆盖 migration round-trip 与 bounds、显式 definition-id capture/apply、authority role、owner/team/area observer 选择、协议范围/catalog 注册、staged session admission、disconnected/unauthenticated rejection、connection reuse、post-stage ban 和 server damage validation。基础 GameplayFramework 测试覆盖 `GameSession` roster/capacity、local-controller initialization 与 authoritative World lifecycle 行为。Networking Core 测试覆盖共享 buffer、消息 catalog、security validation 和 replication infrastructure。

在支持 batchmode 的环境中，使用 `UnityStarter/ProjectSettings/ProjectVersion.txt` 记录的 Unity 版本运行 package suite：

```text
<Unity-editor-executable> -batchmode -nographics -projectPath <repo-root>/UnityStarter -runTests -testPlatform EditMode -assemblyNames CycloneGames.GameplayFramework.Networking.Tests.Editor -testResults <test-results-path> -quit
```

发布前还应执行以下 integration 检查：

1. 在 client 与 server 注册相同 protocol manifest；验证 ID `11000`、`11001`、`11002`、版本 `1` 和一致的 protocol fingerprint。
2. 使用固定版本 1 byte fixture 验证消息 `11000`，覆盖空 string/tag 与项目批准的最大值。
3. 通过真实 backend adapter 验证无需 staging 的 listen-server local-player bootstrap，强制所有 network-derived request 使用 `IsLocal = false`，并测试 authenticated staging、one connection ↔ one PlayerId rejection、staged-capacity rejection、post-stage ban、failed-login cleanup、自动 binding、spectator/player capacity、重复注册、断开、authoritative logout、kick、`4096` 条地址 ban 上限和 unban。
4. 验证 owner、non-owner、host、dedicated server、unauthenticated、team、area、layer-mask 和 always-relevant observer 情况。
5. 对每个受支持 backend/platform combination 执行 clean target Player build 与 runtime smoke test，包括适用的 IL2CPP/AOT 和 managed stripping。
6. 使用 Unity Profiler 或平台工具，在生产规模负载下测量 migration allocation、observer selection cost、payload size 和 transport-thread marshaling。
