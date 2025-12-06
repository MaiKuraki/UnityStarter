using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Build.VersionControl.Editor;

namespace Build.Pipeline.Editor
{
    /// <summary>
    /// Build processor to ensure Addressables version file is saved after Unity builds Addressables content
    /// and copied to StreamingAssets during Player build.
    /// </summary>
    public class AddressablesVersionBuildProcessor : BuildPlayerProcessor, IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        // Unity's AddressablesPlayerBuildProcessor has callbackOrder = 1
        // IPreprocessBuildWithReport callback order (lower = earlier)
        // This runs before the actual build starts, ensuring version file is in StreamingAssets
        public override int callbackOrder => 0;

        const string versionFileName = "AddressablesVersion.json";

        public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
        {
            BuildData buildData = BuildConfigHelper.GetBuildData();
            if (buildData == null || !buildData.UseAddressables)
            {
                return; // Not using Addressables, skip
            }

            try
            {
                AddressablesBuildConfig config = BuildConfigHelper.GetAddressablesConfig();
                if (config == null)
                {
                    Debug.LogWarning("[AddressablesVersionBuildProcessor] AddressablesBuildConfig not found. Version file may not be created.");
                    return;
                }

                BuildTarget buildTarget = EditorUserBuildSettings.activeBuildTarget;

                Type settingsType = ReflectionCache.GetType("UnityEditor.AddressableAssets.Settings.AddressableAssetSettings");
                if (settingsType == null)
                {
                    Debug.LogWarning("[AddressablesVersionBuildProcessor] Addressables settings type not found.");
                    return;
                }

                object settings = GetDefaultSettings(settingsType);
                if (settings == null)
                {
                    Debug.LogWarning("[AddressablesVersionBuildProcessor] Failed to get Addressables settings. Version file will be created in PostprocessBuild.");
                    return;
                }

                // Get the Addressables build output directory
                // Unity's AddressablesPlayerBuildProcessor (callbackOrder = 1) should have completed by now,
                // so the build output should exist. If it doesn't, we'll create the version file in PostprocessBuild.
                string bundleDirectory = GetAddressablesBuildPath(settings, settingsType, buildTarget);

                if (string.IsNullOrEmpty(bundleDirectory))
                {
                    Debug.LogWarning("[AddressablesVersionBuildProcessor] Bundle directory path is empty. Version file will be created in PostprocessBuild.");
                    return;
                }

                if (!Directory.Exists(bundleDirectory))
                {
                    Debug.LogWarning($"[AddressablesVersionBuildProcessor] Bundle directory not found: {bundleDirectory}. This may happen if Addressables build hasn't completed yet. Version file will be created in PostprocessBuild.");
                    return;
                }

                // Verify that Addressables build output exists
                // If no catalog files exist, Addressables build may not have completed yet
                string[] catalogFiles = Directory.GetFiles(bundleDirectory, "catalog_*.json", SearchOption.TopDirectoryOnly);
                if (catalogFiles.Length == 0)
                {
                    catalogFiles = Directory.GetFiles(bundleDirectory, "*.json", SearchOption.TopDirectoryOnly);
                    if (catalogFiles.Length == 0)
                    {
                        Debug.LogWarning($"[AddressablesVersionBuildProcessor] No catalog files found in bundle directory: {bundleDirectory}. Addressables build may not have completed yet. Version file will be created in PostprocessBuild.");
                        return;
                    }
                }

                Debug.Log($"[AddressablesVersionBuildProcessor] Bundle directory found: {bundleDirectory}");

                // Generate version from config
                string contentVersion = GenerateContentVersion(config);
                string versionFilePath = Path.Combine(bundleDirectory, versionFileName);

                if (File.Exists(versionFilePath))
                {
                    try
                    {
                        string existingJson = File.ReadAllText(versionFilePath);
                        VersionDataJson existingData = JsonUtility.FromJson<VersionDataJson>(existingJson);
                        if (existingData != null && !string.IsNullOrEmpty(existingData.contentVersion))
                        {
                            contentVersion = existingData.contentVersion;
                            Debug.Log($"[AddressablesVersionBuildProcessor] Using existing version from pre-build: {contentVersion}");
                        }
                    }
                    catch
                    {
                        // If we can't read existing version, use generated one
                    }
                }

                // Save version file to bundle directory (same location as bundle files)
                // Unity copies the BuildPath contents to StreamingAssets/aa during Player build
                // Since bundleDirectory is BuildPath/{BuildTarget}, Unity will copy it to StreamingAssets/aa/{BuildTarget}
                // So the file will be at: StreamingAssets/aa/{BuildTarget}/{versionFileName}
                try
                {
                    var versionData = new VersionDataJson { contentVersion = contentVersion };
                    string jsonContent = JsonUtility.ToJson(versionData, true);
                    File.WriteAllText(versionFilePath, jsonContent);

                    // Verify file was written
                    if (File.Exists(versionFilePath))
                    {
                        Debug.Log($"[AddressablesVersionBuildProcessor] ✓ Saved version file to bundle directory: {versionFilePath}");
                        Debug.Log($"[AddressablesVersionBuildProcessor] Version: {contentVersion}");
                        Debug.Log($"[AddressablesVersionBuildProcessor] Unity will copy this to StreamingAssets/aa/{buildTarget}/{versionFileName} during Player build.");
                    }
                    else
                    {
                        Debug.LogWarning($"[AddressablesVersionBuildProcessor] Version file was written but not found at: {versionFilePath}. Will create in StreamingAssets directly.");
                    }

                    // For Android and other platforms, we also need to ensure the version file
                    // is in Assets/StreamingAssets/aa/{BuildTarget}/ before Unity packages the build.
                    string streamingAssetsPath = Path.Combine(Application.dataPath, "StreamingAssets");
                    string streamingAssetsVersionDir = Path.Combine(streamingAssetsPath, "aa", buildTarget.ToString());
                    string streamingAssetsVersionPath = Path.Combine(streamingAssetsVersionDir, versionFileName);

                    // Also try platform-specific path (e.g., Android instead of StandaloneAndroid)
                    string platformPath = GetAddressablesPlatformPath(buildTarget);
                    string streamingAssetsPlatformVersionDir = Path.Combine(streamingAssetsPath, "aa", platformPath);
                    string streamingAssetsPlatformVersionPath = Path.Combine(streamingAssetsPlatformVersionDir, versionFileName);

                    // Copy to both possible locations to ensure it's found
                    bool copiedToStreamingAssets = false;
                    if (Directory.Exists(streamingAssetsVersionDir) || Directory.Exists(Path.GetDirectoryName(streamingAssetsVersionDir)))
                    {
                        if (!Directory.Exists(streamingAssetsVersionDir))
                        {
                            Directory.CreateDirectory(streamingAssetsVersionDir);
                        }
                        File.Copy(versionFilePath, streamingAssetsVersionPath, true);
                        copiedToStreamingAssets = true;
                        Debug.Log($"[AddressablesVersionBuildProcessor] ✓ Copied version file to StreamingAssets: {streamingAssetsVersionPath}");
                    }

                    if (Directory.Exists(streamingAssetsPlatformVersionDir) || Directory.Exists(Path.GetDirectoryName(streamingAssetsPlatformVersionDir)))
                    {
                        if (!Directory.Exists(streamingAssetsPlatformVersionDir))
                        {
                            Directory.CreateDirectory(streamingAssetsPlatformVersionDir);
                        }
                        File.Copy(versionFilePath, streamingAssetsPlatformVersionPath, true);
                        copiedToStreamingAssets = true;
                        Debug.Log($"[AddressablesVersionBuildProcessor] ✓ Copied version file to StreamingAssets (platform path): {streamingAssetsPlatformVersionPath}");
                    }

                    if (!copiedToStreamingAssets)
                    {
                        // If directories don't exist yet, create them and copy anyway
                        // Unity will copy these when it processes StreamingAssets
                        Directory.CreateDirectory(streamingAssetsVersionDir);
                        File.Copy(versionFilePath, streamingAssetsVersionPath, true);
                        Debug.Log($"[AddressablesVersionBuildProcessor] ✓ Created and copied version file to StreamingAssets: {streamingAssetsVersionPath}");
                    }
                }
                catch (IOException ioEx)
                {
                    // File might be locked, but that's okay - we'll create it in PostprocessBuild
                    Debug.LogWarning($"[AddressablesVersionBuildProcessor] Could not write version file (may be locked): {ioEx.Message}. Will create in PostprocessBuild.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AddressablesVersionBuildProcessor] Failed to save version file in PrepareForBuild: {ex.Message}. Will create in PostprocessBuild.");
            }
        }

        /// <summary>
        /// Pre-process build to ensure version file exists in StreamingAssets before build starts.
        /// This is critical for Android and other platforms where we can't modify the build after packaging.
        /// </summary>
        public void OnPreprocessBuild(BuildReport report)
        {
            BuildData buildData = BuildConfigHelper.GetBuildData();
            if (buildData == null || !buildData.UseAddressables)
            {
                return; // Not using Addressables, skip
            }

            try
            {
                AddressablesBuildConfig config = BuildConfigHelper.GetAddressablesConfig();
                if (config == null)
                {
                    return;
                }

                BuildTarget buildTarget = report.summary.platform;

                // Get the Addressables build output path
                Type settingsType = ReflectionCache.GetType("UnityEditor.AddressableAssets.Settings.AddressableAssetSettings");
                if (settingsType == null)
                {
                    return;
                }

                object settings = GetDefaultSettings(settingsType);
                if (settings == null)
                {
                    return;
                }

                string bundleDirectory = GetAddressablesBuildPath(settings, settingsType, buildTarget);
                if (string.IsNullOrEmpty(bundleDirectory) || !Directory.Exists(bundleDirectory))
                {
                    // Build output may not exist yet, that's okay
                    return;
                }

                string buildVersionPath = Path.Combine(bundleDirectory, versionFileName);

                if (!File.Exists(buildVersionPath))
                {
                    // Version file doesn't exist in build output, generate it
                    string contentVersion = GenerateContentVersion(config);
                    var versionData = new VersionDataJson { contentVersion = contentVersion };
                    string jsonContent = JsonUtility.ToJson(versionData, true);
                    File.WriteAllText(buildVersionPath, jsonContent);
                    Debug.Log($"[AddressablesVersionBuildProcessor] ✓ Generated version file in build output: {buildVersionPath}");
                }

                // Ensure version file is in StreamingAssets (critical for Android)
                string streamingAssetsPath = Path.Combine(Application.dataPath, "StreamingAssets");
                string platformPath = GetAddressablesPlatformPath(buildTarget);

                // Try both BuildTarget and platform-specific paths
                string[] targetPaths = new string[] { buildTarget.ToString(), platformPath };

                foreach (string targetPath in targetPaths)
                {
                    string streamingAssetsVersionDir = Path.Combine(streamingAssetsPath, "aa", targetPath);
                    string streamingAssetsVersionPath = Path.Combine(streamingAssetsVersionDir, versionFileName);

                    // Copy version file to StreamingAssets if it doesn't exist or is different
                    bool needsCopy = true;
                    if (File.Exists(streamingAssetsVersionPath))
                    {
                        try
                        {
                            string existingContent = File.ReadAllText(streamingAssetsVersionPath);
                            string buildContent = File.ReadAllText(buildVersionPath);
                            if (existingContent == buildContent)
                            {
                                needsCopy = false;
                            }
                        }
                        catch
                        {
                            // If we can't compare, copy anyway
                        }
                    }

                    if (needsCopy)
                    {
                        if (!Directory.Exists(streamingAssetsVersionDir))
                        {
                            Directory.CreateDirectory(streamingAssetsVersionDir);
                        }
                        File.Copy(buildVersionPath, streamingAssetsVersionPath, true);
                        Debug.Log($"[AddressablesVersionBuildProcessor] ✓ Ensured version file in StreamingAssets: {streamingAssetsVersionPath}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AddressablesVersionBuildProcessor] Failed to ensure version file in StreamingAssets during PreprocessBuild: {ex.Message}");
            }
        }

        /// <summary>
        /// Post-process build to ensure version file exists in the built player's StreamingAssets.
        /// This handles cases where Unity rebuilds Addressables during Player build and overwrites our version file.
        /// </summary>
        public void OnPostprocessBuild(BuildReport report)
        {
            BuildData buildData = BuildConfigHelper.GetBuildData();
            if (buildData == null || !buildData.UseAddressables)
            {
                return; // Not using Addressables, skip
            }

            try
            {
                // Get Addressables build config
                AddressablesBuildConfig config = BuildConfigHelper.GetAddressablesConfig();
                if (config == null)
                {
                    return;
                }

                BuildTarget buildTarget = report.summary.platform;

                // Get the built player's output directory
                string outputPath = report.summary.outputPath;
                if (string.IsNullOrEmpty(outputPath))
                {
                    return;
                }

                // Determine StreamingAssets path in built player
                // For Windows: outputPath is .exe, StreamingAssets is in outputPath_Data/StreamingAssets
                // For Android: outputPath is .apk or .aab (compressed), we can't modify it after build
                // For folders: StreamingAssets is in outputPath/StreamingAssets
                string playerStreamingAssetsPath = null;

                if (buildTarget == BuildTarget.Android)
                {
                    // For Android, the APK/AAB is already built and we can't modify it
                    // The version file should have been copied to Assets/StreamingAssets during PrepareForBuild
                    // Just verify it exists in the source StreamingAssets folder
                    string sourceStreamingAssetsPath = Path.Combine(Application.dataPath, "StreamingAssets");
                    string sourceVersionPath = Path.Combine(sourceStreamingAssetsPath, "aa", buildTarget.ToString(), versionFileName);
                    string platformPath = GetAddressablesPlatformPath(buildTarget);
                    string sourcePlatformVersionPath = Path.Combine(sourceStreamingAssetsPath, "aa", platformPath, versionFileName);

                    if (File.Exists(sourceVersionPath))
                    {
                        Debug.Log($"[AddressablesVersionBuildProcessor] ✓ Version file verified in source StreamingAssets: {sourceVersionPath}");
                        Debug.Log($"[AddressablesVersionBuildProcessor] Note: For Android APK/AAB, version file is already packaged. Cannot verify in built APK.");
                        return;
                    }
                    else if (File.Exists(sourcePlatformVersionPath))
                    {
                        Debug.Log($"[AddressablesVersionBuildProcessor] ✓ Version file verified in source StreamingAssets (platform path): {sourcePlatformVersionPath}");
                        Debug.Log($"[AddressablesVersionBuildProcessor] Note: For Android APK/AAB, version file is already packaged. Cannot verify in built APK.");
                        return;
                    }
                    else
                    {
                        Debug.LogWarning($"[AddressablesVersionBuildProcessor] Version file not found in source StreamingAssets. Creating it now (may be too late for current build).");
                        // Try to create it anyway, though it may be too late for the current build
                        string contentVersionStr = GenerateContentVersion(config);
                        if (!Directory.Exists(Path.GetDirectoryName(sourceVersionPath)))
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(sourceVersionPath));
                        }
                        var versionDataJson = new VersionDataJson { contentVersion = contentVersionStr };
                        string jsonContentStr = JsonUtility.ToJson(versionDataJson, true);
                        File.WriteAllText(sourceVersionPath, jsonContentStr);
                        Debug.Log($"[AddressablesVersionBuildProcessor] ✓ Created version file in source StreamingAssets: {sourceVersionPath}");
                        return;
                    }
                }
                else if (File.Exists(outputPath) && outputPath.EndsWith(".exe"))
                {
                    // Windows executable: StreamingAssets is in {exe}_Data/StreamingAssets
                    string dataPath = outputPath.Replace(".exe", "_Data");
                    playerStreamingAssetsPath = Path.Combine(dataPath, "StreamingAssets");
                }
                else if (Directory.Exists(outputPath))
                {
                    // Folder build: StreamingAssets is in outputPath/StreamingAssets
                    playerStreamingAssetsPath = Path.Combine(outputPath, "StreamingAssets");
                }
                else
                {
                    Debug.LogWarning($"[AddressablesVersionBuildProcessor] Cannot determine player StreamingAssets path from: {outputPath}");
                    return;
                }

                if (string.IsNullOrEmpty(playerStreamingAssetsPath))
                {
                    return;
                }

                // Version file should be in the bundle directory (same as bundle files)
                // Path: StreamingAssets/aa/{BuildTarget}/{versionFileName}
                // Unity copies BuildPath contents to StreamingAssets/aa, so the BuildTarget subdirectory
                // becomes StreamingAssets/aa/{BuildTarget}
                string playerVersionPath = Path.Combine(playerStreamingAssetsPath, "aa", buildTarget.ToString(), versionFileName);
                string addressablesPlatformPath = GetAddressablesPlatformPath(buildTarget);
                string fallbackVersionPath = Path.Combine(playerStreamingAssetsPath, "aa", addressablesPlatformPath, versionFileName);

                if (File.Exists(playerVersionPath))
                {
                    Debug.Log($"[AddressablesVersionBuildProcessor] ✓ Version file verified in built player: {playerVersionPath}");
                    return;
                }

                string foundFallbackPath = null;
                if (File.Exists(fallbackVersionPath))
                {
                    foundFallbackPath = fallbackVersionPath;
                }

                string playerVersionDir = Path.GetDirectoryName(playerVersionPath);
                if (foundFallbackPath != null)
                {
                    Debug.Log($"[AddressablesVersionBuildProcessor] ✓ Version file found in fallback location: {foundFallbackPath}");

                    if (!Directory.Exists(playerVersionDir))
                    {
                        Directory.CreateDirectory(playerVersionDir);
                    }
                    File.Copy(foundFallbackPath, playerVersionPath, true);
                    Debug.Log($"[AddressablesVersionBuildProcessor] ✓ Moved version file to bundle directory: {playerVersionPath}");
                    return;
                }

                // Version file doesn't exist, create it in bundle directory
                Debug.LogWarning($"[AddressablesVersionBuildProcessor] Version file not found in built player, creating: {playerVersionPath}");

                string contentVersion = GenerateContentVersion(config);
                if (!Directory.Exists(playerVersionDir))
                {
                    Directory.CreateDirectory(playerVersionDir);
                }

                var versionData = new VersionDataJson { contentVersion = contentVersion };
                string jsonContent = JsonUtility.ToJson(versionData, true);
                File.WriteAllText(playerVersionPath, jsonContent);
                Debug.Log($"[AddressablesVersionBuildProcessor] ✓ Created version file in built player: {playerVersionPath} (Version: {contentVersion})");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AddressablesVersionBuildProcessor] Failed to ensure version file in built player: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the Addressables build path for bundle files.
        /// Uses Addressables.BuildPath static property and platform mapping for consistency.
        /// </summary>
        private static string GetAddressablesBuildPath(object settings, Type settingsType, BuildTarget buildTarget)
        {
            try
            {
                // Try using Addressables.BuildPath static property (most reliable)
                Type addressablesType = ReflectionCache.GetType("UnityEngine.AddressableAssets.Addressables");
                if (addressablesType != null)
                {
                    PropertyInfo buildPathProp = ReflectionCache.GetProperty(addressablesType, "BuildPath", BindingFlags.Public | BindingFlags.Static);
                    if (buildPathProp != null)
                    {
                        object buildPathObj = buildPathProp.GetValue(null);
                        if (buildPathObj != null)
                        {
                            string buildPath = buildPathObj.ToString();
                            if (!string.IsNullOrEmpty(buildPath))
                            {
                                // BuildPath typically returns something like "Library/com.unity.addressables/aa/Windows"
                                // We need to append the BuildTarget subdirectory (e.g., "StandaloneWindows64")
                                string fullPath = Path.Combine(buildPath, buildTarget.ToString());
                                if (Directory.Exists(fullPath))
                                {
                                    return fullPath;
                                }
                                // If BuildTarget subdirectory doesn't exist, return the base path
                                if (Directory.Exists(buildPath))
                                {
                                    return buildPath;
                                }
                            }
                        }
                    }
                }

                // Fallback to ProfileSettings with activeProfileId
                PropertyInfo profileProp = ReflectionCache.GetProperty(settingsType, "profileSettings", BindingFlags.Public | BindingFlags.Instance);
                if (profileProp == null)
                {
                    profileProp = ReflectionCache.GetProperty(settingsType, "ProfileSettings", BindingFlags.Public | BindingFlags.Instance);
                }

                object profileSettings = profileProp?.GetValue(settings);
                if (profileSettings != null)
                {
                    PropertyInfo activeProfileIdProp = ReflectionCache.GetProperty(settingsType, "activeProfileId", BindingFlags.Public | BindingFlags.Instance);
                    string activeProfileId = activeProfileIdProp?.GetValue(settings)?.ToString();

                    if (!string.IsNullOrEmpty(activeProfileId))
                    {
                        Type profileSettingsType = profileSettings.GetType();

                        // Try EvaluateString to resolve variables
                        MethodInfo evaluateStringMethod = ReflectionCache.GetMethod(profileSettingsType, "EvaluateString", BindingFlags.Public | BindingFlags.Instance, new Type[] { typeof(string), typeof(string) });
                        if (evaluateStringMethod != null)
                        {
                            PropertyInfo buildRemoteCatalogProp = ReflectionCache.GetProperty(settingsType, "BuildRemoteCatalog", BindingFlags.Public | BindingFlags.Instance);
                            bool isRemote = buildRemoteCatalogProp != null && (bool)buildRemoteCatalogProp.GetValue(settings);
                            string buildPathVar = isRemote ? "Remote.BuildPath" : "Local.BuildPath";

                            MethodInfo getValueMethod = ReflectionCache.GetMethod(profileSettingsType, "GetValueByName", BindingFlags.Public | BindingFlags.Instance, new Type[] { typeof(string), typeof(string) });
                            if (getValueMethod != null)
                            {
                                string rawValue = getValueMethod.Invoke(profileSettings, new object[] { activeProfileId, buildPathVar })?.ToString();
                                if (!string.IsNullOrEmpty(rawValue))
                                {
                                    string evaluatedPath = evaluateStringMethod.Invoke(profileSettings, new object[] { activeProfileId, rawValue })?.ToString();
                                    if (!string.IsNullOrEmpty(evaluatedPath))
                                    {
                                        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                                        if (Path.IsPathRooted(evaluatedPath))
                                        {
                                            return evaluatedPath;
                                        }
                                        return Path.Combine(projectRoot, evaluatedPath);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AddressablesVersionBuildProcessor] Failed to get Addressables build path: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Gets the default Addressables settings instance.
        /// Uses the same logic as AddressablesBuilder.GetDefaultSettings to ensure consistency.
        /// </summary>
        private static object GetDefaultSettings(Type settingsType)
        {
            if (settingsType == null) return null;

            // Try AddressableAssetSettingsDefaultObject.Settings property first (correct API for 2.7.6)
            Type defaultObjectType = ReflectionCache.GetType("UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject");
            if (defaultObjectType != null)
            {
                // Try Settings property first (returns existing settings or null)
                PropertyInfo settingsProp = ReflectionCache.GetProperty(defaultObjectType, "Settings", BindingFlags.Public | BindingFlags.Static);
                if (settingsProp != null)
                {
                    try
                    {
                        object settings = settingsProp.GetValue(null);
                        if (settings != null)
                        {
                            return settings;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[AddressablesVersionBuildProcessor] Failed to get Settings property: {ex.Message}");
                    }
                }

                // Fallback: Try GetSettings(bool create) method
                MethodInfo getSettingsMethod = ReflectionCache.GetMethod(defaultObjectType, "GetSettings", BindingFlags.Public | BindingFlags.Static, new Type[] { typeof(bool) });
                if (getSettingsMethod == null)
                {
                    getSettingsMethod = ReflectionCache.GetMethod(defaultObjectType, "GetSettings", BindingFlags.Public | BindingFlags.Static);
                }

                if (getSettingsMethod != null)
                {
                    try
                    {
                        ParameterInfo[] parameters = getSettingsMethod.GetParameters();
                        if (parameters.Length == 1 && parameters[0].ParameterType == typeof(bool))
                        {
                            return getSettingsMethod.Invoke(null, new object[] { false });
                        }
                        else if (parameters.Length == 0)
                        {
                            return getSettingsMethod.Invoke(null, null);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[AddressablesVersionBuildProcessor] Failed to invoke GetSettings(false): {ex.Message}");
                        try
                        {
                            return getSettingsMethod.Invoke(null, new object[] { true });
                        }
                        catch
                        {
                            // Fall through to other methods
                        }
                    }
                }
            }

            // Fallback: Try AddressableAssetSettings.Default (older API or alternative)
            MethodInfo defaultMethod = ReflectionCache.GetMethod(settingsType, "Default", BindingFlags.Public | BindingFlags.Static);
            if (defaultMethod != null)
            {
                try
                {
                    return defaultMethod.Invoke(null, null);
                }
                catch
                {
                    // Continue to property fallback
                }
            }

            PropertyInfo defaultProp = ReflectionCache.GetProperty(settingsType, "Default", BindingFlags.Public | BindingFlags.Static);
            if (defaultProp != null)
            {
                try
                {
                    return defaultProp.GetValue(null);
                }
                catch
                {
                    // Return null if all methods fail
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the Addressables platform path for a given BuildTarget.
        /// This matches Unity's PlatformMappingService logic to ensure path consistency.
        /// </summary>
        private static string GetAddressablesPlatformPath(BuildTarget buildTarget)
        {
            // Use Unity's PlatformMappingService to get the correct platform path
            // This ensures consistency: StandaloneWindows64 -> "Windows", StandaloneOSX -> "OSX", etc.
            try
            {
                Type platformMappingType = ReflectionCache.GetType("UnityEngine.AddressableAssets.PlatformMappingService");
                if (platformMappingType != null)
                {
                    MethodInfo method = ReflectionCache.GetMethod(platformMappingType, "GetAddressablesPlatformPathInternal",
                        BindingFlags.NonPublic | BindingFlags.Static);
                    if (method != null)
                    {
                        string platformPath = method.Invoke(null, new object[] { buildTarget })?.ToString();
                        if (!string.IsNullOrEmpty(platformPath))
                        {
                            return platformPath;
                        }
                    }
                }
            }
            catch
            {
                // Fall through to hardcoded mapping
            }

            // Fallback to hardcoded mapping (matches Unity's PlatformMappingService)
            switch (buildTarget)
            {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    return "Windows";
                case BuildTarget.StandaloneOSX:
                    return "OSX";
                case BuildTarget.StandaloneLinux64:
                    return "Linux";
                case BuildTarget.Android:
                    return "Android";
                case BuildTarget.iOS:
                    return "iOS";
                case BuildTarget.WebGL:
                    return "WebGL";
                default:
                    return buildTarget.ToString();
            }
        }

        private static string GenerateContentVersion(AddressablesBuildConfig config)
        {
            if (config == null)
            {
                Debug.LogWarning("[AddressablesVersionBuildProcessor] Config is null, using default version '0.0.0'");
                return "0.0.0";
            }

            // Use the same logic as AddressablesBuilder.GenerateContentVersion to ensure consistency
            if (config.versionMode == AddressablesVersionMode.Manual)
            {
                if (string.IsNullOrEmpty(config.manualVersion))
                {
                    Debug.LogWarning("[AddressablesVersionBuildProcessor] Manual version is empty, using default '0.0.0'");
                    return "0.0.0";
                }
                return config.manualVersion;
            }
            else if (config.versionMode == AddressablesVersionMode.Timestamp)
            {
                return DateTime.Now.ToString("yyyy-MM-dd-HHmmss");
            }
            else // GitCommitCount (default)
            {
                IVersionControlProvider provider = VersionControlFactory.CreateProvider(VersionControlType.Git);
                if (provider == null)
                {
                    Debug.LogWarning("[AddressablesVersionBuildProcessor] Git provider not available, using default version '0'");
                    return string.IsNullOrEmpty(config.versionPrefix) ? "0" : $"{config.versionPrefix}.0";
                }

                string count = provider.GetCommitCount();
                if (string.IsNullOrEmpty(count))
                {
                    Debug.LogWarning("[AddressablesVersionBuildProcessor] Git commit count not available, using default '0'");
                    count = "0";
                }

                return string.IsNullOrEmpty(config.versionPrefix) ? count : $"{config.versionPrefix}.{count}";
            }
        }

        [System.Serializable]
        private class VersionDataJson
        {
            public string contentVersion;
        }
    }
}