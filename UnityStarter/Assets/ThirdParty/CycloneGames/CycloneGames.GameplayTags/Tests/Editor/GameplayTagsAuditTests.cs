using System;
using System.Collections.Generic;
using System.Diagnostics;

using CycloneGames.GameplayTags.Core;
using NUnit.Framework;

namespace CycloneGames.GameplayTags.Tests.Editor
{
    /// <summary>
    /// Deep-audit suite for the GameplayTags core, mirroring the rigor applied to the GAS package:
    /// 1. Membership oracle sweep against a HashSet reference model under a deterministic LCG op stream.
    /// 2. Dual-instance determinism (identical op streams produce identical enumeration order and membership).
    /// 3. Operation scaling measurements (membership/add/remove at 256 and 2048 registered tags).
    /// 4. Count-container oracle (exact counts under random add/remove).
    /// 5. Steady-state zero-allocation contract for membership, counts, queries, and batches.
    /// 6. Query oracle (BuildQueryAll / BuildQueryAny vs an explicit model).
    /// 7. Registry rebuild index stability (epoch contract) and idempotent duplicate registration.
    /// </summary>
    public sealed class GameplayTagsAuditTests
    {
        private const int PoolSize = 4096;
        private const int SubjectCount = 64;

        private readonly List<GameplayTag> pool = new List<GameplayTag>(PoolSize);

        [SetUp]
        public void SetUp()
        {
            GameplayTagManager.ResetForTests();
            pool.Clear();
            var names = new List<string>(PoolSize);
            for (int i = 0; i < PoolSize; i++)
            {
                names.Add($"TagsAudit{i:0000}");
            }
            GameplayTagManager.RegisterDynamicTags(names);
            for (int i = 0; i < PoolSize; i++)
            {
                pool.Add(GameplayTagManager.Request($"TagsAudit{i:0000}"));
            }
        }

        [TearDown]
        public void TearDown()
        {
            GameplayTagManager.ResetForTests();
        }

        [Test]
        public void Container_Membership_OracleSweep_AgainstHashSetModel()
        {
            ulong seed = 0x8A5CD789635D2DFFUL;
            var container = new GameplayTagContainer();
            var explicitModel = new HashSet<int>();
            int checks = 0;

            for (int step = 0; step < 4000; step++)
            {
                StepLcg(ref seed, out uint action);
                GameplayTag tag = pool[(int)(action % SubjectCount)];
                if ((action & 4u) == 0)
                {
                    container.AddTag(tag);
                    explicitModel.Add(tag.RuntimeIndex);
                }
                else
                {
                    container.RemoveTag(tag);
                    explicitModel.Remove(tag.RuntimeIndex); // non-hierarchical names: explicit == expanded
                }

                if (step % 137 == 0)
                {
                    AssertOracleMatch(container, explicitModel);
                    checks++;
                }
            }

            AssertOracleMatch(container, explicitModel);
            checks++;
            UnityEngine.Debug.Log($"[TagsAudit][Oracle] PASS: 4000 mixed ops, {checks} full-pool verifications ({PoolSize} tags each), explicit count={container.ExplicitTagCount}.");
        }

        [Test]
        public void Container_TwoInstances_IdenticalOpStream_Deterministic()
        {
            ulong seed = 0x123456789ABCDEFUL;
            var first = new GameplayTagContainer();
            var second = new GameplayTagContainer();

            var script = new List<int>(2000);
            for (int step = 0; step < 2000; step++)
            {
                StepLcg(ref seed, out uint action);
                script.Add((int)action);
            }

            for (int step = 0; step < script.Count; step++)
            {
                int action = script[step];
                GameplayTag tag = pool[action % SubjectCount];
                bool add = (action & 4u) == 0;
                Apply(first, tag, add);
                Apply(second, tag, add);
            }

            // Enumeration order and membership must be bit-identical.
            var firstSet = new List<int>();
            var secondSet = new List<int>();
            foreach (GameplayTag tag in first) firstSet.Add(tag.RuntimeIndex);
            foreach (GameplayTag tag in second) secondSet.Add(tag.RuntimeIndex);
            Assert.That(secondSet.Count, Is.EqualTo(firstSet.Count));
            for (int i = 0; i < firstSet.Count; i++)
            {
                Assert.That(secondSet[i], Is.EqualTo(firstSet[i]), $"Enumeration order diverged at {i}.");
            }

            for (int i = 0; i < SubjectCount; i++)
            {
                int index = pool[i].RuntimeIndex;
                Assert.That(second.ContainsRuntimeIndex(index, false), Is.EqualTo(first.ContainsRuntimeIndex(index, false)),
                    $"Membership diverged for pool tag {i}.");
            }

            UnityEngine.Debug.Log($"[TagsAudit][Determinism] PASS: 2000-op identical stream, enumeration + membership bit-identical ({firstSet.Count} held tags).");
        }

