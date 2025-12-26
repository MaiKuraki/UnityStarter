[**English**](README.md) | [**简体中文**]

# GameplayAbilities 示例

本文件夹包含 Gameplay Ability System 核心功能的完整示例。

## 🎮 快速开始

1. 打开 `SampleScene.unity`
2. 点击 Play
3. 使用以下按键：
   - `1` - 释放火球术（伤害 + 灼烧）
   - `2` - 净化（移除负面效果）
   - `E` - 敌人释放毒刃
   - `Space` - 获得调试经验值

---

## 📂 目录结构

```
Samples/
├── Scripts/           # 所有示例代码
├── ScriptableObjects/ # 预配置的技能和效果
├── Prefabs/           # 角色预制件
├── Materials/         # 视觉材质
└── SampleScene.unity  # 演示场景
```

---

## 📚 示例脚本（按复杂度分类）

### 🟢 入门级

| 脚本 | 说明 |
|------|------|
| `Character.cs` | 基础角色设置，ASC 初始化 |
| `CharacterAttributeSet.cs` | 定义生命、法力、攻击、防御属性 |
| `GASSampleTags.cs` | 使用常量集中定义 GameplayTag |
| `AbilitySystemComponentHolder.cs` | ASC 的 MonoBehaviour 包装器 |

### 🟡 中级

| 脚本 | 说明 |
|------|------|
| `GA_Fireball_SO.cs` | 完整技能：消耗、冷却、伤害、持续伤害 |
| `GA_Purify_SO.cs` | 通过 Tag 查询移除负面效果 |
| `SampleCombatManager.cs` | 输入处理、UI 更新、按 Tag 激活技能 |
| `GC_Fireball_Impact.cs` | 用于冲击 VFX/SFX 的 GameplayCue |

### 🔴 高级

| 脚本 | 说明 |
|------|------|
| `GA_ChainLightning_SO.cs` | 多目标技能，伤害递减 |
| `GA_Meteor_SO.cs` | 带地面选择的瞄准系统 |
| `ExecCalc_Burn.cs` | DoT 的自定义执行计算 |
| `GameplayAbilityTargetActor_GroundSelect.cs` | 交互式瞄准 Actor |

---

## 🏷️ Tag 组织（GASSampleTags.cs）

Tag 是 GAS 的通用语言。本示例使用了良好的层级组织：

```csharp
// 属性
"Attribute.Primary.Attack"
"Attribute.Secondary.Health"

// 状态
"State.Dead"
"State.Burning"

// 负面效果
"Debuff.Burn"
"Debuff.Poison"

// 冷却
"Cooldown.Skill.Fireball"

// 技能
"Ability.Fireball"

// GameplayCue
"GameplayCue.Fireball.Impact"
```

> **提示**：使用 `[RegisterGameplayTagsFrom]` 程序集特性实现自动 Tag 注册。

---

## 🎯 学习路径

### 路径 1：理解 GameplayEffect
1. 查看 `GE_BaseAttributes_Hero.asset`（初始属性）
2. 查看 `Fireball/GE_Fireball_Damage.asset`（即时伤害）
3. 查看 `DoT/GE_Burn_DoT.asset`（周期性伤害）

### 路径 2：构建 GameplayAbility
1. 阅读 `GA_Fireball_SO.cs`（简单技能）
2. 阅读 `GA_Purify_SO.cs`（效果移除）
3. 阅读 `GA_ChainLightning_SO.cs`（复杂瞄准）

### 路径 3：角色设置
1. 阅读 `Character.cs`（ASC 初始化）
2. 阅读 `CharacterAttributeSet.cs`（属性定义）
3. 阅读 `SampleCombatManager.cs`（技能激活）

---

## 💡 最佳实践演示

- **基于 Tag 的技能查找**：`TryActivateAbilityByTag()`
- **数据驱动效果**：所有数值在 ScriptableObject 中配置
- **正确的对象池**：`CreatePoolableInstance()` 模式
- **伤害减免**：`PreProcessInstantEffect()` 重写
- **升级系统**：使用 `PostGameplayEffectExecute()` 追踪经验值
