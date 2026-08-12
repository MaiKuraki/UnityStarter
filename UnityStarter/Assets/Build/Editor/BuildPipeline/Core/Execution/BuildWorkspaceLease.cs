using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace Build.Pipeline.Editor
{
    public enum BuildWorkspaceOperation
    {
        Build = 1,
        Recovery = 2
    }

    public sealed class BuildWorkspaceBusyException : IOException
    {
        internal BuildWorkspaceBusyException(
            string leaseFilePath,
            BuildWorkspaceOperation attemptedOperation,
            Exception innerException)
            : base(
                $"The build workspace is already leased by another build or recovery operation. " +
                $"Operation='{GetOperationName(attemptedOperation)}', lease='{leaseFilePath}'. " +
                "The lock is authoritative; metadata such as a process ID is diagnostic only.",
                innerException)
        {
            LeaseFilePath = leaseFilePath ?? string.Empty;
            AttemptedOperation = attemptedOperation;
        }

        public string LeaseFilePath { get; }

        public BuildWorkspaceOperation AttemptedOperation { get; }

        private static string GetOperationName(BuildWorkspaceOperation operation)
        {
            switch (operation)
            {
                case BuildWorkspaceOperation.Build:
                    return "build";
                case BuildWorkspaceOperation.Recovery:
                    return "recovery";
                default:
                    return "unknown";
            }
        }
    }

    /// <summary>
    /// Owns the single operating-system file lock that serializes all build and
    /// recovery mutations for one Unity project. The lock, not its metadata,
    /// is the source of truth. Acquisition is fail-fast and never deletes or
    /// waits on an existing lease file.
    /// </summary>
    public sealed class BuildWorkspaceLease : IDisposable
    {
        public const string MetadataDocumentType = "build-workspace-lease";
        public const int MaximumMetadataUtf8Bytes = 4 * 1024;

        private const long LockOffset = 0;
        private const long LockLength = 1;
        private const int MaximumRunIdCharacters = 256;
        private const string LeaseRelativePath = "Temp/BuildPipeline/Workspace/lease.lock";
        private const string MetadataRelativePath = "Temp/BuildPipeline/Workspace/lease.json";

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private FileStream stream;

        private BuildWorkspaceLease(
            string projectRoot,
            string leaseFilePath,
            string metadataFilePath,
            string runId,
            BuildWorkspaceOperation operation,
            DateTimeOffset startedUtc,
            FileStream stream)
        {
            ProjectRoot = projectRoot;
            LeaseFilePath = leaseFilePath;
            MetadataFilePath = metadataFilePath;
            RunId = runId;
            Operation = operation;
            StartedUtc = startedUtc;
            this.stream = stream;
        }

        public string ProjectRoot { get; }

        public string LeaseFilePath { get; }

        public string MetadataFilePath { get; }

        public string RunId { get; }

        public BuildWorkspaceOperation Operation { get; }

        public DateTimeOffset StartedUtc { get; }

        public static BuildWorkspaceLease Acquire(
            string trustedProjectRoot,
            string runId,
            BuildWorkspaceOperation operation)
        {
            int processId;
            using (Process process = Process.GetCurrentProcess())
            {
                processId = process.Id;
            }

            return Acquire(
                trustedProjectRoot,
                runId,
                operation,
                DateTimeOffset.UtcNow,
                processId);
        }

        internal static BuildWorkspaceLease Acquire(
            string trustedProjectRoot,
            string runId,
            BuildWorkspaceOperation operation,
            DateTimeOffset startedUtc,
            int processId)
        {
            string projectRoot = NormalizeUnityProjectRoot(trustedProjectRoot);
            ValidateInputs(runId, operation, processId);

            string leaseFilePath = BuildPathPolicy.EnsureWin32MaxPathBudget(
                Path.Combine(
                    projectRoot,
                    LeaseRelativePath.Replace('/', Path.DirectorySeparatorChar)),
                "Build workspace lease file");
            string metadataFilePath = BuildPathPolicy.EnsureWin32MaxPathBudget(
                Path.Combine(
                    projectRoot,
                    MetadataRelativePath.Replace('/', Path.DirectorySeparatorChar)),
                "Build workspace lease metadata file");
            string leaseDirectory = Path.GetDirectoryName(leaseFilePath);
            if (string.IsNullOrEmpty(leaseDirectory))
            {
                throw new InvalidOperationException(
                    $"Build workspace lease path has no parent directory: '{leaseFilePath}'.");
            }

            BuildPathPolicy.EnsureWin32MaxDirectoryPathBudget(
                leaseDirectory,
                "Build workspace lease directory");
            EnsureWorkspacePathHasNoReparsePoints(
                projectRoot,
                leaseFilePath,
                "lease.lock");
            EnsureWorkspacePathHasNoReparsePoints(
                projectRoot,
                metadataFilePath,
                "lease.json");
            Directory.CreateDirectory(leaseDirectory);
            EnsureWorkspacePathHasNoReparsePoints(
                projectRoot,
                leaseFilePath,
                "lease.lock");
            EnsureWorkspacePathHasNoReparsePoints(
                projectRoot,
                metadataFilePath,
                "lease.json");

            FileStream candidate;
            try
            {
                candidate = new FileStream(
                    leaseFilePath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.Read);
            }
            catch (IOException exception)
            {
                throw new BuildWorkspaceBusyException(
                    leaseFilePath,
                    operation,
                    exception);
            }

            try
            {
                EnsureWorkspacePathHasNoReparsePoints(
                    projectRoot,
                    leaseFilePath,
                    "lease.lock");
                candidate.Lock(LockOffset, LockLength);
            }
            catch (IOException exception)
            {
                candidate.Dispose();
                throw new BuildWorkspaceBusyException(
                    leaseFilePath,
                    operation,
                    exception);
            }
            catch
            {
                candidate.Dispose();
                throw;
            }

            DateTimeOffset normalizedStartedUtc = startedUtc.ToUniversalTime();
            try
            {
                byte[] metadata = CreateMetadata(
                    runId,
                    operation,
                    processId,
                    normalizedStartedUtc);
                candidate.SetLength(0);
                candidate.Flush(true);
                WriteMetadata(metadataFilePath, metadata);
            }
            catch
            {
                ReleaseAfterFailedAcquisition(candidate);
                throw;
            }

            return new BuildWorkspaceLease(
                projectRoot,
                leaseFilePath,
                metadataFilePath,
                runId,
                operation,
                normalizedStartedUtc,
                candidate);
        }

        public void Dispose()
        {
            FileStream ownedStream = Interlocked.Exchange(ref stream, null);
            if (ownedStream == null)
            {
                return;
            }

            try
            {
                ownedStream.Unlock(LockOffset, LockLength);
            }
            finally
            {
                ownedStream.Dispose();
            }
        }

        private static string NormalizeUnityProjectRoot(string trustedProjectRoot)
        {
            if (string.IsNullOrWhiteSpace(trustedProjectRoot))
            {
                throw new ArgumentException(
                    "A trusted Unity project root is required.",
                    nameof(trustedProjectRoot));
            }

            string projectRoot = Path.GetFullPath(trustedProjectRoot);
            if (!Directory.Exists(projectRoot))
            {
                throw new DirectoryNotFoundException(
                    $"Unity project root does not exist: '{projectRoot}'.");
            }

            string assetsDirectory = Path.Combine(projectRoot, "Assets");
            string projectSettingsDirectory = Path.Combine(projectRoot, "ProjectSettings");
            if (!Directory.Exists(assetsDirectory)
                || !Directory.Exists(projectSettingsDirectory))
            {
                throw new InvalidOperationException(
                    $"The trusted project root must contain Assets and ProjectSettings directories: '{projectRoot}'.");
            }

            string volumeRoot = Path.GetPathRoot(projectRoot);
            if (string.Equals(
                    projectRoot,
                    volumeRoot,
                    Path.DirectorySeparatorChar == '\\'
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal))
            {
                return projectRoot;
            }

            return projectRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        }

        private static void ValidateInputs(
            string runId,
            BuildWorkspaceOperation operation,
            int processId)
        {
            BuildIdentityPolicy.ValidatePlainText(
                runId,
                "Build workspace run ID",
                MaximumRunIdCharacters);
            if (operation != BuildWorkspaceOperation.Build
                && operation != BuildWorkspaceOperation.Recovery)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(operation),
                    operation,
                    "Build workspace operation must be Build or Recovery.");
            }

            if (processId <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(processId),
                    processId,
                    "Process ID must be positive.");
            }
        }

        private static byte[] CreateMetadata(
            string runId,
            BuildWorkspaceOperation operation,
            int processId,
            DateTimeOffset startedUtc)
        {
            var builder = new StringBuilder(384);
            builder.Append("{\"documentType\":");
            AppendJsonString(builder, MetadataDocumentType);
            builder.Append(",\"runId\":");
            AppendJsonString(builder, runId);
            builder.Append(",\"operation\":");
            AppendJsonString(builder, GetOperationName(operation));
            builder.Append(",\"pid\":")
                .Append(processId.ToString(CultureInfo.InvariantCulture))
                .Append(",\"startedUtc\":");
            AppendJsonString(
                builder,
                startedUtc.ToString(
                    "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
                    CultureInfo.InvariantCulture));
            builder.Append('}');

            byte[] metadata = StrictUtf8.GetBytes(builder.ToString());
            if (metadata.Length > MaximumMetadataUtf8Bytes)
            {
                throw new InvalidOperationException(
                    $"Build workspace lease metadata exceeds {MaximumMetadataUtf8Bytes} UTF-8 bytes.");
            }

            return metadata;
        }

        private static void WriteMetadata(string leaseFilePath, byte[] metadata)
        {
            using (var metadataStream = new FileStream(
                       leaseFilePath,
                       FileMode.OpenOrCreate,
                       FileAccess.Write,
                       FileShare.ReadWrite))
            {
                metadataStream.Position = 0;
                metadataStream.SetLength(0);
                metadataStream.Write(metadata, 0, metadata.Length);
                metadataStream.Flush(true);
            }
        }

        private static string GetOperationName(BuildWorkspaceOperation operation)
        {
            switch (operation)
            {
                case BuildWorkspaceOperation.Build:
                    return "build";
                case BuildWorkspaceOperation.Recovery:
                    return "recovery";
                default:
                    throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
            }
        }

        private static void AppendJsonString(StringBuilder builder, string value)
        {
            builder.Append('"');
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                switch (character)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (character < 0x20)
                        {
                            builder.Append("\\u")
                                .Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(character);
                        }

                        break;
                }
            }

            builder.Append('"');
        }

        private static void EnsureWorkspacePathHasNoReparsePoints(
            string projectRoot,
            string workspaceFilePath,
            string workspaceFileName)
        {
            string[] relativeSegments =
            {
                "Temp",
                "BuildPipeline",
                "Workspace",
                workspaceFileName
            };
            string currentPath = projectRoot;
            for (int index = 0; index < relativeSegments.Length; index++)
            {
                currentPath = Path.Combine(currentPath, relativeSegments[index]);
                if (!Directory.Exists(currentPath) && !File.Exists(currentPath))
                {
                    continue;
                }

                FileAttributes attributes = File.GetAttributes(currentPath);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(
                        $"Build workspace lease path contains a reparse-point entry: '{currentPath}'. " +
                        $"The lease must remain on the trusted Unity project filesystem: '{workspaceFilePath}'.");
                }
            }
        }

        private static void ReleaseAfterFailedAcquisition(FileStream candidate)
        {
            try
            {
                candidate.Unlock(LockOffset, LockLength);
            }
            catch (Exception)
            {
                // Closing the stream still releases the operating-system lock.
            }

            try
            {
                candidate.Dispose();
            }
            catch (Exception)
            {
                // Preserve the acquisition failure that triggered cleanup.
            }
        }
    }
}
