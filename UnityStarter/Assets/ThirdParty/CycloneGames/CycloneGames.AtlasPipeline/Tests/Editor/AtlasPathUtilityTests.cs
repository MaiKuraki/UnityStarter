using CycloneGames.AtlasPipeline.Pure;
using NUnit.Framework;

namespace CycloneGames.AtlasPipeline.Tests
{
    /// <summary>
    /// The output subfolder is the one place where user text becomes a path on disk, so the
    /// sanitizer is a boundary, not a convenience. Everything here is about what must never come
    /// out the other side: traversal, escaping the output root, or a surprise folder name.
    /// </summary>
    [TestFixture]
    public sealed class AtlasPathUtilityTests
    {
        [TestCase(null, ExpectedResult = "")]
        [TestCase("", ExpectedResult = "")]
        [TestCase("   ", ExpectedResult = "", Description = "Whitespace only is no subfolder")]
        public string SanitizeSubfolder_EmptyMeansOutputRoot(string value)
        {
            return AtlasPathUtility.SanitizeSubfolder(value);
        }

        [TestCase("UI", ExpectedResult = "UI")]
        [TestCase("UI/Battle", ExpectedResult = "UI/Battle")]
        [TestCase("UI/Battle/Common", ExpectedResult = "UI/Battle/Common",
            Description = "Nesting deeper than one level is preserved")]
        public string SanitizeSubfolder_KeepsWellFormedPaths(string value)
        {
            return AtlasPathUtility.SanitizeSubfolder(value);
        }

        [TestCase("/UI/", ExpectedResult = "UI", Description = "Leading and trailing separators")]
        [TestCase("UI//Battle", ExpectedResult = "UI/Battle", Description = "Doubled separator")]
        [TestCase("UI\\Battle", ExpectedResult = "UI/Battle", Description = "Windows separators")]
        [TestCase("UI\\Battle\\", ExpectedResult = "UI/Battle")]
        public string SanitizeSubfolder_NormalizesSeparators(string value)
        {
            return AtlasPathUtility.SanitizeSubfolder(value);
        }

        /// <summary>
        /// Traversal is the failure that matters: a subfolder is joined onto the output root, so
        /// ".." in it would write atlases outside the folder the pipeline excludes, sweeps and
        /// reasons about — and could land them on top of the source art.
        /// </summary>
        [TestCase("..", ExpectedResult = "")]
        [TestCase("../..", ExpectedResult = "")]
        [TestCase("UI/../Battle", ExpectedResult = "UI/Battle",
            Description = "The traversal segment is dropped, not honoured")]
        [TestCase("UI/../../Battle", ExpectedResult = "UI/Battle")]
        [TestCase("..\\UI", ExpectedResult = "UI")]
        [TestCase("UI/./Battle", ExpectedResult = "UI/Battle", Description = "Self references")]
        public string SanitizeSubfolder_DropsTraversal(string value)
        {
            return AtlasPathUtility.SanitizeSubfolder(value);
        }

        [Test]
        public void SanitizeSubfolder_NeverEscapesTheOutputRoot()
        {
            // The invariant the rest of the pipeline depends on: whatever comes out, joining it onto
            // the output root stays inside that root.
            var inputs = new[]
            {
                "../../..",
                "..\\..\\..\\Windows",
                "/",
                "//",
                "UI/../../../etc",
                "....",
                "...",
            };

            foreach (string input in inputs)
            {
                string result = AtlasPathUtility.SanitizeSubfolder(input);
                Assert.IsFalse(
                    result.StartsWith("/", System.StringComparison.Ordinal),
                    $"'{input}' produced an absolute-looking path: '{result}'");
                Assert.IsFalse(
                    result.Contains(".."),
                    $"'{input}' kept a traversal segment: '{result}'");
            }
        }

        /// <summary>
        /// Folder names are preserved as written, because the subfolder usually names a folder that
        /// already exists: sanitizing "UI Battle" into "UI_Battle" would make generation create a
        /// sanitized twin beside the folder that was actually dragged. Only characters that cannot
        /// live in a path segment everywhere survive — spaces, dots inside the name, @ and non-ASCII
        /// are all legal folder names.
        /// </summary>
        [TestCase("UI Battle", ExpectedResult = "UI Battle")]
        [TestCase("UI@Battle", ExpectedResult = "UI@Battle")]
        [TestCase("UI.Battle", ExpectedResult = "UI.Battle")]
        [TestCase("UI  Battle", ExpectedResult = "UI  Battle")]
        [TestCase("UI.v2", ExpectedResult = "UI.v2")]
        public string SanitizeSubfolder_PreservesLegalFolderNames(string value)
        {
            return AtlasPathUtility.SanitizeSubfolder(value);
        }

        /// <summary>
        /// Windows ignores trailing dots and spaces in folder names, so "UI." is "UI" there and a
        /// different folder everywhere else. Trimming both ends per segment removes the
        /// cross-platform mismatch at the source.
        /// </summary>
        [TestCase("UI.", ExpectedResult = "UI")]
        [TestCase("UI ", ExpectedResult = "UI")]
        [TestCase("UI.../Battle", ExpectedResult = "UI/Battle")]
        [TestCase(" Battle/UI ", ExpectedResult = "Battle/UI")]
        public string SanitizeSubfolder_TrimsPlatformAmbiguousEndings(string value)
        {
            return AtlasPathUtility.SanitizeSubfolder(value);
        }

