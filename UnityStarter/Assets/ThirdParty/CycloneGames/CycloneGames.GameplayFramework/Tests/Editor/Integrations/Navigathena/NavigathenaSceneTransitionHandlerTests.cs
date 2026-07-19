using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MackySoft.Navigathena.SceneManagement;
using NUnit.Framework;

namespace CycloneGames.GameplayFramework.Runtime.Integrations.Navigathena.Tests
{
    public sealed class NavigathenaSceneTransitionHandlerTests
    {
        [Test]
        public void ConstructorRejectsNullNavigator()
        {
            Assert.Throws<ArgumentNullException>(
                () => new NavigathenaSceneTransitionHandler(null));
        }

        [Test]
        public void ConstructorRejectsNullLoadRequestFactory()
        {
            Assert.Throws<ArgumentNullException>(
                () => new NavigathenaSceneTransitionHandler(
                    new RecordingSceneNavigator(),
                    loadRequestFactory: null));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void LoadOperationsRejectMissingSceneName(string sceneName)
        {
            var navigator = new RecordingSceneNavigator();
            var handler = new NavigathenaSceneTransitionHandler(navigator);

            Assert.Throws<ArgumentException>(
                () => handler.ChangeScene(sceneName).GetAwaiter().GetResult());
            Assert.Throws<ArgumentException>(
                () => handler.PushScene(sceneName).GetAwaiter().GetResult());
            Assert.Throws<ArgumentException>(
                () => handler.ReplaceScene(sceneName).GetAwaiter().GetResult());
        }

        [Test]
        public void DefaultRequestsMapEveryOperationAndCancellationToken()
        {
            var navigator = new RecordingSceneNavigator();
            var handler = new NavigathenaSceneTransitionHandler(navigator);
            using var cancellationSource = new CancellationTokenSource();

            handler.ChangeScene("Stage01", cancellationSource.Token)
                .GetAwaiter()
                .GetResult();
            AssertRecordedLoad(
                navigator,
                RecordedOperation.Change,
                "Stage01 (BuiltInSceneIdentifier)",
                cancellationSource.Token);

            handler.PushScene("Pause", cancellationSource.Token)
                .GetAwaiter()
                .GetResult();
            AssertRecordedLoad(
                navigator,
                RecordedOperation.Push,
                "Pause (BuiltInSceneIdentifier)",
                cancellationSource.Token);

            handler.ReplaceScene("Results", cancellationSource.Token)
                .GetAwaiter()
                .GetResult();
            AssertRecordedLoad(
                navigator,
                RecordedOperation.Replace,
                "Results (BuiltInSceneIdentifier)",
                cancellationSource.Token);

            handler.PopScene(cancellationSource.Token)
                .GetAwaiter()
                .GetResult();
            Assert.That(navigator.Operation, Is.EqualTo(RecordedOperation.Pop));
            Assert.That(navigator.CancellationToken, Is.EqualTo(cancellationSource.Token));
        }

        [Test]
        public void CustomFactoriesReceiveOperationAndPreserveRequestData()
        {
            var navigator = new RecordingSceneNavigator();
            var sceneIdentifier = new BuiltInSceneIdentifier("CustomStage");
            var sceneData = new TestSceneData();
            var requestedOperation = NavigathenaSceneTransitionOperation.Change;
            string requestedName = null;

            LoadSceneRequest LoadFactory(
                NavigathenaSceneTransitionOperation operation,
                string sceneName)
            {
                requestedOperation = operation;
                requestedName = sceneName;
                return new LoadSceneRequest(
                    sceneIdentifier,
                    transitionDirector: null,
                    data: sceneData,
                    interruptOperation: null);
            }

            var handler = new NavigathenaSceneTransitionHandler(
                navigator,
                LoadFactory,
                () => new PopSceneRequest(null, null));

            handler.PushScene("RouteKey")
                .GetAwaiter()
                .GetResult();

            Assert.That(
                requestedOperation,
                Is.EqualTo(NavigathenaSceneTransitionOperation.Push));
            Assert.That(requestedName, Is.EqualTo("RouteKey"));
            Assert.That(navigator.LoadRequest.Scene, Is.SameAs(sceneIdentifier));
            Assert.That(navigator.LoadRequest.Data, Is.SameAs(sceneData));
        }

        [Test]
        public void FactoryRequestWithoutSceneIdentifierIsRejected()
        {
            var handler = new NavigathenaSceneTransitionHandler(
                new RecordingSceneNavigator(),
                (operation, sceneName) => default);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => handler.ChangeScene("Stage01").GetAwaiter().GetResult());

            StringAssert.Contains("without a scene identifier", exception.Message);
        }

        private static void AssertRecordedLoad(
            RecordingSceneNavigator navigator,
            RecordedOperation operation,
            string sceneDescription,
            CancellationToken cancellationToken)
        {
            Assert.That(navigator.Operation, Is.EqualTo(operation));
            Assert.That(navigator.LoadRequest.Scene.ToString(), Is.EqualTo(sceneDescription));
            Assert.That(navigator.LoadRequest.TransitionDirector, Is.Null);
            Assert.That(navigator.LoadRequest.Data, Is.Null);
            Assert.That(navigator.LoadRequest.InterruptOperation, Is.Null);
            Assert.That(navigator.CancellationToken, Is.EqualTo(cancellationToken));
        }

        private sealed class TestSceneData : ISceneData
        {
        }

        private enum RecordedOperation : byte
        {
            None = 0,
            Change = 1,
            Push = 2,
            Pop = 3,
            Replace = 4
        }

        private sealed class RecordingSceneNavigator : ISceneNavigator
        {
            public IReadOnlyCollection<IReadOnlySceneHistoryEntry> History =>
                Array.Empty<IReadOnlySceneHistoryEntry>();

            public RecordedOperation Operation { get; private set; }
            public LoadSceneRequest LoadRequest { get; private set; }
            public PopSceneRequest PopRequest { get; private set; }
            public CancellationToken CancellationToken { get; private set; }

            public UniTask Initialize()
            {
                return UniTask.CompletedTask;
            }

            public UniTask Push(
                LoadSceneRequest request,
                CancellationToken cancellationToken = default)
            {
                Record(RecordedOperation.Push, request, cancellationToken);
                return UniTask.CompletedTask;
            }

            public UniTask Pop(
                PopSceneRequest request,
                CancellationToken cancellationToken = default)
            {
                Operation = RecordedOperation.Pop;
                PopRequest = request;
                CancellationToken = cancellationToken;
                return UniTask.CompletedTask;
            }

            public UniTask Change(
                LoadSceneRequest request,
                CancellationToken cancellationToken = default)
            {
                Record(RecordedOperation.Change, request, cancellationToken);
                return UniTask.CompletedTask;
            }

            public UniTask Replace(
                LoadSceneRequest request,
                CancellationToken cancellationToken = default)
            {
                Record(RecordedOperation.Replace, request, cancellationToken);
                return UniTask.CompletedTask;
            }

            public UniTask Reload(
                ReloadSceneRequest request,
                CancellationToken cancellationToken = default)
            {
                return UniTask.CompletedTask;
            }

            private void Record(
                RecordedOperation operation,
                LoadSceneRequest request,
                CancellationToken cancellationToken)
            {
                Operation = operation;
                LoadRequest = request;
                CancellationToken = cancellationToken;
            }
        }
    }
}
