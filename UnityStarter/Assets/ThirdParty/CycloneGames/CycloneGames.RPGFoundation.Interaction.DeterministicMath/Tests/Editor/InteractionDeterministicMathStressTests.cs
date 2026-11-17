using System;
using System.Diagnostics;
using CycloneGames.DeterministicMath;
using CycloneGames.RPGFoundation.Interaction.Core;
using CycloneGames.RPGFoundation.Interaction.Integrations.DeterministicMath;
using NUnit.Framework;

namespace CycloneGames.RPGFoundation.Interaction.Tests.Editor.Integrations.DeterministicMath
{
    /// <summary>
    /// Stress and determinism coverage for the fixed-point authority. The deterministic package
    /// exists so a server can replay a request stream and reach the same verdict on every host, so
    /// the strongest assertion here is not throughput but bit-identical verdicts across instances.
    /// </summary>
    public sealed class InteractionDeterministicMathStressTests
    {
        private const int WorldId = 21;
        private static readonly ulong[] Seeds = { 0UL, 1UL, 42UL, 1337UL, 3735928559UL };

        [Test]
        public void Authority_ThroughputAcrossScalesAndSeeds()
        {
            var csv = new StressCsvWriter("detmath_throughput.csv",
                "targets,seed,iterations,ops_per_s,p50_us,p99_us,accepted,rejected");

            try
            {
                foreach (int targets in new[] { 1_000, 5_000, 10_000 })
                {
                    foreach (ulong seed in Seeds)
                    {
                        RunThroughput(csv, targets, seed);
                    }
                }
            }
            finally
            {
                csv.Dispose();
            }
        }

        [Test]
        public void Authority_TwoInstancesProduceIdenticalVerdicts()
        {
            // Two independently constructed services fed the same request stream must agree on
            // every verdict and every rejection reason. This is the property a replay server needs.
            var csv = new StressCsvWriter("detmath_determinism.csv",
                "seed,requests,divergences,accepted,rejected");
            try
            {
                foreach (ulong seed in Seeds)
                {
                    var random = new DeterministicRng(seed);
                    var first = CreateAuthority();
                    var second = CreateAuthority();

                    RegisterTargets(first, 256);
                    RegisterTargets(second, 256);

                    int divergences = 0;
                    long accepted = 0;
                    long rejected = 0;
                    int tick = 1_000;

                    for (int i = 1; i <= 20_000; i++)
                    {
                        tick += random.NextInt(3);
                        var request = new InteractionRequest(
                            i,
                            (ulong)(1 + random.NextInt(64)),
                            (ulong)(1_000 + random.NextInt(256)),
                            "open",
                            tick,
                            WorldId);

                        var position = new FPVector3(FPInt64.FromInt(random.NextInt(8)), FPInt64.Zero, FPInt64.FromInt(random.NextInt(8)));

                        InteractionValidationResult a = first.ValidateRequest(request, position, tick);
                        InteractionValidationResult b = second.ValidateRequest(request, position, tick);

                        if (a.IsAccepted != b.IsAccepted || a.Failure != b.Failure)
                        {
                            divergences++;
                        }

                        if (a.IsAccepted) accepted++;
                        else rejected++;
                    }

                    csv.Row(
                        StressCsvWriter.Int((long)seed),
                        StressCsvWriter.Int(20_000),
                        StressCsvWriter.Int(divergences),
                        StressCsvWriter.Int(accepted),
                        StressCsvWriter.Int(rejected));

                    Assert.That(divergences, Is.Zero,
                        "two instances diverged on " + divergences + " verdicts for seed " + seed);
                }
            }
            finally
            {
                csv.Dispose();
            }
        }

