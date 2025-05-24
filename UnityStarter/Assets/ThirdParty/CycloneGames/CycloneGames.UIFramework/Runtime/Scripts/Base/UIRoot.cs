using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.UIFramework // Added namespace
{
    public class UIRoot : MonoBehaviour
    {
        private const string DEBUG_FLAG = "[UIRoot]";
        [SerializeField] private Camera uiCamera;
        [SerializeField] private List<UILayer> layerList = new List<UILayer>(); // Initialize to avoid null issues

        public Camera UICamera => uiCamera;
        
        // For faster UILayer lookup by name
        private Dictionary<string, UILayer> layerMap;

        // Changed from Start to Awake to ensure layerMap is ready if other services need it early.
        // UILayer.Awake initializes its Canvas, so UIRoot.Awake should be fine if its order is after UILayers,
        // or if UILayer.UICanvas is accessed after its Awake.
        // For safety, UILayer validation logic depending on Canvas properties might move to Start if needed.
        // But GetUILayer can be used in Awake if it only relies on LayerName.
        protected void Awake()
        {
            if (uiCamera == null)
            {
                Debug.LogError($"{DEBUG_FLAG} UI Camera is not assigned in UIRoot!", this);
            }
            
            InitializeLayerMap();
            // Validation can still be in Start if it depends on components fully initialized in other Awakes (like UILayer's Canvas).
            // For now, keeping validation in Start as per original, as it accesses UILayer.UICanvas.
        }
        
        protected void Start()
        {
            // ValidateLayers needs UILayer.UICanvas, which is set in UILayer.Awake().
            // Unity's execution order generally calls all Awakes before any Starts.
            ValidateLayers();
        }
        
        private void InitializeLayerMap()
        {
            layerMap = new Dictionary<string, UILayer>(layerList.Count);
            foreach (var layer in layerList)
            {
                if (layer != null && !string.IsNullOrEmpty(layer.LayerName))
                {
                    if (!layerMap.ContainsKey(layer.LayerName))
                    {
                        layerMap[layer.LayerName] = layer;
                    }
                    else
                    {
                        Debug.LogError($"{DEBUG_FLAG} Duplicate layer name '{layer.LayerName}' found in layerList. Only the first one was added to the map.", this);
                    }
                }
                else if (layer == null)
                {
                    Debug.LogWarning($"{DEBUG_FLAG} Null entry found in layerList.", this);
                }
                else // layer != null but LayerName is empty
                {
                     Debug.LogWarning($"{DEBUG_FLAG} Layer '{layer.gameObject.name}' has an empty LayerName property. It won't be accessible by name.", layer);
                }
            }
        }

        private void ValidateLayers()
        {
            // NOTE:
            // The UILayer's canvas will be get in its Awake function.
            // These steps must be called in Start (or later) to ensure UILayer.Awake has run.
            // Do not call them in UIRoot.Awake if UILayer.UICanvas is needed.
            
            // Create an array of validation checks
            System.Func<bool>[] checks = new System.Func<bool>[]
            {
                CheckLayerListValid, // Renamed, more general check of layerList itself
                CheckLayerNameValid, // Uses layerMap which is built from layerList
                CheckLayerOrderValid // This one heavily relies on UILayer.UICanvas
            };

            bool allValid = true;
            foreach (var check in checks)
            {
                if (!check())
                {
                    allValid = false;
                    // Do not return early; log all validation errors.
                }
            }

            if (allValid)
            {
                Debug.Log($"{DEBUG_FLAG} All UIRoot layer validations passed.", this);
            }
            else
            {
                Debug.LogError($"{DEBUG_FLAG} UIRoot layer validations failed. See previous errors.", this);
            }
        }

        bool CheckLayerListValid()
        {
            // Verify that the number of layers in layerList matches the number of child UILayer GameObjects (optional strict check)
            // For now, we just check if layerList itself has issues like duplicate entries if not handled by map.
            // The primary check here is that UILayers assigned in the list are indeed children of UIRoot.
            bool isValid = true;
            for(int i=0; i < layerList.Count; i++)
            {
                UILayer layer = layerList[i];
                if (layer == null)
                {
                    Debug.LogError($"{DEBUG_FLAG} layerList contains a null UILayer at index {i}.", this);
                    isValid = false;
                    continue;
                }
                if (layer.transform.parent != this.transform)
                {
                    Debug.LogError($"{DEBUG_FLAG} UILayer '{layer.LayerName}' in layerList is not a direct child of UIRoot.", layer);
                    isValid = false;
                }
            }
            // Check if child UILayer GameObjects are all in the layerList (optional strict check)
            // int childUILayerCount = 0;
            // foreach (Transform child in transform) { if (child.GetComponent<UILayer>() != null) childUILayerCount++; }
            // if (layerList.Count != childUILayerCount) { ... log error ... }
            return isValid;
        }

        bool CheckLayerNameValid()
        {
            // This was partially covered by InitializeLayerMap which logs duplicates.
            // Here we can reiterate or check for empty names if map initialization allowed them.
            bool isValid = true;
            HashSet<string> uniqueNames = new HashSet<string>();
            foreach (var layer in layerList)
            {
                if (layer == null) continue; // Already logged by CheckLayerListValid

                if (string.IsNullOrEmpty(layer.LayerName))
                {
                    Debug.LogError($"{DEBUG_FLAG} UILayer '{layer.gameObject.name}' has an empty or null LayerName.", layer);
                    isValid = false;
                }
                // Duplicate check is implicitly handled by dictionary add in InitializeLayerMap.
                // If we want to ensure all layers in list have unique names (even if map only takes first):
                else if (!uniqueNames.Add(layer.LayerName))
                {
                     // This error means layerMap might not contain all layers if names collide.
                     // The InitializeLayerMap already logs this for the map itself.
                     // This specific log confirms list has non-unique names which is problematic.
                     Debug.LogError($"{DEBUG_FLAG} Duplicate LayerName '{layer.LayerName}' detected in layerList. Ensure all names are unique.", layer);
                     isValid = false;
                }
            }
            return isValid;
        }

        bool CheckLayerOrderValid()
        {
            // Verify that layer orders are unique and canvas sorting is overridden
            bool isValid = true;
            HashSet<int> uniqueSortingOrders = new HashSet<int>();
            foreach (var layer in layerList)
            {
                if (layer == null) continue; 

                if (layer.UICanvas == null) // UICanvas is fetched in UILayer.Awake
                {
                    Debug.LogError($"{DEBUG_FLAG} UILayer '{layer.LayerName}' is missing its Canvas component reference (UICanvas is null). This might be an initialization order issue or a missing component.", layer);
                    isValid = false;
                    continue; // Skip further checks for this layer if canvas is null
                }
                
                if (!layer.UICanvas.overrideSorting)
                {
                    Debug.LogError($"{DEBUG_FLAG} UILayer '{layer.LayerName}' Canvas must have 'Override Sorting' enabled.", layer);
                    isValid = false;
                }
                
                if (!uniqueSortingOrders.Add(layer.UICanvas.sortingOrder))
                {
                    Debug.LogError($"{DEBUG_FLAG} Duplicate Canvas sortingOrder ({layer.UICanvas.sortingOrder}) found for UILayer '{layer.LayerName}'. Sorting orders must be unique.", layer);
                    isValid = false;
                }
            }
            return isValid;
        }

        /// <summary>
        /// Gets a UILayer by its registered name.
        /// </summary>
        /// <param name="layerNameKey">The name of the layer.</param>
        /// <returns>The UILayer if found, otherwise null.</returns>
        public UILayer GetUILayer(string layerNameKey)
        {
            if (string.IsNullOrEmpty(layerNameKey))
            {
                Debug.LogError($"{DEBUG_FLAG} Requested UILayer with null or empty name.", this);
                return null;
            }
            if (layerMap == null)
            {
                Debug.LogError($"{DEBUG_FLAG} LayerMap is not initialized. Cannot GetUILayer. This indicates an issue with Awake execution order or UIRoot setup.", this);
                return null; // Or try to rebuild map, though that suggests a deeper issue.
            }

            if (layerMap.TryGetValue(layerNameKey, out UILayer layer))
            {
                return layer;
            }
            
            Debug.LogWarning($"{DEBUG_FLAG} UILayer with name '{layerNameKey}' not found in UIRoot.", this);
            return null;
        }

        // This method is no longer strictly needed by UIManager if UIManager tracks active windows and their layers.
        /*
        public UILayer TryGetUILayerFromUIWindowName(string windowName)
        {
            if (string.IsNullOrEmpty(windowName)) return null;
            foreach (var layer in layerList) // Or iterate layerMap.Values for potentially faster access to layers
            {
                if (layer != null && layer.GetUIWindow(windowName) != null) // GetUIWindow is a search within the layer
                {
                    return layer;
                }
            }
            return null;
        }
        */
    }
}