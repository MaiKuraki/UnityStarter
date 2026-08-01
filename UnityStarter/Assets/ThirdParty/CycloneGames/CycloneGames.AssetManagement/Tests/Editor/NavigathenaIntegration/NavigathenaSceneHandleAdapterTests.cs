#if CYCLONEGAMES_HAS_NAVIGATHENA
using System;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine.SceneManagement;

using Cysharp.Threading.Tasks;
using NUnit.Framework;

using CycloneGames.AssetManagement.Runtime;
using CycloneGames.AssetManagement.Runtime.Integrations.Navigathena;

using AssetSceneHandle = CycloneGames.AssetManagement.Runtime.ISceneHandle;
using NavigathenaSceneHandle = MackySoft.Navigathena.SceneManagement.ISceneHandle;

namespace CycloneGames.AssetManagement.Tests.Editor.NavigathenaIntegration
{
    public sealed class NavigathenaSceneHandleAdapterTests
    {
        [Test]
        public async Task Identifier_CreateHandle_IsLazyAndUsesAdditiveLoad()
        {
            var assetHandle = new FakeAssetSceneHandle(UniTask.CompletedTask);
            var loader = new FakeAssetSceneLoader(assetHandle);
            var identifier = new AssetManagementSceneIdentifier(
                loader,
                "Scenes/Main",
                LoadSceneMode.Additive,
                activateOnLoad: true,
                bucket: "Gameplay.Scene");

            NavigathenaSceneHandle handle = identifier.CreateHandle();

            Assert.That(loader.LoadCount, Is.Zero);

            await handle.Load();

            Assert.That(loader.LoadCount, Is.EqualTo(1));
            Assert.That(loader.LastLocation, Is.EqualTo("Scenes/Main"));
            Assert.That(loader.LastLoadParameters.loadSceneMode, Is.EqualTo(LoadSceneMode.Additive));
            Assert.That(loader.LastActivationMode, Is.EqualTo(SceneActivationMode.ActivateOnLoad));
            Assert.That(loader.LastBucket, Is.EqualTo("Gameplay.Scene"));

            await handle.Unload();

            Assert.That(loader.UnloadCount, Is.EqualTo(1));
            Assert.That(assetHandle.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void Identifier_RejectsSingleAndInvalidLoadModes()
        {
            var loader = new FakeAssetSceneLoader(new FakeAssetSceneHandle(UniTask.CompletedTask));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new AssetManagementSceneIdentifier(loader, "Scenes/Single", LoadSceneMode.Single));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new AssetManagementSceneIdentifier(
                    loader,
                    "Scenes/SingleAdvanced",
                    new LoadSceneParameters(LoadSceneMode.Single)));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new AssetManagementSceneIdentifier(
                    loader,
                    "Scenes/Invalid",
                    (LoadSceneMode)int.MaxValue));
        }

