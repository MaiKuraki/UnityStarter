using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using CycloneGames.GameplayTags.Runtime;
using UnityEditor.SceneManagement;

//  NOTE: This tool doesn't works well.

namespace CycloneGames.GameplayTags.Editor
{
    public class GameplayTagValidationReporter : EditorWindow
    {
        // Internal struct to hold information about an invalid tag reference.
        private struct InvalidTagEntry
        {
            public string AssetPath;
            public string TagName;
            public Object ContextObject; // The specific component or asset containing the tag.
            public string PropertyPath; // The path to the GameplayTagContainer within the object.

            public InvalidTagEntry(string assetPath, string tagName, Object contextObject, string propertyPath)
            {
                AssetPath = assetPath;
                TagName = tagName;
                ContextObject = contextObject;
                PropertyPath = propertyPath;
            }
        }

        private List<InvalidTagEntry> m_InvalidTags = new List<InvalidTagEntry>();
        private Vector2 m_ScrollPosition;

        //  NOTE: This tool doesn't works well. keep comment until we find a good way validate the tags required in GameObject(Prefab)/Scene/ScriptableObject.
        // [MenuItem("Tools/CycloneGames/GameplayTags/Tag Validation Window")]
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

            if (m_InvalidTags.Count == 0)
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
                    EditorGUILayout.LabelField($"Invalid Tag: ", EditorStyles.boldLabel);
                    EditorGUILayout.SelectableLabel(entry.TagName, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                    EditorGUILayout.LabelField($"Location: ", EditorStyles.boldLabel);
                    EditorGUILayout.SelectableLabel(entry.AssetPath, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                    EditorGUILayout.EndVertical();

                    if (GUILayout.Button("Fix", GUILayout.Width(50), GUILayout.Height(EditorGUIUtility.singleLineHeight * 2 + 5)))
                    {
                        FixSingleInvalidTag(entry, i);
                        // Since we modified the list, we should exit the loop for this frame
                        // to avoid issues with the collection being modified during iteration.
                        GUIUtility.ExitGUI();
                    }
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
            GameplayTagManager.InitializeIfNeeded(); // Ensure the tag dictionary is up-to-date

            // --- Part 1: Scan Project Assets (Prefabs and ScriptableObjects) ---
            string[] assetGuids = AssetDatabase.FindAssets("t:ScriptableObject t:GameObject");
            for (int i = 0; i < assetGuids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(assetGuids[i]);
                EditorUtility.DisplayProgressBar("Scanning Project Assets", $"Scanning: {assetPath}", (float)i / assetGuids.Length);

                Object assetObject = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                if (assetObject == null) continue;

                CheckObjectForInvalidTags(assetObject, assetPath);
            }

            // --- Part 2: Scan All Open Scenes ---
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                EditorUtility.DisplayProgressBar("Scanning Open Scenes", $"Scanning Scene: {scene.name}", (float)i / SceneManager.sceneCount);

                GameObject[] rootGameObjects = scene.GetRootGameObjects();
                foreach (GameObject rootGo in rootGameObjects)
                {
                    // For scene objects, the "asset path" is the scene's path.
                    CheckObjectForInvalidTags(rootGo, scene.path);
                }
            }

            EditorUtility.ClearProgressBar();
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
                        SerializedProperty tagsArrayProperty = property.FindPropertyRelative("m_SerializedExplicitTags");
                        if (tagsArrayProperty != null && tagsArrayProperty.isArray)
                        {
                            for (int i = 0; i < tagsArrayProperty.arraySize; i++)
                            {
                                SerializedProperty tagStringProperty = tagsArrayProperty.GetArrayElementAtIndex(i);
                                string tagName = tagStringProperty.stringValue;

                                if (!string.IsNullOrEmpty(tagName) && !GameplayTagManager.TryRequestTag(tagName, out _))
                                {
                                    m_InvalidTags.Add(new InvalidTagEntry(assetPath, tagName, serializedObject.targetObject, property.propertyPath));
                                }
                            }
                        }
                    }
                } while (property.NextVisible(false));
            }
        }

        private void FixSingleInvalidTag(InvalidTagEntry entryToFix, int indexInList)
        {
            if (!EditorUtility.DisplayDialog("Confirm Fix", $"Are you sure you want to remove the invalid tag '{entryToFix.TagName}' from object '{entryToFix.ContextObject.name}'? This action cannot be undone.", "Yes", "No"))
            {
                return;
            }

            SerializedObject serializedObject = new SerializedObject(entryToFix.ContextObject);
            // Find the specific GameplayTagContainer property using its stored path
            SerializedProperty containerProperty = serializedObject.FindProperty(entryToFix.PropertyPath);

            if (containerProperty == null)
            {
                EditorUtility.DisplayDialog("Fix Failed", $"Could not find the GameplayTagContainer property for asset '{entryToFix.AssetPath}'. It might have been fixed manually or the asset changed.", "OK");
                return;
            }

            SerializedProperty tagsArrayProperty = containerProperty.FindPropertyRelative("m_SerializedExplicitTags");

            if (tagsArrayProperty == null || !tagsArrayProperty.isArray) return;

            bool tagRemoved = false;
            for (int i = tagsArrayProperty.arraySize - 1; i >= 0; i--)
            {
                if (tagsArrayProperty.GetArrayElementAtIndex(i).stringValue == entryToFix.TagName)
                {
                    tagsArrayProperty.DeleteArrayElementAtIndex(i);
                    tagRemoved = true;
                }
            }

            if (tagRemoved)
            {
                serializedObject.ApplyModifiedProperties();

                // If it's a scene object, mark the scene as dirty so the user can save.
                if (!EditorUtility.IsPersistent(entryToFix.ContextObject))
                {
                    EditorSceneManager.MarkSceneDirty(((Component)entryToFix.ContextObject).gameObject.scene);
                }

                m_InvalidTags.RemoveAt(indexInList);
                Repaint();
                Debug.Log($"Successfully removed tag '{entryToFix.TagName}' from '{entryToFix.ContextObject.name}'. Please save your scene if the object was in the hierarchy.");
            }
            else
            {
                EditorUtility.DisplayDialog("Fix Failed", $"Could not find or remove tag '{entryToFix.TagName}' from '{entryToFix.ContextObject.name}'. It might have been fixed manually.", "OK");
            }
        }

        private void FixAllInvalidTags()
        {
            if (m_InvalidTags.Count == 0) return;

            if (!EditorUtility.DisplayDialog("Confirm Fix All", $"Are you sure you want to remove all {m_InvalidTags.Count} invalid GameplayTag references? This action cannot be undone.", "Yes", "No"))
            {
                return;
            }

            int fixedCount = 0;
            // Iterate backwards because FixSingleInvalidTag will remove items from the list
            for (int i = m_InvalidTags.Count - 1; i >= 0; i--)
            {
                FixSingleInvalidTag(m_InvalidTags[i], i);
                fixedCount++;
            }

            if (fixedCount > 0)
            {
                EditorUtility.DisplayDialog("Fix All Complete", $"Successfully processed {fixedCount} invalid tag references. Please review and save any modified scenes or assets.", "OK");
            }
        }
    }
}
