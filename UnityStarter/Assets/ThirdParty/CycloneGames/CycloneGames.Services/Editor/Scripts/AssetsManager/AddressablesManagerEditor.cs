using UnityEngine;
using UnityEditor;
using UnityEngine.ResourceManagement.AsyncOperations;
using System.Collections.Concurrent;
using System.Linq;

namespace CycloneGames.Service.Editor
{
    [CustomEditor(typeof(AddressablesManager))]
    public class AddressablesManagerEditor : UnityEditor.Editor
    {
        private AddressablesManager addressablesManager;
        private Vector2 scrollPosition;

        private void OnEnable()
        {
            addressablesManager = (AddressablesManager)target;
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            ShowActiveHandles();
        }

        private void ShowActiveHandles()
        {
            var activeHandlesField = typeof(AddressablesManager).GetField("activeHandles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var activeHandles = (ConcurrentDictionary<string, AsyncOperationHandle>)activeHandlesField.GetValue(addressablesManager);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Active Addressables Handles", EditorStyles.boldLabel);

            if (activeHandles.IsEmpty)
            {
                EditorGUILayout.LabelField("No active handles.");
                return;
            }

            var groupedHandles = activeHandles
                .Where(kvp => kvp.Value.IsValid())
                .GroupBy(kvp => GetDirectory(kvp.Key))
                .OrderBy(g => g.Key)
                .ToList();

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(600));

            foreach (var group in groupedHandles)
            {
                Rect rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                rect.height += 4;

                EditorGUI.DrawRect(rect, new Color(0.1f, 0.3f, 0.6f, 0.2f));

                EditorGUI.LabelField(rect, $"Directory: {group.Key}", EditorStyles.boldLabel);

                foreach (var kvp in group)
                {
                    string key = kvp.Key;
                    AsyncOperationHandle handle = kvp.Value;

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(new GUIContent(key, key), GUILayout.ExpandWidth(true));
                    int referenceCount = GetReferenceCount(key);
                    EditorGUILayout.LabelField($"Reference Count: {referenceCount}", GUILayout.Width(150));
                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private string GetDirectory(string key)
        {
            int lastSlashIndex = key.LastIndexOf('/');
            return lastSlashIndex >= 0 ? key.Substring(0, lastSlashIndex) : key;
        }

        private int GetReferenceCount(string key)
        {
            //  TODO: To be implemented
            return 1;
        }
    }
}