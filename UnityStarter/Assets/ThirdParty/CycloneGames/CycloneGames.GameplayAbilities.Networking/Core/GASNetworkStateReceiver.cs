using System;

namespace CycloneGames.GameplayAbilities.Networking
{
    public enum GASStateReceiveResult : byte
    {
        Invalid = 0,
        Partial = 1,
        Prepared = 2,
        Duplicate = 3,
        Busy = 4,
        WrongStream = 5,
        StaleSequence = 6,
        SequenceGap = 7,
        BaselineMismatch = 8,
        CapacityExceeded = 9,
        InvalidRecord = 10,
        ChecksumMismatch = 11,
        SequenceExhausted = 12
    }

    /// <summary>
    /// Bounded, two-phase snapshot/delta receiver for one stream epoch and one GAS entity.
    /// </summary>
    /// <remarks>
    /// Chunks are staged without touching visible state. A complete batch becomes
    /// <see cref="PreparedState"/> only after structural, content, baseline, and checksum validation.
    /// The owner applies that state to its runtime and then calls <see cref="CommitPrepared"/>. If
    /// runtime application fails, the owner calls <see cref="RejectPrepared"/> and quarantines or
    /// reconstructs the affected runtime object before requesting a snapshot.
    /// </remarks>
    public sealed class GASNetworkStateReceiver
    {
        private readonly GASNetworkContentCatalog contentCatalog;
        private GASNetworkStateBuffer current;
        private GASNetworkStateBuffer candidate;

        private readonly GASAbilityStateRecord[] stagedAbilities;
        private readonly GASAttributeStateRecord[] stagedAttributes;
        private readonly GASEffectStateRecord[] stagedEffects;
        private readonly GASEffectTagStateRecord[] stagedEffectTags;
        private readonly GASEffectMagnitudeStateRecord[] stagedEffectMagnitudes;
        private readonly GASLooseTagStateRecord[] stagedLooseTags;

        private int stagedAbilityCount;
        private int stagedAttributeCount;
        private int stagedEffectCount;
        private int stagedEffectTagCount;
        private int stagedEffectMagnitudeCount;
        private int stagedLooseTagCount;
        private bool assembling;
        private bool prepared;
        private ushort nextChunkIndex;
        private GASStateBatchChunk batchHeader;
        private GASStateBatchChunk preparedHeader;
        private uint streamEpoch;
        private uint expectedBatchSequence;
        private uint lastCommittedBatchSequence;
        private uint nextResyncRequestSequence = 1u;
        private bool allowSameVersionSnapshot;

        public GASNetworkStateReceiver(
            uint streamEpoch,
            GASNetworkEntityId entity,
            GASNetworkContentCatalog contentCatalog,
            GASNetworkStateCapacity capacity,
            uint firstBatchSequence = 1u)
        {
            if (streamEpoch == 0u)
                throw new ArgumentOutOfRangeException(nameof(streamEpoch));
            if (!entity.IsValid)
                throw new ArgumentOutOfRangeException(nameof(entity));
            if (firstBatchSequence == 0u || firstBatchSequence > GameplayAbilitiesNetworkProtocol.MaxSequence)
                throw new ArgumentOutOfRangeException(nameof(firstBatchSequence));

            this.streamEpoch = streamEpoch;
            Entity = entity;
            this.contentCatalog = contentCatalog ?? throw new ArgumentNullException(nameof(contentCatalog));
            current = new GASNetworkStateBuffer(capacity);
            candidate = new GASNetworkStateBuffer(capacity);
            expectedBatchSequence = firstBatchSequence;

            stagedAbilities = new GASAbilityStateRecord[GetOperationCapacity(capacity.Abilities)];
            stagedAttributes = new GASAttributeStateRecord[GetOperationCapacity(capacity.Attributes)];
            stagedEffects = new GASEffectStateRecord[GetOperationCapacity(capacity.Effects)];
            stagedEffectTags = new GASEffectTagStateRecord[GetOperationCapacity(capacity.EffectTags)];
            stagedEffectMagnitudes = new GASEffectMagnitudeStateRecord[GetOperationCapacity(capacity.EffectMagnitudes)];
            stagedLooseTags = new GASLooseTagStateRecord[GetOperationCapacity(capacity.LooseTags)];
        }

