using CycloneGames.Networking;

namespace CycloneGames.RPGFoundation.Movement.Networking
{
    // DomainStateHash, WireLayoutDigest and WireCodecDigest are independent
    // facts; a mismatch in any one of them rejects the session.
    public struct MovementManifestHandshakeMessage : INetworkProtocolHandshakeMessage
    {
        public ulong ProtocolFingerprint;
        public ulong WireLayoutDigest;
        public ulong WireCodecDigest;
        public ulong DomainStateHash;
        public ushort MessageIdMin;
        public ushort MessageIdMax;
        public ushort FeatureMask;

        public MovementManifestHandshakeMessage(
            ulong protocolFingerprint,
            ulong wireLayoutDigest,
            ulong wireCodecDigest,
            ulong domainStateHash,
            ushort messageIdMin,
            ushort messageIdMax,
            ushort featureMask)
        {
            ProtocolFingerprint = protocolFingerprint;
            WireLayoutDigest = wireLayoutDigest;
            WireCodecDigest = wireCodecDigest;
            DomainStateHash = domainStateHash;
            MessageIdMin = messageIdMin;
            MessageIdMax = messageIdMax;
            FeatureMask = featureMask;
        }

        public bool IsValid
        {
            get
            {
                return ProtocolFingerprint != MovementWireIds.InvalidFingerprint
                       && WireLayoutDigest != 0UL
                       && WireCodecDigest != 0UL
                       && MessageIdMin <= MessageIdMax
                       && NetworkMessageRanges.Module.Contains(MessageIdMin)
                       && NetworkMessageRanges.Module.Contains(MessageIdMax)
                       && FeatureMask == 0;
            }
        }

        ulong INetworkProtocolHandshakeMessage.ProtocolFingerprint
        {
            get { return ProtocolFingerprint; }
        }

        ulong INetworkProtocolHandshakeMessage.DomainStateHash
        {
            get { return DomainStateHash; }
        }

        public NetworkHandshakeResult Negotiate(
            MovementNetworkProtocol protocol,
            ulong localDomainStateHash = 0UL,
            bool requireDomainStateMatch = false)
        {
            if (protocol == null) return NetworkHandshakeResult.Invalid;
            if (!IsValid) return NetworkHandshakeResult.Malformed;
            if (ProtocolFingerprint != protocol.ProtocolFingerprint)
                return NetworkHandshakeResult.FingerprintMismatch;
            if (WireLayoutDigest != protocol.WireLayoutDigest
                || WireCodecDigest != protocol.WireCodecDigest)
                return NetworkHandshakeResult.DomainStateMismatch;
            if (MessageIdMin != MovementWireIds.MessageIdBase
                || MessageIdMax != MovementWireIds.MessageIdMax)
                return NetworkHandshakeResult.Malformed;
            return NetworkProtocolHandshake.Negotiate(
                this, protocol.Module, localDomainStateHash, requireDomainStateMatch);
        }

        public bool IsCompatibleWith(
            MovementNetworkProtocol protocol,
            ulong localDomainStateHash = 0UL,
            bool requireDomainStateMatch = false)
        {
            return Negotiate(protocol, localDomainStateHash, requireDomainStateMatch)
                   == NetworkHandshakeResult.Compatible;
        }

        public static MovementManifestHandshakeMessage CreateFor(
            MovementNetworkProtocol protocol,
            ulong domainStateHash = 0UL)
        {
            return protocol.CreateHandshake(domainStateHash);
        }
    }
}
