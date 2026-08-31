using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;

namespace CycloneGames.AtlasPipeline.Tests
{
    /// <summary>
    /// Tests for AtlasPlatformFormats mapping and validation. This is the single source of truth for
    /// "what the UI can select, what the build accepts, and what finally gets written" — shared by
    /// three consumers, so changing it must keep all three consistent, which these tests pin down.
    /// </summary>
    [TestFixture]
    public sealed class AtlasPlatformFormatsTests
    {
        // ----------------------------------------------------------------
        // Supported-format table
        // ----------------------------------------------------------------

        [Test]
        public void GetSupportedFormats_PerPlatformCounts()
        {
            Assert.AreEqual(7, AtlasPlatformFormats.GetSupportedFormats(
                AtlasPlatform.Android).Count);
            Assert.AreEqual(7, AtlasPlatformFormats.GetSupportedFormats(
                AtlasPlatform.Iphone).Count);
            Assert.AreEqual(7, AtlasPlatformFormats.GetSupportedFormats(
                AtlasPlatform.Webgl).Count);
            Assert.AreEqual(3, AtlasPlatformFormats.GetSupportedFormats(
                AtlasPlatform.Standalone).Count);
        }

        /// <summary>
        /// ASTC needs OpenGL ES 3.1 / Vulkan on Android and an A8 GPU or later on iOS. Devices below
        /// those lines fall back to ETC2 and PVRTC respectively, and before those were added RGBA32
        /// was the only non-ASTC option on mobile — 16 MB of VRAM per 2048px atlas against 0.5 MB.
        /// </summary>
        [Test]
        public void MobilePlatforms_ExposeTheirLegacyFallbacks()
        {
            IReadOnlyList<AtlasTextureFormat> android =
                AtlasPlatformFormats.GetSupportedFormats(AtlasPlatform.Android);
            IReadOnlyList<AtlasTextureFormat> iphone =
                AtlasPlatformFormats.GetSupportedFormats(AtlasPlatform.Iphone);

            CollectionAssert.Contains(android, AtlasTextureFormat.Etc2Rgba8);
            CollectionAssert.Contains(android, AtlasTextureFormat.Etc2Rgb4);
            CollectionAssert.Contains(iphone, AtlasTextureFormat.PvrtcRgba4);
            CollectionAssert.Contains(iphone, AtlasTextureFormat.PvrtcRgb4);
        }

        [Test]
        public void LegacyFallbacks_DoNotLeakOntoTheWrongPlatform()
        {
            IReadOnlyList<AtlasTextureFormat> android =
                AtlasPlatformFormats.GetSupportedFormats(AtlasPlatform.Android);
            IReadOnlyList<AtlasTextureFormat> iphone =
                AtlasPlatformFormats.GetSupportedFormats(AtlasPlatform.Iphone);

            CollectionAssert.DoesNotContain(android, AtlasTextureFormat.PvrtcRgba4);
            CollectionAssert.DoesNotContain(android, AtlasTextureFormat.PvrtcRgb4);
            CollectionAssert.DoesNotContain(iphone, AtlasTextureFormat.Etc2Rgba8);
            CollectionAssert.DoesNotContain(iphone, AtlasTextureFormat.Etc2Rgb4);
        }

        [Test]
        public void ToTextureImporterFormat_MapsEverySupportedFormatOffRgba32()
        {
            var platforms = new[]
            {
                AtlasPlatform.Android,
                AtlasPlatform.Iphone,
                AtlasPlatform.Webgl,
                AtlasPlatform.Standalone,
            };

            for (int p = 0; p < platforms.Length; p++)
            {
                IReadOnlyList<AtlasTextureFormat> supported =
                    AtlasPlatformFormats.GetSupportedFormats(platforms[p]);
                for (int i = 0; i < supported.Count; i++)
                {
                    AtlasTextureFormat format = supported[i];
                    if (format == AtlasTextureFormat.Rgba32)
                    {
                        continue;
                    }

                    Assert.AreNotEqual(
                        TextureImporterFormat.RGBA32,
                        AtlasPlatformFormats.ToTextureImporterFormat(format),
                        "every non-RGBA32 format must map to a real compressed format");
                }
            }
        }