        public uint StreamEpoch => streamEpoch;
        public GASNetworkEntityId Entity { get; }
        public uint ExpectedBatchSequence => expectedBatchSequence;
        public bool HasPreparedState => prepared;
        public IGASNetworkStateView CurrentState => current;
        public IGASNetworkStateView PreparedState => prepared ? candidate : null;

        public GASStateReceiveResult ReceiveChunk(
            in GASStateBatchChunk header,
            ReadOnlySpan<GASAbilityStateRecord> abilities,
            ReadOnlySpan<GASAttributeStateRecord> attributes,
            ReadOnlySpan<GASEffectStateRecord> effects,
            ReadOnlySpan<GASEffectTagStateRecord> effectTags,
            ReadOnlySpan<GASEffectMagnitudeStateRecord> effectMagnitudes,
            ReadOnlySpan<GASLooseTagStateRecord> looseTags)
        {
            if (header.StreamEpoch != streamEpoch || header.Entity != Entity)
                return GASStateReceiveResult.WrongStream;
            if (prepared)
                return GASStateReceiveResult.Busy;
            if (expectedBatchSequence == 0u)
                return GASStateReceiveResult.SequenceExhausted;
            if (!header.IsValid || !CountsMatch(
                    in header,
                    abilities.Length,
                    attributes.Length,
                    effects.Length,
                    effectTags.Length,
                    effectMagnitudes.Length,
                    looseTags.Length))
            {
                ResetAssembly();
                return GASStateReceiveResult.Invalid;
            }

            if (header.BatchSequence < expectedBatchSequence)
            {
                return header.BatchSequence == lastCommittedBatchSequence &&
                       current.IsComplete &&
                       header.StateVersion == current.StateVersion &&
                       header.StateChecksum == current.StateChecksum
                    ? GASStateReceiveResult.Duplicate
                    : GASStateReceiveResult.StaleSequence;
            }

            if (header.BatchSequence > expectedBatchSequence)
            {
                return GASStateReceiveResult.SequenceGap;
            }

            if (!assembling)
            {
                if (header.ChunkIndex != 0)
                    return GASStateReceiveResult.SequenceGap;
                bool allowedSameVersionSnapshot = allowSameVersionSnapshot &&
                                                  header.Kind == GASStateBatchKind.Snapshot &&
                                                  current.IsComplete &&
                                                  header.StateVersion == current.StateVersion &&
                                                  header.LastProcessedCommandSequence == current.LastProcessedCommandSequence &&
                                                  header.StateChecksum == current.StateChecksum;
                if (current.IsComplete &&
                    (header.StateVersion < current.StateVersion ||
                     (header.StateVersion == current.StateVersion && !allowedSameVersionSnapshot) ||
                     header.LastProcessedCommandSequence < current.LastProcessedCommandSequence))
                {
                    return GASStateReceiveResult.BaselineMismatch;
                }
                if (header.Kind == GASStateBatchKind.Delta &&
                    (!current.IsComplete || current.StateVersion != header.BaseStateVersion))
                {
                    return GASStateReceiveResult.BaselineMismatch;
                }

                batchHeader = header;
                nextChunkIndex = 0;
                assembling = true;
            }
            else if (!MatchesBatch(in batchHeader, in header))
            {
                ResetAssembly();
                return GASStateReceiveResult.Invalid;
            }

            if (header.ChunkIndex != nextChunkIndex)
            {
                ResetAssembly();
                return GASStateReceiveResult.SequenceGap;
            }

            GASStateReceiveResult appendResult = AppendChunk(
                header.Kind,
                abilities,
                attributes,
                effects,
                effectTags,
                effectMagnitudes,
                looseTags);
            if (appendResult != GASStateReceiveResult.Partial)
            {
                ResetAssembly();
                return appendResult;
            }

            nextChunkIndex++;
            if (nextChunkIndex < header.ChunkCount)
                return GASStateReceiveResult.Partial;

            GASStateReceiveResult prepareResult = PrepareCandidate(in batchHeader);
            ResetAssembly();
            return prepareResult;
        }

