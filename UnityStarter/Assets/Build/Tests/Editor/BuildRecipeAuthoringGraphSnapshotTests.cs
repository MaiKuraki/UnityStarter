using System;
using System.Collections.Generic;
using System.Linq;
using Build.Pipeline.Editor;
using NUnit.Framework;

namespace Build.Pipeline.Tests.Editor
{
    public sealed class BuildRecipeAuthoringGraphSnapshotTests
    {
        [Test]
        public void Snapshot_CycleQueriesUseCaseInsensitiveReverseReachability()
        {
            BuildRecipeAuthoringGraphSnapshot snapshot = CreateSnapshot(
                Node("root", "root-step"),
                Node("middle", "middle-step", "ROOT"),
                Node("leaf", "leaf-step", "middle"));

            Assert.That(snapshot.WouldCreateDependencyCycle(0, "LEAF"), Is.True);
            Assert.That(snapshot.WouldCreateDependencyCycle(1, "leaf"), Is.True);
            Assert.That(snapshot.WouldCreateDependencyCycle(2, "root"), Is.False);
            Assert.That(snapshot.WouldCreateDependencyCycle(0, "missing"), Is.False);
            Assert.That(snapshot.WouldCreateDependencyCycle(0, "Root"), Is.True);
        }

        [Test]
        public void Snapshot_DuplicateIdsKeepFirstLookupAndMissingEdgesRemainCounted()
        {
            BuildRecipeAuthoringGraphSnapshot snapshot = CreateSnapshot(
                Node("alpha", "first-type", "missing"),
                Node("ALPHA", "second-type"),
                Node("consumer", "consumer-type", "Alpha", "MISSING"));

            Assert.That(snapshot.FindInvocationIndex("ALPHA"), Is.EqualTo(0));
            Assert.That(
                snapshot.IsInvocationIdConfiguredAtAnotherIndex("alpha", 0),
                Is.True);
            Assert.That(
                snapshot.IsInvocationIdConfiguredAtAnotherIndex("alpha", 1),
                Is.True);
            Assert.That(snapshot.CountDependencyReferences("alpha", -1), Is.EqualTo(1));
            Assert.That(snapshot.CountDependencyReferences("missing", -1), Is.EqualTo(2));
            Assert.That(snapshot.CountDependencyReferences("missing", 0), Is.EqualTo(1));
            Assert.That(snapshot.WouldCreateDependencyCycle(1, "consumer"), Is.True);
        }

        [Test]
        public void Snapshot_DependencyCandidatesReuseCachedCycleSafetyAndDirectCounts()
        {
            BuildRecipeAuthoringGraphSnapshot snapshot = CreateSnapshot(
                Node("owner", "owner-step", "used"),
                Node("used", "used-step"),
                Node("cycle", "cycle-step", "owner"),
                Node("free", "free-step"));

            CollectionAssert.AreEqual(
                new[] { "free" },
                snapshot.GetAvailableDependencyIds(0, currentDependencyId: null));
            CollectionAssert.AreEqual(
                new[] { "used", "free" },
                snapshot.GetAvailableDependencyIds(0, "USED"));
            Assert.That(snapshot.FindFirstAvailableDependencyId(0), Is.EqualTo("free"));
            Assert.That(snapshot.WouldCreateDependencyCycle(0, "cycle"), Is.True);
            Assert.That(snapshot.WouldCreateDependencyCycle(0, "free"), Is.False);
        }

        [Test]
        public void Snapshot_StepTypeAndInvocationLookupsAreOrdinalIgnoreCase()
        {
            BuildRecipeAuthoringGraphSnapshot snapshot = CreateSnapshot(
                Node("first", "shared-type"),
                Node("second", "SHARED-TYPE"),
                Node("third", "unique-type"));

            Assert.That(
                snapshot.IsStepTypeConfiguredAtAnotherIndex("Shared-Type", 0),
                Is.True);
            Assert.That(
                snapshot.IsStepTypeConfiguredAtAnotherIndex("unique-TYPE", 2),
                Is.False);
            Assert.That(
                snapshot.IsInvocationIdConfiguredAtAnotherIndex("FIRST", 0),
                Is.False);
            Assert.That(snapshot.FindInvocationIndex("missing"), Is.EqualTo(-1));
        }

        [Test]
        public void Snapshot_RevisionMatchingDetectsGraphValueAndShapeChanges()
        {
            BuildRecipeAuthoringGraphSnapshot snapshot = CreateSnapshot(
                Node("alpha", "alpha-step", "beta"),
                Node("beta", "beta-step"));

            Assert.That(
                snapshot.MatchesInvocation(0, " alpha ", "alpha-step", 1),
                Is.True);
            Assert.That(snapshot.MatchesDependency(0, 0, " beta "), Is.True);
            Assert.That(snapshot.MatchesInvocation(0, "ALPHA", "alpha-step", 1), Is.False);
            Assert.That(snapshot.MatchesInvocation(0, "alpha", "changed-step", 1), Is.False);
            Assert.That(snapshot.MatchesInvocation(0, "alpha", "alpha-step", 0), Is.False);
            Assert.That(snapshot.MatchesDependency(0, 0, "BETA"), Is.False);
            Assert.That(snapshot.MatchesDependency(1, 0, "beta"), Is.False);
        }

