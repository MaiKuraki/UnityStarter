using System;
using System.Collections.Generic;
using CycloneGames.Factory.Runtime;
using CycloneGames.GameplayTags.Runtime;
using CycloneGames.Logger;

namespace CycloneGames.GameplayAbilities.Runtime
{
    public enum EReplicationMode
    {
        Full,
        Mixed,
        Minimal
    }

    // Represents the unique key for a prediction event.
    public struct PredictionKey : IEquatable<PredictionKey>
    {
        public int Key { get; private set; }
        private static int nextKey = 1;

        public bool IsValid() => Key != 0;

        public static PredictionKey NewKey()
        {
            return new PredictionKey { Key = nextKey++ };
        }

        public bool Equals(PredictionKey other)
        {
            return Key == other.Key;
        }

        public override bool Equals(object obj)
        {
            return obj is PredictionKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Key;
        }
    }

    public class AbilitySystemComponent : IDisposable
    {
        public object OwnerActor { get; private set; }
        public object AvatarActor { get; private set; }
        public EReplicationMode ReplicationMode { get; set; } = EReplicationMode.Full;

        public GameplayTagCountContainer CombinedTags { get; } = new GameplayTagCountContainer();
        private readonly GameplayTagCountContainer looseTags = new GameplayTagCountContainer();
        private readonly GameplayTagCountContainer fromEffectsTags = new GameplayTagCountContainer();

        private readonly List<AttributeSet> attributeSets = new List<AttributeSet>(4);
        private readonly Dictionary<string, GameplayAttribute> attributes = new Dictionary<string, GameplayAttribute>(32);
        private readonly List<ActiveGameplayEffect> activeEffects = new List<ActiveGameplayEffect>(32);
        public IReadOnlyList<ActiveGameplayEffect> ActiveEffects => activeEffects.AsReadOnly();

        private readonly List<GameplayAbilitySpec> activatableAbilities = new List<GameplayAbilitySpec>(16);
        public IReadOnlyList<GameplayAbilitySpec> GetActivatableAbilities() => activatableAbilities.AsReadOnly();

        private readonly Dictionary<ActiveGameplayEffect, List<GameplayAbilitySpec>> effectGrantedAbilities = new Dictionary<ActiveGameplayEffect, List<GameplayAbilitySpec>>(16);
        private readonly HashSet<GameplayAttribute> dirtyAttributes = new HashSet<GameplayAttribute>(32);
        
        // Caches which active effects affect which attributes, to optimize RecalculateDirtyAttributes.
        private readonly Dictionary<GameplayAttribute, List<ActiveGameplayEffect>> attributeToActiveEffectsMap = new Dictionary<GameplayAttribute, List<ActiveGameplayEffect>>();

        private static readonly List<ActiveGameplayEffect> expiredEffectsScratchPad = new List<ActiveGameplayEffect>(16);
        private static List<ModifierInfo> executionOutputScratchPad = new List<ModifierInfo>(16);

        // --- Prediction ---
        private PredictionKey currentPredictionKey;
        private readonly List<ActiveGameplayEffect> pendingPredictedEffects = new List<ActiveGameplayEffect>();

        public IFactory<IGameplayEffectContext> EffectContextFactory { get; private set; }

        public AbilitySystemComponent(IFactory<IGameplayEffectContext> effectContextFactory)
        {
            this.EffectContextFactory = effectContextFactory;
        }

