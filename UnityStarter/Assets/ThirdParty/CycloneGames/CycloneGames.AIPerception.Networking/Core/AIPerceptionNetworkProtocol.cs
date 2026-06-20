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

        public static bool TryRegisterMessageCatalog(INetworkManager networkManager)
        {
            return Module.TryRegister(networkManager);
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
                MESSAGE_ID_MAX,
                NetworkMessageKind.Module)
            {
                ProtocolId = "CycloneGames.AIPerception.Networking",
                CurrentVersion = PROTOCOL_VERSION,
                MinimumSupportedVersion = MIN_SUPPORTED_PROTOCOL_VERSION
            };

            builder
                .SetMetadata("module", "AIPerception")
                .SetMetadata("snapshot", "Detection and stimulus memory payloads")
                .AddMessage<AIPerceptionManifestHandshakeMessage>(
                    MSG_MANIFEST_HANDSHAKE,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_CONTROL_PAYLOAD_SIZE)
                .AddMessage<AIPerceptionDetectionEventMessage>(
                    MSG_DETECTION_EVENT,
                    NetworkChannel.UnreliableSequenced,
                    DEFAULT_MAX_EVENT_PAYLOAD_SIZE)
                .AddMessage<AIPerceptionDetectionSnapshotMessage>(
                    MSG_DETECTION_SNAPSHOT,
                    NetworkChannel.UnreliableSequenced,
                    DEFAULT_MAX_SNAPSHOT_PAYLOAD_SIZE)
                .AddMessage<AIPerceptionDetectionSnapshotMessage>(
                    MSG_MEMORY_SNAPSHOT,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_SNAPSHOT_PAYLOAD_SIZE)
                .AddMessage<AIPerceptionAuthorityTransferMessage>(
                    MSG_AUTHORITY_TRANSFER,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_CONTROL_PAYLOAD_SIZE)
                .AddMessage<AIPerceptionFullStateRequestMessage>(
                    MSG_FULL_STATE_REQUEST,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_CONTROL_PAYLOAD_SIZE);

            return builder.Build();
        }

        public static void RegisterMessage<T>(
            INetworkMessageCatalog catalog,
            ushort messageId,
            NetworkChannel channel = NetworkChannel.Reliable,
            int maxPayloadSize = NetworkConstants.DefaultMaxPayloadSize) where T : struct
        {
            Module.RegisterMessage<T>(catalog, messageId, channel, maxPayloadSize);
        }
    }
}

