using NUnit.Framework;
using UnityEngine;
using CycloneGames.UIFramework.DynamicAtlas;

namespace CycloneGames.UIFramework.Tests.Editor
{
    public sealed class DynamicAtlasConfigTests
    {
        [Test]
        public void CreateForTier_WebGL_UsesConservativeSingleThreadedProfile()
        {
            DynamicAtlasConfig config = DynamicAtlasConfig.CreateForTier(DynamicAtlasConfig.PlatformTier.WebGL, useCompression: true);

            Assert.AreEqual(1024, config.pageSize);
            Assert.AreEqual(2, config.maxPages);
            Assert.AreEqual(TextureFormat.RGBA32, config.targetFormat);
            Assert.IsFalse(config.enablePlatformOptimizations);
            Assert.IsFalse(config.enableBlockAlignment);
            Assert.IsFalse(config.allowCpuReadPixelsFallback);
            Assert.IsFalse(config.allowCpuBleedFallback);
        }

        [Test]
        public void Validate_RejectsInvalidPaddingAndCpuFallbackCombination()
        {
            var config = new DynamicAtlasConfig
            {
                targetFormat = TextureFormat.RGBA32,
                padding = 1,
                enableBleed = true
            };

            Assert.IsFalse(config.Validate(out string paddingError));
            Assert.That(paddingError, Does.Contain("padding"));

            config.padding = 2;
            config.allowCpuReadPixelsFallback = false;
            config.allowCpuBleedFallback = true;

            Assert.IsFalse(config.Validate(out string fallbackError));
            Assert.That(fallbackError, Does.Contain("allowCpuBleedFallback"));
        }

        [Test]
        public void TextureFormatHelper_AlignsCompressedBlocksAndLeavesUncompressedSizesUnchanged()
        {
            Assert.AreEqual(4, TextureFormatHelper.GetBlockSize(TextureFormat.ASTC_4x4));
            Assert.AreEqual(16, TextureFormatHelper.AlignToBlockSize(13, 4));
            Assert.AreEqual(13, TextureFormatHelper.AlignToBlockSize(13, 1));
            Assert.IsTrue(TextureFormatHelper.RequiresBlockAlignment(TextureFormat.ASTC_4x4));
            Assert.IsFalse(TextureFormatHelper.RequiresBlockAlignment(TextureFormat.RGBA32));
        }

        [Test]
        public void DynamicAtlasPage_TryInsert_TracksUsageAndAllocatedArea()
        {
            Texture2D source = CreateReadableTexture(8, 8, Color.red);
            var page = new DynamicAtlasPage(32, TextureFormat.RGBA32, padding: 2, enablePlatformOptimizations: true, enableBleed: false);

            try
            {
                Assert.IsTrue(page.TryInsert(source, out Rect uvRect, out Vector2Int allocatedSize));

                Assert.AreEqual(new Vector2Int(8, 8), allocatedSize);
                Assert.AreEqual(8f / 32f, uvRect.width);
                Assert.AreEqual(8f / 32f, uvRect.height);
                Assert.AreEqual(64L, page.AllocatedPixelArea);

                page.IncrementActiveCount(8, 8);

                Assert.AreEqual(1, page.ActiveSpriteCount);
                Assert.AreEqual(64L, page.UsedPixelArea);
                Assert.Less(page.FragmentationRatio, 1f);
            }
            finally
            {
                page.Dispose();
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void DynamicAtlasService_GetSpriteFromRegion_ReusesCacheAndReleasesPage()
        {
            Texture2D source = CreateReadableTexture(16, 16, Color.green);
            var service = new DynamicAtlasService(new DynamicAtlasConfig
            {
                pageSize = 64,
                padding = 2,
                targetFormat = TextureFormat.RGBA32,
                enableBleed = false,
                enablePlatformOptimizations = true,
                allowCpuReadPixelsFallback = true,
                allowCpuBleedFallback = false
            });

            try
            {
                Rect sourceRect = new Rect(0, 0, 16, 16);
                Sprite first = service.GetSpriteFromRegion(source, sourceRect, "test/sprite");
                Sprite second = service.GetSpriteFromRegion(source, sourceRect, "test/sprite");

                Assert.IsNotNull(first);
                Assert.AreSame(first, second);

                DynamicAtlasService.AtlasMetrics loadedMetrics = service.GetMetrics();
                Assert.AreEqual(1, loadedMetrics.PageCount);
                Assert.AreEqual(1, loadedMetrics.CachedItemCount);
                Assert.AreEqual(1, loadedMetrics.ActiveSpriteCount);
                Assert.AreEqual(256L, loadedMetrics.UsedPixelArea);
                Assert.AreEqual(256L, loadedMetrics.AllocatedPixelArea);

                service.ReleaseSprite("test/sprite");
                DynamicAtlasService.AtlasMetrics retainedMetrics = service.GetMetrics();
                Assert.AreEqual(1, retainedMetrics.PageCount);
                Assert.AreEqual(1, retainedMetrics.CachedItemCount);
                Assert.AreEqual(1, retainedMetrics.ActiveSpriteCount);

                service.ReleaseSprite("test/sprite");
                DynamicAtlasService.AtlasMetrics releasedMetrics = service.GetMetrics();
                Assert.AreEqual(0, releasedMetrics.PageCount);
                Assert.AreEqual(0, releasedMetrics.CachedItemCount);
                Assert.AreEqual(0, releasedMetrics.ActiveSpriteCount);
            }
            finally
            {
                service.Dispose();
                Object.DestroyImmediate(source);
            }
        }

        private static Texture2D CreateReadableTexture(int width, int height, Color color)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color32[width * height];
            Color32 pixel = color;

            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = pixel;
            }

            texture.SetPixels32(pixels);
            texture.Apply(false);
            return texture;
        }
    }
}
