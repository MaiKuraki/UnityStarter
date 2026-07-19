using System;
using System.Collections.Generic;
using CycloneGames.Hash.Core;

namespace CycloneGames.GameplayAbilities.Networking
{
    /// <summary>Fixed capacities for one replicated GAS entity state.</summary>
    public readonly struct GASNetworkStateCapacity
    {
        public static readonly GASNetworkStateCapacity Default = new GASNetworkStateCapacity(
            abilities: 32,
            attributes: 64,
            effects: 64,
            effectTags: 128,
            effectMagnitudes: 128,
            looseTags: 96);

        public GASNetworkStateCapacity(
            int abilities,
            int attributes,
            int effects,
            int effectTags,
            int effectMagnitudes,
            int looseTags)
        {
            ValidateCategory(abilities, nameof(abilities));
            ValidateCategory(attributes, nameof(attributes));
            ValidateCategory(effects, nameof(effects));
            ValidateCategory(effectTags, nameof(effectTags));
            ValidateCategory(effectMagnitudes, nameof(effectMagnitudes));
            ValidateCategory(looseTags, nameof(looseTags));

            long total = (long)abilities + attributes + effects + effectTags + effectMagnitudes + looseTags;
            int protocolMaximum = GameplayAbilitiesNetworkProtocol.MaxChunksPerBatch *
                                  GameplayAbilitiesNetworkProtocol.MaxRecordsPerChunk;
            if (total > protocolMaximum)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(abilities),
                    $"A full GAS state cannot exceed the protocol batch limit of {protocolMaximum} records.");
            }

