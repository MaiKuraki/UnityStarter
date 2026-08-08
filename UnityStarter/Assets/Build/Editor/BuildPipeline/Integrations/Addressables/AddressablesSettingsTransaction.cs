using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    /// <summary>
    /// Durably snapshots Addressables configuration assets before a build may
    /// save transient settings. Recovery is project-central and independent of
    /// the currently selected Addressables profile or publication root.
    /// </summary>
    internal sealed class AddressablesSettingsTransaction : IDisposable
    {
        private const int FormatVersion = 1;
        private const string PreparingPhase = "Preparing";
        private const string ActivePhase = "Active";
        private const string RestoredPhase = "Restored";
        private const string StateRelativePath = ".buildpipeline/transactions/addressables-settings";
        private const string JournalFileName = "active.json";
        private const string JournalTemporaryFileName = "active.json.tmp";
        private const string JournalBackupFileName = "active.json.bak";
        private const string OwnerFileName = "transaction.owner";
        private const string OwnerTemporaryFileName = "owner.tmp";
        internal const string RestoredCheckpoint = "Restored";
        internal const string TransactionDirectoryDeletedCheckpoint = "TransactionDirectoryDeleted";
        internal const string JournalPreparedCheckpoint = "JournalPrepared";
        internal const string TransactionDirectoryCreatedCheckpoint = "TransactionDirectoryCreated";
        internal const string OwnerTemporaryWrittenCheckpoint = "OwnerTemporaryWritten";
        internal const string OwnerInstalledCheckpoint = "OwnerInstalled";
        internal const string SnapshotWrittenCheckpointPrefix = "SnapshotWritten:";
        private const int MaximumJournalBytes = 2 * 1024 * 1024;
        private const int MaximumRecordCount = 4096;
        private const int MaximumFileBytes = 32 * 1024 * 1024;
        private const long MaximumTotalBytes = 256L * 1024L * 1024L;
        private const int BufferSize = 64 * 1024;

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private readonly string projectRoot;
        private readonly string stateRoot;
        private readonly string journalPath;
        private readonly bool importAssets;
        private Journal journal;
        private bool completed;
        private bool disposed;

        private AddressablesSettingsTransaction(
            string projectRoot,
            string stateRoot,
            Journal journal,
            bool importAssets)
        {
            this.projectRoot = projectRoot;
            this.stateRoot = stateRoot;
            this.journal = journal;
            this.importAssets = importAssets;
            journalPath = Path.Combine(stateRoot, JournalFileName);
        }

        public static AddressablesSettingsTransaction Begin(
            string projectRoot,
            IReadOnlyList<AddressablesBuilder.AssetFileSnapshot> snapshots)
        {
            return Begin(projectRoot, snapshots, importAssets: true);
        }

        internal static AddressablesSettingsTransaction Begin(
            string projectRoot,
            IReadOnlyList<AddressablesBuilder.AssetFileSnapshot> snapshots,
            bool importAssets)
        {
            return Begin(projectRoot, snapshots, importAssets, checkpoint: null);
        }

        internal static AddressablesSettingsTransaction Begin(
            string projectRoot,
            IReadOnlyList<AddressablesBuilder.AssetFileSnapshot> snapshots,
            bool importAssets,
            Action<string> checkpoint)
        {
            string normalizedProjectRoot = NormalizeProjectRoot(projectRoot);
            string stateRoot = GetStateRoot(normalizedProjectRoot);
            ValidateStatePathBudget(stateRoot);
            EnsurePathHasNoReparsePoints(
                normalizedProjectRoot,
                stateRoot,
                allowMissingLeaf: true);
            EnsureNoPendingRecovery(normalizedProjectRoot);

            FileRecord[] records = CreateRecords(normalizedProjectRoot, snapshots);
            string transactionId = Guid.NewGuid().ToString("N");
            var journal = new Journal
            {
                formatVersion = FormatVersion,
                transactionId = transactionId,
                projectRoot = NormalizePath(normalizedProjectRoot),
                transactionDirectoryName = "transaction-" + transactionId,
                phase = PreparingPhase,
                sequence = 0,
                records = records
            };
            ValidateTransactionPathBudget(
                normalizedProjectRoot,
                stateRoot,
                journal);

            Directory.CreateDirectory(stateRoot);
            EnsurePathHasNoReparsePoints(
                normalizedProjectRoot,
                stateRoot,
                allowMissingLeaf: false);
            EnsureNoDetachedState(stateRoot);
            string journalPath = Path.Combine(stateRoot, JournalFileName);
            WriteJournal(journalPath, journal, createNew: true);
            checkpoint?.Invoke(JournalPreparedCheckpoint);
            string transactionDirectory = GetTransactionDirectory(stateRoot, journal);
            try
            {
                Directory.CreateDirectory(transactionDirectory);
                checkpoint?.Invoke(TransactionDirectoryCreatedCheckpoint);
                EnsurePathHasNoReparsePoints(
                    normalizedProjectRoot,
                    transactionDirectory,
                    allowMissingLeaf: false);
                string ownerTemporaryPath = Path.Combine(
                    transactionDirectory,
                    OwnerTemporaryFileName);
                WriteDurably(
                    ownerTemporaryPath,
                    GetOwnerBytes(transactionId),
                    createNew: true);
                checkpoint?.Invoke(OwnerTemporaryWrittenCheckpoint);
                File.Move(
                    ownerTemporaryPath,
                    Path.Combine(transactionDirectory, OwnerFileName));
                checkpoint?.Invoke(OwnerInstalledCheckpoint);
                for (int index = 0; index < records.Length; index++)
                {
                    FileRecord record = records[index];
                    string snapshotPath = Path.Combine(
                        transactionDirectory,
                        record.snapshotFileName);
                    WriteDurably(snapshotPath, record.bytes, createNew: true);
                    VerifySnapshot(snapshotPath, record);
                    record.bytes = null;
                    checkpoint?.Invoke(SnapshotWrittenCheckpointPrefix + index);
                }

                VerifyOriginalRecords(normalizedProjectRoot, journal);
                journal.phase = ActivePhase;
                WriteJournal(journalPath, journal, createNew: false);
                return new AddressablesSettingsTransaction(
                    normalizedProjectRoot,
                    stateRoot,
                    journal,
                    importAssets);
            }
            catch (AddressablesSettingsSimulatedTerminationException)
            {
                throw;
            }
            catch (Exception operationException)
            {
                Exception cleanupException = null;
                try
                {
                    RecoverPending(normalizedProjectRoot, importAssets);
                }
                catch (Exception exception)
                {
                    cleanupException = exception;
                }

                if (cleanupException != null)
                {
                    throw new AggregateException(
                        "Addressables settings transaction preparation and cleanup both failed.",
                        operationException,
                        cleanupException);
                }

                ExceptionDispatchInfo.Capture(operationException).Throw();
                throw;
            }
        }

        public static void RecoverPending(string projectRoot)
        {
            RecoverPending(projectRoot, importAssets: true, checkpoint: null);
        }

        internal static void EnsureNoPendingRecovery(string projectRoot)
        {
            string normalizedProjectRoot = NormalizeProjectRoot(projectRoot);
            string stateRoot = GetStateRoot(normalizedProjectRoot);
            ValidateStatePathBudget(stateRoot);
            if (!TryGetAttributes(stateRoot, out FileAttributes stateAttributes))
            {
                return;
            }

            if ((stateAttributes & (FileAttributes.Directory | FileAttributes.ReparsePoint))
                != FileAttributes.Directory)
            {
                throw new InvalidOperationException(
                    $"Addressables settings recovery state root is unsafe: '{stateRoot}'.");
            }

            EnsurePathHasNoReparsePoints(
                normalizedProjectRoot,
                stateRoot,
                allowMissingLeaf: false);
            string evidencePath = Directory
                .EnumerateFileSystemEntries(stateRoot, "*", SearchOption.TopDirectoryOnly)
                .FirstOrDefault();
            if (evidencePath != null)
            {
                throw new InvalidOperationException(
                    $"Pending Addressables settings recovery must be completed before starting another build: '{stateRoot}'. " +
                    "Use the Build workspace recovery action or -pipelineRecoverOnly.");
            }
        }

        internal static void RecoverPending(string projectRoot, bool importAssets)
        {
            RecoverPending(projectRoot, importAssets, checkpoint: null);
        }

        internal static void RecoverPending(
            string projectRoot,
            bool importAssets,
            Action<string> checkpoint)
        {
            string normalizedProjectRoot = NormalizeProjectRoot(projectRoot);
            string stateRoot = GetStateRoot(normalizedProjectRoot);
            if (!TryGetAttributes(stateRoot, out FileAttributes stateAttributes))
            {
                return;
            }

            if ((stateAttributes & (FileAttributes.Directory | FileAttributes.ReparsePoint))
                != FileAttributes.Directory)
            {
                throw new InvalidOperationException(
                    $"Addressables settings transaction state root is unsafe: '{stateRoot}'.");
            }

            EnsurePathHasNoReparsePoints(normalizedProjectRoot, stateRoot, allowMissingLeaf: false);
            string journalPath = Path.Combine(stateRoot, JournalFileName);
            RecoverJournalScratch(normalizedProjectRoot, stateRoot, journalPath);
            if (!TryGetAttributes(journalPath, out _))
            {
                EnsureNoDetachedState(stateRoot);
                return;
            }

            Journal journal = ReadAndValidateJournal(
                journalPath,
                normalizedProjectRoot,
                stateRoot);
            if (string.Equals(journal.phase, PreparingPhase, StringComparison.Ordinal))
            {
                VerifyOriginalRecords(normalizedProjectRoot, journal);
                DeleteTransactionDirectory(stateRoot, journal, allowIncompleteSnapshots: true);
                DeleteFileStrict(journalPath);
                EnsureNoDetachedState(stateRoot);
                return;
            }

            if (string.Equals(journal.phase, ActivePhase, StringComparison.Ordinal))
            {
                RestoreRecords(normalizedProjectRoot, stateRoot, journal, importAssets);
                journal.phase = RestoredPhase;
                WriteJournal(journalPath, journal, createNew: false);
                checkpoint?.Invoke(RestoredCheckpoint);
            }

            VerifyOriginalRecords(normalizedProjectRoot, journal);
            DeleteTransactionDirectory(stateRoot, journal, allowIncompleteSnapshots: true);
            checkpoint?.Invoke(TransactionDirectoryDeletedCheckpoint);
            DeleteFileStrict(journalPath);
            EnsureNoDetachedState(stateRoot);
        }

        internal void RestoreAndComplete()
        {
            RestoreAndComplete(null);
        }

        internal void RestoreAndComplete(Action<string> checkpoint)
        {
            ThrowIfUnavailable();
            RecoverPending(projectRoot, importAssets, checkpoint);
            completed = true;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (completed)
            {
                return;
            }

            RecoverPending(projectRoot, importAssets, checkpoint: null);
            completed = true;
        }

        private static FileRecord[] CreateRecords(
            string projectRoot,
            IReadOnlyList<AddressablesBuilder.AssetFileSnapshot> snapshots)
        {
            if (snapshots == null || snapshots.Count == 0)
            {
                throw new ArgumentException(
                    "Addressables settings transaction requires at least one configuration snapshot.",
                    nameof(snapshots));
            }

            var sources = new List<SourceRecord>(snapshots.Count * 2);
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < snapshots.Count; index++)
            {
                AddressablesBuilder.AssetFileSnapshot snapshot = snapshots[index]
                    ?? throw new InvalidOperationException(
                        "Addressables configuration snapshots cannot contain null entries.");
                AddSource(
                    projectRoot,
                    snapshot.AssetPath,
                    snapshot.AbsolutePath,
                    (byte[])snapshot.OriginalBytes.Clone(),
                    snapshot.OriginalLastWriteTimeUtc,
                    snapshot.OriginalAttributes,
                    paths,
                    sources);

                string metaPath = snapshot.AssetPath + ".meta";
                string absoluteMetaPath = snapshot.AbsolutePath + ".meta";
                StableFile meta = CaptureStableFile(absoluteMetaPath, "Addressables configuration meta file");
                AddSource(
                    projectRoot,
                    metaPath,
                    absoluteMetaPath,
                    meta.Bytes,
                    meta.LastWriteTimeUtc,
                    meta.Attributes,
                    paths,
                    sources);
            }

            if (sources.Count > MaximumRecordCount)
            {
                throw new InvalidOperationException(
                    $"Addressables settings transaction exceeds {MaximumRecordCount} files.");
            }

            sources.Sort((left, right) => StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));
            long totalBytes = 0;
            var records = new FileRecord[sources.Count];
            for (int index = 0; index < sources.Count; index++)
            {
                SourceRecord source = sources[index];
                totalBytes = checked(totalBytes + source.Bytes.LongLength);
                if (totalBytes > MaximumTotalBytes)
                {
                    throw new InvalidOperationException(
                        "Addressables settings snapshots exceed their total byte budget.");
                }

                records[index] = new FileRecord
                {
                    relativePath = source.RelativePath,
                    snapshotFileName = index.ToString("D4") + ".snapshot",
                    length = source.Bytes.LongLength,
                    sha256 = ComputeSha256(source.Bytes),
                    lastWriteTimeUtcTicks = source.LastWriteTimeUtc.Ticks,
                    attributes = (int)source.Attributes,
                    bytes = source.Bytes
                };
            }

            return records;
        }

        private static void AddSource(
            string projectRoot,
            string relativePath,
            string absolutePath,
            byte[] bytes,
            DateTime lastWriteTimeUtc,
            FileAttributes attributes,
            ISet<string> paths,
            ICollection<SourceRecord> sources)
        {
            BuildPathPolicy.ValidatePortableProjectRelativePath(
                relativePath,
                "Addressables configuration file path");
            if (!relativePath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Addressables configuration file must be below Assets: '{relativePath}'.");
            }

            string expectedAbsolute = Path.GetFullPath(Path.Combine(
                projectRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!PathsEqual(expectedAbsolute, absolutePath)
                || !paths.Add(relativePath)
                || bytes == null
                || bytes.LongLength > MaximumFileBytes
                || (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                throw new InvalidOperationException(
                    $"Addressables configuration snapshot is invalid or duplicated: '{relativePath}'.");
            }

            VerifyFileIdentity(
                expectedAbsolute,
                bytes.LongLength,
                ComputeSha256(bytes),
                lastWriteTimeUtc.Ticks,
                (int)attributes,
                "Addressables configuration snapshot source");
            sources.Add(new SourceRecord(
                relativePath,
                bytes,
                lastWriteTimeUtc,
                attributes));
        }

        private static void RestoreRecords(
            string projectRoot,
            string stateRoot,
            Journal journal,
            bool importAssets)
        {
            string transactionDirectory = GetTransactionDirectory(stateRoot, journal);
            ValidateTransactionDirectory(
                projectRoot,
                transactionDirectory,
                journal.transactionId);
            for (int index = 0; index < journal.records.Length; index++)
            {
                FileRecord record = journal.records[index];
                string snapshotPath = Path.Combine(transactionDirectory, record.snapshotFileName);
                byte[] bytes = ReadAndVerifySnapshot(snapshotPath, record);
                string targetPath = ResolveRecordPath(projectRoot, record.relativePath);
                RestoreRecord(transactionDirectory, index, targetPath, bytes, record);
            }

            if (importAssets)
            {
                for (int index = 0; index < journal.records.Length; index++)
                {
                    string relativePath = journal.records[index].relativePath;
                    if (!relativePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    {
                        AssetDatabase.ImportAsset(
                            relativePath,
                            ImportAssetOptions.ForceUpdate
                            | ImportAssetOptions.ForceSynchronousImport);
                    }
                }
            }

            VerifyOriginalRecords(projectRoot, journal);
        }

        private static void RestoreRecord(
            string transactionDirectory,
            int recordIndex,
            string targetPath,
            byte[] bytes,
            FileRecord record)
        {
            string parent = Path.GetDirectoryName(targetPath);
            if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
            {
                throw new DirectoryNotFoundException(
                    $"Addressables configuration parent directory is missing: '{parent}'.");
            }

            string scratchPrefix = recordIndex.ToString("D4") + ".restore";
            string temporaryPath = Path.Combine(transactionDirectory, scratchPrefix + ".tmp");
            string backupPath = Path.Combine(transactionDirectory, scratchPrefix + ".bak");
            var failures = new List<Exception>();
            FileAttributes attributesBeforeReplacement = default;
            bool destinationAttributesChanged = false;
            bool replacementCompleted = false;
            try
            {
                DeleteFileStrict(temporaryPath);
                DeleteFileStrict(backupPath);
                WriteDurably(temporaryPath, bytes, createNew: true);
                VerifySnapshot(temporaryPath, record);

                if (TryGetAttributes(targetPath, out FileAttributes targetAttributes))
                {
                    if ((targetAttributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                    {
                        throw new InvalidOperationException(
                            $"Addressables configuration destination is not a regular file: '{targetPath}'.");
                    }

                    attributesBeforeReplacement = targetAttributes;
                    if ((targetAttributes & FileAttributes.ReadOnly) != 0)
                    {
                        File.SetAttributes(targetPath, targetAttributes & ~FileAttributes.ReadOnly);
                        destinationAttributesChanged = true;
                    }

                    File.Replace(temporaryPath, targetPath, backupPath);
                    replacementCompleted = true;
                }
                else
                {
                    File.Move(temporaryPath, targetPath);
                    replacementCompleted = true;
                }

                File.SetLastWriteTimeUtc(
                    targetPath,
                    new DateTime(record.lastWriteTimeUtcTicks, DateTimeKind.Utc));
                File.SetAttributes(targetPath, (FileAttributes)record.attributes);
                VerifyRecord(targetPath, record);
            }
            catch (Exception exception)
            {
                failures.Add(new IOException(
                    $"Failed to atomically restore Addressables configuration file '{targetPath}'.",
                    exception));
                if (destinationAttributesChanged
                    && !replacementCompleted
                    && TryGetAttributes(targetPath, out FileAttributes currentAttributes)
                    && (currentAttributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0)
                {
                    try
                    {
                        File.SetAttributes(targetPath, attributesBeforeReplacement);
                    }
                    catch (Exception attributesException)
                    {
                        failures.Add(new IOException(
                            $"Failed to restore attributes after replacement failed: '{targetPath}'.",
                            attributesException));
                    }
                }
            }
            finally
            {
                try
                {
                    DeleteFileStrict(temporaryPath);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }

                try
                {
                    DeleteFileStrict(backupPath);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            if (failures.Count == 1)
            {
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            }

            if (failures.Count > 1)
            {
                throw new AggregateException(
                    "Addressables configuration restoration and scratch cleanup failed.",
                    failures);
            }
        }

        private static void VerifyOriginalRecords(string projectRoot, Journal journal)
        {
            for (int index = 0; index < journal.records.Length; index++)
            {
                FileRecord record = journal.records[index];
                VerifyRecord(ResolveRecordPath(projectRoot, record.relativePath), record);
            }
        }

        private static void VerifyRecord(string path, FileRecord record)
        {
            VerifyFileIdentity(
                path,
                record.length,
                record.sha256,
                record.lastWriteTimeUtcTicks,
                record.attributes,
                "Addressables configuration file");
        }

        private static void VerifySnapshot(string path, FileRecord record)
        {
            ReadAndVerifySnapshot(path, record);
        }

        private static byte[] ReadAndVerifySnapshot(string path, FileRecord record)
        {
            byte[] bytes = ReadExactFile(
                path,
                record.length,
                "Addressables settings snapshot");
            if (!FixedTimeEquals(ComputeSha256(bytes), record.sha256))
            {
                throw new IOException(
                    $"Addressables settings snapshot identity is invalid: '{path}'.");
            }

            return bytes;
        }

        private static void VerifyFileIdentity(
            string path,
            long length,
            string sha256,
            long lastWriteTimeUtcTicks,
            int attributes,
            string label)
        {
            byte[] bytes = ReadExactFile(path, length, label);
            FileAttributes actualAttributes = File.GetAttributes(path);
            if ((actualAttributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(label + " cannot be a reparse point: '" + path + "'.");
            }

            var info = new FileInfo(path);
            if (info.Length != length
                || info.LastWriteTimeUtc.Ticks != lastWriteTimeUtcTicks
                || (int)actualAttributes != attributes
                || !FixedTimeEquals(ComputeSha256(bytes), sha256))
            {
                throw new IOException(label + " identity changed: '" + path + "'.");
            }
        }

        private static void ValidateJournal(
            string projectRoot,
            string stateRoot,
            Journal journal)
        {
            if (journal == null
                || journal.formatVersion != FormatVersion
                || !IsGuidN(journal.transactionId)
                || !string.Equals(journal.projectRoot, NormalizePath(projectRoot), StringComparison.Ordinal)
                || !string.Equals(
                    journal.transactionDirectoryName,
                    "transaction-" + journal.transactionId,
                    StringComparison.Ordinal)
                || (journal.phase != PreparingPhase
                    && journal.phase != ActivePhase
                    && journal.phase != RestoredPhase)
                || !IsExpectedPhaseSequence(journal.phase, journal.sequence)
                || journal.records == null
                || journal.records.Length == 0
                || journal.records.Length > MaximumRecordCount)
            {
                throw new InvalidDataException(
                    "Addressables settings journal has an unsupported or incomplete format.");
            }

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long totalBytes = 0;
            for (int index = 0; index < journal.records.Length; index++)
            {
                FileRecord record = journal.records[index];
                string expectedSnapshotName = index.ToString("D4") + ".snapshot";
                if (record == null
                    || string.IsNullOrEmpty(record.relativePath)
                    || !string.Equals(record.snapshotFileName, expectedSnapshotName, StringComparison.Ordinal)
                    || record.length < 0
                    || record.length > MaximumFileBytes
                    || !IsSha256(record.sha256)
                    || record.lastWriteTimeUtcTicks <= 0
                    || record.lastWriteTimeUtcTicks > DateTime.MaxValue.Ticks
                    || (((FileAttributes)record.attributes)
                        & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0
                    || !paths.Add(record.relativePath))
                {
                    throw new InvalidDataException(
                        "Addressables settings journal contains an invalid file record.");
                }

                BuildPathPolicy.ValidatePortableProjectRelativePath(
                    record.relativePath,
                    "Addressables settings journal path");
                if (!record.relativePath.StartsWith("Assets/", StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Addressables settings journal path is outside Assets.");
                }

                ResolveRecordPath(projectRoot, record.relativePath);
                totalBytes = checked(totalBytes + record.length);
                if (totalBytes > MaximumTotalBytes)
                {
                    throw new InvalidDataException(
                        "Addressables settings journal exceeds its total byte budget.");
                }
            }

            GetTransactionDirectory(stateRoot, journal);
        }

        private static void DeleteTransactionDirectory(
            string stateRoot,
            Journal journal,
            bool allowIncompleteSnapshots)
        {
            string directory = GetTransactionDirectory(stateRoot, journal);
            if (!TryGetAttributes(directory, out FileAttributes directoryAttributes))
            {
                if (string.Equals(journal.phase, ActivePhase, StringComparison.Ordinal))
                {
                    throw new DirectoryNotFoundException(
                        $"Addressables settings transaction directory is missing: '{directory}'.");
                }

                return;
            }

            if ((directoryAttributes & (FileAttributes.Directory | FileAttributes.ReparsePoint))
                != FileAttributes.Directory)
            {
                throw new InvalidOperationException(
                    $"Addressables settings transaction path is not an owned directory: '{directory}'.");
            }

            var expectedNames = new HashSet<string>(StringComparer.Ordinal)
            {
                OwnerFileName,
                OwnerTemporaryFileName
            };
            for (int index = 0; index < journal.records.Length; index++)
            {
                expectedNames.Add(journal.records[index].snapshotFileName);
                string restorePrefix = index.ToString("D4") + ".restore";
                expectedNames.Add(restorePrefix + ".tmp");
                expectedNames.Add(restorePrefix + ".bak");
            }

            string[] entries = Directory.EnumerateFileSystemEntries(directory).ToArray();
            foreach (string entry in entries)
            {
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0
                    || !expectedNames.Contains(Path.GetFileName(entry)))
                {
                    throw new InvalidOperationException(
                        $"Addressables settings transaction contains an unowned entry: '{entry}'.");
                }
            }

            string ownerPath = Path.Combine(directory, OwnerFileName);
            if (TryGetAttributes(ownerPath, out _))
            {
                ValidateOwner(directory, journal.transactionId);
            }
            else
            {
                bool preparingOwnerWriteInterrupted =
                    string.Equals(journal.phase, PreparingPhase, StringComparison.Ordinal)
                    && entries.All(entry => string.Equals(
                        Path.GetFileName(entry),
                        OwnerTemporaryFileName,
                        StringComparison.Ordinal));
                bool restoredCleanupInterrupted =
                    string.Equals(journal.phase, RestoredPhase, StringComparison.Ordinal)
                    && entries.Length == 0;
                if (!preparingOwnerWriteInterrupted && !restoredCleanupInterrupted)
                {
                    throw new FileNotFoundException(
                        "Addressables settings transaction owner is missing.",
                        ownerPath);
                }
            }

            if (!allowIncompleteSnapshots)
            {
                for (int index = 0; index < journal.records.Length; index++)
                {
                    VerifySnapshot(
                        Path.Combine(directory, journal.records[index].snapshotFileName),
                        journal.records[index]);
                }
            }

            foreach (string entry in Directory.EnumerateFiles(directory))
            {
                if (string.Equals(Path.GetFileName(entry), OwnerFileName, StringComparison.Ordinal))
                {
                    continue;
                }

                DeleteFileStrict(entry);
            }

            DeleteFileStrict(ownerPath);
            Directory.Delete(directory, recursive: false);
            if (TryGetAttributes(directory, out _))
            {
                throw new IOException(
                    $"Addressables settings transaction directory still exists after deletion: '{directory}'.");
            }
        }

        private static void EnsureNoDetachedState(string stateRoot)
        {
            if (!TryGetAttributes(stateRoot, out FileAttributes stateAttributes))
            {
                return;
            }

            if ((stateAttributes & (FileAttributes.Directory | FileAttributes.ReparsePoint))
                != FileAttributes.Directory)
            {
                throw new InvalidOperationException(
                    $"Addressables settings transaction state root is unsafe: '{stateRoot}'.");
            }

            string journalPath = Path.Combine(stateRoot, JournalFileName);
            foreach (string entry in Directory.EnumerateFileSystemEntries(stateRoot))
            {
                string name = Path.GetFileName(entry);
                bool allowed = PathsEqual(entry, journalPath)
                    || string.Equals(name, JournalTemporaryFileName, StringComparison.Ordinal)
                    || string.Equals(name, JournalBackupFileName, StringComparison.Ordinal);
                FileAttributes attributes = File.GetAttributes(entry);
                if (allowed
                    && (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0)
                {
                    continue;
                }

                throw new InvalidOperationException(
                    $"Detached Addressables settings transaction state requires manual review: '{entry}'.");
            }
        }

        private static void ValidateOwner(string transactionDirectory, string transactionId)
        {
            string ownerPath = Path.Combine(transactionDirectory, OwnerFileName);
            if (!TryGetAttributes(ownerPath, out FileAttributes attributes)
                || (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                throw new InvalidOperationException(
                    $"Addressables settings transaction owner is not a regular file: '{ownerPath}'.");
            }

            byte[] expected = GetOwnerBytes(transactionId);
            byte[] actual = ReadExactFile(ownerPath, expected.LongLength, "Addressables settings owner");
            if (!ByteArraysEqual(actual, expected))
            {
                throw new InvalidDataException(
                    "Addressables settings transaction owner does not match its journal.");
            }
        }

        private static void ValidateTransactionDirectory(
            string projectRoot,
            string transactionDirectory,
            string transactionId)
        {
            if (!TryGetAttributes(transactionDirectory, out FileAttributes attributes)
                || (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint))
                != FileAttributes.Directory)
            {
                throw new InvalidOperationException(
                    $"Addressables settings transaction directory is unavailable or unsafe: '{transactionDirectory}'.");
            }

            EnsurePathHasNoReparsePoints(
                projectRoot,
                transactionDirectory,
                allowMissingLeaf: false);
            ValidateOwner(transactionDirectory, transactionId);
        }

        private static byte[] GetOwnerBytes(string transactionId)
        {
            return StrictUtf8.GetBytes("Build.Pipeline.AddressablesSettings\n" + transactionId + "\n");
        }

        private static void WriteJournal(string path, Journal journal, bool createNew)
        {
            journal.sequence++;
            string payload = JsonUtility.ToJson(journal, false);
            byte[] payloadBytes = StrictUtf8.GetBytes(payload);
            var envelope = new JournalEnvelope
            {
                formatVersion = FormatVersion,
                payloadBase64 = Convert.ToBase64String(payloadBytes),
                sha256 = ComputeSha256(payloadBytes)
            };
            byte[] bytes = StrictUtf8.GetBytes(JsonUtility.ToJson(envelope, true));
            if (bytes.Length <= 0 || bytes.Length > MaximumJournalBytes)
            {
                throw new InvalidOperationException(
                    "Addressables settings journal exceeds its byte budget.");
            }

            WriteJsonAtomically(path, bytes, createNew);
        }

        private static Journal ReadJournal(string path)
        {
            byte[] bytes = ReadBoundedFile(path, MaximumJournalBytes, "Addressables settings journal");
            JournalEnvelope envelope;
            try
            {
                envelope = JsonUtility.FromJson<JournalEnvelope>(DecodeStrictUtf8(bytes));
            }
            catch (Exception exception)
            {
                throw new InvalidDataException(
                    "Addressables settings journal envelope JSON is invalid.",
                    exception);
            }

            if (envelope == null
                || envelope.formatVersion != FormatVersion
                || string.IsNullOrWhiteSpace(envelope.payloadBase64)
                || !IsSha256(envelope.sha256))
            {
                throw new InvalidDataException("Addressables settings journal envelope is invalid.");
            }

            byte[] payload;
            try
            {
                payload = Convert.FromBase64String(envelope.payloadBase64);
            }
            catch (FormatException exception)
            {
                throw new InvalidDataException("Addressables settings journal payload is invalid.", exception);
            }

            if (!FixedTimeEquals(ComputeSha256(payload), envelope.sha256))
            {
                throw new InvalidDataException("Addressables settings journal checksum verification failed.");
            }

            try
            {
                return JsonUtility.FromJson<Journal>(DecodeStrictUtf8(payload));
            }
            catch (Exception exception)
            {
                throw new InvalidDataException(
                    "Addressables settings journal payload JSON is invalid.",
                    exception);
            }
        }

        private static void WriteJsonAtomically(string path, byte[] bytes, bool createNew)
        {
            string stateRoot = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(stateRoot))
            {
                throw new InvalidOperationException(
                    "Addressables settings journal has no state root.");
            }

            string temporaryPath = Path.Combine(stateRoot, JournalTemporaryFileName);
            string backupPath = Path.Combine(stateRoot, JournalBackupFileName);
            if (TryGetAttributes(temporaryPath, out _)
                || TryGetAttributes(backupPath, out _))
            {
                throw new InvalidOperationException(
                    $"Addressables settings journal scratch requires recovery under '{stateRoot}'.");
            }

            WriteDurably(temporaryPath, bytes, createNew: true);
            if (createNew)
            {
                if (TryGetAttributes(path, out _))
                {
                    throw new IOException(
                        $"Addressables settings journal already exists: '{path}'.");
                }

                File.Move(temporaryPath, path);
            }
            else
            {
                File.Replace(temporaryPath, path, backupPath);
                DeleteFileStrict(backupPath);
            }
        }

        private static Journal ReadAndValidateJournal(
            string path,
            string projectRoot,
            string stateRoot)
        {
            Journal journal = ReadJournal(path);
            ValidateJournal(projectRoot, stateRoot, journal);
            return journal;
        }

        private static void RecoverJournalScratch(
            string projectRoot,
            string stateRoot,
            string journalPath)
        {
            string temporaryPath = Path.Combine(stateRoot, JournalTemporaryFileName);
            string backupPath = Path.Combine(stateRoot, JournalBackupFileName);
            Journal active = TryReadAndValidateJournal(
                journalPath,
                projectRoot,
                stateRoot);
            Journal temporary = TryReadAndValidateJournal(
                temporaryPath,
                projectRoot,
                stateRoot);
            Journal backup = TryReadAndValidateJournal(
                backupPath,
                projectRoot,
                stateRoot);

            if (active != null)
            {
                if (temporary != null && backup != null)
                {
                    throw new InvalidDataException(
                        "Addressables settings journal contains both temporary and backup scratch beside an active journal.");
                }

                if (temporary != null)
                {
                    ValidateSuccessor(active, temporary, "temporary");
                }

                if (backup != null)
                {
                    ValidateSuccessor(backup, active, "active");
                }

                DeleteFileStrict(temporaryPath);
                DeleteFileStrict(backupPath);
                return;
            }

            if (backup != null)
            {
                if (temporary != null)
                {
                    ValidateSuccessor(backup, temporary, "temporary");
                }

                File.Move(backupPath, journalPath);
                ReadAndValidateJournal(journalPath, projectRoot, stateRoot);
                DeleteFileStrict(temporaryPath);
                return;
            }

            if (temporary != null)
            {
                File.Move(temporaryPath, journalPath);
                ReadAndValidateJournal(journalPath, projectRoot, stateRoot);
            }
        }

        private static Journal TryReadAndValidateJournal(
            string path,
            string projectRoot,
            string stateRoot)
        {
            return TryGetAttributes(path, out _)
                ? ReadAndValidateJournal(path, projectRoot, stateRoot)
                : null;
        }

        private static void ValidateSuccessor(
            Journal predecessor,
            Journal successor,
            string successorLabel)
        {
            if (!string.Equals(
                    predecessor.transactionId,
                    successor.transactionId,
                    StringComparison.Ordinal)
                || successor.sequence != predecessor.sequence + 1
                || !HaveSameImmutableState(predecessor, successor))
            {
                throw new InvalidDataException(
                    $"Addressables settings {successorLabel} journal is not the next revision of the same transaction.");
            }
        }

        private static bool HaveSameImmutableState(Journal left, Journal right)
        {
            if (!string.Equals(left.projectRoot, right.projectRoot, StringComparison.Ordinal)
                || !string.Equals(
                    left.transactionDirectoryName,
                    right.transactionDirectoryName,
                    StringComparison.Ordinal)
                || left.records.Length != right.records.Length)
            {
                return false;
            }

            for (int index = 0; index < left.records.Length; index++)
            {
                FileRecord first = left.records[index];
                FileRecord second = right.records[index];
                if (!string.Equals(first.relativePath, second.relativePath, StringComparison.Ordinal)
                    || !string.Equals(first.snapshotFileName, second.snapshotFileName, StringComparison.Ordinal)
                    || first.length != second.length
                    || !string.Equals(first.sha256, second.sha256, StringComparison.Ordinal)
                    || first.lastWriteTimeUtcTicks != second.lastWriteTimeUtcTicks
                    || first.attributes != second.attributes)
                {
                    return false;
                }
            }

            return true;
        }

        private static StableFile CaptureStableFile(string path, string label)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(label + " is missing.", path);
            }

            DateTime lastWriteTimeUtc = File.GetLastWriteTimeUtc(path);
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(label + " cannot be a reparse point: '" + path + "'.");
            }

            byte[] bytes = ReadBoundedFile(path, MaximumFileBytes, label);
            if (File.GetLastWriteTimeUtc(path) != lastWriteTimeUtc
                || File.GetAttributes(path) != attributes)
            {
                throw new IOException(label + " changed while it was captured: '" + path + "'.");
            }

            return new StableFile(bytes, lastWriteTimeUtc, attributes);
        }

        private static byte[] ReadBoundedFile(string path, int maximumBytes, string label)
        {
            EnsureRegularFile(path, label);
            using (var stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       BufferSize,
                       FileOptions.SequentialScan))
            {
                EnsureRegularFile(path, label);
                if (stream.Length < 0 || stream.Length > maximumBytes)
                {
                    throw new IOException(label + " exceeds its byte budget: '" + path + "'.");
                }

                return ReadExactStream(stream, stream.Length, label);
            }
        }

        private static byte[] ReadExactFile(string path, long length, string label)
        {
            EnsureRegularFile(path, label);
            using (var stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       BufferSize,
                       FileOptions.SequentialScan))
            {
                EnsureRegularFile(path, label);
                if (stream.Length != length)
                {
                    throw new IOException(label + " length changed: '" + path + "'.");
                }

                return ReadExactStream(stream, length, label);
            }
        }

        private static byte[] ReadExactStream(Stream stream, long length, string label)
        {
            if (length > int.MaxValue)
            {
                throw new IOException(label + " is too large to buffer safely.");
            }

            var bytes = new byte[(int)length];
            int offset = 0;
            while (offset < bytes.Length)
            {
                int read = stream.Read(bytes, offset, bytes.Length - offset);
                if (read <= 0)
                {
                    throw new EndOfStreamException(label + " ended while it was read.");
                }

                offset += read;
            }

            if (stream.ReadByte() != -1)
            {
                throw new IOException(label + " grew while it was read.");
            }

            return bytes;
        }

        private static void EnsureRegularFile(string path, string label)
        {
            if (!TryGetAttributes(path, out FileAttributes attributes))
            {
                throw new FileNotFoundException(label + " is missing.", path);
            }

            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                throw new InvalidOperationException(
                    label + " is not a regular file: '" + path + "'.");
            }
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                return ToHex(sha256.ComputeHash(bytes));
            }
        }

        private static void WriteDurably(string path, byte[] bytes, bool createNew)
        {
            using (var stream = new FileStream(
                       path,
                       createNew ? FileMode.CreateNew : FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       BufferSize,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        private static void DeleteFileStrict(string path)
        {
            if (!TryGetAttributes(path, out FileAttributes attributes))
            {
                return;
            }

            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                throw new InvalidOperationException(
                    $"Refusing to delete a non-regular transaction file: '{path}'.");
            }

            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }

            File.Delete(path);
            if (TryGetAttributes(path, out _))
            {
                throw new IOException($"Transaction file still exists after deletion: '{path}'.");
            }
        }

        private static bool TryGetAttributes(string path, out FileAttributes attributes)
        {
            try
            {
                attributes = File.GetAttributes(path);
                return true;
            }
            catch (FileNotFoundException)
            {
                attributes = default;
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                attributes = default;
                return false;
            }
        }

        private static string ResolveRecordPath(string projectRoot, string relativePath)
        {
            string path = Path.GetFullPath(Path.Combine(
                projectRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!BuildPathPolicy.IsStrictDescendant(projectRoot, path))
            {
                throw new InvalidDataException(
                    $"Addressables settings path escaped the project root: '{relativePath}'.");
            }

            EnsurePathHasNoReparsePoints(projectRoot, path, allowMissingLeaf: true);
            return path;
        }

        private static string GetStateRoot(string projectRoot)
        {
            return Path.Combine(
                projectRoot,
                StateRelativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string GetTransactionDirectory(string stateRoot, Journal journal)
        {
            string path = Path.GetFullPath(Path.Combine(stateRoot, journal.transactionDirectoryName));
            if (!BuildPathPolicy.IsStrictDescendant(stateRoot, path))
            {
                throw new InvalidDataException(
                    "Addressables settings transaction directory escaped its state root.");
            }

            return path;
        }

        private static void ValidateStatePathBudget(string stateRoot)
        {
            BuildPathPolicy.EnsureWin32MaxDirectoryPathBudget(
                stateRoot,
                "Addressables settings transaction state root");
            BuildPathPolicy.EnsureWin32MaxPathBudget(
                Path.Combine(stateRoot, JournalFileName),
                "Addressables settings transaction journal");
            BuildPathPolicy.EnsureWin32MaxPathBudget(
                Path.Combine(stateRoot, JournalTemporaryFileName),
                "Addressables settings temporary journal");
            BuildPathPolicy.EnsureWin32MaxPathBudget(
                Path.Combine(stateRoot, JournalBackupFileName),
                "Addressables settings backup journal");
        }

        private static void ValidateTransactionPathBudget(
            string projectRoot,
            string stateRoot,
            Journal journal)
        {
            string transactionDirectory = GetTransactionDirectory(stateRoot, journal);
            BuildPathPolicy.EnsureWin32MaxDirectoryPathBudget(
                transactionDirectory,
                "Addressables settings transaction directory");
            BuildPathPolicy.EnsureWin32MaxPathBudget(
                Path.Combine(transactionDirectory, OwnerFileName),
                "Addressables settings transaction owner");
            BuildPathPolicy.EnsureWin32MaxPathBudget(
                Path.Combine(transactionDirectory, OwnerTemporaryFileName),
                "Addressables settings temporary owner");

            for (int index = 0; index < journal.records.Length; index++)
            {
                FileRecord record = journal.records[index];
                BuildPathPolicy.EnsureWin32MaxPathBudget(
                    Path.Combine(transactionDirectory, record.snapshotFileName),
                    "Addressables settings snapshot");
                BuildPathPolicy.EnsureWin32MaxPathBudget(
                    Path.Combine(
                        transactionDirectory,
                        index.ToString("D4") + ".restore.tmp"),
                    "Addressables settings restore scratch");
                BuildPathPolicy.EnsureWin32MaxPathBudget(
                    Path.Combine(
                        transactionDirectory,
                        index.ToString("D4") + ".restore.bak"),
                    "Addressables settings restore backup");
                BuildPathPolicy.EnsureWin32MaxPathBudget(
                    ResolveRecordPath(projectRoot, record.relativePath),
                    "Addressables settings configuration file");
            }
        }

        private static string NormalizeProjectRoot(string path)
        {
            string root = Path.GetFullPath(path ?? throw new ArgumentNullException(nameof(path)))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!Directory.Exists(root))
            {
                throw new DirectoryNotFoundException(
                    $"Unity project root does not exist: '{root}'.");
            }

            return root;
        }

        private static void EnsurePathHasNoReparsePoints(
            string root,
            string path,
            bool allowMissingLeaf)
        {
            string normalizedRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedPath = Path.GetFullPath(path);
            string prefix = normalizedRoot + Path.DirectorySeparatorChar;
            if (!PathsEqual(normalizedRoot, normalizedPath)
                && !normalizedPath.StartsWith(prefix, PathComparison))
            {
                throw new InvalidOperationException(
                    $"Addressables settings path escaped the project root: '{normalizedPath}'.");
            }

            string current = normalizedRoot;
            CheckReparsePoint(current);
            if (PathsEqual(current, normalizedPath))
            {
                return;
            }

            string relative = normalizedPath.Substring(prefix.Length);
            string[] segments = relative.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);
            for (int index = 0; index < segments.Length; index++)
            {
                current = Path.Combine(current, segments[index]);
                if (!File.Exists(current) && !Directory.Exists(current))
                {
                    if (!allowMissingLeaf && index == segments.Length - 1)
                    {
                        throw new FileNotFoundException(
                            "Addressables settings path is missing.",
                            current);
                    }

                    return;
                }

                CheckReparsePoint(current);
            }
        }

        private static void CheckReparsePoint(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"Addressables settings path crosses a reparse point: '{path}'.");
            }
        }

        private static string DecodeStrictUtf8(byte[] bytes)
        {
            if (bytes.Length >= 3
                && bytes[0] == 0xEF
                && bytes[1] == 0xBB
                && bytes[2] == 0xBF)
            {
                throw new InvalidDataException("Transaction JSON must be UTF-8 without BOM.");
            }

            return StrictUtf8.GetString(bytes);
        }

        private static bool IsGuidN(string value)
        {
            return value != null
                && value.Length == 32
                && Guid.TryParseExact(value, "N", out _);
        }

        private static bool IsExpectedPhaseSequence(string phase, long sequence)
        {
            return (string.Equals(phase, PreparingPhase, StringComparison.Ordinal) && sequence == 1)
                || (string.Equals(phase, ActivePhase, StringComparison.Ordinal) && sequence == 2)
                || (string.Equals(phase, RestoredPhase, StringComparison.Ordinal) && sequence == 3);
        }

        private static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64)
            {
                return false;
            }

            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (!((character >= '0' && character <= '9')
                      || (character >= 'A' && character <= 'F')))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool FixedTimeEquals(string first, string second)
        {
            if (first == null || second == null || first.Length != second.Length)
            {
                return false;
            }

            int difference = 0;
            for (int index = 0; index < first.Length; index++)
            {
                difference |= first[index] ^ second[index];
            }

            return difference == 0;
        }

        private static bool ByteArraysEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            int difference = 0;
            for (int index = 0; index < left.Length; index++)
            {
                difference |= left[index] ^ right[index];
            }

            return difference == 0;
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                PathComparison);
        }

        private static string NormalizePath(string path)
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace('\\', '/');
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            for (int index = 0; index < bytes.Length; index++)
            {
                builder.Append(bytes[index].ToString("X2"));
            }

            return builder.ToString();
        }

        private static StringComparison PathComparison =>
            Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        private void ThrowIfUnavailable()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(AddressablesSettingsTransaction));
            }

            if (completed)
            {
                throw new InvalidOperationException(
                    "Addressables settings transaction has already completed.");
            }
        }

        [Serializable]
        private sealed class JournalEnvelope
        {
            public int formatVersion;
            public string payloadBase64;
            public string sha256;
        }

        [Serializable]
        private sealed class Journal
        {
            public int formatVersion;
            public string transactionId;
            public string projectRoot;
            public string transactionDirectoryName;
            public string phase;
            public long sequence;
            public FileRecord[] records;
        }

        [Serializable]
        private sealed class FileRecord
        {
            public string relativePath;
            public string snapshotFileName;
            public long length;
            public string sha256;
            public long lastWriteTimeUtcTicks;
            public int attributes;
            [NonSerialized] public byte[] bytes;
        }

        private sealed class SourceRecord
        {
            public SourceRecord(
                string relativePath,
                byte[] bytes,
                DateTime lastWriteTimeUtc,
                FileAttributes attributes)
            {
                RelativePath = relativePath;
                Bytes = bytes;
                LastWriteTimeUtc = lastWriteTimeUtc;
                Attributes = attributes;
            }

            public string RelativePath { get; }
            public byte[] Bytes { get; }
            public DateTime LastWriteTimeUtc { get; }
            public FileAttributes Attributes { get; }
        }

        private sealed class StableFile
        {
            public StableFile(
                byte[] bytes,
                DateTime lastWriteTimeUtc,
                FileAttributes attributes)
            {
                Bytes = bytes;
                LastWriteTimeUtc = lastWriteTimeUtc;
                Attributes = attributes;
            }

            public byte[] Bytes { get; }
            public DateTime LastWriteTimeUtc { get; }
            public FileAttributes Attributes { get; }
        }
    }

    internal sealed class AddressablesSettingsSimulatedTerminationException : Exception
    {
        public AddressablesSettingsSimulatedTerminationException(string checkpoint)
            : base("Simulated Addressables settings process termination at checkpoint: " + checkpoint)
        {
        }
    }
}
