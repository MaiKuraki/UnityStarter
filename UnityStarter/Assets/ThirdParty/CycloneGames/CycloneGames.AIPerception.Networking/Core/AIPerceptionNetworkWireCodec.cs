using System;
using System.Buffers.Binary;
using CycloneGames.Hash.Core;
using CycloneGames.Networking;

namespace CycloneGames.AIPerception.Networking
{
    public enum AIPerceptionNetworkWireCodecResult : byte
    {
        Invalid = 0,
        Success = 1,
        DestinationTooSmall = 2,
        DestinationEntryCapacityTooSmall = 3,
        InvalidPayloadLength = 4,
        MalformedMessage = 5,
        PayloadTooLarge = 6
    }

    /// <summary>Strict allocation-free little-endian codec for the AIPerception v1 wire contract.</summary>
    public static class AIPerceptionNetworkWireCodec
    {
        public const int HandshakePayloadBytes = 26;
        public const int DetectionEntryBytes = 38;
        public const int DetectionEventPayloadBytes = 62;
        public const int DetectionSnapshotHeaderBytes = 26;
        public const int AuthorityTransferPayloadBytes = 47;
        public const int FullStateRequestPayloadBytes = 24;

        public static int GetSnapshotPayloadBytes(int entryCount)
        {
            if (entryCount < 0 || entryCount > AIPerceptionNetworkProtocol.MAX_SNAPSHOT_ENTRIES)
            {
                return 0;
            }

            return DetectionSnapshotHeaderBytes + (entryCount * DetectionEntryBytes);
        }

        public static int GetMaxSnapshotEntries(int payloadBudgetBytes)
        {
            if (payloadBudgetBytes < DetectionSnapshotHeaderBytes)
            {
                return 0;
            }

            int count = (payloadBudgetBytes - DetectionSnapshotHeaderBytes) / DetectionEntryBytes;
            return Math.Min(count, AIPerceptionNetworkProtocol.MAX_SNAPSHOT_ENTRIES);
        }

        public static AIPerceptionNetworkWireCodecResult TryWriteHandshake(
            in AIPerceptionManifestHandshakeMessage message,
            Span<byte> destination,
            out int bytesWritten)
        {
            bytesWritten = 0;
            if (AIPerceptionNetworkMessageValidator.Validate(in message) !=
                AIPerceptionNetworkMessageValidationResult.Valid)
            {
                return AIPerceptionNetworkWireCodecResult.MalformedMessage;
            }

            if (destination.Length < HandshakePayloadBytes)
            {
                return AIPerceptionNetworkWireCodecResult.DestinationTooSmall;
            }

            WriteUInt64(destination, 0, message.ProtocolFingerprint);
            WriteUInt64(destination, 8, message.PerceptionProfileHash);
            WriteUInt32(destination, 16, (uint)message.SupportedFeatures);
            WriteUInt32(destination, 20, (uint)message.RequiredFeatures);
            destination[24] = message.MinimumSupportedProtocolVersion;
            destination[25] = message.CurrentProtocolVersion;
            bytesWritten = HandshakePayloadBytes;
            return AIPerceptionNetworkWireCodecResult.Success;
        }

        public static AIPerceptionNetworkWireCodecResult TryReadHandshake(
            ReadOnlySpan<byte> payload,
            out AIPerceptionManifestHandshakeMessage message)
        {
            message = default;
            if (payload.Length != HandshakePayloadBytes)
            {
                return AIPerceptionNetworkWireCodecResult.InvalidPayloadLength;
            }

            var candidate = new AIPerceptionManifestHandshakeMessage(
                ReadUInt64(payload, 0),
                ReadUInt64(payload, 8),
                (AIPerceptionNetworkFeatureFlags)ReadUInt32(payload, 16),
                (AIPerceptionNetworkFeatureFlags)ReadUInt32(payload, 20),
                payload[24],
                payload[25]);
            if (AIPerceptionNetworkMessageValidator.Validate(in candidate) !=
                AIPerceptionNetworkMessageValidationResult.Valid)
            {
                return AIPerceptionNetworkWireCodecResult.MalformedMessage;
            }

            message = candidate;
            return AIPerceptionNetworkWireCodecResult.Success;
        }

