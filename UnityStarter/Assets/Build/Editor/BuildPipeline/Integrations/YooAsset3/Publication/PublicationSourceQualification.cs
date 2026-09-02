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
    internal static class PublicationSourceQualification
    {
        internal static bool IsInstalledPublicationAtTarget(
            PublicationJournal value,
            PublicationJournalOperation operation,
            IJournalSerializer serializer)
        {
            if (!Directory.Exists(operation.target))
            {
                if (File.Exists(operation.target))
                {
                    throw new InvalidOperationException(
                        $"YooAsset bundled target became a file during source qualification recovery: '{operation.target}'.");
                }

                return false;
            }

            PublicationOwnership.PublicationSnapshot actual =
                PublicationOwnership.CaptureExisting(
                    value.projectRoot,
                    operation.target,
                    operation.kind,
                    operation.packageName, serializer);
            return actual.Owned
                   && string.Equals(actual.PackageVersion, operation.packageVersion, StringComparison.Ordinal)
                   && string.Equals(actual.CryptographyAdapterId, operation.cryptographyAdapterId, StringComparison.Ordinal)
                   && string.Equals(actual.RuntimeDecryptContractId, operation.runtimeDecryptContractId, StringComparison.Ordinal)
                   && string.Equals(actual.TransactionId, value.transactionId, StringComparison.Ordinal)
                   && string.Equals(actual.ContentIdentity, operation.installedContentIdentity, StringComparison.OrdinalIgnoreCase)
                   && actual.EntryCount == operation.installedEntryCount;
        }


        internal static void EnsureSourceQualificationRootCanBeCreated(
            PublicationJournal value,
            string suspensionRoot)
        {
            PublicationPaths.ValidateSourceQualificationPath(value, suspensionRoot);
            if (Directory.Exists(suspensionRoot) || File.Exists(suspensionRoot))
            {
                throw new InvalidOperationException(
                    $"YooAsset source qualification holding root is not empty: '{suspensionRoot}'.");
            }
        }


        internal static void EnsureSourceQualificationPathsAbsent(
            PublicationJournal value,
            PublicationJournalOperation operation,
            SourceQualificationPaths paths)
        {
            PublicationPaths.ValidateSourceQualificationPath(value, paths.OperationRoot);
            PublicationFileOps.EnsureDirectoryPathAbsent(paths.OperationRoot, "YooAsset source qualification operation root");
            PublicationFileOps.EnsureDirectoryPathAbsent(paths.InstalledDirectory, "YooAsset source qualification installed directory");
            PublicationFileOps.EnsureFilePathAbsent(paths.InstalledMeta, "YooAsset source qualification installed meta");
            PublicationFileOps.EnsureFilePathAbsent(paths.OriginalMeta, "YooAsset source qualification original meta");
            if (!operation.managesSiblingMeta)
            {
                throw new InvalidOperationException(
                    "Only YooAsset bundled operations may enter source qualification suspension.");
            }
        }


        internal static void DeleteSourceQualificationRoot(PublicationJournal value)
        {
            string suspensionRoot = PublicationPaths.GetSourceQualificationRoot(value);
            PublicationPaths.ValidateSourceQualificationPath(value, suspensionRoot);
            if (!Directory.Exists(suspensionRoot) && !File.Exists(suspensionRoot))
            {
                return;
            }

            if (File.Exists(suspensionRoot))
            {
                throw new InvalidOperationException(
                    $"YooAsset source qualification holding root became a file: '{suspensionRoot}'.");
            }

            if (Directory.EnumerateFileSystemEntries(suspensionRoot).Any())
            {
                throw new InvalidOperationException(
                    $"YooAsset source qualification holding root retained evidence: '{suspensionRoot}'.");
            }

            Directory.Delete(suspensionRoot, false);
        }


        


        


        


        internal static void SuspendBundledOperation(
            PublicationJournal value,
            PublicationJournalOperation operation,
            int operationIndex,
            bool downstreamActive,
            IJournalSerializer serializer)
        {
            SourceQualificationPaths paths = PublicationPaths.GetSourceQualificationPaths(
                value,
                operationIndex);
            EnsureSourceQualificationPathsAbsent(value, operation, paths);
            Directory.CreateDirectory(paths.OperationRoot);
            PublicationSafety.ValidateNoPathRedirection(
                value.projectRoot,
                paths.OperationRoot);

            if (downstreamActive)
            {
                ValidateActivatedBundledOperation(value, serializer, operation);
                Directory.Move(operation.target, paths.InstalledDirectory);
            }
            else
            {
                ValidatePreparedBundledOperation(value, serializer, operation);
                Directory.Move(operation.stage, paths.InstalledDirectory);
            }

            PublicationJournalValidator.ValidateInstalledPublicationAt(
                operation,
                paths.InstalledDirectory,
                value.projectRoot,
                value.transactionId, serializer);

            if (!downstreamActive)
            {
                ValidateSourceQualificationOperationSuspended(
                    value,
                    operation,
                    paths,
                    downstreamActive: false,
                    serializer);
                return;
            }

            if (operation.targetInitiallyExisted)
            {
                PublicationJournalValidator.ValidateOriginalPublicationAt(
                    operation,
                    operation.backup,
                    value.projectRoot, serializer);
                Directory.Move(operation.backup, operation.target);
                PublicationJournalValidator.ValidateOriginalPublicationAt(
                    operation,
                    operation.target,
                    value.projectRoot, serializer);

                PublicationMetaGuard.ValidateMetaFile(
                    value.projectRoot,
                    operation.protectedMeta,
                    true,
                    operation.originalMetaLength,
                    operation.originalMetaSha256,
                    "protected bundled publication meta before source qualification", serializer);
                File.Move(operation.protectedMeta, paths.OriginalMeta);
            }
            else
            {
                PublicationFileOps.EnsureDirectoryPathAbsent(operation.backup, "YooAsset bundled backup");
                PublicationMetaGuard.ValidateMetaFile(
                    value.projectRoot,
                    operation.targetMeta,
                    true,
                    operation.installedMetaLength,
                    operation.installedMetaSha256,
                    "installed bundled publication meta before source qualification", serializer);
                File.Move(operation.targetMeta, paths.InstalledMeta);
            }

            ValidateSourceQualificationOperationSuspended(
                value,
                operation,
                paths,
                downstreamActive: true,
                serializer);
        }


        internal static void ResumeBundledOperation(
            PublicationJournal value,
            PublicationJournalOperation operation,
            int operationIndex,
            bool downstreamActive,
            IJournalSerializer serializer)
        {
            SourceQualificationPaths paths = PublicationPaths.GetSourceQualificationPaths(
                value,
                operationIndex);
            ValidateSourceQualificationOperationSuspended(
                value,
                operation,
                paths,
                downstreamActive,
                    serializer);

            if (!downstreamActive)
            {
                Directory.Move(paths.InstalledDirectory, operation.stage);
                ValidatePreparedBundledOperation(value, serializer, operation);
                if (Directory.EnumerateFileSystemEntries(paths.OperationRoot).Any())
                {
                    throw new InvalidOperationException(
                        $"YooAsset source qualification holding directory retained unknown evidence: '{paths.OperationRoot}'.");
                }

                Directory.Delete(paths.OperationRoot, false);
                return;
            }

            if (operation.targetInitiallyExisted)
            {
                Directory.Move(operation.target, operation.backup);
                PublicationJournalValidator.ValidateOriginalPublicationAt(
                    operation,
                    operation.backup,
                    value.projectRoot, serializer);
                File.Move(paths.OriginalMeta, operation.protectedMeta);
                PublicationMetaGuard.ValidateMetaFile(
                    value.projectRoot,
                    operation.protectedMeta,
                    true,
                    operation.originalMetaLength,
                    operation.originalMetaSha256,
                    "protected bundled publication meta after source qualification", serializer);
            }

            Directory.Move(paths.InstalledDirectory, operation.target);
            if (!operation.targetInitiallyExisted)
            {
                File.Move(paths.InstalledMeta, operation.targetMeta);
            }

            ValidateActivatedBundledOperation(value, serializer, operation);
            if (Directory.EnumerateFileSystemEntries(paths.OperationRoot).Any())
            {
                throw new InvalidOperationException(
                    $"YooAsset source qualification holding directory retained unknown evidence: '{paths.OperationRoot}'.");
            }

            Directory.Delete(paths.OperationRoot, false);
        }


        internal static void ValidateSourceQualificationSuspended(
            PublicationJournal value,
            bool downstreamActive,
            IJournalSerializer serializer)
        {
            string suspensionRoot = PublicationPaths.GetSourceQualificationRoot(value);
            PublicationSafety.ValidateNoPathRedirection(
                value.projectRoot,
                suspensionRoot);
            if (!Directory.Exists(suspensionRoot) || File.Exists(suspensionRoot))
            {
                throw new DirectoryNotFoundException(
                    $"YooAsset source qualification holding root does not exist: '{suspensionRoot}'.");
            }

            int expectedOperationCount = 0;
            for (int index = 0; index < value.operations.Length; index++)
            {
                PublicationJournalOperation operation = value.operations[index];
                if (!operation.managesSiblingMeta)
                {
                    continue;
                }

                expectedOperationCount++;
                ValidateSourceQualificationOperationSuspended(
                    value,
                    operation,
                    PublicationPaths.GetSourceQualificationPaths(value, index),
                    downstreamActive,
                    serializer);
            }

            if (Directory.GetFileSystemEntries(suspensionRoot).Length != expectedOperationCount)
            {
                throw new InvalidOperationException(
                    $"YooAsset source qualification holding root contains unknown evidence: '{suspensionRoot}'.");
            }
        }


        internal static void ValidateSourceQualificationOperationSuspended(
            PublicationJournal value,
            PublicationJournalOperation operation,
            SourceQualificationPaths paths,
            bool downstreamActive,
            IJournalSerializer serializer)
        {
            PublicationSafety.ValidateNoPathRedirection(
                value.projectRoot,
                paths.OperationRoot);
            if (!Directory.Exists(paths.OperationRoot) || File.Exists(paths.OperationRoot))
            {
                throw new DirectoryNotFoundException(
                    $"YooAsset source qualification operation root does not exist: '{paths.OperationRoot}'.");
            }

            PublicationJournalValidator.ValidateOriginalPublicationAt(
                operation,
                operation.target,
                value.projectRoot, serializer);
            PublicationJournalValidator.ValidateInstalledPublicationAt(
                operation,
                paths.InstalledDirectory,
                value.projectRoot,
                value.transactionId, serializer);
            PublicationFileOps.EnsureDirectoryPathAbsent(operation.stage, "YooAsset bundled stage");
            PublicationFileOps.EnsureDirectoryPathAbsent(operation.backup, "YooAsset bundled backup");
            PublicationFileOps.EnsureFilePathAbsent(operation.protectedMeta, "YooAsset protected bundled meta");

            if (!downstreamActive)
            {
                PublicationFileOps.EnsureFilePathAbsent(paths.InstalledMeta, "YooAsset source qualification installed meta");
                PublicationFileOps.EnsureFilePathAbsent(paths.OriginalMeta, "YooAsset source qualification original meta");
                if (Directory.GetFileSystemEntries(paths.OperationRoot).Length != 1)
                {
                    throw new InvalidOperationException(
                        $"YooAsset source qualification operation root contains unknown evidence: '{paths.OperationRoot}'.");
                }

                return;
            }

            if (operation.targetInitiallyExisted)
            {
                PublicationMetaGuard.ValidateMetaFile(
                    value.projectRoot,
                    paths.OriginalMeta,
                    true,
                    operation.originalMetaLength,
                    operation.originalMetaSha256,
                    "source qualification protected original bundled meta", serializer);
                PublicationFileOps.EnsureFilePathAbsent(paths.InstalledMeta, "YooAsset source qualification installed meta");
            }
            else
            {
                PublicationMetaGuard.ValidateMetaFile(
                    value.projectRoot,
                    paths.InstalledMeta,
                    true,
                    operation.installedMetaLength,
                    operation.installedMetaSha256,
                    "source qualification installed bundled meta", serializer);
                PublicationFileOps.EnsureFilePathAbsent(paths.OriginalMeta, "YooAsset source qualification original meta");
            }

            if (Directory.GetFileSystemEntries(paths.OperationRoot).Length != 2)
            {
                throw new InvalidOperationException(
                    $"YooAsset source qualification operation root contains unknown evidence: '{paths.OperationRoot}'.");
            }
        }


        internal static void ValidateActivatedBundledOperation(
            PublicationJournal value,
            IJournalSerializer serializer,
            PublicationJournalOperation operation)
        {
            PublicationJournalValidator.ValidateInstalledPublicationAt(
                operation,
                operation.target,
                value.projectRoot,
                value.transactionId, serializer);
            PublicationMetaGuard.ValidateInstalledSiblingMeta(value, serializer, operation);
            PublicationFileOps.EnsureDirectoryPathAbsent(operation.stage, "YooAsset bundled stage");

            if (operation.targetInitiallyExisted)
            {
                PublicationJournalValidator.ValidateOriginalPublicationAt(
                    operation,
                    operation.backup,
                    value.projectRoot, serializer);
                PublicationMetaGuard.ValidateMetaFile(
                    value.projectRoot,
                    operation.protectedMeta,
                    true,
                    operation.originalMetaLength,
                    operation.originalMetaSha256,
                    "protected bundled publication meta after source qualification", serializer);
            }
            else
            {
                PublicationFileOps.EnsureDirectoryPathAbsent(operation.backup, "YooAsset bundled backup");
                PublicationFileOps.EnsureFilePathAbsent(operation.protectedMeta, "YooAsset protected bundled meta");
            }
        }


        internal static void ValidatePreparedForSourceQualification(
            PublicationJournal value,
            IJournalSerializer serializer)
        {
            foreach (PublicationJournalOperation operation in value.operations)
            {
                if (!string.Equals(operation.state, PreparedState, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"YooAsset prepared source qualification found a non-prepared operation for package '{operation.packageName}'.");
                }

                if (operation.managesSiblingMeta)
                {
                    ValidatePreparedBundledOperation(value, serializer, operation);
                }
                else
                {
                    PublicationJournalValidator.ValidateInstalledPublicationAt(
                        operation,
                        operation.stage,
                        value.projectRoot,
                        value.transactionId, serializer);
                    PublicationJournalValidator.ValidateOriginalPublicationAt(
                        operation,
                        operation.target,
                        value.projectRoot, serializer);
                }
            }
        }


        internal static void ValidatePreparedBundledOperation(
            PublicationJournal value,
            IJournalSerializer serializer,
            PublicationJournalOperation operation)
        {
            if (!string.Equals(operation.state, PreparedState, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"YooAsset bundled operation is not prepared for source qualification: '{operation.packageName}'.");
            }

            PublicationJournalValidator.ValidateInstalledPublicationAt(
                operation,
                operation.stage,
                value.projectRoot,
                value.transactionId, serializer);
            PublicationJournalValidator.ValidateOriginalPublicationAt(
                operation,
                operation.target,
                value.projectRoot, serializer);
            PublicationFileOps.EnsureDirectoryPathAbsent(operation.backup, "YooAsset bundled backup");
            PublicationFileOps.EnsureFilePathAbsent(operation.protectedMeta, "YooAsset protected bundled meta");
        }

    }
}
