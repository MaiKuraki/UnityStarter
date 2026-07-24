using System;
using CycloneGames.Networking;

namespace CycloneGames.AIPerception.Networking
{
    public enum AIPerceptionNetworkMessageValidationResult : byte
    {
        Invalid = 0,
        Valid = 1,
        MalformedHandshake = 2,
        InvalidFeatureFlags = 3,
        UnsupportedProtocolVersion = 4,
        InvalidObserverId = 5,
        InvalidSequence = 6,
        InvalidTick = 7,
        InvalidAuthorityGeneration = 8,
        InvalidSensorKind = 9,
        InvalidEventKind = 10,
        InvalidDetectionFlags = 11,
        InvalidTargetId = 12,
        NonFiniteValue = 13,
        ValueOutOfRange = 14,
        NonCanonicalValue = 15,
        EntryCountOutOfRange = 16,
        EntryCountMismatch = 17,
        NonCanonicalOrder = 18,
        StateHashMismatch = 19,
        InvalidAuthorityTransfer = 20,
        UnsupportedFeature = 21
    }

    public static class AIPerceptionNetworkMessageValidator
    {
        public const AIPerceptionDetectionFlags KnownDetectionFlags =
            AIPerceptionDetectionFlags.FromMemory |
            AIPerceptionDetectionFlags.HasLineOfSight |
            AIPerceptionDetectionFlags.TeamShared |
            AIPerceptionDetectionFlags.AuthoritySnapshot;

        public static AIPerceptionNetworkMessageValidationResult Validate(
            in AIPerceptionManifestHandshakeMessage message)
        {
            if (!NetworkProtocolHandshake.IsWellFormed(in message) || message.PerceptionProfileHash == 0UL)
            {
                return AIPerceptionNetworkMessageValidationResult.MalformedHandshake;
            }

            if (!AIPerceptionNetworkProtocol.AreKnownFeatures(message.SupportedFeatures) ||
                !AIPerceptionNetworkProtocol.AreKnownFeatures(message.RequiredFeatures) ||
                (message.RequiredFeatures & ~message.SupportedFeatures) != 0)
            {
                return AIPerceptionNetworkMessageValidationResult.InvalidFeatureFlags;
            }

            return AIPerceptionNetworkMessageValidationResult.Valid;
        }

        public static AIPerceptionNetworkMessageValidationResult Validate(in AIPerceptionDetectionEntry entry)
        {
            if (entry.TargetNetworkId == 0u)
            {
                return AIPerceptionNetworkMessageValidationResult.InvalidTargetId;
            }

            if (!IsConcreteSensorKind(entry.SensorKind))
            {
                return AIPerceptionNetworkMessageValidationResult.InvalidSensorKind;
            }

            if ((entry.Flags & ~KnownDetectionFlags) != 0)
            {
                return AIPerceptionNetworkMessageValidationResult.InvalidDetectionFlags;
            }

            if (!entry.LastKnownPosition.IsFinite() || !IsFinite(entry.Distance) || !IsFinite(entry.Visibility))
            {
                return AIPerceptionNetworkMessageValidationResult.NonFiniteValue;
            }

            if (entry.PerceptibleTypeId < 0 || entry.Distance < 0f ||
                entry.Visibility < 0f || entry.Visibility > 1f ||
                entry.DetectionTick < 0 || entry.SourceSensorId < 0)
            {
                return AIPerceptionNetworkMessageValidationResult.ValueOutOfRange;
            }

            if (!IsCanonicalFloat(entry.LastKnownPosition.X) ||
                !IsCanonicalFloat(entry.LastKnownPosition.Y) ||
                !IsCanonicalFloat(entry.LastKnownPosition.Z) ||
                !IsCanonicalFloat(entry.Distance) ||
                !IsCanonicalFloat(entry.Visibility))
            {
                return AIPerceptionNetworkMessageValidationResult.NonCanonicalValue;
            }

            return AIPerceptionNetworkMessageValidationResult.Valid;
        }

