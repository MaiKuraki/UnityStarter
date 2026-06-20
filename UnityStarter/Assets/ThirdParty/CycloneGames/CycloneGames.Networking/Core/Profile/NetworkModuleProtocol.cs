using System;

namespace CycloneGames.Networking
{
    /// <summary>
    /// Shared scaffolding for a domain networking module's protocol.
    /// </summary>
    /// <remarks>
    /// Every CycloneGames *.Networking package used to copy-paste the same module logic:
    /// message-id range containment checks, protocol-version support checks, catalog resolution
    /// from the runtime context, manifest registration, and idempotent per-message registration.
    /// Those copies had already drifted (for example one module registered the extra range via
    /// <see cref="INetworkMessageCatalog.RegisterRange"/> while another used
    /// <see cref="INetworkMessageCatalog.RegisterModuleRange"/>). This type centralizes the logic so
    /// each domain protocol becomes a thin facade that only declares its owner, version window,
    /// id range, and manifest, and forwards the rest here.
    /// </remarks>
    public sealed class NetworkModuleProtocol
    {
        public NetworkModuleProtocol(NetworkProtocolManifest manifest, NetworkProtocolVersion version)
        {
            Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            Version = version;
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
        /// Resolve the message catalog from a manager's runtime context and register this module's manifest.
        /// Returns false when the manager does not expose a runtime context or a catalog service.
        /// </summary>
        public bool TryRegister(INetworkManager networkManager)
        {
            if (networkManager == null)
            {
                return false;
            }

            if (networkManager is not INetworkRuntimeContextProvider provider || provider.RuntimeContext == null)
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

            catalog.RegisterProtocolManifest(Manifest);
        }

        /// <summary>
        /// Idempotently register a single module-owned message id that is not part of the static
        /// manifest (for example project-specific extensions inside the module's reserved range).
        /// Re-registering an identical descriptor is a no-op; a conflicting descriptor throws.
        /// </summary>
        public void RegisterMessage<T>(
            INetworkMessageCatalog catalog,
            ushort messageId,
            NetworkChannel channel = NetworkChannel.Reliable,
            int maxPayloadSize = NetworkConstants.DefaultMaxPayloadSize) where T : struct
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (!ContainsMessageId(messageId))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(messageId),
                    messageId,
                    $"{Owner} message ids must be inside {MessageRange}.");
            }

            catalog.RegisterModuleRange(MessageRange);

            NetworkMessageDescriptor descriptor = NetworkMessageDescriptor.Create<T>(
                messageId,
                Owner,
                NetworkMessageKind.Module,
                channel,
                maxPayloadSize);

            if (catalog.TryRegister(descriptor))
            {
                return;
            }

            if (catalog.TryGet(messageId, out NetworkMessageDescriptor existing)
                && existing.SchemaHash == descriptor.SchemaHash
                && string.Equals(existing.Owner, descriptor.Owner, StringComparison.Ordinal)
                && string.Equals(existing.Name, descriptor.Name, StringComparison.Ordinal)
                && existing.Kind == descriptor.Kind
                && existing.DefaultChannel == descriptor.DefaultChannel
                && existing.MaxPayloadSize == descriptor.MaxPayloadSize)
            {
                return;
            }

            throw new InvalidOperationException($"Message id {messageId} is already registered by {existing.Owner}:{existing.Name}.");
        }
    }
}
