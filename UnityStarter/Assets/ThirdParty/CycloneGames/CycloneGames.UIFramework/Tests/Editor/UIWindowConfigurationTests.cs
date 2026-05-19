using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.UIFramework.Runtime;

namespace CycloneGames.UIFramework.Tests.Editor
{
    public sealed class UIWindowConfigurationTests
    {
        private UIWindowConfiguration _config;
        private GameObject _prefabObject;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<UIWindowConfiguration>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_prefabObject != null)
            {
                Object.DestroyImmediate(_prefabObject);
            }

            Object.DestroyImmediate(_config);
        }

        [Test]
        public void PrefabReferenceMode_ReportsConfiguredWhenWindowPrefabExists()
        {
            _prefabObject = new GameObject("UIWindow_TestPrefab");
            var window = _prefabObject.AddComponent<UIWindow>();

            SetPrivateField(_config, "source", UIWindowConfiguration.PrefabSource.PrefabReference);
            SetPrivateField(_config, "windowPrefab", window);

            Assert.IsTrue(_config.IsConfigured);
            Assert.AreSame(window, _config.WindowPrefab);
            Assert.AreEqual(string.Empty, _config.EffectiveLocation);
        }

        [Test]
        public void AssetReferenceMode_UsesAssetRefLocationAsEffectiveLocation()
        {
            var assetRef = new AssetRef<GameObject>("UI/Windows/Inventory.prefab", "guid-inventory");

            SetPrivateField(_config, "source", UIWindowConfiguration.PrefabSource.AssetReference);
            SetPrivateField(_config, "prefabAssetRef", assetRef);

            Assert.IsTrue(_config.IsConfigured);
            Assert.AreEqual("UI/Windows/Inventory.prefab", _config.EffectiveLocation);
            Assert.IsNull(_config.PrefabLocation);
        }

        [Test]
        public void PathLocationMode_UsesRawLocationAndDoesNotRequirePrefabReference()
        {
            SetPrivateField(_config, "source", UIWindowConfiguration.PrefabSource.PathLocation);
            SetPrivateField(_config, "prefabLocation", "Resources/UI/Settings");

            Assert.IsTrue(_config.IsConfigured);
            Assert.AreEqual("Resources/UI/Settings", _config.EffectiveLocation);
            Assert.IsNull(_config.WindowPrefab);
        }

        [Test]
        public void EmptyLocationModes_ReportNotConfigured()
        {
            SetPrivateField(_config, "source", UIWindowConfiguration.PrefabSource.PathLocation);
            SetPrivateField(_config, "prefabLocation", string.Empty);
            Assert.IsFalse(_config.IsConfigured);

            SetPrivateField(_config, "source", UIWindowConfiguration.PrefabSource.AssetReference);
            SetPrivateField(_config, "prefabAssetRef", default(AssetRef<GameObject>));
            Assert.IsFalse(_config.IsConfigured);
        }

        [Test]
        public void PrioritySceneBindingAndCanvasPolicy_ReturnSerializedValues()
        {
            SetPrivateField(_config, "priority", 128);
            SetPrivateField(_config, "isSceneBound", true);
            SetPrivateField(_config, "subCanvasPolicy", UIWindowConfiguration.SubCanvasPolicy.ForceOwnSubCanvas);

            Assert.AreEqual(128, _config.Priority);
            Assert.IsTrue(_config.IsSceneBound);
            Assert.AreEqual(UIWindowConfiguration.SubCanvasPolicy.ForceOwnSubCanvas, _config.CanvasIsolationPolicy);
        }

        private static void SetPrivateField<T>(UIWindowConfiguration config, string fieldName, T value)
        {
            FieldInfo field = typeof(UIWindowConfiguration).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, fieldName);
            field.SetValue(config, value);
        }
    }
}
