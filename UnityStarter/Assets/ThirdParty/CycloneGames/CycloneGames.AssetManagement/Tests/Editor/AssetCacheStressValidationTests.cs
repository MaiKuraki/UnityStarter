using System;
using System.Collections.Generic;
using System.Threading;

using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

using CycloneGames.AssetManagement.Runtime;
using CycloneGames.AssetManagement.Runtime.Cache;

namespace CycloneGames.AssetManagement.Tests.Editor
{
    /// <summary>
    /// Capacity, pulse and long-run stability validation for the segmented-LRU cache.
    /// Every case drives the real <see cref="AssetCacheService"/> through its internal surface
    /// (available via InternalsVisibleTo) rather than mocking the cache itself.
    /// </summary>
    public sealed class AssetCacheStressValidationTests
    {
        // ------------------------------------------------------------------ capacity grid

        [Test]
        public void CapacityGrid_EntryCeilingIsNeverExceeded(
            [Values(1, 4, 64, 1024)] int trialCapacity,
            [Values(1, 8, 256)] int mainCapacity,
            [Values(10, 1000, 20000)] int registrations)
        {
            var package = new RecordingAssetPackage();
            using (var cache = new AssetCacheService(package, trialCapacity, mainCapacity, 64L * 1024 * 1024))
            {
                var keys = new List<AssetCacheKey>(registrations);
                var handles = new List<CountingHandle>(registrations);
                for (int i = 0; i < registrations; i++)
                {
                    AssetCacheKey key = AssetCacheService.BuildCacheKey(
                        "Assets/Stress/Asset_" + i.ToString(),
                        typeof(Texture2D),
                        AssetCacheEntryKind.Asset);
                    var handle = new CountingHandle();
                    keys.Add(key);
                    handles.Add(handle);
                    cache.RegisterNew(key, bucket: "B" + (i % 16).ToString(), tag: null, owner: null, handle);
                }

                Assert.That(cache.ActiveCount, Is.EqualTo(registrations),
                    "Every registered handle must stay pinned while retained.");

                for (int i = 0; i < keys.Count; i++)
                {
                    ReleaseHandle(cache, keys[i], handles[i]);
                }

                int idleAfterRelease = cache.IdleCount;
                Assert.That(cache.ActiveCount, Is.EqualTo(0), "No handle may stay active after release.");
                Assert.That(
                    idleAfterRelease,
                    Is.LessThanOrEqualTo(trialCapacity + mainCapacity),
                    $"Idle entries ({idleAfterRelease}) exceeded trial+main ceiling ({trialCapacity + mainCapacity}).");

                cache.TrimIdle(AssetCacheRetentionPolicy.EvictAllIdle);
                Assert.That(cache.IdleCount, Is.EqualTo(0), "EvictAllIdle must drain every idle entry.");
            }

            Assert.That(CountingHandle.LiveCount, Is.EqualTo(0), "Every provider handle must be disposed exactly once.");
        }

        // ------------------------------------------------------------------ pulse resistance

        [Test]
        public void PulseBursts_LeaveNoResidueAndNoStuckHandles(
            [Values(8, 32, 128)] int burstSize,
            [Values(20, 100)] int bursts)
        {
            var package = new RecordingAssetPackage();
            using (var cache = new AssetCacheService(package, 16, 64, 8L * 1024 * 1024))
            {
                for (int b = 0; b < bursts; b++)
                {
                    var keys = new AssetCacheKey[burstSize];
                    var handles = new CountingHandle[burstSize];
                    for (int i = 0; i < burstSize; i++)
                    {
                        AssetCacheKey key = AssetCacheService.BuildCacheKey(
                            "Assets/Pulse/B" + b.ToString() + "/" + i.ToString(),
                            typeof(Texture2D),
                            AssetCacheEntryKind.Asset);
                        var handle = new CountingHandle();
                        keys[i] = key;
                        handles[i] = handle;
                        cache.RegisterNew(key, bucket: "Pulse", tag: null, owner: null, handle);
                    }

                    for (int i = 0; i < burstSize; i++)
                    {
                        ReleaseHandle(cache, keys[i], handles[i]);
                    }

                    cache.TrimIdle(AssetCacheRetentionPolicy.EvictAllIdle);

                    Assert.That(cache.ActiveCount, Is.EqualTo(0), "Burst " + b + " left active handles.");
                    Assert.That(cache.IdleCount, Is.EqualTo(0), "Burst " + b + " left idle entries after trim.");
                }
            }

            Assert.That(CountingHandle.LiveCount, Is.EqualTo(0), "Provider handles leaked across bursts.");
        }

