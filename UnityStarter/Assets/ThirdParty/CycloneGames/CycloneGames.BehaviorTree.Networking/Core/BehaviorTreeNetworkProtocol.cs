using CycloneGames.Networking;

namespace CycloneGames.BehaviorTree.Networking
{
    public static class BehaviorTreeNetworkProtocol
    {
        public const string MessageOwner = "CycloneGames.BehaviorTree";

        public const ushort MESSAGE_ID_BASE = 14000;
        public const ushort MESSAGE_ID_MAX = 14999;
        public const ushort MSG_MANIFEST_HANDSHAKE = MESSAGE_ID_BASE;
        public const ushort MSG_FULL_SNAPSHOT = MESSAGE_ID_BASE + 1;
        public const ushort MSG_BLACKBOARD_DELTA = MESSAGE_ID_BASE + 2;
        public const ushort MSG_DESYNC_REPORT = MESSAGE_ID_BASE + 3;
        public const ushort MSG_TICK_CONTROL = MESSAGE_ID_BASE + 4;
        public const ushort MSG_AUTHORITY_TRANSFER = MESSAGE_ID_BASE + 5;

        // The transport payload limit covers the complete state DTO. The bridge limits
        // the nested byte[] to the remainder after its fixed fields and length prefix.
        public const int STATE_PAYLOAD_FIXED_ENVELOPE_SIZE = 43;
        public const int DEFAULT_MAX_STATE_MESSAGE_SIZE = NetworkConstants.DefaultMaxPayloadSize;
        public const int DEFAULT_MAX_SNAPSHOT_PAYLOAD_SIZE =
            DEFAULT_MAX_STATE_MESSAGE_SIZE - STATE_PAYLOAD_FIXED_ENVELOPE_SIZE;
        public const int DEFAULT_MAX_DELTA_PAYLOAD_SIZE =
            DEFAULT_MAX_STATE_MESSAGE_SIZE - STATE_PAYLOAD_FIXED_ENVELOPE_SIZE;
        public const int DEFAULT_MAX_CONTROL_PAYLOAD_SIZE = 128;

        // Frozen FNV-1a64 identities of the versioned wire contracts.
        private const ulong MANIFEST_HANDSHAKE_SCHEMA_V1 = 0x059263302E9505CDUL;
        private const string FULL_SNAPSHOT_WIRE_SCHEMA_V2 =
            "BehaviorTreeStatePayloadMessage.FullSnapshot:v2|TargetNetworkId:u32le|Sequence:u16le|" +
            "Tick:i32le|PayloadKind:u8=1|TreeTemplateHash:u64le|BlackboardHash:u64le|" +
            "TreeStateHash:u64le|AuthorityGeneration:u32le|Payload:BTS2{Magic:u32le=0x32535442|" +
            "IsValid:u8(0,1)|Timestamp:f64le|TreeStateHash:u64le|BlackboardHash:u64le|" +
            "NodeCount:i32le|NodeStates:NodeCount*u8|NodeAuxInts:NodeCount*i32le|" +
            "BlackboardLength:i32le|Blackboard:RuntimeBlackboardSnapshot{Scope:Snapshot|Sequence:u64le|" +
            "IntCount:i32le|Ints:IntCount*(Key:i32le,Value:i32le)|" +
            "FloatCount:i32le|Floats:FloatCount*(Key:i32le,Value:f32le)|" +
            "BoolCount:i32le|Bools:BoolCount*(Key:i32le,Value:u8(0,1))|" +
            "Vector3Count:i32le|Vector3s:Vector3Count*(Key:i32le,X:f32le,Y:f32le,Z:f32le)|" +
            "LongCount:i32le|Longs:LongCount*(Key:i32le,Value:i64le)|" +
            "Long2Count:i32le|Long2s:Long2Count*(Key:i32le,X:i64le,Y:i64le)|" +
            "Long3Count:i32le|Long3s:Long3Count*(Key:i32le,X:i64le,Y:i64le,Z:i64le)|" +
            "StampCount:i32le|Stamps:StampCount*(Key:i32le,Stamp:u64le)}}";
        private const string BLACKBOARD_DELTA_WIRE_SCHEMA_V2 =
            "BehaviorTreeStatePayloadMessage.BlackboardDelta:v2|TargetNetworkId:u32le|Sequence:u16le|" +
            "Tick:i32le|PayloadKind:u8=2|TreeTemplateHash:u64le|BlackboardHash:u64le|" +
            "TreeStateHash:u64le|AuthorityGeneration:u32le|Payload:BTDP1{Magic:u32le=0x50445442|" +
            "Version:u16le=1|HeaderSize:u16le=16|BodyLength:i32le|EntryCount:i32le|" +
            "Entry:Key:i32le+Tag:u8+Value:tagged-le|" +
            "Tags:0=i32,1=f32,2=bool-u8(0,1),3=3*f32,4=i64,5=2*i64,6=3*i64,255=remove}";
        private const string DESYNC_REPORT_WIRE_SCHEMA_V2 =
            "BehaviorTreeDesyncReportMessage:v2|TargetNetworkId:u32le|Sequence:u16le|LocalTick:i32le|" +
            "RemoteTick:i32le|LocalBlackboardHash:u64le|RemoteBlackboardHash:u64le|" +
            "LocalTreeStateHash:u64le|RemoteTreeStateHash:u64le|AuthorityGeneration:u32le";
        private const ulong FULL_SNAPSHOT_SCHEMA_V2 = 0x750F7F22C73B0946UL;
        private const ulong BLACKBOARD_DELTA_SCHEMA_V2 = 0x5528AAF0A310630DUL;
        private const ulong DESYNC_REPORT_SCHEMA_V2 = 0x566A9F2B1C5C9202UL;
        private const ulong TICK_CONTROL_SCHEMA_V1 = 0x6299F932DCE53765UL;
        private const ulong AUTHORITY_TRANSFER_SCHEMA_V1 = 0x94B78D8EED490D89UL;

