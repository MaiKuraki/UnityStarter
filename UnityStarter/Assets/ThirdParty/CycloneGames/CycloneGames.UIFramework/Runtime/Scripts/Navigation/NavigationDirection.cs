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
}
