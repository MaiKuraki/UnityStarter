#if CYCLONEGAMES_HAS_LITMOTION
using System;
using System.Threading;
using CycloneGames.UIFramework.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.UIFramework.Tests.Editor
{
    public sealed class LitMotionTransitionDriverTests
    {
        private GameObject _windowObject;

        [TearDown]
        public void TearDown()
        {
            if (_windowObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_windowObject);
                _windowObject = null;
            }
        }

        [Test]
        public void PlayOpenAsync_PreCancelledToken_ThrowsOperationCanceled()
        {
            var driver = new LitMotionTransitionDriver(FadeConfig.Default);
            UIWindow window = CreateWindow();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.CatchAsync<OperationCanceledException>(
                async () => await driver.PlayOpenAsync(window, cts.Token));
        }

        [Test]
        public void PlayCloseAsync_PreCancelledToken_ThrowsOperationCanceled()
        {
            var driver = new LitMotionTransitionDriver(FadeConfig.Default);
            UIWindow window = CreateWindow();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.CatchAsync<OperationCanceledException>(
                async () => await driver.PlayCloseAsync(window, cts.Token));
        }

        private UIWindow CreateWindow()
        {
            _windowObject = new GameObject(
                "LitMotionDriverWindow",
                typeof(RectTransform),
                typeof(CanvasGroup));
            return _windowObject.AddComponent<UIWindow>();
        }
    }
}
#endif
