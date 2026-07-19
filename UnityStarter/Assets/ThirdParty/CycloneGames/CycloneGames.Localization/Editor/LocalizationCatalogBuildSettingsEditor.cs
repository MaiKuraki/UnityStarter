#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Localization.Editor
{
    [CustomEditor(typeof(LocalizationCatalogBuildSettings))]
    public sealed class LocalizationCatalogBuildSettingsEditor : UnityEditor.Editor
    {
        private SerializedProperty _localizationSettings;
        private SerializedProperty _outputFolder;
        private SerializedProperty _outputFileName;
        private SerializedProperty _catalogVersion;
        private SerializedProperty _contentKind;
        private SerializedProperty _includedLocales;
        private SerializedProperty _includedTableIds;
        private SerializedProperty _validateBeforeBuild;
        private SerializedProperty _selectBuiltAsset;

        private void OnEnable()
        {
            _localizationSettings = serializedObject.FindProperty("localizationSettings");
            _outputFolder = serializedObject.FindProperty("outputFolder");
            _outputFileName = serializedObject.FindProperty("outputFileName");
            _catalogVersion = serializedObject.FindProperty("catalogVersion");
            _contentKind = serializedObject.FindProperty("contentKind");
            _includedLocales = serializedObject.FindProperty("includedLocales");
            _includedTableIds = serializedObject.FindProperty("includedTableIds");
            _validateBeforeBuild = serializedObject.FindProperty("validateBeforeBuild");
            _selectBuiltAsset = serializedObject.FindProperty("selectBuiltAsset");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(_localizationSettings, new GUIContent(
                "Localization Settings",
                "Optional explicit project settings. Required when more than one LocalizationSettings asset exists."));
            EditorGUILayout.PropertyField(_outputFolder, new GUIContent("Output Folder"));
            EditorGUILayout.PropertyField(_outputFileName, new GUIContent("Output File Name"));
            EditorGUILayout.PropertyField(_catalogVersion, new GUIContent("Catalog Version"));
            EditorGUILayout.PropertyField(_contentKind, new GUIContent(
                "Content Kind",
                "Choose string tables, asset tables, or both."));
            EditorGUILayout.PropertyField(_includedLocales, new GUIContent(
                "Included Locales",
                "Empty includes every locale in LocalizationSettings. Use this to build independently downloadable locale packs."),
                true);
            EditorGUILayout.PropertyField(_includedTableIds, new GUIContent(
                "Included Table IDs",
                "Empty includes every table ID. Values use ordinal matching."),
                true);
            EditorGUILayout.PropertyField(_validateBeforeBuild, new GUIContent("Validate Before Build"));
            EditorGUILayout.PropertyField(_selectBuiltAsset, new GUIContent("Select Built Asset"));

            serializedObject.ApplyModifiedProperties();

            var settings = (LocalizationCatalogBuildSettings)target;
            EditorGUILayout.Space();
            bool validPath = settings.TryGetOutputPath(out string outputPath, out string pathError);
            EditorGUILayout.LabelField("Resolved Output Path", validPath ? outputPath : "Invalid");

            if (!validPath)
                EditorGUILayout.HelpBox(pathError, MessageType.Error);

            bool validSettings = LocalizationEditorSettingsUtility.TryResolve(
                settings.LocalizationSettings,
                out _,
                out string settingsError);
            if (!validSettings)
                EditorGUILayout.HelpBox(settingsError, MessageType.Error);

            using (new EditorGUI.DisabledScope(!validPath || !validSettings))
            {
                if (GUILayout.Button("Build Catalog"))
                    Build(settings);
            }
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
