using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace Build.Pipeline.Editor.Integrations.YooAsset3.Tests
{
    public sealed class YooAsset3PublicationTransactionTests
    {
        private const string InvocationId = "yooasset-main";
        private string projectRoot;
        private string testRoot;
        private string buildOutputRoot;
        private string bundledFileRoot;

        [SetUp]
        public void SetUp()
        {
            string unityProjectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            testRoot = Path.Combine(
                unityProjectRoot,
                "Temp",
                "BuildPipelineTests",
                "YooAsset3Publication",
                Guid.NewGuid().ToString("N"));
            projectRoot = Path.Combine(testRoot, "Project");
            buildOutputRoot = Path.Combine(projectRoot, "BuildOutput");
            bundledFileRoot = Path.Combine(projectRoot, "Assets", "StreamingAssets", "YooAsset");
            Directory.CreateDirectory(Path.Combine(projectRoot, "Assets", "StreamingAssets"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, true);
            }
        }

        [Test]
        public void Publish_WhenSecondPackageStageIsMissing_RestoresEveryOriginalTarget()
        {
            YooAsset3BuildPlan plan = CreatePlan(
                CreatePackage("PackageOne", EBundledCopyOption.None),
                CreatePackage("PackageTwo", EBundledCopyOption.None));
            WriteOwnedPublication(plan.Packages[0], false, "payload.txt", "old-one");
            WriteOwnedPublication(plan.Packages[1], false, "payload.txt", "old-two");
            YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan, InvocationId);
            transaction.Prepare();
            WriteFile(transaction.Packages[0].OutputOperation.stage, "payload.txt", "new-one");
            WriteFile(transaction.Packages[1].OutputOperation.stage, "payload.txt", "new-two");
            transaction.SealReadyDirectories();
            Directory.Delete(transaction.Packages[1].OutputOperation.stage, true);

            TerminalBarrierHarness barrier = BeginBarrier();
            Assert.Throws<DirectoryNotFoundException>(() =>
                transaction.Publish(validatePublishedState: null, refreshAssets: NoOp));
            barrier.AbortAfterRollback();

            Assert.That(ReadFile(plan.Packages[0].OutputPackageDirectory, "payload.txt"), Is.EqualTo("old-one"));
            Assert.That(ReadFile(plan.Packages[1].OutputPackageDirectory, "payload.txt"), Is.EqualTo("old-two"));
            Assert.That(
                File.Exists(Path.Combine(
                    YooAsset3PublicationTransaction.GetStateRoot(projectRoot, InvocationId),
                    "active.json")),
                Is.False);
            Assert.That(
                Directory.Exists(YooAsset3PublicationTransaction.GetStateRoot(projectRoot, InvocationId)),
                Is.False);
            Assert.That(
                Directory.Exists(YooAsset3PublicationTransaction.GetProviderStateRoot(projectRoot)),
                Is.False);
        }

        [Test]
        public void TerminalCommit_WhenEveryStageIsValid_PublishesAllPackagesAndRemovesRecoveryState()
        {
            YooAsset3BuildPlan plan = CreatePlan(
                CreatePackage("PackageOne", EBundledCopyOption.None),
                CreatePackage("PackageTwo", EBundledCopyOption.None));
            WriteOwnedPublication(plan.Packages[0], false, "payload.txt", "old-one");
            YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan, InvocationId);
            transaction.Prepare();
            WriteFile(transaction.Packages[0].OutputOperation.stage, "payload.txt", "new-one");
            WriteFile(transaction.Packages[1].OutputOperation.stage, "payload.txt", "new-two");
            transaction.SealReadyDirectories();

            CommitTerminalPublication(transaction, () =>
            {
                Assert.That(ReadFile(plan.Packages[0].OutputPackageDirectory, "payload.txt"), Is.EqualTo("new-one"));
                Assert.That(ReadFile(plan.Packages[1].OutputPackageDirectory, "payload.txt"), Is.EqualTo("new-two"));
            });

            Assert.That(
                File.Exists(Path.Combine(
                    YooAsset3PublicationTransaction.GetStateRoot(projectRoot, InvocationId),
                    "active.json")),
                Is.False);
        }

        [Test]
        public void EnsureNoPendingRecovery_WhenNoStateExists_IsZeroWrite()
        {
            string stateRoot = YooAsset3PublicationTransaction.GetStateRoot(projectRoot, InvocationId);

            Assert.That(Directory.Exists(stateRoot), Is.False);
            Assert.DoesNotThrow(() =>
                YooAsset3PublicationTransaction.EnsureNoPendingRecovery(projectRoot, InvocationId));
            Assert.That(Directory.Exists(stateRoot), Is.False);
        }

        [TestCase("../escape")]
        [TestCase("nested/id")]
        [TestCase("Uppercase")]
        [TestCase("trailing.")]
        public void GetStateRoot_WithUnsafeInvocationId_RejectsPathFragment(
            string invocationId)
        {
            Assert.Throws<ArgumentException>(() =>
                YooAsset3PublicationTransaction.GetStateRoot(
                    projectRoot,
                    invocationId));
        }

        [Test]
        public void InvocationIsolation_AllowsIndependentPendingTransactionsForSameProvider()
        {
            const string secondInvocationId = "yooasset-secondary";
            YooAsset3BuildPlan firstPlan = CreatePlan(
                CreatePackage("PackageOne", EBundledCopyOption.None));
            YooAsset3BuildPlan secondPlan = CreatePlan(
                CreatePackage("PackageTwo", EBundledCopyOption.None));
            YooAsset3PublicationTransaction first =
                YooAsset3PublicationTransaction.Create(firstPlan, InvocationId);
            YooAsset3PublicationTransaction second =
                YooAsset3PublicationTransaction.Create(
                    secondPlan,
                    secondInvocationId);
            try
            {
                first.Prepare();
                second.Prepare();

                Assert.That(first.PublicationId, Is.Not.EqualTo(second.PublicationId));
                Assert.That(first.StateRelativePath, Is.Not.EqualTo(second.StateRelativePath));
                Assert.That(
                    File.Exists(Path.Combine(
                        YooAsset3PublicationTransaction.GetStateRoot(
                            projectRoot,
                            InvocationId),
                        "active.json")),
                    Is.True);
                Assert.That(
                    File.Exists(Path.Combine(
                        YooAsset3PublicationTransaction.GetStateRoot(
                            projectRoot,
                            secondInvocationId),
                        "active.json")),
                    Is.True);
            }
            finally
            {
                second.Dispose();
                first.Dispose();
            }

            Assert.That(
                Directory.Exists(YooAsset3PublicationTransaction.GetStateRoot(
                    projectRoot,
                    InvocationId)),
                Is.False);
            Assert.That(
                Directory.Exists(YooAsset3PublicationTransaction.GetStateRoot(
                    projectRoot,
                    secondInvocationId)),
                Is.False);
            Assert.That(
                Directory.Exists(YooAsset3PublicationTransaction.GetProviderStateRoot(projectRoot)),
                Is.False);
        }

        [Test]
        public void RecoverPending_AfterPreparedCrash_DiscardsStagesWithoutChangingFinalTarget()
        {
            YooAsset3BuildPlan plan = CreatePlan(CreatePackage("PackageOne", EBundledCopyOption.None));
            WriteOwnedPublication(plan.Packages[0], false, "payload.txt", "old");
            YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan, InvocationId);
            transaction.Prepare();
            string stage = transaction.Packages[0].OutputOperation.stage;
            WriteFile(stage, "payload.txt", "new");

            YooAsset3PublicationTransaction.RecoverPending(projectRoot, () => { });

            Assert.That(ReadFile(plan.Packages[0].OutputPackageDirectory, "payload.txt"), Is.EqualTo("old"));
            Assert.That(Directory.Exists(stage), Is.False);
            Assert.That(
                File.Exists(Path.Combine(
                    YooAsset3PublicationTransaction.GetStateRoot(projectRoot, InvocationId),
                    "active.json")),
                Is.False);
        }

        [Test]
        public void RecoverPending_WhenOnlyDurableTemporaryJournalExists_PromotesItBeforeRollback()
        {
            YooAsset3BuildPlan plan = CreatePlan(CreatePackage("PackageOne", EBundledCopyOption.None));
            WriteOwnedPublication(plan.Packages[0], false, "payload.txt", "old");
            YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan, InvocationId);
            transaction.Prepare();
            string stage = transaction.Packages[0].OutputOperation.stage;
            WriteFile(stage, "payload.txt", "new");
            string journalPath = GetJournalPath();
            string temporaryPath = GetJournalTemporaryPath(journalPath);
            File.Move(journalPath, temporaryPath);

            YooAsset3PublicationTransaction.RecoverPending(projectRoot, NoOp);

            Assert.That(ReadFile(plan.Packages[0].OutputPackageDirectory, "payload.txt"), Is.EqualTo("old"));
            Assert.That(Directory.Exists(stage), Is.False);
            Assert.That(File.Exists(journalPath), Is.False);
            Assert.That(File.Exists(temporaryPath), Is.False);
        }

        [Test]
        public void RecoverPending_WhenTemporaryJournalHasNewerSequence_UsesNewerDurableState()
        {
            YooAsset3BuildPlan plan = CreatePlan(CreatePackage("PackageOne", EBundledCopyOption.None));
            YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan, InvocationId);
            transaction.Prepare();
            string journalPath = GetJournalPath();
            byte[] olderJournal = File.ReadAllBytes(journalPath);
            string stage = transaction.Packages[0].OutputOperation.stage;
            WriteFile(stage, "payload.txt", "sealed");
            transaction.SealReadyDirectories();

            string temporaryPath = GetJournalTemporaryPath(journalPath);
            File.Move(journalPath, temporaryPath);
            File.WriteAllBytes(journalPath, olderJournal);
            WriteFile(stage, "payload.txt", "tampered");

            AggregateException exception = Assert.Throws<AggregateException>(() =>
                YooAsset3PublicationTransaction.RecoverPending(projectRoot, NoOp));

            StringAssert.Contains("rollback", exception.Message.ToLowerInvariant());
            Assert.That(File.Exists(journalPath), Is.True);
            Assert.That(File.Exists(temporaryPath), Is.False);
            Assert.That(Directory.Exists(stage), Is.True);
        }

        [Test]
        public void Prepare_WhenRecoveryEvidenceExists_FailsClosedUntilExplicitRecovery()
        {
            YooAsset3BuildPlan plan = CreatePlan(CreatePackage("PackageOne", EBundledCopyOption.None));
            WriteOwnedPublication(plan.Packages[0], false, "payload.txt", "old");
            YooAsset3PublicationTransaction interrupted = YooAsset3PublicationTransaction.Create(plan, InvocationId);
            interrupted.Prepare();
            string stage = interrupted.Packages[0].OutputOperation.stage;
            WriteFile(stage, "payload.txt", "new");
            string journalPath = GetJournalPath();
            byte[] journalBeforeRetry = File.ReadAllBytes(journalPath);

            InvalidOperationException readinessException = Assert.Throws<InvalidOperationException>(() =>
                YooAsset3PublicationTransaction.EnsureNoPendingRecovery(projectRoot, InvocationId));
            StringAssert.Contains("Pending YooAsset publication recovery", readinessException.Message);
            Assert.That(File.ReadAllBytes(journalPath), Is.EqualTo(journalBeforeRetry));

            YooAsset3PublicationTransaction retry = YooAsset3PublicationTransaction.Create(plan, InvocationId);
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                retry.Prepare());

            StringAssert.Contains("must be recovered before starting", exception.Message);
            Assert.That(File.ReadAllBytes(journalPath), Is.EqualTo(journalBeforeRetry));
            Assert.That(ReadFile(stage, "payload.txt"), Is.EqualTo("new"));
            Assert.That(ReadFile(plan.Packages[0].OutputPackageDirectory, "payload.txt"), Is.EqualTo("old"));

            retry.Dispose();
            YooAsset3PublicationTransaction.RecoverPending(projectRoot, () => { });
            Assert.That(File.Exists(journalPath), Is.False);
            Assert.That(Directory.Exists(stage), Is.False);
        }

        [Test]
        public void RecoverPending_WhenConfiguredRootsChanged_UsesRootsRecordedByCentralJournal()
        {
            YooAsset3BuildPlan plan = CreatePlan(CreatePackage("PackageOne", EBundledCopyOption.None));
            WriteOwnedPublication(plan.Packages[0], false, "payload.txt", "old");
            YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan, InvocationId);
            transaction.Prepare();
            string stage = transaction.Packages[0].OutputOperation.stage;
            WriteFile(stage, "payload.txt", "new");
            string originalTarget = plan.Packages[0].OutputPackageDirectory;
            string journalPath = GetJournalPath();

            buildOutputRoot = Path.Combine(projectRoot, "ChangedBuildOutput");
            bundledFileRoot = Path.Combine(projectRoot, "Assets", "StreamingAssets", "ChangedYooAsset");
            YooAsset3PublicationTransaction.RecoverPending(projectRoot, () => { });

            Assert.That(ReadFile(originalTarget, "payload.txt"), Is.EqualTo("old"));
            Assert.That(Directory.Exists(stage), Is.False);
            Assert.That(File.Exists(journalPath), Is.False);
            Assert.That(
                YooAsset3BuildSafety.IsStrictDescendant(
                    Path.Combine(projectRoot, ".buildpipeline"),
                    YooAsset3PublicationTransaction.GetStateRoot(projectRoot, InvocationId)),
                Is.True);
        }

        [TestCase(EBundledCopyOption.OnlyCopyAll)]
        [TestCase(EBundledCopyOption.OnlyCopyByTags)]
        public void Prepare_ForOnlyCopyModes_SeedsBundledWorkFromCurrentSnapshot(EBundledCopyOption option)
        {
            YooAsset3BuildPlan plan = CreatePlan(CreatePackage("PackageOne", option));
            WriteOwnedPublication(plan.Packages[0], true, "preserved.bundle", "old-bundle");
            YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan, InvocationId);

            transaction.Prepare();

            Assert.That(
                ReadFile(transaction.Packages[0].BundledWorkDirectory, "preserved.bundle"),
                Is.EqualTo("old-bundle"));
            transaction.Abort(NoOp);
        }

        [Test]
        public void RecoverPending_WhenJournalChecksumIsCorrupt_FailsClosedAndRetainsState()
        {
            YooAsset3BuildPlan plan = CreatePlan(CreatePackage("PackageOne", EBundledCopyOption.None));
            YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan, InvocationId);
            transaction.Prepare();
            string journalPath = Path.Combine(
                YooAsset3PublicationTransaction.GetStateRoot(projectRoot, InvocationId),
                "active.json");
            string journal = File.ReadAllText(journalPath);
            const string ChecksumMarker = "\"checksum\": \"";
            int checksumIndex = journal.IndexOf(ChecksumMarker, StringComparison.Ordinal);
            Assert.That(checksumIndex, Is.GreaterThanOrEqualTo(0));
            checksumIndex += ChecksumMarker.Length;
            char replacement = journal[checksumIndex] == '0' ? '1' : '0';
            journal = journal.Substring(0, checksumIndex) + replacement + journal.Substring(checksumIndex + 1);
            File.WriteAllText(journalPath, journal);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                YooAsset3PublicationTransaction.RecoverPending(projectRoot, () => { }));

            StringAssert.Contains("journal", exception.Message.ToLowerInvariant());
            Assert.That(File.Exists(journalPath), Is.True);
        }

        [Test]
        public void CreateExecutionPlan_PreservesConcretePipelineTypeAndRedirectsOnlyPublicationPaths()
        {
            YooAsset3BuildPlan plan = CreatePlan(CreatePackage("PackageOne", EBundledCopyOption.OnlyCopyAll));
            YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan, InvocationId);
            transaction.Prepare();
            var request = new AssetContentBuildRequest(
                InvocationId,
                BuildTarget.StandaloneWindows64,
                "1.0.0",
                projectRoot,
                null,
                BuildIncrementality.Incremental,
                true);

            YooAsset3PackageBuildPlan execution = transaction.CreateExecutionPlan(
                request,
                transaction.Packages[0]);

            Assert.That(execution.Parameters, Is.InstanceOf<RawFileBuildParameters>());
            Assert.That(
                YooAsset3BuildSafety.PathsEqual(
                    execution.OutputPackageDirectory,
                    transaction.Packages[0].OutputOperation.stage),
                Is.True);
            Assert.That(
                YooAsset3BuildSafety.PathsEqual(
                    execution.BundledPackageDirectory,
                    transaction.Packages[0].BundledWorkDirectory),
                Is.True);
            Assert.That(
                YooAsset3BuildSafety.PathsEqual(
                    execution.Parameters.GetPipelineOutputDirectory(),
                    plan.Packages[0].Parameters.GetPipelineOutputDirectory()),
                Is.True);
            transaction.Abort(NoOp);
        }

        [Test]
        public void BuildLock_WhenBuildRootsDifferButBundledRootMatches_RejectsConcurrentPublication()
        {
            string firstBuildRoot = Path.Combine(testRoot, "BuildOne");
            string secondBuildRoot = Path.Combine(testRoot, "BuildTwo");
            using (YooAsset3BuildLock.Acquire(projectRoot, firstBuildRoot, bundledFileRoot))
            {
                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                {
                    using (YooAsset3BuildLock.Acquire(projectRoot, secondBuildRoot, bundledFileRoot))
                    {
                    }
                });

                StringAssert.Contains("publication roots", exception.Message);
            }

            using (YooAsset3BuildLock.Acquire(projectRoot, secondBuildRoot, bundledFileRoot))
            {
            }
        }

        [Test]
        public void BuildLock_WhenAllPublicationRootsDiffer_StillSerializesTheProjectJournal()
        {
            string firstBuildRoot = Path.Combine(projectRoot, "BuildOne");
            string secondBuildRoot = Path.Combine(projectRoot, "BuildTwo");
            string firstBundledRoot = Path.Combine(projectRoot, "Assets", "StreamingAssets", "First");
            string secondBundledRoot = Path.Combine(projectRoot, "Assets", "StreamingAssets", "Second");
            using (YooAsset3BuildLock.Acquire(projectRoot, firstBuildRoot, firstBundledRoot))
            {
                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                {
                    using (YooAsset3BuildLock.Acquire(projectRoot, secondBuildRoot, secondBundledRoot))
                    {
                    }
                });

                StringAssert.Contains("publication roots", exception.Message);
            }
        }

        [Test]
        public void Prepare_WhenExistingTargetContainsUnknownAuthoredFiles_FailsClosedWithoutJournal()
        {
            YooAsset3BuildPlan plan = CreatePlan(CreatePackage("PackageOne", EBundledCopyOption.None));
            WriteFile(plan.Packages[0].OutputPackageDirectory, "authored.txt", "not-owned");
            YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan, InvocationId);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => transaction.Prepare());

            StringAssert.Contains("not a Build-owned", exception.Message);
            Assert.That(ReadFile(plan.Packages[0].OutputPackageDirectory, "authored.txt"), Is.EqualTo("not-owned"));
            Assert.That(File.Exists(GetJournalPath()), Is.False);
        }

        [Test]
        public void Prepare_WhenBundledTargetIsAbsentButRootMetaExists_FailsClosedWithoutDeletingMeta()
        {
            YooAsset3BuildPlan plan = CreatePlan(CreatePackage("PackageOne", EBundledCopyOption.OnlyCopyAll));
            string targetMeta = plan.Packages[0].BundledPackageDirectory + ".meta";
            Directory.CreateDirectory(Path.GetDirectoryName(targetMeta));
            File.WriteAllText(
                targetMeta,
                "fileFormatVersion: 2\nguid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\nfolderAsset: yes\n");
            YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan, InvocationId);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => transaction.Prepare());

            StringAssert.Contains("both exist or both be absent", exception.Message);
            StringAssert.Contains("aaaaaaaaaaaaaaaa", File.ReadAllText(targetMeta));
            Assert.That(File.Exists(GetJournalPath()), Is.False);
        }

        [Test]
        public void Publish_WhenOriginalPublicationChangesBeforeTerminalBoundary_RejectsWithoutReplacingIt()
        {
            YooAsset3BuildPlan plan = CreatePlan(CreatePackage("PackageOne", EBundledCopyOption.None));
            WriteOwnedPublication(plan.Packages[0], false, "payload.txt", "old");
            YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan, InvocationId);
            transaction.Prepare();
            WriteFile(transaction.Packages[0].OutputOperation.stage, "payload.txt", "new");
            transaction.SealReadyDirectories();
            WriteFile(plan.Packages[0].OutputPackageDirectory, "external.txt", "external-change");

            BeginBarrier();
            AggregateException exception = Assert.Throws<AggregateException>(() =>
                transaction.Publish(validatePublishedState: null, refreshAssets: NoOp));

            StringAssert.Contains("rollback", exception.Message.ToLowerInvariant());
            Assert.That(ReadFile(plan.Packages[0].OutputPackageDirectory, "payload.txt"), Is.EqualTo("old"));
            Assert.That(ReadFile(plan.Packages[0].OutputPackageDirectory, "external.txt"), Is.EqualTo("external-change"));
            Assert.That(File.Exists(GetJournalPath()), Is.True);
        }

        [Test]
        public void Rollback_WhenInstalledTargetWasExternallyReplaced_PreservesReplacementAndBackup()
        {
            YooAsset3BuildPlan plan = CreatePlan(CreatePackage("PackageOne", EBundledCopyOption.None));
            WriteOwnedPublication(plan.Packages[0], false, "payload.txt", "old");
            YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan, InvocationId);
            transaction.Prepare();
            YooAsset3PublicationJournalOperation operation = transaction.Packages[0].OutputOperation;
            WriteFile(operation.stage, "payload.txt", "new");
            transaction.SealReadyDirectories();

            BeginBarrier();
            Assert.Throws<AggregateException>(() => transaction.Publish(() =>
            {
                Directory.Delete(operation.target, true);
                WriteFile(operation.target, "external.txt", "replacement");
                throw new InvalidOperationException("force rollback after external replacement");
            }, NoOp));

            Assert.That(ReadFile(operation.target, "external.txt"), Is.EqualTo("replacement"));
            Assert.That(ReadFile(operation.backup, "payload.txt"), Is.EqualTo("old"));
            Assert.That(File.Exists(GetJournalPath()), Is.True);
        }

        [Test]
        public void Complete_WhenRefreshFails_RetainsCommittedJournalUntilRecoveryRefreshSucceeds()
        {
            YooAsset3BuildPlan plan = CreatePlan(CreatePackage("PackageOne", EBundledCopyOption.None));
            WriteOwnedPublication(plan.Packages[0], false, "payload.txt", "old");
            YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan, InvocationId);
            transaction.Prepare();
            YooAsset3PublicationJournalOperation operation = transaction.Packages[0].OutputOperation;
            WriteFile(operation.stage, "payload.txt", "new");
            transaction.SealReadyDirectories();

            TerminalBarrierHarness barrier = BeginBarrier();
            transaction.Publish(validatePublishedState: null, refreshAssets: NoOp);
            barrier.CommitDecision();
            YooAsset3CommittedPublicationException exception = Assert.Throws<YooAsset3CommittedPublicationException>(() =>
                transaction.Complete(() => throw new InvalidOperationException("refresh failed")));

            StringAssert.Contains("committed", exception.Message.ToLowerInvariant());
            Assert.That(ReadFile(operation.target, "payload.txt"), Is.EqualTo("new"));
            Assert.That(ReadFile(operation.backup, "payload.txt"), Is.EqualTo("old"));
            Assert.That(File.Exists(GetJournalPath()), Is.True);

            bool refreshed = false;
            YooAsset3PublicationTransaction.RecoverPending(projectRoot, () => refreshed = true);

            Assert.That(refreshed, Is.True);
            Assert.That(ReadFile(operation.target, "payload.txt"), Is.EqualTo("new"));
            Assert.That(Directory.Exists(operation.backup), Is.False);
            Assert.That(File.Exists(GetJournalPath()), Is.False);
            barrier.Complete();
        }

        [Test]
        public void RecoverPending_WhenBundledTargetWasAbsentDuringCrash_RestoresOriginalDirectoryMetaIdentity()
        {
            YooAsset3BuildPlan plan = CreatePlan(CreatePackage("PackageOne", EBundledCopyOption.OnlyCopyAll));
            WriteOwnedPublication(plan.Packages[0], true, "payload.txt", "old-bundle");
            YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan, InvocationId);
            transaction.Prepare();
            YooAsset3PublicationJournalOperation operation = transaction.Packages[0].BundledOperation;
            string originalMeta = File.ReadAllText(operation.targetMeta);

            File.Copy(operation.targetMeta, operation.protectedMeta);
            Directory.Move(operation.target, operation.backup);
            File.Delete(operation.targetMeta);

            YooAsset3PublicationTransaction.RecoverPending(projectRoot, () => { });

            Assert.That(ReadFile(operation.target, "payload.txt"), Is.EqualTo("old-bundle"));
            Assert.That(File.ReadAllText(operation.targetMeta), Is.EqualTo(originalMeta));
            Assert.That(Directory.Exists(operation.backup), Is.False);
            Assert.That(File.Exists(operation.protectedMeta), Is.False);
            Assert.That(File.Exists(GetJournalPath()), Is.False);
        }

        [Test]
        public void RecoverPending_WhenBundledTargetMetaWasExternallyReplaced_FailsClosed()
        {
            YooAsset3BuildPlan plan = CreatePlan(CreatePackage("PackageOne", EBundledCopyOption.OnlyCopyAll));
            WriteOwnedPublication(plan.Packages[0], true, "payload.txt", "old-bundle");
            YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan, InvocationId);
            transaction.Prepare();
            YooAsset3PublicationJournalOperation operation = transaction.Packages[0].BundledOperation;

            File.Copy(operation.targetMeta, operation.protectedMeta);
            Directory.Move(operation.target, operation.backup);
            File.WriteAllText(
                operation.targetMeta,
                "fileFormatVersion: 2\nguid: fedcba9876543210fedcba9876543210\nfolderAsset: yes\n");

            Assert.Throws<AggregateException>(() =>
                YooAsset3PublicationTransaction.RecoverPending(projectRoot, () => { }));

            Assert.That(Directory.Exists(operation.target), Is.False);
            Assert.That(Directory.Exists(operation.backup), Is.True);
            StringAssert.Contains("fedcba9876543210", File.ReadAllText(operation.targetMeta));
            Assert.That(File.Exists(operation.protectedMeta), Is.True);
            Assert.That(File.Exists(GetJournalPath()), Is.True);
        }

        [Test]
        public void Complete_WhenRefreshFailsAfterGeneratingInitialBundledMeta_RecoveryCapturesItBeforeCleanup()
        {
            YooAsset3BuildPlan plan = CreatePlan(CreatePackage("PackageOne", EBundledCopyOption.OnlyCopyAll));
            YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan, InvocationId);
            transaction.Prepare();
            YooAsset3PublicationJournalOperation output = transaction.Packages[0].OutputOperation;
            YooAsset3PublicationJournalOperation bundled = transaction.Packages[0].BundledOperation;
            WriteFile(output.stage, "payload.txt", "new-output");
            WriteFile(bundled.stage, "payload.txt", "new-bundle");
            transaction.SealReadyDirectories();

            const string GeneratedMeta =
                "fileFormatVersion: 2\nguid: 11111111111111111111111111111111\nfolderAsset: yes\n";
            TerminalBarrierHarness barrier = BeginBarrier();
            transaction.Publish(validatePublishedState: null, refreshAssets: NoOp);
            barrier.CommitDecision();
            Assert.Throws<YooAsset3CommittedPublicationException>(() => transaction.Complete(() =>
            {
                File.WriteAllText(bundled.targetMeta, GeneratedMeta);
                throw new InvalidOperationException("refresh failed after generating meta");
            }));

            Assert.That(File.Exists(bundled.targetMeta), Is.True);
            Assert.That(File.Exists(GetJournalPath()), Is.True);
            YooAsset3PublicationTransaction.RecoverPending(projectRoot, () => { });

            Assert.That(ReadFile(bundled.target, "payload.txt"), Is.EqualTo("new-bundle"));
            StringAssert.Contains("1111111111111111", File.ReadAllText(bundled.targetMeta));
            Assert.That(File.Exists(GetJournalPath()), Is.False);
            barrier.Complete();
        }

        [Test]
        public void Complete_WhenBarrierHasNoCommitDecision_FailsClosedAndCanRollBack()
        {
            YooAsset3BuildPlan plan = CreatePlan(CreatePackage("PackageOne", EBundledCopyOption.None));
            WriteOwnedPublication(plan.Packages[0], false, "payload.txt", "old");
            YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan, InvocationId);
            transaction.Prepare();
            WriteFile(transaction.Packages[0].OutputOperation.stage, "payload.txt", "new");
            transaction.SealReadyDirectories();
            TerminalBarrierHarness barrier = BeginBarrier();
            transaction.Publish(validatePublishedState: null, refreshAssets: NoOp);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                transaction.Complete(NoOp));

            StringAssert.Contains("explicit durable Commit decision", exception.Message);
            Assert.That(File.Exists(GetJournalPath()), Is.True);
            transaction.Abort(NoOp);
            barrier.AbortAfterRollback();
            Assert.That(ReadFile(plan.Packages[0].OutputPackageDirectory, "payload.txt"), Is.EqualTo("old"));
            Assert.That(File.Exists(GetJournalPath()), Is.False);
        }

        [Test]
        public void DownstreamActive_WhenRunFails_InstallsOnlyBundledInputAndRefreshesAfterRollback()
        {
            YooAsset3BuildPlan plan = CreatePlan(CreatePackage("PackageOne", EBundledCopyOption.OnlyCopyAll));
            WriteOwnedPublication(plan.Packages[0], false, "payload.txt", "old-output");
            WriteOwnedPublication(plan.Packages[0], true, "payload.txt", "old-bundle");

            YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan, InvocationId);
            transaction.Prepare();
            YooAsset3PublicationJournalOperation output = transaction.Packages[0].OutputOperation;
            YooAsset3PublicationJournalOperation bundled = transaction.Packages[0].BundledOperation;
            WriteFile(output.stage, "payload.txt", "new-output");
            WriteFile(bundled.stage, "payload.txt", "new-bundle");
            transaction.SealReadyDirectories();

            bool activationRefreshed = false;
            transaction.ActivateDownstreamInputs(() => activationRefreshed = true);
            transaction.ValidateActivatedInputs();

            Assert.That(activationRefreshed, Is.True);
            Assert.That(ReadFile(output.target, "payload.txt"), Is.EqualTo("old-output"));
            Assert.That(ReadFile(output.stage, "payload.txt"), Is.EqualTo("new-output"));
            Assert.That(ReadFile(bundled.target, "payload.txt"), Is.EqualTo("new-bundle"));

            bool rollbackRefreshed = false;
            transaction.Abort(() => rollbackRefreshed = true);

            Assert.That(rollbackRefreshed, Is.True);
            Assert.That(
                ReadFile(plan.Packages[0].OutputPackageDirectory, "payload.txt"),
                Is.EqualTo("old-output"));
            Assert.That(
                ReadFile(plan.Packages[0].BundledPackageDirectory, "payload.txt"),
                Is.EqualTo("old-bundle"));
            Assert.That(File.Exists(GetJournalPath()), Is.False);
        }

        [Test]
        public void SourceQualification_FromPreparedPhase_RestoresExactBundledSourceAndThenRestoresStage()
        {
            YooAsset3BuildPlan plan = CreatePlan(
                CreatePackage("PackageOne", EBundledCopyOption.OnlyCopyAll));
            WriteOwnedPublication(plan.Packages[0], true, "payload.txt", "old-bundle");
            YooAsset3PublicationTransaction transaction =
                YooAsset3PublicationTransaction.Create(plan, InvocationId);
            transaction.Prepare();
            YooAsset3PublicationJournalOperation output = transaction.Packages[0].OutputOperation;
            YooAsset3PublicationJournalOperation bundled = transaction.Packages[0].BundledOperation;
            string originalMeta = File.ReadAllText(bundled.targetMeta);
            WriteFile(output.stage, "payload.txt", "new-output");
            WriteFile(bundled.stage, "payload.txt", "new-bundle");
            transaction.SealReadyDirectories();

            using (transaction.SuspendForSourceQualification())
            {
                Assert.That(ReadFile(bundled.target, "payload.txt"), Is.EqualTo("old-bundle"));
                Assert.That(File.ReadAllText(bundled.targetMeta), Is.EqualTo(originalMeta));
                Assert.That(Directory.Exists(bundled.stage), Is.False);
                Assert.That(Directory.Exists(bundled.backup), Is.False);
                Assert.That(File.Exists(bundled.protectedMeta), Is.False);
                Assert.That(
                    Directory.GetFileSystemEntries(
                        bundledFileRoot,
                        ".yoo-*",
                        SearchOption.TopDirectoryOnly),
                    Is.Empty);
            }

            Assert.That(ReadFile(bundled.stage, "payload.txt"), Is.EqualTo("new-bundle"));
            Assert.That(ReadFile(bundled.target, "payload.txt"), Is.EqualTo("old-bundle"));
            Assert.That(ReadFile(output.stage, "payload.txt"), Is.EqualTo("new-output"));
            transaction.Abort(NoOp);
            transaction.Dispose();
        }

        [Test]
        public void SourceQualification_FromDownstreamActive_RestoresExactBundledSourceAndReactivatesWithoutRefresh()
        {
            YooAsset3BuildPlan plan = CreatePlan(
                CreatePackage("PackageOne", EBundledCopyOption.OnlyCopyAll));
            WriteOwnedPublication(plan.Packages[0], true, "payload.txt", "old-bundle");
            YooAsset3PublicationTransaction transaction =
                YooAsset3PublicationTransaction.Create(plan, InvocationId);
            transaction.Prepare();
            YooAsset3PublicationJournalOperation output = transaction.Packages[0].OutputOperation;
            YooAsset3PublicationJournalOperation bundled = transaction.Packages[0].BundledOperation;
            string originalMeta = File.ReadAllText(bundled.targetMeta);
            WriteFile(output.stage, "payload.txt", "new-output");
            WriteFile(bundled.stage, "payload.txt", "new-bundle");
            transaction.SealReadyDirectories();

            int refreshCount = 0;
            transaction.ActivateDownstreamInputs(() => refreshCount++);
            using (transaction.SuspendForSourceQualification())
            {
                Assert.That(ReadFile(bundled.target, "payload.txt"), Is.EqualTo("old-bundle"));
                Assert.That(File.ReadAllText(bundled.targetMeta), Is.EqualTo(originalMeta));
                Assert.That(Directory.Exists(bundled.stage), Is.False);
                Assert.That(Directory.Exists(bundled.backup), Is.False);
                Assert.That(File.Exists(bundled.protectedMeta), Is.False);
                Assert.That(
                    Directory.GetFileSystemEntries(
                        bundledFileRoot,
                        ".yoo-*",
                        SearchOption.TopDirectoryOnly),
                    Is.Empty);
                Assert.That(refreshCount, Is.EqualTo(1));
            }

            Assert.That(refreshCount, Is.EqualTo(1));
            Assert.That(ReadFile(bundled.target, "payload.txt"), Is.EqualTo("new-bundle"));
            Assert.That(ReadFile(bundled.backup, "payload.txt"), Is.EqualTo("old-bundle"));
            Assert.That(ReadFile(output.stage, "payload.txt"), Is.EqualTo("new-output"));
            transaction.ValidateActivatedInputs();
            transaction.Abort(NoOp);
            transaction.Dispose();
        }

        [Test]
        public void RecoverPending_FromSuspendedSourceQualification_RestoresAbsentBundledSource()
        {
            YooAsset3BuildPlan plan = CreatePlan(
                CreatePackage("PackageOne", EBundledCopyOption.OnlyCopyAll));
            YooAsset3PublicationTransaction transaction =
                YooAsset3PublicationTransaction.Create(plan, InvocationId);
            transaction.Prepare();
            YooAsset3PublicationJournalOperation output = transaction.Packages[0].OutputOperation;
            YooAsset3PublicationJournalOperation bundled = transaction.Packages[0].BundledOperation;
            WriteFile(output.stage, "payload.txt", "new-output");
            WriteFile(bundled.stage, "payload.txt", "new-bundle");
            transaction.SealReadyDirectories();
            const string installedMeta =
                "fileFormatVersion: 2\nguid: 22222222222222222222222222222222\nfolderAsset: yes\n";
            transaction.ActivateDownstreamInputs(() =>
                File.WriteAllText(bundled.targetMeta, installedMeta));

            transaction.SuspendForSourceQualification();

            Assert.That(Directory.Exists(bundled.target), Is.False);
            Assert.That(File.Exists(bundled.targetMeta), Is.False);
            Assert.That(Directory.Exists(bundled.stage), Is.False);
            Assert.That(
                Directory.GetFileSystemEntries(
                    bundledFileRoot,
                    ".yoo-*",
                    SearchOption.TopDirectoryOnly),
                Is.Empty);
            YooAsset3PublicationTransaction.RecoverPending(projectRoot, NoOp);

            Assert.That(Directory.Exists(bundled.target), Is.False);
            Assert.That(File.Exists(bundled.targetMeta), Is.False);
            Assert.That(File.Exists(GetJournalPath()), Is.False);
            transaction.Dispose();
        }

        [Test]
        public void TerminalCommit_AfterDownstreamActivation_PublishesRemainingOutput()
        {
            YooAsset3BuildPlan plan = CreatePlan(CreatePackage("PackageOne", EBundledCopyOption.OnlyCopyAll));
            WriteOwnedPublication(plan.Packages[0], false, "payload.txt", "old-output");
            WriteOwnedPublication(plan.Packages[0], true, "payload.txt", "old-bundle");
            YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan, InvocationId);
            transaction.Prepare();
            YooAsset3PublicationJournalOperation output = transaction.Packages[0].OutputOperation;
            YooAsset3PublicationJournalOperation bundled = transaction.Packages[0].BundledOperation;
            WriteFile(output.stage, "payload.txt", "new-output");
            WriteFile(bundled.stage, "payload.txt", "new-bundle");
            transaction.SealReadyDirectories();
            transaction.ActivateDownstreamInputs(NoOp);

            TerminalBarrierHarness barrier = BeginBarrier();
            transaction.Publish(() =>
            {
                Assert.That(ReadFile(output.target, "payload.txt"), Is.EqualTo("new-output"));
                Assert.That(ReadFile(bundled.target, "payload.txt"), Is.EqualTo("new-bundle"));
            }, NoOp);
            barrier.CommitDecision();
            transaction.Complete(NoOp);
            barrier.Complete();

            Assert.That(ReadFile(output.target, "payload.txt"), Is.EqualTo("new-output"));
            Assert.That(ReadFile(bundled.target, "payload.txt"), Is.EqualTo("new-bundle"));
            Assert.That(File.Exists(GetJournalPath()), Is.False);
        }

        [Test]
        public void RecoverPending_WhenDownstreamActiveHasCommitDecision_FailsClosed()
        {
            YooAsset3BuildPlan plan = CreatePlan(CreatePackage("PackageOne", EBundledCopyOption.OnlyCopyAll));
            WriteOwnedPublication(plan.Packages[0], false, "payload.txt", "old-output");
            WriteOwnedPublication(plan.Packages[0], true, "payload.txt", "old-bundle");
            YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan, InvocationId);
            transaction.Prepare();
            YooAsset3PublicationJournalOperation output = transaction.Packages[0].OutputOperation;
            YooAsset3PublicationJournalOperation bundled = transaction.Packages[0].BundledOperation;
            WriteFile(output.stage, "payload.txt", "new-output");
            WriteFile(bundled.stage, "payload.txt", "new-bundle");
            transaction.SealReadyDirectories();
            transaction.ActivateDownstreamInputs(NoOp);
            TerminalBarrierHarness barrier = BeginBarrier();
            barrier.CommitDecision();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                YooAsset3PublicationTransaction.RecoverPending(projectRoot, NoOp));

            StringAssert.Contains("terminal outputs were never published", exception.Message);
            Assert.That(ReadFile(output.target, "payload.txt"), Is.EqualTo("old-output"));
            Assert.That(ReadFile(output.stage, "payload.txt"), Is.EqualTo("new-output"));
            Assert.That(ReadFile(bundled.target, "payload.txt"), Is.EqualTo("new-bundle"));
            Assert.That(File.Exists(GetJournalPath()), Is.True);
        }

        [Test]
        public void RecoverPending_AfterActivationRefreshCrashWithNewMeta_RestoresAbsentTargetsAndRefreshes()
        {
            YooAsset3BuildPlan plan = CreatePlan(CreatePackage("PackageOne", EBundledCopyOption.OnlyCopyAll));
            using (YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan, InvocationId))
            {
                transaction.Prepare();
                YooAsset3PublicationJournalOperation output = transaction.Packages[0].OutputOperation;
                YooAsset3PublicationJournalOperation bundled = transaction.Packages[0].BundledOperation;
                WriteFile(output.stage, "payload.txt", "new-output");
                WriteFile(bundled.stage, "payload.txt", "new-bundle");
                transaction.SealReadyDirectories();

                Assert.Throws<InvalidOperationException>(() => transaction.ActivateDownstreamInputs(() =>
                {
                    File.WriteAllText(
                        bundled.targetMeta,
                        "fileFormatVersion: 2\nguid: 22222222222222222222222222222222\nfolderAsset: yes\n");
                    throw new InvalidOperationException("Injected refresh failure.");
                }));

                Assert.That(ReadFile(bundled.target, "payload.txt"), Is.EqualTo("new-bundle"));
                Assert.That(ReadFile(output.stage, "payload.txt"), Is.EqualTo("new-output"));
                Assert.That(Directory.Exists(output.target), Is.False);
                Assert.That(File.Exists(bundled.targetMeta), Is.True);
                Assert.That(File.Exists(GetJournalPath()), Is.True);

                bool rollbackRefreshed = false;
                YooAsset3PublicationTransaction.RecoverPending(
                    projectRoot,
                    () => rollbackRefreshed = true);
                Assert.That(rollbackRefreshed, Is.True);
            }

            Assert.That(Directory.Exists(plan.Packages[0].OutputPackageDirectory), Is.False);
            Assert.That(Directory.Exists(plan.Packages[0].BundledPackageDirectory), Is.False);
            Assert.That(File.Exists(plan.Packages[0].BundledPackageDirectory + ".meta"), Is.False);
            Assert.That(File.Exists(GetJournalPath()), Is.False);
        }

        [Test]
        public void BuildLock_WhenLockDirectoryIsReparsePoint_FailsClosed()
        {
            string fakeProjectRoot = Path.Combine(testRoot, "FakeProject");
            Directory.CreateDirectory(Path.Combine(fakeProjectRoot, "Assets"));
            string redirectedTarget = Path.Combine(fakeProjectRoot, "RedirectedLocks");
            Directory.CreateDirectory(redirectedTarget);
            string lockRoot = YooAsset3BuildLock.GetLockRoot(fakeProjectRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(lockRoot));
            CreateDirectoryLink(lockRoot, redirectedTarget);
            try
            {
                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                {
                    using (YooAsset3BuildLock.Acquire(
                               fakeProjectRoot,
                               Path.Combine(fakeProjectRoot, "BuildOutput"),
                               Path.Combine(fakeProjectRoot, "Assets", "StreamingAssets")))
                    {
                    }
                });

                StringAssert.Contains("reparse point", exception.Message);
            }
            finally
            {
                DeleteDirectoryLink(lockRoot);
            }
        }

        [Test]
        public void Prepare_WhenTransactionStateDirectoryIsReparsePoint_FailsClosed()
        {
            YooAsset3BuildPlan plan = CreatePlan(CreatePackage("PackageOne", EBundledCopyOption.None));
            Directory.CreateDirectory(buildOutputRoot);
            string redirectedTarget = Path.Combine(testRoot, "RedirectedState");
            Directory.CreateDirectory(redirectedTarget);
            string stateRoot = YooAsset3PublicationTransaction.GetStateRoot(projectRoot, InvocationId);
            Directory.CreateDirectory(Path.GetDirectoryName(stateRoot));
            CreateDirectoryLink(stateRoot, redirectedTarget);
            try
            {
                YooAsset3PublicationTransaction transaction = YooAsset3PublicationTransaction.Create(plan, InvocationId);
                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => transaction.Prepare());

                StringAssert.Contains("reparse point", exception.Message);
            }
            finally
            {
                DeleteDirectoryLink(stateRoot);
            }
        }

        [Test]
        public void Registry_WhenYooAsset305IsInstalled_ResolvesTypedAdapterByRegistrationMetadata()
        {
            IAssetContentBuildAdapter adapter = BuildPipelineRegistry.ResolveContentAdapter(
                YooAssetBuildConfig.ProviderIdValue);

            Assert.That(adapter, Is.InstanceOf<YooAsset3BuildAdapter>());
        }

        private YooAsset3BuildPlan CreatePlan(params YooAsset3PackageBuildPlan[] packages)
        {
            return new YooAsset3BuildPlan(
                projectRoot,
                buildOutputRoot,
                bundledFileRoot,
                packages,
                Array.Empty<string>());
        }

        private TerminalBarrierHarness BeginBarrier()
        {
            return TerminalBarrierHarness.Begin(
                projectRoot,
                Guid.NewGuid().ToString("N"),
                new IBuildDeferredPublication[] { new BarrierPublicationProxy() });
        }

        private void CommitTerminalPublication(
            YooAsset3PublicationTransaction transaction,
            Action validatePublishedState = null,
            Action refreshAssets = null)
        {
            Action refresh = refreshAssets ?? NoOp;
            TerminalBarrierHarness barrier = BeginBarrier();
            transaction.Publish(validatePublishedState, refresh);
            barrier.CommitDecision();
            transaction.Complete(refresh);
            barrier.Complete();
        }

        private static void NoOp()
        {
        }

        private YooAsset3PackageBuildPlan CreatePackage(string packageName, EBundledCopyOption bundledCopyOption)
        {
            var profile = new YooAssetPackageProfile
            {
                packageName = packageName,
                buildPipeline = YooAssetBuildPipelineKind.RawFile,
                bundledCopyOption = ToProfileOption(bundledCopyOption),
                versionCollisionPolicy = YooAssetVersionCollisionPolicy.ReplaceExactVersion
            };
            var parameters = new RawFileBuildParameters
            {
                BuildOutputRoot = buildOutputRoot,
                BundledFileRoot = bundledFileRoot,
                BuildPipeline = EBuildPipeline.RawFileBuildPipeline.ToString(),
                BuildBundleType = (int)EBundleType.RawBundle,
                BuildTarget = BuildTarget.StandaloneWindows64,
                PackageName = packageName,
                PackageVersion = "1.0.0",
                PackageNote = "transaction-test",
                BundledCopyOption = bundledCopyOption
            };
            return new YooAsset3PackageBuildPlan(
                profile,
                parameters,
                new UnusedBuildPipeline(),
                string.Empty,
                YooAssetCryptographyIdentity.NoneAdapterId,
                YooAssetCryptographyIdentity.NoneRuntimeDecryptContractId);
        }

        private static YooAssetBundledCopyOption ToProfileOption(EBundledCopyOption option)
        {
            switch (option)
            {
                case EBundledCopyOption.None:
                    return YooAssetBundledCopyOption.None;
                case EBundledCopyOption.OnlyCopyAll:
                    return YooAssetBundledCopyOption.OnlyCopyAll;
                case EBundledCopyOption.OnlyCopyByTags:
                    return YooAssetBundledCopyOption.OnlyCopyByTags;
                default:
                    throw new ArgumentOutOfRangeException(nameof(option), option, null);
            }
        }

        private static void WriteFile(string directory, string fileName, string content)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, fileName), content);
        }

        private void WriteOwnedPublication(
            YooAsset3PackageBuildPlan package,
            bool bundled,
            string fileName,
            string content)
        {
            string directory = bundled ? package.BundledPackageDirectory : package.OutputPackageDirectory;
            string kind = bundled
                ? YooAsset3PublicationOwnership.BundledPackageKind
                : YooAsset3PublicationOwnership.PackageOutputKind;
            WriteFile(directory, fileName, content);
            if (bundled)
            {
                File.WriteAllText(
                    directory + ".meta",
                    "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\nfolderAsset: yes\n");
            }
            YooAsset3PublicationOwnership.Seal(
                projectRoot,
                directory,
                kind,
                package.PackageName,
                package.PackageVersion,
                YooAssetCryptographyIdentity.NoneAdapterId,
                YooAssetCryptographyIdentity.NoneRuntimeDecryptContractId,
                Guid.NewGuid().ToString("N"));
        }

        private string GetJournalPath()
        {
            return Path.Combine(YooAsset3PublicationTransaction.GetStateRoot(projectRoot, InvocationId), "active.json");
        }

        private static string GetJournalTemporaryPath(string journalPath)
        {
            JournalIdentity identity = JsonUtility.FromJson<JournalIdentity>(
                File.ReadAllText(journalPath));
            Assert.That(identity, Is.Not.Null);
            Assert.That(identity.transactionId, Has.Length.EqualTo(32));
            return journalPath + ".tmp-" + identity.transactionId;
        }

        private static string ReadFile(string directory, string fileName)
        {
            return File.ReadAllText(Path.Combine(directory, fileName));
        }

        private static void CreateDirectoryLink(string linkPath, string targetPath)
        {
            bool windows = Path.DirectorySeparatorChar == '\\';
            var startInfo = new ProcessStartInfo
            {
                FileName = windows ? Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe" : "/bin/ln",
                Arguments = windows
                    ? $"/d /c mklink /J {QuoteArgument(linkPath)} {QuoteArgument(targetPath)}"
                    : $"-s {QuoteArgument(targetPath)} {QuoteArgument(linkPath)}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (Process process = Process.Start(startInfo))
            {
                string standardOutput = process.StandardOutput.ReadToEnd();
                string standardError = process.StandardError.ReadToEnd();
                process.WaitForExit();
                Assert.That(
                    process.ExitCode,
                    Is.Zero,
                    $"Failed to create a test reparse point. Output: {standardOutput} Error: {standardError}");
            }
        }

        private static void DeleteDirectoryLink(string linkPath)
        {
            if (!Directory.Exists(linkPath) && !File.Exists(linkPath))
            {
                return;
            }

            try
            {
                Directory.Delete(linkPath, false);
            }
            catch (IOException)
            {
                File.Delete(linkPath);
            }
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private sealed class UnusedBuildPipeline : IBuildPipeline
        {
            public BuildResult Run(BuildParameters buildParameters, bool enableLog)
            {
                throw new InvalidOperationException("The filesystem transaction tests do not execute YooAsset.");
            }
        }

        private sealed class BarrierPublicationProxy : IBuildDeferredPublication
        {
            public string Id => YooAsset3PublicationTransaction.GetPublicationId(InvocationId);
            public string RecoveryStateRelativePath => YooAsset3PublicationTransaction.GetStateRelativePath(InvocationId);

            public void Publish()
            {
                throw new InvalidOperationException(
                    "The barrier proxy describes an externally driven test transaction and cannot publish it.");
            }

            public void Complete()
            {
                throw new InvalidOperationException(
                    "The barrier proxy describes an externally driven test transaction and cannot complete it.");
            }

            public void Dispose()
            {
            }
        }

        [Serializable]
        private sealed class JournalIdentity
        {
            public string transactionId = string.Empty;
        }

        // The core assembly intentionally exposes the durable barrier only to the
        // integration assembly. This narrow harness invokes that real barrier so
        // integration tests exercise its on-disk decision instead of fabricating it.
        private sealed class TerminalBarrierHarness
        {
            private const string BarrierTypeName = "Build.Pipeline.Editor.BuildPublicationBarrier";
            private readonly object instance;
            private readonly Type barrierType;

            private TerminalBarrierHarness(object instance, Type barrierType)
            {
                this.instance = instance;
                this.barrierType = barrierType;
            }

            public static TerminalBarrierHarness Begin(
                string projectRoot,
                string runId,
                IReadOnlyList<IBuildDeferredPublication> publications)
            {
                Type type = typeof(BuildPipelineRegistry).Assembly.GetType(
                    BarrierTypeName,
                    throwOnError: true);
                MethodInfo begin = type.GetMethod(
                    "Begin",
                    BindingFlags.Public | BindingFlags.Static);
                Assert.That(begin, Is.Not.Null, "The real terminal publication barrier must expose Begin.");
                object barrier = Invoke(begin, instance: null, projectRoot, runId, publications);
                return new TerminalBarrierHarness(barrier, type);
            }

            public void CommitDecision()
            {
                InvokeInstance("CommitDecision");
            }

            public void Complete()
            {
                InvokeInstance("Complete");
            }

            public void AbortAfterRollback()
            {
                InvokeInstance("AbortAfterRollback");
            }

            private void InvokeInstance(string methodName)
            {
                MethodInfo method = barrierType.GetMethod(
                    methodName,
                    BindingFlags.Public | BindingFlags.Instance);
                Assert.That(method, Is.Not.Null, $"The real terminal publication barrier must expose {methodName}.");
                Invoke(method, instance);
            }

            private static object Invoke(MethodInfo method, object instance, params object[] arguments)
            {
                try
                {
                    return method.Invoke(instance, arguments);
                }
                catch (TargetInvocationException exception) when (exception.InnerException != null)
                {
                    ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                    throw;
                }
            }
        }
    }
}
