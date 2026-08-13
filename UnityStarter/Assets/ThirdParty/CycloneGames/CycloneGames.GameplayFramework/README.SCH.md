# CycloneGames GameplayFramework

[English](README.md)

本模块提供以 `GameInstance -> World -> GameMode -> Controller -> Pawn -> PlayerState -> GameState` 为主链的 Unity gameplay-flow 底座。常用查询 API、authority、玩家准入、possession 与相机编排采用接近 Unreal Engine Gameplay Framework 的使用习惯，同时保留显式的 Unity 生命周期和组合边界。

## 目录

- [概述](#概述)
- [架构](#架构)
- [Assembly 接入](#assembly-接入)
- [快速开始](#快速开始)
- [Runtime 生命周期](#runtime-生命周期)
- [WorldSettings 与 WorldDefinition](#worldsettings-与-worlddefinition)
- [Actor 与 World 所有权](#actor-与-world-所有权)
- [GameMode 登录与 Roster](#gamemode-登录与-roster)
- [Controller、Pawn 与 Possession](#controllerpawn-与-possession)
- [PlayerState 与 GameState](#playerstate-与-gamestate)
- [Camera 系统](#camera-系统)
- [集成](#集成)
- [Editor 工具](#editor-工具)
- [持久化与数据所有权](#持久化与数据所有权)
- [性能、线程与平台说明](#性能线程与平台说明)
- [由简到深示例](#由简到深示例)
- [验证](#验证)
- [故障排查](#故障排查)

## 概述

`GameInstance` 拥有一个 active `World`。World 拥有 actor 和权威端 `GameMode`。玩家通过 GameMode 登录，获得 `PlayerController`，posses 一个 `Pawn`。`PlayerState` 在 Pawn 替换后仍然追踪参与者身份；`GameState` 保存已提交的匹配数据。对本地玩家，`CameraManager` 管理 camera mode 栈并负责混合。

Package 包含两个 Runtime 层。`CycloneGames.GameplayFramework.Core` 持有不依赖 engine 的准入、roster、match state、snapshot 与 capacity 规则。`CycloneGames.GameplayFramework.Runtime` 持有 Unity object graph，并提供熟悉、接近 Unreal 的 gameplay 接口。Runtime 依赖 Core；Core 不引用 Runtime 或 UnityEngine。

本模块处理 UE 中所谓的"game flow"层——不是输入，不是物理，不是网络传输。`WorldNetMode`（Standalone、ListenServer、DedicatedServer）控制框架的 authority 行为；实际的网络传输和复制放在你自行组合进 World 的独立模块中。

### Owner-thread 契约

`GameInstance` 与每个 `World` 都是 single-owner runtime scope。创建 `GameInstance` 的线程成为
owner；Unity composition 应在 Unity main thread 创建并使用它。World mutation 与内联 callback
必须留在该 owner thread，不提供隐式 lock 或跨线程 queue。

对已绑定 World 的 `Actor`，`SetOwner` 与 `SetInstigator` 会在 mutation 前校验 World owner
thread。`OwnerChanged` 在同一线程同步触发。未绑定 Actor 保持既有 Unity-facing 契约，应在 Unity
main thread 访问。产品 network adapter 必须先将 remote input 显式 marshal 到 World owner，
再修改这些引用。

## 架构

### 生命周期与关系图

~~~mermaid
flowchart TD
    H["GameplayWorldHost<br/>Unity composition root"] --> GI
    H --> ED["GameplayWorldTickDriver<br/>较早执行 Update / FixedUpdate"]
    H --> LD["GameplayWorldLateTickDriver<br/>较晚执行 LateUpdate"]
    ED --> GI
    LD --> GI
    GI["GameInstance<br/>application scope"] --> LP["LocalPlayer 槽位<br/>0..8"]
    GI --> W["World<br/>单个 active scope"]
    W --> WD["WorldDefinition<br/>已解析 prefab 引用和 lease"]
    W --> A["已注册 Actor"]
    W --> GM["GameMode<br/>仅 authority"]
    W --> GS["GameState<br/>已提交 World 状态"]
    GM --> S["IGameSession / GameSession<br/>Unity 参与者 facade"]
    S --> R["ParticipantRoster<br/>纯 Core 规则"]
    GS --> M["MatchStateMachine<br/>纯 Core 规则"]
    GM --> PC["PlayerController"]
    PC --> PS["PlayerState"]
    PC --> P["被 possession 的 Pawn"]
    LP -. 本地关联 .-> PC
    PC --> CM["CameraManager<br/>仅本地 Controller"]
    CM --> CC["CameraContext<br/>view target 和 mode stack"]
    CM --> OUT["ICameraOutput<br/>独占输出资源"]
~~~

这些关系具有独立含义：

- **生命周期所有权：**GameInstance 拥有 active World。World 拥有通过 `SpawnActor` 和 `SpawnActorDeferred` 创建的 Actor。
- **注册：**Scene Actor 和外部 Actor 可以加入 World，而不把 GameObject 销毁所有权交给 World。
- **Possession：**一个 Controller 独占控制一个 Pawn。Possession 不转移生命周期所有权。
- **参与者状态：**PlayerState 标识参与者，并可在同一 World 内的 Pawn 替换过程中继续存在。
- **本地关联：**LocalPlayer 把设备/用户槽位关联到当前 world-scoped PlayerController。
- **View target：**PlayerController 的相机目标与 possession 相互独立。
- **Authority：**World 在 Standalone、ListenServer 和 DedicatedServer mode 下接受权威端 Gameplay 编排。

### 目录布局

| 区域 | 职责 |
| --- | --- |
| `Core` | 不依赖 engine 的参与者 roster、login value、match state machine、player snapshot、World runtime limit、Actor admission snapshot 与 Actor tag limit |
| `Runtime/Scripts/World` | GameplayWorldHost、GameplayWorldComposition、early/late Tick driver、GameInstance、LocalPlayer、World、WorldSettings、WorldDefinition、KillZVolume |
| `Runtime/Scripts/Foundation` | Actor 生命周期、primary Tick、tag、damage 契约 |
| `Runtime/Scripts/Game` | GameMode、GameSession、GameState、PlayerState |
| `Runtime/Scripts/Controllers` | Controller、PlayerController、AIController |
| `Runtime/Scripts/Pawns` | Pawn、SpectatorPawn、PlayerStart |
| `Runtime/Scripts/Camera` | Camera context、mode、blend、输出、action、post-processor |
| `Runtime/Scripts/Config` | ScriptableObject authoring asset |
| `Runtime/Scripts/Integrations` | 可选跨 package adapter |
| `Editor` | Inspector、property drawer、gizmo、World Debugger、项目校验和 camera 诊断 |
| `Core/Tests/Editor` | 不依赖 engine 的 EditMode 规则与边界测试 |
| `Tests/Editor` | Unity EditMode 契约测试和性能测试 |
| `Tests/PlayMode` | GameplayWorldHost 的 Unity 生命周期测试 |
| `Samples` | 可在 Runtime 使用的 composition 和 camera 示例 |

### Assembly 边界

| Assembly | Auto referenced | 平台 | 使用方操作 |
| --- | --- | --- | --- |
| `CycloneGames.GameplayFramework.Core` | 否 | Managed Runtime 与 Editor；无 engine reference | 源码直接使用 Core 类型时显式添加 reference |
| `CycloneGames.GameplayFramework.Runtime` | 否 | Runtime 和 Editor | 在 asmdef 中显式添加引用 |
| `CycloneGames.GameplayFramework.Editor` | 是 | 仅 Editor | 为支持的 Inspector 和工具加载 |
| `CycloneGames.GameplayFramework.Core.Tests.Editor` | 否 | 仅 Editor；无 engine reference | 使用 Unity Test Framework 运行 Core rule |
| `CycloneGames.GameplayFramework.Tests.Editor` | 否 | 仅 Editor | 使用 Unity Test Framework 运行 |
| `CycloneGames.GameplayFramework.Tests.PlayMode` | 否 | Runtime test Player | 使用 Unity Test Framework 运行 |
| `CycloneGames.GameplayFramework.Sample.PureUnity` | 否 | Runtime 和 Editor | 使用 sample scene，或从 sample 代码显式引用 |
| `CycloneGames.GameplayFramework.Sample.CameraModes` | 否 | Runtime 和 Editor | 使用 camera 示例，或从 sample 代码显式引用 |
| `CycloneGames.GameplayFramework.Runtime.Integrations.Cinemachine` | 否 | 安装 Cinemachine 时参与 Runtime 和 Editor | 仅从 Cinemachine integration assembly 显式引用 |

`CycloneGames.GameplayFramework.Runtime` 直接引用 Core。这不会让 consumer 源码自动获得 Core 类型的 compile-time reference：consumer asmdef 应添加自身源码直接使用的每个 assembly。Integration assembly 同样不会被自动引用。

## Assembly 接入

模块当前位于：

~~~text
<repo-root>/UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayFramework/
~~~

只使用 `Actor`、`World`、`GameMode` 或其他 Unity-facing 类型的项目 Unity Runtime 代码需要添加：

~~~json
{
  "references": [
    "CycloneGames.GameplayFramework.Runtime"
  ]
}
~~~

直接构造或查询 `ParticipantRoster`、`PlayerLoginRequest`、`MatchStateMachine`、`MatchState`、`PlayerStateSnapshot`、`WorldRuntimeLimits`、`ActorAdmissionSnapshot` 或 `ActorTagLimits` 的代码还要添加：

~~~json
{
  "references": [
    "CycloneGames.GameplayFramework.Core",
    "CycloneGames.GameplayFramework.Runtime"
  ]
}
~~~

纯规则或 protocol Unity asmdef 可以只引用 `CycloneGames.GameplayFramework.Core` 并设置 `noEngineReferences: true`。这里的 engine independence 指 Unity assembly graph 内的依赖；要把源码发布为独立 .NET artifact，还需要单独的 .NET project/package build 与 target-framework 验证。项目代码还应添加自身直接使用的其他 assembly 引用；源码直接调用 UniTask 或可选 companion integration API 时，也需要显式引用对应 assembly。不要编辑 Unity 生成的 csproj 或 solution 文件。

Sample asmdef 同时面向 Runtime 和 Editor，因此其 Prefab 组件在 Player build 中仍然有效。它们不会被自动引用；调用 sample API 的项目代码必须显式添加 assembly 引用。`GameplayWorldHost` 是 sealed Unity composition root。手动 bootstrap 与 DI container 都在启动前提供同一个 `GameplayWorldComposition`；已经持有其他 loop 的应用可直接使用 `GameInstance`。

### Logging 接入

GameplayFramework 只产生日志。其 package dependency 是 `com.cyclone-games.logging`，所有直接写日志的 assembly 都显式引用 `CycloneGames.Logging.Core`，代码使用 `CycloneGames.Logging` API namespace。Runtime 与 sample 使用稳定 category `CycloneGames.GameplayFramework`，Editor 使用 `CycloneGames.GameplayFramework.Editor`。

模块不初始化、持有或关闭具体 backend。应用未安装 `ILogWriter` 时，process writer 是 `NullLogWriter`，所有记录都是安全 no-op。只有应用 composition root 应通过 `LogRuntime` 安装或替换 writer。需要标准 Unity Console 与文件输出时，组合 `com.cyclone-games.logging.pipeline` 和 `com.cyclone-games.logging.unity`，由 `LoggingBootstrap` 持有 pipeline 生命周期。这两个 backend package 都不是 GameplayFramework 的依赖。

Runtime、Editor 与 PureUnity sample 分别拥有 assembly 本地门面 `Runtime/Scripts/Diagnostics/GameplayFrameworkLog.cs`、`Editor/Diagnostics/GameplayFrameworkEditorLog.cs` 和 `Samples/Sample.PureUnity/Diagnostics/GameplayFrameworkSampleLog.cs`，所有门面都提供 `Category`、`Channel` 与 `Create(ILogWriter logWriter)`。包内 ambient 字段命名为 `Log`，显式注入的 instance 字段命名为 `_log`。所有记录都通过这些缓存 channel 使用统一的 `Trace`、`Debug`、`Info`、`Warning`、`Error` 与 `Fatal` 方法，不直接调用平台原生日志或具体 pipeline API。异常使用对应严重级别的重载传递完整 `Exception` 和说明失败操作的 message；包含动态值的普通日志使用 deferred generic-state builder。项目 extension 应在自身产生日志的 assembly 中定义同类门面，不应由 extension 自行初始化 backend。Ambient channel 每次写入都会解析当前 process writer，因此成功执行 `LogRuntime.TryReplaceWriter(expected, replacement)` 交接后不需要重新初始化 GameplayFramework。

Logging 不新增 serialized state，也不会自行写文件。持久化、轮转、flush、shutdown 与损坏恢复由所选 backend 及其应用级 owner 负责。

## 快速开始

### 准备 Prefab

创建以下 GameObject prefab，并确保对应 framework class component 位于 prefab root 且仅有一个：

1. **GameMode prefab：**一个 `GameMode` 子类。需要 match state 和参与者数组时，在 `gameStateClass` 中分配 GameState prefab。
2. **PlayerController prefab：**一个 `PlayerController` 子类。
3. **Pawn prefab：**一个 `Pawn` 子类，以及产品移动和输入 adapter。
4. **PlayerState prefab：**一个 `PlayerState` 子类。
5. **CameraManager prefab：**可选；本地相机需要输出时配置。
6. **SpectatorPawn prefab：**可选；spectator login 需要可 possession 的表现对象时配置。

如果 spawn 玩家需要 authoring 起点，请在 scene 中放置一个或多个 `PlayerStart` Actor。

### 创建 WorldSettings

使用：

~~~text
Create > CycloneGames > GameplayFramework > WorldSettings
~~~

分配四个必需引用：

- GameMode；
- PlayerController；
- Pawn；
- PlayerState。

CameraManager 和 SpectatorPawn 为可选项。进入 Play Mode 前，在 Inspector 中点击 **Validate Configuration**。

### 添加 GameplayWorldHost

1. 创建名为 `Gameplay World Host` 的 scene GameObject。
2. 添加 `GameplayWorldHost`。
3. 分配 WorldSettings 资产。
4. 选择 net mode 和 local-player count。
5. 作为 scene 入口时保持 **Auto Start** 开启。

Dedicated Server mode 始终使用零个 local player。Host 早于普通 Actor 的 `Start` callback 启动，持有 GameInstance，创建较早执行的 Update/FixedUpdate driver 与较晚执行的 LateUpdate driver，公开运行状态和失败诊断，并在其 GameObject 销毁时 dispose World。禁用 Host component 会暂停两个 driver，但不会修改 World 生命周期；Host 应保持 enabled，直到执行 stop 或 disposal。

Direct Reference 不需要 resolver。Asset Reference 和 Path 需要显式 `IWorldSettingsReferenceResolver`；WorldSettings 章节介绍 resolver 契约和可选 AssetManagement companion package。如果项目的 DI 容器已经持有 application lifetime，可直接构造并 dispose `GameInstance`，无需添加 Host。

### Standalone 预期结果

`StartWorldAsync` 完成后：

- `GameInstance.CurrentWorld` 非 null；
- `World.LifecycleState` 为 `Playing`；
- `World.GameMode` 正在运行；
- 每个已配置 LocalPlayer 都有关联的 PlayerController；
- 每个非 spectator Controller 都有 PlayerState 和被 possession 的 Pawn；
- 当 GameMode prefab 分配 `gameStateClass` 时，authoritative World 暴露 GameState；Client World 则在 replication adapter 提交 registered instance 后暴露；
- 配置 CameraManager 时会创建本地 CameraManager。

Sample scene 位于：

~~~text
Samples/Sample.PureUnity/Scene/UnitySampleScene.unity
~~~

## Runtime 生命周期

### GameInstance 与 LocalPlayer

`GameInstance` 将创建它的线程记录为 owner thread。修改状态的调用（包括 `Tick`）会校验该线程。涉及 Unity API 的调用方应在 Unity main thread 创建和使用该实例。

构造参数：

| 参数 | 含义 |
| --- | --- |
| `IActorLifetime` | 必需的 Actor 创建与释放边界 |
| `localPlayerCount` | 持久本地用户槽位数量，范围为 0 到 `MaxLocalPlayers` |
| `IWorldSettingsReferenceResolver` | 可选 WorldSettings 外部资源加载器 |
| `ISceneTransitionHandler` | 可选 scene navigation adapter |
| `WorldRuntimeLimits` | 可选的不可变 Actor 准入与集合初始容量配置 |

`LocalPlayer` 包含稳定的 `Index` 和当前 world-scoped `PlayerController`。Controller logout、World 停止或 GameInstance dispose 时会清理此关联。

一个 GameInstance 只接受一个 active World。启动下一个 World 前先调用并等待 `StopWorldAsync`。直接调用 public `World.ShutdownAsync` 或 `World.Dispose` 也会执行相同的所有权清理，并通知所属 GameInstance 清空 `CurrentWorld`。World 已处于 `Stopping` 时发生的重入 stop 不会释放 `CurrentWorld`；在 disposal 完成前，replacement start 仍会被拒绝。

### Host 组合与 DI

`GameplayWorldComposition` 是手动 bootstrap 与 DI 共用的 Host 依赖边界。它包含必需的 `IActorLifetime`，以及可选的 reference resolver、scene transition、session 与 World runtime-limit service。必须在 sealed Host 启动前配置：

~~~csharp
var composition = new GameplayWorldComposition(
    actorLifetime,
    referenceResolver,
    sceneTransitionHandler,
    gameSession,
    runtimeLimits);

host.Configure(composition);
await host.StartWorldAsync(cancellationToken);
~~~

调用方保留传入 service 的所有权，并在 Host 停止后 dispose。`GameplayWorldComposition.CreateDefault()` 使用 `UnityActorLifetime`，负责实例化和销毁 Unity Actor。DI container 提供相同构造参数即可；不需要 GameplayWorldHost 子类或容器专用 Runtime assembly。

World-owned Actor 的每次 `IActorLifetime.Create` 都会与 `Release` 配对，包括 spawn transaction 失败、self-destruction 和 shutdown。即使 Unity destruction 已经发生，实现也必须接受该 Actor。`Release` 必须永久结束该 Actor instance；已进入终态 `Destroyed` 的 Actor 不支持返回 pool 后复用。使用 CycloneGames.Factory 的项目安装 Factory companion package 并组合 `FactoryActorLifetime`；Factory 类型不会出现在 GameplayFramework Runtime interface 中。

### Net mode

| WorldNetMode | IsAuthority | 创建 GameMode | 自动本地登录 |
| --- | --- | --- | --- |
| `Standalone` | 是 | 是 | 是 |
| `Client` | 否 | 否 | 否 |
| `ListenServer` | 是 | 是 | 是 |
| `DedicatedServer` | 是 | 是 | 否 |

Dedicated server composition 应使用零个 LocalPlayer。Client World 提供非权威 scope；network transport 和 replication adapter 负责添加客户端可见状态。

### World 状态

~~~mermaid
stateDiagram-v2
    [*] --> Created
    Created --> Initializing: StartWorldAsync
    Initializing --> Playing: initialization 提交
    Initializing --> Stopping: 取消或失败
    Playing --> Stopping: StopWorldAsync、travel 或 dispose
    Stopping --> Stopped: Actor 和 Gameplay 状态结束
    Stopped --> Disposed: lease 和生命周期资源释放
    Disposed --> [*]
~~~

World 仅在 `Initializing` 或 `Playing` 时接受新 Actor。

### 初始化顺序

`StartWorldAsync` 执行以下事务：

1. 校验 GameInstance 状态和 WorldSettings。
2. 将 WorldSettings 解析为 WorldDefinition。
3. 切换到 Unity main thread 并校验 owner-thread affinity。
4. 创建 World，并将其公开为 `CurrentWorld`。
5. 通过无排序 scan 发现当前全部已加载有效 scene 中的 Actor，包括 inactive Actor。
6. 在 authoritative World 中 spawn 并初始化 GameMode。
7. 在 authority 中，若 GameMode prefab 配置了 `gameStateClass`，则从该 prefab 创建 GameState。
8. 执行 LocalPlayer 登录事务。
9. 将 World 切换到 `Playing`。
10. 向已注册、active、非 deferred 的 Actor 发布 BeginPlay。
11. 通知 GameMode：World 已启动。
12. 启用 Actor Tick dispatch。

任意异常都会中止初始化、结束已注册 Actor、销毁 World-owned Actor、dispose WorldDefinition lease、清空 `CurrentWorld`，然后重新抛出异常。

### 关闭与 Travel

关闭一旦开始，清理过程不再接受取消：

1. World 停止 Actor Tick dispatch，进入 `Stopping` 并取消 `LifetimeToken`。
2. GameMode logout 所有 PlayerController。
3. 剩余 Actor 按 World registry 逆序接收 EndPlay。
4. 销毁 World-owned GameObject。
5. 解绑 scene/external Actor，但 World 不销毁其 GameObject。
6. Deactivate active camera output，并释放其独占 resource lease。
7. WorldDefinition 按获取逆序释放外部 lease。
8. World 进入 `Disposed`，GameInstance 清理 `CurrentWorld`。

`GameMode.TravelToLevel` 先使用 `EndPlayReason.Travel` 停止 World，再调用 `ISceneTransitionHandler.ChangeScene`。目标 scene 创建自身 World。请求 travel 前先捕获需要跨 scene 传递的数据。

`GameInstance.Dispose` 会取消自身 lifetime，使用 `ApplicationShutdown` 立即关闭 World，清理 LocalPlayer 关联并释放 cancellation source。

## WorldSettings 与 WorldDefinition

### Authoring 与 Runtime 职责

`WorldSettings` 是 ScriptableObject authoring asset。每个 class entry 都选择一个 prefab，其 root 必须且只能包含一个对应 framework 类型的 component。Runtime 启动时把这些 prefab class 解析为不可变 `WorldDefinition`；Runtime 代码通过 `World.Definition` 读取定义。

| 引用 | 必需 | Runtime 用途 |
| --- | --- | --- |
| GameMode | 是 | 权威端规则和玩家编排 |
| PlayerController | 是 | 参与者 Controller spawn |
| Pawn | 是 | 默认非 spectator Pawn spawn |
| PlayerState | 是 | 参与者身份/状态 spawn |
| CameraManager | 否 | 本地相机 Runtime |
| SpectatorPawn | 否 | Spectator possession |

Authoritative GameState 通过 GameMode prefab 的 `gameStateClass` 配置；该字段为空时，authority startup 会让 `World.GameState` 保持 null。Client World 通过“常用 Runtime 查询”章节说明的显式 replication boundary 接收 instance。

`GameMode`、`PlayerController`、`Pawn` 与 `PlayerState` prefab class 为必需项；`CameraManager` 与 `SpectatorPawn` 为可选项。WorldSettings Inspector 会校验必需 entry、prefab root、reference source 与 external location。Runtime 配置始终以 prefab 为基础：这些字段选择要实例化的 Actor class，不引用 scene 中的 live object。

### 引用来源

| Source | Authoring 值 | Resolver 要求 |
| --- | --- | --- |
| `DirectReference` | 直接 prefab 引用 | 无 |
| `AssetReference` | Inspector 记录的 asset location | Resolver 必须支持 `AssetReference` |
| `PathLocation` | 项目定义的 address/path | Resolver 必须支持 `PathLocation` |

必需引用必须解析为非 null asset。可选 direct reference 可以为空。可选外部引用只要 location 非空，就视为已配置并且必须成功解析。

### Resolver 契约

~~~csharp
public interface IWorldSettingsReferenceResolver
{
    bool Supports(WorldSettingsReferenceSource source);

    UniTask<WorldSettingsAssetLoadResult<T>> ResolveAsync<T>(
        string location,
        CancellationToken cancellationToken)
        where T : UnityEngine.Object;
}
~~~

Result 包含 success、asset、error 和可选 `IDisposable` lease。部分解析失败时，WorldSettings dispose 已获取的 lease。成功时，WorldDefinition 拥有这些 lease，并按逆序且只释放一次。

Resolver 实现必须：

- 响应 cancellation；
- 返回有界 error message；
- 在返回前 dispose 失败 handle；
- 不把可变解析状态存入 WorldSettings；
- 当 location 可能来自项目 asset 之外时，将其作为不可信输入处理。

### AssetManagement Companion

Sibling package `com.cyclone-games.gameplay-framework-asset-management` 提供 `AssetManagementWorldSettingsReferenceResolver`。它接收显式 `IAssetPackage`、支持 `AssetReference`，并把成功的 asset handle 作为 lease 移交给 `WorldDefinition`。它不支持 `PathLocation`。

~~~csharp
var resolver =
    new AssetManagementWorldSettingsReferenceResolver(assetPackage);

var instance = new GameInstance(
    new UnityActorLifetime(),
    localPlayerCount: 1,
    referenceResolver: resolver);
~~~

## Actor 与 World 所有权

### Actor 生命周期

~~~mermaid
stateDiagram-v2
    [*] --> Constructed
    Constructed --> Initialized: Awake
    Initialized --> Playing: World BeginPlay 或 bound-World Start fallback
    Playing --> Ending: 请求 EndPlay
    Ending --> Ended: EndPlay 返回
    Ended --> Initialized: non-owned Actor 绑定 replacement World
    Constructed --> Destroyed: OnDestroy
    Initialized --> Destroyed: OnDestroy
    Playing --> Destroyed: EndPlay 后 OnDestroy
    Ended --> Destroyed: OnDestroy
    Destroyed --> [*]
~~~

重写以下 hook：

~~~csharp
protected override void BeginPlay()
{
    base.BeginPlay();
    // Subscribe and start world-bound behavior.
}

protected override void EndPlay(EndPlayReason reason)
{
    // Cancel and unsubscribe world-bound behavior.
    base.EndPlay(reason);
}
~~~

每次 World binding 最多发布一次 `BeginPlay`。World 拥有常规发布 barrier；只有 Actor 已绑定且该 World 已处于 `Playing` 时，Unity `Start` 才提供 fallback。关闭后，non-owned scene/external Actor 会解绑，并可在 replacement World 注册它时从 `Ended` 重置为 `Initialized`。World-owned Actor 结束后不能重入。每次 binding 最多发布一次 `OnWorldUnbound`，direct destruction 也包含在内；EndPlay 内发生 destruction 时，终态 `Destroyed` 不会被 `Ended` 覆盖。`OnDestroyed` 是 Unity 销毁终态事件，与 EndPlay 分离。

在 Playing World 中注册 inactive Actor 时，它会保持 `Initialized`，直到变为 active；随后 `OnEnable` 请求 World 发布 BeginPlay。Actor 仍属于 deferred spawn 时，World 会拒绝该 fallback，因此 activation 不能绕过 `FinishSpawningActor`。

EndPlay reason 包括：

- `Destroyed`；
- `SceneUnload`；
- `WorldShutdown`；
- `Travel`；
- `InitializationFailure`；
- `ApplicationShutdown`。

### Primary Actor Tick

Primary Actor Tick 是可选的 World 生命周期服务，不会替代 Unity 的全部更新机制。`ActorTickPhase.None` 的 Actor 不会进入 Tick registry，也不会收到框架的逐帧 callback。

~~~mermaid
flowchart TD
    PL["Unity PlayerLoop"] --> ED["GameplayWorldTickDriver<br/>Update / FixedUpdate"]
    PL --> LD["GameplayWorldLateTickDriver<br/>LateUpdate"]
    ED --> GI["GameInstance.Tick"]
    LD --> GI
    GI --> W["World.Tick"]
    W --> R["Phase registry snapshot"]
    R --> A["Actor.Tick"]
~~~

可以通过 Actor Inspector 或代码完成配置：

| Member | 用途 |
| --- | --- |
| `ActorTickPhase` | 选择 None、Update、FixedUpdate 或 LateUpdate |
| `CanEverTick` | 配置了可 dispatch phase 时为 true |
| `TickPhase` | 当前 primary phase |
| `IsActorTickEnabled` | 读取 Runtime enable flag |
| `SetActorTickEnabled` | 启用或禁用 Runtime dispatch |
| `SetActorTickPhase` | 修改配置 phase，并在 active phase registry 之间移动已启用的 Actor |
| `ConfigureActorTick` | Protected 的代码侧 phase 和启动配置 |
| `Tick` | Protected virtual Gameplay callback |

只有同时满足以下条件时，World 才会调用 `Tick`：

- World 已完成启动，且 `LifecycleState` 为 `Playing`；
- Actor 仍然注册、不是 deferred，并且已经完成 BeginPlay；
- Actor component 处于 active 和 enabled 状态；
- 配置 phase 与当前 dispatch 一致，且 Runtime Tick 已启用。

每个 active phase registry 只包含 Runtime Tick flag 已启用的 Actor；禁用 Tick 会将 Actor 移出热路径列表。每个 phase 使用可复用 snapshot。Callback 中 spawn、启用或切换到该 phase 的 Actor，会从目标 phase 的下一次 dispatch 开始参与；snapshot 尚未访问到的 Actor 若已被禁用、销毁或移出当前 phase，则会被跳过；World shutdown 会终止剩余 entry。`GetTickActorCount` 返回当前 Runtime-enabled registry count。单个 Actor 抛出的异常会以该 Actor 作为 context 写入日志，并继续执行 phase 中的其他 Actor。Tick dispatch 只能在 owner thread 执行，并拒绝重入。

Tick 顺序不是 Gameplay 排序契约。注册和移除采用 dense swap-back 结构，因此 Actor 之间的依赖必须通过显式状态、事件或编排表达，不能依赖 callback 顺序。

Actor 仍然是 MonoBehaviour，因此专用子类或同级 component 仍可声明原生 Unity message。这些 message 由 Unity 持有，不受 World registration 或 lifecycle gate 约束，适合生命周期跟随 component 的窄 Unity-facing 职责。

一个 Actor 只有一个 primary phase。需要 Unity physics callback、Animator callback、rendering callback、worker-scheduled simulation 或多个独立 phase 的组件，应继续使用职责收敛的 MonoBehaviour adapter 或纯 C# simulation owner。GameplayAbilities、movement、projectile 和 presentation 模块不会因为 owner 是 Actor 就自动改为 Actor-Tick 驱动。

`GameplayWorldHost` 会在 Runtime 创建两个 sealed driver。`GameplayWorldTickDriver` 的 execution order 为 `-9999`，较早转发 Update 与 FixedUpdate；`GameplayWorldLateTickDriver` 的 execution order 为 `9999`，在普通默认顺序的 LateUpdate callback 之后转发 LateUpdate，使 camera evaluation 可以观察它们已经完成的 transform。直接组合 `GameInstance` 的项目必须通过 `GameInstance.Tick`，从所选 Unity phase 或自定义 loop 中各转发一次。

### 注册与所有权

| API | World registry | Begin/End 通知 | World 销毁 GameObject |
| --- | --- | --- | --- |
| Scene discovery | 是 | 是 | 否 |
| `RegisterActor` | 是 | 是 | 否 |
| `SpawnActor` | 是 | 是 | 是 |
| `SpawnActorDeferred` | 是 | Finish 后 | 是 |

对于普通已注册 Actor，`DestroyActor` 会将其从 World 移除、发布 EndPlay 并销毁 GameObject。Play Mode 使用 Unity 正常销毁边界；Edit Mode 使用立即销毁。注入的 `IActorLifetime` 只释放 World-owned spawned Actor；显式销毁 non-owned registered Actor 时使用 core Unity destruction helper，不调用 injected lifetime。

销毁已提交的 PlayerController 或其 PlayerState 会升级为 participant logout，使 roster、GameState、LocalPlayer、Pawn、camera 和 spectator 状态作为一次操作完成清理。在 World 处于 `Initializing` 或 `Playing` 时销毁 active GameMode，会升级为完整 World shutdown。

Actor registry 使用 swap-back removal。Registry 顺序以及 `TryGetActor<T>` 返回的第一个结果都不是稳定选择策略。

诊断和低频工具可在 `0..ActorCount` 范围内调用 `TryGetActorRegistration`。该调用返回 readonly value，不创建 collection snapshot；任何 Actor removal 都会使既有 index 失效，因此不得持久化 index。Unity Actor reference 必须在 main thread 读取。

### World Runtime Limit 与准入

Core `WorldRuntimeLimits` 是不可变值，通过 `GameInstance` 或 `GameplayWorldComposition` 提供。Runtime 构造 World 时直接使用该值，不存在镜像 Unity 配置对象或逐帧转换。它控制单个 World 的准入限制和构造期容量：

| Property | 默认值 | 用途 |
| --- | ---: | --- |
| `MaximumActorCount` | `65,536` | 该 World 的产品级 Actor 准入限制 |
| `InitialActorCapacity` | `128` | Actor registry、lifecycle scratch 与 Actor index 初始容量 |
| `InitialUpdateTickCapacity` | `128` | Update registry 与可复用 Tick scratch 容量规划 |
| `InitialFixedUpdateTickCapacity` | `32` | FixedUpdate registry 容量规划 |
| `InitialLateUpdateTickCapacity` | `32` | LateUpdate registry 容量规划 |

每个 initial capacity 都必须为非负值；高于 configured maximum 的值会被 cap 到该 maximum。它们只预留 managed collection storage，不会预创建 Actor，也不会阻止集合在 `MaximumActorCount` 范围内继续增长。应根据实测 scene 和 gameplay workload 配置，而不是把所有值都设置为 ceiling。

`WorldRuntimeLimits.MaximumSupportedActorCount`（`65,536`）是绝对 implementation ceiling，不是每个 World 的 active budget。容量拒绝属于预期结果时，使用 `TrySpawnActor`、`TrySpawnActorDeferred` 与 `TryRegisterActor`。达到 configured maximum 后，`SpawnActor`、`SpawnActorDeferred` 与 `RegisterActor` 抛出 `InvalidOperationException`；已有 Actor 的 Tick、destroy、unregister 与 shutdown 仍然可用。

`GetActorAdmissionSnapshot()` 返回 Core `ActorAdmissionSnapshot`，其中包含 `ActorCount`、configured `MaximumActorCount`、当前 `AllocatedActorCapacity`、`PeakActorCount` 与单调递增的 `RejectedAdmissionCount`，且不创建 collection snapshot。Allocated capacity、peak 与 rejection 数据用于诊断和容量调优；它们随 World 重置，不进行持久化。

### Deferred Spawn

当依赖或状态必须在 BeginPlay 前完成配置时，使用 deferred spawn：

~~~csharp
Pawn pawn = world.SpawnActorDeferred(pawnPrefab);
bool committed = false;

try
{
    pawn.SetPawnConfig(pawnConfig);
    pawn.SetActorLocationAndRotation(position, rotation);

    world.FinishSpawningActor(pawn);
    committed = true;
}
finally
{
    if (!committed && world.IsActorRegistered(pawn))
    {
        world.DestroyActor(pawn, EndPlayReason.InitializationFailure);
    }
}
~~~

如果 spawn 出的实例原本 active，World 会暂时将其设为 inactive，直到 `FinishSpawningActor`。对已注册 Actor 重复 Finish 是幂等的；Finish 未注册 Actor 会抛出异常。

### Actor 服务

Actor 还提供：

- owner 和 instigator 引用；
- transform 和 view-point helper；
- renderer visibility 同步；
- 由 Core `ActorTagLimits` 约束的精确 ordinal tag，最多 64 个，每个最多 128 个字符；
- generic、point 和 radial damage dispatch；
- 可取消 lifespan；
- 可选的 World-scoped primary Tick；
- `HasAuthority`；Actor 未加入 World 时返回 true，加入后跟随 World authority；
- `FellOutOfWorld` 和 `KillZVolume`。

Actor owner、Controller possession 和 World ownership 是独立关系。

Tag bulk operation 使用 span boundary：

~~~csharp
int copied = actor.CopyTagsTo(destinationSpan);
actor.ReplaceTags(replacementSpan);
~~~

`CopyTagsTo(Span<string>)` 写入调用方持有的 storage。`ReplaceTags(ReadOnlySpan<string>)` 会在修改 Actor 前校验完整输入、移除重复 ordinal value，并接受 empty span 清空 tag set。调用方已经持有兼容 span storage 时，这些 overload 不需要 `IEnumerable<string>` adapter、boxing 或新建 transfer array。两个操作都受 `ActorTagLimits` 约束；replacement 属于 owner-thread mutation。

### 常用 Runtime 查询

常用 gameplay 查询不依赖全局 service locator：

~~~csharp
World world = actor.GetWorld();
GameInstance instance = actor.GetGameInstance();
MyGameMode mode = actor.GetAuthGameMode<MyGameMode>();
MyGameState state = actor.GetGameState<MyGameState>();

World currentWorld = instance.GetWorld();
GameInstance owner = world.GetGameInstance();
MyGameMode worldMode = world.GetAuthGameMode<MyGameMode>();
MyGameState worldState = world.GetGameState<MyGameState>();
PlayerController firstPlayer = world.GetFirstPlayerController();
PlayerController player = world.GetPlayerController(index);
~~~

`GetAuthGameMode` 在非 authoritative World 中返回 null。Generic getter 使用安全类型转换；active object 与请求类型不匹配时同样返回 null。`GameInstance.GetWorld()` 返回 current World。在 Client World 中，replication adapter 先注册接收的 GameState Actor，再通过 `World.SetReplicatedGameState` 提交；已有非 null instance 时拒绝替换，销毁或显式清除该 Actor 后会释放 World reference，此时才能提交另一个 instance。要让接收的 Controller 可通过 `GetFirstPlayerController`/`GetPlayerController` 查询，adapter 需先注册 Controller 及其 PlayerState 并初始化 Controller，再调用 `CommitReplicatedPlayerController`；可选 LocalPlayer 必须是该 GameInstance 实际持有的精确 slot。销毁 Controller 会清除已提交的 World 与 LocalPlayer 关联。这些 API 沿对象图查询，不创建 ambient global state。

## GameMode 登录与 Roster

### Authority 与生命周期

GameMode 只存在于 authoritative World。其状态为：

~~~text
Uninitialized -> Initialized -> Starting -> Running -> Stopping -> Stopped
~~~

初始化会组合传入的 `IGameSession`，或者创建有界 `GameSession`。`GameModeConfig` 当前用于应用默认 spectator 规则。游戏专属配置 asset 可以继承它并重写 `ApplyTo`。

### 登录请求边界

Core `PlayerLoginRequest` 会在 Runtime 登录事务开始前强制以下限制：

| 字段 | 限制 |
| --- | --- |
| PlayerId | 不得为负数 |
| PlayerName | 最多 64 个字符 |
| RemoteAddress | 最多 256 个字符 |
| Options | 最多 1024 个字符 |
| IsLocal | 必须匹配可信 LocalPlayer slot；local request 不能包含 RemoteAddress |

构造请求前必须先对远程输入完成认证、限流、规范化和校验。修改 World 或 GameSession 的调用必须运行在 owner thread。

GameMode 要求 `request.IsLocal` 与是否传入 `localPlayer` 参数一致。传入的 LocalPlayer 必须是 `World.GameInstance` 实际拥有的同一个 slot。Network 和 remote 调用方传入 null，并把 `IsLocal` 设为 false；输入 flag 不能建立可信 local identity。

基础 `CreateLocalPlayerLoginRequest` 会把 LocalPlayer index 0 映射为 player ID 1 和名称 `LocalPlayer1`，把 `IsLocal` 设为 true，后续槽位依次递增。Local identity 来自 platform-user service 时，应重写该方法。

基础 request validation 允许 PlayerName 为 null。GameSession 强制 PlayerId 在单个 session 内唯一；account authenticity、跨 session identity 和 reconnect/rejoin ID 分配仍属于产品准入职责。`PlayerLoginResult.Error` 是诊断文本，任何网络响应都应先进行脱敏和映射。

### 事务流程

~~~mermaid
flowchart TD
    R["PlayerLoginRequest"] --> V["校验 mode、authority、cancellation、边界和可信 local slot"]
    V --> P["PreLogin / IGameSession.ApproveLogin"]
    P --> D["Deferred spawn Controller 和 PlayerState"]
    D --> O["可选本地 CameraManager 或 SpectatorPawn"]
    O --> I["初始化 PlayerController"]
    I --> SR["注册 roster entry"]
    SR --> WC["提交 PlayerController 到 World 和 LocalPlayer"]
    WC --> SP["Possess spectator，或 spawn 并 possess 默认 Pawn"]
    SP --> GS["将 PlayerState 添加到 GameState"]
    GS --> F["Finish deferred Actor"]
    F --> PL["PostLogin"]
    V -. 失败 .-> RB["返回 status"]
    P -. 拒绝 .-> RB
    D -. 失败 .-> RO["回滚 possession、roster、World 关联和 spawned Actor"]
    O -. 失败 .-> RO
    I -. 失败 .-> RO
    SR -. 失败 .-> RO
    WC -. 失败 .-> RO
    SP -. 失败 .-> RO
    GS -. 失败 .-> RO
    F -. 失败 .-> RO
    PL -. 失败 .-> RO
~~~

`PlayerLoginResult.Status` 使用 Core `PlayerLoginStatus`，并报告：

- `Success`；
- `InvalidRequest`；
- `NotAuthoritative`；
- `WorldNotAcceptingPlayers`；
- `Rejected`；
- `AtCapacity`；
- `SpawnFailed`；
- `Cancelled`。

`PostLogin` 会在关系提交且所有 deferred Actor 完成 spawn 后运行。如果 PostLogin 抛出异常，登录事务会回滚。

### GameSession

`GameSession` 是 sealed Runtime facade，内部组合一个 Core `ParticipantRoster`。Roster 按非负 PlayerId 建立索引，拒绝重复 identity，执行 player/spectator capacity 规则，并原子更新分类计数。Facade 在 Runtime dictionary 中保存已准入的 `PlayerController`/`PlayerState` binding，使 World cleanup 与 possession 保持直接访问。产品专属 session 行为通过 `IGameSession` 组合，包括由 DI 提供实现；GameSession transaction 不是 subclass extension point。

注册会让一个 GameSession 独占 PlayerState identity lock，直到 `UnregisterPlayer`；同一个 PlayerState 不能同时注册到另一个 session。已注册参与者的 spectator category 通过 `TrySetSpectatorStatus` 修改；该方法在 owner thread 上先提交 Core roster 变更，再把 PlayerState 与 Runtime binding 作为一次 facade 操作更新。因 identity 或 capacity 被拒绝的注册会在修改 PlayerState 前返回。会破坏已注册 entry 一致性的 PlayerId 或 spectator 直接修改会被拒绝。

容量由构造参数提供。每个容量以及两者总和都受 `ParticipantRoster.MaximumSupportedParticipants` 限制。默认实现由单线程 owner 使用。

默认 GameSession 从 GameMode prefab 接收 serialized `maxPlayers` 和 `maxSpectators`。当产品在其他位置拥有准入状态时，通过 `GameInstance.StartWorldAsync` 传入自定义 `IGameSession`。

实现 `IGameSession` 可提供产品准入和 roster callback：

- `ApproveLogin`；
- `TryRegisterPlayer`；
- `ContainsPlayer`；
- `UnregisterPlayer`；
- `TrySetSpectatorStatus`；
- `HandleMatchHasStarted`；
- `HandleMatchHasEnded`。

Session match notification 成对发布。只有 World 进入 Playing 且全部初始 BeginPlay callback 完成后，`HandleMatchHasStarted` 才会提交。在此之前发生 startup rollback 时，不会发布 `HandleMatchHasEnded`；成功发布一次 start 后，shutdown 会发布一次 end。

### Spawn、Restart、Logout 与 Travel

GameMode 首先按精确 portal/GameObject 名称选择 PlayerStart，然后调用 `ChoosePlayerStart`。基础实现选择缓存中的第一个 start。Scene discovery 无排序要求，因此 spawn 选择需要确定性时，应重写 `ChoosePlayerStart`。

`RestartPlayer` 会复用已有 Pawn，或 deferred-spawn 默认 Pawn；随后执行 teleport、发布 initial rotation、possession，并 finish spawn。

基础 teleport 路径会处理 CharacterController 和 Rigidbody component。非 kinematic Rigidbody 的 velocity 和 angular velocity 会被清零。产品移动 backend 需要其他事务时，应重写 `TeleportPawn`。

`Logout` 是 public non-virtual atomic entry。它会执行 unpossess、注销 roster entry、从 GameState 移除 PlayerState、清理 World/LocalPlayer 关联，并销毁 World-owned 参与者 Actor。通过 protected virtual `HandleLogout` 扩展 logout 行为。Unpossess、roster/GameState 移除、hook 或 Actor 销毁中的异常会被隔离并记录，后续清理仍会继续。

## Controller、Pawn 与 Possession

### Possession 契约

Controller 必须先注册到 World，并针对与 Pawn 相同的 World 完成初始化。`TryPossess` 对无效输入返回 error；`Possess` 在事务无法提交时抛出异常。

事务步骤：

1. 拒绝 reentrant possession。
2. 校验 Controller 和 Pawn 的 World membership。
3. 解绑 Controller 当前 Pawn。
4. 解绑目标 Pawn 当前 Controller。
5. 提交 Controller、Pawn 和 PlayerState 关联。
6. 重置 control rotation 并 dispatch Pawn restart。
7. 在全部双向关系一致后发布 callback。

Possession 是独占的。它不会设置 Actor owner，也不会改变 World 的销毁所有权。Public `Possess`/`TryPossess` 与 `UnPossess` 是 non-virtual transaction boundary；子类通过 `OnPossess` 与 `OnUnPossess` 扩展已提交行为，不替换关系管理过程。

不要在 possession callback 中调用 `Possess` 或 `UnPossess`；reentrancy guard 会拒绝此修改。

Possession callback 在状态提交后运行。每个 callback 返回后，transaction 都会复核 Controller、Pawn 和 PlayerState 的双向关系。Callback 若销毁或以其他方式使已提交 Controller/Pawn 失效，框架会执行无 callback 的 emergency detach，且 `TryPossess` 返回 false。异常仍会向外传播；已提交关系保持有效时会被保留，因此抛异常的 callback 仍需要显式 compensation policy。

World unbind 会清除 Controller 的 possession、PlayerState、start spot、input-suppression counter 和 initialization state；non-owned scene Controller 与 externally registered Controller 同样适用。AIController 还会停止 AI 并清除 focus。PlayerController 会清除 LocalPlayer、camera context、CameraManager、SpectatorPawn 和 view-target 关系。在 replacement World 中复用这些 non-owned object 时，必须显式重新初始化。

### Controller 输入与 View

Controller 提供：

- 带 Pawn pitch limit 的 control rotation；
- start-spot 存储；
- 可叠加的 move/look suppression counter；
- Pawn 和 PlayerState 访问；
- view-point 转发；
- movement stop、game end 和 spawn failure hook。

每个 `SetIgnoreMoveInput(true)` 和 `SetIgnoreLookInput(true)` 都应有匹配的 false 调用。`ResetIgnoreInputFlags` 会清空两个 counter。

### Pawn

Pawn 提供：

- 有界累计 movement input；
- `ConsumeMovementInputVector`；
- controller rotation flag 和 eye height；
- 通过 `PawnConfig` 配置 pitch limit；
- restart 和 initial-rotation hook；
- player、bot 和 local-control query；
- turn-on/turn-off 状态。

Pawn 继承可选的 primary Actor Tick，但默认不参与 Tick。Movement adapter 应在该 movement 实现拥有的 phase 中消费 movement input 并调用 `ApplyControllerRotation`。基于 Rigidbody 的 adapter 通常保留 Unity `FixedUpdate`；确定性 simulator 可以改为公开显式 `Step`。

`NotifyInitialRotation` 会查找 Pawn 上实现 `IInitialRotationSettable` 的组件，并在 possession 完成前发布 spawn rotation。

### PlayerController 与 LocalPlayer

仅在分配 LocalPlayer 时，`PlayerController.IsLocalController` 才为 true。只有本地 PlayerController 可以拥有 CameraManager。Remote PlayerController 可以参与游戏、possess Pawn 并持有 PlayerState，而不创建本地相机状态。

被 possession 的 Pawn、spectator Pawn、manual view target 和 LocalPlayer 是独立关系。自动 view target 顺序为：

1. 被 possession 的 Pawn；
2. spectator Pawn；
3. PlayerController 自身。

`SetViewTarget` 创建 manual override。`ClearViewTargetOverride` 恢复 policy-driven targeting。

### AIController 与 PlayerStart

AIController 提供 focus Actor/focal point 状态，以及可重写的 `RunAI`/`StopAI`。它拥有 Update phase 的 primary Actor Tick：`RunAI` 启用 Tick，`StopAI` 禁用 Tick。运行期间，Tick 会将 control rotation 转向 focus。产品 behavior tree、navigation 和 perception 仍由 adapter 提供。

PlayerStart registration 为 World-scoped。Custom Editor 支持 3D、side-scroller 和 top-down gizmo 展示，不使用 Runtime static registry。

## PlayerState 与 GameState

### PlayerState

PlayerState 存储：

- 有界 player name；
- 非负 player ID；
- spectator status；
- 当前 Pawn 关联。

它可以在同一 World 内的 Pawn 替换过程中继续存在。`CopyProperties` 复制 identity field，不复制 Pawn link。

注册到 GameSession 期间，PlayerId 会被锁定，spectator status 由 session 的 atomic category-change operation 控制。Setters、property copying 和 snapshot restore 会拒绝冲突修改，直到完成注销。

`OnPawnSetEvent` 在 possession 提交后发布。Callback observer 可以读取一致的 Controller、Pawn 和 PlayerState 关系。

### PlayerStateSnapshot

`CaptureSnapshot` 创建包含以下字段的 Core `PlayerStateSnapshot`：

- `PlayerName`；
- `PlayerId`；
- `IsSpectator`。

`TryRestoreSnapshot` 会在修改状态前校验 ID 和 name 边界。Snapshot 不包含 Pawn、Controller、Transform、Unity object reference 或 World membership。Save/network adapter 持有 serialization、envelope metadata 与 storage。`PlayerStateSnapshot` 是 readonly value type，capture 不会分配 snapshot object。

### GameState

GameState 包含参与者 `PlayerArray`，并组合一个 Core `MatchStateMachine`。它拒绝 null/重复 PlayerState entry，并校验 World membership。State machine 持有合法 transition、已提交 `MatchState` 与 in-progress elapsed time；只有 Runtime 推进或提交状态时，GameState 才输入当前 Unity time。

合法 match transition：

| 当前状态 | 允许的下一状态 |
| --- | --- |
| EnteringMap | WaitingToStart、LeavingMap、Aborted |
| WaitingToStart | InProgress、LeavingMap、Aborted |
| InProgress | WaitingPostMatch、LeavingMap、Aborted |
| WaitingPostMatch | WaitingToStart、LeavingMap、Aborted |
| LeavingMap | 无 |
| Aborted | 无 |

Elapsed time 仅在 InProgress 时推进。WaitingPostMatch 到 WaitingToStart 的 transition 会重置累计时间。Core transition rule 可以在不创建 GameObject、不读取 Unity clock 的条件下测试。

GameMode 拥有 transition policy。需要可恢复结果时使用 `TrySetMatchState`；非法 transition 属于编程错误时使用 `SetMatchState`。

`MatchState` 是 Core 顶层 enum，由 `MatchStateMachine` 与 Runtime GameState facade 共同使用：

~~~csharp
if (!gameState.TrySetMatchState(MatchState.InProgress, out string error))
{
    throw new InvalidOperationException(error);
}
~~~

## Camera 系统

### 计算管线

~~~mermaid
flowchart LR
    VT["已解析 view target"] --> BP["Actor.CalcCamera 基础 pose"]
    BP --> BM["Base CameraMode"]
    BM --> SM["Stacked CameraMode<br/>从最早到最新"]
    SM --> PP["Post-processor<br/>按注册顺序"]
    PP --> FO["显式 FOV override"]
    FO --> BL["CameraBlendState"]
    BL --> OUT["CameraManager 输出"]
    OUT --> IO["ICameraOutput.ApplyPose"]
    IO --> UC["UnityCameraOutput"]
    IO -. 可选 .-> CM["CinemachineCameraOutput"]
~~~

### CameraContext

每个 PlayerController 按需创建一个 CameraContext。Context 拥有：

- view-target policy；
- resolved 和 manual view target；
- 一个 base CameraMode；
- 固定容量的 stacked-mode array。

默认 mode capacity 为 8，可通过重写 `PlayerController.GetCameraModeStackCapacity` 修改。请求的容量非正数时会变为 1。

`TryPushCameraMode` 会拒绝 null、重复实例、clearing 状态和容量溢出。`TryPushOrReplaceOldest` 提供显式 full-stack policy。CameraManager evaluation 期间会拒绝 base-mode replacement 以及 stack push、replace 或 remove，使正在迭代的 stack 保持稳定。Evaluation 期间请求的 `Clear` 会延迟到 evaluation scope 结束；随后按逆序 deactivate stacked mode，再 deactivate base mode。

### Camera Mode 与 Blend

继承 `CameraMode` 并实现：

~~~csharp
public override CameraPose Evaluate(
    CameraContext context,
    in CameraPose basePose,
    float deltaTime)
{
    return basePose;
}
~~~

Base mode 最先计算。Stacked mode 随后从 index 0 计算到最新 entry。最新 stacked mode 是 primary mode，用于选择 transition blend duration。

`CameraBlendState` 支持 Linear、SmoothStep、EaseOut、EaseIn 和自定义 `ICameraBlendCurve` 计算。负 blend duration 会被限制为零。

### CameraManager 与输出所有权

GameMode 登录过程中，只有本地 PlayerController 且 WorldDefinition 包含 CameraManager prefab 时才创建 CameraManager。

它会：

- 初始化后通过 LateUpdate phase 的 primary Actor Tick 计算相机状态；
- 解析 authoring `CameraOutputBehaviour`，或通过 `SetCameraOutput` 接收显式 `ICameraOutput`；
- prepare backend，并向 World 申请独占 backend resource；
- 仅在 ownership 成功后 activate output；
- 通过 `ApplyPose` 发布最终 pose 和 FOV；
- 在 output replacement、Actor teardown 或 World shutdown 时 deactivate output 并释放 ownership。

两个 CameraManager 同时申请同一 output resource 时，World 会拒绝后一个请求。没有 output 的 CameraManager 仍会继续计算并公开 `CurrentPose`；output 是可选的 presentation boundary。

### Core Unity Camera Output

`UnityCameraOutput` 位于 GameplayFramework Runtime assembly。可以显式分配 `UnityEngine.Camera`，也可以把 Camera 放在 output hierarchy。该 component 可选择应用最终 transform、field of view 或两者，不需要其他 camera package；PureUnity sample 使用此 output。

GameplayFramework Runtime 与 public interface 不包含 Cinemachine 类型。自定义 backend 可以直接实现 `ICameraOutput`，也可以继承 `CameraOutputBehaviour` 获得 Unity authoring 支持。`TryPrepare` 返回作为独占 ownership resource 的 Unity object；`TryActivate`、`ApplyPose` 与 `Deactivate` 都在 World owner thread 执行。

### 可选 Cinemachine Output

安装受支持 `[3.0.0,4.0.0)` 范围的 `com.unity.cinemachine` 后，gated assembly `CycloneGames.GameplayFramework.Runtime.Integrations.Cinemachine` 提供 `CinemachineCameraOutput`。其 asmdef 使用 `versionDefines`、`defineConstraints` 与 `autoReferenced: false`；GameplayFramework package 不声明 Cinemachine dependency。

通过 `SetVirtualCamera`/`SetBrain` 或 serialized field 分配 `CinemachineCamera` 与 `CinemachineBrain`。可选 scene discovery 只有在可以无歧义选择 camera 与 brain 时才会成功。Activate 时，output 保存 brain update mode 以及 camera Follow/LookAt target，选择 manual brain update，应用 pose 与 lens，并在 deactivate 时恢复保存的状态。World 把 brain 作为独占 output resource。

### View Target 与 Post-processor

`DefaultGameplayViewTargetPolicy` 依次解析 manual override、suggested target、被 possession 的 Pawn、spectator Pawn 和 PlayerController。

CameraManager 最多支持 16 个已注册 `ICameraPostProcessor`。它们在所有 CameraMode 后按注册顺序运行。Owner 结束时应 unregister processor。

`PerlinNoiseShakePostProcessor` 是带 trauma、amplitude、frequency、decay 和 exponent 控制的 Runtime object。

### Camera Action

`CameraActionBinding` 将 string action key 映射到 `CameraActionPreset`：

1. 先检查 inline entry；
2. `CameraActionMap` 作为 fallback；
3. Map 中重复 key 使用最后一个 entry。

Trigger policy：

- `ReplaceSameKey`；
- `IgnoreIfRunning`；
- `Stack`。

Binding 具有可配置 active-action 和 pooled-mode limit，默认均为 8。达到 active limit 或 CameraContext capacity 时，`PlayAction`/`PlayPreset` 返回 false。Pool 中没有可用 mode 时会创建 `PresetCameraMode`；返回的 mode 只保留到配置的 pool limit。

Disable 或 destroy 时，binding 会停止 active action，并从最初接受它们的 PlayerController 移除对应 mode。

可用 bridge：

- `AnimatorCameraActionBridge`，用于 Animation Event；
- `CameraActionStateBehaviour`，用于 Animator state enter、progress threshold 和 exit；
- `TimelineCameraActionReceiver`，用于 Playables notification；
- Gameplay 代码直接调用。

每个 CameraActionStateBehaviour 实例最多跟踪 8 个并发 Animator/layer pair。满载时 enter 和 exit action 继续执行；额外 pair 的 progress trigger 会暂停，直到槽位释放。`OnStateExit` 负责释放槽位。

Exit mode 可以不执行操作、停止 action key 或播放 action key。Progress 在 normalized time 跨过配置 threshold 时触发，并可配置为整个 state lifetime 一次，或每个 loop 一次。Enter 和 progress trigger 分别拥有独立 transition gate。

### Camera Authoring Asset

| Asset/Runtime 类型 | 用途 |
| --- | --- |
| `CameraProfile` | 共享默认 FOV 和 fallback blend duration；需要显式调用 `ApplyTo` |
| `CameraActionPreset` | Action shot 的定时 framing、offset、lens、weight curve 和 blend 数据 |
| `CameraActionMap` | 带 lazy runtime lookup 的共享 action-key table |
| `PresetCameraMode` | CameraActionBinding 使用的 Runtime evaluator |
| `ViewTargetCameraMode` | 使用 resolved Actor camera pose 的 pass-through base mode |

可在 Runtime 使用的 CameraModes sample 包含 first-person、orbital、third-person follow 和 collision post-processor 示例。

## 集成

GameplayFramework Core 没有 engine reference。GameplayFramework Runtime 依赖 Core，以及自身声明的 Unity-facing package 与 asmdef reference。可选产品 bridge 位于 sibling companion package，或位于允许第三方依赖缺失的 gated integration assembly。

| Package | Assembly | 能力 | 启用方式 |
| --- | --- | --- | --- |
| GameplayFramework package | `CycloneGames.GameplayFramework.Runtime.Integrations.Cinemachine` | `CinemachineCameraOutput` | 显式引用；仅在受支持 Cinemachine 版本下编译 |
| `com.cyclone-games.gameplay-framework-factory` | `CycloneGames.GameplayFramework.Runtime.Integrations.Factory` | `FactoryActorLifetime` | 安装 companion 与 Factory；显式引用 |
| `com.cyclone-games.gameplay-framework-asset-management` | `CycloneGames.GameplayFramework.Runtime.Integrations.AssetManagement` | `AssetManagementWorldSettingsReferenceResolver` | 安装 companion；显式引用 |
| `com.cyclone-games.gameplay-framework-gameplay-abilities` | `CycloneGames.GameplayFramework.Runtime.Integrations.GameplayAbilities` | AbilitySystem provider 与 Actor-info helper | 安装 companion；显式引用 |
| `com.cyclone-games.gameplay-framework-gameplay-tags` | `CycloneGames.GameplayFramework.Runtime.Integrations.GameplayTags` | Actor tag-container extension method | 安装 companion；显式引用 |
| `com.cyclone-games.gameplay-framework-networking` | `CycloneGames.GameplayFramework.Networking.Core` | 纯 protocol、Actor migration message/codec、damage validation 与 observer rule | 安装 companion 与 Networking；protocol code 显式引用 |
| `com.cyclone-games.gameplay-framework-networking` | `CycloneGames.GameplayFramework.Networking.Runtime` | Actor capture/apply、World replication 与 Runtime session adapter | Unity replication code 显式引用 |
| GameplayFramework package | `CycloneGames.GameplayFramework.Runtime.Integrations.Navigathena` | `ISceneTransitionHandler` adapter | 显式引用；条件编译 |

### Factory Actor Lifetime

Factory companion 把 Factory 的 `IUnityObjectLifetime` 适配为 core `IActorLifetime`。通过 `GameplayWorldComposition` 或直接向 `GameInstance` 提供 `FactoryActorLifetime`。World 仍是 created Actor instance 的唯一 owner，并为每次成功创建配对一次 release。该边界用于 creation/destruction policy，不会让已进入终态的 Actor instance 变为可复用对象。以 UPM 安装时，companion 声明的 GameplayFramework 与 Factory dependency 由 Package Manager 解析；嵌入 `Assets` 时，物理隔离的 asmdef 直接引用两个模块，不需要 PlayerSettings define。

### AssetManagement

WorldSettings entry 使用 `AssetReference` 时安装 AssetManagement companion。显式组合 `IAssetPackage`，并通过 `GameplayWorldComposition` 或直接向 GameInstance 提供 resolver。

### GameplayAbilities

在 Actor 或其组件上实现 `IAbilitySystemProvider`，然后使用：

- `TryGetAbilitySystem`；
- `InitializeAbilityActorInfo`。

Owner 和 avatar override 都是显式参数。未提供 override 时，会在可用时使用 Actor owner，并使用 Actor 作为 avatar。

GameplayAbilities companion 不负责调度 `AbilitySystemComponent.Tick`。Ability-system owner 选择自身 clock 并显式转发。需要 World 生命周期 gate 时，GameplayFramework Actor 可以从 primary Tick 转发；独立 Unity composition 可以保留专用 MonoBehaviour driver。Movement 和 physics component 继续持有自身 phase。

### GameplayTags

在 Actor GameObject 上添加 `GameObjectGameplayTagContainer`。Integration 提供：

- `TryGetGameplayTagContainer`；
- `ActorHasGameplayTag`；
- `AddGameplayTag`；
- `RemoveGameplayTag`。

Actor 的轻量 string tag 与 GameplayTags container 是独立 API。

这些 extension method 会执行 component discovery，只用于 composition、初始化和其他冷路径。需要重复检查或修改 Tag 的代码，应只调用一次 `TryGetGameplayTagContainer`，在 Actor/component lifetime 内保留返回的 container，并直接使用缓存引用。Integration 不提供隐藏的每帧缓存，也不接管 container 的所有权。

GameplayTags integration 由 sibling companion package 提供，该 companion 持有 GameplayTags dependency。GameplayFramework Core 与 Runtime 都不引用 GameplayTags。使用方从自身 asmdef 显式引用 companion integration assembly；`autoReferenced: false` 会阻止无关 assembly 隐式取得该 API。

### Networking

Networking sibling package 提供两个显式层。`CycloneGames.GameplayFramework.Networking.Core` 没有 engine reference，持有 protocol message、bound、codec、observer selection 与 validation。`CycloneGames.GameplayFramework.Networking.Runtime` 持有 `NetworkGameSessionAdapter`、Actor capture/apply、World replication，以及所有读写 Unity gameplay object 的 adapter。

`NetworkGameSessionAdapter` 可作为权威端 `IGameSession` 传入。Client replication adapter 先注册接收的 GameState Actor 并调用 `World.SetReplicatedGameState`；对每个接收的 PlayerController 和 PlayerState，先完成注册与 Controller 初始化，再调用 `World.CommitReplicatedPlayerController`。这两个 client-only commit boundary 都必须在 World owner thread 运行，并会拒绝 authority World。Transport callback 必须先完成认证并 marshal 到该线程，之后才能调用 gameplay API。

Protocol code 引用 Networking Core。Unity replication code 引用 Networking Runtime，以及源码直接使用的每个 GameplayFramework assembly。GameplayFramework Core 与 Runtime 都不引用 Networking companion。

Actor migration 的 transport value 位于 Networking Core。`ActorMigrationState` 使用 `NetworkVector3`、`NetworkQuaternion`、prefab definition ID、有界 readonly tag 与其他 transport-facing Actor state，不持有 Unity object reference。Networking Runtime 提供 `ActorNetworkingExtensions.CaptureMigrationState` 与 `ApplyMigrationState`，在该值与 Actor 之间转换；还提供 `ToNetworkedGameplayActor` 以及 observer 配置的 Unity `Vector3` overload。这些转换用于显式 replication 或 travel boundary，不应放入 Actor Tick。

### Navigathena Package 边界

Navigathena integration 要求 UPM package 名为 `com.mackysoft.navigathena`，支持范围为 `[1.1.0,2.0.0)`。GameplayFramework 的 `package.json` 不包含 Navigathena dependency，因此安装 GameplayFramework 不会同时安装 Navigathena。新的主版本需要完成 API 兼容验证后再扩展范围。

Integration asmdef 持有以下启用规则：

~~~text
versionDefines: com.mackysoft.navigathena [1.1.0,2.0.0) -> CYCLONEGAMES_HAS_NAVIGATHENA
defineConstraints: CYCLONEGAMES_HAS_NAVIGATHENA
autoReferenced: false
~~~

当 Package Manager 未解析到 Navigathena 时，integration assembly 及其测试不会参与编译。GameplayFramework Runtime、Editor 工具、sample 和核心测试均不依赖 Navigathena。无需在 PlayerSettings 中维护 scripting define。

调用 adapter 的代码应放在项目自身的 integration asmdef 中。该 asmdef 需要引用 GameplayFramework integration 和 Navigathena assembly，并配置自己的 `versionDefines`/`defineConstraints`；version define 生成的 symbol 仅作用于所属 assembly：

~~~json
{
  "name": "Game.Runtime.Integrations.Navigathena",
  "references": [
    "CycloneGames.GameplayFramework.Runtime",
    "CycloneGames.GameplayFramework.Runtime.Integrations.Navigathena",
    "MackySoft.Navigathena",
    "MackySoft.Navigathena.SceneManagement"
  ],
  "autoReferenced": false,
  "defineConstraints": [
    "GAME_HAS_NAVIGATHENA"
  ],
  "versionDefines": [
    {
      "name": "com.mackysoft.navigathena",
      "expression": "[1.1.0,2.0.0)",
      "define": "GAME_HAS_NAVIGATHENA"
    }
  ]
}
~~~

### Navigathena 最小组合

默认 adapter 将 `ISceneTransitionHandler` 接收的 string 作为 built-in scene name。它向 Navigathena 传递 null transition director，使 `StandardSceneNavigator` 使用自身配置的默认转场。

~~~csharp
public static GameInstance CreateGameInstance(ISceneNavigator sceneNavigator)
{
    var sceneTransitions =
        new NavigathenaSceneTransitionHandler(sceneNavigator);

    return new GameInstance(
        new UnityActorLifetime(),
        localPlayerCount: 1,
        referenceResolver: null,
        sceneTransitionHandler: sceneTransitions);
}
~~~

World 进入 Playing 后，authority 端代码可以请求关卡 travel：

~~~csharp
await world.GameMode.TravelToLevel("Stage02", cancellationToken);
~~~

该调用先停止当前 World，再调用 `ISceneNavigator.Change`。目标 scene 的 composition root 负责启动自身 World。Gameplay travel 发生前，应按照 Navigathena 的生命周期初始化传入的 `ISceneNavigator`。

### 通过 Host 组合 Navigathena

由 `GameplayWorldHost` 持有 GameInstance 时，需要在 Host 启动前通过 composition 提供 Navigator：

~~~csharp
var sceneTransitions =
    new NavigathenaSceneTransitionHandler(sceneNavigator);

host.Configure(new GameplayWorldComposition(
    new UnityActorLifetime(),
    sceneTransitionHandler: sceneTransitions));
~~~

项目 composition root 应在 Unity 调用 Host 的 `Start` 前执行 `Configure`。如果无法保证该顺序，请关闭 **Auto Start**，完成配置后再调用 `StartWorldAsync`。`GameplayWorldHost` 是 sealed；integration 通过 `GameplayWorldComposition` 完成，不通过继承完成。

### 自定义 Navigathena Request

`NavigathenaLoadSceneRequestFactory` 会在每次 Change、Push 和 Replace 时接收操作类型与 scene key。它返回完整的 Navigathena `LoadSceneRequest`，因此同一 adapter 可以选择自定义 scene identifier、transition director、scene data 和 interrupt operation，而不会把这些类型加入 GameplayFramework 核心契约。

~~~csharp
public static ISceneTransitionHandler CreateSceneTransitions(
    ISceneNavigator navigator,
    Func<string, ISceneIdentifier> resolveScene,
    ITransitionDirector levelTransition,
    ITransitionDirector overlayTransition,
    ISceneData travelData,
    IAsyncOperation interruptOperation)
{
    LoadSceneRequest CreateLoadRequest(
        NavigathenaSceneTransitionOperation operation,
        string sceneKey)
    {
        ITransitionDirector transition =
            operation == NavigathenaSceneTransitionOperation.Push
                ? overlayTransition
                : levelTransition;

        return new LoadSceneRequest(
            resolveScene(sceneKey),
            transition,
            travelData,
            interruptOperation);
    }

    return new NavigathenaSceneTransitionHandler(
        navigator,
        CreateLoadRequest,
        () => new PopSceneRequest(
            overlayTransition,
            interruptOperation));
}
~~~

Scene key 是产品侧输入。Resolver 应在构造 identifier 前拒绝未知或格式错误的 key。Navigathena history、`Reload`、直接进度报告，以及 `ISceneTransitionHandler` 范围之外的 navigation 操作，仍可通过注入的 `ISceneNavigator` 使用。

Integration asmdef 直接引用其依赖 assembly。Companion package 持有自身依赖；gated core integration 会在受支持的可选依赖缺失时退出编译。

## Editor 工具

| 工具 | 功能 |
| --- | --- |
| Actor Inspector | Serialized Actor 字段、primary Tick authoring、派生字段、多对象编辑、Runtime 生命周期和 Tick 状态，以及 Play Mode Tick 启用/禁用控制 |
| ActorTag drawer | 为标记 `ActorTagAttribute` 的字段提供可搜索选择 |
| WorldSettings Inspector | 必需/可选概览、Direct/Asset/Path authoring、校验按钮 |
| GameplayWorldHost Inspector | WorldSettings 与 external-resolver 提示、explicit-composition 状态、有效 local-player count、Runtime 状态和 Start/Stop 控制 |
| GameMode Inspector | Runtime mode state 和带 Ping 的 PlayerController roster |
| PlayerStart Inspector | 可配置 3D、side-scroller 和 top-down scene gizmo |
| CameraManager Inspector | Configured/active output、owner、pose、blend、view target、mode 和 FOV telemetry |
| CinemachineCameraOutput Inspector | Integration 参与编译时显示可选 active CinemachineCamera 与 CinemachineBrain telemetry |
| CameraActionStateBehaviour Inspector | 条件式 enter/exit/progress authoring 和容量说明 |
| Camera Debug Window | 带缓冲的 camera telemetry、graph 和可配置 alert |
| World Debugger | Host、World、session、Actor admission/allocated-capacity/peak/rejection 诊断、各 phase Tick count 和索引式 Actor registration 检查 |
| Project Validation | 只读扫描 WorldSettings asset 与已加载 scene 中的 Host，并提示 external-resolver composition 要求 |

通过以下菜单打开 camera window：

~~~text
Tools > CycloneGames > GameplayFramework > Camera Debug Window
~~~

World 和 authoring 工具入口：

~~~text
Tools > CycloneGames > GameplayFramework > World Debugger
Tools > CycloneGames > GameplayFramework > Project Validation
~~~

该 window 只在 Play Mode 采样。Sampling mode 包括 Off、Basic 和 Full。采样率可配置为 5 到 120 Hz。内存 ring buffer 可配置为 120 到 2048 个 sample，默认为 600。Full mode 会额外采样 linear 和 angular speed。Alert threshold 覆盖 FOV delta、blend remaining time、blend stall 和 motion speed。

Editor 诊断仅用于观测。将诊断结果作为发布依据前，必须在目标 Player 和 Profiler 中验证性能。

## 持久化与数据所有权

框架不会写入 Runtime save file 或 preference key。

| 数据 | Owner | 模块提供的存储 | 版本控制 | 生命周期与清理 |
| --- | --- | --- | --- | --- |
| WorldSettings | 项目 authoring | ScriptableObject asset | 通常纳入 | 通过 serialized asset 编辑并校验 |
| Actor phase 和 startup Tick flag | Scene/prefab authoring | Serialized MonoBehaviour 字段 | 通常纳入 | 通过 Actor Inspector 编辑；临时变更使用 Runtime API |
| GameModeConfig、PawnConfig、CameraProfile | 项目 authoring | ScriptableObject asset | 通常纳入 | 通过对应 Inspector 编辑与校验 |
| CameraActionPreset、CameraActionMap | 项目 authoring | ScriptableObject asset | 通常纳入 | 保持 action key 与使用方配置一致 |
| WorldDefinition | World Runtime | 仅内存 | 否 | 随 World dispose；按逆序释放 lease |
| GameplayWorldHost、GameInstance、LocalPlayer、World | Runtime composition | 仅内存 | 否 | Host GameObject lifetime 或显式 Stop/Dispose |
| PlayerStateSnapshot | Save/network adapter | Core 内存值 | 取决于 adapter | Restore 前校验 persistence 或 protocol envelope |
| Camera debug sample | CameraDebugWindow | 仅 Editor 内存 | 否 | 清空 buffer，或关闭/重新加载 window |
| World Debugger 和 Project Validation 状态 | Editor window | 仅 Editor 内存 | 否 | 关闭或重载窗口；不写入 EditorPrefs 或 SessionState |

对于存档数据：

1. 在受控边界捕获 PlayerStateSnapshot；
2. 把它传给专用 save service；
3. 包含由 save service 拥有的 slot、format 与 integrity metadata；
4. 原子写入平台 persistent-data location；
5. 反序列化前校验大小和完整性；
6. 按当前 build 的产品格式校验 payload；
7. 调用 `TryRestoreSnapshot` 并处理 error。

应选择并验证在目标 backend 上支持 readonly `PlayerStateSnapshot` property 的 serializer；Unity `JsonUtility` 不会直接序列化该 contract。

## 性能、线程与平台说明

### 线程所有权

- GameInstance 和 World 修改由单一 owner thread 执行。
- GameInstance 记录构造线程 ID。
- Actor Tick dispatch、phase change 和 Runtime enable change 使用同一个 owner thread。
- Network、file 和 asset callback 在修改框架状态前必须 marshal 到 Unity main thread。
- Unity object 与 camera-output 操作运行在 Unity main thread；可选 camera backend 遵循同一规则。
- WorldSettings resolver I/O 可以在其他线程完成；result validation、rollback、lease transfer 和 WorldDefinition disposal 会在执行解析的 main thread 上运行。跨线程调用 WorldDefinition disposal 会在消费所有权前被拒绝。
- `ParticipantRoster`、`MatchStateMachine` 与 `GameSession` 都把构造线程记录为 single owner，不添加 lock。
- 对三者可变状态的读取与所有 mutation 都会校验 owner；跨线程访问以 `InvalidOperationException` 失败，不会读取可能不一致的 roster、match clock 或 Runtime binding。
- Immutable limit value 与 static validation/transition-policy function 不读取可变 instance state。Worker result 仍须 marshal 到 owner，之后才能访问 live roster、match state machine 或 session。
- Async API 使用 UniTask，并在启动和 asset resolution 期间传播 cancellation。World initialization 会链接 caller、GameInstance 和 World lifetime token；direct World shutdown 会取消 pending async login，阻止 startup 继续提交。

### 有界结构

| 结构/输入 | 限制或默认值 |
| --- | --- |
| LocalPlayer 槽位 | 最多 8 |
| World Actor registration | 由 `WorldRuntimeLimits.MaximumActorCount` 配置；不会超过 `WorldRuntimeLimits.MaximumSupportedActorCount` |
| World collection 初始容量 | Actor 128、Update 128、FixedUpdate 32、LateUpdate 32；每项都会 cap 到 configured Actor maximum |
| Actor string tag | 最多 64 个；每个最多 128 个字符 |
| 登录文本输入 | name/address/options 分别为 64/256/1024 个字符 |
| GameSession 参与者总数 | 最多 100,000 |
| CameraContext mode | 每个 context 固定；默认 8 |
| CameraManager post-processor | 最多 16 |
| CameraActionStateBehaviour tracking | 最多 8 个 Animator/layer pair |
| CameraActionBinding active/pool count | 可配置；默认各 8 |
| Actor primary Tick phase | 每个 Actor 一个 phase；热路径 registry size 取决于 Runtime-enabled Actor 数量 |

`WorldRuntimeLimits.MaximumSupportedActorCount` 是 implementation safety ceiling，不是产品预算。通过 `WorldRuntimeLimits` 定义经过测量的单 World maximum 与 initial capacity，使用 `Try*` API 处理可恢复拒绝，并监测 peak/rejection 诊断。Roster growth 由 GameSession limit 单独约束。

### 分配点

性能分析时应检查以下 cold path 或 boundary operation：

- World construction 预留 configured initial collection capacity；
- World scene discovery，以及超过 initial capacity 后的 collection growth；
- WorldSettings resolution 和 lease array 创建；
- PlayerState snapshot capture 之后的 persistence/serializer 工作；
- Actor tag 和 renderer buffer 首次使用；
- Actor lifespan cancellation source 创建；
- Actor 注册期间的 Tick registry 和可复用 snapshot capacity growth；
- CameraContext 构造；
- CameraActionMap lazy lookup 构造；
- CameraActionBinding pool 为空时创建 mode；
- timed Animation Event 的 string parsing；
- 诊断 window buffer resize。

Actor Tick dispatch 遍历可复用 phase snapshot，不扫描 Tick phase 为 None 的 Actor。Core rule composition 不会创建每 Actor mirror object，也没有逐帧同步 pass：一个 roster 属于一个 GameSession，一个 state machine 属于一个 GameState。Dense registry、固定 camera array 与可复用 Tick collection 用于降低容量稳定后的 steady-state allocation。Startup、Actor create/release、snapshot serialization、resolver、logging backend、用户 callback、可选 integration 与 collection growth 仍可能分配。模块不声明全局 zero-GC；必须使用代表性 workload 在目标硬件上进行 allocation profiling。

### Player、IL2CPP 与 Server Build

- `CycloneGames.GameplayFramework.Core` 设置 `noEngineReferences: true`，只包含不依赖 engine 的规则与值类型。
- `CycloneGames.GameplayFramework.Runtime` 引用 Core、UnityEngine、Mathematics、UniTask 与 Logging contract。Cinemachine、Factory、AssetManagement、GameplayAbilities、GameplayTags 与 Networking 保持隔离在可选 integration 或 companion assembly 中。
- GameplayWorldHost 使用独立的 sealed early/late MonoBehaviour driver。直接组合 GameInstance 时必须提供等价 loop owner。
- PlayerStateSnapshot 序列化由外部提供。基于反射的 serializer 可能需要 AOT metadata 或 link preservation。
- DedicatedServer mode 会抑制自动本地登录，但 Runtime assembly 仍包含其声明依赖。
- Client mode 本身不提供 replication。
- Mono、IL2CPP、managed stripping、headless/server 和每个目标平台都需要代表性 Player build 验证。

Owner-thread enforcement 与有界输入提供一致的 failure rule，但框架不会让 Unity physics、floating-point result、scene discovery order、用户 callback 或第三方 integration 在不同硬件上自动具备确定性。需要 lockstep 或 replay determinism 的产品，必须在该 gameplay-flow 层之上提供 deterministic simulation model、clock、ordering policy、numeric policy 与目标平台验证。

## 由简到深示例

### 不创建 Unity Object 使用 Core 规则

引用 `CycloneGames.GameplayFramework.Core` 并导入 `CycloneGames.GameplayFramework.Core`。Roster 可以在不创建 GameObject 的条件下校验并提交有界参与者 membership：

~~~csharp
var roster = new ParticipantRoster(
    maximumPlayers: 2,
    maximumSpectators: 1);

ParticipantRegistrationResult registration = roster.Register(
    participantId: 42,
    category: ParticipantCategory.Player);

if (registration != ParticipantRegistrationResult.Success)
{
    throw new InvalidOperationException(
        $"Participant registration failed: {registration}");
}
~~~

创建 roster 的线程持有它。`EvaluateRegistration`、`Register`、`Remove`、`ChangeCategory`、`Contains`、`TryGetCategory` 与 `AtCapacity` 都会校验 owner。

Match state machine 接收应用持有、以秒为单位的 monotonic timestamp：

~~~csharp
var match = new MatchStateMachine(timestamp: 10d);
match.TryTransition(MatchState.WaitingToStart, timestamp: 11d);
match.TryTransition(MatchState.InProgress, timestamp: 12d);

double elapsedSeconds = match.GetElapsedSeconds(timestamp: 15.5d);
~~~

Core 不读取 Unity time，也不调度 update。GameState 在 Runtime 边界输入 Unity time；纯 simulation 或 server 输入自身的 monotonic clock。

### 查询 World Actor

~~~csharp
if (world.TryGetActor<PlayerStart>(out PlayerStart start))
{
    Vector3 startLocation = start.GetActorLocation();
}
~~~

Type lookup 适合发现，不适合确定性选择。产品选择应使用显式 identifier 或 policy。

### 创建可选参与 Tick 的 Actor

~~~csharp
public sealed class RotatingActor : Actor
{
    [SerializeField] private Vector3 RotationAxis = Vector3.up;
    [SerializeField, Min(0f)] private float DegreesPerSecond = 45f;

    protected override void Awake()
    {
        base.Awake();
        ConfigureActorTick(
            ActorTickPhase.Update,
            startWithTickEnabled: true);
    }

    protected override void Tick(float deltaSeconds)
    {
        if (RotationAxis.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        transform.Rotate(
            RotationAxis.normalized,
            DegreesPerSecond * deltaSeconds,
            Space.Self);
    }
}
~~~

可以把该 component 添加到 scene object，也可以通过 World spawn。World 只会在 BeginPlay 后开始 dispatch。调用 `SetActorTickEnabled(false)` 可以暂停该 Actor，而无需禁用 GameObject；调用 `SetActorTickPhase` 可以在 owner thread 上切换 phase。Package 内提供可编译的对应示例：`Samples/Sample.PureUnity/UnitySampleRotatingActor.cs`。

### 转发直接组合的 GameInstance

`GameplayWorldHost` 已经持有这组转发。自定义 composition root 应在自身拥有的每个 phase 中调用同一个 instance 一次：

~~~csharp
private void Update()
{
    gameInstance?.Tick(ActorTickPhase.Update, Time.deltaTime);
}

private void FixedUpdate()
{
    gameInstance?.Tick(ActorTickPhase.FixedUpdate, Time.fixedDeltaTime);
}

private void LateUpdate()
{
    gameInstance?.Tick(ActorTickPhase.LateUpdate, Time.deltaTime);
}
~~~

存在 GameplayWorldHost 时不要添加第二个 forwarder。Headless host 可以从显式 loop 调用同一 API，但 delta 必须经过校验、有限且非负。显式 Tick 本身不会使 gameplay 具备确定性。

### Deferred Spawn 与 Possession

~~~csharp
Pawn pawn = world.SpawnActorDeferred(pawnPrefab);
bool committed = false;

try
{
    pawn.SetPawnConfig(pawnConfig);
    pawn.SetActorLocationAndRotation(spawnPosition, spawnRotation);

    if (!controller.TryPossess(pawn, out string error))
    {
        throw new InvalidOperationException(error);
    }

    world.FinishSpawningActor(pawn);
    committed = true;
}
finally
{
    if (!committed && world.IsActorRegistered(pawn))
    {
        if (ReferenceEquals(controller.GetPawn(), pawn))
        {
            controller.UnPossess();
        }

        world.DestroyActor(pawn, EndPlayReason.InitializationFailure);
    }
}
~~~

Controller 必须已经注册并针对同一 World 完成初始化。

### 权威端远程登录

~~~csharp
PlayerLoginRequest request = new PlayerLoginRequest(
    playerId: authenticatedPlayerId,
    playerName: validatedDisplayName,
    isSpectator: false,
    remoteAddress: normalizedAddress,
    options: validatedOptions,
    isLocal: false);

PlayerLoginResult result = await world.GameMode.LoginAsync(
    request,
    localPlayer: null,
    cancellationToken);

if (!result.Succeeded)
{
    throw new InvalidOperationException(
        $"Login failed with {result.Status}: {result.Error}");
}

PlayerController remoteController = result.PlayerController;
~~~

Authentication 和 transport check 必须在调用前完成。该调用应在 World owner thread 执行。

### 捕获与恢复参与者状态

~~~csharp
PlayerStateSnapshot snapshot = sourcePlayerState.CaptureSnapshot();

if (!targetPlayerState.TryRestoreSnapshot(snapshot, out string error))
{
    throw new InvalidDataException(error);
}
~~~

`PlayerStateSnapshot` 是 readonly Core value；`CaptureSnapshot` 与 `TryRestoreSnapshot` 是 Runtime facade API。持久化 integration 持有 file path、serialization envelope、atomic replace、integrity 与 encryption policy。

### 校验轻量 Actor Tag

Core 提供与 Runtime Actor operation 相同的有界 tag validation：

~~~csharp
if (!ActorTagLimits.TryValidate(tag, out ActorTagValidationResult result))
{
    throw new ArgumentException($"Invalid Actor tag: {result}", nameof(tag));
}
~~~

`NullOrWhiteSpace` 与 `TooLong` 是显式 validation result。Actor 在添加或替换 tag 时还会执行 `ActorTagLimits.MaximumTagCount`。

### 自定义 Camera Mode

~~~csharp
public sealed class ShoulderOffsetCameraMode : CameraMode
{
    private readonly Vector3 localOffset;

    public ShoulderOffsetCameraMode(Vector3 localOffset)
    {
        this.localOffset = localOffset;
    }

    public override float BlendDuration => 0.12f;

    public override CameraPose Evaluate(
        CameraContext context,
        in CameraPose basePose,
        float deltaTime)
    {
        Vector3 offset = basePose.Rotation * localOffset;
        return new CameraPose(
            basePose.Position + offset,
            basePose.Rotation,
            basePose.Fov);
    }
}

CameraMode mode =
    new ShoulderOffsetCameraMode(new Vector3(0.45f, 0f, 0f));

if (!playerController.TryPushCameraMode(mode))
{
    throw new InvalidOperationException(
        "The camera mode stack rejected the mode.");
}
~~~

拥有该 action 的流程结束时，应移除同一个 mode instance。

## 验证

### EditMode 测试

Core test assembly 覆盖：

- participant capacity、重复 identity、register、remove 与 spectator-category accounting；
- 有界 login request 与 `PlayerLoginStatus` value；
- match-state transition、elapsed-time accounting 与 reset rule；
- 不引用 UnityEngine 的 World limit、Actor admission snapshot、player snapshot 与 Actor-tag bound。

Unity EditMode test assembly 覆盖：

- World mode、启动、回滚、client GameState/PlayerController commit boundary、non-owned Actor 复用、可信 local login 校验、participant/GameMode 销毁升级、logout 和 CurrentWorld 清理；
- terminal Actor-lifetime 在正常销毁、spawn failure、self-destruction、shutdown 与 throwing-release failure isolation 中的配对；
- WorldSettings 校验、外部 resolver、cancellation 和 lease disposal；
- GameplayWorldHost composition、不可变 WorldRuntimeLimits、Actor admission/allocated-capacity/peak/rejection 诊断、索引式 World 检查、自定义 Inspector 和项目校验；
- Actor tag、damage、lifespan、possession、Pawn input、primary Tick phase、Runtime gate、mutation safety、re-entry rejection、exception isolation 和 owner-thread enforcement；
- PlayerState snapshot、session identity lock、atomic spectator change 和 post-commit Pawn notification；
- GameState transition 和 World-scoped PlayerStart；
- CameraContext capacity、replacement、evaluation mutation guard、deferred clear、teardown order、view-target policy 和 action limit；
- Camera blend、camera math、core UnityCameraOutput、output ownership、replacement、teardown 与 failure isolation；
- CameraContext、GameSession 和 1,000 个 opt-in Actor Tick 性能 benchmark；
- 安装受支持 Navigathena package 时的 request 映射、自定义、校验和 cancellation 转发。

从 Unity Test Runner 运行：

~~~text
Window > General > Test Runner > EditMode
Assembly: CycloneGames.GameplayFramework.Core.Tests.Editor
Assembly: CycloneGames.GameplayFramework.Tests.Editor
~~~

安装 Navigathena `[1.1.0,2.0.0)` 后，还需要运行：

~~~text
Assembly: CycloneGames.GameplayFramework.Integrations.Navigathena.Tests.Editor
~~~

未安装 Navigathena 时，确认 integration assembly 与 test assembly 均未出现在 `Library/ScriptAssemblies` 中，然后运行上述 Core 与 Unity package test assembly。

产品包含哪些可选 integration 与 companion，就运行对应测试：

| 能力 | EditMode test assembly |
| --- | --- |
| Cinemachine output | `CycloneGames.GameplayFramework.Integrations.Cinemachine.Tests.Editor` |
| Factory Actor lifetime | `CycloneGames.GameplayFramework.Integrations.Factory.Tests.Editor` |
| AssetManagement resolver | `CycloneGames.GameplayFramework.Integrations.AssetManagement.Tests.Editor` |
| GameplayAbilities bridge | `CycloneGames.GameplayFramework.Integrations.GameplayAbilities.Tests.Editor` |
| GameplayTags bridge | `CycloneGames.GameplayFramework.Integrations.GameplayTags.Tests.Editor` |
| Networking protocol 与 rule layer | `CycloneGames.GameplayFramework.Networking.Core.Tests.Editor` |
| Networking Unity adapter | `CycloneGames.GameplayFramework.Networking.Runtime.Tests.Editor` |

对于 UPM 使用方，GameplayFramework package 需要在可选 package 全部缺失时验证一次，并在每个已选 integration dependency 存在时分别验证。缺失可选依赖不得阻止 Core、Runtime 或 Editor assembly 编译。

Batchmode 示例：

运行命令前先创建 `<repo-root>/UnityStarter/TestResults`，或者把两个输出路径替换为已存在且可写的目录。

~~~powershell
<unity-editor> -batchmode -nographics -projectPath "<repo-root>/UnityStarter" -runTests -testPlatform EditMode -assemblyNames "CycloneGames.GameplayFramework.Core.Tests.Editor;CycloneGames.GameplayFramework.Tests.Editor" -testResults "<repo-root>/UnityStarter/TestResults/GameplayFramework.EditMode.xml" -logFile "<repo-root>/UnityStarter/TestResults/GameplayFramework.EditMode.log"
~~~

### PlayMode 测试

PlayMode assembly 验证 auto-start Host 会创建 Playing World、创建独立 early/late driver、按 configured framework phase 包围普通 MonoBehaviour 的 Update/LateUpdate callback、转发 Update/FixedUpdate/LateUpdate Actor Tick phase、Host disabled 时停止转发、externally destroyed World-owned Actor 只释放一次，并随 Host GameObject dispose World。

~~~text
Window > General > Test Runner > PlayMode
Assembly: CycloneGames.GameplayFramework.Tests.PlayMode
~~~

### Editor 手动 Smoke Test

1. Reimport 或 reload 项目，确认 Runtime、Editor、sample 和 test assembly 编译。
2. 打开 PureUnity sample scene。
3. 确认 GameplayWorldHost 引用了 UnitySampleWorldSettings。
4. 保持在 Edit Mode，将 `UnitySampleRotatingActor` 添加到一个 scene GameObject，并保存 scene。
5. 进入 Play Mode。
6. 验证 World 为 Playing，且本地 Controller 拥有 PlayerState 和 Pawn。
7. 验证本地 CameraManager 拥有 configured `UnityCameraOutput`、向目标 Camera 应用 pose/FOV，并在 stop 时释放 output。
8. 确认 sample Actor 仅在 World 为 Playing 时旋转。
9. 打开 World Debugger，检查 World、configured Actor admission limit、allocated capacity、peak/rejection counter、各 phase Tick count 和 Actor registration。
10. 在 sample Actor Inspector 中点击 `Disable Runtime Tick`，确认旋转停止、Tick Enabled 诊断发生变化且 Actor 仍保持注册；随后点击 `Enable Runtime Tick` 恢复旋转。
11. 运行 Project Validation，并确认 sample 没有配置错误。
12. 打开 Camera Debug Window，观察 pose/blend 数据。
13. 退出 Play Mode，确认没有遗留参与者、Tick 或 camera-mode 状态。

产品包含 Cinemachine 时，使用 `CinemachineCameraOutput` 重复 camera 检查：确认 manual update 仅在 ownership 持有期间生效，并确认 stop 后恢复 brain update mode 与 Follow/LookAt target。

### Player 与平台验证

对每个发布目标：

1. 在 Build Settings 中加入项目 Runtime composition root 和必需 scene。
2. 执行 clean Player build。
3. 覆盖启动、cancellation、登录失败、logout、travel 和 application shutdown。
4. 测试 direct 和 external WorldSettings reference。
5. 在目标硬件上分析 camera、Actor 和 roster hot path。
6. 验证 IL2CPP/AOT serializer 行为和 managed stripping。
7. Server target 应在没有 LocalPlayer 的条件下运行，并检查依赖、日志和关闭。
8. 按受支持的产品矩阵，在所选 Cinemachine、Factory、AssetManagement、GameplayAbilities、GameplayTags、Networking 与 Navigathena integration 存在和缺失时分别构建。

EditMode 测试和源码检查不能证明 Player、IL2CPP、headless 或目标平台验证通过。

## 故障排查

| 现象 | 检查项 |
| --- | --- |
| “A world is already active” | 在 `StartWorldAsync` 前调用并等待 `StopWorldAsync` |
| Owner-thread exception | 在修改 GameInstance、World、login、spawn 或 possession 前 marshal 到 Unity main thread |
| WorldSettings 校验失败 | 配置 GameMode、PlayerController、Pawn 和 PlayerState |
| 外部引用没有 resolver | 向 GameInstance 传入 resolver，并确认 `Supports` 对所选 source 返回 true |
| 外部加载在 cancellation 后失败 | 传播 cancellation 并 dispose loader handle |
| Client World 没有 GameMode | Client mode 是非权威端；通过 network adapter 填充客户端可见状态 |
| Dedicated server 没有本地 Controller | 使用远程 `LoginAsync`；自动本地登录已禁用 |
| 登录返回 InvalidRequest | 检查 ID 和 name/address/options 边界、`IsLocal` 以及 GameInstance 中的同一个 LocalPlayer slot |
| 登录返回 Rejected | 检查 session 内 PlayerId 唯一性和产品 admission policy |
| 登录返回 AtCapacity | 检查 GameSession player/spectator capacity 和 count |
| 登录返回 SpawnFailed | 检查 prefab reference、Actor-lifetime result、World state 和自定义初始化 callback |
| Player spawn point 不稳定 | 重写 `ChoosePlayerStart`，或传入精确 portal name |
| Possession 失败 | 注册并初始化 Controller、使用同一 World，并避免 reentrant callback |
| Movement input 没有效果 | Movement adapter 必须消费 pending vector 并应用它 |
| Actor Tick 不执行 | 确认 phase 不是 None、Runtime Tick 已启用、component active/enabled、BeginPlay 已完成、registration 不是 deferred，且 World 为 Playing |
| Actor Tick 报告重入异常 | 不要从 Actor Tick callback 调用 GameInstance.Tick 或 World.Tick；把工作推迟到下一个 owned loop phase |
| 直接组合 GameInstance 时 Actor 从不 Tick | 由 composition root 对每个必需 phase 精确转发一次；GameplayWorldHost 会自动提供该转发 |
| Movement 或 Ability 使用不同 update model | 保留模块自己的 MonoBehaviour 或显式 simulation clock；Actor Tick 是 opt-in，不替代 package-owned scheduling |
| Scene Actor 在 World barrier 外 BeginPlay | 让 composition root 早于普通 Actor Start callback 启动 |
| Ended Actor 无法加入另一个 World | non-owned scene/external Actor 必须先解绑再注册到 replacement World；World-owned Actor 不能重入 |
| GameState transition 非法 | 遵循合法 transition table，或处理 `TrySetMatchState` failure |
| 没有 CameraManager | 配置可选 prefab，并使用本地 PlayerController |
| CameraManager 没有输出 | 分配 `CameraOutputBehaviour`、调用 `SetCameraOutput`，或者明确选择只计算 pose 而不提供 presentation output |
| Camera output ownership error | 确保一个 backend resource 只由一个 CameraManager 拥有；对 Cinemachine，该 resource 是 brain |
| Cinemachine integration 不编译 | 确认已解析 `[3.0.0,4.0.0)` 范围内的 `com.unity.cinemachine`，并从匹配的 integration asmdef 引用 gated integration assembly |
| Host 依赖需要来自 DI | 构造 `GameplayWorldComposition` 并在 startup 前调用 `Configure`，不要继承 sealed Host |
| Actor admission 返回 false | 检查 configured `WorldRuntimeLimits.MaximumActorCount`、allocated capacity、peak count 与 rejected admission count，并按产品 policy 释放或延迟工作 |
| Camera mode push 返回 false | 检查重复实例、clearing/evaluation 状态和 CameraContext capacity |
| Camera clear 没有立即执行 | Evaluation 期间请求的 clear 会在 evaluation scope 结束后执行 |
| Camera action 返回 false | 检查 action key、preset、active-action limit、Controller resolution 和 mode-stack capacity |
| Animator progress action 未触发 | 检查 progress key、threshold、transition flag、loop policy 和 8-pair tracking capacity |
| Snapshot restore 失败 | 校验非负 ID、player-name 长度和已注册 identity/spectator lock；调用 Runtime 前先校验 persistence 或 protocol envelope |
| 产品代码无法访问 Core 类型 | 将 `CycloneGames.GameplayFramework.Core` 直接添加到 consumer asmdef |
| 纯规则 assembly 意外依赖 UnityEngine | 移除 GameplayFramework Runtime 与 Unity adapter reference，直接引用 Core |
| Networking protocol 可以编译但缺少 Actor adapter | 将 `CycloneGames.GameplayFramework.Networking.Runtime` 添加到 Unity replication asmdef |
| Travel 报告没有 handler | 在 GameInstance 中组合 `ISceneTransitionHandler` |
| Player build 中缺少 sample script | 确认 sample asmdef 包含目标平台，并在构建前解决全部编译错误 |