        // ------------------------------------------------------------------ SLRU segmentation

        [Test]
        public void SegmentedLru_ReusedEntryIsPromotedToProtectedSegment()
        {
            var package = new RecordingAssetPackage();
            using (var cache = new AssetCacheService(package, 4, 4, 64L * 1024 * 1024))
            {
                AssetCacheKey reused = AssetCacheService.BuildCacheKey("Assets/Seg/Reused", typeof(Texture2D), AssetCacheEntryKind.Asset);
                AssetCacheKey single = AssetCacheService.BuildCacheKey("Assets/Seg/Single", typeof(Texture2D), AssetCacheEntryKind.Asset);

                var reusedHandle = new CountingHandle();
                cache.RegisterNew(reused, null, null, null, reusedHandle);
                ReleaseHandle(cache, reused, reusedHandle);

                // Second acquisition then release => AccessCount > 1 => protected segment on re-entry.
                IReferenceCounted again = cache.Get(reused, null, null, null);
                Assert.That(again, Is.Not.Null, "Second acquisition must hit the probation segment.");
                ReleaseHandle(cache, reused, again);

                var singleHandle = new CountingHandle();
                cache.RegisterNew(single, null, null, null, singleHandle);
                ReleaseHandle(cache, single, singleHandle);

                var active = new List<AssetCacheService.CacheDiagnosticEntry>();
                var probation = new List<AssetCacheService.CacheDiagnosticEntry>();
                var protectedSegment = new List<AssetCacheService.CacheDiagnosticEntry>();
                cache.GetDiagnostics(active, probation, protectedSegment);

                Assert.That(IndexIn(protectedSegment, reused), Is.GreaterThanOrEqualTo(0),
                    "Reused entry must live in the protected segment.");
                Assert.That(IndexIn(probation, reused), Is.EqualTo(-1),
                    "Reused entry must not remain in probation.");
                Assert.That(IndexIn(probation, single), Is.GreaterThanOrEqualTo(0),
                    "First-use entry must stay in probation.");
                Assert.That(active.Count, Is.EqualTo(cache.ActiveCount));
            }
        }

        [Test]
        public void SegmentedLru_ProtectedOverflowDemotesInsteadOfEvicting()
        {
            var package = new RecordingAssetPackage();
            using (var cache = new AssetCacheService(package, 16, 2, 64L * 1024 * 1024))
            {
                var keys = new AssetCacheKey[6];
                var handles = new CountingHandle[6];
                for (int i = 0; i < keys.Length; i++)
                {
                    AssetCacheKey key = AssetCacheService.BuildCacheKey("Assets/Demote/" + i.ToString(), typeof(Texture2D), AssetCacheEntryKind.Asset);
                    var handle = new CountingHandle();
                    keys[i] = key;
                    handles[i] = handle;
                    cache.RegisterNew(key, null, null, null, handle);
                    ReleaseHandle(cache, key, handle);
                    IReferenceCounted again = cache.Get(key, null, null, null);
                    ReleaseHandle(cache, key, again);
                }

                var active = new List<AssetCacheService.CacheDiagnosticEntry>();
                var probation = new List<AssetCacheService.CacheDiagnosticEntry>();
                var protectedSegment = new List<AssetCacheService.CacheDiagnosticEntry>();
                cache.GetDiagnostics(active, probation, protectedSegment);

                Assert.That(protectedSegment.Count, Is.LessThanOrEqualTo(2),
                    "Protected segment must respect its ceiling.");
                Assert.That(probation.Count, Is.GreaterThan(0),
                    "Overflow from protected must demote into probation, not disappear.");
                Assert.That(cache.IdleCount, Is.EqualTo(probation.Count + protectedSegment.Count),
                    "Idle count must equal probation + protected.");
            }
        }

        // ------------------------------------------------------------------ bucket mass eviction

