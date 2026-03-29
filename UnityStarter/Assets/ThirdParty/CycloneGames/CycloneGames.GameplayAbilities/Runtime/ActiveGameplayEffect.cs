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

        /// <summary>
        /// True when the effect's OngoingTagRequirements are not met and its modifiers are temporarily suppressed.
        /// UE5: bIsInhibited / OnInhibitionChanged.
        /// </summary>
        public bool IsInhibited { get; internal set; }

        /// <summary>
        /// Fired when the inhibition state changes (true = now inhibited, false = no longer inhibited).
        /// UE5: OnInhibitionChanged delegate.
        /// </summary>
        public event Action<bool> OnInhibitionChanged;

        internal void NotifyInhibitionChanged(bool inhibited)
        {
            OnInhibitionChanged?.Invoke(inhibited);
        }

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
            IsInhibited = false;
            OnInhibitionChanged = null;
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

            // UE5: bExecutePeriodicEffectOnApplication
            // If true (default), first tick fires immediately (periodTimer = 0).
            // If false, first tick waits for the full period interval.
            if (cachedPeriod > 0)
            {
                periodTimer = spec.Def.ExecutePeriodicEffectOnApplication ? 0f : cachedPeriod;
            }
            else
            {
                periodTimer = -1f;
            }
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
        /// Removes a single stack. Used by EGameplayEffectStackingExpirationPolicy.RemoveSingleStackAndRefreshDuration.
        /// </summary>
        public void RemoveStack()
        {
            if (StackCount > 0)
            {
                StackCount--;
            }
        }

        /// <summary>
        /// Clears the expired flag so the effect can continue living (used after removing a single stack).
        /// </summary>
        public void ClearExpired()
        {
            IsExpired = false;
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

        /// <summary>
        /// Sets the remaining duration to a specific value.
        /// UE5: Section 4.5.16 - Changing Active Gameplay Effect Duration.
        /// </summary>
        public void SetRemainingDuration(float newDuration)
        {
            if (cachedDurationPolicy == EDurationPolicy.HasDuration)
            {
                TimeRemaining = System.Math.Max(0f, newDuration);
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
