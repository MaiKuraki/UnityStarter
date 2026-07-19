using System;
using UnityEngine;

namespace CycloneGames.UIFramework.Runtime
{
    [CreateAssetMenu(fileName = "UIWindow_", menuName = "CycloneGames/UIFramework/Window Configuration")]
    public sealed class UIWindowConfiguration : ScriptableObject
    {
        public enum PrefabSource
        {
            PrefabReference = 0,
            PathLocation = 1,
            AssetReference = 2,
        }

        public enum SubCanvasPolicy
        {
            InheritLayerCanvas = 0,
            IsolatedCanvas = 1,
        }

        [SerializeField] private string windowId;
        [SerializeField] private PrefabSource source = PrefabSource.PrefabReference;
        [SerializeField] private UIWindow windowPrefab;
        [SerializeField] private UIAssetReference prefabAssetRef;
        [SerializeField] private string prefabLocation;
        [SerializeField] private UILayerConfiguration layer;
        [SerializeField, Range(-100, 400)] private int priority;
        [SerializeField] private bool isSceneBound;
        [SerializeField] private SubCanvasPolicy subCanvasPolicy = SubCanvasPolicy.InheritLayerCanvas;

        public string WindowId => windowId ?? string.Empty;
        public PrefabSource Source => source;
        public UIWindow WindowPrefab => source == PrefabSource.PrefabReference ? windowPrefab : null;
        public UIAssetReference PrefabAssetReference =>
            source == PrefabSource.AssetReference ? prefabAssetRef : default;
        public string PrefabLocation => source == PrefabSource.PathLocation
            ? prefabLocation ?? string.Empty
            : string.Empty;
        public UILayerConfiguration Layer => layer;
        public int Priority => priority;
        public bool IsSceneBound => isSceneBound;
        public SubCanvasPolicy CanvasIsolationPolicy => subCanvasPolicy;

        public UIAssetReference EffectiveAssetReference
        {
            get
            {
                switch (source)
                {
                    case PrefabSource.AssetReference:
                        return prefabAssetRef;
                    case PrefabSource.PathLocation:
                        return new UIAssetReference(prefabLocation);
                    default:
                        return default;
                }
            }
        }

        public bool IsConfigured
        {
            get
            {
                if (string.IsNullOrWhiteSpace(windowId) || layer == null || !layer.IsValid)
                {
                    return false;
                }

                switch (source)
                {
                    case PrefabSource.PrefabReference:
                        return windowPrefab != null;
                    case PrefabSource.AssetReference:
                        return prefabAssetRef.IsValid;
                    case PrefabSource.PathLocation:
                        return !string.IsNullOrWhiteSpace(prefabLocation);
                    default:
                        return false;
                }
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            windowId = windowId?.Trim();
            prefabLocation = prefabLocation?.Trim();

            if (!Enum.IsDefined(typeof(PrefabSource), source))
            {
                source = PrefabSource.PrefabReference;
            }

            if (!Enum.IsDefined(typeof(SubCanvasPolicy), subCanvasPolicy))
            {
                subCanvasPolicy = SubCanvasPolicy.InheritLayerCanvas;
            }
        }
#endif
    }
}
