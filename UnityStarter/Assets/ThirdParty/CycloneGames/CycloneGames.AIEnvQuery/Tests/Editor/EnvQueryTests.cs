using System;
using CycloneGames.AIEnvQuery.Core;
using NUnit.Framework;

namespace CycloneGames.AIEnvQuery.Tests.Editor
{
    public sealed class EnvQueryTests
    {
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

        [Test]
        public void GridGenerator_CoversRadiusWithoutDuplicates()
        {
            var generator = new GridAroundQuerierGenerator(spacing: 2f, radius: 6f);
            var points = new EnvQueryPoint[512];
            EnvQueryContext context = ContextAt(10f, 0f, 10f);

            int count = generator.Generate(in context, points);

            // spacing 2 inside radius 6 covers every lattice point with i^2 + j^2 <= 9.
            Assert.AreEqual(29, count);

            var seen = new System.Collections.Generic.HashSet<(float, float)>();
            for (int i = 0; i < count; i++)
            {
                float dx = points[i].X - context.QuerierX;
                float dz = points[i].Z - context.QuerierZ;
                Assert.LessOrEqual(dx * dx + dz * dz, 36.0001f, "candidate outside the radius at " + i);
                Assert.IsTrue(seen.Add((points[i].X, points[i].Z)), "duplicate candidate at " + i);
            }
        }

        [Test]
        public void GridGenerator_RespectsDestinationCapacity()
        {
            var generator = new GridAroundQuerierGenerator(spacing: 1f, radius: 20f);
            var points = new EnvQueryPoint[16];

            EnvQueryContext context = ContextAt(0f, 0f, 0f);
            int count = generator.Generate(in context, points);

            Assert.AreEqual(16, count);
        }

        [Test]
        public void PointListGenerator_FiltersByDistance()
        {
            var source = new[]
            {
                new EnvQueryPoint(0f, 0f, 1f),
                new EnvQueryPoint(0f, 0f, 50f)
            };
            var generator = new PointListGenerator(source, maximumDistance: 10f);
            var points = new EnvQueryPoint[8];

            EnvQueryContext context = ContextAt(0f, 0f, 0f);
            int count = generator.Generate(in context, points);

            Assert.AreEqual(1, count);
            Assert.AreEqual(1f, points[0].Z, 1e-4f);
        }

        [Test]
        public void DistanceTest_PrefersCloserByDefault()
        {
            var test = new DistanceTest(range: 10f);
            EnvQueryContext context = ContextAt(0f, 0f, 0f);

            var near = new EnvQueryPoint(0f, 0f, 1f);
            var far = new EnvQueryPoint(0f, 0f, 9f);
            test.Score(in context, ref near);
            test.Score(in context, ref far);

            Assert.Greater(near.Score, far.Score);
        }

        [Test]
        public void DistanceTest_CanPreferFarther()
        {
            var test = new DistanceTest(range: 10f, preferFarther: true);
            EnvQueryContext context = ContextAt(0f, 0f, 0f);

            var near = new EnvQueryPoint(0f, 0f, 1f);
            var far = new EnvQueryPoint(0f, 0f, 9f);
            test.Score(in context, ref near);
            test.Score(in context, ref far);

            Assert.Greater(far.Score, near.Score);
        }

        [Test]
        public void FacingTest_PrefersForwardCandidates()
        {
            var test = new FacingTest();
            EnvQueryContext context = ContextAt(0f, 0f, 0f);

            var ahead = new EnvQueryPoint(0f, 0f, 5f);
            var behind = new EnvQueryPoint(0f, 0f, -5f);
            test.Score(in context, ref ahead);
            test.Score(in context, ref behind);

            Assert.Greater(ahead.Score, behind.Score);
        }

        [Test]
        public void LineOfSightTest_UsesTheHostProbe()
        {
            var test = new LineOfSightTest((fx, fy, fz, tx, ty, tz) => tx > 0f);
            EnvQueryContext context = ContextAt(0f, 0f, 0f);

            var clear = new EnvQueryPoint(1f, 0f, 0f);
            var blocked = new EnvQueryPoint(-1f, 0f, 0f);
            test.Score(in context, ref clear);
            test.Score(in context, ref blocked);

            Assert.AreEqual(1f, clear.Score, 1e-5f);
            Assert.AreEqual(0f, blocked.Score, 1e-5f);
        }

