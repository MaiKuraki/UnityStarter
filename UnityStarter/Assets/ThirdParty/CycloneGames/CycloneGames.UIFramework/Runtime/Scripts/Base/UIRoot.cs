using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.UIFramework
{
    public class UIRoot : MonoBehaviour
    {
        private const string DEBUG_FLAG = "[UIRoot]";
        [SerializeField] private Camera uiCamera;
        [SerializeField] private List<UILayer> layerList;

        public Camera UICamera => uiCamera;

        protected void Start()
        {
            ValidateLayers();
        }

        private void ValidateLayers()
        {
            // NOTE:
            // The UILayer's canvas will be get in Awake function, so
            // these steps must be called in the Start function.
            // Do not call them in Awake.
            
            // Create an array of validation checks
            System.Func<bool>[] checks = new System.Func<bool>[]
            {
                CheckLayerCountValid,
                CheckLayerNameValid,
                CheckLayerOrderValid
            };

            // Execute each validation check and return early if any fails
            foreach (var check in checks)
            {
                if (!check())
                {
                    return;
                }
            }
        }

        bool CheckLayerCountValid()
        {
            // Verify that the number of layers matches the number of child objects
            if (layerList.Count != transform.childCount)
            {
                Debug.LogError(
                    $"{DEBUG_FLAG} The number of child objects does not match the number of layers in the LayerList.");
                return false;
            }

            return true;
        }

        bool CheckLayerNameValid()
        {
            // Verify that layer names are unique and not empty
            HashSet<string> layerNames = new HashSet<string>();
            foreach (var layer in layerList)
            {
                if (string.IsNullOrEmpty(layer.LayerName))
                {
                    Debug.LogError($"{DEBUG_FLAG} Layer name can not be empty, check ui layer in UIRoot");
                    return false;
                }

                if (!layerNames.Add(layer.LayerName))
                {
                    Debug.LogError($"{DEBUG_FLAG} Layer name duplicated, layerName: {layer.LayerName}");
                    return false;
                }
            }

            return true;
        }

        bool CheckLayerOrderValid()
        {
            // Verify that layer orders are unique and canvas sorting is overridden
            HashSet<int> layerOrder = new HashSet<int>();
            foreach (var layer in layerList)
            {
                if (!layer.UICanvas || !layer.UICanvas.overrideSorting)
                {
                    Debug.LogError($"{DEBUG_FLAG} Layer sorting must be override, check ui layer in UIRoot");
                    return false;
                }
                
                if (!layerOrder.Add(layer.UICanvas.sortingOrder))
                {
                    Debug.LogError($"{DEBUG_FLAG} Layer order duplicated, layerName: {layer.LayerName}");
                    return false;
                }
                
                // Debug.Log($"{layer.UICanvas.sortingOrder}");
            }

            return true;
        }

        public UILayer GetUILayer(string LayerName)
        {
            // Return the UILayer with the specified name, or null if not found
            if (string.IsNullOrEmpty(LayerName))
            {
                Debug.LogError($"{DEBUG_FLAG} Invalid Layer Name");
                return null;
            }
            return layerList.Find(m => m.LayerName == LayerName);
        }

        public UILayer TryGetUILayerFromPageName(string pageName)
        {
            foreach (var layer in layerList)
            {
                if (layer.GetUIPage(pageName) != null)
                {
                    return layer;
                }
            }
    
            return null;
        }
    }
}