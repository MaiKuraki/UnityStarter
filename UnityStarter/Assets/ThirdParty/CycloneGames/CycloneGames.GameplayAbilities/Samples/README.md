[English] | [Simplified Chinese](README.SCH.md)

# GameplayAbilities Samples

This folder contains a playable sample scene and authoring assets for `CycloneGames.GameplayAbilities`. The samples demonstrate how to connect an `AbilitySystemComponent`, attributes, GameplayTags, GameplayEffects, GameplayAbilities, GameplayCues, target actors, one-shot runtime leases, bounded cue pooling, and startup helpers.

The sample project is learning material. Production projects should copy the relevant patterns into their own assemblies, replace scene lookups with project services, and add authority, validation, asset registry, runtime lease/cache, and cue-pool rules that match the game.

## Asset Locations

| Content | Path | Purpose |
| --- | --- | --- |
| Scene | `Samples/SampleScene.unity` | Playable end-to-end scene with Player, Enemy, input, combat log, and configured sample assets. |
| Prefabs | `Samples/Prefabs/Player.prefab`, `Samples/Prefabs/Enemy.prefab` | Minimal actors that host sample character and ASC components. |
| Materials | `Samples/Materials/` | Simple visual material used by sample actors. |
| Ability and effect assets | `Samples/ScriptableObjects/` | Preconfigured ability, effect, cue, execution, DoT, poison, purify, passive, bounty, and level data assets. |
| Runtime sample scripts | `Samples/Scripts/` | Ability, attribute, target actor, setup, cue pooling, and UI logger examples. |
| Editor sample scripts | `Samples/Editor/` | Sample property drawer support and Play Mode diagnostics controls for the ASC holder Inspector. |
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
| `F` | Toggles independent runtime diagnostics panels for Player and Enemy. |
| `Space` | Grants debug experience to exercise attribute and level-up hooks. |

Expected result: the UI log reports ability activation, effect application, damage, debuff removal, and level-up events. Pressing `F` shows both `Player [ASC]` and `Enemy [ASC]` panels. Console output should remain free of compile errors and missing script warnings.

## Runtime Diagnostics Overlay

`SampleCombatManager` registers the Player and Enemy ASCs explicitly when the diagnostics button or `F` is pressed. You can also select either actor's `AbilitySystemComponentHolder` during Play Mode and use its **GAS Runtime Overlay** Inspector section. Multi-select Player and Enemy to add, update, or remove both hosted ASCs in one operation. The Inspector reports the selected live and registered counts, the global bounded count and capacity, and current visibility.

Inspector controls are transient commands, not serialized per-ASC flags. They do not create Prefab overrides, own or dispose an ASC, call `ClearTargets`, remove unselected ASCs, or destroy the overlay singleton. The registry contains one shared entry per ASC, so an Inspector command changes the selected ASC's entry even when another caller registered it. Registration is non-owning, does not scan the scene, and does not change ASC lifetime. `SampleCombatManager` removes the Player and Enemy entries during shutdown and destroys the overlay singleton only when it created the singleton and no other registrations remain.

Use `GASOverlayConfig.MaxPanels` to set the bounded panel capacity before the overlay initializes. The value defaults to 8, is clamped to 1 through 32, and is fixed for that overlay instance. Runtime IMGUI diagnostics run on the Unity main thread and are intended for development and explicitly configured support builds, not gameplay hot paths.

## Recommended Reading Order

### Character And ASC Setup

Start with these scripts:

| Script | What To Learn |
| --- | --- |
| `Scripts/AbilitySystemComponentHolder.cs` | Hosting a pure C# `AbilitySystemComponent` from a `MonoBehaviour`. |
| `Editor/AbilitySystemComponentHolderEditor.cs` | Exposing explicit, multi-object, Play Mode-only diagnostics commands without serializing debug state. |
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

