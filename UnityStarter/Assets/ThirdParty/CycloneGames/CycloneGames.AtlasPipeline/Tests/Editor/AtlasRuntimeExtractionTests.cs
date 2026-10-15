using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;

namespace CycloneGames.AtlasPipeline.Tests
{
    /// <summary>
    /// Guards the Runtime Extraction preset: a rule with <c>runtimeExtraction</c> on must resolve
    /// to rotation off, tight packing off and RGBA32 on every platform — whatever the global
    /// toggles and the rule's own format fields say — while a rule without the preset resolves
    /// exactly as before. The decision lives in one place
    /// (<see cref="AtlasPipeline.ResolveEffectivePacking"/> /
    /// <see cref="AtlasPipeline.GetEffectiveFormat"/>), and these tests pin the truth table so
    /// the generator, the build gate and the inspector cannot drift apart.
    /// </summary>
    [TestFixture]
    public sealed class AtlasRuntimeExtractionTests
    {
        private readonly List<AtlasPipelineSettings> _settingsToDestroy =
            new List<AtlasPipelineSettings>();

        [TearDown]
        public void DestroySettings()
        {
            for (int i = 0; i < _settingsToDestroy.Count; i++)
            {
                if (_settingsToDestroy[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_settingsToDestroy[i]);
                }
            }

            _settingsToDestroy.Clear();
        }

        private AtlasPipelineSettings CreateSettings(
            bool enableRotation,
            bool enableTightPacking,
            bool atlasReadable = false)
        {
            var settings = ScriptableObject.CreateInstance<AtlasPipelineSettings>();
            var serialized = new SerializedObject(settings);
            serialized.FindProperty("enableRotation").boolValue = enableRotation;
            serialized.FindProperty("enableTightPacking").boolValue = enableTightPacking;
            serialized.FindProperty("atlasReadable").boolValue = atlasReadable;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            _settingsToDestroy.Add(settings);
            return settings;
        }

        private static AtlasImportRule CreateRule(
            bool runtimeExtraction,
            bool pixelArt = false,
            AtlasRotationMode rotationMode = AtlasRotationMode.Inherit,
            AtlasTextureFormat androidFormat = AtlasTextureFormat.Astc6x6,
            AtlasTextureFormat iphoneFormat = AtlasTextureFormat.Astc6x6,
            AtlasTextureFormat webglFormat = AtlasTextureFormat.Astc6x6,
            AtlasTextureFormat standaloneFormat = AtlasTextureFormat.Bc7)
        {
            return AtlasImportRule.Create(
                "ExtractionRule",
                "Assets/UI",
                androidFormat,
                iphoneFormat,
                AtlasGranularity.PerSourceFolder,
                "ExtractionGroup",
                webglFormat: webglFormat,
                standaloneFormat: standaloneFormat,
                pixelArt: pixelArt,
                atlasRotationMode: rotationMode,
                runtimeExtraction: runtimeExtraction);
        }

        // ----------------------------------------------------------------
        // The preset field itself
        // ----------------------------------------------------------------

        [Test]
        public void RuntimeExtraction_DefaultsToFalse()
        {
            // Rules serialized before the field existed deserialize to false; the factory default
            // must match or old assets would silently change behaviour on upgrade.
            Assert.IsFalse(CreateRule(runtimeExtraction: false).RuntimeExtraction);
            Assert.IsTrue(CreateRule(runtimeExtraction: true).RuntimeExtraction);
        }

        // ----------------------------------------------------------------
        // ResolveEffectivePacking truth table: global x rule x preset
        // ----------------------------------------------------------------

