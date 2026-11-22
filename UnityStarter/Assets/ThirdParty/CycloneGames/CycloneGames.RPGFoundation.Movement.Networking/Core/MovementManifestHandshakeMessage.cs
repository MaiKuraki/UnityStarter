using CycloneGames.Networking;

namespace CycloneGames.RPGFoundation.Movement.Networking
{
    public struct MovementManifestHandshakeMessage : INetworkProtocolHandshakeMessage
    {
        public ulong ProtocolFingerprint;
        public ushort MessageIdMin;
        public ushort MessageIdMax;
        public ushort FeatureMask;

        public MovementManifestHandshakeMessage(
            ulong protocolFingerprint,
            ushort messageIdMin,
            ushort messageIdMax,
            ushort featureMask)
        {
            ProtocolFingerprint = protocolFingerprint;
            MessageIdMin = messageIdMin;
            MessageIdMax = messageIdMax;
            FeatureMask = featureMask;
        }

        public bool IsValid
        {
            get
            {
                return ProtocolFingerprint != 0UL
                       && MessageIdMin <= MessageIdMax
                       && NetworkMessageRanges.Module.Contains(MessageIdMin)
                       && NetworkMessageRanges.Module.Contains(MessageIdMax);
            }
        }

        ulong INetworkProtocolHandshakeMessage.ProtocolFingerprint => ProtocolFingerprint;
        ulong INetworkProtocolHandshakeMessage.DomainStateHash => 0UL;

        public bool IsCompatibleWithLocalProtocol()
        {
            return NetworkProtocolHandshake.IsCompatible(this, MovementNetworkProtocol.Module);
        }

        public static MovementManifestHandshakeMessage CreateDefault()
        {
            return new MovementManifestHandshakeMessage(
                MovementNetworkProtocol.ProtocolFingerprint,
                MovementNetworkProtocol.MESSAGE_ID_BASE,
                MovementNetworkProtocol.MESSAGE_ID_MAX,
                MovementNetworkProtocol.DEFAULT_FEATURE_MASK);
        }
    }
}
