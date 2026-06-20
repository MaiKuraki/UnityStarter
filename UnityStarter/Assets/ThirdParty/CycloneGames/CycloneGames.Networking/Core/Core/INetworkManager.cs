using System;
using System.Collections.Generic;
using CycloneGames.Hash.Core;
using CycloneGames.Networking.Serialization;

namespace CycloneGames.Networking
{
    /// <summary>
    /// High-level networking manager that handles message registration, serialization, and transport management.
    /// Acts as the main entry point for gameplay logic.
    /// </summary>
    public interface INetworkManager
    {
        INetTransport Transport { get; }
        INetSerializer Serializer { get; }

        /// <summary>
        /// Registers a handler for a specific message ID.
        /// The handler receives the connection source and the deserialized message.
        /// </summary>
        void RegisterHandler<T>(ushort msgId, Action<INetConnection, T> handler) where T : struct;

        /// <summary>
        /// Unregisters a handler for a specific message ID.
        /// </summary>
        void UnregisterHandler(ushort msgId);

        /// <summary>
        /// Sends a message to the server.
        /// </summary>
        /// <param name="channel">QoS channel to use (default Reliable).</param>
        NetworkSendResult SendToServer<T>(ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct;

        /// <summary>
        /// Sends a message to a specific client connection.
        /// </summary>
        /// <param name="channel">QoS channel to use (default Reliable).</param>
        NetworkSendResult SendToClient<T>(INetConnection connection, ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct;

        /// <summary>
        /// Broadcasts a message to all connected clients.
        /// </summary>
        /// <param name="channel">QoS channel to use (default Reliable).</param>
        NetworkSendResult BroadcastToClients<T>(ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct;

        /// <summary>
        /// Broadcasts a message to a list of connections.
        /// </summary>
        /// <param name="channel">QoS channel to use (default Reliable).</param>
        NetworkSendResult Broadcast<T>(IReadOnlyList<INetConnection> connections, ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct;

        /// <summary>
        /// Disconnects a client.
        /// </summary>
        void DisconnectClient(INetConnection connection);
    }

    public interface INetworkSerializerConfigurable
    {
        void SetSerializer(INetSerializer serializer);
    }

    [Flags]
    public enum NetworkMessageKind : ushort
    {
        None = 0,
        System = 1 << 0,
        Rpc = 1 << 1,
        Snapshot = 1 << 2,
        StateSync = 1 << 3,
        Module = 1 << 4,
        Backend = 1 << 5,
        User = 1 << 6
    }

    public readonly struct NetworkMessageIdRange
    {
        public readonly string Name;
        public readonly ushort Min;
        public readonly ushort Max;
        public readonly NetworkMessageKind Kind;

        public NetworkMessageIdRange(string name, ushort min, ushort max, NetworkMessageKind kind)
        {
            if (min > max)
                throw new ArgumentOutOfRangeException(nameof(min));

            Name = name ?? string.Empty;
            Min = min;
            Max = max;
            Kind = kind;
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
            NetworkConstants.SystemMsgIdMax,
            NetworkMessageKind.System);

        public static readonly NetworkMessageIdRange Rpc = new NetworkMessageIdRange(
            "RPC",
            NetworkConstants.RpcMsgIdMin,
            NetworkConstants.RpcMsgIdMax,
            NetworkMessageKind.Rpc);

        public static readonly NetworkMessageIdRange Module = new NetworkMessageIdRange(
            "Module",
            NetworkConstants.ModuleMsgIdMin,
            NetworkConstants.ModuleMsgIdMax,
            NetworkMessageKind.Module);

        public static readonly NetworkMessageIdRange User = new NetworkMessageIdRange(
            "User",
            NetworkConstants.UserMsgIdMin,
            NetworkConstants.MaxMessageId,
            NetworkMessageKind.User);

        public static bool TryGetKnownRange(ushort messageId, out NetworkMessageIdRange range)
        {
            if (System.Contains(messageId))
            {
                range = System;
                return true;
            }

            if (Rpc.Contains(messageId))
            {
                range = Rpc;
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

        public static bool TryGetKnownRange(NetworkMessageKind kind, out NetworkMessageIdRange range)
        {
            if ((kind & NetworkMessageKind.System) != 0)
            {
                range = System;
                return true;
            }

            if ((kind & NetworkMessageKind.Rpc) != 0)
            {
                range = Rpc;
                return true;
            }

            if ((kind & NetworkMessageKind.Module) != 0)
            {
                range = Module;
                return true;
            }

            if ((kind & NetworkMessageKind.User) != 0)
            {
                range = User;
                return true;
            }

            range = default;
            return false;
        }

        public static bool ContainsRange(in NetworkMessageIdRange range)
        {
            return TryGetKnownRange(range.Kind, out NetworkMessageIdRange knownRange)
                   && knownRange.Contains(range.Min)
                   && knownRange.Contains(range.Max);
        }

        public static bool IsMessageIdCompatible(ushort messageId, NetworkMessageKind kind)
        {
            return TryGetKnownRange(kind, out NetworkMessageIdRange range)
                   && range.Contains(messageId);
        }
    }

    public readonly struct NetworkMessageDescriptor
    {
        public readonly ushort MessageId;
        public readonly string Name;
        public readonly string Owner;
        public readonly ulong SchemaHash;
        public readonly NetworkMessageKind Kind;
        public readonly NetworkChannel DefaultChannel;
        public readonly int MaxPayloadSize;

        public NetworkMessageDescriptor(
            ushort messageId,
            string name,
            string owner,
            ulong schemaHash,
            NetworkMessageKind kind,
            NetworkChannel defaultChannel = NetworkChannel.Reliable,
            int maxPayloadSize = NetworkConstants.DefaultMaxPayloadSize)
        {
            MessageId = messageId;
            Name = name ?? string.Empty;
            Owner = owner ?? string.Empty;
            SchemaHash = schemaHash;
            Kind = kind;
            DefaultChannel = defaultChannel;
            MaxPayloadSize = maxPayloadSize;
        }

        public bool IsValid => MessageId <= NetworkConstants.MaxMessageId
                               && !string.IsNullOrEmpty(Name)
                               && !string.IsNullOrEmpty(Owner)
                               && SchemaHash != 0UL
                               && MaxPayloadSize >= 0;

        public static NetworkMessageDescriptor Create<T>(
            ushort messageId,
            string owner,
            NetworkMessageKind kind,
            NetworkChannel defaultChannel = NetworkChannel.Reliable,
            int maxPayloadSize = NetworkConstants.DefaultMaxPayloadSize) where T : struct
        {
            string typeName = typeof(T).FullName ?? typeof(T).Name;
            return new NetworkMessageDescriptor(
                messageId,
                typeName,
                owner,
                NetworkMessageCatalog.ComputeStableHash(typeName),
                kind,
                defaultChannel,
                maxPayloadSize);
        }
    }

    public interface INetworkMessageCatalog
    {
        int Count { get; }
        int RangeCount { get; }
        int ModuleRangeCount { get; }
        ulong ProtocolFingerprint { get; }
        void RegisterRange(in NetworkMessageIdRange range);
        bool TryRegisterRange(in NetworkMessageIdRange range);
        bool TryGetRegisteredRange(ushort messageId, out NetworkMessageIdRange range);
        void RegisterModuleRange(in NetworkMessageIdRange range);
        bool TryRegisterModuleRange(in NetworkMessageIdRange range);
        bool TryGetRegisteredModuleRange(ushort messageId, out NetworkMessageIdRange range);
        void Register(in NetworkMessageDescriptor descriptor);
        bool TryRegister(in NetworkMessageDescriptor descriptor);
        bool TryGet(ushort messageId, out NetworkMessageDescriptor descriptor);
        void Clear();
    }

    public sealed class NetworkMessageCatalog : INetworkMessageCatalog
    {
        private const ulong FnvOffsetBasis = Fnv1a64.OffsetBasis;
        private const ulong FnvPrime = Fnv1a64.Prime;

        private readonly object _syncRoot = new object();
        private readonly Dictionary<ushort, NetworkMessageDescriptor> _messages;
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
            _messageIds = new List<ushort>(capacity);
            _ranges = new List<NetworkMessageIdRange>(rangeCapacity);
        }

        public int Count
        {
            get
            {
                lock (_syncRoot)
                {
                    return _messages.Count;
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

        public int RangeCount
        {
            get
            {
                lock (_syncRoot)
                {
                    return _ranges.Count;
                }
            }
        }

        public int ModuleRangeCount
        {
            get
            {
                lock (_syncRoot)
                {
                    int count = 0;
                    for (int i = 0; i < _ranges.Count; i++)
                    {
                        if ((_ranges[i].Kind & NetworkMessageKind.Module) != 0)
                        {
                            count++;
                        }
                    }

                    return count;
                }
            }
        }

        public void RegisterRange(in NetworkMessageIdRange range)
        {
            if (!TryRegisterRange(range))
            {
                throw new InvalidOperationException($"Protocol message range {range} conflicts with an existing protocol range.");
            }
        }

        public bool TryRegisterRange(in NetworkMessageIdRange range)
        {
            if (!IsValidProtocolRange(range))
            {
                throw new ArgumentException("Protocol message range is invalid.", nameof(range));
            }

            lock (_syncRoot)
            {
                for (int i = 0; i < _ranges.Count; i++)
                {
                    NetworkMessageIdRange existing = _ranges[i];
                    if (IsSameRange(existing, range))
                    {
                        return true;
                    }

                    if (existing.Overlaps(range))
                    {
                        return false;
                    }
                }

                _ranges.Add(range);
                _ranges.Sort(CompareRangesByMin);
                _fingerprintDirty = true;
                return true;
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

        public void RegisterModuleRange(in NetworkMessageIdRange range)
        {
            if (!TryRegisterModuleRange(range))
            {
                throw new InvalidOperationException($"Module message range {range} conflicts with an existing module range.");
            }
        }

        public bool TryRegisterModuleRange(in NetworkMessageIdRange range)
        {
            if (!IsValidModuleRange(range))
            {
                throw new ArgumentException("Module message range is invalid.", nameof(range));
            }

            return TryRegisterRange(range);
        }

        public bool TryGetRegisteredModuleRange(ushort messageId, out NetworkMessageIdRange range)
        {
            if (!TryGetRegisteredRange(messageId, out range))
            {
                return false;
            }

            return (range.Kind & NetworkMessageKind.Module) != 0;
        }

        public void Register(in NetworkMessageDescriptor descriptor)
        {
            if (!TryRegister(descriptor))
            {
                throw new InvalidOperationException($"Message id {descriptor.MessageId} is already registered.");
            }
        }

        public bool TryRegister(in NetworkMessageDescriptor descriptor)
        {
            if (!descriptor.IsValid)
            {
                throw new ArgumentException("Message descriptor is invalid.", nameof(descriptor));
            }

            lock (_syncRoot)
            {
                ValidateDescriptorRangeLocked(descriptor);

                if (_messages.ContainsKey(descriptor.MessageId))
                {
                    return false;
                }

                _messages.Add(descriptor.MessageId, descriptor);
                _messageIds.Add(descriptor.MessageId);
                _fingerprintDirty = true;
                return true;
            }
        }

        public bool TryGet(ushort messageId, out NetworkMessageDescriptor descriptor)
        {
            lock (_syncRoot)
            {
                return _messages.TryGetValue(messageId, out descriptor);
            }
        }

        public void Clear()
        {
            lock (_syncRoot)
            {
                _messages.Clear();
                _messageIds.Clear();
                _ranges.Clear();
                _protocolFingerprint = FnvOffsetBasis;
                _fingerprintDirty = false;
            }
        }

        public static ulong ComputeStableHash(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            ulong hash = FnvOffsetBasis;
            for (int i = 0; i < text.Length; i++)
            {
                hash ^= text[i];
                hash *= FnvPrime;
            }

            return hash == 0UL ? FnvOffsetBasis : hash;
        }

        private void RebuildProtocolFingerprintLocked()
        {
            _messageIds.Sort();
            ulong hash = FnvOffsetBasis;

            for (int i = 0; i < _messageIds.Count; i++)
            {
                NetworkMessageDescriptor descriptor = _messages[_messageIds[i]];
                hash = Combine(hash, descriptor.MessageId);
                hash = Combine(hash, (ushort)descriptor.Kind);
                hash = Combine(hash, (ushort)descriptor.DefaultChannel);
                hash = Combine(hash, descriptor.SchemaHash);
                hash = Combine(hash, descriptor.MaxPayloadSize);
                hash = Combine(hash, ComputeStableHash(descriptor.Owner));
                hash = Combine(hash, ComputeStableHash(descriptor.Name));
            }

            for (int i = 0; i < _ranges.Count; i++)
            {
                NetworkMessageIdRange range = _ranges[i];
                hash = Combine(hash, range.Min);
                hash = Combine(hash, range.Max);
                hash = Combine(hash, (ushort)range.Kind);
                hash = Combine(hash, ComputeStableHash(range.Name));
            }

            _protocolFingerprint = hash == 0UL ? FnvOffsetBasis : hash;
            _fingerprintDirty = false;
        }

        private static bool IsValidModuleRange(in NetworkMessageIdRange range)
        {
            return range.Kind == NetworkMessageKind.Module
                   && IsValidProtocolRange(range);
        }

        private static bool IsValidProtocolRange(in NetworkMessageIdRange range)
        {
            return !string.IsNullOrEmpty(range.Name)
                   && range.Kind != NetworkMessageKind.None
                   && NetworkMessageRanges.ContainsRange(range);
        }

        private static bool IsSameRange(in NetworkMessageIdRange left, in NetworkMessageIdRange right)
        {
            return left.Min == right.Min
                   && left.Max == right.Max
                   && left.Kind == right.Kind
                   && string.Equals(left.Name, right.Name, StringComparison.Ordinal);
        }

        private void ValidateDescriptorRangeLocked(in NetworkMessageDescriptor descriptor)
        {
            if (!NetworkMessageRanges.IsMessageIdCompatible(descriptor.MessageId, descriptor.Kind))
            {
                throw new ArgumentException(
                    $"Message id {descriptor.MessageId} is outside the reserved range for kind {descriptor.Kind}.",
                    nameof(descriptor));
            }

            if (!RequiresRegisteredOwnerRange(descriptor.Kind))
            {
                return;
            }

            for (int i = 0; i < _ranges.Count; i++)
            {
                NetworkMessageIdRange range = _ranges[i];
                if (!range.Contains(descriptor.MessageId))
                {
                    continue;
                }

                if ((descriptor.Kind & range.Kind) == 0)
                {
                    continue;
                }

                if (!string.Equals(range.Name, descriptor.Owner, StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"Message id {descriptor.MessageId} belongs to {range.Name}, not {descriptor.Owner}.",
                        nameof(descriptor));
                }

                return;
            }

            throw new ArgumentException(
                $"Message id {descriptor.MessageId} has no registered owner range.",
                nameof(descriptor));
        }

        private static bool RequiresRegisteredOwnerRange(NetworkMessageKind kind)
        {
            return (kind & NetworkMessageKind.Module) != 0
                   || (kind & NetworkMessageKind.User) != 0;
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
        BackendRpc = 1u << 4,
        Storage = 1u << 5,
        Chat = 1u << 6,
        Presence = 1u << 7,
        Relay = 1u << 8,
        AuthoritativeServer = 1u << 9
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
        INetworkManager NetworkManager { get; }
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

    public interface INetworkBackendRpcService
    {
        bool TryCallRpc(string rpcId, in ArraySegment<byte> payload, out ArraySegment<byte> response);
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
            INetworkManager networkManager,
            NetworkBackendFeatures features = NetworkBackendFeatures.None)
        {
            RuntimeId = runtimeId;
            RuntimeName = runtimeName ?? string.Empty;
            NetworkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
            Transport = networkManager.Transport;
            _features = features;
            AddService<INetworkManager>(networkManager);
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

        public INetworkManager NetworkManager { get; }
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
