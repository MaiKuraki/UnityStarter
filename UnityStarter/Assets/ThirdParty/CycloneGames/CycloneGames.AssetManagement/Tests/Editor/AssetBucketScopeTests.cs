using System;
using CycloneGames.AssetManagement.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CycloneGames.AssetManagement.Tests.Editor
{
    public sealed class AssetBucketScopeTests
    {
        [Test]
        public void Constructor_Rejects_Null_Package()
        {
            Assert.Throws<ArgumentNullException>(() => new AssetBucketScope(null, "UI"));
        }

        [Test]
        public void CreateChild_Inherits_Metadata_And_Appends_Bucket()
        {
            var package = new RecordingAssetPackage();
            var root = new AssetBucketScope(package, "UI", "ui-tag", "ui-owner");
            var child = root.CreateChild("Scene");

            Assert.AreSame(package, child.Package);
            Assert.AreEqual("UI.Scene", child.Bucket);
            Assert.AreEqual("ui-tag", child.Tag);
            Assert.AreEqual("ui-owner", child.Owner);
        }

        [Test]
        public void LoadAssetSync_Forwards_Bucket_Tag_And_Owner()
        {
            var package = new RecordingAssetPackage();
            var scope = new AssetBucketScope(package, "UI", "default-tag", "default-owner");

            scope.LoadAssetSync<Texture2D>("Assets/Icon.png");

            Assert.AreEqual("LoadAssetSync", package.LastCall);
            Assert.AreEqual("Assets/Icon.png", package.LastLocation);
            Assert.AreEqual("UI", package.LastBucket);
            Assert.AreEqual("default-tag", package.LastTag);
            Assert.AreEqual("default-owner", package.LastOwner);
        }

        [Test]
        public void LoadAssetAsync_Overrides_Tag_And_Owner_When_Provided()
        {
            var package = new RecordingAssetPackage();
            var scope = new AssetBucketScope(package, "UI", "default-tag", "default-owner");

            scope.LoadAssetAsync<Texture2D>("Assets/Icon.png", "override-tag", "override-owner");

            Assert.AreEqual("LoadAssetAsync", package.LastCall);
            Assert.AreEqual("Assets/Icon.png", package.LastLocation);
            Assert.AreEqual("UI", package.LastBucket);
            Assert.AreEqual("override-tag", package.LastTag);
            Assert.AreEqual("override-owner", package.LastOwner);
        }

        [Test]
        public void Clear_And_ClearHierarchy_Forward_Bucket()
        {
            var package = new RecordingAssetPackage();
            var scope = new AssetBucketScope(package, "UI.Scene");

            scope.Clear();
            Assert.AreEqual("ClearBucket", package.LastCall);
            Assert.AreEqual("UI.Scene", package.LastBucket);

            scope.ClearHierarchy();
            Assert.AreEqual("ClearBucketsByPrefix", package.LastCall);
            Assert.AreEqual("UI.Scene", package.LastBucket);
        }

        [Test]
        public void LoadSceneAsync_Forwards_Manual_Activation_Parameters()
        {
            var package = new RecordingAssetPackage();
            var scope = new AssetBucketScope(package, "SceneBucket");

            var loadParameters = new LoadSceneParameters(LoadSceneMode.Additive)
            {
                localPhysicsMode = LocalPhysicsMode.Physics3D
            };
            scope.LoadSceneAsync("Scenes/Main", loadParameters, SceneActivationMode.Manual, 25);

            Assert.AreEqual("LoadSceneAsyncParameters", package.LastCall);
            Assert.AreEqual("Scenes/Main", package.LastLocation);
            Assert.AreEqual("SceneBucket", package.LastBucket);
            Assert.AreEqual(LoadSceneMode.Additive, package.LastLoadMode);
            Assert.AreEqual(LocalPhysicsMode.Physics3D, package.LastLocalPhysicsMode);
            Assert.AreEqual(SceneActivationMode.Manual, package.LastActivationMode);
            Assert.AreEqual(25, package.LastPriority);
        }
    }
}
