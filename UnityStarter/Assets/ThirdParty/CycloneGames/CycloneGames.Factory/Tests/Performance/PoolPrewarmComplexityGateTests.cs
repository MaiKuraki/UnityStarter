using System;
using System.Diagnostics;
using CycloneGames.Factory.Runtime;
using NUnit.Framework;

namespace CycloneGames.Factory.Tests.Performance
{
    /// <summary>
    /// Complexity gate for bulk prewarm: proves the cost curve is linear rather than quadratic.
    /// <para>
    /// Method:
    /// <list type="number">
    /// <item>Measure prewarm at four sizes on a 2x progression, so a single noisy point cannot decide
    /// the verdict.</item>
    /// <item>Take the minimum of several samples per size. The minimum is the best available estimate of
    /// the algorithmic cost: scheduler preemption and GC pauses can only add time, never remove it.</item>
    /// <item>Fit a least-squares line to ln(time) vs ln(N). The slope is the empirical exponent: about
    /// 1.0 for linear work, about 2.0 for quadratic work. A regression across four points is far more
    /// stable than a single ratio between two points.</item>
    /// </list>
    /// Absolute milliseconds are never asserted: they are machine- and load-dependent. Only the shape of
    /// the curve is gated, and the thresholds sit far enough above the linear band that ordinary
    /// measurement noise cannot produce a false failure while a genuine quadratic term cannot hide below
    /// them.
    /// </para>
    /// </summary>
    public sealed class PoolPrewarmComplexityGateTests
    {
        private static readonly int[] Sizes = { 2048, 4096, 8192, 16384 };

        private const int SAMPLE_COUNT = 5;

        // Linear ~= 1.0, quadratic ~= 2.0.
        private const double MAX_SLOPE = 1.6;

        // Doubling ratio: linear ~= 2.0, quadratic ~= 4.0.
        private const double MAX_DOUBLING_RATIO = 3.0;

        private static int _sink;

        [Test]
        public void Prewarm_LogLogSlope_IsLinear()
        {
            // JIT, list growth and first-touch warmup so the first sample is not dominated by one-time costs.
            RunPrewarm(Sizes[0]);

            double[] times = new double[Sizes.Length];
            for (int i = 0; i < Sizes.Length; i++)
            {
                times[i] = BestOf(Sizes[i]);
                TestContext.WriteLine($"prewarm({Sizes[i]}) = {times[i]:F3} ms (best of {SAMPLE_COUNT})");
            }

            double slope = LogLogSlope(Sizes, times);
            double doublingRatio = times[Sizes.Length - 1] / times[Sizes.Length - 2];

            TestContext.WriteLine($"log-log slope = {slope:F3} (linear ~= 1.0, quadratic ~= 2.0)");
            TestContext.WriteLine($"top-end doubling ratio = {doublingRatio:F3} (linear ~= 2.0, quadratic ~= 4.0)");

            Assert.That(times[Sizes.Length - 1], Is.GreaterThan(0.5),
                "The largest prewarm sample is too small to be measurable; raise the sizes before trusting the slope.");

            Assert.That(slope, Is.LessThan(MAX_SLOPE),
                $"Prewarm log-log slope {slope:F3} indicates super-linear scaling " +
                $"(linear ~= 1.0, quadratic ~= 2.0).");

            Assert.That(doublingRatio, Is.LessThan(MAX_DOUBLING_RATIO),
                $"Prewarm doubling ratio {doublingRatio:F3} indicates super-linear scaling " +
                $"(linear ~= 2.0, quadratic ~= 4.0).");
        }

        private static double BestOf(int count)
        {
            double best = double.MaxValue;
            for (int sample = 0; sample < SAMPLE_COUNT; sample++)
            {
                var stopwatch = Stopwatch.StartNew();
                RunPrewarm(count);
                stopwatch.Stop();
                best = Math.Min(best, stopwatch.Elapsed.TotalMilliseconds);
            }

            return best;
        }

        private static void RunPrewarm(int count)
        {
            var factory = new PerfPoolableFactory();
            using var pool = new ObjectPool<int, PerfPoolable>(
                factory,
                new PoolCapacitySettings(softCapacity: 0, hardCapacity: -1));

            pool.Prewarm(count);
            _sink = pool.CountInactive;
        }

        private static double LogLogSlope(int[] sizes, double[] times)
        {
            int n = sizes.Length;
            double sumX = 0.0;
            double sumY = 0.0;
            for (int i = 0; i < n; i++)
            {
                sumX += Math.Log(sizes[i]);
                sumY += Math.Log(times[i]);
            }

            double meanX = sumX / n;
            double meanY = sumY / n;

            double covariance = 0.0;
            double variance = 0.0;
            for (int i = 0; i < n; i++)
            {
                double dx = Math.Log(sizes[i]) - meanX;
                covariance += dx * (Math.Log(times[i]) - meanY);
                variance += dx * dx;
            }

            return variance <= 0.0 ? 0.0 : covariance / variance;
        }
    }
}
