using System;

namespace CycloneGames.GameplayAbilities.Core
{
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

        public GASAbilitySystemState(
            GASEntityId entity,
            int abilityCapacity = 16,
            int attributeCapacity = 32,
            int activeEffectCapacity = 32,
            int modifierCapacity = 128,
            int predictionCapacity = 32)
        {
            Entity = entity;
            abilitySpecs = new GASAbilitySpecData[Math.Max(1, abilityCapacity)];
            attributes = new GASAttributeValueData[Math.Max(1, attributeCapacity)];
            activeEffects = new GASActiveEffectData[Math.Max(1, activeEffectCapacity)];
            modifiers = new GASModifierData[Math.Max(1, modifierCapacity)];
            predictedAttributeChanges = new GASPredictedAttributeChange[Math.Max(1, predictionCapacity)];
        }

        public void Reset(GASEntityId entity)
        {
            Entity = entity;
            abilitySpecCount = 0;
            attributeCount = 0;
            activeEffectCount = 0;
            modifierCount = 0;
            predictedAttributeChangeCount = 0;
            nextSpecHandle = 1;
            nextEffectHandle = 1;
            Version++;
        }

        public void Reserve(
            int abilityCapacity,
            int attributeCapacity,
            int activeEffectCapacity,
            int modifierCapacity,
            int predictionCapacity)
        {
            if (abilityCapacity > 0)
            {
                EnsureAbilityCapacity(abilityCapacity);
            }

            if (attributeCapacity > 0)
            {
                EnsureAttributeCapacity(attributeCapacity);
            }

            if (activeEffectCapacity > 0)
            {
                EnsureActiveEffectCapacity(activeEffectCapacity);
            }

            if (modifierCapacity > 0)
            {
                EnsureModifierCapacity(modifierCapacity);
            }

            if (predictionCapacity > 0)
            {
                EnsurePredictionCapacity(predictionCapacity);
            }
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
            GASInstancingPolicy instancingPolicy,
            GASNetExecutionPolicy netExecutionPolicy,
            GASReplicationPolicy replicationPolicy)
        {
            if (!abilityDefinitionId.IsValid)
            {
                return default;
            }

            EnsureAbilityCapacity(abilitySpecCount + 1);
            var handle = new GASSpecHandle(nextSpecHandle++);
            abilitySpecs[abilitySpecCount++] = new GASAbilitySpecData(
                handle,
                abilityDefinitionId,
                level,
                instancingPolicy,
                netExecutionPolicy,
                replicationPolicy);
            Version++;
            return handle;
        }

