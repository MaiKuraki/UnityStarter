using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    public static class HybridCLRBuilder
    {
        private const string DEBUG_FLAG = "<color=cyan>[HybridCLR]</color>";

        [MenuItem("Build/HybridCLR/Generate All", priority = 100)]
        public static void Build()
        {
            Debug.Log($"{DEBUG_FLAG} Checking availability...");

            // Use Reflection to avoid compilation errors if HybridCLR is not installed
            Type prebuildCommandType = ReflectionCache.GetType("HybridCLR.Editor.Commands.PrebuildCommand");
            Type installerControllerType = ReflectionCache.GetType("HybridCLR.Editor.Installer.InstallerController");

            if (prebuildCommandType == null)
            {
                Debug.LogWarning($"{DEBUG_FLAG} HybridCLR package not found. Skipping generation.");
                return;
            }

            // Check installation status and install if needed
            if (installerControllerType != null)
            {
                try
                {
                    object installer = Activator.CreateInstance(installerControllerType);
                    MethodInfo hasInstalledMethod = ReflectionCache.GetMethod(installerControllerType, "HasInstalledHybridCLR", BindingFlags.Public | BindingFlags.Instance);

                    bool isInstalled = false;
                    if (hasInstalledMethod != null)
                    {
                        isInstalled = (bool)hasInstalledMethod.Invoke(installer, null);
                    }

                    if (!isInstalled)
                    {
                        Debug.LogWarning($"{DEBUG_FLAG} HybridCLR not initialized. Attempting to install default version...");
                        MethodInfo installMethod = ReflectionCache.GetMethod(installerControllerType, "InstallDefaultHybridCLR", BindingFlags.Public | BindingFlags.Instance);
                        if (installMethod != null)
                        {
                            installMethod.Invoke(installer, null);
                            Debug.Log($"{DEBUG_FLAG} HybridCLR installed successfully.");
                        }
                        else
                        {
                            Debug.LogError($"{DEBUG_FLAG} Could not find InstallDefaultHybridCLR method.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"{DEBUG_FLAG} Failed to check/install HybridCLR: {ex.Message}");
                    // Don't throw here, let GenerateAll try and fail if it must, or maybe it works now.
                }
            }

            Debug.Log($"{DEBUG_FLAG} Start generating all...");
            try
            {
                // Call PrebuildCommand.GenerateAll()
                MethodInfo generateAllMethod = ReflectionCache.GetMethod(prebuildCommandType, "GenerateAll", BindingFlags.Public | BindingFlags.Static);
                if (generateAllMethod != null)
                {
                    generateAllMethod.Invoke(null, null);
                    Debug.Log($"{DEBUG_FLAG} Generation success.");
                }
                else
                {
                    Debug.LogError($"{DEBUG_FLAG} GenerateAll method not found in PrebuildCommand.");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"{DEBUG_FLAG} Generation failed: {e.Message}");
                // We choose NOT to throw here to allow the build to proceed if HybridCLR fails, 
                // but usually this is fatal for hotfix. Uncomment throw if strict.
                throw;
            }
        }

        [MenuItem("Build/HybridCLR/Compile DLL Only (Fast)", priority = 101)]
        public static void CompileDllOnly()
        {
            Debug.Log($"{DEBUG_FLAG} Start compiling DLLs...");
            Type compileDllCommandType = ReflectionCache.GetType("HybridCLR.Editor.Commands.CompileDllCommand");
            if (compileDllCommandType == null)
            {
                Debug.LogWarning($"{DEBUG_FLAG} HybridCLR package not found.");
                return;
            }

            try
            {
                // CompileDllCommand.CompileDll(BuildTarget target);
                // Note: GetMethod with parameter types requires direct call, cache key would be complex
                MethodInfo compileDllMethod = compileDllCommandType.GetMethod("CompileDll", new Type[] { typeof(BuildTarget) });
                if (compileDllMethod != null)
                {
                    compileDllMethod.Invoke(null, new object[] { EditorUserBuildSettings.activeBuildTarget });
                    Debug.Log($"{DEBUG_FLAG} Compile DLL success.");
                }
                else
                {
                    Debug.LogError($"{DEBUG_FLAG} CompileDll method not found.");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"{DEBUG_FLAG} Compile DLL failed: {e.Message}");
                throw;
            }
        }

        [MenuItem("Build/HybridCLR/Pipeline: Generate All + Copy", priority = 200)]
        public static void GenerateAllAndCopy()
        {
            Build();
            CopyHotUpdateDlls();
        }

        [MenuItem("Build/HybridCLR/Pipeline: Compile DLL + Copy (Fast)", priority = 201)]
        public static void CompileDllAndCopy()
        {
            CompileDllOnly();
            CopyHotUpdateDlls();
        }

        [MenuItem("Build/HybridCLR/Copy HotUpdate DLLs", priority = 102)]
        public static void CopyHotUpdateDlls()
        {
            HybridCLRBuildConfig config = GetConfig();
            if (config == null)
            {
                Debug.LogError($"{DEBUG_FLAG} Config not found. Please create a HybridCLRBuildConfig asset.");
                return;
            }

            // Cache config values immediately to avoid potential loss during execution
            string targetDirRelative = config.hotUpdateDllOutputDirectory;
            var assemblyNames = config.GetHotUpdateAssemblyNames();

            Debug.Log($"{DEBUG_FLAG} Using Config -> OutputDir: {targetDirRelative}, Assemblies: {assemblyNames.Count}");

            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            string outputDir = GetHybridCLROutputDir(target);

            if (string.IsNullOrEmpty(outputDir) || !Directory.Exists(outputDir))
            {
                Debug.LogError($"{DEBUG_FLAG} HybridCLR output directory not found: {outputDir}. Please run 'Generate All' first.");
                return;
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string destinationDir = Path.Combine(projectRoot, targetDirRelative);

            // Use BuildUtils to ensure directory exists
            BuildUtils.CreateDirectory(destinationDir);

            if (assemblyNames.Count == 0)
            {
                Debug.LogWarning($"{DEBUG_FLAG} No hot update assemblies defined in config.");
                return;
            }

            int copyCount = 0;
            foreach (var asmName in assemblyNames)
            {
                string srcFile = Path.Combine(outputDir, $"{asmName}.dll");
                string dstFile = Path.Combine(destinationDir, $"{asmName}.dll.bytes");

                if (File.Exists(srcFile))
                {
                    // Use BuildUtils for copying. 
                    // Note: We do NOT clear the destination directory to preserve existing .meta files.
                    // Overwriting ensures GUIDs remain stable if files are updated.
                    BuildUtils.CopyFile(srcFile, dstFile, true);
                    Debug.Log($"{DEBUG_FLAG} Copied: {asmName}.dll -> {targetDirRelative}/{asmName}.dll.bytes");
                    copyCount++;
                }
                else
                {
                    Debug.LogError($"{DEBUG_FLAG} HotUpdate DLL not found: {srcFile}");
                }
            }

            if (copyCount > 0)
            {
                Debug.Log($"{DEBUG_FLAG} Successfully copied {copyCount} assemblies. Refreshing AssetDatabase...");
                AssetDatabase.Refresh();
            }
        }

        private static HybridCLRBuildConfig GetConfig()
        {
            return BuildConfigHelper.GetHybridCLRConfig();
        }

        private static string GetHybridCLROutputDir(BuildTarget target)
        {
            // Try to get path via HybridCLR SettingsUtil reflection
            Type settingsUtilType = ReflectionCache.GetType("HybridCLR.Editor.Settings.SettingsUtil");
            if (settingsUtilType != null)
            {
                MethodInfo getDirMethod = ReflectionCache.GetMethod(settingsUtilType, "GetHotUpdateDllsOutputDirByTarget", BindingFlags.Public | BindingFlags.Static);
                if (getDirMethod != null)
                {
                    return (string)getDirMethod.Invoke(null, new object[] { target });
                }
            }

            // Fallback to standard path structure
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(projectRoot, "HybridCLRData", "HotUpdateDlls", target.ToString());
        }
    }
}