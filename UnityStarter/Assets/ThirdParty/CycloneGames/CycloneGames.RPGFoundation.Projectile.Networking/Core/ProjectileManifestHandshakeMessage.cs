using CycloneGames.Networking;

namespace CycloneGames.RPGFoundation.Projectile.Networking
{
    public struct ProjectileManifestHandshakeMessage : INetworkProtocolHandshakeMessage
    {
        public byte ProtocolVersion;
        public byte MinimumSupportedProtocolVersion;
        public ulong ProtocolFingerprint;
        public ushort MessageIdMin;
        public ushort MessageIdMax;
        public ushort FeatureMask;

        public ProjectileManifestHandshakeMessage(
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

        ulong INetworkProtocolHandshakeMessage.ProtocolFingerprint => ProtocolFingerprint;
        byte INetworkProtocolHandshakeMessage.CurrentProtocolVersion => ProtocolVersion;
        byte INetworkProtocolHandshakeMessage.MinimumSupportedProtocolVersion => MinimumSupportedProtocolVersion;
        ulong INetworkProtocolHandshakeMessage.DomainStateHash => 0UL;

        public bool IsCompatibleWithLocalProtocol()
        {
            return NetworkProtocolHandshake.IsCompatible(this, ProjectileNetworkProtocol.Module);
        }

        public static ProjectileManifestHandshakeMessage CreateDefault()
        {
            return new ProjectileManifestHandshakeMessage(
                ProjectileNetworkProtocol.PROTOCOL_VERSION,
                ProjectileNetworkProtocol.MIN_SUPPORTED_PROTOCOL_VERSION,
                ProjectileNetworkProtocol.ProtocolFingerprint,
                ProjectileNetworkProtocol.MESSAGE_ID_BASE,
                ProjectileNetworkProtocol.MESSAGE_ID_MAX,
                ProjectileNetworkProtocol.DEFAULT_FEATURE_MASK);
        }
    }
}
