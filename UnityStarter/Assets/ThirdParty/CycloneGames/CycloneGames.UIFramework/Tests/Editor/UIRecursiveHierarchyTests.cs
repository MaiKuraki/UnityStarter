using System.Collections.Generic;
using CycloneGames.UIFramework.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.UIFramework.Tests.Editor
{
    public sealed class UIRecursiveHierarchyTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>(8);
        private readonly List<UIWindowConfiguration> _configurations =
            new List<UIWindowConfiguration>(2);

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

            for (int i = _configurations.Count - 1; i >= 0; i--)
            {
                if (_configurations[i] != null)
                {
                    Object.DestroyImmediate(_configurations[i]);
                }
            }

            _objects.Clear();
            _configurations.Clear();
        }

        [Test]
        public void Coordinator_RestoresRootButNeverTouchesChildCanvasGroups()
        {
            UIWindow leaving = CreateWindowWithChildGroups(
                "Leaving",
                out CanvasGroup leavingRoot,
                out CanvasGroup leavingChild,
                out CanvasGroup leavingGrandchild);
            UIWindow entering = CreateWindowWithChildGroups(
                "Entering",
                out CanvasGroup enteringRoot,
                out CanvasGroup enteringChild,
                out CanvasGroup enteringGrandchild);

            // Non-default states on every child level so any framework write is observable.
            leavingChild.alpha = 0.3f;
            leavingChild.interactable = false;
            leavingChild.blocksRaycasts = false;
            leavingGrandchild.alpha = 0.7f;
            leavingGrandchild.interactable = true;
            leavingGrandchild.blocksRaycasts = false;
            enteringChild.alpha = 0.3f;
            enteringChild.interactable = false;
            enteringChild.blocksRaycasts = false;
            enteringGrandchild.alpha = 0.7f;
            enteringGrandchild.interactable = true;
            enteringGrandchild.blocksRaycasts = false;

            leavingRoot.interactable = true;
            leavingRoot.blocksRaycasts = false;
            enteringRoot.interactable = false;
            enteringRoot.blocksRaycasts = true;

            var crossFade = new CrossFadeTransitionCoordinator(0f);
            crossFade
                .TransitionAsync(leaving, entering, NavigationDirection.Replace)
                .GetAwaiter()
                .GetResult();

            AssertChildState(leavingChild, 0.3f, false, false);
            AssertChildState(leavingGrandchild, 0.7f, true, false);
            AssertChildState(enteringChild, 0.3f, false, false);
            AssertChildState(enteringGrandchild, 0.7f, true, false);
            Assert.That(leavingRoot.interactable, Is.True);
            Assert.That(leavingRoot.blocksRaycasts, Is.False);
            Assert.That(enteringRoot.interactable, Is.False);
            Assert.That(enteringRoot.blocksRaycasts, Is.True);

            var slide = new SlideTransitionCoordinator(0f);
            slide
                .TransitionAsync(leaving, entering, NavigationDirection.Forward)
                .GetAwaiter()
                .GetResult();

            AssertChildState(leavingChild, 0.3f, false, false);
            AssertChildState(leavingGrandchild, 0.7f, true, false);
            AssertChildState(enteringChild, 0.3f, false, false);
            AssertChildState(enteringGrandchild, 0.7f, true, false);
            Assert.That(leavingRoot.interactable, Is.True);
            Assert.That(leavingRoot.blocksRaycasts, Is.False);
            Assert.That(enteringRoot.interactable, Is.False);
            Assert.That(enteringRoot.blocksRaycasts, Is.True);
        }

        [Test]
        public void Coordinator_RestoresPreHiddenWindowToHiddenState()
        {
            UIWindow leaving = CreateWindow("Leaving", out CanvasGroup leavingGroup);
            UIWindow entering = CreateWindow("Entering", out CanvasGroup enteringGroup);
            TestReflection.Invoke(leaving, "Awake");
            enteringGroup.interactable = true;
            enteringGroup.blocksRaycasts = true;

            leaving.SetVisible(false);
            Assert.That(leavingGroup.alpha, Is.EqualTo(0f));
            Assert.That(leavingGroup.interactable, Is.False);
            Assert.That(leavingGroup.blocksRaycasts, Is.False);

            var coordinator = new CrossFadeTransitionCoordinator(0f);
            coordinator
                .TransitionAsync(leaving, entering, NavigationDirection.Replace)
                .GetAwaiter()
                .GetResult();

            // The hidden window must stay hidden: input flags restore to their
            // pre-transition snapshot (false), not to an enabled state.
            Assert.That(leavingGroup.alpha, Is.EqualTo(0f));
            Assert.That(leavingGroup.interactable, Is.False);
            Assert.That(leavingGroup.blocksRaycasts, Is.False);
            Assert.That(enteringGroup.alpha, Is.EqualTo(1f));
            Assert.That(enteringGroup.interactable, Is.True);
            Assert.That(enteringGroup.blocksRaycasts, Is.True);
        }

        [Test]
        public void CullWhenHidden_Reactivate_PreservesChildActiveStates()
        {
            GameObject windowObject = Track(new GameObject(
                "Window",
                typeof(RectTransform),
                typeof(CanvasGroup)));
            CanvasGroup canvasGroup = windowObject.GetComponent<CanvasGroup>();
            UIWindow window = windowObject.AddComponent<UIWindow>();
            TestReflection.Invoke(window, "Awake");

            UIWindowConfiguration configuration =
                ScriptableObject.CreateInstance<UIWindowConfiguration>();
            _configurations.Add(configuration);
            TestReflection.SetField(configuration, "cullWhenHidden", true);
            TestReflection.SetField(window, "_configuration", configuration);

            // Children are owned by the tracked window root, so they need no tracking.
            GameObject childA = CreateChild(windowObject.transform, "ChildA");
            GameObject childB = CreateChild(windowObject.transform, "ChildB");
            GameObject childC = CreateChild(windowObject.transform, "ChildC");
            childB.SetActive(false);

            window.SetVisible(false);

            Assert.IsFalse(windowObject.activeSelf);
            Assert.IsFalse(childA.activeInHierarchy);
            Assert.IsFalse(childB.activeInHierarchy);
            Assert.IsFalse(childC.activeInHierarchy);

            window.SetVisible(true);

            Assert.IsTrue(windowObject.activeSelf);
            Assert.IsTrue(childA.activeSelf);
            Assert.IsFalse(childB.activeSelf);
            Assert.IsTrue(childC.activeSelf);
            Assert.That(canvasGroup.alpha, Is.EqualTo(1f));
            Assert.That(canvasGroup.interactable, Is.True);
            Assert.That(canvasGroup.blocksRaycasts, Is.True);
        }

        private UIWindow CreateWindowWithChildGroups(
            string name,
            out CanvasGroup root,
            out CanvasGroup child,
            out CanvasGroup grandchild)
        {
            UIWindow window = CreateWindow(name, out root);
            GameObject childObject = CreateChild(window.transform, name + "_Child");
            child = childObject.AddComponent<CanvasGroup>();
            GameObject grandchildObject = CreateChild(childObject.transform, name + "_Grandchild");
            grandchild = grandchildObject.AddComponent<CanvasGroup>();
            return window;
        }

        private UIWindow CreateWindow(string name, out CanvasGroup group)
        {
            GameObject windowObject = Track(new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasGroup)));
            group = windowObject.GetComponent<CanvasGroup>();
            return windowObject.AddComponent<UIWindow>();
        }

        private static GameObject CreateChild(Transform parent, string name)
        {
            var child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(parent, false);
            return child;
        }

        private static void AssertChildState(
            CanvasGroup group,
            float alpha,
            bool interactable,
            bool blocksRaycasts)
        {
            Assert.That(group.alpha, Is.EqualTo(alpha));
            Assert.That(group.interactable, Is.EqualTo(interactable));
            Assert.That(group.blocksRaycasts, Is.EqualTo(blocksRaycasts));
        }

        private GameObject Track(GameObject gameObject)
        {
            _objects.Add(gameObject);
            return gameObject;
        }
    }
}