        [Test]
        public void Identifier_RejectsInvalidActivationMode()
        {
            var loader = new FakeAssetSceneLoader(new FakeAssetSceneHandle(UniTask.CompletedTask));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new AssetManagementSceneIdentifier(
                    loader,
                    "Scenes/InvalidActivation",
                    new LoadSceneParameters(LoadSceneMode.Additive),
                    (SceneActivationMode)byte.MaxValue));
        }

        [Test]
        public async Task PreCancelledLoad_DoesNotAcquireAndCanRetry()
        {
            var assetHandle = new FakeAssetSceneHandle(UniTask.CompletedTask);
            var loader = new FakeAssetSceneLoader(assetHandle);
            NavigathenaSceneHandle handle = new AssetManagementSceneIdentifier(
                    loader,
                    "Scenes/PreCancelledLoad")
                .CreateHandle();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.CatchAsync<OperationCanceledException>(
                async () => await handle.Load(cancellationToken: cancellation.Token));

            Assert.That(loader.LoadCount, Is.Zero);

            await handle.Load();
            await handle.Unload();

            Assert.That(loader.LoadCount, Is.EqualTo(1));
            Assert.That(loader.UnloadCount, Is.EqualTo(1));
            Assert.That(assetHandle.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task LoadFailure_UnloadsAndReleasesAcquiredHandle()
        {
            var loadFailure = new InvalidOperationException("Synthetic scene load failure.");
            var assetHandle = new FakeAssetSceneHandle(UniTask.FromException(loadFailure));
            var loader = new FakeAssetSceneLoader(assetHandle);
            NavigathenaSceneHandle handle = new AssetManagementSceneIdentifier(
                    loader,
                    "Scenes/Failure")
                .CreateHandle();

            InvalidOperationException observed = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await handle.Load());

            Assert.That(observed, Is.SameAs(loadFailure));
            Assert.That(loader.LoadCount, Is.EqualTo(1));
            Assert.That(loader.UnloadCount, Is.EqualTo(1));
            Assert.That(loader.LastUnloadedHandle, Is.SameAs(assetHandle));
            Assert.That(assetHandle.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task LoadAndCleanupFailure_AggregatesFailuresAndRetainsOwnershipForRetry()
        {
            var loadFailure = new InvalidOperationException("Synthetic scene load failure.");
            var cleanupFailure = new InvalidOperationException("Synthetic scene cleanup failure.");
            var assetHandle = new FakeAssetSceneHandle(UniTask.FromException(loadFailure));
            var loader = new FakeAssetSceneLoader(assetHandle)
            {
                UnloadBehavior = attempt => attempt == 1
                    ? UniTask.FromException(cleanupFailure)
                    : UniTask.CompletedTask,
            };
            NavigathenaSceneHandle handle = new AssetManagementSceneIdentifier(
                    loader,
                    "Scenes/LoadAndCleanupFailure")
                .CreateHandle();

            AggregateException observed = Assert.ThrowsAsync<AggregateException>(
                async () => await handle.Load());

            Assert.That(observed.InnerExceptions, Does.Contain(loadFailure));
            Assert.That(observed.InnerExceptions, Does.Contain(cleanupFailure));
            Assert.That(loader.UnloadCount, Is.EqualTo(1));
            Assert.That(assetHandle.DisposeCount, Is.Zero);

            await handle.Unload();

            Assert.That(loader.UnloadCount, Is.EqualTo(2));
            Assert.That(assetHandle.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task InvalidProviderActivationMode_CleansUpAcquiredHandle()
        {
            var assetHandle = new FakeAssetSceneHandle(UniTask.CompletedTask)
            {
                ActivationMode = (SceneActivationMode)byte.MaxValue,
            };
            var loader = new FakeAssetSceneLoader(assetHandle);
            NavigathenaSceneHandle handle = new AssetManagementSceneIdentifier(
                    loader,
                    "Scenes/InvalidProviderActivation")
                .CreateHandle();

            Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await handle.Load());

            Assert.That(loader.LoadCount, Is.EqualTo(1));
            Assert.That(loader.UnloadCount, Is.EqualTo(1));
            Assert.That(assetHandle.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task CancelledManualActivation_PerformsAuthoritativeCleanup()
        {
            var activationGate = new UniTaskCompletionSource();
            var assetHandle = new FakeAssetSceneHandle(UniTask.CompletedTask)
            {
                ActivationMode = SceneActivationMode.Manual,
                ActivationBehavior = cancellationToken =>
                    activationGate.Task.AttachExternalCancellation(cancellationToken),
            };
            var loader = new FakeAssetSceneLoader(assetHandle);
            NavigathenaSceneHandle handle = new AssetManagementSceneIdentifier(
                    loader,
                    "Scenes/CancelledManualActivation",
                    LoadSceneMode.Additive,
                    activateOnLoad: false)
                .CreateHandle();
            using var cancellation = new CancellationTokenSource();

            UniTask<Scene> load = handle.Load(cancellationToken: cancellation.Token);
            Assert.That(assetHandle.ActivationCount, Is.EqualTo(1));

            cancellation.Cancel();

            Assert.CatchAsync<OperationCanceledException>(async () => await load);
            Assert.That(loader.UnloadCount, Is.EqualTo(1));
            Assert.That(assetHandle.DisposeCount, Is.EqualTo(1));

            activationGate.TrySetResult();
        }

        [Test]
        public async Task ConcurrentAndRepeatedUnload_JoinOneProviderUnload()
        {
            var assetHandle = new FakeAssetSceneHandle(UniTask.CompletedTask);
            var loader = new FakeAssetSceneLoader(assetHandle);
            var unloadGate = new UniTaskCompletionSource();
            loader.UnloadBehavior = _ => unloadGate.Task;
            NavigathenaSceneHandle handle = new AssetManagementSceneIdentifier(
                    loader,
                    "Scenes/ConcurrentUnload")
                .CreateHandle();
            await handle.Load();

            UniTask first = handle.Unload();
            UniTask second = handle.Unload();

            Assert.That(loader.UnloadCount, Is.EqualTo(1));
            Assert.That(assetHandle.DisposeCount, Is.Zero);

            unloadGate.TrySetResult();
            await first;
            await second;
            await handle.Unload();

            Assert.That(loader.UnloadCount, Is.EqualTo(1));
            Assert.That(assetHandle.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task FailedProviderUnload_IsRetryableAndDoesNotReleaseCallerLeaseEarly()
        {
            var assetHandle = new FakeAssetSceneHandle(UniTask.CompletedTask);
            var loader = new FakeAssetSceneLoader(assetHandle)
            {
                UnloadBehavior = attempt => attempt == 1
                    ? UniTask.FromException(new InvalidOperationException("Synthetic provider unload failure."))
                    : UniTask.CompletedTask,
            };
            NavigathenaSceneHandle handle = new AssetManagementSceneIdentifier(
                    loader,
                    "Scenes/RetryUnload")
                .CreateHandle();
            await handle.Load();

            Assert.ThrowsAsync<InvalidOperationException>(async () => await handle.Unload());

            Assert.That(loader.UnloadCount, Is.EqualTo(1));
            Assert.That(assetHandle.DisposeCount, Is.Zero);

            await handle.Unload();

            Assert.That(loader.UnloadCount, Is.EqualTo(2));
            Assert.That(assetHandle.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task SynchronousProviderUnloadFailure_RetainsOwnershipAndCanRetry()
        {
            var assetHandle = new FakeAssetSceneHandle(UniTask.CompletedTask);
            var loader = new FakeAssetSceneLoader(assetHandle)
            {
                UnloadBehavior = attempt =>
                {
                    if (attempt == 1)
                    {
                        throw new InvalidOperationException("Synthetic synchronous provider unload failure.");
                    }

                    return UniTask.CompletedTask;
                },
            };
            NavigathenaSceneHandle handle = new AssetManagementSceneIdentifier(
                    loader,
                    "Scenes/SynchronousRetryUnload")
                .CreateHandle();
            await handle.Load();

            Assert.ThrowsAsync<InvalidOperationException>(async () => await handle.Unload());

            Assert.That(loader.UnloadCount, Is.EqualTo(1));
            Assert.That(assetHandle.DisposeCount, Is.Zero);

            await handle.Unload();

            Assert.That(loader.UnloadCount, Is.EqualTo(2));
            Assert.That(assetHandle.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task PreCancelledUnload_DoesNotCommitAndCanRetry()
        {
            var assetHandle = new FakeAssetSceneHandle(UniTask.CompletedTask);
            var loader = new FakeAssetSceneLoader(assetHandle);
            NavigathenaSceneHandle handle = new AssetManagementSceneIdentifier(
                    loader,
                    "Scenes/PreCancelledUnload")
                .CreateHandle();
            await handle.Load();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.CatchAsync<OperationCanceledException>(
                async () => await handle.Unload(cancellationToken: cancellation.Token));

            Assert.That(loader.UnloadCount, Is.Zero);
            Assert.That(assetHandle.DisposeCount, Is.Zero);

            await handle.Unload();

            Assert.That(loader.UnloadCount, Is.EqualTo(1));
            Assert.That(assetHandle.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task CancelledUnloadWait_DoesNotCancelProviderAndLaterCallJoins()
        {
            var unloadGate = new UniTaskCompletionSource();
            var assetHandle = new FakeAssetSceneHandle(UniTask.CompletedTask);
            var loader = new FakeAssetSceneLoader(assetHandle)
            {
                UnloadBehavior = _ => unloadGate.Task,
            };
            NavigathenaSceneHandle handle = new AssetManagementSceneIdentifier(
                    loader,
                    "Scenes/CancelledUnloadWait")
                .CreateHandle();
            await handle.Load();
            using var cancellation = new CancellationTokenSource();

            UniTask cancelledWait = handle.Unload(cancellationToken: cancellation.Token);
            Assert.That(loader.UnloadCount, Is.EqualTo(1));

            cancellation.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await cancelledWait);

            UniTask joinedWait = handle.Unload();
            Assert.That(loader.UnloadCount, Is.EqualTo(1));

            unloadGate.TrySetResult();
            await joinedWait;

            Assert.That(loader.UnloadCount, Is.EqualTo(1));
            Assert.That(assetHandle.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task FailedCallerRelease_RetriesWithoutRepeatingProviderUnload()
        {
            var assetHandle = new FakeAssetSceneHandle(UniTask.CompletedTask)
            {
                DisposeFailuresRemaining = 1,
            };
            var loader = new FakeAssetSceneLoader(assetHandle);
            NavigathenaSceneHandle handle = new AssetManagementSceneIdentifier(
                    loader,
                    "Scenes/RetryCallerRelease")
                .CreateHandle();
            await handle.Load();

            Assert.ThrowsAsync<InvalidOperationException>(async () => await handle.Unload());

            Assert.That(loader.UnloadCount, Is.EqualTo(1));
            Assert.That(assetHandle.DisposeCount, Is.EqualTo(1));

            await handle.Unload();

            Assert.That(loader.UnloadCount, Is.EqualTo(1));
            Assert.That(assetHandle.DisposeCount, Is.EqualTo(2));
        }

        [Test]
        public async Task TakeOwnership_UsesExistingHandleWithoutStartingAnotherLoad()
        {
            var assetHandle = new FakeAssetSceneHandle(UniTask.CompletedTask);
            var loader = new FakeAssetSceneLoader(assetHandle);
            NavigathenaSceneHandle handle = NavigathenaSceneHandleAdapter.TakeOwnership(
                assetHandle,
                loader,
                LoadSceneMode.Additive);

            await handle.Load();

            Assert.That(loader.LoadCount, Is.Zero);

            await handle.Unload();

            Assert.That(loader.UnloadCount, Is.EqualTo(1));
            Assert.That(loader.LastUnloadedHandle, Is.SameAs(assetHandle));
            Assert.That(assetHandle.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void TakeOwnership_RejectsNonAdditiveSceneHandles()
        {
            var assetHandle = new FakeAssetSceneHandle(UniTask.CompletedTask);
            var loader = new FakeAssetSceneLoader(assetHandle);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                NavigathenaSceneHandleAdapter.TakeOwnership(
                    assetHandle,
                    loader,
                    LoadSceneMode.Single));
        }

        [Test]
        public void TakeOwnership_RejectsInvalidProviderActivationWithoutConsumingOwnership()
        {
            var assetHandle = new FakeAssetSceneHandle(UniTask.CompletedTask)
            {
                ActivationMode = (SceneActivationMode)byte.MaxValue,
            };
            var loader = new FakeAssetSceneLoader(assetHandle);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                NavigathenaSceneHandleAdapter.TakeOwnership(
                    assetHandle,
                    loader,
                    LoadSceneMode.Additive));

            Assert.That(loader.LoadCount, Is.Zero);
            Assert.That(loader.UnloadCount, Is.Zero);
            Assert.That(assetHandle.DisposeCount, Is.Zero);
        }

        private sealed class FakeAssetSceneLoader : IAssetSceneLoader
        {
            private readonly AssetSceneHandle _handle;

            public FakeAssetSceneLoader(AssetSceneHandle handle)
            {
                _handle = handle ?? throw new ArgumentNullException(nameof(handle));
            }

            public int LoadCount { get; private set; }
            public int UnloadCount { get; private set; }
            public string LastLocation { get; private set; }
            public LoadSceneParameters LastLoadParameters { get; private set; }
            public SceneActivationMode LastActivationMode { get; private set; }
            public string LastBucket { get; private set; }
            public AssetSceneHandle LastUnloadedHandle { get; private set; }
            public Func<int, UniTask> UnloadBehavior { get; set; }

            public AssetSceneHandle LoadSceneAsync(
                string sceneLocation,
                LoadSceneParameters loadParameters,
                SceneActivationMode activationMode,
                int priority = 100,
                string bucket = null)
            {
                LoadCount++;
                LastLocation = sceneLocation;
                LastLoadParameters = loadParameters;
                LastActivationMode = activationMode;
                LastBucket = bucket;
                return _handle;
            }

            public AssetSceneHandle LoadSceneAsync(
                string sceneLocation,
                LoadSceneMode loadMode = LoadSceneMode.Single,
                bool activateOnLoad = true,
                int priority = 100,
                string bucket = null)
            {
                return LoadSceneAsync(
                    sceneLocation,
                    new LoadSceneParameters(loadMode),
                    activateOnLoad ? SceneActivationMode.ActivateOnLoad : SceneActivationMode.Manual,
                    priority,
                    bucket);
            }

            public UniTask UnloadSceneAsync(
                AssetSceneHandle sceneHandle,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                UnloadCount++;
                LastUnloadedHandle = sceneHandle;
                return UnloadBehavior?.Invoke(UnloadCount) ?? UniTask.CompletedTask;
            }
        }

        private sealed class FakeAssetSceneHandle : AssetSceneHandle
        {
            private readonly UniTask _task;

            public FakeAssetSceneHandle(UniTask task)
            {
                _task = task;
            }

            public int DisposeCount { get; private set; }
            public int DisposeFailuresRemaining { get; set; }
            public int ActivationCount { get; private set; }
            public Func<CancellationToken, UniTask> ActivationBehavior { get; set; }
            public string ScenePath => "Scenes/Fake";
            public Scene Scene => default;
            public SceneActivationMode ActivationMode { get; set; } = SceneActivationMode.ActivateOnLoad;
            public SceneActivationState ActivationState => SceneActivationState.Activated;
            public bool SupportsManualActivation => true;
            public bool IsDone => _task.Status != UniTaskStatus.Pending;
            public float Progress => IsDone ? 1f : 0f;
            public string Error => string.Empty;
            public UniTask Task => _task;

            public UniTask ActivateAsync(CancellationToken cancellationToken = default)
            {
                ActivationCount++;
                cancellationToken.ThrowIfCancellationRequested();
                return ActivationBehavior?.Invoke(cancellationToken) ?? UniTask.CompletedTask;
            }

            public void WaitForAsyncComplete()
            {
            }

            public void Dispose()
            {
                DisposeCount++;
                if (DisposeFailuresRemaining > 0)
                {
                    DisposeFailuresRemaining--;
                    throw new InvalidOperationException("Synthetic caller release failure.");
                }
            }
        }
    }
}
#endif