        public static AIPerceptionNetworkWireCodecResult TryWriteDetectionEvent(
            in AIPerceptionDetectionEventMessage message,
            Span<byte> destination,
            out int bytesWritten)
        {
            bytesWritten = 0;
            if (AIPerceptionNetworkMessageValidator.Validate(in message) !=
                AIPerceptionNetworkMessageValidationResult.Valid)
            {
                return AIPerceptionNetworkWireCodecResult.MalformedMessage;
            }

            if (destination.Length < DetectionEventPayloadBytes)
            {
                return AIPerceptionNetworkWireCodecResult.DestinationTooSmall;
            }

            destination[0] = message.ProtocolVersion;
            WriteUInt32(destination, 1, message.ObserverNetworkId);
            WriteUInt16(destination, 5, message.Sequence);
            WriteInt32(destination, 7, message.Tick);
            destination[11] = (byte)message.EventKind;
            WriteUInt32(destination, 12, message.AuthorityGeneration);
            WriteUInt64(destination, 16, message.StateHash);
            WriteDetectionEntry(destination.Slice(24, DetectionEntryBytes), in message.Entry);
            bytesWritten = DetectionEventPayloadBytes;
            return AIPerceptionNetworkWireCodecResult.Success;
        }

        public static AIPerceptionNetworkWireCodecResult TryReadDetectionEvent(
            ReadOnlySpan<byte> payload,
            out AIPerceptionDetectionEventMessage message)
        {
            message = default;
            if (payload.Length != DetectionEventPayloadBytes)
            {
                return AIPerceptionNetworkWireCodecResult.InvalidPayloadLength;
            }

            AIPerceptionDetectionEntry entry = ReadDetectionEntry(payload.Slice(24, DetectionEntryBytes));
            var candidate = new AIPerceptionDetectionEventMessage(
                payload[0],
                ReadUInt32(payload, 1),
                ReadUInt16(payload, 5),
                ReadInt32(payload, 7),
                (AIPerceptionNetworkEventKind)payload[11],
                ReadUInt32(payload, 12),
                ReadUInt64(payload, 16),
                in entry);
            if (AIPerceptionNetworkMessageValidator.Validate(in candidate) !=
                AIPerceptionNetworkMessageValidationResult.Valid)
            {
                return AIPerceptionNetworkWireCodecResult.MalformedMessage;
            }

            message = candidate;
            return AIPerceptionNetworkWireCodecResult.Success;
        }

        public static AIPerceptionNetworkWireCodecResult TryWriteDetectionSnapshot(
            in AIPerceptionDetectionSnapshotMessage message,
            ReadOnlySpan<AIPerceptionDetectionEntry> entries,
            Span<byte> destination,
            out int bytesWritten)
        {
            bytesWritten = 0;
            if (AIPerceptionNetworkMessageValidator.Validate(in message, entries) !=
                AIPerceptionNetworkMessageValidationResult.Valid)
            {
                return AIPerceptionNetworkWireCodecResult.MalformedMessage;
            }

            int required = GetSnapshotPayloadBytes(message.EntryCount);
            if (required <= 0 || required > AIPerceptionNetworkProtocol.DEFAULT_MAX_SNAPSHOT_PAYLOAD_SIZE)
            {
                return AIPerceptionNetworkWireCodecResult.PayloadTooLarge;
            }

            if (destination.Length < required)
            {
                return AIPerceptionNetworkWireCodecResult.DestinationTooSmall;
            }

            WriteSnapshotHeader(destination, in message);
            int offset = DetectionSnapshotHeaderBytes;
            for (int i = 0; i < entries.Length; i++, offset += DetectionEntryBytes)
            {
                WriteDetectionEntry(destination.Slice(offset, DetectionEntryBytes), in entries[i]);
            }

            bytesWritten = required;
            return AIPerceptionNetworkWireCodecResult.Success;
        }

