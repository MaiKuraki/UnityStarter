using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;
using UnityEngine.TestTools;

using Cysharp.Threading.Tasks;
using NUnit.Framework;

using CycloneGames.AssetManagement.Runtime;
using CycloneGames.AssetManagement.Runtime.Cache;

namespace CycloneGames.AssetManagement.Tests.Editor
{
    public sealed class ResourcesProviderTests
    {
        [Test]
        public async Task Package_Rejects_Business_Operations_Until_Initialization_Completes()
        {
            var module = new ResourcesModule();
            await module.InitializeAsync();
            try
            {
                IAssetPackage package = module.CreatePackage("UninitializedResourcesTests");
                var syncOperations = (IAssetSyncOperations)package;

                Assert.Throws<InvalidOperationException>(() =>
                    package.IsAssetCached<Texture2D>("__CycloneGames_AssetManagement_Uninitialized__"));
                Assert.Throws<InvalidOperationException>(() =>
                    syncOperations.LoadAssetSync<Texture2D>("__CycloneGames_AssetManagement_Uninitialized__"));

                Assert.IsTrue(await package.InitializeAsync(new AssetPackageInitOptions()));
                Assert.DoesNotThrow(() =>
                    package.IsAssetCached<Texture2D>("__CycloneGames_AssetManagement_Initialized__"));
            }
            finally
            {
                await module.DestroyAsync();
            }
        }

        [Test]
        public async Task Package_Initialization_Is_Idempotent_After_Success()
        {
            var module = new ResourcesModule();
            await module.InitializeAsync();
            try
            {
                IAssetPackage package = module.CreatePackage("RepeatedInitializationResourcesTests");

                Assert.IsTrue(await package.InitializeAsync(new AssetPackageInitOptions()));
                Assert.IsTrue(await package.InitializeAsync(new AssetPackageInitOptions()));
            }
            finally
            {
                await module.DestroyAsync();
            }
        }

        [Test]
        public async Task Cancelled_Package_Initialization_Does_Not_Open_The_Business_Gate()
        {
            var module = new ResourcesModule();
            await module.InitializeAsync();
            try
            {
                IAssetPackage package = module.CreatePackage("CancelledInitializationResourcesTests");
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();

                Assert.CatchAsync<OperationCanceledException>(
                    async () => await package.InitializeAsync(
                        new AssetPackageInitOptions(),
                        cancellation.Token));
                Assert.Throws<InvalidOperationException>(() =>
                    package.IsAssetCached<Texture2D>("__CycloneGames_AssetManagement_Cancelled_Init__"));

                Assert.IsTrue(await package.InitializeAsync(new AssetPackageInitOptions()));
            }
            finally
            {
                await module.DestroyAsync();
            }
        }

