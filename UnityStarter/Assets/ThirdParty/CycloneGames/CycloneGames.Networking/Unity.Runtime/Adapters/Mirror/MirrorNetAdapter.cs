#if CYCLONE_NETWORKING_HAS_MIRROR
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using UnityEngine;
using Mirror;
using CycloneGames.Networking.Security;
using CycloneGames.Logger;

namespace CycloneGames.Networking.Adapter.Mirror
{
    /// <summary>
    /// Carries one complete Cyclone wire frame through Mirror.
    /// The frame contains the Cyclone header followed by the serialized gameplay payload.
    /// </summary>
    public struct CycloneWireFrameMessage : NetworkMessage
    {
        public ArraySegment<byte> Frame;
    }

    /// <summary>
    /// Mirror implementation of the Cyclone Networking stack.
    /// Implements both low-level transport delivery and canonical message framing.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MirrorNetAdapter : MonoBehaviour, INetTransport, INetworkMessageEndpoint, INetworkSecurityPolicyConfigurable, INetworkRuntimeContextProvider, INetworkLifecycleProvider, INetworkFeatureProvider
    {
        private const int MirrorArraySegmentLengthPrefixMaxBytes = 5;
        private const string MissingNetworkManagerError = "Mirror NetworkManager.singleton is not available.";
        private const string MissingTransportError = "Mirror Transport.active is not available.";

        [Header("Security")]
        [SerializeField] private bool _enableMessageValidation = true;
        [SerializeField] private int _maxPayloadSize = NetworkConstants.DefaultMaxPayloadSize;
        [SerializeField] private bool _enableRateLimiter = true;
        [SerializeField] private int _maxMessagesPerSecond = 120;
        [SerializeField] private int _burstMessages = 40;
        [SerializeField] private bool _requireAuthenticatedMessages = false;
        [SerializeField] private bool _requireEncryptedTransport = false;

        // INetTransport Properties
        public bool IsServer
        {
            get
            {
                EnsureMainThread();
                return NetworkServer.active;
            }
        }

        public bool IsClient
        {
            get
            {
                EnsureMainThread();
                return NetworkClient.active;
            }
        }

        public bool IsRunning => IsServer || IsClient;
        public bool Available
        {
            get
            {
                EnsureMainThread();
                global::Mirror.Transport transport = global::Mirror.Transport.active;
                return transport != null && transport.Available();
            }
        }

        public NetworkBackendFeatures Features => NetworkBackendFeatures.RealtimeTransport;
        public NetworkTransportCapabilities Capabilities
        {
            get
            {
                EnsureMainThread();
                return new NetworkTransportCapabilities(
                    "Mirror",
                    NetworkTransportFeatureFlags.Client
                    | NetworkTransportFeatureFlags.Server
                    | NetworkTransportFeatureFlags.Host
                    | NetworkTransportFeatureFlags.Reliable
                    | NetworkTransportFeatureFlags.Unreliable
                    | NetworkTransportFeatureFlags.MainThreadOnly
                    | NetworkTransportFeatureFlags.DedicatedServerCompatible,
                    NetworkChannelFlags.Reliable | NetworkChannelFlags.Unreliable,
                    NetworkConstants.DefaultMaxConnections,
                    GetMaxPacketSize(GetChannelId(NetworkChannel.Reliable)),
                    GetMaxPacketSize(GetChannelId(NetworkChannel.Reliable)),
                    GetMaxPacketSize(GetChannelId(NetworkChannel.Unreliable)));
            }
        }

        public bool IsEncrypted
        {
            get
            {
                EnsureMainThread();
                var t = global::Mirror.Transport.active;
                return t != null && t.IsEncrypted;
            }
        }

        // Channel Mapping
        public int GetChannelId(NetworkChannel channel)
        {
            return channel switch
            {
                NetworkChannel.Reliable => Channels.Reliable,
                NetworkChannel.Unreliable => Channels.Unreliable,
                NetworkChannel.ReliableUnordered => throw new NotSupportedException("Mirror does not advertise a reliable-unordered channel."),
                NetworkChannel.UnreliableSequenced => throw new NotSupportedException("Mirror does not advertise a distinct unreliable-sequenced channel."),
                _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "Unknown network channel.")
            };
        }

        public int GetMaxPacketSize(int channelId)
        {
            EnsureMainThread();
            global::Mirror.Transport transport = global::Mirror.Transport.active;
            if (transport == null)
                return 0;

            int maximumContentSize;
            try
            {
                maximumContentSize = global::Mirror.NetworkMessages.MaxContentSize(channelId);
            }
            catch (Exception)
            {
                return 0;
            }

            int maximumPayloadSize = maximumContentSize
                                     - MirrorArraySegmentLengthPrefixMaxBytes
                                     - NetworkWireProtocol.HeaderLength;
            return Math.Min(_maxPayloadSize, Math.Max(0, maximumPayloadSize));
        }

        private NetworkChannel GetNetworkChannel(int channelId)
        {
            if (channelId == Channels.Reliable)
                return NetworkChannel.Reliable;
            if (channelId == Channels.Unreliable)
                return NetworkChannel.Unreliable;

            throw new ArgumentOutOfRangeException(nameof(channelId), channelId, "Mirror channel id is not supported.");
        }

        private int BuildFrame(ushort msgId, NetworkChannel channel, in ArraySegment<byte> payload, byte[] buffer)
        {
            if (payload.Array == null)
                throw new ArgumentException("Payload must reference a valid array.", nameof(payload));
            if (payload.Offset < 0 || payload.Count < 0 || payload.Offset > payload.Array.Length || payload.Count > payload.Array.Length - payload.Offset)
                throw new ArgumentOutOfRangeException(nameof(payload));

            return BuildFrame(
                msgId,
                channel,
                new ReadOnlySpan<byte>(payload.Array, payload.Offset, payload.Count),
                buffer);
        }

        private int BuildFrame(ushort msgId, NetworkChannel channel, ReadOnlySpan<byte> payload, byte[] buffer)
        {
            int maximumPayloadSize = GetMaxPayloadSize(msgId, channel);
            if (maximumPayloadSize <= 0 || payload.Length > maximumPayloadSize)
                throw new InvalidOperationException("Payload exceeds the available Mirror message route.");

            return BuildFrameUnchecked(msgId, channel, payload, buffer);
        }

