using System;

using NUnit.Framework;

namespace CycloneGames.AssetManagement.Runtime.Tests
{
    public sealed class AssetBundleRuntimeOwnershipTests
    {
        [SetUp]
        public void SetUp()
        {
            AssetBundleRuntimeOwnership.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            AssetBundleRuntimeOwnership.Reset();
        }

        [Test]
        public void Acquire_SameOwnerIsIdempotent()
        {
            var owner = new object();

            AssetBundleRuntimeOwnership.Acquire(owner, "Addressables");
            AssetBundleRuntimeOwnership.Acquire(owner, "Addressables");

            Assert.That(AssetBundleRuntimeOwnership.CurrentProviderName, Is.EqualTo("Addressables"));
        }

        [Test]
        public void Acquire_DifferentOwnerFailsFast()
        {
            AssetBundleRuntimeOwnership.Acquire(new object(), "Addressables");

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                AssetBundleRuntimeOwnership.Acquire(new object(), "YooAsset"));

            StringAssert.Contains("Addressables", exception.Message);
            StringAssert.Contains("YooAsset", exception.Message);
        }

        [Test]
        public void Release_NonOwnerFailsFast()
        {
            AssetBundleRuntimeOwnership.Acquire(new object(), "Addressables");

            Assert.Throws<InvalidOperationException>(() =>
                AssetBundleRuntimeOwnership.Release(new object()));
        }

        [Test]
        public void Release_CurrentOwnerAllowsAnotherProvider()
        {
            var addressablesOwner = new object();
            AssetBundleRuntimeOwnership.Acquire(addressablesOwner, "Addressables");
            AssetBundleRuntimeOwnership.Release(addressablesOwner);

            AssetBundleRuntimeOwnership.Acquire(new object(), "YooAsset");

            Assert.That(AssetBundleRuntimeOwnership.CurrentProviderName, Is.EqualTo("YooAsset"));
        }
    }
}
