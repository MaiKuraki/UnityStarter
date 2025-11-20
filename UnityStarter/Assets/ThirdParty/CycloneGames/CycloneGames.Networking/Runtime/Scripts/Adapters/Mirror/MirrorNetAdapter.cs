#if MIRROR
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Buffers;
using System.Threading;
using Mirror;
using UnityEngine;

namespace CycloneGames.Networking.Adapter.Mirror
{
    /// <summary>
    /// Wraps a generic byte array for Mirror transmission.
    /// </summary>
    public struct CycloneRawMessage : NetworkMessage
    {
        public ushort MsgId; // 0 for raw transport messages
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

        [Tooltip("Use a basic JSON serializer if none is provided via DI? Warning: Not Zero-GC.")]
        [SerializeField] private bool _useFallbackSerializer = true;

        // --- INetTransport Properties ---
        public bool IsServer => NetworkServer.active;
        public bool IsClient => NetworkClient.active;
        public bool IsRunning => IsServer || IsClient;
        public bool IsEncrypted
        {
            get
            {
                var t = global::Mirror.Transport.active;
                return t != null && t.IsEncrypted;
            }
        }

        // --- Channel Mapping ---
        public int GetChannelId(NetworkChannel channel)
        {
            switch (channel)
            {
                case NetworkChannel.Reliable: return Channels.Reliable;
                case NetworkChannel.Unreliable: return Channels.Unreliable;
                // Mirror default transport usually treats ReliableSequenced as Reliable (0)
                case NetworkChannel.ReliableUnordered: return Channels.Reliable;
                // UnreliableSequenced is typically Unreliable (1) in default KCP/Telepathy
                case NetworkChannel.UnreliableSequenced: return Channels.Unreliable;
                default: return Channels.Reliable;
            }
        }

        public INetTransport Transport => this;
        public INetSerializer Serializer { get; private set; }

        // --- Events ---
        public event Action<INetConnection> OnClientConnected;
        public event Action<INetConnection> OnClientDisconnected;
        public event Action OnConnectedToServer;
        public event Action OnDisconnectedFromServer;

        // --- Internal State ---
        private readonly Dictionary<ushort, Action<INetConnection, ArraySegment<byte>>> _handlers =
            new Dictionary<ushort, Action<INetConnection, ArraySegment<byte>>>();

        // Map ConnectionId -> PlayerId for persistence
        private readonly Dictionary<int, ulong> _playerIds = new Dictionary<int, ulong>();

        [ThreadStatic]
        private static byte[] _threadLocalSendBuffer;

        private static byte[] GetThreadBuffer()
        {
            if (_threadLocalSendBuffer == null)
                _threadLocalSendBuffer = new byte[65535];
            return _threadLocalSendBuffer;
        }

        private int _mainThreadId;

        private readonly ConcurrentQueue<QueuedPacket> _sendQueue = new ConcurrentQueue<QueuedPacket>();

        private struct QueuedPacket
        {
            public ushort MsgId;
            public byte[] Data; // Rented from ArrayPool
            public int Length;
            public int ChannelId;
            public int TargetConnectionId; // -1 for Server, 0 for Broadcast
            public bool IsBroadcast;
            public bool IsToServer;
            public bool IsDisconnect;
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
                if (Instance == null) Instance = this;
            }

            NetServices.Register(this);

            if (Serializer == null && _useFallbackSerializer)
            {
                Serializer = new FallbackJsonSerializer();
                Debug.LogWarning("[MirrorNetAdapter] Using FallbackJsonSerializer. For Zero-GC, please inject a specialized INetSerializer (e.g., MemoryPack).");
            }

            // Mirror Events Hookup
            NetworkClient.OnConnectedEvent += HandleClientConnected;
            NetworkClient.OnDisconnectedEvent += HandleClientDisconnected;

