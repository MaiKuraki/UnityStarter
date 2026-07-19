[English](README.md) | [简体中文]

# GameplayAbilities 示例

本目录包含 `CycloneGames.GameplayAbilities` 的可运行示例场景和创作资产。示例展示如何连接 `AbilitySystemComponent`、Attribute、GameplayTag、GameplayEffect、GameplayAbility、GameplayCue、Target Actor、一次性 Runtime Lease、有界 Cue Pool 和启动辅助器。

示例项目用于学习。生产项目应将需要的模式放入项目自己的程序集，以项目服务替换场景查找，并按游戏需求补充 Authority、Validation、Asset Registry、Runtime Lease/Cache 和 Cue Pool 规则。

## 资源位置

| 内容 | 路径 | 用途 |
| --- | --- | --- |
| 场景 | `Samples/SampleScene.unity` | 包含 Player、Enemy、输入、战斗日志和已配置示例资产的可运行端到端场景。 |
| Prefab | `Samples/Prefabs/Player.prefab`、`Samples/Prefabs/Enemy.prefab` | 承载示例 Character 和 ASC 组件的最小 Actor。 |
| Material | `Samples/Materials/` | 示例 Actor 使用的简单视觉材质。 |
| Ability 与 Effect 资产 | `Samples/ScriptableObjects/` | 已配置的 Ability、Effect、Cue、Execution、DoT、Poison、Purify、Passive、Bounty 和 Level Data 资产。 |
| Runtime 示例脚本 | `Samples/Scripts/` | Ability、Attribute、Target Actor、Setup、Cue Pool 和 UI Logger 示例。 |
| Editor 示例脚本 | `Samples/Editor/` | 示例 Property Drawer，以及 ASC Holder Inspector 的 Play Mode Diagnostics 控件。 |
| 预览媒体 | `../Documents~/DemoPreview_1.gif`、`../Documents~/DemoPreview_2.gif` | 用于入门和文档的 README 预览图。 |

## 快速开始

1. 打开 `UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayAbilities/Samples/SampleScene.unity`。
2. 在 Unity Editor 中进入 Play Mode。
3. 使用示例控制键。

| 输入 | 行为 |
| --- | --- |
| `1` | Player 施放 Fireball，造成即时伤害并附加 Burn。 |
| `2` | Player 施放 Purify，从合法目标上移除 Poison 类 Debuff。 |
| `E` | Enemy 施放 Poison Blade。 |
| `F` | 切换 Player 与 Enemy 各自独立的 Runtime Diagnostics 面板。 |
| `Space` | 授予调试经验，用于触发 Attribute 和 Level-up Hook。 |

预期结果：UI 日志报告 Ability Activation、Effect Application、Damage、Debuff Removal 和 Level-up 事件。按下 `F` 后同时显示 `Player [ASC]` 与 `Enemy [ASC]` 面板。Console 不应出现编译错误或 Missing Script 警告。

## Runtime Diagnostics Overlay

按下 Diagnostics 按钮或 `F` 时，`SampleCombatManager` 会显式注册 Player 与 Enemy 的 ASC。在 Play Mode 中，也可以选择任一 Actor 的 `AbilitySystemComponentHolder`，使用其 **GAS Runtime Overlay** Inspector 区域。多选 Player 与 Enemy 后，可以一次添加、更新或移除两个 Holder 承载的 ASC。Inspector 会显示选中对象的 Live/Registered 数量、全局有界数量与容量，以及当前可见状态。

Inspector 控件是临时命令，不是按 ASC 序列化的开关。它们不会产生 Prefab Override、拥有或 Dispose ASC、调用 `ClearTargets`、移除未选中的 ASC，也不会销毁 Overlay Singleton。Registry 对每个 ASC 只有一条共享记录，因此即使最初由其他调用方注册，Inspector 命令仍会修改选中 ASC 的同一记录。注册关系不拥有目标、不扫描 Scene，也不改变 ASC 生命周期。`SampleCombatManager` 在关闭时移除 Player 与 Enemy 的记录；只有 Singleton 由示例创建且没有其他注册时，示例才销毁 Overlay Singleton。

在 Overlay 初始化前，通过 `GASOverlayConfig.MaxPanels` 设置有界面板容量。默认值为 8，取值限制为 1 到 32，并在当前 Overlay 实例生命周期内保持固定。Runtime IMGUI Diagnostics 运行在 Unity Main Thread，适用于开发环境和明确配置的支持版本，不应进入 Gameplay Hot Path。

