# CycloneGames.RPGFoundation

[English](./README.md) | 简体中文

`CycloneGames.RPGFoundation` 为 Unity 项目提供可复用的 RPG 基础模块。包内系统按玩法领域组织，核心契约与 Unity 运行时对象解耦，Unity-facing 组件通过独立的 Runtime、Editor、Tests 和 Integration 程序集暴露。

当前包包含 Interaction 和 Movement 模块。可选网络桥接和第三方集成位于独立程序集或独立包中，项目可以只启用实际使用的依赖。

## 快速入门

1. 根据需要引用对应模块程序集，例如 Unity 移动组件引用 `CycloneGames.RPGFoundation.Movement.Runtime`，交互组件引用 `CycloneGames.RPGFoundation.Interaction.Runtime`。
2. 在 server、headless、simulation、CLI 或 EditMode 测试中优先使用 `Core` 契约。
3. 在 Unity 场景绑定、ScriptableObject 配置和 adapter-facing 行为中使用 `Runtime` 组件。
4. 只有当对应依赖已安装并满足 asmdef 条件时，才启用可选 integration assembly。
5. 修改契约、asmdef、序列化数据或 integration 边界后运行模块 EditMode 测试。

## 模块布局

长期维护模块使用以下布局：

```text
<Module>/
  README.md
  README.SCH.md
  Core/
  Runtime/
  Editor/
  Tests/
  Runtime/Integrations/
```

| 目录 | 作用 |
| --- | --- |
| `Core/` | 不依赖 Unity 的契约、值对象、校验逻辑、确定性数据，以及可在 server、headless、CLI 和 Unity 测试环境运行的服务。 |
| `Runtime/` | Unity-facing 组件、ScriptableObject authoring bridge、runtime adapter 和默认 Unity 实现。 |
| `Editor/` | Inspector、窗口、validator、drawer 和 authoring 工具。 |
| `Tests/` | 模块契约和运行时行为的 EditMode 与 PlayMode 测试。 |
| `Runtime/Integrations/` | 通过独立 asmdef 隔离的可选第三方或 Cyclone 模块 adapter。 |

当前模块：

| 模块 | 作用 |
| --- | --- |
| `Interaction/` | 交互契约、本地运行时组件、权威校验、确定性桥接、Inspector 和测试。 |
| `Movement/` | 移动核心契约、2D/3D Unity 移动组件、寻路 adapter、动画 adapter、Inspector 和测试。 |

## 程序集边界

| Assembly | 职责 |
| --- | --- |
| `CycloneGames.RPGFoundation.Interaction.Core` | 不依赖 Unity 的交互契约、值对象、校验、限流和权威服务。 |
| `CycloneGames.RPGFoundation.Interaction.Runtime` | Unity-facing 交互组件和运行时服务。 |
| `CycloneGames.RPGFoundation.Interaction.Editor` | 交互 Inspector、validator 和 Editor 工具。 |
| `CycloneGames.RPGFoundation.Interaction.Tests.Editor` | 交互 EditMode 测试。 |
| `CycloneGames.RPGFoundation.Movement.Core` | 不依赖 Unity 的移动契约、属性、状态标识、快照和 helper 类型。 |
| `CycloneGames.RPGFoundation.Movement.Runtime` | Unity-facing 2D/3D 移动组件、ScriptableObject 配置、动画抽象和寻路抽象。 |
| `CycloneGames.RPGFoundation.Movement.Editor` | 移动 Inspector 和 authoring 校验。 |
| `CycloneGames.RPGFoundation.Movement.Tests.Editor` | 移动 EditMode 测试。 |

Movement state change 使用 `MovementStateRequestContext` 携带 source、payload、tick、sequence、prediction key 和 custom flags，同时保持 `Movement.Core` 不依赖 GameplayAbilities、GameplayTags 或 Networking。Ability、input、pathfinding、AI 和 network integration 通过独立程序集共享同一套状态门控。

## 可选集成

可选集成隔离在独立程序集中，因此基础包在未安装可选包时也能编译。Cyclone 网络桥接由独立可选包提供。

