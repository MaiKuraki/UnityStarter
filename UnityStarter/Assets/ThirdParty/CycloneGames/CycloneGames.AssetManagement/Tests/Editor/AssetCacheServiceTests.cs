using System;
using UnityEngine;
using NUnit.Framework;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.AssetManagement.Runtime.Cache;

namespace CycloneGames.AssetManagement.Tests.Editor
{
    public sealed class AssetCacheServiceTests
    {
        [Test]
        public void BuildCacheKey_Separates_Operation_Kinds()
        {
            const string location = "Assets/Test/Icon.png";

            string assetKey = AssetCacheService.BuildCacheKey(location, typeof(Texture2D), AssetCacheOperationKind.Asset);
            string allAssetsKey = AssetCacheService.BuildCacheKey(location, typeof(Texture2D), AssetCacheOperationKind.AllAssets);
            string rawKey = AssetCacheService.BuildCacheKey(location, null, AssetCacheOperationKind.RawFile);

            Assert.AreNotEqual(assetKey, allAssetsKey);
            Assert.AreNotEqual(assetKey, rawKey);
            Assert.AreNotEqual(allAssetsKey, rawKey);
        }

        [Test]
        public void ParseCacheKey_Returns_Display_Location_And_Type()
        {
            const string location = "Assets/Test/Icon.png";
            string cacheKey = AssetCacheService.BuildCacheKey(location, typeof(Texture2D), AssetCacheOperationKind.Asset);

            AssetCacheService.ParseCacheKey(cacheKey, out string parsedLocation, out string parsedType);

            Assert.AreEqual(location, parsedLocation);
            Assert.AreEqual(nameof(Texture2D), parsedType);
        }

        [Test]
        public void Cache_Does_Not_Return_Asset_For_AllAssets_Key()
        {
            const string location = "Assets/Test/Icon.png";
            var package = new RecordingAssetPackage();
            using var cache = new AssetCacheService(package, maxTrialEntries: 1, maxMainEntries: 1);

            string assetKey = AssetCacheService.BuildCacheKey(location, typeof(Texture2D), AssetCacheOperationKind.Asset);
            string allAssetsKey = AssetCacheService.BuildCacheKey(location, typeof(Texture2D), AssetCacheOperationKind.AllAssets);
            var handle = new TestAssetHandle<Texture2D>();

            cache.RegisterNew(assetKey, bucket: null, tag: null, owner: null, handle);

            Assert.IsNull(cache.Get(allAssetsKey, bucket: null, tag: null, owner: null));
        }

        [Test]
        public void TrimIdle_Evicts_Only_After_Minimum_Idle_Time()
        {
            const string location = "Assets/Test/Icon.png";
            var package = new RecordingAssetPackage();
            var clock = new ManualAssetCacheClock();
            using var cache = new AssetCacheService(package, 4, 4, 64L * 1024 * 1024, clock);

            string cacheKey = AssetCacheService.BuildCacheKey(location, typeof(Texture2D), AssetCacheOperationKind.Asset);
            var handle = new TestAssetHandle<Texture2D>();
            cache.RegisterNew(cacheKey, bucket: "UI.Shop", tag: "UI", owner: "Shop", handle);
            ReleaseToIdle(cache, cacheKey, handle);

            var policy = AssetCacheRetentionPolicy.IdleForAtLeast(TimeSpan.FromSeconds(60d));

            clock.AdvanceSeconds(59d);
            Assert.AreEqual(0, cache.TrimIdle(policy));
            Assert.IsTrue(cache.Contains(cacheKey));

            clock.AdvanceSeconds(1d);
            Assert.AreEqual(1, cache.TrimIdle(policy));
            Assert.IsFalse(cache.Contains(cacheKey));
        }

        [Test]
        public void TrimIdle_Composes_Global_And_Bucket_Retention_Rules()
        {
            var package = new RecordingAssetPackage();
            var clock = new ManualAssetCacheClock();
            using var cache = new AssetCacheService(package, 8, 8, 64L * 1024 * 1024, clock);

            string sceneKey = AssetCacheService.BuildCacheKey("Assets/Test/SceneIcon.png", typeof(Texture2D), AssetCacheOperationKind.Asset);
            string sharedKey = AssetCacheService.BuildCacheKey("Assets/Test/SharedIcon.png", typeof(Texture2D), AssetCacheOperationKind.Asset);
            var sceneHandle = new TestAssetHandle<Texture2D>();
            var sharedHandle = new TestAssetHandle<Texture2D>();

            cache.RegisterNew(sceneKey, bucket: "Scene.Battle.UI", tag: "UI", owner: "BattleHud", sceneHandle);
            cache.RegisterNew(sharedKey, bucket: "Shared.Foundation", tag: "Shared", owner: "Bootstrap", sharedHandle);
            ReleaseToIdle(cache, sceneKey, sceneHandle);
            ReleaseToIdle(cache, sharedKey, sharedHandle);

            var policy = AssetCacheRetentionPolicy.MatchingAny(
                AssetCacheRetentionRules.IdleForAtLeast(TimeSpan.FromSeconds(120d)),
                AssetCacheRetentionRules.All(
                    AssetCacheRetentionRules.Bucket("Scene.Battle", includeChildren: true),
                    AssetCacheRetentionRules.IdleForAtLeast(TimeSpan.FromSeconds(30d))));

            clock.AdvanceSeconds(40d);
            Assert.AreEqual(1, cache.TrimIdle(policy));
            Assert.IsFalse(cache.Contains(sceneKey));
            Assert.IsTrue(cache.Contains(sharedKey));

            clock.AdvanceSeconds(90d);
            Assert.AreEqual(1, cache.TrimIdle(policy));
            Assert.IsFalse(cache.Contains(sharedKey));
        }

        private static void ReleaseToIdle(AssetCacheService cache, string cacheKey, IReferenceCounted handle)
        {
            handle.Release();
            cache.OnHandleReleased(cacheKey, handle);
        }

        private sealed class ManualAssetCacheClock : IAssetCacheClock
        {
            private double _seconds;

            public long Timestamp => (long)(_seconds * 1000d);

            public void AdvanceSeconds(double seconds)
            {
                _seconds += seconds;
            }

            public TimeSpan GetElapsed(long startTimestamp, long endTimestamp)
            {
                long delta = endTimestamp - startTimestamp;
                return delta <= 0L ? TimeSpan.Zero : TimeSpan.FromSeconds(delta / 1000d);
            }
        }
    }
}
