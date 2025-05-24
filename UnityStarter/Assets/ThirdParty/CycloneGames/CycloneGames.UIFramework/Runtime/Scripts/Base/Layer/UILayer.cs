using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CycloneGames.UIFramework
{
    [RequireComponent(typeof(Canvas))]
    [RequireComponent(typeof(GraphicRaycaster))]
    public class UILayer : MonoBehaviour
    {
        private const string DEBUG_FLAG = "[UILayer]";
        [SerializeField] private string layerName;

        [Tooltip("The amount of window to expand when the window array is full")]
        [SerializeField] private int expansionAmount = 3;

        private Canvas uiCanvas;
        public Canvas UICanvas => uiCanvas;
        private GraphicRaycaster graphicRaycaster;
        public GraphicRaycaster WindowGraphicRaycaster => graphicRaycaster;
        public string LayerName => layerName;
        private UIWindow[] uiWindowArray; // Internal array of managed windows
        public UIWindow[] UIWindowArray => uiWindowArray; // Public accessor for editor or specific cases
        public int WindowCount { get; private set; }
        public bool IsFinishedLayerInit { get; private set; }

        // Static comparer to avoid delegate allocation on each sort if Comparer.Create was an issue.
        private static readonly IComparer<UIWindow> _priorityComparer = Comparer<UIWindow>.Create((a, b) =>
        {
            if (a == null && b == null) return 0;
            if (a == null) return 1; // Nulls go to the end
            if (b == null) return -1;
            return a.Priority.CompareTo(b.Priority);
        });
        
        protected void Awake()
        {
            uiCanvas = GetComponent<Canvas>();
            graphicRaycaster = GetComponent<GraphicRaycaster>();
            // TODO: maybe your UI layer is not named 'UI'
            WindowGraphicRaycaster.blockingMask = LayerMask.GetMask("UI");
            InitLayer();
        }

        private void InitLayer()
        {
            if (transform.childCount == 0)
            {
                uiWindowArray = new UIWindow[expansionAmount > 0 ? expansionAmount : 1]; // Initialize with some capacity
                WindowCount = 0;
                IsFinishedLayerInit = true;
                // Debug.Log($"{DEBUG_FLAG} Finished init Layer: {LayerName}, no initial children.");
                return;
            }

            // GetComponentsInChildren also includes self if component is on self, ensure it's intended.
            // It also allocates a new array.
            var tempWindowArrayFromChildren = GetComponentsInChildren<UIWindow>(false); // 'false' to not include self
            
            // Filter out windows that are not direct children if UILayer should only manage direct children
            List<UIWindow> directChildrenWindows = new List<UIWindow>();
            foreach (var window in tempWindowArrayFromChildren)
            {
                if (window.transform.parent == this.transform)
                {
                    directChildrenWindows.Add(window);
                }
            }

            uiWindowArray = new UIWindow[Mathf.Max(directChildrenWindows.Count, expansionAmount > 0 ? expansionAmount : 1)];
            WindowCount = 0;

            foreach (UIWindow window in directChildrenWindows)
            {
                window.SetWindowName(window.gameObject.name); // Or a more robust naming scheme
                window.SetUILayer(this);
                // Directly add to array without sorting yet, will sort once all are added
                uiWindowArray[WindowCount++] = window; 
            }

            SortUIWindowByPriority(); // This also sets sibling index
            IsFinishedLayerInit = true;
            Debug.Log($"{DEBUG_FLAG} Finished init Layer: {LayerName}, found {WindowCount} initial windows.");
        }

        public UIWindow GetUIWindow(string InWindowName)
        {
            if (string.IsNullOrEmpty(InWindowName) || uiWindowArray == null) return null;
            for (int i = 0; i < WindowCount; i++)
            {
                if (uiWindowArray[i] != null && uiWindowArray[i].WindowName == InWindowName)
                {
                    return uiWindowArray[i];
                }
            }
            return null;
        }

        public bool HasWindow(string InWindowName)
        {
            if (string.IsNullOrEmpty(InWindowName) || uiWindowArray == null) return false;
            for (int i = 0; i < WindowCount; i++)
            {
                // Using OrdinalIgnoreCase for case-insensitive comparison without new string allocations (like ToLower)
                if (uiWindowArray[i] != null && uiWindowArray[i].WindowName.Equals(InWindowName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public void AddWindow(UIWindow newWindow)
        {
            if (!IsFinishedLayerInit)
            {
                Debug.LogError($"{DEBUG_FLAG} Layer not initialized, cannot add window. Current layer: {LayerName}");
                return;
            }
            if (newWindow == null)
            {
                Debug.LogError($"{DEBUG_FLAG} Cannot add a null window to layer: {LayerName}");
                return;
            }
            if (HasWindow(newWindow.WindowName)) // Check if a window with the same name already exists
            {
                Debug.LogError($"{DEBUG_FLAG} Window already exists: {newWindow.WindowName} in layer: {LayerName}");
                return;
            }

            newWindow.gameObject.name = newWindow.WindowName; // Ensure GameObject name matches
            newWindow.SetUILayer(this);
            newWindow.transform.SetParent(transform, false); // Set parent

            // Resize array if full
            if (uiWindowArray == null || WindowCount == uiWindowArray.Length)
            {
                int newSize = (uiWindowArray?.Length ?? 0) + (expansionAmount > 0 ? expansionAmount : 1);
                // Note: Array.Resize creates a new array and copies, causing GC for the old array.
                System.Array.Resize(ref uiWindowArray, newSize);
                Debug.Log($"{DEBUG_FLAG} Resized UIWindowArray for layer {LayerName} to {newSize}");
            }

            // Add window and then re-sort. Simpler than finding insert index if additions are not extremely frequent.
            uiWindowArray[WindowCount++] = newWindow;
            SortUIWindowByPriority(); // This will place the new window correctly and update sibling indices.
        }

        // This method is called by UIWindow.OnDestroy to notify the layer
        public void NotifyWindowDestroyed(UIWindow window)
        {
            if (window == null || uiWindowArray == null) return;

            int windowIndex = -1;
            for (int i = 0; i < WindowCount; i++)
            {
                if (uiWindowArray[i] == window)
                {
                    windowIndex = i;
                    break;
                }
            }

            if (windowIndex != -1)
            {
                // Shift elements to fill the gap
                for (int j = windowIndex; j < WindowCount - 1; j++)
                {
                    uiWindowArray[j] = uiWindowArray[j + 1];
                }
                uiWindowArray[WindowCount - 1] = null; // Clear the last valid spot
                WindowCount--;
                Debug.Log($"{DEBUG_FLAG} Window {window.WindowName} removed from layer {LayerName}. New count: {WindowCount}");
            }
        }
        
        // This method now only initiates the closing of the window.
        // The actual removal from the array happens in NotifyWindowDestroyed when the window's OnDestroy is called.
        public void RemoveWindow(string InWindowName)
        {
            if (!IsFinishedLayerInit)
            {
                Debug.LogError($"{DEBUG_FLAG} Layer not initialized, cannot remove window. Current layer: {LayerName}");
                return;
            }

            UIWindow windowToClose = GetUIWindow(InWindowName);
            if (windowToClose != null)
            {
                windowToClose.Close(); // Tell the window to close itself.
                                       // It will call Destroy(gameObject) and its OnDestroy will trigger NotifyWindowDestroyed.
            }
            else
            {
                Debug.LogWarning($"{DEBUG_FLAG} Window not found to remove: {InWindowName} in layer: {LayerName}");
            }
        }

        private void SortUIWindowByPriority()
        {
            if (uiWindowArray == null || WindowCount <= 1) return;
            
            // Sort the populated part of the array
            System.Array.Sort(uiWindowArray, 0, WindowCount, _priorityComparer);

            // Update GameObject sibling index based on sorted order for rendering
            for (int i = 0; i < WindowCount; i++)
            {
                if (uiWindowArray[i] != null && uiWindowArray[i].transform.parent == this.transform)
                {
                     uiWindowArray[i].transform.SetSiblingIndex(i);
                }
            }
        }

        // Called when the UILayer GameObject itself is being destroyed
        public void OnDestroy()
        {
            if (uiWindowArray != null)
            {
                for (int i = 0; i < WindowCount; i++)
                {
                    if (uiWindowArray[i] != null)
                    {
                        // Inform windows that their parent layer is gone.
                        // This prevents them from trying to call NotifyWindowDestroyed on a destroyed layer.
                        uiWindowArray[i].SetUILayer(null);
                        // Optionally, explicitly destroy windows if the layer's destruction means they should also be gone
                        // if they weren't already handled by their own logic or UIManager.
                        // However, UIManager should typically handle the lifecycle of windows it creates.
                    }
                }
            }
            uiWindowArray = null; // Release the array reference
            WindowCount = 0;
            IsFinishedLayerInit = false; // Mark as no longer initialized
            Debug.Log($"{DEBUG_FLAG} Layer {LayerName} is being destroyed.");
        }
    }
}