using CycloneGames.Networking;
using CycloneGames.RPGFoundation.Interaction.Core;

namespace CycloneGames.RPGFoundation.Interaction.Networking
{
    /// <summary>
    /// Contract registration for interaction networking. This package ships no wire codec:
    /// descriptor schema hashes pin the contract IDENTITY (the exact ContractId
    /// string), not the binary payload layout. The wire format is owned by the
    /// integrator's chosen INetSerializer; peers agree on the layout by sharing
    /// serializer configuration, and any layout change must ship under a new
    /// contract identity.
    /// </summary>
    public static class InteractionNetworkProtocol
    {
        public const string MessageOwner = "CycloneGames.RPGFoundation.Interaction";
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

        // Frozen FNV-1a64 identities of the wire contracts.
        private const ulong REQUEST_SCHEMA = 0x2ADFB567B325490CUL;
        private const ulong RESULT_SCHEMA = 0x9694E2DD45B6B8DAUL;
        private const ulong CANCEL_REQUEST_SCHEMA = 0x3C386AC6CE4B6C22UL;
        private const ulong DETERMINISTIC_REQUEST_SCHEMA = 0x9A5D1D02A7D24950UL;

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
            };

            builder
                .SetMetadata("module", "RPGFoundation.Interaction")
                .AddMessage(
                    "InteractionNetworkRequest",
                    REQUEST_MESSAGE_ID,
                    REQUEST_SCHEMA,
                    REQUEST_CHANNEL,
                    MAX_PAYLOAD_BYTES)
                .AddMessage(
                    "InteractionNetworkResult",
                    RESULT_MESSAGE_ID,
                    RESULT_SCHEMA,
                    RESULT_CHANNEL,
                    MAX_PAYLOAD_BYTES)
                .AddMessage(
                    "InteractionNetworkCancelRequest",
                    CANCEL_REQUEST_MESSAGE_ID,
                    CANCEL_REQUEST_SCHEMA,
                    CANCEL_REQUEST_CHANNEL,
                    MAX_PAYLOAD_BYTES)
                .AddMessage(
                    "InteractionNetworkDeterministicRequest",
                    DETERMINISTIC_REQUEST_MESSAGE_ID,
                    DETERMINISTIC_REQUEST_SCHEMA,
                    DETERMINISTIC_REQUEST_CHANNEL,
                    MAX_PAYLOAD_BYTES);

            return builder.Build();
        }

    }
}
