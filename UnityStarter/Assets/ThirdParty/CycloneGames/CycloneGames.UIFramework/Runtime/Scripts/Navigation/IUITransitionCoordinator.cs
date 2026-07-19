using System.Threading;
using Cysharp.Threading.Tasks;

namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>
    /// Drives one visual transition between two live windows.
    /// The caller serializes operations, owns both window lifetimes, and invokes this contract
    /// on the Unity main thread. Implementations must restore modified visual and input state
    /// before propagating cancellation.
    /// </summary>
    public interface IUITransitionCoordinator
    {
        UniTask TransitionAsync(
            UIWindow leaving,
            UIWindow entering,
            NavigationDirection direction,
            CancellationToken cancellationToken = default);
    }
}
