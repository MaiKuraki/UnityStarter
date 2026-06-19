#if CYCLONE_NETWORKING_HAS_MIRAGE
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Mirage;
using CycloneGames.Networking.Serialization;
using CycloneGames.Networking.Buffers;
using CycloneGames.Networking.Security;
using CycloneGames.Networking.Services;
using CycloneGames.Logger;

namespace CycloneGames.Networking.Adapter.Mirage
{
    /// <summary>
    /// Carries one complete Cyclone wire frame through Mirage.
    /// The frame contains the Cyclone header followed by the serialized gameplay payload.
    /// </summary>
    public struct CycloneWireFrameMessage
    {
        public ArraySegment<byte> Frame;
    }

    [DisallowMultipleComponent]
    public sealed class MirageNetAdapter : MonoBehaviour, INetTransport, INetworkManager, INetworkSerializerConfigurable, INetworkMessageSecurityConfigurable, INetworkRuntimeContextProvider, INetworkLifecycleProvider, INetworkFeatureProvider
    {
        private const string MissingServerError = "Mirage NetworkServer reference is not available.";
        private const string MissingClientError = "Mirage NetworkClient reference is not available.";

        public static MirageNetAdapter Instance { get; private set; }

        [Header("Config")]
        [SerializeField] private bool _singleton = true;
        [SerializeField] private bool _useRecommendedSerializer = true;
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

