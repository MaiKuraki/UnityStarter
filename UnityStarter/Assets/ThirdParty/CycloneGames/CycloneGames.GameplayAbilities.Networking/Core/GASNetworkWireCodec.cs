using System;
using System.Buffers.Binary;
using CycloneGames.Hash.Core;

namespace CycloneGames.GameplayAbilities.Networking
{
    public enum GASNetworkWireCodecResult : byte
    {
        Invalid = 0,
        Success = 1,
        DestinationTooSmall = 2,
        InvalidPayloadLength = 3,
        MalformedMessage = 4,
        DestinationRecordCapacityTooSmall = 5,
        PayloadTooLarge = 6
    }

    /// <summary>Strict allocation-free little-endian codec for the GAS v1 wire contract.</summary>
    public static class GASNetworkWireCodec
    {
        public const int HandshakePayloadBytes = 36;
        public const int AbilityCommandHeaderBytes = 28;
        public const int SingleTargetHitPayloadBytes = 45;
        public const int MaxAbilityCommandPayloadBytes = AbilityCommandHeaderBytes +
                                                        (GameplayAbilitiesNetworkProtocol.MaxActorTargets * 8);
        public const int CommandResultPayloadBytes = 35;
        public const int StateBatchHeaderBytes = 62;
        public const int AbilityStateRecordBytes = 30;
        public const int AttributeStateRecordBytes = 25;
        public const int EffectStateRecordBytes = 74;
        public const int EffectTagStateRecordBytes = 18;
        public const int EffectMagnitudeStateRecordBytes = 26;
        public const int LooseTagStateRecordBytes = 13;
        public const int StateAcknowledgementPayloadBytes = 33;
        public const int ResyncRequestPayloadBytes = 38;
        public const int CueExecutedPayloadBytes = 83;

        public static GASNetworkWireCodecResult TryWriteHandshake(
            in GASNetworkHandshake message,
            Span<byte> destination,
            out int bytesWritten)
        {
            bytesWritten = 0;
            if (!message.IsWellFormed)
            {
                return GASNetworkWireCodecResult.MalformedMessage;
            }

            if (destination.Length < HandshakePayloadBytes)
            {
                return GASNetworkWireCodecResult.DestinationTooSmall;
            }

            WriteUInt64(destination, 0, message.ProtocolFingerprint);
            WriteUInt64(destination, 8, message.WireSchemaFingerprint);
            WriteUInt64(destination, 16, message.ContentCatalogHash);
            WriteUInt64(destination, 24, message.GameplayTagManifestHash);
            WriteUInt16(destination, 32, (ushort)message.SupportedFeatures);
            destination[34] = message.MinimumSupportedProtocolVersion;
            destination[35] = message.CurrentProtocolVersion;
            bytesWritten = HandshakePayloadBytes;
            return GASNetworkWireCodecResult.Success;
        }

        public static GASNetworkWireCodecResult TryReadHandshake(
            ReadOnlySpan<byte> payload,
            out GASNetworkHandshake message)
        {
            message = default;
            if (payload.Length != HandshakePayloadBytes)
            {
                return GASNetworkWireCodecResult.InvalidPayloadLength;
            }

            var candidate = new GASNetworkHandshake(
                ReadUInt64(payload, 0),
                ReadUInt64(payload, 8),
                ReadUInt64(payload, 16),
                ReadUInt64(payload, 24),
                (GASNetworkFeatureFlags)ReadUInt16(payload, 32),
                payload[34],
                payload[35]);
            if (!candidate.IsWellFormed)
            {
                return GASNetworkWireCodecResult.MalformedMessage;
            }

            message = candidate;
            return GASNetworkWireCodecResult.Success;
        }

        public static int GetAbilityCommandPayloadBytes(in GASAbilityCommand message)
        {
            if (!message.IsHeaderValid)
            {
                return 0;
            }

            if (message.TargetDataKind == GASTargetDataKind.ActorList)
            {
                return AbilityCommandHeaderBytes + (message.TargetCount * 8);
            }

            return message.TargetDataKind == GASTargetDataKind.SingleHit
                ? AbilityCommandHeaderBytes + SingleTargetHitPayloadBytes
                : AbilityCommandHeaderBytes;
        }

        /// <summary>
        /// Computes a non-zero fingerprint over the exact canonical command wire payload.
        /// Returns 0 when the command or caller-owned target span is invalid.
        /// </summary>
        public static ulong ComputeAbilityCommandFingerprint(
            in GASAbilityCommand message,
            ReadOnlySpan<GASNetworkEntityId> actorTargets)
        {
            Span<byte> payload = stackalloc byte[MaxAbilityCommandPayloadBytes];
            GASNetworkWireCodecResult result = TryWriteAbilityCommand(
                in message,
                actorTargets,
                payload,
                out int bytesWritten);
            return result == GASNetworkWireCodecResult.Success
                ? StableHash64.ComputeBytes(payload.Slice(0, bytesWritten))
                : 0UL;
        }

