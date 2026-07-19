using System;
using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Represents a stateful instance of a GameplayEffect currently active on an ASC.
    /// Tracks state such as remaining time and stack count.
    /// Released instances are invalidated and discarded.
    /// </summary>
    public class ActiveGameplayEffect : IGASLeasedObject
    {
        private const int MaxInhibitionObservers = 64;
        private GASRuntimeMemory memoryOwner;
        private bool leaseActive;
        private bool leaseEverAcquired;
        private List<Action<bool>> inhibitionObservers;
        public GameplayEffectSpec Spec { get; private set; }
        public float TimeRemaining => GASFixedValue.FromRaw(TimeRemainingRaw).ToFloat();
        public float PeriodTimeRemaining => periodTimerRaw >= 0L ? GASFixedValue.FromRaw(periodTimerRaw).ToFloat() : -1f;
        public long TimeRemainingRaw => timeRemainingRaw;
        public long PeriodTimeRemainingRaw => periodTimerRaw;
        public int StackCount { get; private set; }
        public bool IsExpired { get; private set; }

        /// <summary>
        /// True when the effect's OngoingTagRequirements are not met and its modifiers are temporarily suppressed.
        /// UE5: bIsInhibited / OnInhibitionChanged.
        /// </summary>
        public bool IsInhibited { get; internal set; }

        /// <summary>
        /// ASC-owned process-local reconciliation identifier.
        /// It is assigned monotonically for the effect lifetime and is never a stable wire identity.
        /// </summary>
        public int ReconciliationId { get; internal set; }

        /// <summary>
        /// Process-local handle of the live source ability spec, or 0 when the effect was not
        /// created by an ability. The handle is retained without keeping the ability instance alive.
        /// </summary>
        public int SourceAbilitySpecHandle { get; private set; }

        /// <summary>
        /// Execution policy captured from the source ability before the effect releases its borrowed
        /// ability-instance reference. It remains Invalid when the effect did not originate from an ability.
        /// </summary>
        public EAbilityExecutionPolicy SourceAbilityExecutionPolicy { get; private set; }

        /// <summary>
        /// Fired when the inhibition state changes (true = now inhibited, false = no longer inhibited).
        /// UE5: OnInhibitionChanged delegate.
        /// </summary>
        public event Action<bool> OnInhibitionChanged
        {
            add
            {
                if (value == null) return;
                inhibitionObservers ??= new List<Action<bool>>(2);
                if (inhibitionObservers.Count >= MaxInhibitionObservers)
                {
                    throw new InvalidOperationException(
                        $"ActiveGameplayEffect supports at most {MaxInhibitionObservers} inhibition observers.");
                }
                inhibitionObservers.Add(value);
            }
            remove
            {
                if (value == null || inhibitionObservers == null) return;
                inhibitionObservers.Remove(value);
            }
        }

        internal void NotifyInhibitionChanged(bool inhibited)
        {
            if (inhibitionObservers == null) return;
            for (int i = 0; i < inhibitionObservers.Count; i++)
            {
                try
                {
                    inhibitionObservers[i]?.Invoke(inhibited);
                }
                catch (Exception exception)
                {
                    GASLog.Error($"ActiveGameplayEffect inhibition observer failed: {exception.Message}");
                }
            }
        }

        private long timeRemainingRaw;
        private long periodTimerRaw;
        private long cachedPeriodRaw;
        private EDurationPolicy cachedDurationPolicy;

        public ActiveGameplayEffect() { }

        bool IGASLeasedObject.TryAcquireLease()
        {
            if (leaseActive || leaseEverAcquired) return false;
            leaseEverAcquired = true;
            leaseActive = true;
            return true;
        }

        bool IGASLeasedObject.TryReleaseLease()
        {
            if (!leaseActive) return false;
            leaseActive = false;
            return true;
        }

        void IGASLeasedObject.OnLeaseAcquired()
        {
            ResetState();
        }

        void IGASLeasedObject.OnLeaseReleased()
        {
            try
            {
                Spec?.ReleaseRuntimeLease();
            }
            finally
            {
                ResetState();
            }
        }

        private void ResetState()
        {
            Spec = null;
            timeRemainingRaw = 0L;
            StackCount = 0;
            IsExpired = false;
            IsInhibited = false;
            ReconciliationId = 0;
            SourceAbilitySpecHandle = 0;
            SourceAbilityExecutionPolicy = EAbilityExecutionPolicy.Invalid;
            inhibitionObservers?.Clear();
            periodTimerRaw = -1L;
            cachedPeriodRaw = 0L;
            cachedDurationPolicy = default;
        }

        internal void SetMemoryOwner(GASRuntimeMemory owner) => memoryOwner = owner;

        internal void SetReconciledSourceAbilitySpecHandle(int specHandle)
        {
            if (!leaseActive || Spec == null)
            {
                throw new InvalidOperationException(
                    "Cannot assign reconciled source provenance to an inactive GameplayEffect lease.");
            }
            if (specHandle < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(specHandle), specHandle,
                    "A reconciled source ability spec handle cannot be negative.");
            }

            SourceAbilitySpecHandle = specHandle;
        }

        #region Factory

        internal static ActiveGameplayEffect Create(GameplayEffectSpec spec)
        {
            if (spec?.Target == null)
            {
                throw new ArgumentException("An active effect requires a spec with an assigned target.", nameof(spec));
            }

            spec.TransferToActiveEffect();
            var activeEffect = spec.Target.RuntimeContext.Memory.AcquireActiveEffect();
            try
            {
                activeEffect.Initialize(spec);
                return activeEffect;
            }
            catch
            {
                activeEffect.ReleaseRuntimeLease();
                throw;
            }
        }

        private void Initialize(GameplayEffectSpec spec)
        {
            Spec = spec;
            SourceAbilitySpecHandle = spec.Context?.AbilityInstance?.Spec?.Handle ?? 0;
            SourceAbilityExecutionPolicy =
                spec.Context?.AbilityInstance?.ExecutionPolicy ?? EAbilityExecutionPolicy.Invalid;
            timeRemainingRaw = spec.DurationRaw;
            StackCount = 1;
            IsExpired = false;

            cachedPeriodRaw = GASFixedValue.FromFloat(spec.Def.Period).RawValue;
            cachedDurationPolicy = spec.Def.DurationPolicy;

            // UE5: bExecutePeriodicEffectOnApplication
            // If true (default), first tick fires immediately (periodTimer = 0).
            // If false, first tick waits for the full period interval.
            if (cachedPeriodRaw > 0L)
            {
                periodTimerRaw = spec.Def.ExecutePeriodicEffectOnApplication ? 0L : cachedPeriodRaw;
            }
            else
            {
                periodTimerRaw = -1L;
            }
        }

        internal void ReleaseRuntimeLease()
        {
            memoryOwner?.ReleaseActiveEffect(this);
        }

        #endregion

        #region Stacking

        /// <summary>
        /// Called when a new stack is successfully applied.
        /// </summary>
        internal void OnStackApplied()
        {
            StackCount = Math.Min(StackCount + 1, Spec.Def.Stacking.Limit);

            if (Spec.Def.Stacking.DurationPolicy == EGameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication)
            {
                timeRemainingRaw = Spec.DurationRaw;
            }

            if (periodTimerRaw > 0L)
            {
                periodTimerRaw = cachedPeriodRaw;
            }
        }

        /// <summary>
        /// Removes a single stack. Used by EGameplayEffectStackingExpirationPolicy.RemoveSingleStackAndRefreshDuration.
        /// </summary>
        internal void RemoveStack()
        {
            if (StackCount > 0)
            {
                StackCount--;
            }
        }

        /// <summary>
        /// Clears the expired flag so the effect can continue living (used after removing a single stack).
        /// </summary>
        internal void ClearExpired()
        {
            IsExpired = false;
        }

        /// <summary>
        /// Refreshes duration without modifying stack count.
        /// </summary>
        internal void RefreshDurationAndPeriod()
        {
            if (cachedDurationPolicy == EDurationPolicy.HasDuration)
            {
                timeRemainingRaw = Spec.DurationRaw;
            }

            if (periodTimerRaw >= 0L)
            {
                periodTimerRaw = cachedPeriodRaw;
            }
        }

        /// <summary>
        /// Sets the remaining duration to a specific value.
        /// UE5: Section 4.5.16 - Changing Active Gameplay Effect Duration.
        /// </summary>
        internal void SetRemainingDuration(float newDuration)
        {
            if (float.IsNaN(newDuration) || float.IsInfinity(newDuration) || newDuration < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(newDuration), newDuration, "Remaining duration must be finite and non-negative.");
            }

            SetRemainingDurationRaw(GASFixedValue.FromFloat(newDuration).RawValue);
        }

        internal void SetRemainingDurationRaw(long newDurationRaw)
        {
            if (newDurationRaw < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(newDurationRaw), newDurationRaw, "Remaining duration cannot be negative.");
            }

            if (cachedDurationPolicy == EDurationPolicy.HasDuration)
            {
                timeRemainingRaw = newDurationRaw;
            }
        }

        internal void SetReplicatedStackCount(int newStackCount)
        {
            ValidateReplicatedStackCount(newStackCount);

            StackCount = newStackCount;
            IsExpired = false;
        }

        private void ValidateReplicatedStackCount(int newStackCount)
        {
            int limit = Spec?.Def?.Stacking.Limit ?? 0;
            int effectiveLimit = limit > 0 ? limit : 1;
            if (newStackCount <= 0 || newStackCount > effectiveLimit)
            {
                throw new ArgumentOutOfRangeException(nameof(newStackCount), newStackCount, $"Stack count must be between 1 and {effectiveLimit}.");
            }
        }

        internal void ApplyReplicatedState(
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
                throw new InvalidOperationException("Cannot apply replicated state to an inactive effect lease.");
            }

            bool invalidPeriodTimer = cachedPeriodRaw > 0L
                ? periodTimeRemaining < 0f
                : periodTimeRemaining != -1f;
            if (float.IsNaN(timeRemaining) || float.IsInfinity(timeRemaining) || timeRemaining < 0f ||
                float.IsNaN(periodTimeRemaining) || float.IsInfinity(periodTimeRemaining) || invalidPeriodTimer)
            {
                throw new ArgumentOutOfRangeException(nameof(timeRemaining), "Replicated timers must be finite and non-negative.");
            }

            ValidateReplicatedStackCount(stackCount);
            Spec.ApplyReplicatedState(level, duration, setByCallerTags, setByCallerValues, setByCallerCount);
            SetReplicatedStackCount(stackCount);

            if (cachedDurationPolicy == EDurationPolicy.HasDuration)
            {
                timeRemainingRaw = GASFixedValue.FromFloat(timeRemaining).RawValue;
            }

            if (cachedPeriodRaw > 0L)
            {
                periodTimerRaw = Math.Max(0L, Math.Min(GASFixedValue.FromFloat(periodTimeRemaining).RawValue, cachedPeriodRaw));
            }
            else
            {
                periodTimerRaw = -1L;
            }

            IsExpired = false;
        }

        internal void ApplyReplicatedStateRaw(
            int level,
            int stackCount,
            long durationRaw,
            long timeRemainingRaw,
            long periodTimeRemainingRaw,
            GameplayTag[] setByCallerTags,
            long[] setByCallerValuesRaw,
            int setByCallerCount)
        {
            if (Spec == null || Spec.Def == null)
            {
                throw new InvalidOperationException("Cannot apply replicated state to an inactive effect lease.");
            }

            bool invalidPeriodTimer = cachedPeriodRaw > 0L
                ? periodTimeRemainingRaw < 0L
                : periodTimeRemainingRaw != -1L;
            if (timeRemainingRaw < 0L || invalidPeriodTimer)
            {
                throw new ArgumentOutOfRangeException(nameof(timeRemainingRaw), "Replicated timers must be non-negative.");
            }

            ValidateReplicatedStackCount(stackCount);
            Spec.ApplyReplicatedStateRaw(level, durationRaw, setByCallerTags, setByCallerValuesRaw, setByCallerCount);
            SetReplicatedStackCount(stackCount);

            if (cachedDurationPolicy == EDurationPolicy.HasDuration)
            {
                this.timeRemainingRaw = timeRemainingRaw;
            }

            if (cachedPeriodRaw > 0L)
            {
                periodTimerRaw = Math.Max(0L, Math.Min(periodTimeRemainingRaw, cachedPeriodRaw));
            }
            else
            {
                periodTimerRaw = -1L;
            }

            IsExpired = false;
        }

        internal void ApplyReplicatedStateRaw(
            int level,
            int stackCount,
            long durationRaw,
            long timeRemainingRaw,
            long periodTimeRemainingRaw,
            GameplayTag[] setByCallerTags,
            long[] setByCallerValuesRaw,
            int setByCallerCount,
            string[] setByCallerNames,
            long[] setByCallerNameValuesRaw,
            int setByCallerNameCount,
            GameplayTag[] dynamicGrantedTags,
            int dynamicGrantedTagCount,
            GameplayTag[] dynamicAssetTags,
            int dynamicAssetTagCount)
        {
            if (Spec == null || Spec.Def == null)
            {
                throw new InvalidOperationException("Cannot apply replicated state to an inactive effect lease.");
            }

            bool invalidPeriodTimer = cachedPeriodRaw > 0L
                ? periodTimeRemainingRaw < 0L
                : periodTimeRemainingRaw != -1L;
            if (timeRemainingRaw < 0L || invalidPeriodTimer)
            {
                throw new ArgumentOutOfRangeException(nameof(timeRemainingRaw), "Replicated timers must be non-negative.");
            }

            ValidateReplicatedStackCount(stackCount);
            Spec.ApplyReplicatedStateRaw(
                level,
                durationRaw,
                setByCallerTags,
                setByCallerValuesRaw,
                setByCallerCount,
                setByCallerNames,
                setByCallerNameValuesRaw,
                setByCallerNameCount,
                dynamicGrantedTags,
                dynamicGrantedTagCount,
                dynamicAssetTags,
                dynamicAssetTagCount);
            SetReplicatedStackCount(stackCount);

            if (cachedDurationPolicy == EDurationPolicy.HasDuration)
            {
                this.timeRemainingRaw = timeRemainingRaw;
            }

            periodTimerRaw = cachedPeriodRaw > 0L
                ? Math.Max(0L, Math.Min(periodTimeRemainingRaw, cachedPeriodRaw))
                : -1L;
            IsExpired = false;
        }

        #endregion

        #region Tick

        /// <summary>
        /// Ticks the effect's duration and period timer.
        /// </summary>
        /// <returns>True if the effect expired this tick.</returns>
        internal bool Tick(float deltaTime, AbilitySystemComponent asc)
        {
            long deltaTimeRaw = GASFixedValue.FromFloat(deltaTime).RawValue;

            // Duration handling
            if (!IsExpired && cachedDurationPolicy == EDurationPolicy.HasDuration)
            {
                timeRemainingRaw -= deltaTimeRaw;
                if (timeRemainingRaw <= 0L)
                {
                    timeRemainingRaw = 0L;
                    IsExpired = true;
                }
            }

            // Periodic effect handling --skip entirely when inhibited (OngoingTagRequirements not met).
            // IsInhibited is kept current by AbilitySystemComponent.RecalculateDirtyAttributes(),
            // which runs at the START of each tick (before effects are ticked) when tags change.
            if (!IsExpired && !IsInhibited && cachedPeriodRaw > 0L)
            {
                periodTimerRaw -= deltaTimeRaw;
                int executionBudget = asc?.Limits.MaxPeriodicEffectExecutionsPerTick
                    ?? GASRuntimeLimits.Default.MaxPeriodicEffectExecutionsPerTick;
                for (int executionCount = 0;
                     executionCount < executionBudget && periodTimerRaw <= 0L;
                     executionCount++)
                {
                    asc.ExecutePeriodicEffect(this);
                    // Preserve elapsed-time remainder. If the budget is exhausted, the
                    // non-positive timer carries deterministic backlog into later ticks.
                    periodTimerRaw += cachedPeriodRaw;
                }
            }

            return IsExpired;
        }

        #endregion
    }
}
