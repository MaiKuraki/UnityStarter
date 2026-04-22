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
        // Expose as IReadOnlyList without AsReadOnly() wrapper allocation.
        // List<T> implements IReadOnlyList<T> directly since .NET 4.5.
        public IReadOnlyList<ActiveGameplayEffect> ActiveEffects => activeEffects;

        //  O(1) stacking index — avoids linear search per ApplyGameplayEffectSpecToSelf
        // Key: GameplayEffect def → first matching ActiveGameplayEffect (for AggregateByTarget)
        private readonly Dictionary<GameplayEffect, ActiveGameplayEffect> stackingIndexByTarget = new Dictionary<GameplayEffect, ActiveGameplayEffect>(16);
        // Key: (GameplayEffect def, source ASC) → ActiveGameplayEffect (for AggregateBySource)
        private readonly Dictionary<(GameplayEffect, AbilitySystemComponent), ActiveGameplayEffect> stackingIndexBySource = new Dictionary<(GameplayEffect, AbilitySystemComponent), ActiveGameplayEffect>(16);

        // Explicit-GrantedTag runtime-index -> ActiveGameplayEffects for O(1) tag lookup,
        // while preserving all contributors for correct multi-effect semantics.
        // Maintained in OnEffectApplied / OnEffectRemoved.
        private readonly Dictionary<int, List<ActiveGameplayEffect>> grantedTagIndexToEffects = new Dictionary<int, List<ActiveGameplayEffect>>(16);

        private readonly List<GameplayAbilitySpec> activatableAbilities = new List<GameplayAbilitySpec>(16);
        public IReadOnlyList<GameplayAbilitySpec> GetActivatableAbilities() => activatableAbilities;

        private readonly List<GameplayAbilitySpec> tickingAbilities = new List<GameplayAbilitySpec>(16);

        private readonly List<GameplayAttribute> dirtyAttributes = new List<GameplayAttribute>(32);

        //  Tracks effects applied by abilities for RemoveGameplayEffectsAfterAbilityEnds
        private readonly Dictionary<GameplayAbility, List<ActiveGameplayEffect>> abilityAppliedEffects = new Dictionary<GameplayAbility, List<ActiveGameplayEffect>>(8);

        [ThreadStatic]
        private static List<ModifierInfo> executionOutputScratchPad;

        // --- Prediction ---
        private PredictionKey currentPredictionKey;
        private readonly List<ActiveGameplayEffect> pendingPredictedEffects = new List<ActiveGameplayEffect>();

        //  Attribute snapshot for rolling back instant-effect attribute changes on prediction failure.
        // Populated in ApplyModifier when currentPredictionKey.IsValid(); cleared on confirm or rollback.
        private readonly List<(GameplayAttribute attr, float oldBaseValue)> predictedAttributeSnapshots = new List<(GameplayAttribute, float)>(8);

        // --- Network ---
        // Monotonically-increasing counter for assigning stable NetworkIds to replicated ActiveGameplayEffects.
        // Only the server assigns NetworkIds; clients receive them via ClientReceiveEffectApplied.
        private static int s_NextEffectNetworkId;
        private int networkReplicationScopeDepth;

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
                    }
                    else
                    {
                        GASLog.Warning($"Attribute '{attr.Name}' is already present. Duplicate attributes are not allowed.");
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
        }

        public GameplayAttribute GetAttribute(string name)
        {
            attributes.TryGetValue(name, out var attribute);
            return attribute;
        }

        public GameplayAbilitySpec GrantAbility(GameplayAbility ability, int level = 1)
        {
            if (ability == null) return null;

            var spec = GameplayAbilitySpec.Create(ability, level);
            spec.Init(this);

            activatableAbilities.Add(spec);
            spec.GetPrimaryInstance().OnGiveAbility(cachedActorInfo, spec);
            MarkGrantedAbilitiesDirty();

            // UE5: Register ability triggers (FAbilityTriggerData)
            RegisterAbilityTriggers(spec);

            // UE5: bActivateAbilityOnGranted — auto-activate passive abilities
            if (ability.ActivateAbilityOnGranted)
            {
                TryActivateAbility(spec);
            }

            return spec;
        }

        public void ClearAbility(GameplayAbilitySpec spec)
        {
            if (spec == null) return;

            // UE5: Unregister ability triggers before removal
            UnregisterAbilityTriggers(spec);

            spec.OnRemoveSpec();
            //  Swap-with-last O(1) removal instead of O(n) List.Remove
            int index = activatableAbilities.IndexOf(spec);
            if (index >= 0)
            {
                int lastIndex = activatableAbilities.Count - 1;
                if (index != lastIndex)
                {
                    activatableAbilities[index] = activatableAbilities[lastIndex];
                }
                activatableAbilities.RemoveAt(lastIndex);
            }

            // Delta tracking: record this definition as removed so the next delta snapshot can tell clients.
            var removedDef = spec.AbilityCDO ?? spec.Ability;
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
                    var predictionKey = PredictionKey.NewKey();
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
        public void ServerReceiveTryActivateAbility(int specHandle, PredictionKey predictionKey)
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
        public void ClientReceiveActivationSucceeded(int specHandle, PredictionKey predictionKey)
        {
            var spec = FindSpecByHandle(specHandle);
            if (spec != null) ClientActivateAbilitySucceed(spec, predictionKey);
        }

        /// <summary>
        /// Client entry point: server rejected our predicted activation — roll back.
        /// Called by GASNullNetworkBridge immediately, or by the network bridge on RPC receipt.
        /// </summary>
        public void ClientReceiveActivationFailed(int specHandle, PredictionKey predictionKey)
        {
            var spec = FindSpecByHandle(specHandle);
            if (spec != null) ClientActivateAbilityFailed(spec, predictionKey);
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
                0f);

            GameplayCueManager.Default.HandleCue(cueTag, eventType, new GameplayCueParameters(parameters)).Forget();
        }

        /// <summary>
        /// Client entry point: server replicated a delta snapshot of changed attribute values.
        /// </summary>
        public void ClientReceiveAttributeSnapshot(GameplayAttributeStateSnapshot[] snapshot)
        {
            if (snapshot == null || snapshot.Length == 0) return;
            ApplyAuthorityAttributeSnapshot(snapshot);
        }

        /// <summary>
        /// Client entry point: server forced a full ASC state resync (reconnect, late-join, cheat rollback).
        /// </summary>
        public void ClientReceiveFullSync(in AbilitySystemStateSnapshot snapshot)
        {
            ApplyStateSnapshot(in snapshot);
        }

        /// <summary>
        /// Client entry point: server sent an incremental delta snapshot.
        /// Only sections flagged in ChangeMask are applied.
        /// </summary>
        public void ClientReceiveDeltaSnapshot(in AbilitySystemStateDeltaSnapshot delta)
        {
            ApplyDeltaSnapshot(in delta);
        }

        /// <summary>
        /// Fired on clients when the server sends a replicated effect application.
        /// Subscribe to resolve data.EffectDefId and call ApplyGameplayEffectSpecToSelf.
        /// </summary>
        public event Action<GASEffectReplicationData> OnClientEffectApplied;

        private bool SuppressLocalGameplayCueDispatch => networkReplicationScopeDepth > 0 && !GASServices.NetworkBridge.IsServer;

        private GameplayAbilitySpec FindSpecByHandle(int handle)
        {
            for (int i = 0; i < activatableAbilities.Count; i++)
                if (activatableAbilities[i].Handle == handle) return activatableAbilities[i];
            return null;
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

        private ActiveGameplayEffect FindPredictedEffectForReconcile(GameplayEffect effectDef, AbilitySystemComponent source, PredictionKey predictionKey)
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

                if (predictionKey.IsValid())
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

            if (data.PredictionKey.IsValid())
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

        private void RemovePendingPredictedEffects(PredictionKey predictionKey)
        {
            if (!predictionKey.IsValid())
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
            tickingAbilities.Add(spec);
            MarkGrantedAbilitiesDirty();

            // UE5: Cancel abilities with matching tags
            if (ability.CancelAbilitiesWithTag != null && !ability.CancelAbilitiesWithTag.IsEmpty)
            {
                CancelAbilitiesWithTags(ability.CancelAbilitiesWithTag);
            }

            // Apply ActivationOwnedTags — incremental, no full rebuild
            if (ability.ActivationOwnedTags != null && !ability.ActivationOwnedTags.IsEmpty)
            {
                looseTags.AddTags(ability.ActivationOwnedTags);
                CombinedTags.AddTags(ability.ActivationOwnedTags);
            }

            ability.ActivateAbility(cachedActorInfo, spec, activationInfo);

            OnAbilityActivated?.Invoke(ability);

            // Clear prediction key after atomic activation
            this.currentPredictionKey = default;
        }

        private void ClientActivateAbilitySucceed(GameplayAbilitySpec spec, PredictionKey predictionKey)
        {
            if (!predictionKey.IsValid()) return;
            RemovePendingPredictedEffects(predictionKey);
            //  Prediction confirmed — discard instant-effect attribute snapshots; changes are now authoritative.
            predictedAttributeSnapshots.Clear();
        }

        private void ClientActivateAbilityFailed(GameplayAbilitySpec spec, PredictionKey predictionKey)
        {
            if (!predictionKey.IsValid()) return;

            // P0+ Use zero-GC StringBuilder overload for Warning (avoids string interpolation alloc in Release)
            GASLog.Warning(sb => sb.Append("Client prediction failed for ability '").Append(spec.Ability.Name)
                .Append("' with key ").Append(predictionKey.Key).Append(". Rolling back."));

            // Find and remove all effects applied with this failed prediction key.
            using (CycloneGames.GameplayTags.Runtime.Pools.ListPool<ActiveGameplayEffect>.Get(out var toRemove))
            {
                for (int i = 0; i < pendingPredictedEffects.Count; i++)
                {
                    var effect = pendingPredictedEffects[i];
                    if (effect.Spec.Context.PredictionKey.Equals(predictionKey))
                    {
                        toRemove.Add(effect);
                    }
                }

                for (int i = 0; i < toRemove.Count; i++)
                {
                    var effect = toRemove[i];

                    //  Swap-with-last O(1) removal from activeEffects (avoids O(n) element shift)
                    int aeIdx = activeEffects.IndexOf(effect);
                    if (aeIdx >= 0)
                    {
                        int lastAe = activeEffects.Count - 1;
                        if (aeIdx != lastAe) activeEffects[aeIdx] = activeEffects[lastAe];
                        activeEffects.RemoveAt(lastAe);
                    }

                    //  Swap-with-last O(1) removal from pendingPredictedEffects
                    int peIdx = pendingPredictedEffects.IndexOf(effect);
                    if (peIdx >= 0)
                    {
                        int lastPe = pendingPredictedEffects.Count - 1;
                        if (peIdx != lastPe) pendingPredictedEffects[peIdx] = pendingPredictedEffects[lastPe];
                        pendingPredictedEffects.RemoveAt(lastPe);
                    }

                    OnEffectRemoved(effect, false); // Don't re-dirty attributes on rollback
                }

                //  Restore instant-effect attribute BaseValues that were modified under this prediction key.
                // RecalculateDirtyAttributes() in the next Tick will recompute CurrentValues correctly.
                for (int i = predictedAttributeSnapshots.Count - 1; i >= 0; i--)
                {
                    var (attr, oldBase) = predictedAttributeSnapshots[i];
                    attr.SetBaseValue(oldBase);
                    MarkAttributeDirty(attr);
                }
                predictedAttributeSnapshots.Clear();
            }

            // Immediately end the ability on the client.
            spec.GetPrimaryInstance()?.CancelAbility();
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
                    //  Swap-with-last for tickingAbilities removal
                    int tickIdx = tickingAbilities.IndexOf(ability.Spec);
                    if (tickIdx >= 0)
                    {
                        int lastIdx = tickingAbilities.Count - 1;
                        if (tickIdx != lastIdx)
                        {
                            tickingAbilities[tickIdx] = tickingAbilities[lastIdx];
                        }
                        tickingAbilities.RemoveAt(lastIdx);
                    }

                    // Remove ActivationOwnedTags when ability ends — incremental, no full rebuild
                    if (ability.ActivationOwnedTags != null && !ability.ActivationOwnedTags.IsEmpty)
                    {
                        looseTags.RemoveTags(ability.ActivationOwnedTags);
                        CombinedTags.RemoveTags(ability.ActivationOwnedTags);
                    }

                    // UE5: RemoveGameplayEffectsAfterAbilityEnds — remove effects this ability applied
                    if (abilityAppliedEffects.TryGetValue(ability, out var appliedEffects))
                    {
                        //  Build a HashSet for O(1) membership test, avoiding O(n²) IndexOf per effect.
                        var toRemoveSet = new System.Collections.Generic.HashSet<ActiveGameplayEffect>(appliedEffects.Count);
                        for (int ei = 0; ei < appliedEffects.Count; ei++)
                            if (!appliedEffects[ei].IsExpired) toRemoveSet.Add(appliedEffects[ei]);

                        if (toRemoveSet.Count > 0)
                        {
                            using (Pools.ListPool<ActiveGameplayEffect>.Get(out var removed))
                            {
                                // Single backward pass over activeEffects: O(n) total instead of O(n²)
                                for (int i = activeEffects.Count - 1; i >= 0; i--)
                                {
                                    if (toRemoveSet.Contains(activeEffects[i]))
                                    {
                                        removed.Add(activeEffects[i]);
                                        RemoveActiveEffectAtIndex(i);
                                    }
                                }
                                for (int i = 0; i < removed.Count; i++)
                                    OnEffectRemoved(removed[i], true);
                            }
                        }

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
            if (currentPredictionKey.IsValid())
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
            activeEffects.Add(newActiveEffect);

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

            if (currentPredictionKey.IsValid())
            {
                pendingPredictedEffects.Add(newActiveEffect);
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

            GASLog.Info($"{OwnerActor} Apply GameplayEffect '{spec.Def.Name}' to self.");
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

                foreach (var effect in removedEffects)
                {
                    OnEffectRemoved(effect, true);
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
                    GASLog.Debug($"Apply GameplayEffect '{spec.Def.Name}' blocked: target has immunity to effect's tags.");
                    return false;
                }
                // Also check dynamic tags on the spec instance
                if (!spec.DynamicAssetTags.IsEmpty && spec.DynamicAssetTags.HasAny(ImmunityTags))
                {
                    GASLog.Debug($"Apply GameplayEffect '{spec.Def.Name}' blocked: target has immunity to effect's dynamic asset tags.");
                    return false;
                }
                if (!spec.DynamicGrantedTags.IsEmpty && spec.DynamicGrantedTags.HasAny(ImmunityTags))
                {
                    GASLog.Debug($"Apply GameplayEffect '{spec.Def.Name}' blocked: target has immunity to effect's dynamic granted tags.");
                    return false;
                }
            }

            if (!HasAllMatchingGameplayTags(spec.Def.ApplicationRequiredTagsSnapshot))
            {
                GASLog.Debug($"Apply GameplayEffect '{spec.Def.Name}' failed: does not meet application tag requirements (Required).");
                return false;
            }
            if (HasAnyMatchingGameplayTags(spec.Def.ApplicationForbiddenTagsSnapshot))
            {
                GASLog.Debug($"Apply GameplayEffect '{spec.Def.Name}' failed: does not meet application tag requirements (Ignored).");
                return false;
            }

            //  Custom Application Requirements (UE5: UGameplayEffectCustomApplicationRequirement)
            var requirements = spec.Def.CustomApplicationRequirements;
            for (int i = 0; i < requirements.Count; i++)
            {
                if (!requirements[i].CanApplyGameplayEffect(spec, this))
                {
                    GASLog.Debug($"Apply GameplayEffect '{spec.Def.Name}' blocked by custom application requirement.");
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
                foreach (var modInfo in executionOutputScratchPad)
                {
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

            // Replicate attributes changed during this server tick to all relevant clients.
            if (isServer && ReplicationMode != EReplicationMode.Minimal && dirtyAttributeNames.Count > 0)
            {
                var attrSnapshot = CaptureDirtyAttributesSnapshot();
                dirtyAttributeNames.Clear();
                if (attrSnapshot.Length > 0)
                {
                    GASServices.NetworkBridge.ServerReplicateAttributeSnapshot(this, attrSnapshot);
                }
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
            int lastIndex = activeEffects.Count - 1;
            if (index != lastIndex)
            {
                activeEffects[index] = activeEffects[lastIndex];
            }
            activeEffects.RemoveAt(lastIndex);
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
            //  Incremental tag update — add to both containers directly, no full rebuild
            if (!effect.Spec.Def.GrantedTags.IsEmpty)
            {
                fromEffectsTags.AddTags(effect.Spec.Def.GrantedTags);
                CombinedTags.AddTags(effect.Spec.Def.GrantedTags);
            }
            // UE5: DynamicGrantedTags — spec-instance-level tags
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

                if (attribute != null)
                {
                    attribute.AffectingEffects.Add(effect);
                }
            }

            // Grant abilities via GrantingEffect back-reference (0 GC, no buffer needed)
            if (effect.Spec.Def.GrantedAbilities.Count > 0)
            {
                foreach (var ability in effect.Spec.Def.GrantedAbilities)
                {
                    var newSpec = GrantAbility(ability, effect.Spec.Level);
                    newSpec.GrantingEffect = effect;
                }
            }

            //  SuppressGameplayCues — skip all cue handling when suppressed
            if (!SuppressLocalGameplayCueDispatch)
            {
                DispatchGameplayCues(effect.Spec, EGameplayCueEvent.OnActive);
            }

            //  Maintain grantedTag runtime index for O(1) cooldown and tag effect queries.
            UpdateGrantedTagIndex_Applied(effect);

            ReplicateEffectApplied(effect);

            OnGameplayEffectAppliedToSelf?.Invoke(effect);
        }

        private void OnEffectRemoved(ActiveGameplayEffect effect, bool markDirty)
        {
            //  Incremental tag update — remove from both containers directly, no full rebuild
            if (!effect.Spec.Def.GrantedTags.IsEmpty)
            {
                fromEffectsTags.RemoveTags(effect.Spec.Def.GrantedTags);
                CombinedTags.RemoveTags(effect.Spec.Def.GrantedTags);
            }
            // UE5: DynamicGrantedTags — spec-instance-level tags
            if (!effect.Spec.DynamicGrantedTags.IsEmpty)
            {
                fromEffectsTags.RemoveTags(effect.Spec.DynamicGrantedTags);
                CombinedTags.RemoveTags(effect.Spec.DynamicGrantedTags);
            }

            //  Remove from grantedTag index before the effect’s tag data is invalidated by ReturnToPool.
            UpdateGrantedTagIndex_Removed(effect);

            // Remove from attribute's internal list
            var modifiers = effect.Spec.Def.Modifiers;
            for (int i = 0; i < modifiers.Count; i++)
            {
                var attribute = effect.Spec.TargetAttributes[i];
                if (attribute == null) attribute = GetAttribute(modifiers[i].AttributeName);

                if (attribute != null)
                {
                    int index = attribute.AffectingEffects.IndexOf(effect);
                    if (index >= 0)
                    {
                        int lastIndex = attribute.AffectingEffects.Count - 1;
                        if (index != lastIndex)
                        {
                            attribute.AffectingEffects[index] = attribute.AffectingEffects[lastIndex];
                        }
                        attribute.AffectingEffects.RemoveAt(lastIndex);
                    }
                }
            }

            // Remove abilities granted by this effect via GrantingEffect back-reference (0 GC)
            for (int i = activatableAbilities.Count - 1; i >= 0; i--)
            {
                if (activatableAbilities[i].GrantingEffect == effect)
                {
                    ClearAbility(activatableAbilities[i]);
                }
            }

            //  SuppressGameplayCues — skip cue handling when suppressed
            if (!SuppressLocalGameplayCueDispatch &&
                !effect.Spec.Def.SuppressGameplayCues &&
                effect.Spec.Def.DurationPolicy != EDurationPolicy.Instant && !effect.Spec.Def.GameplayCues.IsEmpty)
            {
                DispatchGameplayCues(effect.Spec, EGameplayCueEvent.Removed);
            }

            OnGameplayEffectRemovedFromSelf?.Invoke(effect);

            if (markDirty) MarkAttributesDirtyFromEffect(effect);
            MarkActiveEffectsDirty();

            // Delta tracking: record this NetworkId as removed so the next delta snapshot includes it.
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

                if (eventType == EGameplayCueEvent.OnActive)
                {
                    GameplayCueManager.Default.HandleCue(cueTag, EGameplayCueEvent.WhileActive, parameters).Forget();
                }

                // Network: broadcast cue to remote clients.
                // GASNullNetworkBridge is a no-op here (same process already dispatched locally above).
                var bridge = GASServices.NetworkBridge;
                if (bridge.IsServer)
                {
                    var resolver = GASServices.ReplicationResolver;
                    var cueParams = new GASCueNetParams(
                        sourceAscNetId:      spec.Source != null ? resolver.GetAbilitySystemNetworkId(spec.Source) : 0,
                        targetAscNetId:      spec.Target != null ? resolver.GetAbilitySystemNetworkId(spec.Target) : 0,
                        magnitude:           0f,
                        normalizedMagnitude: 0f);
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
            var setByCaller = effect.Spec.CaptureSetByCallerTagMagnitudes();
            GameplayTag[] setByCallerTags;
            float[] setByCallerValues;
            int setByCallerCount;

            if (setByCaller.Length == 0)
            {
                setByCallerTags = Array.Empty<GameplayTag>();
                setByCallerValues = Array.Empty<float>();
                setByCallerCount = 0;
            }
            else
            {
                setByCallerCount = setByCaller.Length;
                setByCallerTags = new GameplayTag[setByCallerCount];
                setByCallerValues = new float[setByCallerCount];
                for (int i = 0; i < setByCallerCount; i++)
                {
                    setByCallerTags[i] = setByCaller[i].Tag;
                    setByCallerValues[i] = setByCaller[i].Value;
                }
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

        // --- Tag Management ---
        //  Incremental tag updates — directly add/remove from CombinedTags, no full rebuild
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

            int idx = activeEffects.IndexOf(effect);
            if (idx < 0) return false;

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

            int index = activeEffects.IndexOf(effect);
            if (index < 0)
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

            int index = activeEffects.IndexOf(effect);
            if (index < 0)
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

            int index = activeEffects.IndexOf(effect);
            if (index < 0)
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
            foreach (var effect in activeEffects) effect.ReturnToPool();
            foreach (var abilitySpec in activatableAbilities) abilitySpec.GetPrimaryInstance()?.OnRemoveAbility();

            activeEffects.Clear();
            attributeSets.Clear();
            attributes.Clear();
            activatableAbilities.Clear();
            looseTags.Clear();
            fromEffectsTags.Clear();
            CombinedTags.Clear();
            dirtyAttributes.Clear();
            eventDelegates.Clear();
            stackingIndexByTarget.Clear();
            stackingIndexBySource.Clear();
            grantedTagIndexToEffects.Clear();
            predictedAttributeSnapshots.Clear();
            abilityAppliedEffects.Clear();
            triggerEventAbilities.Clear();
            triggerTagAddedAbilities.Clear();
            triggerTagRemovedAbilities.Clear();
            pendingRemovedEffectNetIds.Clear();
            pendingRemovedAbilityDefs.Clear();
            OnGameplayEffectAppliedToSelf = null;
            OnGameplayEffectRemovedFromSelf = null;
            OnAbilityActivated = null;
            OnAbilityEndedEvent = null;
            OnAbilityCommitted = null;
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
                // UE5: Overflow — apply overflow effects when stack limit is reached.
                if (spec.Def.OverflowEffects.Count > 0)
                {
                    for (int i = 0; i < spec.Def.OverflowEffects.Count; i++)
                    {
                        var overflowSpec = GameplayEffectSpec.Create(spec.Def.OverflowEffects[i], spec.Source, spec.Level);
                        ApplyGameplayEffectSpecToSelf(overflowSpec);
                    }
                }

                // UE5: bDenyOverflowApplication — skip duration refresh if denied.
                if (!spec.Def.DenyOverflowApplication)
                {
                    if (spec.Def.Stacking.DurationPolicy == EGameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication)
                    {
                        existingEffect.RefreshDurationAndPeriod();
                    }
                }
                GASLog.Debug($"Stacking limit for {spec.Def.Name} reached.");
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
            if (currentPredictionKey.IsValid())
            {
                predictedAttributeSnapshots.Add((attribute, attribute.BaseValue));
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

        /// <summary>
        /// Captures a pure C# snapshot of this ASC's gameplay state.
        /// The resolver lets callers provide stable external IDs for active effects.
        /// </summary>
        public AbilitySystemStateSnapshot CaptureStateSnapshot(Func<ActiveGameplayEffect, int> effectInstanceIdResolver = null, bool clearPendingChanges = false)
        {
            var snapshot = new AbilitySystemStateSnapshot(
                CaptureGrantedAbilitiesSnapshot(),
                CaptureActiveEffectsSnapshot(effectInstanceIdResolver),
                CaptureAttributesSnapshot(),
                CaptureTagSnapshot());

            if (clearPendingChanges)
            {
                ClearPendingStateChanges();
            }

            return snapshot;
        }

        public AbilitySystemStateDeltaSnapshot CapturePendingDeltaSnapshot(Func<ActiveGameplayEffect, int> effectInstanceIdResolver = null)
        {
            var baseVersion = stateVersion;
            var changeMask = AbilitySystemStateChangeMask.None;

            GrantedAbilityStateSnapshot[] grantedAbilities = Array.Empty<GrantedAbilityStateSnapshot>();
            IGASAbilityDefinition[] removedAbilityDefs = Array.Empty<IGASAbilityDefinition>();
            if (grantedAbilitiesDirty)
            {
                changeMask |= AbilitySystemStateChangeMask.GrantedAbilities;
                grantedAbilities = CaptureGrantedAbilitiesSnapshot();
                if (pendingRemovedAbilityDefs.Count > 0)
                {
                    removedAbilityDefs = pendingRemovedAbilityDefs.ToArray();
                }
            }

            ActiveGameplayEffectStateSnapshot[] activeEffectsSnapshot = Array.Empty<ActiveGameplayEffectStateSnapshot>();
            int[] removedEffectNetIds = Array.Empty<int>();
            if (activeEffectsDirty)
            {
                changeMask |= AbilitySystemStateChangeMask.ActiveEffects;
                activeEffectsSnapshot = CaptureActiveEffectsSnapshot(effectInstanceIdResolver);
                if (pendingRemovedEffectNetIds.Count > 0)
                {
                    removedEffectNetIds = pendingRemovedEffectNetIds.ToArray();
                }
            }
            else if (pendingRemovedEffectNetIds.Count > 0)
            {
                // Effects were removed but not applied — still need to tell clients.
                changeMask |= AbilitySystemStateChangeMask.ActiveEffects;
                removedEffectNetIds = pendingRemovedEffectNetIds.ToArray();
            }

            GameplayAttributeStateSnapshot[] attributesSnapshot = Array.Empty<GameplayAttributeStateSnapshot>();
            if (attributeStructureDirty || dirtyAttributeNames.Count > 0)
            {
                changeMask |= AbilitySystemStateChangeMask.Attributes;
                attributesSnapshot = attributeStructureDirty
                    ? CaptureAttributesSnapshot()
                    : CaptureDirtyAttributesSnapshot();
            }

            GameplayTag[] addedTags = Array.Empty<GameplayTag>();
            GameplayTag[] removedTags = Array.Empty<GameplayTag>();
            if (tagsDirty)
            {
                changeMask |= AbilitySystemStateChangeMask.Tags;
                addedTags = CopyTags(pendingAddedTags);
                removedTags = CopyTags(pendingRemovedTags);
            }

            ClearPendingStateChanges();

            return new AbilitySystemStateDeltaSnapshot(
                baseVersion,
                stateVersion,
                changeMask,
                grantedAbilities,
                removedAbilityDefs,
                activeEffectsSnapshot,
                removedEffectNetIds,
                attributesSnapshot,
                addedTags,
                removedTags);
        }

        private GrantedAbilityStateSnapshot[] CaptureGrantedAbilitiesSnapshot()
        {
            var entries = new GrantedAbilityStateSnapshot[activatableAbilities.Count];
            for (int i = 0; i < activatableAbilities.Count; i++)
            {
                var spec = activatableAbilities[i];
                var ability = spec.AbilityCDO ?? spec.Ability;
                entries[i] = new GrantedAbilityStateSnapshot(ability, spec.Level, spec.IsActive);
            }

            return entries;
        }

        private ActiveGameplayEffectStateSnapshot[] CaptureActiveEffectsSnapshot(Func<ActiveGameplayEffect, int> effectInstanceIdResolver)
        {
            var entries = new ActiveGameplayEffectStateSnapshot[activeEffects.Count];
            for (int i = 0; i < activeEffects.Count; i++)
            {
                var effect = activeEffects[i];
                entries[i] = new ActiveGameplayEffectStateSnapshot(
                    effectInstanceIdResolver != null ? effectInstanceIdResolver(effect) : 0,
                    effect.Spec.Def,
                    effect.Spec.Source,
                    effect.Spec.Level,
                    effect.StackCount,
                    effect.Spec.Duration,
                    effect.TimeRemaining,
                    effect.PeriodTimeRemaining,
                    effect.Spec.Context?.PredictionKey ?? default,
                    effect.Spec.CaptureSetByCallerTagMagnitudes());
            }

            return entries;
        }

        private GameplayAttributeStateSnapshot[] CaptureAttributesSnapshot()
        {
            int attributeCount = 0;
            for (int setIndex = 0; setIndex < attributeSets.Count; setIndex++)
            {
                attributeCount += attributeSets[setIndex].GetAttributes().Count;
            }

            if (attributeCount == 0)
            {
                return Array.Empty<GameplayAttributeStateSnapshot>();
            }

            var entries = new GameplayAttributeStateSnapshot[attributeCount];
            int index = 0;
            for (int setIndex = 0; setIndex < attributeSets.Count; setIndex++)
            {
                foreach (var attr in attributeSets[setIndex].GetAttributes())
                {
                    entries[index++] = new GameplayAttributeStateSnapshot(attr.Name, attr.BaseValue, attr.CurrentValue);
                }
            }

            return entries;
        }

        private GameplayAttributeStateSnapshot[] CaptureDirtyAttributesSnapshot()
        {
            if (dirtyAttributeNames.Count == 0)
            {
                return Array.Empty<GameplayAttributeStateSnapshot>();
            }

            var entries = new GameplayAttributeStateSnapshot[dirtyAttributeNames.Count];
            int index = 0;
            foreach (var attributeName in dirtyAttributeNames)
            {
                if (attributes.TryGetValue(attributeName, out var attribute))
                {
                    entries[index++] = new GameplayAttributeStateSnapshot(attribute.Name, attribute.BaseValue, attribute.CurrentValue);
                }
            }

            if (index == 0)
            {
                return Array.Empty<GameplayAttributeStateSnapshot>();
            }

            if (index == entries.Length)
            {
                return entries;
            }

            Array.Resize(ref entries, index);
            return entries;
        }

        private GameplayTag[] CaptureTagSnapshot()
        {
            if (CombinedTags.TagCount <= 0)
            {
                return Array.Empty<GameplayTag>();
            }

            var tags = new GameplayTag[CombinedTags.TagCount];
            int index = 0;
            foreach (var tag in CombinedTags)
            {
                if (!tag.IsNone && tag.IsValid)
                {
                    tags[index++] = tag;
                }
            }

            if (index == 0)
            {
                return Array.Empty<GameplayTag>();
            }

            if (index == tags.Length)
            {
                return tags;
            }

            Array.Resize(ref tags, index);
            return tags;
        }

        private GameplayTag[] CopyTags(HashSet<GameplayTag> tags)
        {
            if (tags.Count == 0)
            {
                return Array.Empty<GameplayTag>();
            }

            var entries = new GameplayTag[tags.Count];
            tags.CopyTo(entries);
            return entries;
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
        public void ApplyAuthorityAttributeSnapshot(GameplayAttributeStateSnapshot[] snapshot)
        {
            if (snapshot == null) return;
            for (int i = 0; i < snapshot.Length; i++)
            {
                ref readonly var entry = ref snapshot[i];
                if (!attributes.TryGetValue(entry.AttributeName, out var attr)) continue;
                attr.OwningSet.SetBaseValue(attr, entry.BaseValue);
                attr.OwningSet.SetCurrentValue(attr, entry.CurrentValue);
            }
        }

        /// <summary>
        /// Rebuilds the entire ASC state from a server-authoritative snapshot.
        /// Clears all active effects and re-applies them from snapshot data.
        /// GameplayCue dispatch and outbound replication are suppressed during the rebuild.
        /// Used for reconnect, late-join, and forced resync (anti-cheat rollback).
        /// </summary>
        public void ApplyStateSnapshot(in AbilitySystemStateSnapshot snapshot)
        {
            using (new NetworkReplicationScope(this))
            {
                // 1. Remove all active effects without triggering cues or outbound replication.
                for (int i = activeEffects.Count - 1; i >= 0; i--)
                {
                    activeEffects[i].ReturnToPool();
                }
                activeEffects.Clear();
                stackingIndexByTarget.Clear();
                stackingIndexBySource.Clear();
                grantedTagIndexToEffects.Clear();
                fromEffectsTags.Clear();
                dirtyAttributes.Clear();

                // 2. Reset tags to snapshot state (loose tags only; effect-granted tags come back with effects).
                looseTags.Clear();
                CombinedTags.Clear();
                if (snapshot.Tags != null)
                {
                    for (int i = 0; i < snapshot.Tags.Length; i++)
                    {
                        var tag = snapshot.Tags[i];
                        if (tag.IsValid && !tag.IsNone)
                        {
                            looseTags.AddTag(tag);
                            CombinedTags.AddTag(tag);
                        }
                    }
                }

                // 3. Force-set attribute values from snapshot.
                if (snapshot.Attributes != null)
                {
                    ApplyAuthorityAttributeSnapshot(snapshot.Attributes);
                }

                // 4. Re-apply all effects from snapshot data.
                if (snapshot.ActiveEffects != null)
                {
                    var resolver = GASServices.ReplicationResolver;
                    for (int i = 0; i < snapshot.ActiveEffects.Length; i++)
                    {
                        ref readonly var snap = ref snapshot.ActiveEffects[i];
                        var effectDef = snap.EffectDefinition as GameplayEffect;
                        if (effectDef == null) continue;
                        var source = snap.SourceComponent as AbilitySystemComponent;
                        BuildReplicationDataFromSnapshot(in snap, effectDef, source, resolver, out var data);
                        CreateReplicatedActiveEffect(effectDef, source, data);
                    }
                }

                // 5. Recalculate all attribute current values restored by effects.
                if (dirtyAttributes.Count > 0)
                {
                    RecalculateDirtyAttributes();
                }

                // 6. Rebuild granted abilities from snapshot.
                // First remove any ability that is not present in the snapshot.
                if (snapshot.GrantedAbilities != null)
                {
                    // Build a quick lookup set of the incoming ability definitions.
                    var incomingDefs = new HashSet<IGASAbilityDefinition>(snapshot.GrantedAbilities.Length);
                    for (int i = 0; i < snapshot.GrantedAbilities.Length; i++)
                    {
                        if (snapshot.GrantedAbilities[i].AbilityDefinition != null)
                            incomingDefs.Add(snapshot.GrantedAbilities[i].AbilityDefinition);
                    }

                    // Remove abilities not present in the snapshot.
                    for (int i = activatableAbilities.Count - 1; i >= 0; i--)
                    {
                        var spec = activatableAbilities[i];
                        var def = spec.AbilityCDO ?? spec.Ability;
                        if (def != null && !incomingDefs.Contains(def))
                            ClearAbility(spec);
                    }

                    // Grant abilities that are missing.
                    for (int i = 0; i < snapshot.GrantedAbilities.Length; i++)
                    {
                        ref readonly var abilitySnap = ref snapshot.GrantedAbilities[i];
                        var ability = abilitySnap.AbilityDefinition as GameplayAbility;
                        if (ability == null) continue;

                        bool alreadyGranted = false;
                        for (int j = 0; j < activatableAbilities.Count; j++)
                        {
                            var existing = activatableAbilities[j].AbilityCDO ?? activatableAbilities[j].Ability;
                            if (existing == ability) { alreadyGranted = true; break; }
                        }
                        if (!alreadyGranted)
                            GrantAbility(ability, abilitySnap.Level);
                    }
                }
            }
        }

        /// <summary>
        /// Applies only the sections flagged in <see cref="AbilitySystemStateDeltaSnapshot.ChangeMask"/>.
        /// <br/>
        /// <b>ActiveEffects semantics:</b> each entry is an upsert (update if NetworkId known, create otherwise).
        /// Effects listed in <see cref="AbilitySystemStateDeltaSnapshot.RemovedEffectNetIds"/> are explicitly removed.
        /// <br/>
        /// <b>GrantedAbilities semantics:</b> when the GrantedAbilities flag is set the incoming array
        /// is treated as the complete authoritative list — missing abilities are removed, new ones are granted.
        /// <see cref="AbilitySystemStateDeltaSnapshot.RemovedAbilityDefinitions"/> handles explicit partial removes
        /// without a full list resend.
        /// </summary>
        public void ApplyDeltaSnapshot(in AbilitySystemStateDeltaSnapshot delta)
        {
            if (!delta.HasChanges) return;

            using (new NetworkReplicationScope(this))
            {
                // 1. Attributes
                if ((delta.ChangeMask & AbilitySystemStateChangeMask.Attributes) != 0 &&
                    delta.Attributes != null && delta.Attributes.Length > 0)
                {
                    ApplyAuthorityAttributeSnapshot(delta.Attributes);
                }

                // 2. Effects — explicit removes first, then upserts
                if ((delta.ChangeMask & AbilitySystemStateChangeMask.ActiveEffects) != 0)
                {
                    // 2a. Remove effects the server has already removed.
                    if (delta.RemovedEffectNetIds != null)
                    {
                        for (int i = 0; i < delta.RemovedEffectNetIds.Length; i++)
                        {
                            int netId = delta.RemovedEffectNetIds[i];
                            if (netId == 0) continue;
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
                    }

                    // 2b. Upsert incoming effects.
                    if (delta.ActiveEffects != null && delta.ActiveEffects.Length > 0)
                    {
                        var resolver = GASServices.ReplicationResolver;
                        for (int i = 0; i < delta.ActiveEffects.Length; i++)
                        {
                            ref readonly var snap = ref delta.ActiveEffects[i];
                            var effectDef = snap.EffectDefinition as GameplayEffect;
                            if (effectDef == null) continue;
                            var source = snap.SourceComponent as AbilitySystemComponent;
                            BuildReplicationDataFromSnapshot(in snap, effectDef, source, resolver, out var data);
                            ApplyAuthoritativeEffectReplication(in data, allowCreate: true);
                        }
                    }
                }

                // 3. Tags
                if ((delta.ChangeMask & AbilitySystemStateChangeMask.Tags) != 0)
                {
                    if (delta.AddedTags != null)
                    {
                        for (int i = 0; i < delta.AddedTags.Length; i++)
                        {
                            var tag = delta.AddedTags[i];
                            if (tag.IsValid && !tag.IsNone) { looseTags.AddTag(tag); CombinedTags.AddTag(tag); }
                        }
                    }
                    if (delta.RemovedTags != null)
                    {
                        for (int i = 0; i < delta.RemovedTags.Length; i++)
                        {
                            var tag = delta.RemovedTags[i];
                            if (tag.IsValid && !tag.IsNone) { looseTags.RemoveTag(tag); CombinedTags.RemoveTag(tag); }
                        }
                    }
                }

                // 4. GrantedAbilities
                if ((delta.ChangeMask & AbilitySystemStateChangeMask.GrantedAbilities) != 0)
                {
                    // 4a. Explicit partial removes (populated when only specific abilities were removed
                    //     and the full list is not being resent).
                    if (delta.RemovedAbilityDefinitions != null)
                    {
                        for (int i = 0; i < delta.RemovedAbilityDefinitions.Length; i++)
                        {
                            var def = delta.RemovedAbilityDefinitions[i];
                            if (def == null) continue;
                            for (int j = activatableAbilities.Count - 1; j >= 0; j--)
                            {
                                var existing = activatableAbilities[j].AbilityCDO ?? activatableAbilities[j].Ability;
                                if (existing == def) { ClearAbility(activatableAbilities[j]); break; }
                            }
                        }
                    }

                    // 4b. Authoritative list — treat as full replacement when provided.
                    if (delta.GrantedAbilities != null && delta.GrantedAbilities.Length > 0)
                    {
                        var incomingDefs = new HashSet<IGASAbilityDefinition>(delta.GrantedAbilities.Length);
                        for (int i = 0; i < delta.GrantedAbilities.Length; i++)
                        {
                            if (delta.GrantedAbilities[i].AbilityDefinition != null)
                                incomingDefs.Add(delta.GrantedAbilities[i].AbilityDefinition);
                        }

                        // Remove abilities not in the authoritative list.
                        for (int i = activatableAbilities.Count - 1; i >= 0; i--)
                        {
                            var spec = activatableAbilities[i];
                            var def = spec.AbilityCDO ?? spec.Ability;
                            if (def != null && !incomingDefs.Contains(def))
                                ClearAbility(spec);
                        }

                        // Grant missing abilities.
                        for (int i = 0; i < delta.GrantedAbilities.Length; i++)
                        {
                            ref readonly var abilitySnap = ref delta.GrantedAbilities[i];
                            var ability = abilitySnap.AbilityDefinition as GameplayAbility;
                            if (ability == null) continue;
                            bool alreadyGranted = false;
                            for (int j = 0; j < activatableAbilities.Count; j++)
                            {
                                var existing = activatableAbilities[j].AbilityCDO ?? activatableAbilities[j].Ability;
                                if (existing == ability) { alreadyGranted = true; break; }
                            }
                            if (!alreadyGranted)
                                GrantAbility(ability, abilitySnap.Level);
                        }
                    }
                }

                if (dirtyAttributes.Count > 0)
                {
                    RecalculateDirtyAttributes();
                }
            }
        }

        /// <summary>
        /// Builds a <see cref="GASEffectReplicationData"/> from a passive effect snapshot.
        /// Used by <see cref="ApplyStateSnapshot"/> and <see cref="ApplyDeltaSnapshot"/>.
        /// </summary>
        private static void BuildReplicationDataFromSnapshot(
            in ActiveGameplayEffectStateSnapshot snap,
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

            if (snap.SetByCallerTagMagnitudes != null && snap.SetByCallerTagMagnitudes.Length > 0)
            {
                int count = snap.SetByCallerTagMagnitudes.Length;
                var tags = new GameplayTag[count];
                var values = new float[count];
                for (int j = 0; j < count; j++)
                {
                    tags[j] = snap.SetByCallerTagMagnitudes[j].Tag;
                    values[j] = snap.SetByCallerTagMagnitudes[j].Value;
                }
                data.SetByCallerTags = tags;
                data.SetByCallerValues = values;
                data.SetByCallerCount = count;
            }
        }
    }
}