            // Register Raw Message Handler
            NetworkClient.RegisterHandler<CycloneRawMessage>(OnClientDataReceived);
            NetworkServer.RegisterHandler<CycloneRawMessage>(OnServerDataReceived);
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
                        // Handle disconnect request on main thread
                        if (NetworkServer.active && NetworkServer.connections.TryGetValue(packet.TargetConnectionId, out var conn))
                        {
                            conn.Disconnect();
                        }
                        else if (NetworkClient.active && packet.TargetConnectionId == 0) // Disconnect self
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
                    else
                    {
                        // Send to specific client
                        if (NetworkServer.connections.TryGetValue(packet.TargetConnectionId, out var conn))
                        {
                            SendRawToConnection(conn, packet.MsgId, segment, packet.ChannelId);
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[MirrorNetAdapter] Queue Process Error: {e}");
                }
                finally
                {
                    // Return the buffer to the pool if it exists (Disconnect packets might have null Data)
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
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
            _threadLocalSendBuffer = null;
        }

        // --- Injection Setup ---
        public void SetSerializer(INetSerializer serializer)
        {
            Serializer = serializer;
        }

        // --- State Management ---
        public ulong GetPlayerId(int connectionId)
        {
            return _playerIds.TryGetValue(connectionId, out var id) ? id : 0;
        }

        public void SetPlayerId(int connectionId, ulong playerId)
        {
            _playerIds[connectionId] = playerId;
        }

        // --- Lifecycle Handlers ---
        private void HandleClientConnected() => OnConnectedToServer?.Invoke();
        private void HandleClientDisconnected() => OnDisconnectedFromServer?.Invoke();

        private void Start()
        {
            NetworkServer.OnConnectedEvent += OnMirrorServerConnected;
            NetworkServer.OnDisconnectedEvent += OnMirrorServerDisconnected;
        }

        private void OnMirrorServerConnected(NetworkConnectionToClient conn)
        {
            OnClientConnected?.Invoke(new MirrorNetConnection(conn));
        }

        private void OnMirrorServerDisconnected(NetworkConnectionToClient conn)
        {
            _playerIds.Remove(conn.connectionId);
            OnClientDisconnected?.Invoke(new MirrorNetConnection(conn));
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

        // --- INetTransport Implementation ---

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
            else
            {
                if (connection is MirrorNetConnection mirrorConn)
                {
                    EnqueuePacket(0, payload.Array, payload.Count, channelId, false, false, mirrorConn.ConnectionId);
                }
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

        // --- INetworkManager Implementation ---

        public void RegisterHandler<T>(ushort msgId, Action<INetConnection, T> handler) where T : struct
        {
            if (_handlers.ContainsKey(msgId))
            {
                Debug.LogWarning($"[MirrorNetAdapter] Overwriting handler for MsgId {msgId}");
            }

            _handlers[msgId] = (conn, payload) =>
            {
                try
                {
                    var span = new ReadOnlySpan<byte>(payload.Array, payload.Offset, payload.Count);
                    T msg = Serializer.Deserialize<T>(span);
                    handler(conn, msg);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[MirrorNetAdapter] Error handling message {msgId}: {e}");
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
                Debug.LogError($"[MirrorNetAdapter] Failed to send message {msgId}: {e}");
            }
        }

        public void SendToClient<T>(INetConnection connection, ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct
        {
            int channelId = GetChannelId(channel);
            byte[] buffer = GetThreadBuffer();
            try
            {
                Serializer.Serialize(message, buffer, 0, out int written);

                if (IsMainThread)
                {
                    if (connection is MirrorNetConnection mc && NetworkServer.connections.TryGetValue(mc.ConnectionId, out var conn))
                    {
                        SendRawToConnection(conn, msgId, new ArraySegment<byte>(buffer, 0, written), channelId);
                    }
                }
                else
                {
                    if (connection is MirrorNetConnection mc)
                    {
                        EnqueuePacket(msgId, buffer, written, channelId, false, false, mc.ConnectionId);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[MirrorNetAdapter] Failed to send to client: {e}");
            }
        }

        public void BroadcastToClients<T>(ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct
        {
            int channelId = GetChannelId(channel);
            byte[] buffer = GetThreadBuffer();
            try
            {
                Serializer.Serialize(message, buffer, 0, out int written);

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
                Debug.LogError($"[MirrorNetAdapter] Failed to broadcast: {e}");
            }
        }

        public void Broadcast<T>(IReadOnlyList<INetConnection> connections, ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct
        {
            foreach (var conn in connections)
            {
                SendToClient(conn, msgId, message, channel);
            }
        }

        // --- Internal Handlers ---

        private void OnServerDataReceived(NetworkConnectionToClient conn, CycloneRawMessage msg)
        {
            if (_handlers.TryGetValue(msg.MsgId, out var handler))
            {
                handler(new MirrorNetConnection(conn), msg.Payload);
            }
        }

        private void OnClientDataReceived(CycloneRawMessage msg)
        {
            if (_handlers.TryGetValue(msg.MsgId, out var handler))
            {
                handler(new MirrorNetConnection(NetworkClient.connection), msg.Payload);
            }
        }
    }

    public readonly struct MirrorNetConnection : INetConnection
    {
        public int ConnectionId { get; }
        public string RemoteAddress { get; }
        public bool IsConnected { get; }
        public bool IsAuthenticated { get; }
        public int Ping { get; }

        public ulong PlayerId
        {
            get => MirrorNetAdapter.Instance ? MirrorNetAdapter.Instance.GetPlayerId(ConnectionId) : 0;
            set => MirrorNetAdapter.Instance?.SetPlayerId(ConnectionId, value);
        }

        public MirrorNetConnection(NetworkConnection conn)
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
        }

        public bool Equals(INetConnection other)
        {
            return other != null && ConnectionId == other.ConnectionId;
        }
    }

    public class FallbackJsonSerializer : INetSerializer
    {
        public void Serialize<T>(in T value, byte[] buffer, int offset, out int writtenBytes) where T : struct
        {
            string json = JsonUtility.ToJson(value);
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
            if (offset + bytes.Length > buffer.Length) throw new IndexOutOfRangeException("Buffer too small");

            Array.Copy(bytes, 0, buffer, offset, bytes.Length);
            writtenBytes = bytes.Length;
        }

        public T Deserialize<T>(ReadOnlySpan<byte> data) where T : struct
        {
            byte[] bytes = data.ToArray();
            string json = System.Text.Encoding.UTF8.GetString(bytes);
            return JsonUtility.FromJson<T>(json);
        }
    }
}
#endif