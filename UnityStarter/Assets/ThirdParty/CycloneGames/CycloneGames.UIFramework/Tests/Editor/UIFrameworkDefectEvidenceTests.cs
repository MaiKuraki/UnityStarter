using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using CycloneGames.UIFramework.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace CycloneGames.UIFramework.Tests.Editor
{
    /// <summary>
    /// Evidence tests for defects M-1 (UILayer sibling rewrites), M-4 (re-entrant self
    /// close inside a lifecycle callback) and L-4 (ChildClosePolicy range-guard safety).
    /// No production code is modified; RED tests are expected to fail on the current tree
    /// and BENCH tests only emit timing, never thresholds.
    /// </summary>
    public sealed class UIFrameworkDefectEvidenceTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>(64);
        private readonly List<UIWindowConfiguration> _configurations =
            new List<UIWindowConfiguration>(64);
        private readonly List<UIService> _services = new List<UIService>(2);

        private UIRuntimeTestFixture _fixture;

        [SetUp]
        public void SetUp()
        {
            _fixture = new UIRuntimeTestFixture();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _services.Count - 1; i >= 0; i--)
            {
                _services[i]?.Dispose();
            }

            _services.Clear();
            _fixture?.Dispose();

            for (int i = _objects.Count - 1; i >= 0; i--)
            {
                if (_objects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_objects[i]);
                }
            }

            for (int i = _configurations.Count - 1; i >= 0; i--)
            {
                if (_configurations[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_configurations[i]);
                }
            }

            _objects.Clear();
            _configurations.Clear();
        }

        // M-1: UILayer.InsertSorted rewrites every sibling index below the insertion point,
        // producing O(N) work per head insertion and O(N^2) across a re-attach sweep.
        // BENCH only: emits a timing line, asserts ordering, never a threshold.
        [Test]
        public void Bench_UILayer_ReattachToHeadCycles()
        {
            const int windowCount = 60;
            const int rounds = 30;

            UILayer layer = CreateLayer();
            for (int i = 0; i < windowCount; i++)
            {
                Attach(layer, CreateWindow("Window" + i, i));
            }

            var current = new List<UIWindow>(windowCount);
            var stopwatch = new System.Diagnostics.Stopwatch();
            stopwatch.Start();
            for (int round = 0; round < rounds; round++)
            {
                layer.CopyWindows(current);
                UIWindow mover = current[current.Count - 1];
                Assert.That(Detach(layer, mover), Is.True);

                // Drop the mover below every other window so the re-attach lands at the
                // head, forcing InsertSorted's sibling-rewrite loop over the whole layer.
                TestReflection.SetField(mover.Configuration, "priority", -1 - round);
                Attach(layer, mover);
            }

            stopwatch.Stop();
            UnityEngine.Debug.Log(
                $"BENCH:UILayer_Reattach_Cycles={stopwatch.Elapsed.TotalMilliseconds:F3}");

            layer.CopyWindows(current);
            Assert.That(current, Has.Count.EqualTo(windowCount));
            for (int i = 0; i < current.Count; i++)
            {
                Assert.That(
                    current[i].transform.GetSiblingIndex(),
                    Is.EqualTo(i),
                    "Sibling index must track the priority-sorted position after each re-attach.");
                if (i > 0)
                {
                    Assert.That(
                        current[i - 1].Priority,
                        Is.LessThanOrEqualTo(current[i].Priority),
                        "Windows must stay sorted by ascending priority.");
                }
            }
        }

        // M-4: requesting a close for a window from inside that window's own lifecycle
        // callback must fail fast. Today the request is silently deferred, so the guard
        // flag never flips -> RED.
        [Test]
        public async Task SelfClose_RequestedInsideOwnLifecycleCallback_FailsFast()
        {
            var binder = new SelfCloseObservingBinder();
            UIWindowConfiguration configuration =
                _fixture.CreateDirectConfiguration("SelfCloseProbe");
            UIService service = CreateService(binders: new IUIWindowBinder[] { binder });
            binder.Service = service;

            UIWindow window = await service.OpenAsync(configuration);
            Assert.That(window.WindowId, Is.EqualTo("SelfCloseProbe"));

            bool closed = await service.CloseAsync("SelfCloseProbe");
            Assert.That(closed, Is.True);

            Assert.That(
                binder.GuardFired,
                Is.True,
                "A self-close requested from within the window's own lifecycle callback must throw InvalidOperationException synchronously instead of being silently deferred.");
        }

        // L-4: the proposed range guard `(uint)policy <= (uint)ChildClosePolicy.Cascade`
        // avoids boxing but is only safe if it matches Enum.IsDefined. This test codifies
        // the actual enum domain and proves the equivalence holds for the sound upper bound.
        [Test]
        public void ChildClosePolicy_RangeGuardEquivalenceWithEnumIsDefined()
        {
            // Verified domain (contiguous, ascending): Reparent = 0, Cascade = 1, Detach = 2.
            Assert.That((int)ChildClosePolicy.Reparent, Is.EqualTo(0));
            Assert.That((int)ChildClosePolicy.Cascade, Is.EqualTo(1));
            Assert.That((int)ChildClosePolicy.Detach, Is.EqualTo(2));

            int definedMax = 0;
            foreach (ChildClosePolicy defined in
                     (ChildClosePolicy[])Enum.GetValues(typeof(ChildClosePolicy)))
            {
                Assert.That((int)defined, Is.GreaterThanOrEqualTo(0));
                definedMax = Mathf.Max(definedMax, (int)defined);
            }

            Assert.That(definedMax, Is.EqualTo((int)ChildClosePolicy.Detach));

            int[] samples = { int.MinValue, -1, 0, 1, 2, 3, 64, int.MaxValue };
            for (int i = 0; i < samples.Length; i++)
            {
                int value = samples[i];
                var policy = (ChildClosePolicy)value;
                bool viaEnumIsDefined = Enum.IsDefined(typeof(ChildClosePolicy), policy);
                bool viaRange = (uint)value <= (uint)definedMax;
                Assert.That(
                    viaRange,
                    Is.EqualTo(viaEnumIsDefined),
                    $"Range guard disagrees with Enum.IsDefined for policy value {value}.");
            }

            // Documents why the narrower Cascade bound is NOT a safe substitute: Detach (2)
            // is a valid member above Cascade (1) and would be wrongly rejected.
            Assert.That(Enum.IsDefined(typeof(ChildClosePolicy), ChildClosePolicy.Detach), Is.True);
            Assert.That(
                (uint)ChildClosePolicy.Detach <= (uint)ChildClosePolicy.Cascade,
                Is.False,
                "The proposed `<= Cascade` range bound would reject the valid Detach policy; use `<= Detach`.");
        }

        // M-4: a close requested from inside the window's own OnClosing callback must throw
        // synchronously instead of being deferred into a self-referential close.
        [Test]
        public async Task SelfClose_RequestedInsideOwnOnClosing_ThrowsSynchronously()
        {
            SelfClosingWindow.Service = null;
            SelfClosingWindow.GuardFired = false;
            UIWindowConfiguration configuration =
                _fixture.CreateDirectConfiguration<SelfClosingWindow>("OnClosingSelfClose");
            UIService service = CreateService();
            SelfClosingWindow.Service = service;

            UIWindow window = await service.OpenAsync(configuration);
            Assert.That(window.WindowId, Is.EqualTo("OnClosingSelfClose"));

            bool closed = await service.CloseAsync("OnClosingSelfClose");
            Assert.That(closed, Is.True);

            Assert.That(
                SelfClosingWindow.GuardFired,
                Is.True,
                "CloseAsync called from within the window's own OnClosing callback must throw InvalidOperationException synchronously.");
        }

        // M-4: an open requested from inside the window's own OnOpening callback must throw
        // synchronously instead of re-entering the open pipeline.
        [Test]
        public async Task SelfOpen_RequestedInsideOwnOnOpening_ThrowsSynchronously()
        {
            SelfOpeningWindow.Service = null;
            SelfOpeningWindow.GuardFired = false;
            UIWindowConfiguration configuration =
                _fixture.CreateDirectConfiguration<SelfOpeningWindow>("OnOpeningSelfOpen");
            UIService service = CreateService();
            SelfOpeningWindow.Service = service;

            UIWindow window = await service.OpenAsync(configuration);
            Assert.That(window.WindowId, Is.EqualTo("OnOpeningSelfOpen"));

            Assert.That(
                SelfOpeningWindow.GuardFired,
                Is.True,
                "OpenAsync called from within the window's own OnOpening callback must throw InvalidOperationException synchronously.");
        }

        private UILayer CreateLayer()
        {
            GameObject layerObject = Track(new GameObject(
                "EvidenceLayer",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(GraphicRaycaster)));
            UILayer layer = layerObject.AddComponent<UILayer>();
            TestReflection.SetField(layer, "layerName", "Evidence");
            return layer;
        }

        private UIWindow CreateWindow(string windowId, int priority)
        {
            GameObject windowObject = Track(new GameObject(windowId, typeof(RectTransform)));
            UIWindow window = windowObject.AddComponent<UIWindow>();
            UIWindowConfiguration configuration =
                ScriptableObject.CreateInstance<UIWindowConfiguration>();
            _configurations.Add(configuration);
            TestReflection.SetField(configuration, "priority", priority);
            TestReflection.SetField(window, "_windowId", windowId);
            TestReflection.SetField(window, "_configuration", configuration);
            return window;
        }

        private UIService CreateService(IReadOnlyList<IUIWindowBinder> binders = null)
        {
            var service = new UIService(_fixture.Root, null, null, binders);
            _services.Add(service);
            return service;
        }

        private GameObject Track(GameObject gameObject)
        {
            _objects.Add(gameObject);
            return gameObject;
        }

        private static void Attach(UILayer layer, UIWindow window)
        {
            TestReflection.Invoke(layer, "Attach", window);
        }

        private static bool Detach(UILayer layer, UIWindow window)
        {
            return (bool)TestReflection.Invoke(layer, "Detach", window);
        }

        private sealed class SelfClosingWindow : UIWindow
        {
            public static UIService Service;
            public static bool GuardFired;

            protected override void OnClosing()
            {
                try
                {
                    Service.CloseAsync(WindowId).Forget();
                }
                catch (InvalidOperationException)
                {
                    GuardFired = true;
                }
            }
        }

        private sealed class SelfOpeningWindow : UIWindow
        {
            public static UIService Service;
            public static bool GuardFired;

            protected override void OnOpening()
            {
                try
                {
                    Service.OpenAsync(WindowId).Forget();
                }
                catch (InvalidOperationException)
                {
                    GuardFired = true;
                }
            }
        }

        private sealed class SelfCloseObservingBinder : IUIWindowBinder
        {
            public UIService Service;
            public bool GuardFired;

            public IUIWindowBinding Bind(UIWindowBindingContext context)
            {
                return new Binding(this, context.Window.WindowId);
            }

            private sealed class Binding : IUIWindowBinding
            {
                private SelfCloseObservingBinder _owner;
                private readonly string _windowId;

                public Binding(SelfCloseObservingBinder owner, string windowId)
                {
                    _owner = owner;
                    _windowId = windowId;
                }

                public void OnWindowStateChanged(WindowStateCallbackType state)
                {
                    if (_owner == null || state != WindowStateCallbackType.OnStartClose)
                    {
                        return;
                    }

                    try
                    {
                        // Re-entrant close request for the window currently dispatching its
                        // own lifecycle callback. The returned task is deliberately not
                        // awaited so the current (deferred) behaviour cannot deadlock here.
                        _owner.Service.CloseAsync(_windowId).Forget();
                    }
                    catch (InvalidOperationException)
                    {
                        _owner.GuardFired = true;
                    }
                }

                public void Dispose()
                {
                    _owner = null;
                }
            }
        }
    }
}
