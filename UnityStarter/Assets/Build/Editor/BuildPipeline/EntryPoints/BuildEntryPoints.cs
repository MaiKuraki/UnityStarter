using System;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
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
            builder.AppendLine($"  Steps: {string.Join(" -> ", profile.PipelineSteps)}");
            BuildRecipeAnalysis recipe = BuildRecipePresetCatalog.Analyze(
                profile.PipelineSteps,
                profile.UseHybridCLR,
                !string.IsNullOrWhiteSpace(profile.AssetContentProviderId));
            builder.AppendLine(
                $"  Recipe: {(recipe.MatchedPreset.HasValue ? BuildRecipePresetCatalog.GetDisplayName(recipe.MatchedPreset.Value) : "Custom")}");
            builder.AppendLine(
                $"  Effective Outputs: Player={recipe.ProducesPlayer}, Content={recipe.ProducesAssetContent}, HotUpdate={recipe.ProducesHotUpdate}");
            builder.AppendLine($"  Asset Provider: {(string.IsNullOrWhiteSpace(profile.AssetContentProviderId) ? "None" : profile.AssetContentProviderId)}");
            builder.AppendLine($"  HybridCLR: {profile.UseHybridCLR}");
            builder.AppendLine($"  Player Obfuscation: {profile.EnablePlayerObfuscation}");

            if (!string.IsNullOrWhiteSpace(profile.AssetContentProviderId))
            {
                IAssetContentBuildAdapter adapter = BuildPipelineRegistry.ResolveContentAdapter(
                    profile.AssetContentProviderId);
                builder.AppendLine($"  Provider Adapter: {(adapter == null ? "Unavailable" : adapter.GetType().FullName)}");
            }

            Debug.Log(builder.ToString());
        }

        [MenuItem("Build/Pipeline/Run Selected Recipe/Release (Clean)", priority = 20)]
        public static void RunSelectedRecipeReleaseClean()
        {
            RunSelectedRecipe(
                EditorUserBuildSettings.activeBuildTarget,
                debug: false,
                incrementality: BuildIncrementality.Clean);
        }

        [MenuItem("Build/Pipeline/Run Selected Recipe/Release (Incremental)", priority = 21)]
        public static void RunSelectedRecipeReleaseIncremental()
        {
            RunSelectedRecipe(
                EditorUserBuildSettings.activeBuildTarget,
                debug: false,
                incrementality: BuildIncrementality.Incremental);
        }

        [MenuItem("Build/Pipeline/Run Selected Recipe/Development (Clean)", priority = 22)]
        public static void RunSelectedRecipeDevelopmentClean()
        {
            RunSelectedRecipe(
                EditorUserBuildSettings.activeBuildTarget,
                debug: true,
                incrementality: BuildIncrementality.Clean);
        }

        [MenuItem("Build/Pipeline/Run Selected Recipe/Development (Incremental)", priority = 23)]
        public static void RunSelectedRecipeDevelopmentIncremental()
        {
            RunSelectedRecipe(
                EditorUserBuildSettings.activeBuildTarget,
                debug: true,
                incrementality: BuildIncrementality.Incremental);
        }

        [MenuItem("Build/Pipeline/Android/Export Player Gradle Project", priority = 40)]
        public static void ExportAndroidPlayerGradleProject()
        {
            RunSelectedRecipe(
                BuildTarget.Android,
                debug: false,
                incrementality: BuildIncrementality.Clean,
                exportAndroidProject: true);
        }

        /// <summary>
        /// Canonical TeamCity, Jenkins, and other batch-mode entry point.
        /// </summary>
        public static void RunCommandLine()
        {
            try
            {
                BuildCommandLineOptions options = BuildCommandLine.Parse(Environment.GetCommandLineArgs());
                BuildData profile = BuildProfileResolver.ResolveCommandLine(options.BuildProfilePath);
                BuildRequest request = BuildRequestFactory.CreateForCommandLine(profile, options);
                EnsureSucceeded(new BuildPipelineRunner().Run(request));

                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(0);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                    return;
                }

                throw;
            }
        }

        private static void RunSelectedRecipe(
            BuildTarget target,
            bool debug,
            BuildIncrementality incrementality,
            bool exportAndroidProject = false)
        {
            BuildData profile = BuildProfileResolver.ResolveInteractive();
            RunProfile(profile, target, debug, incrementality, exportAndroidProject);
        }

        internal static void RunProfile(
            BuildData profile,
            BuildTarget target,
            bool debug,
            BuildIncrementality incrementality,
            bool exportAndroidProject = false)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            BuildRequest request = BuildRequestFactory.CreateInteractive(
                profile,
                target,
                debug,
                incrementality,
                exportAndroidProject);
            EnsureSucceeded(new BuildPipelineRunner().Run(request));
        }

        private static void EnsureSucceeded(BuildRunResult result)
        {
            if (!result.Succeeded)
            {
                throw new BuildFailedException(
                    $"Build run '{result.RunId}' failed. See '{result.ResultManifestPath}'.\n{result.Failure}");
            }
        }
    }
}
