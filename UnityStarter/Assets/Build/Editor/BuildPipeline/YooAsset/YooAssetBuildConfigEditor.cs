using System.IO;
using UnityEditor;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    [CustomEditor(typeof(YooAssetBuildConfig))]
    public class YooAssetBuildConfigEditor : UnityEditor.Editor
    {
        // Duplicate detection
        private static string[] allConfigGuids;
        private static bool hasCheckedForDuplicates;

        private SerializedProperty versionMode;
        private SerializedProperty manualVersion;
        private SerializedProperty versionPrefix;
        private SerializedProperty copyToStreamingAssets;
        private SerializedProperty copyToOutputDirectory;
        private SerializedProperty buildOutputDirectory;

        private bool hasValidationErrors = false;

        // Cache BuildData version to avoid repeated lookups
        private string _cachedBuildDataVersion = null;
        private double _lastVersionCheckTime = 0;
        private const double VersionCheckCacheInterval = 0.5;

        private void OnEnable()
        {
            versionMode = serializedObject.FindProperty("versionMode");
            manualVersion = serializedObject.FindProperty("manualVersion");
            versionPrefix = serializedObject.FindProperty("versionPrefix");
            copyToStreamingAssets = serializedObject.FindProperty("copyToStreamingAssets");
            copyToOutputDirectory = serializedObject.FindProperty("copyToOutputDirectory");
            buildOutputDirectory = serializedObject.FindProperty("buildOutputDirectory");
        }

        private string GetBuildDataVersion()
        {
            double currentTime = EditorApplication.timeSinceStartup;
            if (currentTime - _lastVersionCheckTime > VersionCheckCacheInterval || _cachedBuildDataVersion == null)
            {
                BuildData buildData = BuildConfigHelper.GetBuildData();
                _cachedBuildDataVersion = buildData != null ? buildData.ApplicationVersion : null;
                _lastVersionCheckTime = currentTime;
            }
            return _cachedBuildDataVersion;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            hasValidationErrors = false;

            // Check for duplicates
            CheckForDuplicates();

            EditorGUILayout.LabelField("YooAsset Build Configuration", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            // Duplicate warning
            DrawDuplicateWarning();

            // Version Settings
            EditorGUILayout.LabelField("Version Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(versionMode);

            YooAssetVersionMode mode = (YooAssetVersionMode)versionMode.enumValueIndex;
            if (mode == YooAssetVersionMode.Manual)
            {
                EditorGUILayout.PropertyField(manualVersion);
                ValidateManualVersion();
            }
            else if (mode == YooAssetVersionMode.GitCommitCount)
            {
                EditorGUILayout.PropertyField(versionPrefix);
                ValidateVersionPrefix();
            }
            
            // Version Preview
            DrawVersionPreview(mode);

            EditorGUILayout.Space(10);

            // Build Options
            EditorGUILayout.LabelField("Build Options", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(copyToStreamingAssets);
            if (copyToStreamingAssets.boolValue)
            {
                DrawHelpBox(
                    "✓ Copy to StreamingAssets is enabled.\n\n" +
                    "Required for:\n" +
                    "• Offline Mode (Single Player)\n" +
                    "• Host Mode - Base Build (First Install)\n\n" +
                    "The built bundles will be copied to StreamingAssets folder for local access.",
                    MessageType.Info);
            }
            else
            {
                DrawHelpBox(
                    "⚠ Copy to StreamingAssets is disabled.\n\n" +
                    "Suitable for:\n" +
                    "• Host Mode - Patch Build (Hotfix Only)\n\n" +
                    "Note: StreamingAssets will NOT be updated. Make sure you have a base build with bundles in StreamingAssets.",
                    MessageType.Warning);
            }

            EditorGUILayout.Space(5);

            EditorGUILayout.PropertyField(copyToOutputDirectory);
            if (copyToOutputDirectory.boolValue)
            {
                EditorGUILayout.PropertyField(buildOutputDirectory);
                ValidateBuildOutputDirectory();
                DrawHelpBox(
                    "✓ Copy to Output Directory is enabled.\n\n" +
                    "Required for:\n" +
                    "• Host Mode - Patch Build (Upload files to CDN)\n" +
                    "• Backup / Inspecting build artifacts\n\n" +
                    "The built bundles will be copied to the specified output directory for distribution.",
                    MessageType.Info);
            }
            else
            {
                if (!copyToStreamingAssets.boolValue)
                {
                    hasValidationErrors = true;
                    DrawHelpBox(
                        "❌ WARNING: Both copy options are disabled!\n\n" +
                        "How to fix:\n" +
                        "Enable at least one of the following:\n" +
                        "• Copy to StreamingAssets (for local builds)\n" +
                        "• Copy to Output Directory (for CDN distribution)\n\n" +
                        "If both are disabled, build results will only exist in the temporary cache and may be lost.",
                        MessageType.Error);
                }
                else
                {
                    DrawHelpBox(
                        "ℹ Copy to Output Directory is disabled.\n\n" +
                        "Build results will only be copied to StreamingAssets. This is suitable for local-only builds.",
                        MessageType.Info);
                }
            }

            EditorGUILayout.Space(10);
            
            if (GUILayout.Button("Open Build Output Folder"))
            {
                string path = buildOutputDirectory.stringValue;
                if (string.IsNullOrEmpty(path)) path = "Build/HotUpdateBundle";
                
                string fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
                if (Directory.Exists(fullPath))
                {
                    EditorUtility.RevealInFinder(fullPath);
                }
                else
                {
                    Debug.LogWarning($"[YooAssetBuildConfig] Folder not found: {fullPath}");
                }
            }

            // Show validation summary at the end if there are errors
            if (hasValidationErrors)
            {
                EditorGUILayout.Space(5);
                DrawValidationSummary();
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void ValidateManualVersion()
        {
            string version = manualVersion.stringValue;
            if (string.IsNullOrWhiteSpace(version))
            {
                hasValidationErrors = true;
                DrawHelpBox(
                    "❌ Manual Version is required!\n\n" +
                    "How to fill:\n" +
                    "Enter a version string in semantic versioning format.\n\n" +
                    "Correct Examples:\n" +
                    "• 1.0.0\n" +
                    "• 1.2.3\n" +
                    "• 2.0.0-beta.1\n" +
                    "• v1.0.0 (with prefix)\n\n" +
                    "This version will be used as the package version when Version Mode is set to Manual.",
                    MessageType.Error);
            }
            else
            {
                string trimmed = version.Trim();
                if (trimmed.Length == 0)
                {
                    hasValidationErrors = true;
                    DrawHelpBox(
                        "❌ Manual Version cannot be empty!\n\n" +
                        "How to fill:\n" +
                        "Enter a valid version string (e.g., '1.0.0').",
                        MessageType.Error);
                }
                else
                {
                    // Compare with BuildData version
                    string buildDataVersion = GetBuildDataVersion();
                    if (!string.IsNullOrEmpty(buildDataVersion))
                    {
                        string buildDataVersionWithoutV = buildDataVersion.StartsWith("v") ? buildDataVersion.Substring(1) : buildDataVersion;
                        string manualVersionWithoutV = trimmed.StartsWith("v") ? trimmed.Substring(1) : trimmed;
                        
                        // Extract base version (before any commit count suffix)
                        string buildDataBase = buildDataVersionWithoutV.Split('.')[0];
                        string manualBase = manualVersionWithoutV.Split('.')[0];
                        
                        if (buildDataBase != manualBase)
                        {
                            DrawHelpBox(
                                $"⚠ Version mismatch with BuildData!\n\n" +
                                $"BuildData ApplicationVersion: {buildDataVersion}\n" +
                                $"YooAsset Manual Version: {trimmed}\n\n" +
                                "How to fix:\n" +
                                "Consider aligning the YooAsset package version with the BuildData ApplicationVersion for consistency.\n\n" +
                                "Note: The YooAsset package version is independent from the application version, but keeping them aligned helps with version management.",
                                MessageType.Warning);
                        }
                    }
                }
            }
        }

        private void ValidateVersionPrefix()
        {
            string prefix = versionPrefix.stringValue;
            if (string.IsNullOrWhiteSpace(prefix))
            {
                hasValidationErrors = true;
                DrawHelpBox(
                    "❌ Version Prefix is required!\n\n" +
                    "How to fill:\n" +
                    "Enter a version prefix that matches your BuildData ApplicationVersion.\n\n" +
                    "Correct Examples:\n" +
                    "• v1.0 (matches BuildData ApplicationVersion 'v1.0')\n" +
                    "• 1.0 (without 'v' prefix)\n" +
                    "• v2.1 (matches BuildData ApplicationVersion 'v2.1')\n\n" +
                    "The final version will be: {prefix}.{git_commit_count}\n" +
                    "Example: If prefix is 'v1.0' and commit count is 42, the version will be 'v1.0.42'.",
                    MessageType.Error);
            }
            else
            {
                string trimmed = prefix.Trim();
                if (trimmed.Length == 0)
                {
                    hasValidationErrors = true;
                    DrawHelpBox(
                        "❌ Version Prefix cannot be empty!\n\n" +
                        "How to fill:\n" +
                        "Enter a valid version prefix (e.g., 'v1.0').",
                        MessageType.Error);
                }
                else
                {
                    // Compare with BuildData version
                    string buildDataVersion = GetBuildDataVersion();
                    if (!string.IsNullOrEmpty(buildDataVersion))
                    {
                        string prefixWithoutV = trimmed.StartsWith("v") ? trimmed.Substring(1) : trimmed;
                        string buildDataWithoutV = buildDataVersion.StartsWith("v") ? buildDataVersion.Substring(1) : buildDataVersion;
                        
                        // Extract base version (before any commit count suffix)
                        string buildDataBase = buildDataWithoutV.Split('.')[0];
                        string prefixBase = prefixWithoutV.Split('.')[0];
                        
                        if (prefixBase != buildDataBase && trimmed != buildDataVersion)
                        {
                            DrawHelpBox(
                                $"⚠ Version Prefix mismatch with BuildData!\n\n" +
                                $"BuildData ApplicationVersion: {buildDataVersion}\n" +
                                $"YooAsset Version Prefix: {trimmed}\n\n" +
                                "How to fix:\n" +
                                "Update the Version Prefix to match your BuildData ApplicationVersion for consistency.\n\n" +
                                "Example:\n" +
                                $"If BuildData ApplicationVersion is '{buildDataVersion}', set Version Prefix to '{buildDataVersion}' or '{buildDataBase}'.",
                                MessageType.Warning);
                        }
                    }
                    else
                    {
                        // Fallback to PlayerSettings comparison if BuildData is not available
                        string bundleVersion = PlayerSettings.bundleVersion;
                        if (!string.IsNullOrEmpty(bundleVersion))
                        {
                            int lastDotIndex = bundleVersion.LastIndexOf('.');
                            if (lastDotIndex > 0)
                            {
                                string expectedPrefix = bundleVersion.Substring(0, lastDotIndex);
                                string prefixWithoutV = trimmed.StartsWith("v") ? trimmed.Substring(1) : trimmed;
                                string expectedWithoutV = expectedPrefix.StartsWith("v") ? expectedPrefix.Substring(1) : expectedPrefix;

                                if (prefixWithoutV != expectedWithoutV && trimmed != expectedPrefix)
                                {
                                    DrawHelpBox(
                                        $"⚠ Version Prefix mismatch detected!\n\n" +
                                        $"Current prefix: {trimmed}\n" +
                                        $"Project Bundle Version: {bundleVersion}\n" +
                                        $"Expected prefix: {expectedPrefix}\n\n" +
                                        "How to fix:\n" +
                                        "Update the Version Prefix to match your project's bundle version, or update PlayerSettings.bundleVersion to match your prefix.\n\n" +
                                        "Note: It's recommended to use BuildData ApplicationVersion instead of PlayerSettings.bundleVersion for consistency.",
                                        MessageType.Warning);
                                }
                            }
                        }
                    }
                }
            }
        }

        private void ValidateBuildOutputDirectory()
        {
            string path = buildOutputDirectory.stringValue;
            if (string.IsNullOrWhiteSpace(path))
            {
                DrawHelpBox(
                    "ℹ Build Output Directory is empty.\n\n" +
                    "How to fill:\n" +
                    "Enter a path relative to your project root (e.g., 'Build/HotUpdateBundle').\n\n" +
                    "Correct Examples:\n" +
                    "• Build/HotUpdateBundle\n" +
                    "• Build/YooAsset/Bundles\n" +
                    "• Output/Bundles\n\n" +
                    "The directory will be created automatically if it doesn't exist. If left empty, a default path will be used.",
                    MessageType.Info);
            }
            else
            {
                string trimmedPath = path.Trim();
                if (trimmedPath.Length > 0)
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
                            $"❌ Build Output Directory contains invalid character: {charDisplay}\n\n" +
                            "How to fix:\n" +
                            "Remove all invalid characters from the path. Use only letters, numbers, underscores, hyphens, and forward slashes.\n\n" +
                            "Correct Examples:\n" +
                            "• Build/HotUpdateBundle\n" +
                            "• Build/HotUpdate_Bundle\n" +
                            "• Build/HotUpdate-Bundle\n\n" +
                            "Invalid Characters:\n" +
                            "• < > : \" | ? * and other special characters",
                            MessageType.Error);
                    }
                    else if (trimmedPath.Contains(".."))
                    {
                        DrawHelpBox(
                            "⚠ Warning: Build output path contains '..' which may cause issues.\n\n" +
                            "Current value: " + trimmedPath + "\n\n" +
                            "How to fix:\n" +
                            "Use a direct path relative to the project root instead of using '..'.\n\n" +
                            "Correct Examples:\n" +
                            "• Build/HotUpdateBundle (instead of Build/../HotUpdateBundle)\n" +
                            "• Output/Bundles",
                            MessageType.Warning);
                    }
                }
            }
        }

        private void DrawVersionPreview(YooAssetVersionMode mode)
        {
            if (mode == YooAssetVersionMode.Timestamp)
            {
                DrawHelpBox(
                    $"ℹ Version Mode: Timestamp\n\n" +
                    $"Example Version: {System.DateTime.Now:yyyy-MM-dd-HHmmss}\n\n" +
                    "The version will be automatically generated based on the current date and time when building.",
                    MessageType.Info);
            }
            else if (mode == YooAssetVersionMode.GitCommitCount)
            {
                string prefix = versionPrefix.stringValue;
                if (string.IsNullOrWhiteSpace(prefix))
                {
                    DrawHelpBox(
                        "ℹ Version Mode: Git Commit Count\n\n" +
                        "Example Version: v1.0.42 (Requires Git and Version Prefix)\n\n" +
                        "The version will be: {Version Prefix}.{Git Commit Count}\n" +
                        "Make sure your project is a Git repository and Version Prefix is filled.",
                        MessageType.Info);
                }
                else
                {
                    DrawHelpBox(
                        $"ℹ Version Mode: Git Commit Count\n\n" +
                        $"Example Version: {prefix}.42 (Requires Git)\n\n" +
                        $"The version will be: {prefix}.{{Git Commit Count}}\n" +
                        "Make sure your project is a Git repository.",
                        MessageType.Info);
                }
            }
            else if (mode == YooAssetVersionMode.Manual)
            {
                string version = manualVersion.stringValue;
                if (string.IsNullOrWhiteSpace(version))
                {
                    DrawHelpBox(
                        "ℹ Version Mode: Manual\n\n" +
                        "Enter a version string in the Manual Version field above.\n\n" +
                        "Example: 1.0.0, v1.0.0, 2.0.0-beta.1",
                        MessageType.Info);
                }
                else
                {
                    DrawHelpBox(
                        $"ℹ Version Mode: Manual\n\n" +
                        $"Version: {version}\n\n" +
                        "This version will be used as the package version.",
                        MessageType.Info);
                }
            }
        }

        private void DrawValidationSummary()
        {
            DrawHelpBox(
                "⚠ Configuration Issues Detected\n" +
                "Please fix the errors above before building.",
                MessageType.Warning);
        }

        private void DrawHelpBox(string message, MessageType type)
        {
            EditorGUILayout.HelpBox(message, type);
        }

        private void CheckForDuplicates()
        {
            if (!hasCheckedForDuplicates || Event.current.type == EventType.Layout)
            {
                allConfigGuids = AssetDatabase.FindAssets("t:YooAssetBuildConfig");
                hasCheckedForDuplicates = true;
            }
        }

        private void DrawDuplicateWarning()
        {
            if (allConfigGuids != null && allConfigGuids.Length > 1)
            {
                EditorGUILayout.HelpBox(
                    $"⚠ Multiple YooAssetBuildConfig assets detected ({allConfigGuids.Length} found).\n" +
                    "Only one YooAssetBuildConfig should exist in the project. Please delete duplicates.",
                    MessageType.Warning);

                if (GUILayout.Button("Show All in Console"))
                {
                    foreach (var guid in allConfigGuids)
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guid);
                        Debug.Log($"YooAssetBuildConfig found at: {path}");
                    }
                }
                EditorGUILayout.Space(5);
            }
        }
    }
}