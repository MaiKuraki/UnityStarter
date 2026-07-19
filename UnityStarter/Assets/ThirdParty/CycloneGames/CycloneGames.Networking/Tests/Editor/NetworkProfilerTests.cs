using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CycloneGames.Networking.Diagnostics;
using NUnit.Framework;

namespace CycloneGames.Networking.Tests.Editor
{
    public sealed class NetworkProfilerTests
    {
        [Test]
        public void RecordSendAndReceive_TracksBytesAndPackets()
        {
            var profiler = new NetworkProfiler();

            profiler.RecordSend(10, 128);
            profiler.RecordReceive(10, 64);
            profiler.Update(1d);

            ProfilerSnapshot snapshot = profiler.TakeSnapshot();
            Assert.AreEqual(128L, snapshot.TotalBytesSent);
            Assert.AreEqual(64L, snapshot.TotalBytesReceived);
            Assert.AreEqual(1L, snapshot.TotalPacketsSent);
            Assert.AreEqual(1L, snapshot.TotalPacketsReceived);
            Assert.AreEqual(128L, snapshot.BytesSentPerSecond);
            Assert.AreEqual(64L, snapshot.BytesReceivedPerSecond);
            Assert.AreEqual(1L, snapshot.PacketsSentPerSecond);
            Assert.AreEqual(1L, snapshot.PacketsReceivedPerSecond);

            IReadOnlyDictionary<ushort, MessageTypeStats> messageStats = profiler.GetMessageStats();
            Assert.IsTrue(messageStats.TryGetValue(10, out MessageTypeStats stats));
            Assert.AreEqual(1L, stats.SendCount);
            Assert.AreEqual(128L, stats.SendBytes);
            Assert.AreEqual(1L, stats.ReceiveCount);
            Assert.AreEqual(64L, stats.ReceiveBytes);
        }

        [Test]
        public void RecordSendAndReceive_RejectNegativeByteCounts()
        {
            var profiler = new NetworkProfiler();

            Assert.Throws<ArgumentOutOfRangeException>(() => profiler.RecordSend(1, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => profiler.RecordReceive(1, -1));
        }

        [Test]
        public void GetMessageStats_ReturnsStableSnapshot()
        {
            var profiler = new NetworkProfiler();
            profiler.RecordSend(7, 10);

            IReadOnlyDictionary<ushort, MessageTypeStats> first = profiler.GetMessageStats();
            profiler.RecordSend(7, 20);
            IReadOnlyDictionary<ushort, MessageTypeStats> second = profiler.GetMessageStats();

            Assert.AreEqual(10L, first[7].SendBytes);
            Assert.AreEqual(30L, second[7].SendBytes);
        }

        [Test]
        public void Reset_ClearsTotalsWindowAndMessageStats()
        {
            var profiler = new NetworkProfiler();
            profiler.RecordSend(3, 30);
            profiler.RecordReceive(3, 40);
            profiler.RecordError();
            profiler.RecordRateLimitHit();

            profiler.Reset();

            ProfilerSnapshot snapshot = profiler.TakeSnapshot();
            Assert.AreEqual(0L, snapshot.TotalBytesSent);
            Assert.AreEqual(0L, snapshot.TotalBytesReceived);
            Assert.AreEqual(0L, snapshot.TotalPacketsSent);
            Assert.AreEqual(0L, snapshot.TotalPacketsReceived);
            Assert.AreEqual(0L, snapshot.BytesSentPerSecond);
            Assert.AreEqual(0L, snapshot.BytesReceivedPerSecond);
            Assert.AreEqual(0L, snapshot.PacketsSentPerSecond);
            Assert.AreEqual(0L, snapshot.PacketsReceivedPerSecond);
            Assert.AreEqual(0L, snapshot.TotalErrors);
            Assert.AreEqual(0L, snapshot.TotalRateLimitHits);
            Assert.AreEqual(0, profiler.GetMessageStats().Count);
        }

        [Test]
        public void RecordSend_ConcurrentCallers_DoNotLoseCounts()
        {
            const int ITERATIONS = 1024;
            var profiler = new NetworkProfiler();

            Parallel.For(0, ITERATIONS, _ => profiler.RecordSend(5, 2));
            profiler.Update(1d);

            ProfilerSnapshot snapshot = profiler.TakeSnapshot();
            Assert.AreEqual(ITERATIONS * 2L, snapshot.TotalBytesSent);
            Assert.AreEqual(ITERATIONS, snapshot.TotalPacketsSent);
            Assert.AreEqual(ITERATIONS * 2L, snapshot.BytesSentPerSecond);
            Assert.AreEqual((long)ITERATIONS, snapshot.PacketsSentPerSecond);
            Assert.AreEqual(ITERATIONS, profiler.GetMessageStats()[5].SendCount);
        }

        [Test]
        public void Update_AfterOneSecond_PublishesCompletedWindowRate()
        {
            var profiler = new NetworkProfiler();
            profiler.RecordSend(2, 50);

            profiler.Update(1d);

            ProfilerSnapshot snapshot = profiler.TakeSnapshot();
            Assert.AreEqual(50L, snapshot.TotalBytesSent);
            Assert.AreEqual(1L, snapshot.TotalPacketsSent);
            Assert.AreEqual(50L, snapshot.BytesSentPerSecond);
            Assert.AreEqual(1L, snapshot.PacketsSentPerSecond);

            profiler.Update(2d);
            snapshot = profiler.TakeSnapshot();
            Assert.AreEqual(0L, snapshot.BytesSentPerSecond);
            Assert.AreEqual(0L, snapshot.PacketsSentPerSecond);
        }

        [Test]
        public void MessageTypeTracking_IsBounded_AndReportsDroppedSamples()
        {
            var profiler = new NetworkProfiler(maxTrackedMessageTypes: 1);

            profiler.RecordSend(1, 1);
            profiler.RecordSend(2, 1);

            Assert.AreEqual(1, profiler.TrackedMessageTypeCount);
            Assert.AreEqual(1L, profiler.DroppedMessageTypeSamples);
            Assert.IsTrue(profiler.GetMessageStats().ContainsKey(1));
            Assert.IsFalse(profiler.GetMessageStats().ContainsKey(2));
        }
    }
}
