#if CYCLONEGAMES_HAS_DOTWEEN
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using CycloneGames.UIFramework.Runtime;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.TestTools;

namespace CycloneGames.UIFramework.Tests.PlayMode
{
    /// <summary>
    /// Long-running PlayMode soak for the LitMotion transition driver.
    ///
    /// Complements UIFrameworkSoakTests, which is EditMode and therefore cannot tick animations.
    /// This one exercises the real open/close path through LitMotion and checks three things the
    /// EditMode soak cannot observe:
    ///   1. live managed memory after a forced collection stays flat (leak gate),
    ///   2. gen2 collections stay bounded (allocation pressure gate),
    ///   3. per-bucket throughput does not drift (hang / degradation gate).
    ///
    /// Duration is controlled by UIFW_ANIM_SOAK_SECONDS (default 60, clamped 10..600).
    /// </summary>
    public sealed class DOTweenDriverSoakTests
    {
        private const string DurationEnvironmentVariable = "UIFW_ANIM_SOAK_SECONDS";
        private const int DefaultDurationSeconds = 60;
        private const int MinimumDurationSeconds = 10;
        private const int MaximumDurationSeconds = 600;
        private const int PairsPerSample = 500;
        private const long MaximumLiveGrowthBytes = 2L * 1024L * 1024L;

        /// <summary>
        /// Gen2 collections scale with total allocation, so a fixed cap would fail longer runs
        /// for healthy reasons. Measured baseline is ~3 collections per 500 pairs; the budget
        /// below allows roughly twice that rate.
        /// </summary>
        private const int Gen2PerThousandPairsBudget = 12;
        private const float MaximumThroughputDriftRatio = 3.0f;

        private sealed class Sample
        {
            public int Pairs;
            public long LiveBytes;
            public long MonoBytes;
            public int Gen2;
            public double ElapsedSeconds;
        }

        private GameObject windowObject;

        [SetUp]
        public void SetUp()
        {
            windowObject = null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (windowObject != null)
            {
                UnityEngine.Object.Destroy(windowObject);
                windowObject = null;
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator Soak_TransitionDriver_LeakAndThroughputGate()
        {
            int durationSeconds = ResolveDurationSeconds();
            var driver = new DOTweenTransitionDriver(new FadeConfig(0.01f));
            UIWindow window = CreateWindow();
            var group = window.GetComponent<CanvasGroup>();

            for (int i = 0; i < 50; i++)
            {
                yield return driver.PlayOpenAsync(window, CancellationToken.None).ToCoroutine();
                yield return driver.PlayCloseAsync(window, CancellationToken.None).ToCoroutine();
            }

            var samples = new List<Sample>(32);
            double startRealtime = Time.realtimeSinceStartup;
            double deadline = startRealtime + durationSeconds;
            int pairs = 0;
            int gen2Baseline = GC.CollectionCount(2);

            while (Time.realtimeSinceStartup < deadline)
            {
                for (int i = 0; i < PairsPerSample; i++)
                {
                    yield return driver.PlayOpenAsync(window, CancellationToken.None).ToCoroutine();
                    yield return driver.PlayCloseAsync(window, CancellationToken.None).ToCoroutine();
                    pairs++;
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                samples.Add(new Sample
                {
                    Pairs = pairs,
                    LiveBytes = GC.GetTotalMemory(true),
                    MonoBytes = Profiler.GetMonoUsedSizeLong(),
                    Gen2 = GC.CollectionCount(2) - gen2Baseline,
                    ElapsedSeconds = Time.realtimeSinceStartup - startRealtime
                });

                if (group.alpha < -0.001f || group.alpha > 1.001f || float.IsNaN(group.alpha))
                {
                    Assert.Fail($"Alpha left valid range during soak: {group.alpha}");
                }
            }

            for (int i = 0; i < samples.Count; i++)
            {
                Sample sample = samples[i];
                Debug.Log(string.Format(
                    CultureInfo.InvariantCulture,
                    "[anim-soak-dotween] pairs={0} elapsed={1:F1}s live={2} mono={3} gen2={4}",
                    sample.Pairs, sample.ElapsedSeconds, sample.LiveBytes, sample.MonoBytes, sample.Gen2));
            }

            Assert.GreaterOrEqual(samples.Count, 2, "Soak must produce at least two samples.");

            Sample baseline = samples[0];
            Sample last = samples[samples.Count - 1];
            long liveGrowth = last.LiveBytes - baseline.LiveBytes;

            Debug.Log(string.Format(
                CultureInfo.InvariantCulture,
                "[anim-soak-dotween] summary pairs={0} liveGrowth={1}B gen2={2} totalElapsed={3:F1}s",
                pairs, liveGrowth, last.Gen2, last.ElapsedSeconds));

            Assert.LessOrEqual(liveGrowth, MaximumLiveGrowthBytes,
                $"Live managed memory grew by {liveGrowth} B, limit {MaximumLiveGrowthBytes} B.");

            int allowedGen2 = Mathf.Max(4, pairs / 1000 * Gen2PerThousandPairsBudget);
            Assert.LessOrEqual(last.Gen2, allowedGen2,
                $"Gen2 collections reached {last.Gen2} over {pairs} pairs, budget {allowedGen2}.");

            if (samples.Count >= 4)
            {
                double firstHalfRate = Rate(samples[0], samples[samples.Count / 2 - 1]);
                double secondHalfRate = Rate(samples[samples.Count / 2], last);
                Debug.Log(string.Format(
                    CultureInfo.InvariantCulture,
                    "[anim-soak-dotween] throughput firstHalf={0:F1} pairs/s secondHalf={1:F1} pairs/s",
                    firstHalfRate, secondHalfRate));

                if (firstHalfRate > 0d && secondHalfRate > 0d)
                {
                    double ratio = firstHalfRate > secondHalfRate
                        ? firstHalfRate / secondHalfRate
                        : secondHalfRate / firstHalfRate;
                    Assert.LessOrEqual(ratio, MaximumThroughputDriftRatio,
                        $"Throughput drifted by {ratio:F2}x between halves; possible degradation or hang.");
                }
            }

            Assert.AreEqual(0f, group.alpha, 0.01f, "Even pair count must end closed.");
        }

        private static double Rate(Sample from, Sample to)
        {
            double seconds = to.ElapsedSeconds - from.ElapsedSeconds;
            if (seconds <= 0d) return 0d;
            return (to.Pairs - from.Pairs) / seconds;
        }

        private static int ResolveDurationSeconds()
        {
            string raw = Environment.GetEnvironmentVariable(DurationEnvironmentVariable);
            if (!int.TryParse(raw, out int parsed))
            {
                return DefaultDurationSeconds;
            }
            return Mathf.Clamp(parsed, MinimumDurationSeconds, MaximumDurationSeconds);
        }

        private UIWindow CreateWindow()
        {
            windowObject = new GameObject(
                "DotweenSoakWindow",
                typeof(RectTransform),
                typeof(CanvasGroup));
            return windowObject.AddComponent<UIWindow>();
        }
    }
}
#endif
