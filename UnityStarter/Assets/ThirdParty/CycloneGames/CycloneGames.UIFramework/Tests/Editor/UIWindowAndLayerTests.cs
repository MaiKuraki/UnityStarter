using System.Collections.Generic;
using CycloneGames.UIFramework.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace CycloneGames.UIFramework.Tests.Editor
{
    public sealed class UIWindowAndLayerTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>(8);
        private readonly List<UIWindowConfiguration> _configurations =
            new List<UIWindowConfiguration>(8);

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
        public void UIWindow_SetVisible_UsesCanvasGroupWithoutDisablingGameObject()
        {
            GameObject windowObject = Track(new GameObject(
                "Window",
                typeof(RectTransform),
                typeof(CanvasGroup)));
            CanvasGroup canvasGroup = windowObject.GetComponent<CanvasGroup>();
            UIWindow window = windowObject.AddComponent<UIWindow>();
            TestReflection.Invoke(window, "Awake");

            window.SetVisible(false);

            Assert.AreEqual(0f, canvasGroup.alpha);
            Assert.IsFalse(canvasGroup.interactable);
            Assert.IsFalse(canvasGroup.blocksRaycasts);
            Assert.IsTrue(windowObject.activeSelf);

            window.SetVisible(true);
            Assert.AreEqual(1f, canvasGroup.alpha);
            Assert.IsTrue(canvasGroup.interactable);
            Assert.IsTrue(canvasGroup.blocksRaycasts);
        }

        [Test]
        public void UILayer_AuthoringSerializesOnlyStableLayerIdentity()
        {
            UILayer layer = CreateLayer();
            var serializedLayer = new SerializedObject(layer);
            var propertyNames = new List<string>(2);
            SerializedProperty property = serializedLayer.GetIterator();
            bool enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                propertyNames.Add(property.name);
                enterChildren = false;
            }

            CollectionAssert.AreEqual(
                new[] { "m_Script", "layerName" },
                propertyNames);
        }

        [Test]
        public void UILayer_AttachMaintainsStablePriorityAndSiblingOrder()
        {
            UILayer layer = CreateLayer();
            UIWindow high = CreateWindow("High", 100);
            UIWindow equalFirst = CreateWindow("EqualFirst", 20);
            UIWindow low = CreateWindow("Low", 10);
            UIWindow equalSecond = CreateWindow("EqualSecond", 20);

            Attach(layer, high);
            Attach(layer, equalFirst);
            Attach(layer, low);
            Attach(layer, equalSecond);

            var windows = new List<UIWindow>(8);
            layer.CopyWindows(windows);
            CollectionAssert.AreEqual(
                new[] { low, equalFirst, equalSecond, high },
                windows);
            for (int i = 0; i < windows.Count; i++)
            {
                Assert.AreEqual(i, windows[i].transform.GetSiblingIndex());
                Assert.AreSame(layer, windows[i].ParentLayer);
            }
        }

        [Test]
        public void UILayer_DetachRemovesOnlyOwnedWindowAndIsIdempotent()
        {
            UILayer layer = CreateLayer();
            UIWindow first = CreateWindow("First", 0);
            UIWindow second = CreateWindow("Second", 1);
            Attach(layer, first);
            Attach(layer, second);

            Assert.IsTrue(Detach(layer, first));
            Assert.IsFalse(Detach(layer, first));

            Assert.AreEqual(1, layer.WindowCount);
            Assert.IsNull(first.ParentLayer);
            Assert.IsTrue(layer.TryGetWindow("Second", out UIWindow found));
            Assert.AreSame(second, found);
            Assert.IsFalse(layer.TryGetWindow("First", out _));
        }

        [Test]
        public void UILayer_CopyWindowsClearsAndReusesCallerBuffer()
        {
            UILayer layer = CreateLayer();
            UIWindow first = CreateWindow("First", 0);
            UIWindow second = CreateWindow("Second", 1);
            Attach(layer, first);
            Attach(layer, second);
            var destination = new List<UIWindow>(8) { null };
            int capacity = destination.Capacity;

            layer.CopyWindows(destination);
            layer.CopyWindows(destination);

            Assert.AreEqual(capacity, destination.Capacity);
            CollectionAssert.AreEqual(new[] { first, second }, destination);
        }

        [Test]
        public void UILayer_AttachingSameInstanceTwiceDoesNotDuplicateIt()
        {
            UILayer layer = CreateLayer();
            UIWindow window = CreateWindow("Inventory", 10);

            Attach(layer, window);
            Attach(layer, window);

            Assert.AreEqual(1, layer.WindowCount);
            var destination = new List<UIWindow>(1);
            layer.CopyWindows(destination);
            CollectionAssert.AreEqual(new[] { window }, destination);
        }

        private UILayer CreateLayer()
        {
            GameObject layerObject = Track(new GameObject(
                "Layer",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(GraphicRaycaster)));
            UILayer layer = layerObject.AddComponent<UILayer>();
            TestReflection.SetField(layer, "layerName", "Main");
            return layer;
        }

        private UIWindow CreateWindow(string windowId, int priority)
        {
            GameObject windowObject = Track(new GameObject(windowId, typeof(RectTransform)));
            UIWindow window = windowObject.AddComponent<UIWindow>();
            UIWindowConfiguration configuration =
                ScriptableObject.CreateInstance<UIWindowConfiguration>();
            _configurations.Add(configuration);
            TestReflection.SetField(configuration, "priority", priority);
            TestReflection.SetField(window, "_windowId", windowId);
            TestReflection.SetField(window, "_configuration", configuration);
            return window;
        }

        private GameObject Track(GameObject gameObject)
        {
            _objects.Add(gameObject);
            return gameObject;
        }

        private static void Attach(UILayer layer, UIWindow window)
        {
            TestReflection.Invoke(layer, "Attach", window);
        }

        private static bool Detach(UILayer layer, UIWindow window)
        {
            return (bool)TestReflection.Invoke(layer, "Detach", window);
        }
    }
}
