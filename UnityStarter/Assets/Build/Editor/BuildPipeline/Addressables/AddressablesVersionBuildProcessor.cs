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
    /// 
    /// Uses both PrepareForBuild (to save before copy) and PostprocessBuild (to verify/restore after copy)
    /// to handle cases where Unity rebuilds Addressables during Player build.
    /// </summary>
    public class AddressablesVersionBuildProcessor : BuildPlayerProcessor, IPostprocessBuildWithReport
    {
        // This controls when PrepareForBuild is called relative to other BuildPlayerProcessors
        public override int callbackOrder => 2; // Execute after AddressablesPlayerBuildProcessor (order = 1) 

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

                // Get current build target from EditorUserBuildSettings
                // BuildPlayerContext doesn't have BuildTarget property, so we use EditorUserBuildSettings
                BuildTarget buildTarget = EditorUserBuildSettings.activeBuildTarget;

                // Get Addressables settings to determine the actual build path
                // We need to use the same logic as AddressablesBuilder.GetAddressablesBuildPath
                // to get the bundle directory (which includes BuildTarget subdirectory)
                // Note: This is a read-only operation and should not lock any files
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

                // Get the actual bundle directory path (e.g., Library/com.unity.addressables/aa/Windows/StandaloneWindows64)
                // This is where bundle files are located, and where we should save the version file
                string bundleDirectory = GetAddressablesBuildPath(settings, settingsType, buildTarget);
                if (string.IsNullOrEmpty(bundleDirectory))
                {
                    Debug.LogWarning("[AddressablesVersionBuildProcessor] Bundle directory path is empty. Version file will be created in PostprocessBuild.");
                    return;
                }

                // Check if directory exists, but don't create it here if it doesn't
                // The directory should already exist from Addressables build
                if (!Directory.Exists(bundleDirectory))
                {
                    Debug.LogWarning($"[AddressablesVersionBuildProcessor] Bundle directory not found: {bundleDirectory}. Version file will be created in PostprocessBuild.");
                    return;
                }

                Debug.Log($"[AddressablesVersionBuildProcessor] Bundle directory: {bundleDirectory}");

                // Generate version from config
                string contentVersion = GenerateContentVersion(config);

                // Save version file to bundle directory (same location as bundle files)
                // Unity copies the BuildPath contents to StreamingAssets/aa during Player build
                // Since bundleDirectory is BuildPath/{BuildTarget}, Unity will copy it to StreamingAssets/aa/{BuildTarget}
                // So the file will be at: StreamingAssets/aa/{BuildTarget}/AddressablesVersion.json
                const string versionFileName = "AddressablesVersion.json";
                string versionFilePath = Path.Combine(bundleDirectory, versionFileName);

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
                        Debug.Log($"[AddressablesVersionBuildProcessor] Unity will copy this to StreamingAssets/aa/{buildTarget}/AddressablesVersion.json during Player build.");
                    }
                    else
                    {
                        Debug.LogWarning($"[AddressablesVersionBuildProcessor] Version file was written but not found at: {versionFilePath}. Will create in PostprocessBuild.");
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
                // Don't fail the build if version file creation fails
                // We'll try again in PostprocessBuild
                Debug.LogWarning($"[AddressablesVersionBuildProcessor] Failed to save version file in PrepareForBuild: {ex.Message}. Will create in PostprocessBuild.");
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
                // For folders: StreamingAssets is in outputPath/StreamingAssets
                string playerStreamingAssetsPath;
                if (File.Exists(outputPath) && outputPath.EndsWith(".exe"))
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

                // Version file should be in the bundle directory (same as bundle files)
                // Path: StreamingAssets/aa/{BuildTarget}/AddressablesVersion.json
                // Unity copies BuildPath contents to StreamingAssets/aa, so the BuildTarget subdirectory
                // becomes StreamingAssets/aa/{BuildTarget}
                string playerVersionPath = Path.Combine(playerStreamingAssetsPath, "aa", buildTarget.ToString(), "AddressablesVersion.json");
                string addressablesPlatformPath = GetAddressablesPlatformPath(buildTarget);
                string fallbackVersionPath = Path.Combine(playerStreamingAssetsPath, "aa", addressablesPlatformPath, "AddressablesVersion.json");

                // Check if version file exists in built player (correct location with bundle files)
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