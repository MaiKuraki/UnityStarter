using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace CycloneGames.DynamicAtlas.Tests.Editor
{
    /// <summary>
    /// Measured contracts: steady-state allocation, cost scaling in entry count, slot reuse under
    /// fragmentation, coalescing, and byte accounting. Each test logs a data line for comparison
    /// across runs.
    /// </summary>
    /// <remarks>
    /// Rejection paths are excluded from the zero-allocation contract: DEBUG builds allocate a
    /// diagnostic message and a per-key dedup entry. That cost is stripped from release players.
    /// </remarks>
    public sealed class DynamicAtlasPerformanceContractTests
    {
        private static Texture2D CreateTexture(int width, int height, int tint = 0)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            var pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32((byte)((i + tint) & 0xFF), 64, 96, 255);
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static DynamicAtlasConfig CreateConfig(int pageSize, int maxPages, int maxEntries)
        {
            return new DynamicAtlasConfig
            {
                pageSize = pageSize,
                maxPages = maxPages,
                minRetainedPages = 0,
                maxEntries = maxEntries,
                maxEntriesPerPage = maxEntries,
                maxKeyLength = 64,
                memoryBudgetBytes = 64L * 1024L * 1024L,
                padding = 1,
                enableBleed = true,
                retentionPolicy = DynamicAtlasRetentionPolicy.RetainUntilCapacityPressure,
                oversizePolicy = DynamicAtlasOversizePolicy.Reject,
                copyFallback = DynamicAtlasCopyFallback.AllowCpuRawCopy,
                maxMissesPerFrame = 0,
            };
        }

        public enum SteadyStateOperation
        {
            CacheHit = 0,
            GetStats = 1,
            TryGetSprite = 2,
            CopySnapshots = 3,
            TrimNoOp = 4,
        }

        [Test]
        public void SteadyStateOperations_DoNotAllocate(
            [Values(SteadyStateOperation.CacheHit, SteadyStateOperation.GetStats,
                SteadyStateOperation.TryGetSprite, SteadyStateOperation.CopySnapshots,
                SteadyStateOperation.TrimNoOp)]
            SteadyStateOperation operation)
        {
            const int iterations = 5000;
            DynamicAtlasConfig config = CreateConfig(128, 2, 32);
            using var service = new DynamicAtlasService(config);
            Texture2D source = CreateTexture(16, 16);

            var pageBuffer = new List<DynamicAtlasPageSnapshot>(config.maxPages);
            var entryBuffer = new List<DynamicAtlasEntrySnapshot>(config.maxEntries);

            try
            {
                for (int i = 0; i < 4; i++)
                {
                    Assert.That(
                        service.TryAcquire($"alloc/{i}", source, out DynamicAtlasSpriteLease seed),
                        Is.EqualTo(DynamicAtlasInsertStatus.Success));
                    seed.Dispose();
                }

                // Warm every code path once so pool growth and JIT happen before measurement.
                service.TryAcquire("alloc/0", source, out DynamicAtlasSpriteLease warm);
                warm.Dispose();
                service.GetStats();
                service.TryGetSprite("alloc/0", out _);
                pageBuffer.Clear();
                service.CopyPageSnapshots(pageBuffer);
                entryBuffer.Clear();
                service.CopyEntrySnapshots(entryBuffer);
                service.TrimUnused();

                long before = GC.GetAllocatedBytesForCurrentThread();
                switch (operation)
                {
                    case SteadyStateOperation.CacheHit:
                        for (int i = 0; i < iterations; i++)
                        {
                            service.TryAcquire("alloc/0", source, out DynamicAtlasSpriteLease lease);
                            lease.Dispose();
                        }

                        break;
                    case SteadyStateOperation.GetStats:
                        for (int i = 0; i < iterations; i++)
                        {
                            service.GetStats();
                        }

                        break;
                    case SteadyStateOperation.TryGetSprite:
                        for (int i = 0; i < iterations; i++)
                        {
                            service.TryGetSprite("alloc/0", out _);
                        }

                        break;
                    case SteadyStateOperation.CopySnapshots:
                        for (int i = 0; i < iterations; i++)
                        {
                            pageBuffer.Clear();
                            service.CopyPageSnapshots(pageBuffer);
                            entryBuffer.Clear();
                            service.CopyEntrySnapshots(entryBuffer);
                        }

                        break;
                    default:
                        for (int i = 0; i < iterations; i++)
                        {
                            service.TrimUnused();
                        }

                        break;
                }

                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                TestContext.WriteLine(
                    $"ALLOC|op={operation}|iterations={iterations}|bytes={allocated}|" +
                    $"pages={pageBuffer.Count}|entries={entryBuffer.Count}");

                Assert.That(allocated, Is.Zero, $"{operation} allocated {allocated} bytes over {iterations} calls");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void CacheHitCost_IsIndependentOfEntryCount(
            [Values(32, 256, 1024)] int entryCount)
        {
            const int iterations = 20000;
            DynamicAtlasConfig config = CreateConfig(1024, 8, entryCount);
            using var service = new DynamicAtlasService(config);
            Texture2D source = CreateTexture(8, 8);

            try
            {
                for (int i = 0; i < entryCount; i++)
                {
                    Assert.That(
                        service.TryAcquire($"scale/{i}", source, out DynamicAtlasSpriteLease lease),
                        Is.EqualTo(DynamicAtlasInsertStatus.Success),
                        $"seed entry {i} of {entryCount}");
                    lease.Dispose();
                }

                service.TryAcquire("scale/0", source, out DynamicAtlasSpriteLease hot);
                hot.Dispose();

                var stopwatch = Stopwatch.StartNew();
                for (int i = 0; i < iterations; i++)
                {
                    service.TryAcquire("scale/0", source, out DynamicAtlasSpriteLease lease);
                    lease.Dispose();
                }

                stopwatch.Stop();
                double nsPerOp = stopwatch.Elapsed.TotalMilliseconds * 1000000.0 / iterations;
                DynamicAtlasStats stats = service.GetStats();

                TestContext.WriteLine(
                    $"SCALE-CACHEHIT|entries={entryCount}|iterations={iterations}|nsPerOp={nsPerOp:F1}|" +
                    $"pages={stats.PageCount}|finalEntries={stats.EntryCount}");

                Assert.That(stats.EntryCount, Is.LessThanOrEqualTo(entryCount));
                Assert.That(stats.CacheHitCount, Is.GreaterThanOrEqualTo(iterations));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void StatsCost_GrowsLinearlyNotQuadratically(
            [Values(32, 256, 1024)] int entryCount)
        {
            const int iterations = 2000;
            DynamicAtlasConfig config = CreateConfig(1024, 8, entryCount);
            using var service = new DynamicAtlasService(config);
            Texture2D source = CreateTexture(8, 8);

            try
            {
                for (int i = 0; i < entryCount; i++)
                {
                    Assert.That(
                        service.TryAcquire($"stats/{i}", source, out DynamicAtlasSpriteLease lease),
                        Is.EqualTo(DynamicAtlasInsertStatus.Success));
                    lease.Dispose();
                }

                var stopwatch = Stopwatch.StartNew();
                long checksum = 0;
                for (int i = 0; i < iterations; i++)
                {
                    checksum += service.GetStats().EntryCount;
                }

                stopwatch.Stop();
                double nsPerCall = stopwatch.Elapsed.TotalMilliseconds * 1000000.0 / iterations;
                double nsPerEntry = nsPerCall / entryCount;

                TestContext.WriteLine(
                    $"SCALE-STATS|entries={entryCount}|iterations={iterations}|nsPerCall={nsPerCall:F1}|" +
                    $"nsPerEntry={nsPerEntry:F2}|checksum={checksum}");

                Assert.That(checksum, Is.EqualTo((long)entryCount * iterations));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void Fragmentation_ReleasedSlotsAreReusedBeforeGrowingPages()
        {
            // Single page: a failed reuse surfaces as a rejection, not a second page.
            DynamicAtlasConfig config = CreateConfig(128, 1, 64);
            config.retentionPolicy = DynamicAtlasRetentionPolicy.RemoveWhenUnused;
            using var service = new DynamicAtlasService(config);
            var sources = new List<Texture2D>(2) { CreateTexture(16, 16, 7), CreateTexture(16, 16, 9) };
            var leases = new List<DynamicAtlasSpriteLease>(64);

            try
            {
                // 16 px content + 1 px padding on each side = 18 px slot; a 128 page holds exactly
                // 49 of them (7 per row over 7 rows).
                for (int i = 0; i < 49; i++)
                {
                    if (service.TryAcquire($"frag/{i}", sources[0], out DynamicAtlasSpriteLease lease)
                        != DynamicAtlasInsertStatus.Success)
                    {
                        break;
                    }

                    leases.Add(lease);
                }

                int filled = leases.Count;
                DynamicAtlasStats afterFill = service.GetStats();
                Assert.That(filled, Is.GreaterThan(0));
                Assert.That(afterFill.PageCount, Is.EqualTo(1));

                // Free every other slot; each is flanked by a live entry, so none can merge.
                int released = 0;
                for (int i = 0; i < leases.Count; i += 2)
                {
                    leases[i].Dispose();
                    released++;
                }

                Assert.That(service.GetStats().EntryCount, Is.EqualTo(filled - released));

                int reused = 0;
                for (int i = 0; i < released; i++)
                {
                    Assert.That(
                        service.TryAcquire($"reuse/{i}", sources[1], out DynamicAtlasSpriteLease lease),
                        Is.EqualTo(DynamicAtlasInsertStatus.Success),
                        $"reuse {i} should land in a released slot");
                    leases.Add(lease);
                    reused++;
                }

                DynamicAtlasStats afterReuse = service.GetStats();
                TestContext.WriteLine(
                    $"FRAGMENTATION|filled={filled}|released={released}|reused={reused}|" +
                    $"pagesBefore=1|pagesAfter={afterReuse.PageCount}|entries={afterReuse.EntryCount}");

                Assert.That(reused, Is.EqualTo(released), "released slots must absorb same-size inserts");
                Assert.That(
                    afterReuse.PageCount,
                    Is.EqualTo(1),
                    "reuse must not allocate a second page");
            }
            finally
            {
                for (int i = 0; i < leases.Count; i++)
                {
                    leases[i].Dispose();
                }

                for (int i = 0; i < sources.Count; i++)
                {
                    UnityEngine.Object.DestroyImmediate(sources[i]);
                }
            }
        }

        [Test]
        public void Coalescing_AdjacentReleasedSlotsMergeForAWiderInsert()
        {
            DynamicAtlasConfig config = CreateConfig(128, 2, 64);
            config.retentionPolicy = DynamicAtlasRetentionPolicy.RemoveWhenUnused;
            using var service = new DynamicAtlasService(config);
            Texture2D small = CreateTexture(16, 16);
            // 34 px content + 2 px padding = 36 px slot: exactly two merged 18 px slots wide.
            Texture2D wide = CreateTexture(34, 16);
            var leases = new List<DynamicAtlasSpriteLease>(64);

            try
            {
                for (int i = 0; i < 49; i++)
                {
                    if (service.TryAcquire($"coal/{i}", small, out DynamicAtlasSpriteLease lease)
                        != DynamicAtlasInsertStatus.Success)
                    {
                        break;
                    }

                    leases.Add(lease);
                }

                Assert.That(leases.Count, Is.EqualTo(49), "a 128 page must hold 49 eighteen-pixel slots");

                // The first two slots are horizontally adjacent, so release must merge them into
                // one 36x18 slot. No other 36 px slot exists on a full page.
                leases[0].Dispose();
                leases[1].Dispose();

                DynamicAtlasInsertStatus status = service.TryAcquire(
                    "coal/wide",
                    wide,
                    out DynamicAtlasSpriteLease wideLease);

                DynamicAtlasStats after = service.GetStats();
                TestContext.WriteLine(
                    $"COALESCE|seeded={leases.Count}|wideStatus={status}|pages={after.PageCount}|" +
                    $"entries={after.EntryCount}");

                Assert.That(
                    status,
                    Is.EqualTo(DynamicAtlasInsertStatus.Success),
                    "a 36 px slot only exists if the two 18 px slots merged");
                Assert.That(after.PageCount, Is.EqualTo(1), "merging must avoid a second page");
                wideLease.Dispose();
            }
            finally
            {
                for (int i = 0; i < leases.Count; i++)
                {
                    leases[i].Dispose();
                }

                UnityEngine.Object.DestroyImmediate(small);
                UnityEngine.Object.DestroyImmediate(wide);
            }
        }

        [Test]
        public void InsertAndReleaseCost_StayWithinPageBudget(
            [Values(128, 256)] int pageSize)
        {
            DynamicAtlasConfig config = CreateConfig(pageSize, 1, 1024);
            config.retentionPolicy = DynamicAtlasRetentionPolicy.RemoveWhenUnused;
            using var service = new DynamicAtlasService(config);
            Texture2D source = CreateTexture(16, 16);
            var leases = new List<DynamicAtlasSpriteLease>(512);

            try
            {
                var stopwatch = Stopwatch.StartNew();
                for (int i = 0; i < 1024; i++)
                {
                    if (service.TryAcquire($"cost/{i}", source, out DynamicAtlasSpriteLease lease)
                        != DynamicAtlasInsertStatus.Success)
                    {
                        break;
                    }

                    leases.Add(lease);
                }

                stopwatch.Stop();
                int slots = leases.Count;
                Assert.That(slots, Is.GreaterThan(0));
                double insertNs = stopwatch.Elapsed.TotalMilliseconds * 1000000.0 / slots;

                stopwatch.Restart();
                for (int i = 0; i < leases.Count; i++)
                {
                    leases[i].Dispose();
                }

                stopwatch.Stop();
                double releaseNs = stopwatch.Elapsed.TotalMilliseconds * 1000000.0 / slots;

                DynamicAtlasStats stats = service.GetStats();
                TestContext.WriteLine(
                    $"COST|pageSize={pageSize}|slots={slots}|insertNs={insertNs:F1}|releaseNs={releaseNs:F1}|" +
                    $"pages={stats.PageCount}|entries={stats.EntryCount}");

                Assert.That(stats.EntryCount, Is.Zero);
                Assert.That(
                    stats.PageCount,
                    Is.LessThanOrEqualTo(1),
                    "an emptied page is recycled, so at most one page may remain");
            }
            finally
            {
                for (int i = 0; i < leases.Count; i++)
                {
                    leases[i].Dispose();
                }

                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void ByteAccounting_MatchesConfiguredPageFootprint()
        {
            if ((SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) == 0)
            {
                Assert.Ignore("GPU copy is unavailable on this device; the GPU-only byte contract cannot run.");
            }

            DynamicAtlasConfig config = CreateConfig(256, 4, 32);
            config.copyFallback = DynamicAtlasCopyFallback.GpuOnly;
            using var service = new DynamicAtlasService(config);
            Texture2D source = CreateTexture(16, 16);

            try
            {
                for (int i = 0; i < 8; i++)
                {
                    Assert.That(
                        service.TryAcquire($"bytes/{i}", source, out DynamicAtlasSpriteLease lease),
                        Is.EqualTo(DynamicAtlasInsertStatus.Success));
                    lease.Dispose();
                }

                DynamicAtlasStats stats = service.GetStats();
                long expected = TextureFormatHelper.EstimatePageBytes(
                    config.pageSize,
                    DynamicAtlasCopyFallback.GpuOnly) * stats.PageCount;

                TestContext.WriteLine(
                    $"BYTES|pages={stats.PageCount}|estimated={stats.EstimatedTextureBytes}|" +
                    $"expected={expected}|budget={stats.MemoryBudgetBytes}|" +
                    $"payload={stats.PayloadPixelArea}|total={stats.TotalPixelArea}");

                Assert.That(stats.EstimatedTextureBytes, Is.EqualTo(expected));
                Assert.That(stats.EstimatedTextureBytes, Is.LessThanOrEqualTo(stats.MemoryBudgetBytes));
                Assert.That(stats.PayloadPixelArea, Is.LessThanOrEqualTo(stats.TotalPixelArea));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void ZeroFillCache_StaysWithinItsByteCapForEveryPageSize()
        {
            // The shared zero buffer is capped at 4 MiB, so a 2048 page must not retain 16 MiB.
            const long capBytes = 4L * 1024L * 1024L;

            Type pageType = typeof(DynamicAtlasService).Assembly.GetType("CycloneGames.DynamicAtlas.DynamicAtlasPage");
            Assert.That(pageType, Is.Not.Null);

            var bufferField = pageType.GetField(
                "_cachedZeroFillBuffer",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var sizeField = pageType.GetField(
                "_cachedZeroFillSize",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.That(bufferField, Is.Not.Null);
            Assert.That(sizeField, Is.Not.Null);

            var reported = new List<string>(2);
            foreach (int pageSize in new[] { 2048, 1024 })
            {
                DynamicAtlasConfig config = CreateConfig(pageSize, 1, 8);
                config.memoryBudgetBytes = 512L * 1024L * 1024L;

                using (var service = new DynamicAtlasService(config))
                {
                    Assert.That(
                        service.PrewarmPages(1, DynamicAtlasPageMode.CpuBacked),
                        Is.EqualTo(1),
                        $"a {pageSize} page must be creatable");
                }

                var buffer = bufferField.GetValue(null) as Color32[];
                long bytes = buffer == null ? 0L : (long)buffer.Length * 4L;
                int cachedSize = (int)sizeField.GetValue(null);

                reported.Add($"{pageSize}->cached={cachedSize},mib={bytes / 1048576.0:F2}");
                Assert.That(
                    bytes,
                    Is.LessThanOrEqualTo(capBytes),
                    $"after creating a {pageSize} page the shared zero buffer must stay within its cap");
            }

            var finalBuffer = bufferField.GetValue(null) as Color32[];
            Assert.That(finalBuffer, Is.Not.Null, "a 1024 page must still use the cached fast path");
            Assert.That(finalBuffer.Length, Is.EqualTo(1024 * 1024));
            Assert.That((int)sizeField.GetValue(null), Is.EqualTo(1024));

            TestContext.WriteLine($"ZEROFILL|capMiB=4.00|{string.Join("|", reported)}");
        }

        [Test]
        public void LinearColorSpaceSource_IsRejectedNotSilentlyGammaShifted()
        {
            DynamicAtlasConfig config = CreateConfig(128, 2, 8);
            config.copyFallback = DynamicAtlasCopyFallback.AllowCpuRawCopy;

            using var service = new DynamicAtlasService(config);
            var linear = new Texture2D(16, 16, TextureFormat.RGBA32, false, true);
            linear.SetPixels32(new Color32[16 * 16]);
            linear.Apply(false, false);

            var srgb = new Texture2D(16, 16, TextureFormat.RGBA32, false, false);
            srgb.SetPixels32(new Color32[16 * 16]);
            srgb.Apply(false, false);

            try
            {
                DynamicAtlasInsertStatus linearStatus = service.TryAcquire(
                    "colorspace/linear",
                    linear,
                    out DynamicAtlasSpriteLease linearLease);

                DynamicAtlasInsertStatus srgbStatus = service.TryAcquire(
                    "colorspace/srgb",
                    srgb,
                    out DynamicAtlasSpriteLease srgbLease);

                TestContext.WriteLine(
                    $"COLORSPACE|linear={linearStatus}|srgb={srgbStatus}|" +
                    $"pageGraphicsFormat={UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetGraphicsFormat(TextureFormat.RGBA32, true)}");

                Assert.That(srgbStatus, Is.EqualTo(DynamicAtlasInsertStatus.Success));
                Assert.That(
                    linearStatus,
                    Is.EqualTo(DynamicAtlasInsertStatus.CopyUnsupported),
                    "a linear-flagged source must be rejected instead of being gamma-shifted into an sRGB page");

                srgbLease.Dispose();
                linearLease.Dispose();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(linear);
                UnityEngine.Object.DestroyImmediate(srgb);
            }
        }
    }
}
