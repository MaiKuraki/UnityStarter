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

            Entry caseAlias = entries.FirstOrDefault(candidate =>
                PortablePathsEqual(candidate.Path, target));
            if (caseAlias != null)
            {
                throw new InvalidOperationException(
                    "HybridCLR generation targets cannot differ only by path casing because " +
                    $"the transaction must remain portable across filesystems: '{caseAlias.Path}' and '{target}'.");
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
                Path.DirectorySeparatorChar == '\\'
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal);
        }

        private static bool PortablePathsEqual(string left, string right)
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
            AfterRollbackTargetDisplacedBeforeRestore,
            AfterSuspendedTargetDisplacedBeforeRestore,
            AfterResumeOriginalDisplacedBeforeGeneratedRestore
        }

        private sealed class SourceQualificationSuspension : IDisposable
        {
            private HybridCLRGenerationTransaction owner;
            private readonly Func<CrashCheckpoint, string, bool> crashPredicate;

            internal SourceQualificationSuspension(
                HybridCLRGenerationTransaction owner,
                Func<CrashCheckpoint, string, bool> crashPredicate)
            {
                this.owner = owner;
                this.crashPredicate = crashPredicate;
            }

            public void Dispose()
            {
                HybridCLRGenerationTransaction current = owner;
                owner = null;
                current?.ResumeAfterSourceQualification(crashPredicate);
            }
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
            public string documentType;
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
            public PathIdentity originalIdentity;
            public bool generatedIdentityCaptured;
            public PathIdentity generatedIdentity;
        }

        [Serializable]
        private sealed class PathIdentity
        {
            public string kind;
            public long length;
            public long writeUtcTicks;
            public int attributes;
            public string sha256;
            public PathIdentityEntry[] entries;
        }

        [Serializable]
        private sealed class PathIdentityEntry
        {
            public string path;
            public string kind;
            public long length;
            public long writeUtcTicks;
            public int attributes;
            public string sha256;
        }

        internal const string StateRelativePath = ".buildpipeline/transactions/hybridclr-generation";

        private const string JournalDocumentType =
            "hybridclr-generation-transaction";
        private const int MaximumOperationCount = 32;
        private const int MaximumIdentityEntryCount = 100000;
        private const long MaximumIdentityFileBytes = 4L * 1024L * 1024L * 1024L;
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
        private bool sourceQualificationSuspended;

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
            if (finished || committed || sourceQualificationSuspended
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

        private sealed class PathIdentityMismatchException : IOException
        {
            internal PathIdentityMismatchException(string message, Exception innerException = null)
                : base(message, innerException)
            {
            }
        }

        internal void ValidateNoOutputTargetOverlap(
            IReadOnlyList<HybridCLROutputTarget> outputTargets)
        {
            ValidateActive();
            if (outputTargets == null)
            {
                throw new ArgumentNullException(nameof(outputTargets));
            }

            for (int outputIndex = 0; outputIndex < outputTargets.Count; outputIndex++)
            {
                HybridCLROutputTarget output = outputTargets[outputIndex]
                    ?? throw new ArgumentException(
                        "HybridCLR output targets cannot contain null entries.",
                        nameof(outputTargets));
                string outputRoot = Path.GetFullPath(output.FinalDirectory);
                string outputRootMeta = outputRoot + ".meta";
                for (int operationIndex = 0;
                     operationIndex < journal.operations.Length;
                     operationIndex++)
                {
                    string generationTarget = journal.operations[operationIndex].target;
                    if (PathsOverlap(generationTarget, outputRoot)
                        || PathsOverlap(generationTarget, outputRootMeta)
                        || PathsOverlap(generationTarget + ".meta", outputRoot)
                        || PathsOverlap(generationTarget + ".meta", outputRootMeta))
                    {
                        throw new InvalidOperationException(
                            "HybridCLR generation and published output ownership overlap. " +
                            $"Generation target '{generationTarget}' conflicts with output role '{output.Role}' at '{outputRoot}'.");
                    }
                }

                string[] cleanupDirectories = journal.cleanupDirectories
                    ?? Array.Empty<string>();
                for (int cleanupIndex = 0;
                     cleanupIndex < cleanupDirectories.Length;
                     cleanupIndex++)
                {
                    string cleanup = cleanupDirectories[cleanupIndex];
                    if (PathsOverlap(cleanup, outputRoot)
                        || PathsOverlap(cleanup, outputRootMeta))
                    {
                        throw new InvalidOperationException(
                            "HybridCLR generated-directory cleanup and published output ownership overlap. " +
                            $"Cleanup target '{cleanup}' conflicts with output role '{output.Role}' at '{outputRoot}'.");
                    }
                }
            }
        }

        internal IDisposable SuspendForSourceQualification()
        {
            return SuspendForSourceQualificationCore(crashPredicate: null);
        }

        internal IDisposable SuspendForSourceQualificationForTesting(
            Func<CrashCheckpoint, string, bool> crashPredicate)
        {
            if (crashPredicate == null)
            {
                throw new ArgumentNullException(nameof(crashPredicate));
            }

            return SuspendForSourceQualificationCore(crashPredicate);
        }

        private IDisposable SuspendForSourceQualificationCore(
            Func<CrashCheckpoint, string, bool> crashPredicate)
        {
            ValidateActive();
            try
            {
                for (int index = 0; index < journal.operations.Length; index++)
                {
                    Operation operation = journal.operations[index];
                    RequirePathIdentity(
                        operation.backup,
                        operation.originalIdentity,
                        "generation backup before source qualification");
                    operation.generatedIdentity = CapturePathIdentity(
                        operation.target,
                        "generated source-qualification state");
                    operation.generatedIdentityCaptured = true;
                }

                // Generated identities must be durable before the first generated target is moved.
                Persist();
                ValidateActiveStateForSuspension(journal);
            }
            catch (Exception identityFailure)
            {
                preserveForRecovery = true;
                throw new IOException(
                    "HybridCLR source-qualification identity capture failed. The active journal and all filesystem evidence were preserved.",
                    identityFailure);
            }

            try
            {
                journal.phase = RollingBackPhase;
                Persist();
                for (int index = journal.operations.Length - 1; index >= 0; index--)
                {
                    Operation operation = journal.operations[index];
                    operation.state = RestorePendingState;
                    Persist();
                    SuspendOperation(operation, crashPredicate);
                    operation.state = RestoredState;
                    Persist();
                }

                CleanupGeneratedDirectories(journal);
                ValidateOriginalState(journal);
                journal.phase = RolledBackPhase;
                Persist();
                sourceQualificationSuspended = true;
                return new SourceQualificationSuspension(this, crashPredicate);
            }
            catch (SimulatedProcessCrashException)
            {
                preserveForRecovery = true;
                throw;
            }
            catch (PathIdentityMismatchException)
            {
                preserveForRecovery = true;
                throw;
            }
            catch (Exception suspensionFailure)
            {
                try
                {
                    Rollback(crashPredicate: null);
                    sourceQualificationSuspended = false;
                }
                catch (Exception rollbackFailure)
                {
                    preserveForRecovery = true;
                    throw new AggregateException(
                        "HybridCLR generation source-qualification suspension failed and durable rollback did not complete. " +
                        $"Recovery state remains at '{activeJournalPath}'.",
                        suspensionFailure,
                        rollbackFailure);
                }

                throw;
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
            return RecoverPendingCore(
                projectRoot,
                out assetsChanged,
                crashPredicate: null);
        }

        internal static bool RecoverPendingForTesting(
            string projectRoot,
            out bool assetsChanged,
            Func<CrashCheckpoint, string, bool> crashPredicate)
        {
            return RecoverPendingCore(
                projectRoot,
                out assetsChanged,
                crashPredicate);
        }

        private static bool RecoverPendingCore(
            string projectRoot,
            out bool assetsChanged,
            Func<CrashCheckpoint, string, bool> crashPredicate)
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
                bool phaseCanCommit =
                    string.Equals(value.phase, ActivePhase, StringComparison.Ordinal)
                    || string.Equals(value.phase, CommittedPhase, StringComparison.Ordinal);
                if (phaseCanCommit
                    && (string.Equals(value.phase, CommittedPhase, StringComparison.Ordinal)
                        || decision == BuildPublicationDecision.Commit))
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
                    transaction.Rollback(crashPredicate);
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
                    // Move the exact source object into protected scratch first so Unix mode bits,
                    // timestamps, ACL-visible attributes, and the inode's contents remain intact.
                    File.Move(operation.target, operation.backup);
                    RequirePathIdentity(
                        operation.backup,
                        operation.originalIdentity,
                        "generation backup");
                    if (string.Equals(
                            operation.mode,
                            HybridCLRGenerationPathMode.SnapshotFile.ToString(),
                            StringComparison.Ordinal))
                    {
                        File.Copy(operation.backup, operation.target, overwrite: false);
                        ApplyPathMetadata(operation.originalIdentity, operation.target);
                        RequirePathIdentity(
                            operation.target,
                            operation.originalIdentity,
                            "generation working file");
                    }
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

                RequirePathIdentity(
                    operation.backup,
                    operation.originalIdentity,
                    "generation backup");

                TriggerCrash(
                    crashPredicate,
                    CrashCheckpoint.AfterBackupMutationBeforeJournal,
                    operation.target);
                operation.state = BackedUpState;
                Persist();
            }
        }

        private void SuspendOperation(
            Operation operation,
            Func<CrashCheckpoint, string, bool> crashPredicate)
        {
            RequireScratchPathAbsent(operation.discard, "suspended generated state");
            RequirePathIdentity(
                operation.target,
                operation.generatedIdentity,
                "generated source-qualification state before suspension");
            MovePathExact(
                operation.target,
                operation.discard,
                operation.generatedIdentity,
                "generated source-qualification state");

            TriggerCrash(
                crashPredicate,
                CrashCheckpoint.AfterSuspendedTargetDisplacedBeforeRestore,
                operation.target);

            if (!operation.originalExisted)
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(operation.target));
            MovePathExact(
                operation.backup,
                operation.target,
                operation.originalIdentity,
                "source-qualification original");
        }

        private void ResumeAfterSourceQualification(
            Func<CrashCheckpoint, string, bool> crashPredicate)
        {
            ThrowIfDisposed();
            if (!sourceQualificationSuspended
                || finished
                || committed
                || !string.Equals(journal.phase, RolledBackPhase, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "HybridCLR generation inputs are not suspended for source qualification.");
            }

            try
            {
                ValidateSuspendedState(journal);
            }
            catch (Exception identityFailure)
            {
                preserveForRecovery = true;
                throw new IOException(
                    "HybridCLR source-qualification state changed while suspended. Unknown evidence was preserved and automatic resume was refused.",
                    identityFailure);
            }

            try
            {
                journal.phase = PreparedPhase;
                Persist();
                for (int index = 0; index < journal.operations.Length; index++)
                {
                    Operation operation = journal.operations[index];
                    operation.state = operation.originalExisted
                        ? BackupPendingState
                        : PendingState;
                    Persist();
                    ResumeOperation(operation, crashPredicate);
                    operation.state = operation.originalExisted
                        ? BackedUpState
                        : AbsentState;
                    Persist();
                }

                journal.phase = ActivePhase;
                Persist();
                sourceQualificationSuspended = false;
            }
            catch (SimulatedProcessCrashException)
            {
                preserveForRecovery = true;
                throw;
            }
            catch (PathIdentityMismatchException)
            {
                preserveForRecovery = true;
                throw;
            }
            catch (Exception resumeFailure)
            {
                try
                {
                    Rollback(crashPredicate: null);
                    sourceQualificationSuspended = false;
                }
                catch (Exception rollbackFailure)
                {
                    preserveForRecovery = true;
                    throw new AggregateException(
                        "HybridCLR generation source-qualification resume failed and durable rollback did not complete. " +
                        $"Recovery state remains at '{activeJournalPath}'.",
                        resumeFailure,
                        rollbackFailure);
                }

                throw;
            }
        }

        private void ResumeOperation(
            Operation operation,
            Func<CrashCheckpoint, string, bool> crashPredicate)
        {
            if (operation.originalExisted)
            {
                RequireScratchPathAbsent(operation.backup, "resumed generation backup");
                MovePathExact(
                    operation.target,
                    operation.backup,
                    operation.originalIdentity,
                    "resumed source original");
            }
            else if (File.Exists(operation.target) || Directory.Exists(operation.target))
            {
                throw new IOException(
                    $"HybridCLR source qualification created a target that was originally absent: '{operation.target}'.");
            }

            TriggerCrash(
                crashPredicate,
                CrashCheckpoint.AfterResumeOriginalDisplacedBeforeGeneratedRestore,
                operation.target);

            Directory.CreateDirectory(Path.GetDirectoryName(operation.target));
            MovePathExact(
                operation.discard,
                operation.target,
                operation.generatedIdentity,
                "resumed generated state");
        }

        private static void RequireScratchPathAbsent(string path, string description)
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                throw new IOException(
                    $"HybridCLR {description} already exists: '{path}'.");
            }
        }

        private static void RequireSafeDirectory(string path, string description)
        {
            if (!Directory.Exists(path) || File.Exists(path))
            {
                throw new DirectoryNotFoundException(
                    $"HybridCLR {description} was not found: '{path}'.");
            }

            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException(
                    $"HybridCLR {description} became a reparse point: '{path}'.");
            }
        }

        private static void ValidateActiveStateForSuspension(Journal value)
        {
            for (int index = 0; index < value.operations.Length; index++)
            {
                Operation operation = value.operations[index];
                RequirePathIdentity(
                    operation.backup,
                    operation.originalIdentity,
                    "generation backup before source qualification");
                RequirePathIdentity(
                    operation.target,
                    operation.generatedIdentity,
                    "generated source-qualification state");
                RequireScratchPathAbsent(
                    operation.discard,
                    "suspended generated state");
            }
        }

        private static void ValidateSuspendedState(Journal value)
        {
            for (int index = 0; index < value.operations.Length; index++)
            {
                Operation operation = value.operations[index];
                RequirePathIdentity(
                    operation.target,
                    operation.originalIdentity,
                    "source-qualification original");
                RequirePathIdentity(
                    operation.discard,
                    operation.generatedIdentity,
                    "suspended generated state");
                RequireScratchPathAbsent(
                    operation.backup,
                    "source-qualification generation backup");
            }
        }

        private static void MovePathExact(
            string source,
            string destination,
            PathIdentity expected,
            string description)
        {
            RequirePathIdentity(source, expected, description + " source");
            RequireScratchPathAbsent(destination, description + " destination");
            if (expected == null)
            {
                return;
            }

            string parent = Path.GetDirectoryName(destination);
            if (string.IsNullOrEmpty(parent))
            {
                throw new IOException(
                    $"HybridCLR {description} destination has no parent: '{destination}'.");
            }

            Directory.CreateDirectory(parent);
            if (string.Equals(expected.kind, "File", StringComparison.Ordinal))
            {
                File.Move(source, destination);
            }
            else
            {
                Directory.Move(source, destination);
            }

            RequirePathIdentity(destination, expected, description + " destination");
        }

        private static PathIdentity CapturePathIdentity(
            string path,
            string description)
        {
            bool fileExists = File.Exists(path);
            bool directoryExists = Directory.Exists(path);
            if (fileExists && directoryExists)
            {
                throw new IOException(
                    $"HybridCLR {description} has an ambiguous filesystem kind: '{path}'.");
            }

            if (!fileExists && !directoryExists)
            {
                return null;
            }

            FileAttributes rootAttributes = File.GetAttributes(path);
            if ((rootAttributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException(
                    $"HybridCLR {description} cannot be a symbolic link or reparse point: '{path}'.");
            }

            if (fileExists)
            {
                var info = new FileInfo(path);
                ValidateIdentityFileLength(info.Length, path, description);
                return new PathIdentity
                {
                    kind = "File",
                    length = info.Length,
                    writeUtcTicks = info.LastWriteTimeUtc.Ticks,
                    attributes = (int)info.Attributes,
                    sha256 = ComputeFileSha256(path),
                    entries = Array.Empty<PathIdentityEntry>()
                };
            }

            var rootInfo = new DirectoryInfo(path);
            var entries = new List<PathIdentityEntry>();
            var pending = new Stack<string>();
            pending.Push(path);
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                string[] children = Directory.GetFileSystemEntries(directory);
                Array.Sort(children, FileSystemPathComparer);
                for (int index = children.Length - 1; index >= 0; index--)
                {
                    string child = children[index];
                    FileAttributes attributes = File.GetAttributes(child);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new IOException(
                            $"HybridCLR {description} contains a symbolic link or reparse point: '{child}'.");
                    }

                    bool isDirectory = (attributes & FileAttributes.Directory) != 0;
                    string relative = GetIdentityRelativePath(path, child);
                    if (isDirectory)
                    {
                        var info = new DirectoryInfo(child);
                        entries.Add(new PathIdentityEntry
                        {
                            path = relative,
                            kind = "Directory",
                            length = 0,
                            writeUtcTicks = info.LastWriteTimeUtc.Ticks,
                            attributes = (int)info.Attributes,
                            sha256 = string.Empty
                        });
                        pending.Push(child);
                    }
                    else
                    {
                        var info = new FileInfo(child);
                        ValidateIdentityFileLength(info.Length, child, description);
                        entries.Add(new PathIdentityEntry
                        {
                            path = relative,
                            kind = "File",
                            length = info.Length,
                            writeUtcTicks = info.LastWriteTimeUtc.Ticks,
                            attributes = (int)info.Attributes,
                            sha256 = ComputeFileSha256(child)
                        });
                    }

                    if (entries.Count > MaximumIdentityEntryCount)
                    {
                        throw new IOException(
                            $"HybridCLR {description} exceeded the {MaximumIdentityEntryCount}-entry identity budget: '{path}'.");
                    }
                }
            }

            entries.Sort((left, right) =>
                StringComparer.Ordinal.Compare(left.path, right.path));
            return new PathIdentity
            {
                kind = "Directory",
                length = 0,
                writeUtcTicks = rootInfo.LastWriteTimeUtc.Ticks,
                attributes = (int)rootInfo.Attributes,
                sha256 = string.Empty,
                entries = entries.ToArray()
            };
        }

        private static void RequirePathIdentity(
            string path,
            PathIdentity expected,
            string description)
        {
            PathIdentity actual;
            try
            {
                actual = CapturePathIdentity(path, description);
            }
            catch (Exception exception) when (!(exception is PathIdentityMismatchException))
            {
                throw new PathIdentityMismatchException(
                    $"HybridCLR {description} could not be identified exactly: '{path}'.",
                    exception);
            }

            if (!PathIdentityEquals(actual, expected))
            {
                throw new PathIdentityMismatchException(
                    $"HybridCLR {description} identity changed. Unknown content was preserved: '{path}'.");
            }
        }

        private static void RequireOriginalIdentity(
            Operation operation,
            string path,
            string description)
        {
            RequirePathIdentity(path, operation.originalIdentity, description);
        }

        private static bool TryPathIdentityMatches(
            string path,
            PathIdentity expected)
        {
            try
            {
                return PathIdentityEquals(
                    CapturePathIdentity(path, "recovery candidate"),
                    expected);
            }
            catch
            {
                return false;
            }
        }

        private static bool PathIdentityEquals(PathIdentity left, PathIdentity right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null
                || !string.Equals(left.kind, right.kind, StringComparison.Ordinal)
                || left.length != right.length
                || left.writeUtcTicks != right.writeUtcTicks
                || left.attributes != right.attributes
                || !string.Equals(left.sha256, right.sha256, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            PathIdentityEntry[] leftEntries = left.entries ?? Array.Empty<PathIdentityEntry>();
            PathIdentityEntry[] rightEntries = right.entries ?? Array.Empty<PathIdentityEntry>();
            if (leftEntries.Length != rightEntries.Length)
            {
                return false;
            }

            for (int index = 0; index < leftEntries.Length; index++)
            {
                PathIdentityEntry first = leftEntries[index];
                PathIdentityEntry second = rightEntries[index];
                if (first == null || second == null
                    || !string.Equals(first.path, second.path, StringComparison.Ordinal)
                    || !string.Equals(first.kind, second.kind, StringComparison.Ordinal)
                    || first.length != second.length
                    || first.writeUtcTicks != second.writeUtcTicks
                    || first.attributes != second.attributes
                    || !string.Equals(first.sha256, second.sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private static void ValidatePathIdentityFormat(
            PathIdentity identity,
            bool allowNull,
            string fieldName)
        {
            if (identity == null)
            {
                if (allowNull)
                {
                    return;
                }

                throw new InvalidDataException(
                    $"HybridCLR generation journal is missing path identity '{fieldName}'.");
            }

            bool isFile = string.Equals(identity.kind, "File", StringComparison.Ordinal);
            bool isDirectory = string.Equals(identity.kind, "Directory", StringComparison.Ordinal);
            PathIdentityEntry[] entries = identity.entries ?? Array.Empty<PathIdentityEntry>();
            if ((!isFile && !isDirectory)
                || identity.writeUtcTicks <= 0
                || identity.writeUtcTicks > DateTime.MaxValue.Ticks
                || identity.attributes < 0
                || (isFile
                    && (identity.length < 0
                        || identity.length > MaximumIdentityFileBytes
                        || !IsSha256(identity.sha256)
                        || entries.Length != 0))
                || (isDirectory
                    && (identity.length != 0
                        || !string.IsNullOrEmpty(identity.sha256)
                        || entries.Length > MaximumIdentityEntryCount)))
            {
                throw new InvalidDataException(
                    $"HybridCLR generation journal path identity '{fieldName}' is invalid.");
            }

            string previousPath = null;
            var uniquePaths = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < entries.Length; index++)
            {
                PathIdentityEntry entry = entries[index];
                if (entry == null
                    || string.IsNullOrWhiteSpace(entry.path)
                    || Path.IsPathRooted(entry.path)
                    || entry.path.IndexOf('\\') >= 0
                    || entry.path.Split('/').Any(segment =>
                        segment.Length == 0 || segment == "." || segment == "..")
                    || !uniquePaths.Add(entry.path)
                    || (previousPath != null
                        && StringComparer.Ordinal.Compare(previousPath, entry.path) >= 0)
                    || entry.writeUtcTicks <= 0
                    || entry.writeUtcTicks > DateTime.MaxValue.Ticks
                    || entry.attributes < 0)
                {
                    throw new InvalidDataException(
                        $"HybridCLR generation journal path identity entry '{fieldName}[{index}]' is invalid.");
                }

                bool entryIsFile = string.Equals(entry.kind, "File", StringComparison.Ordinal);
                bool entryIsDirectory = string.Equals(entry.kind, "Directory", StringComparison.Ordinal);
                if ((!entryIsFile && !entryIsDirectory)
                    || (entryIsFile
                        && (entry.length < 0
                            || entry.length > MaximumIdentityFileBytes
                            || !IsSha256(entry.sha256)))
                    || (entryIsDirectory
                        && (entry.length != 0 || !string.IsNullOrEmpty(entry.sha256))))
                {
                    throw new InvalidDataException(
                        $"HybridCLR generation journal path identity entry '{fieldName}[{index}]' has invalid metadata.");
                }

                previousPath = entry.path;
            }
        }

        private static string GetIdentityRelativePath(string root, string path)
        {
            string prefix = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(path);
            if (!full.StartsWith(prefix, PathComparison))
            {
                throw new IOException(
                    $"HybridCLR identity entry escaped its root: '{full}'.");
            }

            return full.Substring(prefix.Length).Replace('\\', '/');
        }

        private static void ValidateIdentityFileLength(
            long length,
            string path,
            string description)
        {
            if (length < 0 || length > MaximumIdentityFileBytes)
            {
                throw new IOException(
                    $"HybridCLR {description} file exceeds the {MaximumIdentityFileBytes}-byte identity budget: '{path}'.");
            }
        }

        private static bool IsSha256(string value)
        {
            return value != null
                   && value.Length == 64
                   && value.All(Uri.IsHexDigit);
        }

        private static void ApplyPathMetadata(PathIdentity identity, string path)
        {
            if (identity == null)
            {
                return;
            }

            File.SetLastWriteTimeUtc(
                path,
                new DateTime(identity.writeUtcTicks, DateTimeKind.Utc));
            File.SetAttributes(path, (FileAttributes)identity.attributes);
        }

        private static void ValidateDiscardedGeneratedState(Journal value)
        {
            for (int index = 0; index < value.operations.Length; index++)
            {
                Operation operation = value.operations[index];
                if (operation.generatedIdentityCaptured)
                {
                    RequirePathIdentity(
                        operation.discard,
                        operation.generatedIdentity,
                        "discarded generated recovery evidence");
                }
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
            ValidateDiscardedGeneratedState(journal);
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
                if (operation.generatedIdentityCaptured)
                {
                    if (operation.generatedIdentity == null)
                    {
                        RequirePathIdentity(
                            operation.target,
                            expected: null,
                            "rollback originally-absent target");
                        RequirePathIdentity(
                            operation.discard,
                            expected: null,
                            "rollback originally-absent discard");
                        return;
                    }

                    bool targetMatches = TryPathIdentityMatches(
                        operation.target,
                        operation.generatedIdentity);
                    bool discardMatches = TryPathIdentityMatches(
                        operation.discard,
                        operation.generatedIdentity);
                    if (targetMatches && !discardMatches)
                    {
                        MovePathExact(
                            operation.target,
                            operation.discard,
                            operation.generatedIdentity,
                            "rollback newly-generated state");
                    }
                    else if (!targetMatches && discardMatches)
                    {
                        RequirePathIdentity(
                            operation.target,
                            expected: null,
                            "rollback originally-absent target");
                    }
                    else
                    {
                        throw new PathIdentityMismatchException(
                            "HybridCLR rollback found ambiguous newly-generated state. Unknown evidence was preserved.");
                    }
                }
                else
                {
                    DisplaceCurrentTarget(operation);
                }

                return;
            }

            bool backupExists = IsFileOperation(operation)
                ? File.Exists(operation.backup)
                : Directory.Exists(operation.backup);
            if (IsFileOperation(operation)
                && File.Exists(operation.target)
                && backupExists == false)
            {
                RequireOriginalIdentity(
                    operation,
                    operation.target,
                    "restored generation file");
                return;
            }

            if (!backupExists)
            {
                if (!IsFileOperation(operation)
                    && Directory.Exists(operation.target))
                {
                    RequireOriginalIdentity(
                        operation,
                        operation.target,
                        "restored generation directory");
                    return;
                }

                throw new IOException(
                    $"HybridCLR generation backup is missing and the original target cannot be proven restored: '{operation.target}'.");
            }

            RequireOriginalIdentity(
                operation,
                operation.backup,
                "generation backup");
            if (operation.generatedIdentityCaptured)
            {
                if (operation.generatedIdentity == null)
                {
                    RequirePathIdentity(
                        operation.target,
                        expected: null,
                        "rollback deleted generated target");
                    RequirePathIdentity(
                        operation.discard,
                        expected: null,
                        "rollback deleted generated discard");
                }
                else
                {
                    bool targetMatches = TryPathIdentityMatches(
                        operation.target,
                        operation.generatedIdentity);
                    bool discardMatches = TryPathIdentityMatches(
                        operation.discard,
                        operation.generatedIdentity);
                    if (targetMatches && !discardMatches)
                    {
                        MovePathExact(
                            operation.target,
                            operation.discard,
                            operation.generatedIdentity,
                            "rollback generated state");
                    }
                    else if (!targetMatches && discardMatches)
                    {
                        if (File.Exists(operation.target)
                            || Directory.Exists(operation.target))
                        {
                            RequireOriginalIdentity(
                                operation,
                                operation.target,
                                "already-restored original state");
                            return;
                        }
                    }

                    if (targetMatches == discardMatches)
                    {
                        throw new PathIdentityMismatchException(
                            "HybridCLR rollback found ambiguous generated/original state. Unknown evidence was preserved.");
                    }
                }
            }
            else
            {
                DisplaceCurrentTarget(operation);
            }
            TriggerCrash(
                crashPredicate,
                CrashCheckpoint.AfterRollbackTargetDisplacedBeforeRestore,
                operation.target);
            Directory.CreateDirectory(Path.GetDirectoryName(operation.target));
            if (IsFileOperation(operation))
            {
                File.Move(operation.backup, operation.target);
                RequireOriginalIdentity(
                    operation,
                    operation.target,
                    "restored generation file");
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
                    RequireOriginalIdentity(
                        operation,
                        operation.target,
                        "rollback verification");
                }
                else
                {
                    RequireOriginalIdentity(
                        operation,
                        operation.target,
                        "rollback directory verification");
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
                    originalExisted = exists
                };
                operation.originalIdentity = exists
                    ? CapturePathIdentity(entry.Path, "pre-generation original")
                    : null;

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
                documentType = JournalDocumentType,
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
                || !string.Equals(
                    value.documentType,
                    JournalDocumentType,
                    StringComparison.Ordinal)
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

                ValidatePathIdentityFormat(
                    operation.originalIdentity,
                    allowNull: true,
                    $"operations[{index}].originalIdentity");
                if (operation.originalExisted && operation.originalIdentity == null
                    || !operation.originalExisted && operation.originalIdentity != null)
                {
                    throw new InvalidDataException(
                        $"HybridCLR generation journal operation {index} has an original identity inconsistent with its pre-generation existence state.");
                }

                if (operation.generatedIdentityCaptured)
                {
                    ValidatePathIdentityFormat(
                        operation.generatedIdentity,
                        allowNull: true,
                        $"operations[{index}].generatedIdentity");
                }
                else if (operation.generatedIdentity != null)
                {
                    throw new InvalidDataException(
                        $"HybridCLR generation journal operation {index} has an uncommitted generated identity.");
                }
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
                BuildJsonDocumentContract.Validate<Journal>(
                    json,
                    JournalDocumentType,
                    "HybridCLR generation journal");
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
            string actual = ComputeSha256(
                Utf8WithoutBom.GetBytes(JsonUtility.ToJson(value, false)));
            value.checksum = expected;
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"HybridCLR generation journal checksum mismatch: '{path}'.");
            }

            NormalizeJsonUtilityNullIdentities(value);

            return value;
        }

        private static void NormalizeJsonUtilityNullIdentities(Journal value)
        {
            if (value?.operations == null)
            {
                return;
            }

            for (int index = 0; index < value.operations.Length; index++)
            {
                Operation operation = value.operations[index];
                if (operation == null)
                {
                    continue;
                }

                if (IsEmptyPathIdentity(operation.originalIdentity))
                {
                    operation.originalIdentity = null;
                }

                if (IsEmptyPathIdentity(operation.generatedIdentity))
                {
                    operation.generatedIdentity = null;
                }
            }
        }

        private static bool IsEmptyPathIdentity(PathIdentity identity)
        {
            return identity != null
                   && string.IsNullOrEmpty(identity.kind)
                   && identity.length == 0
                   && identity.writeUtcTicks == 0
                   && identity.attributes == 0
                   && string.IsNullOrEmpty(identity.sha256)
                   && (identity.entries == null || identity.entries.Length == 0);
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
                PathComparison);
        }

        private static bool PathsOverlap(string left, string right)
        {
            return PortablePathsEqual(left, right)
                   || PortableStrictDescendant(left, right)
                   || PortableStrictDescendant(right, left);
        }

        private static bool PortablePathsEqual(string left, string right)
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool PortableStrictDescendant(string parent, string child)
        {
            string prefix = Path.GetFullPath(parent).TrimEnd(
                                Path.DirectorySeparatorChar,
                                Path.AltDirectorySeparatorChar)
                            + Path.DirectorySeparatorChar;
            return Path.GetFullPath(child).StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase);
        }

        private static StringComparison PathComparison =>
            Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        private static StringComparer FileSystemPathComparer =>
            Path.DirectorySeparatorChar == '\\'
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(HybridCLRGenerationTransaction));
            }
        }
    }
}
