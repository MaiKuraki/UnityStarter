using CycloneGames.Networking;

namespace CycloneGames.RPGFoundation.Projectile.Networking
{
    public static class ProjectileNetworkProtocol
    {
        public const string MessageOwner = "CycloneGames.RPGFoundation.Projectile";
        public const byte PROTOCOL_VERSION = 1;
        public const byte MIN_SUPPORTED_PROTOCOL_VERSION = 1;

        public const ushort MESSAGE_ID_BASE = 17000;
        public const ushort MESSAGE_ID_MAX = 17999;
        public const ushort MSG_MANIFEST_HANDSHAKE = MESSAGE_ID_BASE;
        public const ushort MSG_SPAWN = MESSAGE_ID_BASE + 1;
        public const ushort MSG_AUTHORITATIVE_SNAPSHOT = MESSAGE_ID_BASE + 2;
        public const ushort MSG_CORRECTION = MESSAGE_ID_BASE + 3;
        public const ushort MSG_HIT = MESSAGE_ID_BASE + 4;
        public const ushort MSG_DESPAWN = MESSAGE_ID_BASE + 5;
        public const ushort MSG_FULL_STATE_REQUEST = MESSAGE_ID_BASE + 6;

        public const ushort DEFAULT_FEATURE_MASK = 0;
        public const int DEFAULT_MAX_CONTROL_PAYLOAD_SIZE = 128;
        public const int DEFAULT_MAX_SPAWN_PAYLOAD_SIZE = 256;
        public const int DEFAULT_MAX_SNAPSHOT_PAYLOAD_SIZE = 256;
        public const int DEFAULT_MAX_CORRECTION_PAYLOAD_SIZE = 512;
        public const int DEFAULT_MAX_HIT_PAYLOAD_SIZE = 256;

        // Frozen FNV-1a64 identities of the versioned wire contracts ("<contract-name>:v1").
        private const ulong MANIFEST_HANDSHAKE_SCHEMA_V1 = 0xBCEFB413200046DAUL;
        private const ulong SPAWN_SCHEMA_V1 = 0xB9F717F41389BE93UL;
        private const ulong SNAPSHOT_SCHEMA_V1 = 0x706E549553486060UL;
        private const ulong CORRECTION_SCHEMA_V1 = 0x9259A444E90A56A8UL;
        private const ulong HIT_SCHEMA_V1 = 0xEF8D26DB044A29F1UL;
        private const ulong DESPAWN_SCHEMA_V1 = 0x2C4E503FF06B2126UL;
        private const ulong FULL_STATE_REQUEST_SCHEMA_V1 = 0xF9FF5A38C895A567UL;

        public static readonly NetworkModuleProtocol Module = new NetworkModuleProtocol(CreateProtocolManifest());

        public static readonly NetworkProtocolManifest DefaultManifest = Module.Manifest;
        public static readonly NetworkMessageIdRange MessageRange = Module.MessageRange;
        public static readonly ulong ProtocolFingerprint = Module.Fingerprint;

        public static bool IsProjectileMessageId(ushort messageId)
        {
            return MessageRange.Contains(messageId);
        }

        public static bool IsBuiltInProjectileMessage(ushort messageId)
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
                ProtocolId = "CycloneGames.RPGFoundation.Projectile.Networking",
                CurrentVersion = PROTOCOL_VERSION,
                MinimumSupportedVersion = MIN_SUPPORTED_PROTOCOL_VERSION
            };

            builder
                .SetMetadata("module", "RPGFoundation.Projectile")
                .SetMetadata("authority", "Server-authoritative projectile spawn, snapshot, hit, despawn, and correction contracts")
                .SetMetadata("extension", "High-density bullet patterns should replicate seed, definition id, and start tick through project-owned manifests when needed")
                .AddMessage(
                    "ProjectileManifestHandshakeMessage:v1",
                    MSG_MANIFEST_HANDSHAKE,
                    MANIFEST_HANDSHAKE_SCHEMA_V1,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_CONTROL_PAYLOAD_SIZE)
                .AddMessage(
                    "ProjectileSpawnMessage:v1",
                    MSG_SPAWN,
                    SPAWN_SCHEMA_V1,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_SPAWN_PAYLOAD_SIZE)
                .AddMessage(
                    "ProjectileSnapshotMessage:v1",
                    MSG_AUTHORITATIVE_SNAPSHOT,
                    SNAPSHOT_SCHEMA_V1,
                    NetworkChannel.UnreliableSequenced,
                    DEFAULT_MAX_SNAPSHOT_PAYLOAD_SIZE)
                .AddMessage(
                    "ProjectileCorrectionMessage:v1",
                    MSG_CORRECTION,
                    CORRECTION_SCHEMA_V1,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_CORRECTION_PAYLOAD_SIZE)
                .AddMessage(
                    "ProjectileHitMessage:v1",
                    MSG_HIT,
                    HIT_SCHEMA_V1,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_HIT_PAYLOAD_SIZE)
                .AddMessage(
                    "ProjectileDespawnMessage:v1",
                    MSG_DESPAWN,
                    DESPAWN_SCHEMA_V1,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_CONTROL_PAYLOAD_SIZE)
                .AddMessage(
                    "ProjectileFullStateRequestMessage:v1",
                    MSG_FULL_STATE_REQUEST,
                    FULL_STATE_REQUEST_SCHEMA_V1,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_CONTROL_PAYLOAD_SIZE);

            return builder.Build();
        }

    }
}
