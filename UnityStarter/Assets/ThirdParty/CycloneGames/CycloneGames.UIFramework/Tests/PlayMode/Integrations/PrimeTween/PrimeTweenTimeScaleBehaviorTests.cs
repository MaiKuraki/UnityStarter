#if CYCLONEGAMES_HAS_PRIMETWEEN
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using CycloneGames.UIFramework.Runtime;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CycloneGames.UIFramework.Tests.PlayMode
{
    /// <summary>
    /// Measures wall-clock behaviour of the LitMotion transition driver under different
    /// Time.timeScale values. Each sample is written to Temp~/uifw-anim-tests/measurements.csv
    /// so the full matrix can be compared across rounds instead of relying on pass/fail only.
    ///
    /// The discriminator is elapsed wall-clock time:
    ///   scaled behaviour  -> elapsed ~= duration / timeScale
    ///   unscaled behaviour -> elapsed ~= duration
    /// A zero time scale separates the two completely: scaled never completes.
    /// </summary>
    public sealed class PrimeTweenTimeScaleBehaviorTests
    {
        private struct Sample
        {
            public string Round;
            public string Config;
            public float TimeScale;
            public float Duration;
            public float Elapsed;
            public bool Completed;
            public float FinalAlpha;
        }

        private readonly List<Sample> samples = new List<Sample>();
        private GameObject windowObject;
        private float originalTimeScale;

        [SetUp]
        public void SetUp()
        {
            originalTimeScale = Time.timeScale;
            windowObject = null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = originalTimeScale;
            DestroyWindow();
            yield return null;
            WriteReport();
        }

        [UnityTest]
        public IEnumerator RoundA_TimeScaleSweep()
        {
            float[] scales = { 1f, 0.5f, 0.25f, 2f, 0f };
            for (int i = 0; i < scales.Length; i++)
            {
                yield return Measure("A_TimeScale", new FadeConfig(0.3f), scales[i], 2.5f);
            }
        }

        [UnityTest]
        public IEnumerator RoundB_DurationSweep_AtNormalSpeed()
        {
            float[] durations = { 0.05f, 0.1f, 0.3f, 0.5f, 1f };
            for (int i = 0; i < durations.Length; i++)
            {
                yield return Measure("B_Duration_ts1", new FadeConfig(durations[i]), 1f, durations[i] * 3f + 2f);
            }
        }

        [UnityTest]
        public IEnumerator RoundB_DurationSweep_AtHalfSpeed()
        {
            float[] durations = { 0.05f, 0.1f, 0.3f, 0.5f, 1f };
            for (int i = 0; i < durations.Length; i++)
            {
                yield return Measure("B_Duration_ts0.5", new FadeConfig(durations[i]), 0.5f, durations[i] * 5f + 2f);
            }
        }

        [UnityTest]
        public IEnumerator RoundC_ConfigTypes()
        {
            TransitionConfigBase[] configs =
            {
                new FadeConfig(0.3f),
                new ScaleConfig(0.8f, 0.3f),
                new SlideConfig(SlideDirection.Bottom, 1f, 0.3f),
                new CompositeConfig(true, new ScaleConfig(0.9f, 0.3f), null, 0.3f)
            };
            string[] names = { "Fade", "Scale", "Slide", "Composite" };
            float[] scales = { 1f, 0.5f, 0f };

            for (int c = 0; c < configs.Length; c++)
            {
                for (int s = 0; s < scales.Length; s++)
                {
                    yield return Measure("C_Config", configs[c], scales[s], 2.5f, names[c]);
                }
            }
        }

        [UnityTest]
        public IEnumerator RoundD_ResumeAfterZeroTimeScale()
        {
            Time.timeScale = 0f;
            var driver = new PrimeTweenTransitionDriver(new FadeConfig(0.3f));
            var window = CreateWindow();
            var group = window.GetComponent<CanvasGroup>();
            group.alpha = 0f;

            float start = Time.realtimeSinceStartup;
            var task = driver.PlayOpenAsync(window, CancellationToken.None);

            float frozenDeadline = start + 0.5f;
            while (Time.realtimeSinceStartup < frozenDeadline) yield return null;

            float alphaWhileFrozen = group.alpha;
            bool advancedWhileFrozen = alphaWhileFrozen > 0.001f;
            Debug.Log(string.Format(
                CultureInfo.InvariantCulture,
                "[anim-ts-primetween] RoundD alphaAt0.5s={0:F3} advancedWhileFrozen={1}",
                alphaWhileFrozen,
                advancedWhileFrozen));

            Time.timeScale = 1f;
            float resumeDeadline = Time.realtimeSinceStartup + 3f;
            while (task.Status == UniTaskStatus.Pending && Time.realtimeSinceStartup < resumeDeadline) yield return null;

            Record(new Sample
            {
                Round = "D_Resume",
                Config = "Fade",
                TimeScale = 0f,
                Duration = 0.3f,
                Elapsed = Time.realtimeSinceStartup - start,
                Completed = task.Status == UniTaskStatus.Succeeded,
                FinalAlpha = group.alpha
            });

            Assert.AreEqual(UniTaskStatus.Succeeded, task.Status, "Fade must complete after timeScale returns to 1.");

            DestroyWindow();
            yield return null;
        }

        [UnityTest]
        public IEnumerator RoundE_RepeatHalfSpeed_Stability()
        {
            for (int i = 0; i < 3; i++)
            {
                yield return Measure("E_Repeat_ts0.5", new FadeConfig(0.3f), 0.5f, 2.5f);
            }
        }

        private IEnumerator Measure(string round, TransitionConfigBase config, float timeScale, float timeout, string label = null)
        {
            Time.timeScale = timeScale;
            var driver = new PrimeTweenTransitionDriver(config);
            var window = CreateWindow();
            var group = window.GetComponent<CanvasGroup>();
            group.alpha = 0f;

            float start = Time.realtimeSinceStartup;
            var task = driver.PlayOpenAsync(window, CancellationToken.None);
            float deadline = start + timeout;

            while (task.Status == UniTaskStatus.Pending && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            float elapsed = Time.realtimeSinceStartup - start;
            if (task.Status == UniTaskStatus.Faulted)
            {
                try { task.GetAwaiter().GetResult(); }
                catch (Exception exception) { Debug.LogError($"[anim-ts-primetween] {round} faulted: {exception}"); }
            }

            Record(new Sample
            {
                Round = round,
                Config = label ?? config.GetType().Name,
                TimeScale = timeScale,
                Duration = config.Duration,
                Elapsed = elapsed,
                Completed = task.Status == UniTaskStatus.Succeeded,
                FinalAlpha = group.alpha
            });

            DestroyWindow();
            yield return null;
        }

        private UIWindow CreateWindow()
        {
            windowObject = new GameObject(
                "PrimeTweenProbeWindow",
                typeof(RectTransform),
                typeof(CanvasGroup));
            return windowObject.AddComponent<UIWindow>();
        }

        private void DestroyWindow()
        {
            if (windowObject == null) return;
            UnityEngine.Object.Destroy(windowObject);
            windowObject = null;
        }

        private void Record(Sample sample)
        {
            samples.Add(sample);
            float scaledPrediction = sample.TimeScale > 0f ? sample.Duration / sample.TimeScale : float.PositiveInfinity;
            float unscaledPrediction = sample.Duration;
            Debug.Log(string.Format(
                CultureInfo.InvariantCulture,
                "[anim-ts-primetween] round={0} config={1} ts={2} dur={3} elapsed={4:F3} completed={5} scaledPred={6:F3} unscaledPred={7:F3} alpha={8:F3}",
                sample.Round,
                sample.Config,
                sample.TimeScale,
                sample.Duration,
                sample.Elapsed,
                sample.Completed,
                scaledPrediction,
                unscaledPrediction,
                sample.FinalAlpha));
        }

        private void WriteReport()
        {
            if (samples.Count == 0) return;
            try
            {
                string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Temp~", "uifw-anim-tests"));
                Directory.CreateDirectory(root);
                string path = Path.Combine(root, "measurements.csv");

                var builder = new StringBuilder();
                if (!File.Exists(path))
                {
                    builder.AppendLine("round,config,timeScale,duration,elapsed,completed,finalAlpha,scaledPrediction,unscaledPrediction,closerTo");
                }

                for (int i = 0; i < samples.Count; i++)
                {
                    Sample sample = samples[i];
                    float scaledPrediction = sample.TimeScale > 0f ? sample.Duration / sample.TimeScale : float.PositiveInfinity;
                    float unscaledPrediction = sample.Duration;
                    string closer = ClassifyCloser(sample.Elapsed, scaledPrediction, unscaledPrediction);
                    builder.AppendLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0},{1},{2},{3},{4:F4},{5},{6:F4},{7},{8},{9}",
                        sample.Round,
                        sample.Config,
                        sample.TimeScale,
                        sample.Duration,
                        sample.Elapsed,
                        sample.Completed,
                        sample.FinalAlpha,
                        float.IsInfinity(scaledPrediction) ? "inf" : scaledPrediction.ToString("F4", CultureInfo.InvariantCulture),
                        unscaledPrediction.ToString("F4", CultureInfo.InvariantCulture),
                        closer));
                }

                File.AppendAllText(path, builder.ToString(), Encoding.UTF8);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[anim-ts-primetween] failed to write report: {exception}");
            }

            samples.Clear();
        }

        private static string ClassifyCloser(float elapsed, float scaledPrediction, float unscaledPrediction)
        {
            float scaledDelta = Mathf.Abs(elapsed - scaledPrediction);
            float unscaledDelta = Mathf.Abs(elapsed - unscaledPrediction);
            if (float.IsInfinity(scaledPrediction)) return "scaled(never)";
            if (scaledDelta < unscaledDelta * 0.5f) return "scaled";
            if (unscaledDelta < scaledDelta * 0.5f) return "unscaled";
            return "ambiguous";
        }
    }
}
#endif
