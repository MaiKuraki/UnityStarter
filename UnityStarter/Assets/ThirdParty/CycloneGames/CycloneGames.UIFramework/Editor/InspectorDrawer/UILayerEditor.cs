using UnityEditor;
using UnityEngine;
using CycloneGames.UIFramework;

namespace CycloneGames.UIFramework.Editor
{
    [CustomEditor(typeof(UILayer))]
    public class UILayerEditor : UnityEditor.Editor
    {
        private const string InValidPageName = "InvalidPageName";
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            // Get the target object 
            UILayer uiLayer = (UILayer)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Page Validation", EditorStyles.boldLabel);

            // Check if layer is initialized 
            if (!uiLayer.IsFinishedLayerInit)
            {
                EditorGUILayout.HelpBox("Layer not initialized!", MessageType.Warning);
                return;
            }

            // Get child count and page count 
            int childCount = uiLayer.transform.childCount;
            int pageCount = uiLayer.PageCount;

            // Display child count and page count 
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Child Count:", GUILayout.Width(100));
            EditorGUILayout.LabelField(childCount.ToString());
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Page Count:", GUILayout.Width(100));
            EditorGUILayout.LabelField(pageCount.ToString());
            EditorGUILayout.EndHorizontal();

            // Check if child count and page count match 
            bool isMatch = childCount == pageCount;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Status:", GUILayout.Width(100));
            EditorGUILayout.LabelField(
                isMatch ? "✅ All pages match" : "❌ Mismatch detected",
                new GUIStyle(EditorStyles.label)
                {
                    normal = { textColor = isMatch ? Color.green : Color.red }
                });
            EditorGUILayout.EndHorizontal();

            // Display warning if there's a mismatch
            if (!isMatch)
            {
                EditorGUILayout.HelpBox(
                    "Child count and page count don't match. Possible causes:\n" +
                    "1. Pages not properly registered in UILayer.UIPageArray\n" +
                    "2. Extra GameObjects in layer hierarchy",
                    MessageType.Warning);
            }

            // Display page list 
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Page List", EditorStyles.boldLabel);
            for (int i = 0; i < uiLayer.PageCount; i++)
            {
                var page = uiLayer.UIPageArray[i];
                bool pageIsChild = page != null && page.transform.parent == uiLayer.transform;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"Index: {i.ToString().PadLeft(3, ' ')}  |  {(page?.PageName ?? InValidPageName).PadRight(30, ' ')}\t Layer: {page.Priority.ToString().PadLeft(3, ' ')}");
                EditorGUILayout.LabelField(
                    pageIsChild ? "✅" : "❌",
                    GUILayout.Width(20));
                EditorGUILayout.EndHorizontal();
            }
        }
    }
}