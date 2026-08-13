# CycloneGames.GameplayFramework.Networking

`CycloneGames.GameplayFramework.Networking` 将 `CycloneGames.Networking` 中与引擎无关的网络基础能力接入 Unity GameplayFramework Runtime。模块提供有界 wire contract、服务端权威伤害校验、复制权限与观察者策略、Actor 转移快照，以及具备网络连接管理能力的 GameSession adapter。

模块包含两个 assembly 层：

```mermaid
flowchart LR
    GFC["GameplayFramework.Core"] --> NC["GameplayFramework.Networking.Core"]
    N["Networking.Core"] --> NC
    NC --> NR["GameplayFramework.Networking.Runtime"]
    GFC --> NR
    GFR["GameplayFramework.Runtime"] --> NR
    N --> NR

    classDef core fill:#d8f3dc,stroke:#2d6a4f,color:#081c15
    classDef unity fill:#dbeafe,stroke:#1d4ed8,color:#172554
    class GFC,N,NC core
    class GFR,NR unity
```

- `CycloneGames.GameplayFramework.Networking.Core` 启用 `noEngineReferences: true`。协议定义、codec、校验、权限策略、观察者解析和网络 value type 均不依赖 `UnityEngine`。
- `CycloneGames.GameplayFramework.Networking.Runtime` 包含 `Actor`、`PlayerController`、`GameSession` 和 Unity `Vector3` adapter。

## 安装

Unity 项目需要安装：

- `com.cyclone-games.gameplay-framework`
- `com.cyclone-games.networking`
- `com.cyclone-games.gameplay-framework-networking`

Integration package 已在 `package.json` 中声明前两个依赖。如果 package 以内嵌方式放在 `Assets/` 下，必须保证对应 asmdef 同时存在，因为 Unity 不会在该位置解析本地 `package.json` 依赖。

模块不要求 Scripting Define Symbol、全局 Service Locator 或隐藏 Editor 偏好。

## 快速开始

### 注册协议

在接收 GameplayFramework 消息前注册 module manifest：

```csharp
GameplayFrameworkNetworkProtocol.RegisterMessageCatalog(messageCatalog);
```

调用 `NetworkGameSessionAdapter.SetMessageEndpoint` 时，也会通过 `INetworkMessageEndpoint` 尝试注册协议。

### 配置网络 GameSession

在 gameplay World 的 owner thread 创建 adapter：

```csharp
var session = new NetworkGameSessionAdapter(
    maxPlayers: 64,
    maxSpectators: 8);

session.SetMessageEndpoint(messageEndpoint);
```

显式 composition 可以传入任意 `IGameSession` 实现：

```csharp
IGameSession gameplaySession = new GameSession(64, 8);
var session = new NetworkGameSessionAdapter(gameplaySession);
```

该 adapter 是 sealed 类型，玩法 roster 行为委托给传入的 session。它只拥有网络连接 staging、connection-player binding、地址封禁、endpoint 注册和断线协调职责。`maximumBannedAddressCount` 配置每个 session 的内存处罚预算，且不能超过实现安全上限。

Transport callback 必须先进入有界 owner-thread queue，再访问 adapter。所有涉及 collection 的 adapter 操作都会拒绝其他线程调用。这样可以在不引入 lock 或并发 collection 的情况下保持 GameplayFramework 单 owner mutation model。

### 暂存远程连接

玩法登录之前先暂存已经完成认证的 transport connection：

```csharp
if (!session.TryStageConnection(playerId, connection, out string stageError))
{
    RejectConnection(stageError);
    return;
}

var request = new PlayerLoginRequest(
    playerId,
    playerName,
    remoteAddress: connection.RemoteAddress);

if (!session.ApproveLogin(in request, out string loginError))
{
    session.RemoveStagedConnection(playerId, connection);
    RejectConnection(loginError);
}
```

在 gameplay session 接受请求之前，模块会校验 connection 状态、认证状态、玩家身份、地址长度、staging 容量、重复 connection ID 和地址封禁。Transport `ConnectionId` 必须为正数。Staging 会冻结已验证的 ID 与地址；如果 connection identity 在提交前发生变化，approval 与 binding 会拒绝该 connection。

## 复制权限与观察者

`NetworkedGameplayActor` 是与引擎无关的复制描述值，包含网络身份、owner 身份、team、interest layer、relevance 策略和 `NetworkVector3` interest position。该类型不持有 Unity `Actor` 引用。

Unity 代码显式采样 Actor 当前坐标：

```csharp
NetworkedGameplayActor target = actor.ToNetworkedGameplayActor(
    networkId,
    ownerConnectionId,
    ownerPlayerId,
    teamId,
    interestLayerMask,
    alwaysRelevant: false);
```

`ServerAuthoritativeGameplayAuthorityResolver` 返回以下 role：

- `ServerAuthority`
- `AutonomousProxy`
- `SimulatedProxy`
- 描述值无效，或 context 既不是 server 也不是 client 时返回 `None`

