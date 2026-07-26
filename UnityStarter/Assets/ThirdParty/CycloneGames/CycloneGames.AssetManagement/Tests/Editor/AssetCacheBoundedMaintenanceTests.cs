using CycloneGames.AssetManagement.Runtime;
using CycloneGames.AssetManagement.Runtime.Cache;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.AssetManagement.Tests.Editor
{
    public sealed class AssetCacheBoundedMaintenanceTests
    {
        [Test]
        public void TrimIdleStep_IsBoundedAndLegacyTrimStillCompletesAllMatchingWork()
        {
            using var cache = new AssetCacheService(
                new RecordingAssetPackage(),
                maxTrialEntries: 8,
                maxMainEntries: 8,
                maxIdleBytes: 1024L * 1024L);

            for (int index = 0; index < 5; index++)
            {
                AssetCacheKey key = AssetCacheService.BuildCacheKey("Idle" + index, typeof(Texture2D));
                var handle = new TestAssetHandle<Texture2D>();
                cache.RegisterNew(key, null, null, null, handle);
                handle.Release();
                cache.OnHandleReleased(key, handle);
            }

            AssetCacheKey activeKey = AssetCacheService.BuildCacheKey("Active", typeof(Texture2D));
            cache.RegisterNew(activeKey, null, null, null, new TestAssetHandle<Texture2D>());

            AssetCacheTrimResult step = cache.TrimIdleStep(2);

            Assert.That(step.WorkConsumed, Is.EqualTo(2));
            Assert.That(step.EvictedCount, Is.EqualTo(2));
            Assert.That(step.RemainingIdleCount, Is.EqualTo(3));
            Assert.That(cache.ActiveCount, Is.EqualTo(1));
            Assert.That(cache.CreateRuntimeSnapshot("Test", "Test").PressureEvictionCount, Is.EqualTo(2L));

            Assert.That(cache.TrimIdle(AssetCacheRetentionPolicy.EvictAllIdle), Is.EqualTo(3));
            Assert.That(cache.IdleCount, Is.Zero);
            Assert.That(cache.ActiveCount, Is.EqualTo(1));
            Assert.That(cache.TrimIdleStep(2).WorkConsumed, Is.Zero);
        }
    }
}
