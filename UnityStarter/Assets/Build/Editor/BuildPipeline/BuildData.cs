#if UNITY_EDITOR
using UnityEditor;
#endif

using UnityEngine;

namespace Build.Pipeline.Editor
{
    [CreateAssetMenu(menuName = "CycloneGames/Build/BuildData")]
    public class BuildData : ScriptableObject
    {
#if UNITY_EDITOR
        [Header("------ Build Scene Config ------")] 
        
        [SerializeField] private SceneAsset launchScene;
        
        [Header("------ Build Pipeline Options ------")] 
        [Tooltip("If enabled and Buildalon package is present, use Buildalon helpers (e.g. SyncSolution).")]
        [SerializeField] private bool useBuildalon = false;

        [Tooltip("If enabled and HybridCLR package is present, perform HybridCLR generation before build.")]
        [SerializeField] private bool useHybridCLR = false;

        [Tooltip("If enabled and YooAsset package is present, perform YooAsset bundle build before player build.")]
        [SerializeField] private bool useYooAsset = false;
        
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

        public bool UseBuildalon => useBuildalon;
        public bool UseHybridCLR => useHybridCLR;
        public bool UseYooAsset => useYooAsset;
#endif
    }
}