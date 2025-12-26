using UnityEngine;
using CycloneGames.Logger;

namespace CycloneGames.UIFramework.Runtime
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
            // Auto-cleanup logic to ensure 0-overhead when using Location mode
            if (source == PrefabSource.Location && windowPrefab != null)
            {
                CLogger.LogWarning($"[UIWindowConfiguration] Source switched to 'Location', clearing 'WindowPrefab' reference to prevent memory overhead for '{this.name}'.");
                windowPrefab = null;
            }

            if (windowPrefab != null)
            {
                // Ensure the prefab actually has a UIWindow component.
                if (windowPrefab.GetComponent<UIWindow>() == null)
                {
                    CLogger.LogError($"[UIWindowConfiguration] Prefab '{windowPrefab.name}' for '{this.name}' does not have a UIWindow component.");
                    // windowPrefab = null; // Optionally clear if invalid
                }
            }
            if (string.IsNullOrEmpty(prefabLocation) && windowPrefab == null)
            {
                CLogger.LogWarning($"[UIWindowConfiguration] Neither PrefabLocation nor WindowPrefab is set for '{this.name}'.");
            }
            // Warn when the selected source is not properly configured
            if (source == PrefabSource.PrefabReference && windowPrefab == null)
            {
                CLogger.LogWarning($"[UIWindowConfiguration] Source is 'PrefabReference' but WindowPrefab is not assigned for '{this.name}'.");
            }
            if (source == PrefabSource.Location && string.IsNullOrEmpty(prefabLocation))
            {
                CLogger.LogWarning($"[UIWindowConfiguration] Source is 'Location' but PrefabLocation is empty for '{this.name}'.");
            }
            if (layer == null)
            {
                CLogger.LogWarning($"[UIWindowConfiguration] Layer is not set for '{this.name}'.");
            }
        }
#endif
    }
}