        public static GASNetworkWireCodecResult TryWriteAbilityCommand(
            in GASAbilityCommand message,
            ReadOnlySpan<GASNetworkEntityId> actorTargets,
            Span<byte> destination,
            out int bytesWritten)
        {
            bytesWritten = 0;
            if (GASNetworkMessageValidator.Validate(in message, actorTargets) != GASNetworkMessageValidationResult.Valid)
            {
                return GASNetworkWireCodecResult.MalformedMessage;
            }

            int required = GetAbilityCommandPayloadBytes(in message);
            if (required <= 0 || required > MaxAbilityCommandPayloadBytes)
            {
                return GASNetworkWireCodecResult.PayloadTooLarge;
            }

            if (destination.Length < required)
            {
                return GASNetworkWireCodecResult.DestinationTooSmall;
            }

            destination[0] = message.ProtocolVersion;
            WriteUInt32(destination, 1, message.StreamEpoch);
            WriteUInt32(destination, 5, message.CommandSequence);
            WriteUInt64(destination, 9, message.Entity.Value);
            WriteUInt64(destination, 17, message.Grant.Value);
            destination[25] = (byte)message.Kind;
            destination[26] = (byte)message.TargetDataKind;
            destination[27] = message.TargetCount;

            if (message.TargetDataKind == GASTargetDataKind.ActorList)
            {
                int offset = AbilityCommandHeaderBytes;
                for (int i = 0; i < actorTargets.Length; i++, offset += 8)
                {
                    WriteUInt64(destination, offset, actorTargets[i].Value);
                }
            }
            else if (message.TargetDataKind == GASTargetDataKind.SingleHit)
            {
                WriteSingleTargetHit(destination.Slice(AbilityCommandHeaderBytes), in message.SingleHit);
            }

            bytesWritten = required;
            return GASNetworkWireCodecResult.Success;
        }

        public static GASNetworkWireCodecResult TryReadAbilityCommand(
            ReadOnlySpan<byte> payload,
            Span<GASNetworkEntityId> actorTargets,
            out GASAbilityCommand message,
            out int actorTargetCount)
        {
            message = default;
            actorTargetCount = 0;
            if (payload.Length < AbilityCommandHeaderBytes || payload.Length > MaxAbilityCommandPayloadBytes)
            {
                return GASNetworkWireCodecResult.InvalidPayloadLength;
            }

            GASTargetDataKind targetKind = (GASTargetDataKind)payload[26];
            byte targetCount = payload[27];
            int expectedLength;
            GASNetworkSingleTargetHit hit = default;
            if (targetKind == GASTargetDataKind.ActorList)
            {
                expectedLength = AbilityCommandHeaderBytes + (targetCount * 8);
            }
            else if (targetKind == GASTargetDataKind.SingleHit)
            {
                expectedLength = AbilityCommandHeaderBytes + SingleTargetHitPayloadBytes;
                if (payload.Length == expectedLength)
                {
                    hit = ReadSingleTargetHit(payload.Slice(AbilityCommandHeaderBytes));
                }
            }
            else
            {
                expectedLength = AbilityCommandHeaderBytes;
            }

            if (payload.Length != expectedLength)
            {
                return GASNetworkWireCodecResult.InvalidPayloadLength;
            }

            var candidate = new GASAbilityCommand(
                payload[0],
                ReadUInt32(payload, 1),
                ReadUInt32(payload, 5),
                new GASNetworkEntityId(ReadUInt64(payload, 9)),
                new GASNetworkGrantId(ReadUInt64(payload, 17)),
                (GASAbilityCommandKind)payload[25],
                targetKind,
                targetCount,
                hit);
            if (!candidate.IsHeaderValid)
            {
                return GASNetworkWireCodecResult.MalformedMessage;
            }

            if (targetKind == GASTargetDataKind.ActorList)
            {
                if (actorTargets.Length < targetCount)
                {
                    return GASNetworkWireCodecResult.DestinationRecordCapacityTooSmall;
                }

                int offset = AbilityCommandHeaderBytes;
                for (int i = 0; i < targetCount; i++, offset += 8)
                {
                    var target = new GASNetworkEntityId(ReadUInt64(payload, offset));
                    if (!target.IsValid)
                    {
                        return GASNetworkWireCodecResult.MalformedMessage;
                    }

                    for (int j = 0; j < i; j++)
                    {
                        if (ReadUInt64(payload, AbilityCommandHeaderBytes + (j * 8)) == target.Value)
                        {
                            return GASNetworkWireCodecResult.MalformedMessage;
                        }
                    }
                }

                offset = AbilityCommandHeaderBytes;
                for (int i = 0; i < targetCount; i++, offset += 8)
                {
                    actorTargets[i] = new GASNetworkEntityId(ReadUInt64(payload, offset));
                }

                actorTargetCount = targetCount;
            }

            message = candidate;
            return GASNetworkWireCodecResult.Success;
        }

