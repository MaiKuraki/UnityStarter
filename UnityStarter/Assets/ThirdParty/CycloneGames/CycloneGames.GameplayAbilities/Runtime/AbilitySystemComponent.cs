using System;
using System.Collections.Generic;
using CycloneGames.Factory.Runtime;
using CycloneGames.GameplayTags.Runtime;
using CycloneGames.GameplayAbilities.Core;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Runtime
{
    public enum EReplicationMode
    {
        Full,
        Mixed,
        Minimal
    }

    // Delegate for gameplay event delivery (UE5: SendGameplayEventToActor)
    public delegate void GameplayEventDelegate(GameplayEventData eventData);

    // Delegate for effect lifecycle events
    public delegate void ActiveEffectDelegate(ActiveGameplayEffect effect);

    public partial class AbilitySystemComponent : IDisposable, IGASNetworkTarget
    {
        public object OwnerActor { get; private set; }
        public object AvatarActor { get; private set; }
        public UnityEngine.Object OwnerUnityObject { get; private set; }
        public GameObject AvatarGameObject { get; private set; }
        public EReplicationMode ReplicationMode { get; set; } = EReplicationMode.Full;

        public GameplayTagCountContainer CombinedTags { get; } = new GameplayTagCountContainer();
        private readonly GameplayTagCountContainer looseTags = new GameplayTagCountContainer();
        private readonly GameplayTagCountContainer fromEffectsTags = new GameplayTagCountContainer();

        /// <summary>
        /// Tags that grant immunity to effects. Effects with AssetTags or GrantedTags matching these will be blocked.
        /// </summary>
        public GameplayTagContainer ImmunityTags { get; } = new GameplayTagContainer();

        private readonly List<AttributeSet> attributeSets = new List<AttributeSet>(4);
        public IReadOnlyList<AttributeSet> AttributeSets => attributeSets;
        private readonly Dictionary<string, GameplayAttribute> attributes = new Dictionary<string, GameplayAttribute>(32);
        private readonly List<ActiveGameplayEffect> activeEffects = new List<ActiveGameplayEffect>(32);
        private readonly Dictionary<ActiveGameplayEffect, int> activeEffectIndexByEffect = new Dictionary<ActiveGameplayEffect, int>(32);
        // Expose as IReadOnlyList without AsReadOnly() wrapper allocation.
        // List<T> implements IReadOnlyList<T> directly since .NET 4.5.
        public IReadOnlyList<ActiveGameplayEffect> ActiveEffects => activeEffects;

        //  O(1) stacking index --avoids linear search per ApplyGameplayEffectSpecToSelf
        // Key: GameplayEffect def ->first matching ActiveGameplayEffect (for AggregateByTarget)
        private readonly Dictionary<GameplayEffect, ActiveGameplayEffect> stackingIndexByTarget = new Dictionary<GameplayEffect, ActiveGameplayEffect>(16);
        // Key: (GameplayEffect def, source ASC) ->ActiveGameplayEffect (for AggregateBySource)
        private readonly Dictionary<(GameplayEffect, AbilitySystemComponent), ActiveGameplayEffect> stackingIndexBySource = new Dictionary<(GameplayEffect, AbilitySystemComponent), ActiveGameplayEffect>(16);

        // Explicit-GrantedTag runtime-index -> ActiveGameplayEffects for O(1) tag lookup,
        // while preserving all contributors for correct multi-effect semantics.
        // Maintained in OnEffectApplied / OnEffectRemoved.
        private readonly Dictionary<int, List<ActiveGameplayEffect>> grantedTagIndexToEffects = new Dictionary<int, List<ActiveGameplayEffect>>(16);

        private readonly List<GameplayAbilitySpec> activatableAbilities = new List<GameplayAbilitySpec>(16);
        private readonly Dictionary<int, GameplayAbilitySpec> abilitySpecByHandle = new Dictionary<int, GameplayAbilitySpec>(16);
        private readonly Dictionary<GameplayAbilitySpec, int> abilitySpecIndexBySpec = new Dictionary<GameplayAbilitySpec, int>(16);
        private readonly Dictionary<ActiveGameplayEffect, List<GameplayAbilitySpec>> grantedAbilitySpecsByEffect = new Dictionary<ActiveGameplayEffect, List<GameplayAbilitySpec>>(8);
        private readonly Stack<List<GameplayAbilitySpec>> grantedAbilitySpecListPool = new Stack<List<GameplayAbilitySpec>>(4);
        public IReadOnlyList<GameplayAbilitySpec> GetActivatableAbilities() => activatableAbilities;

        private readonly List<GameplayAbilitySpec> tickingAbilities = new List<GameplayAbilitySpec>(16);
        private readonly Dictionary<GameplayAbilitySpec, int> tickingAbilityIndexBySpec = new Dictionary<GameplayAbilitySpec, int>(16);

        private readonly List<GameplayAttribute> dirtyAttributes = new List<GameplayAttribute>(32);

        //  Tracks effects applied by abilities for RemoveGameplayEffectsAfterAbilityEnds
        private readonly Dictionary<GameplayAbility, List<ActiveGameplayEffect>> abilityAppliedEffects = new Dictionary<GameplayAbility, List<ActiveGameplayEffect>>(8);
        private readonly HashSet<ActiveGameplayEffect> abilityAppliedEffectRemovalSet = new HashSet<ActiveGameplayEffect>();

        [ThreadStatic]
        private static List<ModifierInfo> executionOutputScratchPad;

        // --- Prediction ---
        private const int DefaultPredictionTransactionRecordCapacity = 64;
        private GASPredictionKey currentPredictionKey;
        private readonly List<GASPredictionWindowData> predictionWindows = new List<GASPredictionWindowData>(16);
        private readonly Dictionary<GASPredictionKey, int> predictionWindowIndexByKey = new Dictionary<GASPredictionKey, int>(16);
        private readonly List<ActiveGameplayEffect> pendingPredictedEffects = new List<ActiveGameplayEffect>();
        private GASPredictionTransactionRecord[] predictionTransactionRecords = new GASPredictionTransactionRecord[DefaultPredictionTransactionRecordCapacity];
        private int predictionTransactionRecordCursor;
        private int predictionTransactionRecordCount;
        private int localPredictionInputSequence;
        private long totalPredictionWindowsOpened;
        private long totalPredictionWindowsConfirmed;
        private long totalPredictionWindowsRejected;
        private long totalPredictionWindowsTimedOut;
        private long stalePredictionConfirmCount;
        private long stalePredictionRejectCount;
        public int PredictionWindowTimeoutFrames { get; set; } = 180;
        public int OpenPredictionWindowCount => predictionWindows.Count;
        public event Action<GASPredictionKey, GASPredictionWindowStatus> OnPredictionWindowClosed;

        public bool ValidateRuntimeIndexes()
        {
            if (activeEffectIndexByEffect.Count != activeEffects.Count ||
                abilitySpecByHandle.Count != activatableAbilities.Count ||
                abilitySpecIndexBySpec.Count != activatableAbilities.Count ||
                tickingAbilityIndexBySpec.Count != tickingAbilities.Count ||
                predictionWindowIndexByKey.Count != predictionWindows.Count)
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

            for (int i = 0; i < predictionWindows.Count; i++)
            {
                var window = predictionWindows[i];
                if (!predictionWindowIndexByKey.TryGetValue(window.PredictionKey, out int index) ||
                    index != i)
                {
                    return false;
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

        //  Attribute snapshot for rolling back instant-effect attribute changes on prediction failure.
        // Populated in ApplyModifier when currentPredictionKey.IsValid; cleared on confirm or rollback.
        private readonly List<(GASPredictionKey key, GameplayAttribute attr, float oldBaseValue)> predictedAttributeSnapshots = new List<(GASPredictionKey, GameplayAttribute, float)>(8);

        // --- Network ---
        // Monotonically-increasing counter for assigning stable NetworkIds to replicated ActiveGameplayEffects.
        // Only the server assigns NetworkIds; clients receive them via ClientReceiveEffectApplied.
        private static int s_NextEffectNetworkId;
        private static int s_NextCoreEntityId;
        private int networkReplicationScopeDepth;

        private readonly GASAbilitySystemState coreState;
        private readonly GASAbilitySystemFacade core;
        private readonly Dictionary<GameplayAbilitySpec, GASSpecHandle> coreSpecHandles = new Dictionary<GameplayAbilitySpec, GASSpecHandle>(16);
        private readonly Dictionary<ActiveGameplayEffect, GASActiveEffectHandle> coreActiveEffectHandles = new Dictionary<ActiveGameplayEffect, GASActiveEffectHandle>(32);
        private GASModifierData[] coreModifierBuffer = Array.Empty<GASModifierData>();
        private GameplayTag[] effectReplicationSetByCallerTags = Array.Empty<GameplayTag>();
        private float[] effectReplicationSetByCallerValues = Array.Empty<float>();
        private GameplayTag[] stateApplySetByCallerTags = Array.Empty<GameplayTag>();
        private float[] stateApplySetByCallerValues = Array.Empty<float>();
        private int[] targetDataNetworkIdBuffer = Array.Empty<int>();
        public GASAbilitySystemState CoreState => coreState;
        public GASAbilitySystemFacade Core => core;

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
        {
            this.EffectContextFactory = effectContextFactory;
            int entityId = System.Threading.Interlocked.Increment(ref s_NextCoreEntityId);
            coreState = new GASAbilitySystemState(new GASEntityId(entityId));
            core = new GASAbilitySystemFacade(coreState);
            CombinedTags.OnAnyTagNewOrRemove += TrackTagCountChange;
        }

        public void InitAbilityActorInfo(object owner, object avatar)
        {
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
            int predictionTransactionRecordCapacity = DefaultPredictionTransactionRecordCapacity)
        {
            EnsureListCapacity(attributeSets, Math.Min(attributeCapacity, 8));
            EnsureListCapacity(activeEffects, activeEffectCapacity);
            EnsureDictionaryCapacity(activeEffectIndexByEffect, activeEffectCapacity);
            EnsureListCapacity(activatableAbilities, abilityCapacity);
            EnsureDictionaryCapacity(abilitySpecByHandle, abilityCapacity);
            EnsureDictionaryCapacity(abilitySpecIndexBySpec, abilityCapacity);
            EnsureDictionaryCapacity(grantedAbilitySpecsByEffect, activeEffectCapacity);
            EnsureListCapacity(tickingAbilities, tickingAbilityCapacity);
            EnsureDictionaryCapacity(tickingAbilityIndexBySpec, tickingAbilityCapacity);
            EnsureListCapacity(dirtyAttributes, dirtyAttributeCapacity);
            EnsureListCapacity(pendingPredictedEffects, activeEffectCapacity);
            EnsureListCapacity(predictedAttributeSnapshots, predictedAttributeCapacity);
            EnsureListCapacity(predictionWindows, predictionWindowCapacity);
            EnsureDictionaryCapacity(predictionWindowIndexByKey, predictionWindowCapacity);
            EnsureListCapacity(pendingRemovedEffectNetIds, activeEffectCapacity);
            EnsureListCapacity(pendingRemovedAbilityDefs, abilityCapacity);

            if (coreModifierBuffer.Length < coreModifierCapacity)
            {
                coreModifierBuffer = new GASModifierData[coreModifierCapacity];
            }

            EnsureEffectReplicationSetByCallerCapacity(maxSetByCallerPerEffect);
            EnsureStateApplySetByCallerCapacity(maxSetByCallerPerEffect);
            EnsureTargetDataNetworkIdCapacity(targetDataObjectCapacity);
            EnsurePredictionTransactionRecordCapacity(predictionTransactionRecordCapacity);

            coreState.Reserve(
                abilityCapacity,
                attributeCapacity,
                activeEffectCapacity,
                coreModifierCapacity,
                corePredictionCapacity);
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
            if (capacity < 0)
            {
                capacity = 0;
            }

            if (predictionTransactionRecords != null && predictionTransactionRecords.Length >= capacity)
            {
                return;
            }

            var oldRecords = predictionTransactionRecords;
            int recordsToCopy = Math.Min(predictionTransactionRecordCount, capacity);
            var newRecords = new GASPredictionTransactionRecord[capacity];
            for (int i = recordsToCopy - 1; i >= 0; i--)
            {
                if (TryGetClosedPredictionTransactionRecord(i, out var record))
                {
                    int targetIndex = recordsToCopy - 1 - i;
                    newRecords[targetIndex] = record;
                }
            }

            predictionTransactionRecords = newRecords;
            predictionTransactionRecordCount = recordsToCopy;
            predictionTransactionRecordCursor = recordsToCopy % Math.Max(capacity, 1);
            _ = oldRecords;
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
            if (spec == null)
            {
                return;
            }

            if (!abilitySpecIndexBySpec.TryGetValue(spec, out int index) ||
                index < 0 ||
                index >= activatableAbilities.Count ||
                !ReferenceEquals(activatableAbilities[index], spec))
            {
                abilitySpecByHandle.Remove(spec.Handle);
                abilitySpecIndexBySpec.Remove(spec);
                return;
            }

            int lastIndex = activatableAbilities.Count - 1;
            if (index != lastIndex)
            {
                var movedSpec = activatableAbilities[lastIndex];
                activatableAbilities[index] = movedSpec;
                abilitySpecIndexBySpec[movedSpec] = index;
            }
            activatableAbilities.RemoveAt(lastIndex);
            abilitySpecByHandle.Remove(spec.Handle);
            abilitySpecIndexBySpec.Remove(spec);
        }

        private void AddTickingAbilitySpec(GameplayAbilitySpec spec)
        {
            if (spec == null || tickingAbilityIndexBySpec.ContainsKey(spec))
            {
                return;
            }

            tickingAbilityIndexBySpec[spec] = tickingAbilities.Count;
            tickingAbilities.Add(spec);
        }

        private bool RemoveTickingAbilitySpec(GameplayAbilitySpec spec)
        {
            if (spec == null ||
                !tickingAbilityIndexBySpec.TryGetValue(spec, out int index) ||
                index < 0 ||
                index >= tickingAbilities.Count ||
                !ReferenceEquals(tickingAbilities[index], spec))
            {
                return false;
            }

            int lastIndex = tickingAbilities.Count - 1;
            if (index != lastIndex)
            {
                var movedSpec = tickingAbilities[lastIndex];
                tickingAbilities[index] = movedSpec;
                tickingAbilityIndexBySpec[movedSpec] = index;
            }
            tickingAbilities.RemoveAt(lastIndex);
            tickingAbilityIndexBySpec.Remove(spec);
            return true;
        }

        private void RegisterAbilityGrantedByEffect(ActiveGameplayEffect effect, GameplayAbilitySpec spec)
        {
            if (effect == null || spec == null)
            {
                return;
            }

            spec.GrantingEffect = effect;
            if (!grantedAbilitySpecsByEffect.TryGetValue(effect, out var specs))
            {
                specs = RentGrantedAbilitySpecList();
                grantedAbilitySpecsByEffect[effect] = specs;
            }

            specs.Add(spec);
        }

        private void UnregisterAbilityGrantedByEffect(GameplayAbilitySpec spec)
        {
            var effect = spec?.GrantingEffect;
            if (effect == null)
            {
                return;
            }

            if (grantedAbilitySpecsByEffect.TryGetValue(effect, out var specs))
            {
                for (int i = specs.Count - 1; i >= 0; i--)
                {
                    if (!ReferenceEquals(specs[i], spec))
                    {
                        continue;
                    }

                    int lastIndex = specs.Count - 1;
                    if (i != lastIndex)
                    {
                        specs[i] = specs[lastIndex];
                    }
                    specs.RemoveAt(lastIndex);
                    break;
                }

                if (specs.Count == 0)
                {
                    grantedAbilitySpecsByEffect.Remove(effect);
                    ReturnGrantedAbilitySpecList(specs);
                }
            }

            spec.GrantingEffect = null;
        }

        private List<GameplayAbilitySpec> RentGrantedAbilitySpecList()
        {
            if (grantedAbilitySpecListPool.Count > 0)
            {
                return grantedAbilitySpecListPool.Pop();
            }

            return new List<GameplayAbilitySpec>(2);
        }

        private void ReturnGrantedAbilitySpecList(List<GameplayAbilitySpec> specs)
        {
            if (specs == null)
            {
                return;
            }

            specs.Clear();
            grantedAbilitySpecListPool.Push(specs);
        }

        private void ReturnAllGrantedAbilitySpecLists()
        {
            foreach (var kvp in grantedAbilitySpecsByEffect)
            {
                ReturnGrantedAbilitySpecList(kvp.Value);
            }

            grantedAbilitySpecsByEffect.Clear();
        }

        public void AddAttributeSet(AttributeSet set)
        {
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
            if (ability == null) return null;

            var spec = GameplayAbilitySpec.Create(ability, level, replicatedHandle);
            spec.Init(this);

            abilitySpecIndexBySpec[spec] = activatableAbilities.Count;
            activatableAbilities.Add(spec);
            abilitySpecByHandle[spec.Handle] = spec;
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
            if (removedDef != null)
            {
                pendingRemovedAbilityDefs.Add(removedDef);
            }

            MarkGrantedAbilitiesDirty();
        }

        // --- Ability Activation Flow ---
        public bool TryActivateAbility(GameplayAbilitySpec spec)
        {
            if (spec == null || spec.IsActive) return false;

            var ability = spec.GetPrimaryInstance();
            if (ability == null) return false;

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
            var spec = FindSpecByHandle(specHandle);
            ClientActivateAbilitySucceed(spec, predictionKey);
        }

        /// <summary>
        /// Client entry point: server rejected our predicted activation --roll back.
        /// Called by GASNullNetworkBridge immediately, or by the network bridge on RPC receipt.
        /// </summary>
        public void ClientReceiveActivationFailed(int specHandle, GASPredictionKey predictionKey)
        {
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
            ApplyAuthoritativeEffectReplication(data, allowCreate: true);
        }

        /// <summary>
        /// Client entry point: server replicated an effect removal.
        /// </summary>
        public void ClientReceiveEffectRemoved(int effectNetId)
        {
            for (int i = activeEffects.Count - 1; i >= 0; i--)
            {
                var effect = activeEffects[i];
                if (effect.NetworkId == effectNetId)
                {
                    using (new NetworkReplicationScope(this))
                    {
                        RemoveActiveEffectAtIndex(i);
                        RemoveFromStackingIndex(effect);
                        OnEffectRemoved(effect, true);
                    }
                    return;
                }
            }
        }

        /// <summary>
        /// Client entry point: server broadcast a GameplayCue event.
        /// </summary>
        public void ClientReceiveGameplayCue(GameplayTag cueTag, EGameplayCueEvent eventType, in GASCueNetParams cueParams)
        {
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
                0f,
                cueParams.PredictionKey);

            GameplayCueManager.Default.HandleCue(cueTag, eventType, new GameplayCueParameters(parameters)).Forget();
        }

        /// <summary>
        /// Client entry point: server sent a count-based incremental delta buffer.
        /// </summary>
        public void ClientReceiveStateDelta(GASAbilitySystemStateDeltaBuffer delta)
        {
            ApplyStateDelta(delta);
        }

        public void CaptureCoreStateNonAlloc(GASAbilitySystemStateBuffer buffer)
        {
            coreState.CaptureStateNonAlloc(buffer);
        }

        /// <summary>
        /// Fired on clients when the server sends a replicated effect application.
        /// Subscribe to resolve data.EffectDefId and call ApplyGameplayEffectSpecToSelf.
        /// </summary>
        public event Action<GASEffectReplicationData> OnClientEffectApplied;

        private bool SuppressLocalGameplayCueDispatch => networkReplicationScopeDepth > 0 && !GASServices.NetworkBridge.IsServer;

        private GameplayAbilitySpec FindSpecByHandle(int handle)
        {
            return handle > 0 && abilitySpecByHandle.TryGetValue(handle, out var spec) ? spec : null;
        }

        private ActiveGameplayEffect FindActiveEffectByNetworkId(int networkId)
        {
            if (networkId == 0)
            {
                return null;
            }

            for (int i = activeEffects.Count - 1; i >= 0; i--)
            {
                var effect = activeEffects[i];
                if (!effect.IsExpired && effect.NetworkId == networkId)
                {
                    return effect;
                }
            }

            return null;
        }

        /// <summary>
        /// Computes a deterministic, allocation-free checksum over replicated ASC gameplay state.
        /// Use this as a lightweight drift detector between server and client snapshots.
        /// </summary>
        public uint ComputeReplicatedStateChecksum()
        {
            const uint offset = 2166136261u;
            uint hash = offset;

            hash = HashInt(hash, activatableAbilities.Count);
            for (int i = 0; i < activatableAbilities.Count; i++)
            {
                var spec = activatableAbilities[i];
                uint entryHash = 2166136261u;
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

                uint entryHash = 2166136261u;
                entryHash = HashInt(entryHash, effect.NetworkId);
                entryHash = HashInt(entryHash, effect.StackCount);
                entryHash = HashFloat(entryHash, effect.TimeRemaining);
                entryHash = HashFloat(entryHash, effect.PeriodTimeRemaining);
                entryHash = HashInt(entryHash, effect.Spec?.Context?.PredictionKey.Key ?? 0);
                hash = FoldUnordered(hash, entryHash);
            }

            hash = HashInt(hash, attributes.Count);
            for (int i = 0; i < attributeSets.Count; i++)
            {
                foreach (var attribute in attributeSets[i].GetAttributes())
                {
                    uint entryHash = 2166136261u;
                    entryHash = HashString(entryHash, attribute.Name);
                    entryHash = HashFloat(entryHash, attribute.BaseValue);
                    entryHash = HashFloat(entryHash, attribute.CurrentValue);
                    hash = FoldUnordered(hash, entryHash);
                }
            }

            hash = HashInt(hash, CombinedTags.TagCount);
            var tags = CombinedTags.GetTags();
            while (tags.MoveNext())
            {
                var tag = tags.Current;
                uint entryHash = HashString(2166136261u, tag.IsValid && !tag.IsNone ? tag.Name : string.Empty);
                hash = FoldUnordered(hash, entryHash);
            }

            return hash;
        }

        private static uint HashInt(uint hash, int value)
        {
            unchecked
            {
                hash = (hash ^ (uint)value) * 16777619u;
                return hash;
            }
        }

        private static uint HashFloat(uint hash, float value)
        {
            return HashInt(hash, BitConverter.SingleToInt32Bits(value));
        }

        private static uint HashString(uint hash, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return HashInt(hash, 0);
            }

            unchecked
            {
                for (int i = 0; i < value.Length; i++)
                {
                    hash = (hash ^ value[i]) * 16777619u;
                }

                return HashInt(hash, value.Length);
            }
        }

        private static uint FoldUnordered(uint hash, uint entryHash)
        {
            unchecked
            {
                return hash + (entryHash * 16777619u) ^ ((entryHash << 16) | (entryHash >> 16));
            }
        }

        private ActiveGameplayEffect FindPredictedEffectForReconcile(GameplayEffect effectDef, AbilitySystemComponent source, GASPredictionKey predictionKey)
        {
            if (effectDef == null)
            {
                return null;
            }

            for (int i = activeEffects.Count - 1; i >= 0; i--)
            {
                var effect = activeEffects[i];
                if (effect.IsExpired || effect.NetworkId != 0 || effect.Spec?.Def != effectDef)
                {
                    continue;
                }

                if (predictionKey.IsValid)
                {
                    if (effect.Spec.Context == null || !effect.Spec.Context.PredictionKey.Equals(predictionKey))
                    {
                        continue;
                    }
                }
                else if (source != null && effect.Spec.Source != source)
                {
                    continue;
                }

                return effect;
            }

            return null;
        }

        private void RegisterGrantedAbilityInCore(GameplayAbilitySpec spec, GameplayAbility ability, int level)
        {
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
            if (attribute == null)
            {
                return;
            }

            var attributeId = GetOrCreateCoreAttributeId(attribute.Name);
            if (attributeId.IsValid)
            {
                core.SetNumericAttributeBase(attributeId, attribute.BaseValue);
            }
        }

        private void RegisterActiveEffectInCore(ActiveGameplayEffect effect)
        {
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
                spec.Source != null ? spec.Source.CoreState.Entity : default,
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
                    spec.GetCalculatedMagnitude(i));
            }

            return coreModifierBuffer;
        }

        public bool TryGetCoreSpecHandle(GameplayAbilitySpec spec, out GASSpecHandle handle)
        {
            if (spec == null)
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
            if (predictionKey.IsValid)
            {
                core.AcceptPrediction(ConvertPredictionKey(predictionKey));
            }
        }

        private void RejectCorePrediction(GASPredictionKey predictionKey)
        {
            if (predictionKey.IsValid)
            {
                core.RejectPrediction(ConvertPredictionKey(predictionKey));
            }
        }

        public GASPredictionKey OpenPredictionWindow(GameplayAbilitySpec spec, GASPredictionKey parentPredictionKey = default)
        {
            int inputSequence = unchecked(++localPredictionInputSequence);
            if (inputSequence == 0)
            {
                inputSequence = unchecked(++localPredictionInputSequence);
            }

            var predictionKey = GASPredictionKey.NewKey(coreState.Entity, inputSequence);
            RegisterPredictionWindow(spec, predictionKey, parentPredictionKey);
            return predictionKey;
        }

        public bool HasOpenPredictionWindow(GASPredictionKey predictionKey)
        {
            return FindPredictionWindowIndex(predictionKey) >= 0;
        }

        public bool TryGetPredictionWindow(GASPredictionKey predictionKey, out GASPredictionWindowData window)
        {
            int index = FindPredictionWindowIndex(predictionKey);
            if (index >= 0)
            {
                window = predictionWindows[index];
                return true;
            }

            window = default;
            return false;
        }

        public GASPredictionWindowStats GetPredictionWindowStats()
        {
            int parentLinkedCount = 0;
            int expirableCount = 0;
            int earliestTimeoutFrame = 0;
            int predictedEffectCount = 0;
            int predictedAttributeSnapshotCount = 0;
            int predictedGameplayCueCount = 0;
            int predictedAbilityTaskCount = 0;

            for (int i = 0; i < predictionWindows.Count; i++)
            {
                var window = predictionWindows[i];
                predictedEffectCount += window.PredictedEffectCount;
                predictedAttributeSnapshotCount += window.PredictedAttributeSnapshotCount;
                predictedGameplayCueCount += window.PredictedGameplayCueCount;
                predictedAbilityTaskCount += window.PredictedAbilityTaskCount;
                if (window.ParentPredictionKey.IsValid)
                {
                    parentLinkedCount++;
                }

                if (window.TimeoutFrame > 0)
                {
                    expirableCount++;
                    if (earliestTimeoutFrame == 0 || window.TimeoutFrame < earliestTimeoutFrame)
                    {
                        earliestTimeoutFrame = window.TimeoutFrame;
                    }
                }
            }

            return new GASPredictionWindowStats(
                predictionWindows.Count,
                parentLinkedCount,
                expirableCount,
                earliestTimeoutFrame,
                predictedEffectCount,
                predictedAttributeSnapshotCount,
                predictedGameplayCueCount,
                predictedAbilityTaskCount,
                totalPredictionWindowsOpened,
                totalPredictionWindowsConfirmed,
                totalPredictionWindowsRejected,
                totalPredictionWindowsTimedOut,
                stalePredictionConfirmCount,
                stalePredictionRejectCount,
                predictionTransactionRecordCount,
                predictionTransactionRecords?.Length ?? 0);
        }

        public bool TryGetClosedPredictionTransactionRecord(int recentIndex, out GASPredictionTransactionRecord record)
        {
            if (recentIndex < 0 || recentIndex >= predictionTransactionRecordCount || predictionTransactionRecords == null || predictionTransactionRecords.Length == 0)
            {
                record = default;
                return false;
            }

            int index = predictionTransactionRecordCursor - 1 - recentIndex;
            if (index < 0)
            {
                index += predictionTransactionRecords.Length;
            }

            record = predictionTransactionRecords[index];
            return true;
        }

        public int CopyClosedPredictionTransactionRecordsNonAlloc(GASPredictionTransactionRecord[] destination, int destinationIndex = 0, int maxCount = int.MaxValue)
        {
            if (destination == null ||
                destinationIndex < 0 ||
                destinationIndex >= destination.Length ||
                maxCount <= 0 ||
                predictionTransactionRecordCount == 0)
            {
                return 0;
            }

            int count = Math.Min(predictionTransactionRecordCount, Math.Min(maxCount, destination.Length - destinationIndex));
            for (int i = 0; i < count; i++)
            {
                TryGetClosedPredictionTransactionRecord(i, out destination[destinationIndex + i]);
            }

            return count;
        }

        public bool ConfirmPredictionWindow(GASPredictionKey predictionKey)
        {
            if (ClosePredictionWindow(predictionKey, GASPredictionWindowStatus.Confirmed, rollback: false, closeDependents: false))
            {
                return true;
            }

            stalePredictionConfirmCount++;
            RecordStalePredictionTransaction(predictionKey, GASPredictionWindowStatus.Confirmed);
            return false;
        }

        public bool RejectPredictionWindow(GASPredictionKey predictionKey)
        {
            if (ClosePredictionWindow(predictionKey, GASPredictionWindowStatus.Rejected, rollback: true, closeDependents: true))
            {
                return true;
            }

            stalePredictionRejectCount++;
            RecordStalePredictionTransaction(predictionKey, GASPredictionWindowStatus.Rejected);
            return false;
        }

        public void TickPredictionWindows(int currentFrame)
        {
            for (int i = predictionWindows.Count - 1; i >= 0; i--)
            {
                var window = predictionWindows[i];
                if (window.Status == GASPredictionWindowStatus.Open && window.TimeoutFrame > 0 && currentFrame >= window.TimeoutFrame)
                {
                    ClosePredictionWindow(window.PredictionKey, GASPredictionWindowStatus.TimedOut, rollback: true, closeDependents: true);
                }
            }
        }

        private void RegisterPredictionWindow(GameplayAbilitySpec spec, GASPredictionKey predictionKey, GASPredictionKey parentPredictionKey)
        {
            if (!predictionKey.IsValid || FindPredictionWindowIndex(predictionKey) >= 0)
            {
                return;
            }

            TryGetCoreSpecHandle(spec, out var coreHandle);
            int openFrame = Time.frameCount;
            int timeoutFrame = PredictionWindowTimeoutFrames > 0 ? openFrame + PredictionWindowTimeoutFrames : 0;
            int index = predictionWindows.Count;
            predictionWindows.Add(new GASPredictionWindowData(
                predictionKey,
                parentPredictionKey,
                coreHandle,
                spec?.Handle ?? 0,
                openFrame,
                timeoutFrame));
            predictionWindowIndexByKey[predictionKey] = index;
            totalPredictionWindowsOpened++;
        }

        private int FindPredictionWindowIndex(GASPredictionKey predictionKey)
        {
            if (!predictionKey.IsValid)
            {
                return -1;
            }

            if (predictionWindowIndexByKey.TryGetValue(predictionKey, out int index) &&
                index >= 0 &&
                index < predictionWindows.Count &&
                predictionWindows[index].PredictionKey.Equals(predictionKey))
            {
                return index;
            }

            return -1;
        }

        private void IncrementPredictionWindowEffectCount(GASPredictionKey predictionKey)
        {
            int index = FindPredictionWindowIndex(predictionKey);
            if (index < 0)
            {
                return;
            }

            var window = predictionWindows[index];
            window.PredictedEffectCount++;
            predictionWindows[index] = window;
        }

        private void IncrementPredictionWindowAttributeSnapshotCount(GASPredictionKey predictionKey)
        {
            int index = FindPredictionWindowIndex(predictionKey);
            if (index < 0)
            {
                return;
            }

            var window = predictionWindows[index];
            window.PredictedAttributeSnapshotCount++;
            predictionWindows[index] = window;
        }

        private void IncrementPredictionWindowGameplayCueCount(GASPredictionKey predictionKey, int count = 1)
        {
            int index = FindPredictionWindowIndex(predictionKey);
            if (index < 0 || count <= 0)
            {
                return;
            }

            var window = predictionWindows[index];
            window.PredictedGameplayCueCount += count;
            predictionWindows[index] = window;
        }

        internal void NotifyPredictedAbilityTaskCreated(GASPredictionKey predictionKey)
        {
            int index = FindPredictionWindowIndex(predictionKey);
            if (index < 0)
            {
                return;
            }

            var window = predictionWindows[index];
            window.PredictedAbilityTaskCount++;
            predictionWindows[index] = window;
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

            int index = FindPredictionWindowIndex(predictionKey);
            if (index < 0)
            {
                return false;
            }

            var window = predictionWindows[index];
            int lastIndex = predictionWindows.Count - 1;
            if (index != lastIndex)
            {
                var movedWindow = predictionWindows[lastIndex];
                predictionWindows[index] = movedWindow;
                predictionWindowIndexByKey[movedWindow.PredictionKey] = index;
            }
            predictionWindows.RemoveAt(lastIndex);
            predictionWindowIndexByKey.Remove(predictionKey);

            if (rollback)
            {
                rollbackFlags |= RollbackPrediction(predictionKey);
                rollbackFlags |= CancelPredictedExecution(window, predictionKey);
            }

            RecordPredictionTransaction(window, status, rollbackFlags, Time.frameCount);

            switch (status)
            {
                case GASPredictionWindowStatus.Confirmed:
                    totalPredictionWindowsConfirmed++;
                    break;
                case GASPredictionWindowStatus.Rejected:
                    totalPredictionWindowsRejected++;
                    break;
                case GASPredictionWindowStatus.TimedOut:
                    totalPredictionWindowsTimedOut++;
                    break;
            }

            OnPredictionWindowClosed?.Invoke(predictionKey, status);
            return true;
        }

        private bool CloseDependentPredictionWindows(GASPredictionKey parentPredictionKey, GASPredictionWindowStatus status)
        {
            bool closedAny = false;
            while (TryFindDependentPredictionWindow(parentPredictionKey, out var childPredictionKey))
            {
                closedAny |= ClosePredictionWindow(childPredictionKey, status, rollback: true, closeDependents: true);
            }

            return closedAny;
        }

        private bool TryFindDependentPredictionWindow(GASPredictionKey parentPredictionKey, out GASPredictionKey childPredictionKey)
        {
            for (int i = predictionWindows.Count - 1; i >= 0; i--)
            {
                var window = predictionWindows[i];
                if (window.ParentPredictionKey.Equals(parentPredictionKey))
                {
                    childPredictionKey = window.PredictionKey;
                    return true;
                }
            }

            childPredictionKey = default;
            return false;
        }

        private void RecordPredictionTransaction(
            GASPredictionWindowData window,
            GASPredictionWindowStatus status,
            GASPredictionRollbackFlags rollbackFlags,
            int closeFrame)
        {
            if (predictionTransactionRecords == null || predictionTransactionRecords.Length == 0)
            {
                return;
            }

            predictionTransactionRecords[predictionTransactionRecordCursor] = new GASPredictionTransactionRecord(window, status, rollbackFlags, closeFrame);
            predictionTransactionRecordCursor++;
            if (predictionTransactionRecordCursor >= predictionTransactionRecords.Length)
            {
                predictionTransactionRecordCursor = 0;
            }

            if (predictionTransactionRecordCount < predictionTransactionRecords.Length)
            {
                predictionTransactionRecordCount++;
            }
        }

        private void RecordStalePredictionTransaction(GASPredictionKey predictionKey, GASPredictionWindowStatus status)
        {
            if (!predictionKey.IsValid)
            {
                return;
            }

            var window = new GASPredictionWindowData(predictionKey, default, default, 0, 0, 0);
            RecordPredictionTransaction(window, status, GASPredictionRollbackFlags.StaleMessage, Time.frameCount);
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
            var spec = FindSpecByHandle(specHandle);
            spec?.GetPrimaryInstance()?.AcceptTasksForPredictionKey(predictionKey);
            GameplayCueManager.Default.AcceptPredictedCues(this, predictionKey);
        }

        public void ClientReceiveTargetDataRejected(int specHandle, GASPredictionKey predictionKey, TargetDataValidationResult reason)
        {
            var spec = FindSpecByHandle(specHandle);
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

            existing.NetworkId = data.NetworkId != 0 ? data.NetworkId : existing.NetworkId;
            TryApplyReplicatedEffectUpdate(
                existing,
                data.Level,
                data.StackCount,
                data.Duration,
                data.TimeRemaining,
                data.PeriodTimeRemaining,
                data.SetByCallerTags,
                data.SetByCallerValues,
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
            spec.ApplyReplicatedState(data.Level, data.Duration, data.SetByCallerTags, data.SetByCallerValues, data.SetByCallerCount);

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
            if (!predictionKey.IsValid)
            {
                return;
            }

            for (int i = pendingPredictedEffects.Count - 1; i >= 0; i--)
            {
                var predicted = pendingPredictedEffects[i];
                if (predicted.Spec.Context.PredictionKey.Equals(predictionKey))
                {
                    int lastIndex = pendingPredictedEffects.Count - 1;
                    if (i != lastIndex)
                    {
                        pendingPredictedEffects[i] = pendingPredictedEffects[lastIndex];
                    }
                    pendingPredictedEffects.RemoveAt(lastIndex);
                }
            }
        }

        private GASPredictionRollbackFlags RollbackPrediction(GASPredictionKey predictionKey)
        {
            if (!predictionKey.IsValid)
            {
                return GASPredictionRollbackFlags.None;
            }

            var flags = GASPredictionRollbackFlags.None;
            for (int i = pendingPredictedEffects.Count - 1; i >= 0; i--)
            {
                var effect = pendingPredictedEffects[i];
                if (!effect.Spec.Context.PredictionKey.Equals(predictionKey))
                {
                    continue;
                }

                int pendingLastIndex = pendingPredictedEffects.Count - 1;
                if (i != pendingLastIndex)
                {
                    pendingPredictedEffects[i] = pendingPredictedEffects[pendingLastIndex];
                }
                pendingPredictedEffects.RemoveAt(pendingLastIndex);

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

            predictedAttributeSnapshots.Add((predictionKey, attribute, attribute.BaseValue));
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
                    snapshot.attr.SetBaseValue(snapshot.oldBaseValue);
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

            // P0+ Use zero-GC StringBuilder overload for Warning (avoids string interpolation alloc in Release)
            GASLog.Warning(sb => sb.Append("Client prediction failed for ability '").Append(spec?.Ability?.Name ?? "<missing-spec>")
                .Append("' with key ").Append(predictionKey.Key).Append(closedPredictionWindow ? ". Rolling back." : ". Ignoring stale rejection."));
        }

        internal void NotifyAbilityCommitted(GameplayAbility ability)
        {
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
                    if (abilityAppliedEffects.TryGetValue(ability, out var appliedEffects))
                    {
                        abilityAppliedEffectRemovalSet.Clear();
                        for (int ei = 0; ei < appliedEffects.Count; ei++)
                        {
                            if (!appliedEffects[ei].IsExpired)
                            {
                                abilityAppliedEffectRemovalSet.Add(appliedEffects[ei]);
                            }
                        }

                        if (abilityAppliedEffectRemovalSet.Count > 0)
                        {
                            using (Pools.ListPool<ActiveGameplayEffect>.Get(out var removed))
                            {
                                // Single backward pass over activeEffects: O(n) total instead of O(n^2)
                                for (int i = activeEffects.Count - 1; i >= 0; i--)
                                {
                                    if (abilityAppliedEffectRemovalSet.Contains(activeEffects[i]))
                                    {
                                        removed.Add(activeEffects[i]);
                                        RemoveActiveEffectAtIndex(i);
                                    }
                                }
                                for (int i = 0; i < removed.Count; i++)
                                    OnEffectRemoved(removed[i], true);
                            }
                        }

                        abilityAppliedEffectRemovalSet.Clear();
                        appliedEffects.Clear();
                        abilityAppliedEffects.Remove(ability);
                    }
                }

                // This ensures that flags like 'isEnding' are ready for the next activation.
                ability.InternalOnEndAbility();

                OnAbilityEndedEvent?.Invoke(ability);

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
            spec.SetTarget(this);

            // If we are in a prediction scope, tag the spec's context
            if (currentPredictionKey.IsValid)
            {
                spec.Context.PredictionKey = currentPredictionKey;
            }

            if (!CanApplyEffect(spec))
            {
                spec.ReturnToPool();
                return;
            }

            RemoveEffectsWithTags(spec.Def.RemoveGameplayEffectsWithTags);

            if (spec.Def.DurationPolicy == EDurationPolicy.Instant)
            {
                var coreEffectSpec = BuildCoreEffectSpec(spec);
                core.ApplyGameplayEffectSpecToSelf(in coreEffectSpec);
                ExecuteInstantEffect(spec);
                DispatchGameplayCues(spec, EGameplayCueEvent.Executed);
                spec.ReturnToPool();
                return;
            }

            if (HandleStacking(spec))
            {
                spec.ReturnToPool();
                return;
            }

            var newActiveEffect = ActiveGameplayEffect.Create(spec);
            activeEffectIndexByEffect[newActiveEffect] = activeEffects.Count;
            activeEffects.Add(newActiveEffect);
            RegisterActiveEffectInCore(newActiveEffect);

            //  Maintain stacking index for O(1) lookup
            if (spec.Def.Stacking.Type != EGameplayEffectStackingType.None)
            {
                if (spec.Def.Stacking.Type == EGameplayEffectStackingType.AggregateByTarget)
                {
                    stackingIndexByTarget.TryAdd(spec.Def, newActiveEffect);
                }
                else if (spec.Def.Stacking.Type == EGameplayEffectStackingType.AggregateBySource)
                {
                    stackingIndexBySource.TryAdd((spec.Def, spec.Source), newActiveEffect);
                }
            }

            if (currentPredictionKey.IsValid)
            {
                pendingPredictedEffects.Add(newActiveEffect);
                IncrementPredictionWindowEffectCount(currentPredictionKey);
            }

            //  Track effects for RemoveGameplayEffectsAfterAbilityEnds
            if (spec.Def.RemoveGameplayEffectsAfterAbilityEnds && spec.Context?.AbilityInstance != null)
            {
                if (!abilityAppliedEffects.TryGetValue(spec.Context.AbilityInstance, out var list))
                {
                    list = new List<ActiveGameplayEffect>(4);
                    abilityAppliedEffects[spec.Context.AbilityInstance] = list;
                }
                list.Add(newActiveEffect);
            }

            GASLog.Info(sb => sb.Append(OwnerActor).Append(" Apply GameplayEffect '").Append(spec.Def.Name).Append("' to self."));
            OnEffectApplied(newActiveEffect);
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
                        float magnitude = modInfo.Magnitude.GetValueAtLevel(spec.Level);
                        ApplyModifier(spec, attribute, modInfo, magnitude, true);
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
                    ApplyModifier(spec, attribute, mod, spec.GetCalculatedMagnitude(i), true);
                }
            }

            // CLogger.LogInfo($"{OwnerActor} Execute Instant GameplayEffect '{spec.Def.Name}' on self.");
        }

        // --- Tick and State Management ---
        public void Tick(float deltaTime, bool isServer)
        {
            for (int i = tickingAbilities.Count - 1; i >= 0; i--)
            {
                var spec = tickingAbilities[i];
                spec.GetPrimaryInstance()?.TickTasks(deltaTime);
            }

            if (!isServer && predictionWindows.Count > 0)
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
            var removedEffect = activeEffects[index];
            int lastIndex = activeEffects.Count - 1;
            if (index != lastIndex)
            {
                var movedEffect = activeEffects[lastIndex];
                activeEffects[index] = movedEffect;
                activeEffectIndexByEffect[movedEffect] = index;
            }
            activeEffects.RemoveAt(lastIndex);
            activeEffectIndexByEffect.Remove(removedEffect);
        }

        private bool TryFindActiveEffectIndex(ActiveGameplayEffect effect, out int index)
        {
            if (effect != null &&
                activeEffectIndexByEffect.TryGetValue(effect, out index) &&
                index >= 0 &&
                index < activeEffects.Count &&
                ReferenceEquals(activeEffects[index], effect))
            {
                return true;
            }

            index = -1;
            return false;
        }

        /// <summary>
        /// Removes an effect from the stacking index.
        /// </summary>
        private void RemoveFromStackingIndex(ActiveGameplayEffect effect)
        {
            var def = effect.Spec.Def;
            if (def.Stacking.Type == EGameplayEffectStackingType.AggregateByTarget)
            {
                if (stackingIndexByTarget.TryGetValue(def, out var indexed) && indexed == effect)
                {
                    stackingIndexByTarget.Remove(def);
                }
            }
            else if (def.Stacking.Type == EGameplayEffectStackingType.AggregateBySource)
            {
                var key = (def, effect.Spec.Source);
                if (stackingIndexBySource.TryGetValue(key, out var indexed) && indexed == effect)
                {
                    stackingIndexBySource.Remove(key);
                }
            }
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

                float baseValue = attr.OwningSet.GetBaseValue(attr);
                float additive = 0f;
                float multiplicativeBiasSum = 0f;
                float divisionBiasSum = 0f;
                float overrideValue = 0f;
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
                        float baseMagnitude;
                        var mod = modifiers[m];
                        if (mod.SnapshotPolicy == EGameplayEffectAttributeCaptureSnapshot.NotSnapshot && mod.CustomCalculation != null)
                        {
                            baseMagnitude = mod.CustomCalculation.CalculateMagnitude(effect.Spec);
                            effect.Spec.ModifierMagnitudes[m] = baseMagnitude; // Update cached value
                        }
                        else
                        {
                            baseMagnitude = effect.Spec.ModifierMagnitudes[m];
                        }

                        float magnitude = baseMagnitude * stackCount;
                        switch (modifiers[m].Operation)
                        {
                            case EAttributeModifierOperation.Add:
                                additive += magnitude;
                                break;
                            case EAttributeModifierOperation.Multiply:
                                multiplicativeBiasSum += (magnitude - 1f);
                                break;
                            case EAttributeModifierOperation.Division:
                                if (magnitude != 0f) divisionBiasSum += (magnitude - 1f);
                                break;
                            case EAttributeModifierOperation.Override:
                                overrideValue = magnitude;
                                hasOverride = true;
                                break;
                        }
                    }
                }

                float multiplicative = 1f + multiplicativeBiasSum;
                float division = 1f + divisionBiasSum;
                if (division == 0f) division = 1f;

                float finalValue = hasOverride ? overrideValue : ((baseValue + additive) * multiplicative / division);

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

            if (grantedAbilitySpecsByEffect.TryGetValue(effect, out var grantedSpecs))
            {
                while (grantedSpecs.Count > 0)
                {
                    ClearAbility(grantedSpecs[grantedSpecs.Count - 1]);
                }

                grantedAbilitySpecsByEffect.Remove(effect);
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
            if (effect.NetworkId != 0)
            {
                pendingRemovedEffectNetIds.Add(effect.NetworkId);
            }

            // Network: if server, notify clients of this removal.
            var bridge = GASServices.NetworkBridge;
            if (bridge.IsServer && ReplicationMode != EReplicationMode.Minimal && effect.NetworkId != 0)
                bridge.ServerReplicateEffectRemoved(this, effect.NetworkId);

            effect.ReturnToPool();
        }

        private static void DispatchGameplayCues(GameplayEffectSpec spec, EGameplayCueEvent eventType)
        {
            if (spec == null || spec.Def == null || spec.Def.SuppressGameplayCues || spec.Def.GameplayCues.IsEmpty)
            {
                return;
            }

            if (spec.Target != null && spec.Target.SuppressLocalGameplayCueDispatch)
            {
                return;
            }

            var parameters = new GameplayCueParameters(spec);
            foreach (var cueTag in spec.Def.GameplayCues)
            {
                if (cueTag.IsNone)
                {
                    continue;
                }

                GameplayCueManager.Default.HandleCue(cueTag, eventType, parameters).Forget();
                if (spec.Context != null && spec.Context.PredictionKey.IsValid && spec.Target != null)
                {
                    spec.Target.IncrementPredictionWindowGameplayCueCount(spec.Context.PredictionKey);
                }

                if (eventType == EGameplayCueEvent.OnActive)
                {
                    GameplayCueManager.Default.HandleCue(cueTag, EGameplayCueEvent.WhileActive, parameters).Forget();
                    if (spec.Context != null && spec.Context.PredictionKey.IsValid && spec.Target != null)
                    {
                        spec.Target.IncrementPredictionWindowGameplayCueCount(spec.Context.PredictionKey);
                    }
                }

                // Network: broadcast cue to remote clients.
                // GASNullNetworkBridge is a no-op here (same process already dispatched locally above).
                var bridge = GASServices.NetworkBridge;
                if (bridge.IsServer)
                {
                    var resolver = GASServices.ReplicationResolver;
                    var cueParams = new GASCueNetParams(
                        sourceAscNetId: spec.Source != null ? resolver.GetAbilitySystemNetworkId(spec.Source) : 0,
                        targetAscNetId: spec.Target != null ? resolver.GetAbilitySystemNetworkId(spec.Target) : 0,
                        magnitude: 0f,
                        normalizedMagnitude: 0f,
                        predictionKey: spec.Context?.PredictionKey ?? default);
                    bridge.ServerBroadcastGameplayCue(spec.Target, cueTag, eventType, cueParams);
                }
            }
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
                effect.NetworkId = System.Threading.Interlocked.Increment(ref s_NextEffectNetworkId);
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
            float[] setByCallerValues;
            int setByCallerCount = effect.Spec.SetByCallerTagMagnitudeCount;

            if (setByCallerCount <= 0)
            {
                setByCallerTags = Array.Empty<GameplayTag>();
                setByCallerValues = Array.Empty<float>();
                setByCallerCount = 0;
            }
            else
            {
                EnsureEffectReplicationSetByCallerCapacity(setByCallerCount);
                setByCallerCount = effect.Spec.CopySetByCallerTagMagnitudes(
                    effectReplicationSetByCallerTags,
                    effectReplicationSetByCallerValues);
                setByCallerTags = setByCallerCount > 0 ? effectReplicationSetByCallerTags : Array.Empty<GameplayTag>();
                setByCallerValues = setByCallerCount > 0 ? effectReplicationSetByCallerValues : Array.Empty<float>();
            }

            return new GASEffectReplicationData
            {
                NetworkId = effect.NetworkId,
                EffectDefId = resolver.GetGameplayEffectDefinitionId(effect.Spec.Def),
                SourceAscNetId = effect.Spec.Source != null ? resolver.GetAbilitySystemNetworkId(effect.Spec.Source) : 0,
                TargetAscNetId = resolver.GetAbilitySystemNetworkId(this),
                Level = effect.Spec.Level,
                StackCount = effect.StackCount,
                Duration = effect.Spec.Duration,
                TimeRemaining = effect.TimeRemaining,
                PeriodTimeRemaining = effect.PeriodTimeRemaining,
                PredictionKey = effect.Spec.Context?.PredictionKey ?? default,
                SetByCallerTags = setByCallerTags,
                SetByCallerValues = setByCallerValues,
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
            Array.Resize(ref effectReplicationSetByCallerValues, next);
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

            for (int i = 0; i < activatableAbilities.Count; i++)
            {
                activatableAbilities[i].GetPrimaryInstance()?.OnRemoveAbility();
            }

            activeEffects.Clear();
            activeEffectIndexByEffect.Clear();
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
            pendingPredictedEffects.Clear();
            predictedAttributeSnapshots.Clear();
            predictionWindows.Clear();
            predictionWindowIndexByKey.Clear();
            Array.Clear(predictionTransactionRecords, 0, predictionTransactionRecords.Length);
            predictionTransactionRecordCursor = 0;
            predictionTransactionRecordCount = 0;
            currentPredictionKey = default;
            localPredictionInputSequence = 0;
            totalPredictionWindowsOpened = 0;
            totalPredictionWindowsConfirmed = 0;
            totalPredictionWindowsRejected = 0;
            totalPredictionWindowsTimedOut = 0;
            stalePredictionConfirmCount = 0;
            stalePredictionRejectCount = 0;
            coreSpecHandles.Clear();
            coreActiveEffectHandles.Clear();
            coreState.Reset(default);
            abilityAppliedEffects.Clear();
            abilityAppliedEffectRemovalSet.Clear();
            triggerEventAbilities.Clear();
            triggerTagAddedAbilities.Clear();
            triggerTagRemovedAbilities.Clear();
            pendingRemovedEffectNetIds.Clear();
            pendingRemovedAbilityDefs.Clear();
            stateVersion = 0;
            lastReplicatedStateVersion = 0;
            outgoingDeltaSequence = 0;
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

        //  O(1) stacking lookup using dictionary index instead of O(n) linear search
        private bool HandleStacking(GameplayEffectSpec spec)
        {
            if (spec.Def.Stacking.Type == EGameplayEffectStackingType.None)
            {
                return false;
            }

            ActiveGameplayEffect existingEffect = null;

            // O(1) lookup using stacking index
            if (spec.Def.Stacking.Type == EGameplayEffectStackingType.AggregateByTarget)
            {
                stackingIndexByTarget.TryGetValue(spec.Def, out existingEffect);
            }
            else if (spec.Def.Stacking.Type == EGameplayEffectStackingType.AggregateBySource)
            {
                stackingIndexBySource.TryGetValue((spec.Def, spec.Source), out existingEffect);
            }

            // Validate the found effect is still valid
            if (existingEffect == null || existingEffect.IsExpired)
            {
                // Index is stale, remove it
                if (existingEffect != null)
                {
                    RemoveFromStackingIndex(existingEffect);
                }
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
            if (!effect.Spec.Def.GrantedTags.IsEmpty)
            {
                var en = effect.Spec.Def.GrantedTags.GetExplicitTags();
                while (en.MoveNext())
                    AddGrantedTagIndexEntry(en.Current.RuntimeIndex, effect);
            }
            if (!effect.Spec.DynamicGrantedTags.IsEmpty)
            {
                var en = effect.Spec.DynamicGrantedTags.GetExplicitTags();
                while (en.MoveNext())
                    AddGrantedTagIndexEntry(en.Current.RuntimeIndex, effect);
            }
        }

        /// <summary>
        /// Removes all explicitly-granted tags of an effect from the grantedTagIndexToEffects dictionary.
        /// </summary>
        private void UpdateGrantedTagIndex_Removed(ActiveGameplayEffect effect)
        {
            if (!effect.Spec.Def.GrantedTags.IsEmpty)
            {
                var en = effect.Spec.Def.GrantedTags.GetExplicitTags();
                while (en.MoveNext())
                    RemoveGrantedTagIndexEntry(en.Current.RuntimeIndex, effect);
            }
            if (!effect.Spec.DynamicGrantedTags.IsEmpty)
            {
                var en = effect.Spec.DynamicGrantedTags.GetExplicitTags();
                while (en.MoveNext())
                    RemoveGrantedTagIndexEntry(en.Current.RuntimeIndex, effect);
            }
        }

        private void AddGrantedTagIndexEntry(int runtimeIndex, ActiveGameplayEffect effect)
        {
            if (!grantedTagIndexToEffects.TryGetValue(runtimeIndex, out var effects))
            {
                effects = new List<ActiveGameplayEffect>(2);
                grantedTagIndexToEffects.Add(runtimeIndex, effects);
            }

            if (!effects.Contains(effect))
                effects.Add(effect);
        }

        private void RemoveGrantedTagIndexEntry(int runtimeIndex, ActiveGameplayEffect effect)
        {
            if (!grantedTagIndexToEffects.TryGetValue(runtimeIndex, out var effects))
                return;

            effects.Remove(effect);
            if (effects.Count == 0)
                grantedTagIndexToEffects.Remove(runtimeIndex);
        }
        private void ApplyModifier(GameplayEffectSpec spec, GameplayAttribute attribute, ModifierInfo mod, float magnitude, bool isFromExecution)
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
            var callbackData = new GameplayEffectModCallbackData(spec, mod, magnitude, this);
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

        private ulong stateVersion;
        private ulong lastReplicatedStateVersion;
        private uint outgoingDeltaSequence;
        private readonly HashSet<string> dirtyAttributeNames = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<GameplayTag> pendingAddedTags = new HashSet<GameplayTag>();
        private readonly HashSet<GameplayTag> pendingRemovedTags = new HashSet<GameplayTag>();
        // Delta tracking: NetworkIds of effects removed since last snapshot
        private readonly List<int> pendingRemovedEffectNetIds = new List<int>(8);
        // Delta tracking: ability definitions removed since last snapshot
        private readonly List<IGASAbilityDefinition> pendingRemovedAbilityDefs = new List<IGASAbilityDefinition>(8);
        private bool grantedAbilitiesDirty;
        private bool activeEffectsDirty;
        private bool attributeStructureDirty;
        private bool tagsDirty;

        public ulong StateVersion => stateVersion;
        public AbilitySystemStateChangeMask PendingStateChangeMask
        {
            get
            {
                var mask = AbilitySystemStateChangeMask.None;
                if (grantedAbilitiesDirty)
                    mask |= AbilitySystemStateChangeMask.GrantedAbilities;
                if (activeEffectsDirty)
                    mask |= AbilitySystemStateChangeMask.ActiveEffects;
                if (attributeStructureDirty || dirtyAttributeNames.Count > 0)
                    mask |= AbilitySystemStateChangeMask.Attributes;
                if (tagsDirty)
                    mask |= AbilitySystemStateChangeMask.Tags;
                return mask;
            }
        }

        public bool IsAttributeStructureDirty => attributeStructureDirty;
        public IReadOnlyCollection<string> DirtyAttributeNames => dirtyAttributeNames;
        public IReadOnlyCollection<GameplayTag> PendingAddedTags => pendingAddedTags;
        public IReadOnlyCollection<GameplayTag> PendingRemovedTags => pendingRemovedTags;

        public void CapturePendingStateDeltaNonAlloc(GASAbilitySystemStateDeltaBuffer buffer, Func<ActiveGameplayEffect, int> effectInstanceIdResolver = null)
        {
            if (buffer == null)
            {
                return;
            }

            buffer.ClearCounts();
            buffer.BaseVersion = lastReplicatedStateVersion;

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
                    : FillDirtyAttributes(buffer.EnsureAttributeCapacity(dirtyAttributeNames.Count));
            }

            if (tagsDirty)
            {
                buffer.ChangeMask |= AbilitySystemStateChangeMask.Tags;
                buffer.AddedTagCount = CopyTagsNonAlloc(pendingAddedTags, buffer.EnsureAddedTagCapacity(pendingAddedTags.Count));
                buffer.RemovedTagCount = CopyTagsNonAlloc(pendingRemovedTags, buffer.EnsureRemovedTagCapacity(pendingRemovedTags.Count));
            }

            ClearPendingStateChanges();
            buffer.CurrentVersion = stateVersion;
            buffer.StateChecksum = ComputeReplicatedStateChecksum();
            lastReplicatedStateVersion = stateVersion;
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

            buffer.Sequence = ++outgoingDeltaSequence;
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

                entries[i] = new GASActiveEffectStateData(
                    effectInstanceIdResolver != null ? effectInstanceIdResolver(effect) : 0,
                    effect.Spec.Def,
                    effect.Spec.Source,
                    effect.Spec.Level,
                    effect.StackCount,
                    effect.Spec.Duration,
                    effect.TimeRemaining,
                    effect.PeriodTimeRemaining,
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
                    entries[index++] = new GASAttributeStateData(attr.Name, attr.BaseValue, attr.CurrentValue);
                }
            }

            return index;
        }

        private int FillDirtyAttributes(GASAttributeStateData[] entries)
        {
            int index = 0;
            foreach (var attributeName in dirtyAttributeNames)
            {
                if (attributes.TryGetValue(attributeName, out var attribute))
                {
                    entries[index++] = new GASAttributeStateData(attribute.Name, attribute.BaseValue, attribute.CurrentValue);
                }
            }

            return index;
        }

        private static int CopyTagsNonAlloc(HashSet<GameplayTag> tags, GameplayTag[] entries)
        {
            int index = 0;
            foreach (var tag in tags)
            {
                entries[index++] = tag;
            }

            return index;
        }

        private void ClearPendingStateChanges()
        {
            grantedAbilitiesDirty = false;
            activeEffectsDirty = false;
            attributeStructureDirty = false;
            tagsDirty = false;
            dirtyAttributeNames.Clear();
            pendingAddedTags.Clear();
            pendingRemovedTags.Clear();
            pendingRemovedEffectNetIds.Clear();
            pendingRemovedAbilityDefs.Clear();
        }

        public void ConsumePendingStateChanges()
        {
            ClearPendingStateChanges();
        }

        private void MarkGrantedAbilitiesDirty()
        {
            grantedAbilitiesDirty = true;
            stateVersion++;
        }

        private void MarkActiveEffectsDirty()
        {
            activeEffectsDirty = true;
            stateVersion++;
        }

        private void MarkAttributeValueDirty(GameplayAttribute attribute)
        {
            if (attribute == null)
            {
                return;
            }

            dirtyAttributeNames.Add(attribute.Name);
            stateVersion++;
        }

        private void MarkAttributeStructureDirty()
        {
            attributeStructureDirty = true;
            stateVersion++;
        }

        private void TrackTagCountChange(GameplayTag tag, int newCount)
        {
            if (!tag.IsValid || tag.IsNone)
            {
                return;
            }

            tagsDirty = true;
            if (newCount > 0)
            {
                pendingRemovedTags.Remove(tag);
                pendingAddedTags.Add(tag);
            }
            else
            {
                pendingAddedTags.Remove(tag);
                pendingRemovedTags.Add(tag);
            }

            // Bug fix: when a tag actually appears or disappears, any active effect whose
            // OngoingTagRequirements reference this tag may change inhibition state.
            // Mark those effects' attributes dirty so RecalculateDirtyAttributes() (which
            // updates IsInhibited) runs at the start of the next Tick().
            MarkAttributesDirtyForEffectsWithOngoingRequirements();

            stateVersion++;
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
                attr.OwningSet.SetBaseValue(attr, entry.BaseValue);
                attr.OwningSet.SetCurrentValue(attr, entry.CurrentValue);
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
            for (int j = activeEffects.Count - 1; j >= 0; j--)
            {
                var toRemove = activeEffects[j];
                if (toRemove.NetworkId == netId)
                {
                    RemoveActiveEffectAtIndex(j);
                    RemoveFromStackingIndex(toRemove);
                    OnEffectRemoved(toRemove, true);
                    break;
                }
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
                Duration = snap.Duration,
                TimeRemaining = snap.TimeRemaining,
                PeriodTimeRemaining = snap.PeriodTimeRemaining,
                PredictionKey = snap.PredictionKey,
            };

            if (snap.SetByCallerTagMagnitudes != null && snap.SetByCallerTagMagnitudeCount > 0)
            {
                int count = Math.Min(snap.SetByCallerTagMagnitudeCount, snap.SetByCallerTagMagnitudes.Length);
                EnsureStateApplySetByCallerCapacity(count);
                for (int j = 0; j < count; j++)
                {
                    stateApplySetByCallerTags[j] = snap.SetByCallerTagMagnitudes[j].Tag;
                    stateApplySetByCallerValues[j] = snap.SetByCallerTagMagnitudes[j].Value;
                }
                data.SetByCallerTags = stateApplySetByCallerTags;
                data.SetByCallerValues = stateApplySetByCallerValues;
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
            Array.Resize(ref stateApplySetByCallerValues, next);
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
