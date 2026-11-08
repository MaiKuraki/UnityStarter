![Unity Project Starter](<https://capsule-render.vercel.app/api?type=waving&height=220&color=gradient&text=Unity%20Project%20Starter&section=header&reversal=false&textBg=false&desc=GameplayFramework%20│%20GameplayAbility(GAS)%20│%20UIFramework%20│%20HotUpdate%20Ready%20(HybridCLR)%20│%20CI/CD%20Ready&descAlign=50&descAlignY=58&descSize=16&fontAlignY=30&fontSize=72>)

一套模块化的 Unity 工程，附带可复用的框架层，架构对标虚幻引擎。`GameplayFramework` 沿用 `Actor`、`Pawn`、`Controller`、`GameMode` 的拆分；`GameplayAbilities` 与 `GameplayTags` 提供这些契约所依赖的能力与标签基础。

面向长期维护同一套 Unity 代码的中大型项目。具体特性：

- 每个模块是独立的 UPM 包，带自己的程序集定义；项目只引用用得到的包，未使用的模块不进入编译与构建。
- 能保持引擎无关的逻辑就保持引擎无关，因此可以跑在 EditMode 测试、无头进程与 CLI 工具里。
- 模块边界由测试守住（引擎接缝、包图），不只依赖约定。
- 构建管线、analyzer 与维护工具都在仓库内，随代码一起版本化。

[English](README.md) | [简体中文](README.SCH.md)

> [!NOTE]
> 如果你觉得这个项目对你有帮助，请点一个 Star ⭐，谢谢！

![Unity](https://img.shields.io/badge/Unity-2022.3%20LTS-black?logo=unity)
![Unity6](https://img.shields.io/badge/Unity-6000.x%20Compatible-black?logo=unity)
![License](https://img.shields.io/badge/License-MIT-green.svg)
[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/MaiKuraki/UnityStarter)

## 目录

- [目录](#目录)
- [如何使用这个仓库](#如何使用这个仓库)
- [项目内容概览](#项目内容概览)
- [架构原则](#架构原则)
  - [项目所有权模型](#项目所有权模型)
- [仓库结构](#仓库结构)
- [模块地图](#模块地图)
  - [Gameplay](#gameplay)
  - [AI](#ai)
  - [Data、Assets And Content](#dataassets-and-content)
  - [Runtime Infrastructure](#runtime-infrastructure)
  - [Build、Tools And Quality](#buildtools-and-quality)
- [Networking 状态](#networking-状态)
- [Build、CI/CD 与项目工具](#buildcicd-与项目工具)
  - [Build 是项目自有基础设施](#build-是项目自有基础设施)
  - [派生项目工具](#派生项目工具)
- [快速开始](#快速开始)
  - [环境要求](#环境要求)
  - [首次运行](#首次运行)
  - [单独使用模块](#单独使用模块)
- [技术栈](#技术栈)
- [文档入口](#文档入口)
- [验证状态](#验证状态)
- [相关项目](#相关项目)

## 如何使用这个仓库

UnityStarter 提供玩法代码出现之前就需要的结构：资源所有权、拆分清楚的 gameplay 架构、data-driven content、模块边界、build automation、analyzers 与维护工具。

这个仓库可以用两种方式使用：

- **作为项目模板**：在 Unity 中打开 `UnityStarter/`，用内置工具改名，让项目自有的 `Assets/Build/` 层随着你的游戏继续演进。
- **以 Package（UPM）形式**（推荐）：把 `CycloneGames` 目录下的包移到项目外，再用 Package Manager 引入。各包相互独立，项目只引入自己引用的部分，依赖由各包的 `package.json` 声明。

## 项目内容概览

UnityStarter 由可复用的 `CycloneGames` 框架层、Unity 项目模板、项目自有 Build/CI 模块、独立维护工具、中英文文档，以及面向验证和工程规范的 analyzer 支持组成。

[模块地图](#模块地图) 是 gameplay、content、UI/input、AI、runtime infrastructure、build tooling 与 experimental networking packages 的主要入口。

| 项目 | 详细信息 |
| --- | --- |
| Unity 项目根目录 | `UnityStarter/` |
| Unity 版本来源 | `UnityStarter/ProjectSettings/ProjectVersion.txt` |
| CycloneGames 模块目录 | `UnityStarter/Assets/ThirdParty/CycloneGames/` |
| Assembly definitions | Package 与项目 assembly boundary 由 `UnityStarter/Assets/` 下的 `.asmdef` 文件声明。 |
| Analyzer 规则 | 20+ 条已实现的 `CycloneGames.Analyzers` 规则，由 `Assets/Default.ruleset` 项目级分阶段强制（Unity 会自动应用 Assets 根下这个名字的 ruleset） |
| 独立工具 | `Tools/Executable/Windows/` 下的 Go 工具 Windows 可执行文件 |

## 架构原则

- **关键逻辑优先纯 C#**：当逻辑需要在 CLI、EditMode、headless simulation 或未来 adapter 中测试时，核心契约避免泄露 `UnityEngine` 类型。
- **Unity 负责集成与表现**：`MonoBehaviour`、`ScriptableObject`、Editor tools、scene bindings 和 assets 负责桥接 runtime systems，而不是承载复杂领域规则。
- **可选集成物理隔离**：DI containers、tween engines、scene navigation、serializers、transports 和 hot-update backends 都放在独立 integration assemblies 后面。
- **性能是设计约束**：热路径以 zero-GC 或 low-GC 为目标，强调可预测所有权、可复用 buffers 和明确 lifecycle cleanup。
- **Build 是项目自有层**：`Assets/Build/` 应随派生项目一起演进，根据产品需求调整。
- **文档是 API 的一部分**：长期维护模块应同步维护 `README.md` 和 `README.SCH.md`。

### 项目所有权模型

这张图用于说明仓库所有权与模块职责，不表示运行时依赖顺序。它的重点是：哪些部分会随派生项目持续维护，哪些部分属于可复用框架模块，哪些部分是可选或实验性集成。

```mermaid
flowchart TD
  subgraph ProjectOwned["项目自有模板层"]
    StarterAssets["Assets/UnityStarter/\nScenes、项目资源、组合入口"]
    BuildLayer["Assets/Build/\nBuild 与 CI 入口"]
    ProjectTools["Tools/\n改名、清理、维护工具"]
  end

  subgraph FrameworkModules["可复用 CycloneGames 模块"]
    Gameplay["Gameplay\nGameplayFramework、Abilities、Tags、RPGFoundation"]
    Content["Content\nAssetManagement、DataTable、Localization、Audio"]
    Presentation["Presentation/Input\nUIFramework、InputSystem、DeviceFeedback"]
    AI["AI\nBehaviorTree、AIPerception"]
    Infrastructure["Runtime Infrastructure\nFactory、Logger、DeterministicMath、Hash、IO"]
  end

  subgraph OptionalIntegrations["可选 / 实验性集成"]
    Networking["Networking packages\n端到端验证前保持实验性"]
    HotUpdate["Hot-update 构建钩子\n安装 HybridCLR、YooAsset、Addressables 后启用"]
  end

  ProjectTools --> StarterAssets
  ProjectTools --> BuildLayer
  StarterAssets --> Gameplay
  StarterAssets --> Content
  StarterAssets --> Presentation
  StarterAssets --> AI
  Gameplay --> Infrastructure
  Content --> Infrastructure
  Presentation --> Content
  AI --> Infrastructure
  BuildLayer -. 检测 / 调用 .-> HotUpdate
  Gameplay -. 可选桥接 .-> Networking
  AI -. 可选桥接 .-> Networking

  class StarterAssets,BuildLayer,ProjectTools projectNode
  class Gameplay,Content,Presentation,AI frameworkNode
  class Infrastructure infraNode
  class Networking,HotUpdate optionalNode

  classDef projectNode fill:#E6F4FF,stroke:#6B9BC3,color:#263238,stroke-width:1px
  classDef frameworkNode fill:#FFF1D9,stroke:#C99745,color:#263238,stroke-width:1px
  classDef infraNode fill:#F4F4F5,stroke:#9CA3AF,color:#263238,stroke-width:1px
  classDef optionalNode fill:#E8F8F5,stroke:#67A69A,color:#263238,stroke-width:1px,stroke-dasharray: 5 4
```

## 仓库结构

```text
<repo-root>/
  README.md / README.SCH.md              # 根目录中英文总览
  Docs/                                  # 跨模块指南
  Tools/                                 # 独立维护工具
  UnityStarter/                          # Unity 项目根目录
    Analyzers/CycloneGames.Analyzers/    # Roslyn analyzer 项目
    Assets/Build/                        # 项目自有 Build/CI 模块
    Assets/ThirdParty/CycloneGames/      # 可复用 CycloneGames 框架模块
    Assets/UnityStarter/                 # 模板项目 scenes 与 game-side assets
    Packages/                            # Unity package manifest 与 lock file
    ProjectSettings/                     # Unity settings，包含版本来源
```

## 模块地图

建议先看 `GameplayFramework`、`AssetManagement`、`GameplayAbilities`、`GameplayTags`、`DataTable` 和 `Build`。

### Gameplay

| 模块 | 职责 | 文档 |
| --- | --- | --- |
| **GameplayFramework** | Actor/Pawn/Controller/GameMode 结构、gameplay lifecycle、camera flow 与 scene-flow foundation。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayFramework/README.SCH.md) |
| **GameplayFramework.AssetManagement** | 通过 AssetManagement 解析 `WorldSettings` 引用，句柄所有权显式。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayFramework.AssetManagement/README.SCH.md) |
| **GameplayFramework.Factory** | 按 Factory 生命周模型创建与释放 Actor。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayFramework.Factory/README.SCH.md) |
| **GameplayFramework.GameplayAbilities** | 从框架 Actor 暴露能力系统并初始化 actor information。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayFramework.GameplayAbilities/README.SCH.md) |
| **GameplayFramework.GameplayTags** | 解析与修改挂在框架 Actor 上的 tag 容器。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayFramework.GameplayTags/README.SCH.md) |
| **GameplayAbilities** | GAS 风格 data-driven ability、attribute、effect、cost、cooldown 与 cue system，具有显式 authority/replica role 与权威 `AuthorityOnly` execution boundary。可选 Networking integration 提供 authority activation protocol building block。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayAbilities/README.SCH.md) |
| **Choreography** | 引擎无关的 action presentation scheduling，用于 animation、audio、VFX、gameplay-event markers 与 preload coordination。 | [目录](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Choreography/) |
| **GameplayTags** | 层级 tags、generated constants、query helpers、editor tooling 与 integration points。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayTags/README.SCH.md) |
| **RPGFoundation** | RPG movement 与 interaction foundations，可与其他 gameplay packages 集成。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.RPGFoundation/README.SCH.md) |
| **RPGFoundation.Interaction.GameplayFramework** | RPG interaction 的框架 Actor 绑定。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.RPGFoundation.Interaction.GameplayFramework/README.SCH.md) |
| **RPGFoundation.Interaction.DeterministicMath** | RPG interaction 的定点数权威集成。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.RPGFoundation.Interaction.DeterministicMath/README.SCH.md) |
| **RPGFoundation.Interaction.DeterministicMath.GameplayFramework** | 确定性 interaction 权威状态的框架 Actor 桥接。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.RPGFoundation.Interaction.DeterministicMath.GameplayFramework/README.SCH.md) |
| **UIFramework** | Window management、UI flow、presentation patterns，以及委托给 `AssetManagement` segmented-LRU (SLRU) cache 的 asset-backed UI loading。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.UIFramework/README.SCH.md) |
| **UIFramework.Localization** | 把 Localization 绑定到 UI 窗口，附带可选的 TextMeshPro 分片用于按语言保存版式快照。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.UIFramework.Localization/README.SCH.md) |
| **Foundation2D** | 面向派生项目的 2D foundation package 与 samples。 | [目录](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Foundation2D/) |

### AI

| 模块 | 职责 | 文档 |
| --- | --- | --- |
| **BehaviorTree** | Behavior tree runtime、图编辑器、测试，以及面向同构 agent 的 data-oriented Burst 路径。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.BehaviorTree/README.SCH.md) |
| **BehaviorTree.Spatial** | 可选桥接：把最近邻查询结果写入黑板槽位。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.BehaviorTree.Spatial/README.SCH.md) |
| **BehaviorTree.Perception** | 可选桥接：把传感器最近目标写入黑板，并提供多传感器聚合器。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.BehaviorTree.Perception/README.SCH.md) |
| **BehaviorTree.EnvQuery** | 可选桥接：执行环境查询并发布最优候选。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.BehaviorTree.EnvQuery/README.SCH.md) |
| **AIPerception** | 基于 Jobs 与 Burst 的感知：传感器、记忆、变化事件与阵营态度。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.AIPerception/README.SCH.md) |
| **AIEnvQuery** | 候选生成与加权评分，用于位置选择；核心不依赖引擎。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.AIEnvQuery/README.SCH.md) |
| **AIResources** | 同一时刻只允许一个持有者的资源占用表，支持优先级抢占与租约过期。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.AIResources/README.SCH.md) |

### Data、Assets And Content

| 模块 | 职责 | 文档 |
| --- | --- | --- |
| **AssetManagement** | Interface-first asset loading abstraction，包含 bounded segmented-LRU (SLRU) caching、`CacheRetention` policies/scheduler、provider abstraction、diagnostics 和 async loading flows。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.AssetManagement/README.SCH.md) |
| **AtlasPipeline** | Sprite 导入与 SpriteAtlas V2 生成，带增量索引与构建期校验。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.AtlasPipeline/README.SCH.md) |
| **DataTable** | 面向策划配置的数据管线，支持可选 Luban、MessagePack 与 asset-management bridges。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.DataTable/README.SCH.md) |
| **GameplayTags.DataTable** | GameplayTags authoring 与 loading 的 DataTable integration。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayTags.DataTable/README.SCH.md) |
| **Choreography.AssetManagement** | `CycloneGames.AssetManagement` 的可选 Choreography resource provider bridge。 | [目录](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Choreography.AssetManagement/) |
| **Choreography.CycloneAudio** | `CycloneGames.Audio` 的可选 Choreography audio provider bridge。 | [目录](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Choreography.CycloneAudio/) |
| **Localization** | String tables、locale fallback、asset variants 与 hot-reload-oriented loading。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Localization/README.SCH.md) |
| **Audio** | Audio management layer，包含 async loading、runtime ownership 与 platform-aware policies。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Audio/README.SCH.md) |
| **Audio.Localization** | 让音频语音语言与 Localization 的语言设置保持一致。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Audio.Localization/README.SCH.md) |
| **FontAssets** | CJK、Latin、symbols 与 number font assets。 | [目录](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.FontAssets/) |

### Runtime Infrastructure

| 模块 | 职责 | 文档 |
| --- | --- | --- |
| **Factory** | Factory 与 object pooling module，支持 DI-friendly usage 和 ECS/DOD variants。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Factory/README.SCH.md) |
| **Logging** | Unity-free logging contract，提供 channel、severity、process writer 路由与安全的 null writer。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Logging/README.SCH.md) |
| **Logging Pipeline** | Unity-free bounded processing，提供 filtering、sinks、file output、statistics 与确定性 shutdown。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Logging.Pipeline/README.SCH.md) |
| **Logging Unity** | Logging pipeline 的可选 Unity lifecycle、settings、Console sink、Editor tooling 与 build integration。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Logging.Unity/README.SCH.md) |
| **DeterministicMath** | Fixed-point deterministic math，用于 replay、simulation 与 lockstep-friendly systems。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.DeterministicMath/README.SCH.md) |
| **Hash** | Deterministic hashing primitives，用于 manifests、protocol checks、IDs 与 consistency。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Hash/README.SCH.md) |
| **IO** | 面向 Unity-aware foundation modules 的 managed file and path utilities。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.IO/README.SCH.md) |
| **Persistence** | 无 Unity 依赖、有界、版本化的单记录 orchestration，提供严格 Record V1 完整性检查，以及与 serializer/storage 无关的 contract。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Persistence/README.SCH.md) |
| **Persistence.SystemIO** | 可选 System.IO storage 与 Unity `persistentDataPath` composition，提供有界读取和原子 commit 行为。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Persistence.SystemIO/README.SCH.md) |
| **Persistence.VYaml** | 可选 generated-resolver VYaml codec，用于可读 UTF-8 persistence payload。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Persistence.VYaml/README.SCH.md) |
| **Persistence.MessagePack** | 可选、受 asmdef gate 控制的 MessagePack codec source；只有安装锁定 binary、analyzer 与 Unity bridge 后才会参与编译。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Persistence.MessagePack/README.SCH.md) |
| **InputSystem** | 提供经过验证的 YAML 输入 authoring、带优先级的 mapping context、每玩家设备所有权、本地多人、binding profile、Editor tooling 与 opt-in integration。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.InputSystem/README.SCH.md) |
| **InputSystem.AssetManagement** | InputSystem、AssetManagement 与 VContainer 之间的可选物理 package-loading bridge。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.InputSystem.AssetManagement/README.SCH.md) |
| **DeviceFeedback** | Haptics、vibration、rumble 与 device-light feedback abstractions。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.DeviceFeedback/README.SCH.md) |
| **Settings** | 无 Unity 依赖、带 clone 的 settings state，提供默认值、validation、forward migration、隔离 snapshot 与强类型 commit 后通知。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Settings/README.SCH.md) |
| **Settings.Persistence** | 可选 integration，在不耦合两个 Core 的前提下组合 Settings state/migration 与一条 Persistence Store。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Settings.Persistence/README.SCH.md) |
| **Utility** | Common Unity utility components and helpers。 | [目录](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Utility/) |
| **Cheat** | Build-gated internal cheat command system，集成 VitalRouter。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Cheat/README.SCH.md) |
| **EventBus** | 零分配事件总线，带池化订阅句柄、有界跨线程队列，以及可选的 R3、UniTask、VitalRouter 与 Logging 适配。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.EventBus/README.SCH.md) |
| **Scheduling** | 有界协作式工作调度，带通道优先级、软帧预算、故障隔离与确定性 shutdown。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Scheduling/README.SCH.md) |
| **Spatial** | 均匀空间哈希网格用于宽相查询，带句柄生命周期、SoA 存储与单写线程模型。 | [README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Spatial/README.SCH.md) |

