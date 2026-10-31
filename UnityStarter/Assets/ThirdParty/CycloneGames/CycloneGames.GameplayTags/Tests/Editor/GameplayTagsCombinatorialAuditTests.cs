using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CycloneGames.GameplayTags.Core;
using CycloneGames.GameplayTags.Unity.Runtime;
using NUnit.Framework;

namespace CycloneGames.GameplayTags.Tests.Editor
{
    /// <summary>
    /// Seeded combinatorial audit of the GameplayTags mutation surface, checked against reference models.
    /// </summary>
    [TestFixture]
    public sealed class GameplayTagsCombinatorialAuditTests
    {
        private bool m_PreviousThreadAffinity;

        [SetUp]
        public void SetUp()
        {
            m_PreviousThreadAffinity = GameplayTagsDiagnostics.ThreadAffinityChecksEnabled;
            GameplayTagsDiagnostics.ThreadAffinityChecksEnabled = false;
            GameplayTagRedirector.ClearAll();
            GameplayTagManager.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            GameplayTagsDiagnostics.ThreadAffinityChecksEnabled = m_PreviousThreadAffinity;
            GameplayTagRedirector.ClearAll();
            GameplayTagManager.ResetForTests();
        }

        // ---------------------------------------------------------------- fixtures

        private sealed class CombinatorialSource : IGameplayTagSource
        {
            private readonly List<string> m_Names = new();

            internal CombinatorialSource(string name, params string[] names)
            {
                Name = name;
                for (int i = 0; i < names.Length; i++)
                    m_Names.Add(names[i]);
            }

            public string Name { get; }

            internal void Add(string tagName) => m_Names.Add(tagName);

            public void RegisterTags(GameplayTagRegistrationContext context)
            {
                for (int i = 0; i < m_Names.Count && !context.IsRegistrationTerminated; i++)
                    context.RegisterTag(m_Names[i]);
            }
        }

        private static GameplayTagRegistry BuildRegistry(out CombinatorialSource source, params string[] names)
        {
            source = new CombinatorialSource("combinatorial", names);
            return new GameplayTagRegistryBuilder().AddSource(source).Build();
        }

        /// <summary>Names shaped so every level of the hierarchy has realistic width and depth.</summary>
        private static string[] HierarchyNames(int roots, int children, int grandchildren)
        {
            var names = new List<string>(roots * (1 + children * (1 + grandchildren)));
            for (int r = 0; r < roots; r++)
            {
                names.Add($"Comb.R{r}");
                for (int c = 0; c < children; c++)
                {
                    names.Add($"Comb.R{r}.C{c}");
                    for (int g = 0; g < grandchildren; g++)
                        names.Add($"Comb.R{r}.C{c}.G{g}");
                }
            }

            return names.ToArray();
        }

        private static void CollectChain(TagDataSnapshot snapshot, int runtimeIndex, List<int> destination)
        {
            int start = snapshot.HierarchyOffsets[runtimeIndex];
            int end = snapshot.HierarchyOffsets[runtimeIndex + 1];
            for (int i = start; i < end; i++)
                destination.Add(snapshot.HierarchyPool[i]);
        }

        private static HashSet<int> Expand(TagDataSnapshot snapshot, IEnumerable<int> explicitIndices)
        {
            var expanded = new HashSet<int>();
            var chain = new List<int>(8);
            foreach (int runtimeIndex in explicitIndices)
            {
                chain.Clear();
                CollectChain(snapshot, runtimeIndex, chain);
                for (int i = 0; i < chain.Count; i++)
                    expanded.Add(chain[i]);
            }

            return expanded;
        }

        private static Dictionary<int, int> ExpandCounts(TagDataSnapshot snapshot, IReadOnlyDictionary<int, int> explicitCounts)
        {
            var expanded = new Dictionary<int, int>();
            var chain = new List<int>(8);
            foreach (KeyValuePair<int, int> pair in explicitCounts)
            {
                chain.Clear();
                CollectChain(snapshot, pair.Key, chain);
                for (int i = 0; i < chain.Count; i++)
                {
                    int index = chain[i];
                    expanded.TryGetValue(index, out int running);
                    expanded[index] = running + pair.Value;
                }
            }

            return expanded;
        }

        private static HashSet<int> ReadExplicit(GameplayTagContainer container)
        {
            var set = new HashSet<int>();
            foreach (GameplayTag tag in container.GetExplicitTags())
                set.Add(tag.RuntimeIndex);
            return set;
        }

        private static HashSet<int> ReadExpanded(GameplayTagContainer container)
        {
            var set = new HashSet<int>();
            foreach (GameplayTag tag in container.GetTags())
                set.Add(tag.RuntimeIndex);
            return set;
        }

        private static string Join(HashSet<int> values)
        {
            var ordered = new List<int>(values);
            ordered.Sort();
            return string.Join(",", ordered);
        }

        private static void AssertContainerMatches(
            GameplayTagContainer container,
            TagDataSnapshot snapshot,
            HashSet<int> explicitModel,
            string context)
        {
            HashSet<int> explicitActual = ReadExplicit(container);
            HashSet<int> expandedActual = ReadExpanded(container);
            HashSet<int> expandedModel = Expand(snapshot, explicitModel);

            if (!explicitActual.SetEquals(explicitModel))
            {
                Assert.Fail($"{context}: explicit set diverged. expected [{Join(explicitModel)}] actual [{Join(explicitActual)}]");
            }

            if (!expandedActual.SetEquals(expandedModel))
            {
                Assert.Fail($"{context}: expanded set diverged. expected [{Join(expandedModel)}] actual [{Join(expandedActual)}]");
            }

            if (container.ExplicitTagCount != explicitModel.Count)
            {
                Assert.Fail($"{context}: ExplicitTagCount {container.ExplicitTagCount} != {explicitModel.Count}");
            }

            if (container.TagCount != expandedModel.Count)
            {
                Assert.Fail($"{context}: TagCount {container.TagCount} != {expandedModel.Count}");
            }

            int explicitMismatch = -1;
            int expandedMismatch = -1;
            for (int index = 0; index < snapshot.TotalTagCount; index++)
            {
                if (explicitMismatch < 0 &&
                    container.ContainsRuntimeIndex(index, explicitOnly: true) != explicitModel.Contains(index))
                {
                    explicitMismatch = index;
                }

                if (expandedMismatch < 0 &&
                    container.ContainsRuntimeIndex(index, explicitOnly: false) != expandedModel.Contains(index))
                {
                    expandedMismatch = index;
                }
            }

            if (explicitMismatch >= 0)
                Assert.Fail($"{context}: ContainsRuntimeIndex({explicitMismatch}, explicit) diverged");

            if (expandedMismatch >= 0)
                Assert.Fail($"{context}: ContainsRuntimeIndex({expandedMismatch}, expanded) diverged");
        }