        public static GASNetworkWireCodecResult TryWriteCommandResult(
            in GASCommandResult message,
            Span<byte> destination,
            out int bytesWritten)
        {
            bytesWritten = 0;
            if (!message.IsValid)
            {
                return GASNetworkWireCodecResult.MalformedMessage;
            }

            if (destination.Length < CommandResultPayloadBytes)
            {
                return GASNetworkWireCodecResult.DestinationTooSmall;
            }

            destination[0] = message.ProtocolVersion;
            WriteUInt32(destination, 1, message.StreamEpoch);
            WriteUInt32(destination, 5, message.CommandSequence);
            WriteUInt64(destination, 9, message.Entity.Value);
            WriteUInt64(destination, 17, message.Grant.Value);
            destination[25] = (byte)message.CommandKind;
            destination[26] = (byte)message.Status;
            WriteUInt64(destination, 27, message.AuthoritativeStateVersion);
            bytesWritten = CommandResultPayloadBytes;
            return GASNetworkWireCodecResult.Success;
        }

        public static GASNetworkWireCodecResult TryReadCommandResult(
            ReadOnlySpan<byte> payload,
            out GASCommandResult message)
        {
            message = default;
            if (payload.Length != CommandResultPayloadBytes)
            {
                return GASNetworkWireCodecResult.InvalidPayloadLength;
            }

            var candidate = new GASCommandResult(
                payload[0],
                ReadUInt32(payload, 1),
                ReadUInt32(payload, 5),
                new GASNetworkEntityId(ReadUInt64(payload, 9)),
                new GASNetworkGrantId(ReadUInt64(payload, 17)),
                (GASAbilityCommandKind)payload[25],
                (GASCommandStatus)payload[26],
                ReadUInt64(payload, 27));
            if (!candidate.IsValid)
            {
                return GASNetworkWireCodecResult.MalformedMessage;
            }

            message = candidate;
            return GASNetworkWireCodecResult.Success;
        }

        public static int GetStateBatchPayloadBytes(in GASStateBatchChunk message)
        {
            if (!message.IsValid)
            {
                return 0;
            }

            return StateBatchHeaderBytes +
                   (message.AbilityCount * AbilityStateRecordBytes) +
                   (message.AttributeCount * AttributeStateRecordBytes) +
                   (message.EffectCount * EffectStateRecordBytes) +
                   (message.EffectTagCount * EffectTagStateRecordBytes) +
                   (message.EffectMagnitudeCount * EffectMagnitudeStateRecordBytes) +
                   (message.LooseTagCount * LooseTagStateRecordBytes);
        }

