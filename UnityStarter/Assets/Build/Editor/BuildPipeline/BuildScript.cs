using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using System.Reflection;
using Build.VersionControl.Editor;

namespace Build.Pipeline.Editor
{
    [Serializable]
    public class VersionInfo
    {
        public string CommitHash { get; set; }
        public string CreatedDate { get; set; }
    }

    public class BuildScript
    {
        private const string DEBUG_FLAG = "<color=cyan>[Game Builder]</color>";
        private const string INVALID_FLAG = "INVALID";

        private const string CompanyName = "CycloneGames";
        private const string ApplicationName = "UnityStarter";
        private const string VersionInfoAssetPath = "Assets/Resources/VersionInfoData.asset";

        private static BuildData buildData;

        private static VersionControlType DefaultVersionControlType = VersionControlType.Git;
        private static IVersionControlProvider VersionControlProvider;
        private static void InitializeVersionControl(VersionControlType vcType)
        {
            VersionControlProvider = VersionControlFactory.CreateProvider(vcType);
        }

        [MenuItem("Build/Game(Standard)/Print Debug Info", priority = 100)]
        public static void PrintDebugInfo()
        {
            var sceneList = GetBuildSceneList();
            if (sceneList == null || sceneList.Length == 0)
            {
                Debug.LogError($"{DEBUG_FLAG} Invalid scene list, please check BuildData configuration.");
                return;
            }

            foreach (var scene_name in sceneList)
            {
                Debug.Log($"{DEBUG_FLAG} Pre Build Scene: {scene_name}");
            }
        }

        #region Menu Items - Standard Builds (Clean)

        [MenuItem("Build/Game(Standard)/Build Android APK (IL2CPP)", priority = 400)]
        public static void PerformBuild_AndroidAPK()
        {
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
            PerformBuild(
                BuildTarget.Android,
                NamedBuildTarget.Android,
                ScriptingImplementation.IL2CPP,
                $"{GetPlatformFolderName(BuildTarget.Android)}/{ApplicationName}.apk",
                bCleanBuild: true,
                bDeleteDebugFiles: true,
                bOutputIsFolderTarget: false);
        }

        [MenuItem("Build/Game(Standard)/Build Windows (IL2CPP)", priority = 401)]
        public static void PerformBuild_Windows()
        {
            PerformBuild(
                BuildTarget.StandaloneWindows64,
                NamedBuildTarget.Standalone,
                ScriptingImplementation.IL2CPP,
                $"{GetPlatformFolderName(BuildTarget.StandaloneWindows64)}/{ApplicationName}.exe",
                bCleanBuild: true,
                bDeleteDebugFiles: true,
                bOutputIsFolderTarget: false);
        }

        [MenuItem("Build/Game(Standard)/Build Mac (IL2CPP)", priority = 402)]
        public static void PerformBuild_Mac()
        {
            PerformBuild(
                BuildTarget.StandaloneOSX,
                NamedBuildTarget.Standalone,
                ScriptingImplementation.IL2CPP,
                $"{GetPlatformFolderName(BuildTarget.StandaloneOSX)}/{ApplicationName}.app",
                bCleanBuild: true,
                bDeleteDebugFiles: true,
                bOutputIsFolderTarget: false);
        }

        [MenuItem("Build/Game(Standard)/Export Android Project (IL2CPP)", priority = 404)]
        public static void PerformBuild_AndroidProject()
        {
            EditorUserBuildSettings.exportAsGoogleAndroidProject = true;
            PerformBuild(
                BuildTarget.Android,
                NamedBuildTarget.Android,
                ScriptingImplementation.IL2CPP,
                $"{GetPlatformFolderName(BuildTarget.Android)}/{ApplicationName}",
                bCleanBuild: true,
                bDeleteDebugFiles: true,
                bOutputIsFolderTarget: true);
        }

        [MenuItem("Build/Game(Standard)/Build WebGL", priority = 403)]
        public static void PerformBuild_WebGL()
        {
            PerformBuild(
                BuildTarget.WebGL,
                NamedBuildTarget.WebGL,
                ScriptingImplementation.IL2CPP,
                $"{GetPlatformFolderName(BuildTarget.WebGL)}/{ApplicationName}",
                bCleanBuild: true,
                bDeleteDebugFiles: true,
                bOutputIsFolderTarget: true);
        }

