using UnityEngine;

namespace CycloneGames.UIFramework
{
    [CreateAssetMenu(menuName = "CycloneGames/UIFramework/UILayer")]
    [System.Serializable]
    public class UILayerConfirguration : ScriptableObject
    {
        //  This layerName must be same as UILayer's LayerName in UIRoot
        [SerializeField] private string layerName;

        public string LayerName => layerName;
    }
}