        // ------------------------------------------------------------ stale sources

        /// <summary>
        /// Builds a source from the epoch before a non-preserving reload and asserts the fixture really
        /// reassigns its index.
        /// </summary>
        private static GameplayTagContainer BuildStaleSource(
            out GameplayTagRegistry registry,
            out int staleRuntimeIndex,
            out string originalName,
            out string remappedName)
        {
            registry = BuildRegistry(out CombinatorialSource source, "Stale.A", "Stale.B");
            GameplayTag original = registry.Request("Stale.A", logWarningIfNotFound: false);
            Assert.That(original.IsNone, Is.False, "fixture tag must resolve");

            var stale = new GameplayTagContainer(registry);
            stale.AddTag(original);
            staleRuntimeIndex = stale.GetExplicitTag(0).RuntimeIndex;
            originalName = registry.GetName(original);

            // "Stale.0First" sorts before "Stale.A" in ordinal order, so the reload shifts every index.
            source.Add("Stale.0First");
            int epochBefore = registry.RuntimeIndexEpoch;
            registry.Reload(preserveRuntimeIndices: false);

            Assert.That(registry.RuntimeIndexEpoch, Is.Not.EqualTo(epochBefore),
                "fixture: a non-preserving reload must advance the runtime index epoch");
            remappedName = registry.GetName(new GameplayTag(staleRuntimeIndex));
            Assert.That(remappedName, Is.Not.EqualTo(originalName),
                "fixture: the reload must genuinely reassign the stale index, otherwise the case is vacuous");
            return stale;
        }

        /// <summary>True when the call was refused. A stale source may be refused by the epoch guard or the registry guard.</summary>
        private static bool Rejected(Action action)
        {
            try
            {
                action();
                return false;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException)
            {
                return true;
            }
        }

        [Test]
        public void StaleSource_AddTags_MustNotSilentlyRemapTheTag()
        {
            GameplayTagContainer stale = BuildStaleSource(out GameplayTagRegistry registry,
                out int staleIndex, out string originalName, out string remappedName);
            var target = new GameplayTagContainer(registry);

            if (Rejected(() => target.AddTags(stale)))
            {
                return; // refused: the contract held.
            }

            Assert.That(target.ExplicitTagCount, Is.EqualTo(1),
                "AddTags copied the stale source without rejecting it");

            string copiedName = registry.GetName(target.GetExplicitTag(0));
            TestContext.WriteLine(
                $"stale index {staleIndex}: '{originalName}' became '{remappedName}', copied name '{copiedName}'");

            Assert.That(copiedName, Is.EqualTo(originalName),
                $"AddTags silently remapped '{originalName}' to '{copiedName}' across a non-preserving reload");
        }

        [Test]
        public void StaleSource_Copy_MustNotSilentlyRemapTheTag()
        {
            GameplayTagContainer stale = BuildStaleSource(out GameplayTagRegistry registry,
                out _, out string originalName, out string remappedName);
            var target = new GameplayTagContainer(registry);

            if (Rejected(() => GameplayTagContainer.Copy(target, stale)))
            {
                return;
            }

            string copiedName = registry.GetName(target.GetExplicitTag(0));
            TestContext.WriteLine($"Copy: '{originalName}' became '{remappedName}', copied name '{copiedName}'");
            Assert.That(copiedName, Is.EqualTo(originalName),
                $"Copy silently remapped '{originalName}' to '{copiedName}' across a non-preserving reload");
        }

        [Test]
        public void StaleSource_AddUnion_MustNotSilentlyRemapTheTag()
        {
            GameplayTagContainer stale = BuildStaleSource(out GameplayTagRegistry registry,
                out _, out string originalName, out string remappedName);
            var empty = new GameplayTagContainer(registry);
            var target = new GameplayTagContainer(registry);

            if (Rejected(() => target.AddUnion(stale, empty)))
            {
                return;
            }

            string copiedName = registry.GetName(target.GetExplicitTag(0));
            TestContext.WriteLine($"AddUnion: '{originalName}' became '{remappedName}', copied name '{copiedName}'");
            Assert.That(copiedName, Is.EqualTo(originalName),
                $"AddUnion silently remapped '{originalName}' to '{copiedName}' across a non-preserving reload");
        }

        [Test]
        public void StaleSource_AddIntersection_MustNotSilentlyRemapTheTag()
        {
            GameplayTagContainer stale = BuildStaleSource(out GameplayTagRegistry registry,
                out _, out string originalName, out string remappedName);
            var target = new GameplayTagContainer(registry);

            if (Rejected(() => target.AddIntersection(stale, stale)))
            {
                return;
            }

            string copiedName = registry.GetName(target.GetExplicitTag(0));
            TestContext.WriteLine($"AddIntersection: '{originalName}' became '{remappedName}', copied name '{copiedName}'");
            Assert.That(copiedName, Is.EqualTo(originalName),
                $"AddIntersection silently remapped '{originalName}' to '{copiedName}' across a non-preserving reload");
        }

