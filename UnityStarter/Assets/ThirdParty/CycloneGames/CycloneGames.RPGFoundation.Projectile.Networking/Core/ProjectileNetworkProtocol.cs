using CycloneGames.Networking;

namespace CycloneGames.RPGFoundation.Projectile.Networking
{
    /// <summary>
    /// Contract registration for projectile networking. This package ships no wire codec:
    /// descriptor schema hashes pin the contract IDENTITY (the exact ContractId
    /// string), not the binary payload layout. The wire format is owned by the
    /// integrator's chosen serializer; peers agree on the layout by sharing
    /// serializer configuration, and any layout change must ship under a new
    /// contract identity.
    /// </summary>
    /// <remarks>
    /// Contract values are frozen. Tunable defaults use
    /// static readonly fields so a change is visible to every consuming assembly
    /// without recompilation of embedded literals.
    /// </remarks>
    public static class ProjectileNetworkProtocol
    {
        public static readonly string MessageOwner = "CycloneGames.RPGFoundation.Projectile";

        public static readonly ushort MESSAGE_ID_BASE = 17000;
        public static readonly ushort MESSAGE_ID_MAX = 17999;
        public static readonly ushort MSG_MANIFEST_HANDSHAKE = MESSAGE_ID_BASE;
        public static readonly ushort MSG_SPAWN = (ushort)(MESSAGE_ID_BASE + 1);
        public static readonly ushort MSG_AUTHORITATIVE_SNAPSHOT = (ushort)(MESSAGE_ID_BASE + 2);
        public static readonly ushort MSG_CORRECTION = (ushort)(MESSAGE_ID_BASE + 3);
        public static readonly ushort MSG_HIT = (ushort)(MESSAGE_ID_BASE + 4);
        public static readonly ushort MSG_DESPAWN = (ushort)(MESSAGE_ID_BASE + 5);
        public static readonly ushort MSG_FULL_STATE_REQUEST = (ushort)(MESSAGE_ID_BASE + 6);

        public static readonly ushort DEFAULT_FEATURE_MASK = 0;
        public static readonly int DEFAULT_MAX_CONTROL_PAYLOAD_SIZE = 128;
        public static readonly int DEFAULT_MAX_SPAWN_PAYLOAD_SIZE = 256;
        public static readonly int DEFAULT_MAX_SNAPSHOT_PAYLOAD_SIZE = 256;
        public static readonly int DEFAULT_MAX_CORRECTION_PAYLOAD_SIZE = 512;
        public static readonly int DEFAULT_MAX_HIT_PAYLOAD_SIZE = 256;

        // Frozen FNV-1a64 identities of the wire contracts.
        private const ulong MANIFEST_HANDSHAKE_SCHEMA = 0xBF9603F2BAF7ED8BUL;
        private const ulong SPAWN_SCHEMA = 0x8C2D1AFA9AE1FD98UL;
        private const ulong SNAPSHOT_SCHEMA = 0x468C2C096E6CAA95UL;
        private const ulong CORRECTION_SCHEMA = 0xD33279596778BC7DUL;
        private const ulong HIT_SCHEMA = 0x2BADF5DACE4C4AC2UL;
        private const ulong DESPAWN_SCHEMA = 0x2B2F979D125D7257UL;
        private const ulong FULL_STATE_REQUEST_SCHEMA = 0xB3B227B956ADE17CUL;

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
            };

            builder
                .SetMetadata("module", "RPGFoundation.Projectile")
                .SetMetadata("authority", "Server-authoritative projectile spawn, snapshot, hit, despawn, and correction contracts")
                .SetMetadata("extension", "High-density bullet patterns should replicate seed, definition id, and start tick through project-owned manifests when needed")
                .AddMessage(
                    "ProjectileManifestHandshakeMessage",
                    MSG_MANIFEST_HANDSHAKE,
                    MANIFEST_HANDSHAKE_SCHEMA,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_CONTROL_PAYLOAD_SIZE)
                .AddMessage(
                    "ProjectileSpawnMessage",
                    MSG_SPAWN,
                    SPAWN_SCHEMA,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_SPAWN_PAYLOAD_SIZE)
                .AddMessage(
                    "ProjectileSnapshotMessage",
                    MSG_AUTHORITATIVE_SNAPSHOT,
                    SNAPSHOT_SCHEMA,
                    NetworkChannel.UnreliableSequenced,
                    DEFAULT_MAX_SNAPSHOT_PAYLOAD_SIZE)
                .AddMessage(
                    "ProjectileCorrectionMessage",
                    MSG_CORRECTION,
                    CORRECTION_SCHEMA,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_CORRECTION_PAYLOAD_SIZE)
                .AddMessage(
                    "ProjectileHitMessage",
                    MSG_HIT,
                    HIT_SCHEMA,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_HIT_PAYLOAD_SIZE)
                .AddMessage(
                    "ProjectileDespawnMessage",
                    MSG_DESPAWN,
                    DESPAWN_SCHEMA,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_CONTROL_PAYLOAD_SIZE)
                .AddMessage(
                    "ProjectileFullStateRequestMessage",
                    MSG_FULL_STATE_REQUEST,
                    FULL_STATE_REQUEST_SCHEMA,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_CONTROL_PAYLOAD_SIZE);

            return builder.Build();
        }
    }
}
