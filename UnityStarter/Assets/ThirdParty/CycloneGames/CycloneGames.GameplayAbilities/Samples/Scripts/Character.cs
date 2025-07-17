using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.Logger;
using UnityEngine;

[RequireComponent(typeof(AbilitySystemComponentHolder))]
public class Character : MonoBehaviour
{
    private AbilitySystemComponentHolder ascHolder;
    public AbilitySystemComponent AbilitySystemComponent => ascHolder?.AbilitySystemComponent;
    public CharacterAttributeSet AttributeSet { get; private set; }

    [Header("Setup")]
    public List<GameplayAbilitySO> InitialAbilities;
    public GameplayEffectSO InitialAttributesEffect;
    public LevelingSystem LevelingData;

    // Runtime Stats
    private int experience = 0;

    void Awake()
    {
        ascHolder = GetComponent<AbilitySystemComponentHolder>();

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
        experience += amount;
        CLogger.LogInfo($"{name} gained {amount} XP. Total XP: {experience}");
        CheckForLevelUp();
    }

    private void CheckForLevelUp()
    {
        if (LevelingData == null) return;
        int currentLevel = (int)AttributeSet.GetCurrentValue(AttributeSet.Level);
        if (currentLevel >= LevelingData.Levels.Count)
        {
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
        ascHolder?.Tick(Time.deltaTime);
    }
}