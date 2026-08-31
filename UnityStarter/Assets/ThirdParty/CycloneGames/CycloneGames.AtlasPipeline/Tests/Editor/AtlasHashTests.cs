using NUnit.Framework;
using CycloneGames.AtlasPipeline.Pure;

namespace CycloneGames.AtlasPipeline.Tests
{
    /// <summary>
    /// Locks the hash implementation to concrete values. These constants are the whole point of using
    /// FNV-1a instead of <see cref="string.GetHashCode"/>: a value produced on an artist machine must
    /// equal one produced by CI, on a different runtime, months later. If any of these ever change,
    /// every committed atlas manifest becomes invalid at once.
    /// </summary>
    [TestFixture]
    public sealed class AtlasHashTests
    {
        private const int Fnv32Abc = 440920331;
        private const int Fnv32UiPath = 714391369;
        private const long Fnv64Abc = -1792535898324117685L;
        private const long Fnv64UiPath = -140830980634145463L;
        private const long Combine64OneTwo = 954376094613443L;
        private const long Combine64TwoOne = 1914249747341317L;

        [Test]
        public void ComputeFnv1a_MatchesRecordedValues()
        {
            Assert.AreEqual(AtlasHash.NullHash, AtlasHash.ComputeFnv1a(null));
            Assert.AreEqual(AtlasHash.NullHash, AtlasHash.ComputeFnv1a(string.Empty));
            Assert.AreEqual(Fnv32Abc, AtlasHash.ComputeFnv1a("abc"));
        }

        [Test]
        public void ComputeFnv1a64_MatchesRecordedValues()
        {
            Assert.AreEqual(AtlasHash.NullHash, AtlasHash.ComputeFnv1a64(null));
            Assert.AreEqual(Fnv64Abc, AtlasHash.ComputeFnv1a64("abc"));
            Assert.AreEqual(Fnv64UiPath, AtlasHash.ComputeFnv1a64("Assets/UI/btn.png"));
        }

        /// <summary>
        /// Windows and macOS editors disagree on separator and casing. The path hash folds both, so
        /// the same asset must hash identically on every platform — otherwise every atlas looks dirty
        /// on one of them and the team chases phantom diffs forever.
        /// </summary>
        [Test]
        public void ComputePathFnv1a_FoldsCaseAndSeparator()
        {
            Assert.AreEqual(Fnv32UiPath, AtlasHash.ComputePathFnv1a("Assets/UI/btn.png"));
            Assert.AreEqual(Fnv32UiPath, AtlasHash.ComputePathFnv1a("Assets\\UI\\btn.png"));
            Assert.AreEqual(Fnv32UiPath, AtlasHash.ComputePathFnv1a("assets/ui/BTN.png"));
            Assert.AreEqual(Fnv32UiPath, AtlasHash.ComputePathFnv1a("Assets\\UI/Btn.PNG"));
        }

        [Test]
        public void ComputeFnv1a_WithRange_MatchesSubstring()
        {
            const string path = "Assets/UI/btn.png";
            AtlasPathUtility.GetStemRange(path, out int start, out int length);
            Assert.AreEqual("btn", path.Substring(start, length));
            Assert.AreEqual(
                AtlasHash.ComputeFnv1a("btn"),
                AtlasHash.ComputeFnv1a(path, start, length));
        }

        [Test]
        public void AppendFnv1a_StreamingMatchesOneShot()
        {
            int streamed = AtlasHash.BeginFnv1a();
            AtlasHash.AppendFnv1a(ref streamed, "Assets/UI/btn.png");
            Assert.AreEqual(AtlasHash.ComputeFnv1a("Assets/UI/btn.png"), streamed);
        }

        /// <summary>
        /// Documents the hazard the atlas index works around: streaming has no implicit separator, so
        /// ("ab", "c") and ("a", "bc") collide. Callers must append one explicitly.
        /// </summary>
        [Test]
        public void AppendFnv1a_HasNoImplicitSeparator()
        {
            int left = AtlasHash.BeginFnv1a();
            AtlasHash.AppendFnv1a(ref left, "ab");
            AtlasHash.AppendFnv1a(ref left, "c");

            int right = AtlasHash.BeginFnv1a();
            AtlasHash.AppendFnv1a(ref right, "a");
            AtlasHash.AppendFnv1a(ref right, "bc");

            Assert.AreEqual(left, right, "documented collision: add an explicit separator");

            int separated = AtlasHash.BeginFnv1a();
            AtlasHash.AppendFnv1a(ref separated, "ab");
            AtlasHash.AppendFnv1a(ref separated, '\u001F');
            AtlasHash.AppendFnv1a(ref separated, "c");
            Assert.AreNotEqual(left, separated);
        }

        [Test]
        public void AppendFnv1a_NullAndEmptyAreNoOps()
        {
            int baseline = AtlasHash.BeginFnv1a();
            AtlasHash.AppendFnv1a(ref baseline, "x");

            int withNulls = baseline;
            AtlasHash.AppendFnv1a(ref withNulls, (string)null);
            AtlasHash.AppendFnv1a(ref withNulls, string.Empty);

            Assert.AreEqual(baseline, withNulls);
        }

        /// <summary>
        /// Combine64 folds an order-sensitive hash with an order-independent one. A plain XOR would be
        /// commutative and would silently discard the order, making the fingerprint blind to
        /// reordering.
        /// </summary>
        [Test]
        public void Combine64_IsOrderSensitiveAndStable()
        {
            Assert.AreEqual(Combine64OneTwo, AtlasHash.Combine64(1L, 2L));
            Assert.AreEqual(Combine64TwoOne, AtlasHash.Combine64(2L, 1L));
            Assert.AreNotEqual(
                AtlasHash.Combine64(-7L, 991L),
                AtlasHash.Combine64(991L, -7L));
        }

        [Test]
        public void ToHex_IsLowercaseAndFixedWidth()
        {
            Assert.AreEqual("00000000", AtlasHash.ToHex(0));
            Assert.AreEqual("0000ffff", AtlasHash.ToHex(0xFFFF));
            Assert.AreEqual("ffffffff", AtlasHash.ToHex(unchecked((int)0xFFFFFFFF)));
            Assert.AreEqual(16, AtlasHash.ToHex(long.MaxValue).Length);
        }
    }
}
