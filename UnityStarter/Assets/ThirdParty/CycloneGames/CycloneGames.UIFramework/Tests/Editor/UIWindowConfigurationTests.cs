using CycloneGames.UIFramework.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.UIFramework.Tests.Editor
{
    public sealed class UIWindowConfigurationTests
    {
        private UIWindowConfiguration _configuration;
        private UILayerConfiguration _layer;
        private GameObject _prefabObject;

        [SetUp]
        public void SetUp()
        {
            _configuration = ScriptableObject.CreateInstance<UIWindowConfiguration>();
            _layer = ScriptableObject.CreateInstance<UILayerConfiguration>();
            TestReflection.SetField(_layer, "layerName", "Main");
            TestReflection.SetField(_configuration, "windowId", "Inventory");
            TestReflection.SetField(_configuration, "layer", _layer);
        }

        [TearDown]
        public void TearDown()
        {
            if (_prefabObject != null)
            {
                Object.DestroyImmediate(_prefabObject);
            }

            Object.DestroyImmediate(_configuration);
            Object.DestroyImmediate(_layer);
        }

        [Test]
        public void PrefabReference_RequiresDirectWindowPrefab()
        {
            TestReflection.SetField(
                _configuration,
                "source",
                UIWindowConfiguration.PrefabSource.PrefabReference);
            Assert.IsFalse(_configuration.IsConfigured);

            _prefabObject = new GameObject("InventoryPrefab", typeof(RectTransform));
            UIWindow prefab = _prefabObject.AddComponent<UIWindow>();
            TestReflection.SetField(_configuration, "windowPrefab", prefab);

            Assert.IsTrue(_configuration.IsConfigured);
            Assert.AreSame(prefab, _configuration.WindowPrefab);
            Assert.IsFalse(_configuration.EffectiveAssetReference.IsValid);
            Assert.AreEqual(string.Empty, _configuration.PrefabLocation);
        }

        [Test]
        public void AssetReference_UsesProviderNeutralRuntimeLocation()
        {
            var assetReference = new UIAssetReference(
                "content/ui/inventory",
                "editor-only-guid");
            TestReflection.SetField(
                _configuration,
                "source",
                UIWindowConfiguration.PrefabSource.AssetReference);
            TestReflection.SetField(_configuration, "prefabAssetRef", assetReference);

            Assert.IsTrue(_configuration.IsConfigured);
            Assert.AreEqual(assetReference, _configuration.PrefabAssetReference);
            Assert.AreEqual(assetReference, _configuration.EffectiveAssetReference);
            Assert.IsNull(_configuration.WindowPrefab);
            Assert.AreEqual(string.Empty, _configuration.PrefabLocation);
        }

        [Test]
        public void AssetReference_WhitespaceRuntimeLocationIsInvalidEvenWithEditorGuid()
        {
            TestReflection.SetField(
                _configuration,
                "source",
                UIWindowConfiguration.PrefabSource.AssetReference);
            TestReflection.SetField(
                _configuration,
                "prefabAssetRef",
                new UIAssetReference("   ", "editor-only-guid"));

            Assert.IsFalse(_configuration.IsConfigured);
            Assert.IsFalse(_configuration.EffectiveAssetReference.IsValid);
        }

        [Test]
        public void PathLocation_ProducesEquivalentProviderReference()
        {
            TestReflection.SetField(
                _configuration,
                "source",
                UIWindowConfiguration.PrefabSource.PathLocation);
            TestReflection.SetField(_configuration, "prefabLocation", "Resources/UI/Inventory");

            Assert.IsTrue(_configuration.IsConfigured);
            Assert.AreEqual("Resources/UI/Inventory", _configuration.PrefabLocation);
            Assert.AreEqual("Resources/UI/Inventory", _configuration.EffectiveAssetReference.Location);
            Assert.AreEqual(string.Empty, _configuration.EffectiveAssetReference.EditorGuid);
            Assert.IsNull(_configuration.WindowPrefab);
        }

        [Test]
        public void MissingIdentityOrLayerMakesEverySourceInvalid()
        {
            _prefabObject = new GameObject("InventoryPrefab", typeof(RectTransform));
            UIWindow prefab = _prefabObject.AddComponent<UIWindow>();
            TestReflection.SetField(_configuration, "windowPrefab", prefab);

            TestReflection.SetField(_configuration, "windowId", " ");
            Assert.IsFalse(_configuration.IsConfigured);

            TestReflection.SetField(_configuration, "windowId", "Inventory");
            TestReflection.SetField(_configuration, "layer", null);
            Assert.IsFalse(_configuration.IsConfigured);
        }

        [Test]
        public void AssetReference_RuntimeIdentityIgnoresEditorGuid()
        {
            var first = new UIAssetReference("ui/inventory", "guid-a");
            var same = new UIAssetReference("ui/inventory", "guid-a");
            var differentGuid = new UIAssetReference("ui/inventory", "guid-b");

            Assert.IsTrue(first.IsValid);
            Assert.AreEqual(first, same);
            Assert.AreEqual(first.GetHashCode(), same.GetHashCode());
            Assert.AreEqual(first, differentGuid);
            Assert.AreEqual(first.GetHashCode(), differentGuid.GetHashCode());
            Assert.IsFalse(default(UIAssetReference).IsValid);
        }

        [Test]
        public void OnValidate_PreservesInactiveSourceValuesWhenAuthorChangesSourceMode()
        {
            _prefabObject = new GameObject("InventoryPrefab", typeof(RectTransform));
            UIWindow prefab = _prefabObject.AddComponent<UIWindow>();
            TestReflection.SetField(_configuration, "windowPrefab", prefab);
            TestReflection.SetField(
                _configuration,
                "source",
                UIWindowConfiguration.PrefabSource.AssetReference);

            TestReflection.Invoke(_configuration, "OnValidate");

            TestReflection.SetField(
                _configuration,
                "source",
                UIWindowConfiguration.PrefabSource.PrefabReference);
            Assert.AreSame(prefab, _configuration.WindowPrefab);
        }

        [Test]
        public void PrioritySceneBindingAndCanvasPolicyExposeSerializedValues()
        {
            TestReflection.SetField(_configuration, "priority", 128);
            TestReflection.SetField(_configuration, "isSceneBound", true);
            TestReflection.SetField(
                _configuration,
                "subCanvasPolicy",
                UIWindowConfiguration.SubCanvasPolicy.IsolatedCanvas);

            Assert.AreEqual(128, _configuration.Priority);
            Assert.IsTrue(_configuration.IsSceneBound);
            Assert.AreEqual(
                UIWindowConfiguration.SubCanvasPolicy.IsolatedCanvas,
                _configuration.CanvasIsolationPolicy);
        }

        [Test]
        public void CanvasPolicyRequiresAnExplicitIsolationDecision()
        {
            System.Array values = System.Enum.GetValues(
                typeof(UIWindowConfiguration.SubCanvasPolicy));

            CollectionAssert.AreEqual(
                new[]
                {
                    UIWindowConfiguration.SubCanvasPolicy.InheritLayerCanvas,
                    UIWindowConfiguration.SubCanvasPolicy.IsolatedCanvas,
                },
                values);
        }
    }
}
