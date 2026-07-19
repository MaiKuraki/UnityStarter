using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using CycloneGames.UIFramework.Runtime.Integrations.Localization;
using CycloneGames.UIFramework.Runtime.Integrations.Localization.Editor;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CycloneGames.UIFramework.Tests.Editor
{
    public sealed class LocalizationEditorTests
    {
        private readonly List<UnityEngine.Object> _ownedObjects = new List<UnityEngine.Object>(16);

        [TearDown]
        public void TearDown()
        {
            if (AnimationMode.InAnimationMode())
            {
                AnimationMode.StopAnimationMode();
            }

            Selection.activeObject = null;
            for (int i = _ownedObjects.Count - 1; i >= 0; i--)
            {
                UnityEngine.Object owned = _ownedObjects[i];
                if (owned != null)
                {
                    UnityEngine.Object.DestroyImmediate(owned);
                }
            }

            _ownedObjects.Clear();
        }

        [Test]
        public void Preview_UsesAnimationModeAndStopRestoresHierarchyWithoutUndo()
        {
            UILocaleLayout layout = CreateLayout(out RectTransform rect, out TMP_Text text, out LayoutGroup group);
            Vector2 basePosition = rect.anchoredPosition;
            float baseFontSize = text.fontSize;
            ElementSnapshot snapshot = CreateSnapshot(new Vector2(120f, 48f), 37f);
            ConfigureLayout(layout, rect, text, group, LocaleSnapshot.CurrentSchemaVersion, snapshot);

            UnityEditor.Editor editor = CreateEditor(layout);
            int undoGroup = Undo.GetCurrentGroup();

            InvokeInstance(editor, "EnterPreview");

            Assert.That(AnimationMode.InAnimationMode(), Is.True);
            Assert.That(rect.anchoredPosition, Is.EqualTo(snapshot.AnchoredPosition));
            Assert.That(text.fontSize, Is.EqualTo(snapshot.FontSize));
            Assert.That(Undo.GetCurrentGroup(), Is.EqualTo(undoGroup));

            AnimationMode.StopAnimationMode();
            Assert.That(rect.anchoredPosition, Is.EqualTo(basePosition));
            Assert.That(text.fontSize, Is.EqualTo(baseFontSize));

            InvokeInstance(editor, "ExitPreview");
            Assert.That(AnimationMode.InAnimationMode(), Is.False);
        }

        [Test]
        public void Preview_RefusesToTakeOwnershipOfExternalAnimationMode()
        {
            UILocaleLayout layout = CreateLayout(out RectTransform rect, out TMP_Text text, out LayoutGroup group);
            Vector2 basePosition = rect.anchoredPosition;
            ConfigureLayout(
                layout,
                rect,
                text,
                group,
                LocaleSnapshot.CurrentSchemaVersion,
                CreateSnapshot(new Vector2(10f, 20f), 31f));
            UnityEditor.Editor editor = CreateEditor(layout);

            AnimationMode.StartAnimationMode();
            LogAssert.Expect(
                LogType.Warning,
                new Regex("Another Unity animation preview is active", RegexOptions.CultureInvariant));

            InvokeInstance(editor, "EnterPreview");

            Assert.That(AnimationMode.InAnimationMode(), Is.True);
            Assert.That(rect.anchoredPosition, Is.EqualTo(basePosition));
        }

        [Test]
        public void UndoDuringPreview_StopsPreviewAndRestoresHierarchy()
        {
            UILocaleLayout layout = CreateLayout(out RectTransform rect, out TMP_Text text, out LayoutGroup group);
            Vector2 basePosition = rect.anchoredPosition;
            ConfigureLayout(
                layout,
                rect,
                text,
                group,
                LocaleSnapshot.CurrentSchemaVersion,
                CreateSnapshot(new Vector2(90f, 45f), 29f));
            UnityEditor.Editor editor = CreateEditor(layout);

            Undo.RecordObject(layout, "Localization Editor Undo Probe");
            SerializedObject serializedLayout = new SerializedObject(layout);
            serializedLayout.FindProperty("_baseLocale").stringValue = "fr";
            serializedLayout.ApplyModifiedProperties();

            InvokeInstance(editor, "EnterPreview");
            Assert.That(AnimationMode.InAnimationMode(), Is.True);

            Undo.PerformUndo();

            Assert.That(AnimationMode.InAnimationMode(), Is.False);
            Assert.That(rect.anchoredPosition, Is.EqualTo(basePosition));
        }

        [Test]
        public void FutureSchema_FailsClosedForInspectorAndContextTracking()
        {
            UILocaleLayout layout = CreateLayout(out RectTransform rect, out TMP_Text text, out LayoutGroup group);
            ConfigureLayout(
                layout,
                rect,
                null,
                null,
                LocaleSnapshot.CurrentSchemaVersion + 1,
                CreateSnapshot(new Vector2(14f, 28f), 33f));
            UnityEditor.Editor editor = CreateEditor(layout);

            LogAssert.Expect(
                LogType.Warning,
                new Regex("unsupported future snapshot schema", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
            InvokeInstance(editor, "AddEmptyTrackedElement");
            Assert.That(ReadElements(layout).arraySize, Is.EqualTo(1));

            LogAssert.Expect(
                LogType.Warning,
                new Regex("unsupported future snapshot schema", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
            InvokeInstance(editor, "NormalizeSnapshotsWithConfirmation");
            SerializedObject futureData = new SerializedObject(layout);
            Assert.That(
                futureData.FindProperty("_snapshots")
                    .GetArrayElementAtIndex(0)
                    .FindPropertyRelative(nameof(LocaleSnapshot.SchemaVersion))
                    .intValue,
                Is.EqualTo(LocaleSnapshot.CurrentSchemaVersion + 1));

            LogAssert.Expect(
                LogType.Warning,
                new Regex("unsupported future snapshot schema", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
            InvokeContextTrack(rect, text, group);

            SerializedProperty tracked = ReadElements(layout).GetArrayElementAtIndex(0);
            Assert.That(
                tracked.FindPropertyRelative(nameof(TrackedElement.Text)).objectReferenceValue,
                Is.Null);
            Assert.That(
                tracked.FindPropertyRelative(nameof(TrackedElement.LayoutGroup)).objectReferenceValue,
                Is.Null);
        }

        [Test]
        public void ContextTracking_CompletesMissingReferencesAndPreservesExistingConflicts()
        {
            UILocaleLayout layout = CreateLayout(out RectTransform rect, out TMP_Text text, out LayoutGroup group);
            TMP_Text conflictingText = CreateTextSibling(layout.transform, "ConflictingText");
            ElementSnapshot snapshot = CreateSnapshot(new Vector2(12f, 24f), 77f);
            ConfigureLayout(
                layout,
                rect,
                conflictingText,
                null,
                LocaleSnapshot.CurrentSchemaVersion,
                snapshot);

            LogAssert.Expect(
                LogType.Warning,
                new Regex("Existing non-null component references were preserved", RegexOptions.CultureInvariant));
            InvokeContextTrack(rect, text, group);

            SerializedObject serializedLayout = new SerializedObject(layout);
            SerializedProperty tracked = serializedLayout
                .FindProperty("_elements")
                .GetArrayElementAtIndex(0);
            Assert.That(
                tracked.FindPropertyRelative(nameof(TrackedElement.Text)).objectReferenceValue,
                Is.SameAs(conflictingText));
            Assert.That(
                tracked.FindPropertyRelative(nameof(TrackedElement.LayoutGroup)).objectReferenceValue,
                Is.SameAs(group));

            SerializedProperty saved = serializedLayout
                .FindProperty("_snapshots")
                .GetArrayElementAtIndex(0)
                .FindPropertyRelative(nameof(LocaleSnapshot.Elements))
                .GetArrayElementAtIndex(0);
            Assert.That(
                saved.FindPropertyRelative(nameof(ElementSnapshot.FontSize)).floatValue,
                Is.EqualTo(snapshot.FontSize));
            Assert.That(
                saved.FindPropertyRelative(nameof(ElementSnapshot.ChildAlignment)).intValue,
                Is.EqualTo((int)group.childAlignment));
        }

        [Test]
        public void IncompleteCurrentSnapshot_CannotApplyOrPreview()
        {
            UILocaleLayout layout = CreateLayout(out RectTransform rect, out TMP_Text text, out LayoutGroup group);
            Vector2 basePosition = rect.anchoredPosition;
            ElementSnapshot incomplete = CreateSnapshot(new Vector2(200f, 100f), 42f);
            incomplete.HasValue = false;
            ConfigureLayout(
                layout,
                rect,
                text,
                group,
                LocaleSnapshot.CurrentSchemaVersion,
                incomplete);
            UnityEditor.Editor editor = CreateEditor(layout);

            LogAssert.Expect(LogType.Warning, new Regex("incomplete", RegexOptions.IgnoreCase));
            InvokeInstance(editor, "ApplySelectedSnapshotForEditing");
            LogAssert.Expect(LogType.Warning, new Regex("incomplete", RegexOptions.IgnoreCase));
            InvokeInstance(editor, "EnterPreview");

            Assert.That(rect.anchoredPosition, Is.EqualTo(basePosition));
            Assert.That(AnimationMode.InAnimationMode(), Is.False);
        }

        [Test]
        public void NonFiniteCurrentSnapshot_CannotPreview()
        {
            UILocaleLayout layout = CreateLayout(out RectTransform rect, out TMP_Text text, out LayoutGroup group);
            Vector2 basePosition = rect.anchoredPosition;
            ElementSnapshot invalid = CreateSnapshot(new Vector2(float.NaN, 100f), 42f);
            ConfigureLayout(
                layout,
                rect,
                text,
                group,
                LocaleSnapshot.CurrentSchemaVersion,
                invalid);
            UnityEditor.Editor editor = CreateEditor(layout);

            LogAssert.Expect(LogType.Warning, new Regex("non-finite", RegexOptions.IgnoreCase));
            InvokeInstance(editor, "EnterPreview");

            Assert.That(rect.anchoredPosition, Is.EqualTo(basePosition));
            Assert.That(AnimationMode.InAnimationMode(), Is.False);
        }

        [Test]
        public void PureRectImageSnapshot_CanApplyWithoutTextOrLayoutAlignmentData()
        {
            UILocaleLayout layout = CreateLayout(
                out RectTransform rect,
                out TMP_Text text,
                out LayoutGroup group);
            UnityEngine.Object.DestroyImmediate(text);
            UnityEngine.Object.DestroyImmediate(group);
            rect.gameObject.AddComponent<Image>();

            ElementSnapshot snapshot = ElementSnapshot.Capture(rect, null, null);
            snapshot.AnchoredPosition = new Vector2(180f, 72f);
            ConfigureLayout(
                layout,
                rect,
                null,
                null,
                LocaleSnapshot.CurrentSchemaVersion,
                snapshot);
            UnityEditor.Editor editor = CreateEditor(layout);

            InvokeInstance(editor, "ApplySelectedSnapshotForEditing");

            Assert.That(rect.anchoredPosition, Is.EqualTo(snapshot.AnchoredPosition));
        }

        [Test]
        public void Validation_ReportsCompetingLayoutAndAnimationAuthorities()
        {
            UILocaleLayout layout = CreateLayout(out RectTransform rect, out TMP_Text text, out LayoutGroup group);
            layout.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.gameObject.AddComponent<Animator>();
            ContentSizeFitter contentSizeFitter = rect.gameObject.AddComponent<ContentSizeFitter>();
            contentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            AspectRatioFitter aspectRatioFitter = rect.gameObject.AddComponent<AspectRatioFitter>();
            aspectRatioFitter.aspectMode = AspectRatioFitter.AspectMode.WidthControlsHeight;
            text.enableAutoSizing = true;
            ConfigureLayout(
                layout,
                rect,
                text,
                group,
                LocaleSnapshot.CurrentSchemaVersion,
                CreateSnapshot(new Vector2(20f, 40f), 30f));
            UnityEditor.Editor editor = CreateEditor(layout);

            InvokeInstance(editor, "RefreshValidation");
            FieldInfo issuesField = typeof(UILocaleLayoutEditor).GetField(
                "_issues",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(issuesField, Is.Not.Null);
            List<string> issues = (List<string>)issuesField.GetValue(editor);
            string combined = string.Join("\n", issues);

            StringAssert.Contains("parent LayoutGroup", combined);
            StringAssert.Contains("ContentSizeFitter", combined);
            StringAssert.Contains("AspectRatioFitter", combined);
            StringAssert.Contains("Animator", combined);
            StringAssert.Contains("TMP auto-size", combined);
        }

        private UILocaleLayout CreateLayout(
            out RectTransform rect,
            out TMP_Text text,
            out LayoutGroup group)
        {
            GameObject root = new GameObject("LocaleLayout", typeof(RectTransform));
            _ownedObjects.Add(root);
            UILocaleLayout layout = root.AddComponent<UILocaleLayout>();

            GameObject child = new GameObject("Tracked", typeof(RectTransform));
            child.transform.SetParent(root.transform, false);
            rect = child.GetComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(3f, 6f);
            text = child.AddComponent<TextMeshProUGUI>();
            text.fontSize = 18f;
            group = child.AddComponent<HorizontalLayoutGroup>();
            group.childAlignment = TextAnchor.MiddleCenter;
            return layout;
        }

        private TMP_Text CreateTextSibling(Transform parent, string name)
        {
            GameObject sibling = new GameObject(name, typeof(RectTransform));
            sibling.transform.SetParent(parent, false);
            TMP_Text text = sibling.AddComponent<TextMeshProUGUI>();
            text.fontSize = 25f;
            return text;
        }

        private UnityEditor.Editor CreateEditor(UILocaleLayout layout)
        {
            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(
                layout,
                typeof(UILocaleLayoutEditor));
            _ownedObjects.Add(editor);
            return editor;
        }

        private static void ConfigureLayout(
            UILocaleLayout layout,
            RectTransform rect,
            TMP_Text text,
            LayoutGroup group,
            int schemaVersion,
            in ElementSnapshot snapshot)
        {
            SerializedObject serializedLayout = new SerializedObject(layout);
            serializedLayout.FindProperty("_baseLocale").stringValue = "en";

            SerializedProperty elements = serializedLayout.FindProperty("_elements");
            elements.arraySize = 1;
            SerializedProperty tracked = elements.GetArrayElementAtIndex(0);
            tracked.FindPropertyRelative(nameof(TrackedElement.Target)).objectReferenceValue = rect;
            tracked.FindPropertyRelative(nameof(TrackedElement.Text)).objectReferenceValue = text;
            tracked.FindPropertyRelative(nameof(TrackedElement.LayoutGroup)).objectReferenceValue = group;

            SerializedProperty snapshots = serializedLayout.FindProperty("_snapshots");
            snapshots.arraySize = 1;
            SerializedProperty locale = snapshots.GetArrayElementAtIndex(0);
            locale.FindPropertyRelative(nameof(LocaleSnapshot.LocaleCode)).stringValue = "ja";
            locale.FindPropertyRelative(nameof(LocaleSnapshot.SchemaVersion)).intValue = schemaVersion;
            SerializedProperty values = locale.FindPropertyRelative(nameof(LocaleSnapshot.Elements));
            values.arraySize = 1;
            WriteSnapshot(values.GetArrayElementAtIndex(0), snapshot);
            serializedLayout.ApplyModifiedPropertiesWithoutUndo();
        }

        private static ElementSnapshot CreateSnapshot(Vector2 anchoredPosition, float fontSize)
        {
            return new ElementSnapshot
            {
                AnchoredPosition = anchoredPosition,
                SizeDelta = new Vector2(320f, 160f),
                AnchorMin = new Vector2(0.1f, 0.2f),
                AnchorMax = new Vector2(0.8f, 0.9f),
                Pivot = new Vector2(0.5f, 0.5f),
                LocalScale = Vector3.one,
                FontSize = fontSize,
                LineSpacing = 1.5f,
                CharacterSpacing = 2.5f,
                TextAlignment = TextAlignmentOptions.Center,
                IsRightToLeftText = false,
                ChildAlignment = TextAnchor.LowerRight,
                HasValue = true
            };
        }

        private static void WriteSnapshot(SerializedProperty property, in ElementSnapshot snapshot)
        {
            property.FindPropertyRelative(nameof(ElementSnapshot.FontSize)).floatValue = snapshot.FontSize;
            property.FindPropertyRelative(nameof(ElementSnapshot.LineSpacing)).floatValue = snapshot.LineSpacing;
            property.FindPropertyRelative(nameof(ElementSnapshot.CharacterSpacing)).floatValue = snapshot.CharacterSpacing;
            property.FindPropertyRelative(nameof(ElementSnapshot.AnchoredPosition)).vector2Value = snapshot.AnchoredPosition;
            property.FindPropertyRelative(nameof(ElementSnapshot.SizeDelta)).vector2Value = snapshot.SizeDelta;
            property.FindPropertyRelative(nameof(ElementSnapshot.AnchorMin)).vector2Value = snapshot.AnchorMin;
            property.FindPropertyRelative(nameof(ElementSnapshot.AnchorMax)).vector2Value = snapshot.AnchorMax;
            property.FindPropertyRelative(nameof(ElementSnapshot.Pivot)).vector2Value = snapshot.Pivot;
            property.FindPropertyRelative(nameof(ElementSnapshot.LocalScale)).vector3Value = snapshot.LocalScale;
            property.FindPropertyRelative(nameof(ElementSnapshot.TextAlignment)).intValue = (int)snapshot.TextAlignment;
            property.FindPropertyRelative(nameof(ElementSnapshot.IsRightToLeftText)).boolValue = snapshot.IsRightToLeftText;
            property.FindPropertyRelative(nameof(ElementSnapshot.ChildAlignment)).intValue = (int)snapshot.ChildAlignment;
            property.FindPropertyRelative(nameof(ElementSnapshot.HasValue)).boolValue = snapshot.HasValue;
        }

        private static SerializedProperty ReadElements(UILocaleLayout layout)
        {
            SerializedObject serializedLayout = new SerializedObject(layout);
            return serializedLayout.FindProperty("_elements");
        }

        private static void InvokeContextTrack(
            RectTransform rect,
            TMP_Text text,
            LayoutGroup group)
        {
            MethodInfo method = typeof(LocalizeContextMenu).GetMethod(
                "Track",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            method.Invoke(null, new object[] { rect, text, group });
        }

        private static void InvokeInstance(UnityEditor.Editor editor, string methodName)
        {
            MethodInfo method = typeof(UILocaleLayoutEditor).GetMethod(
                methodName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(method, Is.Not.Null);
            method.Invoke(editor, null);
        }
    }
}
