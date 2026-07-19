using System;

namespace CycloneGames.GameplayAbilities.Networking
{
    public enum GASNetworkMessageValidationResult : byte
    {
        Invalid = 0,
        Valid = 1,
        UnsupportedProtocolVersion = 2,
        InvalidStreamEpoch = 3,
        InvalidSequence = 4,
        InvalidEntityId = 5,
        InvalidGrantId = 6,
        InvalidEffectId = 7,
        InvalidContentId = 8,
        InvalidTagId = 9,
        InvalidCommand = 10,
        InvalidTargetData = 11,
        InvalidStatus = 12,
        InvalidStateVersion = 13,
        InvalidBatch = 14,
        InvalidRecord = 15,
        NonFiniteNumber = 16,
        CapacityExceeded = 17,
        NonCanonicalPayload = 18
    }

    /// <summary>Allocation-free structural validation. Authentication and ownership remain endpoint policy.</summary>
    public static class GASNetworkMessageValidator
    {
        public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public static GASNetworkMessageValidationResult ValidateHeader(in GASAbilityCommand message)
        {
            GASNetworkMessageValidationResult common = ValidateCommon(
                message.ProtocolVersion,
                message.StreamEpoch,
                message.CommandSequence,
                message.Entity);
            if (common != GASNetworkMessageValidationResult.Valid)
            {
                return common;
            }

            if (!message.Grant.IsValid)
            {
                return GASNetworkMessageValidationResult.InvalidGrantId;
            }

            if (message.Kind < GASAbilityCommandKind.Activate || message.Kind > GASAbilityCommandKind.CancelTarget)
            {
                return GASNetworkMessageValidationResult.InvalidCommand;
            }

            if (message.Kind != GASAbilityCommandKind.ConfirmTarget)
            {
                return message.TargetDataKind == GASTargetDataKind.None &&
                       message.TargetCount == 0 &&
                       IsDefault(in message.SingleHit)
                    ? GASNetworkMessageValidationResult.Valid
                    : GASNetworkMessageValidationResult.InvalidTargetData;
            }

            if (message.TargetDataKind == GASTargetDataKind.ActorList)
            {
                return message.TargetCount > 0 && message.TargetCount <= GameplayAbilitiesNetworkProtocol.MaxActorTargets &&
                       IsDefault(in message.SingleHit)
                    ? GASNetworkMessageValidationResult.Valid
                    : GASNetworkMessageValidationResult.InvalidTargetData;
            }

            if (message.TargetDataKind == GASTargetDataKind.SingleHit)
            {
                return message.TargetCount == 1
                    ? Validate(in message.SingleHit)
                    : GASNetworkMessageValidationResult.InvalidTargetData;
            }

            return GASNetworkMessageValidationResult.InvalidTargetData;
        }

        public static GASNetworkMessageValidationResult Validate(
            in GASAbilityCommand message,
            ReadOnlySpan<GASNetworkEntityId> actorTargets)
        {
            GASNetworkMessageValidationResult header = ValidateHeader(in message);
            if (header != GASNetworkMessageValidationResult.Valid)
            {
                return header;
            }

            if (message.TargetDataKind != GASTargetDataKind.ActorList)
            {
                return actorTargets.Length == 0
                    ? GASNetworkMessageValidationResult.Valid
                    : GASNetworkMessageValidationResult.InvalidTargetData;
            }

            if (actorTargets.Length != message.TargetCount)
            {
                return GASNetworkMessageValidationResult.InvalidTargetData;
            }

            for (int i = 0; i < actorTargets.Length; i++)
            {
                if (!actorTargets[i].IsValid)
                {
                    return GASNetworkMessageValidationResult.InvalidTargetData;
                }

                for (int j = 0; j < i; j++)
                {
                    if (actorTargets[j] == actorTargets[i])
                    {
                        return GASNetworkMessageValidationResult.InvalidTargetData;
                    }
                }
            }

            return GASNetworkMessageValidationResult.Valid;
        }

        public static GASNetworkMessageValidationResult Validate(in GASNetworkSingleTargetHit hit)
        {
            const GASTargetHitFlags knownFlags = GASTargetHitFlags.BlockingHit | GASTargetHitFlags.HasTargetEntity;
            if ((hit.Flags & ~knownFlags) != 0)
            {
                return GASNetworkMessageValidationResult.InvalidTargetData;
            }

            bool hasTarget = (hit.Flags & GASTargetHitFlags.HasTargetEntity) != 0;
            if (hasTarget != hit.TargetEntity.IsValid)
            {
                return GASNetworkMessageValidationResult.InvalidTargetData;
            }

            if (!hit.Point.IsFinite || !hit.Normal.IsFinite || !IsFinite(hit.Distance))
            {
                return GASNetworkMessageValidationResult.NonFiniteNumber;
            }

            return hit.Distance >= 0f
                ? GASNetworkMessageValidationResult.Valid
                : GASNetworkMessageValidationResult.InvalidTargetData;
        }

