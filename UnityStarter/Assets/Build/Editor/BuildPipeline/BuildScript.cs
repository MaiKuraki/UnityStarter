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

        [MenuItem("Build/Game(Release)/Print Debug Info", priority = 10)]
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

        #region Menu Items - Release Builds (Clean)

        [MenuItem("Build/Game(Release)/Build Android APK (IL2CPP)", priority = 11)]
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

        [MenuItem("Build/Game(Release)/Build Windows (IL2CPP)", priority = 12)]
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

        [MenuItem("Build/Game(Release)/Build Mac (IL2CPP)", priority = 13)]
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

        [MenuItem("Build/Game(Release)/Build WebGL", priority = 14)]
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

        [MenuItem("Build/Game(Release)/Export Android Project (IL2CPP)", priority = 15)]
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

        #endregion

        #region Menu Items - Fast Builds (No Clean)

        [MenuItem("Build/Game(Release)/Fast/Build Android APK (Fast)", priority = 16)]
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

        [MenuItem("Build/Game(Release)/Fast/Build Windows (Fast)", priority = 17)]
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

        #region Menu Items - Debug Builds

        [MenuItem("Build/Game(Debug)/Build Android APK (Debug)", priority = 20)]
        public static void PerformBuild_AndroidAPK_Debug()
        {
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
            PerformBuild(
                BuildTarget.Android,
                NamedBuildTarget.Android,
                ScriptingImplementation.IL2CPP,
                $"{GetPlatformFolderName(BuildTarget.Android)}/{ApplicationName}.apk",
                bCleanBuild: true,
                bDeleteDebugFiles: false,
                bOutputIsFolderTarget: false,
                bIsDebugBuild: true);
        }

        [MenuItem("Build/Game(Debug)/Build Windows (Debug)", priority = 21)]
        public static void PerformBuild_Windows_Debug()
        {
            PerformBuild(
                BuildTarget.StandaloneWindows64,
                NamedBuildTarget.Standalone,
                ScriptingImplementation.IL2CPP,
                $"{GetPlatformFolderName(BuildTarget.StandaloneWindows64)}/{ApplicationName}.exe",
                bCleanBuild: true,
                bDeleteDebugFiles: false,
                bOutputIsFolderTarget: false,
                bIsDebugBuild: true);
        }

        [MenuItem("Build/Game(Debug)/Build Mac (Debug)", priority = 22)]
        public static void PerformBuild_Mac_Debug()
        {
            PerformBuild(
                BuildTarget.StandaloneOSX,
                NamedBuildTarget.Standalone,
                ScriptingImplementation.IL2CPP,
                $"{GetPlatformFolderName(BuildTarget.StandaloneOSX)}/{ApplicationName}.app",
                bCleanBuild: true,
                bDeleteDebugFiles: false,
                bOutputIsFolderTarget: false,
                bIsDebugBuild: true);
        }

        [MenuItem("Build/Game(Debug)/Export Android Project (Debug)", priority = 24)]
        public static void PerformBuild_AndroidProject_Debug()
        {
            EditorUserBuildSettings.exportAsGoogleAndroidProject = true;
            PerformBuild(
                BuildTarget.Android,
                NamedBuildTarget.Android,
                ScriptingImplementation.IL2CPP,
                $"{GetPlatformFolderName(BuildTarget.Android)}/{ApplicationName}",
                bCleanBuild: true,
                bDeleteDebugFiles: false,
                bOutputIsFolderTarget: true,
                bIsDebugBuild: true);
        }

        [MenuItem("Build/Game(Debug)/Build WebGL (Debug)", priority = 23)]
        public static void PerformBuild_WebGL_Debug()
        {
            PerformBuild(
                BuildTarget.WebGL,
                NamedBuildTarget.WebGL,
                ScriptingImplementation.IL2CPP,
                $"{GetPlatformFolderName(BuildTarget.WebGL)}/{ApplicationName}",
                bCleanBuild: true,
                bDeleteDebugFiles: false,
                bOutputIsFolderTarget: true,
                bIsDebugBuild: true);
        }

        [MenuItem("Build/Game(Debug)/Build iOS (Debug)", priority = 25)]
        public static void PerformBuild_iOS_Debug()
        {
            PerformBuild(
                BuildTarget.iOS,
                NamedBuildTarget.iOS,
                ScriptingImplementation.IL2CPP,
                $"{GetPlatformFolderName(BuildTarget.iOS)}/{ApplicationName}",
                bCleanBuild: true,
                bDeleteDebugFiles: false,
                bOutputIsFolderTarget: true,
                bIsDebugBuild: true);
        }

        #endregion

        #region Menu Items - Debug Fast Builds (No Clean)

        [MenuItem("Build/Game(Debug)/Fast/Build Android APK (Debug Fast)", priority = 26)]
        public static void PerformBuild_AndroidAPK_Debug_Fast()
        {
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
            PerformBuild(
                BuildTarget.Android,
                NamedBuildTarget.Android,
                ScriptingImplementation.IL2CPP,
                $"{GetPlatformFolderName(BuildTarget.Android)}/{ApplicationName}.apk",
                bCleanBuild: false,
                bDeleteDebugFiles: false, // Keep debug files for debug builds
                bOutputIsFolderTarget: false,
                bIsDebugBuild: true);
        }

        [MenuItem("Build/Game(Debug)/Fast/Build Windows (Debug Fast)", priority = 27)]
        public static void PerformBuild_Windows_Debug_Fast()
        {
            PerformBuild(
                BuildTarget.StandaloneWindows64,
                NamedBuildTarget.Standalone,
                ScriptingImplementation.IL2CPP,
                $"{GetPlatformFolderName(BuildTarget.StandaloneWindows64)}/{ApplicationName}.exe",
                bCleanBuild: false,
                bDeleteDebugFiles: false, // Keep debug files for debug builds
                bOutputIsFolderTarget: false,
                bIsDebugBuild: true);
        }

        #endregion

        #region CI/CD

        /// <summary>
        /// Entry point for CI/CD. Parses command line arguments to configure the build.
        /// Usage: -executeMethod Build.Pipeline.Editor.BuildScript.PerformBuild_CI -buildTarget <Target> -output <Path> [-clean] [-debug] [-buildHybridCLR] [-buildYooAsset] [-buildAddressables] [-version <Version>] [-outputBasePath <Path>]
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
            bool isDebugBuild = false;
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
                else if (args[i] == "-debug")
                {
                    isDebugBuild = true;
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
                bDeleteDebugFiles: !isDebugBuild, // Keep debug files for debug builds
                bOutputIsFolderTarget: isFolder,
                bIsDebugBuild: isDebugBuild);
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

        /// <summary>
        /// Adds "Debug" suffix to output path for debug builds.
        /// Example: "Windows/UnityStarter.exe" -> "Windows/UnityStarter_Debug.exe"
        /// </summary>
        private static string AddDebugSuffixToOutputPath(string outputPath, bool isFolderTarget)
        {
            if (string.IsNullOrEmpty(outputPath))
                return "Debug";

            if (isFolderTarget)
            {
                // For folder targets, add "_Debug" before the folder name
                string directory = Path.GetDirectoryName(outputPath);
                string folderName = Path.GetFileName(outputPath);
                if (string.IsNullOrEmpty(directory))
                {
                    return $"{folderName}_Debug";
                }
                return Path.Combine(directory, $"{folderName}_Debug");
            }
            else
            {
                // For file targets, add "_Debug" before the extension
                string directory = Path.GetDirectoryName(outputPath);
                string fileName = Path.GetFileNameWithoutExtension(outputPath);
                string extension = Path.GetExtension(outputPath);
                string newFileName = $"{fileName}_Debug{extension}";

                if (string.IsNullOrEmpty(directory))
                {
                    return newFileName;
                }
                return Path.Combine(directory, newFileName);
            }
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
            bool bOutputIsFolderTarget = true,
            bool bIsDebugBuild = false)
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
                // Note: Asset build should happen AFTER HybridCLR copy, 
                // because Asset needs to pack the .bytes files generated by HybridCLR.
                if (buildData != null)
                {
                    if (buildData.UseYooAsset)
                    {
                        YooAssetBuildConfig yooassetBuildConfig = BuildConfigHelper.GetYooAssetConfig();
                        YooAssetBuilder.Build(TargetPlatform, fullBuildVersion, yooassetBuildConfig);
                    }
                    else if (buildData.UseAddressables)
                    {
                        AddressablesBuildConfig addressablesConfig = BuildConfigHelper.GetAddressablesConfig();
                        AddressablesBuilder.Build(TargetPlatform, fullBuildVersion, addressablesConfig);
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

                        // For debug builds, add "Debug" suffix to output path
                        string finalOutputTarget = bIsDebugBuild ? AddDebugSuffixToOutputPath(OutputTarget, bOutputIsFolderTarget) : OutputTarget;
                        buildPlayerOptions.locationPathName = GetOutputTarget(TargetPlatform, finalOutputTarget, bOutputIsFolderTarget);
                        buildPlayerOptions.target = TargetPlatform;
                        buildPlayerOptions.options = BuildOptions.None;

                        if (bCleanBuild)
                        {
                            buildPlayerOptions.options |= BuildOptions.CleanBuildCache;
                        }

                        // Debug build options
                        if (bIsDebugBuild)
                        {
                            buildPlayerOptions.options |= BuildOptions.Development;
                            buildPlayerOptions.options |= BuildOptions.AllowDebugging;

                            // Enable Autoconnect Profiler for Debug builds
                            buildPlayerOptions.options |= BuildOptions.ConnectWithProfiler;

                            Debug.Log($"{DEBUG_FLAG} <color=yellow>Debug Build Configuration:</color>");
                            Debug.Log($"{DEBUG_FLAG}   ✓ Development Build: Enabled");
                            Debug.Log($"{DEBUG_FLAG}   ✓ Allow Debugging: Enabled");
                            Debug.Log($"{DEBUG_FLAG}   ✓ Autoconnect Profiler: Enabled");
                            Debug.Log($"{DEBUG_FLAG} <color=cyan>Info:</color> The built game will automatically connect to Unity Profiler on startup.");
                            Debug.Log($"{DEBUG_FLAG} <color=cyan>Note:</color> Autoconnect Profiler may increase startup time by 5-10 seconds.");
                        }

                        buildPlayerOptions.options |= BuildOptions.CompressWithLz4;
                        buildReport = BuildPipeline.BuildPlayer(buildPlayerOptions);
                    }

                    var summary = buildReport.summary;
                    if (summary.result == BuildResult.Succeeded)
                    {
                        // Don't delete debug files for debug builds
                        if (bDeleteDebugFiles && !bIsDebugBuild)
                        {
                            string platformNameStr = GetPlatformFolderName(TargetPlatform);
                            if (platformNameStr == "Windows" || platformNameStr == "Mac") // TODO: May Linux
                            {
                                DeleteDebugFiles(TargetPlatform);
                            }
                        }
                        else if (bIsDebugBuild)
                        {
                            Debug.Log($"{DEBUG_FLAG} <color=yellow>Debug build:</color> Debug files preserved for debugging.");
                        }

                        string buildType = bIsDebugBuild ? "Debug" : "Release";
                        Debug.Log($"{DEBUG_FLAG} Build <color=#29ff50>SUCCESS</color> ({buildType}), size: {summary.totalSize} bytes, path: {summary.outputPath}\n");
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

                // Cleanup auto-created Addressables version files
                if (buildData != null && buildData.UseAddressables)
                {
                    AddressablesBuilder.CleanupAutoCreatedVersionFiles();
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
                Type addrType = ReflectionCache.GetType("UnityEditor.AddressableAssets.Settings.AddressableAssetSettings");
                if (addrType == null)
                {
                    return;
                }

                // Try CleanPlayerContent() without parameters first
                MethodInfo cleanMethod = ReflectionCache.GetMethod(addrType, "CleanPlayerContent", BindingFlags.Public | BindingFlags.Static);
                if (cleanMethod != null && cleanMethod.GetParameters().Length == 0)
                {
                    cleanMethod.Invoke(null, null);
                    Debug.Log($"{DEBUG_FLAG} Addressables CleanPlayerContent executed.");
                    return;
                }

                // Try AddressableAssetSettingsDefaultObject.GetSettings() first (correct API)
                object settingsInstance = null;
                Type defaultObjectType = ReflectionCache.GetType("UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject");
                if (defaultObjectType != null)
                {
                    MethodInfo getSettingsMethod = ReflectionCache.GetMethod(defaultObjectType, "GetSettings", BindingFlags.Public | BindingFlags.Static);
                    if (getSettingsMethod != null)
                    {
                        try
                        {
                            settingsInstance = getSettingsMethod.Invoke(null, new object[] { false });
                        }
                        catch
                        {
                            try
                            {
                                settingsInstance = getSettingsMethod.Invoke(null, new object[] { true });
                            }
                            catch
                            {
                                // Fall through to fallback
                            }
                        }
                    }
                }

                // Fallback: Try AddressableAssetSettings.Default (older API)
                if (settingsInstance == null)
                {
                    MethodInfo defaultMethod = ReflectionCache.GetMethod(addrType, "Default", BindingFlags.Public | BindingFlags.Static);
                    if (defaultMethod != null)
                    {
                        settingsInstance = defaultMethod.Invoke(null, null);
                    }
                    else
                    {
                        PropertyInfo defaultProp = ReflectionCache.GetProperty(addrType, "Default", BindingFlags.Public | BindingFlags.Static);
                        if (defaultProp != null)
                        {
                            settingsInstance = defaultProp.GetValue(null);
                        }
                    }
                }

                if (settingsInstance != null)
                {
                    // Try CleanPlayerContent(AddressableAssetSettings) with settings parameter
                    MethodInfo cleanWithSettings = ReflectionCache.GetMethod(addrType, "CleanPlayerContent", BindingFlags.Public | BindingFlags.Static);
                    if (cleanWithSettings != null)
                    {
                        var ps = cleanWithSettings.GetParameters();
                        if (ps.Length == 1 && ps[0].ParameterType == addrType)
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