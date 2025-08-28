using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CycloneGames.GameplayTags.Runtime; // Add this line

namespace CycloneGames.GameplayTags.Editor
{
    public class GameplayTagValidationReporter : EditorWindow
    {
        private struct InvalidTagEntry
        {
            public string AssetPath;
            public string TagName;
            public Object AssetObject; // Reference to the actual asset for selection
            public SerializedProperty TagContainerProperty; // Reference to the SerializedProperty for the container

            public InvalidTagEntry(string assetPath, string tagName, Object assetObject, SerializedProperty tagContainerProperty)
            {
                AssetPath = assetPath;
                TagName = tagName;
                AssetObject = assetObject;
                TagContainerProperty = tagContainerProperty.Copy(); // Copy the property to avoid issues with iterator
            }
        }

        private List<InvalidTagEntry> m_InvalidTags = new List<InvalidTagEntry>();
        private Vector2 m_ScrollPosition;

        [MenuItem("Tools/CycloneGames/GameplayTags/Tag Validation Window")]
        public static void ShowWindow()
        {
            GetWindow<GameplayTagValidationReporter>("GameplayTag Validation");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("GameplayTag Validation Tool", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            if (GUILayout.Button("Scan for Invalid Tags"))
            {
                ScanForInvalidTags();
            }

            EditorGUILayout.Space();

            if (m_InvalidTags.Count == 0)
            {
                EditorGUILayout.HelpBox("No invalid GameplayTags found in project.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox($"{m_InvalidTags.Count} invalid GameplayTags found. Please review and fix.", MessageType.Warning);

                m_ScrollPosition = EditorGUILayout.BeginScrollView(m_ScrollPosition);
                for (int i = 0; i < m_InvalidTags.Count; i++)
                {
                    var entry = m_InvalidTags[i];
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.ObjectField(entry.AssetObject, typeof(Object), false);
                    EditorGUILayout.SelectableLabel(entry.TagName, GUILayout.ExpandWidth(true));
                    if (GUILayout.Button("Fix", GUILayout.Width(50)))
                    {
                        FixSingleInvalidTag(entry, i);
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

        private void ScanForInvalidTags()
        {
            m_InvalidTags.Clear();
            EditorUtility.DisplayProgressBar("Scanning Assets", "Initializing scan...", 0f);

            string[] assetGuids = AssetDatabase.FindAssets("t:ScriptableObject t:GameObject"); // Scan for ScriptableObjects and GameObjects (prefabs)
            for (int i = 0; i < assetGuids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(assetGuids[i]);
                EditorUtility.DisplayProgressBar("Scanning Assets", $"Scanning: {assetPath}", (float)i / assetGuids.Length);

                Object assetObject = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                if (assetObject == null) continue;

                // For GameObjects (prefabs), we need to check its components
                if (assetObject is GameObject gameObject)
                {
                    MonoBehaviour[] components = gameObject.GetComponentsInChildren<MonoBehaviour>(true);
                    foreach (MonoBehaviour component in components)
                    {
                        if (component == null) continue;
                        CheckObjectForInvalidTags(component, assetPath);
                    }
                }
                // For ScriptableObjects
                else if (assetObject is ScriptableObject scriptableObject)
                {
                    CheckObjectForInvalidTags(scriptableObject, assetPath);
                }
            }

            EditorUtility.ClearProgressBar();
            Repaint(); // Refresh the window to show results
        }

        private void CheckObjectForInvalidTags(Object obj, string assetPath)
        {
            if (obj == null) return;

            SerializedObject serializedObject = new SerializedObject(obj);
            SerializedProperty property = serializedObject.GetIterator();

            if (property.NextVisible(true))
            {
                do
                {
                    if (property.propertyType == SerializedPropertyType.Generic && property.type == "GameplayTagContainer")
                    {
                        SerializedProperty serializedExplicitTagsProperty = property.FindPropertyRelative("m_SerializedExplicitTags");
                        if (serializedExplicitTagsProperty != null && serializedExplicitTagsProperty.isArray)
                        {
                            for (int i = 0; i < serializedExplicitTagsProperty.arraySize; i++)
                            {
                                SerializedProperty tagStringProperty = serializedExplicitTagsProperty.GetArrayElementAtIndex(i);
                                string tagName = tagStringProperty.stringValue;

                                if (!string.IsNullOrEmpty(tagName) && !GameplayTagManager.TryRequestTag(tagName, out _))
                                {
                                    m_InvalidTags.Add(new InvalidTagEntry(assetPath, tagName, obj, serializedExplicitTagsProperty));
                                }
                            }
                        }
                    }
                } while (property.NextVisible(false));
            }
        }

        private void FixSingleInvalidTag(InvalidTagEntry entryToFix, int indexInList)
        {
            if (!EditorUtility.DisplayDialog("Confirm Fix", $"Are you sure you want to remove the invalid tag '{entryToFix.TagName}' from asset '{entryToFix.AssetPath}'? This action cannot be undone.", "Yes", "No"))
            {
                return;
            }

            EditorUtility.DisplayProgressBar("Fixing Invalid Tag", $"Processing: {entryToFix.AssetPath}", 0f);

            SerializedObject serializedObject = new SerializedObject(entryToFix.AssetObject);
            SerializedProperty serializedExplicitTagsProperty = entryToFix.TagContainerProperty; // Use the copied property

            // Re-find the property in the current SerializedObject context
            // This is crucial because the copied property might be from an old SerializedObject
            SerializedProperty currentProperty = serializedObject.GetIterator();
            bool foundContainer = false;
            if (currentProperty.NextVisible(true))
            {
                do
                {
                    if (currentProperty.propertyType == SerializedPropertyType.Generic && currentProperty.type == "GameplayTagContainer")
                    {
                        // Check if this is the correct GameplayTagContainer by comparing its path
                        if (currentProperty.propertyPath == serializedExplicitTagsProperty.propertyPath)
                        {
                            serializedExplicitTagsProperty = currentProperty.FindPropertyRelative("m_SerializedExplicitTags");
                            foundContainer = true;
                            break;
                        }
                    }
                } while (currentProperty.NextVisible(false));
            }

            if (!foundContainer || serializedExplicitTagsProperty == null || !serializedExplicitTagsProperty.isArray)
            {
                EditorUtility.DisplayDialog("Fix Failed", $"Could not find the GameplayTagContainer property for asset '{entryToFix.AssetPath}'. It might have been fixed manually or the asset changed.", "OK");
                EditorUtility.ClearProgressBar();
                return;
            }

            bool tagRemoved = false;
            for (int i = serializedExplicitTagsProperty.arraySize - 1; i >= 0; i--)
            {
                SerializedProperty tagStringProperty = serializedExplicitTagsProperty.GetArrayElementAtIndex(i);
                string tagName = tagStringProperty.stringValue;

                if (tagName == entryToFix.TagName)
                {
                    serializedExplicitTagsProperty.DeleteArrayElementAtIndex(i);
                    tagRemoved = true;
                    break;
                }
            }

            if (tagRemoved)
            {
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(entryToFix.AssetObject);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                m_InvalidTags.RemoveAt(indexInList); // Remove from the list in the window
                Repaint();
                EditorUtility.DisplayDialog("Fix Complete", $"Successfully removed tag '{entryToFix.TagName}' from '{entryToFix.AssetPath}'.", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Fix Failed", $"Could not find or remove tag '{entryToFix.TagName}' from '{entryToFix.AssetPath}'. It might have been fixed manually or the asset changed.", "OK");
            }

            EditorUtility.ClearProgressBar();
        }

        private void FixAllInvalidTags()
        {
            if (m_InvalidTags.Count == 0) return;

            if (!EditorUtility.DisplayDialog("Confirm Fix", $"Are you sure you want to remove all {m_InvalidTags.Count} invalid GameplayTags from the detected assets? This action cannot be undone.", "Yes", "No"))
            {
                return;
            }

            EditorUtility.DisplayProgressBar("Fixing Invalid Tags", "Initializing fix...", 0f);

            // Group invalid tags by asset path to avoid loading/saving the same asset multiple times
            Dictionary<Object, List<string>> tagsToFixByAsset = new Dictionary<Object, List<string>>();
            foreach (var entry in m_InvalidTags)
            {
                if (!tagsToFixByAsset.ContainsKey(entry.AssetObject))
                {
                    tagsToFixByAsset[entry.AssetObject] = new List<string>();
                }
                tagsToFixByAsset[entry.AssetObject].Add(entry.TagName);
            }

            int totalTagsToFix = m_InvalidTags.Count;
            int fixedCount = 0;
            int assetProcessedCount = 0;

            foreach (var kvp in tagsToFixByAsset)
            {
                Object assetObject = kvp.Key;
                List<string> tagsToRemove = kvp.Value;
                string assetPath = AssetDatabase.GetAssetPath(assetObject);

                EditorUtility.DisplayProgressBar("Fixing Invalid Tags", $"Processing: {assetPath}", (float)assetProcessedCount / tagsToFixByAsset.Count);

                SerializedObject serializedObject = new SerializedObject(assetObject);
                SerializedProperty property = serializedObject.GetIterator();

                bool assetModified = false;
                if (property.NextVisible(true))
                {
                    do
                    {
                        if (property.propertyType == SerializedPropertyType.Generic && property.type == "GameplayTagContainer")
                        {
                            SerializedProperty serializedExplicitTagsProperty = property.FindPropertyRelative("m_SerializedExplicitTags");
                            if (serializedExplicitTagsProperty != null && serializedExplicitTagsProperty.isArray)
                            {
                                for (int i = serializedExplicitTagsProperty.arraySize - 1; i >= 0; i--)
                                {
                                    SerializedProperty tagStringProperty = serializedExplicitTagsProperty.GetArrayElementAtIndex(i);
                                    string tagName = tagStringProperty.stringValue;

                                    if (tagsToRemove.Contains(tagName))
                                    {
                                        serializedExplicitTagsProperty.DeleteArrayElementAtIndex(i);
                                        fixedCount++;
                                        assetModified = true;
                                    }
                                }
                            }
                        }
                    } while (property.NextVisible(false));
                }

                if (assetModified)
                {
                    serializedObject.ApplyModifiedProperties();
                    EditorUtility.SetDirty(assetObject);
                }
                assetProcessedCount++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.ClearProgressBar();
            m_InvalidTags.Clear();
            Repaint();
            EditorUtility.DisplayDialog("Fix Complete", $"Successfully removed {fixedCount} invalid GameplayTags from {tagsToFixByAsset.Count} assets.", "OK");
        }
    }
}