        public static GASNetworkMessageValidationResult Validate(in GASCommandResult message)
        {
            GASNetworkMessageValidationResult common = ValidateCommon(
                message.ProtocolVersion,
                message.StreamEpoch,
                message.CommandSequence,
                message.Entity);
            if (common != GASNetworkMessageValidationResult.Valid)
            {
                return common;
            }

            if (!message.Grant.IsValid)
            {
                return GASNetworkMessageValidationResult.InvalidGrantId;
            }

            if (message.CommandKind < GASAbilityCommandKind.Activate || message.CommandKind > GASAbilityCommandKind.CancelTarget)
            {
                return GASNetworkMessageValidationResult.InvalidCommand;
            }

            if (message.Status < GASCommandStatus.Accepted || message.Status > GASCommandStatus.AuthorityUnavailable)
            {
                return GASNetworkMessageValidationResult.InvalidStatus;
            }

            return message.AuthoritativeStateVersion != 0UL
                ? GASNetworkMessageValidationResult.Valid
                : GASNetworkMessageValidationResult.InvalidStateVersion;
        }

        public static GASNetworkMessageValidationResult Validate(in GASStateBatchChunk message)
        {
            GASNetworkMessageValidationResult common = ValidateCommon(
                message.ProtocolVersion,
                message.StreamEpoch,
                message.BatchSequence,
                message.Entity);
            if (common != GASNetworkMessageValidationResult.Valid)
            {
                return common;
            }

            if (message.Kind != GASStateBatchKind.Snapshot && message.Kind != GASStateBatchKind.Delta)
            {
                return GASNetworkMessageValidationResult.InvalidBatch;
            }

            if (message.StateVersion == 0UL ||
                message.LastProcessedCommandSequence > GameplayAbilitiesNetworkProtocol.MaxSequence ||
                (message.Kind == GASStateBatchKind.Snapshot && message.BaseStateVersion != 0UL) ||
                (message.Kind == GASStateBatchKind.Delta &&
                 (message.BaseStateVersion == 0UL || message.StateVersion <= message.BaseStateVersion)))
            {
                return GASNetworkMessageValidationResult.InvalidStateVersion;
            }

            if (message.ChunkCount == 0 ||
                message.ChunkCount > GameplayAbilitiesNetworkProtocol.MaxChunksPerBatch ||
                message.ChunkIndex >= message.ChunkCount ||
                message.StateChecksum == 0UL)
            {
                return GASNetworkMessageValidationResult.InvalidBatch;
            }

            if (message.AbilityCount > GameplayAbilitiesNetworkProtocol.MaxRecordsPerChunk ||
                message.AttributeCount > GameplayAbilitiesNetworkProtocol.MaxRecordsPerChunk ||
                message.EffectCount > GameplayAbilitiesNetworkProtocol.MaxRecordsPerChunk ||
                message.EffectTagCount > GameplayAbilitiesNetworkProtocol.MaxRecordsPerChunk ||
                message.EffectMagnitudeCount > GameplayAbilitiesNetworkProtocol.MaxRecordsPerChunk ||
                message.LooseTagCount > GameplayAbilitiesNetworkProtocol.MaxRecordsPerChunk ||
                message.TotalRecordCount > GameplayAbilitiesNetworkProtocol.MaxRecordsPerChunk)
            {
                return GASNetworkMessageValidationResult.CapacityExceeded;
            }

            return message.Kind == GASStateBatchKind.Delta && message.TotalRecordCount == 0
                ? GASNetworkMessageValidationResult.InvalidBatch
                : GASNetworkMessageValidationResult.Valid;
        }

        public static GASNetworkMessageValidationResult Validate(in GASStateAcknowledgement message)
        {
            GASNetworkMessageValidationResult common = ValidateCommon(
                message.ProtocolVersion,
                message.StreamEpoch,
                message.BatchSequence,
                message.Entity);
            if (common != GASNetworkMessageValidationResult.Valid)
            {
                return common;
            }

            return message.AppliedStateVersion != 0UL && message.StateChecksum != 0UL
                ? GASNetworkMessageValidationResult.Valid
                : GASNetworkMessageValidationResult.InvalidStateVersion;
        }

        public static GASNetworkMessageValidationResult Validate(in GASResyncRequest message)
        {
            GASNetworkMessageValidationResult common = ValidateCommon(
                message.ProtocolVersion,
                message.StreamEpoch,
                message.RequestSequence,
                message.Entity);
            if (common != GASNetworkMessageValidationResult.Valid)
            {
                return common;
            }

            if (message.ExpectedBatchSequence == 0u ||
                message.ExpectedBatchSequence > GameplayAbilitiesNetworkProtocol.MaxSequence)
            {
                return GASNetworkMessageValidationResult.InvalidSequence;
            }

            return message.Reason >= GASResyncReason.MissingBaseline && message.Reason <= GASResyncReason.LocalStateLost
                ? GASNetworkMessageValidationResult.Valid
                : GASNetworkMessageValidationResult.InvalidStatus;
        }