        #endregion

        #region Menu Items - Fast Builds (No Clean)

        [MenuItem("Build/Game(Standard)/Fast/Build Android APK (Fast)", priority = 500)]
        public static void PerformBuild_AndroidAPK_Fast()
        {
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
            PerformBuild(
                BuildTarget.Android,
                NamedBuildTarget.Android,
                ScriptingImplementation.IL2CPP,
                $"{GetPlatformFolderName(BuildTarget.Android)}/{ApplicationName}.apk",
                bCleanBuild: false,
                bDeleteDebugFiles: true, // Clean up debug files even in fast mode
                bOutputIsFolderTarget: false);
        }

        [MenuItem("Build/Game(Standard)/Fast/Build Windows (Fast)", priority = 501)]
        public static void PerformBuild_Windows_Fast()
        {
            PerformBuild(
                BuildTarget.StandaloneWindows64,
                NamedBuildTarget.Standalone,
                ScriptingImplementation.IL2CPP,
                $"{GetPlatformFolderName(BuildTarget.StandaloneWindows64)}/{ApplicationName}.exe",
                bCleanBuild: false,
                bDeleteDebugFiles: true, // Clean up debug files even in fast mode
                bOutputIsFolderTarget: false);
        }

        #endregion

        #region CI/CD

        /// <summary>
        /// Entry point for CI/CD. Parses command line arguments to configure the build.
        /// Usage: -executeMethod Build.Pipeline.Editor.BuildScript.PerformBuild_CI -buildTarget <Target> -output <Path> [-clean] [-buildHybridCLR] [-buildYooAsset] [-buildAddressables] [-version <Version>] [-outputBasePath <Path>]
        /// </summary>
        public static void PerformBuild_CI()
        {
            Debug.Log($"{DEBUG_FLAG} Starting CI Build...");

            // Load Build Data first
            buildData = BuildConfigHelper.GetBuildData();
            if (buildData == null)
            {
                Debug.LogError($"{DEBUG_FLAG} BuildData not found. Cannot proceed with CI build.");
                return;
            }

            // Parse arguments
            string[] args = System.Environment.GetCommandLineArgs();
            BuildTarget buildTarget = BuildTarget.NoTarget;
            string outputPath = "";
            string overrideVersion = null;
            string overrideOutputBasePath = null;
            bool clean = false;
            bool forceHybridCLR = false;
            bool forceYooAsset = false;
            bool forceAddressables = false;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-buildTarget" && i + 1 < args.Length)
                {
                    // Try parse enum
                    if (Enum.TryParse(args[i + 1], true, out BuildTarget target))
                    {
                        buildTarget = target;
                    }
                }
                else if (args[i] == "-output" && i + 1 < args.Length)
                {
                    outputPath = args[i + 1];
                }
                else if (args[i] == "-version" && i + 1 < args.Length)
                {
                    overrideVersion = args[i + 1];
                }
                else if (args[i] == "-outputBasePath" && i + 1 < args.Length)
                {
                    overrideOutputBasePath = args[i + 1];
                }
                else if (args[i] == "-clean")
                {
                    clean = true;
                }
                else if (args[i] == "-buildHybridCLR")
                {
                    forceHybridCLR = true;
                }
                else if (args[i] == "-buildYooAsset")
                {
                    forceYooAsset = true;
                }
                else if (args[i] == "-buildAddressables")
                {
                    forceAddressables = true;
                }
            }

            if (buildTarget == BuildTarget.NoTarget)
            {
                Debug.LogError($"{DEBUG_FLAG} No valid -buildTarget provided for CI build.");
                return;
            }

            // Validate asset management system selection (only one can be specified)
            if (forceYooAsset && forceAddressables)
            {
                Debug.LogError($"{DEBUG_FLAG} CI Error: Both -buildYooAsset and -buildAddressables are specified. Only one asset management system can be used at a time.");
                return;
            }

            // Apply overrides from CI args
            if (overrideVersion != null)
            {
                BuildUtils.SetField(buildData, "applicationVersion", overrideVersion);
                Debug.Log($"{DEBUG_FLAG} CI Override: ApplicationVersion set to {overrideVersion}");
            }

