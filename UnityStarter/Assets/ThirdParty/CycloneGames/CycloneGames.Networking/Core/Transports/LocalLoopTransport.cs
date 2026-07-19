using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace CycloneGames.Networking.Transports
{
    /// <summary>
    /// In-process <see cref="INetTransport"/> that routes messages through shared memory queues.
    /// Enables single-process client+server debugging without any network I/O.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two <see cref="LocalLoopTransport"/> instances—one acting as server, one as client—are
    /// paired through a named <see cref="LocalLoopChannel"/>. The server creates the channel
    /// on <see cref="INetTransport.StartServer"/>, and the client finds it by name on
    /// <see cref="INetTransport.StartClient"/>.
    /// </para>
    /// <para>
    /// This transport implements <see cref="IPollableTransport"/> and requires
    /// <see cref="IPollableTransport.PollEvents"/> to be called once per frame to drain
    /// inbound message queues and fire connection events.
    /// </para>
    /// <para>
    /// Message buffers are rented from <see cref="ArrayPool{T}.Shared"/> and returned
    /// after dispatch. Pool misses and <see cref="ConcurrentQueue{T}"/> growth can still
    /// allocate; no zero-allocation guarantee is made without a target benchmark.
    /// </para>
    /// </remarks>
    public sealed class LocalLoopTransport : IPollableTransport, INetworkLifecycleProvider, INetworkFeatureProvider, IDisposable
    {
        private const string InvalidPayloadSegmentError = "Invalid payload segment.";
        private const string PayloadTooLargeError = "Payload exceeds the channel packet size limit.";
        private const string ChannelNotReadyError = "Local loop channel is not ready. Ensure both server and client have started.";
        private const string QueueFullError = "Local loop queue capacity is exhausted.";
        private const string StaleConnectionError = "The connection does not belong to the current local loop peer session.";
        private const string UnsupportedChannelError = "Local loop transport only advertises the reliable channel.";
        private const string UnsupportedBuildError = "LocalLoopTransport is only available in the Unity Editor or Development builds.";
        private const string DefaultChannelName = "default";
        private const int DefaultMaxQueuedPackets = 1024;
        private const int DefaultMaxQueuedBytes = 4 * 1024 * 1024;
        private const int DefaultMaxEventsPerPoll = 256;

        private static long s_nextConnectionId;

        private LocalLoopChannel _channel;
        private string _channelName;
        private LocalLoopConnection _localConnection;
        private bool _isServer;
        private volatile bool _isRunning;
        private volatile bool _disposed;
        private int _sessionGeneration;
        private int _observedPeerEpoch;
        private TransportError _lastError;
        private string _lastErrorMessage = string.Empty;

        // Server-side: track whether OnClientConnected has been fired for the current session.
        private bool _clientConnectedFired;
        private bool _connectedToServerFired;

        #region INetTransport properties

        public bool IsServer => _isServer && _isRunning;
        public bool IsClient => !_isServer && _isRunning;
        public bool IsRunning => _isRunning;
        public bool IsEncrypted => false;
        public bool Available => IsBuildSupported;
        public NetworkBackendFeatures Features => NetworkBackendFeatures.RealtimeTransport;
        public NetworkTransportCapabilities Capabilities { get; } = new NetworkTransportCapabilities(
            "LocalLoop",
            NetworkTransportFeatureFlags.Client
            | NetworkTransportFeatureFlags.Server
            | NetworkTransportFeatureFlags.Reliable
            | NetworkTransportFeatureFlags.Backpressure
            | NetworkTransportFeatureFlags.MainThreadOnly,
            NetworkChannelFlags.Reliable,
            maxConnections: 1,
            maxPacketSize: NetworkConstants.DefaultMTU,
            maxReliablePacketSize: NetworkConstants.DefaultMTU,
            maxUnreliablePacketSize: 0,
            maxQueuedPackets: DefaultMaxQueuedPackets,
            isDeterministicLoopback: true);

        #endregion

        #region Events

        public event Action<INetConnection> OnClientConnected;
        public event Action<INetConnection> OnClientDisconnected;
        public event Action OnConnectedToServer;
        public event Action OnDisconnectedFromServer;
        public event Action<INetConnection, TransportError, string> OnError;
        public event Action<INetConnection, ArraySegment<byte>, int> OnDataReceived;

        #endregion

        #region Channel & QoS

        public int GetChannelId(NetworkChannel channel) => (int)channel;
        public int GetMaxPacketSize(int channelId) =>
            channelId == GetChannelId(NetworkChannel.Reliable) ? NetworkConstants.DefaultMTU : 0;
        public NetworkStatistics GetStatistics()
        {
            return new NetworkStatistics(
                bytesSent: _localConnection?.BytesSent ?? 0,
                bytesReceived: _localConnection?.BytesReceived ?? 0,
                packetsSent: _localConnection?.PacketsSent ?? 0,
                packetsReceived: _localConnection?.PacketsReceived ?? 0,
                connectionCount: _localConnection != null && _localConnection.IsConnected ? 1 : 0,
                averageRttMs: 0f);
        }

        public NetworkLifecycleSnapshot GetLifecycleSnapshot()
        {
            NetworkLifecycleState state = _disposed
                ? NetworkLifecycleState.Disposed
                : NetworkLifecycle.GetTransportState(this);

            return new NetworkLifecycleSnapshot(
                state,
                Features,
                _lastError,
                _lastErrorMessage,
                Available,
                IsRunning,
                IsServer,
                IsClient,
                IsEncrypted);
        }

        #endregion

        #region Lifecycle

        public void StartServer()
        {
            ThrowIfDisposed();
            ThrowIfUnsupportedBuild();
            if (_isRunning) return;

            _channel = LocalLoopRegistry.GetOrCreateChannel(DefaultChannelName, DefaultMaxQueuedPackets, DefaultMaxQueuedBytes);
            _channelName = DefaultChannelName;
            _channel.RegisterServer();
            _localConnection = CreateRemoteConnection(isServer: true);
            _isServer = true;
            _isRunning = true;
            unchecked { _sessionGeneration++; }
            _observedPeerEpoch = _channel.ClientEpoch;
            _clientConnectedFired = false;
            _connectedToServerFired = false;
        }

        public void StartClient(string address)
        {
            ThrowIfDisposed();
            ThrowIfUnsupportedBuild();
            if (_isRunning) return;

            string channelName = string.IsNullOrEmpty(address) ? DefaultChannelName : address;
            if (!string.Equals(channelName, DefaultChannelName, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "LocalLoopTransport currently supports only the default in-process channel.",
                    nameof(address));
            }

            _channel = LocalLoopRegistry.GetOrCreateChannel(channelName, DefaultMaxQueuedPackets, DefaultMaxQueuedBytes);
            _channelName = channelName;
            _channel.RegisterClient();
            _localConnection = CreateRemoteConnection(isServer: false);
            _isServer = false;
            _isRunning = true;
            unchecked { _sessionGeneration++; }
            _observedPeerEpoch = _channel.ServerEpoch;
            _clientConnectedFired = false;
            _connectedToServerFired = false;
        }

        public void Stop()
        {
            if (!_isRunning) return;

            LocalLoopConnection connection = _localConnection;
            LocalLoopChannel channel = _channel;
            string channelName = _channelName;
            bool notifyClientDisconnected = _isServer && _clientConnectedFired;
            bool notifyServerDisconnected = !_isServer && _connectedToServerFired;

            if (_isServer)
            {
                channel.UnregisterServer();
            }
            else
            {
                channel.UnregisterClient();
            }

            connection.SetConnected(false);

            LocalLoopRegistry.ReleaseChannel(channelName, channel);
            _channel = null;
            _channelName = null;
            _localConnection = null;
            _isRunning = false;
            unchecked { _sessionGeneration++; }
            _clientConnectedFired = false;
            _connectedToServerFired = false;

            // Invoke user callbacks only after ownership and pooled buffers have been released.
            if (notifyClientDisconnected)
                OnClientDisconnected?.Invoke(connection);
            if (notifyServerDisconnected)
                OnDisconnectedFromServer?.Invoke();
        }

        public void Disconnect(INetConnection connection)
        {
            if (!IsCurrentConnectionArgument(connection))
            {
                return;
            }

            Stop();
        }

        #endregion

        #region Send

        public NetworkSendResult Send(INetConnection connection, in ArraySegment<byte> payload, int channelId)
        {
            if (!_isRunning)
                return NetworkSendResult.Fail(NetworkSendStatus.NotRunning, channelId, connection);
            if (channelId != GetChannelId(NetworkChannel.Reliable))
                return NetworkSendResult.Fail(NetworkSendStatus.Unsupported, channelId, connection, UnsupportedChannelError);

            LocalLoopConnection currentConnection = _localConnection;
            if (!IsCurrentConnectionArgument(connection))
            {
                RaiseError(connection, TransportError.ConnectionClosed, StaleConnectionError);
                return NetworkSendResult.Fail(NetworkSendStatus.NotConnected, channelId, connection, StaleConnectionError);
            }

            connection = currentConnection;

            int length = payload.Count;
            if (length == 0)
                return NetworkSendResult.Accepted(0, channelId, connection);

            if (payload.Array == null || payload.Offset < 0 || payload.Offset + length > payload.Array.Length)
            {
                RaiseError(connection, TransportError.InvalidSend, InvalidPayloadSegmentError);
                return NetworkSendResult.Fail(NetworkSendStatus.InvalidPayload, channelId, connection, InvalidPayloadSegmentError);
            }

            if (length > GetMaxPacketSize(channelId))
            {
                RaiseError(connection, TransportError.InvalidSend, PayloadTooLargeError);
                return NetworkSendResult.Fail(NetworkSendStatus.PayloadTooLarge, channelId, connection, PayloadTooLargeError);
            }

            LocalLoopChannel channel = _channel;
            int peerEpoch = _isServer ? channel?.ClientEpoch ?? 0 : channel?.ServerEpoch ?? 0;
            if (channel == null || !channel.IsReady || peerEpoch != _observedPeerEpoch)
            {
                RaiseError(connection, TransportError.InvalidSend, ChannelNotReadyError);
                return NetworkSendResult.Fail(NetworkSendStatus.NotConnected, channelId, connection, ChannelNotReadyError);
            }

            byte[] buffer = ArrayPool<byte>.Shared.Rent(length);
            Buffer.BlockCopy(payload.Array, payload.Offset, buffer, 0, length);

            var msg = new LocalLoopMessage(buffer, length, channelId);
            if (!channel.TryEnqueue(_isServer, msg))
            {
                ReturnMessageBuffer(msg);
                RaiseError(connection, TransportError.Congestion, QueueFullError);
                return NetworkSendResult.Fail(NetworkSendStatus.Backpressure, channelId, connection, QueueFullError);
            }

            _localConnection.RecordSend(length);
            return NetworkSendResult.Queued(length, channelId, connection);
        }

        public NetworkSendResult Broadcast(IReadOnlyList<INetConnection> connections, in ArraySegment<byte> payload, int channelId)
        {
            if (connections == null)
                throw new ArgumentNullException(nameof(connections));

            if (!_isRunning)
                return NetworkSendResult.Fail(NetworkSendStatus.NotRunning, channelId);

            if (connections.Count == 0)
                return NetworkSendResult.Broadcast(NetworkSendStatus.Accepted, 0, 0, channelId);

            // In local loop, there is only ever one peer connection.
            return Send(connections[0], payload, channelId);
        }

        #endregion

        #region PollEvents (IPollableTransport)

        public void PollEvents()
        {
            LocalLoopChannel channel = _channel;
            LocalLoopConnection connection = _localConnection;
            bool isServer = _isServer;
            int generation = _sessionGeneration;
            if (!_isRunning || channel == null || connection == null) return;

            if (isServer)
            {
                int peerEpoch = channel.ClientEpoch;
                if (_clientConnectedFired && channel.IsReady && peerEpoch != _observedPeerEpoch)
                {
                    _clientConnectedFired = false;
                    connection.SetConnected(false);
                    OnClientDisconnected?.Invoke(connection);
                    if (!IsCurrentSession(generation, channel, connection)) return;
                    if (!channel.IsReady || channel.ClientEpoch != peerEpoch) return;

                    connection = ReplaceRemoteConnection(isServer: true, peerEpoch);
                    _clientConnectedFired = true;
                    connection.SetConnected(true);
                    OnClientConnected?.Invoke(connection);
                    if (!IsCurrentSession(generation, channel, connection)) return;
                    if (!channel.IsReady || channel.ClientEpoch != peerEpoch) return;
                }
                else if (!_clientConnectedFired && channel.IsReady)
                {
                    if (_observedPeerEpoch != 0 && peerEpoch != _observedPeerEpoch)
                        connection = ReplaceRemoteConnection(isServer: true, peerEpoch);
                    else
                        _observedPeerEpoch = peerEpoch;

                    _clientConnectedFired = true;
                    connection.SetConnected(true);
                    OnClientConnected?.Invoke(connection);
                    if (!IsCurrentSession(generation, channel, connection)) return;
                    if (!channel.IsReady || channel.ClientEpoch != peerEpoch) return;
                }
                else if (_clientConnectedFired && !channel.IsReady)
                {
                    _clientConnectedFired = false;
                    connection.SetConnected(false);
                    OnClientDisconnected?.Invoke(connection);
                    return;
                }
            }
            else
            {
                int peerEpoch = channel.ServerEpoch;
                if (_connectedToServerFired && channel.IsReady && peerEpoch != _observedPeerEpoch)
                {
                    _connectedToServerFired = false;
                    connection.SetConnected(false);
                    OnDisconnectedFromServer?.Invoke();
                    if (!IsCurrentSession(generation, channel, connection)) return;
                    if (!channel.IsReady || channel.ServerEpoch != peerEpoch) return;

                    connection = ReplaceRemoteConnection(isServer: false, peerEpoch);
                    _connectedToServerFired = true;
                    connection.SetConnected(true);
                    OnConnectedToServer?.Invoke();
                    if (!IsCurrentSession(generation, channel, connection)) return;
                    if (!channel.IsReady || channel.ServerEpoch != peerEpoch) return;
                }
                else if (!_connectedToServerFired && channel.IsReady)
                {
                    if (_observedPeerEpoch != 0 && peerEpoch != _observedPeerEpoch)
                        connection = ReplaceRemoteConnection(isServer: false, peerEpoch);
                    else
                        _observedPeerEpoch = peerEpoch;

                    _connectedToServerFired = true;
                    connection.SetConnected(true);
                    OnConnectedToServer?.Invoke();
                    if (!IsCurrentSession(generation, channel, connection)) return;
                    if (!channel.IsReady || channel.ServerEpoch != peerEpoch) return;
                }
                else if (_connectedToServerFired && !channel.IsReady)
                {
                    _connectedToServerFired = false;
                    connection.SetConnected(false);
                    OnDisconnectedFromServer?.Invoke();
                    return;
                }
            }

            int dispatched = 0;
            while (dispatched < DefaultMaxEventsPerPoll
                   && IsCurrentSession(generation, channel, connection)
                   && channel.TryDequeue(isServer, out LocalLoopMessage msg))
            {
                dispatched++;
                connection.RecordReceive(msg.Length);
                try
                {
                    OnDataReceived?.Invoke(connection,
                        new ArraySegment<byte>(msg.Buffer, 0, msg.Length),
                        msg.ChannelId);
                }
                finally
                {
                    ReturnMessageBuffer(msg);
                }
            }
        }

        private bool IsCurrentSession(
            int generation,
            LocalLoopChannel channel,
            LocalLoopConnection connection)
        {
            return _isRunning
                && _sessionGeneration == generation
                && ReferenceEquals(_channel, channel)
                && ReferenceEquals(_localConnection, connection);
        }

        private bool IsCurrentConnectionArgument(INetConnection connection)
        {
            if (connection == null || ReferenceEquals(connection, _localConnection))
            {
                return true;
            }

            // The common transport contract supplies the server-side view of the single
            // local peer when the client sends. Accept that live paired view, but never an
            // arbitrary INetConnection or a disconnected view from an earlier peer epoch.
            return connection is LocalLoopConnection localLoopConnection
                   && localLoopConnection.IsConnected;
        }

        private LocalLoopConnection ReplaceRemoteConnection(bool isServer, int peerEpoch)
        {
            LocalLoopConnection replacement = CreateRemoteConnection(isServer);
            _localConnection = replacement;
            _observedPeerEpoch = peerEpoch;
            return replacement;
        }

        private static LocalLoopConnection CreateRemoteConnection(bool isServer)
        {
            long connectionId = Interlocked.Increment(ref s_nextConnectionId);
            if (connectionId <= 0L || connectionId > int.MaxValue)
            {
                throw new InvalidOperationException("Local loop connection identity space is exhausted.");
            }

            return new LocalLoopConnection(
                (int)connectionId,
                isServer ? "loopback:client" : "loopback:server");
        }

        #endregion

        #region Dispose

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_isRunning)
            {
                Stop();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(LocalLoopTransport));
        }

        private static bool IsBuildSupported
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                return true;
#else
                return false;
