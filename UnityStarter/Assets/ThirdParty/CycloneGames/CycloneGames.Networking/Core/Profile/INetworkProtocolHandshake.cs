using System;

namespace CycloneGames.Networking
{
    /// <summary>
    /// Outcome of negotiating a remote protocol handshake against the local module protocol.
    /// </summary>
    /// <remarks>
    /// A diagnosable enum (rather than a bare bool) lets callers log, classify, and surface the
    /// exact rejection reason, mirroring the per-object drift reporting GAS already uses.
    /// </remarks>
    public enum NetworkHandshakeResult : byte
    {
        Compatible = 0,
        Malformed = 1,
        FingerprintMismatch = 2,
        VersionIncompatible = 3,
        DomainStateMismatch = 4
    }

    /// <summary>
    /// Common contract for a domain networking module's manifest-handshake message.
    /// </summary>
    /// <remarks>
    /// Implementing this lets the version/fingerprint negotiation be written once in
    /// <see cref="NetworkProtocolHandshake"/> instead of being copy-pasted into every per-module
    /// handshake struct. <see cref="DomainStateHash"/> carries an optional module-specific
    /// fingerprint that must also match for the peers to be compatible (behavior-tree template hash,
    /// AI perception profile hash, gameplay-tag manifest hash, ...); modules without such a
    /// requirement expose 0.
    /// </remarks>
    public interface INetworkProtocolHandshake
    {
        ulong ProtocolFingerprint { get; }
        byte CurrentProtocolVersion { get; }
        byte MinimumSupportedProtocolVersion { get; }
        ulong DomainStateHash { get; }
    }

    /// <summary>
    /// Shared, allocation-free negotiation for <see cref="INetworkProtocolHandshake"/> payloads.
    /// </summary>
    /// <remarks>
    /// The generic <c>in</c> constraint keeps value-type handshakes unboxed (zero-GC) while still
    /// reusing a single negotiation implementation across every domain module.
    /// </remarks>
    public static class NetworkProtocolHandshake
    {
        /// <summary>
        /// True when the handshake fields form a valid version window with a non-zero fingerprint.
        /// </summary>
        public static bool IsWellFormed<T>(in T handshake) where T : INetworkProtocolHandshake
        {
            return handshake.ProtocolFingerprint != 0UL
                   && handshake.CurrentProtocolVersion > 0
                   && handshake.MinimumSupportedProtocolVersion > 0
                   && handshake.MinimumSupportedProtocolVersion <= handshake.CurrentProtocolVersion;
        }

        /// <summary>
        /// Negotiate a remote handshake against the local module protocol. When
        /// <paramref name="requireDomainStateMatch"/> is set, the remote
        /// <see cref="INetworkProtocolHandshake.DomainStateHash"/> must also equal
        /// <paramref name="localDomainStateHash"/>.
        /// </summary>
        public static NetworkHandshakeResult Negotiate<T>(
            in T remote,
            NetworkModuleProtocol local,
            ulong localDomainStateHash = 0UL,
            bool requireDomainStateMatch = false) where T : INetworkProtocolHandshake
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

            var remoteVersion = new NetworkProtocolVersion(
                remote.CurrentProtocolVersion,
                remote.MinimumSupportedProtocolVersion);

            if (!local.Version.IsCompatibleWith(remoteVersion))
            {
                return NetworkHandshakeResult.VersionIncompatible;
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
            bool requireDomainStateMatch = false) where T : INetworkProtocolHandshake
        {
            return Negotiate(in remote, local, localDomainStateHash, requireDomainStateMatch)
                   == NetworkHandshakeResult.Compatible;
        }
    }
}
