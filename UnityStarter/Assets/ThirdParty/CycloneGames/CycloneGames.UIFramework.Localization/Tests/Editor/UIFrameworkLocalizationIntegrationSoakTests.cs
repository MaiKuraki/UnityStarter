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
    /// Long-running integration soak: window open/close churn with a navigation graph, rotating
    /// child-close policies and continuous locale changes, all through one UIService.
    /// Duration defaults to 600 s; override with the UIFW_INT_SOAK_SECONDS environment variable.
    /// </summary>
    [Category("UIFrameworkLocalization")]
    public sealed class UIFrameworkLocalizationIntegrationSoakTests
    {
        private const string DurationEnvironmentVariable = "UIFW_INT_SOAK_SECONDS";
        private const int DefaultSeconds = 600;
        private const int IdCount = 10;
        private const int MaxDepth = 6;
        private const double SampleIntervalSeconds = 10.0;
        private const long MaximumRetainedBytesPerCycle = 24L;
        private const long MaximumFixedGrowthBytes = 1024L * 1024L;

        private static void Emit(string line)
        {
            UnityEngine.Debug.Log(line);
        }

        [Test]
        [Timeout(900000)]
        public void Soak_NavigationAndLocalization_StayFlat()
        {
            int targetSeconds = ResolveSeconds();
            var previousStackTrace = Application.GetStackTraceLogType(LogType.Log);
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            var fixture = new UIRuntimeTestFixture();
            var navigation = new UINavigationService(IdCount * 2);
            var provider = new IntegrationSoakProvider();
            var binder = new LocalizationWindowBinder(provider);
            var configurations = new UIWindowConfiguration[IdCount];
            var sampleHistory = new List<UINavigationEntry>(IdCount * 2);
            UIService service = null;

            try
            {
                service = new UIService(
                    fixture.Root,
                    null,
                    new UIServiceOptions
                    {
                        InitialWindowCapacity = IdCount,
                        MaxActiveWindows = IdCount,
                        NavigationService = navigation,
                    },
                    new IUIWindowBinder[] { binder });

                for (int i = 0; i < IdCount; i++)
                {
                    configurations[i] = fixture.CreateDirectConfiguration<UIWindow>(
                        "Int" + i.ToString("D2"), i, false);
                    GameObject prefab = configurations[i].WindowPrefab.gameObject;
                    var probeObject = new GameObject("IntProbe" + i, typeof(RectTransform));
                    probeObject.transform.SetParent(prefab.transform, false);
                    probeObject.AddComponent<IntegrationProbe>();
                }

                IntegrationProbe.ResetCounters();
                var open = new List<string>(MaxDepth);

                for (int warm = 0; warm < 64; warm++)
                {
                    UIWindow window = service.OpenAsync(configurations[warm % IdCount]).GetAwaiter().GetResult();
                    service.CloseAsync(window.WindowId).GetAwaiter().GetResult();
                }

                IntegrationProbe.ResetCounters();
                long cycles = 0L;
                long localeChanges = 0L;
                long midBytes = 0L;
                long midCycle = 0L;
                double midSeconds = 0.0;
                bool midCaptured = false;
                double nextSampleSeconds = SampleIntervalSeconds;

                var stopwatch = Stopwatch.StartNew();
                while (stopwatch.Elapsed.TotalSeconds < targetSeconds)
                {
                    int index = (int)(cycles % IdCount);
                    string opener = open.Count > 0 ? open[open.Count - 1] : null;

                    UIWindow window = service.OpenAsync(
                            configurations[index],
                            new UIOpenOptions(openerId: opener))
                        .GetAwaiter()
                        .GetResult();
                    open.Add(window.WindowId);

                    if ((cycles & 3L) == 0L)
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            provider.FireLocaleChange();
                            localeChanges++;
                        }
                    }

                    if (open.Count >= MaxDepth || index == IdCount - 1)
                    {
                        ChildClosePolicy policy = (ChildClosePolicy)(cycles % 3L);
                        while (open.Count > 0)
                        {
                            string closingId = open[open.Count - 1];
                            service.CloseAsync(closingId, policy).GetAwaiter().GetResult();
                            open.RemoveAt(open.Count - 1);
                        }
                    }

                    cycles++;

                    if (!midCaptured && stopwatch.Elapsed.TotalSeconds >= targetSeconds * 0.5)
                    {
                        midBytes = GC.GetTotalMemory(true);
                        midCycle = cycles;
                        midSeconds = stopwatch.Elapsed.TotalSeconds;
                        midCaptured = true;
                    }
                    else if (stopwatch.Elapsed.TotalSeconds >= nextSampleSeconds)
                    {
                        nextSampleSeconds += SampleIntervalSeconds;
                        Emit(
                            "INTSOAK,cycles=" + cycles +
                            ",elapsedSeconds=" + stopwatch.Elapsed.TotalSeconds.ToString("F1") +
                            ",managedBytes=" + GC.GetTotalMemory(false) +
                            ",activeWindows=" + service.ActiveWindowCount +
                            ",navigationNodes=" + navigation.CopyHistory(sampleHistory) +
                            ",binds=" + IntegrationProbe.Binds +
                            ",unbinds=" + IntegrationProbe.Unbinds +
                            ",localeChanges=" + localeChanges);
                    }
                }

                // Drain the chain that was still open when the duration elapsed.
                while (open.Count > 0)
                {
                    service.CloseAsync(open[open.Count - 1], ChildClosePolicy.Cascade)
                        .GetAwaiter()
                        .GetResult();
                    open.RemoveAt(open.Count - 1);
                }

                stopwatch.Stop();
                long finalBytes = GC.GetTotalMemory(true);
                long steadyGrowth = midCaptured ? finalBytes - midBytes : -1L;
                long secondHalfCycles = midCaptured ? cycles - midCycle : 0L;
                double lateRate = secondHalfCycles > 0L
                    ? secondHalfCycles / Math.Max(stopwatch.Elapsed.TotalSeconds - midSeconds, 0.001)
                    : 0.0;
                var history = new List<UINavigationEntry>(IdCount * 2);

                Emit(
                    "INTSOAK,summary,cycles=" + cycles +
                    ",localeChanges=" + localeChanges +
                    ",elapsedSeconds=" + stopwatch.Elapsed.TotalSeconds.ToString("F2") +
                    ",binds=" + IntegrationProbe.Binds +
                    ",unbinds=" + IntegrationProbe.Unbinds +
                    ",activeWindows=" + service.ActiveWindowCount +
                    ",navigationNodes=" + navigation.CopyHistory(history) +
                    ",midManagedBytes=" + midBytes +
                    ",finalManagedBytes=" + finalBytes +
                    ",steadyGrowthBytes=" + steadyGrowth +
                    ",secondHalfCycles=" + secondHalfCycles +
                    ",lateCyclesPerSecond=" + lateRate.ToString("F1") +
                    ",retainedBytesPerCycle=" +
                    (secondHalfCycles > 0L ? ((double)steadyGrowth / secondHalfCycles).ToString("F2") : "n/a"));

                Assert.That(midCaptured, Is.True, "the soak must reach its halfway point");
                Assert.That(service.ActiveWindowCount, Is.EqualTo(0));
                Assert.That(navigation.CopyHistory(history), Is.EqualTo(0));
                Assert.That(service.GetPerformanceStats().SessionCount, Is.EqualTo(0));
                Assert.That(
                    IntegrationProbe.Unbinds,
                    Is.EqualTo(IntegrationProbe.Binds),
                    "every localization binding must be released");
                Assert.That(navigation.CurrentWindow, Is.Null);

                long growthBudget = MaximumFixedGrowthBytes + secondHalfCycles * MaximumRetainedBytesPerCycle;
                Assert.That(
                    steadyGrowth,
                    Is.LessThan(growthBudget),
                    "retained managed memory grew by " + steadyGrowth + " B over " + secondHalfCycles +
                    " second-half cycles (" +
                    (secondHalfCycles > 0L ? ((double)steadyGrowth / secondHalfCycles).ToString("F2") : "n/a") +
                    " B/cycle, budget " + MaximumRetainedBytesPerCycle + " B/cycle)");
            }
            finally
            {
                Application.SetStackTraceLogType(LogType.Log, previousStackTrace);
                service?.Dispose();
                fixture.Dispose();
            }
        }

        private static int ResolveSeconds()
        {
            string raw = Environment.GetEnvironmentVariable(DurationEnvironmentVariable);
            if (!string.IsNullOrEmpty(raw) && int.TryParse(raw, out int parsed))
            {
                return Mathf.Clamp(parsed, 5, 3600);
            }

            return DefaultSeconds;
        }

        private sealed class IntegrationProbe : MonoBehaviour, ILocalizationBindingTarget
        {
            public static int Binds;
            public static int Unbinds;

            public static void ResetCounters()
            {
                Binds = 0;
                Unbinds = 0;
            }

            public void Bind(in LocalizationBindingContext context)
            {
                Binds++;
            }

            public void Unbind()
            {
                Unbinds++;
            }
        }

        private sealed class IntegrationSoakProvider : ILocalizationProvider
        {
            private static readonly List<LocaleId> Locales = new List<LocaleId>(2)
            {
                new LocaleId("en-US"),
                new LocaleId("ja-JP"),
            };

            private Action<LocalizationChange> _changed;
            private int _localeIndex;

            public LocaleId CurrentLocale { get; private set; } = new LocaleId("en-US");
            public IReadOnlyList<LocaleId> AvailableLocales { get { return Locales; } }
            public bool IsInitialized { get { return true; } }
            public long Revision { get; private set; }

            public event Action<LocalizationChange> Changed
            {
                add { _changed += value; }
                remove { _changed -= value; }
            }

            public void FireLocaleChange()
            {
                LocaleId previous = CurrentLocale;
                _localeIndex = (_localeIndex + 1) % Locales.Count;
                CurrentLocale = Locales[_localeIndex];
                Revision++;
                Action<LocalizationChange> handler = _changed;
                if (handler != null)
                {
                    handler(new LocalizationChange(
                        previous,
                        CurrentLocale,
                        LocalizationChangeReason.LocaleChanged,
                        Revision));
                }
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
    }
}