        [Test]
        public void BucketEviction_RemovesOnlyMatchingBucketAndIsIdempotent()
        {
            var package = new RecordingAssetPackage();
            // Capacity must exceed the registration count, otherwise capacity eviction would legitimately
            // remove part of the target bucket before ClearBucket runs and the assertion would be meaningless.
            using (var cache = new AssetCacheService(package, 1024, 1024, 64L * 1024 * 1024))
            {
                for (int i = 0; i < 600; i++)
                {
                    AssetCacheKey key = AssetCacheService.BuildCacheKey("Assets/Bucket/" + i.ToString(), typeof(Texture2D), AssetCacheEntryKind.Asset);
                    var handle = new CountingHandle();
                    cache.RegisterNew(key, bucket: "Scene." + (i % 6).ToString(), tag: null, owner: null, handle);
                    ReleaseHandle(cache, key, handle);
                }

                int before = cache.IdleCount;
                cache.ClearBucket("Scene.3");
                int after = cache.IdleCount;

                Assert.That(after, Is.LessThan(before), "Clearing a populated bucket must evict entries.");
                Assert.That(before - after, Is.EqualTo(100), "Exactly the 100 entries of Scene.3 must be evicted.");

                cache.ClearBucket("Scene.3");
                Assert.That(cache.IdleCount, Is.EqualTo(after), "Repeated ClearBucket must be a no-op.");
            }
        }

        // ------------------------------------------------------------------ long-run stability

