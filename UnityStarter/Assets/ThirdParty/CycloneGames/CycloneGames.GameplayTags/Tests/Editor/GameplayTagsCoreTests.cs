using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CycloneGames.GameplayTags.Core;
using NUnit.Framework;

namespace CycloneGames.GameplayTags.Tests.Editor
{
    public sealed class GameplayTagsCoreTests
    {
        [SetUp]
        public void SetUp()
        {
            GameplayTagManager.ResetForTests();
            GameplayTagRedirector.ClearAll();
            GameplayTagRuntimePlatform.LogWarning = static _ => { };
            GameplayTagRuntimePlatform.LogError = static _ => { };
            GameplayTagRuntimePlatform.IsRuntimePlaying = static () => false;
            GameplayTagRuntimePlatform.LoadBuildTagData = static () => null;
            GameplayTagRuntimePlatform.EnumerateProjectTagSources = static () => System.Array.Empty<IGameplayTagSource>();
            GameplayTagRuntimePlatform.ClearRegisteredProjectTagSources();
        }

        [TearDown]
        public void TearDown()
        {
            GameplayTagManager.ResetForTests();
            GameplayTagRedirector.ClearAll();
            GameplayTagRuntimePlatform.ClearRegisteredProjectTagSources();
        }

        [Test]
        public void DynamicRegistration_BuildsStableHierarchy()
        {
            RegisterTestTags();

            GameplayTag damageFire = GameplayTagManager.RequestTag("Test.Ability.Damage.Fire");
            GameplayTag damage = GameplayTagManager.RequestTag("Test.Ability.Damage");
            GameplayTag ability = GameplayTagManager.RequestTag("Test.Ability");

            Assert.That(damageFire.IsValid, Is.True);
            Assert.That(damageFire.Name, Is.EqualTo("Test.Ability.Damage.Fire"));
            Assert.That(damageFire.Label, Is.EqualTo("Fire"));
            Assert.That(damageFire.HierarchyLevel, Is.EqualTo(4));
            Assert.That(damageFire.ParentTag, Is.EqualTo(damage));
            Assert.That(damageFire.IsChildOf(damage), Is.True);
            Assert.That(ability.IsParentOf(damageFire), Is.True);
            Assert.That(damageFire.MatchesTagDepth(GameplayTagManager.RequestTag("Test.Ability.Damage.Ice")), Is.EqualTo(3));
        }

        [Test]
        public void Container_AddTagIncludesImplicitParentsAndExactLeaf()
        {
            RegisterTestTags();
            GameplayTag damageFire = GameplayTagManager.RequestTag("Test.Ability.Damage.Fire");
            GameplayTag damage = GameplayTagManager.RequestTag("Test.Ability.Damage");
            GameplayTag ability = GameplayTagManager.RequestTag("Test.Ability");

            GameplayTagContainer container = new();
            container.AddTag(damageFire);

            Assert.That(container.ExplicitTagCount, Is.EqualTo(1));
            Assert.That(container.HasTagExact(damageFire), Is.True);
            Assert.That(container.HasTagExact(damage), Is.False);
            Assert.That(container.HasTag(damage), Is.True);
            Assert.That(container.HasTag(ability), Is.True);
        }

        [Test]
        public void Container_UnionAndIntersectionPreserveSortedTagSets()
        {
            RegisterTestTags();
            GameplayTag damageFire = GameplayTagManager.RequestTag("Test.Ability.Damage.Fire");
            GameplayTag damageIce = GameplayTagManager.RequestTag("Test.Ability.Damage.Ice");
            GameplayTag statusStun = GameplayTagManager.RequestTag("Test.Status.Stun");

            GameplayTagContainer lhs = new();
            lhs.AddTag(damageFire);
            lhs.AddTag(statusStun);

            GameplayTagContainer rhs = new();
            rhs.AddTag(damageFire);
            rhs.AddTag(damageIce);

            GameplayTagContainer union = GameplayTagContainer.Union(lhs, rhs);
            GameplayTagContainer intersection = GameplayTagContainer.Intersection(lhs, rhs);

            Assert.That(union.HasTagExact(damageFire), Is.True);
            Assert.That(union.HasTagExact(damageIce), Is.True);
            Assert.That(union.HasTagExact(statusStun), Is.True);
            Assert.That(union.ExplicitTagCount, Is.EqualTo(3));
            Assert.That(intersection.HasTagExact(damageFire), Is.True);
            Assert.That(intersection.ExplicitTagCount, Is.EqualTo(1));
        }

        [Test]
        public void Container_SiblingIntersectionDoesNotRetainImplicitParent()
        {
            RegisterTestTags();
            GameplayTagContainer lhs = new();
            lhs.AddTag(GameplayTagManager.RequestTag("Test.Ability.Damage.Fire"));
            GameplayTagContainer rhs = new();
            rhs.AddTag(GameplayTagManager.RequestTag("Test.Ability.Damage.Ice"));

            GameplayTagContainer intersection = GameplayTagContainer.Intersection(lhs, rhs);

            Assert.That(intersection.IsEmpty, Is.True);
            Assert.That(intersection.ExplicitTagCount, Is.Zero);
            Assert.That(intersection.TagCount, Is.Zero);
        }

        [Test]
        public void ReadOnlySnapshot_HasAnyDoesNotMatchOnlyACommonImplicitParent()
        {
            RegisterTestTags();
            GameplayTag fire = GameplayTagManager.RequestTag("Test.Ability.Damage.Fire");
            GameplayTag ice = GameplayTagManager.RequestTag("Test.Ability.Damage.Ice");

            GameplayTagContainer fireContainer = new();
            fireContainer.AddTag(fire);
            GameplayTagContainer iceContainer = new();
            iceContainer.AddTag(ice);

            ReadOnlyGameplayTagContainer fireSnapshot = fireContainer.CreateSnapshot();
            ReadOnlyGameplayTagContainer iceSnapshot = iceContainer.CreateSnapshot();

            Assert.That(fireSnapshot.HasAny(iceSnapshot), Is.False);
            Assert.That(
                GameplayTagContainerExtensionMethods.HasAny(fireSnapshot, iceSnapshot),
                Is.False);
        }

