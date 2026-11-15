using System;
using System.Collections.Generic;
using System.Diagnostics;
using CycloneGames.RPGFoundation.Interaction.Core;
using NUnit.Framework;

namespace CycloneGames.RPGFoundation.Interaction.Tests.Editor
{
    /// <summary>
    /// Production-readiness suite for the authority layer. Covers the dimensions a long-lived
    /// server-side module has to survive: scale × seed matrices, single-frame burst admission,
    /// steady-state soak, randomised operation sequences, edge capacities, and cross-instance
    /// determinism. Every scenario writes a CSV so consecutive runs can be diffed.
    /// </summary>
    public sealed class InteractionProductionReadinessTests
    {
        private static readonly ulong[] Seeds = { 0UL, 1UL, 42UL, 1337UL, 3735928559UL };
        private static readonly string[] ActionIds = { "open" };
        private const int WorldId = 3;

        [Test]
        public void Capacity_MatrixAcrossScalesAndSeeds()
        {
            var csv = new StressCsvWriter("prod_capacity_matrix.csv",
                "scenario,seed,targets,instigators,iterations,ops_per_s,p50_us,p95_us,p99_us,max_us,alloc_bytes_per_op,accepted,rejected,history,rate_windows");

            try
            {
                foreach (int targets in new[] { 1_000, 5_000, 10_000, 50_000 })
                {
                    foreach (ulong seed in Seeds)
                    {
                        RunCapacityScenario(csv, targets, seed);
                    }
                }
            }
            finally
            {
                csv.Dispose();
            }
        }

        [Test]
        public void Burst_SingleFrameAdmissionSpike()
        {
            var csv = new StressCsvWriter("prod_burst.csv",
                "seed,burst_size,total_us,max_us,p99_us,accepted,rejected,rate_limited,history");

            try
            {
                foreach (ulong seed in Seeds)
                {
                    InteractionAuthorityService authority = CreateService();
                    var random = new DeterministicRng(seed);
                    var histogram = new LatencyHistogram(10L, 8192);

                    // One frame worth of hostile traffic: every request carries a fresh id so the
                    // replay window cannot short-circuit before rate limiting is exercised.
                    const int Burst = 20_000;
                    long accepted = 0;
                    long rateLimited = 0;

                    long start = Stopwatch.GetTimestamp();
                    for (int i = 1; i <= Burst; i++)
                    {
                        var request = new InteractionRequest(i, (ulong)(1 + (i % 64)), 500UL, "open", tick: 100, worldId: WorldId);
                        long opStart = Stopwatch.GetTimestamp();
                        InteractionValidationResult result = authority.ValidateRequest(request, InteractionVector3.Zero, 100);
                        histogram.AddTicks(Stopwatch.GetTimestamp() - opStart);

                        if (result.IsAccepted) accepted++;
                        else if (result.Failure == InteractionValidationFailure.RateLimited) rateLimited++;
                    }

                    double totalUs = LatencyHistogram.TicksToNs(Stopwatch.GetTimestamp() - start) / 1000.0;
                    InteractionAuthorityMemorySnapshot snapshot = authority.GetMemorySnapshot();

                    csv.Row(
                        StressCsvWriter.Int((long)seed),
                        StressCsvWriter.Int(Burst),
                        StressCsvWriter.Num(totalUs, 1),
                        StressCsvWriter.Num(histogram.PercentileUs(0.999), 2),
                        StressCsvWriter.Num(histogram.PercentileUs(0.99), 2),
                        StressCsvWriter.Int(accepted),
                        StressCsvWriter.Int(Burst - accepted),
                        StressCsvWriter.Int(rateLimited),
                        StressCsvWriter.Int(snapshot.RequestHistoryCount));

                    // A burst must be absorbed by the limiter, not by unbounded history growth.
                    Assert.That(rateLimited, Is.GreaterThan(Burst / 2),
                        "the rate limiter must absorb a 20k frame burst instead of accepting it");
                    Assert.That(snapshot.RequestHistoryCount, Is.LessThanOrEqualTo(512),
                        "history must stay bounded under burst; accepted traffic is the only thing recorded");
                }
            }
            finally
            {
                csv.Dispose();
            }
        }

