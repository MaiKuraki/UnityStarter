# CycloneGames.RPGFoundation

CycloneGames.RPGFoundation 是可复用的 RPG foundation package。包结构优先按功能模块组织，然后在每个模块内按运行职责分层。

## 模块布局

长期维护的模块使用以下布局：

```text
<Module>/
  README.md
  README.SCH.md
  Core/
  Runtime/
  Editor/
  Tests/
```

`Core/` 放置不依赖 Unity 的契约、值对象、校验逻辑、确定性数据，以及可在 server、headless、CLI 和 Unity 测试环境运行的服务。可行时 Core assembly 使用 `noEngineReferences`。

`Runtime/` 放置 Unity-facing component、ScriptableObject authoring bridge、runtime adapter，以及默认 Unity 实现。

`Editor/` 放置 inspector、window、validator、drawer 和 authoring tool。

`Tests/` 放置该模块的 EditMode 和 PlayMode 测试。

当前包使用该结构组织：

| 模块 | 用途 |
| --- | --- |
| `Interaction/` | 交互契约、本地运行时组件、权威校验、确定性/网络桥接、Inspector 和测试 |
| `Movement/` | 移动核心契约、2D/3D Unity 移动组件、寻路 adapter、动画 adapter 和 Inspector |

## 程序集布局

当前包已经拆成模块程序集，而不是把所有源码放入一个宽泛的 runtime assembly：

| Assembly | 职责 |
| --- | --- |
| `CycloneGames.RPGFoundation.Interaction.Core` | 不依赖 Unity 的交互契约、值对象、校验、限流和权威服务 |
| `CycloneGames.RPGFoundation.Interaction.Runtime` | Unity-facing 交互组件和运行时服务 |
| `CycloneGames.RPGFoundation.Interaction.Editor` | 交互 Inspector、validator 和 Editor 工具 |
| `CycloneGames.RPGFoundation.Interaction.Tests.Editor` | 交互 EditMode 测试 |
| `CycloneGames.RPGFoundation.Movement.Core` | 不依赖 Unity 的移动契约、属性、状态标识、快照和 helper 类型 |
| `CycloneGames.RPGFoundation.Movement.Runtime` | Unity-facing 2D/3D 移动组件、ScriptableObject 配置、动画抽象和寻路抽象 |
| `CycloneGames.RPGFoundation.Movement.Editor` | 移动 Inspector 和 authoring 校验 |

旧程序集 `CycloneGames.RPGFoundation.Runtime`、`CycloneGames.RPGFoundation.Editor` 和 `CycloneGames.RPGFoundation.Tests.Editor` 会作为轻量兼容壳保留。新代码应直接引用模块程序集。

## 可选集成

可选集成被隔离到独立 assembly 中，这样基础包在未安装可选包时也能正常编译。

| Integration Assembly | 依赖 |
| --- | --- |
| `CycloneGames.RPGFoundation.Interaction.Integrations.Networking` | `CycloneGames.Networking.Core` |
| `CycloneGames.RPGFoundation.Interaction.Integrations.DeterministicMath` | `CycloneGames.DeterministicMath.Core` |
| `CycloneGames.RPGFoundation.Interaction.Integrations.GameplayFramework` | `CycloneGames.GameplayFramework.Runtime` |
| `CycloneGames.RPGFoundation.Interaction.Integrations.DeterministicMath.GameplayFramework` | DeterministicMath + GameplayFramework |
| `CycloneGames.RPGFoundation.Movement.Integrations.Animancer` | `Kybernetik.Animancer` |
| `CycloneGames.RPGFoundation.Movement.Integrations.UnityNavigation` | `Unity.AI.Navigation` |
| `CycloneGames.RPGFoundation.Movement.Integrations.AStar` | `AstarPathfindingProject` |
| `CycloneGames.RPGFoundation.Movement.Integrations.AgentsNavigation` | ProjectDawn Agents Navigation |
| `CycloneGames.RPGFoundation.Movement.Integrations.GameplayAbilities` | `CycloneGames.GameplayAbilities.Runtime` |

当 RPGFoundation 和可选依赖都以 UPM package 安装时，integration assembly 会通过自身 asmdef 的 `versionDefines` 和 `defineConstraints` 自动启用。基础 Core、Runtime 和 Editor assembly 不需要项目级 scripting define symbol。

当依赖直接复制到 `Assets/` 下时，Unity 不能可靠地把 `package.json` 暴露给 `asmdef.versionDefines`。在这种布局下，RPGFoundation 基础包仍会正常编译；强类型可选 integration 会保持禁用，除非该依赖通过 Package Manager 提供，例如使用 local UPM package path。本包不把项目级全局 scripting define symbol 作为常规 integration 开关。

## Define 符号

这些符号由 integration asmdef 通过 `versionDefines` 生成；这里只作为诊断和 integration 内部条件编译说明，不作为项目级全局要求：

| Symbol | 启用内容 |
| --- | --- |
| `CYCLONE_RPGFOUNDATION_HAS_NETWORKING` | Interaction Networking 集成 |
| `CYCLONE_RPGFOUNDATION_HAS_DETERMINISTIC_MATH` | Interaction DeterministicMath 集成 |
| `CYCLONE_RPGFOUNDATION_HAS_GAMEPLAY_FRAMEWORK` | Interaction GameplayFramework 集成 |
| `CYCLONE_RPGFOUNDATION_HAS_ANIMANCER` | Movement Animancer 集成 |
| `CYCLONE_RPGFOUNDATION_HAS_UNITY_AI_NAVIGATION` | Movement Unity AI Navigation 集成 |
| `CYCLONE_RPGFOUNDATION_HAS_ASTAR_PATHFINDING` | Movement A* Pathfinding 集成 |
| `CYCLONE_RPGFOUNDATION_HAS_AGENTS_NAVIGATION` | Movement Agents Navigation 集成 |
| `CYCLONE_RPGFOUNDATION_HAS_GAMEPLAY_ABILITIES` | Movement GameplayAbilities 集成 |

## 验证

修改程序集或移动文件后：

1. 打开 Unity，等待项目重新导入 assemblies。
2. 确认 Console 没有编译错误。
3. 运行 `CycloneGames.RPGFoundation.Interaction.Tests.Editor` 的 EditMode 测试。
4. 如果可选包通过 Package Manager 安装，确认对应 integration assembly 能编译。
5. 对 Animancer movement，在 movement component 旁添加 `AnimancerMovementAnimationBinder`，并分配 Animancer component。