        [Test]
        public void ToTextureImporterCompression_OnlyRgba32IsUncompressed()
        {
            Assert.AreEqual(
                TextureImporterCompression.Uncompressed,
                AtlasPlatformFormats.ToTextureImporterCompression(AtlasTextureFormat.Rgba32));
            Assert.AreEqual(
                TextureImporterCompression.Compressed,
                AtlasPlatformFormats.ToTextureImporterCompression(AtlasTextureFormat.Etc2Rgba8));
            Assert.AreEqual(
                TextureImporterCompression.Compressed,
                AtlasPlatformFormats.ToTextureImporterCompression(AtlasTextureFormat.PvrtcRgba4));
        }

        /// <summary>
        /// The ordering is the whole point of the fallback chain: an uncompressed atlas must be the
        /// most expensive option, and the legacy compressed formats must beat it by a wide margin.
        /// </summary>
        [Test]
        public void GetBytesPerPixel_Rgba32IsByFarTheMostExpensive()
        {
            double rgba32 = AtlasPlatformFormats.GetBytesPerPixel(AtlasTextureFormat.Rgba32);
            double etc2 = AtlasPlatformFormats.GetBytesPerPixel(AtlasTextureFormat.Etc2Rgba8);
            double pvrtc = AtlasPlatformFormats.GetBytesPerPixel(AtlasTextureFormat.PvrtcRgba4);
            double astc6 = AtlasPlatformFormats.GetBytesPerPixel(AtlasTextureFormat.Astc6x6);

            Assert.AreEqual(4d, rgba32);
            Assert.AreEqual(1d, etc2);
            Assert.AreEqual(0.5d, pvrtc);
            Assert.Less(astc6, etc2);
            Assert.Greater(rgba32 / etc2, 3d);
        }

        [Test]
        public void EstimateAtlasBytes_MatchesAHandCalculation()
        {
            // 2048 * 2048 * 4 bytes = 16 MB uncompressed.
            Assert.AreEqual(
                16L * 1024L * 1024L,
                AtlasPlatformFormats.EstimateAtlasBytes(2048, AtlasTextureFormat.Rgba32));

            // 2048 * 2048 * 0.5 bytes = 2 MB.
            Assert.AreEqual(
                2L * 1024L * 1024L,
                AtlasPlatformFormats.EstimateAtlasBytes(2048, AtlasTextureFormat.Etc2Rgb4));

            Assert.AreEqual(0L, AtlasPlatformFormats.EstimateAtlasBytes(0, AtlasTextureFormat.Rgba32));
            Assert.AreEqual(
                0L,
                AtlasPlatformFormats.EstimateAtlasBytes(-1, AtlasTextureFormat.Rgba32));
        }

        [Test]
        public void IsPowerOfTwo_RejectsZeroAndNonPowers()
        {
            Assert.IsTrue(AtlasPlatformFormats.IsPowerOfTwo(1));
            Assert.IsTrue(AtlasPlatformFormats.IsPowerOfTwo(512));
            Assert.IsTrue(AtlasPlatformFormats.IsPowerOfTwo(2048));
            Assert.IsFalse(AtlasPlatformFormats.IsPowerOfTwo(0));
            Assert.IsFalse(AtlasPlatformFormats.IsPowerOfTwo(-1024));
            Assert.IsFalse(AtlasPlatformFormats.IsPowerOfTwo(3000));
        }

        [Test]
        public void RequiresPowerOfTwo_ExemptsUncompressed()
        {
            Assert.IsTrue(AtlasPlatformFormats.RequiresPowerOfTwo(AtlasTextureFormat.Astc6x6));
            Assert.IsTrue(AtlasPlatformFormats.RequiresPowerOfTwo(AtlasTextureFormat.Etc2Rgba8));
            Assert.IsFalse(AtlasPlatformFormats.RequiresPowerOfTwo(AtlasTextureFormat.Rgba32));
        }

        [Test]
        public void GetSupportedFormats_AndroidHasNoDesktopFormats()
        {
            IReadOnlyList<AtlasTextureFormat> android =
                AtlasPlatformFormats.GetSupportedFormats(AtlasPlatform.Android);

            CollectionAssert.DoesNotContain(android, AtlasTextureFormat.Bc7);
            CollectionAssert.DoesNotContain(android, AtlasTextureFormat.Dxt1);
            CollectionAssert.DoesNotContain(android, AtlasTextureFormat.Dxt5);
        }

