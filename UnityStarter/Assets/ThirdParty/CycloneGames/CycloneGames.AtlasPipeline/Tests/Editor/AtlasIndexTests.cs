using System.Collections.Generic;
using NUnit.Framework;
using CycloneGames.AtlasPipeline.Pure;

namespace CycloneGames.AtlasPipeline.Tests
{
    /// <summary>
    /// The determinism contract. Atlas generation order decides the packable order inside every
    /// .spriteatlasv2, so any dependence on insertion order, dictionary enumeration order or path
    /// casing makes the same project produce different atlases on different machines.
    /// </summary>
    [TestFixture]
    public sealed class AtlasIndexTests
    {
        // Content-hash goldens recorded after ComputeContentHash gained the global-fingerprint
        // input: padding, rotation/dilation defaults, tight packing and block offset feed every
        // packed result, and leaving them out let the regeneration skip fire after a global
        // settings edit. A deliberate algorithm change, not drift.
        private const long ExpectedPathHash = -1078049228056889214L;
        private const long ExpectedContentHash1234x777 = -6770390847638341131L;
        private const long ExpectedContentHash5678x777 = 7353532761110073307L;

        private static readonly string[] Members =
        {
            "Assets/UI/zeta.png",
            "Assets/UI/alpha.png",
            "Assets/UI/mid/beta.png",
            "Assets/UI/10.png",
            "Assets/UI/2.png",
        };

        private static AtlasIndex BuildForward()
        {
            var index = new AtlasIndex();
            for (int i = 0; i < Members.Length; i++)
            {
                index.Add(Members[i], "UI", markDirty: false);
            }

            return index;
        }

        private static AtlasIndex BuildReversed()
        {
            var index = new AtlasIndex();
            for (int i = Members.Length - 1; i >= 0; i--)
            {
                index.Add(Members[i], "UI", markDirty: false);
            }

            return index;
        }

        [Test]
        public void PathHash_MatchesRecordedValue()
        {
            Assert.AreEqual(ExpectedPathHash, BuildForward().GetBuckets()[0].GetPathHash());
        }

        [Test]
        public void ContentHash_IncludesTheRuleAndGlobalFingerprints()
        {
            AtlasBucket bucket = BuildForward().GetBuckets()[0];
            Assert.AreEqual(
                ExpectedContentHash1234x777,
                bucket.ComputeContentHash(1234, 777));
            Assert.AreEqual(
                ExpectedContentHash5678x777,
                bucket.ComputeContentHash(5678, 777));
            Assert.AreNotEqual(
                bucket.ComputeContentHash(1234, 777),
                bucket.ComputeContentHash(5678, 777));

            // The global fingerprint is a first-class input: padding, rotation and dilation
            // defaults, tight packing and block offset all change the packed result for every
            // atlas. Leaving it out let the regeneration skip fire after a global settings edit —
            // the change was marked dirty, then skipped anyway.
            Assert.AreNotEqual(
                bucket.ComputeContentHash(1234, 777),
                bucket.ComputeContentHash(1234, 778));
        }

        [Test]
        public void OrderedMembers_AreIndependentOfInsertionOrder()
        {
            IReadOnlyList<string> forward = BuildForward().GetBuckets()[0].GetOrdered();
            IReadOnlyList<string> reversed = BuildReversed().GetBuckets()[0].GetOrdered();

            Assert.AreEqual(forward.Count, reversed.Count);
            for (int i = 0; i < forward.Count; i++)
            {
                Assert.AreEqual(forward[i], reversed[i], "index " + i);
            }
        }

        [Test]
        public void Fingerprint_IsIndependentOfInsertionOrder()
        {
            Assert.AreEqual(
                BuildForward().GetBuckets()[0].GetPathHash(),
                BuildReversed().GetBuckets()[0].GetPathHash());
        }