            if (overrideOutputBasePath != null)
            {
                BuildUtils.SetField(buildData, "outputBasePath", overrideOutputBasePath);
                Debug.Log($"{DEBUG_FLAG} CI Override: OutputBasePath set to {overrideOutputBasePath}");
            }

            if (forceHybridCLR)
            {
                BuildUtils.SetField(buildData, "useHybridCLR", true);
                Debug.Log($"{DEBUG_FLAG} CI Override: HybridCLR enabled.");
            }

            // Apply asset management system override (only one can be active)
            if (forceYooAsset)
            {
                BuildUtils.SetField(buildData, "assetManagementType", AssetManagementType.YooAsset);
                Debug.Log($"{DEBUG_FLAG} CI Override: YooAsset enabled.");
            }
            else if (forceAddressables)
            {
                BuildUtils.SetField(buildData, "assetManagementType", AssetManagementType.Addressables);
                Debug.Log($"{DEBUG_FLAG} CI Override: Addressables enabled.");
            }
            else
            {
                // Use BuildData configuration if no override is specified
                AssetManagementType currentType = buildData.AssetManagementType;
                if (currentType == AssetManagementType.YooAsset)
                {
                    Debug.Log($"{DEBUG_FLAG} Using YooAsset from BuildData configuration.");
                }
                else if (currentType == AssetManagementType.Addressables)
                {
                    Debug.Log($"{DEBUG_FLAG} Using Addressables from BuildData configuration.");
                }
                else
                {
                    Debug.LogWarning($"{DEBUG_FLAG} No asset management system selected in BuildData. Asset bundles will not be built.");
                }
            }

            // Determine NamedBuildTarget and defaults
            NamedBuildTarget namedTarget = NamedBuildTarget.Standalone;
            bool isFolder = false;

            switch (buildTarget)
            {
                case BuildTarget.Android:
                    namedTarget = NamedBuildTarget.Android;
                    if (outputPath.EndsWith(".apk") || outputPath.EndsWith(".aab")) isFolder = false;
                    else
                    {
                        isFolder = true;
                        EditorUserBuildSettings.exportAsGoogleAndroidProject = true;
                    }
                    break;
                case BuildTarget.StandaloneWindows64:
                    namedTarget = NamedBuildTarget.Standalone;
                    isFolder = false;
                    break;
                case BuildTarget.StandaloneOSX:
                    namedTarget = NamedBuildTarget.Standalone;
                    isFolder = false;
                    break;
                case BuildTarget.WebGL:
                    namedTarget = NamedBuildTarget.WebGL;
                    isFolder = true;
                    break;
                case BuildTarget.iOS:
                    namedTarget = NamedBuildTarget.iOS;
                    isFolder = true;
                    break;
            }

            // Fallback output path if not provided
            if (string.IsNullOrEmpty(outputPath))
            {
                string basePath = buildData.OutputBasePath;
                outputPath = $"{GetPlatformFolderName(buildTarget)}/{ApplicationName}";
                if (!isFolder && buildTarget == BuildTarget.StandaloneWindows64) outputPath += ".exe";
                if (!isFolder && buildTarget == BuildTarget.Android) outputPath += ".apk";
            }

            PerformBuild(
                buildTarget,
                namedTarget,
                ScriptingImplementation.IL2CPP,
                outputPath,
                bCleanBuild: clean,
                bDeleteDebugFiles: true,
                bOutputIsFolderTarget: isFolder);
        }

        #endregion

        private static string GetPlatformFolderName(BuildTarget TargetPlatform)
        {
            switch (TargetPlatform)
            {
                case BuildTarget.Android:
                    return "Android";
                case BuildTarget.StandaloneWindows64:
                    return "Windows";
                case BuildTarget.StandaloneOSX:
                    return "Mac";
                case BuildTarget.iOS:
                    return "iOS";
                case BuildTarget.WebGL:
                    return "WebGL";
                case BuildTarget.NoTarget:
                    return INVALID_FLAG;
            }

            return INVALID_FLAG;
        }

