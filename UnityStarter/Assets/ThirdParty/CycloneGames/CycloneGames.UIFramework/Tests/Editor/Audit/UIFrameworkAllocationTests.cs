using System;
using System.Collections.Generic;
using CycloneGames.UIFramework.Runtime;
using NUnit.Framework;

namespace CycloneGames.UIFramework.Tests.Editor.Audit
{
    /// <summary>
    /// Steady-state allocation probes. After warm-up, query APIs with a pre-sized buffer must
    /// report zero bytes; creation APIs report a baseline. Measured with
    /// GC.GetAllocatedBytesForCurrentThread().
    /// </summary>
    [Category("UIFrameworkAudit")]
    public sealed class UIFrameworkAllocationTests
    {
        private const int WarmupIterations = 100;
        private const int MeasureIterations = 1000;

        private static bool _meterTracksAllocations;

        [OneTimeSetUp]
        public void VerifyAllocationMeter()
        {
            // Control probe: one managed allocation must be visible to the meter. Unity's Mono
            // build has been observed to report 0 for GC.GetAllocatedBytesForCurrentThread(); in
            // that case every zero reading in this fixture is vacuous, so the fixture skips with
            // an explicit reason instead of reporting a false zero.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            long before = GC.GetAllocatedBytesForCurrentThread();
            object control = new object();
            GC.KeepAlive(control);
            long delta = GC.GetAllocatedBytesForCurrentThread() - before;
            _meterTracksAllocations = delta > 0L;

            TestContext.Progress.WriteLine(
                "BENCH:allocationMeterControlBytes=" + delta);

            if (!_meterTracksAllocations)
            {
                Assert.Ignore(
                    "GC.GetAllocatedBytesForCurrentThread() reported 0 B for a control allocation, " +
                    "so this runtime cannot prove or disprove zero-GC behaviour through this API. " +
                    "Run the allocation probes under the Unity Profiler (or a Player build) instead; " +
                    "the audit report carries the static allocation ledger.");
            }
        }

        // ------------------------------------------------------------------ query APIs

        [Test]
        public void TryGetWindow_HitAndMiss_DoNotAllocate()
        {
            using (var harness = new AuditHarness())
            {
                AuditAssetProvider provider = harness.CreateProvider();
                UIService service = harness.CreateService(
                    provider, AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, null), null);
                UIWindow window = service.OpenAsync(harness.CreateDirect("AllocLookup", 0, false))
                    .GetAwaiter()
                    .GetResult();

                UIWindow sink;
                for (int i = 0; i < WarmupIterations; i++)
                {
                    service.TryGetWindow("AllocLookup", out sink);
                    service.TryGetWindow("AllocMissing", out sink);
                }

                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < MeasureIterations; i++)
                {
                    service.TryGetWindow("AllocLookup", out sink);
                    service.TryGetWindow("AllocMissing", out sink);
                }

                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Report("TryGetWindow(hit+miss)", allocated, MeasureIterations);

                Assert.That(
                    allocated,
                    Is.EqualTo(0L),
                    "TryGetWindow must not allocate once warm: expected 0 B over " +
                    MeasureIterations + " hit+miss pairs, observed " + allocated + " B (" +
                    BytesPerIteration(allocated, MeasureIterations) + " B/pair)");

