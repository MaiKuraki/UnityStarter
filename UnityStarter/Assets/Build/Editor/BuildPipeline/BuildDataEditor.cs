using System.IO;
using UnityEditor;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    [CustomEditor(typeof(BuildData))]
    public class BuildDataEditor : UnityEditor.Editor
    {
        private SerializedProperty launchScene;
        private SerializedProperty applicationVersion;
        private SerializedProperty outputBasePath;
        private SerializedProperty useBuildalon;
        private SerializedProperty useHybridCLR;
        private SerializedProperty assetManagementType;

        private bool hasValidationErrors = false;

        // Cache validation results to avoid repeated lookups
        private bool? _yooAssetConfigExists = null;
        private bool? _addressablesConfigExists = null;
        private bool? _hybridCLRConfigExists = null;
        private double _lastValidationTime = 0;
        private const double ValidationCacheInterval = 0.5; // Refresh validation cache every 0.5 seconds

        private void OnEnable()
        {
            launchScene = serializedObject.FindProperty("launchScene");
            applicationVersion = serializedObject.FindProperty("applicationVersion");
            outputBasePath = serializedObject.FindProperty("outputBasePath");
            useBuildalon = serializedObject.FindProperty("useBuildalon");
            useHybridCLR = serializedObject.FindProperty("useHybridCLR");
            assetManagementType = serializedObject.FindProperty("assetManagementType");

            // Clear validation cache when editor is enabled
            _yooAssetConfigExists = null;
            _addressablesConfigExists = null;
            _hybridCLRConfigExists = null;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            hasValidationErrors = false;

            EditorGUILayout.LabelField("Build Configuration", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            // Build Scene Config
            EditorGUILayout.LabelField("Build Scene Config", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(launchScene);
            ValidateLaunchScene();
            EditorGUILayout.Space(10);

            // Build Version & Output
            EditorGUILayout.LabelField("Build Version & Output", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(applicationVersion);
            ValidateApplicationVersion();
            EditorGUILayout.PropertyField(outputBasePath);
            ValidateOutputBasePath();

            // Show full output path preview (only if path is valid)
            string fullOutputPath = GetFullOutputPath();
            if (!string.IsNullOrEmpty(fullOutputPath) && !hasValidationErrors)
            {
                DrawHelpBox(
                    $"✓ Full Output Path:\n{fullOutputPath}\n\n" +
                    "Build results will be saved to this directory, organized by platform.",
                    MessageType.Info);
            }

            EditorGUILayout.Space(10);

            // Build Pipeline Options
            EditorGUILayout.LabelField("Build Pipeline Options", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(useBuildalon);
            EditorGUILayout.PropertyField(useHybridCLR);
            EditorGUILayout.Space(10);

            // Asset Management System
            EditorGUILayout.LabelField("Asset Management System", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(assetManagementType);

            AssetManagementType selectedType = (AssetManagementType)assetManagementType.enumValueIndex;

            EditorGUILayout.Space(5);

            switch (selectedType)
            {
                case AssetManagementType.None:
                    DrawHelpBox(
                        "No asset management system selected.\n" +
                        "Resources will be built directly into the player build.",
                        MessageType.Info);
                    break;

                case AssetManagementType.YooAsset:
                    DrawHelpBox(
                        "✓ Using YooAsset for asset management and hot-update.\n\n" +
                        "Features:\n" +
                        "• Flexible bundle packaging\n" +
                        "• Built-in hot-update mechanism\n" +
                        "• Supports multiple loading modes (Offline, Host, Web)\n\n" +
                        "Requires: YooAssetBuildConfig configuration",
                        MessageType.Info);
                    ValidateYooAssetConfig();
                    break;

                case AssetManagementType.Addressables:
                    DrawHelpBox(
                        "✓ Using Addressables for asset management and hot-update.\n\n" +
                        "Features:\n" +
                        "• Unity's official asset management solution\n" +
                        "• Remote content delivery via CDN\n" +
                        "• Built-in catalog system for versioning\n\n" +
                        "Requires: AddressablesBuildConfig configuration",
                        MessageType.Info);
                    ValidateAddressablesConfig();
                    break;
            }

            // Validate HybridCLR config if enabled
            if (useHybridCLR.boolValue)
            {
                ValidateHybridCLRConfig();
            }

            // Show validation summary at the end if there are errors
            if (hasValidationErrors)
            {
                EditorGUILayout.Space(10);
                DrawValidationSummary();
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawValidationSummary()
        {
            if (hasValidationErrors)
            {
                DrawHelpBox(
                    "⚠ Configuration Issues Detected\n" +
                    "Please fix the errors below before building.",
                    MessageType.Warning);
                EditorGUILayout.Space(5);
            }
        }

        private void ValidateLaunchScene()
        {
            if (launchScene.objectReferenceValue == null)
            {
                hasValidationErrors = true;
                DrawHelpBox(
                    "❌ Launch Scene is required!\n\n" +
                    "How to fix:\n" +
                    "1. Click the object field above\n" +
                    "2. Select a scene asset from your project (e.g., Assets/Scenes/MainMenu.unity)\n" +
                    "3. This scene will be used as the entry point when the game starts\n\n" +
                    "Example:\n" +
                    "• Assets/Scenes/MainMenu.unity\n" +
                    "• Assets/Scenes/Gameplay.unity",
                    MessageType.Error);
            }
        }

        private void ValidateApplicationVersion()
        {
            string version = applicationVersion.stringValue;
            if (string.IsNullOrWhiteSpace(version))
            {
                hasValidationErrors = true;
                DrawHelpBox(
                    "❌ Application Version is required!\n\n" +
                    "How to fill:\n" +
                    "Enter a version prefix that will be used for the build version.\n" +
                    "The final version will be: {ApplicationVersion}.{GitCommitCount}\n\n" +
                    "Correct Examples:\n" +
                    "• v0.1 (will become v0.1.42 if commit count is 42)\n" +
                    "• 1.0.0 (will become 1.0.0.42)\n" +
                    "• v2.1 (will become v2.1.42)\n\n" +
                    "This version is used to set PlayerSettings.bundleVersion during build.",
                    MessageType.Error);
            }
            else
            {
                string trimmed = version.Trim();
                if (trimmed.Length == 0)
                {
                    hasValidationErrors = true;
                    DrawHelpBox(
                        "❌ Application Version cannot be empty!\n\n" +
                        "How to fill:\n" +
                        "Enter a valid version string (e.g., 'v0.1' or '1.0.0').",
                        MessageType.Error);
                }
                else
                {
                    // Show version preview
                    DrawHelpBox(
                        $"ℹ Application Version: {trimmed}\n\n" +
                        $"The final build version will be: {trimmed}.{{GitCommitCount}}\n" +
                        "Example: If commit count is 42, the version will be " + trimmed + ".42\n\n" +
                        "This version will be applied to PlayerSettings.bundleVersion during build.",
                        MessageType.Info);
                }
            }
        }

        private void ValidateOutputBasePath()
        {
            string path = outputBasePath.stringValue;
            if (string.IsNullOrWhiteSpace(path))
            {
                hasValidationErrors = true;
                DrawHelpBox(
                    "❌ Output Base Path is required!\n\n" +
                    "How to fill:\n" +
                    "Enter a directory path relative to your project root where build results will be saved.\n\n" +
                    "Correct Examples:\n" +
                    "• Build (simple folder name)\n" +
                    "• Output (alternative folder name)\n" +
                    "• Build/Releases (nested folder)\n\n" +
                    "The full path will be: {OutputBasePath}/{Platform}/{BuildName}",
                    MessageType.Error);
            }
            else
            {
                string trimmedPath = path.Trim();
                if (trimmedPath.Length == 0)
                {
                    hasValidationErrors = true;
                    DrawHelpBox(
                        "❌ Output Base Path cannot be empty!\n\n" +
                        "How to fill:\n" +
                        "Enter a valid directory path (e.g., 'Build' or 'Output').",
                        MessageType.Error);
                }
                else
                {
                    // Check for invalid characters
                    char[] invalidChars = Path.GetInvalidPathChars();
                    bool hasInvalidChar = false;
                    char invalidChar = '\0';
                    foreach (char c in invalidChars)
                    {
                        if (trimmedPath.Contains(c))
                        {
                            hasInvalidChar = true;
                            invalidChar = c;
                            break;
                        }
                    }

                    if (hasInvalidChar)
                    {
                        hasValidationErrors = true;
                        string charDisplay = invalidChar == '\0' ? "null" : $"'{invalidChar}'";
                        DrawHelpBox(
                            $"❌ Output Base Path contains invalid character: {charDisplay}\n\n" +
                            "How to fix:\n" +
                            "Remove all invalid characters from the path. Use only letters, numbers, underscores, hyphens, and forward slashes.\n\n" +
                            "Correct Examples:\n" +
                            "• Build\n" +
                            "• Build/Releases\n" +
                            "• Output_Builds\n\n" +
                            "Invalid Characters:\n" +
                            "• < > : \" | ? * and other special characters",
                            MessageType.Error);
                    }
                    else if (trimmedPath.Contains(".."))
                    {
                        DrawHelpBox(
                            "⚠ Warning: Output path contains '..' which may cause issues.\n\n" +
                            "Current value: " + trimmedPath + "\n\n" +
                            "How to fix:\n" +
                            "Use a direct path relative to the project root instead of using '..'.\n\n" +
                            "Correct Examples:\n" +
                            "• Build (instead of ../Build)\n" +
                            "• Output/Releases",
                            MessageType.Warning);
                    }
                }
            }
        }

        private bool ShouldRefreshValidationCache()
        {
            double currentTime = EditorApplication.timeSinceStartup;
            if (currentTime - _lastValidationTime > ValidationCacheInterval)
            {
                _lastValidationTime = currentTime;
                return true;
            }
            return false;
        }

        private void ValidateYooAssetConfig()
        {
            // Use cached result if available and cache is still valid
            if (!ShouldRefreshValidationCache() && _yooAssetConfigExists.HasValue)
            {
                if (!_yooAssetConfigExists.Value)
                {
                    hasValidationErrors = true;
                    DrawHelpBox(
                        "❌ YooAssetBuildConfig not found!\n" +
                        "Please create a YooAssetBuildConfig asset (Create -> CycloneGames/Build/YooAsset Build Config).",
                        MessageType.Error);
                }
                return;
            }

            // Refresh cache
            YooAssetBuildConfig config = BuildConfigHelper.GetYooAssetConfig();
            _yooAssetConfigExists = config != null;

            if (config == null)
            {
                hasValidationErrors = true;
                DrawHelpBox(
                    "❌ YooAssetBuildConfig not found!\n" +
                    "Please create a YooAssetBuildConfig asset (Create -> CycloneGames/Build/YooAsset Build Config).",
                    MessageType.Error);
            }
        }

        private void ValidateAddressablesConfig()
        {
            // Use cached result if available and cache is still valid
            if (!ShouldRefreshValidationCache() && _addressablesConfigExists.HasValue)
            {
                if (!_addressablesConfigExists.Value)
                {
                    hasValidationErrors = true;
                    DrawHelpBox(
                        "❌ AddressablesBuildConfig not found!\n" +
                        "Please create an AddressablesBuildConfig asset (Create -> CycloneGames/Build/Addressables Build Config).",
                        MessageType.Error);
                }
                return;
            }

            // Refresh cache
            AddressablesBuildConfig config = BuildConfigHelper.GetAddressablesConfig();
            _addressablesConfigExists = config != null;

            if (config == null)
            {
                hasValidationErrors = true;
                DrawHelpBox(
                    "❌ AddressablesBuildConfig not found!\n" +
                    "Please create an AddressablesBuildConfig asset (Create -> CycloneGames/Build/Addressables Build Config).",
                    MessageType.Error);
            }
        }

        private void ValidateHybridCLRConfig()
        {
            // Use cached result if available and cache is still valid
            if (!ShouldRefreshValidationCache() && _hybridCLRConfigExists.HasValue)
            {
                if (!_hybridCLRConfigExists.Value)
                {
                    hasValidationErrors = true;
                    DrawHelpBox(
                        "❌ HybridCLRBuildConfig not found!\n" +
                        "HybridCLR is enabled but no configuration found. Please create a HybridCLRBuildConfig asset.",
                        MessageType.Error);
                }
                return;
            }

            // Refresh cache
            HybridCLRBuildConfig config = BuildConfigHelper.GetHybridCLRConfig();
            _hybridCLRConfigExists = config != null;

            if (config == null)
            {
                hasValidationErrors = true;
                DrawHelpBox(
                    "❌ HybridCLRBuildConfig not found!\n" +
                    "HybridCLR is enabled but no configuration found. Please create a HybridCLRBuildConfig asset.",
                    MessageType.Error);
            }
        }

        private string GetFullOutputPath()
        {
            if (outputBasePath == null || string.IsNullOrEmpty(outputBasePath.stringValue))
                return null;

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string relativePath = outputBasePath.stringValue.Trim();

            if (relativePath.StartsWith("/"))
            {
                relativePath = relativePath.Substring(1);
            }

            if (Path.IsPathRooted(relativePath))
            {
                return relativePath;
            }

            return Path.Combine(projectRoot, relativePath).Replace('\\', '/');
        }

        private void DrawHelpBox(string message, MessageType type)
        {
            EditorGUILayout.HelpBox(message, type);
        }
    }
}