using System;
using System.Collections.Generic;
using CycloneGames.Hash.Core;
using CycloneGames.Networking.Security;

namespace CycloneGames.Networking
{
    /// <summary>
    /// Callback-lifetime view over one validated canonical network message.
    /// The byte span becomes invalid when the handler returns and must not be retained.
    /// </summary>
    public readonly ref struct NetworkMessagePayload
    {
        public NetworkMessagePayload(
            INetConnection connection,
            NetworkMessageDirection direction,
            in NetworkEnvelopeHeader header,
            ReadOnlySpan<byte> bytes)
        {
            if (direction == NetworkMessageDirection.Unknown)
                throw new ArgumentOutOfRangeException(nameof(direction));
            if (bytes.Length != header.PayloadLength)
                throw new ArgumentException("Payload length must match the validated network header.", nameof(bytes));

            Connection = connection;
            Direction = direction;
            Header = header;
            Bytes = bytes;
        }

        public INetConnection Connection { get; }
        public NetworkMessageDirection Direction { get; }
        public NetworkEnvelopeHeader Header { get; }
        public ReadOnlySpan<byte> Bytes { get; }
    }

    /// <summary>
    /// Handles a validated canonical payload without generic serialization or reflection.
    /// </summary>
    public delegate void NetworkMessageHandler(in NetworkMessagePayload payload);

    /// <summary>
    /// Generation-tagged handler registration. Releasing a copied, stale, or already released
    /// lease is harmless and cannot remove a newer handler for the same message id. Dispose on
    /// the owning endpoint thread before the endpoint shuts down.
    /// </summary>
    public readonly struct NetworkMessageHandlerLease : IDisposable, IEquatable<NetworkMessageHandlerLease>
    {
        private readonly NetworkMessageHandlerRegistry _owner;

        internal NetworkMessageHandlerLease(
            NetworkMessageHandlerRegistry owner,
            ushort messageId,
            ulong generation)
        {
            _owner = owner;
            MessageId = messageId;
            Generation = generation;
        }

        public ushort MessageId { get; }
        public ulong Generation { get; }
        public bool IsValid => _owner != null && Generation != 0UL;

        public void Dispose()
        {
            _owner?.TryRelease(MessageId, Generation);
        }

        public bool Equals(NetworkMessageHandlerLease other)
        {
            return ReferenceEquals(_owner, other._owner)
                   && MessageId == other.MessageId
                   && Generation == other.Generation;
        }

        public override bool Equals(object obj)
        {
            return obj is NetworkMessageHandlerLease other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = _owner != null ? _owner.GetHashCode() : 0;
                hashCode = (hashCode * 397) ^ MessageId.GetHashCode();
                return (hashCode * 397) ^ Generation.GetHashCode();
            }
        }

