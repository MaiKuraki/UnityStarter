using System;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    public static class BuildEntryPoints
    {
        private const string LogTag = "[BuildPipeline]";

        [MenuItem("Build/Pipeline/Print Selected Profile", priority = 10)]
        public static void PrintSelectedProfile()
        {
            BuildData profile = BuildProfileResolver.ResolveInteractive();
            var builder = new StringBuilder(768);
            builder.AppendLine($"{LogTag} Build profile '{AssetDatabase.GetAssetPath(profile)}'");
            builder.AppendLine($"  Product: {profile.CompanyName}/{profile.ProductName}");
            builder.AppendLine($"  Application Identifier: {profile.ApplicationIdentifier}");
            builder.AppendLine($"  Version Prefix: {profile.ApplicationVersion}");
            builder.AppendLine($"  Output Root: {profile.OutputBasePath}");
            builder.AppendLine($"  Scenes: {string.Join(", ", profile.GetBuildScenePaths())}");
            builder.AppendLine(
                $"  Enabled Invocation Membership: {string.Join(", ", profile.EnabledInvocationIds)}");
            BuildRecipeAnalysis recipe = BuildRecipePresetCatalog.Analyze(
                profile.RecipeInvocations);
            builder.AppendLine(
                $"  Recipe: {(recipe.MatchedPreset.HasValue ? BuildRecipePresetCatalog.GetDisplayName(recipe.MatchedPreset.Value) : "Custom")}");
            builder.AppendLine(
                $"  Effective Outputs: Player={recipe.ProducesPlayer}, Content={recipe.ProducesAssetContent}, HotUpdate={recipe.ProducesHotUpdate}");
            builder.AppendLine(
                "  Compiled Execution Plan: " +
                (recipe.IsReady
                    ? string.Join(" -> ", recipe.ExecutionOrderInvocationIds)
                    : "Unavailable: " + string.Join(" | ", recipe.BlockingIssues)));

            foreach (BuildRecipeInvocation invocation in profile.RecipeInvocations)
            {
                string configurationPath = invocation.Configuration == null
                    ? "None"
                    : AssetDatabase.GetAssetPath(invocation.Configuration);
                string dependencies = string.Join(
                    ", ",
                    System.Linq.Enumerable.Select(
                        invocation.Dependencies,
                        dependency => dependency.Mode + ":" + dependency.InvocationId));
                builder.AppendLine(
                    $"  Invocation: enabled={invocation.Enabled}, id={invocation.InvocationId}, " +
                    $"type={invocation.StepTypeId}, policy={invocation.Incrementality}, " +
                    $"dependencies=[{dependencies}], config={configurationPath}");
            }

            Debug.Log(builder.ToString());
        }

        [MenuItem("Build/Pipeline/Run Selected Recipe/Release", priority = 20)]
        public static void RunSelectedRecipeRelease()
        {
            RunSelectedRecipe(
                EditorUserBuildSettings.activeBuildTarget,
                debug: false,
                exportAndroidProject: false);
        }

        [MenuItem("Build/Pipeline/Run Selected Recipe/Development", priority = 21)]
        public static void RunSelectedRecipeDevelopment()
        {
            RunSelectedRecipe(
                EditorUserBuildSettings.activeBuildTarget,
                debug: true,
                exportAndroidProject: false);
        }

        [MenuItem("Build/Pipeline/Android/Export Player Gradle Project", priority = 40)]
        public static void ExportAndroidPlayerGradleProject()
        {
            RunSelectedRecipe(
                BuildTarget.Android,
                debug: false,
                exportAndroidProject: true);
        }

        /// <summary>
        /// Canonical TeamCity, Jenkins, and other batch-mode entry point.
        /// </summary>
        public static void RunCommandLine()
        {
            BuildEntryPointExecutionResult execution =
                BuildEntryPointExecutor.ExecuteCommandLine(
                    GetCurrentProjectRoot(),
                    Environment.GetCommandLineArgs(),
                    DefaultBuildEntryPointOperations.Instance);
            CompleteExecution(execution);
        }

        private static void RunSelectedRecipe(
            BuildTarget target,
            bool debug,
            bool exportAndroidProject = false)
        {
            BuildEntryPointExecutionResult execution =
                BuildEntryPointExecutor.ExecuteInteractive(
                    GetCurrentProjectRoot(),
                    BuildProfileResolver.ResolveInteractive,
                    target,
                    debug,
                    exportAndroidProject,
                    invocationIdsOverride: null,
                    DefaultBuildEntryPointOperations.Instance);
            CompleteExecution(execution);
        }

        internal static void RunProfile(
            BuildData profile,
            BuildTarget target,
            bool debug,
            bool exportAndroidProject = false,
            System.Collections.Generic.IReadOnlyList<string> invocationIdsOverride = null)
        {
            BuildEntryPointExecutionResult execution =
                BuildEntryPointExecutor.ExecuteInteractive(
                    GetCurrentProjectRoot(),
                    () => profile,
                    target,
                    debug,
                    exportAndroidProject,
                    invocationIdsOverride,
                    DefaultBuildEntryPointOperations.Instance);
            CompleteExecution(execution);
        }

        internal static void RunLocalReleasePreview(
            BuildData profile,
            BuildTarget target,
            System.Collections.Generic.IReadOnlyList<string> invocationIdsOverride = null)
        {
            BuildEntryPointExecutionResult execution =
                BuildEntryPointExecutor.ExecuteInteractive(
                    GetCurrentProjectRoot(),
                    () => profile,
                    target,
                    debug: false,
                    exportAndroidProject: false,
                    invocationIdsOverride,
                    localReleasePreview: true,
                    DefaultBuildEntryPointOperations.Instance);
            CompleteExecution(execution);
        }

        private static void CompleteExecution(BuildEntryPointExecutionResult execution)
        {
            if (execution == null)
            {
                throw new ArgumentNullException(nameof(execution));
            }

            bool terminalFailureWasAlreadyLogged =
                execution.BuildResult != null
                && !execution.BuildResult.Succeeded
                && ReferenceEquals(
                    execution.Failure,
                    execution.BuildResult.Failure);
            if (execution.Failure != null && !terminalFailureWasAlreadyLogged)
            {
                Debug.LogException(execution.Failure);
            }

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(execution.ExitCode);
                return;
            }

            if (!execution.Succeeded
                && execution.Failure == null
                && execution.BuildResult == null)
            {
                Debug.LogError(
                    $"Build run '{execution.RunId}' failed without an exception. " +
                    $"See '{execution.ManifestPath}'.");
            }
        }

        private static string GetCurrentProjectRoot()
        {
            return System.IO.Path.GetFullPath(
                System.IO.Path.Combine(Application.dataPath, ".."));
        }
    }
}
