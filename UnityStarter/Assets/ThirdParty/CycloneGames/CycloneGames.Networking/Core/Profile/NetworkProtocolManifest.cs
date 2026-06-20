using System;
using System.Collections.Generic;
using CycloneGames.Hash.Core;

namespace CycloneGames.Networking
{
    public sealed class NetworkProtocolManifest
    {
        private const ulong FNV_PRIME = Fnv1a64.Prime;
        private const int FINGERPRINT_BYTE_COUNT = 8;

        private readonly NetworkMessageDescriptor[] _messages;
        private readonly Dictionary<string, string> _metadata;

        internal NetworkProtocolManifest(NetworkProtocolManifestBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            ProtocolId = string.IsNullOrEmpty(builder.ProtocolId) ? builder.Owner : builder.ProtocolId;
            Owner = builder.Owner ?? string.Empty;
            CurrentVersion = builder.CurrentVersion;
            MinimumSupportedVersion = builder.MinimumSupportedVersion;
            MessageRange = builder.MessageRange;
            _messages = builder.Messages.ToArray();
            _metadata = new Dictionary<string, string>(builder.Metadata, StringComparer.Ordinal);
            Validate();
            Fingerprint = ComputeFingerprint();
        }

        public string ProtocolId { get; }
        public string Owner { get; }
        public int CurrentVersion { get; }
        public int MinimumSupportedVersion { get; }
        public NetworkMessageIdRange MessageRange { get; }
        public ulong Fingerprint { get; }

        public IReadOnlyList<NetworkMessageDescriptor> Messages
        {
            get
            {
                return _messages;
            }
        }

        public IReadOnlyDictionary<string, string> Metadata
        {
            get
            {
                return _metadata;
            }
        }

        public bool IsCompatibleWith(int remoteVersion)
        {
            return remoteVersion >= MinimumSupportedVersion && remoteVersion <= CurrentVersion;
        }

        private void Validate()
        {
            if (string.IsNullOrEmpty(Owner))
            {
                throw new ArgumentException("Protocol owner must not be null or empty.");
            }

            if (CurrentVersion <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(CurrentVersion));
            }

            if (MinimumSupportedVersion <= 0 || MinimumSupportedVersion > CurrentVersion)
            {
                throw new ArgumentOutOfRangeException(nameof(MinimumSupportedVersion));
            }

            if (string.IsNullOrEmpty(MessageRange.Name)
                || !string.Equals(MessageRange.Name, Owner, StringComparison.Ordinal))
            {
                throw new ArgumentException("Protocol message range must use the protocol owner name.");
            }

            if (MessageRange.Min > MessageRange.Max)
            {
                throw new ArgumentException("Protocol message range is invalid.");
            }

            if (!NetworkMessageRanges.ContainsRange(MessageRange))
            {
                throw new ArgumentException($"Protocol message range {MessageRange} is outside the reserved global range for kind {MessageRange.Kind}.");
            }

            for (int i = 0; i < _messages.Length; i++)
            {
                NetworkMessageDescriptor descriptor = _messages[i];
                if (!descriptor.IsValid)
                {
                    throw new ArgumentException("Protocol message descriptor is invalid.");
                }

                if (!MessageRange.Contains(descriptor.MessageId))
                {
                    throw new ArgumentException($"Message id {descriptor.MessageId} is outside protocol range {MessageRange}.");
                }

                if (!string.Equals(descriptor.Owner, Owner, StringComparison.Ordinal))
                {
                    throw new ArgumentException($"Message id {descriptor.MessageId} belongs to {descriptor.Owner}, not {Owner}.");
                }

                if ((descriptor.Kind & MessageRange.Kind) == 0)
                {
                    throw new ArgumentException($"Message id {descriptor.MessageId} kind {descriptor.Kind} does not match protocol range kind {MessageRange.Kind}.");
                }

                for (int j = i + 1; j < _messages.Length; j++)
                {
                    if (_messages[j].MessageId == descriptor.MessageId)
                    {
                        throw new ArgumentException($"Duplicate protocol message id {descriptor.MessageId}.");
                    }
                }
            }
        }

        private ulong ComputeFingerprint()
        {
            ulong hash = NetworkMessageCatalog.ComputeStableHash(Owner);
            hash = Combine(hash, NetworkMessageCatalog.ComputeStableHash(ProtocolId));
            hash = Combine(hash, CurrentVersion);
            hash = Combine(hash, MinimumSupportedVersion);
            hash = Combine(hash, MessageRange.Min);
            hash = Combine(hash, MessageRange.Max);
            hash = Combine(hash, (ushort)MessageRange.Kind);

            var ids = new ushort[_messages.Length];
            for (int i = 0; i < _messages.Length; i++)
            {
                ids[i] = _messages[i].MessageId;
            }

            Array.Sort(ids);
            for (int i = 0; i < ids.Length; i++)
            {
                NetworkMessageDescriptor descriptor = Find(ids[i]);
                hash = Combine(hash, descriptor.MessageId);
                hash = Combine(hash, (ushort)descriptor.Kind);
                hash = Combine(hash, (ushort)descriptor.DefaultChannel);
                hash = Combine(hash, descriptor.SchemaHash);
                hash = Combine(hash, descriptor.MaxPayloadSize);
                hash = Combine(hash, NetworkMessageCatalog.ComputeStableHash(descriptor.Name));
            }

            return hash == 0UL ? NetworkMessageCatalog.ComputeStableHash(Owner) : hash;
        }

