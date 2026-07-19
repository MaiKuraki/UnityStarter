using System.Collections;
using CycloneGames.UIFramework.DynamicAtlas;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CycloneGames.UIFramework.Tests.PlayMode
{
    public sealed class DynamicAtlasPlayModeTests
    {
        [UnityTest]
        public IEnumerator DelayedPageDestruction_RemainsInsideBudgetUntilTheNextFrame()
        {
            DynamicAtlasConfig config = CreateConfig();
            long pageBytes = TextureFormatHelper.EstimatePageBytes(
                config.pageSize,
                DynamicAtlasCopyFallback.AllowCpuRawCopy);
            config.memoryBudgetBytes = pageBytes;
            Texture2D source = CreateTexture(8, 8);
            var service = new DynamicAtlasService(config);

            try
            {
                Assert.That(service.PrewarmPages(1, DynamicAtlasPageMode.CpuBacked), Is.EqualTo(1));
                Assert.That(
                    service.TryAcquire("pending/first", source, out DynamicAtlasSpriteLease first),
                    Is.EqualTo(DynamicAtlasInsertStatus.Success));
                first.Dispose();

                DynamicAtlasStats pending = service.GetStats();
                Assert.That(pending.PageCount, Is.Zero);
                Assert.That(pending.EstimatedTextureBytes, Is.EqualTo(pageBytes));
                Assert.That(pending.PendingDestructionBytes, Is.EqualTo(pageBytes));

                DynamicAtlasInsertStatus sameFrameStatus = service.TryAcquire(
                    "pending/same-frame",
                    source,
                    out DynamicAtlasSpriteLease sameFrameLease);
                Assert.That(sameFrameStatus, Is.EqualTo(DynamicAtlasInsertStatus.MemoryBudgetReached));
                Assert.That(sameFrameLease, Is.Null);

                yield return null;

                DynamicAtlasStats released = service.GetStats();
                Assert.That(released.PendingDestructionBytes, Is.Zero);
                Assert.That(released.EstimatedTextureBytes, Is.Zero);
                Assert.That(
                    service.TryAcquire("pending/next-frame", source, out DynamicAtlasSpriteLease nextFrame),
                    Is.EqualTo(DynamicAtlasInsertStatus.Success));
                nextFrame.Dispose();
            }
            finally
            {
                service.Dispose();
                Object.Destroy(source);
            }
        }

        [UnityTest]
        public IEnumerator DelayedPageDestruction_RemainsInsidePageLimitUntilTheNextFrame()
        {
            DynamicAtlasConfig config = CreateConfig();
            config.maxPages = 1;
            config.memoryBudgetBytes = TextureFormatHelper.EstimatePageBytes(
                config.pageSize,
                DynamicAtlasCopyFallback.AllowCpuRawCopy) * 2L;
            Texture2D source = CreateTexture(8, 8);
            var service = new DynamicAtlasService(config);

            try
            {
                Assert.That(service.PrewarmPages(1, DynamicAtlasPageMode.CpuBacked), Is.EqualTo(1));
                Assert.That(
                    service.TryAcquire("pending-page/first", source, out DynamicAtlasSpriteLease first),
                    Is.EqualTo(DynamicAtlasInsertStatus.Success));
                first.Dispose();

                DynamicAtlasInsertStatus sameFrameStatus = service.TryAcquire(
                    "pending-page/same-frame",
                    source,
                    out DynamicAtlasSpriteLease sameFrameLease);
                Assert.That(sameFrameStatus, Is.EqualTo(DynamicAtlasInsertStatus.PageCapacityReached));
                Assert.That(sameFrameLease, Is.Null);

                yield return null;

                Assert.That(
                    service.TryAcquire("pending-page/next-frame", source, out DynamicAtlasSpriteLease nextFrame),
                    Is.EqualTo(DynamicAtlasInsertStatus.Success));
                nextFrame.Dispose();
            }
            finally
            {
                service.Dispose();
                Object.Destroy(source);
            }
        }

        private static DynamicAtlasConfig CreateConfig()
        {
            return new DynamicAtlasConfig
            {
                pageSize = 64,
                maxPages = 2,
                minRetainedPages = 0,
                maxEntries = 1,
                maxEntriesPerPage = 1,
                maxKeyLength = 64,
                memoryBudgetBytes = 1024L * 1024L,
                padding = 1,
                enableBleed = false,
                retentionPolicy = DynamicAtlasRetentionPolicy.RemoveWhenUnused,
                oversizePolicy = DynamicAtlasOversizePolicy.Reject,
                copyFallback = DynamicAtlasCopyFallback.AllowCpuRawCopy,
            };
        }

        private static Texture2D CreateTexture(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false);
            var pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
            }

            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return texture;
        }
    }
}
