using System;
using System.IO;
using System.Linq;
using Build.Pipeline.Editor;
using NUnit.Framework;

namespace Build.Pipeline.Tests.Editor
{
    public sealed class AddressablesSettingsTransactionTests
    {
        private string sandboxRoot;
        private string projectRoot;
        private string assetPath;
        private string metaPath;
        private byte[] originalAssetBytes;
        private byte[] originalMetaBytes;
        private DateTime originalAssetTimeUtc;
        private DateTime originalMetaTimeUtc;
        private FileAttributes originalAssetAttributes;
        private FileAttributes originalMetaAttributes;

        [SetUp]
        public void SetUp()
        {
            sandboxRoot = Path.Combine(
                Path.GetTempPath(),
                "UnityStarter-AddressablesSettingsTests",
                Guid.NewGuid().ToString("N"));
            projectRoot = Path.Combine(sandboxRoot, "UnityProject");
            string configurationDirectory = Path.Combine(
                projectRoot,
                "Assets",
                "AddressableAssetsData");
            Directory.CreateDirectory(configurationDirectory);
            assetPath = Path.Combine(configurationDirectory, "AddressableAssetSettings.asset");
            metaPath = assetPath + ".meta";
            originalAssetBytes = new byte[] { 1, 3, 5, 7, 9 };
            originalMetaBytes = new byte[] { 2, 4, 6, 8 };
            File.WriteAllBytes(assetPath, originalAssetBytes);
            File.WriteAllBytes(metaPath, originalMetaBytes);
            File.SetLastWriteTimeUtc(
                assetPath,
                new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(
                metaPath,
                new DateTime(2024, 2, 3, 4, 5, 6, DateTimeKind.Utc));
            originalAssetTimeUtc = File.GetLastWriteTimeUtc(assetPath);
            originalMetaTimeUtc = File.GetLastWriteTimeUtc(metaPath);
            originalAssetAttributes = File.GetAttributes(assetPath);
            originalMetaAttributes = File.GetAttributes(metaPath);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(sandboxRoot))
            {
                Directory.Delete(sandboxRoot, recursive: true);
            }
        }

        [Test]
        public void RestoreAndComplete_RestoresAssetMetaAndRemovesOwnedState()
        {
            AddressablesSettingsTransaction transaction = BeginTransaction();
            MutateConfigurationFiles();

            transaction.RestoreAndComplete();

            AssertOriginalIdentity();
            AssertStateCleared();
            Assert.That(
                Directory.EnumerateFileSystemEntries(
                    Path.Combine(projectRoot, "Assets"),
                    "*restore*",
                    SearchOption.AllDirectories),
                Is.Empty);
        }

        [TestCase(AddressablesSettingsTransaction.JournalPreparedCheckpoint)]
        [TestCase(AddressablesSettingsTransaction.TransactionDirectoryCreatedCheckpoint)]
        [TestCase(AddressablesSettingsTransaction.OwnerTemporaryWrittenCheckpoint)]
        [TestCase(AddressablesSettingsTransaction.OwnerInstalledCheckpoint)]
        [TestCase(AddressablesSettingsTransaction.SnapshotWrittenCheckpointPrefix + "0")]
        [TestCase(AddressablesSettingsTransaction.SnapshotWrittenCheckpointPrefix + "1")]
        public void RecoverPending_WhenPreparationTerminates_RestoresDeterministically(
            string terminationCheckpoint)
        {
            Assert.Throws<AddressablesSettingsSimulatedTerminationException>(() =>
                AddressablesSettingsTransaction.Begin(
                    projectRoot,
                    CreateSnapshots(),
                    importAssets: false,
                    checkpoint: reached =>
                    {
                        if (string.Equals(
                                reached,
                                terminationCheckpoint,
                                StringComparison.Ordinal))
                        {
                            throw new AddressablesSettingsSimulatedTerminationException(reached);
                        }
                    }));

            AddressablesSettingsTransaction.RecoverPending(
                projectRoot,
                importAssets: false);

            AssertOriginalIdentity();
            AssertStateCleared();
        }

