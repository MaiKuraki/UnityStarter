using System.Threading;
using Cysharp.Threading.Tasks;

namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>
    /// Abstraction for driving window open/close transitions (e.g., DOTween, Animator).
    /// Implementations should avoid GC allocations in hot paths.
    /// </summary>
    public interface IUIWindowTransitionDriver
    {
        UniTask PlayOpenAsync(UIWindow window, CancellationToken ct);
        UniTask PlayCloseAsync(UIWindow window, CancellationToken ct);
    }
}



