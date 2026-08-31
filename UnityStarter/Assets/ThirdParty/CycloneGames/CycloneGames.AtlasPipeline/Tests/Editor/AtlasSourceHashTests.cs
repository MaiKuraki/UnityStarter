using System.Collections.Generic;
using System.IO;
using CycloneGames.AtlasPipeline.Pure;
using NUnit.Framework;

namespace CycloneGames.AtlasPipeline.Tests
{
    /// <summary>
    /// The source fingerprint is the only thing that lets a cold start skip an atlas, so the property
    /// that matters is not the algorithm — it is that the value moves when the pixels move and stays
    /// put when they do not. Everything here is about that boundary, because getting it wrong in the
    /// "stays put" direction ships a stale atlas with no visible symptom.
    /// </summary>
    [TestFixture]
    public sealed class AtlasSourceHashTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "cga-src-hash-" + Path.GetRandomFileName());
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        private string WriteAsset(string name, byte[] bytes, string meta)
        {
            string path = Path.Combine(_root, name);
            File.WriteAllBytes(path, bytes);
            File.WriteAllText(path + AtlasSourceHash.MetaSuffix, meta);
            return path;
        }

        [Test]
        public void Compute_IsStableAcrossCalls()
        {
            string path = WriteAsset("a.png", new byte[] { 1, 2, 3 }, "meta");
            Assert.AreEqual(AtlasSourceHash.Compute(path), AtlasSourceHash.Compute(path));
        }

        /// <summary>
        /// The whole point: repainting a source without renaming it must move the fingerprint. The
        /// manifest's content hash cannot see this, which is exactly the gap this exists to close.
        /// </summary>
        [Test]
        public void Compute_ChangesWhenAssetBytesChange()
        {
            string path = WriteAsset("a.png", new byte[] { 1, 2, 3 }, "meta");
            long before = AtlasSourceHash.Compute(path);

            WriteAsset("a.png", new byte[] { 1, 2, 4 }, "meta");
            long after = AtlasSourceHash.Compute(path);

            Assert.AreNotEqual(before, after);
        }

        /// <summary>
        /// Import settings live in the .meta and change a sprite's rect, so they have to count too.
        /// </summary>
        [Test]
        public void Compute_ChangesWhenMetaChanges()
        {
            string path = WriteAsset("a.png", new byte[] { 1, 2, 3 }, "meta-one");
            long before = AtlasSourceHash.Compute(path);

            WriteAsset("a.png", new byte[] { 1, 2, 3 }, "meta-two");
            long after = AtlasSourceHash.Compute(path);

            Assert.AreNotEqual(before, after);
        }

        /// <summary>
        /// A missing .meta means the asset cannot be vouched for. Returning "unknown" instead of
        /// hashing what is there is what keeps the caller honest: it must regenerate.
        /// </summary>
        [Test]
        public void Compute_ReturnsNullHashWhenMetaIsMissing()
        {
            string path = Path.Combine(_root, "a.png");
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });

            Assert.AreEqual(AtlasHash.NullHash, AtlasSourceHash.Compute(path));
        }

        [Test]
        public void Compute_ReturnsNullHashWhenAssetIsMissing()
        {
            string path = Path.Combine(_root, "missing.png");
            File.WriteAllText(path + AtlasSourceHash.MetaSuffix, "meta");

            Assert.AreEqual(AtlasHash.NullHash, AtlasSourceHash.Compute(path));
        }

        [TestCase("")]
        [TestCase(null)]
        public void Compute_RejectsEmptyPath(string path)
        {
            Assert.AreEqual(AtlasHash.NullHash, AtlasSourceHash.Compute(path));
        }

        /// <summary>
        /// Two different assets must not collide, and a file larger than the read buffer must hash
        /// the whole thing — a fingerprint that only covered the first chunk would let edits past
        /// that point through.
        /// </summary>
        [Test]
        public void Compute_CoversBytesPastTheReadBuffer()
        {
            var bytes = new byte[40000];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = (byte)(i % 251);
            }

            string path = WriteAsset("big.png", bytes, "meta");
            long before = AtlasSourceHash.Compute(path);

            bytes[bytes.Length - 1] = 0x7F;
            WriteAsset("big.png", bytes, "meta");

            Assert.AreNotEqual(before, AtlasSourceHash.Compute(path));
        }

        /// <summary>
        /// Equal-length files with different content must differ. Guards against the fingerprint
        /// degenerating into a length check.
        /// </summary>
        [Test]
        public void Compute_DistinguishesEqualLengthFiles()
        {
            string left = WriteAsset("l.png", new byte[] { 1, 2, 3, 4 }, "meta");
            string right = WriteAsset("r.png", new byte[] { 4, 3, 2, 1 }, "meta");

            Assert.AreNotEqual(AtlasSourceHash.Compute(left), AtlasSourceHash.Compute(right));
        }

        [Test]
        public void TryComputeFile_ReportsMissingFiles()
        {
            var path = Path.Combine(_root, "absent.png");
            Assert.IsFalse(AtlasSourceHash.TryComputeFile(path, out long hash));
            Assert.AreEqual(AtlasHash.NullHash, hash);
        }

        /// <summary>
        /// The pipeline folds per-member fingerprints with XOR. A member that could not be read must
        /// make the atlas unresolvable rather than vanish into the fold — see the caller's guard.
        /// This test documents the arithmetic that makes the guard necessary.
        /// </summary>
        [Test]
        public void NullHashFoldedWithXorDisappears()
        {
            long folded = 0x1234567890abcdefL;
            Assert.AreEqual(folded, folded ^ AtlasHash.NullHash);
        }
    }
}
