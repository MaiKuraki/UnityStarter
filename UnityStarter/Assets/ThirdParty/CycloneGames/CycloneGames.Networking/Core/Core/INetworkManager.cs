using System;
using System.Collections.Generic;
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
        void SendToServer<T>(ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct;

        /// <summary>
        /// Sends a message to a specific client connection.
        /// </summary>
        /// <param name="channel">QoS channel to use (default Reliable).</param>
        void SendToClient<T>(INetConnection connection, ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct;

        /// <summary>
        /// Broadcasts a message to all connected clients.
        /// </summary>
        /// <param name="channel">QoS channel to use (default Reliable).</param>
        void BroadcastToClients<T>(ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct;

        /// <summary>
        /// Broadcasts a message to a list of connections.
        /// </summary>
        /// <param name="channel">QoS channel to use (default Reliable).</param>
        void Broadcast<T>(IReadOnlyList<INetConnection> connections, ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct;

        /// <summary>
        /// Disconnects a client.
        /// </summary>
        void DisconnectClient(INetConnection connection);
    }

    public interface INetworkSerializerConfigurable
    {
        void SetSerializer(INetSerializer serializer);
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
