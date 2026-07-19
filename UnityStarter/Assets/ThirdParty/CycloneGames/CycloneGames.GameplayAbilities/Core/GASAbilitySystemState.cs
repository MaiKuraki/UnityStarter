using System;
using CycloneGames.DeterministicMath;

namespace CycloneGames.GameplayAbilities.Core
{
    /// <summary>
    /// Immutable per-state capacity budget. Limits are ownership policy, not measured
    /// platform recommendations; callers should select them for their gameplay profile.
    /// </summary>
    public sealed class GASAbilitySystemLimits
    {
        public static GASAbilitySystemLimits Default { get; } = new GASAbilitySystemLimits(
            maxAbilities: 256,
            maxAttributes: 512,
            maxActiveEffects: 1024,
            maxModifiers: 16384,
            maxPredictedAttributeChanges: 4096);

        public int MaxAbilities { get; }
        public int MaxAttributes { get; }
        public int MaxActiveEffects { get; }
        public int MaxModifiers { get; }
        public int MaxPredictedAttributeChanges { get; }

        public GASAbilitySystemLimits(
            int maxAbilities,
            int maxAttributes,
            int maxActiveEffects,
            int maxModifiers,
            int maxPredictedAttributeChanges)
        {
            MaxAbilities = RequirePositive(maxAbilities, nameof(maxAbilities));
            MaxAttributes = RequirePositive(maxAttributes, nameof(maxAttributes));
            MaxActiveEffects = RequirePositive(maxActiveEffects, nameof(maxActiveEffects));
            MaxModifiers = RequirePositive(maxModifiers, nameof(maxModifiers));
            MaxPredictedAttributeChanges = RequirePositive(
                maxPredictedAttributeChanges,
                nameof(maxPredictedAttributeChanges));
        }

        private static int RequirePositive(int value, string parameterName)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(parameterName, value, "GAS state limits must be positive.");
            }

