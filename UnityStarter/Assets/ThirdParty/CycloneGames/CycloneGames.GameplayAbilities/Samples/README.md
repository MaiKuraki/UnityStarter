[**English**] | [**简体中文**](README.SCH.md)

# GameplayAbilities Samples

This folder contains working examples demonstrating the core features of the Gameplay Ability System.

## 🎮 Quick Start

1. Open `SampleScene.unity`
2. Press Play
3. Use the following controls:
   - `1` - Cast Fireball (damage + burn)
   - `2` - Cast Purify (remove debuffs)
   - `E` - Enemy casts Poison Blade
   - `Space` - Grant debug XP

---

## 📂 Directory Structure

```
Samples/
├── Scripts/           # All sample code
├── ScriptableObjects/ # Pre-configured abilities & effects
├── Prefabs/           # Character prefabs
├── Materials/         # Visual materials
└── SampleScene.unity  # Demo scene
```

---

## 📚 Sample Scripts by Complexity

### 🟢 Beginner

| Script                            | Description                                      |
| --------------------------------- | ------------------------------------------------ |
| `Character.cs`                    | Basic character setup with ASC initialization    |
| `CharacterAttributeSet.cs`        | Defines Health, Mana, Attack, Defense attributes |
| `GASSampleTags.cs`                | Centralized tag definitions using constants      |
| `AbilitySystemComponentHolder.cs` | MonoBehaviour wrapper for ASC                    |
| `GASPoolInitializer.cs`           | Pool configuration and prewarming setup          |

### 🟡 Intermediate

| Script                   | Description                                           |
| ------------------------ | ----------------------------------------------------- |
| `GA_Fireball_SO.cs`      | Complete ability: cost, cooldown, damage, DoT         |
| `GA_Purify_SO.cs`        | Removes debuffs using tag queries                     |
| `GA_ArmorStack_SO.cs`    | Stacking buff demonstration                           |
| `SampleCombatManager.cs` | Input handling, UI updates, ability activation by tag |
| `GC_Fireball_Impact.cs`  | GameplayCue for VFX/SFX on impact                     |

### 🔴 Advanced

| Script                                       | Description                               |
| -------------------------------------------- | ----------------------------------------- |
| `GA_ChainLightning_SO.cs`                    | Multi-target ability with damage falloff  |
| `GA_Meteor_SO.cs`                            | Targeting system with ground selection    |
| `GA_Berserk_SO.cs`                           | GrantedAbility demo (grants Execute)      |
| `GA_Execute_SO.cs`                           | Ability granted temporarily by buff       |
| `GA_ShieldOfLight_SO.cs`                     | OngoingTagRequirements conditional effect |
| `ExecCalc_Burn.cs`                           | Custom execution calculation for DoT      |
| `GameplayAbilityTargetActor_GroundSelect.cs` | Interactive targeting actor               |

---

## 🏷️ Tag Organization (GASSampleTags.cs)

Tags are the universal language of GAS. This sample uses a well-organized hierarchy:

```csharp
// Attributes
"Attribute.Primary.Attack"
"Attribute.Secondary.Health"

// States
"State.Dead"
"State.Burning"

// Debuffs
"Debuff.Burn"
"Debuff.Poison"

// Cooldowns
"Cooldown.Skill.Fireball"

// Abilities
"Ability.Fireball"

// GameplayCues
"GameplayCue.Fireball.Impact"
```

> **Tip**: Use the `[RegisterGameplayTagsFrom]` assembly attribute for automatic tag registration.

---

## 🎯 Key Learning Paths

### Path 1: Understanding Effects

1. View `GE_BaseAttributes_Hero.asset` (initial stats)
2. View `Fireball/GE_Fireball_Damage.asset` (instant damage)
3. View `DoT/GE_Burn_DoT.asset` (periodic damage)

### Path 2: Building Abilities

1. Read `GA_Fireball_SO.cs` (simple ability)
2. Read `GA_Purify_SO.cs` (effect removal)
3. Read `GA_ChainLightning_SO.cs` (complex targeting)

### Path 3: Character Setup

1. Read `Character.cs` (ASC initialization)
2. Read `CharacterAttributeSet.cs` (attribute definition)
3. Read `SampleCombatManager.cs` (ability activation)

### Path 4: Advanced Mechanics

1. Read `GA_ArmorStack_SO.cs` (effect stacking)
2. Read `GA_Berserk_SO.cs` + `GA_Execute_SO.cs` (granted abilities)
3. Read `GA_ShieldOfLight_SO.cs` (conditional effects via OngoingTagRequirements)

### Path 5: Performance Optimization

1. Read `GASPoolInitializer.cs` (pool tier configuration)
2. Use `GASPoolUtility.ConfigureXXX()` during initialization
3. Call `WarmAllPools()` during loading screens

---

## 💡 Best Practices Demonstrated

- **Tag-based ability lookup**: `TryActivateAbilityByTag()`
- **Data-driven effects**: All values in ScriptableObjects
- **Proper pooling**: `CreatePoolableInstance()` pattern
- **Damage mitigation**: `PreProcessInstantEffect()` override
- **Level-up system**: XP tracking with `PostGameplayEffectExecute()`
- **Effect stacking**: `EGameplayEffectStackingType.AggregateByTarget`
- **Granted abilities**: Temporary abilities via GameplayEffect
- **Conditional effects**: `OngoingTagRequirements` for state-based buffs
- **Pool prewarming**: `GASPoolUtility.WarmAllPools()` for 0-GC runtime