        public GASStateAcknowledgement CommitPrepared()
        {
            if (!prepared)
                throw new InvalidOperationException("No validated GAS state is awaiting commit.");

            GASNetworkStateBuffer previous = current;
            current = candidate;
            candidate = previous;
            candidate.Clear();

            lastCommittedBatchSequence = preparedHeader.BatchSequence;
            expectedBatchSequence = expectedBatchSequence == GameplayAbilitiesNetworkProtocol.MaxSequence
                ? 0u
                : expectedBatchSequence + 1u;
            prepared = false;
            allowSameVersionSnapshot = false;
            var acknowledgement = new GASStateAcknowledgement(
                streamEpoch,
                lastCommittedBatchSequence,
                Entity,
                current.StateVersion,
                current.StateChecksum);
            preparedHeader = default;
            return acknowledgement;
        }

        public void RejectPrepared()
        {
            if (!prepared)
                return;
            candidate.Clear();
            prepared = false;
            preparedHeader = default;
            allowSameVersionSnapshot = false;
        }

        public GASResyncRequest CreateResyncRequest(GASResyncReason reason)
        {
            if (reason < GASResyncReason.MissingBaseline || reason > GASResyncReason.LocalStateLost)
                throw new ArgumentOutOfRangeException(nameof(reason));
            if (nextResyncRequestSequence == 0u || expectedBatchSequence == 0u)
                throw new InvalidOperationException("The GAS stream sequence is exhausted; rotate the stream epoch.");

            uint requestSequence = nextResyncRequestSequence;
            nextResyncRequestSequence = requestSequence == GameplayAbilitiesNetworkProtocol.MaxSequence ? 0u : requestSequence + 1u;
            allowSameVersionSnapshot = reason == GASResyncReason.LocalStateLost;
            return new GASResyncRequest(
                streamEpoch,
                requestSequence,
                Entity,
                current.StateVersion,
                expectedBatchSequence,
                current.StateChecksum,
                reason);
        }

        public void ResetStream(uint newStreamEpoch, uint firstBatchSequence = 1u)
        {
            if (newStreamEpoch == 0u || newStreamEpoch == streamEpoch)
                throw new ArgumentOutOfRangeException(nameof(newStreamEpoch));
            if (firstBatchSequence == 0u || firstBatchSequence > GameplayAbilitiesNetworkProtocol.MaxSequence)
                throw new ArgumentOutOfRangeException(nameof(firstBatchSequence));

            ResetAssembly();
            prepared = false;
            preparedHeader = default;
            candidate.Clear();
            current.Clear();
            streamEpoch = newStreamEpoch;
            expectedBatchSequence = firstBatchSequence;
            lastCommittedBatchSequence = 0u;
            nextResyncRequestSequence = 1u;
            allowSameVersionSnapshot = false;
        }

        public static bool TryGetResyncReason(
            GASStateReceiveResult result,
            out GASResyncReason reason)
        {
            switch (result)
            {
                case GASStateReceiveResult.SequenceGap:
                case GASStateReceiveResult.StaleSequence:
                    reason = GASResyncReason.SequenceGap;
                    return true;
                case GASStateReceiveResult.BaselineMismatch:
                    reason = GASResyncReason.MissingBaseline;
                    return true;
                case GASStateReceiveResult.ChecksumMismatch:
                    reason = GASResyncReason.ChecksumMismatch;
                    return true;
                case GASStateReceiveResult.Invalid:
                case GASStateReceiveResult.InvalidRecord:
                    reason = GASResyncReason.DecodeFailure;
                    return true;
                case GASStateReceiveResult.CapacityExceeded:
                    reason = GASResyncReason.ApplyFailure;
                    return true;
                default:
                    reason = default;
                    return false;
            }
        }

