using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using CycloneGames.Editor.VersionControl;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using System.Reflection;

namespace CycloneGames.Editor.Build
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
        private const string ApplicationVersion = "v0.1";
        private const string OutputBasePath = "Build";
        private const string BuildDataConfig = "Assets/UnityStarter/Editor/Build/BuildData.asset";
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
                Debug.LogError(
                    $"{DEBUG_FLAG} Invalid scene list, please check the file <color=cyan>{BuildDataConfig}</color>");
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
                bDeleteDebugFiles: false,
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
                bDeleteDebugFiles: false,
                bOutputIsFolderTarget: false);
        }

        #endregion

        #region CI/CD

        /// <summary>
        /// Entry point for CI/CD. Parses command line arguments to configure the build.
        /// Usage: -executeMethod CycloneGames.Editor.Build.BuildScript.PerformBuild_CI -buildTarget <Target> -output <Path> [-clean] [-buildHybridCLR] [-buildYooAsset]
        /// </summary>
        public static void PerformBuild_CI()
        {
            Debug.Log($"{DEBUG_FLAG} Starting CI Build...");

            // Parse arguments
            string[] args = System.Environment.GetCommandLineArgs();
            BuildTarget buildTarget = BuildTarget.NoTarget;
            string outputPath = "";
            bool clean = false;
            bool forceHybridCLR = false;
            bool forceYooAsset = false;
            
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
            }

            if (buildTarget == BuildTarget.NoTarget)
            {
                Debug.LogError($"{DEBUG_FLAG} No valid -buildTarget provided for CI build.");
                return;
            }

            // Load Build Data to ensure it's in memory
            TryGetBuildData();

            // Apply overrides from CI args using Reflection if needed
            if (buildData != null)
            {
                if (forceHybridCLR)
                {
                    BuildUtils.SetField(buildData, "useHybridCLR", true);
                    Debug.Log($"{DEBUG_FLAG} CI Override: HybridCLR enabled.");
                }
                if (forceYooAsset)
                {
                    BuildUtils.SetField(buildData, "useYooAsset", true);
                    Debug.Log($"{DEBUG_FLAG} CI Override: YooAsset enabled.");
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
            return buildData ??= AssetDatabase.LoadAssetAtPath<BuildData>($"{BuildDataConfig}");
        }

        private static string[] GetBuildSceneList()
        {
            if (!TryGetBuildData())
            {
                Debug.LogError(
                    $"{DEBUG_FLAG} Invalid Build Data Config, please check the file <color=cyan>{BuildDataConfig}</color>");
                return default;
            }

            return new[] { TryGetBuildData().GetLaunchScenePath() };
        }

        private static void DeletePlatformBuildFolder(BuildTarget TargetPlatform)
        {
            string platformBuildOutputPath = GetPlatformBuildOutputFolder(TargetPlatform);
            string platformOutputFullPath =
                platformBuildOutputPath != INVALID_FLAG ? Path.GetFullPath(platformBuildOutputPath) : INVALID_FLAG;

            if (Directory.Exists(platformOutputFullPath))
            {
                Debug.Log($"{DEBUG_FLAG} Clean old build {Path.GetFullPath(platformBuildOutputPath)}");
                Directory.Delete(platformOutputFullPath, true);
            }
        }

        private static void DeleteDebugFiles(BuildTarget TargetPlatform)
        {
            string platformBuildOutputPath = GetPlatformBuildOutputFolder(TargetPlatform);
            string platformOutputFullPath =
                platformBuildOutputPath != INVALID_FLAG ? Path.GetFullPath(platformBuildOutputPath) : INVALID_FLAG;

            string BackUpPath = Path.Combine(platformOutputFullPath, $"{ApplicationName}_BackUpThisFolder_ButDontShipItWithYourGame");
            if (Directory.Exists(BackUpPath))
            {
                Debug.Log($"{DEBUG_FLAG} Delete Backup Folder: {Path.GetFullPath(BackUpPath)}");
                Directory.Delete(BackUpPath, true);
            }

            string BurstDebugPath = Path.Combine(platformOutputFullPath, $"{ApplicationName}_BurstDebugInformation_DoNotShip");
            if (Directory.Exists(BurstDebugPath))
            {
                Debug.Log($"{DEBUG_FLAG} Delete Burst Debug Folder: {Path.GetFullPath(BurstDebugPath)}");
                Directory.Delete(BurstDebugPath, true);
            }
        }

        private static string GetOutputTarget(BuildTarget TargetPlatform, string TargetPath,
            bool bTargetIsFolder = true)
        {
            string platformOutFolder = GetPlatformBuildOutputFolder(TargetPlatform);
            string resultPath = Path.Combine(OutputBasePath, TargetPath);

            if (!Directory.Exists(Path.GetFullPath(platformOutFolder)))
            {
                Debug.Log($"{DEBUG_FLAG} result path: {resultPath}, platformFolder: {platformOutFolder}, platform fullPath:{Path.GetFullPath(platformOutFolder)}");
                Directory.CreateDirectory(platformOutFolder);
            }

#if UNITY_IOS
            if (!Directory.Exists($"{resultPath}/Unity-iPhone/Images.xcassets/LaunchImage.launchimage"))
            {
                Directory.CreateDirectory($"{resultPath}/Unity-iPhone/Images.xcassets/LaunchImage.launchimage");
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
                TryGetBuildData();
                
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
                string fullBuildVersion = $"{ApplicationVersion}.{buildNumber}";

                // YooAsset Build
                if (buildData != null && buildData.UseYooAsset)
                {
                    // Note: YooAsset build should happen AFTER HybridCLR copy, 
                    // because YooAsset needs to pack the .bytes files generated by HybridCLR.
                    YooAssetBuilder.Build(TargetPlatform, fullBuildVersion);
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
                
                TryCleanAddressablesPlayerContent();

                string originalVersion = PlayerSettings.bundleVersion;

                PlayerSettings.SetScriptingBackend(BuildTargetName, BackendScriptImpl);
                PlayerSettings.companyName = CompanyName;
                PlayerSettings.productName = ApplicationName;
                PlayerSettings.bundleVersion = fullBuildVersion;
                PlayerSettings.SetApplicationIdentifier(BuildTargetName, $"com.{CompanyName}.{ApplicationName}");

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

                PlayerSettings.bundleVersion = originalVersion;
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
            return $"{OutputBasePath}/{GetPlatformFolderName(TargetPlatform)}";
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