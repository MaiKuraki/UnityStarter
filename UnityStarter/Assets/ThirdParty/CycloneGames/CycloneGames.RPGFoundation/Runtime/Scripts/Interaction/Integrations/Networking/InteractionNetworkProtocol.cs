using CycloneGames.Networking;

namespace CycloneGames.RPGFoundation.Runtime.Interaction.Integrations.Networking
{
    public static class InteractionNetworkProtocol
    {
        public const byte PROTOCOL_VERSION = 1;
        public const ushort MESSAGE_ID_BASE = NetworkConstants.UserMsgIdMin + 512;
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

        public static bool IsInteractionMessage(ushort messageId)
        {
            return messageId >= REQUEST_MESSAGE_ID && messageId <= DETERMINISTIC_REQUEST_MESSAGE_ID;
        }

        public static bool IsUserMessageId(ushort messageId)
        {
            return messageId >= NetworkConstants.UserMsgIdMin && messageId <= NetworkConstants.MaxMessageId;
        }
    }
}