        public bool IsServer => _server != null && _server.Active;
        public bool IsClient => _client != null && _client.Active;
        public bool IsRunning => IsServer || IsClient;
        public bool Available => true;
        public bool IsEncrypted => false;
        public NetworkBackendFeatures Features => NetworkBackendFeatures.RealtimeTransport | NetworkBackendFeatures.Relay;
        public NetworkTransportCapabilities Capabilities => new NetworkTransportCapabilities(
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

        public int GetChannelId(NetworkChannel channel)
        {
            return channel switch
            {
                NetworkChannel.Reliable => (int)Channel.Reliable,
                NetworkChannel.Unreliable => (int)Channel.Unreliable,
                NetworkChannel.ReliableUnordered => (int)Channel.Reliable,
                NetworkChannel.UnreliableSequenced => (int)Channel.Unreliable,
                _ => (int)Channel.Reliable
            };
        }

        public int GetMaxPacketSize(int channelId) => 65535;

        private NetworkChannel GetNetworkChannel(int channelId)
        {
            if (channelId == (int)Channel.Unreliable)
                return NetworkChannel.Unreliable;

            return NetworkChannel.Reliable;
        }

        private int BuildFrame(ushort msgId, NetworkChannel channel, in ArraySegment<byte> payload, byte[] buffer)
        {
            if (payload.Array == null)
                throw new ArgumentException("Payload must reference a valid array.", nameof(payload));
            if (payload.Offset < 0 || payload.Count < 0 || payload.Offset > payload.Array.Length || payload.Count > payload.Array.Length - payload.Offset)
                throw new ArgumentOutOfRangeException(nameof(payload));

            int frameLength = NetworkFrameCodec.GetFrameLength(payload.Count);
            if (frameLength > buffer.Length)
                throw new InvalidOperationException($"Frame size exceeds send buffer capacity: {frameLength}");

            Buffer.BlockCopy(payload.Array, payload.Offset, buffer, NetworkWireProtocol.HeaderLength, payload.Count);
            WriteFrameHeader(msgId, channel, buffer, payload.Count);
            return frameLength;
        }

        private int WriteFrameHeader(ushort msgId, NetworkChannel channel, byte[] buffer, int payloadLength)
        {
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
            int connCount = 0;
            if (IsServer && _server.AuthenticatedPlayers != null)
                connCount = _server.AuthenticatedPlayers.Count;
            else if (IsClient)
                connCount = 1;

            return new NetworkStatistics(
                _bytesSent, _bytesReceived,
                _packetsSent, _packetsReceived,
                connCount);
        }

        public NetworkLifecycleSnapshot GetLifecycleSnapshot()
        {
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
        public INetSerializer Serializer { get; private set; }
        public INetworkRuntimeContext RuntimeContext { get; private set; }

        public event Action<INetConnection> OnClientConnected;
        public event Action<INetConnection> OnClientDisconnected;
        public event Action OnConnectedToServer;
        public event Action OnDisconnectedFromServer;
        public event Action<INetConnection, TransportError, string> OnError;
        public event Action<INetConnection, ArraySegment<byte>, int> OnDataReceived;

        // Internal
        private readonly Dictionary<ushort, Action<INetConnection, ArraySegment<byte>>> _handlers =
            new Dictionary<ushort, Action<INetConnection, ArraySegment<byte>>>();

        private readonly Dictionary<INetworkPlayer, ulong> _playerIds = new Dictionary<INetworkPlayer, ulong>();
        private readonly Dictionary<INetworkPlayer, int> _connectionIds = new Dictionary<INetworkPlayer, int>();
        private readonly Dictionary<INetworkPlayer, MirageConnectionData> _connectionData =
            new Dictionary<INetworkPlayer, MirageConnectionData>();
        private int _nextConnectionId = 1;
        private int _sendSequence;
        private MessageValidator _messageValidator;
        private RateLimiter _rateLimiter;
        private MessageSecurityPolicyRegistry _messagePolicies;
        private NetworkReplayGuard _replayGuard;
        private NetworkLifecycleState _lifecycleState = NetworkLifecycleState.Stopped;
        private TransportError _lastError;
        private string _lastErrorMessage = string.Empty;
        private bool _isDestroyed;

        [ThreadStatic] private static byte[] _threadLocalSendBuffer;
        private static byte[] GetThreadBuffer() => _threadLocalSendBuffer ??= new byte[65535];

        internal struct MirageConnectionData
        {
            public long BytesSent;
            public long BytesReceived;
        }

        private void Awake()
        {
            if (_singleton)
            {
                if (Instance != null && Instance != this)
                {
                    Destroy(gameObject);
                    return;
                }
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Instance ??= this;
            }

            NetServices.Register(this);

            Serializer = _useRecommendedSerializer
                ? SerializerFactory.GetRecommended()
                : SerializerFactory.GetDefault();

            _messageValidator = new MessageValidator(Math.Max(1, _maxPayloadSize), 0);
            _rateLimiter = new RateLimiter(
                Math.Max(1, _maxMessagesPerSecond),
                Math.Max(1, _maxPayloadSize) * Math.Max(1, _maxMessagesPerSecond),
                Math.Max(0, _burstMessages));
            _messagePolicies = new MessageSecurityPolicyRegistry(CreateDefaultSecurityPolicy());
            _replayGuard = new NetworkReplayGuard();
            RuntimeContext = new NetworkRuntimeContext(
                    NetworkRuntimeIds.Mirage,
                    "Mirage",
                    this,
                    Features)
                .AddService<INetworkMessageSecurityConfigurable>(this)
                .Build();

            // Auto-find if not assigned
            if (_server == null) _server = GetComponent<NetworkServer>();
            if (_client == null) _client = GetComponent<NetworkClient>();

            SetupServerEvents();
            SetupClientEvents();
        }

        private void SetupServerEvents()
        {
            if (_server == null) return;

            _server.Authenticated.AddListener(player =>
            {
                _lifecycleState = NetworkLifecycle.GetTransportState(this);
                var data = new MirageConnectionData();
                _connectionData[player] = data;
                OnClientConnected?.Invoke(new MirageNetConnection(player, GetConnectionId(player), data));
            });

            _server.Disconnected.AddListener(player =>
            {
                _playerIds.Remove(player);
                if (_connectionIds.TryGetValue(player, out int connectionId))
                {
                    _rateLimiter?.RemoveConnection(connectionId);
                    _replayGuard?.RemoveConnection(connectionId);
                }
                _connectionIds.Remove(player);
                _connectionData.Remove(player);
                _lifecycleState = NetworkLifecycle.GetTransportState(this);
                OnClientDisconnected?.Invoke(new MirageNetConnection(player, 0, default));
            });

            _server.MessageHandler.RegisterHandler<CycloneWireFrameMessage>((player, msg) =>
            {
                HandleDataReceived(player, msg);
            }, allowUnauthenticated: false);
        }

        private void SetupClientEvents()
        {
            if (_client == null) return;

            _client.Connected.AddListener(player =>
            {
                _lifecycleState = NetworkLifecycle.GetTransportState(this);
                OnConnectedToServer?.Invoke();
            });

            _client.Disconnected.AddListener(reason =>
            {
                _lifecycleState = NetworkLifecycle.GetTransportState(this);
                OnDisconnectedFromServer?.Invoke();
            });

            _client.MessageHandler.RegisterHandler<CycloneWireFrameMessage>((player, msg) =>
            {
                OnClientDataReceived(msg);
            }, allowUnauthenticated: false);
        }

        private void OnDestroy()
        {
            _isDestroyed = true;
            _lifecycleState = NetworkLifecycleState.Disposed;

            if (NetServices.IsAvailable && (object)NetServices.Instance == this)
                NetServices.Unregister(this);

            RuntimeContext?.Dispose();
            RuntimeContext = null;

            if (Instance == this) Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
            _threadLocalSendBuffer = null;
        }

        public void SetSerializer(INetSerializer serializer) => Serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));

