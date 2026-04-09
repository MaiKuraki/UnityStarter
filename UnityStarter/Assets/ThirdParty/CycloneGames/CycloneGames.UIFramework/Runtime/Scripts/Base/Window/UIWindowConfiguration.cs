using UnityEngine;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.Logger;

namespace CycloneGames.UIFramework.Runtime
{
    [CreateAssetMenu(fileName = "UIWindow_", menuName = "CycloneGames/UIFramework/UIWindow Configuration")]
    [System.Serializable]
    public class UIWindowConfiguration : ScriptableObject
    {
        /// <summary>
        /// Defines how the window prefab is referenced and loaded.
        /// Modes are mutually exclusive — switching away from <see cref="PrefabReference"/> automatically
        /// clears the <c>windowPrefab</c> object reference to eliminate phantom asset retention.
        /// </summary>
        public enum PrefabSource
        {
            /// <summary>
            /// Direct Unity Object reference. Prefab is bundled into the build or loaded via Resources/built-in scenes.
            /// Use for always-present windows (splash, persistent HUD). Zero-latency; no asset-system dependency.
            /// </summary>
            PrefabReference = 0,

            /// <summary>
            /// <see cref="AssetRef{T}"/> key loaded through <c>CycloneGames.AssetManagement</c> (Addressables / YooAsset).
            /// Stored as a pure value-type struct — zero heap allocation at runtime.
            /// Requires an <see cref="IAssetPackage"/> to be initialized before loading.
            /// </summary>
            AssetReference = 2,

            /// <summary>
            /// Raw string path consumed by xAsset or any custom loader that accepts a plain address string.
            /// Use when your asset pipeline does not integrate with <c>CycloneGames.AssetManagement</c>.
            /// </summary>
            PathLocation = 1,
        }

        // ── Serialized fields ──────────────────────────────────────────────────────────────

        /// <summary>Active loading strategy. Serialized as an integer for forward compatibility.</summary>
        [SerializeField] private PrefabSource source = PrefabSource.PrefabReference;

        // PrefabReference mode — Unity Object ref; MUST be null in all other modes to prevent phantom retention.
        [SerializeField] private UIWindow windowPrefab;

        // AssetReference mode — value-type struct (two strings); zero heap impact.
        [SerializeField] private AssetRef<GameObject> prefabAssetRef;

        // PathLocation mode — plain address string for external / custom loaders.
        [SerializeField] private string prefabLocation;

        // Common configuration
        [SerializeField] private UILayerConfiguration layer;
        [SerializeField, Range(-100, 400)] private int priority = 0;

        // ── Public API ─────────────────────────────────────────────────────────────────────

        /// <summary>The active prefab source mode.</summary>
        public PrefabSource Source => source;

        /// <summary>[<see cref="PrefabSource.PrefabReference"/>] Direct prefab reference.
        /// Always <c>null</c> when any other mode is active.</summary>
        public UIWindow WindowPrefab => windowPrefab;

        /// <summary>[<see cref="PrefabSource.AssetReference"/>] Typed asset key for
        /// <c>CycloneGames.AssetManagement</c>. Returns <c>default</c> when other modes are active.</summary>
        public AssetRef<GameObject> PrefabAssetRef => prefabAssetRef;

        /// <summary>[<see cref="PrefabSource.PathLocation"/>] Raw address string for custom loaders.
        /// Empty when other modes are active.</summary>
        public string PrefabLocation => prefabLocation;

        /// <summary>
        /// The effective address string for the active non-direct source mode.
        /// Returns <see cref="AssetRef{T}.Location"/> for <see cref="PrefabSource.AssetReference"/>,
        /// <see cref="PrefabLocation"/> for <see cref="PrefabSource.PathLocation"/>,
        /// and <see cref="string.Empty"/> for <see cref="PrefabSource.PrefabReference"/>.
        /// No heap allocation — returns an existing string reference.
        /// </summary>
        public string EffectiveLocation
        {
            get
            {
                switch (source)
                {
                    case PrefabSource.AssetReference: return prefabAssetRef.Location ?? string.Empty;
                    case PrefabSource.PathLocation:   return prefabLocation ?? string.Empty;
                    default:                          return string.Empty;
                }
            }
        }

        /// <summary>Returns <c>true</c> when the active source mode is fully configured.</summary>
        public bool IsConfigured
        {
            get
            {
                switch (source)
                {
                    case PrefabSource.PrefabReference: return windowPrefab != null;
                    case PrefabSource.AssetReference:  return prefabAssetRef.IsValid;
                    case PrefabSource.PathLocation:    return !string.IsNullOrEmpty(prefabLocation);
                    default:                           return false;
                }
            }
        }

        /// <summary>The layer this window belongs to.</summary>
        public UILayerConfiguration Layer => layer;

        /// <summary>Render order within the same layer. Higher = closer to camera.</summary>
        public int Priority => priority;

#if UNITY_EDITOR
        private void OnValidate()
        {
            // ── Guard: clear the Unity Object reference in all non-direct modes ─────────────
            // Object references held by ScriptableObjects are included in the asset bundle/build
            // even when never accessed at runtime. Clearing prevents phantom memory retention.
            if (source != PrefabSource.PrefabReference && windowPrefab != null)
            {
                CLogger.LogWarning(
                    $"[UIWindowConfiguration] '{name}': Source is not PrefabReference — " +
                    $"clearing WindowPrefab to prevent phantom memory retention.");
                windowPrefab = null;
            }

            // ── Validate active source mode ───────────────────────────────────────────────
            switch (source)
            {
                case PrefabSource.PrefabReference:
                    if (windowPrefab == null)
                    {
                        CLogger.LogWarning(
                            $"[UIWindowConfiguration] '{name}': Source is PrefabReference but WindowPrefab is not assigned.");
                    }
                    else if (windowPrefab.GetComponent<UIWindow>() == null)
                    {
                        CLogger.LogError(
                            $"[UIWindowConfiguration] '{name}': Prefab '{windowPrefab.name}' does not have a UIWindow component.");
                    }
                    break;

                case PrefabSource.AssetReference:
                    if (!prefabAssetRef.IsValid)
                        CLogger.LogWarning(
                            $"[UIWindowConfiguration] '{name}': Source is AssetReference but PrefabAssetRef has no Location.");
                    break;

                case PrefabSource.PathLocation:
                    if (string.IsNullOrEmpty(prefabLocation))
                        CLogger.LogWarning(
                            $"[UIWindowConfiguration] '{name}': Source is PathLocation but PrefabLocation is empty.");
                    break;
            }

            if (layer == null)
                CLogger.LogWarning($"[UIWindowConfiguration] '{name}': Layer is not assigned. This window won't be placed on any layer.");
        }
#endif
    }
}
