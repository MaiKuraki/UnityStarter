using System.Collections.Generic;
using NUnit.Framework;

namespace CycloneGames.AtlasPipeline.Tests
{
    /// <summary>
    /// Per-platform atlas texture size is the cheapest low-end lever available: capping Android at
    /// 1024px costs nothing in package size, unlike shipping a second resolution. The trade-off is
    /// that it is a quality lever, not a capacity lever — halving the atlas size makes the same
    /// content four times less likely to fit.
    /// </summary>
    [TestFixture]
    public sealed class AtlasPlatformAtlasSizeTests
    {
        private static AtlasImportRule CreateRule(
            int atlasMaxSize = 2048,
            int android = 0,
            int iphone = 0,
            int webgl = 0,
            int standalone = 0,
            AtlasTextureFormat androidFormat = AtlasTextureFormat.Astc6x6,
            AtlasTextureFormat iphoneFormat = AtlasTextureFormat.Astc6x6)
        {
            return AtlasImportRule.Create(
                "TestRule",
                "Assets/UI",
                androidFormat,
                iphoneFormat,
                AtlasGranularity.PerSourceFolder,
                "UI",
                atlasMaxTextureSize: atlasMaxSize,
                androidAtlasMaxSize: android,
                iphoneAtlasMaxSize: iphone,
                webglAtlasMaxSize: webgl,
                standaloneAtlasMaxSize: standalone);
        }

        [Test]
        public void ZeroOverride_InheritsTheSharedAtlasSize()
        {
            AtlasImportRule rule = CreateRule(atlasMaxSize: 1024);

            Assert.AreEqual(1024, rule.GetAtlasMaxTextureSize(AtlasPlatform.Android));
            Assert.AreEqual(1024, rule.GetAtlasMaxTextureSize(AtlasPlatform.Iphone));
            Assert.AreEqual(1024, rule.GetAtlasMaxTextureSize(AtlasPlatform.Webgl));
            Assert.AreEqual(1024, rule.GetAtlasMaxTextureSize(AtlasPlatform.Standalone));
        }

        /// <summary>
        /// This is what makes the feature migration-safe: a settings asset written before the
        /// per-platform fields existed deserializes them as zero and keeps behaving exactly as before.
        /// </summary>
        [Test]
        public void NegativeOverride_IsTreatedAsInherit()
        {
            AtlasImportRule rule = CreateRule(atlasMaxSize: 2048, android: -1);
            Assert.AreEqual(2048, rule.GetAtlasMaxTextureSize(AtlasPlatform.Android));
        }

        [Test]
        public void Overrides_AreIndependentPerPlatform()
        {
            AtlasImportRule rule = CreateRule(
                atlasMaxSize: 2048,
                android: 1024,
                iphone: 4096);

            Assert.AreEqual(1024, rule.GetAtlasMaxTextureSize(AtlasPlatform.Android));
            Assert.AreEqual(4096, rule.GetAtlasMaxTextureSize(AtlasPlatform.Iphone));
            Assert.AreEqual(2048, rule.GetAtlasMaxTextureSize(AtlasPlatform.Webgl));
            Assert.AreEqual(2048, rule.GetAtlasMaxTextureSize(AtlasPlatform.Standalone));
        }

        [Test]
        public void NonPowerOfTwoSize_IsRejectedForCompressedFormats()
        {
            AtlasImportRule rule = CreateRule(android: 1500);
            var errors = new List<string>();
            var warnings = new List<string>();

            AtlasPlatformFormats.ValidateRule(rule, errors, warnings);

            Assert.AreEqual(1, errors.Count);
            Assert.That(errors[0], Does.Contain("requires power-of-two"));
            Assert.IsEmpty(warnings);
        }

        [Test]
        public void NonPowerOfTwoSize_IsAllowedForUncompressed()
        {
            AtlasImportRule rule = CreateRule(
                android: 1500,
                androidFormat: AtlasTextureFormat.Rgba32);
            var errors = new List<string>();

            AtlasPlatformFormats.ValidateRule(rule, errors);

            Assert.IsEmpty(errors);
        }

