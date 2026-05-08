using System;
using System.Collections.Generic;
using System.Threading;

namespace CycloneGames.Networking.Transports
{
    /// <summary>
    /// Decorator that wraps any <see cref="INetTransport"/> and tracks per-second
    /// bandwidth and message rate via a simple rolling-window counter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The meter uses two alternating buckets, each covering a 500ms window.
    /// Every 500ms the older bucket is reset and becomes the active write bucket.
    /// The reported value is the sum of both buckets (covering ~1 second),
    /// providing a stable reading that updates twice per second.
    /// </para>
    /// <para>
    /// Bucket rotation is driven by <c>PollEvents()</c> timestamps, so the meter
    /// self-clocks without requiring an external timer or thread.
    /// </para>
    /// <para>
    /// Thread Safety: Send/Receive counters use <see cref="Interlocked"/> for
    /// atomic updates. Bucket rotation occurs on PollEvents which should be
    /// called from a single thread.
    /// </para>
    /// </remarks>
    public sealed class BandwidthTrackingTransport : IPollableTransport, IDisposable
    {
        private readonly INetTransport _inner;
        private readonly RollingWindowBandwidthMeter _meter;

        public BandwidthTrackingTransport(INetTransport inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _meter = new RollingWindowBandwidthMeter();

            _inner.OnClientConnected += conn => OnClientConnected?.Invoke(conn);
            _inner.OnClientDisconnected += conn => OnClientDisconnected?.Invoke(conn);
            _inner.OnConnectedToServer += () => OnConnectedToServer?.Invoke();
            _inner.OnDisconnectedFromServer += () => OnDisconnectedFromServer?.Invoke();
            _inner.OnError += (conn, err, msg) => OnError?.Invoke(conn, err, msg);
        }

        /// <summary>
        /// Read-only bandwidth telemetry. Write-side methods (RecordSend, PollWindow)
        /// are on the internal <see cref="RollingWindowBandwidthMeter"/> and not
        /// exposed through the <see cref="IBandwidthMeter"/> interface.
        /// </summary>
        public IBandwidthMeter Meter => _meter;

        #region INetTransport delegation

        public bool IsServer => _inner.IsServer;
        public bool IsClient => _inner.IsClient;
        public bool IsRunning => _inner.IsRunning;
        public bool IsEncrypted => _inner.IsEncrypted;
        public bool Available => _inner.Available;

        public int GetChannelId(NetworkChannel channel) => _inner.GetChannelId(channel);
        public int GetMaxPacketSize(int channelId) => _inner.GetMaxPacketSize(channelId);
        public NetworkStatistics GetStatistics() => _inner.GetStatistics();

        public event Action<INetConnection> OnClientConnected;
        public event Action<INetConnection> OnClientDisconnected;
        public event Action OnConnectedToServer;
        public event Action OnDisconnectedFromServer;
        public event Action<INetConnection, TransportError, string> OnError;

        public void StartServer()
        {
            _meter.Reset();
            _inner.StartServer();
        }

        public void StartClient(string address)
        {
            _meter.Reset();
            _inner.StartClient(address);
        }

        public void Stop() => _inner.Stop();
        public void Disconnect(INetConnection connection) => _inner.Disconnect(connection);

        public void Send(INetConnection connection, in ArraySegment<byte> payload, int channelId)
        {
            _inner.Send(connection, payload, channelId);
            _meter.RecordSend(payload.Count);
        }

        public void Broadcast(IReadOnlyList<INetConnection> connections, in ArraySegment<byte> payload, int channelId)
        {
            _inner.Broadcast(connections, payload, channelId);
            _meter.RecordSend(payload.Count * connections.Count);
        }

        #endregion

        #region IPollableTransport

        public void PollEvents()
        {
            _meter.PollWindow();
            if (_inner is IPollableTransport pollable)
            {
                pollable.PollEvents();
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_inner is IDisposable d)
                d.Dispose();
        }

        #endregion
    }

    /// <summary>
    /// Default <see cref="IBandwidthMeter"/> implementation using dual rotating
    /// buckets. Provides stable per-second readings without external timers.
    /// </summary>
    internal sealed class RollingWindowBandwidthMeter : IBandwidthMeter
    {
        private const long WindowDurationTicks = 500 * TimeSpan.TicksPerMillisecond;

        private Bucket _bucketA;
        private Bucket _bucketB;
        private bool _useA;

