# CycloneGames.RPGFoundation.Interaction.DeterministicMath.GameplayFramework

[English](README.md) | 简体中文

## 概述

此 companion package 是 GameplayFramework Actor 与确定性 RPGFoundation Interaction authority 数据之间的窄 Unity 边界。Actor 提供 Unity 生命周期与身份上下文；显式 `IInteractionDeterministicPositionProvider` 提供权威定点位置。

此 bridge 不会从 `Transform` 推导 authority 位置，从而将渲染插值与浮点场景状态隔离在确定性验证之外。

## 包结构

```text
CycloneGames.RPGFoundation.Interaction.DeterministicMath.GameplayFramework/
  Runtime/
    DeterministicGameplayFramework.asmdef
    GameplayFrameworkDeterministicInteractionExtensions.cs
  Tests/Editor/
    DeterministicGameplayFramework.Tests.Editor.asmdef
    InteractionDeterministicGameplayFrameworkIntegrationTests.cs
```

## 程序集与依赖

| 程序集 | 职责 | Unity 依赖 | 使用方引用方式 |
| --- | --- | --- | --- |
| `CycloneGames.RPGFoundation.Interaction.Integrations.DeterministicMath.GameplayFramework` | Actor 与 deterministic position adapter 方法。 | 有 | 显式引用 |
| `CycloneGames.RPGFoundation.Interaction.DeterministicMathGameplayFramework.Tests.Editor` | EditMode 行为测试。 | 有 | 仅 Test Runner |

package.json 声明 Runtime assembly 直接使用的全部 package dependency：RPGFoundation、Interaction DeterministicMath companion、DeterministicMath 和 GameplayFramework。程序集设置为 `autoReferenced: false`。本包不需要 scripting define symbol 或 DI 容器。

此包不引用非确定性的 Interaction GameplayFramework companion。使用方可以独立安装任一 bridge；同时暴露两种 authority path 时也可以同时安装。

## 安装

### UPM

安装 `com.cyclone-games.rpg-foundation-interaction-deterministic-math-gameplay-framework`。Unity Package Manager 会解析已声明的依赖。

### 项目内 Assets 布局

在项目的 Assets 树中放置以下 package root：

```text
CycloneGames.RPGFoundation/
CycloneGames.DeterministicMath/
CycloneGames.GameplayFramework/
CycloneGames.RPGFoundation.Interaction.DeterministicMath/
CycloneGames.RPGFoundation.Interaction.DeterministicMath.GameplayFramework/
```

integration assembly 使用直接 asmdef reference。同一套源码可在 Assets 项目中编译，不需要 PlayerSettings 宏或生成的 capability 文件。

## 确定性 Actor 组合

确定性模拟 owner 实现 `IInteractionDeterministicPositionProvider`：

```csharp
public sealed class PlayerSimulationState : IInteractionDeterministicPositionProvider
{
    public FPVector3 Position { get; set; }

    public bool TryGetDeterministicInteractionPosition(out FPVector3 position)
    {
        position = Position;
        return true;
    }
}
```

结合该 provider 与 Actor 创建 authority 数据：

```csharp
bool created = actor.TryCreateDeterministicInteractionTargetSnapshot(
    simulationState,
    worldId,
    targetStableId,
    interactionRange,
    out InteractionDeterministicTargetSnapshot snapshot,
    enabledActionIds: enabledActions);
```

创建 request payload 时遵循相同规则：

```csharp
bool created = actor.TryCreateDeterministicInteractionRequestPayload(
    simulationState,
    requestId,
    instigatorStableId,
    targetStableId,
    actionId,
    tick,
    worldId,
    out InteractionDeterministicRequestPayload payload);
```

Actor 缺失或已销毁、provider 缺失、provider 读取失败，或 target stable ID 为零时，方法返回 `false` 和默认输出值。

## 所有权、线程与性能

- GameplayFramework 持有 Actor 生命周期。deterministic simulation owner 持有 position provider 及其更新时序。
- provider 必须读取 rollback、lockstep、replay 或 server simulation 使用的同一份权威状态。
- 定点模拟状态是 authority 时，不要在 provider 内读取 Transform position。
- Actor 有效性检查和 MonoBehaviour provider 属于 Unity 主线程操作。纯 C# provider 可以遵循其 simulation owner 的线程契约，但 bridge 调用仍会访问 Actor，因此应在 Unity 主线程执行。
- 成功创建 payload 和 snapshot 时使用 value type。传入的 action array 遵循 snapshot 所有权契约，发布后应视为不可变数据。

## 持久化

本包不写入文件、资产、偏好、缓存或存档，只创建确定性 value object。Protocol、replay 与 save owner 负责序列化、边界、完整性和存储生命周期。

## 验证

修改 integration 后运行以下 EditMode assembly：

```text
CycloneGames.RPGFoundation.Interaction.DeterministicMathGameplayFramework.Tests.Editor
CycloneGames.RPGFoundation.Interaction.DeterministicMath.Tests.Editor
CycloneGames.GameplayFramework.Tests.Editor
```

还应使用产品真实 simulation clock 和目标 build backend 独立验证 rollback 或 lockstep owner。

## 故障排查

- 找不到扩展方法时，在使用方 asmdef 中加入 `CycloneGames.RPGFoundation.Interaction.Integrations.DeterministicMath.GameplayFramework`。
- asmdef reference 无法解析时，确认 Assets 安装章节列出的全部 package root 均已存在。
- 创建返回 `false` 时，检查 Actor 生命周期、provider 是否存在、provider 读取是否成功，以及 stable ID。
- authority 结果与确定性模拟不一致时，确认 provider 读取 simulation state，而不是渲染 Transform state。
