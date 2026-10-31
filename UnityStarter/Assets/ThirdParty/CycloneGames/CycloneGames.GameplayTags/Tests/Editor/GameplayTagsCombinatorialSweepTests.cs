using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using CycloneGames.GameplayTags.Core;
using CycloneGames.GameplayTags.Unity.Runtime;
using NUnit.Framework;

namespace CycloneGames.GameplayTags.Tests.Editor
{
    /// <summary>
    /// Adversarial and boundary cases: self-referencing set operations, the accepting side of the epoch
    /// guard, engine limits, the pre-initialization window, persistence, and foreign source implementations.
    /// </summary>
    [TestFixture]
    public sealed class GameplayTagsCombinatorialSweepTests
    {
        [SetUp]
        public void SetUp()
        {
            GameplayTagsDiagnostics.ThreadAffinityChecksEnabled = false;
            GameplayTagRedirector.ClearAll();
            GameplayTagManager.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            GameplayTagRedirector.ClearAll();
            GameplayTagManager.ResetForTests();
        }

        // ---------------------------------------------------------------- fixtures

        private sealed class SweepSource : IGameplayTagSource
        {
            private readonly List<string> m_Names = new();

            internal SweepSource(string name, params string[] names)
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

        /// <summary>A container outside Core that declares its epoch through the public provider interface.</summary>
        private sealed class ForeignContainer : IReadOnlyGameplayTagContainer, IGameplayTagEpochProvider
        {
            private readonly int[] m_Explicit;
            private readonly int[] m_Expanded;

            internal ForeignContainer(IEnumerable<int> explicitIndices, IEnumerable<int> expandedIndices, int runtimeIndexEpoch = 0)
            {
                m_Explicit = new List<int>(explicitIndices).ToArray();
                m_Expanded = new List<int>(expandedIndices).ToArray();
                Array.Sort(m_Explicit);
                Array.Sort(m_Expanded);
                RuntimeIndexEpoch = runtimeIndexEpoch;
            }

            public int RuntimeIndexEpoch { get; }

            public bool IsEmpty => m_Explicit.Length == 0;
            public int ExplicitTagCount => m_Explicit.Length;
            public int TagCount => m_Expanded.Length;
            public GameplayTag GetTag(int index) => new(m_Expanded[index]);
            public GameplayTag GetExplicitTag(int index) => new(m_Explicit[index]);
            public GameplayTagEnumerator GetTags() => new(m_Expanded, m_Expanded.Length);
            public GameplayTagEnumerator GetExplicitTags() => new(m_Explicit, m_Explicit.Length);
            public void GetParentTags(GameplayTag tag, List<GameplayTag> parentTags) { }
            public void GetChildTags(GameplayTag tag, List<GameplayTag> childTags) { }
            public void GetExplicitParentTags(GameplayTag tag, List<GameplayTag> parentTags) { }
            public void GetExplicitChildTags(GameplayTag tag, List<GameplayTag> childTags) { }

            public bool ContainsRuntimeIndex(int runtimeIndex, bool explicitOnly)
            {
                int[] source = explicitOnly ? m_Explicit : m_Expanded;
                return Array.BinarySearch(source, runtimeIndex) >= 0;
            }

            public GameplayTagEnumerator GetEnumerator() => GetTags();
            IEnumerator<GameplayTag> IEnumerable<GameplayTag>.GetEnumerator() => GetTags();
            IEnumerator IEnumerable.GetEnumerator() => GetTags();
        }

        /// <summary>Same data, but it does not declare an epoch, so no writer can check it.</summary>
        private sealed class EpochlessContainer : IReadOnlyGameplayTagContainer
        {
            private readonly ForeignContainer m_Inner;

            internal EpochlessContainer(ForeignContainer inner) => m_Inner = inner;

            public bool IsEmpty => m_Inner.IsEmpty;
            public int ExplicitTagCount => m_Inner.ExplicitTagCount;
            public int TagCount => m_Inner.TagCount;
            public GameplayTag GetTag(int index) => m_Inner.GetTag(index);
            public GameplayTag GetExplicitTag(int index) => m_Inner.GetExplicitTag(index);
            public GameplayTagEnumerator GetTags() => m_Inner.GetTags();
            public GameplayTagEnumerator GetExplicitTags() => m_Inner.GetExplicitTags();
            public void GetParentTags(GameplayTag tag, List<GameplayTag> parentTags) { }
            public void GetChildTags(GameplayTag tag, List<GameplayTag> childTags) { }
            public void GetExplicitParentTags(GameplayTag tag, List<GameplayTag> parentTags) { }
            public void GetExplicitChildTags(GameplayTag tag, List<GameplayTag> childTags) { }
            public bool ContainsRuntimeIndex(int runtimeIndex, bool explicitOnly)
                => m_Inner.ContainsRuntimeIndex(runtimeIndex, explicitOnly);
            public GameplayTagEnumerator GetEnumerator() => GetTags();
            IEnumerator<GameplayTag> IEnumerable<GameplayTag>.GetEnumerator() => GetTags();
            IEnumerator IEnumerable.GetEnumerator() => GetTags();
        }

        private static GameplayTagRegistry BuildRegistry(out SweepSource source, params string[] names)
        {
            source = new SweepSource("sweep", names);
            return new GameplayTagRegistryBuilder().AddSource(source).Build();
        }

        private static string[] HierarchyNames(int roots, int children, int grandchildren)
        {
            var names = new List<string>(roots * (1 + children * (1 + grandchildren)));
            for (int r = 0; r < roots; r++)
            {
                names.Add($"Swp.R{r}");
                for (int c = 0; c < children; c++)
                {
                    names.Add($"Swp.R{r}.C{c}");
                    for (int g = 0; g < grandchildren; g++)
                        names.Add($"Swp.R{r}.C{c}.G{g}");
                }
            }

            return names.ToArray();
        }

