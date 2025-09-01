using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Runtime;
using CycloneGames.Logger;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace CycloneGames.GameplayAbilities.Sample
{
    public class SampleCombatManager : MonoBehaviour
    {
        [Header("Characters")]
        public Character Player;
        public Character Enemy;

        [Header("Setup")]
        [Tooltip("A GameplayEffect used for debugging to grant XP to the player.")]
        public GameplayEffectSO DebugXpEffect;

        [Header("UI")]
        public Text PlayerStatusText;
        public Text EnemyStatusText;
        public Text LogText;
        private GameObject logTextGORef;

        private void Awake()
        {
            logTextGORef = LogText?.gameObject;
            if (LogText != null)
            {
                CLogger.Instance.AddLogger(new UILogger(UpdateLog, 7));
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
            if (Input.GetKeyDown(KeyCode.Alpha2)) TryActivateAbility(Player, 1); // Purify

            //  Enemy active PoisonBlade ability
            if (Input.GetKeyDown(KeyCode.E))
            {
                if (Enemy != null)
                {
                    CLogger.LogInfo("DEBUG: Forcing Enemy to cast ability.");
                    TryActivateAbility(Enemy, 0);
                }
            }

            if (Input.GetKeyDown(KeyCode.Space))
            {
                // Grant XP by applying a GameplayEffect
                if (Player != null && DebugXpEffect != null)
                {
                    var ge = DebugXpEffect.CreateGameplayEffect();
                    var spec = GameplayEffectSpec.Create(ge, Player.AbilitySystemComponent);
                    Player.AbilitySystemComponent.ApplyGameplayEffectSpecToSelf(spec);
                    CLogger.LogInfo("Granted debug XP to player.");
                }
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

        void UpdateLog(string message)
        {
            ForceRefreshLog(message).Forget();
        }

        async UniTask ForceRefreshLog(string messageStr)
        {
            await UniTask.SwitchToMainThread();
            if (LogText != null)
            {
                LogText.text = messageStr;
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
            int currentLevel = (int)set.GetCurrentValue(set.Level);
            statusBuilder.AppendLine($"LV: {currentLevel:F0}");
            statusBuilder.AppendLine($"HP: {set.GetCurrentValue(set.Health):F1} / {set.GetCurrentValue(set.MaxHealth):F1}");
            statusBuilder.AppendLine($"MP: {set.GetCurrentValue(set.Mana):F1} / {set.GetCurrentValue(set.MaxMana):F1}");

            // Build the EXP string with special handling for max level.
            string expString;
            var levelUpData = character.LevelUpData;
            if (levelUpData != null && levelUpData.Levels.Count > 0)
            {
                // The max level is the number of entries in the level data. e.g., 10 entries = max level 10.
                int maxLevel = levelUpData.Levels.Count;
                
                // The target XP for the current level.
                // We clamp the index to prevent errors if the level is somehow out of bounds.
                int targetExpIndex = Mathf.Clamp(currentLevel - 1, 0, levelUpData.Levels.Count - 1);
                int targetExp = levelUpData.Levels[targetExpIndex].XpToNextLevel;

                // If the character is at or above the max level, display a "full" bar, hiding the real overflow value.
                if (currentLevel >= maxLevel)
                {
                    expString = $"EXP: {targetExp} / {targetExp} (MAX)";
                }
                else
                {
                    expString = $"EXP: {set.GetCurrentValue(set.Experience):F1} / {targetExp}";
                }
            }
            else
            {
                // Fallback if no level up data is assigned.
                expString = $"EXP: {set.GetCurrentValue(set.Experience):F1}";
            }
            statusBuilder.AppendLine($"ATK: {set.GetCurrentValue(set.AttackPower):F1}   |   DEF: {set.GetCurrentValue(set.Defense):F1}  |   {expString}");

            statusBuilder.AppendLine("<b>Active Effects:</b>");
            bool hasEffects = false;
            if (asc.ActiveEffects != null && asc.ActiveEffects.Count > 0)
            {
                foreach (var activeEffect in asc.ActiveEffects)
                {
                    if (activeEffect.Spec.Def.GrantedTags.HasTag(GameplayTagManager.RequestTag("Debuff")))
                    {
                        hasEffects = true;
                        // Display Effect Name, Remaining Duration, and Stack Count
                        statusBuilder.Append($" - <color=red>{activeEffect.Spec.Def.Name}</color> ");
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
                    else
                    {
                        hasEffects = true;
                        statusBuilder.Append($" - {activeEffect.Spec.Def.Name} ");
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
                    if (tag.Name.Contains("Debuff"))
                    {
                        statusBuilder.AppendLine($" - <color=yellow>{tag.Name}</color>");
                    }
                    else if (tag.Name.Contains("Dead"))
                    {
                        statusBuilder.AppendLine($" - <color=red>{tag.Name}</color>");
                    }
                    else
                    {
                        statusBuilder.AppendLine($" - {tag.Name}");
                    }
                }
            }

            return statusBuilder.ToString();
        }
    }
}
