using CycloneGames.Networking;

namespace CycloneGames.GameplayAbilities.Networking
{
    /// <summary>
    /// Connection-level protocol handshake for the GameplayAbilities networking module.
    /// </summary>
    /// <remarks>
    /// GAS validates fine-grained runtime drift per object via <c>GASStateSyncMetadata.StateVersion</c>
    /// and state checksums. This handshake is an optional first line of defense at connection time:
    /// it lets a peer reject an incompatible client (mismatched message schema / protocol version)
    /// before any per-object traffic flows. Whether to require it during the connection flow is a
    /// composition-root decision; the per-object checksum path is unaffected either way.
    /// </remarks>
    public struct GASManifestHandshakeMessage : INetworkProtocolHandshake
    {
        public ulong ProtocolFingerprint;
        public byte MinimumSupportedProtocolVersion;
        public byte CurrentProtocolVersion;

        public GASManifestHandshakeMessage(
            ulong protocolFingerprint,
            byte minimumSupportedProtocolVersion,
            byte currentProtocolVersion)
        {
            ProtocolFingerprint = protocolFingerprint;
            MinimumSupportedProtocolVersion = minimumSupportedProtocolVersion;
            CurrentProtocolVersion = currentProtocolVersion;
        }

        ulong INetworkProtocolHandshake.ProtocolFingerprint => ProtocolFingerprint;
        byte INetworkProtocolHandshake.CurrentProtocolVersion => CurrentProtocolVersion;
        byte INetworkProtocolHandshake.MinimumSupportedProtocolVersion => MinimumSupportedProtocolVersion;
        ulong INetworkProtocolHandshake.DomainStateHash => 0UL;

        public NetworkHandshakeResult Negotiate()
        {
            return NetworkProtocolHandshake.Negotiate(this, NetworkedAbilityBridge.Module);
        }

        public bool IsCompatibleWithLocalProtocol()
        {
            return NetworkProtocolHandshake.IsCompatible(this, NetworkedAbilityBridge.Module);
        }

        public static GASManifestHandshakeMessage CreateLocal()
        {
            return new GASManifestHandshakeMessage(
                NetworkedAbilityBridge.ProtocolFingerprint,
                NetworkedAbilityBridge.MIN_SUPPORTED_PROTOCOL_VERSION,
                NetworkedAbilityBridge.PROTOCOL_VERSION);
        }
    }
}
