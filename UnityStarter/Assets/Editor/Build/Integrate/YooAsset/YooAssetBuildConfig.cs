using UnityEngine;

namespace CycloneGames.Editor.Build
{
    public enum YooAssetVersionMode
    {
        GitCommitCount,
        Timestamp,
        Manual
    }

    [CreateAssetMenu(menuName = "CycloneGames/Build/YooAsset Build Config")]
    public class YooAssetBuildConfig : ScriptableObject
    {
        [Header("Version Configuration")]
        [Tooltip("How to generate the package version.")]
        public YooAssetVersionMode versionMode = YooAssetVersionMode.GitCommitCount;

        [Tooltip("Used when Version Mode is Manual.")]
        public string manualVersion = "1.0.0";

        [Tooltip("Prefix for the version string (e.g. 'v1.0'). Used in GitCommitCount mode.")]
        public string versionPrefix = "v1.0";

        [Header("Build Options")]
        [Tooltip("If true, the built bundles will be copied to the StreamingAssets folder.")]
        public bool copyToStreamingAssets = true;

        [Tooltip("The output directory for bundles. Leave empty to use project default (Bundles).")]
        public string buildOutputDirectory = "";
    }
}