        private GASStateReceiveResult AppendChunk(
            GASStateBatchKind kind,
            ReadOnlySpan<GASAbilityStateRecord> abilities,
            ReadOnlySpan<GASAttributeStateRecord> attributes,
            ReadOnlySpan<GASEffectStateRecord> effects,
            ReadOnlySpan<GASEffectTagStateRecord> effectTags,
            ReadOnlySpan<GASEffectMagnitudeStateRecord> effectMagnitudes,
            ReadOnlySpan<GASLooseTagStateRecord> looseTags)
        {
            if (!HasCapacity(stagedAbilityCount, abilities.Length, stagedAbilities.Length) ||
                !HasCapacity(stagedAttributeCount, attributes.Length, stagedAttributes.Length) ||
                !HasCapacity(stagedEffectCount, effects.Length, stagedEffects.Length) ||
                !HasCapacity(stagedEffectTagCount, effectTags.Length, stagedEffectTags.Length) ||
                !HasCapacity(stagedEffectMagnitudeCount, effectMagnitudes.Length, stagedEffectMagnitudes.Length) ||
                !HasCapacity(stagedLooseTagCount, looseTags.Length, stagedLooseTags.Length))
            {
                return GASStateReceiveResult.CapacityExceeded;
            }

            for (int i = 0; i < abilities.Length; i++)
            {
                if (!ValidateOperation(kind, abilities[i].Operation) ||
                    GASNetworkMessageValidator.Validate(in abilities[i]) != GASNetworkMessageValidationResult.Valid ||
                    !ValidateContent(abilities[i].Operation, abilities[i].Definition, GASNetworkContentKind.AbilityDefinition))
                    return GASStateReceiveResult.InvalidRecord;
                stagedAbilities[stagedAbilityCount++] = abilities[i];
            }

            for (int i = 0; i < attributes.Length; i++)
            {
                if (!ValidateOperation(kind, attributes[i].Operation) ||
                    GASNetworkMessageValidator.Validate(in attributes[i]) != GASNetworkMessageValidationResult.Valid ||
                    !ValidateContentId(attributes[i].Attribute, GASNetworkContentKind.Attribute))
                    return GASStateReceiveResult.InvalidRecord;
                stagedAttributes[stagedAttributeCount++] = attributes[i];
            }

            for (int i = 0; i < effects.Length; i++)
            {
                if (!ValidateOperation(kind, effects[i].Operation) ||
                    GASNetworkMessageValidator.Validate(in effects[i]) != GASNetworkMessageValidationResult.Valid ||
                    !ValidateContent(effects[i].Operation, effects[i].Definition, GASNetworkContentKind.EffectDefinition))
                    return GASStateReceiveResult.InvalidRecord;
                stagedEffects[stagedEffectCount++] = effects[i];
            }

            for (int i = 0; i < effectTags.Length; i++)
            {
                if (!ValidateOperation(kind, effectTags[i].Operation) ||
                    GASNetworkMessageValidator.Validate(in effectTags[i]) != GASNetworkMessageValidationResult.Valid)
                    return GASStateReceiveResult.InvalidRecord;
                stagedEffectTags[stagedEffectTagCount++] = effectTags[i];
            }

            for (int i = 0; i < effectMagnitudes.Length; i++)
            {
                if (!ValidateOperation(kind, effectMagnitudes[i].Operation) ||
                    GASNetworkMessageValidator.Validate(in effectMagnitudes[i]) != GASNetworkMessageValidationResult.Valid ||
                    !ValidateMagnitudeKey(in effectMagnitudes[i]))
                    return GASStateReceiveResult.InvalidRecord;
                stagedEffectMagnitudes[stagedEffectMagnitudeCount++] = effectMagnitudes[i];
            }

            for (int i = 0; i < looseTags.Length; i++)
            {
                if (!ValidateOperation(kind, looseTags[i].Operation) ||
                    GASNetworkMessageValidator.Validate(in looseTags[i]) != GASNetworkMessageValidationResult.Valid)
                    return GASStateReceiveResult.InvalidRecord;
                stagedLooseTags[stagedLooseTagCount++] = looseTags[i];
            }

            return GASStateReceiveResult.Partial;
        }