        [TestCase(false, false, AtlasRotationMode.Inherit, true, true, true, true,
            Description = "No preset, Inherit: global values pass through")]
        [TestCase(false, false, AtlasRotationMode.Inherit, false, false, false, false)]
        [TestCase(false, false, AtlasRotationMode.Enabled, false, true, true, true,
            Description = "Rule override beats the global default")]
        [TestCase(false, false, AtlasRotationMode.Disabled, true, true, false, true)]
        [TestCase(false, true, AtlasRotationMode.Enabled, true, true, false, false,
            Description = "Pixel art hard-blocks both, even with rotation forced on")]
        [TestCase(true, false, AtlasRotationMode.Enabled, true, true, false, false,
            Description = "Preset hard-blocks both, even with rotation forced on")]
        [TestCase(true, false, AtlasRotationMode.Inherit, false, false, false, false)]
        [TestCase(true, true, AtlasRotationMode.Enabled, true, true, false, false,
            Description = "Preset and pixel art compose")]
        public void ResolveEffectivePacking_TruthTable(
            bool runtimeExtraction,
            bool pixelArt,
            AtlasRotationMode rotationMode,
            bool globalRotation,
            bool globalTight,
            bool expectedRotation,
            bool expectedTight)
        {
            AtlasPipelineSettings settings = CreateSettings(globalRotation, globalTight);
            AtlasImportRule rule = CreateRule(runtimeExtraction, pixelArt, rotationMode);

            AtlasPipeline.ResolveEffectivePacking(
                settings,
                rule,
                out bool enableRotation,
                out bool enableTightPacking);

            Assert.AreEqual(expectedRotation, enableRotation, "rotation resolved wrong");
            Assert.AreEqual(expectedTight, enableTightPacking, "tight packing resolved wrong");
        }

        [Test]
        public void ResolveEffectivePacking_NullRule_PassesGlobalThrough()
        {
            AtlasPipelineSettings settings = CreateSettings(true, false);

            AtlasPipeline.ResolveEffectivePacking(
                settings,
                null,
                out bool enableRotation,
                out bool enableTightPacking);

            Assert.IsTrue(enableRotation);
            Assert.IsFalse(enableTightPacking);
        }

        // ----------------------------------------------------------------
        // Effective format: the preset forces RGBA32 on every platform
        // ----------------------------------------------------------------

        [Test]
        public void GetEffectiveFormat_PresetForcesRgba32OnEveryPlatform()
        {
            // Compressed rule formats on every platform: the preset must override them all —
            // this is the configuration the DynamicAtlas conflict came from.
            AtlasImportRule rule = CreateRule(
                runtimeExtraction: true,
                androidFormat: AtlasTextureFormat.Astc8x8,
                iphoneFormat: AtlasTextureFormat.PvrtcRgba4,
                webglFormat: AtlasTextureFormat.Dxt5,
                standaloneFormat: AtlasTextureFormat.Bc7);

            Assert.AreEqual(
                AtlasTextureFormat.Rgba32,
                AtlasPipeline.GetEffectiveFormat(rule, AtlasPlatform.Android));
            Assert.AreEqual(
                AtlasTextureFormat.Rgba32,
                AtlasPipeline.GetEffectiveFormat(rule, AtlasPlatform.Iphone));
            Assert.AreEqual(
                AtlasTextureFormat.Rgba32,
                AtlasPipeline.GetEffectiveFormat(rule, AtlasPlatform.Webgl));
            Assert.AreEqual(
                AtlasTextureFormat.Rgba32,
                AtlasPipeline.GetEffectiveFormat(rule, AtlasPlatform.Standalone));
        }

        [Test]
        public void GetEffectiveFormat_WithoutPreset_KeepsRuleFormats()
        {
            AtlasImportRule rule = CreateRule(
                runtimeExtraction: false,
                androidFormat: AtlasTextureFormat.Astc8x8,
                iphoneFormat: AtlasTextureFormat.Astc4x4,
                webglFormat: AtlasTextureFormat.Dxt5,
                standaloneFormat: AtlasTextureFormat.Bc7);

            Assert.AreEqual(
                AtlasTextureFormat.Astc8x8,
                AtlasPipeline.GetEffectiveFormat(rule, AtlasPlatform.Android));
            Assert.AreEqual(
                AtlasTextureFormat.Astc4x4,
                AtlasPipeline.GetEffectiveFormat(rule, AtlasPlatform.Iphone));
            Assert.AreEqual(
                AtlasTextureFormat.Dxt5,
                AtlasPipeline.GetEffectiveFormat(rule, AtlasPlatform.Webgl));
            Assert.AreEqual(
                AtlasTextureFormat.Bc7,
                AtlasPipeline.GetEffectiveFormat(rule, AtlasPlatform.Standalone));
        }