        public static GASNetworkWireCodecResult TryWriteStateBatchChunk(
            in GASStateBatchChunk message,
            ReadOnlySpan<GASAbilityStateRecord> abilities,
            ReadOnlySpan<GASAttributeStateRecord> attributes,
            ReadOnlySpan<GASEffectStateRecord> effects,
            ReadOnlySpan<GASEffectTagStateRecord> effectTags,
            ReadOnlySpan<GASEffectMagnitudeStateRecord> effectMagnitudes,
            ReadOnlySpan<GASLooseTagStateRecord> looseTags,
            Span<byte> destination,
            out int bytesWritten)
        {
            bytesWritten = 0;
            if (!message.IsValid ||
                abilities.Length != message.AbilityCount ||
                attributes.Length != message.AttributeCount ||
                effects.Length != message.EffectCount ||
                effectTags.Length != message.EffectTagCount ||
                effectMagnitudes.Length != message.EffectMagnitudeCount ||
                looseTags.Length != message.LooseTagCount)
            {
                return GASNetworkWireCodecResult.MalformedMessage;
            }

            for (int i = 0; i < abilities.Length; i++)
            {
                if (GASNetworkMessageValidator.Validate(in abilities[i]) != GASNetworkMessageValidationResult.Valid)
                    return GASNetworkWireCodecResult.MalformedMessage;
            }
            for (int i = 0; i < attributes.Length; i++)
            {
                if (GASNetworkMessageValidator.Validate(in attributes[i]) != GASNetworkMessageValidationResult.Valid)
                    return GASNetworkWireCodecResult.MalformedMessage;
            }
            for (int i = 0; i < effects.Length; i++)
            {
                if (GASNetworkMessageValidator.Validate(in effects[i]) != GASNetworkMessageValidationResult.Valid)
                    return GASNetworkWireCodecResult.MalformedMessage;
            }
            for (int i = 0; i < effectTags.Length; i++)
            {
                if (GASNetworkMessageValidator.Validate(in effectTags[i]) != GASNetworkMessageValidationResult.Valid)
                    return GASNetworkWireCodecResult.MalformedMessage;
            }
            for (int i = 0; i < effectMagnitudes.Length; i++)
            {
                if (GASNetworkMessageValidator.Validate(in effectMagnitudes[i]) != GASNetworkMessageValidationResult.Valid)
                    return GASNetworkWireCodecResult.MalformedMessage;
            }
            for (int i = 0; i < looseTags.Length; i++)
            {
                if (GASNetworkMessageValidator.Validate(in looseTags[i]) != GASNetworkMessageValidationResult.Valid)
                    return GASNetworkWireCodecResult.MalformedMessage;
            }

            int required = GetStateBatchPayloadBytes(in message);
            if (required > GameplayAbilitiesNetworkProtocol.MaxStateBatchChunkPayloadBytes)
            {
                return GASNetworkWireCodecResult.PayloadTooLarge;
            }
            if (destination.Length < required)
            {
                return GASNetworkWireCodecResult.DestinationTooSmall;
            }

            WriteStateBatchHeader(destination, in message);
            int offset = StateBatchHeaderBytes;
            for (int i = 0; i < abilities.Length; i++, offset += AbilityStateRecordBytes)
                WriteAbilityRecord(destination.Slice(offset), in abilities[i]);
            for (int i = 0; i < attributes.Length; i++, offset += AttributeStateRecordBytes)
                WriteAttributeRecord(destination.Slice(offset), in attributes[i]);
            for (int i = 0; i < effects.Length; i++, offset += EffectStateRecordBytes)
                WriteEffectRecord(destination.Slice(offset), in effects[i]);
            for (int i = 0; i < effectTags.Length; i++, offset += EffectTagStateRecordBytes)
                WriteEffectTagRecord(destination.Slice(offset), in effectTags[i]);
            for (int i = 0; i < effectMagnitudes.Length; i++, offset += EffectMagnitudeStateRecordBytes)
                WriteEffectMagnitudeRecord(destination.Slice(offset), in effectMagnitudes[i]);
            for (int i = 0; i < looseTags.Length; i++, offset += LooseTagStateRecordBytes)
                WriteLooseTagRecord(destination.Slice(offset), in looseTags[i]);

            bytesWritten = required;
            return GASNetworkWireCodecResult.Success;
        }

