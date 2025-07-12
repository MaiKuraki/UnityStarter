using System;
using System.Collections.Generic;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Represents a stateful instance of a GameplayEffect that is currently active on an AbilitySystemComponent.
    /// It tracks state such as remaining time and stack count.
    /// Instances of this class should be acquired from an object pool.
    /// </summary>
    public class ActiveGameplayEffect
    {
        private static readonly Stack<ActiveGameplayEffect> pool = new Stack<ActiveGameplayEffect>(64);

        public GameplayEffectSpec Spec { get; private set; }
        public float TimeRemaining { get; private set; }
        public int StackCount { get; private set; }
        public bool IsExpired { get; private set; }

        private ActiveGameplayEffect() { }

        public static ActiveGameplayEffect Create(GameplayEffectSpec spec)
        {
            var activeEffect = pool.Count > 0 ? pool.Pop() : new ActiveGameplayEffect();
            activeEffect.Spec = spec;
            activeEffect.TimeRemaining = spec.Duration;
            activeEffect.StackCount = 1;
            activeEffect.IsExpired = false;
            return activeEffect;
        }

        public void ReturnToPool()
        {
            Spec?.ReturnToPool();
            Spec = null;
            TimeRemaining = 0;
            StackCount = 0;
            IsExpired = false;
            pool.Push(this);
        }

        /// <summary>
        /// Called when a new stack is successfully applied to this existing effect.
        /// </summary>
        public void OnStackApplied()
        {
            StackCount = Math.Min(StackCount + 1, Spec.Def.Stacking.Limit);

            if (Spec.Def.Stacking.DurationPolicy == EGameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication)
            {
                TimeRemaining = Spec.Duration;
            }
        }
        
        /// <summary>
        /// Ticks the effect's duration.
        /// </summary>
        /// <param name="deltaTime">The time since the last frame.</param>
        /// <returns>True if the effect expired this tick, false otherwise.</returns>
        public bool Tick(float deltaTime)
        {
            if (IsExpired || Spec.Def.DurationPolicy != EDurationPolicy.HasDuration)
            {
                return IsExpired;
            }

            TimeRemaining -= deltaTime;
            if (TimeRemaining <= 0)
            {
                IsExpired = true;
            }
            
            return IsExpired;
        }
    }
}