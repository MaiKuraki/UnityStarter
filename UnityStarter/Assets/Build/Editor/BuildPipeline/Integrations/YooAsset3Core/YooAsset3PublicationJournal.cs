using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Build.Pipeline.Editor.Integrations.YooAsset3Core
{
    internal static class YooAsset3PublicationConstants
    {
        internal const string PublicationIdPrefix = "asset-content:yooasset:";
        internal const string StateRootRelativePath = ".buildpipeline/transactions/yooasset3";

        internal const string JournalDocumentType = "yooasset-publication-transaction";
        internal const int MaximumJournalBytes = 1024 * 1024;
        internal const int MaximumOperationCount = 512;
        internal const int MaximumCopiedEntries = 250000;
        internal const int MaximumCopyDepth = 64;
        internal const long MaximumCopiedBytes = 256L * 1024L * 1024L * 1024L;
        internal const long MaximumSiblingMetaBytes = 1024L * 1024L;
        internal const string ActiveJournalFileName = "active.json";
        internal const string StagePrefix = ".yoo-stage-";
        internal const string BackupPrefix = ".yoo-backup-";

        internal const string PreparedPhase = "Prepared";
        internal const string CommittingPhase = "Committing";
        internal const string RollingBackPhase = "RollingBack";
        internal const string RollbackRefreshPendingPhase = "RollbackRefreshPending";
        internal const string ActivationRefreshPendingPhase = "ActivationRefreshPending";
        internal const string DownstreamActivePhase = "DownstreamActive";
        internal const string SourceQualificationSuspendingPhase = "SourceQualificationSuspending";
        internal const string SourceQualificationSuspendedPhase = "SourceQualificationSuspended";
        internal const string SourceQualificationResumingPhase = "SourceQualificationResuming";
        internal const string AwaitingDecisionPhase = "AwaitingDecision";
        internal const string RefreshPendingPhase = "RefreshPending";
        internal const string CommittedPhase = "Committed";

        internal const string PreparedState = "Prepared";
        internal const string BackupPendingState = "BackupPending";
        internal const string BackedUpState = "BackedUp";
        internal const string InstalledState = "Installed";
    }

    internal static class YooAsset3PublicationPaths
    {
        public static string GetProviderStateRoot(string projectRoot)
        {
            return Path.GetFullPath(Path.Combine(
                projectRoot,
                YooAsset3PublicationConstants.StateRootRelativePath.Replace('/', Path.DirectorySeparatorChar)));
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
            return YooAsset3PublicationConstants.StateRootRelativePath + "/" + NormalizeInvocationId(invocationId);
        }

        internal static string GetPublicationId(string invocationId)
        {
            return YooAsset3PublicationConstants.PublicationIdPrefix + NormalizeInvocationId(invocationId);
        }

        internal static string NormalizeInvocationId(string invocationId)
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

        internal static bool IsValidInvocationId(string invocationId)
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
    }

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
                    YooAsset3PublicationPaths.GetProviderStateRoot(normalizedProjectRoot),
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

    /// <summary>
    /// Thrown by failure-injection checkpoints to simulate a process termination at a
    /// named lifecycle node. Transaction catch blocks rethrow it without rollback or
    /// cleanup so the durable journal is retained exactly as a crashed process would
    /// leave it, letting the test drive explicit recovery afterwards.
    /// </summary>
    internal sealed class YooAsset3SimulatedTerminationException : Exception
    {
        public YooAsset3SimulatedTerminationException(string checkpoint)
            : base($"Simulated YooAsset publication termination at checkpoint '{checkpoint}'.")
        {
            Checkpoint = checkpoint ?? string.Empty;
        }

        public string Checkpoint { get; }
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
            YooAsset3PublicationJournalOperation outputOperation,
            YooAsset3PublicationJournalOperation bundledOperation,
            string bundledWorkDirectory)
        {
            OutputOperation = outputOperation;
            BundledOperation = bundledOperation;
            BundledWorkDirectory = bundledWorkDirectory ?? string.Empty;
        }

        public YooAsset3PublicationJournalOperation OutputOperation { get; }
        public YooAsset3PublicationJournalOperation BundledOperation { get; }
        public string BundledWorkDirectory { get; }
    }

    [Serializable]
    internal sealed class Journal
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

    internal readonly struct MetaFileSnapshot
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

    internal readonly struct CopyDirectoryEntry
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

    internal readonly struct SourceQualificationPaths
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
}
