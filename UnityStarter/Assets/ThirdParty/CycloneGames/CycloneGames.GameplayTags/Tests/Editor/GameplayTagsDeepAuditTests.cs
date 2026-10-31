using System;
using System.Collections.Generic;
using System.Reflection;
using CycloneGames.GameplayTags.Core;
using CycloneGames.GameplayTags.Unity.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.GameplayTags.Tests.Editor
{
    /// <summary>
    /// Temporary, independently authorized deep-audit cases. These tests intentionally encode the
    /// documented contracts; failures must remain as evidence rather than being weakened to pass.
    /// </summary>
    public sealed class GameplayTagsDeepAuditTests
    {
        private GameplayTag _a;
        private GameplayTag _b;
        private GameplayTag _c;

        [SetUp]
        public void SetUp()
        {
            GameplayTagManager.ResetForTests();
            GameplayTagRedirector.ClearAll();
            GameplayTagHost.ClearRegisteredProjectTagSources();
            GameplayTagManager.RegisterDynamicTags(new[]
            {
                "DeepAudit.A",
                "DeepAudit.B",
                "DeepAudit.C",
                "DeepAudit.Hierarchy.Root.Child",
                "DeepAudit.Hierarchy.Root.Child.Grand",
                "DeepAudit.Serializable.Root.Child"
            });
            _a = GameplayTagManager.Request("DeepAudit.A");
            _b = GameplayTagManager.Request("DeepAudit.B");
            _c = GameplayTagManager.Request("DeepAudit.C");
        }

        [TearDown]
        public void TearDown()
        {
            GameplayTagManager.ResetForTests();
            GameplayTagRedirector.ClearAll();
            GameplayTagHost.ClearRegisteredProjectTagSources();
        }

        [Test]
        public void Query_NestedFalseBitmask_DoesNotReuseStaleTrueBit_AndMatchesSpanPath()
        {
            GameplayTagQueryExpression root = All(
                All(Leaf(EGameplayTagQueryExprOperator.Any, _a),
                    Leaf(EGameplayTagQueryExprOperator.Any, _b)),
                Leaf(EGameplayTagQueryExprOperator.Any, _c));
            GameplayTagContainer subject = Tags(_a, _b);

            bool bitmask = new GameplayTagQuery { RootExpression = root }.Matches(subject);
            bool span = new GameplayTagQuery { RootExpression = ForceSpanStack(root) }.Matches(subject);
            TestContext.WriteLine($"[DeepAudit][QueryExactCounterexample] expected=False bitmask={bitmask} span={span}");

            Multiple(() =>
            {
                Assert.That(bitmask, Is.False, "Root All must be false when its second child does not match C.");
                Assert.That(span, Is.False, "The equivalent >64-live-stack Span query must return the same result.");
            });
        }

        [TestCase(64, -1)]
        [TestCase(64, 0)]
        [TestCase(64, 63)]
        [TestCase(65, -1)]
        [TestCase(65, 0)]
        [TestCase(65, 32)]
        [TestCase(65, 64)]
        public void Query_StackCapacityBoundary_MatchesReferenceModel(int liveDepth, int falseIndex)
        {
            var children = new List<GameplayTagQueryExpression>(liveDepth);
            for (int i = 0; i < liveDepth; i++)
            {
                children.Add(Leaf(
                    EGameplayTagQueryExprOperator.Any,
                    i == falseIndex ? _c : _a));
            }

            var root = new GameplayTagQueryExpression
            {
                Operator = EGameplayTagQueryExprOperator.All,
                Expressions = children
            };
            bool result = new GameplayTagQuery { RootExpression = root }.Matches(Tags(_a, _b));
            Assert.That(result, Is.EqualTo(falseIndex < 0),
                $"liveDepth={liveDepth}, falseIndex={falseIndex}");
        }

        [Test]
        public void Query_GeneratedAst_BitmaskSpanAndRecursiveModel_AgreeForEverySubject()
        {
            const int astCount = 1000;
            ulong random = 0x4D595DF4D0F33173UL;
            GameplayTagContainer[] subjects = new GameplayTagContainer[8];
            for (int mask = 0; mask < subjects.Length; mask++)
            {
                subjects[mask] = new GameplayTagContainer();
                if ((mask & 1) != 0) subjects[mask].AddTag(_a);
                if ((mask & 2) != 0) subjects[mask].AddTag(_b);
                if ((mask & 4) != 0) subjects[mask].AddTag(_c);
            }

            int mismatchCount = 0;
            string firstMismatch = null;
            for (int astIndex = 0; astIndex < astCount; astIndex++)
            {
                GameplayTagQueryExpression root = GenerateAst(ref random, astIndex % 4);
                var bitmaskQuery = new GameplayTagQuery { RootExpression = root };
                var spanQuery = new GameplayTagQuery { RootExpression = ForceSpanStack(root) };
                for (int mask = 0; mask < subjects.Length; mask++)
                {
                    bool expected = ReferenceEvaluate(root, mask);
                    bool bitmask = bitmaskQuery.Matches(subjects[mask]);
                    bool span = spanQuery.Matches(subjects[mask]);
                    if (bitmask != expected || span != expected)
                    {
                        mismatchCount++;
                        if (firstMismatch == null)
                        {
                            firstMismatch =
                                $"ast={astIndex}, subjectMask={mask}, expected={expected}, " +
                                $"bitmask={bitmask}, span={span}";
                        }
                    }
                }
            }

            TestContext.WriteLine(
                $"[DeepAudit][QueryOracle] evaluated {astCount} deterministic ASTs x 8 subjects x " +
                $"bitmask/Span; mismatches={mismatchCount}; first={firstMismatch ?? "none"}");
            Assert.That(mismatchCount, Is.Zero,
                $"Three-way query mismatches across {astCount * 8} AST/subject pairs. First: {firstMismatch}. " +
                "This is a deterministic sample, not an exhaustive enumeration of all ASTs.");
        }

        [Test]
        public void CountContainer_IntOverflow_IsRejectedWithoutPartialMutation()
        {
            var counts = new GameplayTagCountContainer();
            counts.AddTag(_a, int.MaxValue);
            Exception error = null;
            try
            {
                counts.AddTag(_a, 1);
            }
            catch (Exception exception)
            {
                error = exception;
            }

            TestContext.WriteLine(
                $"[DeepAudit][CountOverflow] error={error?.GetType().Name ?? "none"}, " +
                $"explicit={counts.GetExplicitTagCount(_a)}, total={counts.GetTagCount(_a)}, " +
                $"explicitTags={counts.ExplicitTagCount}");
            Multiple(() =>
            {
                Assert.That(error, Is.TypeOf<OverflowException>(),
                    "int.MaxValue + 1 must be rejected before mutation.");
                Assert.That(counts.GetExplicitTagCount(_a), Is.EqualTo(int.MaxValue));
                Assert.That(counts.GetTagCount(_a), Is.EqualTo(int.MaxValue));
                Assert.That(counts.ExplicitTagCount, Is.EqualTo(1));
            });
        }

        [Test]
        public void Container_ForeignLargeRuntimeIndex_IsRejectedAtomically()
        {
            var donor = new GameplayTagRegistryBuilder().Build();
            var names = new List<string>(512);
            for (int i = 0; i < 512; i++) names.Add($"Foreign{i:0000}");
            donor.RegisterDynamicTags(names);
            GameplayTag foreign = donor.Request("Foreign0511");
            var target = new GameplayTagContainer();
            Exception error = null;
            try
            {
                target.AddTag(foreign);
            }
            catch (Exception exception)
            {
                error = exception;
            }

            TestContext.WriteLine(
                $"[DeepAudit][ForeignIndex] index={foreign.RuntimeIndex}, " +
                $"error={error?.GetType().Name ?? "none"}, explicit={target.ExplicitTagCount}, total={target.TagCount}");
            Multiple(() =>
            {
                Assert.That(error, Is.Not.Null, "An index outside the ambient registry must be rejected.");
                Assert.That(target.ExplicitTagCount, Is.Zero, "A rejected AddTag must not retain an explicit insertion.");
                Assert.That(target.TagCount, Is.Zero, "A rejected AddTag must not retain an implicit insertion.");
                Assert.That(target.IsEmpty, Is.True);
            });
        }

        [Test]
        public void Hierarchy_GetChildTags_ReturnsHeldDirectChildrenInAscendingIndexOrder()
        {
            GameplayTag root = GameplayTagManager.Request("DeepAudit.Hierarchy.Root");
            GameplayTag child = GameplayTagManager.Request("DeepAudit.Hierarchy.Root.Child");
            GameplayTag grand = GameplayTagManager.Request("DeepAudit.Hierarchy.Root.Child.Grand");
            var container = Tags(child, grand);
            var children = new List<GameplayTag>();
            container.GetChildTags(root, children);

            CollectionAssert.AreEqual(new[] { child }, children,
                "GetChildTags returns held direct children only, without a recursive descendant walk.");
            children.Clear();
            container.GetChildTags(child, children);
            CollectionAssert.AreEqual(new[] { grand }, children);
        }

        [Test]
        public void ReadOnlySnapshot_UsesSourceDiRegistry_ForEpochAndHierarchy()
        {
            var registry = new GameplayTagRegistryBuilder().Build();
            registry.RegisterDynamicTag("DeepAudit.DI.Root.Child");
            registry.Reload(false); // Make its epoch observably different from the ambient registry.
            GameplayTag root = registry.Request("DeepAudit.DI.Root");
            GameplayTag child = registry.Request("DeepAudit.DI.Root.Child");
            var source = new GameplayTagContainer(registry);
            source.AddTag(child);

            var snapshot = new ReadOnlyGameplayTagContainer(source);
            var children = new List<GameplayTag>();
            snapshot.GetChildTags(root, children);

            Multiple(() =>
            {
                Assert.That(snapshot.RuntimeIndexEpoch, Is.EqualTo(registry.RuntimeIndexEpoch),
                    "The frozen view must capture the source container's registry, not the ambient one.");
                CollectionAssert.AreEqual(new[] { child }, children,
                    "Hierarchy lookup must use the DI registry that supplied the indices.");
            });
        }

        [Test]
        public void SerializableBridge_NonPreservingReload_ReresolvesDurableNames()
        {
            var bridge = new SerializableGameplayTagContainer();
            bridge.LoadPersisted(new[] { "DeepAudit.Serializable.Root.Child" });
            Assert.That(bridge.ExplicitTagCount, Is.EqualTo(1));

            GameplayTagManager.Reload(false);
            GameplayTag current = GameplayTagManager.Request("DeepAudit.Serializable.Root.Child");

            Multiple(() =>
            {
                Assert.That(bridge.ExplicitTagCount, Is.EqualTo(1));
                Assert.That(bridge.GetExplicitTag(0).RuntimeIndex, Is.EqualTo(current.RuntimeIndex));
                Assert.That(bridge.Container.HasTagExact(current), Is.True);
                Assert.That(bridge.ExplicitTagNames, Is.EqualTo(new[] { "DeepAudit.Serializable.Root.Child" }));
            });
        }

        [Test]
        public void GameObjectContainer_PersistentTagsField_IsUnitySerialized()
        {
            var gameObject = new GameObject("DeepAudit.GameObjectPersistence");
            try
            {
                var component = gameObject.AddComponent<GameObjectGameplayTagContainer>();
                using var serialized = new SerializedObject(component);
                SerializedProperty persistent = serialized.FindProperty("m_PersistentTags");
                Assert.That(persistent, Is.Not.Null,
                    "The Inspector and Unity persistence must be able to see the configured persistent tags.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void Redirect_OnlyChange_ParticipatesInManifestHash()
        {
            ulong before = GameplayTagManager.ManifestHash;
            GameplayTagRedirector.AddRedirect("DeepAudit.A", "DeepAudit.B");
            ulong after = GameplayTagManager.ManifestHash;
            TestContext.WriteLine($"[DeepAudit][RedirectHash] before={before:X16} after={after:X16}");
            Assert.That(after, Is.Not.EqualTo(before),
                "Redirect-only manifest drift must be visible to network handshake participants.");
        }

        [Test]
        public void Redirect_Request_ResolvesRegisteredAlias()
        {
            GameplayTagRedirector.AddRedirect("DeepAudit.A", "DeepAudit.B");
            Assert.That(GameplayTagManager.Request("DeepAudit.A", logWarningIfNotFound: false).RuntimeIndex,
                Is.EqualTo(_b.RuntimeIndex));
        }

        [Test]
        public void Registries_SameNamesDifferentIndexHistory_ShareStableIdentityNotRuntimeIndex()
        {
            var first = new GameplayTagRegistryBuilder().Build();
            first.RegisterDynamicTags(new[] { "DeepAuditSharedA", "DeepAuditSharedB" });
            var second = new GameplayTagRegistryBuilder().Build();
            second.RegisterDynamicTag("DeepAuditSharedB");
            _ = second.Snapshot; // Publish B before A so the second registry has a different index history.
            second.RegisterDynamicTag("DeepAuditSharedA");
            _ = second.Snapshot;

            GameplayTag firstA = first.Request("DeepAuditSharedA");
            GameplayTag secondA = second.Request("DeepAuditSharedA");
            ulong stableA = first.Snapshot.GetStableId(firstA.RuntimeIndex);
            Multiple(() =>
            {
                Assert.That(first.ManifestHash, Is.EqualTo(second.ManifestHash));
                Assert.That(secondA.RuntimeIndex, Is.Not.EqualTo(firstA.RuntimeIndex),
                    "Different preserve/registration histories may assign different indices.");
                Assert.That(second.TryGetByStableId(stableA, out GameplayTag resolved), Is.True);
                Assert.That(second.GetName(resolved), Is.EqualTo("DeepAuditSharedA"));
            });
        }

        [Test]
        public void CountContainer_DrainedAndCleared_RetainsBackingArraysForInspection()
        {
            const int tagCount = 512;
            var names = new List<string>(tagCount);
            for (int i = 0; i < tagCount; i++) names.Add($"Retained{i:0000}");
            GameplayTagManager.RegisterDynamicTags(names);
            var counts = new GameplayTagCountContainer();
            for (int i = 0; i < tagCount; i++) counts.AddTag(GameplayTagManager.Request(names[i]));
            for (int i = 0; i < tagCount; i++) counts.RemoveTag(GameplayTagManager.Request(names[i]));

            int drainedExplicit = ArrayLength(counts, "m_ExplicitIndices");
            int drainedImplicit = ArrayLength(counts, "m_ImplicitIndices");
            Assert.That(counts.IsEmpty, Is.True);
            counts.Clear();
            int clearedExplicit = ArrayLength(counts, "m_ExplicitIndices");
            int clearedImplicit = ArrayLength(counts, "m_ImplicitIndices");
            TestContext.WriteLine(
                $"[DeepAudit][RetainedArrays] drainedExplicit={drainedExplicit}, drainedImplicit={drainedImplicit}, " +
                $"afterClearExplicit={clearedExplicit}, afterClearImplicit={clearedImplicit}; " +
                "zero-GC steady state does not imply minimum retained memory.");

            Multiple(() =>
            {
                Assert.That(drainedExplicit, Is.GreaterThanOrEqualTo(tagCount));
                Assert.That(drainedImplicit, Is.GreaterThanOrEqualTo(tagCount));
                Assert.That(clearedExplicit, Is.EqualTo(drainedExplicit));
                Assert.That(clearedImplicit, Is.EqualTo(drainedImplicit));
            });
        }

        [Test]
        public void Query_NestedMatchAllLeaves_BitmaskClearsStaleTrueBitsOnFalsePush()
        {
            GameplayTagQueryExpression root = All(
                All(Leaf(EGameplayTagQueryExprOperator.All, _a),
                    Leaf(EGameplayTagQueryExprOperator.All, _b)),
                Leaf(EGameplayTagQueryExprOperator.All, _c));
            GameplayTagContainer subject = Tags(_a, _b);
            bool bitmask = new GameplayTagQuery { RootExpression = root }.Matches(subject);
            bool span = new GameplayTagQuery { RootExpression = ForceSpanStack(root) }.Matches(subject);
            TestContext.WriteLine(
                $"[DeepAudit][QueryMatchAllStaleBit] expected=False bitmask={bitmask} span={span}");
            Assert.That(bitmask, Is.False);
            Assert.That(span, Is.False);
        }

        [Test]
        public void Registry_InfiniteDuplicateDynamicInput_StopsAtAttemptBudgetWithoutChangingSnapshot()
        {
            int attemptBudget = GameplayTagRegistrationContext.DefaultMaxRegistrationAttemptCount;
            var registry = new GameplayTagRegistryBuilder().Build();
            registry.RegisterDynamicTag("InputBaseline");
            _ = registry.Snapshot;
            int generation = registry.Generation;
            int epoch = registry.RuntimeIndexEpoch;
            ulong manifest = registry.ManifestHash;
            int yielded = 0;
            int guard = attemptBudget + 10;

            IEnumerable<string> InfiniteDuplicate()
            {
                while (true)
                {
                    yielded++;
                    if (yielded > guard)
                        throw new InputGuardException("The dynamic input did not stop at the attempt budget.");
                    yield return "RepeatedInput";
                }
            }

            Exception error = null;
            try
            {
                registry.RegisterDynamicTags(InfiniteDuplicate());
            }
            catch (Exception exception)
            {
                error = exception;
            }

            TestContext.WriteLine(
                $"[DeepAudit][InfiniteDynamicInput] budget={attemptBudget}, yielded={yielded}, " +
                $"error={error?.GetType().Name ?? "none"}");
            Assert.That(registry.Generation, Is.EqualTo(generation));
            Assert.That(registry.RuntimeIndexEpoch, Is.EqualTo(epoch));
            Assert.That(registry.ManifestHash, Is.EqualTo(manifest));
            Assert.That(registry.TagCount, Is.EqualTo(1));
            Assert.That(error, Is.TypeOf<InvalidOperationException>());
            Assert.That(error, Is.Not.TypeOf<InputGuardException>(),
                "Reaching the guard proves the enumerable was materialized without an early budget stop.");
            Assert.That(yielded, Is.LessThanOrEqualTo(attemptBudget + 1));
        }

        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(100)]
        public void Registry_UniqueDynamicInputLimit_StopsAtFirstOverflowWithoutChangingSnapshot(int inputCount)
        {
            var registry = new GameplayTagRegistryBuilder()
                .SetMaxRegisteredTagCount(4)
                .Build();
            registry.RegisterDynamicTag("InputBaseline");
            _ = registry.Snapshot;
            int generation = registry.Generation;
            int epoch = registry.RuntimeIndexEpoch;
            ulong manifest = registry.ManifestHash;
            int yielded = 0;

            IEnumerable<string> FiniteUniqueInput()
            {
                for (int i = 0; i < inputCount; i++)
                {
                    yielded++;
                    yield return $"Dynamic{i:0000}";
                }
            }

            Exception error = null;
            try
            {
                registry.RegisterDynamicTags(FiniteUniqueInput());
            }
            catch (Exception exception)
            {
                error = exception;
            }

            TestContext.WriteLine(
                $"[DeepAudit][DynamicInputLimit] input={inputCount}, yielded={yielded}, " +
                $"error={error?.GetType().Name ?? "none"}, tags={registry.TagCount}");
            if (inputCount <= 3)
            {
                Assert.That(error, Is.Null);
                Assert.That(yielded, Is.EqualTo(inputCount));
                Assert.That(registry.TagCount, Is.EqualTo(1 + inputCount));
                return;
            }

            Assert.That(registry.Generation, Is.EqualTo(generation));
            Assert.That(registry.RuntimeIndexEpoch, Is.EqualTo(epoch));
            Assert.That(registry.ManifestHash, Is.EqualTo(manifest));
            Assert.That(registry.TagCount, Is.EqualTo(1));
            Assert.That(error, Is.TypeOf<InvalidOperationException>());
            Assert.That(yielded, Is.EqualTo(4),
                "With one baseline tag and a cap of four, enumeration must stop on the fourth new candidate.");
        }

        [Test]
        public void CountContainer_SharedParentOverflow_IsAtomicAndDoesNotNotifyOrAdvanceEpoch()
        {
            GameplayTag parent = GameplayTagManager.Request("DeepAudit.Hierarchy.Root");
            GameplayTag child = GameplayTagManager.Request("DeepAudit.Hierarchy.Root.Child");
            GameplayTag grand = GameplayTagManager.Request("DeepAudit.Hierarchy.Root.Child.Grand");
            var counts = new GameplayTagCountContainer();
            counts.AddTag(child, int.MaxValue);
            int epoch = counts.RuntimeIndexEpoch;
            int notifications = 0;
            counts.OnAnyTagCountChange += (tag, count) => notifications++;

            Assert.Throws<OverflowException>(() => counts.AddTag(grand, 1));

            Multiple(() =>
            {
                Assert.That(counts.GetTagCount(parent), Is.EqualTo(int.MaxValue));
                Assert.That(counts.GetTagCount(child), Is.EqualTo(int.MaxValue));
                Assert.That(counts.GetTagCount(grand), Is.Zero);
                Assert.That(counts.GetExplicitTagCount(grand), Is.Zero);
                Assert.That(counts.ExplicitTagCount, Is.EqualTo(1));
                Assert.That(counts.RuntimeIndexEpoch, Is.EqualTo(epoch));
                Assert.That(notifications, Is.Zero);
            });
        }

        [Test]
        public void CountContainer_NegativeRemoval_KeepsExceptionContractAndState()
        {
            var counts = new GameplayTagCountContainer();
            counts.AddTag(_a, 2);
            int epoch = counts.RuntimeIndexEpoch;
            int notifications = 0;
            counts.OnAnyTagCountChange += (tag, count) => notifications++;

            Assert.Throws<InvalidOperationException>(() => counts.RemoveTag(_a, int.MaxValue));

            Multiple(() =>
            {
                Assert.That(counts.GetExplicitTagCount(_a), Is.EqualTo(2));
                Assert.That(counts.GetTagCount(_a), Is.EqualTo(2));
                Assert.That(counts.ExplicitTagCount, Is.EqualTo(1));
                Assert.That(counts.RuntimeIndexEpoch, Is.EqualTo(epoch));
                Assert.That(notifications, Is.Zero);
            });
        }

        [Test]
        public void CountContainer_ClearAndReleaseMemory_ReleasesAllBackingArrays()
        {
            var counts = new GameplayTagCountContainer();
            counts.AddTag(_a);
            Assert.That(ArrayLength(counts, "m_ExplicitIndices"), Is.GreaterThan(0));
            Assert.That(ArrayLength(counts, "m_ImplicitIndices"), Is.GreaterThan(0));
            Assert.That(ArrayLength(counts, "m_BatchIndices"), Is.GreaterThan(0));

            counts.ClearAndReleaseMemory();

            Multiple(() =>
            {
                Assert.That(counts.IsEmpty, Is.True);
                Assert.That(counts.TagCount, Is.Zero);
                Assert.That(counts.RuntimeIndexEpoch, Is.Zero);
                Assert.That(ArrayLength(counts, "m_ExplicitIndices"), Is.Zero);
                Assert.That(ArrayLength(counts, "m_ExplicitCounts"), Is.Zero);
                Assert.That(ArrayLength(counts, "m_ImplicitIndices"), Is.Zero);
                Assert.That(ArrayLength(counts, "m_ImplicitCounts"), Is.Zero);
                Assert.That(ArrayLength(counts, "m_BatchIndices"), Is.Zero);
                Assert.That(ArrayLength(counts, "m_BatchExplicitDeltas"), Is.Zero);
                Assert.That(ArrayLength(counts, "m_BatchTotalDeltas"), Is.Zero);
            });
        }

        [Test]
        public void Container_CrossRegistrySources_AreRejectedBeforeAnyMutation()
        {
            var firstRegistry = new GameplayTagRegistryBuilder().Build();
            firstRegistry.RegisterDynamicTag("CrossRegistry.First");
            var secondRegistry = new GameplayTagRegistryBuilder().Build();
            secondRegistry.RegisterDynamicTag("CrossRegistry.Second");
            GameplayTag first = firstRegistry.Request("CrossRegistry.First");
            GameplayTag second = secondRegistry.Request("CrossRegistry.Second");
            var source = new GameplayTagContainer(secondRegistry);
            source.AddTag(second);
            var target = new GameplayTagContainer(firstRegistry);
            target.AddTag(first);
            int epoch = target.RuntimeIndexEpoch;
            int tagCount = target.TagCount;

            Assert.Throws<ArgumentException>(() => target.AddTags(source));
            Assert.Throws<ArgumentException>(() => target.RemoveTags(source));
            Assert.Throws<ArgumentException>(() => GameplayTagContainer.Copy(target, source));
            Assert.Throws<ArgumentException>(() => target.AddUnion(target, source));
            Assert.Throws<ArgumentException>(() => target.AddIntersection(target, source));

            Multiple(() =>
            {
                Assert.That(target.ExplicitTagCount, Is.EqualTo(1));
                Assert.That(target.GetExplicitTag(0), Is.EqualTo(first));
                Assert.That(target.TagCount, Is.EqualTo(tagCount));
                Assert.That(target.RuntimeIndexEpoch, Is.EqualTo(epoch));
            });
        }

        [Test]
        public void Container_AddTagsWithLateForeignIndex_DoesNotPartiallyMutate()
        {
            var donor = new GameplayTagRegistryBuilder().Build();
            var names = new List<string>(512);
            for (int i = 0; i < 512; i++) names.Add($"LateForeign{i:0000}");
            donor.RegisterDynamicTags(names);
            var source = new ExplicitTagSequence(_a, donor.Request("LateForeign0511"));
            var target = Tags(_b);
            int epoch = target.RuntimeIndexEpoch;

            Assert.Throws<ArgumentException>(() => target.AddTags(source));

            Multiple(() =>
            {
                Assert.That(target.ExplicitTagCount, Is.EqualTo(1));
                Assert.That(target.GetExplicitTag(0), Is.EqualTo(_b));
                Assert.That(target.RuntimeIndexEpoch, Is.EqualTo(epoch));
            });
        }

        [Test]
        public void Redirect_LoadPersisted_ResolvesAlias_AndEmptyRedirectHashPreservesRegistryHash()
        {
            ulong registryHash = GameplayTagManager.Current.ManifestHash;
            Assert.That(GameplayTagManager.ManifestHash, Is.EqualTo(registryHash));

            GameplayTagRedirector.AddRedirect("DeepAudit.A", "DeepAudit.B");
            ulong redirectedHash = GameplayTagManager.ManifestHash;
            var container = new GameplayTagContainer();
            container.LoadPersisted(new[] { "DeepAudit.A" });

            Multiple(() =>
            {
                Assert.That(container.ExplicitTagCount, Is.EqualTo(1));
                Assert.That(container.GetExplicitTag(0), Is.EqualTo(_b));
                Assert.That(redirectedHash, Is.Not.EqualTo(registryHash));
                Assert.That(GameplayTagManager.ManifestHash, Is.EqualTo(redirectedHash));
            });
        }

        [Test]
        public void DiQueryAndSnapshot_RemainBoundToOwningRegistryAcrossAmbientReload()
        {
            var registry = new GameplayTagRegistryBuilder().Build();
            registry.RegisterDynamicTag("DeepAudit.DI.Query.Root.Child");
            GameplayTag child = registry.Request("DeepAudit.DI.Query.Root.Child");
            var required = new GameplayTagContainer(registry);
            required.AddTag(child);
            var subject = new GameplayTagContainer(registry);
            subject.AddTag(child);
            var query = new GameplayTagQuery
            {
                RootExpression = GameplayTagQueryExpression.MatchAll(required)
            };
            var snapshot = new ReadOnlyGameplayTagContainer(subject);
            Assert.That(query.Matches(subject), Is.True);

            GameplayTagManager.Reload(false);

            Multiple(() =>
            {
                Assert.That(query.Matches(subject), Is.True);
                Assert.That(snapshot.IsCompatibleWithCurrentRegistry, Is.True);
                Assert.That(snapshot.RuntimeIndexEpoch, Is.EqualTo(registry.RuntimeIndexEpoch));
            });

            registry.Reload(false);
            Assert.That(snapshot.IsCompatibleWithCurrentRegistry, Is.False);
        }

        private sealed class InputGuardException : Exception
        {
            internal InputGuardException(string message) : base(message) { }
        }

        private static void Multiple(Action assertions) => assertions();

        private GameplayTagQueryExpression GenerateAst(ref ulong random, int depth)
        {
            if (depth == 0 || Next(ref random) % 4 == 0)
            {
                int count = (int)(Next(ref random) % 3);
                int first = (int)(Next(ref random) % 3);
                int second = (first + 1 + (int)(Next(ref random) % 2)) % 3;
                GameplayTag firstTag = first == 0 ? _a : first == 1 ? _b : _c;
                GameplayTag secondTag = second == 0 ? _a : second == 1 ? _b : _c;
                var op = OperatorFrom(Next(ref random));
                return count == 0 ? Leaf(op) : count == 1 ? Leaf(op, firstTag) : Leaf(op, firstTag, secondTag);
            }

            int childCount = 2 + (int)(Next(ref random) % 2);
            var children = new List<GameplayTagQueryExpression>(childCount);
            for (int i = 0; i < childCount; i++) children.Add(GenerateAst(ref random, depth - 1));
            return new GameplayTagQueryExpression
            {
                Operator = OperatorFrom(Next(ref random)),
                Expressions = children
            };
        }

        private static uint Next(ref ulong random)
        {
            random = unchecked(random * 6364136223846793005UL + 1442695040888963407UL);
            return (uint)(random >> 33);
        }

        private static EGameplayTagQueryExprOperator OperatorFrom(uint value)
        {
            switch (value % 3)
            {
                case 0: return EGameplayTagQueryExprOperator.All;
                case 1: return EGameplayTagQueryExprOperator.Any;
                default: return EGameplayTagQueryExprOperator.None;
            }
        }

        private bool ReferenceEvaluate(GameplayTagQueryExpression expression, int subjectMask)
        {
            if (expression == null) return false;
            bool hasTags = expression.Tags != null && !expression.Tags.IsEmpty;
            bool hasExpressions = expression.Expressions != null && expression.Expressions.Count > 0;
            if (hasTags)
            {
                bool any = false;
                bool all = true;
                for (int i = 0; i < expression.Tags.ExplicitTagCount; i++)
                {
                    GameplayTag tag = expression.Tags.GetExplicitTag(i);
                    int bit = tag.RuntimeIndex == _a.RuntimeIndex ? 1 : tag.RuntimeIndex == _b.RuntimeIndex ? 2 : 4;
                    bool held = (subjectMask & bit) != 0;
                    any |= held;
                    all &= held;
                }
                return expression.Operator == EGameplayTagQueryExprOperator.All ? all :
                    expression.Operator == EGameplayTagQueryExprOperator.Any ? any : !any;
            }

            if (hasExpressions)
            {
                bool any = false;
                bool all = true;
                foreach (GameplayTagQueryExpression child in expression.Expressions)
                {
                    bool value = ReferenceEvaluate(child, subjectMask);
                    any |= value;
                    all &= value;
                }
                return expression.Operator == EGameplayTagQueryExprOperator.All ? all :
                    expression.Operator == EGameplayTagQueryExprOperator.Any ? any : !any;
            }

            return expression.Operator != EGameplayTagQueryExprOperator.Any;
        }

        private static GameplayTagQueryExpression ForceSpanStack(GameplayTagQueryExpression root)
        {
            var children = new List<GameplayTagQueryExpression>(66) { root };
            for (int i = 0; i < 65; i++) children.Add(Leaf(EGameplayTagQueryExprOperator.All));
            return new GameplayTagQueryExpression
            {
                Operator = EGameplayTagQueryExprOperator.All,
                Expressions = children
            };
        }

        private static GameplayTagQueryExpression All(params GameplayTagQueryExpression[] children)
            => GameplayTagQueryExpression.All(children);

        private static GameplayTagQueryExpression Leaf(
            EGameplayTagQueryExprOperator op,
            params GameplayTag[] tags)
        {
            return new GameplayTagQueryExpression
            {
                Operator = op,
                Tags = Tags(tags)
            };
        }

        private static GameplayTagContainer Tags(params GameplayTag[] tags)
        {
            var container = new GameplayTagContainer();
            for (int i = 0; i < tags.Length; i++) container.AddTag(tags[i]);
            return container;
        }

        private sealed class ExplicitTagSequence : IReadOnlyGameplayTagContainer
        {
            private readonly int[] _indices;

            internal ExplicitTagSequence(params GameplayTag[] tags)
            {
                _indices = new int[tags.Length];
                for (int i = 0; i < tags.Length; i++) _indices[i] = tags[i].RuntimeIndex;
            }

            public bool IsEmpty => _indices.Length == 0;
            public int ExplicitTagCount => _indices.Length;
            public int TagCount => _indices.Length;
            public GameplayTag GetTag(int index) => new(_indices[index]);
            public GameplayTag GetExplicitTag(int index) => new(_indices[index]);
            public GameplayTagEnumerator GetTags() => new(_indices, _indices.Length);
            public GameplayTagEnumerator GetExplicitTags() => new(_indices, _indices.Length);
            public void GetParentTags(GameplayTag tag, List<GameplayTag> parentTags) { }
            public void GetChildTags(GameplayTag tag, List<GameplayTag> childTags) { }
            public void GetExplicitParentTags(GameplayTag tag, List<GameplayTag> parentTags) { }
            public void GetExplicitChildTags(GameplayTag tag, List<GameplayTag> childTags) { }
            public bool ContainsRuntimeIndex(int runtimeIndex, bool explicitOnly)
                => Array.BinarySearch(_indices, runtimeIndex) >= 0;
            public GameplayTagEnumerator GetEnumerator() => new(_indices, _indices.Length);
            IEnumerator<GameplayTag> IEnumerable<GameplayTag>.GetEnumerator() => GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private static int ArrayLength(GameplayTagCountContainer container, string fieldName)
        {
            FieldInfo field = typeof(GameplayTagCountContainer).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            return ((int[])field.GetValue(container)).Length;
        }
    }
}
