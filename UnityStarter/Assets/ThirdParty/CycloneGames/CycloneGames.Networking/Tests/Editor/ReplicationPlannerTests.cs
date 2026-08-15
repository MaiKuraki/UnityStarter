using System;
using CycloneGames.Networking.Replication;
using NUnit.Framework;

namespace CycloneGames.Networking.Tests.Editor
{
    public sealed class ReplicationPlannerTests
    {
        [Test]
        public void ReplicationValuesRejectNonFiniteAndUndefinedInputs()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new NetworkReplicationPolicy(
                (NetworkReplicationInterest)0x80,
                NetworkChannel.Reliable));
            Assert.Throws<ArgumentOutOfRangeException>(() => new NetworkReplicationPolicy(
                NetworkReplicationInterest.Always,
                (NetworkChannel)99));
            Assert.Throws<ArgumentOutOfRangeException>(() => NetworkReplicationPolicy.Area(float.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => new NetworkReplicationPolicy(
                NetworkReplicationInterest.Always,
                priority: float.PositiveInfinity));

            Assert.Throws<ArgumentOutOfRangeException>(() => new NetworkReplicationObserver(
                0,
                1UL,
                0,
                NetworkVector3.Zero,
                10f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new NetworkReplicationObserver(
                1,
                1UL,
                0,
                new NetworkVector3(float.NaN, 0f, 0f),
                10f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new NetworkReplicationObserver(
                1,
                1UL,
                0,
                NetworkVector3.Zero,
                float.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => new NetworkReplicationObserver(
                1,
                1UL,
                -1,
                NetworkVector3.Zero,
                10f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new NetworkReplicationObserver(
                1,
                1UL,
                0,
                NetworkVector3.Zero,
                10f,
                quality: (ConnectionQuality)99));

            Assert.Throws<ArgumentOutOfRangeException>(() => new NetworkReplicatedObject(
                1UL,
                NetworkReplicationPolicy.Always(),
                new NetworkVector3(float.PositiveInfinity, 0f, 0f)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new NetworkReplicatedObject(
                1UL,
                NetworkReplicationPolicy.Always(),
                NetworkVector3.Zero,
                ownerConnectionId: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new NetworkReplicatedObject(
                1UL,
                NetworkReplicationPolicy.Always(),
                NetworkVector3.Zero,
                teamId: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new NetworkReplicatedObject(
                1UL,
                NetworkReplicationPolicy.Always(),
                NetworkVector3.Zero,
                lastSentTick: -2));
            Assert.Throws<ArgumentOutOfRangeException>(() => new NetworkReplicationSelection(
                1UL,
                0,
                NetworkChannel.Reliable,
                NetworkInterestReason.None,
                1,
                1f,
                requiresFullState: false));
            Assert.Throws<ArgumentOutOfRangeException>(() => new NetworkReplicationSelection(
                1UL,
                0,
                NetworkChannel.Reliable,
                NetworkInterestReason.Always,
                1,
                float.PositiveInfinity,
                requiresFullState: false));
        }

        [Test]
        public void PlannerRejectsDefaultObserverValue()
        {
            var planner = new NetworkReplicationPlanner();
            var budget = new NetworkSendBudget(0, 0);

            Assert.Throws<ArgumentException>(() => planner.BuildPlan(
                default,
                ReadOnlySpan<NetworkReplicatedObject>.Empty,
                serverTick: 0,
                ref budget,
                Span<NetworkReplicationSelection>.Empty));
        }

        [TestCase(NetworkInterestReason.None)]
        [TestCase((NetworkInterestReason)0x80)]
        public void PlannerFailsClosedWhenCustomEvaluatorReturnsInvalidReason(
            NetworkInterestReason invalidReason)
        {
            var planner = new NetworkReplicationPlanner(new FixedReasonInterestEvaluator(invalidReason));
            var observer = new NetworkReplicationObserver(
                connectionId: 1,
                playerId: 1UL,
                teamId: 0,
                position: NetworkVector3.Zero,
                viewRadius: 10f);
            NetworkReplicatedObject[] objects =
            {
                new NetworkReplicatedObject(
                    1UL,
                    NetworkReplicationPolicy.Always(),
                    NetworkVector3.Zero,
                    estimatedPayloadBytes: 8)
            };
            var budget = new NetworkSendBudget(maxBytes: 8, maxMessages: 1);
            var results = new NetworkReplicationSelection[1];

            int count = planner.BuildPlan(observer, objects, serverTick: 0, ref budget, results);

            Assert.AreEqual(0, count);
            Assert.AreEqual(8, budget.RemainingBytes);
            Assert.AreEqual(1, budget.RemainingMessages);
            Assert.AreEqual(default(NetworkReplicationSelection), results[0]);
        }

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

        private sealed class FixedReasonInterestEvaluator : INetworkInterestEvaluator
        {
            private readonly NetworkInterestReason _reason;

            public FixedReasonInterestEvaluator(NetworkInterestReason reason)
            {
                _reason = reason;
            }

            public bool IsInterested(
                in NetworkReplicationObserver observer,
                in NetworkReplicatedObject replicatedObject,
                out NetworkInterestReason reason)
            {
                reason = _reason;
                return true;
            }
        }
    }
}