        public static AIPerceptionNetworkMessageValidationResult Validate(
            in AIPerceptionDetectionEventMessage message)
        {
            AIPerceptionNetworkMessageValidationResult common = ValidateCommon(
                message.ProtocolVersion,
                message.ObserverNetworkId,
                message.Sequence,
                message.Tick,
                message.AuthorityGeneration);
            if (common != AIPerceptionNetworkMessageValidationResult.Valid)
            {
                return common;
            }

            if (!IsKnownEventKind(message.EventKind))
            {
                return AIPerceptionNetworkMessageValidationResult.InvalidEventKind;
            }

            AIPerceptionNetworkMessageValidationResult entryResult = Validate(in message.Entry);
            if (entryResult != AIPerceptionNetworkMessageValidationResult.Valid)
            {
                return entryResult;
            }

            if (message.Entry.DetectionTick > message.Tick)
            {
                return AIPerceptionNetworkMessageValidationResult.InvalidTick;
            }

            return message.StateHash == AIPerceptionNetworkHash.Compute(in message.Entry)
                ? AIPerceptionNetworkMessageValidationResult.Valid
                : AIPerceptionNetworkMessageValidationResult.StateHashMismatch;
        }

        public static AIPerceptionNetworkMessageValidationResult ValidateHeader(
            in AIPerceptionDetectionSnapshotMessage message)
        {
            AIPerceptionNetworkMessageValidationResult common = ValidateCommon(
                message.ProtocolVersion,
                message.ObserverNetworkId,
                message.Sequence,
                message.Tick,
                message.AuthorityGeneration);
            if (common != AIPerceptionNetworkMessageValidationResult.Valid)
            {
                return common;
            }

            if (!IsKnownSensorKind(message.SensorKind))
            {
                return AIPerceptionNetworkMessageValidationResult.InvalidSensorKind;
            }

            if (message.EntryCount > AIPerceptionNetworkProtocol.MAX_SNAPSHOT_ENTRIES)
            {
                return AIPerceptionNetworkMessageValidationResult.EntryCountOutOfRange;
            }

            return message.StateHash != 0UL
                ? AIPerceptionNetworkMessageValidationResult.Valid
                : AIPerceptionNetworkMessageValidationResult.StateHashMismatch;
        }

        public static AIPerceptionNetworkMessageValidationResult Validate(
            in AIPerceptionDetectionSnapshotMessage message,
            ReadOnlySpan<AIPerceptionDetectionEntry> entries)
        {
            AIPerceptionNetworkMessageValidationResult headerResult = ValidateHeader(in message);
            if (headerResult != AIPerceptionNetworkMessageValidationResult.Valid)
            {
                return headerResult;
            }

            if (entries.Length != message.EntryCount)
            {
                return AIPerceptionNetworkMessageValidationResult.EntryCountMismatch;
            }

            for (int i = 0; i < entries.Length; i++)
            {
                AIPerceptionNetworkMessageValidationResult entryResult = Validate(in entries[i]);
                if (entryResult != AIPerceptionNetworkMessageValidationResult.Valid)
                {
                    return entryResult;
                }

                if (entries[i].DetectionTick > message.Tick)
                {
                    return AIPerceptionNetworkMessageValidationResult.InvalidTick;
                }

                if (message.SensorKind != AIPerceptionNetworkSensorKind.Any &&
                    entries[i].SensorKind != message.SensorKind)
                {
                    return AIPerceptionNetworkMessageValidationResult.InvalidSensorKind;
                }

                if (i > 0 && AIPerceptionNetworkHash.CompareCanonical(in entries[i - 1], in entries[i]) >= 0)
                {
                    return AIPerceptionNetworkMessageValidationResult.NonCanonicalOrder;
                }
            }

            return message.StateHash == AIPerceptionNetworkHash.Compute(entries)
                ? AIPerceptionNetworkMessageValidationResult.Valid
                : AIPerceptionNetworkMessageValidationResult.StateHashMismatch;
        }

