using System.Collections.Generic;
using NUnit.Framework;

namespace CycloneGames.AtlasPipeline.Tests
{
    /// <summary>
    /// Which rules need "AOT" treatment (Include In Build On) depends on what the installer-baked
    /// scenes and Resources content actually reference. The classifier turns that dependency scan
    /// into per-rule advice: a rule whose sprites are referenced by baked content while resolving
    /// Off is told so, so the decision stops being guesswork.
    /// </summary>
    [TestFixture]
    public sealed class AtlasBakedReferenceTests
    {
        private static AtlasImportRule CreateRule(
            string name,
            string folder,
            AtlasToggleOverride includeInBuild = AtlasToggleOverride.Inherit,
            AtlasGranularity granularity = AtlasGranularity.PerSourceFolder)
        {
            return AtlasImportRule.Create(
                name,
                folder,
                AtlasTextureFormat.Astc6x6,
                AtlasTextureFormat.Astc6x6,
                granularity,
                name,
                includeInBuildOverride: includeInBuild);
        }

        [Test]
        public void RuleResolvingOff_WithBakedReferences_IsReported()
        {
            var rules = new[]
            {
                CreateRule("HotUI", "Assets/HotUI"),
            };

            var warnings = new List<string>();
            AtlasPipeline.ClassifyBakedSpriteConflicts(
                new List<string> { "Assets/HotUI/btn.png", "Assets/HotUI/icon.png" },
                rules,
                globalIncludeInBuild: false,
                warnings: warnings);

            Assert.AreEqual(1, warnings.Count);
            Assert.That(warnings[0], Does.Contain("HotUI"));
            Assert.That(warnings[0], Does.Contain("2"));
            Assert.That(warnings[0], Does.Contain("Include In Build"));
        }

        [Test]
        public void RuleResolvingOn_WithBakedReferences_IsNotAConflict()
        {
            // Bootstrap rule, Force On, referenced by baked content: exactly the intended mixed
            // setup — the atlas bakes into the installer and the references resolve through it.
            var rules = new[]
            {
                CreateRule("Bootstrap", "Assets/Boot", AtlasToggleOverride.ForceOn),
            };

            var warnings = new List<string>();
            AtlasPipeline.ClassifyBakedSpriteConflicts(
                new List<string> { "Assets/Boot/logo.png" },
                rules,
                globalIncludeInBuild: false,
                warnings: warnings);

            Assert.IsEmpty(warnings);
        }

        [Test]
        public void GlobalOnWithInheritRules_IsNotAConflict()
        {
            // Monolithic project: global On, rule inherits, baked references resolve through the
            // baked atlas. Nothing to report.
            var rules = new[]
            {
                CreateRule("UI", "Assets/UI"),
            };

            var warnings = new List<string>();
            AtlasPipeline.ClassifyBakedSpriteConflicts(
                new List<string> { "Assets/UI/a.png" },
                rules,
                globalIncludeInBuild: true,
                warnings: warnings);

            Assert.IsEmpty(warnings);
        }

        [Test]
        public void SpriteOutsideEveryRule_IsIgnored()
        {
            var rules = new[]
            {
                CreateRule("UI", "Assets/UI"),
            };

            var warnings = new List<string>();
            AtlasPipeline.ClassifyBakedSpriteConflicts(
                new List<string> { "Assets/Unmanaged/a.png" },
                rules,
                globalIncludeInBuild: false,
                warnings: warnings);

            Assert.IsEmpty(warnings);
        }

        /// <summary>
        /// Rules resolve in order: a sprite under both a broad and a narrow rule belongs to the
        /// first one in the ordered cache, exactly as ResolveRule would answer it.
        /// </summary>
        [Test]
        public void Ownership_FollowsOrderedRuleResolution()
        {
            var rules = new[]
            {
                CreateRule("Narrow", "Assets/UI/Hud", AtlasToggleOverride.ForceOff),
                CreateRule("Broad", "Assets/UI"),
            };

            var warnings = new List<string>();
            AtlasPipeline.ClassifyBakedSpriteConflicts(
                new List<string> { "Assets/UI/Hud/health.png" },
                rules,
                globalIncludeInBuild: false,
                warnings: warnings);

            Assert.AreEqual(1, warnings.Count);
            Assert.That(warnings[0], Does.Contain("Narrow"), "the first matching rule owns the sprite");
        }

        [Test]
        public void NoneGranularityRules_AreSkipped()
        {
            // Granularity None means the pipeline does not atlas the folder at all; there is no
            // atlas whose Include In Build could matter.
            var rules = new[]
            {
                CreateRule("Raw", "Assets/Raw", AtlasToggleOverride.Inherit, AtlasGranularity.None),
            };

            var warnings = new List<string>();
            AtlasPipeline.ClassifyBakedSpriteConflicts(
                new List<string> { "Assets/Raw/a.png" },
                rules,
                globalIncludeInBuild: false,
                warnings: warnings);

            Assert.IsEmpty(warnings);
        }

        [Test]
        public void EmptyInputs_ProduceNothing()
        {
            var warnings = new List<string>();
            AtlasPipeline.ClassifyBakedSpriteConflicts(null, null, false, warnings);
            AtlasPipeline.ClassifyBakedSpriteConflicts(
                new List<string>(),
                new[] { CreateRule("UI", "Assets/UI") },
                false,
                warnings);
            AtlasPipeline.ClassifyBakedSpriteConflicts(
                new List<string> { "Assets/UI/a.png" },
                new AtlasImportRule[0],
                false,
                warnings);
            Assert.IsEmpty(warnings);
        }

        [Test]
        public void NullWarningsCollector_IsTolerated()
        {
            var rules = new[]
            {
                CreateRule("HotUI", "Assets/HotUI"),
            };

            Assert.DoesNotThrow(
                () => AtlasPipeline.ClassifyBakedSpriteConflicts(
                    new List<string> { "Assets/HotUI/a.png" },
                    rules,
                    false,
                    null));
        }

        /// <summary>
        /// Mixed setup end-to-end: bootstrap Force On with baked refs is silent, hot rule with
        /// baked refs is reported — one message per rule, naming the count.
        /// </summary>
        [Test]
        public void MixedProject_ReportsOnlyTheHotRuleWithBakedReferences()
        {
            var rules = new[]
            {
                CreateRule("Bootstrap", "Assets/Boot", AtlasToggleOverride.ForceOn),
                CreateRule("HotUI", "Assets/HotUI"),
                CreateRule("HotIcons", "Assets/HotIcons"),
            };

            var warnings = new List<string>();
            AtlasPipeline.ClassifyBakedSpriteConflicts(
                new List<string>
                {
                    "Assets/Boot/logo.png",
                    "Assets/HotUI/panel.png",
                    "Assets/HotUI/btn.png",
                },
                rules,
                globalIncludeInBuild: false,
                warnings: warnings);

            Assert.AreEqual(1, warnings.Count);
            Assert.That(warnings[0], Does.Contain("HotUI"));
            Assert.That(warnings[0], Does.Not.Contain("Bootstrap"));
            Assert.That(warnings[0], Does.Not.Contain("HotIcons"));
        }
    }
}
