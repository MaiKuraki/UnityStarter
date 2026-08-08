using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    internal enum HybridCLRGenerationPathMode
    {
        SnapshotFile,
        MirrorDirectory,
        ReplaceDirectory
    }

    internal sealed class HybridCLRGenerationPlan
    {
        internal sealed class Entry
        {
            public Entry(string path, HybridCLRGenerationPathMode mode)
            {
                Path = path;
                Mode = mode;
            }

            public string Path { get; }
            public HybridCLRGenerationPathMode Mode { get; }
        }

        private readonly List<Entry> entries = new List<Entry>();
        private readonly List<string> cleanupDirectories = new List<string>();

        public HybridCLRGenerationPlan(string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                throw new ArgumentException("Unity project root is required.", nameof(projectRoot));
            }

            ProjectRoot = Path.GetFullPath(projectRoot);
            if (!Directory.Exists(ProjectRoot))
            {
                throw new DirectoryNotFoundException(
                    $"Unity project root was not found: '{ProjectRoot}'.");
            }
        }

        public string ProjectRoot { get; }
        public IReadOnlyList<Entry> Entries => entries;
        public IReadOnlyList<string> CleanupDirectories => cleanupDirectories;

        public void AddSnapshotFile(string path)
        {
            Add(path, HybridCLRGenerationPathMode.SnapshotFile);
        }

        public void AddMirrorDirectory(string path)
        {
            Add(path, HybridCLRGenerationPathMode.MirrorDirectory);
        }

        public void AddReplaceDirectory(string path)
        {
            Add(path, HybridCLRGenerationPathMode.ReplaceDirectory);
        }

        public void AddGeneratedAssetFile(string path)
        {
            string file = NormalizeTarget(path);
            string assetsRoot = Path.Combine(ProjectRoot, "Assets");
            if (!BuildPathPolicy.IsStrictDescendant(assetsRoot, file))
            {
                throw new InvalidOperationException(
                    $"HybridCLR generated Asset must remain inside Assets: '{file}'.");
            }

            AddSnapshotFile(file);
            AddSnapshotFile(file + ".meta");

            string directory = Path.GetDirectoryName(file);
            while (!string.IsNullOrEmpty(directory)
                   && BuildPathPolicy.IsStrictDescendant(assetsRoot, directory))
            {
                AddSnapshotFile(directory + ".meta");
                if (!Directory.Exists(directory))
                {
                    AddCleanupDirectory(directory);
                }

                directory = Path.GetDirectoryName(directory);
            }
        }

        private void Add(string path, HybridCLRGenerationPathMode mode)
        {
            string target = NormalizeTarget(path);
            Entry existing = entries.FirstOrDefault(candidate =>
                PathsEqual(candidate.Path, target));
            if (existing != null)
            {
                if (existing.Mode != mode)
                {
                    throw new InvalidOperationException(
                        $"HybridCLR generation target has conflicting protection modes: '{target}'.");
                }

                return;
            }

            for (int index = 0; index < entries.Count; index++)
            {
                Entry candidate = entries[index];
                if (mode != HybridCLRGenerationPathMode.SnapshotFile
                    && BuildPathPolicy.IsStrictDescendant(target, candidate.Path))
                {
                    throw new InvalidOperationException(
                        $"HybridCLR generation directory contains another protected target: '{target}' and '{candidate.Path}'.");
                }

                if (candidate.Mode != HybridCLRGenerationPathMode.SnapshotFile
                    && BuildPathPolicy.IsStrictDescendant(candidate.Path, target))
                {
                    throw new InvalidOperationException(
                        $"HybridCLR generation target is contained by another protected directory: '{target}' and '{candidate.Path}'.");
                }
            }

            entries.Add(new Entry(target, mode));
        }

        private void AddCleanupDirectory(string path)
        {
            string directory = NormalizeTarget(path);
            if (cleanupDirectories.Any(candidate => PathsEqual(candidate, directory)))
            {
                return;
            }

            cleanupDirectories.Add(directory);
        }

        private string NormalizeTarget(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("HybridCLR generation target path is required.", nameof(path));
            }

            string target = Path.GetFullPath(path);
            if (!BuildPathPolicy.IsStrictDescendant(ProjectRoot, target))
            {
                throw new InvalidOperationException(
                    $"HybridCLR generation target must remain inside the Unity project: '{target}'.");
            }

            string stateRoot = Path.Combine(
                ProjectRoot,
                HybridCLRGenerationTransaction.StateRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (PathsEqual(target, stateRoot)
                || BuildPathPolicy.IsStrictDescendant(stateRoot, target)
                || BuildPathPolicy.IsStrictDescendant(target, stateRoot))
            {
                throw new InvalidOperationException(
                    $"HybridCLR generation target overlaps transaction state: '{target}'.");
            }

            return BuildPathPolicy.EnsureWin32MaxPathBudget(
                target,
                "HybridCLR generation target");
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Durable lease around the mutable source/cache surface used by HybridCLR and Obfuz generation.
    /// It deliberately remains separate from final runtime-content publication: generation inputs are
    /// protected before third-party commands run, while the existing output transaction still owns the
    /// Assets that are published to downstream content/player steps.
    /// </summary>
    internal sealed class HybridCLRGenerationTransaction : IDisposable
    {
        internal enum CrashCheckpoint
        {
            AfterBackupMutationBeforeJournal,
            AfterCommittedJournalBeforeCleanup,
            AfterRollbackTargetDisplacedBeforeRestore
        }

        internal sealed class SimulatedProcessCrashException : Exception
        {
            public SimulatedProcessCrashException(CrashCheckpoint checkpoint, string target)
                : base($"Simulated HybridCLR generation crash at '{checkpoint}' for '{target}'.")
            {
            }
        }

        [Serializable]
        private sealed class Journal
        {
            public int formatVersion;
            public long sequence;
            public string transactionId;
            public string phase;
            public string projectRoot;
            public string stateRoot;
            public string scratchRoot;
            public bool touchesAssets;
            public Operation[] operations;
            public string[] cleanupDirectories;
            public string checksum;
        }

        [Serializable]
        private sealed class Operation
        {
            public string target;
            public string backup;
            public string discard;
            public string mode;
            public string state;
            public bool originalExisted;
            public long originalLength;
            public long originalWriteUtcTicks;
            public int originalAttributes;
            public string originalSha256;
        }

        internal const string StateRelativePath = ".buildpipeline/transactions/hybridclr-generation";

        private const int JournalFormatVersion = 1;
        private const int MaximumOperationCount = 32;
        private const long MaximumJournalBytes = 2L * 1024L * 1024L;
        private const string ActiveJournalFileName = "active.json";
        private const string LockFileName = "build.lock";
        private const string TemporaryJournalPrefix = "active.json.tmp-";
        private const string PreparedPhase = "Prepared";
        private const string ActivePhase = "Active";
        private const string RollingBackPhase = "RollingBack";
        private const string RolledBackPhase = "RolledBack";
        private const string CommittedPhase = "Committed";
        private const string PendingState = "Pending";
        private const string BackupPendingState = "BackupPending";
        private const string BackedUpState = "BackedUp";
        private const string AbsentState = "Absent";
        private const string RestorePendingState = "RestorePending";
        private const string RestoredState = "Restored";

        private static readonly UTF8Encoding Utf8WithoutBom = new UTF8Encoding(false);

        private readonly string projectRoot;
        private readonly string stateRoot;
        private readonly string activeJournalPath;
        private readonly Journal journal;
        private FileStream buildLock;
        private bool finished;
        private bool committed;
        private bool disposed;
        private bool preserveForRecovery;
        private bool restoredAssets;

        private HybridCLRGenerationTransaction(
            string projectRoot,
            string stateRoot,
            FileStream buildLock,
            Journal journal)
        {
            this.projectRoot = projectRoot;
            this.stateRoot = stateRoot;
            this.buildLock = buildLock;
            this.journal = journal;
            activeJournalPath = Path.Combine(stateRoot, ActiveJournalFileName);
        }

        internal bool RestoredAssets => restoredAssets;

        internal static HybridCLRGenerationTransaction Begin(HybridCLRGenerationPlan plan)
        {
            return BeginCore(plan, crashPredicate: null);
        }

        internal static HybridCLRGenerationTransaction BeginForTesting(
            HybridCLRGenerationPlan plan,
            Func<CrashCheckpoint, string, bool> crashPredicate)
        {
            if (crashPredicate == null)
            {
                throw new ArgumentNullException(nameof(crashPredicate));
            }

            return BeginCore(plan, crashPredicate);
        }

        private static HybridCLRGenerationTransaction BeginCore(
            HybridCLRGenerationPlan plan,
            Func<CrashCheckpoint, string, bool> crashPredicate)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (plan.Entries.Count == 0)
            {
                throw new InvalidOperationException(
                    "HybridCLR generation transaction requires at least one protected path.");
            }

            if (plan.Entries.Count > MaximumOperationCount)
            {
                throw new InvalidOperationException(
                    $"HybridCLR generation transaction supports at most {MaximumOperationCount} protected paths.");
            }

            string project = Path.GetFullPath(plan.ProjectRoot);
            string state = PrepareStateRoot(project);
            FileStream outputLock = AcquireProjectLock(state);
            HybridCLRGenerationTransaction transaction = null;
            try
            {
                string journalPath = Path.Combine(state, ActiveJournalFileName);
                if (File.Exists(journalPath))
                {
                    throw new InvalidOperationException(
                        $"HybridCLR generation recovery is required before a new build: '{journalPath}'.");
                }

                CleanupOrphanJournalTemporaries(state);
                EnsureNoDetachedState(state);
                Journal value = CreateJournal(project, state, plan);
                PersistJournal(value, journalPath, createNew: true);
                Directory.CreateDirectory(value.scratchRoot);

                transaction = new HybridCLRGenerationTransaction(
                    project,
                    state,
                    outputLock,
                    value);
                outputLock = null;
                transaction.PrepareOperations(crashPredicate);
                value.phase = ActivePhase;
                transaction.Persist();
                return transaction;
            }
            catch (SimulatedProcessCrashException)
            {
                if (transaction != null)
                {
                    transaction.preserveForRecovery = true;
                    transaction.ReleaseLock();
                    transaction.disposed = true;
                }

                throw;
            }
            catch (Exception preparationFailure)
            {
                if (transaction != null)
                {
                    try
                    {
                        transaction.Rollback(crashPredicate: null);
                    }
                    catch (Exception rollbackFailure)
                    {
                        transaction.preserveForRecovery = true;
                        transaction.ReleaseLock();
                        transaction.disposed = true;
                        throw new AggregateException(
                            "HybridCLR generation preparation failed and durable rollback did not complete.",
                            preparationFailure,
                            rollbackFailure);
                    }

                    transaction.ReleaseLock();
                    transaction.disposed = true;
                    CleanupEmptyStateRoot(state);
                }

                throw;
            }
            finally
            {
                outputLock?.Dispose();
            }
        }

        internal void ValidateActive()
        {
            ThrowIfDisposed();
            if (finished || committed
                || !string.Equals(journal.phase, ActivePhase, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "HybridCLR generation lease is not active.");
            }

            if (!File.Exists(activeJournalPath))
            {
                preserveForRecovery = true;
                throw new IOException(
                    "HybridCLR generation journal disappeared while the lease was active.");
            }
        }

        internal void Commit()
        {
            CommitCore(requireTerminalDecision: true, crashPredicate: null);
        }

        internal void CommitForTesting(
            Func<CrashCheckpoint, string, bool> crashPredicate = null)
        {
            CommitCore(requireTerminalDecision: false, crashPredicate: crashPredicate);
        }

        private void CommitCore(
            bool requireTerminalDecision,
            Func<CrashCheckpoint, string, bool> crashPredicate)
        {
            ValidateActive();
            if (requireTerminalDecision
                && GetTerminalDecision(projectRoot) != BuildPublicationDecision.Commit)
            {
                throw new InvalidOperationException(
                    "HybridCLR generation inputs cannot commit without the shared terminal commit decision.");
            }

            try
            {
                journal.phase = CommittedPhase;
                Persist();
                committed = true;
                TriggerCrash(
                    crashPredicate,
                    CrashCheckpoint.AfterCommittedJournalBeforeCleanup,
                    string.Empty);
                CleanupTerminalState(journal, activeJournalPath, stateRoot);
                finished = true;
            }
            catch (SimulatedProcessCrashException)
            {
                preserveForRecovery = true;
                throw;
            }
            catch (Exception exception)
            {
                preserveForRecovery = true;
                throw new IOException(
                    $"HybridCLR generation committed, but durable cleanup did not complete. Recovery state remains at '{activeJournalPath}'.",
                    exception);
            }
        }

        internal void AbandonForTesting()
        {
            ThrowIfDisposed();
            preserveForRecovery = true;
            disposed = true;
            ReleaseLock();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            Exception failure = null;
            try
            {
                if (!finished && !preserveForRecovery)
                {
                    if (GetTerminalDecision(projectRoot) == BuildPublicationDecision.Commit)
                    {
                        CommitCore(requireTerminalDecision: false, crashPredicate: null);
                    }
                    else
                    {
                        Rollback(crashPredicate: null);
                    }
                }
            }
            catch (Exception exception)
            {
                preserveForRecovery = true;
                failure = exception;
            }
            finally
            {
                disposed = true;
                ReleaseLock();
                if (!preserveForRecovery)
                {
                    CleanupEmptyStateRoot(stateRoot);
                }
            }

            if (failure != null)
            {
                throw failure;
            }
        }

        internal static bool RecoverPending(string projectRoot, out bool assetsChanged)
        {
            assetsChanged = false;
            string project = NormalizeProjectRoot(projectRoot);
            string state = GetStateRoot(project);
            if (!Directory.Exists(state))
            {
                return false;
            }

            FileStream recoveryLock = AcquireProjectLock(state);
            HybridCLRGenerationTransaction transaction = null;
            try
            {
                string journalPath = Path.Combine(state, ActiveJournalFileName);
                if (!File.Exists(journalPath))
                {
                    CleanupOrphanJournalTemporaries(state);
                    EnsureNoDetachedState(state);
                    recoveryLock.Dispose();
                    recoveryLock = null;
                    CleanupEmptyStateRoot(state);
                    return false;
                }

                Journal value = ReadJournal(journalPath);
                ValidateJournal(value, project, state);
                transaction = new HybridCLRGenerationTransaction(
                    project,
                    state,
                    recoveryLock,
                    value);
                recoveryLock = null;

                BuildPublicationDecision decision = GetTerminalDecision(project);
                if (string.Equals(value.phase, CommittedPhase, StringComparison.Ordinal)
                    || decision == BuildPublicationDecision.Commit)
                {
                    if (!string.Equals(value.phase, CommittedPhase, StringComparison.Ordinal))
                    {
                        value.phase = CommittedPhase;
                        transaction.Persist();
                    }

                    CleanupTerminalState(value, journalPath, state);
                    transaction.committed = true;
                    transaction.finished = true;
                }
                else
                {
                    transaction.Rollback(crashPredicate: null);
                    assetsChanged = value.touchesAssets;
                }

                transaction.ReleaseLock();
                transaction.disposed = true;
                CleanupEmptyStateRoot(state);
                return true;
            }
            catch
            {
                if (transaction != null)
                {
                    transaction.preserveForRecovery = true;
                    transaction.ReleaseLock();
                    transaction.disposed = true;
                }

                throw;
            }
            finally
            {
                recoveryLock?.Dispose();
            }
        }

        internal static string GetActiveJournalPathForTesting(string projectRoot)
        {
            return Path.Combine(GetStateRoot(NormalizeProjectRoot(projectRoot)), ActiveJournalFileName);
        }

        private void PrepareOperations(
            Func<CrashCheckpoint, string, bool> crashPredicate)
        {
            for (int index = 0; index < journal.operations.Length; index++)
            {
                Operation operation = journal.operations[index];
                if (!operation.originalExisted)
                {
                    operation.state = AbsentState;
                    Persist();
                    continue;
                }

                operation.state = BackupPendingState;
                Persist();
                Directory.CreateDirectory(Path.GetDirectoryName(operation.backup));
                if (IsFileOperation(operation))
                {
                    File.Copy(operation.target, operation.backup, overwrite: false);
                    ApplyOriginalFileMetadata(operation, operation.backup);
                    EnsureOriginalFileIdentity(operation, operation.backup, "generation backup");
                }
                else
                {
                    Directory.Move(operation.target, operation.backup);
                    if (string.Equals(
                            operation.mode,
                            HybridCLRGenerationPathMode.MirrorDirectory.ToString(),
                            StringComparison.Ordinal))
                    {
                        CopyDirectory(operation.backup, operation.target);
                    }
                }

                TriggerCrash(
                    crashPredicate,
                    CrashCheckpoint.AfterBackupMutationBeforeJournal,
                    operation.target);
                operation.state = BackedUpState;
                Persist();
            }
        }

        private void Rollback(
            Func<CrashCheckpoint, string, bool> crashPredicate)
        {
            if (finished)
            {
                return;
            }

            journal.phase = RollingBackPhase;
            Persist();
            for (int index = journal.operations.Length - 1; index >= 0; index--)
            {
                Operation operation = journal.operations[index];
                operation.state = RestorePendingState;
                Persist();
                RestoreOperation(operation, crashPredicate);
                operation.state = RestoredState;
                Persist();
            }

            CleanupGeneratedDirectories(journal);
            ValidateOriginalState(journal);
            journal.phase = RolledBackPhase;
            Persist();
            restoredAssets = journal.touchesAssets;
            CleanupTerminalState(journal, activeJournalPath, stateRoot);
            finished = true;
        }

        private void RestoreOperation(
            Operation operation,
            Func<CrashCheckpoint, string, bool> crashPredicate)
        {
            if (!operation.originalExisted)
            {
                DisplaceCurrentTarget(operation);
                return;
            }

            bool backupExists = IsFileOperation(operation)
                ? File.Exists(operation.backup)
                : Directory.Exists(operation.backup);
            if (IsFileOperation(operation)
                && File.Exists(operation.target)
                && MatchesOriginalFile(operation, operation.target))
            {
                // The original file may still be intact when preparation stopped during a
                // non-atomic copy. Prefer the proven original and leave any partial scratch
                // backup for controlled terminal cleanup.
                ApplyOriginalFileMetadata(operation, operation.target);
                return;
            }

            if (!backupExists)
            {
                if (!IsFileOperation(operation)
                    && Directory.Exists(operation.target))
                {
                    // Directory backups are restored through an atomic move. If the backup is gone
                    // and the target is present, recovery previously completed that move.
                    return;
                }

                throw new IOException(
                    $"HybridCLR generation backup is missing and the original target cannot be proven restored: '{operation.target}'.");
            }

            if (IsFileOperation(operation))
            {
                // Never displace the current target until the scratch backup is proven complete.
                // A process can terminate while File.Copy is still producing this file.
                EnsureOriginalFileIdentity(operation, operation.backup, "generation backup");
            }

            DisplaceCurrentTarget(operation);
            TriggerCrash(
                crashPredicate,
                CrashCheckpoint.AfterRollbackTargetDisplacedBeforeRestore,
                operation.target);
            Directory.CreateDirectory(Path.GetDirectoryName(operation.target));
            if (IsFileOperation(operation))
            {
                File.Move(operation.backup, operation.target);
                EnsureOriginalFileIdentity(operation, operation.target, "restored generation file");
                ApplyOriginalFileMetadata(operation, operation.target);
            }
            else
            {
                Directory.Move(operation.backup, operation.target);
            }
        }

        private void DisplaceCurrentTarget(Operation operation)
        {
            string discard = GetAvailableDiscardPath(operation.discard);
            if (IsFileOperation(operation))
            {
                if (Directory.Exists(operation.target))
                {
                    throw new IOException(
                        $"HybridCLR generation file target became a directory; recovery refused to delete it: '{operation.target}'.");
                }

                if (File.Exists(operation.target))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(discard));
                    File.Move(operation.target, discard);
                }

                return;
            }

            if (File.Exists(operation.target))
            {
                throw new IOException(
                    $"HybridCLR generation directory target became a file; recovery refused to delete it: '{operation.target}'.");
            }

            if (Directory.Exists(operation.target))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(discard));
                Directory.Move(operation.target, discard);
            }
        }

        private static string GetAvailableDiscardPath(string preferred)
        {
            if (!File.Exists(preferred) && !Directory.Exists(preferred))
            {
                return preferred;
            }

            for (int index = 1; index <= 64; index++)
            {
                string candidate = preferred + "-" + index.ToString("D2", CultureInfo.InvariantCulture);
                if (!File.Exists(candidate) && !Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new IOException(
                $"HybridCLR generation recovery exceeded the discard retry limit: '{preferred}'.");
        }

        private static void CleanupGeneratedDirectories(Journal value)
        {
            string[] directories = value.cleanupDirectories ?? Array.Empty<string>();
            Array.Sort(directories, (left, right) => right.Length.CompareTo(left.Length));
            for (int index = 0; index < directories.Length; index++)
            {
                string directory = directories[index];
                if (!Directory.Exists(directory))
                {
                    continue;
                }

                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException(
                        $"HybridCLR generated directory became a reparse point; recovery refused to remove it: '{directory}'.");
                }

                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory, recursive: false);
                }
            }
        }

        private static void ValidateOriginalState(Journal value)
        {
            for (int index = 0; index < value.operations.Length; index++)
            {
                Operation operation = value.operations[index];
                if (!operation.originalExisted)
                {
                    if (File.Exists(operation.target) || Directory.Exists(operation.target))
                    {
                        throw new IOException(
                            $"HybridCLR generation rollback left a newly-created target behind: '{operation.target}'.");
                    }

                    continue;
                }

                if (IsFileOperation(operation))
                {
                    EnsureOriginalFileIdentity(operation, operation.target, "rollback verification");
                }
                else if (!Directory.Exists(operation.target))
                {
                    throw new DirectoryNotFoundException(
                        $"HybridCLR generation rollback did not restore directory: '{operation.target}'.");
                }
            }
        }

        private static Journal CreateJournal(
            string projectRoot,
            string stateRoot,
            HybridCLRGenerationPlan plan)
        {
            string transactionId = Guid.NewGuid().ToString("N");
            string scratchRoot = Path.Combine(stateRoot, transactionId);
            var operations = new Operation[plan.Entries.Count];
            bool touchesAssets = false;
            string assetsRoot = Path.Combine(projectRoot, "Assets");
            for (int index = 0; index < plan.Entries.Count; index++)
            {
                HybridCLRGenerationPlan.Entry entry = plan.Entries[index];
                ValidateConcreteTarget(projectRoot, stateRoot, entry.Path, entry.Mode);
                bool isFile = entry.Mode == HybridCLRGenerationPathMode.SnapshotFile;
                bool oppositeExists = isFile
                    ? Directory.Exists(entry.Path)
                    : File.Exists(entry.Path);
                if (oppositeExists)
                {
                    throw new InvalidOperationException(
                        $"HybridCLR generation target has the wrong filesystem kind: '{entry.Path}'.");
                }

                bool exists = isFile ? File.Exists(entry.Path) : Directory.Exists(entry.Path);
                var operation = new Operation
                {
                    target = entry.Path,
                    backup = Path.Combine(scratchRoot, "backup-" + index.ToString("D3", CultureInfo.InvariantCulture)),
                    discard = Path.Combine(scratchRoot, "discard-" + index.ToString("D3", CultureInfo.InvariantCulture)),
                    mode = entry.Mode.ToString(),
                    state = PendingState,
                    originalExisted = exists,
                    originalSha256 = string.Empty
                };
                if (isFile && exists)
                {
                    FileInfo info = new FileInfo(entry.Path);
                    operation.originalLength = info.Length;
                    operation.originalWriteUtcTicks = info.LastWriteTimeUtc.Ticks;
                    operation.originalAttributes = (int)info.Attributes;
                    operation.originalSha256 = ComputeFileSha256(entry.Path);
                }

                operations[index] = operation;
                touchesAssets |= BuildPathPolicy.IsStrictDescendant(assetsRoot, entry.Path);
            }

            string[] cleanup = plan.CleanupDirectories
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            for (int index = 0; index < cleanup.Length; index++)
            {
                if (!BuildPathPolicy.IsStrictDescendant(assetsRoot, cleanup[index]))
                {
                    throw new InvalidOperationException(
                        $"HybridCLR generated-directory cleanup target must remain inside Assets: '{cleanup[index]}'.");
                }
            }

            return new Journal
            {
                formatVersion = JournalFormatVersion,
                sequence = 0,
                transactionId = transactionId,
                phase = PreparedPhase,
                projectRoot = projectRoot,
                stateRoot = stateRoot,
                scratchRoot = scratchRoot,
                touchesAssets = touchesAssets,
                operations = operations,
                cleanupDirectories = cleanup,
                checksum = string.Empty
            };
        }

        private static void ValidateConcreteTarget(
            string projectRoot,
            string stateRoot,
            string target,
            HybridCLRGenerationPathMode mode)
        {
            string full = Path.GetFullPath(target);
            if (!BuildPathPolicy.IsStrictDescendant(projectRoot, full))
            {
                throw new InvalidOperationException(
                    $"HybridCLR generation target escaped the Unity project: '{full}'.");
            }

            if (PathsEqual(full, stateRoot)
                || BuildPathPolicy.IsStrictDescendant(stateRoot, full)
                || BuildPathPolicy.IsStrictDescendant(full, stateRoot))
            {
                throw new InvalidOperationException(
                    $"HybridCLR generation target overlaps transaction state: '{full}'.");
            }

            if ((File.Exists(full) || Directory.Exists(full))
                && (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"HybridCLR generation target cannot be a reparse point: '{full}'.");
            }

            BuildPathPolicy.EnsureWin32MaxPathBudget(
                full,
                mode == HybridCLRGenerationPathMode.SnapshotFile
                    ? "HybridCLR protected generation file"
                    : "HybridCLR protected generation directory");
        }

        private static void ValidateJournal(
            Journal value,
            string projectRoot,
            string stateRoot)
        {
            if (value == null
                || value.formatVersion != JournalFormatVersion
                || value.sequence <= 0
                || string.IsNullOrWhiteSpace(value.transactionId)
                || value.transactionId.Length != 32
                || !value.transactionId.All(Uri.IsHexDigit))
            {
                throw new InvalidDataException(
                    "HybridCLR generation journal header is invalid.");
            }

            if (!IsKnownPhase(value.phase))
            {
                throw new InvalidDataException(
                    $"HybridCLR generation journal phase is invalid: '{value.phase}'.");
            }

            if (!PathsEqual(value.projectRoot, projectRoot)
                || !PathsEqual(value.stateRoot, stateRoot))
            {
                throw new InvalidDataException(
                    "HybridCLR generation journal belongs to a different project or state root.");
            }

            string expectedScratch = Path.Combine(stateRoot, value.transactionId);
            if (!PathsEqual(value.scratchRoot, expectedScratch)
                || value.operations == null
                || value.operations.Length == 0
                || value.operations.Length > MaximumOperationCount)
            {
                throw new InvalidDataException(
                    "HybridCLR generation journal scratch root or operation count is invalid.");
            }

            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < value.operations.Length; index++)
            {
                Operation operation = value.operations[index]
                    ?? throw new InvalidDataException(
                        $"HybridCLR generation journal operation {index} is null.");
                if (!Enum.TryParse(operation.mode, out HybridCLRGenerationPathMode mode)
                    || !Enum.IsDefined(typeof(HybridCLRGenerationPathMode), mode)
                    || !string.Equals(operation.mode, mode.ToString(), StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"HybridCLR generation journal operation mode is invalid: '{operation.mode}'.");
                }


                ValidateOperationState(value.phase, operation, index);

                ValidateConcreteTarget(projectRoot, stateRoot, operation.target, mode);
                if (!targets.Add(Path.GetFullPath(operation.target)))
                {
                    throw new InvalidDataException(
                        $"HybridCLR generation journal contains duplicate target: '{operation.target}'.");
                }

                string expectedBackup = Path.Combine(
                    expectedScratch,
                    "backup-" + index.ToString("D3", CultureInfo.InvariantCulture));
                string expectedDiscard = Path.Combine(
                    expectedScratch,
                    "discard-" + index.ToString("D3", CultureInfo.InvariantCulture));
                if (!PathsEqual(operation.backup, expectedBackup)
                    || !PathsEqual(operation.discard, expectedDiscard))
                {
                    throw new InvalidDataException(
                        $"HybridCLR generation journal operation {index} has invalid scratch paths.");
                }
            }

            string assetsRoot = Path.Combine(projectRoot, "Assets");
            foreach (string directory in value.cleanupDirectories ?? Array.Empty<string>())
            {
                if (!BuildPathPolicy.IsStrictDescendant(assetsRoot, directory))
                {
                    throw new InvalidDataException(
                        $"HybridCLR generation cleanup directory escaped Assets: '{directory}'.");
                }
            }
        }

        private static Journal ReadJournal(string path)
        {
            FileInfo info = new FileInfo(path);
            if (!info.Exists || info.Length <= 0 || info.Length > MaximumJournalBytes)
            {
                throw new InvalidDataException(
                    $"HybridCLR generation journal size is invalid: '{path}'.");
            }

            string json = File.ReadAllText(path, Encoding.UTF8);
            Journal value;
            try
            {
                value = JsonUtility.FromJson<Journal>(json);
            }
            catch (Exception exception)
            {
                throw new InvalidDataException(
                    $"HybridCLR generation journal is not valid JSON: '{path}'.",
                    exception);
            }

            if (value == null || string.IsNullOrWhiteSpace(value.checksum))
            {
                throw new InvalidDataException(
                    $"HybridCLR generation journal checksum is missing: '{path}'.");
            }

            string expected = value.checksum;
            value.checksum = string.Empty;
            string actual = ComputeSha256(Utf8WithoutBom.GetBytes(JsonUtility.ToJson(value, false)));
            value.checksum = expected;
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"HybridCLR generation journal checksum mismatch: '{path}'.");
            }

            return value;
        }

        private void Persist()
        {
            PersistJournal(journal, activeJournalPath, createNew: false);
        }

        private static void PersistJournal(
            Journal value,
            string journalPath,
            bool createNew)
        {
            value.sequence++;
            value.checksum = string.Empty;
            value.checksum = ComputeSha256(
                Utf8WithoutBom.GetBytes(JsonUtility.ToJson(value, false)));
            byte[] bytes = Utf8WithoutBom.GetBytes(JsonUtility.ToJson(value, true));
            if (bytes.LongLength > MaximumJournalBytes)
            {
                throw new InvalidDataException(
                    "HybridCLR generation journal exceeded its maximum size.");
            }

            if (createNew && File.Exists(journalPath))
            {
                throw new IOException(
                    $"HybridCLR generation journal already exists: '{journalPath}'.");
            }

            if (!createNew && !File.Exists(journalPath))
            {
                throw new FileNotFoundException(
                    "HybridCLR generation journal disappeared before a durable update.",
                    journalPath);
            }

            string temporary = Path.Combine(
                Path.GetDirectoryName(journalPath),
                TemporaryJournalPrefix
                + value.transactionId
                + "-"
                + value.sequence.ToString("D6", CultureInfo.InvariantCulture));
            using (var stream = new FileStream(
                       temporary,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }

            if (createNew)
            {
                File.Move(temporary, journalPath);
            }
            else
            {
                File.Replace(temporary, journalPath, null);
            }
        }

        private static void CleanupTerminalState(
            Journal value,
            string journalPath,
            string stateRoot)
        {
            if (Directory.Exists(value.scratchRoot))
            {
                EnsureScratchPath(stateRoot, value.scratchRoot, value.transactionId);
                DeleteScratchTree(value.scratchRoot);
            }

            CleanupOrphanJournalTemporaries(stateRoot);
            if (File.Exists(journalPath))
            {
                File.Delete(journalPath);
            }
        }

        private static void EnsureScratchPath(
            string stateRoot,
            string scratchRoot,
            string transactionId)
        {
            string expected = Path.Combine(stateRoot, transactionId);
            if (!PathsEqual(expected, scratchRoot)
                || !BuildPathPolicy.IsStrictDescendant(stateRoot, scratchRoot))
            {
                throw new InvalidOperationException(
                    $"HybridCLR generation scratch path is unsafe: '{scratchRoot}'.");
            }
        }

        private static void DeleteScratchTree(string root)
        {
            if (!Directory.Exists(root))
            {
                return;
            }

            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException(
                    $"HybridCLR generation scratch root became a reparse point: '{root}'.");
            }

            foreach (string file in Directory.GetFiles(root))
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }

            foreach (string directory in Directory.GetDirectories(root))
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                {
                    Directory.Delete(directory, recursive: false);
                }
                else
                {
                    DeleteScratchTree(directory);
                }
            }

            Directory.Delete(root, recursive: false);
        }

        private static string PrepareStateRoot(string projectRoot)
        {
            string stateRoot = GetStateRoot(projectRoot);
            BuildPathPolicy.EnsureWin32MaxDirectoryPathBudget(
                stateRoot,
                "HybridCLR generation transaction state root");
            if (Directory.Exists(stateRoot)
                && (File.GetAttributes(stateRoot) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"HybridCLR generation transaction state root cannot be a reparse point: '{stateRoot}'.");
            }

            Directory.CreateDirectory(stateRoot);
            return stateRoot;
        }

        private static string GetStateRoot(string projectRoot)
        {
            return Path.GetFullPath(Path.Combine(
                projectRoot,
                StateRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static FileStream AcquireProjectLock(string stateRoot)
        {
            Directory.CreateDirectory(stateRoot);
            string lockPath = Path.Combine(stateRoot, LockFileName);
            if (Directory.Exists(lockPath)
                || (File.Exists(lockPath)
                    && (File.GetAttributes(lockPath) & FileAttributes.ReparsePoint) != 0))
            {
                throw new InvalidOperationException(
                    $"HybridCLR generation lock path is unsafe: '{lockPath}'.");
            }

            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    1,
                    FileOptions.WriteThrough);
            }
            catch (IOException exception)
            {
                throw new InvalidOperationException(
                    "Another HybridCLR generation transaction is active in this Unity project.",
                    exception);
            }
        }

        private void ReleaseLock()
        {
            buildLock?.Dispose();
            buildLock = null;
        }

        private static void CleanupEmptyStateRoot(string stateRoot)
        {
            if (!Directory.Exists(stateRoot))
            {
                return;
            }

            string lockPath = Path.Combine(stateRoot, LockFileName);
            if (File.Exists(lockPath))
            {
                try
                {
                    File.Delete(lockPath);
                }
                catch (IOException)
                {
                    return;
                }
                catch (UnauthorizedAccessException)
                {
                    return;
                }
            }

            if (!Directory.EnumerateFileSystemEntries(stateRoot).Any())
            {
                Directory.Delete(stateRoot, recursive: false);
                string transactionsRoot = Path.GetDirectoryName(stateRoot);
                if (!string.IsNullOrEmpty(transactionsRoot)
                    && Directory.Exists(transactionsRoot)
                    && !Directory.EnumerateFileSystemEntries(transactionsRoot).Any())
                {
                    Directory.Delete(transactionsRoot, recursive: false);
                }
            }
        }

        private static void CleanupOrphanJournalTemporaries(string stateRoot)
        {
            if (!Directory.Exists(stateRoot))
            {
                return;
            }

            string[] files = Directory.GetFiles(
                stateRoot,
                TemporaryJournalPrefix + "*",
                SearchOption.TopDirectoryOnly);
            if (files.Length > 64)
            {
                throw new InvalidDataException(
                    "HybridCLR generation state contains too many temporary journals.");
            }

            for (int index = 0; index < files.Length; index++)
            {
                if ((File.GetAttributes(files[index]) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException(
                        $"HybridCLR generation temporary journal is a reparse point: '{files[index]}'.");
                }

                File.Delete(files[index]);
            }
        }

        private static void EnsureNoDetachedState(string stateRoot)
        {
            if (!Directory.Exists(stateRoot))
            {
                return;
            }

            string lockPath = Path.Combine(stateRoot, LockFileName);
            foreach (string entry in Directory.EnumerateFileSystemEntries(stateRoot))
            {
                if (PathsEqual(entry, lockPath))
                {
                    continue;
                }

                throw new InvalidDataException(
                    "HybridCLR generation state contains detached recovery evidence without an active journal. " +
                    $"Refusing to start or discard it automatically: '{entry}'.");
            }
        }

        private static void CopyDirectory(string source, string destination)
        {
            if (!Directory.Exists(source))
            {
                throw new DirectoryNotFoundException(
                    $"HybridCLR generation directory backup was not found: '{source}'.");
            }

            if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException(
                    $"HybridCLR generation directory backup is a reparse point: '{source}'.");
            }

            Directory.CreateDirectory(destination);
            foreach (string directory in Directory.GetDirectories(source))
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException(
                        $"HybridCLR generation directory contains a reparse point: '{directory}'.");
                }

                CopyDirectory(
                    directory,
                    Path.Combine(destination, Path.GetFileName(directory)));
            }

            foreach (string file in Directory.GetFiles(source))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException(
                        $"HybridCLR generation directory contains a reparse-point file: '{file}'.");
                }

                string target = Path.Combine(destination, Path.GetFileName(file));
                File.Copy(file, target, overwrite: false);
                File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(file));
                // The mirror only preserves the package's pre-existing read surface while the
                // exact original remains in the moved backup. Do not propagate ReadOnly into a
                // directory that the generator must overwrite.
                File.SetAttributes(target, FileAttributes.Normal);
            }
        }

        private static void EnsureOriginalFileIdentity(
            Operation operation,
            string path,
            string description)
        {
            if (!File.Exists(path) || Directory.Exists(path))
            {
                throw new FileNotFoundException(
                    $"HybridCLR {description} is missing.",
                    path);
            }

            var info = new FileInfo(path);
            string sha256 = ComputeFileSha256(path);
            if (info.Length != operation.originalLength
                || !string.Equals(
                    sha256,
                    operation.originalSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(
                    $"HybridCLR {description} does not match the pre-generation file: '{path}'.");
            }
        }

        private static bool MatchesOriginalFile(Operation operation, string path)
        {
            if (!File.Exists(path) || Directory.Exists(path))
            {
                return false;
            }

            var info = new FileInfo(path);
            return info.Length == operation.originalLength
                   && string.Equals(
                       ComputeFileSha256(path),
                       operation.originalSha256,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsKnownPhase(string phase)
        {
            return string.Equals(phase, PreparedPhase, StringComparison.Ordinal)
                   || string.Equals(phase, ActivePhase, StringComparison.Ordinal)
                   || string.Equals(phase, RollingBackPhase, StringComparison.Ordinal)
                   || string.Equals(phase, RolledBackPhase, StringComparison.Ordinal)
                   || string.Equals(phase, CommittedPhase, StringComparison.Ordinal);
        }

        private static void ValidateOperationState(
            string phase,
            Operation operation,
            int index)
        {
            bool knownState = string.Equals(operation.state, PendingState, StringComparison.Ordinal)
                              || string.Equals(operation.state, BackupPendingState, StringComparison.Ordinal)
                              || string.Equals(operation.state, BackedUpState, StringComparison.Ordinal)
                              || string.Equals(operation.state, AbsentState, StringComparison.Ordinal)
                              || string.Equals(operation.state, RestorePendingState, StringComparison.Ordinal)
                              || string.Equals(operation.state, RestoredState, StringComparison.Ordinal);
            if (!knownState)
            {
                throw new InvalidDataException(
                    $"HybridCLR generation journal operation {index} has invalid state '{operation.state}'.");
            }

            if (string.Equals(operation.state, AbsentState, StringComparison.Ordinal)
                && operation.originalExisted)
            {
                throw new InvalidDataException(
                    $"HybridCLR generation journal operation {index} marks an existing target as absent.");
            }

            if ((string.Equals(operation.state, BackupPendingState, StringComparison.Ordinal)
                 || string.Equals(operation.state, BackedUpState, StringComparison.Ordinal))
                && !operation.originalExisted)
            {
                throw new InvalidDataException(
                    $"HybridCLR generation journal operation {index} backs up a target that did not exist.");
            }

            if (string.Equals(phase, ActivePhase, StringComparison.Ordinal)
                || string.Equals(phase, CommittedPhase, StringComparison.Ordinal))
            {
                string expected = operation.originalExisted ? BackedUpState : AbsentState;
                if (!string.Equals(operation.state, expected, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"HybridCLR generation journal operation {index} is incomplete for phase '{phase}'.");
                }
            }
            else if (string.Equals(phase, RolledBackPhase, StringComparison.Ordinal)
                     && !string.Equals(operation.state, RestoredState, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"HybridCLR generation journal operation {index} is incomplete for rolled-back phase.");
            }
        }

        private static void ApplyOriginalFileMetadata(Operation operation, string path)
        {
            File.SetLastWriteTimeUtc(path, new DateTime(operation.originalWriteUtcTicks, DateTimeKind.Utc));
            File.SetAttributes(path, (FileAttributes)operation.originalAttributes);
        }

        private static bool IsFileOperation(Operation operation)
        {
            return string.Equals(
                operation.mode,
                HybridCLRGenerationPathMode.SnapshotFile.ToString(),
                StringComparison.Ordinal);
        }

        private static string ComputeFileSha256(string path)
        {
            using (FileStream stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read))
            using (SHA256 sha256 = SHA256.Create())
            {
                return ToHex(sha256.ComputeHash(stream));
            }
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                return ToHex(sha256.ComputeHash(bytes));
            }
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            for (int index = 0; index < bytes.Length; index++)
            {
                builder.Append(bytes[index].ToString("x2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        private static BuildPublicationDecision GetTerminalDecision(string projectRoot)
        {
            return BuildPublicationBarrier.GetDecision(
                projectRoot,
                HybridCLROutputTransaction.PublicationId,
                HybridCLROutputTransaction.StateRelativePath);
        }

        private static void TriggerCrash(
            Func<CrashCheckpoint, string, bool> crashPredicate,
            CrashCheckpoint checkpoint,
            string target)
        {
            if (crashPredicate != null && crashPredicate(checkpoint, target))
            {
                throw new SimulatedProcessCrashException(checkpoint, target);
            }
        }

        private static string NormalizeProjectRoot(string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                throw new ArgumentException("Unity project root is required.", nameof(projectRoot));
            }

            string project = Path.GetFullPath(projectRoot);
            if (!Directory.Exists(project))
            {
                throw new DirectoryNotFoundException(
                    $"Unity project root was not found: '{project}'.");
            }

            return project;
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(HybridCLRGenerationTransaction));
            }
        }
    }
}
