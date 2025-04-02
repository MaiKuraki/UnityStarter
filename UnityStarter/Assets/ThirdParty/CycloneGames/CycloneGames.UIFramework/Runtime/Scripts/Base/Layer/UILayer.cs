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

        [Tooltip("The amount of pages to expand when the page array is full")]
        [SerializeField] private int expansionAmount = 3;

        private Canvas uiCanvas;
        public Canvas UICanvas => uiCanvas;
        private GraphicRaycaster graphicRaycaster;
        public GraphicRaycaster PageGraphicRaycaster => graphicRaycaster;
        public string LayerName => layerName;
        private UIPage[] uiPagesArray;
        public UIPage[] UIPageArray => uiPagesArray;
        public int PageCount { get; private set; }
        public bool IsFinishedLayerInit { get; private set; }

        protected void Awake()
        {
            uiCanvas = GetComponent<Canvas>();
            graphicRaycaster = GetComponent<GraphicRaycaster>();
            PageGraphicRaycaster.blockingMask = LayerMask.GetMask("UI");
            InitLayer();
        }

        private void InitLayer()
        {
            if (transform.childCount == 0)
            {
                IsFinishedLayerInit = true;
                Debug.Log($"{DEBUG_FLAG} Finished init Layer: {LayerName}");
                return;
            }

            var tempPages = GetComponentsInChildren<UIPage>();
            uiPagesArray = new UIPage[tempPages.Length];
            PageCount = 0;

            foreach (UIPage page in tempPages)
            {
                page.SetPageName(page.gameObject.name);
                page.SetUILayer(this);
                uiPagesArray[PageCount++] = page;
            }

            SortPagesByPriority();
            IsFinishedLayerInit = true;
            Debug.Log($"{DEBUG_FLAG} Finished init Layer: {LayerName}");
        }

        public UIPage GetUIPage(string InPageName)
        {
            if (string.IsNullOrEmpty(InPageName)) return null;
            for (int i = 0; i < PageCount; i++)
            {
                if (uiPagesArray[i].PageName == InPageName)
                {
                    return uiPagesArray[i];
                }
            }
            return null;
        }

        public bool HasPage(string InPageName)
        {
            for (int i = 0; i < PageCount; i++)
            {
                if (uiPagesArray[i].PageName.Equals(InPageName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public void AddPage(UIPage newPage)
        {
            if (!IsFinishedLayerInit)
            {
                Debug.LogError($"{DEBUG_FLAG} layer not init, current layer: {LayerName}");
                return;
            }

            for (int i = 0; i < PageCount; i++)
            {
                if (uiPagesArray[i].PageName == newPage.PageName)
                {
                    Debug.LogError($"{DEBUG_FLAG} Page already exists: {newPage.PageName}");
                    return;
                }
            }

            newPage.gameObject.name = newPage.PageName;
            newPage.SetUILayer(this);
            newPage.transform.SetParent(transform, false);

            if (PageCount == (uiPagesArray?.Length ?? 0))
            {
                int newSize = (uiPagesArray?.Length ?? 0) + expansionAmount;
                System.Array.Resize(ref uiPagesArray, newSize);
            }

            int insertIndex = PageCount;
            for (int i = PageCount - 1; i >= 0; i--)
            {
                if (uiPagesArray[i].Priority > newPage.Priority)
                {
                    insertIndex = i;
                }
                else if (uiPagesArray[i].Priority == newPage.Priority)
                {
                    insertIndex = i + 1;
                    break;
                }
            }

            for (int i = PageCount; i > insertIndex; i--)
            {
                uiPagesArray[i] = uiPagesArray[i - 1];
            }

            uiPagesArray[insertIndex] = newPage;
            PageCount++;

            for (int i = insertIndex; i < PageCount; i++)
            {
                uiPagesArray[i].transform.SetSiblingIndex(i);
            }
        }

        public void RemovePage(string InPageName)
        {
            if (!IsFinishedLayerInit)
            {
                Debug.LogError($"{DEBUG_FLAG} layer not init, current layer: {LayerName}");
                return;
            }

            for (int i = 0; i < PageCount; i++)
            {
                if (uiPagesArray[i].PageName == InPageName)
                {
                    UIPage page = uiPagesArray[i];
                    for (int j = i; j < PageCount - 1; j++)
                    {
                        uiPagesArray[j] = uiPagesArray[j + 1];
                    }
                    PageCount--;

                    page.ClosePage();
                    page.SetUILayer(null);
                    return;
                }
            }
        }

        private void SortPagesByPriority()
        {
            if (PageCount <= 1) return;
            System.Array.Sort(uiPagesArray, 0, PageCount, Comparer<UIPage>.Create((a, b) => a.Priority.CompareTo(b.Priority)));

            for (int i = 0; i < PageCount; i++)
            {
                uiPagesArray[i].transform.SetSiblingIndex(i);
            }
        }

        public void OnDestroy()
        {
            for (int i = 0; i < PageCount; i++)
            {
                if (uiPagesArray[i] != null)
                {
                    uiPagesArray[i].SetUILayer(null);
                }
            }
            PageCount = 0;
        }
    }
}