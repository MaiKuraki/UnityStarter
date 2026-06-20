using CycloneGames.GameplayFramework.Runtime;
using CycloneGames.Networking;

namespace CycloneGames.GameplayFramework.Networking
{
    public static class GameplayFrameworkNetworkProtocol
    {
        public const string MessageOwner = "CycloneGames.GameplayFramework";
        public const byte PROTOCOL_VERSION = 1;

        public const ushort MESSAGE_ID_BASE = 11000;
        public const ushort MESSAGE_ID_MAX = 11999;
        public const ushort MsgActorMigrationState = MESSAGE_ID_BASE;
        public const ushort MsgDamageRequest = MESSAGE_ID_BASE + 1;
        public const ushort MsgDamageResult = MESSAGE_ID_BASE + 2;

        public static readonly NetworkMessageIdRange MessageRange = new NetworkMessageIdRange(
            MessageOwner,
            MESSAGE_ID_BASE,
            MESSAGE_ID_MAX,
            NetworkMessageKind.Module);

        public static readonly NetworkProtocolManifest DefaultManifest = CreateProtocolManifest();

        public static readonly NetworkModuleProtocol Module = new NetworkModuleProtocol(
            DefaultManifest,
            NetworkProtocolVersion.Create(PROTOCOL_VERSION));

        public static ulong ProtocolFingerprint => Module.Fingerprint;

        public static bool IsGameplayFrameworkMessageId(ushort messageId)
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
                ProtocolId = "CycloneGames.GameplayFramework.Networking",
                CurrentVersion = PROTOCOL_VERSION,
                MinimumSupportedVersion = PROTOCOL_VERSION
            };

            builder
                .SetMetadata("module", "GameplayFramework")
                .AddMessage<ActorMigrationState>(MsgActorMigrationState, NetworkChannel.Reliable, NetworkConstants.DefaultMaxPayloadSize * 4)
                .AddMessage<DamageRequestMessage>(MsgDamageRequest, NetworkChannel.Reliable)
                .AddMessage<DamageResultMessage>(MsgDamageResult, NetworkChannel.Reliable);

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
