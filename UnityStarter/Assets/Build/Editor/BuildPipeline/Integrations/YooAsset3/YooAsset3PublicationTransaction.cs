using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Build.Pipeline.Editor.Integrations.YooAsset3
{
    internal sealed class YooAsset3BuildLock : IDisposable
    {
        private const string LockDirectoryName = "YooAsset3Locks";
        private readonly FileStream[] streams;

        private YooAsset3BuildLock(FileStream[] streams)
        {
            this.streams = streams;
        }

        public static YooAsset3BuildLock Acquire(
            string projectRoot,
            string buildOutputRoot,
            string bundledFileRoot)
        {
            string normalizedProjectRoot = Path.GetFullPath(projectRoot);
            string lockRoot = GetLockRoot(normalizedProjectRoot);
            BuildPathPolicy.EnsureWin32MaxDirectoryPathBudget(
                lockRoot,
                "YooAsset publication lock root");
            YooAsset3BuildSafety.ValidateNoPathRedirection(normalizedProjectRoot, lockRoot);
            Directory.CreateDirectory(lockRoot);
            YooAsset3BuildSafety.ValidateNoPathRedirection(normalizedProjectRoot, lockRoot);

            string[] publicationRoots = new[]
                {
                    YooAsset3PublicationTransaction.GetProviderStateRoot(normalizedProjectRoot),
                    Path.GetFullPath(buildOutputRoot),
                    Path.GetFullPath(bundledFileRoot)
                }
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(root => root, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var acquired = new List<FileStream>(publicationRoots.Length);
            try
            {
                foreach (string publicationRoot in publicationRoots)
                {
                    string lockPath = GetLockPath(normalizedProjectRoot, publicationRoot);
                    BuildPathPolicy.EnsureWin32MaxPathBudget(
                        lockPath,
                        "YooAsset publication lock");
                    ValidateLockPath(normalizedProjectRoot, lockRoot, lockPath);
                    var stream = new FileStream(
                        lockPath,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None,
                        1,
                        FileOptions.WriteThrough);
                    try
                    {
                        ValidateLockPath(normalizedProjectRoot, lockRoot, lockPath);
                        acquired.Add(stream);
                    }
                    catch
                    {
                        stream.Dispose();
                        throw;
                    }
                }

                return new YooAsset3BuildLock(acquired.ToArray());
            }
            catch (Exception exception)
            {
                for (int index = acquired.Count - 1; index >= 0; index--)
                {
                    acquired[index].Dispose();
                }

                throw new InvalidOperationException(
                    "Another YooAsset publication owns one of the requested publication roots, or a lock path is unavailable. " +
                    exception.Message,
                    exception);
            }
        }

        internal static string GetLockRoot(string projectRoot)
        {
            return Path.GetFullPath(Path.Combine(projectRoot, "Temp", "BuildPipeline", LockDirectoryName));
        }

        internal static string GetLockPath(string projectRoot, string publicationRoot)
        {
            string portableRoot = Path.GetFullPath(publicationRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/')
                .ToUpperInvariant();
            string identity;
            using (SHA256 sha = SHA256.Create())
            {
                identity = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(portableRoot)))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }

            return Path.Combine(GetLockRoot(projectRoot), identity + ".lock");
        }

        private static void ValidateLockPath(string projectRoot, string lockRoot, string lockPath)
        {
            YooAsset3BuildSafety.ValidateNoPathRedirection(projectRoot, lockRoot);
            YooAsset3BuildSafety.ValidateNoPathRedirection(projectRoot, lockPath);
            if (!YooAsset3BuildSafety.IsStrictDescendant(lockRoot, lockPath) || Directory.Exists(lockPath))
            {
                throw new InvalidOperationException($"YooAsset publication lock path is invalid: '{lockPath}'.");
            }

            if (File.Exists(lockPath) && (File.GetAttributes(lockPath) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException($"YooAsset publication lock path is a reparse point: '{lockPath}'.");
            }
        }

        public void Dispose()
        {
            for (int index = streams.Length - 1; index >= 0; index--)
            {
                streams[index].Dispose();
            }
        }
    }

    internal sealed class YooAsset3CommittedPublicationException : InvalidOperationException
    {
        public YooAsset3CommittedPublicationException(string message, string journalPath, Exception innerException)
            : base(message, innerException)
        {
            JournalPath = journalPath ?? string.Empty;
        }

        public string JournalPath { get; }
    }

    [Serializable]
    internal sealed class YooAsset3PublicationJournalOperation
    {
        public string kind;
        public string packageName;
        public string packageVersion;
        public string cryptographyAdapterId;
        public string runtimeDecryptContractId;
        public string approvedRoot;
        public string target;
        public string stage;
        public string backup;
        public bool targetInitiallyExisted;
        public bool originalWasOwned;
        public string originalTransactionId;
        public string originalPackageVersion;
        public string originalCryptographyAdapterId;
        public string originalRuntimeDecryptContractId;
        public string originalContentIdentity;
        public int originalEntryCount;
        public string installedContentIdentity;
        public int installedEntryCount;
        public bool managesSiblingMeta;
        public string targetMeta;
        public string protectedMeta;
        public bool originalMetaExisted;
        public long originalMetaLength;
        public string originalMetaSha256;
        public bool installedMetaExisted;
        public long installedMetaLength;
        public string installedMetaSha256;
        public string state;
    }

    internal sealed class YooAsset3PackagePublication
    {
        public YooAsset3PackagePublication(
            YooAsset3PackageBuildPlan finalPlan,
            YooAsset3PublicationJournalOperation outputOperation,
            YooAsset3PublicationJournalOperation bundledOperation,
            string bundledWorkDirectory)
        {
            FinalPlan = finalPlan;
            OutputOperation = outputOperation;
            BundledOperation = bundledOperation;
            BundledWorkDirectory = bundledWorkDirectory ?? string.Empty;
        }

        public YooAsset3PackageBuildPlan FinalPlan { get; }
        public YooAsset3PublicationJournalOperation OutputOperation { get; }
        public YooAsset3PublicationJournalOperation BundledOperation { get; }
        public string BundledWorkDirectory { get; }
    }

    internal sealed class YooAsset3PublicationTransaction : IDisposable
    {
        private const string PublicationIdPrefix = "asset-content:yooasset:";
        internal const string StateRootRelativePath = ".buildpipeline/transactions/yooasset3";

        private const string JournalDocumentType = "yooasset-publication-transaction";
        private const int MaximumJournalBytes = 1024 * 1024;
        private const int MaximumOperationCount = 512;
        private const int MaximumCopiedEntries = 250000;
        private const int MaximumCopyDepth = 64;
        private const long MaximumCopiedBytes = 256L * 1024L * 1024L * 1024L;
        private const long MaximumSiblingMetaBytes = 1024L * 1024L;
        private const string ActiveJournalFileName = "active.json";
        private const string StagePrefix = ".yoo-stage-";
        private const string BackupPrefix = ".yoo-backup-";
        private const string PreparedPhase = "Prepared";
        private const string CommittingPhase = "Committing";
        private const string RollingBackPhase = "RollingBack";
        private const string RollbackRefreshPendingPhase = "RollbackRefreshPending";
        private const string ActivationRefreshPendingPhase = "ActivationRefreshPending";
        private const string DownstreamActivePhase = "DownstreamActive";
        private const string SourceQualificationSuspendingPhase = "SourceQualificationSuspending";
        private const string SourceQualificationSuspendedPhase = "SourceQualificationSuspended";
        private const string SourceQualificationResumingPhase = "SourceQualificationResuming";
        private const string AwaitingDecisionPhase = "AwaitingDecision";
        private const string RefreshPendingPhase = "RefreshPending";
        private const string CommittedPhase = "Committed";
        private const string PreparedState = "Prepared";
        private const string BackupPendingState = "BackupPending";
        private const string BackedUpState = "BackedUp";
        private const string InstalledState = "Installed";

        private readonly string projectRoot;
        private readonly string buildOutputRoot;
        private readonly string bundledFileRoot;
        private readonly string invocationId;
        private readonly string publicationId;
        private readonly string stateRelativePath;
        private readonly string stateRoot;
        private readonly string activeJournalPath;
        private readonly Journal journal;
        private readonly YooAsset3PackagePublication[] packages;
        private bool prepared;
        private bool completed;
        private bool disposed;
        private bool sourceQualificationScopeActive;
        private string sourceQualificationResumePhase = string.Empty;

        private YooAsset3PublicationTransaction(
            string projectRoot,
            string buildOutputRoot,
            string bundledFileRoot,
            string invocationId,
            Journal journal,
            YooAsset3PackagePublication[] packages)
        {
            this.projectRoot = projectRoot;
            this.buildOutputRoot = buildOutputRoot;
            this.bundledFileRoot = bundledFileRoot;
            this.invocationId = NormalizeInvocationId(invocationId);
            publicationId = GetPublicationId(this.invocationId);
            stateRelativePath = GetStateRelativePath(this.invocationId);
            stateRoot = GetStateRoot(projectRoot, this.invocationId);
            activeJournalPath = Path.Combine(stateRoot, ActiveJournalFileName);
            this.journal = journal;
            this.packages = packages;
        }

        public IReadOnlyList<YooAsset3PackagePublication> Packages => packages;
        internal bool HasDownstreamInputs => packages.Any(package => package.BundledOperation != null);
        internal string PublicationId => publicationId;
        internal string StateRelativePath => stateRelativePath;

        public static string GetProviderStateRoot(string projectRoot)
        {
            return Path.GetFullPath(Path.Combine(
                projectRoot,
                StateRootRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        public static string GetStateRoot(
            string projectRoot,
            string invocationId)
        {
            return Path.Combine(
                GetProviderStateRoot(projectRoot),
                NormalizeInvocationId(invocationId));
        }

        internal static string GetStateRelativePath(string invocationId)
        {
            return StateRootRelativePath + "/" + NormalizeInvocationId(invocationId);
        }

        internal static string GetPublicationId(string invocationId)
        {
            return PublicationIdPrefix + NormalizeInvocationId(invocationId);
        }

        private static string NormalizeInvocationId(string invocationId)
        {
            BuildIdentityPolicy.ValidateBuildIdentifier(
                invocationId,
                "YooAsset content invocation id");
            BuildPathPolicy.ValidatePortableFileName(
                invocationId,
                "YooAsset content invocation state directory",
                BuildIdentityPolicy.MaximumBuildIdentifierCharacters);
            return invocationId;
        }

        private static bool IsValidInvocationId(string invocationId)
        {
            try
            {
                NormalizeInvocationId(invocationId);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        public static YooAsset3PublicationTransaction Create(
            YooAsset3BuildPlan plan,
            string invocationId)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            string normalizedInvocationId = NormalizeInvocationId(invocationId);
            string transactionId = Guid.NewGuid().ToString("N");
            string stateRoot = GetStateRoot(
                plan.ProjectRoot,
                normalizedInvocationId);
            string workRoot = Path.GetFullPath(Path.Combine(stateRoot, "work", transactionId));
            var operations = new List<YooAsset3PublicationJournalOperation>(plan.Packages.Length * 2);
            var publications = new YooAsset3PackagePublication[plan.Packages.Length];

            for (int index = 0; index < plan.Packages.Length; index++)
            {
                YooAsset3PackageBuildPlan packagePlan = plan.Packages[index];
                string suffix = transactionId + "-" + index.ToString("D3", CultureInfo.InvariantCulture);
                YooAsset3PublicationJournalOperation outputOperation = CreateOperation(
                    plan.ProjectRoot,
                    YooAsset3PublicationOwnership.PackageOutputKind,
                    packagePlan.PackageName,
                    packagePlan.PackageVersion,
                    packagePlan.CryptographyAdapterId,
                    packagePlan.RuntimeDecryptContractId,
                    plan.BuildOutputRoot,
                    packagePlan.OutputPackageDirectory,
                    suffix);
                operations.Add(outputOperation);

                YooAsset3PublicationJournalOperation bundledOperation = null;
                string bundledWorkDirectory = string.Empty;
                if (packagePlan.Parameters.BundledCopyOption != YooAsset.Editor.EBundledCopyOption.None)
                {
                    bundledOperation = CreateOperation(
                        plan.ProjectRoot,
                        YooAsset3PublicationOwnership.BundledPackageKind,
                        packagePlan.PackageName,
                        packagePlan.PackageVersion,
                        packagePlan.CryptographyAdapterId,
                        packagePlan.RuntimeDecryptContractId,
                        plan.BundledFileRoot,
                        packagePlan.BundledPackageDirectory,
                        suffix);
                    operations.Add(bundledOperation);
                    bundledWorkDirectory = Path.GetFullPath(Path.Combine(
                        workRoot,
                        "bundled",
                        index.ToString("D3", CultureInfo.InvariantCulture)));
                }

                publications[index] = new YooAsset3PackagePublication(
                    packagePlan,
                    outputOperation,
                    bundledOperation,
                    bundledWorkDirectory);
            }

            var journal = new Journal
            {
                documentType = JournalDocumentType,
                invocationId = normalizedInvocationId,
                transactionId = transactionId,
                phase = PreparedPhase,
                projectRoot = Path.GetFullPath(plan.ProjectRoot),
                buildOutputRoot = Path.GetFullPath(plan.BuildOutputRoot),
                bundledFileRoot = Path.GetFullPath(plan.BundledFileRoot),
                workRoot = workRoot,
                operations = operations.ToArray()
            };

            ValidateTransactionPathBudgets(journal, publications);
            return new YooAsset3PublicationTransaction(
                journal.projectRoot,
                journal.buildOutputRoot,
                journal.bundledFileRoot,
                normalizedInvocationId,
                journal,
                publications);
        }

        private static void ValidateTransactionPathBudgets(
            Journal value,
            IEnumerable<YooAsset3PackagePublication> packagePublications)
        {
            ValidateJournalPathBudgets(value);
            foreach (YooAsset3PackagePublication publication in packagePublications)
            {
                if (!string.IsNullOrEmpty(publication.BundledWorkDirectory))
                {
                    BuildPathPolicy.EnsureWin32MaxDirectoryPathBudget(
                        publication.BundledWorkDirectory,
                        $"YooAsset bundled work directory '{publication.FinalPlan.PackageName}'",
                        65);
                }
            }
        }

        private static void ValidateJournalPathBudgets(Journal value)
        {
            string stateRoot = GetStateRoot(
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

        public static void RecoverPending(string projectRoot, Action refreshAssets)
        {
            string normalizedProjectRoot = Path.GetFullPath(projectRoot);
            string providerStateRoot = GetProviderStateRoot(normalizedProjectRoot);
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
                    NormalizeInvocationId(Path.GetFileName(invocationStateRoot)),
                    refreshAssets);
            }
        }

        private static void RecoverPendingInvocation(
            string normalizedProjectRoot,
            string invocationId,
            Action refreshAssets)
        {
            string stateRoot = GetStateRoot(normalizedProjectRoot, invocationId);
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
                GetPublicationId(invocationId),
                GetStateRelativePath(invocationId));
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
            string stateRoot = GetStateRoot(
                normalizedProjectRoot,
                NormalizeInvocationId(invocationId));
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

        public void Prepare()
        {
            ThrowIfDisposed();
            if (prepared)
            {
                throw new InvalidOperationException("The YooAsset publication transaction is already prepared.");
            }

            YooAsset3BuildSafety.ValidateNoPathRedirection(projectRoot, stateRoot);
            YooAsset3BuildSafety.ValidateNoPathRedirection(projectRoot, activeJournalPath);
            Directory.CreateDirectory(stateRoot);
            YooAsset3BuildSafety.ValidateNoPathRedirection(projectRoot, stateRoot);
            YooAsset3BuildSafety.ValidateNoPathRedirection(projectRoot, activeJournalPath);
            if (File.Exists(activeJournalPath))
            {
                throw new InvalidOperationException(
                    $"A pending YooAsset publication journal must be recovered before starting a new transaction: '{activeJournalPath}'.");
            }

            EnsureNoDetachedState(stateRoot);
            foreach (YooAsset3PublicationJournalOperation operation in journal.operations)
            {
                ValidateOperation(operation, projectRoot, buildOutputRoot, bundledFileRoot, journal.transactionId);
            }

            EnsureNoOrphanOperationDirectories(journal.operations);
            foreach (YooAsset3PublicationJournalOperation operation in journal.operations)
            {
                CaptureOriginalPublication(operation);
            }

            WriteJournal(journal, activeJournalPath, createNew: true);
            prepared = true;

            foreach (YooAsset3PackagePublication package in packages)
            {
                if (package.BundledOperation == null || !RequiresBundledSeed(package.FinalPlan.Profile.bundledCopyOption))
                {
                    continue;
                }

                if (Directory.Exists(package.BundledOperation.target))
                {
                    CopyDirectorySafely(
                        projectRoot,
                        package.BundledOperation.target,
                        package.BundledWorkDirectory,
                        package.BundledOperation.approvedRoot,
                        journal.workRoot);
                }
            }
        }

        public YooAsset3PackageBuildPlan CreateExecutionPlan(
            AssetContentBuildRequest request,
            YooAsset3PackagePublication publication)
        {
            ThrowIfDisposed();
            if (!prepared)
            {
                throw new InvalidOperationException("Prepare the YooAsset publication transaction before creating execution plans.");
            }

            YooAsset3PackageBuildPlan executionPlan = YooAsset3BuildParameterFactory.Create(
                request,
                publication.FinalPlan.Profile,
                buildOutputRoot,
                bundledFileRoot,
                publication.FinalPlan.BundledCopyParams,
                publication.OutputOperation.stage,
                publication.BundledOperation == null
                    ? Path.Combine(journal.workRoot, "unused-bundled", publication.FinalPlan.PackageName)
                    : publication.BundledWorkDirectory);
            if (!string.Equals(
                    executionPlan.CryptographyAdapterId,
                    publication.FinalPlan.CryptographyAdapterId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    executionPlan.RuntimeDecryptContractId,
                    publication.FinalPlan.RuntimeDecryptContractId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"YooAsset cryptography identity changed between preflight and execution for package '{publication.FinalPlan.PackageName}'.");
            }

            return executionPlan;
        }

        public void PrepareReadyDirectories()
        {
            ThrowIfDisposed();
            foreach (YooAsset3PackagePublication package in packages)
            {
                YooAsset3PublicationJournalOperation bundledOperation = package.BundledOperation;
                if (bundledOperation == null)
                {
                    continue;
                }

                if (!Directory.Exists(package.BundledWorkDirectory))
                {
                    throw new DirectoryNotFoundException(
                        $"YooAsset did not produce its staged bundled package directory: '{package.BundledWorkDirectory}'.");
                }

                EnsureOperationCandidateAbsent(bundledOperation);
                CopyDirectorySafely(
                    projectRoot,
                    package.BundledWorkDirectory,
                    bundledOperation.stage,
                    journal.workRoot,
                    bundledOperation.approvedRoot);
            }
        }

        public void SealReadyDirectories()
        {
            ThrowIfDisposed();
            if (!prepared)
            {
                throw new InvalidOperationException("Prepare the YooAsset publication transaction before sealing its stages.");
            }

            foreach (YooAsset3PublicationJournalOperation operation in journal.operations)
            {
                YooAsset3PublicationOwnership.PublicationSnapshot sealedStage = YooAsset3PublicationOwnership.Seal(
                    projectRoot,
                    operation.stage,
                    operation.kind,
                    operation.packageName,
                    operation.packageVersion,
                    operation.cryptographyAdapterId,
                    operation.runtimeDecryptContractId,
                    journal.transactionId);
                operation.installedContentIdentity = sealedStage.ContentIdentity;
                operation.installedEntryCount = sealedStage.EntryCount;
            }

            WriteJournal(journal, activeJournalPath, createNew: false);
        }

        internal void Publish(
            Action validatePublishedState,
            Action refreshAssets)
        {
            ThrowIfDisposed();
            if (!prepared)
            {
                throw new InvalidOperationException("Prepare the YooAsset publication transaction before publishing it.");
            }

            try
            {
                if (!string.Equals(journal.phase, PreparedPhase, StringComparison.Ordinal)
                    && !string.Equals(journal.phase, DownstreamActivePhase, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"YooAsset publication cannot publish terminal outputs from phase '{journal.phase}'.");
                }

                YooAsset3PublicationJournalOperation[] pending = journal.operations
                    .Where(operation => string.Equals(
                        operation.state,
                        PreparedState,
                        StringComparison.Ordinal))
                    .ToArray();
                if (pending.Length == 0)
                {
                    throw new InvalidOperationException(
                        "YooAsset publication has no pending terminal output operations.");
                }

                ValidateReadyToCommit(pending);
                journal.phase = CommittingPhase;
                WriteJournal(journal, activeJournalPath, createNew: false);
                foreach (YooAsset3PublicationJournalOperation operation in pending)
                {
                    CommitOperation(operation);
                }

                validatePublishedState?.Invoke();
                ValidatePreRefreshCommittedPublications(journal);
                journal.phase = AwaitingDecisionPhase;
                WriteJournal(journal, activeJournalPath, createNew: false);
            }
            catch (Exception publicationException)
            {
                try
                {
                    Rollback(journal, activeJournalPath, refreshAssets);
                    completed = true;
                }
                catch (Exception rollbackException)
                {
                    throw new AggregateException(
                        "YooAsset publication failed and rollback did not complete. The durable journal was retained for recovery.",
                        publicationException,
                        rollbackException);
                }

                throw;
            }
        }

        internal void ActivateDownstreamInputs(Action refreshAssets)
        {
            ThrowIfDisposed();
            if (!HasDownstreamInputs)
            {
                return;
            }

            if (!prepared || !string.Equals(journal.phase, PreparedPhase, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "YooAsset bundled inputs can only be activated from the prepared phase.");
            }

            if (refreshAssets == null)
            {
                throw new ArgumentNullException(nameof(refreshAssets));
            }

            YooAsset3PublicationJournalOperation[] bundled = journal.operations
                .Where(operation => operation.managesSiblingMeta)
                .ToArray();
            try
            {
                ValidateReadyToCommit(bundled);
                journal.phase = CommittingPhase;
                WriteJournal(journal, activeJournalPath, createNew: false);
                foreach (YooAsset3PublicationJournalOperation operation in bundled)
                {
                    CommitOperation(operation);
                }

                ValidateDownstreamInputs(journal, afterRefresh: false);
                journal.phase = ActivationRefreshPendingPhase;
                WriteJournal(journal, activeJournalPath, createNew: false);
                refreshAssets();
                CaptureInstalledSiblingMetas(journal, recoveryCandidates: null);
                journal.phase = DownstreamActivePhase;
                WriteJournal(journal, activeJournalPath, createNew: false);
            }
            catch
            {
                CaptureActivatedSiblingMetasForRollback(journal);
                if (bundled.All(operation => string.Equals(
                    operation.state,
                    InstalledState,
                    StringComparison.Ordinal)))
                {
                    journal.phase = DownstreamActivePhase;
                }

                WriteJournal(journal, activeJournalPath, createNew: false);
                throw;
            }
        }

        internal void ValidateActivatedInputs()
        {
            ThrowIfDisposed();
            if (!HasDownstreamInputs)
            {
                return;
            }

            if (!string.Equals(journal.phase, DownstreamActivePhase, StringComparison.Ordinal)
                && !string.Equals(journal.phase, AwaitingDecisionPhase, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "YooAsset bundled inputs are not active at the terminal decision boundary.");
            }

            ValidateDownstreamInputs(journal, afterRefresh: true);
        }

        internal IDisposable SuspendForSourceQualification()
        {
            ThrowIfDisposed();
            if (!HasDownstreamInputs)
            {
                return SourceQualificationScope.Empty;
            }

            if (sourceQualificationScopeActive)
            {
                throw new InvalidOperationException(
                    "YooAsset bundled inputs are already suspended for source qualification.");
            }

            bool downstreamActive = string.Equals(
                journal.phase,
                DownstreamActivePhase,
                StringComparison.Ordinal);
            bool preparedOnly = string.Equals(
                journal.phase,
                PreparedPhase,
                StringComparison.Ordinal);
            if (!prepared || (!downstreamActive && !preparedOnly))
            {
                throw new InvalidOperationException(
                    $"YooAsset bundled inputs can only be suspended for source qualification from phase '{PreparedPhase}' or '{DownstreamActivePhase}', " +
                    $"but the transaction is in phase '{journal.phase}'.");
            }

            if (downstreamActive)
            {
                ValidateDownstreamInputs(journal, afterRefresh: true);
            }
            else
            {
                ValidatePreparedForSourceQualification(journal);
            }

            sourceQualificationResumePhase = journal.phase;
            journal.phase = SourceQualificationSuspendingPhase;
            WriteJournal(journal, activeJournalPath, createNew: false);

            try
            {
                string suspensionRoot = GetSourceQualificationRoot(journal);
                EnsureSourceQualificationRootCanBeCreated(journal, suspensionRoot);
                Directory.CreateDirectory(suspensionRoot);
                YooAsset3BuildSafety.ValidateNoPathRedirection(projectRoot, suspensionRoot);

                for (int index = journal.operations.Length - 1; index >= 0; index--)
                {
                    YooAsset3PublicationJournalOperation operation = journal.operations[index];
                    if (!operation.managesSiblingMeta)
                    {
                        continue;
                    }

                    SuspendBundledOperation(
                        journal,
                        operation,
                        index,
                        downstreamActive);
                }

                ValidateSourceQualificationSuspended(
                    journal,
                    downstreamActive);
                journal.phase = SourceQualificationSuspendedPhase;
                WriteJournal(journal, activeJournalPath, createNew: false);
                sourceQualificationScopeActive = true;
                return new SourceQualificationScope(this);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "YooAsset could not restore the exact pre-build bundled source tree for source qualification. " +
                    "The durable publication journal was retained so normal build rollback or workspace recovery can restore the original state.",
                    exception);
            }
        }

        private void ResumeAfterSourceQualification()
        {
            ThrowIfDisposed();
            if (!sourceQualificationScopeActive)
            {
                throw new InvalidOperationException(
                    "YooAsset source qualification suspension is not active.");
            }

            if (!string.Equals(journal.phase, SourceQualificationSuspendedPhase, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"YooAsset source qualification suspension cannot resume from phase '{journal.phase}'.");
            }

            bool downstreamActive = string.Equals(
                sourceQualificationResumePhase,
                DownstreamActivePhase,
                StringComparison.Ordinal);
            bool preparedOnly = string.Equals(
                sourceQualificationResumePhase,
                PreparedPhase,
                StringComparison.Ordinal);
            if (!downstreamActive && !preparedOnly)
            {
                throw new InvalidOperationException(
                    "YooAsset source qualification suspension lost its resume phase.");
            }

            ValidateSourceQualificationSuspended(
                journal,
                downstreamActive);
            journal.phase = SourceQualificationResumingPhase;
            WriteJournal(journal, activeJournalPath, createNew: false);

            try
            {
                for (int index = 0; index < journal.operations.Length; index++)
                {
                    YooAsset3PublicationJournalOperation operation = journal.operations[index];
                    if (!operation.managesSiblingMeta)
                    {
                        continue;
                    }

                    ResumeBundledOperation(
                        journal,
                        operation,
                        index,
                        downstreamActive);
                }

                if (downstreamActive)
                {
                    ValidateDownstreamInputs(journal, afterRefresh: true);
                }
                else
                {
                    ValidatePreparedForSourceQualification(journal);
                }

                DeleteSourceQualificationRoot(journal);
                journal.phase = sourceQualificationResumePhase;
                WriteJournal(journal, activeJournalPath, createNew: false);
                sourceQualificationScopeActive = false;
                sourceQualificationResumePhase = string.Empty;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "YooAsset could not reactivate its bundled downstream inputs after source qualification. " +
                    "The durable publication journal was retained so normal build rollback or workspace recovery can restore the original state.",
                    exception);
            }
        }

        private static void SuspendBundledOperation(
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

        private static void ResumeBundledOperation(
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

        private static void ValidateSourceQualificationSuspended(
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

        private static void ValidateSourceQualificationOperationSuspended(
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

        private static void ValidateActivatedBundledOperation(
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

        private static void ValidatePreparedForSourceQualification(Journal value)
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

        private static void ValidatePreparedBundledOperation(
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

        private static void NormalizeSourceQualificationForRollback(Journal value)
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

        private static bool IsInstalledPublicationAtTarget(
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

        private static void EnsureSourceQualificationRootCanBeCreated(
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

        private static void EnsureSourceQualificationPathsAbsent(
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

        private static void DeleteSourceQualificationRoot(Journal value)
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

        private static string GetSourceQualificationRoot(Journal value)
        {
            return Path.GetFullPath(Path.Combine(
                value.workRoot,
                "source-qualification"));
        }

        private static SourceQualificationPaths GetSourceQualificationPaths(
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

        private static void ValidateSourceQualificationPath(
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

        private static void EnsureDirectoryPathAbsent(string path, string description)
        {
            if (Directory.Exists(path) || File.Exists(path))
            {
                throw new InvalidOperationException(
                    $"{description} must be absent: '{path}'.");
            }
        }

        private static void EnsureFilePathAbsent(string path, string description)
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                throw new InvalidOperationException(
                    $"{description} must be absent: '{path}'.");
            }
        }

        internal void Complete(Action refreshAssets)
        {
            ThrowIfDisposed();
            if (!prepared || !string.Equals(journal.phase, AwaitingDecisionPhase, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "YooAsset publication has not reached the terminal decision barrier.");
            }

            BuildPublicationDecision decision = BuildPublicationBarrier.GetDecision(
                projectRoot,
                PublicationId,
                StateRelativePath);
            if (decision != BuildPublicationDecision.Commit)
            {
                throw new InvalidOperationException(
                    "YooAsset publication completion requires an explicit durable Commit decision from the terminal barrier.");
            }

            // Complete is invoked only after the shared barrier has persisted its
            // commit decision. From this point disposal must preserve evidence for
            // explicit recovery instead of attempting a contradictory rollback.
            completed = true;
            try
            {
                ValidatePreRefreshCommittedPublications(journal);
                journal.phase = RefreshPendingPhase;
                WriteJournal(journal, activeJournalPath, createNew: false);
                CompletePendingRefresh(journal, activeJournalPath, refreshAssets);
            }
            catch (YooAsset3CommittedPublicationException)
            {
                throw;
            }
            catch (Exception completionException)
            {
                throw new YooAsset3CommittedPublicationException(
                    "YooAsset publication was selected by the terminal commit barrier, but durable refresh finalization did not complete. " +
                    "The journal and backups were retained for explicit recovery.",
                    activeJournalPath,
                    completionException);
            }
        }

        public void Abort(Action refreshAssets)
        {
            ThrowIfDisposed();
            if (completed)
            {
                return;
            }

            if (prepared && File.Exists(activeJournalPath))
            {
                if (BuildPublicationBarrier.GetDecision(
                        projectRoot,
                        PublicationId,
                        StateRelativePath)
                    == BuildPublicationDecision.Commit)
                {
                    completed = true;
                    return;
                }

                Rollback(journal, activeJournalPath, refreshAssets);
            }

            completed = true;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            if (!completed)
            {
                Abort(refreshAssets: null);
            }

            disposed = true;
        }

        private static YooAsset3PublicationJournalOperation CreateOperation(
            string projectRoot,
            string kind,
            string packageName,
            string packageVersion,
            string cryptographyAdapterId,
            string runtimeDecryptContractId,
            string approvedRoot,
            string target,
            string suffix)
        {
            string normalizedTarget = Path.GetFullPath(target);
            string parent = Path.GetDirectoryName(normalizedTarget);
            if (string.IsNullOrEmpty(parent))
            {
                throw new InvalidOperationException($"Publication target does not have a parent directory: '{normalizedTarget}'.");
            }

            string stage = Path.Combine(parent, StagePrefix + suffix);
            string backup = Path.Combine(parent, BackupPrefix + suffix);
            string streamingAssetsRoot = Path.GetFullPath(Path.Combine(projectRoot, "Assets", "StreamingAssets"));
            bool managesSiblingMeta =
                string.Equals(kind, YooAsset3PublicationOwnership.BundledPackageKind, StringComparison.Ordinal) &&
                (YooAsset3BuildSafety.PathsEqual(streamingAssetsRoot, normalizedTarget) ||
                 YooAsset3BuildSafety.IsStrictDescendant(streamingAssetsRoot, normalizedTarget));
            return new YooAsset3PublicationJournalOperation
            {
                kind = kind,
                packageName = packageName,
                packageVersion = packageVersion,
                cryptographyAdapterId = cryptographyAdapterId,
                runtimeDecryptContractId = runtimeDecryptContractId,
                approvedRoot = Path.GetFullPath(approvedRoot),
                target = normalizedTarget,
                stage = stage,
                backup = backup,
                managesSiblingMeta = managesSiblingMeta,
                targetMeta = managesSiblingMeta ? normalizedTarget + ".meta" : string.Empty,
                protectedMeta = managesSiblingMeta ? backup + ".root-meta" : string.Empty,
                state = PreparedState
            };
        }

        private void CaptureOriginalPublication(YooAsset3PublicationJournalOperation operation)
        {
            ValidateOperation(operation, projectRoot, buildOutputRoot, bundledFileRoot, journal.transactionId);
            YooAsset3PublicationOwnership.PublicationSnapshot original = YooAsset3PublicationOwnership.CaptureExisting(
                projectRoot,
                operation.target,
                operation.kind,
                operation.packageName);
            operation.targetInitiallyExisted = original.Exists;
            operation.originalWasOwned = original.Owned;
            operation.originalTransactionId = original.TransactionId;
            operation.originalPackageVersion = original.PackageVersion;
            operation.originalCryptographyAdapterId = original.CryptographyAdapterId;
            operation.originalRuntimeDecryptContractId = original.RuntimeDecryptContractId;
            operation.originalContentIdentity = original.ContentIdentity;
            operation.originalEntryCount = original.EntryCount;
            if (!operation.managesSiblingMeta)
            {
                return;
            }

            MetaFileSnapshot originalMeta = CaptureMetaFile(projectRoot, operation.targetMeta);
            if (original.Exists != originalMeta.Exists)
            {
                throw new InvalidOperationException(
                    $"Bundled publication directory and its sibling meta file must either both exist or both be absent: " +
                    $"'{operation.target}', '{operation.targetMeta}'.");
            }

            operation.originalMetaExisted = originalMeta.Exists;
            operation.originalMetaLength = originalMeta.Length;
            operation.originalMetaSha256 = originalMeta.Sha256;
        }

        private void ValidateReadyToCommit(
            IReadOnlyList<YooAsset3PublicationJournalOperation> operations)
        {
            if (operations == null || operations.Count == 0)
            {
                throw new InvalidOperationException(
                    "YooAsset publication has no operations to commit.");
            }

            foreach (YooAsset3PublicationJournalOperation operation in operations)
            {
                if (operation == null ||
                    !string.Equals(operation.state, PreparedState, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "YooAsset publication can only commit prepared operations.");
                }

                ValidateDirectoryMovePathBudgets(
                    operation.stage,
                    operation.target,
                    $"YooAsset published artifact '{operation.packageName}'");
                if (operation.targetInitiallyExisted)
                {
                    ValidateDirectoryMovePathBudgets(
                        operation.target,
                        operation.backup,
                        $"YooAsset backup artifact '{operation.packageName}'");
                }

                ValidateOriginalPublicationAt(operation, operation.target, projectRoot);
                ValidateInstalledPublicationAt(operation, operation.stage, projectRoot, journal.transactionId);
                if (Directory.Exists(operation.backup) || File.Exists(operation.backup))
                {
                    throw new InvalidOperationException($"Publication backup path is not empty: '{operation.backup}'.");
                }

                if (operation.managesSiblingMeta &&
                    (File.Exists(operation.protectedMeta) || Directory.Exists(operation.protectedMeta)))
                {
                    throw new InvalidOperationException(
                        $"Publication protected meta path is not empty: '{operation.protectedMeta}'.");
                }
            }
        }

        private void CommitOperation(YooAsset3PublicationJournalOperation operation)
        {
            ValidateOperation(operation, projectRoot, buildOutputRoot, bundledFileRoot, journal.transactionId);
            ValidateInstalledPublicationAt(operation, operation.stage, projectRoot, journal.transactionId);
            ValidateOriginalPublicationAt(operation, operation.target, projectRoot);

            if (Directory.Exists(operation.backup) || File.Exists(operation.backup))
            {
                throw new InvalidOperationException($"Publication backup path is not empty: '{operation.backup}'.");
            }

            operation.state = BackupPendingState;
            WriteJournal(journal, activeJournalPath, createNew: false);
            if (operation.targetInitiallyExisted)
            {
                ProtectOriginalSiblingMeta(projectRoot, operation);
                Directory.Move(operation.target, operation.backup);
                ValidateOriginalPublicationAt(operation, operation.backup, projectRoot);
            }

            operation.state = BackedUpState;
            WriteJournal(journal, activeJournalPath, createNew: false);
            if (Directory.Exists(operation.target) || File.Exists(operation.target))
            {
                throw new InvalidOperationException(
                    $"Publication target appeared while committing package '{operation.packageName}': '{operation.target}'.");
            }

            ValidateInstalledPublicationAt(operation, operation.stage, projectRoot, journal.transactionId);
            Directory.Move(operation.stage, operation.target);
            ValidateInstalledPublicationAt(operation, operation.target, projectRoot, journal.transactionId);
            ValidatePreRefreshSiblingMeta(projectRoot, operation, allowMissingOriginalMeta: false);
            operation.state = InstalledState;
            WriteJournal(journal, activeJournalPath, createNew: false);
        }

        private static void ValidateOriginalPublicationAt(
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

        private static void ValidateInstalledPublicationAt(
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

        private static void ProtectOriginalSiblingMeta(
            string projectRoot,
            YooAsset3PublicationJournalOperation operation)
        {
            if (!operation.managesSiblingMeta)
            {
                return;
            }

            ValidateMetaFile(
                projectRoot,
                operation.targetMeta,
                operation.originalMetaExisted,
                operation.originalMetaLength,
                operation.originalMetaSha256,
                "original bundled publication meta");
            if (!operation.originalMetaExisted)
            {
                return;
            }

            if (File.Exists(operation.protectedMeta) || Directory.Exists(operation.protectedMeta))
            {
                throw new InvalidOperationException(
                    $"Protected bundled publication meta path is not empty: '{operation.protectedMeta}'.");
            }

            CopyMetaFileDurably(operation.targetMeta, operation.protectedMeta);
            ValidateMetaFile(
                projectRoot,
                operation.protectedMeta,
                true,
                operation.originalMetaLength,
                operation.originalMetaSha256,
                "protected bundled publication meta");
        }

        private static void ValidatePreRefreshSiblingMeta(
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

        private static void CaptureInstalledSiblingMetas(
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

        private static void ValidateDownstreamInputs(Journal recovered, bool afterRefresh)
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

        private static bool CaptureActivatedSiblingMetasForRollback(Journal recovered)
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

        private static void ValidateInstalledSiblingMeta(
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

        private static void RestoreOriginalSiblingMeta(
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

        private static void DeleteProtectedSiblingMeta(
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

        private static void DeleteProtectedSiblingMetaIfPresent(
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

        private static MetaFileSnapshot CaptureMetaFile(string projectRoot, string path)
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

        private static void ValidateUnityFolderMeta(byte[] content, string path)
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

        private static void ValidateMetaFile(
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

        private static void ValidateMetaSnapshot(
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

        private static void CopyMetaFileDurably(string source, string destination)
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

        private static void Rollback(
            Journal recovered,
            string journalPath,
            Action refreshAssets)
        {
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
                    GetStateRoot(recovered.projectRoot, recovered.invocationId),
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
            CompleteRollbackRefresh(recovered, journalPath, refreshAssets);
        }

        private static void CompleteRollbackRefresh(
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
                GetStateRoot(recovered.projectRoot, recovered.invocationId),
                journalPath);
            TryDeleteEmptyStateDirectories(
                recovered.projectRoot,
                recovered.invocationId);
        }

        private static void ValidateRolledBackState(Journal recovered)
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

        private static void RollbackOperation(Journal recovered, YooAsset3PublicationJournalOperation operation)
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

        private static void DeleteStageIfOwned(Journal recovered, YooAsset3PublicationJournalOperation operation)
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

        private static void CompletePendingRefresh(Journal recovered, string journalPath, Action refreshAssets)
        {
            try
            {
                Dictionary<YooAsset3PublicationJournalOperation, MetaFileSnapshot> recoveryCandidates =
                    CaptureRefreshRecoveryMetaCandidates(recovered);
                if (refreshAssets == null)
                {
                    throw new InvalidOperationException("A refresh callback is required to recover a committed YooAsset publication.");
                }

                refreshAssets();
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

        private static void ValidateCommittedPublications(Journal recovered)
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

        private static void ValidatePreRefreshCommittedPublications(Journal recovered)
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

        private static Dictionary<YooAsset3PublicationJournalOperation, MetaFileSnapshot>
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

        private static void CleanupCommitted(Journal recovered, string journalPath)
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
                GetStateRoot(recovered.projectRoot, recovered.invocationId),
                recovered.workRoot);
            CleanupOperationMetadata(recovered);
            YooAsset3BuildSafety.DeleteOwnedFile(
                recovered.projectRoot,
                GetStateRoot(recovered.projectRoot, recovered.invocationId),
                journalPath);
            TryDeleteEmptyStateDirectories(
                recovered.projectRoot,
                recovered.invocationId);
        }

        private static void TryDeleteEmptyStateDirectories(
            string projectRoot,
            string invocationId)
        {
            string stateRoot = GetStateRoot(projectRoot, invocationId);
            TryDeleteEmptyStateDirectory(
                projectRoot,
                Path.Combine(stateRoot, "work"));
            TryDeleteEmptyStateDirectory(projectRoot, stateRoot);
            TryDeleteEmptyStateDirectory(
                projectRoot,
                GetProviderStateRoot(projectRoot));
        }

        private static void TryDeleteEmptyStateDirectory(
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

        private static void CleanupOperationMetadata(Journal recovered)
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

        private static void EnsureOperationCandidateAbsent(YooAsset3PublicationJournalOperation operation)
        {
            if (Directory.Exists(operation.stage) || File.Exists(operation.stage))
            {
                throw new InvalidOperationException($"Publication stage already exists: '{operation.stage}'.");
            }
        }

        private static bool RequiresBundledSeed(YooAssetBundledCopyOption option)
        {
            return option == YooAssetBundledCopyOption.OnlyCopyAll ||
                   option == YooAssetBundledCopyOption.OnlyCopyByTags;
        }

        private static void EnsureNoOrphanOperationDirectories(
            IEnumerable<YooAsset3PublicationJournalOperation> operations)
        {
            foreach (string parent in operations
                         .Select(operation => Path.GetDirectoryName(operation.target))
                         .Where(parent => !string.IsNullOrEmpty(parent))
                         .Distinct(YooAsset3BuildSafety.FileSystemPathComparer))
            {
                if (!Directory.Exists(parent))
                {
                    continue;
                }

                foreach (string entry in Directory.EnumerateFileSystemEntries(parent, "*", SearchOption.TopDirectoryOnly))
                {
                    string name = Path.GetFileName(entry);
                    if (name.StartsWith(StagePrefix, StringComparison.Ordinal) ||
                        name.StartsWith(BackupPrefix, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Detached YooAsset transaction state requires manual inspection: '{entry}'.");
                    }
                }
            }
        }

        private static void EnsureNoDetachedState(string stateRoot)
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

        private static void CopyDirectorySafely(
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

        private static void ValidateDirectoryMovePathBudgets(
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

        private static Journal ReadAndValidateJournal(string journalPath, string projectRoot)
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
                !IsValidInvocationId(recovered.invocationId) ||
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

            string stateRoot = GetStateRoot(projectRoot, recovered.invocationId);
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

        private static Journal ResolveLatestJournalForRecovery(
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

        private static void ValidateOperation(
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

        private static void WriteJournal(Journal value, string journalPath, bool createNew)
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

        private static string ComputeChecksum(Journal value)
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

        private static void AppendChecksumValue(StringBuilder builder, string value)
        {
            string normalized = value ?? string.Empty;
            builder.Append(normalized.Length.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(normalized);
            builder.Append(';');
        }

        private static void CleanupJournalTemporaryFiles(string projectRoot, string stateRoot, string journalPath)
        {
            string pattern = Path.GetFileName(journalPath) + ".tmp-*";
            foreach (string temporaryPath in Directory.EnumerateFiles(stateRoot, pattern, SearchOption.TopDirectoryOnly))
            {
                YooAsset3BuildSafety.DeleteOwnedFile(projectRoot, stateRoot, temporaryPath);
            }
        }

        private static bool IsTransactionId(string value)
        {
            return value != null && value.Length == 32 && value.All(character =>
                character >= '0' && character <= '9' || character >= 'a' && character <= 'f');
        }

        private static bool IsSha256(string value)
        {
            return IsHexToken(value, 64);
        }

        private static bool IsHexToken(string value, int length)
        {
            return value != null && value.Length == length && value.All(character =>
                character >= '0' && character <= '9' ||
                character >= 'A' && character <= 'F' ||
                character >= 'a' && character <= 'f');
        }

        private static bool IsKnownPhase(string value)
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

        private static bool IsKnownOperationState(string value)
        {
            return string.Equals(value, PreparedState, StringComparison.Ordinal) ||
                   string.Equals(value, BackupPendingState, StringComparison.Ordinal) ||
                   string.Equals(value, BackedUpState, StringComparison.Ordinal) ||
                   string.Equals(value, InstalledState, StringComparison.Ordinal);
        }

        private static bool IsSourceQualificationPhase(string value)
        {
            return string.Equals(value, SourceQualificationSuspendingPhase, StringComparison.Ordinal) ||
                   string.Equals(value, SourceQualificationSuspendedPhase, StringComparison.Ordinal) ||
                   string.Equals(value, SourceQualificationResumingPhase, StringComparison.Ordinal);
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(YooAsset3PublicationTransaction));
            }
        }

        private sealed class SourceQualificationScope : IDisposable
        {
            internal static readonly IDisposable Empty = new SourceQualificationScope(null);
            private YooAsset3PublicationTransaction owner;

            internal SourceQualificationScope(YooAsset3PublicationTransaction owner)
            {
                this.owner = owner;
            }

            public void Dispose()
            {
                YooAsset3PublicationTransaction current = owner;
                owner = null;
                current?.ResumeAfterSourceQualification();
            }
        }

        private readonly struct SourceQualificationPaths
        {
            internal SourceQualificationPaths(
                string operationRoot,
                string installedDirectory,
                string installedMeta,
                string originalMeta)
            {
                OperationRoot = operationRoot;
                InstalledDirectory = installedDirectory;
                InstalledMeta = installedMeta;
                OriginalMeta = originalMeta;
            }

            internal string OperationRoot { get; }
            internal string InstalledDirectory { get; }
            internal string InstalledMeta { get; }
            internal string OriginalMeta { get; }
        }

        [Serializable]
        private sealed class Journal
        {
            public string documentType;
            public long sequence;
            public string invocationId;
            public string transactionId;
            public string phase;
            public string projectRoot;
            public string buildOutputRoot;
            public string bundledFileRoot;
            public string workRoot;
            public YooAsset3PublicationJournalOperation[] operations;
            public string checksum;
        }

        private readonly struct MetaFileSnapshot
        {
            public static readonly MetaFileSnapshot Missing = new MetaFileSnapshot(false, 0, string.Empty);

            public MetaFileSnapshot(bool exists, long length, string sha256)
            {
                Exists = exists;
                Length = length;
                Sha256 = sha256 ?? string.Empty;
            }

            public bool Exists { get; }
            public long Length { get; }
            public string Sha256 { get; }
        }

        private readonly struct CopyDirectoryEntry
        {
            public CopyDirectoryEntry(string source, string destination, int depth)
            {
                Source = source;
                Destination = destination;
                Depth = depth;
            }

            public string Source { get; }
            public string Destination { get; }
            public int Depth { get; }
        }
    }
}