### Build、Tools And Quality

| 区域 | 职责 | 文档 |
| --- | --- | --- |
| **Build** | 项目自有 player build pipeline、version info、可选 hot-update hooks 与 CI-facing methods。 | [README.SCH](UnityStarter/Assets/Build/README.SCH.md) |
| **Tools** | 用于项目改名、package trimming、cleanup、file trees 与 asset processing 的 Go tools。 | [README.SCH](Tools/README.SCH.md) |
| **Analyzers** | 面向 Unity performance、safety、async 与 conventions 的 Roslyn analyzer rules。项目级 severity 策略位于 `Assets/Default.ruleset`。 | [README.SCH](UnityStarter/Analyzers/CycloneGames.Analyzers/README.SCH.md) |

## Networking 状态

Networking layer 是 experimental foundation。生产接入必须使用选定的 transport、serializer、authority model、reconnect flow、目标平台和 gameplay replication policy 完成端到端验证。

| 模块 | 职责 | 状态 |
| --- | --- | --- |
| **Networking** | Transport-neutral contracts、message catalogs、protocol manifests、sessions、replication、security、serializers、adapters 与 diagnostics。 | 实验性。[README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.Networking/README.SCH.md) |
| **GameplayFramework.Networking** | Session bridge、actor migration serialization、authority roles 与 observer resolution。 | 实验性。[README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayFramework.Networking/README.SCH.md) |
| **GameplayAbilities.Networking** | GameplayAbilities 的 command、prediction、state replication、cue 与 endpoint 契约。 | 实验性。[README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayAbilities.Networking/README.SCH.md) |
| **AIPerception.Networking** | Perception event、snapshot、memory、authority 与 host-migration contracts。 | 实验性。[README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.AIPerception.Networking/README.SCH.md) |
| **BehaviorTree.Networking** | Behavior tree replication profiles、authority helpers、snapshots 与 blackboard deltas。 | 实验性。[README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.BehaviorTree.Networking/README.SCH.md) |
| **RPGFoundation.Movement.Networking** | Movement input、snapshot、correction、teleport、authority transfer、validation、history 与 reconciliation contracts。 | 实验性。[README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.RPGFoundation.Movement.Networking/README.SCH.md) |
| **RPGFoundation.Interaction.Networking** | Interaction DTOs、vector conversion、authority validation bridge 与 message catalog registration。 | 实验性。[README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.RPGFoundation.Interaction.Networking/README.SCH.md) |
| **RPGFoundation.Projectile.Networking** | Projectile protocol metadata、DTOs、validation helpers、prediction reconciliation、snapshot history 与 authority bridge contracts。 | 实验性。[README.SCH](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.RPGFoundation.Projectile.Networking/README.SCH.md) |

