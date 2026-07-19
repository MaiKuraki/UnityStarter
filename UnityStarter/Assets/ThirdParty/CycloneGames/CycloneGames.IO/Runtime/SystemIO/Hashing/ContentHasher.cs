using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CycloneGames.Hash.Core;

namespace CycloneGames.IO
{
    public static class ContentHasher
    {
        private static readonly char[] HexCharacters = "0123456789abcdef".ToCharArray();

        public static int GetHashSize(FileHashAlgorithm algorithm)
        {
            switch (algorithm)
            {
                case FileHashAlgorithm.Md5:
                    return 16;
                case FileHashAlgorithm.Sha256:
                    return 32;
                case FileHashAlgorithm.XxHash64:
                    return 8;
                default:
                    throw new ArgumentOutOfRangeException(nameof(algorithm));
            }
        }

        public static int WriteHash(
            ReadOnlySpan<byte> content,
            FileHashAlgorithm algorithm,
            Span<byte> destination)
        {
            int hashSize = ValidateDestination(algorithm, destination.Length);
            if (algorithm == FileHashAlgorithm.XxHash64)
            {
                XxHash64 hasher = XxHash64.Create();
                hasher.Append(content);
                if (!hasher.TryWriteHash(destination))
                {
                    throw new CryptographicException("XxHash64 failed to write the hash destination.");
                }

                return hashSize;
            }

            using (var hasher = IncrementalHash.CreateHash(GetAlgorithmName(algorithm)))
            {
                hasher.AppendData(content);
                if (!hasher.TryGetHashAndReset(destination, out int bytesWritten)
                    || bytesWritten != hashSize)
                {
                    throw new CryptographicException("The hash provider returned an unexpected result length.");
                }

                return bytesWritten;
            }
        }

        public static int WriteHash(
            Stream source,
            FileHashAlgorithm algorithm,
            Span<byte> destination,
            SystemFileStoreOptions options = null)
        {
            ValidateReadableStream(source);
            int hashSize = ValidateDestination(algorithm, destination.Length);
            SystemFileStoreOptions effectiveOptions = options ?? SystemFileStoreOptions.Default;
            byte[] buffer = PooledByteBuffer.Rent(effectiveOptions.BufferSize);
            int maximumUsedLength = 0;
            try
            {
                if (algorithm == FileHashAlgorithm.XxHash64)
                {
                    XxHash64 hasher = XxHash64.Create();
                    int bytesRead;
                    while ((bytesRead = source.Read(buffer, 0, effectiveOptions.BufferSize)) > 0)
                    {
                        maximumUsedLength = Math.Max(maximumUsedLength, bytesRead);
                        hasher.Append(buffer, 0, bytesRead);
                    }

                    if (!hasher.TryWriteHash(destination))
                    {
                        throw new CryptographicException("XxHash64 failed to write the hash destination.");
                    }

                    return hashSize;
                }

                using (var hasher = IncrementalHash.CreateHash(GetAlgorithmName(algorithm)))
                {
                    int bytesRead;
                    while ((bytesRead = source.Read(buffer, 0, effectiveOptions.BufferSize)) > 0)
                    {
                        maximumUsedLength = Math.Max(maximumUsedLength, bytesRead);
                        hasher.AppendData(buffer, 0, bytesRead);
                    }

                    if (!hasher.TryGetHashAndReset(destination, out int bytesWritten)
                        || bytesWritten != hashSize)
                    {
                        throw new CryptographicException("The hash provider returned an unexpected result length.");
                    }

                    return bytesWritten;
                }
            }
            finally
            {
                PooledByteBuffer.Return(
                    buffer,
                    effectiveOptions.PooledBufferClearMode,
                    maximumUsedLength);
            }
        }