        public bool TryGrantAbility(in GASAbilityGrantRequest request, out GASSpecHandle handle)
        {
            handle = GrantAbility(
                request.AbilityDefinitionId,
                request.Level,
                request.InstancingPolicy,
                request.NetExecutionPolicy,
                request.ReplicationPolicy);
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

        public void SetAttributeBase(GASAttributeId attributeId, float baseValue)
        {
            int index = EnsureAttribute(attributeId);
            attributes[index] = new GASAttributeValueData(
                attributeId,
                baseValue,
                EvaluateCurrentValue(attributeId, baseValue),
                attributes[index].AggregatorVersion + 1u);
            Version++;
        }

        public bool ApplyInstantModifier(GASModifierData modifier)
        {
            return ApplyInstantModifier(modifier, default);
        }

        public bool ApplyInstantModifier(GASModifierData modifier, GASPredictionKey predictionKey)
        {
            int index = EnsureAttribute(modifier.AttributeId);
            var attribute = attributes[index];
            if (predictionKey.IsValid)
            {
                RecordPredictedAttributeChange(predictionKey, modifier.AttributeId, attribute.BaseValue);
            }

            float baseValue = ApplyModifierToBase(attribute.BaseValue, modifier);
            attributes[index] = new GASAttributeValueData(
                modifier.AttributeId,
                baseValue,
                EvaluateCurrentValue(modifier.AttributeId, baseValue),
                attribute.AggregatorVersion + 1u);
            Version++;
            return true;
        }

        public GASActiveEffectHandle AddActiveEffect(
            GASDefinitionId effectDefinitionId,
            GASEntityId source,
            GASPredictionKey predictionKey,
            GASEffectDurationPolicy durationPolicy,
            ushort level,
            ushort stackCount,
            int startTick,
            int durationTicks,
            GASModifierData[] effectModifiers,
            int effectModifierStart,
            int effectModifierCount)
        {
            if (!effectDefinitionId.IsValid)
            {
                return default;
            }

            if (effectModifierCount < 0 || effectModifierStart < 0 || effectModifiers == null && effectModifierCount > 0)
            {
                return default;
            }

            if (effectModifiers != null && effectModifierStart + effectModifierCount > effectModifiers.Length)
            {
                return default;
            }

            EnsureActiveEffectCapacity(activeEffectCount + 1);
            EnsureModifierCapacity(modifierCount + effectModifierCount);

            int modifierStart = modifierCount;
            for (int i = 0; i < effectModifierCount; i++)
            {
                modifiers[modifierCount++] = effectModifiers[effectModifierStart + i];
            }

            var handle = new GASActiveEffectHandle(nextEffectHandle++);
            activeEffects[activeEffectCount++] = new GASActiveEffectData(
                handle,
                effectDefinitionId,
                source,
                Entity,

                predictionKey,
                durationPolicy,
                level,
                stackCount == 0 ? (ushort)1 : stackCount,
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
            if (spec.DurationPolicy == GASEffectDurationPolicy.Instant)
            {
                if (spec.Modifiers == null && spec.ModifierCount > 0)
                {
                    return default;
                }

                if (spec.ModifierStart < 0 || spec.ModifierCount < 0)
                {
                    return default;
                }

                if (spec.Modifiers != null && spec.ModifierStart + spec.ModifierCount > spec.Modifiers.Length)
                {
                    return default;
                }

                for (int i = 0; i < spec.ModifierCount; i++)
                {
                    ApplyInstantModifier(spec.Modifiers[spec.ModifierStart + i], spec.PredictionKey);
                }

                return default;
            }

            return AddActiveEffect(
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
            if (index != lastIndex)
            {
                activeEffects[index] = activeEffects[lastIndex];
            }

            activeEffects[lastIndex] = default;
            activeEffectCount--;
            RecalculateAllAttributes();
            Version++;
            return true;
        }

        public int RemoveExpiredEffects(int currentTick)
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

        public void AcceptPrediction(GASPredictionKey predictionKey)
        {
            if (!predictionKey.IsValid)
            {
                return;
            }

            RemovePredictedAttributeChanges(predictionKey, restore: false);
        }

        public void RejectPrediction(GASPredictionKey predictionKey)
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
        /// 
        /// Magic numbers: 2166136261 is the FNV-1a 32-bit offset basis;
        /// 16777619 is the FNV-1a prime. Both are standard constants chosen
        /// for their avalanche properties in hash mixing.
        /// 
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
                abilities = HashInt(abilities, (int)spec.NetExecutionPolicy);
            }

            uint attrs = 2166136261u;
            for (int i = 0; i < attributeCount; i++)
            {
                var attr = attributes[i];
                attrs = HashInt(attrs, attr.AttributeId.Value);
                attrs = HashFloat(attrs, attr.BaseValue);
                attrs = HashFloat(attrs, attr.CurrentValue);
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
                effects = HashInt(effects, effect.StartTick);
                effects = HashInt(effects, effect.DurationTicks);
            }

            return new GASStateChecksum(abilities, attrs, effects, 2166136261u);
        }

        private int EnsureAttribute(GASAttributeId attributeId)
        {
            int index = FindAttributeIndex(attributeId);
            if (index >= 0)
            {
                return index;
            }

            EnsureAttributeCapacity(attributeCount + 1);
            attributes[attributeCount] = new GASAttributeValueData(attributeId, 0f, 0f, 1u);
            return attributeCount++;
        }

        private void RecordPredictedAttributeChange(GASPredictionKey predictionKey, GASAttributeId attributeId, float oldBaseValue)
        {
            for (int i = 0; i < predictedAttributeChangeCount; i++)
            {
                var change = predictedAttributeChanges[i];
                if (change.PredictionKey.Equals(predictionKey) && change.AttributeId == attributeId)
                {
                    return;
                }
            }

            EnsurePredictionCapacity(predictedAttributeChangeCount + 1);
            predictedAttributeChanges[predictedAttributeChangeCount++] = new GASPredictedAttributeChange(predictionKey, attributeId, oldBaseValue);
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
                    int attrIndex = EnsureAttribute(change.AttributeId);
                    attributes[attrIndex] = new GASAttributeValueData(
                        change.AttributeId,
                        change.OldBaseValue,
                        EvaluateCurrentValue(change.AttributeId, change.OldBaseValue),
                        attributes[attrIndex].AggregatorVersion + 1u);
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

        /// <summary>
        /// Aggregates all active duration/infinite modifiers for a given attribute.
        /// 
        /// Modifier stacking order (matching UE GAS semantics):
        /// 1. Accumulate all Add modifiers (scaled by stack count).
        /// 2. Accumulate all Multiply/Division modifiers as a single multiplicative factor.
        /// 3. If any Override modifier is present, it wins — all other modifiers are ignored
        ///    and the attribute's current value is set to the last Override's magnitude.
        /// 4. Otherwise: (BaseValue + ΣAdd) × ΠMultiply.
        /// 
        /// Stack count scaling: each modifier's magnitude is multiplied by its parent
        /// active effect's StackCount before being applied. This is why Add uses
        /// magnitude * stackCount but Multiply/Division use the raw magnitude
        /// (they compound multiplicatively across stacks).
        /// </summary>
        private float EvaluateCurrentValue(GASAttributeId attributeId, float baseValue)
        {
            float add = 0f;
            float multiply = 1f;
            float overrideValue = 0f;
            bool hasOverride = false;

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

                    float magnitude = modifier.Magnitude * effect.StackCount;
                    switch (modifier.Op)
                    {
                        case GASModifierOp.Add:
                            add += magnitude;
                            break;
                        case GASModifierOp.Multiply:
                            multiply *= modifier.Magnitude;
                            break;
                        case GASModifierOp.Division:
                            if (Math.Abs(modifier.Magnitude) > float.Epsilon)
                            {
                                multiply /= modifier.Magnitude;
                            }
                            break;
                        case GASModifierOp.Override:
                            hasOverride = true;
                            overrideValue = modifier.Magnitude;
                            break;
                    }
                }
            }

            return hasOverride ? overrideValue : (baseValue + add) * multiply;
        }