            Abilities = abilities;
            Attributes = attributes;
            Effects = effects;
            EffectTags = effectTags;
            EffectMagnitudes = effectMagnitudes;
            LooseTags = looseTags;
        }

        public int Abilities { get; }
        public int Attributes { get; }
        public int Effects { get; }
        public int EffectTags { get; }
        public int EffectMagnitudes { get; }
        public int LooseTags { get; }
        public int TotalRecords => Abilities + Attributes + Effects + EffectTags + EffectMagnitudes + LooseTags;

        private static void ValidateCategory(int value, string parameterName)
        {
            int maximum = GameplayAbilitiesNetworkProtocol.MaxChunksPerBatch *
                          GameplayAbilitiesNetworkProtocol.MaxRecordsPerChunk;
            if (value < 0 || value > maximum)
                throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    /// <summary>Read-only view of one complete, canonical GAS entity state.</summary>
    public interface IGASNetworkStateView
    {
        GASNetworkEntityId Entity { get; }
        ulong StateVersion { get; }
        uint LastProcessedCommandSequence { get; }
        ulong StateChecksum { get; }
        int AbilityCount { get; }
        int AttributeCount { get; }
        int EffectCount { get; }
        int EffectTagCount { get; }
        int EffectMagnitudeCount { get; }
        int LooseTagCount { get; }
        GASAbilityStateRecord GetAbility(int index);
        GASAttributeStateRecord GetAttribute(int index);
        GASEffectStateRecord GetEffect(int index);
        GASEffectTagStateRecord GetEffectTag(int index);
        GASEffectMagnitudeStateRecord GetEffectMagnitude(int index);
        GASLooseTagStateRecord GetLooseTag(int index);
    }

    /// <summary>
    /// Reusable, fixed-capacity full state. Mutation is owner-thread-affine; completed views may be
    /// read from another thread only when the owner publishes them with its own synchronization.
    /// </summary>
    public sealed class GASNetworkStateBuffer : IGASNetworkStateView
    {
        private readonly GASAbilityStateRecord[] abilities;
        private readonly GASAttributeStateRecord[] attributes;
        private readonly GASEffectStateRecord[] effects;
        private readonly GASEffectTagStateRecord[] effectTags;
        private readonly GASEffectMagnitudeStateRecord[] effectMagnitudes;
        private readonly GASLooseTagStateRecord[] looseTags;

        public GASNetworkStateBuffer(GASNetworkStateCapacity capacity)
        {
            Capacity = capacity;
            abilities = new GASAbilityStateRecord[capacity.Abilities];
            attributes = new GASAttributeStateRecord[capacity.Attributes];
            effects = new GASEffectStateRecord[capacity.Effects];
            effectTags = new GASEffectTagStateRecord[capacity.EffectTags];
            effectMagnitudes = new GASEffectMagnitudeStateRecord[capacity.EffectMagnitudes];
            looseTags = new GASLooseTagStateRecord[capacity.LooseTags];
        }

        public GASNetworkStateCapacity Capacity { get; }
        public GASNetworkEntityId Entity { get; private set; }
        public ulong StateVersion { get; private set; }
        public uint LastProcessedCommandSequence { get; private set; }
        public ulong StateChecksum { get; private set; }
        private int abilityCount;
        private int attributeCount;
        private int effectCount;
        private int effectTagCount;
        private int effectMagnitudeCount;
        private int looseTagCount;
        public int AbilityCount => abilityCount;
        public int AttributeCount => attributeCount;
        public int EffectCount => effectCount;
        public int EffectTagCount => effectTagCount;
        public int EffectMagnitudeCount => effectMagnitudeCount;
        public int LooseTagCount => looseTagCount;
        public bool IsComplete => Entity.IsValid && StateVersion != 0UL && StateChecksum != 0UL;
        public ReadOnlySpan<GASAbilityStateRecord> Abilities => abilities.AsSpan(0, abilityCount);
        public ReadOnlySpan<GASAttributeStateRecord> Attributes => attributes.AsSpan(0, attributeCount);
        public ReadOnlySpan<GASEffectStateRecord> Effects => effects.AsSpan(0, effectCount);
        public ReadOnlySpan<GASEffectTagStateRecord> EffectTags => effectTags.AsSpan(0, effectTagCount);
        public ReadOnlySpan<GASEffectMagnitudeStateRecord> EffectMagnitudes => effectMagnitudes.AsSpan(0, effectMagnitudeCount);
        public ReadOnlySpan<GASLooseTagStateRecord> LooseTags => looseTags.AsSpan(0, looseTagCount);

        /// <summary>Clears previous records and starts writing one absolute state.</summary>
        public void BeginWrite(
            GASNetworkEntityId entity,
            ulong stateVersion,
            uint lastProcessedCommandSequence)
        {
            if (!entity.IsValid)
                throw new ArgumentOutOfRangeException(nameof(entity));
            if (stateVersion == 0UL)
                throw new ArgumentOutOfRangeException(nameof(stateVersion));
            if (lastProcessedCommandSequence > GameplayAbilitiesNetworkProtocol.MaxSequence)
                throw new ArgumentOutOfRangeException(nameof(lastProcessedCommandSequence));

            Clear();
            Entity = entity;
            StateVersion = stateVersion;
            LastProcessedCommandSequence = lastProcessedCommandSequence;
        }

        public void Clear()
        {
            Entity = default;
            StateVersion = 0UL;
            LastProcessedCommandSequence = 0u;
            StateChecksum = 0UL;
            abilityCount = 0;
            attributeCount = 0;
            effectCount = 0;
            effectTagCount = 0;
            effectMagnitudeCount = 0;
            looseTagCount = 0;
        }

        public bool TrySetAbility(in GASAbilityStateRecord record)
        {
            if (record.Operation != GASStateRecordOperation.Upsert ||
                GASNetworkMessageValidator.Validate(in record) != GASNetworkMessageValidationResult.Valid)
                return false;

            int index = FindAbility(record.Grant);
            if (index >= 0)
            {
                abilities[index] = record;
                StateChecksum = 0UL;
                return true;
            }

            if (AbilityCount >= abilities.Length)
                return false;
            abilities[abilityCount++] = record;
            StateChecksum = 0UL;
            return true;
        }

        public bool TrySetAttribute(in GASAttributeStateRecord record)
        {
            if (record.Operation != GASStateRecordOperation.Upsert ||
                GASNetworkMessageValidator.Validate(in record) != GASNetworkMessageValidationResult.Valid)
                return false;

            int index = FindAttribute(record.Attribute);
            if (index >= 0)
            {
                attributes[index] = record;
                StateChecksum = 0UL;
                return true;
            }

            if (AttributeCount >= attributes.Length)
                return false;
            attributes[attributeCount++] = record;
            StateChecksum = 0UL;
            return true;
        }

        public bool TrySetEffect(in GASEffectStateRecord record)
        {
            if (record.Operation != GASStateRecordOperation.Upsert ||
                GASNetworkMessageValidator.Validate(in record) != GASNetworkMessageValidationResult.Valid)
                return false;

            int index = FindEffect(record.Effect);
            if (index >= 0)
            {
                effects[index] = record;
                StateChecksum = 0UL;
                return true;
            }

            if (EffectCount >= effects.Length)
                return false;
            effects[effectCount++] = record;
            StateChecksum = 0UL;
            return true;
        }

        public bool TrySetEffectTag(in GASEffectTagStateRecord record)
        {
            if (record.Operation != GASStateRecordOperation.Upsert ||
                GASNetworkMessageValidator.Validate(in record) != GASNetworkMessageValidationResult.Valid)
                return false;

            int index = FindEffectTag(record.Effect, record.Tag, record.Kind);
            if (index >= 0)
            {
                effectTags[index] = record;
                StateChecksum = 0UL;
                return true;
            }

            if (EffectTagCount >= effectTags.Length)
                return false;
            effectTags[effectTagCount++] = record;
            StateChecksum = 0UL;
            return true;
        }

        public bool TrySetEffectMagnitude(in GASEffectMagnitudeStateRecord record)
        {
            if (record.Operation != GASStateRecordOperation.Upsert ||
                GASNetworkMessageValidator.Validate(in record) != GASNetworkMessageValidationResult.Valid)
                return false;

            int index = FindEffectMagnitude(record.Effect, record.Key);
            if (index >= 0)
            {
                effectMagnitudes[index] = record;
                StateChecksum = 0UL;
                return true;
            }

            if (EffectMagnitudeCount >= effectMagnitudes.Length)
                return false;
            effectMagnitudes[effectMagnitudeCount++] = record;
            StateChecksum = 0UL;
            return true;
        }

        public bool TrySetLooseTag(in GASLooseTagStateRecord record)
        {
            if (record.Operation != GASStateRecordOperation.Upsert ||
                GASNetworkMessageValidator.Validate(in record) != GASNetworkMessageValidationResult.Valid)
                return false;

            int index = FindLooseTag(record.Tag);
            if (index >= 0)
            {
                looseTags[index] = record;
                StateChecksum = 0UL;
                return true;
            }

            if (LooseTagCount >= looseTags.Length)
                return false;
            looseTags[looseTagCount++] = record;
            StateChecksum = 0UL;
            return true;
        }

        /// <summary>Sorts records, validates cross-record references, and seals the checksum.</summary>
        public bool TryCompleteWrite()
        {
            if (!Entity.IsValid || StateVersion == 0UL)
                return false;

            SortRecords();
            if (!ValidateReferences())
                return false;

            StateChecksum = ComputeChecksum();
            return StateChecksum != 0UL;
        }

        public GASAbilityStateRecord GetAbility(int index) => Get(abilities, AbilityCount, index);
        public GASAttributeStateRecord GetAttribute(int index) => Get(attributes, AttributeCount, index);
        public GASEffectStateRecord GetEffect(int index) => Get(effects, EffectCount, index);
        public GASEffectTagStateRecord GetEffectTag(int index) => Get(effectTags, EffectTagCount, index);
        public GASEffectMagnitudeStateRecord GetEffectMagnitude(int index) => Get(effectMagnitudes, EffectMagnitudeCount, index);
        public GASLooseTagStateRecord GetLooseTag(int index) => Get(looseTags, LooseTagCount, index);

        internal bool TryCopyFrom(GASNetworkStateBuffer source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (source.AbilityCount > abilities.Length ||
                source.AttributeCount > attributes.Length ||
                source.EffectCount > effects.Length ||
                source.EffectTagCount > effectTags.Length ||
                source.EffectMagnitudeCount > effectMagnitudes.Length ||
                source.LooseTagCount > looseTags.Length)
                return false;

            Clear();
            Entity = source.Entity;
            StateVersion = source.StateVersion;
            LastProcessedCommandSequence = source.LastProcessedCommandSequence;
            StateChecksum = source.StateChecksum;
            abilityCount = source.AbilityCount;
            attributeCount = source.AttributeCount;
            effectCount = source.EffectCount;
            effectTagCount = source.EffectTagCount;
            effectMagnitudeCount = source.EffectMagnitudeCount;
            looseTagCount = source.LooseTagCount;
            Array.Copy(source.abilities, abilities, AbilityCount);
            Array.Copy(source.attributes, attributes, AttributeCount);
            Array.Copy(source.effects, effects, EffectCount);
            Array.Copy(source.effectTags, effectTags, EffectTagCount);
            Array.Copy(source.effectMagnitudes, effectMagnitudes, EffectMagnitudeCount);
            Array.Copy(source.looseTags, looseTags, LooseTagCount);
            return true;
        }

        internal void SetPendingMetadata(in GASStateBatchChunk header)
        {
            Entity = header.Entity;
            StateVersion = header.StateVersion;
            LastProcessedCommandSequence = header.LastProcessedCommandSequence;
            StateChecksum = 0UL;
        }

        internal bool TryApplyAbility(in GASAbilityStateRecord record)
        {
            return record.Operation == GASStateRecordOperation.Upsert
                ? TrySetAbility(in record)
                : RemoveAbility(record.Grant);
        }

        internal bool TryApplyAttribute(in GASAttributeStateRecord record)
        {
            return record.Operation == GASStateRecordOperation.Upsert
                ? TrySetAttribute(in record)
                : RemoveAttribute(record.Attribute);
        }

        internal bool TryApplyEffect(in GASEffectStateRecord record)
        {
            return record.Operation == GASStateRecordOperation.Upsert
                ? TrySetEffect(in record)
                : RemoveEffect(record.Effect);
        }

        internal bool TryApplyEffectTag(in GASEffectTagStateRecord record)
        {
            return record.Operation == GASStateRecordOperation.Upsert
                ? TrySetEffectTag(in record)
                : RemoveEffectTag(record.Effect, record.Tag, record.Kind);
        }

        internal bool TryApplyEffectMagnitude(in GASEffectMagnitudeStateRecord record)
        {
            return record.Operation == GASStateRecordOperation.Upsert
                ? TrySetEffectMagnitude(in record)
                : RemoveEffectMagnitude(record.Effect, record.Key);
        }

        internal bool TryApplyLooseTag(in GASLooseTagStateRecord record)
        {
            return record.Operation == GASStateRecordOperation.Upsert
                ? TrySetLooseTag(in record)
                : RemoveLooseTag(record.Tag);
        }

        private bool RemoveAbility(GASNetworkGrantId grant)
        {
            int index = FindAbility(grant);
            if (index >= 0)
                RemoveAt(abilities, ref abilityCount, index);
            StateChecksum = 0UL;
            return true;
        }

        private bool RemoveAttribute(GASNetworkContentId attribute)
        {
            int index = FindAttribute(attribute);
            if (index >= 0)
                RemoveAt(attributes, ref attributeCount, index);
            StateChecksum = 0UL;
            return true;
        }

        private bool RemoveEffect(GASNetworkEffectId effect)
        {
            int index = FindEffect(effect);
            if (index >= 0)
                RemoveAt(effects, ref effectCount, index);

            for (int i = EffectTagCount - 1; i >= 0; i--)
            {
                if (effectTags[i].Effect == effect)
                    RemoveAt(effectTags, ref effectTagCount, i);
            }
            for (int i = EffectMagnitudeCount - 1; i >= 0; i--)
            {
                if (effectMagnitudes[i].Effect == effect)
                    RemoveAt(effectMagnitudes, ref effectMagnitudeCount, i);
            }

            StateChecksum = 0UL;
            return true;
        }

        private bool RemoveEffectTag(
            GASNetworkEffectId effect,
            GASNetworkTagId tag,
            GASEffectTagKind kind)
        {
            int index = FindEffectTag(effect, tag, kind);
            if (index >= 0)
                RemoveAt(effectTags, ref effectTagCount, index);
            StateChecksum = 0UL;
            return true;
        }

        private bool RemoveEffectMagnitude(GASNetworkEffectId effect, GASNetworkMagnitudeKey key)
        {
            int index = FindEffectMagnitude(effect, key);
            if (index >= 0)
                RemoveAt(effectMagnitudes, ref effectMagnitudeCount, index);
            StateChecksum = 0UL;
            return true;
        }

        private bool RemoveLooseTag(GASNetworkTagId tag)
        {
            int index = FindLooseTag(tag);
            if (index >= 0)
                RemoveAt(looseTags, ref looseTagCount, index);
            StateChecksum = 0UL;
            return true;
        }

        private int FindAbility(GASNetworkGrantId grant)
        {
            for (int i = 0; i < AbilityCount; i++)
                if (abilities[i].Grant == grant) return i;
            return -1;
        }

        private int FindAttribute(GASNetworkContentId attribute)
        {
            for (int i = 0; i < AttributeCount; i++)
                if (attributes[i].Attribute == attribute) return i;
            return -1;
        }

        private int FindEffect(GASNetworkEffectId effect)
        {
            for (int i = 0; i < EffectCount; i++)
                if (effects[i].Effect == effect) return i;
            return -1;
        }

        private int FindEffectTag(GASNetworkEffectId effect, GASNetworkTagId tag, GASEffectTagKind kind)
        {
            for (int i = 0; i < EffectTagCount; i++)
            {
                if (effectTags[i].Effect == effect && effectTags[i].Tag == tag && effectTags[i].Kind == kind)
                    return i;
            }
            return -1;
        }

        private int FindEffectMagnitude(GASNetworkEffectId effect, GASNetworkMagnitudeKey key)
        {
            for (int i = 0; i < EffectMagnitudeCount; i++)
                if (effectMagnitudes[i].Effect == effect && effectMagnitudes[i].Key == key) return i;
            return -1;
        }

        private int FindLooseTag(GASNetworkTagId tag)
        {
            for (int i = 0; i < LooseTagCount; i++)
                if (looseTags[i].Tag == tag) return i;
            return -1;
        }

        private bool ValidateReferences()
        {
            for (int i = 0; i < AbilityCount; i++)
            {
                GASNetworkEffectId grantingEffect = abilities[i].GrantingEffect;
                if (grantingEffect.IsValid && !ContainsCanonicalEffect(grantingEffect)) return false;
            }
            for (int i = 0; i < EffectTagCount; i++)
                if (!ContainsCanonicalEffect(effectTags[i].Effect)) return false;
            for (int i = 0; i < EffectMagnitudeCount; i++)
                if (!ContainsCanonicalEffect(effectMagnitudes[i].Effect)) return false;
            return true;
        }

        private bool ContainsCanonicalEffect(GASNetworkEffectId effect)
        {
            int low = 0;
            int high = EffectCount - 1;
            while (low <= high)
            {
                int middle = low + ((high - low) >> 1);
                ulong value = effects[middle].Effect.Value;
                if (value == effect.Value)
                    return true;
                if (value < effect.Value)
                    low = middle + 1;
                else
                    high = middle - 1;
            }

            return false;
        }

        private void SortRecords()
        {
            Array.Sort(abilities, 0, AbilityCount, AbilityComparer.Instance);
            Array.Sort(attributes, 0, AttributeCount, AttributeComparer.Instance);
            Array.Sort(effects, 0, EffectCount, EffectComparer.Instance);
            Array.Sort(effectTags, 0, EffectTagCount, EffectTagComparer.Instance);
            Array.Sort(effectMagnitudes, 0, EffectMagnitudeCount, EffectMagnitudeComparer.Instance);
            Array.Sort(looseTags, 0, LooseTagCount, LooseTagComparer.Instance);
        }

        private ulong ComputeChecksum()
        {
            ulong hash = Fnv1a64.OffsetBasis;
            hash = Combine(hash, Entity.Value);
            hash = Combine(hash, StateVersion);
            hash = Combine(hash, LastProcessedCommandSequence);

            hash = Combine(hash, 1UL);
            hash = Combine(hash, (ulong)AbilityCount);
            for (int i = 0; i < AbilityCount; i++)
            {
                GASAbilityStateRecord value = abilities[i];
                hash = Combine(hash, value.Grant.Value);
                hash = Combine(hash, value.Definition.Value);
                hash = Combine(hash, value.GrantingEffect.Value);
                hash = Combine(hash, unchecked((ulong)value.Level));
                hash = Combine(hash, (byte)value.Flags);
            }

            hash = Combine(hash, 2UL);
            hash = Combine(hash, (ulong)AttributeCount);
            for (int i = 0; i < AttributeCount; i++)
            {
                GASAttributeStateRecord value = attributes[i];
                hash = Combine(hash, value.Attribute.Value);
                hash = Combine(hash, unchecked((ulong)value.BaseValueRaw));
                hash = Combine(hash, unchecked((ulong)value.CurrentValueRaw));
            }

            hash = Combine(hash, 3UL);
            hash = Combine(hash, (ulong)EffectCount);
            for (int i = 0; i < EffectCount; i++)
            {
                GASEffectStateRecord value = effects[i];
                hash = Combine(hash, value.Effect.Value);
                hash = Combine(hash, value.Definition.Value);
                hash = Combine(hash, value.SourceEntity.Value);
                hash = Combine(hash, value.SourceStreamEpoch);
                hash = Combine(hash, value.SourceGrant.Value);
                hash = Combine(hash, unchecked((ulong)value.Level));
                hash = Combine(hash, unchecked((ulong)value.StackCount));
                hash = Combine(hash, unchecked((ulong)value.DurationRaw));
                hash = Combine(hash, unchecked((ulong)value.RemainingRaw));
                hash = Combine(hash, unchecked((ulong)value.PeriodRaw));
                hash = Combine(hash, value.SourceCommandSequence);
                hash = Combine(hash, (byte)value.Flags);
            }

            hash = Combine(hash, 4UL);
            hash = Combine(hash, (ulong)EffectTagCount);
            for (int i = 0; i < EffectTagCount; i++)
            {
                GASEffectTagStateRecord value = effectTags[i];
                hash = Combine(hash, value.Effect.Value);
                hash = Combine(hash, value.Tag.Value);
                hash = Combine(hash, (byte)value.Kind);
            }

            hash = Combine(hash, 5UL);
            hash = Combine(hash, (ulong)EffectMagnitudeCount);
            for (int i = 0; i < EffectMagnitudeCount; i++)
            {
                GASEffectMagnitudeStateRecord value = effectMagnitudes[i];
                hash = Combine(hash, value.Effect.Value);
                hash = Combine(hash, (byte)value.Key.Kind);
                hash = Combine(hash, value.Key.Value);
                hash = Combine(hash, unchecked((ulong)value.ValueRaw));
            }

            hash = Combine(hash, 6UL);
            hash = Combine(hash, (ulong)LooseTagCount);
            for (int i = 0; i < LooseTagCount; i++)
            {
                GASLooseTagStateRecord value = looseTags[i];
                hash = Combine(hash, value.Tag.Value);
                hash = Combine(hash, unchecked((ulong)value.Count));
            }

            return StableHash64.EnsureNonZero(hash);
        }

        private static ulong Combine(ulong hash, ulong value)
        {
            return StableHash64.CombineUInt64LittleEndian(hash, value);
        }

        private static T Get<T>(T[] values, int count, int index)
        {
            if ((uint)index >= (uint)count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return values[index];
        }

        private static void RemoveAt<T>(T[] values, ref int count, int index)
        {
            int last = --count;
            if (index != last)
                values[index] = values[last];
            values[last] = default;
        }

        private sealed class AbilityComparer : IComparer<GASAbilityStateRecord>
        {
            public static readonly AbilityComparer Instance = new AbilityComparer();
            public int Compare(GASAbilityStateRecord x, GASAbilityStateRecord y) => x.Grant.Value.CompareTo(y.Grant.Value);
        }

        private sealed class AttributeComparer : IComparer<GASAttributeStateRecord>
        {
            public static readonly AttributeComparer Instance = new AttributeComparer();
            public int Compare(GASAttributeStateRecord x, GASAttributeStateRecord y) => x.Attribute.Value.CompareTo(y.Attribute.Value);
        }

        private sealed class EffectComparer : IComparer<GASEffectStateRecord>
        {
            public static readonly EffectComparer Instance = new EffectComparer();
            public int Compare(GASEffectStateRecord x, GASEffectStateRecord y) => x.Effect.Value.CompareTo(y.Effect.Value);
        }

        private sealed class EffectTagComparer : IComparer<GASEffectTagStateRecord>
        {
            public static readonly EffectTagComparer Instance = new EffectTagComparer();
            public int Compare(GASEffectTagStateRecord x, GASEffectTagStateRecord y)
            {
                int result = x.Effect.Value.CompareTo(y.Effect.Value);
                if (result != 0) return result;
                result = ((byte)x.Kind).CompareTo((byte)y.Kind);
                return result != 0 ? result : x.Tag.Value.CompareTo(y.Tag.Value);
            }
        }

        private sealed class EffectMagnitudeComparer : IComparer<GASEffectMagnitudeStateRecord>
        {
            public static readonly EffectMagnitudeComparer Instance = new EffectMagnitudeComparer();
            public int Compare(GASEffectMagnitudeStateRecord x, GASEffectMagnitudeStateRecord y)
            {
                int result = x.Effect.Value.CompareTo(y.Effect.Value);
                if (result != 0) return result;
                result = ((byte)x.Key.Kind).CompareTo((byte)y.Key.Kind);
                return result != 0 ? result : x.Key.Value.CompareTo(y.Key.Value);
            }
        }

        private sealed class LooseTagComparer : IComparer<GASLooseTagStateRecord>
        {
            public static readonly LooseTagComparer Instance = new LooseTagComparer();
            public int Compare(GASLooseTagStateRecord x, GASLooseTagStateRecord y) => x.Tag.Value.CompareTo(y.Tag.Value);
        }
    }
}
