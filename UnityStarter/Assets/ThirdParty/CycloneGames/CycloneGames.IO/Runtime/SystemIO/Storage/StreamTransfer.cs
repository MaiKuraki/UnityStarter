using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CycloneGames.IO
{
    internal static class StreamTransfer
    {
        internal static async Task<long> CopyAsync(
            Stream source,
            Stream destination,
            SystemFileStoreOptions options,
            CancellationToken cancellationToken)
        {
            byte[] buffer = PooledByteBuffer.Rent(options.BufferSize);
            int maximumUsedLength = 0;
            long processedBytes = 0L;
            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int bytesRead = await source.ReadAsync(
                        buffer,
                        0,
                        options.BufferSize,
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
#if UNITY_WEBGL && !UNITY_EDITOR
                    await Task.Yield();
#endif
                }

                cancellationToken.ThrowIfCancellationRequested();
                return processedBytes;
            }
            finally
            {
                PooledByteBuffer.Return(
                    buffer,
                    options.PooledBufferClearMode,
                    maximumUsedLength);
            }
        }
    }
}
