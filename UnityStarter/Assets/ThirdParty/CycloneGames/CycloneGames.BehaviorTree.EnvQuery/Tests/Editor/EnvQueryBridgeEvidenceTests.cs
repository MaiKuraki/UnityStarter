using System;
using System.Diagnostics;
using CycloneGames.AIEnvQuery.Core;
using CycloneGames.BehaviorTree.Core;
using CycloneGames.BehaviorTree.Runtime.Integrations.EnvQuery;
using NUnit.Framework;

namespace CycloneGames.BehaviorTree.Integrations.EnvQuery.Tests.Editor
{
    /// <summary>
    /// Long-run evidence for the adapter: per-run cost, heap behaviour and a stable result count over
    /// a minute of continuous queries. Explicit because the soak outlives the default gate.
    /// </summary>
    public sealed class EnvQueryBridgeEvidenceTests
    {
        private const int FoundKey = 41;
        private const int ScoreKey = 42;
        private const int PositionKey = 43;
        private const int CountKey = 44;

        [Test]
        [Explicit("Sixty-second soak for the adapter; run on demand.")]
        public void Soak_SixtySeconds_HoldsCountAndHeap()
        {
            var generator = new GridAroundQuerierGenerator(spacing: 1f, radius: 6f);
            var tests = new IEnvQueryTest[]
            {
                new DistanceTest(range: 12f),
                new FacingTest(weight: 0.5f)
            };

            using var bridge = new EnvQueryBlackboardBridge(generator, tests, maxCandidates: 64, resultCapacity: 4);
            using var blackboard = new RuntimeBlackboard();

            // Time in batches: a per-call Stopwatch would dominate what it measures.
            const int Batch = 512;
            var batchMicroseconds = new double[1 << 20];
            int batchCount = 0;
            long iterations = 0;
            long countMismatches = 0;
            double worstMicroseconds = 0d;

            long heapStart = GC.GetTotalMemory(true);
            long heapBaseline = heapStart;
            double worstHeapDelta = 0d;

            var total = Stopwatch.StartNew();
            var batchWatch = new Stopwatch();
            var heapWatch = Stopwatch.StartNew();
            float phase = 0f;

            while (total.Elapsed < TimeSpan.FromSeconds(60) && batchCount < batchMicroseconds.Length)
            {
                batchWatch.Restart();
                for (int i = 0; i < Batch; i++)
                {
                    EnvQueryContext context = ContextAt(
                        (float)Math.Sin(phase) * 8f,
                        (float)Math.Cos(phase) * 8f);
                    bridge.Run(in context);
                    bridge.PublishBest(blackboard, FoundKey, ScoreKey);
                    bridge.PublishBestPosition(blackboard, PositionKey);
                    bridge.PublishCount(blackboard, CountKey);
                }

                batchWatch.Stop();
                iterations += Batch;
                phase += 0.01f;

                double perCallMicroseconds = batchWatch.Elapsed.TotalMilliseconds * 1000d / Batch;
                batchMicroseconds[batchCount++] = perCallMicroseconds;
                if (perCallMicroseconds > worstMicroseconds)
                {
                    worstMicroseconds = perCallMicroseconds;
                }

                if (bridge.Count != bridge.Capacity)
                {
                    countMismatches++;
                }

                if (heapWatch.Elapsed > TimeSpan.FromSeconds(5))
                {
                    heapWatch.Restart();
                    long now = GC.GetTotalMemory(false);
                    double delta = now - heapBaseline;
                    if (delta > worstHeapDelta)
                    {
                        worstHeapDelta = delta;
                    }
                }
            }

            total.Stop();

            var sorted = new double[batchCount];
            Array.Copy(batchMicroseconds, sorted, batchCount);
            Array.Sort(sorted);

            long heapEnd = GC.GetTotalMemory(true);
            TestContext.WriteLine(
                $"iterations={iterations} batches={batchCount} p50={Percentile(sorted, 0.50):F3} " +
                $"p99={Percentile(sorted, 0.99):F3} max={worstMicroseconds:F3} us/call " +
                $"heapDelta={heapEnd - heapStart} worstSampleDelta={worstHeapDelta:F0}");

            Assert.Greater(iterations, 100_000L, "the soak did not run long enough to be evidence");
            Assert.AreEqual(0L, countMismatches, "result count drifted during the soak");
            Assert.Less(heapEnd - heapStart, 2 * 1024 * 1024, "adapter retained heap across the soak");
            Assert.Less(worstHeapDelta, 4d * 1024 * 1024, "heap sample drifted during the soak");
        }

        private static double Percentile(double[] sorted, double q)
        {
            int index = (int)Math.Min(sorted.Length - 1, Math.Max(0, Math.Round(q * (sorted.Length - 1))));
            return sorted[index];
        }

        private static EnvQueryContext ContextAt(float x, float z)
        {
            var context = new EnvQueryContext
            {
                QuerierX = x,
                QuerierY = 0f,
                QuerierZ = z
            };
            context.SetForward(0f, 0f, 1f);
            return context;
        }
    }
}
