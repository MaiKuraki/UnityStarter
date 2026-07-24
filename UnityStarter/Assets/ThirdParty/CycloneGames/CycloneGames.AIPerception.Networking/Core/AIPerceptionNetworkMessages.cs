using System;
using CycloneGames.Networking;

namespace CycloneGames.AIPerception.Networking
{
    public enum AIPerceptionNetworkSensorKind : byte
    {
        Any = 0,
        Sight = 1,
        Hearing = 2,
        Proximity = 3,
        Custom = 255
    }

    public enum AIPerceptionNetworkEventKind : byte
    {
        Unknown = 0,
        Detected = 1,
        Updated = 2,
        Lost = 3,
        Memory = 4,
        /// <summary>Clears the single target carried by the event entry.</summary>
        Cleared = 5
    }

    [Flags]
    public enum AIPerceptionDetectionFlags : byte
    {
        None = 0,
        FromMemory = 1 << 0,
        HasLineOfSight = 1 << 1,
        TeamShared = 1 << 2,
        AuthoritySnapshot = 1 << 3
    }

    public enum AIPerceptionNetworkHandshakeResult : byte
    {
        Invalid = 0,
        Compatible = 1,
        Malformed = 2,
        FingerprintMismatch = 3,
        VersionIncompatible = 4,
        ProfileMismatch = 5,
        RemoteRequirementsUnsupported = 6,
        LocalRequirementsUnsupported = 7
    }

    public readonly struct AIPerceptionManifestHandshakeMessage : INetworkProtocolHandshakeMessage
    {
        public readonly ulong ProtocolFingerprint;
        public readonly ulong PerceptionProfileHash;
        public readonly AIPerceptionNetworkFeatureFlags SupportedFeatures;
        public readonly AIPerceptionNetworkFeatureFlags RequiredFeatures;
        public readonly byte MinimumSupportedProtocolVersion;
        public readonly byte CurrentProtocolVersion;

        public AIPerceptionManifestHandshakeMessage(
            ulong protocolFingerprint,
            ulong perceptionProfileHash,
            AIPerceptionNetworkFeatureFlags supportedFeatures,
            AIPerceptionNetworkFeatureFlags requiredFeatures,
            byte minimumSupportedProtocolVersion,
            byte currentProtocolVersion)
        {
            ProtocolFingerprint = protocolFingerprint;
            PerceptionProfileHash = perceptionProfileHash;
            SupportedFeatures = supportedFeatures;
            RequiredFeatures = requiredFeatures;
            MinimumSupportedProtocolVersion = minimumSupportedProtocolVersion;
            CurrentProtocolVersion = currentProtocolVersion;
        }

        ulong INetworkProtocolHandshakeMessage.ProtocolFingerprint => ProtocolFingerprint;
        byte INetworkProtocolHandshakeMessage.CurrentProtocolVersion => CurrentProtocolVersion;
        byte INetworkProtocolHandshakeMessage.MinimumSupportedProtocolVersion => MinimumSupportedProtocolVersion;
        ulong INetworkProtocolHandshakeMessage.DomainStateHash => PerceptionProfileHash;

        public bool IsWellFormed => AIPerceptionNetworkMessageValidator.Validate(in this) ==
                                    AIPerceptionNetworkMessageValidationResult.Valid;

        public AIPerceptionNetworkHandshakeResult Negotiate(AIPerceptionNetworkProfile localProfile)
        {
            if (localProfile == null)
            {
                throw new ArgumentNullException(nameof(localProfile));
            }

            if (!IsWellFormed)
            {
                return AIPerceptionNetworkHandshakeResult.Malformed;
            }

            NetworkHandshakeResult commonResult = NetworkProtocolHandshake.Negotiate(
                in this,
                AIPerceptionNetworkProtocol.Module);
            switch (commonResult)
            {
                case NetworkHandshakeResult.FingerprintMismatch:
                    return AIPerceptionNetworkHandshakeResult.FingerprintMismatch;
                case NetworkHandshakeResult.VersionIncompatible:
                    return AIPerceptionNetworkHandshakeResult.VersionIncompatible;
                case NetworkHandshakeResult.Malformed:
                    return AIPerceptionNetworkHandshakeResult.Malformed;
                case NetworkHandshakeResult.Compatible:
                    break;
                default:
                    return AIPerceptionNetworkHandshakeResult.Invalid;
            }

            if ((RequiredFeatures & ~localProfile.Features) != 0)
            {
                return AIPerceptionNetworkHandshakeResult.RemoteRequirementsUnsupported;
            }

            if ((localProfile.RequiredFeatures & ~SupportedFeatures) != 0)
            {
                return AIPerceptionNetworkHandshakeResult.LocalRequirementsUnsupported;
            }

            return PerceptionProfileHash == localProfile.ProfileHash
                ? AIPerceptionNetworkHandshakeResult.Compatible
                : AIPerceptionNetworkHandshakeResult.ProfileMismatch;
        }

        public bool IsCompatibleWith(AIPerceptionNetworkProfile localProfile)
        {
            return Negotiate(localProfile) == AIPerceptionNetworkHandshakeResult.Compatible;
        }

        public static AIPerceptionManifestHandshakeMessage CreateLocal(AIPerceptionNetworkProfile profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            return new AIPerceptionManifestHandshakeMessage(
                AIPerceptionNetworkProtocol.ProtocolFingerprint,
                profile.ProfileHash,
                profile.Features,
                profile.RequiredFeatures,
                AIPerceptionNetworkProtocol.MIN_SUPPORTED_PROTOCOL_VERSION,
                AIPerceptionNetworkProtocol.PROTOCOL_VERSION);
        }
    }