        private static HashSet<int> ReadExplicit(GameplayTagContainer container)
        {
            var set = new HashSet<int>();
            foreach (GameplayTag tag in container.GetExplicitTags())
                set.Add(tag.RuntimeIndex);
            return set;
        }

        private static string Join(HashSet<int> values)
        {
            var ordered = new List<int>(values);
            ordered.Sort();
            return string.Join(",", ordered);
        }

        // ------------------------------------------------- self-referencing set operations

        [Test]
        public void SelfReferencing_SetOperations_MatchModelOrRefuseCleanly()
        {
            GameplayTagRegistry registry = BuildRegistry(out _, HierarchyNames(2, 2, 2));
            TagDataSnapshot snapshot = registry.Snapshot;
            _ = snapshot;

            int[] targetIndices = { 1, 2, 3, 4, 5, 6 };
            int[] donorIndices = { 4, 5, 6, 7, 8, 9, 10 };

            RunCase("AddUnion(self, other)", (target, donor) => target.AddUnion(target, donor), Union);
            RunCase("AddUnion(other, self)", (target, donor) => target.AddUnion(donor, target), Union);
            RunCase("AddIntersection(self, other)", (target, donor) => target.AddIntersection(target, donor), Intersect);
            RunCase("AddIntersection(other, self)", (target, donor) => target.AddIntersection(donor, target), Intersect);
            RunCase("AddTags(self)", (target, donor) => target.AddTags(target), KeepTarget);
            RunCase("RemoveTags(self)", (target, donor) => target.RemoveTags(target), ClearTarget);

            void RunCase(
                string label,
                Action<GameplayTagContainer, GameplayTagContainer> operation,
                Func<HashSet<int>, HashSet<int>, HashSet<int>> expected)
            {
                var target = new GameplayTagContainer(registry);
                var donor = new GameplayTagContainer(registry);
                for (int i = 0; i < targetIndices.Length; i++)
                    target.AddTag(new GameplayTag(targetIndices[i]));
                for (int i = 0; i < donorIndices.Length; i++)
                    donor.AddTag(new GameplayTag(donorIndices[i]));

                HashSet<int> expect = expected(new HashSet<int>(targetIndices), new HashSet<int>(donorIndices));
                HashSet<int> before = ReadExplicit(target);

                try
                {
                    operation(target, donor);
                }
                catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException)
                {
                    // A refusal is acceptable; silent corruption is not.
                    Assert.That(ReadExplicit(target).SetEquals(before), Is.True,
                        $"{label}: refused with {exception.GetType().Name} but left the container modified");
                    TestContext.WriteLine($"{label}: refused with {exception.GetType().Name}");
                    return;
                }

                HashSet<int> actual = ReadExplicit(target);
                Assert.That(actual.SetEquals(expect), Is.True,
                    $"{label}: expected [{Join(expect)}] actual [{Join(actual)}]");
                Assert.That(target.ExplicitTagCount, Is.EqualTo(expect.Count), $"{label}: count diverged");
            }

            static HashSet<int> Union(HashSet<int> target, HashSet<int> donor)
            {
                var result = new HashSet<int>(target);
                result.UnionWith(donor);
                return result;
            }

            static HashSet<int> Intersect(HashSet<int> target, HashSet<int> donor)
            {
                var result = new HashSet<int>(target);
                result.IntersectWith(donor);
                return result;
            }

            static HashSet<int> KeepTarget(HashSet<int> target, HashSet<int> donor) => new(target);

            static HashSet<int> ClearTarget(HashSet<int> target, HashSet<int> donor) => new();
        }

        // --------------------------------------------- epoch guard must not over-reject

        [Test]
        public void PreservingReload_ExistingSource_IsStillAccepted()
        {
            GameplayTagRegistry registry = BuildRegistry(out SweepSource source, "Keep.A", "Keep.B");
            GameplayTag a = registry.Request("Keep.A", false);
            Assert.That(a.IsNone, Is.False, "fixture tag must resolve");

            var sourceContainer = new GameplayTagContainer(registry);
            sourceContainer.AddTag(a);
            var namesBefore = ReadExplicit(sourceContainer);

            source.Add("Keep.C");
            registry.Reload(preserveRuntimeIndices: true);

            var target = new GameplayTagContainer(registry);
            Assert.That(() => target.AddTags(sourceContainer), Throws.Nothing,
                "a preserving reload keeps indices valid, so the existing source must stay accepted");
            Assert.That(ReadExplicit(target).SetEquals(namesBefore), Is.True,
                "the copied set must be unchanged after a preserving reload");

            Assert.That(() => GameplayTagContainer.Copy(target, sourceContainer), Throws.Nothing);
            Assert.That(() => target.AddUnion(sourceContainer, sourceContainer), Throws.Nothing);
            Assert.That(() => target.AddIntersection(sourceContainer, sourceContainer), Throws.Nothing);
        }

