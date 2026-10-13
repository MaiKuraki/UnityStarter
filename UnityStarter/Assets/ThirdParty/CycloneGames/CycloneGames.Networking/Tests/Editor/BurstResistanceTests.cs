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

        [Test]
        public void Classify_ConstrainedPresetTable_UsesTighterMemoryCeilings()
        {
            // 4 GB: Low under the standard preset, Medium under the constrained preset.
            var hardware = new NetworkHardwareInfo(8, 4096);

            Assert.AreEqual(NetworkPerformanceTier.Low,
                NetworkDeviceProfiles.Classify(hardware, NetworkTierThresholds.CreateStandard()));
            Assert.AreEqual(NetworkPerformanceTier.Medium,
                NetworkDeviceProfiles.Classify(hardware, NetworkTierThresholds.CreateConstrained()));
        }

        [Test]
        public void Classify_WeakGpu_CapsAtMedium_WhenGraphicsMemoryReported()
        {
            var weakGpu = new NetworkHardwareInfo(16, 65536, graphicsMemoryMB: 1024);
            var unknownGpu = new NetworkHardwareInfo(16, 65536, graphicsMemoryMB: 0);

            Assert.AreEqual(NetworkPerformanceTier.Medium, NetworkDeviceProfiles.Classify(weakGpu));
            Assert.AreEqual(NetworkPerformanceTier.High, NetworkDeviceProfiles.Classify(unknownGpu),
                "Unreported graphics memory must not participate in the decision.");
        }

        [Test]
        public void Classify_BenchmarkScore_TakesPrecedenceOverCoreCount()
        {
            // 8 heterogeneous cores (would look "Medium" statically) but a strong
            // measured hot path: the benchmark wins.
            var fastSilicon = new NetworkHardwareInfo(8, 65536, benchmarkScoreMBps: 6000);
            var slowSilicon = new NetworkHardwareInfo(16, 65536, benchmarkScoreMBps: 400);

            Assert.AreEqual(NetworkPerformanceTier.High, NetworkDeviceProfiles.Classify(fastSilicon));
            Assert.AreEqual(NetworkPerformanceTier.Low, NetworkDeviceProfiles.Classify(slowSilicon),
                "A weak measured score must classify Low even with many cores.");
        }

        [Test]
        public void Classify_MemoryIsHardWall_EvenWithStrongBenchmark()
        {
            var strongCpuLowRam = new NetworkHardwareInfo(16, 2048, benchmarkScoreMBps: 9000);
            Assert.AreEqual(NetworkPerformanceTier.Low, NetworkDeviceProfiles.Classify(strongCpuLowRam),
                "Memory is a hard capacity wall that throughput cannot lift.");
        }

        [Test]
        public void Classify_CustomThresholds_AreRespected()
        {
            NetworkTierThresholds strict = NetworkTierThresholds.CreateStandard();
            strict.MediumMaxCores = 32;
            strict.MediumMaxMemoryMB = 65536;

            var hardware = new NetworkHardwareInfo(16, 65536);
            Assert.AreEqual(NetworkPerformanceTier.High, NetworkDeviceProfiles.Classify(hardware));
            Assert.AreEqual(NetworkPerformanceTier.Medium, NetworkDeviceProfiles.Classify(hardware, strict));
        }

        [Test]
        public void Composer_Client_IgnoresConnectionCount_ButScalesBuffers()
        {
            NetworkRuntimeProfile profile = NetworkProfileComposer.Compose(
                NetworkPerformanceTier.Low, NetworkSessionScale.Battle, NetworkEndpointRole.Client);

            Assert.AreEqual(NetworkConstants.DefaultMaxConnections, profile.MaxConnections,
                "A client holds exactly one connection; scale must not inflate MaxConnections.");
            Assert.AreEqual(128, profile.SnapshotBufferSize,
                "A weak device in a large session still gets the buffers the session demands.");
            Assert.AreEqual(15, profile.SendRate, "Rates come from the hardware tier, not the scale.");
        }

        [Test]
        public void Composer_ListenHost_TakesConnectionsFromScale()
        {
            NetworkRuntimeProfile profile = NetworkProfileComposer.Compose(
                NetworkPerformanceTier.High, NetworkSessionScale.Coop, NetworkEndpointRole.ListenHost);

            Assert.AreEqual(128, profile.MaxConnections, "max(tier 128, scale 8) keeps the stronger budget.");
        }

        [Test]
        public void Composer_DedicatedServer_UsesServerTier_WithScaleConnections()
        {
            NetworkRuntimeProfile profile = NetworkProfileComposer.Compose(
                NetworkPerformanceTier.Low, NetworkSessionScale.Massive, NetworkEndpointRole.DedicatedServer);

            Assert.AreEqual(1024, profile.MaxConnections);
            Assert.AreEqual(60, profile.TickRate, "Dedicated servers follow the Server tier tick rate.");
            Assert.AreEqual(NetworkConstants.DefaultMTU, profile.Mtu, "Protocol values are never touched by either axis.");
        }

        [Test]
        public void Composer_MassiveListenHost_IsRejected()
        {
            Assert.Throws<System.ArgumentException>(() => NetworkProfileComposer.Compose(
                NetworkPerformanceTier.High, NetworkSessionScale.Massive, NetworkEndpointRole.ListenHost));
        }

        [Test]
        public void ScaleOverride_ReplacesBaseline_AndRestores()
        {
            try
            {
                NetworkSessionScaleTable.RegisterScaleOverride(
                    NetworkSessionScale.Coop, new NetworkScaleSpec(4, maxConnections: 4, snapshotBufferSize: 16));

                NetworkRuntimeProfile profile = NetworkProfileComposer.Compose(
                    NetworkPerformanceTier.Low, NetworkSessionScale.Coop, NetworkEndpointRole.ListenHost);
                Assert.AreEqual(32, profile.MaxConnections, "max(tier 32, scale 4) keeps the tier budget.");
            }
            finally
            {
                NetworkSessionScaleTable.ClearScaleOverrides();
            }
        }

        [Test]
        public void SessionTuning_Ceiling_ClampsClientDown_ButNeverUp()
        {
            // Server advertises a low cadence (e.g. a small rented server).
            var tuning = new NetworkSessionTuning(tickRate: 20, sendRate: 10);

            NetworkRuntimeProfile fastClient = NetworkProfileComposer.Compose(
                NetworkPerformanceTier.High, NetworkSessionScale.Battle, NetworkEndpointRole.Client);
            NetworkRuntimeProfile tunedFast = tuning.ApplyTo(fastClient);
            Assert.AreEqual(10, tunedFast.SendRate, "Client above the ceiling is clamped down.");
            Assert.AreEqual(fastClient.SnapshotBufferSize, tunedFast.SnapshotBufferSize,
                "Local capacity budgets stay per-device.");

            NetworkRuntimeProfile slowClient = NetworkProfileComposer.Compose(
                NetworkPerformanceTier.Low, NetworkSessionScale.Battle, NetworkEndpointRole.Client);
            NetworkRuntimeProfile tunedSlow = new NetworkSessionTuning(60, 30).ApplyTo(slowClient);
            Assert.AreEqual(15, tunedSlow.SendRate,
                "A weak client below the ceiling keeps its own rate; it is never pushed up.");
        }

        [Test]
        public void CapabilityProbe_ProducesPositiveScore_WithinBudget()
        {
            double score = NetworkCapabilityProbe.MeasureThroughputMBps(rounds: 3, roundBudgetMilliseconds: 4, warmupMilliseconds: 2);
            Assert.Greater(score, 0, "Probe must measure a positive throughput on any supported platform.");
        }

        [Test]
        public void CapabilityProbe_RepeatedRuns_AreSameOrderOfMagnitude()
        {
            double first = NetworkCapabilityProbe.MeasureThroughputMBps(rounds: 3, roundBudgetMilliseconds: 4, warmupMilliseconds: 2);
            double second = NetworkCapabilityProbe.MeasureThroughputMBps(rounds: 3, roundBudgetMilliseconds: 4, warmupMilliseconds: 2);

            Assert.Greater(first, 0);
            Assert.Greater(second, 0);
            double ratio = first > second ? first / second : second / first;
            Assert.Less(ratio, 4.0,
                "Median-of-rounds scoring must be stable against transient jitter; a >4x spread indicates a broken timer environment.");
        }

        [Test]
        public void RegisterTierOverride_ReplacesProfile_WithoutTouchingCore()
        {
            try
            {
                var customLow = new NetworkTierSpec("tier-low", "Studio Low", maxConnections: 8, sendRate: 10);
                NetworkDeviceProfiles.RegisterTierOverride(NetworkPerformanceTier.Low, customLow);

                NetworkRuntimeProfile profile = NetworkDeviceProfiles.ForTier(NetworkPerformanceTier.Low);
                Assert.AreEqual(8, profile.MaxConnections);
                Assert.AreEqual(10, profile.SendRate);
                Assert.AreEqual("Studio Low", profile.DisplayName);

                // Other tiers stay at baseline.
                Assert.AreEqual(1024, NetworkDeviceProfiles.ForTier(NetworkPerformanceTier.Server).MaxConnections);
            }
            finally
            {
                NetworkDeviceProfiles.ClearTierOverrides();
            }

            Assert.AreEqual(32, NetworkDeviceProfiles.ForTier(NetworkPerformanceTier.Low).MaxConnections,
                "ClearTierOverrides must restore the baseline spec.");
        }

        [Test]
        public void ForSpec_BuildsCustomProfile_InheritingUnspecifiedKnobs()
        {
            var spec = new NetworkTierSpec("custom-tournament", "Tournament", maxConnections: 64);
            NetworkRuntimeProfile profile = NetworkDeviceProfiles.ForSpec(spec);

            Assert.AreEqual("custom-tournament", profile.ProfileId);
            Assert.AreEqual(64, profile.MaxConnections);
            Assert.AreEqual(NetworkConstants.DefaultSendRate, profile.SendRate, "Null knobs must inherit defaults.");
            Assert.AreEqual(NetworkConstants.DefaultMTU, profile.Mtu, "Protocol values stay uniform.");
        }

        [Test]
        public void GetSpec_InvalidTier_Throws()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => NetworkDeviceProfiles.GetSpec((NetworkPerformanceTier)200));
        }

        [Test]
        public void DefaultThresholds_InvalidTable_IsRejected()
        {
            NetworkTierThresholds broken = NetworkTierThresholds.CreateStandard();
            broken.MediumMaxCores = broken.LowMaxCores; // Not strictly increasing.
            Assert.Throws<System.ArgumentOutOfRangeException>(() => NetworkDeviceProfiles.DefaultThresholds = broken);
        }
    }
}