        public static AIPerceptionNetworkWireCodecResult TryReadDetectionSnapshot(
            ReadOnlySpan<byte> payload,
            Span<AIPerceptionDetectionEntry> entries,
            out AIPerceptionDetectionSnapshotMessage message,
            out int entryCount)
        {
            message = default;
            entryCount = 0;
            if (payload.Length < DetectionSnapshotHeaderBytes ||
                payload.Length > AIPerceptionNetworkProtocol.DEFAULT_MAX_SNAPSHOT_PAYLOAD_SIZE)
            {
                return AIPerceptionNetworkWireCodecResult.InvalidPayloadLength;
            }

            AIPerceptionDetectionSnapshotMessage candidate = ReadSnapshotHeader(payload);
            if (AIPerceptionNetworkMessageValidator.ValidateHeader(in candidate) !=
                AIPerceptionNetworkMessageValidationResult.Valid)
            {
                return AIPerceptionNetworkWireCodecResult.MalformedMessage;
            }

            int expected = GetSnapshotPayloadBytes(candidate.EntryCount);
            if (expected <= 0 || payload.Length != expected)
            {
                return AIPerceptionNetworkWireCodecResult.InvalidPayloadLength;
            }

            if (entries.Length < candidate.EntryCount)
            {
                return AIPerceptionNetworkWireCodecResult.DestinationEntryCapacityTooSmall;
            }

            ulong stateHash = Fnv1a64.OffsetBasis;
            AIPerceptionDetectionEntry previous = default;
            int offset = DetectionSnapshotHeaderBytes;
            for (int i = 0; i < candidate.EntryCount; i++, offset += DetectionEntryBytes)
            {
                AIPerceptionDetectionEntry entry = ReadDetectionEntry(
                    payload.Slice(offset, DetectionEntryBytes));
                if (AIPerceptionNetworkMessageValidator.Validate(in entry) !=
                    AIPerceptionNetworkMessageValidationResult.Valid ||
                    entry.DetectionTick > candidate.Tick ||
                    (candidate.SensorKind != AIPerceptionNetworkSensorKind.Any &&
                     entry.SensorKind != candidate.SensorKind) ||
                    (i > 0 && AIPerceptionNetworkHash.CompareCanonical(in previous, in entry) >= 0))
                {
                    return AIPerceptionNetworkWireCodecResult.MalformedMessage;
                }

                stateHash = AIPerceptionNetworkHash.Append(stateHash, in entry);
                previous = entry;
            }

            if (stateHash == 0UL)
            {
                stateHash = Fnv1a64.OffsetBasis;
            }

            if (stateHash != candidate.StateHash)
            {
                return AIPerceptionNetworkWireCodecResult.MalformedMessage;
            }

            offset = DetectionSnapshotHeaderBytes;
            for (int i = 0; i < candidate.EntryCount; i++, offset += DetectionEntryBytes)
            {
                entries[i] = ReadDetectionEntry(payload.Slice(offset, DetectionEntryBytes));
            }

            message = candidate;
            entryCount = candidate.EntryCount;
            return AIPerceptionNetworkWireCodecResult.Success;
        }

        public static AIPerceptionNetworkWireCodecResult TryWriteAuthorityTransfer(
            in AIPerceptionAuthorityTransferMessage message,
            Span<byte> destination,
            out int bytesWritten)
        {
            bytesWritten = 0;
            if (AIPerceptionNetworkMessageValidator.Validate(in message) !=
                AIPerceptionNetworkMessageValidationResult.Valid)
            {
                return AIPerceptionNetworkWireCodecResult.MalformedMessage;
            }

            if (destination.Length < AuthorityTransferPayloadBytes)
            {
                return AIPerceptionNetworkWireCodecResult.DestinationTooSmall;
            }

            destination[0] = message.ProtocolVersion;
            WriteUInt32(destination, 1, message.ObserverNetworkId);
            WriteInt32(destination, 5, message.PreviousOwnerConnectionId);
            WriteInt32(destination, 9, message.NewOwnerConnectionId);
            WriteUInt64(destination, 13, message.PreviousOwnerPlayerId);
            WriteUInt64(destination, 21, message.NewOwnerPlayerId);
            WriteUInt32(destination, 29, message.AuthorityGeneration);
            WriteUInt16(destination, 33, message.SnapshotSequence);
            WriteInt32(destination, 35, message.SnapshotTick);
            WriteUInt64(destination, 39, message.SnapshotStateHash);
            bytesWritten = AuthorityTransferPayloadBytes;
            return AIPerceptionNetworkWireCodecResult.Success;
        }

