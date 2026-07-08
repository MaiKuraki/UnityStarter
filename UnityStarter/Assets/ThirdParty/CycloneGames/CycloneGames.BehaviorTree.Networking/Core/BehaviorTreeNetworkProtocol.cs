using CycloneGames.Networking;

namespace CycloneGames.BehaviorTree.Networking
{
    public static class BehaviorTreeNetworkProtocol
    {
        public const string MessageOwner = "CycloneGames.BehaviorTree";

        public const ushort MESSAGE_ID_BASE = 14000;
        public const ushort MESSAGE_ID_MAX = 14999;
        public const ushort MSG_MANIFEST_HANDSHAKE = MESSAGE_ID_BASE;
        public const ushort MSG_FULL_SNAPSHOT = MESSAGE_ID_BASE + 1;
        public const ushort MSG_BLACKBOARD_DELTA = MESSAGE_ID_BASE + 2;
        public const ushort MSG_DESYNC_REPORT = MESSAGE_ID_BASE + 3;
        public const ushort MSG_TICK_CONTROL = MESSAGE_ID_BASE + 4;
        public const ushort MSG_AUTHORITY_TRANSFER = MESSAGE_ID_BASE + 5;

        public const int DEFAULT_MAX_SNAPSHOT_PAYLOAD_SIZE = NetworkConstants.DefaultMaxPayloadSize * 8;
        public const int DEFAULT_MAX_DELTA_PAYLOAD_SIZE = NetworkConstants.DefaultMaxPayloadSize * 2;
        public const int DEFAULT_MAX_CONTROL_PAYLOAD_SIZE = 128;

        public static readonly NetworkModuleProtocol Module = new NetworkModuleProtocol(CreateProtocolManifest());

        public static readonly NetworkProtocolManifest DefaultManifest = Module.Manifest;
        public static readonly NetworkMessageIdRange MessageRange = Module.MessageRange;
        public static readonly ulong ProtocolFingerprint = Module.Fingerprint;

        public static bool IsBehaviorTreeMessageId(ushort messageId)
        {
            return MessageRange.Contains(messageId);
        }

        public static bool IsBuiltInBehaviorTreeMessage(ushort messageId)
        {
            return messageId >= MSG_MANIFEST_HANDSHAKE && messageId <= MSG_AUTHORITY_TRANSFER;
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
                ProtocolId = "CycloneGames.BehaviorTree.Networking"
            };

            builder
                .SetMetadata("module", "BehaviorTree")
                .SetMetadata("snapshot", "RuntimeBehaviorTree blackboard snapshot payload")
                .SetMetadata("delta", "RuntimeBlackboard tracked-key delta payload")
                .AddMessage<BehaviorTreeManifestHandshakeMessage>(
                    MSG_MANIFEST_HANDSHAKE,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_CONTROL_PAYLOAD_SIZE)
                .AddMessage<BehaviorTreeStatePayloadMessage>(
                    MSG_FULL_SNAPSHOT,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_SNAPSHOT_PAYLOAD_SIZE)
                .AddMessage<BehaviorTreeStatePayloadMessage>(
                    MSG_BLACKBOARD_DELTA,
                    NetworkChannel.UnreliableSequenced,
                    DEFAULT_MAX_DELTA_PAYLOAD_SIZE)
                .AddMessage<BehaviorTreeDesyncReportMessage>(
                    MSG_DESYNC_REPORT,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_CONTROL_PAYLOAD_SIZE)
                .AddMessage<BehaviorTreeTickControlMessage>(
                    MSG_TICK_CONTROL,
                    NetworkChannel.Reliable,
                    DEFAULT_MAX_CONTROL_PAYLOAD_SIZE)
                .AddMessage<BehaviorTreeAuthorityTransferMessage>(
                    MSG_AUTHORITY_TRANSFER,
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
