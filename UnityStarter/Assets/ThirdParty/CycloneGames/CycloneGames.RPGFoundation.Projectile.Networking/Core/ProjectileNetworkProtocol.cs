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
                ProtocolId = "CycloneGames.RPGFoundation.Projectile.Networking",
                CurrentVersion = PROTOCOL_VERSION,
                MinimumSupportedVersion = MIN_SUPPORTED_PROTOCOL_VERSION
            };

            builder
                .SetMetadata("module", "RPGFoundation.Projectile")
                .SetMetadata("authority", "Server-authoritative projectile spawn, snapshot, hit, despawn, and correction contracts")
                .SetMetadata("extension", "High-density bullet patterns should replicate seed, definition id, and start tick through project-owned manifests when needed")
                .AddMessage<ProjectileManifestHandshakeMessage>(
                    MSG_MANIFEST_HANDSHAKE,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_CONTROL_PAYLOAD_SIZE)
                .AddMessage<ProjectileSpawnMessage>(
                    MSG_SPAWN,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_SPAWN_PAYLOAD_SIZE)
                .AddMessage<ProjectileSnapshotMessage>(
                    MSG_AUTHORITATIVE_SNAPSHOT,
                    NetworkChannel.UnreliableSequenced,
                    DEFAULT_MAX_SNAPSHOT_PAYLOAD_SIZE)
                .AddMessage<ProjectileCorrectionMessage>(
                    MSG_CORRECTION,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_CORRECTION_PAYLOAD_SIZE)
                .AddMessage<ProjectileHitMessage>(
                    MSG_HIT,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_HIT_PAYLOAD_SIZE)
                .AddMessage<ProjectileDespawnMessage>(
                    MSG_DESPAWN,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_CONTROL_PAYLOAD_SIZE)
                .AddMessage<ProjectileFullStateRequestMessage>(
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
