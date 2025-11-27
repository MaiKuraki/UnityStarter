using System;
using System.Collections;
using System.Reflection;
using CycloneGames.Editor.VersionControl;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Editor.Build
{
    public static class YooAssetBuilder
    {
        private const string DEBUG_FLAG = "<color=cyan>[YooAsset]</color>";

        [MenuItem("Build/YooAsset/Build Bundles (From Config)", priority = 100)]
        public static void BuildFromConfig()
        {
            YooAssetBuildConfig config = GetConfig();
            if (config == null)
            {
                Debug.LogError($"{DEBUG_FLAG} Config not found. Please create a YooAssetBuildConfig asset (CycloneGames/Build/YooAsset Build Config).");
                return;
            }

            string packageVersion = GeneratePackageVersion(config);
            Debug.Log($"{DEBUG_FLAG} Starting build with version: {packageVersion}");

            Build(EditorUserBuildSettings.activeBuildTarget, packageVersion, config);
        }

        public static void Build(BuildTarget buildTarget, string packageVersion)
        {
            // Overload for backward compatibility with BuildScript
            Build(buildTarget, packageVersion, null);
        }

        public static void Build(BuildTarget buildTarget, string packageVersion, YooAssetBuildConfig config)
        {
            Debug.Log($"{DEBUG_FLAG} Checking availability...");

            // Reflection types lookup
            Type collectorSettingDataType = BuildUtils.GetTypeInAllAssemblies("YooAsset.Editor.AssetBundleCollectorSettingData");
            Type builderSettingType = BuildUtils.GetTypeInAllAssemblies("YooAsset.Editor.AssetBundleBuilderSetting");
            Type builderHelperType = BuildUtils.GetTypeInAllAssemblies("YooAsset.Editor.AssetBundleBuilderHelper");
            Type builtinPipelineType = BuildUtils.GetTypeInAllAssemblies("YooAsset.Editor.BuiltinBuildPipeline");
            Type scriptablePipelineType = BuildUtils.GetTypeInAllAssemblies("YooAsset.Editor.ScriptableBuildPipeline");
            Type builtinParamsType = BuildUtils.GetTypeInAllAssemblies("YooAsset.Editor.BuiltinBuildParameters");
            Type scriptableParamsType = BuildUtils.GetTypeInAllAssemblies("YooAsset.Editor.ScriptableBuildParameters");

            // Enums - Try strict first, then fallback to search
            Type eBuildinFileCopyOption = BuildUtils.GetTypeInAllAssemblies("YooAsset.Editor.EBuildinFileCopyOption");
            Type eBuildBundleType = BuildUtils.GetTypeInAllAssemblies("YooAsset.Editor.EBuildBundleType");
            Type eCompressOption = BuildUtils.GetTypeInAllAssemblies("YooAsset.Editor.ECompressOption");

            // If EBuildBundleType is still not found, it might not exist in this version of YooAsset.
            // Some versions use int constants or different enum names.
            // However, based on standard YooAsset, it should be there.
            // Let's try to find ANY type with that name if specific lookup failed.
            if (eBuildBundleType == null) eBuildBundleType = FindTypeByName("EBuildBundleType");
            if (eBuildinFileCopyOption == null) eBuildinFileCopyOption = FindTypeByName("EBuildinFileCopyOption");
            if (eCompressOption == null) eCompressOption = FindTypeByName("ECompressOption");

            if (collectorSettingDataType == null || builderHelperType == null)
            {
                Debug.LogWarning($"{DEBUG_FLAG} YooAsset package not found. Skipping asset bundle build.");
                return;
            }

            try
            {
                // Access AssetBundleCollectorSettingData.Setting
                PropertyInfo settingProp = collectorSettingDataType.GetProperty("Setting", BindingFlags.Public | BindingFlags.Static);
                object settingInstance = settingProp.GetValue(null);

                // Access Packages list
                FieldInfo packagesField = settingInstance.GetType().GetField("Packages", BindingFlags.Public | BindingFlags.Instance);
                if (packagesField == null) packagesField = settingInstance.GetType().GetField("Packages"); // Try implicit
                // It's actually a List<AssetBundleCollectorPackage>
                IList packagesList = packagesField.GetValue(settingInstance) as IList;

                if (packagesList == null || packagesList.Count == 0)
                {
                    Debug.LogWarning($"{DEBUG_FLAG} No packages found in YooAsset Collector Setting.");
                    return;
                }

                Debug.Log($"{DEBUG_FLAG} Start building asset bundles...");

                // Prepare enum values - with safety checks
                object fileCopyOption = null;
                object buildBundleType_AssetBundle = null;
                object compressOption_LZ4 = null;

                if (eBuildinFileCopyOption != null)
                {
                    bool copyToStreaming = config != null ? config.copyToStreamingAssets : true;
                    string enumName = copyToStreaming ? "ClearAndCopyAll" : "None";
                    try { fileCopyOption = Enum.Parse(eBuildinFileCopyOption, enumName); }
                    catch { Debug.LogWarning($"{DEBUG_FLAG} Could not parse {enumName} for EBuildinFileCopyOption"); }
                }

                if (eBuildBundleType != null)
                {
                    try { buildBundleType_AssetBundle = Enum.Parse(eBuildBundleType, "AssetBundle"); }
                    catch { Debug.LogWarning($"{DEBUG_FLAG} Could not parse AssetBundle for EBuildBundleType"); }
                }
                else
                {
                    // Fallback: if enum is missing, maybe it's an int? 
                    // But we need an object to set to the field.
                    // Assuming if missing, we might set int 1? (Risk)
                    // Better to throw if critical.
                    // However, buildBundleType might be inside BuildParameters.cs as int constant in some versions?
                    // For now, we warn.
                    Debug.LogError($"{DEBUG_FLAG} EBuildBundleType enum not found. Build might fail if this field is required.");
                }

                if (eCompressOption != null)
                {
                    try { compressOption_LZ4 = Enum.Parse(eCompressOption, "LZ4"); }
                    catch { Debug.LogWarning($"{DEBUG_FLAG} Could not parse LZ4 for ECompressOption"); }
                }

                // Helper methods
                MethodInfo getDefaultOutputRoot = builderHelperType.GetMethod("GetDefaultBuildOutputRoot", BindingFlags.Public | BindingFlags.Static);
                MethodInfo getStreamingAssetsRoot = builderHelperType.GetMethod("GetStreamingAssetsRoot", BindingFlags.Public | BindingFlags.Static);

                string outputRoot = (string)getDefaultOutputRoot.Invoke(null, null);
                if (config != null && !string.IsNullOrEmpty(config.buildOutputDirectory))
                {
                    outputRoot = config.buildOutputDirectory;
                }

                string streamingRoot = (string)getStreamingAssetsRoot.Invoke(null, null);

                foreach (object packageObj in packagesList)
                {
                    FieldInfo packageNameField = packageObj.GetType().GetField("PackageName", BindingFlags.Public | BindingFlags.Instance);
                    string packageName = (string)packageNameField.GetValue(packageObj);

                    Debug.Log($"{DEBUG_FLAG} Building package: {packageName}");

                    MethodInfo getPipelineMethod = builderSettingType.GetMethod("GetPackageBuildPipeline", BindingFlags.Public | BindingFlags.Static);
                    string pipelineName = (string)getPipelineMethod.Invoke(null, new object[] { packageName });

                    if (string.IsNullOrEmpty(pipelineName)) pipelineName = "BuiltinBuildPipeline";

                    object buildParameters = null;
                    object pipelineInstance = null;

                    if (pipelineName == "BuiltinBuildPipeline" && builtinParamsType != null)
                    {
                        buildParameters = Activator.CreateInstance(builtinParamsType);
                        pipelineInstance = Activator.CreateInstance(builtinPipelineType);
                    }
                    else if (pipelineName == "ScriptableBuildPipeline" && scriptableParamsType != null)
                    {
                        buildParameters = Activator.CreateInstance(scriptableParamsType);
                        pipelineInstance = Activator.CreateInstance(scriptablePipelineType);
                    }

                    if (buildParameters != null && pipelineInstance != null)
                    {
                        BuildUtils.SetField(buildParameters, "BuildOutputRoot", outputRoot);
                        BuildUtils.SetField(buildParameters, "BuildinFileRoot", streamingRoot);
                        BuildUtils.SetField(buildParameters, "BuildPipeline", pipelineName);
                        BuildUtils.SetField(buildParameters, "BuildTarget", buildTarget);
                        if (buildBundleType_AssetBundle != null) BuildUtils.SetField(buildParameters, "BuildBundleType", buildBundleType_AssetBundle);
                        BuildUtils.SetField(buildParameters, "PackageName", packageName);
                        BuildUtils.SetField(buildParameters, "PackageVersion", packageVersion);
                        BuildUtils.SetField(buildParameters, "VerifyBuildingResult", true);
                        if (fileCopyOption != null) BuildUtils.SetField(buildParameters, "BuildinFileCopyOption", fileCopyOption);
                        BuildUtils.SetField(buildParameters, "BuildinFileCopyParams", string.Empty);
                        if (compressOption_LZ4 != null) BuildUtils.SetField(buildParameters, "CompressOption", compressOption_LZ4);

                        // Use reflection to get EBuildinFileCopyOption enum value directly if strict mode failed
                        // Force ClearAndCopyAll if copyToStreaming is true, but first we need to handle the "Package output directory exists" error.
                        // YooAsset throws "Package output directory exists" if the output directory already contains files, unless we clear it.
                        // The error [ErrorCode115] Package output directory exists means we need to ensure we are either overwriting or clearing.

                        // NOTE: YooAsset's built-in parameters often default to NOT clearing the output folder for safety.
                        // But we want automated builds, so we likely want to clean it.
                        // HOWEVER, there isn't a direct "CleanOutput" param exposed in standard BuildParameters usually.
                        // Wait, BuildParameters has BuildOutputRoot.

                        // Actually, the error is thrown by TaskPrepare_SBP.
                        // It checks if directory exists and maybe fails if it's not empty?
                        // Let's look at how we can force clean.
                        // Usually deleting the output directory before build is the safest way.

                        string packageOutputRoot = System.IO.Path.Combine(outputRoot, buildTarget.ToString(), packageName);
                        if (System.IO.Directory.Exists(packageOutputRoot))
                        {
                            Debug.Log($"{DEBUG_FLAG} Cleaning old package output: {packageOutputRoot}");
                            System.IO.Directory.Delete(packageOutputRoot, true);
                        }

                        MethodInfo runMethod = pipelineInstance.GetType().GetMethod("Run", new Type[] { buildParameters.GetType().BaseType, typeof(bool) });
                        if (runMethod == null) runMethod = pipelineInstance.GetType().GetMethod("Run");

                        object result = runMethod.Invoke(pipelineInstance, new object[] { buildParameters, true });

                        // Check Success member (Field or Property)
                        bool isSuccess = false;
                        string errorInfo = "Unknown Error";

                        Type resultType = result.GetType();
                        FieldInfo successField = resultType.GetField("Success");
                        if (successField != null)
                        {
                            isSuccess = (bool)successField.GetValue(result);
                        }
                        else
                        {
                            PropertyInfo successProp = resultType.GetProperty("Success");
                            if (successProp != null)
                            {
                                isSuccess = (bool)successProp.GetValue(result);
                            }
                            else
                            {
                                Debug.LogError($"{DEBUG_FLAG} Could not find 'Success' member on BuildResult type: {resultType.FullName}");
                            }
                        }

                        if (isSuccess)
                        {
                            Debug.Log($"{DEBUG_FLAG} Build package {packageName} success!");
                        }
                        else
                        {
                            FieldInfo errorInfoField = resultType.GetField("ErrorInfo");
                            if (errorInfoField != null)
                            {
                                object val = errorInfoField.GetValue(result);
                                if (val != null) errorInfo = (string)val;
                            }
                            else
                            {
                                PropertyInfo errorInfoProp = resultType.GetProperty("ErrorInfo");
                                if (errorInfoProp != null)
                                {
                                    object val = errorInfoProp.GetValue(result);
                                    if (val != null) errorInfo = (string)val;
                                }
                            }

                            throw new Exception($"[YooAsset] Build package {packageName} failed: {errorInfo}");
                        }
                    }
                    else
                    {
                        Debug.LogError($"{DEBUG_FLAG} Unsupported or missing pipeline/parameters type: {pipelineName}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"{DEBUG_FLAG} Build failed with exception: {ex}");
                throw;
            }
        }

        private static YooAssetBuildConfig GetConfig()
        {
            string[] guids = AssetDatabase.FindAssets("t:YooAssetBuildConfig");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                return AssetDatabase.LoadAssetAtPath<YooAssetBuildConfig>(path);
            }
            return null;
        }

        private static string GeneratePackageVersion(YooAssetBuildConfig config)
        {
            if (config.versionMode == YooAssetVersionMode.Manual)
            {
                return config.manualVersion;
            }
            else if (config.versionMode == YooAssetVersionMode.Timestamp)
            {
                return DateTime.Now.ToString("yyyy-MM-dd-HHmmss");
            }
            else // GitCommitCount
            {
                IVersionControlProvider provider = VersionControlFactory.CreateProvider(VersionControlType.Git);
                string count = provider.GetCommitCount();
                if (string.IsNullOrEmpty(count)) count = "0";
                return $"{config.versionPrefix}.{count}";
            }
        }

        private static Type FindTypeByName(string className)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (type.Name == className)
                        return type;
                }
            }
            return null;
        }
    }
}