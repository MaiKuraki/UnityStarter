using CycloneGames.Networking;

namespace CycloneGames.RPGFoundation.Projectile.Networking
{
    public struct ProjectileManifestHandshakeMessage : INetworkProtocolHandshakeMessage
    {
        public ulong ProtocolFingerprint;
        public ushort MessageIdMin;
        public ushort MessageIdMax;
        public ushort FeatureMask;

        public ProjectileManifestHandshakeMessage(
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
            return NetworkProtocolHandshake.IsCompatible(this, ProjectileNetworkProtocol.Module);
        }

        public static ProjectileManifestHandshakeMessage CreateDefault()
        {
            return new ProjectileManifestHandshakeMessage(
                ProjectileNetworkProtocol.ProtocolFingerprint,
                ProjectileNetworkProtocol.MESSAGE_ID_BASE,
                ProjectileNetworkProtocol.MESSAGE_ID_MAX,
                ProjectileNetworkProtocol.DEFAULT_FEATURE_MASK);
        }
    }
}