            return value;
        }
    }

    /// <summary>
    /// Pure ASC state. This is the authoritative storage model that Runtime adapters should wrap.
    /// It intentionally stores ids, handles and compact value data instead of Unity object references.
    /// </summary>
    public sealed class GASAbilitySystemState
    {
        private GASAbilitySpecData[] abilitySpecs;
        private GASAttributeValueData[] attributes;
        private GASActiveEffectData[] activeEffects;
        private GASModifierData[] modifiers;
        private GASPredictedAttributeChange[] predictedAttributeChanges;

        private int abilitySpecCount;
        private int attributeCount;
        private int activeEffectCount;
        private int modifierCount;
        private int predictedAttributeChangeCount;
        private int nextSpecHandle = 1;
        private int nextEffectHandle = 1;

        public GASEntityId Entity { get; private set; }
        public ulong Version { get; private set; }
        public int AbilitySpecCount => abilitySpecCount;
        public int AttributeCount => attributeCount;
        public int ActiveEffectCount => activeEffectCount;
        public int ModifierCount => modifierCount;
        public int PredictedAttributeChangeCount => predictedAttributeChangeCount;
        public GASAbilitySystemLimits Limits { get; }

        public GASAbilitySystemState(GASEntityId entity, GASAbilitySystemLimits limits)
            : this(entity, 16, 32, 32, 128, 32, limits)
        {
        }

        public GASAbilitySystemState(
            GASEntityId entity,
            int abilityCapacity = 16,
            int attributeCapacity = 32,
            int activeEffectCapacity = 32,
            int modifierCapacity = 128,
            int predictionCapacity = 32,
            GASAbilitySystemLimits limits = null)
        {
            Limits = limits ?? GASAbilitySystemLimits.Default;
            Entity = entity;
            abilitySpecs = new GASAbilitySpecData[ClampInitialCapacity(abilityCapacity, Limits.MaxAbilities)];
            attributes = new GASAttributeValueData[ClampInitialCapacity(attributeCapacity, Limits.MaxAttributes)];
            activeEffects = new GASActiveEffectData[ClampInitialCapacity(activeEffectCapacity, Limits.MaxActiveEffects)];
            modifiers = new GASModifierData[ClampInitialCapacity(modifierCapacity, Limits.MaxModifiers)];
            predictedAttributeChanges = new GASPredictedAttributeChange[
                ClampInitialCapacity(predictionCapacity, Limits.MaxPredictedAttributeChanges)];
        }

        public void Reset(GASEntityId entity)
        {
            Entity = entity;
            abilitySpecCount = 0;
            attributeCount = 0;
            activeEffectCount = 0;
            modifierCount = 0;
            predictedAttributeChangeCount = 0;
            Version++;
        }

        public bool Reserve(
            int abilityCapacity,
            int attributeCapacity,
            int activeEffectCapacity,
            int modifierCapacity,
            int predictionCapacity)
        {
            if (ExceedsLimit(abilityCapacity, Limits.MaxAbilities) ||
                ExceedsLimit(attributeCapacity, Limits.MaxAttributes) ||
                ExceedsLimit(activeEffectCapacity, Limits.MaxActiveEffects) ||
                ExceedsLimit(modifierCapacity, Limits.MaxModifiers) ||
                ExceedsLimit(predictionCapacity, Limits.MaxPredictedAttributeChanges))
            {
                return false;
            }

            if (abilityCapacity > 0)
            {
                if (!EnsureAbilityCapacity(abilityCapacity)) return false;
            }

            if (attributeCapacity > 0)
            {
                if (!EnsureAttributeCapacity(attributeCapacity)) return false;
            }

            if (activeEffectCapacity > 0)
            {
                if (!EnsureActiveEffectCapacity(activeEffectCapacity)) return false;
            }

            if (modifierCapacity > 0)
            {
                if (!EnsureModifierCapacity(modifierCapacity)) return false;
            }

            if (predictionCapacity > 0)
            {
                if (!EnsurePredictionCapacity(predictionCapacity)) return false;
            }

            return true;
        }

        public bool TryGetAbilitySpecByIndex(int index, out GASAbilitySpecData spec)
        {
            if ((uint)index >= (uint)abilitySpecCount)
            {
                spec = default;
                return false;
            }

            spec = abilitySpecs[index];
            return true;
        }

        public bool TryGetAttributeByIndex(int index, out GASAttributeValueData attribute)
        {
            if ((uint)index >= (uint)attributeCount)
            {
                attribute = default;
                return false;
            }

            attribute = attributes[index];
            return true;
        }

        public bool TryGetActiveEffectByIndex(int index, out GASActiveEffectData effect)
        {
            if ((uint)index >= (uint)activeEffectCount)
            {
                effect = default;
                return false;
            }

            effect = activeEffects[index];
            return true;
        }

        public bool TryGetModifierByIndex(int index, out GASModifierData modifier)
        {
            if ((uint)index >= (uint)modifierCount)
            {
                modifier = default;
                return false;
            }

            modifier = modifiers[index];
            return true;
        }

        public void CaptureStateNonAlloc(GASAbilitySystemStateBuffer buffer)
        {
            if (buffer == null)
            {
                return;
            }

            buffer.ClearCounts();
            buffer.Entity = Entity;
            buffer.Version = Version;
            buffer.Checksum = ComputeChecksum();

            if (abilitySpecCount > 0)
            {
                Array.Copy(abilitySpecs, 0, buffer.EnsureAbilitySpecCapacity(abilitySpecCount), 0, abilitySpecCount);
                buffer.AbilitySpecCount = abilitySpecCount;
            }

            if (attributeCount > 0)
            {
                Array.Copy(attributes, 0, buffer.EnsureAttributeCapacity(attributeCount), 0, attributeCount);
                buffer.AttributeCount = attributeCount;
            }

            if (activeEffectCount > 0)
            {
                Array.Copy(activeEffects, 0, buffer.EnsureActiveEffectCapacity(activeEffectCount), 0, activeEffectCount);
                buffer.ActiveEffectCount = activeEffectCount;
            }

            if (modifierCount > 0)
            {
                Array.Copy(modifiers, 0, buffer.EnsureModifierCapacity(modifierCount), 0, modifierCount);
                buffer.ModifierCount = modifierCount;
            }
        }

        public bool TryGetAbilitySpec(GASSpecHandle handle, out GASAbilitySpecData spec)
        {
            int index = FindAbilitySpecIndex(handle);
            if (index < 0)
            {
                spec = default;
                return false;
            }

            spec = abilitySpecs[index];
            return true;
        }

        public bool TryGetAttribute(GASAttributeId attributeId, out GASAttributeValueData attribute)
        {
            int index = FindAttributeIndex(attributeId);
            if (index < 0)
            {
                attribute = default;
                return false;
            }

            attribute = attributes[index];
            return true;
        }

        public GASSpecHandle GrantAbility(
            GASDefinitionId abilityDefinitionId,
            ushort level,
            GASInstancingPolicy instancingPolicy)
        {
            if (abilityDefinitionId.Value <= 0 ||
                !IsValidInstancingPolicy(instancingPolicy) ||
                abilitySpecCount >= Limits.MaxAbilities ||
                !TryAllocateSpecHandle(out var handle))
            {
                return default;
            }

            if (!EnsureAbilityCapacity(abilitySpecCount + 1))
            {
                return default;
            }

            abilitySpecs[abilitySpecCount++] = new GASAbilitySpecData(
                handle,
                abilityDefinitionId,
                level,
                instancingPolicy);
            Version++;
            return handle;
        }

        public bool TryGrantAbility(in GASAbilityGrantRequest request, out GASSpecHandle handle)
        {
            handle = GrantAbility(
                request.AbilityDefinitionId,
                request.Level,
                request.InstancingPolicy);
            return handle.IsValid;
        }

        public bool RemoveAbility(GASSpecHandle handle)
        {
            int index = FindAbilitySpecIndex(handle);
            if (index < 0)
            {
                return false;
            }

            int lastIndex = abilitySpecCount - 1;
            if (index != lastIndex)
            {
                abilitySpecs[index] = abilitySpecs[lastIndex];
            }

            abilitySpecs[lastIndex] = default;
            abilitySpecCount--;
            Version++;
            return true;
        }

        public bool SetAttributeBase(GASAttributeId attributeId, GASFixedValue baseValue)
        {
            return SetAttributeBaseRaw(attributeId, baseValue.RawValue);
        }

        public bool SetAttributeBaseRaw(GASAttributeId attributeId, long baseValueRaw)
        {
            if (HasAnyPredictedAttributeChange(attributeId))
            {
                return false;
            }

            if (!TryEnsureAttribute(attributeId, out int index))
            {
                return false;
            }

            attributes[index] = new GASAttributeValueData(
                attributeId,
                baseValueRaw,
                EvaluateCurrentValueRaw(attributeId, baseValueRaw),
                attributes[index].AggregatorVersion + 1u);
            Version++;
            return true;
        }

        public bool RemoveAttribute(GASAttributeId attributeId)
        {
            int index = FindAttributeIndex(attributeId);
            if (index < 0 || !CanRemoveAttribute(attributeId))
            {
                return false;
            }

            int lastIndex = attributeCount - 1;
            if (index < lastIndex)
            {
                Array.Copy(attributes, index + 1, attributes, index, lastIndex - index);
            }
            attributes[lastIndex] = default;
            attributeCount--;
            Version++;
            return true;
        }

        public bool CanRemoveAttribute(GASAttributeId attributeId)
        {
            if (FindAttributeIndex(attributeId) < 0 || HasAnyPredictedAttributeChange(attributeId))
            {
                return false;
            }

            for (int i = 0; i < modifierCount; i++)
            {
                if (modifiers[i].AttributeId == attributeId)
                {
                    return false;
                }
            }
            return true;
        }

        public bool ApplyInstantModifier(GASModifierData modifier)
        {
            return ApplyInstantModifier(modifier, default);
        }

        public bool ApplyInstantModifier(GASModifierData modifier, GASPredictionKey predictionKey)
        {
            if (!IsValidModifier(in modifier) ||
                !CanRecordPredictedAttributeChange(predictionKey, modifier.AttributeId) ||
                !TryEnsureAttribute(modifier.AttributeId, out int index))
            {
                return false;
            }

            var attribute = attributes[index];
            if (predictionKey.IsValid)
            {
                if (!RecordPredictedAttributeChange(predictionKey, modifier.AttributeId, attribute.BaseValueRaw))
                {
                    return false;
                }
            }

            long baseValueRaw = ApplyModifierToBaseRaw(attribute.BaseValueRaw, modifier);
            attributes[index] = new GASAttributeValueData(
                modifier.AttributeId,
                baseValueRaw,
                EvaluateCurrentValueRaw(modifier.AttributeId, baseValueRaw),
                attribute.AggregatorVersion + 1u);
            Version++;
            return true;
        }

        public bool ApplyInstantModifierRaw(GASAttributeId attributeId, GASModifierOp op, long magnitudeRaw)
        {
            return ApplyInstantModifier(new GASModifierData(attributeId, op, magnitudeRaw), default);
        }

        public bool ApplyInstantModifier(GASAttributeId attributeId, GASModifierOp op, GASFixedValue magnitude)
        {
            return ApplyInstantModifier(new GASModifierData(attributeId, op, magnitude.RawValue), default);
        }

        public bool ApplyInstantModifierRaw(GASAttributeId attributeId, GASModifierOp op, long magnitudeRaw, GASPredictionKey predictionKey)
        {
            return ApplyInstantModifier(new GASModifierData(attributeId, op, magnitudeRaw), predictionKey);
        }

        public bool ApplyInstantModifier(GASAttributeId attributeId, GASModifierOp op, GASFixedValue magnitude, GASPredictionKey predictionKey)
        {
            return ApplyInstantModifier(new GASModifierData(attributeId, op, magnitude.RawValue), predictionKey);
        }

        public GASActiveEffectHandle AddActiveEffect(
            GASDefinitionId effectDefinitionId,
            GASEntityId source,
            GASPredictionKey predictionKey,
            GASEffectDurationPolicy durationPolicy,
            ushort level,
            ushort stackCount,
            long startTick,
            int durationTicks,
            GASModifierData[] effectModifiers,
            int effectModifierStart,
            int effectModifierCount)
        {
            if (!ValidateActiveEffectInput(
                    effectDefinitionId,
                    durationPolicy,
                    stackCount,
                    durationTicks,
                    effectModifiers,
                    effectModifierStart,
                    effectModifierCount) ||
                activeEffectCount >= Limits.MaxActiveEffects ||
                effectModifierCount > Limits.MaxModifiers - modifierCount ||
                !TryAllocateEffectHandle(out var handle))
            {
                return default;
            }

            if (!EnsureActiveEffectCapacity(activeEffectCount + 1) ||
                !EnsureModifierCapacity(modifierCount + effectModifierCount))
            {
                return default;
            }

            int modifierStart = modifierCount;
            for (int i = 0; i < effectModifierCount; i++)
            {
                modifiers[modifierCount++] = effectModifiers[effectModifierStart + i];
            }

            activeEffects[activeEffectCount++] = new GASActiveEffectData(
                handle,
                effectDefinitionId,
                source,
                Entity,

                predictionKey,
                durationPolicy,
                level,
                stackCount,
                startTick,
                durationTicks,
                (uint)modifierStart,
                (ushort)effectModifierCount);

            RecalculateModifiedAttributes(modifierStart, effectModifierCount);
            Version++;
            return handle;
        }

        public GASActiveEffectHandle ApplyGameplayEffectSpecToSelf(in GASGameplayEffectSpecData spec)
        {
            TryApplyGameplayEffectSpecToSelf(in spec, out var handle);
            return handle;
        }

        public bool TryApplyGameplayEffectSpecToSelf(
            in GASGameplayEffectSpecData spec,
            out GASActiveEffectHandle handle)
        {
            handle = default;
            if (!ValidateEffectSpec(in spec))
            {
                return false;
            }

            if (spec.DurationPolicy == GASEffectDurationPolicy.Instant)
            {
                if (!PrepareInstantModifierBatch(in spec))
                {
                    return false;
                }

                for (int i = 0; i < spec.ModifierCount; i++)
                {
                    if (!ApplyInstantModifier(spec.Modifiers[spec.ModifierStart + i], spec.PredictionKey))
                    {
                        throw new InvalidOperationException("Validated GAS instant-effect mutation failed unexpectedly.");
                    }
                }

                return true;
            }

            handle = AddActiveEffect(
                spec.EffectDefinitionId,
                spec.Source,
                spec.PredictionKey,
                spec.DurationPolicy,
                spec.Level,
                spec.StackCount,
                spec.StartTick,
                spec.DurationTicks,
                spec.Modifiers,
                spec.ModifierStart,
                spec.ModifierCount);
            return handle.IsValid;
        }

        public bool RemoveActiveEffect(GASActiveEffectHandle handle)
        {
            int index = FindActiveEffectIndex(handle);
            if (index < 0)
            {
                return false;
            }

            var removed = activeEffects[index];
            RemoveModifierRange((int)removed.ModifierStartIndex, removed.ModifierCount);
            int lastIndex = activeEffectCount - 1;
            if (index < lastIndex)
            {
                Array.Copy(activeEffects, index + 1, activeEffects, index, lastIndex - index);
            }

            activeEffects[lastIndex] = default;
            activeEffectCount--;
            RecalculateAllAttributes();
            Version++;
            return true;
        }

        public int RemoveExpiredEffects(long currentTick)
        {
            int removedCount = 0;
            for (int i = activeEffectCount - 1; i >= 0; i--)
            {
                var effect = activeEffects[i];
                if (effect.DurationPolicy != GASEffectDurationPolicy.Duration)
                {
                    continue;
                }

                if (currentTick - effect.StartTick >= effect.DurationTicks)
                {
                    RemoveActiveEffect(effect.Handle);
                    removedCount++;
                }
            }

            return removedCount;
        }

        public void CommitPrediction(GASPredictionKey predictionKey)
        {
            if (!predictionKey.IsValid)
            {
                return;
            }

            RemovePredictedAttributeChanges(predictionKey, restore: false);
        }

        public void RollbackPrediction(GASPredictionKey predictionKey)
        {
            if (!predictionKey.IsValid)
            {
                return;
            }

            for (int i = activeEffectCount - 1; i >= 0; i--)
            {
                if (activeEffects[i].PredictionKey.Equals(predictionKey))
                {
                    RemoveActiveEffect(activeEffects[i].Handle);
                }
            }

            RemovePredictedAttributeChanges(predictionKey, restore: true);
            Version++;
        }

        /// <summary>
        /// Computes a deterministic state fingerprint using FNV-1a hashing.
        /// Magic numbers: 2166136261 is the FNV-1a 32-bit offset basis;
        /// 16777619 is the FNV-1a prime. Both are standard constants chosen
        /// for their avalanche properties in hash mixing.
        /// Each domain (abilities, attributes, effects) is hashed independently
        /// so the network layer can detect which subsystem changed without
        /// reading the full state snapshot.
        /// </summary>
        public GASStateChecksum ComputeChecksum()
        {
            uint abilities = 2166136261u;
            for (int i = 0; i < abilitySpecCount; i++)
            {
                var spec = abilitySpecs[i];
                abilities = HashInt(abilities, spec.Handle.Value);
                abilities = HashInt(abilities, spec.AbilityDefinitionId.Value);
                abilities = HashInt(abilities, spec.Level);
                abilities = HashInt(abilities, (int)spec.InstancingPolicy);
            }

            uint attrs = 2166136261u;
            for (int i = 0; i < attributeCount; i++)
            {
                var attr = attributes[i];
                attrs = HashInt(attrs, attr.AttributeId.Value);
                attrs = HashLong(attrs, attr.BaseValueRaw);
                attrs = HashLong(attrs, attr.CurrentValueRaw);
                attrs = HashInt(attrs, (int)attr.AggregatorVersion);
            }

            uint effects = 2166136261u;
            for (int i = 0; i < activeEffectCount; i++)
            {
                var effect = activeEffects[i];
                effects = HashInt(effects, effect.Handle.Value);
                effects = HashInt(effects, effect.EffectDefinitionId.Value);
                effects = HashInt(effects, effect.Source.Value);
                effects = HashInt(effects, effect.Target.Value);
                effects = HashInt(effects, effect.PredictionKey.Value);
                effects = HashInt(effects, effect.Level);
                effects = HashInt(effects, effect.StackCount);
                effects = HashLong(effects, effect.StartTick);
                effects = HashInt(effects, effect.DurationTicks);

                int modifierStart = (int)effect.ModifierStartIndex;
                int modifierEnd = modifierStart + effect.ModifierCount;
                for (int modifierIndex = modifierStart; modifierIndex < modifierEnd; modifierIndex++)
                {
                    var modifier = modifiers[modifierIndex];
                    effects = HashInt(effects, modifier.AttributeId.Value);
                    effects = HashInt(effects, (int)modifier.Op);
                    effects = HashInt(effects, (int)modifier.EvaluationChannel);
                    effects = HashLong(effects, modifier.MagnitudeRaw);
                }
            }

            return new GASStateChecksum(abilities, attrs, effects, 2166136261u);
        }

        private bool TryEnsureAttribute(GASAttributeId attributeId, out int index)
        {
            if (attributeId.Value <= 0)
            {
                index = -1;
                return false;
            }

            index = FindAttributeIndex(attributeId);
            if (index >= 0)
            {
                return true;
            }

            if (attributeCount >= Limits.MaxAttributes || !EnsureAttributeCapacity(attributeCount + 1))
            {
                index = -1;
                return false;
            }

            attributes[attributeCount] = new GASAttributeValueData(attributeId, 0L, 0L, 1u);
            index = attributeCount++;
            return true;
        }

        private bool CanRecordPredictedAttributeChange(GASPredictionKey predictionKey, GASAttributeId attributeId)
        {
            if (!predictionKey.IsValid)
            {
                return !HasAnyPredictedAttributeChange(attributeId);
            }

            if (attributeId.Value <= 0)
            {
                return false;
            }

            if (HasPredictedAttributeChangeFromAnotherPrediction(predictionKey, attributeId))
            {
                return false;
            }

            return HasPredictedAttributeChange(predictionKey, attributeId) ||
                   predictedAttributeChangeCount < Limits.MaxPredictedAttributeChanges;
        }

        private bool HasPredictedAttributeChange(GASPredictionKey predictionKey, GASAttributeId attributeId)
        {
            for (int i = 0; i < predictedAttributeChangeCount; i++)
            {
                var change = predictedAttributeChanges[i];
                if (change.PredictionKey.Equals(predictionKey) && change.AttributeId == attributeId)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasAnyPredictedAttributeChange(GASAttributeId attributeId)
        {
            for (int i = 0; i < predictedAttributeChangeCount; i++)
            {
                if (predictedAttributeChanges[i].AttributeId == attributeId)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasPredictedAttributeChangeFromAnotherPrediction(
            GASPredictionKey predictionKey,
            GASAttributeId attributeId)
        {
            for (int i = 0; i < predictedAttributeChangeCount; i++)
            {
                var change = predictedAttributeChanges[i];
                if (!change.PredictionKey.Equals(predictionKey) && change.AttributeId == attributeId)
                {
                    return true;
                }
            }

            return false;
        }

        private bool RecordPredictedAttributeChange(
            GASPredictionKey predictionKey,
            GASAttributeId attributeId,
            long oldBaseValueRaw)
        {
            if (!predictionKey.IsValid || attributeId.Value <= 0)
            {
                return false;
            }

            if (HasPredictedAttributeChange(predictionKey, attributeId))
            {
                return true;
            }

            if (predictedAttributeChangeCount >= Limits.MaxPredictedAttributeChanges ||
                !EnsurePredictionCapacity(predictedAttributeChangeCount + 1))
            {
                return false;
            }

            predictedAttributeChanges[predictedAttributeChangeCount++] = new GASPredictedAttributeChange(predictionKey, attributeId, oldBaseValueRaw);
            return true;
        }

        private void RemovePredictedAttributeChanges(GASPredictionKey predictionKey, bool restore)
        {
            for (int i = predictedAttributeChangeCount - 1; i >= 0; i--)
            {
                var change = predictedAttributeChanges[i];
                if (!change.PredictionKey.Equals(predictionKey))
                {
                    continue;
                }

                if (restore)
                {
                    int attrIndex = FindAttributeIndex(change.AttributeId);
                    if (attrIndex >= 0)
                    {
                        attributes[attrIndex] = new GASAttributeValueData(
                            change.AttributeId,
                            change.OldBaseValueRaw,
                            EvaluateCurrentValueRaw(change.AttributeId, change.OldBaseValueRaw),
                            attributes[attrIndex].AggregatorVersion + 1u);
                    }
                }

                int lastIndex = predictedAttributeChangeCount - 1;
                if (i != lastIndex)
                {
                    predictedAttributeChanges[i] = predictedAttributeChanges[lastIndex];
                }

                predictedAttributeChanges[lastIndex] = default;
                predictedAttributeChangeCount--;
            }
        }

        private bool PrepareInstantModifierBatch(in GASGameplayEffectSpecData spec)
        {
            int additionalAttributes = 0;
            int additionalPredictionRecords = 0;
            int end = spec.ModifierStart + spec.ModifierCount;
            for (int modifierIndex = spec.ModifierStart; modifierIndex < end; modifierIndex++)
            {
                var attributeId = spec.Modifiers[modifierIndex].AttributeId;
                bool firstOccurrence = true;
                for (int earlierIndex = spec.ModifierStart; earlierIndex < modifierIndex; earlierIndex++)
                {
                    if (spec.Modifiers[earlierIndex].AttributeId == attributeId)
                    {
                        firstOccurrence = false;
                        break;
                    }
                }

                if (!firstOccurrence)
                {
                    continue;
                }

                if (FindAttributeIndex(attributeId) < 0)
                {
                    additionalAttributes++;
                }

                if (spec.PredictionKey.IsValid &&
                    !HasPredictedAttributeChange(spec.PredictionKey, attributeId))
                {
                    if (HasPredictedAttributeChangeFromAnotherPrediction(spec.PredictionKey, attributeId))
                    {
                        return false;
                    }

                    additionalPredictionRecords++;
                }
            }

            if (additionalAttributes > Limits.MaxAttributes - attributeCount ||
                additionalPredictionRecords > Limits.MaxPredictedAttributeChanges - predictedAttributeChangeCount)
            {
                return false;
            }

            return EnsureAttributeCapacity(attributeCount + additionalAttributes) &&
                   EnsurePredictionCapacity(predictedAttributeChangeCount + additionalPredictionRecords);
        }

        private static bool ValidateModifierSlice(
            GASModifierData[] effectModifiers,
            int effectModifierStart,
            int effectModifierCount)
        {
            if (effectModifierStart < 0 || effectModifierCount < 0 || effectModifierCount > ushort.MaxValue)
            {
                return false;
            }

            if (effectModifierCount == 0)
            {
                return effectModifiers == null
                    ? effectModifierStart == 0
                    : effectModifierStart <= effectModifiers.Length;
            }

            if (effectModifiers == null ||
                effectModifierStart > effectModifiers.Length ||
                effectModifierCount > effectModifiers.Length - effectModifierStart)
            {
                return false;
            }

            int end = effectModifierStart + effectModifierCount;
            for (int i = effectModifierStart; i < end; i++)
            {
                if (!IsValidModifier(in effectModifiers[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ValidateActiveEffectInput(
            GASDefinitionId effectDefinitionId,
            GASEffectDurationPolicy durationPolicy,
            ushort stackCount,
            int durationTicks,
            GASModifierData[] effectModifiers,
            int effectModifierStart,
            int effectModifierCount)
        {
            return effectDefinitionId.Value > 0 &&
                   durationPolicy != GASEffectDurationPolicy.Instant &&
                   IsValidDurationPolicy(durationPolicy) &&
                   durationTicks >= 0 &&
                   (durationPolicy != GASEffectDurationPolicy.Duration || durationTicks > 0) &&
                   stackCount > 0 &&
                   ValidateModifierSlice(effectModifiers, effectModifierStart, effectModifierCount);
        }

        private static bool ValidateEffectSpec(in GASGameplayEffectSpecData spec)
        {
            return spec.EffectDefinitionId.Value > 0 &&
                   IsValidDurationPolicy(spec.DurationPolicy) &&
                   spec.DurationTicks >= 0 &&
                   (spec.DurationPolicy != GASEffectDurationPolicy.Duration || spec.DurationTicks > 0) &&
                   spec.StackCount > 0 &&
                   ValidateModifierSlice(spec.Modifiers, spec.ModifierStart, spec.ModifierCount);
        }

        private static bool IsValidModifier(in GASModifierData modifier)
        {
            return modifier.AttributeId.Value > 0 &&
                   (byte)modifier.Op <= (byte)GASModifierOp.Override &&
                   GASModifierEvaluationChannels.IsValid(modifier.EvaluationChannel);
        }

        private static bool IsValidDurationPolicy(GASEffectDurationPolicy policy)
        {
            return (byte)policy <= (byte)GASEffectDurationPolicy.Duration;
        }

        private static bool IsValidInstancingPolicy(GASInstancingPolicy policy)
        {
            return (byte)policy <= (byte)GASInstancingPolicy.InstancedPerExecution;
        }

        /// <summary>
        /// Aggregates all active duration/infinite modifiers for a given attribute.
        /// Evaluation channels are processed from Channel0 to Channel9. Each channel
        /// receives the value produced by the previous channel, so projects can express
        /// layered modifier domains without hard-coded gameplay branches.
        /// </summary>
        private long EvaluateCurrentValueRaw(GASAttributeId attributeId, long baseValueRaw)
        {
            long valueRaw = baseValueRaw;
            for (int channelIndex = 0; channelIndex < GASModifierEvaluationChannels.MAX_CHANNEL_COUNT; channelIndex++)
            {
                valueRaw = EvaluateCurrentValueRawForChannel(
                    attributeId,
                    valueRaw,
                    (GASModifierEvaluationChannel)channelIndex);
            }

            return valueRaw;
        }

        /// <summary>
        /// Evaluates a single modifier channel. Later channels receive the value produced by
        /// earlier channels, matching Unreal GAS' evaluation channel layering model.
        /// </summary>
        private long EvaluateCurrentValueRawForChannel(
            GASAttributeId attributeId,
            long inputValueRaw,
            GASModifierEvaluationChannel evaluationChannel)
        {
            var add = FPInt64.Zero;
            var multiply = FPInt64.One;
            var overrideValue = FPInt64.Zero;
            bool hasOverride = false;
            bool hasModifierInChannel = false;

            for (int i = 0; i < activeEffectCount; i++)
            {
                var effect = activeEffects[i];
                int start = (int)effect.ModifierStartIndex;
                int end = start + effect.ModifierCount;
                for (int m = start; m < end; m++)
                {
                    var modifier = modifiers[m];
                    if (modifier.AttributeId != attributeId)
                    {
                        continue;
                    }

                    if (modifier.EvaluationChannel != evaluationChannel)
                    {
                        continue;
                    }

                    hasModifierInChannel = true;
                    var factor = FPInt64.FromRaw(modifier.MagnitudeRaw);
                    var magnitude = factor * FPInt64.FromInt(effect.StackCount);
                    switch (modifier.Op)
                    {
                        case GASModifierOp.Add:
                            add += magnitude;
                            break;
                        case GASModifierOp.Multiply:
                            for (int stackIndex = 0; stackIndex < effect.StackCount; stackIndex++)
                            {
                                multiply *= factor;
                            }
                            break;
                        case GASModifierOp.Division:
                            if (modifier.MagnitudeRaw != 0)
                            {
                                for (int stackIndex = 0; stackIndex < effect.StackCount; stackIndex++)
                                {
                                    multiply /= factor;
                                }
                            }
                            break;
                        case GASModifierOp.Override:
                            hasOverride = true;
                            overrideValue = FPInt64.FromRaw(modifier.MagnitudeRaw);
                            break;
                    }
                }
            }

            if (!hasModifierInChannel)
            {
                return inputValueRaw;
            }

            var inputValue = FPInt64.FromRaw(inputValueRaw);
            return (hasOverride ? overrideValue : (inputValue + add) * multiply).RawValue;
        }

        /// <summary>
        /// Applies a single modifier to a base value for instant effects.
        /// Division by zero is guarded: when the modifier magnitude is near zero,
        /// the operation is silently skipped, returning the unmodified base value.
        /// This matches UE GAS behavior where a 0-magnitude Division modifier is a no-op.
        /// </summary>
        private static long ApplyModifierToBaseRaw(long baseValueRaw, GASModifierData modifier)
        {
            var baseValue = FPInt64.FromRaw(baseValueRaw);
            var magnitude = FPInt64.FromRaw(modifier.MagnitudeRaw);
            switch (modifier.Op)
            {
                case GASModifierOp.Add:
                    return (baseValue + magnitude).RawValue;
                case GASModifierOp.Multiply:
                    return (baseValue * magnitude).RawValue;
                case GASModifierOp.Division:
                    return modifier.MagnitudeRaw != 0 ? (baseValue / magnitude).RawValue : baseValueRaw;
                case GASModifierOp.Override:
                    return modifier.MagnitudeRaw;
                default:
                    return baseValueRaw;
            }
        }

        private void RecalculateModifiedAttributes(int modifierStart, int modifierLength)
        {
            int end = modifierStart + modifierLength;
            for (int i = modifierStart; i < end; i++)
            {
                RecalculateAttribute(modifiers[i].AttributeId);
            }
        }

        private void RecalculateAllAttributes()
        {
            for (int i = 0; i < attributeCount; i++)
            {
                RecalculateAttribute(attributes[i].AttributeId);
            }
        }

        private void RecalculateAttribute(GASAttributeId attributeId)
        {
            int index = FindAttributeIndex(attributeId);
            if (index < 0)
            {
                return;
            }

            var attribute = attributes[index];
            attributes[index] = new GASAttributeValueData(
                attribute.AttributeId,
                attribute.BaseValueRaw,
                EvaluateCurrentValueRaw(attribute.AttributeId, attribute.BaseValueRaw),
                attribute.AggregatorVersion + 1u);
        }

        /// <summary>
        /// Removes a contiguous range of modifiers and adjusts active effect indices.
        /// 
        /// When modifiers at [start, start+length) are removed, all active effects whose
        /// ModifierStartIndex lies AFTER the removed range must have their index decremented
        /// by length. This is a pack-and-fixup operation — we shift the tail modifiers down
        /// and then patch every affected effect's pointer.
        /// 
        /// Effects whose modifiers are INSIDE the removed range are themselves removed
        /// by the caller (see RemoveActiveEffect), so this method only needs to fix up
        /// indices for effects that survive the removal.
        /// </summary>
        private void RemoveModifierRange(int start, int length)
        {
            if (length <= 0)
            {
                return;
            }

            int end = start + length;
            int tailLength = modifierCount - end;
            if (tailLength > 0)
            {
                Array.Copy(modifiers, end, modifiers, start, tailLength);
            }

            for (int i = modifierCount - length; i < modifierCount; i++)
            {
                modifiers[i] = default;
            }

            modifierCount -= length;
            for (int i = 0; i < activeEffectCount; i++)
            {
                var effect = activeEffects[i];
                if (effect.ModifierStartIndex > start)
                {
                    activeEffects[i] = new GASActiveEffectData(
                        effect.Handle,
                        effect.EffectDefinitionId,
                        effect.Source,
                        effect.Target,
                        effect.PredictionKey,
                        effect.DurationPolicy,
                        effect.Level,
                        effect.StackCount,
                        effect.StartTick,
                        effect.DurationTicks,
                        effect.ModifierStartIndex - (uint)length,
                        effect.ModifierCount);
                }
            }
        }

        private bool TryAllocateSpecHandle(out GASSpecHandle handle)
        {
            int candidate = nextSpecHandle > 0 ? nextSpecHandle : 1;
            for (int attempt = 0; attempt <= abilitySpecCount; attempt++)
            {
                var proposed = new GASSpecHandle(candidate);
                if (FindAbilitySpecIndex(proposed) < 0)
                {
                    handle = proposed;
                    nextSpecHandle = AdvancePositiveHandle(candidate);
                    return true;
                }

                candidate = AdvancePositiveHandle(candidate);
            }

            handle = default;
            return false;
        }

        private bool TryAllocateEffectHandle(out GASActiveEffectHandle handle)
        {
            int candidate = nextEffectHandle > 0 ? nextEffectHandle : 1;
            for (int attempt = 0; attempt <= activeEffectCount; attempt++)
            {
                var proposed = new GASActiveEffectHandle(candidate);
                if (FindActiveEffectIndex(proposed) < 0)
                {
                    handle = proposed;
                    nextEffectHandle = AdvancePositiveHandle(candidate);
                    return true;
                }

                candidate = AdvancePositiveHandle(candidate);
            }

            handle = default;
            return false;
        }

        private static int AdvancePositiveHandle(int current)
        {
            return current == int.MaxValue ? 1 : current + 1;
        }

        private int FindAbilitySpecIndex(GASSpecHandle handle)
        {
            for (int i = 0; i < abilitySpecCount; i++)
            {
                if (abilitySpecs[i].Handle == handle)
                {
                    return i;
                }
            }

            return -1;
        }

        private int FindAttributeIndex(GASAttributeId attributeId)
        {
            for (int i = 0; i < attributeCount; i++)
            {
                if (attributes[i].AttributeId == attributeId)
                {
                    return i;
                }
            }

            return -1;
        }

        private int FindActiveEffectIndex(GASActiveEffectHandle handle)
        {
            for (int i = 0; i < activeEffectCount; i++)
            {
                if (activeEffects[i].Handle == handle)
                {
                    return i;
                }
            }

            return -1;
        }

        private bool EnsureAbilityCapacity(int capacity)
        {
            return EnsureArrayCapacity(ref abilitySpecs, capacity, Limits.MaxAbilities);
        }

        private bool EnsureAttributeCapacity(int capacity)
        {
            return EnsureArrayCapacity(ref attributes, capacity, Limits.MaxAttributes);
        }

        private bool EnsureActiveEffectCapacity(int capacity)
        {
            return EnsureArrayCapacity(ref activeEffects, capacity, Limits.MaxActiveEffects);
        }

        private bool EnsureModifierCapacity(int capacity)
        {
            return EnsureArrayCapacity(ref modifiers, capacity, Limits.MaxModifiers);
        }

        private bool EnsurePredictionCapacity(int capacity)
        {
            return EnsureArrayCapacity(
                ref predictedAttributeChanges,
                capacity,
                Limits.MaxPredictedAttributeChanges);
        }

        private static bool EnsureArrayCapacity<T>(ref T[] array, int capacity, int maximum)
        {
            if (capacity < 0 || capacity > maximum)
            {
                return false;
            }

            if (array.Length >= capacity)
            {
                return true;
            }

            long doubled = (long)array.Length * 2L;
            int newCapacity = (int)Math.Min(maximum, Math.Max(capacity, doubled));
            Array.Resize(ref array, newCapacity);
            return true;
        }

        private static int ClampInitialCapacity(int requested, int maximum)
        {
            return Math.Min(maximum, Math.Max(1, requested));
        }

        private static bool ExceedsLimit(int requested, int maximum)
        {
            return requested > maximum;
        }

        private static uint HashInt(uint hash, int value)
        {
            unchecked
            {
                return (hash ^ (uint)value) * 16777619u;
            }
        }

        private static uint HashLong(uint hash, long value)
        {
            hash = HashInt(hash, unchecked((int)value));
            return HashInt(hash, unchecked((int)(value >> 32)));
        }
    }
}
