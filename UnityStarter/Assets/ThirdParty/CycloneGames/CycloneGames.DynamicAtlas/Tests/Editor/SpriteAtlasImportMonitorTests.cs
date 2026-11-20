using CycloneGames.DynamicAtlas.Editor;
using NUnit.Framework;

namespace CycloneGames.DynamicAtlas.Tests.Editor
{
    [TestFixture]
    public sealed class SpriteAtlasImportMonitorTests
    {
        [TestCase("Assets/UI/Common.spriteatlas", true, TestName = "V1_Asset")]
        [TestCase("Assets/UI/Common.spriteatlasv2", true, TestName = "V2_Asset")]
        [TestCase("Assets/Settings/Atlas/UI_General_0.spriteatlasv2", true, TestName = "V2_GeneratedPath")]
        [TestCase("Assets/UI/HUD.SPRITEATLASV2", true, TestName = "V2_UpperCase")]
        [TestCase("Assets/UI/Hud.SpriteAtlasV2", true, TestName = "V2_MixedCase")]
        [TestCase("Assets/UI/HUD.SPRITEATLAS", true, TestName = "V1_UpperCase")]
        [TestCase("Assets/UI/\u56fe\u96c6/\u56fe\u6807.spriteatlasv2", true, TestName = "V2_UnicodeDirectory")]
        [TestCase("Assets/My UI Folder/Atlas A.spriteatlasv2", true, TestName = "V2_Spaces")]
        [TestCase("Assets/UI/icon.png", false, TestName = "NonAsset_Png")]
        [TestCase("Assets/UI/Common.spriteatlas.meta", false, TestName = "NonAsset_V1_Meta")]
        [TestCase("Assets/UI/Common.spriteatlasv2.meta", false, TestName = "NonAsset_V2_Meta")]
        [TestCase("Assets/UI/Common.spriteatlasv2.bak", false, TestName = "NonAsset_Bak")]
        [TestCase("Assets/UI/Common.spriteatlasv20", false, TestName = "NonAsset_Lookalike_v20")]
        [TestCase("Assets/UI/Common.spriteatlasv", false, TestName = "NonAsset_Truncated")]
        [TestCase("Assets/UI/Common.spriteatlas2", false, TestName = "NonAsset_Lookalike_2")]
        [TestCase("Assets/UI/Common.spriteatlasV2.txt", false, TestName = "NonAsset_TextFile")]
        [TestCase("", false, TestName = "NonAsset_EmptyPath")]
        public void TouchesSpriteAtlas_MatchesOnlyRealAssetPaths(string path, bool expected)
        {
            Assert.That(
                SpriteAtlasImportMonitor.TouchesSpriteAtlas(new[] { path }),
                Is.EqualTo(expected),
                "path: " + path);
        }

        [Test]
        public void TouchesSpriteAtlas_V2AtEveryDirectoryDepthFromOneToTwentyFour()
        {
            for (int depth = 1; depth <= 24; depth++)
            {
                string prefix = "Assets";
                for (int i = 0; i < depth; i++)
                {
                    prefix += "/N" + i;
                }

                Assert.That(
                    SpriteAtlasImportMonitor.TouchesSpriteAtlas(
                        new[] { prefix + "/Atlas.spriteatlasv2" }),
                    Is.True,
                    "V2 at depth " + depth);
                Assert.That(
                    SpriteAtlasImportMonitor.TouchesSpriteAtlas(
                        new[] { prefix + "/Atlas.spriteatlas" }),
                    Is.True,
                    "V1 at depth " + depth);
            }
        }

        private static bool WouldTrigger(
            string[] imported,
            string[] deleted,
            string[] moved,
            string[] movedFrom)
        {
            return SpriteAtlasImportMonitor.TouchesSpriteAtlas(imported)
                   || SpriteAtlasImportMonitor.TouchesSpriteAtlas(deleted)
                   || SpriteAtlasImportMonitor.TouchesSpriteAtlas(moved)
                   || SpriteAtlasImportMonitor.TouchesSpriteAtlas(movedFrom);
        }

        [Test]
        public void V2_TriggersOnEveryPostprocessChannel()
        {
            string[] v2 = { "Assets/UI/Common.spriteatlasv2" };
            string[] none = { "Assets/UI/icon.png" };

            Assert.That(WouldTrigger(v2, none, none, none), Is.True, "imported channel");
            Assert.That(WouldTrigger(none, v2, none, none), Is.True, "deleted channel");
            Assert.That(WouldTrigger(none, none, v2, none), Is.True, "moved channel");
            Assert.That(WouldTrigger(none, none, none, v2), Is.True, "movedFrom channel");
            Assert.That(WouldTrigger(none, none, none, none), Is.False, "no atlas touched");
        }

        [Test]
        public void RejectsNullArrayNullElementAndEmptyBatch()
        {
            Assert.That(SpriteAtlasImportMonitor.TouchesSpriteAtlas(null), Is.False);
            Assert.That(SpriteAtlasImportMonitor.TouchesSpriteAtlas(new string[] { null }), Is.False);
            Assert.That(SpriteAtlasImportMonitor.TouchesSpriteAtlas(new string[0]), Is.False);
            Assert.That(
                SpriteAtlasImportMonitor.TouchesSpriteAtlas(
                    new[] { null, "Assets/UI/Common.spriteatlasv2" }),
                Is.True,
                "a null element must not hide a later real asset");
        }

        [Test]
        public void VeryLongV2PathStillMatchesWithoutThrowing()
        {
            string path = "Assets/" + new string('L', 4096) + "/Atlas.spriteatlasv2";
            Assert.That(SpriteAtlasImportMonitor.TouchesSpriteAtlas(new[] { path }), Is.True);

            string nonAsset = "Assets/" + new string('L', 4096) + "/Atlas.png";
            Assert.That(SpriteAtlasImportMonitor.TouchesSpriteAtlas(new[] { nonAsset }), Is.False);
        }

        [Test]
        public void LargeBatchFindsV2AndShortCircuitsWithoutThrowing()
        {
            const int count = 100000;
            var paths = new string[count];
            for (int i = 0; i < count; i++)
            {
                paths[i] = "Assets/UI/tex_" + i + ".png";
            }

            Assert.That(SpriteAtlasImportMonitor.TouchesSpriteAtlas(paths), Is.False);

            paths[count / 2] = "Assets/UI/Middle.spriteatlasv2";
            Assert.That(SpriteAtlasImportMonitor.TouchesSpriteAtlas(paths), Is.True);

            paths[count / 2] = "Assets/UI/Middle.spriteatlas";
            Assert.That(SpriteAtlasImportMonitor.TouchesSpriteAtlas(paths), Is.True);
        }
    }
}