        [Test]
        public void LongRunCycles_ShowBoundedHeapGrowth()
        {
            var package = new RecordingAssetPackage();
            using (var cache = new AssetCacheService(package, 64, 256, 32L * 1024 * 1024))
            {
                const int warmupCycles = 20;
                const int measuredCycles = 200;
                const int perCycle = 200;

                for (int c = 0; c < warmupCycles; c++)
                {
                    RunCycle(cache, c, perCycle);
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                long baseline = GC.GetTotalMemory(true);

                for (int c = 0; c < measuredCycles; c++)
                {
                    RunCycle(cache, warmupCycles + c, perCycle);
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                long after = GC.GetTotalMemory(true);

                long growth = after - baseline;
                // 200 cycles x 200 registrations = 40k operation objects. A retained-node leak would grow
                // by megabytes; measured noise floor on this host is 0-16 KiB per probe.
                Assert.That(growth, Is.LessThan(2L * 1024 * 1024),
                    $"Heap grew by {growth} bytes across {measuredCycles} cycles - possible retained-node leak.");
                Assert.That(cache.ActiveCount, Is.EqualTo(0), "Long run left active handles.");
                Assert.That(cache.IdleCount, Is.LessThanOrEqualTo(64 + 256), "Long run exceeded capacity ceiling.");
            }

            Assert.That(CountingHandle.LiveCount, Is.EqualTo(0), "Long run leaked provider handles.");
        }

        // ------------------------------------------------------------------ diagnostics consistency

        [Test]
        public void Diagnostics_StayConsistentWithLiveCounters(
            [Values(1, 100, 5000)] int entryCount)
        {
            var package = new RecordingAssetPackage();
            using (var cache = new AssetCacheService(package, 128, 512, 16L * 1024 * 1024))
            {
                var keys = new List<AssetCacheKey>(entryCount);
                var handles = new List<CountingHandle>(entryCount);
                for (int i = 0; i < entryCount; i++)
                {
                    AssetCacheKey key = AssetCacheService.BuildCacheKey("Assets/Diag/" + i.ToString(), typeof(Texture2D), AssetCacheEntryKind.Asset);
                    var handle = new CountingHandle();
                    keys.Add(key);
                    handles.Add(handle);
                    cache.RegisterNew(key, bucket: "Diag", tag: "T", owner: "O", handle);
                }

                var active = new List<AssetCacheService.CacheDiagnosticEntry>();
                var probation = new List<AssetCacheService.CacheDiagnosticEntry>();
                var protectedSegment = new List<AssetCacheService.CacheDiagnosticEntry>();
                cache.GetDiagnostics(active, probation, protectedSegment);

                Assert.That(active.Count, Is.EqualTo(cache.ActiveCount), "Active diagnostics must match ActiveCount.");
                Assert.That(active[0].Bucket, Is.EqualTo("Diag"), "Bucket metadata must survive registration.");
                Assert.That(active[0].Tag, Is.EqualTo("T"));
                Assert.That(active[0].Owner, Is.EqualTo("O"));
                Assert.That(active[0].RefCount, Is.EqualTo(1));

                for (int i = 0; i < keys.Count; i++)
                {
                    ReleaseHandle(cache, keys[i], handles[i]);
                }

                cache.GetDiagnostics(active, probation, protectedSegment);
                Assert.That(active.Count, Is.EqualTo(0), "Released handles must leave the active snapshot.");
                Assert.That(probation.Count + protectedSegment.Count, Is.EqualTo(cache.IdleCount),
                    "Probation+protected diagnostics must add up to IdleCount.");
            }
        }

        [Test]
        public void Diagnostics_BoundedCaptureRespectsLimits(
            [Values(0, 1, 7, 64)] int limit)
        {
            var package = new RecordingAssetPackage();
            using (var cache = new AssetCacheService(package, 256, 256, 16L * 1024 * 1024))
            {
                for (int i = 0; i < 200; i++)
                {
                    AssetCacheKey key = AssetCacheService.BuildCacheKey("Assets/Bound/" + i.ToString(), typeof(Texture2D), AssetCacheEntryKind.Asset);
                    var handle = new CountingHandle();
                    cache.RegisterNew(key, null, null, null, handle);
                    ReleaseHandle(cache, key, handle);
                }

                var active = new List<AssetCacheService.CacheDiagnosticEntry>();
                var probation = new List<AssetCacheService.CacheDiagnosticEntry>();
                var protectedSegment = new List<AssetCacheService.CacheDiagnosticEntry>();
                AssetCacheService.CacheDiagnosticCapture capture =
                    cache.GetDiagnostics(active, probation, protectedSegment, limit, limit, limit);

                Assert.That(active.Count, Is.LessThanOrEqualTo(limit));
                Assert.That(probation.Count, Is.LessThanOrEqualTo(limit));
                Assert.That(protectedSegment.Count, Is.LessThanOrEqualTo(limit));
                Assert.That(capture.IsTruncated,
                    Is.EqualTo(active.Count < capture.ActiveTotal ||
                               probation.Count < capture.ProbationTotal ||
                               protectedSegment.Count < capture.ProtectedTotal),
                    "Truncation flag must reflect whether the capture actually dropped rows.");
            }
        }

        // ------------------------------------------------------------------ hot-path allocation

        [Test]
        public void HotPathCacheHitAndRelease_StayWithinHeapNoiseFloor()
        {
            var package = new RecordingAssetPackage();
            using (var cache = new AssetCacheService(package, 512, 512, 64L * 1024 * 1024))
            {
                const int keyCount = 256;
                const int warmupRounds = 200;
                const int measuredRounds = 2000;

                var keys = new AssetCacheKey[keyCount];
                var handles = new CountingHandle[keyCount];
                for (int i = 0; i < keyCount; i++)
                {
                    keys[i] = AssetCacheService.BuildCacheKey("Assets/Hot/" + i.ToString(), typeof(Texture2D), AssetCacheEntryKind.Asset);
                    handles[i] = new CountingHandle();
                    cache.RegisterNew(keys[i], bucket: "Hot", tag: "T", owner: "O", handles[i]);
                    HandleRelease(cache, keys[i], handles[i]);
                }

                for (int r = 0; r < warmupRounds; r++)
                {
                    RunHotRound(cache, keys, handles);
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                long baseline = GC.GetTotalMemory(true);

                for (int r = 0; r < measuredRounds; r++)
                {
                    RunHotRound(cache, keys, handles);
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                long growth = GC.GetTotalMemory(true) - baseline;
                long perRound = growth / measuredRounds;

                TestContext.WriteLine(
                    "[HotPathAlloc] rounds=" + measuredRounds +
                    " keysPerRound=" + keyCount +
                    " growthBytes=" + growth +
                    " bytesPerRound=" + perRound);

                // Get + OnHandleReleased must not allocate per operation. A single boxing or closure per
                // operation would show up as >= 8 bytes x 256 keys x 2000 rounds = ~4 MiB.
                Assert.That(growth, Is.LessThan(256L * 1024),
                    $"Hot path grew {growth} bytes over {measuredRounds} rounds ({perRound} B/round) - per-operation allocation suspected.");
                Assert.That(cache.ActiveCount, Is.EqualTo(0));
                Assert.That(cache.IdleCount, Is.EqualTo(keyCount));
            }

            Assert.That(CountingHandle.LiveCount, Is.EqualTo(0));
        }

        /// <summary>
        /// One hot round: every key is acquired from the idle cache and released again. This is the
        /// steady-state path a running game exercises thousands of times per second.
        /// </summary>
        private static void RunHotRound(AssetCacheService cache, AssetCacheKey[] keys, CountingHandle[] handles)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                IReferenceCounted acquired = cache.Get(keys[i], bucket: "Hot", tag: "T", owner: "O");
                if (acquired == null)
                {
                    cache.RegisterNew(keys[i], bucket: "Hot", tag: "T", owner: "O", handles[i]);
                    acquired = handles[i];
                }

                acquired.Release();
                cache.OnHandleReleased(keys[i], acquired);
            }
        }

        /// <summary>
        /// Release helper used by the setup loop; the caller drops the last reference before the cache
        /// sees the key (the revival guard ignores callbacks while RefCount is still positive).
        /// </summary>
        private static void HandleRelease(AssetCacheService cache, AssetCacheKey key, IReferenceCounted handle)
        {
            handle.Release();
            cache.OnHandleReleased(key, handle);
        }

        // ------------------------------------------------------------------ helpers


        /// <summary>
        /// Mirrors the real ownership chain: the caller drops the last reference, then the release
        /// callback hands the key back to the cache. The callback is a no-op while the reference
        /// count is non-zero (revival guard), so the reference must be dropped first.
        /// </summary>
        private static void ReleaseHandle(AssetCacheService cache, AssetCacheKey key, IReferenceCounted handle)
        {
            handle.Release();
            cache.OnHandleReleased(key, handle);
        }

        private static void RunCycle(AssetCacheService cache, int cycle, int perCycle)
        {
            var keys = new AssetCacheKey[perCycle];
            var handles = new CountingHandle[perCycle];
            for (int i = 0; i < perCycle; i++)
            {
                AssetCacheKey key = AssetCacheService.BuildCacheKey(
                    "Assets/LongRun/" + cycle.ToString() + "/" + i.ToString(),
                    typeof(Texture2D),
                    AssetCacheEntryKind.Asset);
                var handle = new CountingHandle();
                keys[i] = key;
                handles[i] = handle;
                cache.RegisterNew(key, bucket: "Cycle", tag: "T", owner: "O", handle);
            }

            for (int i = 0; i < perCycle; i++)
            {
                ReleaseHandle(cache, keys[i], handles[i]);
            }

            cache.TrimIdleStep(64);
        }

        private static int IndexIn(List<AssetCacheService.CacheDiagnosticEntry> rows, AssetCacheKey key)
        {
            string expected = key.ToDiagnosticString();
            for (int i = 0; i < rows.Count; i++)
            {
                if (string.Equals(rows[i].CacheKey, expected, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Tracks live instances so a leaked provider handle fails the case instead of hiding in the finalizer queue.
        /// </summary>
        private sealed class CountingHandle : IAssetHandle<Texture2D>, IReferenceCounted, IAssetMemoryFootprint
        {
            private static int s_liveCount;
            private int _disposed;

            public static int LiveCount => Volatile.Read(ref s_liveCount);

            public CountingHandle()
            {
                Interlocked.Increment(ref s_liveCount);
            }

            public Texture2D Asset => null;
            public UnityEngine.Object AssetObject => null;
            public bool IsDone => true;
            public float Progress => 1f;
            public string Error => null;
            public UniTask Task => UniTask.CompletedTask;
            public int RefCount { get; private set; } = 1;

            public void Retain() => RefCount++;
            public void Release() => RefCount--;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    Interlocked.Decrement(ref s_liveCount);
                }
            }

            public void WaitForAsyncComplete() { }
            long IAssetMemoryFootprint.EstimateRuntimeBytes() => 1024L;
        }
    }
}