                Assert.That(window, Is.Not.Null, "the probe window must stay alive");
                service.CloseAsync("AllocLookup").GetAwaiter().GetResult();
            }
        }

        [Test]
        public void CopyActiveWindows_WithSufficientCapacity_DoesNotAllocate()
        {
            using (var harness = new AuditHarness())
            {
                AuditAssetProvider provider = harness.CreateProvider();
                UIService service = harness.CreateService(
                    provider, AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, null), null);
                service.OpenAsync(harness.CreateDirect("AllocCopyA", 0, false)).GetAwaiter().GetResult();
                service.OpenAsync(harness.CreateDirect("AllocCopyB", 0, false)).GetAwaiter().GetResult();

                var destination = new List<UIWindow>(8);
                for (int i = 0; i < WarmupIterations; i++)
                {
                    service.CopyActiveWindows(destination);
                }

                int capacityBefore = destination.Capacity;
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < MeasureIterations; i++)
                {
                    service.CopyActiveWindows(destination);
                }

                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Report("CopyActiveWindows(sufficient)", allocated, MeasureIterations);

                Assert.That(
                    destination.Capacity,
                    Is.EqualTo(capacityBefore),
                    "CopyActiveWindows must reuse the caller buffer when its capacity is sufficient");
                Assert.That(
                    destination.Count,
                    Is.EqualTo(2),
                    "the probe must observe both active windows");
                Assert.That(
                    allocated,
                    Is.EqualTo(0L),
                    "CopyActiveWindows must not allocate with a sufficient buffer: expected 0 B over " +
                    MeasureIterations + " calls, observed " + allocated + " B (" +
                    BytesPerIteration(allocated, MeasureIterations) + " B/call)");
            }
        }

        [Test]
        public void CopyActiveWindows_WithInsufficientCapacity_GrowsBufferExactlyOnce()
        {
            using (var harness = new AuditHarness())
            {
                AuditAssetProvider provider = harness.CreateProvider();
                UIService service = harness.CreateService(
                    provider, AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, null), null);
                service.OpenAsync(harness.CreateDirect("AllocGrowA", 0, false)).GetAwaiter().GetResult();
                service.OpenAsync(harness.CreateDirect("AllocGrowB", 0, false)).GetAwaiter().GetResult();

                // Warm the method up on a buffer that is already large enough, so the measured
                // call only pays for the growth itself.
                var warm = new List<UIWindow>(8);
                for (int i = 0; i < WarmupIterations; i++)
                {
                    service.CopyActiveWindows(warm);
                }

                var small = new List<UIWindow>(1);
                int capacityBefore = small.Capacity;
                long before = GC.GetAllocatedBytesForCurrentThread();
                service.CopyActiveWindows(small);
                long growthBytes = GC.GetAllocatedBytesForCurrentThread() - before;

                Report("CopyActiveWindows(growth)", growthBytes, 1);

                Assert.That(
                    small.Capacity,
                    Is.GreaterThan(capacityBefore),
                    "an undersized buffer must be grown; capacity was " + capacityBefore +
                    " with 2 active windows");
                Assert.That(
                    growthBytes,
                    Is.GreaterThan(0L),
                    "growing a caller buffer must allocate; 0 B would mean the copy silently dropped windows");

                // Steady state after the growth must be allocation free again.
                for (int i = 0; i < WarmupIterations; i++)
                {
                    service.CopyActiveWindows(small);
                }

                long steadyBefore = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < MeasureIterations; i++)
                {
                    service.CopyActiveWindows(small);
                }

                long steady = GC.GetAllocatedBytesForCurrentThread() - steadyBefore;
                Report("CopyActiveWindows(after growth)", steady, MeasureIterations);
                Assert.That(
                    steady,
                    Is.EqualTo(0L),
                    "after the buffer grew once, CopyActiveWindows must stop allocating; observed " +
                    steady + " B over " + MeasureIterations + " calls");
            }
        }

        [Test]
        public void GetPerformanceStats_DoesNotAllocate()
        {
            using (var harness = new AuditHarness())
            {
                UINavigationService navigation = harness.CreateNavigation(8);
                AuditAssetProvider provider = harness.CreateProvider();
                UIService service = harness.CreateService(
                    provider, AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, navigation), null);
                service.OpenAsync(harness.CreateDirect("AllocStats", 0, false)).GetAwaiter().GetResult();

                for (int i = 0; i < WarmupIterations; i++)
                {
                    UIPerformanceStats warm = service.GetPerformanceStats();
                    Consume(warm.SessionCount);
                }

                long before = GC.GetAllocatedBytesForCurrentThread();
                long sink = 0;
                for (int i = 0; i < MeasureIterations; i++)
                {
                    UIPerformanceStats stats = service.GetPerformanceStats();
                    sink += stats.SessionCount + stats.OpenWindowCount + stats.LayerCount;
                }

                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Report("GetPerformanceStats", allocated, MeasureIterations);

                Assert.That(sink, Is.GreaterThan(0L), "the stats probe must observe a live session");
                Assert.That(
                    allocated,
                    Is.EqualTo(0L),
                    "GetPerformanceStats returns a struct and must not allocate: expected 0 B over " +
                    MeasureIterations + " calls, observed " + allocated + " B (" +
                    BytesPerIteration(allocated, MeasureIterations) + " B/call)");
            }
        }

        [Test]
        public void CopyLayerRuntimeStats_DoesNotAllocate()
        {
            using (var harness = new AuditHarness())
            {
                AuditAssetProvider provider = harness.CreateProvider();
                UIService service = harness.CreateService(
                    provider, AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, null), null);
                service.OpenAsync(harness.CreateDirect("AllocLayers", 0, false)).GetAwaiter().GetResult();

                var destination = new List<UILayerRuntimeStats>(8);
                for (int i = 0; i < WarmupIterations; i++)
                {
                    service.CopyLayerRuntimeStats(destination);
                }

                int capacityBefore = destination.Capacity;
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < MeasureIterations; i++)
                {
                    service.CopyLayerRuntimeStats(destination);
                }

                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Report("CopyLayerRuntimeStats", allocated, MeasureIterations);

                Assert.That(
                    destination.Capacity,
                    Is.EqualTo(capacityBefore),
                    "CopyLayerRuntimeStats must reuse the caller buffer");
                Assert.That(
                    destination.Count,
                    Is.GreaterThan(0),
                    "the probe must observe at least one layer");
                Assert.That(
                    allocated,
                    Is.EqualTo(0L),
                    "CopyLayerRuntimeStats must not allocate: expected 0 B over " + MeasureIterations +
                    " calls, observed " + allocated + " B (" +
                    BytesPerIteration(allocated, MeasureIterations) + " B/call)");
            }
        }

        [Test]
        public void NavigationQueries_WithSufficientCapacity_DoNotAllocate()
        {
            var navigation = new UINavigationService(8);
            navigation.Register("AllocRoot");
            navigation.Register("AllocChild", "AllocRoot");
            navigation.Register("AllocGrandChild", "AllocChild");

            var ids = new List<string>(8);
            var history = new List<UINavigationEntry>(8);

            for (int i = 0; i < WarmupIterations; i++)
            {
                navigation.CopyHistory(history);
                navigation.CopyAncestors("AllocGrandChild", ids);
                navigation.CopyChildren("AllocRoot", ids);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < MeasureIterations; i++)
            {
                navigation.CopyHistory(history);
                navigation.CopyAncestors("AllocGrandChild", ids);
                navigation.CopyChildren("AllocRoot", ids);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Report("Navigation CopyHistory+CopyAncestors+CopyChildren", allocated, MeasureIterations);

            Assert.That(
                allocated,
                Is.EqualTo(0L),
                "IUINavigationService copy queries must not allocate with sufficient buffers: " +
                "expected 0 B over " + MeasureIterations + " triple calls, observed " + allocated +
                " B (" + BytesPerIteration(allocated, MeasureIterations) + " B/triple)");
        }

        // ------------------------------------------------------------------ creation APIs

        [Test]
        public void OpenCloseTransaction_ReportsManagedAllocationBaseline()
        {
            const int warmupTransactions = 32;
            const int measuredTransactions = 64;
            const long ceilingPerTransaction = 65536L;

            using (var harness = new AuditHarness())
            {
                AuditAssetProvider provider = harness.CreateProvider();
                UIService service = harness.CreateService(
                    provider, AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, null), null);
                UIWindowConfiguration configuration =
                    harness.CreateDirect("AllocTransaction", 0, false);

                long warmupBytes = MeasureTransactions(service, configuration, warmupTransactions);
                long measuredBytes = MeasureTransactions(service, configuration, measuredTransactions);
                long perTransaction = measuredBytes / measuredTransactions;

                Report("OpenCloseTransaction(total)", measuredBytes, measuredTransactions);
                TestContext.Progress.WriteLine(
                    "ALLOC:OpenCloseTransaction perTransaction=" + perTransaction +
                    " B warmupPerTransaction=" + (warmupBytes / warmupTransactions) +
                    " B composition=Instantiate+UIWindowLifetimeRelay+IUIWindowBinding[]+CanvasGroup");

                Assert.That(
                    measuredBytes,
                    Is.GreaterThan(0L),
                    "a full open+close transaction instantiates a GameObject and must allocate");
                Assert.That(
                    perTransaction,
                    Is.LessThan(ceilingPerTransaction),
                    "an open+close transaction must stay below the regression ceiling of " +
                    ceilingPerTransaction + " B; observed " + perTransaction + " B per transaction");

                Assert.That(
                    AuditHarness.CountAttachedWindowObjects(harness.Layer),
                    Is.EqualTo(0),
                    "every probe transaction must leave the layer empty");
                Assert.That(
                    AuditInvariants.LeaseBalance(provider),
                    Is.True,
                    "the allocation probe must not leak provider leases: acquired=" +
                    provider.ConfigurationAcquireCount + " disposed=" + provider.ConfigurationDisposeCount +
                    " prefabAcquired=" + provider.PrefabAcquireCount +
                    " prefabDisposed=" + provider.PrefabDisposeCount +
                    " doubleDispose=" + provider.DoubleDisposeCount);
            }
        }

        [Test]
        public void PresenterBinder_MissingRegistration_DoesNotAllocate()
        {
            using (var harness = new AuditHarness())
            {
                AuditAssetProvider provider = harness.CreateProvider();
                UIService service = harness.CreateService(
                    provider, AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, null), null);
                UIWindow window = service.OpenAsync(harness.CreateDirect("AllocPresenter", 0, false))
                    .GetAwaiter()
                    .GetResult();

                var binder = new UIPresenterBinder(4);
                var context = new UIWindowBindingContext(window, service);

                for (int i = 0; i < WarmupIterations; i++)
                {
                    IUIWindowBinding warm = binder.Bind(context);
                    Consume(warm == null ? 0 : 1);
                }

                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < MeasureIterations; i++)
                {
                    IUIWindowBinding unbound = binder.Bind(context);
                    Consume(unbound == null ? 0 : 1);
                }

                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Report("UIPresenterBinder.Bind(miss)", allocated, MeasureIterations);

                Assert.That(
                    allocated,
                    Is.EqualTo(0L),
                    "binding a window without a presenter registration returns null and must not " +
                    "allocate: expected 0 B over " + MeasureIterations + " calls, observed " +
                    allocated + " B (" + BytesPerIteration(allocated, MeasureIterations) + " B/call)");

                service.CloseAsync("AllocPresenter").GetAwaiter().GetResult();
            }
        }

        [Test]
        public void PresenterBinder_BindSuccess_ReportsAllocationBaseline()
        {
            const long ceilingPerBind = 4096L;

            using (var harness = new AuditHarness())
            {
                AuditAssetProvider provider = harness.CreateProvider();
                UIService service = harness.CreateService(
                    provider, AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, null), null);
                UIWindow window = service.OpenAsync(harness.CreateDirect("AllocPresenterHit", 0, false))
                    .GetAwaiter()
                    .GetResult();

                var binder = new UIPresenterBinder(4);
                Func<IUIPresenter> factory = CreateAuditPresenter;
                Action<IUIPresenter> release = ReleaseAuditPresenter;
                binder.Register("AllocPresenterHit", factory, release);
                var context = new UIWindowBindingContext(window, service);

                for (int i = 0; i < WarmupIterations; i++)
                {
                    IUIWindowBinding warm = binder.Bind(context);
                    if (warm != null)
                    {
                        warm.Dispose();
                    }
                }

                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < MeasureIterations; i++)
                {
                    IUIWindowBinding binding = binder.Bind(context);
                    if (binding != null)
                    {
                        binding.Dispose();
                    }
                }

                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                long perBind = BytesPerIteration(allocated, MeasureIterations);
                Report("UIPresenterBinder.Bind(hit)", allocated, MeasureIterations);
                TestContext.Progress.WriteLine(
                    "ALLOC:UIPresenterBinder.Bind(hit) perBind=" + perBind +
                    " B composition=presenter instance + PresenterBinding wrapper (allowed by design)");

                Assert.That(
                    allocated,
                    Is.GreaterThan(0L),
                    "a successful Bind creates one presenter plus one binding wrapper and therefore " +
                    "must allocate; 0 B would mean the presenter was not created");
                Assert.That(
                    perBind,
                    Is.LessThan(ceilingPerBind),
                    "a successful Bind must stay below the regression ceiling of " + ceilingPerBind +
                    " B; observed " + perBind + " B per bind");

                service.CloseAsync("AllocPresenterHit").GetAwaiter().GetResult();
            }
        }

        // ------------------------------------------------------------------ helpers

        private static long MeasureTransactions(
            UIService service,
            UIWindowConfiguration configuration,
            int transactions)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < transactions; i++)
            {
                service.OpenAsync(configuration).GetAwaiter().GetResult();
                service.CloseAsync(configuration.WindowId).GetAwaiter().GetResult();
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        private static IUIPresenter CreateAuditPresenter()
        {
            return new AuditPresenter();
        }

        private static void ReleaseAuditPresenter(IUIPresenter presenter)
        {
            presenter.Dispose();
        }

        private static long BytesPerIteration(long allocated, int iterations)
        {
            return iterations == 0 ? 0L : allocated / iterations;
        }

        private static void Report(string probe, long allocated, int iterations)
        {
            TestContext.Progress.WriteLine(
                "ALLOC:" + probe + " bytes=" + allocated + " calls=" + iterations +
                " perCall=" + BytesPerIteration(allocated, iterations));
        }

        private static void Consume(int value)
        {
            // Keeps the measured expression from being optimised away.
            if (value == int.MinValue)
            {
                TestContext.Progress.WriteLine("ALLOC:sink");
            }
        }
    }
}
