using System;
using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayAbilities.Runtime
{
    public partial class AbilitySystemComponent
    {
        private GameplayTag[] fullStateCurrentLooseTagScratch = Array.Empty<GameplayTag>();
        private readonly Dictionary<GameplayTag, int> fullStateLooseTagCounts =
            new Dictionary<GameplayTag, int>();
        private readonly HashSet<int> fullStateAbilityHandleScratch = new HashSet<int>();
        private readonly HashSet<int> fullStateEffectIdScratch = new HashSet<int>();
        private readonly Dictionary<int, GameplayEffect> fullStateEffectDefinitionScratch =
            new Dictionary<int, GameplayEffect>();
        private readonly HashSet<GameplayEffect> fullStateTargetStackingScratch =
            new HashSet<GameplayEffect>();
        private readonly HashSet<(GameplayEffect Definition, AbilitySystemComponent Source)>
            fullStateSourceStackingScratch =
                new HashSet<(GameplayEffect Definition, AbilitySystemComponent Source)>();

        /// <summary>
        /// Marks an interrupted full-state reconciliation as requiring a new authoritative
        /// snapshot and closes every prediction window that can no longer be correlated safely.
        /// </summary>
        internal void RequireFullStateResynchronization()
        {
            AssertRuntimeThread();
            if (!stateDeltaResyncRequired)
            {
                RequireStateDeltaResync(GASStateDeltaRejectionReason.ApplicationFailed);
            }
            RollbackAllOpenPredictionWindows();
        }

        /// <summary>
        /// Replaces the complete process-local replicated gameplay state after a fail-closed
        /// preflight. The snapshot contains local identities and runtime definitions; it is not a
        /// wire contract. Unexpected application failures require another full snapshot.
        /// </summary>
        /// <remarks>
        /// The ASC remains owner-thread-affine. The method does not overwrite the ASC's local
        /// monotonic <see cref="StateVersion"/> because a replica can have local prediction
        /// mutations that do not share the authority's counter.
        /// </remarks>
        public bool TryApplyFullStateSnapshot(
            GASAbilitySystemFullStateBuffer snapshot,
            out GASStateDeltaRejectionReason rejectionReason)
        {
            AssertRuntimeThread();
            if (effectMutationTransactionDepth != 0 ||
                activeEffectIterationDepth != 0 ||
                callbackDispatchDepth != 0)
            {
                rejectionReason = GASStateDeltaRejectionReason.ApplicationFailed;
                return false;
            }

            if (!ValidateFullStateSnapshot(snapshot, out rejectionReason))
            {
                return false;
            }

            try
            {
                using (new ReconciliationApplyScope(this))
                {
                    RollbackAllOpenPredictionWindows();

                    for (int i = activeEffects.Count - 1; i >= 0; i--)
                    {
                        ActiveGameplayEffect effect = activeEffects[i];
                        if (!fullStateEffectIdScratch.Contains(effect.ReconciliationId))
                        {
                            RemoveActiveEffectAtIndex(i);
                            RemoveFromStackingIndex(effect);
                            OnEffectRemoved(effect, true);
                        }
                    }

                    EnsureFullStateAbilitySpecs(
                        snapshot.GrantedAbilities,
                        snapshot.GrantedAbilityCount);

                    for (int i = 0; i < snapshot.ActiveEffectCount; i++)
                    {
                        ref readonly GASActiveEffectStateData state = ref snapshot.ActiveEffects[i];
                        var definition = (GameplayEffect)state.EffectDefinition;
                        var source = state.SourceComponent as AbilitySystemComponent;
                        if (!ApplyActiveEffectState(in state, definition, source))
                        {
                            throw new InvalidOperationException(
                                $"Active effect state {state.ReconciliationId} could not be applied.");
                        }
                    }

                    ApplyFullLooseTagState(snapshot.LooseTags, snapshot.LooseTagCount);
                    ApplyGrantedAbilityReplacement(snapshot.GrantedAbilities, snapshot.GrantedAbilityCount);

                    if (dirtyAttributes.Count > 0)
                    {
                        RecalculateDirtyAttributes();
                    }

                    ApplyAuthorityAttributeStateData(snapshot.Attributes, snapshot.AttributeCount);
                    ValidateAppliedInhibitionState(snapshot.ActiveEffects, snapshot.ActiveEffectCount);

                    if (!ValidateRuntimeIndexes())
                    {
                        throw new InvalidOperationException(
                            "GAS runtime indexes were inconsistent after full-state application.");
                    }
                }

                if (snapshot.StateChecksum != 0UL &&
                    ComputeReplicatedStateChecksum() != snapshot.StateChecksum)
                {
                    RequireStateDeltaResync(GASStateDeltaRejectionReason.ChecksumMismatch);
                    rejectionReason = GASStateDeltaRejectionReason.ChecksumMismatch;
                    return false;
                }

                ClearPendingStateChanges();
                stateDeltaResyncRequired = false;
                rejectionReason = GASStateDeltaRejectionReason.None;
                return true;
            }
            catch (Exception exception)
            {
                GASLog.Warning(sb => sb
                    .Append("GAS full-state application failed and requires another snapshot: ")
                    .Append(exception.Message));
                RequireStateDeltaResync(GASStateDeltaRejectionReason.ApplicationFailed);
                rejectionReason = GASStateDeltaRejectionReason.ApplicationFailed;
                return false;
            }
            finally
            {
                ClearFullStateValidationScratch();
            }
        }

        private bool ValidateFullStateSnapshot(
            GASAbilitySystemFullStateBuffer snapshot,
            out GASStateDeltaRejectionReason rejectionReason)
        {
            ClearFullStateValidationScratch();
            if (snapshot == null)
            {
                rejectionReason = GASStateDeltaRejectionReason.MissingDelta;
                return false;
            }
            if (snapshot.SchemaVersion != GASRuntimeDataContract.ReconciliationSchemaVersion)
            {
                rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                return false;
            }
            if (!IsValidCount(snapshot.GrantedAbilityCount, snapshot.GrantedAbilities) ||
                !IsValidCount(snapshot.ActiveEffectCount, snapshot.ActiveEffects) ||
                !IsValidCount(snapshot.AttributeCount, snapshot.Attributes) ||
                !IsValidCount(snapshot.LooseTagCount, snapshot.LooseTags))
            {
                rejectionReason = GASStateDeltaRejectionReason.InvalidCounts;
                return false;
            }
            if (snapshot.GrantedAbilityCount > Limits.MaxGrantedAbilities ||
                snapshot.ActiveEffectCount > Limits.MaxActiveEffects ||
                snapshot.AttributeCount > Limits.MaxAttributes ||
                snapshot.LooseTagCount > Limits.MaxTagChangesPerDelta ||
                snapshot.AttributeCount != attributes.Count)
            {
                rejectionReason = GASStateDeltaRejectionReason.CapacityExceeded;
                return false;
            }

            try
            {
                fullStateEffectIdScratch.EnsureCapacity(snapshot.ActiveEffectCount);
                fullStateEffectDefinitionScratch.EnsureCapacity(snapshot.ActiveEffectCount);
                fullStateTargetStackingScratch.EnsureCapacity(snapshot.ActiveEffectCount);
                fullStateSourceStackingScratch.EnsureCapacity(snapshot.ActiveEffectCount);
                for (int i = 0; i < snapshot.ActiveEffectCount; i++)
                {
                    ref readonly GASActiveEffectStateData effect = ref snapshot.ActiveEffects[i];
                    if (!(effect.EffectDefinition is GameplayEffect definition) ||
                        definition.DurationPolicy == EDurationPolicy.Instant ||
                        definition.Modifiers.Count > Limits.MaxModifiersPerEffect ||
                        effect.ReconciliationId <= 0 ||
                        !fullStateEffectIdScratch.Add(effect.ReconciliationId) ||
                        effect.Level <= 0 ||
                        effect.Level > GASRuntimeDataContract.MaxGameplayLevel ||
                        effect.StackCount <= 0 ||
                        effect.StackCount > GetMaximumReplicatedStackCount(definition) ||
                        effect.SourceAbilitySpecHandle < 0 ||
                        !IsCanonicalPredictionKey(effect.PredictionKey) ||
                        (definition.DurationPolicy == EDurationPolicy.HasDuration && effect.DurationRaw <= 0L) ||
                        effect.DurationRaw < 0L ||
                        effect.TimeRemainingRaw < 0L ||
                        (definition.Period > 0f
                            ? effect.PeriodTimeRemainingRaw < 0L
                            : effect.PeriodTimeRemainingRaw != -1L) ||
                        !ValidateFullStateEffectCollections(in effect))
                    {
                        rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                        return false;
                    }

                    var source = effect.SourceComponent as AbilitySystemComponent;
                    if (effect.SourceComponent != null &&
                        (source == null || source.IsDisposed ||
                         !ReferenceEquals(source.RuntimeContext, RuntimeContext)))
                    {
                        rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                        return false;
                    }
                    switch (definition.Stacking.Type)
                    {
                        case EGameplayEffectStackingType.AggregateByTarget:
                            if (!fullStateTargetStackingScratch.Add(definition))
                            {
                                rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                                return false;
                            }
                            break;
                        case EGameplayEffectStackingType.AggregateBySource:
                            if (!fullStateSourceStackingScratch.Add((definition, source)))
                            {
                                rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                                return false;
                            }
                            break;
                    }

                    fullStateEffectDefinitionScratch.Add(effect.ReconciliationId, definition);
                }

                fullStateAbilityHandleScratch.EnsureCapacity(snapshot.GrantedAbilityCount);
                for (int i = 0; i < snapshot.GrantedAbilityCount; i++)
                {
                    ref readonly GASGrantedAbilityStateData ability = ref snapshot.GrantedAbilities[i];
                    if (!(ability.AbilityDefinition is GameplayAbility definition) ||
                        !definition.IsConfigurationInitialized ||
                        definition.InstancingPolicy == EGameplayAbilityInstancingPolicy.NonInstanced ||
                        ability.SpecHandle <= 0 ||
                        !fullStateAbilityHandleScratch.Add(ability.SpecHandle) ||
                        ability.Level <= 0 ||
                        ability.Level > GASRuntimeDataContract.MaxGameplayLevel ||
                        ability.GrantingEffectReconciliationId < 0 ||
                        (ability.GrantingEffectReconciliationId > 0 &&
                         (!fullStateEffectDefinitionScratch.TryGetValue(
                              ability.GrantingEffectReconciliationId,
                              out GameplayEffect grantingEffectDefinition) ||
                          !DefinitionGrantsAbility(grantingEffectDefinition, definition))))
                    {
                        rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                        return false;
                    }
                }

                for (int i = 0; i < snapshot.ActiveEffectCount; i++)
                {
                    ref readonly GASActiveEffectStateData effect = ref snapshot.ActiveEffects[i];
                    if (effect.SourceAbilitySpecHandle <= 0)
                        continue;

                    var source = effect.SourceComponent as AbilitySystemComponent;
                    bool sourceGrantExists = ReferenceEquals(source, this)
                        ? fullStateAbilityHandleScratch.Contains(effect.SourceAbilitySpecHandle)
                        : source != null && source.FindSpecByHandle(effect.SourceAbilitySpecHandle) != null;
                    if (!sourceGrantExists)
                    {
                        rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                        return false;
                    }
                }

                stateDeltaNameValidationScratch.Clear();
                for (int i = 0; i < snapshot.AttributeCount; i++)
                {
                    string name = snapshot.Attributes[i].AttributeName;
                    if (!IsValidReplicatedName(name) ||
                        !attributes.ContainsKey(name) ||
                        !stateDeltaNameValidationScratch.Add(name))
                    {
                        rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                        return false;
                    }
                }

                long totalLooseTagCount = 0L;
                stateDeltaTagValidationScratch.Clear();
                fullStateLooseTagCounts.EnsureCapacity(snapshot.LooseTagCount);
                for (int i = 0; i < snapshot.LooseTagCount; i++)
                {
                    GASTagCountStateData entry = snapshot.LooseTags[i];
                    totalLooseTagCount += entry.ExplicitCount;
                    if (entry.Tag.IsNone ||
                        !entry.Tag.IsValid ||
                        entry.ExplicitCount <= 0 ||
                        entry.ExplicitCount > Limits.MaxTagChangesPerDelta ||
                        totalLooseTagCount > Limits.MaxTagChangesPerDelta ||
                        !stateDeltaTagValidationScratch.Add(entry.Tag))
                    {
                        rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                        return false;
                    }

                    fullStateLooseTagCounts.Add(entry.Tag, entry.ExplicitCount);
                }
            }
            catch (Exception exception)
            {
                GASLog.Warning(sb => sb
                    .Append("Rejected GAS full-state snapshot during preflight: ")
                    .Append(exception.Message));
                rejectionReason = GASStateDeltaRejectionReason.InvalidPayload;
                return false;
            }

            rejectionReason = GASStateDeltaRejectionReason.None;
            return true;
        }

        private bool ValidateFullStateEffectCollections(in GASActiveEffectStateData effect)
        {
            if (effect.SetByCallerTagMagnitudeCount < 0 ||
                effect.SetByCallerNameMagnitudeCount < 0 ||
                effect.SetByCallerTagMagnitudeCount >
                    Limits.MaxSetByCallerEntries - effect.SetByCallerNameMagnitudeCount ||
                (effect.SetByCallerTagMagnitudeCount > 0 &&
                 (effect.SetByCallerTagMagnitudes == null ||
                  effect.SetByCallerTagMagnitudes.Length < effect.SetByCallerTagMagnitudeCount)) ||
                (effect.SetByCallerNameMagnitudeCount > 0 &&
                 (effect.SetByCallerNameMagnitudes == null ||
                  effect.SetByCallerNameMagnitudes.Length < effect.SetByCallerNameMagnitudeCount)) ||
                !IsValidReplicatedTagArray(effect.DynamicGrantedTags, effect.DynamicGrantedTagCount) ||
                !IsValidReplicatedTagArray(effect.DynamicAssetTags, effect.DynamicAssetTagCount) ||
                effect.DynamicGrantedTagCount >
                    GameplayEffect.MaxAggregateTagCount - effect.DynamicAssetTagCount)
            {
                return false;
            }

            stateDeltaTagValidationScratch.Clear();
            for (int i = 0; i < effect.SetByCallerTagMagnitudeCount; i++)
            {
                GameplayTag tag = effect.SetByCallerTagMagnitudes[i].Tag;
                if (tag.IsNone || !tag.IsValid || !stateDeltaTagValidationScratch.Add(tag))
                    return false;
            }

            stateDeltaNameValidationScratch.Clear();
            for (int i = 0; i < effect.SetByCallerNameMagnitudeCount; i++)
            {
                string name = effect.SetByCallerNameMagnitudes[i].Name;
                if (!IsValidReplicatedName(name) || !stateDeltaNameValidationScratch.Add(name))
                    return false;
            }

            stateDeltaTagValidationScratch.Clear();
            for (int i = 0; i < effect.DynamicGrantedTagCount; i++)
            {
                if (!stateDeltaTagValidationScratch.Add(effect.DynamicGrantedTags[i]))
                    return false;
            }

            stateDeltaTagValidationScratch.Clear();
            for (int i = 0; i < effect.DynamicAssetTagCount; i++)
            {
                if (!stateDeltaTagValidationScratch.Add(effect.DynamicAssetTags[i]))
                    return false;
            }

            return true;
        }

        private static int GetMaximumReplicatedStackCount(GameplayEffect definition)
        {
            return definition.Stacking.Type == EGameplayEffectStackingType.None
                ? 1
                : definition.Stacking.Limit;
        }

        private void ApplyFullLooseTagState(GASTagCountStateData[] entries, int count)
        {
            int currentCount = looseTags.ExplicitTagCount;
            EnsureArrayCapacity(ref fullStateCurrentLooseTagScratch, currentCount);
            GameplayTagEnumerator enumerator = looseTags.GetExplicitTags();
            int copied = 0;
            while (enumerator.MoveNext())
            {
                fullStateCurrentLooseTagScratch[copied++] = enumerator.Current;
            }

            for (int i = 0; i < copied; i++)
            {
                GameplayTag tag = fullStateCurrentLooseTagScratch[i];
                fullStateLooseTagCounts.TryGetValue(tag, out int desiredCount);
                SetLooseTagCount(tag, desiredCount);
            }
            for (int i = 0; i < count; i++)
            {
                GASTagCountStateData entry = entries[i];
                if (looseTags.GetExplicitTagCount(entry.Tag) == 0)
                {
                    SetLooseTagCount(entry.Tag, entry.ExplicitCount);
                }
            }
        }

        private void EnsureFullStateAbilitySpecs(GASGrantedAbilityStateData[] abilities, int count)
        {
            ReassignUnambiguousReplicatedAbilitySpecs(abilities, count);

            for (int i = activatableAbilities.Count - 1; i >= 0; i--)
            {
                GameplayAbilitySpec spec = activatableAbilities[i];
                if (!fullStateAbilityHandleScratch.Contains(spec.Handle))
                {
                    ClearAbilityInternal(spec);
                }
            }

            for (int i = 0; i < count; i++)
            {
                GASGrantedAbilityStateData state = abilities[i];
                var definition = (GameplayAbility)state.AbilityDefinition;
                GameplayAbilitySpec spec = FindSpecByHandle(state.SpecHandle);
                if (spec != null && !ReferenceEquals(spec.AbilityCDO ?? spec.Ability, definition))
                {
                    ClearAbilityInternal(spec);
                    spec = null;
                }

                if (spec == null)
                {
                    spec = GrantAbility(definition, state.Level, state.SpecHandle);
                }
                else
                {
                    spec.Level = state.Level;
                }

                spec.IsInputPressed = state.IsInputPressed;
            }
        }

        private void SetLooseTagCount(GameplayTag tag, int desiredCount)
        {
            int currentCount = looseTags.GetExplicitTagCount(tag);
            while (currentCount < desiredCount)
            {
                looseTags.AddTag(tag);
                combinedTags.AddTag(tag);
                currentCount++;
            }
            while (currentCount > desiredCount)
            {
                looseTags.RemoveTag(tag);
                combinedTags.RemoveTag(tag);
                currentCount--;
            }
        }

        private void ValidateAppliedInhibitionState(GASActiveEffectStateData[] effects, int count)
        {
            for (int i = 0; i < count; i++)
            {
                GASActiveEffectStateData state = effects[i];
                ActiveGameplayEffect applied = FindActiveEffectByReconciliationId(state.ReconciliationId);
                if (applied == null || applied.IsInhibited != state.IsInhibited)
                {
                    throw new InvalidOperationException(
                        $"Active effect {state.ReconciliationId} inhibition state did not match the snapshot.");
                }
            }
        }

        private void ClearFullStateValidationScratch()
        {
            fullStateAbilityHandleScratch.Clear();
            fullStateEffectIdScratch.Clear();
            fullStateEffectDefinitionScratch.Clear();
            fullStateTargetStackingScratch.Clear();
            fullStateSourceStackingScratch.Clear();
            fullStateLooseTagCounts.Clear();
            stateDeltaNameValidationScratch.Clear();
            stateDeltaTagValidationScratch.Clear();
        }
    }
}