        private static RuntimePlatform GetRuntimePlatformFromBuildTarget(BuildTarget TargetPlatform)
        {
            switch (TargetPlatform)
            {
                case BuildTarget.Android:
                    return RuntimePlatform.Android;
                case BuildTarget.StandaloneWindows64:
                    return RuntimePlatform.WindowsPlayer;
                case BuildTarget.StandaloneOSX:
                    return RuntimePlatform.OSXPlayer;
                case BuildTarget.iOS:
                    return RuntimePlatform.IPhonePlayer;
                case BuildTarget.WebGL:
                    return RuntimePlatform.WebGLPlayer;
            }

            return RuntimePlatform.WindowsPlayer;
        }

        private static BuildData TryGetBuildData()
        {
            return buildData ??= BuildConfigHelper.GetBuildData();
        }

        private static string[] GetBuildSceneList()
        {
            BuildData data = TryGetBuildData();
            if (data == null)
            {
                Debug.LogError($"{DEBUG_FLAG} Invalid Build Data Config. Please create a BuildData asset.");
                return default;
            }

            return new[] { data.GetLaunchScenePath() };
        }

        private static void DeletePlatformBuildFolder(BuildTarget TargetPlatform)
        {
            string platformBuildOutputPath = GetPlatformBuildOutputFolder(TargetPlatform);
            string platformOutputFullPath =
                platformBuildOutputPath != INVALID_FLAG ? Path.GetFullPath(platformBuildOutputPath) : INVALID_FLAG;

            if (Directory.Exists(platformOutputFullPath))
            {
                Debug.Log($"{DEBUG_FLAG} Clean old build {Path.GetFullPath(platformBuildOutputPath)}");
                BuildUtils.DeleteDirectory(platformOutputFullPath);
            }
        }

        private static void DeleteDebugFiles(BuildTarget TargetPlatform)
        {
            string platformBuildOutputPath = GetPlatformBuildOutputFolder(TargetPlatform);
            string platformOutputFullPath =
                platformBuildOutputPath != INVALID_FLAG ? Path.GetFullPath(platformBuildOutputPath) : INVALID_FLAG;

            if (platformOutputFullPath == INVALID_FLAG) return;

            string[] foldersToDelete = new[]
            {
                Path.Combine(platformOutputFullPath, $"{ApplicationName}_BackUpThisFolder_ButDontShipItWithYourGame"),
                Path.Combine(platformOutputFullPath, $"{ApplicationName}_BurstDebugInformation_DoNotShip")
            };

            // Unity might create these folders asynchronously AFTER BuildPlayer returns, especially with IL2CPP.
            // We implement a "watch and kill" strategy: check repeatedly for a few seconds.
            int maxWaitTimeMs = 3000;
            int checkIntervalMs = 500;
            int totalChecks = maxWaitTimeMs / checkIntervalMs;

            Debug.Log($"{DEBUG_FLAG} Starting post-build cleanup monitoring ({maxWaitTimeMs}ms)...");

            for (int i = 0; i < totalChecks; i++)
            {
                foreach (var folderPath in foldersToDelete)
                {
                    if (Directory.Exists(folderPath))
                    {
                        Debug.Log($"{DEBUG_FLAG} Detected unwanted folder: {folderPath}. Deleting...");
                        try
                        {
                            BuildUtils.DeleteDirectory(folderPath);
                            Debug.Log($"{DEBUG_FLAG} Successfully deleted: {folderPath}");
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"{DEBUG_FLAG} Failed to delete debug folder: {folderPath}. Error: {ex.Message}");
                        }
                    }
                }

                // Only sleep if we are not at the last check
                if (i < totalChecks - 1)
                {
                    System.Threading.Thread.Sleep(checkIntervalMs);
                }
            }

