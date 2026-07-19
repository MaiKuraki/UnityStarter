using CycloneGames.AssetManagement.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CycloneGames.AssetManagement.Tests.Editor
{
    public sealed class AssetRefTests
    {
        [Test]
        public void AssetRef_Equality_Uses_Location_Only()
        {
            var left = new AssetRef<Texture2D>("Assets/Icon.png", "guid-a");
            var right = new AssetRef<Texture2D>("Assets/Icon.png", "guid-b");
            var other = new AssetRef<Texture2D>("Assets/Other.png", "guid-a");

            Assert.IsTrue(left == right);
            Assert.IsTrue(left.Equals(right));
            Assert.IsTrue(left != other);
            Assert.AreEqual(left.GetHashCode(), right.GetHashCode());
        }

        [Test]
        public void AssetRef_Converts_Between_Typed_And_Untyped()
        {
            var typed = new AssetRef<Texture2D>("Assets/Icon.png", "guid-a");
            AssetRef untyped = typed.Untyped();
            AssetRef<Sprite> spriteRef = untyped.Typed<Sprite>();

            Assert.AreEqual("Assets/Icon.png", untyped.Location);
            Assert.AreEqual("Assets/Icon.png", spriteRef.Location);
            Assert.IsTrue(untyped.IsValid);
            Assert.AreEqual("Assets/Icon.png", untyped.ToString());
            Assert.AreEqual("Assets/Icon.png", (string)typed);
        }

        [Test]
        public void AssetRef_Default_Is_Invalid_And_String_Empty()
        {
            var assetRef = default(AssetRef);
            var typedRef = default(AssetRef<Texture2D>);

            Assert.IsFalse(assetRef.IsValid);
            Assert.IsFalse(typedRef.IsValid);
            Assert.AreEqual(string.Empty, assetRef.ToString());
            Assert.AreEqual(string.Empty, typedRef.ToString());
        }

        [Test]
        public void SceneRef_Equality_Uses_Location_Only()
        {
            var left = new SceneRef("Scenes/Main", "guid-a");
            var right = new SceneRef("Scenes/Main", "guid-b");
            var other = new SceneRef("Scenes/Other", "guid-a");

            Assert.IsTrue(left == right);
            Assert.IsTrue(left != other);
            Assert.AreEqual("Scenes/Main", (string)left);
            Assert.AreEqual("Scenes/Main", left.ToString());
        }

        [Test]
        public void AssetRefExtensions_Forward_Location_Metadata_To_Package()
        {
            var package = new RecordingAssetPackage();
            var assetRef = new AssetRef<Texture2D>("Assets/Icon.png");

            package.LoadAsync(assetRef, "UI", "tag", "owner");

            Assert.AreEqual("LoadAssetAsync", package.LastCall);
            Assert.AreEqual("Assets/Icon.png", package.LastLocation);
            Assert.AreEqual("UI", package.LastBucket);
            Assert.AreEqual("tag", package.LastTag);
            Assert.AreEqual("owner", package.LastOwner);
        }

        [Test]
        public void SceneRefExtensions_Forward_Location_Metadata_To_Package()
        {
            var package = new RecordingAssetPackage();
            var sceneRef = new SceneRef("Scenes/Main");

            var loadParameters = new LoadSceneParameters(LoadSceneMode.Additive)
            {
                localPhysicsMode = LocalPhysicsMode.Physics2D
            };
            package.LoadSceneAsync(sceneRef, loadParameters, SceneActivationMode.Manual, 50, "Scenes");

            Assert.AreEqual("LoadSceneAsyncParameters", package.LastCall);
            Assert.AreEqual("Scenes/Main", package.LastLocation);
            Assert.AreEqual("Scenes", package.LastBucket);
            Assert.AreEqual(LocalPhysicsMode.Physics2D, package.LastLocalPhysicsMode);
            Assert.AreEqual(SceneActivationMode.Manual, package.LastActivationMode);
            Assert.AreEqual(50, package.LastPriority);
        }
    }
}
