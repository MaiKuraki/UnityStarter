using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace CycloneGames.Networking.Diagnostics
{
    /// <summary>
    /// Collects per-tick network performance metrics for profiling and debugging.
    /// Recording and snapshot operations are thread-safe. Known message IDs reuse a pre-sized
    /// table entry; concrete runtime allocation behavior still requires profiling. Diagnostic map snapshots allocate.
    /// </summary>
    public sealed class NetworkProfiler
    {
        private long _totalBytesSent;
        private long _totalBytesReceived;
        private long _totalPacketsSent;
        private long _totalPacketsReceived;
        private long _totalErrors;
        private long _totalRateLimitHits;

        private const int DefaultMaxTrackedMessageTypes = 1024;

        // Active counters are atomically exchanged when a complete sampling window closes.
        private long _windowBytesSent;
        private long _windowBytesReceived;
        private long _windowPacketsSent;
        private long _windowPacketsReceived;
        private long _publishedBytesSentPerSecond;
        private long _publishedBytesReceivedPerSecond;
        private long _publishedPacketsSentPerSecond;
        private long _publishedPacketsReceivedPerSecond;
        private double _lastWindowTime;
        private long _droppedMessageTypeSamples;

        // Per-message-type tracking
        private readonly Dictionary<ushort, MessageTypeStats> _messageStats;
        private readonly int _maxTrackedMessageTypes;

        public NetworkProfiler(int maxTrackedMessageTypes = DefaultMaxTrackedMessageTypes)
        {
            if (maxTrackedMessageTypes <= 0 || maxTrackedMessageTypes > ushort.MaxValue + 1)
                throw new ArgumentOutOfRangeException(nameof(maxTrackedMessageTypes));

            _maxTrackedMessageTypes = maxTrackedMessageTypes;
            _messageStats = new Dictionary<ushort, MessageTypeStats>(maxTrackedMessageTypes);
        }

        public long TotalBytesSent => Interlocked.Read(ref _totalBytesSent);
        public long TotalBytesReceived => Interlocked.Read(ref _totalBytesReceived);
        public long TotalPacketsSent => Interlocked.Read(ref _totalPacketsSent);
        public long TotalPacketsReceived => Interlocked.Read(ref _totalPacketsReceived);
        public long BytesSentPerSecond => Interlocked.Read(ref _publishedBytesSentPerSecond);
        public long BytesReceivedPerSecond => Interlocked.Read(ref _publishedBytesReceivedPerSecond);
        public long PacketsSentPerSecond => Interlocked.Read(ref _publishedPacketsSentPerSecond);
        public long PacketsReceivedPerSecond => Interlocked.Read(ref _publishedPacketsReceivedPerSecond);
        public long DroppedMessageTypeSamples => Interlocked.Read(ref _droppedMessageTypeSamples);
        public int MaxTrackedMessageTypes => _maxTrackedMessageTypes;
        public int TrackedMessageTypeCount
        {
            get
            {
                lock (_messageStats)
                    return _messageStats.Count;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordSend(ushort msgId, int bytes)
        {
            if (bytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bytes), bytes, "Byte count cannot be negative.");
            }

            Interlocked.Add(ref _totalBytesSent, bytes);
            Interlocked.Increment(ref _totalPacketsSent);
            Interlocked.Add(ref _windowBytesSent, bytes);
            Interlocked.Increment(ref _windowPacketsSent);
            RecordMessageStat(msgId, bytes, true);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordReceive(ushort msgId, int bytes)
        {
            if (bytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bytes), bytes, "Byte count cannot be negative.");
            }

            Interlocked.Add(ref _totalBytesReceived, bytes);
            Interlocked.Increment(ref _totalPacketsReceived);
            Interlocked.Add(ref _windowBytesReceived, bytes);
            Interlocked.Increment(ref _windowPacketsReceived);
            RecordMessageStat(msgId, bytes, false);
        }

        public void RecordError() => Interlocked.Increment(ref _totalErrors);
        public void RecordRateLimitHit() => Interlocked.Increment(ref _totalRateLimitHits);

        /// <summary>
        /// Call from one diagnostics owner to publish a normalized completed window.
        /// Recording can continue concurrently, so the independently exchanged byte/packet
        /// counters are approximate telemetry rather than an atomic accounting boundary.
        /// </summary>
        public void Update(double time)
        {
            if (time < 0d || double.IsNaN(time) || double.IsInfinity(time))
                return;

            double observedReset = Volatile.Read(ref _lastWindowTime);
            double elapsed = time - observedReset;
            if (elapsed < 1d)
                return;

            if (Interlocked.CompareExchange(ref _lastWindowTime, time, observedReset) != observedReset)
                return;

            long bytesSent = Interlocked.Exchange(ref _windowBytesSent, 0L);
            long bytesReceived = Interlocked.Exchange(ref _windowBytesReceived, 0L);
            long packetsSent = Interlocked.Exchange(ref _windowPacketsSent, 0L);
            long packetsReceived = Interlocked.Exchange(ref _windowPacketsReceived, 0L);

            Interlocked.Exchange(ref _publishedBytesSentPerSecond, ToPerSecond(bytesSent, elapsed));
            Interlocked.Exchange(ref _publishedBytesReceivedPerSecond, ToPerSecond(bytesReceived, elapsed));
            Interlocked.Exchange(ref _publishedPacketsSentPerSecond, ToPerSecond(packetsSent, elapsed));
            Interlocked.Exchange(ref _publishedPacketsReceivedPerSecond, ToPerSecond(packetsReceived, elapsed));
        }

        public ProfilerSnapshot TakeSnapshot()
        {
            return new ProfilerSnapshot
            {
                TotalBytesSent = TotalBytesSent,
                TotalBytesReceived = TotalBytesReceived,
                TotalPacketsSent = TotalPacketsSent,
                TotalPacketsReceived = TotalPacketsReceived,
                BytesSentPerSecond = BytesSentPerSecond,
                BytesReceivedPerSecond = BytesReceivedPerSecond,
                PacketsSentPerSecond = PacketsSentPerSecond,
                PacketsReceivedPerSecond = PacketsReceivedPerSecond,
                TotalErrors = Interlocked.Read(ref _totalErrors),
                TotalRateLimitHits = Interlocked.Read(ref _totalRateLimitHits)
            };
        }

        /// <summary>
        /// Creates a stable point-in-time copy of the per-message statistics.
        /// This diagnostics call allocates and must not be used in a network hot path.
        /// </summary>
        public IReadOnlyDictionary<ushort, MessageTypeStats> GetMessageStats()
        {
            lock (_messageStats)
            {
                return new Dictionary<ushort, MessageTypeStats>(_messageStats);
            }
        }

        private void RecordMessageStat(ushort msgId, int bytes, bool isSend)
        {
            lock (_messageStats)
            {
                if (!_messageStats.TryGetValue(msgId, out var stats))
                {
                    if (_messageStats.Count >= _maxTrackedMessageTypes)
                    {
                        Interlocked.Increment(ref _droppedMessageTypeSamples);
                        return;
                    }

                    stats = new MessageTypeStats { MessageId = msgId };
                    _messageStats[msgId] = stats;
                }

                if (isSend)
                {
                    stats.SendCount++;
                    stats.SendBytes += bytes;
                }
                else
                {
                    stats.ReceiveCount++;
                    stats.ReceiveBytes += bytes;
                }

                _messageStats[msgId] = stats;
            }
        }

        public void Reset()
        {
            Interlocked.Exchange(ref _totalBytesSent, 0);
            Interlocked.Exchange(ref _totalBytesReceived, 0);
            Interlocked.Exchange(ref _totalPacketsSent, 0);
            Interlocked.Exchange(ref _totalPacketsReceived, 0);
            Interlocked.Exchange(ref _totalErrors, 0);
            Interlocked.Exchange(ref _totalRateLimitHits, 0);
            Interlocked.Exchange(ref _windowBytesSent, 0L);
            Interlocked.Exchange(ref _windowBytesReceived, 0L);
            Interlocked.Exchange(ref _windowPacketsSent, 0L);
            Interlocked.Exchange(ref _windowPacketsReceived, 0L);
            Interlocked.Exchange(ref _publishedBytesSentPerSecond, 0L);
            Interlocked.Exchange(ref _publishedBytesReceivedPerSecond, 0L);
            Interlocked.Exchange(ref _publishedPacketsSentPerSecond, 0L);
            Interlocked.Exchange(ref _publishedPacketsReceivedPerSecond, 0L);
            Interlocked.Exchange(ref _lastWindowTime, 0d);
            Interlocked.Exchange(ref _droppedMessageTypeSamples, 0L);
            lock (_messageStats)
            {
                _messageStats.Clear();
            }
        }

        private static long ToPerSecond(long count, double elapsedSeconds)
        {
            if (count <= 0L)
                return 0L;

            double rate = count / elapsedSeconds;
            return rate >= long.MaxValue ? long.MaxValue : (long)Math.Round(rate, MidpointRounding.AwayFromZero);
        }
    }

    public struct ProfilerSnapshot
    {
        public long TotalBytesSent;
        public long TotalBytesReceived;
        public long TotalPacketsSent;
        public long TotalPacketsReceived;
        public long BytesSentPerSecond;
        public long BytesReceivedPerSecond;
        public long PacketsSentPerSecond;
        public long PacketsReceivedPerSecond;
        public long TotalErrors;
        public long TotalRateLimitHits;

        public double SendBandwidthKBps => BytesSentPerSecond / 1024d;
        public double ReceiveBandwidthKBps => BytesReceivedPerSecond / 1024d;
    }

    public struct MessageTypeStats
    {
        public ushort MessageId;
        public long SendCount;
        public long SendBytes;
        public long ReceiveCount;
        public long ReceiveBytes;
    }
}
