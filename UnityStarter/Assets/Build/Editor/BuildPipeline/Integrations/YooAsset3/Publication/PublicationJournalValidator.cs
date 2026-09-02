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
    internal static class PublicationJournalValidator
    {
        internal static void ValidateOperation(
            PublicationJournalOperation operation,
            string projectRoot,
            string buildOutputRoot,
            string bundledFileRoot,
            string transactionId)
        {
            if (operation == null || string.IsNullOrWhiteSpace(operation.packageName) ||
                string.IsNullOrWhiteSpace(operation.packageVersion) ||
                (!string.Equals(operation.kind, PublicationOwnership.PackageOutputKind, StringComparison.Ordinal) &&
                 !string.Equals(operation.kind, PublicationOwnership.BundledPackageKind, StringComparison.Ordinal)) ||
                !PublicationJournalFormat.IsKnownOperationState(operation.state))
            {
                throw new InvalidOperationException("YooAsset publication journal contains an invalid operation.");
            }

            try
            {
                BuildIdentityPolicy.ValidateBuildIdentifier(
                    operation.cryptographyAdapterId,
                    "YooAsset cryptography adapter id");
                BuildIdentityPolicy.ValidateBuildIdentifier(
                    operation.runtimeDecryptContractId,
                    "YooAsset runtime decrypt contract id");
                if (operation.originalWasOwned)
                {
                    BuildIdentityPolicy.ValidateBuildIdentifier(
                        operation.originalCryptographyAdapterId,
                        "Original YooAsset cryptography adapter id");
                    BuildIdentityPolicy.ValidateBuildIdentifier(
                        operation.originalRuntimeDecryptContractId,
                        "Original YooAsset runtime decrypt contract id");
                }
                else if (!string.IsNullOrEmpty(operation.originalCryptographyAdapterId)
                         || !string.IsNullOrEmpty(operation.originalRuntimeDecryptContractId))
                {
                    throw new InvalidOperationException(
                        "An unowned original YooAsset publication may not carry cryptography provenance.");
                }
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"YooAsset publication journal cryptography identity is invalid for package '{operation.packageName}'.",
                    exception);
            }

            string expectedRoot = string.Equals(operation.kind, PublicationOwnership.PackageOutputKind, StringComparison.Ordinal)
                ? buildOutputRoot
                : bundledFileRoot;
            if (!PublicationSafety.PathsEqual(expectedRoot, operation.approvedRoot) ||
                !PublicationSafety.IsStrictDescendant(operation.approvedRoot, operation.target))
            {
                throw new InvalidOperationException(
                    $"YooAsset publication operation escaped its approved root: '{operation.target}'.");
            }

            string targetParent = Path.GetDirectoryName(Path.GetFullPath(operation.target));
            if (string.IsNullOrEmpty(targetParent) ||
                !PublicationSafety.PathsEqual(targetParent, Path.GetDirectoryName(Path.GetFullPath(operation.stage))) ||
                !PublicationSafety.PathsEqual(targetParent, Path.GetDirectoryName(Path.GetFullPath(operation.backup))) ||
                !Path.GetFileName(operation.stage).StartsWith(StagePrefix + transactionId + "-", StringComparison.Ordinal) ||
                !Path.GetFileName(operation.backup).StartsWith(BackupPrefix + transactionId + "-", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"YooAsset publication stage or backup path is invalid for package '{operation.packageName}'.");
            }

            if (PublicationSafety.PathsEqual(operation.target, operation.stage) ||
                PublicationSafety.PathsEqual(operation.target, operation.backup) ||
                PublicationSafety.PathsEqual(operation.stage, operation.backup))
            {
                throw new InvalidOperationException(
                    $"YooAsset publication paths collide for package '{operation.packageName}'.");
            }

            PublicationSafety.ValidateNoPathRedirection(projectRoot, operation.target);
            PublicationSafety.ValidateNoPathRedirection(projectRoot, operation.stage);
            PublicationSafety.ValidateNoPathRedirection(projectRoot, operation.backup);

            string streamingAssetsRoot = Path.GetFullPath(Path.Combine(projectRoot, "Assets", "StreamingAssets"));
            bool expectedSiblingMetaManagement =
                string.Equals(operation.kind, PublicationOwnership.BundledPackageKind, StringComparison.Ordinal) &&
                PublicationSafety.IsStrictDescendant(streamingAssetsRoot, operation.target);
            if (operation.managesSiblingMeta != expectedSiblingMetaManagement)
            {
                throw new InvalidOperationException(
                    $"YooAsset publication sibling meta policy is invalid for package '{operation.packageName}'.");
            }

            if (operation.managesSiblingMeta)
            {
                string expectedTargetMeta = operation.target + ".meta";
                string expectedProtectedMeta = operation.backup + ".root-meta";
                if (!PublicationSafety.PathsEqual(expectedTargetMeta, operation.targetMeta) ||
                    !PublicationSafety.PathsEqual(expectedProtectedMeta, operation.protectedMeta) ||
                    !PublicationSafety.IsStrictDescendant(operation.approvedRoot, operation.targetMeta) ||
                    !PublicationSafety.IsStrictDescendant(operation.approvedRoot, operation.protectedMeta))
                {
                    throw new InvalidOperationException(
                        $"YooAsset publication sibling meta paths are invalid for package '{operation.packageName}'.");
                }

                PublicationSafety.ValidateNoPathRedirection(projectRoot, operation.targetMeta);
                PublicationSafety.ValidateNoPathRedirection(projectRoot, operation.protectedMeta);
                if (operation.targetInitiallyExisted != operation.originalMetaExisted ||
                    operation.originalMetaExisted &&
                    (operation.originalMetaLength < 0 || operation.originalMetaLength > MaximumSiblingMetaBytes ||
                     !PublicationJournalFormat.IsSha256(operation.originalMetaSha256)) ||
                    !operation.originalMetaExisted &&
                    (operation.originalMetaLength != 0 || !string.IsNullOrEmpty(operation.originalMetaSha256)) ||
                    operation.installedMetaExisted &&
                    (operation.installedMetaLength < 0 || operation.installedMetaLength > MaximumSiblingMetaBytes ||
                     !PublicationJournalFormat.IsSha256(operation.installedMetaSha256)) ||
                    !operation.installedMetaExisted &&
                    (operation.installedMetaLength != 0 || !string.IsNullOrEmpty(operation.installedMetaSha256)))
                {
                    throw new InvalidOperationException(
                        $"YooAsset publication sibling meta identity is incomplete for package '{operation.packageName}'.");
                }
            }
            else if (!string.IsNullOrEmpty(operation.targetMeta) ||
                     !string.IsNullOrEmpty(operation.protectedMeta) ||
                     operation.originalMetaExisted || operation.originalMetaLength != 0 ||
                     !string.IsNullOrEmpty(operation.originalMetaSha256) ||
                     operation.installedMetaExisted || operation.installedMetaLength != 0 ||
                     !string.IsNullOrEmpty(operation.installedMetaSha256))
            {
                throw new InvalidOperationException(
                    $"YooAsset publication contains unexpected sibling meta state for package '{operation.packageName}'.");
            }

            if ((operation.targetInitiallyExisted && string.IsNullOrWhiteSpace(operation.originalContentIdentity)) ||
                (operation.originalWasOwned &&
                  (string.IsNullOrWhiteSpace(operation.originalTransactionId) ||
                   string.IsNullOrWhiteSpace(operation.originalPackageVersion) ||
                   string.IsNullOrWhiteSpace(operation.originalCryptographyAdapterId) ||
                   string.IsNullOrWhiteSpace(operation.originalRuntimeDecryptContractId))) ||
                (string.Equals(operation.state, InstalledState, StringComparison.Ordinal) &&
                 string.IsNullOrWhiteSpace(operation.installedContentIdentity)))
            {
                throw new InvalidOperationException(
                    $"YooAsset publication journal ownership identity is incomplete for package '{operation.packageName}'.");
            }
        }


        internal static void ValidateOriginalPublicationAt(
            PublicationJournalOperation operation,
            string directory,
            string projectRoot,
            IJournalSerializer serializer,
            bool validateSiblingMeta = true)
        {
            bool directoryExists = Directory.Exists(directory);
            if (File.Exists(directory) || directoryExists != operation.targetInitiallyExisted)
            {
                throw new InvalidOperationException(
                    $"Publication target changed after ownership validation for package '{operation.packageName}': '{directory}'.");
            }

            if (validateSiblingMeta && operation.managesSiblingMeta)
            {
                if (PublicationSafety.PathsEqual(directory, operation.target))
                {
                    PublicationMetaGuard.ValidateMetaFile(
                        projectRoot,
                        operation.targetMeta,
                        operation.originalMetaExisted,
                        operation.originalMetaLength,
                        operation.originalMetaSha256,
                        "original bundled publication meta", serializer);
                }
                else if (PublicationSafety.PathsEqual(directory, operation.backup))
                {
                    PublicationMetaGuard.ValidateMetaFile(
                        projectRoot,
                        operation.protectedMeta,
                        operation.originalMetaExisted,
                        operation.originalMetaLength,
                        operation.originalMetaSha256,
                        "protected bundled publication meta", serializer);
                }
            }

            if (!directoryExists)
            {
                return;
            }

            PublicationOwnership.PublicationSnapshot actual;
            if (operation.originalWasOwned)
            {
                actual = PublicationOwnership.ValidateOwned(
                    projectRoot,
                    directory,
                    operation.kind,
                    operation.packageName,
                    operation.originalPackageVersion,
                    operation.originalCryptographyAdapterId,
                    operation.originalRuntimeDecryptContractId,
                    operation.originalTransactionId,
                    operation.originalContentIdentity,
                    operation.originalEntryCount,
                    serializer);
            }
            else
            {
                actual = PublicationOwnership.ValidateEmptyUnowned(projectRoot, directory);
            }

            if (!string.Equals(actual.ContentIdentity, operation.originalContentIdentity, StringComparison.OrdinalIgnoreCase) ||
                actual.EntryCount != operation.originalEntryCount)
            {
                throw new InvalidOperationException(
                    $"Original publication identity changed for package '{operation.packageName}': '{directory}'.");
            }

        }


        internal static void ValidateInstalledPublicationAt(
            PublicationJournalOperation operation,
            string directory,
            string projectRoot,
            string transactionId,
            IJournalSerializer serializer)
        {
            if (string.IsNullOrWhiteSpace(operation.installedContentIdentity) || operation.installedEntryCount < 0)
            {
                throw new InvalidOperationException(
                    $"Publication stage was not sealed for package '{operation.packageName}'.");
            }

            PublicationOwnership.ValidateOwned(
                projectRoot,
                directory,
                operation.kind,
                operation.packageName,
                operation.packageVersion,
                operation.cryptographyAdapterId,
                operation.runtimeDecryptContractId,
                transactionId,
                operation.installedContentIdentity,
                operation.installedEntryCount,
                serializer);
        }


    }
}
