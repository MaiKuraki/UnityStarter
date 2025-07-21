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
        public LevelUpDataSO LevelUpData;

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

        public void AddExperience(int amount)
        {
            if (amount <= 0) return;
            experience += amount;
            CLogger.LogInfo($"{name} gained {amount} XP. Total XP: {experience}");
            CheckForLevelUp();
        }

        private void CheckForLevelUp()
        {
            if (LevelUpData == null) return;

            // Use a while loop to handle multiple level-ups from a single XP gain.
            bool leveledUp;
            do
            {
                leveledUp = false;
                int currentLevel = (int)AttributeSet.GetCurrentValue(AttributeSet.Level);

                // Check if the character is already at max level as defined by the LevelUpData.
                if (currentLevel >= LevelUpData.Levels.Count)
                {
                    return; // At max level
                }

                // LevelUpData is 0-indexed, but character level is 1-indexed.
                LevelData currentLevelData = LevelUpData.Levels[currentLevel - 1];
                if (experience >= currentLevelData.XpToNextLevel)
                {
                    LevelUp(currentLevelData);
                    leveledUp = true; // Mark that a level-up occurred to continue the loop.
                }
            } while (leveledUp);
        }

        private void LevelUp(LevelData gains)
        {
            int oldLevel = (int)AttributeSet.GetCurrentValue(AttributeSet.Level);
            int newLevel = oldLevel + 1;

            // We should subtract the XP cost for the level-up.
            // In many RPGs, XP resets to 0 or carries over. Here, we'll assume it carries over.
            experience -= gains.XpToNextLevel;

            // Create a dynamic, instant GE to grant the level-up bonuses.
            var mods = new List<ModifierInfo>
            {
                new ModifierInfo(AttributeSet.Level, EAttributeModifierOperation.Add, 1),
                new ModifierInfo(AttributeSet.MaxHealth, EAttributeModifierOperation.Add, gains.HealthGain),
                new ModifierInfo(AttributeSet.Health, EAttributeModifierOperation.Add, gains.HealthGain),
                new ModifierInfo(AttributeSet.MaxMana, EAttributeModifierOperation.Add, gains.ManaGain),
                new ModifierInfo(AttributeSet.Mana, EAttributeModifierOperation.Add, gains.ManaGain),
                new ModifierInfo(AttributeSet.AttackPower, EAttributeModifierOperation.Add, gains.AttackGain),
                new ModifierInfo(AttributeSet.Defense, EAttributeModifierOperation.Add, gains.DefenseGain)
            };

            var levelUpEffect = new GameplayEffect("GE_LevelUp", EDurationPolicy.Instant, 0, 0, mods,
                gameplayCues: new GameplayTagContainer { GASSampleTags.Event_Character_LeveledUp }); // Add a cue for level up VFX/SFX

            var spec = GameplayEffectSpec.Create(levelUpEffect, AbilitySystemComponent);
            AbilitySystemComponent.ApplyGameplayEffectSpecToSelf(spec);

            CLogger.LogWarning($"{name} has reached Level {newLevel}! (XP: {experience})");

            // Broadcast the level-up event for other systems to listen to.
            OnLeveledUp?.Invoke(newLevel);
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