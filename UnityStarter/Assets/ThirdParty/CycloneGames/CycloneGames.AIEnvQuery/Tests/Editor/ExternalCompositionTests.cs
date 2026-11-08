using System;
using CycloneGames.AIEnvQuery.Core;
using NUnit.Framework;

namespace CycloneGames.AIEnvQuery.Tests.Editor
{
    /// <summary>
    /// Shows that the two cost-control concerns left out of the runner can be composed outside it:
    /// score shaping is a test, and time slicing is a work item.
    /// </summary>
    public sealed class ExternalCompositionTests
    {
        [Test]
        public void ScoreShaping_IsATestNotARunnerFeature()
        {
            // A curve that prefers the middle of the range; the runner has no notion of it.
            var shaped = new MidRangeTest(range: 10f);
            var linear = new DistanceTest(range: 10f);
            var source = new[]
            {
                new EnvQueryPoint(0f, 0f, 1f) { Payload = 1 },
                new EnvQueryPoint(0f, 0f, 5f) { Payload = 5 }
            };
            var generator = new PointListGenerator(source);
            using var runner = new EnvQueryRunner(8);
            var results = new EnvQueryPoint[2];
            EnvQueryContext context = ContextAt();

            runner.Run(generator, new IEnvQueryTest[] { linear }, in context, results);
            Assert.AreEqual(1, results[0].Payload, "linear distance prefers the nearest point");

            runner.Run(generator, new IEnvQueryTest[] { shaped }, in context, results);
            Assert.AreEqual(5, results[0].Payload, "the shaped test prefers the middle of the range");
        }

        [Test]
        public void TimeSlicing_IsAWorkItemThatStepsThePublicSeams()
        {
            var generator = new GridAroundQuerierGenerator(spacing: 1f, radius: 6f);
            var tests = new IEnvQueryTest[] { new DistanceTest(range: 12f) };
            EnvQueryContext context = ContextAt();

            using var runner = new EnvQueryRunner(256);
            var expected = new EnvQueryPoint[1];
            runner.Run(generator, tests, in context, expected);

            var item = new ChunkedQueryStepper(generator, tests, in context, maxCandidates: 256, chunkSize: 8);

            int guard = 0;
            while (!item.IsComplete && guard++ < 1000)
            {
                item.Step();
            }

            Assert.IsTrue(item.IsComplete, "the stepper never reached the last candidate");
            Assert.Greater(item.Steps, 1, "the query must span more than one step to be time sliced");
            Assert.AreEqual(expected[0].X, item.Best.X, 1e-4f, "sliced best X differs from a single run");
            Assert.AreEqual(expected[0].Z, item.Best.Z, 1e-4f, "sliced best Z differs from a single run");
            Assert.AreEqual(expected[0].Score, item.Best.Score, 1e-4f, "sliced best score differs from a single run");
        }

        private static EnvQueryContext ContextAt()
        {
            var context = new EnvQueryContext { QuerierX = 0f, QuerierY = 0f, QuerierZ = 0f };
            context.SetForward(0f, 0f, 1f);
            return context;
        }

        /// <summary>Peak at half the range, zero at both ends.</summary>
        private sealed class MidRangeTest : IEnvQueryTest
        {
            private readonly float _range;

            public MidRangeTest(float range)
            {
                _range = range;
            }

            public float Weight => 1f;

            public void Score(in EnvQueryContext context, ref EnvQueryPoint point)
            {
                float dx = point.X - context.QuerierX;
                float dz = point.Z - context.QuerierZ;
                float normalized = MathF.Sqrt(dx * dx + dz * dz) / _range;
                if (normalized > 1f)
                {
                    normalized = 1f;
                }

                point.Score += 1f - MathF.Abs(normalized - 0.5f) * 2f;
            }
        }

        /// <summary>
        /// Drives the public generator and test seams directly, a fixed number of candidates per step.
        /// The runner is not used, which is what makes the query time sliceable from the outside; a
        /// scheduler such as CycloneGames.Scheduling only has to call <see cref="Step"/>.
        /// </summary>
        private sealed class ChunkedQueryStepper
        {
            private readonly EnvQueryPoint[] _candidates;
            private readonly IEnvQueryTest[] _tests;
            private readonly EnvQueryContext _context;
            private readonly int _chunkSize;
            private readonly int _count;
            private int _index;
            private bool _hasBest;

            public ChunkedQueryStepper(
                IEnvQueryGenerator generator,
                IEnvQueryTest[] tests,
                in EnvQueryContext context,
                int maxCandidates,
                int chunkSize)
            {
                _candidates = new EnvQueryPoint[maxCandidates];
                _tests = tests;
                _context = context;
                _chunkSize = chunkSize;
                _count = generator.Generate(in _context, _candidates);
            }

            public EnvQueryPoint Best { get; private set; }
            public int Steps { get; private set; }

            public bool IsComplete => _index >= _count;

            public void Step()
            {
                Steps++;
                int end = Math.Min(_index + _chunkSize, _count);

                for (; _index < end; _index++)
                {
                    for (int i = 0; i < _tests.Length; i++)
                    {
                        EnvQueryPoint point = _candidates[_index];
                        point.Score = 0f;
                        _tests[i].Score(in _context, ref point);
                        _candidates[_index].Score += point.Score * _tests[i].Weight;
                    }

                    if (!_hasBest || _candidates[_index].Score > Best.Score)
                    {
                        Best = _candidates[_index];
                        _hasBest = true;
                    }
                }

            }
        }
    }
}