        [Test]
        public void GetEffectiveFormat_PixelArtStillForcesRgba32()
        {
            // Pre-existing behaviour the preset shares a branch with; pinned so the preset's
            // addition cannot regress it.
            AtlasImportRule rule = CreateRule(runtimeExtraction: false, pixelArt: true);

            Assert.AreEqual(
                AtlasTextureFormat.Rgba32,
                AtlasPipeline.GetEffectiveFormat(rule, AtlasPlatform.Android));
        }

        // ----------------------------------------------------------------
        // Global atlas readability
        // ----------------------------------------------------------------

        [Test]
        public void CreateTextureSettings_ReadableDefaultsToFalse()
        {
            var settings = ScriptableObject.CreateInstance<AtlasPipelineSettings>();
            _settingsToDestroy.Add(settings);

            Assert.IsFalse(
                settings.AtlasReadable,
                "settings assets written before atlasReadable existed must stay unreadable");

            SpriteAtlasTextureSettings textureSettings =
                AtlasPipeline.CreateTextureSettings(settings, FilterMode.Bilinear);
            Assert.IsFalse(textureSettings.readable);
        }

        [Test]
        public void CreateTextureSettings_ReadableFollowsGlobalSetting()
        {
            AtlasPipelineSettings settings =
                CreateSettings(true, true, atlasReadable: true);

            SpriteAtlasTextureSettings textureSettings =
                AtlasPipeline.CreateTextureSettings(settings, FilterMode.Bilinear);

            Assert.IsTrue(textureSettings.readable);
        }

        [Test]
        public void CreateTextureSettings_KeepsSrgbOn()
        {
            // The preset overrides the pixel format, not the colour space: sRGB is a sampling
            // flag and must stay on for the extractor's copy to match what rendering sampled.
            AtlasPipelineSettings settings = CreateSettings(true, true);

            SpriteAtlasTextureSettings textureSettings =
                AtlasPipeline.CreateTextureSettings(settings, FilterMode.Bilinear);

            Assert.IsTrue(textureSettings.sRGB);
        }

        // ----------------------------------------------------------------
        // IsRuntimeExtractionEffective: the inspector hint's question
        // ----------------------------------------------------------------

        [Test]
        public void IsRuntimeExtractionEffective_PresetRule_IsAlwaysEffective()
        {
            AtlasPipelineSettings settings = CreateSettings(true, true);

            Assert.IsTrue(AtlasPipeline.IsRuntimeExtractionEffective(
                settings,
                CreateRule(runtimeExtraction: true)));
        }

        [Test]
        public void IsRuntimeExtractionEffective_CompliantManualConfiguration()
        {
            // Rotation off by rule, tight off globally, RGBA32 everywhere by hand: extractable
            // without the preset, so the inspector must not nag.
            AtlasPipelineSettings settings = CreateSettings(false, false);
            AtlasImportRule rule = CreateRule(
                runtimeExtraction: false,
                rotationMode: AtlasRotationMode.Disabled,
                androidFormat: AtlasTextureFormat.Rgba32,
                iphoneFormat: AtlasTextureFormat.Rgba32,
                webglFormat: AtlasTextureFormat.Rgba32,
                standaloneFormat: AtlasTextureFormat.Rgba32);

            Assert.IsTrue(AtlasPipeline.IsRuntimeExtractionEffective(settings, rule));
        }

        [Test]
        public void IsRuntimeExtractionEffective_CompressedFormat_IsNotEffective()
        {
            AtlasPipelineSettings settings = CreateSettings(false, false);
            AtlasImportRule rule = CreateRule(
                runtimeExtraction: false,
                rotationMode: AtlasRotationMode.Disabled);

            Assert.IsFalse(AtlasPipeline.IsRuntimeExtractionEffective(settings, rule));
        }

