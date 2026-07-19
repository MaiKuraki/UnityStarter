using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace CycloneGames.UIFramework.Runtime.Integrations.Localization.Editor
{
    /// <summary>
    /// Adds explicit authoring actions to common localized UI component menus.
    /// </summary>
    public static class LocalizeContextMenu
    {
        private const string TmpMenu = "CONTEXT/TMP_Text/Track Locale Layout";
        private const string ImageMenu = "CONTEXT/Image/Track Locale Layout";
        private const string RectMenu = "CONTEXT/RectTransform/Track Locale Layout";
        private const string LayoutGroupMenu = "CONTEXT/LayoutGroup/Track Locale Layout";

        [MenuItem(TmpMenu)]
        private static void TrackTmpText(MenuCommand command)
        {
            TMP_Text text = command.context as TMP_Text;
            if (text != null)
            {
                Track(text.rectTransform, text, text.GetComponent<LayoutGroup>());
            }
        }

        [MenuItem(ImageMenu)]
        private static void TrackImage(MenuCommand command)
        {
            Image image = command.context as Image;
            if (image != null)
            {
                Track(image.rectTransform, image.GetComponent<TMP_Text>(), image.GetComponent<LayoutGroup>());
            }
        }

        [MenuItem(RectMenu)]
        private static void TrackRectTransform(MenuCommand command)
        {
            RectTransform rect = command.context as RectTransform;
            if (rect != null)
            {
                Track(rect, rect.GetComponent<TMP_Text>(), rect.GetComponent<LayoutGroup>());
            }
        }

        [MenuItem(LayoutGroupMenu)]
        private static void TrackLayoutGroup(MenuCommand command)
        {
            LayoutGroup layoutGroup = command.context as LayoutGroup;
            if (layoutGroup != null)
            {
                RectTransform rect = layoutGroup.transform as RectTransform;
                Track(rect, layoutGroup.GetComponent<TMP_Text>(), layoutGroup);
            }
        }

        private static void Track(
            RectTransform rect,
            TMP_Text text,
            LayoutGroup layoutGroup)
        {
            if (rect == null)
            {
                Debug.LogWarning("[UI Locale Layout] The target must have a RectTransform.");
                return;
            }

            UILocaleLayout localeLayout = rect.GetComponentInParent<UILocaleLayout>(true);
            if (localeLayout == null)
            {
                GameObject root = FindLayoutRoot(rect);
                if (root == null)
                {
                    Debug.LogWarning("[UI Locale Layout] No valid UI layout root was found.");
                    return;
                }

                bool addComponent = EditorUtility.DisplayDialog(
                    "UI Locale Layout",
                    $"Add a UILocaleLayout component to '{root.name}' and track '{rect.name}'?",
                    "Add and Track",
                    "Cancel");
                if (!addComponent)
                {
                    return;
                }

                localeLayout = Undo.AddComponent<UILocaleLayout>(root);
            }

            SerializedObject serializedLayout = new SerializedObject(localeLayout);
            SerializedProperty elements = serializedLayout.FindProperty("_elements");
            SerializedProperty snapshots = serializedLayout.FindProperty("_snapshots");
            if (UILocaleLayoutEditor.HasUnsupportedFutureSchema(snapshots))
            {
                Debug.LogWarning(
                    $"[UI Locale Layout] '{localeLayout.name}' contains an unsupported future snapshot schema. " +
                    "Tracking changes are disabled until a compatible editor is available.",
                    localeLayout);
                Selection.activeObject = localeLayout;
                EditorGUIUtility.PingObject(localeLayout);
                return;
            }

            for (int i = 0; i < elements.arraySize; i++)
            {
                SerializedProperty tracked = elements.GetArrayElementAtIndex(i);
                if (tracked.FindPropertyRelative(nameof(TrackedElement.Target)).objectReferenceValue == rect)
                {
                    SerializedProperty trackedText = tracked.FindPropertyRelative(nameof(TrackedElement.Text));
                    SerializedProperty trackedLayoutGroup = tracked.FindPropertyRelative(nameof(TrackedElement.LayoutGroup));
                    bool changed = false;
                    bool conflictingReference = false;
                    TMP_Text completedText = null;
                    LayoutGroup completedLayoutGroup = null;

                    if (text != null)
                    {
                        if (trackedText.objectReferenceValue == null)
                        {
                            Undo.RecordObject(localeLayout, "Complete Locale Layout Element References");
                            trackedText.objectReferenceValue = text;
                            completedText = text;
                            changed = true;
                        }
                        else if (trackedText.objectReferenceValue != text)
                        {
                            conflictingReference = true;
                        }
                    }

                    if (layoutGroup != null)
                    {
                        if (trackedLayoutGroup.objectReferenceValue == null)
                        {
                            if (!changed)
                            {
                                Undo.RecordObject(localeLayout, "Complete Locale Layout Element References");
                            }
                            trackedLayoutGroup.objectReferenceValue = layoutGroup;
                            completedLayoutGroup = layoutGroup;
                            changed = true;
                        }
                        else if (trackedLayoutGroup.objectReferenceValue != layoutGroup)
                        {
                            conflictingReference = true;
                        }
                    }

                    if (changed)
                    {
                        CompleteCurrentSnapshotFields(snapshots, i, completedText, completedLayoutGroup);
                        serializedLayout.ApplyModifiedProperties();
                        PrefabUtility.RecordPrefabInstancePropertyModifications(localeLayout);
                        EditorUtility.SetDirty(localeLayout);
                        MarkSceneDirty(localeLayout.gameObject);
                    }

                    Selection.activeObject = localeLayout;
                    EditorGUIUtility.PingObject(localeLayout);
                    if (conflictingReference)
                    {
                        Debug.LogWarning(
                            $"[UI Locale Layout] '{rect.name}' is already tracked by '{localeLayout.name}'. " +
                            "Existing non-null component references were preserved.",
                            localeLayout);
                    }
                    else if (changed)
                    {
                        Debug.Log(
                            $"[UI Locale Layout] Completed component references for '{rect.name}' on '{localeLayout.name}'.",
                            localeLayout);
                    }
                    else
                    {
                        Debug.Log($"[UI Locale Layout] '{rect.name}' is already tracked by '{localeLayout.name}'.");
                    }
                    return;
                }
            }

            Undo.RecordObject(localeLayout, "Track Locale Layout Element");
            int index = elements.arraySize;
            elements.InsertArrayElementAtIndex(index);
            SerializedProperty element = elements.GetArrayElementAtIndex(index);
            element.FindPropertyRelative(nameof(TrackedElement.Target)).objectReferenceValue = rect;
            element.FindPropertyRelative(nameof(TrackedElement.Text)).objectReferenceValue = text;
            element.FindPropertyRelative(nameof(TrackedElement.LayoutGroup)).objectReferenceValue = layoutGroup;

            for (int snapshotIndex = 0; snapshotIndex < snapshots.arraySize; snapshotIndex++)
            {
                SerializedProperty localeSnapshot = snapshots.GetArrayElementAtIndex(snapshotIndex);
                int schemaVersion = localeSnapshot
                    .FindPropertyRelative(nameof(LocaleSnapshot.SchemaVersion))
                    .intValue;
                if (schemaVersion != LocaleSnapshot.CurrentSchemaVersion)
                {
                    continue;
                }

                SerializedProperty snapshotElements = localeSnapshot.FindPropertyRelative(nameof(LocaleSnapshot.Elements));
                snapshotElements.arraySize = elements.arraySize;
                UILocaleLayoutEditor.WriteSnapshot(
                    snapshotElements.GetArrayElementAtIndex(index),
                    default);
            }

            serializedLayout.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(localeLayout);
            EditorUtility.SetDirty(localeLayout);
            MarkSceneDirty(localeLayout.gameObject);

            Selection.activeObject = localeLayout;
            EditorGUIUtility.PingObject(localeLayout);
            Debug.Log($"[UI Locale Layout] Tracking '{rect.name}' on '{localeLayout.name}'.");
        }

        private static void CompleteCurrentSnapshotFields(
            SerializedProperty snapshots,
            int elementIndex,
            TMP_Text text,
            LayoutGroup layoutGroup)
        {
            for (int snapshotIndex = 0; snapshotIndex < snapshots.arraySize; snapshotIndex++)
            {
                SerializedProperty localeSnapshot = snapshots.GetArrayElementAtIndex(snapshotIndex);
                int schemaVersion = localeSnapshot
                    .FindPropertyRelative(nameof(LocaleSnapshot.SchemaVersion))
                    .intValue;
                if (schemaVersion != LocaleSnapshot.CurrentSchemaVersion)
                {
                    continue;
                }

                SerializedProperty snapshotElements = localeSnapshot.FindPropertyRelative(nameof(LocaleSnapshot.Elements));
                if (elementIndex >= snapshotElements.arraySize)
                {
                    continue;
                }

                SerializedProperty snapshot = snapshotElements.GetArrayElementAtIndex(elementIndex);
                if (text != null)
                {
                    snapshot.FindPropertyRelative(nameof(ElementSnapshot.FontSize)).floatValue = text.fontSize;
                    snapshot.FindPropertyRelative(nameof(ElementSnapshot.LineSpacing)).floatValue = text.lineSpacing;
                    snapshot.FindPropertyRelative(nameof(ElementSnapshot.CharacterSpacing)).floatValue = text.characterSpacing;
                    snapshot.FindPropertyRelative(nameof(ElementSnapshot.TextAlignment)).intValue = (int)text.alignment;
                    snapshot.FindPropertyRelative(nameof(ElementSnapshot.IsRightToLeftText)).boolValue = text.isRightToLeftText;
                }

                if (layoutGroup != null)
                {
                    snapshot.FindPropertyRelative(nameof(ElementSnapshot.ChildAlignment)).intValue =
                        (int)layoutGroup.childAlignment;
                }
            }
        }

        private static GameObject FindLayoutRoot(RectTransform start)
        {
            Transform current = start;
            while (current != null)
            {
                if (current.GetComponent<UIWindow>() != null)
                {
                    return current.gameObject;
                }

                current = current.parent;
            }

            Canvas rootCanvas = start.GetComponentInParent<Canvas>(true);
            return rootCanvas != null ? rootCanvas.rootCanvas.gameObject : start.root.gameObject;
        }

        private static void MarkSceneDirty(GameObject gameObject)
        {
            if (gameObject.scene.IsValid() && gameObject.scene.isLoaded)
            {
                EditorSceneManager.MarkSceneDirty(gameObject.scene);
            }
        }
    }
}