        [TestCase(AddressablesSettingsTransaction.RestoredCheckpoint)]
        [TestCase(AddressablesSettingsTransaction.TransactionDirectoryDeletedCheckpoint)]
        public void RecoverPending_WhenRestorationCleanupTerminates_CompletesCleanup(
            string terminationCheckpoint)
        {
            AddressablesSettingsTransaction transaction = BeginTransaction();
            MutateConfigurationFiles();
            Assert.Throws<AddressablesSettingsSimulatedTerminationException>(() =>
                transaction.RestoreAndComplete(reached =>
                {
                    if (string.Equals(reached, terminationCheckpoint, StringComparison.Ordinal))
                    {
                        throw new AddressablesSettingsSimulatedTerminationException(reached);
                    }
                }));

            AddressablesSettingsTransaction.RecoverPending(
                projectRoot,
                importAssets: false);

            AssertOriginalIdentity();
            AssertStateCleared();
        }

        [Test]
        public void RecoverPending_WhenSnapshotIsCorrupt_FailsClosedAndRetainsJournal()
        {
            AddressablesSettingsTransaction transaction = BeginTransaction();
            MutateConfigurationFiles();
            string stateRoot = GetStateRoot();
            string transactionDirectory = Directory.EnumerateDirectories(
                    stateRoot,
                    "transaction-*",
                    SearchOption.TopDirectoryOnly)
                .Single();
            File.WriteAllText(Path.Combine(transactionDirectory, "0000.snapshot"), "corrupt");

            Assert.Throws<IOException>(() =>
                AddressablesSettingsTransaction.RecoverPending(
                    projectRoot,
                    importAssets: false));
            Assert.That(File.Exists(Path.Combine(stateRoot, "active.json")), Is.True);
            Assert.That(File.ReadAllBytes(assetPath), Is.Not.EqualTo(originalAssetBytes));

            GC.KeepAlive(transaction);
        }

        [Test]
        public void RecoverPending_WhenJournalPathIsDirectory_FailsClosed()
        {
            string stateRoot = GetStateRoot();
            Directory.CreateDirectory(Path.Combine(stateRoot, "active.json"));

            Assert.Throws<InvalidOperationException>(() =>
                AddressablesSettingsTransaction.RecoverPending(
                    projectRoot,
                    importAssets: false));
            Assert.That(Directory.Exists(Path.Combine(stateRoot, "active.json")), Is.True);
        }

        [TestCase("active.json.tmp")]
        [TestCase("active.json.bak")]
        public void RecoverPending_ValidJournalScratch_IsPromotedAndRecoveryIsIdempotent(
            string scratchFileName)
        {
            AddressablesSettingsTransaction transaction = BeginTransaction();
            MutateConfigurationFiles();
            string stateRoot = GetStateRoot();
            string journalPath = Path.Combine(stateRoot, "active.json");
            string scratchPath = Path.Combine(stateRoot, scratchFileName);
            File.Move(journalPath, scratchPath);

            AddressablesSettingsTransaction.RecoverPending(projectRoot, importAssets: false);
            AddressablesSettingsTransaction.RecoverPending(projectRoot, importAssets: false);

            AssertOriginalIdentity();
            AssertStateCleared();
            transaction.Dispose();
        }

        [TestCase("active.json.tmp")]
        [TestCase("active.json.bak")]
        public void RecoverPending_CorruptScratchBesideActiveJournal_FailsClosed(
            string scratchFileName)
        {
            AddressablesSettingsTransaction transaction = BeginTransaction();
            MutateConfigurationFiles();
            string stateRoot = GetStateRoot();
            string journalPath = Path.Combine(stateRoot, "active.json");
            string scratchPath = Path.Combine(stateRoot, scratchFileName);
            File.WriteAllText(scratchPath, "corrupt");

            Assert.Throws<InvalidDataException>(() =>
                AddressablesSettingsTransaction.RecoverPending(projectRoot, importAssets: false));
            Assert.That(File.Exists(journalPath), Is.True);
            Assert.That(File.Exists(scratchPath), Is.True);
            Assert.That(File.ReadAllBytes(assetPath), Is.Not.EqualTo(originalAssetBytes));
            GC.KeepAlive(transaction);
        }

