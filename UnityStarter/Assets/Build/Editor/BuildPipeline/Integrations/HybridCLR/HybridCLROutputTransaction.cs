using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    internal sealed class HybridCLROutputTarget
    {
        internal HybridCLROutputTarget(string role, string finalDirectory)
        {
            if (string.IsNullOrWhiteSpace(role))
            {
                throw new ArgumentException("HybridCLR output role is required.", nameof(role));
            }

            if (string.IsNullOrWhiteSpace(finalDirectory))
            {
                throw new ArgumentException("HybridCLR output directory is required.", nameof(finalDirectory));
            }

            Role = role;
            FinalDirectory = Path.GetFullPath(finalDirectory);
        }

        internal string Role { get; }
        internal string FinalDirectory { get; }
    }

    /// <summary>
    /// Publishes all HybridCLR generated outputs as one durable, identity-checked transaction.
    /// </summary>
    internal sealed class HybridCLROutputTransaction : IDisposable
    {
        private sealed class OutputState
        {
            internal HybridCLROutputTarget Target;
            internal JournalOperation Operation;
        }

        [Serializable]
        private sealed class Journal
        {
            public int schemaVersion;
            public long sequence;
            public string transactionId;
            public string phase;
            public string projectRoot;
            public string stateRoot;
            public string scratchRoot;
            public bool scratchInitialized;
            public JournalOperation[] operations;
            public string checksum;
        }

        [Serializable]
        private sealed class JournalOperation
        {
            public string role;
            public string target;
            public string targetMeta;
            public string stage;
            public string backup;
            public string stagedMeta;
            public string recoveryMeta;
            public string generatedMetaGuid;
            public HybridCLRDirectoryIdentity initialDirectory;
            public HybridCLRDirectoryIdentity stagedDirectory;
            public HybridCLRFileIdentity initialMeta;
            public HybridCLRFileIdentity finalMeta;
            public string state;
        }

        private sealed class JournalCandidate
        {
            internal string Path;
            internal Journal Value;
        }

        internal enum CrashCheckpoint
        {
            AfterBackupMoveBeforeJournal,
            AfterRootMetaInstallMoveBeforeJournal,
            AfterInstallMoveBeforeJournal,
            AfterUninstallMoveBeforeJournal,
            AfterRootMetaUninstallMoveBeforeJournal,
            AfterRestoreMoveBeforeJournal,
            AfterRootMetaRestoreMoveBeforeJournal,
            AfterCommittedJournalBeforeCleanup
        }

        private sealed class SimulatedProcessCrashException : IOException
        {
            internal SimulatedProcessCrashException(CrashCheckpoint checkpoint, string role)
                : base($"Simulated HybridCLR process crash at '{checkpoint}' for role '{role}'.")
            {
            }
        }

        internal const string OwnershipManifestFileName = HybridCLROutputOwnership.ManifestFileName;
        internal const string OwnershipIdentifier = HybridCLROutputOwnership.Owner;
        internal const int OwnershipSchemaVersion = HybridCLROutputOwnership.SchemaVersion;
        internal bool OutputsCommitted => committed;

        private const int JournalSchemaVersion = 2;
        private const string StateFolderName = "hybridclr";
        private const string LockFileName = "build.lock";
        private const string ActiveJournalFileName = "active.json";
        private const string JournalTemporaryPrefix = ActiveJournalFileName + ".tmp-";
        private const string PreparedPhase = "Prepared";
        private const string CommittingPhase = "Committing";
        private const string RollingBackPhase = "RollingBack";
        private const string RolledBackPhase = "RolledBack";
        private const string CommittedPhase = "Committed";
        private const string CleaningCommittedPhase = "CleaningCommitted";
        private const string CleaningRolledBackPhase = "CleaningRolledBack";
        private const string PreparedState = "Prepared";
        private const string StagedState = "Staged";
        private const string BackupMovePendingState = "BackupMovePending";
        private const string BackedUpState = "BackedUp";
        private const string MetaInstallMovePendingState = "MetaInstallMovePending";
        private const string MetaInstalledState = "MetaInstalled";
        private const string InstallMovePendingState = "InstallMovePending";
        private const string InstalledState = "Installed";
        private const string UninstallMovePendingState = "UninstallMovePending";
        private const string UninstalledState = "Uninstalled";
        private const string MetaUninstallMovePendingState = "MetaUninstallMovePending";
        private const string MetaUninstalledState = "MetaUninstalled";
        private const string RestoreMovePendingState = "RestoreMovePending";
        private const string DirectoryRestoredState = "DirectoryRestored";
        private const string MetaRestoreMovePendingState = "MetaRestoreMovePending";
        private const string RestoredState = "Restored";
        private const int MaximumOutputCount = 3;
        private const int MaximumStateRootEntryCount = 32;
        private const int MaximumJournalTemporaryFileCount = 8;
        private const long MaximumJournalSequence = 999;
        private const int MaximumScratchEntryCount =
            HybridCLROutputOwnership.MaximumManagedFileCount * MaximumOutputCount + 64;
        private const long MaximumJournalByteCount = 4L * 1024L * 1024L;
        private readonly string projectRoot;
        private readonly string stateRoot;
        private readonly string activeJournalPath;
        private readonly Journal journal;
        private readonly List<OutputState> outputs;
        private readonly FileStream outputLock;
        private bool committed;
        private bool finished;
        private bool disposed;
        private bool preserveStateForRecovery;

        private HybridCLROutputTransaction(
            string projectRoot,
            string stateRoot,
            FileStream outputLock,
            Journal journal,
            IReadOnlyList<HybridCLROutputTarget> targets)
        {
            this.projectRoot = projectRoot;
            this.stateRoot = stateRoot;
            this.outputLock = outputLock;
            this.journal = journal;
            activeJournalPath = Path.Combine(stateRoot, ActiveJournalFileName);
            outputs = new List<OutputState>(targets.Count);
            for (int index = 0; index < targets.Count; index++)
            {
                outputs.Add(new OutputState
                {
                    Target = targets[index],
                    Operation = journal.operations[index]
                });
            }
        }

        internal static HybridCLROutputTransaction Begin(
            string projectRoot,
            IReadOnlyList<HybridCLROutputTarget> targets)
        {
            string project = NormalizeProjectRoot(projectRoot);
            ValidateTargets(targets);
            ValidateTargetLocations(project, targets);
            string stateRoot = PrepareStateRoot(project);
            FileStream outputLock = AcquireProjectLock(stateRoot);
            try
            {
                RecoverPendingUnderLock(project, stateRoot);
                ValidateExistingOutputs(targets);
                EnsureNoDetachedState(stateRoot, expectedTransactionId: null);

                Journal journal = CreateJournal(project, stateRoot, targets);
                string journalPath = Path.Combine(stateRoot, ActiveJournalFileName);
                bool journalCreated = false;
                try
                {
                    PersistJournal(journal, journalPath, createNew: true);
                    journalCreated = true;
                    InitializeScratch(journal, journalPath);
                }
                catch (Exception setupException)
                {
                    if (journalCreated)
                    {
                        try
                        {
                            Rollback(journal, journalPath, project, stateRoot, crashPredicate: null);
                        }
                        catch (Exception rollbackException)
                        {
                            throw new AggregateException(
                                "HybridCLR transaction setup failed and durable rollback did not complete.",
                                setupException,
                                rollbackException);
                        }
                    }

                    ExceptionDispatchInfo.Capture(setupException).Throw();
                }

                return new HybridCLROutputTransaction(
                    project,
                    stateRoot,
                    outputLock,
                    journal,
                    targets);
            }
            catch
            {
                outputLock.Dispose();
                throw;
            }
        }

        internal static bool RecoverPending(string projectRoot)
        {
            string project = NormalizeProjectRoot(projectRoot);
            string stateRoot = GetStateRoot(project);
            EnsureStateRootIsSafe(project, stateRoot);
            if (!Directory.Exists(stateRoot))
            {
                return false;
            }

            using (FileStream outputLock = AcquireProjectLock(stateRoot))
            {
                bool recovered = RecoverPendingUnderLock(project, stateRoot);
                EnsureNoDetachedState(stateRoot, expectedTransactionId: null);
                return recovered;
            }
        }

        internal static bool RecoverPending(
            string projectRoot,
            IReadOnlyList<HybridCLROutputTarget> targets)
        {
            bool recovered = RecoverPending(projectRoot);
            string project = NormalizeProjectRoot(projectRoot);
            ValidateTargets(targets);
            ValidateTargetLocations(project, targets);
            ValidateExistingOutputs(targets);
            return recovered;
        }

        internal static string GetActiveJournalPathForTesting(string projectRoot)
        {
            return Path.Combine(GetStateRoot(Path.GetFullPath(projectRoot)), ActiveJournalFileName);
        }

        internal static void ValidateExistingOutputs(IReadOnlyList<HybridCLROutputTarget> targets)
        {
            ValidateTargets(targets);
            foreach (HybridCLROutputTarget target in targets)
            {
                HybridCLRDirectoryIdentity directory =
                    HybridCLROutputOwnership.CaptureInitialDirectory(target.FinalDirectory, target.Role);
                HybridCLRFileIdentity rootMeta = HybridCLROutputOwnership.CaptureOptionalFileIdentity(
                    target.FinalDirectory + ".meta",
                    $"root meta for role '{target.Role}'");
                if (directory != null
                    && directory.kind == HybridCLROutputOwnership.OwnedDirectoryKind
                    && rootMeta == null)
                {
                    throw new InvalidOperationException(
                        $"Owned HybridCLR output is missing its root Unity meta: '{target.FinalDirectory}.meta'.");
                }
            }
        }

        internal string GetStagingFilePath(string role, string fileName)
        {
            ThrowIfDisposed();
            HybridCLROutputOwnership.ValidateManagedFileName(fileName, allowMeta: false);
            return BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                Path.Combine(FindOutput(role).Operation.stage, fileName),
                $"HybridCLR staged artifact '{fileName}'");
        }

        internal string GetFinalFilePath(string role, string fileName)
        {
            ThrowIfDisposed();
            HybridCLROutputOwnership.ValidateManagedFileName(fileName, allowMeta: false);
            return BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                Path.Combine(FindOutput(role).Target.FinalDirectory, fileName),
                $"HybridCLR published artifact '{fileName}'");
        }

        internal void CompleteStaging(string role, IReadOnlyCollection<string> artifactFileNames)
        {
            ThrowIfDisposed();
            OutputState output = FindOutput(role);
            JournalOperation operation = output.Operation;
            if (operation.stagedDirectory != null || operation.state != PreparedState)
            {
                throw new InvalidOperationException(
                    $"HybridCLR output role '{role}' has already completed staging.");
            }

            operation.stagedDirectory = HybridCLROutputOwnership.PrepareStagedDirectory(
                operation.stage,
                operation.role,
                journal.transactionId,
                artifactFileNames,
                operation.target);
            operation.state = StagedState;
            PersistJournal(journal, activeJournalPath, createNew: false);
        }

        internal void Commit()
        {
            Commit(null);
        }

        internal void Commit(Action<string> beforePublish)
        {
            CommitCore(beforePublish, crashPredicate: null);
        }

        internal void CommitForTesting(Func<CrashCheckpoint, string, bool> crashPredicate)
        {
            if (crashPredicate == null)
            {
                throw new ArgumentNullException(nameof(crashPredicate));
            }

            CommitCore(beforePublish: null, crashPredicate: crashPredicate);
        }

        private void CommitCore(
            Action<string> beforePublish,
            Func<CrashCheckpoint, string, bool> crashPredicate)
        {
            ThrowIfDisposed();
            if (finished || committed)
            {
                throw new InvalidOperationException("HybridCLR outputs have already been committed.");
            }

            OutputState incomplete = outputs.FirstOrDefault(output =>
                output.Operation.stagedDirectory == null || output.Operation.state != StagedState);
            if (incomplete != null)
            {
                throw new InvalidOperationException(
                    $"HybridCLR output role '{incomplete.Target.Role}' has not completed staging.");
            }

            try
            {
                ValidateAllPreparedOutputs();
                journal.phase = CommittingPhase;
                PersistJournal(journal, activeJournalPath, createNew: false);
                foreach (OutputState output in outputs)
                {
                    beforePublish?.Invoke(output.Target.Role);
                    CommitOperation(output.Operation, crashPredicate);
                }

                ValidateCommittedOutputs(journal);
                journal.phase = CommittedPhase;
                PersistJournal(journal, activeJournalPath, createNew: false);
                committed = true;
                TriggerCrashCheckpoint(
                    crashPredicate,
                    CrashCheckpoint.AfterCommittedJournalBeforeCleanup,
                    string.Empty);
                CleanupCommitted(journal, activeJournalPath, projectRoot, stateRoot);
                finished = true;
            }
            catch (SimulatedProcessCrashException)
            {
                preserveStateForRecovery = true;
                throw;
            }
            catch (Exception commitFailure)
            {
                if (committed
                    || journal.phase == CommittedPhase
                    || journal.phase == CleaningCommittedPhase)
                {
                    preserveStateForRecovery = true;
                    throw new IOException(
                        "HybridCLR outputs committed successfully, but durable cleanup did not complete. " +
                        $"Recovery state remains at '{activeJournalPath}'.",
                        commitFailure);
                }

                try
                {
                    Rollback(
                        journal,
                        activeJournalPath,
                        projectRoot,
                        stateRoot,
                        crashPredicate: null);
                    finished = true;
                }
                catch (Exception rollbackFailure)
                {
                    preserveStateForRecovery = true;
                    throw new AggregateException(
                        "HybridCLR publication failed and durable rollback did not complete. " +
                        $"Recovery state remains at '{activeJournalPath}'.",
                        commitFailure,
                        rollbackFailure);
                }

                ExceptionDispatchInfo.Capture(commitFailure).Throw();
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            Exception disposeFailure = null;
            try
            {
                if (!finished && !committed && !preserveStateForRecovery)
                {
                    Rollback(
                        journal,
                        activeJournalPath,
                        projectRoot,
                        stateRoot,
                        crashPredicate: null);
                    finished = true;
                }
            }
            catch (Exception exception)
            {
                preserveStateForRecovery = true;
                disposeFailure = exception;
            }
            finally
            {
                disposed = true;
                outputLock.Dispose();
            }

            if (disposeFailure != null)
            {
                ExceptionDispatchInfo.Capture(disposeFailure).Throw();
            }
        }

        internal static void ValidateTargets(IReadOnlyList<HybridCLROutputTarget> targets)
        {
            if (targets == null || targets.Count == 0 || targets.Count > MaximumOutputCount)
            {
                throw new ArgumentException(
                    $"Between one and {MaximumOutputCount} HybridCLR output targets are required.",
                    nameof(targets));
            }

            var roles = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < targets.Count; index++)
            {
                HybridCLROutputTarget target = targets[index]
                    ?? throw new ArgumentException(
                        "HybridCLR output targets cannot contain null entries.",
                        nameof(targets));
                if (!IsKnownRole(target.Role))
                {
                    throw new InvalidOperationException(
                        $"Unsupported HybridCLR output role: '{target.Role}'.");
                }

                if (!roles.Add(target.Role))
                {
                    throw new InvalidOperationException(
                        $"HybridCLR output role is configured more than once: '{target.Role}'.");
                }

                string targetMeta = target.FinalDirectory + ".meta";
                for (int otherIndex = 0; otherIndex < index; otherIndex++)
                {
                    string other = targets[otherIndex].FinalDirectory;
                    string otherMeta = other + ".meta";
                    if (PortablePathsEqual(target.FinalDirectory, other)
                        || IsPortableStrictDescendant(target.FinalDirectory, other)
                        || IsPortableStrictDescendant(other, target.FinalDirectory)
                        || PortablePathsEqual(targetMeta, other)
                        || PortablePathsEqual(otherMeta, target.FinalDirectory)
                        || PortablePathsEqual(targetMeta, otherMeta))
                    {
                        throw new InvalidOperationException(
                            "HybridCLR generated-output directories and root meta paths must not overlap. " +
                            $"'{target.FinalDirectory}' conflicts with '{other}'.");
                    }
                }
            }
        }

        private void ValidateAllPreparedOutputs()
        {
            ValidateTargetLocations(projectRoot, outputs.Select(output => output.Target));
            foreach (OutputState output in outputs)
            {
                JournalOperation operation = output.Operation;
                ValidateOperationArtifactPathBudgets(operation);
                RequireDirectoryAt(
                    operation.target,
                    operation.role,
                    operation.initialDirectory,
                    "initial output");
                RequireAbsent(operation.backup, "backup");
                RequireDirectoryAt(
                    operation.stage,
                    operation.role,
                    operation.stagedDirectory,
                    "staged output");
                HybridCLROutputOwnership.RequireFileIdentity(
                    operation.targetMeta,
                    operation.initialMeta,
                    $"initial root meta for role '{operation.role}'");
                ValidateScratchMetaFiles(operation);
            }
        }

        private void CommitOperation(
            JournalOperation operation,
            Func<CrashCheckpoint, string, bool> crashPredicate)
        {
            ValidateJournalOperation(
                operation,
                journal,
                Array.IndexOf(journal.operations, operation));
            RequireDirectoryAt(
                operation.target,
                operation.role,
                operation.initialDirectory,
                "initial output at publication boundary");
            RequireAbsent(operation.backup, "backup at publication boundary");
            RequireDirectoryAt(
                operation.stage,
                operation.role,
                operation.stagedDirectory,
                "staged output at publication boundary");
            HybridCLROutputOwnership.RequireFileIdentity(
                operation.targetMeta,
                operation.initialMeta,
                $"initial root meta at publication boundary for role '{operation.role}'");
            ValidateScratchMetaFiles(operation);
            string parent = Path.GetDirectoryName(operation.target);
            if (string.IsNullOrEmpty(parent))
            {
                throw new InvalidOperationException(
                    $"HybridCLR output has no parent: '{operation.target}'.");
            }

            Directory.CreateDirectory(parent);
            if (operation.initialDirectory != null)
            {
                operation.state = BackupMovePendingState;
                PersistJournal(journal, activeJournalPath, createNew: false);
                RequireDirectoryAt(
                    operation.target,
                    operation.role,
                    operation.initialDirectory,
                    "initial output before backup");
                RequireAbsent(operation.backup, "backup");
                HybridCLROutputOwnership.RequireFileIdentity(
                    operation.targetMeta,
                    operation.initialMeta,
                    $"root meta before backup for role '{operation.role}'");
                Directory.Move(operation.target, operation.backup);
                TriggerCrashCheckpoint(
                    crashPredicate,
                    CrashCheckpoint.AfterBackupMoveBeforeJournal,
                    operation.role);
            }

            operation.state = BackedUpState;
            PersistJournal(journal, activeJournalPath, createNew: false);
            RequireAbsent(operation.target, "publication target");
            if (operation.initialDirectory != null)
            {
                RequireDirectoryAt(
                    operation.backup,
                    operation.role,
                    operation.initialDirectory,
                    "backed-up output");
            }

            if (operation.initialMeta == null)
            {
                operation.state = MetaInstallMovePendingState;
                PersistJournal(journal, activeJournalPath, createNew: false);
                RequireAbsent(operation.targetMeta, "root meta target");
                HybridCLROutputOwnership.RequireFileIdentity(
                    operation.stagedMeta,
                    operation.finalMeta,
                    $"staged root meta for role '{operation.role}'");
                RequireDirectoryAt(
                    operation.stage,
                    operation.role,
                    operation.stagedDirectory,
                    "staged output before root meta install");
                if (operation.initialDirectory != null)
                {
                    RequireDirectoryAt(
                        operation.backup,
                        operation.role,
                        operation.initialDirectory,
                        "backup before root meta install");
                }

                File.Move(operation.stagedMeta, operation.targetMeta);
                TriggerCrashCheckpoint(
                    crashPredicate,
                    CrashCheckpoint.AfterRootMetaInstallMoveBeforeJournal,
                    operation.role);
            }
            else
            {
                HybridCLROutputOwnership.RequireFileIdentity(
                    operation.targetMeta,
                    operation.initialMeta,
                    $"preserved root meta for role '{operation.role}'");
            }

            operation.state = MetaInstalledState;
            PersistJournal(journal, activeJournalPath, createNew: false);
            HybridCLROutputOwnership.RequireFileIdentity(
                operation.targetMeta,
                operation.finalMeta,
                $"published root meta for role '{operation.role}'");

            operation.state = InstallMovePendingState;
            PersistJournal(journal, activeJournalPath, createNew: false);
            RequireAbsent(operation.target, "publication target");
            RequireDirectoryAt(
                operation.stage,
                operation.role,
                operation.stagedDirectory,
                "staged output before install");
            if (operation.initialDirectory != null)
            {
                RequireDirectoryAt(
                    operation.backup,
                    operation.role,
                    operation.initialDirectory,
                    "backup before install");
            }

            HybridCLROutputOwnership.RequireFileIdentity(
                operation.targetMeta,
                operation.finalMeta,
                $"root meta before install for role '{operation.role}'");
            Directory.Move(operation.stage, operation.target);
            TriggerCrashCheckpoint(
                crashPredicate,
                CrashCheckpoint.AfterInstallMoveBeforeJournal,
                operation.role);
            operation.state = InstalledState;
            PersistJournal(journal, activeJournalPath, createNew: false);
            RequireDirectoryAt(
                operation.target,
                operation.role,
                operation.stagedDirectory,
                "installed output");
            HybridCLROutputOwnership.RequireFileIdentity(
                operation.targetMeta,
                operation.finalMeta,
                $"installed root meta for role '{operation.role}'");
        }

        private static void Rollback(
            Journal recovered,
            string journalPath,
            string projectRoot,
            string stateRoot,
            Func<CrashCheckpoint, string, bool> crashPredicate)
        {
            if (recovered.phase == CommittedPhase
                || recovered.phase == CleaningCommittedPhase)
            {
                throw new InvalidOperationException(
                    "A durably committed HybridCLR transaction cannot be rolled back.");
            }

            recovered.phase = RollingBackPhase;
            PersistJournal(recovered, journalPath, createNew: false);
            var failures = new List<Exception>();
            for (int index = recovered.operations.Length - 1; index >= 0; index--)
            {
                try
                {
                    RollbackOperation(
                        recovered,
                        recovered.operations[index],
                        index,
                        journalPath,
                        crashPredicate);
                }
                catch (Exception exception)
                {
                    failures.Add(new IOException(
                        $"Failed to durably roll back HybridCLR output role '{recovered.operations[index].role}'.",
                        exception));
                }
            }

            if (failures.Count > 0)
            {
                throw new AggregateException(
                    "HybridCLR durable rollback could not restore every output.",
                    failures);
            }

            recovered.phase = RolledBackPhase;
            PersistJournal(recovered, journalPath, createNew: false);
            CleanupRolledBack(recovered, journalPath, projectRoot, stateRoot);
        }

        private static void RollbackOperation(
            Journal recovered,
            JournalOperation operation,
            int operationIndex,
            string journalPath,
            Func<CrashCheckpoint, string, bool> crashPredicate)
        {
            ValidateJournalOperation(operation, recovered, operationIndex);
            HybridCLRDirectoryIdentity targetIdentity =
                HybridCLROutputOwnership.CaptureDirectory(operation.target, operation.role);
            HybridCLRDirectoryIdentity stageIdentity = null;
            if (operation.stagedDirectory == null)
            {
                ValidatePartialStage(operation, recovered.transactionId);
            }
            else
            {
                stageIdentity = HybridCLROutputOwnership.CaptureDirectory(
                    operation.stage,
                    operation.role);
            }

            HybridCLRDirectoryIdentity backupIdentity =
                HybridCLROutputOwnership.CaptureDirectory(operation.backup, operation.role);

            ValidateKnownDirectoryCopy(
                targetIdentity,
                operation.initialDirectory,
                operation.stagedDirectory,
                operation.target,
                "target");
            if (operation.stagedDirectory != null)
            {
                ValidateKnownDirectoryCopy(
                    stageIdentity,
                    expectedPrimary: operation.stagedDirectory,
                    expectedSecondary: null,
                    operation.stage,
                    "stage");
            }
            ValidateKnownDirectoryCopy(
                backupIdentity,
                expectedPrimary: operation.initialDirectory,
                expectedSecondary: null,
                operation.backup,
                "backup");

            bool targetIsInitial = operation.initialDirectory != null
                && HybridCLROutputOwnership.DirectoryIdentityEquals(
                    targetIdentity,
                    operation.initialDirectory);
            bool targetIsStaged = operation.stagedDirectory != null
                && HybridCLROutputOwnership.DirectoryIdentityEquals(
                    targetIdentity,
                    operation.stagedDirectory);
            bool stageIsStaged = operation.stagedDirectory != null
                && HybridCLROutputOwnership.DirectoryIdentityEquals(
                    stageIdentity,
                    operation.stagedDirectory);
            bool backupIsInitial = operation.initialDirectory != null
                && HybridCLROutputOwnership.DirectoryIdentityEquals(
                    backupIdentity,
                    operation.initialDirectory);

            if (operation.stagedDirectory == null)
            {
                if (!HybridCLROutputOwnership.DirectoryIdentityEquals(
                        targetIdentity,
                        operation.initialDirectory)
                    || backupIdentity != null)
                {
                    throw new InvalidOperationException(
                        $"Incomplete HybridCLR staging changed durable output for role '{operation.role}'.");
                }
            }
            else
            {
                if (targetIsStaged && stageIsStaged)
                {
                    throw new InvalidOperationException(
                        $"Ambiguous HybridCLR recovery has two staged copies for role '{operation.role}'.");
                }

                if (targetIsInitial && backupIsInitial)
                {
                    throw new InvalidOperationException(
                        $"Ambiguous HybridCLR recovery has two initial copies for role '{operation.role}'.");
                }

                if (targetIsStaged)
                {
                    if (stageIdentity != null)
                    {
                        throw new InvalidOperationException(
                            $"HybridCLR stage is not empty before uninstall for role '{operation.role}'.");
                    }

                    operation.state = UninstallMovePendingState;
                    PersistJournal(recovered, journalPath, createNew: false);
                    RequireDirectoryAt(
                        operation.target,
                        operation.role,
                        operation.stagedDirectory,
                        "installed output before rollback");
                    HybridCLROutputOwnership.RequireFileIdentity(
                        operation.targetMeta,
                        operation.finalMeta,
                        $"installed root meta before rollback for role '{operation.role}'");
                    if (operation.initialDirectory != null)
                    {
                        RequireDirectoryAt(
                            operation.backup,
                            operation.role,
                            operation.initialDirectory,
                            "backup before installed-output rollback");
                    }

                    Directory.Move(operation.target, operation.stage);
                    TriggerCrashCheckpoint(
                        crashPredicate,
                        CrashCheckpoint.AfterUninstallMoveBeforeJournal,
                        operation.role);
                    operation.state = UninstalledState;
                    PersistJournal(recovered, journalPath, createNew: false);
                    targetIdentity = null;
                    stageIdentity = operation.stagedDirectory;
                    targetIsInitial = false;
                }
                else if (targetIdentity != null && !targetIsInitial)
                {
                    throw new InvalidOperationException(
                        $"HybridCLR target identity is unknown for role '{operation.role}'.");
                }

                RollbackRootMeta(recovered, operation, journalPath, crashPredicate);

                targetIdentity = HybridCLROutputOwnership.CaptureDirectory(
                    operation.target,
                    operation.role);
                backupIdentity = HybridCLROutputOwnership.CaptureDirectory(
                    operation.backup,
                    operation.role);
                targetIsInitial = operation.initialDirectory != null
                    && HybridCLROutputOwnership.DirectoryIdentityEquals(
                        targetIdentity,
                        operation.initialDirectory);
                backupIsInitial = operation.initialDirectory != null
                    && HybridCLROutputOwnership.DirectoryIdentityEquals(
                        backupIdentity,
                        operation.initialDirectory);
                if (operation.initialDirectory != null && !targetIsInitial)
                {
                    if (targetIdentity != null || !backupIsInitial)
                    {
                        throw new InvalidOperationException(
                            $"Original HybridCLR directory cannot be proven recoverable for role '{operation.role}'.");
                    }

                    operation.state = RestoreMovePendingState;
                    PersistJournal(recovered, journalPath, createNew: false);
                    RequireDirectoryAt(
                        operation.backup,
                        operation.role,
                        operation.initialDirectory,
                        "backup before restoration");
                    RequireAbsent(operation.target, "target before restoration");
                    RequireDirectoryAt(
                        operation.stage,
                        operation.role,
                        operation.stagedDirectory,
                        "staged output before initial restoration");
                    Directory.Move(operation.backup, operation.target);
                    TriggerCrashCheckpoint(
                        crashPredicate,
                        CrashCheckpoint.AfterRestoreMoveBeforeJournal,
                        operation.role);
                    operation.state = DirectoryRestoredState;
                    PersistJournal(recovered, journalPath, createNew: false);
                }
                else if (operation.initialDirectory == null)
                {
                    if (targetIdentity != null || backupIdentity != null)
                    {
                        throw new InvalidOperationException(
                            $"Initially absent HybridCLR output was not removed for role '{operation.role}'.");
                    }

                    operation.state = DirectoryRestoredState;
                    PersistJournal(recovered, journalPath, createNew: false);
                }
            }

            RestoreInitialRootMeta(recovered, operation, journalPath, crashPredicate);
            RequireDirectoryAt(
                operation.target,
                operation.role,
                operation.initialDirectory,
                "restored output");
            HybridCLROutputOwnership.RequireFileIdentity(
                operation.targetMeta,
                operation.initialMeta,
                $"restored root meta for role '{operation.role}'");
            operation.state = RestoredState;
            PersistJournal(recovered, journalPath, createNew: false);
        }

        private static void RollbackRootMeta(
            Journal recovered,
            JournalOperation operation,
            string journalPath,
            Func<CrashCheckpoint, string, bool> crashPredicate)
        {
            HybridCLRFileIdentity targetMeta = HybridCLROutputOwnership.CaptureOptionalFileIdentity(
                operation.targetMeta,
                $"root meta target for role '{operation.role}'");
            HybridCLRFileIdentity stagedMeta = HybridCLROutputOwnership.CaptureOptionalFileIdentity(
                operation.stagedMeta,
                $"staged root meta for role '{operation.role}'");
            HybridCLRFileIdentity recoveryMeta = HybridCLROutputOwnership.CaptureOptionalFileIdentity(
                operation.recoveryMeta,
                $"recovery root meta for role '{operation.role}'");

            if (operation.initialMeta != null)
            {
                if (targetMeta != null
                    && !HybridCLROutputOwnership.FileIdentityEquals(targetMeta, operation.initialMeta))
                {
                    throw new InvalidOperationException(
                        $"HybridCLR root meta was externally replaced for role '{operation.role}'.");
                }

                if (recoveryMeta != null
                    && !HybridCLROutputOwnership.FileIdentityEquals(recoveryMeta, operation.initialMeta))
                {
                    throw new InvalidOperationException(
                        $"HybridCLR root meta recovery copy was externally replaced for role '{operation.role}'.");
                }

                if (stagedMeta != null)
                {
                    throw new InvalidOperationException(
                        $"HybridCLR found an impossible staged root meta for role '{operation.role}'.");
                }

                return;
            }

            if (recoveryMeta != null)
            {
                throw new InvalidOperationException(
                    $"HybridCLR found an impossible root meta recovery copy for role '{operation.role}'.");
            }

            if (targetMeta != null
                && !HybridCLROutputOwnership.FileIdentityEquals(targetMeta, operation.finalMeta))
            {
                throw new InvalidOperationException(
                    $"HybridCLR generated root meta was externally replaced for role '{operation.role}'.");
            }

            if (stagedMeta != null
                && !HybridCLROutputOwnership.FileIdentityEquals(stagedMeta, operation.finalMeta))
            {
                throw new InvalidOperationException(
                    $"HybridCLR staged root meta was externally replaced for role '{operation.role}'.");
            }

            if (targetMeta != null && stagedMeta != null)
            {
                throw new InvalidOperationException(
                    $"Ambiguous HybridCLR recovery has two generated root meta copies for role '{operation.role}'.");
            }

            if (targetMeta != null)
            {
                operation.state = MetaUninstallMovePendingState;
                PersistJournal(recovered, journalPath, createNew: false);
                HybridCLROutputOwnership.RequireFileIdentity(
                    operation.targetMeta,
                    operation.finalMeta,
                    $"generated root meta before rollback for role '{operation.role}'");
                RequireAbsent(operation.stagedMeta, "staged root meta before rollback");
                File.Move(operation.targetMeta, operation.stagedMeta);
                TriggerCrashCheckpoint(
                    crashPredicate,
                    CrashCheckpoint.AfterRootMetaUninstallMoveBeforeJournal,
                    operation.role);
                operation.state = MetaUninstalledState;
                PersistJournal(recovered, journalPath, createNew: false);
            }
            else if (stagedMeta == null && recovered.scratchInitialized)
            {
                throw new InvalidOperationException(
                    $"HybridCLR generated root meta is missing for role '{operation.role}'.");
            }
        }

        private static void RestoreInitialRootMeta(
            Journal recovered,
            JournalOperation operation,
            string journalPath,
            Func<CrashCheckpoint, string, bool> crashPredicate)
        {
            HybridCLRFileIdentity targetMeta = HybridCLROutputOwnership.CaptureOptionalFileIdentity(
                operation.targetMeta,
                $"root meta target for role '{operation.role}'");
            if (operation.initialMeta == null)
            {
                if (targetMeta != null)
                {
                    throw new InvalidOperationException(
                        $"Initially absent HybridCLR root meta remains published for role '{operation.role}'.");
                }

                return;
            }

            if (targetMeta != null)
            {
                if (!HybridCLROutputOwnership.FileIdentityEquals(targetMeta, operation.initialMeta))
                {
                    throw new InvalidOperationException(
                        $"HybridCLR root meta identity changed during restoration for role '{operation.role}'.");
                }

                return;
            }

            HybridCLRFileIdentity recoveryMeta = HybridCLROutputOwnership.CaptureOptionalFileIdentity(
                operation.recoveryMeta,
                $"root meta recovery copy for role '{operation.role}'");
            if (!HybridCLROutputOwnership.FileIdentityEquals(recoveryMeta, operation.initialMeta))
            {
                throw new InvalidOperationException(
                    $"Original HybridCLR root meta cannot be recovered for role '{operation.role}'.");
            }

            operation.state = MetaRestoreMovePendingState;
            PersistJournal(recovered, journalPath, createNew: false);
            RequireAbsent(operation.targetMeta, "root meta target before restoration");
            HybridCLROutputOwnership.RequireFileIdentity(
                operation.recoveryMeta,
                operation.initialMeta,
                $"root meta recovery copy for role '{operation.role}'");
            File.Move(operation.recoveryMeta, operation.targetMeta);
            TriggerCrashCheckpoint(
                crashPredicate,
                CrashCheckpoint.AfterRootMetaRestoreMoveBeforeJournal,
                operation.role);
            operation.state = RestoredState;
            PersistJournal(recovered, journalPath, createNew: false);
        }

        private static void ValidateKnownDirectoryCopy(
            HybridCLRDirectoryIdentity actual,
            HybridCLRDirectoryIdentity expectedPrimary,
            HybridCLRDirectoryIdentity expectedSecondary,
            string path,
            string kind)
        {
            if (actual == null)
            {
                return;
            }

            if (!HybridCLROutputOwnership.DirectoryIdentityEquals(actual, expectedPrimary)
                && !HybridCLROutputOwnership.DirectoryIdentityEquals(actual, expectedSecondary))
            {
                throw new InvalidOperationException(
                    $"HybridCLR recovery found an unknown {kind} identity: '{path}'.");
            }
        }

        private static void CleanupCommitted(
            Journal recovered,
            string journalPath,
            string projectRoot,
            string stateRoot)
        {
            ValidateCommittedOutputs(recovered);
            if (recovered.phase != CleaningCommittedPhase)
            {
                ValidateScratchForCleanup(recovered);
                foreach (JournalOperation operation in recovered.operations)
                {
                    if (Directory.Exists(operation.backup))
                    {
                        RequireDirectoryAt(
                            operation.backup,
                            operation.role,
                            operation.initialDirectory,
                            "backup before committed cleanup");
                    }
                    else
                    {
                        RequireAbsent(operation.backup, "backup before committed cleanup");
                    }
                }

                recovered.phase = CleaningCommittedPhase;
                PersistJournal(recovered, journalPath, createNew: false);
            }

            foreach (JournalOperation operation in recovered.operations)
            {
                DeleteCleanupTree(operation.backup, operation.target);
            }

            DeleteCleanupTree(recovered.scratchRoot, stateRoot);
            CleanupJournalTemporaryFiles(
                projectRoot,
                stateRoot,
                journalPath,
                recovered.transactionId);
            DeleteJournalFile(projectRoot, stateRoot, journalPath);
        }

        private static void CleanupRolledBack(
            Journal recovered,
            string journalPath,
            string projectRoot,
            string stateRoot)
        {
            ValidateRolledBackOutputs(recovered);
            if (recovered.phase != CleaningRolledBackPhase)
            {
                ValidateScratchForCleanup(recovered);
                recovered.phase = CleaningRolledBackPhase;
                PersistJournal(recovered, journalPath, createNew: false);
            }

            DeleteCleanupTree(recovered.scratchRoot, stateRoot);
            CleanupJournalTemporaryFiles(
                projectRoot,
                stateRoot,
                journalPath,
                recovered.transactionId);
            DeleteJournalFile(projectRoot, stateRoot, journalPath);
        }

        private static void ValidateCommittedOutputs(Journal value)
        {
            foreach (JournalOperation operation in value.operations)
            {
                RequireDirectoryAt(
                    operation.target,
                    operation.role,
                    operation.stagedDirectory,
                    "committed output");
                HybridCLROutputOwnership.RequireFileIdentity(
                    operation.targetMeta,
                    operation.finalMeta,
                    $"committed root meta for role '{operation.role}'");
                RequireAbsent(operation.stage, "committed stage");
                if (operation.initialDirectory == null)
                {
                    RequireAbsent(operation.backup, "impossible committed backup");
                }
            }
        }

        private static void ValidateRolledBackOutputs(Journal value)
        {
            foreach (JournalOperation operation in value.operations)
            {
                RequireDirectoryAt(
                    operation.target,
                    operation.role,
                    operation.initialDirectory,
                    "rolled-back output");
                HybridCLROutputOwnership.RequireFileIdentity(
                    operation.targetMeta,
                    operation.initialMeta,
                    $"rolled-back root meta for role '{operation.role}'");
                RequireAbsent(operation.backup, "rolled-back backup");
            }
        }

        private OutputState FindOutput(string role)
        {
            ThrowIfDisposed();
            OutputState output = outputs.FirstOrDefault(candidate =>
                string.Equals(candidate.Target.Role, role, StringComparison.Ordinal));
            if (output == null)
            {
                throw new InvalidOperationException(
                    $"HybridCLR output role is not part of this transaction: '{role}'.");
            }

            return output;
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
                throw new DirectoryNotFoundException($"Unity project root was not found: '{project}'.");
            }

            return project;
        }

        private static string GetStateRoot(string projectRoot)
        {
            return Path.GetFullPath(Path.Combine(
                projectRoot,
                ".buildpipeline",
                "transactions",
                StateFolderName));
        }

        private static string PrepareStateRoot(string projectRoot)
        {
            string stateRoot = GetStateRoot(projectRoot);
            BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                stateRoot,
                "HybridCLR transaction state root",
                86);
            EnsureStateRootIsSafe(projectRoot, stateRoot);
            Directory.CreateDirectory(stateRoot);
            EnsureStateRootIsSafe(projectRoot, stateRoot);
            return stateRoot;
        }

        private static FileStream AcquireProjectLock(string stateRoot)
        {
            string lockPath = BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                Path.Combine(stateRoot, LockFileName),
                "HybridCLR transaction lock");
            if (Directory.Exists(lockPath))
            {
                throw new InvalidOperationException(
                    $"HybridCLR transaction lock path resolves to a directory: '{lockPath}'.");
            }

            if (File.Exists(lockPath)
                && (File.GetAttributes(lockPath) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"HybridCLR transaction lock cannot be a reparse point: '{lockPath}'.");
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
                    "Another HybridCLR output transaction is active in this Unity project.",
                    exception);
            }
        }

        private static Journal CreateJournal(
            string projectRoot,
            string stateRoot,
            IReadOnlyList<HybridCLROutputTarget> targets)
        {
            string transactionId = Guid.NewGuid().ToString("N");
            string scratchRoot = Path.Combine(stateRoot, transactionId);
            var operations = new JournalOperation[targets.Count];
            for (int index = 0; index < targets.Count; index++)
            {
                HybridCLROutputTarget target = targets[index];
                string suffix = index.ToString("D3", CultureInfo.InvariantCulture)
                    + "-"
                    + SanitizeRoleForPath(target.Role);
                HybridCLRDirectoryIdentity initialDirectory =
                    HybridCLROutputOwnership.CaptureInitialDirectory(
                        target.FinalDirectory,
                        target.Role);
                string targetMeta = target.FinalDirectory + ".meta";
                HybridCLRFileIdentity initialMeta =
                    HybridCLROutputOwnership.CaptureOptionalFileIdentity(
                        targetMeta,
                        $"initial root meta for role '{target.Role}'");
                if (initialDirectory != null
                    && initialDirectory.kind == HybridCLROutputOwnership.OwnedDirectoryKind
                    && initialMeta == null)
                {
                    throw new InvalidOperationException(
                        $"Owned HybridCLR output is missing its root Unity meta: '{targetMeta}'.");
                }

                string generatedMetaGuid = string.Empty;
                HybridCLRFileIdentity finalMeta = initialMeta;
                if (finalMeta == null)
                {
                    generatedMetaGuid = HybridCLROutputOwnership.CreateDeterministicMetaGuid(
                        transactionId + "|" + target.Role + "|" + target.FinalDirectory);
                    finalMeta = HybridCLROutputOwnership.CreateGeneratedMetaIdentity(
                        generatedMetaGuid,
                        folderAsset: true);
                }

                string backup = Path.Combine(
                    Path.GetDirectoryName(target.FinalDirectory)
                        ?? throw new InvalidOperationException(
                            $"HybridCLR output has no parent: '{target.FinalDirectory}'."),
                    ".buildpipeline-hybridclr-"
                    + transactionId
                    + "-"
                    + index.ToString("D3", CultureInfo.InvariantCulture)
                    + ".backup");
                RequireAbsent(backup, "new transaction backup");
                operations[index] = new JournalOperation
                {
                    role = target.Role,
                    target = target.FinalDirectory,
                    targetMeta = targetMeta,
                    stage = Path.Combine(scratchRoot, "staging-" + suffix),
                    backup = backup,
                    stagedMeta = Path.Combine(scratchRoot, "root-meta-stage-" + suffix),
                    recoveryMeta = Path.Combine(scratchRoot, "root-meta-recovery-" + suffix),
                    generatedMetaGuid = generatedMetaGuid,
                    initialDirectory = initialDirectory,
                    stagedDirectory = null,
                    initialMeta = initialMeta,
                    finalMeta = finalMeta,
                    state = PreparedState
                };
            }

            var journal = new Journal
            {
                schemaVersion = JournalSchemaVersion,
                sequence = 0,
                transactionId = transactionId,
                phase = PreparedPhase,
                projectRoot = projectRoot,
                stateRoot = stateRoot,
                scratchRoot = scratchRoot,
                scratchInitialized = false,
                operations = operations,
                checksum = string.Empty
            };
            ValidateJournalPathBudgets(journal);
            return journal;
        }

        private static void ValidateJournalPathBudgets(Journal value)
        {
            const int maximumSequenceCharacterCount = 3;
            int temporaryJournalSuffixLength = ".tmp-".Length
                + 32
                + 1
                + maximumSequenceCharacterCount
                + 1
                + 32;
            BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                value.stateRoot,
                "HybridCLR transaction state root");
            BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                Path.Combine(value.stateRoot, LockFileName),
                "HybridCLR transaction lock");
            BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                Path.Combine(value.stateRoot, ActiveJournalFileName),
                "HybridCLR durable journal",
                temporaryJournalSuffixLength);
            BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                value.scratchRoot,
                "HybridCLR transaction scratch root");

            foreach (JournalOperation operation in value.operations)
            {
                BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                    operation.target,
                    $"HybridCLR published directory for role '{operation.role}'");
                BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                    operation.targetMeta,
                    $"HybridCLR published root meta for role '{operation.role}'");
                BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                    operation.stage,
                    $"HybridCLR staged directory for role '{operation.role}'");
                BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                    operation.backup,
                    $"HybridCLR backup directory for role '{operation.role}'");
                BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                    operation.stagedMeta,
                    $"HybridCLR staged root meta for role '{operation.role}'");
                BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                    operation.recoveryMeta,
                    $"HybridCLR recovery root meta for role '{operation.role}'");
                BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                    Path.Combine(operation.stage, HybridCLROutputOwnership.ManifestFileName),
                    $"HybridCLR staged ownership manifest for role '{operation.role}'");
            }
        }

        private static void ValidateOperationArtifactPathBudgets(JournalOperation operation)
        {
            ValidateFlatDirectoryMappingPathBudgets(
                operation.stage,
                operation.target,
                $"HybridCLR published artifact for role '{operation.role}'");
            if (Directory.Exists(operation.target))
            {
                ValidateFlatDirectoryMappingPathBudgets(
                    operation.target,
                    operation.backup,
                    $"HybridCLR backup artifact for role '{operation.role}'");
            }
        }

        private static void ValidateFlatDirectoryMappingPathBudgets(
            string sourceDirectory,
            string destinationDirectory,
            string displayName)
        {
            BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                destinationDirectory,
                displayName + " root");
            if (!Directory.Exists(sourceDirectory))
            {
                return;
            }

            foreach (string entry in Directory.EnumerateFileSystemEntries(
                         sourceDirectory,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                string destination = Path.Combine(
                    destinationDirectory,
                    Path.GetFileName(entry));
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                        destination,
                        displayName);
                }
                else
                {
                    BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                        destination,
                        displayName);
                }
            }
        }

        private static void InitializeScratch(Journal value, string journalPath)
        {
            RequireAbsent(value.scratchRoot, "transaction scratch");
            Directory.CreateDirectory(value.scratchRoot);
            foreach (JournalOperation operation in value.operations)
            {
                Directory.CreateDirectory(operation.stage);
                if (operation.initialMeta != null)
                {
                    HybridCLROutputOwnership.CopyFileAndVerify(
                        operation.targetMeta,
                        operation.recoveryMeta,
                        operation.initialMeta,
                        $"root meta recovery for role '{operation.role}'");
                }
                else
                {
                    HybridCLRFileIdentity generated =
                        HybridCLROutputOwnership.WriteGeneratedMeta(
                            operation.stagedMeta,
                            operation.generatedMetaGuid,
                            folderAsset: true);
                    if (!HybridCLROutputOwnership.FileIdentityEquals(
                            generated,
                            operation.finalMeta))
                    {
                        throw new InvalidOperationException(
                            $"Generated HybridCLR root meta identity is not deterministic for role '{operation.role}'.");
                    }
                }
            }

            value.scratchInitialized = true;
            PersistJournal(value, journalPath, createNew: false);
        }

        private static bool RecoverPendingUnderLock(string projectRoot, string stateRoot)
        {
            string journalPath = Path.Combine(stateRoot, ActiveJournalFileName);
            if (Directory.Exists(journalPath))
            {
                throw new InvalidOperationException(
                    $"HybridCLR active journal path resolves to a directory: '{journalPath}'.");
            }

            bool hasActive = File.Exists(journalPath);
            bool hasTemporary = Directory
                .EnumerateFiles(stateRoot, JournalTemporaryPrefix + "*", SearchOption.TopDirectoryOnly)
                .Take(1)
                .Any();
            if (!hasActive && !hasTemporary)
            {
                EnsureNoDetachedState(stateRoot, expectedTransactionId: null);
                return false;
            }

            Journal recovered = ReadJournalAndReconcileTemporaryFiles(
                journalPath,
                projectRoot,
                stateRoot);
            EnsureNoDetachedState(stateRoot, recovered.transactionId);
            if (recovered.phase == CommittedPhase
                || recovered.phase == CleaningCommittedPhase)
            {
                CleanupCommitted(recovered, journalPath, projectRoot, stateRoot);
            }
            else if (recovered.phase == RolledBackPhase
                     || recovered.phase == CleaningRolledBackPhase)
            {
                CleanupRolledBack(recovered, journalPath, projectRoot, stateRoot);
            }
            else
            {
                Rollback(
                    recovered,
                    journalPath,
                    projectRoot,
                    stateRoot,
                    crashPredicate: null);
            }

            EnsureNoDetachedState(stateRoot, expectedTransactionId: null);
            return true;
        }

        private static Journal ReadJournalAndReconcileTemporaryFiles(
            string journalPath,
            string projectRoot,
            string stateRoot)
        {
            var candidates = new List<JournalCandidate>();
            if (File.Exists(journalPath))
            {
                candidates.Add(new JournalCandidate
                {
                    Path = journalPath,
                    Value = ReadAndValidateJournal(journalPath, projectRoot, stateRoot)
                });
            }

            string[] temporaryFiles = Directory
                .EnumerateFiles(stateRoot, JournalTemporaryPrefix + "*", SearchOption.TopDirectoryOnly)
                .Take(MaximumJournalTemporaryFileCount + 1)
                .ToArray();
            if (temporaryFiles.Length > MaximumJournalTemporaryFileCount)
            {
                throw new InvalidOperationException(
                    $"HybridCLR transaction has more than {MaximumJournalTemporaryFileCount} journal temporary files.");
            }

            foreach (string temporaryFile in temporaryFiles)
            {
                candidates.Add(new JournalCandidate
                {
                    Path = temporaryFile,
                    Value = ReadAndValidateJournal(temporaryFile, projectRoot, stateRoot)
                });
            }

            if (candidates.Count == 0)
            {
                throw new InvalidOperationException(
                    "HybridCLR recovery was requested without a durable journal candidate.");
            }

            string transactionId = candidates[0].Value.transactionId;
            if (candidates.Any(candidate =>
                    !string.Equals(
                        candidate.Value.transactionId,
                        transactionId,
                        StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    "HybridCLR journal candidates belong to different transactions.");
            }

            long highestSequence = candidates.Max(candidate => candidate.Value.sequence);
            JournalCandidate[] newest = candidates
                .Where(candidate => candidate.Value.sequence == highestSequence)
                .ToArray();
            string checksum = newest[0].Value.checksum;
            if (newest.Any(candidate =>
                    !string.Equals(candidate.Value.checksum, checksum, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    "HybridCLR transaction has conflicting durable journal candidates at the same sequence.");
            }

            Journal selected = newest[0].Value;
            if (!File.Exists(journalPath))
            {
                PersistJournal(selected, journalPath, createNew: true);
            }
            else if (!FileSystemPathsEqual(newest[0].Path, journalPath))
            {
                PersistJournal(selected, journalPath, createNew: false);
            }

            CleanupJournalTemporaryFiles(
                projectRoot,
                stateRoot,
                journalPath,
                selected.transactionId);
            return selected;
        }

        private static Journal ReadAndValidateJournal(
            string journalPath,
            string projectRoot,
            string stateRoot)
        {
            string fullPath = Path.GetFullPath(journalPath);
            if (!IsStrictDescendant(stateRoot, fullPath)
                || !FileSystemPathsEqual(Path.GetDirectoryName(fullPath), stateRoot)
                || Directory.Exists(fullPath)
                || !File.Exists(fullPath))
            {
                throw new InvalidOperationException(
                    $"HybridCLR durable journal path is invalid: '{fullPath}'.");
            }

            if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"HybridCLR durable journal cannot be a reparse point: '{fullPath}'.");
            }

            var info = new FileInfo(fullPath);
            if (info.Length <= 0 || info.Length > MaximumJournalByteCount)
            {
                throw new InvalidOperationException(
                    $"HybridCLR durable journal size is invalid: '{fullPath}', {info.Length} bytes.");
            }

            byte[] bytes = File.ReadAllBytes(fullPath);
            if (HasUtf8Bom(bytes))
            {
                throw new InvalidOperationException(
                    $"HybridCLR durable journal must use UTF-8 without BOM: '{fullPath}'.");
            }

            Journal recovered;
            try
            {
                recovered = JsonUtility.FromJson<Journal>(
                    new UTF8Encoding(false, true).GetString(bytes));
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"HybridCLR durable journal is not valid UTF-8 JSON: '{fullPath}'.",
                    exception);
            }

            NormalizeJsonUtilityOptionalIdentities(recovered);

            if (recovered == null
                || recovered.schemaVersion != JournalSchemaVersion
                || recovered.sequence <= 0
                || !HybridCLROutputOwnership.IsTransactionId(recovered.transactionId)
                || !IsKnownPhase(recovered.phase)
                || recovered.operations == null
                || recovered.operations.Length == 0
                || recovered.operations.Length > MaximumOutputCount)
            {
                throw new InvalidOperationException(
                    $"HybridCLR durable journal has an unsupported or incomplete schema: '{fullPath}'.");
            }

            string journalName = Path.GetFileName(fullPath);
            if (!string.Equals(journalName, ActiveJournalFileName, StringComparison.Ordinal)
                && (!TryParseTemporaryJournalFileName(
                        journalName,
                        recovered.transactionId,
                        out long temporarySequence)
                    || temporarySequence != recovered.sequence))
            {
                throw new InvalidOperationException(
                    $"HybridCLR journal candidate name does not match its transaction: '{fullPath}'.");
            }

            if (!FileSystemPathsEqual(projectRoot, recovered.projectRoot)
                || !FileSystemPathsEqual(stateRoot, recovered.stateRoot))
            {
                throw new InvalidOperationException(
                    $"HybridCLR durable journal belongs to another Unity project: '{fullPath}'.");
            }

            string expectedScratch = Path.Combine(stateRoot, recovered.transactionId);
            if (!FileSystemPathsEqual(expectedScratch, recovered.scratchRoot))
            {
                throw new InvalidOperationException(
                    $"HybridCLR durable journal scratch root is invalid: '{recovered.scratchRoot}'.");
            }

            string expectedChecksum = ComputeJournalChecksum(recovered);
            if (!string.Equals(expectedChecksum, recovered.checksum, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"HybridCLR durable journal checksum is invalid: '{fullPath}'.");
            }

            var roles = new HashSet<string>(StringComparer.Ordinal);
            var targetPaths = new List<string>();
            for (int index = 0; index < recovered.operations.Length; index++)
            {
                JournalOperation operation = recovered.operations[index];
                ValidateJournalOperation(operation, recovered, index);
                if (!roles.Add(operation.role))
                {
                    throw new InvalidOperationException(
                        $"HybridCLR durable journal contains duplicate role '{operation.role}'.");
                }

                foreach (string previous in targetPaths)
                {
                    if (PortablePathsEqual(previous, operation.target)
                        || IsPortableStrictDescendant(previous, operation.target)
                        || IsPortableStrictDescendant(operation.target, previous))
                    {
                        throw new InvalidOperationException(
                            "HybridCLR durable journal contains overlapping targets.");
                    }
                }

                targetPaths.Add(operation.target);
            }

            if (!recovered.scratchInitialized
                && (recovered.phase != PreparedPhase
                    || recovered.operations.Any(operation => operation.state != PreparedState)))
            {
                throw new InvalidOperationException(
                    "HybridCLR durable journal advanced before scratch initialization.");
            }

            if (recovered.phase == PreparedPhase
                && recovered.operations.Any(operation =>
                    operation.state != PreparedState && operation.state != StagedState))
            {
                throw new InvalidOperationException(
                    "HybridCLR prepared journal contains a publication operation.");
            }

            if ((recovered.phase == CommittedPhase
                 || recovered.phase == CleaningCommittedPhase)
                && recovered.operations.Any(operation => operation.state != InstalledState))
            {
                throw new InvalidOperationException(
                    "HybridCLR committed journal does not declare every output installed.");
            }

            if ((recovered.phase == RolledBackPhase
                 || recovered.phase == CleaningRolledBackPhase)
                && recovered.operations.Any(operation => operation.state != RestoredState))
            {
                throw new InvalidOperationException(
                    "HybridCLR rolled-back journal does not declare every output restored.");
            }

            ValidateJournalPathBudgets(recovered);
            return recovered;
        }

        private static void ValidateJournalOperation(
            JournalOperation operation,
            Journal owner,
            int operationIndex)
        {
            if (operation == null
                || !IsKnownRole(operation.role)
                || !IsKnownOperationState(operation.state)
                || operationIndex < 0
                || operationIndex >= MaximumOutputCount)
            {
                throw new InvalidOperationException(
                    "HybridCLR durable journal contains an invalid operation.");
            }

            BuildPathPolicy.EnsureGeneratedAssetsDirectory(owner.projectRoot, operation.target);
            if (!FileSystemPathsEqual(operation.target + ".meta", operation.targetMeta))
            {
                throw new InvalidOperationException(
                    $"HybridCLR durable journal has an invalid root meta path for role '{operation.role}'.");
            }

            string suffix = operationIndex.ToString("D3", CultureInfo.InvariantCulture)
                + "-"
                + SanitizeRoleForPath(operation.role);
            string expectedStage = Path.Combine(owner.scratchRoot, "staging-" + suffix);
            string expectedStagedMeta = Path.Combine(owner.scratchRoot, "root-meta-stage-" + suffix);
            string expectedRecoveryMeta = Path.Combine(owner.scratchRoot, "root-meta-recovery-" + suffix);
            string expectedBackup = Path.Combine(
                Path.GetDirectoryName(operation.target)
                    ?? throw new InvalidOperationException(
                        $"HybridCLR journal target has no parent: '{operation.target}'."),
                ".buildpipeline-hybridclr-"
                + owner.transactionId
                + "-"
                + operationIndex.ToString("D3", CultureInfo.InvariantCulture)
                + ".backup");
            if (!FileSystemPathsEqual(expectedStage, operation.stage)
                || !FileSystemPathsEqual(expectedStagedMeta, operation.stagedMeta)
                || !FileSystemPathsEqual(expectedRecoveryMeta, operation.recoveryMeta)
                || !FileSystemPathsEqual(expectedBackup, operation.backup))
            {
                throw new InvalidOperationException(
                    $"HybridCLR durable journal paths are invalid for role '{operation.role}'.");
            }

            EnsureNoReparsePointsInPath(owner.stateRoot, operation.stage);
            EnsureNoReparsePointsInPath(owner.stateRoot, operation.stagedMeta);
            EnsureNoReparsePointsInPath(owner.stateRoot, operation.recoveryMeta);
            HybridCLROutputOwnership.ValidateDirectoryIdentitySchema(
                operation.initialDirectory,
                allowNull: true,
                operation.role + ".initialDirectory");
            HybridCLROutputOwnership.ValidateDirectoryIdentitySchema(
                operation.stagedDirectory,
                allowNull: true,
                operation.role + ".stagedDirectory");
            HybridCLROutputOwnership.ValidateFileIdentitySchema(
                operation.initialMeta,
                allowNull: true,
                operation.role + ".initialMeta");
            HybridCLROutputOwnership.ValidateFileIdentitySchema(
                operation.finalMeta,
                allowNull: false,
                operation.role + ".finalMeta");
            if (operation.initialDirectory != null
                && operation.initialDirectory.kind == HybridCLROutputOwnership.OwnedDirectoryKind
                && operation.initialMeta == null)
            {
                throw new InvalidOperationException(
                    $"HybridCLR journal owned directory has no root meta for role '{operation.role}'.");
            }

            if (operation.stagedDirectory != null
                && !string.Equals(
                    operation.stagedDirectory.transactionId,
                    owner.transactionId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"HybridCLR staged identity belongs to another transaction for role '{operation.role}'.");
            }

            if (operation.initialMeta == null)
            {
                if (!HybridCLROutputOwnership.IsTransactionId(operation.generatedMetaGuid))
                {
                    throw new InvalidOperationException(
                        $"HybridCLR journal generated root meta GUID is invalid for role '{operation.role}'.");
                }

                HybridCLRFileIdentity deterministic =
                    HybridCLROutputOwnership.CreateGeneratedMetaIdentity(
                        operation.generatedMetaGuid,
                        folderAsset: true);
                if (!HybridCLROutputOwnership.FileIdentityEquals(
                        deterministic,
                        operation.finalMeta))
                {
                    throw new InvalidOperationException(
                        $"HybridCLR journal generated root meta identity is invalid for role '{operation.role}'.");
                }
            }
            else if (!string.IsNullOrEmpty(operation.generatedMetaGuid)
                     || !HybridCLROutputOwnership.FileIdentityEquals(
                         operation.initialMeta,
                         operation.finalMeta))
            {
                throw new InvalidOperationException(
                    $"HybridCLR journal did not preserve the initial root meta for role '{operation.role}'.");
            }
        }

        private static void PersistJournal(Journal value, string journalPath, bool createNew)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            string expectedJournal = Path.Combine(value.stateRoot, ActiveJournalFileName);
            if (!FileSystemPathsEqual(expectedJournal, journalPath))
            {
                throw new InvalidOperationException(
                    $"HybridCLR journal path is not the active project journal: '{journalPath}'.");
            }

            EnsureStateRootIsSafe(value.projectRoot, value.stateRoot);
            if (Directory.Exists(journalPath)
                || (File.Exists(journalPath)
                    && (File.GetAttributes(journalPath) & FileAttributes.ReparsePoint) != 0))
            {
                throw new InvalidOperationException(
                    $"HybridCLR active journal path is unsafe: '{journalPath}'.");
            }

            if (value.sequence >= MaximumJournalSequence)
            {
                throw new InvalidOperationException(
                    $"HybridCLR durable journal exceeded its {MaximumJournalSequence}-update safety budget.");
            }

            value.sequence = checked(value.sequence + 1);
            value.checksum = ComputeJournalChecksum(value);
            byte[] bytes = new UTF8Encoding(false).GetBytes(JsonUtility.ToJson(value, true));
            if (bytes.Length <= 0 || bytes.Length > MaximumJournalByteCount)
            {
                throw new InvalidOperationException(
                    $"HybridCLR durable journal exceeds {MaximumJournalByteCount} bytes.");
            }

            if (createNew && File.Exists(journalPath))
            {
                throw new IOException(
                    $"HybridCLR active journal already exists: '{journalPath}'.");
            }

            if (!createNew && !File.Exists(journalPath))
            {
                throw new FileNotFoundException(
                    "HybridCLR active journal disappeared before a durable state update.",
                    journalPath);
            }

            string temporaryPath = CreateJournalTemporaryPath(
                journalPath,
                value.transactionId,
                value.sequence);
            BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                journalPath,
                "HybridCLR durable journal");
            BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                temporaryPath,
                "HybridCLR durable journal temporary file");
            using (var stream = new FileStream(
                       temporaryPath,
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
                File.Move(temporaryPath, journalPath);
            }
            else
            {
                File.Replace(temporaryPath, journalPath, null);
            }
        }

        private static string ComputeJournalChecksum(Journal value)
        {
            var builder = new StringBuilder(2048);
            AppendChecksumValue(builder, value.schemaVersion.ToString(CultureInfo.InvariantCulture));
            AppendChecksumValue(builder, value.sequence.ToString(CultureInfo.InvariantCulture));
            AppendChecksumValue(builder, value.transactionId);
            AppendChecksumValue(builder, value.phase);
            AppendChecksumValue(builder, value.projectRoot);
            AppendChecksumValue(builder, value.stateRoot);
            AppendChecksumValue(builder, value.scratchRoot);
            AppendChecksumValue(builder, value.scratchInitialized ? "1" : "0");
            int count = value.operations == null ? -1 : value.operations.Length;
            AppendChecksumValue(builder, count.ToString(CultureInfo.InvariantCulture));
            if (value.operations != null)
            {
                foreach (JournalOperation operation in value.operations)
                {
                    AppendChecksumValue(builder, operation?.role);
                    AppendChecksumValue(builder, operation?.target);
                    AppendChecksumValue(builder, operation?.targetMeta);
                    AppendChecksumValue(builder, operation?.stage);
                    AppendChecksumValue(builder, operation?.backup);
                    AppendChecksumValue(builder, operation?.stagedMeta);
                    AppendChecksumValue(builder, operation?.recoveryMeta);
                    AppendChecksumValue(builder, operation?.generatedMetaGuid);
                    AppendDirectoryIdentity(builder, operation?.initialDirectory);
                    AppendDirectoryIdentity(builder, operation?.stagedDirectory);
                    AppendFileIdentity(builder, operation?.initialMeta);
                    AppendFileIdentity(builder, operation?.finalMeta);
                    AppendChecksumValue(builder, operation?.state);
                }
            }

            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));
                var result = new StringBuilder(64);
                foreach (byte valueByte in hash)
                {
                    result.Append(valueByte.ToString("x2", CultureInfo.InvariantCulture));
                }

                return result.ToString();
            }
        }

        private static void NormalizeJsonUtilityOptionalIdentities(Journal value)
        {
            if (value?.operations == null)
            {
                return;
            }

            foreach (JournalOperation operation in value.operations)
            {
                if (operation == null)
                {
                    continue;
                }

                if (IsJsonUtilityNullDirectoryIdentity(operation.initialDirectory))
                {
                    operation.initialDirectory = null;
                }

                if (IsJsonUtilityNullDirectoryIdentity(operation.stagedDirectory))
                {
                    operation.stagedDirectory = null;
                }

                if (IsJsonUtilityNullFileIdentity(operation.initialMeta))
                {
                    operation.initialMeta = null;
                }
            }
        }

        private static bool IsJsonUtilityNullDirectoryIdentity(HybridCLRDirectoryIdentity identity)
        {
            return identity != null
                && string.IsNullOrEmpty(identity.kind)
                && string.IsNullOrEmpty(identity.transactionId)
                && identity.fileCount == 0
                && identity.totalSize == 0
                && identity.manifestSize == 0
                && string.IsNullOrEmpty(identity.manifestSha256)
                && string.IsNullOrEmpty(identity.treeSha256);
        }

        private static bool IsJsonUtilityNullFileIdentity(HybridCLRFileIdentity identity)
        {
            return identity != null
                && identity.size == 0
                && string.IsNullOrEmpty(identity.sha256);
        }

        private static void AppendDirectoryIdentity(
            StringBuilder builder,
            HybridCLRDirectoryIdentity identity)
        {
            if (identity == null)
            {
                AppendChecksumValue(builder, null);
                return;
            }

            AppendChecksumValue(builder, identity.kind);
            AppendChecksumValue(builder, identity.transactionId);
            AppendChecksumValue(builder, identity.fileCount.ToString(CultureInfo.InvariantCulture));
            AppendChecksumValue(builder, identity.totalSize.ToString(CultureInfo.InvariantCulture));
            AppendChecksumValue(builder, identity.manifestSize.ToString(CultureInfo.InvariantCulture));
            AppendChecksumValue(builder, identity.manifestSha256);
            AppendChecksumValue(builder, identity.treeSha256);
        }

        private static void AppendFileIdentity(
            StringBuilder builder,
            HybridCLRFileIdentity identity)
        {
            if (identity == null)
            {
                AppendChecksumValue(builder, null);
                return;
            }

            AppendChecksumValue(builder, identity.size.ToString(CultureInfo.InvariantCulture));
            AppendChecksumValue(builder, identity.sha256);
        }

        private static void AppendChecksumValue(StringBuilder builder, string value)
        {
            if (value == null)
            {
                builder.Append("-1:");
                return;
            }

            builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(value);
        }

        private static void EnsureNoDetachedState(string stateRoot, string expectedTransactionId)
        {
            if (!Directory.Exists(stateRoot))
            {
                return;
            }

            int entryCount = 0;
            foreach (string entry in Directory.EnumerateFileSystemEntries(
                         stateRoot,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                entryCount++;
                if (entryCount > MaximumStateRootEntryCount)
                {
                    throw new InvalidOperationException(
                        $"HybridCLR transaction state exceeds its entry budget: '{stateRoot}'.");
                }

                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(
                        $"HybridCLR transaction state contains a reparse point: '{entry}'.");
                }

                string name = Path.GetFileName(entry);
                if ((attributes & FileAttributes.Directory) == 0)
                {
                    if (name == LockFileName
                        || name == ActiveJournalFileName
                        || name.StartsWith(JournalTemporaryPrefix, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    throw new InvalidOperationException(
                        $"Detached HybridCLR transaction file requires manual inspection: '{entry}'.");
                }

                if (!HybridCLROutputOwnership.IsTransactionId(name)
                    || string.IsNullOrEmpty(expectedTransactionId)
                    || !string.Equals(name, expectedTransactionId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Detached HybridCLR transaction directory requires manual inspection: '{entry}'.");
                }
            }

            if (string.IsNullOrEmpty(expectedTransactionId)
                && Directory.EnumerateFiles(
                    stateRoot,
                    JournalTemporaryPrefix + "*",
                    SearchOption.TopDirectoryOnly).Any())
            {
                throw new InvalidOperationException(
                    $"Detached HybridCLR journal candidates require manual inspection: '{stateRoot}'.");
            }
        }

        private static void ValidateScratchMetaFiles(JournalOperation operation)
        {
            if (operation.initialMeta != null)
            {
                HybridCLROutputOwnership.RequireFileIdentity(
                    operation.recoveryMeta,
                    operation.initialMeta,
                    $"root meta recovery copy for role '{operation.role}'");
                RequireAbsent(operation.stagedMeta, "unexpected staged root meta");
            }
            else
            {
                HybridCLROutputOwnership.RequireFileIdentity(
                    operation.stagedMeta,
                    operation.finalMeta,
                    $"generated root meta stage for role '{operation.role}'");
                RequireAbsent(operation.recoveryMeta, "unexpected root meta recovery copy");
            }
        }

        private static void ValidateScratchForCleanup(Journal value)
        {
            if (!Directory.Exists(value.scratchRoot))
            {
                return;
            }

            if ((File.GetAttributes(value.scratchRoot) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"HybridCLR scratch cannot be a reparse point: '{value.scratchRoot}'.");
            }

            var allowed = new Dictionary<string, Action>(FileSystemPathComparer);
            foreach (JournalOperation operation in value.operations)
            {
                allowed.Add(operation.stage, () =>
                {
                    if (operation.stagedDirectory != null)
                    {
                        RequireDirectoryAt(
                            operation.stage,
                            operation.role,
                            operation.stagedDirectory,
                            "scratch stage before cleanup");
                    }
                    else
                    {
                        ValidatePartialStage(operation, value.transactionId);
                    }
                });
                allowed.Add(operation.stagedMeta, () =>
                    HybridCLROutputOwnership.RequireFileIdentity(
                        operation.stagedMeta,
                        operation.finalMeta,
                        $"scratch staged root meta for role '{operation.role}'"));
                if (operation.initialMeta != null)
                {
                    allowed.Add(operation.recoveryMeta, () =>
                        HybridCLROutputOwnership.RequireFileIdentity(
                            operation.recoveryMeta,
                            operation.initialMeta,
                            $"scratch root meta recovery for role '{operation.role}'"));
                }
            }

            foreach (string entry in Directory.EnumerateFileSystemEntries(
                         value.scratchRoot,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                string full = Path.GetFullPath(entry);
                if (!allowed.TryGetValue(full, out Action validator))
                {
                    throw new InvalidOperationException(
                        $"HybridCLR scratch contains an unowned entry: '{entry}'.");
                }

                validator();
            }
        }

        private static void ValidatePartialStage(JournalOperation operation, string transactionId)
        {
            if (!Directory.Exists(operation.stage))
            {
                return;
            }

            string manifest = Path.Combine(
                operation.stage,
                HybridCLROutputOwnership.ManifestFileName);
            if (File.Exists(manifest))
            {
                HybridCLRDirectoryIdentity identity =
                    HybridCLROutputOwnership.CaptureDirectory(
                        operation.stage,
                        operation.role);
                if (identity == null
                    || identity.kind != HybridCLROutputOwnership.OwnedDirectoryKind
                    || !string.Equals(identity.transactionId, transactionId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"HybridCLR partial stage ownership is invalid for role '{operation.role}'.");
                }

                return;
            }

            int count = 0;
            foreach (string entry in Directory.EnumerateFileSystemEntries(
                         operation.stage,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                count++;
                if (count > HybridCLROutputOwnership.MaximumArtifactCount)
                {
                    throw new InvalidOperationException(
                        $"HybridCLR partial stage exceeds its entry budget: '{operation.stage}'.");
                }

                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.Directory) != 0
                    || (attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(
                        $"HybridCLR partial stage contains an unsafe entry: '{entry}'.");
                }

                HybridCLROutputOwnership.ValidateManagedFileName(
                    Path.GetFileName(entry),
                    allowMeta: false);
                HybridCLROutputOwnership.CaptureRequiredFileIdentity(
                    entry,
                    "partial staged artifact");
            }
        }

        private static void DeleteCleanupTree(string path, string approvedAnchor)
        {
            if (File.Exists(path))
            {
                throw new InvalidOperationException(
                    $"HybridCLR cleanup tree became a file: '{path}'.");
            }

            if (!Directory.Exists(path))
            {
                return;
            }

            string fullPath = Path.GetFullPath(path);
            string anchor = Path.GetFullPath(approvedAnchor);
            bool isScratch = FileSystemPathsEqual(Path.GetDirectoryName(fullPath), anchor);
            bool isSiblingBackup = FileSystemPathsEqual(Path.GetDirectoryName(fullPath), Path.GetDirectoryName(anchor))
                && Path.GetFileName(fullPath).StartsWith(
                    ".buildpipeline-hybridclr-",
                    StringComparison.Ordinal);
            if (!isScratch && !isSiblingBackup)
            {
                throw new InvalidOperationException(
                    $"Refusing to delete an unexpected HybridCLR cleanup tree: '{fullPath}'.");
            }

            EnsureTreeContainsNoReparsePoints(fullPath);
            Directory.Delete(fullPath, recursive: true);
            if (Directory.Exists(fullPath))
            {
                throw new IOException(
                    $"Failed to delete HybridCLR cleanup tree: '{fullPath}'.");
            }
        }

        private static void EnsureTreeContainsNoReparsePoints(string root)
        {
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"HybridCLR cleanup tree is a reparse point: '{root}'.");
            }

            var pending = new Stack<string>();
            pending.Push(root);
            int count = 0;
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    count++;
                    if (count > MaximumScratchEntryCount)
                    {
                        throw new InvalidOperationException(
                            $"HybridCLR cleanup exceeds its entry budget: '{root}'.");
                    }

                    FileAttributes attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidOperationException(
                            $"HybridCLR cleanup tree contains a reparse point: '{entry}'.");
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        pending.Push(entry);
                    }
                }
            }
        }

        private static void RequireDirectoryAt(
            string path,
            string role,
            HybridCLRDirectoryIdentity expected,
            string description)
        {
            if (expected == null)
            {
                RequireAbsent(path, description);
                return;
            }

            HybridCLROutputOwnership.RequireDirectoryIdentity(
                path,
                role,
                expected,
                description);
        }

        private static void RequireAbsent(string path, string description)
        {
            if (Directory.Exists(path) || File.Exists(path))
            {
                throw new InvalidOperationException(
                    $"HybridCLR {description} must be absent: '{path}'.");
            }
        }

        private static void TriggerCrashCheckpoint(
            Func<CrashCheckpoint, string, bool> crashPredicate,
            CrashCheckpoint checkpoint,
            string role)
        {
            if (crashPredicate != null && crashPredicate(checkpoint, role))
            {
                throw new SimulatedProcessCrashException(checkpoint, role);
            }
        }

        private static void CleanupJournalTemporaryFiles(
            string projectRoot,
            string stateRoot,
            string journalPath,
            string transactionId)
        {
            EnsureStateRootIsSafe(projectRoot, stateRoot);
            if (!FileSystemPathsEqual(
                    Path.Combine(stateRoot, ActiveJournalFileName),
                    journalPath))
            {
                throw new InvalidOperationException(
                    $"HybridCLR temporary journal cleanup received an unexpected path: '{journalPath}'.");
            }

            string[] files = Directory
                .EnumerateFiles(stateRoot, JournalTemporaryPrefix + "*", SearchOption.TopDirectoryOnly)
                .Take(MaximumJournalTemporaryFileCount + 1)
                .ToArray();
            if (files.Length > MaximumJournalTemporaryFileCount)
            {
                throw new InvalidOperationException(
                    $"HybridCLR transaction has more than {MaximumJournalTemporaryFileCount} journal candidates.");
            }

            foreach (string file in files)
            {
                if (!FileSystemPathsEqual(Path.GetDirectoryName(file), stateRoot)
                    || !TryParseTemporaryJournalFileName(
                        Path.GetFileName(file),
                        transactionId,
                        out _)
                    || (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(
                        $"Refusing to delete an unexpected HybridCLR journal candidate: '{file}'.");
                }

                File.Delete(file);
            }
        }

        private static void DeleteJournalFile(
            string projectRoot,
            string stateRoot,
            string journalPath)
        {
            EnsureStateRootIsSafe(projectRoot, stateRoot);
            if (!FileSystemPathsEqual(
                    Path.Combine(stateRoot, ActiveJournalFileName),
                    journalPath)
                || Directory.Exists(journalPath))
            {
                throw new InvalidOperationException(
                    $"Refusing to delete an unexpected HybridCLR journal: '{journalPath}'.");
            }

            if (!File.Exists(journalPath))
            {
                return;
            }

            if ((File.GetAttributes(journalPath) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"Refusing to delete a redirected HybridCLR journal: '{journalPath}'.");
            }

            File.Delete(journalPath);
            if (File.Exists(journalPath))
            {
                throw new IOException(
                    $"Failed to delete HybridCLR durable journal: '{journalPath}'.");
            }
        }

        private static string CreateJournalTemporaryPath(
            string journalPath,
            string transactionId,
            long sequence)
        {
            return journalPath
                + ".tmp-"
                + transactionId
                + "-"
                + sequence.ToString(CultureInfo.InvariantCulture)
                + "-"
                + Guid.NewGuid().ToString("N");
        }

        private static bool TryParseTemporaryJournalFileName(
            string fileName,
            string transactionId,
            out long sequence)
        {
            sequence = 0;
            if (!HybridCLROutputOwnership.IsTransactionId(transactionId)
                || string.IsNullOrEmpty(fileName))
            {
                return false;
            }

            string prefix = JournalTemporaryPrefix + transactionId + "-";
            if (!fileName.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            string suffix = fileName.Substring(prefix.Length);
            int separator = suffix.IndexOf('-');
            if (separator <= 0
                || !long.TryParse(
                    suffix.Substring(0, separator),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out sequence)
                || sequence <= 0)
            {
                return false;
            }

            string candidateId = suffix.Substring(separator + 1);
            return candidateId.Length == 32 && Guid.TryParseExact(candidateId, "N", out _);
        }

        private static bool IsKnownPhase(string value)
        {
            return value == PreparedPhase
                || value == CommittingPhase
                || value == RollingBackPhase
                || value == RolledBackPhase
                || value == CommittedPhase
                || value == CleaningCommittedPhase
                || value == CleaningRolledBackPhase;
        }

        private static bool IsKnownOperationState(string value)
        {
            return value == PreparedState
                || value == StagedState
                || value == BackupMovePendingState
                || value == BackedUpState
                || value == MetaInstallMovePendingState
                || value == MetaInstalledState
                || value == InstallMovePendingState
                || value == InstalledState
                || value == UninstallMovePendingState
                || value == UninstalledState
                || value == MetaUninstallMovePendingState
                || value == MetaUninstalledState
                || value == RestoreMovePendingState
                || value == DirectoryRestoredState
                || value == MetaRestoreMovePendingState
                || value == RestoredState;
        }

        private static bool IsKnownRole(string role)
        {
            return role == HybridCLRBuilder.HotUpdateOutputRole
                || role == HybridCLRBuilder.AOTOutputRole;
        }

        private static void ValidateTargetLocations(
            string projectRoot,
            IEnumerable<HybridCLROutputTarget> targets)
        {
            foreach (HybridCLROutputTarget target in targets)
            {
                BuildPathPolicy.EnsureGeneratedAssetsDirectory(
                    projectRoot,
                    target.FinalDirectory);
            }
        }

        private static void EnsureStateRootIsSafe(string projectRoot, string stateRoot)
        {
            string project = Path.GetFullPath(projectRoot);
            string expected = GetStateRoot(project);
            if (!FileSystemPathsEqual(expected, stateRoot)
                || !IsStrictDescendant(project, stateRoot)
                || File.Exists(stateRoot))
            {
                throw new InvalidOperationException(
                    $"HybridCLR durable state root is invalid: '{stateRoot}'.");
            }

            string current = Directory.Exists(stateRoot)
                ? stateRoot
                : Path.GetDirectoryName(stateRoot);
            while (!string.IsNullOrEmpty(current) && IsDescendantOrEqual(project, current))
            {
                if (Directory.Exists(current)
                    && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(
                        $"HybridCLR durable state path traverses a reparse point: '{current}'.");
                }

                if (FileSystemPathsEqual(current, project))
                {
                    break;
                }

                current = Path.GetDirectoryName(current);
            }
        }

        private static void EnsureNoReparsePointsInPath(string approvedRoot, string targetPath)
        {
            string root = Path.GetFullPath(approvedRoot);
            string target = Path.GetFullPath(targetPath);
            if (!IsStrictDescendant(root, target))
            {
                throw new InvalidOperationException(
                    $"HybridCLR transaction path escaped its state root: '{target}'.");
            }

            string current = root;
            string relative = target.Substring(TrimTrailingSeparators(root).Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (string segment in relative.Split(
                         new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                if (!Directory.Exists(current) && !File.Exists(current))
                {
                    break;
                }

                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(
                        $"HybridCLR transaction path traverses a reparse point: '{current}'.");
                }
            }
        }

        private static string SanitizeRoleForPath(string role)
        {
            var builder = new StringBuilder(role.Length);
            foreach (char value in role)
            {
                builder.Append(char.IsLetterOrDigit(value) || value == '-' || value == '_'
                    ? value
                    : '_');
            }

            return builder.Length == 0 ? "output" : builder.ToString();
        }

        private static bool HasUtf8Bom(byte[] bytes)
        {
            return bytes.Length >= 3
                && bytes[0] == 0xEF
                && bytes[1] == 0xBB
                && bytes[2] == 0xBF;
        }

        private static readonly StringComparer FileSystemPathComparer =
            Path.DirectorySeparatorChar == '\\'
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;

        private static bool PortablePathsEqual(string left, string right)
        {
            return string.Equals(
                TrimTrailingSeparators(Path.GetFullPath(left)),
                TrimTrailingSeparators(Path.GetFullPath(right)),
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool FileSystemPathsEqual(string left, string right)
        {
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
            {
                return false;
            }

            StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return string.Equals(
                TrimTrailingSeparators(Path.GetFullPath(left)),
                TrimTrailingSeparators(Path.GetFullPath(right)),
                comparison);
        }

        private static bool IsPortableStrictDescendant(string parent, string child)
        {
            string prefix = TrimTrailingSeparators(Path.GetFullPath(parent))
                + Path.DirectorySeparatorChar;
            return Path.GetFullPath(child).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsStrictDescendant(string parent, string child)
        {
            string prefix = TrimTrailingSeparators(Path.GetFullPath(parent))
                + Path.DirectorySeparatorChar;
            StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return Path.GetFullPath(child).StartsWith(prefix, comparison);
        }

        private static bool IsDescendantOrEqual(string parent, string child)
        {
            return PortablePathsEqual(parent, child)
                || IsPortableStrictDescendant(parent, child);
        }

        private static string TrimTrailingSeparators(string path)
        {
            return path.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(HybridCLROutputTransaction));
            }
        }
    }
}
