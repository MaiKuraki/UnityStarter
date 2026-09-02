using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
// FilterMode comes from UnityEngine; the tests below assert the PixelArt → Point override.

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
        // EffectiveFilterMode: PixelArt implies Point for the atlas
        // ----------------------------------------------------------------

        /// <summary>
        /// The atlas texture is what renders at runtime — a source set to Point means nothing once
        /// it is packed. Pixel art therefore implies Point filtering, the same way it already
        /// forces RGBA32: "Pixel Art" that ships bilinear is the exact outcome the toggle exists to
        /// prevent. The stored FilterMode still applies whenever PixelArt is off.
        /// </summary>
        [Test]
        public void EffectiveFilterMode_PixelArtForcesPoint()
        {
            AtlasImportRule rule = AtlasImportRule.Create(
                "PixelRule",
                "Assets/UI",
                AtlasTextureFormat.Astc6x6,
                AtlasTextureFormat.Astc6x6,
                AtlasGranularity.PerSourceFolder,
                "PixelGroup",
                filterMode: FilterMode.Bilinear,
                pixelArt: true);

            Assert.AreEqual(FilterMode.Point, rule.EffectiveFilterMode);
        }

        [Test]
        public void EffectiveFilterMode_WithoutPixelArt_UsesStoredValue()
        {
            AtlasImportRule rule = AtlasImportRule.Create(
                "NormalRule",
                "Assets/UI",
                AtlasTextureFormat.Astc6x6,
                AtlasTextureFormat.Astc6x6,
                AtlasGranularity.PerSourceFolder,
                "NormalGroup",
                filterMode: FilterMode.Bilinear,
                pixelArt: false);

            Assert.AreEqual(FilterMode.Bilinear, rule.EffectiveFilterMode);
        }

        [Test]
        public void EffectiveFilterMode_PixelArtWithExplicitPoint_StaysPoint()
        {
            AtlasImportRule rule = AtlasImportRule.Create(
                "AlreadyPointRule",
                "Assets/UI",
                AtlasTextureFormat.Astc6x6,
                AtlasTextureFormat.Astc6x6,
                AtlasGranularity.PerSourceFolder,
                "PointGroup",
                filterMode: FilterMode.Point,
                pixelArt: true);

            Assert.AreEqual(FilterMode.Point, rule.EffectiveFilterMode);
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

        /// <summary>
        /// The boundary the allocation-free rewrite must preserve. The old form built
        /// <c>excludedFolder + "/"</c> and used StartsWith, which also had to be checked against the
        /// equality case separately; this pins the segment-boundary behaviour that replaces it.
        /// </summary>
        [TestCase("Assets/UI/Raw/a.png", ExpectedResult = true)]
        [TestCase("Assets/UI/Raw", ExpectedResult = true, Description = "The folder itself")]
        [TestCase("Assets/UI/RawFoo/a.png", ExpectedResult = false, Description = "Prefix must be a whole segment")]
        [TestCase("Assets/UI/RawExtra", ExpectedResult = false)]
        [TestCase("assets/ui/raw/a.png", ExpectedResult = true, Description = "Case-insensitive")]
        [TestCase("Assets/UI/a.png", ExpectedResult = false)]
        public bool IsPathExcluded_FolderPrefixBoundaries(string assetPath)
        {
            return CreateRule(
                "Assets/UI",
                excludedFolderPaths: new[] { "Assets/UI/Raw" }).IsPathExcluded(assetPath);
        }

        // ----------------------------------------------------------------
        // OwnsPath
        // ----------------------------------------------------------------

        /// <summary>
        /// OwnsPath is the combined predicate every pipeline call site wants. It exists because the
        /// callers used to run the folder and keyword match twice per asset per rule — once directly
        /// and once inside the exclusion check's own guard.
        /// </summary>
        [Test]
        public void OwnsPath_CombinesMatchAndExclusion()
        {
            AtlasImportRule rule = CreateRule(
                "Assets/UI",
                excludedFolderPaths: new[] { "Assets/UI/Raw" },
                excludedNameKeywords: new[] { "_temp" });

            Assert.IsTrue(rule.OwnsPath("Assets/UI/a.png"));
            Assert.IsFalse(rule.OwnsPath("Assets/UI/Raw/a.png"), "excluded folder");
            Assert.IsFalse(rule.OwnsPath("Assets/UI/a_temp.png"), "excluded keyword");
            Assert.IsFalse(rule.OwnsPath("Assets/Other/a.png"), "not mine");
        }

        [Test]
        public void OwnsPath_AgreesWithMatchesPathAndIsPathExcluded()
        {
            AtlasImportRule rule = CreateRule(
                "Assets/UI",
                pathKeywords: new[] { "icon" },
                excludedFolderPaths: new[] { "Assets/UI/Raw" });

            var paths = new[]
            {
                "Assets/UI/icon_a.png",
                "Assets/UI/a.png",
                "Assets/UI/Raw/icon_a.png",
                "Assets/Other/icon_a.png",
            };

            foreach (string path in paths)
            {
                Assert.AreEqual(
                    rule.MatchesPath(path) && !rule.IsPathExcluded(path),
                    rule.OwnsPath(path),
                    path);
            }
        }

        /// <summary>
        /// Regression guard for the measured cost: the exclusion check used to concatenate
        /// <c>excludedFolder + "/"</c> per asset per rule per entry, which accounted for the entire
        /// 152 bytes-per-asset allocation on a full rescan. Matching must now allocate nothing.
        /// </summary>
        [Test]
        public void OwnsPath_AllocatesNothingPerAsset()
        {
            AtlasImportRule rule = CreateRule(
                "Assets/Art/UI",
                pathKeywords: new[] { "icon", "btn" },
                excludedFolderPaths: new[]
                {
                    "Assets/Art/UI/Raw",
                    "Assets/Art/UI/Source",
                },
                excludedNameKeywords: new[] { "_draft", "@tmp" });

            var path = "Assets/Art/UI/Sub/icon_00001.png";

            // Measured with the per-thread counter, not GC.GetTotalAllocatedBytes. The precise
            // overload forces a blocking collection, and that collection's own bookkeeping plus any
            // finalizer work still draining from earlier fixtures lands inside the window — it read
            // as 6 to 14 bytes per asset for code that allocates nothing, and varied run to run.
            // The per-thread counter sees only what this loop allocates.
            //
            // Tiered compilation promotes the method after a few hundred calls and a rejit inside
            // the window allocates too, so warm up past promotion before measuring.
            for (int i = 0; i < 256; i++)
            {
                rule.OwnsPath(path);
            }

            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 8192; i++)
            {
                rule.OwnsPath(path);
            }

            long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(
                0L,
                allocated,
                $"rule matching allocated {allocated} bytes over 8192 assets "
                + $"({allocated / 8192.0} per asset)");
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
