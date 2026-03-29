namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Interface for custom gameplay effect application requirements.
    /// UE5: UGameplayEffectCustomApplicationRequirement.
    /// 
    /// Implement this interface to define complex, code-driven conditions that determine
    /// whether a GameplayEffect can be applied to a target. This is checked during
    /// <see cref="AbilitySystemComponent.ApplyGameplayEffectSpecToSelf"/> AFTER tag-based checks pass.
    /// 
    /// Examples:
    /// - Only allow a heal if target health is below 50%
    /// - Apply a debuff only if the target doesn't have a specific buff active
    /// - Conditional stacking logic based on source attributes
    /// </summary>
    public interface ICustomApplicationRequirement
    {
        /// <summary>
        /// Determines if the effect can be applied to the target.
        /// </summary>
        /// <param name="spec">The effect spec being applied, containing source, target, and level info.</param>
        /// <param name="target">The target AbilitySystemComponent.</param>
        /// <returns>True if the effect can be applied, false to block application.</returns>
        bool CanApplyGameplayEffect(GameplayEffectSpec spec, AbilitySystemComponent target);
    }
}
