using System;
using System.IO;
using NUnit.Framework;
using CycloneGames.AtlasPipeline.Pure;

namespace CycloneGames.AtlasPipeline.Tests
{
    /// <summary>
    /// The manifest is the committed record CI compares against, so the atomic writer's failure
    /// contract is the whole point: any failure must leave the previous manifest byte-for-byte
    /// intact. These tests run against the real file system, which is the only honest way to test
    /// file replacement.
    /// </summary>
    [TestFixture]
    public sealed class AtlasManifestAtomicWriteTests
    {
        private string _directory;
        private string _manifestPath;

        [SetUp]
        public void CreateTempDirectory()
        {
            _directory = Path.Combine(
                Path.GetTempPath(),
                "atlas-pipeline-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            _manifestPath = Path.Combine(_directory, "AtlasPipelineManifest.txt");
        }

        [TearDown]
        public void RemoveTempDirectory()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        private static bool ParseHasEntries(string text)
        {
            // The real WriteManifest validator parses the manifest and compares entry counts; for
            // the writer's contract, any content check stands in.
            return text != null && text.Contains("schema=");
        }

        [Test]
        public void Write_NewFile_CreatesManifestWithoutTempResidue()
        {
            bool written = AtlasManifestFile.TryWriteAtomically(
                _manifestPath, "schema=1", ParseHasEntries, out string error);

            Assert.IsTrue(written, error);
            Assert.AreEqual("schema=1", File.ReadAllText(_manifestPath));
            Assert.IsFalse(
                File.Exists(_manifestPath + AtlasManifestFile.TempSuffix),
                "no temporary file may survive a successful write");
        }

        [Test]
        public void Write_OverExistingFile_ReplacesContentAtomically()
        {
            File.WriteAllText(_manifestPath, "schema=old");

            bool written = AtlasManifestFile.TryWriteAtomically(
                _manifestPath, "schema=new", ParseHasEntries, out string error);

            Assert.IsTrue(written, error);
            Assert.AreEqual("schema=new", File.ReadAllText(_manifestPath));
        }

        [Test]
        public void Write_ConsecutiveWrites_AreStableAndParsable()
        {
            for (int i = 0; i < 2; i++)
            {
                bool written = AtlasManifestFile.TryWriteAtomically(
                    _manifestPath,
                    "schema=1\nentry=" + i,
                    ParseHasEntries,
                    out string error);

                Assert.IsTrue(written, "write " + i + ": " + error);
                Assert.AreEqual(
                    "schema=1\nentry=" + i,
                    File.ReadAllText(_manifestPath),
                    "no newline translation: the committed file must not gain CRLF");
            }
        }

        /// <summary>
        /// The dangerous case, simulated with a validator that rejects: a torn or failed write must
        /// leave the previous manifest byte-for-byte intact — a half-written manifest parses as the
        /// wrong project and reports the wrong drift.
        /// </summary>
        [Test]
        public void Write_WhenVerificationFails_PreviousManifestIsUntouched()
        {
            File.WriteAllText(_manifestPath, "schema=previous");

            bool written = AtlasManifestFile.TryWriteAtomically(
                _manifestPath, "schema=torn", text => false, out string error);

            Assert.IsFalse(written);
            Assert.IsNotEmpty(error);
            Assert.AreEqual(
                "schema=previous",
                File.ReadAllText(_manifestPath),
                "the previous manifest must survive a rejected write");
        }

        [Test]
        public void Write_NonUtf8SafeContent_IsWrittenWithoutBom()
        {
            bool written = AtlasManifestFile.TryWriteAtomically(
                _manifestPath, "schema=1\ngenerator=战斗", ParseHasEntries, out string error);

            Assert.IsTrue(written, error);
            byte[] bytes = File.ReadAllBytes(_manifestPath);
            Assert.IsFalse(
                bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                "a BOM would show up as a whole-file diff between machines");
        }

        /// <summary>
        /// LF must be preserved exactly as given: the manifest is committed, so a CRLF difference
        /// between a Windows machine and a Linux CI agent would turn every update into a
        /// whole-file diff and every merge into a conflict.
        /// </summary>
        [Test]
        public void Write_PreservesLineEndingsExactly()
        {
            string content = "schema=1\natlas=a\natlas=b";

            Assert.IsTrue(AtlasManifestFile.TryWriteAtomically(
                _manifestPath, content, ParseHasEntries, out string error));

            Assert.IsFalse(
                File.ReadAllText(_manifestPath).Contains("\r"),
                "no CRLF may be introduced");
        }
    }
}
