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
        BuildStepTypeIds.Player,
        DisplayName = "Player",
        Description = "Build the Unity Player into a transaction stage for post-restore publication.",
        Category = "Player",
        ConfigurationType = typeof(PlayerBuildConfiguration),
        ConfigurationRequired = false)]
    public sealed partial class PlayerBuildStep : IBuildStep, IBuildStepRequirementsProvider
    {
        public string StepTypeId => BuildStepTypeIds.Player;

        public BuildStepRequirements GetRequirements(
            BuildExecutionContext context,
            BuildStepInvocation invocation)
        {
            return BuildStepRequirements.UnityGlobalState
                | BuildStepRequirements.VersionInfoAsset
                | BuildStepRequirements.PlayerOutput;
        }

        public bool IsApplicable(
            BuildExecutionContext context,
            BuildStepInvocation invocation)
        {
            return true;
        }

        public IReadOnlyList<string> Validate(
            BuildExecutionContext context,
            BuildStepInvocation invocation)
        {
            var errors = new List<string>();
            BuildRequest request = context.Request;
            try
            {
                context.RegisterExclusiveOutputPaths(
                    invocation.InvocationId,
                    new[] { request.OutputDirectory });
            }
            catch (Exception exception)
            {
                errors.Add(
                    "Player exclusive output claim validation failed: " +
                    exception.Message);
            }

            ValidateHotUpdatePlayerBuildHooks(context, invocation, errors);

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

            ValidateAssetContentPlayerBuildHook(context, invocation, errors);

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

            ValidatePlayerBuildExtensions(context, invocation, errors);

            return errors;
        }

        public void Execute(
            BuildExecutionContext context,
            BuildStepInvocation invocation)
        {
            BuildRequest request = context.Request;
            IReadOnlyList<BuildStepInvocation> contentInvocations =
                context.GetDependencyInvocations(
                    invocation,
                    BuildStepTypeIds.AssetContent);
            IReadOnlyList<AssetContentBuildRequest> assetContentRequests =
                CreateAssetContentRequests(context, contentInvocations);
            IReadOnlyList<AssetContentPlayerSessionBinding>
                assetContentSessionBindings =
                    ResolveAssetContentPlayerSessionBindings(
                        context,
                        contentInvocations,
                        assetContentRequests);
            IReadOnlyList<string> exclusiveSessionErrors =
                AssetContentPlayerSessionPolicy.ValidateExclusiveClaims(
                    invocation.InvocationId,
                    assetContentSessionBindings
                        .Select(binding => binding.Claim)
                        .ToArray());
            if (exclusiveSessionErrors.Count > 0)
            {
                throw new BuildFailedException(string.Join(
                    Environment.NewLine,
                    exclusiveSessionErrors));
            }

            BuildOptions options = BuildOptions.CompressWithLz4;
            if (invocation.Incrementality == BuildIncrementality.Clean)
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

            IReadOnlyList<PlayerExtensionBinding> extensionBindings =
                ResolvePlayerExtensionBindings(context, invocation);
            IReadOnlyList<IPlayerBuildEnvironmentGuard> environmentGuards =
                PlayerBuildExtensionRegistry.ResolveEnvironmentGuards();
            PlayerBuildEnvironmentRequest environmentRequest =
                CreatePlayerEnvironmentRequest(
                    context,
                    invocation,
                    assetContentRequests,
                    extensionBindings);
            string extensionFingerprint =
                context.GetRequiredPlayerExtensionFingerprint();
            var playerBuildSessions = new List<IDisposable>();
            PlayerOutputTransaction outputTransaction = null;
            Exception playerBuildFailure = null;
            Exception sessionRestoreFailure = null;
            Exception outputRecoveryFailure = null;
            BuildReport report = null;
            try
            {
                outputTransaction = PlayerOutputTransaction.Begin(
                    request,
                    invocation.Incrementality,
                    extensionFingerprint);
                var optionsData = new BuildPlayerOptions
                {
                    scenes = request.BuildScenePaths.ToArray(),
                    locationPathName = outputTransaction.StageOutputPath,
                    target = request.Target,
                    options = options,
                    extraScriptingDefines = extraDefines
                };

                for (int guardIndex = 0;
                     guardIndex < environmentGuards.Count;
                     guardIndex++)
                {
                    IDisposable session = environmentGuards[guardIndex]
                        .BeginEnvironment(environmentRequest);
                    if (session != null)
                    {
                        playerBuildSessions.Add(session);
                    }
                }

                for (int extensionIndex = 0;
                     extensionIndex < extensionBindings.Count;
                     extensionIndex++)
                {
                    PlayerExtensionBinding binding = extensionBindings[extensionIndex];
                    IDisposable session = binding.Adapter.BeginPlayerBuild(
                        binding.Request);
                    if (session != null)
                    {
                        playerBuildSessions.Add(session);
                    }
                }

                for (int contentIndex = 0;
                     contentIndex < assetContentSessionBindings.Count;
                     contentIndex++)
                {
                    AssetContentPlayerSessionBinding binding =
                        assetContentSessionBindings[contentIndex];
                    IDisposable session = binding.Factory.BeginPlayerBuild(
                        binding.Request);
                    if (session != null)
                    {
                        playerBuildSessions.Add(session);
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
                sessionRestoreFailure = DisposePlayerBuildSessions(
                    playerBuildSessions);
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
                    context.RegisterDeferredPublication(outputTransaction);
                    outputTransaction = null;
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

    }
}