Owner 匹配要求两侧 ID 都为正数。`OwnerConnectionId == 0` 表示 Actor 无主，绝不会与 `LocalConnectionId == 0` 匹配。

`GameplayNetworkObserverResolver` 对调用方传入的 candidate list 应用 owner、team、area、layer、认证和 always-relevant 规则。它使用可复用 scratch set 对正数 connection ID 去重，并在配置的 `MaximumResultCount` 达到时停止。Area visibility 使用 replication policy distance 与 observer radius 中的较小值。Observer radius 为零时关闭 area visibility，但不关闭 team visibility。结果写入调用方拥有并复用的 list，因此 steady state 解析不产生分配。

```csharp
var resolver = new GameplayNetworkObserverResolver(
    initialCapacity: 64,
    maximumResultCount: 512);
```

`GameplayNetworkObserverRegistry` 同时接收 dictionary 初始容量和产品预算：

```csharp
var observers = new GameplayNetworkObserverRegistry(
    initialCapacity: 64,
    maximumObserverCount: 512);
```

实例属性 `MaximumObserverCount` 表示产品预算，且不能超过 `MaximumSupportedObserverCount`。`GetAdmissionSnapshot()` 提供 admission diagnostics，其中同名 `MaximumObserverCount` 表示实例预算，而不是 dictionary 已分配容量。Position 与 radius 必须为有限数值，radius 不能为负数，observer connection ID 必须为正数。

Core 使用 `NetworkVector3` 注册 observer。Unity 代码可以使用 Runtime adapter overload：

```csharp
observers.SetObserver(connection, cameraPosition, radius, layerMask, teamId);
```

## Actor 转移状态

`ActorMigrationState` 是不可变且与引擎无关的快照，用于在权威 World 或服务器实例间转移 Actor runtime state。Transform 使用 `NetworkVector3` 和 `NetworkQuaternion`。内容身份使用 `PrefabDefinitionId`，不假设 Unity asset path。

从 Unity Actor 捕获状态：

```csharp
ActorMigrationState state = actor.CaptureMigrationState(
    prefabDefinitionId: "actors/player/warrior",
    ownerConnectionId,
    instigatorActorId);
```

通过共享 networking primitive 序列化：

```csharp
writer.WriteMigrationState(in state);
ActorMigrationState state = reader.ReadMigrationState();
```

目标 World 完成 Actor spawn 和 registration 后应用已经校验的状态：

```csharp
destinationActor.ApplyMigrationState(in state);
```

Unity 状态发生任何变化前，模块会先校验完整快照。Definition ID 必须非空；transform 必须有限；quaternion 必须已归一化且非退化；lifespan 必须非负且有限；tag 必须按 ordinal 唯一，并满足数量、字符长度与内容限制；内容 ID、名称和 tag 必须满足 strict UTF-8 byte budget。无效 Unicode 会被拒绝，不会被替换。Unity capture 会先归一化有限 quaternion，再构造严格的 Core value。快照构造时复制 tag，并通过无分配的只读索引接口访问。

Owner 与 instigator 字段是稳定网络标识符。目标 World 创建对应 runtime object 后，由网络层完成解析。`ActorMigrationNetworkingExtensions.MaximumEncodedSize` 是合法 payload 的精确最大值 26,045 bytes，protocol descriptor 使用相同上限。Deployment 必须为其实际允许的最大 state 配置足够的 route payload/fragmentation budget。

## 服务端权威伤害

`DamageRequestMessage` 表示不可信客户端意图。服务端将权威 ownership、transform、range、damage cap、target state、clock 和 cooldown 数据传给 `DefaultServerDamageValidator` 或项目自定义 `IServerDamageValidator`。

```csharp
ServerDamageValidationResult validation = processor.Process(
    in validationRequest,
    out DamageResultMessage result,
    requestSequence,
    damageEventType,
    hitLocation);
```

默认 validator 对以下情况执行 fail closed：

- 非正数 Actor 或 connection ID；
- 非有限 position、damage、clock、range 或 cooldown；
- 负数 damage、clock、range 或 cooldown（未知 last-accepted-time sentinel 使用 `float.NegativeInfinity`）；
- ownership 不匹配；
- target 不可受伤；
- 请求超出距离；
- 请求处于权威 cooldown 窗口内。

`ServerAuthoritativeDamageProcessor` 总是先执行默认 fail-closed baseline，再调用自定义 validator。自定义 validator 可以拒绝 baseline 已批准的请求或降低批准伤害，但不能绕过 baseline，也不能将伤害提高到权威上限之上。Processor 不信任调用方提供的 `LastAcceptedTimeSeconds`，而是根据自身 `DamageCooldownTracker` 重建该值；已接受时间戳保持单调，不允许向后回退。

`ServerDamageValidationResult.Accept` 只接受有限且非负的 damage；`Reject` 只接受已定义的 rejection reason。Processor 会把自定义 validator 返回的畸形结果，或超过 baseline 批准值的伤害，转换为 `Custom` rejection。在提交 cooldown 状态前，Processor 会验证完整的 outbound result。已接受结果要求 instigator 和 target Actor ID 均为正数且不相同，applied damage 有限且非负，hit location 有限。被拒绝的结果不能携带非零 damage。非法 hit location 会在自定义验证之前失败，并仅在 rejection payload 中替换为 zero，使 fail-closed 结果仍可序列化。

