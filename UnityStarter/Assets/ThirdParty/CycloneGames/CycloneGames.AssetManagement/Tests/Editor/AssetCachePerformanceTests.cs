using System.Collections.Generic;

using Cysharp.Threading.Tasks;

using NUnit.Framework;

using Unity.PerformanceTesting;

using UnityEngine;

using CycloneGames.AssetManagement.Runtime;
using CycloneGames.AssetManagement.Runtime.Cache;

namespace CycloneGames.AssetManagement.Tests.Editor
{
    public sealed class AssetCachePerformanceTests
    {
        [Test, Performance]
        public void Active_Cache_Hit_Retain_Release_Benchmark()
        {
            var package = new RecordingAssetPackage();
            using var cache = new AssetCacheService(package, 64, 512, 512L * 1024 * 1024);
            AssetCacheKey key = AssetCacheService.BuildCacheKey("Benchmark/Hot", typeof(Texture2D));
            var handle = new BenchmarkHandle();
            cache.RegisterNew(key, "Benchmark", "Hot", "Runner", handle);

            Measure.Method(() =>
                {
                    IReferenceCounted acquired = cache.Get(key, "Benchmark", "Hot", "Runner");
                    acquired.Release();
                })
                .WarmupCount(5)
                .MeasurementCount(20)
                .IterationsPerMeasurement(50_000)
                .GC()
                .Run();

            handle.Release();
            cache.OnHandleReleased(key, handle);
        }

        [Test, Performance]
        public void Idle_Cache_Full_Trim_Benchmark()
        {
            const int entryCount = 10_000;
            var package = new RecordingAssetPackage();
            using var cache = new AssetCacheService(
                package,
                entryCount,
                entryCount,
                2L * 1024 * 1024 * 1024);

            for (int i = 0; i < entryCount; i++)
            {
                AssetCacheKey key = AssetCacheService.BuildCacheKey($"Benchmark/Idle/{i}", typeof(Texture2D));
                var handle = new BenchmarkHandle();
                cache.RegisterNew(key, "Benchmark.Idle", null, null, handle);
                handle.Release();
                cache.OnHandleReleased(key, handle);
            }

            Measure.Method(() => cache.TrimIdle(AssetCacheRetentionPolicy.EvictAllIdle))
                .MeasurementCount(1)
                .IterationsPerMeasurement(1)
                .GC()
                .Run();

            Assert.AreEqual(0, cache.IdleCount);
        }

        [Test, Performance]
        public void Bounded_Diagnostics_Capture_Benchmark()
        {
            const int entryCount = 20_000;
            const int captureLimit = 4_096;
            var package = new RecordingAssetPackage();
            using var cache = new AssetCacheService(package, 64, 64, 512L * 1024 * 1024);
            var active = new List<AssetCacheService.CacheDiagnosticEntry>(captureLimit);
            var probation = new List<AssetCacheService.CacheDiagnosticEntry>(1);
            var protectedEntries = new List<AssetCacheService.CacheDiagnosticEntry>(1);
            AssetCacheService.CacheDiagnosticCapture capture = default;

            for (int i = 0; i < entryCount; i++)
            {
                AssetCacheKey key = AssetCacheService.BuildCacheKey($"Benchmark/Diagnostics/{i}", typeof(Texture2D));
                cache.RegisterNew(key, "Benchmark.Diagnostics", null, null, new BenchmarkHandle());
            }

            Measure.Method(() =>
                {
                    capture = cache.GetDiagnostics(
                        active,
                        probation,
                        protectedEntries,
                        captureLimit,
                        0,
                        0);
                })
                .WarmupCount(2)
                .MeasurementCount(10)
                .IterationsPerMeasurement(1)
                .GC()
                .Run();

            Assert.AreEqual(entryCount, capture.ActiveTotal);
            Assert.AreEqual(captureLimit, capture.ActiveCaptured);
            Assert.IsTrue(capture.IsTruncated);
            Assert.AreEqual(captureLimit, active.Count);
        }

        private sealed class BenchmarkHandle : IAssetHandle<Texture2D>, IReferenceCounted, IInternalCacheable,
            IAssetMemoryFootprint
        {
            public Texture2D Asset => null;
            public UnityEngine.Object AssetObject => null;
            public bool IsDone => true;
            public float Progress => 1f;
            public string Error => null;
            public UniTask Task => UniTask.CompletedTask;
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
                RefCount = 0;
            }

            public void WaitForAsyncComplete()
            {
            }

            public void ForceDispose()
            {
                RefCount = 0;
            }

            long IAssetMemoryFootprint.EstimateRuntimeBytes()
            {
                return 1L;
            }
        }
    }
}
