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

            Type settingsType = ReflectionCache.GetType("UnityEditor.AddressableAssets.Settings.AddressableAssetSettings");
            Type buildResultType = ReflectionCache.GetType("UnityEditor.AddressableAssets.Build.AddressablesPlayerBuildResult");
            if (buildResultType == null)
            {
                buildResultType = ReflectionCache.GetType("UnityEditor.AddressableAssets.Build.DataBuildResult");
            }

            if (settingsType == null)
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

                object buildResult = BuildWithSettings(settingsType, settings, buildTarget, buildResultType);

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
                            // Use default path if buildOutputDirectory is empty (similar to YooAsset)
                            string outputDir = useBuildOutputDirectory;
                            if (string.IsNullOrEmpty(outputDir))
                            {
                                outputDir = "Build/AddressablesContent";
                                Debug.Log($"{DEBUG_FLAG} BuildOutputDirectory is empty, using default path: {outputDir}");
                            }
                            CopyBuildResultToOutput(buildTarget, outputDir, useBuildRemoteCatalog);
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
                        Debug.LogWarning($"{DEBUG_FLAG} Failed to get Settings property: {ex.Message}");
                    }
                }

                // Fallback: Try GetSettings(bool create) method
                MethodInfo getSettingsMethod = ReflectionCache.GetMethod(defaultObjectType, "GetSettings", BindingFlags.Public | BindingFlags.Static, new Type[] { typeof(bool) });
                if (getSettingsMethod == null)
                {
                    // Try without parameter types (may fail if multiple overloads)
                    getSettingsMethod = ReflectionCache.GetMethod(defaultObjectType, "GetSettings", BindingFlags.Public | BindingFlags.Static);
                }

                if (getSettingsMethod != null)
                {
                    // GetSettings(bool create) - pass false to avoid creating if it doesn't exist
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
                        Debug.LogWarning($"{DEBUG_FLAG} Failed to invoke GetSettings(false): {ex.Message}");
                        // Try with true as last resort
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

        private static void SetContentVersion(object settings, Type settingsType, string contentVersion)
        {
            if (settings == null || settingsType == null || string.IsNullOrEmpty(contentVersion))
                return;

            try
            {
                // Initialize ProfileValueReference fields to avoid "GetValue called with empty id" warnings
                // This happens when Addressables accesses uninitialized ProfileValueReference fields during build
                InitializeProfileValueReferences(settings, settingsType);

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

        /// <summary>
        /// Initializes ProfileValueReference fields to avoid "GetValue called with empty id" warnings.
        /// These warnings occur when Addressables accesses uninitialized ProfileValueReference fields during build.
        /// </summary>
        private static void InitializeProfileValueReferences(object settings, Type settingsType)
        {
            try
            {
                // Check if BuildRemoteCatalog is enabled
                PropertyInfo buildRemoteCatalogProp = ReflectionCache.GetProperty(settingsType, "BuildRemoteCatalog", BindingFlags.Public | BindingFlags.Instance);
                bool buildRemoteCatalog = buildRemoteCatalogProp != null && (bool)buildRemoteCatalogProp.GetValue(settings);

                if (buildRemoteCatalog)
                {
                    // Initialize RemoteCatalogBuildPath if needed
                    PropertyInfo remoteCatalogBuildPathProp = ReflectionCache.GetProperty(settingsType, "RemoteCatalogBuildPath", BindingFlags.Public | BindingFlags.Instance);
                    if (remoteCatalogBuildPathProp != null)
                    {
                        object remoteCatalogBuildPath = remoteCatalogBuildPathProp.GetValue(settings);
                        if (remoteCatalogBuildPath != null)
                        {
                            Type profileValueReferenceType = ReflectionCache.GetType("UnityEditor.AddressableAssets.Settings.ProfileValueReference");
                            if (profileValueReferenceType != null)
                            {
                                // Check if Id is empty
                                PropertyInfo idProp = ReflectionCache.GetProperty(profileValueReferenceType, "Id", BindingFlags.Public | BindingFlags.Instance);
                                if (idProp != null)
                                {
                                    string id = idProp.GetValue(remoteCatalogBuildPath)?.ToString();
                                    if (string.IsNullOrEmpty(id))
                                    {
                                        // Initialize with default Remote.BuildPath variable
                                        MethodInfo setVariableByNameMethod = ReflectionCache.GetMethod(profileValueReferenceType, "SetVariableByName", BindingFlags.Public | BindingFlags.Instance, new Type[] { typeof(object), typeof(string) });
                                        if (setVariableByNameMethod != null)
                                        {
                                            setVariableByNameMethod.Invoke(remoteCatalogBuildPath, new object[] { settings, "Remote.BuildPath" });
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // Initialize RemoteCatalogLoadPath if needed
                    PropertyInfo remoteCatalogLoadPathProp = ReflectionCache.GetProperty(settingsType, "RemoteCatalogLoadPath", BindingFlags.Public | BindingFlags.Instance);
                    if (remoteCatalogLoadPathProp != null)
                    {
                        object remoteCatalogLoadPath = remoteCatalogLoadPathProp.GetValue(settings);
                        if (remoteCatalogLoadPath != null)
                        {
                            Type profileValueReferenceType = ReflectionCache.GetType("UnityEditor.AddressableAssets.Settings.ProfileValueReference");
                            if (profileValueReferenceType != null)
                            {
                                PropertyInfo idProp = ReflectionCache.GetProperty(profileValueReferenceType, "Id", BindingFlags.Public | BindingFlags.Instance);
                                if (idProp != null)
                                {
                                    string id = idProp.GetValue(remoteCatalogLoadPath)?.ToString();
                                    if (string.IsNullOrEmpty(id))
                                    {
                                        MethodInfo setVariableByNameMethod = ReflectionCache.GetMethod(profileValueReferenceType, "SetVariableByName", BindingFlags.Public | BindingFlags.Instance, new Type[] { typeof(object), typeof(string) });
                                        if (setVariableByNameMethod != null)
                                        {
                                            setVariableByNameMethod.Invoke(remoteCatalogLoadPath, new object[] { settings, "Remote.LoadPath" });
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Silently fail - this is just an optimization to reduce warnings
                // The build will still work even if ProfileValueReference initialization fails
                Debug.LogWarning($"{DEBUG_FLAG} Failed to initialize ProfileValueReferences (non-critical): {ex.Message}");
            }
        }


        /// <summary>
        /// Builds Addressables content using AddressableAssetSettings.BuildPlayerContent (standard API).
        /// </summary>
        private static object BuildWithSettings(Type settingsType, object settings, BuildTarget buildTarget, Type buildResultType)
        {
            // Try BuildPlayerContent(out AddressablesPlayerBuildResult) - standard API
            MethodInfo buildMethod = null;
            MethodInfo[] allMethods = settingsType.GetMethods(BindingFlags.Public | BindingFlags.Static);
            foreach (var method in allMethods)
            {
                if (method.Name == "BuildPlayerContent")
                {
                    ParameterInfo[] parameters = method.GetParameters();

                    // Prefer BuildPlayerContent(out result) signature
                    if (parameters.Length == 1 && parameters[0].IsOut)
                    {
                        buildMethod = method;
                        break;
                    }
                    // Fallback to no parameters
                    else if (parameters.Length == 0 && buildMethod == null)
                    {
                        buildMethod = method;
                    }
                }
            }

            if (buildMethod == null)
            {
                Debug.LogWarning($"{DEBUG_FLAG} BuildPlayerContent method not found in AddressableAssetSettings.");
                return null;
            }

            ParameterInfo[] methodParams = buildMethod.GetParameters();

            // Check if it's BuildPlayerContent(out result) signature
            if (methodParams.Length == 1 && methodParams[0].IsOut)
            {
                // Get the result type from the out parameter
                Type resultType = methodParams[0].ParameterType.GetElementType();
                if (resultType == null && buildResultType != null)
                {
                    resultType = buildResultType;
                }

                if (resultType != null)
                {
                    try
                    {
                        // Create result object
                        object result = Activator.CreateInstance(resultType);
                        object[] invokeParams = new object[] { result };
                        buildMethod.Invoke(null, invokeParams);
                        return invokeParams[0]; // Return the out parameter
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"{DEBUG_FLAG} Failed to invoke BuildPlayerContent with out parameter: {ex.Message}");
                    }
                }
            }
            // Check if it's BuildPlayerContent() signature (no parameters)
            else if (methodParams.Length == 0)
            {
                try
                {
                    return buildMethod.Invoke(null, null);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"{DEBUG_FLAG} Failed to invoke BuildPlayerContent(): {ex.Message}");
                }
            }
            else
            {
                Debug.LogWarning($"{DEBUG_FLAG} BuildPlayerContent method signature not recognized. Parameters: {methodParams.Length}");
            }

            return null;
        }

        private static bool CheckBuildResult(object buildResult, Type buildResultType)
        {
            if (buildResult == null) return false;

            Type resultType = buildResult.GetType();

            // Try Success field/property first
            FieldInfo successField = ReflectionCache.GetField(resultType, "Success", BindingFlags.Public | BindingFlags.Instance);
            if (successField != null)
            {
                object successValue = successField.GetValue(buildResult);
                if (successValue is bool boolValue)
                {
                    return boolValue;
                }
            }

            PropertyInfo successProp = ReflectionCache.GetProperty(resultType, "Success", BindingFlags.Public | BindingFlags.Instance);
            if (successProp != null)
            {
                object successValue = successProp.GetValue(buildResult);
                if (successValue is bool boolValue)
                {
                    return boolValue;
                }
            }

            // Check Error field/property - if Error is null or empty, build succeeded
            FieldInfo errorField = ReflectionCache.GetField(resultType, "Error", BindingFlags.Public | BindingFlags.Instance);
            if (errorField != null)
            {
                object errorValue = errorField.GetValue(buildResult);
                if (errorValue == null)
                {
                    return true; // No error means success
                }
                string error = errorValue.ToString();
                if (string.IsNullOrEmpty(error))
                {
                    return true; // Empty error means success
                }
                // If error is not empty, check if it's just a warning
                if (error.Contains("warning", StringComparison.OrdinalIgnoreCase) && !error.Contains("failed", StringComparison.OrdinalIgnoreCase))
                {
                    return true; // Only warnings, not failures
                }
                return false; // Has error
            }

            PropertyInfo errorProp = ReflectionCache.GetProperty(resultType, "Error", BindingFlags.Public | BindingFlags.Instance);
            if (errorProp != null)
            {
                object errorValue = errorProp.GetValue(buildResult);
                if (errorValue == null)
                {
                    return true; // No error means success
                }
                string error = errorValue.ToString();
                if (string.IsNullOrEmpty(error))
                {
                    return true; // Empty error means success
                }
                // If error is not empty, check if it's just a warning
                if (error.Contains("warning", StringComparison.OrdinalIgnoreCase) && !error.Contains("failed", StringComparison.OrdinalIgnoreCase))
                {
                    return true; // Only warnings, not failures
                }
                return false; // Has error
            }

            // Check Exception field/property
            FieldInfo exceptionField = ReflectionCache.GetField(resultType, "Exception", BindingFlags.Public | BindingFlags.Instance);
            if (exceptionField != null)
            {
                object exceptionValue = exceptionField.GetValue(buildResult);
                return exceptionValue == null; // No exception means success
            }

            PropertyInfo exceptionProp = ReflectionCache.GetProperty(resultType, "Exception", BindingFlags.Public | BindingFlags.Instance);
            if (exceptionProp != null)
            {
                object exceptionValue = exceptionProp.GetValue(buildResult);
                return exceptionValue == null; // No exception means success
            }

            // If we can't determine, but buildResult is not null and no exception was thrown,
            // assume success (since BuildPlayerContent would throw if it failed)
            Debug.LogWarning($"{DEBUG_FLAG} Could not determine build result success status from result type {resultType.Name}. Assuming success since no exception was thrown and build completed.");
            return true; // Assume success if we can't determine
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

        /// <summary>
        /// Gets the Addressables build output path for the specified build target.
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
                                // (some Addressables configurations may not use BuildTarget subdirectory)
                                if (Directory.Exists(buildPath))
                                {
                                    return buildPath;
                                }
                            }
                        }
                    }
                }

                // Fallback to ProfileSettings.GetValueByName with activeProfileId
                PropertyInfo profileProp = ReflectionCache.GetProperty(settingsType, "profileSettings", BindingFlags.Public | BindingFlags.Instance);
                if (profileProp == null)
                {
                    profileProp = ReflectionCache.GetProperty(settingsType, "ProfileSettings", BindingFlags.Public | BindingFlags.Instance);
                }

                object profileSettings = profileProp?.GetValue(settings);
                if (profileSettings != null)
                {
                    // Get activeProfileId
                    PropertyInfo activeProfileIdProp = ReflectionCache.GetProperty(settingsType, "activeProfileId", BindingFlags.Public | BindingFlags.Instance);
                    string activeProfileId = activeProfileIdProp?.GetValue(settings)?.ToString();

                    if (!string.IsNullOrEmpty(activeProfileId))
                    {
                        Type profileSettingsType = profileSettings.GetType();

                        // Try EvaluateString first (handles variables like [BuildPath]/[BuildTarget])
                        MethodInfo evaluateStringMethod = ReflectionCache.GetMethod(profileSettingsType, "EvaluateString", BindingFlags.Public | BindingFlags.Instance, new Type[] { typeof(string), typeof(string) });
                        if (evaluateStringMethod != null)
                        {
                            PropertyInfo buildRemoteCatalogProp = ReflectionCache.GetProperty(settingsType, "BuildRemoteCatalog", BindingFlags.Public | BindingFlags.Instance);
                            bool isRemote = buildRemoteCatalogProp != null && (bool)buildRemoteCatalogProp.GetValue(settings);
                            string buildPathVar = isRemote ? "Remote.BuildPath" : "Local.BuildPath";

                            // Get the raw value first
                            MethodInfo getValueMethod = ReflectionCache.GetMethod(profileSettingsType, "GetValueByName", BindingFlags.Public | BindingFlags.Instance, new Type[] { typeof(string), typeof(string) });
                            if (getValueMethod != null)
                            {
                                string rawValue = getValueMethod.Invoke(profileSettings, new object[] { activeProfileId, buildPathVar })?.ToString();
                                if (!string.IsNullOrEmpty(rawValue))
                                {
                                    // Evaluate the string to resolve variables
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
                Debug.LogWarning($"{DEBUG_FLAG} Failed to get Addressables build path: {ex.Message}");
            }

            return null;
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
        /// Cleans up version files in StreamingAssets after build.
        /// These files are automatically created by Unity during Player build from Addressables build output.
        /// 
        /// This should be called after a full build (not after resource-only builds),
        /// as the version file is needed during the build process.
        /// </summary>
        public static void CleanupAutoCreatedVersionFiles()
        {
            try
            {
                const string streamingAssetsPath = "Assets/StreamingAssets";
                const string addressablesFolder = "aa";
                const string versionFileName = "AddressablesVersion.json";

                string addressablesRoot = Path.Combine(streamingAssetsPath, addressablesFolder);

                if (!Directory.Exists(addressablesRoot))
                {
                    return; // No Addressables folder, nothing to clean
                }

                // Search for version files in all platform subdirectories
                // These files are automatically copied by Unity from build output, so we can safely delete them
                string[] platformDirs = Directory.GetDirectories(addressablesRoot);
                foreach (string platformDir in platformDirs)
                {
                    string versionFilePath = Path.Combine(platformDir, versionFileName);

                    if (File.Exists(versionFilePath))
                    {
                        // Convert absolute path to relative path for AssetDatabase
                        string versionFileRelativePath = versionFilePath.Replace(Application.dataPath, "Assets").Replace('\\', '/');
                        
                        // Delete version file using AssetDatabase (handles .meta automatically)
                        AssetDatabase.DeleteAsset(versionFileRelativePath);
                        Debug.Log($"{DEBUG_FLAG} Cleaned up version file: {versionFileRelativePath}");
                    }
                }

                AssetDatabase.Refresh();
                Debug.Log($"{DEBUG_FLAG} Cleanup completed for version files.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{DEBUG_FLAG} Failed to cleanup version files: {ex.Message}");
            }
        }

        [System.Serializable]
        private class VersionDataJson
        {
            public string contentVersion;
        }
    }
}