        public MessageSecurityPolicy DefaultMessageSecurityPolicy => EnsureMessagePolicies().DefaultPolicy;

        public void SetDefaultMessageSecurityPolicy(MessageSecurityPolicy policy)
        {
            EnsureMessagePolicies().SetDefaultPolicy(policy);
        }

        public void SetMessageSecurityPolicy(ushort messageId, MessageSecurityPolicy policy)
        {
            EnsureMessagePolicies().SetPolicy(messageId, policy);
        }

        public void ClearMessageSecurityPolicy(ushort messageId)
        {
            EnsureMessagePolicies().ClearPolicy(messageId);
        }

        internal ulong GetPlayerId(INetworkPlayer player)
        {
            return _playerIds.TryGetValue(player, out var id) ? id : 0;
        }

        internal void SetPlayerId(INetworkPlayer player, ulong playerId)
        {
            _playerIds[player] = playerId;
        }

        internal MirageConnectionData GetConnectionData(INetworkPlayer player)
        {
            return _connectionData.TryGetValue(player, out var data) ? data : default;
        }

        internal int GetConnectionId(INetworkPlayer player)
        {
            if (player == null)
                return 0;

            if (_connectionIds.TryGetValue(player, out int connectionId))
                return connectionId;

            connectionId = _nextConnectionId++;
            if (connectionId <= 0)
            {
                _nextConnectionId = 1;
                connectionId = _nextConnectionId++;
            }
            _connectionIds[player] = connectionId;
            return connectionId;
        }

        // INetTransport
        public void StartServer()
        {
            if (_server == null)
            {
                RaiseError(null, TransportError.Unexpected, MissingServerError);
                return;
            }

            _lifecycleState = NetworkLifecycleState.StartingServer;
            _server.StartServer();
            _lifecycleState = NetworkLifecycle.GetTransportState(this);
        }

        public void StartClient(string address)
        {
            if (_client == null)
            {
                RaiseError(null, TransportError.Unexpected, MissingClientError);
                return;
            }

            _lifecycleState = NetworkLifecycleState.StartingClient;
            _client.Connect(address ?? string.Empty);
            _lifecycleState = NetworkLifecycle.GetTransportState(this);
        }

        public void Stop()
        {
            _lifecycleState = NetworkLifecycleState.Stopping;
            _server?.Stop();
            _client?.Disconnect();
            _lifecycleState = NetworkLifecycle.GetTransportState(this);
        }

        public void Disconnect(INetConnection connection)
        {
            if (connection is MirageNetConnection mc && mc.Player != null)
                mc.Player.Disconnect();
        }

        public NetworkSendResult Send(INetConnection connection, in ArraySegment<byte> payload, int channelId)
        {
            if (!IsRunning)
                return NetworkSendResult.Fail(NetworkSendStatus.NotRunning, channelId, connection);

            if (connection is not MirageNetConnection mc || mc.Player == null)
                return NetworkSendResult.Fail(NetworkSendStatus.NotConnected, channelId, connection);

            byte[] buffer = GetThreadBuffer();
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

            Interlocked.Add(ref _bytesSent, frameLength);
            Interlocked.Increment(ref _packetsSent);

            mc.Player.Send(new ArraySegment<byte>(buffer, 0, frameLength), (Channel)channelId);
            return NetworkSendResult.Accepted(frameLength, channelId, connection);
        }

