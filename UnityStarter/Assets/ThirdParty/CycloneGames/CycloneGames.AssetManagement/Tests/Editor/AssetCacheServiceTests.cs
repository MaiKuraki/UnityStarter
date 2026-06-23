using CycloneGames.AssetManagement.Runtime.Cache;
using NUnit.Framework;
using UnityEngine;

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
    }
}