        [Test]
        public async Task Missing_Synchronous_Resource_Faults_A_Memoized_Task()
        {
            var module = new ResourcesModule();
            await module.InitializeAsync();
            try
            {
                IAssetPackage package = module.CreatePackage("ResourcesTests");
                await package.InitializeAsync(new AssetPackageInitOptions());
                var syncOperations = (IAssetSyncOperations)package;

                using IAssetHandle<Texture2D> handle = syncOperations.LoadAssetSync<Texture2D>(
                    "__CycloneGames_AssetManagement_Missing_Resource__");

                Assert.IsTrue(handle.IsDone);
                Assert.IsNull(handle.Asset);
                Assert.IsNotEmpty(handle.Error);

                InvalidOperationException first = Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await handle.Task);
                InvalidOperationException second = Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await handle.Task);
                Assert.AreEqual(first.Message, second.Message);
            }
            finally
            {
                await module.DestroyAsync();
            }
        }

        [UnityTest]
        public IEnumerator Missing_Asynchronous_Resource_Faults_And_Retains_Diagnostic_Error()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var module = new ResourcesModule();
                await module.InitializeAsync();
                try
                {
                    IAssetPackage package = module.CreatePackage("AsyncFailureResourcesTests");
                    await package.InitializeAsync(new AssetPackageInitOptions());

                    using IAssetHandle<Texture2D> handle = package.LoadAssetAsync<Texture2D>(
                        "__CycloneGames_AssetManagement_Missing_Async_Resource__");

                    InvalidOperationException failure = null;
                    try
                    {
                        await handle.Task;
                    }
                    catch (InvalidOperationException exception)
                    {
                        failure = exception;
                    }

                    Assert.IsNotNull(failure);
                    Assert.IsTrue(handle.IsDone);
                    Assert.IsNull(handle.Asset);
                    Assert.IsNotEmpty(handle.Error);
                }
                finally
                {
                    await module.DestroyAsync();
                }
            });
        }

        [Test]
        public async Task Failed_Resource_Instantiation_Is_Rejected_Before_Creating_Instance_Operation()
        {
            var module = new ResourcesModule();
            await module.InitializeAsync();
            try
            {
                IAssetPackage package = module.CreatePackage("InstantiateFailureResourcesTests");
                await package.InitializeAsync(new AssetPackageInitOptions());
                var syncOperations = (IAssetSyncOperations)package;

                using IAssetHandle<GameObject> source = syncOperations.LoadAssetSync<GameObject>(
                    "__CycloneGames_AssetManagement_Missing_Prefab__");

                Assert.Throws<InvalidOperationException>(() => package.InstantiateAsync(source));
            }
            finally
            {
                await module.DestroyAsync();
            }
        }

        [Test]
        public async Task Destroyed_Package_Rejects_Load_And_Instantiate()
        {
            var module = new ResourcesModule();
            await module.InitializeAsync();
            IAssetPackage package = module.CreatePackage("DestroyedResourcesTests");
            await package.InitializeAsync(new AssetPackageInitOptions());
            var syncOperations = (IAssetSyncOperations)package;
            await package.DestroyAsync();

            Assert.Throws<System.ObjectDisposedException>(() =>
                syncOperations.LoadAssetSync<Texture2D>("__CycloneGames_AssetManagement_Missing_Resource__"));

            await module.DestroyAsync();
        }

        [Test]
        public async Task Failed_Package_Cleanup_Does_Not_Mark_Destroyed_And_Can_Be_Retried()
        {
            var module = new ResourcesModule();
            await module.InitializeAsync();
            try
            {
                IAssetPackage package = module.CreatePackage("RetryableDestroyResourcesTests");
                await package.InitializeAsync(new AssetPackageInitOptions());
                AssetCacheService cache = GetCacheService((ResourcesAssetPackage)package);
                AssetCacheKey key = AssetCacheService.BuildCacheKey("Tests/RetryableDestroy", typeof(Texture2D));
                var handle = new FailOnceCacheHandle();
                cache.RegisterNew(key, null, null, null, handle);

                Assert.ThrowsAsync<AggregateException>(async () => await package.DestroyAsync());
                Assert.That(handle.ForceDisposeCount, Is.EqualTo(1));
                Assert.That(cache.PendingReleaseRetryCount, Is.EqualTo(1));
                Assert.Throws<ObjectDisposedException>(() =>
                    package.InitializeAsync(new AssetPackageInitOptions()));
                Assert.Throws<ObjectDisposedException>(() =>
                    package.IsAssetCached<Texture2D>("Tests/RetryableDestroy"));

                Assert.DoesNotThrowAsync(async () => await package.DestroyAsync());
                Assert.That(handle.ForceDisposeCount, Is.EqualTo(2));
                Assert.That(cache.PendingReleaseRetryCount, Is.Zero);
            }
            finally
            {
                await module.DestroyAsync();
            }
        }

        [Test]
        public async Task UnloadUnusedAssets_Is_SingleFlight_And_Package_Destroy_Joins_The_Operation()
        {
            var unloadGate = new UniTaskCompletionSource();
            int providerInvocationCount = 0;
            var package = new ResourcesAssetPackage(
                "UnloadUnusedAssetsSingleFlightTests",
                () =>
                {
                    providerInvocationCount++;
                    return unloadGate.Task;
                });
            await package.InitializeAsync(new AssetPackageInitOptions());

            UniTask first = package.CollectUnusedUnityAssetsAsync();
            UniTask second = package.CollectUnusedUnityAssetsAsync();

            Assert.That(providerInvocationCount, Is.EqualTo(1));
            Assert.That(package.OperationTails.PendingCount, Is.EqualTo(1));

            UniTask destroy = package.DestroyAsync();
            Assert.That(destroy.Status, Is.EqualTo(UniTaskStatus.Pending));
            Assert.Throws<ObjectDisposedException>(() => package.CollectUnusedUnityAssetsAsync());

            unloadGate.TrySetResult();
            await first;
            await second;
            await destroy;

            Assert.That(package.OperationTails.PendingCount, Is.Zero);
        }

        [Test]
        public async Task Bulk_Load_Capability_Is_Absent_Instead_Of_Blocking_The_Main_Thread()
        {
            var module = new ResourcesModule();
            await module.InitializeAsync();
            try
            {
                IAssetPackage package = module.CreatePackage("BulkLoadResourcesTests");
                await package.InitializeAsync(new AssetPackageInitOptions());

                Assert.IsFalse(package is IAssetBulkLoader);
            }
            finally
            {
                await module.DestroyAsync();
            }
        }

        [Test]
        public async Task Module_Destroy_Is_Idempotent()
        {
            var module = new ResourcesModule();
            await module.InitializeAsync();
            IAssetPackage package = module.CreatePackage("DestroyTwiceResourcesTests");
            await package.InitializeAsync(new AssetPackageInitOptions());

            await module.DestroyAsync();
            await module.DestroyAsync();

            Assert.IsFalse(module.Initialized);
            Assert.Throws<ObjectDisposedException>(() => module.GetAllPackageNames());
            Assert.Throws<ObjectDisposedException>(() => module.InitializeAsync());
        }

        [Test]
        public async Task Failed_Module_Cleanup_Stays_Shut_Down_And_Can_Be_Retried()
        {
            var module = new ResourcesModule();
            await module.InitializeAsync();
            IAssetPackage package = module.CreatePackage("RetryableModuleDestroyResourcesTests");
            await package.InitializeAsync(new AssetPackageInitOptions());
            AssetCacheService cache = GetCacheService((ResourcesAssetPackage)package);
            AssetCacheKey key = AssetCacheService.BuildCacheKey("Tests/RetryableModuleDestroy", typeof(Texture2D));
            var handle = new FailOnceCacheHandle();
            cache.RegisterNew(key, null, null, null, handle);

            try
            {
                Assert.ThrowsAsync<AggregateException>(async () => await module.DestroyAsync());
                Assert.That(handle.ForceDisposeCount, Is.EqualTo(1));
                Assert.IsFalse(module.Initialized);

                Assert.Throws<ObjectDisposedException>(() => module.InitializeAsync());
                Assert.Throws<ObjectDisposedException>(() => module.CreatePackage("RejectedAfterShutdown"));
                Assert.Throws<ObjectDisposedException>(() => module.GetPackage(package.Name));
                Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                    await module.RemovePackageAsync(package.Name));
                Assert.Throws<ObjectDisposedException>(() => module.GetAllPackageNames());
                Assert.Throws<ObjectDisposedException>(() =>
                    package.InitializeAsync(new AssetPackageInitOptions()));

                Assert.DoesNotThrowAsync(async () => await module.DestroyAsync());
                Assert.That(handle.ForceDisposeCount, Is.EqualTo(2));
                Assert.That(cache.PendingReleaseRetryCount, Is.Zero);
                Assert.IsFalse(module.Initialized);
                Assert.DoesNotThrowAsync(async () => await module.DestroyAsync());
            }
            finally
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    try
                    {
                        await module.DestroyAsync();
                        break;
                    }
                    catch (Exception exception) when (AssetRuntimeGuard.IsRecoverableException(exception))
                    {
                        // Preserve the primary assertion failure while retrying the fail-once cleanup fixture.
                    }
                }
            }
        }

        [Test]
        public async Task Package_Destroy_Invalidates_Outstanding_Asset_Lease_Access()
        {
            var module = new ResourcesModule();
            await module.InitializeAsync();
            IAssetPackage package = module.CreatePackage("OutstandingLeaseResourcesTests");
            await package.InitializeAsync(new AssetPackageInitOptions());
            var syncOperations = (IAssetSyncOperations)package;
            IAssetHandle<Texture2D> lease = syncOperations.LoadAssetSync<Texture2D>(
                "__CycloneGames_AssetManagement_Missing_Resource__");

            await package.DestroyAsync();

            Assert.Throws<System.ObjectDisposedException>(() => _ = lease.Error);
            Assert.Throws<System.ObjectDisposedException>(() => _ = lease.Task);
            lease.Dispose();
            await module.DestroyAsync();
        }

        [Test]
        public async Task Package_Destroy_Releases_Outstanding_Resource_Instance()
        {
            var module = new ResourcesModule();
            await module.InitializeAsync();
            IAssetPackage package = module.CreatePackage("OutstandingInstanceResourcesTests");
            await package.InitializeAsync(new AssetPackageInitOptions());
            var sourceObject = new GameObject("ResourcesInstanceSource");
            using IAssetHandle<GameObject> source = CreateOwnedPrefabLease(package, sourceObject);
            IInstantiateHandle instance = package.InstantiateAsync(source);

            try
            {
                Assert.IsNotNull(instance.Instance);

                await package.DestroyAsync();

                Assert.IsNull(instance.Instance);
                Assert.DoesNotThrow(instance.Dispose);
            }
            finally
            {
                if (sourceObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(sourceObject);
                }

                await module.DestroyAsync();
            }
        }

        [Test]
        public async Task Module_Destroy_Releases_Outstanding_Resource_Instance()
        {
            var module = new ResourcesModule();
            await module.InitializeAsync();
            IAssetPackage package = module.CreatePackage("ModuleOutstandingInstanceResourcesTests");
            await package.InitializeAsync(new AssetPackageInitOptions());
            var sourceObject = new GameObject("ModuleResourcesInstanceSource");
            using IAssetHandle<GameObject> source = CreateOwnedPrefabLease(package, sourceObject);
            IInstantiateHandle instance = package.InstantiateAsync(source);

            try
            {
                Assert.IsNotNull(instance.Instance);

                await module.DestroyAsync();

                Assert.IsNull(instance.Instance);
                Assert.DoesNotThrow(instance.Dispose);
            }
            finally
            {
                if (sourceObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(sourceObject);
                }

                await module.DestroyAsync();
            }
        }

        [Test]
        public async Task Instantiate_Rejects_Prefab_Lease_From_Another_Package()
        {
            var module = new ResourcesModule();
            await module.InitializeAsync();
            var sourceObject = new GameObject("CrossPackageResourcesSource");
            try
            {
                IAssetPackage sourcePackage = module.CreatePackage("SourceResourcesPackage");
                IAssetPackage targetPackage = module.CreatePackage("TargetResourcesPackage");
                await sourcePackage.InitializeAsync(new AssetPackageInitOptions());
                await targetPackage.InitializeAsync(new AssetPackageInitOptions());
                using IAssetHandle<GameObject> source = CreateOwnedPrefabLease(sourcePackage, sourceObject);

                Assert.Throws<ArgumentException>(() => targetPackage.InstantiateAsync(source));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sourceObject);
                await module.DestroyAsync();
            }
        }

        private static IAssetHandle<GameObject> CreateOwnedPrefabLease(
            IAssetPackage package,
            GameObject sourceObject)
        {
            var owner = (ResourcesAssetPackage)package;
            AssetCacheKey key = AssetCacheService.BuildCacheKey(
                $"Tests/Prefab/{sourceObject.GetInstanceID()}",
                typeof(GameObject));
            ResourcesAssetHandle<GameObject> backend = ResourcesAssetHandle<GameObject>.Create(
                AssetRuntimeGuard.NextHandleId(),
                key,
                sourceObject,
                null,
                owner,
                owner.OperationTails);
            return AssetHandleLeases.Create(backend);
        }

        private static AssetCacheService GetCacheService(ResourcesAssetPackage package)
        {
            FieldInfo field = typeof(ResourcesAssetPackage).GetField(
                "_cacheService",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (AssetCacheService)field.GetValue(package);
        }

        private sealed class FailOnceCacheHandle : IReferenceCounted, IInternalCacheable
        {
            public int RefCount { get; private set; } = 1;
            public int ForceDisposeCount { get; private set; }

            public void Retain() => RefCount++;
            public void Release() => RefCount--;
            public void Dispose() => Release();

            void IInternalCacheable.ForceDispose()
            {
                ForceDisposeCount++;
                if (ForceDisposeCount == 1)
                {
                    throw new InvalidOperationException("Synthetic Resources cleanup failure.");
                }
            }
        }

        [Test]
        public void Resource_Instance_Destroy_Failure_Preserves_Strong_Ownership_For_Retry()
        {
            var instance = new GameObject("ResourcesDestroyFailureOwnershipTests");
            var operationTails = new AssetOperationTailTracker();
            int destroyAttempts = 0;
            int disposedCallbackCount = 0;
            ResourcesInstantiateHandle handle = ResourcesInstantiateHandle.Create(
                AssetRuntimeGuard.NextHandleId(),
                instance,
                _ => disposedCallbackCount++,
                operationTails,
                target =>
                {
                    destroyAttempts++;
                    if (destroyAttempts == 1)
                    {
                        throw new InvalidOperationException("Synthetic Unity destroy request failure.");
                    }

                    UnityEngine.Object.DestroyImmediate(target);
                },
                _ => UniTask.CompletedTask);

            try
            {
                Assert.Throws<InvalidOperationException>(() => handle.Dispose());
                Assert.That(destroyAttempts, Is.EqualTo(1));
                Assert.That(handle.Instance, Is.SameAs(instance));
                Assert.That(handle.RefCount, Is.EqualTo(1));
                Assert.That(disposedCallbackCount, Is.Zero);
                Assert.That(operationTails.PendingCount, Is.Zero);

                Assert.DoesNotThrow(handle.Dispose);
                Assert.That(destroyAttempts, Is.EqualTo(2));
                Assert.IsTrue(handle.Instance == null);
                Assert.That(handle.RefCount, Is.Zero);
                Assert.That(disposedCallbackCount, Is.EqualTo(1));
            }
            finally
            {
                if (instance != null)
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }
        }

        [Test]
        public async Task Resource_Instance_Release_Waits_For_The_Destroy_Barrier()
        {
            var instance = new GameObject("ResourcesDestroyBarrierOwnershipTests");
            var destroyBarrier = new UniTaskCompletionSource();
            var operationTails = new AssetOperationTailTracker();
            int destroyRequestCount = 0;
            int disposedCallbackCount = 0;
            ResourcesInstantiateHandle handle = ResourcesInstantiateHandle.Create(
                AssetRuntimeGuard.NextHandleId(),
                instance,
                _ => disposedCallbackCount++,
                operationTails,
                _ => destroyRequestCount++,
                _ => destroyBarrier.Task);

            try
            {
                UniTask release = handle.DisposeInternal();

                Assert.That(release.Status, Is.EqualTo(UniTaskStatus.Pending));
                Assert.That(destroyRequestCount, Is.EqualTo(1));
                Assert.That(handle.Instance, Is.SameAs(instance));
                Assert.That(disposedCallbackCount, Is.Zero);
                Assert.That(operationTails.PendingCount, Is.EqualTo(1));

                UnityEngine.Object.DestroyImmediate(instance);
                destroyBarrier.TrySetResult();
                await release;

                Assert.IsTrue(handle.Instance == null);
                Assert.That(handle.RefCount, Is.Zero);
                Assert.That(disposedCallbackCount, Is.EqualTo(1));
                Assert.That(operationTails.PendingCount, Is.Zero);
            }
            finally
            {
                if (instance != null)
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }
            }
        }

    }
}
