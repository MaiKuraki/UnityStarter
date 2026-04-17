using System.Collections.Generic;

namespace CycloneGames.UIFramework.Runtime
{
    public readonly struct UILayerRuntimeStats
    {
        public readonly string LayerName;
        public readonly int SortingOrder;
        public readonly int WindowCount;

        public UILayerRuntimeStats(string layerName, int sortingOrder, int windowCount)
        {
            LayerName = layerName;
            SortingOrder = sortingOrder;
            WindowCount = windowCount;
        }
    }

    public readonly struct UIPerformanceStats
    {
        public readonly int ActiveWindowCount;
        public readonly int SceneBoundWindowCount;
        public readonly int InFlightOpenCount;
        public readonly int CachedConfigHandleCount;
        public readonly int CachedPrefabHandleCount;
        public readonly int LayerCount;
        public readonly int TotalLayerWindowCount;
        public readonly int IsolatedWindowCanvasCount;
        public readonly bool HasPendingSceneSweep;

        public UIPerformanceStats(
            int activeWindowCount,
            int sceneBoundWindowCount,
            int inFlightOpenCount,
            int cachedConfigHandleCount,
            int cachedPrefabHandleCount,
            int layerCount,
            int totalLayerWindowCount,
            int isolatedWindowCanvasCount,
            bool hasPendingSceneSweep)
        {
            ActiveWindowCount = activeWindowCount;
            SceneBoundWindowCount = sceneBoundWindowCount;
            InFlightOpenCount = inFlightOpenCount;
            CachedConfigHandleCount = cachedConfigHandleCount;
            CachedPrefabHandleCount = cachedPrefabHandleCount;
            LayerCount = layerCount;
            TotalLayerWindowCount = totalLayerWindowCount;
            IsolatedWindowCanvasCount = isolatedWindowCanvasCount;
            HasPendingSceneSweep = hasPendingSceneSweep;
        }
    }
}