        private GASStateReceiveResult PrepareCandidate(in GASStateBatchChunk header)
        {
            if (header.Kind == GASStateBatchKind.Snapshot)
                candidate.Clear();
            else if (!candidate.TryCopyFrom(current))
                return GASStateReceiveResult.CapacityExceeded;

            candidate.SetPendingMetadata(in header);
            if (!ApplyStagedRecords())
            {
                candidate.Clear();
                return GASStateReceiveResult.CapacityExceeded;
            }

            if (!candidate.TryCompleteWrite())
            {
                candidate.Clear();
                return GASStateReceiveResult.InvalidRecord;
            }

            if (candidate.StateChecksum != header.StateChecksum)
            {
                candidate.Clear();
                return GASStateReceiveResult.ChecksumMismatch;
            }

            preparedHeader = header;
            prepared = true;
            return GASStateReceiveResult.Prepared;
        }

        private bool ApplyStagedRecords()
        {
            if (!ApplyAbilities(GASStateRecordOperation.Remove) ||
                !ApplyAttributes(GASStateRecordOperation.Remove) ||
                !ApplyEffects(GASStateRecordOperation.Remove) ||
                !ApplyEffectTags(GASStateRecordOperation.Remove) ||
                !ApplyEffectMagnitudes(GASStateRecordOperation.Remove) ||
                !ApplyLooseTags(GASStateRecordOperation.Remove))
                return false;

            return ApplyAbilities(GASStateRecordOperation.Upsert) &&
                   ApplyAttributes(GASStateRecordOperation.Upsert) &&
                   ApplyEffects(GASStateRecordOperation.Upsert) &&
                   ApplyEffectTags(GASStateRecordOperation.Upsert) &&
                   ApplyEffectMagnitudes(GASStateRecordOperation.Upsert) &&
                   ApplyLooseTags(GASStateRecordOperation.Upsert);
        }

        private bool ApplyAbilities(GASStateRecordOperation operation)
        {
            for (int i = 0; i < stagedAbilityCount; i++)
                if (stagedAbilities[i].Operation == operation && !candidate.TryApplyAbility(in stagedAbilities[i])) return false;
            return true;
        }

        private bool ApplyAttributes(GASStateRecordOperation operation)
        {
            for (int i = 0; i < stagedAttributeCount; i++)
                if (stagedAttributes[i].Operation == operation && !candidate.TryApplyAttribute(in stagedAttributes[i])) return false;
            return true;
        }

        private bool ApplyEffects(GASStateRecordOperation operation)
        {
            for (int i = 0; i < stagedEffectCount; i++)
                if (stagedEffects[i].Operation == operation && !candidate.TryApplyEffect(in stagedEffects[i])) return false;
            return true;
        }

        private bool ApplyEffectTags(GASStateRecordOperation operation)
        {
            for (int i = 0; i < stagedEffectTagCount; i++)
                if (stagedEffectTags[i].Operation == operation && !candidate.TryApplyEffectTag(in stagedEffectTags[i])) return false;
            return true;
        }

        private bool ApplyEffectMagnitudes(GASStateRecordOperation operation)
        {
            for (int i = 0; i < stagedEffectMagnitudeCount; i++)
                if (stagedEffectMagnitudes[i].Operation == operation && !candidate.TryApplyEffectMagnitude(in stagedEffectMagnitudes[i])) return false;
            return true;
        }

