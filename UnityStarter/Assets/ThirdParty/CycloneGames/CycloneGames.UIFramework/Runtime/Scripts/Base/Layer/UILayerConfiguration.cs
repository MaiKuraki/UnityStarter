using UnityEngine;

namespace CycloneGames.UIFramework
{
    [CreateAssetMenu(fileName = "UILayer_", menuName = "CycloneGames/UIFramework/UILayer Configuration")] // Added file name convention
    [System.Serializable]
    public class UILayerConfiguration : ScriptableObject
    {
        // This layerName must be the same as UILayer's LayerName in UIRoot's layerList
        [SerializeField] private string layerName;
        public string LayerName => layerName;
    }
}