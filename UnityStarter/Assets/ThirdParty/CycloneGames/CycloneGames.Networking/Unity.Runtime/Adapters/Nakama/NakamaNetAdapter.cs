#if CYCLONE_NETWORKING_HAS_NAKAMA
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CycloneGames.Networking.Security;
using CycloneGames.Networking.Serialization;
using Nakama;
using UnityEngine;

namespace CycloneGames.Networking.Adapter.Nakama
{
    [DisallowMultipleComponent]
    public sealed class NakamaNetAdapter : MonoBehaviour, INetTransport, INetworkManager, INetworkSerializerConfigurable,
        INetworkRuntimeContextProvider, INetworkLifecycleProvider, INetworkFeatureProvider,
        INetworkSessionService, INetworkMatchStateService, INetworkMatchmakerService,
        INetworkBackendRpcService, INetworkPresenceService
    {
        private const string UnsupportedServerError = "NakamaNetAdapter is a client-side Nakama socket adapter. Run authoritative server logic in Nakama server modules or a dedicated server.";
        private const string MissingClientError = "Nakama client is not configured.";
        private const string MissingSessionError = "Nakama session is not available.";
        private const string MissingSocketError = "Nakama socket is not available.";
        private const string MissingMatchError = "Nakama match is not joined.";
        private const long DefaultMatchStateOpCode = 0L;

        public static NakamaNetAdapter Instance { get; private set; }

        [Header("Lifecycle")]
        [SerializeField] private bool _singleton = true;
        [SerializeField] private bool _connectOnStart;
        [SerializeField] private bool _useRecommendedSerializer = true;

        [Header("Nakama Client")]
        [SerializeField] private string _scheme = "http";
        [SerializeField] private string _host = "127.0.0.1";
        [SerializeField] private int _port = 7350;
        [SerializeField] private string _serverKey = "defaultkey";
        [SerializeField] private bool _useMainThreadSocket = true;
        [SerializeField] private bool _appearOnline = true;
        [SerializeField] private int _connectTimeoutSeconds = 30;
        [SerializeField] private string _languageTag = string.Empty;

        [Header("Authentication")]
        [SerializeField] private bool _autoAuthenticateDevice = true;
        [SerializeField] private bool _createAccount = true;
        [SerializeField] private string _deviceIdOverride = string.Empty;
        [SerializeField] private string _username = string.Empty;

        [Header("Match State")]
        [SerializeField] private string _matchId = string.Empty;
        [SerializeField] private bool _joinMatchOnConnect = true;
        [SerializeField] private long _matchStateOpCode = DefaultMatchStateOpCode;
        [SerializeField] private int _maxPayloadSize = NetworkConstants.MaxMTU;

        private readonly Dictionary<ushort, Action<INetConnection, ArraySegment<byte>>> _handlers =
            new Dictionary<ushort, Action<INetConnection, ArraySegment<byte>>>();

        private readonly Dictionary<string, NakamaNetConnection> _connectionsBySessionId =
            new Dictionary<string, NakamaNetConnection>(16, StringComparer.Ordinal);

        private readonly List<IUserPresence> _presenceScratch = new List<IUserPresence>(16);
        private readonly MessageValidator _messageValidator = new MessageValidator(NetworkConstants.MaxMTU, 0);

        [ThreadStatic] private static byte[] _threadSendBuffer;

        private IClient _client;
        private ISocket _socket;
        private ISession _session;
        private IMatch _match;
        private INetworkRuntimeContext _runtimeContext;
        private NetworkLifecycleState _lifecycleState = NetworkLifecycleState.Stopped;
        private TransportError _lastError;
        private string _lastErrorMessage = string.Empty;
        private CancellationTokenSource _shutdown;
        private int _sendSequence;
        private long _bytesSent;
        private long _bytesReceived;
        private int _packetsSent;
        private int _packetsReceived;
        private bool _socketEventsBound;

        public IClient Client => _client;
        public ISocket Socket => _socket;
        public ISession NakamaSession => _session;
        public IMatch CurrentMatch => _match;

        public bool IsServer => false;
        public bool IsClient => _socket != null && _socket.IsConnected && _match != null;
        public bool IsRunning => IsClient;
        public bool IsEncrypted => string.Equals(_scheme, "https", StringComparison.OrdinalIgnoreCase) || string.Equals(_scheme, "wss", StringComparison.OrdinalIgnoreCase);
        public bool Available => true;
        public NetworkBackendFeatures Features => NetworkBackendFeatures.RealtimeTransport
                                                  | NetworkBackendFeatures.AuthSession
                                                  | NetworkBackendFeatures.Matchmaker
                                                  | NetworkBackendFeatures.MatchState
                                                  | NetworkBackendFeatures.BackendRpc
                                                  | NetworkBackendFeatures.Presence
                                                  | NetworkBackendFeatures.Chat;
        public NetworkTransportCapabilities Capabilities => new NetworkTransportCapabilities(
            "Nakama",
            NetworkTransportFeatureFlags.Client
            | NetworkTransportFeatureFlags.Reliable
            | NetworkTransportFeatureFlags.Unreliable
            | NetworkTransportFeatureFlags.WebGLCompatible
            | NetworkTransportFeatureFlags.MainThreadOnly,
            NetworkChannelFlags.All,
            NetworkConstants.DefaultMaxConnections,
            _maxPayloadSize,
            _maxPayloadSize,
            _maxPayloadSize);

        public INetTransport Transport => this;
        public INetSerializer Serializer { get; private set; }
        public INetworkRuntimeContext RuntimeContext => _runtimeContext;

        public bool HasSession => _session != null && !_session.IsExpired;
        public INetworkSession CurrentSession => _session != null ? new NakamaNetworkSession(_session) : null;
        public NetworkMatchId CurrentMatchId => _match != null ? new NetworkMatchId(_match.Id) : new NetworkMatchId(_matchId);
        public bool IsInMatch => _match != null;
        public bool IsMatchmaking { get; private set; }
        public NetworkMatchmakerTicket CurrentTicket { get; private set; }

        public event Action<INetConnection> OnClientConnected;
        public event Action<INetConnection> OnClientDisconnected;
        public event Action OnConnectedToServer;
        public event Action OnDisconnectedFromServer;
        public event Action<INetConnection, TransportError, string> OnError;
        public event Action<INetConnection, ArraySegment<byte>, int> OnDataReceived;
        public event Action<NetworkMatchId, INetConnection, ArraySegment<byte>, long> OnMatchState;
        public event Action<NetworkMatchmakerTicket, NetworkMatchId> OnMatched;
        public event Action<NetworkMatchId, NetworkPresence> OnPresenceJoined;
        public event Action<NetworkMatchId, NetworkPresence> OnPresenceLeft;

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

            _shutdown = new CancellationTokenSource();
            Serializer = _useRecommendedSerializer ? SerializerFactory.GetRecommended() : SerializerFactory.GetDefault();
            EnsureClientAndSocket();
            _runtimeContext = BuildRuntimeContext();
        }