        [Test]
        public void Snapshot_MaximumRecipeBuildScansEachSourceNodeAndEdgeOnce()
        {
            int nodeCount = BuildPipelineBudgets.MaximumInvocationCount;
            int edgeBudget = BuildPipelineBudgets.MaximumDependencyEdgeCount;
            var dependencies = new List<string>[nodeCount];
            for (int index = 0; index < dependencies.Length; index++)
            {
                dependencies[index] = new List<string>();
            }

            int edgeCount = 0;
            for (int ownerIndex = 1;
                 ownerIndex < nodeCount && edgeCount < edgeBudget;
                 ownerIndex++)
            {
                for (int targetIndex = 0;
                     targetIndex < ownerIndex && edgeCount < edgeBudget;
                     targetIndex++)
                {
                    dependencies[ownerIndex].Add("node-" + targetIndex);
                    edgeCount++;
                }
            }

            Assert.That(edgeCount, Is.EqualTo(edgeBudget));
            BuildRecipeAuthoringGraphNode[] nodes = Enumerable.Range(0, nodeCount)
                .Select(index => Node(
                    "node-" + index,
                    "step-" + index,
                    dependencies[index].ToArray()))
                .ToArray();

            BuildRecipeAuthoringGraphSnapshot snapshot =
                BuildRecipeAuthoringGraphSnapshot.Create(nodes);
            BuildRecipeAuthoringGraphBuildMetrics metrics = snapshot.BuildMetrics;

            Assert.That(metrics.SourceNodeReadCount, Is.EqualTo(nodeCount));
            Assert.That(metrics.SourceDependencyReadCount, Is.EqualTo(edgeBudget));
            Assert.That(metrics.ReverseReachabilityPassCount, Is.EqualTo(nodeCount));
            Assert.That(
                metrics.ReverseReachabilityNodeVisitCount,
                Is.LessThanOrEqualTo(nodeCount * nodeCount));
            Assert.That(
                metrics.ReverseReachabilityEdgeVisitCount,
                Is.LessThanOrEqualTo(nodeCount * edgeBudget));
            Assert.That(snapshot.InvocationCount, Is.EqualTo(nodeCount));
            Assert.That(snapshot.DependencyCount, Is.EqualTo(edgeBudget));
            Assert.That(snapshot.WouldCreateDependencyCycle(0, "node-91"), Is.True);

            for (int index = 0; index < nodeCount; index++)
            {
                snapshot.FindInvocationIndex("NODE-" + index);
                snapshot.WouldCreateDependencyCycle(index, "node-255");
                snapshot.GetAvailableDependencyIds(index, currentDependencyId: null);
            }

            Assert.That(snapshot.BuildMetrics, Is.EqualTo(metrics));
        }

        [Test]
        public void Snapshot_RejectsInvocationCountAboveSafetyBudgetBeforeGraphAllocation()
        {
            BuildRecipeAuthoringGraphNode[] nodes = Enumerable.Range(
                    0,
                    BuildPipelineBudgets.MaximumInvocationCount + 1)
                .Select(index => Node("node-" + index, "step-" + index))
                .ToArray();

            ArgumentException exception = Assert.Throws<ArgumentException>(() =>
                BuildRecipeAuthoringGraphSnapshot.Create(nodes));

            StringAssert.Contains("invocation safety budget", exception.Message);
        }

        [Test]
        public void Snapshot_RejectsDependencyCountAboveSafetyBudget()
        {
            string[] dependencies = Enumerable.Range(
                    0,
                    BuildPipelineBudgets.MaximumDependencyEdgeCount + 1)
                .Select(index => "dependency-" + index)
                .ToArray();

            ArgumentException exception = Assert.Throws<ArgumentException>(() =>
                BuildRecipeAuthoringGraphSnapshot.Create(new[]
                {
                    Node("owner", "owner-step", dependencies)
                }));

            StringAssert.Contains("edge dependency safety budget", exception.Message);
        }

        private static BuildRecipeAuthoringGraphSnapshot CreateSnapshot(
            params BuildRecipeAuthoringGraphNode[] nodes)
        {
            return BuildRecipeAuthoringGraphSnapshot.Create(nodes);
        }

        private static BuildRecipeAuthoringGraphNode Node(
            string invocationId,
            string stepTypeId,
            params string[] dependencies)
        {
            return new BuildRecipeAuthoringGraphNode(
                invocationId,
                stepTypeId,
                dependencies ?? Array.Empty<string>());
        }
    }
}
