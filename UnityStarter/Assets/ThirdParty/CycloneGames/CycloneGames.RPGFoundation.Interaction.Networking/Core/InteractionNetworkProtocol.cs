using System;
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

        public static readonly NetworkMessageIdRange MessageRange = new NetworkMessageIdRange(
            MessageOwner,
            MESSAGE_ID_BASE,
            MESSAGE_ID_MAX,
            NetworkMessageKind.Module);

        public static bool IsInteractionMessage(ushort messageId)
        {
            return messageId >= REQUEST_MESSAGE_ID && messageId <= DETERMINISTIC_REQUEST_MESSAGE_ID;
        }

        public static bool IsRPGFoundationInteractionMessageId(ushort messageId)
        {
            return MessageRange.Contains(messageId);
        }

        public static bool TryRegisterMessageCatalog(INetworkManager networkManager)
        {
            if (networkManager == null)
            {
                return false;
            }

            if (networkManager is not INetworkRuntimeContextProvider provider || provider.RuntimeContext == null)
            {
                return false;
            }

            if (!provider.RuntimeContext.TryGetService(out INetworkMessageCatalog catalog))
            {
                return false;
            }

            RegisterMessageCatalog(catalog);
            return true;
        }

        public static void RegisterMessageCatalog(INetworkMessageCatalog catalog)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            catalog.RegisterModuleRange(MessageRange);

            RegisterMessage<InteractionNetworkRequest>(
                catalog,
                REQUEST_MESSAGE_ID,
                REQUEST_CHANNEL,
                MAX_PAYLOAD_BYTES);

            RegisterMessage<InteractionNetworkResult>(
                catalog,
                RESULT_MESSAGE_ID,
                RESULT_CHANNEL,
                MAX_PAYLOAD_BYTES);

            RegisterMessage<InteractionNetworkCancelRequest>(
                catalog,
                CANCEL_REQUEST_MESSAGE_ID,
                CANCEL_REQUEST_CHANNEL,
                MAX_PAYLOAD_BYTES);

            RegisterMessage<InteractionNetworkRequest>(
                catalog,
                DETERMINISTIC_REQUEST_MESSAGE_ID,
                DETERMINISTIC_REQUEST_CHANNEL,
                MAX_PAYLOAD_BYTES);
        }

        public static void RegisterMessage<T>(
            INetworkMessageCatalog catalog,
            ushort messageId,
            NetworkChannel channel = NetworkChannel.Reliable,
            int maxPayloadSize = NetworkConstants.DefaultMaxPayloadSize) where T : struct
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (!IsRPGFoundationInteractionMessageId(messageId))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(messageId),
                    messageId,
                    $"RPGFoundation Interaction message ids must be inside {MessageRange}.");
            }

            NetworkMessageDescriptor descriptor = NetworkMessageDescriptor.Create<T>(
                messageId,
                MessageOwner,
                NetworkMessageKind.Module,
                channel,
                maxPayloadSize);

            if (catalog.TryRegister(descriptor))
            {
                return;
            }

            if (catalog.TryGet(messageId, out NetworkMessageDescriptor existing)
                && existing.SchemaHash == descriptor.SchemaHash
                && string.Equals(existing.Owner, descriptor.Owner, StringComparison.Ordinal)
                && string.Equals(existing.Name, descriptor.Name, StringComparison.Ordinal)
                && existing.Kind == descriptor.Kind
                && existing.DefaultChannel == descriptor.DefaultChannel
                && existing.MaxPayloadSize == descriptor.MaxPayloadSize)
            {
                return;
            }

            throw new InvalidOperationException($"Message id {messageId} is already registered by {existing.Owner}:{existing.Name}.");
        }
    }
}
