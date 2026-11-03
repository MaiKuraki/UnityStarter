using System;
using System.Collections.Generic;
using System.Diagnostics;
using CycloneGames.Localization.Core;
using CycloneGames.UIFramework.Runtime;
using CycloneGames.UIFramework.Runtime.Integrations.Localization;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.UIFramework.Tests.Editor.Integrations.Localization
{
    /// <summary>
    /// Combinations of the localization binder with the window lifecycle: deferred initialization,
    /// multiple windows, bind failure rollback, windows destroyed before binding, and locale churn.
    /// Counters are aggregated statically because the binder binds instantiated copies, not prefabs.
    /// </summary>
    [Category("UIFrameworkLocalization")]
    public sealed class LocalizationWindowBinderCombinationTests
    {
        private UIRuntimeTestFixture _fixture;
        private readonly List<UIService> _services = new List<UIService>(2);
        private readonly Dictionary<string, UIWindowConfiguration> _configurations =
            new Dictionary<string, UIWindowConfiguration>(4, StringComparer.Ordinal);

        [SetUp]
        public void SetUp()
        {
            _fixture = new UIRuntimeTestFixture();
            BindingProbe.ResetStatics();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _services.Count - 1; i >= 0; i--)
            {
                UIService service = _services[i];
                if (service != null && !service.IsDisposed)
                {
                    service.Dispose();
                }
            }

            _services.Clear();
            _configurations.Clear();
            _fixture?.Dispose();
        }

        [Test]
        public void BoundService_BindsEveryTargetOnce()
        {
            var provider = new FakeLocalizationProvider(initialized: true);
            UIService service = CreateService(provider);
            PrepareWindow("WindowA");

            UIWindow window = Open(service, "WindowA");
            Assert.That(BindingProbe.BindsFor("WindowA"), Is.EqualTo(1));
            Assert.That(BindingProbe.UnbindsFor("WindowA"), Is.EqualTo(0));

            Close(service, window.WindowId);
            Assert.That(BindingProbe.UnbindsFor("WindowA"), Is.EqualTo(1));
        }

        [Test]
        public void UninitializedService_DefersBindingUntilInitialized()
        {
            var provider = new FakeLocalizationProvider(initialized: false);
            UIService service = CreateService(provider);
            PrepareWindow("WindowB");

            UIWindow window = Open(service, "WindowB");
            Assert.That(BindingProbe.BindsFor("WindowB"), Is.EqualTo(0), "binding must wait for initialization");
            Assert.That(provider.SubscriberCount, Is.EqualTo(1), "the binder must subscribe while waiting");

            provider.Initialize();
            Assert.That(BindingProbe.BindsFor("WindowB"), Is.EqualTo(1));
            Assert.That(provider.SubscriberCount, Is.EqualTo(0),
                "the binder must unsubscribe once the deferred bind ran");

            Close(service, window.WindowId);
            Assert.That(BindingProbe.UnbindsFor("WindowB"), Is.EqualTo(1));
        }

        [Test]
        public void WindowClosedWhileDeferred_NeverBinds()
        {
            var provider = new FakeLocalizationProvider(initialized: false);
            UIService service = CreateService(provider);
            PrepareWindow("WindowC");

            UIWindow window = Open(service, "WindowC");
            Assert.That(provider.SubscriberCount, Is.EqualTo(1));

            Close(service, window.WindowId);
            Assert.That(provider.SubscriberCount, Is.EqualTo(0), "closing must drop the deferred subscription");

            provider.Initialize();
            Assert.That(BindingProbe.BindsFor("WindowC"), Is.EqualTo(0), "a closed window must not bind later");
        }

        [Test]
        public void WindowDestroyedWhileDeferred_ReconcilesAndNeverBinds()
        {
            var provider = new FakeLocalizationProvider(initialized: false);
            UIService service = CreateService(provider);
            PrepareWindow("WindowD");

            UIWindow window = Open(service, "WindowD");
            UnityEngine.Object.DestroyImmediate(window.gameObject);
            service.ReconcileDestroyedWindows();

            Assert.That(provider.SubscriberCount, Is.EqualTo(0), "the destroyed window binding must unsubscribe");
            provider.Initialize();
            Assert.That(BindingProbe.BindsFor("WindowD"), Is.EqualTo(0));
            Assert.That(service.ActiveWindowCount, Is.EqualTo(0));
        }

        [Test]
        public void MultipleWindows_BindIndependently_AndUnbindOnClose()
        {
            var provider = new FakeLocalizationProvider(initialized: true);
            UIService service = CreateService(provider);
            PrepareWindow("MultiA");
            PrepareWindow("MultiB");

            UIWindow first = Open(service, "MultiA");
            UIWindow second = Open(service, "MultiB");

            Assert.That(BindingProbe.BindsFor("MultiA"), Is.EqualTo(1));
            Assert.That(BindingProbe.BindsFor("MultiB"), Is.EqualTo(1));

            Close(service, first.WindowId);
            Assert.That(BindingProbe.UnbindsFor("MultiA"), Is.EqualTo(1));
            Assert.That(BindingProbe.UnbindsFor("MultiB"), Is.EqualTo(0),
                "closing one window must not unbind the other");

            Close(service, second.WindowId);
            Assert.That(BindingProbe.UnbindsFor("MultiB"), Is.EqualTo(1));
        }

        [Test]
        public void TargetBindFailure_RollsBackAndFailsTheOpen()
        {
            var provider = new FakeLocalizationProvider(initialized: true);
            UIService service = CreateService(provider);
            PrepareWindow("FailingWindow", addFailingProbe: true);

            Exception observed = null;
            try
            {
                Open(service, "FailingWindow");
            }
            catch (Exception exception)
            {
                observed = exception;
            }

            Assert.That(observed, Is.Not.Null, "a bind failure must fail the open transaction");
            Assert.That(BindingProbe.BindsFor("FailingWindow"), Is.EqualTo(1));
            Assert.That(BindingProbe.UnbindsFor("FailingWindow"), Is.EqualTo(1),
                "the healthy target must be rolled back");
            Assert.That(service.ActiveWindowCount, Is.EqualTo(0));
        }

        [Test]
        public void LocaleChurn_DoesNotRebindOrRescan()
        {
            var provider = new FakeLocalizationProvider(initialized: true);
            UIService service = CreateService(provider);
            PrepareWindow("Churn");
            UIWindow window = Open(service, "Churn");

            const int changes = 1000;
            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < changes; i++)
            {
                provider.ChangeLocale(i % 2 == 0 ? "en-US" : "ja-JP");
            }

            stopwatch.Stop();

            UnityEngine.Debug.Log(
                "LOCBENCH,localeChanges=" + changes +
                ",totalMs=" + stopwatch.Elapsed.TotalMilliseconds.ToString("F2") +
                ",bindCount=" + BindingProbe.BindsFor("Churn"));

            Assert.That(BindingProbe.BindsFor("Churn"), Is.EqualTo(1),
                "locale changes must not re-run the binder");
            Assert.That(BindingProbe.UnbindsFor("Churn"), Is.EqualTo(0));
            Assert.That(stopwatch.Elapsed.TotalMilliseconds, Is.LessThan(500.0),
                "1000 locale notifications took " + stopwatch.Elapsed.TotalMilliseconds.ToString("F1") + " ms");

            Close(service, window.WindowId);
        }

        [Test]
        public void BindAndUnbindCycles_StayWithinBudget()
        {
            var provider = new FakeLocalizationProvider(initialized: true);
            UIService service = CreateService(provider);
            PrepareWindow("Cycles");

            const int cycles = 300;
            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < cycles; i++)
            {
                UIWindow window = Open(service, "Cycles");
                Close(service, window.WindowId);
            }

            stopwatch.Stop();
            double perCycleMs = stopwatch.Elapsed.TotalMilliseconds / cycles;

            UnityEngine.Debug.Log(
                "LOCBENCH,cycles=" + cycles +
                ",totalMs=" + stopwatch.Elapsed.TotalMilliseconds.ToString("F1") +
                ",perCycleMs=" + perCycleMs.ToString("F3") +
                ",binds=" + BindingProbe.BindsFor("Cycles") +
                ",unbinds=" + BindingProbe.UnbindsFor("Cycles"));

            Assert.That(BindingProbe.BindsFor("Cycles"), Is.EqualTo(cycles));
            Assert.That(BindingProbe.UnbindsFor("Cycles"), Is.EqualTo(cycles));
            Assert.That(service.ActiveWindowCount, Is.EqualTo(0));
        }

        private UIService CreateService(ILocalizationProvider provider)
        {
            var binder = new LocalizationWindowBinder(provider);
            var service = new UIService(
                _fixture.Root,
                null,
                new UIServiceOptions { InitialWindowCapacity = 4, MaxActiveWindows = 8 },
                new IUIWindowBinder[] { binder });
            _services.Add(service);
            return service;
        }

        private void PrepareWindow(string windowId, bool addFailingProbe = false)
        {
            UIWindowConfiguration configuration =
                _fixture.CreateDirectConfiguration<UIWindow>(windowId, 0, false);
            _configurations[windowId] = configuration;

            GameObject prefab = configuration.WindowPrefab.gameObject;
            AddProbe(prefab, windowId);
            if (addFailingProbe)
            {
                BindingProbe failing = AddProbe(prefab, windowId + "_Failing");
                failing.ThrowOnBind = true;
            }
        }

        private static BindingProbe AddProbe(GameObject prefab, string name)
        {
            var probeObject = new GameObject(name, typeof(RectTransform));
            probeObject.transform.SetParent(prefab.transform, false);
            return probeObject.AddComponent<BindingProbe>();
        }

        private UIWindow Open(UIService service, string windowId)
        {
            return service.OpenAsync(_configurations[windowId]).GetAwaiter().GetResult();
        }

        private static void Close(UIService service, string windowId)
        {
            service.CloseAsync(windowId).GetAwaiter().GetResult();
        }

        private sealed class FakeLocalizationProvider : ILocalizationProvider
        {
            private readonly List<LocaleId> _locales = new List<LocaleId>(4);
            private Action<LocalizationChange> _changed;

            public FakeLocalizationProvider(bool initialized)
            {
                IsInitialized = initialized;
                CurrentLocale = new LocaleId("en-US");
                _locales.Add(CurrentLocale);
                _locales.Add(new LocaleId("ja-JP"));
            }

            public LocaleId CurrentLocale { get; private set; }
            public IReadOnlyList<LocaleId> AvailableLocales { get { return _locales; } }
            public bool IsInitialized { get; private set; }
            public long Revision { get; private set; }

            public int SubscriberCount
            {
                get { return _changed == null ? 0 : _changed.GetInvocationList().Length; }
            }

            public event Action<LocalizationChange> Changed
            {
                add { _changed += value; }
                remove { _changed -= value; }
            }

            public void Initialize()
            {
                IsInitialized = true;
                Revision++;
                Raise(LocalizationChangeReason.Initialized, default);
            }

            public void ChangeLocale(string code)
            {
                LocaleId previous = CurrentLocale;
                CurrentLocale = new LocaleId(code);
                Revision++;
                Raise(LocalizationChangeReason.LocaleChanged, previous);
            }

            private void Raise(LocalizationChangeReason reason, LocaleId previous)
            {
                Action<LocalizationChange> handler = _changed;
                if (handler == null)
                {
                    return;
                }

                handler(new LocalizationChange(previous, CurrentLocale, reason, Revision));
            }

            public string GetString(string tableId, string entryKey)
            {
                return entryKey;
            }

            public bool TryGetString(string tableId, string entryKey, out string value)
            {
                value = entryKey;
                return true;
            }

            public string GetFormattedString(string tableId, string entryKey, params object[] args)
            {
                return entryKey;
            }

            public string GetPluralString(string tableId, string entryKey, int count)
            {
                return entryKey;
            }
        }

        private sealed class BindingProbe : MonoBehaviour, ILocalizationBindingTarget
        {
            private static readonly Dictionary<string, int> BindCounts =
                new Dictionary<string, int>(4, StringComparer.Ordinal);
            private static readonly Dictionary<string, int> UnbindCounts =
                new Dictionary<string, int>(4, StringComparer.Ordinal);

            public bool ThrowOnBind;

            public static void ResetStatics()
            {
                BindCounts.Clear();
                UnbindCounts.Clear();
            }

            public static int BindsFor(string name)
            {
                return BindCounts.TryGetValue(name, out int value) ? value : 0;
            }

            public static int UnbindsFor(string name)
            {
                return UnbindCounts.TryGetValue(name, out int value) ? value : 0;
            }

            public void Bind(in LocalizationBindingContext context)
            {
                BindCounts[gameObject.name] = BindsFor(gameObject.name) + 1;
                if (ThrowOnBind)
                {
                    throw new InvalidOperationException("Probe bind failure.");
                }
            }

            public void Unbind()
            {
                UnbindCounts[gameObject.name] = UnbindsFor(gameObject.name) + 1;
            }
        }
    }
}