        [Test]
        public void IsRuntimeExtractionEffective_RotationOn_IsNotEffective()
        {
            AtlasPipelineSettings settings = CreateSettings(true, false);
            AtlasImportRule rule = CreateRule(
                runtimeExtraction: false,
                androidFormat: AtlasTextureFormat.Rgba32,
                iphoneFormat: AtlasTextureFormat.Rgba32,
                webglFormat: AtlasTextureFormat.Rgba32,
                standaloneFormat: AtlasTextureFormat.Rgba32);

            Assert.IsFalse(AtlasPipeline.IsRuntimeExtractionEffective(settings, rule));
        }
    }
}

#if UNITY_EDITOR
// Unity EditMode integration tests for the build gate. These drive the REAL asset database —
// a generated .spriteatlasv2 on disk whose importer is then read back — so they only compile
// and run inside Unity's Test Runner. Guarded the same way as AtlasRuleLifecycleTests.
namespace CycloneGames.AtlasPipeline.Tests
{
    /// <summary>
    /// The Runtime Extraction build gate (<see cref="AtlasPipeline.VerifyRuntimeExtractionAtlas"/>)
    /// against real atlas assets: a preset-marked rule whose on-disk atlas carries tampered
    /// packing or platform settings must be reported, a compliant one must pass — including when
    /// the global toggles say something the preset overrides.
    /// </summary>
    [TestFixture]
    public sealed class AtlasRuntimeExtractionBuildGateTests
    {
        private const string TempFolder = "Assets/TempRuntimeExtractionGate";
        private const string AtlasPath = TempFolder + "/gate.spriteatlasv2";
        private const string SourceImagePath = TempFolder + "/dot.png";

        private readonly List<AtlasPipelineSettings> _settingsToDestroy =
            new List<AtlasPipelineSettings>();

        [SetUp]
        public void CreateAtlasAsset()
        {
            if (!AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.CreateFolder("Assets", "TempRuntimeExtractionGate");
            }

            var texture = new Texture2D(4, 4);
            byte[] png = texture.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(texture);
            string projectRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
            System.IO.File.WriteAllBytes(
                System.IO.Path.Combine(projectRoot, SourceImagePath),
                png);
            AssetDatabase.ImportAsset(SourceImagePath);
            var sourceImporter = AssetImporter.GetAtPath(SourceImagePath) as TextureImporter;
            sourceImporter.textureType = TextureImporterType.Sprite;
            sourceImporter.SaveAndReimport();

            // A non-empty atlas: the source gives the importer a real page to configure, which
            // keeps the asset close to what the generator writes. The main asset is loaded rather
            // than the Sprite sub-asset so the packable exists regardless of sprite-mode timing.
            var atlasAsset = new SpriteAtlasAsset();
            var source = AssetDatabase.LoadAssetAtPath<Texture2D>(SourceImagePath);
            Assert.IsNotNull(source, "the source image must import before the atlas is written");
            atlasAsset.Add(new UnityEngine.Object[] { source });
            SpriteAtlasAsset.Save(atlasAsset, AtlasPath);
            UnityEngine.Object.DestroyImmediate(atlasAsset);
            AssetDatabase.ImportAsset(AtlasPath, ImportAssetOptions.ForceSynchronousImport);
        }

        [TearDown]
        public void Cleanup()
        {
            for (int i = 0; i < _settingsToDestroy.Count; i++)
            {
                if (_settingsToDestroy[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_settingsToDestroy[i]);
                }
            }

            _settingsToDestroy.Clear();

            if (AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.DeleteAsset(TempFolder);
            }

            AssetDatabase.Refresh();
        }

        private AtlasPipelineSettings CreateSettings(bool enableRotation, bool enableTightPacking)
        {
            var settings = ScriptableObject.CreateInstance<AtlasPipelineSettings>();
            var serialized = new SerializedObject(settings);
            serialized.FindProperty("enableRotation").boolValue = enableRotation;
            serialized.FindProperty("enableTightPacking").boolValue = enableTightPacking;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            _settingsToDestroy.Add(settings);
            return settings;
        }

