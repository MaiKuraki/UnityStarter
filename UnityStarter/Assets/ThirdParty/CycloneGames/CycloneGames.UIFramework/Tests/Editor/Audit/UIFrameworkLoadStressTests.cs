using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using CycloneGames.UIFramework.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.UIFramework.Tests.Editor.Audit
{
    /// <summary>
    /// Load-shape stress: capacity-boundary churn, burst in-flight opens, large navigation graphs
    /// and rapid visibility toggling. Timing assertions are regression guards, not thresholds.
    /// </summary>
    [Category("UIFrameworkAudit")]
    public sealed class UIFrameworkLoadStressTests
    {
        private const int TimeoutMilliseconds = 300000;

        private static void Emit(string line)
        {
            UnityEngine.Debug.Log(line);
            TestContext.Progress.WriteLine(line);
        }

        // ------------------------------------------------------------------ UIService load shapes

        [Test]
        [Timeout(TimeoutMilliseconds)]
        public async Task Stress_CapacityBoundaryThrash_SingleSlot()
        {
            const int transactions = 2000;

            using (var harness = new AuditHarness())
            {
                AuditAssetProvider provider = harness.CreateProvider();
                UIService service = harness.CreateService(
                    provider,
                    AuditHarness.CreateOptions(AuditDims.CapacityBoundarySingleSlot, null),
                    null);

                UIWindowConfiguration configuration = harness.CreateDirect("Thrash", 0, false);
                await service.OpenAsync(configuration);

                for (int warm = 0; warm < 20; warm++)
                {
                    UIWindow warmWindow = await service.OpenAsync(configuration);
                    await service.CloseAsync(warmWindow.WindowId);
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
                long before = GC.GetTotalMemory(false);

                var stopwatch = Stopwatch.StartNew();
                for (int i = 0; i < transactions; i++)
                {
                    UIWindow opened = await service.OpenAsync(configuration);
                    bool closed = await service.CloseAsync(opened.WindowId);
                    if (!closed)
                    {
                        Assert.Fail("capacity thrash transaction " + i + " failed to close the window");
                    }
                }

                stopwatch.Stop();
                double perTransactionMs = stopwatch.Elapsed.TotalMilliseconds / transactions;

                GC.Collect();
                GC.WaitForPendingFinalizers();
                long growth = GC.GetTotalMemory(false) - before;

                Emit(
                    "STRESS,name=CapacityBoundaryThrash,transactions=" + transactions +
                    ",totalMs=" + stopwatch.Elapsed.TotalMilliseconds.ToString("F1") +
                    ",perTransactionMs=" + perTransactionMs.ToString("F4") +
                    ",managedGrowthBytes=" + growth);

                Assert.That(
                    growth,
                    Is.LessThan(512L * 1024L),
                    "2000 capacity-boundary transactions retained " + growth +
                    " B; steady churn must not accumulate managed state.");

                AuditInvariants.AssertSettled(
                    service, harness.Layer, null, provider, 0, "capacity thrash final state");
            }
        }

        [Test]
        [Timeout(TimeoutMilliseconds)]
        public async Task Stress_InterleavedOpenClose_MixedIds()
        {
            const int transactions = 5000;
            const int idCount = 8;

            using (var harness = new AuditHarness())
            {
                AuditAssetProvider provider = harness.CreateProvider();
                UINavigationService navigation = harness.CreateNavigation(idCount * 2);
                UIService service = harness.CreateService(
                    provider, AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, navigation), null);

                var configurations = new UIWindowConfiguration[idCount];
                for (int i = 0; i < idCount; i++)
                {
                    configurations[i] = harness.CreateDirect("Mix" + i, i, false);
                }

                for (int warm = 0; warm < 16; warm++)
                {
                    int w = warm % idCount;
                    UIWindow warmWindow = await service.OpenAsync(configurations[w]);
                    await service.CloseAsync(warmWindow.WindowId);
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
                long before = GC.GetTotalMemory(false);

                var stopwatch = Stopwatch.StartNew();
                for (int i = 0; i < transactions; i++)
                {
                    UIWindow opened = await service.OpenAsync(configurations[i % idCount]);
                    await service.CloseAsync(opened.WindowId);
                }

                stopwatch.Stop();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                long growth = GC.GetTotalMemory(false) - before;

                double perTransactionMicros = stopwatch.Elapsed.TotalMilliseconds * 1000.0 / transactions;
                Emit(
                    "STRESS,name=InterleavedOpenClose,transactions=" + transactions +
                    ",totalMs=" + stopwatch.Elapsed.TotalMilliseconds.ToString("F1") +
                    ",perTransactionMicros=" + perTransactionMicros.ToString("F1") +
                    ",managedGrowthBytes=" + growth);

                Assert.That(
                    growth,
                    Is.LessThan(512L * 1024L),
                    "5000 mixed open/close transactions retained " + growth + " B.");

                AuditInvariants.AssertSettled(
                    service, harness.Layer, navigation, provider, 0, "interleaved stress final state");
            }
        }

        [Test]
        [Timeout(TimeoutMilliseconds)]
        public async Task Stress_BurstInFlightOpens_ResolveTogether()
        {
            const int burstSize = 8;

            using (var harness = new AuditHarness())
            {
                AuditAssetProvider provider = harness.CreateProvider();
                UIService service = harness.CreateService(
                    provider,
                    CreateBurstOptions(burstSize),
                    null);

                var configurations = new UIWindowConfiguration[burstSize];
                for (int i = 0; i < burstSize; i++)
                {
                    configurations[i] = harness.CreateDirect("Burst" + i, i, false);
                }

                // Issue every open before awaiting any completion: all requests must register
                // as distinct sessions, then resolve together without losing one another.
                var tasks = new List<UniTask<UIWindow>>(burstSize);
                for (int i = 0; i < burstSize; i++)
                {
                    tasks.Add(service.OpenAsync(configurations[i]));
                }

                int opened = 0;
                for (int i = 0; i < burstSize; i++)
                {
                    UIWindow window = await tasks[i];
                    if (window != null)
                    {
                        opened++;
                    }
                }

                Assert.That(
                    opened,
                    Is.EqualTo(burstSize),
                    "every burst open must resolve to a live window");

                Assert.That(
                    service.ActiveWindowCount,
                    Is.EqualTo(burstSize),
                    "the burst must be visible as " + burstSize + " concurrent sessions");

                var windows = new List<UIWindow>(burstSize);
                service.CopyActiveWindows(windows);
                Assert.That(windows.Count, Is.EqualTo(burstSize));

                for (int i = 0; i < burstSize; i++)
                {
                    await service.CloseAsync(windows[i].WindowId);
                }

                Emit(
                    "STRESS,name=BurstInFlightOpens,burstSize=" + burstSize +
                    ",resolved=" + opened);

                AuditInvariants.AssertSettled(
                    service, harness.Layer, null, provider, 0, "burst final state");
            }
        }

        [Test]
        [Timeout(TimeoutMilliseconds)]
        public async Task Stress_VisibilityToggleTenThousand()
        {
            using (var harness = new AuditHarness())
            {
                AuditAssetProvider provider = harness.CreateProvider();
                UIService service = harness.CreateService(
                    provider, AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, null), null);

                UIWindowConfiguration culledConfiguration = harness.CreateDirect("ToggleStress", 0, false);
                AuditHarness.ApplyCullWhenHidden(culledConfiguration, AuditDims.CullEnabled);
                UIWindow window = await service.OpenAsync(culledConfiguration);

                const int toggles = 10000;
                var stopwatch = Stopwatch.StartNew();
                for (int i = 0; i < toggles; i++)
                {
                    window.SetVisible(false);
                    window.SetVisible(true);
                }

                stopwatch.Stop();
                double perToggleMicros = stopwatch.Elapsed.TotalMilliseconds * 1000.0 / toggles;

                Emit(
                    "STRESS,name=VisibilityToggle,toggles=" + toggles +
                    ",totalMs=" + stopwatch.Elapsed.TotalMilliseconds.ToString("F1") +
                    ",perToggleMicros=" + perToggleMicros.ToString("F1") +
                    ",activeSelf=" + window.gameObject.activeSelf);

                Assert.That(
                    window.gameObject.activeSelf,
                    Is.True,
                    "the window must end visible after an even number of toggles");

                await service.CloseAsync(window.WindowId);
                AuditInvariants.AssertSettled(
                    service, harness.Layer, null, provider, 0, "visibility stress final state");
            }
        }

        // ------------------------------------------------------------------ navigation load shapes

        [Test]
        [Timeout(TimeoutMilliseconds)]
        public void Stress_Navigation_DeepChain512_CascadeUnregister()
        {
            const int depth = 512;
            UINavigationService navigation = new UINavigationService(depth * 2);

            for (int i = 0; i < depth; i++)
            {
                string id = "Deep" + i.ToString("D4");
                string opener = i == 0 ? null : "Deep" + (i - 1).ToString("D4");
                Assert.That(
                    navigation.Register(id, opener),
                    Is.True,
                    "registering chain node " + i + " must succeed");
            }

            var history = new List<UINavigationEntry>(depth * 2);
            Assert.That(navigation.CopyHistory(history), Is.EqualTo(depth));

            var affected = new List<string>(depth * 2);
            var stopwatch = Stopwatch.StartNew();
            Assert.That(
                navigation.Unregister("Deep0000", ChildClosePolicy.Cascade, affected),
                Is.True);
            stopwatch.Stop();

            Assert.That(affected.Count, Is.EqualTo(depth), "cascade must remove the whole chain");
            Assert.That(navigation.CopyHistory(history), Is.EqualTo(0));

            Emit(
                "STRESS,name=NavigationDeepCascade,depth=" + depth +
                ",cascadeMs=" + stopwatch.Elapsed.TotalMilliseconds.ToString("F3"));

            Assert.That(
                stopwatch.Elapsed.TotalMilliseconds,
                Is.LessThan(1000.0),
                "a 512-deep cascade took " + stopwatch.Elapsed.TotalMilliseconds.ToString("F1") +
                " ms; growth beyond this indicates a traversal regression.");
        }

        [Test]
        [Timeout(TimeoutMilliseconds)]
        public void Stress_Navigation_WideGraph4096_SequentialUnregister()
        {
            const int nodeCount = 4096;
            UINavigationService navigation = new UINavigationService(nodeCount);

            for (int i = 0; i < nodeCount; i++)
            {
                Assert.That(
                    navigation.Register("Wide" + i.ToString("D5")),
                    Is.True,
                    "registering wide node " + i + " must succeed");
            }

            var affected = new List<string>(8);
            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < nodeCount; i++)
            {
                Assert.That(
                    navigation.Unregister("Wide" + i.ToString("D5"), ChildClosePolicy.Detach, affected),
                    Is.True,
                    "unregister " + i + " must succeed");
            }

            stopwatch.Stop();
            double perUnregisterMicros = stopwatch.Elapsed.TotalMilliseconds * 1000.0 / nodeCount;

            var history = new List<UINavigationEntry>(8);
            Assert.That(navigation.CopyHistory(history), Is.EqualTo(0));

            Emit(
                "STRESS,name=NavigationWideUnregister,nodes=" + nodeCount +
                ",totalMs=" + stopwatch.Elapsed.TotalMilliseconds.ToString("F1") +
                ",perUnregisterMicros=" + perUnregisterMicros.ToString("F1") +
                ",note=each unregister compacts the registration order, so total work is O(n^2)");

            Assert.That(
                stopwatch.Elapsed.TotalMilliseconds,
                Is.LessThan(5000.0),
                "4096 sequential unregisters took " + stopwatch.Elapsed.TotalMilliseconds.ToString("F1") +
                " ms. This is the CompactRegistrationOrder O(n^2) path; a regression here becomes " +
                "a visible stall when a large project closes many windows one by one.");
        }

        // ------------------------------------------------------------------ helpers

        private static UIServiceOptions CreateBurstOptions(int capacity)
        {
            return new UIServiceOptions
            {
                InitialWindowCapacity = capacity,
                MaxActiveWindows = capacity,
                MaxInstantiatesPerFrame = capacity,
            };
        }
    }
}
