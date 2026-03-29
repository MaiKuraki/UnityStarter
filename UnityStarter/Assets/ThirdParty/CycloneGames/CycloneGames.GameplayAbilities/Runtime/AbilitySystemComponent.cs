using System;
using System.Collections.Generic;
using CycloneGames.Factory.Runtime;
using CycloneGames.GameplayTags.Runtime;
using CycloneGames.GameplayAbilities.Core;

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

    public partial class AbilitySystemComponent : IDisposable
    {
        public object OwnerActor { get; private set; }
        public object AvatarActor { get; private set; }
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

        // P0: O(1) stacking index — avoids linear search per ApplyGameplayEffectSpecToSelf
        // Key: GameplayEffect def → first matching ActiveGameplayEffect (for AggregateByTarget)
        private readonly Dictionary<GameplayEffect, ActiveGameplayEffect> stackingIndexByTarget = new Dictionary<GameplayEffect, ActiveGameplayEffect>(16);
        // Key: (GameplayEffect def, source ASC) → ActiveGameplayEffect (for AggregateBySource)
        private readonly Dictionary<(GameplayEffect, AbilitySystemComponent), ActiveGameplayEffect> stackingIndexBySource = new Dictionary<(GameplayEffect, AbilitySystemComponent), ActiveGameplayEffect>(16);

        private readonly List<GameplayAbilitySpec> activatableAbilities = new List<GameplayAbilitySpec>(16);
        public IReadOnlyList<GameplayAbilitySpec> GetActivatableAbilities() => activatableAbilities;

        private readonly List<GameplayAbilitySpec> tickingAbilities = new List<GameplayAbilitySpec>(16);

        private readonly List<GameplayAttribute> dirtyAttributes = new List<GameplayAttribute>(32);

        // P2: Tracks effects applied by abilities for RemoveGameplayEffectsAfterAbilityEnds
        private readonly Dictionary<GameplayAbility, List<ActiveGameplayEffect>> abilityAppliedEffects = new Dictionary<GameplayAbility, List<ActiveGameplayEffect>>(8);

        [ThreadStatic]
        private static List<ModifierInfo> executionOutputScratchPad;

        // --- Prediction ---
        private PredictionKey currentPredictionKey;
        private readonly List<ActiveGameplayEffect> pendingPredictedEffects = new List<ActiveGameplayEffect>();

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
        }

        public void InitAbilityActorInfo(object owner, object avatar)
        {
            OwnerActor = owner;
            AvatarActor = avatar;
            cachedActorInfo = new GameplayAbilityActorInfo(owner, avatar);
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
        }

        public void MarkAttributeDirty(GameplayAttribute attribute)
        {
            if (attribute != null && !attribute.IsDirty)
            {
                attribute.IsDirty = true;
                dirtyAttributes.Add(attribute);
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
            // P0: Swap-with-last O(1) removal instead of O(n) List.Remove
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
                    // In a real network scenario, this would be an RPC
                    ServerTryActivateAbility(spec, activationInfo);
                    break;
                case ENetExecutionPolicy.ServerOnly:
                    // In a real network scenario, client would send an RPC to request activation.
                    // Here we simulate the server directly trying to activate.
                    ServerTryActivateAbility(spec, new GameplayAbilityActivationInfo());
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
                ClientActivateAbilitySucceed(spec, activationInfo.PredictionKey);
            }
            else
            {
                // Server rejects activation
                ClientActivateAbilityFailed(spec, activationInfo.PredictionKey);
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
            // The server has confirmed our prediction. 
            // Remove matching items without allocating a predicate.
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

        private void ClientActivateAbilityFailed(GameplayAbilitySpec spec, PredictionKey predictionKey)
        {
            if (!predictionKey.IsValid()) return;

            GASLog.Warning($"Client prediction failed for ability '{spec.Ability.Name}' with key {predictionKey.Key}. Rolling back.");

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
                    activeEffects.Remove(effect);
                    pendingPredictedEffects.Remove(effect);
                    OnEffectRemoved(effect, false); // Don't re-dirty attributes on rollback
                }
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
                    // P0: Swap-with-last for tickingAbilities removal
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
                        for (int ei = appliedEffects.Count - 1; ei >= 0; ei--)
                        {
                            var trackedEffect = appliedEffects[ei];
                            int aeIdx = activeEffects.IndexOf(trackedEffect);
                            if (aeIdx >= 0 && !trackedEffect.IsExpired)
                            {
                                RemoveActiveEffectAtIndex(aeIdx);
                                OnEffectRemoved(trackedEffect, true);
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

            // P0: Maintain stacking index for O(1) lookup
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

            // P2: Track effects for RemoveGameplayEffectsAfterAbilityEnds
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
                if (spec.Def.AssetTags.HasAny(ImmunityTags) || spec.Def.GrantedTags.HasAny(ImmunityTags))
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

            if (spec.Def.ApplicationTagRequirements.RequiredTags != null && !spec.Def.ApplicationTagRequirements.RequiredTags.IsEmpty)
            {
                if (!CombinedTags.HasAll(spec.Def.ApplicationTagRequirements.RequiredTags))
                {
                    GASLog.Debug($"Apply GameplayEffect '{spec.Def.Name}' failed: does not meet application tag requirements (Required).");
                    return false;
                }
            }
            if (spec.Def.ApplicationTagRequirements.ForbiddenTags != null && !spec.Def.ApplicationTagRequirements.ForbiddenTags.IsEmpty)
            {
                if (CombinedTags.HasAny(spec.Def.ApplicationTagRequirements.ForbiddenTags))
                {
                    GASLog.Debug($"Apply GameplayEffect '{spec.Def.Name}' failed: does not meet application tag requirements (Ignored).");
                    return false;
                }
            }

            // P1: Custom Application Requirements (UE5: UGameplayEffectCustomApplicationRequirement)
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

            // Server is authoritative over effect duration
            if (isServer)
            {
                for (int i = activeEffects.Count - 1; i >= 0; i--)
                {
                    var effect = activeEffects[i];
                    if (effect.Tick(deltaTime, this))
                    {
                        // P2: Stack Expiration Policy
                        HandleEffectExpiration(effect, i);
                    }
                }
            }

            if (dirtyAttributes.Count > 0)
            {
                RecalculateDirtyAttributes();
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

                    // P0: Cache OngoingTagRequirements check per-effect, not per-modifier
                    bool isInhibited = !def.OngoingTagRequirements.IsEmpty && !def.OngoingTagRequirements.MeetsRequirements(CombinedTags);

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
            // P0: Incremental tag update — add to both containers directly, no full rebuild
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

            // P2: SuppressGameplayCues — skip all cue handling when suppressed
            if (!effect.Spec.Def.SuppressGameplayCues && !effect.Spec.Def.GameplayCues.IsEmpty)
            {
                var eventType = (effect.Spec.Def.DurationPolicy == EDurationPolicy.Instant) ? EGameplayCueEvent.Executed : EGameplayCueEvent.OnActive;

                foreach (var cueTag in effect.Spec.Def.GameplayCues)
                {
                    if (cueTag.IsNone) continue;

                    GameplayCueManager.Default.HandleCue(cueTag, eventType, effect.Spec).Forget();

                    if (eventType == EGameplayCueEvent.OnActive)
                    {
                        GameplayCueManager.Default.HandleCue(cueTag, EGameplayCueEvent.WhileActive, effect.Spec).Forget();
                    }
                }
            }

            OnGameplayEffectAppliedToSelf?.Invoke(effect);
        }

        private void OnEffectRemoved(ActiveGameplayEffect effect, bool markDirty)
        {
            // P0: Incremental tag update — remove from both containers directly, no full rebuild
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

            // P2: SuppressGameplayCues — skip cue handling when suppressed
            if (!effect.Spec.Def.SuppressGameplayCues &&
                effect.Spec.Def.DurationPolicy != EDurationPolicy.Instant && !effect.Spec.Def.GameplayCues.IsEmpty)
            {
                foreach (var cueTag in effect.Spec.Def.GameplayCues)
                {
                    if (cueTag.IsNone) continue;
                    GameplayCueManager.Default.HandleCue(cueTag, EGameplayCueEvent.Removed, effect.Spec).Forget();
                }
            }

            OnGameplayEffectRemovedFromSelf?.Invoke(effect);

            if (markDirty) MarkAttributesDirtyFromEffect(effect);

            effect.ReturnToPool();
        }

        // --- Tag Management ---
        // P0: Incremental tag updates — directly add/remove from CombinedTags, no full rebuild
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

        /// <summary>
        /// Checks if the owner has ANY of the specified gameplay tags.
        /// UE5: HasAnyMatchingGameplayTags.
        /// </summary>
        public bool HasAnyMatchingGameplayTags(GameplayTagContainer tags) => tags != null && !tags.IsEmpty && CombinedTags.HasAny(tags);

        /// <summary>
        /// Gets the count of how many times a specific tag is applied (from loose + effects).
        /// UE5: GetTagCount.
        /// </summary>
        public int GetTagCount(GameplayTag tag) => CombinedTags.GetTagCount(tag);

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
            abilityAppliedEffects.Clear();
            triggerEventAbilities.Clear();
            triggerTagAddedAbilities.Clear();
            triggerTagRemovedAbilities.Clear();
            OnGameplayEffectAppliedToSelf = null;
            OnGameplayEffectRemovedFromSelf = null;
            OnAbilityActivated = null;
            OnAbilityEndedEvent = null;
            OnAbilityCommitted = null;
            OwnerActor = null;
            AvatarActor = null;
        }

        // P0: O(1) stacking lookup using dictionary index instead of O(n) linear search
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

            MarkAttributesDirtyFromEffect(existingEffect);
            return true;
        }
        private void RemoveEffectsWithTags(GameplayTagContainer tags)
        {
            // This is a private helper that can be called when a new effect is applied.
            RemoveActiveEffectsWithGrantedTags(tags);
        }
        private void ApplyModifier(GameplayEffectSpec spec, GameplayAttribute attribute, ModifierInfo mod, float magnitude, bool isFromExecution)
        {
            if (attribute == null)
            {
                GASLog.Warning($"ApplyModifier failed: Attribute '{mod.AttributeName}' not found on ASC.");
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
    }
}