        public static bool operator ==(NetworkMessageHandlerLease left, NetworkMessageHandlerLease right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(NetworkMessageHandlerLease left, NetworkMessageHandlerLease right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// Bounded-by-registration-count handler table shared by concrete endpoints. Registration,
    /// dispatch, lease release, and clear must run on the endpoint owner thread. Dispatch performs
    /// one dictionary lookup and does not allocate or acquire a lock.
    /// </summary>
    public sealed class NetworkMessageHandlerRegistry
    {
        public const int DefaultMaxHandlers = 1024;

        private readonly struct HandlerEntry
        {
            public HandlerEntry(NetworkMessageHandler handler, ulong generation)
            {
                Handler = handler;
                Generation = generation;
            }

            public NetworkMessageHandler Handler { get; }
            public ulong Generation { get; }
        }

        private readonly Dictionary<ushort, HandlerEntry> _handlers;
        private readonly int _maxHandlers;
        private ulong _nextGeneration;

        public NetworkMessageHandlerRegistry(
            int capacity = 32,
            int maxHandlers = DefaultMaxHandlers)
        {
            if (capacity < 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            if (maxHandlers <= 0 || maxHandlers > ushort.MaxValue + 1)
                throw new ArgumentOutOfRangeException(nameof(maxHandlers));
            if (capacity > maxHandlers)
                throw new ArgumentOutOfRangeException(nameof(capacity));

            _handlers = new Dictionary<ushort, HandlerEntry>(capacity);
            _maxHandlers = maxHandlers;
        }

        public int Count => _handlers.Count;
        public int MaxHandlers => _maxHandlers;

        public NetworkMessageHandlerLease Register(ushort messageId, NetworkMessageHandler handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            if (_handlers.ContainsKey(messageId))
            {
                throw new InvalidOperationException(
                    $"A handler is already registered for network message id {messageId}.");
            }

            if (_handlers.Count >= _maxHandlers)
            {
                throw new InvalidOperationException(
                    $"The network message handler capacity of {_maxHandlers} has been reached.");
            }

            ulong generation = unchecked(++_nextGeneration);
            if (generation == 0UL)
                generation = unchecked(++_nextGeneration);

            _handlers.Add(messageId, new HandlerEntry(handler, generation));
            return new NetworkMessageHandlerLease(this, messageId, generation);
        }

        public bool TryDispatch(in NetworkMessagePayload payload)
        {
            if (!_handlers.TryGetValue(payload.Header.MessageId, out HandlerEntry entry))
                return false;

            entry.Handler(in payload);
            return true;
        }

        public void Clear()
        {
            _handlers.Clear();
        }

        internal bool TryRelease(ushort messageId, ulong generation)
        {
            if (generation == 0UL)
                return false;

            if (!_handlers.TryGetValue(messageId, out HandlerEntry entry)
                || entry.Generation != generation)
            {
                return false;
            }

            return _handlers.Remove(messageId);
        }
    }

    /// <summary>
    /// Backend-neutral endpoint for already encoded, canonical message bytes. Domain codecs own
    /// their schemas; the endpoint owns framing, validation, routing, and transport delivery.
    /// </summary>
    public interface INetworkMessageEndpoint
    {
        INetTransport Transport { get; }
        bool IsAcceptingMessages { get; }

        /// <summary>
        /// Returns the maximum canonical payload accepted for the message and channel, excluding
        /// the Cyclone frame header. A return value of zero means that the route is unavailable.
        /// </summary>
        int GetMaxPayloadSize(ushort messageId, NetworkChannel channel);

        /// <summary>
        /// Registers the sole handler for a message id and returns a generation-safe lease.
        /// Duplicate ownership fails immediately instead of silently replacing another module.
        /// </summary>
        NetworkMessageHandlerLease RegisterHandler(ushort messageId, NetworkMessageHandler handler);

        /// <summary>
        /// Sends one canonical payload to the authoritative server endpoint.
        /// </summary>
        NetworkSendResult SendToServer(
            ushort messageId,
            ReadOnlySpan<byte> payload,
            NetworkChannel channel = NetworkChannel.Reliable);

        /// <summary>
        /// Sends one canonical payload to a specific client connection.
        /// </summary>
        NetworkSendResult SendToClient(
            INetConnection connection,
            ushort messageId,
            ReadOnlySpan<byte> payload,
            NetworkChannel channel = NetworkChannel.Reliable);

        /// <summary>
        /// Broadcasts one canonical payload to all connected clients.
        /// </summary>
        NetworkSendResult BroadcastToClients(
            ushort messageId,
            ReadOnlySpan<byte> payload,
            NetworkChannel channel = NetworkChannel.Reliable);

        /// <summary>
        /// Broadcasts one canonical payload to a caller-owned connection set.
        /// </summary>
        NetworkSendResult Broadcast(
            IReadOnlyList<INetConnection> connections,
            ushort messageId,
            ReadOnlySpan<byte> payload,
            NetworkChannel channel = NetworkChannel.Reliable);

        /// <summary>
        /// Disconnects a connection according to the concrete backend's ownership rules.
        /// </summary>
        void Disconnect(INetConnection connection);
    }

    public readonly struct NetworkMessageIdRange
    {
        public readonly string Name;
        public readonly ushort Min;
        public readonly ushort Max;

        public NetworkMessageIdRange(string name, ushort min, ushort max)
        {
            if (min > max)
                throw new ArgumentOutOfRangeException(nameof(min));

            Name = name ?? string.Empty;
            Min = min;
            Max = max;
        }

        public bool Contains(ushort messageId)
        {
            return messageId >= Min && messageId <= Max;
        }

        public bool Overlaps(in NetworkMessageIdRange other)
        {
            return Min <= other.Max && other.Min <= Max;
        }

        public override string ToString()
        {
            return string.IsNullOrEmpty(Name)
                ? $"{Min}-{Max}"
                : $"{Name}:{Min}-{Max}";
        }
    }

    public static class NetworkMessageRanges
    {
        public static readonly NetworkMessageIdRange System = new NetworkMessageIdRange(
            "System",
            NetworkConstants.SystemMsgIdMin,
            NetworkConstants.SystemMsgIdMax);

        public static readonly NetworkMessageIdRange Module = new NetworkMessageIdRange(
            "Module",
            NetworkConstants.ModuleMsgIdMin,
            NetworkConstants.ModuleMsgIdMax);

        public static readonly NetworkMessageIdRange User = new NetworkMessageIdRange(
            "User",
            NetworkConstants.UserMsgIdMin,
            NetworkConstants.MaxMessageId);

        public static bool TryGetKnownRange(ushort messageId, out NetworkMessageIdRange range)
        {
            if (System.Contains(messageId))
            {
                range = System;
                return true;
            }

            if (Module.Contains(messageId))
            {
                range = Module;
                return true;
            }

            if (User.Contains(messageId))
            {
                range = User;
                return true;
            }

            range = default;
            return false;
        }

        public static bool ContainsRange(in NetworkMessageIdRange range)
        {
            return TryGetKnownRange(range.Min, out NetworkMessageIdRange minimumRange)
                   && TryGetKnownRange(range.Max, out NetworkMessageIdRange maximumRange)
                   && minimumRange.Min == maximumRange.Min
                   && minimumRange.Max == maximumRange.Max;
        }
    }

    public readonly struct NetworkMessageDescriptor
    {
        public readonly ushort MessageId;
        public readonly string ContractId;
        public readonly string Owner;
        public readonly ulong SchemaHash;
        public readonly NetworkChannel DefaultChannel;
        public readonly int MaxPayloadSize;

        public NetworkMessageDescriptor(
            ushort messageId,
            string contractId,
            string owner,
            ulong schemaHash,
            NetworkChannel defaultChannel = NetworkChannel.Reliable,
            int maxPayloadSize = NetworkConstants.DefaultMaxPayloadSize)
        {
            MessageId = messageId;
            ContractId = contractId ?? string.Empty;
            Owner = owner ?? string.Empty;
            SchemaHash = schemaHash;
            DefaultChannel = defaultChannel;
            MaxPayloadSize = maxPayloadSize;
        }

        public bool IsValid => !string.IsNullOrEmpty(ContractId)
                               && !string.IsNullOrEmpty(Owner)
                               && SchemaHash != 0UL
                               && MaxPayloadSize >= 0
                               && MaxPayloadSize <= NetworkConstants.MaxMTU - Security.NetworkWireProtocol.HeaderLength
                               && DefaultChannel >= NetworkChannel.Reliable
                               && DefaultChannel <= NetworkChannel.UnreliableSequenced;
    }

    public interface INetworkMessageCatalog
    {
        int MessageCount { get; }
        int ManifestCount { get; }
        ulong ProtocolFingerprint { get; }
        bool TryGetRegisteredRange(ushort messageId, out NetworkMessageIdRange range);
        bool TryGet(ushort messageId, out NetworkMessageDescriptor descriptor);
        bool TryRegisterProtocolManifest(NetworkProtocolManifest manifest);
        void Clear();
    }

    public sealed class NetworkMessageCatalog : INetworkMessageCatalog
    {
        private const ulong FnvOffsetBasis = Fnv1a64.OffsetBasis;
        private const ulong FnvPrime = Fnv1a64.Prime;

        private readonly object _syncRoot = new object();
        private readonly Dictionary<ushort, NetworkMessageDescriptor> _messages;
        private readonly Dictionary<string, NetworkProtocolManifest> _manifestsByProtocolId;
        private readonly List<ushort> _messageIds;
        private readonly List<NetworkMessageIdRange> _ranges;
        private bool _fingerprintDirty = true;
        private ulong _protocolFingerprint = FnvOffsetBasis;

        public NetworkMessageCatalog(int capacity = 128, int rangeCapacity = 16)
        {
            if (capacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            if (rangeCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(rangeCapacity));
            }

            _messages = new Dictionary<ushort, NetworkMessageDescriptor>(capacity);
            _manifestsByProtocolId = new Dictionary<string, NetworkProtocolManifest>(rangeCapacity, StringComparer.Ordinal);
            _messageIds = new List<ushort>(capacity);
            _ranges = new List<NetworkMessageIdRange>(rangeCapacity);
        }

        public int MessageCount
        {
            get
            {
                lock (_syncRoot)
                {
                    return _messages.Count;
                }
            }
        }

        public int ManifestCount
        {
            get
            {
                lock (_syncRoot)
                {
                    return _manifestsByProtocolId.Count;
                }
            }
        }

        public ulong ProtocolFingerprint
        {
            get
            {
                lock (_syncRoot)
                {
                    if (_fingerprintDirty)
                    {
                        RebuildProtocolFingerprintLocked();
                    }

                    return _protocolFingerprint;
                }
            }
        }

        public bool TryGetRegisteredRange(ushort messageId, out NetworkMessageIdRange range)
        {
            lock (_syncRoot)
            {
                for (int i = 0; i < _ranges.Count; i++)
                {
                    NetworkMessageIdRange candidate = _ranges[i];
                    if (candidate.Contains(messageId))
                    {
                        range = candidate;
                        return true;
                    }
                }
            }

            range = default;
            return false;
        }

        public bool TryGet(ushort messageId, out NetworkMessageDescriptor descriptor)
        {
            lock (_syncRoot)
            {
                return _messages.TryGetValue(messageId, out descriptor);
            }
        }

        public bool TryRegisterProtocolManifest(NetworkProtocolManifest manifest)
        {
            if (manifest == null)
                throw new ArgumentNullException(nameof(manifest));

            lock (_syncRoot)
            {
                if (_manifestsByProtocolId.TryGetValue(
                        manifest.ProtocolId,
                        out NetworkProtocolManifest registeredManifest))
                {
                    return ProtocolManifestsMatch(registeredManifest, manifest);
                }

                for (int i = 0; i < _ranges.Count; i++)
                {
                    NetworkMessageIdRange existingRange = _ranges[i];
                    if (existingRange.Overlaps(manifest.MessageRange))
                        return false;
                }

                for (int i = 0; i < manifest.Messages.Count; i++)
                {
                    NetworkMessageDescriptor descriptor = manifest.Messages[i];
                    if (!descriptor.IsValid || !manifest.MessageRange.Contains(descriptor.MessageId))
                        return false;

                    if (_messages.ContainsKey(descriptor.MessageId))
                        return false;
                }

                _ranges.Add(manifest.MessageRange);
                _ranges.Sort(CompareRangesByMin);
                _manifestsByProtocolId.Add(manifest.ProtocolId, manifest);

                for (int i = 0; i < manifest.Messages.Count; i++)
                {
                    NetworkMessageDescriptor descriptor = manifest.Messages[i];
                    _messages.Add(descriptor.MessageId, descriptor);
                    _messageIds.Add(descriptor.MessageId);
                }

                _fingerprintDirty = true;
                return true;
            }
        }

        public void Clear()
        {
            lock (_syncRoot)
            {
                _messages.Clear();
                _manifestsByProtocolId.Clear();
                _messageIds.Clear();
                _ranges.Clear();
                _protocolFingerprint = FnvOffsetBasis;
                _fingerprintDirty = false;
            }
        }

        internal static ulong ComputeAsciiFnv1a64(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            ulong hash = FnvOffsetBasis;
            for (int i = 0; i < text.Length; i++)
            {
                char value = text[i];
                if (value < '!' || value > '~')
                {
                    throw new ArgumentException(
                        "Stable network identifiers must use printable ASCII characters without spaces.",
                        nameof(text));
                }

                hash ^= (byte)value;
                hash *= FnvPrime;
            }

            return hash == 0UL ? FnvOffsetBasis : hash;
        }

        private static bool ProtocolDescriptorsMatch(
            in NetworkMessageDescriptor left,
            in NetworkMessageDescriptor right)
        {
            return left.MessageId == right.MessageId
                   && string.Equals(left.ContractId, right.ContractId, StringComparison.Ordinal)
                   && string.Equals(left.Owner, right.Owner, StringComparison.Ordinal)
                   && left.SchemaHash == right.SchemaHash
                   && left.DefaultChannel == right.DefaultChannel
                   && left.MaxPayloadSize == right.MaxPayloadSize;
        }

        private static bool ProtocolManifestsMatch(
            NetworkProtocolManifest left,
            NetworkProtocolManifest right)
        {
            if (ReferenceEquals(left, right))
                return true;

            if (left == null
                || right == null
                || !string.Equals(left.ProtocolId, right.ProtocolId, StringComparison.Ordinal)
                || !string.Equals(left.Owner, right.Owner, StringComparison.Ordinal)
                || left.CurrentVersion != right.CurrentVersion
                || left.MinimumSupportedVersion != right.MinimumSupportedVersion
                || !IsSameRange(left.MessageRange, right.MessageRange)
                || left.Messages.Count != right.Messages.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Messages.Count; i++)
            {
                NetworkMessageDescriptor leftDescriptor = left.Messages[i];
                bool found = false;
                for (int j = 0; j < right.Messages.Count; j++)
                {
                    if (leftDescriptor.MessageId == right.Messages[j].MessageId)
                    {
                        found = ProtocolDescriptorsMatch(leftDescriptor, right.Messages[j]);
                        break;
                    }
                }

                if (!found)
                    return false;
            }

            return true;
        }

        private void RebuildProtocolFingerprintLocked()
        {
            _messageIds.Sort();
            ulong hash = FnvOffsetBasis;

            for (int i = 0; i < _messageIds.Count; i++)
            {
                NetworkMessageDescriptor descriptor = _messages[_messageIds[i]];
                hash = Combine(hash, descriptor.MessageId);
                hash = Combine(hash, (ushort)descriptor.DefaultChannel);
                hash = Combine(hash, descriptor.SchemaHash);
                hash = Combine(hash, descriptor.MaxPayloadSize);
                hash = Combine(hash, ComputeAsciiFnv1a64(descriptor.Owner));
                hash = Combine(hash, ComputeAsciiFnv1a64(descriptor.ContractId));
            }

            for (int i = 0; i < _ranges.Count; i++)
            {
                NetworkMessageIdRange range = _ranges[i];
                hash = Combine(hash, range.Min);
                hash = Combine(hash, range.Max);
                hash = Combine(hash, ComputeAsciiFnv1a64(range.Name));
            }

            _protocolFingerprint = hash == 0UL ? FnvOffsetBasis : hash;
            _fingerprintDirty = false;
        }

        private static bool IsSameRange(in NetworkMessageIdRange left, in NetworkMessageIdRange right)
        {
            return left.Min == right.Min
                   && left.Max == right.Max
                   && string.Equals(left.Name, right.Name, StringComparison.Ordinal);
        }

        private static int CompareRangesByMin(NetworkMessageIdRange left, NetworkMessageIdRange right)
        {
            int result = left.Min.CompareTo(right.Min);
            return result != 0 ? result : string.CompareOrdinal(left.Name, right.Name);
        }

        private static ulong Combine(ulong hash, int value)
        {
            unchecked
            {
                hash ^= (uint)value;
                hash *= FnvPrime;
                hash ^= (uint)(value >> 16);
                hash *= FnvPrime;
                return hash;
            }
        }

        private static ulong Combine(ulong hash, ulong value)
        {
            unchecked
            {
                for (int i = 0; i < 8; i++)
                {
                    hash ^= (byte)(value >> (i * 8));
                    hash *= FnvPrime;
                }

                return hash;
            }
        }
    }

    public interface INetworkRuntimeContextProvider
    {
        INetworkRuntimeContext RuntimeContext { get; }
    }

    public static class NetworkRuntimeIds
    {
        public static readonly NetworkRuntimeId Mirror = NetworkRuntimeId.FromAsciiCode("Mirror");
        public static readonly NetworkRuntimeId Mirage = NetworkRuntimeId.FromAsciiCode("Mirage");
        public static readonly NetworkRuntimeId Nakama = NetworkRuntimeId.FromAsciiCode("Nakama");
    }

    [Flags]
    public enum NetworkBackendFeatures : uint
    {
        None = 0,
        RealtimeTransport = 1u << 0,
        AuthSession = 1u << 1,
        Matchmaker = 1u << 2,
        MatchState = 1u << 3,
        Presence = 1u << 4
    }

    public readonly struct NetworkRuntimeId : IEquatable<NetworkRuntimeId>
    {
        public const int MaxAsciiCodeLength = 8;

        public static readonly NetworkRuntimeId None = new NetworkRuntimeId(0UL);

        public readonly ulong Value;

        public NetworkRuntimeId(ulong value)
        {
            Value = value;
        }

        public static NetworkRuntimeId FromAsciiCode(string code)
        {
            if (string.IsNullOrEmpty(code))
                throw new ArgumentException("Runtime code must not be null or empty.", nameof(code));
            if (code.Length > MaxAsciiCodeLength)
                throw new ArgumentOutOfRangeException(nameof(code));

            ulong value = 0UL;
            for (int i = 0; i < code.Length; i++)
            {
                char c = code[i];
                if (c < 0x21 || c > 0x7E)
                    throw new ArgumentException("Runtime code must use printable ASCII characters without spaces.", nameof(code));

                value = (value << 8) | c;
            }

            return new NetworkRuntimeId(value);
        }

        public bool IsValid => Value != 0UL;
        public bool Equals(NetworkRuntimeId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is NetworkRuntimeId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();

        public bool TryGetAsciiCode(out string code)
        {
            if (Value == 0UL)
            {
                code = string.Empty;
                return false;
            }

            int byteCount = 0;
            ulong scan = Value;
            while (scan != 0UL)
            {
                byte b = (byte)(scan & 0xFF);
                if (b < 0x21 || b > 0x7E)
                {
                    code = string.Empty;
                    return false;
                }

                byteCount++;
                scan >>= 8;
            }

            char[] chars = new char[byteCount];
            ulong value = Value;
            for (int i = byteCount - 1; i >= 0; i--)
            {
                chars[i] = (char)(value & 0xFF);
                value >>= 8;
            }

            code = new string(chars);
            return true;
        }

        public override string ToString()
        {
            return TryGetAsciiCode(out string code)
                ? code
                : "0x" + Value.ToString("X16");
        }
    }

    public interface INetworkRuntimeContext : IDisposable
    {
        NetworkRuntimeId RuntimeId { get; }
        string RuntimeName { get; }
        NetworkBackendFeatures Features { get; }
        INetworkMessageEndpoint MessageEndpoint { get; }
        INetTransport Transport { get; }
        bool IsFrozen { get; }
        bool TryGetService<T>(out T service) where T : class;
    }

    public interface INetworkRuntimeContextBuilder
    {
        INetworkRuntimeContextBuilder AddService<T>(T service) where T : class;
        INetworkRuntimeContextBuilder AddFeature(NetworkBackendFeatures feature);
        INetworkRuntimeContext Build();
    }

    public interface INetworkSession
    {
        string UserId { get; }
        string Username { get; }
        bool IsExpired { get; }
        long ExpireTimeUnixSeconds { get; }
    }

    public interface INetworkSessionService
    {
        bool HasSession { get; }
        INetworkSession CurrentSession { get; }
        void ClearSession();
    }

    public readonly struct NetworkMatchId : IEquatable<NetworkMatchId>
    {
        public readonly string Value;

        public NetworkMatchId(string value)
        {
            Value = value ?? string.Empty;
        }

        public bool IsValid => !string.IsNullOrEmpty(Value);
        public bool Equals(NetworkMatchId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is NetworkMatchId other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);
        public override string ToString() => Value ?? string.Empty;
    }

    public readonly struct NetworkMatchmakerTicket : IEquatable<NetworkMatchmakerTicket>
    {
        public readonly string Value;

        public NetworkMatchmakerTicket(string value)
        {
            Value = value ?? string.Empty;
        }

        public bool IsValid => !string.IsNullOrEmpty(Value);
        public bool Equals(NetworkMatchmakerTicket other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is NetworkMatchmakerTicket other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);
        public override string ToString() => Value ?? string.Empty;
    }

    public interface INetworkMatchStateService
    {
        event Action<NetworkMatchId, INetConnection, ArraySegment<byte>, long> OnMatchState;
        NetworkMatchId CurrentMatchId { get; }
        bool IsInMatch { get; }
        bool TrySendMatchState(NetworkMatchId matchId, long operationCode, in ArraySegment<byte> payload, NetworkChannel channel = NetworkChannel.Reliable);
        void LeaveMatch(NetworkMatchId matchId);
    }

    public interface INetworkMatchmakerService
    {
        event Action<NetworkMatchmakerTicket, NetworkMatchId> OnMatched;
        bool IsMatchmaking { get; }
        NetworkMatchmakerTicket CurrentTicket { get; }
        bool TryCancelMatchmaker(NetworkMatchmakerTicket ticket);
    }

    public readonly struct NetworkPresence : IEquatable<NetworkPresence>
    {
        public readonly string UserId;
        public readonly string SessionId;
        public readonly string Username;

        public NetworkPresence(string userId, string sessionId, string username)
        {
            UserId = userId ?? string.Empty;
            SessionId = sessionId ?? string.Empty;
            Username = username ?? string.Empty;
        }

        public bool IsValid => !string.IsNullOrEmpty(UserId) && !string.IsNullOrEmpty(SessionId);
        public bool Equals(NetworkPresence other)
        {
            return string.Equals(UserId, other.UserId, StringComparison.Ordinal)
                   && string.Equals(SessionId, other.SessionId, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => obj is NetworkPresence other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                return (StringComparer.Ordinal.GetHashCode(UserId ?? string.Empty) * 397)
                       ^ StringComparer.Ordinal.GetHashCode(SessionId ?? string.Empty);
            }
        }
    }

    public interface INetworkPresenceService
    {
        event Action<NetworkMatchId, NetworkPresence> OnPresenceJoined;
        event Action<NetworkMatchId, NetworkPresence> OnPresenceLeft;
        bool TryGetLocalPresence(out NetworkPresence presence);
    }

    public sealed class NetworkRuntimeContext : INetworkRuntimeContext, INetworkRuntimeContextBuilder
    {
        private readonly object _syncRoot = new object();
        private readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();
        private NetworkBackendFeatures _features;
        private bool _frozen;
        private bool _disposed;

        public NetworkRuntimeContext(
            NetworkRuntimeId runtimeId,
            string runtimeName,
            INetworkMessageEndpoint messageEndpoint,
            NetworkBackendFeatures features = NetworkBackendFeatures.None)
        {
            RuntimeId = runtimeId;
            RuntimeName = runtimeName ?? string.Empty;
            MessageEndpoint = messageEndpoint ?? throw new ArgumentNullException(nameof(messageEndpoint));
            Transport = messageEndpoint.Transport;
            _features = features;
            AddService<INetworkMessageEndpoint>(messageEndpoint);
            AddService<INetworkMessageCatalog>(new NetworkMessageCatalog());
            if (Transport != null)
                AddService<INetTransport>(Transport);
        }

        public NetworkRuntimeId RuntimeId { get; }
        public string RuntimeName { get; }
        public NetworkBackendFeatures Features
        {
            get
            {
                lock (_syncRoot)
                    return _features;
            }
        }

        public INetworkMessageEndpoint MessageEndpoint { get; }
        public INetTransport Transport { get; }
        public bool IsFrozen
        {
            get
            {
                lock (_syncRoot)
                    return _frozen;
            }
        }

        public INetworkRuntimeContextBuilder AddService<T>(T service) where T : class
        {
            if (service == null)
                throw new ArgumentNullException(nameof(service));

            lock (_syncRoot)
            {
                ThrowIfDisposed();
                ThrowIfFrozen();
                _services[typeof(T)] = service;
            }
            return this;
        }

        public INetworkRuntimeContextBuilder AddFeature(NetworkBackendFeatures feature)
        {
            lock (_syncRoot)
            {
                ThrowIfDisposed();
                ThrowIfFrozen();
                _features |= feature;
            }
            return this;
        }

        public INetworkRuntimeContext Build()
        {
            lock (_syncRoot)
            {
                ThrowIfDisposed();
                _frozen = true;
            }
            return this;
        }

        public bool TryGetService<T>(out T service) where T : class
        {
            lock (_syncRoot)
            {
                if (_disposed)
                {
                    service = null;
                    return false;
                }

                if (_services.TryGetValue(typeof(T), out object value))
                {
                    service = value as T;
                    return service != null;
                }
            }

            service = null;
            return false;
        }

        public void Dispose()
        {
            lock (_syncRoot)
            {
                if (_disposed)
                    return;

                _services.Clear();
                _features = NetworkBackendFeatures.None;
                _frozen = true;
                _disposed = true;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(NetworkRuntimeContext));
        }

        private void ThrowIfFrozen()
        {
            if (_frozen)
                throw new InvalidOperationException("NetworkRuntimeContext is frozen. Register services before Build is called.");
        }
    }
}
