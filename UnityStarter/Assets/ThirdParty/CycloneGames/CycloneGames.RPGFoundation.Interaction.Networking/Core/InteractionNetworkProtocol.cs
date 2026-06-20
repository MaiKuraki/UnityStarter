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
                ProtocolId = "CycloneGames.RPGFoundation.Interaction.Networking",
                CurrentVersion = PROTOCOL_VERSION,
                MinimumSupportedVersion = PROTOCOL_VERSION
            };

            builder
                .SetMetadata("module", "RPGFoundation.Interaction")
                .AddMessage<InteractionNetworkRequest>(REQUEST_MESSAGE_ID, REQUEST_CHANNEL, MAX_PAYLOAD_BYTES)
                .AddMessage<InteractionNetworkResult>(RESULT_MESSAGE_ID, RESULT_CHANNEL, MAX_PAYLOAD_BYTES)
                .AddMessage<InteractionNetworkCancelRequest>(CANCEL_REQUEST_MESSAGE_ID, CANCEL_REQUEST_CHANNEL, MAX_PAYLOAD_BYTES)
                .AddMessage<InteractionNetworkRequest>(DETERMINISTIC_REQUEST_MESSAGE_ID, DETERMINISTIC_REQUEST_CHANNEL, MAX_PAYLOAD_BYTES);

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