        [Test]
        public void NonPreservingReload_SourceReResolvedFromNames_IsAccepted()
        {
            GameplayTagRegistry registry = BuildRegistry(out SweepSource source, "ReA.A", "ReA.B");
            GameplayTag a = registry.Request("ReA.A", false);
            Assert.That(a.IsNone, Is.False);

            var staleSource = new GameplayTagContainer(registry);
            staleSource.AddTag(a);
            Assert.That(staleSource.TryToPersisted(out string[] persisted), Is.True);

            source.Add("ReA.0First");
            registry.Reload(preserveRuntimeIndices: false);

            // The documented recovery path: rebuild the source from its durable names.
            var recovered = new GameplayTagContainer(registry);
            recovered.LoadPersisted(persisted);

            var target = new GameplayTagContainer(registry);
            Assert.That(() => target.AddTags(recovered), Throws.Nothing,
                "a container rebuilt from persisted names is in the current epoch and must be accepted");
            Assert.That(target.ExplicitTagCount, Is.EqualTo(1));
            Assert.That(registry.GetName(target.GetExplicitTag(0)), Is.EqualTo("ReA.A"),
                "the recovered source must still name the same tag");
        }

        // -------------------------------------------------- pre-initialization registration

        [Test]
        public void DynamicRegistration_BeforeFirstSnapshot_EnforcesBudgetWithoutPublishing()
        {
            const int MaxTags = 8;
            GameplayTagRegistry registry = new GameplayTagRegistryBuilder()
                .SetMaxRegisteredTagCount(MaxTags)
                .Build();

            // Names without a shared prefix: the cap counts implicit parents too, and a common root would
            // consume one slot of its own.
            for (int i = 0; i < 6; i++)
                registry.RegisterDynamicTag($"PreTag{i}");

            Assert.Throws<InvalidOperationException>(() =>
            {
                for (int i = 0; i < 10; i++)
                    registry.RegisterDynamicTag($"PreOverflow{i}");
            }, "the budget must hold before the first snapshot is published");

            TagDataSnapshot snapshot = registry.Snapshot;
            Assert.That(snapshot.TagCount, Is.LessThanOrEqualTo(MaxTags),
                "the first published snapshot must respect the cap");
            for (int i = 0; i < 6; i++)
                Assert.That(registry.TryRequest($"PreTag{i}", out _), Is.True, $"PreTag{i} must survive the first build");

            TestContext.WriteLine($"pre-init: published {snapshot.TagCount} tags with cap {MaxTags}");
        }

        // -------------------------------------------------------- engine limits and depth

        private static GameplayTagQueryExpression Chain(GameplayTagQueryExpression innermost, int depth)
        {
            GameplayTagQueryExpression expression = innermost;
            for (int i = 0; i < depth; i++)
                expression = GameplayTagQueryExpression.All(expression);
            return expression;
        }

        [Test]
        public void Query_DepthSweep_MatchesReferenceAtEveryAcceptedDepth(
            [Values(1, 2, 4, 8, 16, 31, 32)] int depth)
        {
            GameplayTagRegistry registry = BuildRegistry(out _, "Depth.A", "Depth.B");
            GameplayTag a = registry.Request("Depth.A", false);
            GameplayTag b = registry.Request("Depth.B", false);
            Assert.That(a.IsNone || b.IsNone, Is.False);

            var leafTags = new GameplayTagContainer(registry);
            leafTags.AddTag(a);
            GameplayTagQueryExpression leaf = GameplayTagQueryExpression.MatchAll(leafTags);
            GameplayTagQueryExpression root = Chain(leaf, depth);
            var query = new GameplayTagQuery { RootExpression = root };

            var matching = new GameplayTagContainer(registry);
            matching.AddTag(a);
            var notMatching = new GameplayTagContainer(registry);
            notMatching.AddTag(b);

            Assert.That(query.Matches(matching), Is.True, $"depth {depth}: a matching subject must match");
            Assert.That(query.Matches(notMatching), Is.False, $"depth {depth}: a non-matching subject must not match");
            Assert.That(query.Matches(matching), Is.True, $"depth {depth}: the cached compilation must agree");
        }

        [Test]
        public void Query_DepthBeyondLimit_IsRejected()
        {
            GameplayTagRegistry registry = BuildRegistry(out _, "DepthLimit.A");
            GameplayTag a = registry.Request("DepthLimit.A", false);
            var leafTags = new GameplayTagContainer(registry);
            leafTags.AddTag(a);

            GameplayTagQueryExpression root = Chain(GameplayTagQueryExpression.MatchAll(leafTags),
                GameplayTagQuery.MaxExpressionDepth + 1);
            var query = new GameplayTagQuery { RootExpression = root };
            var subject = new GameplayTagContainer(registry);
            subject.AddTag(a);

            Assert.That(() => query.Matches(subject), Throws.TypeOf<InvalidOperationException>(),
                $"a depth of {GameplayTagQuery.MaxExpressionDepth + 1} must be rejected");
        }

        [Test]
        public void Query_NodeBudgetBoundary_AcceptsExactLimitAndRejectsOverflow()
        {
            GameplayTagRegistry registry = BuildRegistry(out _, "Nodes.A");
            var subject = new GameplayTagContainer(registry);

            // One root node plus N empty children: the node count is N + 1.
            GameplayTagQuery BuildAt(int childCount)
            {
                var children = new List<GameplayTagQueryExpression>(childCount);
                for (int i = 0; i < childCount; i++)
                    children.Add(new GameplayTagQueryExpression { Operator = EGameplayTagQueryExprOperator.All });

                return new GameplayTagQuery
                {
                    RootExpression = new GameplayTagQueryExpression
                    {
                        Operator = EGameplayTagQueryExprOperator.All,
                        Expressions = children
                    }
                };
            }

            int atLimit = GameplayTagQuery.MaxExpressionNodes - 1;
            Assert.That(() => BuildAt(atLimit).Matches(subject), Throws.Nothing,
                $"exactly {GameplayTagQuery.MaxExpressionNodes} nodes must compile");
            Assert.That(() => BuildAt(atLimit + 1).Matches(subject), Throws.TypeOf<InvalidOperationException>(),
                $"{GameplayTagQuery.MaxExpressionNodes + 1} nodes must be rejected");
        }