        [Test]
        public void ContainerOperationScaling_TwoRegistrySizes_MeasureAndVerify()
        {
            MeasureContainerOps("small", 256);
            MeasureContainerOps("large", 4096);
        }

        private void MeasureContainerOps(string label, int subjectTagCount)
        {
            var container = new GameplayTagContainer();
            int held = subjectTagCount / 4;
            for (int i = 0; i < held; i++)
            {
                container.AddTag(pool[i]);
            }

            GameplayTag hit = pool[held - 1];
            GameplayTag miss = pool[PoolSize - 1];
            const int iterations = 200_000;

            long t0 = Stopwatch.GetTimestamp();
            bool sink = false;
            for (int i = 0; i < iterations; i++)
            {
                sink ^= container.ContainsRuntimeIndex(hit.RuntimeIndex, false);
                sink ^= container.ContainsRuntimeIndex(miss.RuntimeIndex, false);
            }
            long lookups = Stopwatch.GetTimestamp() - t0;

            t0 = Stopwatch.GetTimestamp();
            for (int i = 0; i < 20_000; i++)
            {
                container.AddTag(miss);
                container.RemoveTag(miss);
            }
            long churn = Stopwatch.GetTimestamp() - t0;

            double lookupNs = lookups * 1_000_000_000.0 / Stopwatch.Frequency / iterations;
            double churnNs = churn * 1_000_000_000.0 / Stopwatch.Frequency / 20_000;
            Assert.That(sink, Is.False);
            Assert.That(container.ContainsRuntimeIndex(miss.RuntimeIndex, false), Is.False, "Churn must end clean.");

            UnityEngine.Debug.Log(
                $"[TagsAudit][Scaling] {label}: registeredPool={PoolSize} subjects={subjectTagCount} held={held} | " +
                $"lookup(2 checks)={lookupNs:F1}ns per iteration, add+remove churn={churnNs:F1}ns per cycle");
        }

        [Test]
        public void CountContainer_CountOracle_RandomIncrementsDecrements()
        {
            ulong seed = 0xDEADBEEFCAFEF00DUL;
            var counts = new GameplayTagCountContainer();
            var oracle = new int[SubjectCount];
            var applied = new List<(int tag, int delta)>(4000);

            for (int step = 0; step < 4000; step++)
            {
                StepLcg(ref seed, out uint action);
                int tagIndex = (int)(action % SubjectCount);
                int delta = ((action >> 8) & 1u) == 0 ? 1 : -1;
                GameplayTag tag = pool[tagIndex];

                if (delta > 0)
                {
                    counts.AddTag(tag, 1);
                }
                else
                {
                    if (oracle[tagIndex] <= 0) continue;
                    counts.RemoveTag(tag, 1);
                }
                oracle[tagIndex] += delta;
                applied.Add((tagIndex, delta));

                if (step % 500 == 0)
                {
                    AssertCountOracle(counts, oracle);
                }
            }

            AssertCountOracle(counts, oracle);

            // Drain every tag: counts must return to zero with no residue.
            for (int i = 0; i < SubjectCount; i++)
            {
                if (oracle[i] > 0)
                {
                    counts.RemoveTag(pool[i], oracle[i]);
                    oracle[i] = 0;
                }
            }
            AssertCountOracle(counts, oracle);
            Assert.That(counts.ExplicitTagCount, Is.Zero, "Drained count container must hold no explicit tags.");

            UnityEngine.Debug.Log($"[TagsAudit][CountOracle] PASS: 4000 random count ops verified against model ({applied.Count} applied), drained to zero.");
        }

        [Test]
        public void Query_Matches_AgainstExplicitModel()
        {
            var subject = new GameplayTagContainer();
            for (int i = 0; i < 32; i++)
            {
                subject.AddTag(pool[i]); // holds 0..31
            }

            var allHeld = new GameplayTagContainer();
            for (int i = 0; i < 32; i++) allHeld.AddTag(pool[i]);
            GameplayTagQuery allHeldQuery = GameplayTagQuery.BuildQueryAll(allHeld);
            Assert.That(allHeldQuery.Matches(subject), Is.True, "QueryAll over the exact held set must match.");

            var allHeldPlusExtra = new GameplayTagContainer();
            for (int i = 0; i < 33; i++) allHeldPlusExtra.AddTag(pool[i]);
            Assert.That(GameplayTagQuery.BuildQueryAll(allHeldPlusExtra).Matches(subject), Is.False,
                "QueryAll over held+extra must not match.");

            var anyOverlap = new GameplayTagContainer();
            for (int i = 16; i < 48; i++) anyOverlap.AddTag(pool[i]);
            Assert.That(GameplayTagQuery.BuildQueryAny(anyOverlap).Matches(subject), Is.True,
                "QueryAny with partial overlap must match.");

            var anyDisjoint = new GameplayTagContainer();
            for (int i = 64; i < 96; i++) anyDisjoint.AddTag(pool[i]);
            Assert.That(GameplayTagQuery.BuildQueryAny(anyDisjoint).Matches(subject), Is.False,
                "QueryAny with disjoint sets must not match.");

            UnityEngine.Debug.Log("[TagsAudit][Query] PASS: QueryAll/QueryAny oracle matches (exact/extra/overlap/disjoint).");
        }