        public static GASNetworkWireCodecResult TryReadStateBatchChunk(
            ReadOnlySpan<byte> payload,
            Span<GASAbilityStateRecord> abilities,
            Span<GASAttributeStateRecord> attributes,
            Span<GASEffectStateRecord> effects,
            Span<GASEffectTagStateRecord> effectTags,
            Span<GASEffectMagnitudeStateRecord> effectMagnitudes,
            Span<GASLooseTagStateRecord> looseTags,
            out GASStateBatchChunk message)
        {
            message = default;
            if (payload.Length < StateBatchHeaderBytes ||
                payload.Length > GameplayAbilitiesNetworkProtocol.MaxStateBatchChunkPayloadBytes)
            {
                return GASNetworkWireCodecResult.InvalidPayloadLength;
            }

            GASStateBatchChunk candidate = ReadStateBatchHeader(payload);
            if (!candidate.IsValid)
            {
                return GASNetworkWireCodecResult.MalformedMessage;
            }

            int expected = GetStateBatchPayloadBytes(in candidate);
            if (payload.Length != expected)
            {
                return GASNetworkWireCodecResult.InvalidPayloadLength;
            }

            if (abilities.Length < candidate.AbilityCount ||
                attributes.Length < candidate.AttributeCount ||
                effects.Length < candidate.EffectCount ||
                effectTags.Length < candidate.EffectTagCount ||
                effectMagnitudes.Length < candidate.EffectMagnitudeCount ||
                looseTags.Length < candidate.LooseTagCount)
            {
                return GASNetworkWireCodecResult.DestinationRecordCapacityTooSmall;
            }

            int offset = StateBatchHeaderBytes;
            for (int i = 0; i < candidate.AbilityCount; i++, offset += AbilityStateRecordBytes)
            {
                GASAbilityStateRecord record = ReadAbilityRecord(payload.Slice(offset));
                if (GASNetworkMessageValidator.Validate(in record) != GASNetworkMessageValidationResult.Valid)
                    return GASNetworkWireCodecResult.MalformedMessage;
                abilities[i] = record;
            }
            for (int i = 0; i < candidate.AttributeCount; i++, offset += AttributeStateRecordBytes)
            {
                GASAttributeStateRecord record = ReadAttributeRecord(payload.Slice(offset));
                if (GASNetworkMessageValidator.Validate(in record) != GASNetworkMessageValidationResult.Valid)
                    return GASNetworkWireCodecResult.MalformedMessage;
                attributes[i] = record;
            }
            for (int i = 0; i < candidate.EffectCount; i++, offset += EffectStateRecordBytes)
            {
                GASEffectStateRecord record = ReadEffectRecord(payload.Slice(offset));
                if (GASNetworkMessageValidator.Validate(in record) != GASNetworkMessageValidationResult.Valid)
                    return GASNetworkWireCodecResult.MalformedMessage;
                effects[i] = record;
            }
            for (int i = 0; i < candidate.EffectTagCount; i++, offset += EffectTagStateRecordBytes)
            {
                GASEffectTagStateRecord record = ReadEffectTagRecord(payload.Slice(offset));
                if (GASNetworkMessageValidator.Validate(in record) != GASNetworkMessageValidationResult.Valid)
                    return GASNetworkWireCodecResult.MalformedMessage;
                effectTags[i] = record;
            }
            for (int i = 0; i < candidate.EffectMagnitudeCount; i++, offset += EffectMagnitudeStateRecordBytes)
            {
                GASEffectMagnitudeStateRecord record = ReadEffectMagnitudeRecord(payload.Slice(offset));
                if (GASNetworkMessageValidator.Validate(in record) != GASNetworkMessageValidationResult.Valid)
                    return GASNetworkWireCodecResult.MalformedMessage;
                effectMagnitudes[i] = record;
            }
            for (int i = 0; i < candidate.LooseTagCount; i++, offset += LooseTagStateRecordBytes)
            {
                GASLooseTagStateRecord record = ReadLooseTagRecord(payload.Slice(offset));
                if (GASNetworkMessageValidator.Validate(in record) != GASNetworkMessageValidationResult.Valid)
                    return GASNetworkWireCodecResult.MalformedMessage;
                looseTags[i] = record;
            }

            message = candidate;
            return GASNetworkWireCodecResult.Success;
        }

        public static GASNetworkWireCodecResult TryWriteStateAcknowledgement(
            in GASStateAcknowledgement message,
            Span<byte> destination,
            out int bytesWritten)
        {
            bytesWritten = 0;
            if (!message.IsValid) return GASNetworkWireCodecResult.MalformedMessage;
            if (destination.Length < StateAcknowledgementPayloadBytes) return GASNetworkWireCodecResult.DestinationTooSmall;
            destination[0] = message.ProtocolVersion;
            WriteUInt32(destination, 1, message.StreamEpoch);
            WriteUInt32(destination, 5, message.BatchSequence);
            WriteUInt64(destination, 9, message.Entity.Value);
            WriteUInt64(destination, 17, message.AppliedStateVersion);
            WriteUInt64(destination, 25, message.StateChecksum);
            bytesWritten = StateAcknowledgementPayloadBytes;
            return GASNetworkWireCodecResult.Success;
        }

        public static GASNetworkWireCodecResult TryReadStateAcknowledgement(
            ReadOnlySpan<byte> payload,
            out GASStateAcknowledgement message)
        {
            message = default;
            if (payload.Length != StateAcknowledgementPayloadBytes) return GASNetworkWireCodecResult.InvalidPayloadLength;
            var candidate = new GASStateAcknowledgement(
                payload[0], ReadUInt32(payload, 1), ReadUInt32(payload, 5),
                new GASNetworkEntityId(ReadUInt64(payload, 9)), ReadUInt64(payload, 17), ReadUInt64(payload, 25));
            if (!candidate.IsValid) return GASNetworkWireCodecResult.MalformedMessage;
            message = candidate;
            return GASNetworkWireCodecResult.Success;
        }

        public static GASNetworkWireCodecResult TryWriteResyncRequest(
            in GASResyncRequest message,
            Span<byte> destination,
            out int bytesWritten)
        {
            bytesWritten = 0;
            if (!message.IsValid) return GASNetworkWireCodecResult.MalformedMessage;
            if (destination.Length < ResyncRequestPayloadBytes) return GASNetworkWireCodecResult.DestinationTooSmall;
            destination[0] = message.ProtocolVersion;
            WriteUInt32(destination, 1, message.StreamEpoch);
            WriteUInt32(destination, 5, message.RequestSequence);
            WriteUInt64(destination, 9, message.Entity.Value);
            WriteUInt64(destination, 17, message.ObservedStateVersion);
            WriteUInt32(destination, 25, message.ExpectedBatchSequence);
            WriteUInt64(destination, 29, message.ObservedChecksum);
            destination[37] = (byte)message.Reason;
            bytesWritten = ResyncRequestPayloadBytes;
            return GASNetworkWireCodecResult.Success;
        }

