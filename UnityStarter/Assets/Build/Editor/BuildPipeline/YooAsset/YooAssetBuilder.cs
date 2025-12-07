using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Build.VersionControl.Editor;
using UnityEditor;
using UnityEngine;

namespace Build.Pipeline.Editor
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

            // Cache configuration values locally at start to avoid any potential object invalidation during build
            bool useCopyToStreamingAssets = true; // Default true if config missing
            bool useCopyToOutputDirectory = true; // Default true if config missing
            string useBuildOutputDirectory = "";

            if (config != null)
            {
                useCopyToStreamingAssets = config.copyToStreamingAssets;
                useCopyToOutputDirectory = config.copyToOutputDirectory;
                useBuildOutputDirectory = config.buildOutputDirectory;
                Debug.Log($"{DEBUG_FLAG} Using Configuration -> CopyToStreamingAssets: <color={(useCopyToStreamingAssets ? "green" : "red")}>{useCopyToStreamingAssets}</color>, CopyToOutput: <color={(useCopyToOutputDirectory ? "green" : "red")}>{useCopyToOutputDirectory}</color>");
            }
            else
            {
                Debug.LogWarning($"{DEBUG_FLAG} No configuration provided. Using default settings (True/True).");
            }

            // Reflection types lookup (cached)
            Type collectorSettingDataType = ReflectionCache.GetType("YooAsset.Editor.AssetBundleCollectorSettingData");
            Type builderSettingType = ReflectionCache.GetType("YooAsset.Editor.AssetBundleBuilderSetting");
            Type builderHelperType = ReflectionCache.GetType("YooAsset.Editor.AssetBundleBuilderHelper");
            Type builtinPipelineType = ReflectionCache.GetType("YooAsset.Editor.BuiltinBuildPipeline");
            Type scriptablePipelineType = ReflectionCache.GetType("YooAsset.Editor.ScriptableBuildPipeline");
            Type rawFilePipelineType = ReflectionCache.GetType("YooAsset.Editor.RawFileBuildPipeline");
            Type builtinParamsType = ReflectionCache.GetType("YooAsset.Editor.BuiltinBuildParameters");
            Type scriptableParamsType = ReflectionCache.GetType("YooAsset.Editor.ScriptableBuildParameters");
            Type rawFileParamsType = ReflectionCache.GetType("YooAsset.Editor.RawFileBuildParameters");

            // Enums - Try strict first, then fallback to search
            Type eBuildinFileCopyOption = ReflectionCache.GetType("YooAsset.Editor.EBuildinFileCopyOption");
            Type eBuildBundleType = ReflectionCache.GetType("YooAsset.Editor.EBuildBundleType");
            Type eCompressOption = ReflectionCache.GetType("YooAsset.Editor.ECompressOption");

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
                PropertyInfo settingProp = ReflectionCache.GetProperty(collectorSettingDataType, "Setting", BindingFlags.Public | BindingFlags.Static);
                object settingInstance = settingProp.GetValue(null);

                // Access Packages list
                Type settingInstanceType = settingInstance.GetType();
                FieldInfo packagesField = ReflectionCache.GetField(settingInstanceType, "Packages", BindingFlags.Public | BindingFlags.Instance);
                if (packagesField == null) packagesField = ReflectionCache.GetField(settingInstanceType, "Packages", BindingFlags.Default);
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
                    bool copyToStreaming = useCopyToStreamingAssets;
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
                MethodInfo getDefaultOutputRoot = ReflectionCache.GetMethod(builderHelperType, "GetDefaultBuildOutputRoot", BindingFlags.Public | BindingFlags.Static);
                MethodInfo getStreamingAssetsRoot = ReflectionCache.GetMethod(builderHelperType, "GetStreamingAssetsRoot", BindingFlags.Public | BindingFlags.Static);

                string outputRoot = (string)getDefaultOutputRoot.Invoke(null, null);

                string customDestRoot = null;
                if (useCopyToOutputDirectory)
                {
                    string targetDir = useBuildOutputDirectory;
                    if (string.IsNullOrEmpty(targetDir))
                    {
                        targetDir = "Build/HotUpdateBundle";
                    }

                    if (targetDir.StartsWith("/"))
                    {
                        // Relative to Assets folder's parent (Project Root)
                        targetDir = targetDir.Substring(1);
                        customDestRoot = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, targetDir);
                    }
                    else if (System.IO.Path.IsPathRooted(targetDir))
                    {
                        // Absolute path
                        customDestRoot = targetDir;
                    }
                    else
                    {
                        // Relative to Project Root
                        customDestRoot = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, targetDir);
                    }

                    // Ensure we can write to destination
                    BuildUtils.CreateDirectory(customDestRoot);
                    Debug.Log($"{DEBUG_FLAG} Copy to output directory enabled. Target: {customDestRoot}");
                }

                string streamingRoot = (string)getStreamingAssetsRoot.Invoke(null, null);

                foreach (object packageObj in packagesList)
                {
                    Type packageObjType = packageObj.GetType();
                    FieldInfo packageNameField = ReflectionCache.GetField(packageObjType, "PackageName", BindingFlags.Public | BindingFlags.Instance);
                    string packageName = (string)packageNameField.GetValue(packageObj);

                    Debug.Log($"{DEBUG_FLAG} Building package: {packageName}");

                    // Auto-detect pipeline based on Collector PackRule (not Package name)
                    string pipelineName = AutoDetectPipelineForPackage(packageObj, packageName);
                    Debug.Log($"{DEBUG_FLAG} Auto-detected pipeline for package '{packageName}': {pipelineName}");

                    object buildParameters = null;
                    object pipelineInstance = null;

                    if (pipelineName == "RawFileBuildPipeline" && rawFileParamsType != null && rawFilePipelineType != null)
                    {
                        buildParameters = Activator.CreateInstance(rawFileParamsType);
                        pipelineInstance = Activator.CreateInstance(rawFilePipelineType);
                    }
                    else if (pipelineName == "BuiltinBuildPipeline" && builtinParamsType != null)
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

                        // Set BuildBundleType based on pipeline
                        if (pipelineName == "RawFileBuildPipeline")
                        {
                            // RawFileBuildPipeline uses RawBundle type
                            if (eBuildBundleType != null)
                            {
                                try
                                {
                                    object buildBundleType_RawBundle = Enum.Parse(eBuildBundleType, "RawBundle");
                                    BuildUtils.SetField(buildParameters, "BuildBundleType", buildBundleType_RawBundle);
                                }
                                catch { Debug.LogWarning($"{DEBUG_FLAG} Could not parse RawBundle for EBuildBundleType"); }
                            }
                        }
                        else
                        {
                            // BuiltinBuildPipeline and ScriptableBuildPipeline use AssetBundle type
                            if (buildBundleType_AssetBundle != null)
                                BuildUtils.SetField(buildParameters, "BuildBundleType", buildBundleType_AssetBundle);
                        }

                        BuildUtils.SetField(buildParameters, "PackageName", packageName);
                        BuildUtils.SetField(buildParameters, "PackageVersion", packageVersion);
                        BuildUtils.SetField(buildParameters, "VerifyBuildingResult", true);
                        if (fileCopyOption != null) BuildUtils.SetField(buildParameters, "BuildinFileCopyOption", fileCopyOption);
                        BuildUtils.SetField(buildParameters, "BuildinFileCopyParams", string.Empty);

                        // Only set CompressOption for non-RawFile pipelines
                        if (pipelineName != "RawFileBuildPipeline" && compressOption_LZ4 != null)
                        {
                            BuildUtils.SetField(buildParameters, "CompressOption", compressOption_LZ4);
                        }

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
                            BuildUtils.DeleteDirectory(packageOutputRoot);
                        }

                        Type pipelineInstanceType = pipelineInstance.GetType();
                        // Run method may have overloads, try to find the one matching our parameters
                        MethodInfo runMethod = pipelineInstanceType.GetMethod("Run", new Type[] { buildParameters.GetType().BaseType, typeof(bool) });
                        if (runMethod == null)
                        {
                            // Fallback: try without parameter types
                            runMethod = ReflectionCache.GetMethod(pipelineInstanceType, "Run", BindingFlags.Public | BindingFlags.Instance);
                        }

                        if (runMethod == null)
                        {
                            Debug.LogError($"{DEBUG_FLAG} Run method not found on pipeline type: {pipelineInstanceType.Name}");
                            continue;
                        }

                        object result = runMethod.Invoke(pipelineInstance, new object[] { buildParameters, true });

                        // Check Success member (Field or Property)
                        bool isSuccess = false;
                        string errorInfo = "Unknown Error";

                        Type resultType = result.GetType();
                        FieldInfo successField = ReflectionCache.GetField(resultType, "Success", BindingFlags.Public | BindingFlags.Instance);
                        if (successField != null)
                        {
                            isSuccess = (bool)successField.GetValue(result);
                        }
                        else
                        {
                            PropertyInfo successProp = ReflectionCache.GetProperty(resultType, "Success", BindingFlags.Public | BindingFlags.Instance);
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

                            if (!string.IsNullOrEmpty(customDestRoot))
                            {
                                try
                                {
                                    // Instead of copying from the raw build output (which contains cache, hash files, versions folders etc.),
                                    // we copy from the StreamingAssets folder if 'copyToStreamingAssets' is enabled.
                                    // This ensures the output directory contains exactly what the runtime expects (BuiltinFileSystem).
                                    // If copyToStreamingAssets is false, we fallback to copying the raw output, but warn the user.

                                    string srcDir;
                                    bool isFromStreaming = false;

                                    if (useCopyToStreamingAssets)
                                    {
                                        // Source is Assets/StreamingAssets/yoo/PackageName
                                        srcDir = System.IO.Path.Combine(streamingRoot, packageName);
                                        isFromStreaming = true;
                                    }
                                    else
                                    {
                                        // Fallback to raw output (OutputCache/Version folders)
                                        srcDir = packageOutputRoot;

                                        // IMPORTANT: If user explicitly disabled copyToStreamingAssets, 
                                        // we must ensure the StreamingAssets folder is CLEAN to avoid misleading old files.
                                        // streamingRoot is "Assets/StreamingAssets/yoo"
                                        string streamingPackageDir = System.IO.Path.Combine(streamingRoot, packageName);
                                        if (System.IO.Directory.Exists(streamingPackageDir))
                                        {
                                            Debug.Log($"{DEBUG_FLAG} [Cleanup] 'Copy To Streaming Assets' is OFF. Cleaning up old files in: {streamingPackageDir}");
                                            BuildUtils.DeleteDirectory(streamingPackageDir);

                                            // Also delete the .meta file associated with the directory to keep AssetDatabase in sync
                                            string metaFilePath = streamingPackageDir + ".meta";
                                            if (System.IO.File.Exists(metaFilePath))
                                            {
                                                try
                                                {
                                                    System.IO.File.Delete(metaFilePath);
                                                    Debug.Log($"{DEBUG_FLAG} [Cleanup] Deleted associated meta file: {metaFilePath}");
                                                }
                                                catch (Exception ex)
                                                {
                                                    Debug.LogWarning($"{DEBUG_FLAG} Failed to delete meta file {metaFilePath}: {ex.Message}");
                                                }
                                            }
                                        }

                                        Debug.LogWarning($"{DEBUG_FLAG} 'Copy To Streaming Assets' is disabled. Copying raw build output to custom destination. " +
                                                         "This may include cache/hash files not needed for runtime. " +
                                                         "Enable 'Copy To Streaming Assets' for a clean, ready-to-use output.");
                                    }

                                    string dstDir = System.IO.Path.Combine(customDestRoot, buildTarget.ToString(), packageName);

                                    Debug.Log($"{DEBUG_FLAG} Copying build result from {(isFromStreaming ? "StreamingAssets" : "BuildOutput")} to: {dstDir}");

                                    if (System.IO.Directory.Exists(srcDir))
                                    {
                                        BuildUtils.ClearDirectory(dstDir);
                                        // Copy files but ignore .meta files as they are not needed for distribution/runtime
                                        BuildUtils.CopyAllFilesRecursively(srcDir, dstDir, new string[] { ".meta" });
                                    }
                                    else
                                    {
                                        Debug.LogError($"{DEBUG_FLAG} Source directory for copy not found: {srcDir}");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.LogError($"{DEBUG_FLAG} Failed to copy build result to custom directory: {ex.Message}");
                                }
                            }
                        }
                        else
                        {
                            FieldInfo errorInfoField = ReflectionCache.GetField(resultType, "ErrorInfo", BindingFlags.Public | BindingFlags.Instance);
                            if (errorInfoField != null)
                            {
                                object val = errorInfoField.GetValue(result);
                                if (val != null) errorInfo = (string)val;
                            }
                            else
                            {
                                PropertyInfo errorInfoProp = ReflectionCache.GetProperty(resultType, "ErrorInfo", BindingFlags.Public | BindingFlags.Instance);
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
            YooAssetBuildConfig config = BuildConfigHelper.GetYooAssetConfig();
            if (config != null)
            {
                Debug.Log($"{DEBUG_FLAG} Loaded config. [StreamingAssets: {config.copyToStreamingAssets}]");
            }
            return config;
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

        /// <summary>
        /// Auto-detect the appropriate build pipeline for a package based on its Collector PackRule configuration.
        /// 
        /// Detection Rules:
        /// 1. If ALL Collectors use PackRawFile → RawFileBuildPipeline
        /// 2. If NO Collectors use PackRawFile → ScriptableBuildPipeline
        /// 3. If MIXED (some PackRawFile, some others) → ERROR (must separate packages)
        /// 
        /// Note: YooAsset limitation - one Package can only use one BuildBundleType.
        /// RawFileBuildPipeline requires each bundle to contain only ONE file.
        /// </summary>
        /// <param name="packageObj">The package object from YooAsset's AssetBundleCollectorSetting</param>
        /// <param name="packageName">The package name (for logging purposes)</param>
        /// <returns>The detected pipeline name: "RawFileBuildPipeline" or "ScriptableBuildPipeline"</returns>
        private static string AutoDetectPipelineForPackage(object packageObj, string packageName)
        {
            try
            {
                Type packageObjType = packageObj.GetType();

                // Get Groups field from AssetBundleCollectorPackage
                FieldInfo groupsField = ReflectionCache.GetField(packageObjType, "Groups", BindingFlags.Public | BindingFlags.Instance);
                if (groupsField == null)
                {
                    Debug.LogWarning($"{DEBUG_FLAG} Could not find 'Groups' field in package '{packageName}'. Falling back to ScriptableBuildPipeline.");
                    return "ScriptableBuildPipeline";
                }

                IList groupsList = groupsField.GetValue(packageObj) as IList;
                if (groupsList == null || groupsList.Count == 0)
                {
                    Debug.Log($"{DEBUG_FLAG} Package '{packageName}' has no groups. Using default ScriptableBuildPipeline.");
                    return "ScriptableBuildPipeline";
                }

                // Collect all PackRuleNames to detect mixed usage
                List<string> allPackRuleNames = new List<string>();
                bool hasPackRawFile = false;
                bool hasOtherRules = false;

                // Check each group's collectors
                foreach (object groupObj in groupsList)
                {
                    Type groupObjType = groupObj.GetType();

                    // Get Collectors field from AssetBundleCollectorGroup
                    FieldInfo collectorsField = ReflectionCache.GetField(groupObjType, "Collectors", BindingFlags.Public | BindingFlags.Instance);
                    if (collectorsField == null)
                        continue;

                    IList collectorsList = collectorsField.GetValue(groupObj) as IList;
                    if (collectorsList == null)
                        continue;

                    // Check each collector's PackRuleName
                    foreach (object collectorObj in collectorsList)
                    {
                        Type collectorObjType = collectorObj.GetType();
                        FieldInfo packRuleNameField = ReflectionCache.GetField(collectorObjType, "PackRuleName", BindingFlags.Public | BindingFlags.Instance);

                        if (packRuleNameField != null)
                        {
                            string packRuleName = packRuleNameField.GetValue(collectorObj) as string;
                            if (!string.IsNullOrEmpty(packRuleName))
                            {
                                allPackRuleNames.Add(packRuleName);

                                if (packRuleName == "PackRawFile")
                                {
                                    hasPackRawFile = true;
                                }
                                else
                                {
                                    hasOtherRules = true;
                                }
                            }
                        }
                    }
                }

                // Decision logic
                if (hasPackRawFile && hasOtherRules)
                {
                    // MIXED: Cannot use RawFileBuildPipeline (requires each bundle to have only one file)
                    // Cannot use ScriptableBuildPipeline for DLLs (Android/iOS requirement)
                    string errorMessage = $"{DEBUG_FLAG} <color=red>ERROR:</color> Package '{packageName}' has MIXED PackRule configuration:\n" +
                                         $"  - Found PackRawFile rules (for DLL files)\n" +
                                         $"  - Found other rules: {string.Join(", ", allPackRuleNames.Where(r => r != "PackRawFile").Distinct())}\n\n" +
                                         $"<color=yellow>SOLUTION:</color> You must separate DLL files into a DIFFERENT Package:\n" +
                                         $"  1. Create a new Package (e.g., 'HotUpdatePackage') for DLL files\n" +
                                         $"  2. Set all DLL Collectors to use 'PackRawFile' rule in the new Package\n" +
                                         $"  3. Keep other resources (textures, models, etc.) in '{packageName}' with other PackRules\n" +
                                         $"  4. Each Package will automatically use the correct Pipeline based on its Collectors\n\n" +
                                         $"<color=cyan>Why?</color> YooAsset limitation: one Package can only use one BuildBundleType.\n" +
                                         $"RawFileBuildPipeline requires each bundle to contain only ONE file.";

                    Debug.LogError(errorMessage);
                    throw new Exception($"Package '{packageName}' has mixed PackRule configuration. DLL files must be in a separate Package. See console for details.");
                }
                else if (hasPackRawFile)
                {
                    // ALL PackRawFile: Use RawFileBuildPipeline
                    Debug.Log($"{DEBUG_FLAG} Package '{packageName}' detected ALL PackRawFile rules. Using RawFileBuildPipeline.");
                    return "RawFileBuildPipeline";
                }
                else
                {
                    // NO PackRawFile: Use ScriptableBuildPipeline (default for asset resources)
                    Debug.Log($"{DEBUG_FLAG} Package '{packageName}' has no PackRawFile rules. Using ScriptableBuildPipeline.");
                    return "ScriptableBuildPipeline";
                }
            }
            catch (Exception ex)
            {
                // Re-throw if it's our custom exception
                if (ex.Message.Contains("mixed PackRule configuration"))
                    throw;

                Debug.LogWarning($"{DEBUG_FLAG} Error auto-detecting pipeline for package '{packageName}': {ex.Message}. Falling back to ScriptableBuildPipeline.");
                return "ScriptableBuildPipeline";
            }
        }
    }
}