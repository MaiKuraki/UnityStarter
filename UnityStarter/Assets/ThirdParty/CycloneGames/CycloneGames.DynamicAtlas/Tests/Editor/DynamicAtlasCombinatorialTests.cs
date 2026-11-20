using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace CycloneGames.DynamicAtlas.Tests.Editor
{
    /// <summary>
    /// Configuration and acquisition-scenario matrix. Results that depend on host GPU support are
    /// branched explicitly, so the matrix also runs on machines without CopyTexture.
    /// </summary>
    public sealed class DynamicAtlasCombinatorialTests
    {
        private static bool GpuCopyAvailable =>
            (SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) != 0;

        private static Texture2D CreateTexture(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            var pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32((byte)(i * 37), (byte)(i * 11), 90, 255);
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static DynamicAtlasConfig CreateConfig(
            int pageSize,
            int padding,
            DynamicAtlasRetentionPolicy retention,
            int maxEntries,
            int maxEntriesPerPage,
            int maxPages,
            int maxMissesPerFrame,
            DynamicAtlasCopyFallback fallback,
            long memoryBudgetBytes = 32L * 1024L * 1024L)
        {
            return new DynamicAtlasConfig
            {
                pageSize = pageSize,
                maxPages = maxPages,
                minRetainedPages = 0,
                maxEntries = maxEntries,
                maxEntriesPerPage = maxEntriesPerPage,
                maxKeyLength = 64,
                memoryBudgetBytes = memoryBudgetBytes,
                padding = padding,
                enableBleed = padding > 0,
                retentionPolicy = retention,
                oversizePolicy = DynamicAtlasOversizePolicy.Reject,
                copyFallback = fallback,
                maxMissesPerFrame = maxMissesPerFrame,
                defaultPixelsPerUnit = 100f,
            };
        }

        [Test]
        public void AdmissionMatrix_KeepsEveryCeiling(
            [Values(64, 128, 256)] int pageSize,
            [Values(0, 1, 3)] int padding,
            [Values(DynamicAtlasRetentionPolicy.RemoveWhenUnused,
                DynamicAtlasRetentionPolicy.RetainUntilCapacityPressure)]
            DynamicAtlasRetentionPolicy retention,
            [Values(4, 12)] int maxEntries,
            [Values(DynamicAtlasCopyFallback.GpuOnly, DynamicAtlasCopyFallback.AllowCpuRawCopy)]
            DynamicAtlasCopyFallback fallback)
        {
            DynamicAtlasConfig config = CreateConfig(
                pageSize,
                padding,
                retention,
                maxEntries,
                maxEntries,
                maxPages: 3,
                maxMissesPerFrame: 0,
                fallback);

            Assert.That(config.Validate(out string error), Is.True, error);

            int spriteSize = Mathf.Min(12, pageSize / 4);
            var sources = new List<Texture2D>(maxEntries);
            var leases = new List<DynamicAtlasSpriteLease>(maxEntries);
            using var service = new DynamicAtlasService(config);

            try
            {
                for (int i = 0; i < maxEntries; i++)
                {
                    sources.Add(CreateTexture(spriteSize, spriteSize));
                }

                bool expectSuccess = GpuCopyAvailable || fallback != DynamicAtlasCopyFallback.GpuOnly;

                for (int i = 0; i < maxEntries; i++)
                {
                    DynamicAtlasInsertStatus status = service.TryAcquire(
                        $"matrix/{i}",
                        sources[i],
                        out DynamicAtlasSpriteLease lease);

                    if (expectSuccess)
                    {
                        Assert.That(
                            status,
                            Is.EqualTo(DynamicAtlasInsertStatus.Success),
                            $"pageSize={pageSize} padding={padding} retention={retention} " +
                            $"maxEntries={maxEntries} fallback={fallback} index={i}");
                        leases.Add(lease);
                    }
                    else
                    {
                        Assert.That(
                            status,
                            Is.EqualTo(DynamicAtlasInsertStatus.CopyUnsupported),
                            $"GPU copy unavailable; unexpected status for index {i}");
                        Assert.That(lease.IsValid, Is.False);
                    }
                }

                DynamicAtlasStats stats = service.GetStats();
                Assert.That(stats.EntryCount, Is.LessThanOrEqualTo(config.maxEntries));
                Assert.That(stats.PageCount, Is.LessThanOrEqualTo(config.maxPages));
                Assert.That(stats.ActiveEntryCount + stats.RetainedEntryCount, Is.EqualTo(stats.EntryCount));
                Assert.That(stats.PayloadPixelArea, Is.LessThanOrEqualTo(stats.TotalPixelArea));
                Assert.That(stats.EstimatedTextureBytes, Is.LessThanOrEqualTo(config.memoryBudgetBytes));
                Assert.That(stats.ActiveReferenceCount, Is.EqualTo(leases.Count));

                for (int i = 0; i < leases.Count; i++)
                {
                    leases[i].Dispose();
                }

                if (!expectSuccess)
                {
                    Assert.That(service.GetStats().EntryCount, Is.Zero);
                    return;
                }

                if (retention == DynamicAtlasRetentionPolicy.RemoveWhenUnused)
                {
                    Assert.That(service.GetStats().EntryCount, Is.Zero);
                }
                else
                {
                    int removed = service.TrimUnused();
                    Assert.That(removed, Is.EqualTo(maxEntries));
                    Assert.That(service.GetStats().EntryCount, Is.Zero);
                }

                DynamicAtlasStats drained = service.GetStats();
                Assert.That(drained.ActiveReferenceCount, Is.Zero);
                Assert.That(drained.PageCount, Is.LessThanOrEqualTo(config.maxPages));
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

        public enum AcquireScenario
        {
            DirectTexture = 0,
            Region = 1,
            SpriteObject = 2,
            LocationLoader = 3,
        }

        [Test]
        public void ScenarioMatrix_EveryEntryPointSharesTheSameContract(
            [Values(AcquireScenario.DirectTexture, AcquireScenario.Region,
                AcquireScenario.SpriteObject, AcquireScenario.LocationLoader)]
            AcquireScenario scenario,
            [Values(64, 256)] int pageSize,
            [Values(0, 2)] int padding)
        {
            DynamicAtlasConfig config = CreateConfig(
                pageSize,
                padding,
                DynamicAtlasRetentionPolicy.RetainUntilCapacityPressure,
                maxEntries: 8,
                maxEntriesPerPage: 8,
                maxPages: 2,
                maxMissesPerFrame: 0,
                DynamicAtlasCopyFallback.AllowCpuRawCopy);

            Texture2D loadedTexture = CreateTexture(16, 16);
            config.loadFunc = _ => loadedTexture;
            config.unloadFunc = (_, texture) =>
            {
                if (!ReferenceEquals(texture, loadedTexture))
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }
            };

            using var service = new DynamicAtlasService(config);
            Texture2D regionSource = CreateTexture(32, 32);
            Texture2D wholeSource = CreateTexture(16, 16);
            Sprite spriteSource = Sprite.Create(
                wholeSource,
                new Rect(0f, 0f, 16f, 16f),
                new Vector2(0.5f, 0.5f),
                100f);

            try
            {
                DynamicAtlasInsertStatus status;
                DynamicAtlasSpriteLease lease;

                switch (scenario)
                {
                    case AcquireScenario.DirectTexture:
                        status = service.TryAcquire("scenario/direct", wholeSource, out lease);
                        break;
                    case AcquireScenario.Region:
                        status = service.TryAcquireRegion(
                            "scenario/region",
                            regionSource,
                            new RectInt(4, 4, 16, 16),
                            out lease);
                        break;
                    case AcquireScenario.SpriteObject:
                        status = service.TryAcquireSprite("scenario/sprite", spriteSource, out lease);
                        break;
                    default:
                        status = service.TryAcquireLocation("scenario/location", out lease);
                        break;
                }

                Assert.That(status, Is.EqualTo(DynamicAtlasInsertStatus.Success),
                    $"{scenario} pageSize={pageSize} padding={padding}");
                Assert.That(lease.IsValid, Is.True);
                Assert.That(lease.Sprite, Is.Not.Null);

                Sprite produced = lease.Sprite;
                Assert.That(produced.texture, Is.Not.Null);

                DynamicAtlasInsertStatus repeated;
                DynamicAtlasSpriteLease repeatedLease;
                switch (scenario)
                {
                    case AcquireScenario.DirectTexture:
                        repeated = service.TryAcquire("scenario/direct", wholeSource, out repeatedLease);
                        break;
                    case AcquireScenario.Region:
                        repeated = service.TryAcquireRegion(
                            "scenario/region", regionSource, new RectInt(4, 4, 16, 16), out repeatedLease);
                        break;
                    case AcquireScenario.SpriteObject:
                        repeated = service.TryAcquireSprite("scenario/sprite", spriteSource, out repeatedLease);
                        break;
                    default:
                        repeated = service.TryAcquireLocation("scenario/location", out repeatedLease);
                        break;
                }

                Assert.That(repeated, Is.EqualTo(DynamicAtlasInsertStatus.CacheHit));
                Assert.That(service.GetStats().ActiveReferenceCount, Is.EqualTo(2));

                repeatedLease.Dispose();
                lease.Dispose();
                DynamicAtlasStats stats = service.GetStats();
                Assert.That(stats.EntryCount, Is.EqualTo(1));
                Assert.That(stats.ActiveReferenceCount, Is.Zero);
                Assert.That(stats.CacheHitCount, Is.EqualTo(1));
                Assert.That(stats.CacheMissCount, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(regionSource);
                UnityEngine.Object.DestroyImmediate(wholeSource);
                UnityEngine.Object.DestroyImmediate(loadedTexture);
            }
        }

        [Test]
        public void EntryCapacity_RejectsBeforeRunningPixelWork(
            [Values(1, 2, 3, 5)] int maxEntries)
        {
            DynamicAtlasConfig config = CreateConfig(
                64,
                padding: 0,
                DynamicAtlasRetentionPolicy.RetainUntilCapacityPressure,
                maxEntries,
                maxEntriesPerPage: maxEntries,
                maxPages: 2,
                maxMissesPerFrame: 0,
                DynamicAtlasCopyFallback.AllowCpuRawCopy);

            using var service = new DynamicAtlasService(config);
            var sources = new List<Texture2D>(maxEntries + 1);
            var leases = new List<DynamicAtlasSpriteLease>(maxEntries);

            try
            {
                for (int i = 0; i <= maxEntries; i++)
                {
                    sources.Add(CreateTexture(8, 8));
                }

                for (int i = 0; i < maxEntries; i++)
                {
                    Assert.That(
                        service.TryAcquire($"cap/{i}", sources[i], out DynamicAtlasSpriteLease lease),
                        Is.EqualTo(DynamicAtlasInsertStatus.Success));
                    leases.Add(lease);
                }

                DynamicAtlasInsertStatus overflow = service.TryAcquire(
                    "cap/overflow",
                    sources[maxEntries],
                    out DynamicAtlasSpriteLease overflowLease);

                Assert.That(overflow, Is.EqualTo(DynamicAtlasInsertStatus.EntryCapacityReached));
                Assert.That(overflowLease.IsValid, Is.False);
                Assert.That(service.GetStats().EntryCount, Is.EqualTo(maxEntries));
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
        public void PageCapacity_RejectsWhenTheOnlyPageCannotFitActiveContent()
        {
            DynamicAtlasConfig config = CreateConfig(
                64,
                padding: 0,
                DynamicAtlasRetentionPolicy.RemoveWhenUnused,
                maxEntries: 16,
                maxEntriesPerPage: 16,
                maxPages: 1,
                maxMissesPerFrame: 0,
                DynamicAtlasCopyFallback.AllowCpuRawCopy);

            using var service = new DynamicAtlasService(config);
            var sources = new List<Texture2D>(2);
            var leases = new List<DynamicAtlasSpriteLease>(1);

            try
            {
                // 48 px cells cannot pair up inside a 64 px page on either axis, so the page holds
                // exactly one entry and the second acquire has nowhere to go.
                for (int i = 0; i < 2; i++)
                {
                    sources.Add(CreateTexture(48, 48));
                }

                Assert.That(
                    service.TryAcquire("page/a", sources[0], out DynamicAtlasSpriteLease a),
                    Is.EqualTo(DynamicAtlasInsertStatus.Success));
                leases.Add(a);

                DynamicAtlasInsertStatus second = service.TryAcquire(
                    "page/b",
                    sources[1],
                    out DynamicAtlasSpriteLease b);

                Assert.That(second, Is.EqualTo(DynamicAtlasInsertStatus.PageCapacityReached));
                Assert.That(b.IsValid, Is.False);
                Assert.That(service.GetStats().PageCount, Is.EqualTo(1));
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
        public void MissBudget_ThrottlesNewKeysButNeverCachedOnes(
            [Values(1, 2, 4)] int maxMissesPerFrame)
        {
            DynamicAtlasConfig config = CreateConfig(
                64,
                padding: 0,
                DynamicAtlasRetentionPolicy.RetainUntilCapacityPressure,
                maxEntries: 16,
                maxEntriesPerPage: 16,
                maxPages: 2,
                maxMissesPerFrame,
                DynamicAtlasCopyFallback.AllowCpuRawCopy);

            using var service = new DynamicAtlasService(config);
            var sources = new List<Texture2D>(maxMissesPerFrame + 2);
            var leases = new List<DynamicAtlasSpriteLease>(maxMissesPerFrame);

            try
            {
                for (int i = 0; i < maxMissesPerFrame + 2; i++)
                {
                    sources.Add(CreateTexture(8, 8));
                }

                for (int i = 0; i < maxMissesPerFrame; i++)
                {
                    Assert.That(
                        service.TryAcquire($"budget/{i}", sources[i], out DynamicAtlasSpriteLease lease),
                        Is.EqualTo(DynamicAtlasInsertStatus.Success),
                        $"miss index {i} should fit inside a {maxMissesPerFrame} budget");
                    leases.Add(lease);
                }

                Assert.That(
                    service.TryAcquire(
                        "budget/overflow",
                        sources[maxMissesPerFrame],
                        out DynamicAtlasSpriteLease throttled),
                    Is.EqualTo(DynamicAtlasInsertStatus.Throttled));
                Assert.That(throttled.IsValid, Is.False);

                Assert.That(
                    service.TryAcquire("budget/0", sources[0], out DynamicAtlasSpriteLease cached),
                    Is.EqualTo(DynamicAtlasInsertStatus.CacheHit),
                    "a cache hit must not consume the miss budget");
                cached.Dispose();

                DynamicAtlasStats stats = service.GetStats();
                Assert.That(stats.CacheMissCount, Is.EqualTo(maxMissesPerFrame + 1));
                Assert.That(stats.CacheHitCount, Is.EqualTo(1));
                Assert.That(stats.EntryCount, Is.EqualTo(maxMissesPerFrame));
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
        public void PageSizeBoundary_AcceptsExactFitAndRejectsOnePixelMore(
            [Values(64, 128, 256)] int pageSize)
        {
            DynamicAtlasConfig config = CreateConfig(
                pageSize,
                padding: 0,
                DynamicAtlasRetentionPolicy.RemoveWhenUnused,
                maxEntries: 4,
                maxEntriesPerPage: 4,
                maxPages: 2,
                maxMissesPerFrame: 0,
                DynamicAtlasCopyFallback.AllowCpuRawCopy);

            using var service = new DynamicAtlasService(config);
            Texture2D exact = CreateTexture(pageSize, pageSize);
            Texture2D oversized = CreateTexture(pageSize + 1, pageSize + 1);

            try
            {
                Assert.That(
                    service.TryAcquire("boundary/exact", exact, out DynamicAtlasSpriteLease lease),
                    Is.EqualTo(DynamicAtlasInsertStatus.Success),
                    $"a {pageSize} source must fit a {pageSize} page");
                lease.Dispose();

                Assert.That(
                    service.TryAcquire("boundary/oversized", oversized, out DynamicAtlasSpriteLease rejected),
                    Is.EqualTo(DynamicAtlasInsertStatus.OversizedSource));
                Assert.That(rejected.IsValid, Is.False);
                Assert.That(service.GetStats().EntryCount, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(exact);
                UnityEngine.Object.DestroyImmediate(oversized);
            }
        }

        [Test]
        public void KeyLengthBoundary_AcceptsLimitAndRejectsOverflow()
        {
            DynamicAtlasConfig config = CreateConfig(
                64,
                padding: 0,
                DynamicAtlasRetentionPolicy.RemoveWhenUnused,
                maxEntries: 4,
                maxEntriesPerPage: 4,
                maxPages: 2,
                maxMissesPerFrame: 0,
                DynamicAtlasCopyFallback.AllowCpuRawCopy);
            config.maxKeyLength = 16;

            using var service = new DynamicAtlasService(config);
            Texture2D source = CreateTexture(8, 8);

            try
            {
                string atLimit = new string('k', 16);
                string overLimit = new string('k', 17);

                Assert.That(
                    service.TryAcquire(atLimit, source, out DynamicAtlasSpriteLease lease),
                    Is.EqualTo(DynamicAtlasInsertStatus.Success));
                lease.Dispose();

                Assert.That(
                    service.TryAcquire(overLimit, source, out _),
                    Is.EqualTo(DynamicAtlasInsertStatus.InvalidKey));
                Assert.That(
                    service.TryAcquire(null, source, out _),
                    Is.EqualTo(DynamicAtlasInsertStatus.InvalidKey));
                Assert.That(
                    service.TryAcquire(string.Empty, source, out _),
                    Is.EqualTo(DynamicAtlasInsertStatus.InvalidKey));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void NestedBatchDepth_PreservesReferencesAndToleratesStaleScopes(
            [Values(1, 2, 4)] int depth)
        {
            DynamicAtlasConfig config = CreateConfig(
                64,
                padding: 0,
                DynamicAtlasRetentionPolicy.RetainUntilCapacityPressure,
                maxEntries: 8,
                maxEntriesPerPage: 8,
                maxPages: 2,
                maxMissesPerFrame: 0,
                DynamicAtlasCopyFallback.AllowCpuRawCopy);

            using var service = new DynamicAtlasService(config);
            var sources = new List<Texture2D>(2) { CreateTexture(8, 8), CreateTexture(8, 8) };
            var batches = new List<DynamicAtlasWriteBatch>(depth);
            var leases = new List<DynamicAtlasSpriteLease>(2);

            try
            {
                for (int i = 0; i < depth; i++)
                {
                    batches.Add(service.BeginBatch());
                }

                for (int i = 0; i < 2; i++)
                {
                    Assert.That(
                        service.TryAcquire($"batch/{i}", sources[i], out DynamicAtlasSpriteLease lease),
                        Is.EqualTo(DynamicAtlasInsertStatus.Success));
                    leases.Add(lease);
                }

                Assert.That(service.GetStats().ActiveReferenceCount, Is.EqualTo(2));

                for (int i = batches.Count - 1; i >= 0; i--)
                {
                    batches[i].Dispose();
                }

                Assert.That(service.GetStats().ActiveReferenceCount, Is.EqualTo(2));
                Assert.That(service.GetStats().EntryCount, Is.EqualTo(2));

                service.Clear();
                batches[depth - 1].Dispose();
                Assert.That(service.GetStats().EntryCount, Is.Zero);
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
    }
}