        public static async Task<int> WriteHashAsync(
            Stream source,
            FileHashAlgorithm algorithm,
            Memory<byte> destination,
            IProgress<FileTransferProgress> progress = null,
            CancellationToken cancellationToken = default,
            SystemFileStoreOptions options = null)
        {
            ValidateReadableStream(source);
            int hashSize = ValidateDestination(algorithm, destination.Length);
            cancellationToken.ThrowIfCancellationRequested();

            SystemFileStoreOptions effectiveOptions = options ?? SystemFileStoreOptions.Default;
            long totalBytes = TryGetRemainingLength(source);
            long processedBytes = 0L;
            progress?.Report(new FileTransferProgress(0L, totalBytes));

            byte[] buffer = PooledByteBuffer.Rent(effectiveOptions.BufferSize);
            int maximumUsedLength = 0;
            try
            {
                if (algorithm == FileHashAlgorithm.XxHash64)
                {
                    XxHash64 hasher = XxHash64.Create();
                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int bytesRead = await source.ReadAsync(
                            buffer,
                            0,
                            effectiveOptions.BufferSize,
                            CancellationToken.None).ConfigureAwait(false);
                        if (bytesRead == 0)
                        {
                            break;
                        }

                        maximumUsedLength = Math.Max(maximumUsedLength, bytesRead);
                        hasher.Append(buffer, 0, bytesRead);
                        processedBytes += bytesRead;
                        progress?.Report(new FileTransferProgress(processedBytes, totalBytes));
#if UNITY_WEBGL && !UNITY_EDITOR
                        await Task.Yield();
#endif
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    if (!hasher.TryWriteHash(destination.Span))
                    {
                        throw new CryptographicException("XxHash64 failed to write the hash destination.");
                    }

                    progress?.Report(new FileTransferProgress(processedBytes, processedBytes));
                    return hashSize;
                }

                using (var hasher = IncrementalHash.CreateHash(GetAlgorithmName(algorithm)))
                {
                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int bytesRead = await source.ReadAsync(
                            buffer,
                            0,
                            effectiveOptions.BufferSize,
                            CancellationToken.None).ConfigureAwait(false);
                        if (bytesRead == 0)
                        {
                            break;
                        }

                        maximumUsedLength = Math.Max(maximumUsedLength, bytesRead);
                        hasher.AppendData(buffer, 0, bytesRead);
                        processedBytes += bytesRead;
                        progress?.Report(new FileTransferProgress(processedBytes, totalBytes));
#if UNITY_WEBGL && !UNITY_EDITOR
                        await Task.Yield();
#endif
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    if (!hasher.TryGetHashAndReset(destination.Span, out int bytesWritten)
                        || bytesWritten != hashSize)
                    {
                        throw new CryptographicException("The hash provider returned an unexpected result length.");
                    }

                    progress?.Report(new FileTransferProgress(processedBytes, processedBytes));
                    return bytesWritten;
                }
            }
            finally
            {
                PooledByteBuffer.Return(
                    buffer,
                    effectiveOptions.PooledBufferClearMode,
                    maximumUsedLength);
            }
        }

        public static string ComputeHex(ReadOnlySpan<byte> content, FileHashAlgorithm algorithm)
        {
            int hashSize = GetHashSize(algorithm);
            Span<byte> hash = stackalloc byte[hashSize];
            WriteHash(content, algorithm, hash);
            return ToHex(hash);
        }

        public static string ToHex(ReadOnlySpan<byte> hash)
        {
            if (hash.IsEmpty)
            {
                return string.Empty;
            }

            Span<char> characters = hash.Length <= 64
                ? stackalloc char[hash.Length * 2]
                : new char[hash.Length * 2];
            for (int i = 0; i < hash.Length; i++)
            {
                byte value = hash[i];
                characters[i * 2] = HexCharacters[value >> 4];
                characters[(i * 2) + 1] = HexCharacters[value & 0x0F];
            }

            return new string(characters);
        }

        private static HashAlgorithmName GetAlgorithmName(FileHashAlgorithm algorithm)
        {
            switch (algorithm)
            {
                case FileHashAlgorithm.Md5:
                    return HashAlgorithmName.MD5;
                case FileHashAlgorithm.Sha256:
                    return HashAlgorithmName.SHA256;
                default:
                    throw new ArgumentOutOfRangeException(nameof(algorithm));
            }
        }

        private static int ValidateDestination(FileHashAlgorithm algorithm, int destinationLength)
        {
            int hashSize = GetHashSize(algorithm);
            if (destinationLength < hashSize)
            {
                throw new ArgumentException(
                    $"Hash destination must contain at least {hashSize} bytes.",
                    "destination");
            }

            return hashSize;
        }

        private static void ValidateReadableStream(Stream source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (!source.CanRead)
            {
                throw new ArgumentException("Source stream must be readable.", nameof(source));
            }
        }

        private static long TryGetRemainingLength(Stream source)
        {
            if (!source.CanSeek)
            {
                return FileTransferProgress.UNKNOWN_TOTAL_BYTES;
            }

            try
            {
                return Math.Max(0L, source.Length - source.Position);
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
    }
}
