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

        [TestCase("UI Battle", ExpectedResult = "UI_Battle")]
        [TestCase("UI@Battle", ExpectedResult = "UI_Battle")]
        [TestCase("UI.Battle", ExpectedResult = "UI_Battle")]
        [TestCase("UI  Battle", ExpectedResult = "UI_Battle",
            Description = "Runs of unsafe characters collapse to one separator")]
        public string SanitizeSubfolder_ReplacesUnsafeCharacters(string value)
        {
            return AtlasPathUtility.SanitizeSubfolder(value);
        }

        /// <summary>
        /// Non-ASCII is preserved on purpose. A studio naming its packages in its own language is
        /// not an error, and Unity plus any modern CI agent handle UTF-8 paths. Forcing ASCII here
        /// would be a different policy — the one the asciiOnlyNames setting applies to generated
        /// file NAMES, complete with its own rename review flow.
        /// </summary>
        [Test]
        public void SanitizeSubfolder_PreservesNonAsciiLetters()
        {
            Assert.AreEqual("战斗", AtlasPathUtility.SanitizeSubfolder("战斗"));
            Assert.AreEqual("战斗/UI", AtlasPathUtility.SanitizeSubfolder("战斗/UI"));
            Assert.AreEqual("战斗_UI", AtlasPathUtility.SanitizeSubfolder("战斗 UI"));
        }

        /// <summary>
        /// A segment with nothing usable in it is dropped, not renamed. SanitizePart falls back to
        /// "Atlas" for an unusable value — right for an atlas key, which must have a name, but
        /// wrong here, where it would silently create a folder called Atlas.
        /// </summary>
        [TestCase("###", ExpectedResult = "")]
        [TestCase("###/UI", ExpectedResult = "UI")]
        [TestCase("UI/###/Battle", ExpectedResult = "UI/Battle")]
        [TestCase("....", ExpectedResult = "")]
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
