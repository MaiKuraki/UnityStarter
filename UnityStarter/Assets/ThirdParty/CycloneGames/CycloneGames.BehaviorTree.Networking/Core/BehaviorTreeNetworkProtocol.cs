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

        public const int DEFAULT_MAX_SNAPSHOT_PAYLOAD_SIZE = NetworkConstants.DefaultMaxPayloadSize * 8;
        public const int DEFAULT_MAX_DELTA_PAYLOAD_SIZE = NetworkConstants.DefaultMaxPayloadSize * 2;
        public const int DEFAULT_MAX_CONTROL_PAYLOAD_SIZE = 128;

        // Frozen FNV-1a64 identities of the versioned wire contracts ("<contract-name>:v1").
        private const ulong MANIFEST_HANDSHAKE_SCHEMA_V1 = 0x059263302E9505CDUL;
        private const ulong STATE_PAYLOAD_SCHEMA_V1 = 0xA5D8529342EA168CUL;
        private const ulong DESYNC_REPORT_SCHEMA_V1 = 0x7CA942FF64163207UL;
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
                ProtocolId = "CycloneGames.BehaviorTree.Networking"
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
                    "BehaviorTreeStatePayloadMessage:v1",
                    MSG_FULL_SNAPSHOT,
                    STATE_PAYLOAD_SCHEMA_V1,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_SNAPSHOT_PAYLOAD_SIZE)
                .AddMessage(
                    "BehaviorTreeStatePayloadMessage:v1",
                    MSG_BLACKBOARD_DELTA,
                    STATE_PAYLOAD_SCHEMA_V1,
                    NetworkChannel.UnreliableSequenced,
                    DEFAULT_MAX_DELTA_PAYLOAD_SIZE)
                .AddMessage(
                    "BehaviorTreeDesyncReportMessage:v1",
                    MSG_DESYNC_REPORT,
                    DESYNC_REPORT_SCHEMA_V1,
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
