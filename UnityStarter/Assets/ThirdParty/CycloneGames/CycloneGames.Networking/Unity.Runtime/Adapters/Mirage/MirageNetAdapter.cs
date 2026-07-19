#if CYCLONE_NETWORKING_HAS_MIRAGE
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using UnityEngine;
using Mirage;
using CycloneGames.Networking.Security;
using CycloneGames.Logger;

namespace CycloneGames.Networking.Adapter.Mirage
{
    internal enum MirageConnectionRouteRole : byte
    {
        Authority = 1,
        RemoteClient = 2,
        HostLocal = 3
    }

    /// <summary>
    /// Carries one complete Cyclone wire frame through Mirage.
    /// The frame contains the Cyclone header followed by the serialized gameplay payload.
    /// </summary>
    public struct CycloneWireFrameMessage
    {
        public ArraySegment<byte> Frame;
    }

    [DisallowMultipleComponent]
    public sealed class MirageNetAdapter : MonoBehaviour, INetTransport, INetworkMessageEndpoint, INetworkSecurityPolicyConfigurable, INetworkRuntimeContextProvider, INetworkLifecycleProvider, INetworkFeatureProvider
    {
        private const int MirageMessageIdBytes = 2;
        private const int MiragePackedLengthMaxBytes = 5;
        private const string MissingServerError = "Mirage NetworkServer reference is not available.";
        private const string MissingClientError = "Mirage NetworkClient reference is not available.";

        [Header("Config")]
        [SerializeField] private bool _enableMessageValidation = true;
        [SerializeField] private int _maxPayloadSize = NetworkConstants.DefaultMaxPayloadSize;
        [SerializeField] private bool _enableRateLimiter = true;
        [SerializeField] private int _maxMessagesPerSecond = 120;
        [SerializeField] private int _burstMessages = 40;
        [SerializeField] private bool _requireAuthenticatedMessages = false;
        [SerializeField] private bool _requireEncryptedTransport = false;

        [Header("Mirage References")]
        [SerializeField] private NetworkServer _server;
        [SerializeField] private NetworkClient _client;

        public bool IsServer
        {
            get
            {
                EnsureMainThread();
                return _server != null && _server.Active;
            }
        }

        public bool IsClient
        {
            get
            {
                EnsureMainThread();
                return _client != null && _client.Active;
            }
        }

        public bool IsRunning => IsServer || IsClient;
        public bool Available
        {
            get
            {
                EnsureMainThread();
                return (_server != null && _server.SocketFactory != null)
                       || (_client != null && _client.SocketFactory != null);
            }
        }

