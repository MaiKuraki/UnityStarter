using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayTags.Unity.Editor
{
    internal enum GameplayTagValidationScanStatus
    {
        NotRun,
        Completed,
        Canceled,
        Failed
    }

    public class GameplayTagValidationReporter : EditorWindow
    {
        internal const int MaxInvalidTagEntries = 4096;
        private const int EntriesPerPage = 50;

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
            public bool CanFix;

            public InvalidTagEntry(string assetPath, string tagName, Object contextObject, string propertyPath, InvalidTagReferenceKind kind, bool canFix)
            {
                AssetPath = assetPath;
                TagName = tagName;
                ContextObject = contextObject;
                PropertyPath = propertyPath;
                Kind = kind;
                CanFix = canFix;
            }
        }

        private readonly List<InvalidTagEntry> m_InvalidTags = new List<InvalidTagEntry>(128);
        private Vector2 m_ScrollPosition;
        private int m_ResultPage;
        private GameplayTagValidationScanStatus m_ScanStatus;
        private string m_ScanFailureMessage;

        internal GameplayTagValidationScanStatus ScanStatus => m_ScanStatus;
        internal int InvalidTagCount => m_InvalidTags.Count;

        internal static bool IsCleanScanResult(GameplayTagValidationScanStatus status, int invalidTagCount)
        {
            return status == GameplayTagValidationScanStatus.Completed && invalidTagCount == 0;
        }

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

            if (m_ScanStatus == GameplayTagValidationScanStatus.NotRun)
            {
                EditorGUILayout.HelpBox("Run a scan to find invalid GameplayTag and GameplayTagContainer references.", MessageType.None);
            }
            else if (m_ScanStatus == GameplayTagValidationScanStatus.Canceled)
            {
                EditorGUILayout.HelpBox("Scan canceled. Results are partial and must not be used as a clean validation result.", MessageType.Warning);
            }
            else if (m_ScanStatus == GameplayTagValidationScanStatus.Failed)
            {
                EditorGUILayout.HelpBox($"Scan failed. Results are partial. {m_ScanFailureMessage}", MessageType.Error);
            }
            else if (IsCleanScanResult(m_ScanStatus, m_InvalidTags.Count))
            {
                EditorGUILayout.HelpBox("Scan complete. No invalid GameplayTags found.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox($"{m_InvalidTags.Count} invalid GameplayTag reference(s) found. Please review and fix.", MessageType.Warning);

                int pageCount = (m_InvalidTags.Count + EntriesPerPage - 1) / EntriesPerPage;
                m_ResultPage = Mathf.Clamp(m_ResultPage, 0, pageCount - 1);
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginDisabledGroup(m_ResultPage == 0);
                if (GUILayout.Button("Previous", GUILayout.Width(80)))
                    m_ResultPage--;
                EditorGUI.EndDisabledGroup();
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField($"Page {m_ResultPage + 1} / {pageCount}", GUILayout.Width(100));
                GUILayout.FlexibleSpace();
                EditorGUI.BeginDisabledGroup(m_ResultPage >= pageCount - 1);
                if (GUILayout.Button("Next", GUILayout.Width(80)))
                    m_ResultPage++;
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.EndHorizontal();

                int firstEntry = m_ResultPage * EntriesPerPage;
                int lastEntryExclusive = Mathf.Min(firstEntry + EntriesPerPage, m_InvalidTags.Count);

                m_ScrollPosition = EditorGUILayout.BeginScrollView(m_ScrollPosition);
                for (int i = lastEntryExclusive - 1; i >= firstEntry; i--)
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

                    EditorGUI.BeginDisabledGroup(!entry.CanFix);
                    if (GUILayout.Button(entry.CanFix ? "Fix" : "Read-only", GUILayout.Width(58), GUILayout.Height(EditorGUIUtility.singleLineHeight * 2 + 5)))
                    {
                        FixSingleInvalidTag(entry, i);
                        // Since we modified the list, we should exit the loop for this frame
                        // to avoid issues with the collection being modified during iteration.
                        GUIUtility.ExitGUI();
                    }
                    EditorGUI.EndDisabledGroup();
                    EditorGUILayout.EndVertical();
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndScrollView();

                EditorGUILayout.Space();
                EditorGUI.BeginDisabledGroup(!HasFixableEntries());
                if (GUILayout.Button("Fix All Writable Invalid Tags"))
                {
                    FixAllInvalidTags();
                }
                EditorGUI.EndDisabledGroup();
            }
        }

        /// <summary>
        /// Scans both project assets and all open scenes for invalid tags.
        /// </summary>
        private void ScanForInvalidTags()
        {
            m_InvalidTags.Clear();
            m_ResultPage = 0;
            m_ScanStatus = GameplayTagValidationScanStatus.NotRun;
            m_ScanFailureMessage = null;
            var previousFilterLogType = Debug.unityLogger.filterLogType;
            try
            {
                GameplayTagManager.InitializeIfNeeded();
                Debug.unityLogger.filterLogType = LogType.Error;
                if (!ScanProjectAssets() || !ScanOpenScenes())
                    m_ScanStatus = GameplayTagValidationScanStatus.Canceled;
                else
                    m_ScanStatus = GameplayTagValidationScanStatus.Completed;
            }
            catch (System.Exception exception)
            {
                m_ScanStatus = GameplayTagValidationScanStatus.Failed;
                m_ScanFailureMessage = exception.Message;
                Debug.LogException(exception);
            }
            finally
            {
                Debug.unityLogger.filterLogType = previousFilterLogType;
                EditorUtility.ClearProgressBar();
                Repaint();
            }
        }

        private bool ScanProjectAssets()
        {
            HashSet<string> uniqueGuids = new HashSet<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:ScriptableObject"))
                uniqueGuids.Add(guid);
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
                uniqueGuids.Add(guid);

            List<string> assetGuids = new List<string>(uniqueGuids);
            assetGuids.Sort(System.StringComparer.Ordinal);
            HashSet<int> scannedInstanceIds = new HashSet<int>();
            for (int assetIndex = 0; assetIndex < assetGuids.Count; assetIndex++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(assetGuids[assetIndex]);
                if (EditorUtility.DisplayCancelableProgressBar("Scanning Project Assets", $"Scanning: {assetPath}", (float)(assetIndex + 1) / assetGuids.Count))
                    return false;

                bool canFix = IsWritableProjectAsset(assetPath);
                scannedInstanceIds.Clear();
                ScanProjectAsset(assetPath, canFix, scannedInstanceIds);
            }
            return true;
        }

        internal void ScanProjectAsset(string assetPath, bool canFix)
        {
            ScanProjectAsset(assetPath, canFix, new HashSet<int>());
        }

        private void ScanProjectAsset(string assetPath, bool canFix, HashSet<int> scannedInstanceIds)
        {
            Object mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);

            if (mainAsset is GameObject mainPrefab && scannedInstanceIds.Add(mainPrefab.GetInstanceID()))
                CheckObjectForInvalidTags(mainPrefab, assetPath, canFix);

            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is ScriptableObject scriptableObject &&
                    scannedInstanceIds.Add(scriptableObject.GetInstanceID()))
                {
                    CheckObjectForInvalidTags(scriptableObject, assetPath, canFix);
                }
            }
        }

        private bool ScanOpenScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                if (EditorUtility.DisplayCancelableProgressBar("Scanning Open Scenes", $"Scanning Scene: {scene.name}", (float)i / SceneManager.sceneCount))
                    return false;

                GameObject[] rootGameObjects = scene.GetRootGameObjects();
                foreach (GameObject rootGo in rootGameObjects)
                {
                    // For scene objects, the "asset path" is the scene's path.
                    CheckObjectForInvalidTags(rootGo, scene.path, true);
                }
            }
            return true;
        }

        private void CheckObjectForInvalidTags(Object obj, string assetPath, bool canFix)
        {
            if (obj is GameObject go)
            {
                MonoBehaviour[] components = go.GetComponentsInChildren<MonoBehaviour>(true);
                foreach (MonoBehaviour component in components)
                {
                    if (component == null) continue;
                    using (SerializedObject serializedObject = new SerializedObject(component))
                        ProcessSerializedObject(serializedObject, assetPath, canFix);
                }
            }
            else if (obj is ScriptableObject scriptableObject)
            {
                using (SerializedObject serializedObject = new SerializedObject(scriptableObject))
                    ProcessSerializedObject(serializedObject, assetPath, canFix);
            }
        }

        private void ProcessSerializedObject(SerializedObject serializedObject, string assetPath, bool canFix)
        {
            SerializedProperty property = serializedObject.GetIterator();
            if (property.NextVisible(true))
            {
                do
                {
                    if (property.propertyType == SerializedPropertyType.Generic && property.type == "GameplayTagContainer")
                    {
                        ProcessTagContainerProperty(serializedObject, property, assetPath, canFix);
                    }
                    else if (property.propertyType == SerializedPropertyType.Generic && property.type == "GameplayTag")
                    {
                        ProcessSingleTagProperty(serializedObject, property, assetPath, canFix);
                    }
                } while (property.NextVisible(true));
            }
        }

        private void ProcessSingleTagProperty(SerializedObject serializedObject, SerializedProperty property, string assetPath, bool canFix)
        {
            SerializedProperty nameProperty = property.FindPropertyRelative("m_Name");
            if (nameProperty == null)
            {
                return;
            }

            string tagName = nameProperty.stringValue;
            if (!string.IsNullOrEmpty(tagName) && !GameplayTagManager.TryRequestTag(tagName, out _))
            {
                AddInvalidTag(new InvalidTagEntry(assetPath, tagName, serializedObject.targetObject, property.propertyPath, InvalidTagReferenceKind.SingleTag, canFix));
            }
        }

        private void ProcessTagContainerProperty(SerializedObject serializedObject, SerializedProperty property, string assetPath, bool canFix)
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
                    AddInvalidTag(new InvalidTagEntry(assetPath, tagName, serializedObject.targetObject, property.propertyPath, InvalidTagReferenceKind.ContainerTag, canFix));
                }
            }
        }

        private void AddInvalidTag(InvalidTagEntry entry)
        {
            if (m_InvalidTags.Count >= MaxInvalidTagEntries)
            {
                throw new InvalidDataException(
                    $"Invalid gameplay tag result count exceeded the {MaxInvalidTagEntries}-entry scan budget.");
            }

            m_InvalidTags.Add(entry);
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
                if (entry.CanFix && TryFixEntry(entry))
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
            if (!entry.CanFix || entry.ContextObject == null)
                return false;

            using SerializedObject serializedObject = new SerializedObject(entry.ContextObject);
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

        private bool HasFixableEntries()
        {
            for (int i = 0; i < m_InvalidTags.Count; i++)
            {
                if (m_InvalidTags[i].CanFix)
                    return true;
            }
            return false;
        }

        private static bool IsWritableProjectAsset(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) ||
                !assetPath.StartsWith("Assets/", System.StringComparison.Ordinal))
            {
                return false;
            }
            return AssetDatabase.IsOpenForEdit(assetPath, StatusQueryOptions.UseCachedIfPossible);
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
