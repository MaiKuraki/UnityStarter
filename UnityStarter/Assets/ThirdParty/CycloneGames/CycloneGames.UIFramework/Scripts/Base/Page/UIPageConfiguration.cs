using UnityEngine;

namespace CycloneGames.UIFramework
{
    [CreateAssetMenu(menuName = "CycloneGames/UIFramework/UIPage")]
    [System.Serializable]
    public class UIPageConfiguration : ScriptableObject
    {
        //TODO: Maybe there is a better way to implement this, to resolve the dependency of UIPageConfiguration and UIPage
        [SerializeField] private UIPage pagePrefab;
        [SerializeField] private UILayerConfirguration layer;

        public UIPage PagePrefab => pagePrefab;
        public UILayerConfirguration Layer => layer;
    }
}