        [Test]
        public void StaleContainer_OwnMutation_IsRejectedAndLeavesStateUntouched()
        {
            GameplayTagContainer stale = BuildStaleSource(out GameplayTagRegistry registry,
                out _, out _, out _);
            HashSet<int> before = ReadExplicit(stale);

            Assert.That(() => stale.AddTag(registry.Request("Stale.0First", false)),
                Throws.TypeOf<InvalidOperationException>(),
                "a container holding a previous epoch must reject its own mutation");

            Assert.That(ReadExplicit(stale).SetEquals(before), Is.True,
                "a rejected mutation must not change the container");
        }

        // ---------------------------------------------------------- thread affinity

        [Test]
        public void Container_Clear_MustReleaseRecordedThreadAffinity()
        {
            GameplayTagRegistry registry = BuildRegistry(out _, "Affinity.A");
            GameplayTag tag = registry.Request("Affinity.A", logWarningIfNotFound: false);
            var container = new GameplayTagContainer(registry);

            GameplayTagsDiagnostics.ThreadAffinityChecksEnabled = true;
            try
            {
                Task.Run(() => container.AddTag(tag)).GetAwaiter().GetResult();
                Assert.That(() => container.AddTag(tag), Throws.TypeOf<InvalidOperationException>(),
                    "fixture: the container must be bound to the worker thread at this point");

                container.Clear();

                Assert.That(() => container.AddTag(tag), Throws.Nothing,
                    "Clear() returns the container to its empty state, so it must also release the " +
                    "recorded owner thread; otherwise a pooled container is bound to one thread forever");
            }
            finally
            {
                GameplayTagsDiagnostics.ThreadAffinityChecksEnabled = false;
            }
        }

        [Test]
        public void CountContainer_Clear_MustReleaseRecordedThreadAffinity()
        {
            GameplayTagRegistry registry = BuildRegistry(out _, "Affinity.B");
            GameplayTag tag = registry.Request("Affinity.B", logWarningIfNotFound: false);
            var container = new GameplayTagCountContainer(registry);

            GameplayTagsDiagnostics.ThreadAffinityChecksEnabled = true;
            try
            {
                Task.Run(() => container.AddTag(tag)).GetAwaiter().GetResult();
                Assert.That(() => container.AddTag(tag), Throws.TypeOf<InvalidOperationException>(),
                    "fixture: the container must be bound to the worker thread at this point");

                container.Clear();

                Assert.That(() => container.AddTag(tag), Throws.Nothing,
                    "Clear() must release the recorded owner thread for the count container as well");
            }
            finally
            {
                GameplayTagsDiagnostics.ThreadAffinityChecksEnabled = false;
            }
        }

        // ------------------------------------- reference model: count container mutations

        [Test]
        public void CountContainer_RandomizedBatches_MatchReferenceModel(
            [Values(1, 7, 12345, 987654)] int seed)
        {
            GameplayTagRegistry registry = BuildRegistry(out _, HierarchyNames(2, 3, 2));
            TagDataSnapshot snapshot = registry.Snapshot;
            var rng = new Random(seed);
            var container = new GameplayTagCountContainer(registry);

            var explicitModel = new Dictionary<int, int>();
            var notifications = new List<KeyValuePair<int, int>>();
            container.OnAnyTagCountChange += (tag, count) => notifications.Add(new KeyValuePair<int, int>(tag.RuntimeIndex, count));

            int applied = 0;
            int rejected = 0;
            int mutated = 0;

            for (int step = 0; step < 600; step++)
            {
                notifications.Clear();
                int index = 1 + rng.Next(snapshot.TotalTagCount - 1);
                int amount = rng.Next(1, 4);
                bool add = rng.Next(2) == 0;

                // Model: propose the delta, reject when any resulting count would be negative.
                var next = new Dictionary<int, int>(explicitModel);
                next.TryGetValue(index, out int current);
                int updated = add ? current + amount : current - amount;
                bool legal = updated >= 0;
                if (legal)
                {
                    if (updated == 0) next.Remove(index);
                    else next[index] = updated;
                }

                Dictionary<int, int> expandedNext = ExpandCounts(snapshot, next);
                if (legal)
                {
                    foreach (KeyValuePair<int, int> pair in expandedNext)
                    {
                        if (pair.Value < 0)
                        {
                            legal = false;
                            break;
                        }
                    }
                }

                if (legal)
                {
                    if (add) container.AddTag(new GameplayTag(index), amount);
                    else container.RemoveTag(new GameplayTag(index), amount);

                    explicitModel = next;
                    applied++;
                    if (updated != current) mutated++;
                }
                else
                {
                    HashSet<int> before = new(ReadExplicitIndices(container));
                    Assert.That(
                        () =>
                        {
                            if (add) container.AddTag(new GameplayTag(index), amount);
                            else container.RemoveTag(new GameplayTag(index), amount);
                        },
                        Throws.TypeOf<InvalidOperationException>(),
                        $"seed {seed} step {step}: an illegal {(add ? "add" : "remove")} of {amount} on index " +
                        $"{index} (model {current}) must be rejected");

                    Assert.That(ReadExplicitIndices(container).SetEquals(before), Is.True,
                        $"seed {seed} step {step}: a rejected mutation must leave the container untouched");
                    Assert.That(notifications.Count, Is.Zero,
                        $"seed {seed} step {step}: a rejected mutation must not notify");
                    rejected++;
                }

                Assert.That(container.ExplicitTagCount, Is.EqualTo(explicitModel.Count),
                    $"seed {seed} step {step}: explicit tag count diverged");

                int expandedExpected = 0;
                foreach (KeyValuePair<int, int> pair in expandedNext)
                    if (pair.Value > 0) expandedExpected++;
                Assert.That(container.TagCount, Is.EqualTo(expandedExpected),
                    $"seed {seed} step {step}: expanded tag count diverged");

                int explicitMismatch = -1;
                int totalMismatch = -1;
                for (int probe = 0; probe < snapshot.TotalTagCount; probe++)
                {
                    explicitModel.TryGetValue(probe, out int modelExplicit);
                    expandedNext.TryGetValue(probe, out int modelTotal);
                    if (modelTotal < 0) modelTotal = 0;

                    if (explicitMismatch < 0 && container.GetExplicitTagCount(new GameplayTag(probe)) != modelExplicit)
                        explicitMismatch = probe;
                    if (totalMismatch < 0 && container.GetTagCount(new GameplayTag(probe)) != modelTotal)
                        totalMismatch = probe;
                }

                Assert.That(explicitMismatch, Is.EqualTo(-1),
                    $"seed {seed} step {step}: GetExplicitTagCount diverged at index {explicitMismatch}");
                Assert.That(totalMismatch, Is.EqualTo(-1),
                    $"seed {seed} step {step}: GetTagCount diverged at index {totalMismatch}");

                if (legal)
                {
                    var expectedTouched = new HashSet<int>();
                    var chain = new List<int>(8);
                    CollectChain(snapshot, index, chain);
                    for (int i = 0; i < chain.Count; i++)
                        expectedTouched.Add(chain[i]);

                    Assert.That(notifications.Count, Is.EqualTo(expectedTouched.Count),
                        $"seed {seed} step {step}: expected one notification per touched index " +
                        $"({expectedTouched.Count}), got {notifications.Count}");

                    for (int i = 0; i < notifications.Count; i++)
                    {
                        Assert.That(expectedTouched.Contains(notifications[i].Key), Is.True,
                            $"seed {seed} step {step}: notification for untouched index {notifications[i].Key}");
                        expandedNext.TryGetValue(notifications[i].Key, out int modelCount);
                        Assert.That(notifications[i].Value, Is.EqualTo(modelCount),
                            $"seed {seed} step {step}: notification count for index {notifications[i].Key} diverged");
                    }
                }
            }

            TestContext.WriteLine(
                $"seed {seed}: applied {applied} (state changed {mutated}), rejected {rejected}, " +
                $"final explicit {container.ExplicitTagCount}, expanded {container.TagCount}");
        }