        private NetworkMessageDescriptor Find(ushort messageId)
        {
            for (int i = 0; i < _messages.Length; i++)
            {
                if (_messages[i].MessageId == messageId)
                {
                    return _messages[i];
                }
            }

            return default;
        }

        private static ulong Combine(ulong hash, int value)
        {
            unchecked
            {
                hash ^= (uint)value;
                hash *= FNV_PRIME;
                hash ^= (uint)(value >> 16);
                hash *= FNV_PRIME;
                return hash;
            }
        }

        private static ulong Combine(ulong hash, ulong value)
        {
            unchecked
            {
                for (int i = 0; i < FINGERPRINT_BYTE_COUNT; i++)
                {
                    hash ^= (byte)(value >> (i * 8));
                    hash *= FNV_PRIME;
                }

                return hash;
            }
        }
    }

    public sealed class NetworkProtocolManifestBuilder
    {
        private const int DEFAULT_MESSAGE_CAPACITY = 8;
        private const int DEFAULT_PROTOCOL_VERSION = 1;

        internal readonly List<NetworkMessageDescriptor> Messages = new List<NetworkMessageDescriptor>(DEFAULT_MESSAGE_CAPACITY);
        internal readonly Dictionary<string, string> Metadata = new Dictionary<string, string>(StringComparer.Ordinal);

        public NetworkProtocolManifestBuilder(string owner, ushort minMessageId, ushort maxMessageId, NetworkMessageKind kind)
        {
            Owner = owner ?? string.Empty;
            MessageRange = new NetworkMessageIdRange(Owner, minMessageId, maxMessageId, kind);
        }

        public string ProtocolId { get; set; }
        public string Owner { get; }
        public int CurrentVersion { get; set; } = DEFAULT_PROTOCOL_VERSION;
        public int MinimumSupportedVersion { get; set; } = DEFAULT_PROTOCOL_VERSION;
        public NetworkMessageIdRange MessageRange { get; }

        public NetworkProtocolManifestBuilder Add(NetworkMessageDescriptor descriptor)
        {
            Messages.Add(descriptor);
            return this;
        }

        public NetworkProtocolManifestBuilder AddMessage<T>(
            ushort messageId,
            NetworkChannel defaultChannel = NetworkChannel.Reliable,
            int maxPayloadSize = NetworkConstants.DefaultMaxPayloadSize) where T : struct
        {
            return Add(NetworkMessageDescriptor.Create<T>(
                messageId,
                Owner,
                MessageRange.Kind,
                defaultChannel,
                maxPayloadSize));
        }

        public NetworkProtocolManifestBuilder SetMetadata(string key, string value)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("Protocol metadata key must not be null or empty.", nameof(key));
            }

            Metadata[key] = value ?? string.Empty;
            return this;
        }

        public NetworkProtocolManifest Build()
        {
            return new NetworkProtocolManifest(this);
        }
    }

    public static class NetworkProtocolManifestCatalogExtensions
    {
        public static void RegisterProtocolManifest(
            this INetworkMessageCatalog catalog,
            NetworkProtocolManifest manifest)
        {
            if (!TryRegisterProtocolManifest(catalog, manifest))
            {
                throw new InvalidOperationException($"Protocol manifest {manifest?.ProtocolId ?? string.Empty} conflicts with the message catalog.");
            }
        }

        public static bool TryRegisterProtocolManifest(
            this INetworkMessageCatalog catalog,
            NetworkProtocolManifest manifest)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            for (int i = 0; i < manifest.Messages.Count; i++)
            {
                NetworkMessageDescriptor descriptor = manifest.Messages[i];
                if (catalog.TryGet(descriptor.MessageId, out NetworkMessageDescriptor existing)
                    && !DescriptorsMatch(existing, descriptor))
                {
                    return false;
                }
            }

            if (!catalog.TryRegisterRange(manifest.MessageRange))
            {
                return false;
            }

            for (int i = 0; i < manifest.Messages.Count; i++)
            {
                NetworkMessageDescriptor descriptor = manifest.Messages[i];
                if (catalog.TryGet(descriptor.MessageId, out NetworkMessageDescriptor existing))
                {
                    if (!DescriptorsMatch(existing, descriptor))
                    {
                        return false;
                    }

                    continue;
                }

                if (!catalog.TryRegister(descriptor))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool DescriptorsMatch(
            in NetworkMessageDescriptor left,
            in NetworkMessageDescriptor right)
        {
            return left.MessageId == right.MessageId
                   && string.Equals(left.Name, right.Name, StringComparison.Ordinal)
                   && string.Equals(left.Owner, right.Owner, StringComparison.Ordinal)
                   && left.SchemaHash == right.SchemaHash
                   && left.Kind == right.Kind
                   && left.DefaultChannel == right.DefaultChannel
                   && left.MaxPayloadSize == right.MaxPayloadSize;
        }
    }
}