    public readonly struct AIPerceptionDetectionEntry
    {
        public readonly uint TargetNetworkId;
        public readonly int PerceptibleTypeId;
        public readonly AIPerceptionNetworkSensorKind SensorKind;
        public readonly AIPerceptionDetectionFlags Flags;
        public readonly NetworkVector3 LastKnownPosition;
        public readonly float Distance;
        public readonly float Visibility;
        public readonly int DetectionTick;
        public readonly int SourceSensorId;

        public AIPerceptionDetectionEntry(
            uint targetNetworkId,
            int perceptibleTypeId,
            AIPerceptionNetworkSensorKind sensorKind,
            AIPerceptionDetectionFlags flags,
            NetworkVector3 lastKnownPosition,
            float distance,
            float visibility,
            int detectionTick,
            int sourceSensorId = 0)
        {
            TargetNetworkId = targetNetworkId;
            PerceptibleTypeId = perceptibleTypeId;
            SensorKind = sensorKind;
            Flags = flags;
            LastKnownPosition = lastKnownPosition;
            Distance = distance;
            Visibility = visibility;
            DetectionTick = detectionTick;
            SourceSensorId = sourceSensorId;
        }

        public bool IsValid => AIPerceptionNetworkMessageValidator.Validate(in this) ==
                               AIPerceptionNetworkMessageValidationResult.Valid;
    }

    public readonly struct AIPerceptionDetectionEventMessage
    {
        public readonly byte ProtocolVersion;
        public readonly uint ObserverNetworkId;
        public readonly ushort Sequence;
        public readonly int Tick;
        public readonly AIPerceptionNetworkEventKind EventKind;
        public readonly uint AuthorityGeneration;
        public readonly ulong StateHash;
        public readonly AIPerceptionDetectionEntry Entry;

        public AIPerceptionDetectionEventMessage(
            byte protocolVersion,
            uint observerNetworkId,
            ushort sequence,
            int tick,
            AIPerceptionNetworkEventKind eventKind,
            uint authorityGeneration,
            ulong stateHash,
            in AIPerceptionDetectionEntry entry)
        {
            ProtocolVersion = protocolVersion;
            ObserverNetworkId = observerNetworkId;
            Sequence = sequence;
            Tick = tick;
            EventKind = eventKind;
            AuthorityGeneration = authorityGeneration;
            StateHash = stateHash;
            Entry = entry;
        }

        public bool IsValid => AIPerceptionNetworkMessageValidator.Validate(in this) ==
                               AIPerceptionNetworkMessageValidationResult.Valid;
    }

