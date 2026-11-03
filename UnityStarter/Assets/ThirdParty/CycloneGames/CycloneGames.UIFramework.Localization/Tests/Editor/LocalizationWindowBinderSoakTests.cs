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
    /// Sustained open/close churn with localization binding and periodic locale changes.
    /// Duration defaults to 60 s; override with the UIFW_LOC_SOAK_SECONDS environment variable.
    /// </summary>
    [Category("UIFrameworkLocalization")]
    public sealed class LocalizationWindowBinderSoakTests
    {
        private const string DurationEnvironmentVariable = "UIFW_LOC_SOAK_SECONDS";
        private const int DefaultSeconds = 60;
        private const int SampleIntervalCycles = 500;
        private const long MaximumRetainedBytes = 1024L * 1024L;
        private const long MaximumRetainedBytesPerCycle = 16L;

        private static void Emit(string line)
        {
            UnityEngine.Debug.Log(line);
            TestContext.Progress.WriteLine(line);
        }

        [Test]
        [Timeout(300000)]
        public void Soak_OpenCloseWithLocaleChurn_StaysFlat()
        {
            int targetSeconds = ResolveSeconds();
            var fixture = new UIRuntimeTestFixture();
            UIService service = null;
            try
            {
                var provider = new LocalizationWindowBinderSoakProvider();
                var binder = new LocalizationWindowBinder(provider);
                service = new UIService(
                    fixture.Root,
                    null,
                    new UIServiceOptions { InitialWindowCapacity = 4, MaxActiveWindows = 8 },
                    new IUIWindowBinder[] { binder });

                UIWindowConfiguration configuration =
                    fixture.CreateDirectConfiguration<UIWindow>("SoakWindow", 0, false);
                GameObject prefab = configuration.WindowPrefab.gameObject;
                var probeObject = new GameObject("SoakProbe", typeof(RectTransform));
                probeObject.transform.SetParent(prefab.transform, false);
                probeObject.AddComponent<SoakProbe>();
                SoakProbe.ResetCounters();

                // Warm-up so the baseline excludes JIT and first-instantiation costs.
                for (int i = 0; i < 64; i++)
                {
                    UIWindow warm = service.OpenAsync(configuration).GetAwaiter().GetResult();
                    service.CloseAsync(warm.WindowId).GetAwaiter().GetResult();
                }

                SoakProbe.ResetCounters();

                long midBytes = 0L;
                long midCycle = 0L;
                double midSeconds = 0.0;
                bool midCaptured = false;
                long cycles = 0L;
                long localeChanges = 0L;

                var stopwatch = Stopwatch.StartNew();
                while (stopwatch.Elapsed.TotalSeconds < targetSeconds)
                {
                    UIWindow window = service.OpenAsync(configuration).GetAwaiter().GetResult();
                    if ((cycles & 3L) == 0L)
                    {
                        for (int i = 0; i < 8; i++)
                        {
                            provider.FireLocaleChange();
                            localeChanges++;
                        }
                    }

                    service.CloseAsync(window.WindowId).GetAwaiter().GetResult();
                    cycles++;

                    if (!midCaptured && stopwatch.Elapsed.TotalSeconds >= targetSeconds * 0.5)
                    {
                        midBytes = GC.GetTotalMemory(true);
                        midCycle = cycles;
                        midSeconds = stopwatch.Elapsed.TotalSeconds;
                        midCaptured = true;
                    }
                    else if (cycles % SampleIntervalCycles == 0L)
                    {
                        Emit(
                            "LOCSOAK,cycles=" + cycles +
                            ",elapsedSeconds=" + stopwatch.Elapsed.TotalSeconds.ToString("F1") +
                            ",managedBytes=" + GC.GetTotalMemory(false) +
                            ",bindCount=" + SoakProbe.Binds +
                            ",unbindCount=" + SoakProbe.Unbinds);
                    }
                }

                stopwatch.Stop();
                long finalBytes = GC.GetTotalMemory(true);
                long steadyGrowth = midCaptured ? finalBytes - midBytes : -1L;
                long secondHalfCycles = midCaptured ? cycles - midCycle : 0L;
                double lateRate = secondHalfCycles > 0L
                    ? secondHalfCycles / Math.Max(stopwatch.Elapsed.TotalSeconds - midSeconds, 0.001)
                    : 0.0;

                Emit(
                    "LOCSOAK,summary,cycles=" + cycles +
                    ",localeChanges=" + localeChanges +
                    ",elapsedSeconds=" + stopwatch.Elapsed.TotalSeconds.ToString("F2") +
                    ",bindCount=" + SoakProbe.Binds +
                    ",unbindCount=" + SoakProbe.Unbinds +
                    ",midManagedBytes=" + midBytes +
                    ",finalManagedBytes=" + finalBytes +
                    ",steadyGrowthBytes=" + steadyGrowth +
                    ",secondHalfCycles=" + secondHalfCycles +
                    ",lateCyclesPerSecond=" + lateRate.ToString("F1") +
                    ",retainedBytesPerCycle=" +
                    (secondHalfCycles > 0L ? ((double)steadyGrowth / secondHalfCycles).ToString("F2") : "n/a"));

                Assert.That(midCaptured, Is.True, "the soak must reach its halfway point");
                Assert.That(
                    SoakProbe.Binds,
                    Is.EqualTo((int)cycles),
                    "every opened window must bind exactly once");
                Assert.That(
                    SoakProbe.Unbinds,
                    Is.EqualTo((int)cycles),
                    "every closed window must unbind exactly once");
                Assert.That(service.ActiveWindowCount, Is.EqualTo(0));
                Assert.That(service.GetPerformanceStats().SessionCount, Is.EqualTo(0));
                long growthBudget = MaximumRetainedBytes + secondHalfCycles * MaximumRetainedBytesPerCycle;
                Assert.That(
                    steadyGrowth,
                    Is.LessThan(growthBudget),
                    "retained managed memory grew by " + steadyGrowth + " B over " + secondHalfCycles +
                    " second-half cycles (" +
                    (secondHalfCycles > 0L ? ((double)steadyGrowth / secondHalfCycles).ToString("F2") : "n/a") +
                    " B/cycle, budget " + MaximumRetainedBytesPerCycle + " B/cycle + 1 MB)");
            }
            finally
            {
                service?.Dispose();
                fixture.Dispose();
            }
        }

        private static int ResolveSeconds()
        {
            string raw = Environment.GetEnvironmentVariable(DurationEnvironmentVariable);
            if (!string.IsNullOrEmpty(raw) && int.TryParse(raw, out int parsed))
            {
                return Mathf.Clamp(parsed, 5, 1800);
            }

            return DefaultSeconds;
        }

        private sealed class SoakProbe : MonoBehaviour, ILocalizationBindingTarget
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

        private sealed class LocalizationWindowBinderSoakProvider : ILocalizationProvider
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
