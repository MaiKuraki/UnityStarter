using UnityEngine;

namespace CycloneGames.UIFramework
{
    [CreateAssetMenu(fileName = "UIWindow_", menuName = "CycloneGames/UIFramework/UIWindow Configuration")]
    [System.Serializable]
    public class UIWindowConfiguration : ScriptableObject
    {
        /// <summary>
        /// Defines the source of the window prefab to avoid ambiguous configuration.
        /// </summary>
        public enum PrefabSource
        {
            PrefabReference = 0,
            Location = 1
        }

        //TODO: Maybe there is a better way to implement this, to resolve the dependency of UIWindowConfiguration and UIWindow.
        // One common way is to use Addressable asset references (AssetReferenceT<GameObject> or AssetReferenceT<UIWindow>)
        // which gives more flexibility and editor integration for assigning assets.
        [SerializeField] private PrefabSource source = PrefabSource.PrefabReference; // Explicitly pick one to prevent ambiguity
        [SerializeField] private UIWindow windowPrefab; // Should be a prefab of a UIWindow
        [SerializeField] private string prefabLocation; // Optional: location string for loading via AssetManagement
        [SerializeField] private UILayerConfiguration layer; // The layer this window belongs to

        public UIWindow WindowPrefab => windowPrefab;
        public string PrefabLocation => prefabLocation;
        public PrefabSource Source => source;
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
            if (string.IsNullOrEmpty(prefabLocation) && windowPrefab == null)
            {
                Debug.LogWarning($"[UIWindowConfiguration] Neither PrefabLocation nor WindowPrefab is set for '{this.name}'.", this);
            }
            // Warn when the selected source is not properly configured
            if (source == PrefabSource.PrefabReference && windowPrefab == null)
            {
                Debug.LogWarning($"[UIWindowConfiguration] Source is 'PrefabReference' but WindowPrefab is not assigned for '{this.name}'.", this);
            }
            if (source == PrefabSource.Location && string.IsNullOrEmpty(prefabLocation))
            {
                Debug.LogWarning($"[UIWindowConfiguration] Source is 'Location' but PrefabLocation is empty for '{this.name}'.", this);
            }
            if (layer == null)
            {
                Debug.LogWarning($"[UIWindowConfiguration] Layer is not set for '{this.name}'.", this);
            }
        }
#endif
    }
}