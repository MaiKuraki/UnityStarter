[English](README.md) | [Simplified Chinese]

# GameplayAbilities 示例

本目录包含 `CycloneGames.GameplayAbilities` 的可运行示例场景和 authoring asset。示例展示如何连接 `AbilitySystemComponent`、attribute、GameplayTag、GameplayEffect、GameplayAbility、GameplayCue、target actor、pooling 和 startup helper。

示例项目用于学习。生产项目应把需要的模式复制到自己的 assembly 中，替换 scene lookup 为项目服务，并加入符合游戏需求的 authority、validation、asset registry 和 pooling 规则。

## 资源位置

| 内容 | 路径 | 用途 |
| --- | --- | --- |
| 场景 | `Samples/SampleScene.unity` | 可运行端到端场景，包含 Player、Enemy、input、combat log 和已配置 sample asset。 |
| Prefab | `Samples/Prefabs/Player.prefab`, `Samples/Prefabs/Enemy.prefab` | 承载 sample character 和 ASC component 的最小 actor。 |
| Material | `Samples/Materials/` | Sample actor 使用的简单视觉材质。 |
| Ability 和 effect asset | `Samples/ScriptableObjects/` | 已配置的 ability、effect、cue、execution、DoT、poison、purify、passive、bounty 和 level data asset。 |
| Runtime sample script | `Samples/Scripts/` | Ability、attribute、target actor、setup、pooling 和 UI logger 示例。 |
| Editor sample script | `Samples/Editor/` | Attribute name selection 的 sample property drawer 支持。 |
| 预览媒体 | `../Documents~/DemoPreview_1.gif`, `../Documents~/DemoPreview_2.gif` | 用于入门和文档的 README 预览图。 |

## 快速开始

1. 打开 `UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayAbilities/Samples/SampleScene.unity`。
2. 在 Unity Editor 中点击 Play。
3. 使用 sample 控制按键。

| 输入 | 行为 |
| --- | --- |
| `1` | Player 释放 Fireball，造成 instant damage 并附加 burn。 |
| `2` | Player 释放 Purify，从合法目标身上移除 poison 类 debuff。 |
| `E` | Enemy 释放 Poison Blade。 |
| `Space` | 授予调试经验，用于触发 attribute 和 level-up hook。 |

预期结果：UI log 会报告 ability activation、effect application、damage、debuff removal 和 level-up event。Console 不应出现 compile error 或 missing script warning。

## 学习路径

### Character 与 ASC 设置

先阅读这些脚本：

| 脚本 | 学习重点 |
| --- | --- |
| `Scripts/AbilitySystemComponentHolder.cs` | 如何从 `MonoBehaviour` 承载纯 C# `AbilitySystemComponent`。 |
| `Scripts/Character.cs` | Actor initialization、initial attributes、initial passives、ability grant、bounty effect 和 ASC tick。 |
| `Scripts/CharacterAttributeSet.cs` | Primary、secondary 和 meta attribute；clamping；damage conversion；death 和 bounty hook。 |
| `Scripts/GASSampleTags.cs` | 集中式 tag constant 和 runtime tag registration。 |

### Effect 与 Attribute

检查这些 asset：

| Asset | 学习重点 |
| --- | --- |
| `ScriptableObjects/GE_BaseAttributes_Hero.asset` | 通过 GameplayEffect 配置玩家初始 attribute。 |
| `ScriptableObjects/GE_BaseAttributes_Enemy.asset` | 通过 GameplayEffect 配置敌人初始 attribute。 |
| `ScriptableObjects/Fireball/GE_Fireball_Impact.asset` | Fireball 驱动的 instant damage effect。 |
| `ScriptableObjects/DoT/GE_DoT_Burn.asset` | 周期性 burn damage。 |
| `ScriptableObjects/DoT/GE_DoT_Poison.asset` | 周期性 poison damage。 |
| `ScriptableObjects/GE_Passive_IncreaseDamage_10Percent.asset` | Passive attribute modifier pattern。 |

### Ability Authoring

按以下顺序阅读 ability script：

