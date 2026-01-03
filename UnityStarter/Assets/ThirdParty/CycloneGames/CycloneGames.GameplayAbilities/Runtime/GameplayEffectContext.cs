using CycloneGames.GameplayAbilities.Core;

namespace CycloneGames.GameplayAbilities.Runtime
{
    public interface IGameplayEffectContext
    {
        AbilitySystemComponent Instigator { get; }
        GameplayAbility AbilityInstance { get; }
        TargetData TargetData { get; }
        PredictionKey PredictionKey { get; set; }

        void AddInstigator(AbilitySystemComponent instigator, GameplayAbility abilityInstance);
        void AddTargetData(TargetData targetData);
        void Reset();
    }

    /// <summary>
    /// Context object carrying metadata about an effect's application.
    /// Pooled via GASPool for zero-allocation runtime.
    /// </summary>
    public class GameplayEffectContext : IGameplayEffectContext, IGASPoolable
    {
        public AbilitySystemComponent Instigator { get; private set; }
        public GameplayAbility AbilityInstance { get; private set; }
        public TargetData TargetData { get; private set; }
        public PredictionKey PredictionKey { get; set; }

        public GameplayEffectContext() { }

        #region IGASPoolable Implementation

        void IGASPoolable.OnGetFromPool()
        {
            // Initialization happens via AddInstigator/AddTargetData
        }

        void IGASPoolable.OnReturnToPool()
        {
            Reset();
        }

        #endregion

        public void AddInstigator(AbilitySystemComponent instigator, GameplayAbility abilityInstance)
        {
            Instigator = instigator;
            AbilityInstance = abilityInstance;
        }

        public void AddTargetData(TargetData data)
        {
            TargetData = data;
        }

        public void Reset()
        {
            Instigator = null;
            AbilityInstance = null;
            TargetData = null;
            PredictionKey = default;
        }

        /// <summary>
        /// Returns this context to the pool.
        /// </summary>
        public void ReturnToPool()
        {
            GASPool<GameplayEffectContext>.Shared.Return(this);
        }
    }
}