        private void Start()
        {
            if (_connectOnStart)
                StartClient(_matchId);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            Stop();
            _runtimeContext?.Dispose();
            _runtimeContext = null;
        }

        public void Initialize(IClient client, ISocket socket, ISession session, string matchId = null)
        {
            UnbindSocketEvents();
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _socket = socket ?? throw new ArgumentNullException(nameof(socket));
            _session = session;
            if (!string.IsNullOrEmpty(matchId))
                _matchId = matchId;
            BindSocketEvents();
            _runtimeContext = BuildRuntimeContext();
        }

        public int GetChannelId(NetworkChannel channel) => (int)channel;
        public int GetMaxPacketSize(int channelId) => _maxPayloadSize;

        public NetworkStatistics GetStatistics()
        {
            return new NetworkStatistics(
                Interlocked.Read(ref _bytesSent),
                Interlocked.Read(ref _bytesReceived),
                _packetsSent,
                _packetsReceived,
                _connectionsBySessionId.Count,
                0,
                0f);
        }

        public NetworkLifecycleSnapshot GetLifecycleSnapshot()
        {
            return new NetworkLifecycleSnapshot(
                _lifecycleState,
                Features,
                _lastError,
                _lastErrorMessage,
                Available,
                IsRunning,
                IsServer,
                IsClient,
                IsEncrypted);
        }

        public void StartServer()
        {
            RaiseError(null, TransportError.Unexpected, UnsupportedServerError);
        }