共享网络安全管线仍负责认证、payload limit、rate limiting、replay policy 和 transport abuse protection。

该 float 路径面向服务端权威战斗。确定性 ability effect 应使用 GameplayAbilities networking pipeline，并且同一次命中不能同时通过两个路径应用。

## Wire protocol

`GameplayFrameworkNetworkProtocol` 拥有 `11000` 至 `11999` 的消息 ID 范围。当前 catalog 包含：

| 消息 | ID | Channel | 用途 |
| --- | ---: | --- | --- |
| `ActorMigrationState:v1` | `11000` | Reliable | 有界 Actor 转移状态 |
| `DamageRequestMessage:v1` | `11001` | Reliable | 客户端伤害意图 |
| `DamageResultMessage:v1` | `11002` | Reliable | 权威处理结果 |

所有 primitive field 都按固定顺序显式序列化。测试固定了 protocol ID、schema identity、消息长度、result code 和 fingerprint。Deserializer 会拒绝超过 runtime budget 或包含非有限数值的数据。

## 性能与内存

- Core replication descriptor 和 validation request 使用 value type。
- Observer resolver 写入调用方拥有的 list，并复用有界 connection-ID set 完成去重。
- Observer registry 和 GameSession adapter 的有界 dictionary 在 composition 时分配，不在每个 tick 分配。
- Actor 转移是冷路径。快照构造时复制一次 tag 以建立不可变 ownership。
- 较小的转移字符串使用 stack buffer，较大字符串使用 `ArrayPool<byte>`。
- 模块不会创建平行的 per-Actor model，也不引入每 tick Unity/Core 同步层。

产品代码必须为每个 deployment profile 配置 observer、participant、staged connection、inbound queue 和 transport payload budget。

## 线程与生命周期

- Core protocol value 和纯 validator 没有 Unity thread affinity。
- Mutable registry 与 observer resolver 会捕获 owner thread 并拒绝其他线程访问；owner 必须串行化访问。
- `NetworkGameSessionAdapter` 在构造时捕获 owner thread，其他线程访问 collection 时会抛出异常。`MaximumSupportedBannedAddressCount` 是实现上限，`MaximumBannedAddressCount` 表示注入的每 session 预算。
- `ActorNetworkingExtensions` 调用 Unity API。已绑定 Actor 会在任何状态读写前验证所属 World thread。未绑定 authoring Actor 没有可验证的 World owner，因此调用方必须在 Unity main thread 调用 adapter。
- Transport shutdown 应在 owner thread 移除 staged/bound connection 后再替换 message endpoint。

## 持久化

模块不写入文件、PlayerPrefs、EditorPrefs、registry 或隐藏项目状态。封禁地址和 connection binding 都是内存 session state。需要持久化处罚记录的产品必须提供专用 persistence service，并显式定义存储、隐私、保留、完整性与恢复策略。

## 测试与验证

测试按依赖边界分离：

- `CycloneGames.GameplayFramework.Networking.Core.Tests.Editor`：不依赖 UnityEngine，覆盖 protocol fingerprint、codec、有界校验、damage processing、authority rule、observer budget 和 observer resolution。
- `CycloneGames.GameplayFramework.Networking.Runtime.Tests.Editor`：覆盖 Actor capture/apply adapter、Actor replication conversion、网络 session composition、endpoint 行为和 owner-thread enforcement。

最小 Unity 验证步骤：

1. 等待 Unity 重新导入 package，确认 Console 无编译错误。
2. 运行上述两个 EditMode test assembly。
3. 启动 host/server 配置，验证 staged login、disconnect 和 observer update 均运行在 World owner thread。
4. 通过选定 transport round-trip 一次 Actor 转移，并在 spawn 后解析 owner/instigator 标识符。
5. 对所有支持目标执行 Player/IL2CPP build；Editor test 不能证明 AOT、stripping 或 native transport compatibility。

## 故障排查

### Core assembly 报告引擎引用

协议代码应使用 `NetworkVector3`、`NetworkQuaternion` 和 GameplayFramework Core contract。Unity `Vector3`、`Actor`、`PlayerController` 与 `GameSession` 只能位于 Runtime assembly。

### Transport callback 抛出 owner-thread 异常

将 callback 放入有界 main/World-thread dispatcher，并在该队列被消费时调用 `NetworkGameSessionAdapter`。不要禁用检查或增加未同步的 collection 访问。

### Actor 转移被拒绝

写入或应用快照前检查有限 transform、lifespan、`PrefabDefinitionId`、名称 UTF-8 大小、tag 数量、tag 字符长度以及空白 tag。

### Observer 未被选中

检查 connection/认证状态、policy visibility、owner ID、team ID、observer 是否存在、layer mask 交集、有限 radius，以及 Actor interest position 的平方距离。
