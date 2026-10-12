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
    public sealed partial class NakamaNetAdapter : MonoBehaviour, INetTransport, INetworkMessageEndpoint,
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

        // Nakama realtime sockets are reliable-only; every requested channel maps to
        // the reliable channel, which is never weaker than the request.

        public int GetChannelId(NetworkChannel channel)
        {
            if (channel < NetworkChannel.Reliable || channel > NetworkChannel.UnreliableSequenced)
                throw new ArgumentOutOfRangeException(nameof(channel), channel, "Unknown network channel.");

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