        // --------------------------------------------------------- persistence round trip

        [Test]
        public void Persistence_RandomContainers_RoundTripThroughNames([Values(1, 9, 77, 4242)] int seed)
        {
            GameplayTagRegistry registry = BuildRegistry(out _, HierarchyNames(3, 3, 2));
            TagDataSnapshot snapshot = registry.Snapshot;
            var rng = new Random(seed);
            int roundTrips = 0;

            for (int iteration = 0; iteration < 60; iteration++)
            {
                var original = new GameplayTagContainer(registry);
                int count = rng.Next(0, 8);
                for (int i = 0; i < count; i++)
                    original.AddTag(new GameplayTag(1 + rng.Next(snapshot.TotalTagCount - 1)));

                Assert.That(original.TryToPersisted(out string[] names), Is.True,
                    $"seed {seed} iteration {iteration}: a live container must persist");

                var restored = new GameplayTagContainer(registry);
                restored.LoadPersisted(names);

                Assert.That(restored.ExplicitTagCount, Is.EqualTo(original.ExplicitTagCount),
                    $"seed {seed} iteration {iteration}: explicit count changed across the round trip");
                Assert.That(ReadExplicit(restored).SetEquals(ReadExplicit(original)), Is.True,
                    $"seed {seed} iteration {iteration}: the explicit set changed across the round trip");
                Assert.That(restored.TagCount, Is.EqualTo(original.TagCount),
                    $"seed {seed} iteration {iteration}: expanded count changed across the round trip");

                // Names are the durable identity: they must survive a second hop unchanged.
                Assert.That(restored.TryToPersisted(out string[] secondNames), Is.True);
                Assert.That(secondNames, Is.EqualTo(names),
                    $"seed {seed} iteration {iteration}: names are not stable across two round trips");
                roundTrips++;
            }

            TestContext.WriteLine($"seed {seed}: {roundTrips} container round trips through persisted names");
        }

        // ------------------------------------------------------------ source implementation matrix

        [Test]
        public void SourceKindMatrix_EveryBuiltInContainerIsEpochChecked()
        {
            GameplayTagRegistry registry = BuildRegistry(out SweepSource source, "Src.A", "Src.B");
            GameplayTag a = registry.Request("Src.A", false);
            Assert.That(a.IsNone, Is.False);

            var plainContainer = new GameplayTagContainer(registry);
            plainContainer.AddTag(a);

            var countContainer = new GameplayTagCountContainer(registry);
            countContainer.AddTag(a);

            ReadOnlyGameplayTagContainer snapshotView = new(plainContainer);

            SerializableGameplayTagContainer bridge = new SerializableGameplayTagContainer().Bind(registry);
            bridge.LoadPersisted(new[] { "Src.A" });

            IReadOnlyGameplayTagContainer[] staleIndexSources = { plainContainer, countContainer, snapshotView };

            source.Add("Src.0First");
            registry.Reload(preserveRuntimeIndices: false);

            foreach (IReadOnlyGameplayTagContainer stale in staleIndexSources)
            {
                var target = new GameplayTagContainer(registry);
                string kind = stale.GetType().Name;
                Assert.That(
                    () => target.AddTags(stale),
                    Throws.TypeOf<ArgumentException>(),
                    $"{kind}: a source holding the previous epoch's indices must be refused");
                Assert.That(target.ExplicitTagCount, Is.Zero, $"{kind}: a refused copy must not write anything");
            }

            // The bridge stores names, so a new snapshot makes it re-resolve and it stays valid.
            var bridgeTarget = new GameplayTagContainer(registry);
            Assert.That(() => bridgeTarget.AddTags(bridge), Throws.Nothing,
                "a name-backed bridge re-resolves after a rebuild and must be accepted");
            Assert.That(registry.GetName(bridgeTarget.GetExplicitTag(0)), Is.EqualTo("Src.A"),
                "the bridge must copy the tag the name still points at");
        }

        [Test]
        public void ForeignContainer_DeclaringItsEpoch_IsRejectedWhenStale()
        {
            GameplayTagRegistry registry = BuildRegistry(out SweepSource source, "Foreign.A", "Foreign.B");
            GameplayTag a = registry.Request("Foreign.A", false);
            Assert.That(a.IsNone, Is.False, "fixture tag must resolve");
            int staleIndex = a.RuntimeIndex;
            int staleEpoch = registry.RuntimeIndexEpoch;

            source.Add("Foreign.0First");
            registry.Reload(preserveRuntimeIndices: false);

            var foreign = new ForeignContainer(new[] { staleIndex }, new[] { staleIndex }, staleEpoch);
            var target = new GameplayTagContainer(registry);

            Assert.That(() => target.AddTags(foreign), Throws.TypeOf<ArgumentException>(),
                "a third-party container that declares a stale epoch must be refused exactly like a built-in one");
            Assert.That(target.ExplicitTagCount, Is.Zero, "a refused copy must not write anything");
        }

        [Test]
        public void ForeignContainer_WithoutEpochDeclaration_IsStillUnchecked_ResidualGap()
        {
            GameplayTagRegistry registry = BuildRegistry(out SweepSource source, "Foreign.A", "Foreign.B");
            GameplayTag a = registry.Request("Foreign.A", false);
            Assert.That(a.IsNone, Is.False, "fixture tag must resolve");
            int staleIndex = a.RuntimeIndex;
            string originalName = registry.GetName(a);

            source.Add("Foreign.0First");
            registry.Reload(preserveRuntimeIndices: false);
            string remapped = registry.GetName(new GameplayTag(staleIndex));

            var silent = new EpochlessContainer(
                new ForeignContainer(new[] { staleIndex }, new[] { staleIndex }));
            var target = new GameplayTagContainer(registry);

            Assert.That(() => target.AddTags(silent), Throws.Nothing,
                "a container that does not declare an epoch cannot be checked; this is the documented residue");

            string copied = registry.GetName(target.GetExplicitTag(0));
            TestContext.WriteLine(
                $"RESIDUAL: undeclared-epoch source '{originalName}' was remapped to '{copied}' " +
                $"(index {staleIndex} now means '{remapped}') without a refusal");
        }