        private long _lastRotationTimestamp;
        private long _currentBytesSent;
        private long _currentBytesReceived;
        private int _currentMessagesSent;
        private int _currentMessagesReceived;

        // Cached values updated on rotation for zero-allocation reads
        private long _cachedBytesSentPerSec;
        private long _cachedBytesReceivedPerSec;
        private int _cachedMessagesSentPerSec;
        private int _cachedMessagesReceivedPerSec;
        private float _cachedSendUtilization;
        private float _cachedReceiveUtilization;

        public RollingWindowBandwidthMeter()
        {
            _lastRotationTimestamp = DateTime.UtcNow.Ticks;
        }

        public long BytesSentPerSecond => _cachedBytesSentPerSec;
        public long BytesReceivedPerSecond => _cachedBytesReceivedPerSec;
        public int MessagesSentPerSecond => _cachedMessagesSentPerSec;
        public int MessagesReceivedPerSecond => _cachedMessagesReceivedPerSec;
        public float SendUtilization => _cachedSendUtilization;
        public float ReceiveUtilization => _cachedReceiveUtilization;

        public void RecordSend(int byteCount)
        {
            Interlocked.Add(ref _currentBytesSent, byteCount);
            Interlocked.Increment(ref _currentMessagesSent);
        }

        public void RecordReceive(int byteCount)
        {
            Interlocked.Add(ref _currentBytesReceived, byteCount);
            Interlocked.Increment(ref _currentMessagesReceived);
        }

        /// <summary>
        /// Check whether a window rotation is needed based on elapsed time.
        /// Called as part of PollEvents to self-clock without an external timer.
        /// </summary>
        public void PollWindow()
        {
            long now = DateTime.UtcNow.Ticks;
            if (now - _lastRotationTimestamp < WindowDurationTicks) return;

            Rotate(now);
        }

        public void Reset()
        {
            _lastRotationTimestamp = DateTime.UtcNow.Ticks;
            Interlocked.Exchange(ref _currentBytesSent, 0);
            Interlocked.Exchange(ref _currentBytesReceived, 0);
            Interlocked.Exchange(ref _currentMessagesSent, 0);
            Interlocked.Exchange(ref _currentMessagesReceived, 0);
            _bucketA = default;
            _bucketB = default;
            _cachedBytesSentPerSec = 0;
            _cachedBytesReceivedPerSec = 0;
            _cachedMessagesSentPerSec = 0;
            _cachedMessagesReceivedPerSec = 0;
            _cachedSendUtilization = 0;
            _cachedReceiveUtilization = 0;
        }

        private void Rotate(long nowTicks)
        {
            // Snapshot current accumulated values
            Bucket snapshot = new Bucket
            {
                BytesSent = Interlocked.Exchange(ref _currentBytesSent, 0),
                BytesReceived = Interlocked.Exchange(ref _currentBytesReceived, 0),
                MessagesSent = Interlocked.Exchange(ref _currentMessagesSent, 0),
                MessagesReceived = Interlocked.Exchange(ref _currentMessagesReceived, 0)
            };

            // Replace the older bucket
            if (_useA)
            {
                _bucketA = snapshot;
            }
            else
            {
                _bucketB = snapshot;
            }
            _useA = !_useA;

            // Combined view covers ~1 second (two 500ms buckets)
            _cachedBytesSentPerSec = _bucketA.BytesSent + _bucketB.BytesSent;
            _cachedBytesReceivedPerSec = _bucketA.BytesReceived + _bucketB.BytesReceived;
            _cachedMessagesSentPerSec = _bucketA.MessagesSent + _bucketB.MessagesSent;
            _cachedMessagesReceivedPerSec = _bucketA.MessagesReceived + _bucketB.MessagesReceived;

            // Utilization: ratio against a conservative 1 MB/s baseline.
            // Game-specific code should replace this with actual bandwidth estimation.
            const long baselineBytesPerSec = 1024 * 1024;
            _cachedSendUtilization = _cachedBytesSentPerSec > 0
                ? Math.Min(1f, (float)_cachedBytesSentPerSec / baselineBytesPerSec)
                : 0f;
            _cachedReceiveUtilization = _cachedBytesReceivedPerSec > 0
                ? Math.Min(1f, (float)_cachedBytesReceivedPerSec / baselineBytesPerSec)
                : 0f;

            _lastRotationTimestamp = nowTicks;
        }

        private struct Bucket
        {
            public long BytesSent;
            public long BytesReceived;
            public int MessagesSent;
            public int MessagesReceived;
        }
    }
}
