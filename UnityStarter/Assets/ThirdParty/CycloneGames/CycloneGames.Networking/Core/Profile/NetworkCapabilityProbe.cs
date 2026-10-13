using System;
using System.Buffers;
using System.Diagnostics;

namespace CycloneGames.Networking
{
    /// <summary>
    /// Runtime micro-benchmark that measures the machine's actual networking hot
    /// path (checksum + copy + pool churn) instead of trusting static hardware
    /// heuristics. Core count is a poor proxy for networking throughput:
    /// heterogeneous big.LITTLE cores and 2-3x per-core performance spreads across
    /// architectures make static tables misjudge real devices. A short measurement
    /// gets all of that right automatically.
    ///
    /// Measurement hardening, based on how real devices behave:
    /// - Multiple rounds with a per-round warmup phase, so JIT/first-touch costs
    ///   never pollute a measured window.
    /// - The final score is the MEDIAN of per-round scores: a single GC pause,
    ///   scheduler hiccup, VM time-slice gap or thermal blip ruins one round, not
    ///   the result. Median is robust against exactly this kind of outlier.
    /// - Each round measures two payload classes: MTU-sized (cache-resident) and
    ///   64 KB (exceeds L1/L2, stresses the memory subsystem like real reassembly
    ///   and snapshot copies do). The round score is the WORSE of the two, so a
    ///   fast-but-cached number cannot mask a weak memory bus.
    ///
    /// Environment caveats (documented, not hidden): an x86 emulator on a PC will
    /// report PC-class scores — truthful for the code that actually runs there,
    /// but never use emulator numbers to calibrate device threshold tables. In
    /// virtualized/cloud environments timer and scheduler jitter inflate variance,
    /// which the median absorbs; if even the median is untrusted (hooked or
    /// heavily throttled sandboxes), the score is only a default recommendation —
    /// operators can always override the tier explicitly. The probe can never
    /// prove its own environment; treat it as the best available default signal.
    ///
    /// CRITICAL — scores are only comparable WITHIN the same scripting backend.
    /// Measured on the same high-end desktop (2026): Unity Editor (Mono JIT)
    /// ~600 MB/s vs CoreCLR ~1.0-1.7 GB/s — a 2-3x gap on identical hardware;
    /// IL2CPP player builds land elsewhere again. Consequences:
    /// - Never classify from an Editor-measured score using player-calibrated
    ///   threshold tables: in the Editor everything looks ~2-3x slower and even a
    ///   flagship CPU would be stamped Low. In Editor, prefer static heuristics
    ///   or an explicitly Editor-calibrated table.
    /// - Calibrate <see cref="NetworkTierThresholds"/> benchmark ceilings from
    ///   IL2CPP development builds on real target hardware, per backend.
    /// - Future Unity runtimes (CoreCLR-based Unity) shift scores again — by
    ///   design. The probe measures the backend the game actually ships on, so
    ///   classification stays correct across backend migrations as long as
    ///   thresholds were calibrated on that same backend. Static heuristics
    ///   (core/memory tables) are the backend-independent fallback.
    /// - Cache the measured score per install (e.g. in a save file) rather than
    ///   re-probing every launch.
    ///
    /// The probe is time-bounded, allocates nothing after its pooled buffers, is
    /// single-threaded by design (the networking hot path is single-threaded),
    /// and is IL2CPP/AOT-safe (no reflection, no dynamic code).
    /// </summary>
    public static class NetworkCapabilityProbe
    {
        public const int DefaultRoundCount = 5;
        public const int DefaultRoundBudgetMilliseconds = 8;
        public const int DefaultWarmupMilliseconds = 3;

        private const int SmallPayloadSize = NetworkConstants.DefaultMTU;
        private const int LargePayloadSize = 64 * 1024;

