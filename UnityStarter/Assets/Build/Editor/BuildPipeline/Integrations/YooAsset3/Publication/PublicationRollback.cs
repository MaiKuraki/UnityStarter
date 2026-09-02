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
    internal static class PublicationRollback
    {
        internal static void Rollback(
            PublicationJournal recovered,
            string journalPath,
            Action refreshAssets,
            IJournalSerializer serializer,
            Action<string> checkpoint = null)
        {
            checkpoint?.Invoke("RollbackStart");
            bool sourceQualificationPhase = PublicationJournalFormat.IsSourceQualificationPhase(recovered.phase);
            if (sourceQualificationPhase)
            {
                NormalizeSourceQualificationForRollback(recovered, serializer);
            }
            else
            {
                PublicationRollback.CaptureActivatedSiblingMetasForRollback(recovered, serializer);
            }

            recovered.phase = RollingBackPhase;
            var failures = new List<Exception>();
            try
            {
                PublicationJournalStore.WriteJournal(recovered, journalPath, createNew: false, serializer);
            }
            catch (Exception exception)
            {
                failures.Add(new InvalidOperationException(
                    "Failed to persist the rollback phase before restoring publication directories.",
                    exception));
            }

            for (int index = recovered.operations.Length - 1; index >= 0; index--)
            {
                try
                {
                    PublicationJournalOperation operation = recovered.operations[index];
                    RollbackOperation(recovered, operation, serializer);
                    operation.state = PreparedState;
                    PublicationJournalStore.WriteJournal(recovered, journalPath, createNew: false, serializer);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            try
            {
                PublicationSafety.DeleteOwnedDirectory(
                    recovered.projectRoot,
                    PublicationPaths.GetStateRoot(recovered.projectRoot, recovered.invocationId),
                    recovered.workRoot);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            if (failures.Count > 0)
            {
                throw new AggregateException(
                    "YooAsset publication rollback could not restore every owned directory.",
                    failures);
            }

            PublicationCommitCompletion.CleanupOperationMetadata(recovered);
            ValidateRolledBackState(recovered, serializer);
            recovered.phase = RollbackRefreshPendingPhase;
            PublicationJournalStore.WriteJournal(recovered, journalPath, createNew: false, serializer);
            checkpoint?.Invoke("RollbackComplete");
            CompleteRollbackRefresh(recovered, journalPath, refreshAssets, serializer);
        }


        internal static void CompleteRollbackRefresh(
            PublicationJournal recovered,
            string journalPath,
            Action refreshAssets,
            IJournalSerializer serializer)
        {
            ValidateRolledBackState(recovered, serializer);
            bool requiresRefresh = recovered.operations.Any(operation =>
                operation.managesSiblingMeta);
            if (requiresRefresh && refreshAssets == null)
            {
                throw new InvalidOperationException(
                    "YooAsset rollback restored bundled Assets content, but no AssetDatabase refresh callback was supplied. " +
                    "The durable rollback journal was retained for explicit recovery.");
            }

            refreshAssets?.Invoke();
            ValidateRolledBackState(recovered, serializer);
            PublicationSafety.DeleteOwnedFile(
                recovered.projectRoot,
                PublicationPaths.GetStateRoot(recovered.projectRoot, recovered.invocationId),
                journalPath);
            PublicationCommitCompletion.TryDeleteEmptyStateDirectories(
                recovered.projectRoot,
                recovered.invocationId);
        }


        internal static void ValidateRolledBackState(PublicationJournal recovered, IJournalSerializer serializer)
        {
            if (Directory.Exists(recovered.workRoot) || File.Exists(recovered.workRoot))
            {
                throw new InvalidOperationException(
                    $"YooAsset rollback work directory still exists: '{recovered.workRoot}'.");
            }

            foreach (PublicationJournalOperation operation in recovered.operations)
            {
                if (Directory.Exists(operation.stage) || File.Exists(operation.stage)
                    || Directory.Exists(operation.backup) || File.Exists(operation.backup)
                    || Directory.Exists(operation.protectedMeta) || File.Exists(operation.protectedMeta))
                {
                    throw new InvalidOperationException(
                        $"YooAsset rollback retained transaction-owned evidence for package '{operation.packageName}'.");
                }

                if (operation.targetInitiallyExisted)
                {
                    PublicationJournalValidator.ValidateOriginalPublicationAt(
                        operation,
                        operation.target,
                        recovered.projectRoot, serializer);
                }
                else
                {
                    if (Directory.Exists(operation.target) || File.Exists(operation.target))
                    {
                        throw new InvalidOperationException(
                            $"YooAsset rollback retained a newly installed target: '{operation.target}'.");
                    }

                    if (operation.managesSiblingMeta)
                    {
                        PublicationMetaGuard.ValidateMetaFile(
                            recovered.projectRoot,
                            operation.targetMeta,
                            expectedExists: false,
                            expectedLength: 0,
                            expectedSha256: string.Empty,
                            description: "rolled-back bundled publication meta", serializer);
                    }
                }
            }
        }


        internal static void RollbackOperation(
            PublicationJournal recovered,
            PublicationJournalOperation operation,
            IJournalSerializer serializer)
        {
            bool targetExists = Directory.Exists(operation.target);
            bool stageExists = Directory.Exists(operation.stage);
            bool backupExists = Directory.Exists(operation.backup);
            if (File.Exists(operation.target) || File.Exists(operation.stage) || File.Exists(operation.backup))
            {
                throw new InvalidOperationException(
                    $"Cannot recover publication operation because a directory path became a file for package '{operation.packageName}'.");
            }

            if (backupExists)
            {
                if (targetExists && stageExists)
                {
                    throw new InvalidOperationException(
                        $"Ambiguous publication state for package '{operation.packageName}': target, stage, and backup all exist.");
                }

                if (!operation.targetInitiallyExisted)
                {
                    throw new InvalidOperationException(
                        $"A publication backup exists for a target that did not originally exist: '{operation.backup}'.");
                }

                PublicationJournalValidator.ValidateOriginalPublicationAt(operation, operation.backup, recovered.projectRoot, serializer);
                PublicationMetaGuard.ValidatePreRefreshSiblingMeta(
                    recovered.projectRoot,
                    operation,
                    serializer,
                    allowMissingOriginalMeta: true);

                if (targetExists)
                {
                    PublicationJournalValidator.ValidateInstalledPublicationAt(
                        operation,
                        operation.target,
                        recovered.projectRoot,
                        recovered.transactionId,
                        serializer);
                    PublicationSafety.DeleteOwnedDirectory(
                        recovered.projectRoot,
                        operation.approvedRoot,
                        operation.target);
                }

                if (Directory.Exists(operation.target))
                {
                    throw new InvalidOperationException($"Cannot restore publication backup over '{operation.target}'.");
                }

                Directory.Move(operation.backup, operation.target);
                PublicationMetaGuard.RestoreOriginalSiblingMeta(recovered, serializer, operation);
                PublicationJournalValidator.ValidateOriginalPublicationAt(operation, operation.target, recovered.projectRoot, serializer);
            }
            else if (operation.targetInitiallyExisted)
            {
                if (!targetExists)
                {
                    throw new InvalidOperationException(
                        $"The original publication target cannot be proven recoverable for package '{operation.packageName}'.");
                }

                PublicationJournalValidator.ValidateOriginalPublicationAt(
                    operation,
                    operation.target,
                    recovered.projectRoot,
                    serializer, validateSiblingMeta: false);
                if (operation.managesSiblingMeta && File.Exists(operation.protectedMeta))
                {
                    PublicationMetaGuard.RestoreOriginalSiblingMeta(recovered, serializer, operation);
                }
                else
                {
                    PublicationJournalValidator.ValidateOriginalPublicationAt(operation, operation.target, recovered.projectRoot, serializer);
                    PublicationMetaGuard.DeleteProtectedSiblingMetaIfPresent(recovered, serializer, operation);
                }
            }
            else if (targetExists)
            {
                bool installMayHaveCompleted =
                    string.Equals(operation.state, BackedUpState, StringComparison.Ordinal) && !stageExists ||
                    string.Equals(operation.state, InstalledState, StringComparison.Ordinal);
                if (!installMayHaveCompleted)
                {
                    throw new InvalidOperationException(
                        $"An unexpected publication target appeared for package '{operation.packageName}': '{operation.target}'.");
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
                PublicationSafety.DeleteOwnedDirectory(
                    recovered.projectRoot,
                    operation.approvedRoot,
                    operation.target);
                if (operation.managesSiblingMeta)
                {
                    PublicationMetaGuard.RestoreOriginalSiblingMeta(recovered, serializer, operation);
                }
            }

            if (!operation.targetInitiallyExisted && operation.managesSiblingMeta)
            {
                PublicationMetaGuard.RestoreOriginalSiblingMeta(recovered, serializer, operation);
                PublicationMetaGuard.ValidateMetaFile(
                    recovered.projectRoot,
                    operation.targetMeta,
                    expectedExists: false,
                    expectedLength: 0,
                    expectedSha256: string.Empty,
                    description: "rolled-back bundled publication meta", serializer);
            }

            DeleteStageIfOwned(recovered, operation, serializer);
            if (Directory.Exists(operation.backup) || File.Exists(operation.backup))
            {
                throw new InvalidOperationException(
                    $"Publication backup remained after rollback for package '{operation.packageName}': '{operation.backup}'.");
            }

            if (operation.managesSiblingMeta &&
                (Directory.Exists(operation.protectedMeta) || File.Exists(operation.protectedMeta)))
            {
                throw new InvalidOperationException(
                    $"Protected bundled publication meta remained after rollback: '{operation.protectedMeta}'.");
            }
        }


        internal static void DeleteStageIfOwned(
            PublicationJournal recovered,
            PublicationJournalOperation operation,
            IJournalSerializer serializer)
        {
            if (!Directory.Exists(operation.stage) && !File.Exists(operation.stage))
            {
                return;
            }

            if (File.Exists(operation.stage))
            {
                throw new InvalidOperationException(
                    $"Publication stage became a file for package '{operation.packageName}': '{operation.stage}'.");
            }

            if (!string.IsNullOrWhiteSpace(operation.installedContentIdentity))
            {
                PublicationJournalValidator.ValidateInstalledPublicationAt(
                    operation,
                    operation.stage,
                    recovered.projectRoot,
                    recovered.transactionId,
                        serializer);
            }

            PublicationSafety.DeleteOwnedDirectory(
                recovered.projectRoot,
                operation.approvedRoot,
                operation.stage);
        }


        internal static void NormalizeSourceQualificationForRollback(
            PublicationJournal value,
            IJournalSerializer serializer)
        {
            if (!PublicationJournalFormat.IsSourceQualificationPhase(value.phase))
            {
                return;
            }

            for (int index = value.operations.Length - 1; index >= 0; index--)
            {
                PublicationJournalOperation operation = value.operations[index];
                if (!operation.managesSiblingMeta)
                {
                    continue;
                }

                SourceQualificationPaths paths = PublicationPaths.GetSourceQualificationPaths(value, index);
                PublicationPaths.ValidateSourceQualificationPath(value, paths.OperationRoot);
                if (Directory.Exists(paths.InstalledDirectory))
                {
                    PublicationFileOps.EnsureDirectoryPathAbsent(operation.stage, "YooAsset bundled stage during recovery");
                    if (PublicationSourceQualification.IsInstalledPublicationAtTarget(value, operation, serializer))
                    {
                        throw new InvalidOperationException(
                            $"YooAsset source qualification recovery found both active and held installed publications for package '{operation.packageName}'.");
                    }

                    Directory.Move(paths.InstalledDirectory, operation.stage);
                }
                else if (File.Exists(paths.InstalledDirectory))
                {
                    throw new InvalidOperationException(
                        $"YooAsset source qualification installed holding path became a file: '{paths.InstalledDirectory}'.");
                }

                if (File.Exists(paths.OriginalMeta))
                {
                    if (Directory.Exists(operation.backup))
                    {
                        PublicationFileOps.EnsureFilePathAbsent(
                            operation.protectedMeta,
                            "YooAsset protected bundled meta during recovery");
                        File.Move(paths.OriginalMeta, operation.protectedMeta);
                    }
                }
                else if (Directory.Exists(paths.OriginalMeta))
                {
                    throw new InvalidOperationException(
                        $"YooAsset source qualification original meta holding path became a directory: '{paths.OriginalMeta}'.");
                }

                if (File.Exists(paths.InstalledMeta))
                {
                    if (PublicationSourceQualification.IsInstalledPublicationAtTarget(value, operation, serializer))
                    {
                        PublicationFileOps.EnsureFilePathAbsent(
                            operation.targetMeta,
                            "YooAsset installed bundled meta during recovery");
                        File.Move(paths.InstalledMeta, operation.targetMeta);
                    }
                }
                else if (Directory.Exists(paths.InstalledMeta))
                {
                    throw new InvalidOperationException(
                        $"YooAsset source qualification installed meta holding path became a directory: '{paths.InstalledMeta}'.");
                }
            }
        }



internal static bool CaptureActivatedSiblingMetasForRollback(
            PublicationJournal recovered,
            IJournalSerializer serializer)
        {
            bool changed = false;
            foreach (PublicationJournalOperation operation in recovered.operations)
            {
                if (!operation.managesSiblingMeta)
                {
                    continue;
                }

                bool installMayBeVisible =
                    string.Equals(operation.state, InstalledState, StringComparison.Ordinal)
                    || string.Equals(operation.state, BackedUpState, StringComparison.Ordinal)
                    && Directory.Exists(operation.target)
                    && !Directory.Exists(operation.stage);
                if (!installMayBeVisible || !Directory.Exists(operation.target))
                {
                    continue;
                }

                PublicationJournalValidator.ValidateInstalledPublicationAt(
                    operation,
                    operation.target,
                    recovered.projectRoot,
                    recovered.transactionId,
                        serializer);

                MetaFileSnapshot installed = PublicationMetaGuard.CaptureMetaFile(recovered.projectRoot, operation.targetMeta);
                if (operation.originalMetaExisted)
                {
                    PublicationMetaGuard.ValidateMetaSnapshot(
                        installed,
                        operation.targetMeta,
                        true,
                        operation.originalMetaLength,
                        operation.originalMetaSha256,
                        "activated bundled publication meta",
                        serializer);
                }

                if (operation.installedMetaExisted != installed.Exists
                    || operation.installedMetaLength != installed.Length
                    || !string.Equals(
                        operation.installedMetaSha256,
                        installed.Sha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    operation.installedMetaExisted = installed.Exists;
                    operation.installedMetaLength = installed.Length;
                    operation.installedMetaSha256 = installed.Sha256;
                    changed = true;
                }
            }

            return changed;
        }
    }
}
