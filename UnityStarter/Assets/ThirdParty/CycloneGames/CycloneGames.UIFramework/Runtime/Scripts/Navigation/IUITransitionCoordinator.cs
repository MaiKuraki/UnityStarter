using System.Threading;
using Cysharp.Threading.Tasks;

namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>
    /// Orchestrates a coordinated visual transition between two UIWindows.
    ///
    /// Both windows are already alive in the hierarchy when this is called.
    /// The coordinator is solely responsible for driving all animations — the
    /// caller will close/destroy 'leaving' after this task completes.
    ///
    /// To implement a custom transition (fade, zoom, cross-dissolve, etc.),
    /// create a class that implements this interface and pass it to
    /// IUIService.SetTransitionCoordinator().
    /// </summary>
    public interface IUITransitionCoordinator
    {
        /// <summary>
        /// Plays a coordinated transition between two windows simultaneously.
        /// </summary>
        /// <param name="leaving">The window that is exiting the screen.</param>
        /// <param name="entering">The window that is entering the screen. It has been opened silently (no animation yet).</param>
        /// <param name="direction">Semantic direction of the navigation — Forward, Backward, or Replace.</param>
        /// <param name="ct">Cancellation token; implementations must respect it and stop promptly when triggered.</param>
        UniTask TransitionAsync(UIWindow leaving, UIWindow entering, NavigationDirection direction, CancellationToken ct = default);
    }
}
