using System;
using System.Collections.Generic;
using CycloneGames.GameplayTags.Core;
using CycloneGames.GameplayAbilities.Core;
using CycloneGames.Hash.Core;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Runtime
{
    public enum EReplicationMode
    {
        Full,
        Mixed,
        Minimal
    }

    public enum GASRuntimeThreadPolicy : byte
    {
        Disabled,
        LogWarning,
        Throw
    }

    public enum GASStateDeltaRejectionReason : byte
    {
        None,
        MissingDelta,
        UnsupportedChangeMask,
        InvalidSequence,
        StaleOrReplayedSequence,
        InvalidVersionRange,
        InvalidCounts,
        CapacityExceeded,
        InvalidPayload,
        BaselineMismatch,
        ChecksumMismatch,
        ApplicationFailed,
        ResyncRequired
    }

    /// <summary>
    /// Selects whether the Unity-facing AbilitySystemComponent mirrors its runtime object graph into the pure C# Core state.
    /// </summary>
    public enum GASCoreStateMode : byte
    {
        /// <summary>
        /// The AbilitySystemComponent keeps only the Runtime object graph as its authoritative state.
        /// Use this for high-density clients or gameplay objects that do not need Core simulation snapshots.
        /// </summary>
        RuntimeOnly,

        /// <summary>
        /// The AbilitySystemComponent keeps Runtime as the authoritative state and mirrors ability/effect/attribute data into Core.
        /// Use this when tooling, deterministic validation, or Core-level simulation needs a synchronized mirror.
        /// </summary>
        MirrorRuntime
    }

    /// <summary>
    /// Construction options for AbilitySystemComponent runtime state ownership and optional mirrors.
    /// </summary>
    public sealed class GASAbilitySystemRuntimeOptions
    {
        public static readonly GASAbilitySystemRuntimeOptions Default = new GASAbilitySystemRuntimeOptions();
        public static readonly GASAbilitySystemRuntimeOptions RuntimeOnly = new GASAbilitySystemRuntimeOptions(GASCoreStateMode.RuntimeOnly);
        public static readonly GASAbilitySystemRuntimeOptions MirrorRuntime = new GASAbilitySystemRuntimeOptions(GASCoreStateMode.MirrorRuntime);

        public GASAbilitySystemRuntimeOptions(
            GASCoreStateMode coreStateMode = GASCoreStateMode.RuntimeOnly,
            GASRuntimeLimits limits = null,
            GASAbilitySystemLimits coreLimits = null)
        {
            CoreStateMode = coreStateMode;
            Limits = limits ?? GASRuntimeLimits.Default;
            CoreLimits = coreLimits ?? new GASAbilitySystemLimits(
                Limits.MaxGrantedAbilities,
                Limits.MaxAttributes,
                Limits.MaxActiveEffects,
                Limits.MaxCoreModifiers,
                Limits.MaxPredictedAttributeChanges);
        }

        public GASCoreStateMode CoreStateMode { get; }
        public GASRuntimeLimits Limits { get; }
        public GASAbilitySystemLimits CoreLimits { get; }
    }

    // Delegate for gameplay event delivery (UE5: SendGameplayEventToActor)
    public delegate void GameplayEventDelegate(GameplayEventData eventData);

    // Delegate for effect lifecycle events
    public delegate void ActiveEffectDelegate(ActiveGameplayEffect effect);

    public struct GASRuntimeListPoolStatistics
    {
        public int GrantedAbilitySpecListPoolSize;
        public int AbilityAppliedEffectListPoolSize;
        public int PeakGrantedAbilitySpecListPoolSize;
        public int PeakAbilityAppliedEffectListPoolSize;
        public long GrantedAbilitySpecListGets;
        public long GrantedAbilitySpecListMisses;
        public long GrantedAbilitySpecListDiscards;
        public long AbilityAppliedEffectListGets;
        public long AbilityAppliedEffectListMisses;
        public long AbilityAppliedEffectListDiscards;
        public int MaxPooledGrantedAbilitySpecLists;
        public int MaxPooledAbilityAppliedEffectLists;
        public int MaxRetainedGrantedAbilitySpecListCapacity;
        public int MaxRetainedAbilityAppliedEffectListCapacity;
    }

    [Flags]
    public enum GASRuntimeDiagnosticFlags : uint
    {
        None = 0,
        RuntimeIndexMismatch = 1u << 0,
        CoreAbilitySpecHandleMismatch = 1u << 1,
        CoreActiveEffectHandleMismatch = 1u << 2,
        OpenPredictionWindows = 1u << 3,
        PredictionWindowTimeoutRisk = 1u << 4,
        PendingStateChanges = 1u << 5,
        PendingRemovedEffects = 1u << 6,
        PendingRemovedAbilities = 1u << 7,
        DirtyAttributes = 1u << 8,
        PendingTagChanges = 1u << 9,
        RuntimeListPoolMisses = 1u << 10,
        RuntimeListPoolDiscards = 1u << 11,
        RuntimeListPoolAtCapacity = 1u << 12,
        RuntimeListPoolInvalidCapacity = 1u << 13
    }

    public struct GASRuntimeDiagnostics
    {
        public GASRuntimeDiagnosticFlags Flags;
        public int AbilitySpecCount;
        public int ActiveEffectCount;
        public int AttributeSetCount;
        public int DirtyAttributeCount;
        public int OpenPredictionWindowCount;
        public long MaxOpenPredictionWindowAgeFrames;
        public ulong StateVersion;
        public ulong LastReplicatedStateVersion;
        public AbilitySystemStateChangeMask PendingStateChangeMask;
        public ulong ReplicatedStateChecksum;
        public GASCoreStateMode CoreStateMode;
        public bool IsCoreStateEnabled;
        public int CoreAbilitySpecCount;
        public int CoreActiveEffectCount;
        public int CoreAttributeCount;
        public int CoreModifierCount;
        public int CoreSpecHandleCount;
        public int CoreActiveEffectHandleCount;
        public int RuntimeThreadId;
        public int CurrentThreadId;
        public long RuntimeThreadViolationCount;
        public int PendingRemovedEffectCount;
        public int PendingRemovedAbilitySpecHandleCount;
        public int PendingAddedTagCount;
        public int PendingRemovedTagCount;
        public GASRuntimeListPoolStatistics ListPoolStatistics;

        public bool HasCriticalIssues =>
            (Flags & (GASRuntimeDiagnosticFlags.RuntimeIndexMismatch |
                      GASRuntimeDiagnosticFlags.CoreAbilitySpecHandleMismatch |
                      GASRuntimeDiagnosticFlags.CoreActiveEffectHandleMismatch)) != 0;
    }

    public partial class AbilitySystemComponent : IDisposable
    {
        public readonly struct GASPredictionScope : IDisposable
        {
            private readonly AbilitySystemComponent asc;
            private readonly GASPredictionKey previousPredictionKey;
            private readonly int previousScopeToken;
            private readonly int scopeToken;
            private readonly bool active;

            internal GASPredictionScope(AbilitySystemComponent asc, GASPredictionKey predictionKey)
            {
                this.asc = asc;
                previousPredictionKey = asc.currentPredictionKey;
                previousScopeToken = asc.currentPredictionScopeToken;
                active = predictionKey.IsValid;
                scopeToken = active ? asc.EnterPredictionScope(predictionKey) : 0;

            }

            public void Dispose()
            {
                if (active && asc != null)
                {
                    asc.ExitPredictionScope(scopeToken, previousScopeToken, previousPredictionKey);
                }
            }
        }

        public object OwnerActor { get; private set; }
        public object AvatarActor { get; private set; }
        public UnityEngine.Object OwnerUnityObject { get; private set; }
        public GameObject AvatarGameObject { get; private set; }
        private EReplicationMode replicationMode = EReplicationMode.Full;
        public EReplicationMode ReplicationMode
        {
            get
            {
                AssertRuntimeThread();
                return replicationMode;
            }
            set
            {
                AssertRuntimeThread();
                replicationMode = value;
            }
        }
        public GASRuntimeContext RuntimeContext { get; }
        public GASRuntimeLimits Limits { get; }
        public bool IsDisposed => disposed;
        public long SimulationFrame => simulationFrame;

        private readonly GameplayTagCountContainer combinedTags = new GameplayTagCountContainer();
        private readonly GASReadOnlyTagView combinedTagsView;
        public GASReadOnlyTagView CombinedTags
        {
            get
            {
                AssertRuntimeThread();
                return combinedTagsView;
            }
        }
        private readonly GameplayTagCountContainer looseTags = new GameplayTagCountContainer();
        private readonly GameplayTagCountContainer fromEffectsTags = new GameplayTagCountContainer();
        private const int DefaultGrantedAbilitySpecListCapacity = 2;
        private const int DefaultAbilityAppliedEffectListCapacity = 4;
        private const int DefaultReusableListPoolLimit = 32;
        private const int DefaultMaxRetainedGrantedAbilitySpecListCapacity = 16;
        private const int DefaultMaxRetainedAbilityAppliedEffectListCapacity = 32;
        private const int MaxRetainedInstantEffectScratchCapacity = 256;

        /// <summary>
        /// Tags that grant immunity to effects. Effects with AssetTags or GrantedTags matching these will be blocked.
        /// </summary>
        private readonly GameplayTagContainer immunityTags = new GameplayTagContainer();
        private readonly GASReadOnlyTagView immunityTagsView;
        public GASReadOnlyTagView ImmunityTags
        {
            get
            {
                AssertRuntimeThread();
                return immunityTagsView;
            }
        }

        internal AbilitySpecContainer AbilitySpecs { get; }
        internal ActiveEffectContainer ActiveEffectContainer { get; }
        public AttributeAggregator AttributeAggregator { get; }
        internal PredictionManager PredictionManager { get; }
        internal ReplicationStateBuilder ReplicationStateBuilder { get; }
        public GameplayCueDispatcher CueDispatcher { get; }
        private readonly Action assertRuntimeAccess;
        private readonly GASReadOnlySetView<string> dirtyAttributeNamesView;
        private readonly GASReadOnlyListView<GameplayAttribute> dirtyAttributeValueSnapshotsView;
        private readonly GASReadOnlySetView<GameplayTag> pendingAddedTagsView;
        private readonly GASReadOnlySetView<GameplayTag> pendingRemovedTagsView;

        private readonly List<AttributeSet> attributeSets;
        private readonly GASReadOnlyListView<AttributeSet> attributeSetsView;
        public GASReadOnlyListView<AttributeSet> AttributeSets
        {
            get
            {
                AssertRuntimeThread();
                return attributeSetsView;
            }
        }
        private readonly Dictionary<string, GameplayAttribute> attributes;
        private readonly List<ActiveGameplayEffect> activeEffects;
        private readonly GASReadOnlyListView<ActiveGameplayEffect> activeEffectsView;
        private readonly Dictionary<ActiveGameplayEffect, int> activeEffectIndexByEffect;
        private readonly Dictionary<int, ActiveGameplayEffect> activeEffectByReconciliationId;
        public GASReadOnlyListView<ActiveGameplayEffect> ActiveEffects
        {
            get
            {
                AssertRuntimeThread();
                return activeEffectsView;
            }
        }

        //  O(1) stacking index --avoids linear search per ApplyGameplayEffectSpecToSelf
        // Key: GameplayEffect def ->first matching ActiveGameplayEffect (for AggregateByTarget)
        private readonly Dictionary<GameplayEffect, ActiveGameplayEffect> stackingIndexByTarget;
        // Key: (GameplayEffect def, source ASC) ->ActiveGameplayEffect (for AggregateBySource)
        private readonly Dictionary<(GameplayEffect, AbilitySystemComponent), ActiveGameplayEffect> stackingIndexBySource;

        // Explicit-GrantedTag runtime-index -> ActiveGameplayEffects for O(1) tag lookup,
        // while preserving all contributors for correct multi-effect semantics.
        // Maintained in OnEffectApplied / OnEffectRemoved.
        private readonly Dictionary<int, List<ActiveGameplayEffect>> grantedTagIndexToEffects;

        private readonly List<GameplayAbilitySpec> activatableAbilities;
        private readonly Dictionary<int, GameplayAbilitySpec> abilitySpecByHandle;
        private readonly Dictionary<GameplayAbilitySpec, int> abilitySpecIndexBySpec;
        private readonly Dictionary<ActiveGameplayEffect, List<GameplayAbilitySpec>> grantedAbilitySpecsByEffect;
        private readonly Stack<List<GameplayAbilitySpec>> grantedAbilitySpecListPool;
        private readonly GASReadOnlyListView<GameplayAbilitySpec> activatableAbilitiesView;
        private int maxPooledGrantedAbilitySpecLists = DefaultReusableListPoolLimit;
        private int maxRetainedGrantedAbilitySpecListCapacity = DefaultMaxRetainedGrantedAbilitySpecListCapacity;
        public GASReadOnlyListView<GameplayAbilitySpec> GetActivatableAbilities()
        {
            AssertRuntimeThread();
            return activatableAbilitiesView;
        }

        public bool TryGetAbilitySpecByHandle(int handle, out GameplayAbilitySpec spec)
        {
            AssertRuntimeThread();
            spec = FindSpecByHandle(handle);
            return spec != null;
        }

        public int MaxPooledGrantedAbilitySpecLists
        {
            get
            {
                AssertRuntimeThread();
                return maxPooledGrantedAbilitySpecLists;
            }
            set
            {
                AssertRuntimeThread();
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Pool capacity must be non-negative.");
                }

                maxPooledGrantedAbilitySpecLists = value;
            }
        }

        public int MaxRetainedGrantedAbilitySpecListCapacity
        {
            get
            {
                AssertRuntimeThread();
                return maxRetainedGrantedAbilitySpecListCapacity;
            }
            set
            {
                AssertRuntimeThread();
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Retained list capacity must be non-negative.");
                }

                maxRetainedGrantedAbilitySpecListCapacity = value;
            }
        }
        private int peakGrantedAbilitySpecListPoolSize;
        private long grantedAbilitySpecListGets;
        private long grantedAbilitySpecListMisses;
        private long grantedAbilitySpecListDiscards;

        private readonly List<GameplayAbilitySpec> tickingAbilities;
        private readonly Dictionary<GameplayAbilitySpec, int> tickingAbilityIndexBySpec;
        private readonly List<TickingAbilityLeaseSnapshot> tickingAbilityTickSnapshot;
        private int tickingAbilityIterationDepth;

        private readonly struct TickingAbilityLeaseSnapshot
        {
            public TickingAbilityLeaseSnapshot(GameplayAbilitySpec spec)
            {
                Spec = spec;
                LeaseGeneration = spec?.LeaseGeneration ?? 0UL;
            }

            public GameplayAbilitySpec Spec { get; }
            public ulong LeaseGeneration { get; }
        }

        private readonly List<GameplayAttribute> dirtyAttributes;
        private bool dirtyOngoingEffectInhibition;

        private struct ModifierChannelAccumulator
        {
            public GASFixedValue Additive;
            public GASFixedValue Multiplier;
            public GASFixedValue OverrideValue;
            public bool HasModifier;
            public bool HasOverride;

            public void Reset()
            {
                Additive = GASFixedValue.Zero;
                Multiplier = GASFixedValue.One;
                OverrideValue = GASFixedValue.Zero;
                HasModifier = false;
                HasOverride = false;
            }
        }

        private readonly ModifierChannelAccumulator[] modifierChannelAccumulators;
        private List<ModifierInfo> instantExecutionOutputScratch;
        private List<InstantAttributeSnapshot> instantRollbackSnapshotScratch;
        private bool instantEffectExecutionInProgress;

        //  Tracks effects applied by abilities for RemoveGameplayEffectsAfterAbilityEnds
        private readonly Dictionary<GameplayAbility, List<ActiveGameplayEffect>> abilityAppliedEffects;
        private readonly List<ActiveGameplayEffect> abilityAppliedEffectRemovalScratch;
        private readonly Stack<List<ActiveGameplayEffect>> abilityAppliedEffectListPool;
        private int maxPooledAbilityAppliedEffectLists = DefaultReusableListPoolLimit;
        private int maxRetainedAbilityAppliedEffectListCapacity = DefaultMaxRetainedAbilityAppliedEffectListCapacity;
        public int MaxPooledAbilityAppliedEffectLists
        {
            get
            {
                AssertRuntimeThread();
                return maxPooledAbilityAppliedEffectLists;
            }
            set
            {
                AssertRuntimeThread();
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Pool capacity must be non-negative.");
                }

                maxPooledAbilityAppliedEffectLists = value;
            }
        }

        public int MaxRetainedAbilityAppliedEffectListCapacity
        {
            get
            {
                AssertRuntimeThread();
                return maxRetainedAbilityAppliedEffectListCapacity;
            }
            set
            {
                AssertRuntimeThread();
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Retained list capacity must be non-negative.");
                }

                maxRetainedAbilityAppliedEffectListCapacity = value;
            }
        }
        private int peakAbilityAppliedEffectListPoolSize;
        private long abilityAppliedEffectListGets;
        private long abilityAppliedEffectListMisses;
        private long abilityAppliedEffectListDiscards;

        private readonly struct InstantAttributeSnapshot
        {
            public InstantAttributeSnapshot(GameplayAttribute attribute)
            {
                Attribute = attribute;
                BaseValueRaw = attribute.BaseValueRaw;
                CurrentValueRaw = attribute.CurrentValueRaw;
            }

            public GameplayAttribute Attribute { get; }
            public long BaseValueRaw { get; }
            public long CurrentValueRaw { get; }
        }

        // --- Prediction ---
        private const int DefaultPredictionTransactionRecordCapacity = 64;
        private GASPredictionKey currentPredictionKey { get => PredictionManager.CurrentPredictionKey; set => PredictionManager.CurrentPredictionKey = value; }
        private int predictionWindowTimeoutFrames = 180;
        private int predictionScopeSequence;
        private int currentPredictionScopeToken;
        public int PredictionWindowTimeoutFrames
        {
            get
            {
                AssertRuntimeThread();
                return predictionWindowTimeoutFrames;
            }
            set
            {
                AssertRuntimeThread();
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Prediction timeout must be non-negative.");
                }
                predictionWindowTimeoutFrames = value;
            }
        }
        public int OpenPredictionWindowCount => PredictionManager.WindowCount;
        public GASPredictionKey CurrentPredictionKey => currentPredictionKey;
        private readonly GASCallbackList<Action<GASPredictionKey, GASPredictionWindowStatus>> predictionWindowClosedObservers =
            new GASCallbackList<Action<GASPredictionKey, GASPredictionWindowStatus>>(2);
        public event Action<GASPredictionKey, GASPredictionWindowStatus> OnPredictionWindowClosed
        {
            add
            {
                AssertRuntimeThread();
                predictionWindowClosedObservers.Add(value);
            }
            remove
            {
                AssertRuntimeThreadForCleanupRemoval();
                predictionWindowClosedObservers.RemoveLast(value);
            }
        }

        private int EnterPredictionScope(GASPredictionKey predictionKey)
        {
            int token = unchecked(++predictionScopeSequence);
            if (token == 0)
            {
                token = unchecked(++predictionScopeSequence);
            }

            currentPredictionScopeToken = token;
            currentPredictionKey = predictionKey;
            return token;
        }

        private void ExitPredictionScope(int scopeToken, int previousScopeToken, GASPredictionKey previousPredictionKey)
        {
            if (currentPredictionScopeToken != scopeToken)
            {
                if (currentPredictionScopeToken > scopeToken)
                {
                    throw new InvalidOperationException("Prediction scopes must be disposed in last-in, first-out order.");
                }

                return;
            }

            currentPredictionScopeToken = previousScopeToken;
            currentPredictionKey = previousPredictionKey;
        }

        private int runtimeThreadId;
        private long runtimeThreadViolationCount;
        private GASRuntimeThreadPolicy runtimeThreadPolicy = GASRuntimeThreadPolicy.Disabled;
        public GASRuntimeThreadPolicy RuntimeThreadPolicy
        {
            get
            {
                ThrowIfDisposed();
                RuntimeContext.AssertOwnerThread();
                return runtimeThreadPolicy;
            }
            set
            {
                ThrowIfDisposed();
                RuntimeContext.AssertOwnerThread();
                runtimeThreadPolicy = value;
            }
        }
        public int RuntimeThreadId => runtimeThreadId;
        public long RuntimeThreadViolationCount => runtimeThreadViolationCount;
        private readonly bool ownsRuntimeContext;
        private bool disposing;
        private bool disposed;
        private int callbackDispatchDepth;
        private long simulationFrame;
        private int nextAbilitySpecHandle;
        private int nextActiveEffectReconciliationId;

        public void BindRuntimeThreadToCurrent()
        {
            ThrowIfDisposed();
            RuntimeContext.AssertOwnerThread();
            runtimeThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void AssertRuntimeThread()
        {
            ThrowIfDisposed();
            RuntimeContext.AssertOwnerThread();
            if (runtimeThreadPolicy == GASRuntimeThreadPolicy.Disabled)
            {
                return;
            }

            int currentThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            if (runtimeThreadId == 0)
            {
                runtimeThreadId = currentThreadId;
                return;
            }

            if (currentThreadId == runtimeThreadId)
            {
                return;
            }

            runtimeThreadViolationCount++;
            if (runtimeThreadPolicy == GASRuntimeThreadPolicy.LogWarning)
            {
                GASLog.Warning(sb => sb.Append("AbilitySystemComponent rejected access from thread ")
                    .Append(currentThreadId)
                    .Append("; runtime thread is ")
                    .Append(runtimeThreadId)
                    .Append('.'));
            }

            throw new InvalidOperationException($"AbilitySystemComponent accessed from thread {currentThreadId}; runtime thread is {runtimeThreadId}.");
        }

        private void ThrowIfDisposed()
        {
            if (disposing || disposed)
            {
                throw new ObjectDisposedException(nameof(AbilitySystemComponent));
            }
        }

        internal void EnterRuntimeCallbackDispatch()
        {
            RuntimeContext.AssertOwnerThread();
            callbackDispatchDepth++;
        }

        internal void ExitRuntimeCallbackDispatch()
        {
            if (callbackDispatchDepth <= 0)
            {
                throw new InvalidOperationException("AbilitySystemComponent callback dispatch scopes must be balanced.");
            }

            callbackDispatchDepth--;
        }

        public bool ValidateRuntimeIndexes()
        {
            AssertRuntimeThread();
            if (!ActiveEffectContainer.ValidateIndexes() || !AbilitySpecs.ValidateIndexes())
            {
                return false;
            }

            if (activeEffectIndexByEffect.Count != activeEffects.Count ||
                activeEffectByReconciliationId.Count > activeEffects.Count ||
                abilitySpecByHandle.Count != activatableAbilities.Count ||
                abilitySpecIndexBySpec.Count != activatableAbilities.Count ||
                tickingAbilityIndexBySpec.Count != tickingAbilities.Count ||
                !PredictionManager.ValidateIndexes() ||
                grantedAbilitySpecsByEffect.Count > activeEffects.Count ||
                abilityAppliedEffects.Count > activeEffects.Count)
            {
                return false;
            }

            for (int i = 0; i < activeEffects.Count; i++)
            {
                var effect = activeEffects[i];
                if (effect == null ||
                    !activeEffectIndexByEffect.TryGetValue(effect, out int index) ||
                    index != i)
                {
                    return false;
                }

                if (effect.ReconciliationId != 0 &&
                    (!activeEffectByReconciliationId.TryGetValue(effect.ReconciliationId, out var indexedByReconciliationId) ||
                     !ReferenceEquals(indexedByReconciliationId, effect)))
                {
                    return false;
                }
            }

            for (int i = 0; i < activatableAbilities.Count; i++)
            {
                var spec = activatableAbilities[i];
                if (spec == null ||
                    !abilitySpecByHandle.TryGetValue(spec.Handle, out var indexedSpec) ||
                    !ReferenceEquals(indexedSpec, spec) ||
                    !abilitySpecIndexBySpec.TryGetValue(spec, out int index) ||
                    index != i ||
                    (spec.GrantingEffect != null && !ValidateGrantedAbilityEffectIndex(spec)))
                {
                    return false;
                }
            }

            for (int i = 0; i < tickingAbilities.Count; i++)
            {
                var spec = tickingAbilities[i];
                if (spec == null ||
                    !tickingAbilityIndexBySpec.TryGetValue(spec, out int index) ||
                    index != i)
                {
                    return false;
                }
            }

            foreach (var kvp in grantedAbilitySpecsByEffect)
            {
                if (kvp.Key == null ||
                    kvp.Value == null ||
                    !activeEffectIndexByEffect.ContainsKey(kvp.Key))
                {
                    return false;
                }

                var specs = kvp.Value;
                for (int i = 0; i < specs.Count; i++)
                {
                    var spec = specs[i];
                    if (spec == null ||
                        !ReferenceEquals(spec.GrantingEffect, kvp.Key) ||
                        !abilitySpecIndexBySpec.ContainsKey(spec))
                    {
                        return false;
                    }
                }
            }

            foreach (var kvp in abilityAppliedEffects)
            {
                if (kvp.Key == null || kvp.Value == null)
                {
                    return false;
                }

                var effects = kvp.Value;
                for (int i = 0; i < effects.Count; i++)
                {
                    var effect = effects[i];
                    if (effect == null || !activeEffectIndexByEffect.ContainsKey(effect))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private bool ValidateGrantedAbilityEffectIndex(GameplayAbilitySpec spec)
        {
            if (spec == null || spec.GrantingEffect == null)
            {
                return true;
            }

            if (!grantedAbilitySpecsByEffect.TryGetValue(spec.GrantingEffect, out var specs))
            {
                return false;
            }

            for (int i = 0; i < specs.Count; i++)
            {
                if (ReferenceEquals(specs[i], spec))
                {
                    return true;
                }
            }

            return false;
        }

        public GASRuntimeDiagnostics GetRuntimeDiagnostics(bool computeChecksum = true)
        {
            AssertRuntimeThread();
            var listPoolStats = GetRuntimeListPoolStatistics();
            var flags = GASRuntimeDiagnosticFlags.None;

            if (!ValidateRuntimeIndexes())
            {
                flags |= GASRuntimeDiagnosticFlags.RuntimeIndexMismatch;
            }

            if (IsCoreStateEnabled &&
                (coreSpecHandles.Count != activatableAbilities.Count ||
                 coreState.AbilitySpecCount != activatableAbilities.Count))
            {
                flags |= GASRuntimeDiagnosticFlags.CoreAbilitySpecHandleMismatch;
            }

            if (IsCoreStateEnabled &&
                (coreActiveEffectHandles.Count > activeEffects.Count ||
                 coreState.ActiveEffectCount > activeEffects.Count))
            {
                flags |= GASRuntimeDiagnosticFlags.CoreActiveEffectHandleMismatch;
            }

            if (PredictionManager.WindowCount > 0)
            {
                flags |= GASRuntimeDiagnosticFlags.OpenPredictionWindows;
            }

            long maxPredictionAge = 0;
            long frame = simulationFrame;
            var windows = PredictionManager.Windows;
            for (int i = 0; i < windows.Count; i++)
            {
                long age = frame - windows[i].OpenFrame;
                if (age > maxPredictionAge)
                {
                    maxPredictionAge = age;
                }

                if (windows[i].TimeoutFrame > 0 && frame >= windows[i].TimeoutFrame)
                {
                    flags |= GASRuntimeDiagnosticFlags.PredictionWindowTimeoutRisk;
                }
            }

            var pendingMask = PendingStateChangeMask;
            if (pendingMask != AbilitySystemStateChangeMask.None || stateVersion != lastReplicatedStateVersion)
            {
                flags |= GASRuntimeDiagnosticFlags.PendingStateChanges;
            }

            if (pendingRemovedEffectReconciliationIds.Count > 0)
            {
                flags |= GASRuntimeDiagnosticFlags.PendingRemovedEffects;
            }

            if (pendingRemovedAbilitySpecHandles.Count > 0)
            {
                flags |= GASRuntimeDiagnosticFlags.PendingRemovedAbilities;
            }

            if (dirtyAttributeNames.Count > 0)
            {
                flags |= GASRuntimeDiagnosticFlags.DirtyAttributes;
            }

            if (pendingAddedTags.Count > 0 || pendingRemovedTags.Count > 0)
            {
                flags |= GASRuntimeDiagnosticFlags.PendingTagChanges;
            }

            if (listPoolStats.GrantedAbilitySpecListMisses > 0 ||
                listPoolStats.AbilityAppliedEffectListMisses > 0)
            {
                flags |= GASRuntimeDiagnosticFlags.RuntimeListPoolMisses;
            }

            if (listPoolStats.GrantedAbilitySpecListDiscards > 0 ||
                listPoolStats.AbilityAppliedEffectListDiscards > 0)
            {
                flags |= GASRuntimeDiagnosticFlags.RuntimeListPoolDiscards;
            }

            if ((maxPooledGrantedAbilitySpecLists > 0 &&
                 listPoolStats.GrantedAbilitySpecListPoolSize >= maxPooledGrantedAbilitySpecLists) ||
                (maxPooledAbilityAppliedEffectLists > 0 &&
                 listPoolStats.AbilityAppliedEffectListPoolSize >= maxPooledAbilityAppliedEffectLists))
            {
                flags |= GASRuntimeDiagnosticFlags.RuntimeListPoolAtCapacity;
            }

            if (maxRetainedGrantedAbilitySpecListCapacity < DefaultGrantedAbilitySpecListCapacity ||
                maxRetainedAbilityAppliedEffectListCapacity < DefaultAbilityAppliedEffectListCapacity)
            {
                flags |= GASRuntimeDiagnosticFlags.RuntimeListPoolInvalidCapacity;
            }

            return new GASRuntimeDiagnostics
            {
                Flags = flags,
                AbilitySpecCount = activatableAbilities.Count,
                ActiveEffectCount = activeEffects.Count,
                AttributeSetCount = attributeSets.Count,
                DirtyAttributeCount = dirtyAttributeNames.Count,
                OpenPredictionWindowCount = PredictionManager.WindowCount,
                MaxOpenPredictionWindowAgeFrames = maxPredictionAge,
                StateVersion = stateVersion,
                LastReplicatedStateVersion = lastReplicatedStateVersion,
                PendingStateChangeMask = pendingMask,
                ReplicatedStateChecksum = computeChecksum ? ComputeReplicatedStateChecksum() : 0UL,
                CoreStateMode = coreStateMode,
                IsCoreStateEnabled = IsCoreStateEnabled,
                CoreAbilitySpecCount = IsCoreStateEnabled ? coreState.AbilitySpecCount : 0,
                CoreActiveEffectCount = IsCoreStateEnabled ? coreState.ActiveEffectCount : 0,
                CoreAttributeCount = IsCoreStateEnabled ? coreState.AttributeCount : 0,
                CoreModifierCount = IsCoreStateEnabled ? coreState.ModifierCount : 0,
                CoreSpecHandleCount = IsCoreStateEnabled ? coreSpecHandles.Count : 0,
                CoreActiveEffectHandleCount = IsCoreStateEnabled ? coreActiveEffectHandles.Count : 0,
                RuntimeThreadId = runtimeThreadId,
                CurrentThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId,
                RuntimeThreadViolationCount = runtimeThreadViolationCount,
                PendingRemovedEffectCount = pendingRemovedEffectReconciliationIds.Count,
                PendingRemovedAbilitySpecHandleCount = pendingRemovedAbilitySpecHandles.Count,
                PendingAddedTagCount = pendingAddedTags.Count,
                PendingRemovedTagCount = pendingRemovedTags.Count,
                ListPoolStatistics = listPoolStats
            };
        }

        //  Attribute snapshot for rolling back instant-effect attribute changes on prediction failure.
        // Populated in ApplyModifier when currentPredictionKey.IsValid; cleared on commit or rollback.
        private readonly List<(GASPredictionKey key, GameplayAttribute attr, long oldBaseValueRaw)> predictedAttributeSnapshots = new List<(GASPredictionKey, GameplayAttribute, long)>(8);

        // --- Process-local reconciliation ---
        // The ASC owns monotonically assigned active-effect reconciliation IDs.
        private int reconciliationApplyScopeDepth;
        private int effectMutationTransactionDepth;
        private int activeEffectIterationDepth;
        private int abilityEndMutationBypassDepth;
        private uint lastAppliedDeltaSequence;
        private ulong lastAppliedDeltaVersion;
        private bool hasAppliedDeltaSequence;
        private bool stateDeltaResyncRequired;
        private readonly HashSet<int> stateDeltaIdValidationScratch = new HashSet<int>();
        private readonly HashSet<string> stateDeltaNameValidationScratch = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<GameplayTag> stateDeltaTagValidationScratch = new HashSet<GameplayTag>();

        private readonly GASCoreStateMode coreStateMode;
        private readonly GASEntityId coreEntity;
        private readonly GASAbilitySystemState coreState;
        private readonly GASAbilitySystemFacade core;
        private readonly Dictionary<GameplayAbilitySpec, GASSpecHandle> coreSpecHandles;
        private readonly Dictionary<ActiveGameplayEffect, GASActiveEffectHandle> coreActiveEffectHandles;
        private GASModifierData[] coreModifierBuffer = Array.Empty<GASModifierData>();
        private GameplayTag[] effectReplicationSetByCallerTags = Array.Empty<GameplayTag>();
        private long[] effectReplicationSetByCallerValuesRaw = Array.Empty<long>();
        private string[] effectReplicationSetByCallerNames = Array.Empty<string>();
        private long[] effectReplicationSetByCallerNameValuesRaw = Array.Empty<long>();
        private GameplayTag[] effectReplicationDynamicGrantedTags = Array.Empty<GameplayTag>();
        private GameplayTag[] effectReplicationDynamicAssetTags = Array.Empty<GameplayTag>();
        private GameplayTag[] stateApplySetByCallerTags = Array.Empty<GameplayTag>();
        private long[] stateApplySetByCallerValuesRaw = Array.Empty<long>();
        private string[] stateApplySetByCallerNames = Array.Empty<string>();
        private long[] stateApplySetByCallerNameValuesRaw = Array.Empty<long>();
        private GameplayTag[] stateApplyDynamicGrantedTags = Array.Empty<GameplayTag>();
        private GameplayTag[] stateApplyDynamicAssetTags = Array.Empty<GameplayTag>();

        /// <summary>
        /// Current Core mirror mode. Runtime state remains authoritative in every mode.
        /// </summary>
        public GASCoreStateMode CoreStateMode => coreStateMode;

        /// <summary>
        /// True when this ASC owns a synchronized Core mirror.
        /// </summary>
        public bool IsCoreStateEnabled => coreState != null;

        /// <summary>
        /// Stable Core entity id assigned to this ASC even when Core mirroring is disabled.
        /// </summary>
        public GASEntityId CoreEntity => coreEntity;

        /// <summary>
        /// Optional Core mirror. Prefer TryGetCoreState when code can run in RuntimeOnly mode.
        /// </summary>
        public GASAbilitySystemState CoreState => coreState;

        /// <summary>
        /// Optional Core facade. Prefer TryGetCoreFacade when code can run in RuntimeOnly mode.
        /// </summary>
        public GASAbilitySystemFacade Core => core;

        /// <summary>
        /// Gets the optional Core mirror without assuming the ASC was constructed in MirrorRuntime mode.
        /// </summary>
        public bool TryGetCoreState(out GASAbilitySystemState state)
        {
            state = coreState;
            return state != null;
        }

        /// <summary>
        /// Gets the optional Core facade without assuming the ASC was constructed in MirrorRuntime mode.
        /// </summary>
        public bool TryGetCoreFacade(out GASAbilitySystemFacade facade)
        {
            facade = core;
            return facade != null;
        }

        public IGameplayEffectContextFactory EffectContextFactory { get; }

        // Cached ActorInfo to avoid repeated struct construction
        private GameplayAbilityActorInfo cachedActorInfo;

        // --- Events (UE5: OnGameplayEffectAppliedDelegateToSelf, OnAnyGameplayEffectRemovedDelegate) ---
        private readonly GASCallbackList<ActiveEffectDelegate> effectAppliedObservers = new GASCallbackList<ActiveEffectDelegate>(4);
        private readonly GASCallbackList<ActiveEffectDelegate> effectRemovedObservers = new GASCallbackList<ActiveEffectDelegate>(4);
        private readonly GASCallbackList<GameplayCueCommittedDelegate> gameplayCueCommittedObservers =
            new GASCallbackList<GameplayCueCommittedDelegate>(2);
        public event ActiveEffectDelegate OnGameplayEffectAppliedToSelf
        {
            add
            {
                AssertRuntimeThread();
                effectAppliedObservers.Add(value);
            }
            remove
            {
                AssertRuntimeThreadForCleanupRemoval();
                effectAppliedObservers.RemoveLast(value);
            }
        }
        public event ActiveEffectDelegate OnGameplayEffectRemovedFromSelf
        {
            add
            {
                AssertRuntimeThread();
                effectRemovedObservers.Add(value);
            }
            remove
            {
                AssertRuntimeThreadForCleanupRemoval();
                effectRemovedObservers.RemoveLast(value);
            }
        }

        /// <summary>
        /// Observes committed Gameplay Cues on the ASC owner thread. Each observer is isolated so an
        /// exception cannot affect the committed effect or suppress later observers.
        /// </summary>
        public event GameplayCueCommittedDelegate OnGameplayCueCommitted
        {
            add
            {
                AssertRuntimeThread();
                gameplayCueCommittedObservers.Add(value);
            }
            remove
            {
                AssertRuntimeThreadForCleanupRemoval();
                gameplayCueCommittedObservers.RemoveLast(value);
            }
        }

        // --- Ability Lifecycle Events (UE5: AbilityActivatedCallbacks, AbilityEndedCallbacks, AbilityCommittedCallbacks) ---
        private readonly GASCallbackList<Action<GameplayAbility>> abilityActivatedObservers = new GASCallbackList<Action<GameplayAbility>>(4);
        private readonly GASCallbackList<Action<GameplayAbility>> abilityEndedObservers = new GASCallbackList<Action<GameplayAbility>>(4);
        private readonly GASCallbackList<Action<GameplayAbility>> abilityCommittedObservers = new GASCallbackList<Action<GameplayAbility>>(4);
        private readonly GASCallbackList<Action<GASStateDeltaRejectionReason>> stateDeltaResyncObservers =
            new GASCallbackList<Action<GASStateDeltaRejectionReason>>(2);
        public event Action<GameplayAbility> OnAbilityActivated
        {
            add
            {
                AssertRuntimeThread();
                abilityActivatedObservers.Add(value);
            }
            remove
            {
                AssertRuntimeThreadForCleanupRemoval();
                abilityActivatedObservers.RemoveLast(value);
            }
        }
        public event Action<GameplayAbility> OnAbilityEndedEvent
        {
            add
            {
                AssertRuntimeThread();
                abilityEndedObservers.Add(value);
            }
            remove
            {
                AssertRuntimeThreadForCleanupRemoval();
                abilityEndedObservers.RemoveLast(value);
            }
        }
        public event Action<GameplayAbility> OnAbilityCommitted
        {
            add
            {
                AssertRuntimeThread();
                abilityCommittedObservers.Add(value);
            }
            remove
            {
                AssertRuntimeThreadForCleanupRemoval();
                abilityCommittedObservers.RemoveLast(value);
            }
        }
        public event Action<GASStateDeltaRejectionReason> OnStateDeltaResyncRequired
        {
            add
            {
                AssertRuntimeThread();
                stateDeltaResyncObservers.Add(value);
            }
            remove
            {
                AssertRuntimeThreadForCleanupRemoval();
                stateDeltaResyncObservers.RemoveLast(value);
            }
        }
        public bool StateDeltaResyncRequired => stateDeltaResyncRequired;

        // --- Gameplay Event System (UE5: SendGameplayEventToActor) ---
        private readonly Dictionary<GameplayTag, GASCallbackList<GameplayEventDelegate>> eventDelegates =
            new Dictionary<GameplayTag, GASCallbackList<GameplayEventDelegate>>(8);
        private readonly Dictionary<GameplayTag, GASCallbackList<OnTagCountChangedDelegate>> tagNewOrRemovedObservers =
            new Dictionary<GameplayTag, GASCallbackList<OnTagCountChangedDelegate>>(8);
        private readonly Dictionary<GameplayTag, GASCallbackList<OnTagCountChangedDelegate>> tagAnyCountObservers =
            new Dictionary<GameplayTag, GASCallbackList<OnTagCountChangedDelegate>>(8);

        // --- Ability Trigger System (UE5: FAbilityTriggerData) ---
        // Maps trigger tags to the specs whose abilities should be activated when the trigger fires.
        private readonly Dictionary<GameplayTag, List<GameplayAbilitySpec>> triggerEventAbilities = new Dictionary<GameplayTag, List<GameplayAbilitySpec>>(8);
        private readonly Dictionary<GameplayTag, List<GameplayAbilitySpec>> triggerTagAddedAbilities = new Dictionary<GameplayTag, List<GameplayAbilitySpec>>(8);
        private readonly Dictionary<GameplayTag, List<GameplayAbilitySpec>> triggerTagRemovedAbilities = new Dictionary<GameplayTag, List<GameplayAbilitySpec>>(8);
        private readonly List<GameplayAbilitySpec> deferredTriggerActivations = new List<GameplayAbilitySpec>(8);
        private bool flushingDeferredTriggerActivations;

        #region Tag Event Convenience API

        /// <summary>
        /// Registers a callback for when a specific tag is added or removed from this ASC.
        /// </summary>
        public void RegisterTagEventCallback(GameplayTag tag, GameplayTagEventType eventType, OnTagCountChangedDelegate callback)
        {
            AssertRuntimeThread();
            if (tag.IsNone || !tag.IsValid)
            {
                throw new ArgumentException("A valid gameplay tag is required.", nameof(tag));
            }
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            Dictionary<GameplayTag, GASCallbackList<OnTagCountChangedDelegate>> observers =
                GetTagObserverMap(eventType);
            if (!observers.TryGetValue(tag, out GASCallbackList<OnTagCountChangedDelegate> callbacks))
            {
                callbacks = new GASCallbackList<OnTagCountChangedDelegate>(2);
                observers.Add(tag, callbacks);
            }
            callbacks.Add(callback);
        }

        /// <summary>
        /// Removes a tag event callback.
        /// </summary>
        public void RemoveTagEventCallback(GameplayTag tag, GameplayTagEventType eventType, OnTagCountChangedDelegate callback)
        {
            AssertRuntimeThreadForCleanupRemoval();
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            Dictionary<GameplayTag, GASCallbackList<OnTagCountChangedDelegate>> observers =
                GetTagObserverMap(eventType);
            if (observers.TryGetValue(tag, out GASCallbackList<OnTagCountChangedDelegate> callbacks))
            {
                callbacks.RemoveLast(callback);
                if (callbacks.ActiveCount == 0)
                {
                    observers.Remove(tag);
                }
            }
        }

        private Dictionary<GameplayTag, GASCallbackList<OnTagCountChangedDelegate>> GetTagObserverMap(
            GameplayTagEventType eventType)
        {
            switch (eventType)
            {
                case GameplayTagEventType.NewOrRemoved:
                    return tagNewOrRemovedObservers;
                case GameplayTagEventType.AnyCountChange:
                    return tagAnyCountObservers;
                default:
                    throw new ArgumentOutOfRangeException(nameof(eventType), eventType, "Unknown gameplay tag event type.");
            }
        }

        /// <summary>
        /// Adds an immunity tag. Effects matching this tag will be blocked.
        /// </summary>
        public void AddImmunityTag(GameplayTag tag)
        {
            AssertRuntimeThread();
            if (!tag.IsNone && !immunityTags.HasTag(tag))
            {
                immunityTags.AddTag(tag);
            }
        }

        /// <summary>
        /// Removes an immunity tag.
        /// </summary>
        public void RemoveImmunityTag(GameplayTag tag)
        {
            AssertRuntimeThread();
            if (!tag.IsNone)
            {
                immunityTags.RemoveTag(tag);
            }
        }

        #endregion

        #region Gameplay Event System (UE5: SendGameplayEventToActor)

        /// <summary>
        /// Registers a delegate to handle gameplay events with a specific tag.
        /// </summary>
        public void RegisterGameplayEventCallback(GameplayTag eventTag, GameplayEventDelegate callback)
        {
            AssertRuntimeThread();
            if (eventTag.IsNone || callback == null) return;
            if (!eventDelegates.TryGetValue(eventTag, out GASCallbackList<GameplayEventDelegate> callbacks))
            {
                callbacks = new GASCallbackList<GameplayEventDelegate>(2);
                eventDelegates.Add(eventTag, callbacks);
            }
            callbacks.Add(callback);
        }

        /// <summary>
        /// Removes a gameplay event callback.
        /// </summary>
        public void RemoveGameplayEventCallback(GameplayTag eventTag, GameplayEventDelegate callback)
        {
            AssertRuntimeThreadForCleanupRemoval();
            if (eventTag.IsNone || callback == null) return;
            if (eventDelegates.TryGetValue(eventTag, out GASCallbackList<GameplayEventDelegate> callbacks))
            {
                callbacks.RemoveLast(callback);
                if (callbacks.ActiveCount == 0)
                {
                    eventDelegates.Remove(eventTag);
                }
            }
        }

        /// <summary>
        /// Sends a gameplay event to this ASC. Matching handlers and waiting AbilityTasks will be notified.
        /// Also triggers abilities registered with GameplayEvent trigger type.
        /// UE5 equivalent: UAbilitySystemBlueprintLibrary::SendGameplayEventToActor.
        /// </summary>
        public void HandleGameplayEvent(GameplayEventData eventData)
        {
            AssertRuntimeThread();
            if (eventData.EventTag.IsNone) return;

            if (eventDelegates.TryGetValue(eventData.EventTag, out GASCallbackList<GameplayEventDelegate> callbacks))
            {
                DispatchGameplayEventObservers(callbacks, eventData);
            }

            // Authority triggers always run after observer delivery, even if an observer fails.
            if (triggerEventAbilities.TryGetValue(eventData.EventTag, out var triggeredSpecs))
            {
                for (int i = 0; i < triggeredSpecs.Count; i++)
                {
                    var spec = triggeredSpecs[i];
                    if (!spec.IsActive)
                    {
                        RequestTriggerActivation(spec);
                    }
                }
            }
        }

        private void DispatchGameplayEventObservers(
            GASCallbackList<GameplayEventDelegate> callbacks,
            GameplayEventData eventData)
        {
            EnterRuntimeCallbackDispatch();
            bool callbackListDispatchStarted = false;
            try
            {
                int count = callbacks.BeginDispatch();
                callbackListDispatchStarted = true;
                for (int i = 0; i < count; i++)
                {
                    GameplayEventDelegate callback = callbacks.GetCallback(i);
                    if (callback == null)
                    {
                        continue;
                    }

                    try
                    {
                        callback.Invoke(eventData);
                    }
                    catch (Exception exception)
                    {
                        GASLog.Error($"GameplayEvent observer failed after event delivery began: {exception.Message}");
                    }
                }
            }
            finally
            {
                try
                {
                    if (callbackListDispatchStarted)
                    {
                        callbacks.EndDispatch();
                    }
                }
                finally
                {
                    ExitRuntimeCallbackDispatch();
                }
            }
        }

        #endregion

        public AbilitySystemComponent()
            : this(null, GASAbilitySystemRuntimeOptions.Default, null)
        {
        }

        public AbilitySystemComponent(
            GASRuntimeContext runtimeContext,
            GASAbilitySystemRuntimeOptions options = null,
            IGameplayEffectContextFactory effectContextFactory = null)
        {
            if (options == null)
            {
                options = GASAbilitySystemRuntimeOptions.Default;
            }

            ownsRuntimeContext = runtimeContext == null;
            RuntimeContext = runtimeContext ?? new GASRuntimeContext();
            Limits = options.Limits;
            EffectContextFactory = effectContextFactory ?? new GameplayEffectContextFactory();

            AbilitySpecs = new AbilitySpecContainer();
            ActiveEffectContainer = new ActiveEffectContainer();
            AttributeAggregator = new AttributeAggregator();
            PredictionManager = new PredictionManager();
            ReplicationStateBuilder = new ReplicationStateBuilder();
            CueDispatcher = new GameplayCueDispatcher(RuntimeContext);
            assertRuntimeAccess = AssertRuntimeThread;
            combinedTagsView = new GASReadOnlyTagView(combinedTags, assertRuntimeAccess);
            immunityTagsView = new GASReadOnlyTagView(immunityTags, assertRuntimeAccess);

            attributeSets = AttributeAggregator.MutableAttributeSets;
            attributeSetsView = new GASReadOnlyListView<AttributeSet>(attributeSets, assertRuntimeAccess);
            attributes = AttributeAggregator.MutableAttributes;
            dirtyAttributes = AttributeAggregator.MutableDirtyAttributes;
            modifierChannelAccumulators = new ModifierChannelAccumulator[GASModifierEvaluationChannels.MAX_CHANNEL_COUNT];

            activeEffects = ActiveEffectContainer.MutableActiveEffects;
            activeEffectsView = new GASReadOnlyListView<ActiveGameplayEffect>(activeEffects, assertRuntimeAccess);
            activeEffectIndexByEffect = ActiveEffectContainer.MutableIndexByEffect;
            activeEffectByReconciliationId = ActiveEffectContainer.MutableEffectByReconciliationId;
            stackingIndexByTarget = ActiveEffectContainer.MutableStackingByTarget;
            stackingIndexBySource = ActiveEffectContainer.MutableStackingBySource;
            grantedTagIndexToEffects = ActiveEffectContainer.MutableEffectsByGrantedTagIndex;
            abilityAppliedEffects = ActiveEffectContainer.MutableEffectsByAbility;
            abilityAppliedEffectRemovalScratch = ActiveEffectContainer.MutableAbilityEffectRemovalScratch;
            abilityAppliedEffectListPool = ActiveEffectContainer.MutableAbilityEffectListPool;

            activatableAbilities = AbilitySpecs.MutableActivatableAbilities;
            activatableAbilitiesView = new GASReadOnlyListView<GameplayAbilitySpec>(activatableAbilities, assertRuntimeAccess);
            abilitySpecByHandle = AbilitySpecs.MutableSpecByHandle;
            abilitySpecIndexBySpec = AbilitySpecs.MutableIndexBySpec;
            grantedAbilitySpecsByEffect = AbilitySpecs.MutableSpecsByGrantingEffect;
            grantedAbilitySpecListPool = AbilitySpecs.MutableGrantedSpecListPool;
            tickingAbilities = AbilitySpecs.MutableTickingAbilities;
            tickingAbilityIndexBySpec = AbilitySpecs.MutableTickingIndexBySpec;
            tickingAbilityTickSnapshot = new List<TickingAbilityLeaseSnapshot>(16);

            dirtyAttributeNamesView = new GASReadOnlySetView<string>(ReplicationStateBuilder.DirtyAttributeNames, assertRuntimeAccess);
            dirtyAttributeValueSnapshotsView = new GASReadOnlyListView<GameplayAttribute>(ReplicationStateBuilder.DirtyAttributeValueSnapshots, assertRuntimeAccess);
            pendingAddedTagsView = new GASReadOnlySetView<GameplayTag>(ReplicationStateBuilder.PendingAddedTags, assertRuntimeAccess);
            pendingRemovedTagsView = new GASReadOnlySetView<GameplayTag>(ReplicationStateBuilder.PendingRemovedTags, assertRuntimeAccess);

            BindRuntimeThreadToCurrent();
            RuntimeThreadPolicy = RuntimeContext.ThreadPolicy;
            coreEntity = RuntimeContext.RegisterAbilitySystem();
            coreStateMode = options.CoreStateMode;
            if (coreStateMode == GASCoreStateMode.MirrorRuntime)
            {
                coreState = new GASAbilitySystemState(coreEntity, options.CoreLimits);
                core = new GASAbilitySystemFacade(coreState);
                coreSpecHandles = new Dictionary<GameplayAbilitySpec, GASSpecHandle>(16);
                coreActiveEffectHandles = new Dictionary<ActiveGameplayEffect, GASActiveEffectHandle>(32);
            }
            else
            {
                coreState = null;
                core = null;
                coreSpecHandles = null;
                coreActiveEffectHandles = null;
            }

            combinedTags.OnAnyTagNewOrRemove += HandleCombinedTagCountChange;
            combinedTags.OnAnyTagCountChange += HandleCombinedAnyTagCountChange;
            looseTags.OnAnyTagNewOrRemove += TrackLooseTagCountChange;
        }

        public void InitAbilityActorInfo(object owner, object avatar)
        {
            AssertRuntimeThread();
            OwnerActor = owner;
            AvatarActor = avatar;
            OwnerUnityObject = owner as UnityEngine.Object;
            AvatarGameObject = ResolveActorGameObject(avatar);
            cachedActorInfo = new GameplayAbilityActorInfo(owner, avatar);
        }

        public void InitAbilityActorInfo(UnityEngine.Object owner, GameObject avatar)
        {
            InitAbilityActorInfo((object)owner, avatar);
        }

        public void ReserveRuntimeCapacity(
            int abilityCapacity = 16,
            int attributeCapacity = 32,
            int activeEffectCapacity = 32,
            int tickingAbilityCapacity = 16,
            int dirtyAttributeCapacity = 32,
            int predictedAttributeCapacity = 8,
            int predictionWindowCapacity = 16,
            int coreModifierCapacity = 128,
            int corePredictionCapacity = 32,
            int maxSetByCallerPerEffect = 8,
            int predictionTransactionRecordCapacity = DefaultPredictionTransactionRecordCapacity,
            int tagDeltaCapacity = 16)
        {
            AssertRuntimeThread();
            AbilitySpecs.Reserve(abilityCapacity, activeEffectCapacity);
            ActiveEffectContainer.Reserve(activeEffectCapacity, activeEffectCapacity, tagDeltaCapacity, activeEffectCapacity);
            AttributeAggregator.Reserve(Math.Min(attributeCapacity, 8), attributeCapacity);
            PredictionManager.Reserve(predictionWindowCapacity, predictionTransactionRecordCapacity);

            EnsureListCapacity(attributeSets, Math.Min(attributeCapacity, 8));
            EnsureListCapacity(activeEffects, activeEffectCapacity);
            EnsureDictionaryCapacity(activeEffectIndexByEffect, activeEffectCapacity);
            EnsureDictionaryCapacity(activeEffectByReconciliationId, activeEffectCapacity);
            EnsureListCapacity(activatableAbilities, abilityCapacity);
            EnsureDictionaryCapacity(abilitySpecByHandle, abilityCapacity);
            EnsureDictionaryCapacity(abilitySpecIndexBySpec, abilityCapacity);
            EnsureDictionaryCapacity(grantedAbilitySpecsByEffect, activeEffectCapacity);
            EnsureListCapacity(tickingAbilities, tickingAbilityCapacity);
            EnsureListCapacity(tickingAbilityTickSnapshot, tickingAbilityCapacity);
            EnsureDictionaryCapacity(tickingAbilityIndexBySpec, tickingAbilityCapacity);
            EnsureListCapacity(dirtyAttributes, dirtyAttributeCapacity);
            EnsureListCapacity(predictedAttributeSnapshots, predictedAttributeCapacity);
            ReplicationStateBuilder.Reserve(
                dirtyAttributeCapacity,
                tagDeltaCapacity,
                activeEffectCapacity,
                abilityCapacity);

            if (IsCoreStateEnabled && coreModifierBuffer.Length < coreModifierCapacity)
            {
                coreModifierBuffer = new GASModifierData[coreModifierCapacity];
            }

            EnsureEffectReplicationSetByCallerCapacity(maxSetByCallerPerEffect);
            EnsureStateApplySetByCallerCapacity(maxSetByCallerPerEffect);
            EnsurePredictionTransactionRecordCapacity(predictionTransactionRecordCapacity);
            stateDeltaIdValidationScratch.EnsureCapacity(Math.Max(0, Math.Max(abilityCapacity, activeEffectCapacity)));
            stateDeltaNameValidationScratch.EnsureCapacity(Math.Max(0, Math.Max(attributeCapacity, maxSetByCallerPerEffect)));
            stateDeltaTagValidationScratch.EnsureCapacity(Math.Max(0, Math.Max(tagDeltaCapacity, maxSetByCallerPerEffect)));

            if (IsCoreStateEnabled)
            {
                coreState.Reserve(
                    abilityCapacity,
                    attributeCapacity,
                    activeEffectCapacity,
                    coreModifierCapacity,
                    corePredictionCapacity);
            }
        }

        private static void EnsureListCapacity<T>(List<T> list, int capacity)
        {
            if (list != null && capacity > list.Capacity)
            {
                list.Capacity = capacity;
            }
        }

        private static void EnsureDictionaryCapacity<TKey, TValue>(Dictionary<TKey, TValue> dictionary, int capacity)
        {
            if (dictionary == null || capacity <= 0)
            {
                return;
            }

            dictionary.EnsureCapacity(capacity);
        }

        private void EnsurePredictionTransactionRecordCapacity(int capacity)
        {
            PredictionManager.EnsureTransactionRecordCapacity(capacity);
        }

        private static GameObject ResolveActorGameObject(object actor)
        {
            if (actor is GameObject gameObject)
            {
                return gameObject;
            }

            if (actor is Component component)
            {
                return component.gameObject;
            }

            return null;
        }

        private void RemoveActivatableAbilitySpec(GameplayAbilitySpec spec)
        {
            AbilitySpecs.RemoveSpec(spec);
        }

        private void AddTickingAbilitySpec(GameplayAbilitySpec spec)
        {
            if (AbilitySpecs.AddTickingSpec(spec) && tickingAbilityTickSnapshot.Capacity < tickingAbilities.Count)
            {
                tickingAbilityTickSnapshot.Capacity = tickingAbilities.Capacity;
            }
        }

        private bool RemoveTickingAbilitySpec(GameplayAbilitySpec spec)
        {
            return AbilitySpecs.RemoveTickingSpec(spec);
        }

        private void RegisterAbilityGrantedByEffect(ActiveGameplayEffect effect, GameplayAbilitySpec spec)
        {
            AbilitySpecs.RegisterGrantedByEffect(effect, spec, RentGrantedAbilitySpecList);
        }

        private void UnregisterAbilityGrantedByEffect(GameplayAbilitySpec spec)
        {
            AbilitySpecs.UnregisterGrantedByEffect(spec, ReturnGrantedAbilitySpecList);
        }

        private List<GameplayAbilitySpec> RentGrantedAbilitySpecList()
        {
            grantedAbilitySpecListGets++;
            if (grantedAbilitySpecListPool.Count > 0)
            {
                return grantedAbilitySpecListPool.Pop();
            }

            grantedAbilitySpecListMisses++;
            return new List<GameplayAbilitySpec>(DefaultGrantedAbilitySpecListCapacity);
        }

        private void ReturnGrantedAbilitySpecList(List<GameplayAbilitySpec> specs)
        {
            if (specs == null)
            {
                return;
            }

            specs.Clear();
            if (maxPooledGrantedAbilitySpecLists <= 0 ||
                grantedAbilitySpecListPool.Count >= maxPooledGrantedAbilitySpecLists ||
                specs.Capacity > maxRetainedGrantedAbilitySpecListCapacity)
            {
                grantedAbilitySpecListDiscards++;
                return;
            }

            grantedAbilitySpecListPool.Push(specs);
            if (grantedAbilitySpecListPool.Count > peakGrantedAbilitySpecListPoolSize)
            {
                peakGrantedAbilitySpecListPoolSize = grantedAbilitySpecListPool.Count;
            }
        }

        private void ReturnAllGrantedAbilitySpecLists()
        {
            AbilitySpecs.ReturnAllGrantedSpecLists(ReturnGrantedAbilitySpecList);
        }

        private List<ActiveGameplayEffect> RentAbilityAppliedEffectList()
        {
            abilityAppliedEffectListGets++;
            if (abilityAppliedEffectListPool.Count > 0)
            {
                return abilityAppliedEffectListPool.Pop();
            }

            abilityAppliedEffectListMisses++;
            return new List<ActiveGameplayEffect>(DefaultAbilityAppliedEffectListCapacity);
        }

        private void ReturnAbilityAppliedEffectList(List<ActiveGameplayEffect> effects)
        {
            if (effects == null)
            {
                return;
            }

            effects.Clear();
            if (maxPooledAbilityAppliedEffectLists <= 0 ||
                abilityAppliedEffectListPool.Count >= maxPooledAbilityAppliedEffectLists ||
                effects.Capacity > maxRetainedAbilityAppliedEffectListCapacity)
            {
                abilityAppliedEffectListDiscards++;
                return;
            }

            abilityAppliedEffectListPool.Push(effects);
            if (abilityAppliedEffectListPool.Count > peakAbilityAppliedEffectListPoolSize)
            {
                peakAbilityAppliedEffectListPoolSize = abilityAppliedEffectListPool.Count;
            }
        }

        private void ReturnAllAbilityAppliedEffectLists()
        {
            ActiveEffectContainer.ReturnAllAbilityAppliedEffectLists(ReturnAbilityAppliedEffectList);
        }

        public void PrewarmRuntimePools(int grantedAbilitySpecLists, int abilityAppliedEffectLists)
        {
            AssertRuntimeThread();
            PrewarmGrantedAbilitySpecListPool(grantedAbilitySpecLists);
            PrewarmAbilityAppliedEffectListPool(abilityAppliedEffectLists);
        }

        private void PrewarmGrantedAbilitySpecListPool(int count)
        {
            int target = count;
            if (target < 0)
            {
                target = 0;
            }

            if (target > maxPooledGrantedAbilitySpecLists)
            {
                target = maxPooledGrantedAbilitySpecLists;
            }

            if (target < 0)
            {
                target = 0;
            }

            while (grantedAbilitySpecListPool.Count < target)
            {
                grantedAbilitySpecListPool.Push(new List<GameplayAbilitySpec>(DefaultGrantedAbilitySpecListCapacity));
            }

            if (grantedAbilitySpecListPool.Count > peakGrantedAbilitySpecListPoolSize)
            {
                peakGrantedAbilitySpecListPoolSize = grantedAbilitySpecListPool.Count;
            }
        }

        private void PrewarmAbilityAppliedEffectListPool(int count)
        {
            int target = count;
            if (target < 0)
            {
                target = 0;
            }

            if (target > maxPooledAbilityAppliedEffectLists)
            {
                target = maxPooledAbilityAppliedEffectLists;
            }

            if (target < 0)
            {
                target = 0;
            }

            while (abilityAppliedEffectListPool.Count < target)
            {
                abilityAppliedEffectListPool.Push(new List<ActiveGameplayEffect>(DefaultAbilityAppliedEffectListCapacity));
            }

            if (abilityAppliedEffectListPool.Count > peakAbilityAppliedEffectListPoolSize)
            {
                peakAbilityAppliedEffectListPoolSize = abilityAppliedEffectListPool.Count;
            }
        }

        public GASRuntimeListPoolStatistics GetRuntimeListPoolStatistics()
        {
            AssertRuntimeThread();
            return new GASRuntimeListPoolStatistics
            {
                GrantedAbilitySpecListPoolSize = grantedAbilitySpecListPool.Count,
                AbilityAppliedEffectListPoolSize = abilityAppliedEffectListPool.Count,
                PeakGrantedAbilitySpecListPoolSize = peakGrantedAbilitySpecListPoolSize,
                PeakAbilityAppliedEffectListPoolSize = peakAbilityAppliedEffectListPoolSize,
                GrantedAbilitySpecListGets = grantedAbilitySpecListGets,
                GrantedAbilitySpecListMisses = grantedAbilitySpecListMisses,
                GrantedAbilitySpecListDiscards = grantedAbilitySpecListDiscards,
                AbilityAppliedEffectListGets = abilityAppliedEffectListGets,
                AbilityAppliedEffectListMisses = abilityAppliedEffectListMisses,
                AbilityAppliedEffectListDiscards = abilityAppliedEffectListDiscards,
                MaxPooledGrantedAbilitySpecLists = maxPooledGrantedAbilitySpecLists,
                MaxPooledAbilityAppliedEffectLists = maxPooledAbilityAppliedEffectLists,
                MaxRetainedGrantedAbilitySpecListCapacity = maxRetainedGrantedAbilitySpecListCapacity,
                MaxRetainedAbilityAppliedEffectListCapacity = maxRetainedAbilityAppliedEffectListCapacity
            };
        }

        public void ResetRuntimeListPoolStatistics()
        {
            AssertRuntimeThread();
            ResetRuntimeListPoolStatisticsInternal();
        }

        private void ResetRuntimeListPoolStatisticsInternal()
        {
            peakGrantedAbilitySpecListPoolSize = grantedAbilitySpecListPool.Count;
            peakAbilityAppliedEffectListPoolSize = abilityAppliedEffectListPool.Count;
            grantedAbilitySpecListGets = 0;
            grantedAbilitySpecListMisses = 0;
            grantedAbilitySpecListDiscards = 0;
            abilityAppliedEffectListGets = 0;
            abilityAppliedEffectListMisses = 0;
            abilityAppliedEffectListDiscards = 0;
        }

        public void ClearIdleRuntimeListPools()
        {
            AssertRuntimeThread();
            ClearIdleRuntimeListPoolsInternal();
        }

        private void ClearIdleRuntimeListPoolsInternal()
        {
            grantedAbilitySpecListPool.Clear();
            abilityAppliedEffectListPool.Clear();
            peakGrantedAbilitySpecListPoolSize = 0;
            peakAbilityAppliedEffectListPoolSize = 0;
        }

        public void AddAttributeSet(AttributeSet set)
        {
            AssertRuntimeThread();
            if (effectMutationTransactionDepth != 0 || activeEffectIterationDepth != 0)
            {
                throw new InvalidOperationException("Attribute-set registration cannot re-enter active-effect mutation or iteration.");
            }
            if (set != null && !attributeSets.Contains(set))
            {
                if (set.OwningAbilitySystemComponent != null && !ReferenceEquals(set.OwningAbilitySystemComponent, this))
                {
                    throw new InvalidOperationException($"AttributeSet '{set.GetType().FullName}' is already owned by another AbilitySystemComponent.");
                }

                if (attributeSets.Count >= Limits.MaxAttributeSets)
                {
                    throw new InvalidOperationException($"AbilitySystemComponent exceeded the AttributeSet limit of {Limits.MaxAttributeSets}.");
                }

                IReadOnlyCollection<GameplayAttribute> setAttributes = set.GetAttributes();
                var attributesToAdd = new GameplayAttribute[setAttributes.Count];
                int addedAttributeCount = 0;
                foreach (GameplayAttribute attr in setAttributes)
                {
                    if (attr == null || string.IsNullOrWhiteSpace(attr.Name))
                    {
                        throw new InvalidOperationException($"AttributeSet '{set.GetType().FullName}' contains a null or unnamed attribute.");
                    }

                    if (attributes.ContainsKey(attr.Name))
                    {
                        throw new InvalidOperationException($"Attribute '{attr.Name}' is already registered on this AbilitySystemComponent.");
                    }

                    attributesToAdd[addedAttributeCount++] = attr;
                }

                if (attributes.Count + addedAttributeCount > Limits.MaxAttributes)
                {
                    throw new InvalidOperationException($"AbilitySystemComponent exceeded the attribute limit of {Limits.MaxAttributes}.");
                }

                using (BeginReplicationMutationScope(attributeStructure: true))
                {
                    if (IsCoreStateEnabled)
                    {
                        var coreAttributeIds = new GASAttributeId[addedAttributeCount];
                        int additionalCoreAttributes = 0;
                        for (int i = 0; i < addedAttributeCount; i++)
                        {
                            GASAttributeId id = GetOrCreateCoreAttributeId(attributesToAdd[i].Name);
                            if (!id.IsValid)
                            {
                                throw new InvalidOperationException($"Core attribute registry rejected '{attributesToAdd[i].Name}'.");
                            }
                            coreAttributeIds[i] = id;
                            if (!coreState.TryGetAttribute(id, out _))
                            {
                                additionalCoreAttributes++;
                            }
                        }

                        if (additionalCoreAttributes > coreState.Limits.MaxAttributes - coreState.AttributeCount)
                        {
                            throw new InvalidOperationException(
                                $"Core state exceeded the attribute limit of {coreState.Limits.MaxAttributes}.");
                        }

                        for (int i = 0; i < addedAttributeCount; i++)
                        {
                            if (!core.SetNumericAttributeBaseRaw(coreAttributeIds[i], attributesToAdd[i].BaseValueRaw))
                            {
                                throw new InvalidOperationException($"Core state rejected attribute '{attributesToAdd[i].Name}'.");
                            }
                        }
                    }

                    attributeSets.Add(set);
                    set.OwningAbilitySystemComponent = this;
                    for (int i = 0; i < attributesToAdd.Length; i++)
                    {
                        GameplayAttribute attr = attributesToAdd[i];
                        attributes.Add(attr.Name, attr);
                    }

                    MarkAttributeStructureDirty();
                }
            }
        }

        /// <summary>
        /// Removes an AttributeSet from this ASC at runtime.
        /// UE5: UAbilitySystemComponent::GetSpawnedAttributes_Mutable().Remove()
        /// </summary>
        public void RemoveAttributeSet(AttributeSet set)
        {
            AssertRuntimeThread();
            if (effectMutationTransactionDepth != 0 || activeEffectIterationDepth != 0)
            {
                throw new InvalidOperationException("Attribute-set removal cannot re-enter active-effect mutation or iteration.");
            }
            if (set == null || !attributeSets.Contains(set)) return;

            IReadOnlyCollection<GameplayAttribute> setAttributes = set.GetAttributes();
            foreach (GameplayAttribute attr in setAttributes)
            {
                if (attr.ActiveModifierSourceCount > 0 || HasPredictedAttributeSnapshotForAttribute(attr))
                {
                    throw new InvalidOperationException(
                        $"AttributeSet '{set.GetType().FullName}' cannot detach while attribute '{attr.Name}' is referenced by an active effect or prediction.");
                }
            }

            if (IsCoreStateEnabled)
            {
                foreach (GameplayAttribute attr in setAttributes)
                {
                    if (!RuntimeContext.AttributeRegistry.TryGetAttributeId(attr.Name, out GASAttributeId id) ||
                        !core.CanRemoveNumericAttribute(id))
                    {
                        throw new InvalidOperationException($"Core state cannot detach attribute '{attr.Name}'.");
                    }
                }
            }

            using (BeginReplicationMutationScope(attributeStructure: true))
            {
                if (IsCoreStateEnabled)
                {
                    foreach (GameplayAttribute attr in setAttributes)
                    {
                        RuntimeContext.AttributeRegistry.TryGetAttributeId(attr.Name, out GASAttributeId id);
                        core.RemoveNumericAttribute(id);
                    }
                }

                foreach (GameplayAttribute attr in setAttributes)
                {
                    // Only remove if this set actually owns the attribute in our dictionary
                    if (attributes.TryGetValue(attr.Name, out var registered) && registered == attr)
                    {
                        attributes.Remove(attr.Name);
                    }
                    if (attr.IsDirty)
                    {
                        attr.IsDirty = false;
                        dirtyAttributes.Remove(attr);
                    }
                }

                attributeSets.Remove(set);
                set.OwningAbilitySystemComponent = null;
                MarkAttributeStructureDirty();
            }
        }

        public void MarkAttributeDirty(GameplayAttribute attribute)
        {
            AssertRuntimeThread();
            if (attribute != null &&
                (attribute.OwningSet == null ||
                 !ReferenceEquals(attribute.OwningSet.OwningAbilitySystemComponent, this) ||
                 !attributes.TryGetValue(attribute.Name, out GameplayAttribute registered) ||
                 !ReferenceEquals(registered, attribute)))
            {
                throw new InvalidOperationException("Only a registered attribute owned by this AbilitySystemComponent can be marked dirty.");
            }

            if (attribute == null)
            {
                return;
            }

            using (BeginReplicationMutationScope())
            {
                bool newlyDirty = false;
                if (!attribute.IsDirty)
                {
                    attribute.IsDirty = true;
                    dirtyAttributes.Add(attribute);
                    MarkAttributeValueDirty(attribute);
                    newlyDirty = true;
                }

                RegisterAttributeInCore(attribute);
                if (newlyDirty)
                {
                    MarkLiveAttributeDependentsDirty(attribute);
                }
            }
        }

        public GameplayAttribute GetAttribute(string name)
        {
            AssertRuntimeThread();
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            attributes.TryGetValue(name, out var attribute);
            return attribute;
        }

        internal int AllocateAbilitySpecHandle()
        {
            if (nextAbilitySpecHandle == int.MaxValue)
            {
                throw new InvalidOperationException("The AbilitySystemComponent exhausted its ability spec handle space.");
            }

            nextAbilitySpecHandle++;
            return nextAbilitySpecHandle;
        }

        internal void ObserveAbilitySpecHandle(int handle)
        {
            if (handle <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(handle), handle, "Ability spec handles must be positive.");
            }

            if (handle > nextAbilitySpecHandle)
            {
                nextAbilitySpecHandle = handle;
            }
        }

        private int AllocateActiveEffectReconciliationId()
        {
            if (nextActiveEffectReconciliationId == int.MaxValue)
            {
                throw new InvalidOperationException(
                    "The AbilitySystemComponent exhausted its process-local active-effect reconciliation ID space.");
            }

            nextActiveEffectReconciliationId++;
            return nextActiveEffectReconciliationId;
        }

        private void ObserveActiveEffectReconciliationId(int reconciliationId)
        {
            if (reconciliationId <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(reconciliationId),
                    reconciliationId,
                    "Active-effect reconciliation IDs must be positive.");
            }

            if (reconciliationId > nextActiveEffectReconciliationId)
            {
                nextActiveEffectReconciliationId = reconciliationId;
            }
        }

        internal void NotifyDirectCurrentValueChanged(GameplayAttribute attribute)
        {
            AssertRuntimeThread();
            if (attribute == null || attribute.OwningSet == null ||
                !ReferenceEquals(attribute.OwningSet.OwningAbilitySystemComponent, this) ||
                !attributes.TryGetValue(attribute.Name, out GameplayAttribute registered) ||
                !ReferenceEquals(registered, attribute))
            {
                throw new InvalidOperationException("Only an attribute owned by this AbilitySystemComponent can publish a direct current-value change.");
            }

            using (BeginReplicationMutationScope())
            {
                MarkAttributeValueDirty(attribute);
            }
        }

        public GameplayAbilitySpec GrantAbility(GameplayAbility ability, int level = 1, int replicatedHandle = 0)
        {
            AssertRuntimeThread();
            if (ability == null) return null;
            if (!ability.IsConfigurationInitialized)
            {
                throw new InvalidOperationException("Cannot grant an uninitialized GameplayAbility definition.");
            }
            if (ability.InstancingPolicy == EGameplayAbilityInstancingPolicy.NonInstanced)
            {
                throw new InvalidOperationException(
                    "Unity Runtime abilities cannot use NonInstanced because a shared definition cannot safely own ASC, task, or activation state. " +
                    "Use InstancedPerActor/InstancedPerExecution, or use the pure Core state for stateless simulation commands.");
            }
            if (activatableAbilities.Count >= Limits.MaxGrantedAbilities)
            {
                throw new InvalidOperationException($"AbilitySystemComponent exceeded the granted ability limit of {Limits.MaxGrantedAbilities}.");
            }
            if (replicatedHandle > 0 && AbilitySpecs.TryGetSpecByHandle(replicatedHandle, out _))
            {
                throw new InvalidOperationException($"Ability spec handle {replicatedHandle} is already registered.");
            }

            using (BeginReplicationMutationScope())
            {
                var spec = GameplayAbilitySpec.Create(ability, this, level, replicatedHandle);
                bool specAdded = false;
                try
                {
                    if (!AbilitySpecs.AddSpec(spec))
                    {
                        throw new InvalidOperationException($"Failed to register ability spec handle {spec.Handle}.");
                    }
                    specAdded = true;

                    if (ability.InstancingPolicy == EGameplayAbilityInstancingPolicy.InstancedPerActor)
                    {
                        spec.CreateInstance();
                    }
                    RegisterGrantedAbilityInCore(spec, ability, level);

                    // UE5: Register ability triggers (FAbilityTriggerData)
                    RegisterAbilityTriggers(spec);
                    MarkGrantedAbilitiesDirty();

                    // UE5: bActivateAbilityOnGranted --auto-activate passive abilities
                    if (ability.ActivateAbilityOnGranted && reconciliationApplyScopeDepth == 0)
                    {
                        RequestTriggerActivation(spec);
                    }

                    return spec;
                }
                catch
                {
                    UnregisterAbilityTriggers(spec);
                    RemoveGrantedAbilityFromCore(spec);
                    RemoveActivatableAbilitySpec(spec);
                    RemoveTickingAbilitySpec(spec);
                    try
                    {
                        if (specAdded)
                        {
                            abilityEndMutationBypassDepth++;
                            try
                            {
                                spec.OnRemoveSpec();
                            }
                            finally
                            {
                                abilityEndMutationBypassDepth--;
                            }
                        }
                    }
                    catch (Exception cleanupException)
                    {
                        GASLog.Error($"Ability grant rollback cleanup failed for '{ability.Name}': {cleanupException.Message}");
                    }
                    finally
                    {
                        spec.ReleaseRuntimeLease();
                        MarkGrantedAbilitiesDirty();
                    }

                    throw;
                }
            }
        }

        public void ClearAbility(GameplayAbilitySpec spec)
        {
            AssertRuntimeThread();
            if (effectMutationTransactionDepth != 0 || activeEffectIterationDepth != 0)
            {
                throw new InvalidOperationException("Ability removal cannot re-enter active-effect mutation or iteration.");
            }

            ClearAbilityInternal(spec);
        }

        private void ClearAbilityInternal(GameplayAbilitySpec spec)
        {
            if (spec == null) return;
            if (!ReferenceEquals(spec.Owner, this) ||
                !AbilitySpecs.TryGetSpecByHandle(spec.Handle, out GameplayAbilitySpec registeredSpec) ||
                !ReferenceEquals(registeredSpec, spec))
            {
                throw new InvalidOperationException("Cannot clear an ability spec that is not owned by this AbilitySystemComponent.");
            }
            if (spec.ActivationCallInProgress)
            {
                throw new InvalidOperationException("Cannot clear an ability spec while its activation call is in progress.");
            }
            if (spec.EndCallInProgress)
            {
                throw new InvalidOperationException("Cannot clear an ability spec while its end call is in progress.");
            }

            using (BeginReplicationMutationScope())
            {
                int removedSpecHandle = spec.Handle;

                // UE5: Unregister ability triggers before removal
                UnregisterAbilityTriggers(spec);
                UnregisterAbilityGrantedByEffect(spec);

                RemoveActivatableAbilitySpec(spec);
                RemoveTickingAbilitySpec(spec);

                try
                {
                    abilityEndMutationBypassDepth++;
                    try
                    {
                        spec.OnRemoveSpec();
                    }
                    finally
                    {
                        abilityEndMutationBypassDepth--;
                    }
                }
                finally
                {
                    RemoveGrantedAbilityFromCore(spec);
                    spec.ReleaseRuntimeLease();

                    TrackRemovedAbilitySpecHandle(removedSpecHandle);
                    MarkGrantedAbilitiesDirty();
                }
            }
        }

        // --- Ability Activation Flow ---
        public bool TryActivateAbility(GameplayAbilitySpec spec)
        {
            AssertRuntimeThread();
            if (effectMutationTransactionDepth != 0 || activeEffectIterationDepth != 0)
            {
                return false;
            }
            if (stateDeltaResyncRequired)
            {
                return false;
            }

            if (spec == null)
            {
                if (GASTrace.Enabled)
                {
                    GASTrace.Record(GASTraceEventType.AbilityActivateBlocked, this, decision: GASTraceDecision.Blocked, reason: GASTraceReason.MissingSpec);
                }
                return false;
            }

            if (!ReferenceEquals(spec.Owner, this) ||
                !AbilitySpecs.TryGetSpecByHandle(spec.Handle, out GameplayAbilitySpec registeredSpec) ||
                !ReferenceEquals(registeredSpec, spec))
            {
                if (GASTrace.Enabled)
                {
                    GASTrace.Record(GASTraceEventType.AbilityActivateBlocked, this, decision: GASTraceDecision.Blocked, reason: GASTraceReason.MissingSpec, abilitySpecHandle: spec.Handle);
                }
                return false;
            }

            if (spec.IsActive)
            {
                if (GASTrace.Enabled)
                {
                    GASTrace.Record(GASTraceEventType.AbilityActivateBlocked, this, spec.GetPrimaryInstance(), decision: GASTraceDecision.Blocked, reason: GASTraceReason.AlreadyActive, abilitySpecHandle: spec.Handle);
                }
                return false;
            }

            var ability = spec.GetPrimaryInstance();
            if (ability == null)
            {
                if (GASTrace.Enabled)
                {
                    GASTrace.Record(GASTraceEventType.AbilityActivateBlocked, this, decision: GASTraceDecision.Blocked, reason: GASTraceReason.MissingAbility, abilitySpecHandle: spec.Handle);
                }
                return false;
            }

            if (GASTrace.Enabled)
            {
                GASTrace.Record(GASTraceEventType.AbilityActivateAttempt, this, ability, abilitySpecHandle: spec.Handle);
            }

            if (!RuntimeContext.HasAuthority &&
                (ability.ExecutionPolicy == EAbilityExecutionPolicy.AuthorityOnly ||
                 ability.ExecutionPolicy == EAbilityExecutionPolicy.LocalPredicted))
            {
                return false;
            }

            if (ability.ExecutionPolicy == EAbilityExecutionPolicy.AuthorityOnly ||
                ability.ExecutionPolicy == EAbilityExecutionPolicy.LocalPredicted)
            {
                return TryExecuteAuthorityAbility(spec).Activated;
            }

            if (!ability.CanActivate(cachedActorInfo, spec))
            {
                return false;
            }

            switch (ability.ExecutionPolicy)
            {
                case EAbilityExecutionPolicy.LocalOnly:
                    ActivateAbilityInternal(spec, new GameplayAbilityActivationInfo()); // No prediction key
                    break;
                default:
                    return false;
            }

            return true;
        }

        /// <summary>Executes a locally predicted ability and returns its reconciliation key.</summary>
        public bool TryActivatePredictedAbility(
            GameplayAbilitySpec spec,
            out GASPredictionKey predictionKey)
        {
            AssertRuntimeThread();
            predictionKey = default;
            if (RuntimeContext.HasAuthority ||
                effectMutationTransactionDepth != 0 ||
                activeEffectIterationDepth != 0 ||
                stateDeltaResyncRequired ||
                spec == null ||
                !ReferenceEquals(spec.Owner, this) ||
                !AbilitySpecs.TryGetSpecByHandle(spec.Handle, out GameplayAbilitySpec registeredSpec) ||
                !ReferenceEquals(registeredSpec, spec) ||
                spec.IsActive)
            {
                return false;
            }

            GameplayAbility ability = spec.GetPrimaryInstance();
            if (ability == null ||
                ability.ExecutionPolicy != EAbilityExecutionPolicy.LocalPredicted ||
                !ability.CanActivate(cachedActorInfo, spec))
            {
                return false;
            }

            predictionKey = OpenPredictionWindow(spec);
            ActivateAbilityInternal(spec, new GameplayAbilityActivationInfo { PredictionKey = predictionKey });
            return true;
        }

        /// <summary>Delivers an input edge to one exact granted ability spec.</summary>
        public bool TrySetAbilityInputPressed(GameplayAbilitySpec spec, bool isPressed)
        {
            AssertRuntimeThread();
            if (effectMutationTransactionDepth != 0 ||
                activeEffectIterationDepth != 0 ||
                spec == null ||
                !ReferenceEquals(spec.Owner, this) ||
                !AbilitySpecs.TryGetSpecByHandle(spec.Handle, out GameplayAbilitySpec registeredSpec) ||
                !ReferenceEquals(registeredSpec, spec))
            {
                return false;
            }

            if (spec.IsInputPressed == isPressed)
                return true;

            using (BeginReplicationMutationScope())
            {
                spec.IsInputPressed = isPressed;
                MarkGrantedAbilitiesDirty();
                if (spec.IsLocallyExecuting)
                {
                    GameplayAbility ability = spec.GetPrimaryInstance();
                    if (isPressed)
                        ability?.InputPressed(spec);
                    else
                        ability?.InputReleased(spec);
                }
            }
            return true;
        }

        /// <summary>Cancels one active exact grant without searching by ability definition.</summary>
        public bool TryCancelAbility(GameplayAbilitySpec spec)
        {
            AssertRuntimeThread();
            if (effectMutationTransactionDepth != 0 ||
                activeEffectIterationDepth != 0 ||
                spec == null ||
                !ReferenceEquals(spec.Owner, this) ||
                !AbilitySpecs.TryGetSpecByHandle(spec.Handle, out GameplayAbilitySpec registeredSpec) ||
                !ReferenceEquals(registeredSpec, spec) ||
                !spec.IsActive ||
                !spec.IsLocallyExecuting)
            {
                return false;
            }

            GameplayAbility ability = spec.GetPrimaryInstance();
            ability?.CancelAbility();
            if (spec.IsActive)
            {
                // A derived CancelAbility implementation may omit the base call. The ASC still owns
                // the active-state invariant and must complete cancellation deterministically.
                ability?.EndAbility();
            }
            return !spec.IsActive;
        }

        /// <summary>
        /// Executes one authority-approved ability by its local spec.
        /// </summary>
        /// <remarks>
        /// A network endpoint must authenticate the sender, validate target ownership, apply replay
        /// and rate limits, and resolve its authority-issued grant ID to this exact local spec before
        /// calling. This method owns no transport state and sends no callbacks.
        /// </remarks>
        public GASAuthorityActivationResult TryExecuteAuthorityAbility(GameplayAbilitySpec spec)
        {
            return TryExecuteAuthorityAbility(spec, default);
        }

        /// <summary>
        /// Executes one authority-approved ability and propagates a validated command correlation
        /// key into effects and cues created by that activation.
        /// </summary>
        /// <remarks>
        /// The correlation key is process-local metadata, not a wire identity. A network adapter
        /// may encode its positive input sequence in its own versioned protocol.
        /// </remarks>
        public GASAuthorityActivationResult TryExecuteAuthorityAbility(
            GameplayAbilitySpec spec,
            GASPredictionKey commandCorrelationKey)
        {
            AssertRuntimeThread();
            ulong currentStateVersion = stateVersion;
            if (!RuntimeContext.HasAuthority ||
                effectMutationTransactionDepth != 0 ||
                activeEffectIterationDepth != 0 ||
                stateDeltaResyncRequired)
            {
                return new GASAuthorityActivationResult(
                    GASAuthorityActivationStatus.RuntimeUnavailable,
                    currentStateVersion);
            }

            if (!IsCanonicalPredictionKey(commandCorrelationKey) ||
                (commandCorrelationKey.IsValid && commandCorrelationKey.Owner != coreEntity))
            {
                return new GASAuthorityActivationResult(
                    GASAuthorityActivationStatus.AbilityRejected,
                    currentStateVersion);
            }

            if (spec == null ||
                !ReferenceEquals(spec.Owner, this) ||
                !AbilitySpecs.TryGetSpecByHandle(spec.Handle, out GameplayAbilitySpec registeredSpec) ||
                !ReferenceEquals(registeredSpec, spec))
            {
                return new GASAuthorityActivationResult(
                    GASAuthorityActivationStatus.MissingOrStaleGrant,
                    currentStateVersion);
            }

            GameplayAbility ability = spec.GetPrimaryInstance();
            if (ability == null)
            {
                return new GASAuthorityActivationResult(
                    GASAuthorityActivationStatus.MissingOrStaleGrant,
                    currentStateVersion);
            }

            if (ability.ExecutionPolicy != EAbilityExecutionPolicy.AuthorityOnly &&
                ability.ExecutionPolicy != EAbilityExecutionPolicy.LocalPredicted)
            {
                return new GASAuthorityActivationResult(
                    GASAuthorityActivationStatus.WrongExecutionPolicy,
                    currentStateVersion);
            }

            if (spec.IsActive || !ability.CanActivate(cachedActorInfo, spec))
            {
                return new GASAuthorityActivationResult(
                    GASAuthorityActivationStatus.AbilityRejected,
                    stateVersion);
            }

            ActivateAbilityInternal(
                spec,
                new GameplayAbilityActivationInfo { PredictionKey = commandCorrelationKey });
            return new GASAuthorityActivationResult(
                GASAuthorityActivationStatus.Activated,
                stateVersion);
        }

        public void CaptureCoreStateNonAlloc(GASAbilitySystemStateBuffer buffer)
        {
            if (!IsCoreStateEnabled || buffer == null)
            {
                return;
            }

            coreState.CaptureStateNonAlloc(buffer);
        }

        /// <summary>
        /// Captures the complete process-local replicated state without consuming pending changes.
        /// </summary>
        /// <remarks>
        /// The destination contains runtime object references and local handles. It is suitable for
        /// owner-thread adapters and recovery staging, but must never be serialized directly.
        /// </remarks>
        public void CaptureFullStateNonAlloc(GASAbilitySystemFullStateBuffer destination)
        {
            AssertRuntimeThread();
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (effectMutationTransactionDepth != 0 || activeEffectIterationDepth != 0)
            {
                throw new InvalidOperationException(
                    "A full GAS state snapshot cannot be captured during an active-effect mutation or iteration.");
            }

            ulong capturedVersion = stateVersion;
            destination.ClearCounts();
            destination.StateVersion = capturedVersion;
            destination.GrantedAbilityCount = FillGrantedAbilities(
                destination.EnsureGrantedAbilityCapacity(activatableAbilities.Count));
            destination.ActiveEffectCount = FillActiveEffects(
                destination,
                destination.EnsureActiveEffectCapacity(activeEffects.Count));
            destination.AttributeCount = FillAttributes(
                destination.EnsureAttributeCapacity(CountAttributes()));
            destination.LooseTagCount = FillLooseTagCounts(
                destination.EnsureLooseTagCapacity(looseTags.ExplicitTagCount));
            destination.StateChecksum = ComputeReplicatedStateChecksum();

            if (capturedVersion != stateVersion)
            {
                destination.ClearCounts();
                throw new InvalidOperationException(
                    "GAS state changed while a full-state snapshot was being captured.");
            }
        }

        internal bool SuppressLocalGameplayCueDispatch => reconciliationApplyScopeDepth > 0 && !RuntimeContext.HasAuthority;

        private GameplayAbilitySpec FindSpecByHandle(int handle)
        {
            return handle > 0 && abilitySpecByHandle.TryGetValue(handle, out var spec) ? spec : null;
        }

        private ActiveGameplayEffect FindActiveEffectByReconciliationId(int reconciliationId)
        {
            return ActiveEffectContainer.FindByReconciliationId(reconciliationId);
        }

        private void SetActiveEffectReconciliationId(ActiveGameplayEffect effect, int reconciliationId)
        {
            ActiveEffectContainer.SetReconciliationId(effect, reconciliationId);
        }

        /// <summary>
        /// Computes an order-independent checksum over semantic replicated ASC gameplay state.
        /// Volatile local countdown timers and local ability execution state are intentionally excluded.
        /// The steady-state path is allocation-free after SetByCaller scratch capacity is reserved.
        /// </summary>
        public ulong ComputeReplicatedStateChecksum()
        {
            ulong hash = Fnv1a64.OffsetBasis;
            var abilityAccumulator = new UnorderedHashAccumulator();
            for (int i = 0; i < activatableAbilities.Count; i++)
            {
                var spec = activatableAbilities[i];
                ulong entryHash = Fnv1a64.OffsetBasis;
                entryHash = HashInt(entryHash, spec.Handle);
                entryHash = HashInt(entryHash, spec.Level);
                entryHash = HashInt(entryHash, spec.GrantingEffect?.ReconciliationId ?? 0);
                GameplayAbility ability = spec.AbilityCDO ?? spec.Ability;
                entryHash = HashString(entryHash, ability?.Name);
                entryHash = HashInt(entryHash, (int)(ability?.InstancingPolicy ?? default));
                entryHash = HashInt(entryHash, (int)(ability?.ExecutionPolicy ?? default));
                abilityAccumulator.Add(entryHash);
            }
            abilityAccumulator.FoldInto(ref hash);

            var effectAccumulator = new UnorderedHashAccumulator();
            for (int i = 0; i < activeEffects.Count; i++)
            {
                var effect = activeEffects[i];
                if (effect == null || effect.IsExpired)
                {
                    continue;
                }

                ulong entryHash = Fnv1a64.OffsetBasis;
                entryHash = HashInt(entryHash, effect.ReconciliationId);
                entryHash = HashString(entryHash, effect.Spec?.Def?.Name);
                entryHash = HashInt(entryHash, effect.Spec?.Level ?? 0);
                entryHash = HashInt(entryHash, effect.StackCount);
                entryHash = HashInt(entryHash, effect.IsInhibited ? 1 : 0);
                entryHash = HashInt(entryHash, effect.SourceAbilitySpecHandle);
                entryHash = HashLong(entryHash, effect.Spec?.DurationRaw ?? 0L);
                GASPredictionKey predictionKey = effect.Spec?.Context?.PredictionKey ?? default;
                entryHash = HashInt(entryHash, predictionKey.Value);
                entryHash = HashInt(entryHash, predictionKey.Owner.Value);
                entryHash = HashInt(entryHash, predictionKey.InputSequence);

                int setByCallerCount = effect.Spec?.SetByCallerTagMagnitudeCount ?? 0;
                EnsureEffectReplicationSetByCallerCapacity(setByCallerCount);
                if (setByCallerCount > 0)
                {
                    setByCallerCount = effect.Spec.CopySetByCallerTagMagnitudesRaw(
                        effectReplicationSetByCallerTags,
                        effectReplicationSetByCallerValuesRaw);
                }

                var setByCallerAccumulator = new UnorderedHashAccumulator();
                for (int setByCallerIndex = 0; setByCallerIndex < setByCallerCount; setByCallerIndex++)
                {
                    ulong setByCallerHash = HashString(
                        Fnv1a64.OffsetBasis,
                        effectReplicationSetByCallerTags[setByCallerIndex].Name);
                    setByCallerHash = HashLong(
                        setByCallerHash,
                        effectReplicationSetByCallerValuesRaw[setByCallerIndex]);
                    setByCallerAccumulator.Add(setByCallerHash);
                }
                setByCallerAccumulator.FoldInto(ref entryHash);

                int setByCallerNameCount = effect.Spec?.SetByCallerNameMagnitudeCount ?? 0;
                int dynamicGrantedTagCount = effect.Spec?.DynamicGrantedTags.ExplicitTagCount ?? 0;
                int dynamicAssetTagCount = effect.Spec?.DynamicAssetTags.ExplicitTagCount ?? 0;
                EnsureEffectReplicationExtendedCapacity(
                    setByCallerNameCount,
                    dynamicGrantedTagCount,
                    dynamicAssetTagCount);
                if (setByCallerNameCount > 0)
                {
                    setByCallerNameCount = effect.Spec.CopySetByCallerNameMagnitudesRaw(
                        effectReplicationSetByCallerNames,
                        effectReplicationSetByCallerNameValuesRaw);
                }

                var nameAccumulator = new UnorderedHashAccumulator();
                for (int nameIndex = 0; nameIndex < setByCallerNameCount; nameIndex++)
                {
                    ulong nameHash = HashString(Fnv1a64.OffsetBasis, effectReplicationSetByCallerNames[nameIndex]);
                    nameHash = HashLong(nameHash, effectReplicationSetByCallerNameValuesRaw[nameIndex]);
                    nameAccumulator.Add(nameHash);
                }
                nameAccumulator.FoldInto(ref entryHash);

                dynamicGrantedTagCount = CopyExplicitTags(
                    effect.Spec.DynamicGrantedTags,
                    effectReplicationDynamicGrantedTags);
                dynamicAssetTagCount = CopyExplicitTags(
                    effect.Spec.DynamicAssetTags,
                    effectReplicationDynamicAssetTags);
                var dynamicTagAccumulator = new UnorderedHashAccumulator();
                for (int tagIndex = 0; tagIndex < dynamicGrantedTagCount; tagIndex++)
                {
                    dynamicTagAccumulator.Add(HashString(
                        Fnv1a64.OffsetBasis,
                        effectReplicationDynamicGrantedTags[tagIndex].Name));
                }
                for (int tagIndex = 0; tagIndex < dynamicAssetTagCount; tagIndex++)
                {
                    dynamicTagAccumulator.Add(HashInt(
                        HashString(Fnv1a64.OffsetBasis, effectReplicationDynamicAssetTags[tagIndex].Name),
                        1));
                }
                dynamicTagAccumulator.FoldInto(ref entryHash);
                effectAccumulator.Add(entryHash);
            }
            effectAccumulator.FoldInto(ref hash);

            var attributeAccumulator = new UnorderedHashAccumulator();
            for (int i = 0; i < attributeSets.Count; i++)
            {
                foreach (var attribute in attributeSets[i].GetAttributes())
                {
                    ulong entryHash = Fnv1a64.OffsetBasis;
                    entryHash = HashString(entryHash, attribute.Name);
                    entryHash = HashLong(entryHash, attribute.BaseValueRaw);
                    entryHash = HashLong(entryHash, attribute.CurrentValueRaw);
                    attributeAccumulator.Add(entryHash);
                }
            }
            attributeAccumulator.FoldInto(ref hash);

            var tagAccumulator = new UnorderedHashAccumulator();
            var tags = combinedTags.GetTags();
            while (tags.MoveNext())
            {
                var tag = tags.Current;
                ulong entryHash = HashString(Fnv1a64.OffsetBasis, tag.IsValid && !tag.IsNone ? tag.Name : string.Empty);
                tagAccumulator.Add(entryHash);
            }
            tagAccumulator.FoldInto(ref hash);

            return hash;
        }

        private struct UnorderedHashAccumulator
        {
            private ulong sum;
            private ulong xor;
            private ulong sumOfProducts;
            private int count;

            public void Add(ulong entryHash)
            {
                unchecked
                {
                    ulong mixed = AvalancheHash(entryHash);
                    sum += mixed;
                    xor ^= RotateLeft(mixed, 23);
                    sumOfProducts += mixed * (mixed | 1UL);
                    count++;
                }
            }

            public void FoldInto(ref ulong hash)
            {
                hash = HashInt(hash, count);
                hash = HashLong(hash, unchecked((long)sum));
                hash = HashLong(hash, unchecked((long)xor));
                hash = HashLong(hash, unchecked((long)sumOfProducts));
            }
        }

        private static ulong AvalancheHash(ulong value)
        {
            unchecked
            {
                value ^= value >> 30;
                value *= 0xbf58476d1ce4e5b9UL;
                value ^= value >> 27;
                value *= 0x94d049bb133111ebUL;
                return value ^ (value >> 31);
            }
        }

        private static ulong RotateLeft(ulong value, int count)
        {
            return (value << count) | (value >> (64 - count));
        }

        private static ulong HashInt(ulong hash, int value)
        {
            unchecked
            {
                hash = (hash ^ (uint)value) * Fnv1a64.Prime;
                return hash;
            }
        }

        private static ulong HashFloat(ulong hash, float value)
        {
            long raw = GASFixedValue.FromFloat(value).RawValue;
            return HashLong(hash, raw);
        }

        private static ulong HashLong(ulong hash, long raw)
        {
            hash = HashInt(hash, unchecked((int)raw));
            return HashInt(hash, unchecked((int)(raw >> 32)));
        }

        private static ulong HashString(ulong hash, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return HashInt(hash, 0);
            }

            unchecked
            {
                for (int i = 0; i < value.Length; i++)
                {
                    hash = (hash ^ value[i]) * Fnv1a64.Prime;
                }

                return HashInt(hash, value.Length);
            }
        }

        private void RegisterGrantedAbilityInCore(GameplayAbilitySpec spec, GameplayAbility ability, int level)
        {
            if (!IsCoreStateEnabled)
            {
                return;
            }

            if (spec == null || ability == null)
            {
                return;
            }

            if (coreSpecHandles.ContainsKey(spec))
            {
                return;
            }

            var definitionId = GetOrRegisterCoreAbilityDefinitionId(ability);
            var handle = core.GiveAbility(
                definitionId,
                checked((ushort)level),
                ConvertInstancingPolicy(ability.InstancingPolicy));

            if (handle.IsValid)
            {
                coreSpecHandles[spec] = handle;
                return;
            }

            throw new InvalidOperationException($"Core state rejected ability '{ability.Name}' because its configured capacity was exhausted.");
        }

        private void RemoveGrantedAbilityFromCore(GameplayAbilitySpec spec)
        {
            if (!IsCoreStateEnabled)
            {
                return;
            }

            if (spec == null)
            {
                return;
            }

            if (!coreSpecHandles.TryGetValue(spec, out var handle))
            {
                return;
            }

            coreSpecHandles.Remove(spec);
            core.ClearAbility(handle);
        }

        private void RegisterAttributeInCore(GameplayAttribute attribute)
        {
            if (!IsCoreStateEnabled)
            {
                return;
            }

            if (attribute == null)
            {
                return;
            }

            var attributeId = GetOrCreateCoreAttributeId(attribute.Name);
            if (attributeId.IsValid)
            {
                core.SetNumericAttributeBaseRaw(attributeId, attribute.BaseValueRaw);
            }
        }

        private void RegisterActiveEffectInCore(ActiveGameplayEffect effect)
        {
            if (!IsCoreStateEnabled)
            {
                return;
            }

            if (effect == null || effect.Spec == null || effect.Spec.Def == null)
            {
                return;
            }

            if (coreActiveEffectHandles.ContainsKey(effect))
            {
                return;
            }

            var spec = effect.Spec;
            var effectSpec = BuildCoreEffectSpec(spec);
            var handle = core.ApplyGameplayEffectSpecToSelf(in effectSpec);
            if (handle.IsValid)
            {
                coreActiveEffectHandles[effect] = handle;
                return;
            }

            throw new InvalidOperationException(
                $"Core state rejected GameplayEffect '{effect.Spec.Def.Name}' because an effect or modifier capacity was exhausted.");
        }

        private void RemoveActiveEffectFromCore(ActiveGameplayEffect effect)
        {
            if (!IsCoreStateEnabled)
            {
                return;
            }

            if (effect == null)
            {
                return;
            }

            if (!coreActiveEffectHandles.TryGetValue(effect, out var handle))
            {
                return;
            }

            coreActiveEffectHandles.Remove(effect);
            core.RemoveActiveGameplayEffect(handle);
        }

        private GASGameplayEffectSpecData BuildCoreEffectSpec(GameplayEffectSpec spec)
        {
            var def = spec.Def;
            var modifiers = BuildCoreModifiers(spec, out int modifierCount);
            return new GASGameplayEffectSpecData(
                GetOrCreateCoreDefinitionId(def),
                spec.Source != null ? spec.Source.CoreEntity : default,
                ConvertPredictionKey(spec.Context?.PredictionKey ?? default),
                ConvertDurationPolicy(def.DurationPolicy),
                checked((ushort)spec.Level),
                1,
                simulationFrame,
                ConvertDurationToTicks(spec.Duration),
                modifiers,
                0,
                modifierCount);
        }

        private GASModifierData[] BuildCoreModifiers(GameplayEffectSpec spec, out int modifierCount)
        {
            var modifiers = spec.Def.Modifiers;
            if (modifiers == null || modifiers.Count == 0)
            {
                modifierCount = 0;
                return Array.Empty<GASModifierData>();
            }

            modifierCount = modifiers.Count;
            if (coreModifierBuffer.Length < modifierCount)
            {
                int next = Math.Max(modifierCount, coreModifierBuffer.Length == 0 ? 8 : coreModifierBuffer.Length * 2);
                coreModifierBuffer = new GASModifierData[next];
            }

            for (int i = 0; i < modifierCount; i++)
            {
                var modifier = modifiers[i];
                coreModifierBuffer[i] = new GASModifierData(
                    GetOrCreateCoreAttributeId(modifier.AttributeName),
                    ConvertModifierOp(modifier.Operation),
                    spec.GetCalculatedMagnitudeRaw(i),
                    modifier.EvaluationChannel);
            }

            return coreModifierBuffer;
        }

        public bool TryGetCoreSpecHandle(GameplayAbilitySpec spec, out GASSpecHandle handle)
        {
            if (!IsCoreStateEnabled || spec == null)
            {
                handle = default;
                return false;
            }

            return coreSpecHandles.TryGetValue(spec, out handle);
        }

        private GASDefinitionId GetOrCreateCoreDefinitionId(object definition)
        {
            return definition is GameplayAbility ability
                ? GetOrRegisterCoreAbilityDefinitionId(ability)
                : GetOrRegisterCoreEffectDefinitionId(definition as GameplayEffect);
        }

        private GASDefinitionId GetOrRegisterCoreAbilityDefinitionId(GameplayAbility ability)
        {
            if (ability == null)
            {
                return default;
            }

            var registry = RuntimeContext.DefinitionRegistry;
            return registry.TryGetAbilityDefinitionId(ability, out var id)
                ? id
                : registry.RegisterAbilityDefinition(ability, ability.Name);
        }

        private GASDefinitionId GetOrRegisterCoreEffectDefinitionId(GameplayEffect effect)
        {
            if (effect == null)
            {
                return default;
            }

            var registry = RuntimeContext.DefinitionRegistry;
            return registry.TryGetEffectDefinitionId(effect, out var id)
                ? id
                : registry.RegisterEffectDefinition(effect, effect.Name);
        }

        private GASAttributeId GetOrCreateCoreAttributeId(string attributeName)
        {
            if (string.IsNullOrEmpty(attributeName))
            {
                return default;
            }

            var registry = RuntimeContext.AttributeRegistry;
            return registry.TryGetAttributeId(attributeName, out var id)
                ? id
                : registry.RegisterAttribute(attributeName);
        }

        private static GASInstancingPolicy ConvertInstancingPolicy(EGameplayAbilityInstancingPolicy policy)
        {
            switch (policy)
            {
                case EGameplayAbilityInstancingPolicy.NonInstanced:
                    return GASInstancingPolicy.NonInstanced;
                case EGameplayAbilityInstancingPolicy.InstancedPerExecution:
                    return GASInstancingPolicy.InstancedPerExecution;
                default:
                    return GASInstancingPolicy.InstancedPerActor;
            }
        }

        private static GASEffectDurationPolicy ConvertDurationPolicy(EDurationPolicy policy)
        {
            switch (policy)
            {
                case EDurationPolicy.Instant:
                    return GASEffectDurationPolicy.Instant;
                case EDurationPolicy.HasDuration:
                    return GASEffectDurationPolicy.Duration;
                default:
                    return GASEffectDurationPolicy.Infinite;
            }
        }

        private static GASModifierOp ConvertModifierOp(EAttributeModifierOperation operation)
        {
            switch (operation)
            {
                case EAttributeModifierOperation.Multiply:
                    return GASModifierOp.Multiply;
                case EAttributeModifierOperation.Division:
                    return GASModifierOp.Division;
                case EAttributeModifierOperation.Override:
                    return GASModifierOp.Override;
                default:
                    return GASModifierOp.Add;
            }
        }

        private static GASPredictionKey ConvertPredictionKey(GASPredictionKey predictionKey)
        {
            return predictionKey;
        }

        private void CommitCorePrediction(GASPredictionKey predictionKey)
        {
            if (IsCoreStateEnabled && predictionKey.IsValid)
            {
                core.CommitPrediction(ConvertPredictionKey(predictionKey));
            }
        }

        private void RollbackCorePrediction(GASPredictionKey predictionKey)
        {
            if (IsCoreStateEnabled && predictionKey.IsValid)
            {
                core.RollbackPrediction(ConvertPredictionKey(predictionKey));
            }
        }

        public GASPredictionKey OpenPredictionWindow(GameplayAbilitySpec spec, GASPredictionKey parentPredictionKey = default)
        {
            AssertRuntimeThread();
            if (spec == null ||
                !ReferenceEquals(spec.Owner, this) ||
                !AbilitySpecs.TryGetSpecByHandle(spec.Handle, out GameplayAbilitySpec registeredSpec) ||
                !ReferenceEquals(registeredSpec, spec))
            {
                throw new InvalidOperationException("Prediction windows require a registered ability spec owned by this AbilitySystemComponent.");
            }
            if (parentPredictionKey.IsValid && !PredictionManager.HasOpenWindow(parentPredictionKey))
            {
                throw new InvalidOperationException("A dependent prediction window requires an open parent prediction key.");
            }
            if (PredictionManager.WindowCount >= Limits.MaxPredictionWindows)
            {
                throw new InvalidOperationException($"AbilitySystemComponent exceeded the prediction window limit of {Limits.MaxPredictionWindows}.");
            }

            var predictionKey = PredictionManager.CreatePredictionKey(coreEntity);
            RegisterPredictionWindow(spec, predictionKey, parentPredictionKey);
            if (GASTrace.Enabled)
            {
                GASTrace.Record(GASTraceEventType.PredictionOpened, this, spec?.GetPrimaryInstance(), decision: GASTraceDecision.Success, abilitySpecHandle: spec?.Handle ?? 0, predictionKey: predictionKey, level: spec?.Level ?? 0);
            }
            return predictionKey;
        }

        public GASPredictionScope BeginPredictionScope(GASPredictionKey predictionKey)
        {
            AssertRuntimeThread();
            if (!predictionKey.IsValid || !PredictionManager.HasOpenWindow(predictionKey))
            {
                throw new InvalidOperationException("A prediction scope requires an open prediction key owned by this AbilitySystemComponent.");
            }
            return new GASPredictionScope(this, predictionKey);
        }

        public bool HasOpenPredictionWindow(GASPredictionKey predictionKey)
        {
            AssertRuntimeThread();
            return PredictionManager.HasOpenWindow(predictionKey);
        }

        public bool TryGetPredictionWindow(GASPredictionKey predictionKey, out GASPredictionWindowData window)
        {
            AssertRuntimeThread();
            return PredictionManager.TryGetWindow(predictionKey, out window);
        }

        public GASPredictionWindowStats GetPredictionWindowStats()
        {
            AssertRuntimeThread();
            return PredictionManager.GetStats();
        }

        public bool TryGetClosedPredictionTransactionRecord(int recentIndex, out GASPredictionTransactionRecord record)
        {
            AssertRuntimeThread();
            return PredictionManager.TryGetClosedTransactionRecord(recentIndex, out record);
        }

        public int CopyClosedPredictionTransactionRecordsNonAlloc(GASPredictionTransactionRecord[] destination, int destinationIndex = 0, int maxCount = int.MaxValue)
        {
            AssertRuntimeThread();
            return PredictionManager.CopyClosedTransactionRecordsNonAlloc(destination, destinationIndex, maxCount);
        }

        public bool CommitPredictionWindow(GASPredictionKey predictionKey)
        {
            AssertRuntimeThread();
            if (ClosePredictionWindow(predictionKey, GASPredictionWindowStatus.Committed, rollback: false, closeDependents: false))
            {
                return true;
            }

            PredictionManager.RecordStaleTransaction(predictionKey, GASPredictionWindowStatus.Committed, simulationFrame);
            return false;
        }

        public bool RollbackPredictionWindow(GASPredictionKey predictionKey)
        {
            AssertRuntimeThread();
            if (ClosePredictionWindow(predictionKey, GASPredictionWindowStatus.RolledBack, rollback: true, closeDependents: true))
            {
                return true;
            }

            PredictionManager.RecordStaleTransaction(predictionKey, GASPredictionWindowStatus.RolledBack, simulationFrame);
            return false;
        }

        public void TickPredictionWindows(long currentFrame)
        {
            AssertRuntimeThread();
            if (currentFrame < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(currentFrame), currentFrame, "Prediction frame must be non-negative.");
            }
            if (!PredictionManager.TryGetTimedOutWindow(currentFrame, out var window))
            {
                return;
            }

            using (BeginReplicationMutationScope())
            {
                do
                {
                    if (GASTrace.Enabled)
                    {
                        var spec = FindSpecByHandle(window.AbilitySpecHandle);
                        GASTrace.Record(GASTraceEventType.PredictionTimedOut, this, spec?.GetPrimaryInstance(), decision: GASTraceDecision.TimedOut, reason: GASTraceReason.PredictionTimeout, abilitySpecHandle: window.AbilitySpecHandle, predictionKey: window.PredictionKey, level: spec?.Level ?? 0);
                    }

                    ClosePredictionWindow(window.PredictionKey, GASPredictionWindowStatus.TimedOut, rollback: true, closeDependents: true);
                }
                while (PredictionManager.TryGetTimedOutWindow(currentFrame, out window));
            }
        }

        private void RegisterPredictionWindow(GameplayAbilitySpec spec, GASPredictionKey predictionKey, GASPredictionKey parentPredictionKey)
        {
            if (!predictionKey.IsValid)
            {
                return;
            }

            TryGetCoreSpecHandle(spec, out var coreHandle);
            long openFrame = simulationFrame;
            long timeoutFrame = PredictionWindowTimeoutFrames > 0
                ? checked(openFrame + PredictionWindowTimeoutFrames)
                : 0L;
            PredictionManager.RegisterWindow(new GASPredictionWindowData(
                predictionKey,
                parentPredictionKey,
                coreHandle,
                spec?.Handle ?? 0,
                openFrame,
                timeoutFrame));
        }

        private int FindPredictionWindowIndex(GASPredictionKey predictionKey)
        {
            return PredictionManager.FindWindowIndex(predictionKey);
        }

        private void IncrementPredictionWindowEffectCount(GASPredictionKey predictionKey)
        {
            PredictionManager.IncrementPredictedEffectCount(predictionKey);
        }

        private void IncrementPredictionWindowAttributeSnapshotCount(GASPredictionKey predictionKey)
        {
            PredictionManager.IncrementPredictedAttributeSnapshotCount(predictionKey);
        }

        internal void IncrementPredictionWindowGameplayCueCount(GASPredictionKey predictionKey, int count = 1)
        {
            PredictionManager.IncrementPredictedGameplayCueCount(predictionKey, count);
        }

        internal void NotifyPredictedAbilityTaskCreated(GASPredictionKey predictionKey)
        {
            PredictionManager.IncrementPredictedAbilityTaskCount(predictionKey);
        }

        private bool ClosePredictionWindow(GASPredictionKey predictionKey, GASPredictionWindowStatus status, bool rollback, bool closeDependents)
        {
            if (!predictionKey.IsValid)
            {
                return false;
            }

            if (!PredictionManager.TryGetWindow(predictionKey, out _))
            {
                return false;
            }

            using (rollback ? BeginReplicationMutationScope() : default)
            {
                GASPredictionRollbackFlags rollbackFlags = GASPredictionRollbackFlags.None;
                if (closeDependents)
                {
                    if (CloseDependentPredictionWindows(predictionKey, status))
                    {
                        rollbackFlags |= GASPredictionRollbackFlags.DependentWindows;
                    }
                }

                if (!PredictionManager.TryRemoveWindow(predictionKey, out var window))
                {
                    return false;
                }

                Exception cleanupFailure = null;
                if (status == GASPredictionWindowStatus.Committed)
                {
                    CompleteCommittedPrediction(window, predictionKey, ref cleanupFailure);
                }
                else if (rollback)
                {
                    if (IsCoreStateEnabled)
                    {
                        try
                        {
                            RollbackCorePrediction(predictionKey);
                            rollbackFlags |= GASPredictionRollbackFlags.CorePrediction;
                        }
                        catch (Exception exception)
                        {
                            cleanupFailure = exception;
                        }
                    }
                    try { rollbackFlags |= RollbackPrediction(predictionKey); }
                    catch (Exception exception) { cleanupFailure ??= exception; }
                    try { rollbackFlags |= RollbackPredictedExecution(window, predictionKey); }
                    catch (Exception exception) { cleanupFailure ??= exception; }
                }

                PredictionManager.RecordTransaction(window, status, rollbackFlags, simulationFrame);
                PredictionManager.IncrementClosedWindowCount(status);

                if (GASTrace.Enabled)
                {
                    GameplayAbilitySpec closedSpec = window.AbilitySpecHandle != 0
                        ? FindSpecByHandle(window.AbilitySpecHandle)
                        : null;
                    if (status == GASPredictionWindowStatus.Committed)
                    {
                        GASTrace.Record(
                            GASTraceEventType.PredictionCommitted,
                            this,
                            closedSpec?.GetPrimaryInstance(),
                            decision: GASTraceDecision.Success,
                            abilitySpecHandle: window.AbilitySpecHandle,
                            predictionKey: predictionKey,
                            level: closedSpec?.Level ?? 0);
                    }
                    else if (status == GASPredictionWindowStatus.RolledBack)
                    {
                        GASTrace.Record(
                            GASTraceEventType.PredictionRolledBack,
                            this,
                            closedSpec?.GetPrimaryInstance(),
                            decision: GASTraceDecision.RolledBack,
                            abilitySpecHandle: window.AbilitySpecHandle,
                            predictionKey: predictionKey,
                            level: closedSpec?.Level ?? 0);
                    }
                }

                if (dirtyAttributes.Count > 0)
                {
                    try { RecalculateDirtyAttributes(); }
                    catch (Exception exception) { cleanupFailure ??= exception; }
                }

                if (cleanupFailure != null)
                {
                    RequireStateDeltaResync(GASStateDeltaRejectionReason.ApplicationFailed);
                }

                DispatchPredictionWindowClosedObservers(predictionKey, status);
                if (cleanupFailure != null)
                {
                    GASLog.Error($"Prediction window {predictionKey.Key} closed with cleanup failures: {cleanupFailure.Message}");
                }
                return true;
            }
        }

        private void CompleteCommittedPrediction(
            GASPredictionWindowData window,
            GASPredictionKey predictionKey,
            ref Exception cleanupFailure)
        {
            try { CommitCorePrediction(predictionKey); }
            catch (Exception exception) { cleanupFailure = exception; }

            try { RemovePredictedAttributeSnapshots(predictionKey, false); }
            catch (Exception exception) { cleanupFailure ??= exception; }

            try { PredictionManager.RemovePendingPredictedEffects(predictionKey); }
            catch (Exception exception) { cleanupFailure ??= exception; }

            try { RuntimeContext.CueManager.CommitPredictedCues(this, predictionKey); }
            catch (Exception exception) { cleanupFailure ??= exception; }

            GameplayAbilitySpec spec = window.AbilitySpecHandle != 0
                ? FindSpecByHandle(window.AbilitySpecHandle)
                : null;
            try { spec?.GetPrimaryInstance()?.CommitTasksForPredictionKey(predictionKey); }
            catch (Exception exception) { cleanupFailure ??= exception; }
        }

        private bool CloseDependentPredictionWindows(GASPredictionKey parentPredictionKey, GASPredictionWindowStatus status)
        {
            bool closedAny = false;
            while (PredictionManager.TryFindDependentWindow(parentPredictionKey, out var childPredictionKey))
            {
                closedAny |= ClosePredictionWindow(childPredictionKey, status, rollback: true, closeDependents: true);
            }

            return closedAny;
        }

        private void RollbackAllOpenPredictionWindows()
        {
            while (true)
            {
                IReadOnlyList<GASPredictionWindowData> windows = PredictionManager.Windows;
                if (windows.Count == 0)
                {
                    return;
                }

                GASPredictionKey keyToRollback = windows[windows.Count - 1].PredictionKey;
                ClosePredictionWindow(
                    keyToRollback,
                    GASPredictionWindowStatus.RolledBack,
                    rollback: true,
                    closeDependents: true);
            }
        }

        private bool TryFindDependentPredictionWindow(GASPredictionKey parentPredictionKey, out GASPredictionKey childPredictionKey)
        {
            return PredictionManager.TryFindDependentWindow(parentPredictionKey, out childPredictionKey);
        }

        private void RecordPredictionTransaction(
            GASPredictionWindowData window,
            GASPredictionWindowStatus status,
            GASPredictionRollbackFlags rollbackFlags,
            long closeFrame)
        {
            PredictionManager.RecordTransaction(window, status, rollbackFlags, closeFrame);
        }

        private void RecordStalePredictionTransaction(GASPredictionKey predictionKey, GASPredictionWindowStatus status)
        {
            PredictionManager.RecordStaleTransaction(predictionKey, status, simulationFrame);
        }
        private static int ConvertDurationToTicks(float duration)
        {
            if (duration <= 0f || duration == GameplayEffectConstants.INFINITE_DURATION)
            {
                return 0;
            }

            return Mathf.CeilToInt(duration * 60f);
        }

        /// <summary>
        /// Rents target data owned by this ASC's runtime context.
        /// The receiver of the data must call Release exactly once after processing.
        /// </summary>
        public T RentTargetData<T>() where T : TargetData, new()
        {
            AssertRuntimeThread();
            return RuntimeContext.Memory.AcquireTargetData<T>(Limits.MaxTargetsPerTargetData);
        }

        public bool ValidateTargetData(TargetData data, GameplayAbilitySpec spec, GASPredictionKey predictionKey, float maxRange = 0f, int maxAgeFrames = 0)
        {
            return TryValidateTargetData(data, spec, predictionKey, maxRange, maxAgeFrames, out _);
        }

        public bool TryValidateTargetData(
            TargetData data,
            GameplayAbilitySpec spec,
            GASPredictionKey predictionKey,
            float maxRange,
            int maxAgeFrames,
            out TargetDataValidationResult result)
        {
            if (float.IsNaN(maxRange) || float.IsInfinity(maxRange) || maxRange < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(maxRange), maxRange, "Target range must be finite and non-negative.");
            }

            if (maxAgeFrames < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxAgeFrames), maxAgeFrames, "Target data age must be non-negative.");
            }

            if (data == null)
            {
                result = TargetDataValidationResult.MissingData;
                return false;
            }

            if (predictionKey.IsValid && !data.PredictionKey.Equals(predictionKey))
            {
                result = TargetDataValidationResult.PredictionKeyMismatch;
                return false;
            }

            if (spec != null && data.AbilitySpecHandle != 0 && data.AbilitySpecHandle != spec.Handle)
            {
                result = TargetDataValidationResult.AbilitySpecMismatch;
                return false;
            }

            if (data.Source != null && data.Source != this)
            {
                result = TargetDataValidationResult.SourceMismatch;
                return false;
            }

            if (data.CreatedFrame > simulationFrame)
            {
                result = TargetDataValidationResult.FutureFrame;
                return false;
            }

            if (data.CreatedFrame < 0L)
            {
                result = TargetDataValidationResult.InvalidTarget;
                return false;
            }

            if (maxAgeFrames > 0 && data.CreatedFrame > 0 && simulationFrame - data.CreatedFrame > maxAgeFrames)
            {
                result = TargetDataValidationResult.TooOld;
                return false;
            }

            if (data is GameplayAbilityTargetData_ActorArray actorTargets &&
                actorTargets.ActorCount > Limits.MaxTargetsPerTargetData)
            {
                result = TargetDataValidationResult.InvalidTarget;
                return false;
            }

            if (maxRange > 0f && data is GameplayAbilityTargetData_ActorArray rangedTargets)
            {
                var sourceObject = AvatarGameObject;
                if (sourceObject == null)
                {
                    result = TargetDataValidationResult.InvalidTarget;
                    return false;
                }

                float maxRangeSq = maxRange * maxRange;
                var sourcePosition = sourceObject.transform.position;
                for (int i = 0; i < rangedTargets.ActorCount; i++)
                {
                    var target = rangedTargets.GetActor(i);
                    if (target == null)
                    {
                        result = TargetDataValidationResult.InvalidTarget;
                        return false;
                    }

                    if ((target.transform.position - sourcePosition).sqrMagnitude > maxRangeSq)
                    {
                        result = TargetDataValidationResult.TargetOutOfRange;
                        return false;
                    }
                }
            }

            if (data is GameplayAbilityTargetData_SingleTargetHit hitData &&
                (!IsFiniteVector(hitData.HitPoint) ||
                 !IsFiniteVector(hitData.HitNormal) ||
                 float.IsNaN(hitData.HitDistance) ||
                 float.IsInfinity(hitData.HitDistance) ||
                 hitData.HitDistance < 0f))
            {
                result = TargetDataValidationResult.InvalidTarget;
                return false;
            }

            result = TargetDataValidationResult.Valid;
            return true;
        }

        private static bool IsFiniteVector(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private bool ApplyActiveEffectState(
            in GASActiveEffectStateData state,
            GameplayEffect definition,
            AbilitySystemComponent source)
        {
            ActiveGameplayEffect existing = FindActiveEffectByReconciliationId(state.ReconciliationId);
            if (existing != null &&
                (!ReferenceEquals(existing.Spec.Def, definition) ||
                 !ReferenceEquals(existing.Spec.Source, source)))
            {
                return false;
            }

            PrepareStateApplyScratch(in state);
            bool created = false;
            if (existing == null)
            {
                existing = CreateReconciledActiveEffect(definition, source, in state);
                if (existing == null)
                {
                    return false;
                }

                created = true;
            }

            try
            {
                existing.SetReconciledSourceAbilitySpecHandle(state.SourceAbilitySpecHandle);
                SetActiveEffectReconciliationId(existing, state.ReconciliationId);
                ObserveActiveEffectReconciliationId(state.ReconciliationId);
                if (!TryApplyReplicatedEffectUpdateRaw(
                    existing,
                    state.Level,
                    state.StackCount,
                    state.DurationRaw,
                    state.TimeRemainingRaw,
                    state.PeriodTimeRemainingRaw,
                    stateApplySetByCallerTags,
                    stateApplySetByCallerValuesRaw,
                    state.SetByCallerTagMagnitudeCount,
                    stateApplySetByCallerNames,
                    stateApplySetByCallerNameValuesRaw,
                    state.SetByCallerNameMagnitudeCount,
                    stateApplyDynamicGrantedTags,
                    state.DynamicGrantedTagCount,
                    stateApplyDynamicAssetTags,
                    state.DynamicAssetTagCount))
                {
                    if (created)
                    {
                        TryRemoveActiveEffect(existing);
                    }

                    return false;
                }

                if (existing.IsInhibited != state.IsInhibited)
                {
                    existing.IsInhibited = state.IsInhibited;
                    existing.NotifyInhibitionChanged(state.IsInhibited);
                }
            }
            catch
            {
                if (created)
                {
                    try { TryRemoveActiveEffect(existing); }
                    catch (Exception cleanupException)
                    {
                        GASLog.Error($"Reconciled GameplayEffect rollback failed: {cleanupException.Message}");
                    }
                }

                throw;
            }

            if (dirtyAttributes.Count > 0)
            {
                RecalculateDirtyAttributes();
            }

            return true;
        }

        private static bool IsValidReplicatedTagArray(GameplayTag[] tags, int count)
        {
            if (count < 0 || count > GameplayEffect.MaxAggregateTagCount ||
                (count > 0 && (tags == null || tags.Length < count)))
            {
                return false;
            }

            for (int i = 0; i < count; i++)
            {
                if (tags[i].IsNone || !tags[i].IsValid)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsValidReplicatedName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > GameplayEffect.MaxDefinitionStringLength)
            {
                return false;
            }
            for (int i = 0; i < name.Length; i++)
            {
                if (char.IsControl(name[i])) return false;
            }
            return true;
        }

        private static bool IsCanonicalPredictionKey(GASPredictionKey predictionKey)
        {
            if (!predictionKey.IsValid)
            {
                return predictionKey.Value == 0 &&
                       !predictionKey.Owner.IsValid &&
                       predictionKey.InputSequence == 0;
            }

            return predictionKey.Value > 0 &&
                   predictionKey.InputSequence > 0 &&
                   predictionKey.Owner.IsValid;
        }

        private ActiveGameplayEffect CreateReconciledActiveEffect(
            GameplayEffect definition,
            AbilitySystemComponent source,
            in GASActiveEffectStateData state)
        {
            GameplayAbility sourceAbility = null;
            if (state.SourceAbilitySpecHandle > 0)
            {
                GameplayAbilitySpec sourceSpec = source?.FindSpecByHandle(state.SourceAbilitySpecHandle);
                sourceAbility = sourceSpec?.GetPrimaryInstance();
                if (sourceAbility == null)
                {
                    return null;
                }
            }

            var context = MakeEffectContext();
            context.PredictionKey = state.PredictionKey;
            if (context is GameplayEffectContext runtimeContext)
            {
                runtimeContext.AddInstigator(source, sourceAbility);
            }

            var spec = GameplayEffectSpec.Create(definition, source, context, state.Level);
            spec.ApplyReplicatedStateRaw(
                state.Level,
                state.DurationRaw,
                stateApplySetByCallerTags,
                stateApplySetByCallerValuesRaw,
                state.SetByCallerTagMagnitudeCount,
                stateApplySetByCallerNames,
                stateApplySetByCallerNameValuesRaw,
                state.SetByCallerNameMagnitudeCount,
                stateApplyDynamicGrantedTags,
                state.DynamicGrantedTagCount,
                stateApplyDynamicAssetTags,
                state.DynamicAssetTagCount);

            if (ActiveEffectContainer.TryGetStackingEffect(spec, out _))
            {
                spec.Discard();
                return null;
            }

            using (new ReconciliationApplyScope(this))
            {
                GameplayEffectApplicationResult application = ApplyGameplayEffectSpecToSelf(spec);
                return application.ActiveEffect;
            }
        }

        private GASPredictionRollbackFlags RollbackPrediction(GASPredictionKey predictionKey)
        {
            if (!predictionKey.IsValid)
            {
                return GASPredictionRollbackFlags.None;
            }

            var flags = GASPredictionRollbackFlags.None;
            while (PredictionManager.TryTakePendingPredictedEffect(predictionKey, out var effect))
            {
                if (TryFindActiveEffectIndex(effect, out int activeIndex))
                {
                    RemoveActiveEffectAtIndex(activeIndex);
                }

                flags |= GASPredictionRollbackFlags.ActiveEffects;
                RemoveFromStackingIndex(effect);
                OnEffectRemoved(effect, false);
            }

            if (RemovePredictedAttributeSnapshots(predictionKey, true) > 0)
            {
                flags |= GASPredictionRollbackFlags.AttributeSnapshots;
            }

            return flags;
        }

        private GASPredictionRollbackFlags RollbackPredictedExecution(GASPredictionWindowData window, GASPredictionKey predictionKey)
        {
            var flags = GASPredictionRollbackFlags.None;
            var spec = window.AbilitySpecHandle != 0 ? FindSpecByHandle(window.AbilitySpecHandle) : null;
            var ability = spec?.GetPrimaryInstance();
            Exception cleanupFailure = null;
            if (ability != null)
            {
                if (window.PredictedAbilityTaskCount > 0)
                {
                    flags |= GASPredictionRollbackFlags.AbilityTasks;
                }

                try { ability.RollbackTasksForPredictionKey(predictionKey); }
                catch (Exception exception) { cleanupFailure = exception; }
            }

            if (window.PredictedGameplayCueCount > 0)
            {
                flags |= GASPredictionRollbackFlags.GameplayCues;
            }

            try { RuntimeContext.CueManager.RollbackPredictedCues(this, predictionKey); }
            catch (Exception exception) { cleanupFailure ??= exception; }
            if (ability != null &&
                spec.IsLocallyExecuting &&
                ability.CurrentActivationInfo.PredictionKey.Equals(predictionKey))
            {
                try { ability.CancelAbility(); }
                catch (Exception exception) { cleanupFailure ??= exception; }
                flags |= GASPredictionRollbackFlags.AbilityCancelled;
            }

            if (cleanupFailure != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
            }
            return flags;
        }
        private void AddPredictedAttributeSnapshot(GASPredictionKey predictionKey, GameplayAttribute attribute)
        {
            if (HasPredictedAttributeSnapshot(predictionKey, attribute)) return;

            if (predictedAttributeSnapshots.Count >= Limits.MaxPredictedAttributeChanges)
            {
                throw new InvalidOperationException($"AbilitySystemComponent exceeded the predicted attribute snapshot limit of {Limits.MaxPredictedAttributeChanges}.");
            }

            predictedAttributeSnapshots.Add((predictionKey, attribute, attribute.BaseValueRaw));
            IncrementPredictionWindowAttributeSnapshotCount(predictionKey);
        }

        private int RemovePredictedAttributeSnapshots(GASPredictionKey predictionKey, bool restore)
        {
            if (!predictionKey.IsValid)
            {
                return 0;
            }

            int removedCount = 0;
            for (int i = predictedAttributeSnapshots.Count - 1; i >= 0; i--)
            {
                var snapshot = predictedAttributeSnapshots[i];
                if (!snapshot.key.Equals(predictionKey))
                {
                    continue;
                }

                if (restore && snapshot.attr != null)
                {
                    snapshot.attr.SetBaseValueRaw(snapshot.oldBaseValueRaw);
                    MarkAttributeDirty(snapshot.attr);
                }

                int lastIndex = predictedAttributeSnapshots.Count - 1;
                if (i != lastIndex)
                {
                    predictedAttributeSnapshots[i] = predictedAttributeSnapshots[lastIndex];
                }
                predictedAttributeSnapshots.RemoveAt(lastIndex);
                removedCount++;
            }

            return removedCount;
        }

        private readonly struct ReconciliationApplyScope : IDisposable
        {
            private readonly AbilitySystemComponent asc;

            public ReconciliationApplyScope(AbilitySystemComponent asc)
            {
                this.asc = asc;
                asc.reconciliationApplyScopeDepth++;
            }

            public void Dispose()
            {
                asc.reconciliationApplyScopeDepth--;
            }
        }

        private void ActivateAbilityInternal(GameplayAbilitySpec spec, GameplayAbilityActivationInfo activationInfo)
        {
            using (BeginReplicationMutationScope())
            {
                var ability = spec.GetPrimaryInstance();
                GASPredictionKey previousPredictionKey = currentPredictionKey;
                bool enteredActiveState = false;
                spec.ActivationCallInProgress = true;
                try
                {
                    if (ability.InstancingPolicy == EGameplayAbilityInstancingPolicy.InstancedPerExecution)
                    {
                        spec.CreateInstance();
                        ability = spec.AbilityInstance;
                    }
                    else if (ability.InstancingPolicy == EGameplayAbilityInstancingPolicy.InstancedPerActor && spec.AbilityInstance == null)
                    {
                        spec.CreateInstance();
                        ability = spec.AbilityInstance;
                    }

                    currentPredictionKey = activationInfo.PredictionKey;
                    spec.IsLocallyExecuting = true;
                    spec.IsActive = true;
                    enteredActiveState = true;
                    AddTickingAbilitySpec(spec);
                    MarkGrantedAbilitiesDirty();

                    if (ability.CancelAbilitiesWithTag != null && !ability.CancelAbilitiesWithTag.IsEmpty)
                    {
                        CancelAbilitiesWithTags(ability.CancelAbilitiesWithTag);
                    }

                    if (ability.ActivationOwnedTags != null && !ability.ActivationOwnedTags.IsEmpty)
                    {
                        looseTags.AddTags(ability.ActivationOwnedTags);
                        combinedTags.AddTags(ability.ActivationOwnedTags);
                    }

                    ability.SetCurrentActivationInfo(activationInfo);
                    ability.ActivateAbility(cachedActorInfo, spec, activationInfo);
                    if (GASTrace.Enabled)
                    {
                        GASTrace.Record(GASTraceEventType.AbilityActivated, this, ability, decision: GASTraceDecision.Success, abilitySpecHandle: spec.Handle, predictionKey: activationInfo.PredictionKey, level: spec.Level);
                    }

                    InvokeAbilityObserversSafely(abilityActivatedObservers, ability, "OnAbilityActivated");
                }
                catch
                {
                    if (activationInfo.PredictionKey.IsValid)
                    {
                        try
                        {
                            RollbackPredictionWindow(activationInfo.PredictionKey);
                        }
                        catch (Exception cleanupException)
                        {
                            GASLog.Error($"Prediction rollback failed for ability '{ability?.Name}': {cleanupException.Message}");
                        }
                    }

                    if (enteredActiveState && spec.IsActive)
                    {
                        try
                        {
                            ability.EndAbility();
                        }
                        catch (Exception cleanupException)
                        {
                            GASLog.Error($"Ability activation rollback failed for '{ability.Name}': {cleanupException.Message}");
                        }

                        if (spec.IsActive)
                        {
                            spec.IsActive = false;
                            spec.IsLocallyExecuting = false;
                            RemoveTickingAbilitySpec(spec);
                            try
                            {
                                if (ability.ActivationOwnedTags != null && !ability.ActivationOwnedTags.IsEmpty)
                                {
                                    looseTags.RemoveTags(ability.ActivationOwnedTags);
                                    combinedTags.RemoveTags(ability.ActivationOwnedTags);
                                }
                            }
                            catch (Exception cleanupException)
                            {
                                GASLog.Error($"Ability activation tag rollback failed for '{ability?.Name}': {cleanupException.Message}");
                            }
                            ability.InternalOnEndAbility();
                            MarkGrantedAbilitiesDirty();
                        }
                    }

                    throw;
                }
                finally
                {
                    spec.ActivationCallInProgress = false;
                    currentPredictionKey = previousPredictionKey;
                    if (!spec.IsActive &&
                        ability != null &&
                        ability.InstancingPolicy == EGameplayAbilityInstancingPolicy.InstancedPerExecution &&
                        spec.AbilityInstance != null)
                    {
                        try
                        {
                            spec.ClearInstance();
                        }
                        catch (Exception cleanupException)
                        {
                            GASLog.Error($"Per-execution ability instance cleanup failed for '{ability.Name}': {cleanupException.Message}");
                        }
                    }
                }
            }
        }

        private void InvokeAbilityObserversSafely(
            GASCallbackList<Action<GameplayAbility>> observers,
            GameplayAbility ability,
            string observerName)
        {
            EnterRuntimeCallbackDispatch();
            bool callbackListDispatchStarted = false;
            try
            {
                int count = observers.BeginDispatch();
                callbackListDispatchStarted = true;
                for (int i = 0; i < count; i++)
                {
                    Action<GameplayAbility> observer = observers.GetCallback(i);
                    if (observer == null)
                    {
                        continue;
                    }

                    try
                    {
                        observer.Invoke(ability);
                    }
                    catch (Exception exception)
                    {
                        GASLog.Error($"{observerName} observer failed after the authoritative state was committed: {exception.Message}");
                    }
                }
            }
            finally
            {
                try
                {
                    if (callbackListDispatchStarted)
                    {
                        observers.EndDispatch();
                    }
                }
                finally
                {
                    ExitRuntimeCallbackDispatch();
                }
            }
        }

        private void DispatchPredictionWindowClosedObservers(
            GASPredictionKey predictionKey,
            GASPredictionWindowStatus status)
        {
            EnterRuntimeCallbackDispatch();
            bool callbackListDispatchStarted = false;
            try
            {
                int count = predictionWindowClosedObservers.BeginDispatch();
                callbackListDispatchStarted = true;
                for (int i = 0; i < count; i++)
                {
                    Action<GASPredictionKey, GASPredictionWindowStatus> observer =
                        predictionWindowClosedObservers.GetCallback(i);
                    if (observer == null)
                    {
                        continue;
                    }

                    try
                    {
                        observer.Invoke(predictionKey, status);
                    }
                    catch (Exception exception)
                    {
                        GASLog.Error($"OnPredictionWindowClosed observer failed after the local prediction transaction closed: {exception.Message}");
                    }
                }
            }
            finally
            {
                try
                {
                    if (callbackListDispatchStarted)
                    {
                        predictionWindowClosedObservers.EndDispatch();
                    }
                }
                finally
                {
                    ExitRuntimeCallbackDispatch();
                }
            }
        }

        private void DispatchStateDeltaResyncObservers(GASStateDeltaRejectionReason reason)
        {
            EnterRuntimeCallbackDispatch();
            bool callbackListDispatchStarted = false;
            try
            {
                int count = stateDeltaResyncObservers.BeginDispatch();
                callbackListDispatchStarted = true;
                for (int i = 0; i < count; i++)
                {
                    Action<GASStateDeltaRejectionReason> observer = stateDeltaResyncObservers.GetCallback(i);
                    if (observer == null)
                    {
                        continue;
                    }

                    try
                    {
                        observer.Invoke(reason);
                    }
                    catch (Exception exception)
                    {
                        GASLog.Error($"OnStateDeltaResyncRequired observer failed: {exception.Message}");
                    }
                }
            }
            finally
            {
                try
                {
                    if (callbackListDispatchStarted)
                    {
                        stateDeltaResyncObservers.EndDispatch();
                    }
                }
                finally
                {
                    ExitRuntimeCallbackDispatch();
                }
            }
        }

        internal void NotifyAbilityCommitted(GameplayAbility ability)
        {
            if (GASTrace.Enabled)
            {
                GASTrace.Record(GASTraceEventType.AbilityCommitted, this, ability, decision: GASTraceDecision.Success, abilitySpecHandle: ability?.Spec?.Handle ?? 0, predictionKey: ability?.CurrentActivationInfo.PredictionKey ?? default, level: ability?.Spec?.Level ?? 0);
            }
            InvokeAbilityObserversSafely(abilityCommittedObservers, ability, "OnAbilityCommitted");
        }

        internal void OnAbilityEnded(GameplayAbility ability)
        {
            GameplayAbilitySpec endingSpec = ability?.Spec;
            if (endingSpec == null)
            {
                return;
            }

            if (endingSpec.EndCallInProgress)
            {
                throw new InvalidOperationException("Ability end processing cannot re-enter for the same spec lease.");
            }

            ulong endingLeaseGeneration = endingSpec.LeaseGeneration;
            endingSpec.EndCallInProgress = true;
            try
            {
                OnAbilityEndedWithinLease(ability, endingSpec, endingLeaseGeneration);
            }
            finally
            {
                if (endingSpec.LeaseGeneration == endingLeaseGeneration)
                {
                    endingSpec.EndCallInProgress = false;
                }
            }
        }

        private void OnAbilityEndedWithinLease(
            GameplayAbility ability,
            GameplayAbilitySpec endingSpec,
            ulong endingLeaseGeneration)
        {
            if (disposing)
            {
                if (abilityEndMutationBypassDepth == 0)
                {
                    throw new ObjectDisposedException(nameof(AbilitySystemComponent));
                }

                endingSpec.IsActive = false;
                endingSpec.IsLocallyExecuting = false;
                RemoveTickingAbilitySpec(endingSpec);
                ability.InternalOnEndAbility();
                return;
            }

            Exception cleanupFailure = null;
            bool wasLocallyExecuting = endingSpec.IsLocallyExecuting;
            endingSpec.IsLocallyExecuting = false;
            if (endingSpec.IsActive)
            {
                endingSpec.IsActive = false;
                MarkGrantedAbilitiesDirty();
                RemoveTickingAbilitySpec(endingSpec);

                try
                {
                    if (wasLocallyExecuting &&
                        ability.ActivationOwnedTags != null &&
                        !ability.ActivationOwnedTags.IsEmpty)
                    {
                        looseTags.RemoveTags(ability.ActivationOwnedTags);
                        combinedTags.RemoveTags(ability.ActivationOwnedTags);
                    }
                }
                catch (Exception exception)
                {
                    cleanupFailure = exception;
                }

                try
                {
                    if (wasLocallyExecuting &&
                        ActiveEffectContainer.TryGetAbilityAppliedEffects(ability, out var appliedEffects))
                    {
                        abilityAppliedEffectRemovalScratch.Clear();
                        for (int ei = 0; ei < appliedEffects.Count; ei++)
                        {
                            if (!appliedEffects[ei].IsExpired)
                            {
                                abilityAppliedEffectRemovalScratch.Add(appliedEffects[ei]);
                            }
                        }

                        ActiveEffectContainer.UntrackAbilityAppliedEffectsForAbility(ability, ReturnAbilityAppliedEffectList);

                        if (abilityAppliedEffectRemovalScratch.Count > 0)
                        {
                            List<ActiveGameplayEffect> removed = RentAbilityAppliedEffectList();
                            try
                            {
                                // Small contiguous scratch is usually faster than hashing for per-ability applied effects.
                                for (int i = activeEffects.Count - 1; i >= 0; i--)
                                {
                                    if (ContainsReference(abilityAppliedEffectRemovalScratch, activeEffects[i]))
                                    {
                                        removed.Add(activeEffects[i]);
                                        RemoveActiveEffectAtIndex(i);
                                    }
                                }
                                for (int i = 0; i < removed.Count; i++)
                                {
                                    try
                                    {
                                        OnEffectRemoved(removed[i], true);
                                    }
                                    catch (Exception exception)
                                    {
                                        cleanupFailure ??= exception;
                                    }
                                }
                            }
                            finally
                            {
                                ReturnAbilityAppliedEffectList(removed);
                            }
                        }
                    }
                }
                catch (Exception exception)
                {
                    CaptureCleanupFailure(ref cleanupFailure, exception);
                }
                finally
                {
                    abilityAppliedEffectRemovalScratch.Clear();
                }
            }

            ability.InternalOnEndAbility();
            InvokeAbilityObserversSafely(abilityEndedObservers, ability, "OnAbilityEndedEvent");
            if (endingSpec.LeaseGeneration != endingLeaseGeneration ||
                !ReferenceEquals(endingSpec.Owner, this) ||
                !ReferenceEquals(ability.Spec, endingSpec))
            {
                throw new InvalidOperationException("Ability end callback invalidated the ending spec lease.");
            }
            if (GASTrace.Enabled)
            {
                GASTrace.Record(GASTraceEventType.AbilityEnded, this, ability, decision: GASTraceDecision.Success, abilitySpecHandle: endingSpec.Handle, level: endingSpec.Level);
            }

            if (ability.InstancingPolicy == EGameplayAbilityInstancingPolicy.InstancedPerExecution &&
                !endingSpec.ActivationCallInProgress)
            {
                try
                {
                    endingSpec.ClearInstance();
                }
                catch (Exception exception)
                {
                    CaptureCleanupFailure(ref cleanupFailure, exception);
                }
            }

            if (cleanupFailure != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
            }
        }

        // --- Gameplay Effect Application ---
        public GameplayEffectContext MakeEffectContext()
        {
            AssertRuntimeThread();
            return EffectContextFactory.Create(this);
        }

        public GameplayEffectApplicationResult ApplyGameplayEffectSpecToSelf(GameplayEffectSpec spec)
        {
            AssertRuntimeThread();
            if (effectMutationTransactionDepth != 0 || activeEffectIterationDepth != 0)
            {
                if (spec != null && spec.TryTransferToAbilitySystem())
                {
                    spec.ReleaseRuntimeLease();
                }
                return new GameplayEffectApplicationResult(GameplayEffectApplicationResultCode.ReentrantMutationRejected);
            }

            effectMutationTransactionDepth++;
            try
            {
                return ApplyGameplayEffectSpecWithinTransaction(spec);
            }
            finally
            {
                effectMutationTransactionDepth--;
                FlushDeferredTriggerActivations();
            }
        }

        private GameplayEffectApplicationResult ApplyGameplayEffectSpecWithinTransaction(GameplayEffectSpec spec)
        {
            if (spec == null)
            {
                return new GameplayEffectApplicationResult(GameplayEffectApplicationResultCode.InvalidSpec);
            }

            if (!spec.TryTransferToAbilitySystem())
            {
                return new GameplayEffectApplicationResult(GameplayEffectApplicationResultCode.InvalidSpec);
            }

            try
            {
                return ApplyOwnedGameplayEffectSpecToSelf(spec);
            }
            catch (Exception exception)
            {
                GASLog.Error($"GameplayEffect application failed before ownership closure: {exception.Message}");
                return new GameplayEffectApplicationResult(GameplayEffectApplicationResultCode.ExecutionFailed);
            }
            finally
            {
                if (spec.IsOwnedByAbilitySystem)
                {
                    try
                    {
                        spec.ReleaseRuntimeLease();
                    }
                    catch (Exception exception)
                    {
                        GASLog.Error($"GameplayEffectSpec reset failed and the lease was quarantined: {exception.Message}");
                    }
                }
            }
        }

        private GameplayEffectApplicationResult ApplyOwnedGameplayEffectSpecToSelf(GameplayEffectSpec spec)
        {

            if (stateDeltaResyncRequired && reconciliationApplyScopeDepth == 0)
            {
                spec.ReleaseRuntimeLease();
                return new GameplayEffectApplicationResult(GameplayEffectApplicationResultCode.StateResyncRequired);
            }

            if (spec.Def == null)
            {
                spec.ReleaseRuntimeLease();
                return new GameplayEffectApplicationResult(GameplayEffectApplicationResultCode.InvalidDefinition);
            }

            if (spec.Source != null && !ReferenceEquals(spec.Source.RuntimeContext, RuntimeContext))
            {
                spec.ReleaseRuntimeLease();
                return new GameplayEffectApplicationResult(GameplayEffectApplicationResultCode.RuntimeContextMismatch);
            }

            if (spec.Context?.MemoryOwner != null &&
                !ReferenceEquals(spec.Context.MemoryOwner, RuntimeContext.Memory))
            {
                spec.ReleaseRuntimeLease();
                return new GameplayEffectApplicationResult(GameplayEffectApplicationResultCode.RuntimeContextMismatch);
            }

            spec.SetTarget(this);
            if (GASTrace.Enabled)
            {
                GASTrace.Record(GASTraceEventType.EffectApplyAttempt, this, effect: spec.Def, source: spec.Source, level: spec.Level);
            }

            // If we are in a prediction scope, tag the spec's context
            if (currentPredictionKey.IsValid)
            {
                spec.SetPredictionKeyFromOwner(currentPredictionKey);
            }

            GameplayEffectApplicationResultCode validationCode = ValidateGameplayEffectSpec(spec);
            if (validationCode != GameplayEffectApplicationResultCode.Applied)
            {
                if (GASTrace.Enabled)
                {
                    GASTrace.Record(GASTraceEventType.EffectApplyBlocked, this, effect: spec.Def, decision: GASTraceDecision.Blocked, reason: GASTraceReason.ApplicationBlockedTags, source: spec.Source, level: spec.Level);
                }
                spec.ReleaseRuntimeLease();
                return new GameplayEffectApplicationResult(validationCode);
            }

            GameplayEffectApplicationResultCode predictionCode = ValidatePredictedEffectBudget(spec);
            if (predictionCode != GameplayEffectApplicationResultCode.Applied)
            {
                spec.ReleaseRuntimeLease();
                return new GameplayEffectApplicationResult(predictionCode);
            }

            bool joinsExistingStack =
                spec.Def.DurationPolicy != EDurationPolicy.Instant &&
                spec.Def.Stacking.Type != EGameplayEffectStackingType.None &&
                ActiveEffectContainer.TryGetStackingEffect(spec, out _);
            if (spec.Def.DurationPolicy != EDurationPolicy.Instant &&
                !joinsExistingStack &&
                activeEffects.Count >= Limits.MaxActiveEffects)
            {
                spec.ReleaseRuntimeLease();
                return new GameplayEffectApplicationResult(GameplayEffectApplicationResultCode.ActiveEffectLimitReached);
            }

            if (reconciliationApplyScopeDepth == 0 &&
                !joinsExistingStack &&
                spec.Def.DurationPolicy != EDurationPolicy.Instant &&
                spec.Def.GrantedAbilities.Count > Limits.MaxGrantedAbilities - activatableAbilities.Count)
            {
                spec.ReleaseRuntimeLease();
                return new GameplayEffectApplicationResult(GameplayEffectApplicationResultCode.GrantedAbilityLimitReached);
            }

            using (BeginReplicationMutationScope())
            {
                if (spec.Def.DurationPolicy == EDurationPolicy.Instant)
                {
                    try
                    {
                        ExecuteInstantEffect(spec);
                    }
                    catch (Exception exception)
                    {
                        GASLog.Error($"GameplayEffect execution failed for '{spec.Def.Name}': {exception.Message}");
                        spec.ReleaseRuntimeLease();
                        return new GameplayEffectApplicationResult(GameplayEffectApplicationResultCode.ExecutionFailed);
                    }

                    RemoveEffectsWithTags(spec.Def.RemoveGameplayEffectsWithTags);
                    TryDispatchGameplayCues(spec, EGameplayCueEvent.Executed);

                    if (GASTrace.Enabled)
                    {
                        GASTrace.Record(GASTraceEventType.EffectExecuted, this, effect: spec.Def, decision: GASTraceDecision.Success, source: spec.Source, predictionKey: spec.Context.PredictionKey, level: spec.Level);
                    }
                    spec.ReleaseRuntimeLease();
                    return new GameplayEffectApplicationResult(GameplayEffectApplicationResultCode.Executed);
                }

                bool stacked;
                try
                {
                    stacked = HandleStacking(spec);
                }
                catch (Exception exception)
                {
                    GASLog.Error($"GameplayEffect stacking failed for '{spec.Def.Name}': {exception.Message}");
                    spec.ReleaseRuntimeLease();
                    return new GameplayEffectApplicationResult(GameplayEffectApplicationResultCode.DurationCommitFailed);
                }

                if (stacked)
                {
                    RemoveEffectsWithTags(spec.Def.RemoveGameplayEffectsWithTags);
                    spec.ReleaseRuntimeLease();
                    return new GameplayEffectApplicationResult(GameplayEffectApplicationResultCode.Stacked);
                }

                bool isLocallyPredictedEffect =
                    currentPredictionKey.IsValid &&
                    !RuntimeContext.HasAuthority &&
                    PredictionManager.HasOpenWindow(currentPredictionKey);
                ActiveGameplayEffect newActiveEffect = null;
                try
                {
                    newActiveEffect = ActiveGameplayEffect.Create(spec);
                    if (!ActiveEffectContainer.AddEffect(newActiveEffect))
                    {
                        throw new InvalidOperationException("ActiveGameplayEffect container rejected a unique effect lease.");
                    }
                    if (!isLocallyPredictedEffect)
                    {
                        SetActiveEffectReconciliationId(newActiveEffect, AllocateActiveEffectReconciliationId());
                    }
                    RegisterActiveEffectInCore(newActiveEffect);
                    OnEffectApplied(newActiveEffect);
                }
                catch (Exception exception)
                {
                    if (newActiveEffect != null)
                    {
                        RollbackUncommittedActiveEffect(newActiveEffect);
                    }
                    else
                    {
                        spec.ReleaseRuntimeLease();
                    }

                    GASLog.Error($"GameplayEffect duration commit failed for '{spec.Def?.Name}': {exception.Message}");
                    return new GameplayEffectApplicationResult(GameplayEffectApplicationResultCode.DurationCommitFailed);
                }

                RemoveEffectsWithTags(spec.Def.RemoveGameplayEffectsWithTags, newActiveEffect);

                if (isLocallyPredictedEffect)
                {
                    PredictionManager.AddPendingPredictedEffect(newActiveEffect);
                    IncrementPredictionWindowEffectCount(currentPredictionKey);
                }

                // Track by the current ability lease, then release the context's borrowed reference so a
                // A long-lived effect never retains an ability instance after that lease is released.
                GameplayAbility sourceAbility = spec.Context?.ConsumeAbilityInstance(spec);
                if (spec.Def.RemoveGameplayEffectsAfterAbilityEnds && sourceAbility != null)
                {
                    ActiveEffectContainer.TrackAbilityAppliedEffect(sourceAbility, newActiveEffect, RentAbilityAppliedEffectList);
                }

                if (GASTrace.Enabled)
                {
                    GASTrace.Record(GASTraceEventType.EffectApplied, this, effect: spec.Def, decision: GASTraceDecision.Success, source: spec.Source, predictionKey: spec.Context.PredictionKey, level: spec.Level, stackCount: newActiveEffect.StackCount, reconciliationId: newActiveEffect.ReconciliationId);
                }
                MarkActiveEffectsDirty();

                if (spec.Def.Period <= 0 || !spec.Def.OngoingTagRequirements.IsEmpty)
                {
                    MarkAttributesDirtyFromEffect(newActiveEffect);
                }

                return new GameplayEffectApplicationResult(GameplayEffectApplicationResultCode.Applied, newActiveEffect);
            }
        }

        private void RollbackUncommittedActiveEffect(ActiveGameplayEffect effect)
        {
            if (effect == null) return;
            Exception cleanupFailure = null;
            try
            {
                try
                {
                    if (TryFindActiveEffectIndex(effect, out int index))
                    {
                        RemoveActiveEffectAtIndex(index);
                    }
                    RemoveFromStackingIndex(effect);
                }
                catch (Exception exception) { CaptureCleanupFailure(ref cleanupFailure, exception); }

                try { RemoveActiveEffectFromCore(effect); }
                catch (Exception exception) { CaptureCleanupFailure(ref cleanupFailure, exception); }
                try { ActiveEffectContainer.UntrackAppliedEffectFromAbilities(effect, ReturnAbilityAppliedEffectList); }
                catch (Exception exception) { CaptureCleanupFailure(ref cleanupFailure, exception); }

                if (AbilitySpecs.TryGetGrantedSpecs(effect, out var grantedSpecs))
                {
                    while (grantedSpecs.Count > 0)
                    {
                        GameplayAbilitySpec grantedSpec = grantedSpecs[grantedSpecs.Count - 1];
                        try
                        {
                            ClearAbilityInternal(grantedSpec);
                        }
                        catch (Exception exception)
                        {
                            CaptureCleanupFailure(ref cleanupFailure, exception);
                            GASLog.Error($"Granted ability rollback failed: {exception.Message}");
                            if (grantedSpecs.Count > 0 &&
                                ReferenceEquals(grantedSpecs[grantedSpecs.Count - 1], grantedSpec))
                            {
                                grantedSpecs.RemoveAt(grantedSpecs.Count - 1);
                            }
                        }
                    }
                }

                TryRemoveTagsForCleanup(fromEffectsTags, effect.Spec.Def.GrantedTags, ref cleanupFailure);
                TryRemoveTagsForCleanup(combinedTags, effect.Spec.Def.GrantedTags, ref cleanupFailure);
                TryRemoveTagsForCleanup(fromEffectsTags, effect.Spec.DynamicGrantedTags, ref cleanupFailure);
                TryRemoveTagsForCleanup(combinedTags, effect.Spec.DynamicGrantedTags, ref cleanupFailure);

                try { UpdateGrantedTagIndex_Removed(effect); }
                catch (Exception exception) { CaptureCleanupFailure(ref cleanupFailure, exception); }

                try
                {
                    IReadOnlyList<ModifierInfo> modifiers = effect.Spec.Def.Modifiers;
                    for (int i = 0; i < modifiers.Count; i++)
                    {
                        GameplayAttribute attribute = i < effect.Spec.TargetAttributes.Length
                            ? effect.Spec.TargetAttributes[i]
                            : null;
                        attribute ??= GetAttribute(modifiers[i].AttributeName);
                        if (attribute != null && !HasEarlierModifierForAttribute(effect, attribute, i))
                        {
                            RemoveAffectingEffect(attribute, effect);
                        }
                    }
                }
                catch (Exception exception) { CaptureCleanupFailure(ref cleanupFailure, exception); }

            }
            finally
            {
                try { effect.ReleaseRuntimeLease(); }
                catch (Exception exception) { CaptureCleanupFailure(ref cleanupFailure, exception); }
                try { MarkActiveEffectsDirty(); }
                catch (Exception exception) { CaptureCleanupFailure(ref cleanupFailure, exception); }
            }

            if (cleanupFailure != null)
            {
                GASLog.Error($"GameplayEffect rollback completed with cleanup failures: {cleanupFailure.Message}");
            }
        }

        public GameplayEffectApplicationResultCode CanApplyGameplayEffectSpec(GameplayEffectSpec spec)
        {
            AssertRuntimeThread();
            if (spec == null) return GameplayEffectApplicationResultCode.InvalidSpec;
            if (spec.IsExternalEvaluationInProgress) return GameplayEffectApplicationResultCode.InvalidSpec;
            if (spec.Def == null) return GameplayEffectApplicationResultCode.InvalidDefinition;
            if (spec.Source != null && !ReferenceEquals(spec.Source.RuntimeContext, RuntimeContext))
            {
                return GameplayEffectApplicationResultCode.RuntimeContextMismatch;
            }

            spec.SetTarget(this);
            GameplayEffectApplicationResultCode code = ValidateGameplayEffectSpec(spec);
            if (code != GameplayEffectApplicationResultCode.Applied)
            {
                return code;
            }

            code = ValidatePredictedEffectBudget(spec);
            if (code != GameplayEffectApplicationResultCode.Applied)
            {
                return code;
            }

            bool joinsExistingStack =
                spec.Def.DurationPolicy != EDurationPolicy.Instant &&
                spec.Def.Stacking.Type != EGameplayEffectStackingType.None &&
                ActiveEffectContainer.TryGetStackingEffect(spec, out _);
            return spec.Def.DurationPolicy != EDurationPolicy.Instant &&
                   !joinsExistingStack &&
                   activeEffects.Count >= Limits.MaxActiveEffects
                ? GameplayEffectApplicationResultCode.ActiveEffectLimitReached
                : GameplayEffectApplicationResultCode.Applied;
        }

        public void RemoveActiveEffectsWithGrantedTags(GameplayTagContainer tags)
        {
            AssertRuntimeThread();
            if (effectMutationTransactionDepth != 0 || activeEffectIterationDepth != 0)
            {
                throw new InvalidOperationException("Active-effect removal cannot re-enter an effect mutation transaction.");
            }
            RemoveActiveEffectsWithGrantedTags(tags, null);
        }

        private void RemoveActiveEffectsWithGrantedTags(
            GameplayTagContainer tags,
            ActiveGameplayEffect excludedEffect)
        {
            if (tags == null || tags.IsEmpty) return;

            List<ActiveGameplayEffect> removedEffects = RentAbilityAppliedEffectList();
            try
            {
                for (int i = activeEffects.Count - 1; i >= 0; i--)
                {
                    var effect = activeEffects[i];
                    if (ReferenceEquals(effect, excludedEffect)) continue;
                    bool shouldRemove = effect.Spec.Def.GrantedTags.HasAny(tags) || effect.Spec.Def.AssetTags.HasAny(tags);
                    if (shouldRemove)
                    {
                        removedEffects.Add(effect);
                    }
                }

                if (removedEffects.Count == 0)
                {
                    return;
                }

                using (BeginReplicationMutationScope())
                {
                    for (int i = 0; i < removedEffects.Count; i++)
                    {
                        ActiveGameplayEffect effect = removedEffects[i];
                        if (TryFindActiveEffectIndex(effect, out int index))
                        {
                            RemoveActiveEffectAtIndex(index);
                            RemoveFromStackingIndex(effect);
                        }
                    }

                    for (int i = 0; i < removedEffects.Count; i++)
                    {
                        OnEffectRemoved(removedEffects[i], true);
                    }
                }
            }
            finally
            {
                ReturnAbilityAppliedEffectList(removedEffects);
            }
        }

        private GameplayEffectApplicationResultCode ValidateGameplayEffectSpec(GameplayEffectSpec spec)
        {
            // Check immunity - block effects whose tags match any immunity tag
            if (!immunityTags.IsEmpty)
            {
                if (spec.Def.AssetTagsSnapshot.HasAny(immunityTags) || spec.Def.GrantedTagsSnapshot.HasAny(immunityTags))
                {
                    GASLog.Debug(sb => sb.Append("Apply GameplayEffect '").Append(spec.Def.Name).Append("' blocked: target has immunity to effect's tags."));
                    return GameplayEffectApplicationResultCode.BlockedByImmunity;
                }
                // Also check dynamic tags on the spec instance
                if (!spec.DynamicAssetTags.IsEmpty && spec.DynamicAssetTags.HasAny(immunityTags))
                {
                    GASLog.Debug(sb => sb.Append("Apply GameplayEffect '").Append(spec.Def.Name).Append("' blocked: target has immunity to effect's dynamic asset tags."));
                    return GameplayEffectApplicationResultCode.BlockedByImmunity;
                }
                if (!spec.DynamicGrantedTags.IsEmpty && spec.DynamicGrantedTags.HasAny(immunityTags))
                {
                    GASLog.Debug(sb => sb.Append("Apply GameplayEffect '").Append(spec.Def.Name).Append("' blocked: target has immunity to effect's dynamic granted tags."));
                    return GameplayEffectApplicationResultCode.BlockedByImmunity;
                }
            }

            if (!HasAllMatchingGameplayTags(spec.Def.ApplicationRequiredTagsSnapshot))
            {
                GASLog.Debug(sb => sb.Append("Apply GameplayEffect '").Append(spec.Def.Name).Append("' failed: does not meet application tag requirements (Required)."));
                return GameplayEffectApplicationResultCode.MissingRequiredTags;
            }
            if (HasAnyMatchingGameplayTags(spec.Def.ApplicationForbiddenTagsSnapshot))
            {
                GASLog.Debug(sb => sb.Append("Apply GameplayEffect '").Append(spec.Def.Name).Append("' failed: does not meet application tag requirements (Ignored)."));
                return GameplayEffectApplicationResultCode.BlockedByForbiddenTags;
            }

            //  Custom Application Requirements (UE5: UGameplayEffectCustomApplicationRequirement)
            var requirements = spec.Def.CustomApplicationRequirements;
            for (int i = 0; i < requirements.Count; i++)
            {
                bool canApply;
                try
                {
                    spec.BeginExternalEvaluation();
                    try
                    {
                        canApply = requirements[i].CanApplyGameplayEffect(spec, this);
                    }
                    finally
                    {
                        spec.EndExternalEvaluation();
                    }
                }
                catch (Exception exception)
                {
                    GASLog.Error($"Custom GameplayEffect application requirement failed: {exception.Message}");
                    return GameplayEffectApplicationResultCode.BlockedByCustomRequirement;
                }

                if (!canApply)
                {
                    GASLog.Debug(sb => sb.Append("Apply GameplayEffect '").Append(spec.Def.Name).Append("' blocked by custom application requirement."));
                    return GameplayEffectApplicationResultCode.BlockedByCustomRequirement;
                }
            }

            return GameplayEffectApplicationResultCode.Applied;
        }

        private GameplayEffectApplicationResultCode ValidatePredictedEffectBudget(GameplayEffectSpec spec)
        {
            if (spec?.Def == null)
            {
                return GameplayEffectApplicationResultCode.Applied;
            }

            if (spec.Def.DurationPolicy == EDurationPolicy.Instant &&
                HasConflictingPredictedAttributeSnapshot(spec, currentPredictionKey))
            {
                return GameplayEffectApplicationResultCode.PredictionUnsupported;
            }

            if (!currentPredictionKey.IsValid)
            {
                return GameplayEffectApplicationResultCode.Applied;
            }

            if (spec.Def.Execution != null)
            {
                return GameplayEffectApplicationResultCode.PredictionUnsupported;
            }

            if (spec.Def.DurationPolicy != EDurationPolicy.Instant &&
                spec.Def.Stacking.Type != EGameplayEffectStackingType.None &&
                ActiveEffectContainer.TryGetStackingEffect(spec, out _))
            {
                return GameplayEffectApplicationResultCode.PredictionUnsupported;
            }

            int additionalSnapshots = 0;
            IReadOnlyList<ModifierInfo> modifiers = spec.Def.Modifiers;
            for (int i = 0; i < modifiers.Count; i++)
            {
                GameplayAttribute attribute = i < spec.TargetAttributes.Length
                    ? spec.TargetAttributes[i]
                    : null;
                attribute ??= GetAttribute(modifiers[i].AttributeName);
                if (attribute == null || HasPredictedAttributeSnapshot(currentPredictionKey, attribute))
                {
                    continue;
                }

                if (HasPredictedAttributeSnapshotFromAnotherWindow(currentPredictionKey, attribute))
                {
                    return GameplayEffectApplicationResultCode.PredictionUnsupported;
                }

                bool alreadyCounted = false;
                for (int j = 0; j < i; j++)
                {
                    GameplayAttribute previous = j < spec.TargetAttributes.Length
                        ? spec.TargetAttributes[j]
                        : null;
                    previous ??= GetAttribute(modifiers[j].AttributeName);
                    if (ReferenceEquals(previous, attribute))
                    {
                        alreadyCounted = true;
                        break;
                    }
                }

                if (!alreadyCounted)
                {
                    additionalSnapshots++;
                }
            }

            return additionalSnapshots > Limits.MaxPredictedAttributeChanges - predictedAttributeSnapshots.Count
                ? GameplayEffectApplicationResultCode.PredictionLimitReached
                : GameplayEffectApplicationResultCode.Applied;
        }

        private bool HasPredictedAttributeSnapshot(GASPredictionKey predictionKey, GameplayAttribute attribute)
        {
            for (int i = predictedAttributeSnapshots.Count - 1; i >= 0; i--)
            {
                var snapshot = predictedAttributeSnapshots[i];
                if (snapshot.key.Equals(predictionKey) && ReferenceEquals(snapshot.attr, attribute))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasPredictedAttributeSnapshotForAttribute(GameplayAttribute attribute)
        {
            for (int i = predictedAttributeSnapshots.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(predictedAttributeSnapshots[i].attr, attribute))
                {
                    return true;
                }
            }
            return false;
        }

        private bool HasConflictingPredictedAttributeSnapshot(
            GameplayEffectSpec spec,
            GASPredictionKey permittedPredictionKey)
        {
            IReadOnlyList<ModifierInfo> modifiers = spec.Def.Modifiers;
            for (int i = 0; i < modifiers.Count; i++)
            {
                GameplayAttribute attribute = i < spec.TargetAttributes.Length
                    ? spec.TargetAttributes[i]
                    : null;
                attribute ??= GetAttribute(modifiers[i].AttributeName);
                if (attribute == null)
                {
                    continue;
                }

                for (int snapshotIndex = predictedAttributeSnapshots.Count - 1; snapshotIndex >= 0; snapshotIndex--)
                {
                    var snapshot = predictedAttributeSnapshots[snapshotIndex];
                    if (ReferenceEquals(snapshot.attr, attribute) &&
                        (!permittedPredictionKey.IsValid || !snapshot.key.Equals(permittedPredictionKey)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool HasPredictedAttributeSnapshotFromAnotherWindow(
            GASPredictionKey predictionKey,
            GameplayAttribute attribute)
        {
            for (int i = predictedAttributeSnapshots.Count - 1; i >= 0; i--)
            {
                var snapshot = predictedAttributeSnapshots[i];
                if (!snapshot.key.Equals(predictionKey) && ReferenceEquals(snapshot.attr, attribute))
                {
                    return true;
                }
            }

            return false;
        }

        internal void ExecuteInstantEffect(GameplayEffectSpec spec)
        {
            if (instantEffectExecutionInProgress)
            {
                throw new InvalidOperationException(
                    "GameplayEffect execution cannot re-enter the same AbilitySystemComponent.");
            }

            instantExecutionOutputScratch ??= new List<ModifierInfo>(Math.Min(Limits.MaxModifiersPerEffect, 8));
            instantRollbackSnapshotScratch ??= new List<InstantAttributeSnapshot>(16);
            List<ModifierInfo> executionOutput = instantExecutionOutputScratch;
            List<InstantAttributeSnapshot> rollbackSnapshots = instantRollbackSnapshotScratch;
            executionOutput.Clear();
            rollbackSnapshots.Clear();
            instantEffectExecutionInProgress = true;
            int predictionSnapshotStart = predictedAttributeSnapshots.Count;
            try
            {
                if (spec.Def.Execution != null)
                {
                    var output = new GameplayEffectExecutionOutput(executionOutput, Limits.MaxModifiersPerEffect);
                    spec.Def.Execution.Execute(spec, output);
                }

                for (int i = 0; i < executionOutput.Count; i++)
                {
                    ModifierInfo modifier = executionOutput[i] ??
                        throw new InvalidOperationException("GameplayEffect execution produced a null modifier.");
                    CaptureInstantAttributeSnapshot(GetAttribute(modifier.AttributeName), rollbackSnapshots);
                }

                IReadOnlyList<ModifierInfo> modifiers = spec.Def.Modifiers;
                for (int i = 0; i < modifiers.Count; i++)
                {
                    GameplayAttribute attribute = i < spec.TargetAttributes.Length ? spec.TargetAttributes[i] : null;
                    attribute ??= GetAttribute(modifiers[i].AttributeName);
                    CaptureInstantAttributeSnapshot(attribute, rollbackSnapshots);
                }

                try
                {
                    for (int i = 0; i < executionOutput.Count; i++)
                    {
                        ModifierInfo modifier = executionOutput[i];
                        GameplayAttribute attribute = GetAttribute(modifier.AttributeName);
                        if (attribute != null)
                        {
                            ApplyModifier(spec, attribute, modifier, modifier.CalculateMagnitudeRaw(spec, spec.Level), true);
                        }
                    }

                    for (int i = 0; i < modifiers.Count; i++)
                    {
                        ModifierInfo modifier = modifiers[i];
                        GameplayAttribute attribute = i < spec.TargetAttributes.Length ? spec.TargetAttributes[i] : null;
                        attribute ??= GetAttribute(modifier.AttributeName);
                        if (attribute != null)
                        {
                            ApplyModifier(spec, attribute, modifier, spec.GetCalculatedMagnitudeRaw(i), true);
                        }
                    }
                }
                catch (Exception executionFailure)
                {
                    Exception rollbackFailure = null;
                    for (int i = rollbackSnapshots.Count - 1; i >= 0; i--)
                    {
                        try
                        {
                            InstantAttributeSnapshot snapshot = rollbackSnapshots[i];
                            snapshot.Attribute.SetBaseValueRawUnchecked(snapshot.BaseValueRaw);
                            snapshot.Attribute.SetCurrentValueRawUnchecked(snapshot.CurrentValueRaw);
                            RegisterAttributeInCore(snapshot.Attribute);
                            MarkAttributeValueDirty(snapshot.Attribute);
                        }
                        catch (Exception exception)
                        {
                            rollbackFailure ??= exception;
                        }
                    }

                    try
                    {
                        int addedPredictionSnapshots = predictedAttributeSnapshots.Count - predictionSnapshotStart;
                        while (predictedAttributeSnapshots.Count > predictionSnapshotStart)
                        {
                            predictedAttributeSnapshots.RemoveAt(predictedAttributeSnapshots.Count - 1);
                        }
                        if (addedPredictionSnapshots > 0 && currentPredictionKey.IsValid)
                        {
                            PredictionManager.DecrementPredictedAttributeSnapshotCount(currentPredictionKey, addedPredictionSnapshots);
                        }
                    }
                    catch (Exception exception)
                    {
                        rollbackFailure ??= exception;
                    }

                    if (rollbackFailure != null)
                    {
                        GASLog.Error($"GameplayEffect execution rollback completed with cleanup failures: {rollbackFailure.Message}");
                    }
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(executionFailure).Throw();
                    throw;
                }
            }
            finally
            {
                executionOutput.Clear();
                rollbackSnapshots.Clear();
                if (executionOutput.Capacity > MaxRetainedInstantEffectScratchCapacity)
                {
                    instantExecutionOutputScratch = null;
                }
                if (rollbackSnapshots.Capacity > MaxRetainedInstantEffectScratchCapacity)
                {
                    instantRollbackSnapshotScratch = null;
                }
                instantEffectExecutionInProgress = false;
            }
        }

        internal void ExecutePeriodicEffect(ActiveGameplayEffect effect)
        {
            if (effect?.Spec == null || !ReferenceEquals(effect.Spec.Target, this))
            {
                throw new ArgumentException(
                    "A periodic GameplayEffect must be an active lease owned by this AbilitySystemComponent.",
                    nameof(effect));
            }

            ExecuteInstantEffect(effect.Spec);
            TryDispatchGameplayCues(
                effect.Spec,
                EGameplayCueEvent.Executed,
                effect.ReconciliationId,
                effect.SourceAbilitySpecHandle,
                effect.SourceAbilityExecutionPolicy);
        }

        private static void CaptureInstantAttributeSnapshot(
            GameplayAttribute attribute,
            List<InstantAttributeSnapshot> snapshots)
        {
            if (attribute == null) return;
            for (int i = 0; i < snapshots.Count; i++)
            {
                if (ReferenceEquals(snapshots[i].Attribute, attribute)) return;
            }

            snapshots.Add(new InstantAttributeSnapshot(attribute));
        }

        // --- Tick and State Management ---
        public void Tick(float deltaTime, bool isServer)
        {
            AssertRuntimeThread();
            ThrowIfActiveEffectMutationLocked("AbilitySystemComponent.Tick");
            if (deltaTime < 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime), deltaTime, "Delta time must be finite and non-negative.");
            }

            if (stateDeltaResyncRequired)
            {
                return;
            }

            if (tickingAbilityIterationDepth != 0)
            {
                throw new InvalidOperationException("AbilitySystemComponent.Tick cannot be re-entered while abilities are being ticked.");
            }

            if (simulationFrame == long.MaxValue)
            {
                throw new InvalidOperationException("The GAS simulation frame counter is exhausted. Start a new simulation context.");
            }

            bool mayMutateReplicatedState =
                tickingAbilities.Count > 0 ||
                (isServer && activeEffects.Count > 0) ||
                (!isServer && PredictionManager.WindowCount > 0);
            ReplicationStateBuilder.MutationScope mutationScope = mayMutateReplicatedState
                ? BeginReplicationMutationScope()
                : default;
            using (mutationScope)
            {
                simulationFrame++;
                tickingAbilityTickSnapshot.Clear();
                for (int i = 0; i < tickingAbilities.Count; i++)
                {
                    tickingAbilityTickSnapshot.Add(new TickingAbilityLeaseSnapshot(tickingAbilities[i]));
                }

                tickingAbilityIterationDepth++;
                try
                {
                    for (int i = tickingAbilityTickSnapshot.Count - 1; i >= 0; i--)
                    {
                        TickingAbilityLeaseSnapshot snapshot = tickingAbilityTickSnapshot[i];
                        GameplayAbilitySpec spec = snapshot.Spec;
                        if (spec == null ||
                            spec.LeaseGeneration != snapshot.LeaseGeneration ||
                            !tickingAbilityIndexBySpec.ContainsKey(spec))
                        {
                            continue;
                        }

                        spec.GetPrimaryInstance()?.TickTasks(deltaTime);
                    }
                }
                finally
                {
                    tickingAbilityIterationDepth--;
                    tickingAbilityTickSnapshot.Clear();
                }

                if (!isServer && PredictionManager.WindowCount > 0)
                {
                    TickPredictionWindows(simulationFrame);
                }

                // Recalculate dirty attributes and inhibition before ticking effects. Tag changes
                // during ability tasks may have invalidated ongoing requirements.
                if (dirtyOngoingEffectInhibition)
                {
                    RefreshDirtyOngoingEffectInhibition();
                }

                if (dirtyAttributes.Count > 0)
                {
                    RecalculateDirtyAttributes();
                }

                // Server is authoritative over effect duration.
                if (isServer)
                {
                    activeEffectIterationDepth++;
                    try
                    {
                        for (int i = activeEffects.Count - 1; i >= 0; i--)
                        {
                            var effect = activeEffects[i];
                            bool expired;
                            try
                            {
                                expired = effect.Tick(deltaTime, this);
                            }
                            catch (Exception exception)
                            {
                                GASLog.Error($"Periodic GameplayEffect '{effect.Spec?.Def?.Name}' failed and was removed: {exception.Message}");
                                RemoveActiveEffectAtIndex(i);
                                RemoveFromStackingIndex(effect);
                                OnEffectRemoved(effect, true);
                                continue;
                            }

                            if (expired)
                            {
                                HandleEffectExpiration(effect, i);
                            }
                        }
                    }
                    finally
                    {
                        activeEffectIterationDepth--;
                        FlushDeferredTriggerActivations();
                    }
                }

                // Second pass: recalculate attributes dirtied by periodic effect executions.
                if (dirtyAttributes.Count > 0)
                {
                    RecalculateDirtyAttributes();
                }

                // Attribute changes are collected into the pending delta transaction. A transport adapter
                // may capture, encode, send, and then explicitly commit that transaction.
            }
        }

        /// <summary>
        /// Handles effect expiration with stack expiration policy support.
        /// UE5: EGameplayEffectStackingExpirationPolicy.
        /// </summary>
        private void HandleEffectExpiration(ActiveGameplayEffect effect, int index)
        {
            var stacking = effect.Spec.Def.Stacking;
            if (stacking.Type != EGameplayEffectStackingType.None && effect.StackCount > 1)
            {
                switch (stacking.ExpirationPolicy)
                {
                    case EGameplayEffectStackingExpirationPolicy.RemoveSingleStackAndRefreshDuration:
                    case EGameplayEffectStackingExpirationPolicy.RefreshDuration:
                        // Remove one stack and refresh duration
                        effect.RemoveStack();
                        effect.RefreshDurationAndPeriod();
                        effect.ClearExpired();
                        MarkActiveEffectsDirty();
                        MarkAttributesDirtyFromEffect(effect);
                        return;

                    case EGameplayEffectStackingExpirationPolicy.ClearEntireStack:
                    default:
                        break; // Fall through to full removal
                }
            }

            // Full removal
            RemoveActiveEffectAtIndex(index);
            RemoveFromStackingIndex(effect);
            OnEffectRemoved(effect, true);
        }

        private void RemoveActiveEffectAtIndex(int index)
        {
            ActiveEffectContainer.RemoveAtStable(index);
        }

        private void RebuildActiveEffectReconciliationIdIndex()
        {
            ActiveEffectContainer.RebuildReconciliationIdIndex();
        }

        private bool TryFindActiveEffectIndex(ActiveGameplayEffect effect, out int index)
        {
            return ActiveEffectContainer.TryFindIndex(effect, out index);
        }

        /// <summary>
        /// Removes an effect from the stacking index.
        /// </summary>
        private void RemoveFromStackingIndex(ActiveGameplayEffect effect)
        {
            ActiveEffectContainer.RemoveFromStackingIndex(effect);
        }

        /// <summary>
        /// Recalculates all dirty attributes using UE5-style aggregation formula.
        /// Formula: ((BaseValue + Additive) * Multiplicative) / Division
        /// Multiplicative uses bias-based summation: 1 + (Mod1 - 1) + (Mod2 - 1) + ...
        /// Division uses same bias formula: 1 + (Mod1 - 1) + (Mod2 - 1) + ...
        ///
        /// P0 Optimization: OngoingTagRequirements check is refreshed once per effect scan,
        /// then periodic-only effects are skipped before continuous modifier aggregation.
        /// </summary>
        private void RecalculateDirtyAttributes()
        {
            for (int d = 0; d < dirtyAttributes.Count; d++)
            {
                var attr = dirtyAttributes[d];
                attr.IsDirty = false;

                var currentValue = attr.OwningSet.GetBaseFixedValue(attr);
                for (int channelIndex = 0; channelIndex < modifierChannelAccumulators.Length; channelIndex++)
                {
                    modifierChannelAccumulators[channelIndex].Reset();
                }

                var affectingEffects = attr.AffectingEffects;
                for (int i = 0; i < affectingEffects.Count; i++)
                {
                    var effect = affectingEffects[i];
                    var def = effect.Spec.Def;

                    bool isInhibited = RefreshInhibitionState(effect);
                    if (def.Period > 0 || isInhibited)
                    {
                        continue;
                    }

                    var modifiers = def.Modifiers;
                    int stackCount = effect.StackCount;
                    for (int m = 0; m < modifiers.Count; m++)
                    {
                        if (effect.Spec.TargetAttributes[m] != attr) continue;

                        // For non-snapshotted dynamic magnitudes, recalculate live.
                        long baseMagnitudeRaw;
                        var mod = modifiers[m];
                        if (mod.ShouldRecalculateLiveMagnitude)
                        {
                            effect.Spec.RecalculateCalculatedMagnitude(m);
                            baseMagnitudeRaw = effect.Spec.GetCalculatedMagnitudeRaw(m);
                        }
                        else
                        {
                            baseMagnitudeRaw = effect.Spec.GetCalculatedMagnitudeRaw(m);
                        }

                        var factor = GASFixedValue.FromRaw(baseMagnitudeRaw);
                        int channelIndex = (int)mod.EvaluationChannel;
                        ref ModifierChannelAccumulator accumulator = ref modifierChannelAccumulators[channelIndex];
                        accumulator.HasModifier = true;
                        switch (mod.Operation)
                        {
                            case EAttributeModifierOperation.Add:
                                accumulator.Additive += factor * GASFixedValue.FromInt(stackCount);
                                break;
                            case EAttributeModifierOperation.Multiply:
                                for (int stackIndex = 0; stackIndex < stackCount; stackIndex++)
                                {
                                    accumulator.Multiplier *= factor;
                                }
                                break;
                            case EAttributeModifierOperation.Division:
                                if (factor.RawValue != 0)
                                {
                                    for (int stackIndex = 0; stackIndex < stackCount; stackIndex++)
                                    {
                                        accumulator.Multiplier /= factor;
                                    }
                                }
                                break;
                            case EAttributeModifierOperation.Override:
                                accumulator.OverrideValue = factor;
                                accumulator.HasOverride = true;
                                break;
                        }
                    }
                }

                for (int channelIndex = 0; channelIndex < modifierChannelAccumulators.Length; channelIndex++)
                {
                    ref ModifierChannelAccumulator accumulator = ref modifierChannelAccumulators[channelIndex];
                    if (!accumulator.HasModifier) continue;
                    currentValue = accumulator.HasOverride
                        ? accumulator.OverrideValue
                        : (currentValue + accumulator.Additive) * accumulator.Multiplier;
                }

                attr.OwningSet.PreAttributeChange(attr, ref currentValue);
                // CurrentValue is derived from a mutation that already dirtied this attribute and
                // reserved its replication version. Do not open a second transaction on the later
                // aggregation pass, especially when the original reservation consumed MaxValue.
                attr.SetCurrentValueRawUnchecked(currentValue.RawValue);
            }

            dirtyAttributes.Clear();
        }

        private void OnEffectApplied(ActiveGameplayEffect effect)
        {
            // Update the attribute's internal effect list directly
            var modifiers = effect.Spec.Def.Modifiers;
            for (int i = 0; i < modifiers.Count; i++)
            {
                var attribute = effect.Spec.TargetAttributes[i];
                if (attribute == null) attribute = GetAttribute(modifiers[i].AttributeName);

                if (attribute != null && !HasEarlierModifierForAttribute(effect, attribute, i))
                {
                    attribute.AffectingEffects.Add(effect);
                }
            }

            // Grant abilities via GrantingEffect back-reference (0 GC, no buffer needed)
            if (reconciliationApplyScopeDepth == 0 && effect.Spec.Def.GrantedAbilities.Count > 0)
            {
                var grantedAbilities = effect.Spec.Def.GrantedAbilities;
                for (int i = 0; i < grantedAbilities.Count; i++)
                {
                    var newSpec = GrantAbility(grantedAbilities[i], effect.Spec.Level);
                    RegisterAbilityGrantedByEffect(effect, newSpec);
                }
            }

            // Maintain the internal query index before publishing post-commit notifications.
            UpdateGrantedTagIndex_Applied(effect);

            // Publish tag edges only after every structural back-reference is committed. Tag callbacks
            // may synchronously query the ASC, so they must never observe a half-applied effect.
            if (!effect.Spec.Def.GrantedTags.IsEmpty)
            {
                fromEffectsTags.AddTags(effect.Spec.Def.GrantedTags);
                combinedTags.AddTags(effect.Spec.Def.GrantedTags);
            }
            if (!effect.Spec.DynamicGrantedTags.IsEmpty)
            {
                fromEffectsTags.AddTags(effect.Spec.DynamicGrantedTags);
                combinedTags.AddTags(effect.Spec.DynamicGrantedTags);
            }

            if (!effect.Spec.Def.OngoingTagRequirements.IsEmpty)
            {
                RefreshInhibitionState(effect);
            }

            if (!SuppressLocalGameplayCueDispatch)
            {
                TryDispatchGameplayCues(
                    effect.Spec,
                    EGameplayCueEvent.OnActive,
                    effect.ReconciliationId,
                    effect.SourceAbilitySpecHandle,
                    effect.SourceAbilityExecutionPolicy);
            }

            InvokeEffectObserversSafely(effectAppliedObservers, effect, "OnGameplayEffectAppliedToSelf");
        }

        private void InvokeEffectObserversSafely(
            GASCallbackList<ActiveEffectDelegate> observers,
            ActiveGameplayEffect effect,
            string observerName)
        {
            EnterRuntimeCallbackDispatch();
            bool callbackListDispatchStarted = false;
            try
            {
                int count = observers.BeginDispatch();
                callbackListDispatchStarted = true;
                for (int i = 0; i < count; i++)
                {
                    ActiveEffectDelegate observer = observers.GetCallback(i);
                    if (observer == null)
                    {
                        continue;
                    }

                    try
                    {
                        observer.Invoke(effect);
                    }
                    catch (Exception exception)
                    {
                        GASLog.Error($"{observerName} observer failed after authoritative state commit: {exception.Message}");
                    }
                }
            }
            finally
            {
                try
                {
                    if (callbackListDispatchStarted)
                    {
                        observers.EndDispatch();
                    }
                }
                finally
                {
                    ExitRuntimeCallbackDispatch();
                }
            }
        }

        private bool HasEarlierModifierForAttribute(ActiveGameplayEffect effect, GameplayAttribute attribute, int modifierIndex)
        {
            for (int i = 0; i < modifierIndex; i++)
            {
                var previousAttribute = effect.Spec.TargetAttributes[i];
                if (previousAttribute == null)
                {
                    previousAttribute = GetAttribute(effect.Spec.Def.Modifiers[i].AttributeName);
                }

                if (ReferenceEquals(previousAttribute, attribute))
                {
                    return true;
                }
            }

            return false;
        }

        private static void RemoveAffectingEffect(GameplayAttribute attribute, ActiveGameplayEffect effect)
        {
            var effects = attribute.AffectingEffects;
            for (int i = effects.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(effects[i], effect))
                {
                    continue;
                }

                effects.RemoveAt(i);
                return;
            }
        }

        private static void TryRemoveTagsForCleanup(
            GameplayTagCountContainer destination,
            IReadOnlyGameplayTagContainer tags,
            ref Exception cleanupFailure)
        {
            if (destination == null || tags == null || tags.IsEmpty)
            {
                return;
            }

            try
            {
                destination.RemoveTags(tags);
            }
            catch (Exception exception)
            {
                CaptureCleanupFailure(ref cleanupFailure, exception);
            }
        }

        private static void CaptureCleanupFailure(ref Exception cleanupFailure, Exception exception)
        {
            if (exception == null)
            {
                return;
            }

            if (cleanupFailure == null)
            {
                cleanupFailure = exception;
                return;
            }

            if (cleanupFailure is AggregateException aggregate)
            {
                var failures = new Exception[aggregate.InnerExceptions.Count + 1];
                for (int i = 0; i < aggregate.InnerExceptions.Count; i++)
                {
                    failures[i] = aggregate.InnerExceptions[i];
                }
                failures[failures.Length - 1] = exception;
                cleanupFailure = new AggregateException(failures);
                return;
            }

            cleanupFailure = new AggregateException(cleanupFailure, exception);
        }

        private void OnEffectRemoved(ActiveGameplayEffect effect, bool markDirty)
        {
            if (effect?.Spec?.Def == null) return;
            Exception cleanupFailure = null;
            int reconciliationId = effect.ReconciliationId;
            try
            {
                PredictionManager.RemovePendingPredictedEffect(effect);
                try { RemoveActiveEffectFromCore(effect); }
                catch (Exception exception) { CaptureCleanupFailure(ref cleanupFailure, exception); }
                try { ActiveEffectContainer.UntrackAppliedEffectFromAbilities(effect, ReturnAbilityAppliedEffectList); }
                catch (Exception exception) { CaptureCleanupFailure(ref cleanupFailure, exception); }

                try { UpdateGrantedTagIndex_Removed(effect); }
                catch (Exception exception) { CaptureCleanupFailure(ref cleanupFailure, exception); }

                IReadOnlyList<ModifierInfo> modifiers = effect.Spec.Def.Modifiers;
                for (int i = 0; i < modifiers.Count; i++)
                {
                    GameplayAttribute attribute = i < effect.Spec.TargetAttributes.Length
                        ? effect.Spec.TargetAttributes[i]
                        : null;
                    attribute ??= GetAttribute(modifiers[i].AttributeName);
                    if (attribute != null && !HasEarlierModifierForAttribute(effect, attribute, i))
                    {
                        RemoveAffectingEffect(attribute, effect);
                    }
                }

                if (AbilitySpecs.TryGetGrantedSpecs(effect, out var grantedSpecs))
                {
                    while (grantedSpecs.Count > 0)
                    {
                        int previousCount = grantedSpecs.Count;
                        GameplayAbilitySpec grantedSpec = grantedSpecs[previousCount - 1];
                        try { ClearAbilityInternal(grantedSpec); }
                        catch (Exception exception) { CaptureCleanupFailure(ref cleanupFailure, exception); }
                        if (grantedSpecs.Count == previousCount)
                        {
                            grantedSpecs.RemoveAt(previousCount - 1);
                        }
                    }
                }

                // Publish removal edges only after indexes, modifiers, and effect-granted abilities no
                // longer reference the effect. Synchronous tag callbacks therefore see committed state.
                TryRemoveTagsForCleanup(fromEffectsTags, effect.Spec.Def.GrantedTags, ref cleanupFailure);
                TryRemoveTagsForCleanup(combinedTags, effect.Spec.Def.GrantedTags, ref cleanupFailure);
                TryRemoveTagsForCleanup(fromEffectsTags, effect.Spec.DynamicGrantedTags, ref cleanupFailure);
                TryRemoveTagsForCleanup(combinedTags, effect.Spec.DynamicGrantedTags, ref cleanupFailure);

                if (!SuppressLocalGameplayCueDispatch &&
                    !effect.Spec.Def.SuppressGameplayCues &&
                    effect.Spec.Def.DurationPolicy != EDurationPolicy.Instant &&
                    !effect.Spec.Def.GameplayCues.IsEmpty)
                {
                    TryDispatchGameplayCues(
                        effect.Spec,
                        EGameplayCueEvent.Removed,
                        effect.ReconciliationId,
                        effect.SourceAbilitySpecHandle,
                        effect.SourceAbilityExecutionPolicy);
                }

                InvokeEffectObserversSafely(effectRemovedObservers, effect, "OnGameplayEffectRemovedFromSelf");
                if (markDirty) MarkAttributesDirtyFromEffect(effect);
                MarkActiveEffectsDirty();
                TrackRemovedEffectReconciliationId(reconciliationId);

                if (GASTrace.Enabled)
                {
                    GASTrace.Record(
                        GASTraceEventType.EffectRemoved,
                        this,
                        effect: effect.Spec.Def,
                        decision: GASTraceDecision.Success,
                        source: effect.Spec.Source,
                        level: effect.Spec.Level,
                        stackCount: effect.StackCount,
                        reconciliationId: reconciliationId);
                }
            }
            finally
            {
                try
                {
                    effect.ReleaseRuntimeLease();
                }
                catch (Exception exception)
                {
                    CaptureCleanupFailure(ref cleanupFailure, exception);
                }
            }

            if (cleanupFailure != null)
            {
                GASLog.Error($"GameplayEffect removal completed with cleanup failures: {cleanupFailure.Message}");
            }
        }

        private void DispatchGameplayCues(GameplayEffectSpec spec, EGameplayCueEvent eventType)
        {
            CueDispatcher.DispatchGameplayCues(spec, eventType);
        }

        private void TryDispatchGameplayCues(
            GameplayEffectSpec spec,
            EGameplayCueEvent eventType,
            int activeEffectReconciliationId = 0,
            int sourceAbilitySpecHandle = 0,
            EAbilityExecutionPolicy sourceAbilityExecutionPolicy = EAbilityExecutionPolicy.Invalid)
        {
            try
            {
                DispatchGameplayCues(spec, eventType);
            }
            catch (Exception exception)
            {
                GASLog.Error($"GameplayCue dispatch failed after authoritative state commit: {exception.Message}");
            }

            NotifyGameplayCuesCommitted(
                spec,
                eventType,
                activeEffectReconciliationId,
                sourceAbilitySpecHandle,
                sourceAbilityExecutionPolicy);
        }

        private void NotifyGameplayCuesCommitted(
            GameplayEffectSpec spec,
            EGameplayCueEvent eventType,
            int activeEffectReconciliationId,
            int sourceAbilitySpecHandle,
            EAbilityExecutionPolicy sourceAbilityExecutionPolicy)
        {
            if (gameplayCueCommittedObservers.ActiveCount == 0 ||
                spec?.Def == null ||
                spec.Def.SuppressGameplayCues ||
                spec.Def.GameplayCues.IsEmpty)
            {
                return;
            }

            GameplayAbility sourceAbility = spec.Context?.AbilityInstance;
            if (sourceAbilitySpecHandle == 0)
            {
                sourceAbilitySpecHandle = sourceAbility?.Spec?.Handle ?? 0;
            }
            if (sourceAbilityExecutionPolicy == EAbilityExecutionPolicy.Invalid)
            {
                sourceAbilityExecutionPolicy =
                    sourceAbility?.ExecutionPolicy ?? EAbilityExecutionPolicy.Invalid;
            }

            EnterRuntimeCallbackDispatch();
            bool callbackListDispatchStarted = false;
            try
            {
                int observerCount = gameplayCueCommittedObservers.BeginDispatch();
                callbackListDispatchStarted = true;
                // Implicit parent tags support hierarchy matching and are not independent cue
                // emissions. The committed stream must mirror the authored cue events exactly.
                foreach (GameplayTag cueTag in spec.Def.GameplayCues.GetExplicitTags())
                {
                    if (!cueTag.IsValid || cueTag.IsNone)
                    {
                        continue;
                    }

                    var committedCue = new GameplayCueCommitted(
                        cueTag,
                        eventType,
                        spec.Source,
                        spec.Target,
                        spec.Def,
                        spec.Level,
                        spec.DurationRaw,
                        spec.Context?.PredictionKey ?? default,
                        activeEffectReconciliationId,
                        sourceAbilitySpecHandle,
                        sourceAbilityExecutionPolicy,
                        stateVersion);
                    for (int i = 0; i < observerCount; i++)
                    {
                        GameplayCueCommittedDelegate observer =
                            gameplayCueCommittedObservers.GetCallback(i);
                        if (observer == null)
                        {
                            continue;
                        }

                        try
                        {
                            observer.Invoke(in committedCue);
                        }
                        catch (Exception exception)
                        {
                            GASLog.Error(
                                $"OnGameplayCueCommitted observer failed after state commit: {exception.Message}");
                        }
                    }
                }
            }
            finally
            {
                try
                {
                    if (callbackListDispatchStarted)
                    {
                        gameplayCueCommittedObservers.EndDispatch();
                    }
                }
                finally
                {
                    ExitRuntimeCallbackDispatch();
                }
            }
        }

        private void EnsureEffectReplicationSetByCallerCapacity(int count)
        {
            if (count <= 0 || effectReplicationSetByCallerTags.Length >= count)
            {
                return;
            }

            int next = Math.Max(count, effectReplicationSetByCallerTags.Length == 0 ? 4 : effectReplicationSetByCallerTags.Length * 2);
            Array.Resize(ref effectReplicationSetByCallerTags, next);
            Array.Resize(ref effectReplicationSetByCallerValuesRaw, next);
        }

        private void EnsureEffectReplicationExtendedCapacity(
            int setByCallerNameCount,
            int dynamicGrantedTagCount,
            int dynamicAssetTagCount)
        {
            EnsureArrayCapacity(ref effectReplicationSetByCallerNames, setByCallerNameCount);
            EnsureArrayCapacity(ref effectReplicationSetByCallerNameValuesRaw, setByCallerNameCount);
            EnsureArrayCapacity(ref effectReplicationDynamicGrantedTags, dynamicGrantedTagCount);
            EnsureArrayCapacity(ref effectReplicationDynamicAssetTags, dynamicAssetTagCount);
        }

        private static int CopyExplicitTags<T>(in T source, GameplayTag[] destination)
            where T : IReadOnlyGameplayTagContainer
        {
            if (destination == null || source.IsEmpty)
            {
                return 0;
            }

            int index = 0;
            GameplayTagEnumerator tags = source.GetExplicitTags();
            while (index < destination.Length && tags.MoveNext())
            {
                destination[index++] = tags.Current;
            }
            return index;
        }

        private static void EnsureArrayCapacity<T>(ref T[] buffer, int count)
        {
            if (count <= 0 || buffer.Length >= count) return;
            int next = Math.Max(count, buffer.Length == 0 ? 4 : buffer.Length * 2);
            Array.Resize(ref buffer, next);
        }

        // --- Tag Management ---
        // Incremental tag updates avoid a full rebuild of the combined tag state.
        public void AddLooseGameplayTag(GameplayTag tag)
        {
            AssertRuntimeThread();
            if (!tag.IsValid || tag.IsNone)
            {
                throw new ArgumentException("A valid GameplayTag is required.", nameof(tag));
            }

            using (BeginReplicationMutationScope())
            {
                looseTags.AddTag(tag);
                combinedTags.AddTag(tag);
            }
        }

        public void RemoveLooseGameplayTag(GameplayTag tag)
        {
            AssertRuntimeThread();
            if (!tag.IsValid || tag.IsNone)
            {
                throw new ArgumentException("A valid GameplayTag is required.", nameof(tag));
            }
            if (looseTags.GetExplicitTagCount(tag) <= 0)
            {
                return;
            }

            using (BeginReplicationMutationScope())
            {
                looseTags.RemoveTag(tag);
                combinedTags.RemoveTag(tag);
            }
        }

        #region Missing UE5 GAS Features

        /// <summary>
        /// Cancels all currently active abilities that have ANY of the specified tags.
        /// UE5: Integrated into ability activation flow via CancelAbilitiesWithTag.
        /// </summary>
        public void CancelAbilitiesWithTags(GameplayTagContainer tags)
        {
            AssertRuntimeThread();
            ThrowIfActiveEffectMutationLocked("Ability cancellation");
            if (tags == null || tags.IsEmpty) return;

            bool hasMatchingActiveAbility = false;
            for (int i = 0; i < activatableAbilities.Count; i++)
            {
                var spec = activatableAbilities[i];
                if (!spec.IsLocallyExecuting) continue;

                var abilityTags = spec.GetPrimaryInstance()?.AbilityTags;
                if (abilityTags != null && abilityTags.HasAny(tags))
                {
                    hasMatchingActiveAbility = true;
                    break;
                }
            }

            if (!hasMatchingActiveAbility)
            {
                return;
            }

            using (BeginReplicationMutationScope())
            {
                for (int i = activatableAbilities.Count - 1; i >= 0; i--)
                {
                    var spec = activatableAbilities[i];
                    if (!spec.IsLocallyExecuting) continue;

                    var abilityTags = spec.GetPrimaryInstance()?.AbilityTags;
                    if (abilityTags != null && abilityTags.HasAny(tags))
                    {
                        spec.GetPrimaryInstance()?.CancelAbility();
                    }
                }
            }
        }

        /// <summary>
        /// Checks if any currently active ability is blocking the given tags.
        /// UE5: BlockAbilitiesWithTag check during CanActivateAbility.
        /// </summary>
        public bool AreAbilitiesBlockedByTag(GameplayTagContainer abilityTags)
        {
            if (abilityTags == null || abilityTags.IsEmpty) return false;

            for (int i = 0; i < activatableAbilities.Count; i++)
            {
                var spec = activatableAbilities[i];
                if (!spec.IsActive) continue;

                var blockTags = spec.GetPrimaryInstance()?.BlockAbilitiesWithTag;
                if (blockTags != null && !blockTags.IsEmpty && abilityTags.HasAny(blockTags))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Changes the remaining duration of an active gameplay effect.
        /// UE5: Section 4.5.16 - Changing Active Gameplay Effect Duration.
        /// </summary>
        /// <returns>True if the effect was found and its duration was changed.</returns>
        public bool ChangeActiveEffectDuration(ActiveGameplayEffect effect, float newDuration)
        {
            AssertRuntimeThread();
            if (effectMutationTransactionDepth != 0 || activeEffectIterationDepth != 0) return false;
            if (effect == null || effect.IsExpired) return false;
            if (effect.Spec.Def.DurationPolicy != EDurationPolicy.HasDuration) return false;

            if (!TryFindActiveEffectIndex(effect, out _))
            {
                return false;
            }

            if (float.IsNaN(newDuration) || float.IsInfinity(newDuration) || newDuration < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(newDuration), newDuration, "Remaining duration must be finite and non-negative.");
            }

            long newDurationRaw = GASFixedValue.FromFloat(newDuration).RawValue;
            if (effect.TimeRemainingRaw == newDurationRaw)
            {
                return true;
            }

            using (BeginReplicationMutationScope())
            {
                effect.SetRemainingDurationRaw(newDurationRaw);
                MarkActiveEffectsDirty();
            }
            return true;
        }

        public bool TryRemoveActiveEffect(ActiveGameplayEffect effect)
        {
            AssertRuntimeThread();
            if (effectMutationTransactionDepth != 0 || activeEffectIterationDepth != 0) return false;
            if (effect == null)
            {
                return false;
            }

            if (!TryFindActiveEffectIndex(effect, out int index))
            {
                return false;
            }

            using (BeginReplicationMutationScope())
            {
                RemoveActiveEffectAtIndex(index);
                RemoveFromStackingIndex(effect);
                OnEffectRemoved(effect, true);
            }
            return true;
        }

        public bool TryApplyActiveEffectStackChange(ActiveGameplayEffect effect, int newStackCount)
        {
            AssertRuntimeThread();
            if (effectMutationTransactionDepth != 0 || activeEffectIterationDepth != 0) return false;
            if (effect == null || effect.IsExpired)
            {
                return false;
            }

            if (!TryFindActiveEffectIndex(effect, out _))
            {
                return false;
            }

            if (effect.StackCount == newStackCount)
            {
                return true;
            }

            using (BeginReplicationMutationScope())
            {
                effect.SetReplicatedStackCount(newStackCount);
                MarkActiveEffectsDirty();
                MarkAttributesDirtyFromEffect(effect);
            }
            return true;
        }

        public bool TryApplyReplicatedEffectUpdate(
            ActiveGameplayEffect effect,
            int level,
            int stackCount,
            float duration,
            float timeRemaining,
            float periodTimeRemaining,
            GameplayTag[] setByCallerTags,
            float[] setByCallerValues,
            int setByCallerCount)
        {
            AssertRuntimeThread();
            if (effectMutationTransactionDepth != 0 || activeEffectIterationDepth != 0) return false;
            if (effect == null || effect.IsExpired)
            {
                return false;
            }

            if (!TryFindActiveEffectIndex(effect, out _))
            {
                return false;
            }

            using (BeginReplicationMutationScope())
            {
                effect.ApplyReplicatedState(level, stackCount, duration, timeRemaining, periodTimeRemaining, setByCallerTags, setByCallerValues, setByCallerCount);
                MarkActiveEffectsDirty();
                MarkAttributesDirtyFromEffect(effect);
            }
            return true;
        }

        public bool TryApplyReplicatedEffectUpdateRaw(
            ActiveGameplayEffect effect,
            int level,
            int stackCount,
            long durationRaw,
            long timeRemainingRaw,
            long periodTimeRemainingRaw,
            GameplayTag[] setByCallerTags,
            long[] setByCallerValuesRaw,
            int setByCallerCount)
        {
            AssertRuntimeThread();
            if (effectMutationTransactionDepth != 0 || activeEffectIterationDepth != 0) return false;
            if (effect == null || effect.IsExpired)
            {
                return false;
            }

            if (!TryFindActiveEffectIndex(effect, out _))
            {
                return false;
            }

            using (BeginReplicationMutationScope())
            {
                effect.ApplyReplicatedStateRaw(level, stackCount, durationRaw, timeRemainingRaw, periodTimeRemainingRaw, setByCallerTags, setByCallerValuesRaw, setByCallerCount);
                MarkActiveEffectsDirty();
                MarkAttributesDirtyFromEffect(effect);
            }
            return true;
        }

        public bool TryApplyReplicatedEffectUpdateRaw(
            ActiveGameplayEffect effect,
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
            AssertRuntimeThread();
            if (effectMutationTransactionDepth != 0 || activeEffectIterationDepth != 0) return false;
            if (effect == null ||
                effect.IsExpired ||
                !TryFindActiveEffectIndex(effect, out _) ||
                !MatchesExplicitTags(effect.Spec.DynamicGrantedTags, dynamicGrantedTags, dynamicGrantedTagCount) ||
                !MatchesExplicitTags(effect.Spec.DynamicAssetTags, dynamicAssetTags, dynamicAssetTagCount))
            {
                return false;
            }

            using (BeginReplicationMutationScope())
            {
                effect.ApplyReplicatedStateRaw(
                    level,
                    stackCount,
                    durationRaw,
                    timeRemainingRaw,
                    periodTimeRemainingRaw,
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
                MarkActiveEffectsDirty();
                MarkAttributesDirtyFromEffect(effect);
            }
            return true;
        }

        private static bool MatchesExplicitTags<T>(in T existing, GameplayTag[] expected, int expectedCount)
            where T : IReadOnlyGameplayTagContainer
        {
            if (expectedCount < 0 || existing.ExplicitTagCount != expectedCount)
            {
                return false;
            }

            for (int i = 0; i < expectedCount; i++)
            {
                if (!existing.HasTagExact(expected[i]))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Finds the first active effect matching the given GameplayEffect definition.
        /// </summary>
        public ActiveGameplayEffect FindActiveEffectByDef(GameplayEffect def)
        {
            for (int i = 0; i < activeEffects.Count; i++)
            {
                if (activeEffects[i].Spec.Def == def && !activeEffects[i].IsExpired)
                    return activeEffects[i];
            }
            return null;
        }

        /// <summary>
        /// Gets the current stack count of an active effect by its definition.
        /// Returns 0 if the effect is not active.
        /// UE5: GetCurrentStackCount.
        /// </summary>
        public int GetCurrentStackCount(GameplayEffect def)
        {
            var effect = FindActiveEffectByDef(def);
            return effect?.StackCount ?? 0;
        }

        #endregion

        #region Tag Convenience API

        /// <summary>
        /// Checks if the owner has a specific gameplay tag.
        /// UE5: HasMatchingGameplayTag.
        /// </summary>
        public bool HasMatchingGameplayTag(GameplayTag tag)
        {
            AssertRuntimeThread();
            return combinedTags.HasTag(tag);
        }

        /// <summary>
        /// Checks if the owner has ALL of the specified gameplay tags.
        /// UE5: HasAllMatchingGameplayTags.
        /// </summary>
        public bool HasAllMatchingGameplayTags(GameplayTagContainer tags)
        {
            AssertRuntimeThread();
            return tags == null || tags.IsEmpty || combinedTags.HasAll(tags);
        }

        public bool HasAllMatchingGameplayTags(ReadOnlyGameplayTagContainer tags)
        {
            AssertRuntimeThread();
            if (tags == null || tags.IsEmpty)
            {
                return true;
            }

            var indices = tags.GetImplicitIndices();
            for (int i = 0; i < indices.Length; i++)
            {
                if (!combinedTags.ContainsRuntimeIndex(indices[i], false))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Checks if the owner has ANY of the specified gameplay tags.
        /// UE5: HasAnyMatchingGameplayTags.
        /// </summary>
        public bool HasAnyMatchingGameplayTags(GameplayTagContainer tags)
        {
            AssertRuntimeThread();
            return tags != null && !tags.IsEmpty && combinedTags.HasAny(tags);
        }

        public bool HasAnyMatchingGameplayTags(ReadOnlyGameplayTagContainer tags)
        {
            AssertRuntimeThread();
            if (tags == null || tags.IsEmpty)
            {
                return false;
            }

            var indices = tags.GetImplicitIndices();
            for (int i = 0; i < indices.Length; i++)
            {
                if (combinedTags.ContainsRuntimeIndex(indices[i], false))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Checks if the owner has ANY of the specified gameplay tags using explicit-only matching.
        /// This avoids sibling false positives when tags share parents (e.g., Cooldown.Skill.Fireball
        /// should not match Cooldown.Skill.Purify unless the shared parent is explicitly present).
        /// </summary>
        public bool HasAnyMatchingGameplayTagsExact(ReadOnlyGameplayTagContainer tags)
        {
            AssertRuntimeThread();
            if (tags == null || tags.IsEmpty)
            {
                return false;
            }

            var indices = tags.GetExplicitIndices();
            for (int i = 0; i < indices.Length; i++)
            {
                if (combinedTags.ContainsRuntimeIndex(indices[i], true))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Gets the count of how many times a specific tag is applied (from loose + effects).
        /// UE5: GetTagCount.
        /// </summary>
        public int GetTagCount(GameplayTag tag)
        {
            AssertRuntimeThread();
            return combinedTags.GetTagCount(tag);
        }

        private bool MeetsTagRequirements(ReadOnlyGameplayTagContainer requiredTags, ReadOnlyGameplayTagContainer forbiddenTags)
        {
            return !HasAnyMatchingGameplayTags(forbiddenTags) && HasAllMatchingGameplayTags(requiredTags);
        }

        #endregion

        #region Ability Trigger System

        /// <summary>
        /// Registers all triggers defined on the ability spec.
        /// Called when an ability is granted.
        /// </summary>
        private void RegisterAbilityTriggers(GameplayAbilitySpec spec)
        {
            var ability = spec.GetPrimaryInstance();
            if (ability?.AbilityTriggers == null || ability.AbilityTriggers.Count == 0) return;

            for (int i = 0; i < ability.AbilityTriggers.Count; i++)
            {
                var trigger = ability.AbilityTriggers[i];
                if (trigger.TriggerTag.IsNone) continue;

                switch (trigger.TriggerSource)
                {
                    case EAbilityTriggerSource.GameplayEvent:
                        AddToTriggerMap(triggerEventAbilities, trigger.TriggerTag, spec);
                        break;
                    case EAbilityTriggerSource.OwnedTagAdded:
                        AddToTriggerMap(triggerTagAddedAbilities, trigger.TriggerTag, spec);
                        break;
                    case EAbilityTriggerSource.OwnedTagRemoved:
                        AddToTriggerMap(triggerTagRemovedAbilities, trigger.TriggerTag, spec);
                        break;
                }
            }
        }

        /// <summary>
        /// Unregisters all triggers defined on the ability spec.
        /// Called when an ability is removed.
        /// </summary>
        private void UnregisterAbilityTriggers(GameplayAbilitySpec spec)
        {
            var ability = spec.GetPrimaryInstance();
            if (ability?.AbilityTriggers == null || ability.AbilityTriggers.Count == 0) return;

            for (int i = 0; i < ability.AbilityTriggers.Count; i++)
            {
                var trigger = ability.AbilityTriggers[i];
                if (trigger.TriggerTag.IsNone) continue;

                switch (trigger.TriggerSource)
                {
                    case EAbilityTriggerSource.GameplayEvent:
                        RemoveFromTriggerMap(triggerEventAbilities, trigger.TriggerTag, spec);
                        break;
                    case EAbilityTriggerSource.OwnedTagAdded:
                        RemoveFromTriggerMap(triggerTagAddedAbilities, trigger.TriggerTag, spec);
                        break;
                    case EAbilityTriggerSource.OwnedTagRemoved:
                        RemoveFromTriggerMap(triggerTagRemovedAbilities, trigger.TriggerTag, spec);
                        break;
                }
            }
        }

        /// <summary>
        /// Callback fired when a trigger-related tag changes on the owner.
        /// </summary>
        private void OnTriggerTagChanged(GameplayTag tag, int newCount)
        {
            if (newCount > 0)
            {
                // Tag was added
                if (triggerTagAddedAbilities.TryGetValue(tag, out var addedSpecs))
                {
                    for (int i = 0; i < addedSpecs.Count; i++)
                    {
                        if (!addedSpecs[i].IsActive)
                        {
                            RequestTriggerActivation(addedSpecs[i]);
                        }
                    }
                }
            }
            else
            {
                // Tag was removed
                if (triggerTagRemovedAbilities.TryGetValue(tag, out var removedSpecs))
                {
                    for (int i = 0; i < removedSpecs.Count; i++)
                    {
                        if (!removedSpecs[i].IsActive)
                        {
                            RequestTriggerActivation(removedSpecs[i]);
                        }
                    }
                }
            }
        }

        private void RequestTriggerActivation(GameplayAbilitySpec spec)
        {
            if (spec == null || spec.IsActive)
            {
                return;
            }

            if (effectMutationTransactionDepth == 0 && activeEffectIterationDepth == 0)
            {
                TryActivateAbility(spec);
                return;
            }

            for (int i = 0; i < deferredTriggerActivations.Count; i++)
            {
                if (ReferenceEquals(deferredTriggerActivations[i], spec))
                {
                    return;
                }
            }

            if (deferredTriggerActivations.Count >= Limits.MaxGrantedAbilities)
            {
                throw new InvalidOperationException(
                    $"Deferred ability-trigger activation exceeded the bounded capacity of {Limits.MaxGrantedAbilities}.");
            }

            deferredTriggerActivations.Add(spec);
        }

        private void FlushDeferredTriggerActivations()
        {
            if (flushingDeferredTriggerActivations ||
                effectMutationTransactionDepth != 0 ||
                activeEffectIterationDepth != 0 ||
                deferredTriggerActivations.Count == 0)
            {
                return;
            }

            flushingDeferredTriggerActivations = true;
            try
            {
                int budget = Limits.MaxGrantedAbilities;
                while (deferredTriggerActivations.Count > 0 && budget-- > 0)
                {
                    int lastIndex = deferredTriggerActivations.Count - 1;
                    GameplayAbilitySpec spec = deferredTriggerActivations[lastIndex];
                    deferredTriggerActivations.RemoveAt(lastIndex);
                    if (spec != null && !spec.IsActive)
                    {
                        TryActivateAbility(spec);
                    }
                }

                if (deferredTriggerActivations.Count > 0)
                {
                    deferredTriggerActivations.Clear();
                    GASLog.Error(
                        $"Deferred ability-trigger activation exceeded the per-flush budget of {Limits.MaxGrantedAbilities}; remaining requests were discarded.");
                }
            }
            finally
            {
                flushingDeferredTriggerActivations = false;
            }
        }

        private static void AddToTriggerMap(Dictionary<GameplayTag, List<GameplayAbilitySpec>> map, GameplayTag tag, GameplayAbilitySpec spec)
        {
            if (!map.TryGetValue(tag, out var list))
            {
                list = new List<GameplayAbilitySpec>(2);
                map[tag] = list;
            }
            list.Add(spec);
        }

        private static void RemoveFromTriggerMap(Dictionary<GameplayTag, List<GameplayAbilitySpec>> map, GameplayTag tag, GameplayAbilitySpec spec)
        {
            if (map.TryGetValue(tag, out var list))
            {
                list.Remove(spec);
                if (list.Count == 0) map.Remove(tag);
            }
        }

        #endregion

        public void Dispose()
        {
            if (disposing || disposed)
            {
                return;
            }

            RuntimeContext.AssertOwnerThread();
            if (effectMutationTransactionDepth != 0 ||
                activeEffectIterationDepth != 0 ||
                tickingAbilityIterationDepth != 0 ||
                callbackDispatchDepth != 0 ||
                ReplicationStateBuilder.MutationScopeDepth != 0)
            {
                throw new InvalidOperationException("AbilitySystemComponent cannot be disposed from inside a runtime mutation, tick, or observer callback.");
            }
            disposing = true;
            Exception cleanupFailure = null;
            try
            {
                try
                {
                    combinedTags.OnAnyTagNewOrRemove -= HandleCombinedTagCountChange;
                    combinedTags.OnAnyTagCountChange -= HandleCombinedAnyTagCountChange;
                    looseTags.OnAnyTagNewOrRemove -= TrackLooseTagCountChange;
                }
                catch (Exception exception)
                {
                    CaptureCleanupFailure(ref cleanupFailure, exception);
                }

                try
                {
                    combinedTags.RemoveAllTagEventCallbacks();
                }
                catch (Exception exception)
                {
                    CaptureCleanupFailure(ref cleanupFailure, exception);
                }

                try
                {
                    RuntimeContext.CueManager.RemoveAllCuesFor(this);
                }
                catch (Exception exception)
                {
                    CaptureCleanupFailure(ref cleanupFailure, exception);
                }

                // Cancel abilities before returning effects. Ability shutdown may remove effects that
                // were configured to follow the ability lifetime, so effects must remain valid here.
                for (int i = activatableAbilities.Count - 1; i >= 0; i--)
                {
                    GameplayAbilitySpec spec = activatableAbilities[i];
                    try
                    {
                        abilityEndMutationBypassDepth++;
                        try
                        {
                            spec?.OnRemoveSpec();
                        }
                        finally
                        {
                            abilityEndMutationBypassDepth--;
                        }
                    }
                    catch (Exception exception)
                    {
                        CaptureCleanupFailure(ref cleanupFailure, exception);
                    }
                    finally
                    {
                        try
                        {
                            spec?.ReleaseRuntimeLease();
                        }
                        catch (Exception exception)
                        {
                            CaptureCleanupFailure(ref cleanupFailure, exception);
                        }
                    }
                }

                for (int i = activeEffects.Count - 1; i >= 0; i--)
                {
                    CleanupActiveEffectForShutdown(activeEffects[i], ref cleanupFailure);
                }
            }
            catch (Exception exception)
            {
                CaptureCleanupFailure(ref cleanupFailure, exception);
            }
            finally
            {
                FinalizeDisposedState(ref cleanupFailure);

                try
                {
                    RuntimeContext.UnregisterAbilitySystem();
                }
                catch (Exception exception)
                {
                    CaptureCleanupFailure(ref cleanupFailure, exception);
                }

                if (ownsRuntimeContext)
                {
                    try
                    {
                        RuntimeContext.Dispose();
                    }
                    catch (Exception exception)
                    {
                        CaptureCleanupFailure(ref cleanupFailure, exception);
                    }
                }

                disposed = true;
                disposing = false;
            }

            if (cleanupFailure != null)
            {
                GASLog.Error($"AbilitySystemComponent disposal completed with cleanup failures: {cleanupFailure.Message}");
            }
        }

        private void CleanupActiveEffectForShutdown(
            ActiveGameplayEffect effect,
            ref Exception cleanupFailure)
        {
            if (effect?.Spec?.Def == null)
            {
                try { effect?.ReleaseRuntimeLease(); }
                catch (Exception exception) { CaptureCleanupFailure(ref cleanupFailure, exception); }
                return;
            }

            try { RemoveActiveEffectFromCore(effect); }
            catch (Exception exception) { CaptureCleanupFailure(ref cleanupFailure, exception); }
            try { ActiveEffectContainer.UntrackAppliedEffectFromAbilities(effect, ReturnAbilityAppliedEffectList); }
            catch (Exception exception) { CaptureCleanupFailure(ref cleanupFailure, exception); }
            try { RemoveFromStackingIndex(effect); }
            catch (Exception exception) { CaptureCleanupFailure(ref cleanupFailure, exception); }
            try { UpdateGrantedTagIndex_Removed(effect); }
            catch (Exception exception) { CaptureCleanupFailure(ref cleanupFailure, exception); }

            TryRemoveTagsForCleanup(fromEffectsTags, effect.Spec.Def.GrantedTags, ref cleanupFailure);
            TryRemoveTagsForCleanup(combinedTags, effect.Spec.Def.GrantedTags, ref cleanupFailure);
            TryRemoveTagsForCleanup(fromEffectsTags, effect.Spec.DynamicGrantedTags, ref cleanupFailure);
            TryRemoveTagsForCleanup(combinedTags, effect.Spec.DynamicGrantedTags, ref cleanupFailure);

            try
            {
                IReadOnlyList<ModifierInfo> modifiers = effect.Spec.Def.Modifiers;
                for (int i = 0; i < modifiers.Count; i++)
                {
                    GameplayAttribute attribute = i < effect.Spec.TargetAttributes.Length
                        ? effect.Spec.TargetAttributes[i]
                        : null;
                    attribute ??= GetAttribute(modifiers[i].AttributeName);
                    if (attribute != null && !HasEarlierModifierForAttribute(effect, attribute, i))
                    {
                        RemoveAffectingEffect(attribute, effect);
                    }
                }
            }
            catch (Exception exception)
            {
                CaptureCleanupFailure(ref cleanupFailure, exception);
            }

            try { effect.ReleaseRuntimeLease(); }
            catch (Exception exception) { CaptureCleanupFailure(ref cleanupFailure, exception); }
        }

        private void FinalizeDisposedState(ref Exception cleanupFailure)
        {
            try
            {
                activeEffects.Clear();
                activeEffectIndexByEffect.Clear();
                activeEffectByReconciliationId.Clear();
                stackingIndexByTarget.Clear();
                stackingIndexBySource.Clear();
                grantedTagIndexToEffects.Clear();
            }
            catch (Exception exception) { CaptureCleanupFailure(ref cleanupFailure, exception); }

            try
            {
                for (int i = 0; i < attributeSets.Count; i++)
                {
                    AttributeSet set = attributeSets[i];
                    if (set != null && ReferenceEquals(set.OwningAbilitySystemComponent, this))
                    {
                        set.OwningAbilitySystemComponent = null;
                    }
                }
                attributeSets.Clear();
                attributes.Clear();
                dirtyAttributes.Clear();
                dirtyOngoingEffectInhibition = false;
            }
            catch (Exception exception) { CaptureCleanupFailure(ref cleanupFailure, exception); }

            try
            {
                activatableAbilities.Clear();
                abilitySpecByHandle.Clear();
                abilitySpecIndexBySpec.Clear();
                ReturnAllGrantedAbilitySpecLists();
                tickingAbilities.Clear();
                tickingAbilityIndexBySpec.Clear();
                ReturnAllAbilityAppliedEffectLists();
                abilityAppliedEffectRemovalScratch.Clear();
                instantExecutionOutputScratch?.Clear();
                instantExecutionOutputScratch = null;
                instantRollbackSnapshotScratch?.Clear();
                instantRollbackSnapshotScratch = null;
                instantEffectExecutionInProgress = false;
            }
            catch (Exception exception) { CaptureCleanupFailure(ref cleanupFailure, exception); }

            try { looseTags.Clear(); }
            catch (Exception exception) { CaptureCleanupFailure(ref cleanupFailure, exception); }
            try { fromEffectsTags.Clear(); }
            catch (Exception exception) { CaptureCleanupFailure(ref cleanupFailure, exception); }
            try { combinedTags.Clear(); }
            catch (Exception exception) { CaptureCleanupFailure(ref cleanupFailure, exception); }

            try
            {
                eventDelegates.Clear();
                tagNewOrRemovedObservers.Clear();
                tagAnyCountObservers.Clear();
                predictedAttributeSnapshots.Clear();
                PredictionManager.Reset();
                coreSpecHandles?.Clear();
                coreActiveEffectHandles?.Clear();
                coreState?.Reset(coreEntity);
                ClearIdleRuntimeListPoolsInternal();
                ResetRuntimeListPoolStatisticsInternal();
                triggerEventAbilities.Clear();
                triggerTagAddedAbilities.Clear();
                triggerTagRemovedAbilities.Clear();
                ReplicationStateBuilder.ResetAll();
            }
            catch (Exception exception) { CaptureCleanupFailure(ref cleanupFailure, exception); }

            effectAppliedObservers.Clear();
            effectRemovedObservers.Clear();
            gameplayCueCommittedObservers.Clear();
            abilityActivatedObservers.Clear();
            abilityEndedObservers.Clear();
            abilityCommittedObservers.Clear();
            predictionWindowClosedObservers.Clear();
            stateDeltaResyncObservers.Clear();
            OwnerActor = null;
            AvatarActor = null;
            OwnerUnityObject = null;
            AvatarGameObject = null;
        }

        private bool HandleStacking(GameplayEffectSpec spec)
        {
            if (spec.Def.Stacking.Type == EGameplayEffectStackingType.None)
            {
                return false;
            }

            if (!ActiveEffectContainer.TryGetStackingEffect(spec, out var existingEffect))
            {
                return false;
            }

            // Found a stackable effect
            if (existingEffect.StackCount >= spec.Def.Stacking.Limit)
            {
                // UE5: Overflow --apply overflow effects when stack limit is reached.
                if (spec.Def.OverflowEffects.Count > 0)
                {
                    for (int i = 0; i < spec.Def.OverflowEffects.Count; i++)
                    {
                        var overflowSpec = GameplayEffectSpec.Create(spec.Def.OverflowEffects[i], spec.Source, spec.Level);
                        ApplyGameplayEffectSpecWithinTransaction(overflowSpec);
                    }
                }

                // UE5: bDenyOverflowApplication --skip duration refresh if denied.
                if (!spec.Def.DenyOverflowApplication)
                {
                    if (spec.Def.Stacking.DurationPolicy == EGameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication)
                    {
                        existingEffect.RefreshDurationAndPeriod();
                    }
                }
                GASLog.Debug(sb => sb.Append("Stacking limit for ").Append(spec.Def.Name).Append(" reached."));
            }
            else
            {
                existingEffect.OnStackApplied();
            }

            MarkActiveEffectsDirty();
            MarkAttributesDirtyFromEffect(existingEffect);
            return true;
        }
        private void RemoveEffectsWithTags(
            GameplayTagContainer tags,
            ActiveGameplayEffect excludedEffect = null)
        {
            RemoveActiveEffectsWithGrantedTags(tags, excludedEffect);
        }

        /// <summary>
        /// Adds all explicitly-granted tags of an effect to the grantedTagIndexToEffects dictionary.
        /// Enables O(1) tag lookup and then O(k) per-tag scan over only matching effects.
        /// </summary>
        private void UpdateGrantedTagIndex_Applied(ActiveGameplayEffect effect)
        {
            ActiveEffectContainer.TrackGrantedTags(effect);
        }

        /// <summary>
        /// Removes all explicitly-granted tags of an effect from the grantedTagIndexToEffects dictionary.
        /// </summary>
        private void UpdateGrantedTagIndex_Removed(ActiveGameplayEffect effect)
        {
            ActiveEffectContainer.UntrackGrantedTags(effect);
        }

        private static bool ContainsReference<T>(List<T> list, T item) where T : class
        {
            if (list == null)
                return false;

            for (int i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], item))
                    return true;
            }

            return false;
        }
        private void ApplyModifier(GameplayEffectSpec spec, GameplayAttribute attribute, ModifierInfo mod, long magnitudeRaw, bool isFromExecution)
        {
            if (attribute == null)
            {
                //  Zero-GC StringBuilder overload avoids string interpolation allocation in Release builds.
                GASLog.Warning(sb => sb.Append("ApplyModifier failed: Attribute '").Append(mod.AttributeName).Append("' not found on ASC."));
                return;
            }

            var targetAttributeSet = attribute.OwningSet;
            if (targetAttributeSet == null) return;

            // If the effect is duration-based AND this is not an explicit execution (like a periodic tick),
            // then it's a temporary modifier that affects the CurrentValue by dirtying the attribute.
            if (spec.Def.DurationPolicy != EDurationPolicy.Instant && !isFromExecution)
            {
                MarkAttributeDirty(attribute);
                return;
            }

            // Snapshot attribute BaseValue so the owning local transaction can restore it on rollback.
            if (currentPredictionKey.IsValid)
            {
                AddPredictedAttributeSnapshot(currentPredictionKey, attribute);
            }

            // Otherwise, this is a permanent modification.
            // The AttributeSet is SOLELY responsible for handling it via PostGameplayEffectExecute.
            // The ASC's job is just to deliver the data.
            var callbackData = new GameplayEffectModCallbackData(spec, mod, magnitudeRaw, this);
            targetAttributeSet.PostGameplayEffectExecute(callbackData);
        }
        private void MarkAttributesDirtyFromEffect(ActiveGameplayEffect activeEffect)
        {
            // Ensure the effect and its definition are valid.
            if (activeEffect?.Spec?.Def?.Modifiers == null) return;

            var targetAttributes = activeEffect.Spec.TargetAttributes;
            if (targetAttributes == null) return;

            for (int i = 0; i < targetAttributes.Length; i++)
            {
                var attribute = targetAttributes[i];
                if (attribute != null)
                {
                    MarkAttributeDirty(attribute);
                }
            }
        }

        private void MarkOngoingEffectInhibitionDirty()
        {
            dirtyOngoingEffectInhibition = true;
        }

        private void RefreshDirtyOngoingEffectInhibition()
        {
            dirtyOngoingEffectInhibition = false;
            activeEffectIterationDepth++;
            try
            {
                for (int i = 0; i < activeEffects.Count; i++)
                {
                    RefreshInhibitionState(activeEffects[i]);
                }
            }
        finally
        {
            activeEffectIterationDepth--;
            FlushDeferredTriggerActivations();
        }
        }

        private bool RefreshInhibitionState(ActiveGameplayEffect effect)
        {
            var def = effect?.Spec?.Def;
            if (def == null || def.OngoingTagRequirements.IsEmpty)
            {
                return false;
            }

            bool isInhibited = !MeetsTagRequirements(def.OngoingRequiredTagsSnapshot, def.OngoingForbiddenTagsSnapshot);
            if (isInhibited != effect.IsInhibited)
            {
                effect.IsInhibited = isInhibited;
                effect.NotifyInhibitionChanged(isInhibited);
            }

            return isInhibited;
        }

        private void MarkLiveAttributeDependentsDirty(GameplayAttribute changedAttribute)
        {
            if (changedAttribute == null || activeEffects.Count == 0)
            {
                return;
            }

            for (int i = 0; i < activeEffects.Count; i++)
            {
                var effect = activeEffects[i];
                var spec = effect?.Spec;
                var modifiers = spec?.Def?.Modifiers;
                if (modifiers == null)
                {
                    continue;
                }

                for (int m = 0; m < modifiers.Count; m++)
                {
                    var mod = modifiers[m];
                    if (!mod.DependsOnLiveAttribute(changedAttribute, spec))
                    {
                        continue;
                    }

                    GameplayAttribute targetAttribute = null;
                    if (m < spec.TargetAttributes.Length)
                    {
                        targetAttribute = spec.TargetAttributes[m];
                    }

                    if (targetAttribute == null)
                    {
                        targetAttribute = GetAttribute(mod.AttributeName);
                    }

                    if (targetAttribute != null && !ReferenceEquals(targetAttribute, changedAttribute))
                    {
                        MarkAttributeDirty(targetAttribute);
                    }
                }
            }
        }

        private ulong stateVersion { get => ReplicationStateBuilder.StateVersion; set => ReplicationStateBuilder.StateVersion = value; }
        private ulong lastReplicatedStateVersion { get => ReplicationStateBuilder.LastReplicatedStateVersion; set => ReplicationStateBuilder.LastReplicatedStateVersion = value; }
        private uint outgoingDeltaSequence { get => ReplicationStateBuilder.OutgoingDeltaSequence; set => ReplicationStateBuilder.OutgoingDeltaSequence = value; }
        private uint attributeRegistryVersion { get => ReplicationStateBuilder.AttributeRegistryVersion; set => ReplicationStateBuilder.AttributeRegistryVersion = value; }
        private HashSet<string> dirtyAttributeNames => ReplicationStateBuilder.DirtyAttributeNames;
        private List<GameplayAttribute> dirtyAttributeValueSnapshots => ReplicationStateBuilder.DirtyAttributeValueSnapshots;
        private HashSet<GameplayTag> pendingAddedTags => ReplicationStateBuilder.PendingAddedTags;
        private HashSet<GameplayTag> pendingRemovedTags => ReplicationStateBuilder.PendingRemovedTags;
        private List<GameplayTag> pendingAddedTagSnapshots => ReplicationStateBuilder.PendingAddedTagSnapshots;
        private List<GameplayTag> pendingRemovedTagSnapshots => ReplicationStateBuilder.PendingRemovedTagSnapshots;
        // Process-local reconciliation IDs of effects removed since the last captured delta.
        private List<int> pendingRemovedEffectReconciliationIds => ReplicationStateBuilder.PendingRemovedEffectReconciliationIds;
        // Process-local spec handles removed since the last captured delta.
        private List<int> pendingRemovedAbilitySpecHandles => ReplicationStateBuilder.PendingRemovedAbilitySpecHandles;
        private bool grantedAbilitiesDirty { get => ReplicationStateBuilder.GrantedAbilitiesDirty; set => ReplicationStateBuilder.GrantedAbilitiesDirty = value; }
        private bool activeEffectsDirty { get => ReplicationStateBuilder.ActiveEffectsDirty; set => ReplicationStateBuilder.ActiveEffectsDirty = value; }
        private bool attributeStructureDirty { get => ReplicationStateBuilder.AttributeStructureDirty; set => ReplicationStateBuilder.AttributeStructureDirty = value; }
        private bool tagsDirty { get => ReplicationStateBuilder.TagsDirty; set => ReplicationStateBuilder.TagsDirty = value; }

        public ulong StateVersion
        {
            get
            {
                AssertRuntimeThread();
                return stateVersion;
            }
        }

        public uint AttributeRegistryVersion
        {
            get
            {
                AssertRuntimeThread();
                return attributeRegistryVersion;
            }
        }

        public AbilitySystemStateChangeMask PendingStateChangeMask
        {
            get
            {
                AssertRuntimeThread();
                return ReplicationStateBuilder.PendingMask;
            }
        }

        public bool IsAttributeStructureDirty
        {
            get
            {
                AssertRuntimeThread();
                return attributeStructureDirty;
            }
        }

        public GASReadOnlySetView<string> DirtyAttributeNames
        {
            get
            {
                AssertRuntimeThread();
                return dirtyAttributeNamesView;
            }
        }

        public GASReadOnlyListView<GameplayAttribute> DirtyAttributeValueSnapshots
        {
            get
            {
                AssertRuntimeThread();
                return dirtyAttributeValueSnapshotsView;
            }
        }

        public GASReadOnlySetView<GameplayTag> PendingAddedTags
        {
            get
            {
                AssertRuntimeThread();
                return pendingAddedTagsView;
            }
        }

        public GASReadOnlySetView<GameplayTag> PendingRemovedTags
        {
            get
            {
                AssertRuntimeThread();
                return pendingRemovedTagsView;
            }
        }

        public void CapturePendingStateDeltaNonAlloc(GASAbilitySystemStateDeltaBuffer buffer)
        {
            PreparePendingStateDeltaNonAlloc(buffer);
            if (buffer != null && !CommitPreparedStateDelta(buffer))
            {
                throw new InvalidOperationException("State changed before the captured delta could be committed.");
            }
        }

        public void PreparePendingStateDeltaNonAlloc(GASAbilitySystemStateDeltaBuffer buffer)
        {
            AssertRuntimeThread();
            if (buffer == null)
            {
                return;
            }

            ReplicationStateBuilder.BeginCapture(buffer);
            ulong capturedStateVersion = stateVersion;

            if (grantedAbilitiesDirty)
            {
                buffer.ChangeMask |= AbilitySystemStateChangeMask.GrantedAbilities;
                var granted = buffer.EnsureGrantedAbilityCapacity(activatableAbilities.Count);
                buffer.GrantedAbilityCount = FillGrantedAbilities(granted);

                if (pendingRemovedAbilitySpecHandles.Count > 0)
                {
                    int[] removed = buffer.EnsureRemovedAbilitySpecHandleCapacity(pendingRemovedAbilitySpecHandles.Count);
                    for (int i = 0; i < pendingRemovedAbilitySpecHandles.Count; i++)
                    {
                        removed[i] = pendingRemovedAbilitySpecHandles[i];
                    }

                    buffer.RemovedAbilitySpecHandleCount = pendingRemovedAbilitySpecHandles.Count;
                }
            }

            if (activeEffectsDirty)
            {
                buffer.ChangeMask |= AbilitySystemStateChangeMask.ActiveEffects;
                var effects = buffer.EnsureActiveEffectCapacity(activeEffects.Count);
                buffer.ActiveEffectCount = FillActiveEffects(buffer, effects);
            }

            if (pendingRemovedEffectReconciliationIds.Count > 0)
            {
                buffer.ChangeMask |= AbilitySystemStateChangeMask.ActiveEffects;
                var removed = buffer.EnsureRemovedEffectReconciliationIdCapacity(pendingRemovedEffectReconciliationIds.Count);
                for (int i = 0; i < pendingRemovedEffectReconciliationIds.Count; i++)
                {
                    removed[i] = pendingRemovedEffectReconciliationIds[i];
                }

                buffer.RemovedEffectReconciliationIdCount = pendingRemovedEffectReconciliationIds.Count;
            }

            if (attributeStructureDirty || dirtyAttributeNames.Count > 0)
            {
                buffer.ChangeMask |= AbilitySystemStateChangeMask.Attributes;
                buffer.AttributeCount = attributeStructureDirty
                    ? FillAttributes(buffer.EnsureAttributeCapacity(CountAttributes()))
                : FillDirtyAttributes(buffer.EnsureAttributeCapacity(dirtyAttributeValueSnapshots.Count));
            }

            if (tagsDirty)
            {
                buffer.ChangeMask |= AbilitySystemStateChangeMask.Tags;
                buffer.AddedTagCount = CopyTagsNonAlloc(pendingAddedTagSnapshots, buffer.EnsureAddedTagCapacity(pendingAddedTagSnapshots.Count));
                buffer.RemovedTagCount = CopyTagsNonAlloc(pendingRemovedTagSnapshots, buffer.EnsureRemovedTagCapacity(pendingRemovedTagSnapshots.Count));
            }

            ulong stateChecksum = ComputeReplicatedStateChecksum();
            if (capturedStateVersion != stateVersion)
            {
                throw new InvalidOperationException("Authoritative GAS state changed while a state delta was being prepared.");
            }

            ReplicationStateBuilder.FinalizeCapture(buffer, stateChecksum);
        }

        public bool CommitPreparedStateDelta(GASAbilitySystemStateDeltaBuffer buffer)
        {
            AssertRuntimeThread();
            return ReplicationStateBuilder.CommitCapture(buffer);
        }

        private int FillGrantedAbilities(GASGrantedAbilityStateData[] entries)
        {
            for (int i = 0; i < activatableAbilities.Count; i++)
            {
                var spec = activatableAbilities[i];
                var ability = spec.AbilityCDO ?? spec.Ability;
                entries[i] = new GASGrantedAbilityStateData(
                    spec.Handle,
                    ability,
                    spec.Level,
                    spec.IsActive,
                    spec.IsInputPressed,
                    spec.GrantingEffect?.ReconciliationId ?? 0);
            }

            return activatableAbilities.Count;
        }

        private int FillActiveEffects(
            GASAbilitySystemStateDeltaBuffer buffer,
            GASActiveEffectStateData[] entries)
        {
            for (int i = 0; i < activeEffects.Count; i++)
            {
                var effect = activeEffects[i];
                int setByCallerCount = effect.Spec.SetByCallerTagMagnitudeCount;
                var setByCallerEntries = setByCallerCount > 0
                    ? buffer.EnsureActiveEffectSetByCallerCapacity(i, setByCallerCount)
                    : Array.Empty<GASSetByCallerTagStateData>();
                if (setByCallerCount > 0)
                {
                    setByCallerCount = effect.Spec.CopySetByCallerTagStateData(setByCallerEntries);
                }

                int setByCallerNameCount = effect.Spec.SetByCallerNameMagnitudeCount;
                var setByCallerNameEntries = setByCallerNameCount > 0
                    ? buffer.EnsureActiveEffectSetByCallerNameCapacity(i, setByCallerNameCount)
                    : Array.Empty<GASSetByCallerNameStateData>();
                if (setByCallerNameCount > 0)
                {
                    setByCallerNameCount = effect.Spec.CopySetByCallerNameStateData(setByCallerNameEntries);
                }

                int dynamicGrantedTagCount = effect.Spec.DynamicGrantedTags.ExplicitTagCount;
                GameplayTag[] dynamicGrantedTags = dynamicGrantedTagCount > 0
                    ? buffer.EnsureActiveEffectDynamicGrantedTagCapacity(i, dynamicGrantedTagCount)
                    : Array.Empty<GameplayTag>();
                dynamicGrantedTagCount = CopyExplicitTags(effect.Spec.DynamicGrantedTags, dynamicGrantedTags);

                int dynamicAssetTagCount = effect.Spec.DynamicAssetTags.ExplicitTagCount;
                GameplayTag[] dynamicAssetTags = dynamicAssetTagCount > 0
                    ? buffer.EnsureActiveEffectDynamicAssetTagCapacity(i, dynamicAssetTagCount)
                    : Array.Empty<GameplayTag>();
                dynamicAssetTagCount = CopyExplicitTags(effect.Spec.DynamicAssetTags, dynamicAssetTags);

                int reconciliationId = effect.ReconciliationId;
                if (reconciliationId <= 0)
                {
                    throw new InvalidOperationException(
                        $"Active GameplayEffect '{effect.Spec.Def.Name}' must be assigned a positive reconciliation ID before state-delta capture.");
                }

                entries[i] = GASActiveEffectStateData.FromRaw(
                    reconciliationId,
                    effect.Spec.Def,
                    effect.Spec.Source,
                    effect.SourceAbilitySpecHandle,
                    effect.Spec.Level,
                    effect.StackCount,
                    effect.IsInhibited,
                    effect.Spec.DurationRaw,
                    effect.TimeRemainingRaw,
                    effect.PeriodTimeRemainingRaw,
                    effect.Spec.Context?.PredictionKey ?? default,
                    setByCallerCount > 0 ? setByCallerEntries : Array.Empty<GASSetByCallerTagStateData>(),
                    setByCallerCount,
                    setByCallerNameCount > 0 ? setByCallerNameEntries : Array.Empty<GASSetByCallerNameStateData>(),
                    setByCallerNameCount,
                    dynamicGrantedTags,
                    dynamicGrantedTagCount,
                    dynamicAssetTags,
                    dynamicAssetTagCount);
            }

            return activeEffects.Count;
        }

        private int FillActiveEffects(
            GASAbilitySystemFullStateBuffer buffer,
            GASActiveEffectStateData[] entries)
        {
            for (int i = 0; i < activeEffects.Count; i++)
            {
                ActiveGameplayEffect effect = activeEffects[i];
                int setByCallerCount = effect.Spec.SetByCallerTagMagnitudeCount;
                GASSetByCallerTagStateData[] setByCallerEntries = setByCallerCount > 0
                    ? buffer.EnsureActiveEffectSetByCallerCapacity(i, setByCallerCount)
                    : Array.Empty<GASSetByCallerTagStateData>();
                if (setByCallerCount > 0)
                {
                    setByCallerCount = effect.Spec.CopySetByCallerTagStateData(setByCallerEntries);
                }

                int setByCallerNameCount = effect.Spec.SetByCallerNameMagnitudeCount;
                GASSetByCallerNameStateData[] setByCallerNameEntries = setByCallerNameCount > 0
                    ? buffer.EnsureActiveEffectSetByCallerNameCapacity(i, setByCallerNameCount)
                    : Array.Empty<GASSetByCallerNameStateData>();
                if (setByCallerNameCount > 0)
                {
                    setByCallerNameCount = effect.Spec.CopySetByCallerNameStateData(setByCallerNameEntries);
                }

                int dynamicGrantedTagCount = effect.Spec.DynamicGrantedTags.ExplicitTagCount;
                GameplayTag[] dynamicGrantedTags = dynamicGrantedTagCount > 0
                    ? buffer.EnsureActiveEffectDynamicGrantedTagCapacity(i, dynamicGrantedTagCount)
                    : Array.Empty<GameplayTag>();
                dynamicGrantedTagCount = CopyExplicitTags(effect.Spec.DynamicGrantedTags, dynamicGrantedTags);

                int dynamicAssetTagCount = effect.Spec.DynamicAssetTags.ExplicitTagCount;
                GameplayTag[] dynamicAssetTags = dynamicAssetTagCount > 0
                    ? buffer.EnsureActiveEffectDynamicAssetTagCapacity(i, dynamicAssetTagCount)
                    : Array.Empty<GameplayTag>();
                dynamicAssetTagCount = CopyExplicitTags(effect.Spec.DynamicAssetTags, dynamicAssetTags);

                int reconciliationId = effect.ReconciliationId;
                if (reconciliationId <= 0)
                {
                    throw new InvalidOperationException(
                        $"Active GameplayEffect '{effect.Spec.Def.Name}' has no process-local reconciliation ID.");
                }

                entries[i] = GASActiveEffectStateData.FromRaw(
                    reconciliationId,
                    effect.Spec.Def,
                    effect.Spec.Source,
                    effect.SourceAbilitySpecHandle,
                    effect.Spec.Level,
                    effect.StackCount,
                    effect.IsInhibited,
                    effect.Spec.DurationRaw,
                    effect.TimeRemainingRaw,
                    effect.PeriodTimeRemainingRaw,
                    effect.Spec.Context?.PredictionKey ?? default,
                    setByCallerEntries,
                    setByCallerCount,
                    setByCallerNameEntries,
                    setByCallerNameCount,
                    dynamicGrantedTags,
                    dynamicGrantedTagCount,
                    dynamicAssetTags,
                    dynamicAssetTagCount);
            }

            return activeEffects.Count;
        }

        private int CountAttributes()
        {
            int attributeCount = 0;
            for (int setIndex = 0; setIndex < attributeSets.Count; setIndex++)
            {
                attributeCount += attributeSets[setIndex].GetAttributes().Count;
            }

            return attributeCount;
        }

        private int FillAttributes(GASAttributeStateData[] entries)
        {
            int index = 0;
            for (int setIndex = 0; setIndex < attributeSets.Count; setIndex++)
            {
                foreach (var attr in attributeSets[setIndex].GetAttributes())
                {
                    entries[index++] = GASAttributeStateData.FromRaw(attr.Name, attr.BaseValueRaw, attr.CurrentValueRaw);
                }
            }

            return index;
        }

        private int FillDirtyAttributes(GASAttributeStateData[] entries)
        {
            int index = 0;
            for (int i = 0; i < dirtyAttributeValueSnapshots.Count; i++)
            {
                var attribute = dirtyAttributeValueSnapshots[i];
                if (attribute != null &&
                    attributes.TryGetValue(attribute.Name, out var registeredAttribute) &&
                    ReferenceEquals(registeredAttribute, attribute))
                {
                    entries[index++] = GASAttributeStateData.FromRaw(attribute.Name, attribute.BaseValueRaw, attribute.CurrentValueRaw);
                }
            }

            return index;
        }

        private static int CopyTagsNonAlloc(List<GameplayTag> tags, GameplayTag[] entries)
        {
            int count = tags.Count;
            for (int i = 0; i < count; i++)
            {
                entries[i] = tags[i];
            }

            return count;
        }

        private int FillLooseTagCounts(GASTagCountStateData[] entries)
        {
            int index = 0;
            GameplayTagEnumerator enumerator = looseTags.GetExplicitTags();
            while (enumerator.MoveNext())
            {
                GameplayTag tag = enumerator.Current;
                int explicitCount = looseTags.GetExplicitTagCount(tag);
                if (explicitCount > 0)
                {
                    entries[index++] = new GASTagCountStateData(tag, explicitCount);
                }
            }

            return index;
        }

        private void ClearPendingStateChanges()
        {
            ReplicationStateBuilder.ClearPendingStateChanges();
        }

        public void ConsumePendingStateChanges()
        {
            AssertRuntimeThread();
            ClearPendingStateChanges();
        }

        internal ReplicationStateBuilder.MutationScope BeginReplicationMutationScope(
            bool attributeStructure = false)
        {
            AssertRuntimeThread();
            if (SuppressOutboundReplicationTracking || disposing || disposed)
            {
                return default;
            }

            return ReplicationStateBuilder.BeginMutationScope(attributeStructure);
        }

        private void MarkGrantedAbilitiesDirty()
        {
            if (SuppressOutboundReplicationTracking || disposing || disposed) return;
            EnsureReplicationMutationScopeActive("Granted ability mutation");
            ReplicationStateBuilder.MarkGrantedAbilitiesDirty();
        }

        private void MarkActiveEffectsDirty()
        {
            if (SuppressOutboundReplicationTracking || disposing || disposed) return;
            EnsureReplicationMutationScopeActive("Active GameplayEffect mutation");
            ReplicationStateBuilder.MarkActiveEffectsDirty();
        }

        private void MarkAttributeValueDirty(GameplayAttribute attribute)
        {
            if (SuppressOutboundReplicationTracking || disposing || disposed) return;
            EnsureReplicationMutationScopeActive("GameplayAttribute value mutation");
            ReplicationStateBuilder.MarkAttributeValueDirty(attribute);
        }

        private void MarkAttributeStructureDirty()
        {
            if (SuppressOutboundReplicationTracking || disposing || disposed) return;
            EnsureReplicationMutationScopeActive("AttributeSet structure mutation");
            ReplicationStateBuilder.MarkAttributeStructureDirty();
        }

        private void TrackLooseTagCountChange(GameplayTag tag, int newCount)
        {
            if (SuppressOutboundReplicationTracking || disposing || disposed) return;
            EnsureReplicationMutationScopeActive("Loose GameplayTag mutation");
            ReplicationStateBuilder.TrackTagCountChange(tag, newCount);
        }

        private void HandleCombinedTagCountChange(GameplayTag tag, int newCount)
        {
            Exception authorityFailure = null;
            try
            {
                // A tag edge may change OngoingTagRequirements. Refresh effect-level inhibition
                // before ticking periodic effects, then dirty affected attributes for aggregation.
                MarkOngoingEffectInhibitionDirty();
                MarkAttributesDirtyForEffectsWithOngoingRequirements();
            }
            catch (Exception exception)
            {
                CaptureCleanupFailure(ref authorityFailure, exception);
            }

            try
            {
                OnTriggerTagChanged(tag, newCount);
            }
            catch (Exception exception)
            {
                CaptureCleanupFailure(ref authorityFailure, exception);
            }

            DispatchTagObservers(tagNewOrRemovedObservers, tag, newCount, "NewOrRemoved GameplayTag");
            if (authorityFailure != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(authorityFailure).Throw();
            }
        }

        private void HandleCombinedAnyTagCountChange(GameplayTag tag, int newCount)
        {
            DispatchTagObservers(tagAnyCountObservers, tag, newCount, "AnyCountChange GameplayTag");
        }

        private void DispatchTagObservers(
            Dictionary<GameplayTag, GASCallbackList<OnTagCountChangedDelegate>> observerMap,
            GameplayTag tag,
            int newCount,
            string observerName)
        {
            if (!observerMap.TryGetValue(tag, out GASCallbackList<OnTagCountChangedDelegate> callbacks))
            {
                return;
            }

            EnterRuntimeCallbackDispatch();
            bool callbackListDispatchStarted = false;
            try
            {
                int count = callbacks.BeginDispatch();
                callbackListDispatchStarted = true;
                for (int i = 0; i < count; i++)
                {
                    OnTagCountChangedDelegate callback = callbacks.GetCallback(i);
                    if (callback == null)
                    {
                        continue;
                    }

                    try
                    {
                        callback.Invoke(tag, newCount);
                    }
                    catch (Exception exception)
                    {
                        GASLog.Error($"{observerName} observer failed after the tag state was committed: {exception.Message}");
                    }
                }
            }
            finally
            {
                try
                {
                    if (callbackListDispatchStarted)
                    {
                        callbacks.EndDispatch();
                    }
                }
                finally
                {
                    ExitRuntimeCallbackDispatch();
                }
            }
        }

        private void TrackRemovedEffectReconciliationId(int reconciliationId)
        {
            if (SuppressOutboundReplicationTracking || disposing || disposed) return;
            EnsureReplicationMutationScopeActive("Removed GameplayEffect tracking");
            ReplicationStateBuilder.TrackRemovedEffectReconciliationId(reconciliationId);
        }

        private void TrackRemovedAbilitySpecHandle(int specHandle)
        {
            if (SuppressOutboundReplicationTracking || disposing || disposed) return;
            EnsureReplicationMutationScopeActive("Removed GameplayAbility tracking");
            ReplicationStateBuilder.TrackRemovedAbilitySpecHandle(specHandle);
        }

        private void EnsureReplicationMutationScopeActive(string operation)
        {
            if (ReplicationStateBuilder.MutationScopeDepth <= 0)
            {
                throw new InvalidOperationException(
                    $"{operation} must execute inside a replication mutation scope so version reservation precedes authoritative state changes.");
            }
        }

        private bool SuppressOutboundReplicationTracking => reconciliationApplyScopeDepth > 0;

        private void ThrowIfActiveEffectMutationLocked(string operation)
        {
            if (effectMutationTransactionDepth != 0 || activeEffectIterationDepth != 0)
            {
                throw new InvalidOperationException(
                    $"{operation} cannot re-enter active-effect mutation or iteration. Queue the operation for a later owner-thread phase.");
            }
        }

        internal ReplicationStateBuilder.MutationScope BeginAbilityEndMutationScope(
            GameplayAbility ability)
        {
            if (disposing)
            {
                RuntimeContext.AssertOwnerThread();
                if (abilityEndMutationBypassDepth == 0)
                {
                    throw new ObjectDisposedException(nameof(AbilitySystemComponent));
                }

                return default;
            }

            AssertRuntimeThread();
            if (abilityEndMutationBypassDepth == 0)
            {
                ThrowIfActiveEffectMutationLocked("Ability end");
            }

            GameplayAbilitySpec spec = ability?.Spec;
            if (spec == null || !spec.IsLocallyExecuting)
            {
                return default;
            }

            return BeginReplicationMutationScope();
        }

        private void AssertRuntimeThreadForCleanupRemoval()
        {
            if (!disposing)
            {
                AssertRuntimeThread();
                return;
            }

            RuntimeContext.AssertOwnerThread();
        }

        internal void AssertRuntimeSubscriptionAccess()
        {
            AssertRuntimeThread();
        }

        internal void AssertRuntimeSubscriptionRemovalAccess()
        {
            AssertRuntimeThreadForCleanupRemoval();
        }

        private void MarkAttributesDirtyForEffectsWithOngoingRequirements()
        {
            for (int i = 0; i < activeEffects.Count; i++)
            {
                var effect = activeEffects[i];
                if (!effect.Spec.Def.OngoingTagRequirements.IsEmpty)
                    MarkAttributesDirtyFromEffect(effect);
            }
        }

        // ---- Authoritative State Apply ----

        /// <summary>
        /// Forces attribute <see cref="GameplayAttribute.BaseValue"/> and
        /// <see cref="GameplayAttribute.CurrentValue"/> from a server-authoritative snapshot.
        /// Only attributes already registered with this ASC are updated; unknown names are silently ignored.
        /// </summary>
        public void ApplyAuthorityAttributeStateData(GASAttributeStateData[] snapshot)
        {
            AssertRuntimeThread();
            if (snapshot == null) return;
            ApplyAuthorityAttributeStateData(snapshot, snapshot.Length);
        }

        public void ApplyAuthorityAttributeStateData(GASAttributeStateData[] snapshot, int count)
        {
            AssertRuntimeThread();
            if (snapshot == null) return;
            if (count < 0 || count > snapshot.Length || count > Limits.MaxAttributes)
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, "Authoritative attribute count is outside the provided buffer or runtime limit.");
            }
            int safeCount = Math.Min(count, snapshot.Length);
            bool hasKnownAttribute = false;
            for (int i = 0; i < safeCount; i++)
            {
                ref readonly var entry = ref snapshot[i];
                if (attributes.ContainsKey(entry.AttributeName))
                {
                    hasKnownAttribute = true;
                    break;
                }
            }
            if (!hasKnownAttribute)
            {
                return;
            }

            using (BeginReplicationMutationScope())
            {
                for (int i = 0; i < safeCount; i++)
                {
                    ref readonly var entry = ref snapshot[i];
                    if (!attributes.TryGetValue(entry.AttributeName, out var attr)) continue;
                    attr.OwningSet.SetBaseValueRaw(attr, entry.BaseValueRaw);
                    attr.OwningSet.SetCurrentValueRaw(attr, entry.CurrentValueRaw);
                }
            }
        }

        public void ApplyStateDelta(GASAbilitySystemStateDeltaBuffer delta)
        {
            if (!TryApplyStateDelta(delta, out GASStateDeltaRejectionReason rejectionReason) &&
                rejectionReason != GASStateDeltaRejectionReason.None)
            {
                GASLog.Warning(sb => sb.Append("Rejected GAS state delta: ").Append(rejectionReason).Append('.'));
            }
        }

        public bool TryApplyStateDelta(
            GASAbilitySystemStateDeltaBuffer delta,
            out GASStateDeltaRejectionReason rejectionReason)
        {
            AssertRuntimeThread();
            if (effectMutationTransactionDepth != 0 || activeEffectIterationDepth != 0)
            {
                rejectionReason = GASStateDeltaRejectionReason.ApplicationFailed;
                return false;
            }
            if (!ValidateStateDelta(delta, out rejectionReason))
            {
                return false;
            }

            if (!delta.HasChanges)
            {
                return true;
            }

            try
            {
                using (new ReconciliationApplyScope(this))
                {
                    RollbackAllOpenPredictionWindows();

                    if ((delta.ChangeMask & AbilitySystemStateChangeMask.Attributes) != 0 &&
                        delta.AttributeCount > 0)
                    {
                        ApplyAuthorityAttributeStateData(delta.Attributes, delta.AttributeCount);
                    }

                    if ((delta.ChangeMask & AbilitySystemStateChangeMask.ActiveEffects) != 0)
                    {
                        for (int i = 0; i < delta.RemovedEffectReconciliationIdCount; i++)
                        {
                            int reconciliationId = delta.RemovedEffectReconciliationIds[i];
                            if (reconciliationId == 0) continue;
                            RemoveActiveEffectByReconciliationId(reconciliationId);
                        }

                        for (int i = 0; i < delta.ActiveEffectCount; i++)
                        {
                            ref readonly GASActiveEffectStateData state = ref delta.ActiveEffects[i];
                            var definition = (GameplayEffect)state.EffectDefinition;
                            var source = state.SourceComponent as AbilitySystemComponent;
                            if (!ApplyActiveEffectState(in state, definition, source))
                            {
                                throw new InvalidOperationException($"Active effect state {state.ReconciliationId} could not be applied.");
                            }
                        }
                    }

                    if ((delta.ChangeMask & AbilitySystemStateChangeMask.Tags) != 0)
                    {
                        ApplyAddedTags(delta.AddedTags, delta.AddedTagCount);
                        ApplyRemovedTags(delta.RemovedTags, delta.RemovedTagCount);
                    }

                    if ((delta.ChangeMask & AbilitySystemStateChangeMask.GrantedAbilities) != 0)
                    {
                        ApplyRemovedAbilities(delta.RemovedAbilitySpecHandles, delta.RemovedAbilitySpecHandleCount);
                        ApplyGrantedAbilityReplacement(delta.GrantedAbilities, delta.GrantedAbilityCount);
                    }

                    if (dirtyAttributes.Count > 0)
                    {
                        RecalculateDirtyAttributes();
                    }
                }
            }
            catch (Exception exception)
            {
                GASLog.Warning(sb => sb.Append("GAS state delta application failed and requires a full-state resync: ").Append(exception.Message));
                RequireStateDeltaResync(GASStateDeltaRejectionReason.ApplicationFailed);
                rejectionReason = GASStateDeltaRejectionReason.ApplicationFailed;
                return false;
            }

            if (ComputeReplicatedStateChecksum() != delta.StateChecksum)
            {
                RequireStateDeltaResync(GASStateDeltaRejectionReason.ChecksumMismatch);
                rejectionReason = GASStateDeltaRejectionReason.ChecksumMismatch;
                return false;
            }

            lastAppliedDeltaSequence = delta.Sequence;
            lastAppliedDeltaVersion = delta.CurrentVersion;
            hasAppliedDeltaSequence = true;

            rejectionReason = GASStateDeltaRejectionReason.None;
            return true;
        }

        public void ResetStateDeltaBaseline(uint sequence, ulong version, ulong expectedStateChecksum)
        {
            AssertRuntimeThread();
            if (sequence == 0u)
            {
                throw new ArgumentOutOfRangeException(nameof(sequence), sequence, "A full-state baseline sequence must be non-zero.");
            }
            if (expectedStateChecksum == 0UL)
            {
                throw new ArgumentOutOfRangeException(nameof(expectedStateChecksum), expectedStateChecksum, "A full-state baseline checksum must be non-zero.");
            }
            ulong currentChecksum = ComputeReplicatedStateChecksum();
            if (currentChecksum != expectedStateChecksum)
            {
                throw new InvalidOperationException(
                    $"The full-state baseline checksum does not match the current ASC state. Expected {expectedStateChecksum}, actual {currentChecksum}.");
            }

            lastAppliedDeltaSequence = sequence;
            lastAppliedDeltaVersion = version;
            hasAppliedDeltaSequence = true;
            stateDeltaResyncRequired = false;
        }

        private bool ValidateStateDelta(
            GASAbilitySystemStateDeltaBuffer delta,
            out GASStateDeltaRejectionReason rejectionReason)
        {
            if (delta == null)
            {
                rejectionReason = GASStateDeltaRejectionReason.MissingDelta;
                return false;
            }

            if (delta.SchemaVersion != GASRuntimeDataContract.ReconciliationSchemaVersion)
            {
                rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                return false;
            }

            const AbilitySystemStateChangeMask knownMask =
                AbilitySystemStateChangeMask.GrantedAbilities |
                AbilitySystemStateChangeMask.ActiveEffects |
                AbilitySystemStateChangeMask.Attributes |
                AbilitySystemStateChangeMask.Tags;
            if ((delta.ChangeMask & ~knownMask) != 0)
            {
                rejectionReason = GASStateDeltaRejectionReason.UnsupportedChangeMask;
                return false;
            }

            if (!delta.HasChanges)
            {
                rejectionReason = GASStateDeltaRejectionReason.None;
                return true;
            }

            if (stateDeltaResyncRequired)
            {
                rejectionReason = GASStateDeltaRejectionReason.ResyncRequired;
                return false;
            }

            if (delta.Sequence == 0u)
            {
                rejectionReason = GASStateDeltaRejectionReason.InvalidSequence;
                return false;
            }

            if (hasAppliedDeltaSequence && !IsNewerSequence(delta.Sequence, lastAppliedDeltaSequence))
            {
                rejectionReason = GASStateDeltaRejectionReason.StaleOrReplayedSequence;
                return false;
            }

            if (delta.CurrentVersion <= delta.BaseVersion)
            {
                rejectionReason = GASStateDeltaRejectionReason.InvalidVersionRange;
                return false;
            }

            if (delta.StateChecksum == 0UL)
            {
                rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                return false;
            }

            if ((!hasAppliedDeltaSequence && delta.BaseVersion != 0UL) ||
                (hasAppliedDeltaSequence && delta.BaseVersion != lastAppliedDeltaVersion))
            {
                RequireStateDeltaResync(GASStateDeltaRejectionReason.BaselineMismatch);
                rejectionReason = GASStateDeltaRejectionReason.BaselineMismatch;
                return false;
            }

            if (!IsValidCount(delta.GrantedAbilityCount, delta.GrantedAbilities) ||
                !IsValidCount(delta.RemovedAbilitySpecHandleCount, delta.RemovedAbilitySpecHandles) ||
                !IsValidCount(delta.ActiveEffectCount, delta.ActiveEffects) ||
                !IsValidCount(delta.RemovedEffectReconciliationIdCount, delta.RemovedEffectReconciliationIds) ||
                !IsValidCount(delta.AttributeCount, delta.Attributes) ||
                !IsValidCount(delta.AddedTagCount, delta.AddedTags) ||
                !IsValidCount(delta.RemovedTagCount, delta.RemovedTags))
            {
                rejectionReason = GASStateDeltaRejectionReason.InvalidCounts;
                return false;
            }

            if (((delta.ChangeMask & AbilitySystemStateChangeMask.GrantedAbilities) == 0 &&
                 (delta.GrantedAbilityCount != 0 || delta.RemovedAbilitySpecHandleCount != 0)) ||
                ((delta.ChangeMask & AbilitySystemStateChangeMask.ActiveEffects) == 0 &&
                 (delta.ActiveEffectCount != 0 || delta.RemovedEffectReconciliationIdCount != 0)) ||
                ((delta.ChangeMask & AbilitySystemStateChangeMask.Attributes) == 0 &&
                 delta.AttributeCount != 0) ||
                ((delta.ChangeMask & AbilitySystemStateChangeMask.Tags) == 0 &&
                 (delta.AddedTagCount != 0 || delta.RemovedTagCount != 0)))
            {
                rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                return false;
            }

            if (delta.GrantedAbilityCount > Limits.MaxGrantedAbilities ||
                delta.RemovedAbilitySpecHandleCount > Limits.MaxGrantedAbilities ||
                delta.ActiveEffectCount > Limits.MaxActiveEffects ||
                delta.RemovedEffectReconciliationIdCount > Limits.MaxActiveEffects ||
                delta.AttributeCount > Limits.MaxAttributes ||
                delta.AddedTagCount > Limits.MaxTagChangesPerDelta ||
                delta.RemovedTagCount > Limits.MaxTagChangesPerDelta)
            {
                rejectionReason = GASStateDeltaRejectionReason.CapacityExceeded;
                return false;
            }

            for (int i = 0; i < delta.GrantedAbilityCount; i++)
            {
                ref readonly GASGrantedAbilityStateData ability = ref delta.GrantedAbilities[i];
                if (ability.SpecHandle <= 0 ||
                    ability.GrantingEffectReconciliationId < 0 ||
                    ability.Level <= 0 ||
                    ability.Level > GASRuntimeDataContract.MaxGameplayLevel ||
                    !(ability.AbilityDefinition is GameplayAbility))
                {
                    rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                    return false;
                }
            }

            for (int i = 0; i < delta.RemovedAbilitySpecHandleCount; i++)
            {
                if (delta.RemovedAbilitySpecHandles[i] <= 0)
                {
                    rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                    return false;
                }
            }

            for (int i = 0; i < delta.ActiveEffectCount; i++)
            {
                ref readonly GASActiveEffectStateData effect = ref delta.ActiveEffects[i];
                if (!(effect.EffectDefinition is GameplayEffect definition) ||
                    definition.DurationPolicy == EDurationPolicy.Instant ||
                    effect.ReconciliationId <= 0 ||
                    effect.SourceAbilitySpecHandle < 0 ||
                    (effect.SourceAbilitySpecHandle > 0 && effect.SourceComponent == null) ||
                    !IsCanonicalPredictionKey(effect.PredictionKey) ||
                    effect.Level <= 0 ||
                    effect.Level > GASRuntimeDataContract.MaxGameplayLevel ||
                    effect.StackCount <= 0 ||
                    effect.StackCount > (definition.Stacking.Type == EGameplayEffectStackingType.None ? 1 : definition.Stacking.Limit) ||
                    (definition.DurationPolicy == EDurationPolicy.HasDuration && effect.DurationRaw <= 0L) ||
                    effect.DurationRaw < 0L ||
                    effect.TimeRemainingRaw < 0L ||
                    (definition.Period > 0f ? effect.PeriodTimeRemainingRaw < 0L : effect.PeriodTimeRemainingRaw != -1L) ||
                    effect.SetByCallerTagMagnitudeCount < 0 ||
                    effect.SetByCallerNameMagnitudeCount < 0 ||
                    effect.SetByCallerTagMagnitudeCount > Limits.MaxSetByCallerEntries - effect.SetByCallerNameMagnitudeCount ||
                    (effect.SetByCallerTagMagnitudeCount > 0 &&
                     (effect.SetByCallerTagMagnitudes == null ||
                      effect.SetByCallerTagMagnitudes.Length < effect.SetByCallerTagMagnitudeCount)) ||
                    (effect.SetByCallerNameMagnitudeCount > 0 &&
                     (effect.SetByCallerNameMagnitudes == null ||
                      effect.SetByCallerNameMagnitudes.Length < effect.SetByCallerNameMagnitudeCount)) ||
                    !IsValidReplicatedTagArray(effect.DynamicGrantedTags, effect.DynamicGrantedTagCount) ||
                    !IsValidReplicatedTagArray(effect.DynamicAssetTags, effect.DynamicAssetTagCount) ||
                    effect.DynamicGrantedTagCount > GameplayEffect.MaxAggregateTagCount - effect.DynamicAssetTagCount)
                {
                    rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                    return false;
                }

                if (effect.SourceComponent != null &&
                    (!(effect.SourceComponent is AbilitySystemComponent source) ||
                     source.IsDisposed ||
                     !ReferenceEquals(source.RuntimeContext, RuntimeContext)))
                {
                    rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                    return false;
                }

                if (effect.SourceAbilitySpecHandle > 0)
                {
                    var sourceComponent = (AbilitySystemComponent)effect.SourceComponent;
                    GameplayAbilitySpec sourceSpec = sourceComponent.FindSpecByHandle(effect.SourceAbilitySpecHandle);
                    if (sourceSpec?.GetPrimaryInstance() == null)
                    {
                        rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                        return false;
                    }
                }

                stateDeltaTagValidationScratch.Clear();
                for (int j = 0; j < effect.SetByCallerTagMagnitudeCount; j++)
                {
                    GameplayTag tag = effect.SetByCallerTagMagnitudes[j].Tag;
                    if (tag.IsNone || !tag.IsValid || !stateDeltaTagValidationScratch.Add(tag))
                    {
                        rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                        return false;
                    }
                }

                stateDeltaTagValidationScratch.Clear();
                for (int j = 0; j < effect.DynamicGrantedTagCount; j++)
                {
                    if (!stateDeltaTagValidationScratch.Add(effect.DynamicGrantedTags[j]))
                    {
                        rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                        return false;
                    }
                }

                stateDeltaTagValidationScratch.Clear();
                for (int j = 0; j < effect.DynamicAssetTagCount; j++)
                {
                    if (!stateDeltaTagValidationScratch.Add(effect.DynamicAssetTags[j]))
                    {
                        rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                        return false;
                    }
                }

                stateDeltaNameValidationScratch.Clear();
                for (int j = 0; j < effect.SetByCallerNameMagnitudeCount; j++)
                {
                    string name = effect.SetByCallerNameMagnitudes[j].Name;
                    if (!IsValidReplicatedName(name) || !stateDeltaNameValidationScratch.Add(name))
                    {
                        rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                        return false;
                    }
                }
            }

            for (int i = 0; i < delta.RemovedEffectReconciliationIdCount; i++)
            {
                if (delta.RemovedEffectReconciliationIds[i] <= 0)
                {
                    rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                    return false;
                }
            }

            for (int i = 0; i < delta.AttributeCount; i++)
            {
                if (!IsValidReplicatedName(delta.Attributes[i].AttributeName))
                {
                    rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                    return false;
                }
            }

            if (!ValidateTags(delta.AddedTags, delta.AddedTagCount) ||
                !ValidateTags(delta.RemovedTags, delta.RemovedTagCount))
            {
                rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                return false;
            }

            if (!ValidateStateDeltaCrossReferences(delta, out rejectionReason))
            {
                return false;
            }

            rejectionReason = GASStateDeltaRejectionReason.None;
            return true;
        }

        private bool ValidateStateDeltaCrossReferences(
            GASAbilitySystemStateDeltaBuffer delta,
            out GASStateDeltaRejectionReason rejectionReason)
        {
            try
            {
                stateDeltaIdValidationScratch.Clear();
                for (int i = 0; i < delta.GrantedAbilityCount; i++)
                {
                    ref readonly GASGrantedAbilityStateData entry = ref delta.GrantedAbilities[i];
                    GameplayAbility ability = entry.AbilityDefinition as GameplayAbility;
                    if (ability == null ||
                        ability.InstancingPolicy == EGameplayAbilityInstancingPolicy.NonInstanced ||
                        !stateDeltaIdValidationScratch.Add(entry.SpecHandle))
                    {
                        rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                        return false;
                    }
                }

                for (int i = 0; i < delta.RemovedAbilitySpecHandleCount; i++)
                {
                    if (!stateDeltaIdValidationScratch.Add(delta.RemovedAbilitySpecHandles[i]))
                    {
                        rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                        return false;
                    }
                }

                stateDeltaIdValidationScratch.Clear();
                int additionalEffects = 0;
                int additionalGrantedAbilities = 0;
                for (int i = 0; i < delta.ActiveEffectCount; i++)
                {
                    ref readonly GASActiveEffectStateData entry = ref delta.ActiveEffects[i];
                    GameplayEffect definition = entry.EffectDefinition as GameplayEffect;
                    if (definition == null ||
                        definition.Modifiers.Count > Limits.MaxModifiersPerEffect ||
                        !stateDeltaIdValidationScratch.Add(entry.ReconciliationId))
                    {
                        rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                        return false;
                    }

                    if (FindActiveEffectByReconciliationId(entry.ReconciliationId) == null)
                    {
                        additionalEffects++;
                        additionalGrantedAbilities += definition.GrantedAbilities.Count;
                    }
                }

                int removedExistingEffects = 0;
                int releasedGrantedAbilities = 0;
                for (int i = 0; i < delta.RemovedEffectReconciliationIdCount; i++)
                {
                    int removedEffectId = delta.RemovedEffectReconciliationIds[i];
                    if (!stateDeltaIdValidationScratch.Add(removedEffectId))
                    {
                        rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                        return false;
                    }

                    ActiveGameplayEffect removedEffect = FindActiveEffectByReconciliationId(removedEffectId);
                    if (removedEffect != null)
                    {
                        removedExistingEffects++;
                        if (AbilitySpecs.TryGetGrantedSpecs(removedEffect, out List<GameplayAbilitySpec> grantedSpecs))
                        {
                            releasedGrantedAbilities += grantedSpecs.Count;
                        }
                    }
                }

                for (int i = 0; i < delta.GrantedAbilityCount; i++)
                {
                    ref readonly GASGrantedAbilityStateData abilityEntry = ref delta.GrantedAbilities[i];
                    int grantingEffectId = abilityEntry.GrantingEffectReconciliationId;
                    if (grantingEffectId == 0)
                    {
                        continue;
                    }

                    for (int removedIndex = 0; removedIndex < delta.RemovedEffectReconciliationIdCount; removedIndex++)
                    {
                        if (delta.RemovedEffectReconciliationIds[removedIndex] == grantingEffectId)
                        {
                            rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                            return false;
                        }
                    }

                    GameplayEffect grantingDefinition = FindActiveEffectByReconciliationId(grantingEffectId)?.Spec?.Def;
                    for (int effectIndex = 0; effectIndex < delta.ActiveEffectCount; effectIndex++)
                    {
                        if (delta.ActiveEffects[effectIndex].ReconciliationId == grantingEffectId)
                        {
                            grantingDefinition = delta.ActiveEffects[effectIndex].EffectDefinition as GameplayEffect;
                            break;
                        }
                    }

                    if (!(abilityEntry.AbilityDefinition is GameplayAbility abilityDefinition) ||
                        !DefinitionGrantsAbility(grantingDefinition, abilityDefinition))
                    {
                        rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                        return false;
                    }
                }

                int projectedActiveEffectCount = activeEffects.Count - removedExistingEffects + additionalEffects;
                int projectedGrantedAbilityCount = activatableAbilities.Count - releasedGrantedAbilities + additionalGrantedAbilities;
                if (projectedActiveEffectCount > Limits.MaxActiveEffects ||
                    projectedGrantedAbilityCount > Limits.MaxGrantedAbilities)
                {
                    rejectionReason = GASStateDeltaRejectionReason.CapacityExceeded;
                    return false;
                }

                stateDeltaNameValidationScratch.Clear();
                for (int i = 0; i < delta.AttributeCount; i++)
                {
                    string attributeName = delta.Attributes[i].AttributeName;
                    if (!attributes.ContainsKey(attributeName) || !stateDeltaNameValidationScratch.Add(attributeName))
                    {
                        rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                        return false;
                    }
                }

                stateDeltaTagValidationScratch.Clear();
                for (int i = 0; i < delta.AddedTagCount; i++)
                {
                    GameplayTag tag = delta.AddedTags[i];
                    if (!stateDeltaTagValidationScratch.Add(tag) || looseTags.GetExplicitTagCount(tag) != 0)
                    {
                        rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                        return false;
                    }
                }
                for (int i = 0; i < delta.RemovedTagCount; i++)
                {
                    GameplayTag tag = delta.RemovedTags[i];
                    if (!stateDeltaTagValidationScratch.Add(tag) || looseTags.GetExplicitTagCount(tag) <= 0)
                    {
                        rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                        return false;
                    }
                }

                rejectionReason = GASStateDeltaRejectionReason.None;
                return true;
            }
            catch (Exception exception)
            {
                GASLog.Warning(sb => sb.Append("Rejected GAS state delta during validation preflight: ").Append(exception.Message));
                rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                return false;
            }
            finally
            {
                stateDeltaIdValidationScratch.Clear();
                stateDeltaNameValidationScratch.Clear();
                stateDeltaTagValidationScratch.Clear();
            }
        }

        private static bool IsValidCount<T>(int count, T[] array)
        {
            return count >= 0 && count <= (array?.Length ?? 0);
        }

        private static bool ValidateTags(GameplayTag[] tags, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (tags[i].IsNone || !tags[i].IsValid)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsNewerSequence(uint candidate, uint current)
        {
            uint distance = unchecked(candidate - current);
            return distance != 0u && distance < 0x80000000u;
        }

        private void RequireStateDeltaResync(GASStateDeltaRejectionReason reason)
        {
            stateDeltaResyncRequired = true;
            DispatchStateDeltaResyncObservers(reason);
        }

        private void RemoveActiveEffectByReconciliationId(int reconciliationId)
        {
            var toRemove = FindActiveEffectByReconciliationId(reconciliationId);
            if (toRemove == null)
            {
                return;
            }

            if (TryFindActiveEffectIndex(toRemove, out int index))
            {
                RemoveActiveEffectAtIndex(index);
                RemoveFromStackingIndex(toRemove);
                OnEffectRemoved(toRemove, true);
            }
        }

        private void ApplyAddedTags(GameplayTag[] tags, int count)
        {
            if (tags == null) return;
            for (int i = 0; i < count; i++)
            {
                var tag = tags[i];
                if (tag.IsValid && !tag.IsNone)
                {
                    looseTags.AddTag(tag);
                    combinedTags.AddTag(tag);
                }
            }
        }

        private void ApplyRemovedTags(GameplayTag[] tags, int count)
        {
            if (tags == null) return;
            for (int i = 0; i < count; i++)
            {
                var tag = tags[i];
                if (tag.IsValid && !tag.IsNone)
                {
                    looseTags.RemoveTag(tag);
                    combinedTags.RemoveTag(tag);
                }
            }
        }

        private void ApplyRemovedAbilities(int[] specHandles, int count)
        {
            if (specHandles == null) return;
            for (int i = 0; i < count; i++)
            {
                GameplayAbilitySpec spec = FindSpecByHandle(specHandles[i]);
                if (spec != null)
                {
                    ClearAbilityInternal(spec);
                }
            }
        }

        private void ApplyGrantedAbilityReplacement(GASGrantedAbilityStateData[] grantedAbilities, int count)
        {
            if (grantedAbilities == null) return;

            ReassignUnambiguousReplicatedAbilitySpecs(grantedAbilities, count);

            for (int i = activatableAbilities.Count - 1; i >= 0; i--)
            {
                var spec = activatableAbilities[i];
                if (!ContainsGrantedAbilityHandle(grantedAbilities, count, spec.Handle))
                {
                    ClearAbilityInternal(spec);
                }
            }

            for (int i = 0; i < count; i++)
            {
                ref readonly var abilitySnap = ref grantedAbilities[i];
                var ability = abilitySnap.AbilityDefinition as GameplayAbility;
                if (ability == null) continue;

                GameplayAbilitySpec existingSpec = FindSpecByHandle(abilitySnap.SpecHandle);
                if (existingSpec != null)
                {
                    GameplayAbility existingDefinition = existingSpec.AbilityCDO ?? existingSpec.Ability;
                    if (!ReferenceEquals(existingDefinition, ability))
                    {
                        ClearAbilityInternal(existingSpec);
                        existingSpec = null;
                    }
                }

                if (existingSpec == null)
                {
                    existingSpec = GrantAbility(ability, abilitySnap.Level, abilitySnap.SpecHandle);
                }
                else
                {
                    existingSpec.Level = abilitySnap.Level;
                }

                ActiveGameplayEffect grantingEffect = abilitySnap.GrantingEffectReconciliationId > 0
                    ? FindActiveEffectByReconciliationId(abilitySnap.GrantingEffectReconciliationId)
                    : null;
                if (abilitySnap.GrantingEffectReconciliationId > 0 && grantingEffect == null)
                {
                    throw new InvalidOperationException(
                        $"Granting effect {abilitySnap.GrantingEffectReconciliationId} was not available for ability spec {abilitySnap.SpecHandle}.");
                }

                if (!ReferenceEquals(existingSpec.GrantingEffect, grantingEffect))
                {
                    if (existingSpec.GrantingEffect != null)
                    {
                        UnregisterAbilityGrantedByEffect(existingSpec);
                    }
                    if (grantingEffect != null)
                    {
                        RegisterAbilityGrantedByEffect(grantingEffect, existingSpec);
                    }
                }

                ApplyReplicatedAbilityActivity(existingSpec, abilitySnap.IsActive);
                existingSpec.IsInputPressed = abilitySnap.IsInputPressed;
            }
        }

        private void ReassignUnambiguousReplicatedAbilitySpecs(
            GASGrantedAbilityStateData[] grantedAbilities,
            int count)
        {
            if (grantedAbilities == null)
            {
                return;
            }

            for (int incomingIndex = 0; incomingIndex < count; incomingIndex++)
            {
                ref readonly GASGrantedAbilityStateData incoming = ref grantedAbilities[incomingIndex];
                if (FindSpecByHandle(incoming.SpecHandle) != null ||
                    !(incoming.AbilityDefinition is GameplayAbility definition))
                {
                    continue;
                }

                int matchingIncomingCount = 0;
                for (int i = 0; i < count; i++)
                {
                    ref readonly GASGrantedAbilityStateData candidateState = ref grantedAbilities[i];
                    if (ReferenceEquals(candidateState.AbilityDefinition, definition) &&
                        candidateState.GrantingEffectReconciliationId == incoming.GrantingEffectReconciliationId)
                    {
                        matchingIncomingCount++;
                    }
                }
                if (matchingIncomingCount != 1)
                {
                    continue;
                }

                GameplayAbilitySpec reusableSpec = null;
                for (int i = 0; i < activatableAbilities.Count; i++)
                {
                    GameplayAbilitySpec candidate = activatableAbilities[i];
                    GameplayAbility candidateDefinition = candidate.AbilityCDO ?? candidate.Ability;
                    int candidateGrantingEffectId = candidate.GrantingEffect?.ReconciliationId ?? 0;
                    if (!ReferenceEquals(candidateDefinition, definition) ||
                        candidateGrantingEffectId != incoming.GrantingEffectReconciliationId ||
                        ContainsGrantedAbilityHandle(grantedAbilities, count, candidate.Handle))
                    {
                        continue;
                    }

                    if (reusableSpec != null)
                    {
                        reusableSpec = null;
                        break;
                    }
                    reusableSpec = candidate;
                }

                if (reusableSpec == null)
                {
                    continue;
                }
                if (!AbilitySpecs.TryReassignHandle(reusableSpec, incoming.SpecHandle))
                {
                    throw new InvalidOperationException(
                        $"Ability spec handle {incoming.SpecHandle} could not be assigned without corrupting the handle index.");
                }
                ObserveAbilitySpecHandle(incoming.SpecHandle);
            }
        }

        private void ApplyReplicatedAbilityActivity(GameplayAbilitySpec spec, bool authorityActive)
        {
            if (!authorityActive && spec.IsLocallyExecuting)
            {
                GameplayAbility ability = spec.GetPrimaryInstance();
                ability?.CancelAbility();
                if (spec.IsLocallyExecuting)
                {
                    ability?.EndAbility();
                }
            }

            if (!authorityActive)
            {
                spec.IsLocallyExecuting = false;
                RemoveTickingAbilitySpec(spec);
            }

            // A remote active flag is bookkeeping only. It must never execute gameplay logic,
            // add activation tags, create tasks, or enter the ticking list.
            spec.IsActive = authorityActive;
        }

        private static bool ContainsGrantedAbilityHandle(
            GASGrantedAbilityStateData[] grantedAbilities,
            int count,
            int specHandle)
        {
            for (int i = 0; i < count; i++)
            {
                if (grantedAbilities[i].SpecHandle == specHandle)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool DefinitionGrantsAbility(
            GameplayEffect effectDefinition,
            GameplayAbility abilityDefinition)
        {
            if (effectDefinition == null || abilityDefinition == null)
            {
                return false;
            }

            IReadOnlyList<GameplayAbility> grantedAbilities = effectDefinition.GrantedAbilities;
            for (int i = 0; i < grantedAbilities.Count; i++)
            {
                if (ReferenceEquals(grantedAbilities[i], abilityDefinition))
                {
                    return true;
                }
            }

            return false;
        }

        private void PrepareStateApplyScratch(in GASActiveEffectStateData snap)
        {
            EnsureStateApplySetByCallerCapacity(snap.SetByCallerTagMagnitudeCount);
            EnsureStateApplyExtendedCapacity(
                snap.SetByCallerNameMagnitudeCount,
                snap.DynamicGrantedTagCount,
                snap.DynamicAssetTagCount);

            if (snap.SetByCallerTagMagnitudes != null && snap.SetByCallerTagMagnitudeCount > 0)
            {
                for (int j = 0; j < snap.SetByCallerTagMagnitudeCount; j++)
                {
                    stateApplySetByCallerTags[j] = snap.SetByCallerTagMagnitudes[j].Tag;
                    stateApplySetByCallerValuesRaw[j] = snap.SetByCallerTagMagnitudes[j].ValueRaw;
                }
            }


            if (snap.SetByCallerNameMagnitudes != null && snap.SetByCallerNameMagnitudeCount > 0)
            {
                for (int i = 0; i < snap.SetByCallerNameMagnitudeCount; i++)
                {
                    stateApplySetByCallerNames[i] = snap.SetByCallerNameMagnitudes[i].Name;
                    stateApplySetByCallerNameValuesRaw[i] = snap.SetByCallerNameMagnitudes[i].ValueRaw;
                }
            }

            if (snap.DynamicGrantedTagCount > 0)
            {
                Array.Copy(snap.DynamicGrantedTags, stateApplyDynamicGrantedTags, snap.DynamicGrantedTagCount);
            }
            if (snap.DynamicAssetTagCount > 0)
            {
                Array.Copy(snap.DynamicAssetTags, stateApplyDynamicAssetTags, snap.DynamicAssetTagCount);
            }
        }

        private void EnsureStateApplySetByCallerCapacity(int count)
        {
            if (count <= 0 || stateApplySetByCallerTags.Length >= count)
            {
                return;
            }

            int next = Math.Max(count, stateApplySetByCallerTags.Length == 0 ? 4 : stateApplySetByCallerTags.Length * 2);
            Array.Resize(ref stateApplySetByCallerTags, next);
            Array.Resize(ref stateApplySetByCallerValuesRaw, next);
        }

        private void EnsureStateApplyExtendedCapacity(
            int setByCallerNameCount,
            int dynamicGrantedTagCount,
            int dynamicAssetTagCount)
        {
            EnsureArrayCapacity(ref stateApplySetByCallerNames, setByCallerNameCount);
            EnsureArrayCapacity(ref stateApplySetByCallerNameValuesRaw, setByCallerNameCount);
            EnsureArrayCapacity(ref stateApplyDynamicGrantedTags, dynamicGrantedTagCount);
            EnsureArrayCapacity(ref stateApplyDynamicAssetTags, dynamicAssetTagCount);
        }

    }
}
