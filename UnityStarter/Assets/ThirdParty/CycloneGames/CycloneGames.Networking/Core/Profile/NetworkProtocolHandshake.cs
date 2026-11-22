using System;

namespace CycloneGames.Networking
{
    /// <summary>
    /// Outcome of matching a remote protocol handshake against the local module protocol.
    /// </summary>
    public enum NetworkHandshakeResult : byte
    {
        Invalid = 0,
        Compatible = 1,
        Malformed = 2,
        FingerprintMismatch = 3,
        DomainStateMismatch = 5
    }

    /// <summary>
    /// Common contract for a domain networking module's manifest-handshake message.
    /// Peers are compatible when both fingerprints match exactly.
    /// </summary>
    public interface INetworkProtocolHandshakeMessage
    {
        ulong ProtocolFingerprint { get; }
        ulong DomainStateHash { get; }
    }

    /// <summary>Shared exact-match negotiation for handshake payloads.</summary>
    public static class NetworkProtocolHandshake
    {
        /// <summary>True when the handshake carries a non-zero protocol fingerprint.</summary>
        public static bool IsWellFormed<T>(in T handshake) where T : INetworkProtocolHandshakeMessage
        {
            return handshake.ProtocolFingerprint != 0UL;
        }

        /// <summary>
        /// Match a remote handshake against the local module protocol. When
        /// <paramref name="requireDomainStateMatch"/> is set, the remote
        /// <see cref="INetworkProtocolHandshakeMessage.DomainStateHash"/> must equal
        /// <paramref name="localDomainStateHash"/> as well.
        /// </summary>
        public static NetworkHandshakeResult Negotiate<T>(
            in T remote,
            NetworkModuleProtocol local,
            ulong localDomainStateHash = 0UL,
            bool requireDomainStateMatch = false) where T : INetworkProtocolHandshakeMessage
        {
            if (local == null)
            {
                throw new ArgumentNullException(nameof(local));
            }

            if (!IsWellFormed(in remote))
            {
                return NetworkHandshakeResult.Malformed;
            }

            if (remote.ProtocolFingerprint != local.Fingerprint)
            {
                return NetworkHandshakeResult.FingerprintMismatch;
            }

            if (requireDomainStateMatch && remote.DomainStateHash != localDomainStateHash)
            {
                return NetworkHandshakeResult.DomainStateMismatch;
            }

            return NetworkHandshakeResult.Compatible;
        }

        /// <summary>Boolean convenience form of <see cref="Negotiate{T}"/>.</summary>
        public static bool IsCompatible<T>(
            in T remote,
            NetworkModuleProtocol local,
            ulong localDomainStateHash = 0UL,
            bool requireDomainStateMatch = false) where T : INetworkProtocolHandshakeMessage
        {
            return Negotiate(in remote, local, localDomainStateHash, requireDomainStateMatch)
                   == NetworkHandshakeResult.Compatible;
        }
    }
}