        private int BuildFrameUnchecked(ushort msgId, NetworkChannel channel, ReadOnlySpan<byte> payload, byte[] buffer)
        {
            int frameLength = NetworkFrameCodec.GetFrameLength(payload.Length);
            if (frameLength > buffer.Length)
                throw new InvalidOperationException($"Frame size exceeds send buffer capacity: {frameLength}");

            payload.CopyTo(new Span<byte>(buffer, NetworkWireProtocol.HeaderLength, payload.Length));
            WriteFrameHeader(msgId, channel, buffer, payload.Length);
            return frameLength;
        }

        public int GetMaxPayloadSize(ushort messageId, NetworkChannel channel)
        {
            int channelId;
            try
            {
                channelId = GetChannelId(channel);
            }
            catch (ArgumentException)
            {
                return 0;
            }
            catch (NotSupportedException)
            {
                return 0;
            }

            int transportLimit = GetMaxPacketSize(channelId);
            int maximumPayloadSize = Math.Min(_maxPayloadSize, Math.Max(0, transportLimit));
            if (RuntimeContext != null
                && RuntimeContext.TryGetService(out INetworkMessageCatalog catalog))
            {
                if (catalog.TryGet(messageId, out NetworkMessageDescriptor descriptor))
                {
                    maximumPayloadSize = Math.Min(maximumPayloadSize, descriptor.MaxPayloadSize);
                }
                else if (messageId > NetworkConstants.SystemMsgIdMax)
                {
                    return 0;
                }
            }

            return maximumPayloadSize;
        }

        private int WriteFrameHeader(ushort msgId, NetworkChannel channel, byte[] buffer, int payloadLength)
        {
            if (payloadLength < 0 || payloadLength > _maxPayloadSize)
                throw new InvalidOperationException("Payload exceeds the configured Mirror payload limit.");

            int frameLength = NetworkFrameCodec.GetFrameLength(payloadLength);
            if (frameLength > buffer.Length)
                throw new InvalidOperationException($"Frame size exceeds send buffer capacity: {frameLength}");

            uint sequence = unchecked((uint)Interlocked.Increment(ref _sendSequence));
            if (sequence == 0u)
                sequence = unchecked((uint)Interlocked.Increment(ref _sendSequence));

            var payload = new ReadOnlySpan<byte>(buffer, NetworkWireProtocol.HeaderLength, payloadLength);
            var flags = GetMessageFlags(channel);
            uint checksum = NetworkFrameCodec.ComputeChecksum(msgId, channel, flags, sequence, payload);
            var header = new NetworkEnvelopeHeader(msgId, channel, payloadLength, sequence, checksum, flags);
            NetworkFrameCodec.WriteHeader(buffer, 0, header);
            return frameLength;
        }

        private static NetworkMessageFlags GetMessageFlags(NetworkChannel channel)
        {
            return channel switch
            {
                NetworkChannel.Reliable => NetworkMessageFlags.Reliable | NetworkMessageFlags.Ordered,
                NetworkChannel.ReliableUnordered => NetworkMessageFlags.Reliable,
                NetworkChannel.UnreliableSequenced => NetworkMessageFlags.Ordered,
                _ => NetworkMessageFlags.None
            };
        }

        // Statistics
        private long _bytesSent;
        private long _bytesReceived;
        private int _packetsSent;
        private int _packetsReceived;

        public NetworkStatistics GetStatistics()
        {
            EnsureMainThread();
            return new NetworkStatistics(
                Interlocked.Read(ref _bytesSent),
                Interlocked.Read(ref _bytesReceived),
                Volatile.Read(ref _packetsSent),
                Volatile.Read(ref _packetsReceived),
                NetworkServer.active ? NetworkServer.connections.Count : (NetworkClient.active ? 1 : 0)
            );
        }

        public NetworkLifecycleSnapshot GetLifecycleSnapshot()
        {
            EnsureMainThread();
            return new NetworkLifecycleSnapshot(
                GetCurrentLifecycleState(),
                Features,
                _lastError,
                _lastErrorMessage,
                Available,
                IsRunning,
                IsServer,
                IsClient,
                IsEncrypted);
        }

        public INetTransport Transport => this;
        public bool IsAcceptingMessages => !_isDestroyed && IsRunning;
        public INetworkRuntimeContext RuntimeContext { get; private set; }

        // Events
        public event Action<INetConnection> OnClientConnected;
        public event Action<INetConnection> OnClientDisconnected;
        public event Action OnConnectedToServer;
        public event Action OnDisconnectedFromServer;
        public event Action<INetConnection, TransportError, string> OnError;
        public event Action<INetConnection, ArraySegment<byte>, int> OnDataReceived;

        // Internal State
        private readonly NetworkMessageHandlerRegistry _messageHandlers =
            new NetworkMessageHandlerRegistry();

        private readonly Dictionary<int, ulong> _playerIds = new Dictionary<int, ulong>();
        private readonly Dictionary<int, MirrorConnectionData> _connectionData = new Dictionary<int, MirrorConnectionData>();
        private readonly Dictionary<int, MirrorNetConnection> _serverConnections =
            new Dictionary<int, MirrorNetConnection>();
        private MirrorNetConnection _authorityConnection;

        private readonly byte[] _sendBuffer = new byte[NetworkConstants.MaxMTU];

        private byte[] GetSendBuffer() => _sendBuffer;

        private int _mainThreadId;
        private int _sendSequence;
        private MessageValidator _messageValidator;
        private RateLimiter _rateLimiter;
        private MessageSecurityPolicyRegistry _messagePolicies;
        private NetworkSecurityPipeline _securityPipeline;
        private NetworkLifecycleState _lifecycleState = NetworkLifecycleState.Stopped;
        private TransportError _lastError;
        private string _lastErrorMessage = string.Empty;
        private bool _isDestroyed;
        private bool _clientCallbacksBound;
        private bool _serverCallbacksBound;
        private global::Mirror.Transport _boundTransport;

        // Tracks per-connection statistics
        internal struct MirrorConnectionData
        {
            public long BytesSent;
            public long BytesReceived;
            public double Jitter;
            public int LastPing;
        }

        private void Awake()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;

