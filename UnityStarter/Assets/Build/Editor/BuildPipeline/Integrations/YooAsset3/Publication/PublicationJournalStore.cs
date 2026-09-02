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
    internal static class PublicationJournalStore
    {
        internal static PublicationJournal ReadAndValidateJournal(string journalPath, string projectRoot, IJournalSerializer serializer)
        {
            PublicationSafety.ValidateNoPathRedirection(projectRoot, journalPath);
            var info = new FileInfo(journalPath);
            if (info.Length <= 0 || info.Length > MaximumJournalBytes)
            {
                throw new InvalidOperationException(
                    $"YooAsset publication journal size is invalid: '{journalPath}', {info.Length} bytes.");
            }

            string json = File.ReadAllText(journalPath, Encoding.UTF8);
            PublicationJournal recovered;
            try
            {
                BuildJsonDocumentContract.Validate<PublicationJournal>(
                    json,
                    JournalDocumentType,
                    "YooAsset publication journal");
                recovered = serializer.FromJson<PublicationJournal>(json);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"YooAsset publication journal is not valid JSON: '{journalPath}'.", exception);
            }

            if (recovered == null ||
                !string.Equals(recovered.documentType, JournalDocumentType, StringComparison.Ordinal) ||
                !PublicationPaths.IsValidInvocationId(recovered.invocationId) ||
                recovered.sequence <= 0 ||
                recovered.operations == null || recovered.operations.Length == 0 ||
                recovered.operations.Length > MaximumOperationCount ||
                !PublicationJournalFormat.IsTransactionId(recovered.transactionId) ||
                !PublicationJournalFormat.IsKnownPhase(recovered.phase))
            {
                throw new InvalidOperationException($"YooAsset publication journal has an unsupported or incomplete format: '{journalPath}'.");
            }

            if (!PublicationSafety.PathsEqual(projectRoot, recovered.projectRoot))
            {
                throw new InvalidOperationException(
                    $"YooAsset publication journal belongs to a different Unity project: '{journalPath}'.");
            }

            string stateRoot = PublicationPaths.GetStateRoot(projectRoot, recovered.invocationId);
            PublicationSafety.ValidateNoPathRedirection(projectRoot, stateRoot);
            string candidateDirectory = Path.GetDirectoryName(journalPath);
            string candidateName = Path.GetFileName(journalPath);
            string temporaryName = ActiveJournalFileName + ".tmp-" + recovered.transactionId;
            bool candidateNameIsKnown = string.Equals(
                    candidateName,
                    ActiveJournalFileName,
                    StringComparison.Ordinal)
                || string.Equals(
                    candidateName,
                    temporaryName,
                    StringComparison.Ordinal);
            if (string.IsNullOrEmpty(candidateDirectory)
                || !PublicationSafety.PathsEqual(candidateDirectory, stateRoot)
                || !candidateNameIsKnown)
            {
                throw new InvalidOperationException(
                    $"YooAsset publication journal is outside its invocation-owned state directory: '{journalPath}'.");
            }

            string buildOutputRoot = Path.GetFullPath(recovered.buildOutputRoot);
            string bundledFileRoot = Path.GetFullPath(recovered.bundledFileRoot);
            string streamingAssetsRoot = Path.GetFullPath(Path.Combine(projectRoot, "Assets", "StreamingAssets"));
            if (!PublicationSafety.IsStrictDescendant(projectRoot, buildOutputRoot) ||
                !PublicationSafety.PathsEqual(streamingAssetsRoot, bundledFileRoot) &&
                !PublicationSafety.IsStrictDescendant(streamingAssetsRoot, bundledFileRoot))
            {
                throw new InvalidOperationException(
                    $"YooAsset publication journal contains roots outside their approved project locations: '{journalPath}'.");
            }

            PublicationSafety.EnsureRootsDoNotOverlap(buildOutputRoot, bundledFileRoot);
            PublicationSafety.ValidateNoPathRedirection(projectRoot, buildOutputRoot);
            PublicationSafety.ValidateNoPathRedirection(projectRoot, bundledFileRoot);

            string expectedWorkRoot = Path.Combine(
                stateRoot,
                "work",
                recovered.transactionId);
            if (!PublicationSafety.PathsEqual(expectedWorkRoot, recovered.workRoot))
            {
                throw new InvalidOperationException($"YooAsset publication journal work root is invalid: '{recovered.workRoot}'.");
            }

            string expectedChecksum = ComputeChecksum(recovered);
            if (!string.Equals(expectedChecksum, recovered.checksum, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"YooAsset publication journal checksum is invalid: '{journalPath}'.");
            }

            foreach (PublicationJournalOperation operation in recovered.operations)
            {
                PublicationJournalValidator.ValidateOperation(operation, projectRoot, buildOutputRoot, bundledFileRoot, recovered.transactionId);
                if (string.Equals(recovered.phase, CommittedPhase, StringComparison.Ordinal) &&
                    operation.managesSiblingMeta && !operation.installedMetaExisted)
                {
                    throw new InvalidOperationException(
                        $"Committed YooAsset publication journal has no installed sibling meta identity for package " +
                        $"'{operation.packageName}'.");
                }
            }

            ValidateJournalPathBudgets(recovered);
            return recovered;
        }


        internal static PublicationJournal ResolveLatestJournalForRecovery(
            string projectRoot,
            string stateRoot,
            string journalPath,
            IJournalSerializer serializer)
        {
            string pattern = Path.GetFileName(journalPath) + ".tmp-*";
            string[] temporaryPaths = Directory.Exists(stateRoot)
                ? Directory.EnumerateFiles(stateRoot, pattern, SearchOption.TopDirectoryOnly).ToArray()
                : Array.Empty<string>();
            if (temporaryPaths.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Multiple YooAsset publication journal candidates require manual inspection: '{stateRoot}'.");
            }

            PublicationJournal active = File.Exists(journalPath)
                ? ReadAndValidateJournal(journalPath, projectRoot, serializer)
                : null;
            if (temporaryPaths.Length == 0)
            {
                return active;
            }

            string temporaryPath = temporaryPaths[0];
            PublicationJournal candidate = ReadAndValidateJournal(temporaryPath, projectRoot, serializer);
            string expectedTemporaryPath = journalPath + ".tmp-" + candidate.transactionId;
            if (!PublicationSafety.PathsEqual(temporaryPath, expectedTemporaryPath))
            {
                throw new InvalidOperationException(
                    $"YooAsset publication journal candidate name does not match its transaction identity: '{temporaryPath}'.");
            }

            if (active != null && !string.Equals(
                    active.transactionId,
                    candidate.transactionId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"YooAsset publication journal candidates belong to different transactions: " +
                    $"'{journalPath}', '{temporaryPath}'.");
            }

            if (active != null && candidate.sequence < active.sequence)
            {
                PublicationSafety.DeleteOwnedFile(projectRoot, stateRoot, temporaryPath);
                return active;
            }

            if (active != null && candidate.sequence == active.sequence)
            {
                if (!string.Equals(active.checksum, candidate.checksum, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"YooAsset publication journal candidates have the same sequence but different content: " +
                        $"'{journalPath}', '{temporaryPath}'.");
                }

                PublicationSafety.DeleteOwnedFile(projectRoot, stateRoot, temporaryPath);
                return active;
            }

            PublicationSafety.ValidateNoPathRedirection(projectRoot, journalPath);
            PublicationSafety.ValidateNoPathRedirection(projectRoot, temporaryPath);
            if (active == null)
            {
                File.Move(temporaryPath, journalPath);
            }
            else
            {
                File.Replace(temporaryPath, journalPath, null);
            }

            PublicationJournal promoted = ReadAndValidateJournal(journalPath, projectRoot, serializer);
            if (promoted.sequence != candidate.sequence ||
                !string.Equals(promoted.checksum, candidate.checksum, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"YooAsset publication journal candidate promotion could not be verified: '{journalPath}'.");
            }

            CleanupJournalTemporaryFiles(projectRoot, stateRoot, journalPath);
            return promoted;
        }


        internal static void WriteJournal(PublicationJournal value, string journalPath, bool createNew, IJournalSerializer serializer)
        {
            BuildPathPolicy.EnsureWin32MaxPathBudget(
                journalPath,
                "YooAsset publication journal",
                ".tmp-".Length + 32);
            string journalDirectory = Path.GetDirectoryName(journalPath);
            if (string.IsNullOrEmpty(journalDirectory))
            {
                throw new InvalidOperationException($"YooAsset publication journal path has no parent: '{journalPath}'.");
            }

            PublicationSafety.ValidateNoPathRedirection(value.projectRoot, journalDirectory);
            PublicationSafety.ValidateNoPathRedirection(value.projectRoot, journalPath);
            value.sequence = checked(value.sequence + 1);
            value.checksum = ComputeChecksum(value);
            string json = serializer.ToJson(value);
            byte[] bytes = new UTF8Encoding(false).GetBytes(json);
            if (bytes.Length <= 0 || bytes.Length > MaximumJournalBytes)
            {
                throw new InvalidOperationException($"YooAsset publication journal exceeds {MaximumJournalBytes} bytes.");
            }

            Directory.CreateDirectory(journalDirectory);
            PublicationSafety.ValidateNoPathRedirection(value.projectRoot, journalDirectory);
            PublicationSafety.ValidateNoPathRedirection(value.projectRoot, journalPath);
            string temporaryPath = journalPath + ".tmp-" + value.transactionId;
            BuildPathPolicy.EnsureWin32MaxPathBudget(
                temporaryPath,
                "YooAsset publication temporary journal");
            PublicationSafety.ValidateNoPathRedirection(value.projectRoot, temporaryPath);
            bool candidateIsDurable = false;
            try
            {
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

                candidateIsDurable = true;

                PublicationSafety.ValidateNoPathRedirection(value.projectRoot, journalPath);
                PublicationSafety.ValidateNoPathRedirection(value.projectRoot, temporaryPath);
                if (createNew)
                {
                    if (File.Exists(journalPath) || Directory.Exists(journalPath))
                    {
                        throw new InvalidOperationException(
                            $"A YooAsset publication journal already exists: '{journalPath}'.");
                    }

                    File.Move(temporaryPath, journalPath);
                }
                else
                {
                    File.Replace(temporaryPath, journalPath, null);
                }

                PublicationJournal persisted = ReadAndValidateJournal(journalPath, value.projectRoot, serializer);
                if (persisted.sequence != value.sequence ||
                    !string.Equals(persisted.checksum, value.checksum, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"YooAsset publication journal write could not be verified: '{journalPath}'.");
                }
            }
            catch
            {
                if (!candidateIsDurable && File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }

                throw;
            }
        }


        internal static string ComputeChecksum(PublicationJournal value)
        {
            var builder = new StringBuilder();
            AppendChecksumValue(builder, value.documentType);
            AppendChecksumValue(builder, value.sequence.ToString(CultureInfo.InvariantCulture));
            AppendChecksumValue(builder, value.invocationId);
            AppendChecksumValue(builder, value.transactionId);
            AppendChecksumValue(builder, value.phase);
            AppendChecksumValue(builder, value.projectRoot);
            AppendChecksumValue(builder, value.buildOutputRoot);
            AppendChecksumValue(builder, value.bundledFileRoot);
            AppendChecksumValue(builder, value.workRoot);
            PublicationJournalOperation[] operations =
                value.operations ?? Array.Empty<PublicationJournalOperation>();
            AppendChecksumValue(builder, operations.Length.ToString(CultureInfo.InvariantCulture));
            foreach (PublicationJournalOperation operation in operations)
            {
                AppendChecksumValue(builder, operation?.kind);
                AppendChecksumValue(builder, operation?.packageName);
                AppendChecksumValue(builder, operation?.packageVersion);
                AppendChecksumValue(builder, operation?.cryptographyAdapterId);
                AppendChecksumValue(builder, operation?.runtimeDecryptContractId);
                AppendChecksumValue(builder, operation?.approvedRoot);
                AppendChecksumValue(builder, operation?.target);
                AppendChecksumValue(builder, operation?.stage);
                AppendChecksumValue(builder, operation?.backup);
                AppendChecksumValue(builder, operation != null && operation.targetInitiallyExisted ? "1" : "0");
                AppendChecksumValue(builder, operation != null && operation.originalWasOwned ? "1" : "0");
                AppendChecksumValue(builder, operation?.originalTransactionId);
                AppendChecksumValue(builder, operation?.originalPackageVersion);
                AppendChecksumValue(builder, operation?.originalCryptographyAdapterId);
                AppendChecksumValue(builder, operation?.originalRuntimeDecryptContractId);
                AppendChecksumValue(builder, operation?.originalContentIdentity);
                AppendChecksumValue(builder, operation == null
                    ? string.Empty
                    : operation.originalEntryCount.ToString(CultureInfo.InvariantCulture));
                AppendChecksumValue(builder, operation?.installedContentIdentity);
                AppendChecksumValue(builder, operation == null
                    ? string.Empty
                    : operation.installedEntryCount.ToString(CultureInfo.InvariantCulture));
                AppendChecksumValue(builder, operation != null && operation.managesSiblingMeta ? "1" : "0");
                AppendChecksumValue(builder, operation?.targetMeta);
                AppendChecksumValue(builder, operation?.protectedMeta);
                AppendChecksumValue(builder, operation != null && operation.originalMetaExisted ? "1" : "0");
                AppendChecksumValue(builder, operation == null
                    ? string.Empty
                    : operation.originalMetaLength.ToString(CultureInfo.InvariantCulture));
                AppendChecksumValue(builder, operation?.originalMetaSha256);
                AppendChecksumValue(builder, operation != null && operation.installedMetaExisted ? "1" : "0");
                AppendChecksumValue(builder, operation == null
                    ? string.Empty
                    : operation.installedMetaLength.ToString(CultureInfo.InvariantCulture));
                AppendChecksumValue(builder, operation?.installedMetaSha256);
                AppendChecksumValue(builder, operation?.state);
            }

            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));
                return BitConverter.ToString(hash).Replace("-", string.Empty);
            }
        }


        internal static void AppendChecksumValue(StringBuilder builder, string value)
        {
            string normalized = value ?? string.Empty;
            builder.Append(normalized.Length.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(normalized);
            builder.Append(';');
        }


        internal static void CleanupJournalTemporaryFiles(string projectRoot, string stateRoot, string journalPath)
        {
            string pattern = Path.GetFileName(journalPath) + ".tmp-*";
            foreach (string temporaryPath in Directory.EnumerateFiles(stateRoot, pattern, SearchOption.TopDirectoryOnly))
            {
                PublicationSafety.DeleteOwnedFile(projectRoot, stateRoot, temporaryPath);
            }
        }


        internal static void ValidateJournalPathBudgets(PublicationJournal value)
        {
            string stateRoot = PublicationPaths.GetStateRoot(
                value.projectRoot,
                value.invocationId);
            BuildPathPolicy.EnsureWin32MaxDirectoryPathBudget(
                stateRoot,
                "YooAsset publication state root");
            BuildPathPolicy.EnsureWin32MaxPathBudget(
                Path.Combine(stateRoot, ActiveJournalFileName),
                "YooAsset publication journal",
                ".tmp-".Length + 32);
            BuildPathPolicy.EnsureWin32MaxDirectoryPathBudget(
                value.workRoot,
                "YooAsset publication work root",
                65);

            for (int operationIndex = 0; operationIndex < value.operations.Length; operationIndex++)
            {
                PublicationJournalOperation operation = value.operations[operationIndex];
                BuildPathPolicy.EnsureWin32MaxDirectoryPathBudget(
                    operation.target,
                    $"YooAsset publication target '{operation.packageName}'");
                BuildPathPolicy.EnsureWin32MaxDirectoryPathBudget(
                    operation.stage,
                    $"YooAsset publication stage '{operation.packageName}'");
                BuildPathPolicy.EnsureWin32MaxDirectoryPathBudget(
                    operation.backup,
                    $"YooAsset publication backup '{operation.packageName}'");
                BuildPathPolicy.EnsureWin32MaxPathBudget(
                    Path.Combine(operation.stage, PublicationOwnership.MarkerFileName),
                    $"YooAsset staged ownership marker '{operation.packageName}'");
                BuildPathPolicy.EnsureWin32MaxPathBudget(
                    Path.Combine(operation.target, PublicationOwnership.MarkerFileName),
                    $"YooAsset published ownership marker '{operation.packageName}'");
                if (operation.managesSiblingMeta)
                {
                    SourceQualificationPaths sourceQualificationPaths =
                        PublicationPaths.GetSourceQualificationPaths(value, operationIndex);
                    BuildPathPolicy.EnsureWin32MaxDirectoryPathBudget(
                        sourceQualificationPaths.OperationRoot,
                        $"YooAsset source qualification operation root '{operation.packageName}'");
                    BuildPathPolicy.EnsureWin32MaxDirectoryPathBudget(
                        sourceQualificationPaths.InstalledDirectory,
                        $"YooAsset source qualification installed directory '{operation.packageName}'");
                    BuildPathPolicy.EnsureWin32MaxPathBudget(
                        sourceQualificationPaths.InstalledMeta,
                        $"YooAsset source qualification installed meta '{operation.packageName}'");
                    BuildPathPolicy.EnsureWin32MaxPathBudget(
                        sourceQualificationPaths.OriginalMeta,
                        $"YooAsset source qualification original meta '{operation.packageName}'");
                    BuildPathPolicy.EnsureWin32MaxPathBudget(
                        operation.targetMeta,
                        $"YooAsset published sibling meta '{operation.packageName}'");
                    BuildPathPolicy.EnsureWin32MaxPathBudget(
                        operation.protectedMeta,
                        $"YooAsset protected sibling meta '{operation.packageName}'");
                }
            }
        }


        


        


        


        


        


        


    }
}
