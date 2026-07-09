using System;
using NUnit.Framework;
using CycloneGames.Networking.Buffers;
using CycloneGames.Networking.Replication;
using CycloneGames.Networking.Routing;
using CycloneGames.Networking.Serialization;

namespace CycloneGames.Networking.Tests.Editor
{
    public sealed class ReplicationInfrastructureTests
    {
        [Test]
        public void StateCache_UnknownObjectsRequireFullStateUntilSent()
        {
            var cache = new NetworkReplicationStateCache();
            var source = new NetworkReplicatedObject(
                10UL,
                NetworkReplicationPolicy.Area(50f),
                NetworkVector3.Zero,
                isDirty: true,
                requiresFullState: false);

            NetworkReplicatedObject first = cache.ApplyState(connectionId: 1, source);

            Assert.IsTrue(first.RequiresFullState);
            Assert.AreEqual(NetworkReplicatedObject.NEVER_SENT, first.LastSentTick);

            cache.MarkSent(1, source.ObjectId, serverTick: 12, fullState: true, payloadBytes: 44, payloadHash: 123UL);
            NetworkReplicatedObject second = cache.ApplyState(connectionId: 1, source);

            Assert.IsFalse(second.RequiresFullState);
            Assert.AreEqual(12, second.LastSentTick);

            cache.RequireFullState(1, source.ObjectId);
            NetworkReplicatedObject third = cache.ApplyState(connectionId: 1, source);

            Assert.IsTrue(third.RequiresFullState);
        }

        [Test]
        public void SpatialHashIndex_QueryFiltersByRadiusAndLayer()
        {
            var index = new NetworkSpatialHashIndex(cellSize: 10f);
            index.Upsert(1UL, sourceIndex: 0, new NetworkVector3(5f, 0f, 0f), layerMask: 0b0001u);
            index.Upsert(2UL, sourceIndex: 1, new NetworkVector3(12f, 0f, 0f), layerMask: 0b0010u);
            index.Upsert(3UL, sourceIndex: 2, new NetworkVector3(100f, 0f, 0f), layerMask: 0b0001u);
            NetworkSpatialQueryResult[] results = new NetworkSpatialQueryResult[4];

            int count = index.Query(NetworkVector3.Zero, radius: 20f, layerMask: 0b0001u, results);

            Assert.AreEqual(1, count);
            Assert.AreEqual(1UL, results[0].ObjectId);
            Assert.AreEqual(0, results[0].SourceIndex);

            index.Upsert(1UL, sourceIndex: 0, new NetworkVector3(80f, 0f, 0f), layerMask: 0b0001u);
            count = index.Query(NetworkVector3.Zero, radius: 20f, layerMask: 0b0001u, results);

            Assert.AreEqual(0, count);
            Assert.IsTrue(index.Remove(1UL));
            Assert.AreEqual(2, index.Count);
        }

