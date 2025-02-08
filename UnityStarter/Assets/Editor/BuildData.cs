#if UNITY_EDITOR
using UnityEditor;
#endif

using UnityEngine;

namespace CycloneGames.Editor.Build
{
    [CreateAssetMenu(menuName = "CycloneGames/Build/BuildData")]
    public class BuildData : ScriptableObject
    {
#if UNITY_EDITOR
        [Header("------ Build Scene Config ------")] 
        
        [SerializeField] private SceneAsset launchScene;
        
        public SceneAsset LaunchScene => launchScene;
        
        public string GetLaunchScenePath()
        {
            if (launchScene != null)
            {
                // 获取 launchScene 的实例 ID
                string path = AssetDatabase.GetAssetPath(launchScene);
                return path;
            }

            return string.Empty;
        }
#endif
    }
}