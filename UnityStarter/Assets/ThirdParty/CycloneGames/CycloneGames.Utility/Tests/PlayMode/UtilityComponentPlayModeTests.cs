using System.Collections;

using CycloneGames.Utility.Runtime;

using NUnit.Framework;

using UnityEngine;
using UnityEngine.TestTools;

namespace CycloneGames.Utility.Tests.PlayMode
{
    public sealed class UtilityComponentPlayModeTests
    {
        [UnityTest]
        public IEnumerator FpsCounter_SamplesElapsedTimeWithBoundedAverage()
        {
            var owner = new GameObject("FPS Counter Test");
            FPSCounter counter = owner.AddComponent<FPSCounter>();
            try
            {
                for (int i = 0; i < 10; i++)
                {
                    counter.SampleFrame(0.1f);
                }

                Assert.That(counter.CurrentFPS, Is.EqualTo(10));
                Assert.That(counter.AverageFPS, Is.EqualTo(10));

                counter.ResetAverage();
                Assert.That(counter.AverageFPS, Is.Zero);
                yield return null;
            }
            finally
            {
                Object.Destroy(owner);
            }
        }

        [UnityTest]
        public IEnumerator AdaptiveSafeAreaFitter_NeverAppliesInvertedAnchors()
        {
            var owner = new GameObject("Safe Area Test", typeof(RectTransform));
            AdaptiveSafeAreaFitter fitter = owner.AddComponent<AdaptiveSafeAreaFitter>();
            RectTransform rectTransform = owner.GetComponent<RectTransform>();
            try
            {
                fitter.PaddingPixels = new Vector4(100_000f, 100_000f, 100_000f, 100_000f);
                fitter.Refresh();
                yield return null;

                Assert.That(rectTransform.anchorMin.x, Is.LessThanOrEqualTo(rectTransform.anchorMax.x));
                Assert.That(rectTransform.anchorMin.y, Is.LessThanOrEqualTo(rectTransform.anchorMax.y));
                Assert.That(rectTransform.offsetMin, Is.EqualTo(Vector2.zero));
                Assert.That(rectTransform.offsetMax, Is.EqualTo(Vector2.zero));

                fitter.enabled = false;
                fitter.enabled = true;
                yield return null;
                Assert.That(fitter.isActiveAndEnabled, Is.True);
            }
            finally
            {
                Object.Destroy(owner);
            }
        }
    }
}
