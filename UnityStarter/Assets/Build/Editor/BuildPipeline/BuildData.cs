#if UNITY_EDITOR
using UnityEditor;
#endif

using UnityEngine;

namespace Build.Pipeline.Editor
{
    public enum AssetManagementType
    {
        None,
        YooAsset,
        Addressables
    }

    [CreateAssetMenu(menuName = "CycloneGames/Build/BuildData")]
    public class BuildData : ScriptableObject
    {
#if UNITY_EDITOR
        [Tooltip("The scene asset to use as the build entry point.")]
        [SerializeField] private SceneAsset launchScene;

        [Tooltip("Application version prefix (e.g., 'v0.1'). Final version will be '{ApplicationVersion}.{CommitCount}'.")]
        [SerializeField] private string applicationVersion = "v0.1";

        [Tooltip("Base output directory for build results. Relative to project root.")]
        [SerializeField] private string outputBasePath = "Build";

        [Tooltip("If enabled and Buildalon package is present, use Buildalon helpers (e.g. SyncSolution).")]
        [SerializeField] private bool useBuildalon = false;

        [Tooltip("If enabled and HybridCLR package is present, perform HybridCLR generation before build.")]
        [SerializeField] private bool useHybridCLR = false;

        [Tooltip("Select the asset management system to use for resource hot-update.")]
        [SerializeField] private AssetManagementType assetManagementType = AssetManagementType.None;

        public SceneAsset LaunchScene => launchScene;

        public string GetLaunchScenePath()
        {
            if (launchScene != null)
            {
                string path = AssetDatabase.GetAssetPath(launchScene);
                return path;
            }

            return string.Empty;
        }

        public string ApplicationVersion => applicationVersion;
        public string OutputBasePath => outputBasePath;

        public bool UseBuildalon => useBuildalon;
        public bool UseHybridCLR => useHybridCLR;
        public AssetManagementType AssetManagementType => assetManagementType;

        public bool UseYooAsset => assetManagementType == AssetManagementType.YooAsset;
        public bool UseAddressables => assetManagementType == AssetManagementType.Addressables;
#endif
    }
}