        public static readonly NetworkModuleProtocol Module = new NetworkModuleProtocol(CreateProtocolManifest());

        public static readonly NetworkProtocolManifest DefaultManifest = Module.Manifest;
        public static readonly NetworkMessageIdRange MessageRange = Module.MessageRange;
        public static readonly ulong ProtocolFingerprint = Module.Fingerprint;

        public static bool IsBehaviorTreeMessageId(ushort messageId)
        {
            return MessageRange.Contains(messageId);
        }

        public static bool IsBuiltInBehaviorTreeMessage(ushort messageId)
        {
            return messageId >= MSG_MANIFEST_HANDSHAKE && messageId <= MSG_AUTHORITY_TRANSFER;
        }

        public static bool TryRegisterMessageCatalog(INetworkMessageEndpoint messageEndpoint)
        {
            return Module.TryRegister(messageEndpoint);
        }

        public static void RegisterMessageCatalog(INetworkMessageCatalog catalog)
        {
            Module.Register(catalog);
        }

        public static NetworkProtocolManifest CreateProtocolManifest()
        {
            var builder = new NetworkProtocolManifestBuilder(
                MessageOwner,
                MESSAGE_ID_BASE,
                MESSAGE_ID_MAX)
            {
                ProtocolId = "CycloneGames.BehaviorTree.Networking",
                CurrentVersion = 2,
                MinimumSupportedVersion = 2
            };

            builder
                .SetMetadata("module", "BehaviorTree")
                .SetMetadata("snapshot", "RuntimeBehaviorTree blackboard snapshot payload")
                .SetMetadata("delta", "RuntimeBlackboard tracked-key delta payload")
                .AddMessage(
                    "BehaviorTreeManifestHandshakeMessage:v1",
                    MSG_MANIFEST_HANDSHAKE,
                    MANIFEST_HANDSHAKE_SCHEMA_V1,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_CONTROL_PAYLOAD_SIZE)
                .AddMessage(
                    FULL_SNAPSHOT_WIRE_SCHEMA_V2,
                    MSG_FULL_SNAPSHOT,
                    FULL_SNAPSHOT_SCHEMA_V2,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_STATE_MESSAGE_SIZE)
                .AddMessage(
                    BLACKBOARD_DELTA_WIRE_SCHEMA_V2,
                    MSG_BLACKBOARD_DELTA,
                    BLACKBOARD_DELTA_SCHEMA_V2,
                    NetworkChannel.UnreliableSequenced,
                    DEFAULT_MAX_STATE_MESSAGE_SIZE)
                .AddMessage(
                    DESYNC_REPORT_WIRE_SCHEMA_V2,
                    MSG_DESYNC_REPORT,
                    DESYNC_REPORT_SCHEMA_V2,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_CONTROL_PAYLOAD_SIZE)
                .AddMessage(
                    "BehaviorTreeTickControlMessage:v1",
                    MSG_TICK_CONTROL,
                    TICK_CONTROL_SCHEMA_V1,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_CONTROL_PAYLOAD_SIZE)
                .AddMessage(
                    "BehaviorTreeAuthorityTransferMessage:v1",
                    MSG_AUTHORITY_TRANSFER,
                    AUTHORITY_TRANSFER_SCHEMA_V1,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_CONTROL_PAYLOAD_SIZE);

            return builder.Build();
        }
    }
}