## 建议阅读顺序

### Character 与 ASC 设置

先阅读以下脚本：

| 脚本 | 学习重点 |
| --- | --- |
| `Scripts/AbilitySystemComponentHolder.cs` | 如何通过 `MonoBehaviour` 承载纯 C# `AbilitySystemComponent`。 |
| `Editor/AbilitySystemComponentHolderEditor.cs` | 不序列化调试状态，通过显式、支持多对象且仅 Play Mode 可用的命令控制 Diagnostics。 |
| `Scripts/Character.cs` | Actor 初始化、初始 Attribute、初始 Passive、Ability Grant、Bounty Effect 和 ASC Tick。 |
| `Scripts/CharacterAttributeSet.cs` | Primary、Secondary 与 Meta Attribute；Clamping；Damage Conversion；Death 与 Bounty Hook。 |
| `Scripts/GASSampleTags.cs` | 集中式 Tag 常量和 Runtime Tag 注册。 |

### Effect 与 Attribute

检查以下资产：

| 资产 | 学习重点 |
| --- | --- |
| `ScriptableObjects/GE_BaseAttributes_Hero.asset` | 通过 GameplayEffect 配置玩家初始 Attribute。 |
| `ScriptableObjects/GE_BaseAttributes_Enemy.asset` | 通过 GameplayEffect 配置敌人初始 Attribute。 |
| `ScriptableObjects/Fireball/GE_Fireball_Impact.asset` | Fireball 驱动的即时伤害 Effect。 |
| `ScriptableObjects/DoT/GE_DoT_Burn.asset` | 周期性 Burn Damage。 |
| `ScriptableObjects/DoT/GE_DoT_Poison.asset` | 周期性 Poison Damage。 |
| `ScriptableObjects/GE_Passive_IncreaseDamage_10Percent.asset` | Passive Attribute Modifier 模式。 |

### Ability Authoring

每个示例 `CreateGameplayAbility()` 都使用不可变输入构造派生 Ability，然后恰好调用一次 `InitializeAbility(ability)`。每个 `CreateRuntimeInstance()` 只重建这些派生输入；Runtime 会从 Definition 复制已封存的 Base Ability 配置。每个 Runtime Instance 都是一次性 Lease，Owner 释放后即被丢弃。Poison Blade 与 Purify 资产使用 `InstancedPerActor`，Runtime 示例资产不能选择 `NonInstanced`。

Purify 与 Shockwave 会在派生构造边界复制 Faction Filter Tag Container。因此，在 `GetGameplayAbility()` 发布缓存 Definition 后修改 ScriptableObject Container 或 Source Container，不会改变该 Definition，也不会改变 Runtime Instance 的 Filter State。

按以下顺序阅读 Ability 脚本：

| 脚本 | 学习重点 |
| --- | --- |
| `Scripts/GA_Fireball_SO.cs` | Cost、Cooldown、即时伤害、Burn、SetByCaller Magnitude 和示例 Target Lookup。 |
| `Scripts/GA_PoisonBlade_SO.cs` | 从 Ability 应用 Debuff。 |
| `Scripts/GA_Purify_SO.cs` | 按 Tag 移除 Active Effect、过滤 Target，以及隔离构造输入 Tag。 |
| `Scripts/GA_ArmorStack_SO.cs` | Stack 行为和 Stack 调试。 |
| `Scripts/GA_Berserk_SO.cs` 与 `Scripts/GA_Execute_SO.cs` | Granted Ability 模式。 |
| `Scripts/GA_ShieldOfLight_SO.cs` | 使用 Ongoing Requirement 的 Defensive Buff 模式。 |
| `Scripts/GA_ChainLightning_SO.cs` | 带 Falloff 的多目标 Ability 流程。 |
| `Scripts/GA_Meteor_SO.cs` | Target Actor 工作流和地面选择。 |
| `Scripts/GA_Shockwave_SO.cs` | 使用隔离的 Required/Forbidden Faction Tag Filter 执行范围伤害。 |

### Targeting 与 AbilityTask

