using System;
using System.Buffers;
using System.Diagnostics;

namespace CycloneGames.Networking
{
    /// <summary>
    /// Measures networking hot-path throughput (checksum, copy and pooled buffer
    /// churn) with fixed-size workloads, high-resolution Stopwatch timing and a
    /// per-iteration checksum seed that keeps the FNV loop from being folded into
    /// a constant. The score is the median of per-round scores; each round uses
    /// the worse of the MTU-sized and 64 KB payload classes.
    ///
    /// Measure returns a NetworkCapabilityScore carrying the ParametersId of the
    /// configuration that produced it. Classifiers reject scores whose
    /// ParametersId does not match NetworkTierThresholds.ExpectedProbeParametersId.
    ///
    /// Scores are comparable only within the same scripting backend. Calibrate
    /// threshold tables on target hardware and cache the result per install.
    /// The inner loop is single-threaded, time-bounded and allocation-free; each
    /// invocation allocates the per-round score array and rents four buffers.
    /// </summary>
    public static class NetworkCapabilityProbe
    {
        public const int DefaultRoundCount = 5;
        public const int DefaultRoundBudgetMilliseconds = 8;
        public const int DefaultWarmupMilliseconds = 3;

        private const int DefaultMinIterationsPerWindow = 64;

        // Warm both payload classes past tiered-JIT promotion, then score a
        // fixed number of iterations per window so the result does not depend on
        // the caller's time budget.
        private const int GlobalWarmupSmallIterations = 20000;
        private const int GlobalWarmupLargeIterations = 512;
        private const int SmallWindowIterationMultiplier = 128;
        private const int LargeWindowIterationMultiplier = 4;
        private const int ProbeWarmupRounds = 2;
        private const int MinScoredRounds = 5;

        /// <summary>
        /// Compatibility overload. Prefer <see cref="Measure(in NetworkProbeParameters)"/>,
        /// which returns a score carrying its parameter identity.
        /// </summary>
        public static double MeasureThroughputMBps(
            int rounds = DefaultRoundCount,
            int roundBudgetMilliseconds = DefaultRoundBudgetMilliseconds,
            int warmupMilliseconds = DefaultWarmupMilliseconds)
        {
            NetworkProbeParameters parameters = new NetworkProbeParameters(
                rounds,
                roundBudgetMilliseconds,
                warmupMilliseconds,
                DefaultMinIterationsPerWindow,
                NetworkConstants.DefaultMTU,
                64 * 1024);

            return Measure(parameters).ThroughputMBps;
        }

        public static NetworkCapabilityScore Measure(in NetworkProbeParameters parameters)
        {
            byte[] smallSource = ArrayPool<byte>.Shared.Rent(parameters.SmallPayloadSize);
            byte[] smallDestination = ArrayPool<byte>.Shared.Rent(parameters.SmallPayloadSize);
            byte[] largeSource = ArrayPool<byte>.Shared.Rent(parameters.LargePayloadSize);
            byte[] largeDestination = ArrayPool<byte>.Shared.Rent(parameters.LargePayloadSize);

            try
            {
                FillPattern(smallSource, parameters.SmallPayloadSize, 17);
                FillPattern(largeSource, parameters.LargePayloadSize, 29);

                // Global warmup: JIT tier promotion, first-touch and page-fault
                // costs for BOTH payload classes. The iteration counts are fixed,
                // so they do not depend on the caller's budget arguments.
                ulong sink = 0UL;
                for (int i = 0; i < GlobalWarmupSmallIterations; i++)
                {
                    sink ^= RunIteration(smallSource, smallDestination,
                        parameters.SmallPayloadSize, 0x9E3779B97F4A7C15UL ^ (ulong)i);
                }
                for (int i = 0; i < GlobalWarmupLargeIterations; i++)
                {
                    sink ^= RunIteration(largeSource, largeDestination,
                        parameters.LargePayloadSize, 0xC2B2AE3D27D4EB4FUL ^ (ulong)i);
                }

                // Discard complete rounds so MeasureWindow/RunFor are already on the
                // optimised JIT tier and the CPU clock is steady before any scored
                // sample. Without this, a 1-round probe measures tier-0 code while a
                // 9-round probe measures tier-1 code on the very same machine.
                for (int d = 0; d < ProbeWarmupRounds; d++)
                {
                    ulong dSeed = 0xD1C0FFEEUL ^ (ulong)d;
                    sink ^= RunFor(smallSource, smallDestination,
                        parameters.SmallPayloadSize, parameters.WarmupMilliseconds, dSeed);
                    sink ^= RunFor(largeSource, largeDestination,
                        parameters.LargePayloadSize, parameters.WarmupMilliseconds,
                        dSeed ^ 0x5A5A5A5AUL);
                    MeasureWindow(smallSource, smallDestination,
                        parameters.SmallPayloadSize, parameters,
                        dSeed ^ 0x1111111111111111UL, ref sink, out _);
                    MeasureWindow(largeSource, largeDestination,
                        parameters.LargePayloadSize, parameters,
                        dSeed ^ 0x2222222222222222UL, ref sink, out _);
                }

                int scoredRounds = parameters.Rounds < MinScoredRounds
                    ? MinScoredRounds
                    : parameters.Rounds;
                double[] roundScores = new double[scoredRounds];
                long totalIterations = 0L;
                long startedTicks = Stopwatch.GetTimestamp();

                for (int round = 0; round < scoredRounds; round++)
                {
                    ulong roundSeed = unchecked((ulong)round * 0x100000001B3UL + 1UL);

                    // Per-round warmup covers both payload classes.
                    sink ^= RunFor(smallSource, smallDestination,
                        parameters.SmallPayloadSize, parameters.WarmupMilliseconds, roundSeed);
                    sink ^= RunFor(largeSource, largeDestination,
                        parameters.LargePayloadSize, parameters.WarmupMilliseconds,
                        roundSeed ^ 0x5A5A5A5AUL);

                    double smallMBps = MeasureWindow(smallSource, smallDestination,
                        parameters.SmallPayloadSize, parameters,
                        roundSeed ^ 0x1111111111111111UL, ref sink, out long smallIterations);
                    double largeMBps = MeasureWindow(largeSource, largeDestination,
                        parameters.LargePayloadSize, parameters,
                        roundSeed ^ 0x2222222222222222UL, ref sink, out long largeIterations);

                    roundScores[round] = Math.Min(smallMBps, largeMBps);
                    totalIterations += smallIterations + largeIterations;
                }

                long elapsedTicks = Stopwatch.GetTimestamp() - startedTicks;
                double median = Median(roundScores);

                // Data dependency so the workload cannot be eliminated by the optimizer.
                if (median <= 0d || sink == ulong.MaxValue)
                    median = 0d;

                return new NetworkCapabilityScore(
                    median, parameters.ParametersId, scoredRounds, elapsedTicks, totalIterations);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(smallSource);
                ArrayPool<byte>.Shared.Return(smallDestination);
                ArrayPool<byte>.Shared.Return(largeSource);
                ArrayPool<byte>.Shared.Return(largeDestination);
            }
        }