        [Test]
        public void Authority_SoakStaysBounded()
        {
            var csv = new StressCsvWriter("detmath_soak.csv",
                "window,ops_per_s,p50_us,p99_us,history,rate_windows,targets");
            var summary = new StressCsvWriter("detmath_soak_summary.csv",
                "windows,elapsed_s,first_ops,last_ops,decay,history_end,queue_owners");

            try
            {
                var authority = CreateAuthority();
                RegisterTargets(authority, 1_000);

                const int Windows = 10;
                const int OpsPerWindow = 200_000;
                int requestId = 0;
                int tick = 1_000;
                double firstOps = 0;
                double lastOps = 0;
                long startAll = Stopwatch.GetTimestamp();

                for (int w = 0; w < Windows; w++)
                {
                    var histogram = new LatencyHistogram(10L, 8192);
                    long start = Stopwatch.GetTimestamp();

                    for (int i = 0; i < OpsPerWindow; i++)
                    {
                        requestId++;
                        if ((i & 0xFF) == 0) tick++;

                        var request = new InteractionRequest(requestId, (ulong)(1 + (i % 32)), (ulong)(1_000 + (i % 1_000)), "open", tick, WorldId);
                        long opStart = Stopwatch.GetTimestamp();
                        authority.ValidateRequest(request, FPVector3.Zero, tick);
                        histogram.AddTicks(Stopwatch.GetTimestamp() - opStart);
                    }

                    double elapsedNs = LatencyHistogram.TicksToNs(Stopwatch.GetTimestamp() - start);
                    double ops = elapsedNs <= 0 ? 0 : OpsPerWindow / (elapsedNs / 1_000_000_000.0);
                    if (w == 0) firstOps = ops;
                    lastOps = ops;

                    csv.Row(
                        StressCsvWriter.Int(w + 1),
                        StressCsvWriter.Num(ops, 0),
                        StressCsvWriter.Num(histogram.PercentileUs(0.50), 3),
                        StressCsvWriter.Num(histogram.PercentileUs(0.99), 3),
                        StressCsvWriter.Int(authority.Metrics.GetSnapshot().RejectedRequests),
                        StressCsvWriter.Int(authority.RegisteredTargetCount),
                        StressCsvWriter.Int(authority.QueueOwnerCount));
                }

                double elapsedSeconds = LatencyHistogram.TicksToNs(Stopwatch.GetTimestamp() - startAll) / 1_000_000_000.0;
                double decay = firstOps <= 0 ? 0 : lastOps / firstOps;

                summary.Row(
                    StressCsvWriter.Int(Windows),
                    StressCsvWriter.Num(elapsedSeconds, 1),
                    StressCsvWriter.Num(firstOps, 0),
                    StressCsvWriter.Num(lastOps, 0),
                    StressCsvWriter.Num(decay, 4),
                    StressCsvWriter.Int(authority.Metrics.GetSnapshot().RejectedRequests),
                    StressCsvWriter.Int(authority.QueueOwnerCount));

                Assert.That(decay, Is.GreaterThan(0.70), "throughput decayed past 30%: " + decay.ToString("F4"));
                Assert.That(authority.RegisteredTargetCount, Is.EqualTo(1_000),
                    "target table must stay at its populated size across the soak");
            }
            finally
            {
                csv.Dispose();
                summary.Dispose();
            }
        }

        private static void RunThroughput(StressCsvWriter csv, int targetCount, ulong seed)
        {
            var authority = CreateAuthority();
            RegisterTargets(authority, targetCount);

            const int Iterations = 20_000;
            var random = new DeterministicRng(seed ^ (ulong)targetCount);
            var histogram = new LatencyHistogram(10L, 8192);
            long accepted = 0;
            long rejected = 0;
            int tick = 1_000;

            for (int i = 0; i < 2_000; i++)
            {
                authority.ValidateRequest(
                    new InteractionRequest(i + 1, 1UL, 1_000UL, "open", tick, WorldId),
                    FPVector3.Zero, tick);
            }

            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < Iterations; i++)
            {
                if ((i & 0xFF) == 0) tick++;
                var request = new InteractionRequest(
                    i + 1,
                    (ulong)(1 + (i % 64)),
                    (ulong)(1_000 + random.NextInt(targetCount)),
                    "open",
                    tick,
                    WorldId);

                long opStart = Stopwatch.GetTimestamp();
                InteractionValidationResult result = authority.ValidateRequest(request, FPVector3.Zero, tick);
                histogram.AddTicks(Stopwatch.GetTimestamp() - opStart);

                if (result.IsAccepted) accepted++;
                else rejected++;
            }

            double elapsedNs = LatencyHistogram.TicksToNs(Stopwatch.GetTimestamp() - start);
            double ops = elapsedNs <= 0 ? 0 : Iterations / (elapsedNs / 1_000_000_000.0);

            csv.Row(
                StressCsvWriter.Int(targetCount),
                StressCsvWriter.Int((long)seed),
                StressCsvWriter.Int(Iterations),
                StressCsvWriter.Num(ops, 0),
                StressCsvWriter.Num(histogram.PercentileUs(0.50), 3),
                StressCsvWriter.Num(histogram.PercentileUs(0.99), 3),
                StressCsvWriter.Int(accepted),
                StressCsvWriter.Int(rejected));
        }


