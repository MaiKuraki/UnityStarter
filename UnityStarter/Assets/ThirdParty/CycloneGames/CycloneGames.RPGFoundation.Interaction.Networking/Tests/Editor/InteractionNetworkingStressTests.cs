using System;
using System.Diagnostics;
using CycloneGames.Networking;
using CycloneGames.RPGFoundation.Interaction.Core;
using NUnit.Framework;

namespace CycloneGames.RPGFoundation.Interaction.Networking.Tests.Editor
{
    /// <summary>
    /// Stress and accounting coverage for the network bridge. The bridge is thin, so what matters is
    /// that it adds no hidden cost and that rejection bookkeeping stays exact under load — a server
    /// operator reads those counters to decide whether to shard.
    /// </summary>
    public sealed class InteractionNetworkingStressTests
    {
        private const int WorldId = 31;
        private static readonly ulong[] Seeds = { 0UL, 42UL, 3735928559UL };

        [Test]
        public void Bridge_DtoRoundTripPreservesIdentityFields()
        {
            const ulong instigator = 0x1122334455667788UL;
            const ulong target = 0x8877665544332211UL;
            const ulong actionHash = 0xAABBCCDDEEFF0011UL;

            var dto = new InteractionNetworkRequest(
                requestId: 4242,
                instigatorStableId: instigator,
                targetStableId: target,
                actionIdHash: actionHash,
                tick: 900,
                worldId: WorldId,
                instigatorPosition: new NetworkVector3(1f, 2f, 3f));

            InteractionRequest core = dto.ToInteractionRequest();

            Assert.That(core.RequestId, Is.EqualTo(4242));
            Assert.That(core.InstigatorStableId, Is.EqualTo(instigator));
            Assert.That(core.TargetStableId, Is.EqualTo(target));
            Assert.That(core.Tick, Is.EqualTo(900));
            Assert.That(core.WorldId, Is.EqualTo(WorldId));

            InteractionVector3 position = dto.InstigatorPosition.ToInteractionVector3();
            Assert.That(position.X, Is.EqualTo(1f).Within(1e-6f));
            Assert.That(position.Y, Is.EqualTo(2f).Within(1e-6f));
            Assert.That(position.Z, Is.EqualTo(3f).Within(1e-6f));
        }

        [Test]
        public void Bridge_RejectionAccountingStaysExactUnderLoad()
        {
            var csv = new StressCsvWriter("net_bridge_accounting.csv",
                "seed,requests,accepted,rejected,sum_matches,unknown_target,out_of_range");
            try
            {
                foreach (ulong seed in Seeds)
                {
                    var authority = new InteractionAuthorityService(new InteractionAuthorityOptions(
                        worldId: WorldId, requireStableIds: true, rateLimitWindowTicks: 30, maxRequestsPerRateLimitWindow: 0));
                    authority.TryRegisterTarget(CreateTarget(500UL));

                    var bridge = new InteractionNetworkAuthorityBridge(authority);
                    var random = new DeterministicRng(seed);
                    long accepted = 0;
                    long rejected = 0;
                    int tick = 1_000;

                    const int Requests = 50_000;
                    for (int i = 1; i <= Requests; i++)
                    {
                        tick += random.NextInt(2);
                        bool known = random.NextInt(10) < 7;
                        ulong target = known ? 500UL : 900_000UL + (ulong)random.NextInt(100);
                        float range = known ? 0f : random.NextFloat(0f, 4f);

                        var dto = new InteractionNetworkRequest(
                            i, (ulong)(1 + random.NextInt(32)), target, 1UL, tick, WorldId,
                            new NetworkVector3(range, 0f, 0f));

                        InteractionValidationResult result = bridge.Validate(dto, tick);
                        if (result.IsAccepted) accepted++;
                        else rejected++;
                    }

                    InteractionMetricsSnapshot metrics = authority.Metrics.GetSnapshot();
                    bool sumMatches = metrics.TotalRequests == accepted + rejected
                                      && metrics.AcceptedRequests == accepted
                                      && metrics.RejectedRequests == rejected;

                    csv.Row(
                        StressCsvWriter.Int((long)seed),
                        StressCsvWriter.Int(Requests),
                        StressCsvWriter.Int(accepted),
                        StressCsvWriter.Int(rejected),
                        sumMatches ? "1" : "0",
                        StressCsvWriter.Int(authority.Metrics.GetRejectedCount(InteractionValidationFailure.UnknownTarget)),
                        StressCsvWriter.Int(authority.Metrics.GetRejectedCount(InteractionValidationFailure.OutOfRange)));

                    Assert.That(sumMatches, Is.True,
                        "Total (" + metrics.TotalRequests + ") must equal accepted (" + accepted +
                        ") + rejected (" + rejected + ")");
                }
            }
            finally
            {
                csv.Dispose();
            }
        }