## Build、CI/CD 与项目工具

### Build 是项目自有基础设施

`UnityStarter/Assets/Build/` 是项目自有基础设施。产品项目可以在其中调整 scenes、version prefixes、output layout、hot-update assembly lists、platform signing 和 release rules。

当开发者从 UnityStarter 派生新游戏并运行 `rename_project` 后，Build layer 会保留在新项目中，并应继续由该项目维护。

Build 模块包含：

- `BuildData` ScriptableObject 配置。
- 通过 `Build.VersionControl.Editor` 获取 Git 或 Perforce revision metadata；batch mode 和 release 构建必须具备其中之一，只有交互式 Development 构建可以使用显式 `LocalDevelopment` fallback。
- Editor menu items 与 command-line player build entry points。
- HybridCLR、Obfuz、YooAsset 3 和 Addressables 可选集成；缺少可选包不会破坏核心构建程序集。
- 面向内部构建的 Cheat define control。
- 唯一 CI 入口：`Build.Pipeline.Editor.BuildEntryPoints.RunCommandLine`。

最小命令形态：

```bash
Unity -batchmode -quit -projectPath UnityStarter \
  -executeMethod Build.Pipeline.Editor.BuildEntryPoints.RunCommandLine \
  -buildTarget Win64 \
  -pipelineProfile Assets/UnityStarter/Editor/Build/BuildData.asset \
  -pipelineOutput Build/Windows/Release/UnityStarter.exe \
  -pipelineClean
```