        public NetworkSendResult Broadcast(IReadOnlyList<INetConnection> connections, in ArraySegment<byte> payload, int channelId)
        {
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
                    acceptedBytes += result.BytesAccepted;
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

        // INetworkManager
        public void RegisterHandler<T>(ushort msgId, Action<INetConnection, T> handler) where T : struct
        {
            if (_handlers.ContainsKey(msgId))
                CLogger.LogWarning($"Overwriting handler for MsgId {msgId}", LogCategory.Network);

            _handlers[msgId] = (conn, payload) =>
            {
                try
                {
                    Interlocked.Add(ref _bytesReceived, payload.Count);
                    Interlocked.Increment(ref _packetsReceived);

                    var span = new ReadOnlySpan<byte>(payload.Array, payload.Offset, payload.Count);
                    T msg = Serializer.Deserialize<T>(span);
                    handler(conn, msg);
                }
                catch (Exception e)
                {
                    CLogger.LogError($"Error handling message {msgId}: {e}", LogCategory.Network);
                }
            };
        }

        public void UnregisterHandler(ushort msgId) => _handlers.Remove(msgId);

        public void DisconnectClient(INetConnection connection) => Disconnect(connection);

        public NetworkSendResult SendToServer<T>(ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct
        {
            int channelId = GetChannelId(channel);
            if (_client == null || !_client.Active)
                return NetworkSendResult.Fail(NetworkSendStatus.NotConnected, channelId);

            byte[] buffer = GetThreadBuffer();
            try
            {
                Serializer.Serialize(message, buffer, NetworkWireProtocol.HeaderLength, out int written);
                int frameLength = WriteFrameHeader(msgId, channel, buffer, written);
                Interlocked.Add(ref _bytesSent, frameLength);
                Interlocked.Increment(ref _packetsSent);

                var wireFrame = new CycloneWireFrameMessage { Frame = new ArraySegment<byte>(buffer, 0, frameLength) };
                _client.Send(wireFrame, (Channel)channelId);
                return NetworkSendResult.Accepted(frameLength, channelId);
            }
            catch (Exception e)
            {
                CLogger.LogError($"Failed to send message {msgId}: {e}", LogCategory.Network);
                return NetworkSendResult.Fail(NetworkSendStatus.InvalidPayload, channelId, reason: e.Message);
            }
        }

        public NetworkSendResult SendToClient<T>(INetConnection connection, ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct
        {
            int channelId = GetChannelId(channel);
            if (connection is not MirageNetConnection mc || mc.Player == null)
                return NetworkSendResult.Fail(NetworkSendStatus.NotConnected, channelId, connection);

            byte[] buffer = GetThreadBuffer();
            try
            {
                Serializer.Serialize(message, buffer, NetworkWireProtocol.HeaderLength, out int written);
                int frameLength = WriteFrameHeader(msgId, channel, buffer, written);
                Interlocked.Add(ref _bytesSent, frameLength);
                Interlocked.Increment(ref _packetsSent);

                var wireFrame = new CycloneWireFrameMessage { Frame = new ArraySegment<byte>(buffer, 0, frameLength) };
                mc.Player.Send(wireFrame, (Channel)channelId);
                return NetworkSendResult.Accepted(frameLength, channelId, connection);
            }
            catch (Exception e)
            {
                CLogger.LogError($"Failed to send to client: {e}", LogCategory.Network);
                return NetworkSendResult.Fail(NetworkSendStatus.InvalidPayload, channelId, connection, e.Message);
            }
        }

        public NetworkSendResult BroadcastToClients<T>(ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct
        {
            int channelId = GetChannelId(channel);
            if (_server == null || !_server.Active)
                return NetworkSendResult.Fail(NetworkSendStatus.NotRunning, channelId);

            byte[] buffer = GetThreadBuffer();
            try
            {
                Serializer.Serialize(message, buffer, NetworkWireProtocol.HeaderLength, out int written);
                int frameLength = WriteFrameHeader(msgId, channel, buffer, written);
                Interlocked.Add(ref _bytesSent, frameLength);
                Interlocked.Increment(ref _packetsSent);

                var wireFrame = new CycloneWireFrameMessage { Frame = new ArraySegment<byte>(buffer, 0, frameLength) };
                _server.SendToAll(wireFrame, false, (Channel)channelId);
                return NetworkSendResult.Broadcast(NetworkSendStatus.Accepted, frameLength, 0, channelId);
            }
            catch (Exception e)
            {
                CLogger.LogError($"Failed to broadcast: {e}", LogCategory.Network);
                return NetworkSendResult.Fail(NetworkSendStatus.InvalidPayload, channelId, reason: e.Message);
            }
        }

        public NetworkSendResult Broadcast<T>(IReadOnlyList<INetConnection> connections, ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct
        {
            if (connections == null)
                throw new ArgumentNullException(nameof(connections));

            int acceptedBytes = 0;
            int acceptedRecipients = 0;
            NetworkSendResult lastFailure = default;

            for (int i = 0; i < connections.Count; i++)
            {
                NetworkSendResult result = SendToClient(connections[i], msgId, message, channel);
                if (result.Succeeded)
                {
                    acceptedBytes += result.BytesAccepted;
                    acceptedRecipients++;
                }
                else
                {
                    lastFailure = result;
                }
            }

            int channelId = GetChannelId(channel);
            if (acceptedRecipients > 0)
                return NetworkSendResult.Broadcast(NetworkSendStatus.Accepted, acceptedBytes, acceptedRecipients, channelId);

            return connections.Count == 0
                ? NetworkSendResult.Broadcast(NetworkSendStatus.Accepted, 0, 0, channelId)
                : lastFailure;
        }

        // Data Handlers
        private void HandleDataReceived(INetworkPlayer player, CycloneWireFrameMessage msg)
        {
            var connection = new MirageNetConnection(player, GetConnectionId(player), GetConnectionData(player));
            if (!TryValidateIncoming(connection, msg.Frame, NetworkMessageDirection.ClientToServer, out NetworkEnvelopeHeader header, out ArraySegment<byte> payload))
                return;

            OnDataReceived?.Invoke(connection, payload, GetChannelId(header.Channel));

            if (_handlers.TryGetValue(header.MessageId, out var handler))
                handler(connection, payload);
        }

        private void OnClientDataReceived(CycloneWireFrameMessage msg)
        {
            if (_client.Player == null)
                return;

            var connection = new MirageNetConnection(_client.Player, GetConnectionId(_client.Player), default);
            if (!TryValidateIncoming(connection, msg.Frame, NetworkMessageDirection.ServerToClient, out NetworkEnvelopeHeader header, out ArraySegment<byte> payload))
                return;

            OnDataReceived?.Invoke(connection, payload, GetChannelId(header.Channel));

            if (_handlers.TryGetValue(header.MessageId, out var handler))
                handler(connection, payload);
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
            MessageSecurityResult securityResult = EnsureMessagePolicies().Validate(
                envelope,
                connection,
                IsEncrypted,
                _replayGuard);
            if (securityResult != MessageSecurityResult.Valid)
                return false;

            if (_enableRateLimiter && connection != null && _rateLimiter != null)
            {
                if (!_rateLimiter.TryConsume(connection.ConnectionId, frame.Count, Time.unscaledTime))
                    return false;
            }

            return true;
        }

        private MessageSecurityPolicy CreateDefaultSecurityPolicy()
        {
            return new MessageSecurityPolicy(
                NetworkMessageDirectionMask.Any,
                Math.Max(1, _maxPayloadSize),
                _requireAuthenticatedMessages,
                _requireEncryptedTransport,
                enableReplayProtection: false);
        }

        private MessageSecurityPolicyRegistry EnsureMessagePolicies()
        {
            if (_messagePolicies != null)
                return _messagePolicies;

            _messagePolicies = new MessageSecurityPolicyRegistry(CreateDefaultSecurityPolicy());
            _replayGuard ??= new NetworkReplayGuard();
            return _messagePolicies;
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
            _lastError = error;
            _lastErrorMessage = message ?? string.Empty;
            if (!IsRunning)
                _lifecycleState = NetworkLifecycleState.Faulted;
            OnError?.Invoke(connection, error, _lastErrorMessage);
        }
    }

    public readonly struct MirageNetConnection : INetConnection
    {
        internal readonly INetworkPlayer Player;

        public int ConnectionId { get; }
        public string RemoteAddress { get; }
        public bool IsConnected { get; }
        public bool IsAuthenticated { get; }
        public int Ping { get; }
        public ConnectionQuality Quality { get; }
        public double Jitter { get; }
        public long BytesSent { get; }
        public long BytesReceived { get; }

        public ulong PlayerId
        {
            get => MirageNetAdapter.Instance != null ? MirageNetAdapter.Instance.GetPlayerId(Player) : 0;
            set => MirageNetAdapter.Instance?.SetPlayerId(Player, value);
        }

        internal MirageNetConnection(INetworkPlayer player, int connectionId, MirageNetAdapter.MirageConnectionData data)
        {
            Player = player;
            ConnectionId = connectionId;
            RemoteAddress = player?.Connection?.EndPoint?.ToString() ?? "unknown";
            IsConnected = player?.IsConnected ?? false;
            IsAuthenticated = player?.IsAuthenticated ?? false;
            Ping = 0;
            Jitter = 0;
            BytesSent = data.BytesSent;
            BytesReceived = data.BytesReceived;
            Quality = ConnectionQuality.Good;
        }

        public bool Equals(INetConnection other) => other != null && ConnectionId == other.ConnectionId;
        public override bool Equals(object obj) => obj is INetConnection other && Equals(other);
        public override int GetHashCode() => ConnectionId;
    }
}
#endif