        private bool ApplyLooseTags(GASStateRecordOperation operation)
        {
            for (int i = 0; i < stagedLooseTagCount; i++)
                if (stagedLooseTags[i].Operation == operation && !candidate.TryApplyLooseTag(in stagedLooseTags[i])) return false;
            return true;
        }

        private bool ValidateContent(
            GASStateRecordOperation operation,
            GASNetworkContentId id,
            GASNetworkContentKind kind)
        {
            if (operation == GASStateRecordOperation.Remove)
                return true;
            return contentCatalog.TryGetEntry(id, out GASNetworkContentEntry entry) && entry.Kind == kind;
        }

        private bool ValidateContentId(GASNetworkContentId id, GASNetworkContentKind kind)
        {
            return contentCatalog.TryGetEntry(id, out GASNetworkContentEntry entry) && entry.Kind == kind;
        }

        private bool ValidateMagnitudeKey(in GASEffectMagnitudeStateRecord record)
        {
            if (record.Key.Kind == GASEffectMagnitudeKeyKind.GameplayTag)
                return record.Key.Value != 0UL;
            if (record.Key.Kind != GASEffectMagnitudeKeyKind.Name)
                return false;
            var contentId = new GASNetworkContentId(record.Key.Value);
            return contentCatalog.TryGetEntry(contentId, out GASNetworkContentEntry entry) &&
                   entry.Kind == GASNetworkContentKind.SetByCallerName;
        }

        private void ResetAssembly()
        {
            assembling = false;
            nextChunkIndex = 0;
            stagedAbilityCount = 0;
            stagedAttributeCount = 0;
            stagedEffectCount = 0;
            stagedEffectTagCount = 0;
            stagedEffectMagnitudeCount = 0;
            stagedLooseTagCount = 0;
            batchHeader = default;
        }

        private static bool CountsMatch(
            in GASStateBatchChunk header,
            int abilities,
            int attributes,
            int effects,
            int effectTags,
            int effectMagnitudes,
            int looseTags)
        {
            return header.AbilityCount == abilities &&
                   header.AttributeCount == attributes &&
                   header.EffectCount == effects &&
                   header.EffectTagCount == effectTags &&
                   header.EffectMagnitudeCount == effectMagnitudes &&
                   header.LooseTagCount == looseTags;
        }

        private static bool MatchesBatch(in GASStateBatchChunk expected, in GASStateBatchChunk actual)
        {
            return expected.ProtocolVersion == actual.ProtocolVersion &&
                   expected.StreamEpoch == actual.StreamEpoch &&
                   expected.BatchSequence == actual.BatchSequence &&
                   expected.Entity == actual.Entity &&
                   expected.Kind == actual.Kind &&
                   expected.BaseStateVersion == actual.BaseStateVersion &&
                   expected.StateVersion == actual.StateVersion &&
                   expected.LastProcessedCommandSequence == actual.LastProcessedCommandSequence &&
                   expected.ChunkCount == actual.ChunkCount &&
                   expected.StateChecksum == actual.StateChecksum;
        }

        private static bool ValidateOperation(GASStateBatchKind kind, GASStateRecordOperation operation)
        {
            return operation == GASStateRecordOperation.Upsert ||
                   (kind == GASStateBatchKind.Delta && operation == GASStateRecordOperation.Remove);
        }

        private static bool HasCapacity(int count, int added, int capacity)
        {
            return count <= capacity - added;
        }

        private static int GetOperationCapacity(int stateCapacity)
        {
            if (stateCapacity == 0)
                return 0;
            int protocolMaximum = GameplayAbilitiesNetworkProtocol.MaxChunksPerBatch *
                                  GameplayAbilitiesNetworkProtocol.MaxRecordsPerChunk;
            long doubled = (long)stateCapacity * 2L;
            return (int)Math.Min(protocolMaximum, doubled);
        }
    }
}
