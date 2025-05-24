using UnityEngine;

namespace CycloneGames.UIFramework // Added namespace
{
    [CreateAssetMenu(fileName = "UIWindow_", menuName = "CycloneGames/UIFramework/UIWindow Configuration")] // Added file name convention
    [System.Serializable]
    public class UIWindowConfiguration : ScriptableObject
    {
        //TODO: Maybe there is a better way to implement this, to resolve the dependency of UIWindowConfiguration and UIWindow.
        // One common way is to use Addressable asset references (AssetReferenceT<GameObject> or AssetReferenceT<UIWindow>)
        // which gives more flexibility and editor integration for assigning assets.
        [SerializeField] private UIWindow windowPrefab; // Should be a prefab of a UIWindow
        [SerializeField] private UILayerConfiguration layer; // The layer this window belongs to

        public UIWindow WindowPrefab => windowPrefab;
        public UILayerConfiguration Layer => layer;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (windowPrefab != null)
            {
                // Ensure the prefab actually has a UIWindow component.
                if (windowPrefab.GetComponent<UIWindow>() == null)
                {
                    Debug.LogError($"[UIWindowConfiguration] Prefab '{windowPrefab.name}' for '{this.name}' does not have a UIWindow component.", this);
                    // windowPrefab = null; // Optionally clear if invalid
                }
            }
            if (layer == null)
            {
                Debug.LogWarning($"[UIWindowConfiguration] Layer is not set for '{this.name}'.", this);
            }
        }
#endif
    }
}