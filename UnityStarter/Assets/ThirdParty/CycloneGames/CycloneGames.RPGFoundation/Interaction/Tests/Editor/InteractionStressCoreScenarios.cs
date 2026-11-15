using System;
using System.Collections.Generic;
using CycloneGames.RPGFoundation.Interaction.Core;
using CycloneGames.RPGFoundation.Interaction.Networking;
using NUnit.Framework;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace CycloneGames.RPGFoundation.Interaction.Tests.Editor
{
    /// <summary>
    /// Deterministic stress evidence for the engine-independent Core package: authority admission,
    /// validation, rate limiting, request queues and the network replay window.
    ///
    /// These types are pure C#, so the measurements are free of engine noise and are the closest
    /// thing to a server-side number this project can produce inside the Editor.
    /// </summary>
    [TestFixture]
    public sealed class InteractionStressCoreScenarios
    {
        private static readonly ulong[] Seeds =
        {
            0UL,
            1UL,
            42UL,
            1337UL,
            3735928559UL
        };

        private const int WorldId = 1;
        private const int BaseTick = 1_000;

        private static InteractionAuthorityOptions CreateOptions(
            int maxRequestsPerRateLimitWindow = 8,
            int rateLimitWindowTicks = 30,
            int queueCapacityPerTarget = 64,
            bool requireStableIds = true)
        {
            return new InteractionAuthorityOptions(
                worldId: WorldId,
                requireStableIds: requireStableIds,
                maxFutureTickDelta: 8,
                maxPastTickDelta: 120,
                rateLimitWindowTicks: rateLimitWindowTicks,
                maxRequestsPerRateLimitWindow: maxRequestsPerRateLimitWindow,
                requestHistoryWindowTicks: 600,
                requestHistoryCapacity: 65_536,
                queueCapacityPerTarget: queueCapacityPerTarget,
                maxQueuedRequestsPerInstigator: 4);
        }

        private static void Emit(
            StressCsvWriter csv,
            string scenario,
            ulong seed,
            int scale,
            long operations,
            double opsPerSecond,
            LatencyHistogram histogram,
            AllocationProbe allocation,
            long accepted,
            long rejected,
            long retained,
            string note)
        {
            csv.Row(
                scenario,
                StressCsvWriter.Int((long)seed),
                StressCsvWriter.Int(scale),
                StressCsvWriter.Int(operations),
                StressCsvWriter.Int(operations),
                StressCsvWriter.Num(opsPerSecond, 0),
                StressCsvWriter.Num(histogram.PercentileUs(0.50), 3),
                StressCsvWriter.Num(histogram.PercentileUs(0.95), 3),
                StressCsvWriter.Num(histogram.PercentileUs(0.99), 3),
                StressCsvWriter.Num(allocation.BytesPerOperation, 2),
                StressCsvWriter.Int(allocation.Gen0Collections),
                StressCsvWriter.Int(accepted),
                StressCsvWriter.Int(rejected),
                StressCsvWriter.Int(retained),
                StressCsvWriter.Int(StressEnvironment.WorkingSetMb),
                StressCsvWriter.Int(StressEnvironment.ManagedHeapMb),
                note);
        }

        /// <summary>
        /// Writes the per-failure rejection counts and proves they sum to the reported RejectedRequests.
        /// A single total cannot distinguish "rate limited" from "request history full", which is exactly
        /// the distinction that matters when judging whether a guard is actually holding.
        /// </summary>
        private static void WriteRejectionBreakdown(
            StressCsvWriter csv,
            string scenario,
            ulong seed,
            InteractionAuthorityService service)
        {
            InteractionMetricsSnapshot metrics = service.Metrics.GetSnapshot();
            long sum = 0L;

            for (int f = 1; f < (int)InteractionValidationFailure.Count; f++)
            {
                long count = service.Metrics.GetRejectedCount((InteractionValidationFailure)f);
                if (count <= 0L)
                {
                    continue;
                }

                csv.Row(
                    scenario,
                    StressCsvWriter.Int((long)seed),
                    ((InteractionValidationFailure)f).ToString(),
                    StressCsvWriter.Int(count));
                sum += count;
            }

            // Per-failure counters are cumulative for the whole fixture body (warm-up + measured loop +
            // latency loop + allocation probe), so the identity is checked against the cumulative
            // TotalRequests rather than the measured-window delta. This is the strongest form of the
            // accounting gate: it proves no rejection is unattributed and none is double counted.
            Assert.That(metrics.AcceptedRequests + sum, Is.EqualTo(metrics.TotalRequests),
                scenario + " seed=" + seed + ": accepted + per-failure rejection counts must equal "
                + "TotalRequests (cumulative). accepted=" + metrics.AcceptedRequests
                + " sumByFailure=" + sum + " total=" + metrics.TotalRequests);
        }

        /// <summary>
        /// Runs one operation body through warm-up, a throughput span, a per-operation latency span
        /// and a heap-delta allocation probe. All four use the same production call path.
        /// Returns the accepted / rejected delta observed during the measured spans.
        /// </summary>
        private static (long accepted, long rejected) RunOpBody(
            StressCsvWriter csv,
            string scenario,
            ulong seed,
            int scale,
            int iterations,
            Action operation,
            Func<(long accepted, long rejected, long retained)> snapshot,
            string note)
        {
            // Instrumentation discipline: warm-up is untimed AND uncounted, the accepted/rejected delta
            // spans exactly one measured loop, and the latency loop plus the allocation probe run outside
            // that window. An earlier version opened the metric window before warm-up and closed it after
            // the probe, which reported accepted+rejected = 3 x iterations plus warm-up.
            const int WarmupIterations = 1_000;
            for (int i = 0; i < WarmupIterations; i++)
            {
                operation();
            }

            (long acceptedBefore, long rejectedBefore, _) = snapshot();

            Stopwatch throughputWatch = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                operation();
            }

            throughputWatch.Stop();
            (long acceptedAfter, long rejectedAfter, long retained) = snapshot();

            long acceptedDelta = acceptedAfter - acceptedBefore;
            long rejectedDelta = rejectedAfter - rejectedBefore;

            double throughputNs = LatencyHistogram.TicksToNs(throughputWatch.ElapsedTicks);
            double opsPerSecond = throughputNs <= 0.0
                ? 0.0
                : iterations / (throughputNs / 1_000_000_000.0);

            // Latency and allocation windows are deliberately outside the accept/reject window.
            var histogram = new LatencyHistogram();
            for (int i = 0; i < iterations; i++)
            {
                long start = Stopwatch.GetTimestamp();
                operation();
                histogram.AddTicks(Stopwatch.GetTimestamp() - start);
            }

            int probeIterations = Math.Min(iterations, 5_000);
            AllocationProbe allocation = AllocationMeter.Measure(probeIterations, () =>
            {
                for (int i = 0; i < probeIterations; i++)
                {
                    operation();
                }
            });

            Emit(
                csv,
                scenario,
                seed,
                scale,
                iterations,
                opsPerSecond,
                histogram,
                allocation,
                acceptedDelta,
                rejectedDelta,
                retained,
                note);

            return (acceptedDelta, rejectedDelta);
        }

        private static InteractionTargetSnapshot CreateTarget(ulong targetStableId)
        {
            return new InteractionTargetSnapshot(
                WorldId,
                targetStableId,
                new InteractionVector3(0f, 0f, 0f),
                5f,
                isAvailable: true,
                allowDefaultAction: true,
                enabledActionIds: null);
        }

        /// <summary>Target that can execute the named action, used to exercise the action-id resolution path.</summary>
        private static InteractionTargetSnapshot CreateActionTarget(ulong targetStableId, string actionId)
        {
            return new InteractionTargetSnapshot(
                WorldId,
                targetStableId,
                new InteractionVector3(0f, 0f, 0f),
                5f,
                isAvailable: true,
                allowDefaultAction: true,
                enabledActionIds: new[] { actionId });
        }

        // ------------------------------------------------------------------
        // Authority admission + validation
        // ------------------------------------------------------------------

        [Test]
        [Timeout(900_000)]
        public void Authority_ValidationThroughputByTargetScale()
        {
            using var csv = new StressCsvWriter("core_scenarios.csv", StressPaths.CoreHeader);

            foreach (int targetCount in new[] { 1_000, 10_000, 50_000 })
            {
                foreach (ulong seed in Seeds)
                {
                    var service = new InteractionAuthorityService(CreateOptions());
                    for (int i = 0; i < targetCount; i++)
                    {
                        Assert.That(service.TryRegisterTarget(CreateActionTarget((ulong)(i + 1), "open")), Is.True);
                    }

                    int requestId = 0;
                    int tick = BaseTick;

                    void Operation()
                    {
                        requestId++;
                        tick++;
                        ulong instigator = (ulong)((requestId % 64) + 1);
                        ulong target = (ulong)(((requestId * 2654435761L) % targetCount) + 1);
                        var request = new InteractionRequest(requestId, instigator, target, "open", tick, WorldId);
                        service.ValidateRequest(request, InteractionVector3.Zero, tick);
                    }

                    (long accepted, long rejected) = RunOpBody(
                        csv,
                        "authority_validate_targets_" + targetCount,
                        seed,
                        targetCount,
                        200_000,
                        Operation,
                        () =>
                        {
                            InteractionMetricsSnapshot m = service.Metrics.GetSnapshot();
                            return (m.AcceptedRequests, m.RejectedRequests, service.GetMemorySnapshot().RequestHistoryCount);
                        },
                        "valid in-range requests with action id, 64 instigators, tick advance 1");

                    Assert.That(accepted, Is.GreaterThan(0L),
                        "Accept-path scenario must actually accept requests; otherwise the measurement is a reject loop.");
                    Assert.That(accepted + rejected, Is.EqualTo(200_000L),
                        "The accept/reject window must cover exactly one measured loop, not warm-up plus probe.");
                }
            }
        }

        [Test]
        public void Authority_AdmissionCeilingsAreEnforced()
        {
            using var csv = new StressCsvWriter("core_admission_ceiling.csv",
                "check,declared_limit,observed,verdict");

            var service = new InteractionAuthorityService(CreateOptions());
            int accepted = 0;
            for (int i = 0; i < InteractionAuthorityService.MaximumRegisteredTargetCount + 512; i++)
            {
                if (service.TryRegisterTarget(CreateTarget((ulong)(i + 1))))
                {
                    accepted++;
                }
            }

            InteractionAuthorityMemorySnapshot snapshot = service.GetMemorySnapshot();
            csv.Row("registered_targets", StressCsvWriter.Int(InteractionAuthorityService.MaximumRegisteredTargetCount),
                StressCsvWriter.Int(snapshot.RegisteredTargetCount), "asserted");
            Assert.That(snapshot.RegisteredTargetCount, Is.EqualTo(InteractionAuthorityService.MaximumRegisteredTargetCount));
            Assert.That(accepted, Is.EqualTo(InteractionAuthorityService.MaximumRegisteredTargetCount));
            Assert.That(snapshot.RejectedTargetAdmissionCount, Is.GreaterThan(0L));

            // Queue-owner ceiling: queue creation must fail closed, not throw, on the public Try path.
            var queueOptions = CreateOptions();
            var queueService = new InteractionAuthorityService(queueOptions);
            int created = 0;
            for (int i = 0; i < InteractionAuthorityService.MaximumQueueOwnerCount + 64; i++)
            {
                if (queueService.TryGetOrCreateQueue((ulong)(i + 1), out _))
                {
                    created++;
                }
            }

            InteractionAuthorityMemorySnapshot queueSnapshot = queueService.GetMemorySnapshot();
            csv.Row("queue_owners", StressCsvWriter.Int(InteractionAuthorityService.MaximumQueueOwnerCount),
                StressCsvWriter.Int(queueSnapshot.QueueOwnerCount), "asserted");
            Assert.That(created, Is.EqualTo(InteractionAuthorityService.MaximumQueueOwnerCount));
            Assert.That(queueSnapshot.QueueOwnerCount, Is.EqualTo(InteractionAuthorityService.MaximumQueueOwnerCount));
            Assert.That(queueSnapshot.RejectedQueueOwnerAdmissionCount, Is.GreaterThan(0L));

            Assert.Throws<InvalidOperationException>(() =>
                queueService.GetOrCreateQueue((ulong)(InteractionAuthorityService.MaximumQueueOwnerCount + 1000)),
                "The throwing GetOrCreateQueue overload must surface the ceiling.");
        }

        /// <summary>
        /// Measures the managed heap cost of filling each bounded structure to its declared ceiling.
        /// A ceiling that is enforced but expensive to reach is still an out-of-memory risk on a
        /// server, so the admission limit alone is not sufficient evidence.
        /// </summary>
        [Test]
        [Timeout(900_000)]
        public void Authority_CeilingMemoryFootprint()
        {
            using var csv = new StressCsvWriter("core_memory_footprint.csv",
                "structure,entries,declared_limit,heap_delta_bytes,bytes_per_entry,note");

            // Registered targets.
            {
                var service = new InteractionAuthorityService(CreateOptions());
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                long before = GC.GetTotalMemory(true);
                int entries = 0;
                for (int i = 0; i < InteractionAuthorityService.MaximumRegisteredTargetCount; i++)
                {
                    if (service.TryRegisterTarget(CreateTarget((ulong)(i + 1))))
                    {
                        entries++;
                    }
                }

                long delta = GC.GetTotalMemory(true) - before;
                csv.Row("registered_targets", StressCsvWriter.Int(entries),
                    StressCsvWriter.Int(InteractionAuthorityService.MaximumRegisteredTargetCount),
                    StressCsvWriter.Int(delta), StressCsvWriter.Num(delta / (double)entries, 2),
                    "InteractionTargetSnapshot dictionary at ceiling");
                Assert.That(service.RegisteredTargetCount, Is.EqualTo(entries));
            }

            // Queue owners: each owner eagerly allocates a queue with several backing containers.
            {
                var service = new InteractionAuthorityService(CreateOptions());
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                long before = GC.GetTotalMemory(true);
                int entries = 0;
                for (int i = 0; i < InteractionAuthorityService.MaximumQueueOwnerCount; i++)
                {
                    if (service.TryGetOrCreateQueue((ulong)(i + 1), out _))
                    {
                        entries++;
                    }
                }

                long delta = GC.GetTotalMemory(true) - before;
                csv.Row("queue_owners_with_empty_queues", StressCsvWriter.Int(entries),
                    StressCsvWriter.Int(InteractionAuthorityService.MaximumQueueOwnerCount),
                    StressCsvWriter.Int(delta), StressCsvWriter.Num(delta / (double)entries, 2),
                    "each owner allocates Queue+HashSet+2 Dictionaries at QueueCapacityPerTarget");
                Assert.That(service.GetMemorySnapshot().QueueOwnerCount, Is.EqualTo(entries));
            }

            // Rate-limit windows.
            {
                var limiter = new InteractionRateLimiter();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                long before = GC.GetTotalMemory(true);
                int entries = 0;
                for (int i = 0; i < InteractionRateLimiter.MaximumWindowCount; i++)
                {
                    if (limiter.TryConsume((ulong)(i + 1), BaseTick, 8, 100_000))
                    {
                        entries++;
                    }
                }

                long delta = GC.GetTotalMemory(true) - before;
                csv.Row("rate_limit_windows", StressCsvWriter.Int(entries),
                    StressCsvWriter.Int(InteractionRateLimiter.MaximumWindowCount),
                    StressCsvWriter.Int(delta), StressCsvWriter.Num(delta / (double)entries, 2),
                    "WindowState dictionary plus expiry heap");
                Assert.That(limiter.GetMemorySnapshot().WindowCount, Is.EqualTo(entries));
            }

            // Replay windows: one class plus one linked-list node per tracked instigator.
            {
                var window = new InteractionRequestReplayWindow();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                long before = GC.GetTotalMemory(true);
                int entries = 0;
                for (int i = 0; i < InteractionRequestReplayWindow.MaxTrackedInstigators; i++)
                {
                    if (window.TryAccept((ulong)(i + 1), 1))
                    {
                        entries++;
                    }
                }

                long delta = GC.GetTotalMemory(true) - before;
                csv.Row("replay_window_instigators", StressCsvWriter.Int(entries),
                    StressCsvWriter.Int(InteractionRequestReplayWindow.MaxTrackedInstigators),
                    StressCsvWriter.Int(delta), StressCsvWriter.Num(delta / (double)entries, 2),
                    "InstigatorWindow class plus LinkedListNode per instigator");
                Assert.That(window.TrackedInstigatorCount, Is.EqualTo(entries));
            }
        }

        /// <summary>
        /// Accept path for the queueing entry point. The earlier matrix contained no row that reached
        /// the successful enqueue exit, so the throughput and allocation of the accepted branch, the
        /// QueuePosition assignment and the matching dequeue were never measured.
        /// </summary>
        [Test]
        [Timeout(900_000)]
        public void Authority_QueueAcceptPath()
        {
            using var csv = new StressCsvWriter("core_scenarios.csv", StressPaths.CoreHeader);
            const int TargetCount = 1_000;

            foreach (ulong seed in Seeds)
            {
                var service = new InteractionAuthorityService(CreateOptions());
                for (int i = 0; i < TargetCount; i++)
                {
                    service.TryRegisterTarget(CreateActionTarget((ulong)(i + 1), "open"));
                }

                int requestId = 0;
                int tick = BaseTick;
                long observedQueuePositions = 0L;

                void Operation()
                {
                    requestId++;
                    tick++;
                    ulong instigator = (ulong)((requestId % 64) + 1);
                    ulong target = (ulong)(((requestId * 2654435761L) % TargetCount) + 1);
                    var request = new InteractionRequest(requestId, instigator, target, "open", tick, WorldId);

                    InteractionValidationResult result = service.TryQueueRequest(request, InteractionVector3.Zero, tick);
                    if (result.IsAccepted && result.QueuePosition > 0)
                    {
                        observedQueuePositions++;
                    }

                    // Drain immediately so the queue never saturates: this models a server tick loop
                    // that consumes one queued request per accepted request.
                    if (service.TryGetOrCreateQueue(target, out InteractionQueue queue))
                    {
                        queue.TryDequeue(out _);
                    }
                }

                (long accepted, long rejected) = RunOpBody(
                    csv,
                    "authority_queue_accept",
                    seed,
                    TargetCount,
                    200_000,
                    Operation,
                    () =>
                    {
                        InteractionMetricsSnapshot m = service.Metrics.GetSnapshot();
                        return (m.AcceptedRequests, m.RejectedRequests,
                            service.GetMemorySnapshot().QueueOwnerCount);
                    },
                    "TryQueueRequest accept plus immediate dequeue, queue depth pinned at <= 1");

                Assert.That(accepted, Is.GreaterThan(0L),
                    "Queue accept path must accept; otherwise the accepted branch is unmeasured.");
                Assert.That(accepted + rejected, Is.EqualTo(200_000L),
                    "Queue accept/reject window must equal one measured loop.");
                Assert.That(observedQueuePositions, Is.GreaterThan(0L),
                    "QueuePosition was never populated; the enqueue success exit was not reached.");
            }
        }

        [Test]
        [Timeout(600_000)]
        public void Authority_RejectionHotPaths()
        {
            using var csv = new StressCsvWriter("core_scenarios.csv", StressPaths.CoreHeader);
            var breakdownCsv = new StressCsvWriter("core_rejection_breakdown.csv",
                "scenario,seed,failure,count");

            foreach (ulong seed in Seeds)
            {
                // RateLimited: window never advances, 1 request per window, 64 instigators.
                var rateLimitedService = new InteractionAuthorityService(
                    CreateOptions(maxRequestsPerRateLimitWindow: 1, rateLimitWindowTicks: 30));
                for (int i = 0; i < 64; i++)
                {
                    rateLimitedService.TryRegisterTarget(CreateTarget((ulong)(i + 1)));
                }

                int rateRequestId = 0;
                void RateLimitedOperation()
                {
                    rateRequestId++;
                    ulong instigator = (ulong)((rateRequestId % 64) + 1);
                    var request = new InteractionRequest(rateRequestId, instigator, instigator, null, BaseTick, WorldId);
                    rateLimitedService.ValidateRequest(request, InteractionVector3.Zero, BaseTick);
                }

                (long rateAccepted, long rateRejected) = RunOpBody(
                    csv,
                    "authority_reject_ratelimited",
                    seed,
                    64,
                    200_000,
                    RateLimitedOperation,
                    () =>
                    {
                        InteractionMetricsSnapshot m = rateLimitedService.Metrics.GetSnapshot();
                        return (m.AcceptedRequests, m.RejectedRequests,
                            rateLimitedService.GetMemorySnapshot().RateLimitWindowCount);
                    },
                    "static tick, 1 request per window, steady-state reject path");

                Assert.That(rateRejected, Is.GreaterThan(rateAccepted),
                    "Rate-limited scenario must be dominated by rejections.");
                Assert.That(rateAccepted + rateRejected, Is.EqualTo(200_000L),
                    "Rate-limited accept/reject window must equal one measured loop.");
                WriteRejectionBreakdown(breakdownCsv, "authority_reject_ratelimited", seed, rateLimitedService);

                // DuplicateRequest: fixed RequestId, rate limiting disabled so the history check runs.
                var duplicateService = new InteractionAuthorityService(
                    CreateOptions(maxRequestsPerRateLimitWindow: 0));
                duplicateService.TryRegisterTarget(CreateTarget(1UL));

                void DuplicateOperation()
                {
                    var request = new InteractionRequest(7, 1UL, 1UL, null, BaseTick, WorldId);
                    duplicateService.ValidateRequest(request, InteractionVector3.Zero, BaseTick);
                }

                (long duplicateAccepted, long duplicateRejected) = RunOpBody(
                    csv,
                    "authority_reject_duplicate",
                    seed,
                    1,
                    200_000,
                    DuplicateOperation,
                    () =>
                    {
                        InteractionMetricsSnapshot m = duplicateService.Metrics.GetSnapshot();
                        return (m.AcceptedRequests, m.RejectedRequests, 0L);
                    },
                    "identical request id replayed, rate limit disabled");

                Assert.That(duplicateRejected, Is.GreaterThan(duplicateAccepted),
                    "Duplicate scenario must be dominated by duplicate rejections.");
                WriteRejectionBreakdown(breakdownCsv, "authority_reject_duplicate", seed, duplicateService);

                // OutOfRange: valid target, instigator far away.
                var rangeService = new InteractionAuthorityService(CreateOptions(maxRequestsPerRateLimitWindow: 0));
                rangeService.TryRegisterTarget(CreateTarget(1UL));
                int rangeRequestId = 0;

                void RangeOperation()
                {
                    rangeRequestId++;
                    var request = new InteractionRequest(rangeRequestId, 1UL, 1UL, null, BaseTick, WorldId);
                    rangeService.ValidateRequest(request, new InteractionVector3(500f, 0f, 500f), BaseTick);
                }

                (long rangeAccepted, long rangeRejected) = RunOpBody(
                    csv,
                    "authority_reject_outofrange",
                    seed,
                    1,
                    200_000,
                    RangeOperation,
                    () =>
                    {
                        InteractionMetricsSnapshot m = rangeService.Metrics.GetSnapshot();
                        return (m.AcceptedRequests, m.RejectedRequests, 0L);
                    },
                    "instigator 707 units from a 5-unit range target");

                Assert.That(rangeRejected, Is.GreaterThan(rangeAccepted),
                    "Out-of-range scenario must be dominated by range rejections.");
                WriteRejectionBreakdown(breakdownCsv, "authority_reject_outofrange", seed, rangeService);
            }
        }

        // ------------------------------------------------------------------
        // Rate limiter
        // ------------------------------------------------------------------

        [Test]
        [Timeout(900_000)]
        public void RateLimiter_ThroughputAndBoundedness()
        {
            using var csv = new StressCsvWriter("core_scenarios.csv", StressPaths.CoreHeader);

            foreach (int instigatorCount in new[] { 64, 4_096, 65_536 })
            {
                foreach (ulong seed in Seeds)
                {
                    var limiter = new InteractionRateLimiter();
                    int counter = 0;
                    int tick = BaseTick;
                    var rng = new StressRandom(seed);

                    void Operation()
                    {
                        counter++;
                        tick++;
                        ulong key = (ulong)((counter % instigatorCount) + 1);
                        limiter.TryConsume(key, tick + (int)(rng.NextUInt64() % 3UL), 8, 30);
                    }

                    RunOpBody(
                        csv,
                        "ratelimiter_consume_instigators_" + instigatorCount,
                        seed,
                        instigatorCount,
                        200_000,
                        Operation,
                        () => (0L, 0L, limiter.GetMemorySnapshot().WindowCount),
                        "max 8 requests per 30-tick window, tick advancing every call");

                    InteractionRateLimiterMemorySnapshot snapshot = limiter.GetMemorySnapshot();
                    Assert.That(snapshot.WindowCount, Is.LessThanOrEqualTo(InteractionRateLimiter.MaximumWindowCount),
                        "Rate limiter window count must never exceed its declared ceiling.");
                }
            }
        }

        [Test]
        public void RateLimiter_SaturatedAdmissionFailsClosed()
        {
            using var csv = new StressCsvWriter("core_boundedness.csv",
                "scenario,entered,expected_cap,observed_count,rejected_admission,expired_pruned,explicit_removed,verdict");

            var limiter = new InteractionRateLimiter();
            int Cap = InteractionRateLimiter.MaximumWindowCount;

            for (int i = 0; i < Cap; i++)
            {
                limiter.TryConsume((ulong)(i + 1), BaseTick, 8, 10_000);
            }

            InteractionRateLimiterMemorySnapshot filled = limiter.GetMemorySnapshot();
            csv.Row("saturated_fill", StressCsvWriter.Int(Cap), StressCsvWriter.Int(Cap),
                StressCsvWriter.Int(filled.WindowCount), StressCsvWriter.Int(filled.RejectedAdmissionCount),
                StressCsvWriter.Int(filled.ExpiredWindowRemovalCount), StressCsvWriter.Int(filled.ExplicitWindowRemovalCount),
                "asserted");
            Assert.That(filled.WindowCount, Is.EqualTo(Cap));

            int admittedBeyondCap = 0;
            for (int i = 0; i < 10_000; i++)
            {
                if (limiter.TryConsume((ulong)(Cap + i + 1), BaseTick, 8, 10_000))
                {
                    admittedBeyondCap++;
                }
            }

            InteractionRateLimiterMemorySnapshot pressured = limiter.GetMemorySnapshot();
            csv.Row("saturated_overflow", "10000", StressCsvWriter.Int(Cap),
                StressCsvWriter.Int(pressured.WindowCount), StressCsvWriter.Int(pressured.RejectedAdmissionCount),
                StressCsvWriter.Int(pressured.ExpiredWindowRemovalCount), StressCsvWriter.Int(pressured.ExplicitWindowRemovalCount),
                "asserted");
            Assert.That(pressured.WindowCount, Is.EqualTo(Cap),
                "Window count must stay pinned at the ceiling under a distinct-key flood.");
            Assert.That(admittedBeyondCap, Is.Zero,
                "A flooded limiter must fail closed rather than grow without bound.");

            // Expiry must reclaim windows when the tick advances past the window length.
            for (int i = 0; i < Cap; i++)
            {
                limiter.TryConsume((ulong)(i + 1), BaseTick + 100_000, 8, 10_000);
            }

            InteractionRateLimiterMemorySnapshot afterExpiry = limiter.GetMemorySnapshot();
            csv.Row("expiry_reclaim", StressCsvWriter.Int(Cap), StressCsvWriter.Int(Cap),
                StressCsvWriter.Int(afterExpiry.WindowCount), StressCsvWriter.Int(afterExpiry.RejectedAdmissionCount),
                StressCsvWriter.Int(afterExpiry.ExpiredWindowRemovalCount), StressCsvWriter.Int(afterExpiry.ExplicitWindowRemovalCount),
                "observational");
            Assert.That(afterExpiry.WindowCount, Is.LessThanOrEqualTo(Cap));
        }

        // ------------------------------------------------------------------
        // Interaction queue
        // ------------------------------------------------------------------

        [Test]
        [Timeout(900_000)]
        public void Queue_ThroughputCapacityAndCancelScan()
        {
            using var csv = new StressCsvWriter("core_scenarios.csv", StressPaths.CoreHeader);

            foreach (ulong seed in Seeds)
            {
                // Steady state: enqueue then dequeue, capacity 64.
                var queue = new InteractionQueue(64);
                int counter = 0;

                void Operation()
                {
                    counter++;
                    var request = new InteractionRequest(counter, (ulong)((counter % 8) + 1), 1UL, "open", BaseTick, WorldId);
                    if (queue.TryEnqueue(request))
                    {
                        queue.TryDequeue(out _);
                    }
                }

                RunOpBody(
                    csv,
                    "queue_enqueue_dequeue_capacity64",
                    seed,
                    64,
                    200_000,
                    Operation,
                    () => (0L, 0L, queue.Count),
                    "paired enqueue/dequeue, 8 instigators");

                // Full queue: every enqueue is rejected, measures the QueueFull path.
                var fullQueue = new InteractionQueue(64);
                for (int i = 0; i < 64; i++)
                {
                    fullQueue.TryEnqueue(new InteractionRequest(i + 1, 1UL, 1UL, "open", BaseTick, WorldId));
                }

                int fullCounter = 100_000;
                void FullOperation()
                {
                    fullCounter++;
                    fullQueue.TryEnqueue(new InteractionRequest(fullCounter, 1UL, 1UL, "open", BaseTick, WorldId));
                }

                RunOpBody(
                    csv,
                    "queue_reject_when_full",
                    seed,
                    64,
                    200_000,
                    FullOperation,
                    () => (0L, 0L, fullQueue.Count),
                    "queue pinned at capacity 64");

                // Cancel scan: full queue, cancel a stable instigator that is not present, so the
                // implementation must walk and re-enqueue every entry.
                var scanQueue = new InteractionQueue(64);
                for (int i = 0; i < 64; i++)
                {
                    scanQueue.TryEnqueue(new InteractionRequest(i + 1, 1UL, 1UL, "open", BaseTick, WorldId));
                }

                void CancelScanOperation()
                {
                    scanQueue.CancelStable(0xFFFF_FFFF_FFFF_FFFFUL);
                }

                RunOpBody(
                    csv,
                    "queue_cancel_stable_scan64",
                    seed,
                    64,
                    100_000,
                    CancelScanOperation,
                    () => (0L, 0L, scanQueue.Count),
                    "CancelStable with no match, full re-enqueue walk");
            }
        }

        // ------------------------------------------------------------------
        // Network replay window
        // ------------------------------------------------------------------

        [Test]
        [Timeout(600_000)]
        public void ReplayWindow_AdmissionCostAndBound()
        {
            using var csv = new StressCsvWriter("core_scenarios.csv", StressPaths.CoreHeader);

            foreach (ulong seed in Seeds)
            {
                // Fresh instigator per call: exercises window creation, the most allocation-heavy path.
                var replayWindow = new InteractionRequestReplayWindow();
                int counter = 0;

                void NewInstigatorOperation()
                {
                    counter++;
                    ulong instigator = (ulong)(counter % InteractionRequestReplayWindow.MaxTrackedInstigators) + 1UL;
                    replayWindow.TryAccept(instigator, 1 + (counter / InteractionRequestReplayWindow.MaxTrackedInstigators));
                }

                RunOpBody(
                    csv,
                    "replaywindow_new_instigator",
                    seed,
                    InteractionRequestReplayWindow.MaxTrackedInstigators,
                    100_000,
                    NewInstigatorOperation,
                    () => (0L, 0L, replayWindow.TrackedInstigatorCount),
                    "rotating distinct instigators, exercises window allocation and eviction");

                Assert.That(replayWindow.TrackedInstigatorCount,
                    Is.LessThanOrEqualTo(InteractionRequestReplayWindow.MaxTrackedInstigators),
                    "Replay window must stay bounded under instigator pressure.");

                // Steady state: one instigator, monotonic request ids.
                var steadyWindow = new InteractionRequestReplayWindow();
                int steadyCounter = 0;

                void SteadyOperation()
                {
                    steadyCounter++;
                    steadyWindow.TryAccept(1UL, steadyCounter);
                }

                RunOpBody(
                    csv,
                    "replaywindow_monotonic_single_instigator",
                    seed,
                    1,
                    200_000,
                    SteadyOperation,
                    () => (0L, 0L, steadyWindow.TrackedInstigatorCount),
                    "monotonic request ids, single instigator");

                // Duplicate storm: same request id repeatedly.
                var duplicateWindow = new InteractionRequestReplayWindow();
                duplicateWindow.TryAccept(1UL, 10);

                void DuplicateOperation()
                {
                    duplicateWindow.TryAccept(1UL, 10);
                }

                RunOpBody(
                    csv,
                    "replaywindow_duplicate_reject",
                    seed,
                    1,
                    200_000,
                    DuplicateOperation,
                    () => (0L, 0L, duplicateWindow.TrackedInstigatorCount),
                    "identical request id, duplicate reject path");
            }
        }
    }
}