        [Test]
        public void Runner_RanksByWeightedScore()
        {
            var runner = new EnvQueryRunner(maxCandidates: 64);
            var generator = new GridAroundQuerierGenerator(spacing: 1f, radius: 4f);
            var tests = new IEnvQueryTest[]
            {
                new DistanceTest(range: 10f, weight: 1f),
                new FacingTest(weight: 0.5f)
            };
            var results = new EnvQueryPoint[4];

            int count = runner.Run(generator, tests, ContextAt(0f, 0f, 0f), results);

            Assert.AreEqual(4, count);
            for (int i = 1; i < count; i++)
            {
                Assert.GreaterOrEqual(results[i - 1].Score, results[i].Score, "results must be sorted");
            }

            // The best candidate sits in front of the querier, not behind it.
            Assert.Greater(results[0].Z, 0f);
        }

        [Test]
        public void Runner_IsDeterministicForRepeatedRuns()
        {
            var runner = new EnvQueryRunner(maxCandidates: 64);
            var generator = new GridAroundQuerierGenerator(spacing: 1f, radius: 4f);
            var tests = new IEnvQueryTest[] { new FacingTest() };
            var first = new EnvQueryPoint[8];
            var second = new EnvQueryPoint[8];
            EnvQueryContext context = ContextAt(0f, 0f, 0f);

            runner.Run(generator, tests, in context, first);
            runner.Run(generator, tests, in context, second);

            for (int i = 0; i < first.Length; i++)
            {
                Assert.AreEqual(first[i].X, second[i].X);
                Assert.AreEqual(first[i].Z, second[i].Z);
                Assert.AreEqual(first[i].Score, second[i].Score);
            }
        }

        [Test]
        public void Runner_WithoutTestsKeepsGeneratorOrder()
        {
            var runner = new EnvQueryRunner(maxCandidates: 16);
            var generator = new GridAroundQuerierGenerator(spacing: 1f, radius: 2f);
            var results = new EnvQueryPoint[3];

            int count = runner.Run(generator, null, ContextAt(0f, 0f, 0f), results);

            Assert.AreEqual(3, count);
            Assert.AreEqual(0f, results[0].Score);
        }

        [Test]
        public void Payload_SurvivesScoringAndOrdering()
        {
            var source = new[]
            {
                new EnvQueryPoint(0f, 0f, 2f) { Payload = 100 },
                new EnvQueryPoint(0f, 0f, 1f) { Payload = 200 },
                new EnvQueryPoint(0f, 0f, 3f) { Payload = 300 }
            };
            var generator = new PointListGenerator(source);
            using var runner = new EnvQueryRunner(8);
            var results = new EnvQueryPoint[2];
            EnvQueryContext context = ContextAt(0f, 0f, 0f);

            runner.Run(generator, new IEnvQueryTest[] { new DistanceTest(range: 10f) }, in context, results);

            Assert.AreEqual(200, results[0].Payload, "the nearest candidate must keep its payload");
            Assert.AreEqual(100, results[1].Payload);
        }

        [Test]
        public void Filter_RemovesCandidatesBeforeScoring()
        {
            var generator = new GridAroundQuerierGenerator(spacing: 1f, radius: 4f);
            var filter = new HalfPlaneFilter(0f);
            using var runner = new EnvQueryRunner(256);
            var results = new EnvQueryPoint[256];
            EnvQueryContext context = ContextAt(0f, 0f, 0f);

            int all = runner.Run(generator, (IEnvQueryTest[])null, in context, results);
            int kept = runner.Run(generator, new IEnvQueryFilter[] { filter }, null, in context, results);

            Assert.Greater(all, 0);
            Assert.Less(kept, all, "the filter must remove candidates");
            for (int i = 0; i < kept; i++)
            {
                Assert.GreaterOrEqual(results[i].X, 0f, "a rejected candidate survived at " + i);
            }
        }

