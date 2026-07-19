using System.Threading;
using Cysharp.Threading.Tasks;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Scene-navigation boundary used by gameplay code. Implementations own scene history,
    /// loading, activation, and failure handling. A destination scene composes its own World.
    /// </summary>
    public interface ISceneTransitionHandler
    {
        /// <summary>
        /// Navigate to a scene and reset navigation history.
        /// </summary>
        UniTask ChangeScene(string sceneName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Navigate to a scene while retaining the current scene in history.
        /// </summary>
        UniTask PushScene(string sceneName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Return to the previous scene in history.
        /// </summary>
        UniTask PopScene(CancellationToken cancellationToken = default);

        /// <summary>
        /// Replace the current history entry without adding another entry.
        /// </summary>
        UniTask ReplaceScene(string sceneName, CancellationToken cancellationToken = default);
    }
}
