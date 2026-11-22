using CycloneGames.Networking;

namespace CycloneGames.RPGFoundation.Movement.Networking
{
    /// <summary>
    /// Contract registration for movement networking. This package ships no wire codec:
    /// descriptor schema hashes pin the contract IDENTITY (the exact ContractId
    /// string), not the binary payload layout. The wire format is owned by the
    /// integrator's chosen INetSerializer; peers agree on the layout by sharing
    /// serializer configuration, and any layout change must ship under a new
    /// contract identity.
    /// </summary>
    public static class MovementNetworkProtocol
    {
        public const string MessageOwner = "CycloneGames.RPGFoundation.Movement";

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

        // Frozen FNV-1a64 identities of the wire contracts.
        private const ulong MANIFEST_HANDSHAKE_SCHEMA = 0xF9DBBF4DFD6A2DBDUL;
        private const ulong INPUT_COMMAND_SCHEMA = 0xED5685E0364FD524UL;
        private const ulong SNAPSHOT_SCHEMA = 0x290EAF94DF7CAE29UL;
        private const ulong CORRECTION_SCHEMA = 0x1849CCF7502F53FFUL;
        private const ulong FULL_STATE_REQUEST_SCHEMA = 0x8BED532024596AFAUL;
        private const ulong AUTHORITY_TRANSFER_SCHEMA = 0x04340E944DE5396DUL;
        private const ulong TELEPORT_SCHEMA = 0x2DA340ABAE23680AUL;

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
            };

            builder
                .SetMetadata("module", "RPGFoundation.Movement")
                .SetMetadata("authority", "Server-authoritative, owner-authoritative, host-migration, and relay-friendly movement contracts")
                .SetMetadata("extension", "Project-specific movement actions should use project-owned User protocol manifests")
                .AddMessage(
                    "MovementManifestHandshakeMessage",
                    MSG_MANIFEST_HANDSHAKE,
                    MANIFEST_HANDSHAKE_SCHEMA,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_CONTROL_PAYLOAD_SIZE)
                .AddMessage(
                    "MovementInputCommandMessage",
                    MSG_INPUT_COMMAND,
                    INPUT_COMMAND_SCHEMA,
                    NetworkChannel.UnreliableSequenced,
                    DEFAULT_MAX_INPUT_PAYLOAD_SIZE)
                .AddMessage(
                    "MovementNetworkSnapshotMessage",
                    MSG_AUTHORITATIVE_SNAPSHOT,
                    SNAPSHOT_SCHEMA,
                    NetworkChannel.UnreliableSequenced,
                    DEFAULT_MAX_SNAPSHOT_PAYLOAD_SIZE)
                .AddMessage(
                    "MovementCorrectionMessage",
                    MSG_CORRECTION,
                    CORRECTION_SCHEMA,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_CORRECTION_PAYLOAD_SIZE)
                .AddMessage(
                    "MovementFullStateRequestMessage",
                    MSG_FULL_STATE_REQUEST,
                    FULL_STATE_REQUEST_SCHEMA,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_CONTROL_PAYLOAD_SIZE)
                .AddMessage(
                    "MovementAuthorityTransferMessage",
                    MSG_AUTHORITY_TRANSFER,
                    AUTHORITY_TRANSFER_SCHEMA,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_CONTROL_PAYLOAD_SIZE)
                .AddMessage(
                    "MovementTeleportMessage",
                    MSG_TELEPORT,
                    TELEPORT_SCHEMA,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_SNAPSHOT_PAYLOAD_SIZE);

            return builder.Build();
        }

    }
}
