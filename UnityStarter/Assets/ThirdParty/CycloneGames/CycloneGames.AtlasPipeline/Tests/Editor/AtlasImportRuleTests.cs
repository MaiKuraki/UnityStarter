using System.Collections.Generic;
using NUnit.Framework;

namespace CycloneGames.AtlasPipeline.Tests
{
    /// <summary>
    /// Pure-logic tests for AtlasImportRule path matching. Focuses on locking the prefix boundaries
    /// (the allocation-free rewrite must behave identically to the original) and the keyword
    /// ordering semantics.
    /// </summary>
    [TestFixture]
    public sealed class AtlasImportRuleTests
    {
        private static AtlasImportRule CreateRule(
            string folder,
            IEnumerable<string> pathKeywords = null,
            IEnumerable<string> excludedFolderPaths = null,
            IEnumerable<string> excludedNameKeywords = null)
        {
            return AtlasImportRule.Create(
                "TestRule",
                folder,
                AtlasTextureFormat.Astc6x6,
                AtlasTextureFormat.Astc6x6,
                AtlasGranularity.PerSourceFolder,
                "TestGroup",
                pathKeywords: pathKeywords,
                excludedFolderPaths: excludedFolderPaths,
                excludedNameKeywords: excludedNameKeywords);
        }

        // ----------------------------------------------------------------
        // MatchesPath prefix boundaries
        // ----------------------------------------------------------------

        [TestCase("Assets/UI/a.png", ExpectedResult = true)]
        [TestCase("Assets/UI", ExpectedResult = true, Description = "The folder path itself")]
        [TestCase("Assets/UI/sub/a.png", ExpectedResult = true)]
        [TestCase("Assets/UIFoo/a.png", ExpectedResult = false, Description = "Prefix must be a whole segment, not a substring")]
        [TestCase("Assets/UIExtra", ExpectedResult = false)]
        [TestCase("assets/ui/a.png", ExpectedResult = true, Description = "Case-insensitive")]
        [TestCase("Assets/other/a.png", ExpectedResult = false)]
        [TestCase("", ExpectedResult = false)]
        [TestCase(null, ExpectedResult = false)]
        public bool MatchesPath_PrefixBoundaries(string assetPath)
        {
            return CreateRule("Assets/UI").MatchesPath(assetPath);
        }

        [Test]
        public void MatchesPath_InvalidFolderNeverMatches()
        {
            Assert.IsFalse(CreateRule("Packages/Foo").MatchesPath("Packages/Foo/a.png"));
            Assert.IsFalse(CreateRule("").MatchesPath("Assets/UI/a.png"));
            Assert.IsFalse(CreateRule(null).MatchesPath("Assets/UI/a.png"));
        }

        // ----------------------------------------------------------------
        // Keyword semantics
        // ----------------------------------------------------------------

        [Test]
        public void MatchesPath_NoKeywordsMatchesEverythingUnderFolder()
        {
            AtlasImportRule rule = CreateRule("Assets/UI");
            Assert.IsTrue(rule.MatchesPath("Assets/UI/anything.png"));
        }

        [Test]
        public void MatchesPath_AnyKeywordHitMatches()
        {
            AtlasImportRule rule = CreateRule(
                "Assets/UI",
                pathKeywords: new[] { "icon", "btn" });

            Assert.IsTrue(rule.MatchesPath("Assets/UI/icon_a.png"));
            Assert.IsTrue(rule.MatchesPath("Assets/UI/btn_confirm.png"));
            Assert.IsFalse(rule.MatchesPath("Assets/UI/bg.png"));
        }

        [Test]
        public void MatchesPath_KeywordIsSubstringAnywhere()
        {
            // Locks the current behavior: IndexOf full-path substring matching over-matches — the
            // keyword "icon" hits "fantastic_iconic_x.png". Whether to tighten this to per-segment
            // matching is a pending decision; this test goes red before that changes.
            AtlasImportRule rule = CreateRule(
                "Assets/UI",
                pathKeywords: new[] { "icon" });

            Assert.IsTrue(rule.MatchesPath("Assets/UI/fantastic_iconic_x.png"));
        }

        [Test]
        public void MatchesPath_EmptyKeywordIgnored()
        {
            AtlasImportRule rule = CreateRule(
                "Assets/UI",
                pathKeywords: new[] { "  ", "" });

            Assert.IsFalse(rule.MatchesPath("Assets/UI/a.png"),
                "All-blank keywords should not be treated as a hit");
        }

        // ----------------------------------------------------------------
        // IsPathExcluded
        // ----------------------------------------------------------------

        [Test]
        public void IsPathExcluded_FolderExclusion()
        {
            AtlasImportRule rule = CreateRule(
                "Assets/UI",
                excludedFolderPaths: new[] { "Assets/UI/Raw" });

            Assert.IsTrue(rule.IsPathExcluded("Assets/UI/Raw/a.png"));
            Assert.IsFalse(rule.IsPathExcluded("Assets/UI/a.png"));
        }

        [Test]
        public void IsPathExcluded_KeywordExclusion()
        {
            AtlasImportRule rule = CreateRule(
                "Assets/UI",
                excludedNameKeywords: new[] { "_temp" });

            Assert.IsTrue(rule.IsPathExcluded("Assets/UI/a_temp.png"));
            Assert.IsFalse(rule.IsPathExcluded("Assets/UI/a.png"));
        }

