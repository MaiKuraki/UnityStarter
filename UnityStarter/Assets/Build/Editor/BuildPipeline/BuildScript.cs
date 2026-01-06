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

        [MenuItem("Build/Print Debug Info", priority = 10)]
        public static void PrintDebugInfo()
        {
            const string Separator = "-------------------------------------------------------------------------";

            // Load BuildData
            buildData = TryGetBuildData();
            if (buildData == null)
            {
                Debug.LogError($"{DEBUG_FLAG} BuildData not found. Please create a BuildData asset.");
                return;
            }

            var sb = new System.Text.StringBuilder(2048);
            var warnings = new System.Collections.Generic.List<string>();

            sb.AppendLine();
            sb.AppendLine("=======================================================");
            sb.AppendLine("                             Build Configuration Debug Info");
            sb.AppendLine("=======================================================");

            // Basic Build Info
            sb.AppendLine();
            sb.AppendLine("[Basic Build Configuration]");
            sb.AppendLine(Separator);
            sb.AppendLine($"  Application Version\t\t{buildData.ApplicationVersion}");
            sb.AppendLine($"  Output Base Path\t\t{buildData.OutputBasePath}");
            sb.AppendLine($"  Active Build Target\t\t{EditorUserBuildSettings.activeBuildTarget}");

            // Scene Configuration
            var sceneList = GetBuildSceneList();
            sb.AppendLine();
            sb.AppendLine("[Build Scenes]");
            sb.AppendLine(Separator);
            if (sceneList == null || sceneList.Length == 0)
            {
                sb.AppendLine("  [!] No scenes configured");
                warnings.Add("Invalid scene list, please check BuildData configuration.");
            }
            else
            {
                for (int i = 0; i < sceneList.Length; i++)
                {
                    string sceneName = System.IO.Path.GetFileNameWithoutExtension(sceneList[i]);
                    sb.AppendLine($"  [{i + 1}] {sceneName}");
                }
            }

            // Buildalon Configuration
            sb.AppendLine();
            sb.AppendLine("[Buildalon Configuration]");
            sb.AppendLine(Separator);
            sb.AppendLine($"  Use Buildalon\t\t\t{(buildData.UseBuildalon ? "[Y] Enabled" : "[N] Disabled")}");

            // HybridCLR Configuration
            bool hybridCLREnabled = buildData.UseHybridCLR;
            sb.AppendLine();
            sb.AppendLine("[HybridCLR Configuration]");
            sb.AppendLine(Separator);
            sb.AppendLine($"  Use HybridCLR\t\t{(hybridCLREnabled ? "[Y] Enabled" : "[N] Disabled")}");

            if (hybridCLREnabled)
            {
                HybridCLRBuildConfig hybridCLRConfig = BuildConfigHelper.GetHybridCLRConfig();
                if (hybridCLRConfig != null)
                {
                    sb.AppendLine($"  Config Asset\t\t\t[Y] Found");
                    sb.AppendLine($"  Obfuz in HybridCLR\t\t{(hybridCLRConfig.enableObfuz ? "[Y] Enabled" : "[N] Disabled")}");

                    string aotDllOutputDir = hybridCLRConfig.GetAOTDllOutputDirectoryPath();
                    if (!string.IsNullOrEmpty(aotDllOutputDir))
                    {
                        sb.AppendLine($"  AOT DLL Output Dir\t\t{aotDllOutputDir}");
                    }
                    else
                    {
                        sb.AppendLine($"  AOT DLL Output Dir\t\t[!] Not configured");
                        warnings.Add("AOT DLL Output Directory not configured (required for HybridCLR)");
                    }
                }
                else
                {
                    sb.AppendLine($"  Config Asset\t\t\t[!] Not found");
                    warnings.Add("HybridCLR Config Asset not found (please create HybridCLRBuildConfig asset)");
                }
            }

            // Obfuz Configuration
            bool obfuzEnabledInBuildData = buildData.UseObfuz;
            bool baseObfuzAvailable = ObfuzIntegrator.IsBaseObfuzAvailable();
            bool hybridCLRObfuzAvailable = ObfuzIntegrator.IsHybridCLRObfuzAvailable();

            sb.AppendLine();
            sb.AppendLine("[Obfuz Configuration]");
            sb.AppendLine(Separator);
            sb.AppendLine($"  Use Obfuz (BuildData)\t\t{(obfuzEnabledInBuildData ? "[Y] Enabled" : "[N] Disabled")}");
            sb.AppendLine($"  Base Obfuz Package\t\t{(baseObfuzAvailable ? "[Y] Available" : "[N] Not Available")}");
            sb.AppendLine($"  Obfuz4HybridCLR Pkg\t\t{(hybridCLRObfuzAvailable ? "[Y] Available" : "[N] Not Available")}");

            if (obfuzEnabledInBuildData && !baseObfuzAvailable)
            {
                warnings.Add("Obfuz is enabled but base Obfuz package is not available!");
            }

            if (hybridCLREnabled && obfuzEnabledInBuildData && !hybridCLRObfuzAvailable)
            {
                warnings.Add("HybridCLR + Obfuz enabled but Obfuz4HybridCLR package is not available!");
            }

            // Determine effective Obfuz state
            bool effectiveObfuzEnabled = obfuzEnabledInBuildData;
            if (!effectiveObfuzEnabled && hybridCLREnabled)
            {
                HybridCLRBuildConfig hybridCLRConfig = BuildConfigHelper.GetHybridCLRConfig();
                if (hybridCLRConfig != null)
                {
                    effectiveObfuzEnabled = hybridCLRConfig.enableObfuz;
                }
            }
            sb.AppendLine($"  Effective Obfuz State\t\t{(effectiveObfuzEnabled ? "[Y] Enabled" : "[N] Disabled")}");

            // Asset Management Configuration
            AssetManagementType assetManagementType = buildData.AssetManagementType;
            sb.AppendLine();
            sb.AppendLine("[Asset Management Configuration]");
            sb.AppendLine(Separator);
            sb.AppendLine($"  Asset Management Type\t{assetManagementType}");

            if (assetManagementType == AssetManagementType.YooAsset)
            {
                YooAssetBuildConfig yooAssetConfig = BuildConfigHelper.GetYooAssetConfig();
                sb.AppendLine($"  YooAsset Config\t\t{(yooAssetConfig != null ? "[Y] Found" : "[!] Not found")}");
                if (yooAssetConfig == null)
                {
                    warnings.Add("YooAsset Config Asset not found (please create YooAssetBuildConfig asset)");
                }
            }
            else if (assetManagementType == AssetManagementType.Addressables)
            {
                AddressablesBuildConfig addressablesConfig = BuildConfigHelper.GetAddressablesConfig();
                sb.AppendLine($"  Addressables Config\t\t{(addressablesConfig != null ? "[Y] Found" : "[!] Not found")}");
                if (addressablesConfig == null)
                {
                    warnings.Add("Addressables Config Asset not found (please create AddressablesBuildConfig asset)");
                }
            }
            else
            {
                sb.AppendLine($"  Status\t\t\t\t[N] No asset management");
            }

            // Version Control Configuration
            string commitHash = INVALID_FLAG;
            string commitCount = INVALID_FLAG;
            string fullBuildVersion = INVALID_FLAG;
            string vcStatus = "[Y] Available";

            try
            {
                InitializeVersionControl(DefaultVersionControlType);
                if (VersionControlProvider != null)
                {
                    commitHash = VersionControlProvider.GetCommitHash();
                    commitCount = VersionControlProvider.GetCommitCount();
                    fullBuildVersion = $"{buildData.ApplicationVersion}.{commitCount}";
                }
                else
                {
                    vcStatus = "[!] Not available";
                    warnings.Add("Version Control Provider not available");
                }
            }
            catch (Exception ex)
            {
                vcStatus = "[!] Error";
                warnings.Add($"Failed to get version control info: {ex.Message}");
            }

            sb.AppendLine();
            sb.AppendLine("[Version Control Configuration]");
            sb.AppendLine(Separator);
            sb.AppendLine($"  Version Control Type\t\t{DefaultVersionControlType}");
            sb.AppendLine($"  VC Status\t\t\t{vcStatus}");
            sb.AppendLine($"  Current Commit Hash\t\t{commitHash}");
            sb.AppendLine($"  Commit Count\t\t\t{commitCount}");
            sb.AppendLine($"  Full Build Version\t\t{fullBuildVersion}");

            // Build Target Configuration
            BuildTarget currentTarget = EditorUserBuildSettings.activeBuildTarget;
            NamedBuildTarget namedTarget = GetNamedBuildTargetFromBuildTarget(currentTarget);
            ScriptingImplementation scriptingBackend = PlayerSettings.GetScriptingBackend(namedTarget);

            sb.AppendLine();
            sb.AppendLine("[Build Target Configuration]");
            sb.AppendLine(Separator);
            sb.AppendLine($"  Current Build Target\t\t{currentTarget}");
            sb.AppendLine($"  Scripting Backend\t\t{scriptingBackend}");
            sb.AppendLine($"  API Compatibility\t\t{PlayerSettings.GetApiCompatibilityLevel(namedTarget)}");

            sb.AppendLine();
            sb.AppendLine("=======================================================");

            // Output single log message
            Debug.Log($"{DEBUG_FLAG}{sb}");

            // Output warnings separately for visibility
            foreach (var warning in warnings)
            {
                Debug.LogWarning($"{DEBUG_FLAG} [!] {warning}");
            }
        }


        /// <summary>
        /// Gets NamedBuildTarget from BuildTarget.
        /// </summary>
        private static NamedBuildTarget GetNamedBuildTargetFromBuildTarget(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.Android:
                    return NamedBuildTarget.Android;
                case BuildTarget.iOS:
                    return NamedBuildTarget.iOS;
                case BuildTarget.WebGL:
                    return NamedBuildTarget.WebGL;
                case BuildTarget.StandaloneWindows64:
                case BuildTarget.StandaloneOSX:
                case BuildTarget.StandaloneLinux64:
                    return NamedBuildTarget.Standalone;
                default:
                    return NamedBuildTarget.Standalone;
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

        [MenuItem("Build/Game(Release)/Build Linux (IL2CPP)", priority = 14)]
        public static void PerformBuild_Linux()
        {
            PerformBuild(
                BuildTarget.StandaloneLinux64,
                NamedBuildTarget.Standalone,
                ScriptingImplementation.IL2CPP,
                $"{GetPlatformFolderName(BuildTarget.StandaloneLinux64)}/{ApplicationName}",
                bCleanBuild: true,
                bDeleteDebugFiles: true,
                bOutputIsFolderTarget: false);
        }

        [MenuItem("Build/Game(Release)/Build WebGL", priority = 15)]
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

        [MenuItem("Build/Game(Release)/Build iOS (IL2CPP)", priority = 16)]
        public static void PerformBuild_iOS()
        {
            PerformBuild(
                BuildTarget.iOS,
                NamedBuildTarget.iOS,
                ScriptingImplementation.IL2CPP,
                $"{GetPlatformFolderName(BuildTarget.iOS)}/{ApplicationName}",
                bCleanBuild: true,
                bDeleteDebugFiles: true,
                bOutputIsFolderTarget: true);
        }

        [MenuItem("Build/Game(Release)/Export Android Project (IL2CPP)", priority = 17)]
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

        [MenuItem("Build/Game(Release)/Fast/Build Android APK (Fast)", priority = 18)]
        public static void PerformBuild_AndroidAPK_Fast()
        {
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
            PerformBuild(
                BuildTarget.Android,
                NamedBuildTarget.Android,
                ScriptingImplementation.IL2CPP,
                $"{GetPlatformFolderName(BuildTarget.Android)}/{ApplicationName}.apk",
                bCleanBuild: false,
                bDeleteDebugFiles: true,
                bOutputIsFolderTarget: false,
                bIsFastBuild: true);
        }

        [MenuItem("Build/Game(Release)/Fast/Build Windows (Fast)", priority = 19)]
        public static void PerformBuild_Windows_Fast()
        {
            PerformBuild(
                BuildTarget.StandaloneWindows64,
                NamedBuildTarget.Standalone,
                ScriptingImplementation.IL2CPP,
                $"{GetPlatformFolderName(BuildTarget.StandaloneWindows64)}/{ApplicationName}.exe",
                bCleanBuild: false,
                bDeleteDebugFiles: true,
                bOutputIsFolderTarget: false,
                bIsFastBuild: true);
        }

        [MenuItem("Build/Game(Release)/Fast/Build Mac (Fast)", priority = 20)]
        public static void PerformBuild_Mac_Fast()
        {
            PerformBuild(
                BuildTarget.StandaloneOSX,
                NamedBuildTarget.Standalone,
                ScriptingImplementation.IL2CPP,
                $"{GetPlatformFolderName(BuildTarget.StandaloneOSX)}/{ApplicationName}.app",
                bCleanBuild: false,
                bDeleteDebugFiles: true,
                bOutputIsFolderTarget: false,
                bIsFastBuild: true);
        }

        [MenuItem("Build/Game(Release)/Fast/Build Linux (Fast)", priority = 21)]
        public static void PerformBuild_Linux_Fast()
        {
            PerformBuild(
                BuildTarget.StandaloneLinux64,
                NamedBuildTarget.Standalone,
                ScriptingImplementation.IL2CPP,
                $"{GetPlatformFolderName(BuildTarget.StandaloneLinux64)}/{ApplicationName}",
                bCleanBuild: false,
                bDeleteDebugFiles: true,
                bOutputIsFolderTarget: false,
                bIsFastBuild: true);
        }

        [MenuItem("Build/Game(Release)/Fast/Build iOS (Fast)", priority = 23)]
        public static void PerformBuild_iOS_Fast()
        {
            PerformBuild(
                BuildTarget.iOS,
                NamedBuildTarget.iOS,
                ScriptingImplementation.IL2CPP,
                $"{GetPlatformFolderName(BuildTarget.iOS)}/{ApplicationName}",
                bCleanBuild: false,
                bDeleteDebugFiles: true,
                bOutputIsFolderTarget: true,
                bIsFastBuild: true);
        }

        [MenuItem("Build/Game(Release)/Fast/Build WebGL (Fast)", priority = 24)]
        public static void PerformBuild_WebGL_Fast()
        {
            PerformBuild(
                BuildTarget.WebGL,
                NamedBuildTarget.WebGL,
                ScriptingImplementation.IL2CPP,
                $"{GetPlatformFolderName(BuildTarget.WebGL)}/{ApplicationName}",
                bCleanBuild: false,
                bDeleteDebugFiles: true,
                bOutputIsFolderTarget: true,
                bIsFastBuild: true);
        }

        [MenuItem("Build/Game(Release)/Fast/Export Android Project (Fast)", priority = 25)]
        public static void PerformBuild_AndroidProject_Fast()
        {
            EditorUserBuildSettings.exportAsGoogleAndroidProject = true;
            PerformBuild(
                BuildTarget.Android,
                NamedBuildTarget.Android,
                ScriptingImplementation.IL2CPP,
                $"{GetPlatformFolderName(BuildTarget.Android)}/{ApplicationName}",
                bCleanBuild: false,
                bDeleteDebugFiles: true,
                bOutputIsFolderTarget: true,
                bIsFastBuild: true);
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

        [MenuItem("Build/Game(Debug)/Build Linux (Debug)", priority = 23)]
        public static void PerformBuild_Linux_Debug()
        {
            PerformBuild(
                BuildTarget.StandaloneLinux64,
                NamedBuildTarget.Standalone,
                ScriptingImplementation.IL2CPP,
                $"{GetPlatformFolderName(BuildTarget.StandaloneLinux64)}/{ApplicationName}",
                bCleanBuild: true,
                bDeleteDebugFiles: false,
                bOutputIsFolderTarget: false,
                bIsDebugBuild: true);
        }

        [MenuItem("Build/Game(Debug)/Build WebGL (Debug)", priority = 24)]
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

        [MenuItem("Build/Game(Debug)/Export Android Project (Debug)", priority = 26)]
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
                bDeleteDebugFiles: false,
                bOutputIsFolderTarget: false,
                bIsDebugBuild: true,
                bIsFastBuild: true);
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
                bDeleteDebugFiles: false,
                bOutputIsFolderTarget: false,
                bIsDebugBuild: true,
                bIsFastBuild: true);
        }

        [MenuItem("Build/Game(Debug)/Fast/Build Mac (Debug Fast)", priority = 28)]
        public static void PerformBuild_Mac_Debug_Fast()
        {
            PerformBuild(
                BuildTarget.StandaloneOSX,
                NamedBuildTarget.Standalone,
                ScriptingImplementation.IL2CPP,
                $"{GetPlatformFolderName(BuildTarget.StandaloneOSX)}/{ApplicationName}.app",
                bCleanBuild: false,
                bDeleteDebugFiles: false,
                bOutputIsFolderTarget: false,
                bIsDebugBuild: true,
                bIsFastBuild: true);
        }

        [MenuItem("Build/Game(Debug)/Fast/Build Linux (Debug Fast)", priority = 29)]
        public static void PerformBuild_Linux_Debug_Fast()
        {
            PerformBuild(
                BuildTarget.StandaloneLinux64,
                NamedBuildTarget.Standalone,
                ScriptingImplementation.IL2CPP,
                $"{GetPlatformFolderName(BuildTarget.StandaloneLinux64)}/{ApplicationName}",
                bCleanBuild: false,
                bDeleteDebugFiles: false,
                bOutputIsFolderTarget: false,
                bIsDebugBuild: true,
                bIsFastBuild: true);
        }

        [MenuItem("Build/Game(Debug)/Fast/Build iOS (Debug Fast)", priority = 30)]
        public static void PerformBuild_iOS_Debug_Fast()
        {
            PerformBuild(
                BuildTarget.iOS,
                NamedBuildTarget.iOS,
                ScriptingImplementation.IL2CPP,
                $"{GetPlatformFolderName(BuildTarget.iOS)}/{ApplicationName}",
                bCleanBuild: false,
                bDeleteDebugFiles: false,
                bOutputIsFolderTarget: true,
                bIsDebugBuild: true,
                bIsFastBuild: true);
        }

        [MenuItem("Build/Game(Debug)/Fast/Build WebGL (Debug Fast)", priority = 31)]
        public static void PerformBuild_WebGL_Debug_Fast()
        {
            PerformBuild(
                BuildTarget.WebGL,
                NamedBuildTarget.WebGL,
                ScriptingImplementation.IL2CPP,
                $"{GetPlatformFolderName(BuildTarget.WebGL)}/{ApplicationName}",
                bCleanBuild: false,
                bDeleteDebugFiles: false,
                bOutputIsFolderTarget: true,
                bIsDebugBuild: true,
                bIsFastBuild: true);
        }

        [MenuItem("Build/Game(Debug)/Fast/Export Android Project (Debug Fast)", priority = 32)]
        public static void PerformBuild_AndroidProject_Debug_Fast()
        {
            EditorUserBuildSettings.exportAsGoogleAndroidProject = true;
            PerformBuild(
                BuildTarget.Android,
                NamedBuildTarget.Android,
                ScriptingImplementation.IL2CPP,
                $"{GetPlatformFolderName(BuildTarget.Android)}/{ApplicationName}",
                bCleanBuild: false,
                bDeleteDebugFiles: false,
                bOutputIsFolderTarget: true,
                bIsDebugBuild: true,
                bIsFastBuild: true);
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
                case BuildTarget.StandaloneLinux64:
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
                // Note: Linux executable has no extension by default
            }

            PerformBuild(
                buildTarget,
                namedTarget,
                ScriptingImplementation.IL2CPP,
                outputPath,
                bCleanBuild: clean,
                bDeleteDebugFiles: !isDebugBuild,
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
                case BuildTarget.StandaloneLinux64:
                    return "Linux";
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
                case BuildTarget.StandaloneLinux64:
                    return RuntimePlatform.LinuxPlayer;
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
            bool bIsDebugBuild = false,
            bool bIsFastBuild = false)
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

                // Switch platform BEFORE HybridCLR operations to ensure correct platform DLLs are generated
                Debug.Log($"{DEBUG_FLAG} Preparing for build, Current Platform: {EditorUserBuildSettings.activeBuildTarget}, Target Platform: {TargetPlatform}");

                if (EditorUserBuildSettings.activeBuildTarget != TargetPlatform)
                {
                    Debug.Log($"{DEBUG_FLAG} Switching active build target to {TargetPlatform}...");
                    EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetName, TargetPlatform);
                }
                else
                {
                    Debug.Log($"{DEBUG_FLAG} Active build target already {TargetPlatform}, skipping switch.");
                }

                // Buildalon SyncSolution should be called AFTER platform switch to ensure project files reflect the correct platform configuration
                AssetDatabase.SaveAssets();

                if (buildData != null && buildData.UseBuildalon)
                {
                    Debug.Log($"{DEBUG_FLAG} Syncing solution after platform switch...");
                    BuildalonIntegrator.SyncSolution();
                }

                // HybridCLR Generation & Copy
                if (buildData != null && buildData.UseHybridCLR)
                {
                    if (bIsFastBuild)
                    {
                        // Fast build: Only compile DLLs and copy (skip full generation)
                        Debug.Log($"{DEBUG_FLAG} Using HybridCLR FastBuild (CompileDllAndCopy) for platform: {TargetPlatform}...");
                        HybridCLRBuilder.CompileDllAndCopy(TargetPlatform);
                    }
                    else
                    {
                        // Full build: Generate all metadata and compile DLLs, then copy
                        Debug.Log($"{DEBUG_FLAG} Using HybridCLR FullBuild (GenerateAllAndCopy) for platform: {TargetPlatform}...");
                        HybridCLRBuilder.GenerateAllAndCopy(TargetPlatform);
                    }
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
                        // Clean up StreamingAssets/aa before build to prevent file conflicts
                        // Unity's Addressables will copy files from Library to StreamingAssets during BuildPlayer,
                        // so we need to ensure no stale files exist that would cause conflicts
                        AddressablesBuildConfig addressablesConfig = BuildConfigHelper.GetAddressablesConfig();
                        AddressablesBuilder.CleanupStreamingAssetsAddressables();
                        AddressablesBuilder.Build(TargetPlatform, fullBuildVersion, addressablesConfig);    
                    }
                }

                Debug.Log($"{DEBUG_FLAG} Start Build, Platform: {EditorUserBuildSettings.activeBuildTarget}");

                if (buildData != null && buildData.UseAddressables)
                {
                    TryCleanAddressablesPlayerContent();
                }

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

                        if (buildData != null && buildData.UseObfuz && ObfuzIntegrator.IsBaseObfuzAvailable())
                        {
                            Debug.Log($"{DEBUG_FLAG} Initializing ObfuzSettings before build...");
                            ObfuzIntegrator.ForceInitializeObfuzSettings();

                            Debug.Log($"{DEBUG_FLAG} Ensuring Encryption VM is generated and compiled (adaptive timeout)...");
                            try
                            {
                                ObfuzIntegrator.EnsureEncryptionVMGeneratedAndCompiled();
                                Debug.Log($"{DEBUG_FLAG} Encryption VM is ready for obfuscation.");
                            }
                            catch (Exception ex)
                            {
                                Debug.LogError($"{DEBUG_FLAG} Failed to ensure Encryption VM is compiled: {ex.Message}");
                                Debug.LogError($"{DEBUG_FLAG} Build will be cancelled to prevent obfuscation failure.");
                                throw new BuildFailedException($"Encryption VM is not compiled. Please generate it manually via Obfuz > GenerateEncryptionVM menu and wait for compilation to complete before building. Error: {ex.Message}");
                            }
                        }

                        buildReport = BuildPipeline.BuildPlayer(buildPlayerOptions);
                    }

                    var summary = buildReport.summary;
                    if (summary.result == BuildResult.Succeeded)
                    {
                        // Don't delete debug files for debug builds
                        if (bDeleteDebugFiles && !bIsDebugBuild)
                        {
                            string platformNameStr = GetPlatformFolderName(TargetPlatform);
                            if (platformNameStr == "Windows" || platformNameStr == "Mac" || platformNameStr == "Linux")
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