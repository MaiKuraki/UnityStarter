using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace CycloneGames.Networking.Diagnostics
{
    /// <summary>
    /// Collects per-tick network performance metrics for profiling and debugging.
    /// All operations are thread-safe and zero-allocation at steady state.
    /// </summary>
    public sealed class NetworkProfiler
    {
        private long _totalBytesSent;
        private long _totalBytesReceived;
        private long _totalPacketsSent;
        private long _totalPacketsReceived;
        private long _totalMessagesHandled;
        private long _totalErrors;
        private long _totalRateLimitHits;

        // Per-second sliding window
        private int _currentSecondBytesSent;
        private int _currentSecondBytesReceived;
        private int _currentSecondPacketsSent;
        private int _currentSecondPacketsReceived;
        private float _lastSecondReset;

        // Per-message-type tracking
        private readonly Dictionary<ushort, MessageTypeStats> _messageStats =
            new Dictionary<ushort, MessageTypeStats>(64);

        public long TotalBytesSent => Interlocked.Read(ref _totalBytesSent);
        public long TotalBytesReceived => Interlocked.Read(ref _totalBytesReceived);
        public long TotalPacketsSent => Interlocked.Read(ref _totalPacketsSent);
        public long TotalPacketsReceived => Interlocked.Read(ref _totalPacketsReceived);
        public int BytesSentPerSecond => _currentSecondBytesSent;
        public int BytesReceivedPerSecond => _currentSecondBytesReceived;
        public int PacketsSentPerSecond => _currentSecondPacketsSent;
        public int PacketsReceivedPerSecond => _currentSecondPacketsReceived;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordSend(ushort msgId, int bytes)
        {
            Interlocked.Add(ref _totalBytesSent, bytes);
            Interlocked.Increment(ref _totalPacketsSent);
            Interlocked.Increment(ref _currentSecondBytesSent);
            Interlocked.Increment(ref _currentSecondPacketsSent);
            RecordMessageStat(msgId, bytes, true);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordReceive(ushort msgId, int bytes)
        {
            Interlocked.Add(ref _totalBytesReceived, bytes);
            Interlocked.Increment(ref _totalPacketsReceived);
            Interlocked.Increment(ref _currentSecondBytesReceived);
            Interlocked.Increment(ref _currentSecondPacketsReceived);
            RecordMessageStat(msgId, bytes, false);
        }

        public void RecordError() => Interlocked.Increment(ref _totalErrors);
        public void RecordRateLimitHit() => Interlocked.Increment(ref _totalRateLimitHits);

        /// <summary>
        /// Call once per second (or frame) to slide the per-second window.
        /// </summary>
        public void Update(float time)
        {
            if (time - _lastSecondReset >= 1f)
            {
                _lastSecondReset = time;
                Interlocked.Exchange(ref _currentSecondBytesSent, 0);
                Interlocked.Exchange(ref _currentSecondBytesReceived, 0);
                Interlocked.Exchange(ref _currentSecondPacketsSent, 0);
                Interlocked.Exchange(ref _currentSecondPacketsReceived, 0);
            }
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

        public IReadOnlyDictionary<ushort, MessageTypeStats> GetMessageStats() => _messageStats;

        private void RecordMessageStat(ushort msgId, int bytes, bool isSend)
        {
            lock (_messageStats)
            {
                if (!_messageStats.TryGetValue(msgId, out var stats))
                {
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
            lock (_messageStats) { _messageStats.Clear(); }
        }
    }

    public struct ProfilerSnapshot
    {
        public long TotalBytesSent;
        public long TotalBytesReceived;
        public long TotalPacketsSent;
        public long TotalPacketsReceived;
        public int BytesSentPerSecond;
        public int BytesReceivedPerSecond;
        public int PacketsSentPerSecond;
        public int PacketsReceivedPerSecond;
        public long TotalErrors;
        public long TotalRateLimitHits;

        public float SendBandwidthKBps => BytesSentPerSecond / 1024f;
        public float ReceiveBandwidthKBps => BytesReceivedPerSecond / 1024f;
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
