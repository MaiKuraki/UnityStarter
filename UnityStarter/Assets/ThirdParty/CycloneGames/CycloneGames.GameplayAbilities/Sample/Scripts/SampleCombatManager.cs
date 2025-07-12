using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Runtime;
using CycloneGames.Logger;
using UnityEngine;
using UnityEngine.UI;

public class SampleCombatManager : MonoBehaviour
{
    [Header("Characters")]
    public Character Player;
    public Character Enemy;

    [Header("UI")]
    public Text PlayerStatusText;
    public Text EnemyStatusText;
    public Text LogText;

    private void Start()
    {
        // Initialize the GameplayTagManager with our defined tags.
        var tagNames = new List<string>
        {
            ProjectGameplayTags.Attribute_Primary_Attack, ProjectGameplayTags.Attribute_Primary_Defense,
            ProjectGameplayTags.Attribute_Secondary_Health, ProjectGameplayTags.Attribute_Secondary_MaxHealth,
            ProjectGameplayTags.Attribute_Secondary_Mana, ProjectGameplayTags.Attribute_Secondary_MaxMana,
            ProjectGameplayTags.Attribute_Secondary_Speed, ProjectGameplayTags.Attribute_Meta_Damage,
            ProjectGameplayTags.State_Dead, ProjectGameplayTags.State_Stunned,
            ProjectGameplayTags.State_Burning, ProjectGameplayTags.State_Poisoned,
            ProjectGameplayTags.Debuff_Burn, ProjectGameplayTags.Debuff_Poison,
            ProjectGameplayTags.Cooldown_Fireball, ProjectGameplayTags.Cooldown_PoisonBlade,
            ProjectGameplayTags.Cooldown_Purify, ProjectGameplayTags.Cooldown_ChainLightning,
            ProjectGameplayTags.Event_Character_Death, ProjectGameplayTags.Event_Character_LeveledUp,
            ProjectGameplayTags.GameplayCue_Fireball_Impact, ProjectGameplayTags.GameplayCue_Burn_Loop,
            ProjectGameplayTags.GameplayCue_Poison_Impact, ProjectGameplayTags.GameplayCue_Poison_Loop,
            ProjectGameplayTags.GameplayCue_Purify_Effect, ProjectGameplayTags.GameplayCue_Lightning_Impact
        };
        GameplayTagManager.RegisterDynamicTags(tagNames);

        // Setup a simple logger to display messages on screen.
        // CLogger.OnLog += (message, type) =>
        // {
        //     if (LogText != null) LogText.text = message;
        // };
    }

    void Update()
    {
        HandleInput();
        UpdateUI();
    }

    void HandleInput()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1)) TryActivateAbility(Player, 0); // Fireball
        if (Input.GetKeyDown(KeyCode.Alpha2)) TryActivateAbility(Player, 1); // Poison Blade
        if (Input.GetKeyDown(KeyCode.Alpha3)) TryActivateAbility(Player, 2); // Chain Lightning
        if (Input.GetKeyDown(KeyCode.Alpha4)) TryActivateAbility(Player, 3); // Purify

        if (Input.GetKeyDown(KeyCode.Space))
        {
            Player.AddExperience(50); // Give player XP
        }
    }

    void TryActivateAbility(Character character, int abilityIndex)
    {
        var abilities = character.AbilitySystemComponent.GetActivatableAbilities();
        if (abilityIndex < abilities.Count)
        {
            character.AbilitySystemComponent.TryActivateAbility(abilities[abilityIndex]);
        }
    }

    void UpdateUI()
    {
        if (Player != null && PlayerStatusText != null)
        {
            PlayerStatusText.text = GetCharacterStatus(Player);
        }
        if (Enemy != null && EnemyStatusText != null)
        {
            EnemyStatusText.text = GetCharacterStatus(Enemy);
        }
    }

    string GetCharacterStatus(Character character)
    {
        if (character == null) return "N/A";

        var asc = character.AbilitySystemComponent;
        var set = character.AttributeSet;

        string status = $"<b>{character.name}</b>\n" +
                        $"LV: {set.GetCurrentValue(set.Level):F0}\n" +
                        $"HP: {set.GetCurrentValue(set.Health):F1} / {set.GetCurrentValue(set.MaxHealth):F1}\n" +
                        $"MP: {set.GetCurrentValue(set.Mana):F1} / {set.GetCurrentValue(set.MaxMana):F1}\n" +
                        $"ATK: {set.GetCurrentValue(set.AttackPower):F1} | DEF: {set.GetCurrentValue(set.Defense):F1}\n" +
                        "<b>Tags:</b>\n";

        foreach (var tag in asc.CombinedTags)
        {
            status += $"{tag.Name}\n";
        }

        return status;
    }
}