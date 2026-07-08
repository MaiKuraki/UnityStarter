using CycloneGames.AssetManagement.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.AssetManagement.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(AssetPatchProfileAsset), true)]
    public sealed class AssetPatchProfileAssetEditor : UnityEditor.Editor
    {
        private static readonly GUIContent PackageNameLabel = new GUIContent("Package Name");
        private static readonly GUIContent DefaultSettingsLabel = new GUIContent("Default Settings");
        private static readonly GUIContent PlatformOverridesLabel = new GUIContent("Platform Overrides");

        private SerializedProperty _packageName;
        private SerializedProperty _defaultSettings;
        private SerializedProperty _platformOverrides;

        private void OnEnable()
        {
            _packageName = serializedObject.FindProperty("PackageName");
            _defaultSettings = serializedObject.FindProperty("DefaultSettings");
            _platformOverrides = serializedObject.FindProperty("PlatformOverrides");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(_packageName, PackageNameLabel);
            EditorGUILayout.PropertyField(_defaultSettings, DefaultSettingsLabel, includeChildren: true);
            EditorGUILayout.PropertyField(_platformOverrides, PlatformOverridesLabel, includeChildren: true);

            serializedObject.ApplyModifiedProperties();

            DrawValidation();
        }

        private void DrawValidation()
        {
            if (serializedObject.isEditingMultipleObjects)
            {
                return;
            }

            var profile = (AssetPatchProfileAsset)target;
            if (profile.TryBuildRuntimeProfile(out _, out string error))
            {
                EditorGUILayout.HelpBox("Runtime profile is valid for the current platform.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }
        }
    }
}
