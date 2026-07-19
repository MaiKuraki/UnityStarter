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

        // Frozen FNV-1a64 identities of the versioned wire contracts ("<contract-name>:v1").
        private const ulong MANIFEST_HANDSHAKE_SCHEMA_V1 = 0x0DD5DE0CCEFCE7E8UL;
        private const ulong INPUT_COMMAND_SCHEMA_V1 = 0xAA87AB05419B69EFUL;
        private const ulong SNAPSHOT_SCHEMA_V1 = 0x2BE4EE685C0790F4UL;
        private const ulong CORRECTION_SCHEMA_V1 = 0x758234C52F9A9A0EUL;
        private const ulong FULL_STATE_REQUEST_SCHEMA_V1 = 0xE17B11E352C841E9UL;
        private const ulong AUTHORITY_TRANSFER_SCHEMA_V1 = 0xA29871158FF1EDD8UL;
        private const ulong TELEPORT_SCHEMA_V1 = 0xCBBC9E08374EA4B9UL;

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
                ProtocolId = "CycloneGames.RPGFoundation.Movement.Networking",
                CurrentVersion = PROTOCOL_VERSION,
                MinimumSupportedVersion = MIN_SUPPORTED_PROTOCOL_VERSION
            };

            builder
                .SetMetadata("module", "RPGFoundation.Movement")
                .SetMetadata("authority", "Server-authoritative, owner-authoritative, host-migration, and relay-friendly movement contracts")
                .SetMetadata("extension", "Project-specific movement actions should use project-owned User protocol manifests")
                .AddMessage(
                    "MovementManifestHandshakeMessage:v1",
                    MSG_MANIFEST_HANDSHAKE,
                    MANIFEST_HANDSHAKE_SCHEMA_V1,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_CONTROL_PAYLOAD_SIZE)
                .AddMessage(
                    "MovementInputCommandMessage:v1",
                    MSG_INPUT_COMMAND,
                    INPUT_COMMAND_SCHEMA_V1,
                    NetworkChannel.UnreliableSequenced,
                    DEFAULT_MAX_INPUT_PAYLOAD_SIZE)
                .AddMessage(
                    "MovementNetworkSnapshotMessage:v1",
                    MSG_AUTHORITATIVE_SNAPSHOT,
                    SNAPSHOT_SCHEMA_V1,
                    NetworkChannel.UnreliableSequenced,
                    DEFAULT_MAX_SNAPSHOT_PAYLOAD_SIZE)
                .AddMessage(
                    "MovementCorrectionMessage:v1",
                    MSG_CORRECTION,
                    CORRECTION_SCHEMA_V1,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_CORRECTION_PAYLOAD_SIZE)
                .AddMessage(
                    "MovementFullStateRequestMessage:v1",
                    MSG_FULL_STATE_REQUEST,
                    FULL_STATE_REQUEST_SCHEMA_V1,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_CONTROL_PAYLOAD_SIZE)
                .AddMessage(
                    "MovementAuthorityTransferMessage:v1",
                    MSG_AUTHORITY_TRANSFER,
                    AUTHORITY_TRANSFER_SCHEMA_V1,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_CONTROL_PAYLOAD_SIZE)
                .AddMessage(
                    "MovementTeleportMessage:v1",
                    MSG_TELEPORT,
                    TELEPORT_SCHEMA_V1,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_SNAPSHOT_PAYLOAD_SIZE);

            return builder.Build();
        }

    }
}
