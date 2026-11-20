using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.AtlasPipeline.Tests
{
    [TestFixture]
    public sealed class AtlasExtractionContractTests
    {
        private readonly List<AtlasPipelineSettings> _settingsToDestroy =
            new List<AtlasPipelineSettings>();

        private static readonly AtlasPlatform[] Platforms =
        {
            AtlasPlatform.Android,
            AtlasPlatform.Iphone,
            AtlasPlatform.Webgl,
            AtlasPlatform.Standalone,
        };

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _settingsToDestroy.Count; i++)
            {
                if (_settingsToDestroy[i] != null)
                {
                    Object.DestroyImmediate(_settingsToDestroy[i]);
                }
            }

            _settingsToDestroy.Clear();
        }

        private AtlasPipelineSettings CreateSettings(bool rotation, bool tight)
        {
            var settings = ScriptableObject.CreateInstance<AtlasPipelineSettings>();
            var serialized = new SerializedObject(settings);
            serialized.FindProperty("enableRotation").boolValue = rotation;
            serialized.FindProperty("enableTightPacking").boolValue = tight;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            _settingsToDestroy.Add(settings);
            return settings;
        }

        private static AtlasImportRule CreateRule(string formatMode)
        {
            AtlasTextureFormat rgba = AtlasTextureFormat.Rgba32;
            AtlasTextureFormat compressed = AtlasTextureFormat.Astc6x6;

            AtlasTextureFormat android = formatMode == "none" ? compressed : rgba;
            AtlasTextureFormat iphone = formatMode == "none" ? compressed : rgba;
            AtlasTextureFormat webgl = formatMode == "all" ? rgba : compressed;
            AtlasTextureFormat standalone = formatMode == "all"
                ? rgba
                : AtlasTextureFormat.Bc7;

            return AtlasImportRule.Create(
                "ContractRule",
                "Assets/UI",
                android,
                iphone,
                AtlasGranularity.PerSourceFolder,
                "ContractGroup",
                webglFormat: webgl,
                standaloneFormat: standalone,
                atlasRotationMode: AtlasRotationMode.Inherit);
        }

        [TestCase(false, false, "all", true)]
        [TestCase(false, false, "none", false)]
        [TestCase(false, false, "partial", false)]
        [TestCase(true, false, "all", false)]
        [TestCase(false, true, "all", false)]
        [TestCase(true, true, "all", false)]
        [TestCase(true, true, "none", false)]
        public void IsRuntimeExtractionEffective_MatchesThreeFieldContract(
            bool rotation,
            bool tight,
            string formatMode,
            bool expected)
        {
            AtlasPipelineSettings settings = CreateSettings(rotation, tight);
            AtlasImportRule rule = CreateRule(formatMode);

            Assert.That(
                AtlasPipeline.IsRuntimeExtractionEffective(settings, rule),
                Is.EqualTo(expected),
                "rotation=" + rotation + " tight=" + tight + " formats=" + formatMode);
        }

        [Test]
        public void ProducerEffectiveMatchesConsumerConsumableAcrossFullGrid()
        {
            foreach (bool rotation in new[] { false, true })
            {
                foreach (bool tight in new[] { false, true })
                {
                    foreach (string formatMode in new[] { "all", "none", "partial" })
                    {
                        AtlasPipelineSettings settings = CreateSettings(rotation, tight);
                        AtlasImportRule rule = CreateRule(formatMode);
                        bool producer = AtlasPipeline.IsRuntimeExtractionEffective(settings, rule);

                        bool allRgba32 = true;
                        for (int p = 0; p < Platforms.Length; p++)
                        {
                            if (AtlasPipeline.GetEffectiveFormat(rule, Platforms[p])
                                != AtlasTextureFormat.Rgba32)
                            {
                                allRgba32 = false;
                                break;
                            }
                        }

                        bool consumerConsumable = !rotation && !tight && allRgba32;
                        string context = string.Format(
                            "contract drift: rotation={0} tight={1} formats={2}",
                            rotation, tight, formatMode);
                        Assert.That(producer, Is.EqualTo(consumerConsumable), context);
                    }
                }
            }
        }

        [Test]
        public void PresetForcesRgba32OnEveryPlatformRegardlessOfRuleFormats()
        {
            AtlasImportRule preset = AtlasImportRule.Create(
                "PresetRule",
                "Assets/UI",
                AtlasTextureFormat.Astc8x8,
                AtlasTextureFormat.PvrtcRgba4,
                AtlasGranularity.PerSourceFolder,
                "PresetGroup",
                webglFormat: AtlasTextureFormat.Dxt5,
                standaloneFormat: AtlasTextureFormat.Bc7,
                runtimeExtraction: true);

            for (int p = 0; p < Platforms.Length; p++)
            {
                Assert.That(
                    AtlasPipeline.GetEffectiveFormat(preset, Platforms[p]),
                    Is.EqualTo(AtlasTextureFormat.Rgba32),
                    "platform " + Platforms[p]);
            }
        }
    }
}