        [Test]
        public void GetSafeFormat_UnsupportedFallsBackToDefault()
        {
            Assert.AreEqual(
                AtlasTextureFormat.Astc6x6,
                AtlasPlatformFormats.GetSafeFormat(
                    AtlasPlatform.Android,
                    AtlasTextureFormat.Bc7));
            Assert.AreEqual(
                AtlasTextureFormat.Bc7,
                AtlasPlatformFormats.GetSafeFormat(
                    AtlasPlatform.Standalone,
                    AtlasTextureFormat.Astc6x6));
            Assert.AreEqual(
                AtlasTextureFormat.Astc6x6,
                AtlasPlatformFormats.GetSafeFormat(
                    AtlasPlatform.Webgl,
                    AtlasTextureFormat.Bc7));
        }

        // ----------------------------------------------------------------
        // Enum mapping (all 8 formats, one by one)
        // ----------------------------------------------------------------

        [TestCase(AtlasTextureFormat.Astc4x4, ExpectedResult = TextureImporterFormat.ASTC_4x4)]
        [TestCase(AtlasTextureFormat.Astc5x5, ExpectedResult = TextureImporterFormat.ASTC_5x5)]
        [TestCase(AtlasTextureFormat.Astc6x6, ExpectedResult = TextureImporterFormat.ASTC_6x6)]
        [TestCase(AtlasTextureFormat.Astc8x8, ExpectedResult = TextureImporterFormat.ASTC_8x8)]
        [TestCase(AtlasTextureFormat.Rgba32, ExpectedResult = TextureImporterFormat.RGBA32)]
        [TestCase(AtlasTextureFormat.Dxt1, ExpectedResult = TextureImporterFormat.DXT1)]
        [TestCase(AtlasTextureFormat.Dxt5, ExpectedResult = TextureImporterFormat.DXT5)]
        [TestCase(AtlasTextureFormat.Bc7, ExpectedResult = TextureImporterFormat.BC7)]
        public TextureImporterFormat ToTextureImporterFormat_MapsAllValues(
            AtlasTextureFormat format)
        {
            return AtlasPlatformFormats.ToTextureImporterFormat(format);
        }

        [TestCase(AtlasTextureFormat.Rgba32, ExpectedResult = TextureImporterCompression.Uncompressed)]
        [TestCase(AtlasTextureFormat.Astc4x4, ExpectedResult = TextureImporterCompression.Compressed)]
        [TestCase(AtlasTextureFormat.Astc6x6, ExpectedResult = TextureImporterCompression.Compressed)]
        [TestCase(AtlasTextureFormat.Bc7, ExpectedResult = TextureImporterCompression.Compressed)]
        [TestCase(AtlasTextureFormat.Dxt1, ExpectedResult = TextureImporterCompression.Compressed)]
        public TextureImporterCompression ToTextureImporterCompression_MapsAllValues(
            AtlasTextureFormat format)
        {
            return AtlasPlatformFormats.ToTextureImporterCompression(format);
        }

        // ----------------------------------------------------------------
        // Query and validation
        // ----------------------------------------------------------------

        [Test]
        public void GetSupportedFormatIndex_ReturnsMinusOneForUnsupported()
        {
            Assert.AreEqual(
                -1,
                AtlasPlatformFormats.GetSupportedFormatIndex(
                    AtlasPlatform.Android,
                    AtlasTextureFormat.Bc7));
            Assert.AreEqual(
                0,
                AtlasPlatformFormats.GetSupportedFormatIndex(
                    AtlasPlatform.Android,
                    AtlasTextureFormat.Astc4x4));
        }

        [TestCase(-1, ExpectedResult = false)]
        [TestCase(0, ExpectedResult = true)]
        [TestCase(50, ExpectedResult = true)]
        [TestCase(100, ExpectedResult = true)]
        [TestCase(101, ExpectedResult = false)]
        public bool IsCompressionQualityValid_Boundaries(int quality)
        {
            return AtlasPlatformFormats.IsCompressionQualityValid(quality);
        }

        [Test]
        public void TryGetPlatformByName_CaseInsensitiveAndUnknown()
        {
            Assert.IsTrue(AtlasPlatformFormats.TryGetPlatformByName(
                "android",
                out AtlasPlatform platform));
            Assert.AreEqual(AtlasPlatform.Android, platform);

            Assert.IsTrue(AtlasPlatformFormats.TryGetPlatformByName(
                "WEBGL",
                out platform));
            Assert.AreEqual(AtlasPlatform.Webgl, platform);

            Assert.IsFalse(AtlasPlatformFormats.TryGetPlatformByName(
                "Switch",
                out platform));
        }