        /// <summary>
        /// An uncompressed mobile atlas is expensive but legitimate — pixel art needs it — so it must
        /// land in warnings and never block the build.
        /// </summary>
        [Test]
        public void Rgba32OnMobile_IsAWarningNotAnError()
        {
            AtlasImportRule rule = CreateRule(androidFormat: AtlasTextureFormat.Rgba32);
            var errors = new List<string>();
            var warnings = new List<string>();

            AtlasPlatformFormats.ValidateRule(rule, errors, warnings);

            Assert.IsEmpty(errors, "a pixel-art rule must still be allowed to build");
            Assert.AreEqual(1, warnings.Count);
            Assert.That(warnings[0], Does.Contain("RGBA 32"));
            Assert.That(warnings[0], Does.Contain("ETC2 RGBA8"));
        }

        [Test]
        public void Rgba32OnIos_SuggestsPvrtcRatherThanEtc2()
        {
            AtlasImportRule rule = CreateRule(iphoneFormat: AtlasTextureFormat.Rgba32);
            var errors = new List<string>();
            var warnings = new List<string>();

            AtlasPlatformFormats.ValidateRule(rule, errors, warnings);

            Assert.IsEmpty(errors);
            Assert.AreEqual(1, warnings.Count);
            Assert.That(warnings[0], Does.Contain("PVRTC RGBA4"));
            Assert.That(warnings[0], Does.Not.Contain("ETC2 RGBA8"));
        }

        [Test]
        public void Rgba32OnStandalone_DoesNotWarn()
        {
            AtlasImportRule rule = AtlasImportRule.Create(
                "TestRule",
                "Assets/UI",
                AtlasTextureFormat.Astc6x6,
                AtlasTextureFormat.Astc6x6,
                AtlasGranularity.PerSourceFolder,
                "UI",
                standaloneFormat: AtlasTextureFormat.Rgba32);

            var errors = new List<string>();
            var warnings = new List<string>();

            AtlasPlatformFormats.ValidateRule(rule, errors, warnings);

            Assert.IsEmpty(errors);
            Assert.IsEmpty(warnings, "desktop memory budgets are not the mobile ones");
        }

        /// <summary>
        /// A null warnings collector is legal — the build steps always pass one, but the window and
        /// direct callers may not, and dropping an advisory is correct there.
        /// </summary>
        [Test]
        public void Validation_ToleratesANullWarningsCollector()
        {
            AtlasImportRule rule = CreateRule(androidFormat: AtlasTextureFormat.Rgba32);
            var errors = new List<string>();

            Assert.DoesNotThrow(() => AtlasPlatformFormats.ValidateRule(rule, errors));
            Assert.IsEmpty(errors);
        }

        [Test]
        public void ZeroAtlasSize_IsAnError()
        {
            AtlasImportRule rule = CreateRule(android: 0, atlasMaxSize: 0);
            var errors = new List<string>();

            AtlasPlatformFormats.ValidateRule(rule, errors);

            Assert.IsNotEmpty(errors);
            Assert.That(errors[0], Does.Contain("power of two"));
        }

        // ----------------------------------------------------------------
        // Tri-state toggles: include-in-build and alpha dilation
        // ----------------------------------------------------------------

        [Test]
        public void ResolveToggle_InheritFollowsTheGlobalDefault()
        {
            Assert.IsTrue(
                AtlasImportRule.ResolveToggle(AtlasToggleOverride.Inherit, globalDefault: true));
            Assert.IsFalse(
                AtlasImportRule.ResolveToggle(AtlasToggleOverride.Inherit, globalDefault: false));
        }

