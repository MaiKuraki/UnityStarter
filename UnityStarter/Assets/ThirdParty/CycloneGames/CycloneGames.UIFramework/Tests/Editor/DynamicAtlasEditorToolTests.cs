using System;
using CycloneGames.UIFramework.DynamicAtlas;
using CycloneGames.UIFramework.Editor.DynamicAtlas;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.UIFramework.Tests.Editor
{
    public sealed class DynamicAtlasEditorToolTests
    {
        [Test]
        public void ManagerInspectorSupportsMultiObjectSerializedConfiguration()
        {
            var firstObject = new GameObject("DynamicAtlasManagerEditorTest_First");
            var secondObject = new GameObject("DynamicAtlasManagerEditorTest_Second");
            UnityEditor.Editor inspector = null;

            try
            {
                DynamicAtlasManager first = firstObject.AddComponent<DynamicAtlasManager>();
                DynamicAtlasManager second = secondObject.AddComponent<DynamicAtlasManager>();
                var selectedManagers = new UnityEngine.Object[] { first, second };

                inspector = UnityEditor.Editor.CreateEditor(selectedManagers);

                Assert.That(inspector, Is.TypeOf<DynamicAtlasManagerEditor>());
                Assert.That(
                    Attribute.IsDefined(typeof(DynamicAtlasManagerEditor), typeof(CanEditMultipleObjects), inherit: true),
                    Is.True);

                var serializedManagers = new SerializedObject(selectedManagers);
                SerializedProperty configuration = serializedManagers.FindProperty("configuration");
                SerializedProperty pageSize = configuration.FindPropertyRelative("pageSize");
                pageSize.intValue = 512;

                Assert.That(serializedManagers.ApplyModifiedProperties(), Is.True);
                Assert.That(first.Configuration.pageSize, Is.EqualTo(512));
                Assert.That(second.Configuration.pageSize, Is.EqualTo(512));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(inspector);
                UnityEngine.Object.DestroyImmediate(firstObject);
                UnityEngine.Object.DestroyImmediate(secondObject);
            }
        }

        [Test]
        public void StartingProfileAppliesCompleteBoundedConfigurationToMultipleManagers()
        {
            var firstObject = new GameObject("DynamicAtlasProfileTest_First");
            var secondObject = new GameObject("DynamicAtlasProfileTest_Second");
            try
            {
                DynamicAtlasManager first = firstObject.AddComponent<DynamicAtlasManager>();
                DynamicAtlasManager second = secondObject.AddComponent<DynamicAtlasManager>();
                var selectedManagers = new UnityEngine.Object[] { first, second };
                var serializedManagers = new SerializedObject(selectedManagers);

                DynamicAtlasManagerEditor.ApplyStartingProfile(
                    serializedManagers,
                    DynamicAtlasConfig.PlatformTier.MobileLowEnd);

                DynamicAtlasConfig expected = DynamicAtlasConfig.CreateForTier(
                    DynamicAtlasConfig.PlatformTier.MobileLowEnd);
                AssertConfigurationMatches(first.Configuration, expected);
                AssertConfigurationMatches(second.Configuration, expected);
                Assert.That(
                    first.Configuration.copyFallback,
                    Is.Not.EqualTo(DynamicAtlasCopyFallback.AllowSynchronousReadback));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(firstObject);
                UnityEngine.Object.DestroyImmediate(secondObject);
            }
        }

        [TestCase(false, false, true, false, 0)]
        [TestCase(false, false, true, true, 1)]
        [TestCase(false, false, false, true, 2)]
        [TestCase(true, false, true, true, 3)]
        [TestCase(false, true, true, true, 3)]
        public void SpriteAtlasCompatibilityClassificationMatchesRuntimeCopyRequirements(
            bool rotationEnabled,
            bool tightPackingEnabled,
            bool explicitRgba32,
            bool readable,
            int expected)
        {
            SpriteAtlasFormatValidator.Compatibility actual =
                SpriteAtlasFormatValidator.ClassifyCompatibility(
                    rotationEnabled,
                    tightPackingEnabled,
                    explicitRgba32,
                    readable);

            Assert.That((int)actual, Is.EqualTo(expected));
        }

        private static void AssertConfigurationMatches(
            DynamicAtlasConfig actual,
            DynamicAtlasConfig expected)
        {
            Assert.That(actual.pageSize, Is.EqualTo(expected.pageSize));
            Assert.That(actual.maxPages, Is.EqualTo(expected.maxPages));
            Assert.That(actual.minRetainedPages, Is.EqualTo(expected.minRetainedPages));
            Assert.That(actual.maxEntries, Is.EqualTo(expected.maxEntries));
            Assert.That(actual.maxEntriesPerPage, Is.EqualTo(expected.maxEntriesPerPage));
            Assert.That(actual.maxKeyLength, Is.EqualTo(expected.maxKeyLength));
            Assert.That(actual.memoryBudgetBytes, Is.EqualTo(expected.memoryBudgetBytes));
            Assert.That(actual.padding, Is.EqualTo(expected.padding));
            Assert.That(actual.enableBleed, Is.EqualTo(expected.enableBleed));
            Assert.That(actual.filterMode, Is.EqualTo(expected.filterMode));
            Assert.That(actual.retentionPolicy, Is.EqualTo(expected.retentionPolicy));
            Assert.That(actual.oversizePolicy, Is.EqualTo(expected.oversizePolicy));
            Assert.That(actual.copyFallback, Is.EqualTo(expected.copyFallback));
            Assert.That(actual.defaultPixelsPerUnit, Is.EqualTo(expected.defaultPixelsPerUnit));
        }
    }
}
