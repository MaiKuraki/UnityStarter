using CycloneGames.GameplayTags.Runtime;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Cooldown query API extension for AbilitySystemComponent.
    /// </summary>
    public partial class AbilitySystemComponent
    {
        #region Cooldown Query API
        
        /// <summary>
        /// Gets the remaining cooldown time for a specific ability.
        /// Returns 0 if the ability is not on cooldown.
        /// </summary>
        public float GetCooldownTimeRemaining(GameplayAbility ability)
        {
            if (ability?.CooldownEffectDefinition?.GrantedTags == null || ability.CooldownEffectDefinition.GrantedTags.IsEmpty)
                return 0f;

            float maxRemaining = 0f;
            foreach (var effect in activeEffects)
            {
                if (effect.Spec.Def.GrantedTags.HasAny(ability.CooldownEffectDefinition.GrantedTags))
                {
                    if (effect.TimeRemaining > maxRemaining)
                        maxRemaining = effect.TimeRemaining;
                }
            }
            return maxRemaining;
        }
        
        /// <summary>
        /// Gets detailed cooldown information for an ability.
        /// </summary>
        /// <param name="ability">The ability to check cooldown for.</param>
        /// <param name="timeRemaining">Output: Remaining cooldown time in seconds.</param>
        /// <param name="totalDuration">Output: Total cooldown duration.</param>
        /// <returns>True if the ability is on cooldown, false otherwise.</returns>
        public bool GetCooldownInfo(GameplayAbility ability, out float timeRemaining, out float totalDuration)
        {
            timeRemaining = 0f;
            totalDuration = 0f;
            
            if (ability?.CooldownEffectDefinition?.GrantedTags == null || ability.CooldownEffectDefinition.GrantedTags.IsEmpty)
                return false;

            foreach (var effect in activeEffects)
            {
                if (effect.Spec.Def.GrantedTags.HasAny(ability.CooldownEffectDefinition.GrantedTags))
                {
                    if (effect.TimeRemaining > timeRemaining)
                    {
                        timeRemaining = effect.TimeRemaining;
                        totalDuration = effect.Spec.Def.Duration;
                    }
                }
            }
            return timeRemaining > 0f;
        }
        
        /// <summary>
        /// Gets the remaining time of the cooldown effect that grants the specified tag.
        /// </summary>
        public float GetCooldownTimeRemainingByTag(GameplayTag cooldownTag)
        {
            if (cooldownTag.IsNone) return 0f;
            
            foreach (var effect in activeEffects)
            {
                if (effect.Spec.Def.GrantedTags.HasTag(cooldownTag))
                    return effect.TimeRemaining;
            }
            return 0f;
        }
        
        /// <summary>
        /// Checks if the owner currently has a cooldown tag present.
        /// </summary>
        public bool IsOnCooldown(GameplayTag cooldownTag)
        {
            return !cooldownTag.IsNone && CombinedTags.HasTag(cooldownTag);
        }
        
        /// <summary>
        /// Checks if an ability is currently on cooldown.
        /// </summary>
        public bool IsAbilityOnCooldown(GameplayAbility ability)
        {
            if (ability?.CooldownEffectDefinition?.GrantedTags == null || ability.CooldownEffectDefinition.GrantedTags.IsEmpty)
                return false;
            return CombinedTags.HasAny(ability.CooldownEffectDefinition.GrantedTags);
        }
        
        #endregion
    }
}
