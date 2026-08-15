# CycloneGames.RPGFoundation.Interaction.DeterministicMath

[English](README.md) | 简体中文

## 概述

此 companion package 提供基于 `CycloneGames.DeterministicMath` 的定点数 Interaction authority 类型。在显式进行 presentation 转换之前，确定性模拟值始终保留为 `FPVector3` 和 `FPInt64`。

此包不依赖 Unity API，适用于 authoritative server、lockstep 或 rollback simulation、replay validation，以及 EditMode 或独立纯 C# 测试。Interaction 基础包不依赖 DeterministicMath。

## 包结构

```text
CycloneGames.RPGFoundation.Interaction.DeterministicMath/
  Runtime/
    CycloneGames.RPGFoundation.Interaction.Integrations.DeterministicMath.asmdef
    IInteractionDeterministicPositionProvider.cs
    InteractionDeterministicAuthorityService.cs
    InteractionDeterministicRequest.cs
    InteractionDeterministicRequestPayload.cs
    InteractionDeterministicTargetSnapshot.cs
    InteractionDeterministicVector3Payload.cs
  Tests/Editor/
    CycloneGames.RPGFoundation.Interaction.DeterministicMath.Tests.Editor.asmdef
    InteractionDeterministicMathIntegrationTests.cs
```

## 程序集与依赖

| 程序集 | 职责 | Unity 依赖 | 使用方引用方式 |
| --- | --- | --- | --- |
| `CycloneGames.RPGFoundation.Interaction.Integrations.DeterministicMath` | 定点 DTO、provider、转换与 authority validation。 | 无 | 显式引用 |
| `CycloneGames.RPGFoundation.Interaction.DeterministicMath.Tests.Editor` | EditMode 行为测试。 | 无 | 仅 Test Runner |

package.json 显式依赖 `com.cyclone-games.rpg-foundation` 和 `com.cyclone-games.deterministic-math`。两个 assembly 均设置为 `autoReferenced: false`。本包不需要 scripting define symbol、UnityEngine reference、runtime reflection 或 DI 容器。

## 安装

### UPM

安装 `com.cyclone-games.rpg-foundation-interaction-deterministic-math`。Unity Package Manager 会解析已声明的 Interaction 与 DeterministicMath 依赖。

### 项目内 Assets 布局

在项目的 Assets 树中放置以下 package root：

```text
CycloneGames.RPGFoundation/
CycloneGames.DeterministicMath/
CycloneGames.RPGFoundation.Interaction.DeterministicMath/
```

直接 asmdef reference 使同一 assembly 可在 Assets 项目中使用。不需要 PlayerSettings 宏或生成的 capability 文件。

## Authority 组合

为一个明确的 authority owner 和 world 构造一个 service：

```csharp
using CycloneGames.RPGFoundation.Interaction.Core;
using CycloneGames.RPGFoundation.Interaction.Integrations.DeterministicMath;

var authority = new InteractionDeterministicAuthorityService(
    new InteractionAuthorityOptions(worldId: worldId));

authority.TryRegisterTarget(new InteractionDeterministicTargetSnapshot(
    worldId,
    targetStableId,
    targetPosition,
    interactionRange,
    isAvailable: true,
    enabledActionIds: enabledActions));
```

使用 `FPVector3` 或显式 `IInteractionDeterministicPositionProvider` 验证请求：

```csharp
InteractionValidationResult result = authority.ValidateRequest(
    request,
    instigatorPosition,
    serverTick);
```

`InteractionDeterministicVector3Payload` 保存 Q32.32 raw component，可在不经过浮点转换的情况下往返。只应在非权威的 presentation 或 reporting 边界调用 `ToInteractionVector3`。

## 所有权、线程与性能

- composition root 持有每个 `InteractionDeterministicAuthorityService`，并负责其 reset 或释放边界。
- service 是可变对象，内部不做同步。创建、配置、注册、验证、排队与清理必须由同一个 owner thread 执行。
- DTO 与 vector payload 是 value type，转换过程不产生 allocation。
- Authority dictionary 在保留的 identity 集合增长时会分配内存。上线前必须确定并压测产品级 world 分片与 identity budget。
- target snapshot 接收的 action array 会被 snapshot 观察；注册后应将该数组视为不可变数据。
- Runtime assembly 不包含 Unity API，可在源码层面用于 headless 纯 C# 组合。目标 runtime 与 AOT 行为仍需通过对应平台构建验证。

## 持久化与协议边界

本包不写入文件、偏好、资产、缓存或存档。Payload struct 是便于传输的 value shape，但不定义 wire codec 或持久化 schema。Networking、replay 和 save owner 必须定义各自的有界 envelope、版本、校验与完整性策略。

## 验证

修改 integration 后运行以下 EditMode assembly：

```text
CycloneGames.RPGFoundation.Interaction.DeterministicMath.Tests.Editor
CycloneGames.RPGFoundation.Interaction.Tests.Editor
CycloneGames.DeterministicMath.Tests.Editor
```

用于 server target 时，还应在不引用 UnityEngine 的 consumer 中编译，并执行产品实际使用的 backend 与 AOT build matrix。

## 故障排查

- 找不到确定性类型时，在使用方 asmdef 中加入 `CycloneGames.RPGFoundation.Interaction.Integrations.DeterministicMath`。
- asmdef reference 无法解析时，确认 Assets 安装章节列出的三个 package root 均已存在。
- validation 返回 `WrongWorld` 时，确认 service option、target snapshot 与 request 使用相同的显式 world ID。
- validation 返回 `InvalidRequest` 时，在进入 authority 边界前检查 stable ID 与 deterministic position provider。