        public static GASNetworkMessageValidationResult Validate(in GASCueExecuted message)
        {
            GASNetworkMessageValidationResult common = ValidateCommon(
                message.ProtocolVersion,
                message.StreamEpoch,
                message.CueSequence,
                message.Entity);
            if (common != GASNetworkMessageValidationResult.Valid)
            {
                return common;
            }

            if (!message.Cue.IsValid)
            {
                return GASNetworkMessageValidationResult.InvalidTagId;
            }

            if (message.AuthoritativeStateVersion == 0UL)
            {
                return GASNetworkMessageValidationResult.InvalidStateVersion;
            }

            if (message.SourceCommandSequence > GameplayAbilitiesNetworkProtocol.MaxSequence)
            {
                return GASNetworkMessageValidationResult.InvalidSequence;
            }

            if (message.Event < GASCueEvent.Execute || message.Event > GASCueEvent.Removed)
            {
                return GASNetworkMessageValidationResult.InvalidStatus;
            }

            const GASCueFlags knownFlags = GASCueFlags.HasLocation | GASCueFlags.HasNormal | GASCueFlags.Predicted;
            if ((message.Flags & ~knownFlags) != 0 || !IsFinite(message.Magnitude) ||
                !message.Location.IsFinite || !message.Normal.IsFinite)
            {
                return GASNetworkMessageValidationResult.NonFiniteNumber;
            }

            if ((message.Flags & GASCueFlags.Predicted) != 0 && message.SourceCommandSequence == 0u)
            {
                return GASNetworkMessageValidationResult.NonCanonicalPayload;
            }

            if ((message.Flags & GASCueFlags.HasLocation) == 0 && !IsZero(in message.Location))
            {
                return GASNetworkMessageValidationResult.NonCanonicalPayload;
            }

            if ((message.Flags & GASCueFlags.HasNormal) == 0 && !IsZero(in message.Normal))
            {
                return GASNetworkMessageValidationResult.NonCanonicalPayload;
            }

            return GASNetworkMessageValidationResult.Valid;
        }

        public static GASNetworkMessageValidationResult Validate(in GASAbilityStateRecord record)
        {
            if (!record.Grant.IsValid)
            {
                return GASNetworkMessageValidationResult.InvalidGrantId;
            }

            if (record.Operation == GASStateRecordOperation.Remove)
            {
                return !record.Definition.IsValid && !record.GrantingEffect.IsValid &&
                       record.Level == 0 && record.Flags == GASAbilityStateFlags.None
                    ? GASNetworkMessageValidationResult.Valid
                    : GASNetworkMessageValidationResult.NonCanonicalPayload;
            }

            const GASAbilityStateFlags knownFlags = GASAbilityStateFlags.Active |
                                                    GASAbilityStateFlags.InputPressed |
                                                    GASAbilityStateFlags.Predicted;
            return record.Operation == GASStateRecordOperation.Upsert &&
                   record.Definition.IsValid &&
                   record.Level > 0 &&
                   (record.Flags & ~knownFlags) == 0
                ? GASNetworkMessageValidationResult.Valid
                : GASNetworkMessageValidationResult.InvalidRecord;
        }

        public static GASNetworkMessageValidationResult Validate(in GASAttributeStateRecord record)
        {
            if (!record.Attribute.IsValid)
            {
                return GASNetworkMessageValidationResult.InvalidContentId;
            }

            if (record.Operation == GASStateRecordOperation.Remove)
            {
                return record.BaseValueRaw == 0L && record.CurrentValueRaw == 0L
                    ? GASNetworkMessageValidationResult.Valid
                    : GASNetworkMessageValidationResult.NonCanonicalPayload;
            }

            return record.Operation == GASStateRecordOperation.Upsert
                ? GASNetworkMessageValidationResult.Valid
                : GASNetworkMessageValidationResult.InvalidRecord;
        }

