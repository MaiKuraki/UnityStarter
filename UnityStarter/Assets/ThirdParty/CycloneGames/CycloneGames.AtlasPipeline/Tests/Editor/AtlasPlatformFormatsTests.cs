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
            Assert.AreEqual(5, AtlasPlatformFormats.GetSupportedFormats(
                AtlasPlatform.Android).Count);
            Assert.AreEqual(5, AtlasPlatformFormats.GetSupportedFormats(
                AtlasPlatform.Iphone).Count);
            Assert.AreEqual(7, AtlasPlatformFormats.GetSupportedFormats(
                AtlasPlatform.Webgl).Count);
            Assert.AreEqual(3, AtlasPlatformFormats.GetSupportedFormats(
                AtlasPlatform.Standalone).Count);
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
    }
}
