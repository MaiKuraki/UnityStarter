using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayTags.Unity.Editor
{
    public class GameplayTagValidationReporter : EditorWindow
    {
        private enum InvalidTagReferenceKind
        {
            SingleTag,
            ContainerTag
        }

        // Internal struct to hold information about an invalid tag reference.
        private struct InvalidTagEntry
        {
            public string AssetPath;
            public string TagName;
            public Object ContextObject; // The specific component or asset containing the tag.
            public string PropertyPath; // The path to the serialized GameplayTag or GameplayTagContainer.
            public InvalidTagReferenceKind Kind;

            public InvalidTagEntry(string assetPath, string tagName, Object contextObject, string propertyPath, InvalidTagReferenceKind kind)
            {
                AssetPath = assetPath;
                TagName = tagName;
                ContextObject = contextObject;
                PropertyPath = propertyPath;
                Kind = kind;
            }
        }

        private List<InvalidTagEntry> m_InvalidTags = new List<InvalidTagEntry>();
        private Vector2 m_ScrollPosition;
        private bool m_HasScanned;

        [MenuItem("Tools/CycloneGames/GameplayTags/Tag Validation Window")]
        public static void ShowWindow()
        {
            GetWindow<GameplayTagValidationReporter>("GameplayTag Validation");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("GameplayTag Validation Tool", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("This tool scans all project assets (Prefabs, ScriptableObjects) AND all objects in currently open scenes for invalid GameplayTag references.", MessageType.Info);
            EditorGUILayout.Space();

            if (GUILayout.Button("Scan Project and Open Scenes for Invalid Tags"))
            {
                ScanForInvalidTags();
            }

            EditorGUILayout.Space();

            if (!m_HasScanned)
            {
                EditorGUILayout.HelpBox("Run a scan to find invalid GameplayTag and GameplayTagContainer references.", MessageType.None);
            }
            else if (m_InvalidTags.Count == 0)
            {
                EditorGUILayout.HelpBox("Scan complete. No invalid GameplayTags found.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox($"{m_InvalidTags.Count} invalid GameplayTag reference(s) found. Please review and fix.", MessageType.Warning);

                m_ScrollPosition = EditorGUILayout.BeginScrollView(m_ScrollPosition);
                for (int i = m_InvalidTags.Count - 1; i >= 0; i--) // Iterate backwards for safe removal
                {
                    var entry = m_InvalidTags[i];
                    EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

                    // Display the context object (the component or SO) which is clickable
                    EditorGUILayout.ObjectField(entry.ContextObject, typeof(Object), true, GUILayout.Width(150));

                    EditorGUILayout.BeginVertical();
                    EditorGUILayout.LabelField("Invalid Tag: ", EditorStyles.boldLabel);
                    EditorGUILayout.SelectableLabel(entry.TagName, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                    EditorGUILayout.LabelField("Reference Type: ", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(entry.Kind == InvalidTagReferenceKind.SingleTag ? "GameplayTag" : "GameplayTagContainer");
                    EditorGUILayout.LabelField("Location: ", EditorStyles.boldLabel);
                    EditorGUILayout.SelectableLabel(entry.AssetPath, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                    EditorGUILayout.EndVertical();

                    EditorGUILayout.BeginVertical(GUILayout.Width(58));
                    if (GUILayout.Button("Ping", GUILayout.Width(54)))
                    {
                        Selection.activeObject = entry.ContextObject;
                        EditorGUIUtility.PingObject(entry.ContextObject);
                    }

                    if (GUILayout.Button("Fix", GUILayout.Width(50), GUILayout.Height(EditorGUIUtility.singleLineHeight * 2 + 5)))
                    {
                        FixSingleInvalidTag(entry, i);
                        // Since we modified the list, we should exit the loop for this frame
                        // to avoid issues with the collection being modified during iteration.
                        GUIUtility.ExitGUI();
                    }
                    EditorGUILayout.EndVertical();
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndScrollView();

                EditorGUILayout.Space();
                if (GUILayout.Button("Fix All Invalid Tags"))
                {
                    FixAllInvalidTags();
                }
            }
        }

        /// <summary>
        /// Scans both project assets and all open scenes for invalid tags.
        /// </summary>
        private void ScanForInvalidTags()
        {
            m_InvalidTags.Clear();
            m_HasScanned = false;
            GameplayTagManager.InitializeIfNeeded(); // Ensure the tag dictionary is up-to-date

            // --- Part 1: Scan Project Assets (Prefabs and ScriptableObjects) ---
            HashSet<string> assetGuids = new HashSet<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:ScriptableObject"))
            {
                assetGuids.Add(guid);
            }

            foreach (string guid in AssetDatabase.FindAssets("t:GameObject"))
            {
                assetGuids.Add(guid);
            }

            // Suppress "The referenced script (Unknown) on this Behaviour is missing!" warnings
            // that Unity emits when loading prefabs with missing script references.
            var previousFilterLogType = Debug.unityLogger.filterLogType;
            Debug.unityLogger.filterLogType = LogType.Error;
            try
            {
                int assetIndex = 0;
                foreach (string assetGuid in assetGuids)
                {
                    assetIndex++;
                    string assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
                    if (EditorUtility.DisplayCancelableProgressBar("Scanning Project Assets", $"Scanning: {assetPath}", (float)assetIndex / assetGuids.Count))
                    {
                        break;
                    }

                    Object assetObject = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                    if (assetObject == null) continue;

                    CheckObjectForInvalidTags(assetObject, assetPath);
                }
            }
            finally
            {
                Debug.unityLogger.filterLogType = previousFilterLogType;
            }

            // --- Part 2: Scan All Open Scenes ---
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                if (EditorUtility.DisplayCancelableProgressBar("Scanning Open Scenes", $"Scanning Scene: {scene.name}", (float)i / SceneManager.sceneCount))
                {
                    break;
                }

                GameObject[] rootGameObjects = scene.GetRootGameObjects();
                foreach (GameObject rootGo in rootGameObjects)
                {
                    // For scene objects, the "asset path" is the scene's path.
                    CheckObjectForInvalidTags(rootGo, scene.path);
                }
            }

            EditorUtility.ClearProgressBar();
            m_HasScanned = true;
            Repaint(); // Refresh the window to show results
        }

        private void CheckObjectForInvalidTags(Object obj, string assetPath)
        {
            if (obj is GameObject go)
            {
                MonoBehaviour[] components = go.GetComponentsInChildren<MonoBehaviour>(true);
                foreach (MonoBehaviour component in components)
                {
                    if (component == null) continue;
                    ProcessSerializedObject(new SerializedObject(component), assetPath);
                }
            }
            else if (obj is ScriptableObject scriptableObject)
            {
                ProcessSerializedObject(new SerializedObject(scriptableObject), assetPath);
            }
        }

        private void ProcessSerializedObject(SerializedObject serializedObject, string assetPath)
        {
            SerializedProperty property = serializedObject.GetIterator();
            if (property.NextVisible(true))
            {
                do
                {
                    if (property.propertyType == SerializedPropertyType.Generic && property.type == "GameplayTagContainer")
                    {
                        ProcessTagContainerProperty(serializedObject, property, assetPath);
                    }
                    else if (property.propertyType == SerializedPropertyType.Generic && property.type == "GameplayTag")
                    {
                        ProcessSingleTagProperty(serializedObject, property, assetPath);
                    }
                } while (property.NextVisible(true));
            }
        }

        private void ProcessSingleTagProperty(SerializedObject serializedObject, SerializedProperty property, string assetPath)
        {
            SerializedProperty nameProperty = property.FindPropertyRelative("m_Name");
            if (nameProperty == null)
            {
                return;
            }

            string tagName = nameProperty.stringValue;
            if (!string.IsNullOrEmpty(tagName) && !GameplayTagManager.TryRequestTag(tagName, out _))
            {
                m_InvalidTags.Add(new InvalidTagEntry(assetPath, tagName, serializedObject.targetObject, property.propertyPath, InvalidTagReferenceKind.SingleTag));
            }
        }

        private void ProcessTagContainerProperty(SerializedObject serializedObject, SerializedProperty property, string assetPath)
        {
            SerializedProperty tagsArrayProperty = property.FindPropertyRelative("m_SerializedExplicitTags");
            if (tagsArrayProperty == null || !tagsArrayProperty.isArray)
            {
                return;
            }

            for (int i = 0; i < tagsArrayProperty.arraySize; i++)
            {
                SerializedProperty tagStringProperty = tagsArrayProperty.GetArrayElementAtIndex(i);
                string tagName = tagStringProperty.stringValue;

                if (!string.IsNullOrEmpty(tagName) && !GameplayTagManager.TryRequestTag(tagName, out _))
                {
                    m_InvalidTags.Add(new InvalidTagEntry(assetPath, tagName, serializedObject.targetObject, property.propertyPath, InvalidTagReferenceKind.ContainerTag));
                }
            }
        }

        private void FixSingleInvalidTag(InvalidTagEntry entryToFix, int indexInList)
        {
            if (!EditorUtility.DisplayDialog("Confirm Fix", $"Are you sure you want to remove the invalid tag '{entryToFix.TagName}' from object '{entryToFix.ContextObject.name}'? This action can be undone.", "Yes", "No"))
            {
                return;
            }

            if (TryFixEntry(entryToFix))
            {
                m_InvalidTags.RemoveAt(indexInList);
                Repaint();
                return;
            }

            EditorUtility.DisplayDialog("Fix Failed", $"Could not find or remove tag '{entryToFix.TagName}' from '{entryToFix.ContextObject.name}'. It might have been fixed manually.", "OK");
        }

        private void FixAllInvalidTags()
        {
            if (m_InvalidTags.Count == 0) return;

            if (!EditorUtility.DisplayDialog("Confirm Fix All", $"Are you sure you want to remove all {m_InvalidTags.Count} invalid GameplayTag references? This action can be undone.", "Yes", "No"))
            {
                return;
            }

            int fixedCount = 0;
            for (int i = m_InvalidTags.Count - 1; i >= 0; i--)
            {
                var entry = m_InvalidTags[i];
                if (TryFixEntry(entry))
                {
                    m_InvalidTags.RemoveAt(i);
                    fixedCount++;
                }
            }

            if (fixedCount > 0)
            {
                Debug.Log($"Successfully removed {fixedCount} invalid GameplayTag references. Please save modified scenes/assets.");
            }

            Repaint();
        }

        private bool TryFixEntry(InvalidTagEntry entry)
        {
            SerializedObject serializedObject = new SerializedObject(entry.ContextObject);
            SerializedProperty property = serializedObject.FindProperty(entry.PropertyPath);
            if (property == null)
            {
                return false;
            }

            serializedObject.Update();
            Undo.RecordObject(entry.ContextObject, "Remove Invalid Gameplay Tag");

            bool modified = entry.Kind == InvalidTagReferenceKind.SingleTag
                ? TryFixSingleTagProperty(property, entry.TagName)
                : TryFixTagContainerProperty(property, entry.TagName);

            if (!modified)
            {
                return false;
            }

            serializedObject.ApplyModifiedProperties();
            MarkContextDirty(entry.ContextObject);
            return true;
        }

        private static bool TryFixSingleTagProperty(SerializedProperty tagProperty, string tagName)
        {
            SerializedProperty nameProperty = tagProperty.FindPropertyRelative("m_Name");
            if (nameProperty == null || nameProperty.stringValue != tagName)
            {
                return false;
            }

            nameProperty.stringValue = null;
            return true;
        }

        private static bool TryFixTagContainerProperty(SerializedProperty containerProperty, string tagName)
        {
            SerializedProperty tagsArrayProperty = containerProperty.FindPropertyRelative("m_SerializedExplicitTags");
            if (tagsArrayProperty == null || !tagsArrayProperty.isArray)
            {
                return false;
            }

            bool tagRemoved = false;
            for (int i = tagsArrayProperty.arraySize - 1; i >= 0; i--)
            {
                if (tagsArrayProperty.GetArrayElementAtIndex(i).stringValue == tagName)
                {
                    tagsArrayProperty.DeleteArrayElementAtIndex(i);
                    tagRemoved = true;
                }
            }

            return tagRemoved;
        }

        private static void MarkContextDirty(Object contextObject)
        {
            EditorUtility.SetDirty(contextObject);

            if (EditorUtility.IsPersistent(contextObject))
            {
                return;
            }

            if (contextObject is Component component)
            {
                EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
            }
            else if (contextObject is GameObject gameObject)
            {
                EditorSceneManager.MarkSceneDirty(gameObject.scene);
            }
        }
    }
}