        [Test]
        public void ValidateRule_NullRuleDoesNotThrow()
        {
            var errors = new List<string>();
            Assert.DoesNotThrow(() =>
                AtlasPlatformFormats.ValidateRule(null, errors));
            Assert.AreEqual(0, errors.Count);
        }

        [Test]
        public void ValidateRule_UnsupportedFormatAppendsError()
        {
            AtlasImportRule rule = AtlasImportRule.Create(
                "BadRule",
                "Assets/UI",
                AtlasTextureFormat.Bc7,
                AtlasTextureFormat.Astc6x6,
                AtlasGranularity.PerSourceFolder,
                "G");

            var errors = new List<string>();
            AtlasPlatformFormats.ValidateRule(rule, errors);

            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains("BadRule", errors[0]);
            StringAssert.Contains("Android", errors[0]);
        }

        /// <summary>
        /// The recommended max is the one size a user can type freely, and it is copied into every
        /// source importer's Max Size. A value like 1000 makes the imported source itself
        /// non-power-of-two, which PVRTC cannot compress and WebGL 1 will not mip — silently.
        /// </summary>
        [TestCase(0, ExpectedResult = true)]
        [TestCase(-1, ExpectedResult = true)]
        [TestCase(1000, ExpectedResult = true)]
        [TestCase(1500, ExpectedResult = true)]
        [TestCase(3000, ExpectedResult = true)]
        [TestCase(256, ExpectedResult = false)]
        [TestCase(512, ExpectedResult = false)]
        [TestCase(1024, ExpectedResult = false)]
        [TestCase(2048, ExpectedResult = false)]
        [TestCase(4096, ExpectedResult = false)]
        public bool ValidateRule_FlagsNonPowerOfTwoRecommendedMax(int recommendedMax)
        {
            AtlasImportRule rule = AtlasImportRule.Create(
                "Rule",
                "Assets/UI",
                AtlasTextureFormat.Astc6x6,
                AtlasTextureFormat.Astc6x6,
                AtlasGranularity.PerSourceFolder,
                "G",
                webglFormat: AtlasTextureFormat.Astc6x6,
                recommendedMaxTextureSize: recommendedMax);

            var errors = new List<string>();
            AtlasPlatformFormats.ValidateRule(rule, errors);

            for (int i = 0; i < errors.Count; i++)
            {
                if (errors[i].Contains("recommended max texture size"))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Powers of two are what the option list offers, and 4096 is part of it: a large project
        /// may prefer fewer, larger atlas files. The cost is reported as a warning, never blocked.
        /// </summary>
        [Test]
        public void ValidateRule_LargeMobileAtlasWarnsButDoesNotFail()
        {
            AtlasImportRule rule = AtlasImportRule.Create(
                "BigRule",
                "Assets/UI",
                AtlasTextureFormat.Astc6x6,
                AtlasTextureFormat.Astc6x6,
                AtlasGranularity.PerSourceFolder,
                "G",
                webglFormat: AtlasTextureFormat.Astc6x6,
                atlasMaxTextureSize: 4096,
                recommendedMaxTextureSize: 1024);

            var errors = new List<string>();
            var warnings = new List<string>();
            AtlasPlatformFormats.ValidateRule(rule, errors, warnings);

            Assert.IsEmpty(errors, "a large atlas is a choice, not an error");
            Assert.AreEqual(
                2,
                warnings.Count,
                "one warning per mobile platform (Android and iOS)");
            StringAssert.Contains("4096", warnings[0]);
            StringAssert.Contains("2048", warnings[0], "and names the cheaper alternative");
        }

        [Test]
        public void ValidateRule_ModerateMobileAtlasDoesNotWarn()
        {
            AtlasImportRule rule = AtlasImportRule.Create(
                "NormalRule",
                "Assets/UI",
                AtlasTextureFormat.Astc6x6,
                AtlasTextureFormat.Astc6x6,
                AtlasGranularity.PerSourceFolder,
                "G",
                webglFormat: AtlasTextureFormat.Astc6x6,
                atlasMaxTextureSize: 2048,
                recommendedMaxTextureSize: 1024);

            var errors = new List<string>();
            var warnings = new List<string>();
            AtlasPlatformFormats.ValidateRule(rule, errors, warnings);

            Assert.IsEmpty(errors);
            Assert.IsEmpty(warnings);
        }
    }
}
