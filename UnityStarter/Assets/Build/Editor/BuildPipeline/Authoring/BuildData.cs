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

    [CreateAssetMenu(menuName = "CycloneGames/Build/Build Profile")]
    public sealed class BuildData : ScriptableObject
    {
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

        [Tooltip("Project-relative path for temporary VersionInfoData generated during a build.")]
        [SerializeField] private string versionInfoAssetPath = "Assets/Resources/VersionInfoData.asset";

        [Tooltip("Additional scenes appended after the launch scene.")]
        [SerializeField] private SceneAsset[] additionalScenes = Array.Empty<SceneAsset>();

        [Tooltip("Ordered build step identifiers. Dependencies are validated and compiled into a safe execution plan.")]
        [SerializeField] private string[] pipelineSteps =
        {
            "hot-update",
            "asset-content",
            "player"
        };

        [Tooltip("Enable the HybridCLR step. Missing packages or configuration fail preflight.")]
        [SerializeField] private bool useHybridCLR = false;

        [Tooltip("Enable the base Obfuz pipeline for Player assemblies. This is independent from HybridCLR hot-update DLL obfuscation.")]
        [SerializeField] private bool enablePlayerObfuscation = false;

        [Tooltip("Controls whether ENABLE_CHEAT is applied during player builds.")]
        [SerializeField] private CheatBuildMode cheatBuildMode = CheatBuildMode.Disabled;

        [Tooltip("Adapter identifier registered by an IAssetContentBuildAdapter. Leave empty when the project has no external content build.")]
        [SerializeField] private string assetContentProviderId = string.Empty;

        [Tooltip("Configuration asset passed to the selected content adapter.")]
        [SerializeField] private ScriptableObject assetContentConfiguration;

        [Tooltip("Explicit HybridCLR build configuration. Required when HybridCLR is enabled.")]
        [SerializeField] private HybridCLRBuildConfig hybridCLRBuildConfig;

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
        public string[] PipelineSteps => pipelineSteps == null
            ? Array.Empty<string>()
            : (string[])pipelineSteps.Clone();

        public bool UseHybridCLR => useHybridCLR;
        public bool EnablePlayerObfuscation => enablePlayerObfuscation;
        public CheatBuildMode CheatBuildMode => cheatBuildMode;
        public string AssetContentProviderId => assetContentProviderId;
        public ScriptableObject AssetContentConfiguration => assetContentConfiguration;
        public HybridCLRBuildConfig HybridCLRBuildConfig => hybridCLRBuildConfig;
    }
}
