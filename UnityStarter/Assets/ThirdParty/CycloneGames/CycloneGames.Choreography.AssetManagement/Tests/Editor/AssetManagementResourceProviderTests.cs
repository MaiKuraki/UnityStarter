using System.Threading;
using Cysharp.Threading.Tasks;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.Choreography.Core;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.Choreography.AssetManagement.Tests
{
    public sealed class AssetManagementResourceProviderTests
    {
        private sealed class TestAsset : ScriptableObject
        {
        }

        private sealed class ControlledAssetHandle : IAssetHandle<UnityEngine.Object>
        {
            public bool IsDoneValue;
            public UnityEngine.Object AssetValue;
            public string ErrorValue;
            public int DisposeCount;

            public UnityEngine.Object Asset => AssetValue;
            public UnityEngine.Object AssetObject => AssetValue;
            public bool IsDone => IsDoneValue;
            public float Progress => IsDoneValue ? 1f : 0.5f;
            public string Error => ErrorValue;
            public UniTask Task => UniTask.CompletedTask;

            public void Dispose()
            {
                DisposeCount++;
            }

            public void WaitForAsyncComplete()
            {
            }
        }

        private sealed class ControlledAssetPackage : IAssetPackage
        {
            public readonly ControlledAssetHandle Handle = new ControlledAssetHandle();
            public int LoadCount;

            public string Name => "ChoreographyTests";

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
                CancellationToken cancellationToken = default)
                where TAsset : UnityEngine.Object
            {
                LoadCount++;
                return (IAssetHandle<TAsset>)(object)Handle;
            }

            public IInstantiateHandle InstantiateAsync(
                IAssetHandle<GameObject> handle,
                Transform parent = null,
                bool worldPositionStays = false,
                bool setActive = true)
            {
                return null;
            }

            public bool IsAssetCached<TAsset>(string location) where TAsset : UnityEngine.Object
            {
                return false;
            }

            public UniTask UnloadUnusedAssetsAsync()
            {
                return UniTask.CompletedTask;
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

        [Test]
        public void PendingRequest_RemainsUntilBackendHandleCompletionAndEndsExactlyOnce()
        {
            var package = new ControlledAssetPackage();
            var provider = new AssetManagementResourceProvider(package);
            var reference = new ChoreographyResourceReference("asset", ChoreographyResourceKind.Generic);
            IChoreographyResourceHandle lease = provider.Load(in reference);

            Assert.That(provider.GetMemoryStats().PendingRequestCount, Is.EqualTo(1));

            TestAsset asset = ScriptableObject.CreateInstance<TestAsset>();
            try
            {
                package.Handle.AssetValue = asset;
                package.Handle.IsDoneValue = true;

                Assert.That(provider.GetMemoryStats().PendingRequestCount, Is.Zero);
                Assert.That(lease.IsDone, Is.True);
                Assert.That(provider.GetMemoryStats().PendingRequestCount, Is.Zero);
                Assert.That(lease.Succeeded, Is.True);
                Assert.That(lease.Succeeded, Is.True);

                ChoreographyAssetManagementMemoryStats stats = provider.GetMemoryStats();
                Assert.That(stats.PendingRequestCount, Is.Zero);
                Assert.That(stats.FailedRequestCount, Is.Zero);
            }
            finally
            {
                lease.Release();
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void PendingRequest_LastLeaseReleaseEndsPendingWithoutDoubleDispose()
        {
            var package = new ControlledAssetPackage();
            var provider = new AssetManagementResourceProvider(package);
            var reference = new ChoreographyResourceReference("asset", ChoreographyResourceKind.Generic);
            IChoreographyResourceHandle lease = provider.Load(in reference);

            lease.Release();
            lease.Release();

            ChoreographyAssetManagementMemoryStats stats = provider.GetMemoryStats();
            Assert.That(stats.PendingRequestCount, Is.Zero);
            Assert.That(stats.ActiveLeaseCount, Is.Zero);
            Assert.That(stats.RetainedRequestCount, Is.Zero);
            Assert.That(package.Handle.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void PendingRequest_ReusedLeasesShareOneBackendLifecycle()
        {
            var package = new ControlledAssetPackage();
            var provider = new AssetManagementResourceProvider(package);
            var reference = new ChoreographyResourceReference("shared", ChoreographyResourceKind.Generic);
            IChoreographyResourceHandle first = provider.Load(in reference);
            IChoreographyResourceHandle second = provider.Load(in reference);

            Assert.That(second, Is.SameAs(first));
            Assert.That(package.LoadCount, Is.EqualTo(1));
            Assert.That(provider.GetMemoryStats().PendingRequestCount, Is.EqualTo(1));
            Assert.That(provider.GetMemoryStats().ActiveLeaseCount, Is.EqualTo(2));

            TestAsset asset = ScriptableObject.CreateInstance<TestAsset>();
            try
            {
                package.Handle.AssetValue = asset;
                package.Handle.IsDoneValue = true;
                Assert.That(first.IsDone, Is.True);
                Assert.That(second.IsDone, Is.True);
                Assert.That(provider.GetMemoryStats().PendingRequestCount, Is.Zero);
            }
            finally
            {
                first.Release();
                second.Release();
                Object.DestroyImmediate(asset);
            }

            Assert.That(package.Handle.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void FailedCompletion_IsCountedOnce()
        {
            var package = new ControlledAssetPackage();
            var provider = new AssetManagementResourceProvider(package);
            var reference = new ChoreographyResourceReference("missing", ChoreographyResourceKind.Generic);
            IChoreographyResourceHandle lease = provider.Load(in reference);
            package.Handle.ErrorValue = "missing";
            package.Handle.IsDoneValue = true;

            Assert.That(lease.IsDone, Is.True);
            Assert.That(lease.Succeeded, Is.False);
            Assert.That(lease.Succeeded, Is.False);

            ChoreographyAssetManagementMemoryStats stats = provider.GetMemoryStats();
            Assert.That(stats.PendingRequestCount, Is.Zero);
            Assert.That(stats.FailedRequestCount, Is.EqualTo(1));
            lease.Release();
        }

        [Test]
        public void Provider_PreservesLegacyClrConstructor()
        {
            Assert.That(
                typeof(AssetManagementResourceProvider).GetConstructor(new[]
                {
                    typeof(IAssetPackage),
                    typeof(string)
                }),
                Is.Not.Null);
        }
    }
}
