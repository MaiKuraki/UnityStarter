using System;
using CycloneGames.Hash.Core;

namespace CycloneGames.GameplayAbilities.Networking
{
    public static class GASNetworkStateChecksum
    {
        /// <summary>
        /// Computes a deterministic checksum from wire-format values. The tag buffer is sorted in place.
        /// </summary>
        public static ulong Compute(
            GrantedAbilityEntry[] abilities,
            int abilityCount,
            EffectReplicationData[] effects,
            int effectCount,
            AttributeEntry[] attributes,
            int attributeCount,
            int[] tagHashes,
            int tagCount)
        {
            ulong hash = Fnv1a64.OffsetBasis;
            hash = HashUInt(hash, 0x4741534Eu);

            int safeAbilityCount = abilities != null ? Math.Min(abilityCount, abilities.Length) : 0;
            hash = HashInt(hash, safeAbilityCount);
            for (int i = 0; i < safeAbilityCount; i++)
            {
                var ability = abilities[i];
                hash = HashInt(hash, ability.AbilityDefinitionId);
                hash = HashInt(hash, ability.AbilitySpecHandle);
                hash = HashInt(hash, ability.Level);
                hash = HashBool(hash, ability.IsActive);
            }

            int safeEffectCount = effects != null ? Math.Min(effectCount, effects.Length) : 0;
            hash = HashInt(hash, safeEffectCount);
            for (int i = 0; i < safeEffectCount; i++)
            {
                var effect = effects[i];
                hash = HashUInt(hash, effect.TargetNetworkId);
                hash = HashUInt(hash, effect.SourceNetworkId);
                hash = HashInt(hash, effect.EffectInstanceId);
                hash = HashInt(hash, effect.EffectDefinitionId);
                hash = HashInt(hash, effect.Level);
                hash = HashInt(hash, effect.StackCount);
                hash = HashLong(hash, effect.DurationRaw);
                hash = HashLong(hash, effect.TimeRemainingRaw);
                hash = HashLong(hash, effect.PeriodTimeRemainingRaw);
                hash = HashInt(hash, effect.PredictionKey);
                hash = HashInt(hash, effect.PredictionKeyOwner);
                hash = HashInt(hash, effect.PredictionInputSequence);

                int safeSetByCallerCount = effect.SetByCallerEntries != null
                    ? Math.Min(effect.SetByCallerCount, effect.SetByCallerEntries.Length)
                    : 0;
                hash = HashInt(hash, safeSetByCallerCount);
                for (int entryIndex = 0; entryIndex < safeSetByCallerCount; entryIndex++)
                {
                    var entry = effect.SetByCallerEntries[entryIndex];
                    hash = HashInt(hash, entry.TagHash);
                    hash = HashLong(hash, entry.ValueRaw);
                }
            }

            int safeAttributeCount = attributes != null ? Math.Min(attributeCount, attributes.Length) : 0;
            hash = HashInt(hash, safeAttributeCount);
            for (int i = 0; i < safeAttributeCount; i++)
            {
                var attribute = attributes[i];
                hash = HashInt(hash, attribute.AttributeId);
                hash = HashLong(hash, attribute.BaseValueRaw);
                hash = HashLong(hash, attribute.CurrentValueRaw);
            }

            int safeTagCount = tagHashes != null ? Math.Min(tagCount, tagHashes.Length) : 0;
            if (safeTagCount > 1)
            {
                Array.Sort(tagHashes, 0, safeTagCount);
            }

            hash = HashInt(hash, safeTagCount);
            for (int i = 0; i < safeTagCount; i++)
            {
                hash = HashInt(hash, tagHashes[i]);
            }

            return hash;
        }

        private static ulong HashBool(ulong hash, bool value)
        {
            return HashUInt(hash, value ? 1u : 0u);
        }

        private static ulong HashInt(ulong hash, int value)
        {
            return HashUInt(hash, unchecked((uint)value));
        }

        private static ulong HashLong(ulong hash, long value)
        {
            hash = HashUInt(hash, unchecked((uint)value));
            return HashUInt(hash, unchecked((uint)(value >> 32)));
        }

        private static ulong HashUInt(ulong hash, uint value)
        {
            return Fnv1a64.CombineUInt32LittleEndian(hash, value);
        }
    }
}
