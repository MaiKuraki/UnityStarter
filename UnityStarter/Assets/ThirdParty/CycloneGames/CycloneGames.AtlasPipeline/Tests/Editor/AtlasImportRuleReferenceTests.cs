using System.IO;
using NUnit.Framework;
using UnityEditor;

namespace CycloneGames.AtlasPipeline.Tests
{
    /// <summary>
    /// Integration tests for source-folder GUID references (depend on AssetDatabase, run in
    /// EditMode). Verifies the core experience that a rule automatically follows its folder after a
    /// rename.
    /// </summary>
    [TestFixture]
    public sealed class AtlasImportRuleReferenceTests
    {
        private string _tempRoot;
        private AtlasImportRule _rule;

        [SetUp]
        public void SetUp()
        {
            _tempRoot = "Assets/_AtlasRuleRefTests_"
                        + System.Guid.NewGuid().ToString("N").Substring(0, 8);
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(_tempRoot));
            AssetDatabase.CreateFolder(_tempRoot, "Src");
        }

        [TearDown]
        public void TearDown()
        {
            _rule = null;
            if (!string.IsNullOrEmpty(_tempRoot)
                && AssetDatabase.IsValidFolder(_tempRoot))
            {
                AssetDatabase.DeleteAsset(_tempRoot);
            }
        }

        private AtlasImportRule CreateRuleForTest()
        {
            return AtlasImportRule.Create(
                "R",
                _tempRoot + "/Src",
                AtlasTextureFormat.Astc6x6,
                AtlasTextureFormat.Astc6x6,
                AtlasGranularity.PerSourceFolder,
                "G");
        }

        [Test]
        public void HealSourceFolderGuid_StoresGuidForExistingPath()
        {
            _rule = CreateRuleForTest();

            Assert.IsTrue(_rule.HealSourceFolderGuid(),
                "A valid path should resolve to a GUID");
            Assert.AreEqual(
                AssetDatabase.AssetPathToGUID(_tempRoot + "/Src"),
                _rule.SourceFolderGuid);

            // Idempotent: once the GUID is backfilled, it is not written again.
            Assert.IsFalse(_rule.HealSourceFolderGuid());
        }

        [Test]
        public void SourceFolder_FollowsFolderRename_WhenGuidKnown()
        {
            _rule = CreateRuleForTest();
            Assert.IsTrue(_rule.HealSourceFolderGuid());

            string renamedPath = _tempRoot + "/SrcRenamed";
            string error = AssetDatabase.MoveAsset(_tempRoot + "/Src", renamedPath);
            Assert.IsEmpty(error, $"MoveAsset failed: {error}");

            _rule.RefreshResolvedFolder();
            Assert.AreEqual(
                renamedPath.Replace('\\', '/'),
                _rule.NormalizedSourceFolder,
                "After a folder rename, the rule must resolve to the new path.");
        }

        [Test]
        public void SourceFolder_FallsBackToRawPath_WhenGuidInvalid()
        {
            _rule = CreateRuleForTest();
            string rawPath = _rule.NormalizedSourceFolder;

            // Simulate "folder and its .meta deleted together": the GUID resolves to empty.
            typeof(AtlasImportRule)
                .GetField(
                    "sourceFolderGuid",
                    System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance)
                ?.SetValue(_rule, "deadbeefdeadbeefdeadbeefdeadbeef");
            _rule.RefreshResolvedFolder();

            Assert.AreEqual(
                rawPath,
                _rule.NormalizedSourceFolder,
                "When the GUID is invalid, the rule must fall back to the historical path string so "
                + "validation reports a missing folder instead of silently failing to match.");
        }
    }
}
