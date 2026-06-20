using CycloneGames.Networking;

namespace CycloneGames.RPGFoundation.Movement.Networking
{
    public struct MovementManifestHandshakeMessage : INetworkProtocolHandshake
    {
        public byte ProtocolVersion;
        public byte MinimumSupportedProtocolVersion;
        public ulong ProtocolFingerprint;
        public ushort MessageIdMin;
        public ushort MessageIdMax;
        public ushort FeatureMask;

        public MovementManifestHandshakeMessage(
            byte protocolVersion,
            byte minimumSupportedProtocolVersion,
            ulong protocolFingerprint,
            ushort messageIdMin,
            ushort messageIdMax,
            ushort featureMask)
        {
            ProtocolVersion = protocolVersion;
            MinimumSupportedProtocolVersion = minimumSupportedProtocolVersion;
            ProtocolFingerprint = protocolFingerprint;
            MessageIdMin = messageIdMin;
            MessageIdMax = messageIdMax;
            FeatureMask = featureMask;
        }

        public bool IsValid
        {
            get
            {
                return ProtocolVersion > 0
                       && MinimumSupportedProtocolVersion > 0
                       && MinimumSupportedProtocolVersion <= ProtocolVersion
                       && ProtocolFingerprint != 0UL
                       && MessageIdMin <= MessageIdMax
                       && NetworkMessageRanges.Module.Contains(MessageIdMin)
                       && NetworkMessageRanges.Module.Contains(MessageIdMax);
            }
        }

        ulong INetworkProtocolHandshake.ProtocolFingerprint => ProtocolFingerprint;
        byte INetworkProtocolHandshake.CurrentProtocolVersion => ProtocolVersion;
        byte INetworkProtocolHandshake.MinimumSupportedProtocolVersion => MinimumSupportedProtocolVersion;
        ulong INetworkProtocolHandshake.DomainStateHash => 0UL;

        public bool IsCompatibleWithLocalProtocol()
        {
            return NetworkProtocolHandshake.IsCompatible(this, MovementNetworkProtocol.Module);
        }

        public static MovementManifestHandshakeMessage CreateDefault()
        {
            return new MovementManifestHandshakeMessage(
                MovementNetworkProtocol.PROTOCOL_VERSION,
                MovementNetworkProtocol.MIN_SUPPORTED_PROTOCOL_VERSION,
                MovementNetworkProtocol.ProtocolFingerprint,
                MovementNetworkProtocol.MESSAGE_ID_BASE,
                MovementNetworkProtocol.MESSAGE_ID_MAX,
                MovementNetworkProtocol.DEFAULT_FEATURE_MASK);
        }
    }
}
