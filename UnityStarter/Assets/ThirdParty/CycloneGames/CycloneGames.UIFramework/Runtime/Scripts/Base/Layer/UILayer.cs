using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CycloneGames.UIFramework.Runtime
{
    [RequireComponent(typeof(Canvas))]
    [RequireComponent(typeof(GraphicRaycaster))]
    public class UILayer : MonoBehaviour
    {
        private const string DEBUG_FLAG = "[UILayer]";
        [SerializeField] private string layerName;

        [Tooltip("The amount of window to expand when the window array is full")]
        [SerializeField] private int expansionAmount = 5;

        private Canvas uiCanvas;
        public Canvas UICanvas => uiCanvas;
        private GraphicRaycaster graphicRaycaster;
        public GraphicRaycaster WindowGraphicRaycaster => graphicRaycaster;
        public string LayerName => layerName;

        private UIWindow[] uiWindowArray; // Internal array of managed windows
        public UIWindow[] UIWindowArray => uiWindowArray; // Public accessor for editor or specific cases
        public int WindowCount { get; private set; }
        public bool IsFinishedLayerInit { get; private set; }
        private static readonly List<UIWindow> _tempWindowList = new List<UIWindow>(64);

        // comparer
        private static readonly IComparer<UIWindow> _priorityComparer = Comparer<UIWindow>.Create((a, b) =>
        {
            if (ReferenceEquals(a, b)) return 0;
            if (ReferenceEquals(a, null)) return 1;
            if (ReferenceEquals(b, null)) return -1;
            return a.Priority.CompareTo(b.Priority);
        });

        protected void Awake()
        {
            uiCanvas = GetComponent<Canvas>();
            graphicRaycaster = GetComponent<GraphicRaycaster>();
            // TODO: maybe your UI layer is not named 'UI'
            if (WindowGraphicRaycaster != null)
            {
                WindowGraphicRaycaster.blockingMask = LayerMask.GetMask("UI");
            }
            InitLayer();
        }

        private void InitLayer()
        {
            if (transform.childCount == 0)
            {
                uiWindowArray = new UIWindow[expansionAmount > 0 ? expansionAmount : 4];
                WindowCount = 0;
                IsFinishedLayerInit = true;
                return;
            }

            // Optimization: Use GetComponentsInChildren with a reusable List to avoid array allocation
            _tempWindowList.Clear();
            GetComponentsInChildren(false, _tempWindowList);

            int validCount = 0;
            for (int i = 0; i < _tempWindowList.Count; i++)
            {
                if (_tempWindowList[i].transform.parent == this.transform)
                {
                    validCount++;
                }
                else
                {
                    _tempWindowList[i] = null;
                }
            }

            int initialCapacity = Mathf.Max(validCount, expansionAmount > 0 ? expansionAmount : 4);
            uiWindowArray = new UIWindow[initialCapacity];
            WindowCount = 0;

            for (int i = 0; i < _tempWindowList.Count; i++)
            {
                var window = _tempWindowList[i];
                if (window != null)
                {
                    window.SetWindowName(window.gameObject.name);
                    window.SetUILayer(this);
                    uiWindowArray[WindowCount++] = window;
                }
            }

            _tempWindowList.Clear();

            SortUIWindowByPriority();
            IsFinishedLayerInit = true;
            Debug.Log($"{DEBUG_FLAG} Finished init Layer: {LayerName}, found {WindowCount} initial windows.");
        }

        public UIWindow GetUIWindow(string InWindowName)
        {
            if (string.IsNullOrEmpty(InWindowName) || uiWindowArray == null) return null;

            for (int i = 0; i < WindowCount; i++)
            {
                var w = uiWindowArray[i];
                if (w != null && string.Equals(w.WindowName, InWindowName, System.StringComparison.Ordinal))
                {
                    return w;
                }
            }
            return null;
        }

        public bool HasWindow(string InWindowName)
        {
            return GetUIWindow(InWindowName) != null;
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

            if (HasWindow(newWindow.WindowName))
            {
                Debug.LogError($"{DEBUG_FLAG} Window already exists: {newWindow.WindowName} in layer: {LayerName}");
                return;
            }

            newWindow.gameObject.name = newWindow.WindowName;
            newWindow.SetUILayer(this);
            newWindow.transform.SetParent(transform, false);

            //  expand
            if (uiWindowArray == null || WindowCount == uiWindowArray.Length)
            {
                int newSize = (uiWindowArray?.Length ?? 0) + (expansionAmount > 0 ? expansionAmount : 4);
                var newArray = new UIWindow[newSize];
                if (uiWindowArray != null)
                {
                    System.Array.Copy(uiWindowArray, newArray, uiWindowArray.Length);
                }
                uiWindowArray = newArray;
                // Debug.Log($"{DEBUG_FLAG} Resized UIWindowArray for layer {LayerName} to {newSize}");
            }

            uiWindowArray[WindowCount++] = newWindow;
            SortUIWindowByPriority();
        }

        public void NotifyWindowDestroyed(UIWindow window)
        {
            if (window == null || uiWindowArray == null) return;

            int windowIndex = -1;
            for (int i = 0; i < WindowCount; i++)
            {
                if (ReferenceEquals(uiWindowArray[i], window))
                {
                    windowIndex = i;
                    break;
                }
            }

            if (windowIndex != -1)
            {
                if (windowIndex < WindowCount - 1)
                {
                    System.Array.Copy(uiWindowArray, windowIndex + 1, uiWindowArray, windowIndex, WindowCount - windowIndex - 1);
                }

                WindowCount--;
                uiWindowArray[WindowCount] = null; // Avoid object reference leak

                // Debug.Log($"{DEBUG_FLAG} Window {window.WindowName} removed from layer {LayerName}. New count: {WindowCount}");
            }
        }

        public void RemoveWindow(string InWindowName)
        {
            if (!IsFinishedLayerInit) return;

            UIWindow windowToClose = GetUIWindow(InWindowName);
            if (windowToClose != null)
            {
                windowToClose.Close();
            }
        }

        private void SortUIWindowByPriority()
        {
            if (uiWindowArray == null || WindowCount <= 1) return;

            System.Array.Sort(uiWindowArray, 0, WindowCount, _priorityComparer);

            for (int i = 0; i < WindowCount; i++)
            {
                var w = uiWindowArray[i];
                if (w != null && w.transform.parent == this.transform)
                {
                    w.transform.SetSiblingIndex(i);
                }
            }
        }

        public void OnDestroy()
        {
            if (uiWindowArray != null)
            {
                for (int i = 0; i < WindowCount; i++)
                {
                    var w = uiWindowArray[i];
                    if (w != null)
                    {
                        w.SetUILayer(null);
                    }
                }
            }
            uiWindowArray = null;
            WindowCount = 0;
            IsFinishedLayerInit = false;
        }
    }
}