        [Test]
        public void Filter_KeepsGeneratorOrder()
        {
            var source = new[]
            {
                new EnvQueryPoint(0f, 0f, 0f) { Payload = 0 },
                new EnvQueryPoint(1f, 0f, 0f) { Payload = 1 },
                new EnvQueryPoint(2f, 0f, 0f) { Payload = 2 },
                new EnvQueryPoint(3f, 0f, 0f) { Payload = 3 }
            };
            var generator = new PointListGenerator(source);
            var filter = new PayloadParityFilter(even: false);
            using var runner = new EnvQueryRunner(8);
            var results = new EnvQueryPoint[8];
            EnvQueryContext context = ContextAt(0f, 0f, 0f);

            int count = runner.Run(generator, new IEnvQueryFilter[] { filter }, null, in context, results);

            Assert.AreEqual(2, count);
            Assert.AreEqual(1, results[0].Payload);
            Assert.AreEqual(3, results[1].Payload);
        }

        [Test]
        public void ContextUserData_IsVisibleToCustomTests()
        {
            var marker = new Marker { Value = 0.25f };
            var generator = new PointListGenerator(new[] { new EnvQueryPoint(0f, 0f, 1f) });
            var test = new UserDataTest();
            using var runner = new EnvQueryRunner(4);
            var results = new EnvQueryPoint[1];

            EnvQueryContext context = ContextAt(0f, 0f, 0f);
            context.UserData = marker;
            runner.Run(generator, new IEnvQueryTest[] { test }, in context, results);

            Assert.AreEqual(1, test.Invocations);
            Assert.AreEqual(0.25f, results[0].Score, 1e-5f);

            context.UserData = null;
            runner.Run(generator, new IEnvQueryTest[] { test }, in context, results);
            Assert.AreEqual(0f, results[0].Score, 1e-5f);
        }

        [Test]
        public void Runner_DoesNotRetainHeap()
        {
            var runner = new EnvQueryRunner(maxCandidates: 256);
            var generator = new GridAroundQuerierGenerator(spacing: 1f, radius: 8f);
            var tests = new IEnvQueryTest[]
            {
                new DistanceTest(range: 12f),
                new FacingTest(),
                new LineOfSightTest((fx, fy, fz, tx, ty, tz) => true)
            };
            var results = new EnvQueryPoint[8];

            for (int i = 0; i < 1000; i++)
            {
                runner.Run(generator, tests, ContextAt(0f, 0f, 0f), results);
            }

            long before = GC.GetTotalMemory(true);
            for (int i = 0; i < 100000; i++)
            {
                runner.Run(generator, tests, ContextAt(0f, 0f, 0f), results);
            }

            long after = GC.GetTotalMemory(true);
            Assert.Less(after - before, 1024 * 1024, "a steady query run must not retain heap");
        }
        private sealed class HalfPlaneFilter : IEnvQueryFilter
        {
            private readonly float _minimumX;

            public HalfPlaneFilter(float minimumX)
            {
                _minimumX = minimumX;
            }

            public bool Accept(in EnvQueryContext context, in EnvQueryPoint point)
            {
                return point.X >= _minimumX;
            }
        }

        private sealed class PayloadParityFilter : IEnvQueryFilter
        {
            private readonly bool _even;

            public PayloadParityFilter(bool even)
            {
                _even = even;
            }

            public bool Accept(in EnvQueryContext context, in EnvQueryPoint point)
            {
                return (point.Payload & 1) == (_even ? 0 : 1);
            }
        }

        private sealed class Marker
        {
            public float Value;
        }

        private sealed class UserDataTest : IEnvQueryTest
        {
            public int Invocations;
            public float Weight => 1f;

            public void Score(in EnvQueryContext context, ref EnvQueryPoint point)
            {
                Invocations++;
                if (context.UserData is Marker marker)
                {
                    point.Score += marker.Value;
                }
            }
        }
    }
}
