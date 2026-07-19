using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using MackySoft.Navigathena.SceneManagement;

namespace CycloneGames.GameplayFramework.Runtime.Integrations.Navigathena
{
    public enum NavigathenaSceneTransitionOperation : byte
    {
        Change = 0,
        Push = 1,
        Replace = 2
    }

    public delegate LoadSceneRequest NavigathenaLoadSceneRequestFactory(
        NavigathenaSceneTransitionOperation operation,
        string sceneName);

    public delegate PopSceneRequest NavigathenaPopSceneRequestFactory();

    /// <summary>
    /// Adapts Navigathena scene navigation to the GameplayFramework travel contract.
    /// </summary>
    public sealed class NavigathenaSceneTransitionHandler : ISceneTransitionHandler
    {
        private readonly ISceneNavigator navigator;
        private readonly NavigathenaLoadSceneRequestFactory loadRequestFactory;
        private readonly NavigathenaPopSceneRequestFactory popRequestFactory;

        /// <summary>
        /// Creates a handler that uses <see cref="BuiltInSceneIdentifier"/> and the
        /// navigator's configured default transition behavior.
        /// </summary>
        public NavigathenaSceneTransitionHandler(ISceneNavigator navigator)
            : this(navigator, CreateDefaultLoadRequest, CreateDefaultPopRequest)
        {
        }

        /// <summary>
        /// Creates a handler with explicit request factories. Factories can select
        /// identifiers, transition directors, scene data, and interrupt operations.
        /// </summary>
        public NavigathenaSceneTransitionHandler(
            ISceneNavigator navigator,
            NavigathenaLoadSceneRequestFactory loadRequestFactory,
            NavigathenaPopSceneRequestFactory popRequestFactory = null)
        {
            this.navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
            this.loadRequestFactory = loadRequestFactory ??
                throw new ArgumentNullException(nameof(loadRequestFactory));
            this.popRequestFactory = popRequestFactory ?? CreateDefaultPopRequest;
        }

        public UniTask ChangeScene(
            string sceneName,
            CancellationToken cancellationToken = default)
        {
            LoadSceneRequest request = CreateLoadRequest(
                NavigathenaSceneTransitionOperation.Change,
                sceneName);
            return navigator.Change(request, cancellationToken);
        }

        public UniTask PushScene(
            string sceneName,
            CancellationToken cancellationToken = default)
        {
            LoadSceneRequest request = CreateLoadRequest(
                NavigathenaSceneTransitionOperation.Push,
                sceneName);
            return navigator.Push(request, cancellationToken);
        }

        public UniTask PopScene(CancellationToken cancellationToken = default)
        {
            return navigator.Pop(popRequestFactory(), cancellationToken);
        }

        public UniTask ReplaceScene(
            string sceneName,
            CancellationToken cancellationToken = default)
        {
            LoadSceneRequest request = CreateLoadRequest(
                NavigathenaSceneTransitionOperation.Replace,
                sceneName);
            return navigator.Replace(request, cancellationToken);
        }

        private LoadSceneRequest CreateLoadRequest(
            NavigathenaSceneTransitionOperation operation,
            string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                throw new ArgumentException("Scene name is required.", nameof(sceneName));
            }

            LoadSceneRequest request = loadRequestFactory(operation, sceneName);
            if (request.Scene == null)
            {
                throw new InvalidOperationException(
                    "The Navigathena load request factory returned a request without a scene identifier.");
            }

            return request;
        }

        private static LoadSceneRequest CreateDefaultLoadRequest(
            NavigathenaSceneTransitionOperation operation,
            string sceneName)
        {
            return new LoadSceneRequest(
                new BuiltInSceneIdentifier(sceneName),
                transitionDirector: null,
                data: null,
                interruptOperation: null);
        }

        private static PopSceneRequest CreateDefaultPopRequest()
        {
            return new PopSceneRequest(
                overrideTransitionDirector: null,
                interruptOperation: null);
        }
    }
}