        public void InitAbilityActorInfo(object owner, object avatar)
        {
            OwnerActor = owner;
            AvatarActor = avatar;
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
                        CLogger.LogWarning($"Attribute '{attr.Name}' is already present. Duplicate attributes are not allowed.");
                    }
                }
            }
        }

        public void MarkAttributeDirty(GameplayAttribute attribute)
        {
            if (attribute != null) dirtyAttributes.Add(attribute);
        }

        public GameplayAttribute GetAttribute(string name)
        {
            attributes.TryGetValue(name, out var attribute);
            return attribute;
        }

        public GameplayAbilitySpec GrantAbility(GameplayAbility ability, int level = 1)
        {
            if (ability == null) return null;

            var spec = new GameplayAbilitySpec(ability, level);
            spec.Init(this);

            activatableAbilities.Add(spec);
            spec.GetPrimaryInstance().OnGiveAbility(new GameplayAbilityActorInfo(OwnerActor, AvatarActor), spec);
            return spec;
        }

        public void ClearAbility(GameplayAbilitySpec spec)
        {
            if (spec == null) return;
            spec.OnRemoveSpec();
            activatableAbilities.Remove(spec);
        }

        // --- Ability Activation Flow ---
        public bool TryActivateAbility(GameplayAbilitySpec spec)
        {
            if (spec == null || spec.IsActive) return false;

            var ability = spec.GetPrimaryInstance();
            if (ability == null) return false;

            var actorInfo = new GameplayAbilityActorInfo(OwnerActor, AvatarActor);

            if (!ability.CanActivate(actorInfo, spec))
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
            var actorInfo = new GameplayAbilityActorInfo(OwnerActor, AvatarActor);
            if (spec.GetPrimaryInstance().CanActivate(actorInfo, spec))
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
            ability.ActivateAbility(new GameplayAbilityActorInfo(OwnerActor, AvatarActor), spec, activationInfo);

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
                    pendingPredictedEffects.RemoveAt(i);
                }
            }
        }

        private void ClientActivateAbilityFailed(GameplayAbilitySpec spec, PredictionKey predictionKey)
        {
            if (!predictionKey.IsValid()) return;

            CLogger.LogWarning($"Client prediction failed for ability '{spec.Ability.Name}' with key {predictionKey.Key}. Rolling back.");

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

        internal void OnAbilityEnded(GameplayAbility ability)
        {
            if (ability.Spec != null)
            {
                ability.Spec.IsActive = false;
                // This ensures that flags like 'isEnding' are ready for the next activation.
                ability.InternalOnEndAbility();

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

            if (currentPredictionKey.IsValid())
            {
                pendingPredictedEffects.Add(newActiveEffect);
            }

            CLogger.LogInfo($"{OwnerActor} Apply GameplayEffect '{spec.Def.Name}' to self.");
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
                // The predicate captures the effects to be removed into a temporary list for post-processing.
                activeEffects.RemoveAll(effect =>
                {
                    bool shouldRemove = effect.Spec.Def.GrantedTags.HasAny(tags) || effect.Spec.Def.AssetTags.HasAny(tags);
                    if (shouldRemove)
                    {
                        removedEffects.Add(effect);
                    }
                    return shouldRemove;
                });

                foreach (var effect in removedEffects)
                {
                    OnEffectRemoved(effect, true);
                }
            }
        }

        private bool CanApplyEffect(GameplayEffectSpec spec)
        {
            if (spec.Def.ApplicationTagRequirements.RequiredTags != null && !spec.Def.ApplicationTagRequirements.RequiredTags.IsEmpty)
            {
                if (!CombinedTags.HasAll(spec.Def.ApplicationTagRequirements.RequiredTags))
                {
                    CLogger.LogInfo($"Apply GameplayEffect '{spec.Def.Name}' failed: does not meet application tag requirements (Required).");
                    return false;
                }
            }
            if (spec.Def.ApplicationTagRequirements.ForbiddenTags != null && !spec.Def.ApplicationTagRequirements.ForbiddenTags.IsEmpty)
            {
                if (CombinedTags.HasAny(spec.Def.ApplicationTagRequirements.ForbiddenTags))
                {
                    CLogger.LogInfo($"Apply GameplayEffect '{spec.Def.Name}' failed: does not meet application tag requirements (Ignored).");
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

            foreach (var mod in spec.Def.Modifiers)
            {
                var attribute = GetAttribute(mod.AttributeName);
                if (attribute != null)
                {
                    //  Modify the base value, so isFromExecution is true
                    ApplyModifier(spec, attribute, mod, spec.GetCalculatedMagnitude(mod), true);
                }
            }

            // CLogger.LogInfo($"{OwnerActor} Execute Instant GameplayEffect '{spec.Def.Name}' on self.");
        }

        // --- Tick and State Management ---
        public void Tick(float deltaTime, bool isServer)
        {
            // Tick tasks for active abilities
            foreach (var spec in activatableAbilities)
            {
                if (spec.IsActive) spec.GetPrimaryInstance()?.TickTasks(deltaTime);
            }

            // Server is authoritative over effect duration
            if (isServer)
            {
                expiredEffectsScratchPad.Clear();
                for (int i = activeEffects.Count - 1; i >= 0; i--)
                {
                    if (activeEffects[i].Tick(deltaTime, this))
                    {
                        expiredEffectsScratchPad.Add(activeEffects[i]);
                    }
                }

                if (expiredEffectsScratchPad.Count > 0)
                {
                    foreach (var expiredEffect in expiredEffectsScratchPad)
                    {
                        activeEffects.Remove(expiredEffect);
                        OnEffectRemoved(expiredEffect, true);
                    }
                    expiredEffectsScratchPad.Clear();
                }
            }

            if (dirtyAttributes.Count > 0)
            {
                RecalculateDirtyAttributes();
            }
        }

        private void RecalculateDirtyAttributes()
        {
            foreach (var attr in dirtyAttributes)
            {
                float baseValue = attr.OwningSet.GetBaseValue(attr);
                float additive = 0;
                float multiplicitive = 1.0f;
                float division = 1.0f;
                float overrideValue = 0;
                bool hasOverride = false;

                if (attributeToActiveEffectsMap.TryGetValue(attr, out var affectingEffects))
                {
                    foreach (var effect in affectingEffects)
                    {
                        if (effect.Spec.Def.Period > 0 || (!effect.Spec.Def.OngoingTagRequirements.IsEmpty && !effect.Spec.Def.OngoingTagRequirements.MeetsRequirements(CombinedTags)))
                        {
                            continue;
                        }

                        foreach (var mod in effect.Spec.Def.Modifiers)
                        {
                            if (mod.AttributeName == attr.Name)
                            {
                                float magnitude = effect.Spec.GetCalculatedMagnitude(mod) * effect.StackCount;
                                switch (mod.Operation)
                                {
                                    case EAttributeModifierOperation.Add:
                                        additive += magnitude;
                                        break;
                                    case EAttributeModifierOperation.Multiply:
                                        multiplicitive *= magnitude;
                                        break;
                                    case EAttributeModifierOperation.Division:
                                        if (magnitude != 0) division *= magnitude;
                                        break;
                                    case EAttributeModifierOperation.Override:
                                        overrideValue = magnitude;
                                        hasOverride = true;
                                        break;
                                }
                            }
                        }
                    }
                }

                float finalValue = hasOverride ? overrideValue : ((baseValue + additive) * multiplicitive / (division == 0 ? 1.0f : division));

                attr.OwningSet.PreAttributeChange(attr, ref finalValue);
                attr.OwningSet.SetCurrentValue(attr, finalValue);
            }

            dirtyAttributes.Clear();
        }

        private void OnEffectApplied(ActiveGameplayEffect effect)
        {
            fromEffectsTags.AddTags(effect.Spec.Def.GrantedTags);
            UpdateCombinedTags();
            
            // Update the attribute-to-effect map
            foreach (var modifier in effect.Spec.Def.Modifiers)
            {
                var attribute = GetAttribute(modifier.AttributeName);
                if (attribute != null)
                {
                    if (!attributeToActiveEffectsMap.TryGetValue(attribute, out var effectList))
                    {
                        effectList = new List<ActiveGameplayEffect>();
                        attributeToActiveEffectsMap[attribute] = effectList;
                    }
                    effectList.Add(effect);
                }
            }

            if (effect.Spec.Def.GrantedAbilities.Count > 0)
            {
                var grantedSpecs = new List<GameplayAbilitySpec>(effect.Spec.Def.GrantedAbilities.Count);
                foreach (var ability in effect.Spec.Def.GrantedAbilities)
                {
                    var newSpec = GrantAbility(ability, effect.Spec.Level);
                    grantedSpecs.Add(newSpec);
                }
                effectGrantedAbilities[effect] = grantedSpecs;
            }

            if (!effect.Spec.Def.GameplayCues.IsEmpty)
            {
                var eventType = (effect.Spec.Def.DurationPolicy == EDurationPolicy.Instant) ? EGameplayCueEvent.Executed : EGameplayCueEvent.OnActive;

                // Iterate through the tags in the effect's cue container.
                foreach (var cueTag in effect.Spec.Def.GameplayCues)
                {
                    if (cueTag == GameplayTag.None) continue;

                    GameplayCueManager.Instance.HandleCue(cueTag, eventType, effect.Spec).Forget();

                    if (eventType == EGameplayCueEvent.OnActive)
                    {
                        GameplayCueManager.Instance.HandleCue(cueTag, EGameplayCueEvent.WhileActive, effect.Spec).Forget();
                    }
                }
            }
        }

        private void OnEffectRemoved(ActiveGameplayEffect effect, bool markDirty)
        {
            fromEffectsTags.RemoveTags(effect.Spec.Def.GrantedTags);
            UpdateCombinedTags();
            
            // Update the attribute-to-effect map
            foreach (var modifier in effect.Spec.Def.Modifiers)
            {
                var attribute = GetAttribute(modifier.AttributeName);
                if (attribute != null && attributeToActiveEffectsMap.TryGetValue(attribute, out var effectList))
                {
                    effectList.Remove(effect);
                }
            }

            if (effectGrantedAbilities.TryGetValue(effect, out var specsToRemove))
            {
                foreach (var spec in specsToRemove) ClearAbility(spec);
                effectGrantedAbilities.Remove(effect);
            }

            if (effect.Spec.Def.DurationPolicy != EDurationPolicy.Instant && !effect.Spec.Def.GameplayCues.IsEmpty)
            {
                foreach (var cueTag in effect.Spec.Def.GameplayCues)
                {
                    if (cueTag == GameplayTag.None) continue;
                    GameplayCueManager.Instance.HandleCue(cueTag, EGameplayCueEvent.Removed, effect.Spec).Forget();
                }
            }

            if (markDirty) MarkAttributesDirtyFromEffect(effect);

            effect.ReturnToPool();
        }

        // --- Tag Management ---
        public void AddLooseGameplayTag(GameplayTag tag) { looseTags.AddTag(tag); UpdateCombinedTags(); }
        public void RemoveLooseGameplayTag(GameplayTag tag) { looseTags.RemoveTag(tag); UpdateCombinedTags(); }

        private void UpdateCombinedTags()
        {
            CombinedTags.Clear();
            CombinedTags.AddTags(looseTags);
            CombinedTags.AddTags(fromEffectsTags);
        }

        public void Dispose()
        {
            foreach (var effect in activeEffects) effect.ReturnToPool();
            foreach (var abilitySpec in activatableAbilities) abilitySpec.GetPrimaryInstance()?.OnRemoveAbility();

            activeEffects.Clear();
            attributeSets.Clear();
            attributes.Clear();
            activatableAbilities.Clear();
            effectGrantedAbilities.Clear();
            looseTags.Clear();
            fromEffectsTags.Clear();
            CombinedTags.Clear();
            dirtyAttributes.Clear();
            OwnerActor = null;
            AvatarActor = null;
        }

        // Dummy implementations for brevity in this example
        private bool HandleStacking(GameplayEffectSpec spec)
        {
            // If the effect doesn't stack, do nothing and let the calling function create a new effect instance.
            if (spec.Def.Stacking.Type == EGameplayEffectStackingType.None)
            {
                return false;
            }

            // Search for an existing active effect that matches the stacking criteria.
            for (int i = 0; i < activeEffects.Count; i++)
            {
                var existingEffect = activeEffects[i];
                if (existingEffect.IsExpired) continue;

                // Stacking requires the GameplayEffect definitions to be the same.
                if (existingEffect.Spec.Def == spec.Def)
                {
                    // For AggregateBySource, the source AbilitySystemComponent must also match.
                    if (spec.Def.Stacking.Type == EGameplayEffectStackingType.AggregateBySource &&
                        existingEffect.Spec.Source != spec.Source)
                    {
                        continue; // Not a match, keep searching.
                    }

                    // Found a stackable effect.

                    // Check if the stack limit has been reached.
                    if (existingEffect.StackCount >= spec.Def.Stacking.Limit)
                    {
                        // At stack limit. Respect duration refresh policy when re-applied.
                        if (spec.Def.Stacking.DurationPolicy == EGameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication)
                        {
                            existingEffect.RefreshDurationAndPeriod();
                        }
                        CLogger.LogInfo($"Stacking limit for {spec.Def.Name} reached.");
                    }
                    else
                    {
                        // Increment the stack count on the existing effect.
                        existingEffect.OnStackApplied();
                    }

                    // Mark attributes as dirty to force recalculation.
                    MarkAttributesDirtyFromEffect(existingEffect);

                    // The new spec has been handled by stacking, so it can be returned to the pool.
                    // Returning true prevents a new ActiveGameplayEffect from being created.
                    return true;
                }
            }

            // No existing effect found to stack with.
            return false;
        }
        private void RemoveEffectsWithTags(GameplayTagContainer tags)
        {
            // This is a private helper that can be called when a new effect is applied.
            RemoveActiveEffectsWithGrantedTags(tags);
        }
        private void ApplyModifier(GameplayEffectSpec spec, GameplayAttribute attribute, ModifierInfo mod, float magnitude, bool isFromExecution)
        {
            var targetAttribute = GetAttribute(mod.AttributeName);
            if (targetAttribute == null)
            {
                CLogger.LogWarning($"ApplyModifier failed: Attribute '{mod.AttributeName}' not found on ASC.");
                return;
            }

            var targetAttributeSet = targetAttribute.OwningSet;
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

            // Iterate through all modifiers defined in the effect.
            foreach (var modifier in activeEffect.Spec.Def.Modifiers)
            {
                // We cannot just use modifier.Attribute directly anymore as it doesn't exist.
                // We use the attribute's name string to find the actual attribute instance.
                if (!string.IsNullOrEmpty(modifier.AttributeName))
                {
                    var attribute = GetAttribute(modifier.AttributeName);
                    if (attribute != null)
                    {
                        // Mark the resolved attribute instance as dirty.
                        MarkAttributeDirty(attribute);
                    }
                }
            }
        }
    }
}
