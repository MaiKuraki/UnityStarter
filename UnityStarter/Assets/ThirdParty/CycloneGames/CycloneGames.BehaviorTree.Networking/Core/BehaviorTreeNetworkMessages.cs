using CycloneGames.Networking;

namespace CycloneGames.BehaviorTree.Networking
{
    public enum BehaviorTreeNetworkPayloadKind : byte
    {
        Unknown = 0,
        FullSnapshot = 1,
        BlackboardDelta = 2,
        HashOnly = 3
    }

    [System.Flags]
    public enum BehaviorTreeTickControlFlags : byte
    {
        None = 0,
        Play = 1 << 0,
        Stop = 1 << 1,
        WakeUp = 1 << 2,
        ForceFullSnapshot = 1 << 3
    }

    public struct BehaviorTreeManifestHandshakeMessage
    {
        public ulong ProtocolFingerprint;
        public ulong TreeTemplateHash;
        public BehaviorTreeNetworkFeatureFlags RequiredFeatures;

        public BehaviorTreeManifestHandshakeMessage(
            ulong protocolFingerprint,
            ulong treeTemplateHash,
            BehaviorTreeNetworkFeatureFlags requiredFeatures)
        {
            ProtocolFingerprint = protocolFingerprint;
            TreeTemplateHash = treeTemplateHash;
            RequiredFeatures = requiredFeatures;
        }

        public bool IsCompatibleWithLocalProtocol()
        {
            return ProtocolFingerprint == BehaviorTreeNetworkProtocol.ProtocolFingerprint;
        }

        public static BehaviorTreeManifestHandshakeMessage CreateLocal(
            ulong treeTemplateHash = 0UL,
            BehaviorTreeNetworkFeatureFlags requiredFeatures = BehaviorTreeNetworkFeatureFlags.None)
        {
            return new BehaviorTreeManifestHandshakeMessage(
                BehaviorTreeNetworkProtocol.ProtocolFingerprint,
                treeTemplateHash,
                requiredFeatures);
        }
    }

    public struct BehaviorTreeStatePayloadMessage
    {
        public uint TargetNetworkId;
        public ushort Sequence;
        public int Tick;
        public BehaviorTreeNetworkPayloadKind PayloadKind;
        public ulong TreeTemplateHash;
        public ulong BlackboardHash;
        public ulong TreeStateHash;
        public byte[] Payload;

        public BehaviorTreeStatePayloadMessage(
            uint targetNetworkId,
            ushort sequence,
            int tick,
            BehaviorTreeNetworkPayloadKind payloadKind,
            ulong treeTemplateHash,
            ulong blackboardHash,
            ulong treeStateHash,
            byte[] payload)
        {
            TargetNetworkId = targetNetworkId;
            Sequence = sequence;
            Tick = tick;
            PayloadKind = payloadKind;
            TreeTemplateHash = treeTemplateHash;
            BlackboardHash = blackboardHash;
            TreeStateHash = treeStateHash;
            Payload = payload;
        }

        public bool IsValid
        {
            get
            {
                return TargetNetworkId != 0u &&
                       PayloadKind != BehaviorTreeNetworkPayloadKind.Unknown &&
                       (PayloadKind == BehaviorTreeNetworkPayloadKind.HashOnly || (Payload != null && Payload.Length > 0));
            }
        }
    }

    public struct BehaviorTreeDesyncReportMessage
    {
        public uint TargetNetworkId;
        public ushort Sequence;
        public int LocalTick;
        public int RemoteTick;
        public ulong LocalBlackboardHash;
        public ulong RemoteBlackboardHash;
        public ulong LocalTreeStateHash;
        public ulong RemoteTreeStateHash;

        public BehaviorTreeDesyncReportMessage(
            uint targetNetworkId,
            ushort sequence,
            int localTick,
            int remoteTick,
            ulong localBlackboardHash,
            ulong remoteBlackboardHash,
            ulong localTreeStateHash,
            ulong remoteTreeStateHash)
        {
            TargetNetworkId = targetNetworkId;
            Sequence = sequence;
            LocalTick = localTick;
            RemoteTick = remoteTick;
            LocalBlackboardHash = localBlackboardHash;
            RemoteBlackboardHash = remoteBlackboardHash;
            LocalTreeStateHash = localTreeStateHash;
            RemoteTreeStateHash = remoteTreeStateHash;
        }

        public bool IsValid => TargetNetworkId != 0u;
    }

    public struct BehaviorTreeTickControlMessage
    {
        public uint TargetNetworkId;
        public ushort Sequence;
        public int Tick;
        public ushort TickInterval;
        public byte WakeUpTickBudget;
        public uint AuthorityGeneration;
        public BehaviorTreeTickControlFlags Flags;

        public BehaviorTreeTickControlMessage(
            uint targetNetworkId,
            ushort sequence,
            int tick,
            ushort tickInterval,
            byte wakeUpTickBudget,
            uint authorityGeneration,
            BehaviorTreeTickControlFlags flags)
        {
            TargetNetworkId = targetNetworkId;
            Sequence = sequence;
            Tick = tick;
            TickInterval = tickInterval;
            WakeUpTickBudget = wakeUpTickBudget;
            AuthorityGeneration = authorityGeneration;
            Flags = flags;
        }

        public bool IsValid => TargetNetworkId != 0u && Flags != BehaviorTreeTickControlFlags.None;
    }

    public struct BehaviorTreeAuthorityTransferMessage
    {
        public uint TargetNetworkId;
        public int PreviousOwnerConnectionId;
        public int NewOwnerConnectionId;
        public ulong PreviousOwnerPlayerId;
        public ulong NewOwnerPlayerId;
        public uint AuthorityGeneration;
        public ushort SnapshotSequence;
        public int SnapshotTick;
        public ulong SnapshotTreeStateHash;

        public BehaviorTreeAuthorityTransferMessage(
            uint targetNetworkId,
            int previousOwnerConnectionId,
            int newOwnerConnectionId,
            ulong previousOwnerPlayerId,
            ulong newOwnerPlayerId,
            uint authorityGeneration,
            ushort snapshotSequence,
            int snapshotTick,
            ulong snapshotTreeStateHash)
        {
            TargetNetworkId = targetNetworkId;
            PreviousOwnerConnectionId = previousOwnerConnectionId;
            NewOwnerConnectionId = newOwnerConnectionId;
            PreviousOwnerPlayerId = previousOwnerPlayerId;
            NewOwnerPlayerId = newOwnerPlayerId;
            AuthorityGeneration = authorityGeneration;
            SnapshotSequence = snapshotSequence;
            SnapshotTick = snapshotTick;
            SnapshotTreeStateHash = snapshotTreeStateHash;
        }

        public bool IsValid => TargetNetworkId != 0u && NewOwnerConnectionId >= 0;
    }
}
