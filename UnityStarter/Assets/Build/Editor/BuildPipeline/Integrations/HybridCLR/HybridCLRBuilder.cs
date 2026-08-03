using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using UnityEditor;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    internal static class HybridCLRBuilder
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
        /// Serializable wrapper for the HybridCLR hot-update assembly list.
        /// </summary>
        [Serializable]
        private class HotUpdateAssemblyList
        {
            public List<string> assemblies;
        }

        private const string DEBUG_FLAG = "<color=cyan>[HybridCLR]</color>";
        internal const string HotUpdateOutputRole = "HotUpdate";
        internal const string AOTOutputRole = "AOT";
        private static void Build(BuildTarget target)
        {
            Debug.Log($"{DEBUG_FLAG} Checking availability for platform: {target}...");

            // Use Reflection to avoid compilation errors if HybridCLR is not installed
            Type prebuildCommandType = ReflectionCache.GetType("HybridCLR.Editor.Commands.PrebuildCommand");
            Type installerControllerType = ReflectionCache.GetType("HybridCLR.Editor.Installer.InstallerController");

            if (prebuildCommandType == null)
            {
                throw new InvalidOperationException("HybridCLR package is not installed. Provision HybridCLR before running the build pipeline.");
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
                        throw new InvalidOperationException(
                            "HybridCLR is installed but not initialized. Run the HybridCLR installer as a separate provisioning step before building.");
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("Failed to validate the HybridCLR installation.", ex);
                }
            }

            BuildTarget currentTarget = EditorUserBuildSettings.activeBuildTarget;
            if (currentTarget != target)
            {
                throw new InvalidOperationException(
                    $"HybridCLR GenerateAll uses the active build target. Active target '{currentTarget}' does not match requested target '{target}'.");
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
                    throw new MissingMethodException(prebuildCommandType.FullName, "GenerateAll");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"{DEBUG_FLAG} Generation failed for platform {target}: {e.Message}");
                throw;
            }
        }

        private static void CompileDllOnly(BuildTarget target)
        {
            Debug.Log($"{DEBUG_FLAG} Start compiling DLLs for platform: {target}...");
            Type compileDllCommandType = ReflectionCache.GetType("HybridCLR.Editor.Commands.CompileDllCommand");
            if (compileDllCommandType == null)
            {
                throw new InvalidOperationException("HybridCLR package is not installed. Provision HybridCLR before running the build pipeline.");
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
                    throw new MissingMethodException(compileDllCommandType.FullName, "CompileDll");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"{DEBUG_FLAG} Compile DLL failed for platform {target}: {e.Message}");
                throw;
            }
        }

        internal static void GenerateAllAndCopy(BuildTarget target, HybridCLRBuildConfig config)
        {
            bool obfuscateHotUpdateAssemblies = ValidateRequest(config);
            Build(target);
            if (obfuscateHotUpdateAssemblies)
            {
                RunObfuzPostProcessing(target);
            }

            CopyHotUpdateDlls(target, config);
        }

        internal static void CompileDllAndCopy(BuildTarget target, HybridCLRBuildConfig config)
        {
            bool obfuscateHotUpdateAssemblies = ValidateRequest(config);
            CompileDllOnly(target);
            if (obfuscateHotUpdateAssemblies)
            {
                RunObfuzPostProcessing(target);
            }

            CopyHotUpdateDlls(target, config);
        }

        private static bool ValidateRequest(HybridCLRBuildConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            ValidateHybridCLRSettings(config);

            if (!config.ObfuscateHotUpdateAssemblies)
            {
                return false;
            }

            if (!ObfuzIntegrator.IsBaseObfuzAvailable() || !ObfuzIntegrator.IsHybridCLRObfuzAvailable())
            {
                throw new InvalidOperationException(
                    "HybridCLR Obfuz requires compatible Obfuz and Obfuz4HybridCLR packages.");
            }

            if (!ObfuzIntegrator.VerifyEncryptionVMCompiled())
            {
                throw new InvalidOperationException(
                    "Obfuz prerequisites are not compiled. Generate them in a separate provisioning step before building HybridCLR content.");
            }

            return true;
        }

        private static void RunObfuzPostProcessing(BuildTarget target)
        {
            string outputDirectory = ObfuzIntegrator.GetObfuscatedHotUpdateAssemblyOutputPath(target);
            ObfuzIntegrator.ObfuscateHotUpdateAssemblies(target, outputDirectory);
            ObfuzIntegrator.GenerateMethodBridgeAndReversePInvokeWrapper(target, outputDirectory);
            ObfuzIntegrator.GenerateAOTGenericReference(target, outputDirectory);
        }

        private static void CopyHotUpdateDlls(BuildTarget target, HybridCLRBuildConfig config)
        {
            string sourceDirectory = GetHybridCLROutputDir(target);
            if (config.ObfuscateHotUpdateAssemblies)
            {
                sourceDirectory = ObfuzIntegrator.GetObfuscatedHotUpdateAssemblyOutputPath(target);
                if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
                {
                    throw new DirectoryNotFoundException(
                        $"HybridCLR hot-update obfuscation is enabled, but its output directory was not found: '{sourceDirectory}'.");
                }

                Debug.Log($"{DEBUG_FLAG} Using obfuscated assemblies from: {sourceDirectory}");
            }

            if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
            {
                throw new DirectoryNotFoundException(
                    $"HybridCLR output directory was not found after generation: '{sourceDirectory}'.");
            }

            string aotSourceDirectory = GetAOTDllSourceDir(target);
            if (!Directory.Exists(aotSourceDirectory))
            {
                throw new DirectoryNotFoundException(
                    $"HybridCLR stripped-AOT directory was not found after generation: '{aotSourceDirectory}'. " +
                    "Run a full HybridCLR build before using fast mode.");
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            IReadOnlyList<HybridCLROutputTarget> outputTargets = CreateManagedOutputTargets(config, projectRoot);
            HybridCLROutputTransaction transaction = HybridCLROutputTransaction.Begin(projectRoot, outputTargets);
            try
            {
                using (transaction)
                {
                    StageConfiguredAssemblies(
                        projectRoot,
                        sourceDirectory,
                        config.GetHotUpdateAssemblyNames(),
                        HotUpdateOutputRole,
                        "HotUpdate.bytes",
                        transaction);

                    StageAOTAssemblies(projectRoot, aotSourceDirectory, transaction);
                    transaction.Commit();
                }
            }
            catch (Exception publicationException)
            {
                if (transaction.OutputsCommitted)
                {
                    try
                    {
                        AssetDatabase.Refresh();
                    }
                    catch (Exception refreshException)
                    {
                        throw new AggregateException(
                            "HybridCLR outputs committed, but post-commit cleanup and AssetDatabase refresh both failed.",
                            publicationException,
                            refreshException);
                    }
                }

                ExceptionDispatchInfo.Capture(publicationException).Throw();
            }

            // Import only the fully committed output set. Staging and rollback never expose
            // partial generated files to the AssetDatabase.
            AssetDatabase.Refresh();
        }

        internal static void ValidateManagedOutputOwnership(HybridCLRBuildConfig config, string projectRoot)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            RecoverPendingManagedOutputs(projectRoot);

            IReadOnlyList<HybridCLROutputTarget> targets = CreateManagedOutputTargets(config, projectRoot);
            HybridCLROutputTransaction.ValidateExistingOutputs(targets);
        }

        internal static void RecoverPendingManagedOutputs(string projectRoot)
        {
            // Recovery is intentionally independent of feature applicability and the current
            // configuration. The central journal owns every path from the interrupted run.
            bool recovered = HybridCLROutputTransaction.RecoverPending(projectRoot);
            if (recovered)
            {
                // Recovery is itself atomic. Refresh only after it has restored the complete
                // pre-transaction output set or finished committed-state cleanup.
                AssetDatabase.Refresh();
            }
        }

        private static IReadOnlyList<HybridCLROutputTarget> CreateManagedOutputTargets(
            HybridCLRBuildConfig config,
            string projectRoot)
        {
            var targets = new List<HybridCLROutputTarget>(2)
            {
                new HybridCLROutputTarget(
                    HotUpdateOutputRole,
                    BuildPathPolicy.ResolveGeneratedAssetsDirectory(
                        projectRoot,
                        config.GetHotUpdateDllOutputDirectoryPath()))
            };

            string aotPath = config.GetAOTDllOutputDirectoryPath();
            if (string.IsNullOrWhiteSpace(aotPath))
            {
                throw new InvalidOperationException("HybridCLR AOT DLL output directory is required.");
            }

            targets.Add(new HybridCLROutputTarget(
                AOTOutputRole,
                BuildPathPolicy.ResolveGeneratedAssetsDirectory(projectRoot, aotPath)));

            // Validation also rejects parent/child overlap and portable casing aliases.
            HybridCLROutputTransaction.ValidateTargets(targets);
            return targets;
        }

        private static void StageConfiguredAssemblies(
            string projectRoot,
            string sourceDirectory,
            IReadOnlyList<string> assemblyNames,
            string outputRole,
            string listFileName,
            HybridCLROutputTransaction transaction)
        {
            if (outputRole == HotUpdateOutputRole && assemblyNames.Count == 0)
            {
                throw new InvalidOperationException(
                    "HybridCLR is enabled but no hot update assemblies are configured.");
            }

            var artifacts = new List<string>(assemblyNames.Count + 1);
            var assetPaths = new List<string>(assemblyNames.Count);
            foreach (string assemblyName in assemblyNames)
            {
                string artifactName = assemblyName + ".dll.bytes";
                string sourcePath = BuildPathPolicy.EnsureSafeReadableFile(
                    sourceDirectory,
                    Path.Combine(sourceDirectory, assemblyName + ".dll"));
                string stagingPath = transaction.GetStagingFilePath(outputRole, artifactName);
                CopyGeneratedFile(sourcePath, stagingPath);
                artifacts.Add(artifactName);
                assetPaths.Add(GetProjectAssetPath(
                    projectRoot,
                    transaction.GetFinalFilePath(outputRole, artifactName)));
            }

            if (assetPaths.Count > 0)
            {
                string listPath = transaction.GetStagingFilePath(outputRole, listFileName);
                GenerateAssemblyList(listPath, assetPaths);
                artifacts.Add(listFileName);
            }

            transaction.CompleteStaging(outputRole, artifacts);
            Debug.Log(
                $"{DEBUG_FLAG} Staged {assemblyNames.Count} {outputRole} assemblies for transactional publication.");
        }

        private static void StageAOTAssemblies(
            string projectRoot,
            string sourceDirectory,
            HybridCLROutputTransaction transaction)
        {
            string[] sourceFiles = Directory.GetFiles(sourceDirectory, "*.dll", SearchOption.TopDirectoryOnly);
            Array.Sort(sourceFiles, StringComparer.Ordinal);
            if (sourceFiles.Length == 0)
            {
                throw new FileNotFoundException(
                    $"HybridCLR stripped-AOT directory contains no DLL assemblies: '{sourceDirectory}'.");
            }

            var artifacts = new List<string>(sourceFiles.Length + 1);
            var assetPaths = new List<string>(sourceFiles.Length);
            foreach (string sourceFile in sourceFiles)
            {
                string readableSource = BuildPathPolicy.EnsureSafeReadableFile(sourceDirectory, sourceFile);
                string artifactName = Path.GetFileName(sourceFile) + ".bytes";
                string stagingPath = transaction.GetStagingFilePath(AOTOutputRole, artifactName);
                CopyGeneratedFile(readableSource, stagingPath);
                artifacts.Add(artifactName);
                assetPaths.Add(GetProjectAssetPath(
                    projectRoot,
                    transaction.GetFinalFilePath(AOTOutputRole, artifactName)));
            }

            const string listFileName = "AOT.bytes";
            GenerateAOTAssemblyList(
                transaction.GetStagingFilePath(AOTOutputRole, listFileName),
                assetPaths);
            artifacts.Add(listFileName);
            transaction.CompleteStaging(AOTOutputRole, artifacts);
            Debug.Log(
                $"{DEBUG_FLAG} Staged {sourceFiles.Length} AOT assemblies for transactional publication.");
        }

        private static void ValidateHybridCLRSettings(HybridCLRBuildConfig config)
        {
            const string settingsUtilTypeName = "HybridCLR.Editor.SettingsUtil";
            Type settingsUtilType = ReflectionCache.GetType(settingsUtilTypeName);
            if (settingsUtilType == null)
            {
                throw new InvalidOperationException(
                    $"HybridCLR SettingsUtil API is unavailable: '{settingsUtilTypeName}'. Install a compatible HybridCLR package.");
            }

            PropertyInfo namesProperty = ReflectionCache.GetProperty(
                settingsUtilType,
                "HotUpdateAssemblyNamesExcludePreserved",
                BindingFlags.Public | BindingFlags.Static);
            if (namesProperty == null)
            {
                throw new MissingMemberException(settingsUtilType.FullName, "HotUpdateAssemblyNamesExcludePreserved");
            }

            object value = namesProperty.GetValue(null);
            if (!(value is IEnumerable<string> hybridAssemblyNames))
            {
                throw new InvalidOperationException(
                    "HybridCLR SettingsUtil returned an incompatible hot-update assembly collection.");
            }

            var configuredNames = config.GetHotUpdateAssemblyNames();
            var uniqueConfiguredNames = new HashSet<string>(StringComparer.Ordinal);
            var duplicateNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (string assemblyName in configuredNames)
            {
                if (!uniqueConfiguredNames.Add(assemblyName))
                {
                    duplicateNames.Add(assemblyName);
                }
            }

            if (duplicateNames.Count > 0)
            {
                throw new InvalidOperationException(
                    $"HybridCLRBuildConfig contains duplicate assembly entries: {string.Join(", ", duplicateNames)}.");
            }

            var hybridNames = new HashSet<string>(hybridAssemblyNames, StringComparer.Ordinal);
            var missingNames = uniqueConfiguredNames.Where(name => !hybridNames.Contains(name)).ToArray();
            if (missingNames.Length > 0)
            {
                throw new InvalidOperationException(
                    "HybridCLRBuildConfig contains assemblies that are absent from HybridCLR Settings > " +
                    $"Hot Update Assembly Definitions: {string.Join(", ", missingNames)}.");
            }
        }

        private static string GetHybridCLROutputDir(BuildTarget target)
        {
            return GetRequiredHybridCLRDirectory(target, "GetHotUpdateDllsOutputDirByTarget");
        }

        private static string GetAOTDllSourceDir(BuildTarget target)
        {
            return GetRequiredHybridCLRDirectory(target, "GetAssembliesPostIl2CppStripDir");
        }

        private static string GetRequiredHybridCLRDirectory(BuildTarget target, string methodName)
        {
            const string settingsUtilTypeName = "HybridCLR.Editor.SettingsUtil";
            Type settingsUtilType = ReflectionCache.GetType(settingsUtilTypeName);
            if (settingsUtilType == null)
            {
                throw new InvalidOperationException(
                    $"HybridCLR SettingsUtil API is unavailable: '{settingsUtilTypeName}'. Install a compatible HybridCLR package.");
            }

            MethodInfo getDirectoryMethod = ReflectionCache.GetMethod(
                settingsUtilType,
                methodName,
                BindingFlags.Public | BindingFlags.Static,
                new[] { typeof(BuildTarget) });
            if (getDirectoryMethod == null)
            {
                throw new MissingMethodException(settingsUtilType.FullName, methodName);
            }

            string directory = getDirectoryMethod.Invoke(null, new object[] { target }) as string;
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException(
                    $"HybridCLR '{methodName}' returned an empty directory for build target '{target}'.");
            }

            return directory;
        }

        private static void GenerateAssemblyList(string outputPath, List<string> assemblyPaths)
        {
            if (assemblyPaths == null || assemblyPaths.Count == 0)
            {
                throw new ArgumentException("At least one assembly is required for an assembly list.", nameof(assemblyPaths));
            }

            var list = new HotUpdateAssemblyList { assemblies = assemblyPaths };
            WriteAssemblyList(outputPath, JsonUtility.ToJson(list, true), assemblyPaths.Count);
        }

        private static void GenerateAOTAssemblyList(string outputPath, List<string> assemblyPaths)
        {
            if (assemblyPaths == null || assemblyPaths.Count == 0)
            {
                throw new ArgumentException("At least one AOT assembly is required for an assembly list.", nameof(assemblyPaths));
            }

            var list = new AOTAssemblyList { assemblies = assemblyPaths };
            WriteAssemblyList(outputPath, JsonUtility.ToJson(list, true), assemblyPaths.Count);
        }

        private static void WriteAssemblyList(string outputPath, string json, int assemblyCount)
        {
            HybridCLROutputOwnership.WriteFileDurably(
                outputPath,
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(json));
            Debug.Log($"{DEBUG_FLAG} Assembly list staged: {outputPath} ({assemblyCount} assemblies)");
        }

        private static string GetProjectAssetPath(string projectRoot, string fullPath)
        {
            string assetsRoot = Path.GetFullPath(Path.Combine(projectRoot, "Assets"));
            string normalizedFullPath = Path.GetFullPath(fullPath);
            if (!BuildPathPolicy.IsStrictDescendant(assetsRoot, normalizedFullPath))
            {
                throw new InvalidOperationException(
                    $"HybridCLR generated artifact must remain inside Assets: '{normalizedFullPath}'.");
            }

            string normalizedAssetsRoot = assetsRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            string relativeToAssets = normalizedFullPath.Substring(normalizedAssetsRoot.Length + 1);
            return ("Assets/" + relativeToAssets).Replace('\\', '/');
        }

        private static void CopyGeneratedFile(string sourcePath, string destinationPath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                throw new ArgumentException("Generated source file is required.", nameof(sourcePath));
            }

            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException(
                    $"HybridCLR generated source file was not found: '{sourcePath}'.",
                    sourcePath);
            }

            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                throw new ArgumentException("Generated destination file is required.", nameof(destinationPath));
            }

            try
            {
                HybridCLRFileIdentity identity =
                    HybridCLROutputOwnership.CaptureRequiredFileIdentity(
                        sourcePath,
                        "generated source artifact");
                HybridCLROutputOwnership.CopyFileAndVerify(
                    sourcePath,
                    destinationPath,
                    identity,
                    "generated artifact staging");
            }
            catch (Exception exception) when (IsExpectedFileSystemException(exception))
            {
                throw new IOException(
                    $"Failed to copy HybridCLR generated file from '{sourcePath}' to '{destinationPath}'.",
                    exception);
            }
        }

        private static bool IsExpectedFileSystemException(Exception exception)
        {
            return exception is IOException
                || exception is UnauthorizedAccessException
                || exception is ArgumentException
                || exception is NotSupportedException
                || exception is System.Security.SecurityException;
        }

    }
}