        /// <summary>
        /// A path reported with different casing is one asset to Unity, but the set still has to store
        /// one canonical spelling. Whichever machine imported it first must not decide the spelling,
        /// because the spelling is what gets sorted and written into the atlas.
        /// </summary>
        [Test]
        public void MemberSpelling_ConvergesRegardlessOfInsertionOrder()
        {
            var lowerFirst = new AtlasIndex();
            lowerFirst.Add("Assets/UI/alpha.png", "UI", false);
            lowerFirst.Add("Assets/UI/Alpha.png", "UI", false);

            var upperFirst = new AtlasIndex();
            upperFirst.Add("Assets/UI/Alpha.png", "UI", false);
            upperFirst.Add("Assets/UI/alpha.png", "UI", false);

            Assert.AreEqual(1, lowerFirst.AssetCount);
            Assert.AreEqual(1, upperFirst.AssetCount);
            Assert.AreEqual(
                lowerFirst.GetBuckets()[0].GetOrdered()[0],
                upperFirst.GetBuckets()[0].GetOrdered()[0]);
            Assert.AreEqual(
                lowerFirst.GetBuckets()[0].GetPathHash(),
                upperFirst.GetBuckets()[0].GetPathHash());
            Assert.AreEqual(
                "Assets/UI/Alpha.png",
                lowerFirst.GetBuckets()[0].GetOrdered()[0],
                "the ordinally smallest spelling wins");
        }

        /// <summary>
        /// The atlas key becomes the generated file name, so it needs the same canonicalization.
        /// </summary>
        [Test]
        public void AtlasKeySpelling_ConvergesRegardlessOfInsertionOrder()
        {
            var lowerFirst = new AtlasIndex();
            lowerFirst.GetOrCreateBucket("ui_icon");
            lowerFirst.GetOrCreateBucket("UI_icon");

            var upperFirst = new AtlasIndex();
            upperFirst.GetOrCreateBucket("UI_icon");
            upperFirst.GetOrCreateBucket("ui_icon");

            Assert.AreEqual(1, lowerFirst.BucketCount, "both spellings are one bucket");
            Assert.AreEqual(1, upperFirst.BucketCount);
            Assert.AreEqual("UI_icon", lowerFirst.GetBuckets()[0].Key);
            Assert.AreEqual("UI_icon", upperFirst.GetBuckets()[0].Key);
        }

        [Test]
        public void TakeDirtyKeys_ReturnsThemInOrdinalOrder()
        {
            var index = new AtlasIndex();
            index.Add("Assets/UI/z.png", "Zebra", true);
            index.Add("Assets/UI/a.png", "Apple", true);
            index.Add("Assets/UI/m.png", "Mango", true);

            var taken = new List<string>();
            Assert.AreEqual(3, index.TakeDirtyKeys(taken));

            Assert.AreEqual(new[] { "Apple", "Mango", "Zebra" }, taken);
            Assert.AreEqual(0, index.DirtyCount);
        }

        [Test]
        public void TakeDirtyKeys_ToleratesANullBuffer()
        {
            var index = new AtlasIndex();
            index.Add("Assets/UI/a.png", "A", true);

            Assert.AreEqual(0, index.TakeDirtyKeys(null));
            Assert.AreEqual(1, index.DirtyCount, "a null buffer must not consume the dirty set");
        }

        [Test]
        public void RepointingAnAsset_MovesItAndDirtiesTheDestination()
        {
            var index = new AtlasIndex();
            index.Add("Assets/UI/x.png", "A", false);

            Assert.AreEqual(AtlasIndexChange.Moved, index.Add("Assets/UI/x.png", "B", true));
            Assert.IsTrue(index.IsDirty("B"));
            Assert.AreEqual(2, index.BucketCount, "source and destination both exist");

            IReadOnlyList<AtlasBucket> buckets = index.GetBuckets();
            Assert.AreEqual("A", buckets[0].Key);
            Assert.AreEqual(0, buckets[0].Count);
            Assert.AreEqual("B", buckets[1].Key);
            Assert.AreEqual(1, buckets[1].Count);
        }

        [Test]
        public void RemovingAMember_ChangesTheFingerprint()
        {
            AtlasIndex index = BuildForward();
            long before = index.GetBuckets()[0].GetPathHash();

            Assert.IsTrue(index.Remove("Assets/UI/alpha.png", false, out string affected));
            Assert.AreEqual("UI", affected);
            Assert.AreNotEqual(before, index.GetBuckets()[0].GetPathHash());
        }

