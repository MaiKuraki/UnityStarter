using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    public static class HybridCLRBuilder
    {
        /// <summary>
        /// Serializable wrapper for AOT assembly list.
        /// </summary>
        [Serializable]
        private class AOTAssemblyList
        {
            public List<string> assemblies;
        }

        /// <summary>
        /// Serializable wrapper for HotUpdate assembly list (reused for Cheat as well).
        /// </summary>
        [Serializable]
        private class HotUpdateAssemblyList
        {
            public List<string> assemblies;
        }

        private const string DEBUG_FLAG = "<color=cyan>[HybridCLR]</color>";

        [MenuItem("Build/HybridCLR/Generate All", priority = 100)]
        public static void Build()
        {
            Build(EditorUserBuildSettings.activeBuildTarget);
        }

        public static void Build(BuildTarget target)
        {
            Debug.Log($"{DEBUG_FLAG} Checking availability for platform: {target}...");

            // Use Reflection to avoid compilation errors if HybridCLR is not installed
            Type prebuildCommandType = ReflectionCache.GetType("HybridCLR.Editor.Commands.PrebuildCommand");
            Type installerControllerType = ReflectionCache.GetType("HybridCLR.Editor.Installer.InstallerController");

            if (prebuildCommandType == null)
            {
                Debug.LogWarning($"{DEBUG_FLAG} HybridCLR package not found. Skipping generation.");
                return;
            }

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

            BuildTarget currentTarget = EditorUserBuildSettings.activeBuildTarget;
            if (currentTarget != target)
            {
                Debug.LogWarning($"{DEBUG_FLAG} Warning: Current active build target ({currentTarget}) does not match requested target ({target}). " +
                    $"PrebuildCommand.GenerateAll() will use the active target. Consider switching platform first.");
            }

            Debug.Log($"{DEBUG_FLAG} Start generating all for platform: {target}...");
            try
            {
                MethodInfo generateAllMethod = ReflectionCache.GetMethod(prebuildCommandType, "GenerateAll", BindingFlags.Public | BindingFlags.Static);
                if (generateAllMethod != null)
                {
                    generateAllMethod.Invoke(null, null);
                    Debug.Log($"{DEBUG_FLAG} Generation success for platform: {target}.");
                }
                else
                {
                    Debug.LogError($"{DEBUG_FLAG} GenerateAll method not found in PrebuildCommand.");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"{DEBUG_FLAG} Generation failed for platform {target}: {e.Message}");
                throw;
            }
        }

        [MenuItem("Build/HybridCLR/Compile DLL Only (Fast)", priority = 101)]
        public static void CompileDllOnly()
        {
            CompileDllOnly(EditorUserBuildSettings.activeBuildTarget);
        }

        public static void CompileDllOnly(BuildTarget target)
        {
            Debug.Log($"{DEBUG_FLAG} Start compiling DLLs for platform: {target}...");
            Type compileDllCommandType = ReflectionCache.GetType("HybridCLR.Editor.Commands.CompileDllCommand");
            if (compileDllCommandType == null)
            {
                Debug.LogWarning($"{DEBUG_FLAG} HybridCLR package not found.");
                return;
            }

            try
            {
                MethodInfo compileDllMethod = compileDllCommandType.GetMethod("CompileDll", new Type[] { typeof(BuildTarget) });
                if (compileDllMethod != null)
                {
                    compileDllMethod.Invoke(null, new object[] { target });
                    Debug.Log($"{DEBUG_FLAG} Compile DLL success for platform: {target}.");
                }
                else
                {
                    Debug.LogError($"{DEBUG_FLAG} CompileDll method not found.");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"{DEBUG_FLAG} Compile DLL failed for platform {target}: {e.Message}");
                throw;
            }
        }

        [MenuItem("Build/HybridCLR/Pipeline: Generate All + Copy", priority = 200)]
        public static void GenerateAllAndCopy()
        {
            GenerateAllAndCopy(EditorUserBuildSettings.activeBuildTarget);
        }

        public static void GenerateAllAndCopy(BuildTarget target)
        {
            HybridCLRBuildConfig config = GetConfig();

            bool isObfuzEnabled = IsObfuzEnabled(config);

            // If Obfuz is enabled, ensure prerequisites are generated before Build() is called
            // because Build() -> PrebuildCommand.GenerateAll() -> StripAOTDllCommand may trigger Obfuz
            if (isObfuzEnabled && ObfuzIntegrator.IsAvailable())
            {
                Debug.Log($"{DEBUG_FLAG} Obfuz is enabled. Generating prerequisites...");
                ObfuzIntegrator.EnsureObfuzPrerequisites();
                // Force reload ObfuzSettings to ensure configuration is applied
                ObfuzIntegrator.ForceReloadObfuzSettings();
            }

            Build(target);

            if (isObfuzEnabled && ObfuzIntegrator.IsAvailable())
            {
                Debug.Log($"{DEBUG_FLAG} Obfuz is enabled. Starting obfuscation...");
                string obfuscatedOutputDir = ObfuzIntegrator.GetObfuscatedHotUpdateAssemblyOutputPath(target);
                if (!string.IsNullOrEmpty(obfuscatedOutputDir))
                {
                    ObfuzIntegrator.ObfuscateHotUpdateAssemblies(target, obfuscatedOutputDir);

                    // After obfuscation, regenerate method bridge and AOT generic reference using obfuscated assemblies
                    Debug.Log($"{DEBUG_FLAG} Regenerating method bridge and AOT generic reference using obfuscated assemblies...");
                    ObfuzIntegrator.GenerateMethodBridgeAndReversePInvokeWrapper(target, obfuscatedOutputDir);
                    ObfuzIntegrator.GenerateAOTGenericReference(target, obfuscatedOutputDir);
                }
                else
                {
                    Debug.LogWarning($"{DEBUG_FLAG} Failed to get obfuscated output path. Skipping obfuscation.");
                }
            }

            CopyHotUpdateDlls(target);
        }

        [MenuItem("Build/HybridCLR/Pipeline: Compile DLL + Copy (Fast)", priority = 201)]
        public static void CompileDllAndCopy()
        {
            CompileDllAndCopy(EditorUserBuildSettings.activeBuildTarget);
        }

        public static void CompileDllAndCopy(BuildTarget target)
        {
            HybridCLRBuildConfig config = GetConfig();

            bool isObfuzEnabled = IsObfuzEnabled(config);

            if (isObfuzEnabled && ObfuzIntegrator.IsAvailable())
            {
                Debug.Log($"{DEBUG_FLAG} Obfuz is enabled. Generating prerequisites...");
                ObfuzIntegrator.EnsureObfuzPrerequisites();
                // Force reload ObfuzSettings to ensure configuration is applied
                ObfuzIntegrator.ForceReloadObfuzSettings();
            }

            CompileDllOnly(target);

            if (isObfuzEnabled && ObfuzIntegrator.IsAvailable())
            {
                Debug.Log($"{DEBUG_FLAG} Obfuz is enabled. Starting obfuscation...");
                string obfuscatedOutputDir = ObfuzIntegrator.GetObfuscatedHotUpdateAssemblyOutputPath(target);
                if (!string.IsNullOrEmpty(obfuscatedOutputDir))
                {
                    ObfuzIntegrator.ObfuscateHotUpdateAssemblies(target, obfuscatedOutputDir);

                    // After obfuscation, regenerate method bridge and AOT generic reference using obfuscated assemblies
                    // Note: For fast build (CompileDllAndCopy), these steps may not be necessary if Build() was already called
                    // But we include them to ensure consistency when using obfuscated assemblies
                    Debug.Log($"{DEBUG_FLAG} Regenerating method bridge and AOT generic reference using obfuscated assemblies...");
                    ObfuzIntegrator.GenerateMethodBridgeAndReversePInvokeWrapper(target, obfuscatedOutputDir);
                    ObfuzIntegrator.GenerateAOTGenericReference(target, obfuscatedOutputDir);
                }
                else
                {
                    Debug.LogWarning($"{DEBUG_FLAG} Failed to get obfuscated output path. Skipping obfuscation.");
                }
            }

            CopyHotUpdateDlls(target);
        }

        [MenuItem("Build/HybridCLR/Copy HotUpdate DLLs", priority = 102)]
        public static void CopyHotUpdateDlls()
        {
            CopyHotUpdateDlls(EditorUserBuildSettings.activeBuildTarget);
        }

        public static void CopyHotUpdateDlls(BuildTarget target)
        {
            HybridCLRBuildConfig config = GetConfig();
            if (config == null)
            {
                Debug.LogError($"{DEBUG_FLAG} Config not found. Please create a HybridCLRBuildConfig asset.");
                return;
            }

            // Synchronize HybridCLRSettings before copying
            SyncHybridCLRSettings(config);

            // Determine source directory (obfuscated or regular)
            string sourceDir = GetHybridCLROutputDir(target);
            bool useObfuscated = IsObfuzEnabled(config) && ObfuzIntegrator.IsAvailable();

            if (useObfuscated)
            {
                string obfuscatedDir = ObfuzIntegrator.GetObfuscatedHotUpdateAssemblyOutputPath(target);
                if (!string.IsNullOrEmpty(obfuscatedDir) && Directory.Exists(obfuscatedDir))
                {
                    sourceDir = obfuscatedDir;
                    Debug.Log($"{DEBUG_FLAG} Using obfuscated assemblies from: {obfuscatedDir}");
                }
                else
                {
                    Debug.LogWarning($"{DEBUG_FLAG} Obfuz is enabled but obfuscated directory not found. Falling back to regular assemblies.");
                    useObfuscated = false;
                }
            }

            if (string.IsNullOrEmpty(sourceDir) || !Directory.Exists(sourceDir))
            {
                Debug.LogError($"{DEBUG_FLAG} HybridCLR output directory not found: {sourceDir}. Please run 'Generate All' first.");
                return;
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            // Copy HotUpdate DLLs
            CopyHotUpdateDllsInternal(config, target, sourceDir, projectRoot);

            // Copy Cheat DLLs (if configured)
            CopyCheatDllsInternal(config, target, sourceDir, projectRoot);

            // Copy AOT assemblies (required for HybridCLR supplementary metadata)
            CopyAOTDllsInternal(config, target, projectRoot);

            AssetDatabase.Refresh();
        }

        /// <summary>
        /// Internal method to copy HotUpdate DLLs and generate list file.
        /// </summary>
        private static void CopyHotUpdateDllsInternal(HybridCLRBuildConfig config, BuildTarget target, string sourceDir, string projectRoot)
        {
            string targetDirRelative = config.GetHotUpdateDllOutputDirectoryPath();
            var assemblyNames = config.GetHotUpdateAssemblyNames();

            Debug.Log($"{DEBUG_FLAG} Copying HotUpdate DLLs -> OutputDir: {targetDirRelative}, Assemblies: {assemblyNames.Count}");

            if (assemblyNames.Count == 0)
            {
                Debug.LogWarning($"{DEBUG_FLAG} No hot update assemblies defined in config.");
                return;
            }

            string destinationDir = Path.Combine(projectRoot, targetDirRelative);
            BuildUtils.CreateDirectory(destinationDir);
            CleanOldHotUpdateDlls(destinationDir);

            List<string> copiedAssemblyPaths = new List<string>();
            int copyCount = 0;

            foreach (var asmName in assemblyNames)
            {
                string srcFile = Path.Combine(sourceDir, $"{asmName}.dll");
                string dstFile = Path.Combine(destinationDir, $"{asmName}.dll.bytes");

                if (File.Exists(srcFile))
                {
                    BuildUtils.CopyFile(srcFile, dstFile, true);
                    string relativePath = GetRelativePathFromAssets(dstFile);
                    if (!string.IsNullOrEmpty(relativePath))
                    {
                        copiedAssemblyPaths.Add(relativePath);
                    }
                    Debug.Log($"{DEBUG_FLAG} Copied HotUpdate DLL: {asmName}.dll -> {targetDirRelative}/{asmName}.dll.bytes");
                    copyCount++;
                }
                else
                {
                    Debug.LogError($"{DEBUG_FLAG} HotUpdate DLL not found: {srcFile}");
                }
            }

            // Generate HotUpdate list JSON
            if (copiedAssemblyPaths.Count > 0)
            {
                string listPath = Path.Combine(destinationDir, "HotUpdate.bytes");
                GenerateAssemblyList(target, listPath, copiedAssemblyPaths);
            }

            Debug.Log($"{DEBUG_FLAG} Successfully copied {copyCount} HotUpdate assemblies.");
        }

        /// <summary>
        /// Internal method to copy Cheat DLLs and generate list file.
        /// </summary>
        private static void CopyCheatDllsInternal(HybridCLRBuildConfig config, BuildTarget target, string sourceDir, string projectRoot)
        {
            string cheatOutputDir = config.GetCheatDllOutputDirectoryPath();
            if (string.IsNullOrEmpty(cheatOutputDir))
            {
                Debug.Log($"{DEBUG_FLAG} Cheat DLL Output Directory not configured. Skipping Cheat DLL copy.");
                return;
            }

            var cheatAssemblyNames = config.GetCheatAssemblyNames();
            if (cheatAssemblyNames.Count == 0)
            {
                Debug.Log($"{DEBUG_FLAG} No cheat assemblies defined in config. Skipping Cheat DLL copy.");
                return;
            }

            Debug.Log($"{DEBUG_FLAG} Copying Cheat DLLs -> OutputDir: {cheatOutputDir}, Assemblies: {cheatAssemblyNames.Count}");

            string destinationDir = Path.Combine(projectRoot, cheatOutputDir);
            BuildUtils.CreateDirectory(destinationDir);
            CleanOldCheatDlls(destinationDir);

            List<string> copiedAssemblyPaths = new List<string>();
            int copyCount = 0;

            foreach (var asmName in cheatAssemblyNames)
            {
                string srcFile = Path.Combine(sourceDir, $"{asmName}.dll");
                string dstFile = Path.Combine(destinationDir, $"{asmName}.dll.bytes");

                if (File.Exists(srcFile))
                {
                    BuildUtils.CopyFile(srcFile, dstFile, true);
                    string relativePath = GetRelativePathFromAssets(dstFile);
                    if (!string.IsNullOrEmpty(relativePath))
                    {
                        copiedAssemblyPaths.Add(relativePath);
                    }
                    Debug.Log($"{DEBUG_FLAG} Copied Cheat DLL: {asmName}.dll -> {cheatOutputDir}/{asmName}.dll.bytes");
                    copyCount++;
                }
                else
                {
                    Debug.LogWarning($"{DEBUG_FLAG} Cheat DLL not found: {srcFile} (this is normal if cheat DLL is not compiled)");
                }
            }

            // Generate Cheat list JSON
            if (copiedAssemblyPaths.Count > 0)
            {
                string listPath = Path.Combine(destinationDir, "Cheat.bytes");
                GenerateAssemblyList(target, listPath, copiedAssemblyPaths);
            }

            Debug.Log($"{DEBUG_FLAG} Successfully copied {copyCount} Cheat assemblies.");
        }

        /// <summary>
        /// Internal method to copy AOT DLLs and generate list file.
        /// </summary>
        private static void CopyAOTDllsInternal(HybridCLRBuildConfig config, BuildTarget target, string projectRoot)
        {
            string aotOutputDir = config.GetAOTDllOutputDirectoryPath();
            if (string.IsNullOrEmpty(aotOutputDir))
            {
                Debug.LogError($"{DEBUG_FLAG} AOT DLL Output Directory is not configured! " +
                    $"This is required for HybridCLR supplementary metadata generation. " +
                    $"Please configure the AOT DLL Output Directory in HybridCLRBuildConfig.");
                throw new Exception("AOT DLL Output Directory must be configured for HybridCLR builds.");
            }

            string aotSourceDir = GetAOTDllSourceDir(target);
            if (!string.IsNullOrEmpty(aotSourceDir))
            {
                if (Directory.Exists(aotSourceDir))
                {
                    string aotDestinationDir = Path.Combine(projectRoot, aotOutputDir);
                    CopyAOTAssembliesAndGenerateList(target, aotSourceDir, aotDestinationDir);
                    Debug.Log($"{DEBUG_FLAG} AOT assemblies copied and list generated from: {aotSourceDir}");
                }
                else
                {
                    Debug.LogWarning($"{DEBUG_FLAG} AOT source directory not found: {aotSourceDir}. " +
                        $"This directory is created by HybridCLR's CopyStrippedAOTAssemblies build processor. " +
                        $"If you're running GenerateAllAndCopy, the AOT DLLs should be available after the build completes. " +
                        $"You may need to run CopyHotUpdateDlls separately after the build.");
                }
            }
            else
            {
                Debug.LogWarning($"{DEBUG_FLAG} Failed to get AOT source directory path. Skipping AOT copy.");
            }
        }

        private static HybridCLRBuildConfig GetConfig()
        {
            return BuildConfigHelper.GetHybridCLRConfig();
        }

        /// <summary>
        /// Synchronizes HybridCLRSettings.asset's hotUpdateAssemblies list with HybridCLRBuildConfig.
        /// Adds missing assemblies and removes extra assemblies based on config.
        /// Note: This syncs the string list (hotUpdateAssemblies), not the AssemblyDefinitionAsset references.
        /// </summary>
        private static void SyncHybridCLRSettings(HybridCLRBuildConfig config)
        {
            try
            {
                // Load HybridCLRSettings asset
                string settingsPath = "ProjectSettings/HybridCLRSettings.asset";
                UnityEngine.Object settingsAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(settingsPath);

                if (settingsAsset == null)
                {
                    Debug.LogWarning($"{DEBUG_FLAG} HybridCLRSettings.asset not found at {settingsPath}. Cannot synchronize.");
                    return;
                }

                // Use SerializedObject to modify the asset
                SerializedObject serializedSettings = new SerializedObject(settingsAsset);
                SerializedProperty hotUpdateAssembliesProp = serializedSettings.FindProperty("hotUpdateAssemblies");

                if (hotUpdateAssembliesProp == null)
                {
                    Debug.LogWarning($"{DEBUG_FLAG} hotUpdateAssemblies property not found in HybridCLRSettings. Cannot synchronize.");
                    return;
                }

                // Get target assembly names from config (HotUpdate + Cheat)
                var targetAssemblyNames = config.GetAllHotUpdateAssemblyNames();
                Debug.Log($"{DEBUG_FLAG} Syncing HybridCLRSettings.hotUpdateAssemblies with {targetAssemblyNames.Count} assemblies from config.");

                // Get current assembly names in HybridCLRSettings
                HashSet<string> currentAssemblyNames = new HashSet<string>();
                for (int i = 0; i < hotUpdateAssembliesProp.arraySize; i++)
                {
                    SerializedProperty element = hotUpdateAssembliesProp.GetArrayElementAtIndex(i);
                    string assemblyName = element.stringValue;
                    if (!string.IsNullOrEmpty(assemblyName))
                    {
                        currentAssemblyNames.Add(assemblyName);
                    }
                }

                // Check if synchronization is needed
                bool needsSync = false;
                if (targetAssemblyNames.Count != currentAssemblyNames.Count)
                {
                    needsSync = true;
                }
                else
                {
                    foreach (var targetName in targetAssemblyNames)
                    {
                        if (!currentAssemblyNames.Contains(targetName))
                        {
                            needsSync = true;
                            break;
                        }
                    }
                }

                if (!needsSync)
                {
                    Debug.Log($"{DEBUG_FLAG} HybridCLRSettings.hotUpdateAssemblies is already in sync. No changes needed.");
                    return;
                }

                // Update the list
                hotUpdateAssembliesProp.ClearArray();
                hotUpdateAssembliesProp.arraySize = targetAssemblyNames.Count;

                for (int i = 0; i < targetAssemblyNames.Count; i++)
                {
                    SerializedProperty element = hotUpdateAssembliesProp.GetArrayElementAtIndex(i);
                    element.stringValue = targetAssemblyNames[i];
                }

                serializedSettings.ApplyModifiedProperties();
                EditorUtility.SetDirty(settingsAsset);
                AssetDatabase.SaveAssets();

                Debug.Log($"{DEBUG_FLAG} HybridCLRSettings.hotUpdateAssemblies synchronized successfully. Updated list with {targetAssemblyNames.Count} assemblies: {string.Join(", ", targetAssemblyNames)}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"{DEBUG_FLAG} Failed to synchronize HybridCLRSettings: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Checks if Obfuz is enabled.
        /// Priority: BuildData.useObfuz > HybridCLRBuildConfig.enableObfuz
        /// </summary>
        private static bool IsObfuzEnabled(HybridCLRBuildConfig config)
        {
            // First check BuildData
            BuildData buildData = BuildConfigHelper.GetBuildData();
            if (buildData != null && buildData.UseObfuz)
            {
                return true;
            }

            // Fallback to HybridCLRBuildConfig
            if (config != null)
            {
                return config.enableObfuz;
            }

            return false;
        }

        private static string GetHybridCLROutputDir(BuildTarget target)
        {
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

        private static string GetAOTDllSourceDir(BuildTarget target)
        {
            Type settingsUtilType = ReflectionCache.GetType("HybridCLR.Editor.Settings.SettingsUtil");
            if (settingsUtilType != null)
            {
                MethodInfo getDirMethod = ReflectionCache.GetMethod(settingsUtilType, "GetAssembliesPostIl2CppStripDir", BindingFlags.Public | BindingFlags.Static);
                if (getDirMethod != null)
                {
                    try
                    {
                        string dir = (string)getDirMethod.Invoke(null, new object[] { target });
                        if (!string.IsNullOrEmpty(dir))
                        {
                            return dir;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"{DEBUG_FLAG} Failed to invoke GetAssembliesPostIl2CppStripDir: {ex.Message}");
                    }
                }
            }

            // Fallback: Use the standard HybridCLR directory structure
            // HybridCLR uses "AssembliesPostIl2CppStrip" not "StrippedAOTDlls"
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(projectRoot, "HybridCLRData", "AssembliesPostIl2CppStrip", target.ToString());
        }

        /// <summary>
        /// Generates assembly list JSON file using Unity's JsonUtility.
        /// The file is saved as .bytes for compatibility with runtime loading.
        /// </summary>
        public static void GenerateAssemblyList(BuildTarget target, string outputPath, List<string> assemblyPaths)
        {
            if (assemblyPaths == null || assemblyPaths.Count == 0)
            {
                Debug.LogWarning($"{DEBUG_FLAG} No assemblies to list. Skipping list generation.");
                return;
            }

            try
            {
                HotUpdateAssemblyList list = new HotUpdateAssemblyList
                {
                    assemblies = assemblyPaths
                };

                string json = JsonUtility.ToJson(list, true);

                string directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);
                File.WriteAllBytes(outputPath, jsonBytes);
                Debug.Log($"{DEBUG_FLAG} Assembly list generated: {outputPath} ({assemblyPaths.Count} assemblies)");
            }
            catch (Exception ex)
            {
                Debug.LogError($"{DEBUG_FLAG} Failed to generate assembly list: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
        }

        /// <summary>
        /// Generates AOT assembly list JSON file using Unity's JsonUtility.
        /// The file is saved as .bytes for compatibility with runtime loading.
        /// </summary>
        public static void GenerateAOTAssemblyList(BuildTarget target, string outputPath, List<string> aotAssemblyPaths)
        {
            if (aotAssemblyPaths == null || aotAssemblyPaths.Count == 0)
            {
                Debug.LogWarning($"{DEBUG_FLAG} No AOT assemblies to list. Skipping AOT list generation.");
                return;
            }

            try
            {
                AOTAssemblyList list = new AOTAssemblyList
                {
                    assemblies = aotAssemblyPaths
                };

                string json = JsonUtility.ToJson(list, true);

                string directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);
                File.WriteAllBytes(outputPath, jsonBytes);
                Debug.Log($"{DEBUG_FLAG} AOT assembly list generated: {outputPath} ({aotAssemblyPaths.Count} assemblies)");
            }
            catch (Exception ex)
            {
                Debug.LogError($"{DEBUG_FLAG} Failed to generate AOT assembly list: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
        }

        /// <summary>
        /// Copies AOT assemblies to the specified directory and generates the AOT list.
        /// </summary>
        public static void CopyAOTAssembliesAndGenerateList(BuildTarget target, string sourceDir, string destinationDir, string aotListFileName = "AOT.bytes")
        {
            if (!Directory.Exists(sourceDir))
            {
                Debug.LogWarning($"{DEBUG_FLAG} AOT source directory does not exist: {sourceDir}. Skipping AOT copy.");
                return;
            }

            try
            {
                if (!Directory.Exists(destinationDir))
                {
                    Directory.CreateDirectory(destinationDir);
                }

                CleanOldAOTDlls(destinationDir, aotListFileName);

                List<string> aotAssemblyPaths = new List<string>();
                string[] dllFiles = Directory.GetFiles(sourceDir, "*.dll", SearchOption.TopDirectoryOnly);

                foreach (string dllFile in dllFiles)
                {
                    string fileName = Path.GetFileName(dllFile);
                    string dstFile = Path.Combine(destinationDir, $"{fileName}.bytes");

                    File.Copy(dllFile, dstFile, true);

                    string relativePath = GetRelativePathFromAssets(dstFile);
                    if (!string.IsNullOrEmpty(relativePath))
                    {
                        aotAssemblyPaths.Add(relativePath);
                    }

                    Debug.Log($"{DEBUG_FLAG} Copied AOT DLL: {fileName} -> {GetRelativePathFromAssets(dstFile)}");
                }

                // Generate AOT list JSON
                if (aotAssemblyPaths.Count > 0)
                {
                    string aotListPath = Path.Combine(destinationDir, aotListFileName);
                    GenerateAOTAssemblyList(target, aotListPath, aotAssemblyPaths);

                    AssetDatabase.Refresh();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"{DEBUG_FLAG} Failed to copy AOT assemblies: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
        }

        /// <summary>
        /// Cleans up old AOT DLL files and AOT list from the destination directory.
        /// This ensures only current AOT assemblies are present after the build.
        /// </summary>
        private static void CleanOldAOTDlls(string destinationDir, string aotListFileName)
        {
            if (!Directory.Exists(destinationDir))
            {
                return;
            }

            string[] oldDllFiles = Directory.GetFiles(destinationDir, "*.dll.bytes", SearchOption.TopDirectoryOnly);
            foreach (string dllFile in oldDllFiles)
            {
                string assetPath = GetRelativePathFromAssets(dllFile);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    AssetDatabase.DeleteAsset(assetPath);
                    Debug.Log($"{DEBUG_FLAG} Removed old AOT DLL: {assetPath}");
                }
            }

            string aotListPath = Path.Combine(destinationDir, aotListFileName);
            if (File.Exists(aotListPath))
            {
                string assetPath = GetRelativePathFromAssets(aotListPath);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    AssetDatabase.DeleteAsset(assetPath);
                    Debug.Log($"{DEBUG_FLAG} Removed old AOT list: {assetPath}");
                }
            }
        }

        /// <summary>
        /// Cleans up old HotUpdate DLL files and list from the destination directory.
        /// This ensures only current assemblies are present after the build.
        /// </summary>
        private static void CleanOldHotUpdateDlls(string destinationDir)
        {
            if (!Directory.Exists(destinationDir))
            {
                return;
            }

            string[] oldDllFiles = Directory.GetFiles(destinationDir, "*.dll.bytes", SearchOption.TopDirectoryOnly);
            foreach (string dllFile in oldDllFiles)
            {
                string assetPath = GetRelativePathFromAssets(dllFile);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    AssetDatabase.DeleteAsset(assetPath);
                    Debug.Log($"{DEBUG_FLAG} Removed old HotUpdate DLL: {assetPath}");
                }
            }

            string listPath = Path.Combine(destinationDir, "HotUpdate.bytes");
            if (File.Exists(listPath))
            {
                string assetPath = GetRelativePathFromAssets(listPath);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    AssetDatabase.DeleteAsset(assetPath);
                    Debug.Log($"{DEBUG_FLAG} Removed old HotUpdate list: {assetPath}");
                }
            }
        }

        /// <summary>
        /// Cleans up old Cheat DLL files and list from the destination directory.
        /// </summary>
        private static void CleanOldCheatDlls(string destinationDir)
        {
            if (!Directory.Exists(destinationDir))
            {
                return;
            }

            string[] oldDllFiles = Directory.GetFiles(destinationDir, "*.dll.bytes", SearchOption.TopDirectoryOnly);
            foreach (string dllFile in oldDllFiles)
            {
                string assetPath = GetRelativePathFromAssets(dllFile);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    AssetDatabase.DeleteAsset(assetPath);
                    Debug.Log($"{DEBUG_FLAG} Removed old Cheat DLL: {assetPath}");
                }
            }

            string listPath = Path.Combine(destinationDir, "Cheat.bytes");
            if (File.Exists(listPath))
            {
                string assetPath = GetRelativePathFromAssets(listPath);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    AssetDatabase.DeleteAsset(assetPath);
                    Debug.Log($"{DEBUG_FLAG} Removed old Cheat list: {assetPath}");
                }
            }
        }

        /// <summary>
        /// Gets relative path from Assets root if the path is within Assets folder.
        /// </summary>
        private static string GetRelativePathFromAssets(string fullPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string normalizedFullPath = Path.GetFullPath(fullPath).Replace('\\', '/');
            string normalizedProjectRoot = projectRoot.Replace('\\', '/');

            if (normalizedFullPath.StartsWith(normalizedProjectRoot))
            {
                string relativePath = normalizedFullPath.Substring(normalizedProjectRoot.Length);
                if (relativePath.StartsWith("/"))
                {
                    relativePath = relativePath.Substring(1);
                }
                return relativePath;
            }

            return null;
        }
    }
}