        public static AIPerceptionNetworkWireCodecResult TryReadAuthorityTransfer(
            ReadOnlySpan<byte> payload,
            out AIPerceptionAuthorityTransferMessage message)
        {
            message = default;
            if (payload.Length != AuthorityTransferPayloadBytes)
            {
                return AIPerceptionNetworkWireCodecResult.InvalidPayloadLength;
            }

            var candidate = new AIPerceptionAuthorityTransferMessage(
                payload[0],
                ReadUInt32(payload, 1),
                ReadInt32(payload, 5),
                ReadInt32(payload, 9),
                ReadUInt64(payload, 13),
                ReadUInt64(payload, 21),
                ReadUInt32(payload, 29),
                ReadUInt16(payload, 33),
                ReadInt32(payload, 35),
                ReadUInt64(payload, 39));
            if (AIPerceptionNetworkMessageValidator.Validate(in candidate) !=
                AIPerceptionNetworkMessageValidationResult.Valid)
            {
                return AIPerceptionNetworkWireCodecResult.MalformedMessage;
            }

            message = candidate;
            return AIPerceptionNetworkWireCodecResult.Success;
        }

        public static AIPerceptionNetworkWireCodecResult TryWriteFullStateRequest(
            in AIPerceptionFullStateRequestMessage message,
            Span<byte> destination,
            out int bytesWritten)
        {
            bytesWritten = 0;
            if (AIPerceptionNetworkMessageValidator.Validate(in message) !=
                AIPerceptionNetworkMessageValidationResult.Valid)
            {
                return AIPerceptionNetworkWireCodecResult.MalformedMessage;
            }

            if (destination.Length < FullStateRequestPayloadBytes)
            {
                return AIPerceptionNetworkWireCodecResult.DestinationTooSmall;
            }

            destination[0] = message.ProtocolVersion;
            WriteUInt32(destination, 1, message.ObserverNetworkId);
            WriteUInt16(destination, 5, message.Sequence);
            WriteInt32(destination, 7, message.Tick);
            destination[11] = (byte)message.SensorKind;
            WriteUInt32(destination, 12, message.ExpectedAuthorityGeneration);
            WriteUInt64(destination, 16, message.LastKnownStateHash);
            bytesWritten = FullStateRequestPayloadBytes;
            return AIPerceptionNetworkWireCodecResult.Success;
        }

        public static AIPerceptionNetworkWireCodecResult TryReadFullStateRequest(
            ReadOnlySpan<byte> payload,
            out AIPerceptionFullStateRequestMessage message)
        {
            message = default;
            if (payload.Length != FullStateRequestPayloadBytes)
            {
                return AIPerceptionNetworkWireCodecResult.InvalidPayloadLength;
            }

            var candidate = new AIPerceptionFullStateRequestMessage(
                payload[0],
                ReadUInt32(payload, 1),
                ReadUInt16(payload, 5),
                ReadInt32(payload, 7),
                (AIPerceptionNetworkSensorKind)payload[11],
                ReadUInt32(payload, 12),
                ReadUInt64(payload, 16));
            if (AIPerceptionNetworkMessageValidator.Validate(in candidate) !=
                AIPerceptionNetworkMessageValidationResult.Valid)
            {
                return AIPerceptionNetworkWireCodecResult.MalformedMessage;
            }

            message = candidate;
            return AIPerceptionNetworkWireCodecResult.Success;
        }

