using System;

using NUnit.Framework;

using CycloneGames.AssetManagement.Runtime;

namespace CycloneGames.AssetManagement.Tests.Editor
{
    public sealed class AssetStoragePreflightTests
    {
        [Test]
        public void Storage_Contracts_Reject_Invalid_Adapter_Data()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new AssetStoragePreflightRequest(-1L));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new AssetStoragePreflightResult(AssetStorageCapacityStatus.Available));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new AssetStoragePreflightResult((AssetStorageCapacityStatus)255, 0L));
        }

        [Test]
        public void Default_Result_Is_Unknown()
        {
            Assert.AreEqual(
                AssetStorageCapacityStatus.Unknown,
                default(AssetStoragePreflightResult).Status);
        }

        [Test]
        public void Volume_Matching_Uses_Directory_Boundaries_And_Platform_Case_Semantics()
        {
            Assert.IsTrue(AssetStorageVolumeUtility.IsPathWithinRoot(
                "/mnt/data/cache/file.bundle",
                "/mnt/data",
                caseInsensitive: false));
            Assert.IsFalse(AssetStorageVolumeUtility.IsPathWithinRoot(
                "/mnt/database/cache/file.bundle",
                "/mnt/data",
                caseInsensitive: false));
            Assert.IsFalse(AssetStorageVolumeUtility.IsPathWithinRoot(
                "/MNT/DATA/cache/file.bundle",
                "/mnt/data",
                caseInsensitive: false));
            Assert.IsTrue(AssetStorageVolumeUtility.IsPathWithinRoot(
                "C:\\CACHE\\file.bundle",
                "c:\\cache",
                caseInsensitive: true));
        }
    }
}
