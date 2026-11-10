#if CYCLONEGAMES_HAS_PRIMETWEEN
using System;
using System.Collections;
using System.Threading;
using CycloneGames.UIFramework.Runtime;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CycloneGames.UIFramework.Tests.PlayMode
{
    /// <summary>
    /// Cancellation semantics of the transition drivers. A cancelled transition must leave the
    /// window in a visually consistent state instead of forcing a terminal value.
    /// </summary>
    public sealed class PrimeTweenTransitionCancellationTests
    {
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
        public IEnumerator CancelCloseMidway_DoesNotForceAlphaToZero()
        {
            var driver = new PrimeTweenTransitionDriver(new FadeConfig(0.4f));
            UIWindow window = CreateWindow();
            var group = window.GetComponent<CanvasGroup>();

            group.alpha = 1f;
            yield return driver.PlayOpenAsync(window, CancellationToken.None).ToCoroutine();

            Assert.That(group.alpha, Is.EqualTo(1f).Within(0.01f), "Open should reach alpha 1.");

            using var cts = new CancellationTokenSource();
            UniTask closeTask = driver.PlayCloseAsync(window, cts.Token);

            yield return new WaitForSecondsRealtime(0.2f);
            float alphaAtCancel = group.alpha;
            cts.Cancel();

            Exception captured = null;
            yield return closeTask.ToCoroutine(exception => captured = exception);

            Assert.That(captured, Is.InstanceOf<OperationCanceledException>(),
                "Cancelled close must surface OperationCanceledException.");

            Debug.Log(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "[anim-cancel-primetween] alphaAtCancel={0:F3} alphaAfterCancel={1:F3}",
                alphaAtCancel,
                group.alpha));

            Assert.That(alphaAtCancel, Is.LessThan(1f).And.GreaterThan(0f),
                "Test requires the close to be interrupted midway.");
            Assert.That(group.alpha, Is.EqualTo(alphaAtCancel).Within(0.05f),
                "A cancelled close must not overwrite the current alpha with a terminal value.");
        }

        [UnityTest]
        public IEnumerator CancelOpenMidway_DoesNotForceAlphaToOne()
        {
            var driver = new PrimeTweenTransitionDriver(new FadeConfig(0.4f));
            UIWindow window = CreateWindow();
            var group = window.GetComponent<CanvasGroup>();
            group.alpha = 0f;

            using var cts = new CancellationTokenSource();
            UniTask openTask = driver.PlayOpenAsync(window, cts.Token);

            yield return new WaitForSecondsRealtime(0.2f);
            float alphaAtCancel = group.alpha;
            cts.Cancel();

            Exception captured = null;
            yield return openTask.ToCoroutine(exception => captured = exception);

            Assert.That(captured, Is.InstanceOf<OperationCanceledException>());
            Assert.That(group.alpha, Is.EqualTo(alphaAtCancel).Within(0.05f),
                "A cancelled open must not force alpha to 1.");
        }

        [UnityTest]
        public IEnumerator ReopenAfterCancelledClose_ReachesFullAlpha()
        {
            var driver = new PrimeTweenTransitionDriver(new FadeConfig(0.2f));
            UIWindow window = CreateWindow();
            var group = window.GetComponent<CanvasGroup>();

            yield return driver.PlayOpenAsync(window, CancellationToken.None).ToCoroutine();

            using (var cts = new CancellationTokenSource())
            {
                UniTask closeTask = driver.PlayCloseAsync(window, cts.Token);
                yield return new WaitForSecondsRealtime(0.08f);
                cts.Cancel();
                yield return closeTask.ToCoroutine(_ => { });
            }

            yield return driver.PlayOpenAsync(window, CancellationToken.None).ToCoroutine();

            Assert.That(group.alpha, Is.EqualTo(1f).Within(0.01f),
                "Reopening after a cancelled close must still reach full alpha.");
            Assert.IsTrue(group.interactable, "Interactable must be restored after open.");
            Assert.IsTrue(group.blocksRaycasts, "Raycast blocking must be restored after open.");
        }

        [UnityTest]
        public IEnumerator PreCancelledToken_LeavesWindowUntouched()
        {
            var driver = new PrimeTweenTransitionDriver(new FadeConfig(0.2f));
            UIWindow window = CreateWindow();
            var group = window.GetComponent<CanvasGroup>();
            group.alpha = 0.37f;

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Exception captured = null;
            yield return driver.PlayCloseAsync(window, cts.Token).ToCoroutine(exception => captured = exception);

            Assert.That(captured, Is.InstanceOf<OperationCanceledException>());
            Assert.That(group.alpha, Is.EqualTo(0.37f).Within(0.001f),
                "A pre-cancelled close must not modify alpha.");
        }

        private UIWindow CreateWindow()
        {
            windowObject = new GameObject(
                "PrimeTweenCancelWindow",
                typeof(RectTransform),
                typeof(CanvasGroup));
            return windowObject.AddComponent<UIWindow>();
        }
    }
}
#endif
