using System;
using CycloneGames.GameplayTags.Core;

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
        public float TimeRemaining => (float)timeRemaining;
        public float PeriodTimeRemaining => (float)periodTimer;
        public int StackCount { get; private set; }
        public bool IsExpired { get; private set; }

        /// <summary>
        /// True when the effect's OngoingTagRequirements are not met and its modifiers are temporarily suppressed.
        /// UE5: bIsInhibited / OnInhibitionChanged.
        /// </summary>
        public bool IsInhibited { get; internal set; }

        /// <summary>
        /// Stable identifier assigned by the server when this effect is replicated.
        /// 0 means unassigned (local-only effect). Used by IGASNetworkBridge for effect removal messages.
        /// </summary>
        public int NetworkId { get; internal set; }

        /// <summary>
        /// Fired when the inhibition state changes (true = now inhibited, false = no longer inhibited).
        /// UE5: OnInhibitionChanged delegate.
        /// </summary>
        public event Action<bool> OnInhibitionChanged;

        internal void NotifyInhibitionChanged(bool inhibited)
        {
            OnInhibitionChanged?.Invoke(inhibited);
        }

        private double timeRemaining;
        private double periodTimer;
        private double cachedPeriod;
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
            timeRemaining = 0d;
            StackCount = 0;
            IsExpired = false;
            IsInhibited = false;
            NetworkId = 0;
            OnInhibitionChanged = null;
            periodTimer = -1d;
            cachedPeriod = 0d;
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
            timeRemaining = spec.Duration;
            StackCount = 1;
            IsExpired = false;

            cachedPeriod = spec.Def.Period;
            cachedDurationPolicy = spec.Def.DurationPolicy;

            // UE5: bExecutePeriodicEffectOnApplication
            // If true (default), first tick fires immediately (periodTimer = 0).
            // If false, first tick waits for the full period interval.
            if (cachedPeriod > 0)
            {
                periodTimer = spec.Def.ExecutePeriodicEffectOnApplication ? 0d : cachedPeriod;
            }
            else
            {
                periodTimer = -1d;
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
                timeRemaining = Spec.Duration;
            }

            if (periodTimer > 0d)
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
                timeRemaining = Spec.Duration;
            }

            if (periodTimer >= 0d)
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
                timeRemaining = System.Math.Max(0d, newDuration);
            }
        }

        public void SetReplicatedStackCount(int newStackCount)
        {
            int clampedStackCount = Math.Max(1, newStackCount);
            int limit = Spec?.Def?.Stacking.Limit ?? 0;
            if (limit > 0)
            {
                clampedStackCount = Math.Min(clampedStackCount, limit);
            }

            StackCount = clampedStackCount;
            IsExpired = false;
        }

        public void ApplyReplicatedState(
            int level,
            int stackCount,
            float duration,
            float timeRemaining,
            float periodTimeRemaining,
            GameplayTag[] setByCallerTags,
            float[] setByCallerValues,
            int setByCallerCount)
        {
            if (Spec == null || Spec.Def == null)
            {
                return;
            }

            Spec.ApplyReplicatedState(level, duration, setByCallerTags, setByCallerValues, setByCallerCount);
            SetReplicatedStackCount(stackCount);

            if (cachedDurationPolicy == EDurationPolicy.HasDuration)
            {
                this.timeRemaining = Math.Max(0d, timeRemaining);
            }

            if (cachedPeriod > 0d)
            {
                periodTimer = Math.Max(0d, Math.Min(periodTimeRemaining, cachedPeriod));
            }
            else
            {
                periodTimer = -1d;
            }

            IsExpired = false;
        }

        #endregion

        #region Tick

        /// <summary>
        /// Ticks the effect's duration and period timer.
        /// </summary>
        /// <returns>True if the effect expired this tick.</returns>
        public bool Tick(float deltaTime, AbilitySystemComponent asc)
        {
            double deltaTime64 = deltaTime;

            // Duration handling
            if (!IsExpired && cachedDurationPolicy == EDurationPolicy.HasDuration)
            {
                timeRemaining -= deltaTime64;
                if (timeRemaining <= 0d)
                {
                    timeRemaining = 0d;
                    IsExpired = true;
                }
            }

            // Periodic effect handling --skip entirely when inhibited (OngoingTagRequirements not met).
            // IsInhibited is kept current by AbilitySystemComponent.RecalculateDirtyAttributes(),
            // which runs at the START of each tick (before effects are ticked) when tags change.
            if (!IsExpired && !IsInhibited && cachedPeriod > 0d)
            {
                periodTimer -= deltaTime64;
                while (periodTimer <= 0d)
                {
                    asc.ExecuteInstantEffect(this.Spec);
                    // Carry over leftover time to prevent drift; the while loop ensures we
                    // fire once per elapsed period and leave periodTimer in [0, cachedPeriod).
                    periodTimer += cachedPeriod;
                    if (cachedPeriod <= 0d) break; // safety: avoid infinite loop if period is zero
                }
            }

            return IsExpired;
        }

        #endregion
    }
}