        private static double MeasureWindow(
            byte[] source,
            byte[] destination,
            int length,
            in NetworkProbeParameters parameters,
            ulong seed,
            ref ulong sink,
            out long iterations)
        {
            long budgetTicks = (long)(parameters.RoundBudgetMilliseconds
                                      * (double)Stopwatch.Frequency / 1000d);
            if (budgetTicks <= 0L)
                budgetTicks = 1L;

            // Fixed workload per payload class. Both short and long budget
            // arguments therefore measure the same number of iterations on the
            // same (already promoted) JIT tier; only the lower time bound differs.
            long targetIterations = length >= parameters.LargePayloadSize
                ? (long)parameters.MinIterationsPerWindow * LargeWindowIterationMultiplier
                : (long)parameters.MinIterationsPerWindow * SmallWindowIterationMultiplier;
            if (targetIterations < parameters.MinIterationsPerWindow)
                targetIterations = parameters.MinIterationsPerWindow;

            long start = Stopwatch.GetTimestamp();
            ulong checksum = sink;
            long count = 0L;

            while (true)
            {
                checksum ^= RunIteration(source, destination, length, seed ^ (ulong)count);
                count++;

                if (count >= targetIterations
                    && Stopwatch.GetTimestamp() - start >= budgetTicks)
                {
                    break;
                }
            }

            long elapsedTicks = Stopwatch.GetTimestamp() - start;
            sink = checksum;
            iterations = count;

            double seconds = elapsedTicks / (double)Stopwatch.Frequency;
            if (seconds <= 0d)
                return 0d;

            return (double)count * length * 2d / seconds / (1024d * 1024d);
        }

        private static ulong RunFor(
            byte[] source, byte[] destination, int length, int milliseconds, ulong seed)
        {
            if (milliseconds == 0)
                return 0UL;

            long endTicks = Stopwatch.GetTimestamp()
                            + (long)(milliseconds * (double)Stopwatch.Frequency / 1000d);
            ulong checksum = 0UL;
            ulong count = 0UL;

            do
            {
                checksum ^= RunIteration(source, destination, length, seed ^ count);
                count++;
            }
            while (Stopwatch.GetTimestamp() < endTicks);

            return checksum;
        }

        private static ulong RunIteration(byte[] source, byte[] destination, int length, ulong seed)
        {
            // FNV-1a over the payload (serialization-like read), then a block copy
            // (transport-like write). The seed varies per iteration so the loop is
            // not loop-invariant and cannot be hoisted out by the JIT.
            ulong hash = 14695981039346656037UL ^ seed;
            for (int i = 0; i < length; i++)
            {
                hash ^= source[i];
                hash *= 1099511628211UL;
            }

            Buffer.BlockCopy(source, 0, destination, 0, length);
            return hash;
        }

        private static void FillPattern(byte[] buffer, int length, byte seed)
        {
            for (int i = 0; i < length; i++)
                buffer[i] = (byte)(i * 31 + seed);
        }

        private static double Median(double[] values)
        {
            Array.Sort(values);
            int mid = values.Length / 2;
            if ((values.Length & 1) != 0)
                return values[mid];
            return (values[mid - 1] + values[mid]) * 0.5;
        }
    }
}
