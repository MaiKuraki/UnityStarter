using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Build.Pipeline.Editor;
using System.Security.Cryptography;
using System.Text;

namespace Build.Pipeline.Integrations.YooAsset3.Publication
{
    internal sealed class PublicationBuildLock : IDisposable
    {
        private const string LockDirectoryName = "YooAsset3Locks";
        private readonly FileStream[] streams;

        private PublicationBuildLock(FileStream[] streams)
        {
            this.streams = streams;
        }

        public static PublicationBuildLock Acquire(
            string projectRoot,
            string buildOutputRoot,
            string bundledFileRoot)
        {
            string normalizedProjectRoot = Path.GetFullPath(projectRoot);
            string lockRoot = GetLockRoot(normalizedProjectRoot);
            BuildPathPolicy.EnsureWin32MaxDirectoryPathBudget(
                lockRoot,
                "YooAsset publication lock root");
            PublicationSafety.ValidateNoPathRedirection(normalizedProjectRoot, lockRoot);
            Directory.CreateDirectory(lockRoot);
            PublicationSafety.ValidateNoPathRedirection(normalizedProjectRoot, lockRoot);

            string[] publicationRoots = new[]
                {
                    PublicationPaths.GetProviderStateRoot(normalizedProjectRoot),
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

                return new PublicationBuildLock(acquired.ToArray());
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
            PublicationSafety.ValidateNoPathRedirection(projectRoot, lockRoot);
            PublicationSafety.ValidateNoPathRedirection(projectRoot, lockPath);
            if (!PublicationSafety.IsStrictDescendant(lockRoot, lockPath) || Directory.Exists(lockPath))
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

    internal sealed class CommittedPublicationException : InvalidOperationException
    {
        public CommittedPublicationException(string message, string journalPath, Exception innerException)
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
    internal sealed class SimulatedTerminationException : Exception
    {
        public SimulatedTerminationException(string checkpoint)
            : base($"Simulated YooAsset publication termination at checkpoint '{checkpoint}'.")
        {
            Checkpoint = checkpoint ?? string.Empty;
        }

        public string Checkpoint { get; }
    }
}
