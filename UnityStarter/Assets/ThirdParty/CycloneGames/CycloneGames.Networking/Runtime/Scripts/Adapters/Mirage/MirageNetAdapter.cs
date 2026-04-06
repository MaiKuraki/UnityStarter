#if MIRAGE
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Mirage;
using CycloneGames.Networking.Serialization;
using CycloneGames.Networking.Buffers;
using CycloneGames.Networking.Services;
using CycloneGames.Logger;

namespace CycloneGames.Networking.Adapter.Mirage
{
    public struct CycloneRawMessage
    {
        public ushort MsgId;
        public ArraySegment<byte> Payload;
    }

    [DisallowMultipleComponent]
    public sealed class MirageNetAdapter : MonoBehaviour, INetTransport, INetworkManager
    {
        public static MirageNetAdapter Instance { get; private set; }

        [Header("Config")]
        [SerializeField] private bool _singleton = true;
        [SerializeField] private bool _useRecommendedSerializer = true;

        [Header("Mirage References")]
        [SerializeField] private NetworkServer _server;
        [SerializeField] private NetworkClient _client;

        public bool IsServer => _server != null && _server.Active;
        public bool IsClient => _client != null && _client.Active;
        public bool IsRunning => IsServer || IsClient;
        public bool Available => true;
        public bool IsEncrypted => false;

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

        public INetTransport Transport => this;
        public INetSerializer Serializer { get; private set; }

        public event Action<INetConnection> OnClientConnected;
        public event Action<INetConnection> OnClientDisconnected;
        public event Action OnConnectedToServer;
        public event Action OnDisconnectedFromServer;
        public event Action<INetConnection, TransportError, string> OnError;

        // Internal
        private readonly Dictionary<ushort, Action<INetConnection, ArraySegment<byte>>> _handlers =
            new Dictionary<ushort, Action<INetConnection, ArraySegment<byte>>>();

        private readonly Dictionary<INetworkPlayer, ulong> _playerIds = new Dictionary<INetworkPlayer, ulong>();
        private readonly Dictionary<INetworkPlayer, MirageConnectionData> _connectionData =
            new Dictionary<INetworkPlayer, MirageConnectionData>();

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
                : JsonSerializerAdapter.Instance;

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
                var data = new MirageConnectionData();
                _connectionData[player] = data;
                OnClientConnected?.Invoke(new MirageNetConnection(player, data));
            });

            _server.Disconnected.AddListener(player =>
            {
                _playerIds.Remove(player);
                _connectionData.Remove(player);
                OnClientDisconnected?.Invoke(new MirageNetConnection(player, default));
            });

            _server.MessageHandler.RegisterHandler<CycloneRawMessage>((player, msg) =>
            {
                OnDataReceived(player, msg);
            }, allowUnauthenticated: false);
        }

        private void SetupClientEvents()
        {
            if (_client == null) return;

            _client.Connected.AddListener(player =>
            {
                OnConnectedToServer?.Invoke();
            });

            _client.Disconnected.AddListener(reason =>
            {
                OnDisconnectedFromServer?.Invoke();
            });

            _client.MessageHandler.RegisterHandler<CycloneRawMessage>((player, msg) =>
            {
                OnClientDataReceived(msg);
            }, allowUnauthenticated: false);
        }

        private void OnDestroy()
        {
            if (NetServices.IsAvailable && (object)NetServices.Instance == this)
                NetServices.Unregister(this);

            if (Instance == this) Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
            _threadLocalSendBuffer = null;
        }

        public void SetSerializer(INetSerializer serializer) => Serializer = serializer;

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

        // INetTransport
        public void StartServer()
        {
            if (_server != null)
                _server.StartServer();
        }

        public void StartClient(string address)
        {
            if (_client != null)
                _client.Connect(address);
        }

        public void Stop()
        {
            _server?.Stop();
            _client?.Disconnect();
        }

        public void Disconnect(INetConnection connection)
        {
            if (connection is MirageNetConnection mc && mc.Player != null)
                mc.Player.Disconnect();
        }

        public void Send(INetConnection connection, in ArraySegment<byte> payload, int channelId)
        {
            Interlocked.Add(ref _bytesSent, payload.Count);
            Interlocked.Increment(ref _packetsSent);

            if (connection is MirageNetConnection mc && mc.Player != null)
            {
                mc.Player.Send(payload, (Channel)channelId);
            }
        }

        public void Broadcast(IReadOnlyList<INetConnection> connections, in ArraySegment<byte> payload, int channelId)
        {
            for (int i = 0; i < connections.Count; i++)
                Send(connections[i], payload, channelId);
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

        public void SendToServer<T>(ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct
        {
            if (_client == null || !_client.Active) return;

            byte[] buffer = GetThreadBuffer();
            try
            {
                Serializer.Serialize(message, buffer, 0, out int written);
                Interlocked.Add(ref _bytesSent, written);
                Interlocked.Increment(ref _packetsSent);

                var rawMsg = new CycloneRawMessage { MsgId = msgId, Payload = new ArraySegment<byte>(buffer, 0, written) };
                _client.Send(rawMsg, (Channel)GetChannelId(channel));
            }
            catch (Exception e)
            {
                CLogger.LogError($"Failed to send message {msgId}: {e}", LogCategory.Network);
            }
        }

        public void SendToClient<T>(INetConnection connection, ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct
        {
            if (connection is not MirageNetConnection mc || mc.Player == null) return;

            byte[] buffer = GetThreadBuffer();
            try
            {
                Serializer.Serialize(message, buffer, 0, out int written);
                Interlocked.Add(ref _bytesSent, written);
                Interlocked.Increment(ref _packetsSent);

                var rawMsg = new CycloneRawMessage { MsgId = msgId, Payload = new ArraySegment<byte>(buffer, 0, written) };
                mc.Player.Send(rawMsg, (Channel)GetChannelId(channel));
            }
            catch (Exception e)
            {
                CLogger.LogError($"Failed to send to client: {e}", LogCategory.Network);
            }
        }

        public void BroadcastToClients<T>(ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct
        {
            if (_server == null || !_server.Active) return;

            byte[] buffer = GetThreadBuffer();
            try
            {
                Serializer.Serialize(message, buffer, 0, out int written);
                Interlocked.Add(ref _bytesSent, written);
                Interlocked.Increment(ref _packetsSent);

                var rawMsg = new CycloneRawMessage { MsgId = msgId, Payload = new ArraySegment<byte>(buffer, 0, written) };
                _server.SendToAll(rawMsg, false, (Channel)GetChannelId(channel));
            }
            catch (Exception e)
            {
                CLogger.LogError($"Failed to broadcast: {e}", LogCategory.Network);
            }
        }

        public void Broadcast<T>(IReadOnlyList<INetConnection> connections, ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct
        {
            for (int i = 0; i < connections.Count; i++)
                SendToClient(connections[i], msgId, message, channel);
        }

        // Data Handlers
        private void OnDataReceived(INetworkPlayer player, CycloneRawMessage msg)
        {
            if (_handlers.TryGetValue(msg.MsgId, out var handler))
                handler(new MirageNetConnection(player, GetConnectionData(player)), msg.Payload);
        }

        private void OnClientDataReceived(CycloneRawMessage msg)
        {
            if (_handlers.TryGetValue(msg.MsgId, out var handler) && _client.Player != null)
                handler(new MirageNetConnection(_client.Player, default), msg.Payload);
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

        internal MirageNetConnection(INetworkPlayer player, MirageNetAdapter.MirageConnectionData data)
        {
            Player = player;
            ConnectionId = player?.GetHashCode() ?? 0;
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