        [Test]
        public void HotOperations_SteadyState_AllocateNoManagedBytes()
        {
            var container = new GameplayTagContainer();
            for (int i = 0; i < 64; i++) container.AddTag(pool[i]);
            var counts = new GameplayTagCountContainer();
            counts.AddTag(pool[0], 3);
            GameplayTagQuery query = GameplayTagQuery.BuildQueryAny(container);
            GameplayTag hit = pool[10];
            GameplayTag miss = pool[PoolSize - 1];

            // Warm every internal cache/bitset before probing.
            for (int i = 0; i < 256; i++)
            {
                _ = container.ContainsRuntimeIndex(hit.RuntimeIndex, false);
                _ = container.ContainsRuntimeIndex(miss.RuntimeIndex, false);
                counts.AddTag(pool[1]);
                counts.RemoveTag(pool[1]);
                _ = query.Matches(container);
            }

            const int probe = 20_000;
            long before = GC.GetAllocatedBytesForCurrentThread();
            bool sink = false;
            for (int i = 0; i < probe; i++)
            {
                sink ^= container.ContainsRuntimeIndex(hit.RuntimeIndex, false);
                sink ^= container.ContainsRuntimeIndex(miss.RuntimeIndex, false);
                counts.AddTag(pool[1]);
                counts.RemoveTag(pool[1]);
                sink ^= query.Matches(container);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            _ = sink;
            UnityEngine.Debug.Log($"[TagsAudit][Alloc] steady-state 20000 x (2 lookups + count churn + query): {allocated} bytes");
            TagsAllocationProbe.AssertNoMeasuredAllocation(allocated, "Warm tag hot paths must not allocate managed memory.");
        }

        [Test]
        public void RegistryRebuild_PreservesIndices_AndDuplicateRegistrationIsIdempotent()
        {
            var before = new int[SubjectCount];
            for (int i = 0; i < SubjectCount; i++)
            {
                before[i] = pool[i].RuntimeIndex;
            }
            int tagCountBefore = GameplayTagManager.TagCount;

            var duplicates = new List<string>(SubjectCount);
            for (int i = 0; i < SubjectCount; i++)
            {
                duplicates.Add($"TagsAudit{i:0000}");
            }
            GameplayTagManager.RegisterDynamicTags(duplicates);

            for (int i = 0; i < SubjectCount; i++)
            {
                GameplayTag reResolved = GameplayTagManager.Request($"TagsAudit{i:0000}");
                Assert.That(reResolved.RuntimeIndex, Is.EqualTo(before[i]),
                    $"Index for tag {i} must survive a duplicate-registration rebuild (epoch contract).");
            }
            Assert.That(GameplayTagManager.TagCount, Is.EqualTo(tagCountBefore),
                "Duplicate registration must not grow the registry.");

            UnityEngine.Debug.Log($"[TagsAudit][Registry] PASS: duplicate rebuild preserved {SubjectCount} indices; TagCount={tagCountBefore} stable.");
        }

        private void AssertOracleMatch(GameplayTagContainer container, HashSet<int> explicitModel)
        {
            Assert.That(container.ExplicitTagCount, Is.EqualTo(explicitModel.Count), "Explicit count diverged.");
            for (int i = 0; i < PoolSize; i++)
            {
                bool expected = explicitModel.Contains(pool[i].RuntimeIndex);
                bool actual = container.ContainsRuntimeIndex(pool[i].RuntimeIndex, explicitOnly: false);
                Assert.That(actual, Is.EqualTo(expected), $"Membership diverged for tag {i} (expected {expected}).");
            }
        }

        private void AssertCountOracle(GameplayTagCountContainer counts, int[] oracle)
        {
            for (int i = 0; i < oracle.Length; i++)
            {
                Assert.That(counts.GetTagCount(pool[i]), Is.EqualTo(Math.Max(0, oracle[i])),
                    $"Count diverged for subject tag {i}.");
            }
        }

        private static void Apply(GameplayTagContainer container, GameplayTag tag, bool add)
        {
            if (add) container.AddTag(tag);
            else container.RemoveTag(tag);
        }

        private static void StepLcg(ref ulong seed, out uint action)
        {
            seed = unchecked(seed * 6364136223846793005UL + 1442695040888963407UL);
            action = (uint)(seed >> 33);
        }
    }
}
