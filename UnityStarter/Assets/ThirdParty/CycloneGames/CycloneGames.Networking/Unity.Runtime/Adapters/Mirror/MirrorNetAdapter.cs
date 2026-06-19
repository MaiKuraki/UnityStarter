#if CYCLONE_NETWORKING_HAS_MIRROR
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Buffers;
using System.Threading;
using Mirror;
using UnityEngine;
using CycloneGames.Networking.Serialization;
using CycloneGames.Networking.Buffers;
using CycloneGames.Networking.Security;
using CycloneGames.Networking.Services;
using CycloneGames.Logger;

using CycloneGames.Networking;

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
    /// Implements both low-level Transport and high-level NetworkManager.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MirrorNetAdapter : MonoBehaviour, INetTransport, INetworkManager, INetworkSerializerConfigurable, INetworkMessageSecurityConfigurable, INetworkRuntimeContextProvider, INetworkLifecycleProvider, INetworkFeatureProvider
    {
        private const string MissingNetworkManagerError = "Mirror NetworkManager.singleton is not available.";
        private const string MissingTransportError = "Mirror Transport.active is not available.";

        public static MirrorNetAdapter Instance { get; private set; }

        [Header("Config")]
        [Tooltip("If true, sets DontDestroyOnLoad.")]
        [SerializeField] private bool _singleton = true;

        [Tooltip("Use MessagePack serializer if available, otherwise Json.")]
        [SerializeField] private bool _useRecommendedSerializer = true;

        [Header("Security")]
        [SerializeField] private bool _enableMessageValidation = true;
        [SerializeField] private int _maxPayloadSize = NetworkConstants.DefaultMaxPayloadSize;
        [SerializeField] private bool _enableRateLimiter = true;
        [SerializeField] private int _maxMessagesPerSecond = 120;
        [SerializeField] private int _burstMessages = 40;
        [SerializeField] private int _maxQueuedPackets = 1024;
        [SerializeField] private bool _requireAuthenticatedMessages = false;
        [SerializeField] private bool _requireEncryptedTransport = false;

        // INetTransport Properties
        public bool IsServer => NetworkServer.active;
        public bool IsClient => NetworkClient.active;
        public bool IsRunning => IsServer || IsClient;
        public bool Available => global::Mirror.Transport.active != null && global::Mirror.Transport.active.Available();
        public NetworkBackendFeatures Features => NetworkBackendFeatures.RealtimeTransport | NetworkBackendFeatures.Relay;
        public NetworkTransportCapabilities Capabilities => new NetworkTransportCapabilities(
            "Mirror",
            NetworkTransportFeatureFlags.Client
            | NetworkTransportFeatureFlags.Server
            | NetworkTransportFeatureFlags.Host
            | NetworkTransportFeatureFlags.Reliable
            | NetworkTransportFeatureFlags.Unreliable
            | NetworkTransportFeatureFlags.Backpressure
            | NetworkTransportFeatureFlags.DedicatedServerCompatible,
            NetworkChannelFlags.Reliable | NetworkChannelFlags.Unreliable,
            NetworkConstants.DefaultMaxConnections,
            GetMaxPacketSize(GetChannelId(NetworkChannel.Reliable)),
            GetMaxPacketSize(GetChannelId(NetworkChannel.Reliable)),
            GetMaxPacketSize(GetChannelId(NetworkChannel.Unreliable)),
            _maxQueuedPackets);

        public bool IsEncrypted
        {
            get
            {
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
                NetworkChannel.ReliableUnordered => Channels.Reliable,
                NetworkChannel.UnreliableSequenced => Channels.Unreliable,
                _ => Channels.Reliable
            };
        }

        public int GetMaxPacketSize(int channelId)
        {
            var t = global::Mirror.Transport.active;
            return t != null ? t.GetMaxPacketSize(channelId) : 65535;
        }

        private NetworkChannel GetNetworkChannel(int channelId)
        {
            if (channelId == Channels.Unreliable)
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
            return new NetworkStatistics(
                _bytesSent,
                _bytesReceived,
                _packetsSent,
                _packetsReceived,
                NetworkServer.active ? NetworkServer.connections.Count : (NetworkClient.active ? 1 : 0)
            );
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

        // Events
        public event Action<INetConnection> OnClientConnected;
        public event Action<INetConnection> OnClientDisconnected;
        public event Action OnConnectedToServer;
        public event Action OnDisconnectedFromServer;
        public event Action<INetConnection, TransportError, string> OnError;
        public event Action<INetConnection, ArraySegment<byte>, int> OnDataReceived;

        // Internal State
        private readonly Dictionary<ushort, Action<INetConnection, ArraySegment<byte>>> _handlers =
            new Dictionary<ushort, Action<INetConnection, ArraySegment<byte>>>();

        private readonly Dictionary<int, ulong> _playerIds = new Dictionary<int, ulong>();
        private readonly Dictionary<int, MirrorConnectionData> _connectionData = new Dictionary<int, MirrorConnectionData>();

        [ThreadStatic]
        private static byte[] _threadLocalSendBuffer;

        private static byte[] GetThreadBuffer()
        {
            return _threadLocalSendBuffer ??= new byte[65535];
        }

        private int _mainThreadId;
        private int _sendSequence;
        private readonly ConcurrentQueue<QueuedPacket> _sendQueue = new ConcurrentQueue<QueuedPacket>();
        private int _queuedPacketCount;
        private MessageValidator _messageValidator;
        private RateLimiter _rateLimiter;
        private MessageSecurityPolicyRegistry _messagePolicies;
        private NetworkReplayGuard _replayGuard;
        private NetworkLifecycleState _lifecycleState = NetworkLifecycleState.Stopped;
        private TransportError _lastError;
        private string _lastErrorMessage = string.Empty;
        private bool _isDestroyed;

        private struct QueuedPacket
        {
            public byte[] Data;
            public int Length;
            public int ChannelId;
            public int TargetConnectionId;
            public bool IsBroadcast;
            public bool IsToServer;
            public bool IsDisconnect;
        }

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

            // Use recommended serializer (MessagePack if available, else Json)
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
                    NetworkRuntimeIds.Mirror,
                    "Mirror",
                    this,
                    Features)
                .AddService<INetworkMessageSecurityConfigurable>(this)
                .Build();

            // Mirror Events Hookup
            NetworkClient.OnConnectedEvent += HandleClientConnected;
            NetworkClient.OnDisconnectedEvent += HandleClientDisconnected;

            NetworkClient.RegisterHandler<CycloneWireFrameMessage>(OnClientDataReceived);
            NetworkServer.RegisterHandler<CycloneWireFrameMessage>(OnServerDataReceived);
        }

        private void Start()
        {
            NetworkServer.OnConnectedEvent += OnMirrorServerConnected;
            NetworkServer.OnDisconnectedEvent += OnMirrorServerDisconnected;

            // Hook into Mirror error events
            if (global::Mirror.Transport.active != null)
            {
                global::Mirror.Transport.active.OnClientError += HandleClientError;
                global::Mirror.Transport.active.OnServerError += HandleServerError;
            }
        }

        private void Update()
        {
            ProcessSendQueue();
        }

        private void ProcessSendQueue()
        {
            while (_sendQueue.TryDequeue(out var packet))
            {
                try
                {
                    if (packet.IsDisconnect)
                    {
                        if (NetworkServer.active && NetworkServer.connections.TryGetValue(packet.TargetConnectionId, out var conn))
                        {
                            conn.Disconnect();
                        }
                        else if (NetworkClient.active && packet.TargetConnectionId == 0)
                        {
                            NetworkManager.singleton.StopClient();
                        }
                        continue;
                    }

                    var segment = new ArraySegment<byte>(packet.Data, 0, packet.Length);

                    if (packet.IsToServer)
                    {
                        SendFrameToServer(segment, packet.ChannelId);
                    }
                    else if (packet.IsBroadcast)
                    {
                        SendFrameBroadcast(segment, packet.ChannelId);
                    }
                    else if (NetworkServer.connections.TryGetValue(packet.TargetConnectionId, out var conn))
                    {
                        SendFrameToConnection(conn, segment, packet.ChannelId);
                    }
                }
                catch (Exception e)
                {
                    CLogger.LogError($"Queue Process Error: {e}", LogCategory.Network);
                }
                finally
                {
                    if (packet.Data != null)
                        ArrayPool<byte>.Shared.Return(packet.Data);
                    Interlocked.Decrement(ref _queuedPacketCount);
                }
            }
        }

        private void OnDestroy()
        {
            _isDestroyed = true;
            _lifecycleState = NetworkLifecycleState.Disposed;

            if (NetServices.IsAvailable && (object)NetServices.Instance == this)
            {
                NetServices.Unregister(this);
            }

            if (NetworkClient.active) NetworkClient.UnregisterHandler<CycloneWireFrameMessage>();
            if (NetworkServer.active) NetworkServer.UnregisterHandler<CycloneWireFrameMessage>();

            NetworkClient.OnConnectedEvent -= HandleClientConnected;
            NetworkClient.OnDisconnectedEvent -= HandleClientDisconnected;
            NetworkServer.OnConnectedEvent -= OnMirrorServerConnected;
            NetworkServer.OnDisconnectedEvent -= OnMirrorServerDisconnected;

            if (global::Mirror.Transport.active != null)
            {
                global::Mirror.Transport.active.OnClientError -= HandleClientError;
                global::Mirror.Transport.active.OnServerError -= HandleServerError;
            }

            RuntimeContext?.Dispose();
            RuntimeContext = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
            _threadLocalSendBuffer = null;
        }

        // Error Handlers
        private void HandleClientError(global::Mirror.TransportError error, string message)
        {
            RaiseError(null, ConvertError(error), message);
        }

        private void HandleServerError(int connectionId, global::Mirror.TransportError error, string message)
        {
            if (NetworkServer.connections.TryGetValue(connectionId, out var conn))
            {
                RaiseError(new MirrorNetConnection(conn, GetConnectionData(connectionId)), ConvertError(error), message);
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

        // Injection Setup
        public void SetSerializer(INetSerializer serializer)
        {
            Serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

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

        // State Management
        public ulong GetPlayerId(int connectionId)
        {
            return _playerIds.TryGetValue(connectionId, out var id) ? id : 0;
        }

        public void SetPlayerId(int connectionId, ulong playerId)
        {
            _playerIds[connectionId] = playerId;
        }

        internal MirrorConnectionData GetConnectionData(int connectionId)
        {
            return _connectionData.TryGetValue(connectionId, out var data) ? data : default;
        }

        // Lifecycle Handlers
        private void HandleClientConnected()
        {
            _lifecycleState = NetworkLifecycle.GetTransportState(this);
            OnConnectedToServer?.Invoke();
        }

        private void HandleClientDisconnected()
        {
            _lifecycleState = NetworkLifecycle.GetTransportState(this);
            OnDisconnectedFromServer?.Invoke();
        }

        private void OnMirrorServerConnected(NetworkConnectionToClient conn)
        {
            _lifecycleState = NetworkLifecycle.GetTransportState(this);
            _connectionData[conn.connectionId] = new MirrorConnectionData
            {
                BytesSent = 0L,
                BytesReceived = 0L,
                Jitter = 0d,
                LastPing = 0
            };
            OnClientConnected?.Invoke(new MirrorNetConnection(conn, GetConnectionData(conn.connectionId)));
        }

        private void OnMirrorServerDisconnected(NetworkConnectionToClient conn)
        {
            _playerIds.Remove(conn.connectionId);
            _connectionData.Remove(conn.connectionId);
            _rateLimiter?.RemoveConnection(conn.connectionId);
            _replayGuard?.RemoveConnection(conn.connectionId);
            _lifecycleState = NetworkLifecycle.GetTransportState(this);
            OnClientDisconnected?.Invoke(new MirrorNetConnection(conn, default));
        }

        private bool EnqueuePacket(byte[] sourceBuffer, int length, int channelId, bool toServer, bool broadcast, int targetConnId)
        {
            if (sourceBuffer == null || length < 0 || length > sourceBuffer.Length)
                return false;

            if (!TryReserveQueueSlot())
                return false;

            try
            {
                byte[] rented = ArrayPool<byte>.Shared.Rent(length);
                Buffer.BlockCopy(sourceBuffer, 0, rented, 0, length);

                _sendQueue.Enqueue(new QueuedPacket
                {
                    Data = rented,
                    Length = length,
                    ChannelId = channelId,
                    IsToServer = toServer,
                    IsBroadcast = broadcast,
                    TargetConnectionId = targetConnId,
                    IsDisconnect = false
                });
                return true;
            }
            catch
            {
                Interlocked.Decrement(ref _queuedPacketCount);
                throw;
            }
        }

        private void EnqueueDisconnect(int targetConnId)
        {
            if (!TryReserveQueueSlot())
                return;

            _sendQueue.Enqueue(new QueuedPacket
            {
                IsDisconnect = true,
                TargetConnectionId = targetConnId,
                Data = null
            });
        }

        private bool TryReserveQueueSlot()
        {
            int queued = Interlocked.Increment(ref _queuedPacketCount);
            if (_maxQueuedPackets <= 0 || queued <= _maxQueuedPackets)
                return true;

            Interlocked.Decrement(ref _queuedPacketCount);
            CLogger.LogWarning("Mirror send queue limit reached. Dropping queued packet.", LogCategory.Network);
            return false;
        }

        private bool IsMainThread => Thread.CurrentThread.ManagedThreadId == _mainThreadId;

        // INetTransport Implementation
        public void StartServer()
        {
            if (!TryGetNetworkManager(out NetworkManager manager))
                return;

            if (global::Mirror.Transport.active == null)
            {
                RaiseError(null, TransportError.Unexpected, MissingTransportError);
                return;
            }

            _lifecycleState = NetworkLifecycleState.StartingServer;
            manager.StartServer();
            _lifecycleState = NetworkLifecycle.GetTransportState(this);
        }

        public void StartClient(string address)
        {
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
            _lifecycleState = NetworkLifecycle.GetTransportState(this);
        }

        public void Stop()
        {
            if (!TryGetNetworkManager(out NetworkManager manager))
                return;

            _lifecycleState = NetworkLifecycleState.Stopping;
            manager.StopHost();
            _lifecycleState = NetworkLifecycle.GetTransportState(this);
        }

        public void Disconnect(INetConnection connection)
        {
            if (connection is MirrorNetConnection mc)
            {
                if (IsMainThread)
                {
                    if (NetworkServer.active && NetworkServer.connections.TryGetValue(mc.ConnectionId, out var conn))
                    {
                        conn.Disconnect();
                    }
                }
                else
                {
                    EnqueueDisconnect(mc.ConnectionId);
                }
            }
        }

        public NetworkSendResult Send(INetConnection connection, in ArraySegment<byte> payload, int channelId)
        {
            if (!IsRunning)
                return NetworkSendResult.Fail(NetworkSendStatus.NotRunning, channelId, connection);

            if (connection is not MirrorNetConnection mirrorConn)
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

            if (IsMainThread)
            {
                var frame = new ArraySegment<byte>(buffer, 0, frameLength);
                if (NetworkServer.active && NetworkServer.connections.TryGetValue(mirrorConn.ConnectionId, out var conn))
                {
                    Interlocked.Add(ref _bytesSent, frameLength);
                    Interlocked.Increment(ref _packetsSent);
                    SendFrameToConnection(conn, frame, channelId);
                    return NetworkSendResult.Accepted(frameLength, channelId, connection);
                }

                if (NetworkClient.active)
                {
                    Interlocked.Add(ref _bytesSent, frameLength);
                    Interlocked.Increment(ref _packetsSent);
                    SendFrameToServer(frame, channelId);
                    return NetworkSendResult.Accepted(frameLength, channelId, connection);
                }

                return NetworkSendResult.Fail(NetworkSendStatus.NotConnected, channelId, connection);
            }

            if (EnqueuePacket(buffer, frameLength, channelId, false, false, mirrorConn.ConnectionId))
            {
                Interlocked.Add(ref _bytesSent, frameLength);
                Interlocked.Increment(ref _packetsSent);
                return NetworkSendResult.Queued(frameLength, channelId, connection);
            }

            return NetworkSendResult.Fail(NetworkSendStatus.Backpressure, channelId, connection, "Mirror send queue limit reached.");
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

        private void SendFrameToServer(ArraySegment<byte> frame, int channelId)
        {
            if (!NetworkClient.active) return;
            var wireFrame = new CycloneWireFrameMessage { Frame = frame };
            NetworkClient.Send(wireFrame, channelId);
        }

        private void SendFrameBroadcast(ArraySegment<byte> frame, int channelId)
        {
            if (!NetworkServer.active) return;
            var wireFrame = new CycloneWireFrameMessage { Frame = frame };
            NetworkServer.SendToAll(wireFrame, channelId);
        }

        private void SendFrameToConnection(NetworkConnectionToClient conn, ArraySegment<byte> frame, int channelId)
        {
            var wireFrame = new CycloneWireFrameMessage { Frame = frame };
            conn.Send(wireFrame, channelId);
            RecordConnectionSent(conn.connectionId, frame.Count);
        }

        // INetworkManager Implementation
        public void RegisterHandler<T>(ushort msgId, Action<INetConnection, T> handler) where T : struct
        {
            if (_handlers.ContainsKey(msgId))
            {
                CLogger.LogWarning($"Overwriting handler for MsgId {msgId}", LogCategory.Network);
            }

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

        public void UnregisterHandler(ushort msgId)
        {
            _handlers.Remove(msgId);
        }

        public void DisconnectClient(INetConnection connection)
        {
            Disconnect(connection);
        }

        public NetworkSendResult SendToServer<T>(ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct
        {
            int channelId = GetChannelId(channel);
            byte[] buffer = GetThreadBuffer();

            try
            {
                Serializer.Serialize(message, buffer, NetworkWireProtocol.HeaderLength, out int written);
                int frameLength = WriteFrameHeader(msgId, channel, buffer, written);
                Interlocked.Add(ref _bytesSent, frameLength);
                Interlocked.Increment(ref _packetsSent);

                if (IsMainThread)
                {
                    SendFrameToServer(new ArraySegment<byte>(buffer, 0, frameLength), channelId);
                    return NetworkSendResult.Accepted(frameLength, channelId);
                }
                else
                {
                    return EnqueuePacket(buffer, frameLength, channelId, true, false, -1)
                        ? NetworkSendResult.Queued(frameLength, channelId)
                        : NetworkSendResult.Fail(NetworkSendStatus.Backpressure, channelId, reason: "Mirror send queue limit reached.");
                }
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
            byte[] buffer = GetThreadBuffer();

            try
            {
                Serializer.Serialize(message, buffer, NetworkWireProtocol.HeaderLength, out int written);
                int frameLength = WriteFrameHeader(msgId, channel, buffer, written);
                Interlocked.Add(ref _bytesSent, frameLength);
                Interlocked.Increment(ref _packetsSent);

                if (IsMainThread)
                {
                    if (connection is MirrorNetConnection mc && NetworkServer.connections.TryGetValue(mc.ConnectionId, out var conn))
                    {
                        SendFrameToConnection(conn, new ArraySegment<byte>(buffer, 0, frameLength), channelId);
                        return NetworkSendResult.Accepted(frameLength, channelId, connection);
                    }
                    return NetworkSendResult.Fail(NetworkSendStatus.NotConnected, channelId, connection);
                }
                else if (connection is MirrorNetConnection mc)
                {
                    return EnqueuePacket(buffer, frameLength, channelId, false, false, mc.ConnectionId)
                        ? NetworkSendResult.Queued(frameLength, channelId, connection)
                        : NetworkSendResult.Fail(NetworkSendStatus.Backpressure, channelId, connection, "Mirror send queue limit reached.");
                }

                return NetworkSendResult.Fail(NetworkSendStatus.NotConnected, channelId, connection);
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
            byte[] buffer = GetThreadBuffer();

            try
            {
                Serializer.Serialize(message, buffer, NetworkWireProtocol.HeaderLength, out int written);
                int frameLength = WriteFrameHeader(msgId, channel, buffer, written);
                Interlocked.Add(ref _bytesSent, frameLength);
                Interlocked.Increment(ref _packetsSent);

                if (IsMainThread)
                {
                    SendFrameBroadcast(new ArraySegment<byte>(buffer, 0, frameLength), channelId);
                    int recipients = NetworkServer.active ? NetworkServer.connections.Count : 0;
                    return NetworkSendResult.Broadcast(NetworkSendStatus.Accepted, frameLength * recipients, recipients, channelId);
                }
                else
                {
                    return EnqueuePacket(buffer, frameLength, channelId, false, true, 0)
                        ? NetworkSendResult.Queued(frameLength, channelId)
                        : NetworkSendResult.Fail(NetworkSendStatus.Backpressure, channelId, reason: "Mirror send queue limit reached.");
                }
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

            foreach (var conn in connections)
            {
                NetworkSendResult result = SendToClient(conn, msgId, message, channel);
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

        // Internal Handlers
        private void OnServerDataReceived(NetworkConnectionToClient conn, CycloneWireFrameMessage msg)
        {
            var connection = new MirrorNetConnection(conn, GetConnectionData(conn.connectionId));
            if (!TryValidateIncoming(connection, msg.Frame, NetworkMessageDirection.ClientToServer, out NetworkEnvelopeHeader header, out ArraySegment<byte> payload))
                return;

            RecordConnectionReceived(conn.connectionId, msg.Frame.Count);
            OnDataReceived?.Invoke(connection, payload, GetChannelId(header.Channel));

            if (_handlers.TryGetValue(header.MessageId, out var handler))
            {
                handler(connection, payload);
            }
        }

        private void OnClientDataReceived(CycloneWireFrameMessage msg)
        {
            if (NetworkClient.connection == null)
                return;

            var connection = new MirrorNetConnection(NetworkClient.connection, default);
            if (!TryValidateIncoming(connection, msg.Frame, NetworkMessageDirection.ServerToClient, out NetworkEnvelopeHeader header, out ArraySegment<byte> payload))
                return;

            OnDataReceived?.Invoke(connection, payload, GetChannelId(header.Channel));

            if (_handlers.TryGetValue(header.MessageId, out var handler))
            {
                handler(connection, payload);
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

        private void RecordConnectionSent(int connectionId, int byteCount)
        {
            if (!_connectionData.TryGetValue(connectionId, out MirrorConnectionData data))
                return;

            data.BytesSent += byteCount;
            _connectionData[connectionId] = data;
        }

        private void RecordConnectionReceived(int connectionId, int byteCount)
        {
            if (!_connectionData.TryGetValue(connectionId, out MirrorConnectionData data))
                return;

            data.BytesReceived += byteCount;
            _connectionData[connectionId] = data;
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
    /// Readonly struct wrapping Mirror's NetworkConnection with extended metrics.
    /// </summary>
    public readonly struct MirrorNetConnection : INetConnection
    {
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
            get => MirrorNetAdapter.Instance ? MirrorNetAdapter.Instance.GetPlayerId(ConnectionId) : 0;
            set => MirrorNetAdapter.Instance?.SetPlayerId(ConnectionId, value);
        }

        internal MirrorNetConnection(NetworkConnection conn, MirrorNetAdapter.MirrorConnectionData data)
        {
            if (conn is NetworkConnectionToClient connToClient)
            {
                ConnectionId = connToClient.connectionId;
                RemoteAddress = connToClient.address;
                Ping = 0;
            }
            else
            {
                ConnectionId = 0;
                RemoteAddress = "server";
                Ping = (int)(NetworkTime.rtt * 1000);
            }

            IsConnected = conn.isReady;
            IsAuthenticated = conn.isAuthenticated;
            Jitter = data.Jitter;
            BytesSent = data.BytesSent;
            BytesReceived = data.BytesReceived;
            Quality = CalculateQuality(Ping, Jitter);
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
            return other != null && ConnectionId == other.ConnectionId;
        }

        public override bool Equals(object obj)
        {
            return obj is INetConnection other && Equals(other);
        }

        public override int GetHashCode()
        {
            return ConnectionId;
        }
    }
}
#endif
