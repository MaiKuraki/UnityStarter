using UnityEngine;

namespace Build.Pipeline.Editor
{
    public enum AddressablesVersionMode
    {
        GitCommitCount,
        Timestamp,
        Manual
    }

    [CreateAssetMenu(menuName = "CycloneGames/Build/Addressables Build Config")]
    public class AddressablesBuildConfig : ScriptableObject
    {
        [Header("Version Configuration")]
        [Tooltip("How to generate the content version.")]
        public AddressablesVersionMode versionMode = AddressablesVersionMode.GitCommitCount;

        [Tooltip("Used when Version Mode is Manual.")]
        public string manualVersion = "1.0.0";

        [Tooltip("Prefix for the version string (e.g. 'v1.0'). Used in GitCommitCount mode.")]
        public string versionPrefix = "v1.0";

        [HideInInspector]
        public bool buildRemoteCatalog = false;

        [HideInInspector]
        public bool copyToOutputDirectory = true;

        [HideInInspector]
        public string buildOutputDirectory = "";
    }
}