// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if UNITY_EDITOR
using CycloneGames.Audio.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Audio.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(AudioClipReference))]
    public sealed class AudioClipReferenceEditor : UnityEditor.Editor
    {
        private SerializedProperty locationKindProp;
        private SerializedProperty locationProp;
        private SerializedProperty guidProp;
        private SerializedProperty runtimeMutableProp;
        private SerializedProperty versionProp;
        private string cachedAssetGuid;
        private string cachedAssetPath;
        private AudioClip cachedAsset;
        private int pendingPerTargetVersionIncrementCount;

        private static GUIContent missingIcon;
        private static readonly GUIContent EditorAssetLinkLabel = new GUIContent("Editor Asset Link");
        private static readonly GUIContent LocationLabel = new GUIContent("Location");
        private static readonly GUIContent GuidLabel = new GUIContent("GUID");
        private static readonly GUIContent VersionLabel = new GUIContent("Version");

        private void OnEnable()
        {
            locationKindProp = serializedObject.FindProperty("locationKind");
            locationProp = serializedObject.FindProperty("m_Location");
            guidProp = serializedObject.FindProperty("m_GUID");
            runtimeMutableProp = serializedObject.FindProperty("runtimeMutable");
            versionProp = serializedObject.FindProperty("version");
            EditorApplication.projectChanged -= InvalidateAssetLinkCache;
            EditorApplication.projectChanged += InvalidateAssetLinkCache;
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= InvalidateAssetLinkCache;
        }

        public override void OnInspectorGUI()
        {
            pendingPerTargetVersionIncrementCount = 0;
            serializedObject.Update();

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(locationKindProp);
            if (EditorGUI.EndChangeCheck())
                IncrementSerializedVersion();
            EditorGUILayout.PropertyField(runtimeMutableProp);

            if (locationKindProp.hasMultipleDifferentValues)
            {
                EditorGUILayout.HelpBox("Choose a common Location Kind to edit location details for multiple references.", MessageType.Info);
            }
            else
            {
                AudioLocationKind kind = (AudioLocationKind)locationKindProp.enumValueIndex;
                switch (kind)
                {
                    case AudioLocationKind.AssetAddress:
                        DrawAssetReferenceField();
                        break;
                    case AudioLocationKind.Url:
                        DrawStringLocationField("URL");
                        break;
                    case AudioLocationKind.StreamingAssetsPath:
                        DrawStringLocationField("StreamingAssets Path");
                        DrawResolvedPathHelpBox();
                        break;
                    case AudioLocationKind.PersistentDataPath:
                        DrawStringLocationField("PersistentData Path");
                        DrawResolvedPathHelpBox();
                        break;
                    default:
                        DrawStringLocationField("File Path");
                        break;
                }
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(versionProp, VersionLabel);
            }

            serializedObject.ApplyModifiedProperties();
            ApplyPendingPerTargetVersionIncrements();
        }

        private void DrawStringLocationField(string label, bool clearEditorAssetLink = true)
        {
            bool previousMixedValue = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = locationProp.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            string newValue = EditorGUILayout.TextField(label, locationProp.stringValue);
            if (EditorGUI.EndChangeCheck())
            {
                locationProp.stringValue = newValue ?? string.Empty;
                if (clearEditorAssetLink)
                    guidProp.stringValue = string.Empty;
                IncrementSerializedVersion();
            }
            EditorGUI.showMixedValue = previousMixedValue;
        }

        private void DrawAssetReferenceField()
        {
            DrawStringLocationField("Address / Location", false);
            EditorGUILayout.HelpBox(
                "Location is the explicit runtime provider key. The Editor Asset Link is authoring-only and never writes an AssetDatabase path into Location.",
                MessageType.Info);

            Rect rect = EditorGUILayout.GetControlRect();
            DrawAssetObjectField(rect, EditorAssetLinkLabel);

            if (locationProp.hasMultipleDifferentValues || !string.IsNullOrEmpty(locationProp.stringValue))
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.PropertyField(locationProp, LocationLabel);
                }
            }

            if (guidProp.hasMultipleDifferentValues || !string.IsNullOrEmpty(guidProp.stringValue))
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.PropertyField(guidProp, GuidLabel);
                }
            }
        }

        private void DrawAssetObjectField(Rect position, GUIContent label)
        {
            bool hasMixedGuid = guidProp.hasMultipleDifferentValues;
            string guid = hasMixedGuid ? string.Empty : guidProp.stringValue;
            AudioClip currentObj = null;
            bool isBroken = false;
            string currentPath = string.Empty;

            if (!string.IsNullOrEmpty(guid))
            {
                ResolveCachedAssetLink(guid);
                currentPath = cachedAssetPath;
                currentObj = cachedAsset;
                isBroken = string.IsNullOrEmpty(currentPath) || currentObj == null;
            }

            Rect fieldRect = position;
            if (isBroken)
            {
                Rect iconRect = new Rect(position.xMax - 18, position.y + 1, 16, 16);
                fieldRect = new Rect(position.x, position.y, position.width - 20, position.height);
                GUI.Label(iconRect, GetMissingIcon());
            }

            EditorGUI.BeginChangeCheck();
            bool previousMixedValue = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = hasMixedGuid;
            AudioClip newObj = EditorGUI.ObjectField(fieldRect, label, currentObj, typeof(AudioClip), false) as AudioClip;
            EditorGUI.showMixedValue = previousMixedValue;
            if (EditorGUI.EndChangeCheck())
            {
                if (newObj != null)
                {
                    string path = AssetDatabase.GetAssetPath(newObj);
                    string newGuid = AssetDatabase.AssetPathToGUID(path);
                    if (hasMixedGuid || guidProp.stringValue != newGuid)
                    {
                        guidProp.stringValue = newGuid;
                        cachedAssetGuid = newGuid;
                        cachedAssetPath = path;
                        cachedAsset = newObj;
                        IncrementSerializedVersion();
                    }
                }
                else if (hasMixedGuid || !string.IsNullOrEmpty(guidProp.stringValue))
                {
                    guidProp.stringValue = string.Empty;
                    InvalidateAssetLinkCache();
                    IncrementSerializedVersion();
                }
            }

            if (!string.IsNullOrEmpty(currentPath))
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField("Editor Asset Path", currentPath);
                }
            }
        }

        private void DrawResolvedPathHelpBox()
        {
            if (serializedObject.isEditingMultipleObjects)
            {
                return;
            }

            AudioClipReference reference = (AudioClipReference)target;
            if (reference.TryResolveLocation(out string resolved, out string error))
            {
                EditorGUILayout.HelpBox(resolved, MessageType.None);
            }
            else if (!string.IsNullOrEmpty(error))
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }
        }

        private void ResolveCachedAssetLink(string guid)
        {
            if (cachedAssetGuid == guid) return;

            cachedAssetGuid = guid;
            cachedAssetPath = AssetDatabase.GUIDToAssetPath(guid);
            cachedAsset = !string.IsNullOrEmpty(cachedAssetPath)
                ? AssetDatabase.LoadAssetAtPath<AudioClip>(cachedAssetPath)
                : null;
        }

        private void InvalidateAssetLinkCache()
        {
            cachedAssetGuid = null;
            cachedAssetPath = string.Empty;
            cachedAsset = null;
        }

        private void IncrementSerializedVersion()
        {
            if (serializedObject.isEditingMultipleObjects)
            {
                pendingPerTargetVersionIncrementCount++;
                return;
            }

            versionProp.intValue = GetNextVersion(versionProp.intValue);
        }

        private void ApplyPendingPerTargetVersionIncrements()
        {
            if (pendingPerTargetVersionIncrementCount == 0)
            {
                return;
            }

            for (int targetIndex = 0; targetIndex < targets.Length; targetIndex++)
            {
                SerializedObject targetSerializedObject = new SerializedObject(targets[targetIndex]);
                targetSerializedObject.Update();
                SerializedProperty targetVersionProp = targetSerializedObject.FindProperty("version");
                if (targetVersionProp == null)
                {
                    continue;
                }

                int version = targetVersionProp.intValue;
                for (int incrementIndex = 0; incrementIndex < pendingPerTargetVersionIncrementCount; incrementIndex++)
                {
                    version = GetNextVersion(version);
                }

                targetVersionProp.intValue = version;
                targetSerializedObject.ApplyModifiedProperties();
            }

            pendingPerTargetVersionIncrementCount = 0;
            serializedObject.Update();
        }

        private static int GetNextVersion(int version)
        {
            return version == int.MaxValue ? 1 : version + 1;
        }

        private static GUIContent GetMissingIcon()
        {
            if (missingIcon == null)
            {
                missingIcon = EditorGUIUtility.TrIconContent(
                    "console.warnicon.sml", "Referenced asset is missing.");
            }

            return missingIcon;
        }
    }
}
#endif
