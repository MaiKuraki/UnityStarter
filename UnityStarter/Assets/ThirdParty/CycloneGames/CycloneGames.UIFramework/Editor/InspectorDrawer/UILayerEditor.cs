#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using CycloneGames.UIFramework;

namespace CycloneGames.UIFramework.Editor
{
    [CustomEditor(typeof(CycloneGames.UIFramework.UILayer))]
    public class UILayerEditor : UnityEditor.Editor
    {
        private const string InValidPageName = "InvalidPageName";
        
        private GUIStyle _statusStyleGreen;
        private GUIStyle _statusStyleRed;
        private GUIStyle _labelStyleDefault; // For the check/cross mark icons

        private void OnEnable()
        {
            // Cache GUIStyles to avoid creating them every OnInspectorGUI call (GC and performance)
            _statusStyleGreen = new GUIStyle(EditorStyles.label) { normal = { textColor = Color.green } };
            _statusStyleRed = new GUIStyle(EditorStyles.label) { normal = { textColor = Color.red } };
            _labelStyleDefault = new GUIStyle(EditorStyles.label); 
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            UILayer uiLayer = (UILayer)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Page Validation", EditorStyles.boldLabel);

            if (!uiLayer.IsFinishedLayerInit)
            {
                EditorGUILayout.HelpBox("Layer not initialized!", MessageType.Warning);
                return;
            }

            int childCount = uiLayer.transform.childCount;
            int pageCount = uiLayer.WindowCount;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Child Count:", GUILayout.Width(100));
            EditorGUILayout.LabelField(childCount.ToString());
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Page Count:", GUILayout.Width(100));
            EditorGUILayout.LabelField(pageCount.ToString());
            EditorGUILayout.EndHorizontal();

            bool isMatch = childCount == pageCount;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Status:", GUILayout.Width(100));
            EditorGUILayout.LabelField(
                isMatch ? "✅ All pages match" : "❌ Mismatch detected",
                isMatch ? _statusStyleGreen : _statusStyleRed);
            EditorGUILayout.EndHorizontal();

            if (!isMatch)
            {
                EditorGUILayout.HelpBox(
                    "Child count and page count don't match. Possible causes:\n" +
                    "1. Pages not properly registered in UILayer.UIWindowArray (or UIWindowArray is out of sync with actual children).\n" +
                    "2. Extra GameObjects in layer hierarchy that are not UIWindows.\n" +
                    "3. UIWindows were destroyed but not properly unregistered from the UILayer.",
                    MessageType.Warning);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Page List (from UIWindowArray)", EditorStyles.boldLabel);
            if (uiLayer.UIWindowArray != null)
            {
                for (int i = 0; i < uiLayer.WindowCount; i++) // Iterate up to WindowCount
                {
                    var page = uiLayer.UIWindowArray[i]; // Accessing the cached array
                    bool pageIsChild = page != null && page.transform.parent == uiLayer.transform;
                    bool pageIsActive = page != null && page.gameObject.activeInHierarchy;

                    EditorGUILayout.BeginHorizontal();
                    // Using string.Format or StringBuilder would be more GC friendly for complex strings,
                    // but for editor GUI, current interpolation is often acceptable.
                    string pageInfo = $"Index: {i.ToString().PadLeft(3, ' ')} | Name: {(page?.WindowName ?? InValidPageName).PadRight(30, ' ')} | Priority: {(page != null ? page.Priority.ToString() : "N/A").PadLeft(3, ' ')}";
                    EditorGUILayout.LabelField(pageInfo);
                    
                    string statusIcon = pageIsChild ? (pageIsActive ? "✅" : "☑️ (Inactive)") : "❌ (Not Child)";
                    EditorGUILayout.LabelField(statusIcon, _labelStyleDefault, GUILayout.Width(100)); // Increased width for text
                    
                    // Allow to ping the object in hierarchy
                    if (page != null && GUILayout.Button("Select", GUILayout.Width(60)))
                    {
                        EditorGUIUtility.PingObject(page.gameObject);
                        Selection.activeGameObject = page.gameObject;
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }
            else
            {
                EditorGUILayout.LabelField("UIWindowArray is null.");
            }
        }
    }
}
#endif