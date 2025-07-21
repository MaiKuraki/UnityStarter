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
}