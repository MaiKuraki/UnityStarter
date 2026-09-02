using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using static Build.Pipeline.Integrations.YooAsset3.Publication.PublicationConstants;
namespace Build.Pipeline.Integrations.YooAsset3.Publication
{
    internal static class PublicationCommitCompletion
    {
        internal static Dictionary<PublicationJournalOperation, MetaFileSnapshot> CaptureRefreshRecoveryMetaCandidates(
            PublicationJournal recovered,
            IJournalSerializer serializer)
        {
            var candidates = new Dictionary<PublicationJournalOperation, MetaFileSnapshot>();
            foreach (PublicationJournalOperation operation in recovered.operations)
            {
                if (!string.Equals(operation.state, InstalledState, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Committed YooAsset publication contains a non-installed operation for package '{operation.packageName}'.");
                }

                PublicationJournalValidator.ValidateInstalledPublicationAt(
                    operation,
                    operation.target,
                    recovered.projectRoot,
                    recovered.transactionId,
                        serializer);
                if (!operation.managesSiblingMeta || operation.originalMetaExisted)
                {
                    PublicationMetaGuard.ValidatePreRefreshSiblingMeta(
                        recovered.projectRoot,
                        operation,
                        serializer,
                        allowMissingOriginalMeta: false);
                    continue;
                }

                MetaFileSnapshot candidate = PublicationMetaGuard.CaptureMetaFile(recovered.projectRoot, operation.targetMeta);
                if (candidate.Exists)
                {
                    candidates.Add(operation, candidate);
                }
            }

            return candidates;
        }


        internal static void CompletePendingRefresh(
            PublicationJournal recovered,
            string journalPath,
            Action refreshAssets,
            IJournalSerializer serializer,
            Action<string> checkpoint = null)
        {
            try
            {
                Dictionary<PublicationJournalOperation, MetaFileSnapshot> recoveryCandidates =
                    CaptureRefreshRecoveryMetaCandidates(recovered, serializer);
                if (refreshAssets == null)
                {
                    throw new InvalidOperationException("A refresh callback is required to recover a committed YooAsset publication.");
                }

                checkpoint?.Invoke("CommitRefreshPreRefresh");
                refreshAssets();
                checkpoint?.Invoke("CommitRefreshPostRefresh");
                PublicationMetaGuard.CaptureInstalledSiblingMetas(recovered, serializer, recoveryCandidates);
                recovered.phase = CommittedPhase;
                PublicationJournalStore.WriteJournal(recovered, journalPath, createNew: false, serializer);
                CleanupCommitted(recovered, journalPath, serializer);
            }
            catch (Exception exception)
            {
                throw new CommittedPublicationException(
                    "YooAsset publication files are committed, but AssetDatabase refresh or committed-state cleanup still requires recovery.",
                    journalPath,
                    exception);
            }
        }


        internal static void ValidateCommittedPublications(PublicationJournal recovered, IJournalSerializer serializer)
        {
            foreach (PublicationJournalOperation operation in recovered.operations)
            {
                if (!string.Equals(operation.state, InstalledState, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Committed YooAsset publication contains a non-installed operation for package '{operation.packageName}'.");
                }

                PublicationJournalValidator.ValidateInstalledPublicationAt(
                    operation,
                    operation.target,
                    recovered.projectRoot,
                    recovered.transactionId,
                        serializer);
                PublicationMetaGuard.ValidateInstalledSiblingMeta(recovered, serializer, operation);
            }
        }


        internal static void ValidatePreRefreshCommittedPublications(PublicationJournal recovered, IJournalSerializer serializer)
        {
            foreach (PublicationJournalOperation operation in recovered.operations)
            {
                if (!string.Equals(operation.state, InstalledState, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Committed YooAsset publication contains a non-installed operation for package '{operation.packageName}'.");
                }

                PublicationJournalValidator.ValidateInstalledPublicationAt(
                    operation,
                    operation.target,
                    recovered.projectRoot,
                    recovered.transactionId,
                        serializer);
                PublicationMetaGuard.ValidatePreRefreshSiblingMeta(
                    recovered.projectRoot,
                    operation,
                    serializer,
                    allowMissingOriginalMeta: false);
            }
        }


        internal static void CleanupCommitted(PublicationJournal recovered, string journalPath, IJournalSerializer serializer)
        {
            ValidateCommittedPublications(recovered, serializer);
            foreach (PublicationJournalOperation operation in recovered.operations)
            {
                if (Directory.Exists(operation.stage) || File.Exists(operation.stage))
                {
                    throw new InvalidOperationException(
                        $"Committed publication unexpectedly retained a stage for package '{operation.packageName}': '{operation.stage}'.");
                }

                bool backupExists = Directory.Exists(operation.backup);
                if (File.Exists(operation.backup) || backupExists && !operation.targetInitiallyExisted)
                {
                    throw new InvalidOperationException(
                        $"Committed publication backup state is invalid for package '{operation.packageName}': '{operation.backup}'.");
                }

                if (backupExists)
                {
                    PublicationJournalValidator.ValidateOriginalPublicationAt(operation, operation.backup, recovered.projectRoot, serializer);
                    PublicationSafety.DeleteOwnedDirectory(
                        recovered.projectRoot,
                        operation.approvedRoot,
                        operation.backup);
                    PublicationMetaGuard.DeleteProtectedSiblingMeta(recovered, serializer, operation);
                }
                else
                {
                    PublicationMetaGuard.DeleteProtectedSiblingMetaIfPresent(recovered, serializer, operation);
                }
            }

            PublicationSafety.DeleteOwnedDirectory(
                recovered.projectRoot,
                PublicationPaths.GetStateRoot(recovered.projectRoot, recovered.invocationId),
                recovered.workRoot);
            CleanupOperationMetadata(recovered);
            PublicationSafety.DeleteOwnedFile(
                recovered.projectRoot,
                PublicationPaths.GetStateRoot(recovered.projectRoot, recovered.invocationId),
                journalPath);
            TryDeleteEmptyStateDirectories(
                recovered.projectRoot,
                recovered.invocationId);
        }


        internal static void TryDeleteEmptyStateDirectories(
            string projectRoot,
            string invocationId)
        {
            string stateRoot = PublicationPaths.GetStateRoot(projectRoot, invocationId);
            TryDeleteEmptyStateDirectory(
                projectRoot,
                Path.Combine(stateRoot, "work"));
            TryDeleteEmptyStateDirectory(projectRoot, stateRoot);
            TryDeleteEmptyStateDirectory(
                projectRoot,
                PublicationPaths.GetProviderStateRoot(projectRoot));
        }


        internal static void TryDeleteEmptyStateDirectory(
            string projectRoot,
            string path)
        {
            if (!Directory.Exists(path) && !File.Exists(path))
            {
                return;
            }

            if (File.Exists(path))
            {
                throw new InvalidOperationException(
                    $"YooAsset transaction state path is a file: '{path}'.");
            }

            PublicationSafety.ValidateNoPathRedirection(projectRoot, path);
            if (!Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path, recursive: false);
            }
        }


        internal static void CleanupOperationMetadata(PublicationJournal recovered)
        {
            foreach (PublicationJournalOperation operation in recovered.operations)
            {
                PublicationSafety.DeleteOwnedFile(
                    recovered.projectRoot,
                    operation.approvedRoot,
                    operation.stage + ".meta");
                PublicationSafety.DeleteOwnedFile(
                    recovered.projectRoot,
                    operation.approvedRoot,
                    operation.backup + ".meta");
            }
        }


        internal static void EnsureOperationCandidateAbsent(PublicationJournalOperation operation)
        {
            if (Directory.Exists(operation.stage) || File.Exists(operation.stage))
            {
                throw new InvalidOperationException($"Publication stage already exists: '{operation.stage}'.");
            }
        }


    }
}
