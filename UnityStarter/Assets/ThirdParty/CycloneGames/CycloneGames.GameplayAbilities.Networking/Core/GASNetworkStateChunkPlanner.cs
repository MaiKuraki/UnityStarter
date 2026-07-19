using System;

namespace CycloneGames.GameplayAbilities.Networking
{
    /// <summary>Array slices for one bounded GAS state chunk.</summary>
    public readonly struct GASNetworkStateChunkPlan
    {
        internal GASNetworkStateChunkPlan(
            ushort chunkIndex,
            ushort chunkCount,
            int payloadBytes,
            int abilityStart,
            ushort abilityCount,
            int attributeStart,
            ushort attributeCount,
            int effectStart,
            ushort effectCount,
            int effectTagStart,
            ushort effectTagCount,
            int effectMagnitudeStart,
            ushort effectMagnitudeCount,
            int looseTagStart,
            ushort looseTagCount)
        {
            ChunkIndex = chunkIndex;
            ChunkCount = chunkCount;
            PayloadBytes = payloadBytes;
            AbilityStart = abilityStart;
            AbilityCount = abilityCount;
            AttributeStart = attributeStart;
            AttributeCount = attributeCount;
            EffectStart = effectStart;
            EffectCount = effectCount;
            EffectTagStart = effectTagStart;
            EffectTagCount = effectTagCount;
            EffectMagnitudeStart = effectMagnitudeStart;
            EffectMagnitudeCount = effectMagnitudeCount;
            LooseTagStart = looseTagStart;
            LooseTagCount = looseTagCount;
        }

        public ushort ChunkIndex { get; }
        public ushort ChunkCount { get; }
        public int PayloadBytes { get; }
        public int AbilityStart { get; }
        public ushort AbilityCount { get; }
        public int AttributeStart { get; }
        public ushort AttributeCount { get; }
        public int EffectStart { get; }
        public ushort EffectCount { get; }
        public int EffectTagStart { get; }
        public ushort EffectTagCount { get; }
        public int EffectMagnitudeStart { get; }
        public ushort EffectMagnitudeCount { get; }
        public int LooseTagStart { get; }
        public ushort LooseTagCount { get; }
        public int TotalRecordCount => AbilityCount + AttributeCount + EffectCount + EffectTagCount + EffectMagnitudeCount + LooseTagCount;
    }

    /// <summary>
    /// Allocation-free cursor that packs fixed-size state records into endpoint payload budgets.
    /// </summary>
    public sealed class GASNetworkStateChunkPlanner
    {
        private readonly int maxPayloadBytes;
        private readonly int abilityCount;
        private readonly int attributeCount;
        private readonly int effectCount;
        private readonly int effectTagCount;
        private readonly int effectMagnitudeCount;
        private readonly int looseTagCount;
        private int abilityIndex;
        private int attributeIndex;
        private int effectIndex;
        private int effectTagIndex;
        private int effectMagnitudeIndex;
        private int looseTagIndex;
        private ushort nextChunkIndex;

        public GASNetworkStateChunkPlanner(
            int maxPayloadBytes,
            int abilityCount,
            int attributeCount,
            int effectCount,
            int effectTagCount,
            int effectMagnitudeCount,
            int looseTagCount)
        {
            ValidateCount(abilityCount, nameof(abilityCount));
            ValidateCount(attributeCount, nameof(attributeCount));
            ValidateCount(effectCount, nameof(effectCount));
            ValidateCount(effectTagCount, nameof(effectTagCount));
            ValidateCount(effectMagnitudeCount, nameof(effectMagnitudeCount));
            ValidateCount(looseTagCount, nameof(looseTagCount));
            long total = (long)abilityCount + attributeCount + effectCount + effectTagCount + effectMagnitudeCount + looseTagCount;
            int protocolMaximum = GameplayAbilitiesNetworkProtocol.MaxChunksPerBatch *
                                  GameplayAbilitiesNetworkProtocol.MaxRecordsPerChunk;
            if (total > protocolMaximum)
                throw new ArgumentOutOfRangeException(nameof(abilityCount));

            int largestRecordBytes = GetLargestPresentRecordBytes(
                abilityCount,
                attributeCount,
                effectCount,
                effectTagCount,
                effectMagnitudeCount,
                looseTagCount);
            if (maxPayloadBytes < GASNetworkWireCodec.StateBatchHeaderBytes + largestRecordBytes ||
                maxPayloadBytes > GameplayAbilitiesNetworkProtocol.MaxStateBatchChunkPayloadBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(maxPayloadBytes));
            }

            this.maxPayloadBytes = maxPayloadBytes;
            this.abilityCount = abilityCount;
            this.attributeCount = attributeCount;
            this.effectCount = effectCount;
            this.effectTagCount = effectTagCount;
            this.effectMagnitudeCount = effectMagnitudeCount;
            this.looseTagCount = looseTagCount;

            ChunkCount = CountChunks();
            if (ChunkCount == 0 || ChunkCount > GameplayAbilitiesNetworkProtocol.MaxChunksPerBatch)
                throw new ArgumentOutOfRangeException(nameof(abilityCount), "The state requires more chunks than the protocol permits.");
        }

        public ushort ChunkCount { get; }
        public bool IsComplete => nextChunkIndex >= ChunkCount;

        public void Reset()
        {
            abilityIndex = 0;
            attributeIndex = 0;
            effectIndex = 0;
            effectTagIndex = 0;
            effectMagnitudeIndex = 0;
            looseTagIndex = 0;
            nextChunkIndex = 0;
        }

