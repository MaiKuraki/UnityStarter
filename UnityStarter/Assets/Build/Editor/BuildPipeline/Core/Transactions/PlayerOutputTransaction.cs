using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    internal sealed class PlayerOutputSimulatedTerminationException : Exception
    {
        internal PlayerOutputSimulatedTerminationException(string checkpoint)
            : base($"Simulated Player output process termination at checkpoint '{checkpoint}'.")
        {
        }
    }

    /// <summary>
    /// Publishes a complete Player output directory without exposing a partial
    /// build or deleting the last-known-good output before BuildPlayer succeeds.
    /// </summary>
    internal sealed class PlayerOutputTransaction : IBuildDeferredPublication
    {
        internal const string PreparedCheckpoint = "prepared";
        internal const string PrepareJournalWrittenCheckpoint = "prepare-journal-written";
        internal const string PrepareOwnerWrittenCheckpoint = "prepare-owner-written";
        internal const string PrepareStageCreatedCheckpoint = "prepare-stage-created";
        internal const string PrepareAnchorWrittenCheckpoint = "prepare-anchor-written";
        internal const string PreparePayloadCreatedCheckpoint = "prepare-payload-created";
        internal const string ReadyCheckpoint = "ready";
        internal const string BackupMovedCheckpoint = "backup-moved";
        internal const string StageMovedCheckpoint = "stage-moved";
        internal const string StagePromotedCheckpoint = "stage-promoted";
        internal const string BackupDeletedCheckpoint = "backup-deleted";

        private const string PrepareOwnerPendingCheckpoint = "prepare-owner-pending";
        private const string PrepareStagePendingCheckpoint = "prepare-stage-pending";
        private const string PrepareAnchorPendingCheckpoint = "prepare-anchor-pending";
        private const string PreparePayloadPendingCheckpoint = "prepare-payload-pending";

        private const int JournalFormatVersion = 1;
        private const int OwnerFormatVersion = 1;
        private const int CompatibilityIdentityFormatVersion = 1;
        internal const int PlayerPipelineCompatibilityRevision = 1;
        private const string StateRelativePath = ".buildpipeline/transactions/player";
        private const string JournalFileName = "active.json";
        private const string LockFileName = "active.lock";
        private const string PublishedOwnerSuffix = ".buildpipeline-player-owner.json";
        private const string StageAnchorFileName = ".buildpipeline-player-stage-anchor";
        private const string StageRootPrefix = ".bps-";
        private const string BackupRootPrefix = ".bpb-";
        private const int MaximumJournalBytes = 256 * 1024;
        private const int MaximumTreeEntries = 1000000;
        private const int MaximumTreeFiles = 500000;
        private const long MaximumTreeBytes = 256L * 1024L * 1024L * 1024L;
        private const int BufferSize = 64 * 1024;
        private const int PlayerGeneratedChildPathReserve = 48;
        private const int MaximumDirectoryMoveAttempts = 8;
        private const int InitialDirectoryMoveRetryDelayMilliseconds = 50;
        private const int MaximumDirectoryMoveRetryDelayMilliseconds = 2000;
        private const int ErrorAccessDenied = 5;
        private const int ErrorSharingViolation = 32;
        private const int ErrorLockViolation = 33;

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private readonly BuildRequest request;
        private readonly BuildIncrementality incrementality;
        private readonly string transactionId;
        private readonly string stateRoot;
        private readonly string journalPath;
        private readonly string finalRoot;
        private readonly string stageRoot;
        private readonly string stagePayloadRoot;
        private readonly string backupRoot;
        private readonly string stageOwnerPath;
        private readonly string stageAnchorPath;
        private readonly string publishedOwnerPath;
        private readonly string relativeOutputPath;
        private readonly CompatibilityIdentity compatibilityIdentity;
        private readonly Action<string> faultInjector;
        private FileStream lockStream;
        private bool published;
        private bool completed;
        private bool disposed;

        private PlayerOutputTransaction(
            BuildRequest request,
            BuildIncrementality incrementality,
            string playerExtensionFingerprint,
            string transactionId,
            string stateRoot,
            FileStream lockStream,
            Action<string> faultInjector)
        {
            this.request = request;
            this.incrementality = incrementality;
            this.transactionId = transactionId;
            this.stateRoot = stateRoot;
            this.lockStream = lockStream;
            this.faultInjector = faultInjector;
            journalPath = Path.Combine(stateRoot, JournalFileName);
            finalRoot = NormalizeDirectoryPath(request.OutputDirectory);

            string parent = Path.GetDirectoryName(finalRoot);
            if (string.IsNullOrEmpty(parent))
            {
                throw new InvalidOperationException(
                    $"Player output directory must have a parent directory: '{finalRoot}'.");
            }

            string scratchIdentity = GetScratchPathIdentity(finalRoot);
            stageRoot = Path.Combine(
                parent,
                StageRootPrefix + scratchIdentity + "-" + transactionId);
            backupRoot = Path.Combine(
                parent,
                BackupRootPrefix + scratchIdentity + "-" + transactionId);
            stageOwnerPath = stageRoot + ".owner.json";
            stageAnchorPath = Path.Combine(stageRoot, StageAnchorFileName);
            stagePayloadRoot = GetStagePayloadRoot(stageRoot, finalRoot);
            publishedOwnerPath = GetPublishedOwnerPath(finalRoot);
            relativeOutputPath = GetRelativeOutputPath(finalRoot, request.OutputPath);
            compatibilityIdentity = CreateCompatibilityIdentity(
                request,
                finalRoot,
                relativeOutputPath,
                playerExtensionFingerprint);

            ValidateTransactionPathBudgets();
        }

        public string StageOutputPath => relativeOutputPath.Length == 0
            ? stagePayloadRoot
            : Path.Combine(stagePayloadRoot, relativeOutputPath);

        public string Id => BuildStepTypeIds.Player;
        public string RecoveryStateRelativePath => StateRelativePath;

        internal string StageRoot => stageRoot;

        public static PlayerOutputTransaction Begin(
            BuildRequest request,
            BuildIncrementality incrementality,
            string playerExtensionFingerprint)
        {
            return Begin(
                request,
                incrementality,
                playerExtensionFingerprint,
                null);
        }

        internal static PlayerOutputTransaction Begin(
            BuildRequest request,
            BuildIncrementality incrementality,
            string playerExtensionFingerprint,
            Action<string> faultInjector)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            string stateRoot = GetStateRoot(request.ProjectRoot);
            BuildPathPolicy.EnsureWin32MaxDirectoryPathBudget(
                stateRoot,
                "Player transaction state root");
            BuildPathPolicy.EnsureWin32MaxPathBudget(
                Path.Combine(stateRoot, LockFileName),
                "Player transaction lock");
            BuildPathPolicy.EnsureWin32MaxPathBudget(
                Path.Combine(stateRoot, JournalFileName),
                "Player transaction journal",
                ".bak".Length);
            Directory.CreateDirectory(stateRoot);
            FileStream lockStream = null;
            try
            {
                lockStream = new FileStream(
                    Path.Combine(stateRoot, LockFileName),
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    1,
                    FileOptions.WriteThrough);
                if (File.Exists(Path.Combine(stateRoot, JournalFileName)))
                {
                    throw new InvalidOperationException(
                        "A pending Player publication transaction requires explicit workspace recovery before another build can begin.");
                }

                EnsureNoUnjournaledScratch(
                    NormalizeDirectoryPath(request.OutputDirectory));

                string transactionId = Guid.NewGuid().ToString("N");
                var transaction = new PlayerOutputTransaction(
                    request,
                    incrementality,
                    RequirePlayerExtensionFingerprint(playerExtensionFingerprint),
                    transactionId,
                    stateRoot,
                    lockStream,
                    faultInjector);
                lockStream = null;
                try
                {
                    transaction.Prepare();
                    return transaction;
                }
                catch (PlayerOutputSimulatedTerminationException)
                {
                    transaction.AbandonForSimulatedTermination();
                    throw;
                }
                catch (Exception prepareException)
                {
                    Exception cleanupException = null;
                    try
                    {
                        transaction.Dispose();
                    }
                    catch (Exception exception)
                    {
                        cleanupException = exception;
                    }

                    if (cleanupException != null)
                    {
                        throw new AggregateException(
                            "Failed to prepare and recover the Player output transaction.",
                            prepareException,
                            cleanupException);
                    }

                    ExceptionDispatchInfo.Capture(prepareException).Throw();
                    throw;
                }
            }
            catch
            {
                lockStream?.Dispose();
                throw;
            }
        }

        internal static void RecoverPending(string projectRoot)
        {
            string stateRoot = GetStateRoot(projectRoot);
            if (!Directory.Exists(stateRoot))
            {
                return;
            }

            BuildPathPolicy.EnsureWin32MaxPathBudget(
                Path.Combine(stateRoot, LockFileName),
                "Player transaction recovery lock");
            BuildPathPolicy.EnsureWin32MaxPathBudget(
                Path.Combine(stateRoot, JournalFileName),
                "Player transaction recovery journal",
                ".bak".Length);

            using (var stream = new FileStream(
                       Path.Combine(stateRoot, LockFileName),
                       FileMode.OpenOrCreate,
                       FileAccess.ReadWrite,
                       FileShare.None,
                       1,
                       FileOptions.WriteThrough))
            {
                RecoverPendingLocked(projectRoot, stateRoot);
            }
        }

        public void Publish()
        {
            ThrowIfUnavailable();
            if (published)
            {
                throw new InvalidOperationException(
                    "Player output transaction has already published its stage.");
            }

            ValidateStageAnchor(stageAnchorPath, transactionId);
            EnsureStageContainerLayout(stageRoot, finalRoot, requirePayload: true);
            TreeIdentity newIdentity = ComputeTreeIdentity(stagePayloadRoot, null);

            ValidateReplaceableFinalRoot(finalRoot, publishedOwnerPath);
            bool hadOriginal = Directory.Exists(finalRoot);
            bool originalWasOwned = File.Exists(publishedOwnerPath);

            TreeIdentity originalIdentity = hadOriginal
                ? ComputeTreeIdentity(finalRoot, null)
                : null;
            string originalOwnerTransactionId = string.Empty;
            CompatibilityIdentity originalCompatibilityIdentity = null;
            if (originalWasOwned)
            {
                Owner originalOwner = ReadPublishedOwner(publishedOwnerPath);
                ValidatePublishedOwner(originalOwner);
                if (!IdentitiesEqual(originalOwner.identity, originalIdentity))
                {
                    throw new InvalidOperationException(
                        "Original Player output ownership changed before publication.");
                }

                if (incrementality == BuildIncrementality.Incremental
                    && !CompatibilityIdentitiesEqual(
                        originalOwner.compatibilityIdentity,
                        compatibilityIdentity))
                {
                    throw new InvalidOperationException(
                        "Incremental Player output compatibility identity changed after staging. "
                        + "The staged output was not published; run this Player invocation with Clean incrementality.");
                }

                originalOwnerTransactionId = originalOwner.transactionId;
                originalCompatibilityIdentity = originalOwner.compatibilityIdentity;
            }

            if (hadOriginal && !originalWasOwned && originalIdentity.entryCount != 0)
            {
                throw CreateUnownedNonEmptyOutputException(finalRoot, publishedOwnerPath);
            }
            ValidateMappedTreePathBudget(
                stagePayloadRoot,
                finalRoot,
                "Published Player artifact");
            if (hadOriginal)
            {
                ValidateMappedTreePathBudget(
                    finalRoot,
                    backupRoot,
                    "Player backup artifact");
            }

            WriteOwner(
                stageOwnerPath,
                "ready",
                transactionId,
                newIdentity,
                compatibilityIdentity);

            var journal = CreateJournal(
                ReadyCheckpoint,
                hadOriginal,
                originalWasOwned,
                originalOwnerTransactionId,
                originalIdentity,
                originalCompatibilityIdentity,
                newIdentity);
            WriteJournal(journalPath, journal);
            faultInjector?.Invoke(ReadyCheckpoint);

            if (hadOriginal)
            {
                MoveDirectoryWithTransientRetry(
                    finalRoot,
                    backupRoot,
                    "back up the current Player output");
                AssertIdentity(backupRoot, originalIdentity, null, "Player output backup");
            }

            journal.checkpoint = BackupMovedCheckpoint;
            WriteJournal(journalPath, journal);
            faultInjector?.Invoke(BackupMovedCheckpoint);

            MoveDirectoryWithTransientRetry(
                stagePayloadRoot,
                finalRoot,
                "promote the staged Player output");
            faultInjector?.Invoke(StageMovedCheckpoint);
            AssertIdentity(finalRoot, newIdentity, null, "Published Player output");
            WritePublishedOwner(
                publishedOwnerPath,
                transactionId,
                newIdentity,
                compatibilityIdentity,
                originalOwnerTransactionId,
                originalIdentity,
                originalCompatibilityIdentity);
            DeletePromotedStageContainer(stageRoot, finalRoot, transactionId);
            DeleteFileStrict(stageOwnerPath);

            journal.checkpoint = StagePromotedCheckpoint;
            WriteJournal(journalPath, journal);
            faultInjector?.Invoke(StagePromotedCheckpoint);

            published = true;
        }

        public void Complete()
        {
            ThrowIfUnavailable();
            if (!published)
            {
                throw new InvalidOperationException(
                    "Player output transaction must publish before completion.");
            }

            Journal journal = ReadJournal(journalPath);
            ValidateJournal(request.ProjectRoot, journal);
            if (!string.Equals(
                    journal.checkpoint,
                    StagePromotedCheckpoint,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Player output transaction is not awaiting terminal completion.");
            }

            FinishPromotedOutput(
                journal,
                new RecoveryRequest(
                    journal.projectRoot,
                    journal.buildRoot,
                    journal.allowExternalOutput));

            journal.checkpoint = BackupDeletedCheckpoint;
            WriteJournal(journalPath, journal);
            faultInjector?.Invoke(BackupDeletedCheckpoint);
            DeleteFileStrict(journalPath);
            completed = true;
        }

        internal void Commit()
        {
            Publish();
            Complete();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Exception recoveryFailure = null;
            if (!completed)
            {
                try
                {
                    RecoverPendingLocked(request.ProjectRoot, stateRoot);
                    completed = true;
                }
                catch (Exception exception)
                {
                    recoveryFailure = new InvalidOperationException(
                        "Failed to recover the Player output transaction.",
                        exception);
                }
            }

            Exception lockFailure = null;
            try
            {
                lockStream?.Dispose();
            }
            catch (Exception exception)
            {
                lockFailure = new IOException(
                    "Failed to release the Player output transaction lock.",
                    exception);
            }
            finally
            {
                lockStream = null;
            }

            if (recoveryFailure != null && lockFailure != null)
            {
                throw new AggregateException(recoveryFailure, lockFailure);
            }

            if (recoveryFailure != null)
            {
                ExceptionDispatchInfo.Capture(recoveryFailure).Throw();
            }

            if (lockFailure != null)
            {
                throw lockFailure;
            }
        }

        private void Prepare()
        {
            ValidateTransactionPaths(
                request.ProjectRoot,
                request.BuildRoot,
                request.AllowExternalOutput,
                finalRoot,
                stageRoot,
                backupRoot,
                stageOwnerPath,
                transactionId);

            if (Directory.Exists(stageRoot)
                || Directory.Exists(backupRoot)
                || File.Exists(stageRoot)
                || File.Exists(backupRoot)
                || File.Exists(stageOwnerPath))
            {
                throw new IOException(
                    "A Player output transaction scratch path already exists.");
            }

            if (incrementality == BuildIncrementality.Incremental)
            {
                ValidateIncrementalBaseline();
            }
            else
            {
                ValidateReplaceableFinalRoot(finalRoot, publishedOwnerPath);
            }

            var journal = CreateJournal(
                PrepareOwnerPendingCheckpoint,
                false,
                false,
                string.Empty,
                null,
                null,
                null);
            WriteJournal(journalPath, journal);
            faultInjector?.Invoke(PrepareJournalWrittenCheckpoint);

            WriteOwner(
                stageOwnerPath,
                "stage",
                transactionId,
                null,
                compatibilityIdentity);
            faultInjector?.Invoke(PrepareOwnerWrittenCheckpoint);

            journal.checkpoint = PrepareStagePendingCheckpoint;
            WriteJournal(journalPath, journal);
            Directory.CreateDirectory(stageRoot);
            faultInjector?.Invoke(PrepareStageCreatedCheckpoint);

            journal.checkpoint = PrepareAnchorPendingCheckpoint;
            WriteJournal(journalPath, journal);
            WriteStageAnchor(stageAnchorPath, transactionId);
            faultInjector?.Invoke(PrepareAnchorWrittenCheckpoint);

            journal.checkpoint = PreparePayloadPendingCheckpoint;
            WriteJournal(journalPath, journal);
            Directory.CreateDirectory(stagePayloadRoot);
            faultInjector?.Invoke(PreparePayloadCreatedCheckpoint);

            if (incrementality == BuildIncrementality.Incremental
                && Directory.Exists(finalRoot))
            {
                TreeIdentity before = ComputeTreeIdentity(finalRoot, null);
                CopyDirectoryTree(finalRoot, stagePayloadRoot);
                TreeIdentity after = ComputeTreeIdentity(stagePayloadRoot, null);
                AssertIdentityEqual(before, after, "Incremental Player output staging");
            }

            journal.checkpoint = PreparedCheckpoint;
            WriteJournal(journalPath, journal);
            faultInjector?.Invoke(PreparedCheckpoint);
        }

        private Journal CreateJournal(
            string checkpoint,
            bool hadOriginal,
            bool originalWasOwned,
            string originalOwnerTransactionId,
            TreeIdentity originalIdentity,
            CompatibilityIdentity originalCompatibilityIdentity,
            TreeIdentity newIdentity)
        {
            return new Journal
            {
                formatVersion = JournalFormatVersion,
                transactionId = transactionId,
                checkpoint = checkpoint,
                projectRoot = Path.GetFullPath(request.ProjectRoot),
                buildRoot = Path.GetFullPath(request.BuildRoot),
                allowExternalOutput = request.AllowExternalOutput,
                finalRoot = finalRoot,
                stageRoot = stageRoot,
                backupRoot = backupRoot,
                stageOwnerPath = stageOwnerPath,
                hadOriginal = hadOriginal,
                originalWasOwned = originalWasOwned,
                originalOwnerTransactionId = originalOwnerTransactionId,
                hasOriginalIdentity = originalIdentity != null,
                originalIdentity = originalIdentity,
                hasOriginalCompatibilityIdentity = originalCompatibilityIdentity != null,
                originalCompatibilityIdentity = originalCompatibilityIdentity,
                hasNewIdentity = newIdentity != null,
                newIdentity = newIdentity,
                newCompatibilityIdentity = compatibilityIdentity,
                checksum = string.Empty
            };
        }

        private static void RecoverPendingLocked(string projectRoot, string stateRoot)
        {
            string journalPath = Path.Combine(stateRoot, JournalFileName);
            RecoverJournalScratch(journalPath);
            if (!File.Exists(journalPath))
            {
                return;
            }

            Journal journal = ReadJournal(journalPath);
            ValidateJournal(projectRoot, journal);
            RecoverOwnerScratch(journal.stageOwnerPath);
            RecoverOwnerScratch(GetPublishedOwnerPath(journal.finalRoot));
            var recoveryRequest = new RecoveryRequest(
                journal.projectRoot,
                journal.buildRoot,
                journal.allowExternalOutput);

            string stagePayloadRoot = GetStagePayloadRoot(journal.stageRoot, journal.finalRoot);
            bool stageExists = Directory.Exists(stagePayloadRoot);
            bool finalExists = Directory.Exists(journal.finalRoot);
            bool backupExists = Directory.Exists(journal.backupRoot);
            RejectFileInPlaceOfDirectory(journal.stageRoot, "Player stage");
            RejectFileInPlaceOfDirectory(stagePayloadRoot, "Player stage payload");
            RejectFileInPlaceOfDirectory(journal.finalRoot, "Player output");
            RejectFileInPlaceOfDirectory(journal.backupRoot, "Player backup");

            switch (journal.checkpoint)
            {
                case PrepareOwnerPendingCheckpoint:
                case PrepareStagePendingCheckpoint:
                case PrepareAnchorPendingCheckpoint:
                case PreparePayloadPendingCheckpoint:
                case PreparedCheckpoint:
                    if (backupExists)
                    {
                        throw new InvalidOperationException(
                            "Prepared Player transaction unexpectedly contains a backup.");
                    }

                    DeletePreparingStage(journal, recoveryRequest);
                    break;

                case ReadyCheckpoint:
                    if (stageExists)
                    {
                        ValidateReadyStage(journal);
                        if (backupExists && !finalExists)
                        {
                            RestoreOriginal(journal, recoveryRequest);
                        }
                        else if (finalExists && !backupExists && journal.hadOriginal)
                        {
                            AssertIdentity(
                                journal.finalRoot,
                                journal.originalIdentity,
                                null,
                                "Original Player output");
                        }
                        else if (finalExists || backupExists || journal.hadOriginal)
                        {
                            throw new InvalidOperationException(
                                "Ready Player transaction has an inconsistent output/backup layout.");
                        }

                        DeleteReadyStage(journal, recoveryRequest);
                    }
                    else
                    {
                        ResolvePreCheckpointPromotedOutput(
                            projectRoot,
                            journal,
                            recoveryRequest);
                        DeletePromotedStageContainerIfPresent(journal);
                    }
                    break;

                case BackupMovedCheckpoint:
                    if (stageExists)
                    {
                        if (finalExists)
                        {
                            throw new InvalidOperationException(
                                "Backup-moved Player transaction contains both the stage and final output.");
                        }

                        ValidateReadyStage(journal);
                        RestoreOriginal(journal, recoveryRequest);
                        DeleteReadyStage(journal, recoveryRequest);
                    }
                    else
                    {
                        ResolvePreCheckpointPromotedOutput(
                            projectRoot,
                            journal,
                            recoveryRequest);
                        DeletePromotedStageContainerIfPresent(journal);
                    }
                    break;

                case StagePromotedCheckpoint:
                    if (stageExists)
                    {
                        throw new InvalidOperationException(
                            "Promoted Player transaction unexpectedly still contains its stage directory.");
                    }

                    BuildPublicationDecision decision =
                        BuildPublicationBarrier.GetDecision(
                            projectRoot,
                            BuildStepTypeIds.Player,
                            StateRelativePath);
                    switch (decision)
                    {
                        case BuildPublicationDecision.Rollback:
                            RollbackPromotedOutput(journal, recoveryRequest);
                            break;
                        case BuildPublicationDecision.Commit:
                            FinishPromotedOutput(journal, recoveryRequest);
                            break;
                        default:
                            throw new InvalidOperationException(
                                "Promoted Player output has no durable terminal publication decision. " +
                                "Recovery stopped fail-closed without changing the output.");
                    }

                    DeletePromotedStageContainerIfPresent(journal);
                    break;

                case BackupDeletedCheckpoint:
                    if (stageExists)
                    {
                        throw new InvalidOperationException(
                            "Completed Player transaction unexpectedly still contains its stage directory.");
                    }

                    FinishPromotedOutput(journal, recoveryRequest);
                    DeletePromotedStageContainerIfPresent(journal);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unsupported Player output transaction checkpoint: '{journal.checkpoint}'.");
            }

            DeleteFileStrict(journal.stageOwnerPath);
            DeleteFileStrict(journalPath);
        }

        private static void FinishPromotedOutput(
            Journal journal,
            RecoveryRequest request)
        {
            if (!Directory.Exists(journal.finalRoot))
            {
                throw new InvalidOperationException(
                    "The promoted Player output is missing during recovery.");
            }

            AssertIdentity(
                journal.finalRoot,
                journal.newIdentity,
                null,
                "Published Player output");
            string finalOwnerPath = GetPublishedOwnerPath(journal.finalRoot);
            WritePublishedOwner(
                finalOwnerPath,
                journal.transactionId,
                journal.newIdentity,
                journal.newCompatibilityIdentity,
                journal.originalWasOwned
                    ? journal.originalOwnerTransactionId
                    : null,
                journal.hadOriginal ? journal.originalIdentity : null,
                journal.originalWasOwned
                    ? journal.originalCompatibilityIdentity
                    : null);

            if (Directory.Exists(journal.backupRoot))
            {
                if (!journal.hadOriginal)
                {
                    throw new InvalidOperationException(
                        "A Player backup exists even though the journal records no original output.");
                }

                AssertIdentity(
                    journal.backupRoot,
                    journal.originalIdentity,
                    null,
                    "Player output backup");
                DeleteDirectoryStrict(journal.backupRoot, request);
            }
        }

        private static void RestoreOriginal(Journal journal, RecoveryRequest request)
        {
            if (journal.hadOriginal)
            {
                if (!Directory.Exists(journal.backupRoot))
                {
                    throw new InvalidOperationException(
                        "The original Player output backup is missing during rollback.");
                }

                AssertIdentity(
                    journal.backupRoot,
                    journal.originalIdentity,
                    null,
                    "Player output backup");
                if (Directory.Exists(journal.finalRoot) || File.Exists(journal.finalRoot))
                {
                    throw new InvalidOperationException(
                        "Refusing to overwrite an unexpected Player output during rollback.");
                }

                MoveDirectoryWithTransientRetry(
                    journal.backupRoot,
                    journal.finalRoot,
                    "restore the previous Player output");
                AssertIdentity(
                    journal.finalRoot,
                    journal.originalIdentity,
                    null,
                    "Restored Player output");
            }
            else if (Directory.Exists(journal.backupRoot))
            {
                throw new InvalidOperationException(
                    "A Player output backup exists for a transaction that had no original output.");
            }
        }

        private static void MoveDirectoryWithTransientRetry(
            string source,
            string destination,
            string operation)
        {
            int delayMilliseconds = InitialDirectoryMoveRetryDelayMilliseconds;
            for (int attempt = 1; attempt <= MaximumDirectoryMoveAttempts; attempt++)
            {
                try
                {
                    Directory.Move(source, destination);
                    return;
                }
                catch (Exception exception) when (IsTransientWindowsDirectoryMoveFailure(exception))
                {
                    bool sourceStillExists = Directory.Exists(source);
                    bool destinationAppeared = Directory.Exists(destination)
                        || File.Exists(destination);
                    if (!sourceStillExists || destinationAppeared)
                    {
                        throw new IOException(
                            $"Failed to {operation} because the publication layout changed during a transient Windows directory-move failure. " +
                            $"SourceExists={sourceStillExists}, DestinationExists={destinationAppeared}, " +
                            $"Source='{source}', Destination='{destination}'.",
                            exception);
                    }

                    if (attempt == MaximumDirectoryMoveAttempts)
                    {
                        int nativeError = exception.HResult & 0xFFFF;
                        throw new IOException(
                            $"Failed to {operation} after {MaximumDirectoryMoveAttempts} bounded attempts because Windows kept the source directory locked. " +
                            $"NativeError={nativeError}, Source='{source}', Destination='{destination}'. " +
                            "Close processes using the Player output and check antivirus or indexing exclusions for the build root.",
                            exception);
                    }

                    Thread.Sleep(delayMilliseconds);
                    delayMilliseconds = Math.Min(
                        delayMilliseconds * 2,
                        MaximumDirectoryMoveRetryDelayMilliseconds);
                }
            }

            throw new InvalidOperationException("Unreachable Player directory publication state.");
        }

        private static bool IsTransientWindowsDirectoryMoveFailure(Exception exception)
        {
            if (Path.DirectorySeparatorChar != '\\'
                || (!(exception is IOException) && !(exception is UnauthorizedAccessException)))
            {
                return false;
            }

            int nativeError = exception.HResult & 0xFFFF;
            return nativeError == ErrorAccessDenied
                || nativeError == ErrorSharingViolation
                || nativeError == ErrorLockViolation;
        }

        private static void DeleteReadyStage(
            Journal journal,
            RecoveryRequest request)
        {
            if (!Directory.Exists(journal.stageRoot))
            {
                if (File.Exists(journal.stageOwnerPath))
                {
                    Owner detachedOwner = ReadOwner(journal.stageOwnerPath);
                    ValidateOwner(
                        detachedOwner,
                        journal.transactionId,
                        "ready",
                        journal.newIdentity,
                        journal.newCompatibilityIdentity);
                    DeleteFileStrict(journal.stageOwnerPath);
                }

                return;
            }

            Owner owner = ReadOwner(journal.stageOwnerPath);
            ValidateOwner(
                owner,
                journal.transactionId,
                "ready",
                journal.newIdentity,
                journal.newCompatibilityIdentity);
            ValidateStageAnchor(
                Path.Combine(journal.stageRoot, StageAnchorFileName),
                journal.transactionId);
            EnsureStageContainerLayout(
                journal.stageRoot,
                journal.finalRoot,
                requirePayload: true);
            string payloadRoot = GetStagePayloadRoot(journal.stageRoot, journal.finalRoot);
            AssertIdentity(
                payloadRoot,
                journal.newIdentity,
                null,
                "Player output stage");

            DeleteDirectoryStrict(journal.stageRoot, request);
            DeleteFileStrict(journal.stageOwnerPath);
        }

        private static void RollbackPromotedOutput(
            Journal journal,
            RecoveryRequest request,
            bool allowPreOwnerRewriteCheckpoint = false)
        {
            if (!Directory.Exists(journal.finalRoot))
            {
                throw new InvalidOperationException(
                    "The promoted Player output is missing during coordinated rollback.");
            }

            AssertIdentity(
                journal.finalRoot,
                journal.newIdentity,
                null,
                "Published Player output");
            string ownerPath = GetPublishedOwnerPath(journal.finalRoot);
            if (File.Exists(ownerPath))
            {
                Owner owner = ReadPublishedOwner(ownerPath);
                ValidatePublishedOwner(owner);
                bool isNewOwner = string.Equals(
                                      owner.transactionId,
                                      journal.transactionId,
                                      StringComparison.Ordinal)
                                  && IdentitiesEqual(owner.identity, journal.newIdentity)
                                  && CompatibilityIdentitiesEqual(
                                      owner.compatibilityIdentity,
                                      journal.newCompatibilityIdentity);
                bool isOriginalOwnerBeforeRewrite =
                    allowPreOwnerRewriteCheckpoint
                    && journal.hadOriginal
                    && journal.originalWasOwned
                    && string.Equals(
                        owner.transactionId,
                        journal.originalOwnerTransactionId,
                        StringComparison.Ordinal)
                    && IdentitiesEqual(owner.identity, journal.originalIdentity)
                    && CompatibilityIdentitiesEqual(
                        owner.compatibilityIdentity,
                        journal.originalCompatibilityIdentity);
                if (!isNewOwner && !isOriginalOwnerBeforeRewrite)
                {
                    throw new InvalidOperationException(
                        "Published Player ownership changed before coordinated rollback.");
                }
            }
            else if (!allowPreOwnerRewriteCheckpoint
                     || (journal.hadOriginal && journal.originalWasOwned))
            {
                throw new InvalidOperationException(
                    "Published Player ownership is missing before coordinated rollback.");
            }

            DeleteDirectoryStrict(journal.finalRoot, request);
            DeleteFileStrict(ownerPath);
            RestoreOriginal(journal, request);
            if (journal.hadOriginal && journal.originalWasOwned)
            {
                WriteOwner(
                    ownerPath,
                    "published",
                    journal.originalOwnerTransactionId,
                    journal.originalIdentity,
                    journal.originalCompatibilityIdentity);
            }
        }

        private static void ResolvePreCheckpointPromotedOutput(
            string projectRoot,
            Journal journal,
            RecoveryRequest request)
        {
            BuildPublicationDecision decision = BuildPublicationBarrier.GetDecision(
                projectRoot,
                BuildStepTypeIds.Player,
                StateRelativePath);
            if (decision == BuildPublicationDecision.Rollback)
            {
                RollbackPromotedOutput(
                    journal,
                    request,
                    allowPreOwnerRewriteCheckpoint: true);
                return;
            }

            if (decision == BuildPublicationDecision.Commit)
            {
                throw new InvalidOperationException(
                    "Committed terminal barrier references a Player publication that did not persist its promoted checkpoint.");
            }

            throw new InvalidOperationException(
                "Promoted Player output has no durable terminal publication decision. " +
                "Recovery stopped fail-closed without changing the output.");
        }

        private static void DeletePreparingStage(
            Journal journal,
            RecoveryRequest request)
        {
            bool ownerExists = File.Exists(journal.stageOwnerPath);
            bool stageExists = Directory.Exists(journal.stageRoot);
            bool ownerMayBeAbsent = string.Equals(
                journal.checkpoint,
                PrepareOwnerPendingCheckpoint,
                StringComparison.Ordinal);
            bool stageMayBeAbsent = ownerMayBeAbsent || string.Equals(
                journal.checkpoint,
                PrepareStagePendingCheckpoint,
                StringComparison.Ordinal);

            if (!ownerExists && !ownerMayBeAbsent)
            {
                throw new InvalidOperationException(
                    "Preparing Player stage owner is missing after its durable write-ahead transition.");
            }

            Owner stageOwner = null;
            if (ownerExists)
            {
                stageOwner = ReadOwner(journal.stageOwnerPath);
                string requiredKind = string.Equals(
                    journal.checkpoint,
                    PreparedCheckpoint,
                    StringComparison.Ordinal)
                    ? null
                    : "stage";
                ValidateOwner(
                    stageOwner,
                    journal.transactionId,
                    requiredKind,
                    null,
                    journal.newCompatibilityIdentity);
                if (requiredKind == null
                    && !string.Equals(stageOwner.kind, "stage", StringComparison.Ordinal)
                    && !string.Equals(stageOwner.kind, "ready", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Prepared Player stage owner has an unsupported kind.");
                }
            }

            if (!stageExists)
            {
                if (!stageMayBeAbsent)
                {
                    throw new InvalidOperationException(
                        "Preparing Player stage is missing after its durable write-ahead transition.");
                }

                DeleteFileStrict(journal.stageOwnerPath);
                return;
            }

            if (!ownerExists)
            {
                throw new InvalidOperationException(
                    "Refusing to delete a preparing Player stage without its transaction owner.");
            }

            if (ownerMayBeAbsent)
            {
                throw new InvalidOperationException(
                    "Preparing Player stage exists before its durable stage-pending transition.");
            }

            ValidatePreparingStageLayout(journal);
            if (stageOwner != null && string.Equals(stageOwner.kind, "ready", StringComparison.Ordinal))
            {
                if (!stageOwner.hasIdentity || stageOwner.identity == null)
                {
                    throw new InvalidOperationException(
                        "Ready Player stage pending journal promotion has no output identity.");
                }

                ValidateIdentity(stageOwner.identity);
                AssertIdentity(
                    GetStagePayloadRoot(journal.stageRoot, journal.finalRoot),
                    stageOwner.identity,
                    null,
                    "Ready Player stage pending journal promotion");
            }

            DeleteDirectoryStrict(journal.stageRoot, request);
            DeleteFileStrict(journal.stageOwnerPath);
        }

        private static void ValidatePreparingStageLayout(Journal journal)
        {
            bool anchorMayExist = !string.Equals(
                journal.checkpoint,
                PrepareStagePendingCheckpoint,
                StringComparison.Ordinal);
            bool anchorRequired = string.Equals(
                                      journal.checkpoint,
                                      PreparePayloadPendingCheckpoint,
                                      StringComparison.Ordinal)
                                  || string.Equals(
                                      journal.checkpoint,
                                      PreparedCheckpoint,
                                      StringComparison.Ordinal);
            bool payloadMayExist = anchorRequired;
            bool payloadRequired = string.Equals(
                journal.checkpoint,
                PreparedCheckpoint,
                StringComparison.Ordinal);
            string anchorPath = Path.Combine(journal.stageRoot, StageAnchorFileName);
            string payloadPath = GetStagePayloadRoot(journal.stageRoot, journal.finalRoot);

            foreach (string entry in Directory.EnumerateFileSystemEntries(journal.stageRoot))
            {
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(
                        $"Preparing Player stage contains a reparse-point entry: '{entry}'.");
                }

                if (PathsEqual(entry, anchorPath))
                {
                    if (!anchorMayExist || (attributes & FileAttributes.Directory) != 0)
                    {
                        throw new InvalidOperationException(
                            $"Preparing Player stage has an anchor inconsistent with checkpoint '{journal.checkpoint}'.");
                    }

                    continue;
                }

                if (PathsEqual(entry, payloadPath))
                {
                    if (!payloadMayExist || (attributes & FileAttributes.Directory) == 0)
                    {
                        throw new InvalidOperationException(
                            $"Preparing Player stage has a payload inconsistent with checkpoint '{journal.checkpoint}'.");
                    }

                    continue;
                }

                throw new InvalidOperationException(
                    $"Preparing Player stage contains an unowned entry: '{entry}'.");
            }

            bool anchorExists = File.Exists(anchorPath);
            bool payloadExists = Directory.Exists(payloadPath);
            if (Directory.Exists(anchorPath)
                || File.Exists(payloadPath)
                || (anchorRequired && !anchorExists)
                || (!anchorMayExist && anchorExists)
                || (payloadRequired && !payloadExists)
                || (!payloadMayExist && payloadExists))
            {
                throw new InvalidOperationException(
                    $"Preparing Player stage layout does not match checkpoint '{journal.checkpoint}'.");
            }

            if (anchorExists)
            {
                ValidateStageAnchor(anchorPath, journal.transactionId);
            }
        }

        private static void ValidateReadyStage(Journal journal)
        {
            Owner owner = ReadOwner(journal.stageOwnerPath);
            ValidateOwner(
                owner,
                journal.transactionId,
                "ready",
                journal.newIdentity,
                journal.newCompatibilityIdentity);
            ValidateStageAnchor(
                Path.Combine(journal.stageRoot, StageAnchorFileName),
                journal.transactionId);
            EnsureStageContainerLayout(
                journal.stageRoot,
                journal.finalRoot,
                requirePayload: true);
            AssertIdentity(
                GetStagePayloadRoot(journal.stageRoot, journal.finalRoot),
                journal.newIdentity,
                null,
                "Player output stage");
        }

        private static void ValidateJournal(string projectRoot, Journal journal)
        {
            if (journal == null
                || journal.formatVersion != JournalFormatVersion
                || !IsTransactionId(journal.transactionId)
                || string.IsNullOrWhiteSpace(journal.checkpoint))
            {
                throw new InvalidOperationException(
                    "Player output transaction journal has an unsupported or incomplete format.");
            }

            string actualProject = Path.GetFullPath(projectRoot);
            if (!PathsEqual(actualProject, journal.projectRoot))
            {
                throw new InvalidOperationException(
                    "Player output transaction journal belongs to a different Unity project.");
            }

            ValidateTransactionPaths(
                journal.projectRoot,
                journal.buildRoot,
                journal.allowExternalOutput,
                journal.finalRoot,
                journal.stageRoot,
                journal.backupRoot,
                journal.stageOwnerPath,
                journal.transactionId);
            ValidateCompatibilityIdentity(journal.newCompatibilityIdentity);

            if (journal.checkpoint != PrepareOwnerPendingCheckpoint
                && journal.checkpoint != PrepareStagePendingCheckpoint
                && journal.checkpoint != PrepareAnchorPendingCheckpoint
                && journal.checkpoint != PreparePayloadPendingCheckpoint
                && journal.checkpoint != PreparedCheckpoint)
            {
                if (!journal.hasNewIdentity || journal.newIdentity == null)
                {
                    throw new InvalidOperationException(
                        "Player output transaction journal does not contain the staged output identity.");
                }

                ValidateIdentity(journal.newIdentity);
                if (journal.hadOriginal)
                {
                    if (!journal.hasOriginalIdentity)
                    {
                        throw new InvalidOperationException(
                            "Player output journal does not contain the original output identity.");
                    }

                    ValidateIdentity(journal.originalIdentity);
                    if (!journal.originalWasOwned
                        && journal.originalIdentity.entryCount != 0)
                    {
                        throw new InvalidOperationException(
                            "Player output journal attempts to adopt a non-empty unowned original output.");
                    }
                }
                else if (journal.hasOriginalIdentity)
                {
                    throw new InvalidOperationException(
                        "Player output journal contains an original identity without an original output.");
                }

                if (journal.originalWasOwned && !journal.hadOriginal)
                {
                    throw new InvalidOperationException(
                        "Player output journal records ownership without an original output.");
                }

                if (journal.originalWasOwned)
                {
                    if (!IsTransactionId(journal.originalOwnerTransactionId))
                    {
                        throw new InvalidOperationException(
                            "Player output journal does not contain the original ownership transaction.");
                    }

                    if (!journal.hasOriginalCompatibilityIdentity)
                    {
                        throw new InvalidOperationException(
                            "Player output journal does not contain the original compatibility identity.");
                    }

                    ValidateCompatibilityIdentity(journal.originalCompatibilityIdentity);
                }
                else if (!string.IsNullOrEmpty(journal.originalOwnerTransactionId))
                {
                    throw new InvalidOperationException(
                        "Player output journal contains an ownership transaction for an unowned original output.");
                }

                if (!journal.originalWasOwned
                    && journal.hasOriginalCompatibilityIdentity)
                {
                    throw new InvalidOperationException(
                        "Player output journal contains compatibility identity for an unowned original output.");
                }
            }
            else if (!string.IsNullOrEmpty(journal.originalOwnerTransactionId)
                     || journal.hasOriginalCompatibilityIdentity)
            {
                throw new InvalidOperationException(
                    "Preparing Player output journal unexpectedly contains original ownership state.");
            }
        }

        private static void ValidateTransactionPaths(
            string projectRoot,
            string buildRoot,
            bool allowExternalOutput,
            string finalRoot,
            string stageRoot,
            string backupRoot,
            string stageOwnerPath,
            string transactionId)
        {
            string final = Path.GetFullPath(finalRoot);
            BuildPathPolicy.EnsureSafeDeleteTarget(
                projectRoot,
                final,
                buildRoot,
                allowExternalOutput);
            string parent = Path.GetDirectoryName(final);
            string scratchIdentity = GetScratchPathIdentity(final);
            string expectedStage = Path.Combine(
                parent,
                StageRootPrefix + scratchIdentity + "-" + transactionId);
            string expectedBackup = Path.Combine(
                parent,
                BackupRootPrefix + scratchIdentity + "-" + transactionId);
            if (!PathsEqual(stageRoot, expectedStage)
                || !PathsEqual(backupRoot, expectedBackup)
                || !PathsEqual(stageOwnerPath, expectedStage + ".owner.json"))
            {
                throw new InvalidOperationException(
                    "Player output transaction scratch paths do not match their deterministic ownership contract.");
            }

            BuildPathPolicy.EnsureSafeDeleteTarget(
                projectRoot,
                expectedStage,
                buildRoot,
                allowExternalOutput);
            BuildPathPolicy.EnsureSafeDeleteTarget(
                projectRoot,
                expectedBackup,
                buildRoot,
                allowExternalOutput);

            BuildPathPolicy.EnsureWin32MaxDirectoryPathBudget(
                final,
                "Player output directory",
                1 + PlayerGeneratedChildPathReserve);
            BuildPathPolicy.EnsureWin32MaxDirectoryPathBudget(
                expectedStage,
                "Player transaction stage root");
            string expectedPayload = GetStagePayloadRoot(expectedStage, final);
            BuildPathPolicy.EnsureWin32MaxDirectoryPathBudget(
                expectedPayload,
                "Player transaction stage payload",
                1 + PlayerGeneratedChildPathReserve);
            BuildPathPolicy.EnsureWin32MaxDirectoryPathBudget(
                expectedBackup,
                "Player transaction backup root");
            BuildPathPolicy.EnsureWin32MaxPathBudget(
                expectedStage + ".owner.json",
                "Player transaction stage owner",
                ".bak".Length);
            BuildPathPolicy.EnsureWin32MaxPathBudget(
                Path.Combine(expectedStage, StageAnchorFileName),
                "Player transaction stage anchor");
            BuildPathPolicy.EnsureWin32MaxPathBudget(
                GetPublishedOwnerPath(final),
                "Published Player ownership marker",
                ".bak".Length);
        }

        private static void EnsureNoUnjournaledScratch(string finalRoot)
        {
            string fullRoot = Path.GetFullPath(finalRoot);
            string parent = Path.GetDirectoryName(fullRoot);
            if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
            {
                return;
            }

            string scratchIdentity = GetScratchPathIdentity(fullRoot);
            string stagePrefix = StageRootPrefix + scratchIdentity + "-";
            string backupPrefix = BackupRootPrefix + scratchIdentity + "-";
            foreach (string entry in Directory.EnumerateFileSystemEntries(parent))
            {
                string name = Path.GetFileName(entry);
                if (name.StartsWith(stagePrefix, StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith(backupPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Unjournaled Player output transaction scratch entry requires manual inspection: '{entry}'.");
                }
            }
        }

        private void ValidateTransactionPathBudgets()
        {
            ValidateTransactionPaths(
                request.ProjectRoot,
                request.BuildRoot,
                request.AllowExternalOutput,
                finalRoot,
                stageRoot,
                backupRoot,
                stageOwnerPath,
                transactionId);
            BuildPathPolicy.EnsureWin32MaxPathBudget(
                journalPath,
                "Player transaction journal",
                ".bak".Length);
            BuildPathPolicy.EnsureWin32MaxPathBudget(
                StageOutputPath,
                "Player BuildPlayer staging destination");
        }

        private static void ValidateMappedTreePathBudget(
            string sourceRoot,
            string destinationRoot,
            string displayName)
        {
            BuildPathPolicy.EnsureWin32MaxDirectoryPathBudget(
                destinationRoot,
                displayName + " root");
            foreach (string entry in EnumerateTreeEntries(sourceRoot))
            {
                string relative = GetRelativePath(sourceRoot, entry);
                string destination = Path.Combine(destinationRoot, relative);
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    BuildPathPolicy.EnsureWin32MaxDirectoryPathBudget(
                        destination,
                        displayName);
                }
                else
                {
                    BuildPathPolicy.EnsureWin32MaxPathBudget(
                        destination,
                        displayName);
                }
            }
        }

        private static void CopyDirectoryTree(string sourceRoot, string destinationRoot)
        {
            foreach (string entry in EnumerateTreeEntries(sourceRoot))
            {
                string relative = GetRelativePath(sourceRoot, entry);
                string destination = Path.Combine(destinationRoot, relative);
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    BuildPathPolicy.EnsureWin32MaxDirectoryPathBudget(
                        destination,
                        "Incremental Player staging directory");
                    Directory.CreateDirectory(destination);
                }
                else
                {
                    BuildPathPolicy.EnsureWin32MaxPathBudget(
                        destination,
                        "Incremental Player staging artifact");
                    string parent = Path.GetDirectoryName(destination);
                    if (!string.IsNullOrEmpty(parent))
                    {
                        BuildPathPolicy.EnsureWin32MaxDirectoryPathBudget(
                            parent,
                            "Incremental Player staging directory");
                        Directory.CreateDirectory(parent);
                    }

                    File.Copy(entry, destination, overwrite: false);
                }
            }
        }

        private static TreeIdentity ComputeTreeIdentity(
            string root,
            string excludedRootFileName)
        {
            if (!Directory.Exists(root))
            {
                throw new DirectoryNotFoundException(
                    $"Player output directory does not exist: '{root}'.");
            }

            var entries = new List<TreeEntry>();
            var portableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long totalBytes = 0;
            int fileCount = 0;
            foreach (string path in EnumerateTreeEntries(root))
            {
                string relative = GetRelativePath(root, path).Replace('\\', '/');
                if (!string.IsNullOrEmpty(excludedRootFileName)
                    && relative.IndexOf('/') < 0
                    && string.Equals(relative, excludedRootFileName, StringComparison.Ordinal))
                {
                    continue;
                }

                BuildPathPolicy.ValidatePortableProjectRelativePath(
                    relative,
                    "Player output entry");
                if (!portableNames.Add(relative))
                {
                    throw new InvalidOperationException(
                        $"Player output contains a portable casing collision: '{relative}'.");
                }

                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    entries.Add(new TreeEntry(relative, true, 0, string.Empty));
                    continue;
                }

                FileInfo before = new FileInfo(path);
                long length = before.Length;
                DateTime lastWriteUtc = before.LastWriteTimeUtc;
                string hash = ComputeFileHash(path);
                var after = new FileInfo(path);
                if (after.Length != length || after.LastWriteTimeUtc != lastWriteUtc)
                {
                    throw new IOException(
                        $"Player output file changed while its identity was captured: '{path}'.");
                }

                checked
                {
                    totalBytes += length;
                }

                fileCount++;
                if (fileCount > MaximumTreeFiles || totalBytes > MaximumTreeBytes)
                {
                    throw new InvalidOperationException(
                        "Player output exceeds the configured ownership identity budget.");
                }

                entries.Add(new TreeEntry(relative, false, length, hash));
            }

            entries.Sort((left, right) => StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));
            using (IncrementalHash digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                foreach (TreeEntry entry in entries)
                {
                    string record = entry.IsDirectory
                        ? "D|" + entry.RelativePath + "\n"
                        : "F|" + entry.RelativePath + "|"
                          + entry.Length.ToString(CultureInfo.InvariantCulture) + "|"
                          + entry.Hash + "\n";
                    byte[] bytes = StrictUtf8.GetBytes(record);
                    digest.AppendData(bytes);
                }

                return new TreeIdentity
                {
                    digest = ToHex(digest.GetHashAndReset()),
                    entryCount = entries.Count,
                    fileCount = fileCount,
                    totalBytes = totalBytes
                };
            }
        }

        private static IReadOnlyList<string> EnumerateTreeEntries(string root)
        {
            string fullRoot = Path.GetFullPath(root);
            FileAttributes rootAttributes = File.GetAttributes(fullRoot);
            if ((rootAttributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"Player output root may not be a reparse point: '{fullRoot}'.");
            }

            var pending = new Stack<string>();
            var entries = new List<string>();
            pending.Push(fullRoot);
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    if (entries.Count >= MaximumTreeEntries)
                    {
                        throw new InvalidOperationException(
                            $"Player output contains more than {MaximumTreeEntries} entries.");
                    }

                    FileAttributes attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidOperationException(
                            $"Player output may not contain a reparse-point entry: '{entry}'.");
                    }

                    entries.Add(entry);
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        pending.Push(entry);
                    }
                }
            }

            entries.Sort(StringComparer.Ordinal);
            return entries;
        }

        private static string ComputeFileHash(string path)
        {
            using (var stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       BufferSize,
                       FileOptions.SequentialScan))
            using (SHA256 sha256 = SHA256.Create())
            {
                return ToHex(sha256.ComputeHash(stream));
            }
        }

        private static void AssertIdentity(
            string root,
            TreeIdentity expected,
            string excludedRootFileName,
            string displayName)
        {
            TreeIdentity actual = ComputeTreeIdentity(root, excludedRootFileName);
            AssertIdentityEqual(expected, actual, displayName);
        }

        private static void AssertIdentityEqual(
            TreeIdentity expected,
            TreeIdentity actual,
            string displayName)
        {
            if (!IdentitiesEqual(expected, actual))
            {
                throw new IOException(
                    $"{displayName} identity verification failed.");
            }
        }

        private static bool IdentitiesEqual(TreeIdentity expected, TreeIdentity actual)
        {
            ValidateIdentity(expected);
            ValidateIdentity(actual);
            return string.Equals(expected.digest, actual.digest, StringComparison.Ordinal)
                   && expected.entryCount == actual.entryCount
                   && expected.fileCount == actual.fileCount
                   && expected.totalBytes == actual.totalBytes;
        }

        private static void ValidateIdentity(TreeIdentity identity)
        {
            if (identity == null
                || identity.digest == null
                || identity.digest.Length != 64
                || identity.entryCount < 0
                || identity.fileCount < 0
                || identity.fileCount > identity.entryCount
                || identity.totalBytes < 0
                || identity.entryCount > MaximumTreeEntries
                || identity.fileCount > MaximumTreeFiles
                || identity.totalBytes > MaximumTreeBytes)
            {
                throw new InvalidOperationException(
                    "Player output transaction contains an invalid tree identity.");
            }
        }

        private static void WriteJournal(string path, Journal journal)
        {
            journal.checksum = string.Empty;
            journal.checksum = ComputeTextHash(JsonUtility.ToJson(journal, false));
            WriteJsonAtomically(path, JsonUtility.ToJson(journal, true));
        }

        private static Journal ReadJournal(string path)
        {
            string json = ReadBoundedText(path);
            Journal journal = JsonUtility.FromJson<Journal>(json);
            if (journal == null)
            {
                throw new InvalidOperationException(
                    "Player output transaction journal is not valid JSON.");
            }

            string checksum = journal.checksum;
            journal.checksum = string.Empty;
            string expected = ComputeTextHash(JsonUtility.ToJson(journal, false));
            journal.checksum = checksum;
            if (!string.Equals(checksum, expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Player output transaction journal checksum verification failed.");
            }

            return journal;
        }

        private static void WriteOwner(
            string path,
            string kind,
            string transactionId,
            TreeIdentity identity,
            CompatibilityIdentity compatibilityIdentity)
        {
            ValidateCompatibilityIdentity(compatibilityIdentity);
            var owner = new Owner
            {
                formatVersion = OwnerFormatVersion,
                kind = kind,
                transactionId = transactionId,
                hasIdentity = identity != null,
                identity = identity,
                compatibilityIdentity = compatibilityIdentity,
                checksum = string.Empty
            };
            owner.checksum = ComputeTextHash(JsonUtility.ToJson(owner, false));
            WriteJsonAtomically(path, JsonUtility.ToJson(owner, true));
        }

        private static void WritePublishedOwner(
            string path,
            string transactionId,
            TreeIdentity newIdentity,
            CompatibilityIdentity newCompatibilityIdentity,
            string replaceableTransactionId,
            TreeIdentity replaceableIdentity,
            CompatibilityIdentity replaceableCompatibilityIdentity)
        {
            ValidateCompatibilityIdentity(newCompatibilityIdentity);
            if (File.Exists(path))
            {
                Owner existing = ReadPublishedOwner(path);
                ValidatePublishedOwner(existing);
                bool isCurrentOwner = string.Equals(
                                          existing.transactionId,
                                          transactionId,
                                          StringComparison.Ordinal)
                                      && IdentitiesEqual(existing.identity, newIdentity)
                                      && CompatibilityIdentitiesEqual(
                                          existing.compatibilityIdentity,
                                          newCompatibilityIdentity);
                bool isReplaceableOwner = IsTransactionId(replaceableTransactionId)
                                          && string.Equals(
                                              existing.transactionId,
                                              replaceableTransactionId,
                                              StringComparison.Ordinal)
                                          && replaceableIdentity != null
                                          && IdentitiesEqual(existing.identity, replaceableIdentity)
                                          && replaceableCompatibilityIdentity != null
                                          && CompatibilityIdentitiesEqual(
                                              existing.compatibilityIdentity,
                                              replaceableCompatibilityIdentity);
                if (!isCurrentOwner && !isReplaceableOwner)
                {
                    throw new InvalidOperationException(
                        $"Refusing to replace a Player output ownership marker that changed after transaction preparation: '{path}'.");
                }
            }

            WriteOwner(
                path,
                "published",
                transactionId,
                newIdentity,
                newCompatibilityIdentity);
        }

        private static void ValidatePublishedOwnerFile(string path, string finalRoot)
        {
            Owner owner = ReadPublishedOwner(path);
            ValidatePublishedOwner(owner);
            AssertIdentity(
                finalRoot,
                owner.identity,
                null,
                "Previously published Player output");
        }

        private void ValidateIncrementalBaseline()
        {
            if (!Directory.Exists(finalRoot) || !File.Exists(publishedOwnerPath))
            {
                throw new InvalidOperationException(
                    "Incremental Player builds require a previously published, pipeline-owned output. "
                    + "Run this Player invocation with Clean incrementality before using Incremental. "
                    + $"Output: '{finalRoot}'.");
            }

            ValidatePublishedOwnerFile(publishedOwnerPath, finalRoot);
            Owner owner = ReadPublishedOwner(publishedOwnerPath);
            if (CompatibilityIdentitiesEqual(
                    owner.compatibilityIdentity,
                    compatibilityIdentity))
            {
                return;
            }

            throw new InvalidOperationException(
                "Incremental Player output compatibility identity does not match the current build request. "
                + DescribeCompatibilityMismatch(
                    owner.compatibilityIdentity,
                    compatibilityIdentity)
                + " Run this Player invocation with Clean incrementality to replace the incompatible output. "
                + $"Output: '{finalRoot}'.");
        }

        private static CompatibilityIdentity CreateCompatibilityIdentity(
            BuildRequest request,
            string finalRoot,
            string relativeOutputPath,
            string playerExtensionFingerprint)
        {
            string artifactPath = relativeOutputPath.Length == 0
                ? Path.GetFileName(NormalizeDirectoryPath(finalRoot))
                : relativeOutputPath;
            if (string.IsNullOrWhiteSpace(artifactPath))
            {
                throw new InvalidOperationException(
                    "Player output compatibility identity requires a stable artifact name.");
            }

            var identity = new CompatibilityIdentity
            {
                formatVersion = CompatibilityIdentityFormatVersion,
                playerPipelineCompatibilityRevision =
                    PlayerPipelineCompatibilityRevision,
                unityVersion = Application.unityVersion,
                buildTarget = request.Target.ToString(),
                namedBuildTarget = request.NamedTarget.TargetName,
                scriptingBackend = request.ScriptingBackend.ToString(),
                outputArtifactPath = artifactPath.Replace('\\', '/'),
                outputIsFolder = request.OutputIsFolder,
                companyName = request.CompanyName,
                productName = request.ProductName,
                applicationIdentifier = request.ApplicationIdentifier,
                exportAndroidProject = request.ExportAndroidProject,
                debugBuild = request.DebugBuild,
                deleteDebugFiles = request.DeleteDebugFiles,
                cheatEnabled = request.CheatEnabled,
                playerExtensionFingerprint = RequirePlayerExtensionFingerprint(
                    playerExtensionFingerprint),
                digest = string.Empty
            };
            identity.digest = ComputeCompatibilityDigest(identity);
            ValidateCompatibilityIdentity(identity);
            return identity;
        }

        private static string DescribeCompatibilityMismatch(
            CompatibilityIdentity existing,
            CompatibilityIdentity requested)
        {
            var differences = new List<string>();
            AddCompatibilityDifference(
                differences,
                "PlayerPipelineCompatibilityRevision",
                existing.playerPipelineCompatibilityRevision,
                requested.playerPipelineCompatibilityRevision);
            AddCompatibilityDifference(
                differences,
                "UnityVersion",
                existing.unityVersion,
                requested.unityVersion);
            AddCompatibilityDifference(
                differences,
                "BuildTarget",
                existing.buildTarget,
                requested.buildTarget);
            AddCompatibilityDifference(
                differences,
                "NamedBuildTarget",
                existing.namedBuildTarget,
                requested.namedBuildTarget);
            AddCompatibilityDifference(
                differences,
                "ScriptingBackend",
                existing.scriptingBackend,
                requested.scriptingBackend);
            AddCompatibilityDifference(
                differences,
                "OutputArtifactPath",
                existing.outputArtifactPath,
                requested.outputArtifactPath);
            AddCompatibilityDifference(
                differences,
                "OutputIsFolder",
                existing.outputIsFolder,
                requested.outputIsFolder);
            AddCompatibilityDifference(
                differences,
                "CompanyName",
                existing.companyName,
                requested.companyName);
            AddCompatibilityDifference(
                differences,
                "ProductName",
                existing.productName,
                requested.productName);
            AddCompatibilityDifference(
                differences,
                "ApplicationIdentifier",
                existing.applicationIdentifier,
                requested.applicationIdentifier);
            AddCompatibilityDifference(
                differences,
                "ExportAndroidProject",
                existing.exportAndroidProject,
                requested.exportAndroidProject);
            AddCompatibilityDifference(
                differences,
                "DebugBuild",
                existing.debugBuild,
                requested.debugBuild);
            AddCompatibilityDifference(
                differences,
                "DeleteDebugFiles",
                existing.deleteDebugFiles,
                requested.deleteDebugFiles);
            AddCompatibilityDifference(
                differences,
                "CheatEnabled",
                existing.cheatEnabled,
                requested.cheatEnabled);
            AddCompatibilityDifference(
                differences,
                "PlayerExtensionFingerprint",
                existing.playerExtensionFingerprint,
                requested.playerExtensionFingerprint);
            return differences.Count == 0
                ? "Persisted compatibility data differs."
                : "Changed fields: " + string.Join(", ", differences) + ".";
        }

        private static void AddCompatibilityDifference(
            ICollection<string> differences,
            string fieldName,
            string existing,
            string requested)
        {
            if (!string.Equals(existing, requested, StringComparison.Ordinal))
            {
                differences.Add(
                    $"{fieldName} ('{existing}' -> '{requested}')");
            }
        }

        private static void AddCompatibilityDifference(
            ICollection<string> differences,
            string fieldName,
            int existing,
            int requested)
        {
            if (existing != requested)
            {
                differences.Add(
                    $"{fieldName} ('{existing}' -> '{requested}')");
            }
        }

        private static void AddCompatibilityDifference(
            ICollection<string> differences,
            string fieldName,
            bool existing,
            bool requested)
        {
            if (existing != requested)
            {
                differences.Add(
                    $"{fieldName} ('{existing}' -> '{requested}')");
            }
        }

        private static bool CompatibilityIdentitiesEqual(
            CompatibilityIdentity left,
            CompatibilityIdentity right)
        {
            ValidateCompatibilityIdentity(left);
            ValidateCompatibilityIdentity(right);
            return left.formatVersion == right.formatVersion
                   && left.playerPipelineCompatibilityRevision ==
                   right.playerPipelineCompatibilityRevision
                   && string.Equals(
                       left.unityVersion,
                       right.unityVersion,
                       StringComparison.Ordinal)
                   && string.Equals(left.buildTarget, right.buildTarget, StringComparison.Ordinal)
                   && string.Equals(left.namedBuildTarget, right.namedBuildTarget, StringComparison.Ordinal)
                   && string.Equals(left.scriptingBackend, right.scriptingBackend, StringComparison.Ordinal)
                   && string.Equals(left.outputArtifactPath, right.outputArtifactPath, StringComparison.Ordinal)
                   && left.outputIsFolder == right.outputIsFolder
                   && string.Equals(left.companyName, right.companyName, StringComparison.Ordinal)
                   && string.Equals(left.productName, right.productName, StringComparison.Ordinal)
                   && string.Equals(left.applicationIdentifier, right.applicationIdentifier, StringComparison.Ordinal)
                   && left.exportAndroidProject == right.exportAndroidProject
                   && left.debugBuild == right.debugBuild
                   && left.deleteDebugFiles == right.deleteDebugFiles
                   && left.cheatEnabled == right.cheatEnabled
                   && string.Equals(
                       left.playerExtensionFingerprint,
                       right.playerExtensionFingerprint,
                       StringComparison.Ordinal)
                   && string.Equals(left.digest, right.digest, StringComparison.Ordinal);
        }

        private static void ValidateCompatibilityIdentity(CompatibilityIdentity identity)
        {
            if (identity == null
                || identity.formatVersion != CompatibilityIdentityFormatVersion
                || identity.playerPipelineCompatibilityRevision <= 0
                || string.IsNullOrWhiteSpace(identity.unityVersion)
                || identity.unityVersion.Length > 128
                || string.IsNullOrWhiteSpace(identity.buildTarget)
                || string.IsNullOrWhiteSpace(identity.namedBuildTarget)
                || string.IsNullOrWhiteSpace(identity.scriptingBackend)
                || string.IsNullOrWhiteSpace(identity.outputArtifactPath)
                || Path.IsPathRooted(identity.outputArtifactPath)
                || identity.companyName == null
                || identity.productName == null
                || identity.applicationIdentifier == null
                || identity.playerExtensionFingerprint == null
                || identity.playerExtensionFingerprint.Length != 64
                || identity.digest == null
                || identity.digest.Length != 64)
            {
                throw new InvalidOperationException(
                    "Player output compatibility identity is invalid or unsupported.");
            }

            try
            {
                BuildIdentityPolicy.ValidatePlainText(
                    identity.unityVersion,
                    "Player output compatibility Unity version",
                    128);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidOperationException(
                    "Player output compatibility identity contains an invalid Unity version.",
                    exception);
            }

            BuildPathPolicy.ValidatePortableProjectRelativePath(
                identity.outputArtifactPath,
                "Player output compatibility artifact");
            string expectedDigest = ComputeCompatibilityDigest(identity);
            if (!string.Equals(identity.digest, expectedDigest, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Player output compatibility identity digest verification failed.");
            }
        }

        private static string ComputeCompatibilityDigest(CompatibilityIdentity identity)
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

        private static string RequirePlayerExtensionFingerprint(string fingerprint)
        {
            if (string.IsNullOrWhiteSpace(fingerprint)
                || fingerprint.Length != 64)
            {
                throw new ArgumentException(
                    "Player extension fingerprint must be a 64-character SHA-256 digest.",
                    nameof(fingerprint));
            }

            for (int index = 0; index < fingerprint.Length; index++)
            {
                char character = fingerprint[index];
                if (!((character >= '0' && character <= '9')
                      || (character >= 'a' && character <= 'f')))
                {
                    throw new ArgumentException(
                        "Player extension fingerprint must use lowercase hexadecimal SHA-256 text.",
                        nameof(fingerprint));
                }
            }

            return fingerprint;
        }

        private static void AppendCompatibilityValue(StringBuilder builder, string value)
        {
            string normalized = value ?? string.Empty;
            builder.Append(normalized.Length.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(normalized);
            builder.Append('\n');
        }

        private static void AppendCompatibilityValue(StringBuilder builder, bool value)
        {
            AppendCompatibilityValue(builder, value ? "1" : "0");
        }

        private static void ValidateReplaceableFinalRoot(string finalRoot, string publishedOwnerPath)
        {
            if (File.Exists(finalRoot))
            {
                throw new IOException(
                    $"Player output directory resolves to a file: '{finalRoot}'.");
            }

            bool finalExists = Directory.Exists(finalRoot);
            if (File.Exists(publishedOwnerPath))
            {
                if (!finalExists)
                {
                    throw new InvalidOperationException(
                        $"A detached Player output ownership marker requires manual inspection: '{publishedOwnerPath}'.");
                }

                ValidatePublishedOwnerFile(publishedOwnerPath, finalRoot);
                return;
            }

            if (!finalExists)
            {
                return;
            }

            FileAttributes attributes = File.GetAttributes(finalRoot);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"Player output root may not be a reparse point: '{finalRoot}'.");
            }

            using (IEnumerator<string> entries = Directory.EnumerateFileSystemEntries(finalRoot).GetEnumerator())
            {
                if (entries.MoveNext())
                {
                    throw CreateUnownedNonEmptyOutputException(finalRoot, publishedOwnerPath);
                }
            }
        }

        private static InvalidOperationException CreateUnownedNonEmptyOutputException(
            string finalRoot,
            string publishedOwnerPath)
        {
            return new InvalidOperationException(
                $"Refusing to replace non-empty Player output without a valid ownership marker. "
                + $"Move or clear the directory once, then let a successful build create '{publishedOwnerPath}'. "
                + $"Output: '{finalRoot}'.");
        }

        private static void ValidatePublishedOwner(Owner owner)
        {
            if (owner == null
                || owner.formatVersion != OwnerFormatVersion
                || !string.Equals(owner.kind, "published", StringComparison.Ordinal)
                || !IsTransactionId(owner.transactionId)
                || !owner.hasIdentity
                || owner.identity == null)
            {
                throw new InvalidOperationException(
                    "Player output ownership marker is not a valid published marker.");
            }

            ValidateIdentity(owner.identity);
            ValidateCompatibilityIdentity(owner.compatibilityIdentity);
        }

        private static Owner ReadPublishedOwner(string path)
        {
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"Player output ownership marker may not be a reparse point: '{path}'.");
            }

            return ReadOwner(path);
        }

        private static Owner ReadOwner(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "Player output transaction ownership marker is missing.",
                    path);
            }

            string json = ReadBoundedText(path);
            Owner owner = JsonUtility.FromJson<Owner>(json);
            if (owner == null)
            {
                throw new InvalidOperationException(
                    "Player output ownership marker is not valid JSON.");
            }

            string checksum = owner.checksum;
            owner.checksum = string.Empty;
            string expected = ComputeTextHash(JsonUtility.ToJson(owner, false));
            owner.checksum = checksum;
            if (!string.Equals(checksum, expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Player output ownership marker checksum verification failed.");
            }

            return owner;
        }

        private static void ValidateOwner(
            Owner owner,
            string transactionId,
            string requiredKind,
            TreeIdentity expectedIdentity,
            CompatibilityIdentity expectedCompatibilityIdentity)
        {
            if (owner == null
                || owner.formatVersion != OwnerFormatVersion
                || !string.Equals(owner.transactionId, transactionId, StringComparison.Ordinal)
                || (requiredKind != null
                    && !string.Equals(owner.kind, requiredKind, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    "Player output ownership marker does not match the active transaction.");
            }

            ValidateCompatibilityIdentity(owner.compatibilityIdentity);
            if (!CompatibilityIdentitiesEqual(
                    owner.compatibilityIdentity,
                    expectedCompatibilityIdentity))
            {
                throw new InvalidOperationException(
                    "Player output ownership marker has an incompatible build identity.");
            }

            if (requiredKind == "ready")
            {
                if (!owner.hasIdentity)
                {
                    throw new InvalidOperationException(
                        "Ready Player stage marker does not contain an output identity.");
                }

                AssertIdentityEqual(expectedIdentity, owner.identity, "Player stage owner");
            }
            else if (owner.kind == "stage" && owner.hasIdentity)
            {
                throw new InvalidOperationException(
                    "Unready Player stage marker unexpectedly contains an output identity.");
            }
        }

        private static void WriteJsonAtomically(string path, string json)
        {
            BuildPathPolicy.EnsureWin32MaxPathBudget(
                path,
                "Player transaction JSON",
                ".bak".Length);
            string directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory))
            {
                throw new InvalidOperationException(
                    $"JSON transaction path has no parent directory: '{path}'.");
            }

            BuildPathPolicy.EnsureWin32MaxDirectoryPathBudget(
                directory,
                "Player transaction JSON directory");
            Directory.CreateDirectory(directory);
            string temporaryPath = path + ".tmp";
            string backupPath = path + ".bak";
            BuildPathPolicy.EnsureWin32MaxPathBudget(
                temporaryPath,
                "Player transaction JSON temporary file");
            BuildPathPolicy.EnsureWin32MaxPathBudget(
                backupPath,
                "Player transaction JSON backup file");
            DeleteFileStrict(temporaryPath);
            byte[] bytes = StrictUtf8.GetBytes(json);
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       BufferSize,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }

            if (File.Exists(path))
            {
                DeleteFileStrict(backupPath);
                File.Replace(temporaryPath, path, backupPath);
                DeleteFileStrict(backupPath);
            }
            else
            {
                File.Move(temporaryPath, path);
            }
        }

        private static void RecoverJournalScratch(string journalPath)
        {
            string temporaryPath = journalPath + ".tmp";
            string backupPath = journalPath + ".bak";
            if (!File.Exists(journalPath) && File.Exists(backupPath))
            {
                ReadJournal(backupPath);
                File.Move(backupPath, journalPath);
            }

            if (File.Exists(journalPath))
            {
                ReadJournal(journalPath);
                DeleteFileStrict(temporaryPath);
                DeleteFileStrict(backupPath);
                return;
            }

            if (File.Exists(temporaryPath))
            {
                // The initial journal is written before any output mutation. A
                // lone temporary file therefore has no owned artifact to recover.
                ReadJournal(temporaryPath);
                DeleteFileStrict(temporaryPath);
            }
        }

        private static void RecoverOwnerScratch(string ownerPath)
        {
            string temporaryPath = ownerPath + ".tmp";
            string backupPath = ownerPath + ".bak";
            if (!File.Exists(ownerPath) && File.Exists(backupPath))
            {
                ReadOwner(backupPath);
                File.Move(backupPath, ownerPath);
            }

            if (File.Exists(ownerPath))
            {
                ReadOwner(ownerPath);
                DeleteFileStrict(temporaryPath);
                DeleteFileStrict(backupPath);
                return;
            }

            if (File.Exists(temporaryPath))
            {
                ReadOwner(temporaryPath);
                File.Move(temporaryPath, ownerPath);
            }
        }

        private static string ReadBoundedText(string path)
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= 0 || info.Length > MaximumJournalBytes)
            {
                throw new InvalidOperationException(
                    $"Transaction JSON file is empty or exceeds {MaximumJournalBytes} bytes: '{path}'.");
            }

            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.LongLength != info.Length || bytes.Length > MaximumJournalBytes)
            {
                throw new IOException(
                    $"Transaction JSON file changed while it was read: '{path}'.");
            }

            return StrictUtf8.GetString(bytes);
        }

        private static void DeleteDirectoryStrict(string path, BuildRequest request)
        {
            DeleteDirectoryStrict(
                path,
                new RecoveryRequest(
                    request.ProjectRoot,
                    request.BuildRoot,
                    request.AllowExternalOutput));
        }

        private static void DeleteDirectoryStrict(string path, RecoveryRequest request)
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            BuildPathPolicy.EnsureSafeDeleteDirectoryTree(
                request.ProjectRoot,
                path,
                request.BuildRoot,
                request.AllowExternalOutput);
            Directory.Delete(path, true);
            if (Directory.Exists(path))
            {
                throw new IOException(
                    $"Transaction directory still exists after deletion: '{path}'.");
            }
        }

        private static void DeleteFileStrict(string path)
        {
            if (!File.Exists(path))
            {
                return;
            }

            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"Refusing to delete a transaction file reparse point: '{path}'.");
            }

            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }

            File.Delete(path);
            if (File.Exists(path))
            {
                throw new IOException(
                    $"Transaction file still exists after deletion: '{path}'.");
            }
        }

        private static void WriteStageAnchor(string path, string transactionId)
        {
            byte[] bytes = StrictUtf8.GetBytes(transactionId + "\n");
            using (var stream = new FileStream(
                       path,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bytes.Length,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        private static void ValidateStageAnchor(string path, string transactionId)
        {
            if (!File.Exists(path))
            {
                throw new InvalidOperationException(
                    $"Player stage ownership anchor is missing: '{path}'.");
            }

            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"Player stage ownership anchor may not be a reparse point: '{path}'.");
            }

            byte[] expected = StrictUtf8.GetBytes(transactionId + "\n");
            byte[] actual = File.ReadAllBytes(path);
            if (actual.Length != expected.Length)
            {
                throw new InvalidOperationException(
                    "Player stage ownership anchor does not match the active transaction.");
            }

            for (int index = 0; index < expected.Length; index++)
            {
                if (actual[index] != expected[index])
                {
                    throw new InvalidOperationException(
                        "Player stage ownership anchor does not match the active transaction.");
                }
            }
        }

        private static void EnsureStageContainerLayout(
            string stageRoot,
            string finalRoot,
            bool requirePayload)
        {
            if (!Directory.Exists(stageRoot))
            {
                throw new DirectoryNotFoundException(
                    $"Player stage container is missing: '{stageRoot}'.");
            }

            string payloadRoot = GetStagePayloadRoot(stageRoot, finalRoot);
            string anchorPath = Path.Combine(stageRoot, StageAnchorFileName);
            int entryCount = 0;
            foreach (string entry in Directory.EnumerateFileSystemEntries(stageRoot))
            {
                entryCount++;
                if (!PathsEqual(entry, payloadRoot) && !PathsEqual(entry, anchorPath))
                {
                    throw new InvalidOperationException(
                        $"Player stage container contains an unowned entry: '{entry}'.");
                }

                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(
                        $"Player stage container contains a reparse-point entry: '{entry}'.");
                }
            }

            if (entryCount > 2
                || !File.Exists(anchorPath)
                || (requirePayload && !Directory.Exists(payloadRoot))
                || (!requirePayload && Directory.Exists(payloadRoot)))
            {
                throw new InvalidOperationException(
                    $"Player stage container has an inconsistent ownership layout: '{stageRoot}'.");
            }
        }

        private static void DeletePromotedStageContainerIfPresent(Journal journal)
        {
            if (!Directory.Exists(journal.stageRoot))
            {
                return;
            }

            DeletePromotedStageContainer(
                journal.stageRoot,
                journal.finalRoot,
                journal.transactionId);
        }

        private static void DeletePromotedStageContainer(
            string stageRoot,
            string finalRoot,
            string transactionId)
        {
            ValidateStageAnchor(
                Path.Combine(stageRoot, StageAnchorFileName),
                transactionId);
            EnsureStageContainerLayout(stageRoot, finalRoot, requirePayload: false);
            DeleteFileStrict(Path.Combine(stageRoot, StageAnchorFileName));
            Directory.Delete(stageRoot, recursive: false);
            if (Directory.Exists(stageRoot))
            {
                throw new IOException(
                    $"Promoted Player stage container still exists after deletion: '{stageRoot}'.");
            }
        }

        private static void RejectFileInPlaceOfDirectory(string path, string displayName)
        {
            if (File.Exists(path))
            {
                throw new InvalidOperationException(
                    $"{displayName} resolves to a file: '{path}'.");
            }
        }

        private static string GetStateRoot(string projectRoot)
        {
            return Path.Combine(
                Path.GetFullPath(projectRoot),
                StateRelativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string GetPublishedOwnerPath(string finalRoot)
        {
            return NormalizeDirectoryPath(finalRoot) + PublishedOwnerSuffix;
        }

        private static string GetScratchPathIdentity(string finalRoot)
        {
            string portablePath = NormalizeDirectoryPath(finalRoot)
                .Replace('\\', '/')
                .ToUpperInvariant();
            return ComputeTextHash(portablePath).Substring(0, 12);
        }

        private static string GetStagePayloadRoot(string stageRoot, string finalRoot)
        {
            string leaf = Path.GetFileName(NormalizeDirectoryPath(finalRoot));
            if (string.IsNullOrWhiteSpace(leaf))
            {
                throw new InvalidOperationException(
                    $"Player output directory must have a final path component: '{finalRoot}'.");
            }

            return Path.Combine(Path.GetFullPath(stageRoot), leaf);
        }

        private static string NormalizeDirectoryPath(string path)
        {
            string fullPath = Path.GetFullPath(path);
            string pathRoot = Path.GetPathRoot(fullPath);
            if (!string.IsNullOrEmpty(pathRoot) && PathsEqual(fullPath, pathRoot))
            {
                return pathRoot;
            }

            return fullPath.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        }

        private static string GetRelativeOutputPath(string root, string outputPath)
        {
            string fullRoot = Path.GetFullPath(root);
            string fullOutput = Path.GetFullPath(outputPath);
            if (PathsEqual(fullRoot, fullOutput))
            {
                return string.Empty;
            }

            string prefix = fullRoot.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!fullOutput.StartsWith(prefix, PathComparison))
            {
                throw new InvalidOperationException(
                    $"Player artifact must remain inside its dedicated output directory. Root: '{fullRoot}', artifact: '{fullOutput}'.");
            }

            return fullOutput.Substring(prefix.Length);
        }

        private static string GetRelativePath(string root, string path)
        {
            string fullRoot = Path.GetFullPath(root).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            string prefix = fullRoot + Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(path);
            if (!fullPath.StartsWith(prefix, PathComparison))
            {
                throw new InvalidOperationException(
                    $"Path is outside the Player output root. Root: '{fullRoot}', path: '{fullPath}'.");
            }

            return fullPath.Substring(prefix.Length);
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                PathComparison);
        }

        private static StringComparison PathComparison => Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        private static bool IsTransactionId(string value)
        {
            if (value == null || value.Length != 32)
            {
                return false;
            }

            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (!((character >= '0' && character <= '9')
                      || (character >= 'a' && character <= 'f')))
                {
                    return false;
                }
            }

            return true;
        }

        private static string ComputeTextHash(string value)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                return ToHex(sha256.ComputeHash(StrictUtf8.GetBytes(value)));
            }
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            for (int index = 0; index < bytes.Length; index++)
            {
                builder.Append(bytes[index].ToString("X2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        internal void AbandonForSimulatedTermination()
        {
            disposed = true;
            lockStream?.Dispose();
            lockStream = null;
        }

        private void ThrowIfUnavailable()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(PlayerOutputTransaction));
            }

            if (completed)
            {
                throw new InvalidOperationException(
                    "Player output transaction has already completed.");
            }
        }

        [Serializable]
        private sealed class Journal
        {
            public int formatVersion;
            public string transactionId;
            public string checkpoint;
            public string projectRoot;
            public string buildRoot;
            public bool allowExternalOutput;
            public string finalRoot;
            public string stageRoot;
            public string backupRoot;
            public string stageOwnerPath;
            public bool hadOriginal;
            public bool originalWasOwned;
            public string originalOwnerTransactionId;
            public bool hasOriginalIdentity;
            public TreeIdentity originalIdentity;
            public bool hasOriginalCompatibilityIdentity;
            public CompatibilityIdentity originalCompatibilityIdentity;
            public bool hasNewIdentity;
            public TreeIdentity newIdentity;
            public CompatibilityIdentity newCompatibilityIdentity;
            public string checksum;
        }

        [Serializable]
        private sealed class Owner
        {
            public int formatVersion;
            public string kind;
            public string transactionId;
            public bool hasIdentity;
            public TreeIdentity identity;
            public CompatibilityIdentity compatibilityIdentity;
            public string checksum;
        }

        [Serializable]
        private sealed class CompatibilityIdentity
        {
            public int formatVersion;
            public int playerPipelineCompatibilityRevision;
            public string unityVersion;
            public string buildTarget;
            public string namedBuildTarget;
            public string scriptingBackend;
            public string outputArtifactPath;
            public bool outputIsFolder;
            public string companyName;
            public string productName;
            public string applicationIdentifier;
            public bool exportAndroidProject;
            public bool debugBuild;
            public bool deleteDebugFiles;
            public bool cheatEnabled;
            public string playerExtensionFingerprint;
            public string digest;
        }

        [Serializable]
        private sealed class TreeIdentity
        {
            public string digest;
            public int entryCount;
            public int fileCount;
            public long totalBytes;
        }

        private sealed class TreeEntry
        {
            public TreeEntry(string relativePath, bool isDirectory, long length, string hash)
            {
                RelativePath = relativePath;
                IsDirectory = isDirectory;
                Length = length;
                Hash = hash;
            }

            public string RelativePath { get; }
            public bool IsDirectory { get; }
            public long Length { get; }
            public string Hash { get; }
        }

        private sealed class RecoveryRequest
        {
            public RecoveryRequest(
                string projectRoot,
                string buildRoot,
                bool allowExternalOutput)
            {
                ProjectRoot = projectRoot;
                BuildRoot = buildRoot;
                AllowExternalOutput = allowExternalOutput;
            }

            public string ProjectRoot { get; }
            public string BuildRoot { get; }
            public bool AllowExternalOutput { get; }
        }
    }
}