        [Test]
        public void Copy_ForeignContainerReportingNone_DropsTheNoneEntry()
        {
            GameplayTagRegistry registry = BuildRegistry(out _, "NoneSrc.A");
            GameplayTag a = registry.Request("NoneSrc.A", false);
            Assert.That(a.IsNone, Is.False, "fixture tag must resolve");

            var foreign = new ForeignContainer(
                new[] { 0, a.RuntimeIndex },
                new[] { 0, a.RuntimeIndex });

            var target = new GameplayTagContainer(registry);
            GameplayTagContainer.Copy(target, foreign);

            Assert.That(target.ExplicitTagCount, Is.EqualTo(1),
                "index 0 is the None sentinel, not a tag, and must not enter the explicit set");
            Assert.That(target.TryToPersisted(out string[] names), Is.True);
            Assert.That(names, Is.EqualTo(new[] { "NoneSrc.A" }),
                "persisting the copied set must not write the None sentinel as a name");
        }

        // ------------------------------------------------------ hierarchy queries vs model

        [Test]
        public void HierarchyQueries_RandomContainers_MatchModel([Values(5, 31, 808)] int seed)
        {
            GameplayTagRegistry registry = BuildRegistry(out _, HierarchyNames(2, 3, 2));
            TagDataSnapshot snapshot = registry.Snapshot;
            var rng = new Random(seed);
            int checks = 0;

            for (int iteration = 0; iteration < 40; iteration++)
            {
                var container = new GameplayTagContainer(registry);
                int count = rng.Next(1, 6);
                for (int i = 0; i < count; i++)
                    container.AddTag(new GameplayTag(1 + rng.Next(snapshot.TotalTagCount - 1)));

                HashSet<int> explicitSet = ReadExplicit(container);
                HashSet<int> expandedSet = Expand(snapshot, explicitSet);
                var parents = new List<GameplayTag>();
                var children = new List<GameplayTag>();

                for (int probe = 1; probe < snapshot.TotalTagCount; probe++)
                {
                    var tag = new GameplayTag(probe);

                    var expectedParents = new HashSet<int>();
                    int start = snapshot.HierarchyOffsets[probe];
                    int end = snapshot.HierarchyOffsets[probe + 1] - 1;
                    for (int k = start; k < end; k++)
                    {
                        if (explicitSet.Contains(snapshot.HierarchyPool[k]))
                            expectedParents.Add(snapshot.HierarchyPool[k]);
                    }

                    parents.Clear();
                    container.GetExplicitParentTags(tag, parents);
                    var actualParents = new HashSet<int>();
                    for (int k = 0; k < parents.Count; k++)
                        actualParents.Add(parents[k].RuntimeIndex);
                    Assert.That(actualParents.SetEquals(expectedParents), Is.True,
                        $"seed {seed} iter {iteration} probe {probe}: explicit parents diverged");

                    var expectedChildren = new HashSet<int>();
                    int childStart = snapshot.ChildOffsets[probe];
                    int childEnd = snapshot.ChildOffsets[probe + 1];
                    for (int k = childStart; k < childEnd; k++)
                    {
                        if (expandedSet.Contains(snapshot.ChildPool[k]))
                            expectedChildren.Add(snapshot.ChildPool[k]);
                    }

                    children.Clear();
                    container.GetChildTags(tag, children);
                    var actualChildren = new HashSet<int>();
                    for (int k = 0; k < children.Count; k++)
                        actualChildren.Add(children[k].RuntimeIndex);
                    Assert.That(actualChildren.SetEquals(expectedChildren), Is.True,
                        $"seed {seed} iter {iteration} probe {probe}: direct children diverged");
                    checks++;
                }
            }

            TestContext.WriteLine($"seed {seed}: {checks} hierarchy query comparisons against the model");
        }

        private static HashSet<int> Expand(TagDataSnapshot snapshot, IEnumerable<int> explicitIndices)
        {
            var expanded = new HashSet<int>();
            foreach (int runtimeIndex in explicitIndices)
            {
                int start = snapshot.HierarchyOffsets[runtimeIndex];
                int end = snapshot.HierarchyOffsets[runtimeIndex + 1];
                for (int i = start; i < end; i++)
                    expanded.Add(snapshot.HierarchyPool[i]);
            }

            return expanded;
        }

        // -------------------------------------------------------------- concurrency

        [Test]
        public void Concurrent_IndependentContainers_MatchSingleThreadedReference()
        {
            GameplayTagRegistry registry = BuildRegistry(out _, HierarchyNames(4, 4, 3));
            TagDataSnapshot snapshot = registry.Snapshot;
            const int Workers = 8;
            const int Operations = 400;

            HashSet<int> RunScript(GameplayTagContainer container, int worker)
            {
                var rng = new Random(7000 + worker);
                for (int step = 0; step < Operations; step++)
                {
                    int index = 1 + rng.Next(snapshot.TotalTagCount - 1);
                    if (rng.Next(3) == 0) container.RemoveTag(new GameplayTag(index));
                    else container.AddTag(new GameplayTag(index));
                }

                return ReadExplicit(container);
            }

            var expected = new List<HashSet<int>>(Workers);
            for (int worker = 0; worker < Workers; worker++)
                expected.Add(RunScript(new GameplayTagContainer(registry), worker));

            var results = new HashSet<int>[Workers];
            var containers = new GameplayTagContainer[Workers];
            for (int worker = 0; worker < Workers; worker++)
                containers[worker] = new GameplayTagContainer(registry);

            Parallel.For(0, Workers, worker =>
            {
                results[worker] = RunScript(containers[worker], worker);
            });

            for (int worker = 0; worker < Workers; worker++)
            {
                Assert.That(results[worker].SetEquals(expected[worker]), Is.True,
                    $"worker {worker}: concurrent result diverged from the single-threaded reference");
            }

            TestContext.WriteLine($"{Workers} workers x {Operations} operations matched the single-threaded reference");
        }

