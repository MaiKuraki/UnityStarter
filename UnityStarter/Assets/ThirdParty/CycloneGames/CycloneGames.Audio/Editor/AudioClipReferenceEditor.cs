// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if UNITY_EDITOR
using CycloneGames.Audio.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Audio.Editor
{
    [CustomEditor(typeof(AudioClipReference))]
    public sealed class AudioClipReferenceEditor : UnityEditor.Editor
    {
        private SerializedProperty locationKindProp;
        private SerializedProperty locationProp;
        private SerializedProperty guidProp;
        private SerializedProperty runtimeMutableProp;
        private SerializedProperty versionProp;

        private static GUIContent missingIcon;

        private void OnEnable()
        {
            locationKindProp = serializedObject.FindProperty("locationKind");
            locationProp = serializedObject.FindProperty("m_Location");
            guidProp = serializedObject.FindProperty("m_GUID");
            runtimeMutableProp = serializedObject.FindProperty("runtimeMutable");
            versionProp = serializedObject.FindProperty("version");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(locationKindProp);
            EditorGUILayout.PropertyField(runtimeMutableProp);

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

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.IntField("Version", versionProp.intValue);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawStringLocationField(string label)
        {
            EditorGUI.BeginChangeCheck();
            string newValue = EditorGUILayout.TextField(label, locationProp.stringValue);
            if (EditorGUI.EndChangeCheck())
            {
                locationProp.stringValue = newValue ?? string.Empty;
                guidProp.stringValue = string.Empty;
                versionProp.intValue++;
            }
        }

        private void DrawAssetReferenceField()
        {
            DrawStringLocationField("Address / Location");

            Rect rect = EditorGUILayout.GetControlRect();
            DrawAssetObjectField(rect, new GUIContent("Editor Asset Link"));

            if (!string.IsNullOrEmpty(locationProp.stringValue))
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField("Location", locationProp.stringValue);
                }
            }

            if (!string.IsNullOrEmpty(guidProp.stringValue))
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField("GUID", guidProp.stringValue);
                }
            }
        }

        private void DrawAssetObjectField(Rect position, GUIContent label)
        {
            string guid = guidProp.stringValue;
            AudioClip currentObj = null;
            bool isBroken = false;
            string currentPath = string.Empty;

            if (!string.IsNullOrEmpty(guid))
            {
                currentPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(currentPath))
                {
                    currentObj = AssetDatabase.LoadAssetAtPath<AudioClip>(currentPath);
                }
                else
                {
                    isBroken = true;
                }
            }

            Rect fieldRect = position;
            if (isBroken)
            {
                Rect iconRect = new Rect(position.xMax - 18, position.y + 1, 16, 16);
                fieldRect = new Rect(position.x, position.y, position.width - 20, position.height);
                GUI.Label(iconRect, GetMissingIcon());
            }

            EditorGUI.BeginChangeCheck();
            AudioClip newObj = EditorGUI.ObjectField(fieldRect, label, currentObj, typeof(AudioClip), false) as AudioClip;
            if (EditorGUI.EndChangeCheck())
            {
                if (newObj != null)
                {
                    string path = AssetDatabase.GetAssetPath(newObj);
                    string newGuid = AssetDatabase.AssetPathToGUID(path);
                    if (guidProp.stringValue != newGuid)
                    {
                        guidProp.stringValue = newGuid;
                        if (string.IsNullOrEmpty(locationProp.stringValue))
                        {
                            locationProp.stringValue = path;
                        }
                        versionProp.intValue++;
                    }
                }
                else if (!string.IsNullOrEmpty(guidProp.stringValue))
                {
                    guidProp.stringValue = string.Empty;
                    versionProp.intValue++;
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
            AudioClipReference reference = (AudioClipReference)target;
            string resolved = reference.ResolveLocation();
            if (!string.IsNullOrEmpty(resolved))
            {
                EditorGUILayout.HelpBox(resolved, MessageType.None);
            }
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
