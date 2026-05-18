using CycloneGames.AssetManagement.Runtime;
using NUnit.Framework;

namespace CycloneGames.AssetManagement.Tests.Editor
{
    public sealed class AssetBucketPathTests
    {
        [Test]
        public void Combine_Handles_Empty_And_Null_Segments()
        {
            Assert.AreEqual("child", AssetBucketPath.Combine(null, "child"));
            Assert.AreEqual("parent", AssetBucketPath.Combine("parent", null));
            Assert.AreEqual(string.Empty, AssetBucketPath.Combine(null, null));
        }

        [Test]
        public void Combine_Uses_Dot_Separated_Hierarchy()
        {
            Assert.AreEqual("UI.Scene.MainCity", AssetBucketPath.Combine("UI", "Scene", "MainCity"));
        }

        [Test]
        public void IsPrefixMatch_Requires_Hierarchy_Boundary()
        {
            Assert.IsTrue(AssetBucketPath.IsPrefixMatch("UI.Scene", "UI"));
            Assert.IsTrue(AssetBucketPath.IsPrefixMatch("UI", "UI"));
            Assert.IsFalse(AssetBucketPath.IsPrefixMatch("UIX.Scene", "UI"));
            Assert.IsFalse(AssetBucketPath.IsPrefixMatch("UI", "UI.Scene"));
            Assert.IsFalse(AssetBucketPath.IsPrefixMatch(null, "UI"));
            Assert.IsFalse(AssetBucketPath.IsPrefixMatch("UI", null));
        }
    }
}
