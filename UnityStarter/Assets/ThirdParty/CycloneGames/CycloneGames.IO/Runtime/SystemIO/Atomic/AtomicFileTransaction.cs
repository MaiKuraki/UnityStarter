using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CycloneGames.IO
{
    internal sealed class AtomicFileTransaction
    {
        private const int FILE_STREAM_BUFFER_SIZE = 4096;

        private readonly SystemFileStoreOptions _options;
        private readonly IAtomicFileOperations _fileOperations;

        internal AtomicFileTransaction(
            SystemFileStoreOptions options,
            IAtomicFileOperations fileOperations)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _fileOperations = fileOperations ?? throw new ArgumentNullException(nameof(fileOperations));
        }

        internal void WriteBytes(string destinationPath, byte[] content)
        {
            ValidatePath(destinationPath, nameof(destinationPath));
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            destinationPath = Path.GetFullPath(destinationPath);
            EnsureParentDirectory(destinationPath);
            string temporaryPath = CreateTemporaryPath(destinationPath);
            try
            {
                using (var destination = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    FILE_STREAM_BUFFER_SIZE,
                    FileOptions.SequentialScan))
                {
                    destination.Write(content, 0, content.Length);
                    FlushDurably(destination);
                }

                Commit(temporaryPath, destinationPath);
            }
            catch
            {
                TryDelete(temporaryPath);
                throw;
            }
        }

        internal async Task WriteBytesAsync(
            string destinationPath,
            byte[] content,
            CancellationToken cancellationToken)
        {
            ValidatePath(destinationPath, nameof(destinationPath));
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            cancellationToken.ThrowIfCancellationRequested();
            destinationPath = Path.GetFullPath(destinationPath);
            EnsureParentDirectory(destinationPath);
            string temporaryPath = CreateTemporaryPath(destinationPath);
            try
            {
                using (var destination = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    FILE_STREAM_BUFFER_SIZE,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    int offset = 0;
                    while (offset < content.Length)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int count = Math.Min(_options.BufferSize, content.Length - offset);
                        await destination.WriteAsync(
                            content,
                            offset,
                            count,
                            CancellationToken.None).ConfigureAwait(false);
                        offset += count;
#if UNITY_WEBGL && !UNITY_EDITOR
                        await Task.Yield();
#endif
                    }

                    FlushDurably(destination);
                }

                Commit(temporaryPath, destinationPath, cancellationToken);
            }
            catch
            {
                TryDelete(temporaryPath);
                throw;
            }
        }

        internal async Task<long> WriteStreamAsync(
            string destinationPath,
            Stream source,
            IProgress<FileTransferProgress> progress,
            CancellationToken cancellationToken)
        {
            ValidatePath(destinationPath, nameof(destinationPath));
            ValidateReadableStream(source, nameof(source));
            cancellationToken.ThrowIfCancellationRequested();
            destinationPath = Path.GetFullPath(destinationPath);
            EnsureParentDirectory(destinationPath);

            string temporaryPath = CreateTemporaryPath(destinationPath);
            byte[] buffer = PooledByteBuffer.Rent(_options.BufferSize);
            int maximumUsedLength = 0;
            try
            {
                long totalBytes = TryGetRemainingLength(source);
                long processedBytes = 0L;
                progress?.Report(new FileTransferProgress(0L, totalBytes));

                using (var destination = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    FILE_STREAM_BUFFER_SIZE,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int bytesRead = await source.ReadAsync(
                            buffer,
                            0,
                            _options.BufferSize,
                            CancellationToken.None).ConfigureAwait(false);
                        if (bytesRead == 0)
                        {
                            break;
                        }

                        maximumUsedLength = Math.Max(maximumUsedLength, bytesRead);
                        cancellationToken.ThrowIfCancellationRequested();
                        await destination.WriteAsync(
                            buffer,
                            0,
                            bytesRead,
                            CancellationToken.None).ConfigureAwait(false);
                        processedBytes += bytesRead;
                        progress?.Report(new FileTransferProgress(processedBytes, totalBytes));
#if UNITY_WEBGL && !UNITY_EDITOR
                        await Task.Yield();
#endif
                    }

                    FlushDurably(destination);
                }

                progress?.Report(new FileTransferProgress(processedBytes, processedBytes));
                Commit(temporaryPath, destinationPath, cancellationToken);
                return processedBytes;
            }
            catch
            {
                TryDelete(temporaryPath);
                throw;
            }
            finally
            {
                PooledByteBuffer.Return(
                    buffer,
                    _options.PooledBufferClearMode,
                    maximumUsedLength);
            }
        }

        internal void Commit(string temporaryPath, string destinationPath)
        {
            using (PathCommitCoordinator.Acquire(destinationPath))
            {
                CommitExclusive(temporaryPath, destinationPath);
            }
        }

        internal void Commit(
            string temporaryPath,
            string destinationPath,
            CancellationToken cancellationToken)
        {
            using (PathCommitCoordinator.Acquire(destinationPath))
            {
                // Waiting for another writer is still before the commit point. Once this check
                // succeeds, replacement is intentionally non-cancellable and must report its
                // actual outcome instead of translating a successful commit into cancellation.
                cancellationToken.ThrowIfCancellationRequested();
                CommitExclusive(temporaryPath, destinationPath);
            }
        }

        private void CommitExclusive(string temporaryPath, string destinationPath)
        {
            if (!_fileOperations.Exists(destinationPath))
            {
                try
                {
                    _fileOperations.Move(temporaryPath, destinationPath);
                    return;
                }
                catch (IOException) when (_fileOperations.Exists(destinationPath))
                {
                    // Another writer committed first. Replace it without deleting either destination.
                }
            }

            try
            {
                _fileOperations.Replace(temporaryPath, destinationPath);
            }
            catch (PlatformNotSupportedException exception)
            {
                throw CreateUnsupportedReplacementException(destinationPath, exception);
            }
            catch (NotSupportedException exception)
            {
                throw CreateUnsupportedReplacementException(destinationPath, exception);
            }
        }

        private static PlatformNotSupportedException CreateUnsupportedReplacementException(
            string destinationPath,
            Exception innerException)
        {
            return new PlatformNotSupportedException(
                $"Atomic replacement is not supported for '{Path.GetFileName(destinationPath)}'.",
                innerException);
        }

        private static void FlushDurably(FileStream stream)
        {
            try
            {
                stream.Flush(true);
            }
            catch (PlatformNotSupportedException)
            {
                stream.Flush();
            }
            catch (NotSupportedException)
            {
                stream.Flush();
            }
        }

        private static string CreateTemporaryPath(string destinationPath)
        {
            string directoryPath = Path.GetDirectoryName(destinationPath);
            return Path.Combine(
                directoryPath,
                ".cyclone-" + Guid.NewGuid().ToString("N") + ".tmp");
        }

        private static void EnsureParentDirectory(string path)
        {
            string directoryPath = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
        }

        private void TryDelete(string path)
        {
            try
            {
                if (_fileOperations.Exists(path))
                {
                    _fileOperations.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (NotSupportedException)
            {
            }
        }

        private static long TryGetRemainingLength(Stream stream)
        {
            if (!stream.CanSeek)
            {
                return FileTransferProgress.UNKNOWN_TOTAL_BYTES;
            }

            try
            {
                return Math.Max(0L, stream.Length - stream.Position);
            }
            catch (IOException)
            {
                return FileTransferProgress.UNKNOWN_TOTAL_BYTES;
            }
            catch (NotSupportedException)
            {
                return FileTransferProgress.UNKNOWN_TOTAL_BYTES;
            }
        }

        private static void ValidatePath(string path, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Path cannot be null, empty, or whitespace.", parameterName);
            }
        }

        private static void ValidateReadableStream(Stream stream, string parameterName)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            if (!stream.CanRead)
            {
                throw new ArgumentException("Stream must be readable.", parameterName);
            }
        }
    }
}
