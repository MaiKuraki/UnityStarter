using System;
using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayTags.Core;
using CycloneGames.Logging;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Represents a stateful instance of a GameplayEffect currently active on an ASC.
    /// Tracks state such as remaining time and stack count.
    /// Released instances are invalidated and discarded.
    /// </summary>
    public class ActiveGameplayEffect : IGASLeasedObject
    {
        private static readonly LogChannel Log = GameplayAbilitiesLog.Channel;

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
        /// </summary>
        public bool IsInhibited { get; internal set; }

        /// <summary>
        /// True while this effect's granted tags are present in the owner's tag containers. Inhibition
        /// withdraws them, so removal paths must consult this flag before decrementing tag counts.
        /// </summary>
        internal bool GrantedTagsVisible { get; set; }

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
                    Log.Error(exception, "ActiveGameplayEffect inhibition observer failed.");
                }
            }
        }

        private long timeRemainingRaw;
        private long periodTimerRaw;
        private long cachedPeriodRaw;
        private EDurationPolicy cachedDurationPolicy;
        private EGameplayEffectPeriodInhibitionRemovedPolicy cachedPeriodicInhibitionPolicy;
        private EGameplayEffectStackingPeriodPolicy cachedStackPeriodResetPolicy;
        private bool pendingImmediatePeriodicExecution;

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
            cachedPeriodicInhibitionPolicy = EGameplayEffectPeriodInhibitionRemovedPolicy.NeverReset;
            cachedStackPeriodResetPolicy = EGameplayEffectStackingPeriodPolicy.ResetOnSuccessfulApplication;
            pendingImmediatePeriodicExecution = false;
            unlimitedStackCapReported = false;
            liveDependencyModifierIndices = null;
            liveDependencySourceAttributes = null;
            liveDependencyIndicesResolved = false;
        }

        private int[] liveDependencyModifierIndices;
        // Parallel to liveDependencyModifierIndices: the attribute each live read resolved to when
        // the effect was indexed. Recorded so unlinking never has to re-resolve captures.
        private GameplayAttribute[] liveDependencySourceAttributes;
        private bool liveDependencyIndicesResolved;

        /// <summary>
        /// Drops the cached live-dependency resolution. Call after replacing <see cref="Spec"/>, and only
        /// after unlinking, which still reads the recorded source attributes.
        /// </summary>
        internal void InvalidateLiveDependencyIndex()
        {
            liveDependencyModifierIndices = null;
            liveDependencySourceAttributes = null;
            liveDependencyIndicesResolved = false;
        }

        /// <summary>
        /// The modifier indices of this effect whose magnitude reads a live (non-snapshot)
        /// attribute. Cached lazily per active effect; null when the effect has none, which lets
        /// the dependents scan skip the effect entirely with a single reference check.
        /// </summary>
        internal int[] GetLiveDependencyModifierIndices()
        {
            if (liveDependencyIndicesResolved)
            {
                return liveDependencyModifierIndices;
            }

            liveDependencyIndicesResolved = true;
            IReadOnlyList<ModifierInfo> modifiers = Spec?.Def?.Modifiers;
            if (modifiers == null)
            {
                return null;
            }

            int count = 0;
            for (int i = 0; i < modifiers.Count; i++)
            {
                ModifierInfo modifier = modifiers[i];
                if (modifier.MagnitudeCalculationType == EGameplayEffectMagnitudeCalculation.AttributeBased &&
                    modifier.SnapshotPolicy == EGameplayEffectAttributeCaptureSnapshot.NotSnapshot)
                {
                    count++;
                }
            }

            if (count == 0)
            {
                return null;
            }

            var indices = new int[count];
            int write = 0;
            for (int i = 0; i < modifiers.Count; i++)
            {
                ModifierInfo modifier = modifiers[i];
                if (modifier.MagnitudeCalculationType == EGameplayEffectMagnitudeCalculation.AttributeBased &&
                    modifier.SnapshotPolicy == EGameplayEffectAttributeCaptureSnapshot.NotSnapshot)
                {
                    indices[write++] = i;
                }
            }

            liveDependencyModifierIndices = indices;
            liveDependencySourceAttributes = new GameplayAttribute[count];
            return indices;
        }

        /// <summary>
        /// The attributes the live reads of this effect resolved to, parallel to
        /// <see cref="GetLiveDependencyModifierIndices"/>. Null entries mean the capture was not
        /// resolvable and the read is not indexed.
        /// </summary>
        internal GameplayAttribute[] GetLiveDependencySourceAttributes()
        {
            GetLiveDependencyModifierIndices();
            return liveDependencySourceAttributes;
        }

        internal void SetLiveDependencySourceAttribute(int ordinal, GameplayAttribute attribute)
        {
            if (liveDependencySourceAttributes != null &&
                (uint)ordinal < (uint)liveDependencySourceAttributes.Length)
            {
                liveDependencySourceAttributes[ordinal] = attribute;
            }
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
            cachedPeriodicInhibitionPolicy = spec.Def.PeriodicInhibitionPolicy;
            cachedStackPeriodResetPolicy = spec.Def.Stacking.PeriodResetPolicy;

            // An extra execution is scheduled on the next tick when the effect executes on application.
            // The looping period timer always starts a full period from the application moment.
            if (cachedPeriodRaw > 0L)
            {
                periodTimerRaw = cachedPeriodRaw;
                pendingImmediatePeriodicExecution = spec.Def.ExecutePeriodicEffectOnApplication;
            }
            else
            {
                periodTimerRaw = -1L;
                pendingImmediatePeriodicExecution = false;
            }
        }

        internal void ReleaseRuntimeLease()
        {
            memoryOwner?.ReleaseActiveEffect(this);
        }

        #endregion

        #region Stacking

        private bool unlimitedStackCapReported;

        /// <summary>
        /// True once this effect has reported that Unlimited stacking reached its storage capacity.
        /// </summary>
        internal bool UnlimitedStackCapReported => unlimitedStackCapReported;

        internal void MarkUnlimitedStackCapReported()
        {
            unlimitedStackCapReported = true;
        }

        /// <summary>
        /// Called when a new stack is successfully applied.
        /// </summary>
        internal void OnStackApplied()
        {
            OnStackApplied(Spec.DurationRaw, null);
        }

        /// <summary>
        /// Called when a new stack is successfully applied.
        /// The duration refresh uses the incoming application's duration (including SetDuration
        /// overrides). The active effect retains its original spec lease; only the duration
        /// is adopted.
        /// </summary>
        /// <param name="applicationDurationRaw">
        /// The raw duration of the incoming application spec, read before its lease is released.
        /// </param>
        internal void OnStackApplied(long applicationDurationRaw, GameplayEffectSpec applicationSpec)
        {
            StackCount = Math.Min(StackCount + 1, Spec.Def.Stacking.ResolveStackLimit());

            if (applicationSpec != null && !ReferenceEquals(applicationSpec, Spec))
            {
                // The newest application carries the payload: level, SetByCaller values, dynamic tags
                // and snapshotted magnitudes. Granted abilities are tracked per effect, not per spec.
                GameplayEffectSpec previous = Spec;
                applicationSpec.TransferToActiveEffect();
                Spec = applicationSpec;
                // The swap replaces the captures the dependency edge was built from.
                InvalidateLiveDependencyIndex();
                previous?.ReleaseRuntimeLease();
            }

            ApplyStackDurationPolicy(applicationDurationRaw);

            RefreshPeriodOnApplication();
        }

        /// <summary>
        /// Applies the stacking duration policy for one accepted application whose spec carried
        /// <paramref name="applicationDurationRaw"/> as its duration.
        /// </summary>
        internal void ApplyStackDurationPolicy(long applicationDurationRaw)
        {
            switch (Spec.Def.Stacking.DurationPolicy)
            {
                case EGameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication:
                    timeRemainingRaw = applicationDurationRaw;
                    break;
                case EGameplayEffectStackingDurationPolicy.ExtendDuration:
                    // Saturating fixed-point add: deterministic and overflow-safe for
                    // arbitrarily repeated applications.
                    timeRemainingRaw = SaturatingAddRaw(timeRemainingRaw, applicationDurationRaw);
                    break;
            }
        }

        /// <summary>
        /// Adds one application's duration onto the remaining time without touching stack count or
        /// the period timer.
        /// </summary>
        internal void ExtendDuration(long durationRaw)
        {
            if (cachedDurationPolicy == EDurationPolicy.HasDuration)
            {
                timeRemainingRaw = SaturatingAddRaw(timeRemainingRaw, durationRaw);
            }
        }

        private static long SaturatingAddRaw(long a, long b)
        {
            long sum = a + b;
            // Both operands are non-negative durations; a negative sum means long overflow.
            return sum < 0L ? long.MaxValue : sum;
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
        /// Refreshes duration without modifying stack count or the period timer.
        /// </summary>
        internal void RefreshDuration()
        {
            RefreshDuration(Spec.DurationRaw);
        }

        /// <summary>
        /// Refreshes duration without modifying stack count or the period timer, using the
        /// duration of the incoming application spec (see OnStackApplied).
        /// </summary>
        internal void RefreshDuration(long durationRaw)
        {
            if (cachedDurationPolicy == EDurationPolicy.HasDuration)
            {
                timeRemainingRaw = durationRaw;
            }
        }

        /// <summary>
        /// Restarts the period timer to a full period and re-arms the immediate execution.
        /// </summary>
        internal void RefreshPeriodOnApplication()
        {
            if (periodTimerRaw < 0L ||
                cachedStackPeriodResetPolicy != EGameplayEffectStackingPeriodPolicy.ResetOnSuccessfulApplication)
            {
                return;
            }

            periodTimerRaw = cachedPeriodRaw;
            pendingImmediatePeriodicExecution = Spec.Def.ExecutePeriodicEffectOnApplication;
        }

        /// <summary>
        /// Applies the periodic inhibition policy when OngoingTagRequirements become satisfied again.
        /// </summary>
        internal void OnInhibitionRemoved()
        {
            if (periodTimerRaw < 0L)
            {
                return;
            }

            switch (cachedPeriodicInhibitionPolicy)
            {
                case EGameplayEffectPeriodInhibitionRemovedPolicy.ResetPeriod:
                    periodTimerRaw = cachedPeriodRaw;
                    break;
                case EGameplayEffectPeriodInhibitionRemovedPolicy.ExecuteAndResetPeriod:
                    periodTimerRaw = cachedPeriodRaw;
                    pendingImmediatePeriodicExecution = true;
                    break;
            }
        }

        /// <summary>
        /// Sets the remaining duration to a specific value.
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
            GameplayEffect definition = Spec?.Def;
            int effectiveLimit = definition != null ? definition.Stacking.ResolveStackLimit() : 1;
            if (effectiveLimit <= 0)
            {
                effectiveLimit = 1;
            }

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
            bool wasExpired = IsExpired;
            bool durationElapsed = false;
            if (!wasExpired && cachedDurationPolicy == EDurationPolicy.HasDuration)
            {
                timeRemainingRaw -= deltaTimeRaw;
                if (timeRemainingRaw <= 0L)
                {
                    timeRemainingRaw = 0L;
                    durationElapsed = true;
                }
            }

            if (!wasExpired)
            {
                int executionBudget = asc?.Limits.MaxPeriodicEffectExecutionsPerTick
                    ?? GASRuntimeLimits.Default.MaxPeriodicEffectExecutionsPerTick;

                if (pendingImmediatePeriodicExecution)
                {
                    pendingImmediatePeriodicExecution = false;
                    executionBudget = ConsumePeriodicCycle(asc, executionBudget);
                }

                if (cachedPeriodRaw > 0L)
                {
                    periodTimerRaw -= deltaTimeRaw;
                    while (executionBudget > 0 && periodTimerRaw <= 0L)
                    {
                        executionBudget = ConsumePeriodicCycle(asc, executionBudget);
                        // Preserve elapsed-time remainder. If the budget is exhausted, the
                        // non-positive timer carries deterministic backlog into later ticks.
                        periodTimerRaw += cachedPeriodRaw;
                    }
                }
            }

            if (durationElapsed)
            {
                IsExpired = true;
            }

            return IsExpired;
        }

        private int ConsumePeriodicCycle(AbilitySystemComponent asc, int executionBudget)
        {
            if (executionBudget <= 0)
            {
                return executionBudget;
            }

            executionBudget--;
            if (asc != null && !IsInhibited)
            {
                asc.ExecutePeriodicEffect(this);
            }

            return executionBudget;
        }

        #endregion
    }
}
