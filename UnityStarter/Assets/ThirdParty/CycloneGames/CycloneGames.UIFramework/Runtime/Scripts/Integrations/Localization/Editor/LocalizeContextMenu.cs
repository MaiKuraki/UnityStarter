#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace CycloneGames.UIFramework.Runtime.Integrations.Localization.Editor
{
    /// <summary>
    /// Adds "Track Layout" context menu item to TMP_Text and Image components.
    /// Finds or creates a <see cref="UILocaleLayout"/> on the nearest ancestor and
    /// registers the selected component as a tracked element.
    /// </summary>
    public static class LocalizeContextMenu
    {
        [MenuItem("CONTEXT/TMP_Text/Track Layout (UILocaleLayout)")]
        private static void LocalizeTMP(MenuCommand cmd)
        {
            var tmp = cmd.context as TMP_Text;
            if (tmp == null) return;
            AddToLayout(tmp.gameObject, tmp);
        }

        [MenuItem("CONTEXT/Image/Track Layout (UILocaleLayout)")]
        private static void LocalizeImage(MenuCommand cmd)
        {
            var img = cmd.context as Image;
            if (img == null) return;
            AddToLayout(img.gameObject, null);
        }

        [MenuItem("CONTEXT/RectTransform/Track Layout (UILocaleLayout)")]
        private static void LocalizeRect(MenuCommand cmd)
        {
            var rt = cmd.context as RectTransform;
            if (rt == null) return;
            var tmp = rt.GetComponent<TMP_Text>();
            AddToLayout(rt.gameObject, tmp);
        }

        private static void AddToLayout(GameObject go, TMP_Text tmp)
        {
            var rt = go.GetComponent<RectTransform>();
            if (rt == null)
            {
                Debug.LogWarning("[Localize] Target must have a RectTransform.");
                return;
            }

            // Walk up to find existing UILocaleLayout
            var layout = go.GetComponentInParent<UILocaleLayout>(true);

            if (layout == null)
            {
                // Find UIWindow root or use topmost canvas
                var root = FindLayoutRoot(go.transform);
                if (root == null)
                {
                    Debug.LogWarning("[Localize] No suitable root found for UILocaleLayout.");
                    return;
                }

                if (!EditorUtility.DisplayDialog("Localize",
                    $"No UILocaleLayout found in hierarchy.\nAdd one to '{root.name}'?",
                    "Add", "Cancel"))
                    return;

                Undo.RecordObject(root, "Add UILocaleLayout");
                layout = Undo.AddComponent<UILocaleLayout>(root);
            }

            // Check if already tracked
            var so = new SerializedObject(layout);
            var elementsProp = so.FindProperty("_elements");

            for (int i = 0; i < elementsProp.arraySize; i++)
            {
                if (elementsProp.GetArrayElementAtIndex(i)
                    .FindPropertyRelative("Target").objectReferenceValue == rt)
                {
                    // Already tracked — just select the layout
                    Selection.activeGameObject = layout.gameObject;
                    EditorGUIUtility.PingObject(layout);
                    Debug.Log($"[Localize] '{go.name}' is already tracked by UILocaleLayout on '{layout.name}'.");
                    return;
                }
            }

            // Add element
            Undo.RecordObject(layout, "Add Localized Element");
            int idx = elementsProp.arraySize;
            elementsProp.InsertArrayElementAtIndex(idx);
            var entry = elementsProp.GetArrayElementAtIndex(idx);
            entry.FindPropertyRelative("Target").objectReferenceValue = rt;
            entry.FindPropertyRelative("Text").objectReferenceValue = tmp;

            // Also extend all existing snapshots
            var snapshotsProp = so.FindProperty("_snapshots");
            for (int s = 0; s < snapshotsProp.arraySize; s++)
            {
                var elems = snapshotsProp.GetArrayElementAtIndex(s).FindPropertyRelative("Elements");
                elems.InsertArrayElementAtIndex(elems.arraySize);
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(layout);

            // Select and ping the layout component
            Selection.activeGameObject = layout.gameObject;
            EditorGUIUtility.PingObject(layout);
            Debug.Log($"[Localize] Added '{go.name}' to UILocaleLayout on '{layout.name}'.");
        }

        private static GameObject FindLayoutRoot(Transform start)
        {
            // Prefer a UIWindow if present in hierarchy
            var current = start;
            while (current != null)
            {
                if (current.GetComponent<UIWindow>() != null)
                    return current.gameObject;
                current = current.parent;
            }

            // Fallback: topmost Canvas
            current = start;
            Canvas topCanvas = null;
            while (current != null)
            {
                var canvas = current.GetComponent<Canvas>();
                if (canvas != null) topCanvas = canvas;
                current = current.parent;
            }

            return topCanvas != null ? topCanvas.gameObject : start.root.gameObject;
        }
    }
}
#endif
