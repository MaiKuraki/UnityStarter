using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using static Build.Pipeline.Editor.Integrations.YooAsset3Core.YooAsset3PublicationConstants;

namespace Build.Pipeline.Editor.Integrations.YooAsset3Core
{
    internal static class YooAsset3PublicationRecovery
    {
        public static void RecoverPending(string projectRoot, Action refreshAssets)
        {
            string normalizedProjectRoot = Path.GetFullPath(projectRoot);
            string providerStateRoot = YooAsset3PublicationPaths.GetProviderStateRoot(normalizedProjectRoot);
            if (!Directory.Exists(providerStateRoot) && !File.Exists(providerStateRoot))
            {
                return;
            }

            if (File.Exists(providerStateRoot))
            {
                throw new InvalidOperationException(
                    $"YooAsset provider transaction state root is a file: '{providerStateRoot}'.");
            }

            YooAsset3BuildSafety.ValidateNoPathRedirection(
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
                YooAsset3BuildSafety.ValidateNoPathRedirection(
                    normalizedProjectRoot,
                    invocationStateRoot);
                RecoverPendingInvocation(
                    normalizedProjectRoot,
                    YooAsset3PublicationPaths.NormalizeInvocationId(Path.GetFileName(invocationStateRoot)),
                    refreshAssets);
            }
        }