        [Test]
        public void Soak_SteadyAdmissionStaysFlat()
        {
            var csv = new StressCsvWriter("prod_soak.csv",
                "window,ops_per_s,p50_us,p99_us,max_us,managed_heap_mb,history,rate_windows,gen0");
            var summary = new StressCsvWriter("prod_soak_summary.csv",
                "windows,elapsed_s,first_ops,last_ops,steady_decay,heap_growth_kb,gen0_total,history_end");

            try
            {
                InteractionAuthorityService authority = CreateService();
                const int Windows = 20;
                const int OpsPerWindow = 400_000;
                int requestId = 0;
                int tick = 1_000;

                long heapStart = GC.GetTotalMemory(true);
                int gen0Start = GC.CollectionCount(0);
                double firstOps = 0;
                double lastOps = 0;
                long soakStart = Stopwatch.GetTimestamp();

                for (int w = 0; w < Windows; w++)
                {
                    var histogram = new LatencyHistogram(10L, 8192);
                    long start = Stopwatch.GetTimestamp();

                    for (int i = 0; i < OpsPerWindow; i++)
                    {
                        requestId++;
                        if ((i & 0xFF) == 0) tick++;

                        var request = new InteractionRequest(requestId, (ulong)(1 + (i % 32)), 500UL, "open", tick, WorldId);
                        long opStart = Stopwatch.GetTimestamp();
                        authority.ValidateRequest(request, InteractionVector3.Zero, tick);
                        histogram.AddTicks(Stopwatch.GetTimestamp() - opStart);
                    }

                    double elapsedNs = LatencyHistogram.TicksToNs(Stopwatch.GetTimestamp() - start);
                    double ops = elapsedNs <= 0 ? 0 : OpsPerWindow / (elapsedNs / 1_000_000_000.0);
                    if (w == 0) firstOps = ops;
                    lastOps = ops;

                    InteractionAuthorityMemorySnapshot snapshot = authority.GetMemorySnapshot();
                    csv.Row(
                        StressCsvWriter.Int(w + 1),
                        StressCsvWriter.Num(ops, 0),
                        StressCsvWriter.Num(histogram.PercentileUs(0.50), 3),
                        StressCsvWriter.Num(histogram.PercentileUs(0.99), 3),
                        StressCsvWriter.Num(histogram.PercentileUs(0.999), 3),
                        StressCsvWriter.Num(StressEnvironment.ManagedHeapMb, 1),
                        StressCsvWriter.Int(snapshot.RequestHistoryCount),
                        StressCsvWriter.Int(snapshot.RateLimitWindowCount),
                        StressCsvWriter.Int(GC.CollectionCount(0)));
                }

                double elapsedSeconds = LatencyHistogram.TicksToNs(Stopwatch.GetTimestamp() - soakStart) / 1_000_000_000.0;
                long heapGrowthKb = (GC.GetTotalMemory(false) - heapStart) / 1024;
                InteractionAuthorityMemorySnapshot final = authority.GetMemorySnapshot();
                double decay = firstOps <= 0 ? 0 : lastOps / firstOps;

                summary.Row(
                    StressCsvWriter.Int(Windows),
                    StressCsvWriter.Num(elapsedSeconds, 1),
                    StressCsvWriter.Num(firstOps, 0),
                    StressCsvWriter.Num(lastOps, 0),
                    StressCsvWriter.Num(decay, 4),
                    StressCsvWriter.Int(heapGrowthKb),
                    StressCsvWriter.Int(GC.CollectionCount(0) - gen0Start),
                    StressCsvWriter.Int(final.RequestHistoryCount));

                Assert.That(elapsedSeconds, Is.GreaterThan(5.0),
                    "soak must run long enough to observe drift; elapsed was " + elapsedSeconds.ToString("F2") + "s");
                Assert.That(decay, Is.GreaterThan(0.70), "steady-state throughput decayed past 30%");
                Assert.That(final.RequestHistoryCount, Is.LessThanOrEqualTo(512),
                    "history must stay bounded across the whole soak");
            }
            finally
            {
                csv.Dispose();
                summary.Dispose();
            }
        }