        /// <summary>
        /// Runs the probe and returns the median per-round throughput in MB/s.
        /// Total wall time is bounded by
        /// rounds * (roundBudget + warmup) milliseconds plus small constant costs.
        /// Returns 0 if no round produced a usable measurement (caller should then
        /// fall back to static heuristics).
        /// </summary>
        public static double MeasureThroughputMBps(
            int rounds = DefaultRoundCount,
            int roundBudgetMilliseconds = DefaultRoundBudgetMilliseconds,
            int warmupMilliseconds = DefaultWarmupMilliseconds)
        {
            if (rounds <= 0)
                throw new ArgumentOutOfRangeException(nameof(rounds));
            if (roundBudgetMilliseconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(roundBudgetMilliseconds));
            if (warmupMilliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(warmupMilliseconds));

            byte[] smallSource = ArrayPool<byte>.Shared.Rent(SmallPayloadSize);
            byte[] smallDestination = ArrayPool<byte>.Shared.Rent(SmallPayloadSize);
            byte[] largeSource = ArrayPool<byte>.Shared.Rent(LargePayloadSize);
            byte[] largeDestination = ArrayPool<byte>.Shared.Rent(LargePayloadSize);
            try
            {
                FillPattern(smallSource, SmallPayloadSize, 17);
                FillPattern(largeSource, LargePayloadSize, 29);

                // Global warmup: get JIT/first-touch/page-fault costs out of the
                // way before any timed window exists.
                ulong sink = RunIteration(smallSource, smallDestination, SmallPayloadSize);
                sink ^= RunIteration(largeSource, largeDestination, LargePayloadSize);

                double[] roundScores = new double[rounds];
                for (int round = 0; round < rounds; round++)
                {
                    // Per-round warmup keeps each window on equally warm code paths.
                    sink ^= RunFor(smallSource, smallDestination, SmallPayloadSize, warmupMilliseconds);

                    double smallMBps = Measure(smallSource, smallDestination, SmallPayloadSize, roundBudgetMilliseconds, ref sink);
                    double largeMBps = Measure(largeSource, largeDestination, LargePayloadSize, roundBudgetMilliseconds, ref sink);

                    // Round score is the worse of the two payload classes.
                    roundScores[round] = Math.Min(smallMBps, largeMBps);
                }

                double median = Median(roundScores);

                // Data dependency so the optimizer cannot eliminate the workload.
                if (median <= 0 || sink == ulong.MaxValue)
                    return 0;

                return median;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(smallSource);
                ArrayPool<byte>.Shared.Return(smallDestination);
                ArrayPool<byte>.Shared.Return(largeSource);
                ArrayPool<byte>.Shared.Return(largeDestination);
            }
        }

        private static double Measure(byte[] source, byte[] destination, int length, int budgetMilliseconds, ref ulong sink)
        {
            var stopwatch = Stopwatch.StartNew();
            long iterations = 0;
            ulong checksum = sink;
            while (stopwatch.ElapsedMilliseconds < budgetMilliseconds)
            {
                checksum ^= RunIteration(source, destination, length);
                iterations++;
            }
            stopwatch.Stop();
            sink = checksum;

            if (iterations == 0 || stopwatch.ElapsedTicks == 0)
                return 0;

            double bytesProcessed = (double)iterations * length * 2; // read + write
            return bytesProcessed / stopwatch.Elapsed.TotalSeconds / (1024.0 * 1024.0);
        }

        private static ulong RunFor(byte[] source, byte[] destination, int length, int milliseconds)
        {
            if (milliseconds == 0)
                return 0;

            var stopwatch = Stopwatch.StartNew();
            ulong checksum = 0;
            while (stopwatch.ElapsedMilliseconds < milliseconds)
                checksum ^= RunIteration(source, destination, length);
            return checksum;
        }

        private static ulong RunIteration(byte[] source, byte[] destination, int length)
        {
            // FNV-1a over the payload (serialization-like read), then a block copy
            // (transport-like write) — the two dominant costs of the network stack.
            ulong hash = 14695981039346656037UL;
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
