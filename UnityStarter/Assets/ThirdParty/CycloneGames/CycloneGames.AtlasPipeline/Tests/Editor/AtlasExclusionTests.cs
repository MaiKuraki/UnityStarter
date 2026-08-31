using System.Collections.Generic;
using NUnit.Framework;

namespace CycloneGames.AtlasPipeline.Tests
{
    /// <summary>
    /// Global exclusion is the difference between "the pipeline ignores this folder" and "every rule
    /// has to remember to exclude it". The output atlas folder is excluded without configuration,
    /// because the tool's own output must never be able to become its own input.
    /// </summary>
    [TestFixture]
    public sealed class AtlasExclusionTests
    {
        private const string OutputFolder = "Assets/Atlas";

        private static bool Excluded(
            string path,
            string outputFolder = OutputFolder,
            IReadOnlyList<string> globalExcludes = null)
        {
            return AtlasPipeline.IsGloballyExcluded(path, outputFolder, globalExcludes);
        }

        [Test]
        public void OutputFolder_IsAlwaysExcluded()
        {
            Assert.IsTrue(Excluded("Assets/Atlas/UI.spriteatlasv2"));
            Assert.IsTrue(Excluded("Assets/Atlas/nested/stray.png"));
            Assert.IsTrue(Excluded("Assets/Atlas/stray.png"));
        }

        [Test]
        public void OutputFolderExclusion_DoesNotLeakToSiblingFolders()
        {
            Assert.IsFalse(Excluded("Assets/AtlasExtra/a.png"));
            Assert.IsFalse(Excluded("Assets/Art/a.png"));
            Assert.IsTrue(Excluded("Assets/Atlas/a.png"));
        }

        [Test]
        public void GlobalExcludes_ApplyToTheWholeSubtree()
        {
            var excludes = new List<string> { "Assets/Editor", "Assets/StreamingAssets" };

            Assert.IsTrue(Excluded("Assets/Editor/a.png", globalExcludes: excludes));
            Assert.IsTrue(Excluded("Assets/Editor/deep/nested/a.png", globalExcludes: excludes));
            Assert.IsTrue(Excluded("Assets/StreamingAssets/a.png", globalExcludes: excludes));

            Assert.IsFalse(Excluded("Assets/Editorial/a.png", globalExcludes: excludes));
            Assert.IsFalse(Excluded("Assets/UI/a.png", globalExcludes: excludes));
        }

        [Test]
        public void GlobalExcludes_TolerateMessyEntries()
        {
            var excludes = new List<string>
            {
                "Assets\\Editor\\",
                "  Assets/Resources  ",
                string.Empty,
                null,
            };

            Assert.IsTrue(Excluded("Assets/Editor/a.png", globalExcludes: excludes));
            Assert.IsTrue(Excluded("Assets/Resources/a.png", globalExcludes: excludes));
            Assert.IsFalse(Excluded("Assets/UI/a.png", globalExcludes: excludes));
        }

        [Test]
        public void EmptyAndNullInput_AreExcluded()
        {
            Assert.IsTrue(Excluded(string.Empty));
            Assert.IsTrue(Excluded(null));
        }

        /// <summary>
        /// A rule can point its source folder at the output folder only by misconfiguration, and the
        /// overlap check in ValidateForBuild already rejects that. Exclusion still has to win here so
        /// the failure is "nothing was indexed" rather than "indexing fed on its own output".
        /// </summary>
        [Test]
        public void OutputFolderExclusion_WinsOverEverythingElse()
        {
            var excludes = new List<string> { "Assets/UI" };
            Assert.IsTrue(Excluded("Assets/Atlas/a.png", globalExcludes: excludes));
        }

        [Test]
        public void NoOutputFolderConfigured_FallsBackToExcludesOnly()
        {
            var excludes = new List<string> { "Assets/Editor" };
            Assert.IsTrue(Excluded("Assets/Editor/a.png", null, excludes));
            Assert.IsFalse(Excluded("Assets/Atlas/a.png", null, excludes));
        }
    }
}