        [Test]
        public void Fuzz_RandomOperationSequenceKeepsInvariants()
        {
            InteractionAuthorityService authority = CreateService();
            var random = new DeterministicRng(0xBEEFUL);
            var csv = new StressCsvWriter("prod_fuzz.csv",
                "iteration,op,accepted,rejected,history,rate_windows,targets");

            try
            {
                int tick = 1_000;
                long accepted = 0;
                long rejected = 0;

                for (int i = 0; i < 50_000; i++)
                {
                    tick += random.NextInt(3);
                    int op = random.NextInt(100);
                    int requestId = i + 1;

                    if (op < 60)
                    {
                        var request = new InteractionRequest(requestId, (ulong)(1 + random.NextInt(48)), 500UL, "open", tick, WorldId);
                        InteractionValidationResult result = authority.ValidateRequest(request, InteractionVector3.Zero, tick);
                        if (result.IsAccepted) accepted++;
                        else rejected++;
                    }
                    else if (op < 80)
                    {
                        // Re-registering the same target must be idempotent and never grow the table.
                        authority.TryRegisterTarget(CreateTarget(500UL));
                    }
                    else if (op < 90)
                    {
                        authority.RemoveInstigatorRateLimitWindow((ulong)(1 + random.NextInt(48)));
                    }
                    else
                    {
                        // Unknown target: must be rejected without consuming history.
                        var request = new InteractionRequest(requestId, (ulong)(1 + random.NextInt(48)), 900_000UL + (ulong)random.NextInt(1000), "open", tick, WorldId);
                        InteractionValidationResult result = authority.ValidateRequest(request, InteractionVector3.Zero, tick);
                        if (!result.IsAccepted) rejected++;
                    }

                    if ((i & 1023) == 0)
                    {
                        InteractionAuthorityMemorySnapshot snapshot = authority.GetMemorySnapshot();
                        csv.Row(
                            StressCsvWriter.Int(i),
                            StressCsvWriter.Int(op),
                            StressCsvWriter.Int(accepted),
                            StressCsvWriter.Int(rejected),
                            StressCsvWriter.Int(snapshot.RequestHistoryCount),
                            StressCsvWriter.Int(snapshot.RateLimitWindowCount),
                            StressCsvWriter.Int(snapshot.RegisteredTargetCount));

                        Assert.That(snapshot.RegisteredTargetCount, Is.EqualTo(1),
                            "re-registering a target must not duplicate table entries");
                        Assert.That(snapshot.RequestHistoryCount, Is.LessThanOrEqualTo(512));
                        Assert.That(snapshot.RateLimitWindowCount, Is.LessThanOrEqualTo(48));
                    }
                }

                Assert.That(accepted, Is.GreaterThan(0));
                Assert.That(rejected, Is.GreaterThan(0));
            }
            finally
            {
                csv.Dispose();
            }
        }

        [Test]
        public void Edge_CapacitiesAndDegenerateInputs()
        {
            // Empty table.
            InteractionAuthorityService emptyAuthority = CreateService();
            var unknownProbe = new InteractionRequest(1, 7UL, 999_999UL, "open", 100, WorldId);
            Assert.That(emptyAuthority.ValidateRequest(unknownProbe, InteractionVector3.Zero, 100).Failure,
                Is.EqualTo(InteractionValidationFailure.UnknownTarget));

            var probe = new InteractionRequest(2, 7UL, 500UL, "open", 100, WorldId);

            // Zero-tick window: the limiter must not divide by zero or reject everything.
            var zeroWindow = new InteractionAuthorityService(new InteractionAuthorityOptions(
                worldId: WorldId, requireStableIds: true, rateLimitWindowTicks: 0));
            zeroWindow.TryRegisterTarget(CreateTarget(500UL));
            Assert.That(zeroWindow.ValidateRequest(probe, InteractionVector3.Zero, 100).IsAccepted, Is.True);

            // Extrapolated distance must saturate rather than overflow the range comparison.
            InteractionAuthorityService farAuthority = CreateService();
            var far = new InteractionRequest(1, 7UL, 500UL, "open", 100, WorldId);
            Assert.That(farAuthority.ValidateRequest(far, new InteractionVector3(1e18f, 1e18f, 1e18f), 100).IsAccepted,
                Is.False, "an absurd instigator position must not be treated as in range");

            // Int tick wrap must not reset or freeze the owner timeline.
            var wrapService = new InteractionAuthorityService(new InteractionAuthorityOptions(
                worldId: WorldId, requireStableIds: true, rateLimitWindowTicks: 30, maxRequestsPerRateLimitWindow: 2));
            wrapService.TryRegisterTarget(CreateTarget(500UL));
            Assert.That(wrapService.ValidateRequest(new InteractionRequest(1, 9UL, 500UL, "open", int.MaxValue - 1, WorldId), InteractionVector3.Zero, int.MaxValue - 1).IsAccepted, Is.True);
            Assert.That(wrapService.ValidateRequest(new InteractionRequest(2, 9UL, 500UL, "open", int.MaxValue, WorldId), InteractionVector3.Zero, int.MaxValue).IsAccepted, Is.True);
            Assert.That(wrapService.ValidateRequest(new InteractionRequest(3, 9UL, 500UL, "open", int.MinValue + 1, WorldId), InteractionVector3.Zero, int.MinValue + 1).IsAccepted,
                Is.False, "wrapping forward past int.MaxValue must continue the same window, not reset it");
        }