        /// <summary>
        /// Non-ASCII is preserved on purpose. A studio naming its packages in its own language is
        /// not an error, and Unity plus any modern CI agent handle UTF-8 paths. Forcing ASCII here
        /// would be a different policy — the one the asciiOnlyNames setting applies to generated
        /// file NAMES, complete with its own rename review flow.
        /// </summary>
        /// <summary>
        /// Equality is deliberately NOT nesting: two rules writing into the same output folder is
        /// the intended "two rules, one package" case, so the folder-relationship validation must
        /// only flag strict descendants. Flagging shared folders too would warn on every legitimate
        /// package and train people to ignore the warning.
        /// </summary>
        [Test]
        public void IsProperlyUnderFolder_DistinguishesNestingFromSharing()
        {
            // Strict nesting: flagged.
            Assert.IsTrue(AtlasPathUtility.IsProperlyUnderFolder(
                "Assets/Atlas/UI/Icons", "Assets/Atlas/UI"));
            Assert.IsTrue(AtlasPathUtility.IsProperlyUnderFolder(
                "Assets/Atlas/UI", "Assets/Atlas"));

            // Sharing the same folder: not flagged.
            Assert.IsFalse(AtlasPathUtility.IsProperlyUnderFolder(
                "Assets/Atlas/UI", "Assets/Atlas/UI"));

            // Siblings: not flagged.
            Assert.IsFalse(AtlasPathUtility.IsProperlyUnderFolder(
                "Assets/Atlas/Battle", "Assets/Atlas/UI"));

            // A prefix that is not a folder boundary: not flagged ("UI2" is not inside "UI").
            Assert.IsFalse(AtlasPathUtility.IsProperlyUnderFolder(
                "Assets/Atlas/UI2", "Assets/Atlas/UI"));

            // The outer folder checked against the inner: not flagged in this direction.
            Assert.IsFalse(AtlasPathUtility.IsProperlyUnderFolder(
                "Assets/Atlas/UI", "Assets/Atlas/UI/Icons"));
        }

        [TestCase(null, "Assets/Atlas")]
        [TestCase("Assets/Atlas", null)]
        [TestCase("", "Assets/Atlas")]
        public void IsProperlyUnderFolder_RejectsEmptyPaths(string inner, string outer)
        {
            Assert.IsFalse(AtlasPathUtility.IsProperlyUnderFolder(inner, outer));
        }

        /// <summary>
        /// A hand-edited .asset can carry anything, so the sanitizer is the last line of the
        /// "every atlas stays under the output root" invariant. An absolute-looking value must
        /// degrade to a relative subfolder — never survive as something that could escape the root.
        /// </summary>
        [Test]
        public void SanitizeSubfolder_DegradesAbsolutePathsToRelative()
        {
            Assert.AreEqual(
                "C/Windows",
                AtlasPathUtility.SanitizeSubfolder("C:/Windows"),
                "drive letters are stripped along with the colon");
            Assert.AreEqual(
                "etc",
                AtlasPathUtility.SanitizeSubfolder("/etc"),
                "a leading separator cannot make the result absolute");
        }

        [Test]
        public void SanitizeSubfolder_PreservesNonAsciiLetters()
        {
            Assert.AreEqual("战斗", AtlasPathUtility.SanitizeSubfolder("战斗"));
            Assert.AreEqual("战斗/UI", AtlasPathUtility.SanitizeSubfolder("战斗/UI"));
            Assert.AreEqual("战斗 UI", AtlasPathUtility.SanitizeSubfolder("战斗 UI"));
        }

        /// <summary>
        /// A segment that cannot name a folder on the target platforms is dropped, not renamed:
        /// SanitizePart falls back to "Atlas" for an unusable value — right for an atlas key, which
        /// must have a name, but wrong here, where it would invent a folder nobody asked for.
        /// Note "###" survives: hash is a legal folder name everywhere, and preserving folder names
        /// as written is the point of this sanitizer.
        /// </summary>
        [TestCase("###", ExpectedResult = "###")]
        [TestCase("###/UI", ExpectedResult = "###/UI")]
        [TestCase("UI/###/Battle", ExpectedResult = "UI/###/Battle")]
        [TestCase(":?*", ExpectedResult = "", Description = "only path-invalid characters")]
        [TestCase(":?*/UI", ExpectedResult = "UI")]
        [TestCase("....", ExpectedResult = "", Description = "Windows ignores trailing dots")]
        public string SanitizeSubfolder_DropsUnusableSegments(string value)
        {
            return AtlasPathUtility.SanitizeSubfolder(value);
        }

        /// <summary>
        /// Sanitizing twice must be a no-op. The rule caches the sanitized value, but the property
        /// is also read from paths that were sanitized upstream, so a value that kept changing
        /// would make the output path and the recorded fingerprint disagree.
        /// </summary>
        [TestCase("UI")]
        [TestCase("/UI/")]
        [TestCase("UI\\Battle\\")]
        [TestCase("UI/../Battle")]
        [TestCase("UI Battle")]
        public void SanitizeSubfolder_IsIdempotent(string value)
        {
            string once = AtlasPathUtility.SanitizeSubfolder(value);
            Assert.AreEqual(once, AtlasPathUtility.SanitizeSubfolder(once));
        }
    }
}