        [TestCase("active.json.tmp")]
        [TestCase("active.json.bak")]
        public void RecoverPending_ScratchDirectoryBesideActiveJournal_FailsClosed(
            string scratchFileName)
        {
            AddressablesSettingsTransaction transaction = BeginTransaction();
            string stateRoot = GetStateRoot();
            string journalPath = Path.Combine(stateRoot, "active.json");
            string scratchPath = Path.Combine(stateRoot, scratchFileName);
            Directory.CreateDirectory(scratchPath);

            Assert.Throws<InvalidOperationException>(() =>
                AddressablesSettingsTransaction.RecoverPending(projectRoot, importAssets: false));
            Assert.That(File.Exists(journalPath), Is.True);
            Assert.That(Directory.Exists(scratchPath), Is.True);
            GC.KeepAlive(transaction);
        }

        [Test]
        public void RecoverPending_BackupWithCorruptTemporaryJournal_FailsBeforePromotion()
        {
            AddressablesSettingsTransaction transaction = BeginTransaction();
            MutateConfigurationFiles();
            string stateRoot = GetStateRoot();
            string journalPath = Path.Combine(stateRoot, "active.json");
            string backupPath = Path.Combine(stateRoot, "active.json.bak");
            string temporaryPath = Path.Combine(stateRoot, "active.json.tmp");
            File.Move(journalPath, backupPath);
            File.WriteAllText(temporaryPath, "corrupt");

            Assert.Throws<InvalidDataException>(() =>
                AddressablesSettingsTransaction.RecoverPending(projectRoot, importAssets: false));
            Assert.That(File.Exists(journalPath), Is.False);
            Assert.That(File.Exists(backupPath), Is.True);
            Assert.That(File.Exists(temporaryPath), Is.True);
            Assert.That(File.ReadAllBytes(assetPath), Is.Not.EqualTo(originalAssetBytes));
            GC.KeepAlive(transaction);
        }

        [Test]
        public void RecoverPending_ValidButNonSuccessorScratch_FailsClosed()
        {
            AddressablesSettingsTransaction transaction = BeginTransaction();
            string stateRoot = GetStateRoot();
            string journalPath = Path.Combine(stateRoot, "active.json");
            string temporaryPath = Path.Combine(stateRoot, "active.json.tmp");
            File.Copy(journalPath, temporaryPath);

            Assert.Throws<InvalidDataException>(() =>
                AddressablesSettingsTransaction.RecoverPending(projectRoot, importAssets: false));
            Assert.That(File.Exists(journalPath), Is.True);
            Assert.That(File.Exists(temporaryPath), Is.True);
            GC.KeepAlive(transaction);
        }

        [Test]
        public void RecoverPending_WhenStateRootIsAFile_FailsClosed()
        {
            string stateRoot = GetStateRoot();
            Directory.CreateDirectory(Path.GetDirectoryName(stateRoot));
            File.WriteAllText(stateRoot, "do not replace");

            Assert.Throws<InvalidOperationException>(() =>
                AddressablesSettingsTransaction.RecoverPending(projectRoot, importAssets: false));
            Assert.That(File.ReadAllText(stateRoot), Is.EqualTo("do not replace"));
        }

        [Test]
        public void RecoverPending_PreparingJournalWithChangedSource_RetainsEvidence()
        {
            Assert.Throws<AddressablesSettingsSimulatedTerminationException>(() =>
                AddressablesSettingsTransaction.Begin(
                    projectRoot,
                    CreateSnapshots(),
                    importAssets: false,
                    checkpoint: reached =>
                    {
                        if (reached == AddressablesSettingsTransaction.JournalPreparedCheckpoint)
                        {
                            throw new AddressablesSettingsSimulatedTerminationException(reached);
                        }
                    }));
            File.WriteAllBytes(assetPath, new byte[] { 99 });

            Assert.Throws<IOException>(() =>
                AddressablesSettingsTransaction.RecoverPending(projectRoot, importAssets: false));
            Assert.That(File.Exists(Path.Combine(GetStateRoot(), "active.json")), Is.True);
            Assert.That(File.ReadAllBytes(assetPath), Is.EqualTo(new byte[] { 99 }));
        }

