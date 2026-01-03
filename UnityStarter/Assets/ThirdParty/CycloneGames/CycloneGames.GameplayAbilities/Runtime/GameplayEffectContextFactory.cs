using CycloneGames.Factory.Runtime;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// The default factory for creating GameplayEffectContext objects.
    /// Uses GASPool for zero-allocation pooling.
    /// </summary>
    public class GameplayEffectContextFactory : IFactory<IGameplayEffectContext>
    {
        public IGameplayEffectContext Create()
        {
            return GASPool<GameplayEffectContext>.Shared.Get();
        }
    }
}