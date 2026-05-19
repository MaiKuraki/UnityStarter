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
    }
}