        [Test]
        public void Begin_WhenRestoreScratchWouldExceedWindowsPathBudget_FailsBeforeStateCreation()
        {
            const int desiredProjectRootLength = 190;
            int segmentLength = desiredProjectRootLength - sandboxRoot.Length - 1;
            Assert.That(segmentLength, Is.GreaterThan(0));
            string longProjectRoot = Path.Combine(
                sandboxRoot,
                new string('p', segmentLength));
            string longAssetPath = Path.Combine(longProjectRoot, "Assets", "A.asset");
            Directory.CreateDirectory(Path.GetDirectoryName(longAssetPath));
            File.WriteAllBytes(longAssetPath, new byte[] { 1 });
            File.WriteAllBytes(longAssetPath + ".meta", new byte[] { 2 });
            DateTime timestamp = File.GetLastWriteTimeUtc(longAssetPath);
            FileAttributes attributes = File.GetAttributes(longAssetPath);
            var snapshot = new AddressablesBuilder.AssetFileSnapshot(
                "Assets/A.asset",
                longAssetPath,
                new byte[] { 1 },
                timestamp,
                attributes);

            Assert.Throws<PathTooLongException>(() =>
                AddressablesSettingsTransaction.Begin(
                    longProjectRoot,
                    new[] { snapshot },
                    importAssets: false));
            Assert.That(
                Directory.Exists(Path.Combine(longProjectRoot, ".buildpipeline")),
                Is.False);
        }

        private AddressablesSettingsTransaction BeginTransaction()
        {
            return AddressablesSettingsTransaction.Begin(
                projectRoot,
                CreateSnapshots(),
                importAssets: false);
        }

        private AddressablesBuilder.AssetFileSnapshot[] CreateSnapshots()
        {
            const string relativePath =
                "Assets/AddressableAssetsData/AddressableAssetSettings.asset";
            return new[]
            {
                new AddressablesBuilder.AssetFileSnapshot(
                    relativePath,
                    assetPath,
                    (byte[])originalAssetBytes.Clone(),
                    originalAssetTimeUtc,
                    originalAssetAttributes)
            };
        }

        private void MutateConfigurationFiles()
        {
            File.WriteAllBytes(assetPath, new byte[] { 10, 11, 12 });
            File.WriteAllBytes(metaPath, new byte[] { 13, 14, 15 });
            File.SetLastWriteTimeUtc(assetPath, DateTime.UtcNow);
            File.SetLastWriteTimeUtc(metaPath, DateTime.UtcNow.AddSeconds(1));
        }

        private void AssertOriginalIdentity()
        {
            Assert.That(File.ReadAllBytes(assetPath), Is.EqualTo(originalAssetBytes));
            Assert.That(File.ReadAllBytes(metaPath), Is.EqualTo(originalMetaBytes));
            Assert.That(File.GetLastWriteTimeUtc(assetPath), Is.EqualTo(originalAssetTimeUtc));
            Assert.That(File.GetLastWriteTimeUtc(metaPath), Is.EqualTo(originalMetaTimeUtc));
            Assert.That(File.GetAttributes(assetPath), Is.EqualTo(originalAssetAttributes));
            Assert.That(File.GetAttributes(metaPath), Is.EqualTo(originalMetaAttributes));
        }

        private void AssertStateCleared()
        {
            string stateRoot = GetStateRoot();
            Assert.That(File.Exists(Path.Combine(stateRoot, "active.json")), Is.False);
            if (Directory.Exists(stateRoot))
            {
                Assert.That(
                    Directory.EnumerateFileSystemEntries(stateRoot),
                    Is.Empty);
            }
        }

        private string GetStateRoot()
        {
            return Path.Combine(
                projectRoot,
                ".buildpipeline",
                "transactions",
                "addressables-settings");
        }
    }
}
