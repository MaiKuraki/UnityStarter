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
            BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                lockRoot,
                "YooAsset publication lock root");
            YooAsset3BuildSafety.ValidateNoPathRedirection(normalizedProjectRoot, lockRoot);
            Directory.CreateDirectory(lockRoot);
            YooAsset3BuildSafety.ValidateNoPathRedirection(normalizedProjectRoot, lockRoot);

            string[] publicationRoots = new[]
                {
                    YooAsset3PublicationTransaction.GetStateRoot(normalizedProjectRoot),
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
                    BuildPathPolicy.EnsureLegacyWindowsPathBudget(
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
        public string approvedRoot;
        public string target;
        public string stage;
        public string backup;
        public bool targetInitiallyExisted;
        public bool originalWasOwned;
        public string originalTransactionId;
        public string originalPackageVersion;
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
        private const int JournalSchemaVersion = 3;
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
        private const string RefreshPendingPhase = "RefreshPending";
        private const string CommittedPhase = "Committed";
        private const string PreparedState = "Prepared";
        private const string BackupPendingState = "BackupPending";
        private const string BackedUpState = "BackedUp";
        private const string InstalledState = "Installed";

        private readonly string projectRoot;
        private readonly string buildOutputRoot;
        private readonly string bundledFileRoot;
        private readonly string stateRoot;
        private readonly string activeJournalPath;
        private readonly Journal journal;
        private readonly YooAsset3PackagePublication[] packages;
        private bool prepared;
        private bool completed;
        private bool disposed;

        private YooAsset3PublicationTransaction(
            string projectRoot,
            string buildOutputRoot,
            string bundledFileRoot,
            Journal journal,
            YooAsset3PackagePublication[] packages)
        {
            this.projectRoot = projectRoot;
            this.buildOutputRoot = buildOutputRoot;
            this.bundledFileRoot = bundledFileRoot;
            stateRoot = GetStateRoot(projectRoot);
            activeJournalPath = Path.Combine(stateRoot, ActiveJournalFileName);
            this.journal = journal;
            this.packages = packages;
        }

        public IReadOnlyList<YooAsset3PackagePublication> Packages => packages;

        public static string GetStateRoot(string projectRoot)
        {
            return Path.GetFullPath(Path.Combine(
                projectRoot,
                ".buildpipeline",
                "transactions",
                "yooasset3"));
        }

        public static YooAsset3PublicationTransaction Create(YooAsset3BuildPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            string transactionId = Guid.NewGuid().ToString("N");
            string stateRoot = GetStateRoot(plan.ProjectRoot);
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
                schemaVersion = JournalSchemaVersion,
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
                    BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                        publication.BundledWorkDirectory,
                        $"YooAsset bundled work directory '{publication.FinalPlan.PackageName}'",
                        65);
                }
            }
        }

        private static void ValidateJournalPathBudgets(Journal value)
        {
            string stateRoot = GetStateRoot(value.projectRoot);
            BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                stateRoot,
                "YooAsset publication state root");
            BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                Path.Combine(stateRoot, ActiveJournalFileName),
                "YooAsset publication journal",
                ".tmp-".Length + 32);
            BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                value.workRoot,
                "YooAsset publication work root",
                65);

            foreach (YooAsset3PublicationJournalOperation operation in value.operations)
            {
                BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                    operation.target,
                    $"YooAsset publication target '{operation.packageName}'");
                BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                    operation.stage,
                    $"YooAsset publication stage '{operation.packageName}'");
                BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                    operation.backup,
                    $"YooAsset publication backup '{operation.packageName}'");
                BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                    Path.Combine(operation.stage, YooAsset3PublicationOwnership.MarkerFileName),
                    $"YooAsset staged ownership marker '{operation.packageName}'");
                BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                    Path.Combine(operation.target, YooAsset3PublicationOwnership.MarkerFileName),
                    $"YooAsset published ownership marker '{operation.packageName}'");
                if (operation.managesSiblingMeta)
                {
                    BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                        operation.targetMeta,
                        $"YooAsset published sibling meta '{operation.packageName}'");
                    BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                        operation.protectedMeta,
                        $"YooAsset protected sibling meta '{operation.packageName}'");
                }
            }
        }

        public static void RecoverPending(string projectRoot, Action refreshAssets)
        {
            string normalizedProjectRoot = Path.GetFullPath(projectRoot);
            string stateRoot = GetStateRoot(normalizedProjectRoot);
            string journalPath = Path.Combine(stateRoot, ActiveJournalFileName);
            YooAsset3BuildSafety.ValidateNoPathRedirection(normalizedProjectRoot, stateRoot);
            YooAsset3BuildSafety.ValidateNoPathRedirection(normalizedProjectRoot, journalPath);
            if (!File.Exists(journalPath))
            {
                EnsureNoDetachedState(stateRoot);
                return;
            }

            Journal recovered = ReadAndValidateJournal(journalPath, normalizedProjectRoot);
            CleanupJournalTemporaryFiles(normalizedProjectRoot, stateRoot, journalPath);

            if (string.Equals(recovered.phase, RefreshPendingPhase, StringComparison.Ordinal))
            {
                CompletePendingRefresh(recovered, journalPath, refreshAssets);
            }
            else if (string.Equals(recovered.phase, CommittedPhase, StringComparison.Ordinal))
            {
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
                Rollback(recovered, journalPath);
            }
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

            return YooAsset3BuildParameterFactory.Create(
                request,
                publication.FinalPlan.Profile,
                buildOutputRoot,
                bundledFileRoot,
                publication.FinalPlan.BundledCopyParams,
                publication.OutputOperation.stage,
                publication.BundledOperation == null
                    ? Path.Combine(journal.workRoot, "unused-bundled", publication.FinalPlan.PackageName)
                    : publication.BundledWorkDirectory);
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
                    journal.transactionId);
                operation.installedContentIdentity = sealedStage.ContentIdentity;
                operation.installedEntryCount = sealedStage.EntryCount;
            }

            WriteJournal(journal, activeJournalPath, createNew: false);
        }

        public void Commit(Action validatePublishedState, Action refreshAssets)
        {
            ThrowIfDisposed();
            if (!prepared)
            {
                throw new InvalidOperationException("Prepare the YooAsset publication transaction before committing it.");
            }

            bool refreshPendingWasPersisted = false;
            try
            {
                ValidateReadyToCommit();
                journal.phase = CommittingPhase;
                WriteJournal(journal, activeJournalPath, createNew: false);
                foreach (YooAsset3PublicationJournalOperation operation in journal.operations)
                {
                    CommitOperation(operation);
                }

                validatePublishedState?.Invoke();
                ValidatePreRefreshCommittedPublications(journal);
                journal.phase = RefreshPendingPhase;
                WriteJournal(journal, activeJournalPath, createNew: false);
                refreshPendingWasPersisted = true;
                completed = true;
                try
                {
                    if (refreshAssets == null)
                    {
                        throw new InvalidOperationException("A refresh callback is required to complete a YooAsset publication.");
                    }

                    refreshAssets();
                    CaptureInstalledSiblingMetas(journal, null);
                }
                catch (Exception refreshException)
                {
                    throw new YooAsset3CommittedPublicationException(
                        "YooAsset publication files were committed, but AssetDatabase refresh did not complete. " +
                        "The journal and backups were retained; run transaction recovery before another publication.",
                        activeJournalPath,
                        refreshException);
                }

                journal.phase = CommittedPhase;
                WriteJournal(journal, activeJournalPath, createNew: false);
                try
                {
                    CleanupCommitted(journal, activeJournalPath);
                }
                catch (Exception cleanupException)
                {
                    throw new YooAsset3CommittedPublicationException(
                        "YooAsset publication and AssetDatabase refresh completed, but transaction cleanup did not. " +
                        "The committed journal was retained for recovery.",
                        activeJournalPath,
                        cleanupException);
                }
            }
            catch (YooAsset3CommittedPublicationException)
            {
                throw;
            }
            catch (Exception commitException)
            {
                if (refreshPendingWasPersisted)
                {
                    completed = true;
                    throw new YooAsset3CommittedPublicationException(
                        "YooAsset publication reached its durable committed boundary, but finalization did not complete. " +
                        "The journal and backups were retained for recovery.",
                        activeJournalPath,
                        commitException);
                }

                try
                {
                    Rollback(journal, activeJournalPath);
                    completed = true;
                }
                catch (Exception rollbackException)
                {
                    throw new AggregateException(
                        "YooAsset publication failed and rollback did not complete. The durable journal was retained for recovery.",
                        commitException,
                        rollbackException);
                }

                throw;
            }
        }

        public void Abort()
        {
            ThrowIfDisposed();
            if (completed)
            {
                return;
            }

            if (prepared && File.Exists(activeJournalPath))
            {
                Rollback(journal, activeJournalPath);
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
                Abort();
            }

            disposed = true;
        }

        private static YooAsset3PublicationJournalOperation CreateOperation(
            string projectRoot,
            string kind,
            string packageName,
            string packageVersion,
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

        private void ValidateReadyToCommit()
        {
            foreach (YooAsset3PublicationJournalOperation operation in journal.operations)
            {
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
            if (targetMeta.Exists)
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

        private static void Rollback(Journal recovered, string journalPath)
        {
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
                    GetStateRoot(recovered.projectRoot),
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
            YooAsset3BuildSafety.DeleteOwnedFile(
                recovered.projectRoot,
                GetStateRoot(recovered.projectRoot),
                journalPath);
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
            }

            if (!operation.targetInitiallyExisted)
            {
                ValidatePreRefreshSiblingMeta(
                    recovered.projectRoot,
                    operation,
                    allowMissingOriginalMeta: false);
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
                GetStateRoot(recovered.projectRoot),
                recovered.workRoot);
            CleanupOperationMetadata(recovered);
            YooAsset3BuildSafety.DeleteOwnedFile(
                recovered.projectRoot,
                GetStateRoot(recovered.projectRoot),
                journalPath);
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

                BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
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
                        BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                            destinationEntry,
                            "YooAsset transactional copy directory");
                        pending.Push(new CopyDirectoryEntry(entry, destinationEntry, current.Depth + 1));
                        continue;
                    }

                    BuildPathPolicy.EnsureLegacyWindowsPathBudget(
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
            BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
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
                        BuildPathPolicy.EnsureLegacyWindowsDirectoryPathBudget(
                            destination,
                            displayName);
                        pending.Push(new CopyDirectoryEntry(
                            entry,
                            destination,
                            current.Depth + 1));
                    }
                    else
                    {
                        BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                            destination,
                            displayName);
                    }
                }
            }
        }

        private static Journal ReadAndValidateJournal(string journalPath, string projectRoot)
        {
            YooAsset3BuildSafety.ValidateNoPathRedirection(projectRoot, GetStateRoot(projectRoot));
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
                recovered = JsonUtility.FromJson<Journal>(json);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"YooAsset publication journal is not valid JSON: '{journalPath}'.", exception);
            }

            if (recovered == null || recovered.schemaVersion != JournalSchemaVersion ||
                recovered.operationRecords == null || recovered.operationRecords.Length == 0 ||
                recovered.operationRecords.Length > MaximumOperationCount ||
                !IsTransactionId(recovered.transactionId) ||
                !IsKnownPhase(recovered.phase))
            {
                throw new InvalidOperationException($"YooAsset publication journal has an unsupported or incomplete schema: '{journalPath}'.");
            }

            try
            {
                recovered.operations = DeserializeOperations(recovered.operationRecords);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"YooAsset publication journal contains invalid operation records: '{journalPath}'.",
                    exception);
            }

            if (!YooAsset3BuildSafety.PathsEqual(projectRoot, recovered.projectRoot))
            {
                throw new InvalidOperationException(
                    $"YooAsset publication journal belongs to a different Unity project: '{journalPath}'.");
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

            string expectedWorkRoot = Path.Combine(GetStateRoot(projectRoot), "work", recovered.transactionId);
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
                  string.IsNullOrWhiteSpace(operation.originalPackageVersion))) ||
                (string.Equals(operation.state, InstalledState, StringComparison.Ordinal) &&
                 string.IsNullOrWhiteSpace(operation.installedContentIdentity)))
            {
                throw new InvalidOperationException(
                    $"YooAsset publication journal ownership identity is incomplete for package '{operation.packageName}'.");
            }
        }

        private static void WriteJournal(Journal value, string journalPath, bool createNew)
        {
            BuildPathPolicy.EnsureLegacyWindowsPathBudget(
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
            value.checksum = ComputeChecksum(value);
            value.operationRecords = SerializeOperations(value.operations);
            string json = JsonUtility.ToJson(value, true);
            byte[] bytes = new UTF8Encoding(false).GetBytes(json);
            if (bytes.Length <= 0 || bytes.Length > MaximumJournalBytes)
            {
                throw new InvalidOperationException($"YooAsset publication journal exceeds {MaximumJournalBytes} bytes.");
            }

            Directory.CreateDirectory(journalDirectory);
            YooAsset3BuildSafety.ValidateNoPathRedirection(value.projectRoot, journalDirectory);
            YooAsset3BuildSafety.ValidateNoPathRedirection(value.projectRoot, journalPath);
            if (createNew)
            {
                using (var stream = new FileStream(
                           journalPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           4096,
                           FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                return;
            }

            string temporaryPath = journalPath + ".tmp-" + value.transactionId;
            BuildPathPolicy.EnsureLegacyWindowsPathBudget(
                temporaryPath,
                "YooAsset publication temporary journal");
            YooAsset3BuildSafety.ValidateNoPathRedirection(value.projectRoot, temporaryPath);
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

                YooAsset3BuildSafety.ValidateNoPathRedirection(value.projectRoot, journalPath);
                YooAsset3BuildSafety.ValidateNoPathRedirection(value.projectRoot, temporaryPath);
                File.Replace(temporaryPath, journalPath, null);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private static string[] SerializeOperations(YooAsset3PublicationJournalOperation[] operations)
        {
            if (operations == null || operations.Length == 0 || operations.Length > MaximumOperationCount)
            {
                throw new InvalidOperationException("YooAsset publication journal has no valid operations to persist.");
            }

            var records = new string[operations.Length];
            for (int index = 0; index < operations.Length; index++)
            {
                if (operations[index] == null)
                {
                    throw new InvalidOperationException("YooAsset publication journal contains a null operation.");
                }

                records[index] = JsonUtility.ToJson(operations[index], false);
                if (string.IsNullOrWhiteSpace(records[index]))
                {
                    throw new InvalidOperationException("YooAsset publication operation could not be serialized.");
                }
            }

            return records;
        }

        private static YooAsset3PublicationJournalOperation[] DeserializeOperations(string[] records)
        {
            if (records == null || records.Length == 0 || records.Length > MaximumOperationCount)
            {
                throw new InvalidOperationException("YooAsset publication journal operation count is invalid.");
            }

            var operations = new YooAsset3PublicationJournalOperation[records.Length];
            for (int index = 0; index < records.Length; index++)
            {
                if (string.IsNullOrWhiteSpace(records[index]))
                {
                    throw new InvalidOperationException("YooAsset publication journal contains an empty operation record.");
                }

                operations[index] = JsonUtility.FromJson<YooAsset3PublicationJournalOperation>(records[index]);
                if (operations[index] == null)
                {
                    throw new InvalidOperationException("YooAsset publication journal contains an invalid operation record.");
                }
            }

            return operations;
        }

        private static string ComputeChecksum(Journal value)
        {
            var builder = new StringBuilder();
            AppendChecksumValue(builder, value.schemaVersion.ToString(CultureInfo.InvariantCulture));
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
                AppendChecksumValue(builder, operation?.approvedRoot);
                AppendChecksumValue(builder, operation?.target);
                AppendChecksumValue(builder, operation?.stage);
                AppendChecksumValue(builder, operation?.backup);
                AppendChecksumValue(builder, operation != null && operation.targetInitiallyExisted ? "1" : "0");
                AppendChecksumValue(builder, operation != null && operation.originalWasOwned ? "1" : "0");
                AppendChecksumValue(builder, operation?.originalTransactionId);
                AppendChecksumValue(builder, operation?.originalPackageVersion);
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

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(YooAsset3PublicationTransaction));
            }
        }

        [Serializable]
        private sealed class Journal
        {
            public int schemaVersion;
            public string transactionId;
            public string phase;
            public string projectRoot;
            public string buildOutputRoot;
            public string bundledFileRoot;
            public string workRoot;
            public string[] operationRecords;
            [NonSerialized] public YooAsset3PublicationJournalOperation[] operations;
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