        public static GASNetworkWireCodecResult TryReadResyncRequest(
            ReadOnlySpan<byte> payload,
            out GASResyncRequest message)
        {
            message = default;
            if (payload.Length != ResyncRequestPayloadBytes) return GASNetworkWireCodecResult.InvalidPayloadLength;
            var candidate = new GASResyncRequest(
                payload[0], ReadUInt32(payload, 1), ReadUInt32(payload, 5),
                new GASNetworkEntityId(ReadUInt64(payload, 9)), ReadUInt64(payload, 17),
                ReadUInt32(payload, 25), ReadUInt64(payload, 29), (GASResyncReason)payload[37]);
            if (!candidate.IsValid) return GASNetworkWireCodecResult.MalformedMessage;
            message = candidate;
            return GASNetworkWireCodecResult.Success;
        }

        public static GASNetworkWireCodecResult TryWriteCueExecuted(
            in GASCueExecuted message,
            Span<byte> destination,
            out int bytesWritten)
        {
            bytesWritten = 0;
            if (!message.IsValid) return GASNetworkWireCodecResult.MalformedMessage;
            if (destination.Length < CueExecutedPayloadBytes) return GASNetworkWireCodecResult.DestinationTooSmall;
            destination[0] = message.ProtocolVersion;
            WriteUInt32(destination, 1, message.StreamEpoch);
            WriteUInt32(destination, 5, message.CueSequence);
            WriteUInt64(destination, 9, message.Entity.Value);
            WriteUInt64(destination, 17, message.Cue.Value);
            WriteUInt64(destination, 25, message.Instigator.Value);
            WriteUInt64(destination, 33, message.SourceEffect.Value);
            WriteUInt32(destination, 41, message.SourceCommandSequence);
            WriteUInt64(destination, 45, message.AuthoritativeStateVersion);
            destination[53] = (byte)message.Event;
            destination[54] = (byte)message.Flags;
            WriteSingle(destination, 55, message.Magnitude);
            WriteVector3(destination, 59, in message.Location);
            WriteVector3(destination, 71, in message.Normal);
            bytesWritten = CueExecutedPayloadBytes;
            return GASNetworkWireCodecResult.Success;
        }

        public static GASNetworkWireCodecResult TryReadCueExecuted(
            ReadOnlySpan<byte> payload,
            out GASCueExecuted message)
        {
            message = default;
            if (payload.Length != CueExecutedPayloadBytes) return GASNetworkWireCodecResult.InvalidPayloadLength;
            var candidate = new GASCueExecuted(
                payload[0], ReadUInt32(payload, 1), ReadUInt32(payload, 5),
                new GASNetworkEntityId(ReadUInt64(payload, 9)), new GASNetworkTagId(ReadUInt64(payload, 17)),
                new GASNetworkEntityId(ReadUInt64(payload, 25)), new GASNetworkEffectId(ReadUInt64(payload, 33)),
                ReadUInt32(payload, 41), ReadUInt64(payload, 45), (GASCueEvent)payload[53],
                (GASCueFlags)payload[54], ReadSingle(payload, 55), ReadVector3(payload, 59), ReadVector3(payload, 71));
            if (!candidate.IsValid) return GASNetworkWireCodecResult.MalformedMessage;
            message = candidate;
            return GASNetworkWireCodecResult.Success;
        }

        private static void WriteStateBatchHeader(Span<byte> destination, in GASStateBatchChunk message)
        {
            destination[0] = message.ProtocolVersion;
            WriteUInt32(destination, 1, message.StreamEpoch);
            WriteUInt32(destination, 5, message.BatchSequence);
            WriteUInt64(destination, 9, message.Entity.Value);
            destination[17] = (byte)message.Kind;
            WriteUInt64(destination, 18, message.BaseStateVersion);
            WriteUInt64(destination, 26, message.StateVersion);
            WriteUInt32(destination, 34, message.LastProcessedCommandSequence);
            WriteUInt16(destination, 38, message.ChunkIndex);
            WriteUInt16(destination, 40, message.ChunkCount);
            WriteUInt16(destination, 42, message.AbilityCount);
            WriteUInt16(destination, 44, message.AttributeCount);
            WriteUInt16(destination, 46, message.EffectCount);
            WriteUInt16(destination, 48, message.EffectTagCount);
            WriteUInt16(destination, 50, message.EffectMagnitudeCount);
            WriteUInt16(destination, 52, message.LooseTagCount);
            WriteUInt64(destination, 54, message.StateChecksum);
        }

