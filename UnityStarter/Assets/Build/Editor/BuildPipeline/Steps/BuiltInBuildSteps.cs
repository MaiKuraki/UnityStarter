using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    [BuildStepRegistration(
        BuildStepIds.HotUpdate,
        DisplayName = "Hot Update",
        Description = "Generate and publish HybridCLR hot-update and AOT metadata assemblies when enabled.",
        Category = "Compilation")]
    public sealed class HotUpdateBuildStep : IBuildStep
    {
        public string Id => BuildStepIds.HotUpdate;
        public int Priority => 0;

        public bool IsApplicable(BuildExecutionContext context)
        {
            return context.Request.UseHybridClr;
        }

        public IReadOnlyList<string> GetRequiredStepIds(BuildExecutionContext context)
        {
            return Array.Empty<string>();
        }

        public IReadOnlyList<string> Validate(BuildExecutionContext context)
        {
            var errors = new List<string>();
            HybridCLRBuildConfig config = context.Request.HybridClrConfiguration;
            if (config == null)
            {
                errors.Add("HybridCLR is enabled, but BuildData does not reference a HybridCLRBuildConfig asset.");
                return errors;
            }

            string commandType = context.Request.Incrementality == BuildIncrementality.Incremental
                ? "HybridCLR.Editor.Commands.CompileDllCommand"
                : "HybridCLR.Editor.Commands.PrebuildCommand";
            if (ReflectionCache.GetType(commandType) == null)
            {
                errors.Add("HybridCLR package is not installed or its supported editor API is unavailable.");
            }

            if (config.GetHotUpdateAssemblyNames().Count == 0)
            {
                errors.Add("HybridCLRBuildConfig must contain at least one hot update assembly.");
            }

            string hotUpdateOutput = null;
            if (string.IsNullOrWhiteSpace(config.GetHotUpdateDllOutputDirectoryPath()))
            {
                errors.Add("HybridCLRBuildConfig must define a Hot Update DLL output directory.");
            }
            else
            {
                hotUpdateOutput = ValidateGeneratedOutput(
                    context.Request.ProjectRoot,
                    config.GetHotUpdateDllOutputDirectoryPath(),
                    "Hot update DLL",
                    errors);
            }

            if (string.IsNullOrWhiteSpace(config.GetAOTDllOutputDirectoryPath()))
            {
                errors.Add("HybridCLRBuildConfig must define an AOT DLL output directory.");
            }

            string aotOutput = ValidateGeneratedOutput(
                context.Request.ProjectRoot,
                config.GetAOTDllOutputDirectoryPath(),
                "AOT DLL",
                errors);

            EnsureDistinctGeneratedOutputs(hotUpdateOutput, aotOutput, errors);
            if (hotUpdateOutput != null && aotOutput != null)
            {
                try
                {
                    HybridCLRBuilder.ValidateManagedOutputOwnership(config, context.Request.ProjectRoot);
                }
                catch (Exception exception)
                {
                    errors.Add($"HybridCLR generated-output ownership validation failed: {exception.Message}");
                }
            }

            if (config.ObfuscateHotUpdateAssemblies)
            {
                if (!ObfuzIntegrator.IsBaseObfuzAvailable() || !ObfuzIntegrator.IsHybridCLRObfuzAvailable())
                {
                    errors.Add("HybridCLR hot-update obfuscation is enabled, but the atomic HybridCLR + Obfuz + Obfuz4HybridCLR package set is unavailable.");
                }
                else if (!ObfuzIntegrator.VerifyEncryptionVMCompiled())
                {
                    errors.Add("Obfuz Encryption VM is not compiled. Run provisioning before the build.");
                }
            }

            return errors;
        }

        public void Execute(BuildExecutionContext context)
        {
            if (context.Request.Incrementality == BuildIncrementality.Incremental)
            {
                Debug.LogWarning(
                    "[BuildPipeline] Fast HybridCLR mode reuses existing stripped-AOT input. " +
                    "Use a full build after assembly, signature, generic, or AOT dependency changes.");
                HybridCLRBuilder.CompileDllAndCopy(
                    context.Request.Target,
                    context.Request.HybridClrConfiguration);
            }
            else
            {
                HybridCLRBuilder.GenerateAllAndCopy(
                    context.Request.Target,
                    context.Request.HybridClrConfiguration);
            }

            VerifyOutputs(context.Request);
        }

        public void Cleanup(BuildExecutionContext context)
        {
        }

        private static void VerifyOutputs(BuildRequest request)
        {
            HybridCLRBuildConfig config = request.HybridClrConfiguration;
            string hotUpdateDirectory = ResolveProjectAssetPath(request.ProjectRoot, config.GetHotUpdateDllOutputDirectoryPath());
            var missing = new List<string>();
            foreach (string assemblyName in config.GetHotUpdateAssemblyNames())
            {
                string path = Path.Combine(hotUpdateDirectory, assemblyName + ".dll.bytes");
                if (!File.Exists(path))
                {
                    missing.Add(path);
                }
            }

            string listPath = Path.Combine(hotUpdateDirectory, "HotUpdate.bytes");
            if (!File.Exists(listPath))
            {
                missing.Add(listPath);
            }

            string aotDirectory = ResolveProjectAssetPath(request.ProjectRoot, config.GetAOTDllOutputDirectoryPath());
            string aotListPath = Path.Combine(aotDirectory, "AOT.bytes");
            if (!File.Exists(aotListPath))
            {
                missing.Add(aotListPath);
            }

            if (missing.Count > 0)
            {
                throw new BuildFailedException("HybridCLR output verification failed:\n" + string.Join("\n", missing));
            }

            HybridCLRBuilder.ValidateManagedOutputOwnership(config, request.ProjectRoot);
        }

        private static string ResolveProjectAssetPath(string projectRoot, string assetPath)
        {
            try
            {
                return BuildPathPolicy.ResolveGeneratedAssetsDirectory(projectRoot, assetPath);
            }
            catch (Exception exception)
            {
                throw new BuildFailedException(
                    $"HybridCLR output must be a safe project-relative Assets directory: '{assetPath}'. {exception.Message}");
            }
        }

        private static string ValidateGeneratedOutput(
            string projectRoot,
            string assetPath,
            string label,
            ICollection<string> errors)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return null;
            }

            try
            {
                return BuildPathPolicy.ResolveGeneratedAssetsDirectory(projectRoot, assetPath);
            }
            catch (Exception exception)
            {
                errors.Add($"{label} output is unsafe: {exception.Message}");
                return null;
            }
        }

        private static void EnsureDistinctGeneratedOutputs(
            string hotUpdateOutput,
            string aotOutput,
            ICollection<string> errors)
        {
            var outputs = new[]
            {
                (Label: "Hot update DLL", Path: hotUpdateOutput),
                (Label: "AOT DLL", Path: aotOutput)
            };

            for (int left = 0; left < outputs.Length; left++)
            {
                if (string.IsNullOrEmpty(outputs[left].Path))
                {
                    continue;
                }

                for (int right = left + 1; right < outputs.Length; right++)
                {
                    if (!string.IsNullOrEmpty(outputs[right].Path)
                        && string.Equals(outputs[left].Path, outputs[right].Path, StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add(
                            $"{outputs[left].Label} and {outputs[right].Label} outputs must use different directories: '{outputs[left].Path}'.");
                    }
                }
            }

        }
    }

    [BuildStepRegistration(
        BuildStepIds.AssetContent,
        DisplayName = "Asset Content",
        Description = "Build the selected Addressables, YooAsset, or future content provider.",
        Category = "Content")]
    public sealed class AssetContentBuildStep : IBuildStep
    {
        public string Id => BuildStepIds.AssetContent;
        public int Priority => 0;

        public bool IsApplicable(BuildExecutionContext context)
        {
            return !string.IsNullOrWhiteSpace(context.Request.AssetContentProviderId);
        }

        public IReadOnlyList<string> GetRequiredStepIds(BuildExecutionContext context)
        {
            return context.Request.UseHybridClr
                ? new[] { BuildStepIds.HotUpdate }
                : Array.Empty<string>();
        }

        public IReadOnlyList<string> Validate(BuildExecutionContext context)
        {
            var errors = new List<string>();
            ScriptableObject configuration = context.Request.AssetContentConfiguration;
            if (configuration == null)
            {
                errors.Add(
                    $"BuildData must reference an explicit configuration for provider '{context.Request.AssetContentProviderId}'.");
                return errors;
            }

            IAssetContentBuildAdapter adapter;
            try
            {
                adapter = context.ResolveAssetContentAdapter();
            }
            catch (Exception exception)
            {
                errors.Add(exception.Message);
                return errors;
            }

            if (adapter == null)
            {
                errors.Add(
                    $"No compatible '{context.Request.AssetContentProviderId}' content adapter is available. " +
                    "Install a supported version-gated integration or select another provider.");
                return errors;
            }

            if (context.Version == null)
            {
                errors.Add("Version context is unavailable.");
                return errors;
            }

            AssetContentBuildRequest adapterRequest = CreateAdapterRequest(context, configuration);
            AssetContentBuildResult validation = adapter.Validate(adapterRequest);
            if (validation == null || !validation.Succeeded)
            {
                errors.Add(validation?.ErrorInfo ?? "The content adapter returned no validation result.");
            }

            return errors;
        }

        public void Execute(BuildExecutionContext context)
        {
            ScriptableObject configuration = context.Request.AssetContentConfiguration;
            IAssetContentBuildAdapter adapter = context.ResolveAssetContentAdapter();
            if (adapter == null)
            {
                throw new BuildFailedException(
                    $"No compatible '{context.Request.AssetContentProviderId}' content adapter is available.");
            }

            IReadOnlyList<AssetContentBuildResult> results = adapter.Build(CreateAdapterRequest(context, configuration));
            if (results == null || results.Count == 0)
            {
                throw new BuildFailedException($"{adapter.ProviderId} did not return any package build results.");
            }

            foreach (AssetContentBuildResult result in results)
            {
                context.AddContentResult(result);
                if (result == null || !result.Succeeded)
                {
                    string message = result == null
                        ? $"{adapter.ProviderId} returned a null package result."
                        : $"{adapter.ProviderId} failed in '{result.FailedTask}': {result.ErrorInfo}\n{result.ErrorStack}";
                    throw new BuildFailedException(message);
                }
            }
        }

        public void Cleanup(BuildExecutionContext context)
        {
        }

        private static AssetContentBuildRequest CreateAdapterRequest(BuildExecutionContext context, ScriptableObject configuration)
        {
            return new AssetContentBuildRequest(
                context.Request.Target,
                context.Version.PackageVersion,
                context.Request.ProjectRoot,
                configuration,
                context.Request.Incrementality,
                context.Request.BatchMode);
        }
    }

    [BuildStepRegistration(
        BuildStepIds.Player,
        DisplayName = "Player",
        Description = "Build and transactionally publish the Unity Player.",
        Category = "Player")]
    public sealed class PlayerBuildStep : IBuildStep
    {
        public string Id => BuildStepIds.Player;
        public int Priority => 0;

        public bool IsApplicable(BuildExecutionContext context)
        {
            return true;
        }

        public IReadOnlyList<string> GetRequiredStepIds(BuildExecutionContext context)
        {
            var required = new List<string>(2);
            if (context.Request.UseHybridClr)
            {
                required.Add(BuildStepIds.HotUpdate);
            }

            if (!string.IsNullOrWhiteSpace(context.Request.AssetContentProviderId))
            {
                required.Add(BuildStepIds.AssetContent);
            }

            return required;
        }

        public IReadOnlyList<string> Validate(BuildExecutionContext context)
        {
            var errors = new List<string>();
            BuildRequest request = context.Request;
            if (request.UseHybridClr && request.CheatEnabled)
            {
                errors.Add(
                    "HybridCLR and per-build ENABLE_CHEAT cannot currently be combined safely for a Player build: " +
                    "the installed HybridCLR compilation API does not accept the Player's extra scripting defines. " +
                    "Disable Cheat or HybridCLR until a version-gated compilation strategy is installed.");
            }

            IReadOnlyList<string> scenes = request.BuildScenePaths;
            if (scenes.Count == 0)
            {
                errors.Add("At least one build scene is required.");
            }

            var uniqueScenes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string scene in scenes)
            {
                if (string.IsNullOrWhiteSpace(scene))
                {
                    errors.Add("Build scene paths may not be empty.");
                    continue;
                }

                if (!uniqueScenes.Add(scene))
                {
                    errors.Add($"Build scene is configured more than once: '{scene}'.");
                    continue;
                }

                try
                {
                    BuildPathPolicy.ValidatePortableProjectRelativePath(
                        scene,
                        "Build scene path");
                    if (!scene.StartsWith("Assets/", StringComparison.Ordinal)
                        || !scene.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            "Build scenes must be project-relative .unity assets below Assets.");
                    }

                    string assetsRoot = Path.Combine(request.ProjectRoot, "Assets");
                    string absolute = Path.GetFullPath(Path.Combine(request.ProjectRoot, scene));
                    BuildPathPolicy.EnsureSafeReadableFile(assetsRoot, absolute);
                    if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scene) == null)
                    {
                        throw new InvalidOperationException(
                            "The path does not resolve to an imported SceneAsset.");
                    }
                }
                catch (Exception exception)
                {
                    errors.Add($"Invalid build scene '{scene}': {exception.Message}");
                }
            }

            if (string.IsNullOrWhiteSpace(request.CompanyName))
            {
                errors.Add("Company name is required.");
            }

            try
            {
                BuildPathPolicy.ValidatePortableFileName(request.ProductName, "Product name");
            }
            catch (ArgumentException exception)
            {
                errors.Add(exception.Message);
            }

            if (string.IsNullOrWhiteSpace(request.ApplicationIdentifier))
            {
                errors.Add("Application identifier is required.");
            }

            ValidateAssetContentPlayerBuildHook(context, errors);

            bool cheatRequested = request.CheatEnabled;
            bool cheatInstalled = CheatBuildDefineUtility.IsCheatModuleInstalled();
            bool globalCheatDefine = CheatBuildDefineUtility.HasCheatDefine(request.NamedTarget);
            if (cheatRequested && !cheatInstalled)
            {
                errors.Add("Cheat capability was requested, but CycloneGames.Cheat.Runtime is unavailable.");
            }
            else if (!cheatRequested && globalCheatDefine)
            {
                errors.Add(
                    $"Global {CheatBuildDefineUtility.DefineSymbol} is defined for this target. " +
                    "Remove the global symbol; this pipeline only adds per-build symbols and never mutates PlayerSettings defines.");
            }

            if (request.EnablePlayerObfuscation)
            {
                if (!ObfuzIntegrator.IsBaseObfuzAvailable())
                {
                    errors.Add("Player obfuscation is enabled, but the base Obfuz package is unavailable.");
                }
                else if (!ObfuzIntegrator.TryGetObfuzBuildPipelineEnabled(out _))
                {
                    errors.Add("Obfuz settings are unavailable or incomplete. Provision them before building.");
                }
                else if (!ObfuzIntegrator.VerifyEncryptionVMCompiled())
                {
                    errors.Add("Obfuz Encryption VM is not compiled. Provision it before building.");
                }
            }

            return errors;
        }

        public void Execute(BuildExecutionContext context)
        {
            BuildRequest request = context.Request;

            BuildOptions options = BuildOptions.CompressWithLz4;
            if (request.Incrementality == BuildIncrementality.Clean)
            {
                options |= BuildOptions.CleanBuildCache;
            }

            if (request.DebugBuild)
            {
                options |= BuildOptions.Development | BuildOptions.AllowDebugging | BuildOptions.ConnectWithProfiler;
            }

            bool cheatRequested = request.CheatEnabled;
            string[] extraDefines = cheatRequested && CheatBuildDefineUtility.IsCheatModuleInstalled()
                ? new[] { CheatBuildDefineUtility.DefineSymbol }
                : Array.Empty<string>();

            IDisposable assetContentPlayerSession = null;
            PlayerOutputTransaction outputTransaction = null;
            Exception playerBuildFailure = null;
            Exception sessionRestoreFailure = null;
            Exception outputRecoveryFailure = null;
            BuildReport report = null;
            try
            {
                outputTransaction = PlayerOutputTransaction.Begin(request);
                var optionsData = new BuildPlayerOptions
                {
                    scenes = request.BuildScenePaths.ToArray(),
                    locationPathName = outputTransaction.StageOutputPath,
                    target = request.Target,
                    options = options,
                    extraScriptingDefines = extraDefines
                };

                if (!string.IsNullOrWhiteSpace(request.AssetContentProviderId))
                {
                    IAssetContentBuildAdapter adapter = context.ResolveAssetContentAdapter();
                    if (adapter == null)
                    {
                        throw new BuildFailedException(
                            $"No compatible '{request.AssetContentProviderId}' content adapter is available for the Player build.");
                    }

                    if (adapter is IAssetContentPlayerBuildSessionFactory sessionFactory)
                    {
                        assetContentPlayerSession = sessionFactory.BeginPlayerBuild(
                            CreateAssetContentRequest(context));
                    }
                }

                BuildGlobalStateScope.EnsureCurrentPlayerSettingsOwned();
                report = UnityEditor.BuildPipeline.BuildPlayer(optionsData);
            }
            catch (Exception exception)
            {
                playerBuildFailure = exception;
            }
            finally
            {
                if (assetContentPlayerSession != null)
                {
                    try
                    {
                        assetContentPlayerSession.Dispose();
                    }
                    catch (Exception restoreException)
                    {
                        sessionRestoreFailure = restoreException;
                    }
                }
            }

            context.PlayerBuildReport = report;
            if (playerBuildFailure == null
                && (report == null || report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded))
            {
                string result = report == null ? "null report" : report.summary.result.ToString();
                playerBuildFailure = new BuildFailedException(
                    $"Player build failed with result '{result}'.");
            }

            Exception combinedFailure = CombinePlayerBuildFailures(
                playerBuildFailure,
                sessionRestoreFailure);
            if (combinedFailure == null)
            {
                try
                {
                    if (request.DeleteDebugFiles && !request.DebugBuild)
                    {
                        DeleteDesktopDebugDirectories(
                            request,
                            outputTransaction.StageOutputPath);
                    }

                    BuildGlobalStateScope.EnsureCurrentPlayerSettingsOwned();
                    outputTransaction.Commit();
                }
                catch (Exception exception)
                {
                    combinedFailure = exception;
                }
            }

            if (outputTransaction != null)
            {
                try
                {
                    outputTransaction.Dispose();
                }
                catch (Exception exception)
                {
                    outputRecoveryFailure = exception;
                }
            }

            combinedFailure = CombinePlayerBuildFailures(
                combinedFailure,
                outputRecoveryFailure);
            if (combinedFailure != null)
            {
                ExceptionDispatchInfo.Capture(combinedFailure).Throw();
            }
        }

        public void Cleanup(BuildExecutionContext context)
        {
        }

        private static Exception CombinePlayerBuildFailures(
            Exception playerBuildFailure,
            Exception sessionRestoreFailure)
        {
            if (playerBuildFailure == null)
            {
                return sessionRestoreFailure;
            }

            if (sessionRestoreFailure == null)
            {
                return playerBuildFailure;
            }

            return new AggregateException(
                "Player build and asset-content provider state restoration both failed.",
                playerBuildFailure,
                sessionRestoreFailure);
        }

        private static void ValidateAssetContentPlayerBuildHook(
            BuildExecutionContext context,
            ICollection<string> errors)
        {
            if (string.IsNullOrWhiteSpace(context.Request.AssetContentProviderId)
                || context.Version == null)
            {
                return;
            }

            IAssetContentBuildAdapter adapter;
            try
            {
                adapter = context.ResolveAssetContentAdapter();
            }
            catch (Exception exception)
            {
                errors.Add($"Asset-content Player hook resolution failed: {exception.Message}");
                return;
            }

            if (!(adapter is IAssetContentPlayerBuildSessionFactory sessionFactory))
            {
                return;
            }

            try
            {
                IReadOnlyList<string> hookErrors = sessionFactory.ValidatePlayerBuild(
                    CreateAssetContentRequest(context)) ?? Array.Empty<string>();
                foreach (string error in hookErrors)
                {
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        errors.Add($"Asset-content Player hook: {error}");
                    }
                }
            }
            catch (Exception exception)
            {
                errors.Add($"Asset-content Player hook validation failed: {exception.Message}");
            }
        }

        private static AssetContentBuildRequest CreateAssetContentRequest(BuildExecutionContext context)
        {
            return new AssetContentBuildRequest(
                context.Request.Target,
                context.Version.PackageVersion,
                context.Request.ProjectRoot,
                context.Request.AssetContentConfiguration,
                context.Request.Incrementality,
                context.Request.BatchMode);
        }

        private static void DeleteDesktopDebugDirectories(
            BuildRequest request,
            string outputPath)
        {
            if (request.Target != BuildTarget.StandaloneWindows64
                && request.Target != BuildTarget.StandaloneOSX
                && request.Target != BuildTarget.StandaloneLinux64)
            {
                return;
            }

            string parent = Path.GetDirectoryName(outputPath);
            string productName = request.ProductName;
            string[] names =
            {
                productName + "_BackUpThisFolder_ButDontShipItWithYourGame",
                productName + "_BurstDebugInformation_DoNotShip"
            };

            foreach (string name in names)
            {
                string path = Path.Combine(parent, name);
                if (!Directory.Exists(path))
                {
                    continue;
                }

                BuildPathPolicy.EnsureSafeDeleteDirectoryTree(
                    request.ProjectRoot,
                    path,
                    request.BuildRoot,
                    request.AllowExternalOutput);
                Directory.Delete(path, true);
            }
        }
    }

}
