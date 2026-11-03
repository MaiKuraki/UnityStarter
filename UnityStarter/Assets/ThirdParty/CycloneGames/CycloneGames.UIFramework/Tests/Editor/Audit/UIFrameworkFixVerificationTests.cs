using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using CycloneGames.UIFramework.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace CycloneGames.UIFramework.Tests.Editor.Audit
{
    /// <summary>Regression tests for the repaired lifetime, ownership and navigation defects.</summary>
    [Category("UIFrameworkAudit")]
    public sealed class UIFrameworkFixVerificationTests
    {
        private const int TimeoutMilliseconds = 180000;

        private static void Emit(string line)
        {
            UnityEngine.Debug.Log(line);
            TestContext.Progress.WriteLine(line);
        }

        // ------------------------------------------------------------------ D-18 / D-04

        [Test]
        [Timeout(TimeoutMilliseconds)]
        public async Task Fix_D18_ExternalDestroy_ReleasesSessionLayerAndBindings()
        {
            using (var harness = new AuditHarness())
            {
                AuditAssetProvider provider = harness.CreateProvider();
                UIService service = harness.CreateService(
                    provider, AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, null), null);
                var binder = new AuditBinder("D18Binder");
                service.RegisterBinder(binder);

                UIWindowConfiguration configuration = harness.CreateDirect("D18Target", 0, false);
                UIWindow window = await service.OpenAsync(configuration);

                Assert.That(binder.BindCount, Is.EqualTo(1));
                Assert.That(binder.DisposeCount, Is.EqualTo(0));

                // Self-diagnosing probe: report every stage of the lifetime relay so a failure
                // says exactly where the notification chain broke instead of only "session alive".
                Component relay = FindComponentByName(window.gameObject, "UIWindowLifetimeRelay");
                Assert.That(relay, Is.Not.Null, "the lifetime relay component must exist");
                Type relayType = relay.GetType();
                FieldInfo observerField = relayType.GetField(
                    "_observer", BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo notifiedField = relayType.GetField(
                    "_notified", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(observerField, Is.Not.Null, "relay observer field must exist");
                object observer = observerField.GetValue(relay);

                string stateBefore = window.State.ToString();
                bool activeBeforeDestroy = window.gameObject.activeInHierarchy;

                UnityEngine.Object.DestroyImmediate(window.gameObject);
                service.ReconcileDestroyedWindows();

                bool notified = notifiedField != null && (bool)notifiedField.GetValue(relay);
                FieldInfo destroyFlagField = typeof(UIWindow).GetField(
                    "_destroyNotified", BindingFlags.Instance | BindingFlags.NonPublic);
                bool windowNotified = destroyFlagField != null &&
                                      (bool)destroyFlagField.GetValue(window);
                int sessionsAfterDestroy = service.GetPerformanceStats().SessionCount;
                string diagnosis =
                    "relayFound=True,observerBound=" + (observer != null) +
                    ",relayNotified=" + notified +
                    ",windowNotified=" + windowNotified +
                    ",activeBefore=" + activeBeforeDestroy +
                    ",stateBefore=" + stateBefore +
                    ",stateAfter=" + window.State +
                    ",sessionsAfterDestroy=" + sessionsAfterDestroy;

                if (sessionsAfterDestroy != 0)
                {
                    // Unity may not have invoked OnDestroy (destroy ordering, batching). Invoke the
                    // handler manually to separate "not called" from "called but rejected".
                    MethodInfo onDestroy = relayType.GetMethod(
                        "OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (onDestroy != null)
                    {
                        onDestroy.Invoke(relay, null);
                        diagnosis += ",manualInvokeSessions=" +
                                     service.GetPerformanceStats().SessionCount;
                    }
                }

                Assert.That(
                    sessionsAfterDestroy,
                    Is.EqualTo(0),
                    "the destroyed window must release its session [" + diagnosis + "]");
                Assert.That(
                    harness.Layer.WindowCount,
                    Is.EqualTo(0),
                    "the destroyed window must detach from the layer");
                Assert.That(
                    service.ActiveWindowCount,
                    Is.EqualTo(0),
                    "active count must drop to zero");
                Assert.That(
                    binder.DisposeCount,
                    Is.EqualTo(1),
                    "the binding must be disposed exactly once");

                var buffer = new List<UIWindow>(2);
                harness.Layer.CopyWindows(buffer);
                Assert.That(buffer.Count, Is.EqualTo(0), "no fake-null entry may survive in the layer");

                service.CopyActiveWindows(buffer);
                Assert.That(buffer.Count, Is.EqualTo(0), "no fake-null entry may survive in the service");
            }
        }

        [Test]
        [Timeout(TimeoutMilliseconds)]
        public async Task Fix_D04_ExternalDestroy_FaultsThePendingClose()
        {
            using (var harness = new AuditHarness())
            {
                AuditAssetProvider provider = harness.CreateProvider();
                var driver = new AuditTransitionDriver();
                driver.CloseGate = new UniTaskCompletionSource();
                UIServiceOptions options = AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, null);
                options.DefaultTransitionDriver = driver;
                UIService service = harness.CreateService(provider, options, null);

                UIWindow window = await service.OpenAsync(harness.CreateDirect("D04Target", 0, false));
                UniTask<bool> close = service.CloseAsync("D04Target");
                Assert.That(close.Status, Is.EqualTo(UniTaskStatus.Pending));

                UnityEngine.Object.DestroyImmediate(window.gameObject);
                service.ReconcileDestroyedWindows();

                // Contract: the pending close converges to "closed" (the object is gone), the
                // session is released, and the ownership violation is reported as a warning rather
                // than by changing the return shape of every CloseAsync call site.
                bool closed = await close;
                Assert.That(
                    closed,
                    Is.True,
                    "a close whose window is destroyed externally must converge to closed");
                Assert.That(
                    service.GetPerformanceStats().SessionCount,
                    Is.EqualTo(0),
                    "the session must be released");

                if (driver.CloseGate != null)
                {
                    driver.CloseGate.TrySetResult();
                }

                Assert.That(service.GetPerformanceStats().SessionCount, Is.EqualTo(0));
            }
        }

        // ------------------------------------------------------------------ D-03

        [Test]
        [Timeout(TimeoutMilliseconds)]
        public async Task Fix_D03_AsyncBindingSelfClose_ThrowsInsteadOfDeadlocking()
        {
            using (var harness = new AuditHarness())
            {
                AuditAssetProvider provider = harness.CreateProvider();
                var binder = new SelfClosingAsyncBinder();
                UIService service = harness.CreateService(
                    provider,
                    AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, null),
                    new IUIWindowBinder[] { binder });

                binder.Service = service;
                UIWindow window = await service.OpenAsync(harness.CreateDirect("D03Target", 0, false));

                // The async binding closes its own window from inside OnStartClose. That request
                // must be rejected synchronously instead of awaiting its own completion source.
                UniTask<bool> close = service.CloseAsync(window.WindowId);
                bool closed = await close;

                Assert.That(closed, Is.True, "the outer close must still complete");
                Assert.That(
                    binder.GuardHitCount,
                    Is.EqualTo(1),
                    "the re-entrant close must be rejected with InvalidOperationException");
                Assert.That(
                    service.GetPerformanceStats().SessionCount,
                    Is.EqualTo(0),
                    "the session must be released");
            }
        }

        // ------------------------------------------------------------------ D-07

        [Test]
        [Timeout(TimeoutMilliseconds)]
        public async Task Fix_D07_RollbackBeforeOpen_DoesNotDispatchCloseCallbacks()
        {
            using (var harness = new AuditHarness())
            {
                AuditAssetProvider provider = harness.CreateProvider();
                var binder = new AuditBinder("D07Binder");
                binder.ThrowOnBind = true;
                binder.ThrowForWindowId = "D07Target";
                UIService service = harness.CreateService(
                    provider,
                    AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, null),
                    new IUIWindowBinder[] { binder });

                LifecycleProbeWindow.ResetCounters();
                UIWindowConfiguration configuration =
                    harness.Fixture.CreateDirectConfiguration<LifecycleProbeWindow>("D07Target", 0, false);

                Exception observed = null;
                try
                {
                    await service.OpenAsync(configuration);
                }
                catch (Exception exception)
                {
                    observed = exception;
                }

                Assert.That(observed, Is.Not.Null, "the open must fail at the binder stage");
                Assert.That(LifecycleProbeWindow.OpeningCount, Is.EqualTo(0));
                Assert.That(LifecycleProbeWindow.OpenedCount, Is.EqualTo(0));
                Assert.That(
                    LifecycleProbeWindow.ClosingCount,
                    Is.EqualTo(0),
                    "a window that never entered Opening must not receive OnClosing");
                Assert.That(
                    LifecycleProbeWindow.ClosedCount,
                    Is.EqualTo(0),
                    "a window that never entered Opening must not receive OnClosed");
                Assert.That(
                    LifecycleProbeWindow.LastState,
                    Is.EqualTo(UIWindowState.Created),
                    "no lifecycle callback may run for a window rolled back before the open " +
                    "stage, so the probe must never observe a transition");
            }
        }

        // ------------------------------------------------------------------ D-05

        [Test]
        [Timeout(TimeoutMilliseconds)]
        public async Task Fix_D05_DecoratedLayer_KeepsWindowOrderAndDecorationBehind()
        {
            using (var harness = new AuditHarness())
            {
                AuditAssetProvider provider = harness.CreateProvider();
                UIService service = harness.CreateService(
                    provider, AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, null), null);

                // Decoration first: a background owned by the layer itself.
                var decoration = new GameObject("LayerBackground", typeof(RectTransform), typeof(Image));
                decoration.transform.SetParent(harness.Layer.transform, false);

                var opened = new List<UIWindow>(4);
                int[] priorities = { 10, -5, 0 };
                for (int i = 0; i < priorities.Length; i++)
                {
                    UIWindowConfiguration configuration =
                        harness.CreateDirect("Decor" + i, priorities[i], false);
                    opened.Add(await service.OpenAsync(configuration));
                }

                var ordered = new List<UIWindow>(4);
                harness.Layer.CopyWindows(ordered);

                Assert.That(ordered.Count, Is.EqualTo(3));
                Assert.That(ordered[0].Priority, Is.EqualTo(-5));
                Assert.That(ordered[1].Priority, Is.EqualTo(0));
                Assert.That(ordered[2].Priority, Is.EqualTo(10));

                Assert.That(
                    decoration.transform.GetSiblingIndex(),
                    Is.EqualTo(0),
                    "a layer background must stay behind every window");

                for (int i = 0; i < ordered.Count; i++)
                {
                    Assert.That(
                        ordered[i].transform.GetSiblingIndex(),
                        Is.EqualTo(i + 1),
                        "window " + i + " must occupy sibling slot " + (i + 1) +
                        " (0 is the decoration); actual " + ordered[i].transform.GetSiblingIndex());
                }

                Emit(
                    "FIX,name=D05,decorationSibling=" + decoration.transform.GetSiblingIndex() +
                    ",windowSiblings=" + ordered[0].transform.GetSiblingIndex() + "/" +
                    ordered[1].transform.GetSiblingIndex() + "/" +
                    ordered[2].transform.GetSiblingIndex());

                UnityEngine.Object.DestroyImmediate(decoration);
            }
        }

        // ------------------------------------------------------------------ D-01

        [Test]
        [Timeout(TimeoutMilliseconds)]
        public void Fix_D01_ProviderDrainBound_IsValidatedAndDefaultsToFiveSeconds()
        {
            var defaults = new UIServiceOptions();
            Assert.That(
                defaults.MaxProviderDrainSeconds,
                Is.EqualTo(5),
                "the drain bound must default to a finite value so an ignoring provider " +
                "cannot hang shutdown");

            var zero = new UIServiceOptions { MaxProviderDrainSeconds = 0 };
            using (var harness = new AuditHarness())
            {
                Assert.That(
                    () => { harness.CreateService(harness.CreateProvider(), zero, null); },
                    Throws.Nothing,
                    "zero must remain a valid opt-out (wait indefinitely)");
            }

            var negative = new UIServiceOptions { MaxProviderDrainSeconds = -1 };
            using (var harness = new AuditHarness())
            {
                Assert.That(
                    () => { harness.CreateService(harness.CreateProvider(), negative, null); },
                    Throws.TypeOf<ArgumentOutOfRangeException>(),
                    "a negative bound must be rejected instead of disabling the guard silently");
            }
        }

        // ------------------------------------------------------------------ D-02

        [Test]
        [Timeout(TimeoutMilliseconds)]
        public void Fix_D02_DestroyedRoot_SelfDisposesOnSceneChange()
        {
            using (var harness = new AuditHarness())
            {
                UIService service = harness.CreateService(
                    harness.CreateProvider(),
                    AuditHarness.CreateOptions(AuditDims.CapacityRelaxed, null),
                    null);

                Assert.That(service.IsDisposed, Is.False);

                UnityEngine.Object.DestroyImmediate(harness.Root.gameObject);

                MethodInfo handler = typeof(UIService).GetMethod(
                    "OnActiveSceneChanged",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(handler, Is.Not.Null, "the scene handler must exist for this probe");
                handler.Invoke(service, new object[] { default(UnityEngine.SceneManagement.Scene), default(UnityEngine.SceneManagement.Scene) });

                Assert.That(
                    service.IsDisposed,
                    Is.True,
                    "an abandoned service whose UIRoot is gone must release itself from the " +
                    "static scene event instead of staying alive and acting on a dead hierarchy");
            }
        }

        // ------------------------------------------------------------------ D-15

        [Test]
        [Timeout(TimeoutMilliseconds)]
        public void Fix_D15_WideSequentialUnregister_IsLinearNotQuadratic()
        {
            const int nodeCount = 4096;
            var navigation = new UINavigationService(nodeCount);

            for (int i = 0; i < nodeCount; i++)
            {
                Assert.That(navigation.Register("Lin" + i.ToString("D5")), Is.True);
            }

            var affected = new List<string>(8);
            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < nodeCount; i++)
            {
                Assert.That(
                    navigation.Unregister("Lin" + i.ToString("D5"), ChildClosePolicy.Detach, affected),
                    Is.True);
            }

            stopwatch.Stop();
            double totalMs = stopwatch.Elapsed.TotalMilliseconds;

            var history = new List<UINavigationEntry>(8);
            Assert.That(navigation.CopyHistory(history), Is.EqualTo(0));
            Assert.That(navigation.CurrentWindow, Is.Null);

            Emit(
                "FIX,name=D15,unregisters=" + nodeCount +
                ",totalMs=" + totalMs.ToString("F1") +
                ",perUnregisterMicros=" + (totalMs * 1000.0 / nodeCount).ToString("F1"));

            // Pre-fix measurement on the same machine: 2226.5 ms (543.6 us/unregister).
            // Amortised compaction must land far below that; 800 ms still leaves a 2.7x margin.
            Assert.That(
                totalMs,
                Is.LessThan(800.0),
                nodeCount + " sequential unregisters took " + totalMs.ToString("F1") +
                " ms; O(1) removal is expected to stay far below this bound.");
        }

        [Test]
        [Timeout(TimeoutMilliseconds)]
        public void Fix_D15_CurrentWindow_StillResolvesAfterDeferredCompaction()
        {
            var navigation = new UINavigationService(32);
            Assert.That(navigation.Register("A"), Is.True);
            Assert.That(navigation.Register("B", "A"), Is.True);
            Assert.That(navigation.Register("C", "B"), Is.True);
            Assert.That(navigation.CurrentWindow, Is.EqualTo("C"));

            var affected = new List<string>(8);
            Assert.That(navigation.Unregister("C", ChildClosePolicy.Reparent, affected), Is.True);
            Assert.That(
                navigation.CurrentWindow,
                Is.EqualTo("B"),
                "CurrentWindow must resolve to the last live node while stale tail entries " +
                "are still awaiting compaction");

            Assert.That(navigation.Unregister("B", ChildClosePolicy.Reparent, affected), Is.True);
            Assert.That(navigation.CurrentWindow, Is.EqualTo("A"));

            Assert.That(navigation.Unregister("A", ChildClosePolicy.Reparent, affected), Is.True);
            Assert.That(navigation.CurrentWindow, Is.Null);
        }

        // ------------------------------------------------------------------ probes

        private static Component FindComponentByName(GameObject target, string typeName)
        {
            Component[] components = target.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component != null &&
                    string.Equals(component.GetType().Name, typeName, StringComparison.Ordinal))
                {
                    return component;
                }
            }

            return null;
        }

        private sealed class LifecycleProbeWindow : UIWindow
        {
            public static int OpeningCount;
            public static int OpenedCount;
            public static int ClosingCount;
            public static int ClosedCount;
            public static UIWindowState LastState;

            public static void ResetCounters()
            {
                OpeningCount = 0;
                OpenedCount = 0;
                ClosingCount = 0;
                ClosedCount = 0;
                LastState = UIWindowState.Created;
            }

            protected override void OnOpening()
            {
                OpeningCount++;
                LastState = State;
            }

            protected override void OnOpened()
            {
                OpenedCount++;
                LastState = State;
            }

            protected override void OnClosing()
            {
                ClosingCount++;
                LastState = State;
            }

            protected override void OnClosed()
            {
                ClosedCount++;
                LastState = State;
            }
        }

        private sealed class SelfClosingAsyncBinder : IUIWindowBinder
        {
            public UIService Service;
            public int GuardHitCount;

            public IUIWindowBinding Bind(UIWindowBindingContext context)
            {
                return new Binding(this, context.Window.WindowId);
            }

            private sealed class Binding : IAsyncUIWindowBinding
            {
                private readonly SelfClosingAsyncBinder _owner;
                private readonly string _windowId;

                public Binding(SelfClosingAsyncBinder owner, string windowId)
                {
                    _owner = owner;
                    _windowId = windowId;
                }

                public UniTask OnWindowStateChangedAsync(
                    WindowStateCallbackType state,
                    CancellationToken cancellationToken)
                {
                    if (state != WindowStateCallbackType.OnStartClose)
                    {
                        return UniTask.CompletedTask;
                    }

                    try
                    {
                        // Same-window close from inside this window's own async stage.
                        _owner.Service.CloseAsync(_windowId);
                    }
                    catch (InvalidOperationException)
                    {
                        _owner.GuardHitCount++;
                    }

                    return UniTask.CompletedTask;
                }

                public void OnWindowStateChanged(WindowStateCallbackType state)
                {
                }

                public void Dispose()
                {
                }
            }
        }
    }
}