        private static HashSet<int> ReadExplicitIndices(GameplayTagCountContainer container)
        {
            var set = new HashSet<int>();
            for (int i = 0; i < container.ExplicitTagCount; i++)
                set.Add(container.GetExplicitTag(i).RuntimeIndex);
            return set;
        }

        // ------------------------------------------ reference model: container set operations

        [Test]
        public void Container_RandomizedSetOperations_MatchHashSetModel(
            [Values(3, 17, 2024, 555)] int seed)
        {
            GameplayTagRegistry registry = BuildRegistry(out _, HierarchyNames(4, 4, 3));
            TagDataSnapshot snapshot = registry.Snapshot;
            var rng = new Random(seed);

            var containers = new List<GameplayTagContainer>();
            var models = new List<HashSet<int>>();
            for (int i = 0; i < 4; i++)
            {
                containers.Add(new GameplayTagContainer(registry));
                models.Add(new HashSet<int>());
            }

            // Slot 0 starts with every tag so the bitset storage is exercised from the first step.
            for (int index = 1; index < snapshot.TotalTagCount; index++)
            {
                containers[0].AddTag(new GameplayTag(index));
                models[0].Add(index);
            }

            Assert.That(containers[0].UsesBitset, Is.True,
                "fixture: a container holding every tag must cross the bitset activation threshold");

            int bitsetOps = 0;

            for (int step = 0; step < 400; step++)
            {
                int targetSlot = rng.Next(containers.Count);
                GameplayTagContainer target = containers[targetSlot];
                HashSet<int> model = models[targetSlot];
                int index = 1 + rng.Next(snapshot.TotalTagCount - 1);

                switch (rng.Next(7))
                {
                    case 0:
                        target.AddTag(new GameplayTag(index));
                        model.Add(index);
                        break;

                    case 1:
                        target.RemoveTag(new GameplayTag(index));
                        model.Remove(index);
                        break;

                    case 2:
                    {
                        int donorSlot = (targetSlot + 1 + rng.Next(containers.Count - 1)) % containers.Count;
                        target.AddTags(containers[donorSlot]);
                        model.UnionWith(models[donorSlot]);
                        break;
                    }

                    case 3:
                    {
                        int donorSlot = (targetSlot + 1 + rng.Next(containers.Count - 1)) % containers.Count;
                        target.RemoveTags(containers[donorSlot]);
                        model.ExceptWith(models[donorSlot]);
                        break;
                    }

                    case 4:
                    {
                        int firstSlot = (targetSlot + 1 + rng.Next(containers.Count - 1)) % containers.Count;
                        int secondSlot = (targetSlot + 1 + rng.Next(containers.Count - 1)) % containers.Count;
                        var expected = new HashSet<int>(models[firstSlot]);
                        expected.UnionWith(models[secondSlot]);
                        target.AddUnion(containers[firstSlot], containers[secondSlot]);
                        model.Clear();
                        model.UnionWith(expected);
                        break;
                    }

                    case 5:
                    {
                        int firstSlot = (targetSlot + 1 + rng.Next(containers.Count - 1)) % containers.Count;
                        int secondSlot = (targetSlot + 1 + rng.Next(containers.Count - 1)) % containers.Count;
                        var expected = new HashSet<int>(models[firstSlot]);
                        expected.IntersectWith(models[secondSlot]);
                        target.AddIntersection(containers[firstSlot], containers[secondSlot]);
                        model.Clear();
                        model.UnionWith(expected);
                        break;
                    }

                    default:
                        if (rng.Next(4) == 0)
                        {
                            target.Clear();
                            model.Clear();
                        }
                        else
                        {
                            int donorSlot = (targetSlot + 1 + rng.Next(containers.Count - 1)) % containers.Count;
                            GameplayTagContainer clone = containers[donorSlot].Clone();
                            var expected = new HashSet<int>(models[donorSlot]);
                            containers[targetSlot] = clone;
                            models[targetSlot] = expected;
                            target = clone;
                            model = expected;
                        }

                        break;
                }

                if (target.UsesBitset)
                    bitsetOps++;

                AssertContainerMatches(target, snapshot, model, $"seed {seed} step {step}");
                // Epoch 0 is the documented "no tag written yet" state, so an empty container is exempt.
                Assert.That(
                    target.RuntimeIndexEpoch == 0 || target.RuntimeIndexEpoch == snapshot.RuntimeIndexEpoch,
                    Is.True,
                    $"seed {seed} step {step}: a live container must carry the current epoch " +
                    $"({target.RuntimeIndexEpoch} vs {snapshot.RuntimeIndexEpoch})");
                Assert.That(target.IsStale, Is.False,
                    $"seed {seed} step {step}: a container mutated in this epoch must not report stale");
            }

            // The random schedule may never cross the bitset threshold, so probe both storage modes.
            var bitsetProbe = new GameplayTagContainer(registry);
            var bitsetProbeModel = new HashSet<int>();
            for (int index = 1; index < snapshot.TotalTagCount; index++)
            {
                bitsetProbe.AddTag(new GameplayTag(index));
                bitsetProbeModel.Add(index);
            }

            Assert.That(bitsetProbe.UsesBitset, Is.True,
                "fixture: the probe must cross the bitset activation threshold");
            AssertContainerMatches(bitsetProbe, snapshot, bitsetProbeModel, "bitset coverage (full)");

            // Shrink below the activation threshold to check the binary-search storage against the same model.
            bitsetProbe.Clear();
            bitsetProbeModel.Clear();
            for (int index = 1; index <= 40; index++)
            {
                bitsetProbe.AddTag(new GameplayTag(index));
                bitsetProbeModel.Add(index);
            }

            Assert.That(bitsetProbe.UsesBitset, Is.False,
                "fixture: below the threshold the probe must use binary search");
            AssertContainerMatches(bitsetProbe, snapshot, bitsetProbeModel, "binary-search coverage");

            TestContext.WriteLine(
                $"seed {seed}: {bitsetOps} of 400 random operations ran against bitset-backed storage; " +
                "both storage modes verified against the model");
        }

