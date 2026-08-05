#if CYCLONE_NETWORKING_HAS_NAKAMA
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using CycloneGames.Hash.Core;
using CycloneGames.Networking.Security;
using Nakama;
using UnityEngine;

namespace CycloneGames.Networking.Adapter.Nakama
{
    internal enum NakamaConnectionRouteRole : byte
    {
        Authority = 1,
        Peer = 2
    }

    /// <summary>
    /// Unity main-thread-owned Nakama transport and service adapter.
    /// </summary>
    /// <remarks>
    /// Nakama SDK access, task continuations, and callbacks remain on the Unity main thread.
    /// Wrong-thread access fails fast; no implicit dispatch queue is provided.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class NakamaNetAdapter : MonoBehaviour, INetTransport, INetworkMessageEndpoint,
        INetworkRuntimeContextProvider, INetworkLifecycleProvider, INetworkFeatureProvider,
        INetworkSessionService, INetworkMatchStateService, INetworkMatchmakerService,
        INetworkPresenceService
    {
        private const string UnsupportedServerError = "NakamaNetAdapter is a client-side Nakama socket adapter. Run authoritative server logic in Nakama server modules or a dedicated server.";
        private const string MissingClientError = "Nakama client is not configured.";
        private const string MissingSessionError = "Nakama session is not available.";
        private const string MissingSocketError = "Nakama socket is not available.";
        private const string MissingMatchError = "Nakama match is not joined.";
        private const string UnsupportedChannelError = "Nakama match-state transport only supports the reliable ordered channel.";
        private const string NonAuthoritativeMatchError = "SendToServer requires a joined authoritative Nakama match.";
        private const string UnsupportedClientRouteError = "The client-side Nakama adapter cannot send through a server-to-client or server-broadcast route.";
        private const string ConnectionCapacityError = "Nakama connection capacity is exhausted.";
        private const string SendCapacityError = "Nakama asynchronous send capacity is exhausted.";
        private const string AuthenticationOperationCapacityError = "A Nakama authentication operation for another client is already pending.";
        private const string UnavailableSocketEpochError = "A Nakama socket can have only one adapter owner and one connection epoch. Use a newly created ISocket for another adapter or reconnect.";
        private const string LeaveMatchCapacityError = "A Nakama leave-match operation is already pending.";
        private const string MatchmakerStartCapacityError = "A Nakama matchmaker-start operation or ticket is already active.";
        private const string SocketCloseCapacityError = "Nakama socket-close operation capacity is exhausted.";
        private const string InvalidMatchmakerResultError = "Nakama matchmaker result contains neither a relayed-match token nor an authoritative match ID.";
        private const long DefaultMatchStateOpCode = 0L;
        private const int MaximumPendingSocketCloseOperations = 4;

        private static readonly object s_socketEpochRegistryGate = new object();
        private static readonly ConditionalWeakTable<ISocket, SocketEpochState> s_socketEpochRegistry =
            new ConditionalWeakTable<ISocket, SocketEpochState>();

        [Header("Lifecycle")]
        [SerializeField] private bool _connectOnStart;

        [Header("Nakama Client")]
        [SerializeField] private string _scheme = "http";
        [SerializeField] private string _host = "127.0.0.1";
        [SerializeField] private int _port = 7350;
        [SerializeField] private string _serverKey = "defaultkey";
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
        [SerializeField, Min(1)] private int _maxPayloadSize = NetworkConstants.DefaultMaxPayloadSize;
        [SerializeField, Min(1)] private int _maxPendingSends = 128;
        [SerializeField, Min(1)] private int _maxConnections = NetworkConstants.DefaultMaxConnections;

        private readonly NetworkMessageHandlerRegistry _messageHandlers =
            new NetworkMessageHandlerRegistry();

        private readonly Dictionary<string, NakamaNetConnection> _connectionsBySessionId =
            new Dictionary<string, NakamaNetConnection>(16, StringComparer.Ordinal);
        private readonly Dictionary<int, NakamaNetConnection> _connectionsById =
            new Dictionary<int, NakamaNetConnection>(16);

        private MessageValidator _messageValidator;

        private readonly byte[] _sendBuffer = new byte[NetworkConstants.MaxMTU];

        private IClient _client;
        private ISocket _socket;
        private ISession _session;
        private IMatch _match;
        private INetworkRuntimeContext _runtimeContext;
        private NetworkLifecycleState _lifecycleState = NetworkLifecycleState.Stopped;
        private TransportError _lastError;
        private string _lastErrorMessage = string.Empty;
        private CancellationTokenSource _shutdown;
        private Task<ISession> _authenticationTask;
        private IClient _authenticationClient;
        private Task _connectTask = Task.CompletedTask;
        private ISocket _connectTaskSocket;
        private int _connectTaskGeneration;
        private readonly ISocket[] _socketCloseSockets =
            new ISocket[MaximumPendingSocketCloseOperations + 1];
        private readonly Task[] _socketCloseTasks =
            new Task[MaximumPendingSocketCloseOperations + 1];
        private readonly UnitySocket[] _socketCloseDispatchers =
            new UnitySocket[MaximumPendingSocketCloseOperations + 1];
        private int _sendSequence;
        private long _bytesSent;
        private long _bytesReceived;
        private int _packetsSent;
        private int _packetsReceived;
        private int _pendingSends;
        private int _nextConnectionId;
        private int _operationGeneration;
        private int _sessionGeneration;
        private int _authenticationSessionGeneration;
        private int _socketEventGeneration;
        private int _mainThreadId;
        private bool _socketEventsBound;
        private bool _socketOwnedByAdapter;
        private bool _socketConnectionEpochStarted;
        private bool _isDestroyed;
        private bool _connectOperationPending;
        private bool _connectRequestQueued;
        private bool _queuedConnectStartScheduled;
        private ISocket _queuedConnectSocket;
        private int _queuedConnectGeneration;
        private bool _leaveMatchOperationPending;
        private bool _cancelMatchmakerOperationPending;
        private bool _addMatchmakerOperationPending;
        private bool _matchmakerJoinOperationPending;
        private bool _socketCloseCapacityReported;
        private ISocket _socketEventSource;
        private Action<string> _socketClosedHandler;
        private Action<Exception> _socketErrorHandler;
        private Action<IMatchState> _matchStateHandler;
        private Action<IMatchPresenceEvent> _matchPresenceHandler;
        private Action<IMatchmakerMatched> _matchmakerMatchedHandler;
        private UnitySocket _ownedSocketDispatcher;
        private NakamaNetConnection _authorityConnection;

        private sealed class SocketEpochState
        {
            internal SocketEpochState(NakamaNetAdapter owner, bool isAdapterOwned)
            {
                Owner = new WeakReference<NakamaNetAdapter>(owner);
                IsAdapterOwned = isAdapterOwned;
            }

            internal WeakReference<NakamaNetAdapter> Owner;
            internal bool IsAdapterOwned;
            internal bool EpochStarted;
        }

        private readonly struct SocketCloseReservation
        {
            internal SocketCloseReservation(
                int slot,
                TaskCompletionSource<bool> completion)
            {
                Slot = slot;
                Completion = completion;
            }

            internal int Slot { get; }
            internal TaskCompletionSource<bool> Completion { get; }
            internal bool RequiresStart => Completion != null;
        }

        public IClient Client
        {
            get
            {
                EnsureMainThread();
                return _client;
            }
        }

        public ISocket Socket
        {
            get
            {
                EnsureMainThread();
                return _socket;
            }
        }

        public ISession NakamaSession
        {
            get
            {
                EnsureMainThread();
                return _session;
            }
        }

        public IMatch CurrentMatch
        {
            get
            {
                EnsureMainThread();
                return _match;
            }
        }

        public bool IsServer => false;
        public bool IsClient
        {
            get
            {
                EnsureMainThread();
                return _socket != null && _socket.IsConnected && _match != null;
            }
        }

        public bool IsRunning => IsClient;
        public bool IsEncrypted => string.Equals(_scheme, "https", StringComparison.OrdinalIgnoreCase);
        public bool Available => true;
        public NetworkBackendFeatures Features => NetworkBackendFeatures.RealtimeTransport
                                                  | NetworkBackendFeatures.AuthSession
                                                  | NetworkBackendFeatures.Matchmaker
                                                  | NetworkBackendFeatures.MatchState
                                                  | NetworkBackendFeatures.Presence;
        public NetworkTransportCapabilities Capabilities => new NetworkTransportCapabilities(
            "Nakama",
            NetworkTransportFeatureFlags.Client
            | NetworkTransportFeatureFlags.Reliable
            | NetworkTransportFeatureFlags.Backpressure
            | NetworkTransportFeatureFlags.WebGLCompatible
            | NetworkTransportFeatureFlags.MainThreadOnly,
            NetworkChannelFlags.Reliable,
            _maxConnections,
            _maxPayloadSize,
            _maxPayloadSize,
            0,
            _maxPendingSends);

        public INetTransport Transport => this;
        public bool IsAcceptingMessages
        {
            get
            {
                EnsureMainThread();
                return !_isDestroyed && _match != null && _socket != null && _socket.IsConnected;
            }
        }
        public INetworkRuntimeContext RuntimeContext => _runtimeContext;

        public bool HasSession
        {
            get
            {
                EnsureMainThread();
                return _session != null && !_session.IsExpired;
            }
        }

        public INetworkSession CurrentSession
        {
            get
            {
                EnsureMainThread();
                return _session != null ? new NakamaNetworkSession(_session) : null;
            }
        }

        public NetworkMatchId CurrentMatchId
        {
            get
            {
                EnsureMainThread();
                return _match != null ? new NetworkMatchId(_match.Id) : default;
            }
        }

        public bool IsInMatch
        {
            get
            {
                EnsureMainThread();
                return _match != null;
            }
        }
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
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;

            if (!string.Equals(_scheme, "http", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(_scheme, "https", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Nakama scheme must be either 'http' or 'https'.");
            }
            if (string.IsNullOrWhiteSpace(_host))
                throw new InvalidOperationException("Nakama host cannot be empty.");
            if (_port < 1 || _port > 65535)
                throw new InvalidOperationException("Nakama port must be between 1 and 65535.");
            if (_connectTimeoutSeconds <= 0)
                throw new InvalidOperationException("Nakama connect timeout must be greater than zero.");
            if (_maxPayloadSize <= 0
                || _maxPayloadSize > NetworkConstants.MaxMTU - NetworkWireProtocol.HeaderLength)
            {
                throw new InvalidOperationException(
                    $"Nakama max payload size must be between 1 and {NetworkConstants.MaxMTU - NetworkWireProtocol.HeaderLength} bytes.");
            }
            if (_maxPendingSends <= 0)
                throw new InvalidOperationException("Nakama max pending sends must be greater than zero.");
            if (_maxConnections <= 0)
                throw new InvalidOperationException("Nakama max connections must be greater than zero.");

            _shutdown = new CancellationTokenSource();
            _messageValidator = new MessageValidator(_maxPayloadSize, 0);
            EnsureClientAndSocket();
            _runtimeContext = BuildRuntimeContext();
        }

        private void Start()
        {
            if (_connectOnStart)
                StartClient(_host);
        }

        private void OnDestroy()
        {
            _isDestroyed = true;
            ISocket socket = _socket;
            UnitySocket ownedDispatcher = _ownedSocketDispatcher;
            bool ownsSocket = _socketOwnedByAdapter;
            bool socketEpochStarted = _socketConnectionEpochStarted;
            bool socketWasActive = socket != null && (socket.IsConnected || socket.IsConnecting);
            UnbindSocketEvents();
            CancelCurrentOperations(createReplacement: false);
            ClearMatchState(notifyDisconnected: false);

            if (socket != null && (ownsSocket || socketWasActive))
                RequestSocketClose(
                    socket,
                    reportErrors: false,
                    ownsSocket ? ownedDispatcher : null,
                    useDestroyReservation: true);

            if (socket != null)
            {
                RelinquishSocketClaim(
                    socket,
                    this,
                    retire: ownsSocket || socketEpochStarted || socketWasActive);
            }

            _client = null;
            _socket = null;
            _session = null;
            _socketOwnedByAdapter = false;
            _socketConnectionEpochStarted = false;
            _ownedSocketDispatcher = null;
            _authenticationTask = null;
            _authenticationClient = null;
            _authenticationSessionGeneration = 0;
            _messageHandlers.Clear();
            _lifecycleState = NetworkLifecycleState.Disposed;
            _runtimeContext?.Dispose();
            _runtimeContext = null;

            OnClientConnected = null;
            OnClientDisconnected = null;
            OnConnectedToServer = null;
            OnDisconnectedFromServer = null;
            OnError = null;
            OnDataReceived = null;
            OnMatchState = null;
            OnMatched = null;
            OnPresenceJoined = null;
            OnPresenceLeft = null;
        }

        public void Initialize(IClient client, ISocket socket, ISession session, string matchId = null)
        {
            EnsureMainThread();
            ThrowIfDisposed();
            if (client == null)
                throw new ArgumentNullException(nameof(client));
            if (socket == null)
                throw new ArgumentNullException(nameof(socket));
            if (socket.IsConnected || socket.IsConnecting)
            {
                throw new InvalidOperationException(
                    "Initialize requires a newly created, disconnected Nakama socket whose connection epoch has not started.");
            }
            if (ReferenceEquals(_socket, socket) && _socketConnectionEpochStarted)
            {
                throw new InvalidOperationException(UnavailableSocketEpochError);
            }
            if (ReferenceEquals(_socket, socket) && !ReferenceEquals(_client, client))
            {
                throw new InvalidOperationException(
                    "The current Nakama socket cannot be rebound to a different client.");
            }

            ISocket previousSocket = _socket;
            UnitySocket previousOwnedDispatcher = _ownedSocketDispatcher;
            bool previousSocketOwnedByAdapter = _socketOwnedByAdapter;
            bool previousSocketEpochStarted = _socketConnectionEpochStarted;
            bool previousSocketWasActive = previousSocket != null
                                           && (previousSocket.IsConnected || previousSocket.IsConnecting);
            bool replacingSocket = !ReferenceEquals(previousSocket, socket);
            bool requestedSocketOwnedByAdapter = !replacingSocket && previousSocketOwnedByAdapter;
            bool acquiredSocketClaim = ClaimSocket(socket, this, requestedSocketOwnedByAdapter);
            bool closePreviousSocket = replacingSocket
                                       && previousSocket != null
                                       && (previousSocketOwnedByAdapter
                                           || previousSocketWasActive);
            SocketCloseReservation previousCloseReservation = default;
            if (closePreviousSocket
                && !TryReserveSocketClose(
                    previousSocket,
                    reportErrors: false,
                    previousSocketOwnedByAdapter ? previousOwnedDispatcher : null,
                    useDestroyReservation: false,
                    out previousCloseReservation))
            {
                if (acquiredSocketClaim)
                    RelinquishSocketClaim(socket, this, retire: requestedSocketOwnedByAdapter);
                throw new InvalidOperationException(SocketCloseCapacityError);
            }

            bool previousSocketEventsUnbound = false;
            try
            {
                UnbindSocketEvents();
                previousSocketEventsUnbound = true;
                CancelCurrentOperations(createReplacement: true);
                ClearMatchState(notifyDisconnected: false);
                unchecked
                {
                    _sessionGeneration++;
                }
                if (_authenticationTask == null || _authenticationTask.IsCompleted)
                {
                    _authenticationTask = null;
                    _authenticationClient = null;
                    _authenticationSessionGeneration = 0;
                }

                if (replacingSocket && previousSocket != null)
                {
                    RelinquishSocketClaim(
                        previousSocket,
                        this,
                        retire: previousSocketOwnedByAdapter
                                || previousSocketEpochStarted
                                || previousSocketWasActive);
                }

                _client = client;
                _socket = socket;
                if (replacingSocket)
                {
                    _socketOwnedByAdapter = false;
                    _socketConnectionEpochStarted = false;
                    _ownedSocketDispatcher = null;
                }
                _session = session;
                if (!string.IsNullOrEmpty(matchId))
                    _matchId = matchId;

                BindSocketEvents();
            }
            catch (Exception e) when (IsRecoverableException(e))
            {
                if (acquiredSocketClaim && !ReferenceEquals(_socket, socket))
                    RelinquishSocketClaim(socket, this, retire: requestedSocketOwnedByAdapter);
                throw;
            }
            finally
            {
                if (closePreviousSocket)
                {
                    if (previousSocketEventsUnbound)
                    {
                        StartReservedSocketClose(
                            previousSocket,
                            reportErrors: false,
                            previousCloseReservation);
                    }
                    else
                    {
                        ReleaseSocketCloseReservation(previousCloseReservation);
                    }
                }
            }

            _runtimeContext?.Dispose();
            _runtimeContext = BuildRuntimeContext();
        }

        public int GetChannelId(NetworkChannel channel)
        {
            if (channel != NetworkChannel.Reliable)
                throw new NotSupportedException(UnsupportedChannelError);

            return (int)NetworkChannel.Reliable;
        }
        public int GetMaxPacketSize(int channelId) =>
            channelId == GetChannelId(NetworkChannel.Reliable) ? _maxPayloadSize : 0;

        public int GetMaxPayloadSize(ushort messageId, NetworkChannel channel)
        {
            if (channel != NetworkChannel.Reliable)
                return 0;

            int maximumPayloadSize = _maxPayloadSize;
            if (_runtimeContext != null
                && _runtimeContext.TryGetService(out INetworkMessageCatalog catalog))
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

        public NetworkStatistics GetStatistics()
        {
            EnsureMainThread();
            return new NetworkStatistics(
                Interlocked.Read(ref _bytesSent),
                Interlocked.Read(ref _bytesReceived),
                _packetsSent,
                _packetsReceived,
                _connectionsById.Count,
                0,
                0f);
        }

        public NetworkLifecycleSnapshot GetLifecycleSnapshot()
        {
            EnsureMainThread();
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
            EnsureMainThread();
            ThrowIfDisposed();
            throw new NotSupportedException(UnsupportedServerError);
        }

        public void StartClient(string address)
        {
            EnsureMainThread();
            ThrowIfDisposed();
            if (!string.IsNullOrWhiteSpace(address)
                && !string.Equals(address, _host, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Nakama endpoint selection is owned by the adapter Host, Port, and Scheme configuration.",
                    nameof(address));
            }

            if (_lifecycleState == NetworkLifecycleState.ClientRunning
                && _socket != null
                && _socket.IsConnected)
            {
                return;
            }

            if (_connectOperationPending || _queuedConnectStartScheduled)
            {
                bool isSameActiveRequest = ReferenceEquals(_connectTaskSocket, _socket)
                                           && _connectTaskGeneration == _operationGeneration
                                           && _shutdown != null
                                           && !_shutdown.IsCancellationRequested
                                           && _lifecycleState == NetworkLifecycleState.StartingClient;
                if (isSameActiveRequest)
                    return;

                ThrowIfInjectedSocketRetired();
                QueueLatestConnectRequest();
                return;
            }

            ThrowIfInjectedSocketRetired();
            BeginConnect();
        }

        private void BeginConnect()
        {
            EnsureMainThread();
            ThrowIfDisposed();
            PrepareSocketForConnectionEpoch();
            CancellationToken cancellationToken = CancelCurrentOperations(createReplacement: true);
            int operationGeneration = _operationGeneration;
            _connectTaskSocket = _socket;
            _connectTaskGeneration = operationGeneration;
            _connectOperationPending = true;
            _connectTask = ConnectAndJoinAsync(cancellationToken, operationGeneration);
            _connectTask.AsUniTask(useCurrentSynchronizationContext: false).Forget();
        }

        public void Stop()
        {
            EnsureMainThread();
            if (_isDestroyed)
                return;
            UnbindSocketEvents();
            CancelCurrentOperations(createReplacement: true);

            ISocket socket = _socket;
            bool shouldClose = socket != null
                               && (_socketOwnedByAdapter || socket.IsConnected || socket.IsConnecting);

            ClearMatchState(notifyDisconnected: false);
            _lifecycleState = NetworkLifecycleState.Stopped;
            if (shouldClose)
            {
                RequestSocketClose(
                    socket,
                    reportErrors: true,
                    _socketOwnedByAdapter ? _ownedSocketDispatcher : null);
            }
        }

        public void Disconnect(INetConnection connection)
        {
            EnsureMainThread();
            ThrowIfDisposed();
            throw new NotSupportedException(
                "Nakama clients cannot disconnect an individual remote presence. Stop the local adapter to leave the match.");
        }

        public NetworkSendResult Send(INetConnection connection, in ArraySegment<byte> payload, int channelId)
        {
            EnsureMainThread();
            ThrowIfDisposed();
            if (channelId != GetChannelId(NetworkChannel.Reliable))
                return NetworkSendResult.Fail(NetworkSendStatus.Unsupported, channelId, connection, UnsupportedChannelError);
            if (!ValidatePayload(payload))
                return NetworkSendResult.Fail(NetworkSendStatus.InvalidPayload, channelId, connection);

            if (!IsInMatch)
                return NetworkSendResult.Fail(NetworkSendStatus.NotConnected, channelId, connection, MissingMatchError);
            if (connection is not NakamaNetConnection nakamaConnection
                || !IsCurrentConnection(nakamaConnection))
                return NetworkSendResult.Fail(NetworkSendStatus.NotConnected, channelId, connection);

            IEnumerable<IUserPresence> targets;
            if (nakamaConnection.RouteRole == NakamaConnectionRouteRole.Authority)
            {
                if (_match == null
                    || !_match.Authoritative
                    || !ReferenceEquals(nakamaConnection, _authorityConnection))
                {
                    return NetworkSendResult.Fail(
                        NetworkSendStatus.Unsupported,
                        channelId,
                        connection,
                        NonAuthoritativeMatchError);
                }

                targets = null;
            }
            else
            {
                if (nakamaConnection.Presence == null)
                    return NetworkSendResult.Fail(NetworkSendStatus.NotConnected, channelId, connection);
                targets = nakamaConnection.TargetPresences;
            }

            byte[] buffer = GetSendBuffer();
            int frameLength = BuildFrame(NetworkConstants.SystemMsgIdMin, NetworkChannel.Reliable, payload, buffer);
            NetworkSendStatus status = QueueFrameToMatch(
                new ArraySegment<byte>(buffer, 0, frameLength),
                targets);
            return status == NetworkSendStatus.Queued
                ? NetworkSendResult.Queued(frameLength, channelId, connection)
                : NetworkSendResult.Fail(status, channelId, connection, GetSendFailureReason(status));
        }

        public NetworkSendResult Broadcast(IReadOnlyList<INetConnection> connections, in ArraySegment<byte> payload, int channelId)
        {
            EnsureMainThread();
            ThrowIfDisposed();
            if (connections == null)
                throw new ArgumentNullException(nameof(connections));
            return NetworkSendResult.Fail(
                NetworkSendStatus.Unsupported,
                channelId,
                reason: UnsupportedClientRouteError);
        }

        public NetworkMessageHandlerLease RegisterHandler(ushort messageId, NetworkMessageHandler handler)
        {
            EnsureMainThread();
            ThrowIfDisposed();
            return _messageHandlers.Register(messageId, handler);
        }

        public NetworkSendResult SendToServer(
            ushort messageId,
            ReadOnlySpan<byte> payload,
            NetworkChannel channel = NetworkChannel.Reliable)
        {
            EnsureMainThread();
            ThrowIfDisposed();
            if (!TryPrepareCanonicalPayload(messageId, channel, payload.Length, null, out _, out NetworkSendResult failure))
                return failure;

            if (_match == null || !_match.Authoritative)
            {
                return NetworkSendResult.Fail(
                    NetworkSendStatus.Unsupported,
                    GetChannelId(channel),
                    reason: NonAuthoritativeMatchError);
            }

            return SendCanonicalMessage(messageId, payload, channel, null);
        }

        public NetworkSendResult SendToClient(
            INetConnection connection,
            ushort messageId,
            ReadOnlySpan<byte> payload,
            NetworkChannel channel = NetworkChannel.Reliable)
        {
            EnsureMainThread();
            ThrowIfDisposed();
            int channelId = channel == NetworkChannel.Reliable ? GetChannelId(channel) : (int)channel;
            return NetworkSendResult.Fail(
                NetworkSendStatus.Unsupported,
                channelId,
                connection,
                UnsupportedClientRouteError);
        }

        public NetworkSendResult BroadcastToClients(
            ushort messageId,
            ReadOnlySpan<byte> payload,
            NetworkChannel channel = NetworkChannel.Reliable)
        {
            EnsureMainThread();
            ThrowIfDisposed();
            int channelId = channel == NetworkChannel.Reliable ? GetChannelId(channel) : (int)channel;
            return NetworkSendResult.Fail(
                NetworkSendStatus.Unsupported,
                channelId,
                reason: UnsupportedClientRouteError);
        }

        public NetworkSendResult Broadcast(
            IReadOnlyList<INetConnection> connections,
            ushort messageId,
            ReadOnlySpan<byte> payload,
            NetworkChannel channel = NetworkChannel.Reliable)
        {
            EnsureMainThread();
            ThrowIfDisposed();
            if (connections == null)
                throw new ArgumentNullException(nameof(connections));
            int channelId = channel == NetworkChannel.Reliable ? GetChannelId(channel) : (int)channel;
            return NetworkSendResult.Fail(
                NetworkSendStatus.Unsupported,
                channelId,
                reason: UnsupportedClientRouteError);
        }

        private bool TryPrepareCanonicalPayload(
            ushort messageId,
            NetworkChannel channel,
            int payloadLength,
            INetConnection connection,
            out int channelId,
            out NetworkSendResult failure)
        {
            if (channel != NetworkChannel.Reliable)
            {
                channelId = (int)channel;
                failure = NetworkSendResult.Fail(NetworkSendStatus.ChannelUnavailable, channelId, connection, UnsupportedChannelError);
                return false;
            }

            channelId = GetChannelId(channel);
            if (!IsAcceptingMessages)
            {
                failure = NetworkSendResult.Fail(NetworkSendStatus.NotConnected, channelId, connection, MissingMatchError);
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

        public void ClearSession()
        {
            EnsureMainThread();
            ThrowIfDisposed();
            unchecked
            {
                _sessionGeneration++;
            }
            _session = null;

            if (_connectOperationPending
                || _connectRequestQueued
                || _queuedConnectStartScheduled
                || _lifecycleState == NetworkLifecycleState.StartingClient)
            {
                UnbindSocketEvents();
                CancelCurrentOperations(createReplacement: true);
                ISocket socket = _socket;
                if (socket != null
                    && (_socketOwnedByAdapter || socket.IsConnected || socket.IsConnecting))
                {
                    RequestSocketClose(
                        socket,
                        reportErrors: false,
                        _socketOwnedByAdapter ? _ownedSocketDispatcher : null);
                }
                _lifecycleState = NetworkLifecycleState.Stopped;
            }
        }

        public bool TrySendMatchState(NetworkMatchId matchId, long operationCode, in ArraySegment<byte> payload, NetworkChannel channel = NetworkChannel.Reliable)
        {
            EnsureMainThread();
            ThrowIfDisposed();
            if (channel != NetworkChannel.Reliable || !matchId.IsValid || !ValidatePayload(payload))
                return false;

            return QueueMatchState(matchId.Value, operationCode, payload, null) == NetworkSendStatus.Queued;
        }

        public void LeaveMatch(NetworkMatchId matchId)
        {
            EnsureMainThread();
            ThrowIfDisposed();
            if (_socket == null || !matchId.IsValid)
                return;
            if (_leaveMatchOperationPending)
            {
                RaiseError(null, TransportError.Congestion, LeaveMatchCapacityError);
                return;
            }

            _leaveMatchOperationPending = true;
            LeaveMatchAsync(
                _socket,
                matchId.Value,
                _shutdown != null ? _shutdown.Token : CancellationToken.None,
                _operationGeneration).Forget();
        }

        public bool TryCancelMatchmaker(NetworkMatchmakerTicket ticket)
        {
            EnsureMainThread();
            ThrowIfDisposed();
            if (_socket == null ||
                !ticket.IsValid ||
                !IsMatchmaking ||
                !CurrentTicket.Equals(ticket))
            {
                return false;
            }
            if (_cancelMatchmakerOperationPending)
                return false;

            _cancelMatchmakerOperationPending = true;
            CancelMatchmakerAsync(
                _socket,
                ticket.Value,
                _shutdown != null ? _shutdown.Token : CancellationToken.None,
                _operationGeneration).Forget();
            return true;
        }

        public bool TryGetLocalPresence(out NetworkPresence presence)
        {
            EnsureMainThread();
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
            EnsureMainThread();
            ThrowIfDisposed();
            if (_addMatchmakerOperationPending ||
                _cancelMatchmakerOperationPending ||
                _matchmakerJoinOperationPending ||
                IsMatchmaking ||
                CurrentTicket.IsValid)
            {
                RaiseError(null, TransportError.Congestion, MatchmakerStartCapacityError);
                return;
            }

            _addMatchmakerOperationPending = true;
            AddMatchmakerAsync(
                _socket,
                query,
                minCount,
                maxCount,
                _shutdown != null ? _shutdown.Token : CancellationToken.None,
                _operationGeneration).Forget();
        }

        private INetworkRuntimeContext BuildRuntimeContext()
        {
            EnsureMainThread();
            var context = new NetworkRuntimeContext(NetworkRuntimeIds.Nakama, "Nakama", this, Features);
            context.AddService<INetworkSessionService>(this);
            context.AddService<INetworkMatchStateService>(this);
            context.AddService<INetworkMatchmakerService>(this);
            context.AddService<INetworkPresenceService>(this);
            if (_client != null)
                context.AddService<IClient>(_client);
            if (_socket != null)
                context.AddService<ISocket>(_socket);
            return context.Build();
        }

        private void EnsureClientAndSocket()
        {
            EnsureMainThread();
            ThrowIfDisposed();
            if (_client == null)
#if UNITY_WEBGL && !UNITY_EDITOR
                _client = new Client(_scheme, _host, _port, _serverKey, UnityWebRequestAdapter.Instance);
#else
                _client = new Client(_scheme, _host, _port, _serverKey);
#endif
            if (_socket == null)
                CreateOwnedSocket();
            if (!_socketConnectionEpochStarted)
                BindSocketEvents();
        }

        private void PrepareSocketForConnectionEpoch()
        {
            EnsureClientAndSocket();
            if (_socketConnectionEpochStarted)
            {
                if (!_socketOwnedByAdapter)
                    throw new InvalidOperationException(UnavailableSocketEpochError);

                RetireOwnedSocket();
                CreateOwnedSocket();
            }

            ThrowIfSocketUnavailableForNewEpoch();
            BindSocketEvents();
            MarkSocketEpochStarted(_socket, this);
            _socketConnectionEpochStarted = true;
        }

        private void CreateOwnedSocket()
        {
            EnsureMainThread();
            ThrowIfDisposed();
            if (_client == null)
                throw new InvalidOperationException(MissingClientError);

            ISocketAdapter transportAdapter;
#if UNITY_WEBGL && !UNITY_EDITOR
            transportAdapter = new JsWebSocketAdapter();
#else
            transportAdapter = new WebSocketStdlibAdapter();
#endif
            UnitySocket dispatcher = UnitySocket.Create(transportAdapter);
            try
            {
                ISocket socket = global::Nakama.Socket.From(_client, dispatcher);
                ClaimSocket(socket, this, isAdapterOwned: true);
                _socket = socket;
                _ownedSocketDispatcher = dispatcher;
                _socketOwnedByAdapter = true;
                _socketConnectionEpochStarted = false;
                RefreshRuntimeContext();
            }
            catch
            {
                if (dispatcher != null)
                    Destroy(dispatcher.gameObject);
                throw;
            }
        }

        private void RetireOwnedSocket()
        {
            EnsureMainThread();
            ISocket retiredSocket = _socket;
            UnitySocket retiredDispatcher = _ownedSocketDispatcher;
            if (retiredSocket != null
                && !RequestSocketClose(retiredSocket, reportErrors: false, retiredDispatcher))
            {
                throw new InvalidOperationException(SocketCloseCapacityError);
            }

            if (retiredSocket != null)
                RelinquishSocketClaim(retiredSocket, this, retire: true);
            UnbindSocketEvents();
            _socket = null;
            _ownedSocketDispatcher = null;
            _socketOwnedByAdapter = false;
            _socketConnectionEpochStarted = false;

        }

        private void ThrowIfInjectedSocketRetired()
        {
            if (_socket == null || _socketOwnedByAdapter)
                return;
            if (_socketConnectionEpochStarted)
                throw new InvalidOperationException(UnavailableSocketEpochError);

            ThrowIfSocketUnavailableForNewEpoch();
        }

        private static bool ClaimSocket(
            ISocket socket,
            NakamaNetAdapter owner,
            bool isAdapterOwned)
        {
            lock (s_socketEpochRegistryGate)
            {
                if (!s_socketEpochRegistry.TryGetValue(socket, out SocketEpochState state))
                {
                    s_socketEpochRegistry.Add(socket, new SocketEpochState(owner, isAdapterOwned));
                    return true;
                }

                if (state.EpochStarted)
                    throw new InvalidOperationException(UnavailableSocketEpochError);

                if (state.Owner != null
                    && state.Owner.TryGetTarget(out NakamaNetAdapter currentOwner))
                {
                    if (!ReferenceEquals(currentOwner, owner)
                        || state.IsAdapterOwned != isAdapterOwned)
                    {
                        throw new InvalidOperationException(UnavailableSocketEpochError);
                    }

                    return false;
                }

                if (state.IsAdapterOwned)
                {
                    state.EpochStarted = true;
                    state.Owner = null;
                    throw new InvalidOperationException(UnavailableSocketEpochError);
                }

                state.Owner = new WeakReference<NakamaNetAdapter>(owner);
                state.IsAdapterOwned = isAdapterOwned;
                return true;
            }
        }

        private static void MarkSocketEpochStarted(ISocket socket, NakamaNetAdapter owner)
        {
            lock (s_socketEpochRegistryGate)
            {
                if (!TryGetSocketStateForOwner(socket, owner, out SocketEpochState state)
                    || state.EpochStarted)
                {
                    throw new InvalidOperationException(UnavailableSocketEpochError);
                }

                state.EpochStarted = true;
            }
        }

        private static void RelinquishSocketClaim(
            ISocket socket,
            NakamaNetAdapter owner,
            bool retire)
        {
            lock (s_socketEpochRegistryGate)
            {
                if (!TryGetSocketStateForOwner(socket, owner, out SocketEpochState state))
                    return;

                if (retire || state.IsAdapterOwned || state.EpochStarted)
                {
                    state.EpochStarted = true;
                    state.Owner = null;
                    return;
                }

                s_socketEpochRegistry.Remove(socket);
            }
        }

        private void ThrowIfSocketUnavailableForNewEpoch()
        {
            lock (s_socketEpochRegistryGate)
            {
                if (!TryGetSocketStateForOwner(_socket, this, out SocketEpochState state)
                    || state.EpochStarted)
                {
                    throw new InvalidOperationException(UnavailableSocketEpochError);
                }
            }
        }

        private static bool TryGetSocketStateForOwner(
            ISocket socket,
            NakamaNetAdapter owner,
            out SocketEpochState state)
        {
            if (socket != null
                && s_socketEpochRegistry.TryGetValue(socket, out state)
                && state.Owner != null
                && state.Owner.TryGetTarget(out NakamaNetAdapter currentOwner)
                && ReferenceEquals(currentOwner, owner))
            {
                return true;
            }

            state = null;
            return false;
        }

        private void RefreshRuntimeContext()
        {
            if (_runtimeContext == null)
                return;

            _runtimeContext.Dispose();
            _runtimeContext = BuildRuntimeContext();
        }

        private async Task ConnectAndJoinAsync(
            CancellationToken cancellationToken,
            int operationGeneration)
        {
            IClient client = null;
            ISocket socket = null;

            try
            {
                EnsureClientAndSocket();
                client = _client;
                socket = _socket;
                if (client == null)
                    throw new InvalidOperationException(MissingClientError);
                if (socket == null)
                    throw new InvalidOperationException(MissingSocketError);

                _lifecycleState = NetworkLifecycleState.StartingClient;

                Task pendingClose = GetPendingSocketCloseTask(socket);
                if (pendingClose != null && !pendingClose.IsCompleted)
                    await pendingClose;
                EnsureMainThread();
                if (!IsOperationCurrent(operationGeneration, cancellationToken, socket))
                    return;

                if (_session == null && _autoAuthenticateDevice)
                {
                    string deviceId = string.IsNullOrEmpty(_deviceIdOverride) ? SystemInfo.deviceUniqueIdentifier : _deviceIdOverride;
                    int authenticationSessionGeneration = _sessionGeneration;
                    Task<ISession> authenticationTask;
                    ISession authenticatedSession = await AuthenticateDeviceSingleFlightAsync(
                        client,
                        deviceId,
                        _connectTimeoutSeconds,
                        cancellationToken,
                        authenticationSessionGeneration);
                    authenticationTask = _authenticationTask;
                    EnsureMainThread();
                    if (!IsOperationCurrent(operationGeneration, cancellationToken, socket)
                        || authenticationSessionGeneration != _sessionGeneration)
                        return;
                    _session = authenticatedSession;
                    if (ReferenceEquals(_authenticationTask, authenticationTask)
                        && _authenticationSessionGeneration == authenticationSessionGeneration)
                    {
                        _authenticationTask = null;
                        _authenticationClient = null;
                        _authenticationSessionGeneration = 0;
                    }
                }

                if (_session == null)
                {
                    RaiseError(null, TransportError.Unexpected, MissingSessionError);
                    return;
                }

                if (!socket.IsConnected)
                    await socket.ConnectAsync(_session, _appearOnline, _connectTimeoutSeconds, _languageTag);
                EnsureMainThread();
                if (!IsOperationCurrent(operationGeneration, cancellationToken, socket))
                {
                    if (socket.IsConnected)
                        RequestSocketClose(socket, reportErrors: false);
                    return;
                }

                if (_joinMatchOnConnect && !string.IsNullOrEmpty(_matchId))
                {
                    IMatch joinedMatch = await socket.JoinMatchAsync(_matchId, null);
                    EnsureMainThread();
                    if (!IsOperationCurrent(operationGeneration, cancellationToken, socket))
                    {
                        if (socket.IsConnected)
                            RequestSocketClose(socket, reportErrors: false);
                        return;
                    }
                    ClearMatchState(notifyDisconnected: true);
                    if (!IsOperationCurrent(operationGeneration, cancellationToken, socket))
                        return;
                    _match = joinedMatch;
                }

                if (!IsOperationCurrent(operationGeneration, cancellationToken, socket))
                    return;

                _lifecycleState = NetworkLifecycleState.ClientRunning;
                NotifyObserver(OnConnectedToServer);
                if (!IsOperationCurrent(operationGeneration, cancellationToken, socket))
                    return;
                AddInitialPresences(_match);
            }
            catch (Exception e) when (IsRecoverableException(e))
            {
                EnsureMainThread();
                if (!IsOperationCurrent(operationGeneration, cancellationToken, socket))
                    return;
                _lifecycleState = NetworkLifecycleState.Faulted;
                RaiseError(
                    null,
                    e is TimeoutException ? TransportError.Timeout : TransportError.Unexpected,
                    e.Message);
            }
            finally
            {
                if (IsMainThread())
                {
                    _connectOperationPending = false;
                    ScheduleQueuedConnect();
                }
            }
        }

        private async Task<ISession> AuthenticateDeviceSingleFlightAsync(
            IClient client,
            string deviceId,
            int timeoutSeconds,
            CancellationToken cancellationToken,
            int authenticationSessionGeneration)
        {
            while (true)
            {
                EnsureMainThread();
                Task<ISession> authenticationTask = _authenticationTask;
                if (authenticationTask == null
                    || authenticationTask.IsCompleted
                    || (ReferenceEquals(_authenticationClient, client)
                        && _authenticationSessionGeneration == authenticationSessionGeneration))
                {
                    authenticationTask = GetOrCreateAuthenticationTask(
                        client,
                        deviceId,
                        authenticationSessionGeneration);
                    return await AwaitWithTimeoutAsync(authenticationTask, timeoutSeconds, cancellationToken);
                }

                try
                {
                    await AwaitWithTimeoutAsync(authenticationTask, timeoutSeconds, cancellationToken);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // A previous client's provider task reached the canceled state. The slot is
                    // terminal and can be reused without treating that old result as this request.
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (TimeoutException)
                {
                    throw;
                }
                catch (Exception exception) when (IsRecoverableException(exception))
                {
                    // Faults from a previous client are already observed by the authentication
                    // continuation. Once terminal, they do not poison the next client's request.
                }

                EnsureMainThread();
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        private Task<ISession> GetOrCreateAuthenticationTask(
            IClient client,
            string deviceId,
            int authenticationSessionGeneration)
        {
            EnsureMainThread();
            if (ReferenceEquals(_authenticationClient, client)
                && _authenticationSessionGeneration == authenticationSessionGeneration
                && _authenticationTask != null
                && !_authenticationTask.IsCanceled
                && !_authenticationTask.IsFaulted)
            {
                return _authenticationTask;
            }

            if (_authenticationTask != null && !_authenticationTask.IsCompleted)
                throw new InvalidOperationException(AuthenticationOperationCapacityError);

            Task<ISession> authenticationTask = client.AuthenticateDeviceAsync(deviceId, _username, _createAccount);
            _authenticationClient = client;
            _authenticationSessionGeneration = authenticationSessionGeneration;
            _authenticationTask = authenticationTask;
            _ = authenticationTask.ContinueWith(
                static faulted => { _ = faulted.Exception; },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return authenticationTask;
        }

        private static async Task<T> AwaitWithTimeoutAsync<T>(
            Task<T> operation,
            int timeoutSeconds,
            CancellationToken cancellationToken)
        {
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            int timeoutMilliseconds = timeoutSeconds >= int.MaxValue / 1000
                ? int.MaxValue
                : timeoutSeconds * 1000;
            Task timeoutTask = Task.Delay(timeoutMilliseconds, timeout.Token);
            Task completed = await Task.WhenAny(operation, timeoutTask);
            if (ReferenceEquals(completed, operation))
            {
                timeout.Cancel();
                return await operation;
            }

            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"Nakama operation exceeded the {timeoutSeconds}-second timeout.");
        }

        private bool RequestSocketClose(
            ISocket socket,
            bool reportErrors,
            UnitySocket ownedDispatcher = null,
            bool useDestroyReservation = false)
        {
            if (!TryReserveSocketClose(
                    socket,
                    reportErrors,
                    ownedDispatcher,
                    useDestroyReservation,
                    out SocketCloseReservation reservation))
            {
                return false;
            }

            StartReservedSocketClose(socket, reportErrors, reservation);
            return true;
        }

        private bool TryReserveSocketClose(
            ISocket socket,
            bool reportErrors,
            UnitySocket ownedDispatcher,
            bool useDestroyReservation,
            out SocketCloseReservation reservation)
        {
            EnsureMainThread();
            reservation = default;
            if (socket == null)
                return true;

            int availableSlot = -1;
            int slotLimit = useDestroyReservation
                ? _socketCloseTasks.Length
                : MaximumPendingSocketCloseOperations;
            for (int i = 0; i < _socketCloseTasks.Length; i++)
            {
                Task closeTask = _socketCloseTasks[i];
                if (closeTask == null || closeTask.IsCompleted)
                {
                    _socketCloseSockets[i] = null;
                    _socketCloseTasks[i] = null;
                    _socketCloseDispatchers[i] = null;
                    _socketCloseCapacityReported = false;
                    if (i < slotLimit && availableSlot < 0)
                        availableSlot = i;
                    continue;
                }

                if (ReferenceEquals(_socketCloseSockets[i], socket))
                {
                    if (ownedDispatcher != null && _socketCloseDispatchers[i] == null)
                        _socketCloseDispatchers[i] = ownedDispatcher;
                    return true;
                }
            }

            if (availableSlot < 0)
            {
                if (reportErrors && !_socketCloseCapacityReported)
                {
                    _socketCloseCapacityReported = true;
                    RaiseError(null, TransportError.Congestion, SocketCloseCapacityError);
                }
                return false;
            }

            var completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _socketCloseSockets[availableSlot] = socket;
            _socketCloseTasks[availableSlot] = completion.Task;
            _socketCloseDispatchers[availableSlot] = ownedDispatcher;
            reservation = new SocketCloseReservation(availableSlot, completion);
            return true;
        }

        private void StartReservedSocketClose(
            ISocket socket,
            bool reportErrors,
            SocketCloseReservation reservation)
        {
            if (!reservation.RequiresStart)
                return;

            CloseSocketAsync(
                socket,
                reportErrors,
                reservation.Completion,
                reservation.Slot).Forget();
        }

        private void ReleaseSocketCloseReservation(SocketCloseReservation reservation)
        {
            if (!reservation.RequiresStart)
                return;

            int slot = reservation.Slot;
            if ((uint)slot < (uint)_socketCloseTasks.Length
                && ReferenceEquals(_socketCloseTasks[slot], reservation.Completion.Task))
            {
                _socketCloseSockets[slot] = null;
                _socketCloseTasks[slot] = null;
                _socketCloseDispatchers[slot] = null;
            }

            reservation.Completion.TrySetResult(true);
        }

        private Task GetPendingSocketCloseTask(ISocket socket)
        {
            for (int i = 0; i < _socketCloseTasks.Length; i++)
            {
                Task closeTask = _socketCloseTasks[i];
                if (closeTask == null || closeTask.IsCompleted)
                {
                    _socketCloseSockets[i] = null;
                    _socketCloseTasks[i] = null;
                    _socketCloseDispatchers[i] = null;
                    _socketCloseCapacityReported = false;
                    continue;
                }

                if (ReferenceEquals(_socketCloseSockets[i], socket))
                    return closeTask;
            }

            return Task.CompletedTask;
        }

        private async UniTask CloseSocketAsync(
            ISocket socket,
            bool reportErrors,
            TaskCompletionSource<bool> completion,
            int closeSlot)
        {
            try
            {
                EnsureMainThread();
                await socket.CloseAsync();
                EnsureMainThread();
            }
            catch (Exception e) when (IsRecoverableException(e))
            {
                EnsureMainThread();
                if (reportErrors && ReferenceEquals(socket, _socket))
                    RaiseError(null, TransportError.ConnectionClosed, e.Message);
            }
            finally
            {
                UnitySocket ownedDispatcher = closeSlot >= 0 && closeSlot < _socketCloseDispatchers.Length
                    ? _socketCloseDispatchers[closeSlot]
                    : null;
                if (ownedDispatcher != null && IsMainThread())
                    Destroy(ownedDispatcher.gameObject);
                completion.TrySetResult(true);
            }
        }

        private async UniTask LeaveMatchAsync(
            ISocket socket,
            string matchId,
            CancellationToken cancellationToken,
            int operationGeneration)
        {
            try
            {
                EnsureMainThread();
                await socket.LeaveMatchAsync(matchId);
                EnsureMainThread();
                if (!IsOperationCurrent(operationGeneration, cancellationToken, socket))
                    return;
                if (_match != null && string.Equals(_match.Id, matchId, StringComparison.Ordinal))
                ClearMatchState(notifyDisconnected: true);
            }
            catch (Exception e) when (IsRecoverableException(e))
            {
                EnsureMainThread();
                if (!IsOperationCurrent(operationGeneration, cancellationToken, socket))
                    return;
                RaiseError(null, TransportError.Unexpected, e.Message);
            }
            finally
            {
                if (IsMainThread())
                    _leaveMatchOperationPending = false;
            }
        }

        private async UniTask CancelMatchmakerAsync(
            ISocket socket,
            string ticket,
            CancellationToken cancellationToken,
            int operationGeneration)
        {
            try
            {
                EnsureMainThread();
                await socket.RemoveMatchmakerAsync(ticket);
                EnsureMainThread();
                if (!IsOperationCurrent(operationGeneration, cancellationToken, socket))
                    return;
                IsMatchmaking = false;
                CurrentTicket = default;
            }
            catch (Exception e) when (IsRecoverableException(e))
            {
                EnsureMainThread();
                if (!IsOperationCurrent(operationGeneration, cancellationToken, socket))
                    return;
                RaiseError(null, TransportError.Unexpected, e.Message);
            }
            finally
            {
                if (IsMainThread())
                    _cancelMatchmakerOperationPending = false;
            }
        }

        private async UniTask AddMatchmakerAsync(
            ISocket socket,
            string query,
            int minCount,
            int maxCount,
            CancellationToken cancellationToken,
            int operationGeneration)
        {
            try
            {
                EnsureMainThread();
                if (socket == null || !socket.IsConnected)
                {
                    RaiseError(null, TransportError.Unexpected, MissingSocketError);
                    return;
                }

                IMatchmakerTicket ticket = await socket.AddMatchmakerAsync(query, minCount, maxCount);
                EnsureMainThread();
                if (!IsOperationCurrent(operationGeneration, cancellationToken, socket))
                    return;
                CurrentTicket = new NetworkMatchmakerTicket(ticket.Ticket);
                IsMatchmaking = true;
            }
            catch (Exception e) when (IsRecoverableException(e))
            {
                EnsureMainThread();
                if (!IsOperationCurrent(operationGeneration, cancellationToken, socket))
                    return;
                IsMatchmaking = false;
                RaiseError(null, TransportError.Unexpected, e.Message);
            }
            finally
            {
                if (IsMainThread())
                    _addMatchmakerOperationPending = false;
            }
        }

        private void BindSocketEvents()
        {
            EnsureMainThread();
            ThrowIfDisposed();
            if (_socket == null || _socketEventsBound)
                return;

            unchecked
            {
                _socketEventGeneration++;
            }
            int socketEventGeneration = _socketEventGeneration;
            ISocket boundSocket = _socket;
            _socketEventSource = boundSocket;
            _socketClosedHandler = reason =>
                OnSocketClosed(reason, boundSocket, socketEventGeneration);
            _socketErrorHandler = exception =>
                OnSocketError(exception, boundSocket, socketEventGeneration);
            _matchStateHandler = state =>
                OnReceivedMatchState(state, boundSocket, socketEventGeneration);
            _matchPresenceHandler = presence =>
                OnReceivedMatchPresence(presence, boundSocket, socketEventGeneration);
            _matchmakerMatchedHandler = matched =>
                OnReceivedMatchmakerMatched(matched, boundSocket, socketEventGeneration);
            boundSocket.Closed += _socketClosedHandler;
            boundSocket.ReceivedError += _socketErrorHandler;
            boundSocket.ReceivedMatchState += _matchStateHandler;
            boundSocket.ReceivedMatchPresence += _matchPresenceHandler;
            boundSocket.ReceivedMatchmakerMatched += _matchmakerMatchedHandler;
            _socketEventsBound = true;
        }

        private void UnbindSocketEvents()
        {
            EnsureMainThread();
            if (!_socketEventsBound)
                return;

            ISocket boundSocket = _socketEventSource;
            if (boundSocket != null)
            {
                if (_socketClosedHandler != null)
                    boundSocket.Closed -= _socketClosedHandler;
                if (_socketErrorHandler != null)
                    boundSocket.ReceivedError -= _socketErrorHandler;
                if (_matchStateHandler != null)
                    boundSocket.ReceivedMatchState -= _matchStateHandler;
                if (_matchPresenceHandler != null)
                    boundSocket.ReceivedMatchPresence -= _matchPresenceHandler;
                if (_matchmakerMatchedHandler != null)
                    boundSocket.ReceivedMatchmakerMatched -= _matchmakerMatchedHandler;
            }

            _socketEventSource = null;
            _socketClosedHandler = null;
            _socketErrorHandler = null;
            _matchStateHandler = null;
            _matchPresenceHandler = null;
            _matchmakerMatchedHandler = null;
            _socketEventsBound = false;
            unchecked
            {
                _socketEventGeneration++;
            }
        }

        private void OnSocketClosed(
            string reason,
            ISocket socket,
            int socketEventGeneration)
        {
            EnsureMainThread();
            if (!IsSocketEventCurrent(socket, socketEventGeneration))
                return;

            // UnitySocket dispatches close notifications from its Update queue. A completed close can
            // therefore be observed after a serialized restart has already reconnected the same socket.
            if (_socket != null
                && _socket.IsConnected
                && (_lifecycleState == NetworkLifecycleState.StartingClient
                    || _lifecycleState == NetworkLifecycleState.ClientRunning))
            {
                return;
            }

            ClearMatchState(notifyDisconnected: true);
            if (!IsSocketEventCurrent(socket, socketEventGeneration))
                return;
            _lifecycleState = NetworkLifecycleState.Stopped;
            _lastError = TransportError.ConnectionClosed;
            _lastErrorMessage = reason ?? string.Empty;
            NotifyObserver(OnDisconnectedFromServer);
        }

        private void OnSocketError(
            Exception exception,
            ISocket socket,
            int socketEventGeneration)
        {
            EnsureMainThread();
            if (!IsSocketEventCurrent(socket, socketEventGeneration))
                return;
            RaiseError(null, TransportError.Unexpected, exception != null ? exception.Message : string.Empty);
        }

        private void OnReceivedMatchmakerMatched(
            IMatchmakerMatched matched,
            ISocket socket,
            int socketEventGeneration)
        {
            EnsureMainThread();
            if (matched == null ||
                _matchmakerJoinOperationPending ||
                !IsSocketEventCurrent(socket, socketEventGeneration) ||
                !IsMatchmaking ||
                !CurrentTicket.IsValid ||
                !string.Equals(CurrentTicket.Value, matched.Ticket, StringComparison.Ordinal))
            {
                return;
            }

            CancellationToken cancellationToken = _shutdown != null
                ? _shutdown.Token
                : CancellationToken.None;
            int operationGeneration = _operationGeneration;
            if (!IsOperationCurrent(operationGeneration, cancellationToken, socket))
                return;

            _matchmakerJoinOperationPending = true;
            HandleMatchmakerMatchedAsync(
                matched,
                socket,
                cancellationToken,
                operationGeneration,
                CurrentTicket).Forget();
        }

        private async UniTask HandleMatchmakerMatchedAsync(
            IMatchmakerMatched matched,
            ISocket socket,
            CancellationToken cancellationToken,
            int operationGeneration,
            NetworkMatchmakerTicket expectedTicket)
        {
            try
            {
                EnsureMainThread();
                if (!IsOperationCurrent(operationGeneration, cancellationToken, socket) ||
                    !IsMatchmaking ||
                    !CurrentTicket.Equals(expectedTicket) ||
                    !string.Equals(expectedTicket.Value, matched.Ticket, StringComparison.Ordinal))
                {
                    return;
                }

                if (string.IsNullOrEmpty(matched.MatchId) && string.IsNullOrEmpty(matched.Token))
                {
                    IsMatchmaking = false;
                    CurrentTicket = default;
                    RaiseError(null, TransportError.Unexpected, InvalidMatchmakerResultError);
                    return;
                }

                IsMatchmaking = false;
                CurrentTicket = new NetworkMatchmakerTicket(matched.Ticket);
                NotifyMatchedObservers(CurrentTicket, new NetworkMatchId(matched.MatchId));

                if (!IsOperationCurrent(operationGeneration, cancellationToken, socket) ||
                    !CurrentTicket.Equals(expectedTicket))
                {
                    return;
                }

                IMatch joinedMatch = await socket.JoinMatchAsync(matched);
                EnsureMainThread();
                if (!IsOperationCurrent(operationGeneration, cancellationToken, socket))
                    return;
                ClearMatchState(notifyDisconnected: true);
                if (!IsOperationCurrent(operationGeneration, cancellationToken, socket))
                    return;
                _match = joinedMatch;
                _matchId = joinedMatch.Id;
                AddInitialPresences(joinedMatch);
            }
            catch (Exception e) when (IsRecoverableException(e))
            {
                EnsureMainThread();
                if (!IsOperationCurrent(operationGeneration, cancellationToken, socket))
                    return;
                if (CurrentTicket.Equals(expectedTicket))
                    CurrentTicket = default;
                RaiseError(null, TransportError.Unexpected, e.Message);
            }
            finally
            {
                if (IsMainThread())
                    _matchmakerJoinOperationPending = false;
            }
        }

        private void NotifyMatchedObservers(
            NetworkMatchmakerTicket ticket,
            NetworkMatchId matchId)
        {
            try
            {
                OnMatched?.Invoke(ticket, matchId);
            }
            catch (Exception exception) when (IsRecoverableException(exception))
            {
                // Product observers do not own the transport state machine. Publish their
                // failure through the same observed UniTask boundary without aborting JoinMatch.
                PublishObserverException(exception);
            }
        }

        private void NotifyObserver(Action observer)
        {
            try
            {
                observer?.Invoke();
            }
            catch (Exception exception) when (IsRecoverableException(exception))
            {
                PublishObserverException(exception);
            }
        }

        private void NotifyObserver<T>(Action<T> observer, T value)
        {
            try
            {
                observer?.Invoke(value);
            }
            catch (Exception exception) when (IsRecoverableException(exception))
            {
                PublishObserverException(exception);
            }
        }

        private void NotifyObserver<T1, T2>(Action<T1, T2> observer, T1 value1, T2 value2)
        {
            try
            {
                observer?.Invoke(value1, value2);
            }
            catch (Exception exception) when (IsRecoverableException(exception))
            {
                PublishObserverException(exception);
            }
        }

        private void NotifyObserver<T1, T2, T3>(
            Action<T1, T2, T3> observer,
            T1 value1,
            T2 value2,
            T3 value3)
        {
            try
            {
                observer?.Invoke(value1, value2, value3);
            }
            catch (Exception exception) when (IsRecoverableException(exception))
            {
                PublishObserverException(exception);
            }
        }

        private void NotifyObserver<T1, T2, T3, T4>(
            Action<T1, T2, T3, T4> observer,
            T1 value1,
            T2 value2,
            T3 value3,
            T4 value4)
        {
            try
            {
                observer?.Invoke(value1, value2, value3, value4);
            }
            catch (Exception exception) when (IsRecoverableException(exception))
            {
                PublishObserverException(exception);
            }
        }

        private void OnReceivedMatchPresence(
            IMatchPresenceEvent presenceEvent,
            ISocket socket,
            int socketEventGeneration)
        {
            EnsureMainThread();
            if (!IsSocketEventCurrent(socket, socketEventGeneration) ||
                presenceEvent == null ||
                !IsCurrentMatch(presenceEvent.MatchId))
            {
                return;
            }

            NetworkMatchId matchId = new NetworkMatchId(presenceEvent.MatchId);
            IEnumerable<IUserPresence> joins = presenceEvent.Joins;
            if (joins != null)
            {
                foreach (IUserPresence joined in joins)
                {
                    if (!IsSocketEventCurrent(socket, socketEventGeneration)
                        || !IsCurrentMatch(presenceEvent.MatchId))
                        return;
                    if (joined == null)
                        continue;

                    if (!IsLocalPresence(joined))
                    {
                        bool wasConnected = TryGetConnection(joined, out NakamaNetConnection existingConnection)
                                            && existingConnection.IsConnected;
                        NakamaNetConnection connection = GetOrCreateConnection(joined);
                        if (connection != null && !wasConnected)
                        {
                            NotifyObserver(OnClientConnected, (INetConnection)connection);
                            if (!IsSocketEventCurrent(socket, socketEventGeneration)
                                || !IsCurrentMatch(presenceEvent.MatchId)
                                || !IsCurrentConnection(connection))
                            {
                                return;
                            }
                        }
                    }
                    if (!IsSocketEventCurrent(socket, socketEventGeneration)
                        || !IsCurrentMatch(presenceEvent.MatchId))
                        return;
                    NotifyObserver(OnPresenceJoined, matchId, ToNetworkPresence(joined));
                }
            }

            IEnumerable<IUserPresence> leaves = presenceEvent.Leaves;
            if (leaves == null)
                return;

            foreach (IUserPresence left in leaves)
            {
                if (!IsSocketEventCurrent(socket, socketEventGeneration)
                    || !IsCurrentMatch(presenceEvent.MatchId))
                    return;
                if (left == null)
                    continue;

                if (TryGetConnection(left, out NakamaNetConnection connection))
                {
                    _connectionsBySessionId.Remove(GetPresenceKey(left));
                    _connectionsById.Remove(connection.ConnectionId);
                    connection.Invalidate();
                    NotifyObserver(OnClientDisconnected, (INetConnection)connection);
                }

                if (!IsSocketEventCurrent(socket, socketEventGeneration)
                    || !IsCurrentMatch(presenceEvent.MatchId))
                    return;
                NotifyObserver(OnPresenceLeft, matchId, ToNetworkPresence(left));
            }
        }

        private void OnReceivedMatchState(
            IMatchState state,
            ISocket socket,
            int socketEventGeneration)
        {
            EnsureMainThread();
            if (!IsSocketEventCurrent(socket, socketEventGeneration) ||
                state == null ||
                state.State == null)
                return;

            if (_match == null
                || string.IsNullOrEmpty(state.MatchId)
                || !string.Equals(_match.Id, state.MatchId, StringComparison.Ordinal))
            {
                return;
            }

            NakamaNetConnection connection;
            NetworkMessageDirection direction;
            if (state.UserPresence == null)
            {
                connection = GetOrCreateAuthorityConnection(state.MatchId);
                direction = NetworkMessageDirection.ServerToClient;
            }
            else
            {
                if (IsLocalPresence(state.UserPresence))
                    return;

                connection = GetOrCreateConnection(state.UserPresence);
                direction = NetworkMessageDirection.PeerToPeer;
            }

            if (connection == null)
                return;

            connection.RecordReceived(state.State.Length);
            Interlocked.Add(ref _bytesReceived, state.State.Length);
            Interlocked.Increment(ref _packetsReceived);

            var raw = new ArraySegment<byte>(state.State);
            NotifyObserver(
                OnMatchState,
                new NetworkMatchId(state.MatchId),
                (INetConnection)connection,
                raw,
                state.OpCode);
            if (!IsSocketEventCurrent(socket, socketEventGeneration)
                || !IsCurrentMatch(state.MatchId)
                || !IsCurrentConnection(connection))
                return;

            if (state.OpCode != _matchStateOpCode)
                return;

            if (!TryValidateIncoming(connection, raw, out NetworkEnvelopeHeader header, out ArraySegment<byte> payload))
                return;

            NotifyObserver(
                OnDataReceived,
                (INetConnection)connection,
                payload,
                GetChannelId(header.Channel));
            if (!IsSocketEventCurrent(socket, socketEventGeneration)
                || !IsCurrentMatch(state.MatchId)
                || !IsCurrentConnection(connection))
            {
                return;
            }
            try
            {
                var bytes = new ReadOnlySpan<byte>(payload.Array, payload.Offset, payload.Count);
                var message = new NetworkMessagePayload(
                    connection,
                    direction,
                    in header,
                    bytes);
                _messageHandlers.TryDispatch(in message);
            }
            catch (Exception e) when (IsRecoverableException(e))
            {
                if (IsSocketEventCurrent(socket, socketEventGeneration)
                    && IsCurrentMatch(state.MatchId)
                    && IsCurrentConnection(connection))
                {
                    RaiseError(connection, TransportError.InvalidReceive, e.Message);
                }
                else
                {
                    PublishObserverException(e);
                }
            }
        }

        private void AddInitialPresences(IMatch match)
        {
            if (match == null)
                return;

            if (match.Authoritative)
                GetOrCreateAuthorityConnection(match.Id);

            string selfKey = GetPresenceKey(match.Self);
            IEnumerable<IUserPresence> presences = match.Presences;
            if (presences == null)
                return;

            foreach (IUserPresence presence in presences)
            {
                if (!IsCurrentMatch(match.Id))
                    return;
                if (presence == null
                    || (!string.IsNullOrEmpty(selfKey)
                        && string.Equals(selfKey, GetPresenceKey(presence), StringComparison.Ordinal)))
                {
                    continue;
                }

                bool wasConnected = TryGetConnection(presence, out NakamaNetConnection existingConnection)
                                    && existingConnection.IsConnected;
                NakamaNetConnection connection = GetOrCreateConnection(presence);
                if (connection != null && !wasConnected)
                {
                    NotifyObserver(OnClientConnected, (INetConnection)connection);
                    if (!IsCurrentMatch(match.Id) || !IsCurrentConnection(connection))
                        return;
                }
                if (!IsCurrentMatch(match.Id))
                    return;
                NotifyObserver(
                    OnPresenceJoined,
                    new NetworkMatchId(match.Id),
                    ToNetworkPresence(presence));
            }
        }

        private NetworkSendResult SendCanonicalMessage(
            ushort messageId,
            ReadOnlySpan<byte> payload,
            NetworkChannel channel,
            IEnumerable<IUserPresence> targetPresences)
        {
            if (channel != NetworkChannel.Reliable)
            {
                return NetworkSendResult.Fail(NetworkSendStatus.Unsupported, (int)channel, reason: UnsupportedChannelError);
            }

            int channelId = GetChannelId(channel);
            if (!IsInMatch)
            {
                return NetworkSendResult.Fail(NetworkSendStatus.NotConnected, channelId, reason: MissingMatchError);
            }

            byte[] buffer = GetSendBuffer();
            try
            {
                int frameLength = BuildFrameUnchecked(messageId, channel, payload, buffer);
                NetworkSendStatus status = QueueFrameToMatch(
                    new ArraySegment<byte>(buffer, 0, frameLength),
                    targetPresences);
                return status == NetworkSendStatus.Queued
                    ? NetworkSendResult.Queued(frameLength, channelId)
                    : NetworkSendResult.Fail(status, channelId, reason: GetSendFailureReason(status));
            }
            catch (Exception e) when (IsRecoverableException(e))
            {
                RaiseError(null, TransportError.InvalidSend, e.Message);
                return NetworkSendResult.Fail(NetworkSendStatus.InvalidPayload, channelId, reason: e.Message);
            }
        }

        private int BuildFrame(ushort msgId, NetworkChannel channel, in ArraySegment<byte> payload, byte[] buffer)
        {
            if (payload.Array == null)
                throw new ArgumentException("Payload must reference a valid array.", nameof(payload));

            if (payload.Offset < 0
                || payload.Count < 0
                || payload.Offset > payload.Array.Length
                || payload.Count > payload.Array.Length - payload.Offset)
            {
                throw new ArgumentOutOfRangeException(nameof(payload));
            }

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
                throw new InvalidOperationException("Payload exceeds the available Nakama message route.");

            return BuildFrameUnchecked(msgId, channel, payload, buffer);
        }

        private int BuildFrameUnchecked(ushort msgId, NetworkChannel channel, ReadOnlySpan<byte> payload, byte[] buffer)
        {
            int frameLength = NetworkFrameCodec.GetFrameLength(payload.Length);
            if (frameLength > buffer.Length)
                throw new InvalidOperationException("Frame size exceeds send buffer capacity.");

            payload.CopyTo(new Span<byte>(buffer, NetworkWireProtocol.HeaderLength, payload.Length));
            WriteFrameHeader(msgId, channel, buffer, payload.Length);
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

        private NetworkSendStatus QueueFrameToMatch(
            ArraySegment<byte> frame,
            IEnumerable<IUserPresence> presences)
        {
            string matchId = _match != null ? _match.Id : _matchId;
            if (string.IsNullOrEmpty(matchId))
                return NetworkSendStatus.NotConnected;

            return QueueMatchState(matchId, _matchStateOpCode, frame, presences);
        }

        private NetworkSendStatus QueueMatchState(
            string matchId,
            long opCode,
            ArraySegment<byte> payload,
            IEnumerable<IUserPresence> presences)
        {
            ISocket socket = _socket;
            if (socket == null
                || !socket.IsConnected
                || !_socketEventsBound
                || !ReferenceEquals(socket, _socketEventSource))
            {
                return NetworkSendStatus.NotConnected;
            }

            if (Interlocked.Increment(ref _pendingSends) > _maxPendingSends)
            {
                Interlocked.Decrement(ref _pendingSends);
                return NetworkSendStatus.Backpressure;
            }

            CancellationToken cancellationToken = _shutdown != null
                ? _shutdown.Token
                : CancellationToken.None;
            SendMatchStateAsync(
                socket,
                matchId,
                opCode,
                payload,
                presences,
                cancellationToken,
                _operationGeneration).Forget();
            return NetworkSendStatus.Queued;
        }

        private async UniTask SendMatchStateAsync(
            ISocket socket,
            string matchId,
            long opCode,
            ArraySegment<byte> payload,
            IEnumerable<IUserPresence> presences,
            CancellationToken cancellationToken,
            int operationGeneration)
        {
            byte[] rented = null;
            try
            {
                EnsureMainThread();
                rented = ArrayPool<byte>.Shared.Rent(payload.Count);
                Buffer.BlockCopy(payload.Array, payload.Offset, rented, 0, payload.Count);
                var segment = new ArraySegment<byte>(rented, 0, payload.Count);
                await socket.SendMatchStateAsync(matchId, opCode, segment, presences);
                EnsureMainThread();
                Interlocked.Add(ref _bytesSent, payload.Count);
                Interlocked.Increment(ref _packetsSent);
            }
            catch (Exception e) when (IsRecoverableException(e))
            {
                EnsureMainThread();
                if (!IsOperationCurrent(operationGeneration, cancellationToken, socket))
                    return;
                RaiseError(null, TransportError.InvalidSend, e.Message);
            }
            finally
            {
                if (rented != null)
                    ArrayPool<byte>.Shared.Return(rented, clearArray: true);
                Interlocked.Decrement(ref _pendingSends);
            }
        }

        private static string GetSendFailureReason(NetworkSendStatus status)
        {
            switch (status)
            {
                case NetworkSendStatus.Backpressure:
                    return SendCapacityError;
                case NetworkSendStatus.Unsupported:
                    return UnsupportedChannelError;
                case NetworkSendStatus.NotConnected:
                    return MissingSocketError;
                default:
                    return string.Empty;
            }
        }

        private bool TryValidateIncoming(INetConnection connection, ArraySegment<byte> frame, out NetworkEnvelopeHeader header, out ArraySegment<byte> payload)
        {
            header = default;
            payload = default;

            NetworkFrameResult frameResult = NetworkFrameCodec.TryReadPayload(frame, out header, out payload);
            if (frameResult != NetworkFrameResult.Valid)
                return false;
            if (header.Channel != NetworkChannel.Reliable)
                return false;

            int routePayloadLimit = GetMaxPayloadSize(header.MessageId, header.Channel);
            if (routePayloadLimit <= 0 || header.PayloadLength > routePayloadLimit)
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

        private NakamaNetConnection GetOrCreateAuthorityConnection(string matchId)
        {
            if (_match == null
                || !_match.Authoritative
                || string.IsNullOrEmpty(matchId)
                || !string.Equals(_match.Id, matchId, StringComparison.Ordinal))
            {
                return null;
            }

            if (_authorityConnection != null)
            {
                if (_authorityConnection.IsAuthorityRoute(matchId))
                    return _authorityConnection;

                _connectionsById.Remove(_authorityConnection.ConnectionId);
                _authorityConnection.Invalidate();
                _authorityConnection = null;
            }

            if (_connectionsById.Count >= _maxConnections || !TryAllocateConnectionId(out int connectionId))
            {
                _lastError = TransportError.Congestion;
                _lastErrorMessage = ConnectionCapacityError;
                return null;
            }

            _authorityConnection = new NakamaNetConnection(
                this,
                null,
                connectionId,
                NakamaConnectionRouteRole.Authority,
                matchId);
            _connectionsById.Add(connectionId, _authorityConnection);
            return _authorityConnection;
        }

        private NakamaNetConnection GetOrCreateConnection(IUserPresence presence)
        {
            if (presence == null)
                return null;

            string key = GetPresenceKey(presence);
            if (string.IsNullOrEmpty(key))
                return null;

            if (_connectionsBySessionId.TryGetValue(key, out NakamaNetConnection connection))
            {
                if (connection.IsValid)
                    return connection;

                _connectionsBySessionId.Remove(key);
                _connectionsById.Remove(connection.ConnectionId);
                connection.Invalidate();
            }

            if (_connectionsById.Count >= _maxConnections || !TryAllocateConnectionId(out int connectionId))
            {
                _lastError = TransportError.Congestion;
                _lastErrorMessage = ConnectionCapacityError;
                return null;
            }

            connection = new NakamaNetConnection(
                this,
                presence,
                connectionId,
                NakamaConnectionRouteRole.Peer,
                key);
            _connectionsBySessionId[key] = connection;
            _connectionsById.Add(connectionId, connection);
            return connection;
        }

        private bool TryGetConnection(IUserPresence presence, out NakamaNetConnection connection)
        {
            if (presence == null)
            {
                connection = null;
                return false;
            }

            return _connectionsBySessionId.TryGetValue(GetPresenceKey(presence), out connection)
                   && IsCurrentConnection(connection);
        }

        private bool IsCurrentConnection(NakamaNetConnection connection)
        {
            if (connection == null
                || !connection.IsValid
                || !connection.IsConnected
                || !connection.IsOwnedBy(this)
                || !_connectionsById.TryGetValue(connection.ConnectionId, out NakamaNetConnection currentById)
                || !ReferenceEquals(currentById, connection))
            {
                return false;
            }

            if (connection.RouteRole == NakamaConnectionRouteRole.Authority)
            {
                return ReferenceEquals(_authorityConnection, connection)
                       && _match != null
                       && _match.Authoritative
                       && connection.IsAuthorityRoute(_match.Id);
            }

            return !string.IsNullOrEmpty(connection.RouteKey)
                   && _connectionsBySessionId.TryGetValue(connection.RouteKey, out NakamaNetConnection currentBySession)
                   && ReferenceEquals(currentBySession, connection);
        }

        internal void SetPlayerId(NakamaNetConnection connection, ulong playerId)
        {
            EnsureMainThread();
            ThrowIfDisposed();
            if (!IsCurrentConnection(connection))
                throw new ObjectDisposedException(nameof(NakamaNetConnection));

            connection.SetPlayerIdValue(playerId);
        }

        private static string GetPresenceKey(IUserPresence presence)
        {
            if (presence == null)
                return string.Empty;

            return presence.SessionId ?? string.Empty;
        }

        private bool IsLocalPresence(IUserPresence presence)
        {
            if (presence == null || _match == null || _match.Self == null)
                return false;

            string key = GetPresenceKey(presence);
            return !string.IsNullOrEmpty(key)
                   && string.Equals(key, GetPresenceKey(_match.Self), StringComparison.Ordinal);
        }

        private bool IsCurrentMatch(string matchId)
        {
            return _match != null
                   && !string.IsNullOrEmpty(matchId)
                   && string.Equals(_match.Id, matchId, StringComparison.Ordinal);
        }

        private bool TryAllocateConnectionId(out int connectionId)
        {
            if (_nextConnectionId == int.MaxValue)
            {
                connectionId = 0;
                return false;
            }

            connectionId = checked(_nextConnectionId + 1);
            _nextConnectionId = connectionId;
            return true;
        }

        private void RaiseError(INetConnection connection, TransportError error, string message)
        {
            EnsureMainThread();
            if (_isDestroyed)
                return;

            _lastError = error;
            _lastErrorMessage = message ?? string.Empty;
            try
            {
                OnError?.Invoke(connection, error, _lastErrorMessage);
            }
            catch (Exception exception) when (IsRecoverableException(exception))
            {
                // Error observers cannot re-enter an async transport catch block and cause a
                // duplicate notification. Their failure still reaches the UniTask global hook.
                PublishObserverException(exception);
            }
        }

        private static void PublishObserverException(Exception exception)
        {
            UniTask.FromException(exception).Forget();
        }

        private void QueueLatestConnectRequest()
        {
            if (_isDestroyed || _shutdown == null || _shutdown.IsCancellationRequested)
                return;

            _connectRequestQueued = true;
            _queuedConnectSocket = _socket;
            _queuedConnectGeneration = _operationGeneration;
        }

        private void ScheduleQueuedConnect()
        {
            if (!_connectRequestQueued || _queuedConnectStartScheduled || _isDestroyed)
                return;

            CancellationToken cancellationToken = _shutdown != null
                ? _shutdown.Token
                : CancellationToken.None;
            _queuedConnectStartScheduled = true;
            StartQueuedConnectAsync(cancellationToken).Forget();
        }

        private async UniTask StartQueuedConnectAsync(CancellationToken cancellationToken)
        {
            try
            {
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                if (IsMainThread())
                {
                    _queuedConnectStartScheduled = false;
                    ScheduleQueuedConnect();
                }
                return;
            }

            EnsureMainThread();
            _queuedConnectStartScheduled = false;
            if (!_connectRequestQueued)
                return;

            ISocket expectedSocket = _queuedConnectSocket;
            int expectedGeneration = _queuedConnectGeneration;
            ClearQueuedConnectRequest();
            if (_isDestroyed
                || cancellationToken.IsCancellationRequested
                || expectedGeneration != _operationGeneration
                || !ReferenceEquals(expectedSocket, _socket))
            {
                return;
            }

            BeginConnect();
        }

        private void ClearQueuedConnectRequest()
        {
            _connectRequestQueued = false;
            _queuedConnectSocket = null;
            _queuedConnectGeneration = 0;
        }

        private CancellationToken CancelCurrentOperations(bool createReplacement)
        {
            unchecked
            {
                _operationGeneration++;
            }

            ClearQueuedConnectRequest();

            CancellationTokenSource previous = _shutdown;
            _shutdown = createReplacement ? new CancellationTokenSource() : null;
            if (previous != null)
            {
                previous.Cancel();
                previous.Dispose();
            }

            return _shutdown != null ? _shutdown.Token : CancellationToken.None;
        }

        private bool IsOperationCurrent(int operationGeneration, CancellationToken cancellationToken, ISocket socket)
        {
            return !_isDestroyed
                   && !cancellationToken.IsCancellationRequested
                   && operationGeneration == _operationGeneration
                   && ReferenceEquals(socket, _socket);
        }

        private bool IsSocketEventCurrent(ISocket socket, int socketEventGeneration)
        {
            return _socketEventsBound
                   && socketEventGeneration == _socketEventGeneration
                   && ReferenceEquals(socket, _socketEventSource)
                   && ReferenceEquals(socket, _socket);
        }

        private bool IsMainThread()
        {
            return _mainThreadId != 0 && Thread.CurrentThread.ManagedThreadId == _mainThreadId;
        }

        private static bool IsRecoverableException(Exception exception)
        {
            return exception is not OutOfMemoryException
                   && exception is not AccessViolationException;
        }

        private void ThrowIfDisposed()
        {
            if (_isDestroyed || _lifecycleState == NetworkLifecycleState.Disposed)
                throw new ObjectDisposedException(nameof(NakamaNetAdapter));
        }

        private void EnsureMainThread()
        {
            if (!IsMainThread())
            {
                throw new InvalidOperationException(
                    "NakamaNetAdapter requires the injected ISocket callbacks and task continuations to run on the Unity main owner thread after Awake. Cross-thread callbacks are rejected and are not queued.");
            }
        }

        private void ClearMatchState(bool notifyDisconnected)
        {
            int notificationGeneration = _operationGeneration;
            List<NakamaNetConnection> disconnected = notifyDisconnected && _connectionsBySessionId.Count > 0
                ? new List<NakamaNetConnection>(_connectionsBySessionId.Values)
                : null;

            foreach (NakamaNetConnection connection in _connectionsById.Values)
                connection.Invalidate();

            _match = null;
            _authorityConnection = null;
            IsMatchmaking = false;
            CurrentTicket = default;
            _connectionsBySessionId.Clear();
            _connectionsById.Clear();

            if (disconnected == null)
                return;

            for (int i = 0; i < disconnected.Count; i++)
            {
                NotifyObserver(OnClientDisconnected, (INetConnection)disconnected[i]);
                if (_isDestroyed || notificationGeneration != _operationGeneration)
                    return;
            }
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

        private byte[] GetSendBuffer() => _sendBuffer;
    }

    /// <summary>
    /// One cached wrapper per bounded Nakama authority or peer route. Invalidation releases all
    /// SDK and adapter references while preserving only stable value diagnostics.
    /// </summary>
    internal sealed class NakamaNetConnection : INetConnection
    {
        private NakamaNetAdapter _owner;
        private string _routeKey;
        private long _bytesSent;
        private long _bytesReceived;
        private bool _isConnected;
        private IUserPresence[] _targetPresences;
        private long _playerIdBits;

        internal NakamaNetConnection(
            NakamaNetAdapter owner,
            IUserPresence presence,
            int connectionId,
            NakamaConnectionRouteRole routeRole,
            string routeKey)
        {
            _owner = owner != null ? owner : throw new ArgumentNullException(nameof(owner));
            if (routeRole == NakamaConnectionRouteRole.Peer && presence == null)
                throw new ArgumentNullException(nameof(presence));
            if (routeRole == NakamaConnectionRouteRole.Authority && presence != null)
                throw new ArgumentException("An authority route cannot carry a user presence.", nameof(presence));
            if (string.IsNullOrEmpty(routeKey))
                throw new ArgumentException("A stable Nakama route key is required.", nameof(routeKey));

            Presence = presence;
            RouteRole = routeRole;
            _routeKey = routeKey;
            ConnectionId = connectionId > 0
                ? connectionId
                : throw new ArgumentOutOfRangeException(nameof(connectionId));
            RemoteAddress = routeRole == NakamaConnectionRouteRole.Authority
                ? "nakama-authority:" + routeKey
                : "nakama-peer:" + routeKey;
            _playerIdBits = unchecked((long)StableUlongHash(
                routeRole == NakamaConnectionRouteRole.Authority
                    ? "authority:" + routeKey
                    : !string.IsNullOrEmpty(presence.UserId)
                        ? presence.UserId
                        : "session:" + routeKey));
            _isConnected = true;
            _targetPresences = routeRole == NakamaConnectionRouteRole.Peer
                ? new[] { presence }
                : null;
        }

        public IUserPresence Presence { get; private set; }
        internal NakamaConnectionRouteRole RouteRole { get; }
        internal string RouteKey => _routeKey;
        internal bool IsValid { get; private set; } = true;
        internal IEnumerable<IUserPresence> TargetPresences => _targetPresences;
        public int ConnectionId { get; }
        public string RemoteAddress { get; }
        public bool IsConnected => _isConnected;
        public bool IsAuthenticated => IsValid
                                       && (RouteRole == NakamaConnectionRouteRole.Authority || Presence != null);
        public int Ping => 0;
        public ConnectionQuality Quality => ConnectionQuality.Good;
        public double Jitter => 0d;
        public long BytesSent => Interlocked.Read(ref _bytesSent);
        public long BytesReceived => Interlocked.Read(ref _bytesReceived);
        public ulong PlayerId
        {
            get => unchecked((ulong)Interlocked.Read(ref _playerIdBits));
            set => GetOwner().SetPlayerId(this, value);
        }

        internal bool IsOwnedBy(NakamaNetAdapter owner)
        {
            return ReferenceEquals(_owner, owner);
        }

        internal bool IsAuthorityRoute(string matchId)
        {
            return IsValid
                   && RouteRole == NakamaConnectionRouteRole.Authority
                   && string.Equals(_routeKey, matchId, StringComparison.Ordinal);
        }

        internal void SetPlayerIdValue(ulong playerId)
        {
            Interlocked.Exchange(ref _playerIdBits, unchecked((long)playerId));
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

        internal void Invalidate()
        {
            if (!IsValid)
                return;

            _isConnected = false;
            IsValid = false;
            Presence = null;
            _targetPresences = null;
            _routeKey = null;
            _owner = null;
        }

        private NakamaNetAdapter GetOwner()
        {
            if (_owner == null)
                throw new ObjectDisposedException(nameof(NakamaNetAdapter));

            return _owner;
        }

        public void RecordSent(int bytes)
        {
            if (!IsValid)
                return;

            Interlocked.Add(ref _bytesSent, bytes);
        }

        public void RecordReceived(int bytes)
        {
            if (!IsValid)
                return;

            Interlocked.Add(ref _bytesReceived, bytes);
        }

        private static ulong StableUlongHash(string value)
        {
            if (string.IsNullOrEmpty(value))
                return 1UL;

            const ulong offset = Fnv1a64.OffsetBasis;
            const ulong prime = Fnv1a64.Prime;

            ulong hash = offset;
            for (int i = 0; i < value.Length; i++)
                hash = (hash ^ value[i]) * prime;

            return hash == 0UL ? 1UL : hash;
        }
    }

    internal readonly struct NakamaNetworkSession : INetworkSession
    {
        private readonly string _userId;
        private readonly string _username;
        private readonly long _expireTimeUnixSeconds;

        internal NakamaNetworkSession(ISession session)
        {
            _userId = session != null ? session.UserId : string.Empty;
            _username = session != null ? session.Username : string.Empty;
            _expireTimeUnixSeconds = session != null ? session.ExpireTime : 0L;
        }

        public string UserId => _userId;
        public string Username => _username;
        public bool IsExpired => _expireTimeUnixSeconds <= DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        public long ExpireTimeUnixSeconds => _expireTimeUnixSeconds;
    }
}
#endif
