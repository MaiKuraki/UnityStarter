using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Build.Pipeline.Editor;
using static Build.Pipeline.Integrations.YooAsset3.Publication.PublicationConstants;
namespace Build.Pipeline.Integrations.YooAsset3.Publication
{
    internal static class PublicationRecovery
    {
        internal static void RecoverPending(string projectRoot, Action refreshAssets, IJournalSerializer serializer)
        {
            string normalizedProjectRoot = Path.GetFullPath(projectRoot);
            string providerStateRoot = PublicationPaths.GetProviderStateRoot(normalizedProjectRoot);
            if (!Directory.Exists(providerStateRoot) && !File.Exists(providerStateRoot))
            {
                return;
            }

            if (File.Exists(providerStateRoot))
            {
                throw new InvalidOperationException(
                    $"YooAsset provider transaction state root is a file: '{providerStateRoot}'.");
            }

            PublicationSafety.ValidateNoPathRedirection(
                normalizedProjectRoot,
                providerStateRoot);
            string[] invocationStateRoots = Directory.GetDirectories(
                providerStateRoot,
                "*",
                SearchOption.TopDirectoryOnly);
            if (invocationStateRoots.Length > 256)
            {
                throw new InvalidOperationException(
                    "YooAsset publication recovery exceeds the 256-invocation safety budget.");
            }

            string unexpectedFile = Directory.GetFiles(
                    providerStateRoot,
                    "*",
                    SearchOption.TopDirectoryOnly)
                .FirstOrDefault();
            if (unexpectedFile != null)
            {
                throw new InvalidOperationException(
                    $"Unknown YooAsset provider transaction state file requires manual review: '{unexpectedFile}'.");
            }

            Array.Sort(invocationStateRoots, StringComparer.Ordinal);
            foreach (string invocationStateRoot in invocationStateRoots)
            {
                PublicationSafety.ValidateNoPathRedirection(
                    normalizedProjectRoot,
                    invocationStateRoot);
                RecoverPendingInvocation(
                    normalizedProjectRoot,
                    PublicationPaths.NormalizeInvocationId(Path.GetFileName(invocationStateRoot)),
                    refreshAssets,
                    serializer);
            }
        }


        internal static void RecoverPendingInvocation(
            string normalizedProjectRoot,
            string invocationId,
            Action refreshAssets,
            IJournalSerializer serializer)
        {
            string stateRoot = PublicationPaths.GetStateRoot(normalizedProjectRoot, invocationId);
            string journalPath = Path.Combine(stateRoot, ActiveJournalFileName);
            PublicationSafety.ValidateNoPathRedirection(normalizedProjectRoot, stateRoot);
            PublicationSafety.ValidateNoPathRedirection(normalizedProjectRoot, journalPath);
            PublicationJournal recovered = PublicationJournalStore.ResolveLatestJournalForRecovery(
                normalizedProjectRoot,
                stateRoot,
                journalPath,
                serializer);
            if (recovered == null)
            {
                EnsureNoDetachedState(stateRoot);
                PublicationCommitCompletion.TryDeleteEmptyStateDirectories(
                    normalizedProjectRoot,
                    invocationId);
                return;
            }

            BuildPublicationDecision decision = BuildPublicationBarrier.GetDecision(
                normalizedProjectRoot,
                PublicationPaths.GetPublicationId(invocationId),
                PublicationPaths.GetStateRelativePath(invocationId));
            if (!string.Equals(recovered.phase, RefreshPendingPhase, StringComparison.Ordinal)
                && !string.Equals(recovered.phase, CommittedPhase, StringComparison.Ordinal)
                && !string.Equals(recovered.phase, RollbackRefreshPendingPhase, StringComparison.Ordinal)
                && !PublicationJournalFormat.IsSourceQualificationPhase(recovered.phase))
            {
                if (PublicationRollback.CaptureActivatedSiblingMetasForRollback(recovered, serializer))
                {
                    PublicationJournalStore.WriteJournal(recovered, journalPath, createNew: false, serializer);
                }
            }

            if (string.Equals(recovered.phase, ActivationRefreshPendingPhase, StringComparison.Ordinal))
            {
                recovered.phase = DownstreamActivePhase;
                PublicationJournalStore.WriteJournal(recovered, journalPath, createNew: false, serializer);
            }

            if (PublicationJournalFormat.IsSourceQualificationPhase(recovered.phase))
            {
                if (decision == BuildPublicationDecision.Commit)
                {
                    throw new InvalidOperationException(
                        "Committed terminal barrier conflicts with a YooAsset publication that was suspended for source qualification.");
                }

                PublicationRollback.Rollback(recovered, journalPath, refreshAssets, serializer);
            }
            else if (string.Equals(recovered.phase, DownstreamActivePhase, StringComparison.Ordinal))
            {
                if (decision == BuildPublicationDecision.Commit)
                {
                    throw new InvalidOperationException(
                        "Committed terminal barrier references a YooAsset publication whose terminal outputs were never published.");
                }

                PublicationRollback.Rollback(recovered, journalPath, refreshAssets, serializer);
            }
            else if (string.Equals(recovered.phase, AwaitingDecisionPhase, StringComparison.Ordinal))
            {
                if (decision == BuildPublicationDecision.Commit)
                {
                    PublicationCommitCompletion.ValidatePreRefreshCommittedPublications(recovered, serializer);
                    recovered.phase = RefreshPendingPhase;
                    PublicationJournalStore.WriteJournal(recovered, journalPath, createNew: false, serializer);
                    PublicationCommitCompletion.CompletePendingRefresh(recovered, journalPath, refreshAssets, serializer);
                }
                else
                {
                    PublicationRollback.Rollback(recovered, journalPath, refreshAssets, serializer);
                }
            }
            else if (string.Equals(recovered.phase, RollbackRefreshPendingPhase, StringComparison.Ordinal))
            {
                if (decision == BuildPublicationDecision.Commit)
                {
                    throw new InvalidOperationException(
                        "Committed terminal barrier conflicts with a YooAsset publication that already restored its original files.");
                }

                PublicationRollback.CompleteRollbackRefresh(recovered, journalPath, refreshAssets, serializer);
            }
            else if (string.Equals(recovered.phase, RefreshPendingPhase, StringComparison.Ordinal))
            {
                if (decision != BuildPublicationDecision.Commit)
                {
                    throw new InvalidOperationException(
                        "YooAsset committed refresh recovery requires an explicit durable Commit decision.");
                }

                PublicationCommitCompletion.CompletePendingRefresh(recovered, journalPath, refreshAssets, serializer);
            }
            else if (string.Equals(recovered.phase, CommittedPhase, StringComparison.Ordinal))
            {
                if (decision != BuildPublicationDecision.Commit)
                {
                    throw new InvalidOperationException(
                        "YooAsset committed cleanup recovery requires an explicit durable Commit decision.");
                }

                try
                {
                    PublicationCommitCompletion.CleanupCommitted(recovered, journalPath, serializer);
                }
                catch (Exception exception)
                {
                    throw new CommittedPublicationException(
                        "YooAsset publication is committed, but committed-state cleanup still requires recovery.",
                        journalPath,
                        exception);
                }
            }
            else
            {
                if (decision == BuildPublicationDecision.Commit)
                {
                    throw new InvalidOperationException(
                        "Committed terminal barrier references a YooAsset publication that was not fully installed.");
                }

                PublicationRollback.Rollback(recovered, journalPath, refreshAssets, serializer);
            }
        }