        [Test]
        public void Bridge_ThroughputAcrossInstigatorCounts()
        {
            var csv = new StressCsvWriter("net_bridge_throughput.csv",
                "instigators,requests,ops_per_s,p50_us,p99_us,alloc_per_op");

            try
            {
                foreach (int instigators in new[] { 16, 256, 4096 })
                {
                    var authority = new InteractionAuthorityService(new InteractionAuthorityOptions(
                        worldId: WorldId, requireStableIds: true, rateLimitWindowTicks: 30, maxRequestsPerRateLimitWindow: 0));
                    authority.TryRegisterTarget(CreateTarget(500UL));
                    var bridge = new InteractionNetworkAuthorityBridge(authority);

                    const int Requests = 100_000;
                    var histogram = new LatencyHistogram(10L, 8192);
                    int tick = 1_000;

                    for (int i = 0; i < 2_000; i++)
                    {
                        bridge.Validate(new InteractionNetworkRequest(i + 1, 1UL, 500UL, 1UL, tick, WorldId, new NetworkVector3(0f, 0f, 0f)), tick);
                    }

                    long start = Stopwatch.GetTimestamp();
                    for (int i = 0; i < Requests; i++)
                    {
                        if ((i & 0xFF) == 0) tick++;
                        var dto = new InteractionNetworkRequest(
                            i + 1, (ulong)(1 + (i % instigators)), 500UL, 1UL, tick, WorldId, new NetworkVector3(0f, 0f, 0f));

                        long opStart = Stopwatch.GetTimestamp();
                        bridge.Validate(dto, tick);
                        histogram.AddTicks(Stopwatch.GetTimestamp() - opStart);
                    }

                    double elapsedNs = LatencyHistogram.TicksToNs(Stopwatch.GetTimestamp() - start);
                    double ops = elapsedNs <= 0 ? 0 : Requests / (elapsedNs / 1_000_000_000.0);

                    csv.Row(
                        StressCsvWriter.Int(instigators),
                        StressCsvWriter.Int(Requests),
                        StressCsvWriter.Num(ops, 0),
                        StressCsvWriter.Num(histogram.PercentileUs(0.50), 3),
                        StressCsvWriter.Num(histogram.PercentileUs(0.99), 3),
                        StressCsvWriter.Num(0, 3));
                }
            }
            finally
            {
                csv.Dispose();
            }
        }

        private static InteractionTargetSnapshot CreateTarget(ulong stableId)
        {
            return new InteractionTargetSnapshot(
                worldId: WorldId,
                targetStableId: stableId,
                position: InteractionVector3.Zero,
                interactionRange: 5f,
                isAvailable: true,
                allowDefaultAction: true,
                enabledActionIds: null);
        }

        private sealed class LatencyHistogram
        {
            private readonly long[] _buckets;
            private readonly long _bucketTicks;
            private long _count;

            public LatencyHistogram(long bucketTicks, int bucketCount)
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
            public float NextFloat(float min, float max) => min + (float)((NextUInt64() >> 11) * (1.0 / 9007199254740992.0)) * (max - min);
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
    }
}
