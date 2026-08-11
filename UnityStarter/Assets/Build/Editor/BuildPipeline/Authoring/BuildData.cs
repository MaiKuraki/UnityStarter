using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    public enum CheatBuildMode
    {
        Disabled,
        DevelopmentBuilds,
        Enabled
    }

    public enum BuildSourceCleanlinessPolicy
    {
        RequireClean = 0,
        AllowDirtyDevelopment = 1
    }

    [Serializable]
    public sealed class BuildRecipeInvocation
    {
        [Tooltip("Whether this step participates in the saved build recipe.")]
        [SerializeField] private bool enabled;

        [Tooltip("Unique execution identity inside this recipe. CI overrides and result manifests address this value.")]
        [SerializeField] private string invocationId;

        [Tooltip("Registered build step type selected for this invocation.")]
        [SerializeField] private string stepTypeId;

        [Tooltip("Optional typed configuration asset owned by the selected step.")]
        [SerializeField] private ScriptableObject configuration;

        [Tooltip("Clean or incremental policy owned by this invocation. Different steps may use different policies in the same run.")]
        [SerializeField] private BuildIncrementality incrementality = BuildIncrementality.Clean;

        [Tooltip("Invocation-level DAG edges. Required dependencies must be selected; If Selected dependencies only order entries that participate in this run.")]
        [SerializeField] private BuildInvocationDependency[] dependencies =
            Array.Empty<BuildInvocationDependency>();

        public BuildRecipeInvocation(
            string invocationId,
            string stepTypeId,
            bool enabled = true,
            ScriptableObject configuration = null,
            BuildIncrementality incrementality = BuildIncrementality.Clean,
            IReadOnlyList<BuildInvocationDependency> dependencies = null)
        {
            this.invocationId = invocationId ?? string.Empty;
            this.stepTypeId = stepTypeId ?? string.Empty;
            this.enabled = enabled;
            this.configuration = configuration;
            this.incrementality = incrementality;
            this.dependencies = SnapshotDependencies(dependencies);
        }

        public bool Enabled => enabled;
        public string InvocationId => invocationId ?? string.Empty;
        public string StepTypeId => stepTypeId ?? string.Empty;
        public ScriptableObject Configuration => configuration;
        public BuildIncrementality Incrementality => incrementality;
        public IReadOnlyList<BuildInvocationDependency> Dependencies =>
            SnapshotDependencies(dependencies);

        internal BuildRecipeInvocation Snapshot()
        {
            return new BuildRecipeInvocation(
                InvocationId,
                StepTypeId,
                enabled,
                configuration,
                incrementality,
                dependencies);
        }

        private static BuildInvocationDependency[] SnapshotDependencies(
            IReadOnlyList<BuildInvocationDependency> values)
        {
            if (values == null || values.Count == 0)
            {
                return Array.Empty<BuildInvocationDependency>();
            }

            var snapshot = new BuildInvocationDependency[values.Count];
            for (int index = 0; index < values.Count; index++)
            {
                snapshot[index] = values[index]?.Snapshot()
                    ?? new BuildInvocationDependency(string.Empty);
            }

            return snapshot;
        }
    }

    [CreateAssetMenu(menuName = "CycloneGames/Build/Build Profile")]
    public sealed class BuildData : ScriptableObject
    {
        internal const string DefaultVersionInfoAssetPath =
            RuntimeVersionInfoPathPolicy.DefaultAssetPath;

        [Tooltip("The scene asset to use as the build entry point.")]
        [SerializeField] private SceneAsset launchScene;

        [Tooltip("Cross-platform native application version in major.minor.patch form. Content package versions append the VCS commit count separately.")]
        [SerializeField] private string applicationVersion = "0.1.0";

        [Tooltip("Base output directory for build results. Relative to project root.")]
        [SerializeField] private string outputBasePath = "Build";

        [Tooltip("Company name applied only for the duration of a player build.")]
        [SerializeField] private string companyName = string.Empty;

        [Tooltip("Product name and default executable name.")]
        [SerializeField] private string productName = string.Empty;

        [Tooltip("Application identifier applied only for the duration of a player build.")]
        [SerializeField] private string applicationIdentifier = string.Empty;

        [Tooltip("Project-relative path for temporary VersionInfoData. Missing destination folders are created transactionally for the build and removed afterward.")]
        [SerializeField] private string versionInfoAssetPath =
            DefaultVersionInfoAssetPath;

        [Tooltip("Additional scenes appended after the launch scene.")]
        [SerializeField] private SceneAsset[] additionalScenes = Array.Empty<SceneAsset>();

        [Tooltip("Invocation DAG used by the build compiler. Dependencies define order; array order is authoring-only.")]
        [SerializeField] private BuildRecipeInvocation[] recipeInvocations =
        {
            new BuildRecipeInvocation(BuildStepTypeIds.HotUpdate, BuildStepTypeIds.HotUpdate, enabled: false),
            new BuildRecipeInvocation(
                BuildStepTypeIds.AssetContent,
                BuildStepTypeIds.AssetContent,
                enabled: false,
                dependencies: new[]
                {
                    new BuildInvocationDependency(
                        BuildStepTypeIds.HotUpdate,
                        BuildDependencyMode.IfSelected)
                }),
            new BuildRecipeInvocation(
                BuildStepTypeIds.Player,
                BuildStepTypeIds.Player,
                dependencies: new[]
                {
                    new BuildInvocationDependency(
                        BuildStepTypeIds.HotUpdate,
                        BuildDependencyMode.IfSelected),
                    new BuildInvocationDependency(
                        BuildStepTypeIds.AssetContent,
                        BuildDependencyMode.IfSelected)
                })
        };

        [Tooltip("Controls whether ENABLE_CHEAT is applied during player builds.")]
        [SerializeField] private CheatBuildMode cheatBuildMode = CheatBuildMode.Disabled;

        [Tooltip("Requires a verified clean source workspace. Release builds always require clean source; this policy can relax only Development builds.")]
        [SerializeField] private BuildSourceCleanlinessPolicy sourceCleanlinessPolicy =
            BuildSourceCleanlinessPolicy.RequireClean;

        public string[] GetBuildScenePaths()
        {
            var paths = new List<string>();
            var seen = new HashSet<string>();

            AddScenePath(launchScene, paths, seen);
            if (additionalScenes != null)
            {
                foreach (SceneAsset scene in additionalScenes)
                {
                    AddScenePath(scene, paths, seen);
                }
            }

            return paths.ToArray();
        }

        private static void AddScenePath(SceneAsset scene, List<string> paths, HashSet<string> seen)
        {
            if (scene == null)
            {
                return;
            }

            string path = AssetDatabase.GetAssetPath(scene);
            if (!string.IsNullOrEmpty(path) && seen.Add(path))
            {
                paths.Add(path);
            }
        }

        public string ApplicationVersion => applicationVersion;
        public string OutputBasePath => outputBasePath;
        public string CompanyName => companyName;
        public string ProductName => productName;
        public string ApplicationIdentifier => applicationIdentifier;
        public string VersionInfoAssetPath => versionInfoAssetPath;
        public IReadOnlyList<BuildRecipeInvocation> RecipeInvocations
        {
            get
            {
                if (recipeInvocations == null || recipeInvocations.Length == 0)
                {
                    return Array.Empty<BuildRecipeInvocation>();
                }

                var snapshot = new BuildRecipeInvocation[recipeInvocations.Length];
                for (int index = 0; index < recipeInvocations.Length; index++)
                {
                    snapshot[index] = recipeInvocations[index]?.Snapshot()
                        ?? new BuildRecipeInvocation(string.Empty, string.Empty, enabled: false);
                }

                return snapshot;
            }
        }

        public IReadOnlyList<string> EnabledInvocationIds
        {
            get
            {
                if (recipeInvocations == null || recipeInvocations.Length == 0)
                {
                    return Array.Empty<string>();
                }

                var ids = new List<string>(recipeInvocations.Length);
                for (int index = 0; index < recipeInvocations.Length; index++)
                {
                    BuildRecipeInvocation step = recipeInvocations[index];
                    if (step != null && step.Enabled)
                    {
                        ids.Add(step.InvocationId);
                    }
                }

                return ids;
            }
        }

        public CheatBuildMode CheatBuildMode => cheatBuildMode;
        public BuildSourceCleanlinessPolicy SourceCleanlinessPolicy => sourceCleanlinessPolicy;
    }

    internal static class BuildAuthoringAssetGuard
    {
        public static IReadOnlyList<UnityEngine.Object> GetDirtyAssets(
            BuildData profile,
            IReadOnlyCollection<string> selectedInvocationIds = null)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            var dirty = new List<UnityEngine.Object>();
            AddIfDirtyAndPersistent(profile, dirty);
            HashSet<string> selected = selectedInvocationIds == null
                ? null
                : new HashSet<string>(selectedInvocationIds, StringComparer.OrdinalIgnoreCase);
            IReadOnlyList<BuildRecipeInvocation> steps = profile.RecipeInvocations;
            for (int index = 0; index < steps.Count; index++)
            {
                BuildRecipeInvocation step = steps[index];
                bool isSelected = selected == null
                    ? step.Enabled
                    : selected.Contains(step.InvocationId);
                if (isSelected)
                {
                    AddIfDirtyAndPersistent(step.Configuration, dirty);
                    if (step.Configuration is PlayerBuildConfiguration playerConfiguration)
                    {
                        IReadOnlyList<PlayerBuildExtensionConfiguration> extensions =
                            playerConfiguration.Extensions;
                        for (int extensionIndex = 0;
                             extensionIndex < extensions.Count;
                             extensionIndex++)
                        {
                            AddIfDirtyAndPersistent(extensions[extensionIndex], dirty);
                        }
                    }
                }
            }

            return dirty;
        }

        public static void EnsureSaved(
            BuildData profile,
            IReadOnlyCollection<string> selectedInvocationIds = null)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            IReadOnlyList<UnityEngine.Object> dirty = GetDirtyAssets(
                profile,
                selectedInvocationIds);
            if (dirty.Count == 0)
            {
                return;
            }

            var paths = new string[dirty.Count];
            for (int index = 0; index < dirty.Count; index++)
            {
                paths[index] = AssetDatabase.GetAssetPath(dirty[index]);
            }

            throw new InvalidOperationException(
                "Build authoring assets contain unsaved changes. Save them explicitly before building so the " +
                "Editor and CI consume the same recipe:\n" + string.Join("\n", paths));
        }

        private static void AddIfDirtyAndPersistent(
            UnityEngine.Object asset,
            ICollection<UnityEngine.Object> dirty)
        {
            if (asset == null
                || string.IsNullOrWhiteSpace(AssetDatabase.GetAssetPath(asset))
                || !EditorUtility.IsDirty(asset)
                || dirty.Contains(asset))
            {
                return;
            }

            dirty.Add(asset);
        }
    }
}