        private static GASStateBatchChunk ReadStateBatchHeader(ReadOnlySpan<byte> payload)
        {
            return new GASStateBatchChunk(
                payload[0], ReadUInt32(payload, 1), ReadUInt32(payload, 5),
                new GASNetworkEntityId(ReadUInt64(payload, 9)), (GASStateBatchKind)payload[17],
                ReadUInt64(payload, 18), ReadUInt64(payload, 26), ReadUInt32(payload, 34),
                ReadUInt16(payload, 38), ReadUInt16(payload, 40), ReadUInt16(payload, 42),
                ReadUInt16(payload, 44), ReadUInt16(payload, 46), ReadUInt16(payload, 48),
                ReadUInt16(payload, 50), ReadUInt16(payload, 52), ReadUInt64(payload, 54));
        }

        private static void WriteAbilityRecord(Span<byte> destination, in GASAbilityStateRecord record)
        {
            destination[0] = (byte)record.Operation;
            WriteUInt64(destination, 1, record.Grant.Value);
            WriteUInt64(destination, 9, record.Definition.Value);
            WriteUInt64(destination, 17, record.GrantingEffect.Value);
            WriteInt32(destination, 25, record.Level);
            destination[29] = (byte)record.Flags;
        }

        private static GASAbilityStateRecord ReadAbilityRecord(ReadOnlySpan<byte> source) =>
            new GASAbilityStateRecord((GASStateRecordOperation)source[0],
                new GASNetworkGrantId(ReadUInt64(source, 1)), new GASNetworkContentId(ReadUInt64(source, 9)),
                new GASNetworkEffectId(ReadUInt64(source, 17)),
                ReadInt32(source, 25), (GASAbilityStateFlags)source[29]);

        private static void WriteAttributeRecord(Span<byte> destination, in GASAttributeStateRecord record)
        {
            destination[0] = (byte)record.Operation;
            WriteUInt64(destination, 1, record.Attribute.Value);
            WriteInt64(destination, 9, record.BaseValueRaw);
            WriteInt64(destination, 17, record.CurrentValueRaw);
        }

        private static GASAttributeStateRecord ReadAttributeRecord(ReadOnlySpan<byte> source) =>
            new GASAttributeStateRecord((GASStateRecordOperation)source[0],
                new GASNetworkContentId(ReadUInt64(source, 1)), ReadInt64(source, 9), ReadInt64(source, 17));

        private static void WriteEffectRecord(Span<byte> destination, in GASEffectStateRecord record)
        {
            destination[0] = (byte)record.Operation;
            WriteUInt64(destination, 1, record.Effect.Value);
            WriteUInt64(destination, 9, record.Definition.Value);
            WriteUInt64(destination, 17, record.SourceEntity.Value);
            WriteUInt32(destination, 25, record.SourceStreamEpoch);
            WriteUInt64(destination, 29, record.SourceGrant.Value);
            WriteInt32(destination, 37, record.Level);
            WriteInt32(destination, 41, record.StackCount);
            WriteInt64(destination, 45, record.DurationRaw);
            WriteInt64(destination, 53, record.RemainingRaw);
            WriteInt64(destination, 61, record.PeriodRaw);
            WriteUInt32(destination, 69, record.SourceCommandSequence);
            destination[73] = (byte)record.Flags;
        }

        private static GASEffectStateRecord ReadEffectRecord(ReadOnlySpan<byte> source) =>
            new GASEffectStateRecord((GASStateRecordOperation)source[0],
                new GASNetworkEffectId(ReadUInt64(source, 1)), new GASNetworkContentId(ReadUInt64(source, 9)),
                new GASNetworkEntityId(ReadUInt64(source, 17)), ReadUInt32(source, 25),
                new GASNetworkGrantId(ReadUInt64(source, 29)), ReadInt32(source, 37), ReadInt32(source, 41),
                ReadInt64(source, 45), ReadInt64(source, 53), ReadInt64(source, 61), ReadUInt32(source, 69),
                (GASEffectStateFlags)source[73]);

        private static void WriteEffectTagRecord(Span<byte> destination, in GASEffectTagStateRecord record)
        {
            destination[0] = (byte)record.Operation;
            WriteUInt64(destination, 1, record.Effect.Value);
            WriteUInt64(destination, 9, record.Tag.Value);
            destination[17] = (byte)record.Kind;
        }

        private static GASEffectTagStateRecord ReadEffectTagRecord(ReadOnlySpan<byte> source) =>
            new GASEffectTagStateRecord((GASStateRecordOperation)source[0],
                new GASNetworkEffectId(ReadUInt64(source, 1)), new GASNetworkTagId(ReadUInt64(source, 9)),
                (GASEffectTagKind)source[17]);

