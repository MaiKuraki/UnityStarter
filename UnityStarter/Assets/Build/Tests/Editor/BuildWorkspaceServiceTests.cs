using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Build.Pipeline.Editor.Tests
{
    public sealed class BuildWorkspaceServiceTests
    {
        private string projectRoot;

        [SetUp]
        public void SetUp()
        {
            projectRoot = Path.Combine(
                Path.GetTempPath(),
                "BuildWorkspaceServiceTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(projectRoot, "Assets"));
            Directory.CreateDirectory(Path.Combine(projectRoot, "ProjectSettings"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void Inspect_WithOnlyInertLockFile_IsCleanAndDoesNotDeleteIt()
        {
            string stateRoot = CreateStateRoot("fake");
            string lockPath = Path.Combine(stateRoot, "build.lock");
            File.WriteAllText(lockPath, "owner");

            BuildWorkspaceSnapshot snapshot = BuildWorkspaceService.Inspect(
                projectRoot,
                new IBuildRecoveryParticipant[] { new FakeParticipant(projectRoot) },
                editorIsBusy: false);

            Assert.That(snapshot.Status, Is.EqualTo(BuildWorkspaceHealthStatus.Clean));
            Assert.That(File.ReadAllText(lockPath), Is.EqualTo("owner"));
        }

        [Test]
        public void Inspect_WithPriorResultManifest_IsCleanAndPreservesHistory()
        {
            string resultsRoot = Path.Combine(projectRoot, ".buildpipeline", "results");
            Directory.CreateDirectory(resultsRoot);
            string manifestPath = Path.Combine(resultsRoot, "failed-other-platform.json");
            File.WriteAllText(manifestPath, "{\"succeeded\":false}");

            BuildWorkspaceSnapshot snapshot = BuildWorkspaceService.Inspect(
                projectRoot,
                Array.Empty<IBuildRecoveryParticipant>(),
                editorIsBusy: false);

            Assert.That(snapshot.Status, Is.EqualTo(BuildWorkspaceHealthStatus.Clean));
            Assert.That(File.ReadAllText(manifestPath), Is.EqualTo("{\"succeeded\":false}"));
        }

        [Test]
        public void Inspect_WithArbitraryLockExtension_RequiresExplicitRecovery()
        {
            string stateRoot = CreateStateRoot("fake");
            string lockPath = Path.Combine(stateRoot, "unexpected.lock");
            File.WriteAllText(lockPath, "owner");

            BuildWorkspaceSnapshot snapshot = BuildWorkspaceService.Inspect(
                projectRoot,
                new IBuildRecoveryParticipant[] { new FakeParticipant(projectRoot) },
                editorIsBusy: false);

            Assert.That(
                snapshot.Status,
                Is.EqualTo(BuildWorkspaceHealthStatus.RecoveryRequired));
            Assert.That(snapshot.Issues[0].EvidencePath, Is.EqualTo(lockPath));
        }

        [Test]
        public void Inspect_WithClaimedEvidence_RequiresExplicitRecovery()
        {
            string evidencePath = Path.Combine(CreateStateRoot("fake"), "active.json");
            File.WriteAllText(evidencePath, "pending");

            BuildWorkspaceSnapshot snapshot = BuildWorkspaceService.Inspect(
                projectRoot,
                new IBuildRecoveryParticipant[] { new FakeParticipant(projectRoot) },
                editorIsBusy: false);

            Assert.That(snapshot.Status, Is.EqualTo(BuildWorkspaceHealthStatus.RecoveryRequired));
            Assert.That(snapshot.CanRecover, Is.True);
            Assert.That(snapshot.Issues.Count, Is.EqualTo(1));
            Assert.That(snapshot.Issues[0].ParticipantId, Is.EqualTo(FakeParticipant.ParticipantId));
            Assert.That(File.ReadAllText(evidencePath), Is.EqualTo("pending"));
        }

        [Test]
        public void Inspect_WhenFacadeIsUnavailable_BlocksRecoveryWithReason()
        {
            string evidencePath = Path.Combine(CreateStateRoot("fake"), "active.json");
            File.WriteAllText(evidencePath, "pending");

            BuildWorkspaceSnapshot snapshot = BuildWorkspaceService.Inspect(
                projectRoot,
                new IBuildRecoveryParticipant[] { new FakeParticipant(projectRoot, available: false) },
                editorIsBusy: false);

            Assert.That(snapshot.Status, Is.EqualTo(BuildWorkspaceHealthStatus.Blocked));
            Assert.That(snapshot.CanRecover, Is.False);
            Assert.That(snapshot.Issues[0].Title, Is.EqualTo("Recovery implementation unavailable"));
            Assert.That(snapshot.Issues[0].Message, Does.Contain("package is missing"));
        }

        [Test]
        public void Inspect_TokenChangesWhenEvidenceContentChanges()
        {
            string evidencePath = Path.Combine(CreateStateRoot("fake"), "active.json");
            File.WriteAllText(evidencePath, "first");
            var participants = new IBuildRecoveryParticipant[] { new FakeParticipant(projectRoot) };

            string firstToken = BuildWorkspaceService.Inspect(
                projectRoot,
                participants,
                editorIsBusy: false).Token;
            File.WriteAllText(evidencePath, "other");
            string secondToken = BuildWorkspaceService.Inspect(
                projectRoot,
                participants,
                editorIsBusy: false).Token;

            Assert.That(secondToken, Is.Not.EqualTo(firstToken));
        }

        [Test]
        public void Inspect_WithUnknownTransactionRootFile_FailsClosed()
        {
            string transactionRoot = Path.Combine(projectRoot, ".buildpipeline", "transactions");
            Directory.CreateDirectory(transactionRoot);
            string evidencePath = Path.Combine(transactionRoot, "unknown.json");
            File.WriteAllText(evidencePath, "preserve");

            BuildWorkspaceSnapshot snapshot = BuildWorkspaceService.Inspect(
                projectRoot,
                Array.Empty<IBuildRecoveryParticipant>(),
                editorIsBusy: false);

            Assert.That(snapshot.Status, Is.EqualTo(BuildWorkspaceHealthStatus.Blocked));
            Assert.That(snapshot.Issues[0].Title, Is.EqualTo("Unknown transaction-root file"));
            Assert.That(File.ReadAllText(evidencePath), Is.EqualTo("preserve"));
        }

        [Test]
        public void Recover_AfterParticipantStarts_RefreshesSynchronouslyOnSuccess()
        {
            string evidencePath = CreateEvidence("success");
            var order = new List<string>();
            var participant = new RecoveringParticipant(
                "Success",
                ".buildpipeline/transactions/success",
                priority: 0,
                recover: _ =>
                {
                    order.Add("recover");
                    File.Delete(evidencePath);
                });
            IBuildRecoveryParticipant[] participants = { participant };
            BuildWorkspaceSnapshot before = BuildWorkspaceService.Inspect(
                projectRoot,
                participants,
                editorIsBusy: false);

            BuildWorkspaceSnapshot after = BuildWorkspaceService.Recover(
                projectRoot,
                before.Token,
                () => participants,
                () => false,
                () => order.Add("refresh"));

            Assert.That(after.Status, Is.EqualTo(BuildWorkspaceHealthStatus.Clean));
            Assert.That(order, Is.EqualTo(new[] { "recover", "refresh" }));
        }

        [Test]
        public void Recover_WhenOwnedRefreshMakesEditorBusy_ReturnsCleanEvidenceState()
        {
            string evidencePath = CreateEvidence("refresh-busy");
            bool editorBusy = false;
            var participant = new RecoveringParticipant(
                "RefreshBusy",
                ".buildpipeline/transactions/refresh-busy",
                priority: 0,
                recover: _ => File.Delete(evidencePath));
            IBuildRecoveryParticipant[] participants = { participant };
            BuildWorkspaceSnapshot before = BuildWorkspaceService.Inspect(
                projectRoot,
                participants,
                editorIsBusy: false);

            BuildWorkspaceSnapshot after = BuildWorkspaceService.Recover(
                projectRoot,
                before.Token,
                () => participants,
                () => editorBusy,
                () => editorBusy = true);

            Assert.That(editorBusy, Is.True);
            Assert.That(after.Status, Is.EqualTo(BuildWorkspaceHealthStatus.Clean));
            Assert.That(after.CanRecover, Is.False);
        }

        [Test]
        public void Recover_WhenLaterParticipantFails_RefreshesOnceAndPreservesPrimaryFailure()
        {
            string firstEvidence = CreateEvidence("first");
            CreateEvidence("second");
            var first = new RecoveringParticipant(
                "First",
                ".buildpipeline/transactions/first",
                priority: 100,
                recover: _ => File.Delete(firstEvidence));
            var primaryFailure = new InvalidOperationException("participant failed");
            var second = new RecoveringParticipant(
                "Second",
                ".buildpipeline/transactions/second",
                priority: 0,
                recover: _ => throw primaryFailure);
            IBuildRecoveryParticipant[] participants = { first, second };
            BuildWorkspaceSnapshot before = BuildWorkspaceService.Inspect(
                projectRoot,
                participants,
                editorIsBusy: false);
            int refreshCount = 0;

            InvalidOperationException actual = Assert.Throws<InvalidOperationException>(() =>
                BuildWorkspaceService.Recover(
                    projectRoot,
                    before.Token,
                    () => participants,
                    () => false,
                    () => refreshCount++));

            Assert.That(actual, Is.SameAs(primaryFailure));
            Assert.That(refreshCount, Is.EqualTo(1));
        }

        [Test]
        public void Recover_WhenParticipantAndRefreshFail_AggregatesWithoutMaskingPrimaryFailure()
        {
            CreateEvidence("aggregate");
            var primaryFailure = new InvalidOperationException("participant failed");
            var refreshFailure = new IOException("refresh failed");
            var participant = new RecoveringParticipant(
                "Aggregate",
                ".buildpipeline/transactions/aggregate",
                priority: 0,
                recover: _ => throw primaryFailure);
            IBuildRecoveryParticipant[] participants = { participant };
            BuildWorkspaceSnapshot before = BuildWorkspaceService.Inspect(
                projectRoot,
                participants,
                editorIsBusy: false);

            AggregateException aggregate = Assert.Throws<AggregateException>(() =>
                BuildWorkspaceService.Recover(
                    projectRoot,
                    before.Token,
                    () => participants,
                    () => false,
                    () => throw refreshFailure));

            Assert.That(aggregate.InnerExceptions.Count, Is.EqualTo(2));
            Assert.That(aggregate.InnerExceptions[0], Is.SameAs(primaryFailure));
            Assert.That(aggregate.InnerExceptions[1], Is.SameAs(refreshFailure));
        }

        [Test]
        public void Recover_AttemptsEveryChildBeforeCoordinatorAndAggregatesChildFailures()
        {
            CreateEvidence("first-child");
            CreateEvidence("second-child");
            string coordinatorEvidence = CreateEvidence("coordinator");
            var order = new List<string>();
            var firstFailure = new InvalidOperationException("first failed");
            var secondFailure = new IOException("second failed");
            var first = new RecoveringParticipant(
                "FirstChild",
                ".buildpipeline/transactions/first-child",
                priority: 100,
                recover: _ =>
                {
                    order.Add("first");
                    throw firstFailure;
                });
            var second = new RecoveringParticipant(
                "SecondChild",
                ".buildpipeline/transactions/second-child",
                priority: 0,
                recover: _ =>
                {
                    order.Add("second");
                    throw secondFailure;
                });
            var coordinator = new CoordinatingParticipant(
                "Coordinator",
                ".buildpipeline/transactions/coordinator",
                priority: 1000,
                recover: _ =>
                {
                    order.Add("coordinator");
                    File.Delete(coordinatorEvidence);
                });
            IBuildRecoveryParticipant[] participants =
            {
                coordinator,
                second,
                first
            };
            BuildWorkspaceSnapshot before = BuildWorkspaceService.Inspect(
                projectRoot,
                participants,
                editorIsBusy: false);

            AggregateException aggregate = Assert.Throws<AggregateException>(() =>
                BuildWorkspaceService.Recover(
                    projectRoot,
                    before.Token,
                    () => participants,
                    () => false,
                    () => order.Add("refresh")));

            Assert.That(order, Is.EqualTo(new[]
            {
                "first",
                "second",
                "coordinator",
                "refresh"
            }));
            Assert.That(aggregate.InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(aggregate.InnerExceptions[0], Is.SameAs(firstFailure));
            Assert.That(aggregate.InnerExceptions[1], Is.SameAs(secondFailure));
        }

        [Test]
        public void Recover_WhenTokenChanged_DoesNotStartParticipantOrRefresh()
        {
            string evidencePath = CreateEvidence("token");
            int recoverCount = 0;
            int refreshCount = 0;
            var participant = new RecoveringParticipant(
                "Token",
                ".buildpipeline/transactions/token",
                priority: 0,
                recover: _ => recoverCount++);
            IBuildRecoveryParticipant[] participants = { participant };
            BuildWorkspaceSnapshot before = BuildWorkspaceService.Inspect(
                projectRoot,
                participants,
                editorIsBusy: false);
            File.WriteAllText(evidencePath, "changed");

            Assert.Throws<InvalidOperationException>(() =>
                BuildWorkspaceService.Recover(
                    projectRoot,
                    before.Token,
                    () => participants,
                    () => false,
                    () => refreshCount++));

            Assert.That(recoverCount, Is.Zero);
            Assert.That(refreshCount, Is.Zero);
            Assert.That(File.ReadAllText(evidencePath), Is.EqualTo("changed"));
        }

        private string CreateStateRoot(string name)
        {
            string path = Path.Combine(projectRoot, ".buildpipeline", "transactions", name);
            Directory.CreateDirectory(path);
            return path;
        }

        private string CreateEvidence(string name)
        {
            string path = Path.Combine(CreateStateRoot(name), "active.json");
            File.WriteAllText(path, "pending");
            return path;
        }

        private sealed class FakeParticipant :
            IBuildRecoveryParticipant,
            IBuildRecoveryAvailability
        {
            internal const string ParticipantId = "WorkspaceTest";
            private static readonly string[] StatePaths =
            {
                ".buildpipeline/transactions/fake"
            };
            private readonly bool available;
            private readonly string expectedProjectRoot;

            internal FakeParticipant(string expectedProjectRoot, bool available = true)
            {
                this.expectedProjectRoot = expectedProjectRoot;
                this.available = available;
            }

            public string Id => ParticipantId;
            public int Priority => 0;
            public IReadOnlyList<string> StateDirectoryRelativePaths => StatePaths;

            public bool IsRecoveryAvailable(
                string requestedProjectRoot,
                out string unavailableReason)
            {
                Assert.That(requestedProjectRoot, Is.EqualTo(expectedProjectRoot));
                unavailableReason = available ? string.Empty : "The package is missing.";
                return available;
            }

            public void Recover(string requestedProjectRoot)
            {
                throw new AssertionException("Inspect must never invoke recovery.");
            }
        }

        private class RecoveringParticipant : IBuildRecoveryParticipant
        {
            private readonly string id;
            private readonly int priority;
            private readonly string[] statePaths;
            private readonly Action<string> recover;

            public RecoveringParticipant(
                string id,
                string statePath,
                int priority,
                Action<string> recover)
            {
                this.id = id;
                this.priority = priority;
                statePaths = new[] { statePath };
                this.recover = recover;
            }

            public string Id => id;
            public int Priority => priority;
            public IReadOnlyList<string> StateDirectoryRelativePaths => statePaths;

            public void Recover(string requestedProjectRoot)
            {
                recover(requestedProjectRoot);
            }
        }

        private sealed class CoordinatingParticipant :
            RecoveringParticipant,
            IBuildRecoveryCoordinator
        {
            public CoordinatingParticipant(
                string id,
                string statePath,
                int priority,
                Action<string> recover)
                : base(id, statePath, priority, recover)
            {
            }
        }
    }
}