        public static GASNetworkMessageValidationResult Validate(in GASEffectStateRecord record)
        {
            if (!record.Effect.IsValid)
            {
                return GASNetworkMessageValidationResult.InvalidEffectId;
            }

            if (record.Operation == GASStateRecordOperation.Remove)
            {
                return !record.Definition.IsValid && !record.SourceEntity.IsValid &&
                       record.SourceStreamEpoch == 0u && !record.SourceGrant.IsValid &&
                       record.Level == 0 && record.StackCount == 0 && record.DurationRaw == 0L &&
                       record.RemainingRaw == 0L && record.PeriodRaw == 0L && record.SourceCommandSequence == 0 &&
                       record.Flags == GASEffectStateFlags.None
                    ? GASNetworkMessageValidationResult.Valid
                    : GASNetworkMessageValidationResult.NonCanonicalPayload;
            }

            const GASEffectStateFlags knownFlags = GASEffectStateFlags.Infinite |
                                                   GASEffectStateFlags.Inhibited |
                                                   GASEffectStateFlags.Predicted;
            if (record.Operation != GASStateRecordOperation.Upsert || !record.Definition.IsValid ||
                record.Level <= 0 || record.StackCount <= 0 || (record.Flags & ~knownFlags) != 0 ||
                (record.SourceGrant.IsValid &&
                 (!record.SourceEntity.IsValid || record.SourceStreamEpoch == 0u)) ||
                (!record.SourceGrant.IsValid && record.SourceStreamEpoch != 0u) ||
                record.SourceCommandSequence > GameplayAbilitiesNetworkProtocol.MaxSequence)
            {
                return GASNetworkMessageValidationResult.InvalidRecord;
            }

            return record.DurationRaw >= 0L && record.RemainingRaw >= 0L && record.PeriodRaw >= 0L
                ? GASNetworkMessageValidationResult.Valid
                : GASNetworkMessageValidationResult.InvalidRecord;
        }

        public static GASNetworkMessageValidationResult Validate(in GASEffectTagStateRecord record)
        {
            if (!record.Tag.IsValid)
            {
                return GASNetworkMessageValidationResult.InvalidTagId;
            }

            return (record.Operation == GASStateRecordOperation.Upsert || record.Operation == GASStateRecordOperation.Remove) &&
                   record.Effect.IsValid &&
                   record.Kind >= GASEffectTagKind.Granted && record.Kind <= GASEffectTagKind.Asset
                ? GASNetworkMessageValidationResult.Valid
                : GASNetworkMessageValidationResult.InvalidRecord;
        }

        public static GASNetworkMessageValidationResult Validate(in GASEffectMagnitudeStateRecord record)
        {
            if ((record.Operation != GASStateRecordOperation.Upsert && record.Operation != GASStateRecordOperation.Remove) ||
                !record.Effect.IsValid || !record.Key.IsValid)
            {
                return GASNetworkMessageValidationResult.InvalidRecord;
            }

            return record.Operation != GASStateRecordOperation.Remove || record.ValueRaw == 0L
                ? GASNetworkMessageValidationResult.Valid
                : GASNetworkMessageValidationResult.NonCanonicalPayload;
        }

        public static GASNetworkMessageValidationResult Validate(in GASLooseTagStateRecord record)
        {
            if (!record.Tag.IsValid)
            {
                return GASNetworkMessageValidationResult.InvalidTagId;
            }

            if (record.Operation == GASStateRecordOperation.Remove)
            {
                return record.Count == 0
                    ? GASNetworkMessageValidationResult.Valid
                    : GASNetworkMessageValidationResult.NonCanonicalPayload;
            }

            return record.Operation == GASStateRecordOperation.Upsert &&
                   record.Count > 0 &&
                   record.Count <= GameplayAbilitiesNetworkProtocol.MaxExplicitLooseTagCount
                ? GASNetworkMessageValidationResult.Valid
                : GASNetworkMessageValidationResult.InvalidRecord;
        }

        private static GASNetworkMessageValidationResult ValidateCommon(
            byte protocolVersion,
            uint streamEpoch,
            uint sequence,
            GASNetworkEntityId entity)
        {
            if (!GameplayAbilitiesNetworkProtocol.IsSupportedProtocolVersion(protocolVersion))
            {
                return GASNetworkMessageValidationResult.UnsupportedProtocolVersion;
            }

            if (streamEpoch == 0u)
            {
                return GASNetworkMessageValidationResult.InvalidStreamEpoch;
            }

            if (sequence == 0u || sequence > GameplayAbilitiesNetworkProtocol.MaxSequence)
            {
                return GASNetworkMessageValidationResult.InvalidSequence;
            }

            return entity.IsValid
                ? GASNetworkMessageValidationResult.Valid
                : GASNetworkMessageValidationResult.InvalidEntityId;
        }

        private static bool IsDefault(in GASNetworkSingleTargetHit hit)
        {
            return !hit.TargetEntity.IsValid &&
                   IsZero(in hit.Point) &&
                   IsZero(in hit.Normal) &&
                   hit.Distance == 0f &&
                   !hit.Surface.IsValid &&
                   hit.Flags == GASTargetHitFlags.None;
        }

        private static bool IsZero(in GASNetworkVector3 value) => value.X == 0f && value.Y == 0f && value.Z == 0f;
    }
}