        [Test]
        public void Container_ParentAndChildQueriesDoNotRequireAdjacentRuntimeIndices()
        {
            GameplayTagManager.RegisterDynamicTags(new[]
            {
                "Test.Status.Alpha.Child",
                "Test.Status.Other",
                "Test.Status.Stun"
            });
            GameplayTagManager.InitializeIfNeeded();

            GameplayTag status = GameplayTagManager.RequestTag("Test.Status");
            GameplayTag stun = GameplayTagManager.RequestTag("Test.Status.Stun");
            GameplayTag other = GameplayTagManager.RequestTag("Test.Status.Other");
            GameplayTagContainer parentSource = new();
            parentSource.AddTag(status);
            parentSource.AddTag(other);
            List<GameplayTag> parents = new();

            parentSource.GetExplicitParentTags(stun, parents);

            Assert.That(parents, Does.Contain(status));

            GameplayTagRuntimePlatform.IsRuntimePlaying = static () => true;
            GameplayTagManager.RegisterDynamicTag("Other.Unrelated");
            GameplayTagManager.RegisterDynamicTag("Test.Status.LateChild");
            GameplayTagContainer childSource = new();
            childSource.AddTag(GameplayTagManager.RequestTag("Test.Status.Alpha.Child"));
            childSource.AddTag(GameplayTagManager.RequestTag("Other.Unrelated"));
            childSource.AddTag(GameplayTagManager.RequestTag("Test.Status.LateChild"));
            List<GameplayTag> children = new();

            childSource.GetExplicitChildTags(status, children);

            Assert.That(children, Does.Contain(GameplayTagManager.RequestTag("Test.Status.Alpha.Child")));
            Assert.That(children, Does.Contain(GameplayTagManager.RequestTag("Test.Status.LateChild")));
        }

        [Test]
        public void CountContainer_TracksExplicitAndImplicitCounts()
        {
            RegisterTestTags();
            GameplayTag damageFire = GameplayTagManager.RequestTag("Test.Ability.Damage.Fire");
            GameplayTag damage = GameplayTagManager.RequestTag("Test.Ability.Damage");
            int anyChangeCount = 0;
            int newOrRemovedCount = 0;

            GameplayTagCountContainer container = new();
            container.RegisterTagEventCallback(damage, GameplayTagEventType.AnyCountChange, (_, _) => anyChangeCount++);
            container.RegisterTagEventCallback(damage, GameplayTagEventType.NewOrRemoved, (_, _) => newOrRemovedCount++);

            container.AddTag(damageFire);
            container.AddTag(damageFire);
            container.RemoveTag(damageFire);
            container.RemoveTag(damageFire);

            Assert.That(container.GetExplicitTagCount(damageFire), Is.EqualTo(0));
            Assert.That(container.GetTagCount(damage), Is.EqualTo(0));
            Assert.That(anyChangeCount, Is.EqualTo(4));
            Assert.That(newOrRemovedCount, Is.EqualTo(2));
        }

