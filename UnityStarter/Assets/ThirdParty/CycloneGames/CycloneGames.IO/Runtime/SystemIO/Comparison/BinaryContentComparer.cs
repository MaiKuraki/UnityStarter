using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CycloneGames.IO
{
    public static class BinaryContentComparer
    {
        public static bool AreEqual(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
        {
            return first.SequenceEqual(second);
        }

        public static async Task<bool> AreEqualAsync(
            Stream first,
            Stream second,
            long byteCount,
            IProgress<FileTransferProgress> progress = null,
            CancellationToken cancellationToken = default,
            SystemFileStoreOptions options = null)
        {
            ValidateReadableStream(first, nameof(first));
            ValidateReadableStream(second, nameof(second));
            if (byteCount < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(byteCount));
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new FileTransferProgress(0L, byteCount));
            if (byteCount == 0L || ReferenceEquals(first, second))
            {
                progress?.Report(new FileTransferProgress(byteCount, byteCount));
                return true;
            }

            SystemFileStoreOptions effectiveOptions = options ?? SystemFileStoreOptions.Default;
            byte[] firstBuffer = PooledByteBuffer.Rent(effectiveOptions.BufferSize);
            byte[] secondBuffer = PooledByteBuffer.Rent(effectiveOptions.BufferSize);
            int maximumUsedLength = 0;
            try
            {
                long processedBytes = 0L;
                while (processedBytes < byteCount)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int requested = (int)Math.Min(effectiveOptions.BufferSize, byteCount - processedBytes);
                    int firstRead = await ReadExactlyAsync(
                        first,
                        firstBuffer,
                        requested,
                        cancellationToken).ConfigureAwait(false);
                    int secondRead = await ReadExactlyAsync(
                        second,
                        secondBuffer,
                        requested,
                        cancellationToken).ConfigureAwait(false);

                    if (firstRead != requested || secondRead != requested)
                    {
                        return false;
                    }

                    maximumUsedLength = Math.Max(maximumUsedLength, requested);
                    if (!firstBuffer.AsSpan(0, requested).SequenceEqual(secondBuffer.AsSpan(0, requested)))
                    {
                        return false;
                    }

                    processedBytes += requested;
                    progress?.Report(new FileTransferProgress(processedBytes, byteCount));
#if UNITY_WEBGL && !UNITY_EDITOR
                    await Task.Yield();
#endif
                }

                cancellationToken.ThrowIfCancellationRequested();
                return true;
            }
            finally
            {
                PooledByteBuffer.Return(
                    firstBuffer,
                    effectiveOptions.PooledBufferClearMode,
                    maximumUsedLength);
                PooledByteBuffer.Return(
                    secondBuffer,
                    effectiveOptions.PooledBufferClearMode,
                    maximumUsedLength);
            }
        }

        private static async Task<int> ReadExactlyAsync(
            Stream source,
            byte[] buffer,
            int count,
            CancellationToken cancellationToken)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int bytesRead = await source.ReadAsync(
                    buffer,
                    totalRead,
                    count - totalRead,
                    CancellationToken.None).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    break;
                }

                totalRead += bytesRead;
            }

            return totalRead;
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