            int maximumPayloadSize = NetworkConstants.MaxMTU - NetworkWireProtocol.HeaderLength;
            if (_maxPayloadSize <= 0 || _maxPayloadSize > maximumPayloadSize)
            {
                throw new InvalidOperationException(
                    $"Max Payload Size must be between 1 and {maximumPayloadSize} bytes.");
            }
            if (_maxMessagesPerSecond <= 0)
                throw new InvalidOperationException("Max Messages Per Second must be positive.");
            if (_burstMessages < 0)
                throw new InvalidOperationException("Burst Messages cannot be negative.");
            _messageValidator = new MessageValidator(_maxPayloadSize, 0);
            _rateLimiter = new RateLimiter(
                _maxMessagesPerSecond,
                (long)_maxPayloadSize * _maxMessagesPerSecond,
                _burstMessages);
            _messagePolicies = new MessageSecurityPolicyRegistry(CreateDefaultSecurityPolicy());
            _securityPipeline = CreateSecurityPipeline();
            RuntimeContext = new NetworkRuntimeContext(
                    NetworkRuntimeIds.Mirror,
                    "Mirror",
                    this,
                    Features)
                .AddService<INetworkSecurityPolicyConfigurable>(this)
                .AddSecurityPipeline(_securityPipeline)
                .Build();

        }

        private void Start()
        {
            RefreshMirrorBindings(forceRebind: true);
        }

        private void Update()
        {
            RefreshMirrorBindings(forceRebind: false);
        }

        private void OnDestroy()
        {
            _isDestroyed = true;
            _lifecycleState = NetworkLifecycleState.Disposed;

            UnbindClientCallbacks();
            UnbindServerCallbacks();
            UnbindTransportErrors();
            InvalidateAuthorityConnection();
            InvalidateServerConnections();
            _messageHandlers.Clear();

            RuntimeContext?.Dispose();
            RuntimeContext = null;
        }

        private void RefreshMirrorBindings(bool forceRebind)
        {
            EnsureMainThread();
            bool clientActive = NetworkClient.active;
            bool serverActive = NetworkServer.active;

            if (clientActive)
            {
                if (forceRebind || !_clientCallbacksBound)
                    BindClientCallbacks();
            }
            else if (_clientCallbacksBound)
            {
                UnbindClientCallbacks();
                InvalidateAuthorityConnection();
            }

            if (serverActive)
            {
                if (forceRebind || !_serverCallbacksBound)
                    BindServerCallbacks();
            }
            else if (_serverCallbacksBound)
            {
                UnbindServerCallbacks();
                InvalidateServerConnections();
            }

            RefreshTransportErrorBinding();

        }

        private void BindClientCallbacks()
        {
            EnsureMainThread();
            NetworkClient.OnConnectedEvent -= HandleClientConnected;
            NetworkClient.OnDisconnectedEvent -= HandleClientDisconnected;
            NetworkClient.OnConnectedEvent += HandleClientConnected;
            NetworkClient.OnDisconnectedEvent += HandleClientDisconnected;

            NetworkClient.UnregisterHandler<CycloneWireFrameMessage>();
            NetworkClient.RegisterHandler<CycloneWireFrameMessage>(OnClientDataReceived);
            _clientCallbacksBound = true;
        }

        private void UnbindClientCallbacks()
        {
            EnsureMainThread();
            if (!_clientCallbacksBound)
                return;

            NetworkClient.OnConnectedEvent -= HandleClientConnected;
            NetworkClient.OnDisconnectedEvent -= HandleClientDisconnected;
            NetworkClient.UnregisterHandler<CycloneWireFrameMessage>();
            _clientCallbacksBound = false;
        }

        private void BindServerCallbacks()
        {
            EnsureMainThread();
            NetworkServer.OnConnectedEvent -= OnMirrorServerConnected;
            NetworkServer.OnDisconnectedEvent -= OnMirrorServerDisconnected;
            NetworkServer.OnConnectedEvent += OnMirrorServerConnected;
            NetworkServer.OnDisconnectedEvent += OnMirrorServerDisconnected;

            NetworkServer.UnregisterHandler<CycloneWireFrameMessage>();
            NetworkServer.RegisterHandler<CycloneWireFrameMessage>(OnServerDataReceived);
            _serverCallbacksBound = true;
        }

        private void UnbindServerCallbacks()
        {
            EnsureMainThread();
            if (!_serverCallbacksBound)
                return;

            NetworkServer.OnConnectedEvent -= OnMirrorServerConnected;
            NetworkServer.OnDisconnectedEvent -= OnMirrorServerDisconnected;
            NetworkServer.UnregisterHandler<CycloneWireFrameMessage>();
            _serverCallbacksBound = false;
        }

        private void RefreshTransportErrorBinding()
        {
            EnsureMainThread();
            global::Mirror.Transport activeTransport = global::Mirror.Transport.active;
            if (ReferenceEquals(_boundTransport, activeTransport))
                return;

            UnbindTransportErrors();
            _boundTransport = activeTransport;
            if (_boundTransport == null)
                return;

            _boundTransport.OnClientError += HandleClientError;
            _boundTransport.OnServerError += HandleServerError;
        }

        private void UnbindTransportErrors()
        {
            EnsureMainThread();
            if (_boundTransport == null)
                return;

            _boundTransport.OnClientError -= HandleClientError;
            _boundTransport.OnServerError -= HandleServerError;
            _boundTransport = null;
        }

        // Error Handlers
        private void HandleClientError(global::Mirror.TransportError error, string message)
        {
            EnsureMainThread();
            RaiseError(null, ConvertError(error), message);
        }

        private void HandleServerError(int connectionId, global::Mirror.TransportError error, string message)
        {
            EnsureMainThread();
            if (NetworkServer.connections.TryGetValue(connectionId, out var conn))
            {
                RaiseError(GetOrCreateServerConnection(conn), ConvertError(error), message);
            }
            else
            {
                RaiseError(null, ConvertError(error), message);
            }
        }

        private static Networking.TransportError ConvertError(global::Mirror.TransportError mirrorError)
        {
            return mirrorError switch
            {
                global::Mirror.TransportError.DnsResolve => Networking.TransportError.DnsResolve,
                global::Mirror.TransportError.Refused => Networking.TransportError.Refused,
                global::Mirror.TransportError.Timeout => Networking.TransportError.Timeout,
                global::Mirror.TransportError.Congestion => Networking.TransportError.Congestion,
                global::Mirror.TransportError.InvalidReceive => Networking.TransportError.InvalidReceive,
                global::Mirror.TransportError.InvalidSend => Networking.TransportError.InvalidSend,
                global::Mirror.TransportError.ConnectionClosed => Networking.TransportError.ConnectionClosed,
                global::Mirror.TransportError.Unexpected => Networking.TransportError.Unexpected,
                _ => Networking.TransportError.Unexpected
            };
        }

        public MessageSecurityPolicy DefaultMessageSecurityPolicy
        {
            get
            {
                EnsureMainThread();
                return EnsureMessagePolicies().DefaultPolicy;
            }
        }

        public void SetDefaultMessageSecurityPolicy(MessageSecurityPolicy policy)
        {
            EnsureMainThread();
            EnsureMessagePolicies().SetDefaultPolicy(policy);
        }

        public void SetMessageSecurityPolicy(ushort messageId, MessageSecurityPolicy policy)
        {
            EnsureMainThread();
            EnsureMessagePolicies().SetPolicy(messageId, policy);
        }

        public void ClearMessageSecurityPolicy(ushort messageId)
        {
            EnsureMainThread();
            EnsureMessagePolicies().ClearPolicy(messageId);
        }

        // State Management
        public ulong GetPlayerId(int connectionId)
        {
            EnsureMainThread();
            return _playerIds.TryGetValue(connectionId, out var id) ? id : 0;
        }

        public void SetPlayerId(int connectionId, ulong playerId)
        {
            EnsureMainThread();
            _playerIds[connectionId] = playerId;
        }

        internal MirrorConnectionData GetConnectionData(int connectionId)
        {
            EnsureMainThread();
            return _connectionData.TryGetValue(connectionId, out var data) ? data : default;
        }

        internal ulong GetPlayerId(MirrorNetConnection connection)
        {
            EnsureMainThread();
            return IsCurrentConnection(connection)
                ? GetPlayerId(connection.ConnectionId)
                : 0UL;
        }

        internal void SetPlayerId(MirrorNetConnection connection, ulong playerId)
        {
            EnsureMainThread();
            if (!IsCurrentConnection(connection))
                throw new ObjectDisposedException(nameof(MirrorNetConnection));

            SetPlayerId(connection.ConnectionId, playerId);
        }

        private MirrorNetConnection GetOrCreateServerConnection(NetworkConnectionToClient connection)
        {
            EnsureMainThread();
            if (connection == null)
                throw new ArgumentNullException(nameof(connection));

            int connectionId = connection.connectionId;
            if (_serverConnections.TryGetValue(connectionId, out MirrorNetConnection cached))
            {
                if (cached.Matches(connection))
                {
                    cached.Refresh(GetConnectionData(connectionId));
                    return cached;
                }

                cached.Invalidate(GetConnectionData(connectionId));
                _serverConnections.Remove(connectionId);
                _playerIds.Remove(connectionId);
                _connectionData[connectionId] = default;
                _securityPipeline?.RemoveConnection(connectionId);
            }

            var created = new MirrorNetConnection(this, connection, GetConnectionData(connectionId));
            _serverConnections.Add(connectionId, created);
            return created;
        }

        private MirrorNetConnection GetOrCreateAuthorityConnection(NetworkConnection connection)
        {
            EnsureMainThread();
            if (connection == null)
                return null;

            if (_authorityConnection != null)
            {
                if (_authorityConnection.Matches(connection))
                {
                    _authorityConnection.Refresh(default);
                    return _authorityConnection;
                }

                InvalidateAuthorityConnection();
            }

            _authorityConnection = new MirrorNetConnection(this, connection, default);
            return _authorityConnection;
        }

        private bool IsCurrentConnection(MirrorNetConnection connection)
        {
            if (connection == null || !connection.IsOwnedBy(this) || !connection.IsValid)
                return false;

            if (connection.IsServerConnection)
            {
                return ReferenceEquals(_authorityConnection, connection)
                       && ReferenceEquals(NetworkClient.connection, connection.Connection);
            }

            return _serverConnections.TryGetValue(connection.ConnectionId, out MirrorNetConnection current)
                   && ReferenceEquals(current, connection)
                   && NetworkServer.connections.TryGetValue(connection.ConnectionId, out NetworkConnectionToClient mirrorConnection)
                   && ReferenceEquals(mirrorConnection, connection.Connection);
        }

        private void InvalidateAuthorityConnection()
        {
            if (_authorityConnection == null)
                return;

            _authorityConnection.Invalidate(default);
            _authorityConnection = null;
            _securityPipeline?.RemoveConnection(0);
        }

        private void InvalidateServerConnections()
        {
            foreach (KeyValuePair<int, MirrorNetConnection> pair in _serverConnections)
            {
                pair.Value.Invalidate(GetConnectionData(pair.Key));
                _securityPipeline?.RemoveConnection(pair.Key);
            }

            _serverConnections.Clear();
            foreach (int connectionId in _connectionData.Keys)
                _securityPipeline?.RemoveConnection(connectionId);
            _playerIds.Clear();
            _connectionData.Clear();
        }

        // Lifecycle Handlers
        private void HandleClientConnected()
        {
            EnsureMainThread();
            GetOrCreateAuthorityConnection(NetworkClient.connection);
            _lifecycleState = NetworkLifecycle.GetTransportState(this);
            OnConnectedToServer?.Invoke();
        }

        private void HandleClientDisconnected()
        {
            EnsureMainThread();
            InvalidateAuthorityConnection();
            _lifecycleState = NetworkLifecycle.GetTransportState(this);
            OnDisconnectedFromServer?.Invoke();
        }

        private void OnMirrorServerConnected(NetworkConnectionToClient conn)
        {
            EnsureMainThread();
            _lifecycleState = NetworkLifecycle.GetTransportState(this);
            _connectionData[conn.connectionId] = new MirrorConnectionData
            {
                BytesSent = 0L,
                BytesReceived = 0L,
                Jitter = 0d,
                LastPing = 0
            };
            OnClientConnected?.Invoke(GetOrCreateServerConnection(conn));
        }

        private void OnMirrorServerDisconnected(NetworkConnectionToClient conn)
        {
            EnsureMainThread();
            MirrorNetConnection connection = GetOrCreateServerConnection(conn);
            MirrorConnectionData data = GetConnectionData(conn.connectionId);
            _playerIds.Remove(conn.connectionId);
            _connectionData.Remove(conn.connectionId);
            _securityPipeline?.RemoveConnection(conn.connectionId);
            _serverConnections.Remove(conn.connectionId);
            connection.Invalidate(data);
            _lifecycleState = NetworkLifecycle.GetTransportState(this);
            OnClientDisconnected?.Invoke(connection);
        }

        private void EnsureMainThread()
        {
            if (_mainThreadId == 0 || Thread.CurrentThread.ManagedThreadId != _mainThreadId)
            {
                throw new InvalidOperationException(
                    "MirrorNetAdapter and the injected Mirror runtime must be accessed on the Unity main thread after Awake has completed.");
            }
        }

        // INetTransport Implementation
        public void StartServer()
        {
            EnsureMainThread();
            if (!TryGetNetworkManager(out NetworkManager manager))
                return;

            if (global::Mirror.Transport.active == null)
            {
                RaiseError(null, TransportError.Unexpected, MissingTransportError);
                return;
            }

            _lifecycleState = NetworkLifecycleState.StartingServer;
            manager.StartServer();
            RefreshMirrorBindings(forceRebind: true);
            _lifecycleState = NetworkLifecycle.GetTransportState(this);
        }

        public void StartClient(string address)
        {
            EnsureMainThread();
            if (!TryGetNetworkManager(out NetworkManager manager))
                return;

            if (global::Mirror.Transport.active == null)
            {
                RaiseError(null, TransportError.Unexpected, MissingTransportError);
                return;
            }

            _lifecycleState = NetworkLifecycleState.StartingClient;
            manager.networkAddress = address ?? string.Empty;
            manager.StartClient();
            RefreshMirrorBindings(forceRebind: true);
            _lifecycleState = NetworkLifecycle.GetTransportState(this);
        }

        public void Stop()
        {
            EnsureMainThread();
            _lifecycleState = NetworkLifecycleState.Stopping;

            if (!TryGetNetworkManager(out NetworkManager manager))
                return;

            manager.StopHost();
            RefreshMirrorBindings(forceRebind: false);
            _lifecycleState = NetworkLifecycle.GetTransportState(this);
        }

        public void Disconnect(INetConnection connection)
        {
            EnsureMainThread();
            if (connection is MirrorNetConnection mc && IsCurrentConnection(mc))
            {
                if (mc.IsServerConnection)
                {
                    if (NetworkClient.active && NetworkManager.singleton != null)
                        NetworkManager.singleton.StopClient();
                }
                else if (NetworkServer.active && NetworkServer.connections.TryGetValue(mc.ConnectionId, out var conn))
                {
                    conn.Disconnect();
                }
            }
        }

        public NetworkSendResult Send(INetConnection connection, in ArraySegment<byte> payload, int channelId)
        {
            EnsureMainThread();
            if (!IsRunning)
                return NetworkSendResult.Fail(NetworkSendStatus.NotRunning, channelId, connection);

            if (connection is not MirrorNetConnection mirrorConn || !IsCurrentConnection(mirrorConn))
                return NetworkSendResult.Fail(NetworkSendStatus.NotConnected, channelId, connection);

            byte[] buffer = GetSendBuffer();
            NetworkChannel channel = GetNetworkChannel(channelId);
            int frameLength;
            try
            {
                frameLength = BuildFrame(NetworkConstants.SystemMsgIdMin, channel, payload, buffer);
            }
            catch (Exception e)
            {
                return NetworkSendResult.Fail(NetworkSendStatus.InvalidPayload, channelId, connection, e.Message);
            }

            var frame = new ArraySegment<byte>(buffer, 0, frameLength);
            if (mirrorConn.IsServerConnection)
            {
                if (!NetworkClient.active)
                    return NetworkSendResult.Fail(NetworkSendStatus.NotConnected, channelId, connection);

                SendFrameToServer(frame, channelId);
                Interlocked.Add(ref _bytesSent, frameLength);
                Interlocked.Increment(ref _packetsSent);
                return NetworkSendResult.Accepted(frameLength, channelId, connection);
            }

            if (NetworkServer.active && NetworkServer.connections.TryGetValue(mirrorConn.ConnectionId, out var conn))
            {
                SendFrameToConnection(conn, frame, channelId);
                Interlocked.Add(ref _bytesSent, frameLength);
                Interlocked.Increment(ref _packetsSent);
                return NetworkSendResult.Accepted(frameLength, channelId, connection);
            }

            return NetworkSendResult.Fail(NetworkSendStatus.NotConnected, channelId, connection);
        }

        public NetworkSendResult Broadcast(IReadOnlyList<INetConnection> connections, in ArraySegment<byte> payload, int channelId)
        {
            EnsureMainThread();
            if (connections == null)
                throw new ArgumentNullException(nameof(connections));

            int acceptedBytes = 0;
            int acceptedRecipients = 0;
            NetworkSendResult lastFailure = default;

            for (int i = 0; i < connections.Count; i++)
            {
                NetworkSendResult result = Send(connections[i], payload, channelId);
                if (result.Succeeded)
                {
                    acceptedBytes = SaturatingAdd(acceptedBytes, result.BytesAccepted);
                    acceptedRecipients++;
                }
                else
                {
                    lastFailure = result;
                }
            }

            if (acceptedRecipients > 0)
                return NetworkSendResult.Broadcast(NetworkSendStatus.Accepted, acceptedBytes, acceptedRecipients, channelId);

            return connections.Count == 0
                ? NetworkSendResult.Broadcast(NetworkSendStatus.Accepted, 0, 0, channelId)
                : lastFailure;
        }

        private void SendFrameToServer(ArraySegment<byte> frame, int channelId)
        {
            EnsureMainThread();
            if (!NetworkClient.active) return;
            var wireFrame = new CycloneWireFrameMessage { Frame = frame };
            NetworkClient.Send(wireFrame, channelId);
        }

        private void SendFrameBroadcast(ArraySegment<byte> frame, int channelId)
        {
            EnsureMainThread();
            if (!NetworkServer.active) return;
            var wireFrame = new CycloneWireFrameMessage { Frame = frame };
            NetworkServer.SendToAll(wireFrame, channelId);
        }

        private void SendFrameToConnection(NetworkConnectionToClient conn, ArraySegment<byte> frame, int channelId)
        {
            EnsureMainThread();
            var wireFrame = new CycloneWireFrameMessage { Frame = frame };
            conn.Send(wireFrame, channelId);
            RecordConnectionSent(conn.connectionId, frame.Count);
        }

        // INetworkMessageEndpoint implementation
        public NetworkMessageHandlerLease RegisterHandler(ushort messageId, NetworkMessageHandler handler)
        {
            EnsureMainThread();
            return _messageHandlers.Register(messageId, handler);
        }

        public NetworkSendResult SendToServer(
            ushort messageId,
            ReadOnlySpan<byte> payload,
            NetworkChannel channel = NetworkChannel.Reliable)
        {
            EnsureMainThread();
            if (!TryPrepareCanonicalPayload(messageId, channel, payload.Length, null, out int channelId, out NetworkSendResult failure))
                return failure;
            if (!NetworkClient.active)
                return NetworkSendResult.Fail(NetworkSendStatus.NotConnected, channelId);

            byte[] buffer = GetSendBuffer();
            try
            {
                int frameLength = BuildFrameUnchecked(messageId, channel, payload, buffer);
                SendFrameToServer(new ArraySegment<byte>(buffer, 0, frameLength), channelId);
                Interlocked.Add(ref _bytesSent, frameLength);
                Interlocked.Increment(ref _packetsSent);
                return NetworkSendResult.Accepted(frameLength, channelId);
            }
            catch (Exception e)
            {
                CLogger.LogError($"Failed to send canonical message {messageId}: {e}", LogCategory.Network);
                return NetworkSendResult.Fail(NetworkSendStatus.InvalidPayload, channelId, reason: e.Message);
            }
        }

        public NetworkSendResult SendToClient(
            INetConnection connection,
            ushort messageId,
            ReadOnlySpan<byte> payload,
            NetworkChannel channel = NetworkChannel.Reliable)
        {
            EnsureMainThread();
            if (!TryPrepareCanonicalPayload(messageId, channel, payload.Length, connection, out int channelId, out NetworkSendResult failure))
                return failure;
            if (connection is not MirrorNetConnection mirrorConnection || !IsCurrentConnection(mirrorConnection))
                return NetworkSendResult.Fail(NetworkSendStatus.NotConnected, channelId, connection);

            byte[] buffer = GetSendBuffer();
            try
            {
                int frameLength = BuildFrameUnchecked(messageId, channel, payload, buffer);
                if (!NetworkServer.connections.TryGetValue(mirrorConnection.ConnectionId, out NetworkConnectionToClient conn))
                    return NetworkSendResult.Fail(NetworkSendStatus.NotConnected, channelId, connection);

                SendFrameToConnection(conn, new ArraySegment<byte>(buffer, 0, frameLength), channelId);
                Interlocked.Add(ref _bytesSent, frameLength);
                Interlocked.Increment(ref _packetsSent);
                return NetworkSendResult.Accepted(frameLength, channelId, connection);
            }
            catch (Exception e)
            {
                CLogger.LogError($"Failed to send canonical message {messageId} to a client: {e}", LogCategory.Network);
                return NetworkSendResult.Fail(NetworkSendStatus.InvalidPayload, channelId, connection, e.Message);
            }
        }

        public NetworkSendResult BroadcastToClients(
            ushort messageId,
            ReadOnlySpan<byte> payload,
            NetworkChannel channel = NetworkChannel.Reliable)
        {
            EnsureMainThread();
            if (!TryPrepareCanonicalPayload(messageId, channel, payload.Length, null, out int channelId, out NetworkSendResult failure))
                return failure;
            if (!NetworkServer.active)
                return NetworkSendResult.Fail(NetworkSendStatus.NotRunning, channelId);

            byte[] buffer = GetSendBuffer();
            try
            {
                int frameLength = BuildFrameUnchecked(messageId, channel, payload, buffer);
                SendFrameBroadcast(new ArraySegment<byte>(buffer, 0, frameLength), channelId);
                int recipients = NetworkServer.connections.Count;
                int acceptedBytes = SaturatingMultiply(frameLength, recipients);
                Interlocked.Add(ref _bytesSent, acceptedBytes);
                Interlocked.Add(ref _packetsSent, recipients);
                return NetworkSendResult.Broadcast(
                    NetworkSendStatus.Accepted,
                    acceptedBytes,
                    recipients,
                    channelId);
            }
            catch (Exception e)
            {
                CLogger.LogError($"Failed to broadcast canonical message {messageId}: {e}", LogCategory.Network);
                return NetworkSendResult.Fail(NetworkSendStatus.InvalidPayload, channelId, reason: e.Message);
            }
        }

        public NetworkSendResult Broadcast(
            IReadOnlyList<INetConnection> connections,
            ushort messageId,
            ReadOnlySpan<byte> payload,
            NetworkChannel channel = NetworkChannel.Reliable)
        {
            EnsureMainThread();
            if (connections == null)
                throw new ArgumentNullException(nameof(connections));
            if (!TryPrepareCanonicalPayload(messageId, channel, payload.Length, null, out int channelId, out NetworkSendResult failure))
                return failure;

            byte[] buffer = GetSendBuffer();
            int frameLength;
            try
            {
                frameLength = BuildFrameUnchecked(messageId, channel, payload, buffer);
            }
            catch (Exception e)
            {
                return NetworkSendResult.Fail(NetworkSendStatus.InvalidPayload, channelId, reason: e.Message);
            }

            var frame = new ArraySegment<byte>(buffer, 0, frameLength);
            int acceptedRecipients = 0;
            for (int i = 0; i < connections.Count; i++)
            {
                if (connections[i] is not MirrorNetConnection mirrorConnection
                    || !IsCurrentConnection(mirrorConnection)
                    || !NetworkServer.connections.TryGetValue(mirrorConnection.ConnectionId, out NetworkConnectionToClient connection))
                {
                    continue;
                }

                SendFrameToConnection(connection, frame, channelId);
                acceptedRecipients++;
            }

            if (acceptedRecipients == 0 && connections.Count > 0)
                return NetworkSendResult.Fail(NetworkSendStatus.NotConnected, channelId);

            int acceptedBytes = SaturatingMultiply(frameLength, acceptedRecipients);
            Interlocked.Add(ref _bytesSent, acceptedBytes);
            Interlocked.Add(ref _packetsSent, acceptedRecipients);
            return NetworkSendResult.Broadcast(
                NetworkSendStatus.Accepted,
                acceptedBytes,
                acceptedRecipients,
                channelId);
        }

        private bool TryPrepareCanonicalPayload(
            ushort messageId,
            NetworkChannel channel,
            int payloadLength,
            INetConnection connection,
            out int channelId,
            out NetworkSendResult failure)
        {
            try
            {
                channelId = GetChannelId(channel);
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException)
            {
                channelId = (int)channel;
                failure = NetworkSendResult.Fail(NetworkSendStatus.ChannelUnavailable, channelId, connection, e.Message);
                return false;
            }

            if (!IsAcceptingMessages)
            {
                failure = NetworkSendResult.Fail(NetworkSendStatus.NotRunning, channelId, connection);
                return false;
            }

            int maximumPayloadSize = GetMaxPayloadSize(messageId, channel);
            if (maximumPayloadSize <= 0)
            {
                failure = NetworkSendResult.Fail(NetworkSendStatus.ChannelUnavailable, channelId, connection);
                return false;
            }

            if (payloadLength > maximumPayloadSize)
            {
                failure = NetworkSendResult.Fail(NetworkSendStatus.PayloadTooLarge, channelId, connection);
                return false;
            }

            failure = default;
            return true;
        }

        // Internal Handlers
        private void OnServerDataReceived(NetworkConnectionToClient conn, CycloneWireFrameMessage msg)
        {
            EnsureMainThread();
            Interlocked.Add(ref _bytesReceived, msg.Frame.Count);
            Interlocked.Increment(ref _packetsReceived);
            RecordConnectionReceived(conn.connectionId, msg.Frame.Count);
            MirrorNetConnection connection = GetOrCreateServerConnection(conn);
            if (!TryValidateIncoming(connection, msg.Frame, NetworkMessageDirection.ClientToServer, out NetworkEnvelopeHeader header, out ArraySegment<byte> payload))
                return;
            OnDataReceived?.Invoke(connection, payload, GetChannelId(header.Channel));
            DispatchCanonicalMessage(connection, NetworkMessageDirection.ClientToServer, in header, in payload);
        }

        private void OnClientDataReceived(CycloneWireFrameMessage msg)
        {
            EnsureMainThread();
            if (NetworkClient.connection == null)
                return;

            MirrorNetConnection connection = GetOrCreateAuthorityConnection(NetworkClient.connection);
            Interlocked.Add(ref _bytesReceived, msg.Frame.Count);
            Interlocked.Increment(ref _packetsReceived);
            if (!TryValidateIncoming(connection, msg.Frame, NetworkMessageDirection.ServerToClient, out NetworkEnvelopeHeader header, out ArraySegment<byte> payload))
                return;

            OnDataReceived?.Invoke(connection, payload, GetChannelId(header.Channel));
            DispatchCanonicalMessage(connection, NetworkMessageDirection.ServerToClient, in header, in payload);
        }

        private void DispatchCanonicalMessage(
            INetConnection connection,
            NetworkMessageDirection direction,
            in NetworkEnvelopeHeader header,
            in ArraySegment<byte> payload)
        {
            try
            {
                var bytes = new ReadOnlySpan<byte>(payload.Array, payload.Offset, payload.Count);
                var message = new NetworkMessagePayload(connection, direction, in header, bytes);
                _messageHandlers.TryDispatch(in message);
            }
            catch (Exception e)
            {
                CLogger.LogError($"Canonical message handler {header.MessageId} failed: {e}", LogCategory.Network);
            }
        }

        private bool TryValidateIncoming(
            INetConnection connection,
            ArraySegment<byte> frame,
            NetworkMessageDirection direction,
            out NetworkEnvelopeHeader header,
            out ArraySegment<byte> payload)
        {
            header = default;
            payload = default;

            NetworkFrameResult frameResult = NetworkFrameCodec.TryReadPayload(frame, out header, out payload);
            if (frameResult != NetworkFrameResult.Valid)
                return false;

            if (payload.Array == null)
                return false;

            int routePayloadLimit = GetMaxPayloadSize(header.MessageId, header.Channel);
            if (routePayloadLimit <= 0 || header.PayloadLength > routePayloadLimit)
                return false;

            if (_enableMessageValidation)
            {
                ValidationResult result = _messageValidator.Validate(header.MessageId, header.PayloadLength);
                if (result != ValidationResult.Valid)
                    return false;
            }

            ReadOnlySpan<byte> payloadSpan = new ReadOnlySpan<byte>(payload.Array, payload.Offset, payload.Count);
            if (NetworkFrameCodec.ValidateChecksum(header, payloadSpan) != NetworkFrameResult.Valid)
                return false;

            NetworkMessageEnvelope envelope = header.ToEnvelope(direction);
            NetworkSecurityPipelineResult securityResult = EnsureSecurityPipeline().ValidateInbound(
                connection,
                envelope,
                payloadSpan,
                ReadOnlySpan<byte>.Empty,
                IsEncrypted,
                Time.unscaledTimeAsDouble,
                frame.Count);
            if (!securityResult.Accepted)
                return false;

            return true;
        }

        private void RecordConnectionSent(int connectionId, int byteCount)
        {
            if (!_connectionData.TryGetValue(connectionId, out MirrorConnectionData data))
                return;

            data.BytesSent += byteCount;
            _connectionData[connectionId] = data;
            if (_serverConnections.TryGetValue(connectionId, out MirrorNetConnection connection))
                connection.UpdateStatistics(data);
        }

        private void RecordConnectionReceived(int connectionId, int byteCount)
        {
            if (!_connectionData.TryGetValue(connectionId, out MirrorConnectionData data))
                return;

            data.BytesReceived += byteCount;
            _connectionData[connectionId] = data;
            if (_serverConnections.TryGetValue(connectionId, out MirrorNetConnection connection))
                connection.UpdateStatistics(data);
        }

        private MessageSecurityPolicy CreateDefaultSecurityPolicy()
        {
            return new MessageSecurityPolicy(
                NetworkMessageDirectionMask.Any,
                _maxPayloadSize,
                _requireAuthenticatedMessages,
                _requireEncryptedTransport,
                enableReplayProtection: false);
        }

        private MessageSecurityPolicyRegistry EnsureMessagePolicies()
        {
            if (_messagePolicies != null)
                return _messagePolicies;

            _messagePolicies = new MessageSecurityPolicyRegistry(CreateDefaultSecurityPolicy());
            return _messagePolicies;
        }

        private NetworkSecurityPipeline CreateSecurityPipeline()
        {
            return new NetworkSecurityPipeline(new NetworkSecurityPipelineOptions
            {
                MessagePolicies = EnsureMessagePolicies(),
                RateLimiter = _enableRateLimiter ? _rateLimiter : null
            });
        }

        private NetworkSecurityPipeline EnsureSecurityPipeline()
        {
            return _securityPipeline ??= CreateSecurityPipeline();
        }

        private static int SaturatingMultiply(int left, int right)
        {
            long value = (long)left * right;
            return value >= int.MaxValue ? int.MaxValue : (int)value;
        }

        private static int SaturatingAdd(int left, int right)
        {
            long value = (long)left + right;
            return value >= int.MaxValue ? int.MaxValue : (int)value;
        }

        private NetworkLifecycleState GetCurrentLifecycleState()
        {
            if (_isDestroyed)
                return NetworkLifecycleState.Disposed;

            if (!Available)
                return NetworkLifecycleState.Unavailable;

            NetworkLifecycleState inferred = NetworkLifecycle.GetTransportState(this);
            if (inferred != NetworkLifecycleState.Stopped)
                return inferred;

            if (_lifecycleState == NetworkLifecycleState.StartingServer
                || _lifecycleState == NetworkLifecycleState.StartingClient
                || _lifecycleState == NetworkLifecycleState.Stopping
                || _lifecycleState == NetworkLifecycleState.Faulted)
            {
                return _lifecycleState;
            }

            return inferred;
        }

        private bool TryGetNetworkManager(out NetworkManager manager)
        {
            manager = NetworkManager.singleton;
            if (manager != null)
                return true;

            RaiseError(null, TransportError.Unexpected, MissingNetworkManagerError);
            return false;
        }

        private void RaiseError(INetConnection connection, TransportError error, string message)
        {
            _lastError = error;
            _lastErrorMessage = message ?? string.Empty;
            if (!IsRunning)
                _lifecycleState = NetworkLifecycleState.Faulted;
            OnError?.Invoke(connection, error, _lastErrorMessage);
        }
    }

    /// <summary>
    /// One cached wrapper per live Mirror route. The owner invalidates the wrapper on disconnect,
    /// backend connection replacement, or adapter shutdown.
    /// </summary>
    internal sealed class MirrorNetConnection : INetConnection
    {
        private MirrorNetAdapter _owner;
        private NetworkConnection _connection;

        internal bool IsServerConnection { get; }
        internal bool IsValid { get; private set; }
        internal NetworkConnection Connection => _connection;
        public int ConnectionId { get; }
        public string RemoteAddress { get; }
        public bool IsConnected { get; private set; }
        public bool IsAuthenticated { get; private set; }
        public int Ping { get; private set; }
        public ConnectionQuality Quality { get; private set; }
        public double Jitter { get; private set; }
        public long BytesSent { get; private set; }
        public long BytesReceived { get; private set; }

        public ulong PlayerId
        {
            get => _owner != null ? _owner.GetPlayerId(this) : 0UL;
            set
            {
                MirrorNetAdapter owner = GetOwner();
                owner.SetPlayerId(this, value);
            }
        }

        internal MirrorNetConnection(
            MirrorNetAdapter owner,
            NetworkConnection conn,
            MirrorNetAdapter.MirrorConnectionData data)
        {
            _owner = owner != null ? owner : throw new ArgumentNullException(nameof(owner));
            _connection = conn ?? throw new ArgumentNullException(nameof(conn));

            if (conn is NetworkConnectionToClient connToClient)
            {
                IsServerConnection = false;
                ConnectionId = connToClient.connectionId;
                RemoteAddress = connToClient.address;
                Ping = 0;
            }
            else
            {
                IsServerConnection = true;
                ConnectionId = 0;
                RemoteAddress = "server";
            }

            IsValid = true;
            Refresh(data);
        }

        internal bool IsOwnedBy(MirrorNetAdapter owner)
        {
            return ReferenceEquals(_owner, owner);
        }

        internal bool Matches(NetworkConnection connection)
        {
            return IsValid && ReferenceEquals(_connection, connection);
        }

        internal void Refresh(MirrorNetAdapter.MirrorConnectionData data)
        {
            if (!IsValid || _connection == null)
                return;

            IsConnected = _connection.isReady;
            IsAuthenticated = _connection.isAuthenticated;
            Ping = IsServerConnection ? (int)(NetworkTime.rtt * 1000d) : data.LastPing;
            UpdateStatistics(data);
        }

        internal void UpdateStatistics(MirrorNetAdapter.MirrorConnectionData data)
        {
            Jitter = data.Jitter;
            BytesSent = data.BytesSent;
            BytesReceived = data.BytesReceived;
            Quality = CalculateQuality(Ping, Jitter);
        }

        internal void Invalidate(MirrorNetAdapter.MirrorConnectionData data)
        {
            if (!IsValid)
                return;

            UpdateStatistics(data);
            IsConnected = false;
            IsAuthenticated = false;
            IsValid = false;
            _connection = null;
            _owner = null;
        }

        private MirrorNetAdapter GetOwner()
        {
            if (_owner == null)
                throw new ObjectDisposedException(nameof(MirrorNetAdapter));

            return _owner;
        }

        private static ConnectionQuality CalculateQuality(int ping, double jitter)
        {
            if (ping < 50 && jitter < 10) return ConnectionQuality.Excellent;
            if (ping < 100) return ConnectionQuality.Good;
            if (ping < 200) return ConnectionQuality.Fair;
            return ConnectionQuality.Poor;
        }

        public bool Equals(INetConnection other)
        {
            return ReferenceEquals(this, other);
        }

        public override bool Equals(object obj)
        {
            return obj is INetConnection other && Equals(other);
        }

        public override int GetHashCode()
        {
            return RuntimeHelpers.GetHashCode(this);
        }
    }
}
#endif
