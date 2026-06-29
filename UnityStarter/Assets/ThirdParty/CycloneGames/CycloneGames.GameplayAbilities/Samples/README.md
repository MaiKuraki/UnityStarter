[English] | [Simplified Chinese](README.SCH.md)

# GameplayAbilities Samples

This folder contains a playable sample scene and authoring assets for `CycloneGames.GameplayAbilities`. The samples demonstrate how to connect an `AbilitySystemComponent`, attributes, GameplayTags, GameplayEffects, GameplayAbilities, GameplayCues, target actors, pooling, and startup helpers.

The sample project is learning material. Production projects should copy the relevant patterns into their own assemblies, replace scene lookups with project services, and add authority, validation, asset registry, and pooling rules that match the game.

## Asset Locations

| Content | Path | Purpose |
| --- | --- | --- |
| Scene | `Samples/SampleScene.unity` | Playable end-to-end scene with Player, Enemy, input, combat log, and configured sample assets. |
| Prefabs | `Samples/Prefabs/Player.prefab`, `Samples/Prefabs/Enemy.prefab` | Minimal actors that host sample character and ASC components. |
| Materials | `Samples/Materials/` | Simple visual material used by sample actors. |
| Ability and effect assets | `Samples/ScriptableObjects/` | Preconfigured ability, effect, cue, execution, DoT, poison, purify, passive, bounty, and level data assets. |
| Runtime sample scripts | `Samples/Scripts/` | Ability, attribute, target actor, setup, pooling, and UI logger examples. |
| Editor sample scripts | `Samples/Editor/` | Sample property drawer support for attribute name selection. |
| Preview media | `../Documents~/DemoPreview_1.gif`, `../Documents~/DemoPreview_2.gif` | README preview images for onboarding and documentation. |

## Quick Start

1. Open `UnityStarter/Assets/ThirdParty/CycloneGames/CycloneGames.GameplayAbilities/Samples/SampleScene.unity`.
2. Press Play in the Unity Editor.
3. Use the sample controls.

| Input | Action |
| --- | --- |
| `1` | Player casts Fireball, applying instant damage and burn. |
| `2` | Player casts Purify, removing poison-style debuffs from valid targets. |
| `E` | Enemy casts Poison Blade. |
| `Space` | Grants debug experience to exercise attribute and level-up hooks. |

Expected result: the UI log reports ability activation, effect application, damage, debuff removal, and level-up events. Console output should remain free of compile errors and missing script warnings.

## Learning Path

### Character And ASC Setup

Start with these scripts:

| Script | What To Learn |
| --- | --- |
| `Scripts/AbilitySystemComponentHolder.cs` | Hosting a pure C# `AbilitySystemComponent` from a `MonoBehaviour`. |
| `Scripts/Character.cs` | Actor initialization, initial attributes, initial passives, ability grants, bounty effect, and ASC ticking. |
| `Scripts/CharacterAttributeSet.cs` | Primary, secondary, and meta attributes; clamping; damage conversion; death and bounty hooks. |
| `Scripts/GASSampleTags.cs` | Centralized tag constants and runtime tag registration. |

### Effects And Attributes

Inspect these assets:

| Asset | What To Learn |
| --- | --- |
| `ScriptableObjects/GE_BaseAttributes_Hero.asset` | Initial player attributes through a GameplayEffect. |
| `ScriptableObjects/GE_BaseAttributes_Enemy.asset` | Initial enemy attributes through a GameplayEffect. |
| `ScriptableObjects/Fireball/GE_Fireball_Impact.asset` | Instant damage effect driven by Fireball. |
| `ScriptableObjects/DoT/GE_DoT_Burn.asset` | Periodic burn damage. |
| `ScriptableObjects/DoT/GE_DoT_Poison.asset` | Periodic poison damage. |
| `ScriptableObjects/GE_Passive_IncreaseDamage_10Percent.asset` | Passive attribute modifier pattern. |