        public void StartClient(string address)
        {
            if (!string.IsNullOrEmpty(address))
                _matchId = address;

            ConnectAndJoinAsync(_shutdown != null ? _shutdown.Token : CancellationToken.None);
        }

        public void Stop()
        {
            _shutdown?.Cancel();
            _shutdown?.Dispose();
            _shutdown = new CancellationTokenSource();

            if (_socket != null && _socket.IsConnected)
                CloseSocketAsync();

            _match = null;
            _connectionsBySessionId.Clear();
            _lifecycleState = NetworkLifecycleState.Stopped;
        }

        public void Disconnect(INetConnection connection)
        {
            Stop();
        }

        public NetworkSendResult Send(INetConnection connection, in ArraySegment<byte> payload, int channelId)
        {
            if (!ValidatePayload(payload))
                return NetworkSendResult.Fail(NetworkSendStatus.InvalidPayload, channelId, connection);

            if (!IsInMatch)
                return NetworkSendResult.Fail(NetworkSendStatus.NotConnected, channelId, connection, MissingMatchError);

            NetworkChannel channel = (NetworkChannel)channelId;
            byte[] buffer = GetThreadBuffer();
            int frameLength = BuildFrame(NetworkConstants.SystemMsgIdMin, channel, payload, buffer);
            SendFrameToMatch(new ArraySegment<byte>(buffer, 0, frameLength), GetTargetPresences(connection));
            return NetworkSendResult.Queued(frameLength, channelId, connection);
        }

        public NetworkSendResult Broadcast(IReadOnlyList<INetConnection> connections, in ArraySegment<byte> payload, int channelId)
        {
            if (connections == null)
                throw new ArgumentNullException(nameof(connections));
            if (!ValidatePayload(payload))
                return NetworkSendResult.Fail(NetworkSendStatus.InvalidPayload, channelId);

            if (!IsInMatch)
                return NetworkSendResult.Fail(NetworkSendStatus.NotConnected, channelId, reason: MissingMatchError);

            _presenceScratch.Clear();
            for (int i = 0; i < connections.Count; i++)
            {
                if (connections[i] is NakamaNetConnection connection && connection.Presence != null)
                    _presenceScratch.Add(connection.Presence);
            }

            NetworkChannel channel = (NetworkChannel)channelId;
            byte[] buffer = GetThreadBuffer();
            int frameLength = BuildFrame(NetworkConstants.SystemMsgIdMin, channel, payload, buffer);
            SendFrameToMatch(new ArraySegment<byte>(buffer, 0, frameLength), _presenceScratch.Count > 0 ? _presenceScratch.ToArray() : null);
            int recipients = _presenceScratch.Count;
            _presenceScratch.Clear();
            return NetworkSendResult.Broadcast(NetworkSendStatus.Queued, frameLength * recipients, recipients, channelId);
        }

