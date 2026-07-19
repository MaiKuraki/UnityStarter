using System;
using System.Reflection;

using CycloneGames.Utility.Editor;
using CycloneGames.Utility.Runtime;

using NUnit.Framework;

using UnityEditor;
using UnityEngine;

namespace CycloneGames.Utility.Tests.Editor
{
    public sealed class UtilityInspectorTests
    {
        [Test]
        public void StringSelectorCache_FiltersInvalidConstantsAndUsesStableFirstValue()
        {
            MethodInfo getCache = typeof(StringAsConstSelectorDrawer).GetMethod(
                "GetAndCacheConstants",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(getCache, Is.Not.Null);

            object cache = getCache.Invoke(null, new object[] { typeof(TestConstants) });
            FieldInfo valueOptionsField = cache.GetType().GetField("ValueOptions", BindingFlags.Public | BindingFlags.Instance);
            Assert.That(valueOptionsField, Is.Not.Null);
            string[] values = (string[])valueOptionsField.GetValue(cache);

            Assert.That(values, Is.EqualTo(new[] { "same", "z" }));

            FieldInfo popupOptionsField = cache.GetType().GetField(
                "PopupDisplayOptions",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.That(popupOptionsField, Is.Not.Null);
            GUIContent[] popupOptions = (GUIContent[])popupOptionsField.GetValue(cache);
            Assert.That(popupOptions, Has.Length.EqualTo(3));
            Assert.That(popupOptions[0].text, Is.EqualTo("None"));
        }

        [Test]
        public void ExplicitComponentEditors_SupportMultiObjectCreation()
        {
            GameObject first = new GameObject("First", typeof(RectTransform));
            GameObject second = new GameObject("Second", typeof(RectTransform));
            UnityEditor.Editor fpsEditor = null;
            UnityEditor.Editor safeAreaEditor = null;
            UnityEditor.Editor registryEditor = null;
            try
            {
                FPSCounter firstFps = first.AddComponent<FPSCounter>();
                FPSCounter secondFps = second.AddComponent<FPSCounter>();
                AdaptiveSafeAreaFitter firstFitter = first.AddComponent<AdaptiveSafeAreaFitter>();
                AdaptiveSafeAreaFitter secondFitter = second.AddComponent<AdaptiveSafeAreaFitter>();
                TransformKeyRegistry firstRegistry = first.AddComponent<TransformKeyRegistry>();
                TransformKeyRegistry secondRegistry = second.AddComponent<TransformKeyRegistry>();

                fpsEditor = UnityEditor.Editor.CreateEditor(new UnityEngine.Object[] { firstFps, secondFps });
                safeAreaEditor = UnityEditor.Editor.CreateEditor(new UnityEngine.Object[] { firstFitter, secondFitter });
                registryEditor = UnityEditor.Editor.CreateEditor(new UnityEngine.Object[] { firstRegistry, secondRegistry });

                Assert.That(fpsEditor, Is.TypeOf<FPSCounterEditor>());
                Assert.That(safeAreaEditor, Is.TypeOf<AdaptiveSafeAreaFitterEditor>());
                Assert.That(registryEditor, Is.TypeOf<TransformKeyRegistryEditor>());
            }
            finally
            {
                if (fpsEditor != null)
                {
                    UnityEngine.Object.DestroyImmediate(fpsEditor);
                }
                if (safeAreaEditor != null)
                {
                    UnityEngine.Object.DestroyImmediate(safeAreaEditor);
                }
                if (registryEditor != null)
                {
                    UnityEngine.Object.DestroyImmediate(registryEditor);
                }
                UnityEngine.Object.DestroyImmediate(first);
                UnityEngine.Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void RestoredCompatibilityTypes_ArePresentAndLegacyEditorSplashTypeIsAbsent()
        {
            Assert.That(
                Type.GetType("CycloneGames.Utility.Editor.PropertyGroupInspectorDrawer, CycloneGames.Utility.Editor"),
                Is.Not.Null);
            Assert.That(
                Type.GetType("CycloneGames.Utility.Runtime.PropertyGroupAttribute, CycloneGames.Utility.Runtime"),
                Is.Not.Null);
            Assert.That(
                Type.GetType("CycloneGames.Utility.Runtime.EndPropertyGroupAttribute, CycloneGames.Utility.Runtime"),
                Is.Not.Null);
            Assert.That(
                Type.GetType("CycloneGames.Utility.Runtime.MonoSingleton`1, CycloneGames.Utility.Runtime"),
                Is.Not.Null);
            Assert.That(
                Type.GetType("CycloneGames.Utility.Runtime.Singleton`1, CycloneGames.Utility.Runtime"),
                Is.Not.Null);
            Assert.That(
                Type.GetType("CycloneGames.Utility.Runtime.SplashScreenModifier, CycloneGames.Utility.Runtime"),
                Is.Null);
        }

        private static class TestConstants
        {
            public const string Zulu = "z";
            public const string Alpha = "same";
            public const string Beta = "same";
            public const string Empty = "";
            public const string NullValue = null;
        }
    }
}
