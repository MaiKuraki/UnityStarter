using UnityEditor;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    [CustomEditor(typeof(YooAssetBuildConfig))]
    public class YooAssetBuildConfigEditor : UnityEditor.Editor
    {
        private SerializedProperty versionMode;
        private SerializedProperty manualVersion;
        private SerializedProperty versionPrefix;
        private SerializedProperty copyToStreamingAssets;
        private SerializedProperty copyToOutputDirectory;
        private SerializedProperty buildOutputDirectory;

        private void OnEnable()
        {
            versionMode = serializedObject.FindProperty("versionMode");
            manualVersion = serializedObject.FindProperty("manualVersion");
            versionPrefix = serializedObject.FindProperty("versionPrefix");
            copyToStreamingAssets = serializedObject.FindProperty("copyToStreamingAssets");
            copyToOutputDirectory = serializedObject.FindProperty("copyToOutputDirectory");
            buildOutputDirectory = serializedObject.FindProperty("buildOutputDirectory");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Title
            EditorGUILayout.LabelField("YooAsset Build Configuration", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            // --- Version Section ---
            EditorGUILayout.LabelField("Version Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(versionMode);

            YooAssetVersionMode mode = (YooAssetVersionMode)versionMode.enumValueIndex;
            if (mode == YooAssetVersionMode.Manual)
            {
                EditorGUILayout.PropertyField(manualVersion);
            }
            else if (mode == YooAssetVersionMode.GitCommitCount)
            {
                EditorGUILayout.PropertyField(versionPrefix);

                string bundleVersion = PlayerSettings.bundleVersion;
                int lastDotIndex = bundleVersion.LastIndexOf('.');
                if (lastDotIndex > 0)
                {
                    string expectedPrefix = bundleVersion.Substring(0, lastDotIndex);

                    if (versionPrefix.stringValue != expectedPrefix)
                    {
                        EditorGUILayout.HelpBox($"Version mismatch! Project Version is '{bundleVersion}'. Expected prefix: '{expectedPrefix}'.", MessageType.Warning);
                    }
                }
            }
            
            // Version Preview (Mockup)
            if (mode == YooAssetVersionMode.Timestamp)
            {
                EditorGUILayout.HelpBox($"Example Version: {System.DateTime.Now:yyyy-MM-dd-HHmmss}", MessageType.Info);
            }
            else if (mode == YooAssetVersionMode.GitCommitCount)
            {
                EditorGUILayout.HelpBox($"Example Version: {versionPrefix.stringValue}.42 (Requires Git)", MessageType.Info);
            }

            EditorGUILayout.Space(10);

            // --- Build Options Section ---
            EditorGUILayout.LabelField("Build Options", EditorStyles.boldLabel);

            // Copy To Streaming Assets
            EditorGUILayout.PropertyField(copyToStreamingAssets);
            if (copyToStreamingAssets.boolValue)
            {
                DrawHelpBox("Required for:\n• Offline Mode (Single Player)\n• Host Mode - Base Build (First Install)", MessageType.Info);
            }
            else
            {
                DrawHelpBox("Suitable for:\n• Host Mode - Patch Build (Hotfix Only)\n\n(StreamingAssets will NOT be updated)", MessageType.Warning);
            }

            EditorGUILayout.Space(5);

            // Copy To Output Directory
            EditorGUILayout.PropertyField(copyToOutputDirectory);
            if (copyToOutputDirectory.boolValue)
            {
                EditorGUILayout.PropertyField(buildOutputDirectory);
                DrawHelpBox("Required for:\n• Host Mode - Patch Build (Upload files in 'v1.0.xx' to CDN)\n• Backup / Inspecting build artifacts", MessageType.Info);
            }
            else
            {
                if (!copyToStreamingAssets.boolValue)
                {
                    DrawHelpBox("WARNING: Both copy options are disabled! Build results will only exist in the temporary cache.", MessageType.Error);
                }
            }

            EditorGUILayout.Space(10);
            
            if (GUILayout.Button("Open Build Output Folder"))
            {
                string path = buildOutputDirectory.stringValue;
                if (string.IsNullOrEmpty(path)) path = "Build/HotUpdateBundle";
                
                string fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", path));
                if (System.IO.Directory.Exists(fullPath))
                {
                    EditorUtility.RevealInFinder(fullPath);
                }
                else
                {
                    Debug.LogWarning($"Folder not found: {fullPath}");
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawHelpBox(string message, MessageType type)
        {
            EditorGUILayout.HelpBox(message, type);
        }
    }
}