using System.Collections.Generic;
using NUnit.Framework;
using CycloneGames.AtlasPipeline.Pure;

namespace CycloneGames.AtlasPipeline.Tests
{
    /// <summary>
    /// A packable is identified by its source asset path plus its sprite name. Getting either half
    /// wrong leaves an atlas silently stale: comparing by name alone merges "idle_0" from two
    /// different character sheets, and comparing by GUID makes an atlas look stale after harmless
    /// GUID churn.
    /// </summary>
    [TestFixture]
    public sealed class AtlasSpriteIdentityTests
    {
        private const int ExpectedPathHash = 2080972137;
        private const int ExpectedNameHash = -1336059524;
        private const long ExpectedCombined = 8937707275261139324L;

        [Test]
        public void Hashes_MatchRecordedValues()
        {
            var identity = new AtlasSpriteIdentity("Assets/UI/hero.png", "idle_0");
            Assert.AreEqual(ExpectedPathHash, identity.PathHash);
            Assert.AreEqual(ExpectedNameHash, identity.NameHash);
            Assert.AreEqual(ExpectedCombined, identity.IdentityHash);
            Assert.IsTrue(identity.IsValid);
        }

        [Test]
        public void SameNameInDifferentSheets_AreDifferentSprites()
        {
            var fromA = new AtlasSpriteIdentity("Assets/A/hero.png", "idle_0");
            var fromB = new AtlasSpriteIdentity("Assets/B/hero.png", "idle_0");

            Assert.IsFalse(fromA.Equals(fromB));
            Assert.AreNotEqual(fromA, fromB);
        }

        /// <summary>
        /// "Idle_0" and "idle_0" are two distinct sprites in Unity. Folding the name case would merge
        /// them and drop one from the atlas.
        /// </summary>
        [Test]
        public void SpriteName_IsCaseSensitive()
        {
            var lower = new AtlasSpriteIdentity("Assets/UI/hero.png", "idle_0");
            var upper = new AtlasSpriteIdentity("Assets/UI/hero.png", "Idle_0");

            Assert.IsFalse(lower.Equals(upper));
        }

        /// <summary>
        /// The path is compared case-insensitively so a path reported with different casing on
        /// Windows and on a Linux CI agent still identifies one asset.
        /// </summary>
        [Test]
        public void AssetPath_IsCaseInsensitive()
        {
            var lower = new AtlasSpriteIdentity("assets/ui/hero.png", "idle_0");
            var upper = new AtlasSpriteIdentity("Assets/UI/Hero.png", "idle_0");

            Assert.IsTrue(lower.Equals(upper));
        }

        [Test]
        public void PathHash_FoldsSeparatorsToo()
        {
            var forward = new AtlasSpriteIdentity("Assets/UI/hero.png", "idle_0");
            var backward = new AtlasSpriteIdentity("Assets\\UI\\hero.png", "idle_0");

            Assert.AreEqual(forward.PathHash, backward.PathHash);
        }

        /// <summary>
        /// The packable comparison sorts both sides and compares element by element. That is only
        /// sound if the ordering is total: two different identities must never compare equal, or the
        /// comparison could accept a differently ordered set as a match.
        /// </summary>
        [Test]
        public void Ordering_IsTotalAndAntisymmetric()
        {
            string[] names = { "b", "a", "A", "B", "a_0", "a_1", "aa", "a b", "0", "_" };

            for (int i = 0; i < names.Length; i++)
            {
                for (int j = 0; j < names.Length; j++)
                {
                    var left = new AtlasSpriteIdentity("Assets/p.png", names[i]);
                    var right = new AtlasSpriteIdentity("Assets/p.png", names[j]);
                    int forward = left.CompareTo(right);
                    int backward = right.CompareTo(left);

                    if (i == j)
                    {
                        Assert.AreEqual(0, forward, names[i] + " vs itself");
                        Assert.AreEqual(0, backward, names[i] + " vs itself, reversed");
                        continue;
                    }

                    Assert.AreNotEqual(0, forward, names[i] + " vs " + names[j]);
                    Assert.AreEqual(forward, -backward, names[i] + " vs " + names[j]);
                }
            }
        }

        [Test]
        public void Ordering_PutsPathBeforeName()
        {
            var pathA = new AtlasSpriteIdentity("Assets/A.png", "z");
            var pathB = new AtlasSpriteIdentity("Assets/B.png", "a");

            Assert.Less(pathA.CompareTo(pathB), 0);
        }

        [Test]
        public void Sorting_IsIndependentOfInputOrder()
        {
            var forward = new List<AtlasSpriteIdentity>
            {
                new AtlasSpriteIdentity("Assets/UI/c.png", "s2"),
                new AtlasSpriteIdentity("Assets/UI/a.png", "s1"),
                new AtlasSpriteIdentity("Assets/UI/b.png", "s0"),
                new AtlasSpriteIdentity("Assets/UI/a.png", "s0"),
            };

            var reversed = new List<AtlasSpriteIdentity>(forward);
            reversed.Reverse();

            forward.Sort();
            reversed.Sort();

            Assert.AreEqual(forward.Count, reversed.Count);
            for (int i = 0; i < forward.Count; i++)
            {
                Assert.IsTrue(forward[i].Equals(reversed[i]), "index " + i);
            }
        }

        /// <summary>
        /// Manifest comparison works with hash-only identities because the source strings were never
        /// loaded. A null on either side means "unknown", not "mismatch", so those still match a
        /// populated identity with the same hashes.
        /// </summary>
        [Test]
        public void HashOnlyIdentity_MatchesPopulatedIdentity()
        {
            var populated = new AtlasSpriteIdentity("Assets/UI/hero.png", "idle_0");
            var hashOnly = AtlasSpriteIdentity.FromHashes(
                populated.PathHash,
                populated.NameHash);

            Assert.IsTrue(hashOnly.Equals(populated));
            Assert.IsTrue(hashOnly == populated);

            var different = new AtlasSpriteIdentity("Assets/Other/hero.png", "idle_0");
            Assert.IsFalse(hashOnly.Equals(different));
        }

        [Test]
        public void Comparer_MatchesStructEquality()
        {
            var left = new AtlasSpriteIdentity("Assets/A.png", "x");
            var same = new AtlasSpriteIdentity("assets/a.png", "x");
            var other = new AtlasSpriteIdentity("Assets/A.png", "y");

            Assert.IsTrue(AtlasSpriteIdentity.Comparer.Instance.Equals(left, same));
            Assert.IsFalse(AtlasSpriteIdentity.Comparer.Instance.Equals(left, other));
            Assert.AreEqual(
                AtlasSpriteIdentity.Comparer.Instance.GetHashCode(left),
                AtlasSpriteIdentity.Comparer.Instance.GetHashCode(same));
        }
    }
}
