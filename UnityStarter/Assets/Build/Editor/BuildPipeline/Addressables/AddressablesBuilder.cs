using System;
using System.IO;
using System.Reflection;
using Build.VersionControl.Editor;
using UnityEditor;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    public static class AddressablesBuilder
    {
        private const string DEBUG_FLAG = "<color=cyan>[Addressables]</color>";

        [MenuItem("Build/Addressables/Build Content (From Config)", priority = 100)]
        public static void BuildFromConfig()
        {
            AddressablesBuildConfig config = GetConfig();
            if (config == null)
            {
                Debug.LogError($"{DEBUG_FLAG} Config not found. Please create an AddressablesBuildConfig asset (CycloneGames/Build/Addressables Build Config).");
                return;
            }

            string contentVersion = GenerateContentVersion(config);
            Debug.Log($"{DEBUG_FLAG} Starting build with version: {contentVersion}");

            Build(EditorUserBuildSettings.activeBuildTarget, contentVersion, config);
        }

        public static void Build(BuildTarget buildTarget, string contentVersion)
        {
            Build(buildTarget, contentVersion, null);
        }

        public static void Build(BuildTarget buildTarget, string contentVersion, AddressablesBuildConfig config)
        {
            Debug.Log($"{DEBUG_FLAG} Checking availability...");

            bool useBuildRemoteCatalog = false;
            bool useCopyToOutputDirectory = true;
            string useBuildOutputDirectory = "";

            if (config != null)
            {
                useBuildRemoteCatalog = config.buildRemoteCatalog;
                useCopyToOutputDirectory = config.copyToOutputDirectory;
                useBuildOutputDirectory = config.buildOutputDirectory;
                Debug.Log($"{DEBUG_FLAG} Using Configuration -> BuildRemoteCatalog: <color={(useBuildRemoteCatalog ? "green" : "red")}>{useBuildRemoteCatalog}</color>, CopyToOutput: <color={(useCopyToOutputDirectory ? "green" : "red")}>{useCopyToOutputDirectory}</color>");
            }
            else
            {
                Debug.LogWarning($"{DEBUG_FLAG} No configuration provided. Using default settings.");
            }

            Type contentBuilderType = ReflectionCache.GetType("UnityEditor.AddressableAssets.Build.ContentPipeline");
            Type buildScriptType = ReflectionCache.GetType("UnityEditor.AddressableAssets.Build.BuildScript");
            Type settingsType = ReflectionCache.GetType("UnityEditor.AddressableAssets.Settings.AddressableAssetSettings");
            Type buildResultType = ReflectionCache.GetType("UnityEditor.AddressableAssets.Build.DataBuildResult");

            if (contentBuilderType == null && buildScriptType == null)
            {
                Debug.LogWarning($"{DEBUG_FLAG} Addressables package not found. Skipping content build.");
                return;
            }

            try
            {
                object settings = GetDefaultSettings(settingsType);
                if (settings == null)
                {
                    Debug.LogError($"{DEBUG_FLAG} Failed to get AddressableAssetSettings. Please ensure Addressables is properly configured.");
                    return;
                }

                // Set content version for hot-update support
                // This ensures the catalog version matches the build version, allowing the runtime to detect updates
                SetContentVersion(settings, settingsType, contentVersion);
                Debug.Log($"{DEBUG_FLAG} Set content version to: {contentVersion}");

                Debug.Log($"{DEBUG_FLAG} Start building Addressables content...");

                object buildResult = null;

                if (contentBuilderType != null)
                {
                    buildResult = BuildWithContentPipeline(contentBuilderType, settings, buildTarget, contentVersion);
                }
                else if (buildScriptType != null)
                {
                    buildResult = BuildWithBuildScript(buildScriptType, settings, buildTarget, contentVersion);
                }

                if (buildResult != null)
                {
                    bool isSuccess = CheckBuildResult(buildResult, buildResultType);
                    if (isSuccess)
                    {
                        Debug.Log($"{DEBUG_FLAG} Build content success!");

                        // Save version data to Addressables build output directory
                        // This ensures:
                        // 1. For full Player build: version file is included when Unity copies Addressables content to StreamingAssets
                        // 2. For hot update build: version file is in build output and will be copied to output directory (if configured)
                        // Returns true if file was auto-created (for cleanup tracking)
                        bool wasAutoCreated = SaveVersionDataToAddressablesBuildPath(contentVersion, buildTarget, settings, settingsType);

                        if (useCopyToOutputDirectory)
                        {
                            CopyBuildResultToOutput(buildTarget, useBuildOutputDirectory, useBuildRemoteCatalog);
                        }
                    }
                    else
                    {
                        string errorInfo = GetBuildError(buildResult, buildResultType);
                        throw new Exception($"[Addressables] Build content failed: {errorInfo}");
                    }
                }
                else
                {
                    Debug.LogError($"{DEBUG_FLAG} Build method returned null result.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"{DEBUG_FLAG} Build failed with exception: {ex}");
                throw;
            }
        }

        private static object GetDefaultSettings(Type settingsType)
        {
            if (settingsType == null) return null;

            MethodInfo defaultMethod = ReflectionCache.GetMethod(settingsType, "Default", BindingFlags.Public | BindingFlags.Static);
            if (defaultMethod != null)
            {
                return defaultMethod.Invoke(null, null);
            }

            PropertyInfo defaultProp = ReflectionCache.GetProperty(settingsType, "Default", BindingFlags.Public | BindingFlags.Static);
            if (defaultProp != null)
            {
                return defaultProp.GetValue(null);
            }

            return null;
        }

        private static void SetContentVersion(object settings, Type settingsType, string contentVersion)
        {
            if (settings == null || settingsType == null || string.IsNullOrEmpty(contentVersion))
                return;

            try
            {
                // Addressables uses OverridePlayerVersion to set the catalog version
                // This version is used to generate the catalog hash, which allows the runtime to detect updates
                PropertyInfo overrideVersionProp = ReflectionCache.GetProperty(settingsType, "OverridePlayerVersion", BindingFlags.Public | BindingFlags.Instance);
                if (overrideVersionProp != null)
                {
                    overrideVersionProp.SetValue(settings, contentVersion);
                    Debug.Log($"{DEBUG_FLAG} Successfully set OverridePlayerVersion to: {contentVersion}");
                }
                else
                {
                    FieldInfo overrideVersionField = ReflectionCache.GetField(settingsType, "m_overridePlayerVersion", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (overrideVersionField != null)
                    {
                        overrideVersionField.SetValue(settings, contentVersion);
                        Debug.Log($"{DEBUG_FLAG} Successfully set m_overridePlayerVersion to: {contentVersion}");
                    }
                    else
                    {
                        Debug.LogWarning($"{DEBUG_FLAG} Could not find OverridePlayerVersion property or field. Version may not be set correctly.");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{DEBUG_FLAG} Failed to set content version: {ex.Message}");
            }
        }

        private static object BuildWithContentPipeline(Type contentBuilderType, object settings, BuildTarget buildTarget, string contentVersion)
        {
            MethodInfo buildMethod = ReflectionCache.GetMethod(contentBuilderType, "BuildContent", BindingFlags.Public | BindingFlags.Static);
            if (buildMethod == null)
            {
                Debug.LogWarning($"{DEBUG_FLAG} BuildContent method not found in ContentPipeline.");
                return null;
            }

            ParameterInfo[] parameters = buildMethod.GetParameters();
            if (parameters.Length >= 2)
            {
                return buildMethod.Invoke(null, new object[] { settings, buildTarget });
            }
            else if (parameters.Length == 1)
            {
                return buildMethod.Invoke(null, new object[] { settings });
            }

            return null;
        }

        private static object BuildWithBuildScript(Type buildScriptType, object settings, BuildTarget buildTarget, string contentVersion)
        {
            MethodInfo buildMethod = ReflectionCache.GetMethod(buildScriptType, "BuildContent", BindingFlags.Public | BindingFlags.Static);
            if (buildMethod == null)
            {
                Debug.LogWarning($"{DEBUG_FLAG} BuildContent method not found in BuildScript.");
                return null;
            }

            ParameterInfo[] parameters = buildMethod.GetParameters();
            if (parameters.Length >= 2)
            {
                return buildMethod.Invoke(null, new object[] { settings, buildTarget });
            }
            else if (parameters.Length == 1)
            {
                return buildMethod.Invoke(null, new object[] { settings });
            }

            return null;
        }

        private static bool CheckBuildResult(object buildResult, Type buildResultType)
        {
            if (buildResult == null) return false;

            Type resultType = buildResult.GetType();
            FieldInfo successField = ReflectionCache.GetField(resultType, "Success", BindingFlags.Public | BindingFlags.Instance);
            if (successField != null)
            {
                return (bool)successField.GetValue(buildResult);
            }

            PropertyInfo successProp = ReflectionCache.GetProperty(resultType, "Success", BindingFlags.Public | BindingFlags.Instance);
            if (successProp != null)
            {
                return (bool)successProp.GetValue(buildResult);
            }

            if (buildResultType != null && resultType == buildResultType)
            {
                FieldInfo errorField = ReflectionCache.GetField(resultType, "Error", BindingFlags.Public | BindingFlags.Instance);
                if (errorField != null)
                {
                    string error = errorField.GetValue(buildResult) as string;
                    return string.IsNullOrEmpty(error);
                }
            }

            Debug.LogWarning($"{DEBUG_FLAG} Could not determine build result success status.");
            return false;
        }

        private static string GetBuildError(object buildResult, Type buildResultType)
        {
            if (buildResult == null) return "Unknown Error";

            Type resultType = buildResult.GetType();
            FieldInfo errorField = ReflectionCache.GetField(resultType, "Error", BindingFlags.Public | BindingFlags.Instance);
            if (errorField != null)
            {
                object val = errorField.GetValue(buildResult);
                if (val != null) return val.ToString();
            }

            PropertyInfo errorProp = ReflectionCache.GetProperty(resultType, "Error", BindingFlags.Public | BindingFlags.Instance);
            if (errorProp != null)
            {
                object val = errorProp.GetValue(buildResult);
                if (val != null) return val.ToString();
            }

            FieldInfo exceptionField = ReflectionCache.GetField(resultType, "Exception", BindingFlags.Public | BindingFlags.Instance);
            if (exceptionField != null)
            {
                object val = exceptionField.GetValue(buildResult);
                if (val != null) return val.ToString();
            }

            return "Unknown Error";
        }

        private static void CopyBuildResultToOutput(BuildTarget buildTarget, string outputDirectory, bool buildRemoteCatalog)
        {
            try
            {
                string targetDir = outputDirectory;
                if (string.IsNullOrEmpty(targetDir))
                {
                    targetDir = "Build/AddressablesContent";
                }

                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string customDestRoot;

                if (targetDir.StartsWith("/"))
                {
                    targetDir = targetDir.Substring(1);
                    customDestRoot = Path.Combine(projectRoot, targetDir);
                }
                else if (Path.IsPathRooted(targetDir))
                {
                    customDestRoot = targetDir;
                }
                else
                {
                    customDestRoot = Path.Combine(projectRoot, targetDir);
                }

                BuildUtils.CreateDirectory(customDestRoot);

                Type settingsType = ReflectionCache.GetType("UnityEditor.AddressableAssets.Settings.AddressableAssetSettings");
                object settings = GetDefaultSettings(settingsType);
                if (settings == null)
                {
                    Debug.LogError($"{DEBUG_FLAG} Failed to get settings for determining build path.");
                    return;
                }

                PropertyInfo buildRemoteCatalogProp = ReflectionCache.GetProperty(settingsType, "BuildRemoteCatalog", BindingFlags.Public | BindingFlags.Instance);
                if (buildRemoteCatalogProp != null)
                {
                    buildRemoteCatalogProp.SetValue(settings, buildRemoteCatalog);
                }

                string buildPath = GetAddressablesBuildPath(settings, settingsType, buildTarget);
                if (string.IsNullOrEmpty(buildPath) || !Directory.Exists(buildPath))
                {
                    Debug.LogWarning($"{DEBUG_FLAG} Addressables build path not found: {buildPath}. Skipping copy.");
                    return;
                }

                string dstDir = Path.Combine(customDestRoot, buildTarget.ToString());
                Debug.Log($"{DEBUG_FLAG} Copying build result from {buildPath} to: {dstDir}");

                if (Directory.Exists(buildPath))
                {
                    // Check if version file exists in build path before copying
                    const string versionFileName = "AddressablesVersion.json";
                    string buildVersionPath = Path.Combine(buildPath, versionFileName);
                    bool versionFileExists = File.Exists(buildVersionPath);

                    if (versionFileExists)
                    {
                        Debug.Log($"{DEBUG_FLAG} Version file found in build path: {buildVersionPath}");
                    }
                    else
                    {
                        Debug.LogWarning($"{DEBUG_FLAG} Version file not found in build path: {buildVersionPath}. It may not be included in the copy.");
                    }

                    BuildUtils.ClearDirectory(dstDir);
                    BuildUtils.CopyAllFilesRecursively(buildPath, dstDir, new string[] { ".meta" });

                    // Verify version file was copied
                    string dstVersionPath = Path.Combine(dstDir, versionFileName);
                    if (File.Exists(dstVersionPath))
                    {
                        Debug.Log($"{DEBUG_FLAG} ✓ Successfully copied build result to output directory.");
                        Debug.Log($"{DEBUG_FLAG} ✓ Version file verified in output directory: {dstVersionPath}");
                    }
                    else if (versionFileExists)
                    {
                        Debug.LogWarning($"{DEBUG_FLAG} ⚠ Version file was in build path but not found in output directory: {dstVersionPath}");
                    }
                    else
                    {
                        Debug.Log($"{DEBUG_FLAG} Successfully copied build result to output directory.");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"{DEBUG_FLAG} Failed to copy build result to custom directory: {ex.Message}");
            }
        }

        private static string GetAddressablesBuildPath(object settings, Type settingsType, BuildTarget buildTarget)
        {
            PropertyInfo buildPathProp = ReflectionCache.GetProperty(settingsType, "BuildRemoteCatalog", BindingFlags.Public | BindingFlags.Instance);
            bool isRemote = buildPathProp != null && (bool)buildPathProp.GetValue(settings);

            PropertyInfo profileProp = ReflectionCache.GetProperty(settingsType, "profileSettings", BindingFlags.Public | BindingFlags.Instance);
            if (profileProp == null)
            {
                profileProp = ReflectionCache.GetProperty(settingsType, "ProfileSettings", BindingFlags.Public | BindingFlags.Instance);
            }

            object profileSettings = profileProp?.GetValue(settings);
            if (profileSettings == null) return null;

            Type profileSettingsType = profileSettings.GetType();
            MethodInfo getValueMethod = ReflectionCache.GetMethod(profileSettingsType, "GetValueByName", BindingFlags.Public | BindingFlags.Instance);
            if (getValueMethod == null) return null;

            string buildPathVar = isRemote ? "Remote.BuildPath" : "Local.BuildPath";
            object buildPathObj = getValueMethod.Invoke(profileSettings, new object[] { buildPathVar });

            if (buildPathObj == null) return null;

            string buildPath = buildPathObj.ToString();
            if (string.IsNullOrEmpty(buildPath)) return null;

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            if (Path.IsPathRooted(buildPath))
            {
                return Path.Combine(buildPath, buildTarget.ToString());
            }

            return Path.Combine(projectRoot, buildPath, buildTarget.ToString());
        }

        private static AddressablesBuildConfig GetConfig()
        {
            return BuildConfigHelper.GetAddressablesConfig();
        }

        private static string GenerateContentVersion(AddressablesBuildConfig config)
        {
            if (config.versionMode == AddressablesVersionMode.Manual)
            {
                return config.manualVersion;
            }
            else if (config.versionMode == AddressablesVersionMode.Timestamp)
            {
                return DateTime.Now.ToString("yyyy-MM-dd-HHmmss");
            }
            else
            {
                IVersionControlProvider provider = VersionControlFactory.CreateProvider(VersionControlType.Git);
                string count = provider.GetCommitCount();
                if (string.IsNullOrEmpty(count)) count = "0";
                return $"{config.versionPrefix}.{count}";
            }
        }

        /// <summary>
        /// Saves version data to Addressables build output path.
        /// 
        /// For full Player build:
        ///   Unity automatically copies Addressables build output to StreamingAssets during Player build,
        ///   so the version file will be included in the final build.
        /// 
        /// For hot update build (HotUpdateBuilder):
        ///   Version file is saved to build output path, and will be copied to output directory
        ///   (if copyToOutputDirectory is enabled) for deployment to server.
        /// </summary>
        private static bool SaveVersionDataToAddressablesBuildPath(string contentVersion, BuildTarget buildTarget, object settings, Type settingsType)
        {
            try
            {
                // Get the actual Addressables build output path
                // This is where Addressables stores built content
                // - For full build: Unity copies this to StreamingAssets during Player build
                // - For hot update: This is copied to output directory for deployment
                string buildPath = GetAddressablesBuildPath(settings, settingsType, buildTarget);
                if (string.IsNullOrEmpty(buildPath) || !Directory.Exists(buildPath))
                {
                    Debug.LogWarning($"{DEBUG_FLAG} Addressables build path not found: {buildPath}. Cannot save version file.");
                    return false;
                }

                const string versionFileName = "AddressablesVersion.json";
                string versionFilePath = Path.Combine(buildPath, versionFileName);

                // Check if file already exists (user-created)
                bool fileExisted = File.Exists(versionFilePath);

                // Create directory structure if needed (should already exist from Addressables build)
                string directory = Path.GetDirectoryName(versionFilePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var versionData = new VersionDataJson { contentVersion = contentVersion };
                string jsonContent = JsonUtility.ToJson(versionData, true);
                File.WriteAllText(versionFilePath, jsonContent);

                // Mark as auto-created if it didn't exist before
                // Store flag in StreamingAssets path for cleanup tracking (since build path may be outside project)
                if (!fileExisted)
                {
                    string streamingAssetsVersionPath = GetStreamingAssetsVersionPathForCleanup(buildTarget);
                    string autoCreatedFlagPath = $"{streamingAssetsVersionPath}.autocreated";
                    string flagDir = Path.GetDirectoryName(autoCreatedFlagPath);
                    if (!Directory.Exists(flagDir))
                    {
                        Directory.CreateDirectory(flagDir);
                    }
                    File.WriteAllText(autoCreatedFlagPath, "true");
                }

                Debug.Log($"{DEBUG_FLAG} Saved version data to Addressables build path: {versionFilePath} ({(fileExisted ? "existing" : "auto-created")})");
                Debug.Log($"{DEBUG_FLAG} Version file content: {jsonContent}");
                Debug.Log($"{DEBUG_FLAG} Version file will be:");
                Debug.Log($"{DEBUG_FLAG}   - Copied to StreamingAssets/aa/{buildTarget} during Player build (full build)");
                Debug.Log($"{DEBUG_FLAG}   - Copied to output directory for hot update deployment (if copyToOutputDirectory enabled)");

                // Verify file was actually written
                if (File.Exists(versionFilePath))
                {
                    Debug.Log($"{DEBUG_FLAG} ✓ Version file verified at: {versionFilePath}");
                }
                else
                {
                    Debug.LogError($"{DEBUG_FLAG} ✗ Version file NOT found after write attempt: {versionFilePath}");
                }

                return !fileExisted; // Return true if auto-created
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{DEBUG_FLAG} Failed to save version data to Addressables build path: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets the StreamingAssets path where version file will be located after Player build.
        /// Used for cleanup tracking.
        /// </summary>
        private static string GetStreamingAssetsVersionPathForCleanup(BuildTarget buildTarget)
        {
            const string streamingAssetsPath = "Assets/StreamingAssets";
            const string addressablesFolder = "aa";
            string platformFolder = buildTarget.ToString();
            const string versionFileName = "AddressablesVersion.json";
            return Path.Combine(streamingAssetsPath, addressablesFolder, platformFolder, versionFileName);
        }

        /// <summary>
        /// Cleans up auto-created version files after build.
        /// Only removes files that were automatically created during build process.
        /// User-created files are preserved.
        /// 
        /// This should be called after a full build (not after resource-only builds),
        /// as the version file is needed during the build process.
        /// </summary>
        public static void CleanupAutoCreatedVersionFiles()
        {
            try
            {
                // Check all platform folders in StreamingAssets/aa/
                // These files are created by Unity during Player build from Addressables build output
                const string streamingAssetsPath = "Assets/StreamingAssets";
                const string addressablesFolder = "aa";
                const string versionFileName = "AddressablesVersion.json";

                string addressablesRoot = Path.Combine(streamingAssetsPath, addressablesFolder);

                if (!Directory.Exists(addressablesRoot))
                {
                    return; // No Addressables folder, nothing to clean
                }

                // Search for version files in all platform subdirectories
                string[] platformDirs = Directory.GetDirectories(addressablesRoot);
                foreach (string platformDir in platformDirs)
                {
                    string versionFilePath = Path.Combine(platformDir, versionFileName);
                    string versionMetaPath = $"{versionFilePath}.meta";
                    string autoCreatedFlagPath = $"{versionFilePath}.autocreated";

                    // Only delete if it was auto-created (flag file exists)
                    if (File.Exists(autoCreatedFlagPath))
                    {
                        // Delete the version file
                        if (File.Exists(versionFilePath))
                        {
                            File.Delete(versionFilePath);
                            Debug.Log($"{DEBUG_FLAG} Cleaned up auto-created version file: {versionFilePath}");
                        }

                        // Delete the meta file if it exists
                        if (File.Exists(versionMetaPath))
                        {
                            File.Delete(versionMetaPath);
                            Debug.Log($"{DEBUG_FLAG} Cleaned up auto-created version meta file: {versionMetaPath}");
                        }

                        // Delete the flag file
                        File.Delete(autoCreatedFlagPath);
                    }
                }

                AssetDatabase.Refresh();
                Debug.Log($"{DEBUG_FLAG} Cleanup completed for auto-created version files.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{DEBUG_FLAG} Failed to cleanup auto-created version files: {ex.Message}");
            }
        }

        [System.Serializable]
        private class VersionDataJson
        {
            public string contentVersion;
        }
    }
}