        [Test]
        public void SpatialHashIndex_RejectsNonFiniteCoordinates()
        {
            var index = new NetworkSpatialHashIndex(cellSize: 10f);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                index.Upsert(1UL, sourceIndex: 0, new NetworkVector3(float.NaN, 0f, 0f)));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                index.Query(new NetworkVector3(float.PositiveInfinity, 0f, 0f), 10f, NetworkReplicationObserver.ALL_LAYERS, new NetworkSpatialQueryResult[4]));
        }

        [Test]
        public void SpatialHashIndex_EmptyResultBufferReturnsZero()
        {
            var index = new NetworkSpatialHashIndex(cellSize: 10f);
            index.Upsert(1UL, sourceIndex: 0, NetworkVector3.Zero);

            int count = index.Query(
                NetworkVector3.Zero,
                radius: 20f,
                NetworkReplicationObserver.ALL_LAYERS,
                Array.Empty<NetworkSpatialQueryResult>());

            Assert.AreEqual(0, count);
        }

        [Test]
        public void SnapshotPacketBuilder_WritesMixedFullAndDeltaEntries()
        {
            using NetworkBuffer buffer = NetworkBufferPool.Get();
            var builder = new NetworkSnapshotPacketBuilder();
            var payloadSource = new FixedPayloadSource();
            NetworkReplicationSelection[] selections =
            {
                new NetworkReplicationSelection(
                    10UL,
                    sourceIndex: 0,
                    NetworkChannel.Reliable,
                    NetworkInterestReason.Owner,
                    estimatedPayloadBytes: 4,
                    score: 10f,
                    requiresFullState: true),
                new NetworkReplicationSelection(
                    11UL,
                    sourceIndex: 1,
                    NetworkChannel.UnreliableSequenced,
                    NetworkInterestReason.Area,
                    estimatedPayloadBytes: 4,
                    score: 1f,
                    requiresFullState: false)
            };

            NetworkSnapshotWriteResult result = builder.WriteSnapshot(
                selections,
                serverTick: 25,
                payloadSource,
                buffer);

            Assert.AreEqual(2, result.ObjectCount);
            Assert.AreEqual(1, result.FullStateCount);
            Assert.AreEqual(1, result.DeltaCount);
            Assert.AreEqual(buffer.Position, result.BytesWritten);
            Assert.Greater(result.AggregatePayloadHash, 0UL);
        }

        [Test]
        public void AdaptiveScheduler_ReducesBudgetAndRaisesDegradedEvent()
        {
            var scheduler = new AdaptiveNetworkSendScheduler();
            int degradedConnection = 0;
            scheduler.OnConnectionDegraded += (connectionId, _) => degradedConnection = connectionId;
            var poorStats = new NetworkStatistics(
                bytesSent: 10000,
                bytesReceived: 1000,
                packetsSent: 100,
                packetsReceived: 100,
                connectionCount: 1,
                droppedPackets: 30,
                averageRttMs: 320f);

            scheduler.Update(1, poorStats, ConnectionQuality.Poor, deltaTime: 1f);
            scheduler.Update(1, poorStats, ConnectionQuality.Poor, deltaTime: 1f);
            scheduler.Update(1, poorStats, ConnectionQuality.Poor, deltaTime: 1f);
            NetworkSendBudget budget = scheduler.CreateSendBudget(1);

            Assert.AreEqual(1, degradedConnection);
            Assert.Less(scheduler.GetPriorityBudget(1), 1f);
            Assert.Less(budget.MaxBytes, AdaptiveNetworkSendSchedulerOptions.Default.BaseBudgetBytes);
        }

        [Test]
        public void AdaptiveScheduler_DisconnectedConnectionGetsNoSendBudget()
        {
            var scheduler = new AdaptiveNetworkSendScheduler();
            var stats = new NetworkStatistics(
                bytesSent: 0,
                bytesReceived: 0,
                packetsSent: 0,
                packetsReceived: 0,
                connectionCount: 1,
                droppedPackets: 0,
                averageRttMs: 0f);

            scheduler.Update(1, stats, ConnectionQuality.Disconnected, deltaTime: 0.016f);
            NetworkSendBudget budget = scheduler.CreateSendBudget(1);

            Assert.AreEqual(0, scheduler.GetPriorityBudget(1));
            Assert.AreEqual(0, budget.MaxBytes);
            Assert.AreEqual(0, budget.MaxMessages);
        }

        [Test]
        public void ActorRouteTable_MigrationRemovesActorFromPreviousProcessIndex()
        {
            var router = new ActorRouteTable();

            router.Register(10, "process-a");
            router.Register(11, "process-a");
            router.Register(10, "process-b");

            Assert.IsTrue(router.TryResolve(10, out string processId));
            Assert.AreEqual("process-b", processId);
            CollectionAssert.AreEquivalent(new[] { 11 }, router.GetActorsOnProcess("process-a"));
            CollectionAssert.AreEquivalent(new[] { 10 }, router.GetActorsOnProcess("process-b"));
        }

        [Test]
        public void ActorRouteTable_UnregisterRemovesActorFromProcessIndex()
        {
            var router = new ActorRouteTable();

            router.Register(10, "process-a");
            router.Unregister(10);

            Assert.IsFalse(router.TryResolve(10, out _));
            Assert.AreEqual(0, router.TrackedActorCount);
            Assert.AreEqual(0, router.GetActorsOnProcess("process-a").Count);
        }

        [Test]
        public void LoadSimulator_IsDeterministicForSameOptions()
        {
            var simulator = new NetworkReplicationLoadSimulator();
            var options = new NetworkReplicationLoadSimulationOptions(
                connectionCount: 8,
                objectCount: 128,
                tickCount: 4,
                worldSize: 200f,
                viewRadius: 45f,
                dirtyRatio: 0.75f,
                budgetBytes: 2048,
                budgetMessages: 32,
                resultCapacity: 64,
                seed: 99u);

            NetworkReplicationLoadSimulationResult first = simulator.Run(options);
            NetworkReplicationLoadSimulationResult second = simulator.Run(options);

            Assert.AreEqual(first.TotalPlans, second.TotalPlans);
            Assert.AreEqual(first.TotalSelections, second.TotalSelections);
            Assert.AreEqual(first.TotalEstimatedBytes, second.TotalEstimatedBytes);
            Assert.Greater(first.TotalSelections, 0);
        }

        private sealed class FixedPayloadSource : INetworkSnapshotPayloadSource
        {
            public int GetPayloadSize(int sourceIndex, bool fullState)
            {
                return 4;
            }

            public ulong GetPayloadHash(int sourceIndex, bool fullState)
            {
                return (uint)(sourceIndex + 1) | (fullState ? 0x100000000UL : 0UL);
            }

            public void WritePayload(int sourceIndex, bool fullState, INetWriter writer)
            {
                writer.WriteInt(fullState ? sourceIndex + 100 : sourceIndex);
            }
        }
    }
}
