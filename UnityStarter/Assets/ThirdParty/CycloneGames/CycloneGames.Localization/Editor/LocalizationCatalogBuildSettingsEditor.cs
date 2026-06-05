#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Localization.Editor
{
    [CustomEditor(typeof(LocalizationCatalogBuildSettings))]
    public sealed class LocalizationCatalogBuildSettingsEditor : UnityEditor.Editor
    {
        private SerializedProperty _outputFolder;
        private SerializedProperty _outputFileName;
        private SerializedProperty _catalogVersion;
        private SerializedProperty _validateBeforeBuild;
        private SerializedProperty _selectBuiltAsset;

        private void OnEnable()
        {
            _outputFolder = serializedObject.FindProperty("outputFolder");
            _outputFileName = serializedObject.FindProperty("outputFileName");
            _catalogVersion = serializedObject.FindProperty("catalogVersion");
            _validateBeforeBuild = serializedObject.FindProperty("validateBeforeBuild");
            _selectBuiltAsset = serializedObject.FindProperty("selectBuiltAsset");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(_outputFolder, new GUIContent("Output Folder"));
            EditorGUILayout.PropertyField(_outputFileName, new GUIContent("Output File Name"));
            EditorGUILayout.PropertyField(_catalogVersion, new GUIContent("Catalog Version"));
            EditorGUILayout.PropertyField(_validateBeforeBuild, new GUIContent("Validate Before Build"));
            EditorGUILayout.PropertyField(_selectBuiltAsset, new GUIContent("Select Built Asset"));

            serializedObject.ApplyModifiedProperties();

            var settings = (LocalizationCatalogBuildSettings)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Resolved Output Path", settings.OutputPath);

            if (!IsValidOutputFolder(settings))
                EditorGUILayout.HelpBox("Output Folder must be a folder under Assets. If empty or invalid, Assets is used.", MessageType.Warning);

            if (GUILayout.Button("Build Catalog"))
                Build(settings);
        }

        private static bool IsValidOutputFolder(LocalizationCatalogBuildSettings settings)
        {
            if (settings.OutputFolder == null) return true;
            string path = AssetDatabase.GetAssetPath(settings.OutputFolder);
            return !string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path) && path.StartsWith("Assets");
        }

        private static void Build(LocalizationCatalogBuildSettings settings)
        {
            try
            {
                LocalizationCatalogBuilder.BuildCatalog(settings);
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "Localization Catalog",
                    "Catalog build failed. See Console for details.",
                    "OK");
            }
        }
    }
}
#endif
