#if MIRROR
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Buffers;
using System.Threading;
using Mirror;
using UnityEngine;
using CycloneGames.Networking.Serialization;
using CycloneGames.Networking.Buffers;
using CycloneGames.Networking.Services;
using CycloneGames.Logger;

using CycloneGames.Networking;

namespace CycloneGames.Networking.Adapter.Mirror
{
    /// <summary>
    /// Wraps a generic byte array for Mirror transmission.
    /// </summary>
    public struct CycloneRawMessage : NetworkMessage
    {
        public ushort MsgId;
        public ArraySegment<byte> Payload;
    }

    /// <summary>
    /// Mirror implementation of the Cyclone Networking stack.
    /// Implements both low-level Transport and high-level NetworkManager.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MirrorNetAdapter : MonoBehaviour, INetTransport, INetworkManager
    {
        public static MirrorNetAdapter Instance { get; private set; }

        [Header("Config")]
        [Tooltip("If true, sets DontDestroyOnLoad.")]
        [SerializeField] private bool _singleton = true;

        [Tooltip("Use MessagePack serializer if available, otherwise Json.")]
        [SerializeField] private bool _useRecommendedSerializer = true;

        // INetTransport Properties
        public bool IsServer => NetworkServer.active;
        public bool IsClient => NetworkClient.active;
        public bool IsRunning => IsServer || IsClient;
        public bool Available => global::Mirror.Transport.active != null && global::Mirror.Transport.active.Available();

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

        public INetTransport Transport => this;
        public INetSerializer Serializer { get; private set; }

        // Events
        public event Action<INetConnection> OnClientConnected;
        public event Action<INetConnection> OnClientDisconnected;
        public event Action OnConnectedToServer;
        public event Action OnDisconnectedFromServer;
        public event Action<INetConnection, TransportError, string> OnError;

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
        private readonly ConcurrentQueue<QueuedPacket> _sendQueue = new ConcurrentQueue<QueuedPacket>();

        private struct QueuedPacket
        {
            public ushort MsgId;
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
                : JsonSerializerAdapter.Instance;

            // Mirror Events Hookup
            NetworkClient.OnConnectedEvent += HandleClientConnected;
            NetworkClient.OnDisconnectedEvent += HandleClientDisconnected;

            NetworkClient.RegisterHandler<CycloneRawMessage>(OnClientDataReceived);
            NetworkServer.RegisterHandler<CycloneRawMessage>(OnServerDataReceived);
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
                        SendRawToServer(packet.MsgId, segment, packet.ChannelId);
                    }
                    else if (packet.IsBroadcast)
                    {
                        SendRawBroadcast(packet.MsgId, segment, packet.ChannelId);
                    }
                    else if (NetworkServer.connections.TryGetValue(packet.TargetConnectionId, out var conn))
                    {
                        SendRawToConnection(conn, packet.MsgId, segment, packet.ChannelId);
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
                }
            }
        }

        private void OnDestroy()
        {
            if (NetServices.IsAvailable && (object)NetServices.Instance == this)
            {
                NetServices.Unregister(this);
            }

            if (NetworkClient.active) NetworkClient.UnregisterHandler<CycloneRawMessage>();
            if (NetworkServer.active) NetworkServer.UnregisterHandler<CycloneRawMessage>();

            if (global::Mirror.Transport.active != null)
            {
                global::Mirror.Transport.active.OnClientError -= HandleClientError;
                global::Mirror.Transport.active.OnServerError -= HandleServerError;
            }
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
            OnError?.Invoke(null, ConvertError(error), message);
        }

        private void HandleServerError(int connectionId, global::Mirror.TransportError error, string message)
        {
            if (NetworkServer.connections.TryGetValue(connectionId, out var conn))
            {
                OnError?.Invoke(new MirrorNetConnection(conn, GetConnectionData(connectionId)), ConvertError(error), message);
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
            Serializer = serializer;
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
        private void HandleClientConnected() => OnConnectedToServer?.Invoke();
        private void HandleClientDisconnected() => OnDisconnectedFromServer?.Invoke();

        private void OnMirrorServerConnected(NetworkConnectionToClient conn)
        {
            _connectionData[conn.connectionId] = new MirrorConnectionData();
            OnClientConnected?.Invoke(new MirrorNetConnection(conn, default));
        }

        private void OnMirrorServerDisconnected(NetworkConnectionToClient conn)
        {
            _playerIds.Remove(conn.connectionId);
            _connectionData.Remove(conn.connectionId);
            OnClientDisconnected?.Invoke(new MirrorNetConnection(conn, default));
        }

        private void EnqueuePacket(ushort msgId, byte[] sourceBuffer, int length, int channelId, bool toServer, bool broadcast, int targetConnId)
        {
            byte[] rented = ArrayPool<byte>.Shared.Rent(length);
            Buffer.BlockCopy(sourceBuffer, 0, rented, 0, length);

            _sendQueue.Enqueue(new QueuedPacket
            {
                MsgId = msgId,
                Data = rented,
                Length = length,
                ChannelId = channelId,
                IsToServer = toServer,
                IsBroadcast = broadcast,
                TargetConnectionId = targetConnId,
                IsDisconnect = false
            });
        }

        private void EnqueueDisconnect(int targetConnId)
        {
            _sendQueue.Enqueue(new QueuedPacket
            {
                IsDisconnect = true,
                TargetConnectionId = targetConnId,
                Data = null
            });
        }

        private bool IsMainThread => Thread.CurrentThread.ManagedThreadId == _mainThreadId;

        // INetTransport Implementation
        public void StartServer() => NetworkManager.singleton.StartServer();

        public void StartClient(string address)
        {
            NetworkManager.singleton.networkAddress = address;
            NetworkManager.singleton.StartClient();
        }

        public void Stop() => NetworkManager.singleton.StopHost();

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

        public void Send(INetConnection connection, in ArraySegment<byte> payload, int channelId)
        {
            Interlocked.Add(ref _bytesSent, payload.Count);
            Interlocked.Increment(ref _packetsSent);

            if (IsMainThread)
            {
                var msg = new CycloneRawMessage { MsgId = 0, Payload = payload };
                if (connection is MirrorNetConnection mirrorConn)
                {
                    if (NetworkServer.active && NetworkServer.connections.TryGetValue(mirrorConn.ConnectionId, out var conn))
                    {
                        conn.Send(msg, channelId);
                    }
                    else if (NetworkClient.active)
                    {
                        NetworkClient.Send(msg, channelId);
                    }
                }
            }
            else if (connection is MirrorNetConnection mirrorConn)
            {
                EnqueuePacket(0, payload.Array!, payload.Count, channelId, false, false, mirrorConn.ConnectionId);
            }
        }

        public void Broadcast(IReadOnlyList<INetConnection> connections, in ArraySegment<byte> payload, int channelId)
        {
            for (int i = 0; i < connections.Count; i++)
            {
                Send(connections[i], payload, channelId);
            }
        }

        private void SendRawToServer(ushort msgId, ArraySegment<byte> payload, int channelId)
        {
            if (!NetworkClient.active) return;
            var rawMsg = new CycloneRawMessage { MsgId = msgId, Payload = payload };
            NetworkClient.Send(rawMsg, channelId);
        }

        private void SendRawBroadcast(ushort msgId, ArraySegment<byte> payload, int channelId)
        {
            if (!NetworkServer.active) return;
            var rawMsg = new CycloneRawMessage { MsgId = msgId, Payload = payload };
            NetworkServer.SendToAll(rawMsg, channelId);
        }

        private void SendRawToConnection(NetworkConnectionToClient conn, ushort msgId, ArraySegment<byte> payload, int channelId)
        {
            var rawMsg = new CycloneRawMessage { MsgId = msgId, Payload = payload };
            conn.Send(rawMsg, channelId);
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

        public void SendToServer<T>(ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct
        {
            int channelId = GetChannelId(channel);
            byte[] buffer = GetThreadBuffer();

            try
            {
                Serializer.Serialize(message, buffer, 0, out int written);
                Interlocked.Add(ref _bytesSent, written);
                Interlocked.Increment(ref _packetsSent);

                if (IsMainThread)
                {
                    SendRawToServer(msgId, new ArraySegment<byte>(buffer, 0, written), channelId);
                }
                else
                {
                    EnqueuePacket(msgId, buffer, written, channelId, true, false, -1);
                }
            }
            catch (Exception e)
            {
                CLogger.LogError($"Failed to send message {msgId}: {e}", LogCategory.Network);
            }
        }

        public void SendToClient<T>(INetConnection connection, ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct
        {
            int channelId = GetChannelId(channel);
            byte[] buffer = GetThreadBuffer();

            try
            {
                Serializer.Serialize(message, buffer, 0, out int written);
                Interlocked.Add(ref _bytesSent, written);
                Interlocked.Increment(ref _packetsSent);

                if (IsMainThread)
                {
                    if (connection is MirrorNetConnection mc && NetworkServer.connections.TryGetValue(mc.ConnectionId, out var conn))
                    {
                        SendRawToConnection(conn, msgId, new ArraySegment<byte>(buffer, 0, written), channelId);
                    }
                }
                else if (connection is MirrorNetConnection mc)
                {
                    EnqueuePacket(msgId, buffer, written, channelId, false, false, mc.ConnectionId);
                }
            }
            catch (Exception e)
            {
                CLogger.LogError($"Failed to send to client: {e}", LogCategory.Network);
            }
        }

        public void BroadcastToClients<T>(ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct
        {
            int channelId = GetChannelId(channel);
            byte[] buffer = GetThreadBuffer();

            try
            {
                Serializer.Serialize(message, buffer, 0, out int written);
                Interlocked.Add(ref _bytesSent, written);
                Interlocked.Increment(ref _packetsSent);

                if (IsMainThread)
                {
                    SendRawBroadcast(msgId, new ArraySegment<byte>(buffer, 0, written), channelId);
                }
                else
                {
                    EnqueuePacket(msgId, buffer, written, channelId, false, true, 0);
                }
            }
            catch (Exception e)
            {
                CLogger.LogError($"Failed to broadcast: {e}", LogCategory.Network);
            }
        }

        public void Broadcast<T>(IReadOnlyList<INetConnection> connections, ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct
        {
            foreach (var conn in connections)
            {
                SendToClient(conn, msgId, message, channel);
            }
        }

        // Internal Handlers
        private void OnServerDataReceived(NetworkConnectionToClient conn, CycloneRawMessage msg)
        {
            if (_handlers.TryGetValue(msg.MsgId, out var handler))
            {
                handler(new MirrorNetConnection(conn, GetConnectionData(conn.connectionId)), msg.Payload);
            }
        }

        private void OnClientDataReceived(CycloneRawMessage msg)
        {
            if (_handlers.TryGetValue(msg.MsgId, out var handler))
            {
                handler(new MirrorNetConnection(NetworkClient.connection, default), msg.Payload);
            }
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
    }
}
#endif