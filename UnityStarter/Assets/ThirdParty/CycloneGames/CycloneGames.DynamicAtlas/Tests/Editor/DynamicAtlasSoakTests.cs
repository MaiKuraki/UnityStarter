using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.DynamicAtlas.Tests.Editor
{
    /// <summary>
    /// Long-run boundedness. Each scenario repeats an access pattern for a parameterized duration
    /// and asserts that pages, entries, bytes, and reference counts stay inside their ceilings.
    /// </summary>
    public sealed class DynamicAtlasSoakTests
    {
        private static Texture2D CreateTexture(int size, int tint)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32((byte)((i + tint) & 0xFF), (byte)((tint * 3) & 0xFF), 64, 255);
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static DynamicAtlasConfig CreateConfig(
            int maxEntries,
            int maxPages,
            long budget,
            DynamicAtlasRetentionPolicy retention)
        {
            return new DynamicAtlasConfig
            {
                pageSize = 128,
                maxPages = maxPages,
                minRetainedPages = 0,
                maxEntries = maxEntries,
                maxEntriesPerPage = maxEntries,
                maxKeyLength = 48,
                memoryBudgetBytes = budget,
                padding = 1,
                enableBleed = true,
                retentionPolicy = retention,
                oversizePolicy = DynamicAtlasOversizePolicy.Reject,
                copyFallback = DynamicAtlasCopyFallback.AllowCpuRawCopy,
                maxMissesPerFrame = 0,
            };
        }

        [Test]
        public void Soak_SteadyIconChurn_StaysBounded(
            [Values(400, 1600, 4000)] int cycles,
            [Values(1, 2, 3)] int seed)
        {
            int maxEntries = 32;
            DynamicAtlasConfig config = CreateConfig(
                maxEntries,
                maxPages: 4,
                budget: 16L * 1024L * 1024L,
                DynamicAtlasRetentionPolicy.RemoveWhenUnused);

            using var service = new DynamicAtlasService(config);

            const int sourceCount = 16;
            const int window = 24;
            var sources = new List<Texture2D>(sourceCount);
            var live = new List<KeyValuePair<string, DynamicAtlasSpriteLease>>(16);
            var random = new System.Random(seed);
            var stopwatch = Stopwatch.StartNew();

            long rejections = 0;
            long hires = 0;
            int peakEntries = 0;
            int peakPages = 0;

            try
            {
                for (int i = 0; i < sourceCount; i++)
                {
                    sources.Add(CreateTexture(8 + (i % 5) * 2, i * 13));
                }

                for (int cycle = 0; cycle < cycles; cycle++)
                {
                    string key = "churn/" + random.Next(window);
                    DynamicAtlasInsertStatus status = service.TryAcquire(
                        key,
                        sources[random.Next(sourceCount)],
                        out DynamicAtlasSpriteLease lease);

                    if (status == DynamicAtlasInsertStatus.Success ||
                        status == DynamicAtlasInsertStatus.CacheHit)
                    {
                        live.Add(new KeyValuePair<string, DynamicAtlasSpriteLease>(key, lease));
                    }
                    else
                    {
                        rejections++;
                    }

                    if (live.Count > 6)
                    {
                        int drop = random.Next(live.Count);
                        live[drop].Value.Dispose();
                        live.RemoveAt(drop);
                    }

                    if ((cycle & 63) == 0)
                    {
                        DynamicAtlasStats checkpoint = service.GetStats();
                        Assert.That(checkpoint.EntryCount, Is.LessThanOrEqualTo(maxEntries));
                        Assert.That(checkpoint.PageCount, Is.LessThanOrEqualTo(config.maxPages));
                        Assert.That(checkpoint.EstimatedTextureBytes, Is.LessThanOrEqualTo(config.memoryBudgetBytes));
                        Assert.That(checkpoint.ActiveReferenceCount, Is.EqualTo(live.Count));
                        Assert.That(checkpoint.PayloadPixelArea, Is.LessThanOrEqualTo(checkpoint.TotalPixelArea));

                        if (checkpoint.EntryCount > peakEntries)
                        {
                            peakEntries = checkpoint.EntryCount;
                        }

                        if (checkpoint.PageCount > peakPages)
                        {
                            peakPages = checkpoint.PageCount;
                        }
                    }

                    hires++;
                }

                for (int i = 0; i < live.Count; i++)
                {
                    live[i].Value.Dispose();
                }

                live.Clear();
                service.TrimUnused();

                DynamicAtlasStats final = service.GetStats();
                Assert.That(final.EntryCount, Is.Zero, "RemoveWhenUnused must leave nothing behind");
                Assert.That(final.ActiveReferenceCount, Is.Zero);
                Assert.That(final.PageCount, Is.LessThanOrEqualTo(config.maxPages));

                TestContext.WriteLine(
                    $"SOAK|scenario=steady-churn|seed={seed}|cycles={cycles}|attempts={hires}|" +
                    $"rejections={rejections}|peakEntries={peakEntries}|peakPages={peakPages}|" +
                    $"hits={final.CacheHitCount}|misses={final.CacheMissCount}|elapsedMs={stopwatch.ElapsedMilliseconds}");
            }
            finally
            {
                for (int i = 0; i < live.Count; i++)
                {
                    live[i].Value.Dispose();
                }

                for (int i = 0; i < sources.Count; i++)
                {
                    UnityEngine.Object.DestroyImmediate(sources[i]);
                }
            }
        }

        [Test]
        public void Soak_RetainedPressure_EvictsInsteadOfRejecting(
            [Values(400, 1600, 4000)] int cycles,
            [Values(1, 2, 3)] int seed)
        {
            int maxEntries = 24;
            DynamicAtlasConfig config = CreateConfig(
                maxEntries,
                maxPages: 4,
                budget: 16L * 1024L * 1024L,
                DynamicAtlasRetentionPolicy.RetainUntilCapacityPressure);

            using var service = new DynamicAtlasService(config);

            const int sourceCount = 8;
            var sources = new List<Texture2D>(sourceCount);
            var random = new System.Random(seed);
            var stopwatch = Stopwatch.StartNew();

            long rejections = 0;
            long successes = 0;
            int peakEntries = 0;
            int peakPages = 0;

            try
            {
                for (int i = 0; i < sourceCount; i++)
                {
                    sources.Add(CreateTexture(10 + i, i * 29));
                }

                // Every acquire releases immediately, so admitting a new key requires evicting a
                // retained entry.
                for (int cycle = 0; cycle < cycles; cycle++)
                {
                    string key = "retain/" + (cycle % 256);
                    DynamicAtlasInsertStatus status = service.TryAcquire(
                        key,
                        sources[random.Next(sourceCount)],
                        out DynamicAtlasSpriteLease lease);

                    if (status == DynamicAtlasInsertStatus.Success ||
                        status == DynamicAtlasInsertStatus.CacheHit)
                    {
                        successes++;
                        lease.Dispose();
                    }
                    else
                    {
                        rejections++;
                    }

                    if ((cycle & 63) == 0)
                    {
                        DynamicAtlasStats checkpoint = service.GetStats();
                        Assert.That(checkpoint.EntryCount, Is.LessThanOrEqualTo(maxEntries));
                        Assert.That(checkpoint.PageCount, Is.LessThanOrEqualTo(config.maxPages));
                        Assert.That(checkpoint.ActiveReferenceCount, Is.Zero);
                        Assert.That(checkpoint.EstimatedTextureBytes, Is.LessThanOrEqualTo(config.memoryBudgetBytes));

                        if (checkpoint.EntryCount > peakEntries)
                        {
                            peakEntries = checkpoint.EntryCount;
                        }

                        if (checkpoint.PageCount > peakPages)
                        {
                            peakPages = checkpoint.PageCount;
                        }
                    }
                }

                DynamicAtlasStats final = service.GetStats();
                Assert.That(final.EvictionCount, Is.GreaterThan(0), "retained pressure must evict, not leak");
                Assert.That(final.EntryCount, Is.LessThanOrEqualTo(maxEntries));
                Assert.That(final.ActiveReferenceCount, Is.Zero);

                int trimmed = service.TrimUnused();
                Assert.That(trimmed, Is.LessThanOrEqualTo(maxEntries));
                Assert.That(service.GetStats().EntryCount, Is.Zero);

                TestContext.WriteLine(
                    $"SOAK|scenario=retained-pressure|seed={seed}|cycles={cycles}|successes={successes}|" +
                    $"rejections={rejections}|evictions={final.EvictionCount}|peakEntries={peakEntries}|peakPages={peakPages}|" +
                    $"elapsedMs={stopwatch.ElapsedMilliseconds}");
            }
            finally
            {
                for (int i = 0; i < sources.Count; i++)
                {
                    UnityEngine.Object.DestroyImmediate(sources[i]);
                }
            }
        }

        [Test]
        public void Soak_MixedSizesAndOversizeRejections_DoNotLeak(
            [Values(400, 1600, 4000)] int cycles,
            [Values(1, 2, 3)] int seed)
        {
            int maxEntries = 20;
            DynamicAtlasConfig config = CreateConfig(
                maxEntries,
                maxPages: 3,
                budget: 16L * 1024L * 1024L,
                DynamicAtlasRetentionPolicy.RemoveWhenUnused);

            using var service = new DynamicAtlasService(config);

            var sources = new List<Texture2D>(6);
            var random = new System.Random(seed);
            var live = new List<KeyValuePair<string, DynamicAtlasSpriteLease>>(8);
            var stopwatch = Stopwatch.StartNew();

            long oversizeRejections = 0;
            long successes = 0;
            int peakEntries = 0;
            int peakPages = 0;

            try
            {
                for (int i = 0; i < 5; i++)
                {
                    sources.Add(CreateTexture(16, i * 31));
                }

                // 200 px exceeds the 128 page and must be rejected by policy, never silently dropped.
                sources.Add(CreateTexture(200, 77));

                for (int cycle = 0; cycle < cycles; cycle++)
                {
                    int index = random.Next(sources.Count);
                    bool oversize = index == sources.Count - 1;

                    // A dedicated key, otherwise a cache hit would hide the rejection.
                    string key = oversize ? "mixed/oversize" : "mixed/" + random.Next(40);
                    DynamicAtlasInsertStatus status = service.TryAcquire(
                        key,
                        sources[index],
                        out DynamicAtlasSpriteLease lease);

                    if (oversize)
                    {
                        Assert.That(
                            status,
                            Is.EqualTo(DynamicAtlasInsertStatus.OversizedSource),
                            "an oversized source must be rejected by policy");
                        oversizeRejections++;
                    }
                    else if (status == DynamicAtlasInsertStatus.Success ||
                             status == DynamicAtlasInsertStatus.CacheHit)
                    {
                        successes++;
                        live.Add(new KeyValuePair<string, DynamicAtlasSpriteLease>(key, lease));
                    }

                    if (live.Count > 4)
                    {
                        int drop = random.Next(live.Count);
                        live[drop].Value.Dispose();
                        live.RemoveAt(drop);
                    }

                    if ((cycle & 63) == 0)
                    {
                        DynamicAtlasStats checkpoint = service.GetStats();
                        Assert.That(checkpoint.EntryCount, Is.LessThanOrEqualTo(maxEntries));
                        Assert.That(checkpoint.PageCount, Is.LessThanOrEqualTo(config.maxPages));
                        Assert.That(checkpoint.ActiveReferenceCount, Is.EqualTo(live.Count));
                        Assert.That(checkpoint.EstimatedTextureBytes, Is.LessThanOrEqualTo(config.memoryBudgetBytes));

                        if (checkpoint.EntryCount > peakEntries)
                        {
                            peakEntries = checkpoint.EntryCount;
                        }

                        if (checkpoint.PageCount > peakPages)
                        {
                            peakPages = checkpoint.PageCount;
                        }
                    }
                }

                for (int i = 0; i < live.Count; i++)
                {
                    live[i].Value.Dispose();
                }

                live.Clear();

                DynamicAtlasStats final = service.GetStats();
                Assert.That(final.EntryCount, Is.Zero);
                Assert.That(final.ActiveReferenceCount, Is.Zero);

                TestContext.WriteLine(
                    $"SOAK|scenario=mixed-oversize|seed={seed}|cycles={cycles}|successes={successes}|" +
                    $"oversizeRejections={oversizeRejections}|peakEntries={peakEntries}|peakPages={peakPages}|" +
                    $"elapsedMs={stopwatch.ElapsedMilliseconds}");
            }
            finally
            {
                for (int i = 0; i < live.Count; i++)
                {
                    live[i].Value.Dispose();
                }

                for (int i = 0; i < sources.Count; i++)
                {
                    UnityEngine.Object.DestroyImmediate(sources[i]);
                }
            }
        }
    }
}
