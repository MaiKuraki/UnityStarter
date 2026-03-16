namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>
    /// Semantic direction of a navigation transition.
    /// Used by IUITransitionCoordinator implementations to choose the correct animation.
    /// </summary>
    public enum NavigationDirection
    {
        /// <summary>Navigating deeper into a flow (e.g., list → detail). Enter from right, leave to left.</summary>
        Forward,
        /// <summary>Going back in a flow (e.g., back button). Enter from left, leave to right.</summary>
        Backward,
        /// <summary>Replacing the current window with no directional bias (e.g., cross-fade).</summary>
        Replace,
    }

    /// <summary>
    /// Controls how rapid sequential coordinated navigations are handled.
    /// </summary>
    public enum CoordinatedNavStrategy
    {
        /// <summary>
        /// Skip intermediate pages and jump directly from the original source to the final
        /// destination. E.g. A→B mid-animation + B→C request = cancel B, animate A→C directly.
        /// Best for tab-bars, flat navigation, or any UI where intermediate pages are not meaningful.
        /// </summary>
        DirectJump,

        /// <summary>
        /// Allow multiple coordinated transitions to overlap in a card-stack fashion.
        /// E.g. A→B mid-animation + B→C request = B→C starts immediately while A→B continues,
        /// producing a cascading stack visual. Each transition runs independently on its own pair.
        /// Best for drill-down flows (settings, detail pages) where a "stacking cards" feel is desired.
        /// </summary>
        CardStack,
    }
}
