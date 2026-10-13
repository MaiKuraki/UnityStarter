using System;
using CycloneGames.Networking.Transports;
using NUnit.Framework;

namespace CycloneGames.Networking.Tests.Editor
{
    public sealed class BurstResistanceTests
    {
        [Test]
        public void RejectNewPolicy_RejectsOverflow_WithoutDroppingQueued()
        {
            using var queue = new BoundedSendQueue(capacity: 4, SendQueueOverflowPolicy.RejectNew);
            byte[] data = { 1, 2, 3 };

            for (int i = 0; i < 4; i++)
                Assert.AreEqual(SendQueueEnqueueResult.Enqueued, queue.Enqueue(new ArraySegment<byte>(data), 0));

            Assert.AreEqual(SendQueueEnqueueResult.Full, queue.Enqueue(new ArraySegment<byte>(data), 0));
            Assert.AreEqual(0, queue.DroppedCount);
            Assert.AreEqual(4, queue.Count);
        }

        [Test]
        public void DropOldestPolicy_EvictsOldest_UnderBurst()
        {
            using var queue = new BoundedSendQueue(capacity: 4, SendQueueOverflowPolicy.DropOldest);

            for (int i = 0; i < 4; i++)
            {
                byte[] item = { (byte)i };
                queue.Enqueue(new ArraySegment<byte>(item), 0);
            }

            byte[] newest = { 99 };
            Assert.AreEqual(SendQueueEnqueueResult.Evicted, queue.Enqueue(new ArraySegment<byte>(newest), 0));
            Assert.AreEqual(1, queue.DroppedCount);
            Assert.AreEqual(4, queue.Count);

            // Oldest (0) was evicted; FIFO now starts at 1.
            Assert.IsTrue(queue.TryDequeue(out ArraySegment<byte> payload, out _));
            Assert.AreEqual(1, payload.Array[payload.Offset]);
        }

        [Test]
        public void PulseBurst_NeverExceedsCapacity_AndPreservesOrder()
        {
            using var queue = new BoundedSendQueue(capacity: 256, SendQueueOverflowPolicy.RejectNew);

            // 10x-capacity pulse.
            int accepted = 0;
            for (int i = 0; i < 2560; i++)
            {
                byte[] item = BitConverter.GetBytes(i);
                if (queue.Enqueue(new ArraySegment<byte>(item), 0) == SendQueueEnqueueResult.Enqueued)
                    accepted++;
            }

            Assert.AreEqual(256, accepted);

            for (int expected = 0; expected < 256; expected++)
            {
                Assert.IsTrue(queue.TryDequeue(out ArraySegment<byte> payload, out _));
                Assert.AreEqual(expected, BitConverter.ToInt32(payload.Array, payload.Offset));
            }

            Assert.IsFalse(queue.TryDequeue(out _, out _));
        }

        [Test]
        public void DequeuedSegment_RemainsValid_UntilSlotReuse()
        {
            using var queue = new BoundedSendQueue(capacity: 4, SendQueueOverflowPolicy.RejectNew);
            byte[] item = { 7, 7, 7 };
            queue.Enqueue(new ArraySegment<byte>(item), 0);

            Assert.IsTrue(queue.TryDequeue(out ArraySegment<byte> payload, out _));
            Assert.AreEqual(7, payload.Array[payload.Offset]);
        }

        [Test]
        public void DeviceProfiles_AreDistinct_AndProtocolCompatible()
        {
            NetworkRuntimeProfile low = NetworkDeviceProfiles.ForTier(NetworkPerformanceTier.Low);
            NetworkRuntimeProfile server = NetworkDeviceProfiles.ForTier(NetworkPerformanceTier.Server);

            Assert.Less(low.MaxConnections, server.MaxConnections);
            Assert.Less(low.PoolSize, server.PoolSize);
            Assert.AreEqual(NetworkConstants.DefaultMTU, low.Mtu);
            Assert.AreEqual(NetworkConstants.DefaultMTU, server.Mtu, "MTU must stay uniform across tiers");
        }

        [Test]
        public void Classify_MapsHardwareToExpectedTiers()
        {
            Assert.AreEqual(NetworkPerformanceTier.Server, NetworkDeviceProfiles.Classify(64, 131072, isDedicatedServer: true));
            Assert.AreEqual(NetworkPerformanceTier.Low, NetworkDeviceProfiles.Classify(4, 4096, false));
            Assert.AreEqual(NetworkPerformanceTier.Medium, NetworkDeviceProfiles.Classify(8, 16384, false));
            Assert.AreEqual(NetworkPerformanceTier.High, NetworkDeviceProfiles.Classify(16, 65536, false));
        }
    }
}