        [Test]
        public void ResolveToggle_ForcedValuesIgnoreTheGlobalDefault()
        {
            Assert.IsFalse(
                AtlasImportRule.ResolveToggle(AtlasToggleOverride.ForceOff, globalDefault: true));
            Assert.IsTrue(
                AtlasImportRule.ResolveToggle(AtlasToggleOverride.ForceOn, globalDefault: false));
        }

        /// <summary>
        /// A hot-updated project turns include-in-build off for rules whose atlases ship in asset
        /// packages, while bootstrap UI baked into the installer keeps it on. Both must resolve from
        /// one global setting.
        /// </summary>
        [Test]
        public void IncludeInBuild_HotUpdatedAndBootstrapRulesResolveIndependently()
        {
            AtlasImportRule bootstrap = CreateRule();
            AtlasImportRule hotUpdated = AtlasImportRule.Create(
                "HotRule",
                "Assets/HotUI",
                AtlasTextureFormat.Astc6x6,
                AtlasTextureFormat.Astc6x6,
                AtlasGranularity.PerSourceFolder,
                "HotUI",
                includeInBuildOverride: AtlasToggleOverride.ForceOff);

            Assert.IsTrue(bootstrap.ResolveIncludeInBuild(globalDefault: true));
            Assert.IsFalse(hotUpdated.ResolveIncludeInBuild(globalDefault: true));

            Assert.IsFalse(bootstrap.ResolveIncludeInBuild(globalDefault: false));
            Assert.IsFalse(hotUpdated.ResolveIncludeInBuild(globalDefault: false));
        }

        /// <summary>
        /// Migration safety: an asset written before the override fields existed deserializes them
        /// to Inherit, so the resolved value is exactly the global default, as before.
        /// </summary>
        [Test]
        public void ToggleOverrides_DefaultToInherit()
        {
            AtlasImportRule rule = CreateRule();
            Assert.AreEqual(AtlasToggleOverride.Inherit, rule.IncludeInBuildOverride);
            Assert.AreEqual(AtlasToggleOverride.Inherit, rule.AlphaDilationOverride);
            Assert.IsTrue(rule.ResolveAlphaDilation(globalDefault: true));
        }

        [Test]
        public void AlphaDilation_CanBeForcedOffPerPixelArtRule()
        {
            AtlasImportRule pixelArt = AtlasImportRule.Create(
                "PixelArtRule",
                "Assets/Pixel",
                AtlasTextureFormat.Rgba32,
                AtlasTextureFormat.Rgba32,
                AtlasGranularity.PerSourceFolder,
                "Pixel",
                alphaDilationOverride: AtlasToggleOverride.ForceOff);

            Assert.IsFalse(pixelArt.ResolveAlphaDilation(globalDefault: true));
        }

        // ----------------------------------------------------------------
        // Pixel-art smart default: dilation off, rotation off when Inherit
        // ----------------------------------------------------------------

        [Test]
        public void AlphaDilation_PixelArtInheritsOff_RegardlessOfGlobalDefault()
        {
            AtlasImportRule pixelArt = AtlasImportRule.Create(
                "PixelRule",
                "Assets/Pixel",
                AtlasTextureFormat.Rgba32,
                AtlasTextureFormat.Rgba32,
                AtlasGranularity.PerSourceFolder,
                "Pixel",
                pixelArt: true);

            Assert.IsFalse(pixelArt.ResolveAlphaDilation(globalDefault: true));
            Assert.IsFalse(pixelArt.ResolveAlphaDilation(globalDefault: false));
        }

        [Test]
        public void AlphaDilation_PixelArtForceOn_BypassesSmartDefault()
        {
            AtlasImportRule pixelArt = AtlasImportRule.Create(
                "PixelRule",
                "Assets/Pixel",
                AtlasTextureFormat.Rgba32,
                AtlasTextureFormat.Rgba32,
                AtlasGranularity.PerSourceFolder,
                "Pixel",
                pixelArt: true,
                alphaDilationOverride: AtlasToggleOverride.ForceOn);

            Assert.IsTrue(pixelArt.ResolveAlphaDilation(globalDefault: false));
        }

