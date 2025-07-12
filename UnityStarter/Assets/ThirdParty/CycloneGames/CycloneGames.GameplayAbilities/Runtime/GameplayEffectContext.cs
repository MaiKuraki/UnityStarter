using System.Collections.Generic;

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

    public class GameplayEffectContext : IGameplayEffectContext
    {
        private static readonly Stack<GameplayEffectContext> pool = new Stack<GameplayEffectContext>(64);

        public AbilitySystemComponent Instigator { get; private set; }
        public GameplayAbility AbilityInstance { get; private set; }
        public TargetData TargetData { get; private set; }
        public PredictionKey PredictionKey { get; set; }

        public GameplayEffectContext() { }

        internal static GameplayEffectContext Get()
        {
            return pool.Count > 0 ? pool.Pop() : new GameplayEffectContext();
        }

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

        public void ReturnToPool()
        {
            Reset();
            pool.Push(this);
        }
    }
}