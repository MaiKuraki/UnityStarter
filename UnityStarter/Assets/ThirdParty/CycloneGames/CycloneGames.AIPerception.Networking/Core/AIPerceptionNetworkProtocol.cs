using CycloneGames.Networking;

namespace CycloneGames.AIPerception.Networking
{
    public static class AIPerceptionNetworkProtocol
    {
        public const string MessageOwner = "CycloneGames.AIPerception";
        public const byte PROTOCOL_VERSION = 1;
        public const byte MIN_SUPPORTED_PROTOCOL_VERSION = 1;

        public const ushort MESSAGE_ID_BASE = 15000;
        public const ushort MESSAGE_ID_MAX = 15999;
        public const ushort MSG_MANIFEST_HANDSHAKE = MESSAGE_ID_BASE;
        public const ushort MSG_DETECTION_EVENT = MESSAGE_ID_BASE + 1;
        public const ushort MSG_DETECTION_SNAPSHOT = MESSAGE_ID_BASE + 2;
        public const ushort MSG_MEMORY_SNAPSHOT = MESSAGE_ID_BASE + 3;
        public const ushort MSG_AUTHORITY_TRANSFER = MESSAGE_ID_BASE + 4;
        public const ushort MSG_FULL_STATE_REQUEST = MESSAGE_ID_BASE + 5;

        public const int DEFAULT_MAX_EVENT_PAYLOAD_SIZE = 256;
        public const int DEFAULT_MAX_SNAPSHOT_PAYLOAD_SIZE = NetworkConstants.DefaultMaxPayloadSize * 4;
        public const int DEFAULT_MAX_CONTROL_PAYLOAD_SIZE = 128;

        // Frozen FNV-1a64 identities of the versioned wire contracts ("<contract-name>:v1").
        private const ulong MANIFEST_HANDSHAKE_SCHEMA_V1 = 0xE24FD3DF9C74AB1CUL;
        private const ulong DETECTION_EVENT_SCHEMA_V1 = 0x7FB1540691D2B0BFUL;
        private const ulong DETECTION_SNAPSHOT_SCHEMA_V1 = 0xA9F15D28F3BC339DUL;
        private const ulong AUTHORITY_TRANSFER_SCHEMA_V1 = 0xDD0A7C2010BB2D4CUL;
        private const ulong FULL_STATE_REQUEST_SCHEMA_V1 = 0xF715DC535205849DUL;

        public static readonly NetworkModuleProtocol Module = new NetworkModuleProtocol(CreateProtocolManifest());

        public static readonly NetworkProtocolManifest DefaultManifest = Module.Manifest;
        public static readonly NetworkMessageIdRange MessageRange = Module.MessageRange;
        public static readonly ulong ProtocolFingerprint = Module.Fingerprint;

        public static bool IsAIPerceptionMessageId(ushort messageId)
        {
            return MessageRange.Contains(messageId);
        }

        public static bool IsBuiltInAIPerceptionMessage(ushort messageId)
        {
            return messageId >= MSG_MANIFEST_HANDSHAKE && messageId <= MSG_FULL_STATE_REQUEST;
        }

        public static bool IsSupportedProtocolVersion(byte protocolVersion)
        {
            return Module.IsSupportedProtocolVersion(protocolVersion);
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
                ProtocolId = "CycloneGames.AIPerception.Networking",
                CurrentVersion = PROTOCOL_VERSION,
                MinimumSupportedVersion = MIN_SUPPORTED_PROTOCOL_VERSION
            };

            builder
                .SetMetadata("module", "AIPerception")
                .SetMetadata("snapshot", "Detection and stimulus memory payloads")
                .AddMessage(
                    "AIPerceptionManifestHandshakeMessage:v1",
                    MSG_MANIFEST_HANDSHAKE,
                    MANIFEST_HANDSHAKE_SCHEMA_V1,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_CONTROL_PAYLOAD_SIZE)
                .AddMessage(
                    "AIPerceptionDetectionEventMessage:v1",
                    MSG_DETECTION_EVENT,
                    DETECTION_EVENT_SCHEMA_V1,
                    NetworkChannel.UnreliableSequenced,
                    DEFAULT_MAX_EVENT_PAYLOAD_SIZE)
                .AddMessage(
                    "AIPerceptionDetectionSnapshotMessage:v1",
                    MSG_DETECTION_SNAPSHOT,
                    DETECTION_SNAPSHOT_SCHEMA_V1,
                    NetworkChannel.UnreliableSequenced,
                    DEFAULT_MAX_SNAPSHOT_PAYLOAD_SIZE)
                .AddMessage(
                    "AIPerceptionDetectionSnapshotMessage:v1",
                    MSG_MEMORY_SNAPSHOT,
                    DETECTION_SNAPSHOT_SCHEMA_V1,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_SNAPSHOT_PAYLOAD_SIZE)
                .AddMessage(
                    "AIPerceptionAuthorityTransferMessage:v1",
                    MSG_AUTHORITY_TRANSFER,
                    AUTHORITY_TRANSFER_SCHEMA_V1,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_CONTROL_PAYLOAD_SIZE)
                .AddMessage(
                    "AIPerceptionFullStateRequestMessage:v1",
                    MSG_FULL_STATE_REQUEST,
                    FULL_STATE_REQUEST_SCHEMA_V1,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_CONTROL_PAYLOAD_SIZE);

            return builder.Build();
        }
    }
}

