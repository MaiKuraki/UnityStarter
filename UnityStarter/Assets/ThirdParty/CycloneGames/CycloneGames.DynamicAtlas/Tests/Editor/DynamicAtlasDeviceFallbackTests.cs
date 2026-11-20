using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.DynamicAtlas.Tests.Editor
{
    /// <summary>
    /// Device-fallback contracts: the do-nothing atlas used when packing is unavailable, and the
    /// memory-pressure hook that mobile platforms raise on low-end hardware.
    /// </summary>
    public sealed class DynamicAtlasDeviceFallbackTests
    {
        private static Texture2D CreateTexture(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(120, 80, 40, 255);
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static DynamicAtlasConfig CreateConfig()
        {
            return new DynamicAtlasConfig
            {
                pageSize = 64,
                maxPages = 2,
                minRetainedPages = 0,
                maxEntries = 8,
                maxEntriesPerPage = 8,
                maxKeyLength = 64,
                memoryBudgetBytes = 1024L * 1024L,
                padding = 1,
                enableBleed = false,
                retentionPolicy = DynamicAtlasRetentionPolicy.RetainUntilCapacityPressure,
                oversizePolicy = DynamicAtlasOversizePolicy.Reject,
                copyFallback = DynamicAtlasCopyFallback.AllowCpuRawCopy,
                maxMissesPerFrame = 0,
            };
        }

        [Test]
        public void NullAtlas_ReportsDisabledInsteadOfReturningUnusableLeases()
        {
            IDynamicAtlas atlas = NullDynamicAtlas.Shared;
            Texture2D source = CreateTexture(8);

            try
            {
                Assert.That(
                    atlas.TryAcquire("null/direct", source, out DynamicAtlasSpriteLease direct),
                    Is.EqualTo(DynamicAtlasInsertStatus.Disabled));
                Assert.That(direct.IsValid, Is.False);

                Assert.That(
                    atlas.TryAcquireRegion("null/region", source, new RectInt(0, 0, 4, 4), out DynamicAtlasSpriteLease region),
                    Is.EqualTo(DynamicAtlasInsertStatus.Disabled));
                Assert.That(region.IsValid, Is.False);

                Sprite sprite = Sprite.Create(
                    source,
                    new Rect(0f, 0f, 8f, 8f),
                    new Vector2(0.5f, 0.5f),
                    100f);
                Assert.That(
                    atlas.TryAcquireSprite("null/sprite", sprite, out DynamicAtlasSpriteLease spriteLease),
                    Is.EqualTo(DynamicAtlasInsertStatus.Disabled));
                Assert.That(spriteLease.IsValid, Is.False);

                Assert.That(
                    atlas.TryAcquireLocation("null/location", out DynamicAtlasSpriteLease location),
                    Is.EqualTo(DynamicAtlasInsertStatus.Disabled));
                Assert.That(location.IsValid, Is.False);

                Assert.That(atlas.TryAcquireCached("null/direct", out _), Is.False);
                Assert.That(atlas.TryGetSprite("null/direct", out Sprite cached), Is.False);
                Assert.That(cached, Is.Null);

                Assert.That(atlas.TrimUnused(), Is.Zero);
                Assert.That(atlas.PrewarmPages(2, DynamicAtlasPageMode.GpuOnly), Is.Zero);

                var pages = new List<DynamicAtlasPageSnapshot>(2);
                var entries = new List<DynamicAtlasEntrySnapshot>(2);
                Assert.That(atlas.CopyPageSnapshots(pages), Is.Zero);
                Assert.That(atlas.CopyEntrySnapshots(entries), Is.Zero);
                Assert.That(pages, Is.Empty);
                Assert.That(entries, Is.Empty);

                DynamicAtlasStats stats = atlas.GetStats();
                Assert.That(stats.PageCount, Is.Zero);
                Assert.That(stats.EntryCount, Is.Zero);
                Assert.That(stats.ActiveReferenceCount, Is.Zero);

                UnityEngine.Object.DestroyImmediate(sprite);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void NullAtlas_BatchScopeIsDisposableAndReusable()
        {
            IDynamicAtlas atlas = NullDynamicAtlas.Shared;

            DynamicAtlasWriteBatch first = atlas.BeginBatch();
            Assert.That(first, Is.Not.Null);

            Assert.DoesNotThrow(() => first.Dispose());
            Assert.DoesNotThrow(() => first.Dispose());

            // Disposal must not fault the shared instance: a disabled build still calls the contract.
            using (atlas.BeginBatch())
            {
                Assert.That(
                    atlas.TryAcquire("null/after-batch", null, out _),
                    Is.EqualTo(DynamicAtlasInsertStatus.Disabled));
            }
        }

        [Test]
        public void NullAtlas_SurvivesDisposeAndIsStableAcrossCalls()
        {
            IDynamicAtlas atlas = NullDynamicAtlas.Shared;

            atlas.Clear();
            atlas.Dispose();

            Assert.That(
                atlas.TryAcquire("null/after-dispose", null, out _),
                Is.EqualTo(DynamicAtlasInsertStatus.Disabled),
                "the shared no-op atlas must remain usable after Dispose");
            Assert.That(ReferenceEquals(atlas, NullDynamicAtlas.Shared), Is.True);
        }

        [Test]
        public void Manager_LowMemoryTrimsOwnedService()
        {
            var gameObject = new GameObject("DynamicAtlasLowMemoryOwned");
            gameObject.SetActive(false);
            Texture2D source = CreateTexture(8);

            try
            {
                DynamicAtlasManager manager = gameObject.AddComponent<DynamicAtlasManager>();
                manager.Initialize(CreateConfig());
                Assert.That(manager.IsInitialized, Is.True);

                gameObject.SetActive(true);

                Assert.That(
                    manager.TryAcquire("lowmem/a", source, out DynamicAtlasSpriteLease a),
                    Is.EqualTo(DynamicAtlasInsertStatus.Success));
                Assert.That(
                    manager.TryAcquire("lowmem/b", source, out DynamicAtlasSpriteLease b),
                    Is.EqualTo(DynamicAtlasInsertStatus.Success));
                a.Dispose();
                b.Dispose();

                Assert.That(manager.Service.GetStats().EntryCount, Is.EqualTo(2));
                Assert.That(manager.Service.GetStats().ActiveReferenceCount, Is.Zero);

                TestReflection.Invoke(manager, "HandleLowMemory");

                DynamicAtlasStats after = manager.Service.GetStats();
                TestContext.WriteLine(
                    $"LOWMEMORY|owned=true|entriesAfterTrim={after.EntryCount}|pages={after.PageCount}");
                Assert.That(after.EntryCount, Is.Zero, "an owned service must drop retained entries on memory pressure");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void Manager_LowMemoryLeavesInjectedServiceAlone()
        {
            var gameObject = new GameObject("DynamicAtlasLowMemoryInjected");
            gameObject.SetActive(false);
            Texture2D source = CreateTexture(8);
            var injected = new DynamicAtlasService(CreateConfig());

            try
            {
                DynamicAtlasManager manager = gameObject.AddComponent<DynamicAtlasManager>();
                manager.SetService(injected, takeOwnership: false);
                gameObject.SetActive(true);

                Assert.That(
                    manager.TryAcquire("lowmem/injected", source, out DynamicAtlasSpriteLease lease),
                    Is.EqualTo(DynamicAtlasInsertStatus.Success));
                lease.Dispose();

                Assert.That(injected.GetStats().EntryCount, Is.EqualTo(1));

                TestReflection.Invoke(manager, "HandleLowMemory");

                DynamicAtlasStats after = injected.GetStats();
                TestContext.WriteLine(
                    $"LOWMEMORY|owned=false|entriesAfterTrim={after.EntryCount}|pages={after.PageCount}");
                Assert.That(
                    after.EntryCount,
                    Is.EqualTo(1),
                    "an injected service is trimmed by its owner, not by the host component");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(gameObject);
                injected.Dispose();
            }
        }
    }
}
