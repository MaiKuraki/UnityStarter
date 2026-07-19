using System;

namespace CycloneGames.Networking
{
    /// <summary>
    /// Provides the runtime-facing operations for one immutable, versioned protocol manifest.
    /// </summary>
    public sealed class NetworkModuleProtocol
    {
        public NetworkModuleProtocol(NetworkProtocolManifest manifest)
        {
            Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));

            if (manifest.CurrentVersion > byte.MaxValue || manifest.MinimumSupportedVersion > byte.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(manifest),
                    "Protocol version window must fit in a byte to stay wire-compatible.");
            }

            Version = new NetworkProtocolVersion(
                (byte)manifest.CurrentVersion,
                (byte)manifest.MinimumSupportedVersion);
        }

        public NetworkProtocolManifest Manifest { get; }
        public NetworkProtocolVersion Version { get; }

        public string Owner => Manifest.Owner;
        public string ProtocolId => Manifest.ProtocolId;
        public NetworkMessageIdRange MessageRange => Manifest.MessageRange;
        public ulong Fingerprint => Manifest.Fingerprint;

        /// <summary>True when <paramref name="messageId"/> belongs to this module's reserved id range.</summary>
        public bool ContainsMessageId(ushort messageId)
        {
            return MessageRange.Contains(messageId);
        }

        /// <summary>True when a peer advertising <paramref name="protocolVersion"/> is interoperable with this module.</summary>
        public bool IsSupportedProtocolVersion(byte protocolVersion)
        {
            return Version.Supports(protocolVersion);
        }

        /// <summary>
        /// Resolve the message catalog from an endpoint's runtime context and register this module's manifest.
        /// Returns false when the endpoint does not expose a runtime context or a catalog service.
        /// </summary>
        public bool TryRegister(INetworkMessageEndpoint messageEndpoint)
        {
            if (messageEndpoint == null)
            {
                return false;
            }

            if (messageEndpoint is not INetworkRuntimeContextProvider provider || provider.RuntimeContext == null)
            {
                return false;
            }

            if (!provider.RuntimeContext.TryGetService(out INetworkMessageCatalog catalog))
            {
                return false;
            }

            Register(catalog);
            return true;
        }

        /// <summary>Register this module's full protocol manifest into <paramref name="catalog"/>.</summary>
        public void Register(INetworkMessageCatalog catalog)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (!catalog.TryRegisterProtocolManifest(Manifest))
            {
                throw new InvalidOperationException(
                    $"Protocol manifest {Manifest.ProtocolId} conflicts with the message catalog.");
            }
        }
    }
}