        [Test]
        public void Concurrent_ReadsDuringRepeatedReload_NeverThrowOrTear()
        {
            GameplayTagRegistry registry = BuildRegistry(out SweepSource source, "Conc.A", "Conc.B");
            GameplayTag a = registry.Request("Conc.A", false);
            Assert.That(a.IsNone, Is.False, "fixture tag must resolve");
            _ = registry.Snapshot;

            var owner = new GameplayTagContainer(registry);
            owner.AddTag(a);
            ReadOnlyGameplayTagContainer frozen = new(owner);
            const int ReaderCount = 4;
            const int ReadsPerWorker = 4000;

            var failures = new System.Collections.Concurrent.ConcurrentQueue<string>();

            var readers = new Task[ReaderCount];
            for (int worker = 0; worker < ReaderCount; worker++)
            {
                readers[worker] = Task.Run(() =>
                {
                    for (int i = 0; i < ReadsPerWorker; i++)
                    {
                        try
                        {
                            GameplayTag resolved = registry.Request("Conc.A", false);
                            if (resolved.IsNone)
                                failures.Enqueue("a registered tag resolved to None during a reload");

                            if (!frozen.ContainsRuntimeIndex(a.RuntimeIndex, explicitOnly: true))
                                failures.Enqueue("the frozen view lost a tag it was built with");

                            TagDataSnapshot observed = registry.Snapshot;
                            if (observed.TotalTagCount < 1 || observed.TagCount < 0)
                                failures.Enqueue("a concurrent reader observed a torn snapshot");
                        }
                        catch (Exception exception)
                        {
                            failures.Enqueue($"{exception.GetType().Name}: {exception.Message}");
                        }
                    }
                });
            }

            for (int reload = 0; reload < 40; reload++)
            {
                source.Add($"Conc.Added{reload:D3}");
                registry.Reload(preserveRuntimeIndices: true);
            }

            Assert.That(Task.WaitAll(readers, TimeSpan.FromSeconds(60)), Is.True, "readers did not finish in time");
            Assert.That(failures.IsEmpty, Is.True,
                $"concurrent readers observed {failures.Count} failure(s), first: " +
                (failures.TryDequeue(out string first) ? first : "none"));
            Assert.That(frozen.IsCompatibleWithCurrentRegistry, Is.True,
                "a preserving reload must keep previously frozen views valid");

            TestContext.WriteLine(
                $"{ReaderCount} readers x {ReadsPerWorker} reads survived 40 preserving reloads");
        }

        // ------------------------------------------------------------ determinism golden vectors

        /// <summary>
        /// Pins the manifest hash and index assignment to values from an independent implementation
        /// (FNV-1a over UTF-16 code units, little-endian combine, ordinal sort). A change here means two
        /// peers on different runtimes can disagree about tag identity.
        /// </summary>
        [Test]
        public void Determinism_GoldenVectors_MatchIndependentImplementation()
        {
            var expected = new (string Name, string StableId)[]
            {
                ("Ability.Fireball", "5DB92DF9BEDCA7B0"),
                ("Ability.Fireball.Cast", "A11CCB5988317949"),
                ("Status.Burning", "8B1A85740567C846"),
                ("Status.Burning.Stack", "D64FE696EE527290"),
                ("UI.Menu", "B191BBD85BDED0A0"),
                ("Zeta", "4957C36D35F2197F"),
                ("alpha.lower", "B6ADF620EFA987F4"),
                ("中文.标签", "1511C2882DA898CC")
            };

            var names = new string[expected.Length];
            for (int i = 0; i < expected.Length; i++)
            {
                names[i] = expected[i].Name;
                Assert.That(GameplayTagUtility.ComputeStableId(expected[i].Name).ToString("X16"),
                    Is.EqualTo(expected[i].StableId),
                    $"stable id drifted for '{expected[i].Name}'");
            }

            GameplayTagRegistry registry = BuildRegistry(out _, names);
            TagDataSnapshot snapshot = registry.Snapshot;
            Assert.That(registry.ManifestHash.ToString("X16"), Is.EqualTo("EECEB8010C02B163"),
                "the manifest hash drifted from the independent implementation");

            string[] expectedOrder =
            {
                "Ability", "Ability.Fireball", "Ability.Fireball.Cast", "Status", "Status.Burning",
                "Status.Burning.Stack", "UI", "UI.Menu", "Zeta", "alpha", "alpha.lower", "中文", "中文.标签"
            };

            Assert.That(snapshot.TotalTagCount, Is.EqualTo(expectedOrder.Length + 1),
                "the registry must contain the None entry plus every declared name and its ancestors");
            for (int i = 0; i < expectedOrder.Length; i++)
            {
                Assert.That(snapshot.GetName(i + 1), Is.EqualTo(expectedOrder[i]),
                    $"runtime index {i + 1} was assigned to a different name than the ordinal order requires");
            }

            TestContext.WriteLine(
                $"golden vectors matched: {expected.Length} stable ids, manifest {registry.ManifestHash:X16}, " +
                $"{expectedOrder.Length} ordinal-assigned indices");
        }

