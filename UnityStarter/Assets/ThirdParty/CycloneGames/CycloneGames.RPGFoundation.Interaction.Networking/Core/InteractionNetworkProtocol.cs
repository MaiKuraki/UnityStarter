using CycloneGames.Networking;
using CycloneGames.RPGFoundation.Interaction.Core;

namespace CycloneGames.RPGFoundation.Interaction.Networking
{
    public static class InteractionNetworkProtocol
    {
        public const string MessageOwner = "CycloneGames.RPGFoundation.Interaction";
        public const byte PROTOCOL_VERSION = 1;
        public const ushort MESSAGE_ID_BASE = 13000;
        public const ushort MESSAGE_ID_MAX = 13999;
        public const ushort REQUEST_MESSAGE_ID = MESSAGE_ID_BASE;
        public const ushort RESULT_MESSAGE_ID = MESSAGE_ID_BASE + 1;
        public const ushort CANCEL_REQUEST_MESSAGE_ID = MESSAGE_ID_BASE + 2;
        public const ushort DETERMINISTIC_REQUEST_MESSAGE_ID = MESSAGE_ID_BASE + 3;
        public const int MAX_ACTION_ID_LENGTH = 128;
        public const int MAX_PAYLOAD_BYTES = NetworkConstants.DefaultMaxPayloadSize;
        public const NetworkChannel REQUEST_CHANNEL = NetworkChannel.Reliable;
        public const NetworkChannel RESULT_CHANNEL = NetworkChannel.Reliable;
        public const NetworkChannel CANCEL_REQUEST_CHANNEL = NetworkChannel.Reliable;
        public const NetworkChannel DETERMINISTIC_REQUEST_CHANNEL = NetworkChannel.Reliable;

        // Frozen FNV-1a64 identities of the versioned wire contracts ("<contract-name>:v1").
        private const ulong REQUEST_SCHEMA_V1 = 0xD76DBACD901D2BB7UL;
        private const ulong RESULT_SCHEMA_V1 = 0x50B6ECBD2F378EC9UL;
        private const ulong CANCEL_REQUEST_SCHEMA_V1 = 0x00E384398B2C4151UL;

        public static readonly NetworkModuleProtocol Module = new NetworkModuleProtocol(CreateProtocolManifest());

        public static readonly NetworkProtocolManifest DefaultManifest = Module.Manifest;
        public static readonly NetworkMessageIdRange MessageRange = Module.MessageRange;
        public static readonly ulong ProtocolFingerprint = Module.Fingerprint;

        public static bool IsInteractionMessage(ushort messageId)
        {
            return messageId >= REQUEST_MESSAGE_ID && messageId <= DETERMINISTIC_REQUEST_MESSAGE_ID;
        }

        public static bool IsRPGFoundationInteractionMessageId(ushort messageId)
        {
            return Module.ContainsMessageId(messageId);
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
                ProtocolId = "CycloneGames.RPGFoundation.Interaction.Networking",
                CurrentVersion = PROTOCOL_VERSION,
                MinimumSupportedVersion = PROTOCOL_VERSION
            };

            builder
                .SetMetadata("module", "RPGFoundation.Interaction")
                .AddMessage(
                    "InteractionNetworkRequest:v1",
                    REQUEST_MESSAGE_ID,
                    REQUEST_SCHEMA_V1,
                    REQUEST_CHANNEL,
                    MAX_PAYLOAD_BYTES)
                .AddMessage(
                    "InteractionNetworkResult:v1",
                    RESULT_MESSAGE_ID,
                    RESULT_SCHEMA_V1,
                    RESULT_CHANNEL,
                    MAX_PAYLOAD_BYTES)
                .AddMessage(
                    "InteractionNetworkCancelRequest:v1",
                    CANCEL_REQUEST_MESSAGE_ID,
                    CANCEL_REQUEST_SCHEMA_V1,
                    CANCEL_REQUEST_CHANNEL,
                    MAX_PAYLOAD_BYTES)
                .AddMessage(
                    "InteractionNetworkRequest:v1",
                    DETERMINISTIC_REQUEST_MESSAGE_ID,
                    REQUEST_SCHEMA_V1,
                    DETERMINISTIC_REQUEST_CHANNEL,
                    MAX_PAYLOAD_BYTES);

            return builder.Build();
        }

    }
}