        // The core stress helpers live in another test assembly and are internal; this package keeps
        // its own minimal copies so it can compile and run independently.
        private sealed class LatencyHistogram
        {
            private readonly long[] _buckets;
            private readonly long _bucketTicks;
            private long _count;

            public LatencyHistogram(long bucketTicks = 10L, int bucketCount = 8192)
            {
                _bucketTicks = bucketTicks;
                _buckets = new long[bucketCount];
            }

            public void AddTicks(long ticks)
            {
                _count++;
                long index = ticks / _bucketTicks;
                if (index < 0) index = 0;
                if (index >= _buckets.Length) index = _buckets.Length - 1;
                _buckets[index]++;
            }

            public double PercentileUs(double percentile)
            {
                if (_count == 0) return 0.0;
                long target = (long)Math.Ceiling(_count * percentile);
                if (target < 1) target = 1;
                long seen = 0;
                for (int i = 0; i < _buckets.Length; i++)
                {
                    seen += _buckets[i];
                    if (seen >= target) return TicksToNs((i + 1) * _bucketTicks) / 1000.0;
                }
                return TicksToNs(_buckets.Length * _bucketTicks) / 1000.0;
            }

            public static double TicksToNs(long ticks) => ticks * (1_000_000_000.0 / Stopwatch.Frequency);
        }

        private sealed class DeterministicRng
        {
            private ulong _s0;
            private ulong _s1;

            public DeterministicRng(ulong seed)
            {
                _s0 = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
                _s1 = _s0 ^ 0xD1B54A32D192ED03UL;
                if (_s1 == 0) _s1 = 0x94D049BB133111EBUL;
            }

            public ulong NextUInt64()
            {
                ulong x = _s0;
                ulong y = _s1;
                _s0 = y;
                x ^= x << 23;
                _s1 = x ^ y ^ (x >> 17) ^ (y >> 26);
                return _s1 + y;
            }

            public int NextInt(int exclusiveMax) => exclusiveMax <= 0 ? 0 : (int)(NextUInt64() % (ulong)exclusiveMax);
        }

        private sealed class StressCsvWriter : IDisposable
        {
            private readonly System.IO.StreamWriter _writer;

            public StressCsvWriter(string fileName, string header)
            {
                // Unity loads assemblies from a stream, so Assembly.Location is empty and cannot be used to
                // walk up to the project. The editor's working directory is the project root.
                string projectRoot = System.IO.Directory.GetCurrentDirectory();
                string dir = System.IO.Path.Combine(projectRoot, "deliverables", "review", "evidence", "interaction");
                System.IO.Directory.CreateDirectory(dir);
                _writer = new System.IO.StreamWriter(System.IO.Path.Combine(dir, fileName), append: false);
                _writer.WriteLine(header);
            }

            public void Row(params string[] cells)
            {
                for (int i = 0; i < cells.Length; i++)
                {
                    if (i > 0) _writer.Write(',');
                    _writer.Write(cells[i] ?? string.Empty);
                }
                _writer.WriteLine();
            }

            public static string Int(long value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            public static string Num(double value, int decimals) => value.ToString("F" + decimals, System.Globalization.CultureInfo.InvariantCulture);

            public void Dispose() => _writer.Dispose();
        }

        private static InteractionDeterministicAuthorityService CreateAuthority()
        {
            return new InteractionDeterministicAuthorityService(new InteractionAuthorityOptions(
                worldId: WorldId,
                requireStableIds: true,
                rateLimitWindowTicks: 30,
                maxRequestsPerRateLimitWindow: 8,
                requestHistoryWindowTicks: 600,
                requestHistoryCapacity: 2_048));
        }

        private static void RegisterTargets(InteractionDeterministicAuthorityService authority, int count)
        {
            for (int i = 0; i < count; i++)
            {
                Assert.That(authority.TryRegisterTarget(new InteractionDeterministicTargetSnapshot(
                    worldId: WorldId,
                    targetStableId: (ulong)(1_000 + i),
                    position: new FPVector3(FPInt64.FromInt(i % 16), FPInt64.Zero, FPInt64.FromInt((i / 16) % 16)),
                    interactionRange: FPInt64.FromInt(8),
                    isAvailable: true,
                    allowDefaultAction: true,
                    enabledActionIds: new[] { "open" })), Is.True);
            }
        }
    }
}
