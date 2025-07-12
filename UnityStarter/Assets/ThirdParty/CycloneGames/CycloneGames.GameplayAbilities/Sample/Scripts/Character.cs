using System.Collections.Generic;
using CycloneGames.Factory.Runtime;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Runtime;
using CycloneGames.Logger;
using UnityEngine;

[RequireComponent(typeof(AbilitySystemComponent))]
public class Character : MonoBehaviour
{
    public AbilitySystemComponent AbilitySystemComponent { get; private set; }
    public CharacterAttributeSet AttributeSet { get; private set; }

    [Header("Setup")]
    public List<GameplayAbilitySO> InitialAbilities;
    public GameplayEffectSO InitialAttributesEffect;
    public LevelingSystem LevelingData;

    // Runtime Stats
    private int experience = 0;

    void Awake()
    {
        // For this sample, we manually create a factory.
        // In a real project, this would likely come from a DI container like VContainer or Zenject.
        var effectContextFactory = new GameplayEffectContextFactory();
        AbilitySystemComponent = new AbilitySystemComponent(effectContextFactory);

        // This is a common setup pattern.
        AbilitySystemComponent.InitAbilityActorInfo(this, gameObject);

        AttributeSet = new CharacterAttributeSet();
        AbilitySystemComponent.AddAttributeSet(AttributeSet);
    }

    void Start()
    {
        // Apply initial attributes and abilities on Start to ensure all systems are ready.
        ApplyInitialEffects();
        GrantInitialAbilities();
    }

    private void ApplyInitialEffects()
    {
        if (InitialAttributesEffect != null)
        {
            var ge = InitialAttributesEffect.CreateGameplayEffect();
            var spec = GameplayEffectSpec.Create(ge, AbilitySystemComponent);
            AbilitySystemComponent.ApplyGameplayEffectSpecToSelf(spec);
        }
    }

    private void GrantInitialAbilities()
    {
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
        experience += amount;
        CLogger.LogInfo($"{name} gained {amount} XP. Total XP: {experience}");
        CheckForLevelUp();
    }

    private void CheckForLevelUp()
    {
        int currentLevel = (int)AttributeSet.GetCurrentValue(AttributeSet.Level);
        if (LevelingData == null || currentLevel >= LevelingData.Levels.Count)
        {
            // Max level reached
            return;
        }

        LevelData currentLevelData = LevelingData.Levels[currentLevel - 1];
        if (experience >= currentLevelData.XpToNextLevel)
        {
            LevelUp(currentLevelData);
        }
    }

    private void LevelUp(LevelData gains)
    {
        // Create a dynamic, instant GE to grant the level-up bonuses.
        var mods = new List<ModifierInfo>
        {
            new ModifierInfo(AttributeSet.Level, EAttributeModifierOperation.Add, 1),
            new ModifierInfo(AttributeSet.MaxHealth, EAttributeModifierOperation.Add, gains.HealthGain),
            new ModifierInfo(AttributeSet.Health, EAttributeModifierOperation.Add, gains.HealthGain), // Also heal for the amount gained
            new ModifierInfo(AttributeSet.MaxMana, EAttributeModifierOperation.Add, gains.ManaGain),
            new ModifierInfo(AttributeSet.Mana, EAttributeModifierOperation.Add, gains.ManaGain),
            new ModifierInfo(AttributeSet.AttackPower, EAttributeModifierOperation.Add, gains.AttackGain),
            new ModifierInfo(AttributeSet.Defense, EAttributeModifierOperation.Add, gains.DefenseGain)
        };

        var levelUpEffect = new GameplayEffect("GE_LevelUp", EDurationPolicy.Instant, 0, mods);
        var spec = GameplayEffectSpec.Create(levelUpEffect, AbilitySystemComponent);
        AbilitySystemComponent.ApplyGameplayEffectSpecToSelf(spec);

        CLogger.LogWarning($"{name} has reached Level {AttributeSet.GetCurrentValue(AttributeSet.Level)}!");
    }

    void Update()
    {
        // The Tick needs to be manually called for the AbilitySystemComponent.
        AbilitySystemComponent?.Tick(Time.deltaTime, true); // Assuming this is server/single-player
    }
}