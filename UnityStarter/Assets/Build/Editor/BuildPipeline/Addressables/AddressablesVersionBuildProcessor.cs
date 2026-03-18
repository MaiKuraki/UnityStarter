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
        // We use callbackOrder = 2 so we run AFTER the Addressables auto-build completes.
        // This ensures the bundle directory has fresh content and we can write + register our version file.
        public override int callbackOrder => 2;

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
                    Debug.LogWarning("[AddressablesVersionBuildProcessor] Failed to get Addressables settings.");
                    return;
                }

                // At callbackOrder=2, the Addressables auto-build (callbackOrder=1) has already completed,
                // so the bundle directory should have fresh content.
                string bundleDirectory = GetAddressablesBuildPath(settings, settingsType, buildTarget);

                if (string.IsNullOrEmpty(bundleDirectory) || !Directory.Exists(bundleDirectory))
                {
                    Debug.LogWarning($"[AddressablesVersionBuildProcessor] Bundle directory not found: {bundleDirectory}. Skipping version file.");
                    return;
                }

                Debug.Log($"[AddressablesVersionBuildProcessor] Bundle directory found: {bundleDirectory}");

                // Determine version: use existing file if present (from manual AddressablesBuilder.Build),
                // otherwise generate new version.
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

                var versionData = new VersionDataJson { contentVersion = contentVersion };
                string jsonContent = JsonUtility.ToJson(versionData, true);

                // Write to bundle directory (aa/{Platform}/{BuildTarget}/)
                File.WriteAllText(versionFilePath, jsonContent);

                // Register version file with BuildPlayerContext so Unity includes it in StreamingAssets.
                // Unity's AddressablesPlayerBuildProcessor only registers its OWN files (bundles, catalogs, settings).
                // Our custom version file must be explicitly registered, otherwise it won't appear in the APK.
                //
                // Use platform path: aa/{BuildTarget}/AddressablesVersion.json
                // This matches the runtime search priority in AddressablesVersionPathHelper.GetStreamingAssetsVersionPaths()
                // and keeps the version file alongside bundle files in the platform directory.
                string buildTargetName = buildTarget.ToString();
                buildPlayerContext.AddAdditionalPathToStreamingAssets(versionFilePath, $"aa/{buildTargetName}/{versionFileName}");

                Debug.Log($"[AddressablesVersionBuildProcessor] ✓ Version file written and registered for StreamingAssets.");
                Debug.Log($"[AddressablesVersionBuildProcessor] Version: {contentVersion}");
                Debug.Log($"[AddressablesVersionBuildProcessor] Registered: aa/{buildTargetName}/{versionFileName}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AddressablesVersionBuildProcessor] Failed in PrepareForBuild: {ex.Message}");
            }
        }

        /// <summary>
        /// Pre-process build fallback: ensure version file exists in bundle directory.
        /// PrepareForBuild (callbackOrder=2) handles the main logic and registration.
        /// This is a safety net in case PrepareForBuild didn't run or failed.
        /// </summary>
        public void OnPreprocessBuild(BuildReport report)
        {
            BuildData buildData = BuildConfigHelper.GetBuildData();
            if (buildData == null || !buildData.UseAddressables)
            {
                return;
            }

            try
            {
                AddressablesBuildConfig config = BuildConfigHelper.GetAddressablesConfig();
                if (config == null) return;

                BuildTarget buildTarget = report.summary.platform;

                Type settingsType = ReflectionCache.GetType("UnityEditor.AddressableAssets.Settings.AddressableAssetSettings");
                if (settingsType == null) return;

                object settings = GetDefaultSettings(settingsType);
                if (settings == null) return;

                string bundleDirectory = GetAddressablesBuildPath(settings, settingsType, buildTarget);
                if (string.IsNullOrEmpty(bundleDirectory) || !Directory.Exists(bundleDirectory)) return;

                string buildVersionPath = Path.Combine(bundleDirectory, versionFileName);

                if (!File.Exists(buildVersionPath))
                {
                    string contentVersion = GenerateContentVersion(config);
                    var versionData = new VersionDataJson { contentVersion = contentVersion };
                    string jsonContent = JsonUtility.ToJson(versionData, true);
                    File.WriteAllText(buildVersionPath, jsonContent);
                    Debug.Log($"[AddressablesVersionBuildProcessor] ✓ OnPreprocessBuild fallback: generated version file: {buildVersionPath}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AddressablesVersionBuildProcessor] OnPreprocessBuild fallback failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Post-process build: verify version file in non-compressed builds (Windows/Linux).
        /// Android APK/AAB cannot be modified after build — PrepareForBuild handles registration.
        /// </summary>
        public void OnPostprocessBuild(BuildReport report)
        {
            BuildData buildData = BuildConfigHelper.GetBuildData();
            if (buildData == null || !buildData.UseAddressables)
            {
                return;
            }

            try
            {
                BuildTarget buildTarget = report.summary.platform;

                // Android/iOS: APK/IPA is sealed, PrepareForBuild already registered the file.
                if (buildTarget == BuildTarget.Android || buildTarget == BuildTarget.iOS)
                {
                    Debug.Log($"[AddressablesVersionBuildProcessor] ✓ {buildTarget}: version file was registered via PrepareForBuild.");
                    return;
                }

                string outputPath = report.summary.outputPath;
                if (string.IsNullOrEmpty(outputPath)) return;

                // Determine StreamingAssets path in built player
                string playerStreamingAssetsPath = null;
                if (File.Exists(outputPath) && outputPath.EndsWith(".exe"))
                {
                    playerStreamingAssetsPath = Path.Combine(outputPath.Replace(".exe", "_Data"), "StreamingAssets");
                }
                else if (Directory.Exists(outputPath))
                {
                    playerStreamingAssetsPath = Path.Combine(outputPath, "StreamingAssets");
                }

                if (string.IsNullOrEmpty(playerStreamingAssetsPath)) return;

                string playerVersionPath = Path.Combine(playerStreamingAssetsPath, "aa", buildTarget.ToString(), versionFileName);

                if (File.Exists(playerVersionPath))
                {
                    Debug.Log($"[AddressablesVersionBuildProcessor] ✓ Version file verified in built player: {playerVersionPath}");
                    return;
                }

                // File missing — create it as last resort
                AddressablesBuildConfig config = BuildConfigHelper.GetAddressablesConfig();
                string contentVersion = config != null ? GenerateContentVersion(config) : "0.0.0";
                string dir = Path.GetDirectoryName(playerVersionPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var versionData = new VersionDataJson { contentVersion = contentVersion };
                File.WriteAllText(playerVersionPath, JsonUtility.ToJson(versionData, true));
                Debug.Log($"[AddressablesVersionBuildProcessor] ✓ Created version file in built player: {playerVersionPath} (Version: {contentVersion})");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AddressablesVersionBuildProcessor] OnPostprocessBuild failed: {ex.Message}");
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