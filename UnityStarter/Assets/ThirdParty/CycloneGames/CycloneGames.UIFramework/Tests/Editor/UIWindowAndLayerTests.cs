using System.Reflection;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using CycloneGames.UIFramework.Runtime;

namespace CycloneGames.UIFramework.Tests.Editor
{
    public sealed class UIWindowAndLayerTests
    {
        private GameObject _root;
        private readonly List<UIWindowConfiguration> _configs = new List<UIWindowConfiguration>(4);

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }

            for (int i = 0; i < _configs.Count; i++)
            {
                Object.DestroyImmediate(_configs[i]);
            }
            _configs.Clear();
        }

        [Test]
        public void UIWindow_SetVisible_UsesCanvasGroupWhenAvailable()
        {
            _root = new GameObject("Window");
            var canvasGroup = _root.AddComponent<CanvasGroup>();
            var window = _root.AddComponent<UIWindow>();
            InvokeAwake(window);

            window.SetVisible(false);

            Assert.AreEqual(0f, canvasGroup.alpha);
            Assert.IsFalse(canvasGroup.interactable);
            Assert.IsFalse(canvasGroup.blocksRaycasts);
            Assert.IsTrue(_root.activeSelf);

            window.SetVisible(true);

            Assert.AreEqual(1f, canvasGroup.alpha);
            Assert.IsTrue(canvasGroup.interactable);
            Assert.IsTrue(canvasGroup.blocksRaycasts);
        }

        [Test]
        public void UIWindow_SetWindowName_UpdatesLogicalNameAndGameObjectName()
        {
            _root = new GameObject("OriginalName");
            var window = _root.AddComponent<UIWindow>();

            window.SetWindowName("InventoryWindow");

            Assert.AreEqual("InventoryWindow", window.WindowName);
            Assert.AreEqual("InventoryWindow", _root.name);
        }

        [Test]
        public void UILayer_AddWindow_SortsByConfigurationPriorityAndSupportsDetach()
        {
            _root = CreateLayerRoot("Layer");
            var layer = _root.GetComponent<UILayer>();
            InvokeAwake(layer);

            UIWindow low = CreateWindow("Low", 10);
            UIWindow high = CreateWindow("High", 100);

            layer.AddWindow(high);
            layer.AddWindow(low);

            Assert.AreEqual(2, layer.WindowCount);
            Assert.AreSame(low, layer.UIWindowArray[0]);
            Assert.AreSame(high, layer.UIWindowArray[1]);
            Assert.AreEqual(0, low.transform.GetSiblingIndex());
            Assert.AreEqual(1, high.transform.GetSiblingIndex());

            layer.DetachWindow("Low");

            Assert.AreEqual(1, layer.WindowCount);
            Assert.IsNull(low.ParentLayer);
            Assert.AreSame(high, layer.UIWindowArray[0]);
        }

        private static GameObject CreateLayerRoot(string name)
        {
            var root = new GameObject(name);
            root.AddComponent<Canvas>();
            root.AddComponent<GraphicRaycaster>();
            root.AddComponent<UILayer>();
            return root;
        }

        private UIWindow CreateWindow(string name, int priority)
        {
            var go = new GameObject(name);
            var window = go.AddComponent<UIWindow>();
            window.SetWindowName(name);
            var config = ScriptableObject.CreateInstance<UIWindowConfiguration>();
            _configs.Add(config);
            SetPrivateField(config, "priority", priority);
            window.SetConfiguration(config);
            return window;
        }

        private static void InvokeAwake(UILayer layer)
        {
            MethodInfo awake = typeof(UILayer).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(awake);
            awake.Invoke(layer, null);
        }

        private static void InvokeAwake(UIWindow window)
        {
            MethodInfo awake = typeof(UIWindow).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(awake);
            awake.Invoke(window, null);
        }

        private static void SetPrivateField<T>(UIWindowConfiguration config, string fieldName, T value)
        {
            FieldInfo field = typeof(UIWindowConfiguration).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, fieldName);
            field.SetValue(config, value);
        }
    }
}