        /// <summary>
        /// Applies a single modifier to a base value for instant effects.
        /// Division by zero is guarded: when the modifier magnitude is near zero,
        /// the operation is silently skipped, returning the unmodified base value.
        /// This matches UE GAS behavior where a 0-magnitude Division modifier is a no-op.
        /// </summary>
        private static float ApplyModifierToBase(float baseValue, GASModifierData modifier)
        {
            switch (modifier.Op)
            {
                case GASModifierOp.Add:
                    return baseValue + modifier.Magnitude;
                case GASModifierOp.Multiply:
                    return baseValue * modifier.Magnitude;
                case GASModifierOp.Division:
                    return Math.Abs(modifier.Magnitude) > float.Epsilon ? baseValue / modifier.Magnitude : baseValue;
                case GASModifierOp.Override:
                    return modifier.Magnitude;
                default:
                    return baseValue;
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
                attribute.BaseValue,
                EvaluateCurrentValue(attribute.AttributeId, attribute.BaseValue),
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

        private void EnsureAbilityCapacity(int capacity)
        {
            if (abilitySpecs.Length >= capacity)
            {
                return;
            }

            Array.Resize(ref abilitySpecs, abilitySpecs.Length * 2 >= capacity ? abilitySpecs.Length * 2 : capacity);
        }

        private void EnsureAttributeCapacity(int capacity)
        {
            if (attributes.Length >= capacity)
            {
                return;
            }

            Array.Resize(ref attributes, attributes.Length * 2 >= capacity ? attributes.Length * 2 : capacity);
        }

        private void EnsureActiveEffectCapacity(int capacity)
        {
            if (activeEffects.Length >= capacity)
            {
                return;
            }

            Array.Resize(ref activeEffects, activeEffects.Length * 2 >= capacity ? activeEffects.Length * 2 : capacity);
        }

        private void EnsureModifierCapacity(int capacity)
        {
            if (modifiers.Length >= capacity)
            {
                return;
            }

            Array.Resize(ref modifiers, modifiers.Length * 2 >= capacity ? modifiers.Length * 2 : capacity);
        }

        private void EnsurePredictionCapacity(int capacity)
        {
            if (predictedAttributeChanges.Length >= capacity)
            {
                return;
            }

            Array.Resize(ref predictedAttributeChanges, predictedAttributeChanges.Length * 2 >= capacity ? predictedAttributeChanges.Length * 2 : capacity);
        }

        private static uint HashInt(uint hash, int value)
        {
            unchecked
            {
                return (hash ^ (uint)value) * 16777619u;
            }
        }

        private static uint HashFloat(uint hash, float value)
        {
            return HashInt(hash, BitConverter.SingleToInt32Bits(value));
        }
    }
}