        [Test]
        public void CountContainer_StorageScalesWithActiveTagsInsteadOfHighestRuntimeIndex()
        {
            RegisterTestTags();
            GameplayTag leaf = GameplayTagManager.RequestTag("Test.Status.Stun");
            GameplayTagCountContainer container = new();

            container.AddTag(leaf);

            FieldInfo explicitField = typeof(GameplayTagCountContainer).GetField(
                "m_ExplicitTagCounts", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo totalField = typeof(GameplayTagCountContainer).GetField(
                "m_TagCounts", BindingFlags.Instance | BindingFlags.NonPublic);
            Dictionary<int, int> explicitCounts = (Dictionary<int, int>)explicitField.GetValue(container);
            Dictionary<int, int> totalCounts = (Dictionary<int, int>)totalField.GetValue(container);
            Assert.That(explicitCounts.Count, Is.EqualTo(1));
            Assert.That(totalCounts.Count, Is.EqualTo(leaf.HierarchyLevel));
            FieldInfo[] instanceFields = typeof(GameplayTagCountContainer).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(Array.Exists(instanceFields, field => field.FieldType == typeof(int[])), Is.False);
        }

        [Test]
        public void CountContainer_OverflowAndUnderflowValidationIsAtomic()
        {
            RegisterTestTags();
            GameplayTag leaf = GameplayTagManager.RequestTag("Test.Status.Stun");
            GameplayTagCountContainer container = new();
            container.AddTag(leaf);

            FieldInfo explicitField = typeof(GameplayTagCountContainer).GetField(
                "m_ExplicitTagCounts", BindingFlags.Instance | BindingFlags.NonPublic);
            Dictionary<int, int> explicitCounts = (Dictionary<int, int>)explicitField.GetValue(container);
            explicitCounts[leaf.RuntimeIndex] = int.MaxValue;
            int previousHierarchicalCount = container.GetTagCount(leaf);

            Assert.Throws<OverflowException>(() => container.AddTag(leaf));
            Assert.That(container.GetExplicitTagCount(leaf), Is.EqualTo(int.MaxValue));
            Assert.That(container.GetTagCount(leaf), Is.EqualTo(previousHierarchicalCount));

            GameplayTagCountContainer empty = new();
            Assert.Throws<InvalidOperationException>(() => empty.RemoveTag(leaf));
            Assert.That(empty.IsEmpty, Is.True);
        }

        [Test]
        public void CountContainer_BatchValidationFailureDoesNotMutateNotifyOrRetainScratch()
        {
            RegisterTestTags();
            GameplayTag overflowTag = GameplayTagManager.RequestTag("Test.Status.Stun");
            GameplayTag unaffectedTag = GameplayTagManager.RequestTag("Test.Ability.Damage.Fire");
            GameplayTagCountContainer container = new();
            container.AddTag(overflowTag);

            FieldInfo explicitField = typeof(GameplayTagCountContainer).GetField(
                "m_ExplicitTagCounts", BindingFlags.Instance | BindingFlags.NonPublic);
            Dictionary<int, int> explicitCounts = (Dictionary<int, int>)explicitField.GetValue(container);
            explicitCounts[overflowTag.RuntimeIndex] = int.MaxValue;
            int previousOverflowTotal = container.GetTagCount(overflowTag);
            int callbackCount = 0;
            container.OnAnyTagCountChange += (_, _) => callbackCount++;

            GameplayTagContainer batch = new();
            batch.AddTag(overflowTag);
            batch.AddTag(unaffectedTag);

            Assert.Throws<OverflowException>(() => container.AddTags(batch));
            Assert.That(container.GetExplicitTagCount(overflowTag), Is.EqualTo(int.MaxValue));
            Assert.That(container.GetTagCount(overflowTag), Is.EqualTo(previousOverflowTotal));
            Assert.That(container.GetExplicitTagCount(unaffectedTag), Is.Zero);
            Assert.That(container.GetTagCount(unaffectedTag), Is.Zero);
            Assert.That(callbackCount, Is.Zero);
            Assert.That(container.HasRetainedMutationScratch, Is.False);
        }

        [Test]
        public void CountContainer_CallbackFailuresAreAggregatedAfterCommitAndReentryFailsFast()
        {
            RegisterTestTags();
            GameplayTag leaf = GameplayTagManager.RequestTag("Test.Status.Stun");
            GameplayTagCountContainer container = new();
            Exception reentryFailure = null;
            int laterSubscriberCalls = 0;
            container.OnAnyTagCountChange += (_, _) => throw new InvalidOperationException("subscriber failure");
            container.OnAnyTagCountChange += (_, _) =>
            {
                try
                {
                    container.AddTag(leaf);
                }
                catch (Exception exception)
                {
                    reentryFailure = exception;
                }
            };
            container.OnAnyTagCountChange += (_, _) => laterSubscriberCalls++;

            AggregateException callbackFailure = Assert.Throws<AggregateException>(() => container.AddTag(leaf));
            Assert.That(callbackFailure.Message, Does.Contain("state was committed"));
            Assert.That(callbackFailure.InnerExceptions.Count, Is.EqualTo(leaf.HierarchyLevel));
            Assert.That(reentryFailure, Is.TypeOf<InvalidOperationException>());
            Assert.That(laterSubscriberCalls, Is.EqualTo(leaf.HierarchyLevel));
            Assert.That(container.GetExplicitTagCount(leaf), Is.EqualTo(1));
        }

        [Test]
        public void CountContainer_ReloadDuringCallbackUsesOperationSnapshotAndClearReentryFailsFast()
        {
            const string initialSourceName = "Test.CountSnapshot.Initial";
            const string replacementSourceName = "Test.CountSnapshot.Replacement";
            GameplayTagRuntimePlatform.RegisterProjectTagSource(new StaticGameplayTagSource(
                initialSourceName,
                "Mutation.Alpha.Leaf",
                "Mutation.Zulu.Leaf"));
            GameplayTagManager.InitializeIfNeeded();

            GameplayTagContainer added = new();
            added.AddTag(GameplayTagManager.RequestTag("Mutation.Alpha.Leaf"));
            added.AddTag(GameplayTagManager.RequestTag("Mutation.Zulu.Leaf"));
            List<string> expectedNotifications = new();
            foreach (GameplayTag tag in added.GetTags())
            {
                expectedNotifications.Add(tag.Name);
            }

            GameplayTagCountContainer counts = new();
            List<string> actualNotifications = new();
            Exception clearReentryFailure = null;
            bool reloaded = false;
            counts.OnAnyTagCountChange += (tag, _) =>
            {
                actualNotifications.Add(tag.Name);
                if (reloaded)
                {
                    return;
                }

                reloaded = true;
                GameplayTagRuntimePlatform.UnregisterProjectTagSource(initialSourceName);
                GameplayTagRuntimePlatform.RegisterProjectTagSource(new StaticGameplayTagSource(
                    replacementSourceName,
                    "Aardvark.Inserted",
                    "Mutation.Alpha.Leaf",
                    "Mutation.Zulu.Leaf"));
                GameplayTagManager.ReloadTags();
                try
                {
                    counts.Clear();
                }
                catch (Exception exception)
                {
                    clearReentryFailure = exception;
                }
            };

            Assert.DoesNotThrow(() => counts.AddTags(added));
            Assert.That(actualNotifications, Is.EqualTo(expectedNotifications));
            Assert.That(clearReentryFailure, Is.TypeOf<InvalidOperationException>());
            Assert.Throws<InvalidOperationException>(() => _ = counts.IsEmpty);
            Assert.DoesNotThrow(counts.Clear);
            Assert.That(counts.IsEmpty, Is.True);
        }

        [Test]
        public void CountContainer_RemoveAllCallbacksReleasesPerTagAndGlobalSubscribers()
        {
            RegisterTestTags();
            GameplayTag leaf = GameplayTagManager.RequestTag("Test.Status.Stun");
            GameplayTagCountContainer container = new();
            int perTagCalls = 0;
            int globalCountCalls = 0;
            int globalPresenceCalls = 0;

            container.RegisterTagEventCallback(
                leaf,
                GameplayTagEventType.AnyCountChange,
                (_, _) => perTagCalls++);
            container.OnAnyTagCountChange += (_, _) => globalCountCalls++;
            container.OnAnyTagNewOrRemove += (_, _) => globalPresenceCalls++;

            container.RemoveAllTagEventCallbacks();
            container.AddTag(leaf);

            Assert.That(perTagCalls, Is.Zero);
            Assert.That(globalCountCalls, Is.Zero);
            Assert.That(globalPresenceCalls, Is.Zero);
        }

        [Test]
        public void Query_MatchesNestedExpression()
        {
            RegisterTestTags();
            GameplayTag damageFire = GameplayTagManager.RequestTag("Test.Ability.Damage.Fire");
            GameplayTag statusStun = GameplayTagManager.RequestTag("Test.Status.Stun");
            GameplayTag statusRoot = GameplayTagManager.RequestTag("Test.Status");

            GameplayTagContainer subject = new();
            subject.AddTag(damageFire);

            GameplayTagContainer requiredDamage = new();
            requiredDamage.AddTag(damageFire);

            GameplayTagContainer forbiddenStatus = new();
            forbiddenStatus.AddTag(statusStun);

            GameplayTagQuery query = new()
            {
                RootExpression = new GameplayTagQueryExpression
                {
                    Operator = EGameplayTagQueryExprOperator.All,
                    Expressions = new List<GameplayTagQueryExpression>
                    {
                        new()
                        {
                            Operator = EGameplayTagQueryExprOperator.All,
                            Tags = requiredDamage
                        },
                        new()
                        {
                            Operator = EGameplayTagQueryExprOperator.None,
                            Tags = forbiddenStatus
                        }
                    }
                }
            };

            Assert.That(query.Matches(subject), Is.True);
            Assert.That(subject.HasTag(statusRoot), Is.False);

            subject.AddTag(statusStun);

            Assert.That(query.Matches(subject), Is.False);
        }

        [Test]
        public void Query_InvalidateCompiledCache_RecompilesMutatedExpression()
        {
            RegisterTestTags();
            GameplayTag damageFire = GameplayTagManager.RequestTag("Test.Ability.Damage.Fire");
            GameplayTag statusStun = GameplayTagManager.RequestTag("Test.Status.Stun");

            GameplayTagContainer subject = new();
            subject.AddTag(damageFire);

            GameplayTagContainer required = new();
            required.AddTag(damageFire);

            GameplayTagQuery query = new()
            {
                RootExpression = new GameplayTagQueryExpression
                {
                    Operator = EGameplayTagQueryExprOperator.All,
                    Tags = required
                }
            };

            Assert.That(query.Matches(subject), Is.True);

            required.AddTag(statusStun);
            Assert.That(query.Matches(subject), Is.True);

            query.InvalidateCompiledCache();
            Assert.That(query.Matches(subject), Is.False);
        }

        [Test]
        public void Requirements_MatchAcrossStaticAndDynamicContainers()
        {
            RegisterTestTags();
            GameplayTag damageFire = GameplayTagManager.RequestTag("Test.Ability.Damage.Fire");
            GameplayTag statusStun = GameplayTagManager.RequestTag("Test.Status.Stun");

            GameplayTagContainer staticTags = new();
            GameplayTagContainer dynamicTags = new();
            dynamicTags.AddTag(damageFire);

            GameplayTagContainer required = new();
            required.AddTag(damageFire);

            GameplayTagContainer forbidden = new();
            forbidden.AddTag(statusStun);

            GameplayTagRequirements requirements = new(forbidden, required);

            Assert.That(requirements.Matches(staticTags, dynamicTags), Is.True);

            dynamicTags.AddTag(statusStun);

            Assert.That(requirements.Matches(staticTags, dynamicTags), Is.False);
        }

        [Test]
        public void Containers_RejectNoneAndInvalidTags()
        {
            GameplayTagContainer container = new();
            GameplayTagCountContainer countContainer = new();

            Assert.Throws<ArgumentException>(() => container.AddTag(GameplayTag.None));
            Assert.Throws<ArgumentException>(() => countContainer.AddTag(GameplayTag.None));
        }

        [Test]
        public void ContainerUtility_AddTagNames_ConvertsGeneratedStringLists()
        {
            RegisterTestTags();

            string[] tagNames =
            {
                "Test.Ability.Damage.Fire",
                "Test.Status.Stun"
            };

            GameplayTagContainer container = GameplayTagContainerNameExtensions.FromTagNames(tagNames);
            GameplayTagRequirements requirements = GameplayTagContainerNameExtensions.CreateRequirementsFromTagNames(
                Array.Empty<string>(),
                tagNames);

            Assert.That(container.HasTagExact(GameplayTagManager.RequestTag("Test.Ability.Damage.Fire")), Is.True);
            Assert.That(container.HasTagExact(GameplayTagManager.RequestTag("Test.Status.Stun")), Is.True);
            Assert.That(requirements.Matches(container), Is.True);
        }

        [Test]
        public void ContainerUtility_AddDelimitedTagNames_ConvertsDesignerCells()
        {
            RegisterTestTags();

            GameplayTagContainer container = GameplayTagContainerNameExtensions.FromDelimitedTagNames(
                "Test.Ability.Damage.Fire|Test.Status.Stun",
                '|');

            Assert.That(container.HasTagExact(GameplayTagManager.RequestTag("Test.Ability.Damage.Fire")), Is.True);
            Assert.That(container.HasTagExact(GameplayTagManager.RequestTag("Test.Status.Stun")), Is.True);
        }

        [Test]
        public void RuntimePlatform_RegisteredProjectSource_ParticipatesInInitialization()
        {
            GameplayTagRuntimePlatform.RegisterProjectTagSource(new StaticGameplayTagSource(
                "Test.DataTableSource",
                "Table.Ability.Fireball",
                "Table.Effect.Burn"));

            GameplayTagManager.InitializeIfNeeded();

            Assert.That(GameplayTagManager.RequestTag("Table.Ability.Fireball").IsValid, Is.True);
            Assert.That(GameplayTagManager.RequestTag("Table.Effect.Burn").IsValid, Is.True);
        }

        [Test]
        public void RegistryManifest_IgnoresNonAuthoritativeMetadataButTracksIdentity()
        {
            GameplayTagManager.RegisterDynamicTag("Manifest.Tag", "First description", GameplayTagFlags.None);
            GameplayTagManager.InitializeIfNeeded();
            ulong firstManifest = GameplayTagManager.CurrentManifestHash;

            GameplayTagManager.ResetForTests();
            GameplayTagManager.RegisterDynamicTag(
                "Manifest.Tag", "Changed editor description", GameplayTagFlags.HideInEditor);
            GameplayTagManager.InitializeIfNeeded();
            ulong metadataChangedManifest = GameplayTagManager.CurrentManifestHash;

            GameplayTagManager.RegisterDynamicTag("Manifest.Other");

            Assert.That(metadataChangedManifest, Is.EqualTo(firstManifest));
            Assert.That(GameplayTagManager.CurrentManifestHash, Is.Not.EqualTo(firstManifest));
        }

        [Test]
        public void RegistryManifest_IsIndependentOfDynamicRegistrationHistory()
        {
            GameplayTagManager.InitializeIfNeeded();
            GameplayTagManager.RegisterDynamicTag("Manifest.Order.Z");
            GameplayTagManager.RegisterDynamicTag("Manifest.Order.A");
            ulong zThenAManifest = GameplayTagManager.CurrentManifestHash;
            int firstZIndex = GameplayTagManager.RequestTag("Manifest.Order.Z").RuntimeIndex;
            int firstAIndex = GameplayTagManager.RequestTag("Manifest.Order.A").RuntimeIndex;

            GameplayTagManager.ResetForTests();
            GameplayTagManager.InitializeIfNeeded();
            GameplayTagManager.RegisterDynamicTag("Manifest.Order.A");
            GameplayTagManager.RegisterDynamicTag("Manifest.Order.Z");
            ulong aThenZManifest = GameplayTagManager.CurrentManifestHash;
            int secondAIndex = GameplayTagManager.RequestTag("Manifest.Order.A").RuntimeIndex;
            int secondZIndex = GameplayTagManager.RequestTag("Manifest.Order.Z").RuntimeIndex;

            Assert.That(firstZIndex, Is.LessThan(firstAIndex));
            Assert.That(secondAIndex, Is.LessThan(secondZIndex));
            Assert.That(aThenZManifest, Is.EqualTo(zThenAManifest));
        }

        [Test]
        public void RedirectBatch_RejectsUnboundedInputWithoutPublishing()
        {
            Assert.Throws<InvalidOperationException>(() =>
                GameplayTagRedirector.AddRedirects(EnumerateUnboundedRedirectBatch()));

            Assert.That(GameplayTagRedirector.HasRedirect("Redirect.Unbounded.Legacy"), Is.False);
        }

        [Test]
        public void RedirectBatch_RejectsOversizedCollectionWithoutPublishing()
        {
            KeyValuePair<string, string>[] oversizedBatch =
                new KeyValuePair<string, string>[GameplayTagRedirector.MaxRedirectCount + 1];
            for (int i = 0; i < oversizedBatch.Length; i++)
            {
                oversizedBatch[i] = new KeyValuePair<string, string>(
                    "Redirect.Oversized.Legacy",
                    "Redirect.Oversized.Current");
            }

            Assert.Throws<InvalidOperationException>(() =>
                GameplayTagRedirector.AddRedirects(oversizedBatch));

            Assert.That(GameplayTagRedirector.HasRedirect("Redirect.Oversized.Legacy"), Is.False);
        }

        [Test]
        public void RedirectBatch_PreservesMutationPerformedDuringMaterialization()
        {
            GameplayTagRedirector.AddRedirects(EnumerateRedirectBatchWithReentrantMutation());

            Assert.That(
                GameplayTagRedirector.Resolve("Redirect.Reentrant.Legacy"),
                Is.EqualTo("Redirect.Reentrant.Current"));
            Assert.That(
                GameplayTagRedirector.Resolve("Redirect.Batch.Legacy"),
                Is.EqualTo("Redirect.Batch.Current"));
        }

        [Test]
        public void RedirectBatch_InvalidEntryFailsAtomically()
        {
            GameplayTagRedirector.AddRedirect("Redirect.Existing.Legacy", "Redirect.Existing.Current");
            KeyValuePair<string, string>[] invalidBatch =
            {
                new("Redirect.Valid.Legacy", "Redirect.Valid.Current"),
                new("Redirect..Invalid", "Redirect.Invalid.Current")
            };

            Assert.Throws<ArgumentException>(() => GameplayTagRedirector.AddRedirects(invalidBatch));

            Assert.That(
                GameplayTagRedirector.Resolve("Redirect.Existing.Legacy"),
                Is.EqualTo("Redirect.Existing.Current"));
            Assert.That(GameplayTagRedirector.HasRedirect("Redirect.Valid.Legacy"), Is.False);
        }

        [Test]
        public void CountContainer_BatchScratchIsContainerOwnedAndDropsOversizedPeak()
        {
            int tagCount = GameplayTagCountContainer.MaxRetainedMutationScratchEntries + 1;
            string[] names = new string[tagCount];
            for (int i = 0; i < names.Length; i++)
                names[i] = $"Scratch.Tag{i:000}";

            GameplayTagManager.RegisterDynamicTags(names);
            GameplayTagManager.InitializeIfNeeded();

            GameplayTagContainer largeBatch = new();
            for (int i = 0; i < names.Length; i++)
                largeBatch.AddTag(GameplayTagManager.RequestTag(names[i]));

            GameplayTagCountContainer owner = new();
            GameplayTagCountContainer unrelated = new();
            owner.AddTags(largeBatch);

            Assert.That(owner.ExplicitTagCount, Is.EqualTo(tagCount));
            Assert.That(owner.HasRetainedMutationScratch, Is.False);
            Assert.That(unrelated.HasRetainedMutationScratch, Is.False);

            GameplayTagContainer smallBatch = new();
            smallBatch.AddTag(GameplayTagManager.RequestTag(names[0]));
            smallBatch.AddTag(GameplayTagManager.RequestTag(names[1]));
            owner.RemoveTags(smallBatch);

            Assert.That(owner.HasRetainedMutationScratch, Is.True);
            Assert.That(unrelated.HasRetainedMutationScratch, Is.False);

            owner.Clear();
            Assert.That(owner.IsEmpty, Is.True);
            Assert.That(owner.HasRetainedMutationScratch, Is.False);
        }

        [Test]
        public void BitsetPolicy_RequiresEnoughDensityForRetainedMemory()
        {
            Assert.That(
                GameplayTagContainerUtility.ShouldUseBitset(64, 64, out int denseWordCount),
                Is.True);
            Assert.That(denseWordCount, Is.EqualTo(3));

            Assert.That(
                GameplayTagContainerUtility.ShouldUseBitset(
                    64,
                    GameplayTagUtility.MaxRegisteredTagCount,
                    out int sparseWordCount),
                Is.False);
            Assert.That(sparseWordCount, Is.EqualTo(2048));
        }

        [Test]
        public void RegistrationContext_ImplicitParentCapacityFailureIsAtomicAndTerminal()
        {
            GameplayTagRegistrationContext context = new(
                maxRegisteredTagCount: 3,
                maxRegistrationAttemptCount: 6,
                maxRetainedDiagnosticCount: 4);
            Assert.That(context.RegisterTag(
                "CapacityA.Leaf", string.Empty, GameplayTagFlags.None), Is.True);
            Assert.That(context.RegisterTag(
                "CapacityB.Leaf", string.Empty, GameplayTagFlags.None), Is.True);

            List<GameplayTagDefinition> definitions = context.GenerateDefinitions(addNoneTag: true);

            Assert.That(definitions, Is.Null);
            Assert.That(context.IsRegistrationTerminated, Is.True);
            Assert.That(context.RegisteredTagCount, Is.EqualTo(2),
                "Implicit parents must not be partially committed after capacity validation fails.");
            Assert.That(context.RegistrationAttemptCount, Is.EqualTo(2));
            Assert.That(context.RegistrationErrorCount, Is.EqualTo(1));
            Assert.That(context.RegisterTag(
                "CapacityC.Leaf", string.Empty, GameplayTagFlags.None), Is.False);
            Assert.That(context.RegistrationAttemptCount, Is.EqualTo(2),
                "A terminal context must stop accepting registration attempts.");
        }

        [Test]
        public void RegistrationContext_BoundsAttemptsAndRetainedDiagnostics()
        {
            Assert.That(
                GameplayTagRegistrationContext.DefaultMaxRegistrationAttemptCount,
                Is.EqualTo(GameplayTagUtility.MaxRegisteredTagCount * 2));
            Assert.That(
                GameplayTagRegistrationContext.DefaultMaxRetainedDiagnosticCount,
                Is.EqualTo(128));

            GameplayTagRegistrationContext context = new(
                maxRegisteredTagCount: 4,
                maxRegistrationAttemptCount: 5,
                maxRetainedDiagnosticCount: 2);
            for (int i = 0; i < 5; i++)
            {
                Assert.That(context.RegisterTag(
                    "Invalid..Tag" + i, string.Empty, GameplayTagFlags.None), Is.False);
            }

            Assert.That(context.IsRegistrationTerminated, Is.False);
            Assert.That(context.RegistrationAttemptCount, Is.EqualTo(5));
            Assert.That(context.RegistrationErrorCount, Is.EqualTo(5));
            Assert.That(context.SuppressedRegistrationErrorCount, Is.EqualTo(3));

            Assert.That(context.RegisterTag(
                "AttemptLimit.Tag", string.Empty, GameplayTagFlags.None), Is.False);
            Assert.That(context.IsRegistrationTerminated, Is.True);
            Assert.That(context.RegistrationAttemptCount, Is.EqualTo(5));
            Assert.That(context.RegistrationErrorCount, Is.EqualTo(6));

            int retainedDiagnosticCount = 0;
            foreach (GameplayTagRegistrationError _ in context.GetRegistrationErrors())
            {
                retainedDiagnosticCount++;
            }
            Assert.That(retainedDiagnosticCount, Is.EqualTo(3),
                "Two detailed diagnostics plus one terminal diagnostic must be retained.");
        }

        [Test]
        public void RegisterDynamicTags_BoundsNonCountableInputBeforeMutation()
        {
            GameplayTagManager.RegisterDynamicTag("DynamicBudget.Baseline");
            GameplayTagManager.InitializeIfNeeded();
            int generation = GameplayTagManager.CurrentGeneration;
            int yieldedCount = 0;

            IEnumerable<string> EnumerateUnboundedEmptyTags()
            {
                while (true)
                {
                    yieldedCount++;
                    yield return string.Empty;
                }
            }

            Assert.Throws<InvalidOperationException>(() =>
                GameplayTagManager.RegisterDynamicTags(EnumerateUnboundedEmptyTags()));
            Assert.That(
                yieldedCount,
                Is.EqualTo(GameplayTagRegistrationContext.DefaultMaxRegistrationAttemptCount + 1));
            Assert.That(GameplayTagManager.CurrentGeneration, Is.EqualTo(generation));
        }

        [Test]
        public void Query_RejectsCyclesAndAmbiguousNodes()
        {
            RegisterTestTags();
            GameplayTagQueryExpression cycle = new()
            {
                Operator = EGameplayTagQueryExprOperator.All,
                Expressions = new List<GameplayTagQueryExpression>()
            };
            cycle.Expressions.Add(cycle);
            GameplayTagQuery cyclicQuery = new() { RootExpression = cycle };
            Assert.Throws<InvalidOperationException>(() => cyclicQuery.Matches(new GameplayTagContainer()));

            GameplayTagContainer tags = new();
            tags.AddTag(GameplayTagManager.RequestTag("Test.Status.Stun"));
            GameplayTagQuery ambiguousQuery = new()
            {
                RootExpression = new GameplayTagQueryExpression
                {
                    Operator = EGameplayTagQueryExprOperator.All,
                    Tags = tags,
                    Expressions = new List<GameplayTagQueryExpression> { GameplayTagQueryExpression.All() }
                }
            };
            Assert.Throws<InvalidOperationException>(() => ambiguousQuery.Matches(tags));
        }

        [Test]
        public void Query_RejectsNullChildSlotsBeyondNodeBudget()
        {
            RegisterTestTags();
            List<GameplayTagQueryExpression> nullChildren =
                new(GameplayTagQuery.MaxExpressionNodes + 1);
            for (int i = 0; i <= GameplayTagQuery.MaxExpressionNodes; i++)
            {
                nullChildren.Add(null);
            }

            GameplayTagQuery query = new()
            {
                RootExpression = new GameplayTagQueryExpression
                {
                    Operator = EGameplayTagQueryExprOperator.Any,
                    Expressions = nullChildren
                }
            };

            Assert.Throws<InvalidOperationException>(() =>
                query.Matches(new GameplayTagContainer()));
        }

        [Test]
        public void Reload_RuntimePreservesIndicesAndDefersRemovalUntilResetEpoch()
        {
            const string sourceName = "Test.RuntimeReload";
            GameplayTagRuntimePlatform.RegisterProjectTagSource(
                new StaticGameplayTagSource(sourceName, "Runtime.Reload.Keep"));
            GameplayTagManager.InitializeIfNeeded();
            GameplayTag original = GameplayTagManager.RequestTag("Runtime.Reload.Keep");
            int originalEpoch = GameplayTagManager.CurrentRuntimeIndexEpoch;

            GameplayTagRuntimePlatform.UnregisterProjectTagSource(sourceName);
            GameplayTagRuntimePlatform.IsRuntimePlaying = static () => true;
            GameplayTagManager.ReloadTags();

            Assert.That(GameplayTagManager.RequestTag("Runtime.Reload.Keep").RuntimeIndex, Is.EqualTo(original.RuntimeIndex));
            Assert.That(GameplayTagManager.CurrentRuntimeIndexEpoch, Is.EqualTo(originalEpoch));

            GameplayTagRuntimePlatform.IsRuntimePlaying = static () => false;
            GameplayTagManager.ReloadTags();
            Assert.That(GameplayTagManager.TryRequestTag("Runtime.Reload.Keep", out _), Is.False);
            Assert.That(GameplayTagManager.CurrentRuntimeIndexEpoch, Is.Not.EqualTo(originalEpoch));
        }

        [Test]
        public void RuntimeReset_InvalidatesIndexOwnersButAllowsSafeClear()
        {
            RegisterTestTags();
            GameplayTag stun = GameplayTagManager.RequestTag("Test.Status.Stun");
            GameplayTagContainer container = new();
            container.AddTag(stun);
            GameplayTagCountContainer counts = new();
            counts.AddTag(stun);

            GameplayTagManager.ResetForTests();
            GameplayTagManager.RegisterDynamicTag("Reset.Other.Tag");
            GameplayTagManager.InitializeIfNeeded();

            Assert.Throws<InvalidOperationException>(() => _ = container.IsEmpty);
            Assert.Throws<InvalidOperationException>(() => _ = counts.IsEmpty);
            Assert.DoesNotThrow(container.Clear);
            Assert.DoesNotThrow(counts.Clear);
            Assert.That(container.IsEmpty, Is.True);
            Assert.That(counts.IsEmpty, Is.True);
        }

        [Test]
        public void ReadOnlySnapshot_RejectsAllCrossEpochTagOperations()
        {
            RegisterTestTags();
            GameplayTag stun = GameplayTagManager.RequestTag("Test.Status.Stun");
            GameplayTagContainer mutable = new();
            mutable.AddTag(stun);
            ReadOnlyGameplayTagContainer snapshot = mutable.CreateSnapshot();

            GameplayTagManager.ResetForTests();
            GameplayTagManager.RegisterDynamicTag("Reset.Other.Tag");
            GameplayTagManager.InitializeIfNeeded();

            Assert.That(snapshot.IsCompatibleWithCurrentRegistry, Is.False);
            Assert.Throws<InvalidOperationException>(() => snapshot.HasTag(stun));
            Assert.Throws<InvalidOperationException>(() => snapshot.GetExplicitTags());
            IReadOnlyGameplayTagContainer readOnlyView = snapshot;
            Assert.Throws<InvalidOperationException>(() => readOnlyView.HasTag(stun));
            Assert.Throws<InvalidOperationException>(() =>
                GameplayTagContainer.Copy(new GameplayTagContainer(), snapshot));
        }

        [Test]
        public void PublishedDefinition_RemainsBoundToItsImmutableSnapshot()
        {
            const string sourceName = "Test.DefinitionSnapshot";
            GameplayTagRuntimePlatform.RegisterProjectTagSource(new StaticGameplayTagSource(
                sourceName,
                "DefinitionSnapshot.Root.Child"));
            GameplayTagManager.InitializeIfNeeded();
            GameplayTag child = GameplayTagManager.RequestTag("DefinitionSnapshot.Root.Child");
            GameplayTag parent = GameplayTagManager.RequestTag("DefinitionSnapshot.Root");
            GameplayTagDefinition definition = child.Definition;

            GameplayTagRuntimePlatform.UnregisterProjectTagSource(sourceName);
            GameplayTagManager.ReloadTags();

            Assert.That(GameplayTagManager.TryRequestTag("DefinitionSnapshot.Root.Child", out _), Is.False);
            Assert.That(definition.ParentTags.Length, Is.EqualTo(2));
            Assert.That(definition.ParentTags[1].m_Name, Is.EqualTo("DefinitionSnapshot.Root"));
            Assert.That(definition.IsChildOf(parent), Is.True);
        }

        [Test]
        public void Reload_InvalidCandidatePreservesPublishedSnapshot()
        {
            RegisterTestTags();
            int generation = GameplayTagManager.CurrentGeneration;
            ulong manifestHash = GameplayTagManager.CurrentManifestHash;
            GameplayTagRuntimePlatform.RegisterProjectTagSource(new InvalidGameplayTagSource());

            Assert.Throws<InvalidOperationException>(GameplayTagManager.ReloadTags);
            Assert.That(GameplayTagManager.CurrentGeneration, Is.EqualTo(generation));
            Assert.That(GameplayTagManager.CurrentManifestHash, Is.EqualTo(manifestHash));
            Assert.That(GameplayTagManager.RequestTag("Test.Status.Stun").IsValid, Is.True);
        }

        [Test]
        public void DynamicRegistration_BatchesSnapshotRebuildsAfterInitialization()
        {
            RegisterTestTags();

            int treeChangedCount = 0;
            GameplayTagManager.OnGameplayTagTreeChanged += () => treeChangedCount++;

            GameplayTagManager.RegisterDynamicTags(new[]
            {
                "HotUpdate.Ability.Fire",
                "HotUpdate.Ability.Ice"
            });

            Assert.That(treeChangedCount, Is.EqualTo(1));
            Assert.That(GameplayTagManager.RequestTag("HotUpdate.Ability.Fire").IsValid, Is.True);
            Assert.That(GameplayTagManager.RequestTag("HotUpdate.Ability.Ice").IsValid, Is.True);
            Assert.That(GameplayTagManager.RequestTag("HotUpdate.Ability").IsValid, Is.True);
        }

        [Test]
        public void BuildGameplayTagSource_RegistersValidBinaryData()
        {
            byte[] data = CreateBuildTagDataWithMetadata(
                ("Build.Ability", "Ability category", GameplayTagFlags.HideInEditor),
                ("Build.Ability.Fire", "Fire ability", GameplayTagFlags.None),
                ("Build.Status.Stun", "Stun status", GameplayTagFlags.None));
            Assert.That(data[0], Is.EqualTo((byte)'C'));
            Assert.That(data[1], Is.EqualTo((byte)'G'));
            Assert.That(data[2], Is.EqualTo((byte)'T'));
            Assert.That(data[3], Is.EqualTo((byte)'G'));
            GameplayTagRuntimePlatform.LoadBuildTagData = () => data;

            GameplayTagRegistrationContext context = new();
            new BuildGameplayTagSource().RegisterTags(context);
            List<GameplayTagDefinition> definitions = context.GenerateDefinitions(true);

            Assert.That(definitions.Exists(static definition => definition.TagName == "Build.Ability.Fire"), Is.True);
            Assert.That(definitions.Exists(static definition => definition.TagName == "Build.Status.Stun"), Is.True);
            GameplayTagDefinition ability = definitions.Find(static definition => definition.TagName == "Build.Ability");
            Assert.That(ability.Description, Is.EqualTo("Ability category"));
            Assert.That(ability.Flags, Is.EqualTo(GameplayTagFlags.HideInEditor));
        }

        [Test]
        public void BuildGameplayTagSource_RejectsMissingEmptyAndZeroTagData()
        {
            GameplayTagRegistrationContext context = new();
            GameplayTagRuntimePlatform.LoadBuildTagData = static () => null;
            Assert.Throws<InvalidDataException>(() => new BuildGameplayTagSource().RegisterTags(context));

            GameplayTagRuntimePlatform.LoadBuildTagData = static () => Array.Empty<byte>();
            Assert.Throws<InvalidDataException>(() => new BuildGameplayTagSource().RegisterTags(context));

            byte[] zeroTagData = CreateBuildTagData();
            GameplayTagRuntimePlatform.LoadBuildTagData = () => zeroTagData;
            Assert.Throws<InvalidDataException>(() => new BuildGameplayTagSource().RegisterTags(context));
        }

        [Test]
        public void BuildGameplayTagSource_RejectsCorruptedBinaryData()
        {
            byte[] data = CreateBuildTagData("Build.Ability.Fire");
            data[^1] ^= 0xFF;
            GameplayTagRuntimePlatform.LoadBuildTagData = () => data;

            GameplayTagRegistrationContext context = new();
            Assert.Throws<InvalidDataException>(() => new BuildGameplayTagSource().RegisterTags(context));
        }

        [Test]
        public void BuildGameplayTagSource_RejectsInvalidSignature()
        {
            byte[] data = CreateBuildTagData("Build.Ability.Fire");
            data[0] ^= 0xFF;
            GameplayTagRuntimePlatform.LoadBuildTagData = () => data;

            GameplayTagRegistrationContext context = new();
            Assert.Throws<InvalidDataException>(() => new BuildGameplayTagSource().RegisterTags(context));
        }

        [Test]
        public void BuildGameplayTagSource_RejectsTruncatedBinaryData()
        {
            byte[] data = CreateBuildTagData("Build.Ability.Fire");
            Array.Resize(ref data, data.Length - 1);
            GameplayTagRuntimePlatform.LoadBuildTagData = () => data;

            GameplayTagRegistrationContext context = new();
            Assert.Throws<InvalidDataException>(() => new BuildGameplayTagSource().RegisterTags(context));
        }

        [Test]
        public void BuildGameplayTagSource_RejectsTrailingBytes()
        {
            byte[] data = CreateBuildTagData("Build.Ability.Fire");
            Array.Resize(ref data, data.Length + 1);
            GameplayTagRuntimePlatform.LoadBuildTagData = () => data;

            GameplayTagRegistrationContext context = new();
            Assert.Throws<InvalidDataException>(() => new BuildGameplayTagSource().RegisterTags(context));
        }

        [Test]
        public void BuildGameplayTagSource_RejectsDuplicateTags()
        {
            byte[] data = CreateBuildTagData("Build.Ability.Fire", "Build.Ability.Fire");
            GameplayTagRuntimePlatform.LoadBuildTagData = () => data;

            GameplayTagRegistrationContext context = new();
            Assert.Throws<InvalidDataException>(() => new BuildGameplayTagSource().RegisterTags(context));
            Assert.That(context.GenerateDefinitions(false), Is.Empty);
        }

        private static void RegisterTestTags()
        {
            GameplayTagManager.RegisterDynamicTag("Test.Ability.Damage.Fire", "Fire damage");
            GameplayTagManager.RegisterDynamicTag("Test.Ability.Damage.Ice", "Ice damage");
            GameplayTagManager.RegisterDynamicTag("Test.Status.Stun", "Stun status");
            GameplayTagManager.InitializeIfNeeded();
        }

        private sealed class StaticGameplayTagSource : IGameplayTagSource
        {
            private readonly string[] _tagNames;

            public string Name { get; }

            public StaticGameplayTagSource(string name, params string[] tagNames)
            {
                Name = name;
                _tagNames = tagNames ?? Array.Empty<string>();
            }

            public void RegisterTags(GameplayTagRegistrationContext context)
            {
                for (int i = 0; i < _tagNames.Length; i++)
                {
                    context.RegisterTag(_tagNames[i], _tagNames[i], GameplayTagFlags.None, this);
                }
            }
        }

        private sealed class InvalidGameplayTagSource : IGameplayTagSource
        {
            public string Name => "Test.Invalid";

            public void RegisterTags(GameplayTagRegistrationContext context)
            {
                context.RegisterTag("Invalid..Tag", string.Empty, GameplayTagFlags.None, this);
            }
        }

        private static byte[] CreateBuildTagData(params string[] tagNames)
        {
            using MemoryStream memoryStream = new();
            using BinaryWriter writer = new(memoryStream);

            writer.Write(BuildTagBinaryFormat.FileSignature);
            writer.Write(tagNames.Length);
            for (int i = 0; i < tagNames.Length; i++)
            {
                writer.Write(tagNames[i]);
                writer.Write(string.Empty);
                writer.Write((int)GameplayTagFlags.None);
            }

            writer.Flush();
            byte[] dataWithoutHash = memoryStream.ToArray();
            writer.Write(BuildTagBinaryFormat.ComputeContentHash64(dataWithoutHash, 0, dataWithoutHash.Length));

            return memoryStream.ToArray();
        }

        private static byte[] CreateBuildTagDataWithMetadata(
            params (string Name, string Description, GameplayTagFlags Flags)[] entries)
        {
            using MemoryStream memoryStream = new();
            using BinaryWriter writer = new(memoryStream);

            writer.Write(BuildTagBinaryFormat.FileSignature);
            writer.Write(entries.Length);
            for (int i = 0; i < entries.Length; i++)
            {
                BuildTagBinaryFormat.ValidateEntry(entries[i].Name, entries[i].Description, entries[i].Flags);
                writer.Write(entries[i].Name);
                writer.Write(entries[i].Description ?? string.Empty);
                writer.Write((int)entries[i].Flags);
            }

            writer.Flush();
            byte[] dataWithoutHash = memoryStream.ToArray();
            writer.Write(BuildTagBinaryFormat.ComputeContentHash64(
                dataWithoutHash, 0, dataWithoutHash.Length));
            return memoryStream.ToArray();
        }

        private static IEnumerable<KeyValuePair<string, string>> EnumerateUnboundedRedirectBatch()
        {
            while (true)
            {
                yield return new KeyValuePair<string, string>(
                    "Redirect.Unbounded.Legacy",
                    "Redirect.Unbounded.Current");
            }
        }

        private static IEnumerable<KeyValuePair<string, string>> EnumerateRedirectBatchWithReentrantMutation()
        {
            GameplayTagRedirector.AddRedirect(
                "Redirect.Reentrant.Legacy",
                "Redirect.Reentrant.Current");
            yield return new KeyValuePair<string, string>(
                "Redirect.Batch.Legacy",
                "Redirect.Batch.Current");
        }
    }
}