            Debug.Log($"{DEBUG_FLAG} Post-build cleanup finished.");
        }

        private static string GetOutputTarget(BuildTarget TargetPlatform, string TargetPath,
            bool bTargetIsFolder = true)
        {
            string platformOutFolder = GetPlatformBuildOutputFolder(TargetPlatform);
            string basePath = buildData != null ? buildData.OutputBasePath : "Build";
            string resultPath = Path.Combine(basePath, TargetPath);

            if (!Directory.Exists(Path.GetFullPath(platformOutFolder)))
            {
                Debug.Log($"{DEBUG_FLAG} result path: {resultPath}, platformFolder: {platformOutFolder}, platform fullPath:{Path.GetFullPath(platformOutFolder)}");
                BuildUtils.CreateDirectory(platformOutFolder);
            }

#if UNITY_IOS
            if (!Directory.Exists($"{resultPath}/Unity-iPhone/Images.xcassets/LaunchImage.launchimage"))
            {
                BuildUtils.CreateDirectory($"{resultPath}/Unity-iPhone/Images.xcassets/LaunchImage.launchimage");
            }
#endif
            return resultPath;
        }

        private static void PerformBuild(BuildTarget TargetPlatform, NamedBuildTarget BuildTargetName,
            ScriptingImplementation BackendScriptImpl, string OutputTarget,
            bool bCleanBuild = true,
            bool bDeleteDebugFiles = true,
            bool bOutputIsFolderTarget = true)
        {
            //  cache curernt scene
            var sceneSetup = EditorSceneManager.GetSceneManagerSetup();
            Debug.Log($"{DEBUG_FLAG} Saving current scene setup.");

            //  force save open scenes.
            EditorSceneManager.SaveOpenScenes();

            //  new template scene for build
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Check if version info asset exists using project-relative path
            bool versionInfoAssetExisted = File.Exists(VersionInfoAssetPath);

            try
            {
                // Load Build Data
                if (buildData == null)
                {
                    buildData = BuildConfigHelper.GetBuildData();
                }

                var previousTarget = EditorUserBuildSettings.activeBuildTarget;

                if (bCleanBuild)
                {
                    DeletePlatformBuildFolder(TargetPlatform);
                }

                if (bCleanBuild && previousTarget != TargetPlatform)
                {
                    Debug.Log($"{DEBUG_FLAG} Clean build & Platform switch detected: {previousTarget} -> {TargetPlatform}. Clearing caches...");
                    TryClearPlatformSwitchCaches();
                }

                if (TargetPlatform != BuildTarget.Android)
                {
                    EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
                }

                if (buildData != null && buildData.UseBuildalon)
                {
                    BuildalonIntegrator.SyncSolution();
                }

                // HybridCLR Generation & Copy
                if (buildData != null && buildData.UseHybridCLR)
                {
                    // This ensures DLLs are compiled, metadata generated, and then copied to HotUpdateDLL folder with .bytes extension
                    // ready for YooAsset to pack them.
                    HybridCLRBuilder.GenerateAllAndCopy();
                }

                InitializeVersionControl(DefaultVersionControlType);
                string commitHash = VersionControlProvider?.GetCommitHash();
                string commitCount = VersionControlProvider?.GetCommitCount();
                VersionControlProvider?.UpdateVersionInfoAsset(VersionInfoAssetPath, commitHash, commitCount);

                string buildNumber = string.IsNullOrEmpty(commitCount) ? "0" : commitCount;
                string appVersion = buildData != null ? buildData.ApplicationVersion : "v0.1";
                string fullBuildVersion = $"{appVersion}.{buildNumber}";

                // Asset Management Build
                if (buildData != null)
                {
                    if (buildData.UseYooAsset)
                    {
                        // Note: YooAsset build should happen AFTER HybridCLR copy, 
                        // because YooAsset needs to pack the .bytes files generated by HybridCLR.
                        YooAssetBuilder.Build(TargetPlatform, fullBuildVersion);
                    }
                    else if (buildData.UseAddressables)
                    {
                        // Note: Addressables build should happen AFTER HybridCLR copy,
                        // because Addressables may need to include the .bytes files generated by HybridCLR.
                        AddressablesBuilder.Build(TargetPlatform, fullBuildVersion);
                    }
                }

                Debug.Log($"{DEBUG_FLAG} Start Build, Platform: {EditorUserBuildSettings.activeBuildTarget}");

                if (EditorUserBuildSettings.activeBuildTarget != TargetPlatform)
                {
                    Debug.Log($"{DEBUG_FLAG} Switching active build target to {TargetPlatform}...");
                    EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetName, TargetPlatform);
                }
                else
                {
                    Debug.Log($"{DEBUG_FLAG} Active build target already {TargetPlatform}, skipping switch.");
                }

                // After target switch, refresh assets and optionally sync solution/build scripts
                AssetDatabase.SaveAssets();

                if (buildData != null && buildData.UseBuildalon)
                {
                    BuildalonIntegrator.SyncSolution();
                }

                // Clean Addressables player content if Addressables is being used
                if (buildData != null && buildData.UseAddressables)
                {
                    TryCleanAddressablesPlayerContent();
                }

                // Save original PlayerSettings values for restoration
                string originalVersion = PlayerSettings.bundleVersion;
                string originalCompanyName = PlayerSettings.companyName;
                string originalProductName = PlayerSettings.productName;
                string originalApplicationIdentifier = PlayerSettings.GetApplicationIdentifier(BuildTargetName);

                try
                {
                    PlayerSettings.SetScriptingBackend(BuildTargetName, BackendScriptImpl);
                    PlayerSettings.companyName = CompanyName;
                    PlayerSettings.productName = ApplicationName;
                    PlayerSettings.bundleVersion = fullBuildVersion;
                    PlayerSettings.SetApplicationIdentifier(BuildTargetName, $"com.{CompanyName}.{ApplicationName}");

                    // Force save PlayerSettings changes
                    AssetDatabase.SaveAssets();

                    BuildReport buildReport;

                    {
                        var buildPlayerOptions = new BuildPlayerOptions();
                        buildPlayerOptions.scenes = GetBuildSceneList();
                        buildPlayerOptions.locationPathName = GetOutputTarget(TargetPlatform, OutputTarget, bOutputIsFolderTarget);
                        buildPlayerOptions.target = TargetPlatform;
                        buildPlayerOptions.options = BuildOptions.None;

                        if (bCleanBuild)
                        {
                            buildPlayerOptions.options |= BuildOptions.CleanBuildCache;
                        }

                        buildPlayerOptions.options |= BuildOptions.CompressWithLz4;
                        buildReport = BuildPipeline.BuildPlayer(buildPlayerOptions);
                    }

                    var summary = buildReport.summary;
                    if (summary.result == BuildResult.Succeeded)
                    {
                        if (bDeleteDebugFiles)
                        {
                            string platformNameStr = GetPlatformFolderName(TargetPlatform);
                            if (platformNameStr == "Windows" || platformNameStr == "Mac") // TODO: May Linux
                            {
                                DeleteDebugFiles(TargetPlatform);
                            }
                        }

                        Debug.Log($"{DEBUG_FLAG} Build <color=#29ff50>SUCCESS</color>, size: {summary.totalSize} bytes, path: {summary.outputPath}\n");
                    }

                    if (summary.result == BuildResult.Failed) Debug.Log($"{DEBUG_FLAG} Build <color=red>FAILURE</color>");
                }
                finally
                {
                    // Restore original PlayerSettings values
                    Debug.Log($"{DEBUG_FLAG} Restoring original PlayerSettings...");
                    PlayerSettings.bundleVersion = originalVersion;
                    PlayerSettings.companyName = originalCompanyName;
                    PlayerSettings.productName = originalProductName;
                    PlayerSettings.SetApplicationIdentifier(BuildTargetName, originalApplicationIdentifier);

                    // Force save restored PlayerSettings
                    AssetDatabase.SaveAssets();
                    
                    Debug.Log($"{DEBUG_FLAG} PlayerSettings restored successfully.");
                }
            }
            finally
            {
                if (versionInfoAssetExisted)
                {
                    VersionControlProvider?.ClearVersionInfoAsset(VersionInfoAssetPath);
                }
                else
                {
                    if (File.Exists(VersionInfoAssetPath))
                    {
                        AssetDatabase.DeleteAsset(VersionInfoAssetPath);
                    }
                }

                Debug.Log($"{DEBUG_FLAG} Restoring original scene setup.");
                if (sceneSetup != null && sceneSetup.Length > 0)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(sceneSetup);
                }
            }
        }

        private static string GetPlatformBuildOutputFolder(BuildTarget TargetPlatform)
        {
            string basePath = buildData != null ? buildData.OutputBasePath : "Build";
            return $"{basePath}/{GetPlatformFolderName(TargetPlatform)}";
        }

        // Clears common Unity caches that often cause cross-platform build failures (Bee, IL2CPP, Burst, PlayerData)
        private static void TryClearPlatformSwitchCaches()
        {
            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string libraryPath = Path.Combine(projectRoot, "Library");
                string tempPath = Path.Combine(projectRoot, "Temp");

                string[] cacheDirs = new[]
                {
                    Path.Combine(libraryPath, "Bee"),
                    Path.Combine(libraryPath, "Il2cppBuildCache"),
                    Path.Combine(libraryPath, "BurstCache"),
                    Path.Combine(libraryPath, "PlayerDataCache"),
                    Path.Combine(libraryPath, "BuildPlayerDataCache"),
                    Path.Combine(tempPath, "gradleOut"),
                    Path.Combine(tempPath, "PlayBackEngine")
                };

                foreach (var dir in cacheDirs)
                {
                    if (Directory.Exists(dir))
                    {
                        Debug.Log($"{DEBUG_FLAG} Deleting cache folder: {dir}");
                        try { Directory.Delete(dir, true); }
                        catch (Exception ex) { Debug.LogWarning($"{DEBUG_FLAG} Failed to delete {dir}: {ex.Message}"); }
                    }
                }

                TryPurgeUnityBuildCache();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{DEBUG_FLAG} TryClearPlatformSwitchCaches encountered a non-fatal error: {ex.Message}");
            }
        }

        private static void TryPurgeUnityBuildCache()
        {
            try
            {
                var editorAssemblies = System.AppDomain.CurrentDomain.GetAssemblies();
                foreach (var asm in editorAssemblies)
                {
                    var buildCacheType = asm.GetType("UnityEditor.Build.BuildCache");
                    if (buildCacheType == null) continue;
                    MethodInfo purgeMethod = null;
                    foreach (var m in buildCacheType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    {
                        if (m.Name != "PurgeCache") continue;
                        var parameters = m.GetParameters();
                        if (parameters.Length == 0 ||
                            (parameters.Length == 1 && parameters[0].ParameterType == typeof(bool)))
                        {
                            purgeMethod = m;
                            break;
                        }
                    }
                    if (purgeMethod != null)
                    {
                        if (purgeMethod.GetParameters().Length == 1)
                        {
                            purgeMethod.Invoke(null, new object[] { true });
                        }
                        else
                        {
                            purgeMethod.Invoke(null, null);
                        }
                        Debug.Log($"{DEBUG_FLAG} UnityEditor.Build.BuildCache purged.");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{DEBUG_FLAG} Unable to purge Unity build cache via reflection: {ex.Message}");
            }
        }

        private static void TryCleanAddressablesPlayerContent()
        {
            try
            {
                var assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
                foreach (var asm in assemblies)
                {
                    var addrType = asm.GetType("UnityEditor.AddressableAssets.Settings.AddressableAssetSettings");
                    if (addrType == null) continue;

                    MethodInfo cleanMethod = null;
                    foreach (var m in addrType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    {
                        if (m.Name != "CleanPlayerContent") continue;
                        if (m.GetParameters().Length == 0)
                        {
                            cleanMethod = m;
                            break;
                        }
                    }
                    if (cleanMethod != null)
                    {
                        cleanMethod.Invoke(null, null);
                        Debug.Log($"{DEBUG_FLAG} Addressables CleanPlayerContent executed.");
                        return;
                    }

                    var getSettingsMethod = addrType.GetMethod("Default", BindingFlags.Public | BindingFlags.Static) ??
                                            addrType.GetProperty("Default", BindingFlags.Public | BindingFlags.Static)?.GetGetMethod();
                    var settingsInstance = getSettingsMethod != null ? getSettingsMethod.Invoke(null, null) : null;
                    if (settingsInstance != null)
                    {
                        MethodInfo cleanWithSettings = null;
                        foreach (var m in addrType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                        {
                            if (m.Name != "CleanPlayerContent") continue;
                            var ps = m.GetParameters();
                            if (ps.Length == 1 && ps[0].ParameterType == addrType)
                            {
                                cleanWithSettings = m;
                                break;
                            }
                        }
                        if (cleanWithSettings != null)
                        {
                            cleanWithSettings.Invoke(null, new[] { settingsInstance });
                            Debug.Log($"{DEBUG_FLAG} Addressables CleanPlayerContent(settings) executed.");
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{DEBUG_FLAG} Addressables clean skipped: {ex.Message}");
            }
        }
    }
}