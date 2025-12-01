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
                    // Fallback: try to set via field if property doesn't exist
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
                    BuildUtils.ClearDirectory(dstDir);
                    BuildUtils.CopyAllFilesRecursively(buildPath, dstDir, new string[] { ".meta" });
                    Debug.Log($"{DEBUG_FLAG} Successfully copied build result to output directory.");
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
    }
}