        [Test]
        public void Determinism_InputOrder_DoesNotAffectIndicesOrManifest()
        {
            string[] forward = { "Order.A", "Order.B", "Order.C.D", "Order.C.E" };
            var reversed = new string[forward.Length];
            for (int i = 0; i < forward.Length; i++)
                reversed[i] = forward[forward.Length - 1 - i];

            GameplayTagRegistry first = BuildRegistry(out _, forward);
            GameplayTagRegistry second = BuildRegistry(out _, reversed);

            Assert.That(second.ManifestHash, Is.EqualTo(first.ManifestHash),
                "the manifest must not depend on declaration order");
            Assert.That(second.TagCount, Is.EqualTo(first.TagCount));
            for (int index = 1; index < first.Snapshot.TotalTagCount; index++)
            {
                Assert.That(second.Snapshot.GetName(index), Is.EqualTo(first.Snapshot.GetName(index)),
                    $"index {index} was assigned differently for a different declaration order");
            }
        }

        // ------------------------------------------------------------ bitset storage

        /// <summary>
        /// The bitset keeps its capacity when the highest held index drops. Words above the new maximum
        /// must not answer membership tests for indices the container no longer holds.
        /// </summary>
        [Test]
        public void Container_BitsetShrink_ClearsWordsAboveTheNewMaximum()
        {
            GameplayTagRegistry registry = BuildRegistry(out _, HierarchyNames(6, 4, 3));
            TagDataSnapshot snapshot = registry.Snapshot;

            var container = new GameplayTagContainer(registry);
            var explicitModel = new HashSet<int>();
            for (int index = 1; index <= 100; index++)
            {
                container.AddTag(new GameplayTag(index));
                explicitModel.Add(index);
            }

            Assert.That(container.UsesBitset, Is.True, "fixture: 100 held tags must activate the bitset");

            for (int index = 90; index <= 100; index++)
            {
                container.RemoveTag(new GameplayTag(index));
                explicitModel.Remove(index);
            }

            Assert.That(container.UsesBitset, Is.True, "fixture: the bitset must stay active after the shrink");

            HashSet<int> expandedModel = Expand(snapshot, explicitModel);
            Assert.That(container.ExplicitTagCount, Is.EqualTo(explicitModel.Count));
            Assert.That(container.TagCount, Is.EqualTo(expandedModel.Count));

            int leaked = -1;
            for (int index = 1; index < snapshot.TotalTagCount && leaked < 0; index++)
            {
                bool expectedExplicit = explicitModel.Contains(index);
                bool expectedExpanded = expandedModel.Contains(index);
                if (container.ContainsRuntimeIndex(index, explicitOnly: true) != expectedExplicit ||
                    container.ContainsRuntimeIndex(index, explicitOnly: false) != expectedExpanded)
                {
                    leaked = index;
                }
            }

            Assert.That(leaked, Is.EqualTo(-1), $"index {leaked} is still answered after it was removed");
        }