        // ------------------------------------------------- reference model: query engine

        private static GameplayTagQueryExpression RandomLeaf(
            Random rng,
            GameplayTagRegistry registry,
            int[] candidateIndices)
        {
            var tags = new GameplayTagContainer(registry);
            int count = rng.Next(1, 4);
            for (int i = 0; i < count; i++)
                tags.AddTag(new GameplayTag(candidateIndices[rng.Next(candidateIndices.Length)]));

            return new GameplayTagQueryExpression
            {
                Operator = (EGameplayTagQueryExprOperator)rng.Next(0, 3),
                Tags = tags
            };
        }

        private static GameplayTagQueryExpression RandomExpression(
            Random rng,
            GameplayTagRegistry registry,
            int[] candidateIndices,
            int depth)
        {
            if (depth <= 0 || rng.Next(100) < 40)
                return RandomLeaf(rng, registry, candidateIndices);

            int childCount = rng.Next(1, 4);
            var children = new List<GameplayTagQueryExpression>(childCount);
            for (int i = 0; i < childCount; i++)
                children.Add(RandomExpression(rng, registry, candidateIndices, depth - 1));

            return new GameplayTagQueryExpression
            {
                Operator = (EGameplayTagQueryExprOperator)rng.Next(0, 3),
                Expressions = children
            };
        }

        /// <summary>Mirrors the documented semantics: leaves test the expanded set, an empty node is All/None true.</summary>
        private static bool EvaluateReference(GameplayTagQueryExpression expression, GameplayTagContainer target)
        {
            if (expression == null)
                return false;

            bool hasTags = expression.Tags != null && !expression.Tags.IsEmpty;
            bool hasExpressions = expression.Expressions != null && expression.Expressions.Count > 0;
            Assert.That(hasTags && hasExpressions, Is.False,
                "generator produced a node holding both tags and child expressions");

            if (hasTags)
            {
                int count = expression.Tags.ExplicitTagCount;
                switch (expression.Operator)
                {
                    case EGameplayTagQueryExprOperator.All:
                        for (int i = 0; i < count; i++)
                        {
                            if (!target.ContainsRuntimeIndex(expression.Tags.GetExplicitTag(i).RuntimeIndex, false))
                                return false;
                        }

                        return true;

                    case EGameplayTagQueryExprOperator.Any:
                        for (int i = 0; i < count; i++)
                        {
                            if (target.ContainsRuntimeIndex(expression.Tags.GetExplicitTag(i).RuntimeIndex, false))
                                return true;
                        }

                        return false;

                    default:
                        for (int i = 0; i < count; i++)
                        {
                            if (target.ContainsRuntimeIndex(expression.Tags.GetExplicitTag(i).RuntimeIndex, false))
                                return false;
                        }

                        return true;
                }
            }

            if (hasExpressions)
            {
                int count = expression.Expressions.Count;
                switch (expression.Operator)
                {
                    case EGameplayTagQueryExprOperator.All:
                        for (int i = 0; i < count; i++)
                        {
                            if (!EvaluateReference(expression.Expressions[i], target))
                                return false;
                        }

                        return true;

                    case EGameplayTagQueryExprOperator.Any:
                        for (int i = 0; i < count; i++)
                        {
                            if (EvaluateReference(expression.Expressions[i], target))
                                return true;
                        }

                        return false;

                    default:
                        for (int i = 0; i < count; i++)
                        {
                            if (EvaluateReference(expression.Expressions[i], target))
                                return false;
                        }

                        return true;
                }
            }

            // Empty node: the compiler emits PushFalse for Any and PushTrue otherwise.
            return expression.Operator != EGameplayTagQueryExprOperator.Any;
        }

