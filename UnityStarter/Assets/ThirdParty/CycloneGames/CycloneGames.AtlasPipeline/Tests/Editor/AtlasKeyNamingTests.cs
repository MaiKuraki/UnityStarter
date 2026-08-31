using NUnit.Framework;

namespace CycloneGames.AtlasPipeline.Tests
{
    /// <summary>
    /// The atlas key is the file name of the generated .spriteatlasv2, so every runtime path built
    /// from an atlas depends on these rules. Changing any of them renames shipped assets.
    /// </summary>
    [TestFixture]
    public sealed class AtlasKeyNamingTests
    {
        private const string Folder = "Assets/UI";

        private static AtlasImportRule CreateRule(
            AtlasGranularity granularity,
            string atlasGroup = "UI")
        {
            return AtlasImportRule.Create(
                "TestRule",
                Folder,
                AtlasTextureFormat.Astc6x6,
                AtlasTextureFormat.Astc6x6,
                granularity,
                atlasGroup);
        }

        private static string Key(
            AtlasGranularity granularity,
            string assetPath,
            bool collisionSafe = false,
            string atlasGroup = "UI")
        {
            return AtlasPipeline.ResolveAtlasKey(
                CreateRule(granularity, atlasGroup),
                assetPath,
                collisionSafe);
        }

        [Test]
        public void GranularityNone_ProducesNoKey()
        {
            Assert.IsNull(Key(AtlasGranularity.None, "Assets/UI/a.png"));
            Assert.IsNull(AtlasPipeline.ResolveAtlasKey(null, "Assets/UI/a.png", false));
        }

        [Test]
        public void PerSourceFolder_UsesTheGroupAlone()
        {
            Assert.AreEqual("UI", Key(AtlasGranularity.PerSourceFolder, "Assets/UI/a.png"));
            Assert.AreEqual("UI", Key(AtlasGranularity.PerSourceFolder, "Assets/UI/deep/a.png"));
            Assert.AreEqual(
                "Scene",
                Key(AtlasGranularity.PerSourceFolder, "Assets/UI/a.png", atlasGroup: "Scene"));
        }

        [Test]
        public void PerChildFolder_UsesTheFirstSegmentBelowTheRuleFolder()
        {
            Assert.AreEqual("UI_icons", Key(AtlasGranularity.PerChildFolder, "Assets/UI/icons/a.png"));
            Assert.AreEqual("UI_Root", Key(AtlasGranularity.PerChildFolder, "Assets/UI/a.png"));
            Assert.AreEqual(
                "UI_icons",
                Key(AtlasGranularity.PerChildFolder, "Assets/UI/icons/deep/a.png"),
                "deeper levels are folded into the first segment");
        }

        /// <summary>
        /// The historical behaviour, preserved for existing projects: the file stem alone. Two
        /// identically named files under different folders collapse into one atlas and one set of
        /// sprites silently never ships. It is detected during indexing and blocked by
        /// ValidateForBuild; the collisionSafe switch below is the fix.
        /// </summary>
        [Test]
        public void PerSpriteWithoutCollisionSafety_CollapsesSameNamedFiles()
        {
            Assert.AreEqual("UI_btn", Key(AtlasGranularity.PerSprite, "Assets/UI/a/btn.png"));
            Assert.AreEqual(
                "UI_btn",
                Key(AtlasGranularity.PerSprite, "Assets/UI/b/btn.png"),
                "documented collision - the reason CollisionSafeAtlasKeys exists");
        }

        [Test]
        public void PerSpriteWithCollisionSafety_KeepsSameNamedFilesApart()
        {
            Assert.AreEqual(
                "UI_a_btn",
                Key(AtlasGranularity.PerSprite, "Assets/UI/a/btn.png", collisionSafe: true));
            Assert.AreEqual(
                "UI_b_btn",
                Key(AtlasGranularity.PerSprite, "Assets/UI/b/btn.png", collisionSafe: true));
            Assert.AreEqual(
                "UI_a_deep_btn",
                Key(AtlasGranularity.PerSprite, "Assets/UI/a/deep/btn.png", collisionSafe: true));
            Assert.AreEqual(
                "UI_Root_btn",
                Key(AtlasGranularity.PerSprite, "Assets/UI/btn.png", collisionSafe: true));
        }

        [Test]
        public void KeysAreSanitized()
        {
            Assert.AreEqual(
                "UI_my_icon",
                Key(AtlasGranularity.PerSprite, "Assets/UI/my icon.png"));
            Assert.AreEqual(
                "UI_Atlas",
                Key(AtlasGranularity.PerChildFolder, "Assets/UI/??!/a.png"));
            Assert.AreEqual(
                "My_Group",
                Key(AtlasGranularity.PerSourceFolder, "Assets/UI/a.png", atlasGroup: "My Group"));
        }

