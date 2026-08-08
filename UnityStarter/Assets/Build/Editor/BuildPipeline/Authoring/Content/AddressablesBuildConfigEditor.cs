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
        private SerializedProperty contentUpdateBaselineAsset;
        private SerializedProperty contentUpdateBaselinePath;
        private SerializedProperty allowExternalProfilePublicationSources;
        private SerializedProperty additionalPublicationRoots;

        private bool hasValidationErrors;

        private void OnEnable()
        {
            buildRemoteCatalog = serializedObject.FindProperty("buildRemoteCatalog");
            copyToOutputDirectory = serializedObject.FindProperty("copyToOutputDirectory");
            buildOutputDirectory = serializedObject.FindProperty("buildOutputDirectory");
            contentUpdateBaselineAsset = serializedObject.FindProperty("contentUpdateBaselineAsset");
            contentUpdateBaselinePath = serializedObject.FindProperty("contentUpdateBaselinePath");
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
                    AddressablesBuildConfig.DefaultBuildOutputBaseDirectory,
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

            DrawContentUpdateBaseline();

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

        private void DrawContentUpdateBaseline()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Official Content Update", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Incremental asset-content invocations require an official addressables_content_state.bin " +
                "from a previous clean publication. The sibling AddressablesArtifacts.json proves its " +
                "target, active profile, remote catalog, and file identity. Clean invocations ignore this baseline.",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(
                contentUpdateBaselineAsset,
                new GUIContent(
                    "Baseline Asset",
                    "Drag a previously published addressables_content_state.bin imported under Assets."));
            if (EditorGUI.EndChangeCheck()
                && contentUpdateBaselineAsset.objectReferenceValue != null)
            {
                contentUpdateBaselinePath.stringValue = string.Empty;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(
                contentUpdateBaselinePath,
                new GUIContent(
                    "Baseline Path",
                    "Portable project-relative path used by CI when the baseline is restored outside Assets."));
            if (EditorGUI.EndChangeCheck()
                && !string.IsNullOrWhiteSpace(contentUpdateBaselinePath.stringValue))
            {
                contentUpdateBaselineAsset.objectReferenceValue = null;
            }

            if (GUILayout.Button("Browse", GUILayout.Width(64f)))
            {
                BrowseContentUpdateBaseline();
            }

            if (GUILayout.Button("Clear", GUILayout.Width(54f)))
            {
                contentUpdateBaselineAsset.objectReferenceValue = null;
                contentUpdateBaselinePath.stringValue = string.Empty;
            }

            EditorGUILayout.EndHorizontal();
            ValidateContentUpdateBaselineAuthoring();
        }

        private void BrowseContentUpdateBaseline()
        {
            string projectRoot = BuildAuthoringPathField.GetProjectRoot();
            string initialDirectory = projectRoot;
            if (!string.IsNullOrWhiteSpace(contentUpdateBaselinePath.stringValue))
            {
                string configured = Path.GetFullPath(
                    Path.Combine(projectRoot, contentUpdateBaselinePath.stringValue));
                string configuredDirectory = Path.GetDirectoryName(configured);
                if (Directory.Exists(configuredDirectory))
                {
                    initialDirectory = configuredDirectory;
                }
            }

            string selected = EditorUtility.OpenFilePanel(
                "Select Addressables Content State",
                initialDirectory,
                "bin");
            if (string.IsNullOrEmpty(selected))
            {
                return;
            }

            if (!TryMakeProjectRelativeFile(projectRoot, selected, out string relativePath))
            {
                EditorUtility.DisplayDialog(
                    "Invalid Content Update Baseline",
                    "The baseline must be a portable file inside the Unity project. CI may restore the file " +
                    "under a project-relative artifact directory before starting Unity.",
                    "OK");
                return;
            }

            UnityEngine.Object asset = relativePath.StartsWith("Assets/", StringComparison.Ordinal)
                ? AssetDatabase.LoadMainAssetAtPath(relativePath)
                : null;
            contentUpdateBaselineAsset.objectReferenceValue = asset;
            contentUpdateBaselinePath.stringValue = asset == null ? relativePath : string.Empty;
        }

        private void ValidateContentUpdateBaselineAuthoring()
        {
            UnityEngine.Object asset = contentUpdateBaselineAsset.objectReferenceValue;
            string configuredPath = contentUpdateBaselinePath.stringValue;
            if (asset != null && !string.IsNullOrWhiteSpace(configuredPath))
            {
                hasValidationErrors = true;
                EditorGUILayout.HelpBox(
                    "Choose either Baseline Asset or Baseline Path, not both.",
                    MessageType.Error);
                return;
            }

            string path = asset == null
                ? configuredPath
                : AssetDatabase.GetAssetPath(asset).Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(path))
            {
                EditorGUILayout.HelpBox(
                    "A baseline is required only when this configuration is used by an Incremental invocation.",
                    MessageType.None);
                return;
            }

            try
            {
                BuildPathPolicy.ValidatePortableProjectRelativePath(
                    path,
                    "Addressables content update baseline");
                if (!string.Equals(Path.GetExtension(path), ".bin", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(
                        "The Addressables content update baseline must be a .bin file.");
                }

                string absolutePath = Path.GetFullPath(
                    Path.Combine(BuildAuthoringPathField.GetProjectRoot(), path));
                if (!File.Exists(absolutePath))
                {
                    EditorGUILayout.HelpBox(
                        "The configured baseline is not present locally. CI must restore it before preflight.",
                        MessageType.Warning);
                }
            }
            catch (Exception exception)
            {
                hasValidationErrors = true;
                EditorGUILayout.HelpBox(exception.Message, MessageType.Error);
            }
        }

        private static bool TryMakeProjectRelativeFile(
            string projectRoot,
            string selectedFile,
            out string relativePath)
        {
            string root = Path.GetFullPath(projectRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string file = Path.GetFullPath(selectedFile);
            if (!BuildPathPolicy.IsStrictDescendant(root, file))
            {
                relativePath = null;
                return false;
            }

            string prefix = root + Path.DirectorySeparatorChar;
            relativePath = file.Substring(prefix.Length).Replace('\\', '/');
            try
            {
                BuildPathPolicy.ValidatePortableProjectRelativePath(
                    relativePath,
                    "Addressables content update baseline");
                return string.Equals(
                    Path.GetExtension(relativePath),
                    ".bin",
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException)
            {
                relativePath = null;
                return false;
            }
        }

        private void ValidateBuildOutputDirectory()
        {
            string path = buildOutputDirectory.stringValue;
            if (string.IsNullOrWhiteSpace(path))
            {
                EditorGUILayout.HelpBox(
                    $"An empty path uses '{AddressablesBuildConfig.DefaultBuildOutputBaseDirectory}/<invocation-id>' so multiple content invocations remain isolated by default.",
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
                path = AddressablesBuildConfig.DefaultBuildOutputBaseDirectory;
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