        private static int[] CandidateIndices(GameplayTagRegistry registry)
        {
            TagDataSnapshot snapshot = registry.Snapshot;
            var indices = new int[snapshot.TagCount];
            for (int i = 0; i < indices.Length; i++)
                indices[i] = i + 1;
            return indices;
        }

        [Test]
        public void Query_RandomizedAsts_MatchRecursiveReference([Values(11, 29, 777, 31415)] int seed)
        {
            GameplayTagRegistry registry = BuildRegistry(out _, HierarchyNames(3, 3, 2));
            int[] candidates = CandidateIndices(registry);
            var rng = new Random(seed);

            int evaluated = 0;
            int trues = 0;

            for (int queryIndex = 0; queryIndex < 100; queryIndex++)
            {
                GameplayTagQueryExpression root = RandomExpression(rng, registry, candidates, 4);
                var query = new GameplayTagQuery { RootExpression = root };

                if (queryIndex % 40 == 39)
                    query.InvalidateCompiledCache();

                for (int subjectIndex = 0; subjectIndex < 10; subjectIndex++)
                {
                    var subject = new GameplayTagContainer(registry);
                    int tagCount = rng.Next(0, 6);
                    for (int i = 0; i < tagCount; i++)
                        subject.AddTag(new GameplayTag(candidates[rng.Next(candidates.Length)]));

                    bool expected = EvaluateReference(root, subject);

                    // Evaluated twice: the second pass exercises the compiled cache.
                    bool first = query.Matches(subject);
                    bool second = query.Matches(subject);

                    Assert.That(first, Is.EqualTo(expected),
                        $"seed {seed} query {queryIndex} subject {subjectIndex}: compiled result diverged");
                    Assert.That(second, Is.EqualTo(expected),
                        $"seed {seed} query {queryIndex} subject {subjectIndex}: cached result diverged");

                    evaluated++;
                    if (first) trues++;
                }
            }

            TestContext.WriteLine($"seed {seed}: {evaluated} evaluations, {trues} true");
        }

        /// <summary>Forces the span-stack path (live depth above 64) and keeps nested false pushes inside it.</summary>
        [Test]
        public void Query_WideSpanPath_MixedWithNestedFalseNodes_MatchesReference()
        {
            GameplayTagRegistry registry = BuildRegistry(out _, HierarchyNames(2, 2, 2));
            int[] candidates = CandidateIndices(registry);
            var rng = new Random(424242);

            var wide = new List<GameplayTagQueryExpression>(66);
            for (int i = 0; i < 66; i++)
            {
                GameplayTagQueryExpression inner = RandomExpression(rng, registry, candidates, 2);
                wide.Add(rng.Next(2) == 0
                    ? inner
                    : GameplayTagQueryExpression.Any(inner, RandomLeaf(rng, registry, candidates)));
            }

            GameplayTagQueryExpression root = GameplayTagQueryExpression.All(wide.ToArray());
            var query = new GameplayTagQuery { RootExpression = root };

            for (int subjectIndex = 0; subjectIndex < 60; subjectIndex++)
            {
                var subject = new GameplayTagContainer(registry);
                int tagCount = rng.Next(0, 10);
                for (int i = 0; i < tagCount; i++)
                    subject.AddTag(new GameplayTag(candidates[rng.Next(candidates.Length)]));

                bool expected = EvaluateReference(root, subject);
                Assert.That(query.Matches(subject), Is.EqualTo(expected),
                    $"subject {subjectIndex}: span-path result diverged from the reference model");
            }

            TestContext.WriteLine("span path: 60 evaluations against a 66-wide root");
        }

        /// <summary>
        /// A rebuild that preserves indices must keep a compiled query answerable without changing answers.
        /// </summary>
        [Test]
        public void Query_AfterPreservingReload_RecompilesAndStaysCorrect()
        {
            GameplayTagRegistry registry = BuildRegistry(out CombinatorialSource source, "QReload.A", "QReload.B");
            GameplayTag a = registry.Request("QReload.A", false);
            Assert.That(a.IsNone, Is.False);

            var leafTags = new GameplayTagContainer(registry);
            leafTags.AddTag(a);
            GameplayTagQueryExpression root = GameplayTagQueryExpression.MatchAny(leafTags);
            var query = new GameplayTagQuery { RootExpression = root };

            var subject = new GameplayTagContainer(registry);
            subject.AddTag(a);
            Assert.That(query.Matches(subject), Is.True, "fixture: the query must match before the reload");

            source.Add("QReload.C");
            registry.Reload(preserveRuntimeIndices: true);

            Assert.That(query.Matches(subject), Is.True,
                "a preserving reload keeps indices valid, so the compiled query must still match");
        }

        // --------------------------------------- combinatorial: cross-registry rejection matrix

