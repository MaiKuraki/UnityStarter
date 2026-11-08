using System;
using System.Threading.Tasks;
using CycloneGames.AIEnvQuery.Core;
using CycloneGames.BehaviorTree.Core;
using CycloneGames.BehaviorTree.Runtime.Integrations.EnvQuery;
using NUnit.Framework;

namespace CycloneGames.BehaviorTree.Integrations.EnvQuery.Tests.Editor
{
    public sealed class EnvQueryBlackboardBridgeTests
    {
        private const int FoundKey = 31;
        private const int ScoreKey = 32;
        private const int PositionKey = 33;
        private const int CountKey = 34;
        private const int PayloadKey = 35;

        [Test]
        public void Constructor_RejectsInvalidArguments()
        {
            var generator = new GridAroundQuerierGenerator(1f, 4f);

            Assert.Throws<ArgumentNullException>(() => new EnvQueryBlackboardBridge(null, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new EnvQueryBlackboardBridge(generator, null, maxCandidates: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new EnvQueryBlackboardBridge(generator, null, resultCapacity: 0));
        }

        [Test]
        public void Run_ReturnsBestCandidatesFirst()
        {
            var generator = new GridAroundQuerierGenerator(spacing: 1f, radius: 4f);
            var tests = new IEnvQueryTest[] { new DistanceTest(range: 8f) };
            using var bridge = new EnvQueryBlackboardBridge(generator, tests, maxCandidates: 64, resultCapacity: 4);

            EnvQueryContext context = ContextAt(0f, 0f, 0f);
            int count = bridge.Run(in context);

            Assert.AreEqual(4, count);
            Assert.AreEqual(count, bridge.Count);
            Assert.IsTrue(bridge.HasResult);

            ReadOnlySpan<EnvQueryPoint> results = bridge.Results;
            for (int i = 1; i < results.Length; i++)
            {
                Assert.GreaterOrEqual(results[i - 1].Score, results[i].Score, "results must be ordered");
            }
        }

        [Test]
        public void Run_WithoutTests_KeepsGeneratorOrder()
        {
            var generator = new GridAroundQuerierGenerator(spacing: 1f, radius: 2f);
            using var bridge = new EnvQueryBlackboardBridge(generator, null, maxCandidates: 32, resultCapacity: 2);

            EnvQueryContext context = ContextAt(0f, 0f, 0f);
            bridge.Run(in context);

            Assert.AreEqual(2, bridge.Count);
            Assert.AreEqual(0f, bridge.Best.Score, 1e-6f);
        }

        [Test]
        public void Run_WithEmptySource_ReportsNoResult()
        {
            var generator = new PointListGenerator(Array.Empty<EnvQueryPoint>());
            using var bridge = new EnvQueryBlackboardBridge(generator, null);
            using var blackboard = new RuntimeBlackboard();

            EnvQueryContext context = ContextAt(0f, 0f, 0f);
            Assert.AreEqual(0, bridge.Run(in context));
            Assert.IsFalse(bridge.HasResult);

            bridge.PublishBest(blackboard, FoundKey, ScoreKey);
            Assert.IsFalse(blackboard.GetBool(FoundKey));
            Assert.AreEqual(0f, blackboard.GetFloat(ScoreKey), 1e-6f);
        }

        [Test]
        public void PublishBest_WritesFlagAndScore()
        {
            var generator = new GridAroundQuerierGenerator(spacing: 1f, radius: 3f);
            var tests = new IEnvQueryTest[] { new DistanceTest(range: 6f) };
            using var bridge = new EnvQueryBlackboardBridge(generator, tests, resultCapacity: 1);
            using var blackboard = new RuntimeBlackboard();

            EnvQueryContext context = ContextAt(0f, 0f, 0f);
            bridge.Run(in context);
            bridge.PublishBest(blackboard, FoundKey, ScoreKey);

            Assert.IsTrue(blackboard.GetBool(FoundKey));
            Assert.Greater(blackboard.GetFloat(ScoreKey), 0f);
        }

        [Test]
        public void PublishBestPosition_WritesFloat3Slot()
        {
            var source = new[] { new EnvQueryPoint(4f, 1f, -2f) };
            var generator = new PointListGenerator(source);
            using var bridge = new EnvQueryBlackboardBridge(generator, null, resultCapacity: 1);
            using var blackboard = new RuntimeBlackboard();

            EnvQueryContext context = ContextAt(0f, 0f, 0f);
            bridge.Run(in context);
            bridge.PublishBestPosition(blackboard, PositionKey);

            BTFloat3 stored = blackboard.GetFloat3(PositionKey, BTFloat3.Zero);
            Assert.AreEqual(4f, stored.X, 1e-5f);
            Assert.AreEqual(1f, stored.Y, 1e-5f);
            Assert.AreEqual(-2f, stored.Z, 1e-5f);
        }

        [Test]
        public void PublishBestPayload_WritesCandidateIdentity()
        {
            var source = new[]
            {
                new EnvQueryPoint(0f, 0f, 3f) { Payload = 77 },
                new EnvQueryPoint(0f, 0f, 1f) { Payload = 88 }
            };
            var generator = new PointListGenerator(source);
            var tests = new IEnvQueryTest[] { new DistanceTest(range: 10f) };
            using var bridge = new EnvQueryBlackboardBridge(generator, tests, resultCapacity: 1);
            using var blackboard = new RuntimeBlackboard();

            EnvQueryContext context = ContextAt(0f, 0f, 0f);
            bridge.Run(in context);
            bridge.PublishBestPayload(blackboard, PayloadKey);

            Assert.AreEqual(88, blackboard.GetInt(PayloadKey));
        }

        [Test]
        public void Filters_RemoveCandidatesBeforeRanking()
        {
            var source = new[]
            {
                new EnvQueryPoint(-1f, 0f, 0f) { Payload = 1 },
                new EnvQueryPoint(5f, 0f, 0f) { Payload = 2 }
            };
            var generator = new PointListGenerator(source);
            var tests = new IEnvQueryTest[] { new DistanceTest(range: 20f) };
            var filters = new IEnvQueryFilter[] { new NonNegativeXFilter() };
            using var bridge = new EnvQueryBlackboardBridge(generator, tests, resultCapacity: 4, filters: filters);
            using var blackboard = new RuntimeBlackboard();

            EnvQueryContext context = ContextAt(0f, 0f, 0f);
            int count = bridge.Run(in context);
            bridge.PublishBestPayload(blackboard, PayloadKey);

            Assert.AreEqual(1, count, "the filtered candidate must not reach the result");
            Assert.AreEqual(2, blackboard.GetInt(PayloadKey));
        }

        [Test]
        public void PublishCount_WritesResultCount()
        {
            var generator = new GridAroundQuerierGenerator(spacing: 1f, radius: 3f);
            using var bridge = new EnvQueryBlackboardBridge(generator, null, resultCapacity: 3);
            using var blackboard = new RuntimeBlackboard();

            EnvQueryContext context = ContextAt(0f, 0f, 0f);
            int expected = bridge.Run(in context);
            bridge.PublishCount(blackboard, CountKey);

            Assert.AreEqual(expected, blackboard.GetInt(CountKey));
        }

        [Test]
        public void RepeatedPublish_WithSameValues_DoesNotBumpStamps()
        {
            var generator = new GridAroundQuerierGenerator(spacing: 1f, radius: 3f);
            using var bridge = new EnvQueryBlackboardBridge(generator, null, resultCapacity: 1);
            using var blackboard = new RuntimeBlackboard();
            EnvQueryContext context = ContextAt(0f, 0f, 0f);

            bridge.Run(in context);
            bridge.PublishBest(blackboard, FoundKey, ScoreKey);

            ulong foundStamp = blackboard.GetStamp(FoundKey);
            ulong scoreStamp = blackboard.GetStamp(ScoreKey);

            bridge.Run(in context);
            bridge.PublishBest(blackboard, FoundKey, ScoreKey);

            Assert.AreEqual(foundStamp, blackboard.GetStamp(FoundKey));
            Assert.AreEqual(scoreStamp, blackboard.GetStamp(ScoreKey));
        }

        [Test]
        public void AfterDispose_CallsThrow()
        {
            var generator = new GridAroundQuerierGenerator(1f, 3f);
            var bridge = new EnvQueryBlackboardBridge(generator, null);
            bridge.Dispose();

            EnvQueryContext context = ContextAt(0f, 0f, 0f);
            Assert.Throws<ObjectDisposedException>(() => bridge.Run(in context));
        }

        [Test]
        public void Run_FromAnotherThread_Throws()
        {
            var generator = new GridAroundQuerierGenerator(1f, 3f);
            using var bridge = new EnvQueryBlackboardBridge(generator, null);
            EnvQueryContext context = ContextAt(0f, 0f, 0f);

            Exception captured = null;
            Task.Run(() =>
            {
                try
                {
                    bridge.Run(in context);
                }
                catch (Exception exception)
                {
                    captured = exception;
                }
            }).GetAwaiter().GetResult();

            Assert.That(captured, Is.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void Fuzz_RandomContextsKeepOrderingAndDeterminism()
        {
            var generator = new GridAroundQuerierGenerator(spacing: 1f, radius: 5f);
            var tests = new IEnvQueryTest[]
            {
                new DistanceTest(range: 10f),
                new FacingTest(weight: 0.5f)
            };
            using var bridge = new EnvQueryBlackboardBridge(generator, tests, maxCandidates: 128, resultCapacity: 4);
            var random = new System.Random(20261108);

            for (int round = 0; round < 3000; round++)
            {
                EnvQueryContext context = ContextAt(
                    (float)(random.NextDouble() * 20d - 10d),
                    0f,
                    (float)(random.NextDouble() * 20d - 10d));

                int count = bridge.Run(in context);
                Assert.AreEqual(count, bridge.Count, "count drifted at round " + round);
                Assert.LessOrEqual(count, bridge.Capacity, "count exceeded capacity at round " + round);

                ReadOnlySpan<EnvQueryPoint> first = bridge.Results;
                for (int i = 1; i < first.Length; i++)
                {
                    Assert.GreaterOrEqual(first[i - 1].Score, first[i].Score, "ordering broke at round " + round);
                }

                if (count > 0)
                {
                    float dx = first[0].X - context.QuerierX;
                    float dz = first[0].Z - context.QuerierZ;
                    Assert.LessOrEqual(dx * dx + dz * dz, 25.001f, "candidate outside the generator radius at round " + round);
                }

                EnvQueryPoint best = bridge.Best;
                bridge.Run(in context);
                Assert.AreEqual(best.X, bridge.Best.X, "run is not deterministic at round " + round);
                Assert.AreEqual(best.Z, bridge.Best.Z, "run is not deterministic at round " + round);
                Assert.AreEqual(best.Score, bridge.Best.Score, "score is not deterministic at round " + round);
            }
        }

        [Test]
        public void RunAndPublish_DoNotRetainHeap()
        {
            var generator = new GridAroundQuerierGenerator(spacing: 1f, radius: 5f);
            var tests = new IEnvQueryTest[] { new DistanceTest(range: 10f) };
            using var bridge = new EnvQueryBlackboardBridge(generator, tests, maxCandidates: 128, resultCapacity: 4);
            using var blackboard = new RuntimeBlackboard();
            EnvQueryContext context = ContextAt(0f, 0f, 0f);

            for (int i = 0; i < 5000; i++)
            {
                Step(bridge, blackboard, in context);
            }

            long before = GC.GetTotalMemory(true);
            for (int i = 0; i < 200000; i++)
            {
                Step(bridge, blackboard, in context);
            }

            long after = GC.GetTotalMemory(true);
            Assert.Less(after - before, 1024 * 1024, "steady-state run and publish must not retain heap");
        }

        private static void Step(EnvQueryBlackboardBridge bridge, RuntimeBlackboard blackboard, in EnvQueryContext context)
        {
            bridge.Run(in context);
            bridge.PublishBest(blackboard, FoundKey, ScoreKey);
            bridge.PublishBestPosition(blackboard, PositionKey);
            bridge.PublishCount(blackboard, CountKey);
        }

        private sealed class NonNegativeXFilter : IEnvQueryFilter
        {
            public bool Accept(in EnvQueryContext context, in EnvQueryPoint point)
            {
                return point.X >= 0f;
            }
        }

        private static EnvQueryContext ContextAt(float x, float y, float z)
        {
            var context = new EnvQueryContext
            {
                QuerierX = x,
                QuerierY = y,
                QuerierZ = z
            };
            context.SetForward(0f, 0f, 1f);
            return context;
        }
    }
}
