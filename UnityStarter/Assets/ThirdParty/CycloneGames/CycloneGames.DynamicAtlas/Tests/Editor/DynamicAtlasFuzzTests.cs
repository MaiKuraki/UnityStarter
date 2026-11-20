using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.DynamicAtlas.Tests.Editor
{
    /// <summary>
    /// Seeded random operation sequences checked against a shadow reference model after every step.
    /// Set <c>DA_FUZZ_SEED_OFFSET</c> to explore different sequences across runs.
    /// </summary>
    public sealed class DynamicAtlasFuzzTests
    {
        private struct Lcg
        {
            private uint _state;

            internal Lcg(uint seed)
            {
                _state = seed == 0u ? 1u : seed;
            }

            internal uint NextUInt()
            {
                _state = unchecked((_state * 1664525u) + 1013904223u);
                return _state;
            }

            internal int Next(int maxExclusive)
            {
                return maxExclusive <= 0 ? 0 : (int)(NextUInt() % (uint)maxExclusive);
            }

            internal bool Chance(int percent)
            {
                return Next(100) < percent;
            }
        }

        private static int SeedOffset
        {
            get
            {
                string raw = Environment.GetEnvironmentVariable("DA_FUZZ_SEED_OFFSET");
                return int.TryParse(raw, out int value) ? value : 0;
            }
        }

        private static Texture2D CreateTexture(int width, int height, int tint)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            var pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32((byte)((i + tint) & 0xFF), (byte)((tint * 5) & 0xFF), 128, 255);
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        [Test]
        public void RandomOperationSequence_KeepsModelAndCountersInSync(
            [Values(1u, 7u, 42u, 1337u, 90210u)] uint seed,
            [Values(120, 400)] int operations)
        {
            int effectiveSeed = unchecked((int)seed + SeedOffset);
            var random = new Lcg(unchecked((uint)effectiveSeed));

            var config = new DynamicAtlasConfig
            {
                pageSize = 128,
                maxPages = 3,
                minRetainedPages = 0,
                maxEntries = 16,
                maxEntriesPerPage = 8,
                maxKeyLength = 48,
                memoryBudgetBytes = 8L * 1024L * 1024L,
                padding = 1,
                enableBleed = true,
                retentionPolicy = DynamicAtlasRetentionPolicy.RetainUntilCapacityPressure,
                oversizePolicy = DynamicAtlasOversizePolicy.Reject,
                copyFallback = DynamicAtlasCopyFallback.AllowCpuRawCopy,
                maxMissesPerFrame = 0,
            };

            using var service = new DynamicAtlasService(config);

            var sources = new List<Texture2D>(8);
            var model = new Dictionary<string, int>(StringComparer.Ordinal);
            var live = new List<KeyValuePair<string, DynamicAtlasSpriteLease>>(64);

            long attempts = 0;
            long invalidKeyAttempts = 0;
            int peakEntries = 0;
            int peakPages = 0;
            int nextKey = 0;

            try
            {
                for (int i = 0; i < 8; i++)
                {
                    sources.Add(CreateTexture(8 + (i * 3), 8 + (i * 3), i * 17));
                }

                for (int step = 0; step < operations; step++)
                {
                    int action = random.Next(100);

                    if (action < 28)
                    {
                        string key = "fuzz/" + nextKey++;
                        Texture2D source = sources[random.Next(sources.Count)];
                        attempts++;
                        DynamicAtlasInsertStatus status = service.TryAcquire(
                            key,
                            source,
                            out DynamicAtlasSpriteLease lease);

                        if (status == DynamicAtlasInsertStatus.Success)
                        {
                            model[key] = 1;
                            live.Add(new KeyValuePair<string, DynamicAtlasSpriteLease>(key, lease));
                        }
                        else
                        {
                            Assert.That(
                                status,
                                Is.EqualTo(DynamicAtlasInsertStatus.EntryCapacityReached)
                                    .Or.EqualTo(DynamicAtlasInsertStatus.PageCapacityReached)
                                    .Or.EqualTo(DynamicAtlasInsertStatus.MemoryBudgetReached),
                                $"unexpected rejection '{status}' for a fresh key at step {step}");
                        }
                    }
                    else if (action < 55)
                    {
                        if (model.Count > 0)
                        {
                            string key = PickKey(model, ref random);
                            Texture2D source = sources[random.Next(sources.Count)];
                            attempts++;
                            DynamicAtlasInsertStatus status = service.TryAcquire(
                                key,
                                source,
                                out DynamicAtlasSpriteLease lease);

                            Assert.That(
                                status,
                                Is.EqualTo(DynamicAtlasInsertStatus.CacheHit)
                                    .Or.EqualTo(DynamicAtlasInsertStatus.Success),
                                $"re-acquire of a live key must hit or re-insert, got '{status}' at step {step}");

                            if (status == DynamicAtlasInsertStatus.Success)
                            {
                                model[key] = 1;
                            }
                            else
                            {
                                model[key]++;
                            }

                            live.Add(new KeyValuePair<string, DynamicAtlasSpriteLease>(key, lease));
                        }
                    }
                    else if (action < 82)
                    {
                        if (live.Count > 0)
                        {
                            int index = random.Next(live.Count);
                            KeyValuePair<string, DynamicAtlasSpriteLease> entry = live[index];
                            entry.Value.Dispose();
                            live.RemoveAt(index);

                            if (model.TryGetValue(entry.Key, out int refs))
                            {
                                if (refs <= 1)
                                {
                                    model.Remove(entry.Key);
                                }
                                else
                                {
                                    model[entry.Key] = refs - 1;
                                }
                            }
                        }
                    }
                    else if (action < 90)
                    {
                        service.TrimUnused(random.Next(4) + 1);
                    }
                    else if (action < 93)
                    {
                        int pageCount = random.Next(2) + 1;
                        DynamicAtlasPageMode mode = random.Chance(50)
                            ? DynamicAtlasPageMode.GpuOnly
                            : DynamicAtlasPageMode.CpuBacked;
                        service.PrewarmPages(pageCount, mode);
                    }
                    else if (action < 96)
                    {
                        // Region extraction from a larger source exercises the rect path.
                        Texture2D source = sources[sources.Count - 1];
                        string key = "fuzz/region/" + nextKey++;
                        attempts++;
                        DynamicAtlasInsertStatus status = service.TryAcquireRegion(
                            key,
                            source,
                            new RectInt(0, 0, 8, 8),
                            out DynamicAtlasSpriteLease lease);

                        Assert.That(
                            status,
                            Is.EqualTo(DynamicAtlasInsertStatus.Success)
                                .Or.EqualTo(DynamicAtlasInsertStatus.EntryCapacityReached)
                                .Or.EqualTo(DynamicAtlasInsertStatus.PageCapacityReached)
                                .Or.EqualTo(DynamicAtlasInsertStatus.MemoryBudgetReached),
                            $"unexpected region rejection '{status}' at step {step}");

                        if (status == DynamicAtlasInsertStatus.Success)
                        {
                            model[key] = 1;
                            live.Add(new KeyValuePair<string, DynamicAtlasSpriteLease>(key, lease));
                        }
                    }
                    else if (action < 98)
                    {
                        invalidKeyAttempts++;
                        DynamicAtlasInsertStatus status = service.TryAcquire(
                            random.Chance(50) ? null : new string('x', config.maxKeyLength + 1),
                            sources[0],
                            out DynamicAtlasSpriteLease lease);
                        Assert.That(status, Is.EqualTo(DynamicAtlasInsertStatus.InvalidKey));
                        Assert.That(lease.IsValid, Is.False);
                    }
                    else
                    {
                        service.Clear();
                        model.Clear();
                        live.Clear();
                    }

                    DynamicAtlasStats stats = service.GetStats();
                    int expectedReferences = 0;
                    int expectedActiveEntries = 0;
                    foreach (KeyValuePair<string, int> pair in model)
                    {
                        expectedReferences += pair.Value;
                        if (pair.Value > 0)
                        {
                            expectedActiveEntries++;
                        }
                    }

                    Assert.That(
                        stats.ActiveReferenceCount,
                        Is.EqualTo(expectedReferences),
                        $"reference accounting drifted at step {step} (seed {effectiveSeed})");
                    Assert.That(
                        stats.ActiveEntryCount,
                        Is.EqualTo(expectedActiveEntries),
                        $"active entry count drifted at step {step} (seed {effectiveSeed})");
                    Assert.That(
                        stats.ActiveEntryCount + stats.RetainedEntryCount,
                        Is.EqualTo(stats.EntryCount),
                        $"entry partition drifted at step {step} (seed {effectiveSeed})");
                    Assert.That(stats.EntryCount, Is.LessThanOrEqualTo(config.maxEntries));
                    Assert.That(stats.PageCount, Is.LessThanOrEqualTo(config.maxPages));
                    Assert.That(stats.PayloadPixelArea, Is.LessThanOrEqualTo(stats.TotalPixelArea));
                    Assert.That(stats.EstimatedTextureBytes, Is.LessThanOrEqualTo(config.memoryBudgetBytes));
                    Assert.That(stats.CacheHitCount + stats.CacheMissCount, Is.EqualTo(attempts),
                        $"hit/miss split must cover every valid-key attempt at step {step}");
                    Assert.That(stats.RejectionCount, Is.GreaterThanOrEqualTo(invalidKeyAttempts));

                    if (stats.EntryCount > peakEntries)
                    {
                        peakEntries = stats.EntryCount;
                    }

                    if (stats.PageCount > peakPages)
                    {
                        peakPages = stats.PageCount;
                    }
                }

                for (int i = 0; i < live.Count; i++)
                {
                    live[i].Value.Dispose();
                }

                live.Clear();
                model.Clear();

                for (int guard = 0; guard < 8; guard++)
                {
                    if (service.GetStats().EntryCount == 0)
                    {
                        break;
                    }

                    if (service.TrimUnused() == 0)
                    {
                        break;
                    }
                }

                DynamicAtlasStats final = service.GetStats();
                Assert.That(final.EntryCount, Is.Zero, "every entry must be trimmable once all leases are released");
                Assert.That(final.ActiveReferenceCount, Is.Zero);

                TestContext.WriteLine(
                    $"FUZZ|seed={effectiveSeed}|ops={operations}|attempts={attempts}|" +
                    $"hits={final.CacheHitCount}|misses={final.CacheMissCount}|inserts={final.InsertCount}|" +
                    $"evictions={final.EvictionCount}|rejections={final.RejectionCount}|" +
                    $"peakEntries={peakEntries}|peakPages={peakPages}");
            }
            finally
            {
                for (int i = 0; i < live.Count; i++)
                {
                    live[i].Value.Dispose();
                }

                for (int i = 0; i < sources.Count; i++)
                {
                    if (sources[i] != null)
                    {
                        UnityEngine.Object.DestroyImmediate(sources[i]);
                    }
                }
            }
        }

        private static string PickKey(Dictionary<string, int> model, ref Lcg random)
        {
            int target = random.Next(model.Count);
            int index = 0;
            foreach (string key in model.Keys)
            {
                if (index == target)
                {
                    return key;
                }

                index++;
            }

            return null;
        }
    }
}