        public bool TryGetNext(out GASNetworkStateChunkPlan plan)
        {
            if (IsComplete)
            {
                plan = default;
                return false;
            }

            int startAbility = abilityIndex;
            int startAttribute = attributeIndex;
            int startEffect = effectIndex;
            int startEffectTag = effectTagIndex;
            int startEffectMagnitude = effectMagnitudeIndex;
            int startLooseTag = looseTagIndex;
            int payloadBytes = GASNetworkWireCodec.StateBatchHeaderBytes;
            int records = 0;

            Fill(ref abilityIndex, abilityCount, GASNetworkWireCodec.AbilityStateRecordBytes, ref payloadBytes, ref records);
            Fill(ref attributeIndex, attributeCount, GASNetworkWireCodec.AttributeStateRecordBytes, ref payloadBytes, ref records);
            Fill(ref effectIndex, effectCount, GASNetworkWireCodec.EffectStateRecordBytes, ref payloadBytes, ref records);
            Fill(ref effectTagIndex, effectTagCount, GASNetworkWireCodec.EffectTagStateRecordBytes, ref payloadBytes, ref records);
            Fill(ref effectMagnitudeIndex, effectMagnitudeCount, GASNetworkWireCodec.EffectMagnitudeStateRecordBytes, ref payloadBytes, ref records);
            Fill(ref looseTagIndex, looseTagCount, GASNetworkWireCodec.LooseTagStateRecordBytes, ref payloadBytes, ref records);

            if (records == 0 && TotalCount != 0)
                throw new InvalidOperationException("The endpoint payload budget cannot fit the next GAS state record.");

            plan = new GASNetworkStateChunkPlan(
                nextChunkIndex,
                ChunkCount,
                payloadBytes,
                startAbility,
                checked((ushort)(abilityIndex - startAbility)),
                startAttribute,
                checked((ushort)(attributeIndex - startAttribute)),
                startEffect,
                checked((ushort)(effectIndex - startEffect)),
                startEffectTag,
                checked((ushort)(effectTagIndex - startEffectTag)),
                startEffectMagnitude,
                checked((ushort)(effectMagnitudeIndex - startEffectMagnitude)),
                startLooseTag,
                checked((ushort)(looseTagIndex - startLooseTag)));
            nextChunkIndex++;
            return true;
        }

        private int TotalCount => abilityCount + attributeCount + effectCount + effectTagCount + effectMagnitudeCount + looseTagCount;

        private ushort CountChunks()
        {
            if (TotalCount == 0)
                return 1;

            int abilities = 0;
            int attributes = 0;
            int effects = 0;
            int effectTags = 0;
            int effectMagnitudes = 0;
            int looseTags = 0;
            int chunks = 0;
            while (abilities < abilityCount || attributes < attributeCount || effects < effectCount ||
                   effectTags < effectTagCount || effectMagnitudes < effectMagnitudeCount || looseTags < looseTagCount)
            {
                int payloadBytes = GASNetworkWireCodec.StateBatchHeaderBytes;
                int records = 0;
                Fill(ref abilities, abilityCount, GASNetworkWireCodec.AbilityStateRecordBytes, ref payloadBytes, ref records);
                Fill(ref attributes, attributeCount, GASNetworkWireCodec.AttributeStateRecordBytes, ref payloadBytes, ref records);
                Fill(ref effects, effectCount, GASNetworkWireCodec.EffectStateRecordBytes, ref payloadBytes, ref records);
                Fill(ref effectTags, effectTagCount, GASNetworkWireCodec.EffectTagStateRecordBytes, ref payloadBytes, ref records);
                Fill(ref effectMagnitudes, effectMagnitudeCount, GASNetworkWireCodec.EffectMagnitudeStateRecordBytes, ref payloadBytes, ref records);
                Fill(ref looseTags, looseTagCount, GASNetworkWireCodec.LooseTagStateRecordBytes, ref payloadBytes, ref records);
                if (records == 0)
                    throw new ArgumentOutOfRangeException(nameof(maxPayloadBytes));
                chunks++;
                if (chunks > GameplayAbilitiesNetworkProtocol.MaxChunksPerBatch)
                    return checked((ushort)chunks);
            }
            return checked((ushort)chunks);
        }

        private void Fill(
            ref int index,
            int count,
            int recordBytes,
            ref int payloadBytes,
            ref int records)
        {
            while (index < count &&
                   records < GameplayAbilitiesNetworkProtocol.MaxRecordsPerChunk &&
                   payloadBytes <= maxPayloadBytes - recordBytes)
            {
                index++;
                records++;
                payloadBytes += recordBytes;
            }
        }

        private static void ValidateCount(int value, string parameterName)
        {
            int maximum = GameplayAbilitiesNetworkProtocol.MaxChunksPerBatch *
                          GameplayAbilitiesNetworkProtocol.MaxRecordsPerChunk;
            if (value < 0 || value > maximum)
                throw new ArgumentOutOfRangeException(parameterName);
        }

        private static int GetLargestPresentRecordBytes(
            int abilities,
            int attributes,
            int effects,
            int effectTags,
            int effectMagnitudes,
            int looseTags)
        {
            int largest = 0;
            if (abilities > 0) largest = Math.Max(largest, GASNetworkWireCodec.AbilityStateRecordBytes);
            if (attributes > 0) largest = Math.Max(largest, GASNetworkWireCodec.AttributeStateRecordBytes);
            if (effects > 0) largest = Math.Max(largest, GASNetworkWireCodec.EffectStateRecordBytes);
            if (effectTags > 0) largest = Math.Max(largest, GASNetworkWireCodec.EffectTagStateRecordBytes);
            if (effectMagnitudes > 0) largest = Math.Max(largest, GASNetworkWireCodec.EffectMagnitudeStateRecordBytes);
            if (looseTags > 0) largest = Math.Max(largest, GASNetworkWireCodec.LooseTagStateRecordBytes);
            return largest;
        }
    }
}
