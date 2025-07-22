using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Runtime;
using CycloneGames.Logger;
using UnityEngine;
using UnityEngine.UI;

namespace CycloneGames.GameplayAbilities.Sample
{
    public class SampleCombatManager : MonoBehaviour
    {
        [Header("Characters")]
        public Character Player;
        public Character Enemy;

        [Header("UI")]
        public Text PlayerStatusText;
        public Text EnemyStatusText;
        public Text LogText;

        private void Awake()
        {
            CLogger.Instance.AddLogger(new UnityLogger());
            if (LogText != null)
            {
                CLogger.Instance.AddLogger(new UILogger(LogText, "[Game Log] ", 1));
            }
            else
            {
                Debug.LogWarning("SampleCombatManager: LogText is not assigned in the Inspector. UI logs will not be displayed.");
            }
        }

        private void Start()
        {
            // // Initialize the GameplayTagManager with defined tags.
            // var tagNames = new List<string>
            // {
            //     GASSampleTags.Attribute_Primary_Attack, GASSampleTags.Attribute_Primary_Defense,
            //     GASSampleTags.Attribute_Secondary_Health, GASSampleTags.Attribute_Secondary_MaxHealth,
            //     GASSampleTags.Attribute_Secondary_Mana, GASSampleTags.Attribute_Secondary_MaxMana,
            //     GASSampleTags.Attribute_Secondary_Speed, GASSampleTags.Attribute_Meta_Damage,
            //     GASSampleTags.State_Dead, GASSampleTags.State_Stunned,
            //     GASSampleTags.State_Burning, GASSampleTags.State_Poisoned,
            //     GASSampleTags.Debuff_Burn, GASSampleTags.Debuff_Poison,
            //     GASSampleTags.Cooldown_Fireball, GASSampleTags.Cooldown_PoisonBlade,
            //     GASSampleTags.Cooldown_Purify, GASSampleTags.Cooldown_ChainLightning,
            //     GASSampleTags.Event_Character_Death, GASSampleTags.Event_Character_LeveledUp,
            //     GASSampleTags.GameplayCue_Fireball_Impact, GASSampleTags.GameplayCue_Burn_Loop,
            //     GASSampleTags.GameplayCue_Poison_Impact, GASSampleTags.GameplayCue_Poison_Loop,
            //     GASSampleTags.GameplayCue_Purify_Effect, GASSampleTags.GameplayCue_Lightning_Impact
            // };
            // GameplayTagManager.RegisterDynamicTags(tagNames);
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

            // Use a StringBuilder for efficient string construction
            var statusBuilder = new System.Text.StringBuilder();

            statusBuilder.AppendLine($"<b>{character.name}</b>");
            statusBuilder.AppendLine($"LV: {set.GetCurrentValue(set.Level):F0}");
            statusBuilder.AppendLine($"HP: {set.GetCurrentValue(set.Health):F1} / {set.GetCurrentValue(set.MaxHealth):F1}");
            statusBuilder.AppendLine($"MP: {set.GetCurrentValue(set.Mana):F1} / {set.GetCurrentValue(set.MaxMana):F1}");
            statusBuilder.AppendLine($"ATK: {set.GetCurrentValue(set.AttackPower):F1} | DEF: {set.GetCurrentValue(set.Defense):F1}");

            // --- New Section for Active Effects ---
            statusBuilder.AppendLine("<b>Active Effects:</b>");
            bool hasEffects = false;
            if (asc.ActiveEffects != null && asc.ActiveEffects.Count > 0)
            {
                foreach (var activeEffect in asc.ActiveEffects)
                {
                    // We identify DoTs by checking for the 'Debuff' parent tag.
                    if (activeEffect.Spec.Def.GrantedTags.HasTag(GameplayTagManager.RequestTag("Debuff")))
                    {
                        hasEffects = true;
                        // Display Effect Name, Remaining Duration, and Stack Count
                        statusBuilder.Append($" - {activeEffect.Spec.Def.Name} ");
                        if (activeEffect.Spec.Def.DurationPolicy == EDurationPolicy.HasDuration)
                        {
                            statusBuilder.Append($"({activeEffect.TimeRemaining:F1}s) ");
                        }
                        if (activeEffect.StackCount > 1)
                        {
                            statusBuilder.Append($"[Stacks: {activeEffect.StackCount}]");
                        }
                        statusBuilder.AppendLine();
                    }
                }
            }

            if (!hasEffects)
            {
                statusBuilder.AppendLine(" - None");
            }
            // --- End New Section ---

            // Display all granted tags for debugging purposes
            statusBuilder.AppendLine("<b>Tags:</b>");
            if (asc.CombinedTags.IsEmpty)
            {
                statusBuilder.AppendLine(" - None");
            }
            else
            {
                foreach (var tag in asc.CombinedTags)
                {
                    statusBuilder.AppendLine($" - {tag.Name}");
                }
            }

            return statusBuilder.ToString();
        }
    }
}