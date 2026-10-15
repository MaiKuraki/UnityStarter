using System.Collections.Generic;
using CycloneGames.UIFramework.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.UIFramework.Tests.Editor
{
    public sealed class UITransitionCoordinatorTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>(4);

        [TearDown]
        public void TearDown()
        {
            for (int i = _objects.Count - 1; i >= 0; i--)
            {
                if (_objects[i] != null)
                {
                    Object.DestroyImmediate(_objects[i]);
                }
            }

            _objects.Clear();
        }

        [Test]
        public void CrossFade_Completion_RestoresInputStateForBothWindows()
        {
            UIWindow leaving = CreateWindow("Leaving", true, out CanvasGroup leavingGroup);
            UIWindow entering = CreateWindow("Entering", true, out CanvasGroup enteringGroup);
            leavingGroup.interactable = true;
            leavingGroup.blocksRaycasts = false;
            enteringGroup.interactable = false;
            enteringGroup.blocksRaycasts = true;

            var coordinator = new CrossFadeTransitionCoordinator(0f);
            coordinator
                .TransitionAsync(leaving, entering, NavigationDirection.Replace)
                .GetAwaiter()
                .GetResult();

            Assert.That(leavingGroup.alpha, Is.EqualTo(0f));
            Assert.That(leavingGroup.interactable, Is.True);
            Assert.That(leavingGroup.blocksRaycasts, Is.False);
            Assert.That(enteringGroup.alpha, Is.EqualTo(1f));
            Assert.That(enteringGroup.interactable, Is.False);
            Assert.That(enteringGroup.blocksRaycasts, Is.True);
        }

        [Test]
        public void CrossFade_Completion_AddsCanvasGroupWhenMissing()
        {
            UIWindow leaving = CreateWindow("Leaving", false, out _);
            UIWindow entering = CreateWindow("Entering", false, out _);

            var coordinator = new CrossFadeTransitionCoordinator(0f);
            coordinator
                .TransitionAsync(leaving, entering, NavigationDirection.Replace)
                .GetAwaiter()
                .GetResult();

            CanvasGroup leavingGroup = leaving.GetComponent<CanvasGroup>();
            CanvasGroup enteringGroup = entering.GetComponent<CanvasGroup>();
            Assert.That(leavingGroup, Is.Not.Null);
            Assert.That(enteringGroup, Is.Not.Null);
            Assert.That(leavingGroup.interactable, Is.True);
            Assert.That(leavingGroup.blocksRaycasts, Is.True);
            Assert.That(enteringGroup.interactable, Is.True);
            Assert.That(enteringGroup.blocksRaycasts, Is.True);
        }

        [Test]
        public void Slide_Completion_RestoresInputStateForBothWindows()
        {
            UIWindow leaving = CreateWindow("Leaving", true, out CanvasGroup leavingGroup);
            UIWindow entering = CreateWindow("Entering", true, out CanvasGroup enteringGroup);
            leavingGroup.interactable = true;
            leavingGroup.blocksRaycasts = false;
            enteringGroup.interactable = false;
            enteringGroup.blocksRaycasts = true;

            var coordinator = new SlideTransitionCoordinator(0f);
            coordinator
                .TransitionAsync(leaving, entering, NavigationDirection.Forward)
                .GetAwaiter()
                .GetResult();

            Assert.That(leavingGroup.interactable, Is.True);
            Assert.That(leavingGroup.blocksRaycasts, Is.False);
            Assert.That(enteringGroup.interactable, Is.False);
            Assert.That(enteringGroup.blocksRaycasts, Is.True);
        }

        [Test]
        public void Slide_Completion_AddsCanvasGroupWhenMissing()
        {
            UIWindow leaving = CreateWindow("Leaving", false, out _);
            UIWindow entering = CreateWindow("Entering", false, out _);

            var coordinator = new SlideTransitionCoordinator(0f);
            coordinator
                .TransitionAsync(leaving, entering, NavigationDirection.Backward)
                .GetAwaiter()
                .GetResult();

            CanvasGroup leavingGroup = leaving.GetComponent<CanvasGroup>();
            CanvasGroup enteringGroup = entering.GetComponent<CanvasGroup>();
            Assert.That(leavingGroup, Is.Not.Null);
            Assert.That(enteringGroup, Is.Not.Null);
            Assert.That(leavingGroup.interactable, Is.True);
            Assert.That(enteringGroup.interactable, Is.True);
        }

        private UIWindow CreateWindow(string name, bool withCanvasGroup, out CanvasGroup group)
        {
            GameObject windowObject = withCanvasGroup
                ? new GameObject(name, typeof(RectTransform), typeof(CanvasGroup))
                : new GameObject(name, typeof(RectTransform));
            _objects.Add(windowObject);
            group = windowObject.GetComponent<CanvasGroup>();
            return windowObject.AddComponent<UIWindow>();
        }
    }
}