        private static AtlasImportRule CreatePresetRule()
        {
            return AtlasImportRule.Create(
                "GateRule",
                "Assets/UI",
                AtlasTextureFormat.Astc6x6,
                AtlasTextureFormat.Astc6x6,
                AtlasGranularity.PerSourceFolder,
                "GateGroup",
                runtimeExtraction: true);
        }

        /// <summary>
        /// Writes importer settings the way the generator would (packing + one overridden
        /// platform entry per target), so the gate reads realistic on-disk state.
        /// </summary>
        private static void WriteImporterConfiguration(
            bool rotation,
            bool tightPacking,
            AtlasTextureFormat format)
        {
            var importer = AssetImporter.GetAtPath(AtlasPath) as SpriteAtlasImporter;
            Assert.IsNotNull(importer, "the test atlas must have a SpriteAtlasImporter");

            SpriteAtlasPackingSettings packing = importer.packingSettings;
            packing.enableRotation = rotation;
            packing.enableTightPacking = tightPacking;
            importer.packingSettings = packing;

            AtlasPlatform[] platforms =
            {
                AtlasPlatform.Android,
                AtlasPlatform.Iphone,
                AtlasPlatform.Webgl,
                AtlasPlatform.Standalone,
            };
            for (int i = 0; i < platforms.Length; i++)
            {
                importer.SetPlatformSettings(new TextureImporterPlatformSettings
                {
                    name = AtlasPlatformFormats.GetPlatformName(platforms[i]),
                    overridden = true,
                    format = AtlasPlatformFormats.ToTextureImporterFormat(format),
                    textureCompression =
                        AtlasPlatformFormats.ToTextureImporterCompression(format),
                    maxTextureSize = 2048,
                });
            }

            AssetDatabase.WriteImportSettingsIfDirty(AtlasPath);
        }

        [Test]
        public void CompliantAtlas_PassesTheGate()
        {
            WriteImporterConfiguration(false, false, AtlasTextureFormat.Rgba32);

            var failures = new List<string>();
            AtlasPipeline.VerifyRuntimeExtractionAtlas(
                AtlasPath,
                CreateSettings(true, true),
                CreatePresetRule(),
                failures);

            Assert.AreEqual(
                0,
                failures.Count,
                "compliant atlas reported: " + string.Join(" | ", failures));
        }

        [Test]
        public void TamperedPacking_IsReported()
        {
            // Rotation and tight packing flipped on after generation — the hand edit the gate
            // exists to catch. Formats stay compliant so exactly one violation is expected.
            WriteImporterConfiguration(true, true, AtlasTextureFormat.Rgba32);

            var failures = new List<string>();
            AtlasPipeline.VerifyRuntimeExtractionAtlas(
                AtlasPath,
                CreateSettings(true, true),
                CreatePresetRule(),
                failures);

            Assert.AreEqual(1, failures.Count, "packing tamper must produce one violation");
            StringAssert.Contains("Runtime Extraction", failures[0]);
        }

        [Test]
        public void TamperedFormat_IsReported()
        {
            // A compressed platform format on disk: the original DynamicAtlas failure mode.
            WriteImporterConfiguration(false, false, AtlasTextureFormat.Astc6x6);

            var failures = new List<string>();
            AtlasPipeline.VerifyRuntimeExtractionAtlas(
                AtlasPath,
                CreateSettings(true, true),
                CreatePresetRule(),
                failures);

            Assert.Greater(failures.Count, 0, "a compressed platform format must be reported");
        }

        [Test]
        public void CompliantAtlas_StaysCompliantWhenGlobalsFlip()
        {
            // The atlas is written while the globals sit at one value, then verified with the
            // globals flipped — the tamper CI actually hits when two machines disagree on the
            // settings asset. The preset overrides the globals, so the contract must not move.
            WriteImporterConfiguration(false, false, AtlasTextureFormat.Rgba32);

            var failures = new List<string>();
            AtlasPipeline.VerifyRuntimeExtractionAtlas(
                AtlasPath,
                CreateSettings(false, false),
                CreatePresetRule(),
                failures);

            Assert.AreEqual(
                0,
                failures.Count,
                "global toggles must not affect the preset contract: "
                + string.Join(" | ", failures));
        }
    }
}
#endif