        private static void WriteEffectMagnitudeRecord(Span<byte> destination, in GASEffectMagnitudeStateRecord record)
        {
            destination[0] = (byte)record.Operation;
            WriteUInt64(destination, 1, record.Effect.Value);
            destination[9] = (byte)record.Key.Kind;
            WriteUInt64(destination, 10, record.Key.Value);
            WriteInt64(destination, 18, record.ValueRaw);
        }

        private static GASEffectMagnitudeStateRecord ReadEffectMagnitudeRecord(ReadOnlySpan<byte> source) =>
            new GASEffectMagnitudeStateRecord((GASStateRecordOperation)source[0],
                new GASNetworkEffectId(ReadUInt64(source, 1)),
                GASNetworkMagnitudeKey.FromWire((GASEffectMagnitudeKeyKind)source[9], ReadUInt64(source, 10)),
                ReadInt64(source, 18));

        private static void WriteLooseTagRecord(Span<byte> destination, in GASLooseTagStateRecord record)
        {
            destination[0] = (byte)record.Operation;
            WriteUInt64(destination, 1, record.Tag.Value);
            WriteInt32(destination, 9, record.Count);
        }

        private static GASLooseTagStateRecord ReadLooseTagRecord(ReadOnlySpan<byte> source) =>
            new GASLooseTagStateRecord((GASStateRecordOperation)source[0],
                new GASNetworkTagId(ReadUInt64(source, 1)), ReadInt32(source, 9));

        private static void WriteSingleTargetHit(Span<byte> destination, in GASNetworkSingleTargetHit hit)
        {
            WriteUInt64(destination, 0, hit.TargetEntity.Value);
            WriteVector3(destination, 8, in hit.Point);
            WriteVector3(destination, 20, in hit.Normal);
            WriteSingle(destination, 32, hit.Distance);
            WriteUInt64(destination, 36, hit.Surface.Value);
            destination[44] = (byte)hit.Flags;
        }

        private static GASNetworkSingleTargetHit ReadSingleTargetHit(ReadOnlySpan<byte> source) =>
            new GASNetworkSingleTargetHit(new GASNetworkEntityId(ReadUInt64(source, 0)),
                ReadVector3(source, 8), ReadVector3(source, 20), ReadSingle(source, 32),
                new GASNetworkContentId(ReadUInt64(source, 36)), (GASTargetHitFlags)source[44]);

        private static void WriteVector3(Span<byte> destination, int offset, in GASNetworkVector3 value)
        {
            WriteSingle(destination, offset, value.X);
            WriteSingle(destination, offset + 4, value.Y);
            WriteSingle(destination, offset + 8, value.Z);
        }

        private static GASNetworkVector3 ReadVector3(ReadOnlySpan<byte> source, int offset) =>
            new GASNetworkVector3(ReadSingle(source, offset), ReadSingle(source, offset + 4), ReadSingle(source, offset + 8));

        private static void WriteUInt16(Span<byte> destination, int offset, ushort value) =>
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(offset, 2), value);
        private static void WriteUInt32(Span<byte> destination, int offset, uint value) =>
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(offset, 4), value);
        private static void WriteInt32(Span<byte> destination, int offset, int value) =>
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(offset, 4), value);
        private static void WriteUInt64(Span<byte> destination, int offset, ulong value) =>
            BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(offset, 8), value);
        private static void WriteInt64(Span<byte> destination, int offset, long value) =>
            BinaryPrimitives.WriteInt64LittleEndian(destination.Slice(offset, 8), value);
        private static ushort ReadUInt16(ReadOnlySpan<byte> source, int offset) =>
            BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(offset, 2));
        private static uint ReadUInt32(ReadOnlySpan<byte> source, int offset) =>
            BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(offset, 4));
        private static int ReadInt32(ReadOnlySpan<byte> source, int offset) =>
            BinaryPrimitives.ReadInt32LittleEndian(source.Slice(offset, 4));
        private static ulong ReadUInt64(ReadOnlySpan<byte> source, int offset) =>
            BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(offset, 8));
        private static long ReadInt64(ReadOnlySpan<byte> source, int offset) =>
            BinaryPrimitives.ReadInt64LittleEndian(source.Slice(offset, 8));
        private static void WriteSingle(Span<byte> destination, int offset, float value) =>
            WriteInt32(destination, offset, BitConverter.SingleToInt32Bits(value));
        private static float ReadSingle(ReadOnlySpan<byte> source, int offset) =>
            BitConverter.Int32BitsToSingle(ReadInt32(source, offset));
    }
}