        /// <summary>
        /// The case every "I added art and forgot to regenerate" story comes down to. A new sprite
        /// in a rule's folder joins that atlas's member list, the content fingerprint moves, and the
        /// committed manifest no longer matches — which is the only reason a CI job with no atlases
        /// of its own can tell that the developer's atlases are behind.
        /// </summary>
        [Test]
        public void AddingAMember_ChangesTheFingerprint()
        {
            AtlasIndex index = BuildForward();
            AtlasBucket bucket = index.GetBuckets()[0];

            long before = bucket.GetPathHash();
            long contentBefore = bucket.ComputeContentHash(1234, 777);

            index.Add("Assets/UI/newly_added.png", "UI", markDirty: false);
            AtlasBucket after = index.GetBuckets()[0];

            Assert.AreNotEqual(before, after.GetPathHash(), "membership must move the fingerprint");
            Assert.AreNotEqual(
                contentBefore,
                after.ComputeContentHash(1234, 777),
                "and the content hash the manifest records, which is what drift compares");
            Assert.AreEqual(Members.Length + 1, after.Count);
        }

        /// <summary>
        /// The fingerprint is a pure function of the member list, so undoing the change restores it
        /// exactly. Without this, a fingerprint that drifted on its own would show up as permanent,
        /// unexplainable drift in CI.
        /// </summary>
        [Test]
        public void AddingThenRemovingAMember_RestoresTheFingerprint()
        {
            AtlasIndex index = BuildForward();
            long before = index.GetBuckets()[0].GetPathHash();

            index.Add("Assets/UI/transient.png", "UI", markDirty: false);
            Assert.AreNotEqual(before, index.GetBuckets()[0].GetPathHash());

            index.Remove("Assets/UI/transient.png", false, out _);
            Assert.AreEqual(before, index.GetBuckets()[0].GetPathHash());
        }

        /// <summary>
        /// A brand new rule folder produces a brand new atlas key, which the comparer reports as
        /// Added rather than Changed. Both routes have to end in drift, or a newly added source
        /// folder would slip through a gate that only watched for changed members.
        /// </summary>
        [Test]
        public void ANewAtlasKeyIsReportedAsAddedNotChanged()
        {
            var before = new AtlasManifest(
                2, "gen", 1L,
                new List<AtlasManifestEntry>
                {
                    new AtlasManifestEntry("UI", "p/ui", 5, 10L, 1, 0),
                },
                new Dictionary<string, long>());

            var after = new AtlasManifest(
                2, "gen", 1L,
                new List<AtlasManifestEntry>
                {
                    new AtlasManifestEntry("UI", "p/ui", 5, 10L, 1, 0),
                    new AtlasManifestEntry("HUD", "p/hud", 3, 20L, 1, 0),
                },
                new Dictionary<string, long>());

            AtlasManifestDelta delta = AtlasManifestComparer.Compare(before, after);

            Assert.AreEqual(new[] { "HUD" }, delta.Added);
            Assert.IsEmpty(delta.Changed, "the untouched atlas must not be reported");
            Assert.IsFalse(delta.IsUpToDate);
        }

        [Test]
        public void RemoveEmptyBuckets_ReportsKeysAndToleratesANullCollector()
        {
            var index = new AtlasIndex();
            index.Add("Assets/UI/a.png", "Keep", false);
            index.Add("Assets/UI/b.png", "Drop", false);
            index.Remove("Assets/UI/b.png", false, out _);

            var removed = new List<string>();
            Assert.AreEqual(1, index.RemoveEmptyBuckets(removed));
            Assert.AreEqual(new[] { "Drop" }, removed);
            Assert.AreEqual(1, index.BucketCount);

            index.Remove("Assets/UI/a.png", false, out _);
            Assert.AreEqual(1, index.RemoveEmptyBuckets(null));
            Assert.AreEqual(0, index.BucketCount);
        }

        /// <summary>
        /// The ordered list instance is deliberately reused across calls — that is the zero-GC path.
        /// What changes on a membership edit is the contents, not the instance.
        /// </summary>
        [Test]
        public void OrderedMemberList_IsReusedUntilMembershipChanges()
        {
            AtlasIndex index = BuildForward();
            AtlasBucket bucket = index.GetBuckets()[0];

            IReadOnlyList<string> first = bucket.GetOrdered();
            IReadOnlyList<string> second = bucket.GetOrdered();
            Assert.AreSame(first, second, "the cached list is reused - this is the zero-GC path");

            int beforeCount = first.Count;
            bucket.Add("Assets/UI/added.png", out _);
            IReadOnlyList<string> third = bucket.GetOrdered();

            Assert.AreSame(first, third, "the list instance is reused, not reallocated");
            Assert.AreEqual(beforeCount + 1, third.Count);

            // Ordinal order: "10" < "2" < "added" < "alpha" < "mid" < "zeta".
            Assert.AreEqual("Assets/UI/10.png", third[0]);
            Assert.AreEqual("Assets/UI/added.png", third[2], "the new member is sorted into place");
            Assert.AreEqual("Assets/UI/alpha.png", third[3], "existing members are kept");
        }

