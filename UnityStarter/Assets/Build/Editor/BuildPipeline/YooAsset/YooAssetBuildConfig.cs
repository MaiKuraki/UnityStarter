using UnityEngine;

namespace Build.Pipeline.Editor
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

        // Note: UI layout and detailed tooltips are handled by the CustomEditor (YooAssetBuildConfigEditor)
        [HideInInspector]
        public bool copyToStreamingAssets = true;

        [HideInInspector]
        public bool copyToOutputDirectory = true;

        [HideInInspector]
        public string buildOutputDirectory = "";
    }
}