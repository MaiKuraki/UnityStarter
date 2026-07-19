using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CycloneGames.UIFramework.Runtime
{
    [DisallowMultipleComponent]
    public sealed class UIRoot : MonoBehaviour
    {
        [SerializeField] private Camera uiCamera;
        [SerializeField] private Canvas rootCanvas;
        [SerializeField] private List<UILayer> layerList = new List<UILayer>(8);

        private readonly Dictionary<string, UILayer> _layers =
            new Dictionary<string, UILayer>(8, StringComparer.Ordinal);

        private RectTransform _rootRectTransform;
        private UIAssetContextProvider _assetContextProvider;
        private bool _initialized;

        public Camera UICamera => uiCamera;
        public Canvas RootCanvas => rootCanvas;
        public UIAssetContextProvider AssetContextProvider => _assetContextProvider;
        public int LayerCount => layerList.Count;

        private void Awake()
        {
            if (rootCanvas != null)
            {
                EnsureInitialized();
            }
        }

        public void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            if (rootCanvas == null)
            {
                throw new InvalidOperationException($"UIRoot '{name}' requires a Root Canvas reference.");
            }

            _rootRectTransform = rootCanvas.transform as RectTransform;
            if (_rootRectTransform == null)
            {
                throw new InvalidOperationException($"UIRoot '{name}' Root Canvas must use a RectTransform.");
            }

            _assetContextProvider = GetComponent<UIAssetContextProvider>();
            _layers.Clear();
            for (int i = 0; i < layerList.Count; i++)
            {
                UILayer layer = layerList[i];
                if (layer == null)
                {
                    throw new InvalidOperationException($"UIRoot '{name}' contains a null layer at index {i}.");
                }

                layer.EnsureInitialized();
                string layerName = layer.LayerName;
                if (string.IsNullOrWhiteSpace(layerName))
                {
                    throw new InvalidOperationException($"UIRoot '{name}' contains a layer with an empty name.");
                }

                if (!_layers.TryAdd(layerName, layer))
                {
                    throw new InvalidOperationException(
                        $"UIRoot '{name}' contains duplicate layer name '{layerName}'.");
                }
            }

            _initialized = true;
        }

        public bool TryGetLayer(string layerName, out UILayer layer)
        {
            EnsureInitialized();
            if (string.IsNullOrEmpty(layerName))
            {
                layer = null;
                return false;
            }

            return _layers.TryGetValue(layerName, out layer);
        }

        public bool TryGetLayer(UILayerConfiguration configuration, out UILayer layer)
        {
            if (configuration == null)
            {
                layer = null;
                return false;
            }

            return TryGetLayer(configuration.LayerName, out layer);
        }

        public void CopyLayers(List<UILayer> destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            destination.Clear();
            destination.AddRange(layerList);
        }

        public Vector2 GetRootCanvasSize()
        {
            EnsureInitialized();
            return _rootRectTransform.rect.size;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (rootCanvas == null)
            {
                rootCanvas = GetComponent<Canvas>();
            }
        }
#endif
    }
}
