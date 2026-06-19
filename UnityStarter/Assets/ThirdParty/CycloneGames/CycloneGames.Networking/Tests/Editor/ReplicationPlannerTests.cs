using CycloneGames.Networking.Replication;
using NUnit.Framework;

namespace CycloneGames.Networking.Tests.Editor
{
    public sealed class ReplicationPlannerTests
    {
        [Test]
        public void InterestEvaluator_UsesAreaOwnerAuthAndLayers()
        {
            var evaluator = DefaultNetworkInterestEvaluator.Instance;
            var observer = new NetworkReplicationObserver(
                connectionId: 10,
                playerId: 100UL,
                teamId: 2,
                position: NetworkVector3.Zero,
                viewRadius: 25f,
                interestLayerMask: 0b0010u,
                isAuthenticated: true);
            var ownerObject = new NetworkReplicatedObject(
                1UL,
                NetworkReplicationPolicy.OwnerOnly(),
                new NetworkVector3(100f, 0f, 0f),
                ownerConnectionId: 10,
                ownerPlayerId: 100UL,
                interestLayerMask: 0b0010u);
            var areaObject = new NetworkReplicatedObject(
                2UL,
                NetworkReplicationPolicy.Area(30f),
                new NetworkVector3(5f, 0f, 0f),
                interestLayerMask: 0b0010u);
            var wrongLayerObject = new NetworkReplicatedObject(
                3UL,
                NetworkReplicationPolicy.Area(30f),
                new NetworkVector3(5f, 0f, 0f),
                interestLayerMask: 0b0100u);
            var unauthenticatedObserver = new NetworkReplicationObserver(
                connectionId: 11,
                playerId: 101UL,
                teamId: 2,
                position: NetworkVector3.Zero,
                viewRadius: 25f,
                interestLayerMask: NetworkReplicationObserver.ALL_LAYERS,
                isAuthenticated: false);

            Assert.IsTrue(evaluator.IsInterested(observer, ownerObject, out NetworkInterestReason ownerReason));
            Assert.IsTrue((ownerReason & NetworkInterestReason.Owner) != 0);
            Assert.IsTrue(evaluator.IsInterested(observer, areaObject, out NetworkInterestReason areaReason));
            Assert.IsTrue((areaReason & NetworkInterestReason.Area) != 0);
            Assert.IsFalse(evaluator.IsInterested(observer, wrongLayerObject, out _));
            Assert.IsFalse(evaluator.IsInterested(unauthenticatedObserver, areaObject, out _));
        }

        [Test]
        public void InterestEvaluator_DoesNotAutoIncludeManualPolicy()
        {
            var evaluator = DefaultNetworkInterestEvaluator.Instance;
            var observer = new NetworkReplicationObserver(
                connectionId: 10,
                playerId: 100UL,
                teamId: 2,
                position: NetworkVector3.Zero,
                viewRadius: 25f);
            var manualObject = new NetworkReplicatedObject(
                1UL,
                new NetworkReplicationPolicy(NetworkReplicationInterest.Manual),
                NetworkVector3.Zero);

            Assert.IsFalse(evaluator.IsInterested(observer, manualObject, out NetworkInterestReason reason));
            Assert.AreEqual(NetworkInterestReason.None, reason);
        }

        [Test]
        public void Planner_UsesPriorityBeforeInputOrderUnderBudget()
        {
            var planner = new NetworkReplicationPlanner();
            var observer = new NetworkReplicationObserver(
                connectionId: 1,
                playerId: 1UL,
                teamId: 0,
                position: NetworkVector3.Zero,
                viewRadius: 50f);
            NetworkReplicatedObject[] objects =
            {
                new NetworkReplicatedObject(
                    1UL,
                    NetworkReplicationPolicy.Area(50f, priority: 1f),
                    new NetworkVector3(1f, 0f, 0f),
                    estimatedPayloadBytes: 80),
                new NetworkReplicatedObject(
                    2UL,
                    NetworkReplicationPolicy.Area(50f, priority: 10f),
                    new NetworkVector3(1f, 0f, 0f),
                    estimatedPayloadBytes: 80)
            };
            var budget = new NetworkSendBudget(maxBytes: 80, maxMessages: 1);
            NetworkReplicationSelection[] results = new NetworkReplicationSelection[2];

            int count = planner.BuildPlan(observer, objects, serverTick: 10, ref budget, results);

            Assert.AreEqual(1, count);
            Assert.AreEqual(2UL, results[0].ObjectId);
            Assert.AreEqual(0, budget.RemainingBytes);
            Assert.AreEqual(0, budget.RemainingMessages);
        }

        [Test]
        public void Planner_SkipsCleanObjectsUnlessPolicyAllowsUnchanged()
        {
            var planner = new NetworkReplicationPlanner();
            var observer = new NetworkReplicationObserver(
                connectionId: 1,
                playerId: 1UL,
                teamId: 0,
                position: NetworkVector3.Zero,
                viewRadius: 50f);
            NetworkReplicatedObject[] objects =
            {
                new NetworkReplicatedObject(
                    1UL,
                    NetworkReplicationPolicy.Area(50f),
                    NetworkVector3.Zero,
                    isDirty: false),
                new NetworkReplicatedObject(
                    2UL,
                    NetworkReplicationPolicy.Area(50f, sendUnchanged: true),
                    NetworkVector3.Zero,
                    isDirty: false)
            };
            var budget = new NetworkSendBudget(maxBytes: 256, maxMessages: 4);
            NetworkReplicationSelection[] results = new NetworkReplicationSelection[2];

            int count = planner.BuildPlan(observer, objects, serverTick: 10, ref budget, results);

            Assert.AreEqual(1, count);
            Assert.AreEqual(2UL, results[0].ObjectId);
        }

        [Test]
        public void Planner_RequiresMinIntervalExceptFullState()
        {
            var planner = new NetworkReplicationPlanner();
            var observer = new NetworkReplicationObserver(
                connectionId: 1,
                playerId: 1UL,
                teamId: 0,
                position: NetworkVector3.Zero,
                viewRadius: 50f);
            NetworkReplicatedObject[] objects =
            {
                new NetworkReplicatedObject(
                    1UL,
                    NetworkReplicationPolicy.Area(50f, minIntervalTicks: 10),
                    NetworkVector3.Zero,
                    requiresFullState: false,
                    lastSentTick: 8),
                new NetworkReplicatedObject(
                    2UL,
                    NetworkReplicationPolicy.Area(50f, minIntervalTicks: 10),
                    NetworkVector3.Zero,
                    requiresFullState: true,
                    lastSentTick: 8)
            };
            var budget = new NetworkSendBudget(maxBytes: 256, maxMessages: 4);
            NetworkReplicationSelection[] results = new NetworkReplicationSelection[2];

            int count = planner.BuildPlan(observer, objects, serverTick: 10, ref budget, results);

            Assert.AreEqual(1, count);
            Assert.AreEqual(2UL, results[0].ObjectId);
            Assert.IsTrue(results[0].RequiresFullState);
        }
    }
}