        private static int[] ReadPrivateIntArray(GameplayTagContainer container, string fieldName)
        {
            System.Reflection.FieldInfo field = typeof(GameplayTagContainer).GetField(
                fieldName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.That(field, Is.Not.Null, $"field {fieldName} was not found");
            return (int[])field.GetValue(container);
        }

        /// <summary>
        /// A container that loses every tag releases the bitset, and re-crossing the activation threshold
        /// rebuilds it. Membership must be correct on each side of every transition.
        /// </summary>
        [Test]
        public void Container_BitsetReactivation_AcrossClearAndThreshold_StaysCorrect()
        {
            GameplayTagRegistry registry = BuildRegistry(out _, HierarchyNames(6, 4, 3));
            TagDataSnapshot snapshot = registry.Snapshot;
            var container = new GameplayTagContainer(registry);
            var model = new HashSet<int>();

            void AssertMembership(string stage)
            {
                HashSet<int> expanded = Expand(snapshot, model);
                Assert.That(container.ExplicitTagCount, Is.EqualTo(model.Count), $"{stage}: explicit count");
                Assert.That(container.TagCount, Is.EqualTo(expanded.Count), $"{stage}: expanded count");

                int diverged = -1;
                for (int index = 1; index < snapshot.TotalTagCount && diverged < 0; index++)
                {
                    if (container.ContainsRuntimeIndex(index, explicitOnly: true) != model.Contains(index) ||
                        container.ContainsRuntimeIndex(index, explicitOnly: false) != expanded.Contains(index))
                    {
                        diverged = index;
                    }
                }

                Assert.That(diverged, Is.EqualTo(-1), $"{stage}: index {diverged} diverged");
            }

            void AddRange(int from, int to)
            {
                for (int index = from; index <= to; index++)
                {
                    container.AddTag(new GameplayTag(index));
                    model.Add(index);
                }
            }

            void RemoveRange(int from, int to)
            {
                for (int index = from; index <= to; index++)
                {
                    container.RemoveTag(new GameplayTag(index));
                    model.Remove(index);
                }
            }

            AddRange(1, 100);
            Assert.That(container.UsesBitset, Is.True, "fixture: 100 tags must activate the bitset");
            AssertMembership("after first fill");

            RemoveRange(1, 100);
            Assert.That(container.UsesBitset, Is.False, "an empty container must release the bitset");
            AssertMembership("after draining");

            AddRange(1, 100);
            Assert.That(container.UsesBitset, Is.True, "refilling must reactivate the bitset");
            AssertMembership("after refill");

            RemoveRange(21, 100);
            Assert.That(container.UsesBitset, Is.False, "falling below the activation threshold must release the bitset");
            AssertMembership("below threshold");

            AddRange(21, 100);
            Assert.That(container.UsesBitset, Is.True, "crossing the threshold again must reactivate the bitset");
            AssertMembership("after re-crossing");

            container.Clear();
            model.Clear();
            Assert.That(container.UsesBitset, Is.False, "Clear must release the bitset");
            AssertMembership("after Clear");
        }

        /// <summary>
        /// While the bitset stays active its backing array is reused rather than reallocated, which is what
        /// keeps a grant/drop pair from allocating.
        /// </summary>
        [Test]
        public void Container_BitsetCapacity_IsReusedWhileActive()
        {
            GameplayTagRegistry registry = BuildRegistry(out _, HierarchyNames(6, 4, 3));
            TagDataSnapshot snapshot = registry.Snapshot;
            var container = new GameplayTagContainer(registry);

            for (int index = 1; index <= 100; index++)
                container.AddTag(new GameplayTag(index));

            Assert.That(container.UsesBitset, Is.True, "fixture: 100 tags must activate the bitset");
            int[] active = ReadPrivateIntArray(container, "m_Bitset");
            Assert.That(active.Length, Is.GreaterThan(0));

            for (int index = 90; index <= 100; index++)
                container.RemoveTag(new GameplayTag(index));

            Assert.That(container.UsesBitset, Is.True, "fixture: the bitset must stay active after this shrink");
            Assert.That(ReadPrivateIntArray(container, "m_Bitset"), Is.SameAs(active),
                "the bitset backing array was reallocated while the bitset stayed active");
            _ = snapshot;
        }

        /// <summary>
        /// Clear releases every backing array, including the self-reference snapshot, so a pooled container
        /// does not carry the peak of an operation it no longer performs.
        /// </summary>
        [Test]
        public void Container_Clear_ReleasesSelfReferenceScratch()
        {
            GameplayTagRegistry registry = BuildRegistry(out _, HierarchyNames(2, 3, 2));
            var container = new GameplayTagContainer(registry);
            var donor = new GameplayTagContainer(registry);

            for (int index = 1; index <= 20; index++)
                container.AddTag(new GameplayTag(index));
            for (int index = 10; index <= 20; index++)
                donor.AddTag(new GameplayTag(index));

            container.AddUnion(container, donor);
            Assert.That(ReadPrivateIntArray(container, "m_SourceScratch").Length, Is.GreaterThanOrEqualTo(20),
                "fixture: a self-referencing union must materialize the snapshot");

            container.Clear();
            Assert.That(ReadPrivateIntArray(container, "m_SourceScratch").Length, Is.Zero,
                "Clear must release the self-reference scratch");
            Assert.That(ReadPrivateIntArray(container, "m_Explicit").Length, Is.Zero,
                "Clear must release the explicit array");

            container.AddTags(donor);
            container.AddUnion(container, donor);
            Assert.That(container.ExplicitTagCount, Is.EqualTo(donor.ExplicitTagCount),
                "a self-referencing union must still work after Clear released the scratch");
        }

        // ------------------------------------------------------------------ seed sweep

        [Test]
        public void Container_SetOperations_SeedSweep()
        {
            GameplayTagRegistry registry = BuildRegistry(out _, HierarchyNames(3, 3, 3));
            TagDataSnapshot snapshot = registry.Snapshot;
            int totalOperations = 0;

            for (int seed = 1000; seed < 1024; seed++)
            {
                var rng = new Random(seed);
                var containers = new List<GameplayTagContainer>();
                var models = new List<HashSet<int>>();
                for (int i = 0; i < 3; i++)
                {
                    containers.Add(new GameplayTagContainer(registry));
                    models.Add(new HashSet<int>());
                }

                for (int step = 0; step < 120; step++)
                {
                    int slot = rng.Next(3);
                    int donorSlot = (slot + 1 + rng.Next(2)) % 3;
                    int index = 1 + rng.Next(snapshot.TotalTagCount - 1);
                    GameplayTagContainer target = containers[slot];
                    HashSet<int> model = models[slot];

                    switch (rng.Next(6))
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
                            target.AddTags(containers[donorSlot]);
                            model.UnionWith(models[donorSlot]);
                            break;
                        case 3:
                            target.RemoveTags(containers[donorSlot]);
                            model.ExceptWith(models[donorSlot]);
                            break;
                        case 4:
                        {
                            // A fresh destination keeps operand aliasing out of this sweep.
                            var union = new HashSet<int>(models[slot]);
                            union.UnionWith(models[donorSlot]);
                            var result = new GameplayTagContainer(registry);
                            result.AddUnion(containers[slot], containers[donorSlot]);
                            containers[slot] = result;
                            models[slot] = union;
                            break;
                        }
                        default:
                        {
                            var intersection = new HashSet<int>(models[slot]);
                            intersection.IntersectWith(models[donorSlot]);
                            var result = new GameplayTagContainer(registry);
                            result.AddIntersection(containers[slot], containers[donorSlot]);
                            containers[slot] = result;
                            models[slot] = intersection;
                            break;
                        }
                    }

                    HashSet<int> actual = ReadExplicit(containers[slot]);
                    HashSet<int> expected = models[slot];
                    Assert.That(actual.SetEquals(expected), Is.True,
                        $"seed {seed} step {step}: explicit set diverged " +
                        $"(expected [{Join(expected)}] actual [{Join(actual)}])");
                    totalOperations++;
                }
            }

            TestContext.WriteLine($"seed sweep: {totalOperations} operations across 24 seeds");
        }
    }
}