| 脚本 | 学习重点 |
| --- | --- |
| `Scripts/GA_Fireball_SO.cs` | Cost、cooldown、instant damage、burn、SetByCaller magnitude 和 sample target lookup。 |
| `Scripts/GA_PoisonBlade_SO.cs` | 从 ability 应用 debuff。 |
| `Scripts/GA_Purify_SO.cs` | 按 tag 移除 active effect 并过滤 target。 |
| `Scripts/GA_ArmorStack_SO.cs` | Stack behavior 和 stack debugging。 |
| `Scripts/GA_Berserk_SO.cs` 与 `Scripts/GA_Execute_SO.cs` | Granted ability pattern。 |
| `Scripts/GA_ShieldOfLight_SO.cs` | 使用 ongoing requirement 的 defensive buff pattern。 |
| `Scripts/GA_ChainLightning_SO.cs` | 带 falloff 的 multi-target ability flow。 |
| `Scripts/GA_Meteor_SO.cs` | Target actor workflow 和 ground selection。 |

### Targeting 与 AbilityTask

| 脚本 | 学习重点 |
| --- | --- |
| `Scripts/AbilityTask_WaitTargetData_SpawnedActor.cs` | 从 ability task 创建并绑定 target actor。 |
| `Scripts/GameplayAbilityTargetActor_GroundSelect.cs` | 交互式 ground targeting。 |
| `Scripts/TargetActor/GameplayAbilityTargetActor_SingleLineTrace.cs` | Single line trace targeting。 |
| `Scripts/TargetActor/GameplayAbilityTargetActor_SphereOverlap.cs` | Area targeting。 |
| `Scripts/TargetActor/GameplayAbilityTargetActor_ConeTrace.cs` | Cone targeting。 |

### Startup 与 Integration

| 脚本或 Assembly | 学习重点 |
| --- | --- |
| `Scripts/GASPoolInitializer.cs` | 战斗前 pool configuration 和 warmup。 |
| `Scripts/Integrate/Setup/GASManualSetup.cs` | 使用 `CycloneGames.AssetManagement` 的 manual non-DI cue manager startup。 |
| `Scripts/Integrate/Setup/GASServerSetup.cs` | 使用 `NullGameplayCueManager` 的 server/headless startup。 |
| `Scripts/Integrate/DI/VContainer/GASLifetimeScope.cs` | 可选 VContainer composition。该文件隔离在 `CycloneGames.GameplayAbilities.Sample.Integrations.VContainer` 中，仅在 VContainer package 存在时编译。 |

## GameplayTag 布局

Sample tag 集中在 `Scripts/GASSampleTags.cs`，并通过 `[RegisterGameplayTagsFrom]` 注册。

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

生产内容可以沿用相同层级风格，但项目自有 tag 应放在项目 package 或游戏 assembly 中，不要修改 sample tag 作为生产事实来源。

## Package 与 UPM 说明

当前仓库把 samples 保留在 `Samples/`，因此 CycloneGames 模块直接放在 `Assets/ThirdParty` 下时也能清晰可见并直接运行。Package manifest 通过 `samples` entry 暴露同一目录：

```json
{
  "displayName": "Gameplay Ability Samples",
  "path": "Samples"
}
```

如果发布流水线要求隐藏的 UPM sample folder，应在发布包中镜像该源目录；本仓库中 scene、prefab、ScriptableObject 和 `.meta` GUID 的 source of truth 仍保持在 `Samples/`。

## 持久化

Samples 不写入持久化 player data、project settings、editor preference 或 runtime save file。Play Mode 中创建的 runtime object 是临时对象，会在退出 Play Mode 时销毁。`Documents~/` 下的预览媒体只用于文档。

## 验证

修改 sample asset、script、asmdef 或文档后执行以下检查：

1. 打开 `Samples/SampleScene.unity`，确认没有 missing script warning。
2. 点击 Play，测试 `1`、`2`、`E` 和 `Space`。
3. 确认 Console 没有 compile error、missing assembly reference 或 missing asset reference。
4. 在 Unity Test Runner 中运行 GameplayAbilities EditMode tests。
5. 发布 package 前，确认 `package.json` 仍暴露 sample path，根 README 的预览图仍能渲染。
