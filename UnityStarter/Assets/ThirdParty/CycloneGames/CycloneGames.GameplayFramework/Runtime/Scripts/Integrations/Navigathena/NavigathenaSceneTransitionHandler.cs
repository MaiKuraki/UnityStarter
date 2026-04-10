#if NAVIGATHENA_PRESENT
using System.Threading;
using Cysharp.Threading.Tasks;
using MackySoft.Navigathena.SceneManagement;
using MackySoft.Navigathena.Transitions;
using CycloneGames.GameplayFramework.Runtime;

namespace CycloneGames.GameplayFramework.Runtime.Integrations.Navigathena
{
    /// <summary>
    /// Adapts Navigathena's ISceneNavigator to CycloneGames.GameplayFramework's ISceneTransitionHandler.
    ///
    /// Usage (at bootstrap):
    /// <code>
    /// var sceneNavigator = /* inject or locate your ISceneNavigator */;
    /// var handler = new NavigathenaSceneTransitionHandler(sceneNavigator);
    /// gameMode.SetSceneTransitionHandler(handler);
    ///
    /// // With a custom transition director (e.g., loading screen):
    /// var handler = new NavigathenaSceneTransitionHandler(sceneNavigator, myTransitionDirector);
    /// </code>
    ///
    /// Navigathena handles scene lifecycle (ISceneEntryPoint callbacks, history, transition animations).
    /// GameMode.TravelToLevel performs game-side cleanup then calls ChangeScene —
    /// the new scene's ISceneEntryPoint.OnInitialize bootstraps its own GameMode.
    /// </summary>
    public class NavigathenaSceneTransitionHandler : ISceneTransitionHandler
    {
        private readonly ISceneNavigator navigator;
        private readonly ITransitionDirector transitionDirector;

        /// <param name="navigator">The Navigathena scene navigator (typically injected via DI).</param>
        /// <param name="transitionDirector">
        /// Optional transition director for loading screen animations.
        /// Pass null to use Navigathena's empty (instant) transition.
        /// </param>
        public NavigathenaSceneTransitionHandler(ISceneNavigator navigator, ITransitionDirector transitionDirector = null)
        {
            this.navigator = navigator;
            this.transitionDirector = transitionDirector ?? TransitionDirector.Empty();
        }

        /// <summary>
        /// Navigate to the specified scene and reset navigation history.
        /// Typical use: level-to-level travel (stage 1 → stage 2).
        /// </summary>
        public UniTask ChangeScene(string sceneName, CancellationToken cancellationToken = default)
        {
            var identifier = new BuiltInSceneIdentifier(sceneName);
            var request = new LoadSceneRequest(identifier, transitionDirector, null, null);
            return navigator.Change(request, cancellationToken);
        }

        /// <summary>
        /// Navigate to the specified scene and push current scene onto history.
        /// Typical use: opening a menu scene over gameplay.
        /// </summary>
        public UniTask PushScene(string sceneName, CancellationToken cancellationToken = default)
        {
            var identifier = new BuiltInSceneIdentifier(sceneName);
            var request = new LoadSceneRequest(identifier, transitionDirector, null, null);
            return navigator.Push(request, cancellationToken);
        }

        /// <summary>
        /// Go back to the previous scene in history.
        /// </summary>
        public UniTask PopScene(CancellationToken cancellationToken = default)
        {
            var request = new PopSceneRequest(transitionDirector, null);
            return navigator.Pop(request, cancellationToken);
        }

        /// <summary>
        /// Replace current scene in history with the specified scene.
        /// </summary>
        public UniTask ReplaceScene(string sceneName, CancellationToken cancellationToken = default)
        {
            var identifier = new BuiltInSceneIdentifier(sceneName);
            var request = new LoadSceneRequest(identifier, transitionDirector, null, null);
            return navigator.Replace(request, cancellationToken);
        }
    }
}
#endif
