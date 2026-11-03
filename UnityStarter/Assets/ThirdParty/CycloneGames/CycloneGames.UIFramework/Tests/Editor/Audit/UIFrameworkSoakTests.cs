using System;
using System.Collections.Generic;
using System.Diagnostics;
using CycloneGames.UIFramework.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.UIFramework.Tests.Editor.Audit
{
    /// <summary>
    /// Long-running open/query/close soak with memory, GC and throughput sampling.
    /// The loop body is synchronous so a task that never converges fails instead of hanging.
    /// Duration defaults to 300 s; override with the UIFW_SOAK_SECONDS environment variable.
    /// </summary>
    [Category("UIFrameworkAudit")]
    public sealed class UIFrameworkSoakTests
    {
        private const string DurationEnvironmentVariable = "UIFW_SOAK_SECONDS";
        private const int DefaultTargetSeconds = 300;
        private const int MinimumTargetSeconds = 5;
        private const int MaximumTargetSeconds = 3600;
        private const int SampleIntervalIterations = 1000;
        private const int BaselineIteration = 200;
        private const long MaximumManagedGrowthBytes = 1024L * 1024L;
        // A lifecycle transaction allocates ~1 KB; retaining a handful of bytes per transaction
        // is heap noise, while a real leak grows with the transaction count. The bound is
        // therefore a rate, not an absolute size.
        private const long MaximumRetainedBytesPerIteration = 16L;
        private const int IdPoolSize = 32;
        private const int FirstAssetIdIndex = 11;
        private const int FirstPathIdIndex = 22;
        private const int PairTransactionPeriod = 8;
        private const int TimeoutMilliseconds = 600000;
        private const long MaximumMonoUsedGrowthBytes = 2L * 1024L * 1024L;
        private const int MaximumUnexplainedGen2Collections = 10;

        /// <summary>Bucket wall-clock at each sample, used for the throughput-drift check.</summary>
        private static readonly List<double> s_sampleElapsed = new List<double>(64);
        private static readonly List<long> s_sampleIteration = new List<long>(64);

        private static void Emit(string line)
        {
            // TestContext.Progress is not persisted by the Unity batch log, so each line is also
            // written through Debug.Log, which lands in -logFile verbatim.
            TestContext.Progress.WriteLine(line);
            UnityEngine.Debug.Log(line);
        }

        [Test]
        [Timeout(TimeoutMilliseconds)]
        public void Soak_OpenQueryCloseTransactions_ForConfiguredDuration()
        {
            int targetSeconds = ResolveTargetSeconds();
            Emit(
                "SOAK,config,targetSeconds=" + targetSeconds +
                ",idPool=" + IdPoolSize +
                ",pairTransactionPeriod=" + PairTransactionPeriod +
                ",sampleInterval=" + SampleIntervalIterations +
                ",growthLimitBytes=" + MaximumManagedGrowthBytes +
                ",monoGrowthLimitBytes=" + MaximumMonoUsedGrowthBytes +
                ",maxUnexplainedGen2=" + MaximumUnexplainedGen2Collections);
            Emit(
                "SOAK,columns,iteration,elapsedSeconds,managedBytes,allocatedBytesCumulative," +
                "monoUsedBytes,unityAllocatedBytes,unityHeapBytes,gcGen0,gcGen1,gcGen2," +
                "navigationSessions,plainSessions,layerWindows,navigationNodes,bindCount,bindDisposeCount");

            using (var harness = new AuditHarness())
            {
                SoakContext context = CreateContext(harness);
                long allocatedBaseline = GC.GetAllocatedBytesForCurrentThread();
                long baselineManagedBytes = 0L;
                bool baselineCaptured = false;
                long midManagedBytes = 0L;
                long midIteration = 0L;
                long monoUsedMid = 0L;
                bool midCaptured = false;
                long iteration = 0L;
                int gen0Start = GC.CollectionCount(0);
                int gen1Start = GC.CollectionCount(1);
                int gen2Start = GC.CollectionCount(2);
                long monoUsedStart = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
                long unityAllocatedStart = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
                s_sampleElapsed.Clear();
                s_sampleIteration.Clear();

                var stopwatch = new Stopwatch();
                stopwatch.Start();
                while (stopwatch.Elapsed.TotalSeconds < targetSeconds)
                {
                    RunTransaction(context, iteration);

                    if (iteration == BaselineIteration)
                    {
                        baselineManagedBytes = GC.GetTotalMemory(true);
                        baselineCaptured = true;
                    }

                    // Steady-state baseline: captured at half the configured duration, after JIT
                    // and type-initialisation warm-up are finished. Retained-memory growth is
                    // measured against this point, because comparing the end of a long soak with
                    // iteration 200 measures warm-up, not leakage.
                    if (!midCaptured && stopwatch.Elapsed.TotalSeconds >= targetSeconds * 0.5)
                    {
                        midManagedBytes = GC.GetTotalMemory(true);
                        midIteration = iteration;
                        monoUsedMid = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
                        midCaptured = true;
                    }

                    if (iteration % SampleIntervalIterations == 0L)
                    {
                        Sample(context, iteration, stopwatch, allocatedBaseline);
                    }

                    iteration++;
                }

                stopwatch.Stop();

                long finalManagedBytes = GC.GetTotalMemory(true);
                long totalAllocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBaseline;
                long growth = baselineCaptured ? finalManagedBytes - baselineManagedBytes : -1L;
                long steadyGrowth = midCaptured ? finalManagedBytes - midManagedBytes : -1L;
                // Forced collections: one per Sample (GetTotalMemory(true)), one for the
                // iteration-200 warm-up baseline, one for the halfway steady baseline, one here.
                long forcedCollectionCount = s_sampleElapsed.Count + 3;
                long monoUsedFinal = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
                long unityAllocatedFinal = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
                int gen0End = GC.CollectionCount(0);
                int gen1End = GC.CollectionCount(1);
                int gen2End = GC.CollectionCount(2);
                long unexplainedGen2 = (long)(gen2End - gen2Start) - forcedCollectionCount;

                double earlyRate;
                double lateRate;
                ComputeThroughputDrift(out earlyRate, out lateRate);
                double monoUsedGrowth = midCaptured ? monoUsedFinal - monoUsedMid : -1L;
                long secondHalfIterations = midCaptured ? iteration - midIteration : 0L;
                double retainedPerIteration = secondHalfIterations > 0L
                    ? (double)steadyGrowth / secondHalfIterations
                    : 0.0;
                long steadyGrowthBudget = 1024L * 1024L +
                    secondHalfIterations * MaximumRetainedBytesPerIteration;

                Emit(
                    "SOAK,summary,iterations=" + iteration +
                    ",elapsedSeconds=" + stopwatch.Elapsed.TotalSeconds.ToString("F2") +
                    ",targetSeconds=" + targetSeconds +
                    ",baselineManagedBytes=" + baselineManagedBytes +
                    ",finalManagedBytes=" + finalManagedBytes +
                    ",managedGrowthBytes=" + growth +
                    ",midManagedBytes=" + midManagedBytes +
                    ",steadyGrowthBytes=" + steadyGrowth +
                    ",secondHalfIterations=" + secondHalfIterations +
                    ",retainedBytesPerIteration=" + retainedPerIteration.ToString("F2") +
                    ",totalAllocatedBytes=" + totalAllocated +
                    ",allocatedPerIteration=" +
                    (iteration == 0L ? 0L : totalAllocated / iteration) +
                    ",monoUsedStart=" + monoUsedStart +
                    ",monoUsedFinal=" + monoUsedFinal +
                    ",monoUsedGrowthBytes=" + monoUsedGrowth +
                    ",unityAllocatedStart=" + unityAllocatedStart +
                    ",unityAllocatedFinal=" + unityAllocatedFinal +
                    ",gcGen0=" + (gen0End - gen0Start) +
                    ",gcGen1=" + (gen1End - gen1Start) +
                    ",gcGen2=" + (gen2End - gen2Start) +
                    ",forcedCollections=" + forcedCollectionCount +
                    ",unexplainedGen2=" + unexplainedGen2 +
                    ",earlyIterationsPerSecond=" + earlyRate.ToString("F2") +
                    ",lateIterationsPerSecond=" + lateRate.ToString("F2") +
                    ",throughputDriftRatio=" +
                    (earlyRate <= 0.0 ? 0.0 : lateRate / earlyRate).ToString("F3"));

                Assert.That(iteration, Is.GreaterThan(0L), "the soak loop must run at least one transaction");

                Assert.That(
                    baselineCaptured,
                    Is.True,
                    "the soak must reach iteration " + BaselineIteration + " to capture the baseline");

                Assert.That(
                    stopwatch.Elapsed.TotalSeconds,
                    Is.GreaterThanOrEqualTo(targetSeconds - 1),
                    "the soak must actually run for the configured duration; elapsed " +
                    stopwatch.Elapsed.TotalSeconds.ToString("F2") + " s of " + targetSeconds + " s");

                Assert.That(
                    midCaptured,
                    Is.True,
                    "the soak must reach its halfway point to capture the steady-state baseline");

                Assert.That(
                    steadyGrowth,
                    Is.LessThan(steadyGrowthBudget),
                    "retained managed memory grew by " + steadyGrowth + " B over " +
                    secondHalfIterations + " second-half transactions (" +
                    retainedPerIteration.ToString("F2") + " B/tx, budget " +
                    MaximumRetainedBytesPerIteration + " B/tx + 1 MB). mid=" + midManagedBytes +
                    " B final=" + finalManagedBytes + " B. A constant few bytes per transaction " +
                    "is heap noise; a rate that climbs with the transaction count is a leak.");

                Assert.That(
                    monoUsedGrowth,
                    Is.LessThan(steadyGrowthBudget),
                    "Unity-side managed heap (Profiler.GetMonoUsedSizeLong) grew by " + monoUsedGrowth +
                    " B over " + secondHalfIterations + " second-half transactions (" +
                    (secondHalfIterations <= 0L ? 0.0 : monoUsedGrowth / secondHalfIterations)
                        .ToString("F2") +
                    " B/tx, same budget as the managed heap). Growth past the budget means " +
                    "Unity-tracked managed objects are being retained per transaction.");

                Assert.That(
                    unexplainedGen2,
                    Is.LessThanOrEqualTo(MaximumUnexplainedGen2Collections),
                    "the framework induced " + unexplainedGen2 + " generation-2 collections beyond the " +
                    (forcedCollectionCount + " forced probes; sustained churn must not pressure gen2, ") +
                    "which would show up as periodic frame spikes in a Player.");

                Assert.That(
                    lateRate,
                    Is.GreaterThanOrEqualTo(earlyRate * 0.5),
                    "throughput collapsed during the soak: early=" + earlyRate.ToString("F2") +
                    " it/s late=" + lateRate.ToString("F2") +
                    " it/s. A healthy framework holds a flat iteration rate; decline indicates " +
                    "growing scans, fragmentation or accumulation in the hot path.");

                // Every dimension of the ownership surface must be empty at the end.
                AuditInvariants.AssertSettled(
                    context.NavigationService,
                    context.Layer,
                    context.Navigation,
                    context.Provider,
                    0,
                    "soak final state (navigation service)");

                AuditInvariants.AssertSettled(
                    context.PlainService,
                    context.Layer,
                    null,
                    context.Provider,
                    0,
                    "soak final state (plain service)");

                Assert.That(
                    context.Layer.WindowCount,
                    Is.EqualTo(0),
                    "the layer must be empty after the soak");
                Assert.That(
                    context.Navigation.CopyHistory(context.HistoryScratch),
                    Is.EqualTo(0),
                    "the navigation graph must be empty after the soak");
                Assert.That(
                    AuditInvariants.LeaseBalance(context.Provider),
                    Is.True,
                    "provider leases must balance after the soak: config " +
                    context.Provider.ConfigurationDisposeCount + "/" +
                    context.Provider.ConfigurationAcquireCount + " prefab " +
                    context.Provider.PrefabDisposeCount + "/" + context.Provider.PrefabAcquireCount +
                    " doubleDispose=" + context.Provider.DoubleDisposeCount);
                Assert.That(
                    context.Provider.DoubleDisposeCount,
                    Is.EqualTo(0),
                    "no provider lease may be disposed twice during the soak");
                Assert.That(
                    context.Binder.DisposeCount,
                    Is.EqualTo(context.Binder.BindCount),
                    "every binding created during the soak must be disposed exactly once: bound=" +
                    context.Binder.BindCount + " disposed=" + context.Binder.DisposeCount);
            }
        }

        // ------------------------------------------------------------------ loop body

        private static void RunTransaction(SoakContext context, long iteration)
        {
            bool useNavigationService = (iteration & 1L) == 0L;
            UIService service = useNavigationService ? context.NavigationService : context.PlainService;
            IUINavigationService navigation = useNavigationService ? context.Navigation : null;
            int idIndex = (int)(iteration % IdPoolSize);
            int policyIndex = (int)((iteration >> 5) % 3L);
            ChildClosePolicy policy = PolicyFromIndex(policyIndex);

            if (idIndex == 0)
            {
                // Rotate the D6/D7 variants once per pool cycle: reflection is expensive enough
                // that doing it every iteration would distort the transaction cost measurement.
                int canvas = (int)((iteration >> 5) & 1L);
                int cull = (int)((iteration >> 6) & 1L);
                for (int i = 0; i < IdPoolSize; i++)
                {
                    AuditHarness.ApplyCanvasPolicy(context.Configurations[i], canvas);
                    AuditHarness.ApplyCullWhenHidden(context.Configurations[i], cull);
                }
            }

            if ((iteration % PairTransactionPeriod) == 0L)
            {
                RunPairTransaction(context, service, navigation, idIndex, policy, iteration);
            }
            else
            {
                RunSingleTransaction(context, service, navigation, idIndex, policy, iteration);
            }
        }

        private static void RunSingleTransaction(
            SoakContext context,
            UIService service,
            IUINavigationService navigation,
            int idIndex,
            ChildClosePolicy policy,
            long iteration)
        {
            context.Provider.ResetCounters();
            string windowId = context.WindowIds[idIndex];

            UIWindow window = Open(context, service, idIndex, null);
            if (window == null)
            {
                Assert.Fail("soak iteration " + iteration + " | the open of '" + windowId + "' returned no window");
            }

            QuerySurface(context, service, navigation, windowId, 1);

            bool closed = service.CloseAsync(windowId, policy).GetAwaiter().GetResult();
            if (!closed)
            {
                Assert.Fail("soak iteration " + iteration + " | closing '" + windowId +
                            "' reported false for an active window");
            }

            AuditInvariants.CheckSettled(
                service,
                context.Layer,
                navigation,
                context.Provider,
                0,
                "soak iteration " + iteration + " (single)");
        }

        private static void RunPairTransaction(
            SoakContext context,
            UIService service,
            IUINavigationService navigation,
            int idIndex,
            ChildClosePolicy policy,
            long iteration)
        {
            context.Provider.ResetCounters();
            string parentId = context.WindowIds[idIndex];
            int childIndex = (idIndex + 1) % IdPoolSize;
            string childId = context.WindowIds[childIndex];

            Open(context, service, idIndex, null);
            Open(context, service, childIndex, parentId);

            AuditInvariants.CheckSettled(
                service,
                context.Layer,
                navigation,
                context.Provider,
                2,
                "soak iteration " + iteration + " (pair opened)");

            bool closed = service.CloseAsync(parentId, policy).GetAwaiter().GetResult();
            if (!closed)
            {
                Assert.Fail("soak iteration " + iteration + " | closing the pair parent '" + parentId +
                            "' reported false");
            }

            // Only a navigation-aware service treats the child as a descendant; without a
            // navigation service the ChildClosePolicy cannot reach it by design.
            bool cascadeRemovesChild = navigation != null && policy == ChildClosePolicy.Cascade;
            int expectedAfterParent = cascadeRemovesChild ? 0 : 1;

            AuditInvariants.CheckSettled(
                service,
                context.Layer,
                navigation,
                context.Provider,
                expectedAfterParent,
                "soak iteration " + iteration + " (pair parent closed with " + policy + ")");

            if (expectedAfterParent == 1)
            {
                bool childClosed = service.CloseAsync(childId, policy).GetAwaiter().GetResult();
                if (!childClosed)
                {
                    Assert.Fail("soak iteration " + iteration + " | closing the pair child '" + childId +
                                "' reported false");
                }
            }

            AuditInvariants.CheckSettled(
                service,
                context.Layer,
                navigation,
                context.Provider,
                0,
                "soak iteration " + iteration + " (pair)");
        }

        private static UIWindow Open(SoakContext context, UIService service, int idIndex, string openerId)
        {
            var options = new UIOpenOptions(openerId, null, null, default, null, false);
            if (context.Sources[idIndex] == AuditDims.SourcePrefabReference)
            {
                return service.OpenAsync(context.Configurations[idIndex], options)
                    .GetAwaiter()
                    .GetResult();
            }

            return service.OpenAsync(context.WindowIds[idIndex], options).GetAwaiter().GetResult();
        }

        private static void QuerySurface(
            SoakContext context,
            UIService service,
            IUINavigationService navigation,
            string windowId,
            int expectedActive)
        {
            UIWindow hit;
            if (!service.TryGetWindow(windowId, out hit) || hit == null)
            {
                Assert.Fail("soak query | TryGetWindow missed the active window '" + windowId + "'");
            }

            UIWindow miss;
            if (service.TryGetWindow("SoakMissingWindow", out miss))
            {
                Assert.Fail("soak query | TryGetWindow hit an id that was never opened");
            }

            if (service.CopyActiveWindows(context.WindowScratch) != expectedActive)
            {
                Assert.Fail("soak query | CopyActiveWindows did not report " + expectedActive + " windows");
            }

            UIPerformanceStats stats = service.GetPerformanceStats();
            if (stats.SessionCount != expectedActive || stats.OpeningWindowCount != 0 || stats.ClosingWindowCount != 0)
            {
                Assert.Fail("soak query | stats disagree with the active window count: " +
                            AuditInvariants.Describe(stats));
            }

            if (service.CopyLayerRuntimeStats(context.LayerScratch) != stats.LayerCount)
            {
                Assert.Fail("soak query | CopyLayerRuntimeStats did not report every layer");
            }

            if (navigation != null)
            {
                if (navigation.CopyHistory(context.HistoryScratch) != expectedActive)
                {
                    Assert.Fail("soak query | the navigation graph does not match the active window count");
                }

                navigation.CopyAncestors(windowId, context.IdScratch);
                navigation.CopyChildren(windowId, context.IdScratch);
            }
        }

        private static ChildClosePolicy PolicyFromIndex(int policyIndex)
        {
            switch (policyIndex)
            {
                case 1:
                    return ChildClosePolicy.Cascade;
                case 2:
                    return ChildClosePolicy.Detach;
                default:
                    return ChildClosePolicy.Reparent;
            }
        }

        // ------------------------------------------------------------------ sampling

        /// <summary>
        /// Compares the mean transaction rate of the first half of the soak against the second
        /// half. A flat framework keeps the ratio near 1; leaks and growing scans drag it down.
        /// </summary>
        private static void ComputeThroughputDrift(out double earlyRate, out double lateRate)
        {
            int count = s_sampleElapsed.Count;
            if (count < 4)
            {
                double whole = count == 0
                    ? 0.0
                    : s_sampleIteration[count - 1] / Math.Max(s_sampleElapsed[count - 1], 0.001);
                earlyRate = whole;
                lateRate = whole;
                return;
            }

            int mid = count / 2;
            double earlySeconds = s_sampleElapsed[mid - 1] - s_sampleElapsed[0];
            double lateSeconds = s_sampleElapsed[count - 1] - s_sampleElapsed[mid];
            long earlyIterations = s_sampleIteration[mid - 1] - s_sampleIteration[0];
            long lateIterations = s_sampleIteration[count - 1] - s_sampleIteration[mid];

            earlyRate = earlySeconds <= 0.0 ? 0.0 : earlyIterations / earlySeconds;
            lateRate = lateSeconds <= 0.0 ? 0.0 : lateIterations / lateSeconds;
        }

        private static void Sample(
            SoakContext context,
            long iteration,
            Stopwatch stopwatch,
            long allocatedBaseline)
        {
            long managedBytes = GC.GetTotalMemory(true);
            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBaseline;
            UIPerformanceStats navigationStats = context.NavigationService.GetPerformanceStats();
            UIPerformanceStats plainStats = context.PlainService.GetPerformanceStats();
            int navigationNodes = context.Navigation.CopyHistory(context.HistoryScratch);
            long monoUsed = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            long unityAllocated = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
            long unityHeap = UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong();

            s_sampleElapsed.Add(stopwatch.Elapsed.TotalSeconds);
            s_sampleIteration.Add(iteration);

            Emit(
                "SOAKSAMPLE,iteration=" + iteration +
                ",elapsedSeconds=" + stopwatch.Elapsed.TotalSeconds.ToString("F3") +
                ",managedBytes=" + managedBytes +
                ",allocatedBytesCumulative=" + allocatedBytes +
                ",monoUsedBytes=" + monoUsed +
                ",unityAllocatedBytes=" + unityAllocated +
                ",unityHeapBytes=" + unityHeap +
                ",gcGen0=" + GC.CollectionCount(0) +
                ",gcGen1=" + GC.CollectionCount(1) +
                ",gcGen2=" + GC.CollectionCount(2) +
                ",navigationSessions=" + navigationStats.SessionCount +
                ",plainSessions=" + plainStats.SessionCount +
                ",layerWindows=" + context.Layer.WindowCount +
                ",navigationNodes=" + navigationNodes +
                ",bindCount=" + context.Binder.BindCount +
                ",bindDisposeCount=" + context.Binder.DisposeCount);

            // The sample point is the expensive check; the per-iteration check stays allocation free.
            AuditInvariants.AssertSettled(
                context.NavigationService, context.Layer, context.Navigation, context.Provider, 0,
                "soak sample at iteration " + iteration + " (navigation service)");
            AuditInvariants.AssertSettled(
                context.PlainService, context.Layer, null, context.Provider, 0,
                "soak sample at iteration " + iteration + " (plain service)");
        }

        // ------------------------------------------------------------------ setup

        private static SoakContext CreateContext(AuditHarness harness)
        {
            var context = new SoakContext();
            context.Layer = harness.Layer;
            context.Provider = harness.CreateProvider();
            context.Navigation = harness.CreateNavigation(IdPoolSize * 2);
            context.Binder = new AuditBinder("SoakBinder");
            context.Binder.RecordEvents = false;

            context.WindowIds = new string[IdPoolSize];
            context.Sources = new int[IdPoolSize];
            context.Configurations = new UIWindowConfiguration[IdPoolSize];
            context.WindowScratch = new List<UIWindow>(8);
            context.LayerScratch = new List<UILayerRuntimeStats>(8);
            context.HistoryScratch = new List<UINavigationEntry>(IdPoolSize * 2);
            context.IdScratch = new List<string>(IdPoolSize);

            for (int i = 0; i < IdPoolSize; i++)
            {
                string windowId = "Soak" + i.ToString("D2");
                int source;
                if (i < FirstAssetIdIndex)
                {
                    source = AuditDims.SourcePrefabReference;
                }
                else if (i < FirstPathIdIndex)
                {
                    source = AuditDims.SourceAssetReference;
                }
                else
                {
                    source = AuditDims.SourcePathLocation;
                }

                UIWindowConfiguration configuration = source == AuditDims.SourcePrefabReference
                    ? harness.CreateDirect(windowId, i, false)
                    : harness.CreateProviderConfiguration(windowId, source, i);

                // Start every id on the inherit/no-cull variant; the loop rotates them.
                AuditHarness.ApplyCanvasPolicy(configuration, AuditDims.CanvasInherit);
                AuditHarness.ApplyCullWhenHidden(configuration, AuditDims.CullDisabled);

                context.WindowIds[i] = windowId;
                context.Sources[i] = source;
                context.Configurations[i] = configuration;
                context.Provider.Map(windowId, configuration, harness.CreatePrefab(windowId));
            }

            var binders = new IUIWindowBinder[] { context.Binder };
            context.NavigationService = harness.CreateService(
                context.Provider,
                AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, context.Navigation),
                binders);
            context.PlainService = harness.CreateService(
                context.Provider,
                AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, null),
                binders);

            return context;
        }

        private static int ResolveTargetSeconds()
        {
            string raw = Environment.GetEnvironmentVariable(DurationEnvironmentVariable);
            int parsed;
            if (!string.IsNullOrEmpty(raw) && int.TryParse(raw, out parsed))
            {
                if (parsed < MinimumTargetSeconds)
                {
                    return MinimumTargetSeconds;
                }

                if (parsed > MaximumTargetSeconds)
                {
                    return MaximumTargetSeconds;
                }

                return parsed;
            }

            return DefaultTargetSeconds;
        }

        private sealed class SoakContext
        {
            public UILayer Layer;
            public AuditAssetProvider Provider;
            public UINavigationService Navigation;
            public AuditBinder Binder;
            public UIService NavigationService;
            public UIService PlainService;
            public string[] WindowIds;
            public int[] Sources;
            public UIWindowConfiguration[] Configurations;
            public List<UIWindow> WindowScratch;
            public List<UILayerRuntimeStats> LayerScratch;
            public List<UINavigationEntry> HistoryScratch;
            public List<string> IdScratch;
        }
    }
}
