# CycloneGames.RPGFoundation.Interaction.GameplayFramework

[English](README.md) | 简体中文

## 概述

此 companion package 将 `CycloneGames.GameplayFramework.Runtime.Actor` 连接到 RPGFoundation Interaction，同时避免在 Interaction 宿主包中暴露 GameplayFramework 类型。它提供位置转换、instigator 组合和 authority target snapshot 等聚焦的扩展方法。

仅当 GameplayFramework Actor 需要参与 Interaction runtime 时安装此包。不使用 GameplayFramework 的项目不会引入这些程序集和依赖。

## 包结构

```text
CycloneGames.RPGFoundation.Interaction.GameplayFramework/
  Runtime/
    CycloneGames.RPGFoundation.Interaction.Integrations.GameplayFramework.asmdef
    GameplayFrameworkInteractionExtensions.cs
  Tests/Editor/
    CycloneGames.RPGFoundation.Interaction.GameplayFramework.Tests.Editor.asmdef
    GameplayFrameworkInteractionExtensionsTests.cs
```

## 程序集与依赖

| 程序集 | 职责 | Unity 依赖 | 使用方引用方式 |
| --- | --- | --- | --- |
| `CycloneGames.RPGFoundation.Interaction.Integrations.GameplayFramework` | Actor 到 Interaction 的 adapter。 | 有 | 显式引用 |
| `CycloneGames.RPGFoundation.Interaction.GameplayFramework.Tests.Editor` | EditMode 行为测试。 | 有 | 仅 Test Runner |

package.json 显式依赖 `com.cyclone-games.rpg-foundation` 和 `com.cyclone-games.gameplay-framework`。Runtime assembly 设置为 `autoReferenced: false`；调用扩展方法的程序集必须显式引用它。本包不使用 scripting define symbol，也不依赖 DI 容器。

## 安装

### UPM

安装 `com.cyclone-games.rpg-foundation-interaction-gameplay-framework`。Unity Package Manager 会解析 package.json 中声明的两个依赖。

### 项目内 Assets 布局

在项目的 Assets 树中放置以下三个 package root：

```text
CycloneGames.RPGFoundation/
CycloneGames.GameplayFramework/
CycloneGames.RPGFoundation.Interaction.GameplayFramework/
```

integration assembly 使用直接 asmdef reference，因此同一套源码可在 Assets 项目中编译，不需要 PlayerSettings 宏或生成的 capability 文件。

## Actor Adapter

将 Actor 位置转换为 Interaction core vector：

```csharp
using CycloneGames.RPGFoundation.Interaction.Core;
using CycloneGames.RPGFoundation.Interaction.Integrations.GameplayFramework;

if (actor.TryGetInteractionPosition(out InteractionVector3 position))
{
    // 将位置提交到交互 authority 边界。
}
```

创建以 Actor GameObject 为 Unity owner 的 Interaction instigator：

```csharp
GameObjectInstigator instigator = actor.CreateInteractionInstigator(stablePlayerId);
```

根据 Actor 位置创建有界 authority snapshot：

```csharp
bool created = actor.TryCreateInteractionTargetSnapshot(
    worldId,
    targetStableId,
    interactionRange,
    out InteractionTargetSnapshot snapshot,
    enabledActionIds: enabledActions);
```

`targetStableId` 必须非零。Actor 缺失或已销毁时，`Try*` 操作返回 `false`。无法读取 Actor 时，`GetInteractionPosition` 返回 `InteractionVector3.Zero`。

## 所有权、线程与性能

- Actor 及其 GameObject 由 GameplayFramework 和场景或 spawn composition root 持有。
- `GameObjectInstigator` 观察 Actor GameObject；本包不会单独销毁或持有 Unity 对象生命周期。
- action snapshot 数组按照 Interaction value object 的所有权契约传递。发布后应将传入数组视为不可变数据。
- Actor 与 GameObject API 属于 Unity 主线程操作。
- 位置转换和成功的 `TryGetInteractionPosition` 不产生 managed allocation。创建 instigator 会按设计分配一个 managed wrapper。
- 需要重复使用 instigator 时应缓存，不要在 update loop 中反复创建。

## 持久化

本包不写入文件、资产、偏好、缓存或存档。稳定 Actor ID 与 target ID 由应用 authority 和持久化层负责。

## 验证

修改 integration 后运行以下 EditMode assembly：

```text
CycloneGames.RPGFoundation.Interaction.GameplayFramework.Tests.Editor
CycloneGames.RPGFoundation.Interaction.Tests.Editor
CycloneGames.GameplayFramework.Tests.Editor
```

同时确认只包含 Interaction 宿主包的项目不会引用此 companion assembly。

## 故障排查

- 找不到扩展方法时，在使用方 asmdef 中加入 `CycloneGames.RPGFoundation.Interaction.Integrations.GameplayFramework`。
- asmdef reference 无法解析时，确认 Assets 安装章节列出的三个 package root 均已存在。
- `Try*` 方法返回 `false` 时，确认 Actor 仍有效且 target stable ID 非零。
