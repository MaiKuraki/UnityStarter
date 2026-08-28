using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace CycloneGames.AtlasPipeline.Tests
{
    /// <summary>
    /// Pure-logic tests for AtlasNaming. Locks the naming policy's boundary behavior so later
    /// refactors (e.g. the SanitizeAtlasPart fast path) cannot silently change semantics.
    /// </summary>
    [TestFixture]
    public sealed class AtlasNamingTests
    {
        [SetUp]
        public void SetUp()
        {
            // This fixture tests under the un-tightened policy. The static flag is synced by the
            // pipeline from the settings asset (true after the user enables ASCII-Only), so it must
            // be pinned here — otherwise test results depend on the user's current config, which was
            // the root cause of a failing case (with ASCII-Only on, "图标.png" is correctly rejected
            // while the test still asserted the default accept).
            AtlasNaming.AsciiOnlyNames = false;
        }

        // ----------------------------------------------------------------
        // IsValidAtlasFileName
        // ----------------------------------------------------------------

        [TestCase("icon_1.png", ExpectedResult = true)]
        [TestCase("a-b.png", ExpectedResult = true)]
        [TestCase("123.png", ExpectedResult = true)]
        [TestCase("icon.PNG", ExpectedResult = true)]
        [TestCase("noext", ExpectedResult = true)]
        [TestCase("my icon.png", ExpectedResult = false, Description = "Contains a space")]
        [TestCase("  a.png", ExpectedResult = false, Description = "Leading space")]
        [TestCase("a .png", ExpectedResult = false, Description = "Trailing space")]
        [TestCase(".hidden.png", ExpectedResult = false, Description = "Starts with a dot")]
        [TestCase("icon..png", ExpectedResult = false, Description = "Stem contains a dot")]
        [TestCase("CON.png", ExpectedResult = false, Description = "Windows reserved name")]
        [TestCase("con.png", ExpectedResult = false, Description = "Lowercase reserved name")]
        [TestCase("nul.jpg", ExpectedResult = false)]
        [TestCase("com1.png", ExpectedResult = false)]
        [TestCase("lpt9.png", ExpectedResult = false)]
        [TestCase("", ExpectedResult = false)]
        [TestCase("   ", ExpectedResult = false)]
        [TestCase(null, ExpectedResult = false)]
        [TestCase(".png", ExpectedResult = false, Description = "Empty stem")]
        public bool IsValidAtlasFileName_Boundaries(string fileName)
        {
            return AtlasNaming.IsValidAtlasFileName(fileName);
        }

        [Test]
        public void IsValidAtlasFileName_ExactlyAtStemLimit_Passes()
        {
            string fileName = new string('a', 100) + ".png";
            Assert.IsTrue(AtlasNaming.IsValidAtlasFileName(fileName));
        }

        [Test]
        public void IsValidAtlasFileName_OneCharOverStemLimit_Fails()
        {
            string fileName = new string('a', 101) + ".png";
            Assert.IsFalse(AtlasNaming.IsValidAtlasFileName(fileName));
        }

        [Test]
        public void IsValidAtlasFileName_NonAsciiLetterPasses_WhenPolicyDisabled()
        {
            // Locks the default policy: with ASCII-Only off, Unicode letters (CJK) are valid. This
            // is the contract for the default value — enabling ASCII-Only flips the behavior, and
            // the other side is covered by the AsciiOnlyNames_WhenEnabled_* cases.
            Assert.IsTrue(AtlasNaming.IsValidAtlasFileName("图标.png"));
        }

        // ----------------------------------------------------------------
        // TrySuggestSafeFileName
        // ----------------------------------------------------------------

        [Test]
        public void TrySuggestSafeFileName_ReplacesSeparatorsWithSingleUnderscore()
        {
            Assert.IsTrue(AtlasNaming.TrySuggestSafeFileName(
                "my icon@2x.png",
                out string safe));
            Assert.AreEqual("my_icon_2x.png", safe);
        }

        [Test]
        public void TrySuggestSafeFileName_AllInvalidFallsBackToSprite()
        {
            Assert.IsTrue(AtlasNaming.TrySuggestSafeFileName(
                "@@@.png",
                out string safe));
            Assert.AreEqual("Sprite.png", safe);
        }

        [Test]
        public void TrySuggestSafeFileName_ReservedNameGetsSuffix()
        {
            Assert.IsTrue(AtlasNaming.TrySuggestSafeFileName(
                "CON.png",
                out string safe));
            Assert.AreEqual("CON_.png", safe);

            // The suggested name must actually be valid, otherwise this loops forever (BUG-002).
            Assert.IsTrue(AtlasNaming.IsValidAtlasFileName(safe));
        }

        [Test]
        public void TrySuggestSafeFileName_TruncatesToStemLimit()
        {
            string fileName = new string('a', 130) + ".png";
            Assert.IsTrue(AtlasNaming.TrySuggestSafeFileName(
                fileName,
                out string safe));

            string stem = System.IO.Path.GetFileNameWithoutExtension(safe);
            Assert.LessOrEqual(stem.Length, 100, "Truncated stem must not exceed 100 characters");
            Assert.IsTrue(AtlasNaming.IsValidAtlasFileName(safe));
        }

        [Test]
        public void TrySuggestSafeFileName_CollapsedSeparatorsDoNotExceedLimit()
        {
            // Consecutive illegal characters collapse into a single underscore and must not push the
            // name past the limit.
            string fileName = new string('a', 99) + "   !@#" + ".png";
            Assert.IsTrue(AtlasNaming.TrySuggestSafeFileName(
                fileName,
                out string safe));
            Assert.IsTrue(AtlasNaming.IsValidAtlasFileName(safe));
        }

        [Test]
        public void TrySuggestSafeFileName_RejectsNullOrEmpty()
        {
            Assert.IsFalse(AtlasNaming.TrySuggestSafeFileName(
                null,
                out _));
            Assert.IsFalse(AtlasNaming.TrySuggestSafeFileName(
                string.Empty,
                out _));
            Assert.IsFalse(AtlasNaming.TrySuggestSafeFileName(
                "   ",
                out _));
        }

        // ----------------------------------------------------------------
        // ASCII-Only naming policy (default allows Unicode; can be tightened)
        // ----------------------------------------------------------------

        [Test]
        public void AsciiOnlyNames_WhenEnabled_RejectsNonAscii()
        {
            bool previous = AtlasNaming.AsciiOnlyNames;
            try
            {
                AtlasNaming.AsciiOnlyNames = true;

                Assert.IsFalse(AtlasNaming.IsValidAtlasFileName("图标.png"));
                Assert.IsFalse(AtlasNaming.IsValidAtlasFileName("ＵＩ.png"),
                    "Full-width characters must be invalid in tightened mode");
                Assert.IsTrue(AtlasNaming.IsValidAtlasFileName("UI_01.png"),
                    "Pure ASCII names are unaffected");
            }
            finally
            {
                AtlasNaming.AsciiOnlyNames = previous;
            }
        }

        [Test]
        public void AsciiOnlyNames_WhenEnabled_SuggestionIsStillValid()
        {
            bool previous = AtlasNaming.AsciiOnlyNames;
            try
            {
                AtlasNaming.AsciiOnlyNames = true;

                Assert.IsTrue(AtlasNaming.TrySuggestSafeFileName(
                    "图标.png",
                    out string safe));
                Assert.IsTrue(AtlasNaming.IsValidAtlasFileName(safe),
                    $"Suggested name {safe} must be valid in tightened mode, otherwise this loops forever");
            }
            finally
            {
                AtlasNaming.AsciiOnlyNames = previous;
            }
        }

        // ----------------------------------------------------------------
        // MakeUniqueTargetFileName (CollectInvalidAtlasNames cannot be unit-tested without
        // AssetDatabase, so reflection covers the unique-naming algorithm itself)
        // ----------------------------------------------------------------

        [Test]
        public void MakeUniqueTargetFileName_SuffixRespectsStemLimit()
        {
            // BUG-002 regression: a 100-char stem appended with _2 used to become 102 chars,
            // causing an "still invalid after rename → rescan → rename again" loop. Scenario: an
            // invalid name (trailing space) cleans to exactly 100 chars, and the target is already
            // taken by another file.
            var method = typeof(AtlasNaming).GetMethod(
                "MakeUniqueTargetFileName",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(method, "MakeUniqueTargetFileName should exist");

            string stem = new string('a', 100);
            string assetPath = "Assets/UI/" + stem + " .png";
            string safeFileName = stem + ".png";
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // Another file already occupies the cleaned target name.
                "Assets/UI/" + stem + ".png",
            };
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string result = (string)method.Invoke(
                null,
                new object[] { assetPath, safeFileName, existing, used });

            // The unique suffix must not push the stem past the 100-character limit.
            string resultStem = System.IO.Path.GetFileNameWithoutExtension(result);
            Assert.LessOrEqual(resultStem.Length, 100,
                $"Stem is {resultStem.Length} characters after appending the suffix, over the limit");
            Assert.IsTrue(AtlasNaming.IsValidAtlasFileName(result),
                $"The unique file name must still be valid, got {result}");
            StringAssert.EndsWith("_2.png", result, "Should append the _2 suffix");
        }

        // ----------------------------------------------------------------
        // BuildPreview
        // ----------------------------------------------------------------

        [Test]
        public void BuildPreview_TruncatesAndReportsRemaining()
        {
            var requests = Enumerable.Range(0, 20)
                .Select(i => new AtlasRenameRequest(
                    $"Assets/UI/bad {i}.png",
                    $"bad {i}.png",
                    $"bad_{i}.png",
                    "test"))
                .ToList();

            string preview = AtlasNaming.BuildPreview(requests, maxEntries: 12);

            StringAssert.Contains("... and 8 more", preview);
        }

        [Test]
        public void BuildPreview_EmptyOrNullReturnsEmpty()
        {
            Assert.AreEqual(string.Empty, AtlasNaming.BuildPreview(null));
            Assert.AreEqual(
                string.Empty,
                AtlasNaming.BuildPreview(new List<AtlasRenameRequest>()));
        }
    }
}
