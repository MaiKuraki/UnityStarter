using CycloneGames.Factory.Runtime;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// The default factory for creating GameplayEffectContext objects.
    /// This implementation creates a new instance every time. For pooling, create a custom factory.
    /// 
    /// Note: you should implement your own Factory for your Project, this just a template.
    ///       AND, for GC optimization, you should implement CONTEXT POOL for your Project.
    /// 
    /// </summary>
    public class GameplayEffectContextFactory : IFactory<IGameplayEffectContext>
    {
        public IGameplayEffectContext Create()
        {
            return GameplayEffectContext.Get();
        }
    }
}