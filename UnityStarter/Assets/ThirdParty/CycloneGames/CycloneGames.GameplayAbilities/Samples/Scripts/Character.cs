using System;
using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Runtime;
using CycloneGames.Logger;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Sample
{
    [RequireComponent(typeof(AbilitySystemComponentHolder))]
    public class Character : MonoBehaviour
    {
        private AbilitySystemComponentHolder ascHolder;
        public AbilitySystemComponent AbilitySystemComponent => ascHolder?.AbilitySystemComponent;
        public CharacterAttributeSet AttributeSet { get; private set; }
        public event Action<int> OnLeveledUp;

        [Header("Setup")]
        public List<GameplayAbilitySO> InitialAbilities;
        public GameplayEffectSO InitialAttributesEffect;
        public List<GameplayEffectSO> InitialPassiveEffects;
        public LevelUpDataSO LevelUpData;

        [Header("Faction")]
        // A character can now belong to multiple factions, e.g., "Faction.Player", "Faction.Team.Blue".
        [Tooltip("Tags that define this character's faction, team, or allegiances.")]
        public GameplayTagContainer FactionTags;

        [Header("Bounty")]
        [Tooltip("The GameplayEffect to grant to the killer when this character dies.")]
        public GameplayEffectSO BountyEffect;   // TODO: maybe create a new class for EnemyCharacter?

        // Runtime Stats
        private int experience = 0;

        void Awake()
        {
            ascHolder = GetComponent<AbilitySystemComponentHolder>();
        }

        void Start()
        {
            // This is a common setup pattern.
            AbilitySystemComponent.InitAbilityActorInfo(this, gameObject);

            AttributeSet = new CharacterAttributeSet();
            AbilitySystemComponent.AddAttributeSet(AttributeSet);

            // Apply initial attributes and abilities on Start to ensure all systems are ready.
            ApplyInitialEffects();
            GrantInitialAbilities();
        }

        private void ApplyInitialEffects()
        {
            if (InitialAttributesEffect != null && AbilitySystemComponent != null)
            {
                var ge = InitialAttributesEffect.CreateGameplayEffect();
                var spec = GameplayEffectSpec.Create(ge, AbilitySystemComponent);
                AbilitySystemComponent.ApplyGameplayEffectSpecToSelf(spec);
            }

            if (InitialPassiveEffects != null)
            {
                foreach (var passiveEffectSO in InitialPassiveEffects)
                {
                    if (passiveEffectSO != null)
                    {
                        var ge = passiveEffectSO.CreateGameplayEffect();
                        var spec = GameplayEffectSpec.Create(ge, AbilitySystemComponent);
                        AbilitySystemComponent.ApplyGameplayEffectSpecToSelf(spec);
                    }
                }
            }
        }

        private void GrantInitialAbilities()
        {
            if (AbilitySystemComponent == null) return;
            foreach (var abilitySO in InitialAbilities)
            {
                if (abilitySO != null)
                {
                    AbilitySystemComponent.GrantAbility(abilitySO.CreateAbility());
                }
            }
        }

        public void CheckForLevelUp()
        {
            if (LevelUpData == null) return;

            int initialLevel = (int)AttributeSet.GetCurrentValue(AttributeSet.Level);
            int currentXP = (int)AttributeSet.GetCurrentValue(AttributeSet.Experience);

            int levelsGained = 0;
            int xpCostTotal = 0;

            float healthGain = 0;
            float manaGain = 0;
            float attackGain = 0;
            float defenseGain = 0;

            int tempLevelTracker = initialLevel;

            while (tempLevelTracker < LevelUpData.Levels.Count && currentXP >= LevelUpData.Levels[tempLevelTracker - 1].XpToNextLevel)
            {
                LevelData levelData = LevelUpData.Levels[tempLevelTracker - 1];

                currentXP -= levelData.XpToNextLevel;
                xpCostTotal += levelData.XpToNextLevel;

                levelsGained++;
                tempLevelTracker++;

                healthGain += levelData.HealthGain;
                manaGain += levelData.ManaGain;
                attackGain += levelData.AttackGain;
                defenseGain += levelData.DefenseGain;
            }

            if (levelsGained > 0)
            {
                int finalLevel = initialLevel + levelsGained;
                CLogger.LogInfo($"{name} gained {levelsGained} level(s)! Reached level {finalLevel}.");

                var mods = new List<ModifierInfo>
                {
                    new ModifierInfo(AttributeSet.Experience, EAttributeModifierOperation.Add, -xpCostTotal),
                    new ModifierInfo(AttributeSet.Level, EAttributeModifierOperation.Add, levelsGained),
                    new ModifierInfo(AttributeSet.MaxHealth, EAttributeModifierOperation.Add, healthGain),
                    new ModifierInfo(AttributeSet.Health, EAttributeModifierOperation.Add, healthGain),
                    new ModifierInfo(AttributeSet.MaxMana, EAttributeModifierOperation.Add, manaGain),
                    new ModifierInfo(AttributeSet.Mana, EAttributeModifierOperation.Add, manaGain),
                    new ModifierInfo(AttributeSet.AttackPower, EAttributeModifierOperation.Add, attackGain),
                    new ModifierInfo(AttributeSet.Defense, EAttributeModifierOperation.Add, defenseGain)
                };

                var levelUpEffect = new GameplayEffect($"GE_MultiLevelUp_ToLvl{finalLevel}", EDurationPolicy.Instant, 0, 0, mods,
                    gameplayCues: new GameplayTagContainer { GASSampleTags.Event_Character_LeveledUp });

                var spec = GameplayEffectSpec.Create(levelUpEffect, AbilitySystemComponent);
                AbilitySystemComponent.ApplyGameplayEffectSpecToSelf(spec);

                OnLeveledUp?.Invoke(finalLevel);
            }
        }

        /// <summary>
        /// Grants this character's bounty to the specified killer.
        /// </summary>
        /// <param name="killerASC">The AbilitySystemComponent of the character who gets the bounty.</param>
        public void GrantBountyTo(AbilitySystemComponent killerASC)
        {
            if (BountyEffect == null || killerASC == null)
            {
                return;
            }

            var ge = BountyEffect.CreateGameplayEffect();
            var spec = GameplayEffectSpec.Create(ge, this.AbilitySystemComponent); // Source is the dead character

            // Apply the bounty effect to the killer
            killerASC.ApplyGameplayEffectSpecToSelf(spec);
            CLogger.LogInfo($"{killerASC.OwnerActor} received bounty from {this.name}.");
        }

        void Update()
        {
            // The Tick needs to be manually called for the AbilitySystemComponent.
            ascHolder?.Tick(Time.deltaTime);
        }
    }
}