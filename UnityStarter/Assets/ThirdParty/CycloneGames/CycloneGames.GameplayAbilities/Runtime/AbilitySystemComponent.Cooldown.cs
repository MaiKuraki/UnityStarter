using CycloneGames.GameplayTags.Core;

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
        /// O(1) tag lookup + O(k) scan over effects that explicitly grant matching cooldown tags.
        /// </summary>
        public float GetCooldownTimeRemaining(GameplayAbility ability)
        {
            if (ability?.CooldownGrantedTagsSnapshot == null || ability.CooldownGrantedTagsSnapshot.IsEmpty)
                return 0f;

            float maxRemaining = 0f;
            var indices = ability.CooldownGrantedTagsSnapshot.GetExplicitIndices();
            for (int i = 0; i < indices.Length; i++)
            {
                if (!grantedTagIndexToEffects.TryGetValue(indices[i], out var effects))
                    continue;

                for (int j = 0; j < effects.Count; j++)
                {
                    var effect = effects[j];
                    if (!effect.IsExpired && effect.TimeRemaining > maxRemaining)
                        maxRemaining = effect.TimeRemaining;
                }
            }
            return maxRemaining;
        }

        /// <summary>
        /// Gets detailed cooldown information for an ability.
        /// O(1) tag lookup + O(k) scan over effects that explicitly grant matching cooldown tags.
        /// </summary>
        /// <param name="ability">The ability to check cooldown for.</param>
        /// <param name="timeRemaining">Output: Remaining cooldown time in seconds.</param>
        /// <param name="totalDuration">Output: Total cooldown duration.</param>
        /// <returns>True if the ability is on cooldown, false otherwise.</returns>
        public bool GetCooldownInfo(GameplayAbility ability, out float timeRemaining, out float totalDuration)
        {
            timeRemaining = 0f;
            totalDuration = 0f;

            if (ability?.CooldownGrantedTagsSnapshot == null || ability.CooldownGrantedTagsSnapshot.IsEmpty)
                return false;

            var indices = ability.CooldownGrantedTagsSnapshot.GetExplicitIndices();
            for (int i = 0; i < indices.Length; i++)
            {
                if (!grantedTagIndexToEffects.TryGetValue(indices[i], out var effects))
                    continue;

                for (int j = 0; j < effects.Count; j++)
                {
                    var effect = effects[j];
                    if (!effect.IsExpired && effect.TimeRemaining > timeRemaining)
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
        /// O(1) tag lookup + O(k) scan over effects that explicitly grant the tag.
        /// </summary>
        public float GetCooldownTimeRemainingByTag(GameplayTag cooldownTag)
        {
            if (cooldownTag.IsNone) return 0f;
            if (!grantedTagIndexToEffects.TryGetValue(cooldownTag.RuntimeIndex, out var effects))
                return 0f;

            float maxRemaining = 0f;
            for (int i = 0; i < effects.Count; i++)
            {
                var effect = effects[i];
                if (!effect.IsExpired && effect.TimeRemaining > maxRemaining)
                    maxRemaining = effect.TimeRemaining;
            }

            return maxRemaining;
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
            if (ability?.CooldownGrantedTagsSnapshot == null || ability.CooldownGrantedTagsSnapshot.IsEmpty)
                return false;
            return HasAnyMatchingGameplayTagsExact(ability.CooldownGrantedTagsSnapshot);
        }

        #endregion
    }
}
