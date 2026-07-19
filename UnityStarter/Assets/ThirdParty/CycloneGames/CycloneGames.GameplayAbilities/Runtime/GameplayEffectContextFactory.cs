namespace CycloneGames.GameplayAbilities.Runtime
{
    public interface IGameplayEffectContextFactory
    {
        GameplayEffectContext Create(AbilitySystemComponent owner);
    }

    /// <summary>
    /// The default factory for creating GameplayEffectContext objects.
    /// Uses the owner's runtime memory scope.
    /// </summary>
    public sealed class GameplayEffectContextFactory : IGameplayEffectContextFactory
    {
        public GameplayEffectContext Create(AbilitySystemComponent owner)
        {
            if (owner == null) throw new System.ArgumentNullException(nameof(owner));
            return owner.RuntimeContext.Memory.AcquireEffectContext();
        }
    }
}