        private static void WriteSnapshotHeader(
            Span<byte> destination,
            in AIPerceptionDetectionSnapshotMessage message)
        {
            destination[0] = message.ProtocolVersion;
            WriteUInt32(destination, 1, message.ObserverNetworkId);
            WriteUInt16(destination, 5, message.Sequence);
            WriteInt32(destination, 7, message.Tick);
            destination[11] = (byte)message.SensorKind;
            WriteUInt32(destination, 12, message.AuthorityGeneration);
            WriteUInt16(destination, 16, message.EntryCount);
            WriteUInt64(destination, 18, message.StateHash);
        }

        private static AIPerceptionDetectionSnapshotMessage ReadSnapshotHeader(ReadOnlySpan<byte> payload)
        {
            return new AIPerceptionDetectionSnapshotMessage(
                payload[0],
                ReadUInt32(payload, 1),
                ReadUInt16(payload, 5),
                ReadInt32(payload, 7),
                (AIPerceptionNetworkSensorKind)payload[11],
                ReadUInt32(payload, 12),
                ReadUInt16(payload, 16),
                ReadUInt64(payload, 18));
        }

        private static void WriteDetectionEntry(
            Span<byte> destination,
            in AIPerceptionDetectionEntry entry)
        {
            WriteUInt32(destination, 0, entry.TargetNetworkId);
            WriteInt32(destination, 4, entry.PerceptibleTypeId);
            destination[8] = (byte)entry.SensorKind;
            destination[9] = (byte)entry.Flags;
            WriteSingle(destination, 10, entry.LastKnownPosition.X);
            WriteSingle(destination, 14, entry.LastKnownPosition.Y);
            WriteSingle(destination, 18, entry.LastKnownPosition.Z);
            WriteSingle(destination, 22, entry.Distance);
            WriteSingle(destination, 26, entry.Visibility);
            WriteInt32(destination, 30, entry.DetectionTick);
            WriteInt32(destination, 34, entry.SourceSensorId);
        }

        private static AIPerceptionDetectionEntry ReadDetectionEntry(ReadOnlySpan<byte> source)
        {
            return new AIPerceptionDetectionEntry(
                ReadUInt32(source, 0),
                ReadInt32(source, 4),
                (AIPerceptionNetworkSensorKind)source[8],
                (AIPerceptionDetectionFlags)source[9],
                new NetworkVector3(
                    ReadSingle(source, 10),
                    ReadSingle(source, 14),
                    ReadSingle(source, 18)),
                ReadSingle(source, 22),
                ReadSingle(source, 26),
                ReadInt32(source, 30),
                ReadInt32(source, 34));
        }

        private static void WriteUInt16(Span<byte> destination, int offset, ushort value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(offset, 2), value);
        }

        private static void WriteUInt32(Span<byte> destination, int offset, uint value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(offset, 4), value);
        }

        private static void WriteInt32(Span<byte> destination, int offset, int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(offset, 4), value);
        }

        private static void WriteUInt64(Span<byte> destination, int offset, ulong value)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(offset, 8), value);
        }

        private static ushort ReadUInt16(ReadOnlySpan<byte> source, int offset)
        {
            return BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(offset, 2));
        }

        private static uint ReadUInt32(ReadOnlySpan<byte> source, int offset)
        {
            return BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(offset, 4));
        }

        private static int ReadInt32(ReadOnlySpan<byte> source, int offset)
        {
            return BinaryPrimitives.ReadInt32LittleEndian(source.Slice(offset, 4));
        }

        private static ulong ReadUInt64(ReadOnlySpan<byte> source, int offset)
        {
            return BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(offset, 8));
        }

        private static void WriteSingle(Span<byte> destination, int offset, float value)
        {
            WriteInt32(destination, offset, BitConverter.SingleToInt32Bits(value));
        }

        private static float ReadSingle(ReadOnlySpan<byte> source, int offset)
        {
            return BitConverter.Int32BitsToSingle(ReadInt32(source, offset));
        }
    }
}