        public static AIPerceptionNetworkMessageValidationResult Validate(
            in AIPerceptionAuthorityTransferMessage message)
        {
            if (!AIPerceptionNetworkProtocol.IsSupportedProtocolVersion(message.ProtocolVersion))
            {
                return AIPerceptionNetworkMessageValidationResult.UnsupportedProtocolVersion;
            }

            if (message.ObserverNetworkId == 0u)
            {
                return AIPerceptionNetworkMessageValidationResult.InvalidObserverId;
            }

            if (message.PreviousOwnerConnectionId < 0 || message.NewOwnerConnectionId < 0 ||
                (message.PreviousOwnerConnectionId == message.NewOwnerConnectionId &&
                 message.PreviousOwnerPlayerId == message.NewOwnerPlayerId) ||
                message.AuthorityGeneration == 0u || message.SnapshotSequence == 0 ||
                message.SnapshotTick < 0 || message.SnapshotStateHash == 0UL)
            {
                return AIPerceptionNetworkMessageValidationResult.InvalidAuthorityTransfer;
            }

            return AIPerceptionNetworkMessageValidationResult.Valid;
        }

        public static AIPerceptionNetworkMessageValidationResult Validate(
            in AIPerceptionFullStateRequestMessage message)
        {
            AIPerceptionNetworkMessageValidationResult common = ValidateCommon(
                message.ProtocolVersion,
                message.ObserverNetworkId,
                message.Sequence,
                message.Tick,
                message.ExpectedAuthorityGeneration);
            if (common != AIPerceptionNetworkMessageValidationResult.Valid)
            {
                return common;
            }

            return IsKnownSensorKind(message.SensorKind)
                ? AIPerceptionNetworkMessageValidationResult.Valid
                : AIPerceptionNetworkMessageValidationResult.InvalidSensorKind;
        }

        public static bool IsKnownSensorKind(AIPerceptionNetworkSensorKind value)
        {
            return value == AIPerceptionNetworkSensorKind.Any || IsConcreteSensorKind(value);
        }

        public static bool IsConcreteSensorKind(AIPerceptionNetworkSensorKind value)
        {
            return value == AIPerceptionNetworkSensorKind.Sight ||
                   value == AIPerceptionNetworkSensorKind.Hearing ||
                   value == AIPerceptionNetworkSensorKind.Proximity ||
                   value == AIPerceptionNetworkSensorKind.Custom;
        }

        public static bool IsKnownEventKind(AIPerceptionNetworkEventKind value)
        {
            return value >= AIPerceptionNetworkEventKind.Detected &&
                   value <= AIPerceptionNetworkEventKind.Cleared;
        }

        public static bool IsCanonicalFloat(float value)
        {
            return value != 0f || BitConverter.SingleToInt32Bits(value) == 0;
        }

        private static AIPerceptionNetworkMessageValidationResult ValidateCommon(
            byte protocolVersion,
            uint observerNetworkId,
            ushort sequence,
            int tick,
            uint authorityGeneration)
        {
            if (!AIPerceptionNetworkProtocol.IsSupportedProtocolVersion(protocolVersion))
            {
                return AIPerceptionNetworkMessageValidationResult.UnsupportedProtocolVersion;
            }

            if (observerNetworkId == 0u)
            {
                return AIPerceptionNetworkMessageValidationResult.InvalidObserverId;
            }

            if (sequence == 0)
            {
                return AIPerceptionNetworkMessageValidationResult.InvalidSequence;
            }

            if (tick < 0)
            {
                return AIPerceptionNetworkMessageValidationResult.InvalidTick;
            }

            return authorityGeneration != 0u
                ? AIPerceptionNetworkMessageValidationResult.Valid
                : AIPerceptionNetworkMessageValidationResult.InvalidAuthorityGeneration;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
