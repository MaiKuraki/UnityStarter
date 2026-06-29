using System;
using System.Collections.Generic;
using CycloneGames.Factory.Runtime;
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

        public GASAbilitySystemRuntimeOptions(GASCoreStateMode coreStateMode = GASCoreStateMode.MirrorRuntime)
        {
            CoreStateMode = coreStateMode;
        }

        public GASCoreStateMode CoreStateMode { get; }
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
        public int MaxOpenPredictionWindowAgeFrames;
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
        public int PendingRemovedAbilityDefinitionCount;
        public int PendingAddedTagCount;
        public int PendingRemovedTagCount;
        public GASRuntimeListPoolStatistics ListPoolStatistics;

        public bool HasCriticalIssues =>
            (Flags & (GASRuntimeDiagnosticFlags.RuntimeIndexMismatch |
                      GASRuntimeDiagnosticFlags.CoreAbilitySpecHandleMismatch |
                      GASRuntimeDiagnosticFlags.CoreActiveEffectHandleMismatch)) != 0;
    }

    public partial class AbilitySystemComponent : IDisposable, IGASNetworkTarget
    {
        public readonly struct GASPredictionScope : IDisposable
        {
            private readonly AbilitySystemComponent asc;
            private readonly GASPredictionKey previousPredictionKey;
            private readonly bool active;

            internal GASPredictionScope(AbilitySystemComponent asc, GASPredictionKey predictionKey)
            {
                this.asc = asc;
                previousPredictionKey = asc.currentPredictionKey;
                active = predictionKey.IsValid;

                if (active)
                {
                    asc.currentPredictionKey = predictionKey;
                }
            }

            public void Dispose()
            {
                if (active && asc != null)
                {
                    asc.currentPredictionKey = previousPredictionKey;
                }
            }
        }

        public object OwnerActor { get; private set; }
        public object AvatarActor { get; private set; }
        public UnityEngine.Object OwnerUnityObject { get; private set; }
        public GameObject AvatarGameObject { get; private set; }
        public EReplicationMode ReplicationMode { get; set; } = EReplicationMode.Full;

        public GameplayTagCountContainer CombinedTags { get; } = new GameplayTagCountContainer();
        private readonly GameplayTagCountContainer looseTags = new GameplayTagCountContainer();
        private readonly GameplayTagCountContainer fromEffectsTags = new GameplayTagCountContainer();
        private const int DefaultGrantedAbilitySpecListCapacity = 2;
        private const int DefaultAbilityAppliedEffectListCapacity = 4;
        private const int DefaultReusableListPoolLimit = 32;
        private const int DefaultMaxRetainedGrantedAbilitySpecListCapacity = 16;
        private const int DefaultMaxRetainedAbilityAppliedEffectListCapacity = 32;

        /// <summary>
        /// Tags that grant immunity to effects. Effects with AssetTags or GrantedTags matching these will be blocked.
        /// </summary>
        public GameplayTagContainer ImmunityTags { get; } = new GameplayTagContainer();

        public AbilitySpecContainer AbilitySpecs { get; }
        public ActiveEffectContainer ActiveEffectContainer { get; }
        public AttributeAggregator AttributeAggregator { get; }
        public PredictionManager PredictionManager { get; }
        public ReplicationStateBuilder ReplicationStateBuilder { get; }
        public GameplayCueDispatcher CueDispatcher { get; }

        private readonly List<AttributeSet> attributeSets;
        public IReadOnlyList<AttributeSet> AttributeSets => attributeSets;
        private readonly Dictionary<string, GameplayAttribute> attributes;
        private readonly List<ActiveGameplayEffect> activeEffects;
        private readonly Dictionary<ActiveGameplayEffect, int> activeEffectIndexByEffect;
        private readonly Dictionary<int, ActiveGameplayEffect> activeEffectByNetworkId;
        // Expose as IReadOnlyList without AsReadOnly() wrapper allocation.
        // List<T> implements IReadOnlyList<T> directly since .NET 4.5.
        public IReadOnlyList<ActiveGameplayEffect> ActiveEffects => activeEffects;

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
        public IReadOnlyList<GameplayAbilitySpec> GetActivatableAbilities() => activatableAbilities;
        public int MaxPooledGrantedAbilitySpecLists { get; set; } = DefaultReusableListPoolLimit;
        public int MaxRetainedGrantedAbilitySpecListCapacity { get; set; } = DefaultMaxRetainedGrantedAbilitySpecListCapacity;
        private int peakGrantedAbilitySpecListPoolSize;
        private long grantedAbilitySpecListGets;
        private long grantedAbilitySpecListMisses;
        private long grantedAbilitySpecListDiscards;

        private readonly List<GameplayAbilitySpec> tickingAbilities;
        private readonly Dictionary<GameplayAbilitySpec, int> tickingAbilityIndexBySpec;

        private readonly List<GameplayAttribute> dirtyAttributes;

        //  Tracks effects applied by abilities for RemoveGameplayEffectsAfterAbilityEnds
        private readonly Dictionary<GameplayAbility, List<ActiveGameplayEffect>> abilityAppliedEffects;
        private readonly List<ActiveGameplayEffect> abilityAppliedEffectRemovalScratch;
        private readonly Stack<List<ActiveGameplayEffect>> abilityAppliedEffectListPool;
        public int MaxPooledAbilityAppliedEffectLists { get; set; } = DefaultReusableListPoolLimit;
        public int MaxRetainedAbilityAppliedEffectListCapacity { get; set; } = DefaultMaxRetainedAbilityAppliedEffectListCapacity;
        private int peakAbilityAppliedEffectListPoolSize;
        private long abilityAppliedEffectListGets;
        private long abilityAppliedEffectListMisses;
        private long abilityAppliedEffectListDiscards;

        [ThreadStatic]
        private static List<ModifierInfo> executionOutputScratchPad;

        // --- Prediction ---
        private const int DefaultPredictionTransactionRecordCapacity = 64;
        private GASPredictionKey currentPredictionKey { get => PredictionManager.CurrentPredictionKey; set => PredictionManager.CurrentPredictionKey = value; }
        public int PredictionWindowTimeoutFrames { get; set; } = 180;
        public int OpenPredictionWindowCount => PredictionManager.WindowCount;
        public GASPredictionKey CurrentPredictionKey => currentPredictionKey;
        public event Action<GASPredictionKey, GASPredictionWindowStatus> OnPredictionWindowClosed;

        private int runtimeThreadId;
        private long runtimeThreadViolationCount;
        public GASRuntimeThreadPolicy RuntimeThreadPolicy { get; set; } = GASRuntimeThreadPolicy.Disabled;
        public int RuntimeThreadId => runtimeThreadId;
        public long RuntimeThreadViolationCount => runtimeThreadViolationCount;

        public void BindRuntimeThreadToCurrent()
        {
            runtimeThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void AssertRuntimeThread()
        {
            if (RuntimeThreadPolicy == GASRuntimeThreadPolicy.Disabled)
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
            if (RuntimeThreadPolicy == GASRuntimeThreadPolicy.Throw)
            {
                throw new InvalidOperationException($"AbilitySystemComponent accessed from thread {currentThreadId}; runtime thread is {runtimeThreadId}.");
            }

            GASLog.Warning(sb => sb.Append("AbilitySystemComponent accessed from thread ")
                .Append(currentThreadId)
                .Append("; runtime thread is ")
                .Append(runtimeThreadId)
                .Append('.'));
        }

        public bool ValidateRuntimeIndexes()
        {
            if (!ActiveEffectContainer.ValidateIndexes() || !AbilitySpecs.ValidateIndexes())
            {
                return false;
            }

            if (activeEffectIndexByEffect.Count != activeEffects.Count ||
                activeEffectByNetworkId.Count > activeEffects.Count ||
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

                if (effect.NetworkId != 0 &&
                    (!activeEffectByNetworkId.TryGetValue(effect.NetworkId, out var indexedByNetworkId) ||
                     !ReferenceEquals(indexedByNetworkId, effect)))
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

            int maxPredictionAge = 0;
            int frame = Time.frameCount;
            var windows = PredictionManager.Windows;
            for (int i = 0; i < windows.Count; i++)
            {
                int age = frame - windows[i].OpenFrame;
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

            if (pendingRemovedEffectNetIds.Count > 0)
            {
                flags |= GASRuntimeDiagnosticFlags.PendingRemovedEffects;
            }

            if (pendingRemovedAbilityDefs.Count > 0)
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

            if ((MaxPooledGrantedAbilitySpecLists > 0 &&
                 listPoolStats.GrantedAbilitySpecListPoolSize >= MaxPooledGrantedAbilitySpecLists) ||
                (MaxPooledAbilityAppliedEffectLists > 0 &&
                 listPoolStats.AbilityAppliedEffectListPoolSize >= MaxPooledAbilityAppliedEffectLists))
            {
                flags |= GASRuntimeDiagnosticFlags.RuntimeListPoolAtCapacity;
            }

            if (MaxRetainedGrantedAbilitySpecListCapacity < DefaultGrantedAbilitySpecListCapacity ||
                MaxRetainedAbilityAppliedEffectListCapacity < DefaultAbilityAppliedEffectListCapacity)
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
                PendingRemovedEffectCount = pendingRemovedEffectNetIds.Count,
                PendingRemovedAbilityDefinitionCount = pendingRemovedAbilityDefs.Count,
                PendingAddedTagCount = pendingAddedTags.Count,
                PendingRemovedTagCount = pendingRemovedTags.Count,
                ListPoolStatistics = listPoolStats
            };
        }

        //  Attribute snapshot for rolling back instant-effect attribute changes on prediction failure.
        // Populated in ApplyModifier when currentPredictionKey.IsValid; cleared on confirm or rollback.
        private readonly List<(GASPredictionKey key, GameplayAttribute attr, long oldBaseValueRaw)> predictedAttributeSnapshots = new List<(GASPredictionKey, GameplayAttribute, long)>(8);

        // --- Network ---
        // Monotonically-increasing counter for assigning stable NetworkIds to replicated ActiveGameplayEffects.
        // Only the server assigns NetworkIds; clients receive them via ClientReceiveEffectApplied.
        private static int s_NextEffectNetworkId;
        private static int s_NextCoreEntityId;
        private int networkReplicationScopeDepth;

        private readonly GASCoreStateMode coreStateMode;
        private readonly GASEntityId coreEntity;
        private readonly GASAbilitySystemState coreState;
        private readonly GASAbilitySystemFacade core;
        private readonly Dictionary<GameplayAbilitySpec, GASSpecHandle> coreSpecHandles;
        private readonly Dictionary<ActiveGameplayEffect, GASActiveEffectHandle> coreActiveEffectHandles;
        private GASModifierData[] coreModifierBuffer = Array.Empty<GASModifierData>();
        private GameplayTag[] effectReplicationSetByCallerTags = Array.Empty<GameplayTag>();
        private long[] effectReplicationSetByCallerValuesRaw = Array.Empty<long>();
        private GameplayTag[] stateApplySetByCallerTags = Array.Empty<GameplayTag>();
        private long[] stateApplySetByCallerValuesRaw = Array.Empty<long>();
        private int[] targetDataNetworkIdBuffer = Array.Empty<int>();

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

        public IFactory<IGameplayEffectContext> EffectContextFactory { get; private set; }

        // Cached ActorInfo to avoid repeated struct construction
        private GameplayAbilityActorInfo cachedActorInfo;

        // --- Events (UE5: OnGameplayEffectAppliedDelegateToSelf, OnAnyGameplayEffectRemovedDelegate) ---
        public event ActiveEffectDelegate OnGameplayEffectAppliedToSelf;
        public event ActiveEffectDelegate OnGameplayEffectRemovedFromSelf;

        // --- Ability Lifecycle Events (UE5: AbilityActivatedCallbacks, AbilityEndedCallbacks, AbilityCommittedCallbacks) ---
        public event Action<GameplayAbility> OnAbilityActivated;
        public event Action<GameplayAbility> OnAbilityEndedEvent;
        public event Action<GameplayAbility> OnAbilityCommitted;

        // --- Gameplay Event System (UE5: SendGameplayEventToActor) ---
        private readonly Dictionary<GameplayTag, GameplayEventDelegate> eventDelegates = new Dictionary<GameplayTag, GameplayEventDelegate>(8);

        // --- Ability Trigger System (UE5: FAbilityTriggerData) ---
        // Maps trigger tags to the specs whose abilities should be activated when the trigger fires.
        private readonly Dictionary<GameplayTag, List<GameplayAbilitySpec>> triggerEventAbilities = new Dictionary<GameplayTag, List<GameplayAbilitySpec>>(8);
        private readonly Dictionary<GameplayTag, List<GameplayAbilitySpec>> triggerTagAddedAbilities = new Dictionary<GameplayTag, List<GameplayAbilitySpec>>(8);
        private readonly Dictionary<GameplayTag, List<GameplayAbilitySpec>> triggerTagRemovedAbilities = new Dictionary<GameplayTag, List<GameplayAbilitySpec>>(8);

        #region Tag Event Convenience API

        /// <summary>
        /// Registers a callback for when a specific tag is added or removed from this ASC.
        /// </summary>
        public void RegisterTagEventCallback(GameplayTag tag, GameplayTagEventType eventType, OnTagCountChangedDelegate callback)
        {
            CombinedTags.RegisterTagEventCallback(tag, eventType, callback);
        }

        /// <summary>
        /// Removes a tag event callback.
        /// </summary>
        public void RemoveTagEventCallback(GameplayTag tag, GameplayTagEventType eventType, OnTagCountChangedDelegate callback)
        {
            CombinedTags.RemoveTagEventCallback(tag, eventType, callback);
        }

        /// <summary>
        /// Adds an immunity tag. Effects matching this tag will be blocked.
        /// </summary>
        public void AddImmunityTag(GameplayTag tag)
        {
            if (!tag.IsNone && !ImmunityTags.HasTag(tag))
            {
                ImmunityTags.AddTag(tag);
            }
        }

        /// <summary>
        /// Removes an immunity tag.
        /// </summary>
        public void RemoveImmunityTag(GameplayTag tag)
        {
            if (!tag.IsNone)
            {
                ImmunityTags.RemoveTag(tag);
            }
        }

        #endregion

        #region Gameplay Event System (UE5: SendGameplayEventToActor)

        /// <summary>
        /// Registers a delegate to handle gameplay events with a specific tag.
        /// </summary>
        public void RegisterGameplayEventCallback(GameplayTag eventTag, GameplayEventDelegate callback)
        {
            if (eventTag.IsNone || callback == null) return;
            if (eventDelegates.TryGetValue(eventTag, out var existing))
            {
                eventDelegates[eventTag] = existing + callback;
            }
            else
            {
                eventDelegates[eventTag] = callback;
            }
        }

        /// <summary>
        /// Removes a gameplay event callback.
        /// </summary>
        public void RemoveGameplayEventCallback(GameplayTag eventTag, GameplayEventDelegate callback)
        {
            if (eventTag.IsNone || callback == null) return;
            if (eventDelegates.TryGetValue(eventTag, out var existing))
            {
                var updated = existing - callback;
                if (updated == null)
                    eventDelegates.Remove(eventTag);
                else
                    eventDelegates[eventTag] = updated;
            }
        }

        /// <summary>
        /// Sends a gameplay event to this ASC. Matching handlers and waiting AbilityTasks will be notified.
        /// Also triggers abilities registered with GameplayEvent trigger type.
        /// UE5 equivalent: UAbilitySystemBlueprintLibrary::SendGameplayEventToActor.
        /// </summary>
        public void HandleGameplayEvent(GameplayEventData eventData)
        {
            if (eventData.EventTag.IsNone) return;

            // Notify registered delegates
            if (eventDelegates.TryGetValue(eventData.EventTag, out var handler))
            {
                handler.Invoke(eventData);
            }

            // UE5: Trigger abilities registered for this event tag
            if (triggerEventAbilities.TryGetValue(eventData.EventTag, out var triggeredSpecs))
            {
                for (int i = 0; i < triggeredSpecs.Count; i++)
                {
                    var spec = triggeredSpecs[i];
                    if (!spec.IsActive)
                    {
                        TryActivateAbility(spec);
                    }
                }
            }
        }

        #endregion

        public AbilitySystemComponent(IFactory<IGameplayEffectContext> effectContextFactory)
            : this(effectContextFactory, GASAbilitySystemRuntimeOptions.Default)
        {
        }

        public AbilitySystemComponent(
            IFactory<IGameplayEffectContext> effectContextFactory,
            GASAbilitySystemRuntimeOptions options)
        {
            if (options == null)
            {
                options = GASAbilitySystemRuntimeOptions.Default;
            }

            AbilitySpecs = new AbilitySpecContainer();
            ActiveEffectContainer = new ActiveEffectContainer();
            AttributeAggregator = new AttributeAggregator();
            PredictionManager = new PredictionManager();
            ReplicationStateBuilder = new ReplicationStateBuilder();
            CueDispatcher = GameplayCueDispatcher.Default;

            attributeSets = AttributeAggregator.MutableAttributeSets;
            attributes = AttributeAggregator.MutableAttributes;
            dirtyAttributes = AttributeAggregator.MutableDirtyAttributes;

            activeEffects = ActiveEffectContainer.MutableActiveEffects;
            activeEffectIndexByEffect = ActiveEffectContainer.MutableIndexByEffect;
            activeEffectByNetworkId = ActiveEffectContainer.MutableEffectByNetworkId;
            stackingIndexByTarget = ActiveEffectContainer.MutableStackingByTarget;
            stackingIndexBySource = ActiveEffectContainer.MutableStackingBySource;
            grantedTagIndexToEffects = ActiveEffectContainer.MutableEffectsByGrantedTagIndex;
            abilityAppliedEffects = ActiveEffectContainer.MutableEffectsByAbility;
            abilityAppliedEffectRemovalScratch = ActiveEffectContainer.MutableAbilityEffectRemovalScratch;
            abilityAppliedEffectListPool = ActiveEffectContainer.MutableAbilityEffectListPool;

            activatableAbilities = AbilitySpecs.MutableActivatableAbilities;
            abilitySpecByHandle = AbilitySpecs.MutableSpecByHandle;
            abilitySpecIndexBySpec = AbilitySpecs.MutableIndexBySpec;
            grantedAbilitySpecsByEffect = AbilitySpecs.MutableSpecsByGrantingEffect;
            grantedAbilitySpecListPool = AbilitySpecs.MutableGrantedSpecListPool;
            tickingAbilities = AbilitySpecs.MutableTickingAbilities;
            tickingAbilityIndexBySpec = AbilitySpecs.MutableTickingIndexBySpec;

            this.EffectContextFactory = effectContextFactory;
            BindRuntimeThreadToCurrent();
            int entityId = System.Threading.Interlocked.Increment(ref s_NextCoreEntityId);
            coreEntity = new GASEntityId(entityId);
            coreStateMode = options.CoreStateMode;
            if (coreStateMode == GASCoreStateMode.MirrorRuntime)
            {
                coreState = new GASAbilitySystemState(coreEntity);
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

            CombinedTags.OnAnyTagNewOrRemove += TrackTagCountChange;
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
            int targetDataObjectCapacity = 16,
            int predictionTransactionRecordCapacity = DefaultPredictionTransactionRecordCapacity,
            int tagDeltaCapacity = 16)
        {
            AbilitySpecs.Reserve(abilityCapacity, activeEffectCapacity);
            ActiveEffectContainer.Reserve(activeEffectCapacity, activeEffectCapacity, tagDeltaCapacity, activeEffectCapacity);
            AttributeAggregator.Reserve(Math.Min(attributeCapacity, 8), attributeCapacity);
            PredictionManager.Reserve(predictionWindowCapacity, predictionTransactionRecordCapacity);

            EnsureListCapacity(attributeSets, Math.Min(attributeCapacity, 8));
            EnsureListCapacity(activeEffects, activeEffectCapacity);
            EnsureDictionaryCapacity(activeEffectIndexByEffect, activeEffectCapacity);
            EnsureDictionaryCapacity(activeEffectByNetworkId, activeEffectCapacity);
            EnsureListCapacity(activatableAbilities, abilityCapacity);
            EnsureDictionaryCapacity(abilitySpecByHandle, abilityCapacity);
            EnsureDictionaryCapacity(abilitySpecIndexBySpec, abilityCapacity);
            EnsureDictionaryCapacity(grantedAbilitySpecsByEffect, activeEffectCapacity);
            EnsureListCapacity(tickingAbilities, tickingAbilityCapacity);
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
            EnsureTargetDataNetworkIdCapacity(targetDataObjectCapacity);
            EnsurePredictionTransactionRecordCapacity(predictionTransactionRecordCapacity);

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
            AbilitySpecs.AddTickingSpec(spec);
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
            if (MaxPooledGrantedAbilitySpecLists <= 0 ||
                grantedAbilitySpecListPool.Count >= MaxPooledGrantedAbilitySpecLists ||
                specs.Capacity > MaxRetainedGrantedAbilitySpecListCapacity)
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
            if (MaxPooledAbilityAppliedEffectLists <= 0 ||
                abilityAppliedEffectListPool.Count >= MaxPooledAbilityAppliedEffectLists ||
                effects.Capacity > MaxRetainedAbilityAppliedEffectListCapacity)
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

            if (target > MaxPooledGrantedAbilitySpecLists)
            {
                target = MaxPooledGrantedAbilitySpecLists;
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

            if (target > MaxPooledAbilityAppliedEffectLists)
            {
                target = MaxPooledAbilityAppliedEffectLists;
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
                MaxPooledGrantedAbilitySpecLists = MaxPooledGrantedAbilitySpecLists,
                MaxPooledAbilityAppliedEffectLists = MaxPooledAbilityAppliedEffectLists,
                MaxRetainedGrantedAbilitySpecListCapacity = MaxRetainedGrantedAbilitySpecListCapacity,
                MaxRetainedAbilityAppliedEffectListCapacity = MaxRetainedAbilityAppliedEffectListCapacity
            };
        }

        public void ResetRuntimeListPoolStatistics()
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
            grantedAbilitySpecListPool.Clear();
            abilityAppliedEffectListPool.Clear();
            peakGrantedAbilitySpecListPoolSize = 0;
            peakAbilityAppliedEffectListPoolSize = 0;
        }

        public void AddAttributeSet(AttributeSet set)
        {
            AssertRuntimeThread();
            if (set != null && !attributeSets.Contains(set))
            {
                attributeSets.Add(set);
                set.OwningAbilitySystemComponent = this;
                foreach (var attr in set.GetAttributes())
                {
                    if (!attributes.ContainsKey(attr.Name))
                    {
                        attributes.Add(attr.Name, attr);
                        RegisterAttributeInCore(attr);
                    }
                    else
                    {
                        GASLog.Warning(sb => sb.Append("Attribute '").Append(attr.Name).Append("' is already present. Duplicate attributes are not allowed."));
                    }
                }

                MarkAttributeStructureDirty();
            }
        }

        /// <summary>
        /// Removes an AttributeSet from this ASC at runtime.
        /// UE5: UAbilitySystemComponent::GetSpawnedAttributes_Mutable().Remove()
        /// </summary>
        public void RemoveAttributeSet(AttributeSet set)
        {
            AssertRuntimeThread();
            if (set == null || !attributeSets.Contains(set)) return;

            foreach (var attr in set.GetAttributes())
            {
                // Only remove if this set actually owns the attribute in our dictionary
                if (attributes.TryGetValue(attr.Name, out var registered) && registered == attr)
                {
                    attributes.Remove(attr.Name);
                }
            }

            attributeSets.Remove(set);
            set.OwningAbilitySystemComponent = null;
            MarkAttributeStructureDirty();
        }

        public void MarkAttributeDirty(GameplayAttribute attribute)
        {
            AssertRuntimeThread();
            if (attribute != null && !attribute.IsDirty)
            {
                attribute.IsDirty = true;
                dirtyAttributes.Add(attribute);
                MarkAttributeValueDirty(attribute);
            }

            if (attribute != null)
            {
                RegisterAttributeInCore(attribute);
            }
        }

        public GameplayAttribute GetAttribute(string name)
        {
            attributes.TryGetValue(name, out var attribute);
            return attribute;
        }

        public GameplayAbilitySpec GrantAbility(GameplayAbility ability, int level = 1, int replicatedHandle = 0)
        {
            AssertRuntimeThread();
            if (ability == null) return null;

            var spec = GameplayAbilitySpec.Create(ability, level, replicatedHandle);
            spec.Init(this);

            AbilitySpecs.AddSpec(spec);
            if (ability.InstancingPolicy == EGameplayAbilityInstancingPolicy.InstancedPerActor)
            {
                spec.CreateInstance();
            }
            else if (ability.InstancingPolicy == EGameplayAbilityInstancingPolicy.NonInstanced)
            {
                spec.GetPrimaryInstance().OnGiveAbility(cachedActorInfo, spec);
            }

            RegisterGrantedAbilityInCore(spec, ability, level);
            MarkGrantedAbilitiesDirty();

            // UE5: Register ability triggers (FAbilityTriggerData)
            RegisterAbilityTriggers(spec);

            // UE5: bActivateAbilityOnGranted --auto-activate passive abilities
            if (ability.ActivateAbilityOnGranted)
            {
                TryActivateAbility(spec);
            }

            return spec;
        }

        public void ClearAbility(GameplayAbilitySpec spec)
        {
            AssertRuntimeThread();
            if (spec == null) return;

            var removedDef = spec.AbilityCDO ?? spec.Ability;

            // UE5: Unregister ability triggers before removal
            UnregisterAbilityTriggers(spec);
            UnregisterAbilityGrantedByEffect(spec);

            RemoveActivatableAbilitySpec(spec);
            RemoveTickingAbilitySpec(spec);

            spec.OnRemoveSpec();
            RemoveGrantedAbilityFromCore(spec);
            spec.ReturnToPool();

            // Delta tracking: record this definition as removed so the next state delta can tell clients.
            ReplicationStateBuilder.TrackRemovedAbilityDefinition(removedDef);

            MarkGrantedAbilitiesDirty();
        }

        // --- Ability Activation Flow ---
        public bool TryActivateAbility(GameplayAbilitySpec spec)
        {
            AssertRuntimeThread();
            if (spec == null)
            {
                if (GASTrace.Enabled)
                {
                    GASTrace.Record(GASTraceEventType.AbilityActivateBlocked, this, decision: GASTraceDecision.Blocked, reason: GASTraceReason.MissingSpec);
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

            if (!ability.CanActivate(cachedActorInfo, spec))
            {
                return false;
            }

            switch (ability.NetExecutionPolicy)
            {
                case ENetExecutionPolicy.LocalOnly:
                    ActivateAbilityInternal(spec, new GameplayAbilityActivationInfo()); // No prediction key
                    break;
                case ENetExecutionPolicy.LocalPredicted:
                    var predictionKey = OpenPredictionWindow(spec);
                    var activationInfo = new GameplayAbilityActivationInfo { PredictionKey = predictionKey };
                    ActivateAbilityInternal(spec, activationInfo);
                    GASServices.NetworkBridge.ClientRequestActivateAbility(this, spec.Handle, predictionKey);
                    break;
                case ENetExecutionPolicy.ServerOnly:
                    if (GASServices.NetworkBridge.IsServer)
                    {
                        ServerTryActivateAbility(spec, new GameplayAbilityActivationInfo());
                    }
                    else
                    {
                        GASServices.NetworkBridge.ClientRequestActivateAbility(this, spec.Handle, default);
                    }
                    break;
            }

            return true;
        }

        private void ServerTryActivateAbility(GameplayAbilitySpec spec, GameplayAbilityActivationInfo activationInfo)
        {
            // --- This block runs on the "server" ---
            if (spec.GetPrimaryInstance().CanActivate(cachedActorInfo, spec))
            {
                // Server confirms activation
                ActivateAbilityInternal(spec, activationInfo);
                // Notify the client via bridge (GASNullNetworkBridge calls ClientReceiveActivationSucceeded directly).
                GASServices.NetworkBridge.ServerConfirmActivation(this, spec.Handle, activationInfo.PredictionKey);
            }
            else
            {
                // Server rejects activation
                if (GASTrace.Enabled)
                {
                    GASTrace.Record(GASTraceEventType.PredictionRejected, this, spec.GetPrimaryInstance(), decision: GASTraceDecision.Rejected, reason: GASTraceReason.ServerRejected, abilitySpecHandle: spec.Handle, predictionKey: activationInfo.PredictionKey);
                }
                GASServices.NetworkBridge.ServerRejectActivation(this, spec.Handle, activationInfo.PredictionKey);
            }
        }

        // ---------- IGASNetworkTarget entry points ----------

        /// <summary>
        /// Server entry point: called when the server receives a ClientRequestActivateAbility RPC.
        /// In local mode this is called directly by GASNullNetworkBridge.
        /// </summary>
        public void ServerReceiveTryActivateAbility(int specHandle, GASPredictionKey predictionKey)
        {
            AssertRuntimeThread();
            var spec = FindSpecByHandle(specHandle);
            if (spec == null)
            {
                GASServices.NetworkBridge.ServerRejectActivation(this, specHandle, predictionKey);
                return;
            }
            var activationInfo = new GameplayAbilityActivationInfo { PredictionKey = predictionKey };
            ServerTryActivateAbility(spec, activationInfo);
        }

        /// <summary>
        /// Client entry point: server confirmed our predicted activation.
        /// Called by GASNullNetworkBridge immediately, or by the network bridge on RPC receipt.
        /// </summary>
        public void ClientReceiveActivationSucceeded(int specHandle, GASPredictionKey predictionKey)
        {
            AssertRuntimeThread();
            var spec = FindSpecByHandle(specHandle);
            ClientActivateAbilitySucceed(spec, predictionKey);
        }

        /// <summary>
        /// Client entry point: server rejected our predicted activation --roll back.
        /// Called by GASNullNetworkBridge immediately, or by the network bridge on RPC receipt.
        /// </summary>
        public void ClientReceiveActivationFailed(int specHandle, GASPredictionKey predictionKey)
        {
            AssertRuntimeThread();
            var spec = FindSpecByHandle(specHandle);
            ClientActivateAbilityFailed(spec, predictionKey);
        }

        /// <summary>
        /// Client entry point: server replicated a new ActiveGameplayEffect.
        /// The implementation must look up the GameplayEffect SO by data.EffectDefId,
        /// build a spec, and apply it locally without triggering further replication.
        /// </summary>
        public void ClientReceiveEffectApplied(in GASEffectReplicationData data)
        {
            AssertRuntimeThread();
            if (ApplyAuthoritativeEffectReplication(data, allowCreate: true))
            {
                OnClientEffectApplied?.Invoke(data);
            }
        }

        /// <summary>
        /// Client entry point: server replicated an authoritative update for an already-known effect.
        /// </summary>
        public void ClientReceiveEffectUpdated(in GASEffectReplicationData data)
        {
            AssertRuntimeThread();
            ApplyAuthoritativeEffectReplication(data, allowCreate: true);
        }

        /// <summary>
        /// Client entry point: server replicated an effect removal.
        /// </summary>
        public void ClientReceiveEffectRemoved(int effectNetId)
        {
            AssertRuntimeThread();
            var effect = FindActiveEffectByNetworkId(effectNetId);
            if (effect == null)
            {
                return;
            }

            if (!TryFindActiveEffectIndex(effect, out int index))
            {
                return;
            }

            using (new NetworkReplicationScope(this))
            {
                RemoveActiveEffectAtIndex(index);
                RemoveFromStackingIndex(effect);
                OnEffectRemoved(effect, true);
            }
        }

        /// <summary>
        /// Client entry point: server broadcast a GameplayCue event.
        /// </summary>
        public void ClientReceiveGameplayCue(GameplayTag cueTag, EGameplayCueEvent eventType, in GASCueNetParams cueParams)
        {
            AssertRuntimeThread();
            var resolver = GASServices.ReplicationResolver;
            AbilitySystemComponent source = null;
            AbilitySystemComponent target = null;

            if (cueParams.SourceAscNetId != 0 && resolver.TryResolveAbilitySystem(cueParams.SourceAscNetId, out var resolvedSource))
            {
                source = resolvedSource as AbilitySystemComponent;
            }

            if (cueParams.TargetAscNetId != 0 && resolver.TryResolveAbilitySystem(cueParams.TargetAscNetId, out var resolvedTarget))
            {
                target = resolvedTarget as AbilitySystemComponent;
            }

            var parameters = new GameplayCueEventParams(
                source,
                target,
                null,
                null,
                source?.AvatarGameObject,
                target?.AvatarGameObject,
                0,
                0L,
                cueParams.PredictionKey);

            GameplayCueManager.Default.HandleCue(cueTag, eventType, new GameplayCueParameters(parameters)).Forget();
        }

        /// <summary>
        /// Client entry point: server sent a count-based incremental delta buffer.
        /// </summary>
        public void ClientReceiveStateDelta(GASAbilitySystemStateDeltaBuffer delta)
        {
            AssertRuntimeThread();
            ApplyStateDelta(delta);
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
        /// Fired on clients when the server sends a replicated effect application.
        /// Subscribe to resolve data.EffectDefId and call ApplyGameplayEffectSpecToSelf.
        /// </summary>
        public event Action<GASEffectReplicationData> OnClientEffectApplied;

        internal bool SuppressLocalGameplayCueDispatch => networkReplicationScopeDepth > 0 && !GASServices.NetworkBridge.IsServer;

        private GameplayAbilitySpec FindSpecByHandle(int handle)
        {
            return handle > 0 && abilitySpecByHandle.TryGetValue(handle, out var spec) ? spec : null;
        }

        private ActiveGameplayEffect FindActiveEffectByNetworkId(int networkId)
        {
            return ActiveEffectContainer.FindByNetworkId(networkId);
        }

        private void SetActiveEffectNetworkId(ActiveGameplayEffect effect, int networkId)
        {
            ActiveEffectContainer.SetNetworkId(effect, networkId);
        }

        /// <summary>
        /// Computes a deterministic, allocation-free checksum over replicated ASC gameplay state.
        /// Use this as a lightweight drift detector between server and client snapshots.
        /// </summary>
        public ulong ComputeReplicatedStateChecksum()
        {
            const ulong offset = Fnv1a64.OffsetBasis;
            ulong hash = offset;

            hash = HashInt(hash, activatableAbilities.Count);
            for (int i = 0; i < activatableAbilities.Count; i++)
            {
                var spec = activatableAbilities[i];
                ulong entryHash = Fnv1a64.OffsetBasis;
                entryHash = HashInt(entryHash, spec.Handle);
                entryHash = HashInt(entryHash, spec.Level);
                entryHash = HashInt(entryHash, spec.IsActive ? 1 : 0);
                hash = FoldUnordered(hash, entryHash);
            }

            hash = HashInt(hash, activeEffects.Count);
            for (int i = 0; i < activeEffects.Count; i++)
            {
                var effect = activeEffects[i];
                if (effect == null || effect.IsExpired)
                {
                    continue;
                }

                ulong entryHash = Fnv1a64.OffsetBasis;
                entryHash = HashInt(entryHash, effect.NetworkId);
                entryHash = HashInt(entryHash, effect.StackCount);
                entryHash = HashLong(entryHash, effect.TimeRemainingRaw);
                entryHash = HashLong(entryHash, effect.PeriodTimeRemainingRaw);
                entryHash = HashInt(entryHash, effect.Spec?.Context?.PredictionKey.Key ?? 0);
                hash = FoldUnordered(hash, entryHash);
            }

            hash = HashInt(hash, attributes.Count);
            for (int i = 0; i < attributeSets.Count; i++)
            {
                foreach (var attribute in attributeSets[i].GetAttributes())
                {
                    ulong entryHash = Fnv1a64.OffsetBasis;
                    entryHash = HashString(entryHash, attribute.Name);
                    entryHash = HashLong(entryHash, attribute.BaseValueRaw);
                    entryHash = HashLong(entryHash, attribute.CurrentValueRaw);
                    hash = FoldUnordered(hash, entryHash);
                }
            }

            hash = HashInt(hash, CombinedTags.TagCount);
            var tags = CombinedTags.GetTags();
            while (tags.MoveNext())
            {
                var tag = tags.Current;
                ulong entryHash = HashString(Fnv1a64.OffsetBasis, tag.IsValid && !tag.IsNone ? tag.Name : string.Empty);
                hash = FoldUnordered(hash, entryHash);
            }

            return hash;
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

        private static ulong FoldUnordered(ulong hash, ulong entryHash)
        {
            unchecked
            {
                return hash + (entryHash * Fnv1a64.Prime) ^ ((entryHash << 16) | (entryHash >> 48));
            }
        }

        private ActiveGameplayEffect FindPredictedEffectForReconcile(GameplayEffect effectDef, AbilitySystemComponent source, GASPredictionKey predictionKey)
        {
            return PredictionManager.FindPendingPredictedEffectForReconcile(effectDef, source, predictionKey);
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
                (ushort)(level < 0 ? 0 : level),
                ConvertInstancingPolicy(ability.InstancingPolicy),
                ConvertNetExecutionPolicy(ability.NetExecutionPolicy),
                GASReplicationPolicy.OwnerOnly);

            if (handle.IsValid)
            {
                coreSpecHandles[spec] = handle;
            }
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
            }
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
                (ushort)(spec.Level < 0 ? 0 : spec.Level),
                1,
                Time.frameCount,
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
                    spec.GetCalculatedMagnitudeRaw(i));
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

        public GASAbilityActivationResult TryActivateAbilityCore(GameplayAbilitySpec spec, GASPredictionKey predictionKey)
        {
            if (!TryGetCoreSpecHandle(spec, out var handle))
            {
                return new GASAbilityActivationResult(GASAbilityActivationResultCode.MissingSpec, default, predictionKey);
            }

            if (!IsCoreStateEnabled)
            {
                return new GASAbilityActivationResult(GASAbilityActivationResultCode.MissingSpec, default, predictionKey);
            }

            return core.TryActivateAbility(handle, predictionKey);
        }

        private static GASDefinitionId GetOrCreateCoreDefinitionId(object definition)
        {
            return definition is GameplayAbility ability
                ? GetOrRegisterCoreAbilityDefinitionId(ability)
                : GetOrRegisterCoreEffectDefinitionId(definition as GameplayEffect);
        }

        private static GASDefinitionId GetOrRegisterCoreAbilityDefinitionId(GameplayAbility ability)
        {
            if (ability == null)
            {
                return default;
            }

            var registry = GASServices.DefinitionRegistry;
            return registry.TryGetAbilityDefinitionId(ability, out var id)
                ? id
                : registry.RegisterAbilityDefinition(ability, ability.Name);
        }

        private static GASDefinitionId GetOrRegisterCoreEffectDefinitionId(GameplayEffect effect)
        {
            if (effect == null)
            {
                return default;
            }

            var registry = GASServices.DefinitionRegistry;
            return registry.TryGetEffectDefinitionId(effect, out var id)
                ? id
                : registry.RegisterEffectDefinition(effect, effect.Name);
        }

        private static GASAttributeId GetOrCreateCoreAttributeId(string attributeName)
        {
            if (string.IsNullOrEmpty(attributeName))
            {
                return default;
            }

            var registry = GASServices.AttributeRegistry;
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

        private static GASNetExecutionPolicy ConvertNetExecutionPolicy(ENetExecutionPolicy policy)
        {
            switch (policy)
            {
                case ENetExecutionPolicy.LocalOnly:
                    return GASNetExecutionPolicy.LocalOnly;
                case ENetExecutionPolicy.ServerOnly:
                    return GASNetExecutionPolicy.ServerOnly;
                default:
                    return GASNetExecutionPolicy.LocalPredicted;
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

        private void AcceptCorePrediction(GASPredictionKey predictionKey)
        {
            if (IsCoreStateEnabled && predictionKey.IsValid)
            {
                core.AcceptPrediction(ConvertPredictionKey(predictionKey));
            }
        }

        private void RejectCorePrediction(GASPredictionKey predictionKey)
        {
            if (IsCoreStateEnabled && predictionKey.IsValid)
            {
                core.RejectPrediction(ConvertPredictionKey(predictionKey));
            }
        }

        public GASPredictionKey OpenPredictionWindow(GameplayAbilitySpec spec, GASPredictionKey parentPredictionKey = default)
        {
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
            return new GASPredictionScope(this, predictionKey);
        }

        public bool HasOpenPredictionWindow(GASPredictionKey predictionKey)
        {
            return PredictionManager.HasOpenWindow(predictionKey);
        }

        public bool TryGetPredictionWindow(GASPredictionKey predictionKey, out GASPredictionWindowData window)
        {
            return PredictionManager.TryGetWindow(predictionKey, out window);
        }

        public GASPredictionWindowStats GetPredictionWindowStats()
        {
            return PredictionManager.GetStats();
        }

        public bool TryGetClosedPredictionTransactionRecord(int recentIndex, out GASPredictionTransactionRecord record)
        {
            return PredictionManager.TryGetClosedTransactionRecord(recentIndex, out record);
        }

        public int CopyClosedPredictionTransactionRecordsNonAlloc(GASPredictionTransactionRecord[] destination, int destinationIndex = 0, int maxCount = int.MaxValue)
        {
            return PredictionManager.CopyClosedTransactionRecordsNonAlloc(destination, destinationIndex, maxCount);
        }

        public bool ConfirmPredictionWindow(GASPredictionKey predictionKey)
        {
            if (ClosePredictionWindow(predictionKey, GASPredictionWindowStatus.Confirmed, rollback: false, closeDependents: false))
            {
                return true;
            }

            PredictionManager.RecordStaleTransaction(predictionKey, GASPredictionWindowStatus.Confirmed, Time.frameCount);
            return false;
        }

        public bool RejectPredictionWindow(GASPredictionKey predictionKey)
        {
            if (ClosePredictionWindow(predictionKey, GASPredictionWindowStatus.Rejected, rollback: true, closeDependents: true))
            {
                return true;
            }

            PredictionManager.RecordStaleTransaction(predictionKey, GASPredictionWindowStatus.Rejected, Time.frameCount);
            return false;
        }

        public void TickPredictionWindows(int currentFrame)
        {
            while (PredictionManager.TryGetTimedOutWindow(currentFrame, out var window))
            {
                if (GASTrace.Enabled)
                {
                    var spec = FindSpecByHandle(window.AbilitySpecHandle);
                    GASTrace.Record(GASTraceEventType.PredictionTimedOut, this, spec?.GetPrimaryInstance(), decision: GASTraceDecision.TimedOut, reason: GASTraceReason.PredictionTimeout, abilitySpecHandle: window.AbilitySpecHandle, predictionKey: window.PredictionKey, level: spec?.Level ?? 0);
                }

                ClosePredictionWindow(window.PredictionKey, GASPredictionWindowStatus.TimedOut, rollback: true, closeDependents: true);
            }
        }

        private void RegisterPredictionWindow(GameplayAbilitySpec spec, GASPredictionKey predictionKey, GASPredictionKey parentPredictionKey)
        {
            if (!predictionKey.IsValid)
            {
                return;
            }

            TryGetCoreSpecHandle(spec, out var coreHandle);
            int openFrame = Time.frameCount;
            int timeoutFrame = PredictionWindowTimeoutFrames > 0 ? openFrame + PredictionWindowTimeoutFrames : 0;
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

            if (rollback)
            {
                rollbackFlags |= RollbackPrediction(predictionKey);
                rollbackFlags |= CancelPredictedExecution(window, predictionKey);
            }

            PredictionManager.RecordTransaction(window, status, rollbackFlags, Time.frameCount);
            PredictionManager.IncrementClosedWindowCount(status);

            OnPredictionWindowClosed?.Invoke(predictionKey, status);
            return true;
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

        private bool TryFindDependentPredictionWindow(GASPredictionKey parentPredictionKey, out GASPredictionKey childPredictionKey)
        {
            return PredictionManager.TryFindDependentWindow(parentPredictionKey, out childPredictionKey);
        }

        private void RecordPredictionTransaction(
            GASPredictionWindowData window,
            GASPredictionWindowStatus status,
            GASPredictionRollbackFlags rollbackFlags,
            int closeFrame)
        {
            PredictionManager.RecordTransaction(window, status, rollbackFlags, closeFrame);
        }

        private void RecordStalePredictionTransaction(GASPredictionKey predictionKey, GASPredictionWindowStatus status)
        {
            PredictionManager.RecordStaleTransaction(predictionKey, status, Time.frameCount);
        }
        private static int ConvertDurationToTicks(float duration)
        {
            if (duration <= 0f || duration == GameplayEffectConstants.INFINITE_DURATION)
            {
                return 0;
            }

            return Mathf.CeilToInt(duration * 60f);
        }

        public bool ValidateTargetData(TargetData data, GameplayAbilitySpec spec, GASPredictionKey predictionKey, float maxRange = 0f, int maxAgeFrames = 0)
        {
            return TryValidateTargetData(data, spec, predictionKey, maxRange, maxAgeFrames, out _);
        }

        public bool TryBuildTargetDataNetworkData(
            TargetData data,
            Func<GameObject, int> targetIdResolver,
            out TargetDataNetworkData snapshot,
            Func<AbilitySystemComponent, int> sourceIdResolver = null)
        {
            snapshot = default;
            if (data == null || targetIdResolver == null)
            {
                return false;
            }

            if (sourceIdResolver == null)
            {
                sourceIdResolver = asc => GASServices.ReplicationResolver.GetAbilitySystemNetworkId(asc);
            }

            int targetCapacity = TargetDataNetworkCodec.GetRequiredTargetIdCapacity(data);
            EnsureTargetDataNetworkIdCapacity(targetCapacity);
            snapshot = TargetDataNetworkCodec.CaptureNonAlloc(data, targetIdResolver, targetDataNetworkIdBuffer, sourceIdResolver);
            return snapshot.Type != TargetDataNetworkType.None;
        }

        public bool TryBuildTargetDataNetworkDataNonAlloc(
            TargetData data,
            Func<GameObject, int> targetIdResolver,
            int[] targetIdBuffer,
            out TargetDataNetworkData snapshot,
            Func<AbilitySystemComponent, int> sourceIdResolver = null)
        {
            snapshot = default;
            if (data == null || targetIdResolver == null || targetIdBuffer == null)
            {
                return false;
            }

            if (sourceIdResolver == null)
            {
                sourceIdResolver = asc => GASServices.ReplicationResolver.GetAbilitySystemNetworkId(asc);
            }

            snapshot = TargetDataNetworkCodec.CaptureNonAlloc(data, targetIdResolver, targetIdBuffer, sourceIdResolver);
            return snapshot.Type != TargetDataNetworkType.None;
        }

        public bool TrySendTargetDataToServer(
            GameplayAbilitySpec spec,
            TargetData data,
            Func<GameObject, int> targetIdResolver,
            Func<AbilitySystemComponent, int> sourceIdResolver = null)
        {
            if (spec == null || data == null || targetIdResolver == null)
            {
                return false;
            }

            var targetDataBridge = GASServices.NetworkBridge as IGASTargetDataNetworkBridge;
            if (targetDataBridge == null)
            {
                return false;
            }

            if (!TryBuildTargetDataNetworkData(data, targetIdResolver, out var snapshot, sourceIdResolver))
            {
                return false;
            }

            var predictionKey = data.PredictionKey.IsValid ? data.PredictionKey : currentPredictionKey;
            if (predictionKey.IsValid && (!snapshot.PredictionKey.Equals(predictionKey) || snapshot.AbilitySpecHandle == 0))
            {
                snapshot = new TargetDataNetworkData(
                    snapshot.Type,
                    predictionKey,
                    snapshot.AbilitySpecHandle != 0 ? snapshot.AbilitySpecHandle : spec.Handle,
                    snapshot.SourceAscNetId,
                    snapshot.CreatedFrame,
                    snapshot.TargetObjectIds,
                    snapshot.TargetObjectCount,
                    snapshot.HitPoint,
                    snapshot.HitNormal,
                    snapshot.HitDistance);
            }

            targetDataBridge.ClientSendTargetData(this, spec.Handle, predictionKey, in snapshot);
            return true;
        }

        public bool TrySendTargetDataToServerNonAlloc(
            GameplayAbilitySpec spec,
            TargetData data,
            Func<GameObject, int> targetIdResolver,
            int[] targetIdBuffer,
            Func<AbilitySystemComponent, int> sourceIdResolver = null)
        {
            if (spec == null || data == null || targetIdResolver == null || targetIdBuffer == null)
            {
                return false;
            }

            var targetDataBridge = GASServices.NetworkBridge as IGASTargetDataNetworkBridge;
            if (targetDataBridge == null)
            {
                return false;
            }

            if (!TryBuildTargetDataNetworkDataNonAlloc(data, targetIdResolver, targetIdBuffer, out var snapshot, sourceIdResolver))
            {
                return false;
            }

            var predictionKey = data.PredictionKey.IsValid ? data.PredictionKey : currentPredictionKey;
            if (predictionKey.IsValid && (!snapshot.PredictionKey.Equals(predictionKey) || snapshot.AbilitySpecHandle == 0))
            {
                snapshot = new TargetDataNetworkData(
                    snapshot.Type,
                    predictionKey,
                    snapshot.AbilitySpecHandle != 0 ? snapshot.AbilitySpecHandle : spec.Handle,
                    snapshot.SourceAscNetId,
                    snapshot.CreatedFrame,
                    snapshot.TargetObjectIds,
                    snapshot.TargetObjectCount,
                    snapshot.HitPoint,
                    snapshot.HitNormal,
                    snapshot.HitDistance);
            }

            targetDataBridge.ClientSendTargetData(this, spec.Handle, predictionKey, in snapshot);
            return true;
        }

        public bool TryReceiveTargetDataFromClient(
            int specHandle,
            GASPredictionKey predictionKey,
            in TargetDataNetworkData snapshot,
            Func<int, GameObject> targetResolver,
            float maxRange,
            int maxAgeFrames,
            out GameplayAbilitySpec spec,
            out TargetData targetData,
            out TargetDataValidationResult result)
        {
            spec = FindSpecByHandle(specHandle);
            targetData = null;

            if (spec == null)
            {
                result = TargetDataValidationResult.AbilitySpecMismatch;
                return false;
            }

            targetData = TargetDataNetworkCodec.Create(in snapshot, targetResolver);
            if (targetData == null)
            {
                result = TargetDataValidationResult.MissingData;
                return false;
            }

            var effectiveKey = predictionKey.IsValid ? predictionKey : snapshot.PredictionKey;
            if (!TryValidateTargetData(targetData, spec, effectiveKey, maxRange, maxAgeFrames, out result))
            {
                targetData.ReturnToPool();
                targetData = null;
                return false;
            }

            return true;
        }

        public void ServerReceiveTargetData(
            int specHandle,
            GASPredictionKey predictionKey,
            in TargetDataNetworkData snapshot,
            Func<int, GameObject> targetResolver,
            float maxRange,
            int maxAgeFrames,
            out GameplayAbilitySpec spec,
            out TargetData targetData,
            out TargetDataValidationResult result)
        {
            AssertRuntimeThread();
            if (TryReceiveTargetDataFromClient(
                specHandle,
                predictionKey,
                in snapshot,
                targetResolver,
                maxRange,
                maxAgeFrames,
                out spec,
                out targetData,
                out result))
            {
                var effectiveKey = predictionKey.IsValid ? predictionKey : snapshot.PredictionKey;
                var targetDataBridge = GASServices.NetworkBridge as IGASTargetDataNetworkBridge;
                targetDataBridge?.ServerConfirmTargetData(this, specHandle, effectiveKey);
                return;
            }

            var rejectionKey = predictionKey.IsValid ? predictionKey : snapshot.PredictionKey;
            var rejectBridge = GASServices.NetworkBridge as IGASTargetDataNetworkBridge;
            rejectBridge?.ServerRejectTargetData(this, specHandle, rejectionKey, result);
        }

        public void ClientReceiveTargetDataAccepted(int specHandle, GASPredictionKey predictionKey)
        {
            AssertRuntimeThread();
            var spec = FindSpecByHandle(specHandle);
            if (GASTrace.Enabled)
            {
                GASTrace.Record(GASTraceEventType.TargetDataAccepted, this, spec?.GetPrimaryInstance(), decision: GASTraceDecision.Success, abilitySpecHandle: specHandle, predictionKey: predictionKey, level: spec?.Level ?? 0);
            }
            spec?.GetPrimaryInstance()?.AcceptTasksForPredictionKey(predictionKey);
            GameplayCueManager.Default.AcceptPredictedCues(this, predictionKey);
        }

        public void ClientReceiveTargetDataRejected(int specHandle, GASPredictionKey predictionKey, TargetDataValidationResult reason)
        {
            AssertRuntimeThread();
            var spec = FindSpecByHandle(specHandle);
            if (GASTrace.Enabled)
            {
                GASTrace.Record(GASTraceEventType.TargetDataRejected, this, spec?.GetPrimaryInstance(), decision: GASTraceDecision.Rejected, reason: GASTraceReason.TargetDataValidation, abilitySpecHandle: specHandle, predictionKey: predictionKey, level: spec?.Level ?? 0);
            }
            spec?.GetPrimaryInstance()?.CancelTasksForPredictionKey(predictionKey);
            GameplayCueManager.Default.RemovePredictedCues(this, predictionKey).Forget();
        }

        public bool TryValidateTargetData(
            TargetData data,
            GameplayAbilitySpec spec,
            GASPredictionKey predictionKey,
            float maxRange,
            int maxAgeFrames,
            out TargetDataValidationResult result)
        {
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

            if (maxAgeFrames > 0 && data.CreatedFrame > 0 && Time.frameCount - data.CreatedFrame > maxAgeFrames)
            {
                result = TargetDataValidationResult.TooOld;
                return false;
            }

            if (maxRange > 0f && data is GameplayAbilityTargetData_ActorArray actorTargets)
            {
                var sourceObject = AvatarGameObject;
                if (sourceObject == null)
                {
                    result = TargetDataValidationResult.InvalidTarget;
                    return false;
                }

                float maxRangeSq = maxRange * maxRange;
                var sourcePosition = sourceObject.transform.position;
                for (int i = 0; i < actorTargets.Actors.Count; i++)
                {
                    var target = actorTargets.Actors[i];
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

            result = TargetDataValidationResult.Valid;
            return true;
        }

        private bool ApplyAuthoritativeEffectReplication(in GASEffectReplicationData data, bool allowCreate)
        {
            var resolver = GASServices.ReplicationResolver;
            var existing = FindActiveEffectByNetworkId(data.NetworkId);

            GameplayEffect effectDef = existing?.Spec?.Def;
            if (effectDef == null)
            {
                effectDef = resolver.ResolveGameplayEffectDefinition(data.EffectDefId) as GameplayEffect;
            }

            if (effectDef == null)
            {
                int effectDefId = data.EffectDefId;
                GASLog.Warning(sb => sb.Append("Unable to resolve replicated GameplayEffect definition for id ")
                    .Append(effectDefId).Append(" on ASC '").Append(OwnerActor).Append("'."));
                return false;
            }

            AbilitySystemComponent source = null;
            if (data.SourceAscNetId != 0 && resolver.TryResolveAbilitySystem(data.SourceAscNetId, out var resolvedSource))
            {
                source = resolvedSource as AbilitySystemComponent;
            }

            if (existing == null)
            {
                existing = FindPredictedEffectForReconcile(effectDef, source, data.PredictionKey);
            }

            bool created = false;
            if (existing == null)
            {
                if (!allowCreate)
                {
                    return false;
                }

                existing = CreateReplicatedActiveEffect(effectDef, source, data);
                if (existing == null)
                {
                    return false;
                }
                created = true;
            }

            if (data.NetworkId != 0)
            {
                SetActiveEffectNetworkId(existing, data.NetworkId);
            }
            TryApplyReplicatedEffectUpdateRaw(
                existing,
                data.Level,
                data.StackCount,
                data.DurationRaw,
                data.TimeRemainingRaw,
                data.PeriodTimeRemainingRaw,
                data.SetByCallerTags,
                data.SetByCallerValuesRaw,
                data.SetByCallerCount);

            if (data.PredictionKey.IsValid)
            {
                RemovePendingPredictedEffects(data.PredictionKey);
            }

            if (dirtyAttributes.Count > 0)
            {
                RecalculateDirtyAttributes();
            }

            return created || existing != null;
        }

        private ActiveGameplayEffect CreateReplicatedActiveEffect(GameplayEffect effectDef, AbilitySystemComponent source, in GASEffectReplicationData data)
        {
            var context = MakeEffectContext();
            context.PredictionKey = data.PredictionKey;
            if (context is GameplayEffectContext runtimeContext)
            {
                runtimeContext.AddInstigator(source, null);
            }

            var spec = GameplayEffectSpec.Create(effectDef, source, context, data.Level);
            spec.ApplyReplicatedStateRaw(data.Level, data.DurationRaw, data.SetByCallerTags, data.SetByCallerValuesRaw, data.SetByCallerCount);

            using (new NetworkReplicationScope(this))
            {
                int previousCount = activeEffects.Count;
                ApplyGameplayEffectSpecToSelf(spec);

                if (activeEffects.Count > previousCount)
                {
                    return activeEffects[activeEffects.Count - 1];
                }

                return FindPredictedEffectForReconcile(effectDef, source, data.PredictionKey);
            }
        }

        private void RemovePendingPredictedEffects(GASPredictionKey predictionKey)
        {
            PredictionManager.RemovePendingPredictedEffects(predictionKey);
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

        private GASPredictionRollbackFlags CancelPredictedExecution(GASPredictionWindowData window, GASPredictionKey predictionKey)
        {
            var flags = GASPredictionRollbackFlags.None;
            var spec = window.AbilitySpecHandle != 0 ? FindSpecByHandle(window.AbilitySpecHandle) : null;
            var ability = spec?.GetPrimaryInstance();
            if (ability != null)
            {
                if (window.PredictedAbilityTaskCount > 0)
                {
                    flags |= GASPredictionRollbackFlags.AbilityTasks;
                }

                ability.CancelTasksForPredictionKey(predictionKey);
            }

            if (window.PredictedGameplayCueCount > 0)
            {
                flags |= GASPredictionRollbackFlags.GameplayCues;
            }

            GameplayCueManager.Default.RemovePredictedCues(this, predictionKey).Forget();
            if (ability != null)
            {
                ability.CancelAbility();
                flags |= GASPredictionRollbackFlags.AbilityCancelled;
            }

            return flags;
        }
        private void AddPredictedAttributeSnapshot(GASPredictionKey predictionKey, GameplayAttribute attribute)
        {
            for (int i = predictedAttributeSnapshots.Count - 1; i >= 0; i--)
            {
                var snapshot = predictedAttributeSnapshots[i];
                if (snapshot.key.Equals(predictionKey) && snapshot.attr == attribute)
                {
                    return;
                }
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

        private readonly struct NetworkReplicationScope : IDisposable
        {
            private readonly AbilitySystemComponent asc;

            public NetworkReplicationScope(AbilitySystemComponent asc)
            {
                this.asc = asc;
                asc.networkReplicationScopeDepth++;
            }

            public void Dispose()
            {
                asc.networkReplicationScopeDepth--;
            }
        }

        private void ActivateAbilityInternal(GameplayAbilitySpec spec, GameplayAbilityActivationInfo activationInfo)
        {
            var ability = spec.GetPrimaryInstance();

            // Handle instancing
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

            // Set prediction key for this activation scope
            this.currentPredictionKey = activationInfo.PredictionKey;

            spec.IsActive = true;
            AddTickingAbilitySpec(spec);
            MarkGrantedAbilitiesDirty();

            // UE5: Cancel abilities with matching tags
            if (ability.CancelAbilitiesWithTag != null && !ability.CancelAbilitiesWithTag.IsEmpty)
            {
                CancelAbilitiesWithTags(ability.CancelAbilitiesWithTag);
            }

            // Apply ActivationOwnedTags --incremental, no full rebuild
            if (ability.ActivationOwnedTags != null && !ability.ActivationOwnedTags.IsEmpty)
            {
                looseTags.AddTags(ability.ActivationOwnedTags);
                CombinedTags.AddTags(ability.ActivationOwnedTags);
            }

            ability.SetCurrentActivationInfo(activationInfo);
            ability.ActivateAbility(cachedActorInfo, spec, activationInfo);
            if (GASTrace.Enabled)
            {
                GASTrace.Record(GASTraceEventType.AbilityActivated, this, ability, decision: GASTraceDecision.Success, abilitySpecHandle: spec.Handle, predictionKey: activationInfo.PredictionKey, level: spec.Level);
            }

            OnAbilityActivated?.Invoke(ability);

            // Clear prediction key after atomic activation
            this.currentPredictionKey = default;
        }

        private void ClientActivateAbilitySucceed(GameplayAbilitySpec spec, GASPredictionKey predictionKey)
        {
            if (!predictionKey.IsValid) return;
            if (!ConfirmPredictionWindow(predictionKey))
            {
                return;
            }

            AcceptCorePrediction(predictionKey);
            if (GASTrace.Enabled)
            {
                GASTrace.Record(GASTraceEventType.PredictionConfirmed, this, spec?.GetPrimaryInstance(), decision: GASTraceDecision.Success, abilitySpecHandle: spec?.Handle ?? 0, predictionKey: predictionKey);
            }
            RemovePendingPredictedEffects(predictionKey);
            GameplayCueManager.Default.AcceptPredictedCues(this, predictionKey);
            spec?.GetPrimaryInstance()?.AcceptTasksForPredictionKey(predictionKey);
            //  Prediction confirmed --discard instant-effect attribute snapshots; changes are now authoritative.
            RemovePredictedAttributeSnapshots(predictionKey, false);
        }

        private void ClientActivateAbilityFailed(GameplayAbilitySpec spec, GASPredictionKey predictionKey)
        {
            if (!predictionKey.IsValid) return;
            bool closedPredictionWindow = RejectPredictionWindow(predictionKey);
            if (closedPredictionWindow)
            {
                RejectCorePrediction(predictionKey);
            }

            if (GASTrace.Enabled)
            {
                GASTrace.Record(GASTraceEventType.PredictionRejected, this, spec?.GetPrimaryInstance(), decision: closedPredictionWindow ? GASTraceDecision.RolledBack : GASTraceDecision.Rejected, reason: GASTraceReason.ServerRejected, abilitySpecHandle: spec?.Handle ?? 0, predictionKey: predictionKey);
            }

            // P0+ Use zero-GC StringBuilder overload for Warning (avoids string interpolation alloc in Release)
            GASLog.Warning(sb => sb.Append("Client prediction failed for ability '").Append(spec?.Ability?.Name ?? "<missing-spec>")
                .Append("' with key ").Append(predictionKey.Key).Append(closedPredictionWindow ? ". Rolling back." : ". Ignoring stale rejection."));
        }

        internal void NotifyAbilityCommitted(GameplayAbility ability)
        {
            if (GASTrace.Enabled)
            {
                GASTrace.Record(GASTraceEventType.AbilityCommitted, this, ability, decision: GASTraceDecision.Success, abilitySpecHandle: ability?.Spec?.Handle ?? 0, predictionKey: ability?.CurrentActivationInfo.PredictionKey ?? default, level: ability?.Spec?.Level ?? 0);
            }
            OnAbilityCommitted?.Invoke(ability);
        }

        internal void OnAbilityEnded(GameplayAbility ability)
        {
            if (ability.Spec != null)
            {
                if (ability.Spec.IsActive)
                {
                    ability.Spec.IsActive = false;
                    MarkGrantedAbilitiesDirty();
                    RemoveTickingAbilitySpec(ability.Spec);

                    // Remove ActivationOwnedTags when ability ends --incremental, no full rebuild
                    if (ability.ActivationOwnedTags != null && !ability.ActivationOwnedTags.IsEmpty)
                    {
                        looseTags.RemoveTags(ability.ActivationOwnedTags);
                        CombinedTags.RemoveTags(ability.ActivationOwnedTags);
                    }

                    // UE5: RemoveGameplayEffectsAfterAbilityEnds --remove effects this ability applied
                    if (ActiveEffectContainer.TryGetAbilityAppliedEffects(ability, out var appliedEffects))
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
                            using (Pools.ListPool<ActiveGameplayEffect>.Get(out var removed))
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
                                    OnEffectRemoved(removed[i], true);
                            }
                        }

                        abilityAppliedEffectRemovalScratch.Clear();
                    }
                }

                // This ensures that flags like 'isEnding' are ready for the next activation.
                ability.InternalOnEndAbility();

                OnAbilityEndedEvent?.Invoke(ability);
                if (GASTrace.Enabled)
                {
                    GASTrace.Record(GASTraceEventType.AbilityEnded, this, ability, decision: GASTraceDecision.Success, abilitySpecHandle: ability.Spec.Handle, level: ability.Spec.Level);
                }

                if (ability.InstancingPolicy == EGameplayAbilityInstancingPolicy.InstancedPerExecution)
                {
                    ability.Spec.ClearInstance();
                }
            }
        }

        // --- Gameplay Effect Application ---
        public IGameplayEffectContext MakeEffectContext()
        {
            return EffectContextFactory.Create();
        }

        public void ApplyGameplayEffectSpecToSelf(GameplayEffectSpec spec)
        {
            AssertRuntimeThread();
            spec.SetTarget(this);
            if (GASTrace.Enabled)
            {
                GASTrace.Record(GASTraceEventType.EffectApplyAttempt, this, effect: spec.Def, source: spec.Source, level: spec.Level);
            }

            // If we are in a prediction scope, tag the spec's context
            if (currentPredictionKey.IsValid)
            {
                spec.Context.PredictionKey = currentPredictionKey;
            }

            if (!CanApplyEffect(spec))
            {
                if (GASTrace.Enabled)
                {
                    GASTrace.Record(GASTraceEventType.EffectApplyBlocked, this, effect: spec.Def, decision: GASTraceDecision.Blocked, reason: GASTraceReason.ApplicationBlockedTags, source: spec.Source, level: spec.Level);
                }
                spec.ReturnToPool();
                return;
            }

            RemoveEffectsWithTags(spec.Def.RemoveGameplayEffectsWithTags);

            if (spec.Def.DurationPolicy == EDurationPolicy.Instant)
            {
                if (IsCoreStateEnabled)
                {
                    var coreEffectSpec = BuildCoreEffectSpec(spec);
                    core.ApplyGameplayEffectSpecToSelf(in coreEffectSpec);
                }

                ExecuteInstantEffect(spec);
                DispatchGameplayCues(spec, EGameplayCueEvent.Executed);
                if (GASTrace.Enabled)
                {
                    GASTrace.Record(GASTraceEventType.EffectExecuted, this, effect: spec.Def, decision: GASTraceDecision.Success, source: spec.Source, predictionKey: spec.Context.PredictionKey, level: spec.Level);
                }
                spec.ReturnToPool();
                return;
            }

            if (HandleStacking(spec))
            {
                spec.ReturnToPool();
                return;
            }

            var newActiveEffect = ActiveGameplayEffect.Create(spec);
            ActiveEffectContainer.AddEffect(newActiveEffect);
            RegisterActiveEffectInCore(newActiveEffect);

            if (currentPredictionKey.IsValid)
            {
                PredictionManager.AddPendingPredictedEffect(newActiveEffect);
                IncrementPredictionWindowEffectCount(currentPredictionKey);
            }

            //  Track effects for RemoveGameplayEffectsAfterAbilityEnds
            if (spec.Def.RemoveGameplayEffectsAfterAbilityEnds && spec.Context?.AbilityInstance != null)
            {
                ActiveEffectContainer.TrackAbilityAppliedEffect(spec.Context.AbilityInstance, newActiveEffect, RentAbilityAppliedEffectList);
            }

            GASLog.Info(sb => sb.Append(OwnerActor).Append(" Apply GameplayEffect '").Append(spec.Def.Name).Append("' to self."));
            OnEffectApplied(newActiveEffect);
            if (GASTrace.Enabled)
            {
                GASTrace.Record(GASTraceEventType.EffectApplied, this, effect: spec.Def, decision: GASTraceDecision.Success, source: spec.Source, predictionKey: spec.Context.PredictionKey, level: spec.Level, stackCount: newActiveEffect.StackCount, networkId: newActiveEffect.NetworkId);
            }
            MarkActiveEffectsDirty();

            if (spec.Def.Period <= 0)
            {
                MarkAttributesDirtyFromEffect(newActiveEffect);
            }
        }

        public void RemoveActiveEffectsWithGrantedTags(GameplayTagContainer tags)
        {
            if (tags == null || tags.IsEmpty) return;

            using (Pools.ListPool<ActiveGameplayEffect>.Get(out var removedEffects))
            {
                for (int i = activeEffects.Count - 1; i >= 0; i--)
                {
                    var effect = activeEffects[i];
                    bool shouldRemove = effect.Spec.Def.GrantedTags.HasAny(tags) || effect.Spec.Def.AssetTags.HasAny(tags);
                    if (shouldRemove)
                    {
                        RemoveActiveEffectAtIndex(i);
                        removedEffects.Add(effect);
                    }
                }

                for (int i = 0; i < removedEffects.Count; i++)
                {
                    OnEffectRemoved(removedEffects[i], true);
                }
            }
        }

        private bool CanApplyEffect(GameplayEffectSpec spec)
        {
            // Check immunity - block effects whose tags match any immunity tag
            if (!ImmunityTags.IsEmpty)
            {
                if (spec.Def.AssetTagsSnapshot.HasAny(ImmunityTags) || spec.Def.GrantedTagsSnapshot.HasAny(ImmunityTags))
                {
                    GASLog.Debug(sb => sb.Append("Apply GameplayEffect '").Append(spec.Def.Name).Append("' blocked: target has immunity to effect's tags."));
                    return false;
                }
                // Also check dynamic tags on the spec instance
                if (!spec.DynamicAssetTags.IsEmpty && spec.DynamicAssetTags.HasAny(ImmunityTags))
                {
                    GASLog.Debug(sb => sb.Append("Apply GameplayEffect '").Append(spec.Def.Name).Append("' blocked: target has immunity to effect's dynamic asset tags."));
                    return false;
                }
                if (!spec.DynamicGrantedTags.IsEmpty && spec.DynamicGrantedTags.HasAny(ImmunityTags))
                {
                    GASLog.Debug(sb => sb.Append("Apply GameplayEffect '").Append(spec.Def.Name).Append("' blocked: target has immunity to effect's dynamic granted tags."));
                    return false;
                }
            }

            if (!HasAllMatchingGameplayTags(spec.Def.ApplicationRequiredTagsSnapshot))
            {
                GASLog.Debug(sb => sb.Append("Apply GameplayEffect '").Append(spec.Def.Name).Append("' failed: does not meet application tag requirements (Required)."));
                return false;
            }
            if (HasAnyMatchingGameplayTags(spec.Def.ApplicationForbiddenTagsSnapshot))
            {
                GASLog.Debug(sb => sb.Append("Apply GameplayEffect '").Append(spec.Def.Name).Append("' failed: does not meet application tag requirements (Ignored)."));
                return false;
            }

            //  Custom Application Requirements (UE5: UGameplayEffectCustomApplicationRequirement)
            var requirements = spec.Def.CustomApplicationRequirements;
            for (int i = 0; i < requirements.Count; i++)
            {
                if (!requirements[i].CanApplyGameplayEffect(spec, this))
                {
                    GASLog.Debug(sb => sb.Append("Apply GameplayEffect '").Append(spec.Def.Name).Append("' blocked by custom application requirement."));
                    return false;
                }
            }

            return true;
        }

        internal void ExecuteInstantEffect(GameplayEffectSpec spec)
        {
            // This logic is mostly server-authoritative or for local effects.
            // Prediction of instant damage is complex and often avoided.
            if (spec.Def.Execution != null)
            {
                if (executionOutputScratchPad == null) executionOutputScratchPad = new List<ModifierInfo>(16);
                executionOutputScratchPad.Clear();
                spec.Def.Execution.Execute(spec, ref executionOutputScratchPad);
                for (int i = 0; i < executionOutputScratchPad.Count; i++)
                {
                    var modInfo = executionOutputScratchPad[i];
                    var attribute = GetAttribute(modInfo.AttributeName);
                    if (attribute != null)
                    {
                        ApplyModifier(spec, attribute, modInfo, GASFixedValue.FromFloat(modInfo.Magnitude.GetValueAtLevel(spec.Level)).RawValue, true);
                    }
                }
            }

            var modifiers = spec.Def.Modifiers;
            for (int i = 0; i < modifiers.Count; i++)
            {
                var mod = modifiers[i];
                GameplayAttribute attribute = null;
                if (i < spec.TargetAttributes.Length) attribute = spec.TargetAttributes[i];
                if (attribute == null) attribute = GetAttribute(mod.AttributeName);

                if (attribute != null)
                {
                    //  Modify the base value, so isFromExecution is true
                    ApplyModifier(spec, attribute, mod, spec.GetCalculatedMagnitudeRaw(i), true);
                }
            }

            // CLogger.LogInfo($"{OwnerActor} Execute Instant GameplayEffect '{spec.Def.Name}' on self.");
        }

        // --- Tick and State Management ---
        public void Tick(float deltaTime, bool isServer)
        {
            AssertRuntimeThread();
            for (int i = tickingAbilities.Count - 1; i >= 0; i--)
            {
                var spec = tickingAbilities[i];
                spec.GetPrimaryInstance()?.TickTasks(deltaTime);
            }

            if (!isServer && PredictionManager.WindowCount > 0)
            {
                TickPredictionWindows(Time.frameCount);
            }

            // Bug fix: recalculate dirty attributes (and inhibition) BEFORE ticking effects.
            // Tag changes during ability tasks (above) may have marked attributes dirty.
            // Running RecalculateDirtyAttributes here ensures IsInhibited is current before
            // any periodic effect execution fires via ActiveGameplayEffect.Tick().
            if (dirtyAttributes.Count > 0)
            {
                RecalculateDirtyAttributes();
            }

            // Server is authoritative over effect duration
            if (isServer)
            {
                for (int i = activeEffects.Count - 1; i >= 0; i--)
                {
                    var effect = activeEffects[i];
                    if (effect.Tick(deltaTime, this))
                    {
                        //  Stack Expiration Policy
                        HandleEffectExpiration(effect, i);
                    }
                }
            }

            // Second pass: recalculate attributes dirtied by periodic effect executions this tick.
            if (dirtyAttributes.Count > 0)
            {
                RecalculateDirtyAttributes();
            }

            // Attribute changes are collected into the pending delta buffer. Network adapters
            // flush them through ServerSendPendingStateDelta after interest management selects targets.
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
                        MarkAttributesDirtyFromEffect(effect);
                        ReplicateEffectUpdate(effect);
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
            ActiveEffectContainer.RemoveAtSwapBack(index);
        }

        private void RebuildActiveEffectNetworkIdIndex()
        {
            ActiveEffectContainer.RebuildNetworkIdIndex();
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
        /// P0 Optimization: OngoingTagRequirements check is cached per-effect (once per effect,
        /// not once per modifier), and periodic-only effects are skipped early.
        /// </summary>
        private void RecalculateDirtyAttributes()
        {
            for (int d = 0; d < dirtyAttributes.Count; d++)
            {
                var attr = dirtyAttributes[d];
                attr.IsDirty = false;

                var baseValue = attr.OwningSet.GetBaseFixedValue(attr);
                var additive = GASFixedValue.Zero;
                var multiplicativeBiasSum = GASFixedValue.Zero;
                var divisionBiasSum = GASFixedValue.Zero;
                var overrideValue = GASFixedValue.Zero;
                var one = GASFixedValue.One;
                bool hasOverride = false;

                var affectingEffects = attr.AffectingEffects;
                for (int i = 0; i < affectingEffects.Count; i++)
                {
                    var effect = affectingEffects[i];
                    var def = effect.Spec.Def;

                    // Skip periodic-only effects (they execute instant effects on tick, not continuous modifiers)
                    if (def.Period > 0) continue;

                    //  Cache OngoingTagRequirements check per-effect, not per-modifier
                    bool isInhibited = !def.OngoingTagRequirements.IsEmpty && !MeetsTagRequirements(def.OngoingRequiredTagsSnapshot, def.OngoingForbiddenTagsSnapshot);

                    // UE5: Track inhibition state changes and fire OnInhibitionChanged
                    if (isInhibited != effect.IsInhibited)
                    {
                        effect.IsInhibited = isInhibited;
                        effect.NotifyInhibitionChanged(isInhibited);
                    }

                    if (isInhibited)
                    {
                        continue;
                    }

                    var modifiers = def.Modifiers;
                    int stackCount = effect.StackCount;
                    for (int m = 0; m < modifiers.Count; m++)
                    {
                        if (effect.Spec.TargetAttributes[m] != attr) continue;

                        // For non-snapshotted custom calculations, recalculate magnitude live
                        long baseMagnitudeRaw;
                        var mod = modifiers[m];
                        if (mod.SnapshotPolicy == EGameplayEffectAttributeCaptureSnapshot.NotSnapshot && mod.CustomCalculation != null)
                        {
                            effect.Spec.SetCalculatedMagnitude(m, mod.CustomCalculation.CalculateMagnitude(effect.Spec));
                            baseMagnitudeRaw = effect.Spec.GetCalculatedMagnitudeRaw(m);
                        }
                        else
                        {
                            baseMagnitudeRaw = effect.Spec.GetCalculatedMagnitudeRaw(m);
                        }

                        var magnitude = GASFixedValue.FromRaw(baseMagnitudeRaw) * GASFixedValue.FromInt(stackCount);
                        switch (modifiers[m].Operation)
                        {
                            case EAttributeModifierOperation.Add:
                                additive += magnitude;
                                break;
                            case EAttributeModifierOperation.Multiply:
                                multiplicativeBiasSum += magnitude - one;
                                break;
                            case EAttributeModifierOperation.Division:
                                if (magnitude.RawValue != 0) divisionBiasSum += magnitude - one;
                                break;
                            case EAttributeModifierOperation.Override:
                                overrideValue = magnitude;
                                hasOverride = true;
                                break;
                        }
                    }
                }

                var multiplicative = one + multiplicativeBiasSum;
                var division = one + divisionBiasSum;
                if (division.RawValue == 0) division = one;

                var finalValue = hasOverride ? overrideValue : ((baseValue + additive) * multiplicative / division);

                attr.OwningSet.PreAttributeChange(attr, ref finalValue);
                attr.OwningSet.SetCurrentValue(attr, finalValue);
            }

            dirtyAttributes.Clear();
        }

        private void OnEffectApplied(ActiveGameplayEffect effect)
        {
            //  Incremental tag update --add to both containers directly, no full rebuild
            if (!effect.Spec.Def.GrantedTags.IsEmpty)
            {
                fromEffectsTags.AddTags(effect.Spec.Def.GrantedTags);
                CombinedTags.AddTags(effect.Spec.Def.GrantedTags);
            }
            // UE5: DynamicGrantedTags --spec-instance-level tags
            if (!effect.Spec.DynamicGrantedTags.IsEmpty)
            {
                fromEffectsTags.AddTags(effect.Spec.DynamicGrantedTags);
                CombinedTags.AddTags(effect.Spec.DynamicGrantedTags);
            }

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
            if (effect.Spec.Def.GrantedAbilities.Count > 0)
            {
                var grantedAbilities = effect.Spec.Def.GrantedAbilities;
                for (int i = 0; i < grantedAbilities.Count; i++)
                {
                    var newSpec = GrantAbility(grantedAbilities[i], effect.Spec.Level);
                    RegisterAbilityGrantedByEffect(effect, newSpec);
                }
            }

            //  SuppressGameplayCues --skip all cue handling when suppressed
            if (!SuppressLocalGameplayCueDispatch)
            {
                DispatchGameplayCues(effect.Spec, EGameplayCueEvent.OnActive);
            }

            //  Maintain grantedTag runtime index for O(1) cooldown and tag effect queries.
            UpdateGrantedTagIndex_Applied(effect);

            ReplicateEffectApplied(effect);

            OnGameplayEffectAppliedToSelf?.Invoke(effect);
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

                int lastIndex = effects.Count - 1;
                if (i != lastIndex)
                {
                    effects[i] = effects[lastIndex];
                }
                effects.RemoveAt(lastIndex);
                return;
            }
        }

        private void OnEffectRemoved(ActiveGameplayEffect effect, bool markDirty)
        {
            RemoveActiveEffectFromCore(effect);
            ActiveEffectContainer.UntrackAppliedEffectFromAbilities(effect, ReturnAbilityAppliedEffectList);

            //  Incremental tag update --remove from both containers directly, no full rebuild
            if (!effect.Spec.Def.GrantedTags.IsEmpty)
            {
                fromEffectsTags.RemoveTags(effect.Spec.Def.GrantedTags);
                CombinedTags.RemoveTags(effect.Spec.Def.GrantedTags);
            }
            // UE5: DynamicGrantedTags --spec-instance-level tags
            if (!effect.Spec.DynamicGrantedTags.IsEmpty)
            {
                fromEffectsTags.RemoveTags(effect.Spec.DynamicGrantedTags);
                CombinedTags.RemoveTags(effect.Spec.DynamicGrantedTags);
            }

            //  Remove from grantedTag index before the effect's tag data is invalidated by ReturnToPool.
            UpdateGrantedTagIndex_Removed(effect);

            // Remove from attribute's internal list
            var modifiers = effect.Spec.Def.Modifiers;
            for (int i = 0; i < modifiers.Count; i++)
            {
                var attribute = effect.Spec.TargetAttributes[i];
                if (attribute == null) attribute = GetAttribute(modifiers[i].AttributeName);

                if (attribute != null && !HasEarlierModifierForAttribute(effect, attribute, i))
                {
                    RemoveAffectingEffect(attribute, effect);
                }
            }

            if (AbilitySpecs.TryGetGrantedSpecs(effect, out var grantedSpecs))
            {
                while (grantedSpecs.Count > 0)
                {
                    ClearAbility(grantedSpecs[grantedSpecs.Count - 1]);
                }
            }

            //  SuppressGameplayCues --skip cue handling when suppressed
            if (!SuppressLocalGameplayCueDispatch &&
                !effect.Spec.Def.SuppressGameplayCues &&
                effect.Spec.Def.DurationPolicy != EDurationPolicy.Instant && !effect.Spec.Def.GameplayCues.IsEmpty)
            {
                DispatchGameplayCues(effect.Spec, EGameplayCueEvent.Removed);
            }

            OnGameplayEffectRemovedFromSelf?.Invoke(effect);

            if (markDirty) MarkAttributesDirtyFromEffect(effect);
            MarkActiveEffectsDirty();

            // Delta tracking: record this NetworkId as removed so the next state delta includes it.
            ReplicationStateBuilder.TrackRemovedEffectNetworkId(effect.NetworkId);

            // Network: if server, notify clients of this removal.
            var bridge = GASServices.NetworkBridge;
            if (bridge.IsServer && ReplicationMode != EReplicationMode.Minimal && effect.NetworkId != 0)
                bridge.ServerReplicateEffectRemoved(this, effect.NetworkId);

            effect.ReturnToPool();
        }

        private void DispatchGameplayCues(GameplayEffectSpec spec, EGameplayCueEvent eventType)
        {
            CueDispatcher.DispatchGameplayCues(spec, eventType);
        }

        private void ReplicateEffectApplied(ActiveGameplayEffect effect)
        {
            var bridge = GASServices.NetworkBridge;
            if (!bridge.IsServer || ReplicationMode == EReplicationMode.Minimal)
            {
                return;
            }

            if (effect.NetworkId == 0)
            {
                SetActiveEffectNetworkId(effect, System.Threading.Interlocked.Increment(ref s_NextEffectNetworkId));
            }
            else
            {
                SetActiveEffectNetworkId(effect, effect.NetworkId);
            }

            var repData = CreateEffectReplicationData(effect);
            bridge.ServerReplicateEffectApplied(this, repData);
        }

        private void ReplicateEffectUpdate(ActiveGameplayEffect effect)
        {
            var bridge = GASServices.NetworkBridge;
            if (!bridge.IsServer || ReplicationMode == EReplicationMode.Minimal || effect == null || effect.IsExpired || effect.NetworkId == 0)
            {
                return;
            }

            var repData = CreateEffectReplicationData(effect);
            bridge.ServerReplicateEffectUpdated(this, repData);
        }

        private GASEffectReplicationData CreateEffectReplicationData(ActiveGameplayEffect effect)
        {
            var resolver = GASServices.ReplicationResolver;
            GameplayTag[] setByCallerTags;
            long[] setByCallerValuesRaw;
            int setByCallerCount = effect.Spec.SetByCallerTagMagnitudeCount;

            if (setByCallerCount <= 0)
            {
                setByCallerTags = Array.Empty<GameplayTag>();
                setByCallerValuesRaw = Array.Empty<long>();
                setByCallerCount = 0;
            }
            else
            {
                EnsureEffectReplicationSetByCallerCapacity(setByCallerCount);
                setByCallerCount = effect.Spec.CopySetByCallerTagMagnitudesRaw(
                    effectReplicationSetByCallerTags,
                    effectReplicationSetByCallerValuesRaw);
                setByCallerTags = setByCallerCount > 0 ? effectReplicationSetByCallerTags : Array.Empty<GameplayTag>();
                setByCallerValuesRaw = setByCallerCount > 0 ? effectReplicationSetByCallerValuesRaw : Array.Empty<long>();
            }

            return new GASEffectReplicationData
            {
                NetworkId = effect.NetworkId,
                EffectDefId = resolver.GetGameplayEffectDefinitionId(effect.Spec.Def),
                SourceAscNetId = effect.Spec.Source != null ? resolver.GetAbilitySystemNetworkId(effect.Spec.Source) : 0,
                TargetAscNetId = resolver.GetAbilitySystemNetworkId(this),
                Level = effect.Spec.Level,
                StackCount = effect.StackCount,
                DurationRaw = effect.Spec.DurationRaw,
                TimeRemainingRaw = effect.TimeRemainingRaw,
                PeriodTimeRemainingRaw = effect.PeriodTimeRemainingRaw,
                PredictionKey = effect.Spec.Context?.PredictionKey ?? default,
                SetByCallerTags = setByCallerTags,
                SetByCallerValuesRaw = setByCallerValuesRaw,
                SetByCallerCount = setByCallerCount,
            };
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

        // --- Tag Management ---
        //  Incremental tag updates --directly add/remove from CombinedTags, no full rebuild
        public void AddLooseGameplayTag(GameplayTag tag) { looseTags.AddTag(tag); CombinedTags.AddTag(tag); }
        public void RemoveLooseGameplayTag(GameplayTag tag) { looseTags.RemoveTag(tag); CombinedTags.RemoveTag(tag); }

        #region Missing UE5 GAS Features

        /// <summary>
        /// Cancels all currently active abilities that have ANY of the specified tags.
        /// UE5: Integrated into ability activation flow via CancelAbilitiesWithTag.
        /// </summary>
        public void CancelAbilitiesWithTags(GameplayTagContainer tags)
        {
            if (tags == null || tags.IsEmpty) return;

            for (int i = activatableAbilities.Count - 1; i >= 0; i--)
            {
                var spec = activatableAbilities[i];
                if (!spec.IsActive) continue;

                var abilityTags = spec.GetPrimaryInstance()?.AbilityTags;
                if (abilityTags != null && abilityTags.HasAny(tags))
                {
                    spec.GetPrimaryInstance()?.CancelAbility();
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
            if (effect == null || effect.IsExpired) return false;
            if (effect.Spec.Def.DurationPolicy != EDurationPolicy.HasDuration) return false;

            if (!TryFindActiveEffectIndex(effect, out _))
            {
                return false;
            }

            effect.SetRemainingDuration(newDuration);
            ReplicateEffectUpdate(effect);
            return true;
        }

        public bool TryRemoveActiveEffect(ActiveGameplayEffect effect)
        {
            AssertRuntimeThread();
            if (effect == null)
            {
                return false;
            }

            if (!TryFindActiveEffectIndex(effect, out int index))
            {
                return false;
            }

            RemoveActiveEffectAtIndex(index);
            RemoveFromStackingIndex(effect);
            OnEffectRemoved(effect, true);
            return true;
        }

        public bool TryApplyActiveEffectStackChange(ActiveGameplayEffect effect, int newStackCount)
        {
            AssertRuntimeThread();
            if (effect == null || effect.IsExpired)
            {
                return false;
            }

            if (!TryFindActiveEffectIndex(effect, out _))
            {
                return false;
            }

            effect.SetReplicatedStackCount(newStackCount);
            MarkActiveEffectsDirty();
            MarkAttributesDirtyFromEffect(effect);
            ReplicateEffectUpdate(effect);
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
            if (effect == null || effect.IsExpired)
            {
                return false;
            }

            if (!TryFindActiveEffectIndex(effect, out _))
            {
                return false;
            }

            effect.ApplyReplicatedState(level, stackCount, duration, timeRemaining, periodTimeRemaining, setByCallerTags, setByCallerValues, setByCallerCount);
            MarkActiveEffectsDirty();
            MarkAttributesDirtyFromEffect(effect);
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
            if (effect == null || effect.IsExpired)
            {
                return false;
            }

            if (!TryFindActiveEffectIndex(effect, out _))
            {
                return false;
            }

            effect.ApplyReplicatedStateRaw(level, stackCount, durationRaw, timeRemainingRaw, periodTimeRemainingRaw, setByCallerTags, setByCallerValuesRaw, setByCallerCount);
            MarkActiveEffectsDirty();
            MarkAttributesDirtyFromEffect(effect);
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
        public bool HasMatchingGameplayTag(GameplayTag tag) => CombinedTags.HasTag(tag);

        /// <summary>
        /// Checks if the owner has ALL of the specified gameplay tags.
        /// UE5: HasAllMatchingGameplayTags.
        /// </summary>
        public bool HasAllMatchingGameplayTags(GameplayTagContainer tags) => tags == null || tags.IsEmpty || CombinedTags.HasAll(tags);

        public bool HasAllMatchingGameplayTags(ReadOnlyGameplayTagContainer tags)
        {
            if (tags == null || tags.IsEmpty)
            {
                return true;
            }

            var indices = tags.GetImplicitIndices();
            for (int i = 0; i < indices.Length; i++)
            {
                if (!CombinedTags.ContainsRuntimeIndex(indices[i], false))
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
        public bool HasAnyMatchingGameplayTags(GameplayTagContainer tags) => tags != null && !tags.IsEmpty && CombinedTags.HasAny(tags);

        public bool HasAnyMatchingGameplayTags(ReadOnlyGameplayTagContainer tags)
        {
            if (tags == null || tags.IsEmpty)
            {
                return false;
            }

            var indices = tags.GetImplicitIndices();
            for (int i = 0; i < indices.Length; i++)
            {
                if (CombinedTags.ContainsRuntimeIndex(indices[i], false))
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
            if (tags == null || tags.IsEmpty)
            {
                return false;
            }

            var indices = tags.GetExplicitIndices();
            for (int i = 0; i < indices.Length; i++)
            {
                if (CombinedTags.ContainsRuntimeIndex(indices[i], true))
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
        public int GetTagCount(GameplayTag tag) => CombinedTags.GetTagCount(tag);

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
                        // Register a tag event callback to fire the trigger
                        CombinedTags.RegisterTagEventCallback(trigger.TriggerTag, GameplayTagEventType.NewOrRemoved, OnTriggerTagChanged);
                        break;
                    case EAbilityTriggerSource.OwnedTagRemoved:
                        AddToTriggerMap(triggerTagRemovedAbilities, trigger.TriggerTag, spec);
                        CombinedTags.RegisterTagEventCallback(trigger.TriggerTag, GameplayTagEventType.NewOrRemoved, OnTriggerTagChanged);
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
                            TryActivateAbility(addedSpecs[i]);
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
                            TryActivateAbility(removedSpecs[i]);
                        }
                    }
                }
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
            for (int i = 0; i < activeEffects.Count; i++)
            {
                activeEffects[i].ReturnToPool();
            }

            for (int i = activatableAbilities.Count - 1; i >= 0; i--)
            {
                activatableAbilities[i].OnRemoveSpec();
                activatableAbilities[i].ReturnToPool();
            }

            activeEffects.Clear();
            activeEffectIndexByEffect.Clear();
            activeEffectByNetworkId.Clear();
            attributeSets.Clear();
            attributes.Clear();
            activatableAbilities.Clear();
            abilitySpecByHandle.Clear();
            abilitySpecIndexBySpec.Clear();
            ReturnAllGrantedAbilitySpecLists();
            tickingAbilities.Clear();
            tickingAbilityIndexBySpec.Clear();
            looseTags.Clear();
            fromEffectsTags.Clear();
            CombinedTags.Clear();
            dirtyAttributes.Clear();
            eventDelegates.Clear();
            stackingIndexByTarget.Clear();
            stackingIndexBySource.Clear();
            grantedTagIndexToEffects.Clear();
            predictedAttributeSnapshots.Clear();
            PredictionManager.Reset();
            coreSpecHandles?.Clear();
            coreActiveEffectHandles?.Clear();
            coreState?.Reset(coreEntity);
            ReturnAllAbilityAppliedEffectLists();
            ClearIdleRuntimeListPools();
            ResetRuntimeListPoolStatistics();
            abilityAppliedEffectRemovalScratch.Clear();
            triggerEventAbilities.Clear();
            triggerTagAddedAbilities.Clear();
            triggerTagRemovedAbilities.Clear();
            ReplicationStateBuilder.ResetAll();
            OnGameplayEffectAppliedToSelf = null;
            OnGameplayEffectRemovedFromSelf = null;
            OnAbilityActivated = null;
            OnAbilityEndedEvent = null;
            OnAbilityCommitted = null;
            OnPredictionWindowClosed = null;
            CombinedTags.OnAnyTagNewOrRemove -= TrackTagCountChange;
            OwnerActor = null;
            AvatarActor = null;
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
                        ApplyGameplayEffectSpecToSelf(overflowSpec);
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
            ReplicateEffectUpdate(existingEffect);
            return true;
        }
        private void RemoveEffectsWithTags(GameplayTagContainer tags)
        {
            // This is a private helper that can be called when a new effect is applied.
            RemoveActiveEffectsWithGrantedTags(tags);
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

            //  Snapshot attribute BaseValue for prediction rollback.
            // If the server later rejects this prediction, ClientActivateAbilityFailed restores these values.
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
        // Delta tracking: NetworkIds of effects removed since last snapshot
        private List<int> pendingRemovedEffectNetIds => ReplicationStateBuilder.PendingRemovedEffectNetIds;
        // Delta tracking: ability definitions removed since last snapshot
        private List<IGASAbilityDefinition> pendingRemovedAbilityDefs => ReplicationStateBuilder.PendingRemovedAbilityDefs;
        private bool grantedAbilitiesDirty { get => ReplicationStateBuilder.GrantedAbilitiesDirty; set => ReplicationStateBuilder.GrantedAbilitiesDirty = value; }
        private bool activeEffectsDirty { get => ReplicationStateBuilder.ActiveEffectsDirty; set => ReplicationStateBuilder.ActiveEffectsDirty = value; }
        private bool attributeStructureDirty { get => ReplicationStateBuilder.AttributeStructureDirty; set => ReplicationStateBuilder.AttributeStructureDirty = value; }
        private bool tagsDirty { get => ReplicationStateBuilder.TagsDirty; set => ReplicationStateBuilder.TagsDirty = value; }

        public ulong StateVersion => stateVersion;
        public uint AttributeRegistryVersion => attributeRegistryVersion;
        public AbilitySystemStateChangeMask PendingStateChangeMask
        {
            get => ReplicationStateBuilder.PendingMask;
        }

        public bool IsAttributeStructureDirty => attributeStructureDirty;
        public IReadOnlyCollection<string> DirtyAttributeNames => dirtyAttributeNames;
        public IReadOnlyList<GameplayAttribute> DirtyAttributeValueSnapshots => dirtyAttributeValueSnapshots;
        public IReadOnlyCollection<GameplayTag> PendingAddedTags => pendingAddedTags;
        public IReadOnlyCollection<GameplayTag> PendingRemovedTags => pendingRemovedTags;

        public void CapturePendingStateDeltaNonAlloc(GASAbilitySystemStateDeltaBuffer buffer, Func<ActiveGameplayEffect, int> effectInstanceIdResolver = null)
        {
            AssertRuntimeThread();
            if (buffer == null)
            {
                return;
            }

            ReplicationStateBuilder.BeginCapture(buffer);

            if (grantedAbilitiesDirty)
            {
                buffer.ChangeMask |= AbilitySystemStateChangeMask.GrantedAbilities;
                var granted = buffer.EnsureGrantedAbilityCapacity(activatableAbilities.Count);
                buffer.GrantedAbilityCount = FillGrantedAbilities(granted);

                if (pendingRemovedAbilityDefs.Count > 0)
                {
                    var removed = buffer.EnsureRemovedAbilityDefinitionCapacity(pendingRemovedAbilityDefs.Count);
                    for (int i = 0; i < pendingRemovedAbilityDefs.Count; i++)
                    {
                        removed[i] = pendingRemovedAbilityDefs[i];
                    }

                    buffer.RemovedAbilityDefinitionCount = pendingRemovedAbilityDefs.Count;
                }
            }

            if (activeEffectsDirty)
            {
                buffer.ChangeMask |= AbilitySystemStateChangeMask.ActiveEffects;
                var effects = buffer.EnsureActiveEffectCapacity(activeEffects.Count);
                buffer.ActiveEffectCount = FillActiveEffects(buffer, effects, effectInstanceIdResolver);
            }

            if (pendingRemovedEffectNetIds.Count > 0)
            {
                buffer.ChangeMask |= AbilitySystemStateChangeMask.ActiveEffects;
                var removed = buffer.EnsureRemovedEffectNetIdCapacity(pendingRemovedEffectNetIds.Count);
                for (int i = 0; i < pendingRemovedEffectNetIds.Count; i++)
                {
                    removed[i] = pendingRemovedEffectNetIds[i];
                }

                buffer.RemovedEffectNetIdCount = pendingRemovedEffectNetIds.Count;
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

            ReplicationStateBuilder.CompleteCapture(buffer, ComputeReplicatedStateChecksum());
        }

        public bool ServerSendPendingStateDelta(IGASNetworkTarget targetAsc, GASAbilitySystemStateDeltaBuffer buffer, Func<ActiveGameplayEffect, int> effectInstanceIdResolver = null)
        {
            if (targetAsc == null || buffer == null)
            {
                return false;
            }

            var bridge = GASServices.NetworkBridge;
            if (!bridge.IsServer)
            {
                return false;
            }

            CapturePendingStateDeltaNonAlloc(buffer, effectInstanceIdResolver);
            if (!buffer.HasChanges)
            {
                return false;
            }

            buffer.Sequence = ReplicationStateBuilder.NextOutgoingDeltaSequence();
            bridge.ServerSendStateDelta(targetAsc, buffer);
            return true;
        }

        private int FillGrantedAbilities(GASGrantedAbilityStateData[] entries)
        {
            for (int i = 0; i < activatableAbilities.Count; i++)
            {
                var spec = activatableAbilities[i];
                var ability = spec.AbilityCDO ?? spec.Ability;
                entries[i] = new GASGrantedAbilityStateData(spec.Handle, ability, spec.Level, spec.IsActive);
            }

            return activatableAbilities.Count;
        }

        private int FillActiveEffects(
            GASAbilitySystemStateDeltaBuffer buffer,
            GASActiveEffectStateData[] entries,
            Func<ActiveGameplayEffect, int> effectInstanceIdResolver)
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

                entries[i] = GASActiveEffectStateData.FromRaw(
                    effectInstanceIdResolver != null ? effectInstanceIdResolver(effect) : 0,
                    effect.Spec.Def,
                    effect.Spec.Source,
                    effect.Spec.Level,
                    effect.StackCount,
                    effect.Spec.DurationRaw,
                    effect.TimeRemainingRaw,
                    effect.PeriodTimeRemainingRaw,
                    effect.Spec.Context?.PredictionKey ?? default,
                    setByCallerCount > 0 ? setByCallerEntries : Array.Empty<GASSetByCallerTagStateData>(),
                    setByCallerCount);
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

        private void ClearPendingStateChanges()
        {
            ReplicationStateBuilder.ClearPendingStateChanges();
        }

        public void ConsumePendingStateChanges()
        {
            AssertRuntimeThread();
            ClearPendingStateChanges();
        }

        private void MarkGrantedAbilitiesDirty()
        {
            ReplicationStateBuilder.MarkGrantedAbilitiesDirty();
        }

        private void MarkActiveEffectsDirty()
        {
            ReplicationStateBuilder.MarkActiveEffectsDirty();
        }

        private void MarkAttributeValueDirty(GameplayAttribute attribute)
        {
            ReplicationStateBuilder.MarkAttributeValueDirty(attribute);
        }

        private void MarkAttributeStructureDirty()
        {
            ReplicationStateBuilder.MarkAttributeStructureDirty();
        }

        private void TrackTagCountChange(GameplayTag tag, int newCount)
        {
            bool changed = ReplicationStateBuilder.TrackTagCountChange(tag, newCount);
            if (!changed)
            {
                return;
            }

            // Bug fix: when a tag actually appears or disappears, any active effect whose
            // OngoingTagRequirements reference this tag may change inhibition state.
            // Mark those effects' attributes dirty so RecalculateDirtyAttributes() (which
            // updates IsInhibited) runs at the start of the next Tick().
            MarkAttributesDirtyForEffectsWithOngoingRequirements();
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
            if (snapshot == null) return;
            ApplyAuthorityAttributeStateData(snapshot, snapshot.Length);
        }

        public void ApplyAuthorityAttributeStateData(GASAttributeStateData[] snapshot, int count)
        {
            if (snapshot == null) return;
            int safeCount = Math.Min(count, snapshot.Length);
            for (int i = 0; i < safeCount; i++)
            {
                ref readonly var entry = ref snapshot[i];
                if (!attributes.TryGetValue(entry.AttributeName, out var attr)) continue;
                attr.OwningSet.SetBaseValueRaw(attr, entry.BaseValueRaw);
                attr.OwningSet.SetCurrentValueRaw(attr, entry.CurrentValueRaw);
            }
        }

        public void ApplyStateDelta(GASAbilitySystemStateDeltaBuffer delta)
        {
            if (delta == null || !delta.HasChanges) return;

            using (new NetworkReplicationScope(this))
            {
                if ((delta.ChangeMask & AbilitySystemStateChangeMask.Attributes) != 0 &&
                    delta.AttributeCount > 0)
                {
                    ApplyAuthorityAttributeStateData(delta.Attributes, delta.AttributeCount);
                }

                if ((delta.ChangeMask & AbilitySystemStateChangeMask.ActiveEffects) != 0)
                {
                    for (int i = 0; i < delta.RemovedEffectNetIdCount; i++)
                    {
                        int netId = delta.RemovedEffectNetIds[i];
                        if (netId == 0) continue;
                        RemoveActiveEffectByNetworkId(netId);
                    }

                    var resolver = GASServices.ReplicationResolver;
                    for (int i = 0; i < delta.ActiveEffectCount; i++)
                    {
                        ref readonly var snap = ref delta.ActiveEffects[i];
                        var effectDef = snap.EffectDefinition as GameplayEffect;
                        if (effectDef == null) continue;
                        var source = snap.SourceComponent as AbilitySystemComponent;
                        BuildReplicationDataFromStateData(in snap, effectDef, source, resolver, out var data);
                        ApplyAuthoritativeEffectReplication(in data, allowCreate: true);
                    }
                }

                if ((delta.ChangeMask & AbilitySystemStateChangeMask.Tags) != 0)
                {
                    ApplyAddedTags(delta.AddedTags, delta.AddedTagCount);
                    ApplyRemovedTags(delta.RemovedTags, delta.RemovedTagCount);
                }

                if ((delta.ChangeMask & AbilitySystemStateChangeMask.GrantedAbilities) != 0)
                {
                    ApplyRemovedAbilities(delta.RemovedAbilityDefinitions, delta.RemovedAbilityDefinitionCount);
                    if (delta.GrantedAbilityCount > 0)
                    {
                        ApplyGrantedAbilityReplacement(delta.GrantedAbilities, delta.GrantedAbilityCount);
                    }
                }

                if (dirtyAttributes.Count > 0)
                {
                    RecalculateDirtyAttributes();
                }
            }
        }

        private void RemoveActiveEffectByNetworkId(int netId)
        {
            var toRemove = FindActiveEffectByNetworkId(netId);
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
                    CombinedTags.AddTag(tag);
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
                    CombinedTags.RemoveTag(tag);
                }
            }
        }

        private void ApplyRemovedAbilities(IGASAbilityDefinition[] abilityDefinitions, int count)
        {
            if (abilityDefinitions == null) return;
            for (int i = 0; i < count; i++)
            {
                var def = abilityDefinitions[i];
                if (def == null) continue;
                for (int j = activatableAbilities.Count - 1; j >= 0; j--)
                {
                    var existing = activatableAbilities[j].AbilityCDO ?? activatableAbilities[j].Ability;
                    if (existing == def)
                    {
                        ClearAbility(activatableAbilities[j]);
                        break;
                    }
                }
            }
        }

        private void ApplyGrantedAbilityReplacement(GASGrantedAbilityStateData[] grantedAbilities, int count)
        {
            if (grantedAbilities == null) return;

            for (int i = activatableAbilities.Count - 1; i >= 0; i--)
            {
                var spec = activatableAbilities[i];
                var def = spec.AbilityCDO ?? spec.Ability;
                if (def != null && !ContainsGrantedAbility(grantedAbilities, count, def))
                {
                    ClearAbility(spec);
                }
            }

            for (int i = 0; i < count; i++)
            {
                ref readonly var abilitySnap = ref grantedAbilities[i];
                var ability = abilitySnap.AbilityDefinition as GameplayAbility;
                if (ability == null) continue;

                bool alreadyGranted = false;
                for (int j = 0; j < activatableAbilities.Count; j++)
                {
                    var existing = activatableAbilities[j].AbilityCDO ?? activatableAbilities[j].Ability;
                    if (existing == ability)
                    {
                        activatableAbilities[j].AssignReplicatedHandle(abilitySnap.SpecHandle);
                        activatableAbilities[j].Level = abilitySnap.Level;
                        alreadyGranted = true;
                        break;
                    }
                }

                if (!alreadyGranted)
                {
                    GrantAbility(ability, abilitySnap.Level, abilitySnap.SpecHandle);
                }
            }
        }

        private static bool ContainsGrantedAbility(GASGrantedAbilityStateData[] grantedAbilities, int count, IGASAbilityDefinition definition)
        {
            for (int i = 0; i < count; i++)
            {
                if (grantedAbilities[i].AbilityDefinition == definition)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Builds a <see cref="GASEffectReplicationData"/> from a active effect state data.
        /// Used by <see cref="ApplyStateDelta"/> and <see cref="ApplyStateDelta"/>.
        /// </summary>
        private void BuildReplicationDataFromStateData(
            in GASActiveEffectStateData snap,
            GameplayEffect effectDef,
            AbilitySystemComponent source,
            IGASReplicationResolver resolver,
            out GASEffectReplicationData data)
        {
            data = new GASEffectReplicationData
            {
                NetworkId = snap.InstanceId,
                EffectDefId = resolver.GetGameplayEffectDefinitionId(effectDef),
                SourceAscNetId = source != null ? resolver.GetAbilitySystemNetworkId(source) : 0,
                Level = snap.Level,
                StackCount = snap.StackCount,
                DurationRaw = snap.DurationRaw,
                TimeRemainingRaw = snap.TimeRemainingRaw,
                PeriodTimeRemainingRaw = snap.PeriodTimeRemainingRaw,
                PredictionKey = snap.PredictionKey,
            };

            if (snap.SetByCallerTagMagnitudes != null && snap.SetByCallerTagMagnitudeCount > 0)
            {
                int count = Math.Min(snap.SetByCallerTagMagnitudeCount, snap.SetByCallerTagMagnitudes.Length);
                EnsureStateApplySetByCallerCapacity(count);
                for (int j = 0; j < count; j++)
                {
                    stateApplySetByCallerTags[j] = snap.SetByCallerTagMagnitudes[j].Tag;
                    stateApplySetByCallerValuesRaw[j] = snap.SetByCallerTagMagnitudes[j].ValueRaw;
                }
                data.SetByCallerTags = stateApplySetByCallerTags;
                data.SetByCallerValuesRaw = stateApplySetByCallerValuesRaw;
                data.SetByCallerCount = count;
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

        private void EnsureTargetDataNetworkIdCapacity(int count)
        {
            if (count <= 0 || targetDataNetworkIdBuffer.Length >= count)
            {
                return;
            }

            int next = Math.Max(count, targetDataNetworkIdBuffer.Length == 0 ? 4 : targetDataNetworkIdBuffer.Length * 2);
            Array.Resize(ref targetDataNetworkIdBuffer, next);
        }
    }
}