Every sample `CreateGameplayAbility()` constructs the derived ability from its immutable inputs, then calls `InitializeAbility(ability)` exactly once. Each `CreateRuntimeInstance()` reconstructs only those derived inputs; the Runtime copies sealed base Ability configuration from the definition. Every runtime instance is a one-shot lease and is discarded after its owner releases it. The Poison Blade and Purify assets use `InstancedPerActor`, and Runtime sample assets must not select `NonInstanced`.

Purify and Shockwave copy their faction-filter tag containers at the derived constructor boundary. Editing a ScriptableObject container or a source container after `GetGameplayAbility()` has published the cached definition therefore does not alter that definition or a runtime instance's filter state.

Read the ability scripts in this order:

| Script | What To Learn |
| --- | --- |
| `Scripts/GA_Fireball_SO.cs` | Cost, cooldown, instant damage, burn, SetByCaller magnitude, and sample target lookup. |
| `Scripts/GA_PoisonBlade_SO.cs` | Applying a debuff from an ability. |
| `Scripts/GA_Purify_SO.cs` | Removing active effects by tag, filtering targets, and isolating constructor tag inputs. |
| `Scripts/GA_ArmorStack_SO.cs` | Stack behavior and stack debugging. |
| `Scripts/GA_Berserk_SO.cs` and `Scripts/GA_Execute_SO.cs` | Granted ability pattern. |
| `Scripts/GA_ShieldOfLight_SO.cs` | Defensive buff pattern using ongoing requirements. |
| `Scripts/GA_ChainLightning_SO.cs` | Multi-target ability flow with falloff. |
| `Scripts/GA_Meteor_SO.cs` | Target actor workflow and ground selection. |
| `Scripts/GA_Shockwave_SO.cs` | Area damage with isolated required/forbidden faction tag filters. |

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
| `Scripts/SampleCombatManager.cs` | Scene-owned `GASRuntimeContext` composition, shared ASC initialization, and reverse-order shutdown. |
| `Scripts/Integrate/Setup/GASManualSetup.cs` | Manual non-DI cue manager startup using `CycloneGames.AssetManagement`, with an optional runtime backing-cache profile. |
| `Scripts/Integrate/Setup/GASServerSetup.cs` | Server/headless startup with `NullGameplayCueManager` and an optional runtime backing-cache profile. |
| `Scripts/Integrate/DI/VContainer/GASLifetimeScope.cs` | Optional VContainer composition. This file is isolated in `CycloneGames.GameplayAbilities.Sample.Integrations.VContainer` and compiles only when the VContainer package is present. |

Hardware profiles can pass a bounded EffectSpec backing-cache policy into either explicit setup helper. Omitting it uses the context default:

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

Samples live in `Samples/` so they remain visible and runnable when CycloneGames modules are embedded directly under `Assets/ThirdParty`. The package manifest exposes the same folder through the `samples` entry:

```json
{
  "displayName": "Gameplay Ability Samples",
  "path": "Samples"
}
```

When a distribution pipeline requires hidden UPM sample folders, mirror this source folder into the release package's sample layout without changing the source scene, prefab, ScriptableObject, or `.meta` GUID ownership rooted in `Samples/`.

## Persistence

The samples do not write persistent player data, project settings, editor preferences, or runtime save files. Runtime objects created while the scene is playing are temporary and are destroyed when Play Mode exits. Preview media under `Documents~/` is documentation-only content.

## Validation

Use these checks after changing sample assets, scripts, asmdefs, or documentation:

1. Open `Samples/SampleScene.unity` and confirm there are no missing script warnings.
2. Press Play and exercise `1`, `2`, `E`, `F`, and `Space`; confirm `F` displays both Player and Enemy panels.
3. Confirm the Console has no compile errors, missing assembly references, or missing asset references.
4. Run the GameplayAbilities EditMode tests and `CycloneGames.GameplayAbilities.Tests.PlayMode` from the Unity Test Runner.
5. For package distribution, verify `package.json` still exposes the sample path and the preview images still render from the root README.
6. Confirm each sample `CreateGameplayAbility()` calls `InitializeAbility` once, each `CreateRuntimeInstance()` constructs only derived inputs, and the Poison Blade and Purify assets remain on a supported instanced policy.
