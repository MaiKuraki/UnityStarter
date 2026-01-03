using System;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Represents a stateful instance of a GameplayEffect currently active on an ASC.
    /// Tracks state such as remaining time and stack count.
    /// Pooled via GASPool for zero-allocation runtime.
    /// </summary>
    public class ActiveGameplayEffect : IGASPoolable
    {
        public GameplayEffectSpec Spec { get; private set; }
        public float TimeRemaining { get; private set; }
        public int StackCount { get; private set; }
        public bool IsExpired { get; private set; }

        private float periodTimer;
        private float cachedPeriod;
        private EDurationPolicy cachedDurationPolicy;

        public ActiveGameplayEffect() { }

        #region IGASPoolable Implementation

        void IGASPoolable.OnGetFromPool()
        {
            // Initialization happens in Initialize()
        }

        void IGASPoolable.OnReturnToPool()
        {
            Spec?.ReturnToPool();
            Spec = null;
            TimeRemaining = 0;
            StackCount = 0;
            IsExpired = false;
            periodTimer = -1f;
            cachedPeriod = 0;
            cachedDurationPolicy = default;
        }

        #endregion

        #region Factory

        public static ActiveGameplayEffect Create(GameplayEffectSpec spec)
        {
            var activeEffect = GASPool<ActiveGameplayEffect>.Shared.Get();
            activeEffect.Initialize(spec);
            return activeEffect;
        }

        private void Initialize(GameplayEffectSpec spec)
        {
            Spec = spec;
            TimeRemaining = spec.Duration;
            StackCount = 1;
            IsExpired = false;

            cachedPeriod = spec.Def.Period;
            cachedDurationPolicy = spec.Def.DurationPolicy;

            // First tick executes immediately for periodic effects
            periodTimer = cachedPeriod > 0 ? 0f : -1f;
        }

        public void ReturnToPool()
        {
            GASPool<ActiveGameplayEffect>.Shared.Return(this);
        }

        #endregion

        #region Stacking

        /// <summary>
        /// Called when a new stack is successfully applied.
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
                periodTimer = cachedPeriod;
            }
        }

        /// <summary>
        /// Refreshes duration without modifying stack count.
        /// </summary>
        public void RefreshDurationAndPeriod()
        {
            if (cachedDurationPolicy == EDurationPolicy.HasDuration)
            {
                TimeRemaining = Spec.Duration;
            }

            if (periodTimer >= 0)
            {
                periodTimer = cachedPeriod;
            }
        }

        #endregion

        #region Tick

        /// <summary>
        /// Ticks the effect's duration and period timer.
        /// </summary>
        /// <returns>True if the effect expired this tick.</returns>
        public bool Tick(float deltaTime, AbilitySystemComponent asc)
        {
            // Duration handling
            if (!IsExpired && cachedDurationPolicy == EDurationPolicy.HasDuration)
            {
                TimeRemaining -= deltaTime;
                if (TimeRemaining <= 0)
                {
                    IsExpired = true;
                }
            }

            // Periodic effect handling
            if (!IsExpired && periodTimer >= 0)
            {
                periodTimer -= deltaTime;
                if (periodTimer <= 0)
                {
                    asc.ExecuteInstantEffect(this.Spec);
                    // Carry over leftover time to prevent drift
                    periodTimer += cachedPeriod;
                }
            }

            return IsExpired;
        }

        #endregion
    }
}