        public bool IsEncrypted => false;
        public NetworkBackendFeatures Features => NetworkBackendFeatures.RealtimeTransport;
        public NetworkTransportCapabilities Capabilities
        {
            get
            {
                EnsureMainThread();
                return new NetworkTransportCapabilities(
                    "Mirage",
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

        public int GetChannelId(NetworkChannel channel)
        {
            return channel switch
            {
                NetworkChannel.Reliable => (int)Channel.Reliable,
                NetworkChannel.Unreliable => (int)Channel.Unreliable,
                NetworkChannel.ReliableUnordered => throw new NotSupportedException("Mirage does not advertise a reliable-unordered channel."),
                NetworkChannel.UnreliableSequenced => throw new NotSupportedException("Mirage does not advertise a distinct unreliable-sequenced channel."),
                _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "Unknown network channel.")
            };
        }

        public int GetMaxPacketSize(int channelId)
        {
            EnsureMainThread();
            if (channelId != (int)Channel.Reliable && channelId != (int)Channel.Unreliable)
                return 0;

            int maxPacketSize = int.MaxValue;
            if (_server != null && _server.SocketFactory != null)
                maxPacketSize = Math.Min(maxPacketSize, _server.SocketFactory.MaxPacketSize);
            if (_client != null && _client.SocketFactory != null)
                maxPacketSize = Math.Min(maxPacketSize, _client.SocketFactory.MaxPacketSize);

            if (maxPacketSize == int.MaxValue)
                return 0;

            int maximumPayloadSize = maxPacketSize
                                     - MirageMessageIdBytes
                                     - MiragePackedLengthMaxBytes
                                     - NetworkWireProtocol.HeaderLength;
            return Math.Min(_maxPayloadSize, Math.Max(0, maximumPayloadSize));
        }

        private NetworkChannel GetNetworkChannel(int channelId)
        {
            if (channelId == (int)Channel.Reliable)
                return NetworkChannel.Reliable;
            if (channelId == (int)Channel.Unreliable)
                return NetworkChannel.Unreliable;

            throw new ArgumentOutOfRangeException(nameof(channelId), channelId, "Mirage channel id is not supported.");
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
                throw new InvalidOperationException("Payload exceeds the available Mirage message route.");

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
                throw new InvalidOperationException("Payload exceeds the configured Mirage payload limit.");

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
            int connCount = 0;
            if (IsServer && _server.AuthenticatedPlayers != null)
                connCount = _server.AuthenticatedPlayers.Count;
            else if (IsClient)
                connCount = 1;

            return new NetworkStatistics(
                Interlocked.Read(ref _bytesSent), Interlocked.Read(ref _bytesReceived),
                Volatile.Read(ref _packetsSent), Volatile.Read(ref _packetsReceived),
                connCount);
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

        public event Action<INetConnection> OnClientConnected;
        public event Action<INetConnection> OnClientDisconnected;
        public event Action OnConnectedToServer;
        public event Action OnDisconnectedFromServer;
        public event Action<INetConnection, TransportError, string> OnError;
        public event Action<INetConnection, ArraySegment<byte>, int> OnDataReceived;

        // Internal
        private readonly NetworkMessageHandlerRegistry _messageHandlers =
            new NetworkMessageHandlerRegistry();

        private readonly Dictionary<INetworkPlayer, ulong> _playerIds = new Dictionary<INetworkPlayer, ulong>();
        private readonly Dictionary<INetworkPlayer, int> _connectionIds = new Dictionary<INetworkPlayer, int>();
        private readonly Dictionary<INetworkPlayer, MirageConnectionData> _connectionData =
            new Dictionary<INetworkPlayer, MirageConnectionData>();
        private readonly Dictionary<INetworkPlayer, MirageNetConnection> _serverConnections =
            new Dictionary<INetworkPlayer, MirageNetConnection>();
        private MirageNetConnection _authorityConnection;
        private int _nextConnectionId;
        private int _sendSequence;
        private MessageValidator _messageValidator;
        private RateLimiter _rateLimiter;
        private MessageSecurityPolicyRegistry _messagePolicies;
        private NetworkSecurityPipeline _securityPipeline;
        private NetworkLifecycleState _lifecycleState = NetworkLifecycleState.Stopped;
        private TransportError _lastError;
        private string _lastErrorMessage = string.Empty;
        private bool _isDestroyed;
        private int _mainThreadId;
        private bool _serverEventsBound;
        private bool _clientEventsBound;
        private MessageHandler _serverMessageHandler;
        private MessageHandler _clientMessageHandler;
        private INetworkPlayer _clientPlayer;

        private readonly byte[] _sendBuffer = new byte[NetworkConstants.MaxMTU];
        private byte[] GetSendBuffer() => _sendBuffer;

        internal struct MirageConnectionData
        {
            public long BytesSent;
            public long BytesReceived;
            public string RemoteAddress;
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
                    NetworkRuntimeIds.Mirage,
                    "Mirage",
                    this,
                    Features)
                .AddService<INetworkSecurityPolicyConfigurable>(this)
                .AddSecurityPipeline(_securityPipeline)
                .Build();

            // Auto-find if not assigned
            if (_server == null) _server = GetComponent<NetworkServer>();
            if (_client == null) _client = GetComponent<NetworkClient>();

            SetupServerEvents();
            SetupClientEvents();
        }

        private void SetupServerEvents()
        {
            EnsureMainThread();
            if (_server == null || _serverEventsBound)
                return;

            _server.Started.AddListener(HandleServerStarted);
            _server.Stopped.AddListener(HandleServerStopped);
            _server.Authenticated.AddListener(HandleServerAuthenticated);
            _server.Disconnected.AddListener(HandleServerDisconnected);
            _serverEventsBound = true;
            BindServerMessageHandler();
        }

        private void SetupClientEvents()
        {
            EnsureMainThread();
            if (_client == null || _clientEventsBound)
                return;

            _client.Started.AddListener(HandleClientStarted);
            _client.Connected.AddListener(HandleClientConnected);
            _client.Disconnected.AddListener(HandleClientDisconnected);
            _clientEventsBound = true;
            BindClientMessageHandler();
        }

        private void HandleServerStarted()
        {
            EnsureMainThread();
            BindServerMessageHandler();
        }

        private void HandleServerStopped()
        {
            EnsureMainThread();
            UnbindServerMessageHandler();
            InvalidateServerConnections();
        }

        private void HandleServerAuthenticated(INetworkPlayer player)
        {
            EnsureMainThread();
            BindServerMessageHandler();
            _lifecycleState = NetworkLifecycle.GetTransportState(this);
            MirageConnectionData data = CreateConnectionData(player);
            _connectionData[player] = data;
            OnClientConnected?.Invoke(GetOrCreateServerConnection(player));
        }

        private void HandleServerDisconnected(INetworkPlayer player)
        {
            EnsureMainThread();
            MirageNetConnection disconnectedConnection = GetOrCreateServerConnection(player);
            MirageConnectionData data = GetConnectionData(player);
            _serverConnections.Remove(player);
            disconnectedConnection.Invalidate(data);
            ReleasePlayerStateIfUnreferenced(player, disconnectedConnection.ConnectionId);
            _lifecycleState = NetworkLifecycle.GetTransportState(this);
            OnClientDisconnected?.Invoke(disconnectedConnection);
        }

        private void HandleClientStarted()
        {
            EnsureMainThread();
            BindClientMessageHandler();
        }

        private void HandleClientConnected(INetworkPlayer player)
        {
            EnsureMainThread();
            _clientPlayer = player;
            _connectionData[player] = CreateConnectionData(player);
            GetOrCreateAuthorityConnection(player);
            BindClientMessageHandler();
            _lifecycleState = NetworkLifecycle.GetTransportState(this);
            OnConnectedToServer?.Invoke();
        }

        private void HandleClientDisconnected(ClientStoppedReason reason)
        {
            EnsureMainThread();
            UnbindClientMessageHandler();
            INetworkPlayer player = _clientPlayer ?? _authorityConnection?.Player;
            _clientPlayer = null;
            InvalidateAuthorityConnection();
            if (player != null && _connectionIds.TryGetValue(player, out int connectionId))
                ReleasePlayerStateIfUnreferenced(player, connectionId);
            _lifecycleState = NetworkLifecycle.GetTransportState(this);
            OnDisconnectedFromServer?.Invoke();
        }

        private void HandleServerWireFrame(INetworkPlayer player, CycloneWireFrameMessage message)
        {
            EnsureMainThread();
            HandleDataReceived(player, message);
        }

        private void HandleClientWireFrame(INetworkPlayer player, CycloneWireFrameMessage message)
        {
            EnsureMainThread();
            OnClientDataReceived(message);
        }

        private void BindServerMessageHandler()
        {
            EnsureMainThread();
            MessageHandler messageHandler = _server != null ? _server.MessageHandler : null;
            if (ReferenceEquals(_serverMessageHandler, messageHandler))
                return;

            UnbindServerMessageHandler();
            if (messageHandler == null)
                return;

            messageHandler.RegisterHandler<CycloneWireFrameMessage>(HandleServerWireFrame, allowUnauthenticated: false);
            _serverMessageHandler = messageHandler;
        }

        private void BindClientMessageHandler()
        {
            EnsureMainThread();
            MessageHandler messageHandler = _client != null ? _client.MessageHandler : null;
            if (ReferenceEquals(_clientMessageHandler, messageHandler))
                return;

            UnbindClientMessageHandler();
            if (messageHandler == null)
                return;

            messageHandler.RegisterHandler<CycloneWireFrameMessage>(HandleClientWireFrame, allowUnauthenticated: false);
            _clientMessageHandler = messageHandler;
        }

        private void UnbindServerMessageHandler()
        {
            EnsureMainThread();
            if (_serverMessageHandler == null)
                return;

            _serverMessageHandler.UnregisterHandler<CycloneWireFrameMessage>();
            _serverMessageHandler = null;
        }

        private void UnbindClientMessageHandler()
        {
            EnsureMainThread();
            if (_clientMessageHandler == null)
                return;

            _clientMessageHandler.UnregisterHandler<CycloneWireFrameMessage>();
            _clientMessageHandler = null;
        }

        private void TeardownServerEvents()
        {
            EnsureMainThread();
            UnbindServerMessageHandler();
            if (_server != null && _serverEventsBound)
            {
                _server.Started.RemoveListener(HandleServerStarted);
                _server.Stopped.RemoveListener(HandleServerStopped);
                _server.Authenticated.RemoveListener(HandleServerAuthenticated);
                _server.Disconnected.RemoveListener(HandleServerDisconnected);
            }

            _serverEventsBound = false;
        }

        private void TeardownClientEvents()
        {
            EnsureMainThread();
            UnbindClientMessageHandler();
            if (_client != null && _clientEventsBound)
            {
                _client.Started.RemoveListener(HandleClientStarted);
                _client.Connected.RemoveListener(HandleClientConnected);
                _client.Disconnected.RemoveListener(HandleClientDisconnected);
            }

            _clientEventsBound = false;
        }

        private void OnDestroy()
        {
            TeardownServerEvents();
            TeardownClientEvents();
            _isDestroyed = true;
            _lifecycleState = NetworkLifecycleState.Disposed;

            InvalidateAuthorityConnection();
            InvalidateServerConnections();
            foreach (int connectionId in _connectionIds.Values)
                _securityPipeline?.RemoveConnection(connectionId);
            _playerIds.Clear();
            _connectionIds.Clear();
            _connectionData.Clear();
            _messageHandlers.Clear();
            _clientPlayer = null;

            RuntimeContext?.Dispose();
            RuntimeContext = null;
        }

        private void EnsureMainThread()
        {
            if (_mainThreadId == 0 || Thread.CurrentThread.ManagedThreadId != _mainThreadId)
            {
                throw new InvalidOperationException(
                    "MirageNetAdapter must be accessed on the Unity main thread after Awake has completed.");
            }
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

        internal ulong GetPlayerId(INetworkPlayer player)
        {
            EnsureMainThread();
            return _playerIds.TryGetValue(player, out var id) ? id : 0;
        }

        internal void SetPlayerId(INetworkPlayer player, ulong playerId)
        {
            EnsureMainThread();
            _playerIds[player] = playerId;
        }

        internal ulong GetPlayerId(MirageNetConnection connection)
        {
            EnsureMainThread();
            return IsCurrentConnection(connection)
                ? GetPlayerId(connection.Player)
                : 0UL;
        }

        internal void SetPlayerId(MirageNetConnection connection, ulong playerId)
        {
            EnsureMainThread();
            if (!IsCurrentConnection(connection))
                throw new ObjectDisposedException(nameof(MirageNetConnection));

            SetPlayerId(connection.Player, playerId);
        }

        internal MirageConnectionData GetConnectionData(INetworkPlayer player)
        {
            EnsureMainThread();
            if (player == null)
                return default;
            if (_connectionData.TryGetValue(player, out MirageConnectionData data))
                return data;

            data = CreateConnectionData(player);
            _connectionData[player] = data;
            return data;
        }

        private static MirageConnectionData CreateConnectionData(INetworkPlayer player)
        {
            return new MirageConnectionData
            {
                RemoteAddress = player?.ConnectionHandle?.ToString() ?? "unknown"
            };
        }

        private void RecordConnectionSent(INetworkPlayer player, int byteCount)
        {
            if (player == null || !_connectionData.TryGetValue(player, out MirageConnectionData data))
                return;

            data.BytesSent += byteCount;
            _connectionData[player] = data;
            if (_serverConnections.TryGetValue(player, out MirageNetConnection serverConnection))
                serverConnection.UpdateStatistics(data);
            if (_authorityConnection != null && ReferenceEquals(_authorityConnection.Player, player))
                _authorityConnection.UpdateStatistics(data);
        }

        private void RecordConnectionReceived(INetworkPlayer player, int byteCount)
        {
            if (player == null || !_connectionData.TryGetValue(player, out MirageConnectionData data))
                return;

            data.BytesReceived += byteCount;
            _connectionData[player] = data;
            if (_serverConnections.TryGetValue(player, out MirageNetConnection serverConnection))
                serverConnection.UpdateStatistics(data);
            if (_authorityConnection != null && ReferenceEquals(_authorityConnection.Player, player))
                _authorityConnection.UpdateStatistics(data);
        }

        internal int GetConnectionId(INetworkPlayer player)
        {
            EnsureMainThread();
            if (player == null)
                return 0;

            if (_connectionIds.TryGetValue(player, out int connectionId))
                return connectionId;

            if (_nextConnectionId == int.MaxValue)
                throw new InvalidOperationException("Mirage connection ID space is exhausted for this adapter lifetime.");

            connectionId = checked(_nextConnectionId + 1);
            _nextConnectionId = connectionId;
            _connectionIds[player] = connectionId;
            return connectionId;
        }

        private MirageNetConnection GetOrCreateServerConnection(INetworkPlayer player)
        {
            EnsureMainThread();
            if (player == null)
                throw new ArgumentNullException(nameof(player));

            MirageConnectionRouteRole routeRole = GetServerRouteRole(player);
            if (_serverConnections.TryGetValue(player, out MirageNetConnection cached))
            {
                if (cached.Matches(player, routeRole))
                {
                    cached.Refresh(GetConnectionData(player));
                    return cached;
                }

                _serverConnections.Remove(player);
                cached.Invalidate(GetConnectionData(player));
                ReleasePlayerStateIfUnreferenced(player, cached.ConnectionId);
            }

            var created = new MirageNetConnection(
                this,
                player,
                GetConnectionId(player),
                routeRole,
                GetConnectionData(player));
            _serverConnections.Add(player, created);
            return created;
        }

        private MirageNetConnection GetOrCreateAuthorityConnection(INetworkPlayer player)
        {
            EnsureMainThread();
            if (player == null)
                return null;

            if (_authorityConnection != null)
            {
                if (_authorityConnection.Matches(player, MirageConnectionRouteRole.Authority))
                {
                    _authorityConnection.Refresh(GetConnectionData(player));
                    return _authorityConnection;
                }

                InvalidateAuthorityConnection();
            }

            _authorityConnection = new MirageNetConnection(
                this,
                player,
                GetConnectionId(player),
                MirageConnectionRouteRole.Authority,
                GetConnectionData(player));
            return _authorityConnection;
        }

        private bool IsCurrentConnection(MirageNetConnection connection)
        {
            if (connection == null || !connection.IsOwnedBy(this) || !connection.IsValid || connection.Player == null)
                return false;

            if (connection.RouteRole == MirageConnectionRouteRole.Authority)
            {
                return ReferenceEquals(_authorityConnection, connection)
                       && _client != null
                       && _client.Active
                       && ReferenceEquals(_client.Player, connection.Player);
            }

            return _serverConnections.TryGetValue(connection.Player, out MirageNetConnection current)
                   && ReferenceEquals(current, connection)
                   && _server != null
                   && _server.Active
                   && connection.RouteRole == GetServerRouteRole(connection.Player);
        }

        private void InvalidateAuthorityConnection()
        {
            if (_authorityConnection == null)
                return;

            MirageNetConnection connection = _authorityConnection;
            INetworkPlayer player = connection.Player;
            int connectionId = connection.ConnectionId;
            MirageConnectionData data = GetConnectionData(player);
            _authorityConnection = null;
            connection.Invalidate(data);
            ReleasePlayerStateIfUnreferenced(player, connectionId);
        }

        private void InvalidateServerConnections()
        {
            while (_serverConnections.Count > 0)
            {
                Dictionary<INetworkPlayer, MirageNetConnection>.Enumerator enumerator =
                    _serverConnections.GetEnumerator();
                if (!enumerator.MoveNext())
                    break;

                KeyValuePair<INetworkPlayer, MirageNetConnection> pair = enumerator.Current;
                enumerator.Dispose();
                _serverConnections.Remove(pair.Key);
                pair.Value.Invalidate(GetConnectionData(pair.Key));
                ReleasePlayerStateIfUnreferenced(pair.Key, pair.Value.ConnectionId);
            }
        }

        private void ReleasePlayerStateIfUnreferenced(INetworkPlayer player, int connectionId)
        {
            if (player == null
                || _serverConnections.ContainsKey(player)
                || (_authorityConnection != null && ReferenceEquals(_authorityConnection.Player, player)))
            {
                return;
            }

            _playerIds.Remove(player);
            _connectionIds.Remove(player);
            _connectionData.Remove(player);
            if (connectionId != 0)
                _securityPipeline?.RemoveConnection(connectionId);
        }

        // INetTransport
        public void StartServer()
        {
            EnsureMainThread();
            if (_server == null)
            {
                RaiseError(null, TransportError.Unexpected, MissingServerError);
                return;
            }

            SetupServerEvents();
            _lifecycleState = NetworkLifecycleState.StartingServer;
            _server.StartServer();
            BindServerMessageHandler();
            _lifecycleState = NetworkLifecycle.GetTransportState(this);
        }

        public void StartClient(string address)
        {
            EnsureMainThread();
            if (_client == null)
            {
                RaiseError(null, TransportError.Unexpected, MissingClientError);
                return;
            }

            SetupClientEvents();
            _lifecycleState = NetworkLifecycleState.StartingClient;
            _client.Connect(address ?? string.Empty);
            BindClientMessageHandler();
            _lifecycleState = NetworkLifecycle.GetTransportState(this);
        }

        public void Stop()
        {
            EnsureMainThread();
            _lifecycleState = NetworkLifecycleState.Stopping;
            UnbindServerMessageHandler();
            UnbindClientMessageHandler();
            _server?.Stop();
            _client?.Disconnect();
            _lifecycleState = NetworkLifecycle.GetTransportState(this);
        }

        public void Disconnect(INetConnection connection)
        {
            EnsureMainThread();
            if (connection is MirageNetConnection mc && IsAuthenticatedRemoteClient(mc))
                mc.Player.Disconnect();
        }

        private bool IsAuthenticatedRemoteClient(MirageNetConnection connection)
        {
            if (!IsCurrentConnection(connection)
                || connection.RouteRole != MirageConnectionRouteRole.RemoteClient
                || connection.Player == null
                || !connection.Player.IsAuthenticated
                || _server == null
                || !_server.Active
                || ReferenceEquals(connection.Player, _server.LocalPlayer))
            {
                return false;
            }

            IReadOnlyList<INetworkPlayer> authenticatedPlayers = _server.AuthenticatedPlayers;
            if (authenticatedPlayers == null)
                return false;

            for (int i = 0; i < authenticatedPlayers.Count; i++)
            {
                if (ReferenceEquals(authenticatedPlayers[i], connection.Player))
                    return true;
            }

            return false;
        }

        private int GetAuthenticatedRemoteClientCount()
        {
            if (_server == null || !_server.Active || _server.AuthenticatedPlayers == null)
                return 0;

            IReadOnlyList<INetworkPlayer> authenticatedPlayers = _server.AuthenticatedPlayers;
            INetworkPlayer localPlayer = _server.LocalPlayer;
            int count = 0;
            for (int i = 0; i < authenticatedPlayers.Count; i++)
            {
                INetworkPlayer player = authenticatedPlayers[i];
                if (player != null
                    && player.IsAuthenticated
                    && !ReferenceEquals(player, localPlayer))
                {
                    count++;
                }
            }

            return count;
        }

        private MirageConnectionRouteRole GetServerRouteRole(INetworkPlayer player)
        {
            return _server != null && ReferenceEquals(player, _server.LocalPlayer)
                ? MirageConnectionRouteRole.HostLocal
                : MirageConnectionRouteRole.RemoteClient;
        }

        public NetworkSendResult Send(INetConnection connection, in ArraySegment<byte> payload, int channelId)
        {
            EnsureMainThread();
            if (!IsRunning)
                return NetworkSendResult.Fail(NetworkSendStatus.NotRunning, channelId, connection);

            if (connection is not MirageNetConnection mc || !IsCurrentConnection(mc))
                return NetworkSendResult.Fail(NetworkSendStatus.NotConnected, channelId, connection);

            byte[] buffer = GetSendBuffer();
            NetworkChannel channel = GetNetworkChannel(channelId);
            try
            {
                int frameLength = BuildFrame(NetworkConstants.SystemMsgIdMin, channel, payload, buffer);
                var wireFrame = new CycloneWireFrameMessage { Frame = new ArraySegment<byte>(buffer, 0, frameLength) };
                if (mc.RouteRole == MirageConnectionRouteRole.Authority)
                {
                    if (_client == null
                        || !_client.Active
                        || !ReferenceEquals(_client.Player, mc.Player))
                    {
                        return NetworkSendResult.Fail(NetworkSendStatus.NotConnected, channelId, connection);
                    }

                    _client.Send(wireFrame, (Channel)channelId);
                }
                else
                {
                    if (!IsAuthenticatedRemoteClient(mc))
                        return NetworkSendResult.Fail(NetworkSendStatus.NotConnected, channelId, connection);

                    mc.Player.Send(wireFrame, (Channel)channelId);
                }
                RecordConnectionSent(mc.Player, frameLength);
                Interlocked.Add(ref _bytesSent, frameLength);
                Interlocked.Increment(ref _packetsSent);
                return NetworkSendResult.Accepted(frameLength, channelId, connection);
            }
            catch (Exception e)
            {
                return NetworkSendResult.Fail(NetworkSendStatus.InvalidPayload, channelId, connection, e.Message);
            }
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
                if (connections[i] is not MirageNetConnection connection
                    || !IsAuthenticatedRemoteClient(connection))
                {
                    lastFailure = NetworkSendResult.Fail(NetworkSendStatus.NotConnected, channelId, connections[i]);
                    continue;
                }

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

        // INetworkMessageEndpoint
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
            if (_client == null || !_client.Active)
                return NetworkSendResult.Fail(NetworkSendStatus.NotConnected, channelId);

            byte[] buffer = GetSendBuffer();
            try
            {
                int frameLength = BuildFrameUnchecked(messageId, channel, payload, buffer);
                var wireFrame = new CycloneWireFrameMessage { Frame = new ArraySegment<byte>(buffer, 0, frameLength) };
                _client.Send(wireFrame, (Channel)channelId);
                RecordConnectionSent(_client.Player, frameLength);
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
            if (connection is not MirageNetConnection mirageConnection
                || !IsAuthenticatedRemoteClient(mirageConnection))
            {
                return NetworkSendResult.Fail(NetworkSendStatus.NotConnected, channelId, connection);
            }

            byte[] buffer = GetSendBuffer();
            try
            {
                int frameLength = BuildFrameUnchecked(messageId, channel, payload, buffer);
                var wireFrame = new CycloneWireFrameMessage { Frame = new ArraySegment<byte>(buffer, 0, frameLength) };
                mirageConnection.Player.Send(wireFrame, (Channel)channelId);
                RecordConnectionSent(mirageConnection.Player, frameLength);
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
            if (_server == null || !_server.Active)
                return NetworkSendResult.Fail(NetworkSendStatus.NotRunning, channelId);

            byte[] buffer = GetSendBuffer();
            try
            {
                int frameLength = BuildFrameUnchecked(messageId, channel, payload, buffer);
                var wireFrame = new CycloneWireFrameMessage { Frame = new ArraySegment<byte>(buffer, 0, frameLength) };
                _server.SendToAll(
                    wireFrame,
                    authenticatedOnly: true,
                    excludeLocalPlayer: true,
                    channelId: (Channel)channelId);
                int recipients = GetAuthenticatedRemoteClientCount();
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

            var wireFrame = new CycloneWireFrameMessage { Frame = new ArraySegment<byte>(buffer, 0, frameLength) };
            int acceptedRecipients = 0;
            for (int i = 0; i < connections.Count; i++)
            {
                if (connections[i] is not MirageNetConnection connection
                    || !IsAuthenticatedRemoteClient(connection))
                {
                    continue;
                }

                connection.Player.Send(wireFrame, (Channel)channelId);
                RecordConnectionSent(connection.Player, frameLength);
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

        // Data Handlers
        private void HandleDataReceived(INetworkPlayer player, CycloneWireFrameMessage msg)
        {
            EnsureMainThread();
            Interlocked.Add(ref _bytesReceived, msg.Frame.Count);
            Interlocked.Increment(ref _packetsReceived);
            RecordConnectionReceived(player, msg.Frame.Count);
            MirageNetConnection connection = GetOrCreateServerConnection(player);
            if (!TryValidateIncoming(connection, msg.Frame, NetworkMessageDirection.ClientToServer, out NetworkEnvelopeHeader header, out ArraySegment<byte> payload))
                return;

            OnDataReceived?.Invoke(connection, payload, GetChannelId(header.Channel));
            DispatchCanonicalMessage(connection, NetworkMessageDirection.ClientToServer, in header, in payload);
        }

        private void OnClientDataReceived(CycloneWireFrameMessage msg)
        {
            EnsureMainThread();
            if (_client.Player == null)
                return;

            Interlocked.Add(ref _bytesReceived, msg.Frame.Count);
            Interlocked.Increment(ref _packetsReceived);
            RecordConnectionReceived(_client.Player, msg.Frame.Count);
            MirageNetConnection connection = GetOrCreateAuthorityConnection(_client.Player);
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

        private void RaiseError(INetConnection connection, TransportError error, string message)
        {
            EnsureMainThread();
            _lastError = error;
            _lastErrorMessage = message ?? string.Empty;
            if (!IsRunning)
                _lifecycleState = NetworkLifecycleState.Faulted;
            OnError?.Invoke(connection, error, _lastErrorMessage);
        }
    }

    /// <summary>
    /// One cached wrapper per live Mirage route. Host mode intentionally keeps separate authority
    /// and host-local wrappers because their routing permissions differ.
    /// </summary>
    internal sealed class MirageNetConnection : INetConnection
    {
        private MirageNetAdapter _owner;
        private INetworkPlayer _player;

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
            set => GetOwner().SetPlayerId(this, value);
        }

        internal MirageNetConnection(
            MirageNetAdapter owner,
            INetworkPlayer player,
            int connectionId,
            MirageConnectionRouteRole routeRole,
            MirageNetAdapter.MirageConnectionData data)
        {
            _owner = owner != null ? owner : throw new ArgumentNullException(nameof(owner));
            _player = player ?? throw new ArgumentNullException(nameof(player));
            ConnectionId = connectionId;
            RouteRole = routeRole;
            RemoteAddress = string.IsNullOrEmpty(data.RemoteAddress) ? "unknown" : data.RemoteAddress;
            Ping = 0;
            Jitter = 0;
            Quality = ConnectionQuality.Good;
            IsValid = true;
            Refresh(data);
        }

        internal INetworkPlayer Player => _player;
        internal MirageConnectionRouteRole RouteRole { get; }
        internal bool IsValid { get; private set; }

        internal bool IsOwnedBy(MirageNetAdapter owner)
        {
            return ReferenceEquals(_owner, owner);
        }

        internal bool Matches(INetworkPlayer player, MirageConnectionRouteRole routeRole)
        {
            return IsValid && ReferenceEquals(_player, player) && RouteRole == routeRole;
        }

        internal void Refresh(MirageNetAdapter.MirageConnectionData data)
        {
            if (!IsValid || _player == null)
                return;

            IsConnected = _player.IsConnected;
            IsAuthenticated = _player.IsAuthenticated;
            UpdateStatistics(data);
        }

        internal void UpdateStatistics(MirageNetAdapter.MirageConnectionData data)
        {
            BytesSent = data.BytesSent;
            BytesReceived = data.BytesReceived;
        }

        internal void Invalidate(MirageNetAdapter.MirageConnectionData data)
        {
            if (!IsValid)
                return;

            UpdateStatistics(data);
            IsConnected = false;
            IsAuthenticated = false;
            IsValid = false;
            _player = null;
            _owner = null;
        }

        private MirageNetAdapter GetOwner()
        {
            if (_owner == null)
                throw new ObjectDisposedException(nameof(MirageNetAdapter));

            return _owner;
        }

        public bool Equals(INetConnection other)
        {
            return ReferenceEquals(this, other);
        }

        public override bool Equals(object obj) => obj is INetConnection other && Equals(other);

        public override int GetHashCode()
        {
            return RuntimeHelpers.GetHashCode(this);
        }
    }
}
#endif