| Integration Assembly | 依赖 |
| --- | --- |
| `CycloneGames.RPGFoundation.Interaction.Integrations.DeterministicMath` | `CycloneGames.DeterministicMath.Core` |
| `CycloneGames.RPGFoundation.Interaction.Integrations.GameplayFramework` | `CycloneGames.GameplayFramework.Runtime` |
| `CycloneGames.RPGFoundation.Interaction.Integrations.DeterministicMath.GameplayFramework` | DeterministicMath + GameplayFramework |
| `CycloneGames.RPGFoundation.Movement.Integrations.DeterministicMath` | `CycloneGames.DeterministicMath.Core` |
| `CycloneGames.RPGFoundation.Movement.Integrations.Animancer` | `Kybernetik.Animancer` |
| `CycloneGames.RPGFoundation.Movement.Integrations.UnityNavigation` | `Unity.AI.Navigation` |
| `CycloneGames.RPGFoundation.Movement.Integrations.AStar` | `AstarPathfindingProject` |
| `CycloneGames.RPGFoundation.Movement.Integrations.AgentsNavigation` | ProjectDawn Agents Navigation |
| `CycloneGames.RPGFoundation.Movement.Integrations.GameplayAbilities` | `CycloneGames.GameplayAbilities.Runtime` + `CycloneGames.GameplayTags.Core` |

可选网络包：

| Package | 依赖 | 作用 |
| --- | --- | --- |
| `CycloneGames.RPGFoundation.Interaction.Networking` | `CycloneGames.Networking.Core` | 与传输层无关的 interaction request、result、cancel 和 authority validation contract。 |
| `CycloneGames.RPGFoundation.Movement.Networking` | `CycloneGames.Networking.Core` | 与传输层无关的 movement input、authoritative snapshot、correction、teleport、full-state request、authority transfer、input validation、history 和 reconciliation contract。 |

## Define 符号

以下符号由 integration asmdef 通过 `versionDefines` 或 define constraints 生成。它们用于诊断和 integration-local 条件编译，不作为项目级全局要求。

| Symbol | 启用内容 |
| --- | --- |
| `CYCLONE_RPGFOUNDATION_HAS_DETERMINISTIC_MATH` | Interaction 和 Movement DeterministicMath 集成。 |
| `CYCLONE_RPGFOUNDATION_HAS_GAMEPLAY_FRAMEWORK` | Interaction GameplayFramework 集成。 |
| `CYCLONE_RPGFOUNDATION_HAS_ANIMANCER` | Movement Animancer 集成。 |
| `CYCLONE_RPGFOUNDATION_HAS_UNITY_AI_NAVIGATION` | Movement Unity AI Navigation 集成。 |
| `CYCLONE_RPGFOUNDATION_HAS_ASTAR_PATHFINDING` | Movement A* Pathfinding 集成。 |
| `CYCLONE_RPGFOUNDATION_HAS_AGENTS_NAVIGATION` | Movement Agents Navigation 集成。 |
| `CYCLONE_RPGFOUNDATION_HAS_GAMEPLAY_ABILITIES` | Movement GameplayAbilities 集成，依赖 GameplayAbilities 和 GameplayTags 程序集。 |

## 持久化

本包不定义运行时存档、Editor 偏好、PlayerPrefs、EditorPrefs、SessionState、registry entry 或隐藏缓存。配置和持久化玩法状态由接入项目或明确声明该行为的可选模块持有。

## 验证

修改程序集、移动文件、更新 integration reference 或调整序列化契约后运行以下检查：

```text
Unity Test Runner > EditMode > CycloneGames.RPGFoundation.Interaction.Tests.Editor
Unity Test Runner > EditMode > CycloneGames.RPGFoundation.Movement.Tests.Editor
Unity Test Runner > EditMode > optional RPGFoundation networking package tests when present
```

Unity 刷新 generated project files 后可执行 CLI 编译：

```text
dotnet build UnityStarter/CycloneGames.RPGFoundation.Movement.Core.csproj --nologo
dotnet build UnityStarter/CycloneGames.RPGFoundation.Movement.Runtime.csproj --nologo
dotnet build UnityStarter/CycloneGames.RPGFoundation.Movement.Tests.Editor.csproj --nologo
```
