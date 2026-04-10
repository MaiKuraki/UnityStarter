using Cysharp.Threading.Tasks;
using System.Threading;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Contract for scene navigation.
    /// Implementations manage scene history and transitions, matching the navigation model
    /// used by systems like Navigathena (Push/Pop/Change/Replace).
    /// 
    /// GameMode calls these methods to trigger level travel — it does NOT call
    /// LaunchGameModeAsync after navigation. The new scene's entry point is responsible
    /// for bootstrapping its own GameMode.
    /// </summary>
    public interface ISceneTransitionHandler
    {
        /// <summary>
        /// Navigate to the specified scene, resetting the navigation history.
        /// Use for top-level level transitions (e.g., main menu → gameplay, stage 1 → stage 2).
        /// Maps to Navigathena's ISceneNavigator.Change().
        /// </summary>
        UniTask ChangeScene(string sceneName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Navigate to the specified scene while keeping the current scene in history.
        /// Use when the player should be able to return (e.g., opening a pause menu scene).
        /// Maps to Navigathena's ISceneNavigator.Push().
        /// </summary>
        UniTask PushScene(string sceneName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Return to the previous scene in history.
        /// Maps to Navigathena's ISceneNavigator.Pop().
        /// </summary>
        UniTask PopScene(CancellationToken cancellationToken = default);

        /// <summary>
        /// Replace the current scene in the navigation history with the specified scene.
        /// Use when you want to swap the current scene without adding to history.
        /// Maps to Navigathena's ISceneNavigator.Replace().
        /// </summary>
        UniTask ReplaceScene(string sceneName, CancellationToken cancellationToken = default);
    }
}