### Ability Authoring

Read the ability scripts in this order:

| Script | What To Learn |
| --- | --- |
| `Scripts/GA_Fireball_SO.cs` | Cost, cooldown, instant damage, burn, SetByCaller magnitude, and sample target lookup. |
| `Scripts/GA_PoisonBlade_SO.cs` | Applying a debuff from an ability. |
| `Scripts/GA_Purify_SO.cs` | Removing active effects by tag and filtering targets. |
| `Scripts/GA_ArmorStack_SO.cs` | Stack behavior and stack debugging. |
| `Scripts/GA_Berserk_SO.cs` and `Scripts/GA_Execute_SO.cs` | Granted ability pattern. |
| `Scripts/GA_ShieldOfLight_SO.cs` | Defensive buff pattern using ongoing requirements. |
| `Scripts/GA_ChainLightning_SO.cs` | Multi-target ability flow with falloff. |
| `Scripts/GA_Meteor_SO.cs` | Target actor workflow and ground selection. |

### Targeting And AbilityTasks

| Script | What To Learn |
| --- | --- |
| `Scripts/AbilityTask_WaitTargetData_SpawnedActor.cs` | Spawning and binding a target actor from an ability task. |
| `Scripts/GameplayAbilityTargetActor_GroundSelect.cs` | Interactive ground targeting. |
| `Scripts/TargetActor/GameplayAbilityTargetActor_SingleLineTrace.cs` | Single line trace targeting. |
| `Scripts/TargetActor/GameplayAbilityTargetActor_SphereOverlap.cs` | Area targeting. |
| `Scripts/TargetActor/GameplayAbilityTargetActor_ConeTrace.cs` | Cone targeting. |

### Startup And Integration

| Script Or Assembly | What To Learn |
| --- | --- |
| `Scripts/GASPoolInitializer.cs` | Pool configuration and warmup before combat. |
| `Scripts/Integrate/Setup/GASManualSetup.cs` | Manual non-DI cue manager startup using `CycloneGames.AssetManagement`. |
| `Scripts/Integrate/Setup/GASServerSetup.cs` | Server/headless startup with `NullGameplayCueManager`. |
| `Scripts/Integrate/DI/VContainer/GASLifetimeScope.cs` | Optional VContainer composition. This file is isolated in `CycloneGames.GameplayAbilities.Sample.Integrations.VContainer` and compiles only when the VContainer package is present. |

## GameplayTag Layout

The sample tags are centralized in `Scripts/GASSampleTags.cs` and registered through `[RegisterGameplayTagsFrom]`.

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

Use the same hierarchy style for production content, but define project-owned tags in the project package or game assembly rather than editing sample tags.

## Package And UPM Notes

The repository keeps samples in `Samples/` so they are visible and runnable when CycloneGames modules are used directly under `Assets/ThirdParty`. The package manifest exposes the same folder through the `samples` entry:

```json
{
  "displayName": "Gameplay Ability Samples",
  "path": "Samples"
}
```

When building a distribution pipeline that requires hidden UPM sample folders, mirror this source folder into the release package's sample layout without changing the source scene, prefab, ScriptableObject, or `.meta` GUID ownership in this repository.

## Persistence

The samples do not write persistent player data, project settings, editor preferences, or runtime save files. Runtime objects created while the scene is playing are temporary and are destroyed when Play Mode exits. Preview media under `Documents~/` is documentation-only content.

## Validation

Use these checks after changing sample assets, scripts, asmdefs, or documentation:

1. Open `Samples/SampleScene.unity` and confirm there are no missing script warnings.
2. Press Play and exercise `1`, `2`, `E`, and `Space`.
3. Confirm the Console has no compile errors, missing assembly references, or missing asset references.
4. Run the GameplayAbilities EditMode tests from the Unity Test Runner.
5. For package distribution, verify `package.json` still exposes the sample path and the preview images still render from the root README.
