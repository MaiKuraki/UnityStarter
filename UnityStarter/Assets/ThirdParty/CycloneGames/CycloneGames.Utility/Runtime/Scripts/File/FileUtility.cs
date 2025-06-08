using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CycloneGames.Logger;
using UnityEngine; // For Application.platform and platform-specific defines

namespace CycloneGames.Utility.Runtime
{
    public enum HashAlgorithmType
    {
        MD5,
        SHA256
    }

    public static class FileUtility
    {
        private const string DEBUG_FLAG = "[FileUtility]";

#if UNITY_IOS || UNITY_ANDROID
        private const int ReadBufferSize = 81920;
#elif UNITY_WEBGL
        private const int ReadBufferSize = 131072;
#else
        private const int ReadBufferSize = 65536;
#endif
        private const long LargeFileThreshold = 10 * 1024 * 1024;

        private static readonly ThreadLocal<IncrementalHash> ThreadLocalIncrementalMD5 =
            new ThreadLocal<IncrementalHash>(() => IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.MD5));
        private static readonly ThreadLocal<IncrementalHash> ThreadLocalIncrementalSHA256 =
            new ThreadLocal<IncrementalHash>(() => IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256));

        private static byte[] GetReadBuffer() => ArrayPool<byte>.Shared.Rent(ReadBufferSize);
        private static void ReturnReadBuffer(byte[] buffer) => ArrayPool<byte>.Shared.Return(buffer);

        public static int GetHashSizeInBytes(HashAlgorithmType algorithmType)
        {
            switch (algorithmType)
            {
                case HashAlgorithmType.MD5: return 16;
                case HashAlgorithmType.SHA256: return 32;
                default: throw new ArgumentOutOfRangeException(nameof(algorithmType));
            }
        }

        private static IncrementalHash GetIncrementalHashAlgorithm(HashAlgorithmType type)
        {
            switch (type)
            {
                case HashAlgorithmType.MD5: return ThreadLocalIncrementalMD5.Value;
                case HashAlgorithmType.SHA256: return ThreadLocalIncrementalSHA256.Value;
                default: throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }

        public static string ToHexString(ReadOnlySpan<byte> hashBytes)
        {
            if (hashBytes.IsEmpty) return string.Empty;
            var sb = new StringBuilder(hashBytes.Length * 2);
            foreach (byte b in hashBytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        public static async Task<bool> AreFilesEqualAsync(string filePath1, string filePath2,
            HashAlgorithmType algorithm = HashAlgorithmType.SHA256, CancellationToken cancellationToken = default)
        {
#if UNITY_WEBGL
            if (!filePath1.Contains(Application.persistentDataPath) || !filePath2.Contains(Application.persistentDataPath))
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Using AreFilesEqualAsync with direct file paths on WebGL for non-persistentDataPath is unreliable.");
            }
#endif
            var sw = Stopwatch.StartNew();
            try
            {
                if (string.Equals(filePath1, filePath2, StringComparison.OrdinalIgnoreCase)) return true;

                FileInfo fileInfo1, fileInfo2;
                try
                {
                    if (!File.Exists(filePath1)) { CLogger.LogDebug($"{DEBUG_FLAG} File does not exist: '{filePath1}'"); return false; }
                    if (!File.Exists(filePath2)) { CLogger.LogDebug($"{DEBUG_FLAG} File does not exist: '{filePath2}'"); return false; }
                    fileInfo1 = new FileInfo(filePath1);
                    fileInfo2 = new FileInfo(filePath2);
                }
                catch (Exception ex) { CLogger.LogWarning($"{DEBUG_FLAG} Error getting file info: {ex.Message}"); return false; }

                if (fileInfo1.Length != fileInfo2.Length) return false;
                if (fileInfo1.Length == 0) return true;

                cancellationToken.ThrowIfCancellationRequested();

                bool areEqual = fileInfo1.Length > LargeFileThreshold
                    ? await AreFilesEqualByChunksAsync(filePath1, filePath2, cancellationToken).ConfigureAwait(false)
                    : await AreFilesEqualByHashAsync(filePath1, filePath2, algorithm, cancellationToken).ConfigureAwait(false);

                return areEqual;
            }
            catch (OperationCanceledException) { CLogger.LogDebug($"{DEBUG_FLAG} File comparison cancelled."); throw; }
            catch (Exception ex) { CLogger.LogWarning($"{DEBUG_FLAG} Exception during file comparison: {ex.Message}"); return false; }
            finally { sw.Stop(); CLogger.LogDebug($"{DEBUG_FLAG} File comparison '{Path.GetFileName(filePath1)}' vs '{Path.GetFileName(filePath2)}' took {sw.ElapsedMilliseconds}ms."); }
        }

        private static async Task<bool> AreFilesEqualByHashAsync(string filePath1, string filePath2,
            HashAlgorithmType algorithm, CancellationToken cancellationToken)
        {
            int hashSize = GetHashSizeInBytes(algorithm);
            byte[] hash1Buffer = ArrayPool<byte>.Shared.Rent(hashSize); // Regular array
            byte[] hash2Buffer = ArrayPool<byte>.Shared.Rent(hashSize); // Regular array

            try
            {
                bool success1 = await ComputeFileHashAsync(filePath1, algorithm, hash1Buffer.AsMemory(0, hashSize), cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                bool success2 = await ComputeFileHashAsync(filePath2, algorithm, hash2Buffer.AsMemory(0, hashSize), cancellationToken).ConfigureAwait(false);

                if (!success1 || !success2)
                {
                    CLogger.LogWarning($"{DEBUG_FLAG} Hash computation failed for one or both files.");
                    return false;
                }
                // Call a synchronous helper that takes arrays and length.
                // This helper will create Spans internally, locally to its synchronous scope.
                return CompareHashBuffers(hash1Buffer, hash2Buffer, hashSize);
            }
            catch (OperationCanceledException) { CLogger.LogDebug($"{DEBUG_FLAG} Hash comparison cancelled."); throw; }
            catch (Exception ex) { CLogger.LogWarning($"{DEBUG_FLAG} Error comparing files by hash: {ex.Message}"); return false; }
            finally
            {
                ArrayPool<byte>.Shared.Return(hash1Buffer);
                ArrayPool<byte>.Shared.Return(hash2Buffer);
            }
        }

        private static async Task<bool> AreFilesEqualByChunksAsync(string filePath1, string filePath2, CancellationToken cancellationToken)
        {
            byte[] buffer1 = GetReadBuffer();
            byte[] buffer2 = GetReadBuffer();
            try
            {
                using (var fs1 = new FileStream(filePath1, FileMode.Open, FileAccess.Read, FileShare.Read, ReadBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
                using (var fs2 = new FileStream(filePath2, FileMode.Open, FileAccess.Read, FileShare.Read, ReadBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    int bytesRead1;
                    while ((bytesRead1 = await fs1.ReadAsync(buffer1, 0, buffer1.Length, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int totalBytesReadFs2 = 0;
                        while (totalBytesReadFs2 < bytesRead1)
                        {
                            int bytesRead2 = await fs2.ReadAsync(buffer2, totalBytesReadFs2, bytesRead1 - totalBytesReadFs2, cancellationToken).ConfigureAwait(false);
                            if (bytesRead2 == 0) { CLogger.LogWarning($"{DEBUG_FLAG} Premature end of stream for '{filePath2}'."); return false; }
                            totalBytesReadFs2 += bytesRead2;
                            cancellationToken.ThrowIfCancellationRequested();
                        }
                        // CompareByteArrays is unsafe but synchronous.
                        if (!CompareByteArrays(buffer1, buffer2, bytesRead1)) return false;
                    }
                    return true;
                }
            }
            catch (OperationCanceledException) { CLogger.LogDebug($"{DEBUG_FLAG} Chunk comparison cancelled."); throw; }
            catch (Exception ex) { CLogger.LogWarning($"{DEBUG_FLAG} Error comparing files by chunks: {ex.Message}"); return false; }
            finally { ReturnReadBuffer(buffer1); ReturnReadBuffer(buffer2); }
        }

        // ComputeFileHashAsync takes Memory<byte>, which is not a ref struct.
        // Internally, it passes hashBuffer.Span to TryGetHashAndReset, but this Span is temporary
        // and does not persist across any await within ComputeFileHashAsync itself.
        public static async Task<bool> ComputeFileHashAsync(string filePath, HashAlgorithmType algorithmType,
            Memory<byte> hashBuffer, CancellationToken cancellationToken)
        {
#if UNITY_WEBGL
            if (!filePath.Contains(Application.persistentDataPath)) { CLogger.LogWarning($"{DEBUG_FLAG} ComputeFileHashAsync on WebGL for non-persistentDataPath is unreliable."); }
#endif
            if (hashBuffer.Length < GetHashSizeInBytes(algorithmType)) { CLogger.LogError($"{DEBUG_FLAG} Hash buffer too small."); return false; }

            var fileReadBuffer = GetReadBuffer();
            try
            {
                IncrementalHash incrementalHasher = GetIncrementalHashAlgorithm(algorithmType);
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, ReadBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    int bytesRead;
                    while ((bytesRead = await stream.ReadAsync(fileReadBuffer, 0, fileReadBuffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        incrementalHasher.AppendData(fileReadBuffer, 0, bytesRead);
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }
                return incrementalHasher.TryGetHashAndReset(hashBuffer.Span, out _);
            }
            catch (OperationCanceledException) { CLogger.LogDebug($"{DEBUG_FLAG} Hash computation cancelled for '{filePath}'."); throw; }
            catch (Exception ex) { CLogger.LogError($"{DEBUG_FLAG} Error computing hash for {filePath}: {ex.Message}"); return false; }
            finally { ReturnReadBuffer(fileReadBuffer); }
        }

        public static async Task<bool> AreStreamsEqualAsync(Stream stream1, Stream stream2,
            long length1, long length2, HashAlgorithmType algorithm = HashAlgorithmType.SHA256, CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                if (stream1 == null || stream2 == null) return stream1 == stream2;
                if (!stream1.CanRead || !stream2.CanRead) { CLogger.LogWarning($"{DEBUG_FLAG} Stream not readable."); return false; }
                if (length1 != length2) return false;
                if (length1 == 0) return true;

                cancellationToken.ThrowIfCancellationRequested();

                bool areEqual = length1 > LargeFileThreshold
                    ? await AreStreamsEqualByChunksAsync(stream1, stream2, length1, cancellationToken).ConfigureAwait(false)
                    : await AreStreamsEqualByHashAsync(stream1, stream2, algorithm, cancellationToken).ConfigureAwait(false);

                return areEqual;
            }
            catch (OperationCanceledException) { CLogger.LogDebug($"{DEBUG_FLAG} Stream comparison cancelled."); throw; }
            catch (Exception ex) { CLogger.LogWarning($"{DEBUG_FLAG} Exception during stream comparison: {ex.Message}"); return false; }
            finally { sw.Stop(); CLogger.LogDebug($"{DEBUG_FLAG} Stream comparison took {sw.ElapsedMilliseconds}ms."); }
        }

        private static async Task<bool> AreStreamsEqualByHashAsync(Stream stream1, Stream stream2,
            HashAlgorithmType algorithm, CancellationToken cancellationToken)
        {
            int hashSize = GetHashSizeInBytes(algorithm);
            byte[] hash1Buffer = ArrayPool<byte>.Shared.Rent(hashSize); // Regular array
            byte[] hash2Buffer = ArrayPool<byte>.Shared.Rent(hashSize); // Regular array

            try
            {
                bool success1 = await ComputeStreamHashAsync(stream1, algorithm, hash1Buffer.AsMemory(0, hashSize), cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                bool success2 = await ComputeStreamHashAsync(stream2, algorithm, hash2Buffer.AsMemory(0, hashSize), cancellationToken).ConfigureAwait(false);

                if (!success1 || !success2) { CLogger.LogWarning($"{DEBUG_FLAG} Stream hash computation failed."); return false; }
                // Call synchronous helper that takes arrays.
                return CompareHashBuffers(hash1Buffer, hash2Buffer, hashSize);
            }
            catch (Exception ex) { CLogger.LogWarning($"{DEBUG_FLAG} Error comparing streams by hash: {ex.Message}"); return false; }
            finally
            {
                ArrayPool<byte>.Shared.Return(hash1Buffer);
                ArrayPool<byte>.Shared.Return(hash2Buffer);
            }
        }

        private static async Task<bool> AreStreamsEqualByChunksAsync(Stream stream1, Stream stream2, long streamLength, CancellationToken cancellationToken)
        {
            byte[] buffer1 = GetReadBuffer();
            byte[] buffer2 = GetReadBuffer();
            long totalBytesCompared = 0;
            try
            {
                int bytesRead1;
                while (totalBytesCompared < streamLength && (bytesRead1 = await stream1.ReadAsync(buffer1, 0, buffer1.Length, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int totalBytesReadFs2 = 0;
                    while (totalBytesReadFs2 < bytesRead1)
                    {
                        int bytesRead2 = await stream2.ReadAsync(buffer2, totalBytesReadFs2, bytesRead1 - totalBytesReadFs2, cancellationToken).ConfigureAwait(false);
                        if (bytesRead2 == 0) { CLogger.LogWarning($"{DEBUG_FLAG} Premature end of stream2 in chunk compare."); return false; }
                        totalBytesReadFs2 += bytesRead2;
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    if (!CompareByteArrays(buffer1, buffer2, bytesRead1)) return false;
                    totalBytesCompared += bytesRead1;
                }
                return totalBytesCompared == streamLength;
            }
            catch (Exception ex) { CLogger.LogWarning($"{DEBUG_FLAG} Error comparing streams by chunks: {ex.Message}"); return false; }
            finally { ReturnReadBuffer(buffer1); ReturnReadBuffer(buffer2); }
        }

        public static async Task<bool> ComputeStreamHashAsync(Stream stream, HashAlgorithmType algorithmType,
            Memory<byte> hashBuffer, CancellationToken cancellationToken)
        {
            if (stream == null || !stream.CanRead) { CLogger.LogError($"{DEBUG_FLAG} Stream null or not readable."); return false; }
            if (hashBuffer.Length < GetHashSizeInBytes(algorithmType)) { CLogger.LogError($"{DEBUG_FLAG} Hash buffer too small."); return false; }

            var readBuffer = GetReadBuffer();
            try
            {
                IncrementalHash incrementalHasher = GetIncrementalHashAlgorithm(algorithmType);
                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(readBuffer, 0, readBuffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    incrementalHasher.AppendData(readBuffer, 0, bytesRead);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                return incrementalHasher.TryGetHashAndReset(hashBuffer.Span, out _);
            }
            catch (OperationCanceledException) { CLogger.LogDebug($"{DEBUG_FLAG} Stream hash computation cancelled."); throw; }
            catch (Exception ex) { CLogger.LogError($"{DEBUG_FLAG} Error computing stream hash: {ex.Message}"); return false; }
            finally { ReturnReadBuffer(readBuffer); }
        }

        public static bool AreByteArraysEqualByHash(ReadOnlySpan<byte> content1, ReadOnlySpan<byte> content2, HashAlgorithmType algorithmType)
        {
            if (content1.Length != content2.Length) return false;
            if (content1.IsEmpty) return true;

            int hashSize = GetHashSizeInBytes(algorithmType);
            Span<byte> hash1Bytes = stackalloc byte[hashSize];
            Span<byte> hash2Bytes = stackalloc byte[hashSize];

            IncrementalHash hasher = GetIncrementalHashAlgorithm(algorithmType);
            hasher.AppendData(content1);
            hasher.TryGetHashAndReset(hash1Bytes, out _);

            hasher.AppendData(content2);
            hasher.TryGetHashAndReset(hash2Bytes, out _);

            return CompareByteSpans(hash1Bytes, hash2Bytes); // CompareByteSpans takes ReadOnlySpan
        }

        public static async Task<bool> AreByteArraysEqualByHashAsync(byte[] content1, byte[] content2, HashAlgorithmType algorithmType, CancellationToken cancellationToken = default)
        {
            if (content1 == null || content2 == null) return content1 == content2;
            if (content1.Length != content2.Length) return false;
            if (content1.Length == 0) return true;

            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return AreByteArraysEqualByHash(content1.AsSpan(), content2.AsSpan(), algorithmType);
            }, cancellationToken);
        }

        public static async Task CopyFileWithComparisonAsync(string sourceFilePath, string destinationFilePath,
            HashAlgorithmType comparisonAlgorithm = HashAlgorithmType.SHA256, CancellationToken cancellationToken = default)
        {
#if UNITY_WEBGL
            CLogger.LogWarning($"{DEBUG_FLAG} CopyFileWithComparisonAsync is generally not supported on WebGL.");
#endif
            var sw = Stopwatch.StartNew();
            try
            {
                if (!File.Exists(sourceFilePath)) { CLogger.LogError($"{DEBUG_FLAG} Source file does not exist: {sourceFilePath}"); return; }

                string directoryPath = Path.GetDirectoryName(destinationFilePath);
                if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                bool shouldCopy = true;
                if (File.Exists(destinationFilePath))
                {
                    if (await AreFilesEqualAsync(sourceFilePath, destinationFilePath, comparisonAlgorithm, cancellationToken).ConfigureAwait(false))
                    {
                        shouldCopy = false;
                    }
                    else { File.Delete(destinationFilePath); }
                }

                if (!shouldCopy) { CLogger.LogInfo($"{DEBUG_FLAG} Files identical, copy skipped."); return; }
                cancellationToken.ThrowIfCancellationRequested();

                var buffer = GetReadBuffer();
                try
                {
                    using (var sourceStream = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, ReadBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
                    using (var destinationStream = new FileStream(destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None, ReadBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
                    {
                        int bytesRead;
                        while ((bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                        {
                            await destinationStream.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
                            cancellationToken.ThrowIfCancellationRequested();
                        }
                    }
                    CLogger.LogInfo($"{DEBUG_FLAG} File copied: {sourceFilePath} to {destinationFilePath}.");
                }
                finally { ReturnReadBuffer(buffer); }
            }
            catch (OperationCanceledException)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} File copy cancelled: {sourceFilePath}");
                if (File.Exists(destinationFilePath)) { try { File.Delete(destinationFilePath); } catch (Exception exDel) { CLogger.LogWarning($"{DEBUG_FLAG} Could not delete partial dest file: {exDel.Message}"); } }
                throw;
            }
            catch (Exception ex) { CLogger.LogError($"{DEBUG_FLAG} Error during file copy: {ex.Message}"); }
            finally { sw.Stop(); CLogger.LogDebug($"{DEBUG_FLAG} File copy operation took {sw.ElapsedMilliseconds}ms."); }
        }

        // --- Byte Array and Span Comparison Utilities ---

        // New synchronous helper to compare hash buffers (arrays)
        // This method creates Spans locally, avoiding their presence in async state machines.
        private static bool CompareHashBuffers(byte[] buffer1, byte[] buffer2, int length)
        {
            if (buffer1 == null || buffer2 == null) return buffer1 == buffer2; // Should not happen if hash computation was successful
            // Spans are created here, within a synchronous context.
            ReadOnlySpan<byte> span1 = buffer1.AsSpan(0, length);
            ReadOnlySpan<byte> span2 = buffer2.AsSpan(0, length);
            return span1.SequenceEqual(span2);
        }

        // Original CompareByteSpans - used by AreByteArraysEqualByHash (synchronous)
        private static bool CompareByteSpans(ReadOnlySpan<byte> span1, ReadOnlySpan<byte> span2)
        {
            return span1.SequenceEqual(span2);
        }

        /// <summary>
        /// Compares two byte arrays for equality up to a specified length.
        /// Requires 'Allow unsafe code' in Unity project settings. This method is synchronous.
        /// </summary>
        private static unsafe bool CompareByteArrays(byte[] array1, byte[] array2, int lengthToCompare = -1)
        {
            if (array1 == array2) return true;
            if (array1 == null || array2 == null) return false;

            int len1 = array1.Length;
            int len2 = array2.Length;

            if (lengthToCompare == -1)
            {
                if (len1 != len2) return false;
                lengthToCompare = len1;
            }

            if (lengthToCompare == 0) return true;
            if (len1 < lengthToCompare || len2 < lengthToCompare) return false;

            fixed (byte* p1Fixed = array1, p2Fixed = array2)
            {
                byte* p1 = p1Fixed;
                byte* p2 = p2Fixed;
                int n = lengthToCompare / 8;
                for (int i = 0; i < n; i++)
                {
                    if (*(long*)p1 != *(long*)p2) return false;
                    p1 += 8; p2 += 8;
                }
                int remainder = lengthToCompare % 8;
                while (remainder > 0)
                {
                    if (*p1 != *p2) return false;
                    p1++; p2++; remainder--;
                }
            }
            return true;
        }
    }
}