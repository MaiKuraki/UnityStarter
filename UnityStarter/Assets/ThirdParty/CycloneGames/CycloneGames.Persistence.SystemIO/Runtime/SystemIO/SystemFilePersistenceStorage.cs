using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CycloneGames.IO;

namespace CycloneGames.Persistence.SystemIO
{
    /// <summary>
    /// Binds one fully qualified file path to the asynchronous single-record persistence contract.
    /// Bounded reads and atomic replacement are delegated to CycloneGames.IO.
    /// </summary>
    public sealed class SystemFilePersistenceStorage : IPersistenceStorage
    {
        private readonly SystemFileStore _fileStore;

        public SystemFilePersistenceStorage(string absolutePath)
            : this(absolutePath, SystemFileStore.Default)
        {
        }

        internal SystemFilePersistenceStorage(
            string absolutePath,
            SystemFileStore fileStore)
        {
            EnsurePlatformSupported();
            _fileStore = fileStore ?? throw new ArgumentNullException(nameof(fileStore));

            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                throw new ArgumentException(
                    "A persistence file path is required.",
                    nameof(absolutePath));
            }

            if (!Path.IsPathFullyQualified(absolutePath))
            {
                throw new ArgumentException(
                    "The System.IO adapter requires a fully qualified path.",
                    nameof(absolutePath));
            }

            Location = Path.GetFullPath(absolutePath);
        }

        public string Location { get; }

        public static SystemFilePersistenceStorage CreateSandboxed(
            string trustedRootPath,
            string relativePath)
        {
            string normalizedRelativePath = FilePathSandbox.NormalizeRelativePath(relativePath);
            var sandbox = new FilePathSandbox(trustedRootPath);
            return new SystemFilePersistenceStorage(sandbox.Resolve(normalizedRelativePath));
        }

        public async Task<PersistenceStorageReadResult> ReadAsync(
            int maxByteCount,
            CancellationToken cancellationToken = default)
        {
            EnsurePlatformSupported();
            try
            {
                byte[] content = await _fileStore.ReadBytesAsync(
                    Location,
                    maxByteCount,
                    cancellationToken).ConfigureAwait(false);
                return PersistenceStorageReadResult.Found(content);
            }
            catch (Exception exception) when (IsMissing(exception))
            {
                return PersistenceStorageReadResult.Missing();
            }
        }

        public Task WriteAtomicallyAsync(
            byte[] content,
            CancellationToken cancellationToken = default)
        {
            EnsurePlatformSupported();
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            return _fileStore.WriteBytesAtomicallyAsync(
                Location,
                content,
                cancellationToken);
        }

        public Task DeleteAsync(CancellationToken cancellationToken = default)
        {
            EnsurePlatformSupported();
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                _fileStore.Delete(Location);
            }
            catch (Exception exception) when (IsMissing(exception))
            {
                // Delete is idempotent; an already absent entry is the requested state.
            }

            return Task.CompletedTask;
        }

        private static bool IsMissing(Exception exception)
        {
            return exception is FileNotFoundException
                || exception is DirectoryNotFoundException;
        }

        internal static void EnsurePlatformSupported()
        {
#if UNITY_5_3_OR_NEWER && !UNITY_EDITOR && !UNITY_STANDALONE && !UNITY_IOS && !UNITY_ANDROID && !UNITY_SERVER
            throw new PlatformNotSupportedException(
                "The System.IO persistence adapter is not enabled for this player platform. " +
                "Bind a platform save-data implementation to IPersistenceStorage instead.");
#endif
        }
    }
}