        [Test]
        public void CrossRegistry_SourceKinds_RejectedAtomicallyForEveryOperation()
        {
            GameplayTagRegistry first = BuildRegistry(out _, "Matrix.A", "Matrix.B");
            var secondSource = new CombinatorialSource("matrix-second", "Matrix.A", "Matrix.B");
            GameplayTagRegistry second = new GameplayTagRegistryBuilder().AddSource(secondSource).Build();

            GameplayTag firstTag = first.Request("Matrix.A", false);
            GameplayTag secondTag = second.Request("Matrix.A", false);
            Assert.That(firstTag.IsNone || secondTag.IsNone, Is.False, "fixture: both registries must resolve");

            var foreignContainer = new GameplayTagContainer(second);
            foreignContainer.AddTag(secondTag);

            ReadOnlyGameplayTagContainer foreignSnapshot = new(foreignContainer);

            SerializableGameplayTagContainer foreignSerializable = new SerializableGameplayTagContainer()
                .Bind(second);
            foreignSerializable.LoadPersisted(new[] { "Matrix.A" });

            IReadOnlyGameplayTagContainer[] sources = { foreignContainer, foreignSnapshot, foreignSerializable };

            foreach (IReadOnlyGameplayTagContainer foreign in sources)
            {
                var target = new GameplayTagContainer(first);
                target.AddTag(firstTag);
                var baseline = new HashSet<int>(ReadExplicit(target));
                string kind = foreign.GetType().Name;

                Assert.That(() => target.AddTags(foreign), Throws.TypeOf<ArgumentException>(),
                    $"{kind}: AddTags must reject a source bound to another registry");
                Assert.That(() => target.RemoveTags(foreign), Throws.TypeOf<ArgumentException>(),
                    $"{kind}: RemoveTags must reject a source bound to another registry");
                Assert.That(() => target.AddUnion(foreign, foreign), Throws.TypeOf<ArgumentException>(),
                    $"{kind}: AddUnion must reject a source bound to another registry");
                Assert.That(() => target.AddIntersection(foreign, foreign), Throws.TypeOf<ArgumentException>(),
                    $"{kind}: AddIntersection must reject a source bound to another registry");
                Assert.That(() => GameplayTagContainer.Copy(target, foreign), Throws.TypeOf<ArgumentException>(),
                    $"{kind}: Copy must reject a source bound to another registry");

                Assert.That(ReadExplicit(target).SetEquals(baseline), Is.True,
                    $"{kind}: every rejected operation must leave the target untouched");
            }
        }

        // ------------------------------------------- combinatorial: registry dynamic budget

        [Test]
        public void Registry_RandomizedDynamicBatches_NeverExceedBudgetAndStayAtomic(
            [Values(5, 50, 500)] int seed)
        {
            var source = new CombinatorialSource("budget", "Budget.Seed");
            const int MaxTags = 40;
            GameplayTagRegistry registry = new GameplayTagRegistryBuilder()
                .AddSource(source)
                .SetMaxRegisteredTagCount(MaxTags)
                .Build();

            _ = registry.Snapshot;
            var rng = new Random(seed);
            int accepted = 0;
            int rejectedBatches = 0;
            int noOpBatches = 0;

            for (int batch = 0; batch < 100; batch++)
            {
                int count = rng.Next(1, 6);
                var names = new string[count];
                for (int i = 0; i < count; i++)
                {
                    names[i] = rng.Next(100) < 70
                        ? $"Budget.B{batch}.N{rng.Next(1000)}"
                        : $"Budget.B{rng.Next(batch + 1)}.N{rng.Next(1000)}";
                }

                TagDataSnapshot before = registry.Snapshot;
                int generationBefore = before.Generation;
                int tagCountBefore = before.TagCount;

                bool threw = false;
                try
                {
                    registry.RegisterDynamicTags(names);
                }
                catch (InvalidOperationException)
                {
                    threw = true;
                }

                TagDataSnapshot after = registry.Snapshot;
                Assert.That(after.TagCount, Is.LessThanOrEqualTo(MaxTags),
                    $"seed {seed} batch {batch}: dynamic registration exceeded the budget");

                if (threw)
                {
                    rejectedBatches++;
                    Assert.That(after.TagCount, Is.EqualTo(tagCountBefore),
                        $"seed {seed} batch {batch}: a rejected batch must not change the snapshot");
                    Assert.That(ReferenceEquals(after, before), Is.True,
                        $"seed {seed} batch {batch}: a rejected batch must not publish a new snapshot");
                }
                else
                {
                    accepted++;
                    for (int i = 0; i < names.Length; i++)
                    {
                        Assert.That(registry.TryRequest(names[i], out _), Is.True,
                            $"seed {seed} batch {batch}: accepted name '{names[i]}' is missing from the registry");
                    }

                    if (after.Generation == generationBefore)
                    {
                        noOpBatches++;
                        Assert.That(after.TagCount, Is.EqualTo(tagCountBefore),
                            $"seed {seed} batch {batch}: a no-op publish must not change the tag count");
                    }

                    int generationAfterAccept = registry.Snapshot.Generation;
                    registry.RegisterDynamicTags(names);
                    Assert.That(registry.Snapshot.Generation, Is.EqualTo(generationAfterAccept),
                        $"seed {seed} batch {batch}: re-registering existing names must not republish");
                }
            }

            TestContext.WriteLine(
                $"seed {seed}: accepted {accepted}, rejected {rejectedBatches}, no-op {noOpBatches}, " +
                $"final tag count {registry.Snapshot.TagCount}");
        }

        // --------------------------------------------- combinatorial: redirect rename chains

