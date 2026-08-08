using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Build.Pipeline.Editor;
using Microsoft.Win32.SafeHandles;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Build.Pipeline.Tests.Editor
{
    public sealed class PlayerOutputTransactionTests
    {
        private string sandboxRoot;
        private string projectRoot;
        private string buildRoot;
        private string outputDirectory;
        private string outputPath;

        [SetUp]
        public void SetUp()
        {
            sandboxRoot = Path.Combine(
                Path.GetTempPath(),
                "BuildPipelinePlayerTransactionTests-" + Guid.NewGuid().ToString("N"));
            projectRoot = Path.Combine(sandboxRoot, "UnityProject");
            buildRoot = Path.Combine(projectRoot, "Build");
            outputDirectory = Path.Combine(buildRoot, "Windows", "Release");
            outputPath = Path.Combine(outputDirectory, "TestProduct.exe");
            Directory.CreateDirectory(projectRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(sandboxRoot))
            {
                Directory.Delete(sandboxRoot, true);
            }
        }

        [Test]
        public void DisposeBeforeCommit_PreservesLastKnownGoodOutput()
        {
            PublishOwnedOutput("old");
            BuildRequest request = CreateRequest(BuildIncrementality.Clean);

            using (PlayerOutputTransaction transaction = BeginTransaction(request))
            {
                Assert.That(File.ReadAllText(outputPath), Is.EqualTo("old"));
                File.WriteAllText(transaction.StageOutputPath, "partial");
            }

            Assert.That(File.ReadAllText(outputPath), Is.EqualTo("old"));
            Assert.That(Directory.Exists(GetStateRoot()), Is.True);
            Assert.That(File.Exists(Path.Combine(GetStateRoot(), "active.json")), Is.False);
        }

        [Test]
        public void Commit_ReplacesOutputOnlyAfterStageIsReady()
        {
            PublishOwnedOutput("old");
            BuildRequest request = CreateRequest(BuildIncrementality.Clean);

            using (PlayerOutputTransaction transaction = BeginTransaction(request))
            {
                File.WriteAllText(transaction.StageOutputPath, "new");
                Assert.That(File.ReadAllText(outputPath), Is.EqualTo("old"));
                transaction.Commit();
            }

            Assert.That(File.ReadAllText(outputPath), Is.EqualTo("new"));
            Assert.That(
                File.Exists(outputDirectory + ".buildpipeline-player-owner.json"),
                Is.True);
        }

        [Test]
        public void IncrementalCommit_StagesPriorOutputWithoutMutatingIt()
        {
            PublishOwnedOutput(
                "old",
                stageRoot =>
                {
                    string stagedRetainedPath = Path.Combine(stageRoot, "Data", "retained.bin");
                    Directory.CreateDirectory(Path.GetDirectoryName(stagedRetainedPath));
                    File.WriteAllText(stagedRetainedPath, "retained");
                });
            string retainedPath = Path.Combine(outputDirectory, "Data", "retained.bin");
            BuildRequest request = CreateRequest(BuildIncrementality.Incremental);

            using (PlayerOutputTransaction transaction = BeginTransaction(request))
            {
                Assert.That(File.ReadAllText(transaction.StageOutputPath), Is.EqualTo("old"));
                Assert.That(
                    File.ReadAllText(Path.Combine(
                        Path.GetDirectoryName(transaction.StageOutputPath),
                        "Data",
                        "retained.bin")),
                    Is.EqualTo("retained"));
                File.WriteAllText(transaction.StageOutputPath, "new");
                transaction.Commit();
            }

            Assert.That(File.ReadAllText(outputPath), Is.EqualTo("new"));
            Assert.That(File.ReadAllText(retainedPath), Is.EqualTo("retained"));
        }

        [Test]
        public void BeginIncremental_WithoutPublishedBaseline_FailsBeforeCreatingTransactionState()
        {
            BuildRequest request = CreateRequest(BuildIncrementality.Incremental);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => BeginTransaction(request));

            Assert.That(exception.Message, Does.Contain("previously published"));
            Assert.That(exception.Message, Does.Contain("Clean"));
            Assert.That(
                File.Exists(Path.Combine(GetStateRoot(), "active.json")),
                Is.False);
            AssertNoTransactionScratch();
        }

        [TestCase("BuildTarget", "BuildTarget")]
        [TestCase("NamedBuildTarget", "NamedBuildTarget")]
        [TestCase("ScriptingBackend", "ScriptingBackend")]
        [TestCase("OutputArtifactPath", "OutputArtifactPath")]
        [TestCase("ProductName", "ProductName")]
        [TestCase("ApplicationIdentifier", "ApplicationIdentifier")]
        [TestCase("ExportAndroidProject", "ExportAndroidProject")]
        public void BeginIncremental_WhenCompatibilityIdentityChanges_FailsClosed(
            string changedField,
            string expectedDiagnostic)
        {
            PublishOwnedOutput("old");
            BuildRequest request;
            switch (changedField)
            {
                case "BuildTarget":
                    request = CreateRequest(
                        BuildIncrementality.Incremental,
                        target: BuildTarget.Android);
                    break;
                case "ScriptingBackend":
                    request = CreateRequest(
                        BuildIncrementality.Incremental,
                        scriptingBackend: ScriptingImplementation.IL2CPP);
                    break;
                case "NamedBuildTarget":
                    request = CreateRequest(
                        BuildIncrementality.Incremental,
                        namedTarget: NamedBuildTarget.Server);
                    break;
                case "OutputArtifactPath":
                    outputPath = Path.Combine(outputDirectory, "RenamedProduct.exe");
                    request = CreateRequest(BuildIncrementality.Incremental);
                    break;
                case "ProductName":
                    request = CreateRequest(
                        BuildIncrementality.Incremental,
                        productName: "RenamedProduct");
                    break;
                case "ApplicationIdentifier":
                    request = CreateRequest(
                        BuildIncrementality.Incremental,
                        applicationIdentifier: "com.example.renamed");
                    break;
                case "ExportAndroidProject":
                    request = CreateRequest(
                        BuildIncrementality.Incremental,
                        exportAndroidProject: true);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(changedField),
                        changedField,
                        "Unsupported compatibility field test case.");
            }

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => BeginTransaction(request));

            Assert.That(exception.Message, Does.Contain("compatibility identity"));
            Assert.That(exception.Message, Does.Contain(expectedDiagnostic));
            Assert.That(exception.Message, Does.Contain("Clean"));
            Assert.That(
                File.ReadAllText(Path.Combine(outputDirectory, "TestProduct.exe")),
                Is.EqualTo("old"));
            Assert.That(
                File.Exists(Path.Combine(GetStateRoot(), "active.json")),
                Is.False);
            AssertNoTransactionScratch();
        }

        [Test]
        public void BeginIncremental_WhenPlayerExtensionFingerprintChanges_FailsClosed()
        {
            BuildRequest cleanRequest = CreateRequest(BuildIncrementality.Clean);
            using (PlayerOutputTransaction transaction = PlayerOutputTransaction.Begin(
                       cleanRequest,
                       BuildIncrementality.Clean,
                       new string('a', 64)))
            {
                File.WriteAllText(transaction.StageOutputPath, "published");
                transaction.Commit();
            }

            BuildRequest incrementalRequest = CreateRequest(
                BuildIncrementality.Incremental);
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => PlayerOutputTransaction.Begin(
                    incrementalRequest,
                    BuildIncrementality.Incremental,
                    new string('b', 64)));

            Assert.That(exception.Message, Does.Contain("PlayerExtensionFingerprint"));
            Assert.That(exception.Message, Does.Contain("Clean"));
            Assert.That(File.ReadAllText(outputPath), Is.EqualTo("published"));
            AssertNoTransactionScratch();
        }

        [Test]
        public void BeginIncremental_WhenUnityVersionChanges_FailsClosed()
        {
            PublishOwnedOutput("published");
            RewritePublishedCompatibility(identity =>
                identity.unityVersion = identity.unityVersion + "-different");

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => BeginTransaction(CreateRequest(BuildIncrementality.Incremental)));

            Assert.That(exception.Message, Does.Contain("UnityVersion"));
            Assert.That(exception.Message, Does.Contain("Clean"));
            Assert.That(File.ReadAllText(outputPath), Is.EqualTo("published"));
            AssertNoTransactionScratch();
        }

        [Test]
        public void BeginIncremental_WhenPipelineCompatibilityRevisionChanges_RequiresCleanUpgrade()
        {
            PublishOwnedOutput("published");
            RewritePublishedCompatibility(identity =>
                identity.playerPipelineCompatibilityRevision++);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => BeginTransaction(CreateRequest(BuildIncrementality.Incremental)));

            Assert.That(
                exception.Message,
                Does.Contain("PlayerPipelineCompatibilityRevision"));
            Assert.That(exception.Message, Does.Contain("Clean"));
            Assert.That(File.ReadAllText(outputPath), Is.EqualTo("published"));
            AssertNoTransactionScratch();
        }

        [Test]
        public void CleanCommit_UpgradesPipelineCompatibilityRevision()
        {
            PublishOwnedOutput("published");
            RewritePublishedCompatibility(identity =>
                identity.playerPipelineCompatibilityRevision++);
            BuildRequest cleanRequest = CreateRequest(BuildIncrementality.Clean);

            using (PlayerOutputTransaction transaction = BeginTransaction(cleanRequest))
            {
                File.WriteAllText(transaction.StageOutputPath, "upgraded");
                transaction.Commit();
            }

            OwnerRecord owner = JsonUtility.FromJson<OwnerRecord>(
                File.ReadAllText(GetOwnerPath()));
            Assert.That(
                owner.compatibilityIdentity.playerPipelineCompatibilityRevision,
                Is.EqualTo(PlayerOutputTransaction.PlayerPipelineCompatibilityRevision));
            Assert.That(File.ReadAllText(outputPath), Is.EqualTo("upgraded"));
            Assert.DoesNotThrow(() =>
            {
                using (PlayerOutputTransaction incremental = BeginTransaction(
                           CreateRequest(BuildIncrementality.Incremental)))
                {
                }
            });
        }

        [Test]
        public void Begin_WhenPipelineCompatibilityRevisionIsInvalid_FailsClosed()
        {
            PublishOwnedOutput("published");
            RewritePublishedCompatibility(identity =>
                identity.playerPipelineCompatibilityRevision = 0);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => BeginTransaction(CreateRequest(BuildIncrementality.Clean)));

            Assert.That(exception.Message, Does.Contain("invalid or unsupported"));
            Assert.That(File.ReadAllText(outputPath), Is.EqualTo("published"));
            Assert.That(File.Exists(Path.Combine(GetStateRoot(), "active.json")), Is.False);
            AssertNoTransactionScratch();
        }

        [Test]
        public void CleanCommit_ReplacesCompatibilityIdentityForFutureIncrementalBuilds()
        {
            PublishOwnedOutput("old");
            BuildRequest cleanRequest = CreateRequest(
                BuildIncrementality.Clean,
                productName: "RenamedProduct",
                applicationIdentifier: "com.example.renamed");
            using (PlayerOutputTransaction transaction = BeginTransaction(cleanRequest))
            {
                File.WriteAllText(transaction.StageOutputPath, "renamed");
                transaction.Commit();
            }

            BuildRequest incrementalRequest = CreateRequest(
                BuildIncrementality.Incremental,
                productName: "RenamedProduct",
                applicationIdentifier: "com.example.renamed");
            Assert.DoesNotThrow(() =>
            {
                using (PlayerOutputTransaction transaction = BeginTransaction(incrementalRequest))
                {
                    Assert.That(
                        File.ReadAllText(transaction.StageOutputPath),
                        Is.EqualTo("renamed"));
                }
            });
        }

        [Test]
        public void CleanCommit_PersistsVersionedCompatibilityIdentity()
        {
            PublishOwnedOutput("published");

            OwnerRecord owner = JsonUtility.FromJson<OwnerRecord>(
                File.ReadAllText(GetOwnerPath()));

            Assert.That(owner, Is.Not.Null);
            Assert.That(owner.formatVersion, Is.EqualTo(1));
            Assert.That(owner.compatibilityIdentity, Is.Not.Null);
            Assert.That(owner.compatibilityIdentity.formatVersion, Is.EqualTo(1));
            Assert.That(
                owner.compatibilityIdentity.playerPipelineCompatibilityRevision,
                Is.EqualTo(PlayerOutputTransaction.PlayerPipelineCompatibilityRevision));
            Assert.That(
                owner.compatibilityIdentity.unityVersion,
                Is.EqualTo(Application.unityVersion));
            Assert.That(
                owner.compatibilityIdentity.buildTarget,
                Is.EqualTo(BuildTarget.StandaloneWindows64.ToString()));
            Assert.That(
                owner.compatibilityIdentity.namedBuildTarget,
                Is.EqualTo(NamedBuildTarget.Standalone.TargetName));
            Assert.That(
                owner.compatibilityIdentity.scriptingBackend,
                Is.EqualTo(ScriptingImplementation.Mono2x.ToString()));
            Assert.That(
                owner.compatibilityIdentity.outputArtifactPath,
                Is.EqualTo("TestProduct.exe"));
            Assert.That(owner.compatibilityIdentity.productName, Is.EqualTo("TestProduct"));
            Assert.That(
                owner.compatibilityIdentity.applicationIdentifier,
                Is.EqualTo("com.example.test"));
            Assert.That(owner.compatibilityIdentity.exportAndroidProject, Is.False);
            Assert.That(
                owner.compatibilityIdentity.playerExtensionFingerprint,
                Has.Length.EqualTo(64));
            Assert.That(owner.compatibilityIdentity.digest, Has.Length.EqualTo(64));
        }

        [Test]
        public void DisposeAfterBackupMoveFault_RestoresOriginalOutput()
        {
            PublishOwnedOutput("old");
            BuildRequest request = CreateRequest(
                BuildIncrementality.Clean,
                productName: "ReplacementProduct");
            PlayerOutputTransaction transaction = PlayerOutputTransaction.Begin(
                request,
                request.Steps[0].Incrementality,
                PlayerBuildExtensionFingerprint.Compute(null),
                checkpoint =>
                {
                    if (checkpoint == PlayerOutputTransaction.BackupMovedCheckpoint)
                    {
                        throw new InvalidOperationException("Injected backup-move fault.");
                    }
                });
            File.WriteAllText(transaction.StageOutputPath, "new");

            Assert.Throws<InvalidOperationException>(() => transaction.Commit());
            Assert.DoesNotThrow(() => transaction.Dispose());

            Assert.That(File.ReadAllText(outputPath), Is.EqualTo("old"));
            Assert.That(File.Exists(Path.Combine(GetStateRoot(), "active.json")), Is.False);
        }

        [Test]
        public void DisposeAfterPublish_WithPreparedTerminalBarrier_RestoresOriginalOutput()
        {
            PublishOwnedOutput("old");
            BuildRequest request = CreateRequest(BuildIncrementality.Clean);
            PlayerOutputTransaction transaction = BeginTransaction(request);
            File.WriteAllText(transaction.StageOutputPath, "new");
            BuildPublicationBarrier barrier = BuildPublicationBarrier.Begin(
                projectRoot,
                "rollback-run",
                new IBuildDeferredPublication[] { transaction });

            transaction.Publish();
            Assert.That(File.ReadAllText(outputPath), Is.EqualTo("new"));
            transaction.Dispose();
            barrier.AbortAfterRollback();

            Assert.That(File.ReadAllText(outputPath), Is.EqualTo("old"));
            Assert.That(File.Exists(Path.Combine(GetStateRoot(), "active.json")), Is.False);
            Assert.That(
                Directory.Exists(BuildPublicationBarrier.GetStateRoot(projectRoot)),
                Is.False);
        }

        [Test]
        public void DisposeAfterPublish_WithCommittedTerminalBarrier_PreservesNewOutput()
        {
            PublishOwnedOutput("old");
            BuildRequest request = CreateRequest(BuildIncrementality.Clean);
            PlayerOutputTransaction transaction = BeginTransaction(request);
            File.WriteAllText(transaction.StageOutputPath, "new");
            BuildPublicationBarrier barrier = BuildPublicationBarrier.Begin(
                projectRoot,
                "commit-run",
                new IBuildDeferredPublication[] { transaction });

            transaction.Publish();
            barrier.CommitDecision();
            transaction.Dispose();
            barrier.Complete();

            Assert.That(File.ReadAllText(outputPath), Is.EqualTo("new"));
            Assert.That(File.Exists(Path.Combine(GetStateRoot(), "active.json")), Is.False);
            Assert.That(
                Directory.Exists(BuildPublicationBarrier.GetStateRoot(projectRoot)),
                Is.False);
        }

        [Test]
        public void RecoverPending_WhenProcessStopsAfterStageMove_UsesPreparedBarrierRollbackDecision()
        {
            PublishOwnedOutput("old");
            string originalOwnerTransactionId = ReadOwnerTransactionId(GetOwnerPath());
            BuildRequest request = CreateRequest(
                BuildIncrementality.Clean,
                productName: "ReplacementProduct");
            PlayerOutputTransaction transaction = PlayerOutputTransaction.Begin(
                request,
                request.Steps[0].Incrementality,
                PlayerBuildExtensionFingerprint.Compute(null),
                checkpoint =>
                {
                    if (checkpoint == PlayerOutputTransaction.StageMovedCheckpoint)
                    {
                        throw new PlayerOutputSimulatedTerminationException(checkpoint);
                    }
                });
            File.WriteAllText(transaction.StageOutputPath, "new");
            BuildPublicationBarrier barrier = BuildPublicationBarrier.Begin(
                projectRoot,
                "stage-move-interruption",
                new IBuildDeferredPublication[] { transaction });

            Assert.Throws<PlayerOutputSimulatedTerminationException>(
                () => transaction.Publish());
            transaction.AbandonForSimulatedTermination();
            Assert.That(File.ReadAllText(outputPath), Is.EqualTo("new"));

            PlayerOutputTransaction.RecoverPending(projectRoot);
            barrier.AbortAfterRollback();

            Assert.That(File.ReadAllText(outputPath), Is.EqualTo("old"));
            Assert.That(
                ReadOwnerTransactionId(GetOwnerPath()),
                Is.EqualTo(originalOwnerTransactionId));
            Assert.That(File.Exists(Path.Combine(GetStateRoot(), "active.json")), Is.False);
            AssertNoTransactionScratch();

            Assert.DoesNotThrow(() =>
            {
                using (PlayerOutputTransaction incremental = BeginTransaction(
                           CreateRequest(BuildIncrementality.Incremental)))
                {
                }
            });

            InvalidOperationException mismatch = Assert.Throws<InvalidOperationException>(() =>
                BeginTransaction(CreateRequest(
                    BuildIncrementality.Incremental,
                    productName: "ReplacementProduct")));
            Assert.That(mismatch.Message, Does.Contain("ProductName"));
        }

        [Test]
        public void RecoverPending_WhenPromotedOutputHasNoBarrierDecision_FailsClosed()
        {
            PublishOwnedOutput("old");
            BuildRequest request = CreateRequest(BuildIncrementality.Clean);
            PlayerOutputTransaction transaction = PlayerOutputTransaction.Begin(
                request,
                request.Steps[0].Incrementality,
                PlayerBuildExtensionFingerprint.Compute(null),
                checkpoint =>
                {
                    if (checkpoint == PlayerOutputTransaction.StageMovedCheckpoint)
                    {
                        throw new PlayerOutputSimulatedTerminationException(checkpoint);
                    }
                });
            File.WriteAllText(transaction.StageOutputPath, "new");

            Assert.Throws<PlayerOutputSimulatedTerminationException>(
                () => transaction.Publish());
            transaction.AbandonForSimulatedTermination();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => PlayerOutputTransaction.RecoverPending(projectRoot));

            Assert.That(exception.Message, Does.Contain("no durable terminal publication decision"));
            Assert.That(File.ReadAllText(outputPath), Is.EqualTo("new"));
            Assert.That(File.Exists(Path.Combine(GetStateRoot(), "active.json")), Is.True);

            BuildPublicationBarrier barrier = BuildPublicationBarrier.Begin(
                projectRoot,
                "missing-barrier-repair",
                new IBuildDeferredPublication[] { transaction });
            Assert.DoesNotThrow(() => PlayerOutputTransaction.RecoverPending(projectRoot));
            barrier.AbortAfterRollback();

            Assert.That(File.ReadAllText(outputPath), Is.EqualTo("old"));
            AssertNoTransactionScratch();
        }

        [Test]
        public void RecoverPending_WhenPreOwnerRewriteMarkerIsMissing_FailsClosed()
        {
            PublishOwnedOutput("old");
            string ownerPath = GetOwnerPath();
            string originalOwnerJson = File.ReadAllText(ownerPath);
            BuildRequest request = CreateRequest(BuildIncrementality.Clean);
            PlayerOutputTransaction transaction = PlayerOutputTransaction.Begin(
                request,
                request.Steps[0].Incrementality,
                PlayerBuildExtensionFingerprint.Compute(null),
                checkpoint =>
                {
                    if (checkpoint == PlayerOutputTransaction.StageMovedCheckpoint)
                    {
                        throw new PlayerOutputSimulatedTerminationException(checkpoint);
                    }
                });
            File.WriteAllText(transaction.StageOutputPath, "new");
            BuildPublicationBarrier barrier = BuildPublicationBarrier.Begin(
                projectRoot,
                "missing-original-owner",
                new IBuildDeferredPublication[] { transaction });

            Assert.Throws<PlayerOutputSimulatedTerminationException>(
                () => transaction.Publish());
            transaction.AbandonForSimulatedTermination();
            File.Delete(ownerPath);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => PlayerOutputTransaction.RecoverPending(projectRoot));

            Assert.That(exception.Message, Does.Contain("ownership is missing"));
            Assert.That(File.ReadAllText(outputPath), Is.EqualTo("new"));
            Assert.That(File.Exists(Path.Combine(GetStateRoot(), "active.json")), Is.True);

            File.WriteAllText(ownerPath, originalOwnerJson);
            Assert.DoesNotThrow(() => PlayerOutputTransaction.RecoverPending(projectRoot));
            barrier.AbortAfterRollback();
            Assert.That(File.ReadAllText(outputPath), Is.EqualTo("old"));
        }

        [Test]
        public void RecoverPending_WhenPreOwnerRewriteMarkerHasDifferentValidTransaction_FailsClosed()
        {
            PublishOwnedOutput("old");
            string ownerPath = GetOwnerPath();
            string originalOwnerJson = File.ReadAllText(ownerPath);
            string foreignOwnerJson = PublishForeignOwnedOutput("old");
            Assert.That(
                ReadOwnerTransactionIdFromJson(foreignOwnerJson),
                Is.Not.EqualTo(ReadOwnerTransactionIdFromJson(originalOwnerJson)));
            BuildRequest request = CreateRequest(BuildIncrementality.Clean);
            PlayerOutputTransaction transaction = PlayerOutputTransaction.Begin(
                request,
                request.Steps[0].Incrementality,
                PlayerBuildExtensionFingerprint.Compute(null),
                checkpoint =>
                {
                    if (checkpoint == PlayerOutputTransaction.StageMovedCheckpoint)
                    {
                        throw new PlayerOutputSimulatedTerminationException(checkpoint);
                    }
                });
            File.WriteAllText(transaction.StageOutputPath, "new");
            BuildPublicationBarrier barrier = BuildPublicationBarrier.Begin(
                projectRoot,
                "changed-original-owner",
                new IBuildDeferredPublication[] { transaction });

            Assert.Throws<PlayerOutputSimulatedTerminationException>(
                () => transaction.Publish());
            transaction.AbandonForSimulatedTermination();
            File.WriteAllText(ownerPath, foreignOwnerJson);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => PlayerOutputTransaction.RecoverPending(projectRoot));

            Assert.That(exception.Message, Does.Contain("ownership changed"));
            Assert.That(File.ReadAllText(outputPath), Is.EqualTo("new"));
            Assert.That(File.Exists(Path.Combine(GetStateRoot(), "active.json")), Is.True);

            File.WriteAllText(ownerPath, originalOwnerJson);
            Assert.DoesNotThrow(() => PlayerOutputTransaction.RecoverPending(projectRoot));
            barrier.AbortAfterRollback();
            Assert.That(File.ReadAllText(outputPath), Is.EqualTo("old"));
        }

        [Test]
        public void Dispose_WhenUnreadyStageOwnershipIsRemoved_FailsClosed()
        {
            PublishOwnedOutput("old");
            BuildRequest request = CreateRequest(BuildIncrementality.Clean);
            PlayerOutputTransaction transaction = BeginTransaction(request);
            File.Delete(Path.Combine(
                transaction.StageRoot,
                ".buildpipeline-player-stage-anchor"));

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => transaction.Dispose());

            StringAssert.Contains("recover", exception.Message.ToLowerInvariant());
            Assert.That(File.ReadAllText(outputPath), Is.EqualTo("old"));
            Assert.That(Directory.Exists(transaction.StageRoot), Is.True);
        }

        [Test]
        public void Begin_WhenPublishedOwnerPathContainsForeignFile_FailsClosedAndReleasesLock()
        {
            WriteUnownedOutput("old");
            string ownerPath = outputDirectory + ".buildpipeline-player-owner.json";
            const string foreignContents = "{\"external\":\"owned-by-another-tool\"}";
            File.WriteAllText(ownerPath, foreignContents);
            BuildRequest request = CreateRequest(BuildIncrementality.Clean);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => BeginTransaction(request));

            Assert.That(exception.Message, Does.Contain("ownership marker"));
            Assert.That(File.ReadAllText(ownerPath), Is.EqualTo(foreignContents));
            Assert.That(File.ReadAllText(outputPath), Is.EqualTo("old"));
            Assert.That(File.Exists(Path.Combine(GetStateRoot(), "active.json")), Is.False);

            File.Delete(ownerPath);
            Directory.Delete(outputDirectory, recursive: true);
            Assert.DoesNotThrow(() =>
            {
            using (PlayerOutputTransaction transaction = BeginTransaction(request))
                {
                }
            });
        }

        [Test]
        public void Begin_WhenExistingNonEmptyOutputHasNoOwner_FailsClosedAndReleasesLock()
        {
            WriteUnownedOutput("foreign");
            BuildRequest request = CreateRequest(BuildIncrementality.Clean);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => BeginTransaction(request));

            Assert.That(exception.Message, Does.Contain("non-empty"));
            Assert.That(exception.Message, Does.Contain("ownership marker"));
            Assert.That(File.ReadAllText(outputPath), Is.EqualTo("foreign"));
            Assert.That(File.Exists(Path.Combine(GetStateRoot(), "active.json")), Is.False);

            Directory.Delete(outputDirectory, recursive: true);
            Assert.DoesNotThrow(() =>
            {
                using (PlayerOutputTransaction transaction =
                       BeginTransaction(request))
                {
                }
            });
        }

        [Test]
        public void Commit_WhenUnownedOutputBecomesNonEmptyAfterPrepare_FailsClosed()
        {
            BuildRequest request = CreateRequest(BuildIncrementality.Clean);
            PlayerOutputTransaction transaction = BeginTransaction(request);
            File.WriteAllText(transaction.StageOutputPath, "new");
            WriteUnownedOutput("foreign");

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => transaction.Commit());
            Assert.That(exception.Message, Does.Contain("non-empty"));
            Assert.That(File.ReadAllText(outputPath), Is.EqualTo("foreign"));
            Assert.DoesNotThrow(() => transaction.Dispose());
            Assert.That(File.ReadAllText(outputPath), Is.EqualTo("foreign"));
        }

        [Test]
        public void Commit_WhenExistingOutputIsEmpty_AllowsOwnershipAdoption()
        {
            Directory.CreateDirectory(outputDirectory);
            BuildRequest request = CreateRequest(BuildIncrementality.Clean);

            using (PlayerOutputTransaction transaction = BeginTransaction(request))
            {
                File.WriteAllText(transaction.StageOutputPath, "published");
                transaction.Commit();
            }

            Assert.That(File.ReadAllText(outputPath), Is.EqualTo("published"));
            Assert.That(
                File.Exists(outputDirectory + ".buildpipeline-player-owner.json"),
                Is.True);
        }

        [Test]
        public void Commit_WhenWindowsTemporarilyDeniesDirectoryRename_RetriesWithoutLosingOutput()
        {
            if (Path.DirectorySeparatorChar != '\\')
            {
                Assert.Ignore("Windows directory sharing semantics are required for this test.");
            }

            PublishOwnedOutput("old");
            BuildRequest request = CreateRequest(BuildIncrementality.Clean);
            using (SafeFileHandle directoryHandle = OpenDirectoryWithoutDeleteSharing(outputDirectory))
            using (PlayerOutputTransaction transaction = BeginTransaction(request))
            {
                Assert.That(directoryHandle.IsInvalid, Is.False);
                File.WriteAllText(transaction.StageOutputPath, "new");
                var releaseThread = new Thread(() =>
                {
                    string readyOwnerPath = transaction.StageRoot + ".owner.json";
                    for (int attempt = 0; attempt < 2000 && !File.Exists(readyOwnerPath); attempt++)
                    {
                        Thread.Sleep(5);
                    }

                    Thread.Sleep(500);
                    directoryHandle.Dispose();
                });
                releaseThread.IsBackground = true;
                releaseThread.Start();
                try
                {
                    transaction.Commit();
                }
                finally
                {
                    directoryHandle.Dispose();
                    releaseThread.Join();
                }
            }

            Assert.That(File.ReadAllText(outputPath), Is.EqualTo("new"));
            AssertNoTransactionScratch();
        }

        [TestCase(PlayerOutputTransaction.PrepareJournalWrittenCheckpoint)]
        [TestCase(PlayerOutputTransaction.PrepareOwnerWrittenCheckpoint)]
        [TestCase(PlayerOutputTransaction.PrepareStageCreatedCheckpoint)]
        [TestCase(PlayerOutputTransaction.PrepareAnchorWrittenCheckpoint)]
        [TestCase(PlayerOutputTransaction.PreparePayloadCreatedCheckpoint)]
        public void RecoverPending_AfterPrepareMutationBoundary_RemovesOnlyOwnedPartialState(
            string interruptedCheckpoint)
        {
            PublishOwnedOutput("old");
            BuildRequest request = CreateRequest(BuildIncrementality.Clean);

            Assert.Throws<PlayerOutputSimulatedTerminationException>(() =>
                PlayerOutputTransaction.Begin(
                    request,
                    request.Steps[0].Incrementality,
                    PlayerBuildExtensionFingerprint.Compute(null),
                    checkpoint =>
                    {
                        if (checkpoint == interruptedCheckpoint)
                        {
                            throw new PlayerOutputSimulatedTerminationException(checkpoint);
                        }
                    }));

            Assert.That(File.Exists(Path.Combine(GetStateRoot(), "active.json")), Is.True);
            Assert.That(File.ReadAllText(outputPath), Is.EqualTo("old"));

            Assert.DoesNotThrow(() => PlayerOutputTransaction.RecoverPending(projectRoot));

            Assert.That(File.Exists(Path.Combine(GetStateRoot(), "active.json")), Is.False);
            Assert.That(File.ReadAllText(outputPath), Is.EqualTo("old"));
            AssertNoTransactionScratch();
            Assert.DoesNotThrow(() =>
            {
                using (PlayerOutputTransaction transaction = BeginTransaction(request))
                {
                }
            });
        }

        [Test]
        public void Begin_WhenPublishedOwnerMatchesOutput_AllowsNextTransaction()
        {
            BuildRequest request = CreateRequest(BuildIncrementality.Clean);
            using (PlayerOutputTransaction transaction = BeginTransaction(request))
            {
                File.WriteAllText(transaction.StageOutputPath, "published");
                transaction.Commit();
            }

            Assert.DoesNotThrow(() =>
            {
            using (PlayerOutputTransaction transaction = BeginTransaction(request))
                {
                }
            });
        }

        [Test]
        public void FolderArtifactStage_PreservesFinalAppBundleName()
        {
            outputDirectory = Path.Combine(buildRoot, "macOS", "Release", "TestProduct.app")
                              + Path.DirectorySeparatorChar;
            outputPath = outputDirectory;
            BuildRequest request = CreateRequest(
                incrementality: BuildIncrementality.Clean,
                target: BuildTarget.StandaloneOSX,
                outputIsFolder: true);

            using (PlayerOutputTransaction transaction = BeginTransaction(request))
            {
                Assert.That(
                    Path.GetFileName(transaction.StageOutputPath),
                    Is.EqualTo("TestProduct.app"));
                string stagedInfo = Path.Combine(
                    transaction.StageOutputPath,
                    "Contents",
                    "Info.plist");
                Directory.CreateDirectory(Path.GetDirectoryName(stagedInfo));
                File.WriteAllText(stagedInfo, "plist");
                transaction.Commit();
            }

            Assert.That(
                File.ReadAllText(Path.Combine(outputDirectory, "Contents", "Info.plist")),
                Is.EqualTo("plist"));
        }

        [Test]
        public void Begin_WhenPlayerStageCannotFitWin32MaxPathBudget_FailsBeforeJournalAndReleasesLock()
        {
            const int desiredFinalDirectoryLength = 180;
            int leafLength = desiredFinalDirectoryLength
                - Path.GetFullPath(buildRoot).Length
                - 1;
            Assert.That(leafLength, Is.GreaterThan(0));
            outputDirectory = Path.Combine(buildRoot, new string('p', leafLength));
            outputPath = Path.Combine(outputDirectory, "TestProduct.exe");
            BuildRequest longPathRequest = CreateRequest(BuildIncrementality.Clean);

            Assert.Throws<PathTooLongException>(() =>
                BeginTransaction(longPathRequest));
            Assert.That(
                File.Exists(Path.Combine(GetStateRoot(), "active.json")),
                Is.False);

            outputDirectory = Path.Combine(buildRoot, "Windows", "Release");
            outputPath = Path.Combine(outputDirectory, "TestProduct.exe");
            Assert.DoesNotThrow(() =>
            {
                using (PlayerOutputTransaction transaction =
                       PlayerOutputTransaction.Begin(
                           CreateRequest(BuildIncrementality.Clean),
                           BuildIncrementality.Clean,
                           PlayerBuildExtensionFingerprint.Compute(null)))
                {
                }
            });
        }

        private BuildRequest CreateRequest(
            BuildIncrementality incrementality,
            BuildTarget target = BuildTarget.StandaloneWindows64,
            bool outputIsFolder = false,
            ScriptingImplementation scriptingBackend = ScriptingImplementation.Mono2x,
            string companyName = "TestCompany",
            string productName = "TestProduct",
            string applicationIdentifier = "com.example.test",
            bool exportAndroidProject = false,
            NamedBuildTarget? namedTarget = null)
        {
            return new BuildRequest(
                companyName,
                productName,
                applicationIdentifier,
                "Assets/Resources/VersionInfoData.asset",
                Array.Empty<string>(),
                CheatBuildMode.Disabled,
                target,
                namedTarget ?? BuildRequestFactory.GetNamedBuildTarget(target),
                scriptingBackend,
                projectRoot,
                buildRoot,
                outputPath,
                outputDirectory,
                outputIsFolder: outputIsFolder,
                deleteDebugFiles: true,
                debugBuild: false,
                exportAndroidProject: exportAndroidProject,
                allowExternalOutput: false,
                cheatOverride: null,
                batchMode: true,
                applicationVersion: "1.0.0",
                identityOverride: BuildIdentityOverride.Empty,
                steps: new[]
                {
                    new BuildStepInvocation(
                        BuildStepTypeIds.Player,
                        BuildStepTypeIds.Player,
                        incrementality: incrementality)
                });
        }

        private static PlayerOutputTransaction BeginTransaction(BuildRequest request)
        {
            return PlayerOutputTransaction.Begin(
                request,
                request.Steps[0].Incrementality,
                PlayerBuildExtensionFingerprint.Compute(null));
        }

        private void PublishOwnedOutput(string contents, Action<string> writeAdditionalFiles = null)
        {
            BuildRequest request = CreateRequest(BuildIncrementality.Clean);
            using (PlayerOutputTransaction transaction = BeginTransaction(request))
            {
                File.WriteAllText(transaction.StageOutputPath, contents);
                writeAdditionalFiles?.Invoke(Path.GetDirectoryName(transaction.StageOutputPath));
                transaction.Commit();
            }
        }

        private void WriteUnownedOutput(string contents)
        {
            Directory.CreateDirectory(outputDirectory);
            File.WriteAllText(outputPath, contents);
        }

        private string PublishForeignOwnedOutput(string contents)
        {
            string savedOutputDirectory = outputDirectory;
            string savedOutputPath = outputPath;
            try
            {
                outputDirectory = Path.Combine(buildRoot, "Foreign", "Release");
                outputPath = Path.Combine(outputDirectory, "TestProduct.exe");
                PublishOwnedOutput(contents);
                return File.ReadAllText(GetOwnerPath());
            }
            finally
            {
                outputDirectory = savedOutputDirectory;
                outputPath = savedOutputPath;
            }
        }

        private string GetOwnerPath()
        {
            return outputDirectory + ".buildpipeline-player-owner.json";
        }

        private void RewritePublishedCompatibility(
            Action<CompatibilityIdentityRecord> mutate)
        {
            string ownerPath = GetOwnerPath();
            OwnerRecord owner = JsonUtility.FromJson<OwnerRecord>(
                File.ReadAllText(ownerPath));
            Assert.That(owner, Is.Not.Null);
            Assert.That(owner.compatibilityIdentity, Is.Not.Null);
            string originalChecksum = owner.checksum;
            owner.checksum = string.Empty;
            Assert.That(
                ComputeTextHash(JsonUtility.ToJson(owner, false)),
                Is.EqualTo(originalChecksum),
                "The test owner DTO must preserve the production checksum contract before mutation.");
            mutate(owner.compatibilityIdentity);
            owner.compatibilityIdentity.digest =
                ComputeCompatibilityDigest(owner.compatibilityIdentity);
            owner.checksum = string.Empty;
            owner.checksum = ComputeTextHash(JsonUtility.ToJson(owner, false));
            File.WriteAllText(
                ownerPath,
                JsonUtility.ToJson(owner, true),
                new UTF8Encoding(false, true));
        }

        private static string ComputeCompatibilityDigest(
            CompatibilityIdentityRecord identity)
        {
            var builder = new StringBuilder(512);
            AppendCompatibilityValue(
                builder,
                identity.formatVersion.ToString(CultureInfo.InvariantCulture));
            AppendCompatibilityValue(
                builder,
                identity.playerPipelineCompatibilityRevision.ToString(
                    CultureInfo.InvariantCulture));
            AppendCompatibilityValue(builder, identity.unityVersion);
            AppendCompatibilityValue(builder, identity.buildTarget);
            AppendCompatibilityValue(builder, identity.namedBuildTarget);
            AppendCompatibilityValue(builder, identity.scriptingBackend);
            AppendCompatibilityValue(builder, identity.outputArtifactPath);
            AppendCompatibilityValue(builder, identity.outputIsFolder);
            AppendCompatibilityValue(builder, identity.companyName);
            AppendCompatibilityValue(builder, identity.productName);
            AppendCompatibilityValue(builder, identity.applicationIdentifier);
            AppendCompatibilityValue(builder, identity.exportAndroidProject);
            AppendCompatibilityValue(builder, identity.debugBuild);
            AppendCompatibilityValue(builder, identity.deleteDebugFiles);
            AppendCompatibilityValue(builder, identity.cheatEnabled);
            AppendCompatibilityValue(builder, identity.playerExtensionFingerprint);
            return ComputeTextHash(builder.ToString());
        }

        private static void AppendCompatibilityValue(
            StringBuilder builder,
            string value)
        {
            string normalized = value ?? string.Empty;
            builder.Append(normalized.Length.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(normalized);
            builder.Append('\n');
        }

        private static void AppendCompatibilityValue(
            StringBuilder builder,
            bool value)
        {
            AppendCompatibilityValue(builder, value ? "1" : "0");
        }

        private static string ComputeTextHash(string text)
        {
            using (SHA256 hash = SHA256.Create())
            {
                byte[] bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(text));
                var builder = new StringBuilder(bytes.Length * 2);
                for (int index = 0; index < bytes.Length; index++)
                {
                    builder.Append(
                        bytes[index].ToString("X2", CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
        }

        private static string ReadOwnerTransactionId(string ownerPath)
        {
            return ReadOwnerTransactionIdFromJson(File.ReadAllText(ownerPath));
        }

        private static string ReadOwnerTransactionIdFromJson(string json)
        {
            OwnerRecord owner = JsonUtility.FromJson<OwnerRecord>(json);
            Assert.That(owner, Is.Not.Null);
            Assert.That(owner.transactionId, Is.Not.Null.And.Not.Empty);
            return owner.transactionId;
        }

        private void AssertNoTransactionScratch()
        {
            string parent = Path.GetDirectoryName(outputDirectory);
            if (!Directory.Exists(parent))
            {
                return;
            }

            foreach (string entry in Directory.GetFileSystemEntries(parent))
            {
                string name = Path.GetFileName(entry);
                Assert.That(name, Does.Not.StartWith(".bps-"));
                Assert.That(name, Does.Not.StartWith(".bpb-"));
            }
        }

        private string GetStateRoot()
        {
            return Path.Combine(
                projectRoot,
                ".buildpipeline",
                "transactions",
                "player");
        }

        private static SafeFileHandle OpenDirectoryWithoutDeleteSharing(string path)
        {
            return CreateFile(
                path,
                GenericRead,
                FileShare.Read | FileShare.Write,
                IntPtr.Zero,
                FileMode.Open,
                FileFlagBackupSemantics,
                IntPtr.Zero);
        }

        private const uint FileFlagBackupSemantics = 0x02000000;
        private const uint GenericRead = 0x80000000;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            FileShare shareMode,
            IntPtr securityAttributes,
            FileMode creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [Serializable]
        private sealed class OwnerRecord
        {
            public int formatVersion = -1;
            public string kind = string.Empty;
            public string transactionId = string.Empty;
            public bool hasIdentity = false;
            public TreeIdentityRecord identity = new TreeIdentityRecord();
            public CompatibilityIdentityRecord compatibilityIdentity = new CompatibilityIdentityRecord();
            public string checksum = string.Empty;
        }

        [Serializable]
        private sealed class CompatibilityIdentityRecord
        {
            public int formatVersion = -1;
            public int playerPipelineCompatibilityRevision = -1;
            public string unityVersion = string.Empty;
            public string buildTarget = string.Empty;
            public string namedBuildTarget = string.Empty;
            public string scriptingBackend = string.Empty;
            public string outputArtifactPath = string.Empty;
            public bool outputIsFolder = false;
            public string companyName = string.Empty;
            public string productName = string.Empty;
            public string applicationIdentifier = string.Empty;
            public bool exportAndroidProject = false;
            public bool debugBuild = false;
            public bool deleteDebugFiles = false;
            public bool cheatEnabled = false;
            public string playerExtensionFingerprint = string.Empty;
            public string digest = string.Empty;
        }

        [Serializable]
        private sealed class TreeIdentityRecord
        {
            public string digest = string.Empty;
            public int entryCount = 0;
            public int fileCount = 0;
            public long totalBytes = 0;
        }
    }
}
