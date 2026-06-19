using System;
using CycloneGames.GameplayFramework.Runtime;
using CycloneGames.Networking;

namespace CycloneGames.GameplayFramework.Networking
{
    public static class GameplayFrameworkNetworkProtocol
    {
        public const string MessageOwner = "CycloneGames.GameplayFramework";

        public const ushort MESSAGE_ID_BASE = 11000;
        public const ushort MESSAGE_ID_MAX = 11999;
        public const ushort MsgActorMigrationState = MESSAGE_ID_BASE;

        public static readonly NetworkMessageIdRange MessageRange = new NetworkMessageIdRange(
            MessageOwner,
            MESSAGE_ID_BASE,
            MESSAGE_ID_MAX,
            NetworkMessageKind.Module);

        public static bool IsGameplayFrameworkMessageId(ushort messageId)
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

            RegisterMessage<ActorMigrationState>(
                catalog,
                MsgActorMigrationState,
                NetworkChannel.Reliable,
                NetworkConstants.DefaultMaxPayloadSize * 4);
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

            if (!IsGameplayFrameworkMessageId(messageId))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(messageId),
                    messageId,
                    $"GameplayFramework message ids must be inside {MessageRange}.");
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
