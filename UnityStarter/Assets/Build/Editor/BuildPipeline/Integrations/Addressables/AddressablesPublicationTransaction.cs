using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    internal sealed class AddressablesBuildLock : IDisposable
    {
        private const string LockRelativePath = "Library/BuildPipeline/Addressables/build.lock";
        private readonly FileStream stream;

        private AddressablesBuildLock(FileStream stream)
        {
            this.stream = stream;
        }

        public static AddressablesBuildLock Acquire(string projectRoot)
        {
            string normalizedProjectRoot = Path.GetFullPath(projectRoot);
            string lockPath = Path.GetFullPath(Path.Combine(normalizedProjectRoot, LockRelativePath));
            if (!BuildPathPolicy.IsStrictDescendant(normalizedProjectRoot, lockPath))
            {
                throw new InvalidOperationException($"Addressables build lock escaped the project root: '{lockPath}'.");
            }

            BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                lockPath,
                "Addressables build lock");

            string lockDirectory = Path.GetDirectoryName(lockPath);
            if (string.IsNullOrEmpty(lockDirectory))
            {
                throw new InvalidOperationException("Addressables build lock has no parent directory.");
            }

            BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                lockDirectory,
                "Addressables build lock directory");

            AddressablesPublicationOwnership.EnsurePathComponentsAreNotReparsePoints(
                normalizedProjectRoot,
                lockDirectory);
            Directory.CreateDirectory(lockDirectory);
            AddressablesPublicationOwnership.EnsurePathComponentsAreNotReparsePoints(
                normalizedProjectRoot,
                lockDirectory);
            if (AddressablesPublicationOwnership.TryGetAttributes(lockPath, out FileAttributes attributes)
                && (attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"Addressables build lock cannot be a symbolic link or reparse point: '{lockPath}'.");
            }

            try
            {
                var stream = new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough);
                if ((File.GetAttributes(lockPath) & FileAttributes.ReparsePoint) != 0)
                {
                    stream.Dispose();
                    throw new InvalidOperationException(
                        $"Addressables build lock became a symbolic link or reparse point: '{lockPath}'.");
                }

                return new AddressablesBuildLock(stream);
            }
            catch (IOException exception)
            {
                throw new InvalidOperationException(
                    "Another Addressables build or recovery operation is active for this Unity project.",
                    exception);
            }
        }

        public void Dispose()
        {
            stream.Dispose();
        }
    }

    internal sealed class AddressablesPublicationTransaction : IDisposable
    {
        internal const string PreparedCheckpoint = "Prepared";
        internal const string BackupMovedCheckpoint = "BackupMoved";
        internal const string InstalledCheckpoint = "Installed";
        internal const string CommittedCheckpoint = "Committed";

        private const int JournalSchemaVersion = 1;
        private const int MaximumJournalBytes = 64 * 1024;
        private const string JournalOwner = "Build.Pipeline.AddressablesPublication";
        private const string StateRelativePath = ".buildpipeline/transactions/addressables";
        private const string JournalFileName = "active.json";
        private const string JournalTemporaryFileName = "active.json.tmp";
        private const string JournalBackupFileName = "active.json.bak";
        private const string PreparedPhase = "Prepared";
        private const string CommittingPhase = "Committing";
        private const string CommittedPhase = "Committed";
        private const string RollingBackPhase = "RollingBack";
        private const string PreparedState = "Prepared";
        private const string ReadyState = "Ready";
        private const string BackupPendingState = "BackupPending";
        private const string BackedUpState = "BackedUp";
        private const string InstalledState = "Installed";

        private readonly string projectRoot;
        private readonly string publicationRoot;
        private readonly string destination;
        private readonly string stateRoot;
        private readonly string journalPath;
        private readonly Journal journal;
        private bool prepared;
        private bool completed;
        private bool preserveForRecovery;
        private bool disposed;

        private AddressablesPublicationTransaction(
            string projectRoot,
            string publicationRoot,
            string destination,
            Journal journal)
        {
            this.projectRoot = projectRoot;
            this.publicationRoot = publicationRoot;
            this.destination = destination;
            this.journal = journal;
            stateRoot = GetStateRoot(projectRoot);
            journalPath = Path.Combine(stateRoot, JournalFileName);
        }

        public string StagingDirectory => journal.stage;
        public string TransactionId => journal.transactionId;

        public static AddressablesPublicationTransaction Begin(
            string projectRoot,
            string publicationRoot,
            string destination,
            string deterministicTransactionKey = null)
        {
            string normalizedProjectRoot = Path.GetFullPath(projectRoot);
            string normalizedPublicationRoot = Path.GetFullPath(publicationRoot);
            string normalizedDestination = Path.GetFullPath(destination);
            ValidateRoots(normalizedProjectRoot, normalizedPublicationRoot, normalizedDestination);

            string transactionId = string.IsNullOrEmpty(deterministicTransactionKey)
                ? Guid.NewGuid().ToString("N")
                : CreateDeterministicTransactionId(deterministicTransactionKey);
            string destinationName = Path.GetFileName(normalizedDestination);
            string stage = Path.Combine(
                normalizedPublicationRoot,
                destinationName + ".stage-" + transactionId);
            string backup = Path.Combine(
                normalizedPublicationRoot,
                destinationName + ".backup-" + transactionId);
            ValidateTransactionPathBudget(
                normalizedProjectRoot,
                normalizedPublicationRoot,
                normalizedDestination,
                stage,
                backup);
            var journal = new Journal
            {
                schemaVersion = JournalSchemaVersion,
                owner = JournalOwner,
                transactionId = transactionId,
                phase = PreparedPhase,
                state = PreparedState,
                projectRoot = normalizedProjectRoot,
                publicationRoot = normalizedPublicationRoot,
                destination = normalizedDestination,
                stage = stage,
                backup = backup,
                targetInitiallyExisted = false,
                initialIdentity = string.Empty,
                stagedIdentity = string.Empty,
                checksum = string.Empty
            };
            return new AddressablesPublicationTransaction(
                normalizedProjectRoot,
                normalizedPublicationRoot,
                normalizedDestination,
                journal);
        }

        private static string CreateDeterministicTransactionId(string key)
        {
            if (key.Length > 1024)
            {
                throw new ArgumentException(
                    "Addressables deterministic transaction key exceeds 1024 characters.",
                    nameof(key));
            }

            byte[] bytes;
            try
            {
                bytes = new UTF8Encoding(false, true).GetBytes(key);
            }
            catch (EncoderFallbackException exception)
            {
                throw new ArgumentException(
                    "Addressables deterministic transaction key contains invalid Unicode.",
                    nameof(key),
                    exception);
            }

            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(bytes);
                var builder = new StringBuilder(32);
                for (int index = 0; index < 16; index++)
                {
                    builder.Append(hash[index].ToString("x2"));
                }

                return builder.ToString();
            }
        }

        private static void ValidateTransactionPathBudget(
            string projectRoot,
            string publicationRoot,
            string destination,
            string stage,
            string backup)
        {
            string stateRoot = GetStateRoot(projectRoot);
            BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                stateRoot,
                "Addressables publication transaction state root");
            BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                Path.Combine(stateRoot, JournalFileName),
                "Addressables publication transaction journal");
            BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                Path.Combine(stateRoot, JournalTemporaryFileName),
                "Addressables publication temporary journal");
            BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                Path.Combine(stateRoot, JournalBackupFileName),
                "Addressables publication backup journal");
            BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                publicationRoot,
                "Addressables publication root");
            BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                destination,
                "Addressables publication destination");
            BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                stage,
                "Addressables publication stage directory");
            BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                backup,
                "Addressables publication backup directory");
            BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                Path.Combine(stage, AddressablesPublicationOwnership.OwnerFileName),
                "Addressables publication stage owner");
            BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                Path.Combine(stage, AddressablesPublicationOwnership.OwnerTemporaryFileName),
                "Addressables publication temporary owner");
        }

        public static void RecoverPending(string projectRoot)
        {
            string normalizedProjectRoot = Path.GetFullPath(projectRoot);
            string stateRoot = GetStateRoot(normalizedProjectRoot);
            string journalPath = Path.Combine(stateRoot, JournalFileName);
            RecoverJournalScratch(normalizedProjectRoot, stateRoot, journalPath);
            if (!PathExists(journalPath))
            {
                EnsureCentralStateIsEmpty(normalizedProjectRoot, stateRoot);
                return;
            }

            Journal recovered = ReadAndValidateJournal(journalPath, normalizedProjectRoot);
            RecoverPending(
                normalizedProjectRoot,
                recovered.publicationRoot,
                recovered.destination);
        }

        public static void RecoverPending(
            string projectRoot,
            string publicationRoot,
            string destination)
        {
            string normalizedProjectRoot = Path.GetFullPath(projectRoot);
            string normalizedPublicationRoot = Path.GetFullPath(publicationRoot);
            string normalizedDestination = Path.GetFullPath(destination);
            ValidateRoots(normalizedProjectRoot, normalizedPublicationRoot, normalizedDestination);

            string stateRoot = GetStateRoot(normalizedProjectRoot);
            string journalPath = Path.Combine(stateRoot, JournalFileName);
            RecoverJournalScratch(normalizedProjectRoot, stateRoot, journalPath);
            if (!PathExists(journalPath))
            {
                EnsureNoDetachedState(normalizedPublicationRoot, normalizedDestination, stateRoot, null);
                return;
            }

            Journal recovered = ReadAndValidateJournal(
                journalPath,
                normalizedProjectRoot);
            EnsureNoDetachedState(
                recovered.publicationRoot,
                recovered.destination,
                stateRoot,
                recovered);
            CleanupJournalTemporaryFiles(normalizedProjectRoot, stateRoot, journalPath);

            if (string.Equals(recovered.phase, CommittedPhase, StringComparison.Ordinal))
            {
                CleanupCommitted(recovered, journalPath);
            }
            else
            {
                Rollback(recovered, journalPath);
            }

            EnsureNoDetachedState(
                recovered.publicationRoot,
                recovered.destination,
                stateRoot,
                null);
            if (!PathsEqual(recovered.publicationRoot, normalizedPublicationRoot)
                || !PathsEqual(recovered.destination, normalizedDestination))
            {
                EnsureNoDetachedState(
                    normalizedPublicationRoot,
                    normalizedDestination,
                    stateRoot,
                    null);
            }
        }

        public void Prepare()
        {
            ThrowIfDisposed();
            if (prepared)
            {
                throw new InvalidOperationException("The Addressables publication transaction is already prepared.");
            }

            AddressablesPublicationOwnership.EnsurePathComponentsAreNotReparsePoints(
                projectRoot,
                publicationRoot);
            Directory.CreateDirectory(publicationRoot);
            AddressablesPublicationOwnership.EnsurePathComponentsAreNotReparsePoints(
                projectRoot,
                publicationRoot);
            AddressablesPublicationOwnership.EnsurePathComponentsAreNotReparsePoints(
                projectRoot,
                stateRoot);
            Directory.CreateDirectory(stateRoot);
            AddressablesPublicationOwnership.EnsurePathComponentsAreNotReparsePoints(projectRoot, stateRoot);
            if (PathExists(journalPath))
            {
                throw new InvalidOperationException(
                    $"A pending Addressables publication journal must be recovered first: '{journalPath}'.");
            }

            EnsureNoDetachedState(publicationRoot, destination, stateRoot, null);
            journal.initialIdentity = AddressablesPublicationOwnership.CaptureIdentity(destination);
            journal.targetInitiallyExisted = !string.Equals(
                journal.initialIdentity,
                AddressablesPublicationOwnership.AbsentIdentity,
                StringComparison.Ordinal);
            WriteJournal(journal, journalPath, createNew: true);
            prepared = true;
            Directory.CreateDirectory(journal.stage);
            AddressablesPublicationOwnership.WriteStageMarker(
                journal.stage,
                journal.transactionId);
        }

        public void MarkStageReady(string stagedIdentity)
        {
            ThrowIfDisposed();
            EnsurePrepared();
            if (string.IsNullOrWhiteSpace(stagedIdentity)
                || string.Equals(stagedIdentity, AddressablesPublicationOwnership.AbsentIdentity, StringComparison.Ordinal)
                || string.Equals(stagedIdentity, AddressablesPublicationOwnership.EmptyIdentity, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Addressables staged publication identity is invalid.");
            }

            string actualIdentity = AddressablesPublicationOwnership.CaptureIdentity(
                journal.stage,
                journal.transactionId);
            if (!string.Equals(actualIdentity, stagedIdentity, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Addressables staged publication changed before it was marked ready.");
            }

            journal.stagedIdentity = stagedIdentity;
            journal.state = ReadyState;
            WriteJournal(journal, journalPath, createNew: false);
        }

        public void Commit(Action validatePublishedState)
        {
            Commit(validatePublishedState, null);
        }

        internal void Commit(Action validatePublishedState, Action<string> checkpoint)
        {
            ThrowIfDisposed();
            EnsurePrepared();
            if (!string.Equals(journal.state, ReadyState, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(journal.stagedIdentity))
            {
                throw new InvalidOperationException("Addressables staging has not been completed and verified.");
            }

            try
            {
                string currentInitialIdentity = AddressablesPublicationOwnership.CaptureIdentity(destination);
                if (!string.Equals(currentInitialIdentity, journal.initialIdentity, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Addressables publication destination changed while content was being staged.");
                }

                if (journal.targetInitiallyExisted)
                {
                    AddressablesPublicationOwnership.ValidateMappedTreePathBudget(
                        destination,
                        journal.backup);
                    string identityAfterBudgetValidation =
                        AddressablesPublicationOwnership.CaptureIdentity(destination);
                    if (!string.Equals(
                            identityAfterBudgetValidation,
                            journal.initialIdentity,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "Addressables publication destination changed while its backup path budget was validated.");
                    }
                }

                string currentStagedIdentity = AddressablesPublicationOwnership.CaptureIdentity(
                    journal.stage,
                    journal.transactionId);
                if (!string.Equals(currentStagedIdentity, journal.stagedIdentity, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Addressables staged publication changed before commit.");
                }

                journal.phase = CommittingPhase;
                WriteJournal(journal, journalPath, createNew: false);
                checkpoint?.Invoke(PreparedCheckpoint);

                journal.state = BackupPendingState;
                WriteJournal(journal, journalPath, createNew: false);
                if (journal.targetInitiallyExisted)
                {
                    if (PathExists(journal.backup))
                    {
                        throw new InvalidOperationException(
                            $"Addressables publication backup path is occupied: '{journal.backup}'.");
                    }

                    Directory.Move(destination, journal.backup);
                }

                checkpoint?.Invoke(BackupMovedCheckpoint);
                journal.state = BackedUpState;
                WriteJournal(journal, journalPath, createNew: false);

                if (PathExists(destination))
                {
                    throw new InvalidOperationException(
                        $"Addressables destination appeared during commit: '{destination}'.");
                }

                Directory.Move(journal.stage, destination);
                checkpoint?.Invoke(InstalledCheckpoint);
                journal.state = InstalledState;
                WriteJournal(journal, journalPath, createNew: false);

                string installedIdentity = AddressablesPublicationOwnership.CaptureIdentity(
                    destination,
                    journal.transactionId);
                if (!string.Equals(installedIdentity, journal.stagedIdentity, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Addressables installed publication identity is invalid.");
                }

                validatePublishedState?.Invoke();
                journal.phase = CommittedPhase;
                WriteJournal(journal, journalPath, createNew: false);
                preserveForRecovery = true;
                checkpoint?.Invoke(CommittedCheckpoint);
                CleanupCommitted(journal, journalPath);
                preserveForRecovery = false;
                completed = true;
            }
            catch (AddressablesSimulatedTerminationException)
            {
                preserveForRecovery = true;
                throw;
            }
            catch (Exception commitException)
            {
                if (string.Equals(journal.phase, CommittedPhase, StringComparison.Ordinal))
                {
                    preserveForRecovery = true;
                    throw new InvalidOperationException(
                        "Addressables publication committed, but durable transaction cleanup did not complete. " +
                        "The next build will finish cleanup before publishing again.",
                        commitException);
                }

                try
                {
                    Rollback(journal, journalPath);
                    completed = true;
                }
                catch (Exception rollbackException)
                {
                    preserveForRecovery = true;
                    throw new AggregateException(
                        "Addressables publication failed and rollback did not complete. " +
                        "The durable journal was retained for recovery.",
                        commitException,
                        rollbackException);
                }

                ExceptionDispatchInfo.Capture(commitException).Throw();
                throw;
            }
        }

        public void Abort()
        {
            ThrowIfDisposed();
            if (completed || preserveForRecovery)
            {
                return;
            }

            if (prepared && PathExists(journalPath))
            {
                Rollback(journal, journalPath);
            }

            completed = true;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (!completed && !preserveForRecovery && prepared && PathExists(journalPath))
            {
                Rollback(journal, journalPath);
                completed = true;
            }
        }

        internal static string GetStateRoot(string projectRoot)
        {
            return Path.Combine(Path.GetFullPath(projectRoot), StateRelativePath);
        }

        private void EnsurePrepared()
        {
            if (!prepared || !PathExists(journalPath))
            {
                throw new InvalidOperationException("Prepare the Addressables publication transaction first.");
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(AddressablesPublicationTransaction));
            }
        }

        private static void Rollback(Journal recovered, string journalPath)
        {
            recovered.phase = RollingBackPhase;
            var failures = new List<Exception>();
            try
            {
                WriteJournal(recovered, journalPath, createNew: false);
            }
            catch (Exception exception)
            {
                failures.Add(new InvalidOperationException(
                    "Failed to persist the Addressables rollback phase.",
                    exception));
            }

            try
            {
                RollbackDirectories(recovered);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            if (failures.Count > 0)
            {
                throw new AggregateException(
                    "Addressables publication rollback could not restore the owned directory state.",
                    failures);
            }

            DeleteOwnedJournalFile(recovered, journalPath);
        }

        private static void RollbackDirectories(Journal recovered)
        {
            bool destinationExists = IsDirectory(recovered.destination);
            bool stageExists = IsDirectory(recovered.stage);
            bool backupExists = IsDirectory(recovered.backup);
            RejectFilesAtDirectoryPaths(recovered);

            if (backupExists)
            {
                string backupIdentity = AddressablesPublicationOwnership.CaptureIdentity(recovered.backup);
                if (!string.Equals(backupIdentity, recovered.initialIdentity, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Addressables publication backup no longer matches the captured original destination.");
                }

                if (destinationExists)
                {
                    EnsureInstalledIdentity(recovered, recovered.destination);
                    DeleteOwnedPublicationDirectory(
                        recovered,
                        recovered.destination,
                        recovered.stagedIdentity,
                        recovered.transactionId);
                    destinationExists = false;
                }

                Directory.Move(recovered.backup, recovered.destination);
                backupExists = false;
            }
            else if (recovered.targetInitiallyExisted)
            {
                if (!destinationExists)
                {
                    throw new InvalidOperationException(
                        "The original Addressables destination cannot be proven recoverable because both destination and backup are missing.");
                }

                string destinationIdentity = AddressablesPublicationOwnership.CaptureIdentity(recovered.destination);
                if (!string.Equals(destinationIdentity, recovered.initialIdentity, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "The Addressables destination changed and no matching backup is available for rollback.");
                }
            }
            else if (destinationExists)
            {
                EnsureInstalledIdentity(recovered, recovered.destination);
                DeleteOwnedPublicationDirectory(
                    recovered,
                    recovered.destination,
                    recovered.stagedIdentity,
                    recovered.transactionId);
                destinationExists = false;
            }

            if (stageExists)
            {
                if (!string.IsNullOrEmpty(recovered.stagedIdentity))
                {
                    string stageIdentity = AddressablesPublicationOwnership.CaptureIdentity(
                        recovered.stage,
                        recovered.transactionId);
                    if (!string.Equals(stageIdentity, recovered.stagedIdentity, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "Addressables staged directory changed before rollback cleanup.");
                    }
                }
                else
                {
                    AddressablesPublicationOwnership.EnsureUnreadyStageIsOwned(
                        recovered.stage,
                        recovered.transactionId);
                }

                DeleteExactTransactionDirectory(recovered, recovered.stage);
            }

            if (backupExists)
            {
                throw new InvalidOperationException("Addressables backup remained after rollback.");
            }

            string restoredIdentity = AddressablesPublicationOwnership.CaptureIdentity(recovered.destination);
            if (!string.Equals(restoredIdentity, recovered.initialIdentity, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Addressables rollback did not restore the original destination identity.");
            }
        }

        private static void CleanupCommitted(Journal recovered, string journalPath)
        {
            RejectFilesAtDirectoryPaths(recovered);
            string installedIdentity = AddressablesPublicationOwnership.CaptureIdentity(
                recovered.destination,
                recovered.transactionId);
            if (!string.Equals(installedIdentity, recovered.stagedIdentity, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Committed Addressables output changed before transaction cleanup; backup was retained.");
            }

            if (IsDirectory(recovered.stage))
            {
                string stageIdentity = AddressablesPublicationOwnership.CaptureIdentity(
                    recovered.stage,
                    recovered.transactionId);
                if (!string.Equals(stageIdentity, recovered.stagedIdentity, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Addressables stage changed before committed cleanup.");
                }

                DeleteExactTransactionDirectory(recovered, recovered.stage);
            }

            if (IsDirectory(recovered.backup))
            {
                string backupIdentity = AddressablesPublicationOwnership.CaptureIdentity(recovered.backup);
                if (!string.Equals(backupIdentity, recovered.initialIdentity, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Addressables backup changed before committed cleanup.");
                }

                DeleteExactTransactionDirectory(recovered, recovered.backup);
            }

            DeleteOwnedJournalFile(recovered, journalPath);
        }

        private static void EnsureInstalledIdentity(Journal recovered, string path)
        {
            if (string.IsNullOrEmpty(recovered.stagedIdentity))
            {
                throw new InvalidOperationException(
                    "Addressables journal does not contain a staged identity, so an installed target cannot be deleted safely.");
            }

            string identity = AddressablesPublicationOwnership.CaptureIdentity(
                path,
                recovered.transactionId);
            if (!string.Equals(identity, recovered.stagedIdentity, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Addressables installed target is not owned by the interrupted transaction.");
            }
        }

        private static void DeleteOwnedPublicationDirectory(
            Journal recovered,
            string path,
            string expectedIdentity,
            string requiredTransactionId)
        {
            string identity = AddressablesPublicationOwnership.CaptureIdentity(path, requiredTransactionId);
            if (!string.Equals(identity, expectedIdentity, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Refusing to delete Addressables publication with unexpected identity: '{path}'.");
            }

            DeleteExactTransactionDirectory(recovered, path);
        }

        private static void DeleteExactTransactionDirectory(Journal recovered, string path)
        {
            BuildPathPolicy.EnsureSafeDeleteDirectoryTree(
                recovered.projectRoot,
                path,
                recovered.publicationRoot,
                allowExternalOutput: false);
            AddressablesPublicationOwnership.EnsureTreeContainsNoReparsePoints(path);
            Directory.Delete(path, recursive: true);
        }

        private static void DeleteOwnedJournalFile(Journal recovered, string journalPath)
        {
            CleanupJournalTemporaryFiles(
                recovered.projectRoot,
                GetStateRoot(recovered.projectRoot),
                journalPath);
            DeleteExactFile(
                recovered.projectRoot,
                GetStateRoot(recovered.projectRoot),
                journalPath);
        }

        private static void RejectFilesAtDirectoryPaths(Journal recovered)
        {
            foreach (string path in new[] { recovered.destination, recovered.stage, recovered.backup })
            {
                if (TryGetPathKind(path, out bool isDirectory) && !isDirectory)
                {
                    throw new InvalidOperationException(
                        $"Addressables publication directory path became a file: '{path}'.");
                }
            }
        }

        private static Journal ReadAndValidateJournal(
            string journalPath,
            string projectRoot)
        {
            if (!TryGetPathKind(journalPath, out bool isDirectory) || isDirectory)
            {
                throw new InvalidOperationException(
                    $"Addressables publication journal is unavailable or is a directory: '{journalPath}'.");
            }

            FileAttributes attributes = File.GetAttributes(journalPath);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"Addressables publication journal cannot be a reparse point: '{journalPath}'.");
            }

            byte[] bytes = ReadJournalBytes(journalPath);
            string json = AddressablesPublicationOwnership.DecodeStrictUtf8(bytes, "Addressables publication journal");
            Journal recovered;
            try
            {
                recovered = JsonUtility.FromJson<Journal>(json);
            }
            catch (Exception exception)
            {
                throw new InvalidDataException("Addressables publication journal JSON is invalid.", exception);
            }

            if (recovered == null
                || recovered.schemaVersion != JournalSchemaVersion
                || !string.Equals(recovered.owner, JournalOwner, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(recovered.transactionId)
                || !Guid.TryParseExact(recovered.transactionId, "N", out _)
                || !IsKnownPhase(recovered.phase)
                || !IsKnownState(recovered.state)
                || !IsValidPhaseState(recovered.phase, recovered.state)
                || !IsKnownPublicationIdentity(recovered.initialIdentity)
                || (string.Equals(recovered.state, PreparedState, StringComparison.Ordinal)
                    ? !string.IsNullOrEmpty(recovered.stagedIdentity)
                    : !IsOwnedPublicationIdentity(recovered.stagedIdentity)))
            {
                throw new InvalidDataException("Addressables publication journal contract is invalid.");
            }

            string expectedChecksum = recovered.checksum;
            if (string.IsNullOrWhiteSpace(expectedChecksum))
            {
                throw new InvalidDataException("Addressables publication journal checksum is missing.");
            }

            recovered.checksum = string.Empty;
            string actualChecksum = ComputeChecksum(recovered);
            recovered.checksum = expectedChecksum;
            if (!string.Equals(actualChecksum, expectedChecksum, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Addressables publication journal checksum is invalid.");
            }

            ValidateJournalPaths(recovered, projectRoot);
            return recovered;
        }

        private static void ValidateJournalPaths(
            Journal journal,
            string projectRoot)
        {
            if (!PathsEqual(journal.projectRoot, projectRoot))
            {
                throw new InvalidDataException(
                    "Addressables publication journal project root does not match the current build request.");
            }

            string publicationRoot = Path.GetFullPath(journal.publicationRoot);
            string destination = Path.GetFullPath(journal.destination);
            ValidateRoots(projectRoot, publicationRoot, destination);
            string destinationName = Path.GetFileName(destination);
            string expectedStage = Path.Combine(
                publicationRoot,
                destinationName + ".stage-" + journal.transactionId);
            string expectedBackup = Path.Combine(
                publicationRoot,
                destinationName + ".backup-" + journal.transactionId);
            if (!PathsEqual(journal.stage, expectedStage) || !PathsEqual(journal.backup, expectedBackup))
            {
                throw new InvalidDataException(
                    "Addressables publication journal contains unexpected stage or backup paths.");
            }

            if (journal.targetInitiallyExisted
                && string.Equals(journal.initialIdentity, AddressablesPublicationOwnership.AbsentIdentity, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Addressables journal initial-existence state is inconsistent.");
            }

            if (!journal.targetInitiallyExisted
                && !string.Equals(journal.initialIdentity, AddressablesPublicationOwnership.AbsentIdentity, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Addressables journal initial identity is inconsistent.");
            }

            AddressablesPublicationOwnership.EnsurePathComponentsAreNotReparsePoints(
                projectRoot,
                GetStateRoot(projectRoot));
        }

        private static void ValidateRoots(
            string projectRoot,
            string publicationRoot,
            string destination)
        {
            if (!BuildPathPolicy.IsStrictDescendant(projectRoot, publicationRoot))
            {
                throw new InvalidOperationException(
                    $"Addressables publication root must be below the Unity project: '{publicationRoot}'.");
            }

            if (!BuildPathPolicy.IsStrictDescendant(publicationRoot, destination))
            {
                throw new InvalidOperationException(
                    $"Addressables destination must be below its publication root: '{destination}'.");
            }

            BuildPathPolicy.EnsureSafeDeleteTarget(
                projectRoot,
                destination,
                publicationRoot,
                allowExternalOutput: false);
            AddressablesPublicationOwnership.EnsurePathComponentsAreNotReparsePoints(
                projectRoot,
                publicationRoot);
        }

        private static void WriteJournal(Journal journal, string journalPath, bool createNew)
        {
            journal.checksum = string.Empty;
            journal.checksum = ComputeChecksum(journal);
            byte[] bytes = new UTF8Encoding(false, true).GetBytes(JsonUtility.ToJson(journal, true));
            if (bytes.Length <= 0 || bytes.Length > MaximumJournalBytes)
            {
                throw new InvalidOperationException(
                    $"Addressables publication journal exceeds {MaximumJournalBytes} bytes.");
            }

            string stateRoot = Path.GetDirectoryName(journalPath);
            if (string.IsNullOrEmpty(stateRoot))
            {
                throw new InvalidOperationException(
                    "Addressables publication journal has no state root.");
            }

            string temporaryPath = Path.Combine(stateRoot, JournalTemporaryFileName);
            string backupPath = Path.Combine(stateRoot, JournalBackupFileName);
            if (PathExists(temporaryPath) || PathExists(backupPath))
            {
                throw new InvalidOperationException(
                    $"Addressables publication journal scratch requires recovery under '{stateRoot}'.");
            }
            Exception failure = null;
            try
            {
                WriteNewFileFlushed(temporaryPath, bytes);
                if (createNew)
                {
                    if (PathExists(journalPath))
                    {
                        throw new IOException(
                            $"Addressables publication journal already exists: '{journalPath}'.");
                    }

                    File.Move(temporaryPath, journalPath);
                }
                else
                {
                    if (!PathExists(journalPath))
                    {
                        throw new FileNotFoundException(
                            "Addressables publication journal disappeared before an update.",
                            journalPath);
                    }

                    File.Replace(temporaryPath, journalPath, backupPath);
                    DeleteExactFile(journal.projectRoot, stateRoot, backupPath);
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            Exception cleanupFailure = null;
            try
            {
                if (PathExists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (Exception exception)
            {
                cleanupFailure = exception;
            }

            if (failure != null && cleanupFailure != null)
            {
                throw new AggregateException(
                    "Addressables journal update and temporary-file cleanup both failed.",
                    failure,
                    cleanupFailure);
            }

            if (failure != null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }

            if (cleanupFailure != null)
            {
                throw new IOException(
                    "Addressables journal update succeeded, but temporary-file cleanup failed.",
                    cleanupFailure);
            }
        }

        private static void WriteNewFileFlushed(string path, byte[] bytes)
        {
            using (var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        private static string ComputeChecksum(Journal journal)
        {
            string json = JsonUtility.ToJson(journal, false);
            using (SHA256 sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(new UTF8Encoding(false, true).GetBytes(json)));
            }
        }

        private static void EnsureNoDetachedState(
            string publicationRoot,
            string destination,
            string stateRoot,
            Journal activeJournal)
        {
            if (TryGetPathKind(publicationRoot, out bool rootIsDirectory))
            {
                if (!rootIsDirectory)
                {
                    throw new InvalidOperationException(
                        $"Addressables publication root is a file: '{publicationRoot}'.");
                }

                string destinationName = Path.GetFileName(destination);
                string stagePrefix = destinationName + ".stage-";
                string backupPrefix = destinationName + ".backup-";
                foreach (string directory in Directory.EnumerateDirectories(
                    publicationRoot,
                    "*",
                    SearchOption.TopDirectoryOnly))
                {
                    string name = Path.GetFileName(directory);
                    if (!name.StartsWith(stagePrefix, StringComparison.Ordinal)
                        && !name.StartsWith(backupPrefix, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    bool expected = activeJournal != null
                        && (PathsEqual(directory, activeJournal.stage)
                            || PathsEqual(directory, activeJournal.backup));
                    if (!expected)
                    {
                        throw new InvalidOperationException(
                            $"Detached Addressables publication scratch requires manual review: '{directory}'.");
                    }
                }

                foreach (string file in Directory.EnumerateFiles(
                    publicationRoot,
                    "*",
                    SearchOption.TopDirectoryOnly))
                {
                    string name = Path.GetFileName(file);
                    if (name.StartsWith(stagePrefix, StringComparison.Ordinal)
                        || name.StartsWith(backupPrefix, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Addressables publication scratch path became a file and requires manual review: '{file}'.");
                    }
                }
            }

            if (!TryGetPathKind(stateRoot, out bool stateIsDirectory))
            {
                return;
            }

            if (!stateIsDirectory)
            {
                throw new InvalidOperationException(
                    $"Addressables transaction state root is a file: '{stateRoot}'.");
            }

            string journalPath = Path.Combine(stateRoot, JournalFileName);
            foreach (string entry in Directory.EnumerateFileSystemEntries(stateRoot))
            {
                string name = Path.GetFileName(entry);
                bool allowed = PathsEqual(entry, journalPath)
                    || string.Equals(name, JournalTemporaryFileName, StringComparison.Ordinal)
                    || string.Equals(name, JournalBackupFileName, StringComparison.Ordinal);
                if (allowed
                    && TryGetPathKind(entry, out bool entryIsDirectory)
                    && !entryIsDirectory
                    && (File.GetAttributes(entry) & FileAttributes.ReparsePoint) == 0)
                {
                    continue;
                }

                throw new InvalidOperationException(
                    $"Unknown Addressables transaction state entry requires manual review: '{entry}'.");
            }

            if (activeJournal == null
                && (PathExists(Path.Combine(stateRoot, JournalTemporaryFileName))
                    || PathExists(Path.Combine(stateRoot, JournalBackupFileName))))
            {
                throw new InvalidOperationException(
                    $"Detached Addressables journal temporary file requires manual review under '{stateRoot}'.");
            }
        }

        private static void CleanupJournalTemporaryFiles(
            string projectRoot,
            string stateRoot,
            string journalPath)
        {
            RecoverJournalScratch(projectRoot, stateRoot, journalPath);
        }

        private static void RecoverJournalScratch(
            string projectRoot,
            string stateRoot,
            string journalPath)
        {
            if (!PathExists(stateRoot))
            {
                return;
            }

            if (!IsDirectory(stateRoot))
            {
                throw new InvalidOperationException(
                    $"Addressables transaction state root is not a directory: '{stateRoot}'.");
            }

            AddressablesPublicationOwnership.EnsurePathComponentsAreNotReparsePoints(
                projectRoot,
                stateRoot);
            string temporaryPath = Path.Combine(stateRoot, JournalTemporaryFileName);
            string backupPath = Path.Combine(stateRoot, JournalBackupFileName);
            Journal active = TryReadJournalCandidate(journalPath, projectRoot);
            Journal temporary = TryReadJournalCandidate(temporaryPath, projectRoot);
            Journal backup = TryReadJournalCandidate(backupPath, projectRoot);
            if (active != null)
            {
                if (temporary != null && backup != null)
                {
                    throw new InvalidDataException(
                        "Addressables publication journal contains both temporary and backup scratch beside an active journal.");
                }

                if (temporary != null)
                {
                    ValidateSameTransaction(active, temporary, "temporary");
                }

                if (backup != null)
                {
                    ValidateSameTransaction(active, backup, "backup");
                }

                DeleteExactFile(projectRoot, stateRoot, temporaryPath);
                DeleteExactFile(projectRoot, stateRoot, backupPath);
                return;
            }

            if (backup != null)
            {
                if (temporary != null)
                {
                    ValidateSameTransaction(backup, temporary, "temporary");
                }

                File.Move(backupPath, journalPath);
                ReadAndValidateJournal(journalPath, projectRoot);
                DeleteExactFile(projectRoot, stateRoot, temporaryPath);
                return;
            }

            if (temporary != null)
            {
                File.Move(temporaryPath, journalPath);
                ReadAndValidateJournal(journalPath, projectRoot);
            }
        }

        private static Journal TryReadJournalCandidate(string path, string projectRoot)
        {
            return PathExists(path)
                ? ReadAndValidateJournal(path, projectRoot)
                : null;
        }

        private static void ValidateSameTransaction(
            Journal expected,
            Journal candidate,
            string candidateLabel)
        {
            if (!string.Equals(
                    expected.transactionId,
                    candidate.transactionId,
                    StringComparison.Ordinal)
                || !PathsEqual(expected.projectRoot, candidate.projectRoot)
                || !PathsEqual(expected.publicationRoot, candidate.publicationRoot)
                || !PathsEqual(expected.destination, candidate.destination)
                || !PathsEqual(expected.stage, candidate.stage)
                || !PathsEqual(expected.backup, candidate.backup)
                || expected.targetInitiallyExisted != candidate.targetInitiallyExisted
                || !string.Equals(
                    expected.initialIdentity,
                    candidate.initialIdentity,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Addressables publication {candidateLabel} journal does not belong to the same immutable transaction state.");
            }
        }

        private static void EnsureCentralStateIsEmpty(string projectRoot, string stateRoot)
        {
            if (!PathExists(stateRoot))
            {
                return;
            }

            if (!IsDirectory(stateRoot))
            {
                throw new InvalidOperationException(
                    $"Addressables transaction state root is not a directory: '{stateRoot}'.");
            }

            AddressablesPublicationOwnership.EnsurePathComponentsAreNotReparsePoints(
                projectRoot,
                stateRoot);
            string unexpected = Directory.EnumerateFileSystemEntries(stateRoot).FirstOrDefault();
            if (unexpected != null)
            {
                throw new InvalidOperationException(
                    $"Detached Addressables transaction state requires manual review: '{unexpected}'.");
            }
        }

        private static byte[] ReadJournalBytes(string path)
        {
            using (var stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       4096,
                       FileOptions.SequentialScan))
            {
                if (stream.Length <= 0 || stream.Length > MaximumJournalBytes)
                {
                    throw new InvalidDataException(
                        $"Addressables publication journal size is invalid: {stream.Length} bytes.");
                }

                var bytes = new byte[(int)stream.Length];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int read = stream.Read(bytes, offset, bytes.Length - offset);
                    if (read <= 0)
                    {
                        throw new EndOfStreamException(
                            $"Addressables publication journal changed while it was read: '{path}'.");
                    }

                    offset += read;
                }

                if (stream.ReadByte() != -1)
                {
                    throw new IOException(
                        $"Addressables publication journal grew while it was read: '{path}'.");
                }

                return bytes;
            }
        }

        private static void DeleteExactFile(string projectRoot, string approvedRoot, string path)
        {
            string fullPath = Path.GetFullPath(path);
            if (!BuildPathPolicy.IsStrictDescendant(approvedRoot, fullPath))
            {
                throw new InvalidOperationException(
                    $"Refusing to delete Addressables transaction file outside its state root: '{fullPath}'.");
            }

            if (!TryGetPathKind(fullPath, out bool isDirectory))
            {
                return;
            }

            if (isDirectory)
            {
                throw new InvalidOperationException(
                    $"Addressables transaction file path became a directory: '{fullPath}'.");
            }

            AddressablesPublicationOwnership.EnsurePathComponentsAreNotReparsePoints(projectRoot, fullPath);
            File.Delete(fullPath);
        }

        private static bool IsKnownPhase(string phase)
        {
            return string.Equals(phase, PreparedPhase, StringComparison.Ordinal)
                || string.Equals(phase, CommittingPhase, StringComparison.Ordinal)
                || string.Equals(phase, CommittedPhase, StringComparison.Ordinal)
                || string.Equals(phase, RollingBackPhase, StringComparison.Ordinal);
        }

        private static bool IsKnownState(string state)
        {
            return string.Equals(state, PreparedState, StringComparison.Ordinal)
                || string.Equals(state, ReadyState, StringComparison.Ordinal)
                || string.Equals(state, BackupPendingState, StringComparison.Ordinal)
                || string.Equals(state, BackedUpState, StringComparison.Ordinal)
                || string.Equals(state, InstalledState, StringComparison.Ordinal);
        }

        private static bool IsValidPhaseState(string phase, string state)
        {
            if (string.Equals(phase, PreparedPhase, StringComparison.Ordinal))
            {
                return string.Equals(state, PreparedState, StringComparison.Ordinal)
                    || string.Equals(state, ReadyState, StringComparison.Ordinal);
            }

            if (string.Equals(phase, CommittingPhase, StringComparison.Ordinal))
            {
                return string.Equals(state, ReadyState, StringComparison.Ordinal)
                    || string.Equals(state, BackupPendingState, StringComparison.Ordinal)
                    || string.Equals(state, BackedUpState, StringComparison.Ordinal)
                    || string.Equals(state, InstalledState, StringComparison.Ordinal);
            }

            return string.Equals(phase, RollingBackPhase, StringComparison.Ordinal)
                || (string.Equals(phase, CommittedPhase, StringComparison.Ordinal)
                    && string.Equals(state, InstalledState, StringComparison.Ordinal));
        }

        private static bool IsKnownPublicationIdentity(string identity)
        {
            return string.Equals(
                       identity,
                       AddressablesPublicationOwnership.AbsentIdentity,
                       StringComparison.Ordinal)
                || string.Equals(
                    identity,
                    AddressablesPublicationOwnership.EmptyIdentity,
                    StringComparison.Ordinal)
                || IsOwnedPublicationIdentity(identity);
        }

        private static bool IsOwnedPublicationIdentity(string identity)
        {
            const string prefix = "OWNED:";
            if (identity == null || !identity.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            string hash = identity.Substring(prefix.Length);
            if (hash.Length != 64)
            {
                return false;
            }

            for (int index = 0; index < hash.Length; index++)
            {
                char character = hash[index];
                if (!((character >= '0' && character <= '9')
                      || (character >= 'A' && character <= 'F')))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool PathExists(string path)
        {
            return TryGetPathKind(path, out _);
        }

        private static bool IsDirectory(string path)
        {
            return TryGetPathKind(path, out bool isDirectory) && isDirectory;
        }

        private static bool TryGetPathKind(string path, out bool isDirectory)
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(path);
                isDirectory = (attributes & FileAttributes.Directory) != 0;
                return true;
            }
            catch (FileNotFoundException)
            {
                isDirectory = false;
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                isDirectory = false;
                return false;
            }
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.DirectorySeparatorChar == '\\'
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal);
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

        [Serializable]
        private sealed class Journal
        {
            public int schemaVersion;
            public string owner;
            public string transactionId;
            public string phase;
            public string state;
            public string projectRoot;
            public string publicationRoot;
            public string destination;
            public string stage;
            public string backup;
            public bool targetInitiallyExisted;
            public string initialIdentity;
            public string stagedIdentity;
            public string checksum;
        }
    }

    internal sealed class AddressablesSimulatedTerminationException : Exception
    {
        public AddressablesSimulatedTerminationException(string checkpoint)
            : base("Simulated Addressables process termination at checkpoint: " + checkpoint)
        {
        }
    }

    internal static class AddressablesPublicationOwnership
    {
        internal const string AbsentIdentity = "ABSENT";
        internal const string EmptyIdentity = "EMPTY";
        internal const string OwnerFileName = ".buildpipeline-owner.json";
        internal const string OwnerTemporaryFileName = ".bp-owner.tmp";
        internal const string ArtifactManifestFileName = "AddressablesArtifacts.json";

        private const int OwnerSchemaVersion = 1;
        private const int MaximumOwnerBytes = 64 * 1024;
        private const int MaximumManifestBytes = 16 * 1024 * 1024;
        private const int MaximumEntries = 250000;
        private const int MaximumDepth = 64;
        private const long MaximumTotalBytes = 256L * 1024 * 1024 * 1024;
        private const string OwnerIdentifier = "Build.Pipeline.AddressablesPublication";
        private const string StageOwnerIdentifier = "Build.Pipeline.AddressablesPublication.Stage";

        public static void WriteStageMarker(string publicationDirectory, string transactionId)
        {
            if (string.IsNullOrWhiteSpace(transactionId)
                || !Guid.TryParseExact(transactionId, "N", out _))
            {
                throw new ArgumentException("A valid Addressables transaction id is required.", nameof(transactionId));
            }

            Directory.CreateDirectory(publicationDirectory);
            EnsureTreeContainsNoReparsePoints(publicationDirectory);
            if (Directory.EnumerateFileSystemEntries(publicationDirectory).Any())
            {
                throw new InvalidOperationException(
                    $"Addressables stage must be empty before ownership is established: '{publicationDirectory}'.");
            }

            var marker = new StageOwnerDocument
            {
                schemaVersion = OwnerSchemaVersion,
                owner = StageOwnerIdentifier,
                transactionId = transactionId,
                checksum = string.Empty
            };
            marker.checksum = ComputeSha256(
                new UTF8Encoding(false, true).GetBytes(JsonUtility.ToJson(marker, false)));
            byte[] markerBytes = new UTF8Encoding(false, true).GetBytes(JsonUtility.ToJson(marker, true));
            if (markerBytes.Length > MaximumOwnerBytes)
            {
                throw new InvalidOperationException("Addressables stage marker exceeds its safety budget.");
            }

            WriteNewFileDurably(
                Path.Combine(publicationDirectory, OwnerFileName),
                markerBytes);
        }

        public static void WriteOwner(string publicationDirectory, string transactionId)
        {
            ValidateStageMarker(publicationDirectory, transactionId);
            string manifestPath = Path.Combine(publicationDirectory, ArtifactManifestFileName);
            if (!TryGetAttributes(manifestPath, out FileAttributes manifestAttributes)
                || (manifestAttributes & FileAttributes.Directory) != 0)
            {
                throw new FileNotFoundException(
                    "Addressables artifact manifest is required before writing publication ownership.",
                    manifestPath);
            }

            if ((manifestAttributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"Addressables artifact manifest cannot be a reparse point: '{manifestPath}'.");
            }

            byte[] manifestBytes = ReadBoundedFile(
                manifestPath,
                MaximumManifestBytes,
                "Addressables artifact manifest");
            var owner = new OwnerDocument
            {
                schemaVersion = OwnerSchemaVersion,
                owner = OwnerIdentifier,
                transactionId = transactionId,
                manifestSha256 = ComputeSha256(manifestBytes)
            };
            byte[] ownerBytes = new UTF8Encoding(false, true).GetBytes(JsonUtility.ToJson(owner, true));
            if (ownerBytes.Length > MaximumOwnerBytes)
            {
                throw new InvalidOperationException("Addressables ownership document exceeds its safety budget.");
            }

            string ownerPath = Path.Combine(publicationDirectory, OwnerFileName);
            // Keep the scratch name shorter than the final owner name. Appending a GUID to
            // ownerPath can push otherwise valid Windows publication paths past MAX_PATH.
            string temporaryPath = Path.Combine(publicationDirectory, OwnerTemporaryFileName);
            Exception failure = null;
            try
            {
                if (TryGetAttributes(temporaryPath, out _))
                {
                    throw new InvalidOperationException(
                        $"Addressables final-owner scratch path already exists: '{temporaryPath}'.");
                }

                WriteNewFileDurably(temporaryPath, ownerBytes);
                File.Replace(temporaryPath, ownerPath, null);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            Exception cleanupFailure = null;
            try
            {
                if (TryGetAttributes(temporaryPath, out FileAttributes temporaryAttributes))
                {
                    if ((temporaryAttributes & FileAttributes.Directory) != 0
                        || (temporaryAttributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidOperationException(
                            $"Addressables final-owner temporary path became unsafe: '{temporaryPath}'.");
                    }

                    File.Delete(temporaryPath);
                }
            }
            catch (Exception exception)
            {
                cleanupFailure = exception;
            }

            if (failure != null && cleanupFailure != null)
            {
                throw new AggregateException(
                    "Addressables owner replacement and temporary cleanup both failed.",
                    failure,
                    cleanupFailure);
            }

            if (failure != null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }

            if (cleanupFailure != null)
            {
                throw new IOException(
                    "Addressables owner replacement succeeded, but temporary cleanup failed.",
                    cleanupFailure);
            }
        }

        public static void EnsureUnreadyStageIsOwned(
            string publicationDirectory,
            string transactionId)
        {
            EnsureTreeContainsNoReparsePoints(publicationDirectory);
            if (!Directory.EnumerateFileSystemEntries(publicationDirectory).Any())
            {
                return;
            }

            try
            {
                CaptureIdentity(publicationDirectory, transactionId);
                return;
            }
            catch (InvalidDataException)
            {
                // An in-progress stage uses the stage marker until its exact final owner replaces it.
            }
            catch (FileNotFoundException)
            {
                // ValidateStageMarker below provides the fail-closed ownership error.
            }

            ValidateStageMarker(publicationDirectory, transactionId);
        }

        public static string CaptureIdentity(string publicationDirectory, string requiredTransactionId = null)
        {
            if (!TryGetAttributes(publicationDirectory, out FileAttributes attributes))
            {
                return AbsentIdentity;
            }

            if ((attributes & FileAttributes.Directory) == 0)
            {
                throw new InvalidOperationException(
                    $"Addressables publication path is a file: '{publicationDirectory}'.");
            }

            EnsureTreeContainsNoReparsePoints(publicationDirectory);
            string[] topEntries = Directory.GetFileSystemEntries(publicationDirectory);
            if (topEntries.Length == 0)
            {
                if (!string.IsNullOrEmpty(requiredTransactionId))
                {
                    throw new InvalidOperationException(
                        $"Addressables staged publication is empty: '{publicationDirectory}'.");
                }

                return EmptyIdentity;
            }

            string ownerPath = Path.Combine(publicationDirectory, OwnerFileName);
            string manifestPath = Path.Combine(publicationDirectory, ArtifactManifestFileName);
            byte[] ownerBytes;
            byte[] manifestBytes;
            OwnerDocument owner;
            PublicationManifest manifest;
            try
            {
                ownerBytes = ReadBoundedFile(
                    ownerPath,
                    MaximumOwnerBytes,
                    "Addressables ownership document");
                manifestBytes = ReadBoundedFile(
                    manifestPath,
                    MaximumManifestBytes,
                    "Addressables artifact manifest");
                owner = JsonUtility.FromJson<OwnerDocument>(DecodeStrictUtf8(ownerBytes, "Addressables ownership document"));
                manifest = JsonUtility.FromJson<PublicationManifest>(DecodeStrictUtf8(manifestBytes, "Addressables artifact manifest"));
            }
            catch (Exception exception)
            {
                throw new InvalidDataException(
                    $"Addressables publication metadata is invalid: '{publicationDirectory}'.",
                    exception);
            }

            string manifestHash = ComputeSha256(manifestBytes);
            if (owner == null
                || owner.schemaVersion != OwnerSchemaVersion
                || !string.Equals(owner.owner, OwnerIdentifier, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(owner.transactionId)
                || !Guid.TryParseExact(owner.transactionId, "N", out _)
                || !string.Equals(owner.manifestSha256, manifestHash, StringComparison.Ordinal)
                || (!string.IsNullOrEmpty(requiredTransactionId)
                    && !string.Equals(owner.transactionId, requiredTransactionId, StringComparison.Ordinal)))
            {
                throw new InvalidDataException(
                    $"Addressables publication ownership is invalid or belongs to another transaction: '{publicationDirectory}'.");
            }

            if (manifest == null
                || manifest.schemaVersion != 2
                || string.IsNullOrWhiteSpace(manifest.buildTarget)
                || string.IsNullOrWhiteSpace(manifest.contentVersion)
                || manifest.files == null
                || manifest.files.Length == 0)
            {
                throw new InvalidDataException(
                    $"Addressables artifact manifest contract is invalid: '{manifestPath}'.");
            }

            ValidateExactManifestTree(publicationDirectory, manifest);
            return "OWNED:" + ComputeSha256(ownerBytes);
        }

        internal static string DecodeStrictUtf8(byte[] bytes, string label)
        {
            if (bytes.Length >= 3
                && bytes[0] == 0xEF
                && bytes[1] == 0xBB
                && bytes[2] == 0xBF)
            {
                throw new InvalidDataException(label + " must use UTF-8 without BOM.");
            }

            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidDataException(label + " is not valid UTF-8.", exception);
            }
        }

        private static void ValidateStageMarker(
            string publicationDirectory,
            string transactionId)
        {
            string markerPath = Path.Combine(publicationDirectory, OwnerFileName);
            byte[] markerBytes = ReadBoundedFile(
                markerPath,
                MaximumOwnerBytes,
                "Addressables stage ownership marker");
            StageOwnerDocument marker;
            try
            {
                marker = JsonUtility.FromJson<StageOwnerDocument>(
                    DecodeStrictUtf8(markerBytes, "Addressables stage ownership marker"));
            }
            catch (Exception exception)
            {
                throw new InvalidDataException(
                    $"Addressables stage ownership marker is invalid: '{markerPath}'.",
                    exception);
            }

            if (marker == null
                || marker.schemaVersion != OwnerSchemaVersion
                || !string.Equals(marker.owner, StageOwnerIdentifier, StringComparison.Ordinal)
                || !string.Equals(marker.transactionId, transactionId, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(marker.checksum))
            {
                throw new InvalidDataException(
                    $"Addressables stage is not owned by transaction '{transactionId}': '{publicationDirectory}'.");
            }

            string expectedChecksum = marker.checksum;
            marker.checksum = string.Empty;
            string actualChecksum = ComputeSha256(
                new UTF8Encoding(false, true).GetBytes(JsonUtility.ToJson(marker, false)));
            if (!string.Equals(actualChecksum, expectedChecksum, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Addressables stage ownership checksum is invalid: '{markerPath}'.");
            }
        }

        private static void WriteNewFileDurably(string path, byte[] bytes)
        {
            using (var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        internal static bool TryGetAttributes(string path, out FileAttributes attributes)
        {
            try
            {
                attributes = File.GetAttributes(path);
                return true;
            }
            catch (FileNotFoundException)
            {
                attributes = 0;
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                attributes = 0;
                return false;
            }
        }

        internal static void EnsurePathComponentsAreNotReparsePoints(string root, string path)
        {
            string normalizedRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedPath = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.Equals(normalizedRoot, normalizedPath, PathComparison)
                && !BuildPathPolicy.IsStrictDescendant(normalizedRoot, normalizedPath))
            {
                throw new InvalidOperationException(
                    $"Path escaped its approved root: '{normalizedPath}'.");
            }

            string current = normalizedPath;
            while (!string.IsNullOrEmpty(current))
            {
                if (TryGetAttributes(current, out FileAttributes attributes)
                    && (attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(
                        $"Addressables path cannot traverse a symbolic link or reparse point: '{current}'.");
                }

                if (string.Equals(current, normalizedRoot, PathComparison))
                {
                    return;
                }

                string parent = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, PathComparison))
                {
                    break;
                }

                current = parent;
            }

            throw new InvalidOperationException(
                $"Could not prove Addressables path ownership below '{normalizedRoot}': '{normalizedPath}'.");
        }

        internal static void EnsureTreeContainsNoReparsePoints(string root)
        {
            if (!TryGetAttributes(root, out FileAttributes rootAttributes))
            {
                return;
            }

            if ((rootAttributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"Addressables publication root cannot be a reparse point: '{root}'.");
            }

            var pending = new Stack<Tuple<string, int>>();
            pending.Push(Tuple.Create(root, 0));
            int entries = 0;
            while (pending.Count > 0)
            {
                Tuple<string, int> item = pending.Pop();
                if (item.Item2 > MaximumDepth)
                {
                    throw new InvalidOperationException(
                        $"Addressables publication exceeds the maximum directory depth of {MaximumDepth}.");
                }

                foreach (string entry in Directory.EnumerateFileSystemEntries(item.Item1))
                {
                    entries++;
                    if (entries > MaximumEntries)
                    {
                        throw new InvalidOperationException(
                            $"Addressables publication exceeds the {MaximumEntries}-entry safety budget.");
                    }

                    FileAttributes attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidOperationException(
                            $"Addressables publication contains a reparse point: '{entry}'.");
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        pending.Push(Tuple.Create(entry, item.Item2 + 1));
                    }
                }
            }
        }

        internal static void ValidateMappedTreePathBudget(
            string sourceRoot,
            string destinationRoot)
        {
            if (!TryGetAttributes(sourceRoot, out FileAttributes sourceAttributes)
                || (sourceAttributes & (FileAttributes.Directory | FileAttributes.ReparsePoint))
                != FileAttributes.Directory)
            {
                throw new InvalidOperationException(
                    $"Addressables publication source tree is unavailable or unsafe: '{sourceRoot}'.");
            }

            BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                destinationRoot,
                "Addressables publication mapped backup directory");
            var pending = new Stack<Tuple<string, string, int>>();
            pending.Push(Tuple.Create(sourceRoot, destinationRoot, 0));
            int entries = 0;
            while (pending.Count > 0)
            {
                Tuple<string, string, int> item = pending.Pop();
                if (item.Item3 > MaximumDepth)
                {
                    throw new InvalidOperationException(
                        $"Addressables publication exceeds the maximum directory depth of {MaximumDepth}.");
                }

                foreach (string sourceEntry in Directory.EnumerateFileSystemEntries(item.Item1))
                {
                    entries++;
                    if (entries > MaximumEntries)
                    {
                        throw new InvalidOperationException(
                            $"Addressables publication exceeds the {MaximumEntries}-entry safety budget.");
                    }

                    FileAttributes attributes = File.GetAttributes(sourceEntry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidOperationException(
                            $"Addressables publication contains a reparse point: '{sourceEntry}'.");
                    }

                    string mappedEntry = Path.Combine(
                        item.Item2,
                        Path.GetFileName(sourceEntry));
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                            mappedEntry,
                            "Addressables publication mapped backup directory");
                        pending.Push(Tuple.Create(
                            sourceEntry,
                            mappedEntry,
                            item.Item3 + 1));
                    }
                    else
                    {
                        BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                            mappedEntry,
                            "Addressables publication mapped backup file");
                    }
                }
            }
        }

        private static void ValidateExactManifestTree(string root, PublicationManifest manifest)
        {
            if (manifest.files.Length > MaximumEntries)
            {
                throw new InvalidDataException(
                    $"Addressables artifact manifest exceeds the {MaximumEntries}-file safety budget.");
            }

            var allowedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                OwnerFileName,
                ArtifactManifestFileName
            };
            var allowedDirectories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            long totalBytes = 0;
            foreach (PublicationManifestEntry entry in manifest.files)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.path)
                    || string.IsNullOrWhiteSpace(entry.kind)
                    || entry.size < 0
                    || !IsSha256(entry.sha256))
                {
                    throw new InvalidDataException("Addressables artifact manifest contains an invalid entry.");
                }

                string relativePath = NormalizeRelativePath(entry.path);
                if (allowedDirectories.ContainsKey(relativePath)
                    || !allowedFiles.Add(relativePath))
                {
                    throw new InvalidDataException(
                        $"Addressables artifact manifest contains a portable path collision: '{relativePath}'.");
                }

                AddParentDirectories(relativePath, allowedDirectories, allowedFiles);
                string fullPath = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
                if (!BuildPathPolicy.IsStrictDescendant(root, fullPath)
                    || !TryGetAttributes(fullPath, out FileAttributes attributes)
                    || (attributes & FileAttributes.Directory) != 0
                    || (attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new FileNotFoundException(
                        "Addressables owned publication artifact is missing or unsafe.",
                        fullPath);
                }

                var info = new FileInfo(fullPath);
                if (info.Length != entry.size)
                {
                    throw new InvalidDataException(
                        $"Addressables artifact size does not match its manifest: '{relativePath}'.");
                }

                totalBytes = checked(totalBytes + info.Length);
                if (totalBytes > MaximumTotalBytes)
                {
                    throw new InvalidDataException(
                        $"Addressables publication exceeds the {MaximumTotalBytes}-byte safety budget.");
                }

                if (!string.Equals(ComputeSha256(fullPath), entry.sha256, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"Addressables artifact hash does not match its manifest: '{relativePath}'.");
                }
            }

            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                string relativePath = GetRelativePath(root, file);
                if (!allowedFiles.Contains(relativePath))
                {
                    throw new InvalidDataException(
                        $"Addressables owned publication contains an undeclared file: '{relativePath}'.");
                }
            }

            foreach (string directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
            {
                string relativePath = GetRelativePath(root, directory);
                if (!allowedDirectories.ContainsKey(relativePath))
                {
                    throw new InvalidDataException(
                        $"Addressables owned publication contains an undeclared directory: '{relativePath}'.");
                }
            }
        }

        private static string NormalizeRelativePath(string value)
        {
            if (value == null || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Addressables artifact path is not a canonical relative path: '{value}'.");
            }

            string normalized = value.Replace('\\', '/');
            int utf8ByteCount;
            try
            {
                utf8ByteCount = new UTF8Encoding(false, true).GetByteCount(normalized);
            }
            catch (EncoderFallbackException exception)
            {
                throw new InvalidDataException(
                    $"Addressables artifact path contains invalid Unicode: '{value}'.",
                    exception);
            }

            if (normalized.Length == 0
                || normalized.StartsWith("/", StringComparison.Ordinal)
                || normalized.EndsWith("/", StringComparison.Ordinal)
                || normalized.Contains("//")
                || utf8ByteCount > 1024)
            {
                throw new InvalidDataException(
                    $"Addressables artifact path is not a portable relative path: '{value}'.");
            }

            string[] segments = normalized.Split('/');
            if (segments.Length > MaximumDepth)
            {
                throw new InvalidDataException(
                    $"Addressables artifact path exceeds {MaximumDepth} segments: '{value}'.");
            }

            foreach (string segment in segments)
            {
                ValidatePortableSegment(segment, value);
            }

            return normalized;
        }

        private static void ValidatePortableSegment(string segment, string fullPath)
        {
            try
            {
                BuildPathPolicy.ValidatePortableFileName(
                    segment,
                    "Addressables artifact path segment");
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException(
                    $"Addressables artifact path has an unsafe segment: '{fullPath}'.",
                    exception);
            }
        }

        private static void AddParentDirectories(
            string relativePath,
            IDictionary<string, string> directories,
            ISet<string> files)
        {
            int separator = relativePath.LastIndexOf('/');
            while (separator > 0)
            {
                string directory = relativePath.Substring(0, separator);
                if (files.Contains(directory))
                {
                    throw new InvalidDataException(
                        $"Addressables artifact manifest uses a path as both a file and directory: '{directory}'.");
                }

                if (directories.TryGetValue(directory, out string existing))
                {
                    if (!string.Equals(existing, directory, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            $"Addressables artifact manifest contains a directory casing collision: '{existing}' and '{directory}'.");
                    }
                }
                else
                {
                    directories.Add(directory, directory);
                }

                separator = directory.LastIndexOf('/');
            }
        }

        private static string GetRelativePath(string root, string path)
        {
            string normalizedRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedPath = Path.GetFullPath(path);
            if (!BuildPathPolicy.IsStrictDescendant(normalizedRoot, normalizedPath))
            {
                throw new InvalidOperationException(
                    $"Addressables publication entry escaped its root: '{path}'.");
            }

            return normalizedPath.Substring(normalizedRoot.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace('\\', '/');
        }

        private static byte[] ReadBoundedFile(string path, int maximumBytes, string label)
        {
            if (!TryGetAttributes(path, out FileAttributes attributes)
                || (attributes & FileAttributes.Directory) != 0)
            {
                throw new FileNotFoundException(label + " is missing.", path);
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(label + " cannot be a reparse point: '" + path + "'.");
            }

            using (var stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       4096,
                       FileOptions.SequentialScan))
            {
                long length = stream.Length;
                if (length <= 0 || length > maximumBytes)
                {
                    throw new InvalidDataException(
                        $"{label} size is outside its safety budget: {length} bytes.");
                }

                var bytes = new byte[(int)length];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int read = stream.Read(bytes, offset, bytes.Length - offset);
                    if (read <= 0)
                    {
                        throw new EndOfStreamException(
                            $"{label} changed while it was read: '{path}'.");
                    }

                    offset += read;
                }

                if (stream.ReadByte() != -1)
                {
                    throw new IOException(
                        $"{label} grew while it was read: '{path}'.");
                }

                return bytes;
            }
        }

        private static bool IsSha256(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 64)
            {
                return false;
            }

            foreach (char character in value)
            {
                bool digit = character >= '0' && character <= '9';
                bool upper = character >= 'A' && character <= 'F';
                if (!digit && !upper)
                {
                    return false;
                }
            }

            return true;
        }

        private static string ComputeSha256(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(stream));
            }
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(bytes));
            }
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

        [Serializable]
        private sealed class OwnerDocument
        {
            public int schemaVersion;
            public string owner;
            public string transactionId;
            public string manifestSha256;
        }

        [Serializable]
        private sealed class StageOwnerDocument
        {
            public int schemaVersion;
            public string owner;
            public string transactionId;
            public string checksum;
        }

        [Serializable]
        private sealed class PublicationManifest
        {
            public int schemaVersion = 0;
            public string buildTarget = string.Empty;
            public string contentVersion = string.Empty;
            public PublicationManifestEntry[] files = Array.Empty<PublicationManifestEntry>();
        }

        [Serializable]
        private sealed class PublicationManifestEntry
        {
            public string kind = string.Empty;
            public string path = string.Empty;
            public long size = 0;
            public string sha256 = string.Empty;
        }
    }
}