        /// <summary>
        /// A raw Windows path must not silently produce a different atlas key, because the key is the
        /// output file name.
        /// </summary>
        [Test]
        public void BackslashPathsProduceTheSameKey()
        {
            Assert.AreEqual(
                Key(AtlasGranularity.PerSprite, "Assets/UI/a/btn.png", collisionSafe: true),
                Key(AtlasGranularity.PerSprite, "Assets\\UI\\a\\btn.png", collisionSafe: true));
        }

        [Test]
        public void KeysAreStableAcrossRepeatedCalls()
        {
            AtlasImportRule rule = CreateRule(AtlasGranularity.PerSprite);
            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(
                    "UI_a_btn",
                    AtlasPipeline.ResolveAtlasKey(
                        rule,
                        "Assets/UI/a/btn.png",
                        collisionSafe: true),
                    "call " + i);
            }
        }

        /// <summary>
        /// The atlas key becomes the output file name. Lowercasing it makes that name predictable from
        /// the rule configuration alone, instead of depending on which spelling of a group or folder
        /// happened to be indexed first — and it stops two groups spelled "UI" and "ui" from racing to
        /// name the same file.
        /// </summary>
        [Test]
        public void LowerCasing_AppliesToTheWholeKey()
        {
            Assert.AreEqual(
                "ui",
                AtlasPipeline.ResolveAtlasKey(
                    CreateRule(AtlasGranularity.PerSourceFolder, "UI"),
                    "Assets/UI/a.png",
                    collisionSafe: false,
                    casing: AtlasKeyCasing.Lower));

            Assert.AreEqual(
                "ui_icons",
                AtlasPipeline.ResolveAtlasKey(
                    CreateRule(AtlasGranularity.PerChildFolder, "UI"),
                    "Assets/UI/Icons/a.png",
                    collisionSafe: false,
                    casing: AtlasKeyCasing.Lower));

            Assert.AreEqual(
                "ui_icons_btn",
                AtlasPipeline.ResolveAtlasKey(
                    CreateRule(AtlasGranularity.PerSprite, "UI"),
                    "Assets/UI/Icons/Btn.png",
                    collisionSafe: true,
                    casing: AtlasKeyCasing.Lower));
        }

        [Test]
        public void LowerCasing_MakesCaseVariantGroupsConverge()
        {
            string fromUpper = AtlasPipeline.ResolveAtlasKey(
                CreateRule(AtlasGranularity.PerSourceFolder, "UI"),
                "Assets/UI/a.png",
                collisionSafe: false,
                casing: AtlasKeyCasing.Lower);
            string fromLower = AtlasPipeline.ResolveAtlasKey(
                CreateRule(AtlasGranularity.PerSourceFolder, "ui"),
                "Assets/UI/a.png",
                collisionSafe: false,
                casing: AtlasKeyCasing.Lower);

            Assert.AreEqual(fromUpper, fromLower);
            Assert.AreEqual("ui", fromUpper);
        }

        [Test]
        public void PreserveCasing_KeepsTheSourceSpelling()
        {
            Assert.AreEqual(
                "UI",
                AtlasPipeline.ResolveAtlasKey(
                    CreateRule(AtlasGranularity.PerSourceFolder, "UI"),
                    "Assets/UI/a.png",
                    collisionSafe: false,
                    casing: AtlasKeyCasing.Preserve));
            Assert.AreEqual(
                "UI_Icons",
                AtlasPipeline.ResolveAtlasKey(
                    CreateRule(AtlasGranularity.PerChildFolder, "UI"),
                    "Assets/UI/Icons/a.png",
                    collisionSafe: false,
                    casing: AtlasKeyCasing.Preserve));
        }

        /// <summary>
        /// A path outside the rule folder degenerates to the group rather than throwing or producing
        /// garbage. Rules only ever receive matching paths, but the pipeline must not crash if one
        /// does not.
        /// </summary>
        [Test]
        public void PathOutsideTheRuleFolderFallsBackToTheGroup()
        {
            Assert.AreEqual(
                "UI",
                Key(AtlasGranularity.PerSprite, "Assets/Other/a.png", collisionSafe: true));
            Assert.AreEqual("UI", Key(AtlasGranularity.PerSprite, "Assets/UI", collisionSafe: true));
        }
    }
}