        [Test]
        public void IsPathExcluded_EmptyPathRejected()
        {
            Assert.IsTrue(CreateRule("Assets/UI").IsPathExcluded(""));
        }

        [Test]
        public void IsPathExcluded_NonMatchingPathNotExcluded()
        {
            // Locks the semantics: IsPathExcluded returns false when the path does not belong to
            // this rule (it is not "not mine = excluded"). Callers must check MatchesPath first.
            Assert.IsFalse(CreateRule("Assets/UI").IsPathExcluded("Assets/Scene/a.png"));
        }

        // ----------------------------------------------------------------
        // Normalization
        // ----------------------------------------------------------------

        [TestCase("Assets\\UI\\", ExpectedResult = "Assets/UI")]
        [TestCase(" Assets/UI ", ExpectedResult = "Assets/UI")]
        [TestCase("Assets/UI/", ExpectedResult = "Assets/UI")]
        [TestCase("Assets/UI", ExpectedResult = "Assets/UI")]
        [TestCase(null, ExpectedResult = "")]
        [TestCase("", ExpectedResult = "")]
        public string NormalizedSourceFolder_HandlesSeparatorsAndSlashes(string folder)
        {
            return CreateRule(folder).NormalizedSourceFolder;
        }

        [Test]
        public void NormalizedSourceFolder_IsMemoized()
        {
            AtlasImportRule rule = CreateRule("Assets/UI");
            string first = rule.NormalizedSourceFolder;
            string second = rule.NormalizedSourceFolder;
            Assert.AreSame(first, second,
                "Memoized: stable input should return the same instance (zero allocation)");
        }

        // ----------------------------------------------------------------
        // Rotation and creation
        // ----------------------------------------------------------------

        [Test]
        public void ResolveAtlasRotation_ThreeModes()
        {
            AtlasImportRule inherit = CreateRule("Assets/UI");
            AtlasImportRule enabled = AtlasImportRule.Create(
                "R", "Assets/UI",
                AtlasTextureFormat.Astc6x6,
                AtlasTextureFormat.Astc6x6,
                AtlasGranularity.PerSourceFolder,
                "G",
                atlasRotationMode: AtlasRotationMode.Enabled);
            AtlasImportRule disabled = AtlasImportRule.Create(
                "R", "Assets/UI",
                AtlasTextureFormat.Astc6x6,
                AtlasTextureFormat.Astc6x6,
                AtlasGranularity.PerSourceFolder,
                "G",
                atlasRotationMode: AtlasRotationMode.Disabled);

            // Inherit follows the global setting.
            Assert.IsTrue(inherit.ResolveAtlasRotation(globalEnableRotation: true));
            Assert.IsFalse(inherit.ResolveAtlasRotation(globalEnableRotation: false));
            // Enabled / Disabled override the global setting.
            Assert.IsTrue(enabled.ResolveAtlasRotation(globalEnableRotation: false));
            Assert.IsFalse(disabled.ResolveAtlasRotation(globalEnableRotation: true));
        }

        [Test]
        public void ResolveAtlasRotation_PixelArtForcesDisabled()
        {
            // Pixel art + rotation = non-integer texel sampling artifacts; hard rule: forced off in
            // every mode.
            AtlasImportRule pixelArtExplicitlyEnabled = AtlasImportRule.Create(
                "R", "Assets/UI",
                AtlasTextureFormat.Astc6x6,
                AtlasTextureFormat.Astc6x6,
                AtlasGranularity.PerSourceFolder,
                "G",
                pixelArt: true,
                atlasRotationMode: AtlasRotationMode.Enabled);

            Assert.IsFalse(
                pixelArtExplicitlyEnabled.ResolveAtlasRotation(
                    globalEnableRotation: true),
                "Pixel-art rules must force rotation off even when explicitly Enabled");

            AtlasImportRule pixelArtInherit = AtlasImportRule.Create(
                "R", "Assets/UI",
                AtlasTextureFormat.Astc6x6,
                AtlasTextureFormat.Astc6x6,
                AtlasGranularity.PerSourceFolder,
                "G",
                pixelArt: true);

            Assert.IsFalse(
                pixelArtInherit.ResolveAtlasRotation(globalEnableRotation: true),
                "Pixel-art rules must force rotation off in Inherit mode too, ignoring the global");
        }

        [Test]
        public void Create_NullListsBecomeEmpty()
        {
            AtlasImportRule rule = AtlasImportRule.Create(
                "R",
                "Assets/UI",
                AtlasTextureFormat.Astc6x6,
                AtlasTextureFormat.Astc6x6,
                AtlasGranularity.PerSourceFolder,
                "G");
            Assert.IsNotNull(rule.PathKeywords);
            Assert.AreEqual(0, rule.PathKeywords.Count);
            Assert.IsNotNull(rule.ExcludedFolderPaths);
            Assert.AreEqual(0, rule.ExcludedFolderPaths.Count);
            Assert.IsNotNull(rule.ExcludedNameKeywords);
            Assert.AreEqual(0, rule.ExcludedNameKeywords.Count);
        }
    }
}
