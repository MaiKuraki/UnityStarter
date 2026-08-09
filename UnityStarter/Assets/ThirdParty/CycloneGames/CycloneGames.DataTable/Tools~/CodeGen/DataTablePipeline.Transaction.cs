using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CycloneGames.DataTable.CodeGen
{
    internal static partial class Program
    {
        private static partial class DataTablePipeline
        {
            private sealed class RecoveryRequiredException : InvalidOperationException
            {
                public RecoveryRequiredException(string message, Exception? innerException = null)
                    : base(message, innerException)
                {
                }
            }

            private sealed class PublicationSafetyState
            {
                public bool RequiresRecoveryEvidence { get; private set; }

                public void MarkLiveMutationMayStart()
                {
                    RequiresRecoveryEvidence = true;
                }

                public void MarkVerifiedRollbackCompleted()
                {
                    RequiresRecoveryEvidence = false;
                }

                public void MarkTransactionCleanupCompleted()
                {
                    RequiresRecoveryEvidence = false;
                }
            }

            private static void PublishCandidate(
                PipelineTransaction transaction,
                CandidateSnapshot candidate,
                BaselineSnapshot baseline,
                PublicationSafetyState publicationSafety,
                Action<int>? afterApplyOperation = null)
            {
                ValidateTransactionRoots(transaction, "publication staging roots");
                ValidateBaselineUnchanged(transaction.Profile, baseline);
                TransactionJournal journal = BuildJournal(transaction, candidate, baseline);
                ValidateJournal(journal, transaction.RunId);
                ValidateJournalBinding(journal, transaction.Configuration, transaction.Profile);
                PrepareBackups(transaction, journal);
                ValidateBaselineUnchanged(transaction.Profile, baseline);
                WriteJournal(transaction.JournalPath, journal);
                journal.State = JournalState.Publishing.ToString();
                WriteJournal(transaction.JournalPath, journal);
                Console.WriteLine(
                    $"[DataTable.Pipeline] Publishing {journal.Operations.Length} changed operation(s); " +
                    "cancellation is deferred until commit or verified rollback.");
                publicationSafety.MarkLiveMutationMayStart();

                try
                {
                    ApplyOperations(transaction, journal, afterApplyOperation);
                    GenerationReceipt liveReceipt = ReadAndValidateLiveReceipt(transaction.Profile);
                    ValidateLiveOutputs(
                        transaction.Profile,
                        liveReceipt,
                        identity: null,
                        requireCurrentIdentity: false);
                    if (liveReceipt.Generation != candidate.Receipt.Generation)
                    {
                        throw new InvalidOperationException("Committed receipt generation differs from the candidate generation.");
                    }

                    journal.State = JournalState.Committed.ToString();
                    WriteJournal(transaction.JournalPath, journal);
                }
                catch (Exception publishException) when (IsRecoverableException(publishException))
                {
                    try
                    {
                        RollbackOperations(transaction, journal);
                        publicationSafety.MarkVerifiedRollbackCompleted();
                    }
                    catch (Exception rollbackException) when (IsRecoverableException(rollbackException))
                    {
                        journal.State = JournalState.RecoveryRequired.ToString();
                        try
                        {
                            WriteJournal(transaction.JournalPath, journal);
                        }
                        catch (Exception journalException) when (IsRecoverableException(journalException))
                        {
                            throw new RecoveryRequiredException(
                                "Publication failed, rollback could not be verified, and the recovery journal update failed. " +
                                "Preserve the transaction and writer lock. Publish error: " + publishException.Message +
                                " Rollback error: " + rollbackException.Message +
                                " Journal error: " + journalException.Message,
                                rollbackException);
                        }

                        throw new RecoveryRequiredException(
                            "Publication failed and rollback could not be verified. Preserve the transaction and writer lock. " +
                            "Publish error: " + publishException.Message +
                            " Rollback error: " + rollbackException.Message,
                            rollbackException);
                    }

                    throw new InvalidOperationException(
                        "Publication failed; the previous live output was restored and verified. " + publishException.Message,
                        publishException);
                }
            }

            private static TransactionJournal BuildJournal(
                PipelineTransaction transaction,
                CandidateSnapshot candidate,
                BaselineSnapshot baseline)
            {
                var operations = new List<TransactionOperationModel>();
                AddCandidateOperations(
                    transaction,
                    OutputRootKind.Code,
                    candidate.CodeFiles,
                    baseline.CodeFiles,
                    baseline.CodeMetadata,
                    operations);
                AddCandidateOperations(
                    transaction,
                    OutputRootKind.Data,
                    candidate.DataFiles,
                    baseline.DataFiles,
                    baseline.DataMetadata,
                    operations);

                string receiptRelativePath = ReceiptFileName;
                string receiptCandidatePath = ResolveRelativePath(
                    transaction.CandidateCodeRoot,
                    receiptRelativePath,
                    "candidate receipt");
                string candidateReceiptHash = ComputeFileSha256(receiptCandidatePath);
                long candidateReceiptLength = new FileInfo(receiptCandidatePath).Length;
                bool receiptExists = baseline.Receipt != null;
                string previousReceiptHash = baseline.ReceiptSha256;
                if (!receiptExists || previousReceiptHash != candidateReceiptHash)
                {
                    operations.Add(CreateOperation(
                        OutputRootKind.Code,
                        receiptRelativePath,
                        TransactionAction.Write,
                        receiptExists,
                        baseline.ReceiptLength,
                        previousReceiptHash,
                        candidateReceiptLength,
                        candidateReceiptHash));
                }

                var createdDirectories = DetermineCreatedDirectories(transaction, operations);
                return new TransactionJournal
                {
                    RunId = transaction.RunId,
                    Profile = transaction.Profile.Name,
                    ConfigurationSha256 = transaction.Configuration.ConfigurationSha256,
                    CodeOutputRoot = transaction.Profile.CodeOutputRoot,
                    DataOutputRoot = transaction.Profile.DataOutputRoot,
                    Generation = candidate.Receipt.Generation,
                    PreviousGeneration = baseline.Receipt?.Generation ?? string.Empty,
                    PreviousReceiptSha256 = baseline.ReceiptSha256,
                    State = JournalState.Prepared.ToString(),
                    CreatedDirectories = createdDirectories,
                    Operations = operations.ToArray(),
                };
            }

            private static void AddCandidateOperations(
                PipelineTransaction transaction,
                OutputRootKind rootKind,
                Dictionary<string, ReceiptFile> candidateFiles,
                Dictionary<string, ReceiptFile> previousFiles,
                Dictionary<string, ReceiptFile> previousMetadata,
                List<TransactionOperationModel> operations)
            {
                foreach (KeyValuePair<string, ReceiptFile> pair in candidateFiles.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                {
                    if (previousFiles.TryGetValue(pair.Key, out ReceiptFile? previous))
                    {
                        if (!string.Equals(previous.Path, pair.Value.Path, StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException(
                                "A case-only output path transition is not portable and cannot be published atomically: " +
                                previous.Path + " -> " + pair.Value.Path);
                        }

                        if (previous.Sha256 == pair.Value.Sha256)
                        {
                            continue;
                        }
                    }

                    bool hadOriginal = previous != null;
                    operations.Add(CreateOperation(
                        rootKind,
                        pair.Value.Path,
                        TransactionAction.Write,
                        hadOriginal,
                        previous?.Length ?? 0,
                        previous?.Sha256 ?? string.Empty,
                        pair.Value.Length,
                        pair.Value.Sha256));
                }

                foreach (KeyValuePair<string, ReceiptFile> pair in previousFiles.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                {
                    if (candidateFiles.ContainsKey(pair.Key))
                    {
                        continue;
                    }

                    operations.Add(CreateOperation(
                        rootKind,
                        pair.Value.Path,
                        TransactionAction.Delete,
                        hadOriginal: true,
                        previousLength: pair.Value.Length,
                        previousHash: pair.Value.Sha256,
                        candidateLength: 0,
                        candidateHash: string.Empty));
                    string metadataRelative = pair.Value.Path + ".meta";
                    if (previousMetadata.TryGetValue(metadataRelative, out ReceiptFile? metadata))
                    {
                        operations.Add(CreateOperation(
                            rootKind,
                            metadataRelative,
                            TransactionAction.Delete,
                            hadOriginal: true,
                            previousLength: metadata.Length,
                            previousHash: metadata.Sha256,
                            candidateLength: 0,
                            candidateHash: string.Empty));
                    }
                }
            }

            private static TransactionOperationModel CreateOperation(
                OutputRootKind rootKind,
                string relativePath,
                TransactionAction action,
                bool hadOriginal,
                long previousLength,
                string previousHash,
                long candidateLength,
                string candidateHash)
            {
                return new TransactionOperationModel
                {
                    Root = RootKindName(rootKind),
                    Path = relativePath,
                    Action = action.ToString(),
                    HadOriginal = hadOriginal,
                    PreviousLength = previousLength,
                    PreviousSha256 = previousHash,
                    CandidateLength = candidateLength,
                    CandidateSha256 = candidateHash,
                    BackupPath = RootKindName(rootKind) + "/" + relativePath,
                };
            }

            private static string[] DetermineCreatedDirectories(
                PipelineTransaction transaction,
                List<TransactionOperationModel> operations)
            {
                var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (TransactionOperationModel operation in operations)
                {
                    if (!string.Equals(operation.Action, TransactionAction.Write.ToString(), StringComparison.Ordinal))
                    {
                        continue;
                    }

                    OutputRootKind rootKind = ParseRootKind(operation.Root);
                    string outputRoot = GetOutputRoot(transaction.Profile, rootKind);
                    string target = ResolveRelativePath(outputRoot, operation.Path, "publish target");
                    string? directory = Path.GetDirectoryName(target);
                    while (!string.IsNullOrEmpty(directory) && IsStrictPipelineChildPath(outputRoot, directory))
                    {
                        if (Directory.Exists(directory))
                        {
                            break;
                        }

                        string relative = Path.GetRelativePath(outputRoot, directory).Replace('\\', '/');
                        ValidatePortableRelativePath(relative, "created output directory");
                        directories.Add((rootKind == OutputRootKind.Code ? "C:" : "D:") + relative);
                        directory = Path.GetDirectoryName(directory);
                    }

                    if (!Directory.Exists(outputRoot))
                    {
                        directories.Add(rootKind == OutputRootKind.Code ? "C:" : "D:");
                    }
                }

                return directories.OrderBy(static value => value.Length).ThenBy(static value => value, StringComparer.Ordinal).ToArray();
            }

            private static void PrepareBackups(PipelineTransaction transaction, TransactionJournal journal)
            {
                ValidateTransactionRoots(transaction, "backup staging roots");
                foreach (TransactionOperationModel operation in journal.Operations)
                {
                    if (!operation.HadOriginal)
                    {
                        continue;
                    }

                    OutputRootKind rootKind = ParseRootKind(operation.Root);
                    string livePath = ResolveRelativePath(
                        GetOutputRoot(transaction.Profile, rootKind),
                        operation.Path,
                        "backup source");
                    AssertPhysicalContainedPath(
                        livePath,
                        GetOutputRoot(transaction.Profile, rootKind),
                        "backup source",
                        mustExist: true);
                    if (!File.Exists(livePath) || new FileInfo(livePath).Length != operation.PreviousLength ||
                        ComputeFileSha256(livePath) != operation.PreviousSha256)
                    {
                        throw new InvalidOperationException("Live output changed before backup: " + livePath);
                    }

                    string backupPath = ResolveRelativePath(transaction.BackupRoot, operation.BackupPath, "backup output");
                    Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                    AssertPhysicalContainedPath(
                        backupPath,
                        transaction.BackupRoot,
                        "backup output",
                        mustExist: false);
                    File.Copy(livePath, backupPath, overwrite: false);
                    if (new FileInfo(backupPath).Length != operation.PreviousLength ||
                        ComputeFileSha256(backupPath) != operation.PreviousSha256)
                    {
                        throw new InvalidOperationException("Backup verification failed: " + backupPath);
                    }
                }
            }

            private static void ApplyOperations(
                PipelineTransaction transaction,
                TransactionJournal journal,
                Action<int>? afterOperationApplied = null)
            {
                ValidateTransactionRoots(transaction, "publication transaction roots");
                foreach (string directoryEntry in journal.CreatedDirectories)
                {
                    (OutputRootKind rootKind, string relative) = ParseCreatedDirectory(directoryEntry);
                    string root = GetOutputRoot(transaction.Profile, rootKind);
                    string directory = relative.Length == 0
                        ? root
                        : ResolveRelativePath(root, relative, "created output directory");
                    if (File.Exists(directory))
                    {
                        throw new InvalidOperationException("Output directory path is occupied by a file: " + directory);
                    }

                    Directory.CreateDirectory(directory);
                    AssertPhysicalContainedPath(
                        directory,
                        root,
                        "created output directory",
                        mustExist: true);
                }

                for (int operationIndex = 0; operationIndex < journal.Operations.Length; operationIndex++)
                {
                    TransactionOperationModel operation = journal.Operations[operationIndex];
                    OutputRootKind rootKind = ParseRootKind(operation.Root);
                    string outputRoot = GetOutputRoot(transaction.Profile, rootKind);
                    string target = ResolveRelativePath(outputRoot, operation.Path, "publish target");
                    AssertPhysicalContainedPath(target, outputRoot, "publish target", mustExist: false);
                    TransactionAction action = Enum.Parse<TransactionAction>(operation.Action);
                    if (operation.HadOriginal)
                    {
                        if (!File.Exists(target) || new FileInfo(target).Length != operation.PreviousLength ||
                            ComputeFileSha256(target) != operation.PreviousSha256)
                        {
                            throw new InvalidOperationException("Live output changed during publication: " + target);
                        }
                    }
                    else if (File.Exists(target) || Directory.Exists(target))
                    {
                        throw new InvalidOperationException("A new publish target appeared concurrently: " + target);
                    }

                    if (action == TransactionAction.Delete)
                    {
                        File.Delete(target);
                        afterOperationApplied?.Invoke(operationIndex);
                        continue;
                    }

                    string candidate = ResolveRelativePath(
                        GetCandidateRoot(transaction, rootKind),
                        operation.Path,
                        "candidate publish source");
                    AssertPhysicalContainedPath(
                        candidate,
                        GetCandidateRoot(transaction, rootKind),
                        "candidate publish source",
                        mustExist: true);
                    if (!File.Exists(candidate) || new FileInfo(candidate).Length != operation.CandidateLength ||
                        ComputeFileSha256(candidate) != operation.CandidateSha256)
                    {
                        throw new InvalidOperationException("Candidate output changed before publication: " + candidate);
                    }

                    ReplaceFromSource(
                        candidate,
                        target,
                        operation.CandidateLength,
                        operation.CandidateSha256);
                    afterOperationApplied?.Invoke(operationIndex);
                }
            }

            private static void ValidateJournalBinding(
                TransactionJournal journal,
                PipelineConfiguration configuration,
                PipelineProfile profile)
            {
                if (!string.Equals(
                        journal.ConfigurationSha256,
                        configuration.ConfigurationSha256,
                        StringComparison.Ordinal) ||
                    !string.Equals(journal.CodeOutputRoot, profile.CodeOutputRoot, GetPathComparison()) ||
                    !string.Equals(journal.DataOutputRoot, profile.DataOutputRoot, GetPathComparison()))
                {
                    throw new InvalidOperationException(
                        "Recovery journal configuration or output-root identity differs from the current profile.");
                }

                AssertPhysicalContainedPath(
                    configuration.ConfigurationPath,
                    configuration.RepositoryRoot,
                    "journal-bound pipeline configuration",
                    mustExist: true);
                if (!string.Equals(
                        ComputeFileSha256(configuration.ConfigurationPath),
                        configuration.ConfigurationSha256,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Pipeline configuration changed after it was loaded; publication or recovery is refused.");
                }

                AssertPhysicalContainedPath(
                    profile.CodeOutputRoot,
                    configuration.RepositoryRoot,
                    "journal-bound code output root",
                    mustExist: Directory.Exists(profile.CodeOutputRoot));
                AssertPhysicalContainedPath(
                    profile.DataOutputRoot,
                    configuration.RepositoryRoot,
                    "journal-bound data output root",
                    mustExist: Directory.Exists(profile.DataOutputRoot));
            }

            private static void RollbackOperations(PipelineTransaction transaction, TransactionJournal journal)
            {
                ValidateTransactionRoots(transaction, "rollback transaction roots");
                for (int index = journal.Operations.Length - 1; index >= 0; index--)
                {
                    TransactionOperationModel operation = journal.Operations[index];
                    OutputRootKind rootKind = ParseRootKind(operation.Root);
                    string target = ResolveRelativePath(
                        GetOutputRoot(transaction.Profile, rootKind),
                        operation.Path,
                        "rollback target");
                    AssertPhysicalContainedPath(
                        target,
                        GetOutputRoot(transaction.Profile, rootKind),
                        "rollback target",
                        mustExist: false);
                    long currentLength = File.Exists(target) ? new FileInfo(target).Length : 0;
                    if (currentLength > PipelineMaximumFileBytes)
                    {
                        throw new InvalidOperationException("Rollback target exceeds the per-file budget: " + target);
                    }

                    string currentHash = File.Exists(target) ? ComputeFileSha256(target) : string.Empty;
                    if (operation.HadOriginal)
                    {
                        if (currentHash == operation.PreviousSha256)
                        {
                            continue;
                        }

                        if (currentHash.Length != 0 && currentHash != operation.CandidateSha256)
                        {
                            throw new InvalidOperationException(
                                "Rollback refuses an externally changed target: " + target);
                        }

                        string backup = ResolveRelativePath(
                            transaction.BackupRoot,
                            operation.BackupPath,
                            "rollback backup");
                        AssertPhysicalContainedPath(
                            backup,
                            transaction.BackupRoot,
                            "rollback backup",
                            mustExist: true);
                        if (!File.Exists(backup) || new FileInfo(backup).Length != operation.PreviousLength ||
                            ComputeFileSha256(backup) != operation.PreviousSha256)
                        {
                            throw new InvalidOperationException("Rollback backup is missing or corrupt: " + backup);
                        }

                        ReplaceFromSource(
                            backup,
                            target,
                            operation.PreviousLength,
                            operation.PreviousSha256);
                    }
                    else
                    {
                        if (currentHash.Length == 0)
                        {
                            continue;
                        }

                        if (currentHash != operation.CandidateSha256)
                        {
                            throw new InvalidOperationException(
                                "Rollback refuses an unowned new target: " + target);
                        }

                        File.Delete(target);
                    }
                }

                for (int index = journal.CreatedDirectories.Length - 1; index >= 0; index--)
                {
                    (OutputRootKind rootKind, string relative) = ParseCreatedDirectory(journal.CreatedDirectories[index]);
                    string root = GetOutputRoot(transaction.Profile, rootKind);
                    string directory = relative.Length == 0
                        ? root
                        : ResolveRelativePath(root, relative, "rollback directory");
                    if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                    {
                        Directory.Delete(directory, recursive: false);
                    }
                }

                VerifyRollback(transaction, journal);
            }

            private static void VerifyRollback(PipelineTransaction transaction, TransactionJournal journal)
            {
                foreach (TransactionOperationModel operation in journal.Operations)
                {
                    OutputRootKind rootKind = ParseRootKind(operation.Root);
                    string target = ResolveRelativePath(
                        GetOutputRoot(transaction.Profile, rootKind),
                        operation.Path,
                        "rollback verification target");
                    if (operation.HadOriginal)
                    {
                        if (!File.Exists(target) || new FileInfo(target).Length != operation.PreviousLength ||
                            ComputeFileSha256(target) != operation.PreviousSha256)
                        {
                            throw new InvalidOperationException("Rollback verification failed: " + target);
                        }
                    }
                    else if (File.Exists(target) || Directory.Exists(target))
                    {
                        throw new InvalidOperationException("Rollback left a new target behind: " + target);
                    }
                }

                VerifyRestoredBaseline(transaction.Profile, journal);
            }

            private static void VerifyRestoredBaseline(
                PipelineProfile profile,
                TransactionJournal journal)
            {
                if (journal.PreviousGeneration.Length == 0)
                {
                    EnsureUnreceiptedRootsEmpty(profile);
                    return;
                }

                string receiptPath = GetReceiptPath(profile);
                if (!File.Exists(receiptPath) || new FileInfo(receiptPath).Length > PipelineMaximumFileBytes ||
                    ComputeFileSha256(receiptPath) != journal.PreviousReceiptSha256)
                {
                    throw new InvalidOperationException(
                        "Rollback did not restore the exact previous generation receipt.");
                }

                GenerationReceipt receipt = ReadAndValidateLiveReceipt(profile);
                if (receipt.Generation != journal.PreviousGeneration)
                {
                    throw new InvalidOperationException(
                        "Rollback restored a receipt for a different previous generation.");
                }

                ValidateLiveOutputs(profile, receipt, identity: null, requireCurrentIdentity: false);
            }

            private static void ReplaceFromSource(
                string source,
                string target,
                long expectedLength,
                string expectedHash)
            {
                if (!File.Exists(source) || new FileInfo(source).Length != expectedLength)
                {
                    throw new InvalidOperationException("Replacement source length changed: " + source);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                string temporary = target + ".cyclonegames-datatable-stage-" + Guid.NewGuid().ToString("N");
                try
                {
                    using (var sourceStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var targetStream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        sourceStream.CopyTo(targetStream, 64 * 1024);
                        targetStream.Flush(flushToDisk: true);
                    }

                    if (new FileInfo(temporary).Length != expectedLength ||
                        ComputeFileSha256(temporary) != expectedHash)
                    {
                        throw new InvalidOperationException("Staged replacement hash verification failed: " + target);
                    }

                    File.Move(temporary, target, overwrite: true);
                    if (new FileInfo(target).Length != expectedLength || ComputeFileSha256(target) != expectedHash)
                    {
                        throw new InvalidOperationException("Published replacement hash verification failed: " + target);
                    }
                }
                finally
                {
                    if (File.Exists(temporary))
                    {
                        File.Delete(temporary);
                    }
                }
            }

            private static void WriteJournal(string path, TransactionJournal journal)
            {
                ValidateJournal(journal, journal.RunId);
                string content = SerializeState(journal);
                string temporary = path + ".stage";
                if (File.Exists(temporary))
                {
                    throw new InvalidOperationException("Journal staging path already exists: " + temporary);
                }

                WriteDurableText(temporary, content, overwrite: false);
                File.Move(temporary, path, overwrite: true);
                TransactionJournal readBack = ReadState<TransactionJournal>(path, "transaction journal");
                ValidateJournal(readBack, journal.RunId);
                if (!string.Equals(SerializeState(readBack), content, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Transaction journal readback verification failed.");
                }
            }

            private static (OutputRootKind RootKind, string RelativePath) ParseCreatedDirectory(string value)
            {
                OutputRootKind rootKind = value.StartsWith("C:", StringComparison.Ordinal)
                    ? OutputRootKind.Code
                    : value.StartsWith("D:", StringComparison.Ordinal)
                        ? OutputRootKind.Data
                        : throw new InvalidOperationException("Invalid created-directory root marker: " + value);
                string relative = value.Substring(2);
                if (relative.Length != 0)
                {
                    ValidatePortableRelativePath(relative, "created output directory");
                }

                return (rootKind, relative);
            }

            private static int Recover(PipelineConfiguration configuration, string runId)
            {
                string transactionRoot = Path.Combine(configuration.TransactionsRoot, runId);
                string lockOwnerPath = Path.Combine(configuration.LockDirectory, WriterOwnerFileName);
                WriterLockOwner lockOwner = ValidateRecoveryOwnership(configuration, runId, lockOwnerPath);
                try
                {
                    AssertRecoveryProcessesStopped(configuration, lockOwner);
                }
                catch (Exception exception) when (IsRecoverableException(exception))
                {
                    Console.Error.WriteLine(
                        "[DataTable.Pipeline] Recovery remains required and has not touched live output: " +
                        exception.Message);
                    return 3;
                }
                if (!Directory.Exists(transactionRoot))
                {
                    throw new InvalidOperationException("Recovery transaction directory not found: " + transactionRoot);
                }

                string journalPath = Path.Combine(transactionRoot, "journal.json");
                if (!File.Exists(journalPath))
                {
                    DeleteTreeSafe(transactionRoot, configuration.TransactionsRoot);
                    ReleaseRecoveredLock(configuration, lockOwner);
                    Console.WriteLine("[DataTable.Pipeline] Removed a pre-publication transaction with no journal.");
                    return 0;
                }

                TransactionJournal journal = ReadState<TransactionJournal>(journalPath, "transaction journal");
                ValidateJournal(journal, runId);
                JournalState state = Enum.Parse<JournalState>(journal.State);
                try
                {
                    PipelineProfile profile = configuration.GetProfile(journal.Profile);
                    ValidateJournalBinding(journal, configuration, profile);
                    var transaction = new PipelineTransaction(configuration, profile, runId);
                    if (state == JournalState.Committed)
                    {
                        GenerationReceipt receipt = ReadAndValidateLiveReceipt(profile);
                        ValidateLiveOutputs(profile, receipt, identity: null, requireCurrentIdentity: false);
                        if (receipt.Generation != journal.Generation)
                        {
                            throw new InvalidOperationException("Committed generation does not match the recovery journal.");
                        }
                    }
                    else
                    {
                        RollbackOperations(transaction, journal);
                    }
                }
                catch (Exception exception) when (IsRecoverableException(exception))
                {
                    if (state != JournalState.Committed)
                    {
                        journal.State = JournalState.RecoveryRequired.ToString();
                    }

                    try
                    {
                        WriteJournal(journalPath, journal);
                    }
                    catch (Exception journalException) when (IsRecoverableException(journalException))
                    {
                        Console.Error.WriteLine(
                            "[DataTable.Pipeline] Recovery remains required and the journal-state update failed: " +
                            journalException.Message);
                        return 3;
                    }

                    Console.Error.WriteLine("[DataTable.Pipeline] Recovery remains required: " + exception.Message);
                    return 3;
                }

                DeleteTreeSafe(transactionRoot, configuration.TransactionsRoot);
                ReleaseRecoveredLock(configuration, lockOwner);
                Console.WriteLine("[DataTable.Pipeline] Recovery completed and verified for run " + runId + ".");
                return 0;
            }

            private static WriterLockOwner ValidateRecoveryOwnership(
                PipelineConfiguration configuration,
                string runId,
                string ownerPath)
            {
                AssertPhysicalContainedPath(
                    configuration.LockDirectory,
                    configuration.SourceRoot,
                    "recovery writer lock",
                    mustExist: true);
                if (!File.Exists(ownerPath))
                {
                    throw new InvalidOperationException("Recovery writer-lock owner is missing: " + ownerPath);
                }

                foreach (string entry in Directory.EnumerateFileSystemEntries(configuration.LockDirectory))
                {
                    string name = Path.GetFileName(entry);
                    if (name != WriterOwnerFileName && name != CancelRequestFileName &&
                        name != ActiveLubanFileName && name != ActiveLubanPendingFileName &&
                        name != ActiveLubanStageFileName)
                    {
                        throw new InvalidOperationException("Recovery lock contains an unexpected entry: " + entry);
                    }

                    AssertNotReparsePoint(entry, "recovery lock entry");
                }

                WriterLockOwner owner = ReadWriterLockOwner(ownerPath);
                if (!string.Equals(owner.RunId, runId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Writer lock does not belong to recovery run " + runId + ".");
                }

                return owner;
            }

            private static void AssertRecoveryProcessesStopped(
                PipelineConfiguration configuration,
                WriterLockOwner owner)
            {
                AssertRecordedProcessStopped(owner.ProcessIdentity, "the original DataTable writer");
                string pendingPath = Path.Combine(configuration.LockDirectory, ActiveLubanPendingFileName);
                string stagePath = Path.Combine(configuration.LockDirectory, ActiveLubanStageFileName);
                if (File.Exists(pendingPath) || Directory.Exists(pendingPath) ||
                    File.Exists(stagePath) || Directory.Exists(stagePath))
                {
                    throw new InvalidOperationException(
                        "Recovery cannot prove the identity of a Luban process whose launch record is pending or staged. " +
                        "Audit the process tree and lock evidence manually.");
                }

                string activePath = Path.Combine(configuration.LockDirectory, ActiveLubanFileName);
                if (!File.Exists(activePath))
                {
                    if (Directory.Exists(activePath))
                    {
                        throw new InvalidOperationException("Active Luban identity path is not a physical file.");
                    }

                    return;
                }

                ActiveLubanOwner active = ReadActiveLubanOwner(activePath);
                if (active.RunId != owner.RunId || active.Token != owner.Token)
                {
                    throw new InvalidOperationException(
                        "Active Luban identity does not belong to the retained writer-lock owner.");
                }

                AssertRecordedProcessStopped(active.ProcessIdentity, "the recorded Luban process");
            }

            private static void ReleaseRecoveredLock(
                PipelineConfiguration configuration,
                WriterLockOwner expectedOwner)
            {
                string ownerPath = Path.Combine(configuration.LockDirectory, WriterOwnerFileName);
                WriterLockOwner currentOwner = ValidateRecoveryOwnership(
                    configuration,
                    expectedOwner.RunId,
                    ownerPath);
                if (!string.Equals(currentOwner.Content, expectedOwner.Content, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Writer-lock owner changed during recovery.");
                }

                AssertRecoveryProcessesStopped(configuration, currentOwner);
                string activePath = Path.Combine(configuration.LockDirectory, ActiveLubanFileName);
                if (File.Exists(activePath))
                {
                    ActiveLubanOwner active = ReadActiveLubanOwner(activePath);
                    if (active.RunId != currentOwner.RunId || active.Token != currentOwner.Token)
                    {
                        throw new InvalidOperationException("Active Luban identity ownership changed during recovery.");
                    }

                    File.Delete(activePath);
                }

                string cancelPath = Path.Combine(configuration.LockDirectory, CancelRequestFileName);
                if (File.Exists(cancelPath))
                {
                    File.Delete(cancelPath);
                }

                File.Delete(ownerPath);
                Directory.Delete(configuration.LockDirectory, recursive: false);
            }

            private static void DeleteTreeSafe(string root, string approvedParent)
            {
                DeleteTreeSafe(root, approvedParent, PipelineMaximumFiles * 4);
            }

            private static void DeleteTreeSafe(string root, string approvedParent, int maximumEntries)
            {
                if (maximumEntries < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(maximumEntries));
                }

                if (!Directory.Exists(root))
                {
                    return;
                }

                if (!IsStrictPipelineChildPath(approvedParent, root))
                {
                    throw new InvalidOperationException("Refusing to delete outside the approved transaction root: " + root);
                }

                DeleteTreeSafeRecursive(
                    root,
                    depth: 0,
                    refEntryCount: new int[1],
                    maximumEntries);
            }

            private static void DeleteTreeSafeRecursive(
                string directory,
                int depth,
                int[] refEntryCount,
                int maximumEntries)
            {
                if (depth > 128)
                {
                    throw new InvalidOperationException("Transaction cleanup exceeds its bounded traversal budget.");
                }

                AssertNotReparsePoint(directory, "transaction cleanup directory");
                foreach (string childDirectory in Directory.EnumerateDirectories(directory))
                {
                    IncrementCleanupEntry(refEntryCount, maximumEntries);
                    DeleteTreeSafeRecursive(childDirectory, depth + 1, refEntryCount, maximumEntries);
                }

                foreach (string file in Directory.EnumerateFiles(directory))
                {
                    IncrementCleanupEntry(refEntryCount, maximumEntries);
                    AssertNotReparsePoint(file, "transaction cleanup file");
                    File.Delete(file);
                }

                Directory.Delete(directory, recursive: false);
            }

            private static void IncrementCleanupEntry(int[] refEntryCount, int maximumEntries)
            {
                if (refEntryCount[0] >= maximumEntries)
                {
                    throw new InvalidOperationException("Transaction cleanup exceeds its bounded traversal budget.");
                }

                refEntryCount[0]++;
            }
        }
    }
}