| 脚本 | 学习重点 |
| --- | --- |
| `Scripts/AbilityTask_WaitTargetData_SpawnedActor.cs` | 从 AbilityTask 创建并绑定 Target Actor。 |
| `Scripts/GameplayAbilityTargetActor_GroundSelect.cs` | 交互式地面 Targeting。 |
| `Scripts/TargetActor/GameplayAbilityTargetActor_SingleLineTrace.cs` | 单线 Trace Targeting。 |
| `Scripts/TargetActor/GameplayAbilityTargetActor_SphereOverlap.cs` | 范围 Targeting。 |
| `Scripts/TargetActor/GameplayAbilityTargetActor_ConeTrace.cs` | 锥形 Targeting。 |

### Startup 与 Integration

| 脚本或程序集 | 学习重点 |
| --- | --- |
| `Scripts/SampleCombatManager.cs` | Scene-owned `GASRuntimeContext` 组合、共享 ASC 初始化和逆序 Shutdown。 |
| `Scripts/Integrate/Setup/GASManualSetup.cs` | 使用 `CycloneGames.AssetManagement` 的手动非 DI Cue Manager 启动，并支持可选 Runtime Backing-cache Profile。 |
| `Scripts/Integrate/Setup/GASServerSetup.cs` | 使用 `NullGameplayCueManager` 的 Server/Headless 启动，并支持可选 Runtime Backing-cache Profile。 |
| `Scripts/Integrate/DI/VContainer/GASLifetimeScope.cs` | 可选 VContainer 组合。该文件隔离在 `CycloneGames.GameplayAbilities.Sample.Integrations.VContainer` 中，仅当 VContainer Package 存在时编译。 |

硬件 Profile 可以向任一显式 Setup Helper 传入有界 EffectSpec Backing-cache Policy。省略该参数时使用 Context 默认值：

```csharp
var cacheProfile = new GASRuntimeCacheProfile(
    effectSpecBackingCapacity: 32);

GASRuntimeContext clientContext = GASManualSetup.CreateContext(
    assetPackage,
    cuePoolConfig,
    out GameplayCueManager cueManager,
    cacheProfile: cacheProfile);

GASRuntimeContext serverContext = GASServerSetup.CreateContext(
    cacheProfile: cacheProfile);
```

## GameplayTag 布局

示例 Tag 集中在 `Scripts/GASSampleTags.cs`，并通过 `[RegisterGameplayTagsFrom]` 注册。

```csharp
"Attribute.Primary.Attack"
"Attribute.Secondary.Health"
"State.Burning"
"Buff.ArmorStack"
"Debuff.Poison"
"Cooldown.Skill.Fireball"
"Ability.Fireball"
"GameplayCue.Fireball.Impact"
"Faction.Player"
"Faction.NPC.Enemy"
```

生产内容可以沿用相同层级风格，但项目自有 Tag 应放在项目 Package 或游戏 Assembly 中，不要修改示例 Tag 并将其作为生产事实来源。

## Package 与 UPM 说明

Samples 位于 `Samples/`，因此 CycloneGames 模块直接嵌入 `Assets/ThirdParty` 时仍清晰可见并可直接运行。Package Manifest 通过 `samples` Entry 暴露同一目录：

```json
{
  "displayName": "Gameplay Ability Samples",
  "path": "Samples"
}
```

如果发布流水线要求隐藏的 UPM Sample Folder，应在发布包中镜像该源目录，同时保持 Scene、Prefab、ScriptableObject 和 `.meta` GUID 的事实来源位于 `Samples/`。

## 持久化

Samples 不写入持久化 Player Data、Project Settings、Editor Preference 或 Runtime Save File。Play Mode 中创建的 Runtime Object 是临时对象，会在退出 Play Mode 时销毁。`Documents~/` 下的预览媒体仅用于文档。

## 验证

修改示例资产、脚本、asmdef 或文档后执行以下检查：

1. 打开 `Samples/SampleScene.unity`，确认没有 Missing Script 警告。
2. 进入 Play Mode，测试 `1`、`2`、`E`、`F` 和 `Space`；确认 `F` 同时显示 Player 与 Enemy 面板。
3. 确认 Console 没有编译错误、Missing Assembly Reference 或 Missing Asset Reference。
4. 在 Unity Test Runner 中运行 GameplayAbilities EditMode Test 与 `CycloneGames.GameplayAbilities.Tests.PlayMode`。
5. 发布 Package 前，确认 `package.json` 仍暴露 Sample Path，且根 README 中的预览图仍能渲染。
6. 确认每个示例 `CreateGameplayAbility()` 只调用一次 `InitializeAbility`，每个 `CreateRuntimeInstance()` 只构造派生输入，并且 Poison Blade 与 Purify 资产保持受支持的 Instanced Policy。
