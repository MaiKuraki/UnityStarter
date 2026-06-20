using CycloneGames.Networking;

namespace CycloneGames.RPGFoundation.Movement.Networking
{
    public static class MovementNetworkProtocol
    {
        public const string MessageOwner = "CycloneGames.RPGFoundation.Movement";
        public const byte PROTOCOL_VERSION = 1;
        public const byte MIN_SUPPORTED_PROTOCOL_VERSION = 1;

        public const ushort MESSAGE_ID_BASE = 16000;
        public const ushort MESSAGE_ID_MAX = 16999;
        public const ushort MSG_MANIFEST_HANDSHAKE = MESSAGE_ID_BASE;
        public const ushort MSG_INPUT_COMMAND = MESSAGE_ID_BASE + 1;
        public const ushort MSG_AUTHORITATIVE_SNAPSHOT = MESSAGE_ID_BASE + 2;
        public const ushort MSG_CORRECTION = MESSAGE_ID_BASE + 3;
        public const ushort MSG_FULL_STATE_REQUEST = MESSAGE_ID_BASE + 4;
        public const ushort MSG_AUTHORITY_TRANSFER = MESSAGE_ID_BASE + 5;
        public const ushort MSG_TELEPORT = MESSAGE_ID_BASE + 6;

        public const ushort DEFAULT_FEATURE_MASK = 0;
        public const float MAX_INPUT_DELTA_TIME = 1f;
        public const int DEFAULT_MAX_INPUT_PAYLOAD_SIZE = 128;
        public const int DEFAULT_MAX_SNAPSHOT_PAYLOAD_SIZE = 256;
        public const int DEFAULT_MAX_CORRECTION_PAYLOAD_SIZE = 512;
        public const int DEFAULT_MAX_CONTROL_PAYLOAD_SIZE = 128;

        public static readonly NetworkModuleProtocol Module = new NetworkModuleProtocol(CreateProtocolManifest());

        public static readonly NetworkProtocolManifest DefaultManifest = Module.Manifest;
        public static readonly NetworkMessageIdRange MessageRange = Module.MessageRange;
        public static readonly ulong ProtocolFingerprint = Module.Fingerprint;

        public static bool IsMovementMessageId(ushort messageId)
        {
            return MessageRange.Contains(messageId);
        }

        public static bool IsBuiltInMovementMessage(ushort messageId)
        {
            return messageId >= MSG_MANIFEST_HANDSHAKE && messageId <= MSG_TELEPORT;
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
                ProtocolId = "CycloneGames.RPGFoundation.Movement.Networking",
                CurrentVersion = PROTOCOL_VERSION,
                MinimumSupportedVersion = MIN_SUPPORTED_PROTOCOL_VERSION
            };

            builder
                .SetMetadata("module", "RPGFoundation.Movement")
                .SetMetadata("authority", "Server-authoritative, owner-authoritative, host-migration, and relay-friendly movement contracts")
                .SetMetadata("extension", "Project-specific movement actions should use project-owned User protocol manifests")
                .AddMessage<MovementManifestHandshakeMessage>(
                    MSG_MANIFEST_HANDSHAKE,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_CONTROL_PAYLOAD_SIZE)
                .AddMessage<MovementInputCommandMessage>(
                    MSG_INPUT_COMMAND,
                    NetworkChannel.UnreliableSequenced,
                    DEFAULT_MAX_INPUT_PAYLOAD_SIZE)
                .AddMessage<MovementNetworkSnapshotMessage>(
                    MSG_AUTHORITATIVE_SNAPSHOT,
                    NetworkChannel.UnreliableSequenced,
                    DEFAULT_MAX_SNAPSHOT_PAYLOAD_SIZE)
                .AddMessage<MovementCorrectionMessage>(
                    MSG_CORRECTION,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_CORRECTION_PAYLOAD_SIZE)
                .AddMessage<MovementFullStateRequestMessage>(
                    MSG_FULL_STATE_REQUEST,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_CONTROL_PAYLOAD_SIZE)
                .AddMessage<MovementAuthorityTransferMessage>(
                    MSG_AUTHORITY_TRANSFER,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_CONTROL_PAYLOAD_SIZE)
                .AddMessage<MovementTeleportMessage>(
                    MSG_TELEPORT,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_SNAPSHOT_PAYLOAD_SIZE);

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
