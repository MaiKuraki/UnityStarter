using System;
using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayAbilities.Core
{
    /// <summary>Process-local absolute count for one explicitly owned loose GameplayTag.</summary>
    public readonly struct GASTagCountStateData
    {
        public readonly GameplayTag Tag;
        public readonly int ExplicitCount;

        public GASTagCountStateData(GameplayTag tag, int explicitCount)
        {
            Tag = tag;
            ExplicitCount = explicitCount;
        }
    }

    /// <summary>
    /// Reusable process-local full-state snapshot for an AbilitySystemComponent.
    /// </summary>
    /// <remarks>
    /// This buffer contains local handles, definition references, source references, and attribute
    /// names. It is deliberately not a wire DTO. A network integration must resolve every entry to
    /// an authority-issued stable identity before encoding it.
    /// </remarks>
    public sealed class GASAbilitySystemFullStateBuffer
    {
        public ushort SchemaVersion = GASRuntimeDataContract.ReconciliationSchemaVersion;
        public ulong StateVersion;
        public ulong StateChecksum;

        public GASGrantedAbilityStateData[] GrantedAbilities = Array.Empty<GASGrantedAbilityStateData>();
        public int GrantedAbilityCount;

        public GASActiveEffectStateData[] ActiveEffects = Array.Empty<GASActiveEffectStateData>();
        public int ActiveEffectCount;
        public GASSetByCallerTagStateData[][] ActiveEffectSetByCallerMagnitudes =
            Array.Empty<GASSetByCallerTagStateData[]>();
        public GASSetByCallerNameStateData[][] ActiveEffectSetByCallerNameMagnitudes =
            Array.Empty<GASSetByCallerNameStateData[]>();
        public GameplayTag[][] ActiveEffectDynamicGrantedTags = Array.Empty<GameplayTag[]>();
        public GameplayTag[][] ActiveEffectDynamicAssetTags = Array.Empty<GameplayTag[]>();

        public GASAttributeStateData[] Attributes = Array.Empty<GASAttributeStateData>();
        public int AttributeCount;

        public GASTagCountStateData[] LooseTags = Array.Empty<GASTagCountStateData>();
        public int LooseTagCount;

        public void Reserve(
            int grantedAbilityCapacity,
            int activeEffectCapacity,
            int attributeCapacity,
            int looseTagCapacity,
            int maxSetByCallerTagsPerEffect = 0,
            int maxSetByCallerNamesPerEffect = 0,
            int maxDynamicGrantedTagsPerEffect = 0,
            int maxDynamicAssetTagsPerEffect = 0)
        {
            EnsureGrantedAbilityCapacity(grantedAbilityCapacity);
            EnsureActiveEffectCapacity(activeEffectCapacity);
            EnsureAttributeCapacity(attributeCapacity);
            EnsureLooseTagCapacity(looseTagCapacity);

            for (int i = 0; i < activeEffectCapacity; i++)
            {
                EnsureActiveEffectSetByCallerCapacity(i, maxSetByCallerTagsPerEffect);
                EnsureActiveEffectSetByCallerNameCapacity(i, maxSetByCallerNamesPerEffect);
                EnsureActiveEffectDynamicGrantedTagCapacity(i, maxDynamicGrantedTagsPerEffect);
                EnsureActiveEffectDynamicAssetTagCapacity(i, maxDynamicAssetTagsPerEffect);
            }
        }

        public void ClearCounts()
        {
            SchemaVersion = GASRuntimeDataContract.ReconciliationSchemaVersion;
            StateVersion = 0UL;
            StateChecksum = 0UL;
            GrantedAbilityCount = 0;
            ActiveEffectCount = 0;
            AttributeCount = 0;
            LooseTagCount = 0;
        }

        public GASGrantedAbilityStateData[] EnsureGrantedAbilityCapacity(int capacity)
        {
            EnsureNonNegative(capacity, nameof(capacity));
            if (GrantedAbilities.Length < capacity)
            {
                GrantedAbilities = new GASGrantedAbilityStateData[capacity];
            }

            return GrantedAbilities;
        }

        public GASActiveEffectStateData[] EnsureActiveEffectCapacity(int capacity)
        {
            EnsureNonNegative(capacity, nameof(capacity));
            if (ActiveEffects.Length < capacity)
            {
                ActiveEffects = new GASActiveEffectStateData[capacity];
            }

            ResizeJagged(ref ActiveEffectSetByCallerMagnitudes, capacity);
            ResizeJagged(ref ActiveEffectSetByCallerNameMagnitudes, capacity);
            ResizeJagged(ref ActiveEffectDynamicGrantedTags, capacity);
            ResizeJagged(ref ActiveEffectDynamicAssetTags, capacity);
            return ActiveEffects;
        }

        public GASSetByCallerTagStateData[] EnsureActiveEffectSetByCallerCapacity(int effectIndex, int capacity)
        {
            return EnsureJaggedCapacity(ref ActiveEffectSetByCallerMagnitudes, effectIndex, capacity);
        }

        public GASSetByCallerNameStateData[] EnsureActiveEffectSetByCallerNameCapacity(int effectIndex, int capacity)
        {
            return EnsureJaggedCapacity(ref ActiveEffectSetByCallerNameMagnitudes, effectIndex, capacity);
        }

        public GameplayTag[] EnsureActiveEffectDynamicGrantedTagCapacity(int effectIndex, int capacity)
        {
            return EnsureJaggedCapacity(ref ActiveEffectDynamicGrantedTags, effectIndex, capacity);
        }

        public GameplayTag[] EnsureActiveEffectDynamicAssetTagCapacity(int effectIndex, int capacity)
        {
            return EnsureJaggedCapacity(ref ActiveEffectDynamicAssetTags, effectIndex, capacity);
        }

        public GASAttributeStateData[] EnsureAttributeCapacity(int capacity)
        {
            EnsureNonNegative(capacity, nameof(capacity));
            if (Attributes.Length < capacity)
            {
                Attributes = new GASAttributeStateData[capacity];
            }

            return Attributes;
        }

        public GASTagCountStateData[] EnsureLooseTagCapacity(int capacity)
        {
            EnsureNonNegative(capacity, nameof(capacity));
            if (LooseTags.Length < capacity)
            {
                LooseTags = new GASTagCountStateData[capacity];
            }

            return LooseTags;
        }

        private static T[] EnsureJaggedCapacity<T>(ref T[][] values, int index, int capacity)
        {
            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            EnsureNonNegative(capacity, nameof(capacity));
            ResizeJagged(ref values, index + 1);
            T[] entries = values[index];
            if (entries == null || entries.Length < capacity)
            {
                entries = new T[capacity];
                values[index] = entries;
            }

            return entries;
        }

        private static void ResizeJagged<T>(ref T[][] values, int capacity)
        {
            if (values.Length >= capacity)
            {
                return;
            }

            T[][] resized = new T[capacity][];
            Array.Copy(values, resized, values.Length);
            values = resized;
        }

        private static void EnsureNonNegative(int value, string parameterName)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }
    }
}