[Build README](UnityStarter/Assets/Build/README.SCH.md) 包含更详细的配置说明、hot-update workflows 和 CI examples。

### 派生项目工具

`Tools/` 目录包含独立 Go 工具：

| 工具 | 用途 |
| --- | --- |
| `rename_project` | 安全、可重复地重命名从 UnityStarter 派生的项目。 |
| `remove_unity_packages` | 从 `manifest.json` 移除不需要的 packages。 |
| `unity_project_full_clean` | 清理 Unity caches、generated projects 和 build artifacts。 |
| `audio_volume_normalizer` | 按类别目标标准化 audio loudness。 |
| `texture_channel_packer` | 为 mask maps 等工作流打包 texture channels。 |
| `unity_video_webm_converter` | 将视频转换为 Unity-friendly VP8 WebM。 |
| `generate_file_tree` | 生成用于文档的 Markdown directory trees。 |

详见 [Tools README](Tools/README.SCH.md)。

## 快速开始

### 环境要求

- `UnityStarter/ProjectSettings/ProjectVersion.txt` 中记录的 Unity 版本。
- batch mode 和 release 构建必须使用 Git 或 Perforce 生成自动版本元数据；只有交互式 Development 构建可以使用确定性的 `LocalDevelopment` 回退元数据。

### 首次运行

