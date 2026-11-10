#if CYCLONEGAMES_HAS_LITMOTION
using System;
using System.Collections;
using System.Globalization;
using System.Threading;
using CycloneGames.UIFramework.Runtime;
using Cysharp.Threading.Tasks;
using LitMotion;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CycloneGames.UIFramework.Tests.PlayMode
{
    /// <summary>
    /// Isolates the driver layer from the async/coroutine bridge.
    ///
    /// Scope note: the project already has UIFrameworkAllocationTests, UIFrameworkSoakTests,
    /// UIFrameworkFuzzTests and UIFrameworkLoadStressTests, all in EditMode with stricter
    /// thresholds than anything here. Those cover the UIService transaction pipeline but cannot
    /// exercise animation, because EditMode has no PlayerLoop tick. This file therefore measures
    /// only the pieces that require a running PlayerLoop, and deliberately does not duplicate
    /// soak, fuzz or transaction allocation coverage.
    ///
    /// Counter validity is established by AllocationProbeTests:
    /// GC.GetAllocatedBytesForCurrentThread reports 0 in this host, GC.GetTotalMemory(false)
    /// tracks a known 1 MB allocation.
    /// </summary>
    public sealed class LitMotionDriverAllocationTests
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
        public IEnumerator MotionCreateAndCancel_AllocationPerCall()
        {
            UIWindow window = CreateWindow();

            for (int i = 0; i < 20; i++)
            {
                var warmup = LMotion.Create(0f, 1f, 0.01f)
                    .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                    .WithEase(Ease.Linear)
                    .RunWithoutBinding();
                warmup.Cancel();
            }

            yield return null;
            long before = GC.GetTotalMemory(false);

            const int iterations = 400;
            for (int i = 0; i < iterations; i++)
            {
                var handle = LMotion.Create(0f, 1f, 0.01f)
                    .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                    .WithEase(Ease.Linear)
                    .RunWithoutBinding();
                handle.Cancel();
            }

            long after = GC.GetTotalMemory(false);
            Debug.Log(string.Format(
                CultureInfo.InvariantCulture,
                "[driver-alloc] motionCreateAndCancel iterations={0} perCall={1}B",
                iterations, (after - before) / iterations));
        }

        [UnityTest]
        public IEnumerator FullTransition_ViaCoroutineBridge_AllocationPerTransition()
        {
            var driver = new LitMotionTransitionDriver(new FadeConfig(0.01f));
            UIWindow window = CreateWindow();

            for (int i = 0; i < 10; i++)
            {
                yield return driver.PlayOpenAsync(window, CancellationToken.None).ToCoroutine();
                yield return driver.PlayCloseAsync(window, CancellationToken.None).ToCoroutine();
            }

            yield return null;
            long before = GC.GetTotalMemory(false);

            const int pairs = 200;
            for (int i = 0; i < pairs; i++)
            {
                yield return driver.PlayOpenAsync(window, CancellationToken.None).ToCoroutine();
                yield return driver.PlayCloseAsync(window, CancellationToken.None).ToCoroutine();
            }

            long after = GC.GetTotalMemory(false);
            Debug.Log(string.Format(
                CultureInfo.InvariantCulture,
                "[driver-alloc] fadeViaCoroutine pairs={0} perTransition={1}B",
                pairs, (after - before) / (pairs * 2)));
        }

        private UIWindow CreateWindow()
        {
            windowObject = new GameObject(
                "DriverAllocWindow",
                typeof(RectTransform),
                typeof(CanvasGroup));
            return windowObject.AddComponent<UIWindow>();
        }
    }
}
#endif