        [Test]
        public void Redirect_RandomizedChains_ResolveToTerminalAndRejectCycles()
        {
            var rng = new Random(90210);
            var names = new List<string>(40);
            for (int i = 0; i < 40; i++)
                names.Add($"Chain.T{i:D2}");

            for (int round = 0; round < 20; round++)
            {
                GameplayTagRedirector.ClearAll();
                var adds = new List<KeyValuePair<string, string>>();
                var renamedSources = new HashSet<string>(StringComparer.Ordinal);

                for (int step = 0; step < 10; step++)
                {
                    string from = names[rng.Next(names.Count)];
                    string to = names[rng.Next(names.Count)];

                    // A rename batch renames each source once. Re-pointing one source at a second target is
                    // a different operation whose incremental and batch results are both valid but not comparable.
                    if (string.Equals(from, to, StringComparison.Ordinal) || renamedSources.Contains(from))
                        continue;

                    IReadOnlyDictionary<string, string> table = GameplayTagRedirector.GetAllRedirects();
                    bool wouldCycle = false;
                    string walk = to;
                    for (int guard = 0; guard <= table.Count; guard++)
                    {
                        if (string.Equals(walk, from, StringComparison.Ordinal))
                        {
                            wouldCycle = true;
                            break;
                        }

                        if (!table.TryGetValue(walk, out string next))
                            break;
                        walk = next;
                    }

                    if (wouldCycle)
                    {
                        Assert.That(() => GameplayTagRedirector.AddRedirect(from, to),
                            Throws.TypeOf<InvalidOperationException>(),
                            $"round {round} step {step}: '{from}' -> '{to}' closes a cycle and must be rejected");
                        continue;
                    }

                    GameplayTagRedirector.AddRedirect(from, to);
                    adds.Add(new KeyValuePair<string, string>(from, to));
                    renamedSources.Add(from);

                    table = GameplayTagRedirector.GetAllRedirects();
                    foreach (KeyValuePair<string, string> entry in table)
                    {
                        string resolved = GameplayTagRedirector.Resolve(entry.Key);
                        Assert.That(table.ContainsKey(resolved), Is.False,
                            $"round {round} step {step}: '{entry.Key}' resolved to non-terminal '{resolved}'");
                    }
                }

                // Batch import of the same pairs must reproduce the incremental resolution table.
                var expected = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, string> entry in GameplayTagRedirector.GetAllRedirects())
                    expected[entry.Key] = GameplayTagRedirector.Resolve(entry.Key);

                GameplayTagRedirector.ClearAll();
                GameplayTagRedirector.AddRedirects(adds);

                foreach (KeyValuePair<string, string> pair in expected)
                {
                    Assert.That(GameplayTagRedirector.Resolve(pair.Key), Is.EqualTo(pair.Value),
                        $"round {round}: batch import did not reproduce the incremental resolution for '{pair.Key}'");
                }
            }
        }

        [Test]
        public void RedirectManifestHash_RegistryHashIgnoresRedirects_AmbientHashTracksThem()
        {
            GameplayTagRegistry registry = BuildRegistry(out _, "Hash.A");
            ulong baseHash = registry.ManifestHash;
            ulong ambientBase = GameplayTagManager.ManifestHash;

            GameplayTagRedirector.AddRedirect("Hash.Old", "Hash.A");
            ulong withRedirect = registry.ManifestHash;
            ulong ambientWithRedirect = GameplayTagManager.ManifestHash;

            TestContext.WriteLine(
                $"registry {baseHash:X16} -> {withRedirect:X16}; ambient {ambientBase:X16} -> {ambientWithRedirect:X16}");

            Assert.That(withRedirect, Is.EqualTo(baseHash),
                "a redirect-only change must not perturb the registry's own manifest hash");
            Assert.That(ambientWithRedirect, Is.Not.EqualTo(ambientBase),
                "the ambient manifest hash must account for active redirects");

            GameplayTagRedirector.ClearAll();
            Assert.That(GameplayTagManager.ManifestHash, Is.EqualTo(ambientBase),
                "clearing redirects must restore the original ambient manifest hash");
        }

        // ------------------------------------------------------- observability of the scaling

        /// <summary>
        /// Isolates the buffering cost: no snapshot is published, so the only per-call work is appending a
        /// name. Rescanning the pending set per call would make this quadratic.
        /// </summary>
        [Test]
        [Category("Performance")]
        public void DynamicRegistration_PendingBuffer_DoesNotRescanAccumulatedNames()
        {
            const int Small = 1000;
            const int Large = 4000;

            double smallMs = MeasureBufferedRegistration(Small);
            double largeMs = MeasureBufferedRegistration(Large);
            double ratio = largeMs / Math.Max(smallMs, 0.0001);

            TestContext.WriteLine(
                $"{Small} calls: {smallMs:F2} ms | {Large} calls: {largeMs:F2} ms | ratio {ratio:F2} " +
                $"(linear ~4, quadratic ~16)");

            Assert.That(ratio, Is.LessThan(10.0),
                $"{Large} buffered registrations cost {ratio:F2}x the {Small} case; " +
                "4x the input should be roughly 4x the work, not 16x");
        }

        /// <summary>
        /// Records the per-call republish cost. Every call rebuilds the registry by contract, so this is
        /// informative rather than a gate.
        /// </summary>
        [Test]
        [Category("Performance")]
        public void DynamicRegistration_SingleTagCalls_RepublishPerCall_IsRecorded()
        {
            const int Small = 100;
            const int Large = 400;

            double smallMs = MeasureSingleTagRegistration(Small);
            double largeMs = MeasureSingleTagRegistration(Large);

            TestContext.WriteLine(
                $"republishing singles: {Small} calls {smallMs:F2} ms | {Large} calls {largeMs:F2} ms | " +
                $"ratio {largeMs / Math.Max(smallMs, 0.0001):F2}; RegisterDynamicTag republishes by contract");
        }

        private static double MeasureBufferedRegistration(int count)
        {
            double best = double.MaxValue;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                // No sources and no Snapshot read, so m_Snapshot stays null and no rebuild happens.
                GameplayTagRegistry registry = new GameplayTagRegistryBuilder().Build();
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < count; i++)
                    registry.RegisterDynamicTag($"Buffer.P{attempt}.N{i:D5}");
                stopwatch.Stop();

                best = Math.Min(best, stopwatch.Elapsed.TotalMilliseconds);
            }

            return best;
        }

        private static double MeasureSingleTagRegistration(int count)
        {
            double best = double.MaxValue;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                var probe = new CombinatorialSource("scaling-probe", "Scale.Seed");
                GameplayTagRegistry measured = new GameplayTagRegistryBuilder().AddSource(probe).Build();
                _ = measured.Snapshot;

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < count; i++)
                    measured.RegisterDynamicTag($"Scale.P{attempt}.N{i:D5}");
                stopwatch.Stop();

                best = Math.Min(best, stopwatch.Elapsed.TotalMilliseconds);
            }

            return best;
        }
    }
}
