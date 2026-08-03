using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    [CustomEditor(typeof(AddressablesBuildConfig))]
    public sealed class AddressablesBuildConfigEditor : UnityEditor.Editor
    {
        private SerializedProperty buildRemoteCatalog;
        private SerializedProperty copyToOutputDirectory;
        private SerializedProperty buildOutputDirectory;
        private SerializedProperty allowExternalProfilePublicationSources;
        private SerializedProperty additionalPublicationRoots;

        private bool hasValidationErrors;

        private void OnEnable()
        {
            buildRemoteCatalog = serializedObject.FindProperty("buildRemoteCatalog");
            copyToOutputDirectory = serializedObject.FindProperty("copyToOutputDirectory");
            buildOutputDirectory = serializedObject.FindProperty("buildOutputDirectory");
            allowExternalProfilePublicationSources = serializedObject.FindProperty(
                "allowExternalProfilePublicationSources");
            additionalPublicationRoots = serializedObject.FindProperty("additionalPublicationRoots");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            hasValidationErrors = false;

            EditorGUILayout.LabelField("Addressables Build Configuration", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "The composable build pipeline owns the canonical content version. " +
                "This asset configures only Addressables build and publication behavior.",
                MessageType.Info);

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Build Options", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(buildRemoteCatalog);
            EditorGUILayout.HelpBox(
                buildRemoteCatalog.boolValue
                    ? "A remote catalog will be generated for remote content delivery."
                    : "Only the local catalog will be generated.",
                MessageType.Info);

            EditorGUILayout.Space(5);
            EditorGUILayout.PropertyField(copyToOutputDirectory);
            if (copyToOutputDirectory.boolValue)
            {
                BuildAuthoringPathField.DrawProjectRelativeDirectory(
                    buildOutputDirectory,
                    new GUIContent(
                        "Publication Root",
                        "Project-relative output directory. CI and all workstations resolve the same portable path."),
                    AddressablesBuildConfig.DefaultBuildOutputDirectory,
                    allowEmpty: true);
                EditorGUILayout.PropertyField(allowExternalProfilePublicationSources);
                if (allowExternalProfilePublicationSources.boolValue)
                {
                    EditorGUILayout.HelpBox(
                        "External profile publication sources must be dedicated CI-owned local directories. " +
                        "URI, volume-root, protected, and reparse-point paths remain invalid.",
                        MessageType.Warning);
                }

                EditorGUILayout.PropertyField(additionalPublicationRoots, includeChildren: true);
                ValidateBuildOutputDirectory();
                EditorGUILayout.HelpBox(
                    "The current build FileRegistry is published as PlayerData, RemoteContent, " +
                    "BuildMetadata, and explicitly configured additional roots.",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Build results will remain only in the Addressables build cache.",
                    MessageType.Warning);
            }

            EditorGUILayout.Space(10);
            if (GUILayout.Button("Open Build Output Folder"))
            {
                OpenBuildOutputFolder();
            }

            if (hasValidationErrors)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox(
                    "Configuration issues were detected. Fix the errors before building.",
                    MessageType.Warning);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void ValidateBuildOutputDirectory()
        {
            string path = buildOutputDirectory.stringValue;
            if (string.IsNullOrWhiteSpace(path))
            {
                EditorGUILayout.HelpBox(
                    $"An empty path uses the default '{AddressablesBuildConfig.DefaultBuildOutputDirectory}'.",
                    MessageType.Info);
                return;
            }

            string trimmedPath = path.Trim();
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            try
            {
                BuildPathPolicy.ResolveBuildRoot(projectRoot, trimmedPath);
            }
            catch (Exception exception)
            {
                hasValidationErrors = true;
                EditorGUILayout.HelpBox(
                    exception.Message,
                    MessageType.Error);
            }
        }

        private void OpenBuildOutputFolder()
        {
            string path = buildOutputDirectory.stringValue;
            if (string.IsNullOrWhiteSpace(path))
            {
                path = AddressablesBuildConfig.DefaultBuildOutputDirectory;
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string fullPath;
            try
            {
                fullPath = BuildPathPolicy.ResolveBuildRoot(projectRoot, path);
            }
            catch (System.Exception exception)
            {
                Debug.LogError($"[AddressablesBuildConfig] Invalid build output path: {exception.Message}");
                return;
            }

            if (Directory.Exists(fullPath))
            {
                EditorUtility.RevealInFinder(fullPath);
            }
            else
            {
                Debug.LogWarning($"[AddressablesBuildConfig] Folder not found: {fullPath}");
            }
        }
    }
}
