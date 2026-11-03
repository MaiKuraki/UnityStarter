using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using CycloneGames.UIFramework.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace CycloneGames.UIFramework.Tests.Editor.Audit
{
    /// <summary>
    /// Unity-specific behaviour the framework relies on: fake-null semantics, transform depth,
    /// active-toggle cost and UGUI canvas/batch invariants. Timing bounds only guard against
    /// super-linear growth; exact numbers are emitted as BENCH lines.
    /// </summary>
    [Category("UIFrameworkAudit")]
    public sealed class UIFrameworkUnitySemanticsTests
    {
        private const int TimeoutMilliseconds = 120000;

        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>(32);

        [TearDown]
        public void TearDown()
        {
            for (int i = _owned.Count - 1; i >= 0; i--)
            {
                UnityEngine.Object owned = _owned[i];
                if (owned != null)
                {
                    UnityEngine.Object.DestroyImmediate(owned);
                }
            }

            _owned.Clear();
        }

        // ------------------------------------------------------------------ A. fake null

        [Test]
        [Timeout(TimeoutMilliseconds)]
        public async Task FakeNull_ExternalDestroy_IsObservedAsNullEverywhere()
        {
            using (var harness = new AuditHarness())
            {
                UIWindowConfiguration configuration = harness.CreateDirect("FakeNullTarget", 0, false);
                AuditAssetProvider provider = harness.CreateProvider();
                UIService service = harness.CreateService(
                    provider, AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, null), null);

                UIWindow window = await service.OpenAsync(configuration);
                Assert.That(window, Is.Not.Null);

                UnityEngine.Object.DestroyImmediate(window.gameObject);

                // Contract under test: Unity does not reliably deliver OnDestroy for
                // DestroyImmediate in Editor batch runs, so the service reconciles on its next
                // public touch. This call is that touch.
                service.GetPerformanceStats();

                Assert.That(window == null, Is.True, "a destroyed UIWindow must compare equal to null");
                Assert.That(
                    ReferenceEquals(window, null),
                    Is.False,
                    "the managed wrapper still exists; only Unity's overloaded == reports null");

                UIWindow resolved;
                Assert.That(
                    harness.Layer.TryGetWindow("FakeNullTarget", out resolved),
                    Is.False,
                    "UILayer.TryGetWindow must filter destroyed entries");
                Assert.That(
                    harness.Layer.WindowCount,
                    Is.EqualTo(0),
                    "the destroyed window must be detached from the layer");

                Assert.That(service.ActiveWindowCount, Is.EqualTo(0), "active count must drop to zero");
                Assert.That(
                    service.GetPerformanceStats().SessionCount,
                    Is.EqualTo(0),
                    "the session must be removed after an external destroy");

                var windows = new List<UIWindow>(4);
                service.CopyActiveWindows(windows);
                Assert.That(windows.Count, Is.EqualTo(0), "CopyActiveWindows must not report destroyed windows");

                Assert.That(
                    provider.ConfigurationDisposeCount + provider.PrefabDisposeCount,
                    Is.EqualTo(0),
                    "a direct-configuration window does not acquire provider leases");
            }
        }

        [Test]
        [Timeout(TimeoutMilliseconds)]
        public async Task FakeNull_CopyWindows_DoesNotLeakDestroyedEntries()
        {
            using (var harness = new AuditHarness())
            {
                UIWindowConfiguration configuration = harness.CreateDirect("FakeNullCopy", 0, false);
                UIService service = harness.CreateService(
                    harness.CreateProvider(),
                    AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, null),
                    null);

                UIWindow window = await service.OpenAsync(configuration);
                var buffer = new List<UIWindow>(8);
                harness.Layer.CopyWindows(buffer);
                Assert.That(buffer.Count, Is.EqualTo(1), "one live window must be copied");

                UnityEngine.Object.DestroyImmediate(window.gameObject);
                service.ReconcileDestroyedWindows();

                harness.Layer.CopyWindows(buffer);
                for (int i = 0; i < buffer.Count; i++)
                {
                    Assert.That(
                        buffer[i] != null,
                        Is.True,
                        "UILayer.CopyWindows leaked a fake-null entry at index " + i +
                        "; callers that dereference it will throw MissingReferenceException");
                }

                Assert.That(
                    buffer.Count,
                    Is.EqualTo(0),
                    "CopyWindows must not report a window that was destroyed and detached");
            }
        }

        [Test]
        [Timeout(TimeoutMilliseconds)]
        public async Task FakeNull_DestroyedRoot_FailsWithInvalidOperationNotNullReference()
        {
            using (var harness = new AuditHarness())
            {
                UIWindowConfiguration configuration = harness.CreateDirect("DestroyedRoot", 0, false);
                UIService service = harness.CreateService(
                    harness.CreateProvider(),
                    AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, null),
                    null);

                UnityEngine.Object.DestroyImmediate(harness.Root.gameObject);

                Exception observed = null;
                try
                {
                    await service.OpenAsync(configuration);
                }
                catch (Exception exception)
                {
                    observed = exception;
                }

                Assert.That(observed, Is.Not.Null, "opening after the UIRoot was destroyed must fail");
                Assert.That(
                    observed,
                    Is.InstanceOf<InvalidOperationException>(),
                    "expected an explicit InvalidOperationException, observed " +
                    observed.GetType().Name + ": " + observed.Message);
                Assert.That(
                    observed is NullReferenceException || observed is MissingReferenceException,
                    Is.False,
                    "a destroyed UIRoot must not degrade into a raw null dereference");
            }
        }

        [Test]
        [Timeout(TimeoutMilliseconds)]
        public async Task FakeNull_DestroyedPrefab_FailsCleanlyWithoutLeakingLeases()
        {
            using (var harness = new AuditHarness())
            {
                UIWindowConfiguration configuration =
                    harness.CreateProviderConfiguration("DestroyedPrefab", AuditDims.SourceAssetReference, 0);
                AuditAssetProvider provider = harness.CreateProvider();
                GameObject prefab = harness.CreatePrefab("DestroyedPrefab");
                provider.Map("DestroyedPrefab", configuration, prefab);

                UIService service = harness.CreateService(
                    provider, AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, null), null);

                UnityEngine.Object.DestroyImmediate(prefab);

                Exception observed = null;
                try
                {
                    await service.OpenAsync("DestroyedPrefab");
                }
                catch (Exception exception)
                {
                    observed = exception;
                }

                Assert.That(observed, Is.Not.Null, "opening a destroyed prefab must fail");
                Assert.That(
                    observed,
                    Is.InstanceOf<InvalidOperationException>(),
                    "expected InvalidOperationException, observed " + observed.GetType().Name);
                Assert.That(
                    harness.Layer.WindowCount,
                    Is.EqualTo(0),
                    "no window may be attached after the prefab failed validation");
                Assert.That(
                    service.GetPerformanceStats().SessionCount,
                    Is.EqualTo(0),
                    "the failed open must roll the session back");
            }
        }

        [Test]
        [Timeout(TimeoutMilliseconds)]
        public async Task FakeNull_DestroyedConfiguration_DoesNotThrowFromViewAccessors()
        {
            using (var harness = new AuditHarness())
            {
                UIWindowConfiguration configuration = harness.CreateDirect("DestroyedConfig", 5, false);
                UIService service = harness.CreateService(
                    harness.CreateProvider(),
                    AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, null),
                    null);

                UIWindow window = await service.OpenAsync(configuration);
                UnityEngine.Object.DestroyImmediate(configuration);

                Assert.That(window.Priority, Is.EqualTo(0), "Priority must degrade to 0 for a destroyed configuration");
                Assert.That(window.Configuration == null, Is.True, "the destroyed configuration must read as null");

                window.SetVisible(false);
                window.SetVisible(true);
            }
        }

        [Test]
        [Timeout(TimeoutMilliseconds)]
        public async Task FakeNull_LifetimeRelay_NotifiesObserverOnExternalDestroy()
        {
            using (var harness = new AuditHarness())
            {
                UIWindowConfiguration configuration = harness.CreateDirect("RelayNotify", 0, false);
                UIService service = harness.CreateService(
                    harness.CreateProvider(),
                    AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, null),
                    null);

                UIWindow window = await service.OpenAsync(configuration);
                Assert.That(
                    CountComponentsNamed(window.gameObject, "UIWindowLifetimeRelay"),
                    Is.EqualTo(1),
                    "the relay component is added at initialization");

                UnityEngine.Object.DestroyImmediate(window.gameObject);
                service.ReconcileDestroyedWindows();

                Assert.That(
                    service.GetPerformanceStats().SessionCount,
                    Is.EqualTo(0),
                    "relay did not notify: a destroyed window leaked its session");
            }
        }

        // ------------------------------------------------------------------ B. depth / active

        [Test]
        [Timeout(TimeoutMilliseconds)]
        public async Task Transform_DeepHierarchy_OpensInLinearTime()
        {
            int[] depths = { 1, 8, 32, 128 };
            double[] perOpenMilliseconds = new double[depths.Length];
            const int iterations = 40;

            for (int d = 0; d < depths.Length; d++)
            {
                using (var harness = new AuditHarness())
                {
                    UIService service = harness.CreateService(
                        harness.CreateProvider(),
                        AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, null),
                        null);

                    UIWindowConfiguration configuration =
                        CreateDeepConfiguration(harness, "Deep" + depths[d], depths[d]);

                    for (int warm = 0; warm < 5; warm++)
                    {
                        UIWindow warmWindow = await service.OpenAsync(configuration);
                        await service.CloseAsync(warmWindow.WindowId);
                    }

                    var stopwatch = Stopwatch.StartNew();
                    for (int i = 0; i < iterations; i++)
                    {
                        UIWindow opened = await service.OpenAsync(configuration);
                        await service.CloseAsync(opened.WindowId);
                    }

                    stopwatch.Stop();
                    perOpenMilliseconds[d] = stopwatch.Elapsed.TotalMilliseconds / iterations;
                }
            }

            for (int i = 0; i < depths.Length; i++)
            {
                TestContext.Progress.WriteLine(
                    "BENCH:depth=" + depths[i] + ",openCloseMs=" +
                    perOpenMilliseconds[i].ToString("F4"));
            }

            Assert.That(
                perOpenMilliseconds[3],
                Is.LessThan(perOpenMilliseconds[0] * 40.0 + 5.0),
                "128-deep open/close shows super-linear growth relative to depth 1; depth1=" +
                perOpenMilliseconds[0].ToString("F4") + "ms depth128=" +
                perOpenMilliseconds[3].ToString("F4") + "ms. Linear Instantiate/Destroy cost of the " +
                "larger hierarchy is expected; only a super-linear factor is a framework defect.");
        }

        [Test]
        [Timeout(TimeoutMilliseconds)]
        public async Task Transform_ActiveToggle_IsZeroManagedAllocation()
        {
            using (var harness = new AuditHarness())
            {
                UIService service = harness.CreateService(
                    harness.CreateProvider(),
                    AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, null),
                    null);

                UIWindowConfiguration configuration = CreateDeepConfiguration(harness, "ToggleCost", 64);
                UIWindow window = await service.OpenAsync(configuration);

                const int iterations = 200;
                for (int i = 0; i < 10; i++)
                {
                    window.gameObject.SetActive(false);
                    window.gameObject.SetActive(true);
                }

                long before = GC.GetAllocatedBytesForCurrentThread();
                var stopwatch = Stopwatch.StartNew();
                for (int i = 0; i < iterations; i++)
                {
                    window.gameObject.SetActive(false);
                    window.gameObject.SetActive(true);
                }

                stopwatch.Stop();
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

                // Meter sanity: if this runtime cannot see a single managed allocation, a zero
                // reading is meaningless, so the probe reports it instead of claiming zero GC.
                object meterControl = new object();
                GC.KeepAlive(meterControl);
                long meterDelta = GC.GetAllocatedBytesForCurrentThread() - before - allocated;
                bool meterWorks = meterDelta > 0L;

                TestContext.Progress.WriteLine(
                    "BENCH:activeToggle64,perToggleMs=" +
                    (stopwatch.Elapsed.TotalMilliseconds / iterations).ToString("F4") +
                    ",allocatedBytes=" + allocated +
                    ",allocationMeterWorks=" + meterWorks);

                if (!meterWorks)
                {
                    Assert.Ignore(
                        "GC.GetAllocatedBytesForCurrentThread() cannot see a control allocation in " +
                        "this runtime; the SetActive probe reported " + allocated +
                        " B but that reading is not conclusive. Use the Unity Profiler.");
                }

                Assert.That(
                    allocated,
                    Is.EqualTo(0L),
                    "SetActive on a framework-owned window must not allocate managed memory; observed " +
                    allocated + " bytes over " + iterations + " toggles");
            }
        }

        [Test]
        [Timeout(TimeoutMilliseconds)]
        public async Task Visibility_CullWhenHidden_TradesActiveStateForBatching()
        {
            using (var harness = new AuditHarness())
            {
                UIService service = harness.CreateService(
                    harness.CreateProvider(),
                    AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, null),
                    null);

                UIWindowConfiguration alphaConfiguration = harness.CreateDirect("CullOff", 0, false);
                AuditHarness.ApplyCullWhenHidden(alphaConfiguration, AuditDims.CullDisabled);
                UIWindow alphaWindow = await service.OpenAsync(alphaConfiguration);

                alphaWindow.SetVisible(false);
                Assert.That(
                    alphaWindow.gameObject.activeSelf,
                    Is.True,
                    "CullWhenHidden=false must keep the GameObject active so the canvas keeps batching");
                Assert.That(alphaWindow.CanvasGroup.alpha, Is.EqualTo(0f));
                Assert.That(alphaWindow.CanvasGroup.blocksRaycasts, Is.False);

                UIWindowConfiguration cullConfiguration = harness.CreateDirect("CullOn", 0, false);
                AuditHarness.ApplyCullWhenHidden(cullConfiguration, AuditDims.CullEnabled);
                UIWindow cullWindow = await service.OpenAsync(cullConfiguration);

                cullWindow.SetVisible(false);
                Assert.That(
                    cullWindow.gameObject.activeSelf,
                    Is.False,
                    "CullWhenHidden=true must deactivate so the subtree stops rendering");
                cullWindow.SetVisible(true);
                Assert.That(cullWindow.gameObject.activeSelf, Is.True);
                Assert.That(cullWindow.CanvasGroup.alpha, Is.EqualTo(1f));
            }
        }

        [Test]
        [Timeout(TimeoutMilliseconds)]
        public async Task Components_OneRelayPerWindow_AndNoResidueAfterTwoHundredCycles()
        {
            using (var harness = new AuditHarness())
            {
                UIService service = harness.CreateService(
                    harness.CreateProvider(),
                    CreateLargeOptions(96),
                    null);

                UIWindowConfiguration configuration = harness.CreateDirect("RelayCount", 0, false);
                UIWindow window = await service.OpenAsync(configuration);

                Component[] components = window.GetComponents<Component>();
                int relayCount = CountComponentsNamed(window.gameObject, "UIWindowLifetimeRelay");

                TestContext.Progress.WriteLine(
                    "BENCH:componentsPerWindow=" + components.Length + ",relayCount=" + relayCount);

                Assert.That(relayCount, Is.EqualTo(1), "exactly one lifetime relay is allowed per window");

                await service.CloseAsync(configuration.WindowId);

                for (int i = 0; i < 200; i++)
                {
                    UIWindow cycle = await service.OpenAsync(configuration);
                    await service.CloseAsync(cycle.WindowId);
                }

                Assert.That(
                    harness.Layer.transform.childCount,
                    Is.EqualTo(0),
                    "the layer must contain no residual window objects after 200 open/close cycles");
                Assert.That(
                    service.GetPerformanceStats().SessionCount,
                    Is.EqualTo(0),
                    "no session may survive 200 open/close cycles");
            }
        }

        // ------------------------------------------------------------------ C. UGUI structure

        [Test]
        [Timeout(TimeoutMilliseconds)]
        public async Task Ugui_InheritLayerCanvas_KeepsOneBatchRoot()
        {
            using (var harness = new AuditHarness())
            {
                UIService service = harness.CreateService(
                    harness.CreateProvider(),
                    CreateLargeOptions(96),
                    null);

                Canvas layerCanvas = harness.Layer.UICanvas;
                const int windowCount = 40;

                for (int i = 0; i < windowCount; i++)
                {
                    UIWindowConfiguration configuration =
                        harness.CreateDirect("Inherit" + i, i, false);
                    AuditHarness.ApplyCanvasPolicy(configuration, AuditDims.CanvasInherit);
                    UIWindow window = await service.OpenAsync(configuration);

                    Assert.That(
                        window.GetComponent<Canvas>() == null,
                        Is.True,
                        "inherit policy must not add a nested Canvas to window " + i);
                    Assert.That(
                        window.GetComponent<GraphicRaycaster>() == null,
                        Is.True,
                        "inherit policy must not add a GraphicRaycaster to window " + i);
                    Assert.That(
                        ReferenceEquals(window.GetComponentInParent<Canvas>(), layerCanvas),
                        Is.True,
                        "every inherited window must resolve to the single layer canvas (one batch root)");
                }

                int canvasCount = harness.Root.GetComponentsInChildren<Canvas>(true).Length;
                TestContext.Progress.WriteLine(
                    "BENCH:inheritCanvasCount=" + canvasCount + ",windows=" + windowCount);

                Assert.That(
                    canvasCount,
                    Is.EqualTo(2),
                    "only the root canvas and the layer canvas may exist; observed " + canvasCount);
            }
        }

        [Test]
        [Timeout(TimeoutMilliseconds)]
        public async Task Ugui_IsolatedCanvas_AddsExactlyOneCanvasAndRaycaster()
        {
            using (var harness = new AuditHarness())
            {
                UIService service = harness.CreateService(
                    harness.CreateProvider(),
                    CreateLargeOptions(96),
                    null);

                Canvas layerCanvas = harness.Layer.UICanvas;
                const int windowCount = 20;

                for (int i = 0; i < windowCount; i++)
                {
                    UIWindowConfiguration configuration =
                        harness.CreateDirect("Isolated" + i, i, false);
                    AuditHarness.ApplyCanvasPolicy(configuration, AuditDims.CanvasIsolated);
                    UIWindow window = await service.OpenAsync(configuration);

                    Canvas canvas = window.GetComponent<Canvas>();
                    Assert.That(canvas != null, Is.True, "isolated policy must add a Canvas");
                    Assert.That(
                        window.GetComponents<Canvas>().Length,
                        Is.EqualTo(1),
                        "exactly one Canvas per isolated window");
                    Assert.That(
                        window.GetComponents<GraphicRaycaster>().Length,
                        Is.EqualTo(1),
                        "exactly one GraphicRaycaster per isolated window");
                    Assert.That(
                        canvas.overrideSorting,
                        Is.False,
                        "the service must not silently enable overrideSorting");
                    Assert.That(
                        canvas.additionalShaderChannels,
                        Is.EqualTo(layerCanvas.additionalShaderChannels),
                        "isolated canvases must inherit the layer shader channel mask");
                }

                int totalCanvases = harness.Root.GetComponentsInChildren<Canvas>(true).Length;
                TestContext.Progress.WriteLine(
                    "BENCH:isolatedCanvasCount=" + totalCanvases + ",windows=" + windowCount);
                Assert.That(
                    totalCanvases,
                    Is.EqualTo(windowCount + 2),
                    "each isolated window owns exactly one canvas; expected " + (windowCount + 2));
            }
        }

        [Test]
        [Timeout(TimeoutMilliseconds)]
        public async Task Ugui_PriorityOrder_MatchesSiblingOrderForTieAndOutOfOrderInserts()
        {
            using (var harness = new AuditHarness())
            {
                UIService service = harness.CreateService(
                    harness.CreateProvider(),
                    CreateLargeOptions(128),
                    null);

                int[] priorities = { 30, -5, 12, 0, 30, 7, -20, 99, 1, 12 };
                var opened = new List<UIWindow>(priorities.Length);

                for (int i = 0; i < priorities.Length; i++)
                {
                    UIWindowConfiguration configuration =
                        harness.CreateDirect("Order" + i, priorities[i], false);
                    opened.Add(await service.OpenAsync(configuration));
                }

                var sorted = new List<UIWindow>(opened.Count);
                harness.Layer.CopyWindows(sorted);

                for (int i = 0; i < sorted.Count; i++)
                {
                    Assert.That(
                        sorted[i].transform.GetSiblingIndex(),
                        Is.EqualTo(i),
                        "sibling index must equal the sorted position for index " + i);
                    if (i > 0)
                    {
                        Assert.That(
                            sorted[i - 1].Priority,
                            Is.LessThanOrEqualTo(sorted[i].Priority),
                            "priority order broke between index " + (i - 1) + " and " + i);
                    }
                }

                Assert.That(sorted.Count, Is.EqualTo(priorities.Length));
            }
        }

        [Test]
        [Timeout(TimeoutMilliseconds)]
        public void Ugui_LayoutRebuild_CostStaysLinearInChildCount()
        {
            double small = MeasureLayoutRebuild(50, 20);
            double large = MeasureLayoutRebuild(200, 20);

            TestContext.Progress.WriteLine(
                "BENCH:layoutRebuild50Ms=" + small.ToString("F4") +
                ",layoutRebuild200Ms=" + large.ToString("F4") +
                ",perChildRatio=" + ((large / 200.0) / (small / 50.0)).ToString("F3"));

            double perChildSmall = small / 50.0;
            double perChildLarge = large / 200.0;
            Assert.That(
                perChildLarge,
                Is.LessThan(perChildSmall * 2.0 + 0.05),
                "per-child layout rebuild cost grew by more than 2x from 50 to 200 children; " +
                "small=" + small.ToString("F4") + "ms large=" + large.ToString("F4") + "ms");
        }

        [Test]
        [Timeout(TimeoutMilliseconds)]
        public void Ugui_AlphaVersusSetActive_OnHeavyWindow()
        {
            GameObject root = Track(new GameObject("HeavyWindow", typeof(RectTransform), typeof(CanvasGroup)));
            AddChildren(root.transform, 200);
            CanvasGroup group = root.GetComponent<CanvasGroup>();

            const int iterations = 200;
            for (int i = 0; i < 10; i++)
            {
                group.alpha = 0f;
                group.alpha = 1f;
            }

            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                group.alpha = 0f;
                group.alpha = 1f;
            }

            stopwatch.Stop();
            double alphaMs = stopwatch.Elapsed.TotalMilliseconds;

            for (int i = 0; i < 10; i++)
            {
                root.SetActive(false);
                root.SetActive(true);
            }

            stopwatch.Restart();
            for (int i = 0; i < iterations; i++)
            {
                root.SetActive(false);
                root.SetActive(true);
            }

            stopwatch.Stop();
            double activeMs = stopwatch.Elapsed.TotalMilliseconds;

            TestContext.Progress.WriteLine(
                "BENCH:heavyWindow200,alphaMs=" + alphaMs.ToString("F3") +
                ",setActiveMs=" + activeMs.ToString("F3") +
                ",ratio=" + (activeMs / Math.Max(alphaMs, 0.0001)).ToString("F2"));

            Assert.That(
                activeMs,
                Is.GreaterThan(0.0),
                "SetActive must be measurable; a zero reading means the probe did not run");
        }

        [Test]
        [Explicit("Draw call and batch counts require a rendering device; run in PlayMode with -nographics disabled.")]
        public void Ugui_DrawCallAndBatch_RequireRenderingDevice()
        {
            Assert.Ignore(
                "Batches/SetPassCalls cannot be measured under -nographics. " +
                "Measure them in a PlayMode build with the Frame Debugger or ProfilerRecorder(\"Batches\").");
        }

        // ------------------------------------------------------------------ helpers

        private static UIServiceOptions CreateLargeOptions(int capacity)
        {
            return new UIServiceOptions
            {
                InitialWindowCapacity = capacity,
                MaxActiveWindows = capacity,
                MaxInstantiatesPerFrame = capacity,
            };
        }

        private static int CountComponentsNamed(GameObject target, string typeName)
        {
            Component[] components = target.GetComponents<Component>();
            int count = 0;
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null)
                {
                    continue;
                }

                if (string.Equals(component.GetType().Name, typeName, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        private UIWindowConfiguration CreateDeepConfiguration(
            AuditHarness harness,
            string windowId,
            int depth)
        {
            GameObject prefab = Track(new GameObject(
                windowId + "_Prefab",
                typeof(RectTransform),
                typeof(CanvasGroup)));
            prefab.AddComponent<UIWindow>();

            Transform cursor = prefab.transform;
            for (int i = 0; i < depth; i++)
            {
                GameObject child = new GameObject("Depth" + i, typeof(RectTransform));
                child.AddComponent<SemanticsProbeBehaviour>();
                child.transform.SetParent(cursor, false);
                cursor = child.transform;
            }

            UIWindowConfiguration configuration = harness.CreateDirect(windowId, 0, false);
            TestReflection.SetField(configuration, "windowPrefab", prefab.GetComponent<UIWindow>());
            return configuration;
        }

        private double MeasureLayoutRebuild(int childCount, int iterations)
        {
            GameObject root = Track(new GameObject(
                "Layout" + childCount,
                typeof(RectTransform),
                typeof(VerticalLayoutGroup)));
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(400f, 2000f);

            AddImages(root.transform, childCount);

            for (int i = 0; i < 5; i++)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
            }

            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
            }

            stopwatch.Stop();
            return stopwatch.Elapsed.TotalMilliseconds / iterations;
        }

        private void AddChildren(Transform parent, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var child = new GameObject("Child" + i, typeof(RectTransform), typeof(CanvasRenderer));
                child.transform.SetParent(parent, false);
            }
        }

        private void AddImages(Transform parent, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var child = new GameObject("Image" + i, typeof(RectTransform), typeof(CanvasRenderer));
                child.AddComponent<Image>();
                child.transform.SetParent(parent, false);
            }
        }

        private GameObject Track(GameObject value)
        {
            _owned.Add(value);
            return value;
        }

        private sealed class SemanticsProbeBehaviour : MonoBehaviour
        {
        }
    }
}