#endif
            }
        }

        private static void ThrowIfUnsupportedBuild()
        {
            if (!IsBuildSupported)
            {
                throw new InvalidOperationException(UnsupportedBuildError);
            }
        }

        private void RaiseError(INetConnection connection, TransportError error, string message)
        {
            _lastError = error;
            _lastErrorMessage = message ?? string.Empty;
            OnError?.Invoke(connection, error, _lastErrorMessage);
        }

        #endregion

        #region Buffer management

        private static void ReturnMessageBuffer(in LocalLoopMessage msg)
        {
            ArrayPool<byte>.Shared.Return(msg.Buffer);
        }

        #endregion
    }

    /// <summary>
    /// Lightweight <see cref="INetConnection"/> implementation for local loopback transport.
    /// Represents the single peer connection in an in-process client-server pair.
    /// </summary>
    internal sealed class LocalLoopConnection : INetConnection
    {
        private readonly int _connectionId;
        private readonly string _remoteAddress;

        private long _bytesSent;
        private long _bytesReceived;
        private int _packetsSent;
        private int _packetsReceived;
        private int _isConnected;

        public LocalLoopConnection(int connectionId, string remoteAddress)
        {
            _connectionId = connectionId;
            _remoteAddress = remoteAddress;
        }

        public int ConnectionId => _connectionId;
        public string RemoteAddress => _remoteAddress;
        public bool IsConnected => Volatile.Read(ref _isConnected) != 0;
        public bool IsAuthenticated => IsConnected;
        public int Ping => 1;
        public ConnectionQuality Quality => ConnectionQuality.Excellent;
        public double Jitter => 0;
        public long BytesSent => Interlocked.Read(ref _bytesSent);
        public long BytesReceived => Interlocked.Read(ref _bytesReceived);
        public int PacketsSent => Volatile.Read(ref _packetsSent);
        public int PacketsReceived => Volatile.Read(ref _packetsReceived);
        public ulong PlayerId { get; set; }

        internal void RecordSend(int byteCount)
        {
            Interlocked.Add(ref _bytesSent, byteCount);
            Interlocked.Increment(ref _packetsSent);
        }

        internal void RecordReceive(int byteCount)
        {
            Interlocked.Add(ref _bytesReceived, byteCount);
            Interlocked.Increment(ref _packetsReceived);
        }

        internal void SetConnected(bool connected)
        {
            Volatile.Write(ref _isConnected, connected ? 1 : 0);
        }

        public bool Equals(INetConnection other)
        {
            if (other is LocalLoopConnection llc)
                return _connectionId == llc._connectionId && _remoteAddress == llc._remoteAddress;
            return false;
        }

        public override bool Equals(object obj) => obj is INetConnection c && Equals(c);
        public override int GetHashCode() => _connectionId ^ _remoteAddress.GetHashCode();
        public override string ToString() => $"LocalLoop[{_connectionId}]@{_remoteAddress}";
    }

    /// <summary>
    /// Shared message container for in-process message passing.
    /// </summary>
    internal readonly struct LocalLoopMessage
    {
        public readonly byte[] Buffer;
        public readonly int Length;
        public readonly int ChannelId;

        public LocalLoopMessage(byte[] buffer, int length, int channelId)
        {
            Buffer = buffer;
            Length = length;
            ChannelId = channelId;
        }
    }

    /// <summary>
    /// Bidirectional message channel connecting a local server and client transport pair.
    /// Each direction uses a bounded concurrent queue. Registration changes and enqueue
    /// admission are serialized so shutdown cannot orphan pooled buffers.
    /// </summary>
    internal class LocalLoopChannel
    {
        private readonly object _registrationLock = new object();
        private readonly BoundedLocalLoopQueue _serverToClient;
        private readonly BoundedLocalLoopQueue _clientToServer;

        private int _serverRegistered;
        private int _clientRegistered;

        public bool IsReady => Volatile.Read(ref _serverRegistered) == 1
                               && Volatile.Read(ref _clientRegistered) == 1;
        public bool HasRegistrations => Volatile.Read(ref _serverRegistered) != 0
                                        || Volatile.Read(ref _clientRegistered) != 0;
        internal int ServerEpoch => Volatile.Read(ref _serverEpoch);
        internal int ClientEpoch => Volatile.Read(ref _clientEpoch);

        private int _serverEpoch;
        private int _clientEpoch;

        internal LocalLoopChannel(int maxPackets, int maxBytes)
        {
            _serverToClient = new BoundedLocalLoopQueue(maxPackets, maxBytes);
            _clientToServer = new BoundedLocalLoopQueue(maxPackets, maxBytes);
        }

        internal bool MatchesCapacity(int maxPackets, int maxBytes)
        {
            return _serverToClient.MaxPackets == maxPackets && _serverToClient.MaxBytes == maxBytes;
        }

        internal void RegisterServer()
        {
            lock (_registrationLock)
            {
                if (_serverRegistered != 0)
                    throw new InvalidOperationException("A local loop server is already registered for this channel.");

                _serverEpoch = NextEpoch(_serverEpoch);
                Volatile.Write(ref _serverRegistered, 1);
            }
        }

        internal void UnregisterServer()
        {
            lock (_registrationLock)
            {
                Volatile.Write(ref _serverRegistered, 0);
                DrainQueues();
            }
        }

        internal void RegisterClient()
        {
            lock (_registrationLock)
            {
                if (_clientRegistered != 0)
                    throw new InvalidOperationException("A local loop client is already registered for this channel.");

                _clientEpoch = NextEpoch(_clientEpoch);
                Volatile.Write(ref _clientRegistered, 1);
            }
        }

        internal void UnregisterClient()
        {
            lock (_registrationLock)
            {
                Volatile.Write(ref _clientRegistered, 0);
                DrainQueues();
            }
        }

        /// <summary>
        /// Enqueue a message on behalf of the server (fromServer=true, server-to-client queue)
        /// or client (fromServer=false, client-to-server queue).
        /// </summary>
        internal bool TryEnqueue(bool fromServer, in LocalLoopMessage msg)
        {
            lock (_registrationLock)
            {
                if (_serverRegistered == 0 || _clientRegistered == 0)
                    return false;

                return fromServer
                    ? _serverToClient.TryEnqueue(msg)
                    : _clientToServer.TryEnqueue(msg);
            }
        }

        /// <summary>
        /// Dequeue a message destined for the server (forServer=true, client-to-server queue)
        /// or client (forServer=false, server-to-client queue).
        /// </summary>
        internal bool TryDequeue(bool forServer, out LocalLoopMessage msg)
        {
            return forServer
                ? _clientToServer.TryDequeue(out msg)
                : _serverToClient.TryDequeue(out msg);
        }

        internal void Drain()
        {
            lock (_registrationLock)
            {
                DrainQueues();
            }
        }

        private void DrainQueues()
        {
            while (_serverToClient.TryDequeue(out LocalLoopMessage serverMessage))
            {
                ReturnMessageBuffer(serverMessage);
            }

            while (_clientToServer.TryDequeue(out LocalLoopMessage clientMessage))
            {
                ReturnMessageBuffer(clientMessage);
            }
        }

        private static void ReturnMessageBuffer(in LocalLoopMessage msg)
        {
            if (msg.Buffer != null)
            {
                ArrayPool<byte>.Shared.Return(msg.Buffer);
            }
        }

        private static int NextEpoch(int current)
        {
            int next = unchecked(current + 1);
            return next > 0 ? next : 1;
        }
    }

    internal sealed class BoundedLocalLoopQueue
    {
        private readonly ConcurrentQueue<LocalLoopMessage> _queue = new ConcurrentQueue<LocalLoopMessage>();
        private int _packetCount;
        private long _byteCount;

        internal BoundedLocalLoopQueue(int maxPackets, int maxBytes)
        {
            if (maxPackets <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxPackets));
            if (maxBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxBytes));

            MaxPackets = maxPackets;
            MaxBytes = maxBytes;
        }

        internal int MaxPackets { get; }
        internal int MaxBytes { get; }

        internal bool TryEnqueue(in LocalLoopMessage message)
        {
            if (Interlocked.Increment(ref _packetCount) > MaxPackets)
            {
                Interlocked.Decrement(ref _packetCount);
                return false;
            }

            if (Interlocked.Add(ref _byteCount, message.Length) > MaxBytes)
            {
                Interlocked.Add(ref _byteCount, -message.Length);
                Interlocked.Decrement(ref _packetCount);
                return false;
            }

            _queue.Enqueue(message);
            return true;
        }

        internal bool TryDequeue(out LocalLoopMessage message)
        {
            if (!_queue.TryDequeue(out message))
                return false;

            Interlocked.Add(ref _byteCount, -message.Length);
            Interlocked.Decrement(ref _packetCount);
            return true;
        }
    }

    /// <summary>
    /// Global registry of named <see cref="LocalLoopChannel"/> instances.
    /// Enables server and client transports to discover each other by channel name.
    /// </summary>
    internal static class LocalLoopRegistry
    {
        private static readonly ConcurrentDictionary<string, LocalLoopChannel> s_channels = new();

        internal static LocalLoopChannel GetOrCreateChannel(string name, int maxPackets, int maxBytes)
        {
            LocalLoopChannel channel = s_channels.GetOrAdd(name, _ => new LocalLoopChannel(maxPackets, maxBytes));
            if (!channel.MatchesCapacity(maxPackets, maxBytes))
                throw new InvalidOperationException("Local loop peers must use identical queue capacities.");
            return channel;
        }

        internal static bool TryRemoveChannel(string name)
        {
            if (s_channels.TryRemove(name, out LocalLoopChannel channel))
            {
                channel.Drain();
                return true;
            }

            return false;
        }

        internal static void ReleaseChannel(string name, LocalLoopChannel channel)
        {
            if (string.IsNullOrEmpty(name) || channel == null || channel.HasRegistrations)
            {
                return;
            }

            if (s_channels.TryGetValue(name, out LocalLoopChannel current) && ReferenceEquals(current, channel))
            {
                if (s_channels.TryRemove(name, out LocalLoopChannel removed))
                {
                    removed.Drain();
                }
            }
        }
    }
}
