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
            module.InitializeAsync().GetAwaiter().GetResult();
            IAssetPackage package = module.CreatePackage("Default");

            var diagnostics = package as IAssetRuntimeDiagnostics;

            Assert.IsNotNull(diagnostics);

            AssetRuntimeCacheSnapshot snapshot = diagnostics.GetRuntimeCacheSnapshot();
            Assert.AreEqual("Default", snapshot.PackageName);
            Assert.AreEqual("Resources", snapshot.ProviderName);
            Assert.AreEqual(0, snapshot.ActiveCount);
            Assert.AreEqual(0, snapshot.IdleCount);
            Assert.Greater(snapshot.IdleBytesBudget, 0L);

            module.DestroyAsync().GetAwaiter().GetResult();
        }

        [Test]
        public void CacheSnapshot_Tracks_Active_And_Idle_Handles()
        {
            var package = new RecordingAssetPackage();
            var clock = new ManualAssetCacheClock();
            using var cache = new AssetCacheService(package, maxTrialEntries: 4, maxMainEntries: 4, maxIdleBytes: 64L * 1024 * 1024, clock);
            string cacheKey = AssetCacheService.BuildCacheKey("Assets/Test/Icon.png", typeof(Texture2D), AssetCacheOperationKind.Asset);
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
