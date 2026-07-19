using CycloneGames.AssetManagement.Runtime;
using CycloneGames.AssetManagement.Runtime.Cache;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.AssetManagement.Tests.Editor
{
    public sealed class AssetRuntimeDiagnosticsTests
    {
        [Test]
        public void Snapshot_Computes_Idle_Budget_Usage()
        {
            var snapshot = new AssetRuntimeCacheSnapshot(
                "Default",
                "Test",
                activeCount: 2,
                idleCount: 3,
                idleBytesApprox: 50L,
                idleBytesBudget: 100L);

            Assert.AreEqual(0.5f, snapshot.IdleBudgetUsage);
            Assert.IsFalse(snapshot.IsIdleBudgetExceeded);
        }

        [Test]
        public void ResourcesPackage_Exposes_Runtime_Diagnostics()
        {
            var module = new ResourcesModule();
            try
            {
                module.InitializeAsync().GetAwaiter().GetResult();
                IAssetPackage package = module.CreatePackage("Default");
                package.InitializeAsync(new AssetPackageInitOptions()).GetAwaiter().GetResult();

                var diagnostics = package as IAssetRuntimeDiagnostics;

                Assert.IsNotNull(diagnostics);

                AssetRuntimeCacheSnapshot snapshot = diagnostics.GetRuntimeCacheSnapshot();
                Assert.AreEqual("Default", snapshot.PackageName);
                Assert.AreEqual("Resources", snapshot.ProviderName);
                Assert.AreEqual(0, snapshot.ActiveCount);
                Assert.AreEqual(0, snapshot.IdleCount);
                Assert.Greater(snapshot.IdleBytesBudget, 0L);
            }
            finally
            {
                module.DestroyAsync().GetAwaiter().GetResult();
            }
        }

        [Test]
        public void CacheSnapshot_Tracks_Active_And_Idle_Handles()
        {
            var package = new RecordingAssetPackage();
            var clock = new ManualAssetCacheClock();
            using var cache = new AssetCacheService(package, maxTrialEntries: 4, maxMainEntries: 4, maxIdleBytes: 64L * 1024 * 1024, clock);
            AssetCacheKey cacheKey = AssetCacheService.BuildCacheKey("Assets/Test/Icon.png", typeof(Texture2D), AssetCacheEntryKind.Asset);
            var handle = new FootprintAssetHandle(4096L);

            cache.RegisterNew(cacheKey, bucket: "UI", tag: "Icon", owner: "Test", handle);

            AssetRuntimeCacheSnapshot activeSnapshot = cache.CreateRuntimeSnapshot("Default", "Test");
            Assert.AreEqual(1, activeSnapshot.ActiveCount);
            Assert.AreEqual(0, activeSnapshot.IdleCount);
            Assert.AreEqual(0L, activeSnapshot.IdleBytesApprox);

            handle.Release();
            cache.OnHandleReleased(cacheKey, handle);

            AssetRuntimeCacheSnapshot idleSnapshot = cache.CreateRuntimeSnapshot("Default", "Test");
            Assert.AreEqual(0, idleSnapshot.ActiveCount);
            Assert.AreEqual(1, idleSnapshot.IdleCount);
            Assert.AreEqual(4096L, idleSnapshot.IdleBytesApprox);
            Assert.AreEqual(64L * 1024 * 1024, idleSnapshot.IdleBytesBudget);
        }

        [Test]
        public void CacheSnapshot_Tracks_Lifetime_Activity_And_Eviction_Reasons()
        {
            var package = new RecordingAssetPackage();
            using var cache = new AssetCacheService(
                package,
                maxTrialEntries: 4,
                maxMainEntries: 4,
                maxIdleBytes: 64L * 1024 * 1024);
            AssetCacheKey key = AssetCacheService.BuildCacheKey("Assets/Test/Telemetry.png", typeof(Texture2D));

            Assert.IsNull(cache.Get(key, null, null, null));
            var handle = new TestAssetHandle<Texture2D>();
            cache.RegisterNew(key, null, null, null, handle);

            IReferenceCounted activeHit = cache.Get(key, null, null, null);
            activeHit.Release();
            handle.Release();
            cache.OnHandleReleased(key, handle);

            IReferenceCounted idleHit = cache.Get(key, null, null, null);
            idleHit.Release();
            cache.OnHandleReleased(key, handle);
            Assert.AreEqual(1, cache.TrimIdle(AssetCacheRetentionPolicy.EvictAllIdle));

            AssetRuntimeCacheSnapshot snapshot = cache.CreateRuntimeSnapshot("Default", "Test");
            Assert.AreEqual(3L, snapshot.CacheLookupCount);
            Assert.AreEqual(2L, snapshot.CacheHitCount);
            Assert.AreEqual(1L, snapshot.ActiveHitCount);
            Assert.AreEqual(1L, snapshot.IdleHitCount);
            Assert.AreEqual(1L, snapshot.CacheMissCount);
            Assert.AreEqual(2d / 3d, snapshot.CacheHitRatio, 0.000001d);
            Assert.AreEqual(2L, snapshot.IdleAdmissionCount);
            Assert.AreEqual(0L, snapshot.AdmissionRejectionCount);
            Assert.AreEqual(1L, snapshot.EvictionCount);
            Assert.AreEqual(1L, snapshot.RetentionEvictionCount);
            Assert.AreEqual(1L, snapshot.EvictedBytesApprox);
            Assert.AreEqual(1, snapshot.PeakActiveCount);
            Assert.AreEqual(1, snapshot.PeakIdleCount);
            Assert.AreEqual(1L, snapshot.PeakIdleBytesApprox);
        }

        [Test]
        public void CacheSnapshot_Distinguishes_Capacity_And_Memory_Evictions()
        {
            var package = new RecordingAssetPackage();
            using (var capacityCache = new AssetCacheService(
                package,
                maxTrialEntries: 1,
                maxMainEntries: 1,
                maxIdleBytes: 64L * 1024 * 1024))
            {
                AdmitOneByteHandle(capacityCache, "Capacity/A");
                AdmitOneByteHandle(capacityCache, "Capacity/B");

                AssetRuntimeCacheSnapshot snapshot = capacityCache.CreateRuntimeSnapshot("Default", "Test");
                Assert.AreEqual(1L, snapshot.EvictionCount);
                Assert.AreEqual(1L, snapshot.CapacityEvictionCount);
                Assert.AreEqual(0L, snapshot.MemoryBudgetEvictionCount);
            }

            const long idleBudget = 1L * 1024 * 1024;
            using (var memoryCache = new AssetCacheService(
                package,
                maxTrialEntries: 4,
                maxMainEntries: 4,
                maxIdleBytes: idleBudget))
            {
                AdmitSizedHandle(memoryCache, "Memory/A", 700L * 1024L);
                AdmitSizedHandle(memoryCache, "Memory/B", 700L * 1024L);

                AssetRuntimeCacheSnapshot snapshot = memoryCache.CreateRuntimeSnapshot("Default", "Test");
                Assert.AreEqual(1L, snapshot.EvictionCount);
                Assert.AreEqual(0L, snapshot.CapacityEvictionCount);
                Assert.AreEqual(1L, snapshot.MemoryBudgetEvictionCount);
                Assert.AreEqual(700L * 1024L, snapshot.EvictedBytesApprox);
            }
        }

        private static void AdmitOneByteHandle(AssetCacheService cache, string location)
        {
            AssetCacheKey key = AssetCacheService.BuildCacheKey(location, typeof(Texture2D));
            var handle = new TestAssetHandle<Texture2D>();
            cache.RegisterNew(key, null, null, null, handle);
            handle.Release();
            cache.OnHandleReleased(key, handle);
        }

        private static void AdmitSizedHandle(AssetCacheService cache, string location, long estimatedBytes)
        {
            AssetCacheKey key = AssetCacheService.BuildCacheKey(location, typeof(Texture2D));
            var handle = new FootprintAssetHandle(estimatedBytes);
            cache.RegisterNew(key, null, null, null, handle);
            handle.Release();
            cache.OnHandleReleased(key, handle);
        }

        private sealed class ManualAssetCacheClock : IAssetCacheClock
        {
            public long Timestamp => 0L;

            public System.TimeSpan GetElapsed(long startTimestamp, long endTimestamp)
            {
                return System.TimeSpan.Zero;
            }
        }

        private sealed class FootprintAssetHandle : IAssetHandle<Texture2D>, IReferenceCounted, IAssetMemoryFootprint
        {
            private readonly long _estimatedBytes;

            public FootprintAssetHandle(long estimatedBytes)
            {
                _estimatedBytes = estimatedBytes;
            }

            public Texture2D Asset => null;
            public Object AssetObject => null;
            public bool IsDone => true;
            public float Progress => 1f;
            public string Error => null;
            public Cysharp.Threading.Tasks.UniTask Task => Cysharp.Threading.Tasks.UniTask.CompletedTask;
            public int RefCount { get; private set; } = 1;

            public void Retain()
            {
                RefCount++;
            }

            public void Release()
            {
                RefCount--;
            }

            public void Dispose()
            {
                Release();
            }

            public void WaitForAsyncComplete()
            {
            }

            public long EstimateRuntimeBytes()
            {
                return _estimatedBytes;
            }
        }
    }
}
