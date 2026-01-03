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
        private readonly Dictionary<string, GameplayAttribute> attributes = new Dictionary<string, GameplayAttribute>(32);
        private readonly List<ActiveGameplayEffect> activeEffects = new List<ActiveGameplayEffect>(32);
        public IReadOnlyList<ActiveGameplayEffect> ActiveEffects => activeEffects.AsReadOnly();

        private readonly List<GameplayAbilitySpec> activatableAbilities = new List<GameplayAbilitySpec>(16);
        public IReadOnlyList<GameplayAbilitySpec> GetActivatableAbilities() => activatableAbilities.AsReadOnly();

        private readonly List<GameplayAbilitySpec> tickingAbilities = new List<GameplayAbilitySpec>(16);

        // Pre-allocated lists for granted abilities to avoid per-effect List allocations
        private readonly Dictionary<ActiveGameplayEffect, int> effectGrantedAbilitiesStart = new Dictionary<ActiveGameplayEffect, int>(16);
        private readonly Dictionary<ActiveGameplayEffect, int> effectGrantedAbilitiesCount = new Dictionary<ActiveGameplayEffect, int>(16);
        private readonly List<GameplayAbilitySpec> grantedAbilitiesBuffer = new List<GameplayAbilitySpec>(32);

        private readonly List<GameplayAttribute> dirtyAttributes = new List<GameplayAttribute>(32);

        [ThreadStatic]
        private static List<ActiveGameplayEffect> expiredEffectsScratchPad;
        [ThreadStatic]
        private static List<ModifierInfo> executionOutputScratchPad;

        // --- Prediction ---
        private PredictionKey currentPredictionKey;
        private readonly List<ActiveGameplayEffect> pendingPredictedEffects = new List<ActiveGameplayEffect>();

        public IFactory<IGameplayEffectContext> EffectContextFactory { get; private set; }

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
                        GASLog.Warning($"Attribute '{attr.Name}' is already present. Duplicate attributes are not allowed.");
                    }
                }
            }
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
            tickingAbilities.Add(spec);

            // Apply ActivationOwnedTags - tags granted while ability is active
            if (ability.ActivationOwnedTags != null && !ability.ActivationOwnedTags.IsEmpty)
            {
                looseTags.AddTags(ability.ActivationOwnedTags);
                UpdateCombinedTags();
            }

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

        internal void OnAbilityEnded(GameplayAbility ability)
        {
            if (ability.Spec != null)
            {
                if (ability.Spec.IsActive)
                {
                    ability.Spec.IsActive = false;
                    tickingAbilities.Remove(ability.Spec);

                    // Remove ActivationOwnedTags when ability ends
                    if (ability.ActivationOwnedTags != null && !ability.ActivationOwnedTags.IsEmpty)
                    {
                        looseTags.RemoveTags(ability.ActivationOwnedTags);
                        UpdateCombinedTags();
                    }
                }

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
                        activeEffects.RemoveAt(i);
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
                        RemoveActiveEffectAtIndex(i);
                        OnEffectRemoved(effect, true);
                    }
                }
            }

            if (dirtyAttributes.Count > 0)
            {
                RecalculateDirtyAttributes();
            }
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
        /// Recalculates all dirty attributes using UE5-style aggregation formula.
        /// Formula: ((BaseValue + Additive) * Multiplicative) / Division
        /// Multiplicative uses bias-based summation: 1 + (Mod1 - 1) + (Mod2 - 1) + ...
        /// Division uses same bias formula: 1 + (Mod1 - 1) + (Mod2 - 1) + ...
        /// </summary>
        private void RecalculateDirtyAttributes()
        {
            for (int d = 0; d < dirtyAttributes.Count; d++)
            {
                var attr = dirtyAttributes[d];
                attr.IsDirty = false;

                float baseValue = attr.OwningSet.GetBaseValue(attr);
                float additive = 0f;
                // UE5 uses bias of 1.0 for multiply/divide, meaning we sum (magnitude - 1)
                float multiplicativeBiasSum = 0f;  // Will become: 1 + sum of (magnitude - 1)
                float divisionBiasSum = 0f;        // Will become: 1 + sum of (magnitude - 1)
                float overrideValue = 0f;
                bool hasOverride = false;

                var affectingEffects = attr.AffectingEffects;
                for (int i = 0; i < affectingEffects.Count; i++)
                {
                    var effect = affectingEffects[i];

                    // Check if effect is active and meets requirements
                    if (effect.Spec.Def.Period > 0 ||
                        (!effect.Spec.Def.OngoingTagRequirements.IsEmpty && !effect.Spec.Def.OngoingTagRequirements.MeetsRequirements(CombinedTags)))
                    {
                        continue;
                    }

                    var modifiers = effect.Spec.Def.Modifiers;
                    for (int m = 0; m < modifiers.Count; m++)
                    {
                        var mod = modifiers[m];
                        if (effect.Spec.TargetAttributes[m] == attr)
                        {
                            float magnitude = effect.Spec.ModifierMagnitudes[m] * effect.StackCount;
                            switch (mod.Operation)
                            {
                                case EAttributeModifierOperation.Add:
                                    additive += magnitude;
                                    break;
                                case EAttributeModifierOperation.Multiply:
                                    // UE5 bias formula: sum of (magnitude - bias), bias = 1.0
                                    multiplicativeBiasSum += (magnitude - 1f);
                                    break;
                                case EAttributeModifierOperation.Division:
                                    // Same bias formula for division
                                    if (magnitude != 0f) divisionBiasSum += (magnitude - 1f);
                                    break;
                                case EAttributeModifierOperation.Override:
                                    overrideValue = magnitude;
                                    hasOverride = true;
                                    break;
                            }
                        }
                    }
                }

                // Apply UE5 formula: ((Base + Additive) * Multiplicative) / Division
                // Where Multiplicative = 1 + biasSum, Division = 1 + biasSum
                float multiplicative = 1f + multiplicativeBiasSum;
                float division = 1f + divisionBiasSum;
                if (division == 0f) division = 1f;  // Prevent divide by zero

                float finalValue = hasOverride ? overrideValue : ((baseValue + additive) * multiplicative / division);

                attr.OwningSet.PreAttributeChange(attr, ref finalValue);
                attr.OwningSet.SetCurrentValue(attr, finalValue);
            }

            dirtyAttributes.Clear();
        }

        private void OnEffectApplied(ActiveGameplayEffect effect)
        {
            fromEffectsTags.AddTags(effect.Spec.Def.GrantedTags);
            UpdateCombinedTags();

            // Update the attribute's internal effect list directly
            var modifiers = effect.Spec.Def.Modifiers;
            for (int i = 0; i < modifiers.Count; i++)
            {
                var attribute = effect.Spec.TargetAttributes[i];
                // Fallback if cache is missing (rare/instant execution edge case), usually cached.
                if (attribute == null) attribute = GetAttribute(modifiers[i].AttributeName);

                if (attribute != null)
                {
                    attribute.AffectingEffects.Add(effect);
                }
            }

            // Grant abilities from effect using pre-allocated buffer (0 GC)
            if (effect.Spec.Def.GrantedAbilities.Count > 0)
            {
                int startIndex = grantedAbilitiesBuffer.Count;
                int count = 0;
                foreach (var ability in effect.Spec.Def.GrantedAbilities)
                {
                    var newSpec = GrantAbility(ability, effect.Spec.Level);
                    grantedAbilitiesBuffer.Add(newSpec);
                    count++;
                }
                effectGrantedAbilitiesStart[effect] = startIndex;
                effectGrantedAbilitiesCount[effect] = count;
            }

            if (!effect.Spec.Def.GameplayCues.IsEmpty)
            {
                var eventType = (effect.Spec.Def.DurationPolicy == EDurationPolicy.Instant) ? EGameplayCueEvent.Executed : EGameplayCueEvent.OnActive;

                // Iterate through the tags in the effect's cue container.
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
        }

        private void OnEffectRemoved(ActiveGameplayEffect effect, bool markDirty)
        {
            fromEffectsTags.RemoveTags(effect.Spec.Def.GrantedTags);
            UpdateCombinedTags();

            // Remove from attribute's internal list
            var modifiers = effect.Spec.Def.Modifiers;
            for (int i = 0; i < modifiers.Count; i++)
            {
                var attribute = effect.Spec.TargetAttributes[i];
                // Fallback lookup
                if (attribute == null) attribute = GetAttribute(modifiers[i].AttributeName);

                if (attribute != null)
                {
                    int index = attribute.AffectingEffects.IndexOf(effect);
                    if (index >= 0)
                    {
                        int lastIndex = attribute.AffectingEffects.Count - 1;
                        attribute.AffectingEffects[index] = attribute.AffectingEffects[lastIndex];
                        attribute.AffectingEffects.RemoveAt(lastIndex);
                    }
                }
            }

            // Remove granted abilities using buffer indices (0 GC)
            if (effectGrantedAbilitiesStart.TryGetValue(effect, out int startIndex) &&
                effectGrantedAbilitiesCount.TryGetValue(effect, out int count))
            {
                // Clear abilities in reverse to maintain buffer integrity
                for (int i = startIndex + count - 1; i >= startIndex && i < grantedAbilitiesBuffer.Count; i--)
                {
                    var spec = grantedAbilitiesBuffer[i];
                    if (spec != null) ClearAbility(spec);
                }
                effectGrantedAbilitiesStart.Remove(effect);
                effectGrantedAbilitiesCount.Remove(effect);
            }

            if (effect.Spec.Def.DurationPolicy != EDurationPolicy.Instant && !effect.Spec.Def.GameplayCues.IsEmpty)
            {
                foreach (var cueTag in effect.Spec.Def.GameplayCues)
                {
                    if (cueTag.IsNone) continue;
                    GameplayCueManager.Default.HandleCue(cueTag, EGameplayCueEvent.Removed, effect.Spec).Forget();
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
            grantedAbilitiesBuffer.Clear();
            effectGrantedAbilitiesStart.Clear();
            effectGrantedAbilitiesCount.Clear();
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
                        GASLog.Debug($"Stacking limit for {spec.Def.Name} reached.");
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