        internal static void EnsureNoPendingRecovery(
            string projectRoot,
            string invocationId)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                throw new ArgumentException("A Unity project root is required.", nameof(projectRoot));
            }

            string normalizedProjectRoot = Path.GetFullPath(projectRoot);
            string stateRoot = PublicationPaths.GetStateRoot(
                normalizedProjectRoot,
                PublicationPaths.NormalizeInvocationId(invocationId));
            string journalPath = Path.Combine(stateRoot, ActiveJournalFileName);
            PublicationSafety.ValidateNoPathRedirection(normalizedProjectRoot, stateRoot);
            PublicationSafety.ValidateNoPathRedirection(normalizedProjectRoot, journalPath);
            if (File.Exists(journalPath) || Directory.Exists(journalPath))
            {
                throw new InvalidOperationException(
                    $"Pending YooAsset publication recovery must be completed before starting another build: '{stateRoot}'. " +
                    "Use the Build workspace recovery action or -pipelineRecoverOnly.");
            }

            EnsureNoDetachedState(stateRoot);
        }


        internal static void EnsureNoDetachedState(string stateRoot)
        {
            if (!Directory.Exists(stateRoot))
            {
                return;
            }

            string workParent = Path.Combine(stateRoot, "work");
            if (Directory.Exists(workParent) && Directory.EnumerateFileSystemEntries(workParent).Any())
            {
                throw new InvalidOperationException(
                    $"Detached YooAsset transaction work directories require manual inspection: '{workParent}'.");
            }

            if (Directory.EnumerateFiles(stateRoot, ActiveJournalFileName + ".tmp-*", SearchOption.TopDirectoryOnly).Any())
            {
                throw new InvalidOperationException(
                    $"Detached YooAsset journal temporary files require manual inspection: '{stateRoot}'.");
            }
        }


        internal static void ValidateDownstreamInputs(PublicationJournal recovered, bool afterRefresh, IJournalSerializer serializer)
        {
            bool terminalOutputsInstalled = string.Equals(
                recovered.phase,
                AwaitingDecisionPhase,
                StringComparison.Ordinal);
            foreach (PublicationJournalOperation operation in recovered.operations)
            {
                if (operation.managesSiblingMeta)
                {
                    if (!string.Equals(operation.state, InstalledState, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"YooAsset bundled downstream input is not installed for package '{operation.packageName}'.");
                    }

                    PublicationJournalValidator.ValidateInstalledPublicationAt(
                        operation,
                        operation.target,
                        recovered.projectRoot,
                        recovered.transactionId,
                        serializer);
                    if (afterRefresh)
                    {
                        PublicationMetaGuard.ValidateInstalledSiblingMeta(recovered, serializer, operation);
                    }
                    else
                    {
                        PublicationMetaGuard.ValidatePreRefreshSiblingMeta(
                            recovered.projectRoot,
                            operation,
                            serializer,
                            allowMissingOriginalMeta: false);
                    }

                    continue;
                }

                if (terminalOutputsInstalled)
                {
                    if (!string.Equals(operation.state, InstalledState, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"YooAsset terminal output is not installed for package '{operation.packageName}'.");
                    }

                    PublicationJournalValidator.ValidateInstalledPublicationAt(
                        operation,
                        operation.target,
                        recovered.projectRoot,
                        recovered.transactionId,
                        serializer);
                }
                else
                {
                    if (!string.Equals(operation.state, PreparedState, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"YooAsset terminal output changed before the terminal publication barrier for package '{operation.packageName}'.");
                    }

                    PublicationJournalValidator.ValidateInstalledPublicationAt(
                        operation,
                        operation.stage,
                        recovered.projectRoot,
                        recovered.transactionId,
                        serializer);
                }
            }
        }


    }
}