        [Test]
        public void AlphaDilation_NonPixelArtFollowsGlobal_WhenInherit()
        {
            AtlasImportRule filtered = AtlasImportRule.Create(
                "FilteredRule",
                "Assets/UI",
                AtlasTextureFormat.Astc6x6,
                AtlasTextureFormat.Astc6x6,
                AtlasGranularity.PerSourceFolder,
                "UI");

            Assert.IsTrue(filtered.ResolveAlphaDilation(globalDefault: true));
            Assert.IsFalse(filtered.ResolveAlphaDilation(globalDefault: false));
        }

        [Test]
        public void AtlasRotation_PixelArtInheritsOff_RegardlessOfGlobalDefault()
        {
            AtlasImportRule pixelArt = AtlasImportRule.Create(
                "PixelRule",
                "Assets/Pixel",
                AtlasTextureFormat.Rgba32,
                AtlasTextureFormat.Rgba32,
                AtlasGranularity.PerSourceFolder,
                "Pixel",
                pixelArt: true);

            Assert.IsFalse(pixelArt.ResolveAtlasRotation(globalEnableRotation: true));
            Assert.IsFalse(pixelArt.ResolveAtlasRotation(globalEnableRotation: false));
        }

        /// <summary>
        /// Pixel art outranks an explicit Enabled. Deliberately asymmetric with alpha dilation,
        /// where Force On does bypass the pixel-art default: dilation on pixel art is merely wasted
        /// work, whereas rotated packing samples at non-integer texels and visibly damages the art.
        /// Refusing a request that can only produce a defect is the fail-safe direction — the worst
        /// case is a slightly less dense atlas.
        /// </summary>
        [Test]
        public void AtlasRotation_PixelArtOutranksExplicitEnabled()
        {
            AtlasImportRule pixelArt = AtlasImportRule.Create(
                "PixelRule",
                "Assets/Pixel",
                AtlasTextureFormat.Rgba32,
                AtlasTextureFormat.Rgba32,
                AtlasGranularity.PerSourceFolder,
                "Pixel",
                pixelArt: true,
                atlasRotationMode: AtlasRotationMode.Enabled);

            Assert.IsFalse(
                pixelArt.ResolveAtlasRotation(globalEnableRotation: true),
                "pixel art is a hard block, not a default that Enabled can override");
            Assert.IsFalse(pixelArt.ResolveAtlasRotation(globalEnableRotation: false));
        }

        /// <summary>
        /// The contrast case: on a non-pixel-art rule, Enabled does win over the global default.
        /// Together with the test above this pins the whole truth table instead of half of it.
        /// </summary>
        [Test]
        public void AtlasRotation_NonPixelArtExplicitEnabledWins()
        {
            AtlasImportRule filtered = AtlasImportRule.Create(
                "FilteredRule",
                "Assets/UI",
                AtlasTextureFormat.Astc6x6,
                AtlasTextureFormat.Astc6x6,
                AtlasGranularity.PerSourceFolder,
                "UI",
                pixelArt: false,
                atlasRotationMode: AtlasRotationMode.Enabled);

            Assert.IsTrue(filtered.ResolveAtlasRotation(globalEnableRotation: false));
            Assert.IsTrue(filtered.ResolveAtlasRotation(globalEnableRotation: true));
        }

        [Test]
        public void AtlasRotation_NonPixelArtFollowsGlobal_WhenInherit()
        {
            AtlasImportRule filtered = AtlasImportRule.Create(
                "FilteredRule",
                "Assets/UI",
                AtlasTextureFormat.Astc6x6,
                AtlasTextureFormat.Astc6x6,
                AtlasGranularity.PerSourceFolder,
                "UI");

            Assert.IsTrue(filtered.ResolveAtlasRotation(globalEnableRotation: true));
            Assert.IsFalse(filtered.ResolveAtlasRotation(globalEnableRotation: false));
        }
    }
}
