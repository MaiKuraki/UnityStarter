using System;
using System.Threading;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.GameplayFramework.Runtime;
using CycloneGames.GameplayFramework.Runtime.Integrations.AssetManagement;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Integrations.AssetManagement.Tests.Editor
{
    public sealed class AssetManagementWorldSettingsReferenceResolverTests
    {
        private GameObject prefab;

        [TearDown]
        public void TearDown()
        {
            if (prefab != null)
            {
                UnityEngine.Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void ComponentReference_RegistersHandleBeforeReadingPrefabAsset()
        {
            prefab = new GameObject("PlayerStatePrefab");
            PlayerState expected = prefab.AddComponent<PlayerState>();
            var package = new PrefabOnlyAssetPackage(prefab);
            var resolver = new AssetManagementWorldSettingsReferenceResolver(package);
            var registrar = new RecordingLeaseRegistrar();

            WorldSettingsAssetLoadResult<PlayerState> result = resolver
                .ResolveAsync<PlayerState>(
                    "Gameplay/PlayerState",
                    registrar,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.IsTrue(result.Success, result.Error);
            Assert.AreSame(expected, result.Asset);
            Assert.AreEqual(typeof(GameObject), package.LastRequestedType);
            Assert.IsFalse(package.LastHandle.IsDisposed);
            Assert.AreSame(package.LastHandle, registrar.Lease);

            registrar.Dispose();

            Assert.IsTrue(package.LastHandle.IsDisposed);
        }

        [Test]
        public void ComponentReference_RejectsPrefabWithoutRequiredRootComponent()
        {
            prefab = new GameObject("InvalidPrefab");
            var package = new PrefabOnlyAssetPackage(prefab);
            var resolver = new AssetManagementWorldSettingsReferenceResolver(package);
            var registrar = new RecordingLeaseRegistrar();

            WorldSettingsAssetLoadResult<PlayerState> result = resolver
                .ResolveAsync<PlayerState>(
                    "Gameplay/PlayerState",
                    registrar,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.IsFalse(result.Success);
            StringAssert.Contains("exactly one", result.Error);
            Assert.AreSame(package.LastHandle, registrar.Lease);
            Assert.IsFalse(package.LastHandle.IsDisposed);

            registrar.Dispose();

            Assert.IsTrue(package.LastHandle.IsDisposed);
        }

        [Test]
        public void OutOfMemoryAfterHandleCreation_PropagatesWithLeaseAlreadyRegistered()
        {
            prefab = new GameObject("PlayerStatePrefab");
            prefab.AddComponent<PlayerState>();
            var package = new PrefabOnlyAssetPackage(prefab)
            {
                ThrowOutOfMemoryOnTaskAccess = true,
            };
            var resolver = new AssetManagementWorldSettingsReferenceResolver(package);
            var registrar = new RecordingLeaseRegistrar();

            Assert.Throws<OutOfMemoryException>(() => resolver
                .ResolveAsync<PlayerState>(
                    "Gameplay/PlayerState",
                    registrar,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult());

            Assert.AreSame(package.LastHandle, registrar.Lease);
            Assert.IsFalse(package.LastHandle.IsDisposed);

            registrar.Dispose();

            Assert.IsTrue(package.LastHandle.IsDisposed);
        }

        [Test]
        public void CancellationAfterHandleCreation_PropagatesWithLeaseAlreadyRegistered()
        {
            prefab = new GameObject("PlayerStatePrefab");
            prefab.AddComponent<PlayerState>();
            var package = new PrefabOnlyAssetPackage(prefab)
            {
                ThrowCancellationOnTaskAccess = true,
            };
            var resolver = new AssetManagementWorldSettingsReferenceResolver(package);
            var registrar = new RecordingLeaseRegistrar();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() => resolver
                .ResolveAsync<PlayerState>(
                    "Gameplay/PlayerState",
                    registrar,
                    cancellation.Token)
                .GetAwaiter()
                .GetResult());

            Assert.AreSame(package.LastHandle, registrar.Lease);
            Assert.IsFalse(package.LastHandle.IsDisposed);

            registrar.Dispose();

            Assert.IsTrue(package.LastHandle.IsDisposed);
        }

        private sealed class PrefabOnlyAssetPackage : IAssetPackage
        {
            private readonly GameObject prefabAsset;

            public PrefabOnlyAssetPackage(GameObject prefabAsset)
            {
                this.prefabAsset = prefabAsset;
            }

            public string Name => "GameplayFrameworkTests";
            public Type LastRequestedType { get; private set; }
            public CompletedAssetHandle<GameObject> LastHandle { get; private set; }
            public bool ThrowOutOfMemoryOnTaskAccess { get; set; }
            public bool ThrowCancellationOnTaskAccess { get; set; }

            public UniTask<bool> InitializeAsync(
                AssetPackageInitOptions options,
                CancellationToken cancellationToken = default)
            {
                return UniTask.FromResult(true);
            }

            public UniTask DestroyAsync()
            {
                return UniTask.CompletedTask;
            }

            public IAssetHandle<TAsset> LoadAssetAsync<TAsset>(
                string location,
                string bucket = null,
                string tag = null,
                string owner = null,
                CancellationToken cancellationToken = default) where TAsset : UnityEngine.Object
            {
                LastRequestedType = typeof(TAsset);
                if (typeof(TAsset) != typeof(GameObject))
                {
                    throw new InvalidOperationException("The resolver must request the prefab GameObject.");
                }

                LastHandle = new CompletedAssetHandle<GameObject>(
                    prefabAsset,
                    ThrowOutOfMemoryOnTaskAccess,
                    ThrowCancellationOnTaskAccess);
                return (IAssetHandle<TAsset>)(object)LastHandle;
            }

            public IInstantiateHandle InstantiateAsync(
                IAssetHandle<GameObject> handle,
                Transform parent = null,
                bool worldPositionStays = false,
                bool setActive = true)
            {
                throw new NotSupportedException();
            }

            public bool IsAssetCached<TAsset>(string location) where TAsset : UnityEngine.Object
            {
                return false;
            }

            public void SetCacheIdleMemoryBudget(long maxIdleBytes)
            {
            }

            public int TrimIdleCache(AssetCacheRetentionPolicy policy)
            {
                return 0;
            }

            public void ClearBucket(string bucket)
            {
            }

            public void ClearBucketsByPrefix(string bucketPrefix)
            {
            }
        }

        private sealed class CompletedAssetHandle<TAsset> : IAssetHandle<TAsset>
            where TAsset : UnityEngine.Object
        {
            private readonly bool throwOutOfMemoryOnTaskAccess;
            private readonly bool throwCancellationOnTaskAccess;

            public CompletedAssetHandle(
                TAsset asset,
                bool throwOutOfMemoryOnTaskAccess,
                bool throwCancellationOnTaskAccess)
            {
                Asset = asset;
                this.throwOutOfMemoryOnTaskAccess = throwOutOfMemoryOnTaskAccess;
                this.throwCancellationOnTaskAccess = throwCancellationOnTaskAccess;
            }

            public bool IsDisposed { get; private set; }
            public bool IsDone => true;
            public float Progress => 1f;
            public string Error => null;
            public UniTask Task
            {
                get
                {
                    if (throwOutOfMemoryOnTaskAccess)
                    {
                        throw new OutOfMemoryException("Synthetic asset-handle task failure.");
                    }
                    if (throwCancellationOnTaskAccess)
                    {
                        throw new OperationCanceledException("Synthetic asset-handle cancellation.");
                    }

                    return UniTask.CompletedTask;
                }
            }
            public TAsset Asset { get; }
            public UnityEngine.Object AssetObject => Asset;

            public void WaitForAsyncComplete()
            {
            }

            public void Dispose()
            {
                IsDisposed = true;
            }
        }

        private sealed class RecordingLeaseRegistrar : IWorldSettingsLeaseRegistrar, IDisposable
        {
            public IDisposable Lease { get; private set; }

            public void Register(IDisposable lease)
            {
                if (lease == null)
                {
                    throw new ArgumentNullException(nameof(lease));
                }
                if (Lease != null)
                {
                    throw new InvalidOperationException("The test registrar accepts one lease.");
                }

                Lease = lease;
            }

            public void Dispose()
            {
                IDisposable lease = Lease;
                Lease = null;
                lease?.Dispose();
            }
        }
    }
}