```bash
git clone https://github.com/MaiKuraki/UnityStarter.git
```

1. 在 Unity Hub 中打开 `UnityStarter/`。
2. 打开 `UnityStarter/Assets/UnityStarter/Scenes/Scene_Launch.unity`。
3. 阅读 [GameplayFramework](UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayFramework/README.SCH.md)，理解高层架构。
4. 修改 build settings 或 CI methods 前，先阅读 [Build](UnityStarter/Assets/Build/README.SCH.md)。
5. 如果你要从该模板创建新项目，运行 `Tools/Executable/Windows/rename_project.exe`，并阅读 [Tools README](Tools/README.SCH.md)。

### 单独使用模块

从 `UnityStarter/Assets/ThirdParty/CycloneGames/` 复制模块目录到你的项目，然后检查该模块的 `package.json`、`.asmdef`、README、dependencies 和可选 `Integrations/` 文件夹。不需要全量引入：项目只取自己调用的包，以及这些包声明的依赖。有些模块相对自包含，另一些模块依赖共享 CycloneGames packages 或 Unity packages。

## 技术栈

具体版本应以 `UnityStarter/Packages/manifest.json`、`UnityStarter/Packages/packages-lock.json` 和 `UnityStarter/Packages/nuget-packages/InstalledPackages/` 为准。