        /// <summary>
        /// Rule ids are indices into the resolved rule list, so they must be dropped when that list is
        /// rebuilt. Keeping a stale id would resolve the wrong rule and write the wrong packing
        /// configuration into an atlas.
        /// </summary>
        [Test]
        public void ResetRuleIds_ClearsEveryBucket()
        {
            var index = new AtlasIndex();
            index.Add("Assets/UI/a.png", "UI", false);
            index.Add("Assets/UI/b.png", "Other", false);

            index.GetBuckets()[0].RuleId = 3;
            index.GetBuckets()[1].RuleId = 5;
            index.ResetRuleIds();

            Assert.AreEqual(-1, index.GetBuckets()[0].RuleId);
            Assert.AreEqual(-1, index.GetBuckets()[1].RuleId);
        }

        /// <summary>
        /// Two paths differing only by case are one file on Windows and on default macOS volumes, but
        /// two files on Linux and on case-sensitive macOS volumes. Such a project checks out
        /// differently per developer and generates different atlases, so it has to be detectable.
        /// </summary>
        [Test]
        public void CaseOnlyVariants_AreCountedAndSampled()
        {
            var index = new AtlasIndex();
            index.Add("Assets/UI/alpha.png", "UI", false);
            index.Add("Assets/UI/Alpha.png", "UI", false);

            Assert.AreEqual(1, index.CaseVariantCount);
            Assert.AreEqual(1, index.CaseVariantSamples.Count);
            Assert.AreEqual("Assets/UI/alpha.png -> Assets/UI/Alpha.png", index.CaseVariantSamples[0]);
        }

        [Test]
        public void CaseOnlyVariants_AreNotCountedForOrdinaryAdds()
        {
            var index = new AtlasIndex();
            index.Add("Assets/UI/a.png", "UI", false);
            index.Add("Assets/UI/b.png", "UI", false);
            index.Add("Assets/UI/a.png", "UI", false);

            Assert.AreEqual(0, index.CaseVariantCount);
            Assert.IsEmpty(index.CaseVariantSamples);
        }

        [Test]
        public void CaseVariantSample_IsBounded()
        {
            var index = new AtlasIndex();
            for (int i = 0; i < 40; i++)
            {
                index.Add("Assets/UI/lower" + i + ".png", "UI", false);
                index.Add("Assets/UI/LOWER" + i + ".png", "UI", false);
            }

            Assert.AreEqual(40, index.CaseVariantCount, "the count stays exact");
            Assert.LessOrEqual(index.CaseVariantSamples.Count, 8, "the sample list is capped");
        }

        [Test]
        public void CaseVariantCounters_AreResetWithTheIndex()
        {
            var index = new AtlasIndex();
            index.Add("Assets/UI/a.png", "UI", false);
            index.Add("Assets/UI/A.png", "UI", false);
            Assert.AreEqual(1, index.CaseVariantCount);

            index.Clear();
            Assert.AreEqual(0, index.CaseVariantCount);
            Assert.IsEmpty(index.CaseVariantSamples);

            index.Add("Assets/UI/a.png", "UI", false);
            index.Add("Assets/UI/A.png", "UI", false);
            index.ClearMembership();
            Assert.AreEqual(0, index.CaseVariantCount, "a rescan must not accumulate stale counts");
        }

        [Test]
        public void ClearMembership_KeepsQueuedDirtyWork()
        {
            var index = new AtlasIndex();
            index.Add("Assets/UI/a.png", "UI", true);
            index.ClearMembership();

            Assert.AreEqual(0, index.BucketCount);
            Assert.AreEqual(1, index.DirtyCount, "queued regeneration must survive a rescan");
        }
    }
}