        private static void RecoverPendingInvocation(
            string normalizedProjectRoot,
            string invocationId,
            Action refreshAssets)
        {
            string stateRoot = YooAsset3PublicationPaths.GetStateRoot(normalizedProjectRoot, invocationId);
            string journalPath = Path.Combine(stateRoot, ActiveJournalFileName);
            YooAsset3BuildSafety.ValidateNoPathRedirection(normalizedProjectRoot, stateRoot);
            YooAsset3BuildSafety.ValidateNoPathRedirection(normalizedProjectRoot, journalPath);
            Journal recovered = ResolveLatestJournalForRecovery(
                normalizedProjectRoot,
                stateRoot,
                journalPath);
            if (recovered == null)
            {
                EnsureNoDetachedState(stateRoot);
                TryDeleteEmptyStateDirectories(
                    normalizedProjectRoot,
                    invocationId);
                return;
            }

            BuildPublicationDecision decision = BuildPublicationBarrier.GetDecision(
                normalizedProjectRoot,
                YooAsset3PublicationPaths.GetPublicationId(invocationId),
                YooAsset3PublicationPaths.GetStateRelativePath(invocationId));
            if (!string.Equals(recovered.phase, RefreshPendingPhase, StringComparison.Ordinal)
                && !string.Equals(recovered.phase, CommittedPhase, StringComparison.Ordinal)
                && !string.Equals(recovered.phase, RollbackRefreshPendingPhase, StringComparison.Ordinal)
                && !IsSourceQualificationPhase(recovered.phase))
            {
                if (CaptureActivatedSiblingMetasForRollback(recovered))
                {
                    WriteJournal(recovered, journalPath, createNew: false);
                }
            }

            if (string.Equals(recovered.phase, ActivationRefreshPendingPhase, StringComparison.Ordinal))
            {
                recovered.phase = DownstreamActivePhase;
                WriteJournal(recovered, journalPath, createNew: false);
            }

            if (IsSourceQualificationPhase(recovered.phase))
            {
                if (decision == BuildPublicationDecision.Commit)
                {
                    throw new InvalidOperationException(
                        "Committed terminal barrier conflicts with a YooAsset publication that was suspended for source qualification.");
                }

                Rollback(recovered, journalPath, refreshAssets);
            }
            else if (string.Equals(recovered.phase, DownstreamActivePhase, StringComparison.Ordinal))
            {
                if (decision == BuildPublicationDecision.Commit)
                {
                    throw new InvalidOperationException(
                        "Committed terminal barrier references a YooAsset publication whose terminal outputs were never published.");
                }

                Rollback(recovered, journalPath, refreshAssets);
            }
            else if (string.Equals(recovered.phase, AwaitingDecisionPhase, StringComparison.Ordinal))
            {
                if (decision == BuildPublicationDecision.Commit)
                {
                    ValidatePreRefreshCommittedPublications(recovered);
                    recovered.phase = RefreshPendingPhase;
                    WriteJournal(recovered, journalPath, createNew: false);
                    CompletePendingRefresh(recovered, journalPath, refreshAssets);
                }
                else
                {
                    Rollback(recovered, journalPath, refreshAssets);
                }
            }
            else if (string.Equals(recovered.phase, RollbackRefreshPendingPhase, StringComparison.Ordinal))
            {
                if (decision == BuildPublicationDecision.Commit)
                {
                    throw new InvalidOperationException(
                        "Committed terminal barrier conflicts with a YooAsset publication that already restored its original files.");
                }

                CompleteRollbackRefresh(recovered, journalPath, refreshAssets);
            }
            else if (string.Equals(recovered.phase, RefreshPendingPhase, StringComparison.Ordinal))
            {
                if (decision != BuildPublicationDecision.Commit)
                {
                    throw new InvalidOperationException(
                        "YooAsset committed refresh recovery requires an explicit durable Commit decision.");
                }

                CompletePendingRefresh(recovered, journalPath, refreshAssets);
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
                    CleanupCommitted(recovered, journalPath);
                }
                catch (Exception exception)
                {
                    throw new YooAsset3CommittedPublicationException(
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

                Rollback(recovered, journalPath, refreshAssets);
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
            string stateRoot = YooAsset3PublicationPaths.GetStateRoot(
                normalizedProjectRoot,
                YooAsset3PublicationPaths.NormalizeInvocationId(invocationId));
            string journalPath = Path.Combine(stateRoot, ActiveJournalFileName);
            YooAsset3BuildSafety.ValidateNoPathRedirection(normalizedProjectRoot, stateRoot);
            YooAsset3BuildSafety.ValidateNoPathRedirection(normalizedProjectRoot, journalPath);
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

        internal static void ValidateJournalPathBudgets(Journal value)
        {
            string stateRoot = YooAsset3PublicationPaths.GetStateRoot(
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
                YooAsset3PublicationJournalOperation operation = value.operations[operationIndex];
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
                    Path.Combine(operation.stage, YooAsset3PublicationOwnership.MarkerFileName),
                    $"YooAsset staged ownership marker '{operation.packageName}'");
                BuildPathPolicy.EnsureWin32MaxPathBudget(
                    Path.Combine(operation.target, YooAsset3PublicationOwnership.MarkerFileName),
                    $"YooAsset published ownership marker '{operation.packageName}'");
                if (operation.managesSiblingMeta)
                {
                    SourceQualificationPaths sourceQualificationPaths =
                        GetSourceQualificationPaths(value, operationIndex);
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

        internal static Journal ReadAndValidateJournal(string journalPath, string projectRoot)
        {
            YooAsset3BuildSafety.ValidateNoPathRedirection(projectRoot, journalPath);
            var info = new FileInfo(journalPath);
            if (info.Length <= 0 || info.Length > MaximumJournalBytes)
            {
                throw new InvalidOperationException(
                    $"YooAsset publication journal size is invalid: '{journalPath}', {info.Length} bytes.");
            }

            string json = File.ReadAllText(journalPath, Encoding.UTF8);
            Journal recovered;
            try
            {
                BuildJsonDocumentContract.Validate<Journal>(
                    json,
                    JournalDocumentType,
                    "YooAsset publication journal");
                recovered = JsonUtility.FromJson<Journal>(json);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"YooAsset publication journal is not valid JSON: '{journalPath}'.", exception);
            }

            if (recovered == null ||
                !string.Equals(recovered.documentType, JournalDocumentType, StringComparison.Ordinal) ||
                !YooAsset3PublicationPaths.IsValidInvocationId(recovered.invocationId) ||
                recovered.sequence <= 0 ||
                recovered.operations == null || recovered.operations.Length == 0 ||
                recovered.operations.Length > MaximumOperationCount ||
                !IsTransactionId(recovered.transactionId) ||
                !IsKnownPhase(recovered.phase))
            {
                throw new InvalidOperationException($"YooAsset publication journal has an unsupported or incomplete format: '{journalPath}'.");
            }

            if (!YooAsset3BuildSafety.PathsEqual(projectRoot, recovered.projectRoot))
            {
                throw new InvalidOperationException(
                    $"YooAsset publication journal belongs to a different Unity project: '{journalPath}'.");
            }

            string stateRoot = YooAsset3PublicationPaths.GetStateRoot(projectRoot, recovered.invocationId);
            YooAsset3BuildSafety.ValidateNoPathRedirection(projectRoot, stateRoot);
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
                || !YooAsset3BuildSafety.PathsEqual(candidateDirectory, stateRoot)
                || !candidateNameIsKnown)
            {
                throw new InvalidOperationException(
                    $"YooAsset publication journal is outside its invocation-owned state directory: '{journalPath}'.");
            }

            string buildOutputRoot = Path.GetFullPath(recovered.buildOutputRoot);
            string bundledFileRoot = Path.GetFullPath(recovered.bundledFileRoot);
            string streamingAssetsRoot = Path.GetFullPath(Path.Combine(projectRoot, "Assets", "StreamingAssets"));
            if (!YooAsset3BuildSafety.IsStrictDescendant(projectRoot, buildOutputRoot) ||
                !YooAsset3BuildSafety.PathsEqual(streamingAssetsRoot, bundledFileRoot) &&
                !YooAsset3BuildSafety.IsStrictDescendant(streamingAssetsRoot, bundledFileRoot))
            {
                throw new InvalidOperationException(
                    $"YooAsset publication journal contains roots outside their approved project locations: '{journalPath}'.");
            }

            YooAsset3BuildSafety.EnsureRootsDoNotOverlap(buildOutputRoot, bundledFileRoot);
            YooAsset3BuildSafety.ValidateNoPathRedirection(projectRoot, buildOutputRoot);
            YooAsset3BuildSafety.ValidateNoPathRedirection(projectRoot, bundledFileRoot);

            string expectedWorkRoot = Path.Combine(
                stateRoot,
                "work",
                recovered.transactionId);
            if (!YooAsset3BuildSafety.PathsEqual(expectedWorkRoot, recovered.workRoot))
            {
                throw new InvalidOperationException($"YooAsset publication journal work root is invalid: '{recovered.workRoot}'.");
            }

            string expectedChecksum = ComputeChecksum(recovered);
            if (!string.Equals(expectedChecksum, recovered.checksum, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"YooAsset publication journal checksum is invalid: '{journalPath}'.");
            }

            foreach (YooAsset3PublicationJournalOperation operation in recovered.operations)
            {
                ValidateOperation(operation, projectRoot, buildOutputRoot, bundledFileRoot, recovered.transactionId);
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

        internal static Journal ResolveLatestJournalForRecovery(
            string projectRoot,
            string stateRoot,
            string journalPath)
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

            Journal active = File.Exists(journalPath)
                ? ReadAndValidateJournal(journalPath, projectRoot)
                : null;
            if (temporaryPaths.Length == 0)
            {
                return active;
            }

            string temporaryPath = temporaryPaths[0];
            Journal candidate = ReadAndValidateJournal(temporaryPath, projectRoot);
            string expectedTemporaryPath = journalPath + ".tmp-" + candidate.transactionId;
            if (!YooAsset3BuildSafety.PathsEqual(temporaryPath, expectedTemporaryPath))
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
                YooAsset3BuildSafety.DeleteOwnedFile(projectRoot, stateRoot, temporaryPath);
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

                YooAsset3BuildSafety.DeleteOwnedFile(projectRoot, stateRoot, temporaryPath);
                return active;
            }

            YooAsset3BuildSafety.ValidateNoPathRedirection(projectRoot, journalPath);
            YooAsset3BuildSafety.ValidateNoPathRedirection(projectRoot, temporaryPath);
            if (active == null)
            {
                File.Move(temporaryPath, journalPath);
            }
            else
            {
                File.Replace(temporaryPath, journalPath, null);
            }

            Journal promoted = ReadAndValidateJournal(journalPath, projectRoot);
            if (promoted.sequence != candidate.sequence ||
                !string.Equals(promoted.checksum, candidate.checksum, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"YooAsset publication journal candidate promotion could not be verified: '{journalPath}'.");
            }

            CleanupJournalTemporaryFiles(projectRoot, stateRoot, journalPath);
            return promoted;
        }

        internal static void WriteJournal(Journal value, string journalPath, bool createNew)
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

            YooAsset3BuildSafety.ValidateNoPathRedirection(value.projectRoot, journalDirectory);
            YooAsset3BuildSafety.ValidateNoPathRedirection(value.projectRoot, journalPath);
            value.sequence = checked(value.sequence + 1);
            value.checksum = ComputeChecksum(value);
            string json = JsonUtility.ToJson(value, true);
            byte[] bytes = new UTF8Encoding(false).GetBytes(json);
            if (bytes.Length <= 0 || bytes.Length > MaximumJournalBytes)
            {
                throw new InvalidOperationException($"YooAsset publication journal exceeds {MaximumJournalBytes} bytes.");
            }

            Directory.CreateDirectory(journalDirectory);
            YooAsset3BuildSafety.ValidateNoPathRedirection(value.projectRoot, journalDirectory);
            YooAsset3BuildSafety.ValidateNoPathRedirection(value.projectRoot, journalPath);
            string temporaryPath = journalPath + ".tmp-" + value.transactionId;
            BuildPathPolicy.EnsureWin32MaxPathBudget(
                temporaryPath,
                "YooAsset publication temporary journal");
            YooAsset3BuildSafety.ValidateNoPathRedirection(value.projectRoot, temporaryPath);
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

                YooAsset3BuildSafety.ValidateNoPathRedirection(value.projectRoot, journalPath);
                YooAsset3BuildSafety.ValidateNoPathRedirection(value.projectRoot, temporaryPath);
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

                Journal persisted = ReadAndValidateJournal(journalPath, value.projectRoot);
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

        internal static string ComputeChecksum(Journal value)
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
            YooAsset3PublicationJournalOperation[] operations =
                value.operations ?? Array.Empty<YooAsset3PublicationJournalOperation>();
            AppendChecksumValue(builder, operations.Length.ToString(CultureInfo.InvariantCulture));
            foreach (YooAsset3PublicationJournalOperation operation in operations)
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
                YooAsset3BuildSafety.DeleteOwnedFile(projectRoot, stateRoot, temporaryPath);
            }
        }

        internal static bool IsTransactionId(string value)
        {
            return value != null && value.Length == 32 && value.All(character =>
                character >= '0' && character <= '9' || character >= 'a' && character <= 'f');
        }

        internal static bool IsSha256(string value)
        {
            return IsHexToken(value, 64);
        }

        internal static bool IsHexToken(string value, int length)
        {
            return value != null && value.Length == length && value.All(character =>
                character >= '0' && character <= '9' ||
                character >= 'A' && character <= 'F' ||
                character >= 'a' && character <= 'f');
        }

        internal static bool IsKnownPhase(string value)
        {
            return string.Equals(value, PreparedPhase, StringComparison.Ordinal) ||
                   string.Equals(value, CommittingPhase, StringComparison.Ordinal) ||
                   string.Equals(value, RollingBackPhase, StringComparison.Ordinal) ||
                   string.Equals(value, RollbackRefreshPendingPhase, StringComparison.Ordinal) ||
                   string.Equals(value, ActivationRefreshPendingPhase, StringComparison.Ordinal) ||
                   string.Equals(value, DownstreamActivePhase, StringComparison.Ordinal) ||
                   string.Equals(value, SourceQualificationSuspendingPhase, StringComparison.Ordinal) ||
                   string.Equals(value, SourceQualificationSuspendedPhase, StringComparison.Ordinal) ||
                   string.Equals(value, SourceQualificationResumingPhase, StringComparison.Ordinal) ||
                   string.Equals(value, AwaitingDecisionPhase, StringComparison.Ordinal) ||
                   string.Equals(value, RefreshPendingPhase, StringComparison.Ordinal) ||
                   string.Equals(value, CommittedPhase, StringComparison.Ordinal);
        }

        internal static bool IsKnownOperationState(string value)
        {
            return string.Equals(value, PreparedState, StringComparison.Ordinal) ||
                   string.Equals(value, BackupPendingState, StringComparison.Ordinal) ||
                   string.Equals(value, BackedUpState, StringComparison.Ordinal) ||
                   string.Equals(value, InstalledState, StringComparison.Ordinal);
        }

        internal static bool IsSourceQualificationPhase(string value)
        {
            return string.Equals(value, SourceQualificationSuspendingPhase, StringComparison.Ordinal) ||
                   string.Equals(value, SourceQualificationSuspendedPhase, StringComparison.Ordinal) ||
                   string.Equals(value, SourceQualificationResumingPhase, StringComparison.Ordinal);
        }

        internal static void ValidateOperation(
            YooAsset3PublicationJournalOperation operation,
            string projectRoot,
            string buildOutputRoot,
            string bundledFileRoot,
            string transactionId)
        {
            if (operation == null || string.IsNullOrWhiteSpace(operation.packageName) ||
                string.IsNullOrWhiteSpace(operation.packageVersion) ||
                (!string.Equals(operation.kind, YooAsset3PublicationOwnership.PackageOutputKind, StringComparison.Ordinal) &&
                 !string.Equals(operation.kind, YooAsset3PublicationOwnership.BundledPackageKind, StringComparison.Ordinal)) ||
                !IsKnownOperationState(operation.state))
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

            string expectedRoot = string.Equals(operation.kind, YooAsset3PublicationOwnership.PackageOutputKind, StringComparison.Ordinal)
                ? buildOutputRoot
                : bundledFileRoot;
            if (!YooAsset3BuildSafety.PathsEqual(expectedRoot, operation.approvedRoot) ||
                !YooAsset3BuildSafety.IsStrictDescendant(operation.approvedRoot, operation.target))
            {
                throw new InvalidOperationException(
                    $"YooAsset publication operation escaped its approved root: '{operation.target}'.");
            }

            string targetParent = Path.GetDirectoryName(Path.GetFullPath(operation.target));
            if (string.IsNullOrEmpty(targetParent) ||
                !YooAsset3BuildSafety.PathsEqual(targetParent, Path.GetDirectoryName(Path.GetFullPath(operation.stage))) ||
                !YooAsset3BuildSafety.PathsEqual(targetParent, Path.GetDirectoryName(Path.GetFullPath(operation.backup))) ||
                !Path.GetFileName(operation.stage).StartsWith(StagePrefix + transactionId + "-", StringComparison.Ordinal) ||
                !Path.GetFileName(operation.backup).StartsWith(BackupPrefix + transactionId + "-", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"YooAsset publication stage or backup path is invalid for package '{operation.packageName}'.");
            }

            if (YooAsset3BuildSafety.PathsEqual(operation.target, operation.stage) ||
                YooAsset3BuildSafety.PathsEqual(operation.target, operation.backup) ||
                YooAsset3BuildSafety.PathsEqual(operation.stage, operation.backup))
            {
                throw new InvalidOperationException(
                    $"YooAsset publication paths collide for package '{operation.packageName}'.");
            }

            YooAsset3BuildSafety.ValidateNoPathRedirection(projectRoot, operation.target);
            YooAsset3BuildSafety.ValidateNoPathRedirection(projectRoot, operation.stage);
            YooAsset3BuildSafety.ValidateNoPathRedirection(projectRoot, operation.backup);

            string streamingAssetsRoot = Path.GetFullPath(Path.Combine(projectRoot, "Assets", "StreamingAssets"));
            bool expectedSiblingMetaManagement =
                string.Equals(operation.kind, YooAsset3PublicationOwnership.BundledPackageKind, StringComparison.Ordinal) &&
                YooAsset3BuildSafety.IsStrictDescendant(streamingAssetsRoot, operation.target);
            if (operation.managesSiblingMeta != expectedSiblingMetaManagement)
            {
                throw new InvalidOperationException(
                    $"YooAsset publication sibling meta policy is invalid for package '{operation.packageName}'.");
            }

            if (operation.managesSiblingMeta)
            {
                string expectedTargetMeta = operation.target + ".meta";
                string expectedProtectedMeta = operation.backup + ".root-meta";
                if (!YooAsset3BuildSafety.PathsEqual(expectedTargetMeta, operation.targetMeta) ||
                    !YooAsset3BuildSafety.PathsEqual(expectedProtectedMeta, operation.protectedMeta) ||
                    !YooAsset3BuildSafety.IsStrictDescendant(operation.approvedRoot, operation.targetMeta) ||
                    !YooAsset3BuildSafety.IsStrictDescendant(operation.approvedRoot, operation.protectedMeta))
                {
                    throw new InvalidOperationException(
                        $"YooAsset publication sibling meta paths are invalid for package '{operation.packageName}'.");
                }

                YooAsset3BuildSafety.ValidateNoPathRedirection(projectRoot, operation.targetMeta);
                YooAsset3BuildSafety.ValidateNoPathRedirection(projectRoot, operation.protectedMeta);
                if (operation.targetInitiallyExisted != operation.originalMetaExisted ||
                    operation.originalMetaExisted &&
                    (operation.originalMetaLength < 0 || operation.originalMetaLength > MaximumSiblingMetaBytes ||
                     !IsSha256(operation.originalMetaSha256)) ||
                    !operation.originalMetaExisted &&
                    (operation.originalMetaLength != 0 || !string.IsNullOrEmpty(operation.originalMetaSha256)) ||
                    operation.installedMetaExisted &&
                    (operation.installedMetaLength < 0 || operation.installedMetaLength > MaximumSiblingMetaBytes ||
                     !IsSha256(operation.installedMetaSha256)) ||
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
            YooAsset3PublicationJournalOperation operation,
            string directory,
            string projectRoot,
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
                if (YooAsset3BuildSafety.PathsEqual(directory, operation.target))
                {
                    ValidateMetaFile(
                        projectRoot,
                        operation.targetMeta,
                        operation.originalMetaExisted,
                        operation.originalMetaLength,
                        operation.originalMetaSha256,
                        "original bundled publication meta");
                }
                else if (YooAsset3BuildSafety.PathsEqual(directory, operation.backup))
                {
                    ValidateMetaFile(
                        projectRoot,
                        operation.protectedMeta,
                        operation.originalMetaExisted,
                        operation.originalMetaLength,
                        operation.originalMetaSha256,
                        "protected bundled publication meta");
                }
            }

            if (!directoryExists)
            {
                return;
            }

            YooAsset3PublicationOwnership.PublicationSnapshot actual;
            if (operation.originalWasOwned)
            {
                actual = YooAsset3PublicationOwnership.ValidateOwned(
                    projectRoot,
                    directory,
                    operation.kind,
                    operation.packageName,
                    operation.originalPackageVersion,
                    operation.originalCryptographyAdapterId,
                    operation.originalRuntimeDecryptContractId,
                    operation.originalTransactionId,
                    operation.originalContentIdentity,
                    operation.originalEntryCount);
            }
            else
            {
                actual = YooAsset3PublicationOwnership.ValidateEmptyUnowned(projectRoot, directory);
            }

            if (!string.Equals(actual.ContentIdentity, operation.originalContentIdentity, StringComparison.OrdinalIgnoreCase) ||
                actual.EntryCount != operation.originalEntryCount)
            {
                throw new InvalidOperationException(
                    $"Original publication identity changed for package '{operation.packageName}': '{directory}'.");
            }

        }

        internal static void ValidateInstalledPublicationAt(
            YooAsset3PublicationJournalOperation operation,
            string directory,
            string projectRoot,
            string transactionId)
        {
            if (string.IsNullOrWhiteSpace(operation.installedContentIdentity) || operation.installedEntryCount < 0)
            {
                throw new InvalidOperationException(
                    $"Publication stage was not sealed for package '{operation.packageName}'.");
            }

            YooAsset3PublicationOwnership.ValidateOwned(
                projectRoot,
                directory,
                operation.kind,
                operation.packageName,
                operation.packageVersion,
                operation.cryptographyAdapterId,
                operation.runtimeDecryptContractId,
                transactionId,
                operation.installedContentIdentity,
                operation.installedEntryCount);
        }

        internal static void ValidatePreRefreshSiblingMeta(
            string projectRoot,
            YooAsset3PublicationJournalOperation operation,
            bool allowMissingOriginalMeta)
        {
            if (!operation.managesSiblingMeta)
            {
                return;
            }

            MetaFileSnapshot actual = CaptureMetaFile(projectRoot, operation.targetMeta);
            if (operation.originalMetaExisted && !actual.Exists && allowMissingOriginalMeta)
            {
                return;
            }


            if (!operation.originalMetaExisted && operation.installedMetaExisted)
            {
                ValidateMetaSnapshot(
                    actual,
                    operation.targetMeta,
                    true,
                    operation.installedMetaLength,
                    operation.installedMetaSha256,
                    "activated bundled publication meta");
                return;
            }

            ValidateMetaSnapshot(
                actual,
                operation.targetMeta,
                operation.originalMetaExisted,
                operation.originalMetaLength,
                operation.originalMetaSha256,
                "pre-refresh bundled publication meta");
        }

        internal static void CaptureInstalledSiblingMetas(
            Journal recovered,
            IReadOnlyDictionary<YooAsset3PublicationJournalOperation, MetaFileSnapshot> recoveryCandidates)
        {
            foreach (YooAsset3PublicationJournalOperation operation in recovered.operations)
            {
                if (!operation.managesSiblingMeta)
                {
                    continue;
                }

                MetaFileSnapshot installed = CaptureMetaFile(recovered.projectRoot, operation.targetMeta);
                if (!installed.Exists)
                {
                    throw new InvalidOperationException(
                        $"AssetDatabase refresh did not create or preserve the bundled publication meta: '{operation.targetMeta}'.");
                }

                if (operation.originalMetaExisted &&
                    (installed.Length != operation.originalMetaLength ||
                     !string.Equals(installed.Sha256, operation.originalMetaSha256, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException(
                        $"AssetDatabase refresh changed the preserved bundled publication meta identity: '{operation.targetMeta}'.");
                }

                if (recoveryCandidates != null &&
                    recoveryCandidates.TryGetValue(operation, out MetaFileSnapshot candidate) &&
                    (installed.Length != candidate.Length ||
                     !string.Equals(installed.Sha256, candidate.Sha256, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException(
                        $"AssetDatabase refresh changed a bundled publication meta discovered during recovery: " +
                        $"'{operation.targetMeta}'.");
                }

                operation.installedMetaExisted = true;
                operation.installedMetaLength = installed.Length;
                operation.installedMetaSha256 = installed.Sha256;
            }
        }

        internal static void ValidateDownstreamInputs(Journal recovered, bool afterRefresh)
        {
            bool terminalOutputsInstalled = string.Equals(
                recovered.phase,
                AwaitingDecisionPhase,
                StringComparison.Ordinal);
            foreach (YooAsset3PublicationJournalOperation operation in recovered.operations)
            {
                if (operation.managesSiblingMeta)
                {
                    if (!string.Equals(operation.state, InstalledState, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"YooAsset bundled downstream input is not installed for package '{operation.packageName}'.");
                    }

                    ValidateInstalledPublicationAt(
                        operation,
                        operation.target,
                        recovered.projectRoot,
                        recovered.transactionId);
                    if (afterRefresh)
                    {
                        ValidateInstalledSiblingMeta(recovered, operation);
                    }
                    else
                    {
                        ValidatePreRefreshSiblingMeta(
                            recovered.projectRoot,
                            operation,
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

                    ValidateInstalledPublicationAt(
                        operation,
                        operation.target,
                        recovered.projectRoot,
                        recovered.transactionId);
                }
                else
                {
                    if (!string.Equals(operation.state, PreparedState, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"YooAsset terminal output changed before the terminal publication barrier for package '{operation.packageName}'.");
                    }

                    ValidateInstalledPublicationAt(
                        operation,
                        operation.stage,
                        recovered.projectRoot,
                        recovered.transactionId);
                }
            }
        }

        internal static bool CaptureActivatedSiblingMetasForRollback(Journal recovered)
        {
            bool changed = false;
            foreach (YooAsset3PublicationJournalOperation operation in recovered.operations)
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

                ValidateInstalledPublicationAt(
                    operation,
                    operation.target,
                    recovered.projectRoot,
                    recovered.transactionId);

                MetaFileSnapshot installed = CaptureMetaFile(recovered.projectRoot, operation.targetMeta);
                if (operation.originalMetaExisted)
                {
                    ValidateMetaSnapshot(
                        installed,
                        operation.targetMeta,
                        true,
                        operation.originalMetaLength,
                        operation.originalMetaSha256,
                        "activated bundled publication meta");
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

        internal static void ValidateInstalledSiblingMeta(
            Journal recovered,
            YooAsset3PublicationJournalOperation operation)
        {
            if (!operation.managesSiblingMeta)
            {
                return;
            }

            ValidateMetaFile(
                recovered.projectRoot,
                operation.targetMeta,
                operation.installedMetaExisted,
                operation.installedMetaLength,
                operation.installedMetaSha256,
                "installed bundled publication meta");
        }

        internal static void RestoreOriginalSiblingMeta(
            Journal recovered,
            YooAsset3PublicationJournalOperation operation)
        {
            if (!operation.managesSiblingMeta)
            {
                return;
            }

            ValidateMetaFile(
                recovered.projectRoot,
                operation.protectedMeta,
                operation.originalMetaExisted,
                operation.originalMetaLength,
                operation.originalMetaSha256,
                "protected bundled publication meta");
            MetaFileSnapshot targetMeta = CaptureMetaFile(recovered.projectRoot, operation.targetMeta);
            if (!operation.originalMetaExisted && targetMeta.Exists)
            {
                if (!operation.installedMetaExisted)
                {
                    throw new InvalidOperationException(
                        $"Bundled publication meta appeared without a durable installed identity: '{operation.targetMeta}'.");
                }

                ValidateMetaSnapshot(
                    targetMeta,
                    operation.targetMeta,
                    true,
                    operation.installedMetaLength,
                    operation.installedMetaSha256,
                    "activated bundled publication meta before rollback");
                YooAsset3BuildSafety.DeleteOwnedFile(
                    recovered.projectRoot,
                    operation.approvedRoot,
                    operation.targetMeta);
            }
            else if (targetMeta.Exists)
            {
                ValidateMetaSnapshot(
                    targetMeta,
                    operation.targetMeta,
                    operation.originalMetaExisted,
                    operation.originalMetaLength,
                    operation.originalMetaSha256,
                    "restored bundled publication meta");
            }
            else if (operation.originalMetaExisted)
            {
                CopyMetaFileDurably(operation.protectedMeta, operation.targetMeta);
                ValidateMetaFile(
                    recovered.projectRoot,
                    operation.targetMeta,
                    true,
                    operation.originalMetaLength,
                    operation.originalMetaSha256,
                    "restored bundled publication meta");
            }

            DeleteProtectedSiblingMeta(recovered, operation);
        }

        internal static void DeleteProtectedSiblingMeta(
            Journal recovered,
            YooAsset3PublicationJournalOperation operation)
        {
            if (!operation.managesSiblingMeta)
            {
                return;
            }

            ValidateMetaFile(
                recovered.projectRoot,
                operation.protectedMeta,
                operation.originalMetaExisted,
                operation.originalMetaLength,
                operation.originalMetaSha256,
                "protected bundled publication meta");
            if (operation.originalMetaExisted)
            {
                YooAsset3BuildSafety.DeleteOwnedFile(
                    recovered.projectRoot,
                    operation.approvedRoot,
                    operation.protectedMeta);
            }
        }

        internal static void DeleteProtectedSiblingMetaIfPresent(
            Journal recovered,
            YooAsset3PublicationJournalOperation operation)
        {
            if (!operation.managesSiblingMeta || !File.Exists(operation.protectedMeta))
            {
                if (operation.managesSiblingMeta && Directory.Exists(operation.protectedMeta))
                {
                    throw new InvalidOperationException(
                        $"Protected bundled publication meta became a directory: '{operation.protectedMeta}'.");
                }

                return;
            }

            ValidateMetaFile(
                recovered.projectRoot,
                operation.protectedMeta,
                true,
                operation.originalMetaLength,
                operation.originalMetaSha256,
                "protected bundled publication meta");
            YooAsset3BuildSafety.DeleteOwnedFile(
                recovered.projectRoot,
                operation.approvedRoot,
                operation.protectedMeta);
        }

        internal static MetaFileSnapshot CaptureMetaFile(string projectRoot, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException("Bundled publication meta path is missing.");
            }

            YooAsset3BuildSafety.ValidateNoPathRedirection(projectRoot, path);
            if (Directory.Exists(path))
            {
                throw new InvalidOperationException($"Bundled publication meta path became a directory: '{path}'.");
            }

            if (!File.Exists(path))
            {
                return MetaFileSnapshot.Missing;
            }

            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException($"Bundled publication meta is a reparse point: '{path}'.");
            }

            var before = new FileInfo(path);
            long length = before.Length;
            DateTime lastWriteUtc = before.LastWriteTimeUtc;
            if (length < 0 || length > MaximumSiblingMetaBytes)
            {
                throw new InvalidOperationException(
                    $"Bundled publication meta exceeds the {MaximumSiblingMetaBytes}-byte safety limit: '{path}'.");
            }

            byte[] content = new byte[(int)length];
            using (var stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       4096,
                       FileOptions.SequentialScan))
            {
                int offset = 0;
                while (offset < content.Length)
                {
                    int read = stream.Read(content, offset, content.Length - offset);
                    if (read <= 0)
                    {
                        throw new EndOfStreamException($"Bundled publication meta ended while reading: '{path}'.");
                    }

                    offset += read;
                }

                if (stream.ReadByte() >= 0)
                {
                    throw new InvalidOperationException($"Bundled publication meta grew while reading: '{path}'.");
                }
            }

            ValidateUnityFolderMeta(content, path);
            string sha256;
            using (SHA256 hash = SHA256.Create())
            {
                sha256 = BitConverter.ToString(hash.ComputeHash(content)).Replace("-", string.Empty);
            }

            var after = new FileInfo(path);
            if (!after.Exists || after.Length != length || after.LastWriteTimeUtc != lastWriteUtc)
            {
                throw new InvalidOperationException($"Bundled publication meta changed while hashing: '{path}'.");
            }

            return new MetaFileSnapshot(true, length, sha256);
        }

        internal static void ValidateUnityFolderMeta(byte[] content, string path)
        {
            string text;
            try
            {
                text = new UTF8Encoding(false, true).GetString(content);
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidOperationException(
                    $"Bundled publication meta is not valid UTF-8 text: '{path}'.",
                    exception);
            }

            bool hasFolderAsset = false;
            bool hasGuid = false;
            using (var reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string trimmed = line.Trim();
                    if (string.Equals(trimmed, "folderAsset: yes", StringComparison.Ordinal))
                    {
                        hasFolderAsset = true;
                    }
                    else if (trimmed.StartsWith("guid:", StringComparison.Ordinal))
                    {
                        string guid = trimmed.Substring("guid:".Length).Trim();
                        if (hasGuid || !IsHexToken(guid, 32))
                        {
                            throw new InvalidOperationException(
                                $"Bundled publication meta contains an invalid or duplicate GUID: '{path}'.");
                        }

                        hasGuid = true;
                    }
                }
            }

            if (!hasFolderAsset || !hasGuid)
            {
                throw new InvalidOperationException(
                    $"Bundled publication meta is not a Unity folder meta file: '{path}'.");
            }
        }

        internal static void ValidateMetaFile(
            string projectRoot,
            string path,
            bool expectedExists,
            long expectedLength,
            string expectedSha256,
            string description)
        {
            ValidateMetaSnapshot(
                CaptureMetaFile(projectRoot, path),
                path,
                expectedExists,
                expectedLength,
                expectedSha256,
                description);
        }

        internal static void ValidateMetaSnapshot(
            MetaFileSnapshot actual,
            string path,
            bool expectedExists,
            long expectedLength,
            string expectedSha256,
            string description)
        {
            if (actual.Exists != expectedExists ||
                actual.Exists &&
                (actual.Length != expectedLength ||
                 !string.Equals(actual.Sha256, expectedSha256, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"The {description} identity changed: '{path}'.");
            }
        }

        internal static void CopyMetaFileDurably(string source, string destination)
        {
            using (var input = new FileStream(
                       source,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       4096,
                       FileOptions.SequentialScan))
            using (var output = new FileStream(
                       destination,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                input.CopyTo(output);
                output.Flush(true);
            }
        }

        internal static void Rollback(
            Journal recovered,
            string journalPath,
            Action refreshAssets,
            Action<string> checkpoint = null)
        {
            checkpoint?.Invoke("RollbackStart");
            bool sourceQualificationPhase = IsSourceQualificationPhase(recovered.phase);
            if (sourceQualificationPhase)
            {
                NormalizeSourceQualificationForRollback(recovered);
            }
            else
            {
                CaptureActivatedSiblingMetasForRollback(recovered);
            }

            recovered.phase = RollingBackPhase;
            var failures = new List<Exception>();
            try
            {
                WriteJournal(recovered, journalPath, createNew: false);
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
                    YooAsset3PublicationJournalOperation operation = recovered.operations[index];
                    RollbackOperation(recovered, operation);
                    operation.state = PreparedState;
                    WriteJournal(recovered, journalPath, createNew: false);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            try
            {
                YooAsset3BuildSafety.DeleteOwnedDirectory(
                    recovered.projectRoot,
                    YooAsset3PublicationPaths.GetStateRoot(recovered.projectRoot, recovered.invocationId),
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

            CleanupOperationMetadata(recovered);
            ValidateRolledBackState(recovered);
            recovered.phase = RollbackRefreshPendingPhase;
            WriteJournal(recovered, journalPath, createNew: false);
            checkpoint?.Invoke("RollbackComplete");
            CompleteRollbackRefresh(recovered, journalPath, refreshAssets);
        }

        internal static void CompleteRollbackRefresh(
            Journal recovered,
            string journalPath,
            Action refreshAssets)
        {
            ValidateRolledBackState(recovered);
            bool requiresRefresh = recovered.operations.Any(operation =>
                operation.managesSiblingMeta);
            if (requiresRefresh && refreshAssets == null)
            {
                throw new InvalidOperationException(
                    "YooAsset rollback restored bundled Assets content, but no AssetDatabase refresh callback was supplied. " +
                    "The durable rollback journal was retained for explicit recovery.");
            }

            refreshAssets?.Invoke();
            ValidateRolledBackState(recovered);
            YooAsset3BuildSafety.DeleteOwnedFile(
                recovered.projectRoot,
                YooAsset3PublicationPaths.GetStateRoot(recovered.projectRoot, recovered.invocationId),
                journalPath);
            TryDeleteEmptyStateDirectories(
                recovered.projectRoot,
                recovered.invocationId);
        }

        internal static void ValidateRolledBackState(Journal recovered)
        {
            if (Directory.Exists(recovered.workRoot) || File.Exists(recovered.workRoot))
            {
                throw new InvalidOperationException(
                    $"YooAsset rollback work directory still exists: '{recovered.workRoot}'.");
            }

            foreach (YooAsset3PublicationJournalOperation operation in recovered.operations)
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
                    ValidateOriginalPublicationAt(
                        operation,
                        operation.target,
                        recovered.projectRoot);
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
                        ValidateMetaFile(
                            recovered.projectRoot,
                            operation.targetMeta,
                            expectedExists: false,
                            expectedLength: 0,
                            expectedSha256: string.Empty,
                            description: "rolled-back bundled publication meta");
                    }
                }
            }
        }

        internal static void RollbackOperation(Journal recovered, YooAsset3PublicationJournalOperation operation)
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

                ValidateOriginalPublicationAt(operation, operation.backup, recovered.projectRoot);
                ValidatePreRefreshSiblingMeta(
                    recovered.projectRoot,
                    operation,
                    allowMissingOriginalMeta: true);

                if (targetExists)
                {
                    ValidateInstalledPublicationAt(
                        operation,
                        operation.target,
                        recovered.projectRoot,
                        recovered.transactionId);
                    YooAsset3BuildSafety.DeleteOwnedDirectory(
                        recovered.projectRoot,
                        operation.approvedRoot,
                        operation.target);
                }

                if (Directory.Exists(operation.target))
                {
                    throw new InvalidOperationException($"Cannot restore publication backup over '{operation.target}'.");
                }

                Directory.Move(operation.backup, operation.target);
                RestoreOriginalSiblingMeta(recovered, operation);
                ValidateOriginalPublicationAt(operation, operation.target, recovered.projectRoot);
            }
            else if (operation.targetInitiallyExisted)
            {
                if (!targetExists)
                {
                    throw new InvalidOperationException(
                        $"The original publication target cannot be proven recoverable for package '{operation.packageName}'.");
                }

                ValidateOriginalPublicationAt(
                    operation,
                    operation.target,
                    recovered.projectRoot,
                    validateSiblingMeta: false);
                if (operation.managesSiblingMeta && File.Exists(operation.protectedMeta))
                {
                    RestoreOriginalSiblingMeta(recovered, operation);
                }
                else
                {
                    ValidateOriginalPublicationAt(operation, operation.target, recovered.projectRoot);
                    DeleteProtectedSiblingMetaIfPresent(recovered, operation);
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

                ValidateInstalledPublicationAt(
                    operation,
                    operation.target,
                    recovered.projectRoot,
                    recovered.transactionId);
                ValidatePreRefreshSiblingMeta(
                    recovered.projectRoot,
                    operation,
                    allowMissingOriginalMeta: false);
                YooAsset3BuildSafety.DeleteOwnedDirectory(
                    recovered.projectRoot,
                    operation.approvedRoot,
                    operation.target);
                if (operation.managesSiblingMeta)
                {
                    RestoreOriginalSiblingMeta(recovered, operation);
                }
            }

            if (!operation.targetInitiallyExisted && operation.managesSiblingMeta)
            {
                RestoreOriginalSiblingMeta(recovered, operation);
                ValidateMetaFile(
                    recovered.projectRoot,
                    operation.targetMeta,
                    expectedExists: false,
                    expectedLength: 0,
                    expectedSha256: string.Empty,
                    description: "rolled-back bundled publication meta");
            }

            DeleteStageIfOwned(recovered, operation);
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

        internal static void DeleteStageIfOwned(Journal recovered, YooAsset3PublicationJournalOperation operation)
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
                ValidateInstalledPublicationAt(
                    operation,
                    operation.stage,
                    recovered.projectRoot,
                    recovered.transactionId);
            }

            YooAsset3BuildSafety.DeleteOwnedDirectory(
                recovered.projectRoot,
                operation.approvedRoot,
                operation.stage);
        }

        internal static void CompletePendingRefresh(
            Journal recovered,
            string journalPath,
            Action refreshAssets,
            Action<string> checkpoint = null)
        {
            try
            {
                Dictionary<YooAsset3PublicationJournalOperation, MetaFileSnapshot> recoveryCandidates =
                    CaptureRefreshRecoveryMetaCandidates(recovered);
                if (refreshAssets == null)
                {
                    throw new InvalidOperationException("A refresh callback is required to recover a committed YooAsset publication.");
                }

                checkpoint?.Invoke("CommitRefreshPreRefresh");
                refreshAssets();
                checkpoint?.Invoke("CommitRefreshPostRefresh");
                CaptureInstalledSiblingMetas(recovered, recoveryCandidates);
                recovered.phase = CommittedPhase;
                WriteJournal(recovered, journalPath, createNew: false);
                CleanupCommitted(recovered, journalPath);
            }
            catch (Exception exception)
            {
                throw new YooAsset3CommittedPublicationException(
                    "YooAsset publication files are committed, but AssetDatabase refresh or committed-state cleanup still requires recovery.",
                    journalPath,
                    exception);
            }
        }

        internal static void ValidateCommittedPublications(Journal recovered)
        {
            foreach (YooAsset3PublicationJournalOperation operation in recovered.operations)
            {
                if (!string.Equals(operation.state, InstalledState, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Committed YooAsset publication contains a non-installed operation for package '{operation.packageName}'.");
                }

                ValidateInstalledPublicationAt(
                    operation,
                    operation.target,
                    recovered.projectRoot,
                    recovered.transactionId);
                ValidateInstalledSiblingMeta(recovered, operation);
            }
        }

        internal static void ValidatePreRefreshCommittedPublications(Journal recovered)
        {
            foreach (YooAsset3PublicationJournalOperation operation in recovered.operations)
            {
                if (!string.Equals(operation.state, InstalledState, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Committed YooAsset publication contains a non-installed operation for package '{operation.packageName}'.");
                }

                ValidateInstalledPublicationAt(
                    operation,
                    operation.target,
                    recovered.projectRoot,
                    recovered.transactionId);
                ValidatePreRefreshSiblingMeta(
                    recovered.projectRoot,
                    operation,
                    allowMissingOriginalMeta: false);
            }
        }

        internal static Dictionary<YooAsset3PublicationJournalOperation, MetaFileSnapshot>
            CaptureRefreshRecoveryMetaCandidates(Journal recovered)
        {
            var candidates = new Dictionary<YooAsset3PublicationJournalOperation, MetaFileSnapshot>();
            foreach (YooAsset3PublicationJournalOperation operation in recovered.operations)
            {
                if (!string.Equals(operation.state, InstalledState, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Committed YooAsset publication contains a non-installed operation for package '{operation.packageName}'.");
                }

                ValidateInstalledPublicationAt(
                    operation,
                    operation.target,
                    recovered.projectRoot,
                    recovered.transactionId);
                if (!operation.managesSiblingMeta || operation.originalMetaExisted)
                {
                    ValidatePreRefreshSiblingMeta(
                        recovered.projectRoot,
                        operation,
                        allowMissingOriginalMeta: false);
                    continue;
                }

                MetaFileSnapshot candidate = CaptureMetaFile(recovered.projectRoot, operation.targetMeta);
                if (candidate.Exists)
                {
                    candidates.Add(operation, candidate);
                }
            }

            return candidates;
        }

        internal static void CleanupCommitted(Journal recovered, string journalPath)
        {
            ValidateCommittedPublications(recovered);
            foreach (YooAsset3PublicationJournalOperation operation in recovered.operations)
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
                    ValidateOriginalPublicationAt(operation, operation.backup, recovered.projectRoot);
                    YooAsset3BuildSafety.DeleteOwnedDirectory(
                        recovered.projectRoot,
                        operation.approvedRoot,
                        operation.backup);
                    DeleteProtectedSiblingMeta(recovered, operation);
                }
                else
                {
                    DeleteProtectedSiblingMetaIfPresent(recovered, operation);
                }
            }

            YooAsset3BuildSafety.DeleteOwnedDirectory(
                recovered.projectRoot,
                YooAsset3PublicationPaths.GetStateRoot(recovered.projectRoot, recovered.invocationId),
                recovered.workRoot);
            CleanupOperationMetadata(recovered);
            YooAsset3BuildSafety.DeleteOwnedFile(
                recovered.projectRoot,
                YooAsset3PublicationPaths.GetStateRoot(recovered.projectRoot, recovered.invocationId),
                journalPath);
            TryDeleteEmptyStateDirectories(
                recovered.projectRoot,
                recovered.invocationId);
        }

        internal static void TryDeleteEmptyStateDirectories(
            string projectRoot,
            string invocationId)
        {
            string stateRoot = YooAsset3PublicationPaths.GetStateRoot(projectRoot, invocationId);
            TryDeleteEmptyStateDirectory(
                projectRoot,
                Path.Combine(stateRoot, "work"));
            TryDeleteEmptyStateDirectory(projectRoot, stateRoot);
            TryDeleteEmptyStateDirectory(
                projectRoot,
                YooAsset3PublicationPaths.GetProviderStateRoot(projectRoot));
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

            YooAsset3BuildSafety.ValidateNoPathRedirection(projectRoot, path);
            if (!Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path, recursive: false);
            }
        }

        internal static void CleanupOperationMetadata(Journal recovered)
        {
            foreach (YooAsset3PublicationJournalOperation operation in recovered.operations)
            {
                YooAsset3BuildSafety.DeleteOwnedFile(
                    recovered.projectRoot,
                    operation.approvedRoot,
                    operation.stage + ".meta");
                YooAsset3BuildSafety.DeleteOwnedFile(
                    recovered.projectRoot,
                    operation.approvedRoot,
                    operation.backup + ".meta");
            }
        }

        internal static void EnsureOperationCandidateAbsent(YooAsset3PublicationJournalOperation operation)
        {
            if (Directory.Exists(operation.stage) || File.Exists(operation.stage))
            {
                throw new InvalidOperationException($"Publication stage already exists: '{operation.stage}'.");
            }
        }

        internal static void NormalizeSourceQualificationForRollback(Journal value)
        {
            if (!IsSourceQualificationPhase(value.phase))
            {
                return;
            }

            for (int index = value.operations.Length - 1; index >= 0; index--)
            {
                YooAsset3PublicationJournalOperation operation = value.operations[index];
                if (!operation.managesSiblingMeta)
                {
                    continue;
                }

                SourceQualificationPaths paths = GetSourceQualificationPaths(value, index);
                ValidateSourceQualificationPath(value, paths.OperationRoot);
                if (Directory.Exists(paths.InstalledDirectory))
                {
                    EnsureDirectoryPathAbsent(operation.stage, "YooAsset bundled stage during recovery");
                    if (IsInstalledPublicationAtTarget(value, operation))
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
                        EnsureFilePathAbsent(
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
                    if (IsInstalledPublicationAtTarget(value, operation))
                    {
                        EnsureFilePathAbsent(
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

        internal static bool IsInstalledPublicationAtTarget(
            Journal value,
            YooAsset3PublicationJournalOperation operation)
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

            YooAsset3PublicationOwnership.PublicationSnapshot actual =
                YooAsset3PublicationOwnership.CaptureExisting(
                    value.projectRoot,
                    operation.target,
                    operation.kind,
                    operation.packageName);
            return actual.Owned
                   && string.Equals(actual.PackageVersion, operation.packageVersion, StringComparison.Ordinal)
                   && string.Equals(actual.CryptographyAdapterId, operation.cryptographyAdapterId, StringComparison.Ordinal)
                   && string.Equals(actual.RuntimeDecryptContractId, operation.runtimeDecryptContractId, StringComparison.Ordinal)
                   && string.Equals(actual.TransactionId, value.transactionId, StringComparison.Ordinal)
                   && string.Equals(actual.ContentIdentity, operation.installedContentIdentity, StringComparison.OrdinalIgnoreCase)
                   && actual.EntryCount == operation.installedEntryCount;
        }

        internal static void EnsureSourceQualificationRootCanBeCreated(
            Journal value,
            string suspensionRoot)
        {
            ValidateSourceQualificationPath(value, suspensionRoot);
            if (Directory.Exists(suspensionRoot) || File.Exists(suspensionRoot))
            {
                throw new InvalidOperationException(
                    $"YooAsset source qualification holding root is not empty: '{suspensionRoot}'.");
            }
        }

        internal static void EnsureSourceQualificationPathsAbsent(
            Journal value,
            YooAsset3PublicationJournalOperation operation,
            SourceQualificationPaths paths)
        {
            ValidateSourceQualificationPath(value, paths.OperationRoot);
            EnsureDirectoryPathAbsent(paths.OperationRoot, "YooAsset source qualification operation root");
            EnsureDirectoryPathAbsent(paths.InstalledDirectory, "YooAsset source qualification installed directory");
            EnsureFilePathAbsent(paths.InstalledMeta, "YooAsset source qualification installed meta");
            EnsureFilePathAbsent(paths.OriginalMeta, "YooAsset source qualification original meta");
            if (!operation.managesSiblingMeta)
            {
                throw new InvalidOperationException(
                    "Only YooAsset bundled operations may enter source qualification suspension.");
            }
        }

        internal static void DeleteSourceQualificationRoot(Journal value)
        {
            string suspensionRoot = GetSourceQualificationRoot(value);
            ValidateSourceQualificationPath(value, suspensionRoot);
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

        internal static string GetSourceQualificationRoot(Journal value)
        {
            return Path.GetFullPath(Path.Combine(
                value.workRoot,
                "source-qualification"));
        }

        internal static SourceQualificationPaths GetSourceQualificationPaths(
            Journal value,
            int operationIndex)
        {
            string operationRoot = Path.GetFullPath(Path.Combine(
                GetSourceQualificationRoot(value),
                operationIndex.ToString("D3", CultureInfo.InvariantCulture)));
            return new SourceQualificationPaths(
                operationRoot,
                Path.Combine(operationRoot, "installed"),
                Path.Combine(operationRoot, "installed.meta"),
                Path.Combine(operationRoot, "original.meta"));
        }

        internal static void ValidateSourceQualificationPath(
            Journal value,
            string path)
        {
            if (!YooAsset3BuildSafety.IsStrictDescendant(value.workRoot, path))
            {
                throw new InvalidOperationException(
                    $"YooAsset source qualification holding path escaped its transaction work root: '{path}'.");
            }

            YooAsset3BuildSafety.ValidateNoPathRedirection(value.projectRoot, path);
        }

        internal static void EnsureDirectoryPathAbsent(string path, string description)
        {
            if (Directory.Exists(path) || File.Exists(path))
            {
                throw new InvalidOperationException(
                    $"{description} must be absent: '{path}'.");
            }
        }

        internal static void EnsureFilePathAbsent(string path, string description)
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                throw new InvalidOperationException(
                    $"{description} must be absent: '{path}'.");
            }
        }

        internal static void CopyDirectorySafely(
            string projectRoot,
            string sourceDirectory,
            string destinationDirectory,
            string sourceApprovedRoot,
            string destinationApprovedRoot)
        {
            string source = Path.GetFullPath(sourceDirectory);
            string destination = Path.GetFullPath(destinationDirectory);
            if (!YooAsset3BuildSafety.IsStrictDescendant(sourceApprovedRoot, source) ||
                !YooAsset3BuildSafety.IsStrictDescendant(destinationApprovedRoot, destination))
            {
                throw new InvalidOperationException(
                    $"Transactional copy escaped an approved root. Source: '{source}', destination: '{destination}'.");
            }

            YooAsset3BuildSafety.ValidateNoPathRedirection(projectRoot, source);
            YooAsset3BuildSafety.ValidateNoPathRedirection(projectRoot, destination);
            if (!Directory.Exists(source))
            {
                throw new DirectoryNotFoundException($"Transactional copy source does not exist: '{source}'.");
            }

            if (Directory.Exists(destination) || File.Exists(destination))
            {
                throw new InvalidOperationException($"Transactional copy destination already exists: '{destination}'.");
            }

            var pending = new Stack<CopyDirectoryEntry>();
            pending.Push(new CopyDirectoryEntry(source, destination, 0));
            int entryCount = 0;
            long copiedBytes = 0;
            while (pending.Count > 0)
            {
                CopyDirectoryEntry current = pending.Pop();
                if (current.Depth > MaximumCopyDepth)
                {
                    throw new InvalidOperationException(
                        $"Transactional copy exceeds the maximum directory depth of {MaximumCopyDepth}: '{current.Source}'.");
                }

                BuildPathPolicy.EnsureWin32MaxDirectoryPathBudget(
                    current.Destination,
                    "YooAsset transactional copy directory");
                Directory.CreateDirectory(current.Destination);
                foreach (string entry in Directory.EnumerateFileSystemEntries(current.Source, "*", SearchOption.TopDirectoryOnly))
                {
                    entryCount++;
                    if (entryCount > MaximumCopiedEntries)
                    {
                        throw new InvalidOperationException(
                            $"Transactional copy exceeds the entry limit of {MaximumCopiedEntries}: '{source}'.");
                    }

                    FileAttributes attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidOperationException($"Transactional copy refuses a reparse-point entry: '{entry}'.");
                    }

                    string destinationEntry = Path.Combine(current.Destination, Path.GetFileName(entry));
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        BuildPathPolicy.EnsureWin32MaxDirectoryPathBudget(
                            destinationEntry,
                            "YooAsset transactional copy directory");
                        pending.Push(new CopyDirectoryEntry(entry, destinationEntry, current.Depth + 1));
                        continue;
                    }

                    BuildPathPolicy.EnsureWin32MaxPathBudget(
                        destinationEntry,
                        "YooAsset transactional copy artifact");

                    long length = new FileInfo(entry).Length;
                    copiedBytes = checked(copiedBytes + length);
                    if (copiedBytes > MaximumCopiedBytes)
                    {
                        throw new InvalidOperationException(
                            $"Transactional copy exceeds the byte budget of {MaximumCopiedBytes}: '{source}'.");
                    }

                    File.Copy(entry, destinationEntry, false);
                }
            }
        }

        internal static void ValidateDirectoryMovePathBudgets(
            string sourceDirectory,
            string destinationDirectory,
            string displayName)
        {
            BuildPathPolicy.EnsureWin32MaxDirectoryPathBudget(
                destinationDirectory,
                displayName + " root");
            if (!Directory.Exists(sourceDirectory))
            {
                return;
            }

            var pending = new Stack<CopyDirectoryEntry>();
            pending.Push(new CopyDirectoryEntry(sourceDirectory, destinationDirectory, 0));
            int entryCount = 0;
            while (pending.Count > 0)
            {
                CopyDirectoryEntry current = pending.Pop();
                if (current.Depth > MaximumCopyDepth)
                {
                    throw new InvalidOperationException(
                        $"{displayName} exceeds the maximum directory depth of {MaximumCopyDepth}: '{sourceDirectory}'.");
                }

                foreach (string entry in Directory.EnumerateFileSystemEntries(
                             current.Source,
                             "*",
                             SearchOption.TopDirectoryOnly))
                {
                    entryCount++;
                    if (entryCount > MaximumCopiedEntries)
                    {
                        throw new InvalidOperationException(
                            $"{displayName} exceeds the entry limit of {MaximumCopiedEntries}: '{sourceDirectory}'.");
                    }

                    string destination = Path.Combine(
                        current.Destination,
                        Path.GetFileName(entry));
                    FileAttributes attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidOperationException(
                            $"{displayName} contains a reparse-point entry: '{entry}'.");
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        BuildPathPolicy.EnsureWin32MaxDirectoryPathBudget(
                            destination,
                            displayName);
                        pending.Push(new CopyDirectoryEntry(
                            entry,
                            destination,
                            current.Depth + 1));
                    }
                    else
                    {
                        BuildPathPolicy.EnsureWin32MaxPathBudget(
                            destination,
                            displayName);
                    }
                }
            }
        }

        internal static void SuspendBundledOperation(
            Journal value,
            YooAsset3PublicationJournalOperation operation,
            int operationIndex,
            bool downstreamActive)
        {
            SourceQualificationPaths paths = GetSourceQualificationPaths(
                value,
                operationIndex);
            EnsureSourceQualificationPathsAbsent(value, operation, paths);
            Directory.CreateDirectory(paths.OperationRoot);
            YooAsset3BuildSafety.ValidateNoPathRedirection(
                value.projectRoot,
                paths.OperationRoot);

            if (downstreamActive)
            {
                ValidateActivatedBundledOperation(value, operation);
                Directory.Move(operation.target, paths.InstalledDirectory);
            }
            else
            {
                ValidatePreparedBundledOperation(value, operation);
                Directory.Move(operation.stage, paths.InstalledDirectory);
            }

            ValidateInstalledPublicationAt(
                operation,
                paths.InstalledDirectory,
                value.projectRoot,
                value.transactionId);

            if (!downstreamActive)
            {
                ValidateSourceQualificationOperationSuspended(
                    value,
                    operation,
                    paths,
                    downstreamActive: false);
                return;
            }

            if (operation.targetInitiallyExisted)
            {
                ValidateOriginalPublicationAt(
                    operation,
                    operation.backup,
                    value.projectRoot);
                Directory.Move(operation.backup, operation.target);
                ValidateOriginalPublicationAt(
                    operation,
                    operation.target,
                    value.projectRoot);

                ValidateMetaFile(
                    value.projectRoot,
                    operation.protectedMeta,
                    true,
                    operation.originalMetaLength,
                    operation.originalMetaSha256,
                    "protected bundled publication meta before source qualification");
                File.Move(operation.protectedMeta, paths.OriginalMeta);
            }
            else
            {
                EnsureDirectoryPathAbsent(operation.backup, "YooAsset bundled backup");
                ValidateMetaFile(
                    value.projectRoot,
                    operation.targetMeta,
                    true,
                    operation.installedMetaLength,
                    operation.installedMetaSha256,
                    "installed bundled publication meta before source qualification");
                File.Move(operation.targetMeta, paths.InstalledMeta);
            }

            ValidateSourceQualificationOperationSuspended(
                value,
                operation,
                paths,
                downstreamActive: true);
        }

        internal static void ResumeBundledOperation(
            Journal value,
            YooAsset3PublicationJournalOperation operation,
            int operationIndex,
            bool downstreamActive)
        {
            SourceQualificationPaths paths = GetSourceQualificationPaths(
                value,
                operationIndex);
            ValidateSourceQualificationOperationSuspended(
                value,
                operation,
                paths,
                downstreamActive);

            if (!downstreamActive)
            {
                Directory.Move(paths.InstalledDirectory, operation.stage);
                ValidatePreparedBundledOperation(value, operation);
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
                ValidateOriginalPublicationAt(
                    operation,
                    operation.backup,
                    value.projectRoot);
                File.Move(paths.OriginalMeta, operation.protectedMeta);
                ValidateMetaFile(
                    value.projectRoot,
                    operation.protectedMeta,
                    true,
                    operation.originalMetaLength,
                    operation.originalMetaSha256,
                    "protected bundled publication meta after source qualification");
            }

            Directory.Move(paths.InstalledDirectory, operation.target);
            if (!operation.targetInitiallyExisted)
            {
                File.Move(paths.InstalledMeta, operation.targetMeta);
            }

            ValidateActivatedBundledOperation(value, operation);
            if (Directory.EnumerateFileSystemEntries(paths.OperationRoot).Any())
            {
                throw new InvalidOperationException(
                    $"YooAsset source qualification holding directory retained unknown evidence: '{paths.OperationRoot}'.");
            }

            Directory.Delete(paths.OperationRoot, false);
        }

        internal static void ValidateSourceQualificationSuspended(
            Journal value,
            bool downstreamActive)
        {
            string suspensionRoot = GetSourceQualificationRoot(value);
            YooAsset3BuildSafety.ValidateNoPathRedirection(
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
                YooAsset3PublicationJournalOperation operation = value.operations[index];
                if (!operation.managesSiblingMeta)
                {
                    continue;
                }

                expectedOperationCount++;
                ValidateSourceQualificationOperationSuspended(
                    value,
                    operation,
                    GetSourceQualificationPaths(value, index),
                    downstreamActive);
            }

            if (Directory.GetFileSystemEntries(suspensionRoot).Length != expectedOperationCount)
            {
                throw new InvalidOperationException(
                    $"YooAsset source qualification holding root contains unknown evidence: '{suspensionRoot}'.");
            }
        }

        internal static void ValidateSourceQualificationOperationSuspended(
            Journal value,
            YooAsset3PublicationJournalOperation operation,
            SourceQualificationPaths paths,
            bool downstreamActive)
        {
            YooAsset3BuildSafety.ValidateNoPathRedirection(
                value.projectRoot,
                paths.OperationRoot);
            if (!Directory.Exists(paths.OperationRoot) || File.Exists(paths.OperationRoot))
            {
                throw new DirectoryNotFoundException(
                    $"YooAsset source qualification operation root does not exist: '{paths.OperationRoot}'.");
            }

            ValidateOriginalPublicationAt(
                operation,
                operation.target,
                value.projectRoot);
            ValidateInstalledPublicationAt(
                operation,
                paths.InstalledDirectory,
                value.projectRoot,
                value.transactionId);
            EnsureDirectoryPathAbsent(operation.stage, "YooAsset bundled stage");
            EnsureDirectoryPathAbsent(operation.backup, "YooAsset bundled backup");
            EnsureFilePathAbsent(operation.protectedMeta, "YooAsset protected bundled meta");

            if (!downstreamActive)
            {
                EnsureFilePathAbsent(paths.InstalledMeta, "YooAsset source qualification installed meta");
                EnsureFilePathAbsent(paths.OriginalMeta, "YooAsset source qualification original meta");
                if (Directory.GetFileSystemEntries(paths.OperationRoot).Length != 1)
                {
                    throw new InvalidOperationException(
                        $"YooAsset source qualification operation root contains unknown evidence: '{paths.OperationRoot}'.");
                }

                return;
            }

            if (operation.targetInitiallyExisted)
            {
                ValidateMetaFile(
                    value.projectRoot,
                    paths.OriginalMeta,
                    true,
                    operation.originalMetaLength,
                    operation.originalMetaSha256,
                    "source qualification protected original bundled meta");
                EnsureFilePathAbsent(paths.InstalledMeta, "YooAsset source qualification installed meta");
            }
            else
            {
                ValidateMetaFile(
                    value.projectRoot,
                    paths.InstalledMeta,
                    true,
                    operation.installedMetaLength,
                    operation.installedMetaSha256,
                    "source qualification installed bundled meta");
                EnsureFilePathAbsent(paths.OriginalMeta, "YooAsset source qualification original meta");
            }

            if (Directory.GetFileSystemEntries(paths.OperationRoot).Length != 2)
            {
                throw new InvalidOperationException(
                    $"YooAsset source qualification operation root contains unknown evidence: '{paths.OperationRoot}'.");
            }
        }

        internal static void ValidateActivatedBundledOperation(
            Journal value,
            YooAsset3PublicationJournalOperation operation)
        {
            ValidateInstalledPublicationAt(
                operation,
                operation.target,
                value.projectRoot,
                value.transactionId);
            ValidateInstalledSiblingMeta(value, operation);
            EnsureDirectoryPathAbsent(operation.stage, "YooAsset bundled stage");

            if (operation.targetInitiallyExisted)
            {
                ValidateOriginalPublicationAt(
                    operation,
                    operation.backup,
                    value.projectRoot);
                ValidateMetaFile(
                    value.projectRoot,
                    operation.protectedMeta,
                    true,
                    operation.originalMetaLength,
                    operation.originalMetaSha256,
                    "protected bundled publication meta after source qualification");
            }
            else
            {
                EnsureDirectoryPathAbsent(operation.backup, "YooAsset bundled backup");
                EnsureFilePathAbsent(operation.protectedMeta, "YooAsset protected bundled meta");
            }
        }

        internal static void ValidatePreparedForSourceQualification(Journal value)
        {
            foreach (YooAsset3PublicationJournalOperation operation in value.operations)
            {
                if (!string.Equals(operation.state, PreparedState, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"YooAsset prepared source qualification found a non-prepared operation for package '{operation.packageName}'.");
                }

                if (operation.managesSiblingMeta)
                {
                    ValidatePreparedBundledOperation(value, operation);
                }
                else
                {
                    ValidateInstalledPublicationAt(
                        operation,
                        operation.stage,
                        value.projectRoot,
                        value.transactionId);
                    ValidateOriginalPublicationAt(
                        operation,
                        operation.target,
                        value.projectRoot);
                }
            }
        }

        internal static void ValidatePreparedBundledOperation(
            Journal value,
            YooAsset3PublicationJournalOperation operation)
        {
            if (!string.Equals(operation.state, PreparedState, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"YooAsset bundled operation is not prepared for source qualification: '{operation.packageName}'.");
            }

            ValidateInstalledPublicationAt(
                operation,
                operation.stage,
                value.projectRoot,
                value.transactionId);
            ValidateOriginalPublicationAt(
                operation,
                operation.target,
                value.projectRoot);
            EnsureDirectoryPathAbsent(operation.backup, "YooAsset bundled backup");
            EnsureFilePathAbsent(operation.protectedMeta, "YooAsset protected bundled meta");
        }
    }
}