| 领域 | 示例 |
| --- | --- |
| Async and reactive | `com.cysharp.unitask`、`com.cysharp.r3`、NuGet `R3` |
| Routing and data | `jp.hadashikick.vitalrouter.unity`、`jp.hadashikick.vyaml`、NuGet `VitalRouter`、NuGet `VYaml` |
| Unity performance stack | Burst、Collections、Mathematics、Profiling Core、Memory Profiler、Profile Analyzer |
| Unity gameplay stack | Input System、Cinemachine、URP、TextMeshPro、UGUI、Splines |
| UI and debug helpers | SoftMask、UIEffect、CompositeCanvasRenderer、UnityDebugSheet、InGameDebugConsole、uPalette |
| Build and analysis | Scriptable Build Pipeline、NuGetForUnity、CycloneGames analyzers 使用的 Roslyn packages |
| Optional integrations | VContainer、PrimeTween、Navigathena、Luban、MessagePack、HybridCLR、YooAsset、Addressables、Obfuz、Mirror、Mirage |

## 文档入口

| 位置 | 用途 |
| --- | --- |
| `UnityStarter/Assets/ThirdParty/CycloneGames/*/README.SCH.md` | 长期维护 packages 的模块级文档。 |
| [`UnityStarter/Assets/Build/README.SCH.md`](UnityStarter/Assets/Build/README.SCH.md) | Build pipeline、hot update、optional packages 与 CI。 |
| [`UnityStarter/Analyzers/CycloneGames.Analyzers/README.SCH.md`](UnityStarter/Analyzers/CycloneGames.Analyzers/README.SCH.md) | Analyzer rules、build instructions 与 activation guidance。 |
| [`Tools/README.SCH.md`](Tools/README.SCH.md) | 独立项目维护工具。 |
| [`Docs/AudioBestPractices/AudioBestPractices.SCH.md`](Docs/AudioBestPractices/AudioBestPractices.SCH.md) | Audio import 与 runtime audio guidance。 |
| [DeepWiki](https://deepwiki.com/MaiKuraki/UnityStarter) | 生成式 codebase overview。 |

## 验证状态

验证通过 Unity 与工程内的测试运行器完成。

- 打开 `UnityStarter/`，确认导入后 Console 没有错误。
- 对改动过的模块运行对应的 EditMode 套件。完整套件可在 Test Runner 中执行，也可用批处理：

```bash
Unity -batchmode -nographics -projectPath UnityStarter -runTests \
  -testPlatform EditMode -testFilter CycloneGames -testResults results.xml -logFile test.log
```

- 两个守卫套件保护仓库级不变量，应保持通过：
  - `CoreSeamGuardTests` 检查引擎无关的 Core 程序集不引用 Unity 类型、不做磁盘 IO、不使用宿主编译符号。
  - `PackageReferenceGuardTests` 检查每一处跨包程序集引用，要么在 `package.json` 中声明，要么由能力符号门控。
- 使用 `dotnet build UnityStarter/Analyzers/CycloneGames.Analyzers/CycloneGames.Analyzers.csproj -c Release` 构建 analyzer project。
- 修改 BuildData 或 CI settings 前，使用 `Build > Pipeline > Print Selected Profile`。
- Networking 模块仍属实验性；生产使用需要对选定的 transport、serializer 与 authority model 做端到端验证。

## 相关项目

- **[Rhythm Pulse](https://github.com/MaiKuraki/RhythmPulse)** - Rhythm game mechanics collection
- **[Unity GAS Sample](https://github.com/MaiKuraki/UnityGameplayAbilitySystemSample)** - GAS demonstration project

---

**许可证**: [MIT](LICENSE)
