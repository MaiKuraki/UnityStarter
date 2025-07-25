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

        private float periodTimer;
        private ActiveGameplayEffect() { }

        public static ActiveGameplayEffect Create(GameplayEffectSpec spec)
        {
            var activeEffect = pool.Count > 0 ? pool.Pop() : new ActiveGameplayEffect();
            activeEffect.Spec = spec;
            activeEffect.TimeRemaining = spec.Duration;
            activeEffect.StackCount = 1;
            activeEffect.IsExpired = false;

            // Initialize the period timer. If the period is 0 or less, it's not a periodic effect.
            activeEffect.periodTimer = spec.Def.Period > 0 ? spec.Def.Period : -1f;

            return activeEffect;
        }

        public void ReturnToPool()
        {
            Spec?.ReturnToPool();
            Spec = null;
            TimeRemaining = 0;
            StackCount = 0;
            IsExpired = false;
            periodTimer = -1f;
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

            if (periodTimer > 0)
            {
                periodTimer = Spec.Def.Period;
            }
        }

        /// <summary>
        /// Ticks the effect's duration and period timer.
        /// </summary>
        /// <param name="deltaTime">The time since the last frame.</param>
        /// <param name="asc">The owning AbilitySystemComponent to execute periodic effects on.</param>
        /// <returns>True if the effect expired this tick, false otherwise.</returns>
        public bool Tick(float deltaTime, AbilitySystemComponent asc)
        {
            // --- Duration Handling ---
            if (!IsExpired && Spec.Def.DurationPolicy == EDurationPolicy.HasDuration)
            {
                TimeRemaining -= deltaTime;
                if (TimeRemaining <= 0)
                {
                    IsExpired = true;
                }
            }

            // --- Periodic Effect Handling ---
            if (!IsExpired && periodTimer > 0)
            {
                periodTimer -= deltaTime;
                if (periodTimer <= 0)
                {
                    // Period has elapsed, execute the effect's instant logic.
                    // Note: Periodic effect executions are not predicted in this model.
                    asc.ExecuteInstantEffect(this.Spec);

                    // Reset the timer for the next period.
                    periodTimer = Spec.Def.Period;
                }
            }

            return IsExpired;
        }
    }
}