using System;
using System.Collections.Generic;
using System.IO;

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
        private const string ActiveCacheHitSampleGroup = "AssetCache.ActiveCacheHit.Time";
        private const string IdleFullTrimSampleGroup = "AssetCache.IdleFullTrim.Time";
        private const string BoundedDiagnosticsSampleGroup = "AssetCache.BoundedDiagnostics.Time";

        private const int ActiveCacheHitIterations = 50_000;
        private const int IdleFullTrimEntryCount = 10_000;
        private const int BoundedDiagnosticsEntryCount = 20_000;
        private const int BoundedDiagnosticsCaptureLimit = 4_096;

        // Relative regression gate. The absolute reference is read from the B0/B1 baseline file;
        // no absolute time or byte budget is hard-coded in this assembly.
        private const float TimeRegressionFactor = 1.5f;
        private const int ThresholdWarmupCount = 3;
        private const int ThresholdSampleCount = 5;

        private static string BaselineFilePath => Path.GetFullPath(
            Path.Combine(Application.dataPath, "..", "PerformanceBaselines",
                "asset-cache-performance-baseline.json"));

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
                .IterationsPerMeasurement(ActiveCacheHitIterations)
                .SampleGroup(new SampleGroup(ActiveCacheHitSampleGroup, SampleUnit.Millisecond))
                .GC()
                .Run();

            // Every iteration performs Get (auto-Retain) followed by Release, so the net refcount effect
            // is zero. The fixture state must be unchanged after sampling.
            Assert.AreEqual(1, cache.ActiveCount,
                "The measurement loop must not change the number of active cache entries.");
            Assert.AreEqual(1, handle.RefCount,
                "The measurement loop must leave the handle refcount exactly at its registered value.");
            Assert.AreEqual(cache.ActiveCount, handle.RefCount,
                "Active entry count and handle refcount must stay in exact agreement after the measurement loop.");

            float measuredMilliseconds = MeasureElapsedMilliseconds(
                () =>
                {
                    IReferenceCounted acquired = cache.Get(key, "Benchmark", "Hot", "Runner");
                    acquired.Release();
                },
                ActiveCacheHitIterations);
            AssertRelativeTimeThreshold(ActiveCacheHitSampleGroup, measuredMilliseconds);

            handle.Release();
            cache.OnHandleReleased(key, handle);
        }

        [Test, Performance]
        public void Idle_Cache_Full_Trim_Benchmark()
        {
            var package = new RecordingAssetPackage();
            using var cache = new AssetCacheService(
                package,
                IdleFullTrimEntryCount,
                IdleFullTrimEntryCount,
                2L * 1024 * 1024 * 1024);

            for (int i = 0; i < IdleFullTrimEntryCount; i++)
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
                .SampleGroup(new SampleGroup(IdleFullTrimSampleGroup, SampleUnit.Millisecond))
                .GC()
                .Run();

            Assert.AreEqual(0, cache.IdleCount);

            // TrimIdle is destructive, so the relative-threshold sample uses a fresh equivalent fixture
            // instead of re-trimming the already-emptied cache used by the sampling run above.
            float measuredMilliseconds = MeasureIdleFullTrimMilliseconds();
            AssertRelativeTimeThreshold(IdleFullTrimSampleGroup, measuredMilliseconds);
        }

        [Test, Performance]
        public void Bounded_Diagnostics_Capture_Benchmark()
        {
            var package = new RecordingAssetPackage();
            using var cache = new AssetCacheService(package, 64, 64, 512L * 1024 * 1024);
            var active = new List<AssetCacheService.CacheDiagnosticEntry>(BoundedDiagnosticsCaptureLimit);
            var probation = new List<AssetCacheService.CacheDiagnosticEntry>(1);
            var protectedEntries = new List<AssetCacheService.CacheDiagnosticEntry>(1);
            AssetCacheService.CacheDiagnosticCapture capture = default;

            for (int i = 0; i < BoundedDiagnosticsEntryCount; i++)
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
                        BoundedDiagnosticsCaptureLimit,
                        0,
                        0);
                })
                .WarmupCount(2)
                .MeasurementCount(10)
                .IterationsPerMeasurement(1)
                .SampleGroup(new SampleGroup(BoundedDiagnosticsSampleGroup, SampleUnit.Millisecond))
                .GC()
                .Run();

            Assert.AreEqual(BoundedDiagnosticsEntryCount, capture.ActiveTotal);
            Assert.AreEqual(BoundedDiagnosticsCaptureLimit, capture.ActiveCaptured);
            Assert.IsTrue(capture.IsTruncated);
            Assert.AreEqual(BoundedDiagnosticsCaptureLimit, active.Count);

            float measuredMilliseconds = MeasureElapsedMilliseconds(
                () => capture = cache.GetDiagnostics(
                    active,
                    probation,
                    protectedEntries,
                    BoundedDiagnosticsCaptureLimit,
                    0,
                    0),
                1);
            AssertRelativeTimeThreshold(BoundedDiagnosticsSampleGroup, measuredMilliseconds);
        }

        private static float MeasureElapsedMilliseconds(Action action, int iterationsPerSample)
        {
            for (int i = 0; i < ThresholdWarmupCount; i++)
            {
                action();
            }

            float best = float.MaxValue;
            for (int i = 0; i < ThresholdSampleCount; i++)
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                for (int iteration = 0; iteration < iterationsPerSample; iteration++)
                {
                    action();
                }
                stopwatch.Stop();
                float elapsed = (float)stopwatch.Elapsed.TotalMilliseconds;
                if (elapsed < best)
                {
                    best = elapsed;
                }
            }

            return best;
        }

        private static float MeasureIdleFullTrimMilliseconds()
        {
            var package = new RecordingAssetPackage();
            using var cache = new AssetCacheService(
                package,
                IdleFullTrimEntryCount,
                IdleFullTrimEntryCount,
                2L * 1024 * 1024 * 1024);
            for (int i = 0; i < IdleFullTrimEntryCount; i++)
            {
                AssetCacheKey key = AssetCacheService.BuildCacheKey($"Benchmark/Idle/{i}", typeof(Texture2D));
                var handle = new BenchmarkHandle();
                cache.RegisterNew(key, "Benchmark.Idle", null, null, handle);
                handle.Release();
                cache.OnHandleReleased(key, handle);
            }

            // A trimmed fixture cannot be re-trimmed, so a single fresh-fixture sample is timed with no
            // warmup loop. The B0/B1 baseline uses the same single-measurement shape for this operation.
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            cache.TrimIdle(AssetCacheRetentionPolicy.EvictAllIdle);
            stopwatch.Stop();
            return (float)stopwatch.Elapsed.TotalMilliseconds;
        }

        private static void AssertRelativeTimeThreshold(string sampleGroupName, float measuredMilliseconds)
        {
            if (!TryLoadBaseline(out PerformanceBaselineFile baseline, out string reason))
            {
                TestContext.WriteLine($"[PerformanceThreshold] {sampleGroupName}: Not run ({reason}).");
                return;
            }

            float referenceMilliseconds = -1f;
            for (int i = 0; i < baseline.samples.Length; i++)
            {
                PerformanceBaselineEntry entry = baseline.samples[i];
                if (entry != null && entry.name == sampleGroupName && entry.milliseconds > 0f)
                {
                    referenceMilliseconds = entry.milliseconds;
                    break;
                }
            }

            if (referenceMilliseconds <= 0f)
            {
                TestContext.WriteLine(
                    $"[PerformanceThreshold] {sampleGroupName}: Not run (no positive time baseline entry).");
                return;
            }

            float limitMilliseconds = referenceMilliseconds * TimeRegressionFactor;
            Assert.That(
                measuredMilliseconds,
                Is.LessThanOrEqualTo(limitMilliseconds),
                $"{sampleGroupName} measured {measuredMilliseconds:F3} ms, exceeding " +
                $"{TimeRegressionFactor:F2}x the B0/B1 baseline of {referenceMilliseconds:F3} ms " +
                $"(limit {limitMilliseconds:F3} ms).");
        }

        private static bool TryLoadBaseline(out PerformanceBaselineFile baseline, out string reason)
        {
            baseline = null;
            reason = null;
            if (!File.Exists(BaselineFilePath))
            {
                reason = $"baseline file absent at '{BaselineFilePath}'";
                return false;
            }

            try
            {
                baseline = JsonUtility.FromJson<PerformanceBaselineFile>(File.ReadAllText(BaselineFilePath));
                if (baseline == null || baseline.samples == null)
                {
                    baseline = null;
                    reason = "baseline file has no samples";
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                baseline = null;
                reason = $"baseline file could not be read ({exception.GetType().Name})";
                return false;
            }
        }

        // JsonUtility populates these fields through Unity serialization rather than C# assignment.
#pragma warning disable CS0649
        [Serializable]
        private sealed class PerformanceBaselineFile
        {
            public PerformanceBaselineEntry[] samples;
        }

        [Serializable]
        private sealed class PerformanceBaselineEntry
        {
            public string name;
            public float milliseconds;
        }
#pragma warning restore CS0649

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
