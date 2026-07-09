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
    /// All message buffers are rented from <see cref="ArrayPool{T}.Shared"/> and returned
    /// after dispatch. No allocation occurs on the send hot path beyond the initial
    /// channel negotiation.
    /// </para>
    /// </remarks>
    public sealed class LocalLoopTransport : IPollableTransport, INetworkLifecycleProvider, INetworkFeatureProvider, IDisposable
    {
        private const string InvalidPayloadSegmentError = "Invalid payload segment.";
        private const string PayloadTooLargeError = "Payload exceeds the channel packet size limit.";
        private const string ChannelNotReadyError = "Local loop channel is not ready. Ensure both server and client have started.";
        private const string UnsupportedBuildError = "LocalLoopTransport is only available in the Unity Editor or Development builds.";
        private const string DefaultChannelName = "default";

        private LocalLoopChannel _channel;
        private string _channelName;
        private LocalLoopConnection _localConnection;
        private bool _isServer;
        private bool _isRunning;
        private volatile bool _disposed;
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
            | NetworkTransportFeatureFlags.Host
            | NetworkTransportFeatureFlags.Reliable
            | NetworkTransportFeatureFlags.Unreliable
            | NetworkTransportFeatureFlags.Sequenced
            | NetworkTransportFeatureFlags.DedicatedServerCompatible,
            NetworkChannelFlags.All,
            maxConnections: 1,
            maxPacketSize: NetworkConstants.DefaultMTU,
            maxReliablePacketSize: NetworkConstants.DefaultMTU,
            maxUnreliablePacketSize: NetworkConstants.DefaultMTU,
            maxQueuedPackets: 0,
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
        public int GetMaxPacketSize(int channelId) => NetworkConstants.DefaultMTU;
        public NetworkStatistics GetStatistics()
        {
            return new NetworkStatistics(
                bytesSent: _localConnection?.BytesSent ?? 0,
                bytesReceived: _localConnection?.BytesReceived ?? 0,
                packetsSent: _localConnection?.PacketsSent ?? 0,
                packetsReceived: _localConnection?.PacketsReceived ?? 0,
                connectionCount: _isRunning ? 1 : 0,
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

            _channel = LocalLoopRegistry.GetOrCreateChannel(DefaultChannelName);
            _channelName = DefaultChannelName;
            _channel.RegisterServer();
            _localConnection = new LocalLoopConnection(1, "loopback:server");
            _isServer = true;
            _isRunning = true;
            _clientConnectedFired = false;
            _connectedToServerFired = false;
        }

        public void StartClient(string address)
        {
            ThrowIfDisposed();
            ThrowIfUnsupportedBuild();
            if (_isRunning) return;

            string channelName = string.IsNullOrEmpty(address) ? DefaultChannelName : address;
            _channel = LocalLoopRegistry.GetOrCreateChannel(channelName);
            _channelName = channelName;
            _channel.RegisterClient();
            _localConnection = new LocalLoopConnection(0, "loopback:client");
            _isServer = false;
            _isRunning = true;
            _clientConnectedFired = false;
            _connectedToServerFired = false;
        }

        public void Stop()
        {
            if (!_isRunning) return;

            if (_isServer)
            {
                if (_clientConnectedFired)
                {
                    OnClientDisconnected?.Invoke(_localConnection);
                }
                _channel.UnregisterServer();
            }
            else
            {
                if (_connectedToServerFired)
                {
                    OnDisconnectedFromServer?.Invoke();
                }
                _channel.UnregisterClient();
            }

            LocalLoopRegistry.ReleaseChannel(_channelName, _channel);
            _channel = null;
            _channelName = null;
            _localConnection = null;
            _isRunning = false;
            _clientConnectedFired = false;
            _connectedToServerFired = false;
        }

        public void Disconnect(INetConnection connection)
        {
            Stop();
        }

        #endregion

        #region Send

        public NetworkSendResult Send(INetConnection connection, in ArraySegment<byte> payload, int channelId)
        {
            if (!_isRunning)
                return NetworkSendResult.Fail(NetworkSendStatus.NotRunning, channelId, connection);

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

            if (!_channel.IsReady)
            {
                RaiseError(connection, TransportError.InvalidSend, ChannelNotReadyError);
                return NetworkSendResult.Fail(NetworkSendStatus.NotConnected, channelId, connection, ChannelNotReadyError);
            }

            if (connection == null)
            {
                connection = _localConnection;
            }

            byte[] buffer = ArrayPool<byte>.Shared.Rent(length);
            Buffer.BlockCopy(payload.Array, payload.Offset, buffer, 0, length);

            var msg = new LocalLoopMessage(buffer, length, channelId);
            _channel.Enqueue(_isServer, msg);
            _localConnection.RecordSend(length);
            return NetworkSendResult.Accepted(length, channelId, connection);
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
            if (!_isRunning || _channel == null) return;

            if (_isServer)
            {
                if (!_clientConnectedFired && _channel.IsReady)
                {
                    _clientConnectedFired = true;
                    OnClientConnected?.Invoke(_localConnection);
                }
                else if (_clientConnectedFired && !_channel.IsReady)
                {
                    _clientConnectedFired = false;
                    OnClientDisconnected?.Invoke(_localConnection);
                }
            }
            else
            {
                if (!_connectedToServerFired && _channel.IsReady)
                {
                    _connectedToServerFired = true;
                    OnConnectedToServer?.Invoke();
                }
                else if (_connectedToServerFired && !_channel.IsReady)
                {
                    _connectedToServerFired = false;
                    OnDisconnectedFromServer?.Invoke();
                }
            }

            while (_channel.TryDequeue(_isServer, out LocalLoopMessage msg))
            {
                _localConnection.RecordReceive(msg.Length);
                try
                {
                    OnDataReceived?.Invoke(_localConnection,
                        new ArraySegment<byte>(msg.Buffer, 0, msg.Length),
                        msg.ChannelId);
                }
                finally
                {
                    ReturnMessageBuffer(msg);
                }
            }
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

        public LocalLoopConnection(int connectionId, string remoteAddress)
        {
            _connectionId = connectionId;
            _remoteAddress = remoteAddress;
        }

        public int ConnectionId => _connectionId;
        public string RemoteAddress => _remoteAddress;
        public bool IsConnected => true;
        public bool IsAuthenticated => true;
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
    /// Each direction uses a lock-free <see cref="ConcurrentQueue{T}"/> for zero-contention
    /// producer-consumer message passing.
    /// </summary>
    internal class LocalLoopChannel
    {
        private readonly ConcurrentQueue<LocalLoopMessage> _serverToClient = new();
        private readonly ConcurrentQueue<LocalLoopMessage> _clientToServer = new();

        private volatile int _serverRegistered;
        private volatile int _clientRegistered;

        public bool IsReady => _serverRegistered == 1 && _clientRegistered == 1;
        public bool HasRegistrations => _serverRegistered != 0 || _clientRegistered != 0;

        internal void RegisterServer() => Interlocked.Exchange(ref _serverRegistered, 1);
        internal void UnregisterServer() => Interlocked.Exchange(ref _serverRegistered, 0);
        internal void RegisterClient() => Interlocked.Exchange(ref _clientRegistered, 1);
        internal void UnregisterClient() => Interlocked.Exchange(ref _clientRegistered, 0);

        /// <summary>
        /// Enqueue a message on behalf of server (isServer=true → clientToServer queue)
        /// or client (isServer=false → serverToClient queue).
        /// </summary>
        internal void Enqueue(bool fromServer, in LocalLoopMessage msg)
        {
            if (fromServer)
                _serverToClient.Enqueue(msg);
            else
                _clientToServer.Enqueue(msg);
        }

        /// <summary>
        /// Dequeue a message destined for server (fromServer=false → from clientToServer)
        /// or client (fromServer=true → from serverToClient).
        /// </summary>
        internal bool TryDequeue(bool forServer, out LocalLoopMessage msg)
        {
            return forServer
                ? _clientToServer.TryDequeue(out msg)
                : _serverToClient.TryDequeue(out msg);
        }

        internal void Drain()
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
    }

    /// <summary>
    /// Global registry of named <see cref="LocalLoopChannel"/> instances.
    /// Enables server and client transports to discover each other by channel name.
    /// </summary>
    internal static class LocalLoopRegistry
    {
        private static readonly ConcurrentDictionary<string, LocalLoopChannel> s_channels = new();

        internal static LocalLoopChannel GetOrCreateChannel(string name)
        {
            return s_channels.GetOrAdd(name, _ => new LocalLoopChannel());
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
