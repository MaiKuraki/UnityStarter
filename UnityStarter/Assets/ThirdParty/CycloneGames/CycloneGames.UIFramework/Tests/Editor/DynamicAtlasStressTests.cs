using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using CycloneGames.UIFramework.DynamicAtlas;

namespace CycloneGames.UIFramework.Tests.Editor
{
    public sealed class DynamicAtlasStressTests
    {
        [Test]
        public void Churn_Stability_10kCycles()
        {
            const int cycles = 10000;
            const int keyPoolSize = 200;
            Texture2D source = CreateTexture(8, 8, TextureFormat.RGBA32, Color.white);
            DynamicAtlasConfig config = DynamicAtlasConfigTests.CreateSmallConfig();
            Assert.That(config.maxMissesPerFrame, Is.Zero);
            Assert.That(config.retentionPolicy, Is.EqualTo(DynamicAtlasRetentionPolicy.RetainUntilCapacityPressure));
            using var service = new DynamicAtlasService(config);
            string[] keys = new string[keyPoolSize];
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i] = "churn/" + i;
            }

            var random = new System.Random(20240521);

            try
            {
                int unexpectedStatusCount = 0;
                // One batch collapses per-insert Texture.Apply uploads into a single flush;
                // upload cost is orthogonal to the pool and eviction invariants under test.
                using (DynamicAtlasWriteBatch batch = service.BeginBatch())
                {
                    for (int i = 0; i < cycles; i++)
                    {
                        DynamicAtlasInsertStatus status = service.TryAcquire(
                            keys[random.Next(keys.Length)],
                            source,
                            out DynamicAtlasSpriteLease lease);
                        if (status != DynamicAtlasInsertStatus.Success &&
                            status != DynamicAtlasInsertStatus.CacheHit)
                        {
                            unexpectedStatusCount++;
                        }

                        lease.Dispose();
                    }
                }

                Assert.That(unexpectedStatusCount, Is.Zero);

                DynamicAtlasStats stats = service.GetStats();
                Assert.That(stats.PageCount, Is.LessThanOrEqualTo(config.maxPages));
                Assert.That(stats.EntryCount, Is.LessThanOrEqualTo(config.maxEntries));
                Assert.That(stats.ActiveReferenceCount, Is.Zero);
                Assert.That(stats.ActiveEntryCount, Is.Zero);
                Assert.That(stats.RetainedEntryCount, Is.EqualTo(stats.EntryCount));
                Assert.That(stats.CacheHitCount + stats.CacheMissCount, Is.EqualTo(cycles));
                Assert.That(stats.InsertCount, Is.EqualTo(stats.CacheMissCount));
                Assert.That(stats.RejectionCount, Is.Zero);
                // The key pool is far larger than the entry capacity, so churn must have
                // exercised eviction; zero would mean the loop never stressed the pool.
                Assert.That(stats.EvictionCount, Is.GreaterThan(0));
            }
            finally
            {
                Destroy(source);
            }
        }

        [Test]
        public void CacheHit_AcquireDispose_Loop_ZeroAlloc()
        {
            const int cycles = 10000;
            Texture2D source = CreateTexture(8, 8, TextureFormat.RGBA32, Color.white);
            using var service = new DynamicAtlasService(DynamicAtlasConfigTests.CreateSmallConfig());

            try
            {
                // Warm the entry and the lease-record pool so the measured loop only
                // exercises the pooled cache-hit path.
                AssertSuccessful(service.TryAcquire("zero-alloc/hot", source, out DynamicAtlasSpriteLease warmup));
                warmup.Dispose();

                bool allCacheHits = true;
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < cycles; i++)
                {
                    DynamicAtlasInsertStatus status = service.TryAcquire(
                        "zero-alloc/hot",
                        source,
                        out DynamicAtlasSpriteLease lease);
                    allCacheHits &= status == DynamicAtlasInsertStatus.CacheHit;
                    lease.Dispose();
                }

                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

                Assert.That(allCacheHits, Is.True);
                Assert.That(allocated, Is.Zero);
                Assert.That(service.GetStats().CacheHitCount, Is.EqualTo(cycles));
            }
            finally
            {
                Destroy(source);
            }
        }

        [Test]
        public void Spike_Throttled_BoundedWork()
        {
            const int missBudget = 4;
            const int spikeSize = 50;
            int frame = 500;
            int loadCount = 0;
            Texture2D source = CreateTexture(8, 8, TextureFormat.RGBA32, Color.white);
            DynamicAtlasConfig config = DynamicAtlasConfigTests.CreateSmallConfig();
            config.maxMissesPerFrame = missBudget;
            config.loadFunc = _ =>
            {
                loadCount++;
                return source;
            };
            config.unloadFunc = (_, __) => { };
            using var service = new DynamicAtlasService(config);
            TestReflection.SetField(service, "_frameCountProvider", (Func<int>)(() => frame));

            try
            {
                var leases = new DynamicAtlasSpriteLease[missBudget];
                int successCount = 0;
                int throttledCount = 0;
                for (int i = 0; i < spikeSize; i++)
                {
                    DynamicAtlasInsertStatus status = service.TryAcquireLocation(
                        "spike/" + i,
                        out DynamicAtlasSpriteLease lease);
                    if (status == DynamicAtlasInsertStatus.Success)
                    {
                        leases[successCount++] = lease;
                    }
                    else if (status == DynamicAtlasInsertStatus.Throttled)
                    {
                        throttledCount++;
                        Assert.That(lease.IsValid, Is.False);
                    }
                    else
                    {
                        Assert.Fail($"Expected Success or Throttled for spike/{i}, got {status}.");
                    }
                }

                Assert.That(successCount, Is.EqualTo(missBudget));
                Assert.That(throttledCount, Is.EqualTo(spikeSize - missBudget));
                // Throttled misses never reach the loader, so per-frame work stays bounded.
                Assert.That(loadCount, Is.EqualTo(missBudget));
                Assert.That(service.GetStats().RejectionCount, Is.EqualTo(spikeSize - missBudget));

                // The next frame resets the budget: a previously throttled key now inserts.
                frame++;
                AssertSuccessful(service.TryAcquireLocation(
                    "spike/" + (spikeSize - 1),
                    out DynamicAtlasSpriteLease retried));
                Assert.That(loadCount, Is.EqualTo(missBudget + 1));

                retried.Dispose();
                for (int i = 0; i < successCount; i++)
                {
                    leases[i].Dispose();
                }
            }
            finally
            {
                Destroy(source);
            }
        }

        [Test]
        public void BatchFlush_AmortizesUploads()
        {
            const int insertCount = 16;
            Texture2D source = CreateTexture(8, 8, TextureFormat.RGBA32, Color.white);
            using var service = new DynamicAtlasService(DynamicAtlasConfigTests.CreateSmallConfig());
            // CPU-backed pages take the raw-copy path, which defers Texture.Apply behind the
            // pending-upload flag; GPU-only pages upload per copy and cannot show deferral.
            Assert.That(service.PrewarmPages(2, DynamicAtlasPageMode.CpuBacked), Is.EqualTo(2));

            try
            {
                long copiesBefore = CountCopies(service.GetStats());
                var leases = new DynamicAtlasSpriteLease[insertCount];
                using (DynamicAtlasWriteBatch batch = service.BeginBatch())
                {
                    for (int i = 0; i < insertCount; i++)
                    {
                        AssertSuccessful(service.TryAcquire("batch/" + i, source, out leases[i]));
                    }

                    // All copies ran inside the batch, yet every page still defers its upload.
                    Assert.That(CountCopies(service.GetStats()) - copiesBefore, Is.EqualTo(insertCount));
                    Assert.That(CountPagesWithPendingUpload(service), Is.GreaterThan(0));
                }

                // Disposing the outermost batch flushed every deferred upload at once.
                Assert.That(CountPagesWithPendingUpload(service), Is.Zero);

                for (int i = 0; i < insertCount; i++)
                {
                    leases[i].Dispose();
                }

                // Control: outside a batch the same kind of insert flushes immediately.
                AssertSuccessful(service.TryAcquire("batch/control", source, out DynamicAtlasSpriteLease control));
                Assert.That(CountPagesWithPendingUpload(service), Is.Zero);
                control.Dispose();
            }
            finally
            {
                Destroy(source);
            }
        }

        private static long CountCopies(DynamicAtlasStats stats)
        {
            return stats.GpuCopyCount + stats.CpuRawCopyCount + stats.SynchronousReadbackCount;
        }

        private static int CountPagesWithPendingUpload(DynamicAtlasService service)
        {
            var pages = (IList)GetField(service, "_pages");
            int count = 0;
            foreach (object page in pages)
            {
                PropertyInfo property = page.GetType().GetProperty(
                    "HasPendingUpload",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null && (bool)property.GetValue(page))
                {
                    count++;
                }
            }

            return count;
        }

        private static object GetField(object target, string fieldName)
        {
            Type type = target.GetType();
            while (type != null)
            {
                FieldInfo field = type.GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    return field.GetValue(target);
                }

                type = type.BaseType;
            }

            throw new MissingFieldException(target.GetType().FullName, fieldName);
        }

        private static Texture2D CreateTexture(
            int width,
            int height,
            TextureFormat format,
            Color color)
        {
            var texture = new Texture2D(width, height, format, mipChain: false);
            var pixels = new Color32[width * height];
            Color32 pixel = color;
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = pixel;
            }

            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return texture;
        }

        private static void AssertSuccessful(DynamicAtlasInsertStatus status)
        {
            Assert.That(
                status == DynamicAtlasInsertStatus.Success || status == DynamicAtlasInsertStatus.CacheHit,
                Is.True,
                $"Expected a successful dynamic atlas insertion, got {status}.");
        }

        private static void Destroy(UnityEngine.Object target)
        {
            if (target != null)
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