    /// <summary>
    /// Snapshot metadata. Entry storage is always caller-owned and is passed separately to the codec.
    /// </summary>
    public readonly struct AIPerceptionDetectionSnapshotMessage
    {
        public readonly byte ProtocolVersion;
        public readonly uint ObserverNetworkId;
        public readonly ushort Sequence;
        public readonly int Tick;
        public readonly AIPerceptionNetworkSensorKind SensorKind;
        public readonly uint AuthorityGeneration;
        public readonly ushort EntryCount;
        public readonly ulong StateHash;

        public AIPerceptionDetectionSnapshotMessage(
            byte protocolVersion,
            uint observerNetworkId,
            ushort sequence,
            int tick,
            AIPerceptionNetworkSensorKind sensorKind,
            uint authorityGeneration,
            ushort entryCount,
            ulong stateHash)
        {
            ProtocolVersion = protocolVersion;
            ObserverNetworkId = observerNetworkId;
            Sequence = sequence;
            Tick = tick;
            SensorKind = sensorKind;
            AuthorityGeneration = authorityGeneration;
            EntryCount = entryCount;
            StateHash = stateHash;
        }

        public bool IsHeaderValid => AIPerceptionNetworkMessageValidator.ValidateHeader(in this) ==
                                     AIPerceptionNetworkMessageValidationResult.Valid;
    }

    public readonly struct AIPerceptionAuthorityTransferMessage
    {
        public readonly byte ProtocolVersion;
        public readonly uint ObserverNetworkId;
        public readonly int PreviousOwnerConnectionId;
        public readonly int NewOwnerConnectionId;
        public readonly ulong PreviousOwnerPlayerId;
        public readonly ulong NewOwnerPlayerId;
        public readonly uint AuthorityGeneration;
        public readonly ushort SnapshotSequence;
        public readonly int SnapshotTick;
        public readonly ulong SnapshotStateHash;

        public AIPerceptionAuthorityTransferMessage(
            byte protocolVersion,
            uint observerNetworkId,
            int previousOwnerConnectionId,
            int newOwnerConnectionId,
            ulong previousOwnerPlayerId,
            ulong newOwnerPlayerId,
            uint authorityGeneration,
            ushort snapshotSequence,
            int snapshotTick,
            ulong snapshotStateHash)
        {
            ProtocolVersion = protocolVersion;
            ObserverNetworkId = observerNetworkId;
            PreviousOwnerConnectionId = previousOwnerConnectionId;
            NewOwnerConnectionId = newOwnerConnectionId;
            PreviousOwnerPlayerId = previousOwnerPlayerId;
            NewOwnerPlayerId = newOwnerPlayerId;
            AuthorityGeneration = authorityGeneration;
            SnapshotSequence = snapshotSequence;
            SnapshotTick = snapshotTick;
            SnapshotStateHash = snapshotStateHash;
        }

        public bool IsValid => AIPerceptionNetworkMessageValidator.Validate(in this) ==
                               AIPerceptionNetworkMessageValidationResult.Valid;
    }

    public readonly struct AIPerceptionFullStateRequestMessage
    {
        public readonly byte ProtocolVersion;
        public readonly uint ObserverNetworkId;
        public readonly ushort Sequence;
        public readonly int Tick;
        public readonly AIPerceptionNetworkSensorKind SensorKind;
        public readonly uint ExpectedAuthorityGeneration;
        public readonly ulong LastKnownStateHash;

        public AIPerceptionFullStateRequestMessage(
            byte protocolVersion,
            uint observerNetworkId,
            ushort sequence,
            int tick,
            AIPerceptionNetworkSensorKind sensorKind,
            uint expectedAuthorityGeneration,
            ulong lastKnownStateHash)
        {
            ProtocolVersion = protocolVersion;
            ObserverNetworkId = observerNetworkId;
            Sequence = sequence;
            Tick = tick;
            SensorKind = sensorKind;
            ExpectedAuthorityGeneration = expectedAuthorityGeneration;
            LastKnownStateHash = lastKnownStateHash;
        }

        public bool IsValid => AIPerceptionNetworkMessageValidator.Validate(in this) ==
                               AIPerceptionNetworkMessageValidationResult.Valid;
    }
}