        public void RegisterHandler<T>(ushort msgId, Action<INetConnection, T> handler) where T : struct
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            _handlers[msgId] = (connection, payload) =>
            {
                try
                {
                    var span = new ReadOnlySpan<byte>(payload.Array, payload.Offset, payload.Count);
                    T message = Serializer.Deserialize<T>(span);
                    handler(connection, message);
                }
                catch (Exception e)
                {
                    RaiseError(connection, TransportError.InvalidReceive, e.Message);
                }
            };
        }

        public void UnregisterHandler(ushort msgId)
        {
            _handlers.Remove(msgId);
        }

        public NetworkSendResult SendToServer<T>(ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct
        {
            return SendMessage(msgId, message, channel, null);
        }

        public NetworkSendResult SendToClient<T>(INetConnection connection, ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct
        {
            return SendMessage(msgId, message, channel, GetTargetPresences(connection));
        }

        public NetworkSendResult BroadcastToClients<T>(ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct
        {
            return SendMessage(msgId, message, channel, null);
        }

        public NetworkSendResult Broadcast<T>(IReadOnlyList<INetConnection> connections, ushort msgId, T message, NetworkChannel channel = NetworkChannel.Reliable) where T : struct
        {
            if (connections == null)
                throw new ArgumentNullException(nameof(connections));

            _presenceScratch.Clear();
            for (int i = 0; i < connections.Count; i++)
            {
                if (connections[i] is NakamaNetConnection connection && connection.Presence != null)
                    _presenceScratch.Add(connection.Presence);
            }

            int recipients = _presenceScratch.Count;
            NetworkSendResult result = SendMessage(msgId, message, channel, _presenceScratch.Count > 0 ? _presenceScratch.ToArray() : null);
            _presenceScratch.Clear();
            return result.Succeeded
                ? NetworkSendResult.Broadcast(result.Status, result.BytesAccepted * recipients, recipients, result.ChannelId)
                : result;
        }

        public void DisconnectClient(INetConnection connection)
        {
            Disconnect(connection);
        }

        public void SetSerializer(INetSerializer serializer)
        {
            Serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

        public void ClearSession()
        {
            _session = null;
        }

        public bool TrySendMatchState(NetworkMatchId matchId, long operationCode, in ArraySegment<byte> payload, NetworkChannel channel = NetworkChannel.Reliable)
        {
            if (!matchId.IsValid || !ValidatePayload(payload))
                return false;

            SendMatchStateAsync(matchId.Value, operationCode, payload, null);
            return true;
        }

        public void LeaveMatch(NetworkMatchId matchId)
        {
            if (_socket == null || !matchId.IsValid)
                return;

            LeaveMatchAsync(matchId.Value);
        }

        public bool TryCancelMatchmaker(NetworkMatchmakerTicket ticket)
        {
            if (_socket == null || !ticket.IsValid)
                return false;

            CancelMatchmakerAsync(ticket.Value);
            return true;
        }

        public bool TryCallRpc(string rpcId, in ArraySegment<byte> payload, out ArraySegment<byte> response)
        {
            response = default;
            if (_client == null || _session == null || string.IsNullOrEmpty(rpcId))
                return false;

            string request = payload.Array != null && payload.Count > 0
                ? Encoding.UTF8.GetString(payload.Array, payload.Offset, payload.Count)
                : string.Empty;

            try
            {
                IApiRpc rpc = _client.RpcAsync(_session, rpcId, request).GetAwaiter().GetResult();
                byte[] bytes = Encoding.UTF8.GetBytes(rpc.Payload ?? string.Empty);
                response = new ArraySegment<byte>(bytes);
                return true;
            }
            catch (Exception e)
            {
                RaiseError(null, TransportError.Unexpected, e.Message);
                return false;
            }
        }

        public bool TryGetLocalPresence(out NetworkPresence presence)
        {
            if (_match != null && _match.Self != null)
            {
                presence = ToNetworkPresence(_match.Self);
                return presence.IsValid;
            }

            if (_session != null)
            {
                presence = new NetworkPresence(_session.UserId, string.Empty, _session.Username);
                return presence.IsValid;
            }

            presence = default;
            return false;
        }

        public void StartMatchmaker(string query, int minCount, int maxCount)
        {
            AddMatchmakerAsync(query, minCount, maxCount);
        }

        private INetworkRuntimeContext BuildRuntimeContext()
        {
            var context = new NetworkRuntimeContext(NetworkRuntimeIds.Nakama, "Nakama", this, Features);
            context.AddService<INetworkSessionService>(this);
            context.AddService<INetworkMatchStateService>(this);
            context.AddService<INetworkMatchmakerService>(this);
            context.AddService<INetworkBackendRpcService>(this);
            context.AddService<INetworkPresenceService>(this);
            if (_client != null)
                context.AddService<IClient>(_client);
            if (_socket != null)
                context.AddService<ISocket>(_socket);
            return context.Build();
        }

        private void EnsureClientAndSocket()
        {
            if (_client == null)
                _client = new Client(_scheme, _host, _port, _serverKey);
            if (_socket == null)
                _socket = _client.NewSocket(_useMainThreadSocket);
            BindSocketEvents();
        }

        private async void ConnectAndJoinAsync(CancellationToken cancellationToken)
        {
            EnsureClientAndSocket();
            if (_client == null)
            {
                RaiseError(null, TransportError.Unexpected, MissingClientError);
                return;
            }

            if (_socket == null)
            {
                RaiseError(null, TransportError.Unexpected, MissingSocketError);
                return;
            }

            _lifecycleState = NetworkLifecycleState.StartingClient;

            try
            {
                if (_session == null && _autoAuthenticateDevice)
                {
                    string deviceId = string.IsNullOrEmpty(_deviceIdOverride) ? SystemInfo.deviceUniqueIdentifier : _deviceIdOverride;
                    _session = await _client.AuthenticateDeviceAsync(deviceId, _username, _createAccount);
                }

                if (_session == null)
                {
                    RaiseError(null, TransportError.Unexpected, MissingSessionError);
                    return;
                }

                if (!_socket.IsConnected)
                    await _socket.ConnectAsync(_session, _appearOnline, _connectTimeoutSeconds, _languageTag);

                if (_joinMatchOnConnect && !string.IsNullOrEmpty(_matchId))
                    _match = await _socket.JoinMatchAsync(_matchId, null);

                if (cancellationToken.IsCancellationRequested)
                    return;

                _lifecycleState = NetworkLifecycleState.ClientRunning;
                OnConnectedToServer?.Invoke();
                AddInitialPresences(_match);
            }
            catch (Exception e)
            {
                _lifecycleState = NetworkLifecycleState.Faulted;
                RaiseError(null, TransportError.Unexpected, e.Message);
            }
        }

        private async void CloseSocketAsync()
        {
            try
            {
                await _socket.CloseAsync();
            }
            catch (Exception e)
            {
                RaiseError(null, TransportError.ConnectionClosed, e.Message);
            }
        }

        private async void LeaveMatchAsync(string matchId)
        {
            try
            {
                await _socket.LeaveMatchAsync(matchId);
                if (_match != null && string.Equals(_match.Id, matchId, StringComparison.Ordinal))
                    _match = null;
            }
            catch (Exception e)
            {
                RaiseError(null, TransportError.Unexpected, e.Message);
            }
        }

        private async void CancelMatchmakerAsync(string ticket)
        {
            try
            {
                await _socket.RemoveMatchmakerAsync(ticket);
                IsMatchmaking = false;
                CurrentTicket = default;
            }
            catch (Exception e)
            {
                RaiseError(null, TransportError.Unexpected, e.Message);
            }
        }

        private async void AddMatchmakerAsync(string query, int minCount, int maxCount)
        {
            if (_socket == null || !_socket.IsConnected)
            {
                RaiseError(null, TransportError.Unexpected, MissingSocketError);
                return;
            }

            try
            {
                IMatchmakerTicket ticket = await _socket.AddMatchmakerAsync(query, minCount, maxCount);
                CurrentTicket = new NetworkMatchmakerTicket(ticket.Ticket);
                IsMatchmaking = true;
            }
            catch (Exception e)
            {
                IsMatchmaking = false;
                RaiseError(null, TransportError.Unexpected, e.Message);
            }
        }

        private void BindSocketEvents()
        {
            if (_socket == null || _socketEventsBound)
                return;

            _socket.Closed += OnSocketClosed;
            _socket.ReceivedError += OnSocketError;
            _socket.ReceivedMatchState += OnReceivedMatchState;
            _socket.ReceivedMatchPresence += OnReceivedMatchPresence;
            _socket.ReceivedMatchmakerMatched += OnReceivedMatchmakerMatched;
            _socketEventsBound = true;
        }

        private void UnbindSocketEvents()
        {
            if (_socket == null || !_socketEventsBound)
                return;

            _socket.Closed -= OnSocketClosed;
            _socket.ReceivedError -= OnSocketError;
            _socket.ReceivedMatchState -= OnReceivedMatchState;
            _socket.ReceivedMatchPresence -= OnReceivedMatchPresence;
            _socket.ReceivedMatchmakerMatched -= OnReceivedMatchmakerMatched;
            _socketEventsBound = false;
        }

        private void OnSocketClosed(string reason)
        {
            _lifecycleState = NetworkLifecycleState.Stopped;
            _lastError = TransportError.ConnectionClosed;
            _lastErrorMessage = reason ?? string.Empty;
            OnDisconnectedFromServer?.Invoke();
        }

        private void OnSocketError(Exception exception)
        {
            RaiseError(null, TransportError.Unexpected, exception != null ? exception.Message : string.Empty);
        }

        private async void OnReceivedMatchmakerMatched(IMatchmakerMatched matched)
        {
            if (matched == null)
                return;

            IsMatchmaking = false;
            CurrentTicket = new NetworkMatchmakerTicket(matched.Ticket);
            OnMatched?.Invoke(CurrentTicket, new NetworkMatchId(matched.MatchId));

            if (string.IsNullOrEmpty(matched.MatchId))
                return;

            try
            {
                _match = await _socket.JoinMatchAsync(matched);
                _matchId = _match.Id;
                AddInitialPresences(_match);
            }
            catch (Exception e)
            {
                RaiseError(null, TransportError.Unexpected, e.Message);
            }
        }

        private void OnReceivedMatchPresence(IMatchPresenceEvent presenceEvent)
        {
            if (presenceEvent == null)
                return;

            NetworkMatchId matchId = new NetworkMatchId(presenceEvent.MatchId);
            foreach (IUserPresence joined in presenceEvent.Joins)
            {
                NakamaNetConnection connection = GetOrCreateConnection(joined);
                OnClientConnected?.Invoke(connection);
                OnPresenceJoined?.Invoke(matchId, ToNetworkPresence(joined));
            }

            foreach (IUserPresence left in presenceEvent.Leaves)
            {
                NakamaNetConnection connection = GetOrCreateConnection(left);
                connection.SetConnected(false);
                OnClientDisconnected?.Invoke(connection);
                OnPresenceLeft?.Invoke(matchId, ToNetworkPresence(left));
                if (!string.IsNullOrEmpty(left.SessionId))
                    _connectionsBySessionId.Remove(left.SessionId);
            }
        }

        private void OnReceivedMatchState(IMatchState state)
        {
            if (state == null || state.State == null)
                return;

            NakamaNetConnection connection = GetOrCreateConnection(state.UserPresence);
            connection.RecordReceived(state.State.Length);
            Interlocked.Add(ref _bytesReceived, state.State.Length);
            Interlocked.Increment(ref _packetsReceived);

            var raw = new ArraySegment<byte>(state.State);
            OnMatchState?.Invoke(new NetworkMatchId(state.MatchId), connection, raw, state.OpCode);

            if (state.OpCode != _matchStateOpCode)
                return;

            if (!TryValidateIncoming(connection, raw, out NetworkEnvelopeHeader header, out ArraySegment<byte> payload))
                return;

            OnDataReceived?.Invoke(connection, payload, GetChannelId(header.Channel));
            if (_handlers.TryGetValue(header.MessageId, out Action<INetConnection, ArraySegment<byte>> handler))
                handler(connection, payload);
        }

        private void AddInitialPresences(IMatch match)
        {
            if (match == null)
                return;

            if (match.Self != null)
                GetOrCreateConnection(match.Self);

            foreach (IUserPresence presence in match.Presences)
            {
                NakamaNetConnection connection = GetOrCreateConnection(presence);
                OnClientConnected?.Invoke(connection);
                OnPresenceJoined?.Invoke(new NetworkMatchId(match.Id), ToNetworkPresence(presence));
            }
        }

        private NetworkSendResult SendMessage<T>(ushort msgId, T message, NetworkChannel channel, IEnumerable<IUserPresence> targetPresences) where T : struct
        {
            int channelId = GetChannelId(channel);
            if (!IsInMatch)
                return NetworkSendResult.Fail(NetworkSendStatus.NotConnected, channelId, reason: MissingMatchError);

            byte[] buffer = GetThreadBuffer();
            try
            {
                Serializer.Serialize(message, buffer, NetworkWireProtocol.HeaderLength, out int written);
                int frameLength = WriteFrameHeader(msgId, channel, buffer, written);
                SendFrameToMatch(new ArraySegment<byte>(buffer, 0, frameLength), targetPresences);
                return NetworkSendResult.Queued(frameLength, channelId);
            }
            catch (Exception e)
            {
                RaiseError(null, TransportError.InvalidSend, e.Message);
                return NetworkSendResult.Fail(NetworkSendStatus.InvalidPayload, channelId, reason: e.Message);
            }
        }

        private int BuildFrame(ushort msgId, NetworkChannel channel, in ArraySegment<byte> payload, byte[] buffer)
        {
            if (payload.Array == null)
                throw new ArgumentException("Payload must reference a valid array.", nameof(payload));

            int frameLength = NetworkFrameCodec.GetFrameLength(payload.Count);
            if (frameLength > buffer.Length)
                throw new InvalidOperationException("Frame size exceeds send buffer capacity.");

            Buffer.BlockCopy(payload.Array, payload.Offset, buffer, NetworkWireProtocol.HeaderLength, payload.Count);
            WriteFrameHeader(msgId, channel, buffer, payload.Count);
            return frameLength;
        }

        private int WriteFrameHeader(ushort msgId, NetworkChannel channel, byte[] buffer, int payloadLength)
        {
            uint sequence = unchecked((uint)Interlocked.Increment(ref _sendSequence));
            if (sequence == 0u)
                sequence = unchecked((uint)Interlocked.Increment(ref _sendSequence));

            var payload = new ReadOnlySpan<byte>(buffer, NetworkWireProtocol.HeaderLength, payloadLength);
            NetworkMessageFlags flags = GetMessageFlags(channel);
            uint checksum = NetworkFrameCodec.ComputeChecksum(msgId, channel, flags, sequence, payload);
            var header = new NetworkEnvelopeHeader(msgId, channel, payloadLength, sequence, checksum, flags);
            NetworkFrameCodec.WriteHeader(buffer, 0, header);
            return NetworkFrameCodec.GetFrameLength(payloadLength);
        }

        private void SendFrameToMatch(ArraySegment<byte> frame, IEnumerable<IUserPresence> presences)
        {
            string matchId = _match != null ? _match.Id : _matchId;
            if (string.IsNullOrEmpty(matchId))
            {
                RaiseError(null, TransportError.InvalidSend, MissingMatchError);
                return;
            }

            SendMatchStateAsync(matchId, _matchStateOpCode, frame, presences);
        }

        private async void SendMatchStateAsync(string matchId, long opCode, ArraySegment<byte> payload, IEnumerable<IUserPresence> presences)
        {
            byte[] rented = ArrayPool<byte>.Shared.Rent(payload.Count);
            try
            {
                Buffer.BlockCopy(payload.Array, payload.Offset, rented, 0, payload.Count);
                var segment = new ArraySegment<byte>(rented, 0, payload.Count);
                await _socket.SendMatchStateAsync(matchId, opCode, segment, presences);
                Interlocked.Add(ref _bytesSent, payload.Count);
                Interlocked.Increment(ref _packetsSent);
            }
            catch (Exception e)
            {
                RaiseError(null, TransportError.InvalidSend, e.Message);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        private bool TryValidateIncoming(INetConnection connection, ArraySegment<byte> frame, out NetworkEnvelopeHeader header, out ArraySegment<byte> payload)
        {
            header = default;
            payload = default;

            NetworkFrameResult frameResult = NetworkFrameCodec.TryReadPayload(frame, out header, out payload);
            if (frameResult != NetworkFrameResult.Valid)
                return false;

            if (_messageValidator.Validate(header.MessageId, header.PayloadLength) != ValidationResult.Valid)
                return false;

            var payloadSpan = new ReadOnlySpan<byte>(payload.Array, payload.Offset, payload.Count);
            if (NetworkFrameCodec.ValidateChecksum(header, payloadSpan) != NetworkFrameResult.Valid)
                return false;

            return true;
        }

        private bool ValidatePayload(in ArraySegment<byte> payload)
        {
            if (payload.Array == null || payload.Offset < 0 || payload.Count < 0 || payload.Offset > payload.Array.Length || payload.Count > payload.Array.Length - payload.Offset)
            {
                RaiseError(null, TransportError.InvalidSend, "Invalid payload segment.");
                return false;
            }

            if (payload.Count > _maxPayloadSize)
            {
                RaiseError(null, TransportError.InvalidSend, "Payload exceeds Nakama adapter packet size limit.");
                return false;
            }

            return true;
        }

        private IEnumerable<IUserPresence> GetTargetPresences(INetConnection connection)
        {
            if (connection is NakamaNetConnection nakamaConnection && nakamaConnection.Presence != null)
                return new[] { nakamaConnection.Presence };
            return null;
        }

        private NakamaNetConnection GetOrCreateConnection(IUserPresence presence)
        {
            if (presence == null)
                return NakamaNetConnection.None;

            string key = presence.SessionId ?? string.Empty;
            if (key.Length == 0)
                key = presence.UserId ?? string.Empty;

            if (_connectionsBySessionId.TryGetValue(key, out NakamaNetConnection connection))
            {
                connection.SetConnected(true);
                return connection;
            }

            connection = new NakamaNetConnection(presence);
            _connectionsBySessionId[key] = connection;
            return connection;
        }

        private void RaiseError(INetConnection connection, TransportError error, string message)
        {
            _lastError = error;
            _lastErrorMessage = message ?? string.Empty;
            OnError?.Invoke(connection, error, _lastErrorMessage);
        }

        private static NetworkMessageFlags GetMessageFlags(NetworkChannel channel)
        {
            switch (channel)
            {
                case NetworkChannel.Reliable:
                    return NetworkMessageFlags.Reliable | NetworkMessageFlags.Ordered;
                case NetworkChannel.ReliableUnordered:
                    return NetworkMessageFlags.Reliable;
                case NetworkChannel.UnreliableSequenced:
                    return NetworkMessageFlags.Ordered;
                default:
                    return NetworkMessageFlags.None;
            }
        }

        private static NetworkPresence ToNetworkPresence(IUserPresence presence)
        {
            return presence == null
                ? default
                : new NetworkPresence(presence.UserId, presence.SessionId, presence.Username);
        }

        private static byte[] GetThreadBuffer()
        {
            return _threadSendBuffer ?? (_threadSendBuffer = new byte[NetworkConstants.MaxMTU]);
        }
    }

    public sealed class NakamaNetConnection : INetConnection
    {
        public static readonly NakamaNetConnection None = new NakamaNetConnection();

        private long _bytesSent;
        private long _bytesReceived;
        private bool _isConnected;

        private NakamaNetConnection()
        {
            ConnectionId = 0;
            RemoteAddress = "nakama:none";
            _isConnected = false;
        }

        public NakamaNetConnection(IUserPresence presence)
        {
            Presence = presence;
            ConnectionId = StableIntHash(presence != null ? presence.SessionId : null);
            RemoteAddress = presence != null ? "nakama:" + presence.SessionId : "nakama:unknown";
            PlayerId = StableUlongHash(presence != null ? presence.UserId : null);
            _isConnected = true;
        }

        public IUserPresence Presence { get; }
        public int ConnectionId { get; }
        public string RemoteAddress { get; }
        public bool IsConnected => _isConnected;
        public bool IsAuthenticated => Presence != null;
        public int Ping => 0;
        public ConnectionQuality Quality => ConnectionQuality.Good;
        public double Jitter => 0d;
        public long BytesSent => Interlocked.Read(ref _bytesSent);
        public long BytesReceived => Interlocked.Read(ref _bytesReceived);
        public ulong PlayerId { get; set; }

        public bool Equals(INetConnection other)
        {
            return other != null && other.ConnectionId == ConnectionId;
        }

        public override bool Equals(object obj)
        {
            return obj is INetConnection other && Equals(other);
        }

        public override int GetHashCode()
        {
            return ConnectionId;
        }

        public void SetConnected(bool connected)
        {
            _isConnected = connected;
        }

        public void RecordSent(int bytes)
        {
            Interlocked.Add(ref _bytesSent, bytes);
        }

        public void RecordReceived(int bytes)
        {
            Interlocked.Add(ref _bytesReceived, bytes);
        }

        private static int StableIntHash(string value)
        {
            ulong hash = StableUlongHash(value);
            int result = unchecked((int)(hash ^ (hash >> 32)));
            return result == 0 ? 1 : result;
        }

        private static ulong StableUlongHash(string value)
        {
            if (string.IsNullOrEmpty(value))
                return 1UL;

            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;

            ulong hash = offset;
            for (int i = 0; i < value.Length; i++)
                hash = (hash ^ value[i]) * prime;

            return hash == 0UL ? 1UL : hash;
        }
    }

    public readonly struct NakamaNetworkSession : INetworkSession
    {
        private readonly ISession _session;

        public NakamaNetworkSession(ISession session)
        {
            _session = session;
        }

        public string UserId => _session != null ? _session.UserId : string.Empty;
        public string Username => _session != null ? _session.Username : string.Empty;
        public bool IsExpired => _session == null || _session.IsExpired;
        public long ExpireTimeUnixSeconds => _session != null ? _session.ExpireTime : 0L;
    }
}
#endif
