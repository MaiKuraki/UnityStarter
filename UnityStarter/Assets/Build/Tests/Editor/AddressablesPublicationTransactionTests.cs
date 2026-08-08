using System;
using System.IO;
using System.Text;
using Build.Pipeline.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Build.Pipeline.Tests.Editor
{
    public sealed class AddressablesPublicationTransactionTests
    {
        private const string InvocationId = "addressables-main";
        private string projectRoot;
        private string sandboxRoot;
        private string publicationRoot;
        private string destination;

        [SetUp]
        public void SetUp()
        {
            sandboxRoot = Path.Combine(
                Path.GetTempPath(),
                "UnityStarter",
                nameof(AddressablesPublicationTransactionTests),
                Guid.NewGuid().ToString("N"));
            projectRoot = Path.Combine(sandboxRoot, "UnityProject");
            publicationRoot = Path.Combine(projectRoot, "Build", "publication");
            destination = Path.Combine(publicationRoot, "StandaloneWindows64");
            Directory.CreateDirectory(publicationRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(sandboxRoot))
            {
                string expectedParent = Path.GetFullPath(Path.Combine(
                    Path.GetTempPath(),
                    "UnityStarter",
                    nameof(AddressablesPublicationTransactionTests)));
                string candidate = Path.GetFullPath(sandboxRoot);
                Assert.That(Path.GetDirectoryName(candidate), Is.EqualTo(expectedParent));
                Assert.That(Guid.TryParseExact(Path.GetFileName(candidate), "N", out _), Is.True);
                Directory.Delete(sandboxRoot, recursive: true);
            }
        }

        [Test]
        public void Commit_ReplacesOwnedDestinationAndRemovesTransactionState()
        {
            CreateOwnedPublication(destination, Guid.NewGuid().ToString("N"), "old");
            var transaction = AddressablesPublicationTransaction.Begin(
                    projectRoot,
                    publicationRoot,
                    destination,
                    InvocationId);
            try
            {
                transaction.Prepare();
                string stagedIdentity = CreateOwnedPublication(
                    transaction.StagingDirectory,
                    transaction.TransactionId,
                    "new");
                transaction.MarkStageReady(stagedIdentity);
                transaction.Commit(null);

                Assert.That(File.Exists(Path.Combine(destination, "PlayerData", "new.bundle")), Is.True);
                Assert.That(File.Exists(Path.Combine(destination, "PlayerData", "old.bundle")), Is.False);
                Assert.That(
                    File.Exists(Path.Combine(
                        AddressablesPublicationTransaction.GetStateRoot(projectRoot, InvocationId),
                        "active.json")),
                    Is.False);
                Assert.That(
                    Directory.Exists(AddressablesPublicationTransaction.GetStateRoot(projectRoot, InvocationId)),
                    Is.False);
                Assert.That(
                    Directory.Exists(AddressablesPublicationTransaction.GetProviderStateRoot(projectRoot)),
                    Is.False);
                Assert.That(
                    Directory.GetDirectories(publicationRoot, "*.stage-*", SearchOption.TopDirectoryOnly),
                    Is.Empty);
                Assert.That(
                    Directory.GetDirectories(publicationRoot, "*.backup-*", SearchOption.TopDirectoryOnly),
                    Is.Empty);
            }
            finally
            {
                transaction.Dispose();
            }
        }

        [Test]
        public void EnsureNoPendingRecovery_WhenNoStateExists_IsZeroWrite()
        {
            string stateRoot = AddressablesPublicationTransaction.GetStateRoot(projectRoot, InvocationId);

            Assert.That(Directory.Exists(stateRoot), Is.False);
            Assert.DoesNotThrow(() =>
                AddressablesPublicationTransaction.EnsureNoPendingRecovery(projectRoot, InvocationId));
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
                AddressablesPublicationTransaction.GetStateRoot(
                    projectRoot,
                    invocationId));
        }

        [Test]
        public void EnsureNoPendingRecovery_WhenPreparedJournalExists_FailsClosedWithoutChangingIt()
        {
            var transaction = AddressablesPublicationTransaction.Begin(
                    projectRoot,
                    publicationRoot,
                    destination,
                    InvocationId);
            try
            {
                transaction.Prepare();
                string journalPath = Path.Combine(
                    AddressablesPublicationTransaction.GetStateRoot(projectRoot, InvocationId),
                    "active.json");
                byte[] journalBeforeRetry = File.ReadAllBytes(journalPath);

                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                    AddressablesPublicationTransaction.EnsureNoPendingRecovery(projectRoot, InvocationId));

                StringAssert.Contains("Pending Addressables publication recovery", exception.Message);
                Assert.That(File.ReadAllBytes(journalPath), Is.EqualTo(journalBeforeRetry));
                Assert.That(Directory.Exists(transaction.StagingDirectory), Is.True);
            }
            finally
            {
                transaction.Dispose();
            }
        }

        [Test]
        public void InvocationIsolation_AllowsIndependentPendingTransactionsForSameProvider()
        {
            const string secondInvocationId = "addressables-secondary";
            string secondDestination = Path.Combine(
                publicationRoot,
                "StandaloneWindows64-Secondary");
            var first = AddressablesPublicationTransaction.Begin(
                projectRoot,
                publicationRoot,
                destination,
                InvocationId);
            var second = AddressablesPublicationTransaction.Begin(
                projectRoot,
                publicationRoot,
                secondDestination,
                secondInvocationId);
            try
            {
                first.Prepare();
                second.Prepare();

                Assert.That(first.PublicationId, Is.Not.EqualTo(second.PublicationId));
                Assert.That(first.StateRelativePath, Is.Not.EqualTo(second.StateRelativePath));
                Assert.That(
                    File.Exists(Path.Combine(
                        AddressablesPublicationTransaction.GetStateRoot(
                            projectRoot,
                            InvocationId),
                        "active.json")),
                    Is.True);
                Assert.That(
                    File.Exists(Path.Combine(
                        AddressablesPublicationTransaction.GetStateRoot(
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
                Directory.Exists(AddressablesPublicationTransaction.GetStateRoot(
                    projectRoot,
                    InvocationId)),
                Is.False);
            Assert.That(
                Directory.Exists(AddressablesPublicationTransaction.GetStateRoot(
                    projectRoot,
                    secondInvocationId)),
                Is.False);
            Assert.That(
                Directory.Exists(AddressablesPublicationTransaction.GetProviderStateRoot(projectRoot)),
                Is.False);
        }

        [Test]
        public void Commit_WhenExistingTreeWouldExceedMappedBackupBudget_FailsBeforeMove()
        {
            const int desiredSourceFileLength = 245;
            int labelLength = desiredSourceFileLength
                - Path.Combine(destination, "PlayerData").Length
                - 1
                - ".bundle".Length;
            Assert.That(labelLength, Is.InRange(1, 240));
            string label = new string('o', labelLength);
            string originalArtifactPath = Path.Combine(
                destination,
                "PlayerData",
                label + ".bundle");
            CreateOwnedPublication(
                destination,
                Guid.NewGuid().ToString("N"),
                label);
            Assert.That(originalArtifactPath.Length, Is.EqualTo(desiredSourceFileLength));
            var transaction = PrepareReadyTransaction("new");
            try
            {
                Assert.Throws<PathTooLongException>(() => transaction.Commit(null));
                Assert.That(File.Exists(originalArtifactPath), Is.True);
                Assert.That(
                    Directory.GetDirectories(
                        publicationRoot,
                        "*.backup-*",
                        SearchOption.TopDirectoryOnly),
                    Is.Empty);
                Assert.That(
                    File.Exists(Path.Combine(
                        AddressablesPublicationTransaction.GetStateRoot(projectRoot, InvocationId),
                        "active.json")),
                    Is.False);
            }
            finally
            {
                transaction.Dispose();
            }
        }

        [Test]
        public void Prepare_NonEmptyUnownedDestination_FailsClosedWithoutDeletingIt()
        {
            Directory.CreateDirectory(destination);
            string authoredPath = Path.Combine(destination, "authored.txt");
            File.WriteAllText(authoredPath, "keep");
            var transaction = AddressablesPublicationTransaction.Begin(
                    projectRoot,
                    publicationRoot,
                    destination,
                    InvocationId);
            try
            {
                Assert.Throws<InvalidDataException>(() => transaction.Prepare());
                Assert.That(File.ReadAllText(authoredPath), Is.EqualTo("keep"));
            }
            finally
            {
                transaction.Dispose();
            }
        }

        [Test]
        public void Begin_WhenTransactionScratchWouldExceedWindowsPathBudget_FailsBeforeStateCreation()
        {
            const int desiredDestinationLength = 225;
            int segmentLength = desiredDestinationLength - publicationRoot.Length - 1;
            Assert.That(segmentLength, Is.GreaterThan(0));
            string longDestination = Path.Combine(
                publicationRoot,
                new string('d', segmentLength));
            Assert.That(
                longDestination.Length,
                Is.LessThanOrEqualTo(BuildPathPolicy.Win32MaxPathCharacters));

            Assert.Throws<PathTooLongException>(() =>
                AddressablesPublicationTransaction.Begin(
                    projectRoot,
                    publicationRoot,
                    longDestination,
                    InvocationId));
            Assert.That(
                Directory.Exists(AddressablesPublicationTransaction.GetStateRoot(projectRoot, InvocationId)),
                Is.False);
        }

        [Test]
        public void DurableTextWriter_CreatesBomlessFileAndRefusesOverwrite()
        {
            string path = Path.Combine(publicationRoot, "AddressablesVersion.json");

            AddressablesBuilder.WriteNewTextDurably(path, "version-1");

            Assert.That(File.ReadAllBytes(path), Is.EqualTo(new UTF8Encoding(false).GetBytes("version-1")));
            Assert.Throws<IOException>(() =>
                AddressablesBuilder.WriteNewTextDurably(path, "version-2"));
            Assert.That(File.ReadAllText(path), Is.EqualTo("version-1"));
        }

        [Test]
        public void VersionArtifactDurableUpsert_SupportsRepeatedBuildsAndCleansScratch()
        {
            string path = Path.Combine(publicationRoot, "AddressablesVersion.json");

            AddressablesBuilder.WriteVersionArtifactDurably(projectRoot, path, "1.0.0");
            AddressablesBuilder.WriteVersionArtifactDurably(projectRoot, path, "1.0.1");

            StringAssert.Contains("1.0.1", File.ReadAllText(path));
            Assert.That(
                File.Exists(Path.Combine(
                    publicationRoot,
                    AddressablesBuilder.VersionArtifactTemporaryFileName)),
                Is.False);
            Assert.That(
                File.Exists(Path.Combine(
                    publicationRoot,
                    AddressablesBuilder.VersionArtifactBackupFileName)),
                Is.False);
        }

        [TestCase(AddressablesBuilder.VersionArtifactTemporaryFileName)]
        [TestCase(AddressablesBuilder.VersionArtifactBackupFileName)]
        public void VersionArtifactDurableUpsert_RecoversValidScratchBeforeWriting(
            string scratchFileName)
        {
            string path = Path.Combine(publicationRoot, "AddressablesVersion.json");
            string scratchPath = Path.Combine(publicationRoot, scratchFileName);
            AddressablesBuilder.WriteNewTextDurably(
                scratchPath,
                "{\"contentVersion\":\"interrupted\"}");

            AddressablesBuilder.WriteVersionArtifactDurably(projectRoot, path, "current");

            StringAssert.Contains("current", File.ReadAllText(path));
            Assert.That(File.Exists(scratchPath), Is.False);
            Assert.That(
                File.Exists(Path.Combine(
                    publicationRoot,
                    AddressablesBuilder.VersionArtifactTemporaryFileName)),
                Is.False);
            Assert.That(
                File.Exists(Path.Combine(
                    publicationRoot,
                    AddressablesBuilder.VersionArtifactBackupFileName)),
                Is.False);
        }

        [Test]
        public void VersionArtifactDurableUpsert_CorruptScratchFailsClosed()
        {
            string path = Path.Combine(publicationRoot, "AddressablesVersion.json");
            string temporaryPath = Path.Combine(
                publicationRoot,
                AddressablesBuilder.VersionArtifactTemporaryFileName);
            AddressablesBuilder.WriteVersionArtifactDurably(projectRoot, path, "preserve");
            File.WriteAllText(temporaryPath, "corrupt");

            Assert.Throws<InvalidDataException>(() =>
                AddressablesBuilder.WriteVersionArtifactDurably(
                    projectRoot,
                    path,
                    "replacement"));
            StringAssert.Contains("preserve", File.ReadAllText(path));
            Assert.That(File.Exists(temporaryPath), Is.True);
        }

        [Test]
        public void VersionArtifactReader_OversizedFileFailsClosed()
        {
            string path = Path.Combine(publicationRoot, "AddressablesVersion.json");
            File.WriteAllBytes(path, new byte[(64 * 1024) + 1]);

            Assert.Throws<InvalidDataException>(() =>
                AddressablesBuilder.ReadAndValidateVersionArtifact(
                    projectRoot,
                    path,
                    expectedContentVersion: null));
        }

        [Test]
        public void VersionArtifactReader_MalformedJsonFailsClosed()
        {
            string path = Path.Combine(publicationRoot, "AddressablesVersion.json");
            File.WriteAllText(path, "not-json", new UTF8Encoding(false));

            Assert.Throws<InvalidDataException>(() =>
                AddressablesBuilder.ReadAndValidateVersionArtifact(
                    projectRoot,
                    path,
                    expectedContentVersion: null));
        }

        [TestCase(AddressablesPublicationTransaction.BackupMovedCheckpoint)]
        [TestCase(AddressablesPublicationTransaction.InstalledCheckpoint)]
        public void RecoverPending_InterruptedCommit_RestoresOriginalPublication(string checkpoint)
        {
            CreateOwnedPublication(destination, Guid.NewGuid().ToString("N"), "old");
            var transaction = PrepareReadyTransaction("new");
            try
            {
                Assert.Throws<AddressablesSimulatedTerminationException>(
                    () => transaction.Commit(
                        null,
                        current =>
                        {
                            if (string.Equals(current, checkpoint, StringComparison.Ordinal))
                            {
                                throw new AddressablesSimulatedTerminationException(current);
                            }
                        }));

                AddressablesPublicationTransaction.RecoverPending(
                    projectRoot,
                    InvocationId,
                    publicationRoot,
                    destination);

                Assert.That(File.Exists(Path.Combine(destination, "PlayerData", "old.bundle")), Is.True);
                Assert.That(File.Exists(Path.Combine(destination, "PlayerData", "new.bundle")), Is.False);
            }
            finally
            {
                transaction.Dispose();
            }
        }

        [Test]
        public void RecoverPending_CommittedBeforeCleanup_KeepsNewPublication()
        {
            CreateOwnedPublication(destination, Guid.NewGuid().ToString("N"), "old");
            var transaction = PrepareReadyTransaction("new");
            try
            {
                Assert.Throws<AddressablesSimulatedTerminationException>(
                    () => transaction.Commit(
                        null,
                        current =>
                        {
                            if (string.Equals(
                                current,
                                AddressablesPublicationTransaction.CommittedCheckpoint,
                                StringComparison.Ordinal))
                            {
                                throw new AddressablesSimulatedTerminationException(current);
                            }
                        }));

                AddressablesPublicationTransaction.RecoverPending(
                    projectRoot,
                    InvocationId,
                    publicationRoot,
                    destination);

                Assert.That(File.Exists(Path.Combine(destination, "PlayerData", "new.bundle")), Is.True);
                Assert.That(File.Exists(Path.Combine(destination, "PlayerData", "old.bundle")), Is.False);
            }
            finally
            {
                transaction.Dispose();
            }
        }

        [Test]
        public void RecoverPending_InstalledTargetWasExternallyReplaced_FailsClosedAndRetainsBackup()
        {
            CreateOwnedPublication(destination, Guid.NewGuid().ToString("N"), "old");
            var transaction = PrepareReadyTransaction("new");
            try
            {
                Assert.Throws<AddressablesSimulatedTerminationException>(
                    () => transaction.Commit(
                        null,
                        current =>
                        {
                            if (string.Equals(
                                current,
                                AddressablesPublicationTransaction.InstalledCheckpoint,
                                StringComparison.Ordinal))
                            {
                                throw new AddressablesSimulatedTerminationException(current);
                            }
                        }));

                Directory.Delete(destination, recursive: true);
                Directory.CreateDirectory(destination);
                string externalPath = Path.Combine(destination, "external.txt");
                File.WriteAllText(externalPath, "do not delete");

                Assert.Throws<AggregateException>(
                    () => AddressablesPublicationTransaction.RecoverPending(
                    projectRoot,
                    InvocationId,
                    publicationRoot,
                    destination));
                Assert.That(File.ReadAllText(externalPath), Is.EqualTo("do not delete"));
                Assert.That(
                    Directory.GetDirectories(publicationRoot, "*.backup-*", SearchOption.TopDirectoryOnly),
                    Is.Not.Empty);
            }
            finally
            {
                transaction.Dispose();
            }
        }

        [Test]
        public void RecoverPending_CorruptJournalChecksum_FailsClosed()
        {
            var transaction = PrepareReadyTransaction("new");
            try
            {
                string journalPath = Path.Combine(
                    AddressablesPublicationTransaction.GetStateRoot(projectRoot, InvocationId),
                    "active.json");
                string journal = File.ReadAllText(journalPath);
                File.WriteAllText(
                    journalPath,
                    journal.Replace("\"phase\": \"Prepared\"", "\"phase\": \"Committing\""));

                Assert.Throws<InvalidDataException>(
                    () => AddressablesPublicationTransaction.RecoverPending(
                    projectRoot,
                    InvocationId,
                    publicationRoot,
                    destination));
                Assert.That(File.Exists(journalPath), Is.True);
                Assert.That(Directory.Exists(transaction.StagingDirectory), Is.True);
            }
            finally
            {
                transaction.Dispose();
            }
        }

        [Test]
        public void BuildLock_SecondAcquisitionForSameProject_Fails()
        {
            using (AddressablesBuildLock.Acquire(projectRoot))
            {
                Assert.Throws<InvalidOperationException>(
                    () =>
                    {
                        using (AddressablesBuildLock.Acquire(projectRoot))
                        {
                        }
                    });
            }
        }

        [Test]
        public void Abort_WhenUnreadyStageWasExternallyReplaced_FailsClosedAndRetainsJournal()
        {
            var transaction = AddressablesPublicationTransaction.Begin(
                    projectRoot,
                    publicationRoot,
                    destination,
                    InvocationId);
            transaction.Prepare();
            Directory.Delete(transaction.StagingDirectory, recursive: true);
            Directory.CreateDirectory(transaction.StagingDirectory);
            File.WriteAllText(
                Path.Combine(transaction.StagingDirectory, "external.txt"),
                "not owned by the transaction");

            Assert.Throws<AggregateException>(() => transaction.Abort());
            Assert.That(
                File.Exists(Path.Combine(
                    AddressablesPublicationTransaction.GetStateRoot(projectRoot, InvocationId),
                    "active.json")),
                Is.True);
            Assert.That(
                File.Exists(Path.Combine(transaction.StagingDirectory, "external.txt")),
                Is.True);
            Assert.Throws<AggregateException>(() => transaction.Dispose());
        }

        [Test]
        public void RecoverPending_WhenPublicationConfigChanged_RecoversRecordedTransaction()
        {
            string originalIdentity = CreateOwnedPublication(
                destination,
                Guid.NewGuid().ToString("N"),
                "old");
            var transaction = PrepareReadyTransaction("new");
            try
            {
                Assert.Throws<AddressablesSimulatedTerminationException>(() =>
                    transaction.Commit(
                        null,
                        reached =>
                        {
                            if (reached == AddressablesPublicationTransaction.BackupMovedCheckpoint)
                            {
                                throw new AddressablesSimulatedTerminationException(reached);
                            }
                        }));

                string newPublicationRoot = Path.Combine(
                    projectRoot,
                    "Build",
                    "changed-publication");
                string newDestination = Path.Combine(
                    newPublicationRoot,
                    "StandaloneWindows64");
                AddressablesPublicationTransaction.RecoverPending(
                    projectRoot,
                    InvocationId,
                    newPublicationRoot,
                    newDestination);

                Assert.That(
                    AddressablesPublicationOwnership.CaptureIdentity(destination),
                    Is.EqualTo(originalIdentity));
                Assert.That(Directory.Exists(newDestination), Is.False);
                Assert.That(
                    File.Exists(Path.Combine(
                        AddressablesPublicationTransaction.GetStateRoot(projectRoot, InvocationId),
                        "active.json")),
                    Is.False);
            }
            finally
            {
                transaction.Dispose();
            }
        }

        [TestCase("active.json.tmp")]
        [TestCase("active.json.bak")]
        public void RecoverPending_ProjectOnlyRecoveryPromotesValidJournalScratch(
            string scratchFileName)
        {
            var transaction = AddressablesPublicationTransaction.Begin(
                    projectRoot,
                    publicationRoot,
                    destination,
                    InvocationId);
            transaction.Prepare();
            string stateRoot = AddressablesPublicationTransaction.GetStateRoot(projectRoot, InvocationId);
            string journalPath = Path.Combine(stateRoot, "active.json");
            string scratchPath = Path.Combine(stateRoot, scratchFileName);
            File.Move(journalPath, scratchPath);

            AddressablesPublicationTransaction.RecoverPending(projectRoot);
            AddressablesPublicationTransaction.RecoverPending(projectRoot);

            Assert.That(File.Exists(journalPath), Is.False);
            Assert.That(File.Exists(scratchPath), Is.False);
            Assert.That(Directory.Exists(transaction.StagingDirectory), Is.False);
            transaction.Dispose();
        }

        [TestCase("active.json.tmp")]
        [TestCase("active.json.bak")]
        public void RecoverPending_CorruptScratchBesideActiveJournal_FailsClosed(
            string scratchFileName)
        {
            var transaction = AddressablesPublicationTransaction.Begin(
                    projectRoot,
                    publicationRoot,
                    destination,
                    InvocationId);
            transaction.Prepare();
            string stateRoot = AddressablesPublicationTransaction.GetStateRoot(projectRoot, InvocationId);
            string journalPath = Path.Combine(stateRoot, "active.json");
            string scratchPath = Path.Combine(stateRoot, scratchFileName);
            File.WriteAllText(scratchPath, "corrupt");

            Assert.Throws<InvalidDataException>(
                () => AddressablesPublicationTransaction.RecoverPending(projectRoot));
            Assert.That(File.Exists(journalPath), Is.True);
            Assert.That(File.Exists(scratchPath), Is.True);
            Assert.That(Directory.Exists(transaction.StagingDirectory), Is.True);
            GC.KeepAlive(transaction);
        }

        [TestCase("active.json.tmp")]
        [TestCase("active.json.bak")]
        public void RecoverPending_ScratchDirectoryBesideActiveJournal_FailsClosed(
            string scratchFileName)
        {
            var transaction = AddressablesPublicationTransaction.Begin(
                    projectRoot,
                    publicationRoot,
                    destination,
                    InvocationId);
            transaction.Prepare();
            string stateRoot = AddressablesPublicationTransaction.GetStateRoot(projectRoot, InvocationId);
            string journalPath = Path.Combine(stateRoot, "active.json");
            string scratchPath = Path.Combine(stateRoot, scratchFileName);
            Directory.CreateDirectory(scratchPath);

            Assert.Throws<InvalidOperationException>(
                () => AddressablesPublicationTransaction.RecoverPending(projectRoot));
            Assert.That(File.Exists(journalPath), Is.True);
            Assert.That(Directory.Exists(scratchPath), Is.True);
            GC.KeepAlive(transaction);
        }

        [Test]
        public void RecoverPending_BackupWithCorruptTemporaryJournal_FailsBeforePromotion()
        {
            var transaction = AddressablesPublicationTransaction.Begin(
                    projectRoot,
                    publicationRoot,
                    destination,
                    InvocationId);
            transaction.Prepare();
            string stateRoot = AddressablesPublicationTransaction.GetStateRoot(projectRoot, InvocationId);
            string journalPath = Path.Combine(stateRoot, "active.json");
            string backupPath = Path.Combine(stateRoot, "active.json.bak");
            string temporaryPath = Path.Combine(stateRoot, "active.json.tmp");
            File.Move(journalPath, backupPath);
            File.WriteAllText(temporaryPath, "corrupt");

            Assert.Throws<InvalidDataException>(
                () => AddressablesPublicationTransaction.RecoverPending(projectRoot));
            Assert.That(File.Exists(journalPath), Is.False);
            Assert.That(File.Exists(backupPath), Is.True);
            Assert.That(File.Exists(temporaryPath), Is.True);
            Assert.That(Directory.Exists(transaction.StagingDirectory), Is.True);
            GC.KeepAlive(transaction);
        }

        [Test]
        public void RecoverPending_ActiveWithBothValidScratchFiles_FailsClosed()
        {
            var transaction = AddressablesPublicationTransaction.Begin(
                    projectRoot,
                    publicationRoot,
                    destination,
                    InvocationId);
            transaction.Prepare();
            string stateRoot = AddressablesPublicationTransaction.GetStateRoot(projectRoot, InvocationId);
            string journalPath = Path.Combine(stateRoot, "active.json");
            string temporaryPath = Path.Combine(stateRoot, "active.json.tmp");
            string backupPath = Path.Combine(stateRoot, "active.json.bak");
            File.Copy(journalPath, temporaryPath);
            File.Copy(journalPath, backupPath);

            Assert.Throws<InvalidDataException>(
                () => AddressablesPublicationTransaction.RecoverPending(projectRoot));
            Assert.That(File.Exists(journalPath), Is.True);
            Assert.That(File.Exists(temporaryPath), Is.True);
            Assert.That(File.Exists(backupPath), Is.True);
            GC.KeepAlive(transaction);
        }

        private AddressablesPublicationTransaction PrepareReadyTransaction(string label)
        {
            var transaction = AddressablesPublicationTransaction.Begin(
                    projectRoot,
                    publicationRoot,
                    destination,
                    InvocationId);
            transaction.Prepare();
            string stagedIdentity = CreateOwnedPublication(
                transaction.StagingDirectory,
                transaction.TransactionId,
                label);
            transaction.MarkStageReady(stagedIdentity);
            return transaction;
        }

        private static string CreateOwnedPublication(
            string root,
            string transactionId,
            string label)
        {
            Directory.CreateDirectory(root);
            string ownerPath = Path.Combine(
                root,
                AddressablesPublicationOwnership.OwnerFileName);
            if (!File.Exists(ownerPath))
            {
                AddressablesPublicationOwnership.WriteStageMarker(root, transactionId);
            }

            string relativePath = "PlayerData/" + label + ".bundle";
            string artifactPath = Path.Combine(root, "PlayerData", label + ".bundle");
            Directory.CreateDirectory(Path.GetDirectoryName(artifactPath));
            byte[] artifactBytes = new UTF8Encoding(false).GetBytes(label);
            File.WriteAllBytes(artifactPath, artifactBytes);

            var manifest = new AddressablesArtifactManifest
            {
                buildTarget = "StandaloneWindows64",
                contentVersion = "test",
                files = new[]
                {
                    new AddressablesArtifactManifestEntry
                    {
                        kind = "PlayerData",
                        path = relativePath,
                        size = artifactBytes.Length,
                        sha256 = AddressablesBuilder.ComputeSha256(artifactPath)
                    }
                }
            };
            File.WriteAllText(
                Path.Combine(root, AddressablesPublicationOwnership.ArtifactManifestFileName),
                AddressablesArtifactManifestFormat.Serialize(
                    manifest,
                    prettyPrint: true),
                new UTF8Encoding(false));
            AddressablesPublicationOwnership.WriteOwner(root, transactionId);
            return AddressablesPublicationOwnership.CaptureIdentity(root, transactionId);
        }

    }
}
