namespace CycloneGames.UIFramework.Runtime
{
    public readonly struct UILayerRuntimeStats
    {
        public UILayerRuntimeStats(string layerName, int sortingOrder, int windowCount)
        {
            LayerName = layerName;
            SortingOrder = sortingOrder;
            WindowCount = windowCount;
        }

        public string LayerName { get; }
        public int SortingOrder { get; }
        public int WindowCount { get; }
    }

    public readonly struct UIPerformanceStats
    {
        public UIPerformanceStats(
            int sessionCount,
            int openingWindowCount,
            int openWindowCount,
            int closingWindowCount,
            int sceneBoundWindowCount,
            int binderCount,
            int isolatedWindowCanvasCount,
            int layerCount,
            int maxWindowCapacity)
        {
            SessionCount = sessionCount;
            OpeningWindowCount = openingWindowCount;
            OpenWindowCount = openWindowCount;
            ClosingWindowCount = closingWindowCount;
            SceneBoundWindowCount = sceneBoundWindowCount;
            BinderCount = binderCount;
            IsolatedWindowCanvasCount = isolatedWindowCanvasCount;
            LayerCount = layerCount;
            MaxWindowCapacity = maxWindowCapacity;
        }

        public int SessionCount { get; }
        public int OpeningWindowCount { get; }
        public int OpenWindowCount { get; }
        public int ClosingWindowCount { get; }
        public int SceneBoundWindowCount { get; }
        public int BinderCount { get; }
        public int IsolatedWindowCanvasCount { get; }
        public int LayerCount { get; }
        public int MaxWindowCapacity { get; }
    }
}
