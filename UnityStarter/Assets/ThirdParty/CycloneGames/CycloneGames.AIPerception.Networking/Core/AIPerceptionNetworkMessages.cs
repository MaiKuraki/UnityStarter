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
        Cleared = 5
    }

    [System.Flags]
    public enum AIPerceptionDetectionFlags : byte
    {
        None = 0,
        FromMemory = 1 << 0,
        HasLineOfSight = 1 << 1,
        TeamShared = 1 << 2,
        AuthoritySnapshot = 1 << 3
    }

    public struct AIPerceptionManifestHandshakeMessage : INetworkProtocolHandshake
    {
        public ulong ProtocolFingerprint;
        public byte MinimumSupportedProtocolVersion;
        public byte CurrentProtocolVersion;
        public AIPerceptionNetworkFeatureFlags RequiredFeatures;
        public ulong PerceptionProfileHash;

        public AIPerceptionManifestHandshakeMessage(
            ulong protocolFingerprint,
            byte minimumSupportedProtocolVersion,
            byte currentProtocolVersion,
            AIPerceptionNetworkFeatureFlags requiredFeatures,
            ulong perceptionProfileHash)
        {
            ProtocolFingerprint = protocolFingerprint;
            MinimumSupportedProtocolVersion = minimumSupportedProtocolVersion;
            CurrentProtocolVersion = currentProtocolVersion;
            RequiredFeatures = requiredFeatures;
            PerceptionProfileHash = perceptionProfileHash;
        }

        ulong INetworkProtocolHandshake.ProtocolFingerprint => ProtocolFingerprint;
        byte INetworkProtocolHandshake.CurrentProtocolVersion => CurrentProtocolVersion;
        byte INetworkProtocolHandshake.MinimumSupportedProtocolVersion => MinimumSupportedProtocolVersion;
        ulong INetworkProtocolHandshake.DomainStateHash => PerceptionProfileHash;

        public bool IsCompatibleWithLocalProtocol()
        {
            return NetworkProtocolHandshake.IsCompatible(this, AIPerceptionNetworkProtocol.Module);
        }

        public static AIPerceptionManifestHandshakeMessage CreateLocal(
            AIPerceptionNetworkFeatureFlags requiredFeatures = AIPerceptionNetworkFeatureFlags.None,
            ulong perceptionProfileHash = 0UL)
        {
            return new AIPerceptionManifestHandshakeMessage(
                AIPerceptionNetworkProtocol.ProtocolFingerprint,
                AIPerceptionNetworkProtocol.MIN_SUPPORTED_PROTOCOL_VERSION,
                AIPerceptionNetworkProtocol.PROTOCOL_VERSION,
                requiredFeatures,
                perceptionProfileHash);
        }
    }

    public struct AIPerceptionDetectionEntry
    {
        public uint TargetNetworkId;
        public int PerceptibleTypeId;
        public AIPerceptionNetworkSensorKind SensorKind;
        public AIPerceptionDetectionFlags Flags;
        public NetworkVector3 LastKnownPosition;
        public float Distance;
        public float Visibility;
        public int DetectionTick;
        public int SourceSensorId;

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

        public bool IsValid => TargetNetworkId != 0u && LastKnownPosition.IsFinite();
    }

    public struct AIPerceptionDetectionEventMessage
    {
        public uint ObserverNetworkId;
        public ushort Sequence;
        public int Tick;
        public AIPerceptionNetworkEventKind EventKind;
        public byte ProtocolVersion;
        public ulong StateHash;
        public AIPerceptionDetectionEntry Entry;

        public AIPerceptionDetectionEventMessage(
            uint observerNetworkId,
            ushort sequence,
            int tick,
            AIPerceptionNetworkEventKind eventKind,
            byte protocolVersion,
            ulong stateHash,
            in AIPerceptionDetectionEntry entry)
        {
            ObserverNetworkId = observerNetworkId;
            Sequence = sequence;
            Tick = tick;
            EventKind = eventKind;
            ProtocolVersion = protocolVersion;
            StateHash = stateHash;
            Entry = entry;
        }

        public bool IsValid
        {
            get
            {
                return ObserverNetworkId != 0u &&
                       EventKind != AIPerceptionNetworkEventKind.Unknown &&
                       AIPerceptionNetworkProtocol.IsSupportedProtocolVersion(ProtocolVersion) &&
                       Entry.IsValid;
            }
        }
    }

    public struct AIPerceptionDetectionSnapshotMessage
    {
        public uint ObserverNetworkId;
        public ushort Sequence;
        public int Tick;
        public AIPerceptionNetworkSensorKind SensorKind;
        public byte ProtocolVersion;
        public ulong StateHash;
        public AIPerceptionDetectionEntry[] Entries;

        public AIPerceptionDetectionSnapshotMessage(
            uint observerNetworkId,
            ushort sequence,
            int tick,
            AIPerceptionNetworkSensorKind sensorKind,
            byte protocolVersion,
            ulong stateHash,
            AIPerceptionDetectionEntry[] entries)
        {
            ObserverNetworkId = observerNetworkId;
            Sequence = sequence;
            Tick = tick;
            SensorKind = sensorKind;
            ProtocolVersion = protocolVersion;
            StateHash = stateHash;
            Entries = entries;
        }

        public int EntryCount => Entries != null ? Entries.Length : 0;

        public bool IsValid
        {
            get
            {
                return ObserverNetworkId != 0u &&
                       AIPerceptionNetworkProtocol.IsSupportedProtocolVersion(ProtocolVersion) &&
                       Entries != null;
            }
        }
    }

    public struct AIPerceptionAuthorityTransferMessage
    {
        public uint ObserverNetworkId;
        public int PreviousOwnerConnectionId;
        public int NewOwnerConnectionId;
        public ulong PreviousOwnerPlayerId;
        public ulong NewOwnerPlayerId;
        public uint AuthorityGeneration;
        public ushort SnapshotSequence;
        public int SnapshotTick;
        public ulong SnapshotStateHash;

        public AIPerceptionAuthorityTransferMessage(
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

        public bool IsValid => ObserverNetworkId != 0u && NewOwnerConnectionId >= 0;
    }

    public struct AIPerceptionFullStateRequestMessage
    {
        public uint ObserverNetworkId;
        public ushort Sequence;
        public int Tick;
        public AIPerceptionNetworkSensorKind SensorKind;
        public byte ProtocolVersion;
        public ulong LastKnownStateHash;

        public AIPerceptionFullStateRequestMessage(
            uint observerNetworkId,
            ushort sequence,
            int tick,
            AIPerceptionNetworkSensorKind sensorKind,
            byte protocolVersion,
            ulong lastKnownStateHash)
        {
            ObserverNetworkId = observerNetworkId;
            Sequence = sequence;
            Tick = tick;
            SensorKind = sensorKind;
            ProtocolVersion = protocolVersion;
            LastKnownStateHash = lastKnownStateHash;
        }

        public bool IsValid
        {
            get
            {
                return ObserverNetworkId != 0u &&
                       AIPerceptionNetworkProtocol.IsSupportedProtocolVersion(ProtocolVersion);
            }
        }
    }
}

