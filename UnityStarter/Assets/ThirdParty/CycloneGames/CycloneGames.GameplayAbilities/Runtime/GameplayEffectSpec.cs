using System.Collections.Generic;

namespace CycloneGames.GameplayAbilities.Runtime
{
    public class GameplayEffectSpec
    {
        private static readonly Stack<GameplayEffectSpec> pool = new Stack<GameplayEffectSpec>(32);
        public GameplayEffect Def { get; private set; }
        public AbilitySystemComponent Source { get; private set; }
        public AbilitySystemComponent Target { get; private set; }
        public IGameplayEffectContext Context { get; private set; }
        public int Level { get; private set; }
        public float Duration { get; private set; }
        private readonly Dictionary<ModifierInfo, float> calculatedMagnitudes = new Dictionary<ModifierInfo, float>();
        private GameplayEffectSpec() { } 
        public static GameplayEffectSpec Create(GameplayEffect def, AbilitySystemComponent source, int level = 1)
        {
            var spec = pool.Count > 0 ? pool.Pop() : new GameplayEffectSpec();
            spec.Def = def;
            spec.Source = source;
            spec.Level = level;
            spec.Duration = def.Duration;
            
            // Correctly call the public method on ASC to get a context
            spec.Context = source.MakeEffectContext(); 
            spec.Context.AddInstigator(source, null);
            
            foreach (var mod in def.Modifiers)
            {
                float magnitude;
                if (mod.CustomCalculation != null)
                {
                    magnitude = mod.CustomCalculation.CalculateMagnitude(spec);
                }
                else
                {
                    magnitude = mod.Magnitude.GetValueAtLevel(level);
                }
                spec.calculatedMagnitudes[mod] = magnitude;
            }
            return spec;
        }
        public void ReturnToPool()
        {
            if (Context is GameplayEffectContext pooledContext)
            {
                pooledContext.ReturnToPool();
            }
            Def = null;
            Source = null;
            Target = null;
            Context = null;
            Level = 0;
            Duration = 0;
            calculatedMagnitudes.Clear();
            pool.Push(this);
        }
        public float GetCalculatedMagnitude(ModifierInfo modifier)
        {
            return calculatedMagnitudes.TryGetValue(modifier, out var magnitude) ? magnitude : 0;
        }
        public void SetTarget(AbilitySystemComponent target)
        {
            Target = target;
        }
    }
}