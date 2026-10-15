#if CYCLONEGAMES_HAS_ASSETMANAGEMENT
using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.UIFramework.Runtime;
using CycloneGames.UIFramework.Runtime.Integrations;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.UIFramework.Tests.Editor
{
    public sealed class AssetManagementUIWindowAssetProviderTests
    {
        [Test]
        public async Task AssetLease_AssetAccessAfterDispose_ThrowsObjectDisposed()
        {
            var prefab = new GameObject("FakePrefabAsset");
            try
            {
                var handle = new FakeAssetHandle<GameObject>(prefab);
                handle.Complete();
                var provider = new AssetManagementUIWindowAssetProvider(
                    new FakePackage(handle),
                    new FakePathBuilder());

                IUIAssetLease<GameObject> lease = await provider.AcquirePrefabAsync(
                    new UIAssetReference("UI/FakePrefab"),
                    new UIAssetLoadContext(),
                    CancellationToken.None);

                Assert.That(lease.Asset, Is.SameAs(prefab));

                lease.Dispose();

                Assert.Throws<ObjectDisposedException>(() => _ = lease.Asset);
                Assert.That(handle.DisposeCount, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public async Task AcquirePrefabAsync_CancellingPendingLoad_ThrowsAndDisposesHandleExactlyOnce()
        {
            var handle = new FakeAssetHandle<GameObject>(null);
            var provider = new AssetManagementUIWindowAssetProvider(
                new FakePackage(handle),
                new FakePathBuilder());
            using var cts = new CancellationTokenSource();

            UniTask<IUIAssetLease<GameObject>> pending = provider.AcquirePrefabAsync(
                new UIAssetReference("UI/FakePrefab"),
                new UIAssetLoadContext(),
                cts.Token);
            cts.Cancel();

            Assert.CatchAsync<OperationCanceledException>(async () => await pending);
            Assert.That(handle.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task AcquireConfigurationAsync_CancellingPendingLoad_ThrowsAndDisposesHandleExactlyOnce()
        {
            var handle = new FakeAssetHandle<UIWindowConfiguration>(null);
            var provider = new AssetManagementUIWindowAssetProvider(
                new FakePackage(null, handle),
                new FakePathBuilder());
            using var cts = new CancellationTokenSource();

            UniTask<IUIAssetLease<UIWindowConfiguration>> pending = provider.AcquireConfigurationAsync(
                "FakeWindow",
                new UIAssetLoadContext(),
                cts.Token);
            cts.Cancel();

            Assert.CatchAsync<OperationCanceledException>(async () => await pending);
            Assert.That(handle.DisposeCount, Is.EqualTo(1));
        }

        private sealed class FakePathBuilder : IAssetPathBuilder
        {
            public string GetAssetPath(string key)
            {
                return "Fake/" + key;
            }
        }

        private sealed class FakeAssetHandle<TAsset> : IAssetHandle<TAsset>
            where TAsset : UnityEngine.Object
        {
            private readonly UniTaskCompletionSource _completion = new UniTaskCompletionSource();

            public FakeAssetHandle(TAsset asset)
            {
                Asset = asset;
            }

            public TAsset Asset { get; }
            public UnityEngine.Object AssetObject => Asset;
            public int DisposeCount { get; private set; }
            public bool IsDone { get; private set; }
            public float Progress => IsDone ? 1f : 0f;
            public string Error => null;
            public UniTask Task => _completion.Task;

            public void Complete()
            {
                IsDone = true;
                _completion.TrySetResult();
            }

            public void WaitForAsyncComplete()
            {
                _completion.Task.GetAwaiter().GetResult();
            }

            public void Dispose()
            {
                DisposeCount++;
            }
        }

        private sealed class FakePackage : IAssetPackage
        {
            private readonly IAssetHandle<GameObject> _prefabHandle;
            private readonly IAssetHandle<UIWindowConfiguration> _configurationHandle;

            public FakePackage(
                IAssetHandle<GameObject> prefabHandle,
                IAssetHandle<UIWindowConfiguration> configurationHandle = null)
            {
                _prefabHandle = prefabHandle;
                _configurationHandle = configurationHandle;
            }

            public string Name => "Fake";

            public IAssetHandle<TAsset> LoadAssetAsync<TAsset>(
                string location,
                string bucket = null,
                string tag = null,
                string owner = null,
                CancellationToken cancellationToken = default)
                where TAsset : UnityEngine.Object
            {
                if (typeof(TAsset) == typeof(GameObject))
                {
                    return (IAssetHandle<TAsset>)_prefabHandle;
                }

                if (typeof(TAsset) == typeof(UIWindowConfiguration))
                {
                    return (IAssetHandle<TAsset>)_configurationHandle;
                }

                throw new NotSupportedException();
            }

            public UniTask<bool> InitializeAsync(
                AssetPackageInitOptions options,
                CancellationToken cancellationToken = default)
            {
                throw new NotImplementedException();
            }

            public UniTask DestroyAsync()
            {
                throw new NotImplementedException();
            }

            public IInstantiateHandle InstantiateAsync(
                IAssetHandle<GameObject> handle,
                Transform parent = null,
                bool worldPositionStays = false,
                bool setActive = true)
            {
                throw new NotImplementedException();
            }

            public bool IsAssetCached<TAsset>(string location)
                where TAsset : UnityEngine.Object
            {
                throw new NotImplementedException();
            }

            public void SetCacheIdleMemoryBudget(long maxIdleBytes)
            {
                throw new NotImplementedException();
            }

            public int TrimIdleCache(AssetCacheRetentionPolicy policy)
            {
                throw new NotImplementedException();
            }

            public void ClearBucket(string bucket)
            {
                throw new NotImplementedException();
            }

            public void ClearBucketsByPrefix(string bucketPrefix)
            {
                throw new NotImplementedException();
            }
        }
    }
}
#endif