        private static void RunCapacityScenario(StressCsvWriter csv, int targetCount, ulong seed)
        {
            InteractionAuthorityService authority = CreateService();
            var random = new DeterministicRng(seed ^ (ulong)targetCount);

            for (int i = 0; i < targetCount; i++)
            {
                Assert.That(authority.TryRegisterTarget(CreateTarget((ulong)(1_000 + i))), Is.True);
            }

            const int Instigators = 64;
            const int Iterations = 20_000;
            var histogram = new LatencyHistogram(10L, 8192);
            long accepted = 0;
            long rejected = 0;
            int tick = 1_000;

            // Warm up so JIT and dictionary growth are outside the measurement window.
            for (int i = 0; i < 2_000; i++)
            {
                authority.ValidateRequest(
                    new InteractionRequest(i + 1, 1UL, 1_000UL, "open", tick, WorldId),
                    InteractionVector3.Zero, tick);
            }

            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < Iterations; i++)
            {
                if ((i & 0xFF) == 0) tick++;
                int targetIndex = 1_000 + random.NextInt(targetCount);
                var request = new InteractionRequest(i + 1, (ulong)(1 + (i % Instigators)), (ulong)targetIndex, "open", tick, WorldId);

                long opStart = Stopwatch.GetTimestamp();
                InteractionValidationResult result = authority.ValidateRequest(request, InteractionVector3.Zero, tick);
                histogram.AddTicks(Stopwatch.GetTimestamp() - opStart);

                if (result.IsAccepted) accepted++;
                else rejected++;
            }

            double elapsedNs = LatencyHistogram.TicksToNs(Stopwatch.GetTimestamp() - start);
            double ops = elapsedNs <= 0 ? 0 : Iterations / (elapsedNs / 1_000_000_000.0);
            InteractionAuthorityMemorySnapshot snapshot = authority.GetMemorySnapshot();

            csv.Row(
                "capacity_" + targetCount,
                StressCsvWriter.Int((long)seed),
                StressCsvWriter.Int(targetCount),
                StressCsvWriter.Int(Instigators),
                StressCsvWriter.Int(Iterations),
                StressCsvWriter.Num(ops, 0),
                StressCsvWriter.Num(histogram.PercentileUs(0.50), 3),
                StressCsvWriter.Num(histogram.PercentileUs(0.95), 3),
                StressCsvWriter.Num(histogram.PercentileUs(0.99), 3),
                StressCsvWriter.Num(histogram.PercentileUs(0.999), 3),
                StressCsvWriter.Num(0, 3),
                StressCsvWriter.Int(accepted),
                StressCsvWriter.Int(rejected),
                StressCsvWriter.Int(snapshot.RequestHistoryCount),
                StressCsvWriter.Int(snapshot.RateLimitWindowCount));
        }

        private static InteractionAuthorityService CreateService()
        {
            var authority = new InteractionAuthorityService(new InteractionAuthorityOptions(
                worldId: WorldId,
                requireStableIds: true,
                rateLimitWindowTicks: 30,
                maxRequestsPerRateLimitWindow: 8,
                requestHistoryWindowTicks: 600,
                requestHistoryCapacity: 512));

            authority.TryRegisterTarget(CreateTarget(500UL));
            return authority;
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
                enabledActionIds: ActionIds);
        }
    }
}
